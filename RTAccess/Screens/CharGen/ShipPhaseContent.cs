using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Ship;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The ship phase: choose the Rogue Trader's starship and name it. Completion needs a ship selected (a
    /// default is auto-picked) AND a name set manually, so we surface the ship list, the selected vessel's
    /// description, a live name readout, an inline typed-name modal (via <see cref="NameEntryScreen"/>) and a
    /// random-name action (which marks the name manual). Ships load async, so the base rebuilds on count change.
    ///
    /// The description mirrors the sighted layout: in <c>CharGenShipPhaseDetailedView</c> it is a persistent info
    /// panel (<c>m_InfoView</c>, bound to the phase <c>InfoVM</c>), NOT a hover tooltip — so it belongs in the tab
    /// order as its own navigable line, not behind the tooltip key.
    /// </summary>
    public sealed class ShipPhaseContent : CharGenPhaseContent
    {
        public ShipPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        private CharGenShipPhaseVM Vm => Phase as CharGenShipPhaseVM;

        protected override void OnBuild()
        {
            var vm = Vm;
            if (vm == null) { base.OnBuild(); return; }

            var ships = vm.ShipSelectionGroup.EntitiesCollection;
            if (ships.Count > 0)
            {
                var list = new ListContainer();
                foreach (var e in ships)
                {
                    var item = e; // capture
                    list.Add(new ProxySelectionItem(item, () => item.Title));
                }
                Content.Add(list);
            }

            // The selected ship's description — WHAT the vessel is (its lore/role), so the choice is informed and
            // not just a name. In the sighted UI this is a persistent info panel, not a tooltip, so it lives in the
            // tab order here. Reads the live selection; empty text isn't focusable, so it self-hides until a ship
            // is selected (a default is auto-picked, so it populates as soon as the list loads).
            Content.Add(new TextElement(() => vm.SelectedShipEntity?.Value?.Description ?? ""));

            // Live ship-name readout.
            Content.Add(new TextElement(() => Loc.T("chargen.ship_name", new { value = vm.ShipName?.Value ?? "" })));

            // Type a custom ship name — opens the game's change-name modal (NameEntryScreen makes it navigable).
            Content.Add(new ProxyActionButton(() => Loc.T("chargen.edit_name"), () => true,
                () => vm.ShowChangeNameMessageBox()));

            // Roll a random ship name. SetName(.., isManual:true) satisfies the name-chosen completion gate.
            Content.Add(new ProxyActionButton(() => Loc.T("chargen.random_name"), () => true,
                () => vm.SetName(vm.GetRandomName(), true)));
        }

        protected override int LiveCount() => Vm?.ShipSelectionGroup?.EntitiesCollection?.Count ?? -1;
    }
}
