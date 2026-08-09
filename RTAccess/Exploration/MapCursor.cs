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

    // ---- the listening frame: where the SOUNDSCAPE is standing ----

    private static Func<Vector3> _listen;

    /// <summary>
    /// Redirect the soundscape's point of view at another point — the local map window's free cursor, so sonar,
    /// wall tones and the fog cue all describe the place you are peeking at instead of the place you left the
    /// party standing (see <see cref="LocalMapCursor"/>). Set on the map window opening, cleared on its close.
    ///
    /// Deliberately a SEPARATE frame rather than an override of <see cref="Position"/>/<see cref="Node"/> the way
    /// WrathAccess does it: over there the shared cursor is a bare point, but here it is also what aiming
    /// (<c>AimPointerDriver</c> drives the game's own pointer from it every frame, screen or no screen),
    /// deployment, the holo-sim and move-to all read. Hijacking those for a map peek would aim the game at the
    /// map. Only the ambient-audio readers opt in, and they are the only callers of the Listen* members.
    /// </summary>
    public static void SetListenOverride(Func<Vector3> provider) => _listen = provider;

    public static void ClearListenOverride() => _listen = null;

    /// <summary>True while another surface is driving the soundscape — also the signal that the ambient audio
    /// should keep running with the in-game screen no longer on top (see <c>InGameScreen.SoundscapeActive</c>).</summary>
    public static bool ListenOverridden => _listen != null;

    /// <summary>Is there a point to listen from at all — the override, else a planted cursor.</summary>
    public static bool HasListen => _listen != null || Has;

    /// <summary>The point the soundscape is centred on.</summary>
    public static Vector3 ListenPosition => _listen != null ? _listen() : Position;

    /// <summary>The grid node under the listening point, for the systems that need cells rather than a point
    /// (the wall-tone raycast). Null when the override is off the mesh — a map cursor out over a void has no
    /// walls around it to sound, so the bed correctly fades to silence there.</summary>
    public static CustomGridNodeBase ListenNode
    {
        get
        {
            if (_listen == null) return _node ?? NavmeshProbe.NodeAt(Position);
            return NavmeshProbe.OnMesh(_listen(), out var n) ? n : null;
        }
    }

    /// <summary>The unit every cursor-relative readout measures from: the selected (in combat, current-turn) unit,
    /// else the main character; null mid-transition. Internal so <see cref="Reachability"/> can classify against the
    /// SAME unit the distances in a spoken line are measured from — anchoring the two on different characters made
    /// a door the selected companion was standing in read "0 tiles, other level".</summary>
    internal static MechanicEntity Anchor()
        => Game.Instance?.SelectionCharacter?.SelectedUnit?.Value ?? Game.Instance?.Player?.MainCharacterEntity;
}
