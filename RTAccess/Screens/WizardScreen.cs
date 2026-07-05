using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// Shared shell for the game's phase-based wizards (the New Game setup; character generation grew
    /// its own richer shell in <see cref="CharGenScreen"/>), graph-native: the current phase's content
    /// as a Tab-stop labeled with the phase name, then Back/Next footer stops. IMMEDIATE MODE — the
    /// page is declared fresh from the live VM each render, and the content keys carry the VM + phase,
    /// so advancing re-keys the whole page. A phase change WITHIN the wizard plays the game's page-turn
    /// (our VM-level next/back bypasses the view's own) and lands focus on the new page's content;
    /// opening lands on content too (<see cref="InitialFocusStop"/>). Subclasses supply the VM, the
    /// current phase, the phase content, and the footer behaviour.
    /// </summary>
    public abstract class WizardScreen : Screen
    {
        protected WizardScreen() { Wrap = true; }

        public override bool IsActive() => WizardVm() != null;

        public override bool BuildsGraph => true;

        // Opening (and every phase change) lands on the page content — the phase content is the work
        // surface; the footer buttons stay later in Tab order.
        public override object InitialFocusStop => "content";

        /// <summary>The wizard root VM, or null when inactive. Used for activity + change detection.</summary>
        protected abstract object WizardVm();

        /// <summary>The current phase object — compared by reference to detect phase changes.</summary>
        protected abstract object CurrentPhase();

        /// <summary>Label for the content stop (the current phase's name).</summary>
        protected abstract string PhaseLabel();

        /// <summary>Declare the current phase's content into the builder (inside the "content" stop,
        /// under the phase-label context). <paramref name="k"/> is the phase-scoped key prefix — it
        /// carries the VM + phase, so a phase change re-keys the page. Content may open further stops
        /// of its own (mirroring a page's old Tab topology — a description panel, a toggle block).</summary>
        protected abstract void BuildContent(GraphBuilder b, string k);

        protected abstract void OnBack();
        protected abstract void OnNext();
        protected abstract string NextLabel();
        protected virtual bool NextEnabled() => true;
        protected virtual bool BackEnabled() => true;

        /// <summary>Called each update while the wizard is active and the phase is unchanged — for
        /// per-frame behaviour that follows focus/selection (e.g. driving the game's description
        /// panel). Must not disturb the focus path.</summary>
        protected virtual void OnPhaseTick() { }

        private object _lastVm;
        private object _lastPhase;

        public override void OnPop() { _lastVm = null; _lastPhase = null; }

        public override void OnUpdate()
        {
            var vm = WizardVm();
            if (vm == null) return;
            var phase = CurrentPhase();
            if (ReferenceEquals(vm, _lastVm) && ReferenceEquals(phase, _lastPhase))
            {
                OnPhaseTick();
                return;
            }

            // A phase change WITHIN this wizard (Next/Back) — not the initial build or a VM swap. The
            // game plays a page-turn on phase advance; our VM-level next/back bypasses it, so play it
            // here, and land focus on the new page (its keys changed, so nothing survives the differ).
            bool phaseChange = ReferenceEquals(vm, _lastVm) && _lastPhase != null;
            _lastVm = vm;
            _lastPhase = phase;
            if (phaseChange)
            {
                UiSound.PageTurn();
                Navigation.Active?.FocusStop("content");
            }
        }

        public override void Build(GraphBuilder b)
        {
            var vm = WizardVm();
            if (vm == null) return;
            var phase = CurrentPhase();

            // Phase-carrying key prefix: advancing (or a VM swap) re-keys the whole page.
            string k = "wiz:" + vm.GetHashCode() + ":" + (phase != null ? phase.GetHashCode() : 0) + ":";

            // The phase's content, labeled with the phase name so entering it announces the phase.
            b.BeginStop("content").PushContext(PhaseLabel() ?? "");
            BuildContent(b, k);
            b.PopContext();

            // Footer: Back then Next (label + availability read live per render). Both wizards that
            // share(d) this shell give these Plastick chrome sounds (NewGamePCView / CharGenPCView
            // SetButtonsSounds), so the sound is intrinsic to the footer here.
            var plastick = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound;
            b.BeginStop("back").AddItem(ControlId.Structural("wiz:back"),
                GraphNodes.Button(() => Loc.T("wizard.back"), OnBack, BackEnabled,
                    hoverSound: plastick, clickSound: plastick));
            b.BeginStop("next").AddItem(ControlId.Structural("wiz:next"),
                GraphNodes.Button(NextLabel, OnNext, NextEnabled,
                    hoverSound: plastick, clickSound: plastick));
        }
    }
}
