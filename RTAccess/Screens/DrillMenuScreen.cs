using System.Collections.Generic;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// A drill-in chooser: when the focused element carries more than one thing to read — its own tooltip PLUS
    /// inline glossary terms (see <see cref="RTAccess.Accessibility.GlossaryLinks"/>) — the tooltip key (Space)
    /// opens this list instead of a single body. Arrow to an entry, Enter reads it in a nested
    /// <see cref="TooltipScreen"/> (arrow line-by-line, Back returns here); Back again returns to where you were.
    /// Pushed as a CHILD of the current screen, so it owns the keyboard while open.
    ///
    /// Graph-native: the entry list is immutable per instance (a fresh instance per Space press, gone on
    /// Back), so declaring from the snapshot IS declaring from the state that opened it.
    /// </summary>
    public sealed class DrillMenuScreen : Screen
    {
        private readonly string _title;
        private readonly List<(string label, string body)> _items;

        private DrillMenuScreen(string title, List<(string, string)> items) { _title = title; _items = items; Wrap = true; }

        /// <summary>Open the chooser (pushed as a child of the current screen). No-op for &lt; 1 entry.</summary>
        public static void Open(string title, List<(string, string)> items)
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

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var (label, body) = _items[i];
                // Enter reads the entry in a nested tooltip reader (child of THIS menu, so Back steps back here).
                b.AddItem(ControlId.Structural("drill:" + i), GraphNodes.Button(
                    () => label, () => TooltipScreen.Open(label, body)));
            }
        }
    }
}
