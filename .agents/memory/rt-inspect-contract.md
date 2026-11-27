---
name: rt-inspect-contract
description: "RT Inspect API — invoke path, read-aloud path, and the force-reveal-on-open semantics"
metadata: 
  node_type: memory
  type: reference
  originSessionId: 3974a527-6ebb-4ad1-91d1-b6d843a5a78c
---

How RTAccess reads the game's Inspect panel aloud (shipped as `Exploration/Inspect.cs`). Inspect is on **`'`** (Quote, `inspect.cursor` = tile-cursor occupant) and **`Y`** (`inspect.review` = scanner review selection); K now binds `hud.gauges` → `HudGauges.ReadAll`.

**Invoke:** `EventBus.RaiseEvent<IUnitClickUIHandler>(h => h.HandleUnitConsoleInvoke(unit))` — the same event the
console inspect button raises. `IUnitClickUIHandler` is in `Kingmaker.PubSubSystem`; **`InspectVM` is its ONLY
subscriber**, so raising it has no other side effects (no selection/camera/command). Surface path of
`InGameInspectVM.OnUnitInvoke` sets `Tooltip.Value` **synchronously** (2-arg `TooltipTemplateUnitInspect` ctor with
proper reactive data) and sets `ShowInspect = true` (opens the visible panel).

**Read aloud:** `Game.Instance.RootUiContext.SurfaceVM.StaticPartVM.SurfaceHUDVM.InspectVM.Tooltip.Value`
(a `TooltipBaseTemplate`) → `TooltipReader.GetFull(template)` (renders with `TooltipTemplateType.Info` = the full
body: wounds/deflection/armour/dodge/move + weapons + active & passive abilities + status effects). `SurfaceHUDVM.cs:89`
builds the live VM; `InspectVM` field is public (`SurfaceHUDVM.cs:50`). Path mirrors `InGameScreen.SurfaceHUD()`.

**Gate:** `InspectUnitsHelper.IsInspectAllow(MechanicEntity)` — true for in-combat / player-faction / helping /
enemy / `IsNeutral` / summoned. **Ambient non-combatant NPCs have `IsNeutral=false`** (even when the scanner's
taxonomy bins them "neutral"), so inspect correctly refuses them — matches the game's own affordance.

**Key semantics:** in RT **opening Inspect IS the knowledge reveal.** Both `TooltipTemplateUnitInspect` ctors call
`InspectUnitsManager.ForceRevealUnitInfo` (the 2-arg chains the 1-arg via `: this(unit.Value)`), and
`ForceRevealUnitInfo` does `info.SetCheck(100,…)` — a **persistent** full reveal stored in `m_UnitInfos`. So there is
no knowledge gating to preserve when reading it aloud; driving the game's own VM gives byte-for-byte sighted parity.
(Do NOT construct the 1-arg template yourself just to read it — same reveal, but `GetInfoBody` builds
`TooltipBrickAbilityScoresBlock(m_UnitReactiveProperty)` with a null reactive prop on the 1-arg path; the live-VM
2-arg path avoids that.)

Space-combat inspect is a different template (`TooltipTemplateSpaceUnitInspect`, `GameModeType.SpaceCombat` branch
of `OnUnitInvoke`) — same invoke event, so the K path would read it too once that screen is wired.

Target resolution in `Inspect.cs`: `Scanner.SelectedUnit()` (= `_selectedKey as BaseUnitEntity`) else
`MapCursor.Node?.GetUnit()`. See [[rt-tile-explorer]], [[ui-paradigm-pivot]].
