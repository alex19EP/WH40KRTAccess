using System;
using System.Collections.Generic;
using Kingmaker;                                              // Game, AugmentationsEnum, AugmentationsFiltersPCView
using Kingmaker.Blueprints.Items.Augments;                    // BlueprintAugmentSlot, BlueprintItemAugment
using Kingmaker.Blueprints.Root;                              // BlueprintRoot (metallization buff), LocalizedTexts
using Kingmaker.Blueprints.Root.Strings;                      // UIStrings (the game's own window labels)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Augmentations; // AugmentationsVM + AugmentationsInventoryStashVM
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;            // TooltipTemplateAbility (the overdrive card)
using Kingmaker.GameCommands;                                 // SetInventorySorter
using Kingmaker.Items;                                        // PartUnitBody
using Kingmaker.UI.Common;                                    // ItemsFilterType/ItemsSorterType, UIUtilityItem
using Kingmaker.UnitLogic.Buffs;                              // Buff, BuffDuration (metallization tooltip)
using RTAccess.Accessibility;                                 // ViewedCharacter (switch announce + header)
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The Augmentations service window (<see cref="AugmentationsVM"/> — the DLC3 cybernetics screen) as
    /// a graph-native screen. The sighted window is a rotatable 3D doll with slot cards wired around it —
    /// pure decoration; the accessible model is flat, one labelled Tab-stop per pane:
    /// <list type="bullet">
    /// <item><b>Status</b> — the view-only banner (browsing is allowed anywhere; editing only where the
    /// area releases it, e.g. the Medicae bay), the character readout + prev/next switch (the window
    /// shares SelectedUnitInUI — Shift+A/D work via PartyHotkeys), the Metallization counter (the
    /// stacking buff's rank, read live off the unit — the same source the header label shows), and the
    /// overdrive status line (which slot is galvanized + the granted ability; Space = the ability's own
    /// card) mirroring the overcharge pseudo-slot, which therefore gets no slot row.</item>
    /// <item><b>Slots</b> — one row per real body slot in the sighted view's binding order (common six,
    /// Forge World, the Pasqal/Manipulus mech slots). Rows are keyed by unit + slot BLUEPRINT and the
    /// slot VM is re-resolved fresh per closure — the window's RefreshData recreates every slot VM
    /// (see <see cref="AugmentNodes"/>). Verbs per row: Enter = choose/replace (the game's selector,
    /// surfaced by <see cref="AugmentSelectorScreen"/>), Backspace = Take off / Install / galvanize /
    /// Information menu, Ctrl+Enter = Install, Shift+Enter = galvanize toggle, Space = the item card.</item>
    /// <item><b>Stash</b> — the party inventory filtered to augments, with the game's own chrome as
    /// Ctrl+arrow regions (the InventoryScreen stash convention): the game's search field, the augment
    /// filter set + sorter, then the item list (Enter = equip via the window's own TryEquip).</item>
    /// </list>
    /// Escape closes the whole service-window stack; when any slot is still dirty it first speaks a
    /// warning — the game deletes un-installed augments back to the inventory SILENTLY on close
    /// (RemoveNotInstalled), which a blind player must not discover by surprise. Layer 10; ScreenName
    /// stays null — ServiceWindowAnnounce already speaks "Augmentations" on open.
    /// </summary>
    public sealed class AugmentationsScreen : Screen
    {
        public override string Key => "service.augmentations";
        public override string ScreenName => null; // ServiceWindowAnnounce speaks the window name
        public override int Layer => 10;

        public AugmentationsScreen() { Wrap = true; }

        // Bare letters stay with the game; the stash search is the game's OWN field (BuildSearch), and
        // Shift+A/D are the mod's party chords (PartyHotkeys → ViewedCharacter).
        public override bool AllowsTypeahead => false;

        public override bool IsActive() => Vm() != null;

        // Announce each character switch (SelectedUnitInUI changes silently otherwise); re-baseline on open.
        public override void OnPush() => ViewedCharacter.Reset();
        public override void OnUpdate() => ViewedCharacter.Tick(Vm()?.Unit?.Value);

        // Back (Escape) closes the whole service-window stack. With a dirty (equipped-but-not-installed)
        // slot the game silently reverts the augment to the inventory — speak that first (we cannot block
        // the revert; the sighted cue is the pulsing Install button, ours is this line + the label flag).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ =>
            {
                var vm = Vm();
                if (vm != null && HasDirtySlot(vm))
                    Tts.Speak(Loc.T("aug.dirty_close"), interrupt: true);
                UiContexts.ServiceWindows()?.HandleCloseAll();
            });
        }

        // The window opens in the surface AND the star-system context (it even opens view-only in space).
        private static AugmentationsVM Vm() => UiContexts.Augmentations();

        private static bool HasDirtySlot(AugmentationsVM vm)
        {
            var slots = vm?.AllAugmentSlots;
            if (slots == null) return false;
            foreach (var s in slots)
                if (s != null && !s.IsOverchargeSlot && s.IsDirty.Value) return true;
            return false;
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "aug:" + vm.GetHashCode() + ":";           // a new window = fresh keys
            var unit = vm.Unit?.Value;
            string uk = k + "u:" + (unit?.UniqueId ?? "") + ":";  // per-character keys re-home on a switch

            BuildStatus(b, k, uk, vm);
            BuildSlots(b, uk, vm);

            // The stash pane is ONE stop; its chrome and list are Ctrl+arrow regions (the InventoryScreen
            // stash convention — the pane-wide context labels the stop, deduped against the list context).
            b.BeginStop("stash");
            b.PushContext(Loc.T("inv.stash"));
            InventoryScreen.BuildSearch(b, k, vm.StashVM?.ItemSlotsGroup);
            BuildStashControls(b, k, vm.StashVM);
            BuildStash(b, k, vm.StashVM);
            b.PopContext();
        }

        // ---- the status stop: view-only banner, who's shown, metallization, overdrive ----

        private static void BuildStatus(GraphBuilder b, string k, string uk, AugmentationsVM vm)
        {
            b.BeginStop("status");
            b.PushContext(Loc.T("aug.status"), Loc.T("role.list"));
            // The view-only banner — first, so opening the window leads with the one fact that changes
            // what every verb does here.
            if (AugmentationsVM.AugmentsViewOnly)
                b.AddItem(ControlId.Structural(k + "status:viewonly"), GraphNodes.Text(
                    () => GameText.Or(() => UIStrings.Instance.UIAugmentations.ViewOnlyLabel, "aug.view_only")));
            var unit = vm.Unit?.Value;
            if (unit != null)
                b.AddItem(ControlId.Structural(uk + "status:readout"),
                    GraphNodes.Text(() => ViewedCharacter.HeaderLine(vm.Unit?.Value)));
            // Window-keyed switch buttons: focus stays on the button across the switch while
            // ViewedCharacter.Tick announces who's shown (the InventoryScreen recipe; pets have no
            // augments and live off ActualGroup, so there is no pet-swap axis here).
            b.AddItem(ControlId.Structural(k + "status:prev"), GraphNodes.Button(
                () => Loc.T("char.prev_member"), () => ViewedCharacter.SwitchMember(next: false)));
            b.AddItem(ControlId.Structural(k + "status:next"), GraphNodes.Button(
                () => Loc.T("char.next_member"), () => ViewedCharacter.SwitchMember(next: true)));
            // Metallization — the header counter (the game's label + the buff's live rank; the VM's own
            // reactive lags 4 frames behind, so read the buff directly — the same source). LIVE, so an
            // install/take-off settling under focus speaks the new rank; Space = the buff's own card
            // (the trade-off mechanic explained — the same tooltip the sighted coins container shows).
            var metal = new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(() => MetallizationLine(vm), live: true, kind: AnnouncementKinds.Label),
                },
                SearchText = () => MetallizationLine(vm),
                OnTooltip = () => OpenMetallizationTooltip(vm),
            };
            b.AddItem(ControlId.Structural(uk + "status:metal"), metal);
            // The overdrive status line mirrors the overcharge pseudo-slot + the header label. LIVE, so a
            // galvanize settling under focus speaks the flip; Space = the granted ability's own card.
            var overdrive = new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(() => OverdriveLine(vm), live: true, kind: AnnouncementKinds.Label),
                },
                SearchText = () => OverdriveLine(vm),
                OnTooltip = () => OpenOverdriveTooltip(vm),
            };
            b.AddItem(ControlId.Structural(uk + "status:overdrive"), overdrive);
            b.PopContext();
        }

        private static string MetallizationLine(AugmentationsVM vm)
        {
            var unit = vm?.Unit?.Value;
            var bp = BlueprintRoot.Instance?.SystemMechanics?.MetallicizationBuff;
            var buff = unit != null && bp != null ? unit.Buffs.GetBuff(bp) : null;
            string rank = buff != null ? buff.Rank.ToString() : "0";
            return GameText.Or(() => UIStrings.Instance.UIAugmentations.MetallizationLabel, "aug.metallization")
                + ": " + rank;
        }

        // Space on the metallization line: the buff's own card, built the way the sighted counter's
        // hover builds it (AugmentationsInventoryStashView.UpdateTooltipHandler) — including the game's
        // own trick for a unit with no stacks yet: a DETACHED rank-0 fact created just for the template
        // (never added to the unit), so the mechanic is readable before the first augment.
        private static void OpenMetallizationTooltip(AugmentationsVM vm)
        {
            var unit = vm?.Unit?.Value;
            var bp = BlueprintRoot.Instance?.SystemMechanics?.MetallicizationBuff;
            if (unit == null || bp == null)
            {
                TooltipChooser.OpenTemplate(MetallizationLine(vm), null);
                return;
            }
            var buff = unit.Buffs.GetBuff(bp);
            if (buff == null)
            {
                buff = bp.CreateFact(null, unit, default(BuffDuration)) as Buff;
                buff?.SetRankToZero();
            }
            TooltipChooser.OpenTemplate(MetallizationLine(vm),
                buff != null ? new TooltipTemplateBuff(buff) : null);
        }

        // "No overcharged" / "Overcharged: {slot}, {ability}" — the base view's OnOverdriveLabelsUpdate
        // readout with the galvanized slot + granted ability folded in (both card-visible: the badge and
        // the overcharge slot's ability icon).
        private static string OverdriveLine(AugmentationsVM vm)
        {
            var augments = vm?.Unit?.Value?.GetOptional<PartUnitBody>()?.Augments;
            var ability = augments?.OverdriveAbility;
            if (ability == null)
                return GameText.Or(() => UIStrings.Instance.UIAugmentations.NoOverchargedLabel, "aug.no_overdrive");
            string slotName = UIUtilityItem.GetAugmentSlotName(
                UIUtilityItem.GetAugmentationSlotType(augments.OverdriveSlot));
            return GameText.Or(() => UIStrings.Instance.UIAugmentations.OverchargedLabel, "aug.overdrive")
                + ": " + slotName + ", " + ability.Name;
        }

        // Space on the overdrive line: the granted ability's own card, built the way the game's
        // overcharge tooltip builds it (TooltipTemplateAbility against the source augment item).
        private static void OpenOverdriveTooltip(AugmentationsVM vm)
        {
            var augments = vm?.Unit?.Value?.GetOptional<PartUnitBody>()?.Augments;
            var ability = augments?.OverdriveAbility;
            if (ability == null) { TooltipChooser.OpenTemplate(OverdriveLine(vm), null); return; }
            BlueprintItemAugment itemBp = null;
            if (augments.OverdriveSlot != null
                && augments.Slots.TryGetValue(augments.OverdriveSlot, out var slot))
                itemBp = slot.ItemBlueprint;
            TooltipChooser.OpenTemplate(OverdriveLine(vm),
                new TooltipTemplateAbility(ability.Blueprint, itemBp));
        }

        // ---- the slots stop ----

        // The sighted view's binding order (AugmentationsPCView.UpdateSlots) — the arrangement around the
        // doll is spatial decoration, the binding order is the stable mirror. Unknown/special slots sort
        // after (Array.IndexOf -1 → tail), keeping their AllAugmentSlots order.
        private static readonly AugmentationsEnum[] SlotOrder =
        {
            AugmentationsEnum.NervousSystem, AugmentationsEnum.Forgeworld, AugmentationsEnum.PreceptionSystem,
            AugmentationsEnum.RightHand, AugmentationsEnum.LeftHand, AugmentationsEnum.InternalSystems,
            AugmentationsEnum.Legs, AugmentationsEnum.Mech1, AugmentationsEnum.Mech2, AugmentationsEnum.Mech3,
        };

        private static void BuildSlots(GraphBuilder b, string uk, AugmentationsVM vm)
        {
            b.BeginStop("slots");
            b.PushContext(Loc.T("aug.slots"), Loc.T("role.list"));
            var rows = new List<(int rank, int index, BlueprintAugmentSlot bp)>();
            var all = vm.AllAugmentSlots;
            if (all != null)
                for (int i = 0; i < all.Count; i++)
                {
                    var s = all[i];
                    // The overcharge pseudo-slot is the status stop's overdrive line, not a body slot.
                    if (s == null || s.IsOverchargeSlot || s.BlueprintAugmentSlot == null) continue;
                    int rank = Array.IndexOf(SlotOrder, s.SlotType);
                    rows.Add((rank < 0 ? SlotOrder.Length : rank, i, s.BlueprintAugmentSlot));
                }
            rows.Sort((a, o) => a.rank != o.rank ? a.rank.CompareTo(o.rank) : a.index.CompareTo(o.index));
            foreach (var r in rows)
                b.AddItem(ControlId.Structural(uk + "slot:" + r.bp.AssetGuid),
                    AugmentNodes.SlotRow(vm, r.bp));
            b.PopContext();
        }

        // ---- the stash stop's chrome + list (the augment window's own filter bar) ----

        // The augment filter bar's toggle set (AugmentationsFiltersPCView.m_SortedFiltersList), labelled
        // with the same strings the game's toggle hints carry (SetHints — the Misc toggle reads the
        // Forgeworld string there).
        private static readonly List<ItemsFilterType> FilterOptions = new List<ItemsFilterType>
        {
            ItemsFilterType.AugmentationsAll, ItemsFilterType.AugmentationsArms,
            ItemsFilterType.AugmentationsEyes, ItemsFilterType.AugmentationsLegs,
            ItemsFilterType.AugmentationsSystems, ItemsFilterType.AugmentationsTorso,
            ItemsFilterType.AugmentationsMisc,
        };

        private static string FilterName(ItemsFilterType t)
        {
            var aug = UIStrings.Instance.UIAugmentations;
            string s = t switch
            {
                ItemsFilterType.AugmentationsAll => aug.FilterAll.Text,
                ItemsFilterType.AugmentationsArms => aug.FilterArms.Text,
                ItemsFilterType.AugmentationsEyes => aug.FilterEyes.Text,
                ItemsFilterType.AugmentationsLegs => aug.FilterLegs.Text,
                ItemsFilterType.AugmentationsSystems => aug.FilterSystems.Text,
                ItemsFilterType.AugmentationsTorso => aug.FilterTorso.Text,
                ItemsFilterType.AugmentationsMisc => aug.FilterForgeworld.Text,
                _ => null,
            };
            return string.IsNullOrEmpty(s) ? LocalizedTexts.Instance.ItemsFilter.GetText(t) : s;
        }

        private static void BuildStashControls(GraphBuilder b, string k, AugmentationsInventoryStashVM stash)
        {
            var filter = stash?.ItemsFilter;
            if (filter == null) return;
            b.SetRegion(k + "filters");
            b.PushContext(Loc.T("inv.filters"), Loc.T("role.list"));
            b.StartRow(k + "filtersrow");

            var filters = FilterOptions;
            b.AddItem(ControlId.Structural(k + "filter"), GraphNodes.Cycler(
                () => Loc.T("inv.filters"),
                () => filters.ConvertAll(FilterName),
                () => { var cf = filter.CurrentFilter; return cf != null ? Math.Max(0, filters.IndexOf(cf.Value)) : 0; },
                i => { if (i >= 0 && i < filters.Count) filter.SetCurrentFilter(filters[i]); }));

            // The sorter is the shared inventory dropdown: the same options, the same
            // SetInventorySorter command the stash VM's own subscription fires.
            var sorters = InventoryScreen.SortOptions;
            b.AddItem(ControlId.Structural(k + "sort"), GraphNodes.Cycler(
                () => Loc.T("inv.sort"),
                () => sorters.ConvertAll(t => LocalizedTexts.Instance.ItemsFilter.GetText(t)),
                () => Math.Max(0, sorters.IndexOf(stash.CurrentSorter.Value)),
                i => { if (i >= 0 && i < sorters.Count) Game.Instance.GameCommandQueue.SetInventorySorter(sorters[i]); }));

            b.AddItem(ControlId.Structural(k + "sortnow"), GraphNodes.Button(
                () => Loc.T("inv.sort_now"), () => stash.ItemSlotsGroup?.SortItems()));

            if (ShowsUnavailableToggle(stash))
                b.AddItem(ControlId.Structural(k + "unavail"), GraphNodes.Toggle(
                    () => UIStrings.Instance.InventoryScreen.ShowUnavailableItems.Text,
                    () => filter.ShowUnavailable.Value,
                    () => filter.ShowUnavailable.Value = !filter.ShowUnavailable.Value));

            b.EndRow();
            b.PopContext();
        }

        // Whether the augment window's filter bar carries the "show unavailable" toggle — a serialized
        // per-prefab flag on ITS filter view class (AugmentationsFiltersPCView, not the inventory's
        // ItemsFilterPCView), read off the live view once per window and cached (the InventoryScreen
        // pattern; a FindObjects sweep is too heavy per render).
        private static int s_UnavailCheckedFor;
        private static bool s_UnavailShown;
        private static bool ShowsUnavailableToggle(AugmentationsInventoryStashVM stash)
        {
            int key = stash.GetHashCode();
            if (s_UnavailCheckedFor == key) return s_UnavailShown;
            var views = UnityEngine.Object.FindObjectsByType<AugmentationsFiltersPCView>(
                UnityEngine.FindObjectsSortMode.None);
            foreach (var v in views)
            {
                if (v == null || !v.isActiveAndEnabled) continue;
                s_UnavailCheckedFor = key;
                s_UnavailShown = v.m_ShowToggle;
                return s_UnavailShown;
            }
            return false;
        }

        // The augment stash: one row per item (equip on Enter through the window's own handler), keyed by
        // the item ENTITY so an equipped augment's row vanishes and focus slides to a real neighbour. The
        // party carry weight closes the pane (the stash panel shows the encumbrance bar).
        private static void BuildStash(GraphBuilder b, string k, AugmentationsInventoryStashVM stash)
        {
            b.SetRegion(k + "stash");
            b.PushContext(Loc.T("inv.stash"), Loc.T("role.list"));
            bool any = false;
            var vis = stash?.ItemSlotsGroup?.VisibleCollection;
            if (vis != null)
                foreach (var slot in vis)
                {
                    if (slot == null || !slot.HasItem) continue;
                    var ent = slot.Item?.Value;
                    if (ent == null) continue;
                    b.AddItem(ControlId.Referenced(ent, k + "stash:" + ent.UniqueId), AugmentNodes.StashRow(slot));
                    any = true;
                }
            if (!any) b.AddItem(ControlId.Structural(k + "stash:empty"), GraphNodes.Text(() => Loc.T("inv.no_items")));
            var enc = stash?.EncumbranceVM;
            if (enc != null)
                b.AddItem(ControlId.Structural(k + "stash:enc"), GraphNodes.Text(() =>
                {
                    var status = enc.LoadStatus?.Value;
                    var load = (enc.LoadWeight?.Value ?? "") + (string.IsNullOrEmpty(status) ? "" : ", " + status);
                    return Loc.T("inv.encumbrance", new { value = load });
                }));
            b.PopContext();
        }
    }
}
