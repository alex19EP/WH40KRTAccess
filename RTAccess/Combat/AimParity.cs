using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;                                                     // Game
using Kingmaker.Code.UI.MVVM.VM.Overtips.Unit;                       // OvertipEntityUnitVM
using Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.UnitOvertipParts;      // OvertipHitChanceBlockVM
using Kingmaker.EntitySystem.Entities;                               // BaseUnitEntity
using Kingmaker.UnitLogic;                                           // UnitPredictionManager, HasMechanicFeature
using Kingmaker.UnitLogic.Abilities;                                 // AbilityData, AbilityTargetUIData
using Kingmaker.UnitLogic.Abilities.Blueprints;                      // AbilityTargetAnchor
using Kingmaker.UnitLogic.Abilities.Components;                      // TargetType
using Kingmaker.UnitLogic.Enums;                                     // MechanicsFeatureType
using Kingmaker.Utility;                                             // TargetWrapper

namespace RTAccess.Combat;

/// <summary>
/// The sighted-parity core for aim readouts: decides which units are voiced and with what numbers so we speak EXACTLY
/// what the game's own per-unit hit-chance overtip (<c>OvertipHitChanceBlockVM</c>) shows a sighted player — no more,
/// no less. Everything here is sourced from the game's own gate + the game's own <see cref="AbilityTargetUIData"/>
/// values; we never re-derive a faction/range/LOS rule of our own. See docs/plans/piloted-aiming-lamport.md.
///
/// WHY not just read the rendered overtip fields (HasHit/HitChance): those are written in <c>UpdateProperties</c>, which
/// only runs when <c>IsVisibleTrigger</c> fires — and for a single-target aim that trigger lights ONLY via
/// <c>UnitState.IsMouseOverUnit</c>, whose driver (<c>SurfaceMainInputLayer</c>) is engine-dead behind
/// <c>!IsControllerMouse</c> in our forced controller-mouse mode. So the rendered fields read stale/blank for us. But
/// the GATE the overtip uses — <c>CalculateCanHit</c> — reads only the armed ability and
/// <c>ClickEventsController.WorldPosition</c> (which our <see cref="AimPointerDriver"/> injects), NOT the visibility
/// pipeline. So we CALL that gate synchronously after our forced recompute and get the game's true UI answer, and we
/// take the displayed numbers from the same <c>AbilityTargetUIData</c> the overtip binds, with its exact transforms.
/// </summary>
internal static class AimParity
{
    /// <summary>The game's own hit-chance overtip VM for a unit — the exact VM the sighted overtip binds — or null if
    /// the unit currently has no overtip (off-screen / not tracked → excluded, which is correct fog parity).</summary>
    public static OvertipHitChanceBlockVM Locate(BaseUnitEntity unit)
    {
        var so = Game.Instance?.RootUiContext?.SurfaceVM?.DynamicPartVM?.SurfaceOvertipsVM?.Value;
        if (so == null || unit == null) return null;
        foreach (var o in so.UnitOvertipsCollectionVM.Overtips)
        {
            var hb = (o as OvertipEntityUnitVM)?.HitChanceBlockVM;
            if (hb?.UnitState != null && hb.UnitState.Unit.MechanicEntity == unit) return hb;
        }
        return null;
    }

    /// <summary>True iff the game would show this unit's hit-chance overtip while aiming — the parity gate. Combines the
    /// overtip's own visibility conditions (in combat, visible-for-player = fog parity, not dead) with its hit gate
    /// <c>CalculateCanHit</c> (faction + range + LOS). We deliberately DROP the overtip's <c>isHover||isAoETarget</c>
    /// render trigger: that is a rendering/input-mode artifact (a sighted player can hover any of these to reveal it),
    /// not an information gate. We call the game's own <c>CalculateCanHit</c> on the located VM; if the VM is absent or
    /// its <c>UnitState.Ability</c> isn't set yet, we fall back to a verbatim transcription that calls the same game
    /// methods off our armed ability.</summary>
    public static bool Shown(BaseUnitEntity unit, AbilityData ability)
    {
        if (unit == null || ability == null) return false;
        if (!unit.IsInCombat || !unit.IsVisibleForPlayer || unit.LifeState.IsDead) return false;
        var hb = Locate(unit);
        if (hb != null && hb.UnitState.Ability.Value != null)
        {
            try { return hb.CalculateCanHit(); }
            catch (Exception e) { Main.Log?.Log("AimParity VM gate threw, using fallback: " + e.Message); }
        }
        return StaticGate(unit, ability);
    }

    // Verbatim transcription of OvertipHitChanceBlockVM.CalculateCanHit (VM lines 168-190), calling the game's OWN
    // methods off our armed ability + the WorldPosition our driver injects — used only when the overtip VM is absent /
    // its UnitState.Ability isn't set. VM-independent, so the make-or-break UnitState.Ability anomaly can't sink it.
    private static bool StaticGate(BaseUnitEntity unit, AbilityData ability)
    {
        if (!CanAoETarget(ability.Blueprint.AoETargets, unit)) return false;              // VM:170 / 218-235
        if (ability.TargetAnchor == AbilityTargetAnchor.Owner) return true;               // VM:174-176
        var h = Game.Instance?.SelectedAbilityHandler;                                    // VM:178
        TargetWrapper tw = h?.Ability != null
            ? h.GetTargetForDesiredPosition(unit.View.gameObject, Game.Instance.ClickEventsController.WorldPosition)  // VM:179
            : (TargetWrapper)unit;
        bool ricochet = UnitPredictionManager.Instance?.IsUnitRicochetTarget(unit) ?? false;  // VM:180
        if (tw == null) return false;                                                    // VM:181-189
        return ricochet || ability.CanTargetFromDesiredPosition(tw);                      // VM:183-187
    }

    // Verbatim OvertipHitChanceBlockVM.CanAoETarget (VM:218-235). The VM's Ally branch is `IsPlayer ? true :
    // IsPlayerFaction`, but the wrapper's IsPlayer (== faction.IsPlayer) and IsPlayerFaction (== entity.IsPlayerFaction
    // == GetFactionOptional().IsPlayer, MechanicEntity.cs:108) are the SAME value, so IsPlayerFaction alone is faithful.
    private static bool CanAoETarget(TargetType t, BaseUnitEntity u)
    {
        switch (t)
        {
            case TargetType.Enemy: return u.IsPlayerEnemy;
            case TargetType.Ally: return u.IsPlayerFaction;
            case TargetType.Any: return true;
            default: return false;
        }
    }

    /// <summary>The overtip's displayed numbers for a shown unit, projected from the game's own
    /// <see cref="AbilityTargetUIData"/> (the same struct the overtip binds) with <c>UpdateProperties</c>' exact
    /// transforms: the ricochet/redirect damage mask (VM:148-150) and the kill test (VM:144).</summary>
    public readonly struct Shot
    {
        public readonly bool HitAlways;
        public readonly float HitChance;        // with-avoidance (what the overtip's % shows)
        public readonly float InitialHitChance; // before avoidance
        public readonly int MinDamage, MaxDamage;
        public readonly bool CanDie, CanPush, Ricochet;
        public readonly float Dodge, Parry, Cover, Evasion, Block;
        public readonly List<float> Burst;

        public Shot(AbilityTargetUIData d, BaseUnitEntity u)
        {
            // Ricochet/redirected targets: the screen blanks damage to '???'/0 (VM:148-150). Mirror that mask.
            Ricochet = d.IsAbilityRedirected || (UnitPredictionManager.Instance?.IsUnitRicochetTarget(u) ?? false);
            MinDamage = Ricochet ? 0 : d.MinDamage;
            MaxDamage = Ricochet ? 0 : d.MaxDamage;
            HitAlways = d.HitAlways;
            HitChance = d.HitWithAvoidanceChance;
            InitialHitChance = d.InitialHitChance;
            CanPush = d.CanPush;
            Dodge = d.DodgeChance; Parry = d.ParryChance; Cover = d.CoverChance;
            Evasion = d.EvasionChance; Block = d.BlockChance;
            Burst = d.BurstHitChances;
            // Kill test mirrors the overtip's CanDie (VM:144) but keeps the HideRealHealthInUI mask: a sighted player
            // sees only '???' HP on masked units and no death indicator, so revealing a kill there would be "more" than
            // they see. (VM:144 omits the mask; the overtip VIEW is presumed to suppress the skull — verify and drop
            // this guard if the view actually shows it.)
            CanDie = u?.Health != null && MaxDamage > 0
                     && !u.HasMechanicFeature(MechanicsFeatureType.HideRealHealthInUI)
                     && MaxDamage >= u.Health.HitPointsLeft + u.Health.TemporaryHitPoints;
        }
    }

    /// <summary>Project the overtip's shown values for a unit from its captured aim data.</summary>
    public static Shot Project(AbilityTargetUIData d, BaseUnitEntity u) => new Shot(d, u);

#if DEBUG
    /// <summary>Dev dump (F-key / eval): for the current aim, list every captured target with the located-VM state, the
    /// VM gate vs the static-gate fallback (must agree), the Shown() verdict, and the projected numbers — so we can
    /// confirm in-combat that our parity set/values match the on-screen overtips.</summary>
    public static string DumpGate()
    {
        var ability = Game.Instance?.SelectedAbilityHandler?.Ability;
        if (ability == null) return "not aiming";
        AimRead.RefreshAtCursor();
        var sb = new StringBuilder("AoETargets=").Append(ability.Blueprint.AoETargets).Append('\n');
        foreach (var d in AimReadTap.Instance.Last)
        {
            if (!(d.Target is BaseUnitEntity u)) continue;
            var hb = Locate(u);
            string vm = "no-vm";
            if (hb != null)
            {
                string ab = hb.UnitState.Ability.Value != null ? "set" : "NULL";
                try { vm = "vm[" + ab + "]=" + hb.CalculateCanHit(); } catch (Exception e) { vm = "vm[" + ab + "]ERR:" + e.Message; }
            }
            bool stat = StaticGate(u, ability);
            var s = Project(d, u);
            sb.Append(u.CharacterName).Append(u.IsPlayerEnemy ? "[E] " : "[A] ")
              .Append(vm).Append(" static=").Append(stat).Append(" shown=").Append(Shown(u, ability))
              .Append(" | hit=").Append(s.HitAlways ? "sure" : ((int)s.HitChance) + "%")
              .Append(" dmg=").Append(s.MinDamage).Append('-').Append(s.MaxDamage)
              .Append(s.Ricochet ? " ricochet" : "").Append(s.CanDie ? " kill" : "").Append('\n');
        }
        return sb.ToString();
    }
#endif
}
