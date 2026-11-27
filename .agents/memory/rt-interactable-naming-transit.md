---
name: rt-interactable-naming-transit
description: Which interactables the game itself leaves NAMELESS (barks) and the authored signal that an interactable moves the party between floors (TeleportOnSuccess / IAreaEnterPointReference)
metadata: 
  node_type: memory
  type: reference
  originSessionId: 3aae09b4-8748-4591-a4a1-12fe5d27ae1b
  modified: 2026-07-26T13:05:32.141Z
---

Two verified engine facts behind RT interactable readouts (decompiled `Code.dll`, 2026-07-26):

**1. Bark/examine volumes have no name, for anyone.** `OvertipMapObjectVM.UpdateObjectData()` switches over
Trap / Door / Loot / Ladder / Action / SkillCheck only — `InteractionBarkPart` matches none, so `Name.Value`
stays `string.Empty` and a sighted player sees a bare interaction icon. `InteractionBarkSettings` carries no
`DisplayName` field at all. So falling back to the Unity GameObject name ("CorruptedCogitatorBark",
"BloodTrace_01") speaks untranslated dev English the game never shows — a parity break, not a helpful extra.
Also: `OvertipMapObjectVM.CheckNeedOvertip` suppresses the overtip entirely for a skill check with
`!ShowOvertip` + no display names + no actions/barks.

**2. "This interactable moves the party" is authored data, not a heuristic.**
- `InteractionSkillCheckSettings.TeleportOnSuccess` / `TeleportOnFail` (`BlueprintAreaEnterPoint`) —
  `InteractionSkillCheckPart.OnInteract` calls `Game.Instance.Teleport(point, includeFollowers: true)`.
  The game's own area autoplayer uses exactly this as its transit test:
  `Kingmaker.QA.Clockwork.AreaTaskSelector` → `Settings.TeleportOnSuccess ? 8 : 18`.
- For lever/button interactions (`InteractionActionPart`, whose settings hold only an `ActionList`), the marker
  interface `Kingmaker.Blueprints.Area.IAreaEnterPointReference` is the equivalent signal — implemented by the
  `TeleportParty` GameAction (and by `AreaTransitionPart`, `InteractionSkillCheckPart`, `CapitalExit`,
  `BlueprintRandomEncounter`). Scan the action list, descending `Conditional.IfTrue/IfFalse`.

Athletics remains only a FALLBACK for climbs that move the unit by animation rather than a teleport.

The part types are all siblings — `InteractionActionPart`, `InteractionStairsPart`, `InteractionSkillCheckPart`,
`InteractionBarkPart`, `DisableTrapInteractionPart` each derive straight from `InteractionPart<TSettings>` — so
type-switch order carries no inheritance traps.

Related: [[rt-object-interaction]], [[rt-tile-explorer]], [[rt-scanner-consistency]], [[rt-visual-parity]].
