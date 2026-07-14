using System;
using System.Collections.Generic;
using Kingmaker;                                              // AugmentationsSlotVM, Game
using Kingmaker.Blueprints.Items.Augments;                    // BlueprintAugmentSlot
using Kingmaker.Blueprints.Root.Strings;                      // UIStrings (the game's own labels/warnings)
using Kingmaker.Code.UI.MVVM.VM.ContextMenu;                  // ContextMenuCollectionEntity
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Augmentations; // AugmentationsVM
using Kingmaker.Code.UI.MVVM.VM.Slots;                        // ItemSlotVM, IInventoryHandler
using Kingmaker.Code.UI.MVVM.VM.WarningNotification;          // WarningNotificationFormat
using Kingmaker.Items;                                        // PartUnitBody
using Kingmaker.Items.Slots;                                  // AugmentSlot
using Kingmaker.PubSubSystem;                                 // IAugment* handlers, IWarningNotificationUIHandler
using Kingmaker.PubSubSystem.Core;                            // EventBus
using Kingmaker.UI.Common;                                    // InventoryHelper, UIUtilityItem/Texts, CanBeControlled
using Kingmaker.UI.Sound;                                     // UISounds (the augment window's own stings)
using RTAccess.UI.Graph;

namespace RTAccess.UI
{
    /// <summary>
    /// Node factories for the Augmentations service window (<see cref="AugmentationsVM"/>) — the body
    /// slots and the augment stash rows. Two contracts matter here beyond the shared item-row rules:
    /// <list type="bullet">
    /// <item><b>Never hold an <see cref="AugmentationsSlotVM"/>.</b> The window's <c>RefreshData</c>
    /// clears and recreates every slot VM (unit switch, equip refresh), so nodes capture the WINDOW VM +
    /// the slot BLUEPRINT and re-resolve the slot VM fresh inside every closure (the inventory-doll
    /// lesson, same bug class).</item>
    /// <item><b>Equipping is two-phase.</b> Dropping an augment into a slot only makes it dirty
    /// (<c>IsDirty</c> — the card's pulsing Install button); the game silently reverts un-installed
    /// augments on close/switch, so the dirty state is spoken in the label and Install is a first-class
    /// verb (<c>ApplyInstallation</c> — the game's command + its own "Augment installed" toast, voiced
    /// by WarningReader).</item>
    /// </list>
    /// Verbs on a slot row: Enter = choose/replace (the sighted card click —
    /// <c>AugmentationsVM.HandleChangeItem</c> → the selector window AugmentSelectorScreen mirrors);
    /// Backspace = the card's verbs as a context menu (Take off / Install / overdrive toggle /
    /// Information — the game's own menu plus the card's two buttons); Ctrl+Enter = Install;
    /// Shift+Enter = the galvanize (overdrive) toggle; Space = the item's own card.
    /// </summary>
    public static class AugmentNodes
    {
        /// <summary>One body slot of the augmentations window, keyed by its blueprint — the slot VM is
        /// re-resolved live inside every closure (see class doc). The label mirrors the card: slot name +
        /// item/empty + the card-visible state badges. The label is LIVE: install/galvanize settle through
        /// the deferred command queue and re-speak under focus.</summary>
        public static NodeVtable SlotRow(AugmentationsVM vm, BlueprintAugmentSlot slotBp)
        {
            Func<AugmentationsSlotVM> find = () => Find(vm, slotBp);
            Func<string> label = () => SlotLabel(find());
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                },
                SearchText = label,
                OnActivate = () => ChangeItem(vm, find()),
                OnSecondary = () => OpenSlotMenu(find()),
                OnActivateCtrl = () => Install(find()),
                OnActivateShift = () => ToggleOverdrive(find()),
                OnTooltip = () =>
                {
                    var s = find();
                    if (s == null) return;
                    if (s.HasItem) ItemNodes.OpenItemTooltip(s);
                    else TooltipChooser.OpenTemplate(label(), null);
                },
                ActivateSound = UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        /// <summary>"Slot name: item (badges)" / "Slot name: empty" — the card's readout. Every badge is
        /// card-visible to a sighted player (the state-badge layers, the pulsing Install button), so
        /// speaking them is parity, not a leak. The window-wide view-only lock is NOT repeated per row —
        /// the status stop's banner carries it once.</summary>
        public static string SlotLabel(AugmentationsSlotVM s)
        {
            if (s == null) return "";
            string name = UIUtilityItem.GetAugmentSlotName(s.SlotType);
            string content = s.HasItem ? ItemNodes.ItemName(s) : Loc.T("slot.empty");
            var flags = new List<string>();
            if (s.IsDirty.Value) flags.Add(Loc.T("aug.not_installed"));
            if (IsOverdriven(s)) flags.Add(Loc.T("aug.galvanized"));
            if (s.HasTrauma.Value) flags.Add(Loc.T("aug.broken"));
            if (s.IsDefault) flags.Add(Loc.T("aug.default"));
            if (s.HasItem && s.BlueprintAugmentSlot != null && s.BlueprintAugmentSlot.IsMechSlot)
                flags.Add(Loc.T("aug.permanent")); // mech augments can never be removed once installed
            string text = name + ": " + content;
            return flags.Count > 0 ? text + " (" + string.Join(", ", flags) + ")" : text;
        }

        /// <summary>Whether this slot is the unit's galvanized (overdriven) one — read from the unit's
        /// own <see cref="UnitAugments.OverdriveSlot"/>, the same source the card's badge reads.</summary>
        internal static bool IsOverdriven(AugmentationsSlotVM s)
            => s?.BlueprintAugmentSlot != null
                && s.Unit?.GetOptional<PartUnitBody>()?.Augments?.OverdriveSlot == s.BlueprintAugmentSlot;

        private static bool HasOverdriveAbility(AugmentationsSlotVM s)
            => (s?.ItemSlot as AugmentSlot)?.ItemBlueprint?.OverdriveAbility != null;

        // The view's own take-off gate (AugmentationsEquipSlotBaseView.CanTakeOffAugment), mirrored for
        // the context menu's greyed state.
        private static bool CanTakeOff(AugmentationsSlotVM s)
        {
            if (s == null || !s.HasItem || s.IsDefault) return false;
            if (s.BlueprintAugmentSlot != null && s.BlueprintAugmentSlot.IsMechSlot) return false;
            if (s.IsLocked.Value || s.HasTrauma.Value) return false;
            return s.ItemSlot != null && s.ItemSlot.CanRemoveItem();
        }

        // Enter — the sighted card click: opens the game's augment selector (HandleChangeItem builds the
        // suitable-items list and sets InventorySelectorWindowVM; an empty list raises the game's own
        // "nothing to insert" warning, voiced by WarningReader). A view-only slot mirrors the sighted
        // no-op click, but SPEAKS the block through the game's own view-only warning path
        // (IsBlockedByViewOnly — publicized private: the exact sound + warning the VM's equip paths raise).
        private static void ChangeItem(AugmentationsVM vm, AugmentationsSlotVM s)
        {
            if (vm == null || s == null) return;
            if (s.IsLocked.Value) { vm.IsBlockedByViewOnly(); return; }
            vm.HandleChangeItem(s);
        }

        // Backspace — the card's verbs as one menu: the game's own two entries (Take off / Information,
        // mirroring AugmentationsEquipSlotBaseView.SetupContextMenu) plus the card's two BUTTONS surfaced
        // as entries (Install while dirty; the galvanize toggle while the augment carries an overdrive
        // ability). Conditions/interactable mirror the card's visible/greyed states.
        private static void OpenSlotMenu(AugmentationsSlotVM s)
        {
            if (s == null) return;
            var cm = UIStrings.Instance.ContextMenu;
            var aug = UIStrings.Instance.UIAugmentations;
            bool inCombat = Game.Instance.TurnController.InCombat;
            var entities = new List<ContextMenuCollectionEntity>
            {
                new ContextMenuCollectionEntity(cm.TakeOff, () => TakeOff(s),
                    s.HasItem, CanTakeOff(s) && s.IsEquipPossible),
                new ContextMenuCollectionEntity(aug.InstallButtonText, () => Install(s),
                    s.IsDirty.Value, s.ItemSlot?.Owner != null && s.ItemSlot.Owner.CanBeControlled()),
                new ContextMenuCollectionEntity(Loc.T(IsOverdriven(s) ? "aug.degalvanize" : "aug.galvanize"),
                    () => ToggleOverdrive(s), HasOverdriveAbility(s), !inCombat && !s.IsDirty.Value),
                new ContextMenuCollectionEntity(cm.Information,
                    () => { if (s.HasItem) ItemNodes.OpenItemTooltip(s); }, s.HasItem),
            };
            ContextMenuNodes.Open(SlotLabel(s), entities,
                onEmpty: () => Tts.Speak(Loc.T("inv.no_actions"), interrupt: true));
        }

        // Take off — the view's TryUnequip guard ladder, with each sound-only/silent refusal given a
        // spoken reason (the blocked state is card-visible; the sighted refusals are sound-only):
        // default → mod line; mech → the game's own warning; locked/trauma → the game's sting + mod line;
        // else the game's unequip helper + the same IAugmentUnequipHandler raise the view fires (keeps
        // the overdrive block rebound).
        private static void TakeOff(AugmentationsSlotVM s)
        {
            if (s?.ItemSlot == null || !s.HasItem) return;
            var bp = s.BlueprintAugmentSlot;
            if (bp?.DefaultAugment != null && s.ItemSlot.HasItem
                && s.ItemSlot.Item.Blueprint == bp.DefaultAugment.Get())
            {
                Tts.Speak(Loc.T("aug.cant_remove_default"), interrupt: true);
                return;
            }
            if (bp != null && bp.IsMechSlot)
            {
                UiSound.Play(UISounds.Instance?.Sounds?.AugmentationsWindow?.AugmentMechRestriction);
                EventBus.RaiseEvent(delegate(IWarningNotificationUIHandler h)
                {
                    h.HandleWarning(UIStrings.Instance.UIAugmentations.CannotRemoveMechAugment.Text,
                        addToLog: false, WarningNotificationFormat.Short);
                });
                return;
            }
            if (s.IsLocked.Value)
            {
                UiSound.Play(UISounds.Instance?.Sounds?.AugmentationsWindow?.AugmentViewOnlyInstall);
                Tts.Speak(Loc.T("aug.view_only_blocked"), interrupt: true);
                return;
            }
            if (s.HasTrauma.Value)
            {
                UiSound.Play(UISounds.Instance?.Sounds?.AugmentationsWindow?.AugmentBrokenInstall);
                Tts.Speak(Loc.T("aug.broken_blocked"), interrupt: true);
                return;
            }
            bool wasOverdriven = IsOverdriven(s);
            if (InventoryHelper.TryUnequip(s))
            {
                if (wasOverdriven)
                    UiSound.Play(UISounds.Instance?.Sounds?.AugmentationsWindow?.AugmentOverdriveRemove);
                EventBus.RaiseEvent(delegate(IAugmentUnequipHandler h) { h.HandleAugmentUnequip(); });
            }
        }

        // Install — commit the dirty slot (the card's pulsing button): the slot VM's own
        // ApplyInstallation (the ApplyAugmentInsertion command + the game's "Augment installed" toast,
        // voiced by WarningReader) behind the button's own CanBeControlled gate.
        private static void Install(AugmentationsSlotVM s)
        {
            if (s?.ItemSlot == null) return;
            if (!s.IsDirty.Value) { Tts.Speak(Loc.T("aug.nothing_to_install"), interrupt: true); return; }
            var owner = s.ItemSlot.Owner;
            if (owner == null || !owner.CanBeControlled()) return;
            s.ApplyInstallation();
            UiSound.Play(UISounds.Instance?.Sounds?.AugmentationsWindow?.AugmentInstallButtonClick);
        }

        // The galvanize (overdrive) toggle — the card's overcharge button. Gates mirror
        // SetOverchargeButtonState/OnOverrideButtonClick, each refusal spoken; the action raises the
        // EXACT event the PC button raises (layer 1 = enable, 2 = disable): the slot VM runs the game's
        // SetAugmentOverdrive command, the window VM plays the overdrive sting. The result settles
        // through the deferred queue — the row's live label speaks the flip.
        private static void ToggleOverdrive(AugmentationsSlotVM s)
        {
            if (s == null) return;
            if (!HasOverdriveAbility(s))
            {
                Tts.Speak(Loc.T("aug.no_overdrive_ability"), interrupt: true);
                return;
            }
            if (Game.Instance.TurnController.InCombat)
            {
                Tts.Speak(GameText.Or(() => UIStrings.Instance.UIAugmentations.CannotUseInCombat,
                    "aug.cant_in_combat"), interrupt: true);
                return;
            }
            if (s.IsDirty.Value) { Tts.Speak(Loc.T("aug.install_first"), interrupt: true); return; }
            if (s.Unit == null || !s.Unit.CanBeControlled()) return;
            int layer = IsOverdriven(s) ? 2 : 1;
            var bp = s.BlueprintAugmentSlot;
            EventBus.RaiseEvent(delegate(IAugmentOverdriveToggleHandler h)
            {
                h.HandleAugmentOverdriveToggle(bp, layer);
            });
        }

        /// <summary>One augment in the window's stash (the party inventory filtered to augments). The
        /// label mirrors the card: name + badges, plus — for an augment the card greys out — the card's
        /// own cannot-equip header (default / tier restriction / no-throne-yet, the game's strings).
        /// Enter equips through the window VM's own <c>IInventoryHandler.TryEquip</c> (the sighted
        /// double-click: view-only warns via the game); a greyed row speaks its reason instead of firing.
        /// Backspace = the live verbs (Equip / Information); Space = the item's own card.</summary>
        public static NodeVtable StashRow(ItemSlotVM slot)
        {
            Func<string> label = () => ItemNodes.ItemLabel(slot, withFavorite: true);
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => StashLockLabel(slot), live: true),
                },
                SearchText = label,
                OnActivate = () => TryEquipFromStash(slot),
                OnSecondary = () => OpenStashRowMenu(slot),
                OnTooltip = () => ItemNodes.OpenItemTooltip(slot),
                HoverSound = UISounds.ButtonSoundsEnum.NoSound, // dense grid — the shared item-row convention
                ActivateSound = UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        // The greyed-card reason: null while the augment is installable on the viewed unit. Falls back
        // to a mod line when the game's header carries no text for the cannot-equip state.
        private static string StashLockLabel(ItemSlotVM slot)
        {
            var item = slot?.Item?.Value;
            if (item == null || UIUtilityItem.IsAugmentSuitable(item)) return null;
            var header = UIUtilityTexts.GetItemHeaderText(item);
            return string.IsNullOrEmpty(header.Item1) ? Loc.T("aug.cant_equip") : header.Item1;
        }

        private static void TryEquipFromStash(ItemSlotVM slot)
        {
            if (slot?.Item?.Value == null) return;
            var reason = StashLockLabel(slot);
            if (reason != null) { Tts.Speak(reason, interrupt: true); return; }
            // The window VM's own handler (AugmentationsVM.IInventoryHandler.TryEquip): the view-only
            // gate + InventoryHelper.TryEquip pick the slot exactly like the sighted double-click.
            EventBus.RaiseEvent(delegate(IInventoryHandler h) { h.TryEquip(slot); });
        }

        private static void OpenStashRowMenu(ItemSlotVM slot)
        {
            var cm = UIStrings.Instance.ContextMenu;
            var suitable = slot?.Item?.Value != null && UIUtilityItem.IsAugmentSuitable(slot.Item.Value);
            var entities = new List<ContextMenuCollectionEntity>
            {
                new ContextMenuCollectionEntity(cm.Equip, () => TryEquipFromStash(slot),
                    slot != null && slot.HasItem, suitable),
                new ContextMenuCollectionEntity(cm.Information,
                    () => ItemNodes.OpenItemTooltip(slot), slot != null && slot.HasItem),
            };
            ContextMenuNodes.Open(ItemNodes.ItemLabel(slot, withFavorite: true), entities,
                onEmpty: () => Tts.Speak(Loc.T("inv.no_actions"), interrupt: true));
        }

        // Resolve the slot VM FRESH by blueprint — never cached (see class doc).
        private static AugmentationsSlotVM Find(AugmentationsVM vm, BlueprintAugmentSlot bp)
        {
            var list = vm?.AllAugmentSlots;
            if (list == null || bp == null) return null;
            foreach (var s in list)
                if (s != null && !s.IsOverchargeSlot && s.BlueprintAugmentSlot == bp) return s;
            return null;
        }
    }
}
