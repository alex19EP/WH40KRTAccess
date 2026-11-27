---
name: ui-paradigm-pivot
description: "The navigation paradigm — a parallel mod-owned accessible-UI tree over the game's VMs, driven by the pull-based graph-nav core; the rationale + the WA upstream sync rule & steal-list"
metadata: 
  node_type: memory
  type: project
  originSessionId: 2f822d1e-f036-4c1c-bca4-a9d4bc347439
---

RTAccess navigates via a **parallel mod-owned accessible-UI tree**, NOT a ride on the game's
console-focus ring. The tree mirrors the game's ViewModels, is navigated by our own Navigator, and
activates real VM methods / EventBus events. Forced console mode is removed; we boot in mouse mode.
This realizes [[rt-a11y-architecture-verdict]] with WrathAccess's framework ([[wotr-access-prior-art]]).
CLAUDE.md "Navigation strategy" is the canonical summary; this memory keeps the *why* + the upstream-sync discipline.

**Why (not the console ride):** the console/gamepad focus ring can't reach mouse-only content (dense 40K
tooltips, encyclopedia glossary), can't control reading order, and forced gamepad mode deactivates PC-HUD
panels. See [[rt-mouse-mode-engine-gate]] for the engine gate that complicates any console ride.

**The core is a pull-based immediate-mode "key-graph"** (ported from WrathAccess, itself from Factorio
Access). Every screen rebuilds its node graph each frame from live game state; focus reconciles by two-tier
`ControlId` (follows a moved VM / a rebuilt-same-identity VM / nearest survivor). **The one decisive win: a
single "announce-exactly-once by identity diff" chokepoint** — speak iff the focused identity changed,
regardless of cause. This structurally eliminates the silent-focus bug class (the worst hazard for a screen
reader over Owlcat's churny reactive MVVM). The element/adapter layer (TreeGraphAdapter / UIElement / Container
/ FlowSheet / Table / TreeGroup + per-widget proxies) was torn down; `GraphNavigator` is the sole navigator.
Idioms: `GraphSheet` (graph-native table with identity row keys), node factory families
(`ItemNodes`/`CharGenNodes`/`ActionBarNodes`/…), `Screen.KeepStateOnPop` + per-screen nav-state retention,
declarative `InitialFocusStop`, lazy materialization via `GraphBuilder.IsExpanded` (collapsed subtrees never
built). `ScreenManager` resolves the active screen by polling `Game.Instance.RootUiContext`
(IsMainMenu/IsSurface/IsSpace; `ServiceWindowsVM.<X>VM.Value != null`).

**Chargen/graph doctrines the port must honor:** phase contents hold NO view state; filter state lives on game
VIEWS not VMs; read the SELECTION, not the hover-poisoned reactive; force the detailed view per phase.

**Upstream sync rule.** Track WrathAccess **`main`** (graph-nav is merged there; the old `graph-nav` branch is
stale). WA clone: `E:\Games\modding\not-my\wotr-access`. **Never blanket re-sync** — take core `Graph/*` fixes,
tests, and per-screen recipes selectively. Current exploration/scanner steal candidates (they touch zero graph
files): frontier L-cycle, room unexplored-% + door destinations, rest camp-banter, env-descriptions survey
pipeline; scanner/fog — explored terrain via fog G channel, fog-reveal radius, LOS-oracle cross-floor-leak
convention, trap zones as polygon entities (steal-list for [[rt-scanner-consistency]] / FogProbe work). Migration
history: `docs/plans/keyed-graphing-tarjan.md` + `docs/plans/mirrored-surfacing-engelbart.md`. Relates to
[[rt-input-system-verdict]].
