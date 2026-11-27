---
name: rt-tier2-ui-closeout
description: "Tier-2 UI coverage closeout — soul-mark/vendor-selecting/sector-map info/titles/Twitch screens, plus the two RT VMs that are dead code"
metadata: 
  node_type: memory
  type: project
  originSessionId: db532622-f5c2-4799-ac89-87d8b8a7c866
  modified: 2026-07-24T23:36:47.319Z
---

Shipped 2026-07-25, closing every Tier-2 item in `docs/ui-coverage-audit-2026-07-24.md` except the two
deliberate omissions (**co-op lobby** and **bug-report UI**): `SoulMarkRewardScreen` (26),
`VendorSelectingScreen` (18), `SectorSystemInfoScreen` + `AllSystemsInfoScreen` (11, non-Exclusive),
`TwitchDropsScreen` (26), `TitlesScreen` (26). All built from the decompile, compile-verified, none
live-tested (every flow is story- / campaign-end- / service-gated).

Engine facts that are easy to get wrong:

- **Two of the audited VMs are DEAD CODE in the shipped build.** `EndOfGameVM` (`new EndOfGameVM` appears
  nowhere; `EndOfGameView` never bound) and the whole star-system time control — `TimeRewindVM` is never
  constructed and `TimeRewindPCView` is never bound by any parent view, and `CalendarRoot
  .GetCurrentWHDateText()` has no callers either. **RT ships no Imperial-date readout and no system-map
  time control**, so there was nothing to mirror. Check for a constructor before planning a screen.
- **The soul-mark popup's buttons are named opposite to their VM methods.** The window's *Accept* is
  `SoulMarkRewardVM.OnDeclinePressed` (just close); *See other ranks* is `OnAcceptPressed` (opens the
  character sheet). Drive them by LABEL.
- **The vendor-selecting window is not disposed when a trade starts** — `StartTrading` only queues the
  trade command, so `VendorScreen` opens *on top of* it and closing the trade returns you to the picker.
  Hence layer 18 (under VendorScreen's 24), not above it. Its close path is a VIEW method with three
  steps (capital-party co-op gate → queued `CloseScreen(VendorSelecting)` → `HandleExitSelectingVendor`);
  there is no VM equivalent.
- **The sector-map per-system panel opens ITSELF on warp arrival** in mouse mode
  (`OvertipEntitySystemVM.HandleWarpTravelStopped`, gated on `IsControllerMouse`) — it is the arrival
  dossier, not a window you open. The **all-systems** panel's only PC entry is a mouse-only HUD toggle,
  so `SectorMapScreen`'s Actions stop carries it (`SectorMapVM.ShowHideAllSystemsInformation`). The two
  close each other. Their show flags live on `SectorMapVM`, NOT on `RootUIContext`.
- **The titles `<role>` tag carries the role KEY, not the display name** — resolve it through the block's
  own `group.RolesData.GetRole(key)` (pipe-separated keys, newline-joined result), the same lookup
  `CreditsOneColumnPage.Append` does. Same page markup as `CreditsScreen` otherwise.
- **The sector info panel's fog rules are its own**: all three lists (planets / other objects / anomalies)
  stay hidden until the system is VISITED, and an unexplored planet reads `ExploNotExplored` instead of
  its name. Mirror both gates — [[rt-visual-parity]].

Related: [[rt-respec-appearance]], [[rt-space-maps-research]], [[rt-vendor-screen]], [[rt-message-log]],
[[rt-label-mirror-visual]].
