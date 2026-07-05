using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Parts;   // UnitPartPetOwner
using RTAccess.Speech;

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Shared read of the unit a full-screen service window (Inventory / Character Info) is SHOWING — the
    /// game's <c>SelectionCharacter.SelectedUnitInUI</c>, switched by the game's own Shift+A / Shift+D (which
    /// we no longer swallow, type-ahead being off in those screens).
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
