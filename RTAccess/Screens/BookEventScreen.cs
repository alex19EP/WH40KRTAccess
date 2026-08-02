using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Dialog;          // DialogContextVM
using Kingmaker.Code.UI.MVVM.VM.Dialog.BookEvent; // BookEventVM
using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog;    // CueVM (a book page's paragraphs)
using Kingmaker.Code.UI.MVVM.VM.Dialog.Epilog;    // EpilogVM (a BookEventVM subclass)
using Kingmaker.Controllers.Dialog;               // SkillCheckResult
using Kingmaker.DialogSystem.Blueprints;          // BlueprintBookPage
using RTAccess.Accessibility;                     // VoiceOver, GlossaryLinks, SkillCheckLinks
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// A book event (<see cref="BookEventVM"/>) — the illustrated storybook page with a passage of narrative
    /// and numbered choices — on the shared <see cref="TranscriptScreen{TVm,TId}"/> shell. It rides the SAME
    /// dialog HUD context as ordinary dialogue (<c>DialogContextVM.BookEventVM</c>, beside <c>DialogVM</c>) and
    /// reuses the conversation <c>AnswerVM</c> → <see cref="DialogNodes.AnswerNode"/>.
    ///
    /// IMMEDIATE MODE: one silent transcript stop declared fresh from the live VM each render (the passage lines,
    /// then the choices). A page carries several cues (the paragraphs, shown together) plus the answers; the
    /// shell reads the whole passage when a new page appears (keyed on <c>BlueprintBookPage</c>, like the dialogue
    /// cue) and points focus at the passage top silently, so you can re-read it. Choosing an answer advances to
    /// the next page in place until the book closes.
    ///
    /// Epilogue narration is the same thing — <see cref="EpilogVM"/> derives from BookEventVM (it merges its
    /// paragraphs into one cue and carries a <c>Title</c>) — so we pick that VM up too and read its title
    /// ahead of the passage. RT difference vs WOTR: <c>InterchapterVM</c> is NOT a book event in RT (it is
    /// video narration whose lines arrive as subtitle barks, handled by the bark reader), so it is
    /// deliberately excluded here.
    /// </summary>
    public sealed class BookEventScreen : TranscriptScreen<BookEventVM, BlueprintBookPage>
    {
        public override string Key => "ctx.bookevent";
        public override string ScreenName => Loc.T("screen.book_event");

        // BookEventVM, or its EpilogVM subclass — NOT InterchapterVM (subtitle/video narration; see header).
        protected override BookEventVM Vm()
        {
            var ctx = Context();
            return ctx?.BookEventVM?.Value ?? ctx?.EpilogVM?.Value;
        }

        protected override BlueprintBookPage Identity(BookEventVM vm) => vm.BlueprintBookPage.Value;

        // Home focus at the passage TOP (row 0). If the page has no passage rows, the navigator seats the first
        // answer instead.
        protected override ControlId HomeNode(BookEventVM vm, BlueprintBookPage page)
            => ControlId.Structural(PageKey(vm, page) + "row:0");

        // Read the whole passage once per page, QUEUED (never interrupting — the transcript rule). Re-reading
        // individual paragraphs is done by arrowing the rows. Paragraphs the game narrates itself are dropped
        // from the READING only (BookEventVM queues one voice-over per cue and plays them in sequence, so a
        // page can be part-voiced) — every paragraph stays a browsable row below.
        protected override void SpeakLine(BookEventVM vm, BlueprintBookPage page)
        {
            var lines = PassageLines(vm, skipVoiced: true);
            if (lines.Count == 0) return;
            var spoken = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++) spoken[i] = lines[i].Text;
            Tts.Speak(string.Join("\n", spoken), interrupt: false);
        }

        // Same transcript shape as ordinary dialogue: one silent, positions-off scope holding the passage lines,
        // then the choices. Focus lands at the top of the passage; Down reaches the choices.
        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var page = vm.BlueprintBookPage.Value;
            if (page == null) return;

            string k = PageKey(vm, page);
            b.PushContext("", role: null, positions: false);

            var lines = PassageLines(vm);
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i]; // capture
                var vt = GraphNodes.Text(() => line.Text);
                // Space follows the paragraph's own inline <link> terms — the highlighted words a sighted
                // player hovers (BookEventCueView wires the same links via SetLinkTooltip). Mining needs the
                // RAW paragraph, which is why the row keeps it: the displayed text has been stripped of the
                // very tags we match. Skill-check links resolve against THIS cue's rolls; a single term opens
                // its page directly, several offer a small chooser; a paragraph with no links answers
                // "No tooltip", so the wiring costs nothing where there is nothing to follow.
                if (line.HasLinks)
                    vt.OnTooltip = () => TooltipChooser.FollowLinks(line.Raw,
                        SkillCheckLinks.Results(line.Checks));
                b.AddItem(ControlId.Structural(k + "row:" + i), vt);
            }

            // Choices — the game's own AnswerVM list, through the shared answer node factory (numbered label,
            // Space tooltip, activation routed through DialogChoiceGate). Keys carry the page identity, so a new
            // page drops the old choices and adds the new.
            var list = vm.Answers.Value;
            if (list != null)
            {
                int ai = 0;
                foreach (var a in list)
                {
                    if (a == null) continue;
                    b.AddItem(ControlId.Referenced(a, k + "ans:" + ai++), DialogNodes.AnswerNode(a));
                }
            }

            b.PopContext();
        }

        private static string PageKey(BookEventVM vm, BlueprintBookPage page)
            => "book:" + vm.GetHashCode() + ":page:" + page.GetHashCode() + ":";

        /// <summary>One passage paragraph: what it reads as, plus what it was BEFORE the markup was stripped
        /// and the cue it came from. A row can only offer its links if it kept both — the strip that makes
        /// the text speakable is the same strip that removes the <c>&lt;link&gt;</c> anchors, and a skill
        /// check resolves only against its own cue's roll list.</summary>
        private readonly struct PassageLine
        {
            public readonly string Text;
            public readonly string Raw;
            public readonly CueVM Cue;
            public PassageLine(string text, string raw, CueVM cue) { Text = text; Raw = raw; Cue = cue; }

            public List<SkillCheckResult> Checks => Cue?.SkillChecks;
            public bool HasLinks => Raw != null && Raw.IndexOf("<link", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // The page as transcript lines: the epilogue title first (if any), then the cues split on newlines and
        // rich-text stripped. The cue text is COMPOSED the way BookEventCueView composes it — mechanic overlay
        // then narrative — not read off RawText: the skill-check result is the payoff of a book-event page, it
        // is minted at draw time and lives nowhere in RawText, and book events never reach the combat log
        // either (GameLogEventDialogHistory skips DialogType.Book), so reading RawText alone left the roll
        // outcome unobtainable. In Book dialogs the mechanic text ends with its own blank line, so it falls
        // out as its own row — carrying the two link anchors the row's Space then follows.
        //
        // skipVoiced (the spoken pass only, never Build) drops the paragraphs the game narrates itself — but
        // only the NARRATIVE half: no actor ever reads the roll result, so the mechanic line still speaks.
        // The title is never voiced, so it always reads; it carries no cue, hence no links to follow.
        private static List<PassageLine> PassageLines(BookEventVM vm, bool skipVoiced = false)
        {
            var lines = new List<PassageLine>();
            if (vm is EpilogVM ep && !string.IsNullOrWhiteSpace(ep.Title.Value))
                lines.Add(new PassageLine(TextUtil.StripRichText(ep.Title.Value), null, null));
            foreach (var cue in vm.Cues)
            {
                if (cue == null) continue;
                bool voiced = skipVoiced && VoiceOver.Covers(cue.BlueprintCue?.Text);
                var t = DialogText.ComposedRaw(cue, includeNarrative: !voiced);
                if (string.IsNullOrWhiteSpace(t)) continue;
                foreach (var part in t.Split('\n'))
                {
                    var clean = TextUtil.StripRichText(part);
                    if (!string.IsNullOrWhiteSpace(clean)) lines.Add(new PassageLine(clean, part, cue));
                }
            }
            return lines;
        }
    }
}
