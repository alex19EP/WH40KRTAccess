using System.Linq;
using Kingmaker;
using Kingmaker.UI.MVVM.VM.CharGen;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// Character generation (CharGenVM), graph-native: the roadmap strip (one live entry per phase, each a
    /// jump target) as the first Tab-stop, the current phase's content under the phase name as context —
    /// content keys carry the phase, so advancing re-keys the whole page — then Back/Next stops. Reached
    /// from the main menu (new game); the same VM later hosts in-game custom-companion creation. Next
    /// advances the phase (or Complete on the last), Back retreats (or Close on the first); Next is gated
    /// by the current phase's completion. The phase SET is dynamic (picking custom adds Homeworld/
    /// Occupation/Career/… phases) — immediate mode just renders the live collection, so the old
    /// set-polling/rebuild machinery is gone. Per-phase content comes from
    /// <see cref="CharGenPhaseContent"/>; a phase change plays the game's page-turn and lands focus on the
    /// new page's content, while <see cref="RTAccess.Accessibility.CharGenAnnounce"/> (the Harmony postfix
    /// on the game view's phase change) speaks the orientation line.
    /// </summary>
    public sealed class CharGenScreen : Screen
    {
        public CharGenScreen() { Wrap = true; }

        public override string Key => "ctx.chargen";
        public override int Layer => 15; // full-screen flow: above the menu/in-game contexts + service windows
        // No ScreenName — the content context is labeled with the current phase's name.

        public override bool IsActive() => Vm() != null;

        // Opening (and every phase change) lands on the page content, not the roadmap header — the
        // roadmap stays first in Tab order.
        public override object InitialFocusStop => "content";

        private static CharGenVM Vm()
        {
            // Same VM whether reached from the main menu (new game) or, later, in-game (custom companion).
            return Game.Instance?.RootUiContext?.MainMenuVM?.CharGenContextVM?.CharGenVM?.Value;
        }

        private object _lastVm;
        private object _lastPhase;

        public override void OnPop() { _lastVm = null; _lastPhase = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            var phase = vm.CurrentPhaseVM.Value;
            if (ReferenceEquals(vm, _lastVm) && ReferenceEquals(phase, _lastPhase)) return;

            // A phase change WITHIN this wizard (Next/Back/roadmap jump) — not the initial build or a
            // VM swap. The game plays a page-turn on phase advance; our VM-level SelectNext/Prev
            // bypasses it, so play it here, and land focus on the new page (its keys changed).
            bool phaseChange = ReferenceEquals(vm, _lastVm) && _lastPhase != null;
            _lastVm = vm;
            _lastPhase = phase;
            if (phaseChange)
            {
                UiSound.PageTurn();
                Navigation.Active?.FocusStop("content");
            }
        }

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            // The roadmap strip: one entry per phase, read LIVE from PhasesCollection each render — the
            // set changing (picking custom adds phases, a homeworld adds a child phase) just shows up.
            b.BeginStop("roadmap").PushContext(Loc.T("chargen.steps"), Loc.T("role.list"));
            int i = 0;
            foreach (var p in vm.PhasesCollection)
            {
                if (p == null) { i++; continue; }
                b.AddItem(ControlId.Referenced(p, "cg:step:" + i), CharGenNodes.RoadmapEntry(p));
                i++;
            }
            b.PopContext();

            // The current phase's content, keyed by VM + phase so advancing re-keys the whole page.
            var phase = vm.CurrentPhaseVM.Value;
            string k = "wiz:" + vm.GetHashCode() + ":" + (phase != null ? phase.GetHashCode() : 0) + ":";
            b.BeginStop("content").PushContext(PhaseLabel(phase));

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
            b.PopContext();

            // Footer: Back then Next (label + availability track the current phase live). The game gives
            // these Plastick chrome sounds (CharGenPCView SetButtonsSounds), so they're intrinsic here.
            var plastick = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound;
            b.BeginStop("back").AddItem(ControlId.Structural("cg:back"),
                GraphNodes.Button(() => Loc.T("wizard.back"), OnBack,
                    hoverSound: plastick, clickSound: plastick));
            b.BeginStop("next").AddItem(ControlId.Structural("cg:next"),
                GraphNodes.Button(NextLabel, OnNext, NextEnabled,
                    hoverSound: plastick, clickSound: plastick));
        }

        private static string PhaseLabel(CharGenPhaseBaseVM phase) => phase?.PhaseName?.Value ?? "";

        private static void OnBack()
        {
            var vm = Vm();
            if (vm == null) return;
            // Mirrors the game's view: first phase → close chargen (back to the New Game wizard); otherwise
            // step back a phase.
            if (IsFirstPhase(vm)) vm.Close();
            else vm.PhasesSelectionGroupRadioVM.SelectPrevValidEntity();
        }

        private static void OnNext()
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
        private static string NextLabel() =>
            IsLastPhase(Vm()) ? Loc.T("chargen.complete") : Loc.T("wizard.next");

        private static bool NextEnabled() => Vm()?.CurrentPhaseIsCompleted.Value ?? false;

        private static bool IsLastPhase(CharGenVM vm) =>
            vm != null && ReferenceEquals(vm.CurrentPhaseVM.Value, vm.PhasesCollection.LastOrDefault());

        private static bool IsFirstPhase(CharGenVM vm) =>
            vm != null && ReferenceEquals(vm.CurrentPhaseVM.Value, vm.PhasesCollection.FirstOrDefault());
    }
}
