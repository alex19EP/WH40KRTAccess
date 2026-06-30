using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Career;
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.CareerPath;
using Kingmaker.UnitLogic.Progression.Paths;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The archetype (career) phase. Unlike the other background phases it isn't a
    /// CharGenBackgroundBasePhaseVM — it drives a <c>UnitProgressionVM</c> whose <c>AllCareerPaths</c> are
    /// <see cref="CareerPathVM"/> entries (themselves a selection group). We list the tier-one paths (the
    /// starting archetypes); selecting one routes through the game's select-career command. The list binds
    /// a frame after the phase opens, so the base rebuilds on count change.
    ///
    /// TODO (enrichment): the per-rank ability/talent picks a chosen archetype unlocks (rank entries) —
    /// needed for a fully-built career, surfaced as a drill-in.
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

        protected override void OnBuild()
        {
            var items = Archetypes();
            if (items.Count == 0) { Content.Add(new TextElement(() => Loc.T("chargen.nothing_to_select"))); return; }

            var list = new ListContainer();
            foreach (var c in items)
            {
                var item = c; // capture
                list.Add(new ProxySelectionItem(item, () => item.Name));
            }
            Content.Add(list);

            // Live description of the selected archetype.
            Content.Add(new TextElement(() => SelectedDescription(items)));
        }

        protected override int LiveCount() => Archetypes().Count;

        private static string SelectedDescription(List<CareerPathVM> items)
        {
            foreach (var c in items)
                if (c.IsSelected.Value) return c.Description ?? "";
            return "";
        }
    }
}
