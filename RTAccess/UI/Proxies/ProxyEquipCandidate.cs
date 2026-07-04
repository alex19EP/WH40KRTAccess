using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Code.UI.MVVM.VM.SelectorWindow;          // InventorySelectorWindowVM
using Kingmaker.UI.MVVM.VM.ServiceWindows.Inventory;     // EquipSelectorSlotVM
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One candidate in the empty-slot equip selector — the game's <see cref="InventorySelectorWindowVM"/>,
    /// opened from a doll slot via <c>InventoryDollVM.HandleChangeItem</c>. Each candidate
    /// (<see cref="EquipSelectorSlotVM"/>) carries its item's display name + tooltip, which we read directly.
    /// Enter equips it into the slot through the window's own <see cref="SelectorWindowVM{T}.Confirm"/> callback
    /// (the game's <c>GameCommandQueue.EquipItem</c>), which closes the window on success.
    ///
    /// When a slot is already filled the game inserts its worn item at the HEAD of the candidate list so it can
    /// be taken off; for that one row Enter instead calls <see cref="InventorySelectorWindowVM.Unequip"/> and the
    /// label gains an "(equipped)" marker. The equipped test / action are computed LIVE off the slot, so if an
    /// unequip leaves the window open the head row correctly flips back to a plain equip candidate.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyEquipCandidate : UIElement
    {
        private readonly EquipSelectorSlotVM _candidate;
        private readonly InventorySelectorWindowVM _selector;

        public ProxyEquipCandidate(EquipSelectorSlotVM candidate, InventorySelectorWindowVM selector)
        {
            _candidate = candidate;
            _selector = selector;
        }

        public override bool CanFocus => _candidate != null;

        // The head row the game adds to represent "take off what's on": its Item is the one currently in the
        // slot. Read live so an unequip mid-window re-evaluates (the slot goes empty → this is a plain equip).
        private bool IsEquipped =>
            _selector?.Slot != null && _selector.Slot.HasItem &&
            ReferenceEquals(_candidate.Item, _selector.Slot.Item.Value);

        private string BuildLabel()
        {
            var name = _candidate.DisplayName;
            if (string.IsNullOrEmpty(name)) name = _candidate.Item?.Name ?? "item";
            return IsEquipped ? name + " (" + Loc.T("inv.equipped") + ")" : name;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(BuildLabel()));
            yield return new RoleAnnouncement("item");
        }

        // The candidate's own item card (charges/type/stats all live in it) — read on Space.
        public override TooltipBaseTemplate GetTooltipTemplate() => _candidate?.Tooltip.Value;

        public override IEnumerable<ElementAction> GetActions()
        {
            if (_candidate == null || _selector == null) yield break;
            if (IsEquipped)
                yield return new ElementAction(ActionIds.Activate,
                    Message.Raw(UIStrings.Instance.ContextMenu.TakeOff.Text), _ => _selector.Unequip());
            else
                yield return new ElementAction(ActionIds.Activate,
                    Message.Raw(UIStrings.Instance.ContextMenu.Equip.Text), _ => _selector.Confirm(_candidate));
        }
    }
}
