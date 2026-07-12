using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// Finds UNEXPLORED WALKABLE space — the frontier where exploration can continue. A frontier cell is a walkable
/// <see cref="RoomMap"/> cell that the game's explored layer (<see cref="FogExplored"/>, the fog mask's green
/// channel) says is unexplored, 8-adjacent to an explored walkable cell — i.e. ground you could stand on today
/// and push the fog back. That definition inherently excludes fog over unreachable dressing (not walkable) and
/// sealed pockets (no explored neighbour). Cells cluster into BLOBS ("openings"), each surfaced as one scanner
/// item in the "Unexplored space" category (see <see cref="ProxyFrontier"/> / <c>Scanner.FrontierList</c>).
/// Recomputed lazily on demand (a key press), cached briefly — never per-frame work. Port of WrathAccess's
/// <c>FrontierModel</c> onto RT's native grid; the substrate differences are RoomMap's (its grid IS the graph).
/// </summary>
internal static class FrontierModel
{
    private const float CacheSec = 2f;  // key-press bursts reuse one computation
    private const int MinCells = 2;     // the frontier is a ~1-cell-thick RIBBON across an opening; threshold its
                                        // LENGTH in cells. RT's native 1.35 m cells are coarser than WA's navmesh
                                        // raster, so 2 cells (~2.7 m) drops lone specks but keeps a single-door
                                        // opening's short ribbon (WA's 3-cell floor here would eat doorways).

    internal sealed class Blob
    {
        public Vector3 Position;  // a frontier cell near the blob's centroid (walkable ground)
        public float Reach;       // max centroid→cell distance (the blob's spatial extent)
        public RoomMap.Room Room; // the room the blob's ground belongs to (may be unentered), or null
    }

    private static readonly List<Blob> _blobs = new List<Blob>();
    private static readonly List<Blob> _prev = new List<Blob>(); // last generation, for identity matching
    private static float _nextAt;

    /// <summary>The cached frontier blobs. NEVER recomputes — a full-grid recompute is key-press work
    /// (<see cref="Refresh"/>), not frame work.</summary>
    public static IReadOnlyList<Blob> Current => _blobs;

    /// <summary>Is this blob still in the cached frontier? The scanner's selection resolve — a blob object
    /// survives recomputes while its opening persists (see <see cref="AddBlob"/>), so a held selection stays
    /// valid until the fog is actually pushed past it.</summary>
    public static bool Contains(Blob blob)
    {
        for (int i = 0; i < _blobs.Count; i++)
            if (ReferenceEquals(_blobs[i], blob)) return true;
        return false;
    }

    /// <summary>Drop everything — called by <see cref="RoomMap"/> whenever its grid drops (area/part change),
    /// so no blob survives at stale coordinates.</summary>
    public static void Invalidate()
    {
        _blobs.Clear();
        _prev.Clear();
        _nextAt = 0f;
    }

    /// <summary>Recompute the frontier if the cache is stale — called by the category browse before it rebuilds,
    /// so repeated presses within a burst reuse one computation.</summary>
    public static void Refresh()
    {
        if (Time.unscaledTime < _nextAt) return;
        _nextAt = Time.unscaledTime + CacheSec;
        Recompute();
    }

    private static void Recompute()
    {
        _prev.Clear();
        _prev.AddRange(_blobs);
        _blobs.Clear();
        if (!RoomMap.TryGetGrid(out var label, out _, out int w, out int h)) return;
        float cell = RoomMap.CellSize;
        if (cell <= 0f) return;
        if (!FogExplored.Ensure()) { _prev.Clear(); return; } // no fog in this area → nothing is unexplored

        // Pass 1: classify each walkable cell explored/unexplored via the game's explored layer.
        // (FogExplored is a 256² lookup — cheap per cell; the whole pass is on-demand only.)
        var state = new byte[label.Length]; // 0 = not walkable, 1 = explored, 2 = unexplored
        for (int gz = 0; gz < h; gz++)
            for (int gx = 0; gx < w; gx++)
            {
                int i = gz * w + gx;
                if (label[i] < 0) continue;
                state[i] = FogExplored.IsExplored(RoomMap.CellCenter(gx, gz)) ? (byte)1 : (byte)2;
            }

        // Pass 2: frontier = unexplored cells 8-adjacent to an explored cell.
        var frontier = new bool[label.Length];
        for (int gz = 1; gz < h - 1; gz++)
            for (int gx = 1; gx < w - 1; gx++)
            {
                int i = gz * w + gx;
                if (state[i] != 2) continue;
                for (int dz = -1; dz <= 1 && !frontier[i]; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (state[i + dz * w + dx] == 1) { frontier[i] = true; break; }
            }

        // Pass 3: flood-fill frontier cells into blobs (8-way), keep the meaningful ones.
        var stack = new Stack<int>();
        var cells = new List<int>();
        for (int start = 0; start < frontier.Length; start++)
        {
            if (!frontier[start]) continue;
            cells.Clear();
            stack.Push(start);
            frontier[start] = false;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                cells.Add(i);
                int cz = i / w, cx = i % w;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nz = cz + dz, nx = cx + dx;
                        if (nz < 0 || nx < 0 || nz >= h || nx >= w) continue;
                        int n = nz * w + nx;
                        if (frontier[n]) { frontier[n] = false; stack.Push(n); }
                    }
            }
            if (cells.Count < MinCells) continue;

            // Centroid, then the blob CELL nearest it (so the item sits on walkable frontier ground).
            Vector3 sum = Vector3.zero;
            foreach (var i in cells) sum += RoomMap.CellCenter(i % w, i / w);
            Vector3 centroid = sum / cells.Count;
            Vector3 best = centroid; float bestD = float.MaxValue, reach = 0f;
            foreach (var i in cells)
            {
                var p = RoomMap.CellCenter(i % w, i / w);
                float dx2 = p.x - centroid.x, dz2 = p.z - centroid.z;
                float d = dx2 * dx2 + dz2 * dz2;
                if (d < bestD) { bestD = d; best = p; }
                if (d > reach) reach = d;
            }
            AddBlob(best, Mathf.Sqrt(reach), cell);
        }
        _prev.Clear();
    }

    /// <summary>Record a blob, reusing the previous generation's object when this is the same opening (nearest
    /// old blob whose extent overlaps ours — exact when standing still, and a receding ribbon still lands within
    /// the joined extents). Identity is what the selection keys on — new objects every press would drop the held
    /// selection. Fields update in place; a split opening keeps the old identity for one half, mints the other;
    /// a vanished one is simply never claimed and drops out.</summary>
    private static void AddBlob(Vector3 pos, float reach, float cell)
    {
        Blob match = null; float bestD = float.MaxValue;
        foreach (var old in _prev)
        {
            float dx = old.Position.x - pos.x, dz = old.Position.z - pos.z;
            float d = dx * dx + dz * dz;
            float tol = old.Reach + reach + 2f * cell;
            if (d <= tol * tol && d < bestD) { bestD = d; match = old; }
        }
        if (match != null) _prev.Remove(match); else match = new Blob();
        match.Position = pos; match.Reach = reach; match.Room = RoomMap.RoomAt(pos);
        _blobs.Add(match);
    }
}
