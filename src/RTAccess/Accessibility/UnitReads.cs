using System.Text;
using Kingmaker.EntitySystem.Entities; // BaseUnitEntity

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Shared unit-stat readouts spoken across the HUD and character screens. Keeps line-assembly the game
    /// exposes on the Health part in one place so the wound vocabulary can't drift between call sites.
    /// </summary>
    internal static class UnitReads
    {
        /// <summary>
        /// The current/max wounds line (plus temporary wounds), optionally with the 40K trauma stacks (fresh
        /// and old wounds). No leading separator — the caller positions it. Null when the unit has no Health
        /// part to read (a placeholder / squad card with no BaseUnitEntity body).
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
                if (h.WoundFreshStacks > 0)
                    sb.Append(", ").Append(Loc.T("charinfo.fresh_wounds", new { count = h.WoundFreshStacks }));
                if (h.WoundOldStacks > 0)
                    sb.Append(", ").Append(Loc.T("charinfo.old_wounds", new { count = h.WoundOldStacks }));
            }
            return sb.ToString();
        }
    }
}
