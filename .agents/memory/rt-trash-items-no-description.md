---
name: rt-trash-items-no-description
description: "Verified: RT trash/junk cargo items carry NO description and NO flavor text; their card is name+count+cargo%+origin only"
metadata: 
  node_type: memory
  type: reference
  originSessionId: 0fd67d3f-1f19-472d-8f20-bae08a892d0c
---

**Verified in-harness (2026-07-12, live save):** RT "trash" items — a plain base `BlueprintItem` (exact type, no weapon/armour/usable/note subclass) with `!IsNotable`, per `CargoHelper.IsTrashItem` — have **empty `FlavorText` AND empty `Description`** in the shipped data. Sampled 32/32 (2 inventory + 30 cargo), all identified, all empty; no `UniqueSourceDescription` either. (`ItemRarity.Trash` is a separate thing — a rarity/display grade — not the same predicate.)

Corrects an earlier wrong assumption that "junk cargo usually has flavor text" — it does **not** in Rogue Trader.

**What the mod speaks on Space for a trash item** (real `TooltipReader.GetFull`, Info mode): just **name + count (xN) + cargo volume % + origin category** (e.g. "…(x10). 7% cargo, Xenos Artefacts"). No description paragraph, because there is none. Control: a non-trash item (starship engine) rendered through the SAME path DOES speak its description — so the pipeline is correct; trash items simply have nothing to render. No "empty-Description trap": for trash both the mechanical Description and the flavor fallback are empty by design, and the mod degrades cleanly to card facts.

The game's own tooltip logic: `BaseItemPart.AddDescription` in **Info** mode emits both FlavorText (italic) + mechanical Description; **Tooltip** (hover) mode shows one via fallback ShortDescription→Description→FlavorText. Mod uses Info mode (`TooltipReader.GetFull`→`TooltipViewScraper.Read(..., TooltipTemplateType.Info)`). See [[rt-label-mirror-visual]], [[rt-loot-accessibility]], [[rt-tooltip-reader-audit]].
