using Kingmaker;                                 // Game
using Kingmaker.Controllers.Clicks.Handlers;     // ClickUnitHandler (loot a corpse)
using Kingmaker.EntitySystem.Entities;           // BaseUnitEntity
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A scannable unit (party member, NPC, enemy). While alive, faction decides its single primary node and the review
/// group it cycles under; the spoken detail is faction word + condition (dead/unconscious) or HP (+ in combat).
/// Living units aren't interactable (no talk/attack) — interaction is map-objects-only. A DEAD unit that still has
/// loot is the one exception: it flips its primary node to <see cref="ScanTaxonomy.Corpses"/> (leaving the faction
/// cycles) and becomes interactable, so <c>I</c> loots it via the game's own unit click — a blind player reaches a
/// body exactly like a chest (mirrors WrathAccess; no dedicated corpse key). An emptied/lootless corpse carries no
/// node and drops out of the scanner entirely.
/// </summary>
internal sealed class ProxyUnit : ScanItem
{
    private readonly BaseUnitEntity _unit;

    public ProxyUnit(BaseUnitEntity unit) { _unit = unit; }

    public override object Key => _unit;

    public override string Name => _unit.CharacterName;

    public override Vector3 Position => _unit.Position;

    // This item IS a unit — the target the game's unit-targeted ability click wants.
    public override bool IsUnit => true;
    public override BaseUnitEntity TargetUnit => _unit;

    // Real footprint radius (metres) — a large creature reads distance/bearing to its nearest edge, not its centre,
    // and sorts by that edge, so an adjacent ogryn reads "here"/"1 metre" rather than its centre's 2 m.
    public override float Footprint => _unit.Corpulence;

    // The player's OWN party is always known (the game always shows your party on the map), even though the engine's
    // IsVisibleForPlayer flag reports false for owned units when they aren't the current "spotlight" (out of combat /
    // not the acting unit). So player-faction units are always listed and always "seen"; everyone else is
    // reveal-latched on IsVisibleForPlayer and fog-gated for the review cycles.
    public override bool IsVisible => _unit.IsPlayerFaction || _unit.IsVisibleForPlayer;

    public override bool CurrentlySeen => _unit.IsPlayerFaction || (_unit.IsVisibleForPlayer && !_unit.IsInFogOfWar);

    // A corpse — dropped from the party/enemy/neutral review cycles and the unit category browse (Scanner), matching
    // the game's own enemy navigation (SurfaceCombatInputLayer.IsValidEnemy gates the same !LifeState.IsDead). Death
    // is State==Dead only, so downed-but-unconscious (revivable) companions are NOT corpses and stay listed/inspectable.
    public override bool IsDead => _unit.LifeState.IsDead;

    // A dead unit that still has loot and can be looted now (out of combat) — the game's own gate: looting a body
    // is blocked in combat (ClickUnitHandler), and a dead unit isn't even clickable then. So in combat a corpse is
    // simply absent from the scanner; out of combat, a lootable body appears (and an emptied one never does).
    public override bool LootableCorpse
        => _unit.IsDeadAndHasLoot && Game.Instance?.Player?.IsInCombat != true;

    // A lootable corpse classifies as a CORPSE (a container-like thing), not by faction — that pulls it out of the
    // party/enemy/neutral cycles (which key on Primary) and into the Corpses category / object cycle. Living units
    // (and dead-but-lootless ones, which the scanner filters out anyway) keep their faction node.
    public override string Primary => LootableCorpse ? ScanTaxonomy.Corpses : FactionNode();

    public override IEnumerable<string> Nodes
    {
        get { yield return Primary; }
    }

    // A lootable corpse is an actionable interactable (like a chest), so the scanner's generic I acts on it; a
    // living unit / emptied corpse is not.
    public override bool CanInteract => LootableCorpse;

    // Loot the body through the game's OWN unit-click dispatch — the same context-sensitive handler a mouse click
    // hits, which for a dead-and-has-loot unit out of combat walks the nearest selected party member over and opens
    // its loot window (the ShortUnit LootScreen). Reused verbatim (as ProxyMapObject reuses ClickMapObjectHandler),
    // so approach, coop, and acting-unit selection all match the sighted click. Mirrors WrathAccess's ProxyUnit.
    public override bool Interact()
    {
        var view = _unit.View;
        if (view == null) return false;
        return new ClickUnitHandler().OnClick(view.gameObject, view.transform.position, 0);
    }

    public override string Detail
    {
        get
        {
            try
            {
                var bits = new List<string> { FactionWord() };
                var life = _unit.LifeState;
                if (life.IsDead)
                {
                    bits.Add("dead");
                }
                else if (!life.IsConscious)
                {
                    bits.Add("unconscious");
                }
                else
                {
                    var health = _unit.Health;
                    if (health != null) bits.Add(health.HitPointsLeft + " of " + health.MaxHitPoints + " HP");
                    if (_unit.IsInCombat) bits.Add("in combat");
                }
                return string.Join(", ", bits);
            }
            catch
            {
                return FactionWord();
            }
        }
    }

    // The passive tactical tail for an enemy, relative to the acting unit (whose turn it is, else the selected
    // unit). Enemies only; dead enemies carry nothing tactical. The heavy read lives in CombatReads (shared with
    // the battlefield summary); this just resolves the observer and the enemy/dead gate.
    protected override string CombatSuffix()
    {
        try
        {
            if (!_unit.IsPlayerEnemy || _unit.LifeState.IsDead) return null;
            var me = Game.Instance?.TurnController?.CurrentUnit as BaseUnitEntity
                     ?? Game.Instance?.SelectionCharacter?.SelectedUnit?.Value;
            return RTAccess.Accessibility.CombatReads.CoverRangeThreat(me, _unit);
        }
        catch { return null; }
    }

    private string FactionNode()
        => _unit.IsPlayerFaction ? ScanTaxonomy.UnitsParty
         : _unit.IsPlayerEnemy ? ScanTaxonomy.UnitsEnemies
         : ScanTaxonomy.UnitsNeutrals;

    private string FactionWord()
        => _unit.IsPlayerFaction ? "ally"
         : _unit.IsPlayerEnemy ? "enemy"
         : "neutral";
}
