# RTAccess — Accessibility Mod for Warhammer 40,000: Rogue Trader

Screen-reader accessibility mod for blind players. Speaks UI focus, menus, dialogue,
exploration, loot, character generation, and turn-based combat, and adds a self-built
keyboard layer + (deferred) spatial-audio soundscape. Sibling project to **WrathAccess**
(Pathfinder: WoTR) — reuse those patterns where they fit (see *Prior art* below).

## Game facts
- **Engine**: Unity **6000.0.64f1** (Unity 6), **Mono** scripting backend (not IL2CPP) →
  the game assemblies are fully decompilable and Harmony-patchable. Process name: `WH40KRT`.
  Steam **AppId 2186680**.
- **Install**: `C:\Program Files (x86)\Steam\steamapps\common\Warhammer 40,000 Rogue Trader`
  - Resolved at build time to `$(RogueTraderInstallDir)` in `GamePath.props` (git-ignored,
    auto-generated). If missing, the `GenerateCustomPropsFile` target derives it from the
    `Mono path[0]` line in the game's `Player.log`.
- **Managed dir** (reference assemblies): `<Install>\WH40KRT_Data\Managed`
  - `Code.dll` — game core, `Kingmaker.*` + `RogueTrader.*` namespaces (publicized).
  - `Owlcat.Runtime.UI.dll` — `ViewBase<TVM>` + the console navigation / focus system + MVVM base.
  - `Owlcat.Runtime.Core.dll` / `UniRx.dll` — the reactive properties driving the VM bindings.
  - `Rewired_Core.dll` — the input backend; `CountingGuard.dll` — the `Game.Keyboard.Disabled` guard.
  - `AstarPathfindingProject.dll` — `CustomGridNodeBase` grid walkability (the tile explorer reads it).
  - `0Harmony.dll` (bundled with the game), `Newtonsoft.Json.dll`, `LocalizationShared.dll`.
- **Game data dir** (LocalLow): `%LocalAppData%Low\Owlcat Games\Warhammer 40000 Rogue Trader`
  (note: **no comma** in "40000" here, unlike the install path). Holds `Player.log`, saves,
  the UMM mod folder, and our settings/logs.
- **Modding framework**: **Unity Mod Manager (UMM)**, manager version `0.25.0` — **NOT** the
  native Owlcat modification system (that's WrathAccess/WoTR). This is the single biggest
  divergence from the sibling project.
  - Mod lives in `<GameData>\UnityModManager\RTAccess\`.
  - Manifest is `Info.json`: `EntryMethod = RTAccess.Main.Load(UnityModManager.ModEntry)`,
    `AssemblyName = RTAccess.dll`.
  - Lifecycle is UMM's: `Load` (boot), `modEntry.OnUpdate` (per-frame — no custom `Ticker`
    needed, unlike WoTR), `OnToggle` (enable/disable from the UMM UI), `OnUnload`.
  - `OwlcatModificationManifest.json` is also carried, but only for the **Steam Workshop**
    publish path (`PublishToWorkshop`, appid 2186680) — the game is driven via UMM at runtime.
- **Target framework**: `net481` (.NET Framework 4.8.1). Needs the 4.8.1 targeting pack.
- **Harmony**: the game's own bundled `0Harmony.dll`. `Main.HarmonyInstance.PatchAll` on load.
- **Publicizer**: game assemblies are referenced with `Publicize="true"`
  (BepInEx.AssemblyPublicizer) so we can touch non-public game members directly.

## Decompiled reference (not in this repo)
- `decompiled/` — per-assembly ilspycmd output. **Git-ignored** (regenerable from the install),
  so the `justfile` is the source of truth for how to rebuild it.
  - `just support` — the libs the mod actually needs (UI/focus, reactive core, UniRx, Visual
    for the fog mask, SharedTypes, ModInitializer). The common case.
  - `just all` — **every game/dependency assembly the solution references** (mirrors the
    `RTAccess.csproj` `<Reference>` globs, minus the native-stub Unity engine modules); includes
    `Code.dll` / `RogueTrader.GameCore.dll`, so it's slow but makes all referenceable code available.
  - `just decompile <Name>` — a single assembly (into `decompiled/<Name>/`); `just decompile-glob
    '<pattern>'` for a wildcard; `just list` / `just check` for the Managed dir.
  - Requires `ilspycmd` (`dotnet tool install --global ilspycmd`) and `just` on PATH.

## Build & deploy
```
dotnet build RTAccess.slnx -c Debug
```
Debug build compiles `RTAccess.dll` and the `Deploy` target copies the whole output
(mod dll + `Info.json` + manifest + `assets/` + `prism.dll` + `nvdaControllerClient64.dll`
+ `Mono.CSharp.dll` + `NAudio.dll`) into `<GameData>\UnityModManager\RTAccess\` and zips it.
**The game must be closed** or the copy fails on the locked `RTAccess.dll`. Release
(`-c Release`) is the player build — the DEBUG-only dev harness and `Mono.CSharp` are compiled
out. Enable the mod once in the UMM in-game UI.

**Compile-only check (safe while the game is running)** — skips the `Deploy` target so it
never touches the UMM-locked DLL, and pins `SolutionDir` (the trailing `\` matters):
```
dotnet msbuild RTAccess.csproj -t:Compile -p:Configuration=Debug -p:SolutionDir=<repo-root>\
```

## Dev harness (Debug only)
- A **loopback HTTP dev server on port 8772** (all under `#if DEBUG`), gated on a marker file
  `<GameData>\RTAccess\devserver.enable` (survives Steam relaunches; an env var would not).
- Endpoints: `POST /eval` (Mono.CSharp C# REPL on the main thread), `GET /speech?since=`,
  `GET /screenshot`, `POST /loadsave`, `GET /health`. `/gui` + `/input` land in Phase 2.
- **Game console/cheat surface** (`RTAccess/Dev/GameConsole.cs`) mirrors the game's own retail-gated
  cheat REST plugins in-process (no `CheatsEnabled`/`startup.json` needed — the game builds
  `CheatsManagerHolder.System` + registers `ConsoleLogSink` unconditionally at boot): `POST /cheat`
  (raw command line via the game's parser — `@cursor`/`@mouseover`/`@selectedUnits` preprocessing),
  `POST /command`·`/external`·`/getvariable`·`/setvariable`·`/autocomplete`, `GET /known` (the ~314-command
  DB = a palette), `GET /bindings`·`/status`, `GET|POST /log` (drains the game's Console channel).
  `POST /dumpstate` takes a dotted path (`Game.Instance.Player`) or `{RootObjectPath,ExpandedChildren}`
  and returns a `StateCrawler` JSON object-graph tree. **Cheat exec is fire-and-forget** (enqueues on
  `GameCommandQueue`, runs over later frames — read results back via `/log`); never block on the Task.
- **`scripts/dev-game.ps1`** (and the **`/dev-game` skill`**) wrap the close → build → launch →
  verify cycle: `cycle` (default), `build`, `run`, `restart`, `kill`, `status`; plus `cheat`/`dump`/`log`
  (with `-Arg`) to hit the console surface. Launch is `steam://rungameid/2186680`.
- **Shared-harness rule**: do NOT drive the live game while another agent/session is driving it.
  Give the user manual test steps instead.

## Speech
- Primary backend: native **`prism.dll`** (+ `nvdaControllerClient64.dll` for the NVDA client),
  shipped beside the mod. `Speaker` is the facade; falls back to a stopgap TTS if Prism is absent.
  (`Prismatoid`, the managed wrapper, is net10 and unusable here — we hand-bind the native dll.)

## Logs
- `Main.Log` (`ModLog`) forwards to UMM's `ModLogger` **and** mirrors to `rtaccess_log.txt` in the
  mod folder — UMM keeps no on-disk log here and `Player.log` carries nothing from `ModLogger`, so
  without this mirror an early crash is invisible.
- `speech_log.txt` — chronological transcript of everything spoken (`[!]` = interrupted, `[+]` =
  queued); `focus_log.txt` — focus trace. Both reset on each new game / area load
  (`Logs.Init` / `ResetAll`).
- The game's own log is `<GameData>\Player.log`.

## Navigation strategy (decided — the graph paradigm)
We build a **mod-owned parallel accessible-UI tree**, NOT a ride on the game's console-nav /
gamepad focus ring (that pivot is committed; earlier console-nav experiments are retired).
`Screens.ScreenManager` resolves the active screen stack over `RootUiContext` each frame and
attaches the **`GraphNavigator`** — the pull-based key-graph core ported from WrathAccess
(`UI/Graph/`, BCL-only, unit-tested): the graph rebuilds per operation/frame, focus reconciles
by `ControlId` identity, and a focus change is **announced exactly once no matter what caused
it** (input, screen-moved focus, VM swap/rebuild). Never hand-write announce calls around
focus mutations — the frame differ owns that.
Screens come in two kinds:
- **Adapter (legacy)**: retained `Container`/`UIElement` trees with per-widget **Proxies**
  (`UI/Proxies/`) mirroring the game's live VMs; compiled by `TreeGraphAdapter` per frame.
- **Graph-native**: `BuildsGraph => true` + `Build(GraphBuilder)` declaring nodes fresh from
  live game state each render (immediate mode — node contents hold **NO view state**: read the
  game's own state, flip it via the game's own methods; read the SELECTION, not hover-poisoned
  reactives; some control state lives on game VIEWS, not VMs — reflect the live view).
**Policy: every NEW screen is born graph-native.** When a legacy screen migrates, the
VM-contract knowledge in its proxies moves into a node factory and the proxy is deleted once
its last user is gone (waves + WA recipe commits: `docs/plans/keyed-graphing-tarjan.md`).
Either way we read/activate by driving the game's own VMs and handlers, and a spoken
browse-label **mirrors what the card shows visually** (tooltip-only detail stays on Space).
Upstream sync rule: WA `graph-nav` deleted its element layer at HEAD — adapter-era files are
pinned at WA `4715f3d`; take only core `Graph/*` fixes, tests, and native-screen recipes.

**Keyboard ownership** is per-chord arbitration, not a blanket mute: `FocusMode` +
`KeyboardArbitration` suppress only the chords the mod claims each frame, and `GameKeybinds`
relocates the game's bare-letter service-window openers (C/I/J/M/L/Y/V/B/N) to Ctrl+letter via
the game's own keybinding-settings path — freeing the bare letters for exploration verbs while the
game's hint text auto-updates. See `docs/input-system-architecture-review.md`.

## Engine & domain facts
Load-bearing knowledge about how the game world works (all verified in-harness):
- **The world is one square `CustomGridGraph`, 1.35 m cells** (Owlcat's A* Pathfinding fork). But
  **exploration is real-time with continuous positions** — the grid is the pathfinding / occupancy /
  combat substrate and our tile cursor is a *scan overlay*, NOT how movement works. Only **turn-based
  combat** quantizes to cells. Move the party with `UnitCommandsRunner.MoveSelectedUnitsToPoint(worldPoint)`.
- **Walls = fences, not unwalkable tiles**: a thin wall / cover leaves both cells walkable and stores a
  height on the *edge* between them (`node.HasFenceWithNode`). LOS / cover via `LosCalculations`;
  `CoverVisualizer` is the game's own reusable "cover around me" reference.
- **Interactables are continuous world-space objects, not slotted per-tile.** Always interact through the
  game's own dispatch — `ClickMapObjectHandler.Interact(view.gameObject, units, forceOvertipInteractions:true)`
  (see `Exploration/ProxyMapObject.cs`). Locked / variative objects use the variative-interaction system
  (there is no `LockpickVM`).
- **Inspect readout**: raise `EventBus.RaiseEvent<IUnitClickUIHandler>(h => h.HandleUnitConsoleInvoke(unit))`,
  then read `…SurfaceHUDVM.InspectVM.Tooltip.Value` via `TooltipReader` (`Exploration/Inspect.cs`, key K).
  Opening Inspect **force-reveals** the unit persistently, so reading it aloud is sighted parity — no
  knowledge gating to preserve.
- **Reading VM text is field-first, not property-first.** Owlcat VMs expose their strings as `public
  readonly` **fields** — usually `ReactiveProperty<string>` (unwrap `.Value`) — not C# properties, so a
  `GetProperty`-only reflection scrape silently misses them; check field *or* property. Beware too that
  tooltip *brick* VMs are split across two namespaces (`Kingmaker.Code.UI.MVVM.VM.Tooltip.Bricks` vs
  `Kingmaker.UI.MVVM.VM.Tooltip.Bricks` — e.g. the item-header brick lives in the latter). `UiTextReader` /
  `TooltipReader` handle both.
- **Event narration: the game log is the single source of truth.** `Accessibility/LogTap.cs` (one postfix on
  `LogThreadBase.AddMessage`) voices all channels into the combat-event queue, minus owned streams (barks,
  warnings, dialogue) and a muted noise set — **decouple *captured* from *voiced***. `Screens/LogReviewScreen.cs`
  (bare L) reviews the history. Conviction (soul-mark) shifts are the one thing the game never logs, so they
  ride `ConvictionEvents` instead.

## Source layout (`RTAccess/`)
- `Main.cs` — UMM entry (`Load`), the per-frame `OnUpdate` tick, enable/disable/unload.
- `Speech/` — `Speaker` facade + Prism/stopgap backends. `Localization/` — mod-string tables.
- `Input/` — `InputManager` (registry + per-frame poll), `InputBindings` (the action set),
  `GameKeybinds` (the Ctrl+letter rebind), keyboard arbitration glue.
- `Screens/` (+ `Screens/CharGen/`) — the mod-owned screen tree resolved by `ScreenManager`.
- `UI/` — `Navigation` facade + `GraphNavigator`, `UI/Graph/` (the BCL-only key-graph core:
  KeyGraph, GraphBuilder, differ/announcer — pinned to upstream, tested by link from `tests/`;
  run `just test` or `dotnet test tests/RTAccess.Tests.csproj`, never the slnx),
  `TreeGraphAdapter` + `UI/Proxies/` (adapter-era per-widget VM adapters), `UI/Announcements/`.
- `Accessibility/` — event readers/announcers (`ExplorationEvents`, `BarkEvents`, `CombatEvents`,
  `WarningReader`, `ConvictionEvents`, `LogTap`, `SelectionAnnouncer`, `TileExplorer`, …).
- `Exploration/` — `WorldModel` (per-frame entity registry), `Scanner` (the review/scan cursor),
  `Targeting`, `Sonar` / `WallTones` (spatial audio), `Geo`, `MapCursor`.
- `Combat/`, `Buffers/` (Alt+arrow review buffers), `Settings/` (`ModSettingsRegistry` tree,
  persisted to `<persistentDataPath>/RTAccess`), `Audio/` (deferred, off-by-default spatial engine),
  `Dev/` (the DEBUG dev server), `Diagnostics/` (F9/F10/F12 dumps).

## Hard rules
- **Localize every string the mod speaks or displays** — no hardcoded English in `Speak` calls or
  labels. Mod text goes through `Localization.LocalizationManager` with an entry in
  `RTAccess/assets/locale/enGB/{ui,settings}.json` (enGB is the complete manifest; other languages
  are a dropped-in folder). Sole exception: debug-only tooling. Game content (names, log lines,
  tooltips) is already localized by the game — pass it through, never re-translate.
- **Drive the game's own method/handler for an action** — even when that spawns a dialog to make it
  accessible. Never reimplement a game flow from primitives.
- **A proxy's spoken browse-label mirrors what the game shows ON THE CARD**; tooltip-only info stays
  on Space (read the item VIEW to see which fields are bound).

## Conventions & gotchas
- **Speech interrupt is decided by provenance, not timing.** `Speaker.Speak(text, interrupt)` defaults to
  queue (`false`); pass **`true`** only when the line was caused by a keypress (our own key handlers). Passive /
  event speech and automatic game-set focus stay queued, so a keypress response never clips but background
  narration never cuts off what's playing. (`speech_log.txt`: `[!]` interrupted, `[+]` queued.)
- **The surface interactable layer is engine-dead in mouse mode.** `SurfaceMainInputLayer.OnUpdate` gates all
  its machinery on `!Game.Instance.IsControllerMouse`, and we boot in forced Mouse mode (`ConsoleMode.cs`) —
  which is *why* the scanner / cursor is self-built rather than riding the engine's interactable ring.
- **Parallel-tree screens leak via Unity's EventSystem.** The game's own views stay live beneath our overlay
  and react to `EventSystem` Submit / click on their selected button — a third input path that our navigator
  AND keyboard arbitration both miss. Suppressing chords is not enough: **ownership-gate at the game's action
  method** with a "mine now" flag (see `DialogChoiceGate` / `DialogChoiceGuard` for the dialogue-answer case).
- **Keep the custom raw-`Input` framework — do NOT migrate to the game's input system.** The game's keyboard
  layer is `KeyboardAccess` (itself a raw poller), not Rewired (which can't host new actions at runtime). This
  was settled with an adversarial review; full memo in `docs/input-system-architecture-review.md`.

## Prior art & reference
- Sibling mod **WrathAccess** (Pathfinder: Wrath of the Righteous, ~30k LOC) is the authoritative prior art
  for nearly every subsystem — but it uses the **native Owlcat mod system**, not UMM, so the loader / deploy /
  entry differ. It's cloned locally and can be consulted (ask for the path if needed). Ported steal-reports
  live at `docs/wotr-access-steal-report.md` and `docs/scanner-vs-wrathaccess-parity.md`.
- **`HOOKMAP.md`** (repo root) is the cross-checked map of the game's hook targets (10 subsystems, exact
  signatures). Osmodium's **SpeechMod** (MIT) is the authoritative RT TTS prior art it was built from.
