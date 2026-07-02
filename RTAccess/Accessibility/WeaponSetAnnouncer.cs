using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.Common;   // IsDirectlyControllable()
using RTAccess.Speech;

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Speaks the new active weapon set (index + the weapons now in hand) when the controlled unit swaps sets.
    /// The game gives NO audio on a weapon-set swap, so once the P/X/R relocation freed Ctrl+X for the game's
    /// ChangeWeaponSet, a blind player could swap but had no confirmation of the swap or which weapons are active.
    ///
    /// <para>Polls the APPLIED state (<c>Body.CurrentHandEquipmentSetIndex</c>) of the controlled unit — the
    /// acting unit during the player's turn-based turn, else the selected unit — so it reads the truth directly
    /// (no event-scoping guesswork) and is unit-correct. Tracks (unit, index): announces only when the SAME
    /// unit's index changes; a unit switch re-syncs silently (<see cref="SelectionAnnouncer"/> covers that).
    /// Covers every swap path (Ctrl+X, the HUD weapon-set panel). Announces in all modes — a combat swap needs the
    /// confirmation just as much — and interrupts, since the swap is a keypress outcome ([[rt-interrupt-speech-rule]]).</para>
    /// </summary>
    internal static class WeaponSetAnnouncer
    {
        private static BaseUnitEntity _unit;
        private static int _index = -1;

        /// <summary>Poll the controlled unit's active weapon set once per frame (from Main.OnUpdate).</summary>
        public static void Tick()
        {
            var u = ControlUnit();
            if (u == null || !u.IsDirectlyControllable()) { _unit = null; _index = -1; return; }
            var body = u.Body;
            if (body == null) { _unit = u; _index = -1; return; }
            int idx = body.CurrentHandEquipmentSetIndex;
            if (!ReferenceEquals(u, _unit)) { _unit = u; _index = idx; return; } // unit switch → sync, no announce
            if (idx == _index) return;
            _index = idx;
            Announce(u);
        }

        // The unit whose weapon set the swap affects: the acting unit during the player's turn-based turn (when the
        // selection may be cleared), else the selected unit.
        private static BaseUnitEntity ControlUnit()
        {
            var tc = Game.Instance?.TurnController;
            if (tc != null && tc.TurnBasedModeActive && tc.CurrentUnit is BaseUnitEntity acting) return acting;
            return Game.Instance?.SelectionCharacter?.SelectedUnit?.Value;
        }

        private static void Announce(BaseUnitEntity unit)
        {
            try
            {
                var set = unit.Body?.CurrentHandsEquipmentSet;
                var primary = set?.PrimaryHand?.MaybeWeapon;
                var secondary = set?.SecondaryHand?.MaybeWeapon;
                string weapons;
                if (primary == null && secondary == null) weapons = Loc.T("weaponset.unarmed");
                else if (primary != null && secondary != null && !ReferenceEquals(primary, secondary))
                    weapons = primary.Name + ", " + secondary.Name;
                else weapons = (primary ?? secondary).Name;
                Speaker.Speak(Loc.T("weaponset.changed", new { index = _index + 1, weapons }), interrupt: true);
            }
            catch (Exception e) { Main.Log?.Log("WeaponSetAnnouncer failed: " + e.Message); }
        }
    }
}
