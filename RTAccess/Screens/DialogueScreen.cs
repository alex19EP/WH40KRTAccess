using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Dialog;        // DialogContextVM
using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog; // DialogVM, CueVM
using Kingmaker.PubSubSystem.Core;             // EventBus, IEscMenuHandler
using RTAccess.Accessibility;                  // DialogText.BuildCueLine
using RTAccess.UI;
using RTAccess.UI.Proxies;
using UniRx;

namespace RTAccess.Screens
{
    /// <summary>
    /// An in-game conversation (the common <see cref="DialogVM"/>) as ONE navigable <see cref="FlowSheet"/>
    /// that reads like a transcript: the scrollback (the game's own pre-formatted <c>DialogVM.History</c>
    /// lines — past cues and chosen answers), then the CURRENT cue row, then the answers region. A new cue
    /// rebuilds the sheet and lands focus on the cue row silently — you hear the line via the cue
    /// announcement, press Down for the answers/Continue, or Up to re-read earlier lines.
    ///
    /// We speak a line only once it is actually delivered: the dialogue panel is visible
    /// (<see cref="DialogVM.IsVisible"/> — the engine drops it during cutscene/escmenu/pause, which is RT's
    /// replacement for WOTR's <c>m_CutsceneScheduled</c>) AND it is not faded behind a designer mid-cue
    /// black screen (<see cref="DialogContextVM.ToggleDialogFadeCommand"/>, shadowed into
    /// <see cref="_dialogFaded"/>). Each cue is announced once, QUEUED — dialogue never interrupts speech.
    /// Book events / epilogues are a separate screen; notifications (alignment/items/xp) are deferred.
    /// </summary>
    public sealed class DialogueScreen : Screen
    {
        public override string Key => "ctx.dialogue";
        public override string ScreenName => Loc.T("screen.dialog");
        public override int Layer => 15; // over the in-game context + service windows

        private DialogContextVM _subCtx; // the context our fade subscription belongs to
        private IDisposable _fadeSub;
        private bool _dialogFaded;

        private DialogVM _builtVm;  // the conversation the tree belongs to (a fresh VM per conversation)
        private CueVM _builtCue;    // cue the sheet was built for
        private CueVM _spokenCue;   // cue we've announced

        // In-area OR star-system context — the DialogContextVM lives on whichever StaticPartVM is live
        // (Surface or Space), exactly as RootUIContext.HasDialog resolves it.
        private static DialogContextVM Context()
        {
            var rc = Game.Instance?.RootUiContext;
            if (rc == null) return null;
            return rc.IsSpace
                ? rc.SpaceVM?.StaticPartVM?.DialogContextVM
                : rc.SurfaceVM?.StaticPartVM?.DialogContextVM;
        }

        private static DialogVM Vm() => Context()?.DialogVM?.Value;

        public override bool IsActive() => Vm() != null; // == RootUiContext.HasDialog

        public override void OnPush() { Clear(); Reset(); }
        public override void OnPop() { Clear(); Reset(); }

        private void Reset()
        {
            _builtVm = null;
            _builtCue = null;
            _spokenCue = null;
            _fadeSub?.Dispose();
            _fadeSub = null;
            _subCtx = null;
            _dialogFaded = false;
        }

        // Escape opens the game's pause menu, exactly like the game's own Esc during a conversation —
        // required for save/load/quit/settings mid-dialogue. Without this the dialogue screen swallows
        // Escape (it owns ui.back). DialogVM.HandleShowEscMenu raises this same event, so it's safe.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "hud.game_menu"),
                _ => EventBus.RaiseEvent<IEscMenuHandler>(h => h.HandleOpen()));
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;

            // Shadow the fade command on whatever context is live; re-subscribe on a context swap (area
            // change), and reset the flag — the fade actions only ever target the surface context, so in
            // space the command never fires and _dialogFaded stays false.
            var ctx = Context();
            if (!ReferenceEquals(ctx, _subCtx))
            {
                _fadeSub?.Dispose();
                _fadeSub = null;
                _dialogFaded = false;
                _subCtx = ctx;
                if (ctx != null)
                    _fadeSub = ctx.ToggleDialogFadeCommand.Subscribe(v => _dialogFaded = v);
            }

            // A new conversation is a fresh DialogVM: force a rebuild + re-announce of its first cue.
            if (!ReferenceEquals(vm, _builtVm)) { _builtVm = vm; _builtCue = null; _spokenCue = null; }

            var cue = vm.Cue.Value;
            if (cue == null) return;

            if (!ReferenceEquals(cue, _builtCue)) { _builtCue = cue; Rebuild(vm); }

            // Speak once delivered: panel visible and not faded. Once per cue, QUEUED (never interrupting).
            if (!ReferenceEquals(cue, _spokenCue) && vm.IsVisible.Value && !_dialogFaded)
            {
                _spokenCue = cue;
                var line = DialogText.BuildCueLine(cue, includeSpeaker: true);
                if (!string.IsNullOrEmpty(line)) Tts.Speak(line, interrupt: false);
            }
        }

        private void Rebuild(DialogVM vm)
        {
            Clear();

            var sheet = new FlowSheet();
            var log = sheet.List(null); // unlabeled — region entry stays quiet
            UIElement focus = null;

            // Scrollback: RT's History is a ReactiveCollection<IDialogShowData>, rendered via GetText(colors)
            // (one item per past cue / chosen answer). Guarded per-item so a bad entry can't drop the cue.
            var colors = Kingmaker.Blueprints.Root.UIConfig.Instance?.DialogColors;
            if (colors != null)
                foreach (var d in vm.History)
                {
                    string text;
                    try { text = TextUtil.StripRichText(d.GetText(colors)); }
                    catch { continue; }
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var t = text;
                    var row = new TextElement(t);
                    log.Item(row);
                    if (focus == null) focus = row;
                }

            // The live cue line — focus here to repeat it; Down reaches the answers, Up scrolls back.
            var cueRow = new TextElement(() => DialogText.BuildCueLine(vm.Cue.Value, includeSpeaker: true));
            log.Item(cueRow);
            focus = cueRow;

            // Answers: the real list, else the single system Continue (when only that way forward exists).
            var answers = sheet.List(null);
            int count = 0;
            var list = vm.Answers.Value;
            if (list != null && list.Count > 0)
            {
                foreach (var a in list)
                    if (a != null) { answers.Item(DialogAnswerButton.For(a)); count++; }
            }
            else if (vm.SystemAnswer.Value != null)
            {
                answers.Item(DialogAnswerButton.For(vm.SystemAnswer.Value));
                count++;
            }
            if (count == 0) sheet.RemoveRegion(answers);

            sheet.Reflow();
            Add(sheet);
            Navigation.Attach(this);
            if (focus == null) focus = sheet.FirstFocusable();
            // Land on the current line silently (the cue announcement speaks it): Down reaches the answers.
            if (focus != null) Navigation.Focus(focus, announce: false);
        }
    }
}
