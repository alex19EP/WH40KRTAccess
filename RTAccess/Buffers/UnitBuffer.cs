using System;
using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplateBuff (buff-line detail)
using Kingmaker.Controllers.Combat;     // GetCombatStateOptional()
using Kingmaker.EntitySystem.Entities;  // BaseUnitEntity, UnitEntity
using Kingmaker.RuleSystem;             // Rulebook
using Kingmaker.RuleSystem.Rules;       // RuleCalculateStatsArmor / DodgeChance / ParryChance
using Kingmaker.UnitLogic;              // HasMechanicFeature
using Kingmaker.UnitLogic.Buffs;        // Buff
using Kingmaker.UnitLogic.Buffs.Components; // DOTLogicUIExtensions.CalculateDOTDamage
using Kingmaker.UnitLogic.Enums;        // MechanicsFeatureType (HideRealHealthInUI)
using Kingmaker.UnitLogic.Parts;        // UnitPartNonStackBonuses

namespace RTAccess.Buffers;

/// <summary>
/// A <see cref="Buffer"/> over a live unit (the selected party member, or the current combat target). Its
/// lines, in order: the unit's name, hit points, the action/movement points (only in combat), the defenses
/// (absorption, deflection, and — for a full <see cref="UnitEntity"/> — dodge and parry), then every visible
/// buff and debuff in the game's own order. The unit is resolved live on every <see cref="Update"/> via the
/// supplied factory, so the buffer always reflects the current selection / target. 40K port of WrathAccess'
/// UnitBuffer (RT has Wounds/Trauma + percentile defenses computed by rules, not D&D HP/AC).
/// </summary>
internal sealed class UnitBuffer : Buffer
{
    private readonly Func<BaseUnitEntity> _resolve;

    public UnitBuffer(string label, Func<BaseUnitEntity> resolve) : base(label) { _resolve = resolve; }

    public override void Update() => Repopulate(() => Populate(_resolve?.Invoke()));

    private void Populate(BaseUnitEntity unit)
    {
        if (unit == null) return;
        Add(unit.CharacterName);

        // Fog gate (RULE 2 / audit L1): a not-yet-revealed, non-party unit has its ENTIRE overtip hidden by the
        // game (OvertipEntityUnitVM.HideFromScreen) — HP, defenses AND buffs. Never read any of them here. The
        // name stays (it is parity-safe — the whose-turn cue already speaks the acting enemy's name); the rest is
        // suppressed. Party units (IsPlayerFaction) are always shown, so they never hit this gate.
        if (!(unit.IsPlayerFaction || unit.IsVisibleForPlayer))
        {
            Add(Loc.T("buffer.not_visible"));
            return;
        }

        var health = unit.Health;
        if (health != null)
            // Honor the game's HideRealHealthInUI mask (concealed-health bosses/special units read "???" — audit L2).
            Add(unit.HasMechanicFeature(MechanicsFeatureType.HideRealHealthInUI)
                ? Loc.T("buffer.hit_points_hidden")
                : Loc.T("buffer.hit_points", new { current = health.HitPointsLeft, max = health.MaxHitPoints }));

        // Action / movement points are only meaningful in combat (yellow = actions, blue = movement cells).
        var combat = unit.GetCombatStateOptional();
        if (combat != null && combat.IsInCombat)
            Add(Loc.T("buffer.ap_mp", new { ap = combat.ActionPointsYellow, mp = combat.ActionPointsBlue.ToString("0.#") }));

        Add(DefenseLine(unit));

        // Visible buffs/debuffs in game order — Buff.Hidden already folds in the blueprint's IsHiddenInUI and
        // the buff's suppression, matching the game's own buff panel. Each line carries the game's OWN buff
        // tooltip (TooltipTemplateBuff — description, source, and the non-stack conflict list, audit #20) as
        // its on-demand detail, exactly what a sighted player reads hovering the icon.
        var buffs = unit.Buffs;
        if (buffs != null)
            foreach (var buff in buffs)
                if (buff != null && !buff.Hidden)
                {
                    var b = buff; // pin for the lazy detail closure
                    Add(BuffLine(b), () => new TooltipTemplateBuff(b));
                }
    }

    // "absorption 35 percent, deflection 4, dodge 20 percent, parry 15 percent" — the RT defense values, each
    // computed by the game's own rules (the same path the character sheet uses). Dodge and parry need a
    // concrete UnitEntity; armour absorption/deflection work for any mechanic entity. Rule triggers are
    // guarded so an unexpected unit shape can't throw out of input handling.
    private static string DefenseLine(BaseUnitEntity unit)
    {
        try
        {
            var parts = new List<string>();
            var armor = Rulebook.Trigger(new RuleCalculateStatsArmor(unit));
            parts.Add(Loc.T("buffer.absorption", new { value = armor.ResultAbsorption }));
            parts.Add(Loc.T("buffer.deflection", new { value = armor.ResultDeflection }));
            if (unit is UnitEntity ue)
            {
                int dodge = Rulebook.Trigger(new RuleCalculateDodgeChance(ue)).UncappedResult;
                int parry = Rulebook.Trigger(new RuleCalculateParryChance(ue)).Result;
                parts.Add(Loc.T("buffer.dodge", new { value = dodge }));
                parts.Add(Loc.T("buffer.parry", new { value = parry }));
            }
            return string.Join(", ", parts);
        }
        catch (Exception e)
        {
            Main.Log?.Log("UnitBuffer.DefenseLine failed: " + e.Message);
            return null;
        }
    }

    // "Bleeding x2, 3 rounds" / "Hidden, permanent" / "Stunned". The buff name is game text; the rank /
    // permanent / rounds annotations resolve through the locale table.
    private static string BuffLine(Buff buff)
    {
        string line = buff.Name;

        // #6 DOT buffs read damage-per-turn in place of rank (audit): BuffPCView binds BuffVM.Rank from the DOT
        // damage path — BuffVM.SetGroup/UpdateRank/CalculateDamage route a DOTLogicVisual buff to
        // DOTLogicUIExtensions.CalculateDOTDamage(buff).AverageValue (rounded per-turn average) instead of the
        // raw rank. Mirror that: DOT → damage number, everything else → plain rank. CalculateDOTDamage can
        // return null (no matching DOTLogic on the owner), so guard exactly as the VM does.
        if (buff.Blueprint != null && buff.Blueprint.IsDOTVisual)
        {
            var dot = DOTLogicUIExtensions.CalculateDOTDamage(buff);
            if (dot != null) line += " " + Loc.T("buffer.dot_damage", new { value = dot.AverageValue });
        }
        else if (buff.Rank > 1) line += " " + Loc.T("buffer.rank", new { rank = buff.Rank });

        if (buff.IsPermanent) line += ", " + Loc.T("buffer.permanent");
        else if (buff.ExpirationInRounds > 0) line += ", " + Loc.T("buffer.rounds", new { rounds = buff.ExpirationInRounds });

        // #20 Non-stack conflict — the game shows BuffVM.ShowNonStackNotification when this buff's bonus is being
        // overridden by a stronger non-stacking one; UnitPartNonStackBonuses.ShouldShowWarning(buff) is the same
        // source the VM reads. The part only tracks party companions (see HandleModifierAdded's IsPlayer /
        // IsInCompanionRoster gate), so this is parity-safe with no visibility gate needed. This inline marker
        // mirrors the card's warning badge; WHICH sources conflict is tooltip detail (TooltipBrickNonStack in
        // the game's buff tooltip), reached through the line's detail template above — as the sighted split is.
        if (buff.Owner?.GetOptional<UnitPartNonStackBonuses>()?.ShouldShowWarning(buff) ?? false)
            line += ", " + Loc.T("buffer.non_stack");

        return line;
    }
}
