---
name: rt-encyclopedia
description: "Encyclopedia/codex service-window a11y — two-panel tree+content screen (no new UI primitive), drive-paths, and the RT bestiary data-fill quirk; deferred whole-tree search"
metadata: 
  node_type: memory
  type: project
  originSessionId: 2f822d1e-f036-4c1c-bca4-a9d4bc347439
---

Encyclopedia service window (`ServiceWindowsType.Encyclopedia`) made accessible. Ported from WrathAccess's
`EncyclopediaScreen` recipe onto RT's (richer) VM/block set.

**Files:** `Screens/EncyclopediaScreen.cs`, `Accessibility/BestiaryReader.cs`, registered in `ScreenManager`
(layer 10), `assets/locale/enGB/ui.json` (+`ency.*`/`bestiary.*` keys). Landing-focus fix: same-page re-select
needs a frame budget since `EncyclopediaVM.HandleEncyclopediaPage` no-ops when target==current, so the
reference-swap detector never fires.

**Paradigm:** two-panel service window = a fully-expandable chapter→page→subpage nav tree + the selected page's
typed content blocks. Maps 1:1 onto existing primitives — **NO new UI primitive needed** (`GraphBuilder.BeginGroup`/
`IsExpanded` already gives the lazy tree; the `JournalScreen`/`CharacterInfoScreen` two-panel + detail-restop idiom
already existed). Navigate by driving the game's own path: `EncyclopediaNavigationElementVM.SelectPage()` for tree
jumps, `EventBus.RaiseEvent<IEncyclopediaHandler>(h => h.HandleEncyclopediaPage(page))` for in-page child-link drills
(both guarded — game indexes a top-level-chapters dict, so a hypothetical nested chapter throws in game code). Blocks:
text/class-progression/glossary-entry/book-event = prose (+ `GlossaryLinks.Gather` follow on Space); planet/astropath
= labeled fields via the game's own `UIStrings.EncyclopediaTexts.*`; image skipped (no alt-text); unit → `BestiaryReader`.

**Bestiary quirk (load-bearing):** built from `InspectUnitsHelper.GetInfo(BlueprintUnit, force:false)` →
`UnitInspectInfoByPart`, gated by null-checking the parts (force:false only materializes UNLOCKED parts → respects
Lore-Xenos knowledge reveal). But this is a Pathfinder-era holder RT only PARTIALLY fills: **Base** (9 attributes
[values are debug-string dumps — parse first line after `": "`], skills [`SkillsData` field names are mis-named
holdovers → use the RT-StatType mapping in `BestiaryReader.SkillMap`], race/size/level/speed/init) and **Abilities**
(names) are real; **Defence** carries ONLY HP+saves (armour/DR/immunities/regen are unfilled stubs); **Offence is
entirely EMPTY in RT** (attacks/BAB all 0/empty). Weapons/armour/deflection need a live `BaseUnitEntity` (out of reach
from a blueprint) — a genuine data limit, not a fixable gap. Build spawns/disposes a preview entity → cached per
(blueprint, `CurrentKnownPartsCount`).

**Deferred:** tree-wide type-ahead search. Verified in-harness the game has ZERO encyclopedia search (no input field in
PC OR console view — only a glossary-jump on controller), so a whole-encyclopedia jump-to-entry search would be a real
win the base game lacks. The `TypeAheadSearch` engine already exists and per-Tab-stop search ships free, but spanning
collapsed subtrees across Tab-stops needs new cross-scope navigator glue — left as a separate PR. See
[[rt-a11y-architecture-verdict]], [[wotr-access-prior-art]], [[rt-live-game-shared-harness]].
