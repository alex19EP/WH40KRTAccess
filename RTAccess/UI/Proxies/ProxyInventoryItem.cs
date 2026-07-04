using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Cargo;
using Kingmaker.Code.UI.MVVM.VM.Slots;
using Kingmaker.GameCommands;
using Kingmaker.Items;
using Kingmaker.PubSubSystem.Core;
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.Screens;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One stash item — the Name cell of a row in the inventory stash table. The game's icon tile carries no
    /// text; all of it lives on <see cref="ItemSlotVM"/>, which we read directly. The cell announces the item
    /// name with its visible badges folded in (notable / unusable / rarity grade / count / charges /
    /// favorite), carries the item's tooltip (Space → the LAST entry, which is the item's own template), and
    /// exposes the inventory actions: Enter equips equippable items, and the secondary key opens the full
    /// context menu (send to cargo / inventory, split, drop, favorite) as a submenu — each entry gated by the
    /// same live VM predicate the game uses. Actions route through the same EventBus / GameCommandQueue
    /// contracts <c>InventorySlotView</c> uses, so we don't reimplement equip/move/split. The VM's own
    /// <c>ContextMenu</c> and <c>IsFavorite</c> are populated by the game's view (absent in our headless
    /// screen), so we read the item's favorite state directly off the entity. Drops out of nav once the item
    /// leaves the slot.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyInventoryItem : UIElement
    {
        private readonly ItemSlotVM _slot;

        public ProxyInventoryItem(ItemSlotVM slot) { _slot = slot; }

        public override bool CanFocus => _slot != null && _slot.HasItem;

        // The game silences item-slot hover on PC (InventorySlotPCView.SetMainButtonHoverSound(NoSound)) —
        // a dense grid the devs keep quiet — so we don't machine-gun a hover sound arrowing the stash. Click
        // stays the generic ButtonClick (the slot's NormalSound); equip/move play their own item sounds.
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
            if (_slot.Item.Value != null && _slot.Item.Value.IsFavorite) flags.Add("favorite");
            return flags.Count > 0 ? name + " (" + string.Join(", ", flags.ToArray()) + ")" : name;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Name()));
            yield return new RoleAnnouncement("item");
        }

        public override TooltipBaseTemplate GetTooltipTemplate()
        {
            // The slot packs the EQUIPPED items' comparison templates first; the focused item's own template
            // is always LAST — the same end the game's ShowInfo reads (Tooltip.Value.LastItem()).
            var t = _slot.Tooltip.Value;
            return t != null && t.Count > 0 ? t[t.Count - 1] : null;
        }

        // Space drills into the item's own card (above); when the slot also carries compare-vs-equipped
        // templates (the LEADING entries — the equipped items rendered with deltas against this item), expose
        // them as extra sections so Space opens a drill menu: the item first, then each comparison. Mirrors
        // the game's hover, which shows the item card alongside the equipped items it would replace.
        public override IReadOnlyList<(string label, string body)> GetTooltipSections()
        {
            var t = _slot?.Tooltip.Value;
            if (t == null || t.Count <= 1) return null; // count 1 = own card only, no comparison
            int compares = t.Count - 1;
            var list = new List<(string, string)>();
            for (int i = 0; i < compares; i++)
            {
                var body = RTAccess.Accessibility.TooltipReader.GetFull(t[i]);
                if (string.IsNullOrWhiteSpace(body)) continue;
                var label = compares > 1 ? Loc.T("inv.compare_n", new { index = i + 1 }) : Loc.T("inv.compare");
                list.Add((label, body));
            }
            return list.Count > 0 ? list : null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (_slot == null || !_slot.HasItem) yield break;
            // Enter = the game's double-click quick action (equip), only when the item can be equipped.
            if (_slot.IsEquipPossible)
                yield return new ElementAction(ActionIds.Activate, Message.Raw("Equip"), _ => Equip());
            // Secondary = the whole context menu.
            yield return new ElementAction(ActionIds.Context, Message.Raw("Actions"), _ => OpenMenu());
        }

        // The live context-menu set — mirrors InventorySlotPCView.SetupContextMenu, each entry gated by the
        // same VM predicate, evaluated now (so a non-applicable action just isn't listed). Labels are the
        // game's own localized context-menu strings (pass-through game content).
        private void OpenMenu()
        {
            var labels = new List<string>();
            var runs = new List<Action>();
            void Add(bool when, string label, Action run) { if (when) { labels.Add(label); runs.Add(run); } }

            var item = _slot.Item.Value;
            var cm = UIStrings.Instance.ContextMenu;

            Add(_slot.IsEquipPossible, cm.Equip.Text, Equip);
            Add(_slot.IsInStash && _slot.CanTransferToCargo, UIStrings.Instance.LootWindow.SendToCargo.Text, MoveToCargo);
            Add(_slot.SlotsGroupType == ItemSlotsGroupType.Cargo && _slot.CanTransferToInventory,
                UIStrings.Instance.LootWindow.SendToInventory.Text, MoveToInventory);
            Add(_slot.IsPosibleSplit, cm.Split.Text, Split);
            Add(_slot.HasItem && _slot.IsInStash, cm.Drop.Text, Drop);
            Add(_slot.HasItem, (item != null && item.IsFavorite) ? cm.RemoveFromFav.Text : cm.AddToFav.Text, ToggleFavorite);
            // Auto-add-to-Cargo: a per-item toggle (game shows a checkmark) — we append its on/off state.
            Add(item != null && CargoHelper.CanTransferFromCargo(item) && CargoHelper.CanTransferToCargo(item),
                cm.AutoAddToCargo.Text + ", " + Loc.T(item != null && item.ToCargoAutomatically ? "value.on" : "value.off"),
                () => _slot.AddToCargoAutomatically());

            if (labels.Count == 0) { Tts.Speak(Loc.T("inv.no_actions"), interrupt: true); return; }
            var actions = runs;
            ChoiceSubmenuScreen.Open(Name(), labels, -1, idx => { if (idx >= 0 && idx < actions.Count) actions[idx]?.Invoke(); });
        }

        // Action verbs, routed exactly like InventorySlotView (EventBus → InventoryVM) / GameCommandQueue.
        private void Equip() => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryEquip(_slot));
        private void Drop() => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryDrop(_slot));
        private void Split() => EventBus.RaiseEvent<INewSlotsHandler>(h => h.HandleTrySplitSlot(_slot));
        private void MoveToCargo() => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryMoveToCargo(_slot, true));
        private void MoveToInventory() => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryMoveToInventory(_slot, true));

        private void ToggleFavorite()
        {
            var item = _slot.Item.Value;
            if (item != null) Game.Instance.GameCommandQueue.AddRemoveItemAsFavorite(item);
        }
    }
}
