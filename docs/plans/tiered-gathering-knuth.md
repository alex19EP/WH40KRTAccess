# Accessible looting — tiered-gathering-knuth

Goal: surface the game's loot window to a blind player as a mod-owned navigable screen, covering the FULL loot
system (all six `LootContextVM.LootWindowMode`s) across several passes. Follows the established service-window
mirror pattern (`InventoryScreen`, `VariativeInteractionScreen`, `TransitionScreen`).

## How sighted looting works (verified in decompiled source)

1. **Trigger.** Interacting with a container (the same `ClickMapObjectHandler` path the mod's `I`/`Enter` already
   fire) reaches `InteractionLootPart.OnInteract`, which raises
   `EventBus → ILootInteractionHandler.HandleLootInteraction(objects, containerType, closeCallback)`.
   `InteractionLootPart.CanInteract()` is **false in turn-based combat** (`TurnController.TbActive`) — no looting mid-fight.
2. **Window.** `LootContextVM` (subscriber at `RootUiContext.SurfaceVM.StaticPartVM.LootContextVM`, plus a `SpaceVM`
   twin) handles the event and creates `LootVM.Value`. It picks a **mode**:
   `StandardChest` (normal chest), `Short` (multi-object / environment / dropped loot), `ShortUnit` (dead body's
   inventory), `OneSlot` (a device slot you insert into), `PlayerChest` (your stash), `ZoneExit` (mass-loot before
   leaving the area — its close fires the area transition).
3. **Contents.** `LootVM.ContextLoot` = `ReactiveCollection<LootObjectVM>`, normally two groups: `Normal` (→ party
   inventory) and `Trash` (auto-routes to ship **cargo**). Each `LootObjectVM.SlotsGroup` (`ItemSlotsGroupVM`) exposes
   the items as `ItemSlotVM` (`.VisibleCollection`) / `ItemEntity` (`.Items`). `LootVM.NoLoot` flags empty.
4. **Actions.** Move an item → inventory (or between normal/trash). **Take all** → `LootVM.TryCollectLoot()`
   (`GameCommandQueue.CollectLoot(normal)` + `TransferItemsToCargo(trash)`, then auto-closes unless ExtendedView).
   **Close** → `LootVM.Close()`; for `ZoneExit`, close fires the transition via `LeaveZone()`.

## Why it's accessible-ready (reuse)

- Loot items are `ItemSlotVM` → **`ProxyInventoryItem` works unchanged** (name/badges/tooltip + Equip + context menu:
  send to cargo/inventory, split, drop, favorite), routed via `EventBus IInventoryHandler` / `GameCommandQueue`.
- `LootVM` **implements `IInventoryHandler`** (`TryMoveToInventory`/`TryMoveToCargo`/…), so the proxy's existing move
  verbs are already handled by the open loot window — no new plumbing for per-item take.
- `InventoryScreen` is the structural template: a `FlowSheet` of item tables with capture/restore focus across the
  virtualized-collection rebuilds; closes via the window's own callback.

## Screen design (`LootScreen`)

- **Reachability / IsActive:** `LootContextVM.LootVM.Value != null` (check Surface + Space, like `InventoryScreen.Vm()`).
- **Layer / modal:** loot is a full-screen UI (`HandleFullScreenUiChanged(true, FullScreenUIType.Loot)`); make it
  `Exclusive` so the mod owns the keyboard. Layer near the service windows (≈10) — confirm vs the full-screen flag.
- **Title:** container name — `InteractionLootPart.Name` / `UIStrings.LootWindow.GetLootNameByContext(mode)`.
- **Body:** mirror `ContextLoot` groups → item tables of `ProxyInventoryItem` (reuse `InventoryScreen.BuildStash`
  shape: Name / Type / Quantity / Weight / Value). Capture/restore focus across rebuilds (`LootUpdated` fires).
- **Global actions:** **Take all** (`TryCollectLoot()`), **Back/Close** (`Close()` / `LeaveZone()`), empty → announce + close.
- **Register** in `ScreenManager.Initialize()` (the `// TODO: … Loot …` spot).

## Passes

- **Pass 1 — Foundation + read/take (`StandardChest`, `Short`, `ShortUnit`). SHIPPED 2026-07-02 (compile-verified
  0/0; NOT yet in-game).** `LootScreen` (Exclusive, layer 24; `Vm()` resolves Surface+Space
  `StaticPartVM.LootContextVM.LootVM.Value`; `IsActive` gated to the three Pass 1 modes so ZoneExit/OneSlot/
  PlayerChest stay inert until their passes). Body = one `FlowSheet` List titled by mode (Chest/Remains/Loot):
  a leading **Take all** button (`LootVM.TryCollectLoot`) then a flat `ProxyLootItem` per `HasItem` slot across
  both `ContextLoot` groups. **Per-item take** = a dedicated `ProxyLootItem` (NOT `ProxyInventoryItem` — its
  actions are equip/move/split/drop, wrong for a loot slot) whose Enter calls `InventoryHelper.TryCollectLootSlot`
  → `GameCommandQueue.CollectLoot([item])`, speaking "{name} taken."; reuses the inventory badge/tooltip readout.
  **Close** = Escape → `LootVM.Close()`. **Empty** (NoLoot) → a single focusable "Empty. Press escape to close."
  button. Capture/restore focus across the virtualized rebuilds (copied from `InventoryScreen`). Take-all
  auto-closes unless the player's LootExtendedView setting is on (then it shows the empty line). Note: per-item
  take routes to **inventory** regardless of normal/trash; only Take all sends trash → cargo (game default).
- **Pass 2 — `ZoneExit`. SHIPPED & VERIFIED IN-GAME 2026-07-02.** The mass-loot-before-leaving prompt (raised by
  `AreaTransitionGroupCommand` when reaching an exit with unlooted loot). `LootScreen` title "Leaving area";
  **Take all and leave** (`TryCollectLoot` → collect + fire the transition), **Leave without taking** (`LeaveZone`),
  Escape = **Stay** (`Close` cancels the transition). Bypasses the game's `ExitLocationWindowVM` confirm (our buttons
  are explicit). Also shipped: a **skill-check-result** header line (`LootCollectorVM.SkillCheckText`).
- **Pass 3 — `OneSlot`.** Insert an item from party inventory into a device slot
  (`LootVM.InteractionSlot`, `INewSlotsHandler.HandleTryInsertSlot`, `ILootable.CanInsertItem` gate) — needs a
  "pick an item from inventory to put" flow (a `ChoiceSubmenuScreen` over insertable stash items).
- **Pass 4 — `PlayerChest` + cargo.** Two-way stash (deposit/withdraw via `PlayerStashVM`) and cargo management
  (`CargoInventory`, `InventoryCargoVM`). Overlaps the inventory screen's cargo handling; largest surface.

## Reaching a body (SHIPPED 2026-07-02, mirrors WrathAccess)

A `ShortUnit` window is useless if a blind player can't SELECT the corpse. WrathAccess uses no dedicated corpse key:
a dead unit with loot is a container-type interactable, cycled/browsed like a chest and looted by the generic
interact key. RT now matches. `ProxyUnit.LootableCorpse = IsDeadAndHasLoot && !IsInCombat`; when true its `Primary`
flips to `ScanTaxonomy.Corpses` (leaving the enemy cycle), `CanInteract` is true, and `Interact()` reuses the game's
own `new ClickUnitHandler().OnClick(...)` (walks the nearest selected party member over → opens the ShortUnit loot
window). `Scanner`'s dead-filters became `(!IsDead || LootableCorpse)`; a **Corpses** category was added and `Corpses`
joined the object (`M`) review cycle. A body is reachable via the Corpses category (Ctrl+PageUp/Down) or `M`,
announced "name, faction, dead", looted with `I`. Emptied/lootless corpses stay hidden; none appear in combat.
NO skinning: RT (unlike Pathfinder/WrathAccess) has no skinning loot mechanic — verified across the whole
decompiled tree (`LootCollectorVM` has no `UseSkinning`; no skinning command/interaction/string; the game's
`UILoot` strings have no skinning verb; `DroppedLoot.IsSkinningDisabled` is defined but never consumed). The
copied-over `loot.needs_skinning` locale key was dead and has been removed. RT's real adjacent loot feature is the
**skill-check result** (`LootVM.SkillCheckResult` / `LootCollectorVM.HasSkillCheck` + `SkillCheckText`, populated on
star-system exploration loot) — a candidate readout for the LootScreen, NOT skinning.

## Cross-cutting

- **Combat:** looting is out-of-combat only (engine gate) — nothing extra needed, but announce clearly if a container
  can't be opened.
- **Surface + Space:** resolve the `LootContextVM` from whichever context is live (both exist under `RootUiContext`).
- **Localization:** new `loot.*` keys in `assets/locale/enGB/ui.json` (title fallbacks, "empty", "take all", outcome).
- **Interaction outcome:** taking/collecting already routes through the game commands; no extra feedback plumbing for Pass 1.

## Risks / sequencing

- **Shared-file collisions:** `ScreenManager.Initialize()` and `ui.json` are being edited concurrently by the
  `LogReviewScreen` work — touch them only when settled, or in a tight, isolated hunk.
- The loot window **already opens invisibly** when the player interacts with a container via `I`/`Enter` today — Pass 1
  is what makes that opened window usable (and stops the modal from silently swallowing input).

## Verify (per pass)
Rebuild via `scripts/dev-game.ps1`, open the relevant container type, confirm: items list with names/counts,
per-item take moves it to inventory, Take all empties the container, Back closes, empty container announces + closes.
