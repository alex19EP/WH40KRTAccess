---
name: rt-action-bar-readout
description: "Action-bar slot readout — lead with availability; browse-label numbers must be tooltip-faithful (cooldown length, veil points+degradation)"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 194ba2c2-477c-40d1-adb3-8a7cbd00d0de
---

FEEDBACK (2026-07-11 session) on `RTAccess/UI/ActionBarNodes.cs` — the ability/action-bar slot spoken readout. Two conventions the maintainer set, both live + compile-clean:

**1. Lead with availability.** Order is `unavailable, {name}, {reason}, button, {detail…}` — status word FIRST, name, then the why-not reason. Implemented via a dedicated `ControlTypes.ActionSlot` control type (Key still "button", NOT in `ControlTypes.All`) whose `Order` puts `Enabled` first and a new `AnnouncementKinds.Reason` right after `Label`. The leading marker is LIVE but reads only cheap `IsPossibleActive`; the reason (rule-triggering `GetUnavailableReason`) is a separate non-live `Reason`-kind part. The old trailing "unavailable, {reason}" in `Detail()` was removed (spoken once now).

**2. Browse-label numbers must mirror what the hover TOOLTIP prints, not richer computed forms.**
- **Cooldown** = base LENGTH (`BlueprintAbility.CooldownRounds` → "cooldown N rounds", or `WarhammerCooldown.UntilEndOfCombat` → "once per battle") — NOT the live remaining countdown. Currently-on-cooldown rides the availability reason instead. (The surface bar shows no cooldown text at all — only starship does; the number lives in `TooltipTemplateAbility.AddCooldown`.)
- **Veil** = points added (`BlueprintAbility.GetVeilThicknessPointsToAdd()` → "adds N veil") + the game's Minor/Major degradation label (`UIStrings.Tooltips.MinorVeilDegradation/MajorVeilDegradation`, split on `PsychicPower.Major`) — NOT a predicted after-cast total (the game never prints that; it's the meter's moving slider). Kept "veil would go critical" (faithful to the predicted slider crossing the critical marker).

**Why:** the action bar is all the player's OWN kit — no fog/hidden dimension, so no leak risk — but the browse label should still present the same *form* a sighted player reads off the card/tooltip. Enums: `PsychicPower` is in `Kingmaker.Controllers.Enums` (NOT Abilities.Blueprints); `WarhammerCooldown` in `Kingmaker.UnitLogic.FactLogic`. Full detail still on SPACE (game tooltip render). Related: [[rt-label-mirror-visual]], [[rt-visual-parity]], [[rt-tooltip-reader-audit]].
