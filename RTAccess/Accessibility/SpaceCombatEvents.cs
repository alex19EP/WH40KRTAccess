using System;
using Kingmaker;
using Kingmaker.PubSubSystem;                      // EventInvokerExtensions (the raising ship)
using Kingmaker.SpaceCombat.StarshipLogic.Parts;   // StarshipSectorShieldsType
using Warhammer.SpaceCombat;                       // IShieldAbsorbsDamageHandler

namespace RTAccess.Accessibility;

/// <summary>
/// Space-combat cues the game log does NOT carry (Phase 1 of
/// docs/plans/inertial-broadsiding-tsiolkovsky.md): the PLAYER ship's per-sector shield hits. The log's
/// attack line names only the damage totals ("attacks 2 times, 4 shield damage, 0 hull") — WHICH shield
/// took it is shown to a sighted player as a flash on the HUD's shield diamond, so the blind player needs
/// "starboard shields 62 of 70" spoken. A persistent EventBus subscriber (registered in <see cref="Main"/>
/// beside the other event readers); lines join <see cref="CombatEvents"/>' shared per-frame queue so they
/// interleave with the attack narration in arrival order (a two-shot volley reads both blips — accurate,
/// and each carries the running total).
///
/// Enemy shield drops stay un-cued on purpose: the attack line already carries the total, and the sighted
/// HUD gives enemies no per-sector readout either (only the overtip sum — parity, [[rt-visual-parity]]).
/// </summary>
internal sealed class SpaceCombatEvents : IShieldAbsorbsDamageHandler
{
    internal static readonly SpaceCombatEvents Instance = new SpaceCombatEvents();

    public void HandleShieldAbsorbsDamage(int before, int after, StarshipSectorShieldsType sector)
    {
        try
        {
            var ship = EventInvokerExtensions.StarshipEntity;
            if (ship == null || !ReferenceEquals(ship, Game.Instance?.Player?.PlayerShip)) return;
            int max = ship.Shields?.GetShields(sector)?.Max ?? 0;
            CombatEvents.Instance.EnqueueLogLine(Loc.T("spacecombat.shields_hit",
                new { sector = SectorWord(sector), cur = after, max }));
        }
        catch (Exception e) { Main.Log?.Log("shield cue failed: " + e.Message); }
    }

    internal static string SectorWord(StarshipSectorShieldsType sector)
    {
        switch (sector)
        {
            case StarshipSectorShieldsType.Fore: return Loc.T("spacecombat.sector_fore");
            case StarshipSectorShieldsType.Port: return Loc.T("spacecombat.sector_port");
            case StarshipSectorShieldsType.Starboard: return Loc.T("spacecombat.sector_starboard");
            default: return Loc.T("spacecombat.sector_aft");
        }
    }
}
