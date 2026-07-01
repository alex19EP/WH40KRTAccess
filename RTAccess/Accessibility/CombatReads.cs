using Kingmaker;
using Kingmaker.EntitySystem.Entities;                   // BaseUnitEntity, MechanicEntity
using Kingmaker.Pathfinding;                              // CustomGridNodeBase
using Kingmaker.UnitLogic;                                // IsThreat (AttackOfOpportunityHelper ext)
using Kingmaker.UnitLogic.Abilities;                      // AbilityData
using Kingmaker.UnitLogic.Abilities.Components.Patterns;  // AoEPatternHelper.GetGridNode
using Kingmaker.Utility;                                  // TargetWrapper
using Kingmaker.View.Covers;                              // LosCalculations
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Side-effect-free combat reads shared by the battlefield scanner's per-enemy suffix (C5) and its summary key —
/// the passive counterpart of the aiming-time <see cref="HitPredictor"/>. These are the same facts a sighted player
/// reads off the on-screen cover overtip and threatened-area FX (do NOT read the overtip VM — it is stale for
/// off-screen / non-current units, so the cover is recomputed here exactly as the game's own <c>UpdateCover</c>
/// does: best shooting position from the acting unit's desired/move-preview tile → LOS/cover to the target). No
/// state is mutated; the numbers agree with the reticle after a partial move plan because both use
/// <c>VirtualPositionController.GetDesiredPosition</c>.
/// </summary>
internal static class CombatReads
{
    /// <summary>The observer's primary-weapon attack ability (null if unarmed) — used only for a dry "in range?" +
    /// cover read via <see cref="AbilityData.CanTargetFromNode"/>. NOT the attack-of-opportunity ability (that
    /// returns a <c>BlueprintAbility</c>, which <c>CanTargetFromNode</c> cannot take).</summary>
    public static AbilityData DefaultAttack(BaseUnitEntity u)
    {
        var abilities = u?.GetFirstWeapon()?.Abilities;
        return (abilities != null && abilities.Count > 0) ? abilities[0].Data : null;
    }

    /// <summary>The node the unit would SHOOT from — its desired (move-preview) position, matching the reticle/overtip.</summary>
    public static CustomGridNodeBase ShootNode(BaseUnitEntity u)
    {
        if (u == null) return null;
        var vpc = Game.Instance?.VirtualPositionController;
        Vector3 p = vpc != null ? vpc.GetDesiredPosition(u) : u.Position;
        return AoEPatternHelper.GetGridNode(p);
    }

    /// <summary>Is <paramref name="target"/> in range of <paramref name="me"/>'s default weapon attack right now
    /// (from where I'd shoot)? False when unarmed or not targetable. A pure read — the same check the reticle runs.</summary>
    public static bool InRange(BaseUnitEntity me, BaseUnitEntity target)
    {
        var atk = DefaultAttack(me);
        var node = ShootNode(me);
        if (atk == null || node == null || target == null) return false;
        return atk.CanTargetFromNode(node, null, new TargetWrapper(target), out int _, out var _, out var _);
    }

    /// <summary>The cover the target has against me — computed exactly as the game's on-screen cover overtip does
    /// (<c>OvertipCoverBlockVM.UpdateCover</c>): the best shooting position from my desired/move-preview tile, then
    /// the Warhammer LOS/cover to the target. Returns <c>Invisible</c> when I have no line of sight. NOTE:
    /// <c>AbilityData.CanTargetFromNode</c>'s out-cover is unusable — the game hardcodes it to <c>None</c> and tests
    /// LOS with a separate bool — so cover MUST be computed here, not read from that call.</summary>
    public static LosCalculations.CoverType CoverTo(BaseUnitEntity me, BaseUnitEntity target)
    {
        if (me == null || target == null) return LosCalculations.CoverType.None;
        var vpc = Game.Instance?.VirtualPositionController;
        Vector3 from = vpc != null ? vpc.GetDesiredPosition(me) : me.Position;
        Vector3 best = LosCalculations.GetBestShootingPosition(from, me.SizeRect, target.Position, target.SizeRect);
        return LosCalculations.GetWarhammerLos(best, me.SizeRect, target).CoverType;
    }

    /// <summary>The passive tactical tail for ME considering TARGET — "half cover, in range, threatening you" — the
    /// not-aiming counterpart of the <see cref="HitPredictor"/> line. Returns null when nothing applies.</summary>
    public static string CoverRangeThreat(BaseUnitEntity me, BaseUnitEntity target)
    {
        if (me == null || target == null || me == target) return null;
        var bits = new List<string>();

        var cover = CoverTo(me, target);
        if (cover == LosCalculations.CoverType.Invisible)
        {
            bits.Add("no line of sight");
        }
        else
        {
            if (cover == LosCalculations.CoverType.Half) bits.Add("half cover");
            else if (cover == LosCalculations.CoverType.Full) bits.Add("full cover");

            // With LOS established, whether my default weapon can actually reach the target (range/targetability).
            // Only spoken when I'm armed — an unarmed observer has no weapon range to report.
            var atk = DefaultAttack(me);
            var node = ShootNode(me);
            if (atk != null && node != null)
            {
                bool targetable = atk.CanTargetFromNode(node, null, new TargetWrapper(target), out int _, out var _, out var _);
                bits.Add(targetable ? "in range" : "out of range");
            }
        }
        if (target.IsThreat(me)) bits.Add("threatening you");   // does this enemy threaten my acting unit (AoO reach)

        return bits.Count > 0 ? string.Join(", ", bits) : null;
    }
}
