using Kingmaker.Code.UI.MVVM.VM.Loot;    // InsertableLootSlotVM, InteractionSlotPartVM
using Kingmaker.Code.UI.MVVM.VM.Slots;   // ItemSlotVM, ItemGrade
using Kingmaker.UI.Common;               // InventoryHelper.InsertToInteractionSlot
using Owlcat.Runtime.UI.Tooltips;        // TooltipBaseTemplate
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One party item offered for insertion into an open OneSlot device window (an
    /// <see cref="InsertableLootSlotVM"/> from <c>LootVM.InventoryStash.InsertableSlotsGroup</c>). Reads the item name
    /// with the same visible badges as the loot / inventory lists (notable / unusable / rarity grade / count / charges)
    /// and carries the item's own tooltip, so a blind player can weigh the choice exactly like elsewhere. Enter INSERTS
    /// it into the device slot via the game's own helper (<see cref="InventoryHelper.InsertToInteractionSlot"/>) — the
    /// same call <c>LootVM.HandleTryInsertSlot</c> makes when the sighted UI clicks the slot — which first ejects
    /// whatever was already in the device (back to the party) and then transfers this item in. Drops out of nav once
    /// the item leaves the party (the OneSlotLootScreen rebuilds on every change).
    ///
    /// Parallel to <see cref="ProxyLootItem"/> (same slot readout), but its action is device-insert, not loot-take. Only
    /// <see cref="CanInsert"/> items are surfaced by the screen, so the Insert action is always live here (guarded
    /// defensively).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyInsertItem : UIElement
    {
        private readonly InsertableLootSlotVM _slot;
        private readonly InteractionSlotPartVM _target;

        public ProxyInsertItem(InsertableLootSlotVM slot, InteractionSlotPartVM target) { _slot = slot; _target = target; }

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
            if (_slot == null || !_slot.HasItem || !_slot.CanInsert.Value) yield break;
            yield return new ElementAction(ActionIds.Activate, Message.Raw(Loc.T("insert.insert")), _ => Insert());
        }

        private void Insert()
        {
            var name = Name();
            InventoryHelper.InsertToInteractionSlot(_slot, _target);
            Tts.Speak(Loc.T("insert.inserted_done", new { name }), interrupt: true);
        }
    }
}
