using System.Text;
using Kingmaker.EntitySystem.Entities; // BaseUnitEntity (TargetUnit)
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

    /// <summary>The state/qualifier words spoken after the name (faction + condition for units, verb + open
    /// state for objects); null or empty when there is nothing to add.</summary>
    public virtual string Detail => null;

    /// <summary>Interact with this thing (loot/open/transition). Base: not interactable.</summary>
    public virtual bool Interact() => false;

    /// <summary>Whether this thing IS a unit — the cheap predicate the cursor-target resolver filters on.</summary>
    public virtual bool IsUnit => false;

    /// <summary>The unit this thing IS (for the game's unit-targeted ability click, which wants the unit's
    /// <c>GameObject</c>); null for anything that isn't a unit — targeting then falls back to the world point.</summary>
    public virtual BaseUnitEntity TargetUnit => null;

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

    /// <summary>"&lt;name&gt;[, &lt;detail&gt;], &lt;distance&gt;, &lt;bearing&gt;" relative to a reference point.</summary>
    public string Describe(Vector3 from)
    {
        var sb = new StringBuilder();
        sb.Append(string.IsNullOrWhiteSpace(Name) ? "Unknown" : Name);
        var detail = Detail;
        if (!string.IsNullOrWhiteSpace(detail)) sb.Append(", ").Append(detail);
        sb.Append(", ").Append(InteractableDescriber.DirectionAndDistance(from, NearestPoint(from)));
        return sb.ToString();
    }
}
