using System.Collections.Generic;
using System.Text.RegularExpressions;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The tooltip reader — opened with Space (ui.tooltip) on a focused control. Space reads the
    /// tooltip immediately: the reader opens on the first body line (split into sentences, arrow
    /// through at your own pace); keep arrowing (or End) to reach the References list — one entry
    /// per rendered section and per inline glossary term (see
    /// <see cref="RTAccess.Accessibility.GlossaryLinks"/>); Enter on a reference reads it in a
    /// nested plain reader (a child of this one, so Back steps back here); Back again returns to
    /// where you were. Pushed as a CHILD SCREEN of the current screen, so it owns the keyboard
    /// while open.
    ///
    /// Graph-native: body lines and entries are immutable per instance (a fresh instance per Space
    /// press, gone on Back), so declaring from the snapshot IS declaring from the state that opened
    /// it. Body lines stay at the top level — the push announces ScreenName + the first line — and
    /// the entries sit in their own References context, announced when focus walks in.
    /// </summary>
    public sealed class TooltipScreen : Screen
    {
        private readonly string _title;
        private readonly List<string> _lines;
        private readonly List<(string label, string body)> _entries;

        private TooltipScreen(string title, string body, List<(string label, string body)> entries)
        {
            _title = title;
            _lines = new List<string>(SplitLines(body));
            _entries = entries ?? new List<(string label, string body)>();
            Wrap = true;
        }

        /// <summary>Open a plain tooltip reader (pushed as a child of the current screen).</summary>
        public static void Open(string title, string body) => Open(title, body, entries: null);

        /// <summary>Open the reader with drill-in entries (rendered sections / glossary terms)
        /// following the body lines. No-op for a blank body — <see cref="RTAccess.UI.TooltipChooser"/>
        /// routes the body-less entries-only case to <see cref="DrillMenuScreen"/> instead.</summary>
        internal static void Open(string title, string body, List<(string label, string body)> entries)
        {
            if (!string.IsNullOrWhiteSpace(body))
                ScreenManager.Current?.PushChild(new TooltipScreen(title, body, entries));
        }

        public override string Key => "overlay.tooltip";
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
            for (int i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                b.AddItem(ControlId.Structural("line:" + i), GraphNodes.Text(() => line));
            }

            if (_entries.Count == 0) return;
            // The references are their own presentation level: positions count within the list and
            // walking in from the body announces "References, list" once. Same Tab-stop as the body,
            // so plain arrowing (or End) flows straight in.
            b.PushContext(Loc.T("nav.references"), Loc.T("role.list"));
            for (int i = 0; i < _entries.Count; i++)
            {
                var (label, body) = _entries[i];
                // Enter reads the entry in a nested reader (child of THIS one, so Back steps back here).
                b.AddItem(ControlId.Structural("ref:" + i), GraphNodes.Button(
                    () => label, () => Open(label, body)));
            }
            b.PopContext();
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
