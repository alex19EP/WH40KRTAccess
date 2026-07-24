# Respec + Change Appearance — sighted-experience teardown

Two Tier-2 gaps from `docs/ui-coverage-audit-2026-07-24.md`. Both are **surface-only** modals raised by a
story `GameAction`, both were completely dark for the mod. Teardown from the decompile
(`decompiled/Code/Kingmaker.Code.UI.MVVM.{VM.Retrain,View.Respec}`,
`…VM.ChangeAppearance`, `…View.ChangeAppearance.{Common,PC}`), then the build decisions.

---

## 1. Respec / retrain

### Who raises it

`RespecCompanion` (a `GameAction`, so any dialogue answer / story event can fire it) collects every
companion that is `InParty` or `Remote` and passes `PartUnitProgression.CanRespec` (alive, not a pet,
level above the blueprint's `CharacterLevelLimit`). When `ForFree` is false it *further* filters to
those whose `GetRespecCost()` fits inside the current Profit Factor, and **aborts with a designer
error if that leaves nobody** — so an open window always has at least one row in the paid case.
Then, only `if (UINetUtility.IsControlMainCharacter())`, it raises
`ICharacterSelectorHandler.HandleSelectCharacter(units, FinishRespecialization)`.

The only subscriber is `RespecContextVM`, a field of **`SurfaceStaticPartVM` (`:64`)** — `SpaceStaticPartVM`
has no equivalent, so respec is unreachable in the star-system / sector contexts. It creates `RespecVM`
**only when one isn't already open** (`if (RespecVM.Value == null)`).

### What a sighted player sees (`RespecWindowCommonView` + `RespecWindowPCView`)

| Element | Source | Notes |
|---|---|---|
| Header | `UIStrings.CharGen.RespecWindowHeader` | static |
| Warning paragraph | `UIStrings.CharGen.RespecWindowWarning` | static; the "you will lose your build" caveat |
| Character grid | `RespecCharactersSelectorView` over `CharacterSelectionGroupRadioVM` | one card each: **portrait + name only** (`RespecCharacterCommonView` binds exactly `m_Image` and `m_CharacterName`) |
| Cost | `m_RespecCost.text = FormatCost(RespecCost)` | `"0"` when free, else `"-N"` |
| Resource bar | `SystemMapSpaceResourcesPCView` ← `SystemMapSpaceResourcesVM` | colony resources + the PF widget; `SetAdditionalProfitFactor(-cost)` shows the projected spend, and `JournalOrderProfitFactorVM.IsNegative` goes true (red) when the cost doesn't fit |
| Accept | `m_AcceptButton`, `SetInteractable(CanRespec)` | `CanRespec = cost <= Player.ProfitFactor.Total` |
| Close | `m_CloseButton` + `EscHotkeyManager` | |

`StarshipEntity` rows are filtered out in the `RespecVM` constructor. The radio group
`TrySelectFirstValidEntity()`s on open, and every selection change recomputes `RespecCost` from
`ch.Unit.Progression.GetRespecCost()`.

**Cost model** (`RespecInfo`): the first three respecs *per character* are free; after that the cost is
`respecCount - 2` PF. An `AddFreeRespecToPlayer` grant sets `m_HasExtraRespec`, which makes the next one
free and is consumed instead of incrementing the counter.

### Drive paths

* Select — `RespecCharacterVM.SetSelectedFromView(true)` (the standard `SelectionGroupEntityVM` path).
* Accept — **`RespecVM.OnConfirm()`**, which itself raises `IDialogMessageBoxUIHandler.HandleOpen(
  UIStrings.CharGen.RespecSelectCharacter, Dialog, …)`. So the confirm prompt is the game's own message
  box → our existing `MessageBoxScreen` (layer 30) reads it with no extra work. On `Yes` it runs the
  action's `FinishRespecialization` → `GameCommandQueue.FinishRespec`, then one frame later closes the
  window and raises `INewServiceWindowUIHandler.HandleOpenCharacterInfoPage(LevelProgression, unit)` —
  i.e. it hands the player straight to the level-up screen the mod already owns.
* Close — `RespecVM.OnClose()`.

`FinishRespecGameCommand` does the work: `progression.Respec()`, mechadendrite reset, the PF charge
(`ProfitFactorModifierType.Respec`) and `CountRespecIn()` when not free, **advances game time by one day**,
then raises `IRespecHandler.HandleRespecFinished`.

---

## 2. Change Appearance

### Who raises it

The `ChangeAppearance` `GameAction` builds a `CharGenConfig` for
`Player.MainCharacter` in **`CharGenMode.Appearance`** and calls `OpenUI()`. Its `OnComplete` rebuilds the
unit's view object (detach → destroy → attach a fresh one) and re-runs
`UpdateClaimedDlcRewardsByChosenAppearance`.

`OpenUI` raises `IChangeAppearanceHandler.HandleShowChangeAppearance`, whose only subscriber is
`CharGenContextVM` (`:97`). Two instances of that VM exist — `MainMenuVM.CharGenContextVM` and
`SurfaceStaticPartVM.CharGenContextVM` — but never at the same time (`MainMenuVM` is null in play,
`SurfaceVM` is null in the menu), so exactly one `ChangeAppearanceVM` is ever alive. In practice it is
always the **surface** one, since the action needs a `MainCharacter`.

### What a sighted player sees (`ChangeAppearanceView` + `ChangeAppearancePCView`)

The window is the chargen **Appearance phase, standalone**: `ChangeAppearanceVM` owns a
`CharGenAppearanceComponentAppearancePhaseVM` — the exact type the mod's
`Screens/CharGen/AppearancePhaseContent.cs` already renders (page tabs + per-page component cyclers +
the voice list). Everything else on screen is the 3-D doll, the portrait, and the pantograph — decoration
with no text.

Buttons (PC): **Visual settings** (`ShowVisualSettings()`, hidden while the panel is open),
**Accept**, **Cancel**, **Close** (× and Esc — Cancel and Close are the same handler). All four are
hidden wholesale for a non-host in co-op (`CheckCoopButtons(IsMainCharacter)`).

Confirm and cancel are **not** direct calls — the view wraps each in the game's own message box:

```
OnConfirm → UIUtility.ShowMessageBox(UIStrings.ChangeAppearance.ConfirmWarning, Dialog, Yes → vm.Complete())
OnClose   → UIUtility.ShowMessageBox(UIStrings.ChangeAppearance.CancelWarning,  Dialog, Yes → vm.Close())
```

`Complete()` → `GameCommandQueue.CharGenClose(withComplete: true, …)` → `ICharGenCloseHandler.HandleClose`
commits the `LevelUpManager` and invokes the config's `OnComplete`. `Close()` is the same command with
`withComplete: false`.

### Gotchas found

* **`ShouldShowVisualSettings` is dead.** It mirrors `CharGenAppearancePhaseVM.ShowVisualSettings`, which
  `UpdateVisualSettings()` unconditionally sets to `false`. The PC view ignores it and gates the button on
  `VisualSettingsVM == null` instead. Mirror the view, not the flag.
* **`IsInDetailedView` gating.** The phase VM only materialises its pages inside `OnBeginDetailedView`,
  which the game's own view triggers on bind. Same `!IsInDetailedView.Value → BeginDetailedView()` guard
  `CharGenScreen` already uses.
* **The `Cloth` visual-settings toggle finally appears.** `VisualSettingsScreen` notes that
  `CharacterVisualSettingsVM.Cloth` "exists only on the CharGen path (the unit ctor leaves it null)" —
  this is that path, so the toggle is live here and nowhere else.
* `CharacterVisualSettingsVM.Close()` invokes its dispose action, which on this path is
  `ChangeAppearanceVM.HideVisualSettings` — so the existing screen's Escape handler already does the
  right thing.

---

## Build decisions

**`RespecScreen`** — layer 26, Exclusive (must clear `DialogueScreen`'s 15, since the action commonly fires
from a dialogue answer; the confirm box at 30 stays above it). Three Tab stops per the zone rule:
Characters (radio list, label = **name only**, mirroring the card) / Details (warning, cost, live PF) /
Actions (Accept gated on `CanRespec`, Close). Escape → `OnClose()`.

**`ChangeAppearanceScreen`** — layer 16, Exclusive (above dialogue at 15, below the Esc menu at 20).
Two Tab stops: the reused `AppearancePhaseContent` block, then Actions. The content is deliberately *one*
stop rather than split into pages/components zones — it is the same block the chargen wizard renders, and
the spoken contract must not fork between the two entry points. The action stop is suppressed entirely
when `IsMainCharacter` is false, matching `CheckCoopButtons`. Accept/Cancel go through
`UIUtility.ShowMessageBox` with the game's own warning strings, exactly as the view does, so the prompt
lands on `MessageBoxScreen`.

**`VisualSettingsScreen`** gains the appearance panel as a second source and a layer that follows it
(17 over the appearance screen, 13 over the inventory doll) — the two sources can never be live at once,
so the resolution is unambiguous.

Status: **built from the decompile, not live-tested** — the harness belongs to a parallel session, and
both flows are story-gated (a respec service NPC / an appearance-change mirror).
