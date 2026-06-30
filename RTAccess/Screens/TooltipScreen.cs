using System.Collections.Generic;
using System.Text.RegularExpressions;
using RTAccess.UI;

namespace RTAccess.Screens
{
    /// <summary>
    /// A lightweight reader for a control's tooltip/description text — opened with Space (ui.tooltip) on a
    /// focused element that supplies <see cref="UIElement.GetTooltipText"/>. Pushed as a CHILD SCREEN of the
    /// current screen, so it owns the keyboard while open and Back returns you to where you were. The body is
    /// split into sentences so a long description can be read at your own pace, arrowing line by line. (The
    /// rich brick / glossary-link tooltip reader is a separate later feature; this covers header+body text.)
    /// </summary>
    public sealed class TooltipScreen : Screen
    {
        private readonly string _title;
        private readonly string _body;

        private TooltipScreen(string title, string body) { _title = title; _body = body; Wrap = true; }

        /// <summary>Open a tooltip reader (pushed as a child of the current screen).</summary>
        public static void Open(string title, string body)
        {
            if (!string.IsNullOrWhiteSpace(body))
                ScreenManager.Current?.PushChild(new TooltipScreen(title, body));
        }

        public override string Key => "overlay.tooltip";
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
            foreach (var line in SplitLines(_body))
                list.Add(new TextElement(line));
            Add(list);
        }

        // Paragraphs (\n) then sentences (after sentence-ending punctuation + space) → one navigable line
        // each, so a long description reads line by line instead of in one long breath.
        private static readonly Regex SentenceSplit = new Regex(@"(?<=[\.!?]) +", RegexOptions.Compiled);
        private static IEnumerable<string> SplitLines(string body)
        {
            foreach (var para in body.Split('\n'))
            {
                var p = para.Trim();
                if (p.Length == 0) continue;
                foreach (var s in SentenceSplit.Split(p))
                {
                    var t = s.Trim();
                    if (t.Length > 0) yield return t;
                }
            }
        }
    }
}
