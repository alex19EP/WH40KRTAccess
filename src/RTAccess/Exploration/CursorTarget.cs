using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// What the world cursor is "on": the nearest visible TARGETABLE ENTITY (a unit, or attackable destructible
/// scenery) whose footprint contains the cursor. This is how the commit-at-cursor key (our left-click equivalent)
/// picks its target for a unit-targeted ability — resolved from OUR own <see cref="MapCursor"/> + the
/// <see cref="WorldModel"/> registry rather than the game's screen-ray mouse (which a blind player has no way to
/// aim). Only entities qualify (<see cref="ScanItem.TargetEntity"/>): abilities aim at entities or at ground
/// points, so a cursor not on one falls back to the point (the game validates either way). Ignores things a
/// level away.
/// </summary>
internal static class CursorTarget
{
    private const float LevelGap = 3f; // metres; ignore units on another floor/level

    public static ScanItem Inside()
    {
        if (!MapCursor.Has) return null;
        var c = MapCursor.Position;
        ScanItem best = null;
        float bestSqr = float.MaxValue;
        foreach (var it in WorldModel.Items)
        {
            if (it.TargetEntity == null || !it.IsVisible) continue;
            var p = it.Position;
            if (Mathf.Abs(p.y - c.y) > LevelGap) continue;
            if (!it.Contains(c)) continue;                          // cursor inside the unit's actual footprint
            float dx = p.x - c.x, dz = p.z - c.z, sqr = dx * dx + dz * dz; // nearest centre wins ties
            if (sqr < bestSqr) { bestSqr = sqr; best = it; }
        }
        return best;
    }
}
