using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.UI.MVVM.VM.CharGen;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// Character generation (CharGenVM) on the shared <see cref="WizardScreen"/> shell. Reached from the
    /// main menu (new game) — and, later, in-game for level-up/respec (same VM, different host). Next
    /// advances the phase (or Complete on the last), Back retreats (or Close on the first); Next is gated
    /// by the current phase's completion. A roadmap strip above the content lists every phase (name,
    /// state, live summary) and is a jump target. The phase set is dynamic (picking a custom character
    /// adds Homeworld/Occupation/Career/… phases), so the strip is polled and rebuilt in place. Per-phase
    /// content is produced by <see cref="CharGenPhaseContent"/>.
    /// </summary>
    public sealed class CharGenScreen : WizardScreen
    {
        public override string Key => "ctx.chargen";
        public override int Layer => 15; // full-screen flow: above the menu/in-game contexts + service windows
        // No ScreenName — the content panel is labeled with the current phase's name.

        private static CharGenVM Vm()
        {
            // Same VM whether reached from the main menu (new game) or, later, in-game (level-up).
            return Game.Instance?.RootUiContext?.MainMenuVM?.CharGenContextVM?.CharGenVM?.Value;
        }

        protected override object WizardVm() => Vm();
        protected override object CurrentPhase() => Vm()?.CurrentPhaseVM.Value;
        protected override string PhaseLabel() => Vm()?.CurrentPhaseVM.Value?.PhaseName.Value;

        // The current phase's content builder.
        private CharGenPhaseContent _phaseContent;

        protected override void BuildContent(Container content)
        {
            _phaseContent = CharGenPhaseContent.For(Vm()?.CurrentPhaseVM.Value);
            if (_phaseContent != null)
                _phaseContent.Build(content);
            else
                content.Add(new TextElement(() => Loc.T("chargen.phase_unavailable")));
        }

        // The roadmap strip (top of screen): one entry per phase, name + state + live summary, each a
        // jump target. Built first (above the phase content) via the WizardScreen header hook.
        private Panel _roadmapPanel;
        private List<CharGenPhaseBaseVM> _roadmapPhases;

        protected override void BuildHeader(Container root)
        {
            _roadmapPanel = new Panel();
            root.Add(_roadmapPanel);
            FillRoadmap();
        }

        private void FillRoadmap()
        {
            if (_roadmapPanel == null) return;
            _roadmapPanel.Clear();
            var phases = Vm()?.PhasesCollection;
            _roadmapPhases = phases != null ? phases.ToList() : new List<CharGenPhaseBaseVM>();
            if (_roadmapPhases.Count == 0) return;

            var list = new ListContainer(Loc.T("chargen.steps"));
            foreach (var p in _roadmapPhases)
            {
                if (p == null) continue;
                list.Add(new ProxyRoadmapEntry(p));
            }
            _roadmapPanel.Add(list);
        }

        // The phase set changes from many events (picking custom adds phases, a homeworld adds a child
        // phase, …) without the current phase changing, so poll the set each tick and rebuild the strip in
        // place when it differs. Focus isn't disturbed: those changes originate in the content, where
        // focus lives.
        protected override void OnPhaseTick()
        {
            _phaseContent?.Tick();
            if (PhaseSetChanged()) FillRoadmap();
        }

        private bool PhaseSetChanged()
        {
            var phases = Vm()?.PhasesCollection;
            int n = phases?.Count ?? 0;
            if (_roadmapPhases == null || _roadmapPhases.Count != n) return true;
            int i = 0;
            if (phases != null)
                foreach (var p in phases)
                {
                    if (!ReferenceEquals(p, _roadmapPhases[i])) return true;
                    i++;
                }
            return false;
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
                // Drive Complete() from the VM; it plays its own completion sting (and we add ours so the
                // feedback is consistent with the rest of our VM-driven activations).
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
