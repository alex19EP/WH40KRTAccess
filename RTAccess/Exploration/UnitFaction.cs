using Kingmaker;                                 // Game
using Kingmaker.EntitySystem.Entities;           // BaseUnitEntity
using Kingmaker.UI.Common;                       // IsDirectlyControllable (ext)

namespace RTAccess.Exploration;

/// <summary>
/// How a living unit is classified for the scanner and the tile cursor: MY PARTY (the units the player
/// commands right now), an ALLY (friendly, but not mine to command), an ENEMY, or a NEUTRAL.
///
/// <b>Player faction is NOT "my party"</b> — conflating the two is what made a hub area read wrong. In a
/// capital area (<c>AreaPersistentState.Settings.CapitalPartyMode</c> — the Rogue Trader's palace, verified
/// in-harness) the game hands the player the main character ALONE and leaves the companions standing around as
/// ambient player-faction NPCs; a faction-only test then announced four characters the player had neither
/// chosen nor could command as "your party". Summons, pets of NPCs and scripted temporary allies are player
/// faction too.
///
/// The party test therefore mirrors the game's OWN controllable group,
/// <c>Game.Instance.SelectionCharacter.ActualGroup</c> — the very list the game's party HUD binds to
/// (<c>PartyVM.ActualGroup</c>), built by <c>UIUtility.GetGroup</c> as "party members, view-active while on the
/// surface, plus pets". It is also what the mod's own HUD party zone and the P roster show, so the scanner,
/// the HUD and the roster can no longer disagree about who is in the party.
/// </summary>
internal static class UnitFaction
{
    /// <summary>The game's own controllable group (party + pets, present in this area), or null if unreadable.</summary>
    internal static List<BaseUnitEntity> Group()
    {
        try { return Game.Instance?.SelectionCharacter?.ActualGroup; }
        catch { return null; }
    }

    /// <summary>Is this unit one of the party members the player actually commands right now?</summary>
    internal static bool InParty(BaseUnitEntity unit)
    {
        if (unit == null) return false;
        var group = Group();
        return group != null && group.Contains(unit);
    }

    /// <summary>The party roster the player can select and command, in the game's own party order — the
    /// controllable group filtered the way the game's own selection cycle filters it
    /// (<c>PartyVM.GetSelectableUnits</c>): directly-controllable units plus pets. This is the list the HUD
    /// party zone lists and the Shift+A/D / Alt+1..6 hotkeys step through, so both mirror the game's party
    /// panel — including pets (which <c>Player.Party</c> omits) and excluding roster members who are not
    /// present in this area (which <c>Player.Party</c> still contains while on the surface).</summary>
    internal static List<BaseUnitEntity> Roster()
    {
        var list = new List<BaseUnitEntity>();
        var group = Group();
        if (group == null) return list;
        foreach (var u in group)
            if (u != null && (u.IsDirectlyControllable() || u.IsPet)) list.Add(u);
        return list;
    }

    /// <summary>The taxonomy node a living unit scans under.</summary>
    internal static string Node(BaseUnitEntity unit)
        => unit == null ? ScanTaxonomy.UnitsNeutrals
         : unit.IsPlayerFaction ? (InParty(unit) ? ScanTaxonomy.UnitsParty : ScanTaxonomy.UnitsAllies)
         : unit.IsPlayerEnemy ? ScanTaxonomy.UnitsEnemies
         : ScanTaxonomy.UnitsNeutrals;

    /// <summary>The spoken faction word — "party member" / "ally" / "enemy" / "neutral".</summary>
    internal static string Word(BaseUnitEntity unit)
    {
        switch (Node(unit))
        {
            case ScanTaxonomy.UnitsParty:   return Loc.T("scan.faction.party");
            case ScanTaxonomy.UnitsAllies:  return Loc.T("scan.faction.ally");
            case ScanTaxonomy.UnitsEnemies: return Loc.T("scan.faction.enemy");
            default:                        return Loc.T("scan.faction.neutral");
        }
    }
}
