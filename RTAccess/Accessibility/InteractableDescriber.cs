using System.Linq;
using System.Text;
using Kingmaker;
using Kingmaker.Controllers.Clicks.Handlers; // ClickMapObjectHandler.HasAvailableInteractions (the game's own gate)
using Kingmaker.Controllers.Optimization;
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
            sb.Append(unit.CharacterName);
            Append(sb, Loc.T(unit.Faction != null && unit.Faction.IsPlayerEnemy ? "scan.faction.enemy" : "scan.faction.party"));
            // The tile cursor sits ON the tile, so a corpse must be READ (not hidden) — but tagged so it doesn't read
            // as a live enemy. GetUnit() returns corpses (they stay in the grid's awake set until destroyed), and the
            // scanner cycles now skip the dead, so the tile cursor is the one place a corpse is still announced.
            if (unit.LifeState.IsDead) Append(sb, Loc.T("unit.dead"));
            else if (!unit.LifeState.IsConscious) Append(sb, Loc.T("unit.unconscious"));
        }
        if (seen && TryNameMapObject(node, out var objectName, out var objectVerb))
        {
            Append(sb, objectName);
            if (objectVerb != null) Append(sb, objectVerb);
        }
        else if (seen && unit == null)
        {
            if (DestructibleEntity.FindByNode(node) != null) sb.Append(Loc.T("tile.obstacle"));
            else sb.Append(Loc.T(node.Walkable ? "tile.clear" : "tile.wall"));
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
        bool deployment = turn != null && turn.IsPreparationTurn && turn.IsDeploymentAllowed;
        bool abilityArmed = Game.Instance?.CursorController?.SelectedAbility != null;   // mesh hides cover while aiming
        bool coverShown = seen && node.Walkable && turn != null && turn.TurnBasedModeActive
            && (deployment || (turn.IsPlayerTurn && !abilityArmed));
        if (coverShown)
        {
            var checkType = Game.Instance?.SelectionCharacter?.SelectedUnit?.Value != null
                ? LosCalculations.ForcedCoverCheckType.BySource
                : LosCalculations.ForcedCoverCheckType.ByTarget;
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
            case LosCalculations.CoverType.Half: Append(sb, Loc.T("cover.half") + " " + dir); break;
            case LosCalculations.CoverType.Full: Append(sb, Loc.T("cover.full") + " " + dir); break;
            case LosCalculations.CoverType.Invisible: Append(sb, Loc.T("cover.blocked") + " " + dir); break;
        }
    }

    /// <summary>Tile offset from the anchor's node, e.g. "5 east, 2 north"; "here" on the anchor's own tile.</summary>
    private static string RelativeTile(CustomGridNodeBase node, MechanicEntity anchor)
    {
        var origin = anchor?.CurrentUnwalkableNode;
        if (origin == null) return null;
        int dx = node.XCoordinateInGrid - origin.XCoordinateInGrid; // east(+) / west(-)
        int dz = node.ZCoordinateInGrid - origin.ZCoordinateInGrid; // north(+) / south(-)
        if (dx == 0 && dz == 0) return Loc.T("geo.here");
        var sb = new StringBuilder();
        if (dx > 0) sb.Append(dx).Append(' ').Append(Loc.T("aim.dir_e"));
        else if (dx < 0) sb.Append(-dx).Append(' ').Append(Loc.T("aim.dir_w"));
        if (dz != 0)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(dz > 0 ? dz : -dz).Append(' ').Append(Loc.T(dz > 0 ? "aim.dir_n" : "aim.dir_s"));
        }
        return sb.ToString();
    }

    // internal so ProxyMarker (the scanner's Exits/Poi landmark items) can slot the same type word after the name.
    internal static string MarkerTypeLabel(LocalMapMarkType type)
    {
        switch (type)
        {
            case LocalMapMarkType.Exit: return Loc.T("marker.exit");
            case LocalMapMarkType.DestinationMark: return Loc.T("marker.objective");
            case LocalMapMarkType.VeryImportantThing: return Loc.T("marker.important");
            case LocalMapMarkType.Loot: return Loc.T("marker.loot");
            case LocalMapMarkType.Poi: return Loc.T("marker.poi");
            case LocalMapMarkType.Unit: return Loc.T("marker.creature");
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
                return tips?.Door?.Text ?? Loc.T("scan.singular.door");
            case InteractionLootPart loot:
                var lootName = loot.GetName();
                return string.IsNullOrWhiteSpace(lootName) ? Loc.T("scan.singular.container") : lootName;
            case InteractionStairsPart:
                return tips?.Ladder?.Text ?? Loc.T("scan.singular.stairs");
            case InteractionActionPart action:
                var actionName = action.Settings?.DisplayName?.String?.Text;
                return string.IsNullOrWhiteSpace(actionName) ? Loc.T("scan.singular.action") : actionName;
            // Mirror the overtip (OvertipMapObjectVM): the designer's DisplayName while live, and the
            // DisplayNameAfterUse swap once a check-once interaction is spent — the game's own "already
            // examined" cue. No designer name → the localized category singular (the raw GameObject name
            // the old fallback produced is dev-string junk for these locators).
            case InteractionSkillCheckPart check:
                var used = check.AlreadyUsed && check.Settings?.OnlyCheckOnce == true;
                var checkName = Clean((used ? check.Settings?.DisplayNameAfterUse : check.Settings?.DisplayName)?.String?.Text);
                return string.IsNullOrWhiteSpace(checkName) ? Loc.T("scan.singular.search_point") : checkName;
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

        // Last resort: the GameObject name (minus the Unity "(Clone)" suffix). Never return empty — an
        // unnamed interactable should still announce something rather than just "N tiles, <dir>".
        var fallback = Clean(entity.GameObjectName)?.Replace("(Clone)", "").Trim();
        return string.IsNullOrWhiteSpace(fallback) ? Loc.T("scan.singular.object") : fallback;
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
                        return Clean((check.CheckPassed ? settings.ShortDescriptionPassed : settings.ShortDescriptionFailed)?.String?.Text);
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
    /// explorer's offsets. Public so the exploration scanner speaks the same compass as the other navigators.</summary>
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
        return sb.ToString();
    }

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
