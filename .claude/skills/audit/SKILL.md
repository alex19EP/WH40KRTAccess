---
name: audit
description: Perform a full-codebase architectural audit of the RTAccess mod (not just a diff) — abstraction quality, game-model access invariants, localization, code reuse, complexity hotspots, screen/input/speech organization, and long-term maintainability. Use when the user asks for a codebase audit, health check, or architectural review of the whole mod.
---

# audit — RTAccess full-codebase audit

Perform a comprehensive audit of the **entire** RTAccess codebase, not just the working diff.
Focus on architectural health, abstraction quality, adherence to the project's invariants, and
long-term maintainability. This is the RTAccess-adapted port of WrathAccess's `/audit` — every
invariant below is RTAccess-correct (UMM, parallel mod-owned UI tree, Prism/stopgap), NOT WoTR's.

## Instructions

1. Read `CLAUDE.md` for project context and invariants (the **Hard rules**, **Conventions &
   gotchas**, and **Engine & domain facts** sections are the source of truth for what follows).
2. Scan all `.cs` files under `RTAccess/` with Glob and Grep, plus the locale JSON under
   `RTAccess/assets/locale/enGB/` (`ui.json`, `settings.json`).
3. Analyze each area below and report findings in the output format at the end.

## Audit Areas

### Architecture & Abstractions

**Missed abstractions:**
- Patterns repeated across 3+ files that should be a shared base class or utility?
- Switch statements or type checks that should be polymorphism?
- String-based lookups that should be typed (enums, constants, generics)?

**Over-abstraction:**
- Base classes or interfaces used by only one implementation?
- Layers of indirection that don't add value?
- Generics or patterns that make the code harder to follow without benefit?

**Inconsistent abstractions:**
- Do similar subsystems use different patterns for the same problem? (e.g. some screens poll in
  `OnUpdate`, others subscribe via `EventBus`; some proxies read live VMs, others cache at build.)
- Screen lifecycle compliance: screens are resolved by `Screens.ScreenManager` over `RootUiContext`
  each frame; they build their proxy tree, expose their screen identity, and let the `Navigator`
  own input. Flag screens that mutate the navigator's focus directly or duplicate navigator logic.
- Proxy consistency: per-widget Proxies (`UI/Proxies/`) mirror the game's live VM. Flag proxies
  that hand-roll what a shared proxy base or `ProxyChoiceCycler`/selection adapter already provides.

### Game-Model Access Rules (the invariants that keep us correct against the game's MVVM layer)

Each violation is a bug waiting to surface.

**Read live, not cached — and field-first, not property-first:**
- Proxies must read live computed VM values at announce time — never stash the game's reactive
  values (or our own copies) at build time. Scan proxies for fields capturing VM state that changes.
- Owlcat VMs expose their strings as `public readonly` **fields** — usually
  `ReactiveProperty<string>` (unwrap `.Value`) — **not** C# properties. Flag any `GetProperty`-only
  reflection scrape that would silently miss field-backed text. `UiTextReader`/`TooltipReader` check
  field *or* property; new readers must too. Beware tooltip *brick* VMs split across two namespaces
  (`Kingmaker.Code.UI.MVVM.VM.Tooltip.Bricks` vs `Kingmaker.UI.MVVM.VM.Tooltip.Bricks`).
- Tooltip-carrying APIs resolve a `Func` factory per drill-in; flag cached built tooltip templates.

**Drive the game's own method/handler — never reimplement a flow from primitives:**
- Actions must invoke the game's own dispatch, even when that spawns a dialog to make it accessible:
  interact via `ClickMapObjectHandler.Interact(...)`, issue combat via `unit.Commands.Run(...)` /
  the game's `SetAbility`+`OnClick`, raise `EventBus.RaiseEvent<IHandler>(...)`. Flag any Unity
  scene-hierarchy hunting for a control's `OnClick`, or any hand-rolled reimplementation of a game
  flow (movement, inventory writes, dialogue answer, save/load) from primitives.
- Check the game view's real click path for side effects (UI sounds, view-owned state) the VM call
  alone won't reproduce; flag activations that skip a sound or a guard the real handler has.

**Surface only what's visible (visual parity — never reveal what a sighted player can't see):**
- A proxy's spoken browse-label mirrors what the game shows **ON THE CARD**; tooltip-only detail
  stays on Space (read the item VIEW to see which fields are bound). Flag labels that invent
  indicators from hidden VM data.
- Spatial/exploration readouts must be gated on fog: `FogProbe.Classify` (NeverSeen → "unexplored";
  Explored → layout, no creatures; Visible → full). Flag any tile/entity readout that bypasses fog
  gating (the optional X-ray toggle, default OFF, is the sanctioned exception).
- Combat masks: respect `HideRealHealthInUI` — flag ungated enemy HP/buff readouts (`UnitBuffer`,
  `ProxyUnit`) that leak numbers the game hides. Mirror view-side `IsVisible`/comparer filters
  rather than iterating the raw VM collection.

**Reflection & publicizer:**
- Game assemblies are referenced `Publicize="true"`, so touch non-public game members **directly** —
  flag contortions (reflection, wrappers) made just to reach a member the publicizer already exposes.
- Where reflection is still used, prefer public/publicized API; list all reflection sites with what
  they touch and flag any where a clean compiled path exists.

### Localization & Message Usage

**The hard rule:** every string the mod **speaks or displays** must come from the locale tables —
`Localization.LocalizationManager` with an entry in `RTAccess/assets/locale/enGB/{ui,settings}.json`
(enGB is the complete manifest). Sole exception: **debug-only tooling** (`Player.log`/`rtaccess_log`
output, dev hotkeys, the dev server). Game content (names, log lines, tooltips) passes through —
never re-translate.

- Scan for hardcoded English in `Speaker.Speak("...")` / `Speak("...")` calls, proxy label
  arguments, screen/help labels, and settings labels (which go through `settings.json`).
- Cross-check the locale JSON both ways: keys referenced in code but **missing** from enGB (silent
  fallback at runtime), and **dead** keys in the JSON no longer referenced by code.
- Flag resolved-then-frozen strings: a localized string fed into `string.Join`/`+`/interpolation and
  re-wrapped can't re-translate on a language switch. Resolve only at output boundaries (handing text
  to `Speaker`, a log line, a `{var}` template substitution).

### Code Reuse

**Duplicated logic across files** — search these hot directories for similar blocks:
- `RTAccess/UI/Proxies/`, `RTAccess/Screens/` (+ `Screens/CharGen/`), `RTAccess/Exploration/`,
  `RTAccess/Accessibility/`, `RTAccess/Audio/`, `RTAccess/Buffers/`.
- Focus on: announcement assembly, VM unwrapping (`.Value` chains), `EventBus`
  subscribe/unsubscribe, distance/bearing math (`Exploration/Geo`), settings-tree building
  (`Settings/ModSettingsRegistry`), tooltip brick reads.

**Underused utilities:**
- Helper methods living in one class that should be shared (label stripping, position/bearing math,
  VM lookups duplicated across `Scanner`/`WorldModel`/overlay systems, fog classification).
- Announcement-part logic duplicated between proxies that a base proxy should own.

### Complexity Hotspots

- **Files over ~300 lines** — should they be split? (Screens that grew content-building, multi-tab
  builders, `Main.cs` growth.)
- **Methods over ~50 lines** — should they be decomposed?
- **Overly complex methods** — deep nesting (3+ levels), many parameters (5+), or reflection +
  try/catch + business logic tangled in one block.
- Classes with mixed responsibilities.

### Screen, Input & Speech Organization

**Screen coverage:**
- List the game's major UI surfaces (`RootUiContext`-driven: dialogue, loot, inventory, vendor,
  service windows, book/tutorial events, save/load, transition/area map, combat HUD, char gen /
  level-up, colony/voidship) and whether the mod has a Screen for each. Distinguish **deliberately
  deferred** (note it) from **missed**.
- Screen classes doing too much that should delegate to content builders or per-phase classes.

**Input** — the custom raw-`Input` framework is deliberate; do NOT flag it for "should use the
game's input system" (that was settled adversarially — see `docs/input-system-architecture-review.md`):
- User-facing actions live in `InputBindings` and dispatch through `InputManager` (registry +
  per-frame poll). Flag raw `Input.GetKeyDown` polling scattered **outside** the sanctioned spots.
- **Typing-safety:** poll dispatch must stand down while a text field is focused — flag hotkey
  pollers that would fire (and corrupt input) during name entry / search boxes.
- **Keyboard ownership is per-chord arbitration, not a blanket mute:** `FocusMode` +
  `KeyboardArbitration` suppress only the chords the mod claims each frame; `GameKeybinds` relocates
  the game's bare-letter openers to Ctrl+letter via the game's own keybinding path. Flag any
  blanket keyboard-disable or any bare-letter claim that skips arbitration.
- **Parallel-tree leak:** the game's own views stay live beneath our overlay and react to Unity
  `EventSystem` Submit/click — a third input path the navigator AND arbitration both miss. Flag
  game-action methods reached by our overlay that are **not** ownership-gated with a "mine now" flag
  (pattern: `DialogChoiceGate`/`DialogChoiceGuard`).

**Speech:**
- Primary backend is native **Prism** (`prism.dll` + `nvdaControllerClient64.dll`); the managed
  `Prismatoid` wrapper is net10 and unusable — flag any attempt to use it or to add a
  `System.Speech`/managed-COM path. Falls back to the stopgap TTS when Prism is absent.
- **Interrupt is decided by provenance, not timing:** `Speaker.Speak(text, interrupt)` defaults to
  queue (`false`); pass **`true`** only when the line was caused by a keypress (our own key
  handlers). Flag `interrupt: true` on passive/event/auto-focus speech, and flag keypress responses
  left on the default queue where they should interrupt.
- **Event narration = the game log is the single source of truth:** voicing should funnel through
  `Accessibility/LogTap` (one postfix on `LogThreadBase.AddMessage`), decoupling *captured* from
  *voiced* (minus owned streams + the muted noise set). Flag new per-channel event taps that
  duplicate what LogTap already funnels (conviction/soul-mark shifts are the one unlogged exception).

### Long-term Concerns

**Fragility:**
- Engine is Unity **6000.0.64f1**, **Mono** backend, game assemblies **publicized**. Reflection
  sites and per-instance prefab/VM wiring assumptions should be listed so they're known.
- What breaks if a screen's VM is null mid-transition? Scan `.Value` chains for missing null guards
  on screens/proxies that can close under us (Owlcat dispose+recreate VM lifecycle).
- **Mouse-mode gate:** the surface interactable layer is engine-dead in mouse mode
  (`SurfaceMainInputLayer.OnUpdate` gates on `!IsControllerMouse`, and we boot forced Mouse mode) —
  flag any code that assumes the engine's interactable ring is live.

**UMM / Harmony constraints:**
- The mod is **UMM** (Unity Mod Manager), **not** the native Owlcat modification system. Lifecycle
  is UMM's: `Main.Load` (boot), `modEntry.OnUpdate` (per-frame — **no custom Ticker**), `OnToggle`,
  `OnUnload`. Flag new MonoBehaviours/coroutines/tickers created outside the `OnUpdate` tick.
- Harmony is the game's **bundled `0Harmony.dll`** (`Main.HarmonyInstance.PatchAll` on load). Flag
  Harmony API usage beyond what the bundled version supports.
- The Deploy target copies a specific file set into `<GameData>\UnityModManager\RTAccess\` — flag
  build/deploy changes that would drop a needed native dll (`prism.dll`,
  `nvdaControllerClient64.dll`, `NAudio.dll`, `Mono.CSharp.dll`) or omit `assets/`.
- **DEBUG-only surfaces** (dev server on port 8772, `Mono.CSharp` REPL, diagnostics dumps) must stay
  under `#if DEBUG` — flag dev tooling that would ship in a Release/player build.

**Compiler warnings:**
- Compile and check for warnings. The build should be **0 warnings, 0 errors**. Do NOT suppress with
  `#pragma`/`[SuppressMessage]` — fix the underlying issue.
- **Safe while the game is running**, use the compile-only check (skips the Deploy target so it never
  touches the UMM-locked DLL; the trailing `\` on `SolutionDir` matters):
  ```
  dotnet msbuild RTAccess.csproj -t:Compile -p:Configuration=Debug -p:SolutionDir=<repo-root>\
  ```
  A full `dotnet build RTAccess.slnx -c Debug` deploys but requires the game to be closed.

**Technical debt:**
- TODO comments and known workarounds.
- Leftover temporary `[tag]` debug logging that should have been stripped after diagnosis.
- Commented-out code blocks that should be removed or restored.
- English glue literals awaiting locale-table entries (known pending work) — inventory them.

## Output Format

### Summary
- **Critical issues**: [count] — architectural problems or invariant violations that will cause
  bugs / maintenance pain.
- **Improvements**: [count] — refactoring opportunities.
- **Notes**: [count] — minor observations.

### Findings (grouped by severity)
For each finding:
- **Area**: which audit area it falls under
- **File(s)**: affected files (use `file_path:line` — it's clickable)
- **Description**: what the issue is
- **Impact**: why it matters for long-term health
- **Suggestion**: concrete recommendation
