using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.BackgroundBase;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The shared builder for every "background" chargen phase — homeworld (+ its child world), occupation
    /// (+ its child careers), navigator, soul mark, career path, and the story child phases. They all
    /// derive from the generic <c>CharGenBackgroundBasePhaseVM&lt;T&gt;</c>, whose items share
    /// <see cref="CharGenBackgroundBaseItemVM"/> (a DisplayName + a Feature). We pull the items off the
    /// generic <c>SelectionGroup</c> by reflection (the base type is open-generic, so there's no shared
    /// non-generic accessor) and render: a radio list of the choices + a live description of the selected
    /// one. The selections load a frame late via the level-up manager, so the base rebuilds on count change.
    /// </summary>
    public sealed class SelectionPhaseContent : CharGenPhaseContent
    {
        public SelectionPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        protected override void OnBuild()
        {
            var items = Items(Phase).ToList();
            if (items.Count == 0) { Content.Add(new TextElement(() => Loc.T("chargen.nothing_to_select"))); return; }

            var list = new ListContainer();
            foreach (var it in items)
            {
                var item = it; // capture for the live closures
                list.Add(new ProxySelectionItem(item, () => item.DisplayName));
            }
            Content.Add(list);

            // Live description of whichever choice is currently selected (updates as you arrow the list).
            Content.Add(new TextElement(() => SelectedDescription(items)));
        }

        protected override int LiveCount() => Items(Phase).Count();

        private static string SelectedDescription(List<CharGenBackgroundBaseItemVM> items)
        {
            foreach (var it in items)
                if (it.IsSelected.Value) return it.Feature?.Description ?? "";
            return "";
        }

        // The phase's SelectionGroup (and its EntitiesCollection) are public fields on the open-generic
        // base, so reflect them by name once per build; the items are all CharGenBackgroundBaseItemVM.
        internal static IEnumerable<CharGenBackgroundBaseItemVM> Items(CharGenPhaseBaseVM phase)
        {
            var sg = phase?.GetType().GetField("SelectionGroup")?.GetValue(phase);
            var ec = sg?.GetType().GetField("EntitiesCollection")?.GetValue(sg) as IEnumerable;
            if (ec == null) yield break;
            foreach (var o in ec)
                if (o is CharGenBackgroundBaseItemVM b) yield return b;
        }
    }
}
