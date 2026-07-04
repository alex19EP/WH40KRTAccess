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
/// Splits the current area's walkable space into ROOMS for orientation ("Room 12, large hall"): read the live
/// A* <see cref="CustomGridGraph"/> walkability straight off the grid, compute per-cell clearance (distance to
/// the nearest wall), then a persistence watershed — basins grow from clearance maxima and split where they meet
/// across a pronounced dip (a doorway, or a doorless pinch; <see cref="Persist"/> is how deep the dip must be).
/// Small regions merge into their biggest neighbour; survivors are numbered stably (sorted by centroid) and
/// classified by area/elongation/clearance (passage / corridor / small / room / hall / stairs). Height-aware:
/// cells never union across a height step (<see cref="DyGate"/>), and sloped cells (recast turns staircases into
/// ramps) never union with flat ones — so stacked levels split into separate rooms and the staircase between them
/// is its own "stairs" room, the obvious exit between levels.
///
/// This is a port of WrathAccess's <c>RoomMap</c> (a persistence-watershed of the recast navmesh) onto RT's
/// square grid. The two substrate differences, both verified in-harness (see [[rt-room-classifier]]): (1) RT has
/// NO recast navmesh — the grid IS the raster, so we read <see cref="CustomGridNodeBase.Walkable"/> + world
/// position per cell instead of rasterizing triangles; (2) RT walls are mostly UNWALKABLE cells (not fences),
/// which is exactly WA's "walls are holes in walkable space" assumption, so the watershed transfers directly.
/// Fence edges (thin cover / rails between two walkable cells) are rare but honoured as cardinal wall gates in the
/// watershed union and the exit-boundary scan (a full "burn the fence into the clearance field" is deferred — the
/// measured count was ~8 per deck). Native 1.35 m cells resolve rooms cleanly (validated: 22 rooms on the
/// Officers' Deck), so there is no sub-grid resample. Rebuilt when the area part changes; the graph streams in
/// after the part key, so the build self-latches on <see cref="Ready"/> and retries on a cooldown while empty.
///
/// Consumers: X's "where am I" appends the room; V / Shift+V cycle the current room's exits (planting the shared
/// cursor on the opening); an announce-on-room-change watches the scan reference (cursor,
/// else leader) with a short dwell so a boundary graze doesn't flap.
/// </summary>
internal static class RoomMap
{
    private const float Persist = 0.7f;      // clearance dip (m) required to split two basins
    private const float MinRoomArea = 12f;   // m^2 — smaller regions merge into a neighbour
    private const float CutFloor = 0.45f;    // cells with less clearance never seed a basin (assigned after)
    private const float SlopeT = 0.35f;      // rise/run above which a cell is sloped (stairs ~0.6-0.8)
    private const float DyGate = 0.6f;       // max height step (m) across which cells may join a room
    private const float MinStairArea = 2.5f; // m^2 — stair regions below this merge away (vs 12 for flat)
    private const float StairMinRise = 1.5f; // m a sloped region must CLIMB to count as stairs (bumps ~0.6m)
    private const float FurnitureMax = 12f;  // m^2 — interior obstacle islands up to this cast no clearance shadow
    private const float LevelGap = 3f;       // |y| beyond which a cell is "another floor" (RoomAt height guard)
    private const int MaxCells = 3_000_000;  // grid budget; skip a build beyond it (surface areas are far smaller)
    private const float DwellSeconds = 0.25f; // a new room must persist this long before it is announced

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
    /// snapped to the walkable grid, and is what the exit-cycle plants the shared cursor on.</summary>
    public sealed class Exit
    {
        public Vector3 Position;
        public Room To;
    }

    private static string _builtFor;   // "areaName|partName" the grid was built for
    private static int[] _label;       // per-cell room index (-1 = not walkable / dropped), row-major [z*_w+x]
    private static float[] _cellY;     // per-cell surface height (for the RoomAt level guard)
    private static int _w, _h;
    private static readonly List<Room> _rooms = new List<Room>();

    public static IReadOnlyList<Room> Rooms => _rooms;
    public static bool Ready => _label != null && _rooms.Count > 0;

    /// <summary>The room at a world position, or null (off-mesh, other floor, or no map yet). Resolves through the
    /// grid node nearest the point, then a 2-cell ring so a position hugging a wall / in a residual sliver still
    /// finds its obvious room; height-guarded so a point above another floor doesn't match the floor below.</summary>
    public static Room RoomAt(Vector3 pos)
    {
        if (_label == null || _rooms.Count == 0) return null;
        var node = NavmeshProbe.NodeAt(pos);
        if (node == null) return null;
        int gx = node.XCoordinateInGrid, gz = node.ZCoordinateInGrid;
        for (int ring = 0; ring <= 2; ring++)
            for (int dz = -ring; dz <= ring; dz++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dz), Math.Abs(dx)) != ring) continue;
                    int nx = gx + dx, nz = gz + dz;
                    if (nx < 0 || nz < 0 || nx >= _w || nz >= _h) continue;
                    int idx = nz * _w + nx;
                    int l = _label[idx];
                    if (l < 0 || l >= _rooms.Count) continue;
                    if (Mathf.Abs(_cellY[idx] - pos.y) > LevelGap) continue;
                    return _rooms[l];
                }
        return null;
    }

    public static string Describe(Room room)
        => Loc.T("where.room", new { id = room.Id }) + ", " + Loc.T("room.class." + room.ClassKey);

    // ---- lifecycle ----

    private static int _retryCooldown; // frames until the next attempt while the graph is empty

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
            _label = null;
            _rooms.Clear();
            _retryCooldown = 0;
            ResetAnnounce();
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
                    Main.Log?.Log("[rooms] " + key + ": " + _rooms.Count + " rooms in " + sw.ElapsedMilliseconds + "ms");
            }
            catch (Exception e)
            {
                _label = null; _rooms.Clear();
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
        _label = null;
        _rooms.Clear();
        _retryCooldown = 0;
        ResetAnnounce();
    }

    // ---- announce on room change (dwell-gated so a boundary graze doesn't flap) ----

    private static Room _announced;
    private static Room _pending;
    private static float _pendingSince;

    private static bool AnnounceEnabled =>
        ModSettings.GetSetting<BoolSetting>("exploration.announce_rooms")?.Get() ?? true;

    private static void ResetAnnounce() { _announced = null; _pending = null; }

    private static void TickAnnounce()
    {
        if (!Ready || !InGameScreen.ExplorationActive || !ControlState.HasControl || !AnnounceEnabled)
        { _pending = null; return; }

        var pos = MapCursor.Has ? MapCursor.Position : MapCursor.PlayerPosition;
        var room = RoomAt(pos);
        if (room == null || room == _announced) { _pending = null; return; }

        // Dwell: the new room must be the stable pick for DwellSeconds before we speak it.
        if (room != _pending) { _pending = room; _pendingSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _pendingSince < DwellSeconds) return;

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
            Main.Log?.Log(string.Format("[rooms] {0}: {1} area={2:0}m2 centroid=({3:0.0},{4:0.0},{5:0.0}) exits={6}",
                r.Id, r.ClassKey, r.Area, r.Centroid.x, r.Centroid.y, r.Centroid.z, r.Exits.Count));
        var cur = RoomAt(MapCursor.Has ? MapCursor.Position : MapCursor.PlayerPosition);
        string tail = cur != null ? "; " + Describe(cur) : "";
        Speaker.Speak(_rooms.Count + " rooms" + tail, interrupt: true);
    }

    // ---- the pipeline (port of WrathAccess RoomMap.Build, re-sourced onto the CustomGridGraph) ----

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

        var graph = FindGrid();
        if (graph == null) return;
        int W = graph.width, D = graph.depth;
        int n = W * D;
        if (n <= 0 || n > MaxCells) return;
        float cell = GraphParamsMechanicsCache.GridCellSize;

        // 1) Read the walkable mask + per-cell height + world XZ + cardinal fence bits straight off the grid.
        //    (In WA this step rasterized navmesh triangles; RT's grid IS the raster.) Walls are unwalkable cells;
        //    a fence between two walkable cells (bit k, order S/N/W/E — matches the watershed's first four deltas)
        //    is a thin wall/rail that also separates rooms.
        var walk = new bool[n];
        var cellY = new float[n];
        var wx = new float[n];
        var wz = new float[n];
        var fence = new byte[n];
        var wcells = new List<int>();
        int[] cdz = { -1, 1, 0, 0 };
        int[] cdx = { 0, 0, -1, 1 };
        for (int x = 0; x < W; x++)
            for (int z = 0; z < D; z++)
            {
                var node = graph.GetNode(x, z);
                if (node == null || !node.Walkable) continue;
                int i = z * W + x;
                walk[i] = true;
                var p = node.Vector3Position;
                cellY[i] = p.y; wx[i] = p.x; wz[i] = p.z;
                wcells.Add(i);
                for (int k = 0; k < 4; k++)
                {
                    var nb = graph.GetNode(x + cdx[k], z + cdz[k]);
                    if (nb != null && nb.Walkable && node.HasFenceWithNode(nb))
                        fence[i] |= (byte)(1 << k);
                }
            }
        if (wcells.Count == 0) return;

        // 1.5) Furniture mask: small interior unwalkable ISLANDS (crates, pillars, consoles you can walk around)
        //      cast no clearance shadow, so the watershed never reads the pinch beside them as a doorway. Only
        //      blobs fully surrounded by walkable space count; anything connected to the hull/border is structure.
        var noShadow = new bool[n];
        {
            int[] dz8f = { -1, 1, 0, 0, -1, -1, 1, 1 };
            int[] dx8f = { 0, 0, -1, 1, -1, 1, -1, 1 };
            var visited = new bool[n];
            var stack = new Stack<int>();
            var blob = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (walk[i] || visited[i]) continue;
                blob.Clear();
                bool touchesBorder = false;
                visited[i] = true; stack.Push(i);
                while (stack.Count > 0)
                {
                    int j = stack.Pop();
                    blob.Add(j);
                    int gz = j / W, gx = j % W;
                    if (gz == 0 || gx == 0 || gz == D - 1 || gx == W - 1) touchesBorder = true;
                    for (int k = 0; k < 8; k++)
                    {
                        int nz = gz + dz8f[k], nx = gx + dx8f[k];
                        if (nz < 0 || nx < 0 || nz >= D || nx >= W) continue;
                        int m = nz * W + nx;
                        if (!walk[m] && !visited[m]) { visited[m] = true; stack.Push(m); }
                    }
                }
                if (!touchesBorder && blob.Count * cell * cell <= FurnitureMax)
                    foreach (var j in blob) noShadow[j] = true;
            }
        }

        // 2) Chamfer 3-4 distance transform → clearance in metres (distance to the nearest wall).
        var dist = new int[n];
        const int INF = int.MaxValue / 4;
        for (int i = 0; i < n; i++) dist[i] = (walk[i] || noShadow[i]) ? INF : 0;
        for (int gz = 0; gz < D; gz++)
            for (int gx = 0; gx < W; gx++)
            {
                int i = gz * W + gx;
                if (dist[i] == 0) continue;
                int best = dist[i];
                if (gx > 0) best = Math.Min(best, dist[i - 1] + 3);
                if (gz > 0)
                {
                    best = Math.Min(best, dist[i - W] + 3);
                    if (gx > 0) best = Math.Min(best, dist[i - W - 1] + 4);
                    if (gx < W - 1) best = Math.Min(best, dist[i - W + 1] + 4);
                }
                dist[i] = best;
            }
        for (int gz = D - 1; gz >= 0; gz--)
            for (int gx = W - 1; gx >= 0; gx--)
            {
                int i = gz * W + gx;
                if (dist[i] == 0) continue;
                int best = dist[i];
                if (gx < W - 1) best = Math.Min(best, dist[i + 1] + 3);
                if (gz < D - 1)
                {
                    best = Math.Min(best, dist[i + W] + 3);
                    if (gx < W - 1) best = Math.Min(best, dist[i + W + 1] + 4);
                    if (gx > 0) best = Math.Min(best, dist[i + W - 1] + 4);
                }
                dist[i] = best;
            }
        var clear = new float[n];
        for (int i = 0; i < n; i++) clear[i] = dist[i] * (cell / 3f);

        // 2.5) Slope mask: cells on a sustained height gradient (recast/geometry renders stairs as ramps). Close
        //      r2 absorbs small landings/speckle; open r1 drops isolated specks, so a staircase is one solid region.
        var sloped = new bool[n];
        for (int qi = 0; qi < wcells.Count; qi++)
        {
            int i = wcells[qi];
            int gz = i / W, gx = i % W;
            float dy = 0f;
            if (gx > 0 && walk[i - 1]) dy = Math.Max(dy, Math.Abs(cellY[i] - cellY[i - 1]));
            if (gx < W - 1 && walk[i + 1]) dy = Math.Max(dy, Math.Abs(cellY[i] - cellY[i + 1]));
            if (gz > 0 && walk[i - W]) dy = Math.Max(dy, Math.Abs(cellY[i] - cellY[i - W]));
            if (gz < D - 1 && walk[i + W]) dy = Math.Max(dy, Math.Abs(cellY[i] - cellY[i + W]));
            sloped[i] = dy / cell > SlopeT;
        }
        Morph(sloped, W, D, 2, dilate: true); Morph(sloped, W, D, 2, dilate: false);
        for (int i = 0; i < n; i++) sloped[i] &= walk[i];
        Morph(sloped, W, D, 1, dilate: false); Morph(sloped, W, D, 1, dilate: true);
        for (int i = 0; i < n; i++) sloped[i] &= walk[i];

        // 3) Persistence watershed: visit cells by descending clearance; basins meeting across a saddle merge
        //    unless BOTH rise at least Persist above it. Never union across a height step, a flat/slope class
        //    change, or a cardinal fence (a thin wall between two open cells).
        var order = new int[n];
        var keys = new float[n];
        for (int i = 0; i < n; i++) { order[i] = i; keys[i] = -clear[i]; }
        Array.Sort(keys, order);

        var parent = new int[n];
        var peak = new float[n];
        var seen = new bool[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        Func<int, int> find = null;
        find = a => { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; };

        int[] dz8 = { -1, 1, 0, 0, -1, -1, 1, 1 };
        int[] dx8 = { 0, 0, -1, 1, -1, 1, -1, 1 };
        for (int oi = 0; oi < n; oi++)
        {
            int i = order[oi];
            float c = clear[i];
            if (c < CutFloor) break;
            if (!walk[i]) continue;
            seen[i] = true;
            peak[i] = c;
            int gz = i / W, gx = i % W;
            int me = find(i);
            for (int k = 0; k < 8; k++)
            {
                int nz = gz + dz8[k], nx = gx + dx8[k];
                if (nz < 0 || nx < 0 || nz >= D || nx >= W) continue;
                int j = nz * W + nx;
                if (!seen[j]) continue;
                if (sloped[j] != sloped[i] || Math.Abs(cellY[j] - cellY[i]) > DyGate) continue;
                if (k < 4 && (fence[i] & (1 << k)) != 0) continue; // thin wall/rail between two open cells
                int r = find(j);
                if (r == me) continue;
                if (sloped[i] || Math.Min(peak[r], peak[me]) - c < Persist)
                {
                    float pk = Math.Max(peak[r], peak[me]);
                    parent[me] = r;
                    me = r;
                    peak[r] = pk;
                }
            }
        }

        // 4) Label basins; BFS-flood the sub-CutFloor walkable slivers to the nearest region (height-gated).
        _label = new int[n];
        var regionOf = new Dictionary<int, int>();
        for (int i = 0; i < n; i++) _label[i] = -1;
        for (int qi = 0; qi < wcells.Count; qi++)
        {
            int i = wcells[qi];
            if (!seen[i]) continue;
            int r = find(i);
            int id;
            if (!regionOf.TryGetValue(r, out id)) { id = regionOf.Count; regionOf[r] = id; }
            _label[i] = id;
        }
        var q = new Queue<int>();
        for (int qi = 0; qi < wcells.Count; qi++) { int i = wcells[qi]; if (_label[i] >= 0) q.Enqueue(i); }
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int gz = i / W, gx = i % W;
            for (int k = 0; k < 8; k++)
            {
                int nz = gz + dz8[k], nx = gx + dx8[k];
                if (nz < 0 || nx < 0 || nz >= D || nx >= W) continue;
                int j = nz * W + nx;
                if (walk[j] && _label[j] < 0 && Math.Abs(cellY[j] - cellY[i]) <= DyGate)
                { _label[j] = _label[i]; q.Enqueue(j); }
            }
        }

        // 5) Merge small regions into a neighbour. Stair regions get a smaller floor; borders only count where the
        //    height is continuous; a region prefers a same-class (stairs vs flat) neighbour. Isolated tinies drop.
        int regions = regionOf.Count;
        var size = new int[regions];
        var slopedCells = new int[regions];
        var minY = new float[regions];
        var maxY = new float[regions];
        for (int r = 0; r < regions; r++) { minY[r] = float.MaxValue; maxY[r] = float.MinValue; }
        for (int qi = 0; qi < wcells.Count; qi++)
        {
            int i = wcells[qi];
            int l = _label[i];
            if (l < 0) continue;
            size[l]++;
            if (sloped[i]) slopedCells[l]++;
            if (cellY[i] < minY[l]) minY[l] = cellY[i];
            if (cellY[i] > maxY[l]) maxY[l] = cellY[i];
        }
        // Stairs = majority-sloped AND actually CLIMBING (steep lips / rubble ~0.6m read as part of their room).
        Func<int, bool> isStair = r => slopedCells[r] * 2 > size[r] && maxY[r] - minY[r] >= StairMinRise;
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int rid = 0; rid < regions; rid++)
            {
                if (size[rid] == 0) continue;
                float minArea = isStair(rid) ? MinStairArea : MinRoomArea;
                if (size[rid] * cell * cell >= minArea) continue;
                var border = new Dictionary<int, int>();
                for (int qi = 0; qi < wcells.Count; qi++)
                {
                    int i = wcells[qi];
                    if (_label[i] != rid) continue;
                    int gz = i / W, gx = i % W;
                    for (int k = 0; k < 8; k++)
                    {
                        int nz = gz + dz8[k], nx = gx + dx8[k];
                        if (nz < 0 || nx < 0 || nz >= D || nx >= W) continue;
                        int j = nz * W + nx;
                        int l = _label[j];
                        if (l >= 0 && l != rid && Math.Abs(cellY[j] - cellY[i]) <= DyGate)
                        { int cnt; border.TryGetValue(l, out cnt); border[l] = cnt + 1; }
                    }
                }
                int tgt = -1, btot = 0;
                foreach (var kv in border)
                    if (isStair(kv.Key) == isStair(rid) && kv.Value > btot) { btot = kv.Value; tgt = kv.Key; }
                if (tgt < 0)
                    foreach (var kv in border) if (kv.Value > btot) { btot = kv.Value; tgt = kv.Key; }
                for (int qi = 0; qi < wcells.Count; qi++) { int i = wcells[qi]; if (_label[i] == rid) _label[i] = tgt; }
                if (tgt >= 0)
                {
                    size[tgt] += size[rid];
                    slopedCells[tgt] += slopedCells[rid];
                    if (minY[rid] < minY[tgt]) minY[tgt] = minY[rid];
                    if (maxY[rid] > maxY[tgt]) maxY[tgt] = maxY[rid];
                }
                size[rid] = 0;
                changed = true;
            }
        }

        // 6) Stable numbering (centroid sort) + classification.
        var stats = new Dictionary<int, List<int>>();
        for (int qi = 0; qi < wcells.Count; qi++)
        {
            int i = wcells[qi];
            if (_label[i] < 0) continue;
            List<int> cells;
            if (!stats.TryGetValue(_label[i], out cells)) { cells = new List<int>(); stats[_label[i]] = cells; }
            cells.Add(i);
        }
        var infos = new List<KeyValuePair<int, Room>>();
        foreach (var kv in stats)
        {
            var cells = kv.Value;
            double sx = 0, sy = 0, sz = 0, sc = 0;
            foreach (var i in cells) { sx += wx[i]; sy += cellY[i]; sz += wz[i]; sc += clear[i]; }
            int cnt = cells.Count;
            double gmx = 0, gmz = 0;
            foreach (var i in cells) { gmx += i % W; gmz += i / W; }
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
            float meanClear = (float)(sc / cnt);
            string cls;
            if (isStair(kv.Key)) cls = "stairs";
            else if (elong > 2.6f && meanClear < 2.2f) cls = "passage";
            else if (elong > 3.2f) cls = "corridor";
            else if (area < 35f) cls = "small";
            else if (area > 220f) cls = "hall";
            else cls = "room";
            var room = new Room
            {
                ClassKey = cls,
                Area = area,
                Centroid = new Vector3((float)(sx / cnt), (float)(sy / cnt), (float)(sz / cnt)),
            };
            infos.Add(new KeyValuePair<int, Room>(kv.Key, room));
        }
        infos.Sort((p1, p2) =>
        {
            int c1 = p1.Value.Centroid.z.CompareTo(p2.Value.Centroid.z);
            return c1 != 0 ? c1 : p1.Value.Centroid.x.CompareTo(p2.Value.Centroid.x);
        });
        var remap = new Dictionary<int, int>();
        for (int k = 0; k < infos.Count; k++)
        {
            infos[k].Value.Id = k + 1;
            remap[infos[k].Key] = k;
            _rooms.Add(infos[k].Value);
        }
        for (int i = 0; i < n; i++)
            _label[i] = _label[i] >= 0 && remap.ContainsKey(_label[i]) ? remap[_label[i]] : -1;

        _w = W; _h = D; _cellY = cellY;
        BuildExits(W, D, cell, wx, wz, cellY, fence);
    }

    // 4-neighbourhood binary dilation/erosion passes (the slope mask's close-then-open).
    private static void Morph(bool[] m, int W, int D, int iters, bool dilate)
    {
        int n = W * D;
        var src = new bool[n];
        for (int it = 0; it < iters; it++)
        {
            Array.Copy(m, src, n);
            for (int i = 0; i < n; i++)
            {
                int gz = i / W, gx = i % W;
                if (dilate)
                    m[i] = src[i] || (gx > 0 && src[i - 1]) || (gx < W - 1 && src[i + 1])
                        || (gz > 0 && src[i - W]) || (gz < D - 1 && src[i + W]);
                else
                    m[i] = src[i] && (gx == 0 || src[i - 1]) && (gx == W - 1 || src[i + 1])
                        && (gz == 0 || src[i - W]) && (gz == D - 1 || src[i + W]);
            }
        }
    }

    // Exits from the grid boundary: every cell edge where one room's cell meets a different room's cell (height-
    // continuous, not fenced) is a threshold; the +x edge is engine dir East (fence bit 3), the +z edge is North
    // (fence bit 1). Boundary midpoints cluster per room pair by proximity — one wide doorway = one contiguous
    // cluster = one Exit; two separate doorways between the same pair = two Exits.
    private static void BuildExits(int W, int D, float cell, float[] wx, float[] wz, float[] cellY, byte[] fence)
    {
        if (_label == null) return;
        var bounds = new Dictionary<long, List<Vector3>>();
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
            {
                int i = z * W + x;
                int la = _label[i];
                if (la < 0) continue;
                if (x + 1 < W)
                {
                    int j = i + 1, lb = _label[j];
                    if (lb >= 0 && lb != la && Math.Abs(cellY[i] - cellY[j]) <= DyGate && (fence[i] & (1 << 3)) == 0)
                        AddBoundary(bounds, la, lb, wx, wz, cellY, i, j);
                }
                if (z + 1 < D)
                {
                    int j = i + W, lb = _label[j];
                    if (lb >= 0 && lb != la && Math.Abs(cellY[i] - cellY[j]) <= DyGate && (fence[i] & (1 << 1)) == 0)
                        AddBoundary(bounds, la, lb, wx, wz, cellY, i, j);
                }
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
                List<Vector3> g;
                if (!groups.TryGetValue(r, out g)) { g = new List<Vector3>(); groups[r] = g; }
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
                ra.Exits.Add(new Exit { Position = pos, To = rb });
                rb.Exits.Add(new Exit { Position = pos, To = ra });
            }
        }
    }

    private static void AddBoundary(Dictionary<long, List<Vector3>> map, int la, int lb,
        float[] wx, float[] wz, float[] cellY, int i, int j)
    {
        long key = ((long)Math.Min(la, lb) << 32) | (uint)Math.Max(la, lb);
        List<Vector3> pts;
        if (!map.TryGetValue(key, out pts)) { pts = new List<Vector3>(); map[key] = pts; }
        pts.Add(new Vector3((wx[i] + wx[j]) * 0.5f, (cellY[i] + cellY[j]) * 0.5f, (wz[i] + wz[j]) * 0.5f));
    }
}
