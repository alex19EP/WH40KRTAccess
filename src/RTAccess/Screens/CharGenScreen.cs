using System.Linq;
using Kingmaker;
using Kingmaker.UI.MVVM.VM.CharGen;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.View.CharGen.Common;
using RTAccess.UI;
using Access.Core.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// Character generation (CharGenVM) on the shared graph-native <see cref="WizardScreen"/> shell: the roadmap
    /// strip (one live entry per phase, each a jump target) as the leading Tab-stop (<see cref="BuildLead"/>),
    /// the current phase's content under the phase name as context (<see cref="BuildContent"/>), then Back/Next
    /// stops — the wizard shell owns the phase-change detector (page-turn + focus re-seat), the "wiz:" phase
    /// keys, the footer, InitialFocusStop=content, and Wrap. Reached from the main menu (new game) OR in play
    /// (hiring a custom companion at the Factotum) — the same <c>CharGenVM</c> shape, but hosted by a different
    /// context each time (see <see cref="Vm"/>). Next advances the phase (or Complete on the last), Back
    /// retreats (or Close on the first); Next also allows the game's required naming dialogs. The phase SET is
    /// dynamic (picking custom adds Homeworld/Occupation/Career/… phases) — immediate mode just renders the live
    /// collection. Per-phase content comes from <see cref="CharGenPhaseContent"/>; a phase change plays the
    /// game's page-turn and lands focus on the new page's content (shell behaviour), while
    /// <see cref="RTAccess.Accessibility.CharGenAnnounce"/> (the Harmony postfix on the game view's phase change)
    /// speaks the orientation line.
    /// </summary>
    public sealed class CharGenScreen : WizardScreen
    {
        public override string Key => "ctx.chargen";
        // Full-screen flow above the menu/in-game contexts + service windows. 16, not 15: the in-play hire is
        // raised FROM a Factotum conversation, so the chargen must outrank DialogueScreen (15) — the stack
        // sort is stable and dialogue registers later, so a tie left the dialogue focused over the wizard.
        // Exclusive for the same reason: while the wizard is up the keyboard is its (the conversation and
        // the HUD beneath must not keep answering keys).
        public override int Layer => 16;
        public override bool Exclusive => true;
        // No ScreenName — the content context is labeled with the current phase's name.

        /// <summary>The live chargen, whichever context hosts it: the main menu's <c>CharGenContextVM</c> for a new
        /// game, the surface static part's for the in-play custom-companion hire (<c>MainMenuVM</c> is null in play,
        /// <c>SurfaceVM</c> in the menu — the game's own <c>RootUIContext.IsChargenShown</c> checks both).</summary>
        internal static CharGenVM Vm()
        {
            var rc = Game.Instance?.RootUiContext;
            if (rc == null) return null;
            return rc.SurfaceVM?.StaticPartVM?.CharGenContextVM?.CharGenVM?.Value
                ?? rc.MainMenuVM?.CharGenContextVM?.CharGenVM?.Value;
        }

        private static CharGenPhaseBaseVM CurrentPhaseVm() => Vm()?.CurrentPhaseVM.Value;

        protected override object WizardVm() => Vm();
        protected override object CurrentPhase() => CurrentPhaseVm();
        protected override string PhaseLabel() => CurrentPhaseVm()?.PhaseName?.Value ?? "";

        // The roadmap strip: one entry per phase, read LIVE from PhasesCollection each render — the set
        // changing (picking custom adds phases, a homeworld adds a child phase) just shows up. Leading stop, so
        // it stays first in Tab order while the shell lands initial focus on the content.
        protected override void BuildLead(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            b.BeginStop("roadmap").PushContext(Loc.T("chargen.steps"), Loc.T("role.list"));
            int i = 0;
            foreach (var p in vm.PhasesCollection)
            {
                if (p == null) { i++; continue; }
                b.AddItem(ControlId.Referenced(p, "cg:step:" + i), CharGenNodes.RoadmapEntry(p));
                i++;
            }
            b.PopContext();
        }

        protected override void BuildContent(GraphBuilder b, string k)
        {
            var phase = CurrentPhaseVm();

            // Make sure the phase the player is WORKING IN is in detailed view. The game's phase VMs gate
            // their mechanic sync on IsInDetailedView (background phases only bind the level-up manager —
            // and so only materialize their items and commit selections — inside OnBeginDetailedView), and
            // the flag only flips true when the game's OWN detailed view binds, which can lag (or never
            // happen) under a parallel UI. BeginDetailedView is exactly what the real view's bind calls;
            // gated so it runs only while the game hasn't already done it.
            if (phase != null && !phase.IsInDetailedView.Value) phase.BeginDetailedView();

            var content = CharGenPhaseContent.For(phase);
            if (content != null) content.Build(b, k);
            else CharGenPhaseContent.EmitUnavailable(b, k);
        }

        protected override void OnBack()
        {
            var vm = Vm();
            if (vm == null) return;
            // Mirrors the game's view: first phase → close chargen (back to the New Game wizard); otherwise
            // step back a phase.
            if (IsFirstPhase(vm)) vm.Close();
            else vm.PhasesSelectionGroupRadioVM.SelectPrevValidEntity();
        }

        protected override void OnNext()
        {
            var vm = Vm();
            if (vm == null) return;
            // The view owns the incomplete-phase prompt, its completion callback, and delayed advancement.
            // Calling the selection group directly skips that flow; requiring completion first makes the
            // ship-name prompt unreachable through Next. Resolve only on activation, for this exact VM.
            foreach (var view in UnityEngine.Object.FindObjectsByType<CharGenView>(
                UnityEngine.FindObjectsSortMode.None))
            {
                if (view == null || !view.isActiveAndEnabled || !ReferenceEquals(view.ViewModel, vm)) continue;
                view.NextPressed();
                return;
            }
            Main.Log?.Warning("CharGen Next: no active game view bound to the current character creation.");
        }

        // "Complete" only on the last phase; otherwise "Next".
        protected override string NextLabel() =>
            IsLastPhase(Vm()) ? Loc.T("chargen.complete") : Loc.T("wizard.next");

        protected override bool NextEnabled()
        {
            var vm = Vm();
            return vm != null && (vm.CurrentPhaseIsCompleted.Value || vm.CurrentPhaseCanInterrupt);
        }

        // The phase's own "what's still missing" hint, which the game shows on this button
        // (CharGenPCView: phase.PhaseNextHint → m_NextButton.SetHint). Deliberately NOT
        // NotCompletedReasonTooltip — that is a single generic "this stage is not completed" string shown
        // only on the console path. Only a couple of phases author a hint, so this is usually null.
        protected override Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate NextTooltip()
        {
            var hint = CurrentPhaseVm()?.PhaseNextHint?.Value;
            return string.IsNullOrWhiteSpace(hint)
                ? null
                : new Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates.TooltipTemplateSimple(NextLabel(), hint);
        }

        private static bool IsLastPhase(CharGenVM vm) =>
            vm != null && ReferenceEquals(vm.CurrentPhaseVM.Value, vm.PhasesCollection.LastOrDefault());

        private static bool IsFirstPhase(CharGenVM vm) =>
            vm != null && ReferenceEquals(vm.CurrentPhaseVM.Value, vm.PhasesCollection.FirstOrDefault());
    }
}
