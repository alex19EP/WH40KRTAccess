using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using RTAccess.Screens.CharGen;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// Emits the navigable content for one character-generation phase (graph-native, IMMEDIATE MODE):
    /// a content object is created fresh per render by <see cref="For"/> (selected on
    /// <see cref="CharGenPhaseBaseVM.PhaseType"/>) and declares the phase's controls from the LIVE
    /// phase VM into the wizard's content stop. Contents hold NO view state — a phase whose game list
    /// hasn't materialized yet just emits nothing and renders once it does, which retires the old
    /// Tick()/LiveCount() rebuild machinery wholesale.
    /// </summary>
    public abstract class CharGenPhaseContent
    {
        protected readonly CharGenPhaseBaseVM Phase;

        protected CharGenPhaseContent(CharGenPhaseBaseVM phase) { Phase = phase; }

        /// <summary>Emit the phase's content. <paramref name="k"/> is the phase-scoped key prefix from
        /// the wizard shell (carries the VM + phase, so a phase change re-keys the page).</summary>
        public abstract void Build(GraphBuilder b, string k);

        /// <summary>Pick the content builder for a phase, by phase type; null = no builder yet (the
        /// screen emits the "phase unavailable" placeholder).</summary>
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
                    return null;
            }
        }

        /// <summary>The shared "phase not accessible" placeholder line.</summary>
        internal static void EmitUnavailable(GraphBuilder b, string k)
            => b.AddItem(ControlId.Structural(k + "unavailable"),
                GraphNodes.Text(() => Loc.T("chargen.phase_unavailable")));
    }
}
