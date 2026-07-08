using System;
using System.Collections.Generic;
using HarmonyLib;                              // DialogChoiceGuard (OnChooseAnswer arbitration)
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Dialog;        // DialogContextVM
using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog; // DialogVM, CueVM, AnswerVM
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplateSkillCheckResult
using Kingmaker.DialogSystem.Blueprints;       // BlueprintCue (stable cue identity)
using Kingmaker.PubSubSystem.Core;             // EventBus, IEscMenuHandler
using Owlcat.Runtime.UI.Tooltips;              // TooltipBaseTemplate
using RTAccess.Accessibility;                  // DialogText.BuildCueLine, TooltipReader, GlossaryLinks
using RTAccess.UI;
using RTAccess.UI.Graph;
using RTAccess.UI.Proxies;                     // DialogChoiceGate
using UniRx;

namespace RTAccess.Screens
{
    /// <summary>
    /// An in-game conversation (the common <see cref="DialogVM"/>) as ONE graph stop that reads like a
    /// transcript: the scrollback (the game's own pre-formatted <c>DialogVM.History</c> lines — past cues and
    /// chosen answers), then the CURRENT cue row, then the answers. A new cue re-keys the cue node and
    /// <see cref="OnUpdate"/> points focus at it SILENTLY — you hear the line via the cue announcement, press
    /// Down for the answers/Continue, or Up to re-read earlier lines.
    ///
    /// Graph-native and IMMEDIATE MODE: declared fresh from the live VM each render, so new cues / history
    /// appear without any rebuild bookkeeping. We speak a line only once it is actually delivered: the dialogue
    /// panel is visible (<see cref="DialogVM.IsVisible"/> — the engine drops it during cutscene/escmenu/pause,
    /// RT's replacement for WOTR's <c>m_CutsceneScheduled</c>) AND not faded behind a designer mid-cue black
    /// screen (<see cref="DialogContextVM.ToggleDialogFadeCommand"/>, shadowed into <see cref="_dialogFaded"/>).
    /// Each cue is announced once, QUEUED — dialogue never interrupts speech. Book events / epilogues are a
    /// separate screen. The transient notification block (reputation shifts, item/XP/Profit Factor gains,
    /// companion ability/buff gains) is NOT read here: those flow through the game's log and are voiced by
    /// <see cref="RTAccess.Accessibility.LogTap"/> — conviction (soul-mark) shifts, the one thing the game never
    /// logs, by <see cref="RTAccess.Accessibility.ConvictionEvents"/>.
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

        private DialogVM _convVm;  // the conversation the focus/speak markers belong to (a fresh VM per conversation)
        // Cues are tracked by their stable BlueprintCue identity, NOT the CueVM instance: DialogVM.HandleOnCueShow
        // builds a fresh CueVM per fire and can re-fire for the same cue at dialog start, so instance-keyed dedup
        // would re-home and re-announce the first cue twice. The CueVM instance is the fallback key for
        // string-only cues that carry no BlueprintCue.
        private CueVM _focusedCue;          // instance focus was last homed to (fallback key)
        private BlueprintCue _focusedCueBp; // blueprint focus was last homed to (primary key)
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

        public override void OnPush() { Reset(); }

        // A hide (cutscene gap / pause menu) POPS us with KeepStateOnPop=true. Clear ONLY the focus marker so the
        // next OnUpdate re-homes focus to the CURRENT cue (WA ff35982 — otherwise re-showing lands on the oldest
        // transcript row); keep the spoken marker so the cue isn't re-read on a mere hide. A real close (the
        // conversation ended, Vm()==null) fully resets.
        public override void OnPop()
        {
            _focusedCue = null; _focusedCueBp = null;
            if (Vm() == null) Reset();
        }

        private void Reset()
        {
            _convVm = null;
            _focusedCue = null;
            _focusedCueBp = null;
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

            // A new conversation is a fresh DialogVM: force a re-home + re-announce of its first cue.
            if (!ReferenceEquals(vm, _convVm))
            {
                _convVm = vm;
                _focusedCue = null; _focusedCueBp = null;
                _spokenCue = null; _spokenCueBp = null;
            }

            var cue = vm.Cue.Value;
            if (cue == null) return;

            // On a new cue (or after a hide cleared the focus marker), point focus at the cue node SILENTLY —
            // the delivery announcement below is the speech, and the frame differ would otherwise double-speak.
            // FocusNode is a request the navigator applies once the cue node is in the render (Build declares
            // it live this frame). announce:false always here.
            if (!SameCue(cue, _focusedCue, _focusedCueBp))
            {
                _focusedCue = cue; _focusedCueBp = cue.BlueprintCue;
                Navigation.Active?.FocusNode(CueId(vm, cue), announce: false);
            }

            // Speak once delivered: panel visible and not faded. Once per cue, QUEUED (never interrupting).
            if (!SameCue(cue, _spokenCue, _spokenCueBp) && vm.IsVisible.Value && !_dialogFaded)
            {
                _spokenCue = cue; _spokenCueBp = cue.BlueprintCue;
                var line = DialogText.BuildCueLine(cue, includeSpeaker: true);
                if (!string.IsNullOrEmpty(line)) Tts.Speak(line, interrupt: false);
            }

            TryNumberSelect(vm);
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var cue = vm.Cue.Value;
            if (cue == null) return; // the VM can exist a frame before the first cue is pushed

            string k = ConvKey(vm);

            // One silent, positions-off transcript scope (a transcript, not a list — "37 of 40" per line would
            // be noise): the scrollback, then the live cue row, then the answers — all arrow-navigable top to
            // bottom (Down reaches the answers, Up scrolls back).
            b.PushContext("", role: null, positions: false);

            // Scrollback: RT's History (ReactiveCollection<IDialogShowData>, one entry per past cue / chosen
            // answer), rendered via GetText(colors), rich-text stripped. ABSOLUTE-INDEX keys (stable as history
            // appends); guarded per-item so a bad entry can't drop the cue.
            var colors = Kingmaker.Blueprints.Root.UIConfig.Instance?.DialogColors;
            if (colors != null)
            {
                int i = 0;
                foreach (var d in vm.History)
                {
                    int idx = i++;
                    string text;
                    try { text = TextUtil.StripRichText(d.GetText(colors)); }
                    catch { continue; }
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var captured = text;
                    b.AddItem(ControlId.Structural(k + "row:" + idx), GraphNodes.Text(() => captured));
                }
            }

            // Whether real player choices exist this cue; drives the cue-row Enter-through shortcut and the
            // answers region. The live cue row is keyed PER CUE so a new cue re-keys the node (OnUpdate re-homes
            // focus to it).
            var list = vm.Answers.Value;
            bool hasRealAnswers = list != null && list.Count > 0;
            b.AddItem(CueId(vm, cue), CueNode(vm, hasRealAnswers));

            // Answers: the real player list, else the single system Continue (when only that way forward exists).
            // Keys carry the cue identity, so a new cue drops the old answers and adds the new. Each activation
            // routes through DialogChoiceGate; enabled folds the deliverable gate (visible, not faded) so a
            // queued Enter can't fire into a cutscene-hidden cue.
            Func<bool> deliverable = () => vm.IsVisible.Value && !_dialogFaded;
            string ansPrefix = k + "cue:" + CueKey(cue) + ":ans:";
            if (hasRealAnswers)
            {
                int ai = 0;
                foreach (var a in list)
                {
                    if (a == null) continue;
                    b.AddItem(ControlId.Referenced(a, ansPrefix + ai++), DialogNodes.AnswerNode(a, deliverable));
                }
            }
            else if (vm.SystemAnswer.Value != null)
            {
                b.AddItem(ControlId.Referenced(vm.SystemAnswer.Value, ansPrefix + "sys"),
                    DialogNodes.AnswerNode(vm.SystemAnswer.Value, deliverable));
            }

            b.PopContext();
        }

        // The live cue line as its own graph node. Its label is the spoken cue line (focus here to repeat it;
        // Down reaches the answers, Up scrolls back). Space drills the skill-check roll breakdown + inline
        // glossary terms. Enter-through-exposition: when the ONLY way forward is the system Continue (no real
        // answers) and the window is deliverable, Enter here presses it — so a blind player holds Enter through
        // exposition instead of arrowing to Continue every line. RT's OnChooseAnswer is silent, so we keep the
        // default click sound for feedback (unlike WOTR, whose OnChooseAnswer plays its own).
        private NodeVtable CueNode(DialogVM vm, bool hasRealAnswers)
        {
            Func<string> line = () => DialogText.BuildCueLine(vm.Cue.Value, includeSpeaker: true);

            var sys = vm.SystemAnswer.Value;
            bool canContinue = !hasRealAnswers && sys != null && sys.Enable.Value && vm.IsVisible.Value && !_dialogFaded;
            Action activate = canContinue ? (Action)(() => DialogChoiceGate.Choose(sys)) : null;

            return new NodeVtable
            {
                ControlType = ControlTypes.Text, // a narrative line — no role word, like the old CueRow TextElement
                Announcements = new List<NodeAnnouncement> { GraphNodes.LabelPart(line) },
                SearchText = line,
                OnActivate = activate,
                OnTooltip = () => OpenCueTooltip(vm),
                ActivateSound = activate != null
                    ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick
                    : null,
            };
        }

        // Space on the cue: the skill-check roll breakdown (per acting unit: stat total, DC, d100/chance,
        // pass-fail) via the game's own template, PLUS any inline glossary <link> terms in the RAW (markup-intact)
        // cue text — through the shared chooser, exactly what an item/answer Space press offers. The spoken cue
        // line is rich-text-stripped (no links), so RawText is the link source.
        private static void OpenCueTooltip(DialogVM vm)
        {
            var cue = vm.Cue.Value;
            if (cue == null) { TooltipChooser.Open(null, null, sections: null, links: null); return; }
            var checks = cue.SkillChecks;
            TooltipBaseTemplate tpl = checks != null && checks.Count > 0
                ? new TooltipTemplateSkillCheckResult(checks, Array.Empty<string>())
                : null;
            var body = tpl != null ? TooltipReader.GetFull(tpl) : null;
            var links = GlossaryLinks.Gather(cue.RawText);
            TooltipChooser.Open(DialogText.BuildCueLine(cue, includeSpeaker: false), body, sections: null, links: links);
        }

        // ---- identity keys (Build + OnUpdate must agree) ----
        private static string ConvKey(DialogVM vm) => "dlg:" + vm.GetHashCode() + ":";
        private static string CueKey(CueVM cue)
            => cue.BlueprintCue != null ? cue.BlueprintCue.GetHashCode().ToString() : cue.GetHashCode().ToString();
        private static ControlId CueId(DialogVM vm, CueVM cue)
            => ControlId.Referenced(cue, ConvKey(vm) + "cue:" + CueKey(cue));

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

        private static void SelectByNumber(IEnumerable<AnswerVM> list, int number)
        {
            AnswerVM match = null;
            foreach (var a in list)
                if (a != null && a.Index == number) { match = a; break; }
            if (match == null) return; // no answer with that number this cue
            var text = DialogNodes.AnswerText(match); // Tts strips the rich-text tags
            if (!match.Enable.Value) // shown but locked (failed condition / skill check) — say so, don't fire
            {
                Tts.Speak(text + ", " + Loc.T("state.disabled"), interrupt: true);
                return;
            }
            // Confirm the pick (interrupt), then advance — the response cue queues after via OnUpdate.
            if (!string.IsNullOrEmpty(text)) Tts.Speak(text, interrupt: true);
            DialogChoiceGate.Choose(match);
        }

        // True when this cue is the one already homed/announced, keyed on the stable BlueprintCue (falling back
        // to the CueVM instance for string-only cues). Guards against HandleOnCueShow re-firing a fresh CueVM for
        // the same line at dialog start, which would otherwise re-home and double-announce the first cue.
        private static bool SameCue(CueVM cue, CueVM instance, BlueprintCue bp)
            => cue.BlueprintCue != null ? ReferenceEquals(cue.BlueprintCue, bp) : ReferenceEquals(cue, instance);
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
