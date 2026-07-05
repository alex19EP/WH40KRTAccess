using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kingmaker;
using Kingmaker.Controllers.Clicks.Handlers; // ClickMapObjectHandler.HasAvailableInteractions (the game's own gate)
using Kingmaker.Controllers.Optimization;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Utils;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Mechanics.Entities;
using Kingmaker.Pathfinding;
using Kingmaker.View;
using Kingmaker.View.Covers;
using Kingmaker.View.MapObjects;
using Kingmaker.View.MapObjects.InteractionComponentBase;
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
    private static readonly Regex RichText = new Regex("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

    // 8-way MAP-relative compass (world axes: +Z = north, +X = east). Stable regardless of camera rotation.
    // internal: the system-map screen speaks the same compass for ship-relative bearings.
    internal static readonly string[] Compass8 =
        { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

    /// <summary>Full spoken line for a chosen interactable view. Never throws; returns "" if nothing readable.</summary>
    public static string Describe(EntityViewBase entity)
    {
        if (entity == null) return string.Empty;

        var sb = new StringBuilder();
        var name = ResolveName(entity, out var interaction);
        if (!string.IsNullOrWhiteSpace(name)) sb.Append(name);

        var verb = Verb(interaction);
        if (verb != null) Append(sb, verb);

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
    /// (<see cref="TileExplorer"/>): occupant, then walkability/reason when empty, then cover on each cardinal
    /// edge, then the tile offset from the anchor. Never throws; returns "" only when <paramref name="node"/> is null.
    /// </summary>
    public static string DescribeTile(CustomGridNodeBase node, MechanicEntity anchor)
    {
        if (node == null) return string.Empty;
        var sb = new StringBuilder();

        // 1. Headline — what is on/near the tile. A unit on the tile is announced first; the interactable NEAR this
        //    tile is then ALWAYS announced too (even behind a unit), because the cursor's Enter acts on it and it can
        //    share the tile with a unit — interactables are off-grid, so this is a nearest-within-reach hint, not a
        //    per-tile lookup. With neither a unit nor an interactable, walkability fills in (unwalkable = "wall",
        //    empty walkable = "clear").
        // Visual parity: gate the layout/occupant readout by the tile's fog state so a blind player hears only what a
        // sighted player could perceive on the local map. A never-seen tile reveals nothing but "unexplored"; an
        // explored-but-not-currently-visible tile reveals its static layout (walls / doors / containers) but NOT a
        // live creature now standing in the fog; a currently-visible (or fog-off / off-map) tile reads in full. The
        // cursor's own POSITION is never fog-hidden (the player drives it), so the offset (section 3) is always spoken.
        var fog = RTAccess.Exploration.FogProbe.Classify((Vector3)node.position);
        bool seen = fog != RTAccess.Exploration.FogProbe.FogState.NeverSeen;
        bool hideUnits = fog == RTAccess.Exploration.FogProbe.FogState.Explored;   // explored-not-visible: static layout only
        if (!seen) sb.Append("unexplored");

        var unit = seen && !hideUnits ? node.GetUnit() : null;
        if (unit != null)
        {
            sb.Append(unit.CharacterName);
            Append(sb, unit.Faction != null && unit.Faction.IsPlayerEnemy ? "enemy" : "ally");
            // The tile cursor sits ON the tile, so a corpse must be READ (not hidden) — but tagged so it doesn't read
            // as a live enemy. GetUnit() returns corpses (they stay in the grid's awake set until destroyed), and the
            // scanner cycles now skip the dead, so the tile cursor is the one place a corpse is still announced.
            if (unit.LifeState.IsDead) Append(sb, "dead");
            else if (!unit.LifeState.IsConscious) Append(sb, "unconscious");
        }
        if (seen && TryNameMapObject(node, out var objectName, out var objectVerb))
        {
            Append(sb, objectName);
            if (objectVerb != null) Append(sb, objectVerb);
        }
        else if (seen && unit == null)
        {
            if (DestructibleEntity.FindByNode(node) != null) sb.Append("obstacle");
            else sb.Append(node.Walkable ? "clear" : "wall");
        }

        // 1b. Ground hazard / buff zone standing ON this tile — fire, gas, a psychic cloud: the thing a sighted player
        //     sees burning on the floor, and in turn-based combat the real cost of stepping one tile into it. Read it
        //     like a live creature (only on a currently-visible tile, hidden in fog) from the same placed-zone proxies
        //     the area scanner lists, so the wording matches and on-unit auras stay excluded.
        if (seen && !hideUnits) AppendZones(sb, node);

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
        bool deployment = turn != null && turn.IsPreparationTurn && turn.IsDeploymentAllowed;
        bool abilityArmed = Game.Instance?.CursorController?.SelectedAbility != null;   // mesh hides cover while aiming
        bool coverShown = seen && node.Walkable && turn != null && turn.TurnBasedModeActive
            && (deployment || (turn.IsPlayerTurn && !abilityArmed));
        if (coverShown)
        {
            var checkType = Game.Instance?.SelectionCharacter?.SelectedUnit?.Value != null
                ? LosCalculations.ForcedCoverCheckType.BySource
                : LosCalculations.ForcedCoverCheckType.ByTarget;
            AppendCover(sb, node, 2, "north", checkType);
            AppendCover(sb, node, 1, "east", checkType);
            AppendCover(sb, node, 0, "south", checkType);
            AppendCover(sb, node, 3, "west", checkType);

            // Reachability is an additive note, not a cover gate. UnitMovableAreaController.CurrentUnit is non-null
            // only for a live directly-controllable turn; a tile outside that unit's movable area is "unreachable".
            var controller = Game.Instance?.UnitMovableAreaController;
            if (controller?.CurrentUnit != null && controller.CurrentUnitMovableArea?.Contains(node) == false)
                Append(sb, "unreachable");
        }

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

    /// <summary>Append every active ground hazard / buff zone (fire, gas, a psychic cloud) whose real runtime shape
    /// covers this tile, worded exactly as the area scanner reads them (name + "hazard"/"buff zone"). Sources the same
    /// placed-zone proxies from <see cref="RTAccess.Exploration.WorldModel"/>, so on-unit auras are already excluded
    /// and each zone's own fog visibility (<see cref="RTAccess.Exploration.ScanItem.IsVisible"/>) still gates it.</summary>
    private static void AppendZones(StringBuilder sb, CustomGridNodeBase node)
    {
        try
        {
            var pos = node.Vector3Position;
            foreach (var item in RTAccess.Exploration.WorldModel.Items)
            {
                if (!(item is RTAccess.Exploration.ProxyAreaEffect zone) || !zone.IsVisible || !zone.Contains(pos))
                    continue;
                var label = zone.Name;
                if (!string.IsNullOrWhiteSpace(zone.Detail)) label += ", " + zone.Detail;
                Append(sb, label);
            }
        }
        catch (Exception e) { Main.Log?.Error("DescribeTile hazard read failed: " + e); }
    }

    /// <summary>Append "half/full cover &lt;dir&gt;" (or "blocked &lt;dir&gt;" for sight-blocking) for one edge, read
    /// with the same <paramref name="checkType"/> the game's cover mesh uses (BySource on the acting unit).</summary>
    private static void AppendCover(StringBuilder sb, CustomGridNodeBase node, int direction, string word,
        LosCalculations.ForcedCoverCheckType checkType)
    {
        LosCalculations.CoverType cover;
        try { cover = LosCalculations.GetCellCoverStatus(node, direction, checkType).CoverType; }
        catch (Exception e) { Main.Log?.Error("DescribeTile cover read failed: " + e); return; }
        switch (cover)
        {
            case LosCalculations.CoverType.Half: Append(sb, "half cover " + word); break;
            case LosCalculations.CoverType.Full: Append(sb, "full cover " + word); break;
            case LosCalculations.CoverType.Invisible: Append(sb, "blocked " + word); break;
        }
    }

    /// <summary>Tile offset from the anchor's node, e.g. "5 east, 2 north"; "here" on the anchor's own tile.</summary>
    private static string RelativeTile(CustomGridNodeBase node, MechanicEntity anchor)
    {
        var origin = anchor?.CurrentUnwalkableNode;
        if (origin == null) return null;
        int dx = node.XCoordinateInGrid - origin.XCoordinateInGrid; // east(+) / west(-)
        int dz = node.ZCoordinateInGrid - origin.ZCoordinateInGrid; // north(+) / south(-)
        if (dx == 0 && dz == 0) return "here";
        var sb = new StringBuilder();
        if (dx > 0) sb.Append(dx).Append(" east");
        else if (dx < 0) sb.Append(-dx).Append(" west");
        if (dz != 0)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(dz > 0 ? dz + " north" : -dz + " south");
        }
        return sb.ToString();
    }

    // internal so ProxyMarker (the scanner's Exits/Poi landmark items) can slot the same type word after the name.
    internal static string MarkerTypeLabel(LocalMapMarkType type)
    {
        switch (type)
        {
            case LocalMapMarkType.Exit: return "exit";
            case LocalMapMarkType.DestinationMark: return "objective";
            case LocalMapMarkType.VeryImportantThing: return "important";
            case LocalMapMarkType.Loot: return "loot";
            case LocalMapMarkType.Poi: return "point of interest";
            case LocalMapMarkType.Unit: return "creature";
            default: return null;
        }
    }

    /// <summary>The name only (used for terse contexts); mirrors the type mapping in Describe. Public so the
    /// exploration scanner can reuse the same name + interaction resolution for its map-object proxies.</summary>
    public static string ResolveName(EntityViewBase entity, out InteractionPart interaction)
    {
        interaction = entity.Data != null ? entity.InteractionComponent : null;

        // Units (NPCs / enemies / crowd): CharacterName covers both BaseUnitEntity and LightweightUnitEntity
        // (both derive AbstractUnitEntity). The v1 BaseUnitEntity-only cast missed lightweight crowd and fell
        // back to the raw GameObject name ("BCT_...(Clone)").
        if (entity.Data is AbstractUnitEntity unit && !string.IsNullOrWhiteSpace(unit.CharacterName))
            return unit.CharacterName;

        var tips = Game.Instance?.BlueprintRoot?.LocalizedTexts?.UserInterfacesText?.Tooltips;
        switch (interaction)
        {
            case InteractionDoorPart:
                return tips?.Door?.Text ?? "Door";
            case InteractionLootPart loot:
                var lootName = loot.GetName();
                return string.IsNullOrWhiteSpace(lootName) ? "Container" : lootName;
            case InteractionStairsPart:
                return tips?.Ladder?.Text ?? "Stairs";
            case InteractionActionPart action:
                var actionName = action.Settings?.DisplayName?.String?.Text;
                return string.IsNullOrWhiteSpace(actionName) ? "Action" : actionName;
        }

        // Trap parts (several subtypes) — match by name so we don't bind every concrete type.
        if (interaction != null && interaction.GetType().Name.Contains("Trap"))
            return tips?.Trap?.Text ?? "Trap";

        // Last resort: the GameObject name (minus the Unity "(Clone)" suffix). Never return empty — an
        // unnamed interactable should still announce something rather than just "N tiles, <dir>".
        var fallback = Clean(entity.GameObjectName)?.Replace("(Clone)", "").Trim();
        return string.IsNullOrWhiteSpace(fallback) ? "Object" : fallback;
    }

    /// <summary>English verb for the interaction type; null when there is no meaningful verb. Public so the
    /// exploration scanner can reuse it for map-object detail lines.</summary>
    public static string Verb(InteractionPart interaction)
    {
        if (interaction == null) return null;
        switch (interaction.UIInteractionType)
        {
            case UIInteractionType.Action: return "activate";
            case UIInteractionType.Move: return "approach";
            case UIInteractionType.Info: return "examine";
            case UIInteractionType.Credits: return "collect";
            case UIInteractionType.Pets: return "interact";
            default: return null;
        }
    }

    /// <summary>Distance + map-relative compass between two world points, e.g. "6 tiles, north-east". Distance is
    /// reported in grid tiles (the game's own unit), not metres, so it matches the combat cell readouts and the tile
    /// explorer's offsets. Public so the exploration scanner speaks the same compass as the other navigators.</summary>
    public static string DirectionAndDistance(Vector3 from, Vector3 to)
    {
        float dx = to.x - from.x; // east(+) / west(-)
        float dz = to.z - from.z; // north(+) / south(-)
        float dist = Mathf.Sqrt(dx * dx + dz * dz);
        int tiles = Mathf.RoundToInt(dist / GraphParamsMechanicsCache.GridCellSize); // world metres -> 1.35 m grid cells
        var sb = new StringBuilder();
        sb.Append(tiles == 1 ? "1 tile" : tiles + " tiles");
        if (dist > 0.5f)
        {
            float angle = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg; // 0 = north, +90 = east
            int sector = ((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8;
            sb.Append(", ").Append(Compass8[sector]);
        }
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string part)
    {
        if (string.IsNullOrEmpty(part)) return;
        if (sb.Length > 0) sb.Append(", ");
        sb.Append(part);
    }

    private static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return Whitespace.Replace(RichText.Replace(raw, " "), " ").Trim();
    }
}
