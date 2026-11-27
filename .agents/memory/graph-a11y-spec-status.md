---
name: graph-a11y-spec-status
description: "graph-a11y-spec.md is Draft 2 (2026-08-09), rewritten after a 5-implementation audit; records the four settled decisions"
metadata: 
  node_type: memory
  type: project
  originSessionId: 131ac9ec-e10a-4b05-8537-299a04f47f4f
  modified: 2026-08-09T11:16:27.290Z
---

`docs/graph-a11y-spec.md` is **Draft 2 (2026-08-09)** — a full rewrite of Draft 1 after
auditing it against all five implementations: RTAccess (reference), WrathAccess (upstream,
[[wotr-access-prior-art]]), CyberAccess (Cyberpunk 2077, C++/RED4ext), Stellaris
(C++/DLL-injection, in progress), Sunless Sea (C#/BepInEx net35, in progress).

Decisions the user settled for Draft 2 (don't relitigate):
- **A7 interrupt**: interrupt-on-keypress stays the normative default; hosts MAY adopt
  all-queue as a documented policy (Cyberpunk did; Sunless Sea's same choice is judged a defect
  — its NVDA rationale is wrong for out-of-process speech).
- **Frame differ identity**: structural-key-only comparison standardized (the universal status
  quo); §3.1 instead warns hard against index-based keys.
- **WA↔RT kernel fork**: canonical core + Appendix A extensions catalog (WA columns/OnDrag,
  RT Shift-Ctrl/sounds/reason, CP flyouts/OnActivateHold). NO code reconciliation planned.
- **Scope**: full Draft 2 rewrite, not errata.

Structural changes vs Draft 1: numbers corrected (50 kernel tests not ~100; 62 RT screens;
kernel ~1,700 C# / 2–2.5k C++); new ports P10 text normalization (required), P11 focus
write-back (conditionally required), P12 binding introspection; new §7.1 master gate
(FocusMode), §7.4 claim-vs-consume split, §7.7 Tab-end policy ladder, §10.5 legacy-migration
protocol; §8 reframed as host-shaped catalog; type-ahead + raw-mode/stitching demoted to
SHOULD/niche; Appendix B lists known source-implementation defects (RT-side ones —
MoveToSiblingEdge stop filter, FocusByReference stale read — are candidate code fixes here;
WA-side ones are report-only, WA isn't ours).

The five raw audit reports exist only in the 2026-08-09 session transcript; the spec's ⚠
markers + Appendix B carry everything load-bearing.
