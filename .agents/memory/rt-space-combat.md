---
name: rt-space-combat
description: "Space/voidship combat a11y — mode is a re-skin of surface TB combat (same grid/TurnController/ActionBarSlotVM/aim pipeline/TB-move funnel); new = ShipPath inertia movement, sector shields, posts, weapon arcs, torpedo turns, exit popup; plan doc + Phase 0 live-verify checklist"
metadata: 
  node_type: memory
  type: project
  originSessionId: 34830160-ecba-47c4-9f2d-ad12b417b131
---

Research + **Phase 0 live verification done 2026-07-12** (dev harness, real
`ScenarioSpaceCombat_1` fight); plan + full verified findings (§0b):
`docs/plans/inertial-broadsiding-tsiolkovsky.md`. **Act-loop execution-proven live:**
`CommandDispatch.MoveTo(node)` moves the ship verbatim (the pointerless
double-`MoveSelectedUnitsToPoint` preview→confirm does NOT commit — keep our funnel);
`CommandDispatch.UseAbilityOnUnit` fires ship weapons; `EndTurn` works; grid is the same
1.35 m 500×500; inspect handler populates `SpaceVM.StaticPartVM.InspectVM` with
`TooltipTemplateSpaceUnitInspect`. Forced movement law: starship `CanEndTurn()` false while
`ActionPointsBlue >= FinishingTilesCount && HasAnotherPlaceToStand` — voice "must move".
Refusals via `ad.CanTarget(tw, out UnavailabilityReasonType? r)` (nullable out):
TargetTooFar / HasNoLosToTarget(=arc). LogTap already narrates attacks richly (count +
shield + hull damage); turn cues fire but need: drop "0 AP" for ships, dedupe our "Round N"
vs the log's "Раунд N", add IShieldAbsorbsDamageHandler sector cue. Speed modes are
ability-driven (WarhammerContextActionSetStarshipSpeedMode) — no dedicated UI.
`AbilityTargetUIDataCache` lives in `Kingmaker.UI.SurfaceCombatHUD`.

**The architecture verdict:** `GameModeType.SpaceCombat` reuses the surface substrate —
same square `CustomGridGraph` (ship `SizeRect` footprints, 8-dir facing), same
`TurnController` (`TryEndPlayerTurnManually`, shared `InitiativeTrackerVM`), weapons/posts/
abilities are plain `ActionBarSlotVM`s, same aim pipeline (`ClickEventsController.
WorldPosition` → `AbilityRange` → `ICellAbilityHandler` → `AbilityTargetUIData`, and
`ShipPathNodeMarkersController` reads the same injected field → [[rt-combat-plan]]
AimPointerDriver carries over), same TB move commit (`TryCreateMoveCommandTB` + virtual-move
confirm; starships skip `CanUnitStandOnNode`). So TurnCues/LogTap/ActionBarNodes/
CommandDispatch/Aim* mostly carry over; **no mod base context is active in the mode today**
(InGameScreen=IsSurface, SystemMapScreen=StarSystem) — that's the gap.

**Genuinely new:** movement = `PartStarshipNavigation` + `ShipPath` (inertia + blue-AP
budget + LastStraightMoveLength/turnAngles → `PathCell{Direction,Length,CanStand}`;
`GetEndNodes()`; speed modes Normal/Decel/Low/FullStop decaying end-of-turn; push/finish
phases; dead-end escape); sector shields `PartStarshipShields` (Fore/Port/Starboard/Aft) +
per-sector armour stats; 6 posts (`Post{CurrentUnit,CurrentSkillValue,IsBlocked,
UnlockedAbilities}`, names via `UIStrings.SpaceCombatTexts.GetPostStrings`); weapon arcs
(`WeaponSlot.IsTargetInsideRestrictedFiringArc` / `GetRestrictedFiringArcNodes`, arsenal
variants via `ActiveWeaponIndex`, ammo `Charges`); **torpedo player-turns** (soft
`StarshipEntity` units, `IsPlayerTurn && CurrentUnit != PlayerShip`); rewards popup
`ExitBattlePopupVM` (`ExitBattle(forceOpenVoidshipUpgrade)` → ShipCustomizationScreen).

**Prediction/parity:** hit = `RuleStarshipCalculateHitChances`; damage/shields =
`AbilityDataHelper.GetStarshipDamagePrediction(target, casterPos, ability,
ability.StarshipWeapon)`; overtip hit block reuses `AbilityTargetUIData` (`BurstHitChances`,
`UnitState.IsStarshipAttack`) — hover-gated, so keep our aim-read path. Events:
`IStarshipAttackHandler` (hit/crit/damage/shield-loss/hit-location),
`IShieldAbsorbsDamageHandler`, `ITorpedoSpawnHandler`, `IStarshipPostHandler`.
`ShipCrewPanelVM`/`ShipShieldsPanelVM` are ORPHANED (never instantiated) — field recipes
only; read parts directly. No fog in space combat scenes — gate on `IsVisibleForPlayer` +
`HideRealHealthInUI` only.

**All phases MERGED TO MAIN 2026-07-12** (merge commit c6381b5; the branch was rebased on main
first, which brought in the parallel cargo/holo-sim/frontier work).

**Phase 1 SHIPPED + live-verified 2026-07-12**: `SpaceCombatScreen` (`ctx.spacecombat`) gated on the game's SpaceCombat HUD
component existing (`SpaceStaticPartVM.TryGetComponentVM` — survives Dialog/Cutscene overlays since
`CreateVMs(null)` early-returns for unmapped modes; SystemMapScreen excludes it). Ship + Battle
Tab stops; initiative rendered via `InGameScreen.InitiativeLabel` (made internal — starships are
BaseUnitEntitys). Ship turn cue = "movement N + speed word" (`combat.your_turn_ship`).
**Round cue INVERTED by user call (2026-07-12): keep the GAME's "Раунд N" toast** (from
`InitiativeTrackerView.RoundChanged`, NOT a log thread — reaches speech via WarningReader,
already localized in the game's language) **and drop the mod's own `combat.round` cue** from
CombatEvents (round 1 has no toast but coincides with "Battle begins"; the battle status line
carries the labelled round). `IsTurnChromeToast` now filters only the coop YouTurn banner.
`SpaceCombatEvents` speaks player per-sector shield hits off `IShieldAbsorbsDamageHandler`
(fires per-shot, before the log's attack line — reads fine). Input-audit fact: mod input
categories are SCREEN-declared, so with no active screen a mode has NO live exploration keys.

**Phase 2 SHIPPED + live-verified 2026-07-12** (same worktree, uncommitted): `Exploration/
ShipPathInfo.cs` — `Preview(ship, dest)` reads the game's own `Navigation.ReachableTiles` (kept
fresh per-tick by `StarshipPathController`), quantizes the cursor tile via the game's own private
`GetNodeInMetagrid` (publicized — the exact call inside `FindPath`, so preview == commit verdict),
speaks cost/budget + ARRIVAL FACING + pass-through vs standable + the finishing-window consequence
("then must keep moving, N left" / "turn can end there") + dead-end
(`Game.Instance.StarshipPathController.IsCurrentShipInDeadEnd`; in dead-end the game swaps
ReachableTiles for 1-tile escape moves). `MoveSummary` groups `GetEndNodes()` (standable, the
sighted marker fan) by turn relative to heading. **Grid direction convention:** CustomGraphHelper
0=S 1=E 2=N 3=W 4=SE 5=NE 6=NW 7=SW; map via `{4,2,0,6,3,1,7,5}` onto Compass8 (0=N clockwise);
compass delta clockwise = starboard. `PathInfo.Preview` delegates for any acting StarshipEntity
(torpedoes included); Backspace arm/commit two-step works verbatim. Bare **Z** = `path.summary`
(ships: facing fan; surface: `PathInfo.MoveAreaSummary`, previously unwired); Semicolon vantage
has a starship branch (path verdict instead of cover/threat). SpaceCombatScreen now declares the
InGameScreen category flip (Exploration-first unfocused). NOT built on purpose: speed-mode control
(ability-driven), per-cell push-phase warnings.

**Phase 3 SHIPPED + core loop live-verified 2026-07-12**: `Combat/StarshipAim.cs` (ship
`HitPredictor` sibling) — refusal via game `CanTarget` (arc→`HasNoLosToTarget`→"out of firing
arc"), odds via `RuleStarshipCalculateHitChances` + `GetStarshipDamagePrediction` under
`DisableStatefulRandomContext` (**csproj ref added: StatefulRandom.dll** — the context type's
home); ship bursts share ONE hit chance, shots=`DamageInstances`, per-shot BurstHitChances is
surface-only. `HitPredictor.Describe` delegates (scanner Period cycle + O just work);
`AimRead.CursorReadout` ship branch reads the cursor ship directly (cell broadcast not the ship
source). SpaceCombatScreen Weapons (4 arc groups, arc word = game's own GroupLabel) + Ship
abilities stops via `ActionBarNodes.Slot` — arsenal variants ride the standard convert flyout
(`MechanicActionBarShipWeaponSlot.GetConvertedAbilityData`); NodeVtable.Announcements is an
IReadOnlyList field — REBUILD the list to insert parts. LIVE: refusals arc-vs-range correct
per weapon; adjacent → "80% to hit, 5% crit, 2 shots, shield damage 21 to 42, shields 35 of
35"; fired starboard battery → 140→109 shields (31∈21–42) + 9 hull, log narrated. Untested:
torpedoes (none owned), ammo weapons, Ram/Swing-Run cell-commit.

**Phase 4 SHIPPED compile-only 2026-07-12** (no live pass — user was testing on the shared
instance): Posts Tab stop (position-keyed post names via `GetPostStrings(index)`, officer +
`LocalizedTexts.Stats.GetText(PostData.AssociatedSkill)` + `CurrentSkillValue`, vacant/blocked
+rounds/penalty off the VM + publicized `m_Post`; post ability slots via the shared AddShipSlot);
`SpaceCombatEvents : IStarshipPostHandler` block/unblock cues (unblock gated on a tracked
announced-block set — BuffDidRemoved fires for every post buff).

**Planned-move preview (user request 2026-07-12, compile-only)**: ships have NO hover-sim (surface
UnitPathManager feeder dormant in space; HoloSim ship gate = correct parity) but DO have the
two-click planned-move hologram. `CommandDispatch.ShipMoveStep` rides it verbatim: arm press =
`TryCreateMoveCommandTB(showMovePrediction:true)` (ghost + path line + virtual position/FACING),
second press = SamePath → `TryRunVirtualMoveCommand` (publicized private); fresh arm first calls
`CancelMoveCommandLocal` (a lapsed window must never instant-commit); game's Esc cancel auto-wired
by SetVirtualMoveCommand. StarshipAim now uses `CanTargetFromDesiredPosition` (reads virtual pos +
ROTATION) + `GetDesiredPosition` for the prediction point → arcs/odds answer from the planned cell
with the arrival heading while a move is armed.

**Tile reachability on browse (user feedback 2026-07-12, compile-only)**: ground's additive
"unreachable" tile note rides UnitMovableAreaController — NEVER runs for starships; space twin =
`ShipPathInfo.TileReachabilityWord` in DescribeTile §2b (silent=stoppable, "pass-through only",
"unreachable"; suppressed while aiming; null when the fan isn't computed — no false claims).

**Arc awareness (user feedback 2026-07-12, compile-only)**: enemy browse tail (ProxyUnit, space
combat only) now reads `StarshipAim.OurArcsLine` ("{groups} bear on it" — per-weapon CanTarget,
panel group labels) + `ThreatArcsLine` ("it can fire on you: {arc words}"). **Hotkey fact:**
sighted space combat has NO slot hotkeys (space slot views never call SetKeyBinding — only
surface bar views do); mode binds only EndTurn(Space)+pause → Tab zones are at parity.

**Phase 5 SHIPPED compile-only 2026-07-12**: `ExitBattlePopupScreen` (layer 21 Exclusive) on
`SpaceCombatVM.ExitBattlePopupVM.IsActive`; XP line off the reactive block, scrap/items/cargo
rows (cargo card = origin label via `UICargoTexts.GetLabelByOrigin` + fill% + count; items via
ItemLabel + OpenItemTooltip made internal), buttons = the game's own DialogOk/Attune strings →
`ExitBattle(false/true)`; Attune gated on IsUpgradeAvailable. Needs a WON battle to live-verify.

**Phase 6 EXPLORED + partial ship 2026-07-12**: ScenarioSpaceCombat_1 census = BARE arena (0 map
objects/area effects/destructibles/unwalkable cells in 81×81; only ships; PartVantagePoints
present but no content; no TimeSurvival) → space scanner taxonomy DEFERRED until an encounter
has content. Shipped compile-only: end-turn nudge as a LIVE Value part ON the End-turn button
("N actions still available"); TimeSurvival rounds-left cue after each round advance
(CombatEvents, self-gated); popup OK/Attune buttons moved to their OWN Tab stop (user feedback);
HoloSim gated off for starship turns (no hover-sim mechanic for ships). **Phase 5 LIVE-VERIFIED**:
post-kill quicksave restored with popup ACTIVE; announce + XP line + button nav spoke right;
user exited via OK by hand. Branch REBASED onto main (clean; brought cargo/holo-sim/frontier).

**Open Qs (Phase 6+):** torpedo-turn control surface; vantage-point semantics (empty in
scenario 1); asteroid representation (nodes vs units vs fences); enemy planned-move arcs
readability; TimeSurvival bare-number Counter toast; live Ram/custom-path commit pass;
Phase 4 live pass (post lines, a real block event, post ability cast).

Related: [[rt-combat-plan]] [[rt-space-maps-research]] [[ui-paradigm-pivot]]
[[rt-action-bar-readout]] [[rt-visual-parity]] [[rt-dev-harness]]
