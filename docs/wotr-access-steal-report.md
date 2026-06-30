# What RTAccess Can Steal From WrathAccess

A prioritized port-planning report for the RTAccess maintainer.

## Executive summary

WrathAccess (the Pathfinder: Wrath of the Righteous accessibility mod) is a mature,
broad screen-reader codebase covering whole subsystems RTAccess has not started: a
fallback TTS stack, a managed spatial-audio engine, a combat/event announcement
pipeline, an orthogonal review-buffer framework, a tunable announcement composer with
a settings cascade, and a loopback dev server purpose-built for a blind author working
with an AI co-driver. Because both games are Owlcat titles on Unity + Mono sharing the
`Kingmaker.*` and `Owlcat.Runtime.UI.*` roots, the *patterns* and a surprising amount of
engine-generic code (Prism/SAPI binding, NAudio DSP, Mono.CSharp eval, settings tree,
buffer ring) port cleanly — but **member signatures, rule types, and content must be
re-verified against RT's split assemblies** (`Code.dll` UI + `RogueTrader.GameCore.dll`
mechanics).

The biggest wins for RT are: (1) a **fallback TTS stack** (SAPI 5 + clipboard) so the
mod stops going silent for players without NVDA/JAWS; (2) a **managed spatial-audio
engine** (NAudio sonar/wall-tones) — RT is speech-only today and its square grid makes
bearing/distance *easier* than WOTR's navmesh; (3) the **combat/event announcement
pipeline**, RT's single largest planned-not-built gap; (4) the **review-buffer
framework**, the most reusable single thing in the catalog; and (5) the **dev server**,
uniquely high-leverage for a sightless author + AI loop.

**Two key porting caveats apply to nearly everything:** RT is **turn-based only** (single
`TurnController`, AP-yellow/MP-blue economy) so combat features assume sequential
resolution, not WOTR's real-time-with-pause bursts; and RT's world is a **square
`CustomGridGraph` tile grid**, not a navmesh, so every spatial feature (sonar, wall
tones, path prediction, room segmentation) should be *rebuilt on the grid* — which is
generally cheaper than WOTR's navmesh traces, never a literal port. A third structural
caveat: RT **rides the game's own console nav** (forced console mode + `SetFocused` +
`VirtualNavItem` injection) where WOTR built a parallel mouse-mode nav tree — so WOTR's
entire parallel-UI framework (Screens/UIElement/proxies) is **inspiration-only**, and the
real steals are the engine-generic pieces that sit *on top* of either nav strategy.

---

## Top steals

Sorted by priority then effort. Only includes items RT does not already fully have (or
that are drop-in). Capped at the ~26 highest-value.

| Feature | Subsystem | RT state | Portability | Effort | Priority |
|---|---|---|---|---|---|
| Geo spatial readout helpers (distance/compass/Live-position) | Exploration core | partial | drop-in | S | high |
| Where-am-I readout (area/indoor/3×3 region) | Exploration core | partial | adapt | S | high |
| Buffer base class + BufferManager ring | Review buffers | missing | drop-in | S | high |
| Unit death announcement | Event system | missing | adapt | S | high |
| Typing-safety stand-down (`IsInInputField` guard) | Input & keybindings | missing | adapt | S | high |
| WarningReader (speak refusal/AP-MP toasts) | Dev tooling | missing | adapt | S | high |
| /speech read-back tap (ring buffer at Speaker chokepoint) | Dev tooling | partial | adapt | S | high |
| Auto/fallback backend chain (Prism→SAPI→Clipboard) | Speech & TTS | partial | adapt | M | high |
| NAudio single-mixer positional engine | Spatial audio | missing | adapt | M | high |
| Staggered L→R sonar sweep | Spatial audio | missing | adapt | M | high |
| ListenerAnchor virtual audio head | Exploration core | missing | adapt | M | high |
| ScanTaxonomy (drives ShouldAnnounce filter) | Exploration core | partial | adapt | M | high |
| Faction-segmented tactical review cycles | Exploration core | missing | adapt | M | high |
| Turn-based path info on cursor (tiles/MP cost) | Spatial overlays | missing | rebuild | M | high |
| Two-axis review-channel framework (Alt+L/R, Alt+U/D) | Review buffers | missing | adapt | M | high |
| UnitBuffer (live HP/defenses/AP-MP/buffs) | Review buffers | missing | rebuild | M | high |
| Damage-taken announcement | Event system | missing | adapt | M | high |
| Buff/debuff gain-loss reconciler (de-noised) | Event system | missing | adapt | M | high |
| Typed persistent settings tree + dot-path JSON | Settings & loc | missing | drop-in | M | high |
| Poll-and-diff context resolver (universal screen-change announce) | Screens | partial | adapt | M | high |
| TypeAheadSearch first-letter list search | UI navigation | missing | adapt | M | high |
| TextEntry / name-entry via live TMP_InputField | UI navigation / CharGen | missing | adapt | M | high |
| Loopback dev server (+ /eval, /gui, /screenshot, /loadsave) | Dev tooling | missing | adapt | M | high |
| SAPI 5 fallback handler (+ ComDispatch IDispatch COM) | Speech & TTS | missing | adapt | L | high |
| Turn-based combat readouts (turn order / AP-MP / R-key) | Exploration core | missing | adapt | L | high |
| Log narrator (single Harmony tap on shared log base) | Spatial overlays | missing | adapt | L | high |
| EventBus→ModEvent→dispatcher announcement framework | Event system | missing | adapt | L | high |
| Announcement composition framework (typed parts + verbosity cascade) | UI proxies / Tooltips | partial | adapt | L | high |

---

## Per-subsystem sections (priority order)

### 1. Speech & TTS

**Verdict:** RT already owns the core (native Prism behind an `ISpeech`/`Speaker` facade,
and its `NavInputProbe` provenance interrupt model is *more* advanced than WrathAccess's
queue-by-default). Do **not** regress the facade policy. The real high-value steal is the
**fallback stack** RT is missing — today RT goes silent for any player without NVDA/JAWS.

- **SAPI 5 fallback handler (`SapiHandler` + `ComDispatch` IDispatch COM client)** — direct
  Windows SAPI 5 TTS via a from-scratch IDispatch COM client (CoCreateInstance P/Invoke,
  hand-marshalled 24-byte x64 VARIANTs) — *missing / adapt / L / high*. **Headline steal.**
  Pure OS-facing code, zero Owlcat/grid/turn coupling; net481/Mono lacks managed COM
  activation exactly like WOTR, so `ComDispatch` ports nearly verbatim. Wrap as
  `SapiSpeech : ISpeech` behind `Speaker`. Map SAPI purge-before-speak onto `interrupt:true`.
- **Auto/fallback backend chain (Prism→SAPI→Clipboard)** — load-on-demand roster behind one
  contract with Detect/Load + `SupportsAtRuntime` gate — *partial / adapt / M / high*.
  Generalize `Speaker` from "try Prism else `<none>`" into an ordered roster. Keep RT's
  superior provenance interrupt at the facade. Prerequisite for SAPI + clipboard.
- **Clipboard last-resort handler** — writes spoken text to `GUIUtility.systemCopyBuffer` —
  *missing / drop-in / S / medium*. Identical on RT's Unity build; terminal link in the chain.
- **Braille via Prism `output()`** — route through `output()` (drives braille displays) with a
  one-time feature-bitmask check — *partial / adapt / S / medium*. RT's `PrismSpeech` already
  binds `prism.dll`; just add the `output()` path. Cheap deaf-blind win.
- **Render-to-PCM + world-positioned spatial speech** — second `SpVoice` rendering to PCM,
  mixed/panned through NAudio — *missing / rebuild / XL / medium*. Strategically a *better*
  fit on RT's square grid (TileExplorer already has dx/dz), but gated on the SAPI render
  path (Prism can't render PCM) + a new NAudio dep. Port the second-voice isolation /
  format-pinning / volume-boost techniques; build RT-grid spatialization fresh.
- *Skip / low:* named multi-config voices (premature, gated on unbuilt UMM settings),
  TMP-strip Tts facade (RT's Speaker is more capable), DEBUG ring buffer (RT has SpeechLog).

### 2. Spatial audio engine

**Verdict:** RTAccess is purely speech-based with **no spatial audio** — its single biggest
capability gap and highest-upside area. RT's square `CustomGridGraph` makes bearing/distance
*trivial* (no navmesh trace), and RT already enumerates the interactables/units/cover these
features consume. Steal NAudio + sonar + earcons first; grid-adapt wall tones next. Defer the
entire Wwise half (bank-specific ids must be re-extracted from RT's banks).

- **NAudio single-mixer positional engine (constant-power pan + WAV cache)** — one
  `MixingSampleProvider` → one `WaveOutEvent` (100ms buffer riding GC pauses), self-removing
  panned one-shots — *missing / adapt / M / high*. **Foundation for every audio steal.** DSP
  is game-agnostic; deploy NAudio 1.10.0 (net35 single DLL) beside the mod like `prism.dll`.
  Use TileExplorer cursor / party-leader tile as listener point.
- **Sonar sweep (staggered L→R, adaptive ping gap)** — pings nearby items L→R, distance→volume,
  bearing→pan, gap = `clamp(K/count,min,max)` — *missing / adapt / M / high*. Feed from RT's
  surrounding-interactables set / LandmarkNav markers / TileExplorer units. Doubles as enemy
  radar in TB combat. Needs the NAudio engine first.
- **Directional wall tones (4 cardinal proximity voices)** — looping N/S/E/W voices rising as a
  wall nears — *missing / rebuild / M / medium*. The NAudio voice is drop-in; the *driver*
  rebuilds on the grid: step tiles N/S/E/W until `!node.Walkable` / wall-cut-edge (data
  TileExplorer already queries), distance = steps × 1.35m. **Easier on a square grid.**
- **Non-positional UI earcons** — short chimes for focus/window/turn change — *missing /
  drop-in / S / medium*. `PlayOneShot` no-pan; wire to SetFocusedPatch / ServiceWindowAnnounce /
  IAreaActivationHandler / ITurnStartHandler. Trims serial-reader verbosity.
- **IAudioEngine backend seam** — one interface both NAudio and a future Wwise impl satisfy —
  *missing / adapt / S / medium*. Keep even shipping NAudio-only; preserves a later Wwise option.
- **Phase-continuous tone oscillator (`Osc`)** — *missing / drop-in / S / low*. Drop-in primitive,
  but WOTR's elevation use maps poorly to RT's flat maps — repurpose for an AP/MP/cover scalar.
- *Defer (Wwise half, all low):* 3D-emitter backend, runtime v135 bank generator, virtual-head
  listener, live-swap resolver — brilliant but every hard-coded id is WOTR-bank-specific.

### 3. Event system (combat / event announcement pipeline)

**Verdict:** RT has **no** combat/event-feedback layer and combat accessibility is its
single largest planned-not-built gap — so this is the **most valuable subsystem in the whole
catalog**, stolen as a complete architecture. RT already uses the exact idiom (one EventBus
subscriber implementing several handler interfaces). Reuse RT's `Speaker` + provenance queue;
adapt signatures to `RogueTrader.GameCore` rule types + `BaseUnitEntity` faction/visibility.

- **EventBus→ModEvent→dispatcher framework (whole architecture)** — one persistent subscriber
  emits immutable `ModEvent`s; a dispatcher queues + flushes once/frame in arrival order —
  *missing / adapt / L / high*. **The headline.** Single-subscriber pattern is drop-in; the
  ModEvent/dispatcher/registry scaffolding is the new part. Combat reads are passive →
  `interrupt:false` → queue. Drive Tick from `modEntry.OnUpdate`.
- **Damage-taken announcement** — localized target name + amount off the damage rule —
  *missing / adapt / M / high*. **Carry over the critical gotcha:** rulebook rules must
  subscribe via `IGlobalRulebookHandler<T>` (carries `IGlobalRulebookSubscriber`), **not** bare
  `IRulebookHandler<T>`, or `RulebookEventBus.Subscribe` silently never fires. Verify RT's rule
  type name in GameCore.
- **Unit death announcement** — `IUnitHandler.HandleUnitDeath` — *missing / adapt / S / high*.
  Pair with the per-source classifier so "enemy died" vs "ally died" read distinctly.
- **Buff/debuff gain & loss with per-frame de-noising reconciler** — HashSet active-set +
  frameAdds/frameRemoves so re-applies are silent, hidden/empty-name buffs skipped — *missing /
  adapt / M / high*. **High-leverage:** RT combat is buff-heavy and churns each turn; naive
  add/remove would spam. Reconcile *before* the dispatcher flush.
- **Healing announcement (clamped actual HP)** — *missing / adapt / S / medium*. Read clamped
  Value, silent on zero so regen ticks don't spam.
- **Per-source classification (party/enemy/neutral/sourceless)** — `[Flags]` enum + `Classify()`
  by faction — *missing / adapt / S / medium*. Single-voice RT can ship spoken prefixes
  ("Enemy"/"Ally") instead of separate voices.
- **Visibility/perceptibility gating** — enemy/neutral events read only when visible, try/catch
  default-audible — *missing / adapt / S / medium*. Map onto RT fog-of-war / `IsVisibleForPlayer`.
- **Frame-batched non-interrupting flush buffer** — *partial / adapt / S / medium*. RT has a
  non-interrupting queue but no per-frame ordering; add so one turn-resolution reads as one
  ordered batch.
- *Skip / rebuild:* settings-backed inherit config (gated on unbuilt UMM settings); positional
  3D combat speech (RT Prism can't render PCM, TB is sequential — prefer spoken tile-grid
  directions); ability-score drain (no 40K analog — keep only the localized-stat-table technique).

### 4. Exploration core (world model, scanner, targeting, combat readouts)

**Verdict:** The largest, most game-coupled WOTR subsystem — its concrete proxies, navmesh
RoomMap and global-map stack are near-total rewrites on RT. But the framework/geometry/audio
scaffolding shares RT's Owlcat lineage and is high-leverage. RT already owns interactable-nav
and party-select (have/low). Defer RoomMap and the star-map parallel.

- **ListenerAnchor — virtual audio head** — re-snaps the single Wwise `DefaultListener` (via
  `ObjectRegistry`) onto cursor/leader, fixed north, each frame — *missing / adapt / M / high*.
  **Best high-leverage steal here.** Hook ports almost verbatim; host from `Main.Load`.
  Foundational compass-stable frame for any sonar.
- **ScanTaxonomy — one two-level tree** driving nav + sound + announce + settings — *partial /
  adapt / M / high*. **Directly powers RT's planned `ShouldAnnounce` filter**
  (`docs/plans/abundant-mixing-diffie.md`). Classify off `InteractionPart` family +
  `PartUnitFaction`; keep many-to-many (a lootable corpse = enemy AND container); add 40K nodes
  (Veil/psychic, cogitator/vox, servitors). Formalize InteractableDescriber's ad-hoc verbs.
- **Geo — spatial readout helpers** (MechanicsDistance, compass bearing, vertical, `Live()`
  reads `view.transform.position` not lagged `entity.Position`) — *partial / drop-in / S / high*.
  Cheapest broad win; reused by every announce path. Drop WOTR's feet/miles conversions (40K =
  metres).
- **Where-am-I readout** (area/section, indoor flag, 3×3 compass region) — *partial / adapt / S /
  high*. RT already reads area name via `IAreaActivationHandler`; add indoor flag + region from
  leader position within area bounds. Omit room-id until a segmenter exists.
- **Faction-segmented tactical review cycles** (party/enemies/neutrals/POI separately, nearest-
  first, fog-gated, with ping) — *missing / adapt / M / high*. RT's ExplorationNav cycles the
  ring uniformly; faction-segmented adds threat awareness pre-combat. Needs the proxy registry
  (or a per-frame `Game.Instance.State` scan) + ListenerAnchor for the ping.
- **Turn-based combat readouts** (whose-turn / turn-end / action economy / delay/end-turn) —
  *missing / adapt / L / high*. **Biggest blind-player gap, squarely in scope (RT is TB-only).**
  Subscribe `ITurnStartHandler`/`ITurnBasedModeHandler`. The R-key reads RT AP
  (`ActionPointsYellow`) + MP (`ActionPointsBlue`) from `PartUnitCombatState`, **not** WOTR's
  reflected `GetActionsStates`. Pattern ports; every binding rebuilt.
- **Accessible ability/action targeting + `ITargetingMode` framework** — *missing / rebuild / L /
  high*. Keep the pluggable-mode + live-`Active` pattern; rebuild bindings: RT issues via
  `unit.Commands.Run(UnitCommandParams)`/`AbilityData`, targets from the grid cursor.
- **WorldModel — persistent proxy registry** (per-frame diff into one stable proxy/entity) —
  *partial / adapt / L / medium*. Worth building only when you add sonar (looping sounds need
  stable instances) or whole-area distance-sorted scan.
- **ScanSounds / Scanner review cursor / ScanBounds** — *missing-partial / adapt-drop-in / S–L /
  medium*. `ScanBounds` (pure Vector math, models multi-tile IntRect footprints) is drop-in;
  Scanner + ScanSounds depend on registry + taxonomy + Geo.
- **Reachability gating + path prediction** — *partial-missing / inspiration-only / M / medium*.
  Steal the *idea*: flood the `CustomGridGraph` over walkable cells so a closed door reads "no
  path" and move-to gives a spoken refusal, not a silent clamp. Compute path length/MP on the
  grid (ABPath/BFS) and announce before commit.
- **Two-cursor discipline** (review cursor vs one shared movement cursor) — *partial /
  inspiration-only / S / medium*. RT has both halves (ExplorationNav ~ review, TileExplorer ~
  movement) but not unified; have targeting + future move-to read ONE shared grid cursor.
- *Defer / low:* RoomMap watershed (XL — run clearance+watershed on the grid, skip triangle
  raster), room-exit cycle, camera pan/rotate (low-vision), prefab-name humanizer (fallback
  only), global star-map stack (concept port), curated AreaDetails JSON (content-bespoke).

### 5. Spatial overlays & cues

**Verdict:** RT already owns the tile/point **readout** half (TileExplorer, ExplorationNav,
LandmarkNav, InteractableDescriber), so those composers are low-value duplicates. The genuine
steals are the **non-speech audio layer** RT lacks entirely, plus the log narrator.

- **Pluggable audio-engine abstraction + NAudio mixer + ToneEngine** — *missing / adapt / L /
  high*. Gating prerequisite (same NAudio engine as subsystem 2). Add the NAudio ref to the
  csproj. Keep the Classic NAudio path authoritative; Wwise later/optional.
- **Staggered L→R sonar sweep** — *missing / rebuild / L / high*. World-position based, so
  grid-vs-navmesh irrelevant; feed from RT's surrounding-interactables set. Nearest-shape-point
  maps onto IntRect footprints (a wall reads along its length).
- **Turn-based path info on the cursor** ("Path, N tiles / out of remaining MP / No path") —
  *missing / rebuild / M / high*. **Near-perfect RT fit:** TileExplorer already moves the cursor
  in combat but doesn't read path cost. Rebuild `CombatMode.TryPathInfo` on `TurnController` +
  `CustomGridGraph`; report tiles/MP, not feet/standard-action.
- **Log narrator (single Harmony tap on shared log base + categorized toggles)** — *missing /
  adapt / L / high*. **RT's combat-log narration is an explicitly named unbuilt gap.** "One
  postfix on the shared `LogThreadBase.AddMessage` funnels every thread" is the clean
  architecture (avoids per-thread resubscribes); re-map the thread-class taxonomy to 40K
  (psychic/Veil, momentum, profit-factor, ship/space). Verify RT's `CombatLog` thread base. Log
  lines queue (don't interrupt).
- **Wall tones (4 cardinal)** — *missing / rebuild / M / medium*. Adapts naturally — cardinal
  directions ARE the grid axes; reuse the cover/wall-edge data TileExplorer already queries.
- **Virtual-head listener (relocatable Wwise listener)** — *missing / adapt / M / medium*. Only
  matters with the Wwise backend; NAudio flat-pan needs no listener move. Sequence after NAudio.
- **Composable overlay framework / per-system play modes / settings inheritance / scan composer
  / enter-leave cue** — *missing-partial / adapt / S–L / medium*. Borrow the provider/movement
  split + tri-state mode (Off/WhenMoving/Continuous) + MotionTracker only if RT's audio systems
  multiply; the scan composer is an *enrichment* of InteractableDescriber (add HP/condition/state
  parts for combat scanning).
- *Skip / low:* world-map sonar (concept port), fog cue (cheap later), slope tone (RT maps are
  planar), grid/point readout composers (RT has them — steal only per-fragment toggle granularity
  + "bearing/distance to cell relative to acting unit").

### 6. Review buffers (orthogonal review-channel framework)

**Verdict:** **The single most reusable thing in the catalog.** Gives a blind player a second
navigation axis (Alt+L/R = which buffer, Alt+U/D = which line) fully orthogonal to RT's
SetFocused reader — exactly the "query a unit's HP/buffs/AP without losing my place" capability
RT lacks. Zero Harmony, zero EventBus; pure polling that fits RT's OnUpdate idiom.

- **Two-axis review-channel framework + input model** — Alt+L/R cycle named buffers, Alt+U/D step
  lines, speech interrupts on every nav — *missing / adapt / M / high*. Wire Alt+arrows in
  `Main.OnUpdate` via `Input.GetKeyDown`, console-mode gated. `interrupt:true` matches RT's
  keypress-caused rule exactly (no NavInputProbe change). Verify Alt+arrow doesn't collide.
- **Buffer base class + BufferManager ring** — named ordered list with cursor, Enabled/
  FollowLatest, position-preserving `Repopulate`, skip-disabled+wrap ring, `_position=-1` enter-
  reads-name, live `Func<unit>` resolver — *missing / drop-in / S / high*. **Lowest-effort,
  highest-confidence part.** Pure C#; only swap unit type to `BaseUnitEntity` and `Tts`→`Speaker`.
- **UnitBuffer — on-demand live unit readout** — `Func<unit>` resolved fresh each refresh —
  *missing / rebuild / M / high*. Structure ports; **contents rebuilt for 40K**: no AC — use
  defenses (armor absorption, deflection, dodge/parry) + Wounds for HP + AP/MP from
  `PartUnitCombatState`; buffs in RT's namespace inside GameCore, not `Kingmaker.UnitLogic.Buffs`.
  Highest combat value: an "active unit" buffer (own AP/MP/buffs) + a "current target" buffer.
- **Buff/debuff line + tooltip-faithful duration** — *partial / adapt / S / medium*. Prefer RT's
  own buff-VM/TooltipReader; TB-only collapses duration to mostly "Rounds: N".
- **FollowLatest stream mode → combat/event-log buffer** — *missing / adapt / M / medium*. The
  flag is drop-in; the log buffer is new work but high payoff for the combat build — FollowLatest
  jumps to newest, then Alt+Up scrubs back.

### 7. Dev tooling & mod infra

**Verdict:** The headline is the **DEBUG loopback dev server** — uniquely high-leverage for a
sightless author + AI co-driver that can neither see the screen nor hear TTS. Adapt, don't copy:
RT rides console nav (`/gui` walks the active `GridConsoleNavigationBehaviour`) and splits game
code (the evaluator must reference both `Code.dll` + `RogueTrader.GameCore.dll`). The cross-thread
Pump *simplifies* because UMM gives `modEntry.OnUpdate` for free. On the always-ship side,
`WarningReader` is the one genuinely new player feature.

- **Loopback HTTP dev-server framework** (raw `TcpListener`, env-var+marker-file gate, main-thread
  Pump queue) — *missing / adapt / M / high*. **Crown-jewel steal.** Transport copies almost
  as-is; drain the queue in `OnUpdate`; **keep the marker-file fallback** (Steam relaunch under
  AppId 2186680 drops the env var). Re-assert `Application.runInBackground=true` each Pump. `#if
  DEBUG`.
- **/eval live C# REPL (Mono.CSharp)** — *missing / adapt / M / high*. Lets the AI poke
  `Game.Instance`, `EventBus`, `TurnController`, `CustomGridGraph` live. Seed BOTH `Code.dll` +
  `GameCore.dll` plus Owlcat.Runtime.*/RogueTrader.*/Warhammer.SpaceCombat.*/AstarPathfindingProject;
  reuse the BCL-dedup set; run on the main thread via the Pump.
- **/speech read-back tap** (ring buffer at the Speaker chokepoint) — *partial / adapt / S /
  high*. RT ships SpeechLog + a single `Speaker` chokepoint — add an in-memory monotonic-cursor
  ring + `/speech?since=N`. Closes the "AI can't hear Prism" gap.
- **/gui focused nav-tree dump** — *missing / rebuild / M / high*. Rebuild from the idea: walk the
  active `GridConsoleNavigationBehaviour` entities, render each via UiTextReader, mark the
  SetFocused node, include VirtualNavItems. On-demand whole-ring snapshot (FocusLog is adjacent).
- **WarningReader (speak refusal toasts)** — `IWarningNotificationUIHandler` via `LocalizedTexts`
  — *missing / adapt / S / high*. **The one new player feature here.** Especially high for TB
  AP/MP: "not enough action points", out-of-range, blocked moves surface as toasts a blind player
  never hears. Pass already-localized text straight through.
- **/screenshot (framebuffer + size-stabilization poll)** — *missing / drop-in / S / medium*.
  `UnityEngine.ScreenCapture` is identical; copy verbatim. Lets a sighted/vision AI confirm state.
- **/input action injection** — *missing / rebuild / M / medium*. RT has no central registry —
  rebuild as a named command table → the mod's own handlers; doubles as the controller-trigger
  plumbing RT marks TBD.
- **/loadsave drive-to-interactive** — *missing / adapt / M / medium*. Drive the real Continue
  path; poll `IAreaActivationHandler`/loading handlers. Massive test-loop time-saver.
- **DevApi public reflection handle** — *missing / adapt / S / medium*. One public class
  (mod Assembly + `Say()`/`Screen()`) so eval'd code reaches an otherwise-internal mod.
- **DialogVisibility mirror** — `IGameModeHandler`+`ICutsceneDialogHandler` — *partial / adapt / M
  / medium*. Helps RT's open dialogue races (initial-focus, "Loading." before activation); suppress
  speaking/acting while a cutscene hid the window. Re-verify RT's `HandleOnCueShow(CueShowData)`.
- **Startup self-check (Harmony patch-attachment verify + build timestamp)** — *missing / drop-in
  / S / low*. RT's bundled Harmony isn't pinned + assemblies publicized per-build → a silent
  patch-detach is a real failure mode; one `GetPatchInfo` assert on SetFocusedPatch pays for itself.
- *Skip / have:* ModLogger (RT has Logs/SpeechLog/FocusLog + UMM logger), native entry/Ticker/
  ordered init (UMM gives OnUpdate free — no Ticker needed), Message/TextUtil/UiSound/FocusMode
  (inspiration-only; RT hardcodes English, rides console-nav for sounds, doesn't suppress keys).

### 8. Tooltips, character sheet & announcement composition

**Verdict:** Two clusters plus one insight. **Cluster A — the Announcement composition kernel
+ verbosity cascade** (engine-generic, maps onto RT's planned UMM settings, the highest-leverage
lowest-risk steal). **Cluster B — the navigable nested-drill tooltip/encyclopedia reader with
glossary-link following** exposes RT's biggest unaddressed content gap: dense 40K talent/psychic/
weapon/condition tooltips with inline glossary terms are **mouseover popups, not console
entities**, so RT's console-nav strategy literally cannot reach them. The catch: the tooltip
reader rides WOTR's self-owned document substrate RT lacks — these are real builds, not copies.

- **Announcement composition kernel** (typed Label/Role/Value/Enabled/Selected/Tooltip/Position
  parts, `[AnnouncementOrder]`, self-skip, suffix-join; from SayTheSpire2) — *partial / adapt / L /
  high*. **Crown jewel — transcends the nav divergence (it's speech composition, not nav).**
  Feed parts from the live VM/entity that fires `SetFocused`, not a proxy tree. Pair each part with
  a UMM toggle. Hardcode English.
- **Verbosity settings cascade** (per-element → global → default, reflection auto-registration) —
  *missing / rebuild / L / high*. **Fulfills RT's planned UMM settings + verbosity gap.** Cascade
  *semantics* + reflection auto-register are the steal; storage rebuilds on UMM's `OnGUI`.
- **Inline glossary/encyclopedia link follow via the game's own dispatcher** — regex-extract TMP
  `<link>` ids → `TooltipHelper.GetLinkTooltipTemplate` → drop empties via HasContent — *missing /
  adapt / M / high*. **Highest content-access win for 40K's dense encyclopedia.** Ports largely by
  name (`UIUtility.GetKeysFromLink`); needs a drill destination (substrate/transient ring).
- **Self-owned navigable reader/document substrate** (FlowSheet doc + child-screen drill stack) —
  *missing / rebuild / XL / high*. Enabling substrate for the tooltip reader. Two RT builds:
  (a) a transient overlay owning input in OnUpdate; or (b) push a temporary
  `GridConsoleNavigationBehaviour` of VirtualNavItems + a back action. The long pole.
- **Navigable nested-drill tooltip reader (Space-opened)** — *partial / rebuild / L / high*. RT
  reads a flat tooltip on focus + Ctrl+I but can't navigate within it or follow nested links.
  Build atop the substrate; reuse RT's brick reads (`GetHeader/GetBody/GetFooter/GetHint`).
- **Brick renderer registry + reflection-fallback-that-fails-loud** — *partial / adapt / M /
  medium*. The ~33 renderers key on PF brick types — rebuild against RT's `TooltipBrick*VM` in
  Code.dll. Keep the registry + **audible "no renderer: <Type>"** so 40K coverage gaps surface
  during play-testing.
- **Char sheet as navigable grid + per-stat modifier-breakdown drill-in** — *partial / rebuild / L
  / medium*. The real add is "why is my Ballistic Skill 55" from `RogueTrader.GameCore`
  ModifiableValue's modifier list, on Ctrl+I/drill. Mappers rebuild for 40K (WS/BS/Toughness,
  wounds, armour, dodge/parry, careers/momentum). Steal the shared char-sheet "sink" abstraction.
- **Multi-line brick splitting** (one navigable row per packed line) — *missing / adapt / S /
  medium*. Useful for prereq/keyword lists; absent the substrate, split for a linear read with
  separators.
- *Skip / low:* live-not-cached drill rule (RT already reads live), spell-table touches (PF-specific
  — keep only "speak symbolic icons as words").

### 9. UI navigation framework

**Verdict:** The parallel-tree framework is the wrong architecture to import — RT chose the
opposite (ride console nav). But several self-contained, engine-agnostic pieces slot cleanly
into RT's strategy.

- **TypeAheadSearch first-letter search engine** (6 match tiers, diacritic folding, name-before-
  comma weighting, letter-repeat cycling) — *missing / adapt / M / high*. The matching engine is
  engine-agnostic C# (already an OniAccess port — check THIRD-PARTY-NOTICES licensing).
  Integration differs: enumerate candidate `IConsoleEntity` in the active nav behaviour, pull each
  one's accessible text, run the search, then `SetFocused`/`FocusOnEntity` the winner.
  **Transformative for 40K long lists** (inventory, cargo, vendor, abilities, colony, encyclopedia).
- **TextEntry (native `TMP_InputField` direct edit + per-keystroke echo)** — *missing / adapt / M /
  high*. **Fills RT's CharGen P5 name-entry gap + save naming + search boxes.** `TMP_InputField` +
  `UnityAction` are identical shared types → near drop-in. Ensure typed chars aren't re-dispatched
  by RT's OnUpdate pollers (gate on `RootUiContext.IsInInputField` + console mode).
- **Announcement composer + per-control-type verbosity cascade** — *partial / adapt / L / medium*.
  Same SayTheSpire2 kernel as subsystem 8; converges UiTextReader's scattered string-building onto
  one tunable pipeline. Feed parts from RT VMs.
- **Glossary `<link>` drill-in menu** (Space drills tooltip + inline links; menu when several) —
  *partial / adapt / M / medium*. RT already uses `GetKeysFromLink` + Ctrl+I; add the "enumerate
  all links, present a menu" UX. Keep TMP markup intact while scanning ids.
- **Raw-key reservation while typing + screen-changeover input swallow** — *missing / adapt / S /
  medium*. Correctness companions to TypeAhead/TextEntry; stop OnUpdate pollers firing on letters
  mid-type; discard the still-pending `Input.inputString` so the opening key isn't typed.
- **Path-diff announce + container-label de-dup** — *partial / inspiration-only / M / medium*.
  Steal two ideas: drop a window/section prefix that equals the entity's own label; speak only the
  delta on in-place re-read.
- **Typematic auto-repeat synced to OS delay/rate** — *missing / adapt / S / low*. Improves
  TileExplorer tile-stepping + list cycling. Pure `Input` + a user32 query.
- *Skip / low:* Table/Tree/FlowSheet/associated-element machinery, the parallel retained-mode tree
  + Navigator (XL, the core architectural divergence — mine patterns, don't port).

### 10. UI proxies (element adapters)

**Verdict:** The ~45 proxies exist because WOTR builds custom mouse-mode nav and needs a uniform
navigable-element model. RT rides console nav, so the machinery is largely redundant. The real
steals are (a) the **Announcement composition framework** (already covered) and (b) the per-widget
**read recipes** — what to *say* for a given VM — for widgets RT can already reach but reads thinly.

- **Declarative Announcement composition framework + per-part user-toggle verbosity** — *partial /
  adapt / L / high*. Same kernel; the per-part verbosity model maps onto RT's planned UMM settings.
- **Inventory item read recipe** (badges folded into name, correct (last) tooltip template, full
  context menu mirrored from VM predicates, drop-empty) — *missing / adapt / L / high*. **Major
  missing content area.** RT's `ItemSlotVM` is the same shape; fold badges, read the right tooltip
  template. **First verify** whether RT's console mode already surfaces a readable gamepad context
  menu before reimplementing `CreateContextMenu`. Writes go through RT's EventBus inventory handlers.
- **Action-bar slot read recipe** (read live `MechanicActionBarSlot`, resource-aware counts,
  toggle/targeting state, can't-use fallback) — *missing / adapt / L / high*. **Core to a blind
  player taking a turn.** Report AP/MP cost + Veil/psychic resource (not D&D slots); activate via
  `unit.Commands.Run`. Adopt read-live-never-cache.
- **Settings widget read recipes** (difficulty per-option descriptions, key-binding slot state) —
  *partial / adapt / M / medium*. RT has SettingsValueAnnounce; add the long difficulty help text
  + key-binding slot read. Take READ recipes only (console nav handles stepping).
- **Reusable SelectionGroup adapter** (`SelectionGroupEntityVM` IsSelected/IsAvailable, role-
  flexible) — *partial / adapt / S / medium*. Explicitly shared Owlcat contract → very portable.
  Steal the READ side for chargen selection groups.
- **Live context-menu mirroring into a shared submenu** — *partial / adapt / M / medium*. Prefer
  reading the game's native console context menu first; fall back to mirroring.
- **Nested feature-selector tree** (accordion radio-is-its-own-parent) — *partial / rebuild / M /
  medium*. Rebuild from the idea: detect expand/collapse, announce "expanded, N options" at the
  SetFocused level.
- **Text-field accessibility (`ProxyTextField` → TextEntry)** — *missing / adapt / M / medium*.
  Engine-generic; lands standalone. Fills the name/search gap.
- **Dialogue answer enrichment** (skill-check/DC tags, link resolver) — *partial / adapt / M /
  medium*. Fold the enrichment recipe into RT's existing DialogText reader (read `AnswerVM` checks;
  re-verify RT's `CueShowData` signature). 40K check types, no alignment/mythic.
- **Journal quest / vendor / loot read recipes** — *missing / adapt / S–M / low*. Steal the state
  vocabulary (updated/completed/failed) + buy/sell/return verbs; lower than inventory/combat.
- *Skip:* spellbook proxies (no Vancian 40K analog — action-bar recipe covers power cost/toggle);
  mod's-own-screen primitives (RT uses UMM IMGUI); action-by-id (Owlcat interfaces ARE the
  equivalent).

### 11. Settings & localization

**Verdict:** The cleanest port target in the codebase — ported from a Slay-the-Spire mod, so
almost entirely engine-agnostic with one live dependency (a locale enum), orthogonal to RT's hard
differences. RT has **zero settings persistence** today (every knob is a code constant) and a
settings UI is explicitly on the roadmap.

- **Typed persistent settings tree + flat dot-path JSON store** (Bool/Int/String/Category,
  `FullPath` key decoupled from menu layout, Reset-to-default, Changed events, corrupt-file
  fallback) — *missing / drop-in / M / high*. Newtonsoft.Json + `Mathf` already referenced → near-
  verbatim data layer. **Critical: do NOT surface via UMM's `OnGUI` panel** (mouse ImGui, invisible
  to a screen reader). Render each Setting as a console-nav entity (VirtualNavItem injection) so
  RT's SetFocused + UiTextReader speak every option for free.
- **ChoiceSetting — dropdown persisted by stable Id + live provider** — *missing / drop-in / S /
  medium*. Verbosity dropdown, announce-filter mode, and especially a **Prism voice picker**
  (live-provider overload for the runtime-known installed-voices roster).
- **BindingSetting — persisted rebindable hotkeys (concept)** — *missing / rebuild / M / medium*.
  RT's hotkeys are hardcoded in OnUpdate → no conflict resolution. Port the *concept*: a settings
  leaf storing a KeyCode/combo per action, default-snapshotted, read by the pollers. Reuse the
  `_loading`-guard + default-snapshot tricks; persist through UMM serialization.
- **Batch() write coalescing / unknown-key preservation / inherit (Nullable*)** — *missing / drop-in
  / S / low*. All ship free with the framework; keep them but they're not standalone reasons to port.
- *Skip / low:* localization manager + Message pipeline — RT deliberately hardcodes English mod
  strings and passes through already-localized game content, so there is no consumer demand. Steal
  at most the **TMP rich-text stripper + ResolveRaw-keeps-`<link>`** two-pass idea for glossary
  extraction.

### 12. Input & keybindings

**Verdict:** RT owns its keyboard via UMM's free OnUpdate but does it crudely (scattered raw
`GetKeyDown`, no central registry, no repeat, no typing guard). The high-leverage steals are the
small correctness/ergonomics patterns, not the big framework. RT gets keybinding non-conflict for
free from forced console mode, so the game-hotkey muting is a non-goal.

- **Typing-safety stand-down (`RootUiContext.IsInInputField` guard)** — *missing / adapt / S /
  high*. **A genuine latent bug, not a nicety:** `Input.GetKeyDown` fires regardless of UI focus,
  so RT's pollers will both trigger AND corrupt text input during CharGen name entry / any search
  box. Add one global predicate at the top of OnUpdate dispatch. Cheap, do regardless of the rest.
- **Exact-modifier-match phase detection (`KeyboardBinding`)** — *partial / adapt / S / medium*.
  RT's ad-hoc `GetKey(LeftControl)` checks don't reject extra modifiers (Ctrl+Shift+I misfires).
  Exact compare ports as-is against `KeyCode`.
- **OS-accurate typematic key-repeat (`OsKeyboard` + repeat logic)** — *missing / adapt / S /
  medium*. user32 P/Invoke is essentially drop-in on net481/Windows. Strong win for grid-heavy nav
  (hold an arrow to step TileExplorer across a large map). Subsumes most of the Held(key) need.
- **Control-gating off `ClickEventsController` (world-control signal)** — *partial / adapt / S /
  medium*. Cleaner than RT's per-feature `GameModeType` + loading-handler conjunctions; gate
  exploration keys so they go dead in cutscene/dialog/loading. Note: TB combat needs its own
  TurnController-based check.
- **Centralized input action registry (`InputAction`/`InputManager` core)** — *missing / adapt / M
  / medium*. Consolidates the scattered pollers; prerequisite for rebinding + the planned settings
  UI. Port only registry+poll+dispatch; DROP WOTR's UI-category→navigator tier (RT has no custom
  navigator).
- **Category-priority + chord-shadowing dispatch** — *partial / adapt / L / medium*. Build a slim
  version WHEN TB combat lands and arrows must mean different things across Exploration / tile-
  cursor / CharGen / SurfaceCombat. Encode layers off `GameModeType` + console-mode + a tile-cursor
  flag.
- *Skip / low:* keybind-capture screens + binding persistence (route through UMM, low until
  rebinding is on the roadmap), `FocusMode` KeyboardAccess.Disabled (architecturally contrary —
  RT doesn't suppress game keys), Held(key) (covered by typematic repeat).

### 13. Screens framework & service windows

**Verdict:** Do **not** port the parallel Screen/ScreenManager engine (XL, the largest RT-vs-WOTR
divergence). The real steals are the patterns layered on top, most aligned with RT's console-nav
approach.

- **Poll-and-diff context resolver + universal screen-change announce** — *partial / adapt / M /
  high*. RT's ServiceWindowAnnounce already does this for 5 windows — **this is the kernel worth
  lifting.** Extend into ONE `RootUiContext` poll announcing entry/exit for ALL contexts (Loot,
  Vendor, Rest, Dialogue, BookEvent, GroupChanger, GameOver, EscMenu, GlobalMap). Robust to
  Owlcat's dispose+recreate VM lifecycle → no per-window patches. Add 40K contexts (colony/voidship).
- **Dialogue result-notification injection** (alignment/items/XP/locations) — *missing / adapt / M
  / high*. RT injects a cue but doesn't surface the outcome feedback a sighted player sees float by.
  Subscribe RT's `DialogNotifications.OnUpdateCommand` analog; 40K mapping → Dogmatic/Heretic/
  Iconoclast convictions + items/XP/ProfitFactor/locations. Queue (passive).
- **Per-window structured summary reads** (read-all quest objectives, total cargo/ProfitFactor, all
  equipped slots, save-slot metadata) — *partial / rebuild / L / medium*. Console nav walks items
  one-by-one; add aggregate summary hotkeys sourced from the same VMs. Do high-traffic windows first.
- **Dialogue/book transcript read-back history** — *missing / rebuild / M / medium*. RT offers no
  scroll-back; build a PageUp/Down buffer over the last N delivered cues/answers from `DialogVM`
  history. High value — a missed voiced line is currently unrecoverable.
- **Speak-on-delivery cutscene-gated cue timing** — *partial / adapt / S / medium*. Verify RT's
  `DialogVM` exposes a cutscene-scheduled flag; gate speech so the mod doesn't read before delivery.
- **Accessible first-run setup wizard + in-game settings browser** — *missing / rebuild / L /
  medium*. High value because UMM's settings UI is mouse-only. Build self-voicing keyboard flows
  (engine selection, exploration verbosity, announce-filter presets) driven from OnUpdate + Speaker.
- **Boot-time gamma calibration accessibility** — *missing / adapt / S / medium*. If RT ships the
  same Owlcat first-launch gamma screen it's a **hard boot blocker** — detect via the `IHasViewModel`
  bound-view lookup, announce + allow keyboard confirm. **Verify whether RT presents it; if so,
  raise to high.**
- **IHasViewModel bound-view fallback lookup** — *missing / adapt / S / low*. Small util for screens
  where SetFocused doesn't fire (game-over, boot).
- *Skip / have:* content-signature focus survival (game preserves focus for RT — steal only the
  multi-frame settle dedupe), drive-the-real-UI philosophy (RT already embodies it deeper).

### 14. Character creation & level-up

**Verdict:** RT should NOT port the parallel chargen UI scaffolding (all inspiration-only) but
should steal the small set of high-leverage patterns that enrich RT's existing CharGenAnnounce v1.

- **One-VM-covers-four-flows insight** (chargen = level-up = career = respec) — *partial / adapt / M
  / high*. **Verify RT's chargen VM (resolved in-game, not just MainMenu) also backs level-up/
  respec.** If so, enriching chargen announces gives recurring level-up accessibility almost free —
  worth more than one-time chargen since level-ups recur.
- **Point-buy stepper announce (cost/refund + synchronous live-model read)** — *partial / adapt / M
  / high*. **Read the live allocator model on the keypress, not the LateUpdate-lagged reactive
  prop**, so the spoken new value + points-remaining are fresh; speak cost/refund. Implement as
  announce-enrichment on the SetFocused choke point.
- **Name entry via live `TMP_InputField` (+ random-name mirror)** — *missing / adapt / M / high*.
  **Fills RT's noted P4–P6 name-entry gap.** Reach the name-phase input field via a deterministic
  static-ref chain (not FindObjectOfType); announce current text, echo typed chars, expose random-
  name. Drop alignment/birth-sign (no 40K analog).
- **Per-phase polymorphic dispatch + live roadmap + signature-polling lazy-rebuild** — *partial /
  adapt / S–M / medium*. Adopt the registry shape (phase-VM-type → announcer) + roadmap reader
  (extend Ctrl+P) + cheap-signature change-detection (hardens all OnUpdate pollers against Owlcat's
  lazy/LateUpdate VM population). Rewrite summaries for 40K (origin/homeworld/occupation,
  characteristics, skills, talents, psychic/navigator powers, ship, name, portrait, summary).
- **Final summary readout via shared char-sheet sink** — *partial / rebuild / L / medium*. 40K
  content, split across GameCore + Code.dll — rebuild. Steal the shared "sink" reused by both the
  chargen summary and the in-game sheet; pairs with the one-VM insight.
- **Per-phase content classes as a 40K announcement checklist** — *partial / inspiration-only / S /
  medium*. Use the dozen phase classes as a curated checklist of WHAT to speak per focused element,
  mapped to 40K analogs.
- *Skip / low:* voice-phase sample, nested-selection treeview (RT rides console nav — mine announce
  conventions only), progression matrix (dense grid, content-specific), WizardScreen/FlowSheet shell
  (the opposite of RT's strategy).

---

## Already covered by RT (do NOT re-port)

These map to WOTR items with `rtState = have` — RT already implements them, often more deeply:

- **TMP rich-text stripping + queue facade** — RT's `Speaker` + `NavInputProbe` provenance model is
  strictly more capable than WOTR's queue-by-default `Tts`.
- **DEBUG speech ring buffer** — RT ships `SpeechLog.cs` (+ `FocusLog`, `Logs`).
- **Grid/point readout composers** — RT's `TileExplorer` reads occupant/walkable/cover/offset per
  tile.
- **Non-destructive game-hotkey muting (FocusMode / KeyboardAccess.Disabled)** — RT deliberately
  doesn't suppress game keys; forced console mode deactivates PC binds.
- **Content-signature rebuild + focus survival** — the game's console nav preserves focus across its
  own list virtualization; RT inherits it free.
- **Drive-the-real-UI philosophy** — RT embodies this *deeper* (forces console mode, calls
  `SurfaceMainInputLayer` directly, raises EventBus handlers, injects VirtualNavItem).
- **Synthesize controls the game lacks** — RT already has `VirtualNavItem` injection into the nav
  ring.
- **Action-by-id dispatch** — Owlcat's `IConfirmClickHandler`/`IConsoleNavigationEntity` interfaces
  ARE the equivalent; VirtualNavItem implements them.
- **Slider/dropdown Left-Right adjust + in-place re-announce** — RT ships `SettingsValueAnnounce`.
- **Focus restoration / StartUnfocused dual-mode** — the game's console nav remembers focus per
  screen; RT's mode-gating separates exploration from in-window nav.
- **UIElement/Container navigable base + read-live-never-cache + sound fidelity** — RT's equivalent
  IS the game's console-nav graph read at SetFocused; activating via the game's own Confirm plays the
  native Wwise sounds, so there's nothing to fidelity-match.
- **ModLogger / native entry point / Ticker / ordered init** — RT has all this via UMM (`Main.Load` +
  free `modEntry.OnUpdate`/`OnUnload`); **no Ticker / DefaultExecutionOrder MonoBehaviour needed.**

---

## Categories recap

- **Frameworks & infrastructure** — Announcement composition kernel + verbosity cascade, typed
  settings tree, review-buffer ring, EventBus→ModEvent dispatcher, IAudioEngine seam, central input
  registry.
- **Speech & TTS** — SAPI 5 fallback (+ ComDispatch), auto/fallback backend chain, clipboard last
  resort, braille via Prism `output()`, render-to-PCM positional speech (gated).
- **Spatial audio & exploration** — NAudio mixer engine, staggered sonar sweep, grid wall tones, UI
  earcons, ListenerAnchor virtual head, ScanTaxonomy/ShouldAnnounce filter, Geo helpers, where-am-I,
  faction-segmented cycles, ScanBounds, reachability gating.
- **Combat (turn-based)** — turn order / AP-MP readouts, path info on cursor, damage/death/buff/heal
  events + de-noising reconciler, action-bar slot reads, targeting framework, combat-log narrator,
  "active unit" + "current target" buffers, WarningReader refusal toasts.
- **Service windows & screens** — poll-and-diff universal context resolver, dialogue result-
  notification injection, transcript read-back, per-window structured summaries, boot-gamma
  accessibility.
- **Char creation & level-up** — one-VM-covers-four-flows insight, synchronous point-buy stepper
  announce, name entry via TMP_InputField, roadmap/phase registry, final-summary sink, 40K announce
  checklist.
- **Tooltips & text** — navigable nested-drill tooltip reader, inline glossary link follow, brick
  renderer registry (fail-loud), char-sheet modifier breakdown, multi-line brick split, TypeAhead
  search, TextEntry.
- **Settings & input** — settings tree + ChoiceSetting + BindingSetting concept, verbosity cascade,
  typing-safety guard, exact-modifier match, typematic repeat, ClickEventsController gating.
- **Dev tooling** — loopback HTTP server, /eval REPL, /speech tap, /gui dump, /screenshot, /loadsave,
  /input, DevApi handle, startup self-check.

---

## Recommended 3-phase adoption order

### Phase 1 — Quick wins (S effort, drop-in/adapt, low risk; ship in days)
Close cheap gaps and harden the foundation before any big build:
1. **Typing-safety `IsInInputField` guard** (latent bug — corrupts CharGen name entry today).
2. **Geo helpers** + **Where-am-I readout** (cheap orientation, reused everywhere).
3. **WarningReader** refusal-toast reader (hears AP/MP "not enough action points").
4. **/speech read-back tap** + **startup Harmony self-check** (dev-loop + robustness).
5. **Buffer base + BufferManager ring** (drop-in C#, unlocks Phase-2 review buffers).
6. **Settings tree data layer** (drop-in; back the rest of the roadmap).
7. Input polish: **exact-modifier match**, **OS typematic repeat**, **ClickEventsController gating**.
8. **Clipboard fallback** + **braille via Prism `output()`** (cheap speech-reach wins).

### Phase 2 — Core frameworks (M–L; the load-bearing infrastructure)
Stand up the subsystems everything else hangs off:
1. **SAPI 5 fallback handler + auto/fallback backend chain** (stop going silent for non-NVDA users).
2. **Announcement composition kernel + verbosity cascade**, wired to the settings tree rendered as
   console-nav entities (not UMM OnGUI).
3. **NAudio audio engine** (deploy beside `prism.dll`) — gates all spatial audio.
4. **Review-buffer framework + UnitBuffer** (orthogonal live-state interrogation).
5. **EventBus→ModEvent→dispatcher** + damage/death/buff-reconciler/heal events (the combat-feedback
   backbone; mind the `IGlobalRulebookHandler<T>` gotcha).
6. **Poll-and-diff universal context resolver** (generalize ServiceWindowAnnounce).
7. **TypeAheadSearch + TextEntry** (long-list search + name/save text entry).
8. **ScanTaxonomy** → finally build the planned `ShouldAnnounce` exploration filter.
9. **Loopback dev server + /eval + /gui** (accelerates every subsequent build).

### Phase 3 — Large features (L–XL; depend on Phase-2 foundations)
1. **Turn-based combat suite** — turn-order/AP-MP readouts, path info on cursor, action-bar slot
   reads, `ITargetingMode` targeting, combat-log narrator, combat-context buffers.
2. **Spatial soundscape** — ListenerAnchor + sonar sweep + grid wall tones + earcons + faction cycles
   (the blind-player "hear the room" layer).
3. **Navigable tooltip/encyclopedia reader** — self-owned document substrate + nested drill + glossary
   link follow (RT's biggest unreachable-content gap).
4. **Char-sheet modifier breakdown + final-summary sink + CharGen P4–P6** name/portrait/summary.
5. **Service-window depth** — structured summaries, dialogue transcript read-back, result-notification
   injection, setup wizard / in-game settings browser.
6. **Deferred / optional** — positional speech (gated on SAPI PCM render), world/void-map nav, RoomMap
   grid segmentation, Wwise backend (re-extract all bank ids from RT's banks).
