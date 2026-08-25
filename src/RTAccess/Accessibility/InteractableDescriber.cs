using Access.Core;          // TextUtil
using System.Linq;
using System.Text;
using Kingmaker;
using Kingmaker.Controllers.Clicks.Handlers; // ClickMapObjectHandler.HasAvailableInteractions (the game's own gate)
using Kingmaker.Blueprints.Area;                 // IAreaEnterPointReference (the game's enter-point mover marker)
using Kingmaker.Controllers.Optimization;
using Kingmaker.Designers.EventConditionActionSystem.Actions; // Conditional (nested action branches)
using Kingmaker.ElementsSystem;                  // ActionList
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Utils;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.Common;                       // UIUtility.GetOvertipSkillCheckText / GetTrapSkillCheckText
using Kingmaker.Mechanics.Entities;
using Kingmaker.Pathfinding;
using Kingmaker.View;
using Kingmaker.View.Covers;
using Kingmaker.View.MapObjects;
using Kingmaker.View.MapObjects.InteractionComponentBase;
using Kingmaker.View.MapObjects.Traps;           // TrapObjectView (trigger-zone footprint, audit #9)
using Kingmaker.View.Mechanics.Entities;         // DestructibleEntityView (standard-vs-custom blueprint, dev names)
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Builds the spoken description of a focused world interactable — e.g. "Door, approach, 4 tiles, ahead" —
/// for the exploration navigator (<see cref="ExplorationEvents"/>) and the area scanner.
///
/// There is no single display-name property on a map object and no localized verb strings, so this replicates
/// the small name mapping the game itself uses in <c>OvertipMapObjectVM.UpdateObjectData()</c>
/// (Door/Loot/Stairs/Action/Trap from the <see cref="InteractionPart"/> subtype + localized UI tooltips), maps
/// <see cref="UIInteractionType"/> to an English verb, and appends planar distance + a camera-relative
/// 8-way bearing computed from <c>Entity.Position</c> versus the active character.
/// </summary>
internal static class InteractableDescriber
{
    // 8-way MAP-relative compass (world axes: +Z = north, +X = east), as LOCALIZATION KEYS resolved at speak
    // time (never a frozen resolved-string array — the game language can switch at runtime). Reuses the aim
    // readout's direction words (identical vocabulary) so the map compass and the aim compass share one source.
    // internal: the system-map screen speaks the same compass for ship-relative bearings.
    internal static readonly string[] Compass8 =
        { "aim.dir_n", "aim.dir_ne", "aim.dir_e", "aim.dir_se", "aim.dir_s", "aim.dir_sw", "aim.dir_w", "aim.dir_nw" };

    /// <summary>Full spoken line for a chosen interactable view. Never throws; returns "" if nothing readable.</summary>
    public static string Describe(EntityViewBase entity)
    {
        if (entity == null) return string.Empty;

        var sb = new StringBuilder();
        var name = ResolveName(entity, out var interaction);
        if (!string.IsNullOrWhiteSpace(name)) sb.Append(name);

        var verb = Verb(interaction);
        if (verb != null) Append(sb, verb);

        // The skill-check card line a sighted hover shows (short description + "[Skill: NN%]" chance).
        var check = CheckInfo(interaction);
        if (check != null) Append(sb, check);

        // Distance + map-relative compass from the active character (skipped if unavailable).
        var self = Game.Instance?.SelectionCharacter?.SelectedUnit?.Value;
        if (self != null && entity.Data != null)
            Append(sb, DirectionAndDistance(self.Position, entity.Data.Position));

        return sb.ToString();
    }

    /// <summary>Spoken line for a local-map landmark from the player position, e.g. "Cargo hold, exit, 15 tiles, north".</summary>
    public static string DescribeMarker(ILocalMapMarker marker, Vector3 self)
    {
        if (marker == null) return string.Empty;
        var sb = new StringBuilder();
        var desc = Clean(marker.GetDescription());
        if (!string.IsNullOrWhiteSpace(desc)) sb.Append(desc);
        var type = MarkerTypeLabel(marker.GetMarkerType());
        if (type != null) Append(sb, type);
        Append(sb, DirectionAndDistance(self, marker.GetPosition()));
        return sb.ToString();
    }

    /// <summary>
    /// Full spoken line for a single grid tile relative to <paramref name="anchor"/>, for the tile explorer
    /// (<see cref="TileExplorer"/>): occupant, then "wall" when an empty tile is unwalkable (an empty WALKABLE tile
    /// adds nothing), then cover on each cardinal edge, then the tile offset from the anchor. Never throws; returns
    /// "" only when <paramref name="node"/> is null.
    /// </summary>
    public static string DescribeTile(CustomGridNodeBase node, MechanicEntity anchor)
    {
        if (node == null) return string.Empty;
        var sb = new StringBuilder();

        // 1. Headline — what is on/near the tile. A unit on the tile is announced first; the interactable NEAR this
        //    tile is then ALWAYS announced too (even behind a unit), because the cursor's Enter acts on it and it can
        //    share the tile with a unit — interactables are off-grid, so this is a nearest-within-reach hint, not a
        //    per-tile lookup. With neither a unit nor an interactable, only an unwalkable tile fills in ("wall") —
        //    an empty walkable tile says nothing at all, so the line is just its offset.
        // Visual parity: gate the layout/occupant readout by the tile's fog state so a blind player hears only what a
        // sighted player could perceive on the local map. A never-seen tile reveals nothing but "unexplored"; an
        // explored-but-not-currently-visible tile reveals its static layout (walls / doors / containers) but NOT a
        // live creature now standing in the fog; a currently-visible (or fog-off / off-map) tile reads in full. The
        // cursor's own POSITION is never fog-hidden (the player drives it), so the offset (section 3) is always spoken.
        var fog = RTAccess.Exploration.FogProbe.Classify((Vector3)node.position);
        bool seen = fog != RTAccess.Exploration.FogProbe.FogState.NeverSeen;
        bool hideUnits = fog == RTAccess.Exploration.FogProbe.FogState.Explored;   // explored-not-visible: static layout only
        if (!seen) sb.Append(Loc.T("where.unexplored"));

        var unit = seen && !hideUnits ? node.GetUnit() : null;
        // Parity gate (main-HUD audit L2): the fog probe classifies the GROUND, not the occupant — a
        // stealth-unspotted ambusher, an IsInvisible unit, or a scripted Features.Hidden NPC can stand on a
        // lit tile with its view hidden (EntityVisibilityForPlayerController), invisible to a sighted player.
        // Require the unit's OWN visibility with the same lens the scanner uses (ProxyUnit.IsVisible); this
        // also covers NoFow maps, where every tile reads "seen" but hidden units stay hidden. A lootable
        // corpse remains IsVisibleForPlayer, so the corpse readout below is unaffected.
        if (unit != null && !(unit.IsPlayerFaction || unit.IsVisibleForPlayer)) unit = null;
        if (unit != null)
        {
            sb.Append(UnitNames.Of(unit));
            // Same four-way classifier the scanner uses, so the tile cursor and the review cycles call a unit
            // the same thing — this used to say "ally" for every non-enemy, including plain neutral NPCs and
            // the capital-area companion NPCs the player cannot command.
            Append(sb, RTAccess.Exploration.UnitFaction.Word(unit));
            // The tile cursor sits ON the tile, so a corpse must be READ (not hidden) — but tagged so it doesn't read
            // as a live enemy. GetUnit() returns corpses (they stay in the grid's awake set until destroyed), and the
            // scanner cycles now skip the dead, so the tile cursor is the one place a corpse is still announced.
            if (unit.LifeState.IsDead) Append(sb, Loc.T("unit.dead"));
            else if (!unit.LifeState.IsConscious) Append(sb, Loc.T("unit.unconscious"));
            // The cursor is the one surface that still reaches an untargetable unit (it is dropped from the
            // enemy target cycle), so it must be the surface that says why the attack will bounce — the tag a
            // sighted player infers from the missing overtip / unclickable cursor. See UnitFaction.Untargetable.
            else if (RTAccess.Exploration.UnitFaction.Untargetable(unit)) Append(sb, Loc.T("unit.untargetable"));
        }
        if (seen && TryNameMapObject(node, out var objectName, out var objectVerb))
        {
            Append(sb, objectName);
            if (objectVerb != null) Append(sb, objectVerb);
        }
        else if (seen && unit == null)
        {
            // A destructible on the tile reads by its own name + "destructible" when it can still be shot
            // (the affordance a sighted player gets from its overtip health bar) — the tile cursor is how a
            // blind player finds the fuel tank / wall the game wants shot. Destroyed or unattackable ones
            // stay the generic obstacle word.
            var destructible = DestructibleEntity.FindByNode(node);
            if (destructible != null)
            {
                string dName = null;
                try { dName = destructible.Name; } catch { /* nameless prop */ }
                if (string.IsNullOrWhiteSpace(dName)) dName = Loc.T("tile.obstacle");
                // Same opt-in designer identity the scanner's proxy speaks, so the tile cursor and the browse list
                // call one barrel by one name (see ProxyDestructible.Name).
                var dDev = DevSuffix(destructible.View, dName);
                sb.Append(string.IsNullOrEmpty(dDev) ? dName : dName + " " + dDev);
                try
                {
                    if (destructible.CanBeAttackedDirectly
                        && destructible.DestructionStages.Stage != Kingmaker.Enums.DestructionStage.Destroyed)
                        Append(sb, Loc.T("object.destructible"));
                }
                catch { /* stage/attackability read is best-effort */ }
            }
            // An empty walkable tile now says NOTHING. "clear" was one word carrying no information on the majority
            // of cursor steps — a tester asked for it to go, and a play session bore it out (it led the tile readout
            // dozens of times, e.g. "clear, 1 north"). Silence is unambiguous here because the offset that always
            // follows proves the step registered, and because the informative case still speaks: an unwalkable tile
            // reads "wall". The locale key is kept, since this is a suppression we may want back behind a setting.
            else if (!node.Walkable) sb.Append(Loc.T("tile.wall"));
        }

        // 1b. Ground hazard / buff zone standing ON this tile — fire, gas, a psychic cloud: the thing a sighted player
        //     sees burning on the floor, and in turn-based combat the real cost of stepping one tile into it. Read it
        //     like a live creature (only on a currently-visible tile, hidden in fog) from the same placed-zone proxies
        //     the area scanner lists, so the wording matches and on-unit auras stay excluded.
        if (seen && !hideUnits) AppendZones(sb, node);

        // 1c. Trap trigger-zone footprint (main-HUD audit #9): a revealed, armed trap renders its whole
        //     trigger-zone mesh to sighted players (the warning decal + the zone's own force-enabled renderer),
        //     so the OUTER tiles of a wide zone are visibly dangerous — not just the authored anchor point the
        //     interactable headline names. Flag any probed tile inside the zone collider, gated exactly like the
        //     decal (view visible && trap active).
        if (seen) AppendTrapZone(sb, node);

        // 2. Combat tactical overlay, mirroring the game's own cover meshes (CoverVisualizer). The mesh shows a
        //    tile's per-edge cover whenever it is the player's turn (or the deployment phase) and the tile is
        //    WALKABLE — crucially NOT only on the reachable set: holding Ctrl reveals cover on every nearby walkable
        //    cell, in or out of movement range. Full mesh predicate (IsNodeCoverVisible):
        //    (playerTurn && (inMovableArea || ctrlHold) && !abilityArmed) || deploymentPhase. So cover is NOT gated on
        //    reachability here — the scanned tile always names its cover, and reachability is an ADDITIVE cue:
        //    "unreachable" flags the absence of the blue move-highlight, it does not suppress the cover (the old
        //    reachable-only gate dropped every cover a sighted player scouts with Ctrl before moving, and stayed silent
        //    through the whole deployment phase). While an ability is ARMED the mesh hides cover (the targeting overlay
        //    replaces it), so we suppress too — EXCEPT in the deployment phase, where the mesh shows cover regardless.
        //    Directions N/E/S/W = dirs 2/1/0/3, read from the same LosCalculations source the mesh uses, with the
        //    mesh's own BySource perspective (the selected/acting unit) when a unit is selected so exclusive-user
        //    forced cover resolves as on-screen; ByTarget only when nothing is selected (pre-deploy), to avoid
        //    dereferencing a null selection.
        var turn = Game.Instance?.TurnController;
        // TurnBasedModeActive FIRST: IsPreparationTurn walks TurnOrder → Data → Player.GetOrCreate<TurnDataPart>(),
        // which throws on a null Player between area loads (see DeploymentMode.Active).
        bool tbActive = turn != null && Game.Instance?.Player != null && turn.TurnBasedModeActive;
        bool abilityArmed = Game.Instance?.CursorController?.SelectedAbility != null;   // mesh hides cover while aiming
        // The turn-state half of the mesh predicate lives in CoverOverlayActive so the cover SCANNER
        // (CoverModel / ProxyCover, the J cycle) cannot drift from this readout about whether cover is knowable.
        bool coverShown = seen && node.Walkable && CoverOverlayActive;
        if (coverShown)
        {
            var checkType = CoverCheckType;
            AppendCover(sb, node, 2, "aim.dir_n", checkType);
            AppendCover(sb, node, 1, "aim.dir_e", checkType);
            AppendCover(sb, node, 0, "aim.dir_s", checkType);
            AppendCover(sb, node, 3, "aim.dir_w", checkType);

            // Reachability is an additive note, not a cover gate. UnitMovableAreaController.CurrentUnit is non-null
            // only for a live directly-controllable turn; a tile outside that unit's movable area is "unreachable".
            var controller = Game.Instance?.UnitMovableAreaController;
            if (controller?.CurrentUnit != null && controller.CurrentUnitMovableArea?.Contains(node) == false)
                Append(sb, Loc.T("tile.unreachable"));
        }

        // 2b. Ship reachability (space combat): the branch above rides UnitMovableAreaController, which never
        //     runs for starships — a ship's turn budget lives in Navigation.ReachableTiles (kept fresh by the
        //     game's StarshipPathController). Same additive cue, same word, plus the inertia-specific
        //     "pass-through only" for cells the move fan crosses but cannot stop on. Suppressed while aiming,
        //     like the cover overlay (the aim readout owns the cursor then).
        if (seen && !abilityArmed && tbActive && turn.IsPlayerTurn
            && turn.CurrentUnit is Kingmaker.EntitySystem.Entities.StarshipEntity actingShip)
            Append(sb, RTAccess.Exploration.ShipPathInfo.TileReachabilityWord(actingShip, node));

        // 2c. "No path" — the tile is walkable but sits on a walkable island the anchor cannot reach on foot at
        //     all: a catwalk overhead, a ledge behind a railing, a fenced-off pocket. This is NOT the movement-point
        //     question section 2 answers ("unreachable" = outside THIS turn's blue highlight, and only spoken while
        //     the cover overlay is live, i.e. in your own turn with no ability armed). It is the harder fact, and
        //     the tile cursor never asked it: Reachability.Classify has been sorting the SCANNER's lists since the
        //     Kiava Gamma fix, but DescribeTile never called it — so a cell the pathfinder cannot enter under any
        //     circumstances read as bare empty floor, and the only feedback was the move-to's unexplained refusal
        //     (August field report #5: "one arrow left, path blocked, no wall, no door, no object in the way").
        //     Walls in RT are FENCES on cell edges, so the blocked cell itself is perfectly walkable and neither
        //     the "wall" word nor the object headline ever fires for it — the connected-component id is the only
        //     honest witness. Fog-gated like everything else here, and skipped on unwalkable cells (already "wall").
        //     ClassifyNode, not Classify: the point-based one tolerates a 5x5 neighbourhood (right for an off-grid
        //     chest, wrong here) and would call a fenced-off single cell reachable because its neighbour across the
        //     fence is on the party's island — silent in exactly the reported case.
        if (seen && node.Walkable
            && RTAccess.Exploration.Reachability.ClassifyNode(node) == RTAccess.Exploration.ReachClass.Elsewhere)
            Append(sb, Loc.T("tile.no_path"));

        // 3. Offset from the anchor unit, in tiles (+Z = north, +X = east — matches the compass above).
        Append(sb, RelativeTile(node, anchor));
        return sb.ToString();
    }

    /// <summary>Name + verb of the interactable map object nearest this tile (within <see cref="InteractReach"/>),
    /// if any — the map-object headline for <see cref="DescribeTile"/>. Delegates to <see cref="InteractableAt"/> so
    /// the readout names exactly the object the cursor's Enter would act on.</summary>
    private static bool TryNameMapObject(CustomGridNodeBase node, out string name, out string verb)
    {
        name = null;
        verb = null;
        var mapObject = InteractableAt(node);
        if (mapObject?.View == null) return false;
        try
        {
            name = ResolveName(mapObject.View, out var interaction);
            verb = Verb(interaction);
        }
        catch (Exception e) { Main.Log?.Error("DescribeTile map-object lookup failed: " + e); }
        return !string.IsNullOrWhiteSpace(name);
    }

    // Interactables live in continuous world-space, NOT slotted one-per-tile: an object's Position sits up to ~0.95 m
    // (the cell-corner distance) off any cell centre, can straddle an edge/corner shared by 2-4 tiles, span several
    // cells, or occupy none — and the cursor snaps to the nearest WALKABLE node, which for a door set in a wall is the
    // adjacent FLOOR cell, not the door's (unwalkable) cell. So a grid-footprint containment test misses objects the
    // player is clearly pointing at. Instead the readout and the cursor's Enter both resolve the nearest interactable
    // within this reach of the cursor, gated by the game's own availability check — mirroring how the console/gamepad
    // interaction picker works (SurfaceMainInputLayer). See docs/plans + the rt-world-grid memory.
    private static float InteractReach => GraphParamsMechanicsCache.GridCellSize * 1.5f;

    /// <summary>The interactable map object nearest <paramref name="node"/> within <see cref="InteractReach"/>, or
    /// null — the single-object resolver behind <see cref="DescribeTile"/>'s object headline. A thin "nearest = first"
    /// wrapper over <see cref="InteractablesAt"/>, so both share one gate and one distance metric.</summary>
    public static MapObjectEntity InteractableAt(CustomGridNodeBase node) => InteractablesAt(node).FirstOrDefault();

    /// <summary>EVERY actionable map object within <see cref="InteractReach"/> of <paramref name="node"/>, nearest
    /// first (empty when none) — the resolver behind the interact keys. Interactables are off-grid, so this is a
    /// proximity query (not grid-footprint containment) and more than one can sit within reach of a single tile
    /// (clustered loot, a door beside a lever); the cursor's Enter pops a chooser when there is more than one (see
    /// <see cref="RTAccess.Exploration.Activation"/>). Gated by the game's own
    /// <see cref="ClickMapObjectHandler.HasAvailableInteractions"/> (plus area-transition exits, which carry no
    /// InteractionPart). Each chosen object is driven through the game's click handler by
    /// <see cref="RTAccess.Exploration.ProxyMapObject.Interact"/>.</summary>
    public static List<MapObjectEntity> InteractablesAt(CustomGridNodeBase node)
        => node == null ? new List<MapObjectEntity>() : InteractablesAt((Vector3)node.position);

    /// <summary>As <see cref="InteractablesAt(CustomGridNodeBase)"/> but around an arbitrary world point — lets the
    /// scanner's I key resolve the interactable(s) co-located with the review SELECTION (its position), reproducing
    /// the manual "plant the cursor on the selection, then Enter" without stepping the movement cursor there.</summary>
    public static List<MapObjectEntity> InteractablesAt(Vector3 origin)
    {
        var list = new List<MapObjectEntity>();
        try
        {
            foreach (var mapObject in EntityBoundsHelper.FindEntitiesInRange(origin, InteractReach).OfType<MapObjectEntity>())
                if (IsActionable(mapObject)) list.Add(mapObject);
            list.Sort((a, b) => SqrXZ(a.Position, origin).CompareTo(SqrXZ(b.Position, origin)));
        }
        catch (Exception e) { Main.Log?.Error("InteractablesAt failed: " + e); }
        return list;
    }

    /// <summary>Squared XZ (planar) distance — the ground-plane metric the interact reach uses, ignoring height.</summary>
    private static float SqrXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    /// <summary>Can the player act on this map object right now — the game's own gate (an available interaction, or
    /// an area-transition exit, which carries no InteractionPart)? This mirrors ClickMapObjectHandler exactly, so the
    /// tile cursor surfaces precisely what a sighted player could click — including bark/examine interactions, which
    /// are genuine UIInteractionType.Info interactions (see the scanner gate in
    /// <see cref="RTAccess.Exploration.ProxyMapObject"/>).</summary>
    private static bool IsActionable(MapObjectEntity o)
    {
        if (o?.View == null) return false;
        if (ClickMapObjectHandler.HasAvailableInteractions(o.View.gameObject)) return true;
        return o.GetOptional<AreaTransitionPart>() != null;
    }

    /// <summary>Append every active ground hazard / buff zone (fire, gas, a psychic cloud) whose PAINTED pattern
    /// covers this tile (<see cref="RTAccess.Exploration.ProxyAreaEffect.Covers"/> — the quantized CoveredNodes the
    /// sighted decal draws and the game's pass-through rule tests, NOT the metric shape, which over-reports a rim
    /// of unpainted cells; same rule as PathInfo.HazardWarning), worded exactly as the area scanner reads them
    /// (name + "hazard"/"buff zone"). Sources the same placed-zone proxies from
    /// <see cref="RTAccess.Exploration.WorldModel"/>, so on-unit auras are already excluded
    /// and each zone's own fog visibility (<see cref="RTAccess.Exploration.ScanItem.IsVisible"/>) still gates it.</summary>
    private static void AppendZones(StringBuilder sb, CustomGridNodeBase node)
    {
        try
        {
            foreach (var item in RTAccess.Exploration.WorldModel.Items)
            {
                if (!(item is RTAccess.Exploration.ProxyAreaEffect zone) || !zone.IsVisible || !zone.Covers(node))
                    continue;
                var label = zone.Name;
                if (!string.IsNullOrWhiteSpace(zone.Detail)) label += ", " + zone.Detail;
                Append(sb, label);
            }
        }
        catch (Exception e) { Main.Log?.Error("DescribeTile hazard read failed: " + e); }
    }

    /// <summary>Append the localized "trap zone" word when this tile lies inside a revealed, armed trap's
    /// trigger-zone collider (main-HUD audit #9). The zone's MeshCollider is the one the game ensures on the
    /// ScriptZoneTrigger's renderer and reparents under the trap view (<c>TrapObjectView.Collider</c>); a
    /// downward collider raycast is the containment test (works on non-convex meshes, matches the rendered
    /// XZ shape a sighted player sees). Gated exactly like the warning decal: view visible &amp;&amp; TrapActive.</summary>
    private static void AppendTrapZone(StringBuilder sb, CustomGridNodeBase node)
    {
        try
        {
            var objs = Game.Instance?.State?.MapObjects;
            if (objs == null) return;
            var p = node.Vector3Position;
            foreach (var mo in objs)
            {
                if (!(mo?.View is TrapObjectView tv)) continue;
                if (!tv.IsVisible || tv.Data?.TrapActive != true) continue;
                var col = tv.Collider;
                if (col == null || !col.enabled) continue;
                // Short vertical window (±1 m around the tile plane): the zone mesh lies on the floor, and a
                // longer ray could cross into a zone on a storey below/above on multi-level maps.
                if (col.Raycast(new Ray(p + Vector3.up * 1f, Vector3.down), out _, 2f))
                {
                    Append(sb, Loc.T("tile.trap_zone"));
                    return; // one flag is enough — overlapping zones read identically
                }
            }
        }
        catch (Exception e) { Main.Log?.Error("DescribeTile trap-zone read failed: " + e); }
    }

    /// <summary>
    /// Is the game's own cover overlay showing right now — i.e. may the mod speak a tile's cover at all? The
    /// turn-state half of <c>CoverVisualizer.IsNodeCoverVisible</c>:
    /// <c>(playerTurn &amp;&amp; !abilityArmed) || deploymentPhase</c>, both inside turn-based mode. The mesh's
    /// remaining clause — in the movable area OR Ctrl held — is deliberately dropped: holding Ctrl reveals cover on
    /// every nearby walkable cell in or out of range, so reachability is an additive cue, not a gate (see the
    /// DescribeTile comment above and <see cref="RTAccess.Exploration.CoverModel"/>).
    ///
    /// Single-sourced here because two surfaces read it — this tile readout and the cover scanner (the J cycle /
    /// "Cover" browse category). If they disagreed, the scanner could find a spot the cursor then refuses to
    /// describe, which is exactly the kind of self-contradiction a blind player cannot debug.
    /// </summary>
    internal static bool CoverOverlayActive
    {
        get
        {
            // TurnBasedModeActive FIRST: IsPreparationTurn walks TurnOrder → Data → Player.GetOrCreate<TurnDataPart>(),
            // which throws on a null Player between area loads (see DeploymentMode.Active).
            var turn = Game.Instance?.TurnController;
            if (turn == null || Game.Instance?.Player == null || !turn.TurnBasedModeActive) return false;
            if (turn.IsPreparationTurn && turn.IsDeploymentAllowed) return true;   // deployment shows cover regardless
            return turn.IsPlayerTurn && Game.Instance?.CursorController?.SelectedAbility == null;
        }
    }

    /// <summary>The perspective the cover oracle is asked from, matching the mesh: BySource (the selected/acting
    /// unit) so exclusive-user forced cover resolves as on-screen, ByTarget only when nothing is selected
    /// (pre-deploy), to avoid dereferencing a null selection.</summary>
    internal static LosCalculations.ForcedCoverCheckType CoverCheckType
        => Game.Instance?.SelectionCharacter?.SelectedUnit?.Value != null
            ? LosCalculations.ForcedCoverCheckType.BySource
            : LosCalculations.ForcedCoverCheckType.ByTarget;

    /// <summary>Append "half/full cover &lt;dir&gt;" (or "blocked &lt;dir&gt;" for sight-blocking) for one edge, read
    /// with the same <paramref name="checkType"/> the game's cover mesh uses (BySource on the acting unit).
    /// <paramref name="dirKey"/> is the localization key for the edge's direction word.</summary>
    private static void AppendCover(StringBuilder sb, CustomGridNodeBase node, int direction, string dirKey,
        LosCalculations.ForcedCoverCheckType checkType)
    {
        LosCalculations.CoverType cover;
        try { cover = LosCalculations.GetCellCoverStatus(node, direction, checkType).CoverType; }
        catch (Exception e) { Main.Log?.Error("DescribeTile cover read failed: " + e); return; }
        var dir = Loc.T(dirKey);
        switch (cover)
        {
            case LosCalculations.CoverType.Half: Append(sb, Loc.T("cover.half_dir", new { dir })); break;
            case LosCalculations.CoverType.Full: Append(sb, Loc.T("cover.full_dir", new { dir })); break;
            case LosCalculations.CoverType.Invisible: Append(sb, Loc.T("cover.blocked_dir", new { dir })); break;
        }
    }

    /// <summary>Tile offset from the anchor's node, e.g. "5 east, 2 north"; "here" on the anchor's own tile.</summary>
    private static string RelativeTile(CustomGridNodeBase node, MechanicEntity anchor)
    {
        var origin = anchor?.CurrentUnwalkableNode;
        if (origin == null) return null;
        int dx = node.XCoordinateInGrid - origin.XCoordinateInGrid; // east(+) / west(-)
        int dz = node.ZCoordinateInGrid - origin.ZCoordinateInGrid; // north(+) / south(-)
        // The vertical term FIRST, because it is the one that can be true while the plan offset says "beside you":
        // the grid stores exactly one node per XZ column — the TOPMOST walkable surface (see
        // NavmeshProbe.WalkableNodeOnLevel) — so the cell one step west of a unit standing under a catwalk is the
        // CATWALK, several metres up. Without this the two read byte-identically, the tile sounds adjacent and
        // walkable, and the move-to then refuses with an unexplained "no path" (August field report #5). Same
        // Geo.Vertical the scanner's bearings already use, so "3 metres up" means the same thing everywhere.
        var vertical = RTAccess.Exploration.Geo.Vertical((Vector3)origin.Vector3Position, (Vector3)node.Vector3Position);
        if (dx == 0 && dz == 0)
            return string.IsNullOrEmpty(vertical) ? Loc.T("geo.here") : Loc.T("geo.here") + ", " + vertical;
        var sb = new StringBuilder();
        if (dx != 0)
            sb.Append(Loc.T("geo.offset", new { count = dx > 0 ? dx : -dx, dir = Loc.T(dx > 0 ? "aim.dir_e" : "aim.dir_w") }));
        if (dz != 0)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(Loc.T("geo.offset", new { count = dz > 0 ? dz : -dz, dir = Loc.T(dz > 0 ? "aim.dir_n" : "aim.dir_s") }));
        }
        if (!string.IsNullOrEmpty(vertical)) sb.Append(", ").Append(vertical);
        return sb.ToString();
    }

    // internal so ProxyMarker (the scanner's Exits/Poi landmark items) can slot the same type word after the name.
    internal static string MarkerTypeLabel(LocalMapMarkType type)
    {
        switch (type)
        {
            case LocalMapMarkType.Exit: return Loc.T("marker.exit");
            // NOT a quest objective: LocalMapVM creates one DestinationMark per party member as that unit's own
            // pending move-destination pin (LocalMapDestinationMarkerVM, IsVisible=false, position = the unit's
            // position). Calling it "objective" invented a quest marker the game does not have.
            case LocalMapMarkType.DestinationMark: return Loc.T("marker.destination");
            case LocalMapMarkType.VeryImportantThing: return Loc.T("marker.important");
            case LocalMapMarkType.Loot: return Loc.T("marker.loot");
            case LocalMapMarkType.Poi: return Loc.T("marker.poi");
            case LocalMapMarkType.Unit: return Loc.T("marker.creature");
            default: return null;
        }
    }

    /// <summary>Is this skill check a way between floors rather than a thing to search? Shared with
    /// <c>ProxyMapObject.NodeSet</c> so the spoken name and the browse category can never disagree about what an
    /// object is.</summary>
    public static bool IsLevelChange(InteractionSkillCheckPart check)
    {
        var settings = check?.Settings;
        if (settings == null) return false;
        // The authored, dev-string-free signal: the check carries an enter point it teleports the party (and its
        // followers) to — InteractionSkillCheckPart.OnInteract calls Game.Instance.Teleport(...) with it. The game's
        // own area autoplayer uses exactly this test to recognise a transit interactable
        // (Kingmaker.QA.Clockwork.AreaTaskSelector: "Settings.TeleportOnSuccess ? 8 : 18").
        if (settings.TeleportOnSuccess != null || settings.TeleportOnFail != null) return true;
        // Fallback for the climbs that move the unit by animation rather than a teleport: RT expresses those as
        // ATHLETICS checks — verified in the Kiava Gamma manufactorum, which contains no InteractionStairsPart and
        // no pathfinding node link at all, yet whose ladders, holes and drops are all athletics checks.
        return settings.Skill == Kingmaker.EntitySystem.Stats.Base.StatType.SkillAthletics;
    }

    /// <summary>Is this level changer a climb (an athletics haul up a ladder / down a hole) rather than a passage
    /// that simply moves you (a teleporting console, a hatch)? Only splits the spoken NAME — both browse under
    /// <see cref="RTAccess.Exploration.ScanTaxonomy.LevelChanges"/>.</summary>
    private static bool IsClimb(InteractionSkillCheckPart check)
        => check?.Settings != null
            && check.Settings.Skill == Kingmaker.EntitySystem.Stats.Base.StatType.SkillAthletics;

    /// <summary>Does this lever/button interaction move the party to an area enter point — a lift call button, a
    /// hatch — rather than merely flipping something? Same authored signal as the skill-check case, read from the
    /// action list instead of a settings field: the game marks every enter-point mover with
    /// <c>IAreaEnterPointReference</c> (<c>TeleportParty</c> is the one that appears in an interaction's actions).
    /// Descends through Conditional branches, since a lift button is usually gated on which floor you are on.</summary>
    public static bool IsLevelChange(InteractionActionPart action)
    {
        try { return MovesParty(action?.Settings?.Actions?.Get()?.Actions, 0); }
        catch (Exception e) { Main.Log?.Error("IsLevelChange(action) failed: " + e); return false; }
    }

    private const int ActionScanDepth = 3;

    private static bool MovesParty(ActionList list, int depth)
    {
        if (list?.Actions == null || depth > ActionScanDepth) return false;
        foreach (var a in list.Actions)
        {
            if (a == null) continue;
            if (a is IAreaEnterPointReference) return true;
            if (a is Conditional c && (MovesParty(c.IfTrue, depth + 1) || MovesParty(c.IfFalse, depth + 1))) return true;
        }
        return false;
    }

    /// <summary>The name only (used for terse contexts); mirrors the type mapping in Describe. Public so the
    /// exploration scanner can reuse the same name + interaction resolution for its map-object proxies.</summary>
    public static string ResolveName(EntityViewBase entity, out InteractionPart interaction)
    {
        var name = ResolveNameCore(entity, out interaction);
        var dev = DevName(entity, name);
        return string.IsNullOrEmpty(dev) ? name : name + " " + dev;
    }

    /// <summary>
    /// The designer-facing identity of an object, appended to its spoken name when the
    /// <c>exploration.dev_names</c> setting is on (default OFF).
    ///
    /// Rogue Trader gives map objects no blueprint display name at all — <c>BlueprintMapObject</c> carries only a
    /// prefab — so when a designer leaves <c>DisplayName</c> empty there is genuinely no name to read, for anyone.
    /// What survives is the scene object's own name, and in practice it is highly descriptive: the Kiava Gamma
    /// manufactorum names them <c>LadderUp</c>, <c>Button9_ToDataVault</c>, <c>ChaosAltar</c>. It is untranslated
    /// developer English, which is exactly why it is opt-in rather than the default label — but as a toggle it
    /// tells a player which of twenty identical "search points" is the way up.
    ///
    /// The blueprint is included only when it is not the generic shared asset (every skill-check object in that
    /// area is <c>DefaultMapObject</c>, which distinguishes nothing), and a part that merely repeats
    /// <paramref name="name"/> is dropped — otherwise an object we already name after its scene object read as
    /// "BloodTrace_01 [BloodTrace_01]" (101 such lines in one play session).
    /// </summary>
    private static string DevName(EntityViewBase entity, string name)
    {
        if (entity == null || !DevNamesEnabled) return null;
        try
        {
            var go = Clean(entity.GameObjectName)?.Replace("(Clone)", "").Trim();
            var bp = (entity.Data as MapObjectEntity)?.Blueprint?.name;
            if (string.Equals(bp, "DefaultMapObject", StringComparison.OrdinalIgnoreCase)) bp = null;
            // Same "shared asset that distinguishes nothing" rule, for destructible scenery. A destructible's
            // blueprint is normally one of a handful of toughness presets the engine hands out by enum
            // (StandardDestructibleObjectType -> ExtremeLowHitPointsAndArmor, LowHitPointsLowArmor, ...), naming a
            // durability class every destructible listing already speaks as "N of M HP" — not this object. The view
            // itself draws the line, so honour it: only an authored CUSTOM blueprint identifies anything.
            if (entity is DestructibleEntityView dv && !dv.UseCustomBlueprint) bp = null;
            if (Repeats(go, name)) go = null;
            if (Repeats(bp, name) || Repeats(bp, go)) bp = null;
            if (string.IsNullOrWhiteSpace(go) && string.IsNullOrWhiteSpace(bp)) return null;
            if (string.IsNullOrWhiteSpace(bp)) return "[" + go + "]";
            if (string.IsNullOrWhiteSpace(go)) return "[" + bp + "]";
            return "[" + go + " / " + bp + "]";
        }
        catch (Exception e) { Main.Log?.Error("DevName failed: " + e); return null; }
    }

    /// <summary>The opt-in designer identity suffix ("[SceneObject]" / "[SceneObject / Blueprint]") for a view whose
    /// name was resolved somewhere other than <see cref="ResolveName"/> — destructible scenery names itself from its
    /// entity, but earns the same <c>exploration.dev_names</c> readout, and needs it more than most: a whole area's
    /// covers, barrels and branches share one blueprint display name ("Укрытие"), so with the toggle off nothing in
    /// the browse list tells them apart. Null/empty when the setting is off or nothing distinguishing survives.</summary>
    public static string DevSuffix(EntityViewBase entity, string name) => DevName(entity, name);

    /// <summary>Would speaking <paramref name="part"/> just repeat <paramref name="spoken"/>?</summary>
    private static bool Repeats(string part, string spoken)
        => string.IsNullOrWhiteSpace(part)
            || string.Equals(part.Trim(), spoken?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool DevNamesEnabled
        => RTAccess.Settings.ModSettings
            .GetSetting<RTAccess.Settings.BoolSetting>("exploration.dev_names")?.Get() ?? false;

    /// <summary>
    /// The interaction part an object should be NAMED and described from: its first ENABLED one, falling back to
    /// the engine's own pick when none is live.
    ///
    /// <c>EntityViewBase.InteractionComponent</c> is <c>GetAll&lt;InteractionPart&gt;().FirstOrDefault()</c> — the
    /// first part regardless of <c>Enabled</c> — while the browse category (<c>ProxyMapObject.NodeSet</c>) and the
    /// detail line both iterate ALL parts and skip disabled ones. An object carrying a spent skill check ahead of a
    /// live loot part therefore browsed under "Containers" while being announced as a "Search point": the name and
    /// the category described different halves of the same object. Preferring the enabled part makes the two agree.
    /// A door is the deliberate exception the caller already handles (a disabled-but-open door is still a door).
    /// </summary>
    private static InteractionPart PrimaryInteraction(EntityViewBase entity)
    {
        if (entity?.Data == null) return null;
        try
        {
            if (entity.Data is MapObjectEntity mapObject)
            {
                InteractionPart fallback = null;
                foreach (var part in mapObject.Interactions)
                {
                    if (part == null) continue;
                    if (part.Enabled) return part;
                    fallback ??= part;
                }
                if (fallback != null) return fallback;
            }
        }
        catch (Exception e) { Main.Log?.Error("PrimaryInteraction failed: " + e); }
        return entity.InteractionComponent;
    }

    private static string ResolveNameCore(EntityViewBase entity, out InteractionPart interaction)
    {
        interaction = PrimaryInteraction(entity);

        // Units (NPCs / enemies / crowd): CharacterName covers both BaseUnitEntity and LightweightUnitEntity
        // (both derive AbstractUnitEntity). The v1 BaseUnitEntity-only cast missed lightweight crowd and fell
        // back to the raw GameObject name ("BCT_...(Clone)").
        if (entity.Data is AbstractUnitEntity unit && !string.IsNullOrWhiteSpace(unit.CharacterName))
            return UnitNames.Of(unit);

        var tips = Game.Instance?.BlueprintRoot?.LocalizedTexts?.UserInterfacesText?.Tooltips;
        switch (interaction)
        {
            case InteractionDoorPart:
                return tips?.Door?.Text ?? Loc.T("scan.singular.door");
            case InteractionLootPart loot:
                var lootName = loot.GetName();
                return string.IsNullOrWhiteSpace(lootName) ? Loc.T("scan.singular.container") : lootName;
            case InteractionStairsPart:
                return tips?.Ladder?.Text ?? Loc.T("scan.singular.stairs");
            case InteractionActionPart action:
                var actionName = action.Settings?.DisplayName?.String?.Text;
                if (!string.IsNullOrWhiteSpace(actionName)) return actionName;
                // Unnamed: say which of the two it is, matching the browse category (see IsLevelChange).
                return Loc.T(IsLevelChange(action) ? "scan.singular.passage" : "scan.singular.action");
            // Mirror the overtip (OvertipMapObjectVM): the designer's DisplayName while live, and the
            // DisplayNameAfterUse swap once a check-once interaction is spent — the game's own "already
            // examined" cue. No designer name → the localized category singular (the raw GameObject name
            // the old fallback produced is dev-string junk for these locators).
            case InteractionSkillCheckPart check:
                var used = check.AlreadyUsed && check.Settings?.OnlyCheckOnce == true;
                var checkName = Clean((used ? check.Settings?.DisplayNameAfterUse : check.Settings?.DisplayName)?.String?.Text);
                if (!string.IsNullOrWhiteSpace(checkName)) return checkName;
                // No designer name. A check that MOVES you — a climb/jump/vault, or one carrying a teleport enter
                // point — is a way between floors, and calling that "Search point" is what made the Kiava Gamma
                // manufactorum unnavigable: every ladder, hole and drop there is an unnamed athletics check. The
                // skill is on the card the sighted player reads ("[Athletics: 40%]"), so naming it after the skill
                // claims nothing extra.
                if (IsLevelChange(check))
                    return Loc.T(IsClimb(check) ? "scan.singular.climb_point" : "scan.singular.passage");
                return Loc.T("scan.singular.search_point");

            // A bark/examine volume. The game itself leaves these NAMELESS: OvertipMapObjectVM.UpdateObjectData
            // has no case for InteractionBarkPart, so Name.Value stays string.Empty and a sighted player sees a
            // bare interaction icon. The old GameObject-name fallback below therefore spoke raw untranslated
            // dev strings ("CorruptedCogitatorBark", "BloodTrace_01") as if they were content. Name it by what it
            // is; the scene object's own name stays available behind the dev-names setting.
            case InteractionBarkPart:
                return Loc.T("scan.singular.examine_point");
        }

        // Trap parts (several subtypes) — match by name so we don't bind every concrete type.
        if (interaction != null && interaction.GetType().Name.Contains("Trap"))
            return tips?.Trap?.Text ?? Loc.T("scan.singular.trap");

        // Area exits (main-HUD audit #2): a transition carries no InteractionPart — its name is the destination
        // tooltip the sighted overtip shows persistently (OvertipTransitionVM.Title). Prefer the per-exit
        // Tooltip(TooltipIndex) over the index-less TooltipDescription (the game's own local-map marker makes the
        // same call, AreaTransitionPart.OnSettingsDidSet), falling back to the localized exit word when the
        // designer left it empty. Without this case exits fell to the GameObject-name junk below.
        var transition = (entity.Data as MapObjectEntity)?.GetOptional<AreaTransitionPart>();
        if (transition != null)
        {
            var title = Clean(transition.AreaEnterPoint?.Tooltip(transition.Settings?.TooltipIndex ?? 0)?.Text);
            return string.IsNullOrWhiteSpace(title) ? Loc.T("scan.singular.exit") : title;
        }

        // Last resort: the localized generic word. NOT the GameObject name — that is untranslated developer
        // English ("BloodTrace_01", "BigPipes") and the game shows nothing at all for the parts that land here,
        // so speaking it both leaked dev strings into the default readout and duplicated the dev-names suffix.
        // The scene object's own name is still reachable, deliberately, through <c>exploration.dev_names</c>.
        return Loc.T("scan.singular.object");
    }

    /// <summary>The skill-check line the object's overtip card shows on hover — the short description plus the
    /// "[Skill: NN%]" success chance for the currently selected unit(s) (or, once a check-once interaction is
    /// spent, the designer's passed/failed after-use description); for an armed, detected trap the
    /// "[DisarmSkill: NN%]" line. Pure pass-through of the game's own localized card text
    /// (<c>UIUtility.GetOvertipSkillCheckText</c> / <c>GetTrapSkillCheckText</c> — the exact sighted-hover
    /// parity, including the HideDC "[Skill]"-only form). Null when the part carries no card line. Public so
    /// the scanner's map-object proxies and the focused readout speak the same line.</summary>
    public static string CheckInfo(InteractionPart interaction)
    {
        try
        {
            switch (interaction)
            {
                case InteractionSkillCheckPart check when check.Enabled:
                {
                    var settings = check.Settings;
                    if (settings == null) return null;
                    if (check.AlreadyUsed && settings.OnlyCheckOnce)
                    {
                        // Clean() returns "" (not null) for a blank asset, and most spent one-shot examine points
                        // leave the after-use description empty — returning that emptiness produced a hollow
                        // segment in the spoken line ("Search point, examine, , 56 tiles"), 57 times in one session.
                        var after = Clean((check.CheckPassed ? settings.ShortDescriptionPassed : settings.ShortDescriptionFailed)?.String?.Text);
                        return string.IsNullOrWhiteSpace(after) ? null : after;
                    }
                    var desc = Clean(settings.ShortDescription?.String?.Text);
                    var units = Game.Instance?.SelectionCharacter?.SelectedUnits?.ToList();
                    var chance = units != null && units.Count > 0
                        ? UIUtility.GetOvertipSkillCheckText(check, units, out _)
                        : null;
                    if (string.IsNullOrWhiteSpace(desc)) return string.IsNullOrWhiteSpace(chance) ? null : chance;
                    return string.IsNullOrWhiteSpace(chance) ? desc : desc + ", " + chance;
                }
                case DisableTrapInteractionPart trap when trap.Enabled && trap.Owner?.TrapActive == true:
                {
                    var units = Game.Instance?.SelectionCharacter?.SelectedUnits?.ToList();
                    var text = units != null && units.Count > 0 ? UIUtility.GetTrapSkillCheckText(trap, units) : null;
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
            }
        }
        catch (Exception e) { Main.Log?.Error("CheckInfo failed: " + e); }
        return null;
    }

    /// <summary>English verb for the interaction type; null when there is no meaningful verb. Public so the
    /// exploration scanner can reuse it for map-object detail lines.</summary>
    public static string Verb(InteractionPart interaction)
    {
        if (interaction == null) return null;
        switch (interaction.UIInteractionType)
        {
            case UIInteractionType.Action: return Loc.T("verb.activate");
            case UIInteractionType.Move: return Loc.T("verb.approach");
            case UIInteractionType.Info: return Loc.T("verb.examine");
            case UIInteractionType.Credits: return Loc.T("verb.collect");
            case UIInteractionType.Pets: return Loc.T("verb.interact");
            default: return null;
        }
    }

    /// <summary>Distance + map-relative compass between two world points, e.g. "6 tiles, north-east". Distance is
    /// reported in grid tiles (the game's own unit), not metres, so it matches the combat cell readouts and the tile
    /// explorer's offsets. Public so the exploration scanner speaks the same compass as the other navigators.
    ///
    /// A vertical term ("down 6 metres") follows the bearing whenever the two points are on different levels — the
    /// tiles+compass pair is a plan projection, so without it a catwalk 6 m overhead and the floor beneath it read
    /// identically. Every navigator that speaks a bearing goes through here, so they all gain it at once.</summary>
    public static string DirectionAndDistance(Vector3 from, Vector3 to)
    {
        float dx = to.x - from.x; // east(+) / west(-)
        float dz = to.z - from.z; // north(+) / south(-)
        float dist = RTAccess.Exploration.Geo.Distance(from, to);
        int tiles = Mathf.RoundToInt(dist / GraphParamsMechanicsCache.GridCellSize); // world metres -> 1.35 m grid cells
        var sb = new StringBuilder();
        sb.Append(tiles == 1 ? Loc.T("aim.tile_one") : Loc.T("aim.tiles", new { count = tiles }));
        if (dist > 0.5f && RTAccess.Exploration.Geo.CompassSector(dx, dz, out int sector))
            sb.Append(", ").Append(Loc.T(Compass8[sector]));
        Append(sb, AxisOffset(dx, dz));
        var vertical = RTAccess.Exploration.Geo.Vertical(from, to);
        if (!string.IsNullOrEmpty(vertical)) sb.Append(", ").Append(vertical);
        return sb.ToString();
    }

    /// <summary>
    /// The two-axis tile breakdown behind a diagonal bearing — "6 north, 3 east" after "7 tiles, north-east".
    /// A 45°-wide compass sector is a wedge, not a direction: "north-east, 7 tiles" describes every cell from
    /// (1,7) to (7,1), which is most of a room, and a player trying to walk it has to guess (August field report
    /// #6: "it's just cardinal directions but this can mean nearly everything"). The distance still leads, because
    /// that is the number ability ranges are counted in — this only disambiguates the bearing behind it.
    ///
    /// Emitted ONLY for genuinely diagonal bearings (both axes non-zero in tiles). On a cardinal the compass word
    /// is already exact and the breakdown would just re-say the distance ("5 tiles, north, 5 north"), which is the
    /// spam the same report warned about. Off returns null and nothing is appended.
    /// </summary>
    private static string AxisOffset(float dx, float dz)
    {
        if (!AxisOffsetsEnabled) return null;
        float cell = GraphParamsMechanicsCache.GridCellSize;
        int ex = Mathf.RoundToInt(dx / cell), nz = Mathf.RoundToInt(dz / cell);
        if (ex == 0 || nz == 0) return null;   // cardinal (or co-located): the compass word already says it exactly
        var sb = new StringBuilder();
        sb.Append(Loc.T("geo.offset", new { count = nz > 0 ? nz : -nz, dir = Loc.T(nz > 0 ? "aim.dir_n" : "aim.dir_s") }));
        sb.Append(", ");
        sb.Append(Loc.T("geo.offset", new { count = ex > 0 ? ex : -ex, dir = Loc.T(ex > 0 ? "aim.dir_e" : "aim.dir_w") }));
        return sb.ToString();
    }

    private static bool AxisOffsetsEnabled =>
        RTAccess.Settings.ModSettings.GetSetting<RTAccess.Settings.BoolSetting>("exploration.axis_offsets")?.Get() ?? true;

    private static void Append(StringBuilder sb, string part)
    {
        if (string.IsNullOrEmpty(part)) return;
        if (sb.Length > 0) sb.Append(", ");
        sb.Append(part);
    }

    // Strip TMP rich-text (and decorative sub/superscript) from game-sourced text for speech; "" for blank input.
    private static string Clean(string raw)
        => string.IsNullOrWhiteSpace(raw) ? string.Empty : TextUtil.StripRichTextSpaced(raw);
}
