using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kingmaker;
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
/// Builds the spoken description of a focused world interactable — e.g. "Door, approach, 6 metres, ahead" —
/// for the exploration navigator (<see cref="ExplorationEvents"/> / <see cref="ExplorationNav"/>).
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
    private static readonly string[] Compass8 =
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

    /// <summary>Spoken line for a local-map landmark from the player position, e.g. "Cargo hold, exit, 20 metres, north".</summary>
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

        // 1. Headline — what occupies the tile. Unit > interactable map object > generic obstacle; otherwise the
        //    walkability fills in (a bare unwalkable tile is a "wall", an empty walkable one is "clear").
        var unit = node.GetUnit();
        if (unit != null)
        {
            sb.Append(unit.CharacterName);
            Append(sb, unit.Faction != null && unit.Faction.IsPlayerEnemy ? "enemy" : "ally");
        }
        else if (TryNameMapObject(node, out var objectName, out var objectVerb))
        {
            sb.Append(objectName);
            if (objectVerb != null) Append(sb, objectVerb);
        }
        else if (DestructibleEntity.FindByNode(node) != null)
        {
            sb.Append("obstacle");
        }
        else
        {
            sb.Append(node.Walkable ? "clear" : "wall");
        }

        // 2. Cover on each cardinal edge (N/E/S/W = dirs 2/1/0/3 — the same source the game's CoverVisualizer reads).
        AppendCover(sb, node, 2, "north");
        AppendCover(sb, node, 1, "east");
        AppendCover(sb, node, 0, "south");
        AppendCover(sb, node, 3, "west");

        // 3. Offset from the anchor unit, in tiles (+Z = north, +X = east — matches the compass above).
        Append(sb, RelativeTile(node, anchor));
        return sb.ToString();
    }

    /// <summary>Name + verb of an interactable map object (door/loot/console) occupying this tile, if any.</summary>
    private static bool TryNameMapObject(CustomGridNodeBase node, out string name, out string verb)
    {
        name = null;
        verb = null;
        try
        {
            foreach (var mapObject in EntityBoundsHelper
                         .FindEntitiesInRange((Vector3)node.position, GraphParamsMechanicsCache.GridCellSize)
                         .OfType<MapObjectEntity>())
            {
                if (!mapObject.Interactions.Any()) continue;
                if (!mapObject.GetOccupiedNodes().Contains(node)) continue;
                name = ResolveName(mapObject.View, out var interaction);
                verb = Verb(interaction);
                if (!string.IsNullOrWhiteSpace(name)) return true;
            }
        }
        catch (Exception e) { Main.Log?.Error("DescribeTile map-object lookup failed: " + e); }
        return false;
    }

    /// <summary>Append "half/full cover &lt;dir&gt;" (or "blocked &lt;dir&gt;" for sight-blocking) for one edge.</summary>
    private static void AppendCover(StringBuilder sb, CustomGridNodeBase node, int direction, string word)
    {
        LosCalculations.CoverType cover;
        try { cover = LosCalculations.GetCellCoverStatus(node, direction).CoverType; }
        catch { return; }
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

    private static string MarkerTypeLabel(LocalMapMarkType type)
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

    /// <summary>The name only (used for terse contexts); mirrors the type mapping in Describe.</summary>
    private static string ResolveName(EntityViewBase entity, out InteractionPart interaction)
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
        // unnamed interactable should still announce something rather than just "N metres, <dir>".
        var fallback = Clean(entity.GameObjectName)?.Replace("(Clone)", "").Trim();
        return string.IsNullOrWhiteSpace(fallback) ? "Object" : fallback;
    }

    /// <summary>English verb for the interaction type; null when there is no meaningful verb.</summary>
    private static string Verb(InteractionPart interaction)
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

    /// <summary>Distance + map-relative compass between two world points, e.g. "8 metres, north-east".</summary>
    private static string DirectionAndDistance(Vector3 from, Vector3 to)
    {
        float dx = to.x - from.x; // east(+) / west(-)
        float dz = to.z - from.z; // north(+) / south(-)
        float dist = Mathf.Sqrt(dx * dx + dz * dz);
        int metres = Mathf.RoundToInt(dist);
        var sb = new StringBuilder();
        sb.Append(metres == 1 ? "1 metre" : metres + " metres");
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
