---
name: rt-destructible-targeting
description: "Shootable scenery (fire-trap escapes) = DestructibleEntity; dual pool membership, view-GameObject commit through the game's click handler, capped hit chance"
metadata: 
  node_type: memory
  type: project
  originSessionId: 3f4a57d2-c9c8-466b-9141-a2e751e27c7b
---

Shootable floor/wall objects (fire-trap escapes, fuel tanks, valves, destructible cover walls) are
`DestructibleEntity` (subclasses `CoverEntity`/`ThinCoverEntity`) — MapObjectEntities with `PartHealth`,
NO interaction parts. Engine facts (verified in decompiled source, 2026-07-12):

- **Dual pool membership**: `EntityPool<T>.HandleAdded` routes by `is T`, so destructibles are in BOTH
  `State.MapObjects` AND `State.DestructibleEntities`. `DestructibleEntity.FindByNode(node)` = tile lookup.
- **Targeting works through the normal ability click pipeline**: `ClickWithSelectedAbilityHandler.GetTarget`
  resolves ANY `MechanicEntityView` from the clicked GameObject; anchor-Unit targets are valid iff
  `entity.CanBeAttackedDirectly` (per-view serialized flag, default true). Out of combat "too far" is not a
  refusal — auto-approach then fire. So passing `entity.View.gameObject` into `OnClick` is all a mod needs.
- **Hit chance vs destructibles**: `RuleCalculateHitChances` forces non-burst attacks to the overkill border
  (effectively guaranteed); burst keeps per-pellet chances.
- **Sighted UI**: `OvertipDestructibleObjectVM` (name + health bar + hit-chance), gated `IsVisibleForPlayer`;
  overtip removed at `DestructionStage.Destroyed`.

Mod implementation (LIVE-TESTED by user 2026-07-12, commit c3ae56d — fire-trap escape worked): `ProxyDestructible` (own scanner category
"Destructible objects", label = name + "destructible" + HP, gated attackable+revealed),
`ScanItem.TargetEntity` (MechanicEntity, default = TargetUnit) widened through `AbilityTargeting.CommitAt`,
`CursorTarget.Inside` (any TargetEntity), tile cursor names the destructible, arm-announce hint counts them.
Related: [[rt-combat-plan]], [[rt-cover-los]], [[rt-scanner-explorer-audit]].
