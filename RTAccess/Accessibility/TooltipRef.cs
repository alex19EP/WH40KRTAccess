using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate

namespace RTAccess.Accessibility
{
    /// <summary>
    /// One followable drill-in target on a tooltip page: the word you would hover or right-click in the
    /// sighted UI, plus a factory for the tooltip it opens. Covers all three kinds the mod surfaces —
    /// an inline <c>&lt;link&gt;</c> term (<see cref="GlossaryLinks"/>), a nested tooltip a rendered row
    /// hangs off itself (<see cref="NestedTooltips"/>), and a caller-supplied side panel (an item's
    /// compare-vs-equipped card, a rank feature's category write-up).
    ///
    /// It carries a TEMPLATE FACTORY, never rendered text. That is the whole point: the page a reference
    /// opens is itself a template-backed page, so it re-mines its own links and nested rows and can be
    /// drilled again — pages all the way down, like following links in a browser. Flattening the target to
    /// a string at gather time (what this replaced) capped drilling at exactly one level, because the
    /// stripped text no longer carries the <c>&lt;link&gt;</c> tags or the brick VMs to mine.
    ///
    /// Resolved LIVE on each follow, per the immediate-mode law: the factory runs when you press Enter, not
    /// when the list was built, so a page never shows state that has since moved. It also means gathering is
    /// free — the old eager render cost one full trip through the game's tooltip engine per entry, on a
    /// keypress, for entries you would mostly never open.
    /// </summary>
    internal readonly struct TooltipRef
    {
        public readonly string Label;
        public readonly Func<TooltipBaseTemplate> Open;

        public TooltipRef(string label, Func<TooltipBaseTemplate> open)
        {
            Label = label;
            Open = open;
        }

        /// <summary>A reference to an already-resolved template (a nested row, a compare card) — the common
        /// case where the target object is in hand and there is nothing to look up again.</summary>
        public static TooltipRef To(string label, TooltipBaseTemplate template) =>
            new TooltipRef(label, () => template);
    }
}
