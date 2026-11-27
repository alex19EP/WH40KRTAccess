# Accessible Dev Console — design & implementation plan

Status: **researched, not started** (9-agent workflow sweep 2026-07-12; all engine facts below
cite decompiled sources; live-harness probes still pending are marked ⚠).

## Goal

A mod-owned, keyboard-driven, spoken console screen over the game's cheat system: type a
command line, hear autocomplete, browse the known-command DB, hear results and errors, recall
history. The game's own console exists but is unusable: `UberLoggerAppWindow` ("Uber Console",
bare BackQuote) is legacy IMGUI (`OnGUI` — invisible to NVDA) and every method early-outs
unless `BuildModeUtility.IsDevelopment` (`UberLoggerAppWindow.cs:128,179,203`). A parallel
mod screen over `CheatsManagerHolder.System` is the only viable architecture.

## Verified engine facts

### Command DB (the palette source)
- Built **unconditionally** at boot (`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`
  `CheatGameCommandSystem.Init`, `Kingmaker.Logging/CheatGameCommandSystem.cs:30-33`) — exists
  on every retail box with no startup.json.
- Registration is build-time **codegen**: each of 8 assemblies carries `CheatsCodeGen.AllCheats`
  (Code.dll alone: 314 methods + 30 variables). `CheatDatabase` scans assemblies on a background
  task at construction (`Core.Cheats/CheatDatabase.cs:27-57`).
- Per-command metadata (`CheatMethodInfo`): Name, Description, ExampleUsage, ExecutionPolicy,
  typed `Parameters[]` (Name/Type/HasDefaultValue) and — on the internal type — a human-readable
  `MethodSignature` like `void CreateItem(BlueprintItem blueprint, int quantity = 1)` — the
  ready-made spoken argument hint (`CheatMethodInfoInternal.cs:5-16`).
- `Database.GetKnownObjects()` returns the whole DB synchronously; its `Version` string changes
  on `SetExternals` — cheap cache invalidation (`KnownObjectsInfo.cs`, `CheatDatabase.cs:135`).

### The two-tier gating truth table (who gets which commands)
- `[Cheat]`-attribute commands (~314): **always available**. This is the whole reliable surface.
- Legacy `SmartConsole.RegisterCommand` "externals" (93: `iddqd`, `kill`, `teleport`, `gain_xp`,
  `toggle_fow`, `ironman_off`, …): **DOUBLE gate** — outer `if (BuildModeUtility.IsDevelopment)`
  around `CheatsCommon.RegisterCheats` + `CheatsRelease.RegisterCheats` (`Kingmaker/Game.cs:783-788`),
  inner per-family `if (BuildModeUtility.CheatsEnabled)` (`CheatsCombat.cs:61` etc.). Retail:
  both false → `Database.SetExternals(SmartConsole.CommandNames)` installs an **empty list**.
- `startup.json` **is stock** (shipped in depot 2186681, 22 bytes = `{"ForceLogging": true}`).
  `EnableCheats: true` alone changes exactly two things: the game's own REST server attempt
  (`http://*:35555`, 15 s after boot, LAN-facing, likely dies on Windows URL-ACL for non-admin)
  and name-based `-start_from`. **It does NOT register the externals** (outer gate unmet).
- `development: true` would register them but flips ~40 other `IsDevelopment` consumers
  (QA exception popups, achievement warning toasts, bug-report UI, a *different Photon
  version string* → co-op compat split, dev save behavior, the IMGUI console + its bare-
  BackQuote bind). **Not acceptable for a shipped mod.**
- **Self-provision path (documented, REJECTED)**: in-memory `BuildModeUtility.Data.EnableCheats = true`
  + publicized `CheatsCommon.RegisterCheats` / `CheatsRelease.RegisterCheats` +
  `Database.SetExternals(SmartConsole.CommandNames)` would register the externals on any box.
  **User decision 2026-07-12: vanilla path only** — the mod does not force the game's dormant
  cheat gates, and the install stays stock (`startup.json` reverted to its 22-byte retail
  content). The console's surface is exactly what vanilla retail exposes: the ~314 attribute
  commands + 30 variables; `GetKnownObjects().Externals` is treated as permanently empty.

### Parser / execution / results
- `CheatsParser.Execute(line)` regex-splits (quoted args supported), runs `[CheatArgPreprocessor]`
  @-token substitution, typed arg conversion, then dispatches: set-var / known command / external /
  bare-name get-var; unknown → `CommandNotFoundException` (`CheatsParser.cs:117-152`).
- Command execution enqueues `RunCheatCommandGameCommand` on `GameCommandQueue` with a
  TaskCompletionSource that completes (or faults) when the command runs on a later frame
  (`RunCheatCommandGameCommand.cs:63-103`). Never block on it; observing faults via
  `ContinueWith(OnlyOnFaulted)` is the `Dev/GameConsole.Observe` precedent.
- **Results and errors surface ONLY as "Console" LogChannel lines** ("Command result: {0}" /
  "Command ok" / `Logger.Exception`) — `CheatExecutorCommand.cs:30-50,84-100`.
- **Pause is not a problem**: `GameCommandQueue.Tick` runs unconditionally in `Game.TickInternal`
  outside the mode stack; `IsSimulationTick` is wall-clock (50 ms steps, `Time.unscaledDeltaTime`).
  Decisive proof: unpause is itself a queued `SetPauseGameCommand`. Commands typed while paused
  execute (~50 ms) and their output lands; only downstream world simulation (death processing,
  spawn materialization) waits for unpause (`Game.cs:943-989`, `GamesModeFactoryFacade.cs`).
  The only dead state is **loading** (own pump, `LoadingProcessCommandsLogic`).
- `SmartConsole.ExecuteLine(line)` is the canonical echo+history+execute wrapper: logs `">"+line`
  to the Console channel, awaits `CheatsManagerHolder.Instance.Execute`, appends to its static
  `CommandHistory` (`UberLogger/SmartConsole.cs:42-48`). We keep our own history but may reuse it
  for parity.

### Autocomplete
- `Parser.TryAutocomplete(piece)` — **synchronous**, main-thread, matches lowercased-input
  `Contains` against command/external/variable **names only** (never argument values), filtered
  by ExecutionPolicy. Quirk: `.Take(20)` runs **before** the relevance sort, so better matches
  past the first 20 dictionary hits are dropped (`CheatsParser.cs:47-80`). Mod-side ranking over
  `GetKnownObjects()` can do better if it matters.
- Blueprint-**argument** completion is feasible mod-side: `cheatdata.json` (`BundlesLoadService.BundlesPath`)
  → `BlueprintList{Name,Guid,TypeFullName}`; `Utilities.GetBlueprintNames<T>()` filters by the
  param's declared Blueprint type (`Kingmaker.Cheats/Utilities.cs:30-60,393-439`). ⚠ verify the
  file exists in the retail install before building on it.

### Variables
- `[Cheat]`-tagged static properties (`am_forcespeed`, `am_movetype`, `ignore_encumbrance`,
  `draw_fps`, `AutoCombat`, …). Bare name = get (value arrives as a Console log line);
  `name value` / `name = value` = set.
- **Mixed-case bug** (code-read ⚠): executor lookups `ToLower()` the input but dictionary keys
  are exact-case codegen names → `AutoCombat`, `SecondsToWaitAtStart` etc. appear unreachable
  through every parser path and never match autocomplete's case-sensitive `Contains`
  (`CheatExecutorVariable.cs:38,73` vs `CheatDatabase.cs:84`). The palette should invoke
  `((CheatPropertyInfoInternal)info).Methods.Getter/Setter` + `ArgumentConverter.Convert`
  directly, keyed by exact DB name — sidesteps the bug entirely.

### @-tokens (and their a11y problem)
- Game preprocessors (`Kingmaker.Cheats/CheatArgPreprocessors.cs:12-54`): `@selectedUnits` →
  joined UniqueIds of the selection; `@mouseover` → entity under the **physical mouse**;
  `@cursor` → `ClickEventsController.WorldPosition` (mouse click pos) as `x;y;z`.
- `@mouseover`/`@cursor` are useless to a blind player. The console should **pre-substitute
  mod tokens before handing the line to `Parser.Execute`**: `@scan` → scanner-selection entity
  UniqueId, `@tile` → tile-cursor world position `x;y;z` (G9 invariant). Only the raw-line
  route gets preprocessing — the pre-parsed `/command` route bypasses it entirely.

### Reading output — register our own sink (NOT `ConsoleLogSink.Poll`)
- `ConsoleLogSink.Poll(Guid)` **ignores its Guid and destructively drains the shared queue**
  (`ConsoleLogSink.cs:36-49`) — a screen polling it would steal entries from the dev server's
  `/log` and vice versa. Buffer is cut to **100 entries** in retail (`CheatGameCommandSystem.cs:39-42`).
- Fix: register a **mod-owned `ILogSink`** via `Owlcat.Runtime.Core.Logging.Logger.Instance.AddLogger(sink)`
  — the exact call `ConsoleInitializer` uses; `Logger.Log` fans every LogInfo to ALL sinks, so
  the two paths share no state (`Logger.cs:133-142,297-327`). Sanctioned precedent:
  `CutsceneLogSink` (`Code/Kingmaker.AreaLogic.Cutscenes/CutsceneLogSink.cs:38-46`).
- Sink contract: `Log` runs synchronously on the **calling thread** (any thread —
  `logMessageReceivedThreaded` producers exist) **inside the global logging lock**, and
  reentrant logs are silently dropped (`m_AlreadyLogging`). So: O(1) enqueue into a locked
  ring buffer, filter `logInfo.Channel.Name == "Console"`, copy fields out, **never** Speak
  or `Main.Log` from inside. Screen drains on the main-thread tick and speaks queued.
- No backfill for late sinks (the `populateWithExistingMessages` param is dead code) —
  register at `Main.Load` so history predates the first console open. Unregister via
  `Logger.Instance.RemoveLogger(sink)` on unload (Destroy() = no-op).

### Screen framework fit (all in-repo precedents)
- **Open/close**: LogReviewScreen shape — cached singleton child screen (`IsActive() => false`),
  toggled by walking the focused chain (`LogReviewScreen.cs:62-88`), pushed with
  `ScreenManager.Current.PushChild`. `Exclusive = true` mutes ALL game KeyboardAccess chords
  via KeyboardArbitration's ExclusiveActive lever (`KeyboardArbitration.cs:36`) — bare letters
  free with zero per-key work. `Wrap = true`, `KeepStateOnPop = true`.
- **Open chord**: bare **BackQuote** (genre standard). Retail registers a "Console" binding on
  Ctrl+Alt+Shift+BackQuote but **no consumer exists** (grep-verified dead), and the game's own
  bare-BackQuote bind only registers under IsDevelopment — BackQuote is free in retail
  (`KeyboardAccess.cs:482`, `IngameConsoleKeybinds.cs:10-33`). ⚠ cross-check the F10 keybind dump.
- **Input line (v1 = MessageBox TextField route, fully precedented)**: the input node's Enter
  raises `EventBus.RaiseEvent<IDialogMessageBoxUIHandler>(h => h.HandleOpen(prompt, BoxType.TextField,
  …, inputText: pending))` (SaveLoadScreen.NewSave shape, `SaveLoadScreen.cs:228-246`);
  MessageBoxScreen + TextEntry give TMP caret/IME/per-keystroke echo for free. **Works at the
  main menu** — CommonVM lives in the always-loaded `UI_Common_Scene`, recreated on every area
  load and return-to-menu (`RootUIContext.cs:483-497`, `GameStarter.cs:316`). Holes: requests
  are silently dropped while a loading screen is up (`CommonVM.cs:187`) and queue behind an
  already-open game box (`CommonVM.cs:190-214`) — detect `MessageBoxVM.Value != null` and warn.
- **Input line (v2 option)**: port WA `ModTextEntryScreen` (raw `Input.inputString` editor,
  `wotr-access/src/Screens/ModTextEntryScreen.cs`). Verified viable in RT **only** with:
  ≥1 declared graph node kept focused (holds `HasFocus` → GameInputLayerGate + EventSystemGate),
  `Exclusive = true` (typed printables aren't claimed chords), and WA's `_armed` guard so the
  opening Enter doesn't instantly confirm. KeyBindCaptureScreen's nodeless raw capture is the
  wrong tool — it exists to hand keys TO the game.
- **While typing**, mod dispatch already stands down (`InputManager.Tick` early-outs on
  `IsInputFieldSelected()` / `TextEntry.SuppressInput`, `InputManager.cs:150-159`).
- **Zones** (one BeginStop each, Tab cycles): `input` (edit node + Run + Complete), `output`
  (newest-first history, absolute-index keys so focus survives appends — LogReviewScreen recipe
  `LogReviewScreen.cs:97-131`), `palette` (command/variable DB). CounterWindowScreen is the
  small-modal precedent.
- **Speech**: drained output lines are passive → `Speak(…, interrupt: false)`; only direct key
  handlers interrupt (provenance rule). Autocomplete candidates via DrillMenuScreen chooser
  (`DrillMenuScreen.cs:26-31`).
- Double-speak with LogTap impossible: LogTap patches `LogThreadBase.AddMessage` (combat-log
  pipeline); the "Console" channel never flows there.

### Error taxonomy to speak
`CommandParseException` (unparseable), `CommandNotFoundException`, `CommandArgumentCountException`
(count outside `[firstDefault..max]`), `ExecutionPolicyException` (EditMode-only in play),
`ArgumentException` (conversion failure, includes param name + value). All arrive both on the
awaited Task and as Console-channel exception lines (`Core.Cheats.Exceptions/*`).

## Design decisions

1. **Ship target**: Release, localized, gated behind a ModSettings toggle (default OFF,
   "Developer console") + the BackQuote chord. Rationale: the attribute DB exists on every
   player's box; sighted players get the same surface by editing a documented file; blind
   players shouldn't need a sighted assistant to use cheats. (DEBUG-only would be the localization-
   exempt shortcut — rejected: ~12 ui.json keys is cheap.)
2. **Execution**: echo `"> " + line` into the output buffer ourselves, then
   `CheatsManagerHolder.System.Parser.Execute(line)` fire-and-forget with the `Observe` fault
   hook (GameConsole precedent). All results/errors arrive via our sink and are spoken queued.
3. **Output**: mod-owned `ILogSink` ring buffer (10k cap), registered at `Main.Load`.
4. **Palette**: `GetKnownObjects()` cached by `Version`; command nodes speak
   `name — signature`; Space reads Description + ExampleUsage; Enter prefills the input with
   `name `. Variable nodes: Enter reads the value via the **direct Getter delegate** (mixed-case
   bug workaround); a set-verb opens the TextField box and writes via Setter + ArgumentConverter.
5. **Autocomplete**: Complete-verb on the input node → `TryAutocomplete(pending)` →
   DrillMenuScreen; picking replaces the last token. Type-ahead search already works in the
   palette stop via NodeVtable.SearchText.
6. **History**: mod-side list; recent lines rendered in the output zone; Enter on a `>` echo
   line refills the input.
7. **Legacy externals (`iddqd` et al.): OUT OF SCOPE** — vanilla path per user decision.
   No self-provision toggle, no `BuildMode.dll` reference, no externals zone in the palette.
   `startup.json` reverted to stock (done 2026-07-12).
8. **@-tokens**: pre-substitute `@scan`/`@tile` (mod scanner/tile-cursor) before `Parser.Execute`;
   leave the game's own tokens intact for parity.
9. **Loading screens**: refuse to open (and refuse Run) while `RootUIContext.Instance.IsLoadingScreen`.

## Implementation phases

- **P1 — sink + screen skeleton**: `Accessibility/ConsoleSink.cs` (ILogSink ring buffer, registered
  in Main.Load); `Screens/ConsoleScreen.cs` (child screen, Exclusive, BackQuote toggle via
  `console.toggle` InputAction; zones: input/output; run via echo + `Parser.Execute` + Observe;
  OnUpdate drains sink → output list + queued speech). ui.json keys: `console.title`, `.input`,
  `.run`, `.complete`, `.output`, `.empty`, `.queued`, `.loading_blocked`; settings.json
  `bind.console.toggle`.
- **P2 — input & autocomplete**: TextField MessageBox editing (prefill pending), busy-box
  detection, Complete verb → DrillMenuScreen, history refill from `>` lines.
- **P3 — palette**: third BeginStop from `GetKnownObjects()` (commands + variables sub-groups),
  Space = full help, Enter = prefill; variable direct get/set.
- **P4 (v2)** — blueprint-arg completion from `cheatdata.json`; `@scan`/`@tile` substitution;
  possibly a StateCrawler tree screen (dumpstate as browsable UI).

## Live probes still pending ⚠

1. Mixed-case variable unreachability: `/eval` `CheatsManagerHolder.System.Database` keys vs a
   `getvariable` round-trip.
2. TextField MessageBox actually renders + accepts typing at the real main menu (statically
   proven; never exercised there — SaveLoadScreen only used it in-game).
3. `cheatdata.json` present in the retail install (needed for P4 arg-completion only).
4. BackQuote freedom against the live F10 keybind dump.
