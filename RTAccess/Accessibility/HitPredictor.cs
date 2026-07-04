using System.Text;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UI.Common;                         // UIUtilityUnit.HasEquipedShield
using Kingmaker.UI.SurfaceCombatHUD;               // AbilityTargetUIDataCache
using Kingmaker.UnitLogic;                          // UnitPredictionManager, HasMechanicFeature extension
using Kingmaker.UnitLogic.Abilities;               // AbilityData, AbilityTargetUIData, UnavailabilityReasonType
using Kingmaker.UnitLogic.Abilities.Components.Patterns; // AoEPatternHelper, OrientedPatternData
using Kingmaker.UnitLogic.Enums;                    // MechanicsFeatureType.HideRealHealthInUI
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

            // #5 Ricochet: the game floats "???" over a ricochet target — its hit chance AND damage are
            // indeterminate (AbilityTargetUIData bails out to zeros for a ricochet, mirrored by the overtip
            // zeroing MinDamage/MaxDamage), so there is no honest number to quote. Say so instead of a percentage.
            if (UnitPredictionManager.Instance?.IsUnitRicochetTarget(target) ?? false)
                return Loc.T("predict.ricochet_unknown");

            var ui = cache.GetOrCreate(ability, target, shootPos);

            // Cover for the terse line, computed from the firing position (CanTargetFromNode's out-cover is
            // hardcoded to None by the game — it tests LOS with a separate bool — so read it here, as the overtip does).
            var cover = LosCalculations.GetWarhammerLos(shootPos, caster.SizeRect, target).CoverType;

            int crit = CritChance(caster, target, ability, shootPos);

            // Parity flags a sighted player reads off the overtip. Fog gate (RULE 2): an enemy's state may only be
            // read while it is visible; party units (IsPlayerFaction) are always visible to their own player.
            bool visible = target.IsPlayerFaction || target.IsVisibleForPlayer;
            // #2 death mark — mirror OvertipHitChanceBlockVM.CanDie (MaxDamage >= HitPointsLeft + TemporaryHitPoints).
            // Suppress for a concealed-HP unit so we never leak lethality the overtip itself hides.
            bool canKill = visible && ui.MaxDamage > 0
                && !target.HasMechanicFeature(MechanicsFeatureType.HideRealHealthInUI)
                && ui.MaxDamage >= target.Health.HitPointsLeft + target.Health.TemporaryHitPoints;
            // R1 knockback — mirror OvertipHitChanceBlockVM.CanPush (= AbilityTargetUIData.CanPush). No HP → no mask.
            bool canPush = visible && ui.CanPush;
            // #16 shield indicator — the defenses overtip shows a shield icon whenever one is equipped, independent
            // of block%; HasEquipedShield is the same enemy-inventory check the block-chance rule uses.
            bool shielded = visible && UIUtilityUnit.HasEquipedShield(target);
            // #3 overpenetration tail (verbose only) — reduced hit/damage for each pierced in-line unit.
            string chain = verbose ? OverpenChain(caster, ability, target, casterPos) : null;

            return Compose(ui, crit, cover, verbose, canKill, canPush, shielded, chain);
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
                return over > 0 ? Loc.T("predict.too_far", new { over, cells = Cells(over) }) : Loc.T("predict.out_of_range");
            case AbilityData.UnavailabilityReasonType.TargetTooClose:
                int under = ability.MinRangeCells - distance;
                return under > 0 ? Loc.T("predict.too_close_by", new { under, cells = Cells(under) }) : Loc.T("predict.too_close");
            case AbilityData.UnavailabilityReasonType.HasNoLosToTarget:
                return Loc.T("predict.no_los");
            default:
                return Loc.T("predict.cant_target");
        }
    }

    private static string Compose(AbilityTargetUIData ui, int crit, LosCalculations.CoverType los, bool verbose,
        bool canKill, bool canPush, bool shielded, string chain)
    {
        int hit = Mathf.RoundToInt(ui.HitWithAvoidanceChance);
        var sb = new StringBuilder();
        sb.Append(Loc.T("predict.to_hit", new { hit }));
        if (crit > 0) sb.Append(", ").Append(Loc.T("predict.crit", new { crit }));

        if (!verbose)
        {
            // Terse: the reticle number, cover (the one avoidance a player positions around), and the two
            // one-word overtip flags — can-kill and can-knock-back — that change the whole shot decision.
            AppendCover(sb, los);
            AppendFlags(sb, canKill, canPush);
            return sb.Append('.').ToString();
        }

        // Verbose: the full overtip breakdown — base roll, then each avoidance that actually applies, then damage.
        sb.Append(". ").Append(Loc.T("predict.base", new { value = Mathf.RoundToInt(ui.InitialHitChance) }));
        AppendPct(sb, "predict.dodged", ui.DodgeChance);
        AppendPct(sb, "predict.parried", ui.ParryChance);
        AppendPct(sb, "predict.in_cover", ui.CoverChance);
        AppendPct(sb, "predict.blocked", ui.BlockChance);
        // #16 The overtip keeps the shield icon lit even at 0% block; if the block clause rounded away, note it.
        if (shielded && Mathf.RoundToInt(ui.BlockChance) == 0)
            sb.Append(", ").Append(Loc.T("predict.shielded"));
        AppendPct(sb, "predict.evaded", ui.EvasionChance);
        if (ui.MaxDamage > 0)
            sb.Append(". ").Append(Loc.T("predict.damage", new { min = ui.MinDamage, max = ui.MaxDamage }));
        AppendFlags(sb, canKill, canPush);

        // Bursts: per-shot hit chances (the reticle shows a column of these for multi-shot weapons).
        var shots = ui.BurstHitChances;
        if (shots != null && shots.Count > 1)
        {
            var parts = new string[shots.Count];
            for (int i = 0; i < shots.Count; i++) parts[i] = Mathf.RoundToInt(shots[i]) + "%";
            sb.Append(". ").Append(Loc.T("predict.shots", new { count = shots.Count, list = string.Join(", ", parts) }));
        }
        if (chain != null) sb.Append(". ").Append(chain);
        return sb.Append('.').ToString();
    }

    // #3 Overpenetration chain: a single-shot ranged attack pierces the in-line targets behind the first, each
    // hit at reduced hit%/damage. We mirror AbilitySingleTargetRange exactly — take the line of affected nodes
    // (GetSingleShotAffectedNodes), then let the game's own GatherAffectedTargetsData thread OverpenetrationUIData
    // through them so each returned AbilityTargetUIData already carries the reduced numbers. We only NAME the
    // pierced units (the primary target is the main readout); each is fog-gated and its kill mark honors the
    // concealed-HP mask, same as the primary. Returns null for non-single-shot abilities or an empty line.
    private static string OverpenChain(BaseUnitEntity caster, AbilityData ability, BaseUnitEntity target, Vector3 casterPos)
    {
        if (!ability.IsSingleShot) return null;
        try
        {
            var tw = new TargetWrapper(target);
            var nodes = ability.GetSingleShotAffectedNodes(tw);
            var pattern = new OrientedPatternData(nodes, nodes.FirstOrDefault());
            var list = new List<AbilityTargetUIData>();
            ability.GatherAffectedTargetsData(pattern, casterPos, tw, in list);

            var parts = new List<string>();
            foreach (var d in list)
            {
                // Only real units behind the primary target; skip the primary (already spoken) and destructibles.
                if (!(d.Target is BaseUnitEntity pierced) || pierced == target) continue;
                if (!(pierced.IsPlayerFaction || pierced.IsVisibleForPlayer)) continue; // fog parity
                int h = Mathf.RoundToInt(d.HitWithAvoidanceChance);
                bool kill = d.MaxDamage > 0
                    && !pierced.HasMechanicFeature(MechanicsFeatureType.HideRealHealthInUI)
                    && d.MaxDamage >= pierced.Health.HitPointsLeft + pierced.Health.TemporaryHitPoints;
                parts.Add(Loc.T(kill ? "predict.pierce_kill" : "predict.pierce",
                    new { name = pierced.CharacterName, hit = h, min = d.MinDamage, max = d.MaxDamage }));
            }
            if (parts.Count == 0) return null;
            return Loc.T("predict.pierces", new { count = parts.Count, list = string.Join("; ", parts) });
        }
        catch { return null; }
    }

    private static void AppendCover(StringBuilder sb, LosCalculations.CoverType los)
    {
        if (los == LosCalculations.CoverType.Half) sb.Append(", ").Append(Loc.T("cover.half"));
        else if (los == LosCalculations.CoverType.Full) sb.Append(", ").Append(Loc.T("cover.full"));
        // A LOS-ignoring / indirect ability targets past a sight-blocker: CanTargetFromNode succeeds (so we never hit
        // the no_los early-return), but the target is Invisible. The overtip lights its blocked icon here, so say it.
        else if (los == LosCalculations.CoverType.Invisible) sb.Append(", ").Append(Loc.T("combat.no_los"));
    }

    private static void AppendPct(StringBuilder sb, string key, float pct)
    {
        int p = Mathf.RoundToInt(pct);
        if (p > 0) sb.Append(", ").Append(Loc.T(key, new { pct = p }));
    }

    // The overtip's two decision-changing flags: this shot can drop the target, / can knock it back.
    private static void AppendFlags(StringBuilder sb, bool canKill, bool canPush)
    {
        if (canKill) sb.Append(", ").Append(Loc.T("predict.can_kill"));
        if (canPush) sb.Append(", ").Append(Loc.T("predict.can_push"));
    }

    private static string Cells(int n) => n == 1 ? Loc.T("predict.cell") : Loc.T("predict.cells");

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
