---
name: rt-newgame-chargen
description: "New Game wizard + full character-generation flow — the VM paths, per-phase drive recipes, custom-name typing, and the Story-step DLC on/off toggles"
metadata: 
  node_type: memory
  type: project
  originSessionId: 2f822d1e-f036-4c1c-bca4-a9d4bc347439
---

New Game flow (parallel mod-owned UI, the [[ui-paradigm-pivot]] — supersedes the old console-nav chargen approach):
Main Menu → New Game → Story+Difficulty → Begin → all 10 chargen phases → `Complete()` → loaded into
`VoidshipOfficersDeck` (opening area, party of 1). Drive/verify via [[rt-dev-harness]].

**Shared shell:** `Screens/WizardScreen.cs` (phase Panel + Back/Next footer; rebuilds + re-homes focus + page-turn
on phase change; `BuildHeader` hook). Two screens ride it:
- `NewGameScreen` (`ctx.newgame`, layer 5) — `MainMenuVM.NewGameVM.Value`; null once `CharGenContextVM.CharGenVM`
  opens → hands off. Story = campaign picker; Difficulty reuses `SettingsEntityBuilder`. **RT gotcha:** only Story
  gates Next on `IsNextButtonAvailable`; Difficulty inherits the `false` default and never sets it, so Difficulty's
  Next must be UNCONDITIONALLY enabled.
- `CharGenScreen` (`ctx.chargen`, layer 15) — `MainMenuVM.CharGenContextVM.CharGenVM.Value` (namespace
  `Kingmaker.UI.MVVM.VM.CharGen`). Roadmap = `ProxyRoadmapEntry` per `PhasesCollection`, polled + rebuilt on
  phase-set change (picking custom adds Homeworld/Occupation/Career/… phases). Next =
  `PhasesSelectionGroupRadioVM.SelectNextValidEntity` / `Complete()` on last (gated on `CurrentPhaseIsCompleted`);
  Back = `SelectPrevValidEntity` / `Close()` on first.

**Per-phase content** = `Screens/CharGenPhaseContent.For(phase)` dispatch on `CharGenPhaseType` + `Screens/CharGen/*`:
- **Pregen** — pick a pregen or "Create Custom" (custom reveals the full phase set + auto-completes).
- **Background phases** (Homeworld/Occupation/SoulMark/Navigator/MomentOfTriumph/DarkestHour + children) all derive
  from **generic** `CharGenBackgroundBasePhaseVM<T>` (no shared non-generic accessor) → `SelectionPhaseContent` reads
  items off the public `SelectionGroup` field by **reflection** (`SelectionGroup.EntitiesCollection`, items are all
  `CharGenBackgroundBaseItemVM` = `DisplayName` + `Feature`); renders radio list + live `Feature.Description` (a
  `string`, not LocalizedString). They auto-complete with the default selection.
- **Career** — NOT a background phase; drives `UnitProgressionVM.AllCareerPaths` (`CareerPathVM` IS a
  `SelectionGroupEntityVM`). List tier-one archetypes (`CareerPath.Tier == CareerPathTier.One`); selecting one
  completes the phase (rank-1 ability picks not required to finish chargen).
- **Attributes** (`ProxyStatStepper`) — live `AvailablePointsLeft` + per-stat Left/Right (`AdvanceStat`/`RetreatStat`,
  async via `CharGenTryAdvanceStat`); completes when all points spent (`CanAdvance`/`CanRetreat`).
- **Ship / Summary** — selection + live name + **Edit name** (custom typing) + random-name action. **Both gate
  completion on a MANUALLY-set name** (`SetName(..,isManual:true)` / `CharGenNameVM.SetNameAndNotify`); a default name
  doesn't count. Summary also renders a read-only **character review** (level + final attributes via
  `unit.Stats.GetStat` + `CharInfoAbilityScoresBlockVM.AbilitiesOrdered` + `LocalizedTexts.Instance.Stats.GetText`;
  `LocalizedTexts` is in `Kingmaker.Blueprints.Root`, NOT `.Strings`). Careers line in the review comes back empty
  (Features.Enumerable filter) — minor.
- **Appearance** (`AppearancePhaseContent` + `ProxySequentialSelector`) — page tabs (Portrait/General/Hair/Tattoo/
  Implants/Voice via `PagesSelectionGroupRadioVM`) + the current page's components, rebuilt on page change (pages +
  components load async). **Voice** = named radio list (`CharGenVoiceItemVM.DisplayName`, selecting plays the sample
  — the high-value part). **Gender** = a cycler reading the doll (`DollState.Gender` → Male/Female). Other cyclers
  (face/body/skin/hair/…) derive from the open-generic `SequentialSelectorVM<T>` → `ProxySequentialSelector` reflects
  `Title`/`CurrentIndex`/`TotalCount`/`OnLeft`/`OnRight`/`IsAvailable`, announces "Title, N of M" (values are visual,
  no name). Portrait = visual note. Appearance auto-completes with valid defaults, so none of it blocks finishing.

**Custom name typing** = `Screens/NameEntryScreen.cs` (layer 32, Exclusive) resolves the current phase's change-name
`MessageBoxVM` (`CharGenNameVM.MessageBoxVM` for Summary, `CharGenShipPhaseVM.MessageBoxVM` for Ship). "Edit name"
opens the game's modal; the screen finds the active `TMP_InputField` (`FindObjectsByType<TMP_InputField>`, prefer the
one whose `.text` matches `InputText`) and hands it to `UI/TextEntry` for real typing; setting `field.text`
propagates to `InputText` via the game's binding; Apply = `OnAcceptPressed()`, Back = `OnDeclinePressed()`, Random =
`SetRandomName()`. `MainCharacter.Entity.CharacterName` is the API (`.Entity`, not `.Value`).

**Phase lists load a frame late** (async pregen/blueprint/level-up loads), so `CharGenPhaseContent` rebuilds content
when its `LiveCount()` changes (empty→populated). Proxies: `ProxySelectionItem` (generic `SelectionGroupEntityVM`),
`ProxyRoadmapEntry`, `ProxyStatStepper`, `ProxyDlcToggle`.

**Story-step DLC on/off toggles** (RT ships a single `StoryCampaign`, so one scenario radio + its DLC switches):
- **Scenario radio(s)** — `NewGamePhaseStoryVM.SelectionGroup.EntitiesCollection` (from
  `BlueprintRoot.NewGameSettings.StoryCampaigns`); each entry `NewGamePhaseStoryScenarioEntityVM`.
- **Per-campaign integral-DLC switches** — `scenario.IntegralDlcVms`
  (`List<NewGamePhaseStoryScenarioEntityIntegralDlcVM>`, from `campaign.AdditionalContentDlc` filtered by
  `!HideWhoNotBuyDlc && !HideDlcForAll`). RT DLCs (Void Shadows etc.) are AdditionalContentDlc, NOT extra
  StoryCampaigns. `BuildStory` renders `selected.IntegralDlcVms` as `ProxyDlcToggle`s.
- **Default-on lives in the VIEW, not the VM path.** `UpdateDlcSelectionStatus` (defaults every owned+available DLC
  to on) runs only from the scenario view's `OnChangeSelectedState → SelectMe()`. Our parallel UI doesn't drive that
  view, and the player DLC dict starts EMPTY with `GetAdditionalContentDlcStatus` defaulting to **false** → BuildStory
  must call `selected.SelectMe()` itself, else every switch (and the started game) reads OFF. Idempotent: a toggled-OFF
  DLC keeps its dict key (value false) so re-entry preserves the off choice.
- **Toggle read/write:** state = `BlueprintDlc.GetDlcSwitchOnOffState()` (== `IsEnabled` ==
  `Player.GetAdditionalContentDlcStatus`); write = `BlueprintDlc.SwitchDlcValue(bool)` →
  `Player.UpdateAdditionalContentDlcStatus` sets `m_StartNewGameAdditionalContentDlcStatus`, consumed at new-game start
  (via `SyncAdditionalContentDlcRewards`/`UsedDlcRewards`). **Switchable** (operable) = `IsPurchased && GetDownloadState()
  ∉ {NotLoaded, Loading}` (matches the game's `SwitchOnButtonActive`); otherwise disabled (not owned / coming soon /
  not installed / downloading).

**Robustness fixes (general):** `Main.OnUpdate` guards `Game.Keyboard` via `KeyboardReady()` try/catch — the getter NREs
mid-load when `UISettingsRoot.Instance` is null, which broke the whole per-frame tick that frame. `ScreenManager.Tick`
wraps `Current.OnUpdate()` in `Safe()` so a faulty phase build can't kill the tick.

**Deferred:** career rank-1 ability/talent picks — at chargen the archetype's rank entries aren't exposed
(`RankEntries`/`AllSelections` == 0) and aren't required to complete; the interactive rank/ability picker belongs to
the level-up subsystem ([[rt-levelup-plan]]), not chargen. Minor: portrait/skin-colour are visual-only; the Summary
review's careers line is empty.

**Eval gotcha:** Cyrillic in `--data` gets mangled — match by index/type, not `.Contains("<cyrillic>")`. (Other REPL
quirks — seeded usings excluding `Kingmaker`, `internal`-type access — in [[rt-dev-harness]].)
