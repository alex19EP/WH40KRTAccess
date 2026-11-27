---
name: rt-inventory-window-parity
description: Inventory-window a11y — stash is a flat filtered card list (not a table); verified game drive-paths for filter/sort/search/weapon-set/defences/compare/equip-selector; doll-roster slot audit
metadata: 
  node_type: memory
  type: project
  originSessionId: 5078614d-b96b-4ab7-872e-61d261c7d54c
---

Inventory service window (`InventoryVM`) accessible screen is `Screens/InventoryScreen.cs`. An audit found the
stash was modelled as a fabricated 5-column **table** when the game stash is a filtered/sorted **flat list of
icon cards** (2-D grid position is sort-order only, not semantic — RT has no item footprints), and that the real
chrome (filter tabs / sort / search), weapon-set switch, derived stats, compare tooltip, and empty-slot equip
selector were unexposed. Full roadmap: `docs/plans/gridless-stocking-babbage.md`.

**Built:**
- Stash `Table` → single-item `List` of `ProxyInventoryItem` (Type/Weight/Value on Space tooltip, per
  [[rt-label-mirror-visual]]); a Filters/Sort `BarRegion` via `UI/Proxies/ProxyChoiceCycler.cs` (combo box,
  Enter→ChoiceSubmenu, Activate-only so it doesn't fight FlowSheet inter-cell Left/Right); filter+sorter added
  to `ContentSig`.
- Weapon-set switch combo box in Equipment (`doll.WeaponSets`, `WeaponSetVM.SetSelected(true)` → OnSetChange →
  CurrentSet → SwitchHandEquipment; both usable sets listed set-numbered, single-set units unlabelled+no-switch);
  `Defences` region reads `InventoryVM.LevelClassScoresVM.AdditionalStatsVM` (Deflection/Absorption/Dodge/
  DodgeReduction/Resolve[skip "—"=pet]/Parry, live strings + breakdown tooltips).
- Compare-vs-equipped folded into the Space drill via a general `UIElement.GetTooltipSections()` hook
  (TraditionalNavigator.OpenTooltipOrLinks merges sections+glossary links into DrillMenuScreen; single-tooltip
  path unchanged); ProxyInventoryItem returns the LEADING `Tooltip.Value` templates (equipped-vs-this, rendered
  via TooltipReader.GetFull). Auto-add-to-Cargo context-menu entry (`ItemSlotVM.AddToCargoAutomatically`, gated
  CargoHelper.CanTransferFrom/ToCargo); OpenMenu + ProxyEquipSlot "Take off" localized to game strings
  `UIStrings.Instance.ContextMenu.*`/`LootWindow.*`.
- Empty-slot equip selector: `Screens/EquipSelectorScreen.cs` (layer 12, Exclusive, above InventoryScreen 10)
  mirrors the DOLL's `InventorySelectorWindowVM` + `UI/Proxies/ProxyEquipCandidate.cs`. `ProxyEquipSlot` gained
  an `InventoryDollVM` ctor param: Enter on empty slot / "Choose item" (Context) on filled → `doll.HandleChangeItem(slot)`,
  gated on `doll.CanChangeEquipment.Value`; AddSlot threads doll. Verbs = game strings (ContextMenu.Equip/TakeOff,
  InventoryScreen.ChooseItem); key `inv.equipped`.

**Verified game drive paths (all the game's own VM/command):** filter =
`vm.StashVM.ItemsFilter.SetCurrentFilter(ItemsFilterType)` (set: NoFilter/Weapon/Armor/Accessories/Usable/Notable/
NonUsable/AugmentationsAll/ShipNoFilter, from ItemsFilterPCView.m_SortedFiltersList); sort =
`Game.Instance.GameCommandQueue.SetInventorySorter(ItemsSorterType)` (all except CargoValue); search =
`ItemsFilter.ItemsFilterSearchVM.SetSearchString`; option names via `LocalizedTexts.Instance.ItemsFilter.GetText(type)`.
Weapon-set switch = `GameCommandQueue.SwitchHandEquipment(unit, set.Index)`. Empty-slot equip (CORRECTED — the audit's
`InventoryVM.ShowEquipSelector`/`InventoryEquipSelectorVM` is a DEAD END, all fields private): the live selector is on
the DOLL — `InventoryDollVM.HandleChangeItem(slot)` sets `doll.InventorySelectorWindowVM.Value`
(an `InventorySelectorWindowVM : SelectorWindowVM<EquipSelectorSlotVM>`); public `EntitiesCollection` /
`Confirm(candidate)`→EquipItem / `Unequip()` / `Back()` / `Slot`; worn item inserted at index 0 to take off. The game
inventory context menu has NO Use/Repair/Unlock — don't add a fake "Use".

**Deferred:** 1b search (needs accessible text-entry source — verify game's search field is live beneath overlay),
1c opt-in inline-details toggle, 5 cargo browse + viewed-character switch, badge-word localization pass (still
hardcoded English in ProxyInventoryItem.Name()/ProxyEquipSlot.ItemLabel(): notable/unusable/favorite/empty/can't
remove/rarity/charges). Keys added along the way: inv.weapon_set, inv.defenses, slot.set_primary/secondary, stat.*,
inv.no_actions, inv.compare, inv.compare_n.

**Doll-roster audit (2026-07-11, decompile-confirmed) + FIX PASS SHIPPED same day (compile 0/0, NOT ear-tested):**
`InventoryDollView` binds ONLY Armor/Head/Gloves/Boots(Feet)/Back(Shoulders)/Neck/Ring1/Ring2/**Protocol**/quick×4/
weapon-sets; Belt/Wrist/Glasses/Shirt are Pathfinder leftovers (no RT view binds them, no RT item types in
`TypeSorter.EquipSortingOrder`). Fixes shipped in InventoryScreen/ItemNodes/VisualSettingsScreen (user chose the
full-mirror + all-extras scope):
- doll roster: dropped the 4 PF slots, added `doll.Protocol` (slot.protocol), renamed Shoulders→"Back" (slot.back),
  reordered to the view's binding order; two-hander/shield ghost now spoken (ItemNodes.EquipSlotLabel reads
  publicized `m_FakeItem` mirroring EquipSlotVM.GetIcon; keys slot.held_two_handed / slot.other_set).
- **filter-labels BUG (user repro: filter options spoke blank)**: `LocalizedTexts.ItemsFilter.GetText` FILTER-type
  entries are EMPTY in the RT asset (only SORTER entries filled) — filter names must come from
  `UIStrings.InventoryScreen.FilterText*` + `UIAugmentations.FilterAll` (the SetHints mapping). Sorter dropdown
  keeps GetText. Also mirrored the DLC3 gate on AugmentationsAll (StoreManager.CheckIfDlcPurchasedAndInstalled).
- dropped the "Gold" summary line (PF leftover — RT never shows Player.Money); inv.encumbrance reworded "Party
  encumbrance"; added per-char carry weight (doll.EncumbranceVM.EncumbranceVm + Tooltip) to the equip stop.
- left-panel FULL MIRROR: character stop gained XP/psy-rating/careers/Level-Up (live CharInfoExperienceVM,
  exp.LevelUp()); characteristics + skills stops reuse CharacterInfoScreen.BuildStatSection (made internal);
  new weapons stop = per equipped weapon a group of its WeaponAbilities (reload+HiddenInUI filtered,
  TooltipTemplateAbility(ability, weapon, unit)).
- stash chrome: force-sort button (inv.sort_now → SortItems()); ShowUnavailable toggle gated on the LIVE
  ItemsFilterPCView's serialized m_ShowToggle (cached per window; no live view → re-check, don't cache).
- new VisualSettingsScreen (layer 13 Exclusive, registered after EquipSelectorScreen): doll cosmetics —
  6 entity toggles (Cloth is chargen-only/null; pets get none) + outfit-color cycler over
  OutfitMainColorSelector.SelectionGroup ("color N", key visual.color_option); opener button in equip stop
  (UIStrings.CharGen.ShowVisualSettings → doll.ShowVisualSettings()).
**CargoScreen SHIPPED 2026-07-12 (compile 0/0, NOT ear-tested)** — the CargoManagement service window
(Ctrl+U; `ServiceWindowsVM.CargoManagementVM`, resolver `UiContexts.CargoManagement()`): 3 stops = ship name /
party stash / cargo bays. Stash side reuses InventoryScreen.BuildSearch+BuildStashControls (made internal; same
InventoryStashVM class) with `ItemNodes.VendorStashItem` rows — **CargoManagementVM's `IInventoryHandler` stubs:
`TryMoveToInventory` IS live (→ TryMoveSlotInInventory to FirstEmptySlot) but `TryMoveToCargo`/`TryEquip`/`TryDrop`
are EMPTY** (vendor-window trap) → stash→cargo goes through InventoryHelper.TryMoveToCargo directly. Cargo side:
sorter cycler (`InventoryCargoVM.SetCurrentSorter`, non-vendor set = SortOptions made internal) + one collapsible
group per bay (skip the 8-slot `CargoEntity == null` pads; LIVE label = Title/TypeLabel/fill%/unusable%/new;
Space = TooltipTemplateCargo) with `ItemNodes.CargoItem` children (Enter = EventBus TryMoveToInventory, gate
`CanTransferToInventory` = CargoHelper.CanTransferFromCargo which folds in the LockTransferFromCargo lock); bay
items come from the game's own lazy `CargoSlotVM.CreateItemSlotsGroup()`. Keys cargo.* (ship/bay/bay_unusable/
new/no_bays/sent_to_inventory). Open question for ear-test: two search fields may coexist (stash + cargo side) —
SearchField() picks the first interactable.
**CounterWindowScreen SHIPPED 2026-07-12 (compile 0/0, NOT ear-tested)** — the game's quantity-picker modal
(`CommonVM.CounterWindowVM`, raised by Split / partial Drop / partial Move) was a SILENT inaccessible dialog our
own Split menu verbs opened. Screen = layer 29 Exclusive (under MessageBox 30): item line, count SLIDER
(OnAdjust ±1/Ctrl±10 writing the VM's plain `CurrentValue` field, spoken value mirrors the card's
"{take}/{keep-or-max}" — Split's right number is the REMAINDER), Accept = game's ActionTexts label → `Accept()`,
Close button; NO Back action (game's EscHotkeyManager already closes on Esc — double-Close hazard; the
MessageBoxScreen convention). Key counter.count. **Stack-transfer facts:** `TryMoveToCargo` always moves the
WHOLE stack; partial = split first. Sighted split-drag modifiers (InventoryHelper.GetEndDragAction, stackable →
empty slot): **Shift+drag = Split (counter window), Ctrl+drag = instant half-split**; onto same-item stack =
merge, occupied = swap.
**Split-on-modified-Enter SHIPPED 2026-07-12 (compile 0/0, 50/50 graph tests, NOT ear-tested)** — Shift+Enter =
game's split dialog (InventoryHelper.TrySplitSlot → counter window), Ctrl+Enter = instant half-split (the exact
Ctrl+drag command: `GameCommandQueue.SplitSlot(slot.ToSlotRef(), null, false, count/2)`), on InventoryItem /
VendorStashItem / CargoItem via `ItemNodes.AttachSplitVerbs` (gate IsPosibleSplit; confirmation rides StateText,
key split.half_done). Plumbing: NodeVtable gained **OnActivateShift/OnActivateCtrl** + KeyGraph
ActivateShift/ActivateCtrl (ADDITIVE CHANGE TO THE PINNED Graph CORE — candidate to upstream to WrathAccess) +
GraphNavigator VtableActivateModified (StateText feedback, ui.activate consume shape) + new UI actions
ui.activate_shift/ui.activate_ctrl (Return/KeypadEnter + exact-modifier match; UI-category actions auto-dispatch
to the navigator via InputManager).
See [[rt-loot-accessibility]], [[rt-live-game-shared-harness]].
