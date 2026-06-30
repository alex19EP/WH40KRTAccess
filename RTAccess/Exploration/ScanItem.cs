using System.Text;
using RTAccess.Accessibility; // InteractableDescriber (name/verb/compass reuse)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// One thing the scanner can list: a stable identity key (the backing entity), a name, a world position, the
/// taxonomy nodes it belongs to, and the single state-aware role it sounds/cycles as. Visibility is a live
/// per-item lens: <see cref="IsVisible"/> is reveal-latched ("we know it's here", the area-wide scanner), while
/// <see cref="CurrentlySeen"/> is "can perceive it right now" (the tactical review cycles). <see cref="Describe"/>
/// composes the spoken line relative to a reference point, reusing <see cref="InteractableDescriber"/> for the
/// distance + compass so the scanner matches the other navigators.
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

    public bool HasNode(string key)
    {
        foreach (var n in Nodes)
        {
            if (n == key) return true;
        }
        return false;
    }

    public float DistanceTo(Vector3 from) => Geo.Distance(from, Position);

    /// <summary>"&lt;name&gt;[, &lt;detail&gt;], &lt;distance&gt;, &lt;bearing&gt;" relative to a reference point.</summary>
    public string Describe(Vector3 from)
    {
        var sb = new StringBuilder();
        sb.Append(string.IsNullOrWhiteSpace(Name) ? "Unknown" : Name);
        var detail = Detail;
        if (!string.IsNullOrWhiteSpace(detail)) sb.Append(", ").Append(detail);
        sb.Append(", ").Append(InteractableDescriber.DirectionAndDistance(from, Position));
        return sb.ToString();
    }
}
