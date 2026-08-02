using RTAccess.Accessibility; // TooltipRef, TooltipLine, TooltipPage
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The tooltip reader — opened with Space (ui.tooltip) on a focused control. Space reads the
    /// tooltip immediately: the reader opens on the first body line (one line per PARAGRAPH, arrow
    /// through at your own pace). A line that carries inline link terms says so ("…, 2 links") and
    /// follows them IN PLACE — Space or Enter on the line opens the single term's page directly, or a
    /// small chooser when the line carries several — so reviewing never means walking to the bottom of
    /// the page and back (the consolidated-links complaint). The References list after the body keeps
    /// only what has no line to live on: caller-supplied sections (compare cards), the rows' nested
    /// tooltips, and orphan links. Every page opens WITH ITS OWN LINKS, so you can keep following terms
    /// the way you would in a browser; Back steps back one page, and Back from the first returns to
    /// where you were. Each page is pushed as a CHILD SCREEN of the current one —
    /// <c>ScreenManager.Current</c> is the deepest active screen, so the child chain IS the page history
    /// and focus returns to the right line automatically.
    ///
    /// Graph-native: body lines and entries are immutable per instance (a fresh instance per page, gone
    /// on Back), so declaring from the snapshot IS declaring from the state that opened it. Body lines
    /// stay at the top level — the push announces ScreenName + the first line — and the entries sit in
    /// their own References context, announced when focus walks in.
    /// </summary>
    public sealed class TooltipScreen : Screen
    {
        private readonly string _title;
        private readonly List<TooltipLine> _lines;
        private readonly List<TooltipRef> _refs;

        private TooltipScreen(string title, List<TooltipLine> lines, List<TooltipRef> refs)
        {
            _title = title;
            _lines = lines;
            _refs = refs ?? new List<TooltipRef>();
            Wrap = true;
        }

        /// <summary>Open a plain tooltip reader (pushed as a child of the current screen).</summary>
        public static void Open(string title, string body)
            => Open(title, TooltipPage.FromPlain(body).Lines, refs: null);

        /// <summary>Open the reader over assembled page lines (each with its own inline links) and the
        /// References entries that follow them (sections / nested tooltips / orphan link terms). No-op with
        /// nothing to show — <see cref="RTAccess.UI.TooltipChooser"/> routes the body-less entries-only
        /// case to <see cref="DrillMenuScreen"/> instead.</summary>
        internal static void Open(string title, List<TooltipLine> lines, List<TooltipRef> refs)
        {
            // A reader with nothing to read must never take the keyboard: a screen with ZERO focusable
            // nodes is unclosable — ui.back YieldsWhenUnfocused, so with no focus Escape goes to the game
            // instead of the Back action, and arrows/Tab have nothing to move between. Fall back to the
            // same "no tooltip" answer the chooser gives for a control with no tooltip at all.
            if (lines == null || (lines.Count == 0 && (refs == null || refs.Count == 0)))
            {
                Tts.Speak(Loc.T("nav.no_tooltip"), interrupt: true);
                return;
            }
            ScreenManager.Current?.PushChild(new TooltipScreen(title, lines, refs));
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
                b.AddItem(ControlId.Structural("line:" + i), LineNode(line));
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

        // A body line. With links: the readout appends how many ("…, 2 links" — the spoken form of the
        // highlighting a sighted player sees inline; a Tooltip-kind part, so the per-kind announcement
        // setting can silence it), and Space OR Enter follows them — one link opens its page directly,
        // several open a small chooser; the opened page lands as a child of THIS page, so Back returns to
        // this very line. Without links: a plain re-readable text row (Space answers "No tooltip").
        private static NodeVtable LineNode(TooltipLine line)
        {
            if (line.Links == null)
            {
                var vt = GraphNodes.Text(() => line.Text);
                vt.SearchText = () => line.Text;
                return vt;
            }
            var links = line.Links;
            Action follow = () => TooltipChooser.FollowRefs(links);
            return new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => line.Text),
                    new NodeAnnouncement(() => links.Count == 1
                            ? Loc.T("tooltip.links_one")
                            : Loc.T("tooltip.links_many", new { count = links.Count }),
                        kind: AnnouncementKinds.Tooltip),
                },
                SearchText = () => line.Text,
                OnTooltip = follow,
                OnActivate = follow,
            };
        }
    }
}
