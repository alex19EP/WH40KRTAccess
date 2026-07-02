using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.RankEntry;         // RankEntrySelectionVM, RankEntryState, RankFeatureState
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.RankEntry.Feature;  // BaseRankEntryFeatureVM, RankEntrySelectionStatVM
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.UI;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One pending level-up choice — a <see cref="RankEntrySelectionVM"/> (a talent / ability / attribute
    /// pick from a career-path rank) presented as a collapsible group: the group reads the choice's prompt
    /// (<see cref="RankEntrySelectionVM.GetHintText"/>) plus its live state (not chosen / chosen: X /
    /// committed / choose earlier first); expanding lists the options as <see cref="ProxyRankOption"/>s,
    /// Enter picks one. State is read LIVE off the VM reactives, so picking an option updates this group and
    /// unlocks the next one WITHOUT rebuilding the screen. The authoritative option list is the VM's
    /// <see cref="RankEntrySelectionVM.FilteredGroupList"/>, which we materialise once via
    /// <see cref="RankEntrySelectionVM.UpdateFeatures"/> (filtered groups build it lazily).
    /// </summary>
    public sealed class ProxyRankSelection : Container
    {
        private readonly RankEntrySelectionVM _sel;

        public ProxyRankSelection(RankEntrySelectionVM sel) : base(ContainerShape.Tree)
        {
            _sel = sel;
            LabelProvider = () => sel.GetHintText();
            sel.UpdateFeatures(); // build the option groups (lazy for filtered talent lists)
            foreach (var opt in sel.FilteredGroupList.OfType<BaseRankEntryFeatureVM>())
                Add(new ProxyRankOption(opt));
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_sel.GetHintText()));
            yield return new ValueAnnouncement(Message.Raw(StateText()));
            if (Expandable) yield return new RoleAnnouncement(Expanded ? "expanded" : "collapsed");
        }

        private string StateText()
        {
            var name = _sel.SelectedFeature.Value?.DisplayName ?? "";
            if (_sel.EntryState.Value == RankEntryState.Committed) return Loc.T("levelup.committed", new { name });
            if (_sel.SelectionMade) return Loc.T("levelup.chosen", new { name });
            if (_sel.EntryState.Value == RankEntryState.WaitPreviousToSelect) return Loc.T("levelup.locked");
            return Loc.T("levelup.not_chosen");
        }
    }

    /// <summary>
    /// One selectable option under a <see cref="ProxyRankSelection"/> — a feature/talent/ability, or (for
    /// <see cref="RankEntrySelectionStatVM"/>) an attribute/skill increase. Reads name + selected + (stat
    /// delta and/or recommended) + enabled; Enter selects it via the game's own <c>Select()</c> (which
    /// applies to the level-up preview and refreshes commit-ability); Space drills into the full write-up.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(SelectedAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyRankOption : UIElement
    {
        private readonly BaseRankEntryFeatureVM _opt;

        public ProxyRankOption(BaseRankEntryFeatureVM opt) { _opt = opt; }

        public override bool ReannounceOnActivate => true; // picking flips it to selected in place

        public override TooltipBaseTemplate GetTooltipTemplate() => _opt.TooltipTemplate();

        private bool IsChosen =>
            _opt.FeatureState.Value == RankFeatureState.Selected || _opt.FeatureState.Value == RankFeatureState.Committed;

        private string Name => (_opt is RankEntrySelectionStatVM st && !string.IsNullOrEmpty(st.StatDisplayName))
            ? st.StatDisplayName : _opt.DisplayName;

        private string ValueText()
        {
            string s = null;
            if (_opt is RankEntrySelectionStatVM st)
            {
                s = st.StatIncreaseLabel.Value; // "+10" — the per-rank gain the game shows on each option
                // Show the would-be result WHILE navigating (before the pick is staged). The game's own
                // SummaryStatIncreaseLabel only reflects the preview once THIS option is staged — before that
                // it reads "45 > 45" — so derive the target from current + increase instead. Worded "45 to 55"
                // (locale) so TTS doesn't read the raw ">" arrow as "greater than".
                int cur = st.UnitStat?.ModifiedValue ?? 0;
                int inc = ParseInc(st.StatIncreaseLabel.Value);
                if (inc != 0) s += ", " + Loc.T("levelup.stat_result", new { from = cur, to = cur + inc });
            }
            if (_opt.IsRecommended)
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

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name ?? ""));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(IsChosen);
            var v = ValueText();
            if (!string.IsNullOrEmpty(v)) yield return new ValueAnnouncement(Message.Raw(v));
            yield return new EnabledAnnouncement(_opt.CanSelect() || IsChosen);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (_opt.CanSelect())
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"), _ => _opt.Select());
        }
    }

    /// <summary>
    /// A read-only feature node under a rank in the level-up outline — an ability/talent/stat the rank
    /// grants AUTOMATICALLY (no choice to make). Reads its name (+ recommended); Space drills into the full
    /// write-up. Not activatable — the rank's state (taken / gaining / locked) is carried by the group label.
    /// </summary>
    public sealed class ProxyRankFeature : UIElement
    {
        private readonly BaseRankEntryFeatureVM _f;

        public ProxyRankFeature(BaseRankEntryFeatureVM f) { _f = f; }

        public override TooltipBaseTemplate GetTooltipTemplate() => _f.TooltipTemplate();

        private string Name => (_f is RankEntrySelectionStatVM st && !string.IsNullOrEmpty(st.StatDisplayName))
            ? st.StatDisplayName : _f.DisplayName;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name ?? ""));
            if (_f.IsRecommended) yield return new ValueAnnouncement(Message.Raw(Loc.T("chargen.recommended")));
        }
    }
}
