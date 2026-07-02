using Kingmaker.Code.UI.MVVM.VM.Slots;   // ItemSlotVM, ItemGrade
using Kingmaker.UI.Common;               // InventoryHelper.TryCollectLootSlot
using Owlcat.Runtime.UI.Tooltips;        // TooltipBaseTemplate
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One item in an open loot window (a <see cref="ItemSlotVM"/> from a <c>LootObjectVM.SlotsGroup</c>). Reads the
    /// item name with the same visible badges as the inventory stash (notable / unusable / rarity grade / count /
    /// charges) and carries the item's own tooltip, so a blind player can weigh a pickup exactly like in the
    /// inventory. Enter TAKES the item to the party inventory via the game's own single-slot collect
    /// (<see cref="InventoryHelper.TryCollectLootSlot"/> → <c>GameCommandQueue.CollectLoot</c>) — the same command the
    /// loot window's own take-all uses per item. Drops out of nav once the item leaves the slot (the LootScreen
    /// rebuilds on every loot change). The bulk "take everything" lives on the LootScreen's Take-all button.
    ///
    /// Parallel to <see cref="ProxyInventoryItem"/> (same slot readout), but its actions are loot-take, not the
    /// inventory's equip/move/split/drop menu — a Loot-group slot isn't in the stash, so those don't apply.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyLootItem : UIElement
    {
        private readonly ItemSlotVM _slot;

        public ProxyLootItem(ItemSlotVM slot) { _slot = slot; }

        public override bool CanFocus => _slot != null && _slot.HasItem;

        // Match the inventory stash: a dense item list, so no per-item hover machine-gun (the game silences it too).
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
            // The item's own template is always LAST in the slot's list (equip-comparison templates precede it) —
            // the same end the game's ShowInfo reads.
            var t = _slot.Tooltip.Value;
            return t != null && t.Count > 0 ? t[t.Count - 1] : null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (_slot == null || !_slot.HasItem) yield break;
            yield return new ElementAction(ActionIds.Activate, Message.Raw(Loc.T("loot.take")), _ => Take());
        }

        private void Take()
        {
            var name = Name();
            if (InventoryHelper.TryCollectLootSlot(_slot))
                Tts.Speak(Loc.T("loot.taken", new { name }), interrupt: true);
        }
    }
}
