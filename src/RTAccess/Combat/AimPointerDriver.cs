using System;
using HarmonyLib;
using Kingmaker.Controllers.Clicks;   // PointerController
using RTAccess.Exploration;            // Targeting, MapCursor, CursorTarget

namespace RTAccess.Combat;

/// <summary>
/// Drives the game's own ability-aim pipeline at OUR keyboard cursor. While we are aiming
/// (<see cref="Targeting.Aiming"/>), a postfix on <see cref="PointerController.Tick"/> overwrites the pointer's
/// world target — <c>WorldPosition</c> with <see cref="MapCursor.Position"/> and <c>PointerOn</c> with the unit
/// under the cursor — so the game recomputes its affected-target set, red highlight, and hit-chance overtips AT our
/// tile. <see cref="AimReadTap"/> then reads the result back. See docs/plans/piloted-aiming-lamport.md.
///
/// POSTFIX, not prefix: it runs AFTER Tick's own hardware-cursor raycast write (which is conditional on a raycast
/// hit), so our value is the last write of the frame and the live input <c>AbilityRange.Update</c> reads next —
/// independent of MonoBehaviour script order, and it beats the per-frame mouse stomp WITHOUT disabling the pointer.
/// It reverts automatically: the guard drops the instant aiming ends, and the real mouse owns <c>WorldPosition</c>
/// again on the very next frame. When the cursor is not on a unit, <c>PointerOn = null</c> is legitimate — the
/// game's GetTarget resolves the ground node from WorldPosition (point-anchored abilities), verified in-harness.
/// </summary>
[HarmonyPatch(typeof(PointerController), nameof(PointerController.Tick))]
internal static class AimPointerDriver
{
    private static void Postfix(PointerController __instance)
    {
        try
        {
            if (!Targeting.Aiming || __instance == null) return;
            __instance.WorldPosition = MapCursor.Position;
            var unit = CursorTarget.Inside()?.TargetUnit;
            __instance.PointerOn = unit != null && unit.View != null ? unit.View.gameObject : null;
        }
        catch (Exception e) { Main.Log?.Log("aim pointer drive failed: " + e.Message); }
    }
}
