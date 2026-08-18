using Kingmaker;                    // Game (area epoch)
using Kingmaker.Mechanics.Entities; // AbstractUnitEntity

namespace RTAccess.Accessibility
{
    /// <summary>
    /// The one spoken-name funnel for world units, adding a stable ordinal when several units share a
    /// display name — "Cultist" alone tells a blind player nothing when three of them stand on the field.
    /// An unnamed spawn's <c>CharacterName</c> is its blueprint's display name, byte-identical across
    /// instances; a sighted player separates them by position on screen, so a numeric disambiguator is the
    /// accessible equivalent, not extra information.
    ///
    /// <para>Ordinals key on <c>Entity.UniqueId</c> (serialized, stable across frames and saves) and are
    /// assigned per display name in first-spoken order, never recycled — "Cultist 2" keeps its number when
    /// Cultist 1 dies, and a reinforcement becomes Cultist 4, so a number always means the same individual
    /// for the whole fight. The suffix is only SPOKEN while the name is ambiguous (two or more units with
    /// that name assigned this area): unique names — companions, bosses — never grow a number.</para>
    ///
    /// <para>The registry resets when the loaded area changes (numbering is a per-area conversation;
    /// carrying "Cultist 7" into a fresh two-cultist map would be noise). Callers pass whatever unit they
    /// hold — null-safe, returns null for null — and fog gating stays the caller's job exactly as it was
    /// with raw <c>CharacterName</c>.</para>
    /// </summary>
    internal static class UnitNames
    {
        private static readonly Dictionary<string, int> ByUnit = new Dictionary<string, int>();     // UniqueId → ordinal within its name
        private static readonly Dictionary<string, int> CountByName = new Dictionary<string, int>(); // display name → ordinals assigned
        private static object _area; // the BlueprintArea this numbering epoch belongs to

        /// <summary>The unit's spoken name: <c>CharacterName</c>, plus a stable ordinal while that name is
        /// ambiguous in the current area. Null for null.</summary>
        public static string Of(AbstractUnitEntity unit)
        {
            if (unit == null) return null;
            var name = unit.CharacterName;
            if (string.IsNullOrEmpty(name)) return name;
            try
            {
                CheckEpoch();
                var id = unit.UniqueId;
                if (string.IsNullOrEmpty(id)) return name;
                if (!ByUnit.TryGetValue(id, out int ordinal))
                {
                    CountByName.TryGetValue(name, out int assigned);
                    ordinal = assigned + 1;
                    CountByName[name] = ordinal;
                    ByUnit[id] = ordinal;
                }
                return CountByName[name] > 1
                    ? Loc.T("unit.numbered", new { name, number = ordinal })
                    : name;
            }
            catch (Exception e)
            {
                Main.Log?.Error("UnitNames.Of failed: " + e);
                return name;
            }
        }

        // New area (or the menu, null) → new numbering epoch. Same-area reloads keep their numbers:
        // UniqueId survives the save, so the same individual re-earns the same ordinal on first mention.
        private static void CheckEpoch()
        {
            var area = Game.Instance?.CurrentlyLoadedArea;
            if (ReferenceEquals(area, _area)) return;
            _area = area;
            ByUnit.Clear();
            CountByName.Clear();
        }
    }
}
