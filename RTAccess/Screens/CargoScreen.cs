using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Root;                                        // LocalizedTexts (sorter names)
using Kingmaker.Blueprints.Root.Strings;                                // UIStrings (CargoTexts.Cargo)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CargoManagement;         // CargoManagementVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CargoManagement.Components; // InventoryCargoVM, CargoSlotVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;               // InventoryStashVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The cargo-management service window (<see cref="CargoManagementVM"/>) as a graph-native screen —
    /// the ship's hold, where trade cargo accumulates and items move party stash ↔ cargo bays. Three
    /// Tab-stops mirroring the window's panes:
    ///   • Ship — the header's ship name.
    ///   • Stash — the party inventory side: the SAME <see cref="InventoryStashVM"/> the inventory window
    ///     uses, so its search + filter/sort chrome is shared verbatim (InventoryScreen.BuildSearch /
    ///     BuildStashControls). Rows are <see cref="ItemNodes.VendorStashItem"/> — Enter sends to cargo via
    ///     <c>InventoryHelper.TryMoveToCargo</c> DIRECTLY, because this window's
    ///     <c>IInventoryHandler.TryMoveToCargo</c> is an empty stub (the same trap as the vendor window;
    ///     equip/drop are stubs too, so the inventory-window row semantics would dead-end here).
    ///   • Cargo — the bays (<see cref="InventoryCargoVM.CargoSlots"/>): the game's cargo sorter as a combo
    ///     box (<c>SetCurrentSorter</c> — orders bays and their contents), then one collapsible GROUP per
    ///     bay whose LIVE header mirrors the card (name, type, fill %, unusable %, "new") with the game's
    ///     own cargo tooltip on Space, and whose children are the bay's items
    ///     (<see cref="ItemNodes.CargoItem"/> — Enter sends back to the party through the window's live
    ///     <c>TryMoveToInventory</c> route). Bay item groups are created through the game's own lazy
    ///     <c>CreateItemSlotsGroup</c> (idempotent — the same call its detailed-zone view makes). The
    ///     8-slot placeholder pads (<c>CargoEntity == null</c>) are visual filler and are skipped.
    /// Immediate mode throughout; bays key by their CargoEntity and items by item entity, so transfers
    /// re-home focus by identity and the differ announces the landing. Escape closes the service-window
    /// stack. Layer 10. ScreenName stays null — ServiceWindowAnnounce already speaks "Cargo" on open.
    /// </summary>
    public sealed class CargoScreen : Screen
    {
        public override string Key => "service.cargo";
        public override string ScreenName => null; // ServiceWindowAnnounce speaks the window name on open
        public override int Layer => 10;

        public CargoScreen() { Wrap = true; } // Tab wraps — the service-window convention

        // Type-ahead OFF: bare letters pass to the game; the stash search field is the accessible search.
        public override bool AllowsTypeahead => false;

        public override bool IsActive() => Vm() != null;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => UiContexts.ServiceWindows()?.HandleCloseAll());
        }

        // The window opens on the ship bridge AND in the star-system context; resolve from whichever
        // static part is live (the InventoryScreen shape).
        private static CargoManagementVM Vm() => UiContexts.CargoManagement();


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "cargo:" + vm.GetHashCode() + ":"; // a new window = fresh keys

            BuildShip(b, k, vm);

            // The stash pane — the inventory window's exact shape: search + filter/sort chrome as
            // Ctrl+arrow regions above the list they operate on, one pane-wide context labelling the stop.
            b.BeginStop("stash");
            b.PushContext(Loc.T("inv.stash"));
            InventoryScreen.BuildSearch(b, k, vm.StashVM);
            InventoryScreen.BuildStashControls(b, k, vm.StashVM);
            BuildStash(b, k, vm.StashVM);
            b.PopContext();

            BuildCargo(b, k, vm.InventoryCargoVM);
        }

        // The header pane: which voidship's hold this is (the window's ship name + portrait block).
        private static void BuildShip(GraphBuilder b, string k, CargoManagementVM vm)
        {
            var ship = vm.ShipNameAndPortraitVM;
            if (ship == null) return;
            b.BeginStop("ship");
            b.PushContext(Loc.T("cargo.ship"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(k + "ship:name"), GraphNodes.Text(() => ship.StarShipName));
            b.PopContext();
        }

        // The party-stash list. Rows are the vendor-window shape (Enter = send to cargo via the direct
        // InventoryHelper route, Backspace = the live-verb menu) — see the class doc for why not the
        // inventory-window rows. Keyed by item entity so a transferred item's node vanishes and focus
        // slides to a genuinely different row the differ reads out.
        private static void BuildStash(GraphBuilder b, string k, InventoryStashVM stash)
        {
            b.SetRegion(k + "stash");
            b.PushContext(Loc.T("inv.stash"), Loc.T("role.list"));
            bool any = false;
            var vis = stash?.ItemSlotsGroup?.VisibleCollection;
            if (vis != null)
                foreach (var slot in vis)
                {
                    if (slot == null || !slot.HasItem) continue;
                    var ent = slot.Item?.Value;
                    if (ent == null) continue;
                    b.AddItem(ControlId.Referenced(ent, k + "stash:" + ent.UniqueId), ItemNodes.VendorStashItem(slot));
                    any = true;
                }
            if (!any) b.AddItem(ControlId.Structural(k + "stash:empty"), GraphNodes.Text(() => Loc.T("inv.no_items")));
            b.PopContext();
        }

        // The cargo pane: the sorter, then one group per bay.
        private static void BuildCargo(GraphBuilder b, string k, InventoryCargoVM cargo)
        {
            if (cargo == null) return;
            b.BeginStop("cargo");
            b.PushContext(UIStrings.Instance.CargoTexts.Cargo.Text, Loc.T("role.list"));

            // The game's cargo sorter (its dropdown builds the same non-vendor set — every ItemsSorterType
            // except CargoValue): orders the bays AND the items inside each (OnSorterChanged).
            var sorters = InventoryScreen.SortOptions;
            b.AddItem(ControlId.Structural(k + "cargosort"), GraphNodes.Cycler(
                () => Loc.T("inv.sort"),
                () => sorters.ConvertAll(t => LocalizedTexts.Instance.ItemsFilter.GetText(t)),
                () => Math.Max(0, sorters.IndexOf(cargo.CurrentSorter.Value)),
                i => { if (i >= 0 && i < sorters.Count) cargo.SetCurrentSorter(sorters[i]); }));

            bool any = false;
            foreach (var bay in cargo.CargoSlots)
            {
                if (bay?.CargoEntity == null) continue; // the 8-slot visual pad — not a real bay
                any = true;
                var slot = bay;
                string bk = k + "bay:" + slot.CargoEntity.UniqueId;
                b.BeginGroup(ControlId.Referenced(slot.CargoEntity, bk), BayNode(slot));
                slot.CreateItemSlotsGroup(); // the game's own lazy per-bay group (idempotent)
                var vis = slot.ItemSlotsGroup?.VisibleCollection;
                if (vis != null)
                    foreach (var item in vis)
                    {
                        if (item == null || !item.HasItem) continue;
                        var ent = item.Item?.Value;
                        if (ent == null) continue;
                        b.AddItem(ControlId.Referenced(ent, bk + ":it:" + ent.UniqueId), ItemNodes.CargoItem(item));
                    }
                b.EndGroup();
            }
            if (!any) b.AddItem(ControlId.Structural(k + "bays:none"), GraphNodes.Text(() => Loc.T("cargo.no_bays")));
            b.PopContext();
        }

        // A bay's header node: the card mirror (name, origin type, fill %, unusable %, "new"), LIVE — the
        // fill changes under focus as items transfer — with the game's own cargo tooltip on Space.
        private static NodeVtable BayNode(CargoSlotVM bay)
        {
            Func<string> label = () => BayLabel(bay);
            var vt = GraphNodes.Group(label);
            vt.Announcements = new List<NodeAnnouncement>
            {
                new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
            };
            if (bay.Tooltip != null)
                vt.OnTooltip = () => TooltipChooser.OpenTemplate(label(), bay.Tooltip);
            return vt;
        }

        private static string BayLabel(CargoSlotVM bay)
        {
            var s = Loc.T("cargo.bay", new
            {
                name = bay.Title,
                type = bay.TypeLabel,
                percent = bay.TotalFillValue.Value,
            });
            if (bay.UnusableFillValue.Value > 0)
                s += ", " + Loc.T("cargo.bay_unusable", new { percent = bay.UnusableFillValue.Value });
            if (bay.IsNew) s += ", " + Loc.T("cargo.new");
            return s;
        }
    }
}
