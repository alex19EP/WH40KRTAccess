using Kingmaker;                       // Game (area epoch)
using Kingmaker.EntitySystem.Entities; // MapObjectEntity

namespace RTAccess.Accessibility
{
    /// <summary>
    /// The map-object twin of <see cref="UnitNames"/>: a stable ordinal appended to an object's spoken name while
    /// several objects in the area share it. Rogue Trader leaves most interactables nameless, so the resolver
    /// speaks a category singular for them ("Search point", "Point of interest", "Door") — and a room with twelve
    /// examine volumes read as twelve identical "Point of interest" lines, with nothing to tell a blind player which
    /// one they had already walked to (September 2026 tester item 4). A sighted player tells them apart by where
    /// they sit on screen; a number is the accessible equivalent, not extra information.
    ///
    /// <para>Ordinals key on <c>MapObjectEntity.UniqueId</c> (serialized, stable across frames and saves) and are
    /// assigned per spoken name in first-spoken order, never recycled, so a number always means the same object for
    /// the whole visit. The suffix is only SPOKEN while the name is ambiguous (two or more objects with that name
    /// assigned this area): a uniquely named console never grows a number. The registry resets when the loaded
    /// area changes, exactly like the unit registry. Applied inside <see cref="InteractableDescriber"/>'s name
    /// resolver, so the scanner browse, the review cycles, the tile cursor, the exit cycle and the loot / choice
    /// window titles all agree on one name.</para>
    /// </summary>
    internal static class MapObjectNames
    {
        private static readonly Dictionary<string, int> ByObject = new Dictionary<string, int>();   // UniqueId → ordinal within its name
        private static readonly Dictionary<string, int> CountByName = new Dictionary<string, int>(); // spoken name → ordinals assigned
        private static object _area; // the BlueprintArea this numbering epoch belongs to

        /// <summary>The object's spoken name: <paramref name="name"/>, plus a stable ordinal while that name is
        /// ambiguous in the current area. Pass-through for a null object or a blank name.</summary>
        public static string Of(MapObjectEntity obj, string name)
        {
            if (obj == null || string.IsNullOrEmpty(name)) return name;
            try
            {
                CheckEpoch();
                var id = obj.UniqueId;
                if (string.IsNullOrEmpty(id)) return name;
                if (!ByObject.TryGetValue(id, out int ordinal))
                {
                    CountByName.TryGetValue(name, out int assigned);
                    ordinal = assigned + 1;
                    CountByName[name] = ordinal;
                    ByObject[id] = ordinal;
                }
                // The ordinal was earned under the name the object had when first spoken; a check-once search
                // point that swaps to its after-use name re-enters under the new name and earns a fresh
                // ordinal there — the same "per display name" rule the unit registry applies.
                return CountByName.TryGetValue(name, out int count) && count > 1
                    ? Loc.T("object.numbered", new { name, number = ordinal })
                    : name;
            }
            catch (Exception e)
            {
                Main.Log?.Error("MapObjectNames.Of failed: " + e);
                return name;
            }
        }

        // New area (or the menu, null) → new numbering epoch. Same-area reloads keep their numbers:
        // UniqueId survives the save, so the same object re-earns the same ordinal on first mention.
        private static void CheckEpoch()
        {
            var area = Game.Instance?.CurrentlyLoadedArea;
            if (ReferenceEquals(area, _area)) return;
            _area = area;
            ByObject.Clear();
            CountByName.Clear();
        }
    }
}
