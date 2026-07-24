using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Pathfinding;   // CustomGridGraph, CustomGridNodeBase, GraphParamsMechanicsCache
using RTAccess.Localization;   // Loc
using RTAccess.Screens;        // InGameScreen.ExplorationActive
using RTAccess.Settings;       // ModSettings, BoolSetting
using RTAccess.Speech;         // Speaker
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// Splits the current area's walkable space into ROOMS for orientation ("Room 12, large hall"). This half owns the
/// ENGINE side — reading the live A* <see cref="CustomGridGraph"/>, world positions, classification, naming, fog
/// gating and speech; the arithmetic (clearance transform, persistence watershed, region merge, boundary scan)
/// lives in the BCL-pure <see cref="RoomSegmenter"/> so it can be unit-tested without Unity or the game.
///
/// **Passability comes from the engine, not from geometry.** For every walkable cell we read the eight
/// <see cref="CustomGridNodeBase.HasConnectionInDirection"/> bits the pathfinder itself wrote, and the segmenter
/// treats those as the ONLY way one cell reaches another. Those bits are computed by
/// <c>CustomGridGraph.CalculateConnections</c> → <c>IsValidConnection</c>, which already folds in walkability on
/// both sides, every connection cut (<c>IsConnectionCut</c> — all fences, cardinal and diagonal), the graph's own
/// <c>maxClimb</c> height limit, and the no-corner-cutting rule. One bit read replaces the three separate
/// approximations this used to make (raw grid adjacency, a one-sided cover-height fence test, and a hand-picked
/// 0.6 m height gate) — and those approximations were exactly why the V cycle used to offer openings the party
/// could not walk through. Rebuilt when the area part changes or the graph's own version index moves (a door
/// swinging, scenery destroyed); the graph streams in after the part key, so the build self-latches on
/// <see cref="Ready"/> and retries on a cooldown while empty.
///
/// Height-aware without a hand-tuned gate: stacked levels are separate rooms because the engine refuses to connect
/// cells more than <c>maxClimb</c> apart, and a staircase is its own "stairs" room because the slope mask never
/// unions sloped cells with flat ones.
///
/// Consumers: X's "where am I" appends the room; V / Shift+V review the current room's ways out (doors,
/// transitions, and the bare openings here — see Scanner.CycleExit); an announce-on-room-change watches the scan
/// reference (cursor, else leader) with a short dwell so a boundary graze doesn't flap.
/// </summary>
internal static class RoomMap
{
    private const float LevelGap = 3f;        // |y| beyond which a cell is "another floor" (RoomAt height guard)
    private const int MaxCells = 3_000_000;   // grid budget; skip a build beyond it (surface areas are far smaller)
    private const float DwellSeconds = 0.25f; // a new room must persist this long before it is announced
    private const float RebuildCooldown = 3f; // s between graph-churn rebuilds, so a swinging door can't thrash
    private const float RelatchWindow = 5f;   // s a churn rebuild may suppress the room announcement for

    public sealed class Room
    {
        public int Id;            // stable 1..N (sorted by centroid)
        public string ClassKey;   // room.class.* locale suffix
        public float Area;        // m^2
        public Vector3 Centroid;
        public readonly List<Exit> Exits = new List<Exit>();
    }

    /// <summary>One walkable opening between two rooms — a cluster of grid-boundary cells (a wide doorway is one
    /// exit; two separate doorways between the same pair are two). <see cref="Position"/> is the opening centre,
    /// snapped to the walkable grid — the cursor target; <see cref="Boundary"/> is the full threshold (every
    /// boundary-cell midpoint of the cluster), so distance/bearing can read the nearest PART of a wide opening.</summary>
    public sealed class Exit
    {
        public Vector3 Position;
        public Vector3[] Boundary;
        public Room To;
    }

    /// <summary>Is this exit still part of the CURRENT map? Selection liveness for the V cycle. A rebuild mints
    /// fresh Exit objects, so identity alone would drop a held selection every time the graph changes under the
    /// player — match by POSITION as well, the same rule the V cycle already uses to continue a cycle across the
    /// two Exit objects that make up one threshold. A handful of rooms × exits, so a plain scan is fine.</summary>
    public static bool ContainsExit(Exit exit)
    {
        if (exit == null) return false;
        foreach (var room in _rooms)
            foreach (var e in room.Exits)
                if (ReferenceEquals(e, exit) || (e.Position - exit.Position).sqrMagnitude < 0.05f) return true;
        return false;
    }

    private static string _builtFor;   // "areaName|partName" the grid was built for
    private static int[] _label;       // per-cell room index (-1 = not walkable / dropped), row-major [z*_w+x]
    private static float[] _cellY;     // per-cell surface height (for the RoomAt level guard)
    private static float[] _wx, _wz;   // per-cell world XZ (walkable cells only — read off the graph at build)
    private static byte[] _conn;       // per-cell crossable-edge mask (RoomSegmenter direction order)
    private static float _cell;        // grid cell size (m) as built
    private static int _w, _h;
    private static readonly List<Room> _rooms = new List<Room>();

    public static IReadOnlyList<Room> Rooms => _rooms;
    public static bool Ready => _label != null && _rooms.Count > 0;

    /// <summary>The room at a world position, or null (off-mesh, other floor, or no map yet). Resolves through the
    /// grid node nearest the point; if that cell belongs to no room (a residual sliver), it widens outward over
    /// CROSSABLE edges only — the old raw 2-cell ring happily answered with the room on the far side of a wall,
    /// which then leaked into the exit cycle. Height-guarded so a point above another floor doesn't match below.</summary>
    public static Room RoomAt(Vector3 pos)
    {
        if (_label == null || _rooms.Count == 0) return null;
        if (!TryCell(pos, out int start)) return null;
        return RoomOfCell(start, pos.y) ?? WalkToRoom(start, pos.y, 2);
    }

    /// <summary>Every room reachable from <paramref name="p"/> within <paramref name="steps"/> crossable edges,
    /// starting from a raw square of <paramref name="seedRadius"/> cells around it.
    /// The SEED square ignores connectivity — deliberately, because a closed door's own cells can be cut out of
    /// the walkable grid and a door must still resolve the rooms it joins; a radius of 2 straddles a door set in
    /// a thick bulkhead. Everything past the seed follows the engine's connectivity, so a probe can never reach
    /// through a wall into the room beyond it. Keep the radius as small as the caller can stand: it is exactly
    /// the wall-thickness at which a neighbouring room starts leaking into the answer.</summary>
    internal static void RoomsNear(Vector3 p, int seedRadius, int steps, List<Room> into)
    {
        into.Clear();
        if (_label == null || _conn == null || !TryCell(p, out int start)) return;

        var seen = new HashSet<int>();
        var frontier = new List<int>();
        var next = new List<int>();
        int gx0 = start % _w, gz0 = start / _w;
        for (int dz = -seedRadius; dz <= seedRadius; dz++)
            for (int dx = -seedRadius; dx <= seedRadius; dx++)
            {
                int nx = gx0 + dx, nz = gz0 + dz;
                if (nx < 0 || nz < 0 || nx >= _w || nz >= _h) continue;
                int i = nz * _w + nx;
                if (!seen.Add(i)) continue;
                frontier.Add(i);
                Collect(i, p.y, into);
            }

        for (int s = 0; s < steps && frontier.Count > 0; s++)
        {
            next.Clear();
            foreach (var i in frontier)
                foreach (var j in Crossable(i))
                {
                    if (!seen.Add(j)) continue;
                    Collect(j, p.y, into);
                    next.Add(j);
                }
            var swap = frontier; frontier = next; next = swap;
        }
    }

    private static void Collect(int cell, float y, List<Room> into)
    {
        var room = RoomOfCell(cell, y);
        if (room != null && !into.Contains(room)) into.Add(room);
    }

    // The grid cell a world point snaps to, or false when the point is off-graph / outside the built grid.
    private static bool TryCell(Vector3 pos, out int cell)
    {
        cell = -1;
        var node = NavmeshProbe.NodeAt(pos);
        if (node == null) return false;
        int gx = node.XCoordinateInGrid, gz = node.ZCoordinateInGrid;
        if (gx < 0 || gz < 0 || gx >= _w || gz >= _h) return false;
        cell = gz * _w + gx;
        return true;
    }

    private static Room RoomOfCell(int cell, float y)
    {
        int l = _label[cell];
        if (l < 0 || l >= _rooms.Count) return null;
        if (Mathf.Abs(_cellY[cell] - y) > LevelGap) return null;
        return _rooms[l];
    }

    // The cells one crossable step from `cell` — the engine's own connection bits, so walls, cuts, height steps
    // and corner-cutting are all honoured for free.
    private static IEnumerable<int> Crossable(int cell)
    {
        if (_conn == null) yield break;
        int gz = cell / _w, gx = cell % _w;
        for (int k = 0; k < 8; k++)
        {
            if ((_conn[cell] & (1 << k)) == 0) continue;
            int nx = gx + RoomSegmenter.Dx[k], nz = gz + RoomSegmenter.Dz[k];
            if (nx < 0 || nz < 0 || nx >= _w || nz >= _h) continue;
            yield return nz * _w + nx;
        }
    }

    // Breadth-first over crossable edges only, returning the first cell that resolves to a room.
    private static Room WalkToRoom(int start, float y, int steps)
    {
        if (_conn == null) return null;
        var seen = new HashSet<int> { start };
        var frontier = new List<int> { start };
        var next = new List<int>();
        for (int s = 0; s < steps; s++)
        {
            next.Clear();
            foreach (var i in frontier)
                foreach (var j in Crossable(i))
                {
                    if (!seen.Add(j)) continue;
                    var room = RoomOfCell(j, y);
                    if (room != null) return room;
                    next.Add(j);
                }
            if (next.Count == 0) return null;
            var swap = frontier; frontier = next; next = swap;
        }
        return null;
    }

    public static string Describe(Room room)
    {
        string s = Loc.T("where.room", new { id = room.Id }) + ", " + Loc.T("room.class." + room.ClassKey);
        // How much of the room the explored layer still hides — the darkened patches a sighted player sees on
        // the map, so a blind player knows whether a room is worth re-sweeping. Silent when fully explored
        // (no "0% unexplored" noise); the bare word at 100% (WrathAccess parity).
        int pct = UnexploredPercent(room);
        if (pct >= 100) return s + ", " + Loc.T("room.unexplored");
        if (pct > 0) return s + ", " + Loc.T("room.unexplored_pct", new { pct });
        return s;
    }

    /// <summary>How much of a room the game's explored layer still hides, as the SPOKEN percentage: rounded to
    /// tens (0, 10 … 100 — grid resolution doesn't honestly support finer), so callers comparing against 100
    /// match what <see cref="Describe"/> says.</summary>
    public static int UnexploredPercent(Room room)
        => Mathf.RoundToInt(UnexploredFraction(room) * 10f) * 10;

    // Room.Id -> (fraction, cache expiry). Fraction needs a full-grid pass, so it's cached briefly; callers are
    // announcements (key presses, room-change events), never frame loops.
    private static readonly Dictionary<int, KeyValuePair<float, float>> _unexplored
        = new Dictionary<int, KeyValuePair<float, float>>();

    private static float UnexploredFraction(Room room)
    {
        if (room == null || _label == null) return 0f;
        if (_unexplored.TryGetValue(room.Id, out var hit) && Time.unscaledTime < hit.Value)
            return hit.Key;
        if (!FogExplored.Ensure()) return 0f; // no fog in this area → everything reads explored
        int label = room.Id - 1; // ids are the label indices, 1-based
        int total = 0, unexp = 0;
        for (int gz = 0; gz < _h; gz++)
            for (int gx = 0; gx < _w; gx++)
            {
                int i = gz * _w + gx;
                if (_label[i] != label) continue;
                total++;
                if (!FogExplored.IsExplored(CellCenter(gx, gz))) unexp++;
            }
        float f = total > 0 ? (float)unexp / total : 0f;
        _unexplored[room.Id] = new KeyValuePair<float, float>(f, Time.unscaledTime + 2f);
        return f;
    }

    // ---- the raw grid, for derived overlays (FrontierModel) ----

    /// <summary>The room-label grid: per-cell room index (-1 = not walkable / dropped), row-major [z*w+x], plus
    /// the per-cell surface height. False until a map is built.</summary>
    internal static bool TryGetGrid(out int[] label, out float[] cellY, out int w, out int h)
    {
        label = _label; cellY = _cellY; w = _w; h = _h;
        return _label != null;
    }

    /// <summary>Grid cell size in metres, as built (0 when no map).</summary>
    internal static float CellSize => _cell;

    /// <summary>World centre of a grid cell. Meaningful only for walkable (label ≥ 0) cells — positions were
    /// read off the live graph at build time and unwalkable cells hold zeroes.</summary>
    internal static Vector3 CellCenter(int gx, int gz)
    {
        int i = gz * _w + gx;
        return new Vector3(_wx[i], _cellY[i], _wz[i]);
    }

    // ---- lifecycle ----

    private static int _retryCooldown;      // frames until the next attempt while the graph is empty
    private static int _graphVersion = -1;  // GraphParamsMechanicsCache.GraphVersionIndex the map was built at
    private static float _lastBuild;        // unscaled time of the last successful build (churn throttle)
    private static bool _quietRelatch;      // a churn rebuild must not re-announce the room you're standing in
    private static float _relatchUntil;     // …but the suppression expires, see TickAnnounce

    public static void Tick()
    {
        var game = Game.Instance;
        var area = game?.CurrentlyLoadedArea;
        if (area == null) { Invalidate(); return; }
        var part = game.CurrentlyLoadedAreaPart;
        string key = area.name + "|" + (part != null ? part.name : "");
        if (key != _builtFor)
        {
            _builtFor = key;
            Drop();
            ResetAnnounce();
        }
        else if (Ready && GraphParamsMechanicsCache.GraphVersionIndex != _graphVersion
                 && Time.unscaledTime - _lastBuild >= RebuildCooldown)
        {
            // The graph itself changed under us — a door opened or closed, cover was destroyed, a script toggled
            // walkability. The map is built once and then answers forever, so without this a door that opens after
            // the build never becomes an exit (and one that closes stays one). GraphVersionIndex is the game's own
            // signal (bumped from AstarPath.OnGraphsUpdated), throttled here so a swinging door can't thrash.
            // The room you are standing in is re-latched silently afterwards — churn must not re-announce it.
            // The frontier survives: the grid's dimensions and cell size are unchanged by a graph update, so its
            // cached blob coordinates stay valid and its own refresh re-points any stale room reference.
            _quietRelatch = true;
            _relatchUntil = Time.unscaledTime + RelatchWindow;
            Drop(keepFrontier: true);
        }
        // The grid graph STREAMS IN after the part key changes — an immediate build can see an empty graph. Success
        // latches via Ready; until then, retry on a cooldown (an empty pass is cheap). A thrown build backs off.
        if (!Ready && AstarPath.active != null && --_retryCooldown <= 0)
        {
            _retryCooldown = 30; // ~half a second between attempts
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Build();
                if (_rooms.Count > 0)
                {
                    _graphVersion = GraphParamsMechanicsCache.GraphVersionIndex;
                    _lastBuild = Time.unscaledTime;
                    Main.Log?.Log("[rooms] " + key + ": " + _rooms.Count + " rooms in " + sw.ElapsedMilliseconds + "ms");
                }
            }
            catch (Exception e)
            {
                Drop();
                _retryCooldown = 300; // a real failure: back off; the next part change resets
                Main.Log?.Warning("[rooms] build failed: " + e.Message);
            }
        }
        TickAnnounce();
    }

    /// <summary>Drop the map on area change / feature reset so nothing stale survives.</summary>
    public static void Invalidate()
    {
        _builtFor = null;
        _quietRelatch = false;
        Drop();
        ResetAnnounce();
    }

    // Throw the built map away. Derived overlays hang off the grid, so by default they go too — otherwise the
    // frontier's cached blobs resolve at a previous build's coordinates and the per-room fraction cache answers
    // for a same-numbered room in the new one. A same-area rebuild keeps the frontier: the lattice is identical,
    // so dropping it would only lose the player's place in the unexplored-space cycle for no gain.
    private static void Drop(bool keepFrontier = false)
    {
        _label = null;
        _conn = null;
        _rooms.Clear();
        _retryCooldown = 0;
        _graphVersion = -1;
        _wx = null; _wz = null; _cell = 0f;
        _unexplored.Clear();
        if (!keepFrontier) FrontierModel.Invalidate();
    }

    // ---- announce on room change (dwell-gated so a boundary graze doesn't flap) ----

    private static Room _announced;
    private static Room _pending;
    private static float _pendingSince;
    // Cached fog classification for the announce gate — probed only when the position actually changed
    // (WallTones' readback discipline), so a cursor parked in never-seen fog costs ONE readback, not a
    // 4 Hz loop (review finding). Accepted trade-off, same as WallTones: fog lifting around a stationary
    // cursor re-announces on its next move.
    private static Vector3 _fogPos;
    private static bool _fogNeverSeen;
    private static bool _fogKnown;

    private static bool AnnounceEnabled =>
        ModSettings.GetSetting<BoolSetting>("exploration.announce_rooms")?.Get() ?? true;

    private static void ResetAnnounce() { _announced = null; _pending = null; _fogKnown = false; }

    private static void TickAnnounce()
    {
        if (!Ready || !InGameScreen.ExplorationActive || !ControlState.HasControl || !AnnounceEnabled)
        { _pending = null; return; }

        var pos = MapCursor.Has ? MapCursor.Position : MapCursor.PlayerPosition;
        var room = RoomAt(pos);
        if (room == null) { _pending = null; return; }

        // A rebuild triggered by graph churn re-numbers the rooms, so the latch points at an orphan and the room
        // you never left would be announced again. Re-latch it silently, once — but only within a short window
        // of the rebuild: this code does not run at all while a window or turn-based combat holds control, and an
        // armed flag surviving a whole fight would swallow the first genuine room change afterwards.
        if (_quietRelatch)
        {
            _quietRelatch = false;
            if (Time.unscaledTime <= _relatchUntil) { _announced = room; _pending = null; return; }
        }
        if (room == _announced) { _pending = null; return; }

        // Dwell: the new room must be the stable pick for DwellSeconds before we speak it.
        if (room != _pending) { _pending = room; _pendingSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _pendingSince < DwellSeconds) return;

        // Parity gate (main-HUD audit L4): the map is built fog-free from raw walkability, but never-seen
        // ground is pure blackness to a sighted player — don't narrate room ids/classes as the cursor sweeps
        // through unexplored fog. The readback runs only when the probed position CHANGED (cached
        // otherwise), so a parked cursor costs one probe; re-arming the dwell keeps the room un-latched, so
        // stepping the cursor (or entering on foot) once the fog lifts still announces.
        if (!_fogKnown || (pos - _fogPos).sqrMagnitude > 1e-4f)
        {
            _fogPos = pos;
            _fogKnown = true;
            _fogNeverSeen = FogProbe.Classify(pos) == FogProbe.FogState.NeverSeen;
        }
        if (_fogNeverSeen) { _pendingSince = Time.unscaledTime; return; }

        _announced = room;
        _pending = null;
        // Passive narration (not a keypress) → queued, never interrupts. Per [[rt-interrupt-speech-rule]].
        Speaker.Speak(Describe(room), interrupt: false);
    }

    /// <summary>Debug: speak the room count + the current room; full table to the mod log. DEBUG tooling only.</summary>
    public static void DebugSpeak()
    {
        if (!Ready) { Speaker.Speak(Loc.T("scan.no_rooms"), interrupt: true); return; }
        foreach (var r in _rooms)
            Main.Log?.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[rooms] {0}: {1} area={2:0}m2 centroid=({3:0.0},{4:0.0},{5:0.0}) exits={6}",
                r.Id, r.ClassKey, r.Area, r.Centroid.x, r.Centroid.y, r.Centroid.z, r.Exits.Count));
        var cur = RoomAt(MapCursor.Has ? MapCursor.Position : MapCursor.PlayerPosition);
        string tail = cur != null ? "; " + Describe(cur) : "";
        Speaker.Speak(_rooms.Count + " rooms" + tail, interrupt: true);
    }

    // ---- the build: read the live grid, hand the arithmetic to RoomSegmenter, name what comes back ----

    private static CustomGridGraph FindGrid()
    {
        var astar = AstarPath.active;
        var graphs = astar?.data?.graphs;
        if (graphs == null) return null;
        foreach (var g in graphs)
            if (g is CustomGridGraph cgg) return cgg;
        return null;
    }

    private static void Build()
    {
        _rooms.Clear();
        _label = null;
        _conn = null;

        var graph = FindGrid();
        if (graph == null) return;
        int W = graph.width, D = graph.depth;
        int n = W * D;
        if (n <= 0 || n > MaxCells) return;
        float cell = GraphParamsMechanicsCache.GridCellSize;

        // Read the walkable mask, per-cell height, world XZ and — the load-bearing part — the eight connection
        // bits the pathfinder itself wrote for this node. No neighbour lookups and no geometry reasoning of our
        // own: whatever the engine says a unit can step across is what a room is allowed to span.
        var walk = new bool[n];
        var cellY = new float[n];
        var wx = new float[n];
        var wz = new float[n];
        var conn = new byte[n];
        bool any = false;
        for (int x = 0; x < W; x++)
            for (int z = 0; z < D; z++)
            {
                var node = graph.GetNode(x, z);
                if (node == null || !node.Walkable) continue;
                int i = z * W + x;
                walk[i] = true;
                any = true;
                var p = node.Vector3Position;
                cellY[i] = p.y; wx[i] = p.x; wz[i] = p.z;
                int bits = 0;
                for (int k = 0; k < 8; k++)
                    if (node.HasConnectionInDirection(RoomSegmenter.EngineDir[k])) bits |= 1 << k;
                conn[i] = (byte)bits;
            }
        if (!any) return;

        var seg = RoomSegmenter.Segment(walk, cellY, conn, W, D, cell);
        if (seg.MergeCapHit)
            Main.Log?.Warning("[rooms] wide-opening merge hit its round budget — the map may be over-segmented");

        // Gather the surviving regions, classify them, then number them by centroid so the ids are stable.
        var stats = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int l = seg.Label[i];
            if (l < 0) continue;
            if (!stats.TryGetValue(l, out var cells)) { cells = new List<int>(); stats[l] = cells; }
            cells.Add(i);
        }
        var infos = new List<KeyValuePair<int, Room>>();
        foreach (var kv in stats)
        {
            var cells = kv.Value;
            int cnt = cells.Count;
            double sx = 0, sy = 0, sz = 0;
            double gmx = 0, gmz = 0;
            foreach (var i in cells)
            {
                sx += wx[i]; sy += cellY[i]; sz += wz[i];
                gmx += i % W; gmz += i / W;
            }
            gmx /= cnt; gmz /= cnt;
            double cxx = 0, czz = 0, cxz = 0;
            foreach (var i in cells)
            {
                double ddx = i % W - gmx, ddz = i / W - gmz;
                cxx += ddx * ddx; czz += ddz * ddz; cxz += ddx * ddz;
            }
            cxx /= cnt; czz /= cnt; cxz /= cnt;
            double tr = cxx + czz, det = cxx * czz - cxz * cxz;
            double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
            double e1 = tr / 2 + disc, e2 = Math.Max(tr / 2 - disc, 1e-6);
            float elong = (float)Math.Sqrt(e1 / e2);
            float area = cnt * cell * cell;
            double sc = 0;
            foreach (var i in cells) sc += seg.Clear[i];
            float meanClear = (float)(sc / cnt);
            string cls;
            if (kv.Key < seg.IsStair.Length && seg.IsStair[kv.Key]) cls = "stairs";
            else if (elong > 2.6f && meanClear < 2.2f) cls = "passage";
            else if (elong > 3.2f) cls = "corridor";
            else if (area < 35f) cls = "small";
            else if (area > 220f) cls = "hall";
            else cls = "room";
            infos.Add(new KeyValuePair<int, Room>(kv.Key, new Room
            {
                ClassKey = cls,
                Area = area,
                Centroid = new Vector3((float)(sx / cnt), (float)(sy / cnt), (float)(sz / cnt)),
            }));
        }
        infos.Sort((p1, p2) =>
        {
            int c1 = p1.Value.Centroid.z.CompareTo(p2.Value.Centroid.z);
            return c1 != 0 ? c1 : p1.Value.Centroid.x.CompareTo(p2.Value.Centroid.x);
        });

        var remap = new int[Math.Max(seg.Regions, 1)];
        for (int r = 0; r < remap.Length; r++) remap[r] = -1;
        for (int k = 0; k < infos.Count; k++)
        {
            infos[k].Value.Id = k + 1;
            if (infos[k].Key < remap.Length) remap[infos[k].Key] = k;
            _rooms.Add(infos[k].Value);
        }
        var label = seg.Label;
        for (int i = 0; i < n; i++)
        {
            int l = label[i];
            label[i] = l >= 0 && l < remap.Length ? remap[l] : -1;
        }

        _label = label;
        _conn = seg.Conn;
        _w = W; _h = D; _cellY = cellY; _wx = wx; _wz = wz; _cell = cell;
        BuildExits(seg.Boundaries, remap, W, cell, wx, wz, cellY);
    }

    // Turn the segmenter's boundary edges into Exit objects: midpoints cluster per room pair by proximity, so one
    // wide doorway is one contiguous cluster = one Exit, and two separate doorways between the same pair are two.
    private static void BuildExits(List<RoomSegmenter.Edge> edges, int[] remap, int W, float cell,
        float[] wx, float[] wz, float[] cellY)
    {
        var bounds = new Dictionary<long, List<Vector3>>();
        foreach (var e in edges)
        {
            int la = e.A < remap.Length ? remap[e.A] : -1;
            int lb = e.B < remap.Length ? remap[e.B] : -1;
            if (la < 0 || lb < 0 || la == lb) continue;
            long key = ((long)Math.Min(la, lb) << 32) | (uint)Math.Max(la, lb);
            if (!bounds.TryGetValue(key, out var pts)) { pts = new List<Vector3>(); bounds[key] = pts; }
            int i = e.CellI, j = e.CellJ;
            pts.Add(new Vector3((wx[i] + wx[j]) * 0.5f, (cellY[i] + cellY[j]) * 0.5f, (wz[i] + wz[j]) * 0.5f));
        }

        float link2 = (cell * 1.8f) * (cell * 1.8f);
        foreach (var kv in bounds)
        {
            int la = (int)(kv.Key >> 32), lb = (int)(kv.Key & 0xFFFFFFFF);
            if (la >= _rooms.Count || lb >= _rooms.Count) continue;
            var pts = kv.Value;
            var root = new int[pts.Count];
            for (int i = 0; i < root.Length; i++) root[i] = i;
            Func<int, int> f = null;
            f = a => { while (root[a] != a) { root[a] = root[root[a]]; a = root[a]; } return a; };
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                    if ((pts[i] - pts[j]).sqrMagnitude <= link2)
                    { int ri = f(i), rj = f(j); if (ri != rj) root[ri] = rj; }
            var groups = new Dictionary<int, List<Vector3>>();
            for (int i = 0; i < pts.Count; i++)
            {
                int r = f(i);
                if (!groups.TryGetValue(r, out var g)) { g = new List<Vector3>(); groups[r] = g; }
                g.Add(pts[i]);
            }
            var ra = _rooms[la];
            var rb = _rooms[lb];
            foreach (var g in groups.Values)
            {
                Vector3 sum = Vector3.zero;
                foreach (var p in g) sum += p;
                var pos = sum / g.Count;
                var node = NavmeshProbe.NodeAt(pos);
                if (node != null) pos = node.Vector3Position; // snap the opening centre onto the walkable grid
                var boundary = g.ToArray(); // the full threshold — ProxyRoomExit reads bearing to its nearest part
                ra.Exits.Add(new Exit { Position = pos, Boundary = boundary, To = rb });
                rb.Exits.Add(new Exit { Position = pos, Boundary = boundary, To = ra });
            }
        }
    }
}
