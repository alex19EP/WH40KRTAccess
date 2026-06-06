using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.View;
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

    private static readonly string[] Bearings =
        { "ahead", "ahead-right", "right", "behind-right", "behind", "behind-left", "left", "ahead-left" };

    /// <summary>Full spoken line for a chosen interactable view. Never throws; returns "" if nothing readable.</summary>
    public static string Describe(EntityViewBase entity)
    {
        if (entity == null) return string.Empty;

        var sb = new StringBuilder();
        var name = ResolveName(entity, out var interaction);
        if (!string.IsNullOrWhiteSpace(name)) sb.Append(name);

        var verb = Verb(interaction);
        if (verb != null) Append(sb, verb);

        // Distance + bearing relative to the active character + camera (skipped if either is unavailable).
        var self = Game.Instance?.SelectionCharacter?.SelectedUnit?.Value;
        if (self != null && entity.Data != null)
        {
            Vector3 from = self.Position, to = entity.Data.Position;
            var planar = new Vector2(to.x - from.x, to.z - from.z);
            int metres = Mathf.RoundToInt(planar.magnitude);
            Append(sb, metres == 1 ? "1 metre" : metres + " metres");
            var bearing = Bearing(planar);
            if (bearing != null) Append(sb, bearing);
        }

        return sb.ToString();
    }

    /// <summary>The name only (used for terse contexts); mirrors the type mapping in Describe.</summary>
    private static string ResolveName(EntityViewBase entity, out InteractionPart interaction)
    {
        interaction = entity.Data != null ? entity.InteractionComponent : null;

        // Units (NPCs / enemies): the character name is the clearest label.
        var unitView = entity as UnitEntityView ?? entity.GetComponent<UnitEntityView>();
        if (unitView != null && unitView.Data != null) return unitView.Data.CharacterName;
        if (entity.Data is BaseUnitEntity unit) return unit.CharacterName;

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

        return Clean(entity.GameObjectName);
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

    /// <summary>Camera-relative 8-way bearing of a planar (X,Z) offset, or null if the camera isn't ready.</summary>
    private static string Bearing(Vector2 planar)
    {
        if (planar.sqrMagnitude < 0.0001f) return "here";
        var cam = CameraRig.Instance?.Camera;
        if (cam == null) return null;

        var f = cam.transform.forward;
        var r = cam.transform.right;
        var fwd = new Vector2(f.x, f.z);
        var right = new Vector2(r.x, r.z);
        if (fwd.sqrMagnitude < 0.0001f || right.sqrMagnitude < 0.0001f) return null;
        fwd.Normalize();
        right.Normalize();

        var dir = planar.normalized;
        float along = Vector2.Dot(dir, fwd);   // +ahead / -behind
        float side = Vector2.Dot(dir, right);  // +right / -left
        float angle = Mathf.Atan2(side, along) * Mathf.Rad2Deg; // 0 = ahead, +90 = right
        int sector = Mathf.RoundToInt(angle / 45f);
        sector = ((sector % 8) + 8) % 8;
        return Bearings[sector];
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
