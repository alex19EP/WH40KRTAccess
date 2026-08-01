using RTAccess.Accessibility; // TooltipRef
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// A drill-in chooser for the BODY-LESS tooltip case: when the focused element has no tooltip text of its
    /// own but carries extras — rendered sections and/or inline glossary terms (see
    /// <see cref="RTAccess.Accessibility.GlossaryLinks"/>) — the tooltip key (Space) opens this list of them.
    /// (An element WITH a body opens <see cref="TooltipScreen"/> directly, extras nested inside it — see
    /// <see cref="RTAccess.UI.TooltipChooser"/>.) Arrow to an entry, Enter opens it as a full page with
    /// references of its own (arrow line-by-line, keep following links, Back returns here); Back again
    /// returns to where you were. Pushed as a CHILD of the current screen, so it owns the keyboard while open.
    ///
    /// Graph-native: the entry list is immutable per instance (a fresh instance per Space press, gone on
    /// Back), so declaring from the snapshot IS declaring from the state that opened it.
    /// </summary>
    public sealed class DrillMenuScreen : Screen
    {
        private readonly string _title;
        private readonly List<TooltipRef> _items;

        private DrillMenuScreen(string title, List<TooltipRef> items) { _title = title; _items = items; Wrap = true; }

        /// <summary>Open the chooser (pushed as a child of the current screen). No-op for &lt; 1 entry.</summary>
        internal static void Open(string title, List<TooltipRef> items)
        {
            if (items != null && items.Count > 0)
                ScreenManager.Current?.PushChild(new DrillMenuScreen(title, items));
        }

        public override string Key => "overlay.drillmenu";
        public override string ScreenName => _title;
        public override bool IsActive() => false; // only ever a child

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ParentScreen?.RemoveChild(this));
        }


        public override void Build(GraphBuilder b)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var entry = _items[i];
                // Enter opens the entry as a full page through the chooser — resolved live on the press, and
                // gathering its own references, so the trail keeps going. Child of THIS menu, so Back steps back.
                b.AddItem(ControlId.Structural("drill:" + i), GraphNodes.Button(
                    () => entry.Label, () => TooltipChooser.OpenTemplate(entry.Label, entry.Open?.Invoke())));
            }
        }
    }
}
