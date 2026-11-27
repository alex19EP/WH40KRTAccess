---
name: prefer-tab-stops-per-zone
description: User prefers screen zones as separate Tab-stops (Tab cycles zones), not regions within one stop (Ctrl+arrows)
metadata:
  type: feedback
---

When designing RTAccess screens with multiple zones/panes, make each zone its own
Tab-stop (`GraphBuilder.BeginStop` per zone — Tab cycles them, arrows stay inside),
NOT regions inside a single stop jumped with Ctrl+Up/Down (the InventoryScreen style).
Stated while designing the vendor screen (2026-07-10).

**Why:** Tab-per-zone matches the WrathAccess VendorScreen recipe and standard
screen-reader convention; Ctrl+arrows is a less discoverable chord.

**How to apply:** New multi-zone screens use one labelled `BeginStop` per zone.
Existing regions-style screens (e.g. InventoryScreen) may warrant migration if the
user asks.
