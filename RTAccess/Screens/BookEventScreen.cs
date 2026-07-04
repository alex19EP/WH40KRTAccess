using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Dialog;          // DialogContextVM
using Kingmaker.Code.UI.MVVM.VM.Dialog.BookEvent; // BookEventVM
using Kingmaker.Code.UI.MVVM.VM.Dialog.Epilog;    // EpilogVM (a BookEventVM subclass)
using Kingmaker.DialogSystem.Blueprints;          // BlueprintBookPage
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// A book event (<see cref="BookEventVM"/>) — the illustrated storybook page with a passage of narrative
    /// and numbered choices. It rides the SAME dialog HUD context as ordinary dialogue
    /// (<c>DialogContextVM.BookEventVM</c>, beside <c>DialogVM</c>) and reuses the conversation
    /// <c>AnswerVM</c> → <see cref="DialogNodes.AnswerNode"/>.
    ///
    /// Graph-native and IMMEDIATE MODE: one silent transcript stop declared fresh from the live VM each render
    /// (the passage lines, then the choices). A page carries several cues (the paragraphs, shown together) plus
    /// the answers; we read the whole passage when a new page appears (keyed on <c>BlueprintBookPage</c>, like
    /// the dialogue cue) and point focus at the passage top silently, so you can re-read it. Choosing an answer
    /// advances to the next page in place until the book closes.
    ///
    /// Epilogue narration is the same thing — <see cref="EpilogVM"/> derives from BookEventVM (it merges its
    /// paragraphs into one cue and carries a <c>Title</c>) — so we pick that VM up too and read its title
    /// ahead of the passage. RT difference vs WOTR: <c>InterchapterVM</c> is NOT a book event in RT (it is
    /// video narration whose lines arrive as subtitle barks, handled by the bark reader), so it is
    /// deliberately excluded here.
    /// </summary>
    public sealed class BookEventScreen : Screen
    {
        public override string Key => "ctx.bookevent";
        public override string ScreenName => Loc.T("screen.book_event");
        public override int Layer => 15; // over the in-game context + service windows, like dialogue
        // Like dialogue: a book event can hide without closing (cutscene gap / pause menu) — keep state.
        public override bool KeepStateOnPop => true;

        private BlueprintBookPage _focusedPage; // page focus was homed to
        private BlueprintBookPage _spokenPage;  // page read aloud

        private static DialogContextVM Context()
        {
            var rc = Game.Instance?.RootUiContext;
            if (rc == null) return null;
            return rc.IsSpace
                ? rc.SpaceVM?.StaticPartVM?.DialogContextVM
                : rc.SurfaceVM?.StaticPartVM?.DialogContextVM;
        }

        // BookEventVM, or its EpilogVM subclass — NOT InterchapterVM (subtitle/video narration; see header).
        private static BookEventVM Vm()
        {
            var ctx = Context();
            return ctx?.BookEventVM?.Value ?? ctx?.EpilogVM?.Value;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPush() { Reset(); }

        // A hide (cutscene gap / pause menu) POPS us with KeepStateOnPop=true. Clear ONLY the focus marker so the
        // next OnUpdate re-homes focus to the CURRENT page top (WA ff35982 — otherwise re-showing lands on the
        // top of the whole transcript); keep the spoken marker so the passage isn't re-read on a mere hide. A
        // real close (Vm()==null) fully resets.
        public override void OnPop()
        {
            _focusedPage = null;
            if (Vm() == null) Reset();
        }

        private void Reset() { _focusedPage = null; _spokenPage = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            var page = vm.BlueprintBookPage.Value;
            if (page == null) return; // the VM exists a frame before the first page is pushed

            // On a new page (or after a hide cleared the focus marker), point focus at the passage TOP SILENTLY —
            // the passage read below is the speech. If the page has no passage rows, the navigator seats the
            // first answer instead. announce:false always here.
            if (!ReferenceEquals(page, _focusedPage))
            {
                _focusedPage = page;
                Navigation.Active?.FocusNode(ControlId.Structural(PageKey(vm, page) + "row:0"), announce: false);
            }
            if (!ReferenceEquals(page, _spokenPage)) { _spokenPage = page; Speak(vm); }
        }

        public override bool BuildsGraph => true;

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
                var captured = lines[i];
                b.AddItem(ControlId.Structural(k + "row:" + i), GraphNodes.Text(() => captured));
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

        // Read the whole passage once per page, QUEUED (never interrupting — the dialogue rule). Re-reading
        // individual paragraphs is done by arrowing the rows.
        private static void Speak(BookEventVM vm)
        {
            var lines = PassageLines(vm);
            if (lines.Count > 0) Tts.Speak(string.Join("\n", lines.ToArray()), interrupt: false);
        }

        private static string PageKey(BookEventVM vm, BlueprintBookPage page)
            => "book:" + vm.GetHashCode() + ":page:" + page.GetHashCode() + ":";

        // The page as transcript lines: the epilogue title first (if any), then one line per cue paragraph
        // (RawText = BlueprintCue.DisplayText, split on newlines, rich-text stripped).
        private static List<string> PassageLines(BookEventVM vm)
        {
            var lines = new List<string>();
            if (vm is EpilogVM ep && !string.IsNullOrWhiteSpace(ep.Title.Value))
                lines.Add(TextUtil.StripRichText(ep.Title.Value));
            foreach (var cue in vm.Cues)
            {
                var t = cue != null ? cue.RawText : null;
                if (string.IsNullOrWhiteSpace(t)) continue;
                foreach (var part in t.Split('\n'))
                {
                    var clean = TextUtil.StripRichText(part);
                    if (!string.IsNullOrWhiteSpace(clean)) lines.Add(clean);
                }
            }
            return lines;
        }
    }
}
