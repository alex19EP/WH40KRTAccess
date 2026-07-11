using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.CareerPath;   // CareerPathVM
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.RankEntry;    // RankEntrySelectionVM, RankEntryState, RankFeatureState
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.RankEntry.Feature; // BaseRankEntryFeatureVM, RankEntrySelectionStatVM
using RTAccess.UI.Graph;

namespace RTAccess.UI
{
    /// <summary>
    /// Node factories for the game's career-path rank machinery (<see cref="RankEntrySelectionVM"/> and
    /// its feature options) — the ProxyRankSelection / ProxyRankOption / ProxyRankFeature contracts,
    /// vtable-shaped. Shared by the character level-up flow (<see cref="RTAccess.Screens.LevelUpScreen"/>)
    /// and the ship Skills tab (<see cref="RTAccess.Screens.ShipCustomizationScreen"/>), which both walk a
    /// <c>CareerPathVM</c>'s RankEntries; the spoken contracts must not drift between the two.
    /// </summary>
    internal static class CareerNodes
    {
        /// <summary>One pending level-up choice — a <see cref="RankEntrySelectionVM"/> (a talent / ability /
        /// attribute pick from a career-path rank) as a collapsible group: the header reads the choice's
        /// prompt (<see cref="RankEntrySelectionVM.GetHintText"/>) plus its live state (not chosen / chosen:
        /// X / committed / choose earlier first). State reads LIVE off the VM reactives, so a made pick
        /// speaks the new state when the header is (re)visited or watched.</summary>
        public static NodeVtable SelectionGroup(RankEntrySelectionVM sel)
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

        /// <summary>A rank node's label, reflecting where it sits relative to this level-up: a rank being
        /// gained now (with "choice needed" while it still has an unmade pick — read live, so it drops as
        /// picks are made), an already-earned rank, or a not-yet-reachable one.</summary>
        public static string RankLabel(CareerPathRankEntryVM re, int rank, bool taken, bool gaining)
        {
            if (gaining)
            {
                bool pending = re.Selections.Any(s => s != null && !s.SelectionMadeAndValid);
                return Loc.T("levelup.rank_gaining", new { rank })
                    + (pending ? ", " + Loc.T("levelup.choice_needed") : "");
            }
            return Loc.T(taken ? "levelup.rank_taken" : "levelup.rank_locked", new { rank });
        }

        /// <summary>The outstanding-choices status line: "ready to commit" once every pick is made, else
        /// the count of picks still pending.</summary>
        public static string OutstandingText(CareerPathVM cp)
        {
            if (cp.CanCommit.Value) return Loc.T("levelup.ready");
            int count = cp.AvailableSelections.Count(s => !s.SelectionMadeAndValid);
            return Loc.T("levelup.outstanding", new { count });
        }

        /// <summary>One selectable option under a selection group — a feature/talent/ability, or (for
        /// <see cref="RankEntrySelectionStatVM"/>) an attribute/skill increase. Reads name + selected + (stat
        /// delta and/or recommended) + enabled; Enter selects it via the game's own <c>Select()</c> (which
        /// applies to the level-up preview and refreshes commit-ability) and re-announces "selected"
        /// synchronously; Space drills into the full write-up.</summary>
        public static NodeVtable RankOption(BaseRankEntryFeatureVM opt)
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
        public static NodeVtable RankFeature(BaseRankEntryFeatureVM f)
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
