using System.Collections.Generic;
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
    /// GameCommandQueue, combat-guarded) + the Refresh event the view raises. Empty slots are read-only here
    /// (equipping into one opens the game's selector sub-window — deferred to a later slice); equip an item
    /// from its stash row instead.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyEquipSlot : UIElement
    {
        private readonly string _slotName;
        private readonly EquipSlotVM _slot;

        public ProxyEquipSlot(string slotName, EquipSlotVM slot) { _slotName = slotName; _slot = slot; }

        public override bool CanFocus => true; // the slot is always shown, even when empty

        private bool HasItem => _slot != null && _slot.HasItem;

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
            if (!HasItem) yield break;
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Take off"), _ => Unequip());
        }

        // Unequip via the game's helper (combat-guarded, routes GameCommandQueue.UnequipItem); on success
        // raise the same Refresh the view raises so the doll + stash rebuild.
        private void Unequip()
        {
            if (InventoryHelper.TryUnequip(_slot))
                EventBus.RaiseEvent<IInventoryHandler>(h => h.Refresh());
        }
    }
}
