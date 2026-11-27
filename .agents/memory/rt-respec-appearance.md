---
name: rt-respec-appearance
description: "Respec + Change Appearance windows — surface-only story modals; respec cost model, view-level confirm boxes, and the appearance phase-VM reuse"
metadata: 
  node_type: memory
  type: project
  originSessionId: db532622-f5c2-4799-ac89-87d8b8a7c866
  modified: 2026-07-24T22:49:28.554Z
---

Two RT story-service modals, both **Surface-only** (`SpaceStaticPartVM` has neither), shipped
2026-07-25 as `RespecScreen` (layer 26) + `ChangeAppearanceScreen` (layer 16). Full teardown:
`docs/respec-appearance-teardown.md`.

Non-obvious engine facts:

- **Respec cost is per-character and mostly zero.** `RespecInfo`: the first three respecs of a given
  companion cost nothing; after that it is `respecCount - 2` PF. An `AddFreeRespecToPlayer` grant sets
  `m_HasExtraRespec`, consumed instead of incrementing. So the cost label is read off the LIVE
  selection, not the party.
- **`RespecCompanion` aborts before opening** when no eligible companion's cost fits the PF pool
  (paid path only) — a paid window always has at least one row.
- **The confirm/cancel prompts live at different levels.** Respec's is in the **VM**
  (`RespecVM.OnConfirm` raises `IDialogMessageBoxUIHandler` itself), so driving `OnConfirm()` gets it
  free. Change Appearance's is in the **VIEW** (`ChangeAppearanceView.OnConfirm/OnClose` wrap
  `UIUtility.ShowMessageBox` around `Complete()`/`Close()`) — calling the VM methods directly would
  silently skip the warning a sighted player sees.
- **`ChangeAppearanceVM` owns a `CharGenAppearanceComponentAppearancePhaseVM`** — the exact type the
  chargen wizard's `AppearancePhaseContent` renders. The standalone window is that phase alone plus
  four buttons; reuse the content builder, never fork it. Same `!IsInDetailedView → BeginDetailedView()`
  guard applies.
- **`ChangeAppearanceVM.ShouldShowVisualSettings` is dead** — it mirrors the phase VM's
  `ShowVisualSettings`, which `UpdateVisualSettings()` unconditionally sets false. The PC view ignores
  it and gates on `VisualSettingsVM == null`. Mirror the view.
- **`CharacterVisualSettingsVM.Cloth` is non-null only on this path** (the CharGen doll) — it is
  always null via the inventory doll, which is why `VisualSettingsScreen` had a dead toggle.

`VisualSettingsScreen` now serves both windows with a layer that follows the raiser (17 over
appearance, 13 over inventory); the sources can never be live together.

Related: [[rt-newgame-chargen]], [[rt-levelup-plan]], [[rt-label-mirror-visual]],
[[rt-inventory-window-parity]].
