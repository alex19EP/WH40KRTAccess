using System;
using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.Loot;    // InsertableLootSlotVM, InteractionSlotPartVM
using Kingmaker.Code.UI.MVVM.VM.Slots;   // ItemSlotVM, ItemGrade, ILootHandler, IInventoryHandler
using Kingmaker.PubSubSystem.Core;       // EventBus
using Kingmaker.UI.Common;               // InventoryHelper
using Owlcat.Runtime.UI.Tooltips;        // TooltipBaseTemplate
using RTAccess.UI.Graph;

namespace RTAccess.UI
{
    /// <summary>
    /// Node factories for ITEM ROWS (an <see cref="ItemSlotVM"/> in a loot / stash / inventory grid) —
    /// the loot family first (the old ProxyLootItem / ProxyInsertItem / ProxyStashItem, factory-shaped);
    /// the inventory family joins here as those screens migrate. Shared conventions:
    /// <list type="bullet">
    /// <item>The browse label mirrors the CARD: display name + the visible badges folded in parentheses
    /// (notable / unusable / rarity grade / stack count / charges) — the same readout everywhere an item
    /// appears, so a blind player weighs a pickup exactly like in the inventory. Badges are localized
    /// (the proxies carried raw English — fixed in the port).</item>
    /// <item>Space = the item's OWN tooltip: the LAST of <c>slot.Tooltip.Value</c>'s templates
    /// (equip-comparison templates precede it — the same end the game's ShowInfo reads), resolved live
    /// per press and routed through <see cref="TooltipChooser"/> (body + inline glossary links).</item>
    /// <item>Hover is silent (<c>NoSound</c>) — dense item grids, which the game keeps quiet too;
    /// activation keeps the generic click the adapter's UIElement default played (the game's own
    /// loot/transfer sounds ride the command it fires).</item>
    /// <item>Every action drives the game's OWN handler (the exact call/event the sighted click makes);
    /// the post-action confirmation ("X taken.") rides <see cref="NodeVtable.StateText"/> — spoken
    /// interrupting by the navigator (keypress provenance), never hand-spoken. The action captures its
    /// outcome in the fresh-per-render vtable's closure because the game processes the move through a
    /// DEFERRED command queue: live slot state hasn't settled at StateText time, and a failed take must
    /// stay silent. The emptied slot's node then vanishes from the next render and the differ announces
    /// the landing on the nearest surviving row.</item>
    /// </list>
    /// </summary>
    public static class ItemNodes
    {
        /// <summary>The card's browse label: display name plus the badges the card shows, localized.</summary>
        public static string ItemLabel(ItemSlotVM slot)
        {
            var name = ItemName(slot);
            var flags = new List<string>();
            if (slot.IsNotable.Value) flags.Add(Loc.T("item.notable"));
            if (!slot.CanUse.Value) flags.Add(Loc.T("item.unusable"));
            var grade = slot.ItemGrade.Value;
            if (grade != ItemGrade.Common)
                flags.Add(Loc.T("item.grade." + grade.ToString().ToLowerInvariant()));
            if (slot.Count.Value > 1) flags.Add(Loc.T("item.count", new { count = slot.Count.Value }));
            if (slot.UsableCount.Value > 0) flags.Add(Loc.T("item.charges", new { count = slot.UsableCount.Value }));
            return flags.Count > 0 ? name + " (" + string.Join(", ", flags) + ")" : name;
        }

        /// <summary>The plain display name (no badges) — for spoken action feedback ("X taken.").</summary>
        public static string ItemName(ItemSlotVM slot)
        {
            var name = slot.DisplayName.Value;
            if (string.IsNullOrEmpty(name)) name = slot.Item.Value?.Name;
            return string.IsNullOrEmpty(name) ? Loc.T("item.unknown") : name;
        }

        /// <summary>One item in an open loot window. Enter TAKES it to the party inventory via the game's
        /// own single-slot collect (<see cref="InventoryHelper.TryCollectLootSlot"/> →
        /// <c>GameCommandQueue.CollectLoot</c> — the same command the window's take-all uses per item),
        /// confirming "{name} taken." on success.</summary>
        public static NodeVtable LootItem(ItemSlotVM slot)
        {
            string taken = null; // the activation outcome, read once by StateText (see class doc)
            return ItemRow(slot,
                activate: () =>
                {
                    var name = ItemName(slot);
                    taken = InventoryHelper.TryCollectLootSlot(slot) ? Loc.T("loot.taken", new { name }) : null;
                },
                stateText: () => taken);
        }

        /// <summary>One party item offered for insertion into a OneSlot device window. Enter INSERTS it
        /// via the game's own helper (<see cref="InventoryHelper.InsertToInteractionSlot"/> — the exact
        /// call <c>LootVM.HandleTryInsertSlot</c> makes for the sighted click), which first ejects
        /// whatever was already in the device back to the party. Inserting does NOT close the window
        /// (an authored put-trigger may fire). Only <c>CanInsert</c> items are surfaced by the screen,
        /// so the action is always live here (guarded defensively).</summary>
        public static NodeVtable InsertItem(InsertableLootSlotVM slot, InteractionSlotPartVM target)
        {
            string inserted = null;
            return ItemRow(slot,
                activate: () =>
                {
                    if (!slot.HasItem || !slot.CanInsert.Value) return;
                    var name = ItemName(slot);
                    InventoryHelper.InsertToInteractionSlot(slot, target);
                    inserted = Loc.T("insert.inserted_done", new { name });
                },
                stateText: () => inserted);
        }

        /// <summary>The item currently INSIDE a OneSlot device ("Inserted: {name}. Enter to remove."):
        /// Enter ejects it back to the party — the game's own single-slot collect, the same call
        /// OneSlot's <c>HandleChangeLoot</c> makes when the sighted UI clicks the filled slot. An action
        /// row rather than a grid item (the old ProxyActionButton), so it keeps the default hover.</summary>
        public static NodeVtable InsertedItem(ItemSlotVM inSlot)
        {
            string removed = null;
            Func<string> label = () => Loc.T("insert.inserted", new { name = ItemName(inSlot) });
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new List<NodeAnnouncement> { GraphNodes.LabelPart(label) },
                SearchText = label,
                OnActivate = () =>
                {
                    var name = ItemName(inSlot);
                    if (InventoryHelper.TryCollectLootSlot(inSlot))
                        removed = Loc.T("insert.removed", new { name });
                },
                StateText = () => removed,
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        /// <summary>One item on either side of the PlayerChest window (the shared two-way stash). Enter
        /// depends on the side, each driving the game's OWN per-slot handler (the same EventBus event the
        /// sighted slot click raises, routed to the open <c>LootVM</c>):
        /// <list type="bullet">
        /// <item><b>From the chest</b>: WITHDRAW to the party inventory — <c>ILootHandler.HandleChangeLoot</c>
        /// (for PlayerChest → <c>InventoryHelper.TryCollectLootSlot</c> → <c>GameCommandQueue.CollectLoot</c>).</item>
        /// <item><b>From the party inventory</b>: DEPOSIT into the chest —
        /// <c>IInventoryHandler.TryMoveToCargo(slot, immediately: false)</c>, which the PlayerChest
        /// <c>LootVM</c> routes to <c>TryTransferInventorySlot(slot, ContextLoot[1])</c> (the second view
        /// onto the chest collection): despite the "Cargo" name, the deferred move lands in the CHEST.
        /// Gated on <c>slot.CanTransferToCargo</c> — the game's own gate for the deferred move; an
        /// ineligible item advertises no action (the navigator's no-action feedback).</item>
        /// </list></summary>
        public static NodeVtable StashItem(ItemSlotVM slot, bool fromChest)
        {
            string moved = null;
            Action activate;
            if (fromChest)
                activate = () =>
                {
                    var name = ItemName(slot);
                    EventBus.RaiseEvent<ILootHandler>(h => h.HandleChangeLoot(slot));
                    moved = Loc.T("stash.withdrawn", new { name });
                };
            else if (slot.CanTransferToCargo)
                activate = () =>
                {
                    var name = ItemName(slot);
                    EventBus.RaiseEvent<IInventoryHandler>(h => h.TryMoveToCargo(slot, false));
                    moved = Loc.T("stash.stored", new { name });
                };
            else
                activate = null;
            return ItemRow(slot, activate, () => moved);
        }

        // The shared item-row shell: label + "item" role, silent hover (dense grid), the item's own
        // tooltip on Space, generic click on activation (the adapter's UIElement default — the game's
        // loot/transfer sounds ride the fired command).
        private static NodeVtable ItemRow(ItemSlotVM slot, Action activate, Func<string> stateText)
        {
            Func<string> label = () => ItemLabel(slot);
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement> { GraphNodes.LabelPart(label) },
                SearchText = label,
                OnActivate = activate,
                StateText = activate != null ? stateText : null,
                OnTooltip = () => TooltipChooser.OpenTemplate(ItemLabel(slot), OwnTemplate(slot)),
                HoverSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound,
                ActivateSound = activate != null
                    ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick
                    : null,
            };
        }

        // The item's OWN template: always LAST in the slot's list (equip-comparison templates precede
        // it) — resolved live per press; null (no tooltip) stays TooltipChooser's "No tooltip" case.
        private static TooltipBaseTemplate OwnTemplate(ItemSlotVM slot)
        {
            var t = slot.Tooltip.Value;
            return t != null && t.Count > 0 ? t[t.Count - 1] : null;
        }
    }
}
