---
name: rt-service-window-switch-search
description: Inventory/Character service-window party switching (native Shift+A/D over shared SelectedUnitInUI) + accessible game item search; type-ahead OFF policy; char-sheet Abilities/Factions/Biography pages
metadata: 
  node_type: memory
  type: project
  originSessionId: 8b41581f-abcf-4276-8f2a-09b101ec3a51
---

Service windows (Inventory, CharacterInfo, Augmentations) all bind their viewed unit to ONE shared
`Game.Instance.SelectionCharacter.SelectedUnitInUI` (ReactiveProperty<BaseUnitEntity>); switching in one
moves all of them. The game's switch = `SelectionCharacter.SetSelected(unit)` (while a fullscreen window is
open it stashes `m_FullScreenSelectedUnit`, published to SelectedUnitInUI on the NEXT sim Tick — deferred,
don't read back same-frame; never write SelectedUnitInUI.Value directly). Roster = `SelectionCharacter.ActualGroup`.
Default keys **Shift+D / Shift+A** (next/prev-character, `UIKeybindGeneralSettings.NextCharacter/PrevCharacter`),
bound via `Game.Instance.Keyboard.Bind` on BOTH the window header (`CharInfoNameAndPortraitVM.SelectCharacter`)
AND the always-on HUD (`PartyVM.SelectShiftedCharacter`) — both fire (KeyboardAccess iterates all callbacks).
RT has NO standalone Powers window; abilities/psyker powers = the Abilities section on Character Info's Features page.

**Switching decision:** rely on the game's NATIVE Shift+A/D for switching in these windows rather than build our
own switcher. To do that, set `AllowsTypeahead => false` on InventoryScreen + CharacterInfoScreen (our type-ahead
only READ Input.inputString, never blocked the game — it was double-reacting to the letters). Established pattern:
InGameScreen already does AllowsTypeahead=false.

**Accessible inventory search:** the game's search chain is
`ItemsFilterSearchBaseView.m_InputField` (TMP, publicized) → onValueChanged → `ItemsFilterSearchVM.SetSearchString`
→ `SlotsGroupVM.SearchString` (ReactiveProperty<string>) → UpdateVisibleCollection. InventoryScreen.BuildSearch
adds a Search region: Enter hands m_InputField to [[rt-saveload-screen]]-style `TextEntry.Begin` (Unity/TMP own
typing; game's binding filters live; our BuildStash reads VisibleCollection), + a conditional Clear button.
Find the field via `FindObjectsByType<ItemsFilterSearchBaseView>` (pick active/interactable, PC view in mouse mode).

**Switch-announce + header chrome:** `Accessibility/ViewedCharacter.cs` speaks the viewed unit on a Shift+A/D switch
(name/level/wounds) — the game never announces it and SelectionAnnouncer only watches the WORLD `SelectedUnit`, not
the fullscreen `SelectedUnitInUI` preview. Driven from each screen's `OnUpdate` (fires on the focused screen each
frame; `ScreenManager.Current`=`_stack[^1].DeepestActiveScreen()`), Reset on OnPush. Inventory got a top "Character"
region (header readout + pet/master swap); char sheet got the swap button in its header. Pet swap = the game's
m_PetButton (`unit.IsPet`→SetSelected(Master) / `unit.IsMaster`→SetSelected(GetOptional<UnitPartPetOwner>().PetUnit));
pets are off ActualGroup.

**Char-sheet added pages:** CharacterInfoScreen.Rebuild appends three adapter-era TreeGroups a blind player couldn't
reach — all read field-first off the unit/player, Space drills the game's own tooltip template via TextElement's
`tooltip:` factory (→GetTooltipTemplate→TooltipReader):
- **Abilities ("powers")**: `UIUtilityUnit.CollectAbilities/CollectFeatures(unit)` + `GetOptional<PartUnitBody>().Augments`
  (OverdriveAbility + Slots) → Active/Passive/Augmentations; tooltips = TooltipTemplateAbility(a.Data)/Feature(f)/Item(item).
- **Factions & Reputation** (PARTY-WIDE, reads Player not unit): `ReputationHelper.Factions`/GetCurrentReputationLevel/
  IsMaxReputation/GetCurrentReputationPoints/GetNextLevelReputationPoints + `UIStrings.Instance.CharacterSheet.GetFactionLabel/
  GetFactionDescription` + `Player.ProfitFactor.Total`. Do NOT instantiate CharInfoFactionReputationItemVM/ProfitFactorVM
  (EventBus.Subscribe in ctor → leak); build TooltipTemplateSimple yourself.
- **Biography** (unit-typed like CharInfoPagesPC): soul-mark STANDING all units (`SoulMarkTooltipExtensions.GetSoulMarkInfo`,
  SoulMarks={Faith,Corruption,Hope}, tooltip=TooltipTemplateSoulMarkHeader(unit,dir)); MC → `SoulMarkShiftExtension.AppliedShifts()`
  history; companion/pet → `Player.CompanionStories.Get(unit)`. "Alignment"-named classes = Pathfinder leftovers holding pure
  40K soul-marks.

TYPE GOTCHA verified in-tree: UIStrings.Instance.CharacterSheet.{FactionsReputation,MaxReputationLevel,Biography,
EmptyBiographyDesc,EmptySoulMarkShiftsDesc} + ProfitFactorTexts.{Title,Description} are LocalizedString → .Text needed.
GetFactionLabel/Description return string. SoulMark* live in TWO tooltip namespaces: Code.UI.MVVM.VM.Tooltip.Templates
(Ability/Feature/Item/Simple) vs UI.MVVM.VM.Tooltip.Templates (SoulMarkHeader/SoulMarkTooltipExtensions).
See [[rt-inventory-window-parity]], [[rt-label-mirror-visual]].
