using System;
using System.Collections.Generic;
using Kingmaker;                                              // Game (favorite command)
using Kingmaker.Blueprints.Items.Augments;                    // BlueprintItemAugment (equip guard)
using Kingmaker.Blueprints.Root.Strings;                      // UIStrings (context-menu labels)
using Kingmaker.Cargo;                                        // CargoHelper (auto-add-to-cargo gate)
using Kingmaker.Code.UI.MVVM.VM.ContextMenu;                  // ContextMenuCollectionEntity (the game's menu model)
using Kingmaker.Code.UI.MVVM.VM.Loot;    // InsertableLootSlotVM, InteractionSlotPartVM
using Kingmaker.Code.UI.MVVM.VM.MessageBox;                   // DialogMessageBoxBase (search text entry)
using Kingmaker.Code.UI.MVVM.VM.SelectorWindow;               // InventorySelectorWindowVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;     // EquipSlotVM, InventoryDollVM
using Kingmaker.Code.UI.MVVM.VM.Slots;   // ItemSlotVM, ItemGrade, ILootHandler, IInventoryHandler
using Kingmaker.GameCommands;            // GameCommandQueueExtensions (AddRemoveItemAsFavorite)
using Kingmaker.PubSubSystem;                                 // IDialogMessageBoxUIHandler
using Kingmaker.PubSubSystem.Core;       // EventBus
using Kingmaker.UI.Common;               // InventoryHelper
using Kingmaker.UI.MVVM.VM.ServiceWindows.Inventory;          // EquipSelectorSlotVM (the OTHER namespace)
using Warhammer.SpaceCombat.Blueprints;                       // BlueprintStarshipItem (equip guard)
using Owlcat.Runtime.UI.Tooltips;        // TooltipBaseTemplate
using RTAccess.Accessibility;                                 // TooltipReader, GlossaryLinks
using RTAccess.UI.Graph;

namespace RTAccess.UI
{
    /// <summary>
    /// Node factories for ITEM ROWS (an <see cref="ItemSlotVM"/> in a loot / stash / inventory grid) —
    /// the loot family (the old ProxyLootItem / ProxyInsertItem / ProxyStashItem, factory-shaped) and the
    /// inventory family (the old ProxyInventoryItem / ProxyEquipSlot / ProxyEquipCandidate, plus the
    /// stash search edit field). Shared conventions:
    /// <list type="bullet">
    /// <item>The browse label mirrors the CARD: display name + the visible badges folded in parentheses
    /// (notable / unusable / rarity grade / stack count / charges) — the same readout everywhere an item
    /// appears, so a blind player weighs a pickup exactly like in the inventory. Badges are localized
    /// (the proxies carried raw English — fixed in the port).</item>
    /// <item>Space = the item's OWN tooltip: the LAST of <c>slot.Tooltip.Value</c>'s templates
    /// (equip-comparison templates precede it — the same end the game's ShowInfo reads), resolved live
    /// per press and routed through <see cref="TooltipChooser"/> (body + inline glossary links).</item>
    /// <item>Hover is silent (<c>NoSound</c>) — dense item grids, which the game keeps quiet too;
    /// activation keeps the generic click (the game's own
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
        /// <summary>The card's browse label: display name plus the badges the card shows, localized.
        /// <paramref name="withFavorite"/> folds in the favorite star only the INVENTORY card overlays
        /// (<c>InventorySlotView.m_FavBlock</c>; loot cards don't show it) — read off the item ENTITY,
        /// because the slot VM's <c>IsFavorite</c> reactive is view-populated and absent headless.</summary>
        public static string ItemLabel(ItemSlotVM slot, bool withFavorite = false)
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
            if (withFavorite && slot.Item.Value != null && slot.Item.Value.IsFavorite)
                flags.Add(Loc.T("item.favorite"));
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

        // ---- the inventory family (the old ProxyInventoryItem / ProxyEquipSlot / ProxyEquipCandidate,
        // factory-shaped as the inventory window migrates) ----

        /// <summary>One stash item in the inventory window. The label mirrors the card (name + badges +
        /// favorite star) and is LIVE: a same-identity change under focus (a split shrinking the stack, the
        /// favorite toggling) re-speaks it — the differ's blind spot covered by the announcement watch.
        /// Enter = the sighted DOUBLE-CLICK quick action (<c>InventorySlotView.OnDoubleClick</c> → equip)
        /// when the item is equippable, else it falls through to the context menu so Enter always does
        /// something; Backspace = the full predicate-gated context menu mirroring
        /// <c>InventorySlotPCView.SetupContextMenu</c> (each entry's gate evaluated at open, labels the
        /// game's own); Space = the item's OWN card plus any leading compare-vs-equipped templates as
        /// drill-in sections. An equipped/dropped/moved item's node vanishes from the next render and the
        /// differ announces the landing row.</summary>
        public static NodeVtable InventoryItem(ItemSlotVM slot)
        {
            Func<string> label = () => ItemLabel(slot, withFavorite: true);
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                },
                SearchText = label,
                OnActivate = () => { if (slot.IsEquipPossible) Equip(slot); else OpenStashMenu(slot); },
                OnSecondary = () => OpenStashMenu(slot),
                OnTooltip = () => OpenItemTooltip(slot),
                HoverSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound,
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        /// <summary>One equipment-doll slot — "Slot: item (badges)" / "Slot: empty". The label is LIVE:
        /// the slot is a fixed node that stays put while its CONTENT changes (the differ's same-identity
        /// blind spot), so a take-off settling through the deferred command queue re-speaks the emptied
        /// slot under focus. Enter takes the item off via the game's own unequip path (or, on an empty
        /// slot, opens the game's equip selector — <c>InventoryDollVM.HandleChangeItem</c>, surfaced by
        /// EquipSelectorScreen); Backspace on a filled slot opens the selector to swap. Both selector
        /// affordances gate on the doll's <c>CanChangeEquipment</c> — the same gate that hides the sighted
        /// change button in combat / for pets / on non-controllable units.</summary>
        public static NodeVtable EquipSlot(string slotName, EquipSlotVM slot, InventoryDollVM doll)
        {
            Func<string> label = () => EquipSlotLabel(slotName, slot);
            bool hasItem = slot.HasItem;
            bool canChange = doll != null && doll.CanChangeEquipment.Value;
            Action activate = hasItem ? (Action)(() => Unequip(slot))
                : canChange ? (Action)(() => doll.HandleChangeItem(slot))
                : null;
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                },
                SearchText = label,
                OnActivate = activate,
                OnSecondary = hasItem && canChange ? (Action)(() => doll.HandleChangeItem(slot)) : null,
                OnTooltip = () => TooltipChooser.OpenTemplate(label(), slot.HasItem ? OwnTemplate(slot) : null),
                ActivateSound = activate != null
                    ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick
                    : null,
            };
        }

        /// <summary>One candidate row of the equip-selector window (<see cref="InventorySelectorWindowVM"/>).
        /// The game inserts the currently WORN item at the head of the list; that row is detected LIVE
        /// (an unequip that keeps the window open flips it back to a plain candidate — the live label
        /// announces the flip), gains an "(equipped)" marker, and Enter takes it off via the window's own
        /// <c>Unequip</c>; every other row Enter equips through the window's own <c>Confirm</c> (the
        /// game's EquipItem command, which closes the window on success). Space = the candidate's own
        /// item card (a single template, not a slot list).</summary>
        public static NodeVtable EquipCandidate(EquipSelectorSlotVM candidate, InventorySelectorWindowVM selector)
        {
            Func<bool> equipped = () => selector?.Slot != null && selector.Slot.HasItem
                && ReferenceEquals(candidate.Item, selector.Slot.Item.Value);
            Func<string> label = () =>
            {
                var name = candidate.DisplayName;
                if (string.IsNullOrEmpty(name)) name = candidate.Item?.Name;
                if (string.IsNullOrEmpty(name)) name = Loc.T("item.unknown");
                return equipped() ? name + " (" + Loc.T("inv.equipped") + ")" : name;
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                },
                SearchText = label,
                OnActivate = () => { if (equipped()) selector.Unequip(); else selector.Confirm(candidate); },
                OnTooltip = () => TooltipChooser.OpenTemplate(label(), candidate.Tooltip.Value),
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        /// <summary>An edit field over a model string (the stash search): "label, edit, current-or-blank".
        /// No control type — an explicit edit role part (the WA idiom; there's no edit ControlType). Enter
        /// opens the game's own text-field message box (surfaced by MessageBoxScreen, which drives the live
        /// field) pre-filled with the current text; Accept writes THROUGH the model setter — accepting an
        /// emptied field clears the search — while Decline changes nothing (told apart via the box's button
        /// callback, since Decline also reports empty text). The value part is LIVE, so the applied text
        /// (or "blank") announces itself back on the field.</summary>
        public static NodeVtable SearchField(Func<string> label, Func<string> current, Action<string> apply)
        {
            Func<string> value = () =>
            {
                var v = current?.Invoke();
                return string.IsNullOrEmpty(v) ? Loc.T("value.blank") : v;
            };
            return new NodeVtable
            {
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(() => Loc.T("role.edit"), kind: AnnouncementKinds.Role),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = label,
                OnActivate = () =>
                {
                    bool accepted = false;
                    EventBus.RaiseEvent<IDialogMessageBoxUIHandler>(h => h.HandleOpen(
                        Loc.T("inv.search_prompt"), DialogMessageBoxBase.BoxType.TextField,
                        btn => accepted = btn == DialogMessageBoxBase.BoxButton.Yes, null, null, null,
                        text => { if (accepted) apply?.Invoke(text ?? string.Empty); },
                        inputText: current?.Invoke()));
                },
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        // ---- the vendor family (the RT trade window: Profit-Factor-threshold buys + send-to-cargo;
        // there is no item selling — cargo sells for reputation on the vendor's Reputation tab) ----

        /// <summary>One item in the vendor's wares list. The label mirrors the vendor card (name +
        /// badges); the card's Type and Cost texts ride as extra parts (the same values the screen
        /// emits as sheet columns), and the lock state (reputation / Profit Factor) as a live part
        /// using the game's OWN refusal strings. Enter = the sighted single click —
        /// <see cref="ItemSlotVM.VendorTryMove"/>, which raises the game's transition window (the
        /// quantity/confirm dialog VendorBuyScreen surfaces) — gated through the game's own
        /// <c>CanBuy()</c> so a locked item speaks the game's warning instead (publicized private,
        /// the exact gate <c>VendorTryBuyAll</c> uses). Backspace = the card's context menu (Buy =
        /// whole stack instantly / Information), mirroring <c>VendorSlotPCView.SetupContextMenu</c>;
        /// Space = the item's own card.</summary>
        public static NodeVtable VendorWaresItem(ItemSlotVM slot)
        {
            Func<string> label = () => ItemLabel(slot);
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(() => slot.TypeName.Value),
                    new NodeAnnouncement(() => VendorCostLabel(slot), live: true),
                    new NodeAnnouncement(() => VendorLockLabel(slot), live: true),
                },
                SearchText = label,
                OnActivate = () => { if (slot.CanBuy()) slot.VendorTryMove(split: false); },
                OnSecondary = () => OpenWaresMenu(slot),
                OnTooltip = () => OpenItemTooltip(slot),
                HoverSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound,
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        /// <summary>The vendor card's cost text: the current Profit Factor cost, with the crossed-out
        /// pre-discount price folded in when the card shows the discount block. Empty when the card
        /// hides the cost (0 — e.g. the item is already in a deal).</summary>
        public static string VendorCostLabel(ItemSlotVM slot)
        {
            double cost = slot.CurrentCostPF.Value;
            if (cost <= 0.0) return "";
            string value = cost.ToString("0.#");
            if (slot.HasDiscount && slot.PriceWithoutDiscountPF.Value > 0.0)
                return Loc.T("vendor.cost_was", new { value, old = slot.PriceWithoutDiscountPF.Value.ToString("0.#") });
            return Loc.T("vendor.cost", new { value });
        }

        // The lock state the card greys the slot for, spoken with the game's own refusal strings
        // (the same texts CanBuy toasts). Empty when buyable.
        private static string VendorLockLabel(ItemSlotVM slot)
        {
            if (Game.Instance?.Vendor?.VendorInventory == null) return "";
            var flags = new List<string>();
            if (slot.IsLockedByRep) flags.Add(UIStrings.Instance.Vendor.NotEnoughReputation.Text);
            if (slot.IsLockedByCost) flags.Add(UIStrings.Instance.Vendor.NotEnoughProfitFactor.Text);
            return string.Join(", ", flags);
        }

        // The vendor card's right-click menu, rebuilt from VM state exactly like OpenStashMenu (the
        // live view's SetupContextMenu never runs for our parallel rows): Buy (the whole stack,
        // instant — the game's own double-click/menu path with its lock warnings) + Information.
        private static void OpenWaresMenu(ItemSlotVM slot)
        {
            var cm = UIStrings.Instance.ContextMenu;
            var entities = new List<ContextMenuCollectionEntity>
            {
                new ContextMenuCollectionEntity(cm.Buy, slot.VendorTryBuyAll, slot.HasItem),
                new ContextMenuCollectionEntity(cm.Information, () => OpenItemTooltip(slot), slot.HasItem),
            };
            ContextMenuNodes.Open(ItemLabel(slot), entities,
                onEmpty: () => Tts.Speak(Loc.T("inv.no_actions"), interrupt: true));
        }

        /// <summary>One party-stash item shown at the vendor. There is NO selling — the stash exists
        /// here so items can be converted to cargo (the sighted drag-to-drop-zone). Enter sends the
        /// item to cargo via the game's own <see cref="InventoryHelper.TryMoveToCargo"/> (combat-gated,
        /// plays the game's sound, queues the same <c>TransferItemsToCargo</c> command the drop zone
        /// fires — the EventBus route is dead here: <c>VendorVM.IInventoryHandler.TryMoveToCargo</c> is
        /// a no-op). Ineligible items advertise no action. Backspace = the verbs that actually function
        /// at the vendor (Send to cargo / Split / Information); Space = the item's card.</summary>
        public static NodeVtable VendorStashItem(ItemSlotVM slot)
        {
            string moved = null;
            Func<string> label = () => ItemLabel(slot, withFavorite: true);
            Action activate = slot.CanTransferToCargo
                ? () =>
                {
                    var name = ItemName(slot);
                    if (InventoryHelper.TryMoveToCargo(slot))
                        moved = Loc.T("vendor.sent_to_cargo", new { name });
                }
                : (Action)null;
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement> { GraphNodes.LabelPart(label) },
                SearchText = label,
                OnActivate = activate,
                StateText = activate != null ? () => moved : (Func<string>)null,
                OnSecondary = () => OpenVendorStashMenu(slot),
                OnTooltip = () => OpenItemTooltip(slot),
                HoverSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound,
                ActivateSound = activate != null
                    ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick
                    : null,
            };
        }

        // The stash menu variant for the vendor window: only the verbs whose routes are live here.
        // OpenStashMenu's EventBus verbs (Equip/Drop/SendToCargo via IInventoryHandler) dead-end in
        // VendorVM's empty handler stubs, so this menu drives working paths instead.
        private static void OpenVendorStashMenu(ItemSlotVM slot)
        {
            var cm = UIStrings.Instance.ContextMenu;
            var loot = UIStrings.Instance.LootWindow;
            var entities = new List<ContextMenuCollectionEntity>
            {
                new ContextMenuCollectionEntity(loot.SendToCargo,
                    () => InventoryHelper.TryMoveToCargo(slot),
                    true, slot.CanTransferToCargo),
                // Split — VendorVM DOES implement HandleTrySplitSlot (routes InventoryHelper.TrySplitSlot).
                new ContextMenuCollectionEntity(cm.Split,
                    () => EventBus.RaiseEvent<INewSlotsHandler>(h => h.HandleTrySplitSlot(slot)),
                    slot.IsPosibleSplit),
                new ContextMenuCollectionEntity(cm.Information, () => OpenItemTooltip(slot), slot.HasItem),
            };
            ContextMenuNodes.Open(ItemLabel(slot, withFavorite: true), entities,
                onEmpty: () => Tts.Speak(Loc.T("inv.no_actions"), interrupt: true));
        }

        // "Slot: item (badges)" / "Slot: empty" — the doll card's readout. Badges here are the pair the
        // doll card overlays: notable, and the can't-remove lock.
        private static string EquipSlotLabel(string slotName, EquipSlotVM slot)
        {
            if (slot == null || !slot.HasItem) return slotName + ": " + Loc.T("slot.empty");
            var name = ItemName(slot);
            var flags = new List<string>();
            if (slot.IsNotable.Value) flags.Add(Loc.T("item.notable"));
            if (slot.IsNotRemovable.Value) flags.Add(Loc.T("item.cant_remove"));
            if (flags.Count > 0) name += " (" + string.Join(", ", flags) + ")";
            return slotName + ": " + name;
        }

        // The item context menu, built as the game's OWN entity list (a ContextMenuCollectionEntity per verb)
        // and surfaced through the shared ContextMenuNodes driver — the same model the game's right-click uses.
        // Entries, order, labels, Condition (⇒ shown) and IsInteractable (⇒ clickable, else greyed) mirror
        // InventorySlotPCView.SetupContextMenu EXACTLY, so the menu can't drift from the game the way a
        // hand-picked subset did. We can't read ItemSlotVM.ContextMenu directly here: it is populated by the
        // live PC VIEW's SetupContextMenu, which never runs for the virtualized off-screen slots the parallel
        // nav tree still walks — so we rebuild the identical list from VM state, each verb still routing
        // through the game's own EventBus / GameCommandQueue contracts (never a reimplemented flow).
        private static void OpenStashMenu(ItemSlotVM slot)
        {
            var item = slot.Item.Value;
            var cm = UIStrings.Instance.ContextMenu;
            var loot = UIStrings.Instance.LootWindow;
            // The starship/augment equip guard only applies while the Inventory window is showing (the game
            // reads RootUiContext.IsInventoryShow) — off it, the same slot in a loot window CAN equip them.
            bool inv = Game.Instance.RootUiContext.IsInventoryShow;
            bool isStarship = inv && item?.Blueprint is BlueprintStarshipItem;
            bool isAugment = inv && item?.Blueprint is BlueprintItemAugment;

            var entities = new List<ContextMenuCollectionEntity>
            {
                // Equip — shown when equippable in principle and not a starship/augment item; clickable only
                // when there's an unlocked slot to take it (else greyed, as the game shows it).
                new ContextMenuCollectionEntity(cm.Equip, () => Equip(slot),
                    slot.IsEquipPossible && !(isStarship || isAugment), slot.IsEquipToUnlockedSlotPossible),
                // Send to Cargo — always listed in the stash, greyed unless the item can transfer.
                new ContextMenuCollectionEntity(loot.SendToCargo,
                    () => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryMoveToCargo(slot, true)),
                    true, slot.IsInStash && slot.CanTransferToCargo),
                // Send to Inventory — the cargo-side counterpart.
                new ContextMenuCollectionEntity(loot.SendToInventory,
                    () => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryMoveToInventory(slot, true)),
                    true, slot.SlotsGroupType == ItemSlotsGroupType.Cargo && slot.CanTransferToInventory),
                // Favorite toggle — label reflects the current state; clickable while an item is present.
                new ContextMenuCollectionEntity((item != null && item.IsFavorite) ? cm.RemoveFromFav : cm.AddToFav,
                    () => { var it = slot.Item.Value; if (it != null) Game.Instance.GameCommandQueue.AddRemoveItemAsFavorite(it); },
                    true, item != null),
                // Auto-add-to-Cargo — a per-item toggle; the game shows a checkmark, we fold the on/off state
                // into the label (a plain-string entity so the driver reads the composed text).
                new ContextMenuCollectionEntity(
                    cm.AutoAddToCargo.Text + ", " + Loc.T(item != null && item.ToCargoAutomatically ? "value.on" : "value.off"),
                    () => slot.AddToCargoAutomatically(),
                    item != null && CargoHelper.CanTransferFromCargo(item) && CargoHelper.CanTransferToCargo(item)),
                // Split — a stack of more than one.
                new ContextMenuCollectionEntity(cm.Split,
                    () => EventBus.RaiseEvent<INewSlotsHandler>(h => h.HandleTrySplitSlot(slot)),
                    slot.IsPosibleSplit),
                // Drop — the game gates this on CanEquip (not "in stash"); mirror it exactly.
                new ContextMenuCollectionEntity(cm.Drop,
                    () => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryDrop(slot)),
                    slot.CanEquip),
                // Information — the item's own card (our accessible equivalent of the game's ShowInfo).
                new ContextMenuCollectionEntity(cm.Information, () => OpenItemTooltip(slot), slot.HasItem),
            };

            ContextMenuNodes.Open(ItemLabel(slot, withFavorite: true), entities,
                onEmpty: () => Tts.Speak(Loc.T("inv.no_actions"), interrupt: true));
        }

        // Space on a stash item: its own card (the LAST template), plus any LEADING compare-vs-equipped
        // templates as extra drill-in sections — mirrors the game's hover, which shows the item card
        // alongside the equipped items it would replace. Resolved live per press; links mined from the
        // item's own template so inline glossary terms drill too.
        private static void OpenItemTooltip(ItemSlotVM slot)
        {
            var t = slot.Tooltip.Value;
            var own = t != null && t.Count > 0 ? t[t.Count - 1] : null;
            var body = own != null ? TooltipReader.GetFull(own) : null;
            var links = GlossaryLinks.Gather(own);
            List<(string, string)> sections = null;
            if (t != null && t.Count > 1) // count 1 = own card only, no comparison
            {
                int compares = t.Count - 1;
                sections = new List<(string, string)>();
                for (int i = 0; i < compares; i++)
                {
                    var cb = TooltipReader.GetFull(t[i]);
                    if (string.IsNullOrWhiteSpace(cb)) continue;
                    sections.Add((compares > 1 ? Loc.T("inv.compare_n", new { index = i + 1 })
                        : Loc.T("inv.compare"), cb));
                }
                if (sections.Count == 0) sections = null;
            }
            TooltipChooser.Open(ItemLabel(slot, withFavorite: true), body, sections, links);
        }

        // Action verbs, routed exactly like InventorySlotView (EventBus → InventoryVM) — the game's own
        // handlers, never a reimplementation.
        private static void Equip(ItemSlotVM slot) => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryEquip(slot));

        // Unequip via the game's helper (combat-guarded, routes GameCommandQueue.UnequipItem); on success
        // raise the same Refresh the view raises so the doll + stash rebuild.
        private static void Unequip(EquipSlotVM slot)
        {
            if (InventoryHelper.TryUnequip(slot))
                EventBus.RaiseEvent<IInventoryHandler>(h => h.Refresh());
        }

        // The shared item-row shell: label + "item" role, silent hover (dense grid), the item's own
        // tooltip on Space, generic click on activation (the game's
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
