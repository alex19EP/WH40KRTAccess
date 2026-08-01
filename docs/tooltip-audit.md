# RTAccess Tooltip Parity — Work List

_Audit date: 2026-08-01. Every item below survived an adversarial verification pass against the decompiled game views; the claims that did **not** survive are recorded in the appendix so they are not re-raised. Severity = how much information a blind player cannot obtain by any other route in the mod, not how visible the defect is._

---

## Status: all 37 closed (2026-08-01)

Every item below is implemented. Debug + Release compile clean (0 warnings), 111/111 tests pass. The
document is kept as the evidence trail — each entry still names the game view that justifies the wiring,
which is what a future audit needs in order to re-check it.

**Live-verified in-harness** (main menu → New Game → character generation; the only surface reachable
without loading a save): [H3](#h3--chargen-never-declares-the-live-skills-block) on both the custom and
pregen paths, [M10](#m10--background-phases-speak-the-raw-feature-description-not-the-chargen-specific-one),
[M11](#m11--career-phase-the-doctrinearchetype-explainer-is-unreachable),
[M12](#m12--summary-page-is-tooltip-dead-the-whole-character-review-card-has-no-route),
[M13](#m13--summary-review-ability-score-rows-carry-no-space),
[L4](#l4--chargen-attributes-row-never-speaks-the-per-rank-increment),
[L5](#l5--nextcomplete-gate-the-per-phase-why-you-cannot-proceed-hint-is-never-surfaced), and
[H1](#h1--statskillsave-cards-are-built-from-the-wrong-stattooltipdata-overload) (the attribute card now
carries its Bonus-value brick; the skill card correctly suppresses the Base-value row). No mod-log errors
during the run.

**Not live-verified** — everything else needs a loaded save (character sheet, inventory, loot, vendor,
ship, combat, log, dialogue) and is verified only by compilation and against the decompiled views.

**Two PLAUSIBLE items still want an in-harness look**, and could not get one for the same reason:
- [M7](#m7--item-browse-label-never-speaks-the-uncollectable-trash-badge--plausible) — the badge is
  implemented with the VM's own `CanUse`-then-`IsTrash` precedence and suppressed when the grade badge
  already said "trash". How much real cargo junk sets `Rarity = Trash` (making the badge redundant) is
  blueprint data; the cargo-bay gap it also closes holds regardless.
- [M14](#m14--level-up-career-list-omits-the-unit-background-block--plausible) — the background rows are
  built off `UnitProgressionVM.UnitBackgroundBlockVM`, which is created unconditionally. Whether the
  game's own level-up prefab variant binds the block's view is still unconfirmed.

One finding's fix generalised beyond its own site: [H4](#h4--dialogue-the-answer-conditions-link-is-never-offered)
is delivered by mirroring the view's answer-label branch ([M15](#m15--answer-label-ignores-the-views-dialogtypecanselect-branching)),
which emits the conditions anchor exactly where the game does — plus a `GlossaryLinks` change that labels
an ICON-only anchor with its target page's own title instead of the raw link id (the conditions and
exchange links both wrap a bare `<sprite>`).

Root cause 1's leverage landed as `GraphNodes.TextWithTooltip`, now the standard way to declare a readout
that carries a card; the per-screen `TextWithTooltip` / `StatLine` helpers delegate to it.

---

## Verdict

The tooltip surface is broadly at parity: the mod reaches the game's own `TooltipTemplate*` objects on the great majority of item cards, ability slots, stat rows and glossary drill-ins, and the `TooltipReader` / `TooltipViewScraper` render path is sound. What fails is almost never the rendering — it is the **wiring at the leaf**. The dominant failure mode is a node built with `GraphNodes.Text` / `GraphBuilder.AddLabel`, which produce a vtable with no `OnTooltip` at all, placed where the game's counterpart view calls `SetTooltip` / `SetGlossaryTooltip`; Space on that node answers "No tooltip" and the content has no second route. Three narrower modes account for the rest: a **wrong overload/template** silently substituted for the game's (the `StatTooltipData` overload bug, two hand-built `TooltipTemplateSimple`s standing in for real templates), **link mining over the wrong string** (`RawText` instead of the composed cue text), and **browse labels that under-mirror the card** (numbers printed as card text but never spoken).

37 work items, ordered for action. 6 are high severity — content that exists nowhere else in the mod. Five root causes cover 20 of the 37 sites; fix those first (see [Root causes](#root-causes)).

---

## High severity

Content a blind player cannot obtain anywhere in the mod today.

### H1 — Stat/skill/save cards are built from the wrong `StatTooltipData` overload

- **Loses:** every Characteristics, Skills and Save row on the character sheet gets a degraded card. Attributes lose the "Bonus value" brick (`attribute.Bonus = ModifiedValue / 10`, the 40K characteristic modifier) which appears **nowhere else** — `CharInfoAbilityScorePCView` paints only name + value + temporary diff on the card, so the browse label does not carry it either. Skills gain a "Base value" row the game deliberately suppresses. Saves lose the `÷10` `BaseStat.Bonus` row. All three read the generic "Total value" label instead of the per-kind one.
- **Mod site:** `RTAccess/Screens/CharacterInfoScreen.cs:442-457` — `var mv = unit.Stats.GetStatOptional(stat)` then `new TooltipTemplateStat(new StatTooltipData(mv))`. `StatsContainer.GetStatOptional(StatType)` is declared to return `ModifiableValue`, so C# binds the **base** ctor at compile time regardless of the runtime subtype. Blast radius includes the inventory window: `RTAccess/Screens/InventoryScreen.cs:104-109` reuses `BuildStatSection`.
- **Evidence:** `CharInfoStatVM.cs:129-150` dispatches on the concrete subtype (attribute / skill / savingThrow / else) before building the template; `StatTooltipData.cs:30-89` — the four ctors differ in `TotalValueLabel`, `BonusValue` and `Group`; `TooltipTemplateStat.cs:62-99` — `AddBonusValue` fires only when `BonusValue.HasValue`, and `AddStatBonusesGroup` suppresses the Base-value row only for `StatGroup.Skill`.
- **Fix:** mirror `CharInfoStatVM.OnStatUpdated` — switch on the concrete `ModifiableValue` subtype (`ModifiableValueAttributeStat` / `ModifiableValueSkill` / `ModifiableValueSavingThrow` / else) and call the matching `StatTooltipData` ctor before wrapping in `TooltipTemplateStat`.

### H2 — Soul-mark row never speaks the current rank points

- **Loses:** the character's current soul-mark points are unobtainable **anywhere** in the mod. The Space drill does not recover them: `TooltipReader` renders in `TooltipTemplateType.Info`, and `TooltipTemplateSoulMarkHeader.GetBodyInfo` omits the slider and the "Current value N" brick that only `GetBodyTooltip` emits — its per-tier blocks carry each tier's *threshold*, never the character's value. `ConvictionEvents` only voices individual shift deltas as they happen.
- **Mod site:** `RTAccess/Screens/CharacterInfoScreen.cs:324-332` — `GetSoulMarkInfo` discards `rankThresholds` / `maxValue` / `currentValue` into `out _`; the label is direction name + rank tier name only.
- **Evidence:** `CharInfoSoulMarkSectorView.cs:96-101` `SetupSector()` prints `m_Level.text = roman(CurrentLevel)` and `m_Value.text = CurrentRank + "/" + (next threshold or MaxRank)` on the card face. The faction row on the same sheet (`CharacterInfoScreen.cs:292-300`) already mirrors exactly this cur/next form — the two readouts are inconsistent.
- **Fix:** the out params are already returned; append `"{currentValue} / {nextThreshold}"` (or the Max string at the top tier) to `charinfo.soulmark_standing`, mirroring `FactionRow`.

### H3 — Chargen never declares the live skills block

- **Loses:** a blind player never learns their character's skills during creation — not the values, not which the career recommends, not which move when an attribute is raised, and no per-skill tooltip. 13 skills. This holds on **both** paths: the custom path loses the live consequence-of-a-spend panel, and the pregen path (the default — `CharGenPregenPhaseVM.cs:73` preselects a pregen) has no Attributes phase at all, so the summary block is the only skill surface that would exist.
- **Mod site:** `RTAccess/Screens/CharGen/AttributesPhaseContent.cs:21-37` (points header + 9 stat rows only) and `RTAccess/Screens/CharGen/SummaryPhaseContent.cs:52-93` (level + careers + the 9 attributes only).
- **Evidence:** `CharGenAttributesPhaseDetailedView.cs:39` binds `m_CharInfoSkillsBlockView` to `CharGenAttributesPhaseVM.CharInfoSkillsBlock` (VM created at `:61`, re-highlighted per selected attribute at `:249-256`, recommended marks at `:103`); `CharGenSummaryPhaseDetailedView.cs:43` binds the same block over `CharGenSummaryPhaseVM.CharInfoSkillsBlockVM` (`:84`). Rows are `CharGenCharInfoSkillView` (`SetValues(previewValue, previewValue, bonus)`) over `CharInfoSkillPCView.cs:128-131` `this.SetTooltip(ViewModel.Tooltip)`, built at `CharInfoStatVM.cs:149`.
- **Fix:** one shared builder over `CharInfoStatVM` rows (`Name.Value` + `PreviewStatValue`, `IsRecommended` as a state part, `OnTooltip = TooltipChooser.OpenTemplate(name, statVm.Tooltip.Value)`), declared as its own `BeginStop` on the attributes page (over `((CharGenAttributesPhaseVM)Phase).CharInfoSkillsBlock.Stats`) and again in the summary review (over `CharGenSummaryPhaseVM.CharInfoSkillsBlockVM.Stats`).

### H4 — Dialogue: the answer-conditions link is never offered

- **Loses:** for a requirement-bearing answer the player **can** pick, the requirement list is unreachable. `TooltipTemplateAnswerConditions` carries required Profit Factor (`ContextConditionHasPF`), required cargo, required items with counts, and the Requirement list with per-item satisfied colouring. A sighted player hovers the icon and reads "requires 15 Profit Factor / 100% Xenos cargo"; a blind player has no way to obtain the number or the item.
- **Mod site:** `RTAccess/UI/DialogNodes.cs:58-66` (`OpenAnswerTooltip`) + `:80-98` (`AnswerText`). The mined links come from `UIConstsExtensions.GetAnswerFormattedString`, which emits only the skill-check DC keys and `UIDialogExchangeLinkFormat` (`UIConstsExtensions.cs:21-53`) — never the conditions anchor.
- **Evidence:** `DialogAnswerBaseView.cs:126-131` — when `HasConditionsForTooltip`, the label is `string.Format(UIConfig.Instance.UIDialogConditionsLinkFormat, answer.AssetGuid, …)`; the sprite anchor is emitted in **both** the success and fail case. `:86` wires `SetLinkTooltip(…, RightMouseButton)`; `TooltipHelper.cs:501-502` dispatches `EntityLink.Type.DialogConditions -> TooltipTemplateAnswerConditions`. `AnswerVM.SetupTooltip` (`AnswerVM.cs:164-180`) routes `AnswerTooltip` to that template **only** when `HasConditionsForTooltip && !CanSelect()`.
- **Fix:** in `OpenAnswerTooltip`, when `vm.Answer.Value.HasConditionsForTooltip` and the body is not already that template, append `TooltipRef.To(<localized "Requirements">, () => new TooltipTemplateAnswerConditions(vm.Answer.Value))` to the mined links (new key in `assets/locale/enGB/ui.json`). Formatting `UIDialogConditionsLinkFormat` into the string handed to `GlossaryLinks.Gather` also works and needs no new string, since the standard dispatcher resolves `DialogConditions`.

### H5 — Book-event passages drop the mechanic prefix, so the skill-check outcome is never shown

- **Loses:** the roll result — the payoff of a book-event page — is neither spoken nor browsable. There is no fallback surface: `GameLogEventDialogHistory.cs:14-19` explicitly skips `DialogType.Book`, so book-event content never reaches `LogThreadService` and `LogReviewScreen` cannot show it either.
- **Mod site:** `RTAccess/Screens/BookEventScreen.cs:132-149` (`PassageLines` reads `cue.RawText` only) + `:56-63` (`SpeakLine`).
- **Evidence:** `BookEventCueView.cs:106-112` composes `SetText(GetMechanicText(m_DialogColors) + " " + GetNarrativeText(m_DialogColors))`. `CueVM.cs:103-111` `GetMechanicText` = SoulMarkShiftsText + SkillCheckText; `CueVM.cs:113-119` `GetNarrativeText` returns bare `RawText`. `UIUtility.SkillCheckText` (`UIUtility.cs:664-689`) has a dedicated `DialogType.Book` branch (`SkillCheckSuccessfulBE`/`SkillCheckFailedBE` colours), so book events demonstrably render it; `BookEventVM.SetCues` (`:133-143`) feeds each `CueVM` the `CueShowData`'s `SkillChecks`, so the data is on the very VM the mod holds.
- **Fix:** compose the passage the way the view does — prepend `cue.GetMechanicText(UIConfig.Instance.DialogColors)` to the first paragraph of each cue before the `'\n'` split, keeping the rich text in `PassageLine.Raw`. This also fixes the dead `SkillCheckLinks.Results` resolver at `BookEventScreen.cs:89` for free (see [L6](#l6--cue-link-mining-reads-rawtext-which-cannot-contain-the-runtime-anchors)), because the composed string is where the `SkillcheckResult` / `UnitStat` anchors live. Soul-mark shifts in the same prefix are separately voiced by `ConvictionEvents`.

### H6 — Ultimate-ability duration is never spoken

- **Loses:** for starship ultimates the buff length in rounds is printed on the card and exists nowhere in the mod. It is **not** recoverable from Space either: `TooltipTemplateShipAbility` carries only `blueprintAbility.CooldownRounds` and the blueprint description, while `UltimateDuration` is computed live (`StarshipCompanionsOnPostLogic.GetUltimateBuffDuration`: `StartingUltimateRounds + postSkill / SkillPointsToAddExtraUltimateRound`), changes with the seated officer's skill, and can never appear in blueprint text.
- **Mod site:** `RTAccess/Screens/ShipCustomizationScreen.cs:733-741` (`PostAbilityNode` label). Mod-wide grep for `HasDuration` / `UltimateDuration`: zero hits.
- **Evidence:** `PostAbilityDetailedBaseView.cs:157-163` — `SetupDuration()` activates `m_DurationBlock` on `ViewModel.HasDuration` and prints `UIStrings.Instance.ShipCustomization.PostAbilityDuration + ViewModel.UltimateDuration`, right beside the cooldown block the mod **does** speak (`:149-155`). `PostAbilityVM.cs:36` / `:61-69` / `:140`.
- **Fix:** in the label, when `pab.HasDuration`, add a segment mirroring the cooldown one at `:737-738`, built from `GameText.Or(() => UIStrings.Instance.ShipCustomization.PostAbilityDuration, …)` plus `pab.UltimateDuration`.

---

## Medium severity

Content that is missing from the control the player is standing on, but obtainable elsewhere in the mod at some cost (another window, another screen, a take-then-read round trip).

### Character sheet & inventory

#### M1 — The max-HP stat card is unreachable (3 sites, one root cause)

- **Loses:** the per-source breakdown of maximum wounds plus the HitPoints glossary write-up. The headline numbers are spoken everywhere, only the derivation is lost. `StatType.HitPoints` is in **none** of the three ordered stat lists the mod walks — not `CharacterInfoScreen.DefenseStats` (`:85-98`), not `CharInfoAbilityScoresBlockVM.AbilitiesOrdered`, not `SkillsOrdered` — so `StatEntry`'s template path never fires for HP, and a mod-wide grep finds exactly one `TooltipTemplateStat` construction and no `StatType.HitPoints`.
- **Mod sites:** `RTAccess/Screens/CharacterInfoScreen.cs:428-430` (wounds line, bare `GraphNodes.Text`); `RTAccess/Screens/InventoryScreen.cs:143-144` (character header, `ViewedCharacter.HeaderLine`; `BuildStatSection` is called with `withWounds:false` at `:106`/`:109`); `RTAccess/Screens/InGameScreen.cs:359-369` (HUD party roster row vtable — no `OnTooltip`).
- **Evidence:** `CharInfoHitPointsPCView.cs:52-58` `this.SetTooltip(ViewModel.Tooltip)`, built at `CharInfoHitPointsVM.cs:49-59` as `new TooltipTemplateStat(new StatTooltipData(GetStat(StatType.HitPoints)))`. Bound in all three places: `CharInfoNameAndPortraitPCView` (sheet), `InventoryBaseView.cs:66` → `CharInfoNameAndPortraitPCView.cs:197` (inventory), and `PartyCharacterPCView` → `UnitHealthPartProgressPCView` (`AddDisposable(this.SetTooltip(ViewModel.Tooltip))`, mouse path) where `UnitHealthPartVM : CharInfoHitPointsVM`.
- **Fix:** one construction, three call sites: `new TooltipTemplateStat(new StatTooltipData(unit.Stats.GetStat(StatType.HitPoints)))` via `TextWithTooltip` / a post-assigned `vt.OnTooltip`. The game uses the **base** `ModifiableValue` ctor here, so unlike [H1](#h1--statskillsave-cards-are-built-from-the-wrong-stattooltipdata-overload) this one is correct as written.

#### M2 — Profit Factor hand-builds a `TooltipTemplateSimple` instead of the game's template (2 sites)

- **Loses:** `TooltipTemplateProfitFactor` lists the total plus **every income and loss modifier by name** (colony projects, events, orders, chronicles, resource shortages, dialogue answers). On the sighted Factions page that list is printed on the panel itself (`CharInfoProfitFactorItemBaseView.SetupModifiers()` at `:98-125` instantiates one `TooltipBrickIconStatValueView` per modifier), so this is both a wrong-template and a label-mirror loss. The mod substitutes the static Description blurb and speaks only the total. At the respec window the same pool pays the respec cost, which is exactly when the breakdown matters.
- **Mod sites:** `RTAccess/Screens/CharacterInfoScreen.cs:277-287`; `RTAccess/Screens/RespecScreen.cs:96-106` (this second site is low on its own).
- **Evidence:** `CharInfoProfitFactorItemBaseView.cs:56` `Tooltip = new TooltipTemplateProfitFactor(ViewModel)`; `CharInfoProfitFactorItemPCView.cs:28-38` opens the same on the Information button; `CharInfoFactionsReputationVM.cs:25` appends a `ProfitFactorVM` as the last `ScreenItem`. Respec chain: `RespecWindowCommonView.cs:44` → `SystemMapSpaceResourcesPCView.cs:55` → `SystemMapSpaceProfitFactorView.cs:58` `SetTooltip(new TooltipTemplateProfitFactor(ViewModel.ProfitFactorVM))`.
- **Fix:** sheet — resolve `CharacterInfoVM.ComponentVMs[CharInfoComponentType.FactionsReputation]` → `CharInfoFactionsReputationVM.ScreenItems`, last entry. Respec — `vm.SystemMapSpaceResourcesVM.JournalOrderProfitFactorVM.ProfitFactorVM` is already on the screen's own VM. Both with the null fallback `VendorScreen.cs:171-176` already uses (that site reaches the template correctly — this is an inconsistency inside the mod).

#### M3 — Career write-ups carry no tooltip (2 sites)

- **Loses:** career prerequisites, description, keystone/ultimate abilities, stat and skill gains. Reachable in the mod **only** through `LevelUpScreen.cs:192-193`, and `LevelUpScreen` is reachable only via the header's "Level Up" button, which `CharacterInfoScreen.cs:159` gates on `unit.Progression.CanLevelUp` (grep: `:163` is the only `HandleOpenCharacterInfoPage` call site). So for a character with no pending rank, a career's write-up is unreachable. On the progression page the same text is one Escape away, but backing out with pending picks triggers the game's discard-confirm.
- **Mod sites:** `RTAccess/Screens/CharacterInfoScreen.cs:151-158` (`AllCareerPaths` tuples as bare `GraphNodes.Text`); `RTAccess/Screens/LevelUpScreen.cs:230-231` (`BeginStop("head")` → `ProgressHeader(cp)`) — the second is low on its own.
- **Evidence:** `CareerPathListItemCommonView.cs:167-176` `SetTooltip(ViewModel.CareerTooltip)` with `ShouldShowTooltip` defaulting true (only `CharGenCareerPathListItemView.cs:23` sets it false), over `CareerPathVM.cs:137` `CareerTooltip = new TooltipTemplateCareer(this, isScreenView:true)`. `CareerPathRoundProgressionCommonView.cs:88-90` binds that item at the centre of the progression wheel. **Note:** the `CharInfoSummaryVM` career view is dead code — `CharInfoSummaryVM.cs:59-64` never assigns `CareerPathVM.Value`, so do not cite it.
- **Fix:** build the sheet header rows from `UnitProgressionVM.AllCareerPaths` (the `CareerPathVM` list `LevelUpScreen.Vm()` already resolves) instead of bare `(Blueprint, Rank)` tuples, passing `tooltip: () => cp.CareerTooltip`; on the progression header, `OnTooltip = () => TooltipChooser.OpenTemplate(ProgressHeader(cp), cp.CareerTooltip)`.

#### M4 — Inventory defence readout: Resolve, Dodge reduction and Parry lose their glossary tooltips

- **Loses:** three of six defence rows answer "No tooltip" on Space while the sighted hover gives the glossary write-up. The mod took only what the VM exposes (`InventoryDollAdditionalStatsVM.cs:36-40` carries just Deflection/Absorption/Dodge templates) — the other three live on the **view**, the documented "some control state lives on game VIEWS, not VMs" case.
- **Mod site:** `RTAccess/Screens/InventoryScreen.cs:403-409` — three `StatLine` calls with no tooltip argument.
- **Evidence:** `InventoryDollAdditionalStatsPCView.cs:156-165` — `m_ResolveTooltip.SetGlossaryTooltip("Resolve", config)`, `m_DodgePenetrationTooltip.SetGlossaryTooltip("DodgeReduction", config)`, `m_ParryTooltip.SetGlossaryTooltip("Parry", config)`; `TooltipHelper.cs:167-170` resolves those to `new TooltipTemplateGlossary(key, …)`. All six targets are `OwlcatMultiButton`s registered in `m_StatsBlocks` (`:84`).
- **Fix:** `StatLine(() => Loc.T("stat.dodge_reduction", …), () => new TooltipTemplateGlossary("DodgeReduction"))` and likewise `"Resolve"` / `"Parry"` — the exact keys the view uses, no new locale keys. Tolerate a null `GlossaryEntry` (`UIUtility.GetGlossaryEntry` can miss; the sighted hover would show an empty card in that case too).

#### M5 — The Biography page's conviction bar is not declared

- **Loses:** the Puritan–Radical lean (a headline fact a sighted player reads at a glance from the cursor position) and the two conviction write-ups. Grep shows conviction appears in the mod only as `ConvictionEvents` shift announcements.
- **Mod site:** `RTAccess/Screens/CharacterInfoScreen.cs:313-334` — `BuildBiography` declares the three soul-mark rows and the shift/story lists, no conviction node.
- **Evidence:** `CharInfoAlignmentVM.cs:36` `ConvictionBar` (properly assigned) → `CharInfoAlignmentWheelPCView.cs:66` `m_ConvictionBar.Bind(…)`, and the AlignmentWheel component is on the Biography page (`CharInfoPagesPC.cs:78-86`). `ConvictionBarPCView.cs:8-14` binds `RadicalTooltip`/`PuritanTooltip` on four elements; `ConvictionBarVM.cs:29-42` `CurrentRelativeValue = clamp((Corruption + Hope − Faith) / 700, −1, 1)`.
- **Fix:** add a conviction row off the live `CharInfoAlignmentVM.ConvictionBar`: speak `CurrentRelativeValue` as a Puritan/Radical lean and hang the two write-ups as sections opening `PuritanTooltip` / `RadicalTooltip`. Do **not** surface `CurrentTooltip` — `ConvictionBarPCView` never binds it, so it would over-report. The game cross-wires `m_RightLabel`→`PuritanTooltip` and `m_LeftLabel`→`RadicalTooltip` (a game-side bug): mirror the **buttons'** pairing, not the labels'.

#### M6 — Weapons-block browse label omits the four combat numbers printed on the card

- **Loses:** damage, ammo, range and penetration — the four headline numbers a sighted player reads with **no hover at all**. Grep confirms the mod reads none of `WarhammerPenetration` / `WarhammerMaxAmmo` / `AttackOptimalRange` / `GetWeaponStats` anywhere; the only route is the weapon's item card on Space from a different Tab-stop (the doll's hand slot).
- **Mod site:** `RTAccess/Screens/InventoryScreen.cs:365-367` — group label is `prefix + weapon.Name` only.
- **Evidence:** `CharInfoWeaponSetPCView.BindViewImplementation` (`:64-79`) writes as plain TMP labels (not tooltips): `m_DamageLabel` = `GetWeaponStats(weapon).ResultDamage.MinValueBase|MaxValueBase`; `m_BulletsLabel` = `blueprint.WarhammerMaxAmmo` with `m_BulletsBlock.SetActive(WarhammerMaxAmmo > 0)`; `m_DistanceLabel` = `AttackOptimalRange|AttackRange`; `m_PenetrationLabel` = `blueprint.WarhammerPenetration`.
- **Fix:** extend the group label with the card's four values read the way the view reads them, suppressing ammo at 0 exactly as `m_BulletsBlock` does. Needs new `enGB/ui.json` entries (the composed line is mod-authored). **Call `GetWeaponStats` with the viewed unit** (the view uses `UIUtility.GetCurrentSelectedUnit()`) or the numbers drift. Note the game's block is per **set** showing the selected hand's numbers while the mod's rows are per **weapon** — per-weapon is the faithful mapping.

#### M7 — Item browse label never speaks the Uncollectable (trash) badge — **PLAUSIBLE**

- **Loses:** junk reads with no marker. It matters most in the cargo bays: `ItemNodes.CargoItem` (`:436-443`) gates Enter on `CanTransferToInventory`, which `CargoHelper.CanTransferFromCargo` refuses outright for trash — the row silently offers no action and no reason, while the sighted card carries the badge that explains it.
- **Mod site:** `RTAccess/UI/ItemNodes.cs:56-70` — `ItemLabel` reads notable / `!CanUse` / grade / count / charges / favourite, never `IsTrash` or `ItemStatus`.
- **Evidence:** `ItemSlotVM.cs:213-227` drives a three-state `ItemStatus` (Unsuitable when `!CanUse`, else Uncollectable when `IsTrash`, else None); `ItemSlotView.SetupStatus` (`:132-139`) renders it whenever `HasItem`. `IsTrash` = `CargoHelper.IsTrashItem` = `blueprint.GetType() == typeof(BlueprintItem) && !IsNotable` (`CargoHelper.cs:67-73`) — a **different predicate** from `Blueprint.Rarity == Trash`, which feeds `ItemGrade.Trash`; `BlueprintItem.m_Rarity` defaults to Common, so a junk item can be Uncollectable with grade Common.
- **Fix:** add a badge for `slot.IsTrash.Value` gated on `slot.CanUse.Value` (mirroring the VM's else-if precedence), new `item.uncollectable` key in `enGB/ui.json`, suppressed when the grade badge already said "trash".
- **Why PLAUSIBLE:** `ItemLabel` already emits "trash" for `ItemGrade == Trash`, so for junk authored with `Rarity=Trash` the badge is redundant; how much real cargo junk sets that rarity is blueprint data, unverified offline. The cargo-bay semantic gap (no-action row, no spoken reason) survives regardless — **validate in-harness before doing the label half.**

### Loot, vendor & item rows

#### M8 — Item rows that bypass `ItemNodes.OpenItemTooltip` (4 sites, one root cause)

- **Loses:** in a chest, corpse, the player chest, a one-slot device and the vendor purchase dialog, Space reads only the item's own card or nothing at all. The **compare** template is a rendered `TooltipTemplateItem(equippedItem, ItemEntity)` delta view, not merely the equipped item's card, so it is not reproducible from any other mod surface while the loot window is open — the player must take the item first and re-read it in the stash. The one-slot device's filled slot is the only item row in the loot family with **zero** tooltip. The purchase dialog is the last back-out point and every row there is mute.
- **Mod sites:** `RTAccess/UI/ItemNodes.cs:642` (`ItemRow.OnTooltip` → `OpenTemplate(…, OwnTemplate(slot))`; reached by `LootItem` `:84` / `LootScreen.cs:148` and `StashItem` `:153` / `PlayerChestScreen.cs:87`); `RTAccess/UI/ItemNodes.cs:120-137` (`InsertedItem` — bare vtable, used at `OneSlotLootScreen.cs:77`); `RTAccess/Screens/VendorBuyScreen.cs:72-73`, `:87-88`, `:117-132`.
- **Evidence:** `ItemSlotVM.cs:231-239` ctor defaults `compareEnabled:true`; `ItemSlotsGroupVM.cs:27` constructs the exact base type so the `GetType() == typeof(ItemSlotVM)` compare gate at `ItemSlotVM.cs:307` passes; `LootObjectVM.cs:49/53/57` builds every loot object (incl. PlayerChest) as `ItemSlotsGroupVM`; `LootSlotPCView.cs:26` → `ItemSlotPCView.cs:95` `SetTooltip(Tooltip, m_MainConfig, m_CompareConfig)`; `TooltipHelper.cs:96-104` shows `templates[0..n-2]` with the compare config and the last with the main config. One-slot: `InteractionSlotPartVM.cs:23` → `InteractionSlotPartView.cs:54-57` → the same chain. Vendor: `VendorTransitionWindowView.cs:56-70` binds `Slot`/`Slots` as `LootInventorySlotView` → `LootInventorySlotPCView.cs:19-21` → the same chain, and its context menu even keeps the Information verb.
- **Fix:** route all four through `ItemNodes.OpenItemTooltip(slot)` (already `internal`, already reused by display-only rows in `ExitBattlePopupScreen`). **One caveat:** `OpenItemTooltip` titles with `ItemLabel(slot, withFavorite: true)` and loot cards do not overlay the favourite star (`ItemNodes.cs:55`) — pass the favourite flag through so the loot title stays card-faithful.

#### M9 — Vendor locked-tier header announces the wrong unlock threshold

- **Loses:** for every tier deeper than the immediate next one, the spoken requirement is the **following** tier's number — simply wrong for the tier it is attached to, on a line a player uses to plan purchases.
- **Mod site:** `RTAccess/Screens/VendorScreen.cs:238-240` — feeds `lv.NextLevelReputationPoints` into `vendor.tier_locked` unconditionally (`enGB/ui.json:870` "Reputation level {level}, locked, unlocks at {points} reputation").
- **Evidence:** `VendorReputationLevelVM` ctor — when the tier **is** the immediate next level, `NextLevelReputationPoints = GetReputationPointsByLevel(faction, level)` (the tier's own threshold); **else** `ReputationPoints = GetReputationPointsByLevel(faction, level)` and `NextLevelReputationPoints = GetReputationPointsByLevel(faction, level + 1)`. `VendorTradePartVM:150-166` builds one `VendorLevelItemsVM` per band with `locked = (band > currentLevel)`, so several locked tiers coexist and the buggy branch is live.
- **Fix:** `points = ReputationHelper.GetReputationPointsByLevel(logic.VendorFactionType, level)`. `ReputationHelper` is already imported (`VendorScreen.cs:9`) and used by `RepLine`. Locale key unchanged.

### Chargen

#### M10 — Background phases speak the raw feature Description, not the chargen-specific one

- **Loses:** for features that ship a chargen-specific description the spoken line is the **wrong text**, and the mod contradicts itself — the same file passes `isCharGen:true` for the Space page (`SelectionPhaseContent.cs:51-52`), so browse label and Space page disagree about what the description is.
- **Mod site:** `RTAccess/Screens/CharGen/SelectionPhaseContent.cs:89-94` — `SelectedDescription` returns `it.Feature?.Description`, used as the browse label of the `desc` stop (`:77`).
- **Evidence:** `TooltipTemplateChargenBackground.cs:106-114` — `AddDescription` picks `component.CharGenDescription` when `m_IsCharGen && m_Feature.TryGetComponent<ReplaceDescriptionForCharGen>(out component)`, else `m_Feature.Description`, then runs it through `UIUtilityTexts.UpdateDescriptionWithUIProperties`. That is the template the panel binds (`CharGenBackgroundBasePhaseVM.cs:288` → `InfoVM.SetTemplate` at `:274`). **The component is real in shipped data:** a binary scan of `<Install>/Bundles/blueprints-pack.bbp` finds `ReplaceDescriptionForCharGen` on `ArbitratorOccupation_Feature`, `ExactionCastigatorsMastery_Feature` and `SubductorsMastery_Feature` — all in the ChargenOccupation family this content renders.
- **Fix:** mirror the game's `AddDescription` instead of re-deriving it: `Feature.TryGetComponent<ReplaceDescriptionForCharGen>(out var c) ? (string)c.CharGenDescription : Feature.Description`, then `UIUtilityTexts.UpdateDescriptionWithUIProperties(text, null)`.

#### M11 — Career phase: the doctrine/archetype explainer is unreachable

- **Loses:** entering the archetype phase — the moment the game explains what a doctrine/archetype **is** — the player gets a bare list of archetype names. Nothing on the page carries a Space at that point, and once an archetype is picked the same `OnTooltip` resolves to `TooltipTemplateCareer`, so the explainer is never reachable at any point in the mod's chargen.
- **Mod site:** `RTAccess/Screens/CharGen/CareerPhaseContent.cs:62` — `if (!string.IsNullOrEmpty(SelectedDescription(items)))` gates the only node that carries `OnTooltip` (`:72-73`).
- **Evidence:** `CharGenCareerPhaseVM.cs:95-103` — `GetTooltipTemplate()` returns `new TooltipTemplateCharGenDoctrinesDesc()` while `UnitProgressionVM.PreselectedCareer.Value == null`; pushed by `CharGenCareerPhaseDetailedPCView.cs:23` into `InfoVM` on bind. Content = `Tooltips.DoctrinesHeader` / `DoctrinesShortDesc` / `DoctrinesDescription`. The phase does not auto-select (`SetupDefaultItemsState` `:189-211`; `PhaseNextHint` seeded with `SelectDoctrineHint` at `:43`).
- **Fix:** drop the emptiness guard so the description stop always exists; `OnTooltip` unchanged — `CharGenAnnounce.GetActivePhaseTooltip()` already resolves to the doctrines template in the unselected state. Two caveats: the phase exists only on the **custom** path (`CharGenVM.UpdatePhases` gates it on `IsCustomCharacter`), and an always-emitted text node with an empty label needs a sensible spoken fallback or it reads as a blank stop. The identical guard at `SelectionPhaseContent.cs:63` is harmless only because background phases auto-select.

#### M12 — Summary page is tooltip-dead: the whole-character review card has no route

- **Loses:** the confirm-before-commit panel a sighted player reads on the final page — careers + the five background bricks (Homeworld / ImperialWorld / Occupation / MomentOfTriumph / DarkestHour) + granted abilities. The mod's review speaks level, career names and nine attribute scores; the background set and granted abilities are not on the page in any form.
- **Mod site:** `RTAccess/Screens/CharGen/SummaryPhaseContent.cs:27-93` — every node is `GraphNodes.Text` / `GraphNodes.Button` with no `tooltip:` argument (`GraphNodes.cs:49-53`, `:84-107`); the `WizardScreen` footer (`:124-129`) and `CharGenNodes.RoadmapEntry` (`:102-126`) carry none either.
- **Evidence:** `CharGenSummaryPhaseDetailedView.cs:44` `m_InfoView.Bind(ViewModel.InfoVM)`, fed by `CharGenSummaryPhaseVM.cs:177`/`:196` `new TooltipTemplateChargenUnitInformation(unit, value, m_UnitCareers)`; body at `TooltipTemplateChargenUnitInformation.cs:44-113`.
- **Fix:** add `OnTooltip` to a review node (e.g. `review:level` or a dedicated head) = `TooltipChooser.OpenTemplate(unit.CharacterName, CharGenAnnounce.GetActivePhaseTooltip())` — that helper already resolves to `CharGenSummaryPhaseVM.InfoVM.CurrentTooltip`, and going through the **template** (not a flattened string) keeps the career/ability rows drillable via `NestedTooltips`. Note: hanging it on the `chargen.review` context head is **not** available — `PushContext` creates an announcement scope, not a focusable node. Partly mitigated today: on the custom path each background choice is re-reachable via the roadmap, and on the pregen path `PregenPhaseContent.cs:35` already hangs the identical template on the selected pregen.

#### M13 — Summary review: ability-score rows carry no Space

- **Loses:** on the last chargen page the player hears "Willpower: 40" with no way to learn what the stat does or what feeds the number. The mod's own character sheet answers exactly this for the same rows, so the summary regresses against the mod's own standard.
- **Mod site:** `RTAccess/Screens/CharGen/SummaryPhaseContent.cs:85-86` (stat rows are bare `GraphNodes.Text`).
- **Evidence:** `CharGenSummaryPhaseDetailedView.cs:42` → `CharInfoLevelClassScoresPCView.cs:52` → `CharInfoAbilityScorePCView.cs:147-149` `this.SetTooltip(ViewModel.Tooltip)`, built at `CharInfoStatVM.cs:149`.
- **Fix:** reuse the `CharacterInfoScreen.StatEntry` recipe — build the row as a Text vtable and set `vt.OnTooltip = () => TooltipChooser.OpenTemplate(name, new TooltipTemplateStat(new StatTooltipData(mv)))`. **Drop the level-line half** of this (`:62-63`, `TooltipTemplateLevelExp`): in chargen it is level 1 / 0 exp and near-worthless. Priority is the **pregen path** — on the custom path the identical template is already reachable one page earlier via `CharGenNodes.StatRow` (`:151`), but `CharGenVM.UpdatePhases` gates the Attributes phase on `IsCustomCharacter`, so for a pregen character the stat card is reachable nowhere.

#### M14 — Level-up career list omits the unit background block — **PLAUSIBLE**

- **Loses:** on the page where advancement is chosen, the four origin picks (Homeworld / Occupation / Moment of Triumph / Darkest Hour) and the write-up of what each granted are not declared at all. The mod surfaces `TooltipTemplateChargenBackground` only inside chargen (`Screens/CharGen/SelectionPhaseContent.cs:51`), never afterwards.
- **Mod site:** `RTAccess/Screens/LevelUpScreen.cs:168-201` — `BuildCareerList` declares only the header text and the tier groups.
- **Evidence:** `CareerPathsListsCommonView.cs:43` `m_UnitBackgroundBlockCommonView.Or(null)?.Bind(ViewModel.UnitBackgroundBlockVM)`; `UnitBackgroundBlockCommonView.cs:90-93` four `SetTooltip` calls; `UnitBackgroundBlockVM.cs:67-70` builds a `TooltipTemplateChargenBackground` per entry, nulling MomentOfTriumph/DarkestHour for non-main-characters. The VM is created unconditionally at `UnitProgressionVM.cs:63`, a sibling of the `AllCareerPaths` the screen already walks.
- **Fix:** add a `BeginStop` with four rows off `vm.UnitBackgroundBlockVM`, labelled with each feature's `Name` (skipping nulls as the view does), `OnTooltip` opening the matching `*Tooltip.Value`.
- **Why PLAUSIBLE:** `m_UnitBackgroundBlockCommonView` is prefab-serialized and `.Or(null)`-guarded in both Initialize and Bind — **confirm in-harness that it is wired in the level-up prefab variant, not only the chargen one.** Also unverified: whether the picked homeworld/occupation features already appear in the sheet's Passive list (`UIUtilityUnit.CollectFeatures` filters `IFeatureSelection` and `HideInCharacterSheetAndLevelUp`, blueprint data).

### Dialogue & log

#### M15 — Answer label ignores the view's DialogType/CanSelect branching

- **Loses / leaks:** both directions of the label-mirror law are broken on a condition-bearing answer. (1) When `!CanSelect()`, the card deliberately shows only `DisplayText`; the mod speaks the decorated form, **leaking** DC percentages and the soul-mark requirement label the sighted card withholds. (2) Worse, when the failure *is* the soul-mark requirement, `GetAnswerText` returns decorations with **no** `DisplayText`, so the mod can announce "1. Requires Dogmatic rank 2" and never say what the answer actually says — while the card shows the text. (3) Epilogue choices are drawn unnumbered and undecorated; the mod prefixes a number that exists nowhere on screen.
- **Mod site:** `RTAccess/UI/DialogNodes.cs:80-98` — `AnswerText` calls `GetAnswerFormattedString` unconditionally.
- **Evidence:** `DialogAnswerBaseView.cs:118-141` is a three-way switch: `DialogType.Epilog` → bare `answer.DisplayText`; Common/StarSystemEvent with `hasConditionsForTooltip` → `string.Format(AnswerDialogueFormat, Index, conditionLink + (answer.CanSelect() ? GetAnswerText(answer) : answer.DisplayText))`; default → `GetAnswerFormattedString`. `UIConstsExtensions.cs:27-53` — `GetAnswerText` returns `text + … + text2 + ((!answer.IsSoulMarkRequirementSatisfied()) ? "" : answer.DisplayText)`. `BlueprintAnswer.cs:301-308` — `CanSelect() = IsSoulMarkRequirementSatisfied() && SelectConditions.Check() && IsRequirementsSatisfied()`.
- **Fix:** mirror the branch in `AnswerText` off `Game.Instance.DialogController.Dialog.Type`. All of it is the game's own composition — no hand-built text. Note the live epilogue path is `EpilogPCView.OnAnswersChanged` (`:28-32`, first answer's `DisplayText`, periods stripped, on a single Continue button), not `BookEventAnswerView`; the unnumbered/undecorated conclusion holds a fortiori.

#### M16 — Dialogue scrollback rows expose no inline links

- **Loses:** Space on any scrollback line answers "No tooltip" while a sighted player right-clicks the same highlighted term for its glossary page.
- **Mod site:** `RTAccess/Screens/DialogueScreen.cs:177-186` — history loop, `GraphNodes.Text` with no `OnTooltip`; `GraphNavigator.cs:330-338` then speaks `nav.no_tooltip`.
- **Evidence:** `DialogHistoryEntity.cs:31-37` — `Initialize(str)` sets the text then `m_Text.SetLinkTooltip(null, null, new TooltipConfig(RightMouseButton, …, isGlossary: true))`, unconditionally, one entity per History add (`DialogPCView.cs:139-145`, `SurfaceDialogBaseView.AddHistory`), over `value.GetText(m_DialogColors)` — the exact string the mod renders at `:181`.
- **Fix:** keep the raw string beside the stripped label: capture `raw = d.GetText(colors)` alongside `text`, and when `raw` contains `"<link"`, set `vt.OnTooltip = () => TooltipChooser.Open(captured, null, links: GlossaryLinks.Gather(raw))`. Pass **no** skill-check resolver — the view passes `(null, null)` and `TooltipHelper.cs:487-491` returns null for `SkillcheckResult` with a null list, so those links are dead in the game too and must stay dead here. Mitigated today: the same links are reachable mid-conversation via bare L (log review mirrors dialogue history and already wires `GlossaryLinks.Gather`), so this is consistency/ergonomics.

#### M17 — Log rows drop the shot number

- **Loses:** the per-shot ordinal badge the card prints beside the message. Space does not recover it: `TooltipTemplateCombatLogMessage` carries no shot ordinal, and `TooltipReader`'s `tooltip.shot` case (`:178-181`) only fires for nested-message/scatter bricks. With per-line positions suppressed (`LogReviewScreen.cs:104`, `positions: false`) nothing else supplies it, so a volley of identical outcomes reads byte-identically.
- **Mod site:** `RTAccess/Screens/LogReviewScreen.cs:135-147` — `LogLine` label is `Clean(msg.Message)` alone.
- **Evidence:** `CombatLogItemBaseView.cs:42-57` — `SetIcon()` writes `m_NumberText.text = ViewModel.ShotNumber.ToString()` with `alpha = (ShotNumber > 0) ? 1f : 0f`, a second visible TMP field, and swaps the prefix icon. `CombatLogItemVM.cs:18,32` takes it from `CombatLogMessage.ShotNumber`, a public getter on the object the mod already holds. `PerformAttackLogThread.cs:121-124`: `shotNumber = (AttacksCount > 1) ? AttackNumber : 0` with `AttackNumber = rule.BurstIndex + 1`; `RulebookPerformStarshipAttackLogThread.cs:257` likewise for voidship volleys.
- **Fix:** `msg.ShotNumber > 0 ? <localized "shot {n}"> + Clean(msg.Message) : Clean(msg.Message)`, string in `enGB/ui.json`.

### HUD & combat

#### M18 — Momentum gauge: the momentum tooltip has no route

- **Loses:** the heroic-act threshold, the acting unit's desperate-measure threshold, and the per-companion "has / has not spent their momentum ability" list. A blind player knows the current momentum number and whether a heroic act is live *right now*, but not how far off it is nor which companions still hold theirs.
- **Mod site:** `RTAccess/Accessibility/HudGauges.cs:50-59` (`AppendMomentum` — a flat `Speak`, no graph node); `RTAccess/Screens/InGameScreen.cs:442-499` (`BuildCombat` has no momentum row).
- **Evidence:** `SurfaceMomentumEntityPCView` binds `m_HintPlace.SetTooltip(ViewModel.Tooltip)` (mouse path). `MomentumEntityVM.UpdateMomentum` sets `Tooltip.Value = new TooltipTemplateMomentum(m_Current.Value)`. `TooltipTemplateMomentum.GetBody` emits `MomentumDescription`, a desperate-measure brick (per-unit `GetDesperateMeasureThreshold()`), a heroic-act brick, `AddFeaturesBricks` for the acting unit's momentum abilities, and — in `TooltipTemplateType.Info`, the mode `TooltipReader.cs:47` renders — `AddInfoMomentumPortrait`, one MomentumAvailable/NotAvailable line per party member.
- **Fix:** add a momentum row to `InGameScreen.BuildCombat` (beside `hud:cstatus` — `BuildCombat` is turn-based-only, matching `MomentumEntityVM`'s own combat gate) with `OnTooltip = () => TooltipChooser.OpenTemplate(label(), StaticPart()?.SurfaceHUDVM?.ActionBarVM?.SurfaceMomentumVM?.MomentumEntityVM?.Value?.Tooltip?.Value)`. Hand the game's live reactive to the chooser; do not re-derive thresholds from `MomentumRoot`.

#### M19 — Necron timer: the description tooltip is dropped and the header is mod-authored

- **Loses:** `UIStrings.Tooltips.NecronTimerDescription` — the game's only explanation of what the countdown reaches and what happens then — has no route (mod-wide grep for "Necron" hits only `HudGauges` and an `InputBindings` comment).
- **Mod site:** `RTAccess/Accessibility/HudGauges.cs:106-111` — bare `Loc.T("gauge.necron_timer", new { value })`, no node, no tooltip.
- **Evidence:** `NecronTimerView.cs` — `AddDisposable(this.SetTooltip(new TooltipTemplateSimple(tooltips.NecronTimerHeader, tooltips.NecronTimerDescription), config))` on the **base** view, so it is live on the mouse path.
- **Fix:** give the timer a node (or a Space verb on the gauge readout) with the identical template shape, and source the row label from `GameText.Or(() => UIStrings.Instance.Tooltips.NecronTimerHeader, "gauge.necron_timer")` — the codebase convention at `InGameScreen.cs:295-318`. The label half is drift-risk polish, not a law breach (the mod string is properly localized); the description half is the real gap.

### Ship

#### M20 — `ShipCustomizationScreen` status stop: six readouts with no tooltip (one root cause)

- **Loses:** Space on hull, scrap, experience, armour, shields and speed/inertia answers "No tooltip" while the sighted window gives an encyclopedia entry or a write-up on exactly those controls. Two of the six are unreachable anywhere in the mod: **armour plating** (`ArmorPlatingDescription` is used only by `ShipPCView.cs:167` and `TooltipTemplateSpaceUnitInspect.cs:191`, and the latter is not reachable — `NestedTooltips.Gather` only reads a top-level `Tooltip` member per brick, so it cannot reach the per-cell values inside `TooltipBrickShipInspectScheme`) and **ship experience** (`ShipExperienceDescription` is in no encyclopedia entry). The four glossary ones are obtainable indirectly through the mod's `EncyclopediaScreen`.
- **Mod sites:** `RTAccess/Screens/ShipCustomizationScreen.cs:838` (`st:hull`), `:848` (`st:scrap`), `:853` (`st:xp`), `:857` (`st:armor`), `:865` (`st:shields`), `:875` (`st:speed`). All bare `GraphNodes.Text`, and none post-assigns `OnTooltip` (grep of `OnTooltip` in the file: only `:285, :345, :642, :722, :764, :816`).
- **Evidence:** all cited views are inside the ship-customization window (`ShipCustomizationBaseView.cs:44/:93` serializes+binds `m_ShipStatsPCView`; `ShipCustomizationPCView.cs:11`; `ShipUpgradePCView.cs:13`). `ShipHealthAndRepairPCView.cs:55-56` → `SetGlossaryTooltip("HullIntegritySpace")` on both the HP bar and the hull text; `ShipInventoryStashView.cs:75` → `"ScrapSpace"`; `ShipPCView.cs:178-182` → every `m_Shields` button gets `"VoidshipShields"`; `ShipStatsPCView.cs:72-73` → `m_SpeedBlock` `"SpeedSpace"`, `m_InertiaBlock` `"ManoeuvrabilitySpace"`; `ShipPCView.cs:167`+`:183-187` → `HullTooltip = new TooltipTemplateSimple(UIStrings.Instance.ShipCustomization.ArmorPlating, …ArmorPlatingDescription)` on every hull plate; `ShipUpgradeBaseView.cs:87-88` → `ExperienceTooltip = new TooltipTemplateSimple(UIStrings.Instance.Tooltips.CurrentLevelExperience, UIStrings.Instance.ShipCustomization.ShipExperienceDescription)` bound to `m_ExperiencePanel`.
- **Fix:** post-mutate each Text vtable the way `:816` already does — `vt.OnTooltip = () => TooltipChooser.OpenTemplate(label(), new TooltipTemplateGlossary("HullIntegritySpace"))` etc.; read the two `UIStrings` fields for armour and experience, do not retype the prose. **Split `st:speed` into two nodes** — the game binds two distinct keys to two distinct blocks. Three cautions: (a) `ShipPCView.cs:166` assigns `ShieldsTooltip = TooltipTemplateGlossary("SpeedSpace")` but that field is **dead** (never bound) — the live shield key is `"VoidshipShields"`; (b) `TooltipTemplateGlossary` with an unresolvable key yields an empty template, so keep a label fallback; (c) do **not** attach the experience template to the Skills-tab XP header at `:459` — `ShipRankExpCounterPCView` binds no tooltip there at all, only `SetHint(CharacterSheet.AvailableRanksHint)`, so that would invent parity that does not exist. Unrelated but adjacent: `ShipPCView.UpdateArmor` (`:216-220`) has an upstream port/starboard swap — the mod reads the VM correctly and must not copy that bug.

#### M21 — Post-ability label never names the attuned ability

- **Loses:** the card tells a sighted player what the ability **becomes**; the mod's row stops at base name + locked/cooldown/attuned + the cost/prereq value. The name is only obtainable by pressing Space and then entering a section labelled with the generic "Attuned ability" string — a two-step for something printed on the card.
- **Mod site:** `RTAccess/Screens/ShipCustomizationScreen.cs:733-741` (label) and `:778-780` (generic drill-in caption). Grep for `AttuneAbility` in the mod: only a doc-comment mention.
- **Evidence:** `PostAbilityDetailedBaseView.cs:178-184` — `SetupAttuneBlock` activates `m_AttuneAbilityBlock` on `ViewModel.IsAttunable` and sets `m_AttuneName.text = ViewModel.AttuneAbility?.Name`, i.e. the upgraded ability's **name** is card text. `PostAbilityVM.cs:30` / `:141`. This detailed view is what `PostsBaseView` binds for the selected post.
- **Fix:** when `pab.IsAttunable`, append `pab.AttuneAbility?.Name` to the browse label, and label the `TooltipRef` at `:778-780` with that name instead of the bare `UIStrings.ShipCustomization.AttunedAbility`.

---

## Low severity

Polish and consistency. Real parity gaps, but the information is derivable, adjacent, or one keypress away.

### L1 — The experience tooltip is dropped on both level/XP lines (2 sites)

`TooltipTemplateLevelExp` lists current exp, next-level exp, exp-till-next and the CharacterLevel glossary entry. The mod already speaks the first two (`InventoryScreen.cs:150-154` reads the live `CharInfoExperienceVM`), so what is lost is the delta and the glossary text — and the psy-rating line right beside it (`:156-158`) *does* carry its tooltip, which is what keeps this on the list.
**Sites:** `RTAccess/Screens/CharacterInfoScreen.cs:149-150` (`charinfo.level`), `RTAccess/Screens/InventoryScreen.cs:153-155`.
**Evidence:** `CharInfoExperiencePCView.cs:66-78` `this.SetTooltip(new TooltipTemplateLevelExp(ViewModel))`; bound in the inventory window via `InventoryBaseView.cs:68` → `CharInfoLevelClassScoresPCView.cs:44`.
**Fix:** `StatLine(() => Loc.T("inv.xp", …), () => new TooltipTemplateLevelExp(exp))` — namespace already imported at `InventoryScreen.cs:11`; on the sheet, resolve the VM as `vm.LevelClassScoresVM?.Experience` exactly as `InventoryScreen` already does. No new locale keys.

### L2 — Companion story text is spoken with its inline glossary links dropped

Space on a story body answers "No tooltip". Every other body-text surface in the mod mines its links (`DialogueScreen.cs:264`, `BookEventScreen.cs:89`, `EncyclopediaScreen.cs:272/290/305`, `LogReviewScreen.cs:169`), so this is an inconsistency.
**Site:** `RTAccess/Screens/CharacterInfoScreen.cs:369-370` (single story) and `:381-382` (multi-story body).
**Evidence:** `CharInfoStoriesView.cs:34` — `m_BiographyText.SetLinkTooltip(null, null, new TooltipConfig(RightMouseButton, …, isGlossary: true))`, unconditional, on the live Biography component; the body is `BlueprintCompanionStory.Description`, a `LocalizedString`, so markup-intact.
**Fix:** `OnTooltip = () => TooltipChooser.Open(title, storyText, sections: null, links: GlossaryLinks.Gather(rawStoryText))` on both nodes, matching the `(null, null)` glossary-only contract — no skill-check resolver. **May be a no-op:** whether authored companion-story text actually carries `<link>` anchors is unverified from source.

### L3 — Rank selection group header carries no tooltip

On a committed or already-decided rank the player must expand the group and find the option announced "selected" to read what was taken; the header answers "No tooltip".
**Site:** `RTAccess/UI/CareerNodes.cs:29-37` (`SelectionGroup` returns a plain group vtable), used at `LevelUpScreen.cs:274`.
**Evidence:** `RankEntrySelectionItemCommonView.cs:102-125` — inside the `SelectedFeature` subscription, whenever `featureVM != null` it sets `m_TooltipHandle = m_MainButton.SetTooltip(featureVM.Tooltip, …)`. `RankEntrySelectionVM.cs:103-112` `Tooltip => SelectedFeature.Value?.TooltipTemplate() ?? m_Tooltip`.
**Fix:** `vt.OnTooltip = () => TooltipChooser.OpenTemplate(sel.GetHintText(), sel.Tooltip)`. Do **not** use `sel.HintTooltip` (the note at `CareerNodes.cs:123-134` is correct). Reuse the PetKeystone fallback from `CareerNodes.OptionTemplate` — `RankEntrySelectionFeatureVM.OverrideTooltip` nulls `Tooltip.Value` for that group (`:173-182`), so `sel.Tooltip` can be null.

### L4 — Chargen attributes row never speaks the per-rank increment

Nothing says a rank is worth +N, so the player cannot price a spend before making it; `TooltipTemplateStat` carries no per-rank increment.
**Site:** `RTAccess/UI/CharGenNodes.cs:151` (OnTooltip = `TooltipTemplateStat` only) and `:162-170` (`StatValueText`).
**Evidence:** `CharGenAttributesPhaseSelectorItemView.cs:58-63` formats `UIStrings.CharGen.SkillPointsContainerHint` with `ViewModel.ValuePerRank` and `value + " / " + m_FullRanks.Count`, bound as a hover hint at `:70` `m_RanksButton.SetHint(m_Hint)`. enGB string: "Add {0} points to characteristic: [{1}]".
**Fix:** put it on the **Space page**, not the browse label (the card shows filled rank pips; the +N/rank is hover-only detail) — pass it as a section built from the game's own string with `vm.ValuePerRank` and `vm.StatRanks.Value` / `CharGenAttributesPhaseVM.MaxRanksPerStat`. No new locale entry. Discounted because the increment is derivable in one reversible keypress (`CharGenAnnounce.OnStatAdvanced` already speaks the new value and the remaining pool).

### L5 — Next/Complete gate: the per-phase "why you cannot proceed" hint is never surfaced

A greyed Next reads only "disabled".
**Site:** `RTAccess/Screens/WizardScreen.cs:127-129` passes no `tooltip:`; `RTAccess/Accessibility/CharGenAnnounce.cs:112-137` never reads `PhaseNextHint`.
**Evidence:** `CharGenPCView.cs:89` subscribes `phase.PhaseNextHint` into `m_NextButtonHint`, `:96` `m_NextButton.SetHint(...)`. `GraphNodes.cs:100` deliberately leaves `OnTooltip` ungated by `enabled`, so a disabled button can carry one.
**Fix:** give `WizardScreen` a `NextTooltip()` virtual passed as `GraphNodes.Button(…, tooltip: NextTooltip)`; in `CharGenScreen` return a `TooltipTemplateSimple` over `CurrentPhaseVm()?.PhaseNextHint.Value` when non-empty. Do **not** wire `NotCompletedReasonTooltip` — it is a single generic string ("This character generation stage is not completed") shown only on the console path. Residual content is narrow: only two phases author a hint and the attributes one is already covered, so this is effectively just the career phase's "Select archetype to continue".

### L6 — Cue link mining reads `RawText`, which cannot contain the runtime anchors

`SkillCheckLinks.Results` is unreachable dead wiring at both call sites, and the UnitStat drill-in (the acting character's stat page for the stat that was rolled) is unavailable on Space.
**Sites:** `RTAccess/Screens/DialogueScreen.cs:264` and `RTAccess/Screens/BookEventScreen.cs:89`.
**Evidence:** `UIUtility.cs:685-687` — `SkillCheckText` mints both anchors at runtime (`<link="SkillcheckResult">…` and `<link="us:<StatType>:<ActingUnit.UniqueId>">…`); that string is prepended by `CueVM.GetCueTextInternal`/`GetMechanicText` and is what `DialogCuePCView.cs:76-80` / `BookEventCueView.cs:113` hand to `SetLinkTooltip`. `CueVM.RawText` (`:29-37`) is only `BlueprintCue.DisplayText`. `TooltipHelper.cs:509` dispatches `us:` → `TooltipTemplateStat(LinksHelper.GetStatData(...))`.
**Fix:** mine the composed string — `UIUtility.SkillCheckText(cue.SkillChecks, UIConfig.Instance.DialogColors) + cue.RawText` for dialogue, and the mechanic+narrative composition for book events (**the same edit as [H5](#h5--book-event-passages-drop-the-mechanic-prefix-so-the-skill-check-outcome-is-never-shown)** — do them together). Shallow in dialogue because `DialogueScreen.cs:256-259` already renders `TooltipTemplateSkillCheckResult` as the body, delivering the numbers.

### L7 — Space-combat post headers are bare labels

Space on a post header answers "No tooltip"; the post's function is never spoken during a battle (the header gives only name/officer/skill/blocked state).
**Site:** `RTAccess/Screens/SpaceCombatScreen.cs:155` — `b.AddLabel(ControlId.Structural("posts:head:" + p), () => PostLine(captured))`; `GraphBuilder.AddLabel` (`:233-234`) emits a vtable with only an announcement.
**Evidence:** `ShipPostPCView.BindViewImplementation` — `GetPostStrings(ViewModel.Index)` then `m_MainButton.SetTooltip(new TooltipTemplateSimple(postStrings.Title, postStrings.Description))`; the mod already consumes the sibling `.Title` at `SpaceCombatScreen.cs:278`.
**Fix:** swap the `AddLabel` for a `GraphNodes.Text` vtable with `vt.OnTooltip` opening that template. `Screens/ShipCustomizationScreen.cs:661-670` already has a `PostDescription` helper doing the same read — reuse it. Low because the same description is readable out of combat in the ship-customization window (`:642`), just not mid-battle.

### L8 — Action-bar slots never speak the attack-ability-group cooldown alert

In turn-based combat, arming a weapon attack makes every other attack-group slot **blink** for a sighted player; the mod's readout is unchanged (those slots are still `IsPossibleActive`, so the leading "unavailable" marker stays silent and `ToggleState` reports nothing).
**Site:** `RTAccess/UI/ActionBarNodes.cs:74` (live `ToggleState` announcement) and `:165-175` (reads only `IsSelected` + `MechanicActionBarSlot.IsActive()`). Mod-wide grep for `IsAlerted`: nothing.
**Evidence:** `ActionBarBaseSlotView` subscribes `ViewModel.IsAlerted` and starts/stops `PlayAttackAbilityGroupCooldownAlertAnimation`. `ActionBarSlotVM.HandleAbilityTargetSelectionStart` calls `TryTurnAlertOn()` when the ability is in `WeaponAttackAbilityGroup` and this slot is not the armed one; the alert rides the **target-selection** path, which is exactly what the mod drives via `OnMainClick` (`ActionBarNodes.cs:86`) — not a hover-only reactive, so the "read the SELECTION" law does not disqualify it.
**Fix:** add `vm.IsAlerted.Value` to the live `ToggleState` part with a new `slot.group_cooldown_alert` marker in `enGB/ui.json`, spoken alongside the targeting marker.

### L9 — Veil-thickness gauge: `TooltipTemplateVail` is unreachable

The mod speaks value/max plus a critical flag; the explanatory write-up (what veil thickness is, the state ladder, what breaking the veil means) and the exact critical threshold number are not spoken.
**Site:** `RTAccess/Accessibility/HudGauges.cs:73-82` (`AppendVeil` — flat `Speak`, no node).
**Evidence:** `VeilThicknessPCView` binds `m_TooltipArea.SetTooltip(ViewModel.Tooltip)` (mouse path); `VeilThicknessVM` keeps `public TooltipTemplateVail Tooltip = new TooltipTemplateVail()` as a long-lived field kept fresh by `Value.Subscribe(Tooltip.ChangeValue)`. `GetBody` emits VailHeader/VailFooter and, in `TooltipTemplateType.Info`, `VailCurrentState` / `VailStates` / `string.Format(BrokenVeil, critical, max)`.
**Fix:** `OnTooltip = () => TooltipChooser.OpenTemplate(label(), StaticPart()?.SurfaceHUDVM?.ActionBarVM?.VeilThickness?.Tooltip)` — pass the long-lived field straight through, **never construct a new `TooltipTemplateVail`**. **The row does not belong in `InGameScreen.BuildCombat`** (turn-based only): the veil persists outside combat and `HudGauges.cs:71-78` deliberately reports it there, so it needs a non-combat stop. Heavily discounted — the numeric content is largely already spoken and the VeilThickness glossary entry is in the mod's encyclopedia.

### L10 — Encyclopedia planet / astropath header / glossary-title rows are plain text — **PLAUSIBLE**

If any of those strings carries an inline glossary anchor it is followable for a sighted player and inert for a blind one. The astropath block is also internally inconsistent — the mod routes the **body** through `Prose()` (link-mined) but its location/date/sender/read header through bare text.
**Site:** `RTAccess/Screens/EncyclopediaScreen.cs:309-357` (`EmitPlanet` / `EmitAstropath`) and `:276-291` (`GlossaryEntryNode` mines Description only).
**Evidence:** `EncyclopediaPageBlockPlanetPCView.cs:100-108` `SetLinks()` calls `SetLinkTooltip(null, null, TooltipConfig(LeftMouseButton, …, isEncyclopedia: true))` on all seven fields; `EncyclopediaPageBlockAstropathBriefPCView.cs:69-75` on five; `EncyclopediaPageBlockGlossaryEntryPCView.cs:41-42` on title and description. With `isEncyclopedia: true`, `TooltipHelper.cs:376-392` makes left-click follow the link.
**Fix:** route the rows through the existing `Prose()` helper after composing the raw string; `Prose` no-ops when there are no links.
**Why PLAUSIBLE:** the game's wiring here is a **blanket defensive pattern**, not evidence of link content — every block view overrides `GetLinksTexts()` returning all its TMP fields (10 overrides, consumed by `EncyclopediaConsoleView.cs:212` for console link navigation) and Owlcat calls `SetLinkTooltip` on all of them uniformly. The specific fields are short names and labels. Nothing is demonstrably lost today — **validate in-harness before spending work.**

---

## Root causes

Five wirings account for 20 of the 37 sites. Fix these first; the per-screen items above then collapse to call-site edits.

1. **`UI/GraphNodes.cs` `Text` (`:49-53`) and `UI/Graph/GraphBuilder.cs` `AddLabel` (`:233-234`) produce vtables with no `OnTooltip`.** This is not a bug in the factory — it is the default — but it is the single shape behind ~15 of the findings ([M1](#m1--the-max-hp-stat-card-is-unreachable-3-sites-one-root-cause), [M5](#m5--the-biography-pages-conviction-bar-is-not-declared), [M12](#m12--summary-page-is-tooltip-dead-the-whole-character-review-card-has-no-route), [M13](#m13--summary-review-ability-score-rows-carry-no-space), [M16](#m16--dialogue-scrollback-rows-expose-no-inline-links), [M18](#m18--momentum-gauge-the-momentum-tooltip-has-no-route), [M19](#m19--necron-timer-the-description-tooltip-is-dropped-and-the-header-is-mod-authored), [M20](#m20--shipcustomizationscreen-status-stop-six-readouts-with-no-tooltip-one-root-cause), [L1](#l1--the-experience-tooltip-is-dropped-on-both-levelxp-lines-2-sites), [L2](#l2--companion-story-text-is-spoken-with-its-inline-glossary-links-dropped), [L7](#l7--space-combat-post-headers-are-bare-labels), [L9](#l9--veil-thickness-gauge-tooltiptemplatevail-is-unreachable), [L10](#l10--encyclopedia-planet--astropath-header--glossary-title-rows-are-plain-text--plausible)). **Leverage:** make the omission visible rather than silent — a `TextWithTooltip` / `StatLine`-style helper exists in several screens already; standardise on it, and consider a DEBUG-only audit pass that logs any focusable node whose game counterpart binds a tooltip. The review checklist for any new screen should be "the game's view called `SetTooltip`/`SetGlossaryTooltip` here — did we?".
2. **`UI/ItemNodes.cs` — `ItemRow` (`:642`) and `InsertedItem` (`:120-137`) do not route through `OpenItemTooltip`.** One edit in the factory plus the `VendorBuyScreen` call sites fixes loot, corpses, the player chest, one-slot devices and the purchase dialog — **4 sites, all of the compare-card loss** ([M8](#m8--item-rows-that-bypass-itemnodesopenitemtooltip-4-sites-one-root-cause)). `ItemLabel` (`:56-70`) is the same story for the Uncollectable badge ([M7](#m7--item-browse-label-never-speaks-the-uncollectable-trash-badge--plausible)).
3. **`CharacterInfoScreen.StatEntry` (`:442-457`) is the single `TooltipTemplateStat` construction in the mod**, and it picks the wrong `StatTooltipData` overload ([H1](#h1--statskillsave-cards-are-built-from-the-wrong-stattooltipdata-overload)). Because `InventoryScreen` reuses `BuildStatSection` (`:104-109`), one fix corrects both windows across ~25 stat rows. The same function's absence of `StatType.HitPoints` is what makes [M1](#m1--the-max-hp-stat-card-is-unreachable-3-sites-one-root-cause) a three-site gap. Fix the overload dispatch and add an HP path in the same pass.
4. **Chargen has no shared "read the block the game binds beside the phase" builder.** `CharInfoSkillsBlockVM` is bound on two phases and declared on neither ([H3](#h3--chargen-never-declares-the-live-skills-block)); `CharInfoStatVM` rows are re-hand-rolled per phase instead of shared ([M13](#m13--summary-review-ability-score-rows-carry-no-space)). One `CharGenNodes` builder over `CharInfoStatVM` (name + preview value + recommended + `Tooltip.Value`) serves attributes, summary and any future phase. **Bias every chargen fix toward the pregen path** — it is the default selection and it skips the Attributes phase entirely, so it loses content the custom path still reaches.
5. **Cue/answer text is re-derived from `RawText` / `GetAnswerFormattedString` instead of mirroring the view's composition.** This single habit produces the book-event blackout ([H5](#h5--book-event-passages-drop-the-mechanic-prefix-so-the-skill-check-outcome-is-never-shown)), the dead link resolver ([L6](#l6--cue-link-mining-reads-rawtext-which-cannot-contain-the-runtime-anchors)) and the answer-label leak ([M15](#m15--answer-label-ignores-the-views-dialogtypecanselect-branching)). The rule to enforce in `UI/DialogNodes.cs` and `BookEventScreen`: **compose the string the way the view composes it, then strip/mine** — never rebuild it from the blueprint.

---

## Appendix — refuted claims

Recorded so a later audit does not re-raise them.

| Claim | Why it does not stand |
|---|---|
| Soul-mark ability slots and their `TooltipTemplateSoulMarkFeature` cards are not declared | The reward ladder is **already delivered**. `CharacterInfoScreen.cs:333` opens `TooltipTemplateSoulMarkHeader`, and `TooltipReader` renders in `Info` mode, where `GetBodyInfo` loops every tier and emits `SoulMarkTooltipExtensions.GetFeatureBlock` — rank name + threshold, that tier's description, and a `TooltipBrickFeature` for the granted feature. Only residue is the Active/Inactive/Locked state strings, and the current tier is already in the row label. |
| Weapons-block weapon header has no tooltip although the game binds the item card on the hand slot | Duplicates a surface in the **same screen**: the Weapons block's hand slot and the doll's hand slot are both `EquipSlotVM` over the same `HandSlot`, and `ItemNodes.EquipSlot` (`:238`) already exposes `OwnTemplate(slot)` one Tab-stop away. Ergonomics, not parity. Edge case for the record: `BuildWeaponSets` lists only `IsEnabled` sets while `BuildWeapons` lists every non-empty one, so a disabled-but-armed set would fall through. |
| Inventory stash label drops the "augmentation can be overdriven" badge | The fact is already on Space: `TooltipTemplateItem` dispatches augments to `AugmentItemPart`, whose `GetBody` calls `UIUtilityItem.AddOverchargeAbility` (`:1435-1447`) whenever `OverdriveAbility != null`, and `InventoryItem`/`VendorStashItem`/`CargoItem` all route through `OpenItemTooltip`. Cosmetic polish at most. |
| Weapon attack-mode card is built with a caster the game's block does not pass | Cannot change the numbers. `UIUtilityItem.GetUIAbilityData` (`:330-333`) defaults a null caster to `itemEntity?.Owner ?? UIUtility.GetCurrentSelectedUnit()`, the freshly-created entity's Owner is null, and `GetCurrentSelectedUnit()` returns the same reactive the mod passes (`InventoryVM.cs:51`). A non-null caster only adds two conditional restriction blocks about the same unit. |
| `TooltipTemplateSkillCheckResult`'s glossary write-up is suppressed by the mod's `Array.Empty<string>()` | The link id is the bare tag `"SkillcheckResult"`, so the keyword loop calls `GetGlossaryEntry("SkillcheckResult")` → `ChapterList.GetPage(...)`, which has no page; the game passes identical keys. Delta is at most one empty lookup. |
| The cue's roll breakdown (stat total, DC, chance, pass/fail) is unreachable in dialogue | `DialogueScreen.cs:256-259` already builds `TooltipTemplateSkillCheckResult` as the Space body, yielding character, stat name and total, chance, roll and DC per check. Only the UnitStat drill-in is missing (kept as [L6](#l6--cue-link-mining-reads-rawtext-which-cannot-contain-the-runtime-anchors)). |
| The dialogue transcript is the only place glossary terms can still be followed | History is mirrored into the game log (`GameLogEventDialogHistory` → `DialogHistoryLogThread`, same `GetText` string) and `LogReviewScreen.cs:139-141` already wires `GlossaryLinks.Gather`; bare L opens over dialogue. Finding demoted high → medium ([M16](#m16--dialogue-scrollback-rows-expose-no-inline-links)). |
| Epilogue answers are drawn by `BookEventAnswerView`'s `DialogType.Epilog` branch | Not the live PC path — `EpilogPCView.OnAnswersChanged` (`:28-32`) renders only the first answer's `DisplayText` on a single Continue button. Conclusion survives via a different view. |
| `DialogNodes`' `BookNumberDecoration` regex normalizes a prefix the epilogue never had | `GetAnswerFormattedString` uses the decorative `AnswerDialogueBeFormat` only for `DialogType.Book`; the regex is inert on epilogues. |
| Every shot of a burst reads byte-identically and the ordinal is unobtainable | Rows usually differ (damage, hit/miss wording) and are chronological and adjacent, so the ordinal is derivable by counting. Demoted high → medium ([M17](#m17--log-rows-drop-the-shot-number)). |
| Initiative-tracker rows are inert, losing the unit-inspect card | Both halves fail. The cited binding is **console-only** (`SurfaceCombatUnitOrderVerticalConsoleView.ShowTooltip`, driven by `m_ConsoleNavigation`); the mouse twin binds only `SetHint(m_HintLabel)`, which the mod already speaks verbatim at `InGameScreen.cs:537-538`. And the content is richer elsewhere: `Exploration/Inspect.cs` (Y and ') raises the same handler and reads the **full** inspect template, already visibility-gated. Adding it would duplicate a richer route and create a `ForceRevealUnitInfo` site per enemy. |
| The action bar's two equipped-weapon cards are never emitted | Impact false. Damage and penetration are in the ability tooltip the mod already opens (`ActionBarNodes.cs:109` → `TooltipTemplateAbility`, whose `AddDamageInfo` emits BaseDamageText + Penetration and whose weapon branch prints CurrentAmmo/MaxAmmo); ammo is already spoken by `ActionBarNodes.Detail`; the item card itself is on the Inventory doll. Only non-combat chrome (weight/price/flavour) remains, one window away. |
| Ship-customization post row hand-assembles its tooltip body, dropping inline links | Wrong screen, and the proposed fix deletes information. The cited `ShipPostPCView` is the **space-combat HUD** button; the customization Posts tab's `PostEntityView` binds no tooltip at all, and `PostsBaseView.SetPostDescription` renders the description as always-visible panel text with no link handler — so no drill-in exists to lose. Worse, `PostNode`'s label emits the skill only when an officer is seated, so dropping the "Post skill: X" sentence would make a vacant post's required skill unobtainable. The real gap at the *other* screen is filed as [L7](#l7--space-combat-post-headers-are-bare-labels). |
