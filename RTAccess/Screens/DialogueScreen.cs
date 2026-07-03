using System;
using System.Collections.Generic;
using HarmonyLib;                              // DialogChoiceGuard (OnChooseAnswer arbitration)
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Dialog;        // DialogContextVM
using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog; // DialogVM, CueVM, AnswerVM
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplateSkillCheckResult
using Kingmaker.DialogSystem.Blueprints;       // BlueprintCue (stable cue identity)
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
    /// Book events / epilogues are a separate screen. The transient notification block (reputation shifts,
    /// item/XP/Profit Factor gains, companion ability/buff gains) is NOT read here: those flow through the
    /// game's log and are voiced by <see cref="RTAccess.Accessibility.LogTap"/> — conviction (soul-mark)
    /// shifts, the one thing the game never logs, by <see cref="RTAccess.Accessibility.ConvictionEvents"/>.
    /// </summary>
    public sealed class DialogueScreen : Screen
    {
        public override string Key => "ctx.dialogue";
        public override string ScreenName => Loc.T("screen.dialog");
        public override int Layer => 15; // over the in-game context + service windows
        // Dialogue "pops" without closing (it hides during cutscene gaps / under the pause menu) —
        // keep the nav state so focus survives the gap.
        public override bool KeepStateOnPop => true;

        private DialogContextVM _subCtx; // the context our fade subscription belongs to
        private IDisposable _fadeSub;
        private bool _dialogFaded;

        private DialogVM _builtVm;  // the conversation the tree belongs to (a fresh VM per conversation)
        // Cues are tracked by their stable BlueprintCue identity, NOT the CueVM instance: DialogVM.HandleOnCueShow
        // builds a fresh CueVM per fire and can re-fire for the same cue at dialog start, so instance-keyed dedup
        // would rebuild and re-announce the first cue twice. The CueVM instance is the fallback key for
        // string-only cues that carry no BlueprintCue.
        private CueVM _builtCue;            // instance the sheet was built for (fallback key)
        private BlueprintCue _builtCueBp;   // blueprint the sheet was built for (primary key)
        private CueVM _spokenCue;           // instance we've announced (fallback key)
        private BlueprintCue _spokenCueBp;  // blueprint we've announced (primary key)

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
            _builtCueBp = null;
            _spokenCue = null;
            _spokenCueBp = null;
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
            if (!ReferenceEquals(vm, _builtVm))
            {
                _builtVm = vm;
                _builtCue = null; _builtCueBp = null;
                _spokenCue = null; _spokenCueBp = null;
            }

            var cue = vm.Cue.Value;
            if (cue == null) return;

            if (!SameCue(cue, _builtCue, _builtCueBp)) { _builtCue = cue; _builtCueBp = cue.BlueprintCue; Rebuild(vm); }

            // Speak once delivered: panel visible and not faded. Once per cue, QUEUED (never interrupting).
            if (!SameCue(cue, _spokenCue, _spokenCueBp) && vm.IsVisible.Value && !_dialogFaded)
            {
                _spokenCue = cue; _spokenCueBp = cue.BlueprintCue;
                var line = DialogText.BuildCueLine(cue, includeSpeaker: true);
                if (!string.IsNullOrEmpty(line)) Tts.Speak(line, interrupt: false);
            }

            TryNumberSelect(vm);
        }

        // Number-key quick-select: a bare 1..9 (top row or numpad) picks the answer carrying that displayed
        // number, from anywhere in the transcript — you don't have to arrow down to it. The game's own
        // DialogChoiceN bindings are blocked by DialogChoiceGuard, so this is the sole number path; it routes
        // through DialogChoiceGate so the guard counts it as an owned selection. Confirmation-first (you didn't
        // hear the answer by landing on it): speak the chosen line, then advance. Bare digits only — Alt+digit
        // is party-member select, Ctrl/Shift belong to other chords — and only while the panel is deliverable
        // (visible, not faded) so a stray digit can't fire into a cutscene-hidden cue. Continue-only cues have
        // no numbered answers, so digits no-op there (Enter advances them).
        private void TryNumberSelect(DialogVM vm)
        {
            if (!FocusMode.Active || !vm.IsVisible.Value || _dialogFaded) return;
            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftAlt) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightAlt)
                || UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftControl) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightControl))
                return;
            var list = vm.Answers.Value;
            if (list == null || list.Count == 0) return;
            for (int n = 1; n <= 9; n++)
            {
                if (!UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Alpha0 + n)
                    && !UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Keypad0 + n)) continue;
                SelectByNumber(list, n);
                return;
            }
        }

        private static void SelectByNumber(System.Collections.Generic.IEnumerable<AnswerVM> list, int number)
        {
            AnswerVM match = null;
            foreach (var a in list)
                if (a != null && a.Index == number) { match = a; break; }
            if (match == null) return; // no answer with that number this cue
            var text = RTAccess.UI.Proxies.DialogAnswerButton.AnswerText(match); // Tts strips the rich-text tags
            if (!match.Enable.Value) // shown but locked (failed condition / skill check) — say so, don't fire
            {
                Tts.Speak(text + ", " + Loc.T("state.disabled"), interrupt: true);
                return;
            }
            // Confirm the pick (interrupt), then advance — the response cue queues after via OnUpdate.
            if (!string.IsNullOrEmpty(text)) Tts.Speak(text, interrupt: true);
            RTAccess.UI.Proxies.DialogChoiceGate.Choose(match);
        }

        // True when this cue is the one already built/announced, keyed on the stable BlueprintCue (falling back
        // to the CueVM instance for string-only cues). Guards against HandleOnCueShow re-firing a fresh CueVM for
        // the same line at dialog start, which would otherwise rebuild and double-announce the first cue.
        private static bool SameCue(CueVM cue, CueVM instance, BlueprintCue bp)
            => cue.BlueprintCue != null ? ReferenceEquals(cue.BlueprintCue, bp) : ReferenceEquals(cue, instance);

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

            // Whether real player choices exist this cue; drives the cue-row Enter-through shortcut below and the
            // answers region. Captured at build — answers change only with the cue, which forces a rebuild.
            var list = vm.Answers.Value;
            bool hasRealAnswers = list != null && list.Count > 0;

            // The live cue line — focus here to repeat it; Down reaches the answers, Up scrolls back. Two extras:
            // (1) a drill-in tooltip (Space): the skill-check roll breakdown (per acting unit: stat total, DC,
            //     d100/chance, pass-fail) via the game's own template — the same drill path answer buttons use;
            // (2) Enter-through-exposition: when the ONLY way forward is the system Continue (no real answers),
            //     Enter here presses it, so a blind player holds Enter through exposition instead of arrowing to
            //     Continue every line. Gated on the window being deliverable; real answer sets never ride it.
            var cueRow = new CueRow(
                () => DialogText.BuildCueLine(vm.Cue.Value, includeSpeaker: true),
                () => hasRealAnswers ? null : vm.SystemAnswer.Value,
                () => vm.IsVisible.Value && !_dialogFaded,
                () =>
                {
                    var checks = vm.Cue.Value?.SkillChecks;
                    return checks != null && checks.Count > 0
                        ? new TooltipTemplateSkillCheckResult(checks, Array.Empty<string>())
                        : null;
                },
                // Raw cue text (markup intact) — the source for inline glossary <link> extraction. The spoken
                // line (arg 1) is rich-text-STRIPPED, so it carries no links; RawText is engine-expanded and does.
                () => vm.Cue.Value?.RawText);
            log.Item(cueRow);
            focus = cueRow;

            // Answers: the real list, else the single system Continue (when only that way forward exists). The
            // Continue button stays even though the cue row can now activate it — either path advances the line.
            var answers = sheet.List(null);
            int count = 0;
            if (hasRealAnswers)
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

        // The live cue line as its own element. It carries the skill-check drill-in tooltip (via the base
        // TextElement) and — when the only way forward is the system Continue (no real answers) — presses it
        // straight from the cue row (Enter-through-exposition). Gated on the window being deliverable (visible,
        // not faded) so a queued Enter can't spam-advance a cutscene-hidden window. Activation calls the same
        // OnChooseAnswer the Continue button does; RT's OnChooseAnswer is silent, so we keep the default click
        // sound for feedback (unlike WOTR, whose OnChooseAnswer plays its own — hence no ActivateSound override).
        private sealed class CueRow : TextElement
        {
            private readonly Func<AnswerVM> _continueAnswer;
            private readonly Func<bool> _deliverable;
            private readonly Func<string> _rawText;

            public CueRow(Func<string> text, Func<AnswerVM> continueAnswer, Func<bool> deliverable,
                Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> tooltip, Func<string> rawText) : base(text, tooltip: tooltip)
            {
                _continueAnswer = continueAnswer;
                _deliverable = deliverable;
                _rawText = rawText;
            }

            // The spoken cue line is rich-text-stripped (no links); expose the RAW cue text so Space can surface
            // the inline glossary terms (see RTAccess.Accessibility.GlossaryLinks / the tooltip-key drill menu).
            public override string GetLinkSourceText() => _rawText != null ? _rawText() : null;

            public override IEnumerable<ElementAction> GetActions()
            {
                var a = _continueAnswer();
                if (a != null && a.Enable.Value && _deliverable())
                    yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.choose"),
                        _ => RTAccess.UI.Proxies.DialogChoiceGate.Choose(a));
            }
        }
    }

    /// <summary>
    /// Answer-selection arbitration. The game's own dialogue view stays live under our parallel tree and reacts
    /// to Unity's EventSystem "Submit" (Enter) on its currently-selected button — a path parallel to BOTH our
    /// navigator and KeyboardAccess (so keyboard-chord suppression can't catch it). That let the SAME Enter that
    /// dismissed a tutorial popped over a cue fire <see cref="AnswerVM.OnChooseAnswer"/> and pick an answer the
    /// player never navigated to (observed: a choice one frame after the dismiss, advancing the cue). It also
    /// double-fired every normal selection (our proxy, then the game's view a frame later — usually a stale
    /// no-op). We are the sole answer authority while a dialogue is live under focus mode: allow only the call
    /// our tree initiates (<see cref="RTAccess.UI.Proxies.DialogChoiceGate"/>) and block every other source.
    /// Off (fail-open) when focus mode is off (vanilla mouse/keyboard) or no dialogue is up (book event / epilog
    /// contexts we don't drive), so this never bricks selection outside the case it guards.
    /// </summary>
    [HarmonyPatch(typeof(AnswerVM), nameof(AnswerVM.OnChooseAnswer))]
    internal static class DialogChoiceGuard
    {
        private static bool Prefix()
        {
            if (!FocusMode.Active) return true;                    // vanilla — game / mouse drives
            if (RTAccess.UI.Proxies.DialogChoiceGate.MineNow) return true; // our tree's own sanctioned selection
            try { if (Game.Instance?.RootUiContext?.HasDialog != true) return true; } // not a dialogue we own
            catch { return true; }
            // A dialogue is on our tree and this choice did NOT come from our navigator — it's the game's own
            // Submit/click path acting on the same key. Block it; our tree is the sole answer path.
            return false;
        }
    }
}
