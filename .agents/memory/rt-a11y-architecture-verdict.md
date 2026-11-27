---
name: rt-a11y-architecture-verdict
description: Settled architecture verdict — drive the PC UI in mouse mode + own the keyboard (raw Unity Input) + read VMs; custom combat layer over the command/targeting API offering both a free cell-cursor and a target list. No OS-level a11y; mouse mode fires OnHover not OnFocus (why we self-build)
metadata: 
  node_type: memory
  type: project
  originSessionId: 18bf840e-29c3-4c1b-813d-b71e63c4a053
---

Multi-agent investigation of the decompiled UI answered: is generic GUI accessibility possible, or must we wrap each window? See [[project-accessibility-mod]], [[rt-modding-stack]].

**Settled verdict:** drive the **PC UI in mouse mode**, own the keyboard (raw Unity Input, ignore the game's keybindings — [[rt-keyboard-usage]]), and read the VMs directly. Combat is a **custom layer over the command/targeting API** and can offer BOTH a free cell-cursor AND a target list. (This is the graph-nav paradigm the project committed to; the console-focus-ride experiments are retired.)

**Design rationale (why PC/mouse, not the console focus UI):** a blind dev shipping a Pathfinder (same engine) a11y mod drives PC UI + own keyboard and rejected the console UI because console combat targeting collapses to a limited single-target LIST — no free cursor / positioning / AoE. Two verified checks confirm the path: **PC menu controls are enumerable + activatable** (`GetComponentsInChildren<OwlcatButton>()` / `WidgetList.Entries` / VM collections; `OwlcatButton.OnLeftClick` public `ButtonClickedEvent.Invoke()` or call the VM command; `OwlcatSelectable.Interactable`), and **combat is fully driveable via the command API** (`AbilityData.CanTarget` / `CanTargetFromNode` / `GetPattern().Nodes` to enumerate targets/AoE cells; `unit.Commands.Run(new UnitUseAbilityParams/UnitMoveToParams(...))` to act; `Game.Instance.TurnController.CurrentUnit` / `TurnOrder` + per-unit AP for state — the PC click path calls the SAME API, so bypassing the cursor is equivalent with full control).

**Why self-built (the mouse-mode gate):** in mouse mode `OwlcatSelectable.OnPointerEnter` fires **OnHover, NOT OnFocus**, so the console `SetFocused` choke point never fires — the console-nav focus ring is unusable in mouse mode, which is why the scanner/cursor and screen tree are self-built. (The game boots in forced Mouse mode; see [[rt-mouse-mode-engine-gate]].)

**No OS-level a11y exists:** zero `UnityEngine.Accessibility` / `IAccessible` / `UIAutomation` refs; UI is uGUI + TextMeshPro on a Canvas, so NVDA/JAWS see nothing. The in-game "Accessibility" page is cosmetic only (3 colorblind sliders + font-size). All text must be pulled from inside the process — TMP-scrape → `ViewBase.GetViewModel()` reflection (Name/Title/DisplayName) → headless tooltip-brick walk (`IHasTooltipTemplate` → `TooltipBaseTemplate.GetHeader/Body/Footer/Hint`).

**Spatial/world gameplay needs bespoke navigators:** surface combat, space combat, star-system exploration, colony building-circle, on-battlefield target selection use a free cursor + camera scroll + distance-sorted unit cycling, with no text — a generic wrapper yields ~0%. Combat is the largest custom cost and cannot be deferred.
