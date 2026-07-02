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

    /// <summary>The cursor's grid node, or null when unplanted.</summary>
    public static CustomGridNodeBase Node => _node;

    /// <summary>True when the cursor is planted on a tile (i.e. a feature is actively driving it).</summary>
    public static bool Has => _node != null;

    /// <summary>The cursor's world position when planted, else the anchor unit's live view position.</summary>
    public static Vector3 Position => _node != null ? _node.Vector3Position : PlayerPosition;

    /// <summary>The fallback origin: the selected (in combat, current-turn) unit, else the main character.</summary>
    public static Vector3 PlayerPosition
    {
        get { var a = Anchor(); return a != null ? Geo.Live(a) : Vector3.zero; }
    }

    public static void Set(CustomGridNodeBase node) => _node = node;

    /// <summary>Plant on the grid node nearest a world point — the scanner's Home/Slash "cursor to selection".
    /// Returns false (and keeps the previous node) when the point is off-graph, so planting onto an off-mesh item
    /// (a far exit pin, a floating marker) never silently unplants the cursor AND the caller can tell the plant
    /// did not move rather than falsely re-announcing the old tile.</summary>
    public static bool Set(Vector3 worldPos)
    {
        var node = NavmeshProbe.NodeAt(worldPos);
        if (node == null) return false;
        _node = node;
        return true;
    }

    public static void Clear() => _node = null;

    private static MechanicEntity Anchor()
        => Game.Instance?.SelectionCharacter?.SelectedUnit?.Value ?? Game.Instance?.Player?.MainCharacterEntity;
}
