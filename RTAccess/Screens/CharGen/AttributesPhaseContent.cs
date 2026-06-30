using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Stats;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The attribute point-buy phase: a live "points remaining" readout, then one stepper per attribute
    /// (Left/Right spends or refunds a point, up to two ranks each). The phase completes when every point
    /// is spent. The stat rows load a frame late via the level-up manager, so the base rebuilds on count
    /// change.
    /// </summary>
    public sealed class AttributesPhaseContent : CharGenPhaseContent
    {
        public AttributesPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        private CharGenAttributesPhaseVM Vm => Phase as CharGenAttributesPhaseVM;

        protected override void OnBuild()
        {
            var vm = Vm;
            if (vm == null) { base.OnBuild(); return; }

            // Live points-remaining header (updates as you spend/refund).
            Content.Add(new TextElement(() => Loc.T("chargen.points_remaining", new { value = vm.AvailablePointsLeft.Value })));

            var list = new ListContainer();
            foreach (var it in vm.SelectionGroup.EntitiesCollection)
                list.Add(new ProxyStatStepper(it));
            Content.Add(list);
        }

        protected override int LiveCount() => Vm?.SelectionGroup?.EntitiesCollection?.Count ?? -1;
    }
}
