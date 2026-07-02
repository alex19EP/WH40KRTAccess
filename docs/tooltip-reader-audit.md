# TooltipReader coverage audit (2026-07-02)

Multi-agent audit of `RTAccess/Accessibility/TooltipReader.cs` against **all 88** game tooltip
brick VM types (`TooltipBrick*VM`). Each brick's VM + its View (what sighted players actually see)
was compared to the reader's extraction rules; every flagged gap was adversarially re-verified
(a gap counts only if the content is BOTH rendered to sighted players AND not captured by the reader).

**Verdict: 58 / 88 brick types drop visible text.** Root cause: the reflection fallback reads only
7 exact member names (`Title/Header/Name/Text/Value/Label/Description`, case-sensitive, string or
`ReactiveProperty<string>` only). It never reads differently-named string fields, numeric/int fields
shown as text, nested VMs, or list/collection members — which is where most bricks keep their content.

## Root-cause fix — Option A: scrape the game's own rendered views (2026-07-02, live-verified 2026-07-03)
Instead of re-deriving each brick's bind logic in the reader (the source of the drift), `TooltipViewScraper`
renders each template through the **game's own** view factory and harvests the visible text:
- The game centralizes VM→View instantiation+bind in `TooltipEngine.GetBrickView(TooltipBricksView config, vm)`
  (a big `is`-chain over ~85 brick types), pooled via `WidgetFactory`. `InfoBaseView.SetPart` and
  `TooltipBrickWidgetView` are the game's own reference callers.
- Scraper: obtain a `TooltipBricksView` (via `Resources.FindObjectsOfTypeAll`), then per brick
  `GetBrickView` → scrape active `TMP_Text` children in hierarchy order → `DestroyBrickView` (return to pool).
- **Closes all 58 gaps + future/DLC bricks by construction, no per-brick knowledge**; nested widget lists recurse
  through the same factory automatically. Wired as the **primary `GetFull` (Space) path**, with the curated
  typed-case walk kept as a per-template **fallback** (registry-not-loaded) and as the `GetTitle`/browse-label reader.
- **Noise filter (required, added after live run):** the factory leaves prefab design-time placeholders in
  active-but-unbound TMP fields (`+++`, `-//---`). Drop any harvested string with **no letter or digit**
  (`char.IsLetterOrDigit`, Unicode/language-agnostic) and collapse whitespace runs left by tag-stripping. The
  game's own `---` no-value marker *inside* a labelled field (e.g. `Стоимость ---` = "Cost —") is kept — a
  sighted player sees it, so passing it through is correct parity.
- Caveats: instantiates+binds a view per brick (pooled; fine on-demand, NOT for per-frame labels — hence labels
  stay on typed cases); dynamic reactive fields may be blank at scrape time (same limit as VM read); loses "label:
  value" column pairing (harvests both cells as separate segments).
- **LIVE-VERIFIED 2026-07-03** through the shipped `GetFull` on a loaded save (dev `/eval`): item tooltip →
  `Можно использовать. <name>. 10% груза Компоненты корабля` (weight + category from the `ItemFooter`
  Left/RightLine the old reflection dropped); ability tooltip → the full card — name, cost, AP, target, cooldown
  (Feature `AdditionalField`s + `DoubleText` rows), full description, and the `!` restriction line. Both clean,
  no placeholder noise. `TooltipViewScraper.Available` = true (registry found via `FindObjectsOfTypeAll`).

## Status (2026-07-02)
**Tier A + B SHIPPED** (compile-clean, `TooltipReader.cs` typed cases; not yet in-game verified). Every member
name was re-checked against the decompiled VM/View before writing. Notes on the two heavy bricks and edge cases:
- `TooltipBrickWeaponSetVM` — **name only** for now (`Weapon.Name`). The `CharInfoWeaponSetAbilityVM` list carries
  no name member and full damage/pen/range/RoF needs `Weapon.GetWeaponStats()`; that reproduction is deferred.
- `UIB.TooltipBrickRankEntrySelectionVM` — **label only** (`SelectedFeature.Value.DisplayName`); the level-up
  screen already reads rank selections directly, so this brick is rarely surfaced.
- `TooltipBrickPrerequisiteVM` — the "one from list" case is **already conveyed** by our `tooltip.requires_one`
  wording ("Requires one of:"), so no code change was needed there.
- `TooltipBrickAbilityScoresBlockVM` — the block title ("Character Stats") is View-only (`UIStrings`); we read the
  stat rows (the substance) and skip the title.
- New locale keys: `tooltip.armor_deflection/absorption`, `tooltip.dodge`, `tooltip.shot`, `tooltip.deviation`.

**Tier C + D + WeaponSet-full-stats: still open** (see below).

Fix pattern is uniform: add an explicit typed `case` in `BrickText`'s switch that mirrors the View
(pass game-localized content through, never re-translate). Consolidate where a base type covers
several derived bricks. **Switch ordering matters** — a derived brick's case must precede its base's.

---

## Tier A — high-frequency character / item / ability tooltips (fix first)

| Brick | Missed (visible) | Fix |
|---|---|---|
| `TooltipBrickFeatureVM` (partial; extend existing case l.111) | `AdditionalField1` (AP cost / ability-type), `AdditionalField2` (shots/ammo), `Acronym` | append the two additional string fields (+ acronym) to the current Name-only read |
| `TooltipBrickBuffVM` (partial) | `Duration`, `SourceName`, stack rank, DOT desc/avg | **new case BEFORE the Feature case** (subclass — else shadowed); Name + Duration.Value + SourceName + stacks |
| `TooltipBrickIconStatValueVM` (partial; extend l.110) | `AddValue`, `IconText` (+ prefer `ReactiveValue`) | append AddValue/IconText to the Name:Value read |
| `TooltipBrickAbilityScoresBlockVM` / `TooltipBrickAbilityScoresVM` / `TooltipBrickSkillsVM` (dropped) | whole stat/skill block (names + values), block titles | shared helper: walk `AbilityScoresBlock.Stats` (`CharInfoStatVM`: `Name.Value` + `StatValue.Value`); Block variant also emits its title |
| `TooltipBrickDoubleTextVM` + `TooltipBrickItemFooterVM` (derived) (dropped) | `LeftLine` / `RightLine` (weight, price, "usable by", key/value rows) | one base case `Join(LeftLine, RightLine)` covers both |
| `TooltipBrickTripleTextVM` (derived, dropped) | +`MiddleLine` | case BEFORE DoubleText: `Join3(Left,Middle,Right)` |
| `TooltipBrickTwoColumnsStatVM` (dropped) | `NameLeft/ValueLeft/NameRight/ValueRight` | pair each column, join both |
| `TooltipBrickEntityHeaderVM` (partial) | `MainTitle`, `LeftLabel`, `RightLabel`, `RightLabelClassification` (only `Title` caught) | read all five in order |
| `TooltipBrickPrerequisiteVM` (partial; extend existing case) | "one from list" OR-header | if `OneFromList && entries>1`, prepend `UIStrings…OneFromList.Text` |
| `TooltipBrickWeaponSetVM` (dropped) | weapon name, damage, penetration, range, ammo, RoF, ability list | mirror View: `Weapon.Name` + `Weapon.GetWeaponStats()` fields + abilities |
| `TooltipBrickArmorStatsVM` (dropped) | deflection / absorption / dodge values + labels (View swaps two slots) | read 3 value strings + UIStrings labels, mirror the slot swap |
| `TooltipBrickValueStatFormulaVM` (partial) | `Symbol` operator glyph | read `Value Symbol Name` in visual order (not `Name: Value`) |
| `TooltipBrickRankEntrySelectionVM` (dropped) | selection label / stat short-name (level-up tooltip) | deref `RankEntrySelectionVM`, mirror the view's label logic |

## Tier B — combat-log bricks (hit chance / damage prediction, relied on in combat)

| Brick | Missed | Fix |
|---|---|---|
| `TooltipBrickCombatLogBaseVM` (partial; abstract base) | `ResultValue` line | base case: `Name` + (`ResultValue` when `IsResultValue`) — covers several siblings |
| `TooltipBrickIconTextValueVM` (partial) | result line | covered by base case |
| `TooltipBrickChanceVM` (partial) | roll `%`, target `%`, result value | mirror `RollSlider.SetData` op/chance format |
| `TooltipBrickDamageRangeVM` (partial) | current / min / max / result | `Name` + `current (min-max)` + result |
| `TooltipBrickDamageNullifierVM` (dropped) | header, reason, `=result`, buff-item names, roll numbers | mirror View: static header + ReasonText + ResultValue + iterate ReasonBuffItems |
| `TooltipBrickMinimalAdmissibleDamageVM` (dropped) | `=value`, reason value, static labels | read `MinimalAdmissibleDamage` + `ReasonValue` |
| `TooltipBrickShotDirectionVM` / `…WithNameVM` (dropped) | deviation value/min/max (+ shot #, ray name) | format ints via new locale; WithName rebuilds header from game strings |
| `TooltipBrickTriggeredAutoVM` (dropped) | main line + contributing buff names | `TriggeredAutoText` + iterate `ReasonBuffItems[].Name` |
| `TooltipBrickNestedMessageVM` (partial) | shot ordinal badge | prepend `ShotNumber` when >0 |
| `TooltipBrickTextSignatureValueVM` (partial) | middle `SignatureText` | `Join(Text, SignatureText, Value)` |

## Tier C — simple single-field bricks (trivial, low-risk one-liners)

`TooltipBrickIconAndNameVM` (`.Line`) · `TooltipBrickProtocolPetVM` (`.ProtocolName`) ·
`TooltipBrickCantUsePaperVM` (`CantUseTitle`+`AbilityName`) · `TooltipBrickIconAndTextWithCustomColorsVM`
(`.StringValue`) · `TooltipBrickRecommendPaperVM` (`.FeatureName`) · `TooltipBrickShortLabelVM`
(`.StatShortName`) · `TooltipBrickLastUsedAbilityPaperVM` (`.AbilityName`) · `TooltipBrickAttributeVM`
(+`Acronym`) · `TooltipBrickFactionStatusVM` (+`Status`) · `TooltipBrickResourceInfoVM` (+`Count`) ·
`TooltipBrickPortraitFeaturesVM` (+`AvailableText`) · `TooltipBrickPortraitAndNameVM` (`Line`+subtype+difficulty) ·
`TooltipBrickEventVM` (`EventName`+`EventDescription`) · `TooltipBrickEncumbranceVM` (nested `LoadWeight`) ·
`TooltipBrickSliderVM` (value/max/ticks) · `TooltipBrickRateVM` (`RateName` + `Rate/MaxRate`) ·
`TooltipBrickIconPatternVM` (acronym/title/sub/tertiary label+value) · `TooltipBrickWidgetVM` (recurse child bricks) ·
`TooltipBrickMomentumPortraitVM` + `TooltipBricksMomentumPortraitsVM` (synthesize enabled/disabled label from `.Enable`) ·
`TooltipBrickNonStackVm` (header + `Entities[].Name`) · `TooltipBrickBuffDOTVM` / `TooltipBrickWeaponDOTInitialDamageVM` (`Damage` + UIStrings labels)

## Tier D — niche colony / planet / ship / economy systems (defer)

`TooltipBrickPlanetInfoVM` (+ its View-only children `PointsOfInterest` / `ResourceImage` / `Traits` — must
reproduce game-state derivation at the parent) · `TooltipBrickCargoCapacityVM` (`TotalFillValue%`) ·
`TooltipBrickProfitFactorVM` (nested `ProfitFactorVM.TotalValue`) · `TooltipBrickShipInspectSchemeVM`
(armour/shield per direction) · `TooltipBrickPetInfoVM` (movement/desc/abilities/stats block).

---

## Clean (30 bricks — no gap)

`TooltipBrickTitleVM`, `TooltipBrickTextVM`, plus 28 others already fully covered by an explicit case,
the reflection allowlist, or genuinely text-free (separators, spacers, bare pictures/icons → `na`).

## Notes for implementation
- Several fixes read static labels the View pulls from `UIStrings.Instance…` / `GameLogStrings.Instance…`
  — these are game-localized; pass them through, never re-translate. A few numeric-only readouts need a
  new mod locale key (e.g. `tooltip.rate_of`, `tooltip.shot_number`, `tooltip.shot_deviation`).
- Two namespaces: several bricks live in `Kingmaker.UI.MVVM.VM.Tooltip.Bricks` (not the `Kingmaker.Code.*`
  one the reader imports) — fully-qualify or add the using.
- **Verify every member name against the decompiled VM before compiling** — the agent-suggested names are
  a strong lead, not ground truth.
