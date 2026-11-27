# Inventory window — parity plan (gridless-stocking-babbage)

**Status (2026-07-02):** Slices 1 + 2 + 3 + 4 implemented and compile-clean (not yet in-harness verified).
Slice 1 — stash Table→List, Filters/Sort bar (`ProxyChoiceCycler`), filter+sorter in `ContentSig`,
full localization. Slice 2 — weapon-set switch (`SetSelected`→`SwitchHandEquipment`) surfacing both
sets, and a Defences region reading `LevelClassScoresVM.AdditionalStatsVM`. Slice 3 — compare-vs-
equipped folded into the Space drill, and the Auto-add-to-Cargo context-menu entry + game-localized
menu verbs. Slice 4 — empty-slot equip selector (`EquipSelectorScreen` over the doll's
`InventorySelectorWindowVM`, opened via `HandleChangeItem`). Slices 1b–1c, 5 pending.

Follow-up to the inventory parity audit (2026-07-02). The audit found the mod's
`InventoryScreen` mirrors the doll + a load/money summary + the stash, but (a) models the
stash as a fabricated 5-column **table** when the game stash is a filtered/sorted **flat list
of icon cards** (2-D position is sort-order only, not semantic), and (b) omits the real chrome
a sighted player uses to operate a 120-slot stash: the **filter tabs, sort control and search
field**, plus the **weapon-set switch**, **derived defensive stats**, **compare-vs-equipped
tooltip**, and **empty-slot equip selector**.

Design law being restored: *a proxy's browse-label mirrors what the game shows ON THE CARD;
tooltip-only detail stays on Space.* The item card shows only icon + count + rarity + favourite
+ notable — so the stash row's label is name+badges, and Type/Weight/Value belong on Space (the
item tooltip already carries them).

## Verified drive paths (all through the game's own VMs/commands)

- **Filter** — `vm.StashVM.ItemsFilter.SetCurrentFilter(ItemsFilterType)`. Personal-inventory set
  (from `ItemsFilterPCView.m_SortedFiltersList`): `NoFilter, Weapon, Armor, Accessories, Usable,
  Notable, NonUsable, AugmentationsAll, ShipNoFilter`. Persists via the VM's own
  `UISettings.InventoryFilter` subscription. Names: `LocalizedTexts.Instance.ItemsFilter.GetText(type)`.
- **Sort** — `Game.Instance.GameCommandQueue.SetInventorySorter(ItemsSorterType)` (the canonical
  command the dropdown raises; persists to `UISettings.InventorySorter`). Modes: all `ItemsSorterType`
  except `CargoValue` → `NotSorted, TypeUp/Down, CharacteristicsUp/Down, NameUp/Down, DateUp/Down,
  Favorite`. Names: `GetText(type)`.
- **Search** — `vm.StashVM.ItemsFilter.ItemsFilterSearchVM.SetSearchString(query)` →
  `ItemsFilter.IsMatchSearchRequest`. Needs an accessible text-entry source (see slice 1b).
- **Weapon set** — `Game.Instance.GameCommandQueue.SwitchHandEquipment(unit, set.Index)`; both sets
  are `InventoryDollVM.WeaponSetSelector` radio VMs; gate on `WeaponSetVM.SetEnabled` +
  `CanChangeEquipment`.
- **Empty-slot equip** — `InventoryVM.ShowEquipSelector(EquipSlotVM)` → `InventoryEquipSelectorVM`
  (a `SelectionGroupRadioVM` of candidates); `HideEquipSelector` closes.
- **Compare tooltip** — `ItemSlotVM.Tooltip.Value` is `[compare-bricks…, own-card]`; the mod reads
  only `[last]`. The leading bricks are the vs-equipped diff.
- **Derived stats** — `InventoryDollAdditionalStatsVM` (Deflection, Absorption, Dodge, Dodge-Pen,
  Resolve [hidden for pets], Parry).

All the `inv.*` and `slot.*` locale keys already exist in `assets/locale/enGB/ui.json` (pre-authored,
currently wired to nothing).

## Roadmap

### Slice 1 — faithful stash + filter/sort (THIS SLICE)
- Convert the stash `Table` → single-item-per-row `List` of `ProxyInventoryItem`. Up/Down browses
  items; Type/Weight/Value move to Space (item tooltip). Fixes the wrong abstraction.
- Add a `Filters`/`Sort` `BarRegion` above the stash, each a combo-box control
  (`ProxyChoiceCycler`) whose Enter opens a `ChoiceSubmenuScreen` of localized options and drives
  the game VM/command. (Activate-only — in a multi-cell bar Left/Right navigates between cells.)
- Add current filter + sorter to `ContentSig` so a change forces a refill + re-announce.
- Wire every hardcoded English string in `InventoryScreen`/summary/slot-names to the existing
  `inv.*` / `slot.*` keys.

### Slice 1b — search (next, needs harness verification)
Deferred from 1: search requires an accessible text-entry source. The mod's `TextEntry.Begin`
needs a live `TMP_InputField`; the game's inventory search field (`ItemsFilterSearchPCView`) may be
alive beneath our overlay (parallel-tree) — verify in-harness, then either drive that field or add a
mod-owned text-entry. Add a `Search` cell to the bar once the capture path is confirmed.

### Slice 1c — opt-in inline details (small)
A default-off setting (`inventory.item_details`) that, when on, appends a terse `· Type · Wt · Value`
to each stash row for players who want the numbers one keypress away without opening the tooltip.
Keeps the faithful default while preserving the old table's convenience.

### Slice 2 — weapon-set switch + derived stats  ✅ DONE (compile-clean)
- Weapon-set combo box in the Equipment region: options = the usable sets; select drives
  `WeaponSetVM.SetSelected(true)` → `OnSetChange` → `CurrentSet` → `SwitchHandEquipment`. Both usable
  sets' hands are listed set-numbered ("Set 1/2 primary/secondary hand"); a single-usable-set unit
  (mechadendrites / unique companion, `IsEnabled==false` on set II) shows the active hands unlabelled
  and no switch. The switch trips a `set:` change in `ContentSig` → refill re-announces the active set.
- A `Defences` region reads the reachable `InventoryVM.LevelClassScoresVM.AdditionalStatsVM`
  (Deflection / Absorption / Dodge / Dodge-reduction / Resolve[skipped when "—" = pet] / Parry) — live
  strings + breakdown tooltips on Space. New locale keys: `inv.weapon_set`, `inv.defenses`,
  `slot.set_primary/secondary`, `stat.*`.

### Slice 3 — compare tooltip + item-menu completeness  ✅ DONE (compile-clean)
- **Compare-vs-equipped folded into Space.** New general `UIElement.GetTooltipSections()` hook (default
  none); `TraditionalNavigator.OpenTooltipOrLinks` now, when an element has extra sections and/or
  glossary links, opens the `DrillMenuScreen` with the item's own card first, then the sections, then
  links (single-tooltip case unchanged — fully backward-compatible). `ProxyInventoryItem` returns the
  LEADING `Tooltip.Value` templates (the equipped items rendered with deltas vs this item) as sections,
  rendered via `TooltipReader.GetFull`. So Space on a stash item with equippable comparisons →
  "<item>" + "Compare with equipped [n]". Equipped-slot items have no compares (`EquipSlotVM` isn't
  `typeof(ItemSlotVM)`), so nothing changes there.
- **Auto-add-to-Cargo** context-menu entry added (`ItemSlotVM.AddToCargoAutomatically`, gated on
  `CargoHelper.CanTransferFromCargo && CanTransferToCargo`), label = game string + on/off state. While
  there, the whole `OpenMenu` was re-localized to the game's own context-menu strings
  (`UIStrings.Instance.ContextMenu.*` / `LootWindow.*`) instead of hardcoded English, and `ProxyEquipSlot`'s
  "Take off" → `ContextMenu.TakeOff`. Confirmed the game menu has NO Use/Repair/Unlock — no fake "Use".

**Remaining localization debt (own slice):** the badge words folded into item labels are still
hardcoded English — `ProxyInventoryItem.Name()` ("notable"/"unusable"/rarity/`x{n}`/"{n} charges"/
"favorite") and `ProxyEquipSlot.ItemLabel()` ("notable"/"can't remove"/"empty"). Localize in a dedicated
pass (needs several new `badge.*` keys).

### Slice 4 — empty-slot equip selector  ✅ DONE (compile-clean)
Mod-owned `EquipSelectorScreen` (layer 12, Exclusive) over the doll's `InventorySelectorWindowVM` so an
empty doll slot — or a swap on a filled one — opens an accessible candidate list. Removes the "equip
from the stash row only" workaround.
- **Drive path** (corrected — the audit's `InventoryVM.ShowEquipSelector`/`InventoryEquipSelectorVM` is a
  dead end: all fields private). The live selector is on the DOLL: `InventoryDollVM.HandleChangeItem(slot)`
  builds `SetupSlots` (equippable party items; the worn item inserted at index 0 when the slot is filled,
  so it can be taken off) and sets `doll.InventorySelectorWindowVM.Value` = an
  `InventorySelectorWindowVM : SelectorWindowVM<EquipSelectorSlotVM>`. Public surface: `EntitiesCollection`
  (candidates), `Confirm(candidate)` (→ `GameCommandQueue.EquipItem`, closes on success), `Unequip()`
  (the head/worn row), `Back()` (→ HideSelectionWindow), `Slot` (the target `EquipSlotVM`).
- **`ProxyEquipCandidate`** reads `EquipSelectorSlotVM.DisplayName` + `Tooltip`; Enter = `Confirm` (equip),
  except the head row whose `Item == Slot.Item.Value` → Enter = `Unequip` + an "(equipped)" marker. Both the
  equipped-test and the action are computed LIVE, so an unequip that leaves the window open flips that row
  back to a plain equip candidate without a rebuild.
- **`ProxyEquipSlot`** now carries the `InventoryDollVM`: Enter on an empty slot / the "Choose item" context
  action on a filled one → `doll.HandleChangeItem(slot)`, both gated on `doll.CanChangeEquipment.Value`
  (mirrors the game hiding its change affordance in combat / for pets / non-controllable units).
- **Verbs are game strings** (pass-through): `ContextMenu.Equip` / `ContextMenu.TakeOff` /
  `InventoryScreen.ChooseItem`. Sole new mod key: `inv.equipped` = "equipped".
- **Verify in-harness:** `HandleChangeItem`'s `ChooseSlotMode` branch is console-drag-only (stays false in
  our mouse-mode boot) — confirm no path arms it; a multi-slot item (ring) equipping into the RIGHT slot
  (the selector was opened from a specific `EquipSlotVM`, so `EquipItem(item, unit, slot.ToSlotRef())`
  targets that slot — should be correct, but confirm the ring lands where focused).

### Slice 5 — cargo browse + viewed-character switch (larger, separate windows)
- A screen for the Cargo service window (we can already *send* to cargo; can't *browse* it).
- Drive the shared party strip (`SelectionCharacter.SelectedUnitInUI`) so the doll can be pointed at
  any party member without leaving the window.

### To verify in-harness (do not change blind)
- **Doll roster**: audit suspects Belt/Wrist/Glasses/Shirt have no PC doll tile in RT (we list them)
  and that `doll.Protocol` (pet slot) IS a tile we omit. Confirm against a live doll before editing
  the roster. Slice 1 only *localizes* the existing roster; it does not change membership.
- **Multi-slot equip** (e.g. a ring): does `TryEquip` from a stash row arm the game's `ChooseSlotMode`
  with no accessible slot-pick? Possible correctness snag in the existing equip path.
- **Focus restore across filter/sort change**: the sheet rebuilds on the resulting collection change;
  focus restores by position (established pattern) and should land back on the bar cell — confirm.
