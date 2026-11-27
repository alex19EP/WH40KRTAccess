---
name: wotr-access-prior-art
description: WrathAccess sibling mod is the authoritative prior art; steal report enumerates portable features
metadata: 
  node_type: memory
  type: reference
  originSessionId: f0f71460-c4c2-45c3-8105-5bd3d49b32f5
  modified: 2026-08-09T11:16:14.191Z
---

`E:\Games\modding\not-my\wotr-access` (WrathAccess, by bradjrenshaw) is a mature
(~30,600 LOC) screen-reader accessibility mod for **Pathfinder: Wrath of the Righteous** —
the sibling Owlcat/Unity/Mono game to our Rogue Trader target. It is the most comprehensive
prior art available (broader than [[rt-speechmod-reference]]) and shares the `Kingmaker.*` +
`Owlcat.Runtime.UI.*` roots, so patterns and a lot of engine-generic code port.

A full "what RTAccess can steal" analysis (14 subsystems cataloged + scored for portability)
lives at `docs/wotr-access-steal-report.md` in our repo. Top steals: fallback TTS stack
(SAPI5+clipboard), NAudio spatial-audio engine, EventBus→ModEvent combat-announce pipeline,
review-buffer framework (Alt+arrows), loopback dev server.

Key divergences to respect when porting (see [[rt-a11y-architecture-verdict]],
[[rt-world-grid]]): WOTR uses the **native Owlcat mod system** but **RT uses UnityModManager**
(UMM gives a free OnUpdate hook → no Ticker/DefaultExecutionOrder needed). RT now shares WA's
graph-nav kernel (see [[ui-paradigm-pivot]]). RT is **turn-based only**
and on a **square CustomGridGraph**, so WOTR's navmesh/real-time-with-pause spatial code must
be rebuilt on the grid, not copied. RT signatures live in split assemblies
(`Code.dll` UI + `RogueTrader.GameCore.dll` mechanics).

**Ownership (2026-08-09, from the user): WrathAccess is NOT the user's project — read-only
upstream.** Bugs found there (e.g. the shared-kernel defects in `docs/graph-a11y-spec.md`
Appendix B: MoveToSiblingEdge stop-crossing, FocusByReference stale read, tier-1 hash-order
nondeterminism) cannot be fixed by us directly — options are: fix in RTAccess's kernel copy
(accepting divergence) and/or report/PR upstream. Never plan work that assumes editing WA.
