using System;
using System.Collections.Generic;

namespace Access.Core.Rooms;

/// <summary>
/// The room segmentation CORE: walkable mask + per-edge connectivity in, per-cell room labels and the boundary
/// edges between them out. Deliberately BCL-pure (no Unity, no Kingmaker) so it lives in <c>Access.Core</c>
/// alongside the graph core, shared across games — the per-game <c>RoomMap</c> keeps everything that needs
/// the engine (reading the live A* grid, world positions, naming, fog, speech) and this file keeps everything that
/// is arithmetic. Anything added here MUST stay BCL-pure or Access.Core stops building (that's the point).
///
/// Pipeline (descended from WrathAccess's <c>RoomMap</c> watershed, re-sourced onto RT's square grid):
/// connectivity mask → connected components → furniture mask → chamfer 3-4 distance transform (clearance) →
/// slope mask → persistence watershed as a level-by-level flood → merge regions joined by anything wider than a
/// doorway → merge undersized regions into a neighbour → emit the boundary edges.
///
/// THE THREE CORRECTNESS RULES, all learned the hard way:
/// 1. **Every adjacency question goes through the connectivity mask, never through raw grid neighbourhood.**
///    A cell pair the engine says a unit cannot step across must never join a room and must never produce a
///    boundary edge — that is exactly what made the V cycle announce openings the party could not walk through.
///    The one exemption, furniture bridging, is granted only against a connected-component PROOF of walk-around.
/// 2. **Clearance thresholds are expressed in CELLS, not metres.** The chamfer steps 3 per cardinal / 4 per
///    diagonal and is scaled by <c>cell/3</c>, so one step off a ridge costs a full cell of clearance. A metric
///    constant tuned on a 0.25 m raster (WrathAccess) is meaningless on RT's 1.35 m one.
/// 3. **Clearance depth alone cannot decide where a room ends on this raster** — the quantum is a third of a cell
///    and a doorway gap is exactly one cell deep, so no threshold separates "crate pinch" from "doorway". The
///    watershed therefore over-proposes and <see cref="MergeWideOpenings"/> decides, on the architectural question
///    instead: is what joins these two spaces a narrow gap cut through a wall, or is it just the space carrying on?
/// </summary>
public static class RoomSegmenter
{
    // --- neighbourhood convention (shared with RoomMap, which maps it onto the engine's own direction ids) ---
    // k: 0=S 1=N 2=W 3=E 4=SW 5=SE 6=NW 7=NE. The first four are the cardinals, in the order the boundary scan
    // and the engine-direction table both assume.
    public static readonly int[] Dz = { -1, 1, 0, 0, -1, -1, 1, 1 };
    public static readonly int[] Dx = { 0, 0, -1, 1, -1, 1, -1, 1 };
    /// <summary>k → the direction pointing back the other way (S↔N, W↔E, SW↔NE, SE↔NW).</summary>
    public static readonly int[] Opposite = { 1, 0, 3, 2, 7, 6, 5, 4 };

    // The ENGINE's own neighbour offsets, transcribed from CustomGridGraph's SetUpOffsetsAndCosts
    // (decompiled/Code/Kingmaker.Pathfinding/CustomGridGraph.cs:812-827). Its direction ids are
    // S=0 E=1 N=2 W=3 SE=4 NE=5 NW=6 SW=7 — a different order from ours, which is why the mapping below exists
    // rather than an implicit assumption. Kept here, beside the tables it must agree with, so a unit test can
    // check it: an error in one entry silently corrupts every room and is invisible at the call site.
    public static readonly int[] EngineDx = { 0, 1, 0, -1, 1, 1, -1, -1 };
    public static readonly int[] EngineDz = { -1, 0, 1, 0, -1, 1, 1, -1 };

    /// <summary>Our direction k → the engine direction id with the same offset. Index this with k to read
    /// <c>CustomGridNodeBase.HasConnectionInDirection</c>.</summary>
    public static readonly int[] EngineDir = { 0, 2, 3, 1, 7, 4, 6, 5 };

    public const int DirNorth = 1;  // +z, the boundary scan's second cardinal
    public const int DirEast = 3;   // +x, the boundary scan's first cardinal

    // --- tunables ---

    /// <summary>How much clearance (in CELLS) two basins must each rise above their shared saddle before the
    /// watershed PROPOSES them as separate rooms. On a 1.35 m raster this alone cannot decide the question: the
    /// clearance quantum is a third of a cell and a doorway gap is exactly one cell deep, so no value both keeps a
    /// crated hall whole (needs a high threshold) and separates a 4 m cabin from its corridor (needs a low one).
    /// So it is deliberately set LOW — it over-proposes — and <see cref="MaxOpeningCells"/> takes back the
    /// proposals that are not really rooms. (The shipped value was WrathAccess's 0.7 <em>metres</em>, tuned on its
    /// 0.25 m raster, where 2.8 cells of clearance made the depth signal alone sufficient.)</summary>
    public const float PersistCells = 1f;

    /// <summary>How wide (in CELLS) one continuous opening between two regions may be and still count as a
    /// DOORWAY rather than open space. Two regions stay separate only if what joins them is narrow: a wide
    /// walkable connection is not a room boundary at any clearance depth — it IS the room continuing. 3 cells
    /// ≈ 4 m, a double door or a modest archway; wider than that is one space. Measured per CONTIGUOUS run, so
    /// two separate doors between the same pair of rooms stay two doors rather than summing into an archway.</summary>
    public const int MaxOpeningCells = 3;

    // …and, because absolute width alone cannot tell a 2-cell door between two cabins from a 3-cell corridor
    // pinched to 2 cells by a crate (both are two cells), an opening must also be a gap in an actual WALL:
    // no wider than the blocked frontier beside it. A constriction with more gap than wall is not a doorway,
    // it is the space carrying on. Where the two rooms are divided by something thicker than one cell there is
    // no measurable frontier at all, and the absolute rule decides alone. See MergeWideOpenings.

    /// <summary>Clearance (in CELLS) below which a cell never seeds a basin and is flooded in afterwards. On RT's
    /// raster the smallest clearance ANY walkable cell can have is exactly 1 cell (one orthogonal step from a
    /// wall), so at ≤1 this is inert by construction and every walkable cell seeds — which is what we want here:
    /// raising it far enough to bite would dissolve genuine small cabins (a 3×3-cell room is 4 m across and has
    /// 1-cell clearance throughout) into whatever corridor they open onto. Kept as a live floor, and the flood
    /// below it kept as the safety net, so the knob still exists if a finer raster ever shows up.</summary>
    public const float CutFloorCells = 1f / 3f;

    /// <summary>Interior unwalkable islands up to this many cells cast no clearance shadow — crates, consoles,
    /// pillars you walk around. 9 = a 3×3 pillar. Only islands fully enclosed by walkable space qualify; anything
    /// reaching the grid border is structure.</summary>
    public const int FurnitureMaxCells = 9;

    public const float MinRoomArea = 12f;    // m² — smaller regions merge into a neighbour
    public const float MinStairArea = 2.5f;  // m² — stair regions get a lower floor than flat ones
    public const float SlopeT = 0.35f;       // rise/run above which a cell is sloped (stairs ~0.6-0.8)
    public const float StairMinRise = 1.5f;  // m a sloped region must CLIMB to count as stairs (bumps ~0.6 m)

    /// <summary>One cell-edge where two rooms meet: the labels either side plus the two cell indices, so the
    /// caller can place the threshold in world space. Emitted only for cardinal edges the engine says are
    /// crossable.</summary>
    public struct Edge
    {
        public int A, B;          // region labels (A = the lower-index cell's)
        public int CellI, CellJ;  // row-major cell indices; CellJ is CellI's +x or +z neighbour
    }

    public sealed class Result
    {
        public int[] Label;             // per-cell region index, -1 = unwalkable; labels may have gaps after merging
        public int Regions;             // upper bound on label values (indexing IsStair)
        public bool[] IsStair;          // per region id
        public byte[] Conn;             // the SYMMETRISED connectivity mask actually used (bit k = step to dir k)
        public float[] Clear;           // per-cell clearance in metres (distance to the nearest wall)
        public List<Edge> Boundaries = new List<Edge>();
        public int FloodedCells;        // diagnostic: cells assigned by the sub-CutFloor flood (0 on RT's raster)
        public bool MergeCapHit;        // diagnostic: the wide-opening merge ran out of rounds (normally 2 suffice)
    }

    /// <summary>
    /// Segment a walkable grid into rooms.
    /// <paramref name="conn"/> is the per-cell 8-bit "a unit can step this way" mask in <see cref="Dz"/>/<see cref="Dx"/>
    /// order — in production it comes straight off the engine's own connection bits. It is symmetrised here (an edge
    /// counts only when BOTH cells agree), so a caller may pass a one-sided mask.
    /// </summary>
    public static Result Segment(bool[] walk, float[] cellY, byte[] conn, int w, int d, float cell)
    {
        if (walk == null) throw new ArgumentNullException(nameof(walk));
        if (cellY == null) throw new ArgumentNullException(nameof(cellY));
        if (conn == null) throw new ArgumentNullException(nameof(conn));
        int n = w * d;
        if (n <= 0 || walk.Length < n || cellY.Length < n || conn.Length < n)
            throw new ArgumentException("grid arrays must cover w*d cells");

        var res = new Result();

        // 0) Symmetrise the connectivity mask: an edge is usable only if both ends agree. The engine writes each
        //    node's bits independently, so this also makes the whole pipeline direction-agnostic — the watershed
        //    visits a pair from whichever side happens to come first, and must get the same answer either way.
        var link = new byte[n];
        for (int i = 0; i < n; i++)
        {
            if (!walk[i]) continue;
            int gz = i / w, gx = i % w;
            int bits = 0;
            for (int k = 0; k < 8; k++)
            {
                if ((conn[i] & (1 << k)) == 0) continue;
                int nz = gz + Dz[k], nx = gx + Dx[k];
                if (nz < 0 || nx < 0 || nz >= d || nx >= w) continue;
                int j = nz * w + nx;
                if (!walk[j] || (conn[j] & (1 << Opposite[k])) == 0) continue;
                bits |= 1 << k;
            }
            link[i] = (byte)bits;
        }
        res.Conn = link;

        var wcells = new List<int>();
        for (int i = 0; i < n; i++) if (walk[i]) wcells.Add(i);
        if (wcells.Count == 0)
        {
            res.Label = new int[n];
            for (int i = 0; i < n; i++) res.Label[i] = -1;
            res.IsStair = new bool[0];
            res.Clear = new float[n];
            return res;
        }

        // Connected components of the crossable graph — the ground truth for "can a unit get from here to there
        // at all". Used to prove that a furniture island really is something you walk AROUND before letting it
        // bridge the watershed across itself.
        var comp = Components(walk, link, w, d);
        Furniture(walk, link, comp, w, d, out var noShadow, out var bridge);
        var clear = Clearance(walk, noShadow, w, d, cell);
        var sloped = Slope(walk, link, cellY, wcells, w, d, cell);
        res.Clear = clear;

        // 1) Persistence watershed, as a LEVEL-BY-LEVEL FLOOD. Cells are taken in descending clearance, and each
        //    basin grows into a cell only if the two are connected; two basins meeting at a saddle merge unless
        //    both peaks stand PersistCells of clearance above it.
        //
        //    Within one clearance level the cells are flooded BREADTH-FIRST outward from the already-assigned
        //    ground above, not swept in index order. That matters more than it sounds: clearance on a 1.35 m
        //    raster is quantised to a third of a cell, so whole rooms are one flat plateau, and an index sweep
        //    hands plateau cells to whichever basin the scan happens to reach first. A doorway near a room's
        //    corner was enough for one room to reach through it and claim the whole edge row of its neighbour,
        //    after which the "boundary" between them ran somewhere arbitrary inside the second room. Flooding by
        //    distance instead assigns every plateau cell to the basin it is actually nearest to.
        //
        //    Furniture islands take part as BRIDGES. Exempting them from casting a clearance shadow is not enough
        //    on its own: their cells are still unwalkable, so they still sever the clearance ridge they sit on and
        //    a crate in the middle of a hall still cut it in two. An island proven walk-around may carry a basin
        //    across itself. It never receives a label — only real floor does — and it can never produce a
        //    threshold, so this cannot invent an exit.
        float persist = PersistCells * cell;
        float cutFloor = CutFloorCells * cell;
        var order = new List<int>(wcells.Count);
        for (int i = 0; i < n; i++) if (walk[i] || bridge[i]) order.Add(i);
        order.Sort((a, b) =>
        {
            int c = clear[b].CompareTo(clear[a]);
            return c != 0 ? c : a.CompareTo(b);
        });

        var parent = new int[n];
        var peak = new float[n];
        var seen = new bool[n];
        var queued = new bool[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        var wave = new Queue<int>();

        // Can a basin grow between these two cells? The ENGINE decides between two real cells; an edge touching a
        // bridging island is exempt, and a staircase never joins flat floor.
        bool Joins(int a, int b, int k)
            => (bridge[a] || bridge[b] || (link[a] & (1 << k)) != 0) && sloped[a] == sloped[b];

        void Absorb(int i, float c)
        {
            seen[i] = true;
            peak[i] = c;
            int gz = i / w, gx = i % w;
            int me = Find(parent, i);
            for (int k = 0; k < 8; k++)
            {
                int nz = gz + Dz[k], nx = gx + Dx[k];
                if (nz < 0 || nx < 0 || nz >= d || nx >= w) continue;
                int j = nz * w + nx;
                if (!seen[j] || !Joins(i, j, k)) continue;
                int r = Find(parent, j);
                if (r == me) continue;
                if (sloped[i] || Math.Min(peak[r], peak[me]) - c < persist)
                {
                    peak[r] = Math.Max(peak[r], peak[me]);
                    parent[me] = r;
                    me = r;
                }
            }
        }

        // Drain the current wave, pulling in same-level neighbours as it goes.
        void Flood(float level)
        {
            while (wave.Count > 0)
            {
                int i = wave.Dequeue();
                Absorb(i, level);
                int gz = i / w, gx = i % w;
                for (int k = 0; k < 8; k++)
                {
                    int nz = gz + Dz[k], nx = gx + Dx[k];
                    if (nz < 0 || nx < 0 || nz >= d || nx >= w) continue;
                    int j = nz * w + nx;
                    if (queued[j] || seen[j] || clear[j] != level || !Joins(i, j, k)) continue;
                    if (!walk[j] && !bridge[j]) continue;
                    queued[j] = true;
                    wave.Enqueue(j);
                }
            }
        }

        for (int at = 0; at < order.Count;)
        {
            float level = clear[order[at]];
            if (level < cutFloor) break;
            int from = at;
            while (at < order.Count && clear[order[at]] == level) at++;

            // Seed with the cells of this level that touch ground already assigned above, so the flood spreads
            // downhill from every basin at once.
            for (int t = from; t < at; t++)
            {
                int i = order[t];
                int gz = i / w, gx = i % w;
                for (int k = 0; k < 8; k++)
                {
                    int nz = gz + Dz[k], nx = gx + Dx[k];
                    if (nz < 0 || nx < 0 || nz >= d || nx >= w) continue;
                    int j = nz * w + nx;
                    if (!seen[j] || !Joins(i, j, k)) continue;
                    queued[i] = true;
                    wave.Enqueue(i);
                    break;
                }
            }
            Flood(level);

            // Whatever this level did not reach is a local maximum — a new basin. Index order here is only a
            // tie-break between genuinely independent peaks, so it cannot bias a boundary.
            for (int t = from; t < at; t++)
            {
                int i = order[t];
                if (seen[i]) continue;
                queued[i] = true;
                wave.Enqueue(i);
                Flood(level);
            }
        }

        // 2) Label the basins, then flood whatever the seed floor skipped into the nearest region.
        var label = new int[n];
        for (int i = 0; i < n; i++) label[i] = -1;
        var regionOf = new Dictionary<int, int>();
        foreach (var i in wcells)
        {
            if (!seen[i]) continue;
            int r = Find(parent, i);
            if (!regionOf.TryGetValue(r, out int id)) { id = regionOf.Count; regionOf[r] = id; }
            label[i] = id;
        }
        int regions = regionOf.Count;

        var q = new Queue<int>();
        foreach (var i in wcells) if (label[i] >= 0) q.Enqueue(i);
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int gz = i / w, gx = i % w;
            for (int k = 0; k < 8; k++)
            {
                if ((link[i] & (1 << k)) == 0) continue;
                int j = (gz + Dz[k]) * w + (gx + Dx[k]);
                if (label[j] >= 0) continue;
                label[j] = label[i];
                res.FloodedCells++;
                q.Enqueue(j);
            }
        }

        res.MergeCapHit = MergeWideOpenings(label, link, sloped, regions, w, d);
        var isStair = MergeSmall(label, cellY, sloped, link, regions, w, cell);

        res.Label = label;
        res.Regions = regions;
        res.IsStair = isStair;
        Boundaries(label, link, w, d, res.Boundaries);
        return res;
    }

    private static int Find(int[] parent, int a)
    {
        while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
        return a;
    }

    // Order-independent key for a pair of region labels.
    private static long Key(int a, int b) => ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);

    // Connected components of the crossable graph: which cells a unit can actually reach from which.
    private static int[] Components(bool[] walk, byte[] link, int w, int d)
    {
        int n = w * d;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        for (int i = 0; i < n; i++)
        {
            if (!walk[i]) continue;
            int gz = i / w, gx = i % w;
            for (int k = 0; k < 8; k++)
            {
                if ((link[i] & (1 << k)) == 0) continue;
                int a = Find(parent, i), b = Find(parent, (gz + Dz[k]) * w + (gx + Dx[k]));
                if (a != b) parent[a] = b;
            }
        }
        var comp = new int[n];
        for (int i = 0; i < n; i++) comp[i] = walk[i] ? Find(parent, i) : -1;
        return comp;
    }

    // Small interior unwalkable ISLANDS — crates, consoles, pillars. Two separate outputs:
    //   noShadow: the island casts no clearance shadow, so the pinch beside it is not read as a doorway.
    //   bridge:   the island may additionally carry a basin ACROSS itself in the watershed. Suppressing the
    //             shadow is not enough on its own — the cells are still unwalkable and still sever the clearance
    //             ridge they sit on, so a crate in the middle of a hall still cut it in two.
    // Bridging is granted only on PROOF of walk-around: every walkable cell around the island must be in one
    // connected component. "Fully enclosed by floor" does not imply it — a prop standing against a railing or
    // astride a height step also has floor all round it, and bridging there would union two spaces the party
    // cannot move between, which is the exact bug this whole rewrite exists to remove.
    private static void Furniture(bool[] walk, byte[] link, int[] comp, int w, int d,
        out bool[] noShadow, out bool[] bridge)
    {
        int n = w * d;
        noShadow = new bool[n];
        bridge = new bool[n];
        var visited = new bool[n];
        var stack = new Stack<int>();
        var blob = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (walk[i] || visited[i]) continue;
            blob.Clear();
            bool touchesBorder = false;
            visited[i] = true; stack.Push(i);
            int cells = 0;
            while (stack.Count > 0)
            {
                int j = stack.Pop();
                // The flood must run to completion to mark `visited` (or the outer loop re-enters the blob), but
                // only a furniture-sized blob is ever read back. Recording a whole level's solid rock here would
                // grow this list into megabytes of large-object garbage per build for nothing.
                cells++;
                if (cells <= FurnitureMaxCells) blob.Add(j);
                int gz = j / w, gx = j % w;
                if (gz == 0 || gx == 0 || gz == d - 1 || gx == w - 1) touchesBorder = true;
                for (int k = 0; k < 8; k++)
                {
                    int nz = gz + Dz[k], nx = gx + Dx[k];
                    if (nz < 0 || nx < 0 || nz >= d || nx >= w) continue;
                    int m = nz * w + nx;
                    if (!walk[m] && !visited[m]) { visited[m] = true; stack.Push(m); }
                }
            }
            if (touchesBorder || cells > FurnitureMaxCells) continue;
            foreach (var j in blob) noShadow[j] = true;

            int ring = -1;
            bool oneComponent = true;
            foreach (var j in blob)
            {
                int gz = j / w, gx = j % w;
                for (int k = 0; k < 8 && oneComponent; k++)
                {
                    int nz = gz + Dz[k], nx = gx + Dx[k];
                    if (nz < 0 || nx < 0 || nz >= d || nx >= w) continue;
                    int m = nz * w + nx;
                    if (!walk[m]) continue;
                    if (ring < 0) ring = comp[m];
                    else if (comp[m] != ring) oneComponent = false;
                }
                if (!oneComponent) break;
            }
            if (oneComponent && ring >= 0)
                foreach (var j in blob) bridge[j] = true;
        }
    }

    // Two regions are separate ROOMS only if what joins them is narrow enough to be a doorway. The watershed
    // deliberately over-proposes splits (see PersistCells); this takes back every proposal whose "threshold" is
    // wider than MaxOpeningCells, because a wide walkable connection is not a room boundary at any clearance
    // depth — it is the room continuing. This is what recognises the crate-in-a-hall seam, whose boundary runs
    // the entire width of the room, as the artefact it is.
    // Merging never crosses the stairs/flat divide, so a staircase keeps its own identity.
    /// <summary>Returns true if the round budget was exhausted with merging still to do.</summary>
    private static bool MergeWideOpenings(int[] label, byte[] link, bool[] sloped, int regions, int w, int d)
    {
        if (regions <= 1) return false;
        for (int round = 0; round < 16; round++)
        {
            var edges = new Dictionary<long, List<Edge>>();
            var walls = new Dictionary<long, int>();
            var size = new int[regions];
            var slopedN = new int[regions];
            for (int i = 0; i < label.Length; i++)
            {
                int la = label[i];
                if (la < 0) continue;
                size[la]++;
                if (sloped[i]) slopedN[la]++;
                // Measure the WALL these two regions share: cells of one facing cells of the other across a
                // single blocked step. Only the positive cardinals, so each facing pair is counted once.
                int gz = i / w, gx = i % w;
                for (int t = 0; t < 2; t++)
                {
                    int k = t == 0 ? DirEast : DirNorth;
                    int fz = gz + 2 * Dz[k], fx = gx + 2 * Dx[k];
                    if (fz < 0 || fx < 0 || fz >= d || fx >= w) continue;
                    int mid = (gz + Dz[k]) * w + (gx + Dx[k]);
                    int lb = label[fz * w + fx];
                    if (lb < 0 || lb == la) continue;
                    if ((link[i] & (1 << k)) != 0 && (link[mid] & (1 << k)) != 0) continue; // a way through, not a wall
                    long wk = Key(la, lb);
                    walls.TryGetValue(wk, out int c);
                    walls[wk] = c + 1;
                }
            }
            Boundaries(label, link, w, d, null, edges);

            var parent = new int[regions];
            for (int r = 0; r < regions; r++) parent[r] = r;
            bool merged = false;
            foreach (var kv in edges)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xFFFFFFFF);
                if (slopedN[a] * 2 > size[a] != slopedN[b] * 2 > size[b]) continue;   // never merge stairs into floor
                int open = WidestRun(kv.Value, w, d);
                walls.TryGetValue(kv.Key, out int wall);
                // A doorway is a gap in a wall: narrow in absolute terms, and no wider than the wall it is cut
                // through. wall == 0 means the two rooms are divided by something thicker than one cell, which
                // leaves no measurable frontier — then the absolute rule decides on its own.
                bool doorway = open <= MaxOpeningCells && (wall == 0 || open <= wall);
                if (doorway) continue;
                int ra = Find(parent, a), rb = Find(parent, b);
                if (ra == rb) continue;
                parent[ra] = rb;
                merged = true;
            }
            if (!merged) return false;
            for (int i = 0; i < label.Length; i++)
                if (label[i] >= 0) label[i] = Find(parent, label[i]);
        }
        // Falling out of the loop means merging was still happening at the cap. The labelling is consistent
        // (every round relabels before it ends), just under-merged — but it means the geometry defeated the
        // budget, which is worth knowing about rather than silently shipping extra rooms.
        return true;
    }

    // The widest CONTIGUOUS run among a region pair's boundary edges — two separate doors between the same two
    // rooms are two openings of one cell, not one opening of two.
    private static int WidestRun(List<Edge> edges, int w, int d)
    {
        var cells = new HashSet<int>();
        foreach (var e in edges) cells.Add(e.CellI);
        int best = 0;
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        foreach (var start in cells)
        {
            if (!seen.Add(start)) continue;
            int run = 0;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                run++;
                int gz = c / w, gx = c % w;
                for (int k = 0; k < 8; k++)
                {
                    int nz = gz + Dz[k], nx = gx + Dx[k];
                    if (nz < 0 || nx < 0 || nz >= d || nx >= w) continue;   // or a row edge wraps into the next
                    int m = nz * w + nx;
                    if (cells.Contains(m) && seen.Add(m)) stack.Push(m);
                }
            }
            if (run > best) best = run;
        }
        return best;
    }

    // Chamfer 3-4 distance transform → clearance in metres (distance to the nearest wall). The 3/4 weights make
    // one cardinal step cost a full cell and one diagonal step 4/3 of a cell once scaled by cell/3.
    private static float[] Clearance(bool[] walk, bool[] noShadow, int w, int d, float cell)
    {
        int n = w * d;
        var dist = new int[n];
        const int INF = int.MaxValue / 4;
        for (int i = 0; i < n; i++) dist[i] = (walk[i] || noShadow[i]) ? INF : 0;
        for (int gz = 0; gz < d; gz++)
            for (int gx = 0; gx < w; gx++)
            {
                int i = gz * w + gx;
                if (dist[i] == 0) continue;
                int best = dist[i];
                if (gx > 0) best = Math.Min(best, dist[i - 1] + 3);
                if (gz > 0)
                {
                    best = Math.Min(best, dist[i - w] + 3);
                    if (gx > 0) best = Math.Min(best, dist[i - w - 1] + 4);
                    if (gx < w - 1) best = Math.Min(best, dist[i - w + 1] + 4);
                }
                dist[i] = best;
            }
        for (int gz = d - 1; gz >= 0; gz--)
            for (int gx = w - 1; gx >= 0; gx--)
            {
                int i = gz * w + gx;
                if (dist[i] == 0) continue;
                int best = dist[i];
                if (gx < w - 1) best = Math.Min(best, dist[i + 1] + 3);
                if (gz < d - 1)
                {
                    best = Math.Min(best, dist[i + w] + 3);
                    if (gx < w - 1) best = Math.Min(best, dist[i + w + 1] + 4);
                    if (gx > 0) best = Math.Min(best, dist[i + w - 1] + 4);
                }
                dist[i] = best;
            }
        var clear = new float[n];
        for (int i = 0; i < n; i++) clear[i] = dist[i] * (cell / 3f);
        return clear;
    }

    // Cells on a sustained height gradient (geometry renders staircases as ramps). Close-then-open at one cell —
    // on a 1.35 m raster one cell already smears further than WrathAccess's two 0.25 m ones did.
    private static bool[] Slope(bool[] walk, byte[] link, float[] cellY, List<int> wcells, int w, int d, float cell)
    {
        int n = w * d;
        var sloped = new bool[n];
        foreach (var i in wcells)
        {
            int gz = i / w, gx = i % w;
            float dy = 0f;
            // Only over edges a unit can actually take. A railing or an over-climb step is a WALL, not a ramp —
            // measuring the drop across one used to mark dead-flat floor as sloped, and because the slope mask
            // hard-partitions the watershed that band then read as a room boundary on continuous floor.
            for (int k = 0; k < 4; k++)
            {
                if ((link[i] & (1 << k)) == 0) continue;
                int j = (gz + Dz[k]) * w + (gx + Dx[k]);
                dy = Math.Max(dy, Math.Abs(cellY[i] - cellY[j]));
            }
            sloped[i] = dy / cell > SlopeT;
        }
        var scratch = new bool[n];
        Morph(sloped, scratch, w, d, dilate: true); Morph(sloped, scratch, w, d, dilate: false);
        for (int i = 0; i < n; i++) sloped[i] &= walk[i];
        Morph(sloped, scratch, w, d, dilate: false); Morph(sloped, scratch, w, d, dilate: true);
        for (int i = 0; i < n; i++) sloped[i] &= walk[i];
        return sloped;
    }

    // One 4-neighbourhood binary dilation/erosion pass. `src` is caller-owned scratch, reused across the four
    // passes — it is fully overwritten here, so its incoming contents are irrelevant.
    private static void Morph(bool[] m, bool[] src, int w, int d, bool dilate)
    {
        int n = w * d;
        Array.Copy(m, src, n);
        for (int i = 0; i < n; i++)
        {
            int gz = i / w, gx = i % w;
            if (dilate)
                m[i] = src[i] || (gx > 0 && src[i - 1]) || (gx < w - 1 && src[i + 1])
                    || (gz > 0 && src[i - w]) || (gz < d - 1 && src[i + w]);
            else
                m[i] = src[i] && (gx == 0 || src[i - 1]) && (gx == w - 1 || src[i + 1])
                    && (gz == 0 || src[i - w]) && (gz == d - 1 || src[i + w]);
        }
    }

    // Merge undersized regions into their biggest CONNECTED neighbour, preferring one of the same class (stairs vs
    // flat). A region with no connected neighbour is KEPT rather than dropped: a cell that belongs to no room makes
    // "where am I" and the exit cycle silently fail there, which is worse than naming a small isolated ledge.
    private static bool[] MergeSmall(int[] label, float[] cellY, bool[] sloped, byte[] link,
        int regions, int w, float cell)
    {
        var cells = new List<int>[regions];
        for (int r = 0; r < regions; r++) cells[r] = new List<int>();
        var slopedCells = new int[regions];
        var minY = new float[regions];
        var maxY = new float[regions];
        for (int r = 0; r < regions; r++) { minY[r] = float.MaxValue; maxY[r] = float.MinValue; }
        for (int i = 0; i < label.Length; i++)
        {
            int l = label[i];
            if (l < 0) continue;
            cells[l].Add(i);
            if (sloped[i]) slopedCells[l]++;
            if (cellY[i] < minY[l]) minY[l] = cellY[i];
            if (cellY[i] > maxY[l]) maxY[l] = cellY[i];
        }

        // Stairs = majority-sloped AND actually climbing (steep lips / rubble read as part of their room).
        Func<int, bool> isStair = r =>
            cells[r].Count > 0 && slopedCells[r] * 2 > cells[r].Count && maxY[r] - minY[r] >= StairMinRise;

        float cellArea = cell * cell;
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int rid = 0; rid < regions; rid++)
            {
                if (cells[rid].Count == 0) continue;
                float minArea = isStair(rid) ? MinStairArea : MinRoomArea;
                if (cells[rid].Count * cellArea >= minArea) continue;

                var border = new Dictionary<int, int>();
                foreach (var i in cells[rid])
                {
                    int gz = i / w, gx = i % w;
                    for (int k = 0; k < 8; k++)
                    {
                        if ((link[i] & (1 << k)) == 0) continue;
                        int j = (gz + Dz[k]) * w + (gx + Dx[k]);
                        int l = label[j];
                        if (l < 0 || l == rid) continue;
                        border.TryGetValue(l, out int cnt);
                        border[l] = cnt + 1;
                    }
                }
                int tgt = -1, btot = 0;
                foreach (var kv in border)
                    if (isStair(kv.Key) == isStair(rid) && kv.Value > btot) { btot = kv.Value; tgt = kv.Key; }
                if (tgt < 0)
                    foreach (var kv in border) if (kv.Value > btot) { btot = kv.Value; tgt = kv.Key; }
                if (tgt < 0) continue;   // isolated: keep it as its own room rather than deleting its cells

                foreach (var i in cells[rid]) label[i] = tgt;
                cells[tgt].AddRange(cells[rid]);
                slopedCells[tgt] += slopedCells[rid];
                if (minY[rid] < minY[tgt]) minY[tgt] = minY[rid];
                if (maxY[rid] > maxY[tgt]) maxY[tgt] = maxY[rid];
                cells[rid].Clear();
                changed = true;
            }
        }

        var stairs = new bool[regions];
        for (int r = 0; r < regions; r++) stairs[r] = isStair(r);
        return stairs;
    }

    // Every CROSSABLE cardinal edge where one room's cell meets a different room's cell is a threshold. The
    // connectivity gate is the whole point: grid adjacency alone happily emits a doorway through a solid wall.
    private static void Boundaries(int[] label, byte[] link, int w, int d,
        List<Edge> into, Dictionary<long, List<Edge>> byPair = null)
    {
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                int la = label[i];
                if (la < 0) continue;
                if (x + 1 < w && (link[i] & (1 << DirEast)) != 0) Emit(i, i + 1);
                if (z + 1 < d && (link[i] & (1 << DirNorth)) != 0) Emit(i, i + w);

                void Emit(int a, int b)
                {
                    int lb = label[b];
                    if (lb < 0 || lb == la) return;
                    var edge = new Edge { A = la, B = lb, CellI = a, CellJ = b };
                    into?.Add(edge);
                    if (byPair == null) return;
                    long key = ((long)Math.Min(la, lb) << 32) | (uint)Math.Max(la, lb);
                    if (!byPair.TryGetValue(key, out var list)) { list = new List<Edge>(); byPair[key] = list; }
                    list.Add(edge);
                }
            }
    }
}
