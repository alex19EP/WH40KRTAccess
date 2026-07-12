using System;
using System.Text;
using Kingmaker.ElementsSystem.ContextData;               // ContextData<T>
using Kingmaker.EntitySystem.Entities;                    // StarshipEntity
using Kingmaker.RuleSystem;                               // Rulebook
using Kingmaker.RuleSystem.Rules.Starships;               // RuleStarshipCalculateHitChances
using Kingmaker.UnitLogic;                                // HasMechanicFeature extension
using Kingmaker.UnitLogic.Abilities;                      // AbilityData, AbilityDataHelper, DamagePredictionData, ShieldDamageData
using Kingmaker.UnitLogic.Enums;                          // MechanicsFeatureType.HideRealHealthInUI
using Kingmaker.Utility;                                  // TargetWrapper
using Kingmaker.Utility.StatefulRandom;                   // DisableStatefulRandomContext

namespace RTAccess.Combat;

/// <summary>
/// Hit/damage prediction for STARSHIP weapon aims (Phase 3 of docs/plans/inertial-broadsiding-tsiolkovsky.md) —
/// the voidship sibling of <see cref="Accessibility.HitPredictor"/>. Ship attacks run a different rulebook, so the
/// surface path (RuleCalculateHitChances + the overtip data cache) reads wrong or empty here; this speaks exactly
/// what the sighted overtips show while aiming a ship weapon: the hit-chance block's flat per-shot hit % and shot
/// count (<c>AbilityTargetUIData.UpdateWithStarshipWeapon</c>: <c>RuleStarshipCalculateHitChances</c> +
/// <c>DamageInstances</c> — ship bursts share ONE chance, there is no per-shot list), and the health block's
/// shield prediction (<c>GetStarshipDamagePrediction</c> under <c>DisableStatefulRandomContext</c>, the overtip's
/// own call). Refusals go through the game's <c>CanTarget</c>, whose starship arc check surfaces as
/// <c>HasNoLosToTarget</c> — spoken as "out of firing arc" (verified in-harness, plan §0b).
///
/// Pure reads, no game state changes. Parity gates: never describe a fog-hidden ship; the can-kill flag honours
/// the <c>HideRealHealthInUI</c> mask (shield numbers stay — the sighted health block shows enemy shields
/// unmasked while aiming).
/// </summary>
internal static class StarshipAim
{
    /// <summary>One spoken line for aiming <paramref name="ability"/> (a ship weapon of <paramref name="caster"/>)
    /// at <paramref name="target"/> — a refusal reason ("out of firing arc" / "out of range") or the odds:
    /// "80% to hit, 4 shots, shield damage 28 to 56, shields 35 of 35, hull damage 0 to 3[, can kill]".
    /// Verbose adds crit and the target's evasion. Null on bad input / fog-hidden target (caller stays silent).</summary>
    public static string Describe(StarshipEntity caster, AbilityData ability, StarshipEntity target, bool verbose = false)
    {
        if (caster == null || target == null || ability?.StarshipWeapon == null || target == caster) return null;
        if (!(target.IsPlayerFaction || target.IsVisibleForPlayer)) return null;   // fog parity
        try
        {
            if (!ability.CanTarget(new TargetWrapper(target), out var reason)) return Refusal(reason);

            var rule = Rulebook.Trigger(new RuleStarshipCalculateHitChances(caster, target, ability.StarshipWeapon));
            DamagePredictionData hull;
            ShieldDamageData shields;
            using (ContextData<DisableStatefulRandomContext>.Request())
            {
                var pred = AbilityDataHelper.GetStarshipDamagePrediction(target, caster.Position, ability, ability.StarshipWeapon);
                hull = pred.resultDamage;
                shields = pred.resultShields;
            }

            var sb = new StringBuilder();
            sb.Append(Loc.T("predict.to_hit", new { hit = rule.ResultHitChance }));
            if (verbose && rule.ResultCritChance > 0)
                sb.Append(", ").Append(Loc.T("predict.crit", new { crit = rule.ResultCritChance }));
            int shots = ability.StarshipWeapon.Blueprint?.DamageInstances ?? 0;
            if (shots > 1) sb.Append(", ").Append(Loc.T("spacecombat.shots", new { n = shots }));
            if (shields != null)
            {
                if (shields.MaxDamage > 0)
                    sb.Append(", ").Append(Loc.T("spacecombat.shield_damage", new { min = shields.MinDamage, max = shields.MaxDamage }));
                sb.Append(", ").Append(Loc.T("spacecombat.shields_pool", new { cur = shields.CurrentShield, max = shields.MaxShield }));
            }
            if (hull != null)
                sb.Append(", ").Append(Loc.T("spacecombat.hull_damage", new { min = hull.MinDamage, max = hull.MaxDamage }));
            bool masked = target.HasMechanicFeature(MechanicsFeatureType.HideRealHealthInUI);
            if (!masked && hull != null && hull.MaxDamage > 0 && target.Health != null
                && hull.MaxDamage >= target.Health.HitPointsLeft + target.Health.TemporaryHitPoints)
                sb.Append(", ").Append(Loc.T("predict.can_kill"));
            if (verbose && rule.ResultEvasionChance > 0)
                sb.Append(", ").Append(Loc.T("spacecombat.evasion", new { pct = rule.ResultEvasionChance }));
            return sb.ToString();
        }
        catch (Exception e) { Main.Log?.Log("starship aim describe failed: " + e.Message); return null; }
    }

    // The game maps a starship arc failure onto HasNoLosToTarget (there is no dedicated arc reason) —
    // "out of firing arc" is the honest ship wording for it; real sight-blockers don't exist on the
    // open space grid. TargetTooFar keeps the shared range wording.
    private static string Refusal(AbilityData.UnavailabilityReasonType? reason)
    {
        switch (reason)
        {
            case AbilityData.UnavailabilityReasonType.TargetTooFar: return Loc.T("predict.out_of_range");
            case AbilityData.UnavailabilityReasonType.HasNoLosToTarget: return Loc.T("spacecombat.out_of_arc");
            default: return Loc.T("predict.cant_target");
        }
    }
}
