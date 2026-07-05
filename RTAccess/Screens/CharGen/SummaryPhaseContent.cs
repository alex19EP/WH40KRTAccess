using System.Linq;
using Kingmaker.Blueprints.Root;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.AbilityScores;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Summary;
using Kingmaker.UnitLogic.Progression.Paths;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The final phase: review + name the character. Completion needs every selection valid AND a name
    /// chosen manually, so we surface a name readout, "Edit name" (the typed-name modal via
    /// <see cref="NameEntryScreen"/>) and "Random name" (which sets a manual Imperial name, satisfying that
    /// gate). Below the name controls is a read-only review of the built character — level, archetype(s) and
    /// the final attribute scores — so the player can confirm before committing. The name VM and preview
    /// unit bind a frame after the phase opens; immediate mode renders them once they appear.
    /// </summary>
    public sealed class SummaryPhaseContent : CharGenPhaseContent
    {
        public SummaryPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        private CharGenSummaryPhaseVM Vm => Phase as CharGenSummaryPhaseVM;

        public override void Build(GraphBuilder b, string k)
        {
            var vm = Vm;
            if (vm?.CharGenNameVM == null) { EmitUnavailable(b, k); return; }

            // Live name readout.
            b.AddItem(ControlId.Structural(k + "name"),
                GraphNodes.Text(() => Loc.T("chargen.character_name", new { value = vm.CharGenNameVM.UnitName?.Value ?? "" })));

            // Type a custom name — opens the game's change-name modal, which NameEntryScreen makes navigable.
            b.AddItem(ControlId.Structural(k + "edit"),
                GraphNodes.Button(() => Loc.T("chargen.edit_name"),
                    () => vm.CharGenNameVM.ShowChangeNameMessageBox()));

            // Roll a random Imperial name. SetNameAndNotify marks the name as manually chosen, which is what
            // the completion gate checks (a default-only name doesn't count).
            b.AddItem(ControlId.Structural(k + "random"),
                GraphNodes.Button(() => Loc.T("chargen.random_name"),
                    () => { var n = vm.CharGenNameVM; n.SetNameAndNotify(n.GetRandomName()); }));

            BuildReview(b, k, vm);
        }

        // Read-only summary of the finished character. Best-effort — guarded so a data hiccup can't break
        // the (more important) name controls above.
        private static void BuildReview(GraphBuilder b, string k, CharGenSummaryPhaseVM vm)
        {
            BaseUnitEntity unit;
            try { unit = vm.CharGenNameVM.PreviewUnit?.Value; }
            catch { unit = null; }
            if (unit == null) return;

            b.PushContext(Loc.T("chargen.review"), Loc.T("role.list"));
            try
            {
                b.AddItem(ControlId.Structural(k + "review:level"),
                    GraphNodes.Text(() => Loc.T("chargen.review_level", new { value = unit.Progression.CharacterLevel })));

                int i = 0;
                foreach (var f in unit.Progression.Features.Enumerable.Where(x => x.Blueprint is BlueprintCareerPath))
                {
                    var career = (BlueprintCareerPath)f.Blueprint;
                    b.AddItem(ControlId.Structural(k + "review:career:" + i++),
                        GraphNodes.Text(() => career.Name));
                }

                foreach (var stat in CharInfoAbilityScoresBlockVM.AbilitiesOrdered)
                {
                    var st = stat; // capture
                    var mv = unit.Stats.GetStat(st);
                    if (mv != null)
                        b.AddItem(ControlId.Structural(k + "review:stat:" + st),
                            GraphNodes.Text(() => LocalizedTexts.Instance.Stats.GetText(st) + ": " + mv.ModifiedValue));
                }
            }
            catch { /* leave whatever was added; the name controls already shipped */ }
            b.PopContext();
        }
    }
}
