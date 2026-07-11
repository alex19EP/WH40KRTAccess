# Ship management UI (ShipCustomization) — exploration report

Verified against the decompiled game code (multi-agent read + adversarial cross-check,
2026-07-10). This is the reference for building the accessible `ShipCustomizationScreen`.
All claims below were spot-checked against the cited files; the four upstream quirks in
"Traps" were re-verified line-by-line.

## 1. Window lifecycle

- The voidship window is **`ShipCustomizationVM`** (`Kingmaker.Code.UI.MVVM.VM.ShipCustomization`),
  a service window. Created **only** by `ServiceWindowsVM.ShowWindow` (case
  `ServiceWindowsType.ShipCustomization`): `ShipCustomizationVM.Value = new ShipCustomizationVM(null,
  m_ShipCustomizationTabType)` — gated on `!RootUIContext.Instance.HasDialog` — and disposed by
  `HideWindow` via `DisposeAndRemove`. `closeAction` is passed as **null**, so `VM.Close()` is a no-op
  (`ServiceWindowsVM.cs:457-463,513-515`).
- **Both scene roots host their own `ServiceWindowsVM`** (`SurfaceStaticPartVM`,
  `SpaceStaticPartVM:152`) — the same window serves surface, star-system map, sector (Koronus) map,
  and space combat. Resolve it via our `UiContexts.ServiceWindows()`.
- Openers all funnel through EventBus `INewServiceWindowUIHandler.HandleOpenShipCustomization()` /
  `HandleOpenShipCustomizationPage(tab)`: `SectorMapBottomHudVM.OpenShipCustomization`,
  `IngameMenuVM.OpenShipCustomization` / `OpenShipLevelUp`. The game's `OpenShipCustomization`
  keybind is only bound while `Player.CanAccessStarshipInventory`.
- Opening is **blocked** by `Player.ServiceWindowsBlocked`, `RootUIContext.IsVendorShow`,
  the exploration window, and Chargen/TransitionMap fullscreens. It is **not** blocked during
  SpaceCombat — the window opens with everything locked.
- **Close**: ESC in the view → `IShipCustomizationForceUIHandler.HandleForceCloseAllComponentsMenu`
  → `ServiceWindowsVM.HandleCloseAll()`. That is the mod's Escape action too.
- **Force-close triggers** (all via `HandleCloseAll`): Cutscene/GameOver/CutsceneGlobalMap/Dialog
  game-mode start, turn-based mode start/switch, area unload, additive-area deactivation,
  zone-loot transition, trade start, multi-entrance, level-up complete, chargen start.
  `IsActive()` flipping false mid-frame is normal; never cache the VM across frames.
- Canonical "is it open" probe used by the game itself: `RootUIContext.IsShipInventoryShown`
  (checks both scene roots' `ShipCustomizationVM.Value`).

## 2. Root VM structure

```
ShipCustomizationVM
├─ Navigation : ShipTabsNavigationVM        ActiveTab drives SelectWindow(tab)
├─ ActiveTab  : ReactiveProperty<ShipCustomizationTab>   { Upgrade, Skills, Posts, Abilities }
├─ CanChangeEquipment : BoolReactiveProperty   ⚠ INVERTED: = (CurrentMode == SpaceCombat),
│                                              passed to every tab VM as isLocked
├─ SpaceShipVM : ShipVM                     name, per-facing armor, per-sector shields, morale,
│                                           crew, XP/level, military & turret rating
│                                           ⚠ ShipShieldValue hardcoded "0/0" (ShipVM.cs:111) —
│                                           speak the four per-sector shield ints instead
├─ ShipStatsVM                              Speed = CombatState.WarhammerInitialAPBlue,
│                                           Inertia = 6 − StatType.Inertia
├─ ShipHealthAndRepairVM                    HP, scrap, CanRepair, RepairShipFull /
│                                           RepairShipForAllScrap (→ Player.Scrap methods)
│                                           ⚠ receives CanChangeEquipment as fromShipInventory
│                                           (upstream arg-order bug); rely on CanRepair
│                                           (= !IsInCombat && damaged && scrap>0), not IsLocked
└─ per-tab fields (public MUTABLE fields, not reactive):
   ShipUpgradeVm, ShipSkillsVM, ShipPostsVM, ShipAbilitiesVM
```

Tab-VM lifetime (verified, subtle):

- `SelectWindow(tab)` disposes the previous instance of the tab being **activated** and news a
  fresh one — the **outgoing** tab's VM is NOT disposed on switch; it stays alive (still
  EventBus-subscribed) until its tab is next activated or the window closes.
- `ShipUpgradeVm` is **always constructed at window creation** regardless of the opening tab
  (`Navigation.ActiveTab` defaults to Upgrade and the UniRx subscription fires immediately).
  → A non-null tab field does NOT mean that tab is active. **Gate every per-tab read on
  `ActiveTab.Value`.**
- `SetCurrentTab(tab)` / `SetActiveTab(tab)` no-op when already active — there is no public
  "rebuild current tab" path. Tab cycling: `Navigation.SetNextTab()` / `SetPrevTab()`
  (`OnNextActiveTab`/`OnPrevActiveTab` are dead code — broken range check).

## 3. Upgrade tab (`ShipUpgradeVm`)

Slots, built from `Game.Instance.Player.PlayerShip.GetHull().HullSlots`
(`Warhammer.SpaceCombat.StarshipLogic.Equipment.HullSlots`):

| VM field | SlotType | Notes |
|---|---|---|
| `PlasmaDrives` | PlasmaDrives | may never be emptied (engine slot) |
| `VoidShieldGenerator` | VoidShieldGenerator | |
| `AugerArray` | AugerArray | |
| `ArmorPlating` | ArmorPlating | |
| `Arsenals` (List) | Arsenal | domain always fixes up exactly 2 |
| `Weapons` (List) | Prow1/Prow2/Port/Starboard/Dorsal | `SetIndex` = index into `HullSlots.WeaponSlots` |
| `InternalStructure` | — | `ShipUpgradeSlotVM`, scrap-level upgrade |
| `ProwRam` | — | `ShipUpgradeSlotVM`, scrap-level upgrade |

Each component slot is a `ShipComponentSlotVM : ItemSlotVM` — all the usual card reactives
(`DisplayName`, `TypeName`, `Count`, `ItemGrade`, `ItemStatus`, `Tooltip`, `ContextMenu`) plus
`SlotType`, `WeaponSlotType`, and **shadowing** `new IsLocked` / `PossibleTarget` fields
(⚠ access via the declared `ShipComponentSlotVM` type, not a base-typed reference).

**Equip flow (PC)**: click slot → EventBus `IShipComponentItemHandler.HandleChangeItem(slotVM)`
→ `ShipUpgradeVm` builds candidates from `Game.Instance.Player.Inventory` (filter:
`GetFilterType(SlotType)` + `PossibleEquipItem` + `HoldingSlot == null`, currently-equipped item
prepended) into `ShipItemSelectorWindowVM` (reactive field `ShipSelectorWindowVM`) → confirm →
`slot.InsertItem(item)` → **`Game.Instance.GameCommandQueue.EquipItem(item, ItemSlot.Owner,
slot.ToSlotRef())`** — an async queued, co-op-synchronized game command executing on a later
frame. Empty candidate list → `NothingToInsertInThisSlot` warning instead of a picker.
`HandleChangeItem` silently no-ops while `CurrentServiceWindow == CharacterInfo`.

Calling `slot.InsertItem(item)` directly (bypassing the picker) is safe and needs no manual
refresh: the queued command raises `IInsertItemHandler` (slot VMs self-refresh) and
`IMoveItemHandler` → `ShipInventoryStashVM.CollectionChanged()`.

**Unequip**: `InventoryHelper.TryUnequip(slotVM)` (PC double-click / context-menu TakeOff) —
refuses while `Player.IsInCombat`, refuses empty/`!CanRemoveItem`, and **always refuses
PlasmaDrives**; success enqueues `GameCommandQueue.UnequipItem`.

**System upgrades** (InternalStructure / ProwRam): the game's UI is a left-click context menu;
the actual actions are `Game.Instance.GameCommandQueue.UpgradeSystemComponent(SystemComponentType)`
/ `DowngradeSystemComponent(...)`. Results arrive async via `IUpgradeSystemComponentHandler`
(UpgradeResult: Successful/MaxUpgrade/NotEnoughScrap/Error) and are toasted through
`IWarningNotificationUIHandler` **with `addToLog:false`** (LogTap never sees them — only the
WarningReader path does). Read state from `ShipUpgradeSlotVM.CurrentProwRamLevel` /
`CurrentInternalStructureLevel` / `CanUpgrade*`, or ground truth
`Hull.ProwRam/.InternalStructure` (`UpgradeLevel`, `IsMaxLevel`, `IsEnoughScrap`, cost =
`Blueprint.UpgradeCost[UpgradeLevel + 1]`). Currency: `Game.Instance.Player.Scrap` (int-castable).

**Stash**: there is no separate ship inventory — `PartInventory.SetupInternal` binds any
player-faction owner without `HasOwnInventory` to the shared `Game.Instance.Player.Inventory`,
so `ShipInventoryStashVM` (filter `ShipNoFilter`) and the picker enumerate the same collection.
Equipping never moves items between collections; only `item.HoldingSlot` changes.
Filter contract (`ItemsFilter.ShouldShowItem`): ShipNoFilter = any `BlueprintStarshipItem`;
ShipWeapon = `BlueprintStarshipWeapon`; ShipOther = the 8 component types + `BlueprintItemArsenal`.

**Domain-only slots**: `HullSlots` also owns WarpDrives, GellerFieldDevice, LifeSustainer, Bridge —
they have **no `ShipComponentSlotType` and no UI slot**; `InventoryHelper.GetEquipShipSlot` throws
for them. Read-only announcement at most; never offer swap actions.

**Weapon slot details**: `WeaponSlot.Weapon` ≠ `MaybeItem` — with `ActiveWeaponIndex > 0` it
returns a synthetic arsenal-variant weapon. "Physically installed" = `MaybeItem`; "what fires" =
`Weapon`. Arsenal insert/remove cascades (`RefreshWeaponSlots` rebuilds variants + re-equips ammo).
`ItemSlot.Item` throws on empty — always `MaybeItem` in scraping code.

## 4. Skills tab (`ShipSkillsVM` → `ShipProgressionVM`)

Ship leveling reuses the character career-path machinery: one `CareerPathVM` over
`ProgressionRoot.Instance.ShipPath` with unit = `PlayerShip`; a `LevelUpManager` (preview unit,
`autoCommit:false`) exists only while `Unit.Progression.CanUpgradePath(shipPath)` (false in
combat — `CareerPathVM.ReadOnly.Value` is the browse-only signal).

- XP display: `ShipInfoExperienceVM` — `ShipExperience` ("cur"), `NextLevelExp`, `ShipLvl`,
  `Ranks` (unspent = ExperienceLevel − CharacterLevel), `CanLevelup`; polls InfrequentUpdate.
  Ground truth: `PlayerShip.StarshipProgression.Progression`. ⚠ `NextLevelExp` uses
  CharacterLevel+1 while `ShipExperience`'s "next" uses ExperienceLevel+1 — they diverge when
  ranks are banked. ⚠ `AvailablePoints` is dead (never written) — do not expose.
- The `ShipCareerPathSelectionTabs*` views are **not** a career chooser — just three display modes
  of the one ship path.
- Level-up walk: `CareerPathVM.RankEntries` → auto-granted `RankEntryFeatureItemVM` + choice
  `RankEntrySelectionVM`. On a selection: `HandleClick()` (populates `FilteredGroupList` — empty
  until then), pick an item → `featureVM.Select()` (gate on `CanSelect()`). Traversal helpers the
  console uses: `CareerPathVM.SelectNextItem/SelectPreviousItem/SetRankEntry`.
- Commit: gate on `CareerPathVM.CanCommit.Value`, call `CareerPathVM.Commit()` (validates; →
  `ShipProgressionVM.Commit()` → `AddStarshipLevel(manager)` → async `CommitLvlUp` game command).
  Closing the tab/window mid-level-up **silently discards** selections — announce unsaved state.
- Ship level-up IS logged (`StarshipLevelUpLogThread` via `IStarshipLevelUpHandler` +
  `GameLogEventStarshipExpToNextLevel`) — LogTap voices it; add no duplicate announcement.

## 5. Posts tab (`ShipPostsVM`)

- State of record: `PlayerShip.Hull.Posts` (`List<Post>`); `PostType` enum: SupremeCommander,
  MasterOfOrdnance, EnginseerPrime, WarpGuide, MasterHelmsman, MasterOfEtherics. Per-post
  blueprint data via `Post.PostData` (`AssociatedSkill`, `DefaultAbilities`).
- **Post display names are not on the VM/entity**: `UIStrings.Instance.SpaceCombatTexts
  .GetPostStrings(index).Title/.Description`, index = position in the Posts list
  (= `PostEntityVM.Index`). Skill name via `LocalizedTexts.Instance.Stats.GetText(stat)`.
- Two radio groups sharing one selection: `PostSelectorVM` (one `PostEntityVM` per post,
  auto-selects the first; selection on `ShipPostsVM.CurrentSelectedPost`) and
  `PostOfficerSelectorVM` (one `PostOfficerVM` per candidate: MainCharacter + Active + Remote
  companions, `!IsPet` is the ONLY eligibility rule; list padded to 9 with `Unit == null`
  placeholders — filter them out).
- Officer card readout: **plain public fields** (`SkillValue`, `SkillName`, `SkillRecommendation`
  Best/Normal/Bad, `UnitAbility`, `PostSprite`) recomputed in `UpdatePostData`, signaled by the
  `DataUpdated` ReactiveCommand. Skill numbers are advisory only (they affect ability
  cooldowns/ultimate lock, not eligibility).
- **Assign/unassign**: `PostOfficerVM.DoSelect()` / `DoUnselect()` →
  `Game.Instance.GameCommandQueue.SetUnitOnPost(unit|null, post.PostType, PlayerShip)` (async;
  auto-vacates the unit's previous post; swaps attuned abilities in/out of `Ship.Abilities`;
  broadcast `IOnNewUnitOnPostHandler.HandleNewUnit`). Trust `Post.CurrentUnit == vm.Unit` for
  assigned state, not `IsSelected`. ⚠ `PostOfficerVM.TryUpdateSelect()` NREs with no post
  selected — never call it with null `CurrentSelectedPost`.
- **Post abilities**: `AbilitiesInfoGroupVM.CurrentAbilities` (`PostAbilityVM`: `DisplayName`,
  `IsUnlocked`/`LockedReason`, cooldown fields, attune prerequisites `IsAttunable`/
  `IsAlreadyAttuned`/`IsEnoughScrapForAttune`/`IsUsed`/`IsFullHP`/`ScrapRequired`, `CanAttune`).
  Attune action: `TryAttune()` → `GameCommandQueue.AttuneAbilityForPost(post, ability)`; result
  via `IOnPostAbilityChangeHandler`. Attunement persists per (unit blueprint, default ability)
  across unassignment.

## 6. Abilities tab (`ShipAbilitiesVM`)

One-shot snapshot built in the constructor: `ActiveAbilities` / `PassiveAbilities` as
`List<CharInfoFeatureVM>` (plain fields: `DisplayName`, `Description`, `Rank`). Never refreshes
until the tab is re-entered.

## 7. Action summary (what the mod drives)

| User action | Call | Result event |
|---|---|---|
| Switch tab | `ShipCustomizationVM.SetCurrentTab(tab)` / `Navigation.SetNextTab()` | `ActiveTab` |
| Open item picker for slot | `ShipUpgradeVm.HandleChangeItem(slotVM)` (or EventBus `IShipComponentItemHandler`) | `ShipSelectorWindowVM` |
| Equip | `slotVM.InsertItem(item)` → queued `EquipItem` | `IInsertItemHandler` / `IInsertItemFailHandler` |
| Unequip | `InventoryHelper.TryUnequip(slotVM)` → queued `UnequipItem` | `IUnequipItemHandler` |
| Upgrade structure/ram | `GameCommandQueue.UpgradeSystemComponent(type)` / `Downgrade...` | `IUpgradeSystemComponentHandler` (+ double toast, addToLog:false) |
| Repair ship | `ShipHealthAndRepairVM.RepairShipFull()` / `RepairShipForAllScrap()` (gate on `CanRepair`) | HP reactives |
| Select post | `PostSelectorVM.Selector.SelectedEntity.Value = vm` (or `vm.SetSelected(true)`) | `CurrentSelectedPost` |
| Assign/vacate officer | `PostOfficerVM.DoSelect()` / `DoUnselect()` → queued `SetUnitOnPost` | `IOnNewUnitOnPostHandler` |
| Attune ability | `PostAbilityVM.TryAttune()` (gate on `CanAttune`) → queued `AttuneAbilityForPost` | `IOnPostAbilityChangeHandler` |
| Level-up choice | `RankEntrySelectionVM.HandleClick()` then `featureVM.Select()` | preview-unit rebuild |
| Commit level-up | `CareerPathVM.Commit()` (gate on `CanCommit.Value`) | `IStarshipLevelUpHandler` + game log |
| Close window | `ServiceWindowsVM.HandleCloseAll()` | window VM disposed |

**Everything mutating is a queued synchronized GameCommand** — nothing changes on the calling
frame. Speak results from the EventBus handlers / reactives, never synchronously after the call.

## 8. Traps (verified upstream quirks)

1. **`CanChangeEquipment` is inverted** — true = locked (space combat).
2. **Tab fields ≠ active tab** — Upgrade VM always exists; outgoing tab VMs stay alive. Gate on
   `ActiveTab.Value`.
3. **`ShipHealthAndRepairVM` arg-order bug** — the lock flag lands in `fromShipInventory`;
   use `CanRepair`.
4. **`GetSlotDescription` has no Port case** — Port weapon slots have an empty game-side slot
   description; compute slot labels from `SlotType` ourselves and cover Port
   (use `UIStrings.ShipCustomization.ShipWeapon`).
5. **Double toast on system upgrades** — both `ShipUpgradeSlotVM` twins subscribe to
   `IUpgradeSystemComponentHandler` and both raise the warning; dedupe in warning voicing or
   announce from the handler once ourselves.
6. **`InternalStructure.Upgrade()` off-by-one** — throws at max level (checks `<` not `<=`);
   always gate on `CanUpgrade*`/`IsMaxLevel`.
7. **Prow labeling reversed** (first Prow slot becomes `Prow2`) — cosmetic; and
   `ShipUpgradeVm.Weapons[j]` desyncs if a ship blueprint ever ships a Keel slot — enumerate
   `HullSlots.WeaponSlots` directly if we build our own list.
8. **Dead fields**: `ShipComponentSlotVM.NeedRepair`, `ShipInfoExperienceVM.AvailablePoints` —
   never speak them.
9. Card label vs hover hint differ ("Engine" vs "Plasma Drives") — browse label mirrors the card;
   the hint is secondary.
10. Slot `Tooltip.Value` is a `List<TooltipBaseTemplate>` (comparative) on component slots but a
    single template on picker rows.

## 9. Integration recipe (our screen pattern)

- `IsActive()`: `UiContexts.ServiceWindows()` non-null `&& CurrentWindow ==
  ServiceWindowsType.ShipCustomization && sw.ShipCustomizationVM?.Value != null` (the VM can be
  null even when "opened" — dialog gate). One `Register(new ShipCustomizationScreen())` line in
  `ScreenManager.Initialize()`; openers already exist in InGameScreen / SystemMapScreen /
  SectorMapScreen and `ServiceWindowInfo` already labels + gates it.
- `ScreenName` must be **null** — `ServiceWindowAnnounce` already speaks the window name on open.
- Tab row: pattern A (SettingsScreen/ColonyManagementScreen recipe) — `GraphNodes.ChoiceOption`
  rows driving `SetCurrentTab`, selected state read from `ActiveTab` (the selection, not
  hover-poisoned per-tab reactives), `SetStart` on the active tab, content-stop keys embed the
  active tab.
- Reuse `ItemNodes` for component slots/stash/picker rows, `TooltipChooser.OpenTemplate` for the
  game's tooltip templates, `ChoiceSubmenuScreen` for the picker/context verbs, the
  EncyclopediaScreen `_navigated/_navFrame` recipe for async focus after VM swaps, and the
  UniqueKey dedup helper for repeated labels.
- The game's own Posts/slot views stay live under our overlay (EventSystem Submit/click) — the
  usual ownership-gating concern applies for destructive double-fire paths.
- The Skills tab should reuse the mod's existing career-path screen recipe (CharGen/level-up).
