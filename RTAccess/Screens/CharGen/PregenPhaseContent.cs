using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Pregen;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The first chargen phase: pick a pre-generated character or "Create Custom Character". The first
    /// entry is the custom option (selecting it reveals the homeworld/occupation/career/attribute phases);
    /// the rest are ready-made characters (name + class). Selection routes through the game's pregen
    /// command, so the whole phase set updates as you switch. The list loads asynchronously — immediate
    /// mode just renders it once the pregens arrive. Space on the SELECTED entry opens the phase's own
    /// info-panel tooltip (the pregen's description, which tracks the committed selection).
    /// </summary>
    public sealed class PregenPhaseContent : CharGenPhaseContent
    {
        public PregenPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        private CharGenPregenPhaseVM Vm => Phase as CharGenPregenPhaseVM;

        public override void Build(GraphBuilder b, string k)
        {
            var vm = Vm;
            if (vm == null) { EmitUnavailable(b, k); return; }

            int i = 0;
            foreach (var e in vm.PregenSelectionGroup.EntitiesCollection)
            {
                var item = e; // capture for the live closures
                b.AddItem(ControlId.Referenced(item, k + "pregen:" + i++),
                    CharGenNodes.SelectionItem(item, () => Label(item),
                        // The info panel keys off the COMMITTED selection — only the selected entry
                        // carries the phase tooltip (browsing others would read the wrong character).
                        tooltip: () => item.IsSelected.Value ? Phase?.InfoVM?.CurrentTooltip : null));
            }
        }

        // "Create Custom Character" for the custom entry (no class); "<name>, <class>" for a ready-made one.
        private static string Label(CharGenPregenSelectorItemVM item)
        {
            var name = item?.CharacterName?.Value ?? "";
            var cls = item?.Class?.Value;
            return string.IsNullOrEmpty(cls) ? name : name + ", " + cls;
        }
    }
}
