---
name: rt-tooltip-reader-audit
description: "TooltipReader coverage — reflection fallback missed 58/88 brick VMs; ROOT FIX = TooltipViewScraper renders each template via the game's own TooltipEngine.GetBrickView factory + harvests TMP (noise filter needed); full-detail read on SPACE; typed-case fallback pattern; Tier C/D open"
metadata:
  node_type: memory
  type: project
  originSessionId: 9093d155-ca85-41c3-bf12-f4d62aa97684
---

Audited `RTAccess/Accessibility/TooltipReader.cs` against all **88** `TooltipBrick*VM` types (each brick's VM
+ its View = source of truth for what's visually rendered). Full work-list: `docs/tooltip-reader-audit.md`.

**Verdict: 58/88 bricks dropped visible text.** Root cause = the reflection fallback reads only 7 exact member
names (`Title/Header/Name/Text/Value/Label/Description`, case-sensitive, `string` / `ReactiveProperty<string>`
only). Owlcat bricks keep content in domain-named fields (`LeftLine`, `ArmorDeflection`, `Count`, `ProtocolName`),
numeric fields shown as text, nested VMs (`AbilityScoresBlock`), and lists — none touched.

**ROOT-CAUSE FIX — view-scraper (`Accessibility/TooltipViewScraper.cs`).** The game centralizes VM→View
instantiation+bind in `TooltipEngine.GetBrickView(TooltipBricksView config, vm)` (giant is-chain over ~85
bricks, pooled via `WidgetFactory`; `InfoBaseView.SetPart` + `TooltipBrickWidgetView` are its own callers). The
scraper renders each template through THAT factory and harvests active `TMP_Text` children (config via
`Resources.FindObjectsOfTypeAll<TooltipBricksView>()`; per-brick GetBrickView→scrape→DestroyBrickView
return-to-pool, try/finally, main-thread only). Closes ALL 58 gaps + future/DLC bricks by construction, no
per-brick knowledge; nested widget lists recurse for free. Wired as the PRIMARY `GetFull` path in
`TooltipReader.ReadFull` — the full-detail read is on **Space** (user rebound it off Ctrl+I). Scraping is too
heavy per-frame, so the cheap `GetTitle`/browse-label reader keeps curated typed cases (option-D split).

**NOISE FILTER (required):** the factory leaves prefab design-time placeholders in active-but-unbound TMP fields
(`+++`, `-//---`) — drop any harvested string with NO letter/digit (`char.IsLetterOrDigit`, language-agnostic);
also collapse whitespace runs from tag-stripping. The game's own `---` no-value marker inside a *labelled* field
(e.g. `Стоимость ---` = "Cost —") is KEPT — a sighted player sees it, so it's correct parity. Caveats: dynamic
reactive fields may be blank at scrape time (same as a VM read); loses "label: value" column pairing.

**Typed-case fallback pattern** (per-template FALLBACK when the registry isn't loaded + the browse-label reader):
explicit typed `case` in `BrickText`'s switch mirroring the View ([[rt-label-mirror-visual]]); consolidate via
base-type cases; **switch ORDER is load-bearing** — a derived brick's case must precede its base (`Buff` before
`Feature`; `TripleText` before `DoubleText`; combat-log payloads before `CombatLogBaseVM`), else shadowed. Two
`Tooltip.Bricks` namespaces + a `.CombatLog` sub-namespace overlap simple names → aliased `CL`/`UIB`/`AS` (do NOT
add bare usings for both). Game-static labels via `GameLogStrings.Instance`/`UIStrings.Instance` pass through
(game-localized); numeric-only readouts got new mod keys (`tooltip.shot/deviation/armor_*`/`dodge`). ~23
high-frequency bricks have typed cases (Feature/Buff/IconStatValue/AbilityScores/Double/Triple/ItemFooter/
EntityHeader/ArmorStats/WeaponSet/RankEntrySelection + all combat-log). Member names re-verified vs decompiled
before coding (agent suggestions are leads, not ground truth).

**STILL OPEN:** Tier C (~22 trivial single-field one-liners: IconAndName/ProtocolPet/CantUsePaper/RecommendPaper/
ShortLabel/LastUsedAbilityPaper/Attribute/FactionStatus/ResourceInfo/PortraitFeatures/PortraitAndName/Event/
Encumbrance/Slider/Rate/IconPattern/Widget-recurse/Momentum(s)/NonStack/BuffDOT/WeaponDOTInitialDamage/
IconAndTextWithCustomColors), Tier D niche (Planet*/Cargo/ProfitFactor/ShipInspectScheme/PetInfo — some need
reproducing game-state derivation at parent bricks), and WeaponSet full weapon-stats via `Weapon.GetWeaponStats()`.
30 bricks were already clean. Compile: `dotnet msbuild RTAccess/RTAccess.csproj -t:Compile` [[rt-compile-check]].
