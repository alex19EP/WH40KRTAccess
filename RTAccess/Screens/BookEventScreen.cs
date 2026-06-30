using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Dialog;          // DialogContextVM
using Kingmaker.Code.UI.MVVM.VM.Dialog.BookEvent; // BookEventVM
using Kingmaker.Code.UI.MVVM.VM.Dialog.Epilog;    // EpilogVM (a BookEventVM subclass)
using Kingmaker.DialogSystem.Blueprints;          // BlueprintBookPage
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// A book event (<see cref="BookEventVM"/>) — the illustrated storybook page with a passage of narrative
    /// and numbered choices. It rides the SAME dialog HUD context as ordinary dialogue
    /// (<c>DialogContextVM.BookEventVM</c>, beside <c>DialogVM</c>) and reuses the dialogue
    /// <c>AnswerVM</c> → <see cref="DialogAnswerButton"/>.
    ///
    /// A page carries several cues (the paragraphs, shown together) plus the answers; we read the whole
    /// passage when a new page appears (keyed on <c>BlueprintBookPage</c>, like the dialogue cue), and the
    /// passage is the first focusable element so you can re-read it. Choosing an answer advances to the next
    /// page in place until the book closes.
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

        private BlueprintBookPage _builtPage;  // page the tree was built for
        private BlueprintBookPage _spokenPage; // page we've read aloud

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

        public override void OnPush() { Clear(); Reset(); }
        public override void OnPop() { Clear(); Reset(); }
        private void Reset() { _builtPage = null; _spokenPage = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            var page = vm.BlueprintBookPage.Value;
            if (page == null) return; // the VM exists a frame before the first page is pushed

            if (!ReferenceEquals(page, _builtPage)) { _builtPage = page; Rebuild(vm); }
            if (!ReferenceEquals(page, _spokenPage)) { _spokenPage = page; Speak(vm); }
        }

        // Same transcript FlowSheet shape as ordinary dialogue: the passage as the log region, the choices
        // as the answers region. Focus lands at the top of the passage; Down reaches the choices.
        private void Rebuild(BookEventVM vm)
        {
            Clear();
            var lines = PassageLines(vm);

            var sheet = new FlowSheet();
            var log = sheet.List(null);
            UIElement focus = null;
            foreach (var line in lines)
            {
                var text = line;
                var row = new TextElement(text);
                log.Item(row);
                if (focus == null) focus = row;
            }

            var answers = sheet.List(null);
            int count = 0;
            var list = vm.Answers.Value;
            if (list != null)
                foreach (var a in list)
                    if (a != null) { answers.Item(DialogAnswerButton.For(a)); count++; }
            if (count == 0) sheet.RemoveRegion(answers);

            sheet.Reflow();
            Add(sheet);
            Navigation.Attach(this);
            if (focus == null) focus = sheet.FirstFocusable();
            if (focus != null) Navigation.Focus(focus, announce: false);
        }

        // Read the whole passage once per page, QUEUED (never interrupting — the dialogue rule). Re-reading
        // individual paragraphs is done by arrowing the rows.
        private void Speak(BookEventVM vm)
        {
            var lines = PassageLines(vm);
            if (lines.Count > 0) Tts.Speak(string.Join("\n", lines.ToArray()), interrupt: false);
        }

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
