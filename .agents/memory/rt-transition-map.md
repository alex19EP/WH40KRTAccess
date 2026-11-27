---
name: rt-transition-map
description: "ship deck / area fast-travel \"map\" is the game's Transition (BlueprintMultiEntrance) window, NOT LocalMap; TransitionVM engine facts + mod-owned TransitionScreen"
metadata: 
  node_type: memory
  type: project
  originSessionId: bfa189fa-82da-4f22-b5f7-130e227659ea
---

The in-ship "map" (illustrated Von-Valancius-Voidship parchment with clickable named rooms) is the game's **Transition window** — `Kingmaker.Code.UI.MVVM.VM.Transition.TransitionVM`, driven by a `BlueprintMultiEntrance` — NOT the [[rt-hud-completeness-plan]] LocalMap service window.

Key engine facts (verified live via dev harness on VoidshipOfficersDeck):
- Inside a ship the map key is **deliberately unbound from LocalMap**: `ServiceWindowsVM.BindKeys` does `if (!UIUtility.IsShipArea()) Bind("OpenMap", HandleOpenLocalMap)`. The transition map is instead raised by an in-world transition object via `IMultiEntranceHandler.HandleMultiEntrance(BlueprintMultiEntrance)`.
- Transition is NOT a `ServiceWindowsType` (it's the game's own `IScreenUIHandler.ScreenType.Transition`), so `ServiceWindowAnnounce` never fires for it.
- Live VM held at `RootUiContext.SurfaceVM.StaticPartVM.TransitionVM.Value` (Space equivalent on `SpaceVM.StaticPartVM`) — same StaticPartVM that holds ServiceWindowsVM (see [[rt-input-system-verdict]]/JournalScreen accessor).
- `TransitionVM.EntryVms` = rooms; each `TransitionEntryVM` has Name, IsVisible (discovered), IsInteractable (travelable now), IsCurrentlyLocation (coarse: in current area — the literal spot is current && !interactable), Attention (has linked quest objective → tooltip lists quest titles), Enter() (runs `GameCommandQueue.AreaTransition` + closes), GetTooltipTemplate().
- Legend button `IsVisible = entry.IsVisible && entry.IsInteractable`. Undiscovered rooms hidden on the visual map too.

Sounds: open/close = `Dialogue.BookOpen`/`BookClose`; hover/focus a room = `Buttons.ButtonHover`, click = `Buttons.ButtonClick` (OwlcatSelectable state→sound), BUT the current/auto-highlighted room is muted (`SetClickAndHoverSound(..., NoSound)`); clicking a locked room raises a warning → `GreenMessageLine` show/hide sound (mod's WarningReader also reads it). Quest (`Attention`) rooms have NO sound — visual icon only, so the mod must announce it.

`RTAccess/Screens/TransitionScreen.cs` — layer 24, Exclusive, twin of [[rt-object-interaction]]'s VariativeInteractionScreen. Mirrors reachable rooms + current location into ProxyActionButtons (verb "travel"), state suffixes "you are here"/"unavailable"/"quest objective", ScreenName "<map name>, N destinations", tooltip = quest objectives, Back→TransitionVM.Close(). locale keys `transition.*` + `action.travel` in ui.json. Not yet verified in-game.
