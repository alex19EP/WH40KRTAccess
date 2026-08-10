using Kingmaker;
using Kingmaker.View;    // ObstacleAnalyzer.TraceAlongNavmesh
using RTAccess.Accessibility; // TileExplorer (lazy plant + camera follow)
using RTAccess.Input;    // InputManager.Held
using RTAccess.Screens;  // InGameScreen.ExplorationActive
using RTAccess.Settings;
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// The optional free-floating exploration cursor — WrathAccess's <c>ContinuousGlide</c> on RT's grid. With
/// <c>exploration.cursor_mode = free</c>, holding the arrow keys GLIDES the shared <see cref="MapCursor"/> as a
/// continuous world point at <c>exploration.cursor_speed</c> m/s, tracing each frame along the game's own
/// navmesh (<see cref="ObstacleAnalyzer.TraceAlongNavmesh"/> — a grid linecast, so it stops at unwalkable cells
/// AND at fence edges/thin walls exactly like a moving unit) instead of stepping tile-by-tile. Held opposing /
/// adjacent arrows combine into one vector, so diagonals glide straight rather than zigzagging.
///
/// Gliding is deliberately SILENT: the ear-tested audio bed is the live feedback (sonar + wall tones follow
/// <see cref="MapCursor.Position"/> per frame already, <see cref="FogCue"/> tones the sight boundary, and
/// <see cref="ObjectCue"/> blips footprint enter/exit + speaks whatever the cursor rests inside once the keys
/// release). The node verbs all keep working mid-glide off the derived tile (<see cref="MapCursor.Node"/>):
/// Delete describes, Enter interacts, Backspace move-to walks to the exact point.
///
/// Free mode is EXPLORATION-ONLY by design: turn-based combat and pre-combat deployment always use the tile
/// cursor (the grid is the combat substrate), whatever the setting says — this ticker then also normalizes a
/// leftover sub-tile point back onto its tile centre, so combat's first readout measures from the cell the game
/// itself would use. Shift+arrows stay TILE steps in both modes (the precision slot, WA's two-slot idiom).
///
/// Input discipline: this polls the PRIMARY cursor actions' held state through <see cref="InputManager.Held"/>,
/// which reads live bindings only — so when the HUD owns the arrows the glide stands down by the same chord
/// shadowing that parks the tile steps, with no extra focus check. The registered primary step handlers yield
/// while free mode is active (see <see cref="TileExplorer"/>), so nothing double-drives the cursor.
/// </summary>
internal static class CursorGlide
{
    /// <summary>Whether the free cursor owns the primary arrows RIGHT NOW: the setting says free AND no combat
    /// surface forces tiles. Read live so a mid-hold combat start freezes the glide the same frame.</summary>
    public static bool FreeModeActive =>
        (ModSettings.GetSetting<ChoiceSetting>("exploration.cursor_mode")?.Current?.Id ?? "tiled") == "free"
        && Game.Instance?.TurnController?.TurnBasedModeActive != true
        && !DeploymentMode.Active;

    private static float Speed => ModSettings.GetSetting<IntSetting>("exploration.cursor_speed")?.Get() ?? 5;

    /// <summary>Per-frame: glide the planted cursor along the held-arrow vector; plant it first on a cold hold
    /// (the first touch reads the planted tile — the same discipline as the tile steps — and the glide flows on
    /// from the same hold). Never throws out of the update loop.</summary>
    public static void Tick(float dt)
    {
        try
        {
            if (!InGameScreen.ExplorationActive || !ControlState.HasControl) return;
            if (!FreeModeActive)
            {
                // Combat/deployment (or the tiled setting) took over — collapse a sub-tile point onto its
                // tile centre ONCE so every tile-mode readout measures from where the game itself would.
                if (MapCursor.HasPoint && MapCursor.Node != null) MapCursor.Set(MapCursor.Node);
                return;
            }

            int dx = (InputManager.Held("cursor.right") ? 1 : 0) - (InputManager.Held("cursor.left") ? 1 : 0);
            int dz = (InputManager.Held("cursor.up") ? 1 : 0) - (InputManager.Held("cursor.down") ? 1 : 0);
            if (dx == 0 && dz == 0) return;

            // Cold hold: plant on the anchor and read that tile; the movement starts from the next frame of
            // the same hold. TileExplorer's own handlers yield in free mode, so this is the one plant path.
            if (!MapCursor.Has) { if (TileExplorer.EnsurePlanted(out bool fresh) && fresh) TileExplorer.Announce(); return; }

            var cur = MapCursor.Position;
            var dir = new Vector3(dx, 0f, dz).normalized;
            var traced = ObstacleAnalyzer.TraceAlongNavmesh(cur, cur + dir * (Speed * dt));
            if (!MapCursor.SetPoint(traced)) return;   // off-graph → stay put (matches the tile edge refusal)
            TileExplorer.ScrollTo(MapCursor.Position); // follow-cam, gated on exploration.camera_follow
        }
        catch (Exception e) { Main.Log?.Error("CursorGlide.Tick failed: " + e); }
    }
}
