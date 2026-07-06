using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Parts;   // UnitPartPetOwner
using RTAccess.Speech;

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Shared read of the unit a full-screen service window (Inventory / Character Info) is SHOWING — the
    /// game's <c>SelectionCharacter.SelectedUnitInUI</c>, switched by the mod's party chords (Shift+A /
    /// Shift+D: the Exploration bindings stay CLAIMED under these non-Exclusive windows — the category walk
    /// runs past them down to InGameScreen — so the game's own Prev/NextCharacter binds never fire and
    /// <see cref="SwitchMember"/> must be the responder) and by the focusable prev/next buttons both windows
    /// declare in their character group.
    ///
    /// <para><see cref="Tick"/> speaks the newly-viewed character on a switch: the game never announces it,
    /// and <see cref="SelectionAnnouncer"/> only watches the WORLD selection (<c>SelectedUnit</c>), not the
    /// fullscreen UI preview — so without this a Shift+A/D switch is silent. <see cref="HeaderLine"/> is the
    /// focusable header readout both screens show. Pet swap mirrors the game's own m_PetButton (a pet lives
    /// off <c>ActualGroup</c>, so the roster switch keys never reach it — it's a separate axis).</para>
    /// </summary>
    internal static class ViewedCharacter
    {
        private static BaseUnitEntity _last;

        /// <summary>Announce the viewed unit when it CHANGES (a Shift+A/D switch). Silent on the first
        /// observation after <see cref="Reset"/> (window just opened — its name is already spoken by the
        /// ServiceWindowAnnounce patch). Interrupt: the switch was a keypress the player expects feedback for.</summary>
        public static void Tick(BaseUnitEntity unit)
        {
            if (unit == null) { _last = null; return; }
            if (ReferenceEquals(unit, _last)) return;
            bool first = _last == null;
            _last = unit;
            if (!first) Speaker.Speak(HeaderLine(unit), interrupt: true);
        }

        /// <summary>Clear the guard so the next observation (a fresh window open) re-baselines silently.</summary>
        public static void Reset() => _last = null;

        /// <summary>True while a member-switching service window (Inventory / Character Info) is the focused
        /// mod screen — the contexts where <see cref="SwitchMember"/>/<see cref="SwitchTo"/> apply.</summary>
        public static bool WindowActive
            => RTAccess.Screens.ScreenManager.Current is RTAccess.Screens.InventoryScreen
                or RTAccess.Screens.CharacterInfoScreen;

        /// <summary>Switch the viewed member to the next/previous roster entry — the same walk the game's own
        /// Prev/NextCharacter handlers run (CharInfoNameAndPortraitVM.SelectCharacter): step
        /// <c>SelectionCharacter.ActualGroup</c> relative to the viewed unit, then <c>SetSelected</c>. With a
        /// fullscreen UI up, SetSelected retargets only <c>SelectedUnitInUI</c> (the world selection stays
        /// put), so <see cref="SelectionAnnouncer"/> stays quiet and <see cref="Tick"/> is the one announce.</summary>
        public static void SwitchMember(bool next)
        {
            var sel = Game.Instance?.SelectionCharacter;
            var group = sel?.ActualGroup;
            if (group == null || group.Count == 0) return;
            int i = group.IndexOf(sel.SelectedUnitInUI?.Value);
            if (i < 0) i = next ? -1 : 0; // unknown current → land on the first/last on next/prev
            int target = ((i + (next ? 1 : -1)) % group.Count + group.Count) % group.Count;
            sel.SetSelected(group[target]);
        }

        /// <summary>Switch the viewed member to a roster slot directly (0-based; Alt+1..6).</summary>
        public static void SwitchTo(int index)
        {
            var sel = Game.Instance?.SelectionCharacter;
            var group = sel?.ActualGroup;
            if (group == null || index < 0 || index >= group.Count) return;
            sel.SetSelected(group[index]);
        }

        /// <summary>"{name}, level {n}, {cur} of {max} wounds" — the header a sighted player reads by the portrait.</summary>
        public static string HeaderLine(BaseUnitEntity unit)
        {
            if (unit == null) return "";
            var h = unit.Health;
            string wounds = h != null
                ? Loc.T("char.wounds_short", new { cur = h.HitPointsLeft, max = h.MaxHitPoints })
                : "";
            return Loc.T("char.header", new { name = unit.CharacterName, level = unit.Progression.CharacterLevel, wounds });
        }

        // ---- pet / master swap (the game's m_PetButton; pets are off the Shift+A/D roster) ----

        public static bool HasPetAxis(BaseUnitEntity unit) => unit != null && (unit.IsPet || unit.IsMaster);

        public static string PetLabel(BaseUnitEntity unit)
            => Loc.T(unit != null && unit.IsPet ? "char.swap_master" : "char.swap_pet");

        public static void SwapPet(BaseUnitEntity unit)
        {
            if (unit == null) return;
            if (unit.IsPet)
            {
                if (unit.Master != null) Game.Instance.SelectionCharacter.SetSelected(unit.Master);
            }
            else
            {
                var pet = unit.GetOptional<UnitPartPetOwner>()?.PetUnit;
                if (pet != null) Game.Instance.SelectionCharacter.SetSelected(pet);
            }
        }
    }
}
