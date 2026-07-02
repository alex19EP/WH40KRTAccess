using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                                       // ServiceWindowsVM, ServiceWindowsType
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo;                          // CharacterInfoVM, CharInfoComponentType
using Kingmaker.Code.UI.MVVM.View.ServiceWindows.CharacterInfo;                        // CharInfoPageType
using Kingmaker.EntitySystem.Entities;                                                 // BaseUnitEntity
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers;              // UnitProgressionVM, UnitProgressionWindowState
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.CareerPath;   // CareerPathVM
using Kingmaker.UnitLogic.Progression.Paths;                                           // CareerPathTier
using RTAccess.UI;
using RTAccess.UI.Proxies;

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
    /// </summary>
    public sealed class LevelUpScreen : Screen
    {
        public LevelUpScreen() { Wrap = true; }

        public override string Key => "ctx.levelup";
        public override int Layer => 26;
        public override bool Exclusive => true;
        public override string ScreenName => Loc.T("levelup.title");

        private string _sig;

        public override void OnPush() { _sig = null; }
        public override void OnPop() { Clear(); _sig = null; }

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

        // Rebuild only when the structural state changes (career-list vs progression, which career, how many
        // pending selections). Within the progression view the option proxies read live VM state, so making a
        // selection updates the group and unlocks the next one without a rebuild (no focus churn).
        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            var sig = Sig(vm);
            if (sig == _sig) return;
            _sig = sig;
            Rebuild(vm);
            Navigation.Attach(this);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Back());
        }

        // ---- resolution (Surface OR Space — the character sheet opens in both) ----

        private static ServiceWindowsVM ServiceWindows()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.SurfaceVM?.StaticPartVM?.ServiceWindowsVM
                ?? rc?.SpaceVM?.StaticPartVM?.ServiceWindowsVM;
        }

        private static UnitProgressionVM Vm() => Vm(ServiceWindows()?.CharacterInfoVM?.Value);

        private static UnitProgressionVM Vm(CharacterInfoVM ci)
        {
            if (ci == null) return null;
            return ci.ComponentVMs.TryGetValue(CharInfoComponentType.Progression, out var rp)
                ? rp?.Value as UnitProgressionVM : null;
        }

        private static string Sig(UnitProgressionVM vm)
        {
            var cc = vm.CurrentCareer.Value;
            int sel = 0, rank = 0;
            if (cc != null)
            {
                try { sel = cc.AvailableSelections.Count(); } catch { }
                try { rank = cc.Unit.Progression.GetPathRank(cc.CareerPath); } catch { }
            }
            return (int)vm.State.Value + "|" + (cc?.CareerPath?.AssetGuid ?? "none") + "|" + sel + "|" + rank;
        }

        // ---- build ----

        private void Rebuild(UnitProgressionVM vm)
        {
            Clear();
            var unit = vm.Unit.Value;
            if (unit == null) { Add(new TextElement(() => Loc.T("levelup.none"))); return; }
            if (vm.State.Value == UnitProgressionWindowState.CareerPathProgression && vm.CurrentCareer.Value != null)
                BuildProgression(vm, vm.CurrentCareer.Value);
            else
                BuildCareerList(vm, unit);
        }

        // State 1 — pick a career path to advance, GROUPED BY TIER (the game lays the tiers out spatially with
        // dependency lines; a per-tier tree is the a11y equivalent). Each archetype is a ProxyActionButton whose
        // label MIRRORS ITS CARD (CareerPathListItemCommonView): name + state + "rank cur of max" (shown only
        // while in-progress/finished, matching m_ProgressText) + recommended. An upgradeable one is enabled and
        // activates the game's SetCareerPath; a locked one is disabled (the button announces that) — the WHY
        // (prerequisites), the description, the stats/skills raised and the keystone/ultimate abilities all live
        // in the card's tooltip (Space), exactly as the game keeps them tooltip-only. The tier containing an
        // actionable archetype auto-expands and takes initial focus.
        private void BuildCareerList(UnitProgressionVM vm, BaseUnitEntity unit)
        {
            var paths = vm.AllCareerPaths.Where(c => c?.CareerPath != null).ToList();
            bool anyUp = paths.Any(c => Upgradeable(unit, c));
            Add(new TextElement(() => anyUp ? Loc.T("levelup.pick_career") : Loc.T("levelup.none")));

            var tree = new TreeGroup(); // root; the tier sections are its children
            TreeGroup focusTier = null;
            UIElement focusItem = null;
            foreach (var tier in paths.Select(c => c.CareerPath.Tier).Distinct().OrderBy(t => (int)t))
            {
                var g = new TreeGroup(Loc.T("levelup.tier", new { tier = (int)tier + 1 }));
                bool actionable = false;
                foreach (var cp in paths.Where(c => c.CareerPath.Tier == tier)
                                        .OrderByDescending(c => Upgradeable(unit, c)))
                {
                    var c = cp;
                    var btn = new ProxyActionButton(() => CareerLabel(c), () => Upgradeable(unit, c),
                        () => c.SetCareerPath(), actionVerb: "select", tooltip: () => c.CareerTooltip);
                    g.Add(btn);
                    if (Upgradeable(unit, c)) { actionable = true; focusItem ??= btn; }
                }
                if (actionable) { g.Expand(); focusTier ??= g; }
                tree.Add(g);
            }
            if (focusTier != null)
            {
                tree.SetFocusedChild(focusTier);
                if (focusItem != null) focusTier.SetFocusedChild(focusItem);
            }
            Add(tree);
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

        // State 2 — the chosen path as a per-rank OUTLINE (not just the pending picks). A tree root holds one
        // collapsible node per rank, labelled with its state (taken / gaining now / locked): inside are the
        // rank's automatic features (read-only ProxyRankFeature) and its choices (ProxyRankSelection groups).
        // The ranks being gained now auto-expand (with their pending choices) so the decisions are immediately
        // reachable, while the rest of the path stays browsable for context. Outstanding line + Commit read
        // live off the VM.
        private void BuildProgression(UnitProgressionVM vm, CareerPathVM cp)
        {
            var unit = cp.Unit;
            Add(new TextElement(() => ProgressHeader(cp)));

            int actual = unit.Progression.GetPathRank(cp.CareerPath); // committed rank (before this level-up)
            var range = cp.GetCurrentLevelupRange();                  // ranks being added now (Min..Max, or -1)
            bool hasRange = range.Min > 0 && range.Max >= range.Min;

            var tree = new TreeGroup(); // unlabeled root; its children (rank nodes) are always exposed
            TreeGroup firstGaining = null;
            foreach (var re in cp.RankEntries)
            {
                if (re.IsEmpty) continue;
                int rank = re.Rank;
                bool gaining = hasRange && rank >= range.Min && rank <= range.Max;
                bool taken = rank <= actual;
                bool pendingChoice = gaining && re.Selections.Any(s => !s.SelectionMadeAndValid);

                var g = new TreeGroup(RankLabel(rank, taken, gaining, pendingChoice));
                foreach (var f in re.Features) g.Add(new ProxyRankFeature(f));
                foreach (var sel in re.Selections)
                {
                    var node = new ProxyRankSelection(sel);
                    g.Add(node);
                    if (gaining && !sel.SelectionMadeAndValid) node.Expand(); // surface pending options
                }
                if (gaining) { g.Expand(); firstGaining ??= g; } // open the active ranks so choices are one step away
                tree.Add(g);
            }
            // Land initial focus on the rank being gained (the actionable one), not Rank 1 — matters for a
            // higher-level character whose path has many earned ranks above the ones being added.
            if (firstGaining != null) tree.SetFocusedChild(firstGaining);
            Add(tree);

            Add(new TextElement(() => OutstandingText(cp)));
            Add(new ProxyActionButton(() => Loc.T("levelup.commit"), () => cp.CanCommit.Value, () => Commit(cp)));
            Add(new ProxyActionButton(() => Loc.T("levelup.change_career"), () => true, () => vm.SetCareerPath(null)));
        }

        // A rank node's label reflects where it sits relative to this level-up: a rank being gained now (with
        // "choice needed" when it still has an unmade pick), an already-earned rank, or a not-yet-reachable one.
        private static string RankLabel(int rank, bool taken, bool gaining, bool pendingChoice)
        {
            if (gaining)
                return Loc.T("levelup.rank_gaining", new { rank })
                    + (pendingChoice ? ", " + Loc.T("levelup.choice_needed") : "");
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
    }
}
