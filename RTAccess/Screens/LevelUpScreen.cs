using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                                       // ServiceWindowsVM, ServiceWindowsType
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo;                          // CharacterInfoVM, CharInfoComponentType
using Kingmaker.Code.UI.MVVM.View.ServiceWindows.CharacterInfo;                        // CharInfoPageType
using Kingmaker.EntitySystem.Entities;                                                 // BaseUnitEntity
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers;              // UnitProgressionVM, UnitProgressionWindowState
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.CareerPath;   // CareerPathVM
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.RankEntry;    // CareerPathRankEntryVM, RankEntrySelectionVM, RankEntryState, RankFeatureState
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.RankEntry.Feature; // BaseRankEntryFeatureVM, RankEntrySelectionStatVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The in-game character LEVEL-UP flow as a mod-owned navigable screen. Unlike WOTR, RT level-up is not
    /// a wizard: it's a MODE of the Character Info window's Progression section — a live
    /// <see cref="UnitProgressionVM"/> (<c>UnitProgressionMode.LevelUp</c>) held on
    /// <see cref="CharacterInfoVM"/>. This screen mirrors that VM's two states
    /// (<see cref="UnitProgressionWindowState"/>): pick a career path to advance, then fill in that path's
    /// pending rank selections (talents / abilities / attribute increases), then commit.
    ///
    /// Entry is the game's own <c>PartyCharacterVM.LevelUp()</c> / the "Level Up" action on
    /// <see cref="CharacterInfoScreen"/> (raises <c>HandleOpenCharacterInfoPage(LevelProgression, unit)</c>),
    /// which opens this page and builds the VM. Exclusive at layer 26 so it sits on and suppresses the
    /// read-only <see cref="CharacterInfoScreen"/> (10) while leveling. All picks apply to the level-up
    /// manager's throwaway PreviewUnit; nothing touches the real unit until <see cref="CareerPathVM.Commit"/>.
    /// Escape backs out via the game's guarded close (a discard-confirm through <see cref="MessageBoxScreen"/>
    /// when picks are pending). See docs/plans/ranked-ascending-lamport.md.
    ///
    /// Graph-native: the page is declared fresh from the live VM every render (immediate mode), so making a
    /// pick updates the group / unlocks the next one in place with no rebuild bookkeeping and no focus churn —
    /// what the old signature gating hand-built, the identity differ now gives for free. The page KEYS carry
    /// the VM state + current career, so picking / changing a career re-keys the whole page and focus falls to
    /// that page's declared start (the first upgradeable career / the first rank being gained — never Rank 1
    /// on a high-level character). Tab topology mirrors the verified adapter screen: header text, the tree,
    /// then (in the progression state) the outstanding line, Commit, and Change-career as their own stops.
    ///
    /// The only mod-side state is cursor-adjacent (the SaveLoadScreen precedent): the per-group FOLD map —
    /// defaults are computed once per page (a tier holding an actionable career opens; the ranks being gained
    /// and their unmade choices open) and latched so a made pick doesn't snap its group shut under focus; the
    /// user's own Left/Right folds write the same map. Cleared whenever the page flips.
    /// </summary>
    public sealed class LevelUpScreen : Screen
    {
        public LevelUpScreen() { Wrap = true; }

        public override string Key => "ctx.levelup";
        public override int Layer => 26;
        public override bool Exclusive => true;
        public override string ScreenName => Loc.T("levelup.title");

        // Per-group fold state, keyed by the node key string (which carries the page — see PageKey). An
        // entry is written when its default is first computed (latching it for the page's lifetime) and
        // whenever the user folds/unfolds the group.
        private readonly Dictionary<string, bool> _fold = new Dictionary<string, bool>();

        // Selections whose lazy option list (FilteredGroupList) we already materialized via the VM's own
        // UpdateFeatures — it rebuilds the option VMs wholesale, so once per selection per page, never per
        // render (the one construction-time side effect the adapter proxies carried).
        private readonly HashSet<RankEntrySelectionVM> _materialized = new HashSet<RankEntrySelectionVM>();

        private string _page; // last-built page signature (state + career) — a flip resets the maps above

        public override void OnPush() { ResetPageState(); }
        public override void OnPop() { ResetPageState(); }

        private void ResetPageState()
        {
            _fold.Clear();
            _materialized.Clear();
            _page = null;
        }

        // Active only while the Character Info window is showing the Level Progression page with a live
        // level-up (not chargen) progression VM — i.e. exactly when the game surfaces the level-up flow.
        public override bool IsActive()
        {
            var sw = ServiceWindows();
            if (sw == null || sw.CurrentWindow != ServiceWindowsType.CharacterInfo) return false;
            var ci = sw.CharacterInfoVM?.Value;
            if (ci == null || ci.PageType.Value != CharInfoPageType.LevelProgression) return false;
            var vm = Vm(ci);
            return vm != null && !vm.IsCharGen;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Back());
        }

        // ---- resolution (Surface OR Space — the character sheet opens in both) ----

        private static ServiceWindowsVM ServiceWindows() => UiContexts.ServiceWindows();

        private static UnitProgressionVM Vm() => Vm(ServiceWindows()?.CharacterInfoVM?.Value);

        private static UnitProgressionVM Vm(CharacterInfoVM ci)
        {
            if (ci == null) return null;
            return ci.ComponentVMs.TryGetValue(CharInfoComponentType.Progression, out var rp)
                ? rp?.Value as UnitProgressionVM : null;
        }

        // Which page the VM is showing: the window state + the career being progressed. A change re-keys
        // the whole graph (the prefix carries this), so nothing survives the differ and focus falls to the
        // new page's start node.
        private static string PageSig(UnitProgressionVM vm)
        {
            var cc = vm.CurrentCareer.Value;
            return (int)vm.State.Value + ":" + (cc?.CareerPath?.AssetGuid ?? "none");
        }

        // ---- build (immediate mode) ----


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            // Page flip (career picked / changed / committed): drop the latched folds and the
            // materialization memo — the new page computes fresh defaults, like the adapter's rebuild did.
            var page = PageSig(vm);
            if (_page != page)
            {
                if (_page != null) { _fold.Clear(); _materialized.Clear(); }
                _page = page;
            }

            string k = "lvl:" + vm.GetHashCode() + ":" + page + ":";
            var unit = vm.Unit.Value;
            if (unit == null)
            {
                b.BeginStop("head").AddItem(ControlId.Structural(k + "none"),
                    GraphNodes.Text(() => Loc.T("levelup.none")));
                return;
            }

            var cc = vm.CurrentCareer.Value;
            if (vm.State.Value == UnitProgressionWindowState.CareerPathProgression && cc != null)
                BuildProgression(b, k, vm, cc);
            else
                BuildCareerList(b, k, vm, unit);
        }

        // State 1 — pick a career path to advance, GROUPED BY TIER (the game lays the tiers out spatially with
        // dependency lines; a per-tier group tree is the a11y equivalent). Each archetype is a button whose
        // label MIRRORS ITS CARD (CareerPathListItemCommonView): name + state + "rank cur of max" (shown only
        // while in-progress/finished, matching m_ProgressText) + recommended. An upgradeable one is enabled and
        // activates the game's SetCareerPath; a locked one is disabled (the button announces that) — the WHY
        // (prerequisites), the description, the stats/skills raised and the keystone/ultimate abilities all live
        // in the card's tooltip (Space), exactly as the game keeps them tooltip-only. The tier containing an
        // actionable archetype opens by default, and the first upgradeable archetype is the page's start node.
        private void BuildCareerList(GraphBuilder b, string k, UnitProgressionVM vm, BaseUnitEntity unit)
        {
            var paths = vm.AllCareerPaths.Where(c => c?.CareerPath != null).ToList();
            bool anyUp = paths.Any(c => Upgradeable(unit, c));
            b.BeginStop("head").AddItem(ControlId.Structural(k + "head"),
                GraphNodes.Text(() => anyUp ? Loc.T("levelup.pick_career") : Loc.T("levelup.none")));

            b.BeginStop("list");
            ControlId start = null;
            foreach (var tier in paths.Select(c => c.CareerPath.Tier).Distinct().OrderBy(t => (int)t))
            {
                var careers = paths.Where(c => c.CareerPath.Tier == tier)
                                   .OrderByDescending(c => Upgradeable(unit, c)).ToList(); // actionable first
                bool actionable = careers.Any(c => Upgradeable(unit, c));
                int tierNumber = (int)tier + 1;
                string fkey = k + "tier:" + (int)tier;
                var vt = GraphNodes.Group(() => Loc.T("levelup.tier", new { tier = tierNumber }));
                vt.OnExpand = () => _fold[fkey] = true;
                vt.OnCollapse = () => _fold[fkey] = false;
                b.BeginGroup(ControlId.Structural(fkey), vt, expanded: Fold(fkey, actionable));
                foreach (var cp in careers)
                {
                    var c = cp; // capture
                    var id = ControlId.Referenced(c, k + "career:" + c.CareerPath.AssetGuid);
                    b.AddItem(id, GraphNodes.Button(() => CareerLabel(c), () => c.SetCareerPath(),
                        () => Upgradeable(unit, c), () => c.CareerTooltip));
                    if (start == null && Upgradeable(unit, c)) start = id;
                }
                b.EndGroup();
            }
            // Land on the actionable archetype, not the header — nothing on this page reads as selected,
            // so a full re-key (or first open) falls through the differ to exactly this node.
            if (start != null) b.SetStart(start);
        }

        private static bool Upgradeable(BaseUnitEntity unit, CareerPathVM cp)
            => unit.Progression.CanUpgradePath(cp.CareerPath) || cp.IsInProgress;

        // Mirrors the archetype card: name, its icon-state as a word (completed / in progress — locked & available
        // are carried by the button's enabled/disabled announcement), the "rank cur of max" the card shows only
        // for started/finished paths, and the recommended flag. Everything else is the card's tooltip (Space).
        private static string CareerLabel(CareerPathVM cp)
        {
            var s = cp.Name;
            if (cp.IsFinished) s += ", " + Loc.T("levelup.state_completed");
            else if (cp.IsInProgress) s += ", " + Loc.T("levelup.state_in_progress");
            if (cp.IsInProgress || cp.IsFinished)
                s += ", " + Loc.T("levelup.rank_of", new { rank = cp.CurrentRank.Value, max = cp.MaxRank });
            if (cp.IsRecommended.Value) s += ", " + Loc.T("chargen.recommended");
            return s;
        }

        // State 2 — the chosen path as a per-rank OUTLINE (not just the pending picks). One collapsible group
        // per rank, labelled with its state (taken / gaining now / locked): inside are the rank's automatic
        // features (read-only) and its choices (nested groups of radio options). The ranks being gained open
        // by default (with their pending choices) so the decisions are immediately reachable, while the rest
        // of the path stays browsable for context; the first gaining rank is the page's start node. The
        // outstanding line + Commit read live off the VM.
        private void BuildProgression(GraphBuilder b, string k, UnitProgressionVM vm, CareerPathVM cp)
        {
            b.BeginStop("head").AddItem(ControlId.Structural(k + "head"),
                GraphNodes.Text(() => ProgressHeader(cp)));

            int actual = cp.Unit.Progression.GetPathRank(cp.CareerPath); // committed rank (before this level-up)
            var range = cp.GetCurrentLevelupRange();                     // ranks being added now (Min..Max, or -1)
            bool hasRange = range.Min > 0 && range.Max >= range.Min;

            b.BeginStop("list");
            ControlId start = null;
            foreach (var re in cp.RankEntries)
            {
                if (re == null || re.IsEmpty) continue;
                var entry = re; // capture
                int rank = entry.Rank;
                bool gaining = hasRange && rank >= range.Min && rank <= range.Max;
                bool taken = rank <= actual;

                string rkey = k + "rank:" + rank;
                var rid = ControlId.Referenced(entry, rkey);
                var rvt = GraphNodes.Group(() => RankLabel(entry, rank, taken, gaining));
                rvt.OnExpand = () => _fold[rkey] = true;
                rvt.OnCollapse = () => _fold[rkey] = false;
                // Open the active ranks so the choices are one step away; land on the first of them.
                b.BeginGroup(rid, rvt, expanded: Fold(rkey, gaining));
                if (gaining && start == null) start = rid;

                int fi = 0;
                foreach (var f in entry.Features)
                {
                    if (f == null) continue;
                    var feat = f; // capture
                    b.AddItem(ControlId.Referenced(feat, rkey + ":feat:" + fi++), RankFeature(feat));
                }

                int si = 0;
                foreach (var sel in entry.Selections)
                {
                    if (sel == null) continue;
                    var s = sel; // capture
                    string skey = rkey + ":sel:" + si++;
                    // Materialize the lazy option list ONCE via the VM's own UpdateFeatures (it rebuilds
                    // the option VMs — much too heavy for a per-render call).
                    if (_materialized.Add(s)) s.UpdateFeatures();
                    var svt = SelectionGroup(s);
                    svt.OnExpand = () => _fold[skey] = true;
                    svt.OnCollapse = () => _fold[skey] = false;
                    // Default open while the pick is pending — LATCHED, so making the pick doesn't snap
                    // the group shut under the focus that sits inside it.
                    b.BeginGroup(ControlId.Referenced(s, skey), svt,
                        expanded: Fold(skey, gaining && !s.SelectionMadeAndValid));
                    int oi = 0;
                    foreach (var opt in s.FilteredGroupList.OfType<BaseRankEntryFeatureVM>())
                    {
                        var o = opt; // capture
                        b.AddItem(ControlId.Referenced(o, skey + ":opt:" + oi++), RankOption(o));
                    }
                    b.EndGroup();
                }
                b.EndGroup();
            }
            // Land initial focus on the rank being gained (the actionable one), not Rank 1 — matters for a
            // higher-level character whose path has many earned ranks above the ones being added.
            if (start != null) b.SetStart(start);

            b.BeginStop("status").AddItem(ControlId.Structural(k + "status"),
                GraphNodes.Text(() => OutstandingText(cp)));
            b.BeginStop("commit").AddItem(ControlId.Structural(k + "commit"),
                GraphNodes.Button(() => Loc.T("levelup.commit"), () => Commit(cp), () => cp.CanCommit.Value));
            b.BeginStop("change").AddItem(ControlId.Structural(k + "change"),
                GraphNodes.Button(() => Loc.T("levelup.change_career"), () => vm.SetCareerPath(null)));
        }

        // A rank node's label reflects where it sits relative to this level-up: a rank being gained now (with
        // "choice needed" while it still has an unmade pick — read live, so it drops as picks are made), an
        // already-earned rank, or a not-yet-reachable one.
        private static string RankLabel(CareerPathRankEntryVM re, int rank, bool taken, bool gaining)
        {
            if (gaining)
            {
                bool pending = re.Selections.Any(s => s != null && !s.SelectionMadeAndValid);
                return Loc.T("levelup.rank_gaining", new { rank })
                    + (pending ? ", " + Loc.T("levelup.choice_needed") : "");
            }
            return Loc.T(taken ? "levelup.rank_taken" : "levelup.rank_locked", new { rank });
        }

        private static string ProgressHeader(CareerPathVM cp)
        {
            int rank = cp.Unit.Progression.GetPathRank(cp.CareerPath);
            return Loc.T("levelup.advancing", new { name = cp.Name, rank, max = cp.MaxRank });
        }

        private static string OutstandingText(CareerPathVM cp)
        {
            if (cp.CanCommit.Value) return Loc.T("levelup.ready");
            int count = cp.AvailableSelections.Count(s => !s.SelectionMadeAndValid);
            return Loc.T("levelup.outstanding", new { count });
        }

        // Commit via the game's own CareerPathVM.Commit (re-checks validity, applies the preview to the real
        // unit through CommitLevelUpGameCommand, then destroys the manager and refreshes → back to state 1).
        private static void Commit(CareerPathVM cp)
        {
            if (!cp.CanCommit.Value) return;
            cp.Commit();
            Tts.Speak(Loc.T("levelup.complete"));
        }

        // Escape: in the progression view, back out to the career list (the game pops a discard-confirm when
        // picks are pending — surfaced by MessageBoxScreen); in the career list, close the window.
        private void Back()
        {
            var vm = Vm();
            if (vm == null) return;
            if (vm.State.Value == UnitProgressionWindowState.CareerPathProgression)
                vm.SetCareerPath(null);
            else
                ServiceWindows()?.HandleCloseAll();
        }

        // The group's fold state: the user's explicit fold if any, else the default — computed ONCE and
        // latched (build-time parity with the adapter's Expand() calls), so a default that flips mid-page
        // (an unmade choice getting made) doesn't fold the group under focus.
        private bool Fold(string key, bool def)
        {
            bool v;
            if (_fold.TryGetValue(key, out v)) return v;
            _fold[key] = def;
            return def;
        }

        // ---- node factories (the ProxyRankSelection / ProxyRankOption / ProxyRankFeature contracts,
        // vtable-shaped; the VM-contract knowledge those proxies carried lives here now) ----

        /// <summary>One pending level-up choice — a <see cref="RankEntrySelectionVM"/> (a talent / ability /
        /// attribute pick from a career-path rank) as a collapsible group: the header reads the choice's
        /// prompt (<see cref="RankEntrySelectionVM.GetHintText"/>) plus its live state (not chosen / chosen:
        /// X / committed / choose earlier first). State reads LIVE off the VM reactives, so a made pick
        /// speaks the new state when the header is (re)visited or watched.</summary>
        private static NodeVtable SelectionGroup(RankEntrySelectionVM sel)
        {
            var vt = GraphNodes.Group(() => sel.GetHintText());
            vt.Announcements = new List<NodeAnnouncement>(vt.Announcements)
            {
                new NodeAnnouncement(() => SelectionState(sel), live: true, kind: AnnouncementKinds.Value),
            };
            return vt;
        }

        private static string SelectionState(RankEntrySelectionVM sel)
        {
            var name = sel.SelectedFeature.Value?.DisplayName ?? "";
            if (sel.EntryState.Value == RankEntryState.Committed) return Loc.T("levelup.committed", new { name });
            if (sel.SelectionMade) return Loc.T("levelup.chosen", new { name });
            if (sel.EntryState.Value == RankEntryState.WaitPreviousToSelect) return Loc.T("levelup.locked");
            return Loc.T("levelup.not_chosen");
        }

        /// <summary>One selectable option under a selection group — a feature/talent/ability, or (for
        /// <see cref="RankEntrySelectionStatVM"/>) an attribute/skill increase. Reads name + selected + (stat
        /// delta and/or recommended) + enabled; Enter selects it via the game's own <c>Select()</c> (which
        /// applies to the level-up preview and refreshes commit-ability) and re-announces "selected"
        /// synchronously; Space drills into the full write-up.</summary>
        private static NodeVtable RankOption(BaseRankEntryFeatureVM opt)
        {
            Func<bool> chosen = () => opt.FeatureState.Value == RankFeatureState.Selected
                || opt.FeatureState.Value == RankFeatureState.Committed;
            bool canSelect = opt.CanSelect(); // fresh per render (immediate mode)
            return new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => FeatureName(opt)),
                    GraphNodes.SelectedPart(chosen),
                    new NodeAnnouncement(() => OptionValue(opt), kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(() => opt.CanSelect() || chosen()),
                },
                SearchText = () => FeatureName(opt),
                // Picking flips the option in place — speak the new state synchronously (the
                // ReannounceOnActivate convention, as CharGenNodes.SelectionItem does it).
                StateText = canSelect ? (Func<string>)(() => chosen() ? Loc.T("state.selected") : null) : null,
                OnActivate = canSelect ? (Action)(() => opt.Select()) : null,
                OnTooltip = () => TooltipChooser.OpenTemplate(FeatureName(opt), opt.TooltipTemplate()),
                ActivateSound = canSelect
                    ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick
                    : null,
            };
        }

        /// <summary>A read-only feature node under a rank — an ability/talent/stat the rank grants
        /// AUTOMATICALLY (no choice to make). Reads its name (+ recommended); Space drills into the full
        /// write-up. Not activatable — the rank's state (taken / gaining / locked) is carried by the group
        /// label.</summary>
        private static NodeVtable RankFeature(BaseRankEntryFeatureVM f)
        {
            return new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => FeatureName(f)),
                    new NodeAnnouncement(() => f.IsRecommended ? Loc.T("chargen.recommended") : null,
                        kind: AnnouncementKinds.Value),
                },
                SearchText = () => FeatureName(f),
                OnTooltip = () => TooltipChooser.OpenTemplate(FeatureName(f), f.TooltipTemplate()),
            };
        }

        // Stat options name themselves by the attribute/skill they raise; everything else by DisplayName.
        private static string FeatureName(BaseRankEntryFeatureVM f)
            => (f is RankEntrySelectionStatVM st && !string.IsNullOrEmpty(st.StatDisplayName))
                ? st.StatDisplayName : f.DisplayName ?? "";

        private static string OptionValue(BaseRankEntryFeatureVM opt)
        {
            string s = null;
            if (opt is RankEntrySelectionStatVM st)
            {
                s = st.StatIncreaseLabel.Value; // "+10" — the per-rank gain the game shows on each option
                // Show the would-be result WHILE navigating (before the pick is staged). The game's own
                // SummaryStatIncreaseLabel only reflects the preview once THIS option is staged — before that
                // it reads "45 > 45" — so derive the target from current + increase instead. Worded "45 to 55"
                // (locale) so TTS doesn't read the raw ">" arrow as "greater than". (UnitStat is the WINDOW's
                // unit, not the preview, so the base doesn't drift as picks are staged.)
                int cur = st.UnitStat?.ModifiedValue ?? 0;
                int inc = ParseInc(st.StatIncreaseLabel.Value);
                if (inc != 0) s += ", " + Loc.T("levelup.stat_result", new { from = cur, to = cur + inc });
            }
            if (opt.IsRecommended)
                s = string.IsNullOrEmpty(s) ? Loc.T("chargen.recommended") : s + ", " + Loc.T("chargen.recommended");
            return s;
        }

        // Parse the per-rank increase label ("+10" / "-2") into a signed integer; tolerant of stray glyphs.
        private static int ParseInc(string label)
        {
            if (string.IsNullOrEmpty(label)) return 0;
            int sign = label.Contains("-") ? -1 : 1;
            var digits = new string(label.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? sign * n : 0;
        }
    }
}
