using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.FactLogic;   // TransientPartyMemberFlag
using RTAccess.Speech;

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Passively announces "{name} can level up" the moment a party member becomes eligible — the game gives
    /// only a silent portrait badge, so a blind player would never know a level-up is waiting. Mirrors the
    /// game's own eligibility (<c>PartyCharacterVM.IsLevelUp</c>): out of combat, <c>CanLevelUp</c>, and not a
    /// transient guest.
    ///
    /// <para>Announces only on a false → true transition per unit (tracked in <see cref="_known"/>), so it
    /// never bursts on load or save-load: a unit's FIRST observation is recorded silently, and only a later
    /// flip to eligible speaks. Passive (never interrupts — carried SayTheSpire preference). Reaching the
    /// level-up itself is the "Level Up" action on the character sheet (<see cref="Screens.CharacterInfoScreen"/>
    /// → <see cref="Screens.LevelUpScreen"/>). See docs/plans/ranked-ascending-lamport.md.</para>
    /// </summary>
    internal static class LevelUpAnnouncer
    {
        private static readonly Dictionary<BaseUnitEntity, bool> _known = new Dictionary<BaseUnitEntity, bool>();

        /// <summary>Poll the party's level-up eligibility once per frame (from Main.OnUpdate).</summary>
        public static void Tick()
        {
            var party = Game.Instance?.Player?.Party;
            if (party == null) return;

            var seen = new HashSet<BaseUnitEntity>();
            foreach (var u in party)
            {
                if (u == null || u.IsDisposed) continue;
                seen.Add(u);
                bool cur = Eligible(u);
                if (_known.TryGetValue(u, out bool prev) && !prev && cur) Announce(u);
                _known[u] = cur;
            }

            // Drop units that left the party so the dictionary can't grow unbounded across areas/reloads.
            if (_known.Count > seen.Count)
                foreach (var k in _known.Keys.Where(k => !seen.Contains(k)).ToList())
                    _known.Remove(k);
        }

        private static bool Eligible(BaseUnitEntity u)
        {
            try
            {
                return !u.IsInCombat && u.Progression.CanLevelUp && !u.Facts.HasComponent<TransientPartyMemberFlag>();
            }
            catch { return false; }
        }

        private static void Announce(BaseUnitEntity u)
        {
            try { Speaker.Speak(Loc.T("levelup.available", new { name = u.CharacterName }), interrupt: false); }
            catch (System.Exception e) { Main.Log?.Log("LevelUpAnnouncer failed: " + e.Message); }
        }
    }
}
