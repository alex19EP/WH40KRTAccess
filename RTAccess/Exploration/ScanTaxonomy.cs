namespace RTAccess.Exploration;

/// <summary>
/// The minimal v1 classification of scannable things — node keys that <see cref="ScanItem.Nodes"/> and
/// <see cref="ScanItem.Primary"/> return, kept as constants to avoid magic strings. Units split by faction;
/// interactable map objects fall into one coarse bucket each. Deliberately flat for v1 (no sub-types, sounds,
/// or settings tree) — the categorized browse and the review cycles index directly on these keys.
/// </summary>
internal static class ScanTaxonomy
{
    public const string UnitsParty = "units.party";
    public const string UnitsEnemies = "units.enemies";
    public const string UnitsNeutrals = "units.neutrals";
    public const string Containers = "containers";
    public const string Doors = "doors";
    public const string Exits = "exits";
    public const string SearchPoints = "searchpoints";
    public const string Traps = "traps";
    public const string Mechanisms = "mechanisms";
    public const string Scenery = "scenery";
    // Live area effects (Game.Instance.State.AreaEffects), surfaced by ProxyAreaEffect: a HAZARD is any zone that
    // can catch enemies (a damage/debuff field — route around it); a BUFF ZONE is an ally-exclusive aura.
    public const string Hazards = "hazards";
    public const string BuffZones = "zones.buff";
}
