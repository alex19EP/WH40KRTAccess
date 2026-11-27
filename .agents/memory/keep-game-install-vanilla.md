---
name: keep-game-install-vanilla
description: "FEEDBACK — keep the game install vanilla; the mod must not arm the game's dormant cheat/dev gates (startup.json stays stock)"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 510159e3-8dc1-402e-ab10-d98c2a9f34f9
---

The user wants the **vanilla path**: no edits to game-install files (`startup.json` stays the
stock 22-byte `{"ForceLogging": true}`), and the mod must not force-enable the game's dormant
cheat/dev gates (`EnableCheats`, `development`, SmartConsole externals self-provision).

**Why:** an accessibility mod should sit on what retail exposes for every player; arming dev
gates changes game behavior beyond accessibility (LAN-facing REST server, cheat hotkeys,
co-op version-string splits) and isn't something the user wants shipped or active locally.

**How to apply:** build features only on surfaces that exist unconditionally in retail (e.g.
the ~314 `[Cheat]`-attribute command DB, `CheatsManagerHolder.System`). If a capability would
require flipping a build-mode gate, treat it as out of scope and say so rather than proposing
workarounds. Related: [[rt-dev-console-a11y]].
