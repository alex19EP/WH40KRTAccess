using System.Text;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UI.SurfaceCombatHUD;               // AbilityTargetUIDataCache
using Kingmaker.UnitLogic.Abilities;               // AbilityData, AbilityTargetUIData, UnavailabilityReasonType
using Kingmaker.UnitLogic.Abilities.Components.Patterns; // AoEPatternHelper
using Kingmaker.Utility;                            // TargetWrapper
using Kingmaker.View.Covers;                        // LosCalculations
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Hit prediction (B4). Computes exactly the numbers a sighted player reads off the targeting reticle when they
/// hover an ability over a target — the on-screen hit% (after the target's dodge/parry/cover avoidance), the crit
/// chance, and (verbose) the damage range and the individual avoidance components. This is a PARITY service: it
/// reads the same <see cref="AbilityTargetUIData"/> the game's own <c>LineOfSightVM.UpdateHitChance</c> shows in the
/// reticle, so it never reveals information a sighted player doesn't already have.
///
/// All of it is a DRY read — no command is issued, nothing is committed, no game state changes. The correctness trap
/// (see the combat plan §invariant) is that hit/range must be evaluated from where the caster will actually SHOOT
/// from, not its current tile: the game lets a unit reposition within its remaining movement before firing. So we
/// mirror the reticle exactly — resolve the caster's desired position, then the ability's best shooting position for
/// this target — before reading the cache. Getting this wrong reads a different number than the screen shows.
/// </summary>
public static class HitPredictor
{
    /// <summary>
    /// One spoken line for aiming <paramref name="ability"/> (cast by <paramref name="caster"/>) at
    /// <paramref name="target"/>. Returns null on bad input (caller stays silent); returns a short reason string
    /// ("Out of range, 2 cells too far." / "No line of sight.") when the target can't be hit from here, so the
    /// player learns WHY the reticle is red. On a valid shot: terse = "<hit>% to hit[, <crit>% crit][, cover]";
    /// verbose additionally breaks out base hit, each avoidance component that applies, the damage range, and
    /// per-shot chances for bursts.
    /// </summary>
    public static string Describe(BaseUnitEntity caster, AbilityData ability, BaseUnitEntity target, bool verbose = false)
    {
        if (caster == null || ability == null || target == null) return null;
        try
        {
            // Evaluate from where the shot will actually be taken — the reticle's own reference frame.
            var vpc = Game.Instance?.VirtualPositionController;
            Vector3 casterPos = vpc != null ? vpc.GetDesiredPosition(caster) : caster.Position;
            var casterNode = AoEPatternHelper.GetGridNode(casterPos);
            var tw = new TargetWrapper(target);

            if (!ability.CanTargetFromNode(casterNode, null, tw, out int distance, out var _, out var reason))
                return Untargetable(ability, distance, reason);

            // The ability may pick a better firing node than the desired position (e.g. to clear cover / gain LOS);
            // read the hit cache keyed on THAT node so the number matches the reticle.
            var shootNode = ability.GetBestShootingPositionForDesiredPosition(tw);
            Vector3 shootPos = shootNode != null ? shootNode.Vector3Position : casterPos;

            var cache = AbilityTargetUIDataCache.Instance;
            if (cache == null) return null;
            var ui = cache.GetOrCreate(ability, target, shootPos);

            // Cover for the terse line, computed from the firing position (CanTargetFromNode's out-cover is
            // hardcoded to None by the game — it tests LOS with a separate bool — so read it here, as the overtip does).
            var cover = LosCalculations.GetWarhammerLos(shootPos, caster.SizeRect, target).CoverType;

            int crit = CritChance(caster, target, ability, shootPos);
            return Compose(ui, crit, cover, verbose);
        }
        catch (Exception e) { Main.Log?.Log("hit predict failed: " + e.Message); return null; }
    }

    // Crit (Righteous Fury) chance isn't in AbilityTargetUIData — trigger the same dry-run rule the game uses.
    private static int CritChance(BaseUnitEntity caster, BaseUnitEntity target, AbilityData ability, Vector3 shootPos)
    {
        try
        {
            var rule = new RuleCalculateHitChances(caster, target, ability, 0, shootPos, target.Position);
            Rulebook.Trigger(rule);
            return rule.ResultRighteousFuryChance;
        }
        catch { return 0; }
    }

    private static string Untargetable(AbilityData ability, int distance, AbilityData.UnavailabilityReasonType? reason)
    {
        switch (reason)
        {
            case AbilityData.UnavailabilityReasonType.TargetTooFar:
                int over = distance - ability.RangeCells;
                return over > 0 ? $"Out of range, {over} {Cells(over)} too far." : "Out of range.";
            case AbilityData.UnavailabilityReasonType.TargetTooClose:
                int under = ability.MinRangeCells - distance;
                return under > 0 ? $"Too close, {under} {Cells(under)} closer than the minimum range." : "Too close.";
            case AbilityData.UnavailabilityReasonType.HasNoLosToTarget:
                return "No line of sight.";
            default:
                return "Can't target that.";
        }
    }

    private static string Compose(AbilityTargetUIData ui, int crit, LosCalculations.CoverType los, bool verbose)
    {
        int hit = Mathf.RoundToInt(ui.HitWithAvoidanceChance);
        var sb = new StringBuilder();
        sb.Append(hit).Append("% to hit");
        if (crit > 0) sb.Append(", ").Append(crit).Append("% crit");

        if (!verbose)
        {
            // Terse: the reticle number plus cover, since cover is the one avoidance a player positions around.
            AppendCover(sb, los);
            return sb.Append('.').ToString();
        }

        // Verbose: the full overtip breakdown — base roll, then each avoidance that actually applies, then damage.
        sb.Append(". Base ").Append(Mathf.RoundToInt(ui.InitialHitChance)).Append('%');
        AppendPct(sb, "dodged", ui.DodgeChance);
        AppendPct(sb, "parried", ui.ParryChance);
        AppendPct(sb, "in cover", ui.CoverChance);
        AppendPct(sb, "blocked", ui.BlockChance);
        AppendPct(sb, "evaded", ui.EvasionChance);
        if (ui.MaxDamage > 0)
            sb.Append(". ").Append(ui.MinDamage).Append(" to ").Append(ui.MaxDamage).Append(" damage");

        // Bursts: per-shot hit chances (the reticle shows a column of these for multi-shot weapons).
        var shots = ui.BurstHitChances;
        if (shots != null && shots.Count > 1)
        {
            sb.Append(". ").Append(shots.Count).Append(" shots: ");
            for (int i = 0; i < shots.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Mathf.RoundToInt(shots[i])).Append('%');
            }
        }
        return sb.Append('.').ToString();
    }

    private static void AppendCover(StringBuilder sb, LosCalculations.CoverType los)
    {
        if (los == LosCalculations.CoverType.Half) sb.Append(", half cover");
        else if (los == LosCalculations.CoverType.Full) sb.Append(", full cover");
    }

    private static void AppendPct(StringBuilder sb, string label, float pct)
    {
        int p = Mathf.RoundToInt(pct);
        if (p > 0) sb.Append(", ").Append(p).Append("% ").Append(label);
    }

    private static string Cells(int n) => n == 1 ? "cell" : "cells";

#if DEBUG
    /// <summary>Dev parity check: describe the acting unit's longest-range enemy ability against the nearest visible
    /// enemy, alongside the raw cache number, so /eval can confirm our line matches the game's reticle.
    /// Call: <c>RTAccess.Accessibility.HitPredictor.DevPredictNearestEnemy(true)</c>.</summary>
    public static string DevPredictNearestEnemy(bool verbose = false)
    {
        var unit = RTAccess.Combat.CommandDispatch.ActingUnit(speak: false);
        if (unit == null) return "no acting unit (need player TB turn with the active character selected)";

        AbilityData best = null;
        int bestRange = -1;
        foreach (var ab in unit.Abilities.RawFacts)
        {
            var d = ab.Data;
            if (d == null || !d.CanTargetEnemies) continue;
            if (d.RangeCells > bestRange) { bestRange = d.RangeCells; best = d; }
        }
        if (best == null) return "no enemy-target ability";

        BaseUnitEntity nearest = null;
        float bestDist = float.MaxValue;
        foreach (var o in (System.Collections.IEnumerable)Game.Instance.State.AllBaseAwakeUnits)
        {
            if (!(o is BaseUnitEntity u) || !u.IsInCombat || !u.IsPlayerEnemy || !u.IsVisibleForPlayer) continue;
            float dist = (u.Position - unit.Position).sqrMagnitude;
            if (dist < bestDist) { bestDist = dist; nearest = u; }
        }
        if (nearest == null) return "no visible enemy";

        string line = Describe(unit, best, nearest, verbose);
        return $"[{best.Name} -> {nearest.CharacterName}] {line}";
    }
#endif
}
