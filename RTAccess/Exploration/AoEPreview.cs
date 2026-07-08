using Kingmaker;                                          // Game
using Kingmaker.Blueprints;                               // PatternType
using Kingmaker.EntitySystem.Entities;                    // BaseUnitEntity, MechanicEntity
using Kingmaker.Pathfinding;                              // CustomGridNodeBase
using Kingmaker.UnitLogic;                                // HasMechanicFeature extension
using Kingmaker.UnitLogic.Abilities;                      // AbilityData, AbilityTargetUIData, GatherAffectedTargetsData
using Kingmaker.UnitLogic.Abilities.Components.Patterns;  // AoEPatternHelper, OrientedPatternData, AoEPattern
using Kingmaker.UnitLogic.Enums;                          // MechanicsFeatureType (HideRealHealthInUI)
using Kingmaker.Utility;                                  // TargetWrapper
using UnityEngine;                                        // Vector3, Mathf

namespace RTAccess.Exploration;

/// <summary>
/// The holographic AREA read for B3 v2: while an AoE / point ability is armed, this describes the template that would
/// land on the shared cursor tile — pattern shape + size, offset / range, affected-tile count, and the units caught
/// (flagging ALLIES in friendly fire) — the speech-only equivalent of the sighted red aim highlight. A pure read: it
/// asks the game for the exact same <see cref="OrientedPatternData"/> the commit uses, mutating nothing.
///
/// The one load-bearing invariant (see docs/plans/patterned-blasting-hamming.md §6): the caster is anchored at
/// <c>VirtualPositionController.GetDesiredPosition(caster)</c> — the SAME expression the commit
/// (<c>ClickWithSelectedAbilityHandler.OnClick</c>) resolves — NOT the raw <c>Caster.Position</c>, so the preview and
/// the cast agree even for predicted-caster-position abilities. Everything else takes the explicit cursor cell, never
/// the mouse-dead <c>GetDesiredPosition</c> AIM point. Rides the D4 unified cursor: <see cref="CursorTail"/> is called
/// from <see cref="RTAccess.Accessibility.TileExplorer"/>'s keypress-driven tile readout, so it is inherently pure and
/// only runs on a deliberate step (no per-frame tick).
/// </summary>
internal static class AoEPreview
{
    private static AbilityData Armed => Game.Instance?.SelectedAbilityHandler?.Ability;

    /// <summary>The arm-time shape headline ("Blast, 2-cell radius" / "Cone, length 6, 90 degrees") appended once by
    /// <see cref="Targeting"/>'s arm announce. Null when the armed ability is single-target (no AoE pattern).</summary>
    public static string ShapeLine(AbilityData ability)
    {
        try
        {
            var prov = ability?.GetPatternSettings();
            return prov == null ? null : Shape(ability, prov.Pattern);
        }
        catch (Exception e) { Main.Log?.Error("AoEPreview.ShapeLine failed: " + e); return null; }
    }

    /// <summary>The per-move tail for the cursor readout while an AoE ability is armed: shape, facing (directional
    /// patterns), offset / range, affected tiles, and caught units + friendly-fire warning. Returns null when the
    /// armed ability is single-target (v1 unit-target aim keeps its behavior) or nothing is armed.</summary>
    public static string CursorTail(CustomGridNodeBase cursorNode)
    {
        try
        {
            var ability = Armed;
            if (ability == null || cursorNode == null) return null;
            var prov = ability.GetPatternSettings();
            if (prov == null) return null;                          // single-target — no AoE tail
            var caster = ability.Caster as BaseUnitEntity;
            if (caster == null) return null;

            var pat = prov.Pattern;
            var sb = new System.Text.StringBuilder();
            sb.Append(Shape(ability, pat));
            if (pat == null) return sb.ToString();                  // whole-area / null-pattern: no per-cell math

            // Caster anchor = the SAME point the commit resolves (§6), NOT Caster.Position.
            var vpc = Game.Instance?.VirtualPositionController;
            Vector3 casterPos = vpc != null ? vpc.GetDesiredPosition(caster) : caster.Position;
            var casterNode = AoEPatternHelper.GetGridNode(casterPos);
            Vector3 cursorPos = cursorNode.Vector3Position;

            // Range + offset — the explicit-cell overload the reticle / HitPredictor use. Its bool is false for MANY
            // reasons (blocked LOS, firing arc, area overlap, group restriction, as well as range), so keep the
            // UnavailabilityReasonType for RangeWord to phrase correctly instead of mislabelling everything "out of range".
            var tw = new TargetWrapper(cursorPos);
            bool ok = ability.CanTargetFromNode(casterNode, null, tw, out int cells, out var _, out var reason);

            // The affected node set the commit would produce (pure; internally range-clamps just like the commit does).
            var pattern = ability.GetPattern(tw, casterPos);

            // Facing, for directional patterns only (a blast is radial — no facing).
            if (cells > 0 && IsDirectional(pat, ability))
            {
                var appNode = pattern.ApplicationNode;
                Vector3 aim = appNode != null ? appNode.Vector3Position : cursorPos;
                string dir = Facing(aim.x - casterPos.x, aim.z - casterPos.z);
                if (dir != null) sb.Append(", ").Append(Loc.T("aim.facing", new { dir }));
            }

            sb.Append(". ");
            sb.Append(cells == 0 ? Loc.T("aim.centre_here") : Loc.T("aim.centre_offset", new { cells }));
            sb.Append(", ").Append(RangeWord(ability, ok, reason, casterPos, tw));

            // Tile count, split into primary-impact vs splash cells the way the renderer shades them apart (#23).
            // PatternCellData marks direct-hit cells via MainCell; the ApplicationNode centre is the impact fallback.
            // Only scatter / line patterns carry the per-cell extra-data — GetPattern's radial patterns (blast / cone)
            // leave it null so MainCell is false there, and those fall through to the single total below.
            int tiles = 0, main = 0;
            var impactNode = pattern.ApplicationNode;
            foreach (var (node, cell) in pattern.NodesWithExtraData)
            {
                tiles++;
                if (cell.MainCell || (impactNode != null && node.CoordinatesInGrid == impactNode.CoordinatesInGrid)) main++;
            }
            if (tiles == 0) { sb.Append(". ").Append(Loc.T("aim.no_targets")); return sb.ToString(); }
            if (main > 1)
                sb.Append(". ").Append(Loc.T("aim.impact_split", new { main, splash = tiles - main }));
            else
                sb.Append(". ").Append(tiles == 1 ? Loc.T("aim.tile_one") : Loc.T("aim.tiles", new { count = tiles }));

            // The caught units, per-target odds, and friendly-fire warning now come from the game's OWN affected-target
            // list (AimRead reading AimReadTap), appended after this geometry by TileExplorer — not re-derived here.
            // This block keeps ONLY the pattern geometry (shape / facing / offset / range / tile count).
            return sb.ToString();
        }
        catch (Exception e) { Main.Log?.Error("AoEPreview.CursorTail failed: " + e); return null; }
    }

    /// <summary>The shape + size word. Branches on <see cref="AoEPattern.Type"/> FIRST and reads Radius/Angle only for
    /// the types that carry a blueprint (Custom dereferences a possibly-null blueprint → NRE, so it reads neither).
    /// A null pattern (all-area effect / non-pattern provider) reads as the whole-area line.</summary>
    private static string Shape(AbilityData ability, AoEPattern pat)
    {
        if (pat == null) return Loc.T("aim.whole_area");
        if (pat.Type == PatternType.Custom) return Loc.T("aim.shape_special");
        if (ability.IsScatter) return Loc.T("aim.shape_scatter", new { radius = pat.Radius });
        switch (pat.Type)
        {
            case PatternType.Circle: return Loc.T("aim.shape_blast", new { radius = pat.Radius });
            case PatternType.Cone: return Loc.T("aim.shape_cone", new { radius = pat.Radius, angle = pat.Angle });
            case PatternType.Ray: return Loc.T("aim.shape_line", new { radius = pat.Radius });
            case PatternType.Sector: return Loc.T("aim.shape_sector", new { radius = pat.Radius, angle = pat.Angle });
            default: return Loc.T("aim.shape_special");
        }
    }

    /// <summary>The legality word for the aim point. <c>CanTargetFromNode</c>'s bool is false for a whole family of
    /// reasons — blocked line of sight, firing arc, area overlap, group restriction — not just range, so a bare false
    /// must NOT read as "out of range" (that would mislabel an in-range LOS block and contradict the real refusal the
    /// player hears if they fire). Only genuine range failures get the range word; any other refusal speaks the game's
    /// OWN already-localized reason string (rich text stripped) — the same one the commit would raise.</summary>
    private static string RangeWord(AbilityData ability, bool ok, AbilityData.UnavailabilityReasonType? reason, Vector3 casterPos, TargetWrapper target)
    {
        if (ok) return Loc.T("aim.in_range");
        var r = reason ?? AbilityData.UnavailabilityReasonType.None;
        if (r == AbilityData.UnavailabilityReasonType.None
            || r == AbilityData.UnavailabilityReasonType.TargetTooFar
            || r == AbilityData.UnavailabilityReasonType.TargetTooClose)
            return Loc.T("aim.out_of_range");
        try
        {
            var s = TextUtil.StripRichText(ability.GetUnavailabilityReasonString(r, casterPos, target));
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        catch (Exception e) { Main.Log?.Log("AoEPreview reason string failed: " + e.Message); }
        return Loc.T("aim.out_of_range");
    }

    private static bool IsDirectional(AoEPattern pat, AbilityData ability)
        => ability.IsScatter || pat.Type == PatternType.Cone || pat.Type == PatternType.Ray || pat.Type == PatternType.Sector;

    /// <summary>8-way compass word from an (east=+X, north=+Z) delta; null when the delta is ~zero (cursor on the
    /// caster). Sector math is shared via <see cref="Geo.CompassSector"/>; the words are the same localized
    /// compass keys the focused readout and the system map speak.</summary>
    private static string Facing(float east, float north)
    {
        if (!Geo.CompassSector(east, north, out int sector, 0.1f)) return null;   // ~0.01 m² too-close threshold, preserved
        return Loc.T(Accessibility.InteractableDescriber.Compass8[sector]);
    }
}
