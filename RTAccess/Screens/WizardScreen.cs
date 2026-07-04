using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// Shared shell for the game's phase-based wizards (New Game setup, character generation):
    /// a content panel labeled with the current phase, plus Back/Next footer buttons. Rebuilds
    /// and re-homes focus onto the new page when the wizard VM or current phase changes (so
    /// pressing Next lands you on the next phase's content). Subclasses supply the VM, the current
    /// phase, the phase content, and the footer behaviour.
    /// </summary>
    public abstract class WizardScreen : Screen
    {
        protected WizardScreen() { Wrap = true; }

        public override bool IsActive() => WizardVm() != null;

        /// <summary>The wizard root VM, or null when inactive. Used for activity + change detection.</summary>
        protected abstract object WizardVm();

        /// <summary>The current phase object — compared by reference to detect phase changes.</summary>
        protected abstract object CurrentPhase();

        /// <summary>Label for the content panel (the current phase's name).</summary>
        protected abstract string PhaseLabel();

        /// <summary>Fill the current phase's content.</summary>
        protected abstract void BuildContent(Container content);

        protected abstract void OnBack();
        protected abstract void OnNext();
        protected abstract string NextLabel();
        protected virtual bool NextEnabled() => true;
        protected virtual bool BackEnabled() => true;

        private object _builtVm;
        private object _builtPhase;

        public override void OnPush() { _builtVm = null; _builtPhase = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtVm = null; _builtPhase = null; }

        public override void OnUpdate()
        {
            var vm = WizardVm();
            if (vm == null) return;
            if (!ReferenceEquals(vm, _builtVm) || !ReferenceEquals(CurrentPhase(), _builtPhase))
            {
                // A phase change WITHIN this wizard (Next/Back/step pick) — not the initial build or a
                // VM swap. The game plays a page-turn on phase advance; our VM-level SelectNext/Prev
                // bypasses it, so play it here.
                bool phaseChange = ReferenceEquals(vm, _builtVm) && _builtPhase != null;

                // VM swapped or phase changed — rebuild and land on it.
                Rebuild();
                Navigation.Attach(this);
                if (phaseChange) UiSound.PageTurn();
                return;
            }
            // Same phase: let subclasses refresh selection-driven content in place (e.g. a detail
            // panel) without rebuilding the whole tree or moving focus.
            OnPhaseTick();
        }

        /// <summary>Called each update while the phase is unchanged — for live, in-place updates
        /// (a detail panel that tracks the current selection). Must not disturb the focus path.</summary>
        protected virtual void OnPhaseTick() { }

        /// <summary>Optional content above the phase panel — chargen uses it for the roadmap strip.
        /// Default: nothing (NewGame has no header).</summary>
        protected virtual void BuildHeader(Container root) { }

        private void Rebuild()
        {
            Clear();
            var vm = WizardVm();
            _builtVm = vm;
            _builtPhase = vm != null ? CurrentPhase() : null;
            if (vm == null) return;

            // Header (e.g. the chargen roadmap) above the phase content, matching the game's layout.
            BuildHeader(this);

            // Content panel, labeled with the current phase so entering it announces the phase.
            var content = new Panel(PhaseLabel());
            BuildContent(content);
            Add(content);

            // Footer: Back then Next (label + availability track the current phase live). Both wizards that
            // share this shell (New Game setup and character generation) give these Plastick chrome sounds
            // (NewGamePCView / CharGenPCView SetButtonsSounds), so the sound is intrinsic to the footer here.
            var plastick = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound;
            Add(new ProxyActionButton(() => Loc.T("wizard.back"), BackEnabled, OnBack,
                hoverSoundType: plastick, clickSoundType: plastick));
            Add(new ProxyActionButton(NextLabel, NextEnabled, OnNext,
                hoverSoundType: plastick, clickSoundType: plastick));

            // Land initial focus on the phase content, not the header — so advancing/jumping phases
            // drops you onto the new phase, not back on the roadmap (the header is first in tab order).
            SetFocusedChild(content);
        }
    }
}
