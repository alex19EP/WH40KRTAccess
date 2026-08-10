using Kingmaker;
using Kingmaker.EntitySystem.Entities; // MechanicEntity
using Kingmaker.Pathfinding;           // CustomGridNodeBase
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// The single shared world cursor — the one point the tile explorer, the scanner's measure origin, move-to
/// orders, and (later) the spatial-audio frame all agree on. It is a grid node (the truth on RT's square
/// <see cref="CustomGridGraph"/>) plus the derived world position. <see cref="Has"/> is false until something
/// plants it — the always-active <see cref="RTAccess.Accessibility.TileExplorer"/> self-plants it on the party on
/// the first arrow-step / re-announce / move-to / recenter (there is no toggle); while unplanted,
/// <see cref="Position"/> falls back to the anchor unit's live view position so callers always have a sane origin.
///
/// This is the spine the WrathAccess "map viewer" is built around and RT lacked: the tile explorer no longer
/// owns a private cursor, and the scanner measures distances from here when it is planted. Two-cursor
/// discipline holds — the scanner's review SELECTION (what is highlighted) is separate state and never moves the
/// party; only the measure origin follows this cursor.
/// </summary>
internal static class MapCursor
{
    private static CustomGridNodeBase _node;
    private static Vector3? _point;   // free-cursor sub-tile point (null in tile mode → Position = node centre)

    /// <summary>The cursor's grid node, or null when unplanted. In free mode this is the tile UNDER the gliding
    /// point — every node verb (describe, interact, inspect, combat) answers about the cell the point stands in.</summary>
    public static CustomGridNodeBase Node => _node;

    /// <summary>True when the cursor is planted on a tile (i.e. a feature is actively driving it).</summary>
    public static bool Has => _node != null;

    /// <summary>True when a free-mode sub-tile point is driving <see cref="Position"/> (see <see cref="SetPoint"/>).
    /// The glide's mode resolver normalizes it back onto the tile centre when combat forces tile mode.</summary>
    public static bool HasPoint => _point.HasValue;

    /// <summary>The cursor's world position when planted, else the anchor unit's live view position. Tile mode:
    /// the node centre; free mode: the exact glide point (so sonar / wall tones / fog cues move smoothly).</summary>
    public static Vector3 Position => _point ?? (_node != null ? _node.Vector3Position : PlayerPosition);

    /// <summary>The fallback origin: the selected (in combat, current-turn) unit, else the main character.</summary>
    public static Vector3 PlayerPosition
    {
        get { var a = Anchor(); return a != null ? Geo.Live(a) : Vector3.zero; }
    }

    public static void Set(CustomGridNodeBase node) { _node = node; _point = null; }

    /// <summary>Plant on the grid node nearest a world point — the scanner's Home/Slash "cursor to selection".
    /// Returns false (and keeps the previous node) when the point is off-graph, so planting onto an off-mesh item
    /// (a far exit pin, a floating marker) never silently unplants the cursor AND the caller can tell the plant
    /// did not move rather than falsely re-announcing the old tile.</summary>
    public static bool Set(Vector3 worldPos)
    {
        var node = NavmeshProbe.NodeAt(worldPos);
        if (node == null) return false;
        _node = node;
        _point = null;
        return true;
    }

    /// <summary>The free-cursor write: keep the EXACT world point (sub-tile precision for the audio frame and
    /// move-to) and derive the tile under it for every node verb. The stored Y is re-projected onto the derived
    /// node's surface — the cursor is a standing position, and the navmesh linecast the glide traces with never
    /// re-snaps height (an unobstructed trace keeps the input Y). Same off-graph refusal contract as
    /// <see cref="Set(Vector3)"/>: returns false and keeps the previous plant.</summary>
    public static bool SetPoint(Vector3 worldPos)
    {
        var node = NavmeshProbe.NodeAt(worldPos);
        if (node == null) return false;
        _node = node;
        _point = new Vector3(worldPos.x, node.Vector3Position.y, worldPos.z);
        return true;
    }

    public static void Clear() { _node = null; _point = null; }

    /// <summary>The unit every cursor-relative readout measures from: the selected (in combat, current-turn) unit,
    /// else the main character; null mid-transition. Internal so <see cref="Reachability"/> can classify against the
    /// SAME unit the distances in a spoken line are measured from — anchoring the two on different characters made
    /// a door the selected companion was standing in read "0 tiles, other level".</summary>
    internal static MechanicEntity Anchor()
        => Game.Instance?.SelectionCharacter?.SelectedUnit?.Value ?? Game.Instance?.Player?.MainCharacterEntity;
}
