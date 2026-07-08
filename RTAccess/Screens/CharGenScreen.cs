using System.Linq;
using Kingmaker;
using Kingmaker.UI.MVVM.VM.CharGen;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// Character generation (CharGenVM) on the shared graph-native <see cref="WizardScreen"/> shell: the roadmap
    /// strip (one live entry per phase, each a jump target) as the leading Tab-stop (<see cref="BuildLead"/>),
    /// the current phase's content under the phase name as context (<see cref="BuildContent"/>), then Back/Next
    /// stops — the wizard shell owns the phase-change detector (page-turn + focus re-seat), the "wiz:" phase
    /// keys, the footer, InitialFocusStop=content, and Wrap. Reached from the main menu (new game); the same VM
    /// later hosts in-game custom-companion creation. Next advances the phase (or Complete on the last), Back
    /// retreats (or Close on the first); Next is gated by the current phase's completion. The phase SET is
    /// dynamic (picking custom adds Homeworld/Occupation/Career/… phases) — immediate mode just renders the live
    /// collection. Per-phase content comes from <see cref="CharGenPhaseContent"/>; a phase change plays the
    /// game's page-turn and lands focus on the new page's content (shell behaviour), while
    /// <see cref="RTAccess.Accessibility.CharGenAnnounce"/> (the Harmony postfix on the game view's phase change)
    /// speaks the orientation line.
    /// </summary>
    public sealed class CharGenScreen : WizardScreen
    {
        public override string Key => "ctx.chargen";
        public override int Layer => 15; // full-screen flow: above the menu/in-game contexts + service windows
        // No ScreenName — the content context is labeled with the current phase's name.

        private static CharGenVM Vm()
        {
            // Same VM whether reached from the main menu (new game) or, later, in-game (custom companion).
            return Game.Instance?.RootUiContext?.MainMenuVM?.CharGenContextVM?.CharGenVM?.Value;
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
            if (IsLastPhase(vm))
            {
                // Drive Complete() from the VM; the game's view plays the completion sting itself, but the
                // VM path bypasses it — replay it here (phase advances play the page-turn instead).
                vm.Complete();
                UiSound.ChargenComplete();
            }
            else vm.PhasesSelectionGroupRadioVM.SelectNextValidEntity();
        }

        // "Complete" only on the last phase; otherwise "Next".
        protected override string NextLabel() =>
            IsLastPhase(Vm()) ? Loc.T("chargen.complete") : Loc.T("wizard.next");

        protected override bool NextEnabled() => Vm()?.CurrentPhaseIsCompleted.Value ?? false;

        private static bool IsLastPhase(CharGenVM vm) =>
            vm != null && ReferenceEquals(vm.CurrentPhaseVM.Value, vm.PhasesCollection.LastOrDefault());

        private static bool IsFirstPhase(CharGenVM vm) =>
            vm != null && ReferenceEquals(vm.CurrentPhaseVM.Value, vm.PhasesCollection.FirstOrDefault());
    }
}
