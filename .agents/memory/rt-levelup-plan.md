---
name: rt-levelup-plan
description: "RT level-up is a MODE of the Character Info Progression section (UnitProgressionVM/LevelUpManager), NOT a CharGenVM like WOTR; drive-the-VM flow, tier-grouped career picker + per-rank outline tree, stat-readout gotcha; familiar/PetKeystone fix pass shipped (null tooltip, pick-injected stale lists, invisible pet-name commit box)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9093d155-ca85-41c3-bf12-f4d62aa97684
  modified: 2026-07-22T18:10:28.568Z
---

Plan to make RT character **level-up** accessible: `docs/plans/ranked-ascending-lamport.md`.

**Load-bearing verdict — RT ≠ WOTR here.** In WOTR ([[wotr-access-prior-art]]) in-game level-up reuses the
CharGen wizard (`CharGenVM`), so WrathAccess reuses its `CharGenScreen`. **RT does NOT.** RT level-up is a
*mode* of the **Character Info window's Progression section**: `UnitProgressionVM(unit, sharedLevelUpManager,
UnitProgressionMode.LevelUp)`, created by `CharacterInfoVM` for `CharInfoPageType.LevelProgression`. So the
accessible level-up is a dedicated mod-owned screen driving RT's own progression VMs — do NOT resolve an
"in-game CharGenVM" (there isn't one). Chargen's Career phase ([[rt-newgame-chargen]]) only picks a tier-1
archetype and never does rank picks, so the rank-entry selection UI is entirely new.

**Verified flow (drive-the-VMs):** detect `unit.Progression.CanLevelUp` (CharacterLevel<ExperienceLevel) →
entry `PartyCharacterVM.LevelUp()` raises `INewServiceWindowUIHandler.HandleOpenCharacterInfoPage(LevelProgression,
unit)` → resolve `ServiceWindowsVM.CharacterInfoVM.Value.ComponentVMs[CharInfoComponentType.Progression].Value as
UnitProgressionVM` → state `UnitProgressionWindowState.CareerPathList`→`CareerPathProgression`; pick path
`CareerPathVM.SetCareerPath()` (creates `LevelUpManager` w/ throwaway `PreviewUnit` clone); selections via
`CareerPathVM.AvailableSelections` (`RankEntrySelectionVM`, options=`SelectionStateFeature.Items`, pick=
`SelectFeature(item)`; stat picks=`RankEntrySelectionStatVM`); commit when `CareerPathVM.CanCommit`
(==`LevelUpManager.IsAllSelectionsMadeAndValid`) via `CareerPathVM.Commit()`→`GameCommandQueue.CommitLvlUp`
(synchronized `CommitLevelUpGameCommand`, mutates real unit). Cancel=discard manager (`DestroyLevelUpManager`);
game blocks close mid-levelup (`CharacterInfoVM.CanCloseWindow` false unless committed; `TryClose` pops confirm).

**`AvailableSelections` gotcha:** = AllSelections∩GetCurrentLevelupRange aggregates EVERY pending-input selection
across the whole rank range (not per-step); a rank granting only auto features contributes 0 selections (so
"2 improvements" can show 1 choice panel — committing that one pick levels both ranks).

**Career picker (stage 1) — GROUPED BY TIER** (root TreeGroup with a `Tier N` TreeGroup per `CareerPath.Tier`,
tiers ordered; the game lays tiers out spatially with dependency lines, so a per-tier tree is the a11y
equivalent). Career list shows ALL paths — upgradeable first, locked ones focusable-but-disabled. Each archetype
= `ProxyActionButton` whose label MIRRORS ITS CARD (`CareerPathListItemCommonView`) per [[rt-label-mirror-visual]]:
name + state-word (completed / in progress) + "rank cur of max" ONLY while in-progress/finished (matches
`m_ProgressText`) + recommended; description/prerequisites/stats-raised/keystone+ultimate abilities are all
CareerTooltip → Space (game keeps them tooltip-only). Actionable tier auto-expands + takes initial focus.
**Dependency model** (from `PartUnitProgression.CanUpgradePath`): 3 tiers, ONE archetype per tier for tiers 1-2
(taking any rank in a tier-1 locks all other tier-1s forever; tier-3 exempt), must FINISH current path
(rank==max) before advancing another, `path.Prerequisites.Meet` gates higher tiers. (RT's own
`CalculatedPrerequisite.Description` is obsolete-tagged + huge, unusable — compose the reason instead.)

**Rank selection (stage 2) — PER-RANK OUTLINE (a tree), NOT a 2-D grid.** The game's radial "wheel" is
decorative; the real data is a LINEAR sequence of ranks, each granting auto-features + sometimes a choice (you
advance rank by rank). A 2-D grid is a poor fit — RT has only one axis = rank, unlike WOTR's feature-lines ×
levels. Root TreeGroup → one collapsible TreeGroup per non-empty `cp.RankEntries` (label = "Rank N,
taken/gaining now/locked" + "choice needed"), holding read-only `ProxyRankFeature` nodes (auto features;
Space=tooltip) + `ProxyRankSelection` choice nodes; ranks in `GetCurrentLevelupRange()` auto-expand and initial
focus is the first gaining rank via `tree.SetFocusedChild`. Rank classify: taken=rank<=GetPathRank,
gaining=within range, else locked. Nested trees supported (`Navigator.DescendFrom`: isTreeNode needs
parent.Shape==Tree).

**Stat-readout gotcha:** `RankEntrySelectionStatVM.SummaryStatIncreaseLabel` is `"{cur} > {preview}"` and
preview==cur until the option is staged, so it read "+10, 45 > 45" (noise) and TTS spoke ">" as "greater than".
Fix: derive would-be = `UnitStat.ModifiedValue + parsed StatIncreaseLabel`; announce just "+10" while navigating
and "+10, 45 to 55" (locale `levelup.stat_result`, no ">") only when the option is chosen.

**Files:** `Screens/LevelUpScreen.cs` (ctx.levelup, layer 26, Exclusive; resolves the Progression component VM
above; IsActive = CharacterInfo window + page==LevelProgression + !IsCharGen; career picker → rank selections →
commit), `UI/Proxies/ProxyRankSelection.cs` (group Container(Tree) + `ProxyRankOption` + `ProxyRankFeature`;
options via `sel.UpdateFeatures()`→`sel.FilteredGroupList.OfType<BaseRankEntryFeatureVM>()`, Enter=`opt.Select()`,
live state no rebuild-on-pick), `Accessibility/LevelUpAnnouncer.cs` (passive "{name} can level up" on per-unit
false→true edge, last-known dict avoids load burst). Commit=`CareerPathVM.Commit()`; Escape=`SetCareerPath(null)`
(guarded discard-confirm via MessageBoxScreen) / `HandleCloseAll`. Entry = in-context "Level Up" button on the
CharacterInfo sheet (`CanLevelUp` → the `HandleOpenCharacterInfoPage` raise) + the passive announce. Locale
`levelup.*`.

**Familiar (PetKeystone) fix pass — SHIPPED c74605c (2026-07-22, live-verified on the Overseer career /
Pasqal):** the pet career surfaced three defects, all fixed:
1. *Null tooltip by design*: PetKeystone options null their `TooltipTemplate()` (`RankEntrySelectionFeatureVM.
   OverrideTooltip`); the game's view compensates by building `TooltipTemplatePetKeystone(featureVM, Unit)` on
   the fly (`CareerPathVM.SetFocusOn`). `CareerNodes.OptionTemplate` mirrors that → Space reads the full pet
   card (renders fine through TooltipReader; the "3 0" stat-digit split was fixed in 2ca47b0 —
   `StripRichTextSpaced` glues digit|digit styling-tag runs, since the char-sheet stat views write values
   per-character; TextUtil now unit-tested by link in tests/).
2. *Pick-changes-later-lists (the old "pick-unlocks-new-selection" caveat, now REAL and fixed)*: a staged pick
   injects options into later entries via `AddFeaturesToLevelUp` — the familiar keystone adds the pet's
   abilities (+3), talents (+7) and ultimate (PetUltimateAbility had ZERO options before the pick →
   unfillable required choice → Commit blocked forever). Game rebuilds option VMs on every click-into
   (`HandleClick`/`SetRankEntry`→`UpdateFeatures`); the mod's `LevelUpScreen.RefreshStaleSelections` detects
   pick changes per render and evicts the OTHER in-range selections from the `_materialized` memo (keeping
   the changed one — focus sits inside it), re-latching a fold that was collapsed while the group was a
   non-choice.
3. *Commit parks behind an invisible name box*: with a PetKeystone pick + main character/custom companion,
   `CareerPathVM.Commit()` returns WITHOUT committing and opens `PetChangeNameVM`
   (`CharGenChangeNameMessageBoxVM`) bound ONLY by the game's own progression views (never
   `CommonVM.MessageBoxVM`). `NameEntryScreen.PetBox()` now resolves it via `LevelUpScreen.Vm()` (IsDisposed-
   gated — decline disposes the VM in place while the property keeps holding it); `Commit` skips the
   "complete" announce while the box is open. Story companions (Pasqal) skip the dialog entirely.

**Caveats / open:** builds ALL ranks' selection lists (future ranks show non-selectable previews; possible
brief hitch on long paths); `ShowUnavailableFeatures` hiding options; `ShipCustomizationScreen` Skills tab
carries the same once-per-page `_materialized` memo (no known ship-side `AddFeaturesToLevelUp` injection, so
left alone); chargen-side pet careers (custom companion creation) unhandled by `PetBox` (tier-1 only there,
likely unreachable). Phase 4 starship (`ShipProgressionVM`) deferred.
