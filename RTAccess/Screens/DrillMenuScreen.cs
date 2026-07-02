using System.Collections.Generic;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// A drill-in chooser: when the focused element carries more than one thing to read — its own tooltip PLUS
    /// inline glossary terms (see <see cref="RTAccess.Accessibility.GlossaryLinks"/>) — the tooltip key (Space)
    /// opens this list instead of a single body. Arrow to an entry, Enter reads it in a nested
    /// <see cref="TooltipScreen"/> (arrow line-by-line, Back returns here); Back again returns to where you were.
    /// Pushed as a CHILD of the current screen, so it owns the keyboard while open.
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

        public override void OnPush() { Clear(); Build(); }
        public override void OnPop() { Clear(); }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ParentScreen?.RemoveChild(this));
        }

        private void Build()
        {
            var list = new ListContainer();
            foreach (var it in _items)
            {
                var label = it.label;
                var body = it.body;
                // Enter reads the entry in a nested tooltip reader (child of THIS menu, so Back steps back here).
                list.Add(new ProxyActionButton(() => label, null, () => TooltipScreen.Open(label, body)));
            }
            Add(list);
        }
    }
}
