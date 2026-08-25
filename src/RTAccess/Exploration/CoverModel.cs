using Kingmaker;
using Kingmaker.EntitySystem.Entities;   // BaseUnitEntity (the observer whose own tile stays listable)
using Kingmaker.Pathfinding;             // CustomGridNodeBase / CustomGridGraph / GetUnit / GraphParamsMechanicsCache
using Kingmaker.View.Covers;             // LosCalculations — the game's own per-edge cover oracle
using RTAccess.Accessibility;            // InteractableDescriber (the shared overlay gate + check type)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// Finds COVER POSITIONS — the walkable cells you could stand on to be behind something. A cover cell is a
/// walkable grid cell with half/full (or sight-blocking) cover on at least one cardinal edge, read from the very
/// oracle the game's own cover meshes use (<see cref="LosCalculations.GetCellCoverStatus"/>, exactly as
/// <c>CoverVisualizer.UpdateCoverMeshes</c> calls it). Cells whose cover SIGNATURE is identical and which touch
/// each other collapse into one <see cref="Spot"/>, so a ten-tile wall is one entry ("half cover north, 10 tiles
/// wide") while the four sides of a crate stay four distinct entries — one per cover SIDE, which is what the
/// player is actually choosing between.
///
/// Scanned on demand around a world origin (a key press), TTL-cached so a burst of presses costs one pass; never
/// per-frame work. The window deliberately reaches well past one turn's movement (<see cref="RadiusCells"/>) —
/// the whole point of the feature is planning a position you cannot reach yet.
///
/// Two gates, both borrowed rather than invented:
/// <list type="bullet">
/// <item><b>Visual parity.</b> Never-seen ground is skipped (<see cref="FogExplored"/> — the one-blit full-grid
/// snapshot, NOT <see cref="FogProbe"/>, whose per-call GPU readback would stall for hundreds of cells).</item>
/// <item><b>Overlay parity.</b> Callers gate the whole feature on
/// <see cref="InteractableDescriber.CoverOverlayActive"/>, the same predicate that decides whether the tile
/// readout names a tile's cover — so the two surfaces can never disagree about whether cover is knowable.</item>
/// </list>
/// Surfaced as scanner items by <see cref="ProxyCover"/> (the J cycle and the "Cover" browse category).
/// </summary>
internal static class CoverModel
{
    // Cardinal grid-direction indices, in the order they are spoken. The game's numbering (shared with
    // WallTones, CoverVisualizer and InteractableDescriber's per-edge readout): S=0, E=1, N=2, W=3.
    private static readonly (int Dir, string Key)[] Sides =
    {
        (2, "aim.dir_n"), (1, "aim.dir_e"), (0, "aim.dir_s"), (3, "aim.dir_w"),
    };

    // ~16 m on the 1.35 m grid. Deliberately beyond one turn's move: "including out-of-range tiles so positioning
    // can be planned" is the requested behaviour (docs/feedback/2026-07-discord-triage.md, request 2).
    private const int RadiusCells = 12;

    // Surface-height slack, in metres. Looser than Geo.LevelThreshold (1.5 m — a kerb/step test) because a floor
    // can genuinely slope over a 16 m window, but tight enough that a catwalk a storey up is not offered as cover
    // "beside" you. Anything that survives this yet sits on a disconnected island still reads "other level" via
    // ScanItem.Reach.
    private const float LevelTolerance = 3f;

    private const float CacheSec = 1.5f;   // one pass per burst of presses
    private const int MaxSpots = 48;       // a browse cap; a cell-dense room would otherwise list a hundred stops

    /// <summary>One cover position: a cluster of touching cells that all give the SAME cover on the same sides,
    /// plus the member cell nearest the scan origin (the tile you would actually stand on).</summary>
    internal sealed class Spot
    {
        /// <summary>Canonical identity — the lowest node index in the cluster. Together with <see cref="Sig"/>
        /// this re-finds the same spot after a recompute, so the review selection survives repeated presses even
        /// when the cached objects are rebuilt (see <see cref="Resolve"/>).</summary>
        public int Id;

        /// <summary>Packed per-direction cover: two bits per grid direction, holding the
        /// <see cref="LosCalculations.CoverType"/> of that edge (0 = none). Non-zero by construction.</summary>
        public int Sig;

        /// <summary>How many cells share this exact signature — how far you can shuffle along the same cover.</summary>
        public int Cells;

        /// <summary>The cluster cell nearest the scan origin: the COVER SIDE the cursor plants on.</summary>
        public Vector3 Position;
    }

    private static readonly List<Spot> _spots = new List<Spot>();
    private static readonly List<Spot> _prev = new List<Spot>();   // last generation, for identity re-use

    // Scratch for one pass. The window is a fixed 25x25, so these are allocated once rather than per recompute —
    // a cover scan runs mid-combat, where a few kilobytes of garbage per press is avoidable noise.
    private const int Side = RadiusCells * 2 + 1;
    private static readonly int[] _sig = new int[Side * Side];
    private static readonly CustomGridNodeBase[] _cells = new CustomGridNodeBase[Side * Side];
    private static readonly Stack<int> _stack = new Stack<int>();
    private static readonly List<int> _members = new List<int>();

    private static float _nextAt;
    private static int _originNode = int.MinValue;   // the seed cell the cache was built around
    private static int _graphVersion = -1;           // node indices are only meaningful within one graph build

    /// <summary>Drop the cache — area change / feature reset, so no spot survives at stale coordinates and no
    /// graph node from a dead build stays pinned by the scratch.</summary>
    internal static void Invalidate()
    {
        _spots.Clear();
        _prev.Clear();
        Array.Clear(_cells, 0, _cells.Length);
        _originNode = int.MinValue;
        _nextAt = 0f;
    }

    /// <summary>The cover positions around <paramref name="origin"/>, nearest first. Recomputes when the origin
    /// cell changed or the cache went stale; otherwise hands back the SAME objects, which is what keeps a cycling
    /// selection stable. Never throws — a bad graph simply yields nothing.</summary>
    internal static IReadOnlyList<Spot> Near(Vector3 origin)
    {
        try
        {
            int version = GraphParamsMechanicsCache.GraphVersionIndex;
            if (version != _graphVersion) { _graphVersion = version; Invalidate(); }

            var seed = NavmeshProbe.NodeAt(origin);
            if (seed == null) { Invalidate(); return _spots; }
            if (seed.NodeIndex == _originNode && Time.unscaledTime < _nextAt) return _spots;

            _originNode = seed.NodeIndex;
            _nextAt = Time.unscaledTime + CacheSec;
            Recompute(seed, origin);
            return _spots;
        }
        catch (Exception e)
        {
            Main.Log?.Error("CoverModel.Near failed: " + e);
            Invalidate();
            return _spots;
        }
    }

    /// <summary>Re-find a held spot in the current cache — the scanner's selection resolve. Identity first (the
    /// common case, since a burst of presses reuses one pass), then the origin-independent Id + signature, so a
    /// selection made before the cursor moved still resolves as long as the same cluster is still in range.
    /// Null once it is not, which correctly goes stale rather than planting the cursor on a vanished tile.</summary>
    internal static Spot Resolve(Spot spot)
    {
        if (spot == null) return null;
        for (int i = 0; i < _spots.Count; i++)
        {
            var s = _spots[i];
            if (ReferenceEquals(s, spot)) return s;
            if (s.Id == spot.Id && s.Sig == spot.Sig) return s;
        }
        return null;
    }

    /// <summary>The unit the cover is read FOR: the acting unit in turn-based combat, else the selected one. It is
    /// the unit whose own tile must stay listable (you can already be standing in the best cover) and the one the
    /// "if I stood here" tail is answered for (see <see cref="ProxyCover"/>).</summary>
    internal static BaseUnitEntity Observer()
    {
        var game = Game.Instance;
        return game?.TurnController?.CurrentUnit as BaseUnitEntity
               ?? game?.SelectionCharacter?.SelectedUnit?.Value;
    }

    /// <summary>The spoken sides of a spot — "Half cover north, full cover east", in N/E/S/W order, using the very
    /// phrasing the tile readout uses for the same edges so the two surfaces sound identical. The first side leads
    /// with a capitalised form (it is the item's name).</summary>
    internal static string SidesLine(Spot spot)
    {
        if (spot == null) return Loc.T("cover.spot");
        var parts = new List<string>();
        for (int i = 0; i < Sides.Length; i++)
        {
            var cover = CoverOn(spot, Sides[i].Dir);
            if (cover == LosCalculations.CoverType.None) continue;
            parts.Add(Loc.T(SideKey(cover, lead: parts.Count == 0), new { dir = Loc.T(Sides[i].Key) }));
        }
        return parts.Count > 0 ? string.Join(", ", parts) : Loc.T("cover.spot");
    }

    /// <summary>The cover on one edge of a spot, unpacked from its signature.</summary>
    private static LosCalculations.CoverType CoverOn(Spot spot, int dir)
        => (LosCalculations.CoverType)((spot.Sig >> (2 * dir)) & 3);

    private static string SideKey(LosCalculations.CoverType cover, bool lead)
    {
        switch (cover)
        {
            case LosCalculations.CoverType.Full: return lead ? "cover.spot_full" : "cover.full_dir";
            case LosCalculations.CoverType.Invisible: return lead ? "cover.spot_blocked" : "cover.blocked_dir";
            default: return lead ? "cover.spot_half" : "cover.half_dir";
        }
    }

    // ---- the scan ----

    private static void Recompute(CustomGridNodeBase seed, Vector3 origin)
    {
        _prev.Clear();
        _prev.AddRange(_spots);   // identity is what a held selection keys on — see Adopt
        _spots.Clear();
        var graph = seed.Graph as CustomGridGraph;
        if (graph == null) { _prev.Clear(); return; }

        Array.Clear(_sig, 0, _sig.Length);
        Array.Clear(_cells, 0, _cells.Length);

        int cx = seed.XCoordinateInGrid, cz = seed.ZCoordinateInGrid;
        float baseY = seed.Vector3Position.y;
        var observer = Observer();
        var checkType = InteractableDescriber.CoverCheckType;
        // One blit for the whole pass. FogProbe's per-call 1x1 readback stalls the render thread and would be
        // hundreds of stalls here; FogExplored is exactly the full-grid shape (see FrontierModel, same reason).
        bool fogged = FogExplored.Ensure();

        // Pass 1: classify every standable cell in the window by its per-edge cover.
        for (int gz = 0; gz < Side; gz++)
        {
            for (int gx = 0; gx < Side; gx++)
            {
                var node = graph.GetNode(cx + gx - RadiusCells, cz + gz - RadiusCells);
                if (node == null || !node.Walkable) continue;

                var p = node.Vector3Position;
                if (Mathf.Abs(p.y - baseY) > LevelTolerance) continue;      // a catwalk overhead is not this floor
                if (fogged && !FogExplored.IsExplored(p)) continue;         // parity: never-seen ground stays dark
                var occupant = node.GetUnit();
                if (occupant != null && !ReferenceEquals(occupant, observer)) continue;  // somebody is standing there

                int packed = 0;
                for (int i = 0; i < Sides.Length; i++)
                {
                    int dir = Sides[i].Dir;
                    LosCalculations.CoverType cover;
                    try { cover = LosCalculations.GetCellCoverStatus(node, dir, checkType).CoverType; }
                    catch { continue; }
                    if (cover != LosCalculations.CoverType.None) packed |= (int)cover << (2 * dir);
                }
                if (packed == 0) continue;

                int idx = gz * Side + gx;
                _sig[idx] = packed;
                _cells[idx] = node;
            }
        }

        // Pass 2: flood-fill touching cells with an IDENTICAL signature into one spot. Identical is the right
        // join: a wall that turns half-into-full, or a corner cell covered on two sides, is a genuinely different
        // place to stand and deserves its own entry. `sig` doubles as the visited mask (cleared on claim, and 0
        // never matches a live signature).
        _stack.Clear();
        for (int start = 0; start < _sig.Length; start++)
        {
            int mine = _sig[start];
            if (mine == 0) continue;

            _members.Clear();
            _sig[start] = 0;
            _stack.Push(start);
            while (_stack.Count > 0)
            {
                int i = _stack.Pop();
                _members.Add(i);
                int iz = i / Side, ix = i % Side;
                Claim(ix + 1, iz, mine);
                Claim(ix - 1, iz, mine);
                Claim(ix, iz + 1, mine);
                Claim(ix, iz - 1, mine);
            }

            int id = int.MaxValue;
            float bestSqr = float.MaxValue;
            Vector3 nearest = Vector3.zero;
            for (int m = 0; m < _members.Count; m++)
            {
                var node = _cells[_members[m]];
                if (node == null) continue;
                if (node.NodeIndex < id) id = node.NodeIndex;
                var p = node.Vector3Position;
                float dx = p.x - origin.x, dz = p.z - origin.z;
                float d = dx * dx + dz * dz;
                if (d < bestSqr) { bestSqr = d; nearest = p; }
            }
            if (id == int.MaxValue) continue;   // every member vanished from under us

            var spot = Adopt(id, mine);
            spot.Cells = _members.Count;
            spot.Position = nearest;
            _spots.Add(spot);
        }
        _prev.Clear();

        _spots.Sort((a, b) => SqrTo(a.Position, origin).CompareTo(SqrTo(b.Position, origin)));
        if (_spots.Count > MaxSpots) _spots.RemoveRange(MaxSpots, _spots.Count - MaxSpots);
    }

    /// <summary>Take over the previous generation's object for the same cluster (same canonical id + signature)
    /// rather than minting a new one, so a held review selection survives a plain cache refresh — otherwise a
    /// pause longer than the TTL would silently reset the browse to the first entry. Fields are updated in place
    /// by the caller; an unclaimed old spot simply drops out.</summary>
    private static Spot Adopt(int id, int sig)
    {
        for (int i = 0; i < _prev.Count; i++)
        {
            var old = _prev[i];
            if (old.Id != id || old.Sig != sig) continue;
            _prev.RemoveAt(i);
            return old;
        }
        return new Spot { Id = id, Sig = sig };
    }

    // Claim a neighbouring cell into the current cluster when its signature matches exactly.
    private static void Claim(int x, int z, int mine)
    {
        if (x < 0 || z < 0 || x >= Side || z >= Side) return;
        int i = z * Side + x;
        if (_sig[i] != mine) return;
        _sig[i] = 0;
        _stack.Push(i);
    }

    private static float SqrTo(Vector3 p, Vector3 origin)
    {
        float dx = p.x - origin.x, dz = p.z - origin.z;
        return dx * dx + dz * dz;
    }
}
