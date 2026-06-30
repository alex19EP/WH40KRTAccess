using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Pregen;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The first chargen phase: pick a pre-generated character or "Create Custom Character". The first
    /// entry is the custom option (selecting it reveals the homeworld/occupation/career/attribute phases);
    /// the rest are ready-made characters (name + class). Selection routes through the game's pregen
    /// command, so the whole phase set updates as you switch. The list loads asynchronously, so the base's
    /// count-driven rebuild re-renders it once the pregens arrive.
    /// </summary>
    public sealed class PregenPhaseContent : CharGenPhaseContent
    {
        public PregenPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        private CharGenPregenPhaseVM Vm => Phase as CharGenPregenPhaseVM;

        protected override void OnBuild()
        {
            var vm = Vm;
            if (vm == null) { base.OnBuild(); return; }

            var list = new ListContainer();
            foreach (var e in vm.PregenSelectionGroup.EntitiesCollection)
            {
                var item = e; // capture for the live label closure
                list.Add(new ProxySelectionItem(item, () => Label(item)));
            }
            Content.Add(list);
        }

        protected override int LiveCount() => Vm?.PregenSelectionGroup?.EntitiesCollection?.Count ?? -1;

        // "Create Custom Character" for the custom entry (no class); "<name>, <class>" for a ready-made one.
        private static string Label(CharGenPregenSelectorItemVM item)
        {
            var name = item?.CharacterName?.Value ?? "";
            var cls = item?.Class?.Value;
            return string.IsNullOrEmpty(cls) ? name : name + ", " + cls;
        }
    }
}
