using System.Text;
using Kingmaker.Blueprints;                // GetComponent<T>() blueprint extension
using Kingmaker.Blueprints.Root;           // Root.WH.BlueprintTraumaRoot
using Kingmaker.Code.UnitLogic.FactLogic;  // AddRandomUniqueFactOnEachRank (the named-trauma grants)
using Kingmaker.EntitySystem.Entities;     // BaseUnitEntity
using Kingmaker.UnitLogic.Buffs.Blueprints; // BlueprintBuff

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Shared unit-stat readouts spoken across the HUD and character screens. Keeps line-assembly the game
    /// exposes on the Health part in one place so the injury vocabulary can't drift between call sites.
    /// Terminology: the game's "wounds" ARE hit points; the accrued afflictions are the "Fresh Injury" /
    /// "Old Injury" / trauma buffs — always spoken by their own blueprint names, never re-labelled.
    /// </summary>
    internal static class UnitReads
    {
        /// <summary>
        /// The current/max wounds line (plus temporary wounds), optionally with the 40K injury state:
        /// fresh/old injury stacks and the named traumas (Broken Ribs, Concussion, …). No leading
        /// separator — the caller positions it. Null when the unit has no Health part to read (a
        /// placeholder / squad card with no BaseUnitEntity body).
        /// </summary>
        public static string Wounds(BaseUnitEntity unit, bool withTrauma = false)
        {
            var h = unit?.Health;
            if (h == null) return null;
            var sb = new StringBuilder();
            sb.Append(Loc.T("unit.wounds", new { current = h.HitPointsLeft, max = h.MaxHitPoints }));
            if (h.TemporaryHitPoints > 0)
                sb.Append(", ").Append(Loc.T("unit.wounds_temp", new { temp = h.TemporaryHitPoints }));
            if (withTrauma)
            {
                var root = Root.WH.BlueprintTraumaRoot;
                AppendInjury(sb, root?.FreshWound, h.WoundFreshStacks);
                AppendInjury(sb, root?.OldWound, h.WoundOldStacks);
                AppendNamedTraumas(sb, unit, root?.Trauma);
            }
            return sb.ToString();
        }

        // "Fresh Injury" / "Fresh Injury x3" — the buff's own display name, exactly what the sighted
        // player's icon and hover tooltip carry (the icon shows the rank badge only from 2 up, matching
        // the count suffix here).
        private static void AppendInjury(StringBuilder sb, BlueprintBuff blueprint, int count)
        {
            if (count <= 0) return;
            var name = blueprint?.Name;
            if (string.IsNullOrEmpty(name)) return;
            sb.Append(", ").Append(name);
            if (count > 1) sb.Append(' ').Append(Loc.T("buffer.rank", new { rank = count }));
        }

        // The generic Trauma buff is IsHiddenInUI by design — what a sighted player sees is the one NAMED
        // trauma it grants per rank (Broken Ribs, Concussion, Crippled Arm, …) as an ordinary visible buff
        // icon. Speak those names. This also keeps the line talking at the exact moment a trauma lands:
        // the game clears both injury buffs then (PartHealth.DealTraumasImpl), so without the trauma names
        // the readout would fall silent precisely when the state matters most.
        private static void AppendNamedTraumas(StringBuilder sb, BaseUnitEntity unit, BlueprintBuff trauma)
        {
            var grants = trauma?.GetComponent<AddRandomUniqueFactOnEachRank>();
            var buffs = unit.Buffs;
            if (grants == null || buffs == null) return;
            var facts = grants.Facts;
            foreach (var buff in buffs)
                if (buff != null && !buff.Hidden && facts.HasReference(buff.Blueprint))
                    sb.Append(", ").Append(buff.Name);
        }
    }
}
