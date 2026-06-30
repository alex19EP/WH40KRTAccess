using System;
using System.Collections.Generic;
using Kingmaker.Controllers.Combat;     // GetCombatStateOptional()
using Kingmaker.EntitySystem.Entities;  // BaseUnitEntity, UnitEntity
using Kingmaker.RuleSystem;             // Rulebook
using Kingmaker.RuleSystem.Rules;       // RuleCalculateStatsArmor / DodgeChance / ParryChance
using Kingmaker.UnitLogic.Buffs;        // Buff

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

        var health = unit.Health;
        if (health != null)
            Add($"{health.HitPointsLeft} of {health.MaxHitPoints} hit points");

        // Action / movement points are only meaningful in combat (yellow = actions, blue = movement cells).
        var combat = unit.GetCombatStateOptional();
        if (combat != null && combat.IsInCombat)
            Add($"Action points {combat.ActionPointsYellow}, movement {combat.ActionPointsBlue:0.#}");

        Add(DefenseLine(unit));

        // Visible buffs/debuffs in game order — Buff.Hidden already folds in the blueprint's IsHiddenInUI and
        // the buff's suppression, matching the game's own buff panel.
        var buffs = unit.Buffs;
        if (buffs != null)
            foreach (var buff in buffs)
                if (buff != null && !buff.Hidden)
                    Add(BuffLine(buff));
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
            parts.Add($"absorption {armor.ResultAbsorption} percent");
            parts.Add($"deflection {armor.ResultDeflection}");
            if (unit is UnitEntity ue)
            {
                int dodge = Rulebook.Trigger(new RuleCalculateDodgeChance(ue)).UncappedResult;
                int parry = Rulebook.Trigger(new RuleCalculateParryChance(ue)).Result;
                parts.Add($"dodge {dodge} percent");
                parts.Add($"parry {parry} percent");
            }
            return string.Join(", ", parts);
        }
        catch (Exception e)
        {
            Main.Log?.Log("UnitBuffer.DefenseLine failed: " + e.Message);
            return null;
        }
    }

    // "Bleeding x2, 3 rounds" / "Hidden, permanent" / "Stunned".
    private static string BuffLine(Buff buff)
    {
        string line = buff.Name;
        if (buff.Rank > 1) line += " x" + buff.Rank;
        if (buff.IsPermanent) line += ", permanent";
        else if (buff.ExpirationInRounds > 0) line += ", " + buff.ExpirationInRounds + " rounds";
        return line;
    }
}
