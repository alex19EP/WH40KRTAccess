using System.Text;
using Kingmaker.EntitySystem.Entities; // BaseUnitEntity (TargetUnit), MechanicEntity (TargetEntity)
using RTAccess.Accessibility; // InteractableDescriber (name/verb/compass reuse)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// One thing the scanner can list: a stable identity key (the backing entity), a name, a world position, the
/// taxonomy nodes it belongs to, and the single state-aware role it sounds/cycles as. Visibility is a live
/// per-item lens: <see cref="IsVisible"/> is reveal-latched ("we know it's here", the area-wide scanner), while
/// <see cref="CurrentlySeen"/> is "can perceive it right now" (the tactical review cycles). A thing also has a
/// spatial extent (<see cref="Bounds"/> / <see cref="Footprint"/>): distance and bearing report the nearest PART
/// of it (its <see cref="NearestPoint"/>), so a large creature or wide area effect reads by its nearest edge, not
/// its centre. <see cref="Describe"/> composes the spoken line relative to a reference point, reusing
/// <see cref="InteractableDescriber"/> for the distance + compass so the scanner matches the other navigators.
/// </summary>
internal abstract class ScanItem
{
    /// <summary>The backing engine entity — stable across the per-action rebuilds, so the scanner can re-find
    /// the user's selection by reference even though proxy instances are recreated each press.</summary>
    public abstract object Key { get; }

    public abstract string Name { get; }
    public abstract Vector3 Position { get; }

    /// <summary>The taxonomy node keys this thing belongs to (a thing can be in several).</summary>
    public abstract IEnumerable<string> Nodes { get; }

    /// <summary>The single state-aware node this thing primarily is right now (faction for units, role for
    /// objects). Drives the party/enemies/neutrals review cycles.</summary>
    public abstract string Primary { get; }

    /// <summary>Reveal-latched knowledge: listed when the player could know it's here (the area-wide scanner).</summary>
    public virtual bool IsVisible => true;

    /// <summary>Can the player perceive it right now (not in fog) — used by the review cycles.</summary>
    public virtual bool CurrentlySeen => true;

    // Small tolerance so a target grazing a corner/edge still reads as detectable (matches WrathAccess's LosFudge).
    private const float LosFudge = 0.2f;

    /// <summary>Cycle-visibility for the review cycles (M / comma / period / N): currently seen, OR a remembered
    /// (reveal-latched) thing under fog that has a CLEAR line of sight from <paramref name="cursor"/> — so a chest
    /// behind a wall isn't offered until you'd actually have a straight path to it. The category browse stays
    /// reveal-latched on <see cref="IsVisible"/> ("everything you know is here"); this is the narrower "what can I
    /// make out from here" gate. Ported from WrathAccess (ScanItem.DetectableFrom); the fog case uses the game's own
    /// line-of-sight geometry (<c>LineOfSightGeometry.HasObstacle</c>). A null LoS system (areas without one) admits;
    /// a failed check refuses (conservative — don't re-admit a fogged thing we can't clear).</summary>
    public bool DetectableFrom(Vector3 cursor)
    {
        if (!IsVisible) return false;
        if (CurrentlySeen) return true;
        try
        {
            var los = Kingmaker.Controllers.FogOfWar.LineOfSight.LineOfSightGeometry.Instance;
            return los == null || !los.HasObstacle(cursor, NearestPoint(cursor), LosFudge);
        }
        catch { return false; }
    }

    /// <summary>The state/qualifier words spoken after the name (faction + condition for units, verb + open
    /// state for objects); null or empty when there is nothing to add.</summary>
    public virtual string Detail => null;

    /// <summary>Interact with this thing (loot/open/transition). Base: not interactable.</summary>
    public virtual bool Interact() => false;

    /// <summary>Whether this thing is an actionable interactable right now — the game's own availability gate. Base:
    /// no (units, area effects, landmarks). Only <see cref="ProxyMapObject"/> overrides, mirroring the same
    /// HasAvailableInteractions / area-transition gate the cursor's Enter uses (see
    /// <see cref="RTAccess.Accessibility.InteractableDescriber.InteractableAt"/>), so the scanner's I key and the
    /// cursor's Enter agree on which objects can be acted on. Lets the scanner interact with the review selection
    /// only when it is actionable and otherwise fall back to the object at the cursor, without a type check.</summary>
    public virtual bool CanInteract => false;

    /// <summary>Whether this thing IS a unit — the cheap predicate the cursor-target resolver filters on.</summary>
    public virtual bool IsUnit => false;

    /// <summary>The unit this thing IS (for the game's unit-targeted ability click, which wants the unit's
    /// <c>GameObject</c>); null for anything that isn't a unit — targeting then falls back to the world point.</summary>
    public virtual BaseUnitEntity TargetUnit => null;

    /// <summary>The mechanic entity this thing IS as an ability target — a unit, or attackable destructible
    /// scenery (a fuel tank / wall with hit points). The aim commit passes its view's GameObject to the game's
    /// own click handler, which resolves any <c>MechanicEntityView</c>, so anything returned here is targeted
    /// exactly like a hovered click. Null for non-targetable things (markers, zones) — targeting then falls
    /// back to the world point. Default: the unit; <c>ProxyDestructible</c> widens.</summary>
    public virtual MechanicEntity TargetEntity => TargetUnit;

    /// <summary>Whether this thing is a DEAD unit (a corpse). Corpses are dropped from the party/enemy/neutral review
    /// cycles and the unit category browse (you don't cycle to the dead — the game's own enemy navigation gates the
    /// same <c>!LifeState.IsDead</c>), but they STAY in the registry: the tile cursor still reads a corpse (labelled
    /// "dead") and the deliberate cursor/selection commit still resolves it, so the game can still validate the rare
    /// corpse-targeting ability. Downed-but-unconscious (revivable) units are NOT dead. Non-units and living units are
    /// never dead; overridden by <c>ProxyUnit</c>.</summary>
    public virtual bool IsDead => false;

    /// <summary>Whether this thing is a DEAD unit that still has loot to take (the game's <c>IsDeadAndHasLoot</c>) and
    /// can be looted right now (out of combat). Such a corpse is surfaced like a container — it leaves the faction
    /// cycles and appears in the Corpses category / object cycle, and <see cref="Interact"/> loots it — so a blind
    /// player reaches a body the same way they reach a chest (mirrors WrathAccess). An emptied corpse is never
    /// lootable, so it drops out of the scanner entirely. Only <c>ProxyUnit</c> overrides.</summary>
    public virtual bool LootableCorpse => false;

    public bool HasNode(string key)
    {
        foreach (var n in Nodes)
        {
            if (n == key) return true;
        }
        return false;
    }

    /// <summary>
    /// XZ radius of the thing's footprint, in world metres. Large creatures/objects span several tiles, so the
    /// scanner reports distance/bearing to the nearest EDGE, not the centre. Default 0 = a point (markers); units
    /// and map objects override with their <c>Corpulence</c>.
    /// </summary>
    public virtual float Footprint => 0f;

    /// <summary>The thing's spatial extent — a circle of <see cref="Footprint"/> (a point when 0). Shaped things
    /// (area effects, wide doorways) override so distance/bearing report the nearest PART while the cursor still
    /// targets the centre.</summary>
    public virtual ScanBounds Bounds
        => Footprint > 0f ? ScanBounds.Circle(Position, Footprint) : ScanBounds.Point(Position);

    /// <summary>The closest point of the thing to <paramref name="from"/> (XZ), NON-ALLOCATING — for the per-frame
    /// lenses that run over every item each frame. Default: a circle of <see cref="Footprint"/> about the centre.
    /// Inside the footprint → the reference point itself (distance 0).</summary>
    public virtual Vector3 NearestPoint(Vector3 from) => ScanBounds.NearestOnCircleXZ(Position, Footprint, from);

    /// <summary>Is <paramref name="point"/> inside this thing's footprint (XZ)? — "the cursor is on it".</summary>
    public bool Contains(Vector3 point)
    {
        var np = NearestPoint(point);
        float dx = np.x - point.x, dz = np.z - point.z;
        return dx * dx + dz * dz < 1e-4f;
    }

    /// <summary>Distance from <paramref name="from"/> to the nearest part of the thing (its edge, not its centre).</summary>
    public float DistanceTo(Vector3 from) => Geo.Distance(from, NearestPoint(from));

    /// <summary>"&lt;name&gt;[, &lt;detail&gt;], &lt;distance&gt;, &lt;bearing&gt;[, &lt;combat suffix&gt;]" relative to a reference point.</summary>
    public string Describe(Vector3 from)
    {
        var sb = new StringBuilder();
        sb.Append(string.IsNullOrWhiteSpace(Name) ? "Unknown" : Name);
        var detail = Detail;
        if (!string.IsNullOrWhiteSpace(detail)) sb.Append(", ").Append(detail);
        sb.Append(", ").Append(InteractableDescriber.DirectionAndDistance(from, NearestPoint(from)));

        // In combat, append the tactical tail (cover-vs-me / LOS / in-range / threat) — but not while an ability is
        // armed, since the aiming-time HitPredictor line (appended by Scanner) already carries the richer, ability-
        // specific hit read and would double the cover/range. So: passive read when idle, prediction when aiming.
        if (Kingmaker.Game.Instance?.Player?.IsInCombat == true && !Targeting.Aiming)
        {
            var combat = CombatSuffix();
            if (!string.IsNullOrWhiteSpace(combat)) sb.Append(", ").Append(combat);
        }
        return sb.ToString();
    }

    /// <summary>Combat-only tactical tail (cover-vs-me / LOS / in-range / threat), appended after the bearing while
    /// the player is in combat and not aiming. Measured relative to the ACTING unit, so — unlike <see cref="Detail"/>
    /// (which only has the scan-origin point) — the override resolves the observer unit itself. Base: nothing.</summary>
    protected virtual string CombatSuffix() => null;
}
