using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Career;
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.CareerPath;
using Kingmaker.UnitLogic.Progression.Paths;
using RTAccess.UI;
using Access.Core.Graph;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The archetype (career) phase. Unlike the other background phases it isn't a
    /// CharGenBackgroundBasePhaseVM — it drives a <c>UnitProgressionVM</c> whose <c>AllCareerPaths</c> are
    /// <see cref="CareerPathVM"/> entries (themselves a selection group). We list the tier-one paths (the
    /// starting archetypes); selecting one routes through the game's select-career command. Below the list
    /// is a live description line of the SELECTED archetype (the committed selection); Space on an item
    /// opens the game's own career tooltip.
    ///
    /// TODO (enrichment): the per-rank ability/talent picks a chosen archetype unlocks (rank entries) —
    /// needed for a fully-built career, surfaced as a drill-in (LevelUpScreen has the richer treatment).
    /// </summary>
    public sealed class CareerPhaseContent : CharGenPhaseContent
    {
        public CareerPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        private CharGenCareerPhaseVM Vm => Phase as CharGenCareerPhaseVM;

        private List<CareerPathVM> Archetypes()
        {
            var prog = Vm?.UnitProgressionVM;
            if (prog == null) return new List<CareerPathVM>();
            return prog.AllCareerPaths.Where(c => c.CareerPath != null && c.CareerPath.Tier == CareerPathTier.One).ToList();
        }

        public override void Build(GraphBuilder b, string k)
        {
            var items = Archetypes();
            if (items.Count == 0)
            {
                b.AddItem(ControlId.Structural(k + "empty"),
                    GraphNodes.Text(() => Loc.T("chargen.nothing_to_select")));
                return;
            }

            // Own level for the choices: positions group by (parent, stop), so the description line
            // below must stay outside or it counts into the items' "n of m".
            b.PushContext(Phase?.PhaseName?.Value ?? "", Loc.T("role.list"));
            int i = 0;
            foreach (var c in items)
            {
                var item = c; // capture
                b.AddItem(ControlId.Referenced(item, k + "career:" + i++),
                    CharGenNodes.SelectionItem(item, () => item.Name,
                        tooltip: () => item.CareerTooltip));
            }
            b.PopContext();

            // Live description of the SELECTED archetype (the committed selection). Emitted ALWAYS, not only
            // once something is selected: while nothing is picked the phase's info panel holds
            // TooltipTemplateCharGenDoctrinesDesc — the game explaining what a doctrine/archetype IS, which
            // is precisely the moment a player arriving on this page needs it. Behind an emptiness guard that
            // explainer was unreachable at every point in chargen, because the instant an archetype IS picked
            // the same OnTooltip resolves to that archetype's career card instead. (This phase does not
            // auto-select — SetupDefaultItemsState leaves it open with a "select archetype" hint — unlike the
            // background phases, where the identical guard is harmless.)
            var phase = Phase;
            b.AddItem(ControlId.Structural(k + "desc"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() =>
                    {
                        var d = SelectedDescription(Archetypes());
                        // With nothing picked the line has no text of its own; say what it IS rather than
                        // reading as a blank stop, and let Space carry the explainer.
                        return string.IsNullOrEmpty(d) ? Loc.T("chargen.details") : d;
                    }),
                },
                // Template path, like the background phases: the panel's rows carry the archetype's
                // own talents' tooltips, which a flattened string throws away.
                OnTooltip = () => TooltipChooser.OpenTemplate(phase?.PhaseName?.Value,
                    RTAccess.Accessibility.CharGenAnnounce.GetActivePhaseTooltip()),
            });
        }

        private static string SelectedDescription(List<CareerPathVM> items)
        {
            foreach (var c in items)
                if (c.IsSelected.Value) return c.Description ?? "";
            return "";
        }
    }
}
