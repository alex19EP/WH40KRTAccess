---
name: tester-feedback-triage-2026-09
description: "Verdicts on the 14-item Russian tester batch (2026-09-12) — 7 mod bugs, 1 vanilla ceiling, 1 misconception w/ parity gap, 6 feature requests; canonical write-up docs/feedback/2026-09-tester-triage.md"
metadata: 
  node_type: memory
  type: project
  originSessionId: 68046499-531f-4863-9ce0-2611f898509c
  modified: 2026-09-12T12:29:27.285Z
---

**STATUS 2026-09-12: ALL LANDED on main, NOT pushed. Bug fixes (1/1b/2/3/5/12/13/14) = 4 commits
(ff31578 chargen, a964a42 party digits, ceecf4b credits pager, 52cffbe inventory batch); feature batch (4, 6–11 + the
ReadVantage anchor mix) merged from `worktree-tester-features-2026-09` in ca67066; the peer's worktree at
`.claude/worktrees/tester-features-2026-09` can be removed. Live verification owed for everything (the harness boot
hung — display asleep).**
Keys: **Shift+R** initiative queue; **Semicolon** per-hand attack lists from the desired position, **Shift+Semicolon**
from the cursor cell; **Shift+Backslash** route legs to the cursor tile and **Ctrl+Backslash** nearest LOS spot to
the cursor tile (USER MOVED these off Shift+Delete / Ctrl+Delete at merge — the Backslash family = bare "go to
selection", Shift = the way to the cursor tile, Ctrl = where to stand to see it); **Shift+PageUp/PageDown** scanner
state sub-categories. Item 11 = absolute compass only.
Canonical write-up with anchors + both implementation-status tables: `docs/feedback/2026-09-tester-triage.md`.

Key non-obvious facts learned (the ones that would be re-derived wrongly):

- **Inventory "hide unavailable" toggle is inverted in the mod, 3 screens** — the game's checkbox is the logical
  inverse of `ItemsFilterVM.ShowUnavailable` (`ItemsFilterPCView.cs:277-280` sets `!value`) under a "Hide…" label
  despite the field name `ShowUnavailableItems`. Mod speaks the raw reactive. Default is true → fresh save reads
  "on" while the sighted box is unticked. Same in AugmentationsScreen; CargoScreen shares the helper.
- **Stash hides items the VIEWED unit can't wear when ShowUnavailable=false, and switching the viewed character does
  NOT re-run the pass** (`SlotsGroupVM.InternalUpdate` triggers: filter/sorter/search/ShowUnavailable/collection
  events only). Equip selector (`InventoryDollVM.SetupSlots`) never applies the flag → "picker lists it, stash doesn't".
- **Loot Enter = `InventoryHelper.TryCollectLootSlot` returns true iff the slot HAS an item** (fire-and-forget
  `CollectLoot` command); `GameCommandHelper.TryCollect` silently skips no-origin trash, already-owned, and
  non-removable holding-slot items. Mod speaks "taken" regardless. Sighted click never single-takes in RT loot.
- **RT has no carry weight**: item Weight consumers are vestigial; `InventoryStashVM.EncumbranceVM` (party) is
  bound by NO view — the mod's "Party encumbrance 0/24000" line is a PF leftover. Only the per-doll
  `CharInfoEncumbranceVM` has a view (`InventoryDollView.cs:80`); whether that bar is active in the prefab is unverified.
- **In-play chargen (Factotum hire = custom companion) lives on `SurfaceVM.StaticPartVM.CharGenContextVM`**, never
  MainMenuVM; `RootUIContext.IsChargenShown` checks both hosts. `CharGenScreen.Vm()` and `NameEntryScreen.Box()`
  hardcode MainMenuVM. CharGenScreen also lacks `Exclusive` and ties DialogueScreen at layer 15 (stable sort →
  dialogue wins); use 16 like ChangeAppearanceScreen. No "Mercenary" type exists in Code.dll.
- **Lord-Captain's Infolog = the game's Credits window in `onlyBakers` mode** (`ShowCredits` action → `CreditsVM(onlyBakers:true)`);
  "Shrine of Remembrance" group = 16,801 names (SilverTier 15,914; SpecialThanks 13,809). Sighted view pages
  ~34/page (~490 pages) with search/prev/next/pause; CreditsScreen emits one node per name, rebuilt per frame.
  Fix = mirror `CreditsBaseView.m_Pages/m_CurrentPage` + `SearchPerson`.
- **Pet protocol is worn by the PET** (`BlueprintItemEquipmentPetProtocol.CanBeEquippedBy` requires `Master` whose
  `UnitPartPetOwner.PetType` matches); the slot exists on every body. Sighted doll hides common slots for a pet
  (`CharInfoNameAndPortraitPCView.cs:124,140`, weapon set only for pet types 1/2); mod doll declares everything and
  the selector path has no pet guard. `InventoryDollVM.CanChangeEquipment` is never written (dead gate in ItemNodes).
- **`ActualGroup` DOES contain pets** (inserted right after the master by `UIUtility.GetGroup`) — two mod comments
  claim otherwise. Alt+1..6 index the roster incl. pet → 6th hero unreachable; vanilla has the same 6-slot ceiling.
- Combat: `CombatReads.VantageFrom` already runs `CanTargetFromNode` per enemy but speaks a count only, one ability
  off `GetFirstWeapon()`; `ReadVantage` mixes anchors (cursor cell vs desired position). `PathInfo.PathNodes` has the
  walked chain, private, never spoken; `FindPathTB_Blocking(limitRangeByActionPoints:false)` never used by the mod.
  FiringPositions is unit-target + safest-first. Initiative one-shot readout: all label helpers internal in
  InGameScreen; Shift+R free (re-run F10 dump). Scanner: flat 17-key taxonomy, Shift+PageUp/Down unclaimed, WA has
  the Node tree to port; `ProxyUnit.HasDialogInteraction` exists but isn't in the taxonomy.
- Map objects have no ordinal numbering (units do). "Already used": `InteractionPart.AlreadyUnlocked` is set on the
  first successful Interact even with no restrictions and is save-persisted — so bark/examine volumes ("Point of
  interest" = nameless `InteractionBarkPart`) DO carry an engine used-flag; no mod-side seen set needed (peer
  session finding while implementing item 4; corrects the triage's "needs a seen set").
- Live-harness gotcha (2026-09-12): with the monitor asleep Windows drops to a 1024x768 fallback mode and the game
  hangs at boot on "DX11 could not switch resolution (1024x768 fs=1)" (0 CPU, log frozen, no window). Launch the
  exe with `-screen-fullscreen 0 -screen-width 1024 -screen-height 768` to get past it.
- Ship facing: `MechanicEntity.Forward` works for any ship; only the player speed row speaks a heading.

Related: [[discord-feedback-triage-2026-08]], [[rt-inventory-window-parity]], [[rt-newgame-chargen]], [[rt-combat-plan]],
[[rt-party-identity]], [[rt-scanner-explorer-audit]]
