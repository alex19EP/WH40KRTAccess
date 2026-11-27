---
name: rt-loot-accessibility
description: "Accessible loot window mirrors LootVM as mod-owned screens; covers the 6 LootContextVM.LootWindowMode cases (standard chest, ZoneExit mass-loot, corpse, OneSlot insert, PlayerChest stash) + drive-paths and gotchas"
metadata: 
  node_type: memory
  type: project
  originSessionId: 0fafa837-7aef-4718-ab43-8bceb974a28c
---

**Accessible looting** surfaces the game's loot window as mod-owned navigable screens (service-window
mirror pattern, [[rt-newgame-chargen]]-style). Full plan (6 `LootContextVM.LootWindowMode`s):
`docs/plans/tiered-gathering-knuth.md`.

**How sighted loot works (decompiled-verified):** interacting with a container → `InteractionLootPart.OnInteract`
→ `EventBus ILootInteractionHandler.HandleLootInteraction` → `LootContextVM` (on both `SurfaceStaticPartVM` and
`SpaceStaticPartVM`, sibling of `ServiceWindowsVM`) creates `LootVM.Value` with a mode. `LootVM.ContextLoot` =
`ReactiveCollection<LootObjectVM>` (Normal → inventory, Trash → ship cargo); each `LootObjectVM.SlotsGroup`
(`SlotsGroupVM<ItemSlotVM>`) exposes `.VisibleCollection` of `ItemSlotVM`. `LootVM.NoLoot` flags empty.
Per-item take = `InventoryHelper.TryCollectLootSlot(ItemSlotVM)` → `GameCommandQueue.CollectLoot([item])`.
Take all = the game's `LootVM.LootCollector.CollectAll()` (NOT a reimpl — see [[rt-reuse-game-methods]]). Close =
`LootVM.Close()`. Looting is out-of-combat only (engine gate).

**Standard chest (modes `StandardChest`/`Short`/`ShortUnit`):** `RTAccess/Screens/LootScreen.cs` (Exclusive, layer 24;
`Vm()` resolves Surface+Space; `IsActive` gated so OneSlot/PlayerChest stay inert; one `FlowSheet` List titled by
mode; leading **Take all** button then flat `ProxyLootItem` per `HasItem` slot; NoLoot → "Empty" button; capture/
restore focus like `InventoryScreen`) + `RTAccess/UI/Proxies/ProxyLootItem.cs` (reuses the inventory name/badges/
tooltip readout; Enter → `TryCollectLootSlot`, speaks "{name} taken."). Per-item take routes to **inventory**
regardless of normal/trash; only Take all sends trash → cargo.

**Looting a BODY (mirrors WrathAccess).** No dedicated corpse key: a dead unit with loot is a container-type
interactable. `ProxyUnit`: `LootableCorpse => IsDeadAndHasLoot && !IsInCombat`; when true `Primary` flips to
`ScanTaxonomy.Corpses` (LEAVES the enemy cycle), `CanInteract` true, `Interact()` = `new ClickUnitHandler().OnClick(...)`
(game's own unit click → walks nearest party member over → opens ShortUnit loot). `Scanner`: `!IsDead` filters became
`(!IsDead || LootableCorpse)`, a **Corpses** category added, joined the Objects (M) cycle. Body reachable via
Corpses category (Ctrl+PageUp/Down) or M, looted with **I**. Emptied/lootless corpses hidden; none in combat.
NO SKINNING in RT (verified whole tree; `loot.needs_skinning` key was dead → removed).

**ZoneExit mode** — mass-loot prompt at an area exit with unlooted loot. `LootScreen` title "Leaving area"; **Take all**
→ the game's `LootCollector.CollectAll()` which for ZoneExit RAISES the game's `ExitLocationWindowVM` "collect all
before leaving?" confirm (we do NOT bypass it — [[rt-reuse-game-methods]]), surfaced by
`RTAccess/Screens/ExitLocationScreen.cs`; **Leave without taking** → `LeaveZone`; Escape = **Stay** → `Close` (cancels
the transition). `ExitLocationScreen` (Exclusive, layer 26) is a plain `ListContainer` of two `ProxyActionButton`s
built in OnPush — **Accept** "Collect all and leave" (`Confirm()`) + **Cancel** (`Decline()`), navigated by ARROWS
(not Tab), Escape also declines. (An earlier FlowSheet-in-Panel version left the confirm with no reachable focus
target / no Cancel button — the ListContainer rewrite fixed it.)

**Skill-check readout** — `LootScreen.AddSkillCheck` adds a focusable header line reading
`LootVM.LootCollector.SkillCheckText` when `HasSkillCheck` (star-system exploration loot). RT's only "extra
loot-window state" (no skinning).

**Space = collect-all (2026-07-12, EAR-TESTED works)** — sighted parity: the game's rebindable `CollectAllLoot`
hotkey defaults to SPACE (context-shared with Pause/EndTurn; the sighted button reads "Take all [Space]" —
`LootCollectorPCView` binds it via `Game.Instance.Keyboard.Bind`, gated `!NoLoot`). Mechanism (REUSABLE): new
`ActionIds.Space` — a screen advertising it in `GetActions()` claims Space outright as the game's contextual
Space verb; `GraphNavigator`'s `ui.tooltip.space` case checks `Screen.InvokeAction(ActionIds.Space)` BEFORE the
focus guard + tooltip fallback (tooltip stays on F1 there). `LootScreen` advertises it gated `!vm.NoLoot.Value`
→ same `TakeAll()` as the button (ZoneExit → the game's exit confirm). NOT wired on PlayerChest/OneSlot: the
game's bind is technically live there too (`m_Collector.Bind` is unconditional in `LootView`) but collect-all
would mass-move the personal stash — deliberate divergence, revisit if the user wants it.

**OneSlot mode** — insert a party item into a device slot (fuse/cog/key). Own `RTAccess/Screens/OneSlotLootScreen.cs`
(Exclusive, layer 24; `IsActive = LootVM.IsOneSlot`; LootScreen excludes OneSlot so exactly one is active). Body =
FlowSheet List titled by device name (`LootVM.InteractionSlot.Name`, `.Description` read on open): optional
**remove-inserted** row when the device slot is filled (Enter → `InventoryHelper.TryCollectLootSlot(InteractionSlot.ItemSlot.Value)`
— the OneSlot eject), then the party items satisfying the device's insert condition (`InsertableLootSlotVM.CanInsert`,
from `LootVM.InventoryStash.InsertableSlotsGroup.VisibleCollection`), each a **`RTAccess/UI/Proxies/ProxyInsertItem.cs`**
whose Enter INSERTS via the game's own `InventoryHelper.InsertToInteractionSlot(slot, InteractionSlot)`
(== `LootVM.HandleTryInsertSlot`; ejects any current item then transfers the choice in). Only CanInsert items surfaced.
Insert does NOT close (authored put-trigger may fire); Escape = `Close()`. `insert.*` locale keys. Gate =
`InteractionLootPart.CanInsertItem` → `Settings.LootConditions.Check()`.

**PlayerChest mode (two-way stash)** — the personal shared stash. Own `RTAccess/Screens/PlayerChestScreen.cs`
(Exclusive, layer 24; `IsActive = LootVM.IsPlayerStash`; LootScreen + OneSlotLootScreen both exclude it). Body = two
regions in one FlowSheet (Ctrl+Up/Down between): **Chest** (`LootVM.ContextLoot[0].SlotsGroup.VisibleCollection`) +
**Inventory** (`LootVM.InventoryStash.ItemSlotsGroup`), each a **`RTAccess/UI/Proxies/ProxyStashItem.cs`** (one class,
`fromChest` bool branches the action). KEY: `PlayerStashVM` is a thin display wrapper — collections + transfer logic
live on `LootVM`. **Withdraw** (chest→party) = raise `EventBus ILootHandler.HandleChangeLoot(slot)` (PlayerChest branch
→ `TryCollectLootSlot` → `CollectLoot`). **Deposit** (party→chest) = raise `EventBus IInventoryHandler.TryMoveToCargo(slot, false)`
— misleadingly named but in PlayerChest mode the deferred (immediately:false) move routes to
`InventoryHelper.TryTransferInventorySlot(slot, ContextLoot[1])` = into the CHEST not cargo (gated by slot
`CanTransferToCargo`). Escape = `Close()`. `stash.*` keys. NO deposit-all/take-all in the sighted PlayerChest panel
(only per-slot moves + Close). **Deferred:** the window's separate **cargo** panel (`LootVM.CargoInventory` /
`CargoSlotVM`, ship trade goods) not surfaced yet.

Ties to [[rt-object-interaction]], [[rt-reuse-game-methods]], [[ui-paradigm-pivot]], [[rt-inspect-contract]].
