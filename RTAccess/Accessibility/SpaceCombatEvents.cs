using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.PubSubSystem;                      // EventInvokerExtensions (the raising ship)
using Kingmaker.SpaceCombat.StarshipLogic.Parts;   // StarshipSectorShieldsType
using Kingmaker.UnitLogic.Buffs;                   // Buff (post block/unblock payloads)
using Warhammer.SpaceCombat;                       // IShieldAbsorbsDamageHandler, IStarshipPostHandler
using Warhammer.SpaceCombat.StarshipLogic.Posts;   // Post

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
internal sealed class SpaceCombatEvents : IShieldAbsorbsDamageHandler, IStarshipPostHandler
{
    internal static readonly SpaceCombatEvents Instance = new SpaceCombatEvents();

    // Posts we announced as blocked — so the unblock cue fires only for a real block ending
    // (HandleBuffDidRemoved is raised for EVERY post-buff removal, blocking or not).
    private readonly HashSet<Post> _blockedAnnounced = new HashSet<Post>();

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

    // ---- Post block/unblock cues (Phase 4). The sighted panel shows a lock overlay + duration on the
    // post portrait; nothing reaches the combat log, so these are typed cues like the shield blips.
    // Player-ship posts only (enemy posts have no sighted readout either — parity).

    public void HandlePostBlocked(Post post)
    {
        try
        {
            if (post == null || !ReferenceEquals(post.Ship, Game.Instance?.Player?.PlayerShip)) return;
            if (!post.IsBlocked) return;   // mirror ShipPostVM: the handler re-reads the live state
            _blockedAnnounced.Add(post);
            CombatEvents.Instance.EnqueueLogLine(Loc.T("spacecombat.post_blocked_cue",
                new { post = RTAccess.Screens.SpaceCombatScreen.PostTitle(post) }));
        }
        catch (Exception e) { Main.Log?.Log("post blocked cue failed: " + e.Message); }
    }

    public void HandleBuffDidAdded(Post post, Buff buff) { }

    public void HandleBuffDidRemoved(Post post, Buff buff)
    {
        try
        {
            if (post == null || post.BlockingBuff != null) return;   // still blocked by another buff
            if (!_blockedAnnounced.Remove(post)) return;             // never announced a block for it
            if (!ReferenceEquals(post.Ship, Game.Instance?.Player?.PlayerShip)) return;
            CombatEvents.Instance.EnqueueLogLine(Loc.T("spacecombat.post_freed_cue",
                new { post = RTAccess.Screens.SpaceCombatScreen.PostTitle(post) }));
        }
        catch (Exception e) { Main.Log?.Log("post freed cue failed: " + e.Message); }
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
