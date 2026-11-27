---
name: discord-feedback-triage-2026-08
description: "Verdicts on the 6-item Discord user feedback batch (2026-08-18) — what's a bug, gap, or existing feature, with fix anchors"
metadata: 
  node_type: memory
  type: project
  originSessionId: c2f50ca3-4d5b-4458-84b0-89ccb3e801ce
  modified: 2026-09-01T21:38:52.415Z
---

**STATUS: ALL SIX IMPLEMENTED 2026-08-18, compile-only — live verification pending** (checklist in
docs/feedback/2026-08-discord-triage.md, the canonical write-up). New pieces: Accessibility/UnitNames.cs
(per-area ordinal registry, wired through ~25 spoken-name sites), UnitReads named-trauma line,
BufferManager "Reviewed unit" buffer + window-aware resolver (REVISED 2026-09-02 after live feedback: the
"Target" buffer was DROPPED — it duplicated Selected unit on the player's own turn — so RT is back to WA's two
buffers; Reviewed unit = last-touched of tile-cursor unit (CursorTarget.Inside) vs scanner selection, stamped by
MapCursor.TouchedFrame / Scanner.SelectionFrame, the other filling in when the last-touched isn't on a unit),
PartyHotkeys.Select rework
(CanSelectUnit spoken guard + SelectUnit(single:true) + announce-on-success; InGameScreen.Select delegates),
PathInfo.FindApproachNode + Scanner.ApproachInCombat (I on non-clickable selection in TB = approach
plant/commit), ProxyMapObject stairs TB refusal + "leads up/down" from NodeLink ends.

Triage of the 6-item Discord feedback (2026-08-18), grounded by 4 research agents:

1. **Same-name enemy disambiguation ("Cultist 1/2")** — GAP. No numbering anywhere (WA has none either). All names = raw `CharacterName` (ProxyUnit.cs:33 + ~20 direct sites, worst: AimRead AoE lists repeat bare names). Fix anchor: mod-owned ordinal map keyed on `Entity.UniqueId`, assigned/retired via WorldModel Added/Removed + per-area ClearAll. `nav.position` "{index} of {count}" is cycle-slot, not identity.
2. **Injuries/traumas** — HALF-GAP + readout hole. Fresh/old wounds only on CharInfo defense section (UnitReads.cs:25-31 withTrauma:true, sole caller CharacterInfoScreen.cs:691). Traumas surfaced NOWHERE as a count; PartHealth has no trauma counter — read `unit.Buffs.Get(Root.WH.BlueprintTraumaRoot.Trauma)?.Rank ?? 0`. HOOKMAP.md:473 documents nonexistent `TraumaStacks` (likely why never written). NASTY: DealTraumasImpl removes both wound buffs when trauma lands → wound line goes silent.
   **Sighted-UI model RESOLVED (blueprint-pack decode, no live check needed):** TERMINOLOGY — game's "Wounds" = HP; the buffs are "Fresh Injury"/"Old Injury"/"Trauma" (our charinfo.fresh_wounds/old_wounds labels use the wrong word — pass through buff Blueprint names instead). Generic TraumaBuff m_Flags=13 = HiddenInUi (log carve-out `!IsTraumas` in RulebookCanApplyBuffLogThread exists BECAUSE of this); what sighted players see per trauma = ONE NAMED specific trauma buff (BrokenRibs/Concussion/CrippledArm L+R/CrippledLeg/NerveDamage, m_Flags=12 visible, icons, MaxRank 1) → these DO pass !Hidden and appear in buff lists incl. our UnitBuffer. Injury buffs fully visible icons; rank badge on icon only ≥2; tooltip shows "N/20 stacks" — so speaking counts is parity-OK in detail context. NO numeric counter anywhere in vanilla UI; max-HP reduction invisible (smaller denominator only — do NOT speak "max HP reduced by N"). Other sighted signals: "suffers Fresh Injury/Trauma" log line per stack (never merged; heal line only on last rank), Resolve breakdown itemizes penalty by buff name, augment-doll slot lock (our AugmentNodes covers), Momentum-loss line ×0.25 wound/×0.5 trauma. Fix shape: injuries line = fresh/old counts + NAMED traumas list, game's own buff names throughout.
3. **Switch character for buffer** — 3 REAL DEFECTS. (a) TB combat: `SetSelected(force:false)` no-ops (`CanSelectUnit` false) yet PartyHotkeys.cs:93 announces anyway = false feedback; game itself passes force:true. (b) Shift+A/D non-combat: engine reads physical Shift → multi-select TOGGLE branch, not switch (vanilla-inherited; WA uses Ctrl+1..6, no Shift). (c) Service windows write `SelectedUnitInUI` but BufferManager.cs:77-82 reads `SelectedUnit.Value` first, which is ~never null → fallback dead. Also RT swapped WA's review-cursor buffer (BufferManager ReviewUnit → Scanner.Reviewed) for "Target" — lost the aim-at-anyone channel; `Scanner.SelectedUnit()` (Scanner.cs:783) is ready to wire. WA prior art: TurnBasedBlocks spoken guard.
4. **Status effect details** — ALREADY SHIPPED: Alt+T on a buffer buff line renders the game's TooltipTemplateBuff via TooltipChooser (UnitBuffer.cs:71, BufferControls.cs:36-42). Discoverability problem only.
5. **Combat auto-walk** — GAP, tractable. PathInfo.PathNodes (PathInfo.cs:167) already reconstructs the ordered route from the game's reachable set; movable area ALREADY crosses WarhammerNodeLink ladders (LinkTraversalProvider set at PathfindingService.cs:452; combat runs in GameModeType.Default so IsCorrectMode passes). WA precedent: CombatMode.TryApproach (commit approach path for attack, spoken refusal when out of range; preview must pass updateActionsState:false). MoveAreaSummary exists, unwired.
6. **Ladder "0 distance" unusable in combat** — TWO findings. (a) Mod readout misleads: Geo.Distance is pure XZ, footprint circle collapses to 0 inside collider (ScanBounds.NearestOnCircleXZ), Geo.Vertical null under 1.5m so foot-authored ladders speak "0 tiles" with no height word; Reach classifies where ladder STANDS not where it LEADS. (b) VANILLA BUG: InteractionStairsPart click in TB burns 2 AP + once-per-round marker then MoveSelectedUnitsToPointRT throws InvalidOperationException (UnitCommandsRunner.cs:338). THREE ladder mechanisms: WarhammerNodeLink (pathfinding crosses automatically — just move), InteractionStairsPart (click-move, TB-broken), InteractionSkillCheckPart+TeleportOnSuccess (party teleport; Kiava Gamma type — separate A* components). Mod can't yet tell the player which kind they face. Mod should refuse stairs-interact in TB with speech before the game eats AP.

Related: [[rt-combat-plan]], [[rt-visibility-model]], [[rt-holo-sim]]
