using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Ship;
using Kingmaker.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplatePlayerStarship
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The ship phase: choose the Rogue Trader's starship and name it. Completion needs a ship selected (a
    /// default is auto-picked) AND a name set manually, so we surface the ship list, the selected vessel's
    /// description, a live name readout, an inline typed-name modal (via <see cref="NameEntryScreen"/>) and a
    /// random-name action (which marks the name manual).
    ///
    /// The description mirrors the sighted layout: in <c>CharGenShipPhaseDetailedView</c> it is a persistent info
    /// panel (<c>m_InfoView</c>, bound to the phase <c>InfoVM</c>), NOT a hover tooltip — so it belongs in the tab
    /// order as its own navigable line, not behind the tooltip key.
    /// </summary>
    public sealed class ShipPhaseContent : CharGenPhaseContent
    {
        public ShipPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        private CharGenShipPhaseVM Vm => Phase as CharGenShipPhaseVM;

        public override void Build(GraphBuilder b, string k)
        {
            var vm = Vm;
            if (vm == null) { EmitUnavailable(b, k); return; }

            int i = 0;
            foreach (var e in vm.ShipSelectionGroup.EntitiesCollection)
            {
                var item = e; // capture
                b.AddItem(ControlId.Referenced(item, k + "ship:" + i++),
                    CharGenNodes.SelectionItem(item, () => item.Title,
                        // Space: the game's own starship tooltip (stats, weapons — the info panel's template).
                        tooltip: () => item.BlueprintStarship != null
                            ? (TooltipBaseTemplate)new TooltipTemplatePlayerStarship(item.BlueprintStarship)
                            : null));
            }

            // The selected ship's description — WHAT the vessel is (its lore/role), so the choice is informed
            // and not just a name. A persistent info panel in the sighted UI, so it lives in the tab order;
            // skipped while empty (a default is auto-picked, so it appears as soon as the list loads).
            if (!string.IsNullOrEmpty(vm.SelectedShipEntity?.Value?.Description))
                b.AddItem(ControlId.Structural(k + "desc"),
                    GraphNodes.Text(() => vm.SelectedShipEntity?.Value?.Description ?? ""));

            // Live ship-name readout.
            b.AddItem(ControlId.Structural(k + "name"),
                GraphNodes.Text(() => Loc.T("chargen.ship_name", new { value = vm.ShipName?.Value ?? "" })));

            // Type a custom ship name — opens the game's change-name modal (NameEntryScreen makes it navigable).
            b.AddItem(ControlId.Structural(k + "edit"),
                GraphNodes.Button(() => Loc.T("chargen.edit_name"), () => vm.ShowChangeNameMessageBox()));

            // Roll a random ship name. SetName(.., isManual:true) satisfies the name-chosen completion gate.
            b.AddItem(ControlId.Structural(k + "random"),
                GraphNodes.Button(() => Loc.T("chargen.random_name"), () => vm.SetName(vm.GetRandomName(), true)));
        }
    }
}
