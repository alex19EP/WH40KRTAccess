using Kingmaker.Code.UI.MVVM.VM.Slots;   // ItemSlotVM, ItemGrade, ILootHandler, IInventoryHandler
using Kingmaker.PubSubSystem.Core;       // EventBus
using Owlcat.Runtime.UI.Tooltips;        // TooltipBaseTemplate
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One item on either side of the open PlayerChest window (an <see cref="ItemSlotVM"/>) — the shared two-way stash.
    /// Reads the item name with the same visible badges as the inventory / loot lists (notable / unusable / rarity /
    /// count / charges) and carries the item's own tooltip. The Enter action depends on which side the item sits on,
    /// each driving the game's OWN per-slot handler (the same EventBus event the sighted slot click raises, routed to
    /// the open <c>LootVM</c>):
    /// <list type="bullet">
    /// <item><b>From the chest</b> (<paramref name="fromChest"/> true): Enter WITHDRAWS it to the party inventory —
    /// <c>ILootHandler.HandleChangeLoot</c> (for PlayerChest → <c>InventoryHelper.TryCollectLootSlot</c> →
    /// <c>GameCommandQueue.CollectLoot</c>).</item>
    /// <item><b>From the party inventory</b>: Enter DEPOSITS it into the chest —
    /// <c>IInventoryHandler.TryMoveToCargo(slot, false)</c>, which the PlayerChest <c>LootVM</c> routes to
    /// <c>InventoryHelper.TryTransferInventorySlot(slot, ContextLoot[1])</c> (the second view onto the chest
    /// collection) — despite the "Cargo" name, in PlayerChest mode the deferred move lands in the chest, not cargo.</item>
    /// </list>
    /// Drops out of nav once the item leaves its slot (the PlayerChestScreen rebuilds on every change).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyStashItem : UIElement
    {
        private readonly ItemSlotVM _slot;
        private readonly bool _fromChest;

        public ProxyStashItem(ItemSlotVM slot, bool fromChest) { _slot = slot; _fromChest = fromChest; }

        public override bool CanFocus => _slot != null && _slot.HasItem;

        // Match the loot / stash lists: a dense item list, so no per-item hover machine-gun (the game silences it too).
        public override Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? HoverSoundType
            => Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound;

        private string Name()
        {
            var name = _slot.DisplayName.Value;
            if (string.IsNullOrEmpty(name)) name = _slot.Item.Value?.Name ?? "item";
            var flags = new List<string>();
            if (_slot.IsNotable.Value) flags.Add("notable");
            if (!_slot.CanUse.Value) flags.Add("unusable");
            var grade = _slot.ItemGrade.Value;
            if (grade != ItemGrade.Common) flags.Add(grade.ToString().ToLowerInvariant());
            if (_slot.Count.Value > 1) flags.Add("x" + _slot.Count.Value);
            if (_slot.UsableCount.Value > 0) flags.Add(_slot.UsableCount.Value + " charges");
            return flags.Count > 0 ? name + " (" + string.Join(", ", flags.ToArray()) + ")" : name;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name()));
            yield return new RoleAnnouncement("item");
        }

        public override TooltipBaseTemplate GetTooltipTemplate()
        {
            // The item's own template is always LAST in the slot's list (equip-comparison templates precede it).
            var t = _slot.Tooltip.Value;
            return t != null && t.Count > 0 ? t[t.Count - 1] : null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (_slot == null || !_slot.HasItem) yield break;
            if (_fromChest)
                yield return new ElementAction(ActionIds.Activate, Message.Raw(Loc.T("stash.withdraw")), _ => Withdraw());
            else if (_slot.CanTransferToCargo) // the game's own gate for the deferred move (deposit reuses the cargo path)
                yield return new ElementAction(ActionIds.Activate, Message.Raw(Loc.T("stash.deposit")), _ => Deposit());
        }

        // Withdraw a chest item to the party inventory — the exact event the sighted chest slot click raises.
        private void Withdraw()
        {
            var name = Name();
            EventBus.RaiseEvent<ILootHandler>(h => h.HandleChangeLoot(_slot));
            Tts.Speak(Loc.T("stash.withdrawn", new { name }), interrupt: true);
        }

        // Deposit a party item into the chest — the exact event the sighted inventory slot click raises (immediately:
        // false, so the PlayerChest LootVM transfers it into the chest collection rather than to cargo).
        private void Deposit()
        {
            var name = Name();
            EventBus.RaiseEvent<IInventoryHandler>(h => h.TryMoveToCargo(_slot, false));
            Tts.Speak(Loc.T("stash.stored", new { name }), interrupt: true);
        }
    }
}
