# UI paradigm pivot — parallel mod-owned accessible tree (with Phase 0 dev harness)

## Context

RTAccess today rides the game's **console focus ring**: it forces gamepad mode
([[rt-ui-focus-hook]]), patches `ConsoleEntityExtensions.SetFocused`, reads whatever the game
just focused (`UiTextReader`), and injects `VirtualNavItem`s where the ring has gaps. This got
broad menu/dialogue coverage cheaply (~2,775 LOC), but it has hard ceilings: reading order is
Owlcat's ring-construction order, mouse-only / not-in-ring content is unreachable (dense 40K
tooltips + encyclopedia glossary links), in-place value changes need a bespoke patch per widget,
and forced gamepad mode deactivates PC-HUD panels.

**Decision: pivot to the WrathAccess paradigm** — a retained-mode, mod-owned accessible UI tree
that mirrors the game's ViewModels, navigated by our own `Navigator`, with activation invoking the
**real VM method / EventBus event** (not synthetic clicks). Proven by the sibling mod
([[wotr-access-prior-art]], `docs/wotr-access-steal-report.md`). **Forced console mode is removed
entirely** and **the dev rollout is big-bang** (per the maintainer) — no bridge; the game is dark
for UI until each Screen lands, so screens are built in critical-path order.

We build the **dev harness first (Phase 0)** so every later screen is built against a scriptable,
observable loop. All cross-cutting verify-points have been settled against the decompiled source
(`decompiled/`, see the **Settled facts** section) — including one consequential discovery:
**the game gates its surface interactable layer on console mode at the engine level**
(`SurfaceMainInputLayer.OnUpdate` is wrapped in `!Game.Instance.IsControllerMouse`,
`SurfaceMainInputLayer.cs:107`), so removing console mode also breaks the already-shipped
interactable cycling — see the audit.

This stays a pure UI-layer pivot in spirit; RT's combat/grid differences ([[rt-world-grid]]) don't
touch the framework, which reads VMs directly.

## Settled facts (verified against decompiled source + RTAccess.csproj)

**Dev harness**
- **Mono.CSharp**: NOT in the game's managed dir. Vendor it via NuGet `Mono.CSharp` `4.0.0.143`
  (targets net40, loads fine on Unity Mono / net481 — WrathAccess does exactly this). Add a
  `Condition="'$(Configuration)'=='Debug'"` `PackageReference` (`GeneratePathProperty="true"`) +
  `Reference` (`Private=false`), and one `Copy` of `Mono.CSharp.dll` into the UMM mod dir in the
  Deploy target.
- **Debug deploys**: `RTAccess.csproj` `Deploy` target is `AfterTargets="Build"` with **no**
  Configuration condition → `dotnet build -c Debug` deploys; the SDK defines `DEBUG` so `#if DEBUG`
  dev code compiles in. Release stays clean (dev code compiled out).
- **Pump**: `Main.cs:35` `modEntry.OnUpdate = OnUpdate;` (Main.cs:41) is the per-frame main-thread
  hook — call `DevServer.Instance.Pump()` first thing. `Application.runInBackground = true` is
  settable and unmanaged by game code; set + re-assert each Pump.
- **/loadsave**: `Game.Instance.SaveManager` (field; `GetLatestSave`/`GetNewestQuickslot`/
  `GetAnyLatestSave`; `IEnumerable<SaveInfo>`), `LoadingProcess.Instance.IsLoadingScreenActive` +
  `IsLoadingInProcess`, kick via `MainMenuUI.Instance.EnterGame(() =>
  Game.Instance.LoadGameFromMainMenu(save))`, in-play poll =
  `Game.Instance.CurrentlyLoadedArea != null && !IsLoadingScreenActive && !IsLoadingInProcess`.
- **Speaker.Observer**: `RTAccess/Speech/Speaker.cs:26` `Speak` is the sole chokepoint (already
  calls `SpeechLog.Write`). Add `#if DEBUG public static Action<string> Observer; #endif` + invoke
  at top of `Speak`; feed a 500-line ring buffer (new `Dev/SpeechTap.cs`).

**ScreenManager resolution** — `Game.Instance.RootUiContext` (`Kingmaker.Code.UI.MVVM.RootUIContext`):
- Top-level (mutually exclusive): `IsMainMenu` (`MainMenuVM!=null`), `IsSurface` (`SurfaceVM!=null`),
  `IsSpace` (`SpaceVM!=null`), `IsLoadingScreen`. `IsInGame == IsSurface || IsSpace`.
- New game: `MainMenuVM.NewGameVM.Value`. CharGen (new-game **and** level-up):
  `IsChargenShown` / `CharGenContextVM.CharGenVM.Value` (namespace `Kingmaker.UI.MVVM.VM.CharGen`).
- Service windows: `…StaticPartVM.ServiceWindowsVM.<X>VM.Value != null` (`InventoryVM`,
  `CharacterInfoVM`, `JournalVM`, `EncyclopediaVM`, `LocalMapVM`, …). Active window also pollable
  via `RootUiContext.CurrentServiceWindow` (plain field — must poll) or push via
  `ServiceWindowsVM.OnOpen` (`ReactiveCommand<ServiceWindowsType>`).
- Vendor/Loot (NOT in `ServiceWindowsType`): `…StaticPartVM.VendorVM.Value` /
  `LootContextVM.LootVM.Value`; both raise `IFullScreenUIHandler(state, FullScreenUIType.*)`.
- Esc menu: `CommonVM.EscMenuContextVM.EscMenu.Value`. Save/Load: `CommonVM.SaveLoadVM.Value`.
- Dialogue: `RootUiContext.HasDialog` + `…StaticPartVM.DialogContextVM` (`DialogVM`/`BookEventVM`/
  `EpilogVM`/`InterchapterVM`). **Visibility** (cutscene hides the panel): subscribe
  `DialogContextVM.ToggleDialogFadeCommand` (`ReactiveCommand<bool>`, push-only) into a shadow
  `bool _dialogFaded`; gate cue announce on `HasDialog && !_dialogFaded`.
- Lifecycle: UniRx `ReactiveProperty<TVM>` + dispose→`.Value=null` confirmed → poll-and-diff with
  null-propagating reads + try/catch is correct.

**Activation contracts** (proxy activate → real action; `EventBus.Subscribe/RaiseEvent` already used
at `RTAccess/Main.cs:25,28`):
- Equip (stash): `EventBus.RaiseEvent<IInventoryHandler>(h => h.TryEquip(itemSlotVM))`. Equip-slot
  (doll): `IInventoryItemHandler.HandleChangeItem(equipSlotVM)`. Direct:
  `Game.Instance.GameCommandQueue.EquipItem(...)`. Labels: `ItemSlotVM.DisplayName/TypeName/HasItem/
  CanUse/Count/Tooltip`.
- Action bar: `ActionBarSlotVM.OnMainClick()` → `IClickMechanicActionBarSlotHandler`. Labels:
  `MechanicActionBarSlot.GetTitle()`, `ActionPointCost`, `IsPossibleActive`, `Tooltip`. AP/MP:
  `unit.GetCombatStateOptional().ActionPointsYellow/Blue`.
- Dialogue answer: `AnswerVM.OnChooseAnswer()`. Labels: `Answer.Value.DisplayText`, `Enable`,
  `Index`, `SkillChecksDC`, `SoulMarkShift`.
- Settings: `SettingsEntityBoolVM.ChangeValue()`, `SettingsEntityDropdownVM.SetNext/PrevValue()`,
  `SettingsEntitySliderVM.SetNext/PrevValue()`; labels via `SettingsEntityVM.Title/Description`.

**Keyboard ownership**: raw `UnityEngine.Input.GetKeyDown` is mode-independent (our polling always
works). Game-key suppression lever = `Game.Instance.Keyboard.Disabled.SetValue(true/false)`
(`CountingGuard`; the `Scope()` overload is NOT confirmed in the RT decompile — use `SetValue`).
Our nav keys (PageUp/Dn/Home/End/brackets) collide with no registered `KeyboardAccess` binding.

## WrathAccess → RTAccess file map

Framework (engine-generic — near-verbatim, namespace `WrathAccess.*` → `RTAccess.*`):

| WrathAccess (`src/`) | New RTAccess file | Adaptation |
|---|---|---|
| `UI/UIElement.cs`, `Container.cs`, `ListContainer.cs`, `Panel.cs`, `Bar.cs`, `TreeGroup.cs`, `Table.cs`, `FlowSheet.cs`, `TextElement.cs`, `TextEntry.cs`, `NavTypes.cs` | `RTAccess/UI/*` | none (no game refs) |
| `UI/Navigation.cs`, `Navigator.cs`, `TraditionalNavigator.cs`, `ElementAction.cs`, `ActionArgs.cs`, `TypeAheadSearch.cs` | `RTAccess/UI/*` | none; check `TypeAheadSearch` THIRD-PARTY license (OniAccess port) |
| `UI/Announcements/*` | `RTAccess/UI/Announcements/*` | none; speak via `Speaker` |
| `Input/InputManager.cs`, `InputAction.cs`, `InputBinding.cs`, `InputCategory.cs`, `KeyboardBinding.cs`, `OsKeyboard.cs` | `RTAccess/Input/*` | Unity-generic; driven from UMM `OnUpdate` |
| `FocusMode.cs`, `ControlState.cs` | `RTAccess/UI/FocusMode.cs`, `ControlState.cs` | `FocusMode` uses `Game.Instance.Keyboard.Disabled.SetValue` (not `.Scope()`); `ControlState` uses `Game.Instance.ClickEventsController` |

Dev harness (Phase 0):

| WrathAccess (`src/Dev/`) | New RTAccess file | Adaptation |
|---|---|---|
| `Dev/DevHttpServer.cs` | `RTAccess/Dev/DevHttpServer.cs` | verbatim (raw `TcpListener`, loopback) |
| `Dev/CSharpEvaluator.cs` | `RTAccess/Dev/CSharpEvaluator.cs` | verbatim; `Code.dll` + `RogueTrader.GameCore.dll` get referenced by the AppDomain walk; keep the BCL dedupe set |
| `Dev/DevServer.cs` | `RTAccess/Dev/DevServer.cs` | env gate `RTACCESS_DEV` + marker; Pump from UMM `OnUpdate`; `/speech` taps `Speaker.Observer`; `/loadsave` uses the settled RT types; `/input`+`/gui` land in Phase 2 |
| `Dev/DevApi.cs` | `RTAccess/Dev/DevApi.cs` | `Speaker.Speak` not `Tts.Speak`; `Screen()` lands Phase 2 |
| `Dev/SpeechLog.cs` | `RTAccess/Dev/SpeechTap.cs` | renamed (avoid colliding with `Accessibility/SpeechLog.cs` file logger) |
| `Dev/GuiInspector.cs` | `RTAccess/Dev/GuiInspector.cs` | **Phase 2** (needs the new `ScreenManager`/`UIElement` tree) |

Screens (`src/Screens/*`) + proxies (`src/UI/Proxies/*`) are **not** ported as files — re-authored
against RT VMs per the **Settled facts**, screen by screen, in Phases 2–3.

---

## Phase 0 — Dev harness (build first)

A `#if DEBUG` loopback HTTP server an external driver (Claude/curl) uses to introspect/drive the
live game, gated on `RTACCESS_DEV=1` or marker `devserver.enable` under
`persistentDataPath/RTAccess/`. Loopback-only; default port `8772`.

Endpoints independent of the new tree (ship now): `POST /eval` (main-thread REPL via the Pump;
`runInBackground=true`), `GET /speech?since=N` (the `Speaker.Observer` ring), `GET /screenshot`,
`POST /loadsave` (settled types above), `GET /health`. Deferred to Phase 2 (need the tree):
`GET /gui`, `POST /input`.

NEW `RTAccess/Dev/{DevHttpServer,CSharpEvaluator,DevServer,DevApi,SpeechTap}.cs`.
EDIT `Speaker` (`Observer` hook); `Main.cs` (`DevServer.Start` in `Load`, `Pump` first in `OnUpdate`);
`RTAccess.csproj` (Debug-gated `Mono.CSharp` package + deploy copy).

**Verify:** `dotnet build -c Debug`, drop marker, launch, then
`curl --data 'DevApi.Say("up"); 2+2' 127.0.0.1:8772/eval` → `=> 4`;
`curl '127.0.0.1:8772/speech?since=0'` → shows "up".

**Status — IMPLEMENTED & CONFIRMED IN-GAME (v1.6.0.482).** `/health`→`ok`, `/eval`→`=> 4` +
mod-assembly reflection + multi-statement, `/speech` live-taps spoken lines with cursor paging,
`/screenshot` writes a readable framebuffer PNG. (`/loadsave` not yet exercised — state-changing.)
One follow-up staged for the next rebuild: the evaluator now also seeds
`using RTAccess; using RTAccess.Dev; using RTAccess.Speech; using RTAccess.Accessibility;` so bare
`DevApi`/`Speaker` resolve (this session needed `RTAccess.Dev.DevApi.…` qualified). Shipped
`RTAccess/Dev/{DevHttpServer,
CSharpEvaluator,DevServer,DevApi,SpeechTap}.cs` (all `#if DEBUG`), the `Speaker.Observer` tap, and the
`Main.cs` `Start`/`Pump` wiring. `RTAccess.csproj` gets `Mono.CSharp 4.0.0.143` via a
`Condition="'$(Configuration)'=='Debug'"` `PackageReference` — copy-local lands `Mono.CSharp.dll` in
`$(TargetDir)` and the existing wildcard Deploy carries it into the UMM folder (no explicit `Copy`
needed). Verified: `dotnet build -c Debug` → 0/0, deploys `Mono.CSharp.dll`+`RTAccess.dll`;
`dotnet build -c Release` → 0/0, dev code + `Mono.CSharp` gated out. Marker dropped at
`<persistentDataPath>/RTAccess/devserver.enable` (i.e. `…LocalLow/Owlcat Games/Warhammer 40000 Rogue
Trader/RTAccess/devserver.enable`). `/gui` + `/input` return `[not yet]` stubs until Phase 2.
Remaining: launch the game and run the curl checks above (port `8772`).

## Phase 1 — Port the engine-generic framework

Bring over `RTAccess/UI/*` + `RTAccess/Input/*` + `FocusMode`/`ControlState` per the map. Confirm
the two Owlcat-generic levers exist as stated (`Keyboard.Disabled.SetValue`, `ClickEventsController`).
Exercise from `/eval`: build a scratch `Panel`/`TextElement`, walk it via the `Navigator`, hear the
composed announcements over `/speech`.

**Status — DONE & VERIFIED IN-GAME.** Ported 48 files verbatim (namespace `WrathAccess`→`RTAccess`):
`UI/*` (Container/ListContainer/Panel/Bar/TreeGroup/Table/FlowSheet/TextElement/TextEntry/NavTypes/
Navigation/Navigator/TraditionalNavigator/ElementAction/ActionArgs/TypeAheadSearch), `UI/Announcements/*`,
`Input/*`, `Settings/*` (12), `Localization/*` (2), `Screens/Screen.cs` + the engine-generic core of
`ScreenManager.cs`. Hand-adapted for RT: `UIElement.ActivateSound` (`UISoundType?` →
`BlueprintUISound.UISound`), `UiSound` (`UISounds.Instance.Play*`), `Tts` (→ `Speaker` + StripRichText),
`FocusMode` (`Keyboard.Disabled.SetValue`), `ControlState` (`ClickEventsController`, as-is). Deferred:
`ScreenManager.Initialize` concrete screens → Phase 2; `TraditionalNavigator.OpenTooltipOrLinks` (Tooltips/
TooltipScreen) → Phase 3. Build deltas fixed: added `CountingGuard.dll` reference; **renamed nothing but
qualified legacy bare `Input.GetKey*` → `UnityEngine.Input` (our new `RTAccess.Input` namespace shadowed
it)**; `RootUIContext.IsInInputField` → `KeyboardAccess.IsInputFieldSelected()`; `LocalizationManager`
uses `Main.ModDir` (new) + `LocalizationManager.Instance.CurrentLocale`. Ported enGB locale assets
(`ui.json`/`settings.json`) + wired `LocalizationManager.Initialize`/`Tick` in `Main`. Verified via
`Dev/FrameworkProbe.cs` (`#if DEBUG`): walking a scratch list spoke
`Test Menu, list, Alpha, option, 1 of 3` → `Bravo, option, 2 of 3` → … over `/speech`. Debug 0/0;
Release compiles 0/0 (only its native-DLL Deploy locks while the game runs). NOT yet wired into
`Main.OnUpdate` (InputManager/ScreenManager/FocusMode/Typeahead ticks) — that's Phase 2.

## Phase 1.5 — Console-mode removal (audit results)

**Status — CORE DONE & VERIFIED IN-GAME.** Stopped forcing console mode. ⚠ **Gotcha:** deleting the
`Game.ControllerOverride` patch outright brings back the inaccessible **"press A for gamepad / Enter for
keyboard" boot prompt** (the override had been suppressing it). Fix: **repurpose the override to force
`Mouse`** (not delete it) — that skips the prompt AND lands in mouse mode. `ConsoleMode` now has
`ForceMouseAtLaunch` (the override returns `Mouse` + `Initialize` lock); `EnableByDefault` and the
gamepad-forcing are gone; `SetMode`/F6 `Toggle` kept for debug. Verified: boots straight to the menu
in ~4s, `ControllerMode == Mouse`, no prompt, no gamepad glyphs (screenshot), and the parallel
`MainMenuScreen` resolves + navigates in mouse mode (`/input ui.down` → real labels). The console focus
ring is no longer keyboard-driven, so physical-key nav no longer double-acts. **Deferred:** the
DIES/RE-GATE/REBUILD cleanup below is entangled with the Phase 3 InGame screen — the console-era in-game
helpers (`PartyHotkeys`/`ExplorationNav`/`LandmarkNav`/`TileExplorer`) stay gated on
`ControllerMode == Gamepad` (reachable via F6) for now rather than half-re-gating before that screen
exists; `SetFocusedPatch` & the rest of the console-read path go cleanly inert in mouse mode.

Original audit (delete `ConsoleMode`'s startup patches + `EnableByDefault`; keep `SetMode`/F6 `Toggle`).
Then handle each subsystem per the audit:

**DIES — replaced by the parallel tree (expected, the point of the pivot):** `SetFocusedPatch`
(the *entire* UI-read path — total UI silence until Screens exist), `VirtualNavItem`,
`DialogNavAugmentor` (patches console-only `DialogAnswerConsoleView`; PC uses `DialogAnswerPCView`),
`NavInputProbe`, `WheelMenus` (4 console-only HUD view targets), `SettingsValueAnnounce` (console
views), `UiTextReader`/`TooltipReader` (callers gone), F7 re-read + Ctrl+I (depend on `_last`).

**RE-GATE — mechanism survives, drop `ControllerMode==Gamepad`, gate on `GameModeType.Default`/
world-control instead:** `LandmarkNav` (clean), `PartyHotkeys` (⚠ verify no double-fire vs the PC
HUD's own `Shift+A/D`/`Alt+1-6`), `TileExplorer` (⚠ verify arrow keys don't double-act with the
Unity EventSystem; bracket with `Keyboard.Disabled.SetValue` while active if needed).

**REBUILD — engine-dead in mouse mode:** `ExplorationNav` interactable cycling + `ExplorationEvents`
interactable announce. Cause: `SurfaceMainInputLayer.OnUpdate` is gated on `!IsControllerMouse`
(`:107`), so `m_InteractableObjects` stays empty and `ISurroundingInteractableObjectsCountHandler`
never fires. **Recommended fix:** drive the scan ourselves — each frame on the surface in
`GameModeType.Default`, call `SurfaceMainInputLayer.Instance.UpdateInteractableSet`/
`UpdateInteractions` directly (reachable: `Code.dll` is publicized), bypassing the engine gate, then
cycle/read as today. Verify those methods don't internally re-check `IsControllerMouse` or run
gamepad-stick logic; fallback = our own `EntityBoundsHelper.FindEntitiesInRange` scan + we raise the
EventBus event. (NEEDS-VERIFY-IN-GAME via the harness once Phase 0 lands.)

**SURVIVES AS-IS:** `BarkEvents`, `ServiceWindowAnnounce` (hooks shared `ServiceWindowsVM.ShowWindow`),
`Speaker`, `InteractableDescriber`, `CharGenAnnounce` (hooks shared `CharGenView` base + VM layer —
only the Ctrl+I details path needs rewiring off `_last`), `ExplorationEvents` area/loading handlers,
F9/F10/F12. **`DialogCuePatch`**: flip `AutoReadEnabled = true` — it hooks mode-agnostic
`DialogVM.HandleOnCueShow` and becomes the interim dialogue-cue reader until `DialogueScreen` lands.

## Phase 2 — Vertical slice POC (prove the loop end-to-end)

Stand up `ScreenManager` (poll-and-diff over `RootUiContext` per the settled signals; `SafeIsActive`
try/catch; null-propagating reads) + the minimum Screens to prove
`game VM → ScreenManager → Screen → proxy → Navigator → Speaker`:
`MainMenu → NewGame → InGame (Surface) → Inventory`.

Validates the three things only provable against RT VMs: **resolution** (the `RootUiContext` signals),
**activation** (`IInventoryHandler.TryEquip` etc.), **suppression** (`FocusMode` via
`Keyboard.Disabled.SetValue`). Ship `/gui` + `/input` here.

**Verify:** `/loadsave latest` → `/gui` (InGame tree) → `/input ui.activate` on Inventory → `/gui` +
`/speech` confirm the inventory tree builds/reads → equip via `/input ui.activate` → re-read the slot
VM via `/eval` to confirm real game-state change.

**Status — MainMenu slice DONE & VERIFIED IN-GAME (the first concrete screen, ahead of Inventory).**
Wired the framework into `Main.OnUpdate` (boot-engage `FocusMode` once `Game.Keyboard` exists, then
`FocusMode.Tick` + `InputManager.Tick` + `ScreenManager.Tick` + `Navigation.TickTypeahead`) and
`Main.Load` (`InputBindings.RegisterDefaults` + `ScreenManager.Initialize`). NEW: `Input/InputBindings.cs`
(Global `toggle_focus` Ctrl+Shift+A + the `ui.*` nav actions), `Screens/MainMenuScreen.cs`
(`IsActive` = `RootUiContext.MainMenuVM != null` minus sub-window/popup guards; `OnPush` builds a
`ListContainer` of the `MainMenuSideBarVM` entries), `UI/Proxies/{ProxyActionButton,MainMenuButton}.cs`
(`MainMenuButton` reads `ContextMenuEntityVM.Title.Value`/`Execute()`/`IsEnabled`, live-enabled via the
publicized `m_Entity`), `Dev/GuiInspector.cs`. `ScreenManager.Initialize` now registers `MainMenuScreen`;
DevServer `/gui` + `/input` implemented (the `Inject` path). **Proven via the harness:**
`ScreenManager.Current`→`ctx.mainmenu`; `/gui` shows the 10 sidebar buttons; `/speech` landing =
`Main Menu, list, Продолжить, button, 1 of 10`; `/input ui.down/up/end` navigate with correct labels +
positions; **`/input ui.activate` on Credits ran the real `Execute()` → `CreditsVM.Value != null` and
`ScreenManager` correctly dropped the now-inactive screen.** Confirms the three RT-only unknowns:
resolution (RootUiContext), activation (`ContextMenuEntityVM.Execute`), suppression (`FocusMode`).
Caveat: physical-key nav still double-acts with the live console ring (ConsoleMode not yet removed) —
verified via `/input` (mode-independent); the physical-key path is settled in Phase 1.5.

## Phase 3 — Fan out by critical path

One Screen + proxies each, harness-verified before the next:
`DialogueScreen` (subsumes `DialogCuePatch`; gate on `_dialogFaded`) → `CharGen` (P4–P6,
[[rt-chargen-accessibility]]) → service windows (Journal, Character, Vendor, Loot) → Settings +
Save/Load → long tail. Flagship payoff: the tooltip/encyclopedia drill-in reader (RT's biggest
unreachable-content gap), once the document-substrate `Screen` pattern exists.

**Status — SettingsScreen DONE & VERIFIED IN-GAME** (done early; copies WrathAccess's settings menu).
Resolves `RootUiContext.CommonVM.SettingsVM.Value` (layer 25, sits above the menu). NEW
`Screens/{SettingsScreen,SettingsEntityBuilder}.cs` + proxies `UI/Proxies/{ProxyToggle,ProxySlider,
ProxyDropdown,ProxySettingsTab,ProxyUnsupportedSetting}.cs`. RT entity VMs (`SettingsEntity{Bool,Slider,
Dropdown}VM`, `SettingsMenuEntityVM`, `SettingsEntityHeaderVM`) map 1:1 to WOTR (`Title` is a
`LocalizedString`→`.Text`; header keeps the `Tittle` typo; tab select = `SetSelectedFromView(true)` on
the `SelectionGroupEntityVM` base). Tree: root Panel = [tabs `ListContainer`, content `TreeGroup` of
collapsible header sections (rebuilt on tab-change poll), Close]. Verified: `/gui` shows 6 tabs +
toggles/sliders/dropdowns with live values + descriptions; switching tabs rebuilds content; expanding a
section + `ui.left` on Master Volume read `100→99→98` (real `SetPrevValue`). **Adaptations/defers:**
inlined the `[AnnouncementOrder]` attrs (no sibling carrier types); dropped the WOTR `UISoundType`
sounds; key bindings / difficulty / opt-out / images → `ProxyUnsupportedSetting` fallback.

**Settings follow-ups DONE & VERIFIED IN-GAME** (the three deferred bits):
- **Dropdown submenu** — NEW `Screens/ChoiceSubmenuScreen.cs` + `UI/Proxies/ProxyChoiceOption.cs` (child
  screen via `PushChild`); `ProxyDropdown` activate opens the option list (Left/Right still cycle inline).
  Verified: activating Язык pushed a child listing all 8 languages, focus on the selected one, Back returns.
- **Tooltips (lightweight)** — NEW `Screens/TooltipScreen.cs` + `UIElement.GetTooltipText()` (settings
  proxies return their Description); re-implemented `TraditionalNavigator.OpenTooltipOrLinks` to open it.
  Space splits a description into navigable sentences in a child screen. The rich brick/glossary reader
  (`TooltipFlowBuilder`) stays the deferred flagship.
- **Apply/save-confirm** — NEW `Screens/MessageBoxScreen.cs` (resolves `CommonVM.MessageBoxVM`, layer 30,
  Exclusive; Accept/Decline) + Apply/Reset buttons in `SettingsScreen` (live-gated on
  `SettingsController.HasUnconfirmedSettings()` / `IsDefaultButtonInteractable`). Verified: flip a toggle →
  Apply → navigable "Save / Cancel" dialog → Cancel reverts. (Text-field/progress/checkbox modal variants
  still deferred.)

**New Game wizard + Character Generation DONE & VERIFIED END-TO-END IN-GAME** (full flow Main Menu →
New Game → into the game world). Shared `Screens/WizardScreen.cs` shell (phase content Panel + Back/Next
footer; rebuilds + re-homes focus + page-turn on phase change; `BuildHeader` hook for the roadmap).
- **`NewGameScreen`** (`ctx.newgame`, layer 5) resolves `MainMenuVM.NewGameVM.Value` (null once
  `CharGenContextVM.CharGenVM` opens → hands off). Story phase = campaign picker (`ProxySelectionItem`
  over `NewGamePhaseStoryVM.SelectionGroup` + live `Description`); Difficulty phase reuses
  `SettingsEntityBuilder` over `NewGamePhaseDifficultyVM.SettingEntities`. RT gotcha: only the Story
  phase gates Next on `IsNextButtonAvailable` — Difficulty never sets it (inherits `false`), so it's
  unconditionally enabled; last phase's Next reads "Begin".
- **`CharGenScreen`** (`ctx.chargen`, layer 15) on the WizardScreen shell; resolves
  `MainMenuVM.CharGenContextVM.CharGenVM.Value`. Roadmap strip (`ProxyRoadmapEntry` per
  `PhasesCollection`, polled + rebuilt on phase-set change — picking custom adds Homeworld/Occupation/
  Career/… phases). Next = `SelectNextValidEntity` / Complete on last (gated on `CurrentPhaseIsCompleted`);
  Back = `SelectPrevValidEntity` / Close on first. Per-phase content via `Screens/CharGenPhaseContent.For`
  (dispatch on `PhaseType`) + `Screens/CharGen/*`:
  - **Pregen** (`PregenPhaseContent`) — pick a pregen or "Create Custom" (reveals the custom phase set).
  - **Background phases** (`SelectionPhaseContent`) — Homeworld/Occupation/SoulMark/Navigator/Moment of
    Triumph/Darkest Hour + child phases all derive from generic `CharGenBackgroundBasePhaseVM<T>`; one
    builder reads items off the `SelectionGroup` field by **reflection** (open-generic base, items share
    `CharGenBackgroundBaseItemVM` = DisplayName + Feature) → radio list + live `Feature.Description`.
  - **Career** (`CareerPhaseContent`) — NOT a background phase; drives `UnitProgressionVM.AllCareerPaths`
    (`CareerPathVM` = a `SelectionGroupEntityVM`); list the tier-one archetypes. Selecting one completes
    the phase (rank-1 ability picks not required for chargen).
  - **Attributes** (`AttributesPhaseContent` + `UI/Proxies/ProxyStatStepper`) — live points-remaining +
    per-stat Left/Right steppers (`AdvanceStat`/`RetreatStat` via the game's advance-stat command).
  - **Ship / Summary** — selection (ship) + live name readout + random-name action; both gate completion
    on a *manually-set* name (`SetName(..,isManual:true)` / `SetNameAndNotify`), so random-name unblocks it.
  - **Appearance** (`AppearancePhaseContent` + `UI/Proxies/ProxySequentialSelector`) — page tabs (Portrait/
    General/Hair/Tattoo/Implants/Voice) + the current page's components (rebuilt on page change; pages +
    components load async). Voice = named radio list with audible samples on select; Gender = cycler reading
    the doll (Male/Female); other looks = reflection-driven "Title, N of M" cyclers over the open-generic
    `SequentialSelectorVM<T>`; portrait = visual note. Auto-completes with defaults.
  Many phase lists load a frame late (async pregen/blueprint/level-up loads), so the base rebuilds content
  on `LiveCount()` change. **Verified end-to-end via the harness:** custom character → every phase
  completed (archetype, all 30 attribute points spent, ship + character named) → `Complete()` → loaded
  into `VoidshipOfficersDeck` (the opening area, party of 1). **Robustness fixes made:** `Main.OnUpdate`
  guarded `Game.Keyboard` (its getter NREs mid-load when `UISettingsRoot.Instance` is null, which broke
  the whole tick that frame); `ScreenManager.Tick` now wraps `Current.OnUpdate()` in `Safe` so a faulty
  phase build can't kill the mod tick.
  - **Custom name typing DONE** — `Screens/NameEntryScreen.cs` (layer 32, Exclusive) resolves the current
    phase's change-name `MessageBoxVM`; "Edit name" opens the game modal, the screen finds the active
    `TMP_InputField` and drives it via `UI/TextEntry` (real IME/Unicode), Apply/`OnAcceptPressed` + Random +
    Back/`OnDeclinePressed`. Verified: typed name carried into the world (`MainCharacter.Entity.CharacterName`).
  - **Summary character review DONE** — read-only level + final attributes (`unit.Stats` +
    `CharInfoAbilityScoresBlockVM.AbilitiesOrdered` + `LocalizedTexts.Instance.Stats.GetText`).
  **Remaining (deferred, not blocking):** the career rank-1 ability/talent picker — not exposed at chargen
  (`RankEntries`/`AllSelections` == 0, not required to complete); it belongs to the future level-up subsystem
  (its own screen, shared with in-game level-ups). Minor: portrait/skin-colour visual-only; review careers line empty.

---

## Files

- NEW `RTAccess/Dev/{DevHttpServer,CSharpEvaluator,DevServer,DevApi,SpeechTap}.cs` (P0); `GuiInspector.cs` (P2)
- NEW `RTAccess/UI/*` + `RTAccess/UI/Announcements/*` + `RTAccess/Input/*` + `RTAccess/UI/{FocusMode,ControlState}.cs` (P1)
- NEW `RTAccess/Screens/{Screen,ScreenManager}.cs` + per-window screens + `RTAccess/UI/Proxies/*` (P2–3)
- EDIT `RTAccess/Speech/Speaker.cs` (`Observer`); `RTAccess.csproj` (Debug `Mono.CSharp` + deploy copy)
- EDIT `RTAccess/Main.cs` (`DevServer` P0; `ScreenManager.Tick`+`InputManager.Tick` P2; delete `ConsoleMode` startup patches at P1.5; rewire F7/Ctrl+I or retire)
- RE-GATE `LandmarkNav.cs:30`, `PartyHotkeys.cs:27`, `TileExplorer.cs:39`; REBUILD `ExplorationNav.cs`/`ExplorationEvents.cs` interactable path; ENABLE `DialogCuePatch.AutoReadEnabled`

## Edge cases / risks

- **Total UI silence until Screens land** (big-bang): accepted; build in critical-path order.
- **Interactable nav engine-dead** (highest risk): the recommended self-driven scan must be verified
  in-game; until then exploration interactable cycling is the one regression with no automatic fallback.
- **Mouse-mode key conflicts**: PartyHotkeys double-fire + TileExplorer arrow double-act — verify via
  harness; `Keyboard.Disabled.SetValue` brackets suppression if needed.
- **Silent-wrong on VM restructure**: `/gui`+`/speech` smoke test per screen catches it.
- **No native focus sounds / scroll-into-view**: re-wire activation sounds manually; no visual highlight.
- **`Mono.CSharp` net40-on-net481**: low risk (WrathAccess ships it); confirm load at first `/eval`.

## Verification

Build: `dotnet build RTAccess.slnx -c Release` for players; **`-c Debug`** for harness sessions
([[rt-a11y-architecture-verdict]]). Close the game first (native DLL unlock).

Every phase is verified through the Phase 0 harness (`/eval`, `/speech`, `/gui`, `/input`,
`/screenshot`, `/loadsave`) + the blind dev's ears and `speech_log.txt`:
1. P0: `/health`, `/eval` arithmetic, `/speech` read-back.
2. P1: scratch `Panel` walked via the `Navigator`, composed announcements over `/speech`.
3. P1.5: confirm survivors still fire in mouse mode; settle the interactable-scan question in-game.
4. P2: `/loadsave` → `/gui` → `/input` drive Inventory open + equip; `/eval` confirms real state change.
5. P3: per screen — `/gui` shows the expected tree, `/input` reads correctly over `/speech`, activate
   performs the real action. Regression: surviving features ([[rt-tile-explorer]],
   [[rt-exploration-nav]], [[rt-bark-system]]) still work in mouse mode.