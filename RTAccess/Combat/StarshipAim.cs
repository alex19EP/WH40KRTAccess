using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker.Blueprints.Root.Strings;                  // UIStrings (the weapon groups' own arc labels)
using Kingmaker.ElementsSystem.ContextData;               // ContextData<T>
using Kingmaker.EntitySystem.Entities;                    // StarshipEntity
using Kingmaker.RuleSystem;                               // Rulebook
using Kingmaker.RuleSystem.Rules.Starships;               // RuleStarshipCalculateHitChances
using Kingmaker.UnitLogic;                                // HasMechanicFeature extension
using Kingmaker.UnitLogic.Abilities;                      // AbilityData, AbilityDataHelper, DamagePredictionData, ShieldDamageData
using Kingmaker.UnitLogic.Enums;                          // MechanicsFeatureType.HideRealHealthInUI
using Kingmaker.Utility;                                  // TargetWrapper
using Kingmaker.Utility.StatefulRandom;                   // DisableStatefulRandomContext
using Warhammer.SpaceCombat.Blueprints.Slots;             // WeaponSlotType

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
            // DESIRED position, not raw Position: while a move preview is pinned (the planned-move
            // hologram — CommandDispatch.MoveStep), the arc gate answers from the planned cell WITH
            // the arrival heading (CanTargetFromDesiredPosition reads the virtual rotation too), exactly
            // like the sighted overtips against the ghost. With nothing pinned both collapse to Position.
            if (!ability.CanTargetFromDesiredPosition(new TargetWrapper(target), out var reason)) return Refusal(reason);

            var rule = Rulebook.Trigger(new RuleStarshipCalculateHitChances(caster, target, ability.StarshipWeapon));
            var casterPos = caster.Position;
            try { casterPos = Kingmaker.Game.Instance.VirtualPositionController.GetDesiredPosition(caster); } catch { }
            DamagePredictionData hull;
            ShieldDamageData shields;
            using (ContextData<DisableStatefulRandomContext>.Request())
            {
                var pred = AbilityDataHelper.GetStarshipDamagePrediction(target, casterPos, ability, ability.StarshipWeapon);
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

    /// <summary>Which of OUR weapon groups can bear on <paramref name="target"/> right now — the game's own
    /// per-weapon <c>CanTarget</c> verdict (arc + range), labelled with the weapon panel's own group words:
    /// "Prow, Dorsal bear on it" / "no weapons bear on it". The pre-aim answer to "which battery do I arm?".</summary>
    public static string OurArcsLine(StarshipEntity ours, StarshipEntity target)
    {
        try
        {
            if (ours?.Hull?.WeaponSlots == null || target == null) return null;
            var labels = new List<string>();
            foreach (var slot in ours.Hull.WeaponSlots)
            {
                var ad = slot?.ActiveAbility?.Data;
                if (ad?.StarshipWeapon == null) continue;
                // Desired-position variant: with a move preview pinned, "which batteries bear" answers
                // from the planned cell + arrival heading (the whole point of arming the move first).
                if (!ad.CanTargetFromDesiredPosition(new TargetWrapper(target))) continue;
                string label = GroupLabel(slot.Type);
                if (label != null && !labels.Contains(label)) labels.Add(label);
            }
            return labels.Count > 0
                ? Loc.T("spacecombat.bears", new { list = string.Join(", ", labels) })
                : Loc.T("spacecombat.bears_none");
        }
        catch (Exception e) { Main.Log?.Log("our-arcs line failed: " + e.Message); return null; }
    }

    /// <summary>Which of the ENEMY's arcs we are standing in — its weapons whose <c>CanTarget</c> accepts our
    /// ship from where both stand: "it can fire on you: fore, dorsal" / "it cannot fire on you". Derived from
    /// the visible facing + the inspectable loadout, so no more than a sighted player reads off the screen.</summary>
    public static string ThreatArcsLine(StarshipEntity ours, StarshipEntity enemy)
    {
        try
        {
            if (ours == null || enemy?.Hull?.WeaponSlots == null) return null;
            var arcs = new List<string>();
            var wrapped = new TargetWrapper(ours);
            foreach (var slot in enemy.Hull.WeaponSlots)
            {
                var ad = slot?.ActiveAbility?.Data;
                if (ad?.StarshipWeapon == null) continue;
                if (!ad.CanTarget(wrapped, out _)) continue;
                string word = ArcWord(slot.Type);
                if (word != null && !arcs.Contains(word)) arcs.Add(word);
            }
            return arcs.Count > 0
                ? Loc.T("spacecombat.threat_arcs", new { list = string.Join(", ", arcs) })
                : Loc.T("spacecombat.threat_none");
        }
        catch (Exception e) { Main.Log?.Log("threat-arcs line failed: " + e.Message); return null; }
    }

    // Our slots speak the weapon panel's own group labels (game-localized, matching the Weapons zone);
    // Keel has no panel group, so it gets a mod word.
    private static string GroupLabel(WeaponSlotType type)
    {
        try
        {
            var texts = UIStrings.Instance.SpaceCombatTexts;
            switch (type)
            {
                case WeaponSlotType.Prow: return texts.ProwAbilitiesGroupLabel;
                case WeaponSlotType.Dorsal: return texts.DorsalAbilitiesGroupLabel;
                case WeaponSlotType.Port: return texts.PortAbilitiesGroupLabel;
                case WeaponSlotType.Starboard: return texts.StarboardAbilitiesGroupLabel;
                case WeaponSlotType.Keel: return Loc.T("spacecombat.arc_keel");
                default: return null;
            }
        }
        catch { return null; }
    }

    // The enemy's arcs read as plain direction words (its panel labels are about OUR ship's UI, and the
    // sector words are already in the mod's table for the shield cues).
    private static string ArcWord(WeaponSlotType type)
    {
        switch (type)
        {
            case WeaponSlotType.Prow: return Loc.T("spacecombat.sector_fore");
            case WeaponSlotType.Port: return Loc.T("spacecombat.sector_port");
            case WeaponSlotType.Starboard: return Loc.T("spacecombat.sector_starboard");
            case WeaponSlotType.Dorsal: return Loc.T("spacecombat.arc_dorsal");
            case WeaponSlotType.Keel: return Loc.T("spacecombat.arc_keel");
            default: return null;
        }
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
