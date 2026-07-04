using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;
using Kingmaker.Code.UI.MVVM.VM.Slots;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.UI.Common;
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One equipment-doll slot (<see cref="EquipSlotVM"/>, which extends <see cref="ItemSlotVM"/>) as a list
    /// line — "Slot: item" (or "Slot: empty"). Reads live from the VM: announces the slot name + equipped
    /// item with badges folded in (notable / can't remove), carries the item's tooltip, and on Enter takes
    /// the item off via the game's own unequip path (<see cref="InventoryHelper.TryUnequip(EquipSlotVM)"/> →
    /// GameCommandQueue, combat-guarded) + the Refresh event the view raises.
    ///
    /// Enter on an EMPTY slot — and "Choose item" (the secondary key) on a FILLED one — opens the game's own
    /// equip selector via <see cref="InventoryDollVM.HandleChangeItem"/>, which the mod-owned
    /// <c>EquipSelectorScreen</c> makes navigable. Both are gated on the doll's <c>CanChangeEquipment</c> so we
    /// only offer the affordance when the sighted UI would (mirrors the game hiding its change button in
    /// combat / for pets / on non-controllable units).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyEquipSlot : UIElement
    {
        private readonly string _slotName;
        private readonly EquipSlotVM _slot;
        private readonly InventoryDollVM _doll;

        public ProxyEquipSlot(string slotName, EquipSlotVM slot, InventoryDollVM doll)
        {
            _slotName = slotName;
            _slot = slot;
            _doll = doll;
        }

        public override bool CanFocus => true; // the slot is always shown, even when empty

        private bool HasItem => _slot != null && _slot.HasItem;

        // Whether the game would let this doll change equipment right now (hidden in combat / for pets /
        // non-controllable units) — the same gate that shows/hides the sighted "change" affordance.
        private bool CanChange => _doll != null && _slot != null && _doll.CanChangeEquipment.Value;

        private string ItemLabel()
        {
            if (!HasItem) return _slotName + ": empty";
            var name = _slot.DisplayName.Value;
            if (string.IsNullOrEmpty(name)) name = _slot.Item.Value?.Name ?? "item";
            var flags = new List<string>();
            if (_slot.IsNotable.Value) flags.Add("notable");
            if (_slot.IsNotRemovable.Value) flags.Add("can't remove");
            if (flags.Count > 0) name += " (" + string.Join(", ", flags.ToArray()) + ")";
            return _slotName + ": " + name;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(ItemLabel()));
            yield return new RoleAnnouncement("item");
        }

        public override TooltipBaseTemplate GetTooltipTemplate()
        {
            // Read the LAST entry — the item's own template by the slot contract (game's ShowInfo does the same).
            if (!HasItem) return null;
            var t = _slot.Tooltip.Value;
            return t != null && t.Count > 0 ? t[t.Count - 1] : null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (HasItem)
            {
                yield return new ElementAction(ActionIds.Activate, Message.Raw(UIStrings.Instance.ContextMenu.TakeOff.Text), _ => Unequip());
                // Swap: the game's "choose a different item for this slot" — the selector lists the equipped item
                // (to take off) plus every other equippable party item.
                if (CanChange)
                    yield return new ElementAction(ActionIds.Context, Message.Raw(UIStrings.Instance.InventoryScreen.ChooseItem.Text), _ => OpenSelector());
            }
            else if (CanChange)
            {
                // Empty slot: Enter opens the equip selector directly (there's nothing to take off).
                yield return new ElementAction(ActionIds.Activate, Message.Raw(UIStrings.Instance.InventoryScreen.ChooseItem.Text), _ => OpenSelector());
            }
        }

        // Open the game's own equip-selector sub-window for this slot (EquipSelectorScreen mirrors it). Handles
        // the locked-slot / nothing-to-insert warnings itself; sets doll.InventorySelectorWindowVM on success.
        private void OpenSelector() => _doll?.HandleChangeItem(_slot);

        // Unequip via the game's helper (combat-guarded, routes GameCommandQueue.UnequipItem); on success
        // raise the same Refresh the view raises so the doll + stash rebuild.
        private void Unequip()
        {
            if (InventoryHelper.TryUnequip(_slot))
                EventBus.RaiseEvent<IInventoryHandler>(h => h.Refresh());
        }
    }
}
