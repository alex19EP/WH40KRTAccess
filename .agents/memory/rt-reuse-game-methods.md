---
name: rt-reuse-game-methods
description: "Always drive the game's OWN method/handler for an action, even if it spawns a dialog to make accessible — never reimplement the flow"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 0fafa837-7aef-4718-ab43-8bceb974a28c
---

When making a game action accessible, call the game's OWN method/VM handler for it — do NOT reimplement the flow
from its primitives, even when reusing the game method spawns a further UI dialog that then needs its own accessible
screen. Build that screen; don't shortcut.

**Why:** reimplementing (e.g. `TryCollectLoot()` + `Close()` instead of the button's real `LootCollectorVM.CollectAll()`)
silently diverges from the game — different close/leave/routing semantics, and settings-dependent edge cases the game
handles that we then get wrong. The user's rule (2026-07-02): "use game method always, even if it needs any new
accessible dialogue." This generalizes the existing dispatch-reuse pattern (`ProxyMapObject` → `ClickMapObjectHandler`,
`ProxyUnit` → `ClickUnitHandler`, per [[rt-object-interaction]]) to ALL actions, including multi-step ones.

**How to apply:** find the exact method the game's own button/hint invokes (read the *View* that binds it), call that.
If it opens a sub-window/confirm VM, mirror that VM as a new Exclusive screen rather than bypassing it.
Worked example: loot "Take all" → `LootVM.LootCollector.CollectAll()` (NOT `TryCollectLoot`+`Close`). For a ZoneExit
it raises `ExitLocationWindowVM` (the collect-all-before-leaving confirm) → surfaced by `ExitLocationScreen`
(Accept = `Confirm()`, Back = `Decline()`). See [[rt-loot-accessibility]].
