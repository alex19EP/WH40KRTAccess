# Accessible level-up — ranked-ascending-lamport

Goal: surface the game's character level-up flow to a blind player as a mod-owned navigable screen —
career-path pick, the rank-entry selections (talents/abilities/attributes/skills), and commit. Follows the
established service-window mirror pattern (`LootScreen`, `TransitionScreen`, `CharacterInfoScreen`).

**Key architectural fact — RT is NOT WOTR here.** In Pathfinder/WOTR (WrathAccess), in-game level-up reuses the
CharGen wizard (`CharGenVM`), so WrathAccess reuses its `CharGenScreen`. **RT does not.** RT level-up is a *mode*
of the **Character Info window's Progression section** (`UnitProgressionVM` in `UnitProgressionMode.LevelUp`),
driven by a `LevelUpManager` + `CareerPathVM` + `RankEntrySelectionVM`. So this is a dedicated screen driving
RT's own progression VMs — do NOT try to resolve an "in-game CharGenVM" (there isn't one).

## How sighted level-up works (verified in decompiled source)

1. **Detect.** `PartUnitProgression.CanLevelUp => CharacterLevel < ExperienceLevel`
   (`Kingmaker.UnitLogic/PartUnitProgression.cs:75`); pending ranks = `ExperienceLevel − CharacterLevel`
   (see `CharInfoExperienceVM.NewRanksCount`). Per-path eligibility: `PartUnitProgression.CanUpgradePath(path)`
   (`:135`) enforces rank-remaining, prerequisites, and the "finish current path / one path per tier" rules.
2. **Entry.** Party badge → `PartyCharacterVM.LevelUp()` (`...VM.Party/PartyCharacterVM.cs:281`):
   `EventBus.RaiseEvent(INewServiceWindowUIHandler h => h.HandleOpenCharacterInfoPage(CharInfoPageType.LevelProgression, unit))`
   — i.e. it just opens the Character Info window on the LevelProgression page. `PartyCharacterVM.IsLevelUp`
   (`:255`) = `!IsInCombat && unit.Progression.CanLevelUp && !HasComponent<TransientPartyMemberFlag>()`.
   (The old `CharInfoExperienceVM.LevelUp()` raising `ILevelUpInitiateUIHandler.HandleLevelUpStart` is the
   *obsolete* WOTR wizard — ignore it.)
3. **The VM.** `CharacterInfoVM` builds the progression component:
   `ComponentVMs[CharInfoComponentType.Progression].Value = new UnitProgressionVM(unit, m_LevelUpManager, UnitProgressionMode.LevelUp)`
   (`CharacterInfoVM.cs:282`; `m_LevelUpManager` is a shared `ReactiveProperty<LevelUpManager>` at `:51`).
   `CharacterInfoVM.PageType.Value` reports the current page; `CanCloseWindow` (`:57`) is **false unless the
   manager is committed** (the game blocks closing mid-level-up).
4. **State machine.** `UnitProgressionVM.State : UnitProgressionWindowState` — two values:
   `CareerPathList` (grid of all paths) and `CareerPathProgression` (the chosen path's rank entries).
   Transition: `State = CurrentCareer.Value != null ? CareerPathProgression : CareerPathList`
   (`UnitProgressionVM.cs:354`). `UnitProgressionMode` = `CharGen` | `LevelUp`. **No respec mode here** (respec is
   a separate `PartUnitProgression.Respec()` / `FinishRespecGameCommand` mechanism).
5. **Pick a path (initiate).** `CareerPathVM.SetCareerPath()` → `UnitProgressionVM.TrySelectCareerPath` →
   `CreateLevelUpManager(path)` = `new LevelUpManager(unit, path, autoCommit:false)` stored in the shared prop;
   raises `ILevelUpManagerUIHandler.HandleCreateLevelUpManager`. The manager builds a throwaway `PreviewUnit`
   clone that ALL tentative picks apply to (real unit untouched until commit).
6. **Make selections.** `CareerPathVM.AvailableSelections` = the `RankEntrySelectionVM`s in the current
   level-up rank range (`CareerPathVM.cs:155`; all = `AllSelections` `:151`). Each `RankEntrySelectionVM`
   (`...Careers.RankEntry/RankEntrySelectionVM.cs`) wraps a `SelectionStateFeature`:
   - **Options** = `SelectionStateFeature.Items : ReadonlyList<FeatureSelectionItem>` (each →
     `BlueprintFeature`). **Commit a pick** = `RankEntrySelectionVM.SelectFeature(FeatureSelectionItem?)` (`:464`,
     → `SelectionStateFeature.Select` + `HandleUISelectionChanged`); undo = `ClearSelectedFeature()` (`:454`).
   - **State**: `EntryState : RankEntryState` (`NotSelectable, Committed, Selectable, NotValid, FirstSelectable,
     Selected, WaitPreviousToSelect`); helpers `SelectionMade` / `SelectionMadeAndValid` / `NeedToSelect` /
     `CanChangeSelection`. `FeatureGroup` (`Attribute, Skill, Talent, ActiveAbility, Keystone, …`) classifies it.
   - **Sub-types**: options present as `RankEntrySelectionFeatureVM` (feature/talent/ability) or
     `RankEntrySelectionStatVM` (when the feature is a `BlueprintStatAdvancement` — attributes/skills). The stat
     VM reads `StatDisplayName`, `StatIncreaseLabel` (`"+" + ValuePerRank`), `SummaryStatIncreaseLabel`
     (`"{current} > {preview}"`). **A stat pick is still a `SelectFeature` under the hood** — one mechanism.
7. **Commit.** When `CareerPathVM.CanCommit` (== `LevelUpManager.IsAllSelectionsMadeAndValid` — every selection
   `IsMade||!CanSelectAny` and `IsValid`), `CareerPathVM.Commit()` (`:546`, guards `CanCommitChanges()` `:630`) →
   `UnitProgressionVM.Commit()` (`:168`): `Game.Instance.GameCommandQueue.CommitLvlUp(LevelUpManager)` +
   `DestroyLevelUpManager()` + `RefreshData()`. The command (`CommitLevelUpGameCommand`) rebuilds a fresh manager,
   replays the picks, and mutates the **real unit**; it is **multiplayer-synchronized** (`IsSynchronized => true`).
8. **Cancel / rollback.** No "uncommit" — discard the manager (`UnitProgressionVM.DestroyLevelUpManager()` `:288`
   or `TryClose` `:228` / `ResetNewSelections` `:538`); the PreviewUnit is thrown away, real unit untouched.
   Closing/switching with unsaved picks pops `CloseWithMessage(UIStrings…LevelupDialogCloseProgression)`.

**Readable data for TTS** (all already-resolved `string`s): career `CareerPathVM.Name`/`.Description`/`IsRecommended`
(`CareerPathUIMetaData.RecommendedByOccupations`); feature `CharInfoFeatureVM.DisplayName`/`Description` +
`IsRecommended` (`RecommendedFeatures`); stat `RankEntrySelectionStatVM.StatDisplayName` + `+N` + `"cur > new"`.
Group headers via `UIStrings.Instance.CharacterSheet` (`GetFeatureGroupHint(FeatureGroup, canChange)`,
`GetFeatureGroupDescription`, `HeaderImprovement`, `KeystoneFeaturesHeader`). Stat names via
`LocalizedTexts.Instance.Stats.GetText(stat)`.

## Why it's mostly reuse

- Career paths are `CareerPathVM : SelectionGroupEntityVM` → **`ProxySelectionItem` works unchanged** (the chargen
  Career phase already uses it), read name/description/recommended; activate = `SetSelectedFromView`/`SetCareerPath`.
- The live-VM accessor is the standard service-window path: `ServiceWindowsVM.CharacterInfoVM.Value` →
  `ComponentVMs[CharInfoComponentType.Progression].Value as UnitProgressionVM` (resolve Surface **and** Space like
  `CharacterInfoScreen.ServiceWindows()`).
- `WizardScreen`/`CharGenScreen` give the multi-step shell (phase panel + Back/Next + focus re-home + page-turn);
  `ProxyStatStepper`/`ProxyActionButton`/`FlowSheet`/`ListContainer`/`TreeGroup`/`Loc.T`/tooltip-drill all exist.
- **The one genuinely new component** is the rank-entry selection UI (`ProxyRankSelection`) — RT has no analog yet
  (chargen's Career phase stops at picking a tier-1 archetype and never does rank picks).

## Screen design (`LevelUpScreen`)

- **Base / layer / modal:** `LevelUpScreen : Screen` (or `WizardScreen` if we adopt a phase strip — see below),
  `Exclusive = true`, layer **above** `CharacterInfoScreen` (10) so it suppresses the read-only sheet while
  leveling — e.g. **layer 26** (alongside `ExitLocationScreen`). `Key = "ctx.levelup"`.
- **Resolution:** `Vm()` = `ServiceWindows()?.CharacterInfoVM?.Value` →
  `ComponentVMs[CharInfoComponentType.Progression].Value as UnitProgressionVM` (Surface+Space).
- **IsActive:** `vm != null && vm.Mode == UnitProgressionMode.LevelUp` AND the progression page is showing
  (`CharacterInfoVM.PageType.Value == CharInfoPageType.LevelProgression`). This turns the accessible level-up on
  exactly when the game opens the progression page for a level-up-capable unit.
- **Two states mirroring `UnitProgressionVM.State`:**
  - **`CareerPathList`** → a `ListContainer` of `ProxySelectionItem` over `AllCareerPaths` filtered to upgradeable
    (`CareerPathVM.CanUpgrade` / `unit.Progression.CanUpgradePath`); read name + a live description `TextElement` +
    recommended; disabled options read their reason (one-path-per-tier). Activate = `SetCareerPath()`.
  - **`CareerPathProgression`** → a header line ("{path}, rank {cur} → {target}") + a guided list of
    `ProxyRankSelection` over `CareerPathVM.AvailableSelections`, then a footer **Commit** button.
- **Focus across rebuilds:** the manager reapplies to PreviewUnit on every pick (collections churn) — capture/
  restore focus by signature like `LootScreen`/`CharacterInfoScreen` (`ContentSig` of state + selection made-flags).
- **Back / Escape:** reuse the game's guarded close — `UnitProgressionVM.TryClose(onClose, onCancel)` (which
  raises the `LevelupDialogCloseProgression` confirm) surfaced through our `MessageBoxScreen`; if in
  `CareerPathProgression` with a live manager, Escape should offer discard rather than silently closing.

## Phases

- **Phase 0 — Entry & detection.**
  - Add a **"Level Up"** action to the read-only `CharacterInfoScreen` header (the "Character" list), focusable
    only when `unit.Progression.CanLevelUp`, activating the game's own entry — `PartyCharacterVM.LevelUp()` for the
    sheet unit, or raise `INewServiceWindowUIHandler.HandleOpenCharacterInfoPage(CharInfoPageType.LevelProgression,
    unit)` directly. (Mirrors WrathAccess's char-sheet "Level Up" button, `CharSheetBlocks.cs:37`.)
  - **Passive announce** (recommended default): when a unit gains a pending level-up, announce "{name} can level
    up." Hook a poll/subscription on progression/XP (candidate: `IUnitGainExperienceHandler` or poll `CanLevelUp`
    per party member with a debounce, like the existing landmark/chargen polls consolidated in `Main.OnUpdate`).
  - *(Optional, per user)* a **global hotkey** that reports who can level up and jumps to the first pending unit's
    level-up. Deferred unless requested.
- **Phase 1 — `LevelUpScreen` shell + career picker (`CareerPathList` state).** New screen, resolution, IsActive,
  Exclusive suppression of `CharacterInfoScreen`, career-path list via `ProxySelectionItem`, live description,
  disabled-reason readout, `SetCareerPath` initiate. Register in `ScreenManager.Initialize()`.
- **Phase 2 — Rank-entry selections (`CareerPathProgression` state) — core new work.** New **`ProxyRankSelection`**
  (a `TreeGroup`-style node per `RankEntrySelectionVM`): announces group header
  (`UIStrings.CharacterSheet.GetFeatureGroupHint(FeatureGroup)`), state (needs-select / selected / committed via
  `RankEntryState`+`SelectionMadeAndValid`), and the current pick; **expand** lists options from
  `SelectionStateFeature.Items` — each option a control reading `DisplayName` + recommended + **Space→description**
  drill-down; **activate** = `SelectFeature(item)`. Stat selections (`RankEntrySelectionStatVM`) read
  `StatDisplayName` + `+N` + `"cur > new"`; fixed auto-increases (no choice) render as a read-only "applied" line.
  Optional: a live PreviewUnit stat-delta readout.
- **Phase 3 — Commit / cancel.** Footer **Commit** `ProxyActionButton`, enabled = `CareerPathVM.CanCommit`;
  activate = `CareerPathVM.Commit()`, announce "Level up complete." When disabled, announce the outstanding
  selections (enumerate `AvailableSelections` where `!SelectionMadeAndValid`). Escape = guarded close
  (`TryClose`/discard-confirm through `MessageBoxScreen`); do not fight the game's `CanCloseWindow` gate.
- **Phase 4 — Starship level-up (deferred / optional).** Parallel `ShipProgressionVM : BaseUnitProgressionVM` in
  the **Ship Customization** window (`ShipSkillsVM`), same `LevelUpManager`/`SelectionState` machinery, different
  host + commit (`PartStarshipProgression.AddStarshipLevel` → still `GameCommandQueue.CommitLvlUp`). Entry gate
  `PartStarshipProgression.CanLevelUp`. Build as a sibling screen or a second host resolution once the character
  flow is proven in-game.

## New / reused proxies

- **Reuse** `ProxySelectionItem` for career paths (`CareerPathVM` is a `SelectionGroupEntityVM`).
- **New `ProxyRankSelection`** (`RTAccess/UI/Proxies/`) — the pending-selection node + its options. Main new UI.
- **Reuse** `ProxyActionButton` (Commit / Back), `TextElement` (headers, descriptions, points/outstanding lines),
  `ListContainer`/`TreeGroup`.

## Locale (`assets/locale/enGB/ui.json`)

New `levelup.*` keys: screen/title, career-picker prompt, rank-selection group prompts, "select one" / "selected"
/ "committed", outstanding-selections summary, commit label, "level up complete", discard-confirm. Prefer the
game's own strings via `GameText.Or(() => UIStrings.Instance.CharacterSheet.<X>, "levelup.<key>")` where one
exists; only add fresh keys for mod-authored phrasing. Follow `Loc.T("levelup.key", new { … })`.

## Testing (dev harness)

`/loadsave` a save (e.g. `VoidshipOfficersDeck`), grant XP via `/eval`
(`unit.Progression.AdvanceExperienceTo(...)` or add XP so `CanLevelUp` flips), then: open via the new
CharacterInfo "Level Up" action → pick a career path → walk each `ProxyRankSelection`, choose options → Commit →
assert `unit.Progression.CharacterLevel` advanced and the picks applied. Verify Escape mid-level-up pops the
discard-confirm (not a silent close). Do NOT drive the live game if another agent is (per shared-harness rule).

## Risks / notes

- **`CharacterInfoScreen` must yield.** LevelUpScreen is `Exclusive` and higher-layer; confirm the read-only sheet
  goes inert while leveling (and reappears after commit/close).
- **Drive VM methods, never reimplement** the commit (it's a synchronized `CommitLevelUpGameCommand`) — call
  `CareerPathVM.Commit()` / `SelectFeature()` (per the reuse-game-methods rule).
- **One-path-per-tier / finish-current-path** rules live in `CanUpgradePath` — surface the disabled reasons rather
  than hiding paths, so the player understands why a path is locked.
- **Coop gating** (`CharInfoComponentWithLevelUpVM.IsMainCharacter`/`CheckCoopControls`, `unit.CanEditCareer()`,
  `CareerPathVM.AlreadyInLevelupOther`) — respect but single-player is the target.
- **Async populate.** `AvailableSelections`/option lists can materialize a frame late (like chargen phases) — use
  the `Tick`/signature-rebuild pattern, don't assume populated on first build.
- The `RankEntrySelectionVM` grouping/filtering/favourites/`PointerItem`/`FeaturesToVisit` machinery is
  console-navigation convenience — bypass it; the authoritative option list is `SelectionStateFeature.Items`.

## Status — SHIPPED (compile-verified 0/0; NOT yet in-game)

All four phases implemented 2026-07-02 (compiles clean; in-game verification deferred — the game was running
under another agent, so Deploy was blocked and the live harness left untouched):

- **Phase 0** — `CharacterInfoScreen` gains a "Level Up" `ProxyActionButton` in the Character header, focusable
  only when `unit.Progression.CanLevelUp`; activates `EventBus.RaiseEvent<INewServiceWindowUIHandler>(
  HandleOpenCharacterInfoPage(LevelProgression, unit))`. Signature now includes `CanLevelUp`. Passive
  `RTAccess/Accessibility/LevelUpAnnouncer.cs` (wired in `Main.OnUpdate`) announces "{name} can level up" on a
  per-unit false→true transition (edge-detected via a last-known dict → no load/save-load burst; out-of-combat;
  passive). Locale `levelup.available`/`levelup.button`.
- **Phase 1** — `RTAccess/Screens/LevelUpScreen.cs` (`ctx.levelup`, layer 26, Exclusive). Resolves
  `ServiceWindowsVM.CharacterInfoVM.Value.ComponentVMs[CharInfoComponentType.Progression].Value as
  UnitProgressionVM`; `IsActive` = window==CharacterInfo && page==LevelProgression && `!IsCharGen`. Rebuilds on a
  signature of `(State, CurrentCareer, AvailableSelections.Count)`. Career picker lists `AllCareerPaths` filtered
  to `CanUpgradePath || IsInProgress`, each a `ProxyActionButton` (name + rank + recommended + career tooltip)
  whose activate = `CareerPathVM.SetCareerPath()`.
- **Phase 2** — `RTAccess/UI/Proxies/ProxyRankSelection.cs` (group `Container(Tree)` + `ProxyRankOption` leaf).
  Group reads `RankEntrySelectionVM.GetHintText()` + live state (not chosen / chosen: X / applied / choose earlier
  first); expands to options from `sel.FilteredGroupList.OfType<BaseRankEntryFeatureVM>()` (materialised via
  `sel.UpdateFeatures()`). Option reads name (StatDisplayName for stat picks) + selected + (stat "+N, cur > new"
  and/or recommended) + enabled; Enter = `opt.Select()` (raises the game's HandleUISelectionChanged, so
  everything reads live — no rebuild-on-pick); Space = `opt.TooltipTemplate()`. All state read live off VM
  reactives, so picking unlocks the next selection without focus churn.
- **Phase 3** — footer **Confirm level up** button gated on `CareerPathVM.CanCommit`, activate =
  `CareerPathVM.Commit()` + "Level up complete"; a live outstanding line ("N selections remaining" / "ready").
  Escape (`GetActions` Back): in progression → `UnitProgressionVM.SetCareerPath(null)` (the game's guarded
  discard-confirm via `MessageBoxScreen` when picks pending); in the career list → `HandleCloseAll`. A "Choose a
  different career path" button does the same `SetCareerPath(null)`. Locale block `levelup.*` in enGB ui.json.

**Files:** new `Screens/LevelUpScreen.cs`, `UI/Proxies/ProxyRankSelection.cs`, `Accessibility/LevelUpAnnouncer.cs`;
edited `Screens/CharacterInfoScreen.cs` (entry button + signature), `Screens/ScreenManager.cs` (register),
`Main.cs` (announcer tick), `assets/locale/enGB/ui.json` (`levelup.*`).

**Remaining to verify in-game** (drive via `/dev-game` when the harness is free): grant XP → passive announce →
open via the sheet button → pick career → walk `ProxyRankSelection` options (talents/abilities/attributes) → pick
→ Confirm → assert `CharacterLevel` advanced; Escape mid-pick pops the discard-confirm. Edge cases to watch: a
pick that unlocks a NEW same-rank selection (static list built once — would be missed until a state change);
`ShowUnavailableFeatures`-off hides not-selectable options; the pet-keystone commit path opens a name dialog
(premature "complete" announce). Phase 4 (starship `ShipProgressionVM`) still deferred.

## Scope decisions (defaults chosen while user away — adjust as needed)

- **v1 = character level-up only.** Starship (Phase 4) deferred to a follow-up.
- **Entry = in-context "Level Up" button on the Character Info sheet + passive availability announce.** Global
  hotkey deferred unless requested.
- **Presentation = flat guided list of pending selections** (WrathAccess UX philosophy), NOT a mirror of the
  game's 2-D rank/round grid.
