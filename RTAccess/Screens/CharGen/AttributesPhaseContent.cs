using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Stats;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The attribute point-buy phase: a live "points remaining" readout, then one stepper row per
    /// attribute (Left/Right spends or refunds a point through the game's own advance-stat command, up
    /// to two ranks each). The phase completes when every point is spent. Per-adjust feedback ("name
    /// value. N points remaining.") is spoken by CharGenAnnounce's Harmony postfix on the game's
    /// advance handler — the rows themselves stay silent on adjust so it isn't double-spoken.
    /// </summary>
    public sealed class AttributesPhaseContent : CharGenPhaseContent
    {
        public AttributesPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        private CharGenAttributesPhaseVM Vm => Phase as CharGenAttributesPhaseVM;

        public override void Build(GraphBuilder b, string k)
        {
            var vm = Vm;
            if (vm == null) { EmitUnavailable(b, k); return; }

            // Points-remaining header (reads live whenever announced).
            b.AddItem(ControlId.Structural(k + "points"),
                GraphNodes.Text(() => Loc.T("chargen.points_remaining", new { value = vm.AvailablePointsLeft.Value })));

            // Own level for the stat rows: positions group by (parent, stop), so the points header
            // above must stay outside or it counts into the rows' "n of m".
            b.PushContext(vm.PhaseName?.Value ?? "", Loc.T("role.list"));
            int i = 0;
            foreach (var it in vm.SelectionGroup.EntitiesCollection)
                b.AddItem(ControlId.Referenced(it, k + "stat:" + i++), CharGenNodes.StatRow(it));
            b.PopContext();

            // The live SKILLS panel the detailed view binds beside the steppers
            // (CharGenAttributesPhaseDetailedView → CharGenAttributesPhaseVM.CharInfoSkillsBlock). It is the
            // whole point of the page's right-hand side: a characteristic spend RAISES SKILLS, and the block
            // re-highlights per selected attribute and carries the career's recommended marks. Without it a
            // blind player never learned their character's 13 skills during creation — not the values, not
            // which the career recommends, not which move when a point is spent, and no per-skill card.
            // Its own Tab-stop, so the arrows stay in the steppers and Tab reaches the consequences.
            CharGenNodes.StatBlock(b, "skills", k + "skill:", Loc.T("charinfo.skills"),
                vm.CharInfoSkillsBlock?.Stats);
        }
    }
}
