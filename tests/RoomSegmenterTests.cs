using System;
using System.Collections.Generic;
using System.Linq;
using RTAccess.Exploration;
using Xunit;

namespace RTAccess.Tests
{
    /// <summary>
    /// Pins the room segmentation core against the two failure modes it shipped with:
    /// (1) a persistence threshold copied from WrathAccess in METRES, tuned on a 0.25 m raster, which on RT's
    ///     1.35 m grid is smaller than a single chamfer step — so any crate on a room's centre line split the room
    ///     in two, and the invented seam was then announced as a doorway; and
    /// (2) segmenting on raw grid adjacency instead of the engine's own per-edge connectivity, so rooms joined
    ///     across walls and thresholds were emitted where nobody can walk.
    /// The grid helper below mirrors how the engine derives its connection bits (both ends walkable, within the
    /// climb limit, not cut, and a diagonal only when BOTH flanking cardinals are open), so a test grid behaves
    /// the way <see cref="RoomMap"/> will feed the real one.
    /// </summary>
    public class RoomSegmenterTests
    {
        private const float Cell = 1.35f;      // RT's native grid, the raster every constant has to survive
        private const float MaxClimb = 1.35f;  // CustomGridGraph.maxClimb default

        // --- 1. an open room is ONE room, whatever furniture stands in it ---

        [Fact]
        public void EmptyHall_IsOneRoom()
        {
            var g = Grid.Box(16, 12);
            var r = g.Segment();
            Assert.Equal(1, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        [Theory]
        [InlineData(1)]  // a single crate — the case that shipped broken
        [InlineData(2)]
        [InlineData(3)]
        public void HallWithCentredObstruction_StaysOneRoom(int size)
        {
            // ODD depth on purpose: the clearance ridge of an even-depth hall is two rows wide, so a small crate
            // only blocks one of them and the other still carries the basins together. An odd-depth hall has a
            // single ridge row, which is where a crate really does sever it — the shipped failure.
            var g = Grid.Box(16, 11);
            g.Fill(8 - size / 2, 6 - size / 2, size, size, wall: true);
            var r = g.Segment();
            Assert.Equal(1, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        [Theory]
        [InlineData(4)]   // past FurnitureMaxCells: no shadow exemption, no bridging — persistence alone carries it
        [InlineData(6)]
        public void EnclosedIslandNeverSplitsTheHall(int size)
        {
            // Two independent mechanisms have to hold this: small islands bridge the basins across themselves,
            // and larger ones are held together by persistence because walking around them barely dips the
            // clearance. Whatever its size, an island fully enclosed by floor is something you walk around — it
            // can never be a room boundary, and must never produce a threshold.
            var g = Grid.Box(16, 11);
            g.Fill(8 - size / 2, 6 - size / 2, size, size, wall: true);
            var r = g.Segment();
            Assert.Equal(1, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        [Fact]
        public void BlockAlmostFillingAHall_LeavesAlcovesJoinedByRealGaps()
        {
            // A 9x9 block in a 16x11 hall leaves generous pockets left and right joined by one-cell slots above
            // and below it. Those slots ARE doorway-shaped, so reading the pockets as separate rooms is right —
            // what matters is that every threshold announced sits on a gap you can actually walk through.
            var g = Grid.Box(16, 11);
            g.Fill(4, 2, 9, 9, wall: true);
            var r = g.Segment();
            Assert.Equal(2, Regions(r));
            AssertEveryBoundaryIsReal(g, r);
        }

        [Fact]
        public void CorridorWithCrate_StaysOneRoom()
        {
            var g = Grid.Box(24, 3);
            g.Fill(12, 1, 1, 1, wall: true);
            var r = g.Segment();
            Assert.Equal(1, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        [Fact]
        public void HallWithWallStub_StaysOneRoom()
        {
            // A stub reaching in from the hull is structure, not furniture (no clearance-shadow exemption), but it
            // still must not carve the hall in half — persistence, not the furniture mask, is what carries this.
            var g = Grid.Box(16, 11);
            g.Fill(8, 1, 1, 4, wall: true);
            var r = g.Segment();
            Assert.Equal(1, Regions(r));
        }

        // --- 2. genuinely separate rooms still separate ---

        [Theory]
        [InlineData(3)]   // a 4 m closet still opens off the corridor as its own room
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(8)]
        [InlineData(10)]
        public void TwoRoomsThroughOneCellDoorway_AreTwoRooms(int size)
        {
            var g = Grid.TwinRooms(size, doorways: 1);
            var r = g.Segment();
            Assert.Equal(2, Regions(r));
            Assert.Single(PairsOf(r));
            Assert.NotEmpty(r.Boundaries);
        }

        [Fact]
        public void DoubleDoorBetweenTwoSmallCabins_IsStillADoorway()
        {
            // Two 5.4 m cabins joined by a 2.7 m double door. Numerically this is the same "two-cell opening" as a
            // crate pinching a three-cell corridor, and any rule that reads only the opening's own size merges one
            // or splits the other. What tells them apart is the wall: here the opening is cut through two cells of
            // dividing wall, there it is most of the passage.
            var g = Grid.TwinRooms(4, doorways: 2);
            Assert.Equal(2, Regions(g.Segment()));
        }

        [Fact]
        public void AlcoveNoWiderThanItsOwnDoorway_ReadsAsPartOfWhatItOpensOnto()
        {
            // KNOWN LIMIT, pinned so it is a decision rather than a surprise. A 2-cell-wide (2.7 m) store room has
            // one cell of clearance everywhere — exactly what its doorway has — so it never rises above the saddle
            // and no persistence threshold can give it a basin of its own (peak - saddle is 0, and 0 < persist for
            // any positive persist). It is reported as part of the hall, which for an alcove that shallow is a
            // reasonable reading. Separating one would need a neck-splitting pass, not a different constant.
            var g = new Grid(18, 12);
            g.Fill(1, 1, 12, 10, wall: false);   // the hall
            g.Fill(14, 3, 2, 6, wall: false);    // 2x6 store room, 21.9 m²
            g.Fill(13, 5, 1, 1, wall: false);    // one cell of door through the dividing wall
            var r = g.Segment();
            Assert.Equal(1, Regions(r));
            AssertEveryBoundaryIsReal(g, r);
        }

        [Fact]
        public void TwoDoorwaysBetweenTheSameRooms_BothSurvive()
        {
            var g = Grid.TwinRooms(8, doorways: 2);
            var r = g.Segment();
            Assert.Equal(2, Regions(r));
            Assert.Single(PairsOf(r));
            // Two thresholds, far enough apart that the geometric clustering in RoomMap keeps them distinct.
            var xs = r.Boundaries.Select(e => e.CellI % g.W).Distinct().ToList();
            Assert.Equal(2, Clusters(r.Boundaries.Select(e => e.CellI / g.W).Distinct().OrderBy(v => v).ToList()));
            Assert.Single(xs);
        }

        // --- 3. THE regression: passability comes from the engine, never from grid adjacency ---

        [Fact]
        public void SeveredDoorway_ProducesNoExit()
        {
            // Same geometry as the two-room case, but the engine says the far edge cannot be crossed (a railing,
            // a cut connection, a closed door). The doorway cell still belongs to the room it opens off, and NO
            // threshold may be announced between the two rooms — this is the phantom-exit regression.
            var g = Grid.TwinRooms(8, doorways: 1);
            int door = g.Doorways[0];
            g.Cut(door, door + 1);
            var r = g.Segment();
            Assert.Equal(2, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        [Fact]
        public void OneSidedConnection_IsNotCrossable()
        {
            // The engine writes each node's bits independently; the segmenter symmetrises. Clearing only the far
            // side must still sever the edge, or the watershed's answer would depend on visit order.
            var g = Grid.TwinRooms(8, doorways: 1);
            int door = g.Doorways[0];
            g.CutOneWay(door + 1, door);
            var r = g.Segment();
            Assert.Equal(2, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        [Fact]
        public void FullyIsolatedDoorwayCell_IsItsOwnRoomAndJoinsNothing()
        {
            // A closed door severed from BOTH sides: the two rooms stay apart, the door cell survives as its own
            // (tiny, unmergeable) region rather than being deleted, and nothing is announced as a way through.
            var g = Grid.TwinRooms(8, doorways: 1);
            int door = g.Doorways[0];
            g.Cut(door, door + 1);
            g.Cut(door, door - 1);
            var r = g.Segment();
            Assert.Equal(3, Regions(r));
            Assert.Empty(r.Boundaries);
            Assert.True(r.Label[door] >= 0);
        }

        [Fact]
        public void PropStandingAgainstARailing_DoesNotBridgeAcrossIt()
        {
            // Furniture islands may carry a basin across themselves, which is how a crate stops splitting the hall
            // it stands in. But "floor all the way round" is NOT proof you can walk round: a prop pressed against a
            // railing has floor on both sides of a barrier the party cannot cross. Bridging there would re-create
            // the very defect this rewrite removed, from the other direction — two rooms silently reported as one.
            var g = Grid.Box(14, 9);
            for (int z = 0; z < g.D; z++) g.Cut(g.Index(8, z), g.Index(9, z));
            Assert.Equal(2, Regions(g.Segment()));      // the railing alone
            g.Fill(8, 5, 1, 1, wall: true);             // now stand a crate against it
            var r = g.Segment();
            Assert.Equal(2, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        [Fact]
        public void PropStraddlingAnOverClimbStep_DoesNotBridgeAcrossIt()
        {
            // Same rule, height instead of a railing: the engine refuses the connection, so the island's ring spans
            // two components and it must not bridge.
            var g = Grid.Box(14, 9);
            g.RaiseHalf(2.0f);
            g.Fill(8, 5, 1, 1, wall: true);
            var r = g.Segment();
            Assert.Equal(2, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        [Fact]
        public void SpacesTouchingOnlyAtACorner_DoNotJoin()
        {
            // Two 4x4 rooms meeting corner-to-corner. The engine refuses the diagonal because neither flanking
            // cardinal is open, so this is two rooms with no way between them.
            var g = new Grid(11, 11);
            g.Fill(1, 1, 4, 4, wall: false);
            g.Fill(5, 5, 4, 4, wall: false);
            var r = g.Segment();
            Assert.Equal(2, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        [Fact]
        public void WalkableCellsWithNoConnectionsAtAll_AreTheirOwnRoom()
        {
            // An island the pathfinder cannot reach must not be silently glued to whatever it touches on the grid.
            var g = Grid.Box(16, 12);
            foreach (var (a, b) in g.EdgesAround(3, 3, 3, 3)) g.Cut(a, b);
            var r = g.Segment();
            Assert.Equal(2, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        // --- 4. height is the engine's business, not a hand-picked gate ---

        [Fact]
        public void WalkableStepWithinClimbLimit_DoesNotSplitTheRoom()
        {
            // A 1.0 m dais lip: above the old hardcoded 0.6 m DyGate, below the graph's 1.35 m maxClimb. The
            // engine connects it, so it is part of the room — and its edge is not an "exit".
            var g = Grid.Box(16, 12);
            g.RaiseHalf(1.0f);
            var r = g.Segment();
            Assert.Equal(1, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        [Fact]
        public void StepBeyondClimbLimit_SplitsAndIsNotAnExit()
        {
            // A 2.0 m drop: the engine refuses the connection, so it is two rooms with no threshold between them.
            var g = Grid.Box(16, 12);
            g.RaiseHalf(2.0f);
            var r = g.Segment();
            Assert.Equal(2, Regions(r));
            Assert.Empty(r.Boundaries);
        }

        // --- 5. invariants the pipeline must keep ---

        [Fact]
        public void EveryBoundaryEdgeIsCrossableAndJoinsTwoDistinctRooms()
        {
            var g = Grid.TwinRooms(8, doorways: 2);
            var r = g.Segment();
            Assert.NotEmpty(r.Boundaries);
            AssertEveryBoundaryIsReal(g, r);
        }

        // The invariant the whole rewrite exists to guarantee: a threshold is only ever announced where a unit can
        // actually step, between two rooms that really are different.
        private static void AssertEveryBoundaryIsReal(Grid g, RoomSegmenter.Result r)
        {
            foreach (var e in r.Boundaries)
            {
                Assert.NotEqual(e.A, e.B);
                Assert.Equal(e.A, r.Label[e.CellI]);
                Assert.Equal(e.B, r.Label[e.CellJ]);
                int k = e.CellJ == e.CellI + 1 ? RoomSegmenter.DirEast : RoomSegmenter.DirNorth;
                Assert.True((r.Conn[e.CellI] & (1 << k)) != 0, "a threshold was emitted across an uncrossable edge");
            }
        }

        [Fact]
        public void UniformClearancePlateau_DoesNotFragment()
        {
            // Every cell of a 2-wide corridor has identical clearance, so the watershed order is one huge tie.
            // With an index tie-break the outcome is one deterministic seam; with an unstable sort it was
            // whichever permutation introsort happened to produce.
            var g = Grid.TwinRoomsViaCorridor(8, corridorLength: 8, corridorWidth: 2);
            var first = g.Segment();
            var again = g.Segment();
            Assert.Equal(2, Regions(first));
            Assert.Single(PairsOf(first));
            Assert.Equal(first.Label, again.Label);
        }

        [Fact]
        public void SeedFloorIsInertOnTheNativeRaster()
        {
            // Documents a deliberate deviation from WrathAccess: the smallest clearance any walkable cell can have
            // is exactly one cell, so the seed floor never excludes one and the flood assigns nothing. Raising it
            // far enough to bite would dissolve real 4 m cabins into the corridor they open onto.
            Assert.True(RoomSegmenter.CutFloorCells <= 1f);
            var g = Grid.TwinRoomsViaCorridor(8, corridorLength: 6, corridorWidth: 1);
            Assert.Equal(0, g.Segment().FloodedCells);
        }

        [Fact]
        public void DirectionTablesAgreeWithEachOther()
        {
            // RoomMap maps this table onto the engine's own direction ids, and the boundary scan indexes into it
            // by name. A silently mis-ordered entry would gate the wrong edge — cheap to pin, expensive to find.
            for (int k = 0; k < 8; k++)
            {
                int o = RoomSegmenter.Opposite[k];
                Assert.Equal(-RoomSegmenter.Dz[k], RoomSegmenter.Dz[o]);
                Assert.Equal(-RoomSegmenter.Dx[k], RoomSegmenter.Dx[o]);
            }
            Assert.Equal((1, 0), (RoomSegmenter.Dz[RoomSegmenter.DirNorth], RoomSegmenter.Dx[RoomSegmenter.DirNorth]));
            Assert.Equal((0, 1), (RoomSegmenter.Dz[RoomSegmenter.DirEast], RoomSegmenter.Dx[RoomSegmenter.DirEast]));
        }

        [Fact]
        public void EngineDirectionMappingMatchesTheEnginesOwnOffsets()
        {
            // RoomMap reads the pathfinder's per-edge connection bit as HasConnectionInDirection(EngineDir[k]).
            // The engine numbers its directions differently from us (S=0 E=1 N=2 W=3 SE=4 NE=5 NW=6 SW=7), and a
            // single wrong entry would gate the wrong edge on every cell of every map while still compiling and
            // still producing plausible-looking rooms. Checked against the offsets transcribed from
            // CustomGridGraph's SetUpOffsetsAndCosts.
            for (int k = 0; k < 8; k++)
            {
                int e = RoomSegmenter.EngineDir[k];
                Assert.Equal(RoomSegmenter.Dx[k], RoomSegmenter.EngineDx[e]);
                Assert.Equal(RoomSegmenter.Dz[k], RoomSegmenter.EngineDz[e]);
            }
            Assert.Equal(8, new HashSet<int>(RoomSegmenter.EngineDir).Count);
        }

        [Theory]
        [InlineData(1, 2)]   // a door
        [InlineData(2, 2)]   // a double door
        [InlineData(3, 2)]   // a modest archway - still a way THROUGH a wall
        [InlineData(4, 1)]   // past MaxOpeningCells: the wall has stopped being a wall
        [InlineData(5, 1)]   // wider than a doorway: the two halves are one space
        [InlineData(8, 1)]   // most of the dividing wall is gone
        public void OnlyANarrowOpeningIsARoomBoundary(int openingCells, int expectedRooms)
        {
            // The criterion that replaced tuning the persistence constant. On a 1.35 m raster the clearance
            // signal cannot separate "crate pinch" from "doorway" - a doorway gap is exactly one cell deep - so
            // the watershed deliberately over-proposes and narrowness decides. Both halves are 8 cells across, so
            // the relative test is not what is biting here; this pins the absolute one.
            var g = Grid.TwinRooms(8, doorways: 0);
            for (int i = 0; i < openingCells; i++) g.OpenWall(1 + i);
            var r = g.Segment();
            Assert.Equal(expectedRooms, Regions(r));

            // The doorway here sits in the CORNER, which is what the level-by-level flood exists for: an index
            // sweep let the left room reach through it and swallow the right room's whole edge column, after
            // which the "boundary" between them ran down the middle of the right room. Pin it directly — the two
            // rooms are the same size, so neither may have eaten any of the other.
            if (expectedRooms != 2) return;
            var counts = new Dictionary<int, int>();
            foreach (var l in r.Label)
            {
                if (l < 0) continue;
                counts.TryGetValue(l, out int c);
                counts[l] = c + 1;
            }
            Assert.Equal(2, counts.Count);
            var sizes = new List<int>(counts.Values);
            Assert.True(Math.Abs(sizes[0] - sizes[1]) <= openingCells,
                $"rooms should be near-symmetric, got {sizes[0]} vs {sizes[1]}");
        }

        [Fact]
        public void ConstrictionSpanningMostOfAPassage_IsNotADoorway()
        {
            // Two cells wide is a double door between two rooms, but in a three-cell corridor it is just a crate
            // against the wall. Absolute width cannot tell those apart - proportion can, which is the whole job
            // of MaxOpeningFraction.
            var g = Grid.Box(24, 3);
            g.Fill(12, 1, 1, 1, wall: true);   // against the corridor wall, so it is structure, not furniture
            Assert.Equal(1, Regions(g.Segment()));
        }

        [Fact]
        public void EveryWalkableCellBelongsToARoom()
        {
            // A cell with no room makes "where am I" and the exit cycle silently fail there, so undersized
            // regions merge — and an isolated one is kept rather than deleted.
            var g = Grid.Box(16, 12);
            g.Fill(3, 3, 2, 2, wall: true);
            var r = g.Segment();
            for (int i = 0; i < g.Walk.Length; i++)
                if (g.Walk[i]) Assert.True(r.Label[i] >= 0, $"cell {i % g.W},{i / g.W} belongs to no room");
        }

        [Fact]
        public void IsolatedTinyRegion_IsKeptNotDeleted()
        {
            // A 2x2 closet with no connection anywhere is far below MinRoomArea and has nothing to merge into.
            var g = new Grid(14, 12);
            g.Fill(1, 1, 10, 10, wall: false);
            foreach (var (a, b) in g.EdgesAround(1, 1, 2, 2)) g.Cut(a, b);
            var r = g.Segment();
            for (int z = 1; z < 3; z++)
                for (int x = 1; x < 3; x++)
                    Assert.True(r.Label[z * g.W + x] >= 0, "an isolated region was dropped instead of kept");
        }

        [Fact]
        public void EmptyGrid_IsHandled()
        {
            var g = new Grid(8, 8);   // all wall
            var r = g.Segment();
            Assert.Equal(0, Regions(r));
            Assert.Empty(r.Boundaries);
            Assert.All(r.Label, l => Assert.Equal(-1, l));
        }

        // ---- helpers ----

        private static int Regions(RoomSegmenter.Result r)
            => r.Label.Where(l => l >= 0).Distinct().Count();

        private static HashSet<(int, int)> PairsOf(RoomSegmenter.Result r)
            => r.Boundaries.Select(e => (Math.Min(e.A, e.B), Math.Max(e.A, e.B))).ToHashSet();

        // How many runs of consecutive values a sorted list falls into — two doorways in the same wall are two
        // runs of rows, one wide doorway is one.
        private static int Clusters(List<int> sorted)
        {
            if (sorted.Count == 0) return 0;
            int n = 1;
            for (int i = 1; i < sorted.Count; i++) if (sorted[i] != sorted[i - 1] + 1) n++;
            return n;
        }

        /// <summary>A test grid that derives its connection bits the way CustomGridGraph.CalculateConnections
        /// does, so what the segmenter sees matches what the engine would hand it.</summary>
        private sealed class Grid
        {
            public readonly int W, D;
            public readonly bool[] Walk;
            public readonly float[] Y;
            private readonly HashSet<(int, int)> _cuts = new HashSet<(int, int)>();

            public Grid(int w, int d)
            {
                W = w; D = d;
                Walk = new bool[w * d];
                Y = new float[w * d];
            }

            /// <summary>A rectangular floor of the given interior size, ringed by wall.</summary>
            public static Grid Box(int interiorW, int interiorD)
            {
                var g = new Grid(interiorW + 2, interiorD + 2);
                g.Fill(1, 1, interiorW, interiorD, wall: false);
                return g;
            }

            /// <summary>Two square rooms side by side, sharing a one-cell-thick wall with N doorways in it.
            /// <see cref="Doorways"/> holds the cell index of each gap.</summary>
            public static Grid TwinRooms(int size, int doorways)
            {
                var g = new Grid(size * 2 + 3, size + 2);
                g.Fill(1, 1, size, size, wall: false);
                g.Fill(size + 2, 1, size, size, wall: false);
                int mid = size + 1;
                if (doorways == 1) g.AddDoorway(mid, size / 2 + 1);
                else for (int i = 1; i <= doorways; i++) g.AddDoorway(mid, i * size / (doorways + 1) + 1);
                return g;
            }

            /// <summary>Cell indices of the gaps punched through a dividing wall.</summary>
            public readonly List<int> Doorways = new List<int>();

            /// <summary>Punch one more cell out of a TwinRooms dividing wall, at the given row.</summary>
            public void OpenWall(int z) => AddDoorway((W - 1) / 2, z);

            private void AddDoorway(int x, int z)
            {
                int i = z * W + x;
                Walk[i] = true;
                Doorways.Add(i);
            }

            /// <summary>Two square rooms joined by a corridor — the uniform-clearance plateau case.</summary>
            public static Grid TwinRoomsViaCorridor(int size, int corridorLength, int corridorWidth)
            {
                var g = new Grid(size * 2 + corridorLength + 2, size + 2);
                g.Fill(1, 1, size, size, wall: false);
                g.Fill(size + 1 + corridorLength, 1, size, size, wall: false);
                g.Fill(size + 1, size / 2 - corridorWidth / 2 + 1, corridorLength, corridorWidth, wall: false);
                return g;
            }

            public void Fill(int x, int z, int w, int d, bool wall)
            {
                for (int gz = z; gz < z + d; gz++)
                    for (int gx = x; gx < x + w; gx++)
                        if (gx >= 0 && gz >= 0 && gx < W && gz < D) Walk[gz * W + gx] = !wall;
            }

            /// <summary>Lift every cell in the right-hand half by <paramref name="dy"/> metres.</summary>
            public void RaiseHalf(float dy)
            {
                for (int gz = 0; gz < D; gz++)
                    for (int gx = W / 2; gx < W; gx++)
                        Y[gz * W + gx] = dy;
            }

            /// <summary>Sever an edge in both directions (a wall the pathfinder honours but the grid still calls
            /// walkable — a closed door, a railing, a cut connection).</summary>
            public void Cut(int a, int b) { _cuts.Add((a, b)); _cuts.Add((b, a)); }

            /// <summary>Sever only one direction, to prove the segmenter symmetrises.</summary>
            public void CutOneWay(int a, int b) => _cuts.Add((a, b));

            /// <summary>Every cardinal edge crossing the given grid column.</summary>
            public IEnumerable<(int, int)> CardinalEdgesAt(int x)
            {
                for (int gz = 0; gz < D; gz++)
                {
                    int i = gz * W + x;
                    if (x > 0) yield return (i, i - 1);
                    if (x < W - 1) yield return (i, i + 1);
                }
            }

            /// <summary>Every edge leaving the given rectangle — used to isolate a patch from its surroundings.</summary>
            public IEnumerable<(int, int)> EdgesAround(int x, int z, int w, int d)
            {
                for (int gz = z; gz < z + d; gz++)
                    for (int gx = x; gx < x + w; gx++)
                    {
                        int i = gz * W + gx;
                        for (int k = 0; k < 8; k++)
                        {
                            int nx = gx + RoomSegmenter.Dx[k], nz = gz + RoomSegmenter.Dz[k];
                            if (nx < 0 || nz < 0 || nx >= W || nz >= D) continue;
                            if (nx >= x && nx < x + w && nz >= z && nz < z + d) continue;
                            yield return (i, nz * W + nx);
                        }
                    }
            }

            public int Index(int x, int z) => z * W + x;

            // Mirrors CustomGridGraph.CalculateConnections: cardinals need both ends walkable, uncut and within
            // maxClimb; a diagonal additionally needs BOTH flanking cardinals open (the no-corner-cutting rule).
            private byte[] Connections()
            {
                var conn = new byte[W * D];
                for (int gz = 0; gz < D; gz++)
                    for (int gx = 0; gx < W; gx++)
                    {
                        int i = gz * W + gx;
                        if (!Walk[i]) continue;
                        int bits = 0;
                        for (int k = 0; k < 4; k++)
                            if (Valid(i, gx, gz, k)) bits |= 1 << k;
                        // k: 4=SW(S,W) 5=SE(S,E) 6=NW(N,W) 7=NE(N,E) in RoomSegmenter's order.
                        var flanks = new[] { (0, 2), (0, 3), (1, 2), (1, 3) };
                        for (int k = 4; k < 8; k++)
                        {
                            var (f1, f2) = flanks[k - 4];
                            if (((bits >> f1) & 1) == 0 || ((bits >> f2) & 1) == 0) continue;
                            if (Valid(i, gx, gz, k)) bits |= 1 << k;
                        }
                        conn[i] = (byte)bits;
                    }
                return conn;
            }

            private bool Valid(int i, int gx, int gz, int k)
            {
                int nx = gx + RoomSegmenter.Dx[k], nz = gz + RoomSegmenter.Dz[k];
                if (nx < 0 || nz < 0 || nx >= W || nz >= D) return false;
                int j = nz * W + nx;
                if (!Walk[j]) return false;
                if (_cuts.Contains((i, j))) return false;
                return Math.Abs(Y[i] - Y[j]) <= MaxClimb;
            }

            public RoomSegmenter.Result Segment()
                => RoomSegmenter.Segment(Walk, Y, Connections(), W, D, Cell);
        }
    }
}
