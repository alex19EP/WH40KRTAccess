using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using RTAccess.Screens.CharGen;
using RTAccess.UI;

namespace RTAccess.Screens
{
    /// <summary>
    /// Builds the navigable content for one character-generation phase. The base is a placeholder; concrete
    /// per-phase builders (selected by <see cref="For"/> on <see cref="CharGenPhaseBaseVM.PhaseType"/>)
    /// render the real choices — pregen pick, homeworld/occupation/career selection, attribute point-buy,
    /// appearance, ship, name, summary.
    ///
    /// Some phases load their entries a frame late (the pregen list and the background selections come from
    /// async level-up/blueprint loads), so a builder can report a <see cref="LiveCount"/>; the base rebuilds
    /// the content in place when that count changes (e.g. empty → populated). <see cref="Tick"/> is called
    /// each frame while the phase is unchanged.
    /// </summary>
    public class CharGenPhaseContent
    {
        protected readonly CharGenPhaseBaseVM Phase;
        protected Container Content;
        private int _builtCount;

        protected CharGenPhaseContent(CharGenPhaseBaseVM phase) { Phase = phase; }

        /// <summary>Pick the content builder for a phase, by phase type.</summary>
        public static CharGenPhaseContent For(CharGenPhaseBaseVM phase)
        {
            if (phase == null) return null;
            switch (phase.PhaseType)
            {
                case CharGenPhaseType.Pregen:
                    return new PregenPhaseContent(phase);
                // Every "background" phase shares one selection-list builder (pick one feature of N, read
                // its description). Covers homeworld + its child worlds, occupation + its child careers,
                // navigator, soul mark, the career path, and the story child phases.
                case CharGenPhaseType.Homeworld:
                case CharGenPhaseType.ImperialHomeworldChild:
                case CharGenPhaseType.ForgeHomeworldChild:
                case CharGenPhaseType.Occupation:
                case CharGenPhaseType.SanctionedPsyker:
                case CharGenPhaseType.Arbitrator:
                case CharGenPhaseType.Navigator:
                case CharGenPhaseType.MomentOfTriumph:
                case CharGenPhaseType.DarkestHour:
                case CharGenPhaseType.SoulMark:
                    return new SelectionPhaseContent(phase);
                // Career isn't a background-base phase — it drives UnitProgressionVM's career paths.
                case CharGenPhaseType.Career:
                    return new CareerPhaseContent(phase);
                case CharGenPhaseType.Attributes:
                    return new AttributesPhaseContent(phase);
                case CharGenPhaseType.Ship:
                    return new ShipPhaseContent(phase);
                case CharGenPhaseType.Summary:
                    return new SummaryPhaseContent(phase);
                case CharGenPhaseType.Appearance:
                    return new AppearancePhaseContent(phase);
                default:
                    return new CharGenPhaseContent(phase);
            }
        }

        public void Build(Container content)
        {
            Content = content;
            OnBuild();
            _builtCount = LiveCount();
        }

        protected virtual void OnBuild()
        {
            Content.Add(new TextElement(() => Loc.T("chargen.phase_unavailable")));
        }

        public virtual void Tick()
        {
            int n = LiveCount();
            if (n >= 0 && n != _builtCount) { Content.Clear(); OnBuild(); _builtCount = n; }
        }

        /// <summary>Count of dynamically-loaded entries, or -1 to disable the auto-rebuild. A builder whose
        /// list populates a frame late overrides this so the base re-renders once the entries appear.</summary>
        protected virtual int LiveCount() => -1;
    }
}
