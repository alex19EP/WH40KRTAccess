---
name: rt-dev-console-a11y
description: Accessible dev-console design (docs/plans/accessible-console.md) + the cheat-gating double gate — EnableCheats alone does NOT arm iddqd/externals; startup.json on this box is locally edited
metadata: 
  node_type: memory
  type: project
  originSessionId: 510159e3-8dc1-402e-ab10-d98c2a9f34f9
---

Full researched design: `docs/plans/accessible-console.md` (2026-07-12, 9-agent sweep, all cited).

Load-bearing cross-session facts:

- **Cheat gating is a DOUBLE gate.** The 93 legacy `SmartConsole` commands (`iddqd` god mode,
  `kill`, `teleport`, `gain_xp`, `toggle_fow`, `ironman_off`…) register only under outer
  `if (BuildModeUtility.IsDevelopment)` (`Game.cs:783`) AND inner per-family
  `if (CheatsEnabled)`. `startup.json {"EnableCheats": true}` alone changes ONLY the game's
  LAN-facing REST server attempt (port 35555) + name-based `-start_from` — **no god mode**.
  The ~314 `[Cheat]`-attribute commands are unconditional on every box.
- **Externals are OUT OF SCOPE — vanilla path (user decision 2026-07-12, see
  [[keep-game-install-vanilla]])**: no self-provision, no `BuildMode.dll` reference, no
  externals palette zone; the console surface is exactly the vanilla ~314 attribute commands
  + 30 variables. (For the record the self-provision recipe was: in-memory
  `BuildModeUtility.Data.EnableCheats = true` + publicized `CheatsCommon/CheatsRelease.RegisterCheats`
  + `Database.SetExternals` — rejected. `development: true` doubly so: flips ~40 consumers
  incl. the Photon co-op version string.)
- **Box state**: game-root `startup.json` reverted to stock 22-byte `{"ForceLogging": true}`
  (depot-verified content) on 2026-07-12, before the game ever booted with the transient
  EnableCheats edit. Install is vanilla.
- **Console output**: never use `ConsoleLogSink.Poll` from two consumers — it ignores its Guid
  and destructively drains (screen would steal from dev-server `/log`). Register a mod-owned
  `ILogSink` via `Logger.Instance.AddLogger` (CutsceneLogSink precedent), filter
  `Channel.Name == "Console"`, O(1) locked enqueue only (sink runs on any thread inside the
  global logging lock; reentrant logs silently dropped), drain+Speak on main-thread tick.
- **Pause pumps the queue** (unpause is itself a queued command) — commands typed while paused
  execute in ~50 ms; only loading blocks (and CommonVM drops MessageBox requests during loads).
- **Mixed-case cheat variables** (`AutoCombat`…) unreachable via parser (ToLower lookup vs
  exact-case keys) — invoke `CheatPropertyInfoInternal.Methods.Getter/Setter` directly.
- Related: [[rt-dev-harness]], [[rt-keyboard-usage]] (BackQuote free in retail — dead
  Ctrl+Alt+Shift+BackQuote binding, no consumer).
