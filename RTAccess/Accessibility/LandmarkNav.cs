using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Utils;
using Kingmaker.Controllers.Units;
using Kingmaker.GameModes;
using Kingmaker.Pathfinding;
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Whole-area orientation by keyboard: cycle the game's local-map landmarks (exits, points of interest,
/// important things, loot, and the current quest objective) and walk the party to the selected one. Unlike the
/// interactable cycling (<see cref="ExplorationNav"/>), which is limited to objects within ~12.7 m, this reads
/// the game's <c>LocalMapModel.Markers</c> set — the same markers shown on the local map — across the entire
/// area, so a blind player can find the exits and their objective and travel there without manual steering.
///
/// PageUp/Down already cycle nearby interactables; this uses brackets to cycle landmarks and backslash to walk:
/// <c>[</c> previous landmark, <c>]</c> next landmark, <c>\</c> walk the selected party to the current landmark.
/// Directions are map-relative (north/east), matching <see cref="InteractableDescriber"/>. Gated to console mode
/// + exploration (Default, not in combat). Key-driven announcements interrupt, consistent with the Home re-read.
/// </summary>
internal static class LandmarkNav
{
    private static ILocalMapMarker _current;

    public static void Update()
    {
        var game = Game.Instance;
        if (game == null || game.ControllerMode != Game.ControllerModeType.Gamepad) return;
        if (game.CurrentMode != GameModeType.Default) return;
        if (game.Player != null && game.Player.IsInCombat) return;

        if (UnityEngine.Input.GetKeyDown(KeyCode.LeftBracket)) Cycle(prev: true);
        else if (UnityEngine.Input.GetKeyDown(KeyCode.RightBracket)) Cycle(prev: false);
        else if (UnityEngine.Input.GetKeyDown(KeyCode.Backslash)) WalkToCurrent();
    }

    private static Vector3? SelfPos()
        => Game.Instance.SelectionCharacter?.SelectedUnit?.Value?.Position;

    /// <summary>Visible markers in the current area, nearest first.</summary>
    private static List<ILocalMapMarker> BuildList(Vector3 self)
    {
        // Mirror the game's own LocalMapVM.SetMarkers: show every in-area marker. Do NOT filter on IsVisible()
        // — that requires Owner.IsAwarenessCheckPassed (a perception check that defaults to false), which hides
        // ordinary markers including exits, so the list came back empty.
        return LocalMapModel.Markers
            .Where(m => m != null
                        && m.GetMarkerType() != LocalMapMarkType.Invalid
                        && m.GetMarkerType() != LocalMapMarkType.PlayerCharacter
                        && LocalMapModel.IsInCurrentArea(m.GetPosition()))
            .OrderBy(m => (m.GetPosition() - self).sqrMagnitude)
            .ToList();
    }

    private static void Cycle(bool prev)
    {
        try
        {
            var self = SelfPos();
            if (self == null) { Speaker.Speak("No character selected.", interrupt: true); return; }

            var list = BuildList(self.Value);
            if (list.Count == 0) { Speaker.Speak("No landmarks nearby.", interrupt: true); return; }

            int i = _current != null ? list.IndexOf(_current) : -1;
            if (i < 0) i = prev ? 0 : -1; // unknown current → first item on next, last on prev
            int target = ((i + (prev ? -1 : 1)) % list.Count + list.Count) % list.Count;
            _current = list[target];
            Speaker.Speak(InteractableDescriber.DescribeMarker(_current, self.Value), interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("LandmarkNav.Cycle failed: " + e); }
    }

    /// <summary>
    /// March from the party (<paramref name="from"/>) toward the marker (<paramref name="target"/>) ~2 m at a
    /// time and return the farthest VIEW-space point that is still on the navmesh (its nearest node within ~2 m),
    /// stopping at the first gap. Marker icons sit off the navmesh (exits far across the ship, floating pins), so
    /// targeting them directly makes FindPathRT report "no node near the end point" and the move is silently
    /// dropped; this instead heads as far toward the marker as continuous walkable floor allows. Returns the
    /// party position itself when no floor leads toward the marker (caller treats a near-zero advance as blocked).
    /// </summary>
    private static Vector3 SnapToWalkable(Vector3 target, Vector3 from)
    {
        float dist = Vector3.Distance(from, target);
        if (dist < 0.1f) return from;
        int steps = Mathf.Clamp(Mathf.RoundToInt(dist / 2f), 1, 200);
        Vector3 best = from;
        for (int i = 1; i <= steps; i++)
        {
            var p = Vector3.Lerp(from, target, (float)i / steps);
            var node = p.GetNearestNodeXZ();
            if (node == null) break;                          // off-graph here — navmesh ended
            var d = node.Vector3Position - p; d.y = 0f;
            if (d.sqrMagnitude > 4f) break;                   // nearest floor is >2 m away — not really on-mesh
            best = p;                                         // still on walkable floor; keep advancing
        }
        return best;
    }

    private static void WalkToCurrent()
    {
        try
        {
            if (_current == null) { Speaker.Speak("No landmark selected.", interrupt: true); return; }
            var self = SelfPos();
            if (self == null) { Speaker.Speak("No character selected.", interrupt: true); return; }

            // Head as far toward the marker as continuous walkable floor allows (see SnapToWalkable). dest is a
            // VIEW-space on-mesh point, which MoveSelectedUnitsToPoint expects.
            var dest = SnapToWalkable(_current.GetPosition(), self.Value);
            float advance = new Vector2(dest.x - self.Value.x, dest.z - self.Value.z).magnitude;
            Main.Log?.Log($"LandmarkNav walk: marker={_current.GetPosition()} self={self.Value} dest={dest} advance={advance:0.0}m");
            if (advance < 1.5f) { Speaker.Speak("Can't head toward that landmark.", interrupt: true); return; }

            UnitCommandsRunner.MoveSelectedUnitsToPoint(dest);
            Speaker.Speak("Walking to " + InteractableDescriber.DescribeMarker(_current, self.Value), interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("LandmarkNav.WalkToCurrent failed: " + e); }
    }
}
