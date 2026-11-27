---
name: rt-party-identity
description: "\"My party\" is SelectionCharacter.ActualGroup, never Player.Party or IsPlayerFaction; CapitalPartyMode hub areas collapse the party to the main character"
metadata: 
  node_type: memory
  type: reference
  originSessionId: 253636f4-4da1-4148-89d9-8aa5610674e5
  modified: 2026-07-30T17:54:10.608Z
---

Three different notions of "the party" in Rogue Trader, and only one is the right one to read:

- **`Game.Instance.SelectionCharacter.ActualGroup`** — the units the player actually commands **right now**.
  This is what the game's own party HUD binds to (`PartyVM.ActualGroup`). Built by
  `UIUtility.GetGroup(withRemote:false, withPet:true)` = `Player.Party`, minus transient members without
  `AllowOutOfCombatControl`, **filtered by `UIUtility.IsViewActiveUnit`** (on the surface, only units actually
  spawned in the loaded area), **plus pets**. Use this. `UnitFaction` (`RTAccess/Exploration/UnitFaction.cs`)
  wraps it — `Group()`, `Roster()`, `InParty()`, `Node()`, `Word()`.
- **`Player.Party` / `Player.PartyAndPets`** — the roster by `CompanionState.InParty`. Omits pets (`Party`),
  keeps members not present in this area, and is cached behind `m_CharacterListsValid`.
- **`unit.IsPlayerFaction`** — merely "friendly". Includes summons, scripted temporary allies, and ambient
  companion NPCs. **Never** a party test.

**`CapitalPartyMode`** (`Game.Instance.LoadedAreaState.Settings.CapitalPartyMode`, also `Player.CapitalPartyMode`)
is the trap. In a capital/hub area — verified live in `RogueTraderPalacePeaceful` — the game hands the player the
**main character alone**: `Player.AddCharacterToLists` rewrites `flag = IsMainCharacter`, so `Party` and
`PartyAndPets` both collapse to one, `BaseUnitEntity.IsDirectlyControllable` returns `this == MainCharacterEntity`,
and every companion drops into `RemoteCompanions` while several still **stand around the area as player-faction
NPCs**. A one-character HUD there is correct vanilla behaviour, not a bug — but an `IsPlayerFaction` scan test
announces those ambient NPCs as party members. That was the 2026-07-30 bug report; fixed by splitting
`ScanTaxonomy.UnitsAllies` out of `UnitsParty`.

Related: [[rt-hud-completeness-plan]], [[rt-scanner-consistency]], [[rt-visual-parity]], [[rt-reuse-game-methods]].
