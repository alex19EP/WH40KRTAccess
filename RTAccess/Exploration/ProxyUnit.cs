using Kingmaker.EntitySystem.Entities; // BaseUnitEntity
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A scannable unit (party member, NPC, enemy). Faction decides its single primary node and the review group it
/// cycles under; the spoken detail is faction word + condition (dead/unconscious) or HP (+ in combat). Not
/// interactable in v1 (no talk/attack) — interaction is map-objects-only.
/// </summary>
internal sealed class ProxyUnit : ScanItem
{
    private readonly BaseUnitEntity _unit;

    public ProxyUnit(BaseUnitEntity unit) { _unit = unit; }

    public override object Key => _unit;

    public override string Name => _unit.CharacterName;

    public override Vector3 Position => _unit.Position;

    // Real footprint radius (metres) — a large creature reads distance/bearing to its nearest edge, not its centre,
    // and sorts by that edge, so an adjacent ogryn reads "here"/"1 metre" rather than its centre's 2 m.
    public override float Footprint => _unit.Corpulence;

    // Reveal-latched: the engine's per-unit "visible for player" flag (true for party, fog-gated for others).
    public override bool IsVisible => _unit.IsVisibleForPlayer;

    public override bool CurrentlySeen => _unit.IsVisibleForPlayer && !_unit.IsInFogOfWar;

    public override string Primary => FactionNode();

    public override IEnumerable<string> Nodes
    {
        get { yield return FactionNode(); }
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

    private string FactionNode()
        => _unit.IsPlayerFaction ? ScanTaxonomy.UnitsParty
         : _unit.IsPlayerEnemy ? ScanTaxonomy.UnitsEnemies
         : ScanTaxonomy.UnitsNeutrals;

    private string FactionWord()
        => _unit.IsPlayerFaction ? "ally"
         : _unit.IsPlayerEnemy ? "enemy"
         : "neutral";
}
