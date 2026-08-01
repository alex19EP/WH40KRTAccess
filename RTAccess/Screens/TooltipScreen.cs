using System.Text.RegularExpressions;
using RTAccess.Accessibility; // TooltipRef
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The tooltip reader — opened with Space (ui.tooltip) on a focused control. Space reads the
    /// tooltip immediately: the reader opens on the first body line (one line per PARAGRAPH, arrow
    /// through at your own pace); keep arrowing (or End) to reach the References list — one entry per
    /// caller-supplied section, per nested row tooltip, and per inline link term (see
    /// <see cref="RTAccess.Accessibility.GlossaryLinks"/>); Enter on a reference opens it as a page of
    /// its own, WITH ITS OWN REFERENCES, so you can keep following links the way you would in a browser;
    /// Back steps back one page, and Back from the first returns to where you were. Each page is pushed
    /// as a CHILD SCREEN of the current one — <c>ScreenManager.Current</c> is the deepest active screen,
    /// so the child chain IS the page history and focus returns to the right line automatically.
    ///
    /// Graph-native: body lines and entries are immutable per instance (a fresh instance per page, gone
    /// on Back), so declaring from the snapshot IS declaring from the state that opened it. Body lines
    /// stay at the top level — the push announces ScreenName + the first line — and the entries sit in
    /// their own References context, announced when focus walks in.
    /// </summary>
    public sealed class TooltipScreen : Screen
    {
        private readonly string _title;
        private readonly List<string> _lines;
        private readonly List<TooltipRef> _refs;

        private TooltipScreen(string title, string body, List<TooltipRef> refs)
        {
            _title = title;
            _lines = new List<string>(SplitLines(body));
            _refs = refs ?? new List<TooltipRef>();
            Wrap = true;
        }

        /// <summary>Open a plain tooltip reader (pushed as a child of the current screen).</summary>
        public static void Open(string title, string body) => Open(title, body, refs: null);

        /// <summary>Open the reader with drill-in entries (sections / nested tooltips / link terms)
        /// following the body lines. No-op for a blank body — <see cref="RTAccess.UI.TooltipChooser"/>
        /// routes the body-less entries-only case to <see cref="DrillMenuScreen"/> instead.</summary>
        internal static void Open(string title, string body, List<TooltipRef> refs)
        {
            if (string.IsNullOrWhiteSpace(body)) return;
            // A reader with nothing to read must never take the keyboard. IsNullOrWhiteSpace is not a strong
            // enough gate: SplitLines trims and drops empty fragments, so a body that is markup-only (or strips
            // to nothing) passes it and builds a screen with ZERO focusable nodes. That screen is unclosable —
            // ui.back YieldsWhenUnfocused, so with no focus Escape goes to the game instead of the Back action,
            // and arrows/Tab have nothing to move between. Fall back to the same "no tooltip" answer the
            // chooser gives for a control with no tooltip at all.
            var lines = new List<string>(SplitLines(body));
            if (lines.Count == 0 && (refs == null || refs.Count == 0))
            {
                Tts.Speak(Loc.T("nav.no_tooltip"), interrupt: true);
                return;
            }
            ScreenManager.Current?.PushChild(new TooltipScreen(title, body, refs));
        }

        public override string Key => "overlay.tooltip";
        public override string ScreenName => _title;
        public override bool IsActive() => false; // only ever a child

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ParentScreen?.RemoveChild(this));
        }


        public override void Build(GraphBuilder b)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                b.AddItem(ControlId.Structural("line:" + i), GraphNodes.Text(() => line));
            }

            if (_refs.Count == 0) return;
            // The references are their own presentation level: positions count within the list and
            // walking in from the body announces "References, list" once. Same Tab-stop as the body,
            // so plain arrowing (or End) flows straight in.
            b.PushContext(Loc.T("nav.references"), Loc.T("role.list"));
            for (int i = 0; i < _refs.Count; i++)
            {
                var entry = _refs[i];
                // Enter opens the reference as a FULL page through the chooser — resolved live on the press,
                // and gathering its own references on the way in, so the trail keeps going. It lands as a
                // child of THIS page (ScreenManager.Current is the deepest screen), so Back steps back here.
                b.AddItem(ControlId.Structural("ref:" + i), GraphNodes.Button(
                    () => entry.Label, () => TooltipChooser.OpenTemplate(entry.Label, entry.Open?.Invoke())));
            }
            b.PopContext();
        }

        // One navigable line per PARAGRAPH. Paragraph is the unit a document is read in, and the game's own
        // text already carries the breaks (brick boundaries, <br>/<p>, literal newlines) as long as the body
        // was stripped with TextUtil.StripRichTextLines. Splitting a paragraph at sentence punctuation is
        // what tore the noble homeworld's "You. Serve me." into two lines, so it is NOT what we do to
        // structured text. It survives only for a body that arrived with NO breaks at all — a hand-built
        // string (a message of the day, a DLC blurb) that would otherwise be one unnavigable node — where
        // there is no paragraph structure to damage. Shared with the document screens (the licence).
        private static readonly Regex SentenceSplit = new Regex(@"(?<=[\.!?]) +", RegexOptions.Compiled);
        internal static IEnumerable<string> SplitLines(string body)
        {
            if (string.IsNullOrEmpty(body)) yield break;
            bool structured = body.IndexOf('\n') >= 0;
            foreach (var para in body.Split('\n'))
            {
                var p = para.Trim();
                if (p.Length == 0) continue;
                if (structured) { yield return p; continue; }
                foreach (var s in SentenceSplit.Split(p))
                {
                    var t = s.Trim();
                    if (t.Length > 0) yield return t;
                }
            }
        }
    }
}
