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
    /// An in-game conversation (the common <see cref="DialogVM"/>) on the shared
    /// <see cref="TranscriptScreen{TVm,TId}"/> shell — ONE graph stop that reads like a transcript: the
    /// scrollback (the game's own pre-formatted <c>DialogVM.History</c> lines — past cues and chosen answers),
    /// then the CURRENT cue row, then the answers. A new cue re-keys the cue node and the shell points focus at
    /// it SILENTLY — you hear the line via the cue announcement, press Down for the answers/Continue, or Up to
    /// re-read earlier lines.
    ///
    /// IMMEDIATE MODE: declared fresh from the live VM each render, so new cues / history appear without any
    /// rebuild bookkeeping. We speak a line only once it is actually delivered: the dialogue panel is visible
    /// (<see cref="DialogVM.IsVisible"/> — the engine drops it during cutscene/escmenu/pause, RT's replacement
    /// for WOTR's <c>m_CutsceneScheduled</c>) AND not faded behind a designer mid-cue black screen
    /// (<see cref="DialogContextVM.ToggleDialogFadeCommand"/>, shadowed into <see cref="_dialogFaded"/>) — the
    /// shell's <see cref="Deliverable"/> gate. Each cue is announced once, QUEUED — dialogue never interrupts
    /// speech. Book events / epilogues are a separate screen. The transient notification block (reputation
    /// shifts, item/XP/Profit Factor gains, companion ability/buff gains) is NOT read here: those flow through
    /// the game's log and are voiced by <see cref="RTAccess.Accessibility.LogTap"/> — conviction (soul-mark)
    /// shifts, the one thing the game never logs, by <see cref="RTAccess.Accessibility.ConvictionEvents"/>.
    /// </summary>
    public sealed class DialogueScreen : TranscriptScreen<DialogVM, CueVM>
    {
        public override string Key => "ctx.dialogue";
        public override string ScreenName => Loc.T("screen.dialog");

        private DialogContextVM _subCtx; // the context our fade subscription belongs to
        private IDisposable _fadeSub;
        private bool _dialogFaded;

        // The conversation the focus/speak markers belong to (a fresh DialogVM per conversation). The shell's
        // _focused/_spoken markers key on cue identity; this latches the conversation so a fresh VM re-homes.
        private DialogVM _convVm;

        protected override DialogVM Vm() => Context()?.DialogVM?.Value; // == RootUiContext.HasDialog

        // Escape opens the game's pause menu, exactly like the game's own Esc during a conversation —
        // required for save/load/quit/settings mid-dialogue. Without this the dialogue screen swallows
        // Escape (it owns ui.back). DialogVM.HandleShowEscMenu raises this same event, so it's safe.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "hud.game_menu"),
                _ => EventBus.RaiseEvent<IEscMenuHandler>(h => h.HandleOpen()));
        }

        // Pre-identity hook: shadow the fade command on whatever context is live; re-subscribe on a context swap
        // (area change), and reset the flag — the fade actions only ever target the surface context, so in space
        // the command never fires and _dialogFaded stays false. Then, on a fresh conversation VM, clear the
        // shell's markers so its first cue re-homes + re-announces.
        protected override void PreUpdate(DialogVM vm)
        {
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

            if (!ReferenceEquals(vm, _convVm))
            {
                _convVm = vm;
                _focused = null;
                _spoken = null;
            }
        }

        protected override CueVM Identity(DialogVM vm) => vm.Cue.Value;

        // Delivered only when the panel is visible and not faded — a queued Enter can't fire (nor a line be
        // spoken) into a cutscene-hidden cue.
        protected override bool Deliverable(DialogVM vm) => vm.IsVisible.Value && !_dialogFaded;

        protected override ControlId HomeNode(DialogVM vm, CueVM cue) => CueId(vm, cue);

        protected override void SpeakLine(DialogVM vm, CueVM cue)
        {
            var line = DialogText.BuildCueLine(cue, includeSpeaker: true);
            if (!string.IsNullOrEmpty(line)) Tts.Speak(line, interrupt: false);
        }

        protected override void PostUpdate(DialogVM vm) => TryNumberSelect(vm);

        // True when this cue is the one already homed/announced, keyed on the stable BlueprintCue (falling back
        // to the CueVM instance for string-only cues). Guards against DialogVM.HandleOnCueShow re-firing a fresh
        // CueVM for the same line at dialog start, which would otherwise re-home and double-announce the first
        // cue. The marker CueVM still carries its own BlueprintCue, so no separate blueprint field is needed.
        protected override bool SameId(CueVM a, CueVM b)
        {
            if (b == null) return false;
            return a.BlueprintCue != null ? ReferenceEquals(a.BlueprintCue, b.BlueprintCue) : ReferenceEquals(a, b);
        }

        protected override void Reset()
        {
            base.Reset(); // clears the shell's _focused / _spoken markers
            _convVm = null;
            _fadeSub?.Dispose();
            _fadeSub = null;
            _subCtx = null;
            _dialogFaded = false;
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
