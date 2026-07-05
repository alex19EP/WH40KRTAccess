using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.BackgroundBase;
using Kingmaker.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplateChargenBackground
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The shared builder for every "background" chargen phase — homeworld (+ its child world), occupation
    /// (+ its child careers), navigator, soul mark, and the story child phases. They all derive from the
    /// generic <c>CharGenBackgroundBasePhaseVM&lt;T&gt;</c>, whose items share
    /// <see cref="CharGenBackgroundBaseItemVM"/> (a DisplayName + a Feature). We pull the items off the
    /// generic <c>SelectionGroup</c> by reflection (the base type is open-generic, so there's no shared
    /// non-generic accessor) and render: a radio list of the choices + a live description line of the
    /// SELECTED one (the committed selection, never a hover-fed reactive). Space on an item opens the
    /// game's own chargen-background tooltip for THAT feature; Space on the description line reads the
    /// phase's info panel (the InfoVM fallback — the old console "details" source, rewired).
    /// </summary>
    public sealed class SelectionPhaseContent : CharGenPhaseContent
    {
        public SelectionPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            var items = Items(Phase).ToList();
            if (items.Count == 0)
            {
                b.AddItem(ControlId.Structural(k + "empty"),
                    GraphNodes.Text(() => Loc.T("chargen.nothing_to_select")));
                return;
            }

            int i = 0;
            foreach (var it in items)
            {
                var item = it; // capture for the live closures
                b.AddItem(ControlId.Referenced(item, k + "item:" + i++),
                    CharGenNodes.SelectionItem(item, () => item.DisplayName,
                        // The same template the game's info panel renders for this feature.
                        tooltip: () => item.Feature != null
                            ? (TooltipBaseTemplate)new TooltipTemplateChargenBackground(item.Feature,
                                isInfoWindow: true, isCharGen: true)
                            : null));
            }

            // Live description of whichever choice is currently SELECTED (updates as you arrow the
            // list — doctrine-3-safe: reads the committed selection, not a hover reactive). Skipped
            // while empty (the old TextElement self-hid), so it appears once something is selected.
            // Space here reads the phase's full info panel (InfoVM tooltip) via the shared chooser —
            // the CharGenAnnounce description fallback, rewired from the retired console details key.
            var phase = Phase;
            if (!string.IsNullOrEmpty(SelectedDescription(items)))
                b.AddItem(ControlId.Structural(k + "desc"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new List<NodeAnnouncement>
                    {
                        GraphNodes.LabelPart(() => SelectedDescription(Items(phase))),
                    },
                    OnTooltip = () => TooltipChooser.Open(phase?.PhaseName?.Value,
                        RTAccess.Accessibility.CharGenAnnounce.GetActivePhaseDescription(),
                        sections: null, links: null),
                });
        }

        private static string SelectedDescription(IEnumerable<CharGenBackgroundBaseItemVM> items)
        {
            foreach (var it in items)
                if (it.IsSelected.Value) return it.Feature?.Description ?? "";
            return "";
        }

        // The phase's SelectionGroup (and its EntitiesCollection) are public fields on the open-generic
        // base, so reflect them by name; the items are all CharGenBackgroundBaseItemVM.
        internal static IEnumerable<CharGenBackgroundBaseItemVM> Items(CharGenPhaseBaseVM phase)
        {
            var sg = phase?.GetType().GetField("SelectionGroup")?.GetValue(phase);
            var ec = sg?.GetType().GetField("EntitiesCollection")?.GetValue(sg) as IEnumerable;
            if (ec == null) yield break;
            foreach (var o in ec)
                if (o is CharGenBackgroundBaseItemVM bi) yield return bi;
        }
    }
}
