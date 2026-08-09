using Kingmaker;
using Kingmaker.Pathfinding;   // GraphParamsMechanicsCache (the grid's own cell size)
using RTAccess.Accessibility;  // TileExplorer (plant), InteractableDescriber (bearing + distance)
using RTAccess.Localization;   // Loc
using RTAccess.Speech;         // Speaker
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// The local-map window's MOVEMENT cursor — a free sweep over the area's map rectangle, with its OWN point so a
/// map peek never disturbs the in-area <see cref="MapCursor"/> you were exploring from. This is the WrathAccess
/// map-viewer paradigm on RT's square grid: the sighted local map is a baked top-down photograph of the level
/// (<c>WarhammerLocalMapRenderer</c>) carrying no text to read, so the accessible map is not a picture we describe
/// but a place we let the player *walk their attention over*.
///
/// Two properties make it a MAP rather than a second tile cursor:
/// <list type="bullet">
/// <item><b>It flies.</b> Movement is unconstrained by the navmesh — walls, chasms and never-seen ground are all
/// crossable, exactly as a finger on a paper map crosses them — and clamped only to the area part's
/// <c>LocalMapBounds</c> (the same rectangle the game's own renderer draws and <c>LocalMapModel.IsInCurrentArea</c>
/// tests). The in-area cursor can never get to those places; only this one can.</item>
/// <item><b>It strides.</b> Plain arrows step <see cref="CoarseStride"/> cells at a time — a map is for covering
/// ground fast (WrathAccess spends a 3x glide multiplier on the same idea) — while Shift+arrows step a single cell
/// for reading a doorway precisely. Both ride the registered actions' own key repeat, so there is no hand-rolled
/// typematic here.</item>
/// </list>
///
/// The height re-seats onto the walkable surface wherever there is one, so a plant / where-am-I answers about the
/// right floor. That the graph stores only the TOPMOST walkable node per XZ column — a hazard everywhere else in
/// the mod (see <see cref="NavmeshProbe.WalkableNodeOnLevel"/>) — is exactly right here: a top-down map shows the
/// top surface too.
///
/// <b>Visual parity.</b> The sweep is fog-gated by <see cref="FogProbe"/>: never-seen ground reports only
/// "unexplored" — no room name, no walkability, no layout — because a sighted player sees unlit blackness there.
/// Rooms and things are otherwise reported as the map shows them.
///
/// Keys (registered in <see cref="RTAccess.Input.InputCategory.LocalMap"/>, so they are live only while the window
/// is open): arrows / Shift+arrows sweep; <b>C</b> recenters on the leader; <b>Delete</b> re-reads the spot;
/// <b>Enter</b> plants the in-area cursor here (peek → commit); <b>Backspace</b> sends the party (the sighted map's
/// right-click); <b>Home</b>/<b>/</b> jumps to the review selection; <b>X</b> is where-am-I at the cursor.
/// </summary>
internal static class LocalMapCursor
{
    /// <summary>Cells crossed by one plain-arrow step. Four cells is ~5.4 m — a stride that crosses a room in a
    /// few presses while still landing inside a doorway; Shift+arrow drops to one cell for detail work.</summary>
    private const int CoarseStride = 4;

    /// <summary>How close (metres) the cursor must come to a thing's nearest edge to count as standing on it.</summary>
    private const float LandRadius = 1.0f;

    private static Vector3? _pos;
    private static RoomMap.Room _room;    // the room the last readout reported — a room is narrated only on change
    private static bool _onNavmesh = true;

    /// <summary>Where the cursor is; the party leader while unplanted (so a cold read still has an origin).</summary>
    public static Vector3 Position => _pos ?? LeaderPos();

    /// <summary>On window open: plant at the in-area cursor — the spot you were exploring — else the leader, and
    /// baseline the room/thing state so the first step doesn't narrate a change that never happened.</summary>
    public static void Reset()
    {
        SetPos(MapCursor.Has ? MapCursor.Position : LeaderPos());
        Baseline();
    }

    /// <summary>On window close: forget the point, so re-opening in another area can never resume on a stale one.</summary>
    public static void Clear()
    {
        _pos = null; _room = null; _onNavmesh = true;
    }

    // ---- registered handlers (InputCategory.LocalMap; see InputBindings) ----

    public static void StepNorth() => Step(0, 1, CoarseStride);
    public static void StepSouth() => Step(0, -1, CoarseStride);
    public static void StepEast() => Step(1, 0, CoarseStride);
    public static void StepWest() => Step(-1, 0, CoarseStride);

    public static void FineNorth() => Step(0, 1, 1);
    public static void FineSouth() => Step(0, -1, 1);
    public static void FineEast() => Step(1, 0, 1);
    public static void FineWest() => Step(-1, 0, 1);

    /// <summary>C: snap back to the party leader — the anchor everything on a map is measured from.</summary>
    public static void Recenter() => Safe(() => { SetPos(LeaderPos()); Announce(); });

    /// <summary>Delete: re-read the spot without moving.</summary>
    public static void ReAnnounce() => Safe(Announce);

    /// <summary>Home / slash: snap to whatever the review cycles have selected.</summary>
    public static void JumpToSelection() => Safe(() =>
    {
        var sel = LocalMapReview.Selected;
        if (sel == null) { Speaker.Speak(Loc.T("localmap.no_selection"), interrupt: true); return; }
        SetPos(sel.Position);
        Announce();
    });

    /// <summary>
    /// Enter: plant the IN-AREA cursor here — the peek → commit move that makes a map browse worth anything, since
    /// every in-area verb (move-to, interact, the scanner's measure origin) then answers from the spot you found.
    /// Refused off the walkable surface: the world cursor is a grid node, so it must never be sent somewhere the
    /// party could not stand and the tile readouts could not describe.
    /// </summary>
    public static void PlantWorldCursor() => Safe(() =>
    {
        if (!_onNavmesh) { Speaker.Speak(Loc.T("localmap.not_walkable"), interrupt: true); return; }
        // TileExplorer.PlantOn owns the plant: it snaps to the node, follows the camera for a sighted helper, and
        // reads the tile out — which IS the confirmation, exactly as it is for the scanner's Home/slash plant.
        TileExplorer.PlantOn(Position);
    });

    /// <summary>
    /// Backspace: send the party here — the sighted map's one load-bearing verb (right-click →
    /// <c>UnitCommandsRunner.MoveSelectedUnitsToPoint</c>, via <c>LocalMapVM.OnClick</c>). Routed through the
    /// scanner's shared travel helper so the combat refusal, the walkable snap (a map point behind a wall walks as
    /// far as continuous floor allows) and the spoken confirmation match the landmark cycle's I key.
    /// </summary>
    public static void MoveParty() => Safe(() => Scanner.TravelToPoint(Position, TravelLabel()));

    /// <summary>X: the where-am-I recipe answered at the MAP cursor rather than at the party — "what part of the
    /// map am I looking at". Area, indoors, region of the map, room, and the fog verdict.</summary>
    public static void WhereAmI() => Safe(() =>
    {
        var parts = new List<string>();
        var area = Game.Instance?.CurrentlyLoadedArea;
        var name = area != null ? TextUtil.StripRichText(area.AreaDisplayName) : null;
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);

        var b = MapRect();
        if (b.HasValue && b.Value.size.x > 1f && b.Value.size.z > 1f)
        {
            float fx = Mathf.Clamp01((Position.x - b.Value.min.x) / b.Value.size.x);
            float fz = Mathf.Clamp01((Position.z - b.Value.min.z) / b.Value.size.z);
            parts.Add(Geo.RegionWord(fx, fz));
        }

        bool seen = FogProbe.Classify(Position) != FogProbe.FogState.NeverSeen;
        if (seen) { var room = Room(); if (room != null) parts.Add(RoomMap.Describe(room)); }
        else parts.Add(Loc.T("where.unexplored"));

        parts.Add(Bearing());
        Speaker.Speak(string.Join(", ", parts), interrupt: true);
    });

    // ---- movement ----

    // A step that the map rectangle swallowed entirely says "edge" rather than re-reading the same spot, so
    // sweeping into the border is audible instead of looking like the keyboard stopped responding.
    private static void Step(int dx, int dz, int cells) => Safe(() =>
    {
        var before = Position;
        float cell = CellSize();
        SetPos(before + new Vector3(dx * cell * cells, 0f, dz * cell * cells));
        if (Geo.Distance(before, Position) < 0.01f) { Speaker.Speak(Loc.T("cursor.edge"), interrupt: true); return; }
        Announce();
    });

    /// <summary>
    /// Move to a point: clamp into the map rectangle, then re-seat the height on the walkable surface where one
    /// exists (an off-mesh spot keeps its last height, so gliding over a chasm doesn't fall through the world).
    /// </summary>
    private static void SetPos(Vector3 p)
    {
        var b = MapRect();
        if (b.HasValue)
        {
            p.x = Mathf.Clamp(p.x, b.Value.min.x, b.Value.max.x);
            p.z = Mathf.Clamp(p.z, b.Value.min.z, b.Value.max.z);
        }
        _onNavmesh = NavmeshProbe.OnMesh(p, out var node);
        if (_onNavmesh && node != null) p.y = node.Vector3Position.y;
        _pos = p;
    }

    // ---- readout ----

    /// <summary>
    /// The one line a step speaks, composed so a sweep stays listenable: the room when it CHANGED (the primary
    /// by-ear feedback while covering ground — an unchanged room is not re-announced), then what the cursor stands
    /// on, then where that is relative to the party.
    ///
    /// Fog rules the whole line. On never-seen ground the only honest answer is "unexplored" plus the bearing:
    /// naming the room or reporting walkability there would hand a blind player the level layout a sighted player
    /// is still staring into darkness for.
    /// </summary>
    private static void Announce()
    {
        var parts = new List<string>();
        bool seen = FogProbe.Classify(Position) != FogProbe.FogState.NeverSeen;

        var room = seen ? Room() : null;
        if (room != null && room != _room) parts.Add(RoomMap.Describe(room));
        _room = room;

        var on = ThingAt(Position);
        if (on != null) parts.Add(InPlace(on));
        else if (!seen) parts.Add(Loc.T("where.unexplored"));
        else if (!_onNavmesh) parts.Add(Loc.T("localmap.not_walkable"));

        parts.Add(Bearing());
        Speaker.Speak(string.Join(", ", parts), interrupt: true);
    }

    // Re-seat the room state without speaking, so the first step after an open narrates the room only if that step
    // actually left it.
    private static void Baseline()
        => _room = FogProbe.Classify(Position) != FogProbe.FogState.NeverSeen ? Room() : null;

    /// <summary>"&lt;name&gt;, &lt;detail&gt;" — a thing described as being HERE, so it carries no distance or bearing
    /// of its own (the line's own bearing already says where "here" is).</summary>
    private static string InPlace(ScanItem it)
    {
        var detail = it.Detail;
        return string.IsNullOrWhiteSpace(detail) ? it.Name : it.Name + ", " + detail;
    }

    /// <summary>Where the cursor sits relative to the party leader — the anchor a map reading is relative to.</summary>
    private static string Bearing() => InteractableDescriber.DirectionAndDistance(LeaderPos(), Position);

    /// <summary>What the party would be walking toward, for the move-to confirmation.</summary>
    private static string TravelLabel()
    {
        var on = ThingAt(Position);
        if (on != null) return on.Name;
        var room = FogProbe.Classify(Position) != FogProbe.FogState.NeverSeen ? Room() : null;
        return room != null ? RoomMap.Describe(room) : Bearing();
    }

    /// <summary>The nearest reviewable thing whose footprint (plus a landing pad) contains the cursor — the map's
    /// pins and the units it draws, i.e. exactly what the review cycles browse.</summary>
    private static ScanItem ThingAt(Vector3 p)
    {
        ScanItem best = null;
        float bd = float.MaxValue;
        foreach (var it in LocalMapReview.Hoverable)
        {
            float d = it.DistanceTo(p);
            if (d <= LandRadius && d < bd) { bd = d; best = it; }
        }
        return best;
    }

    private static RoomMap.Room Room() => RoomMap.Ready ? RoomMap.RoomAt(Position) : null;

    private static Vector3 LeaderPos()
    {
        var a = MapCursor.Anchor();
        return a != null ? Geo.Live(a) : Vector3.zero;
    }

    /// <summary>The map rectangle the game's own renderer draws and <c>LocalMapModel.IsInCurrentArea</c> tests.</summary>
    private static Bounds? MapRect()
    {
        var b = Game.Instance?.CurrentlyLoadedAreaPart?.Bounds;
        return b != null ? b.LocalMapBounds : (Bounds?)null;
    }

    private static float CellSize()
    {
        float c = GraphParamsMechanicsCache.GridCellSize;
        return c > 0.01f ? c : 1.35f;   // RT's baked cell size, if the graph isn't up yet
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Main.Log?.Error("LocalMapCursor failed: " + e); }
    }
}
