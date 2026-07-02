using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Utils; // ILocalMapMarker
using RTAccess.Accessibility;                                   // InteractableDescriber (MarkerTypeLabel)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A scannable local-map landmark — a point of interest (loot, objective, important thing). Unlike
/// <see cref="ProxyMapObject"/> (a world interactable within reach), landmarks come from the game's area-wide
/// <see cref="LocalMapModel.Markers"/> set — the same markers the local map shows — so the scanner's
/// "Points of interest" category can browse the whole area, sort from the cursor, and hand a world position to
/// Home-plant. (Area exits are surfaced as their real, activatable world objects in the Exits category, not as
/// these marker pins.)
///
/// Landmarks are not reach-interactables — the game's own map pin isn't clickable (verified: no marker view handles
/// a click), so the only thing a landmark supports is travelling to it. <see cref="ScanItem.Interact"/> stays the
/// base no-op; the scanner's I key walks the party toward the marker instead (see <c>Scanner.TravelTo</c>). The
/// spoken line is composed by the base <see cref="ScanItem.Describe"/> from <see cref="Name"/> + <see cref="Detail"/>,
/// which reproduces <see cref="InteractableDescriber.DescribeMarker"/> verbatim
/// ("&lt;description&gt;, &lt;type&gt;, &lt;distance&gt;, &lt;bearing&gt;").
/// </summary>
internal sealed class ProxyMarker : ScanItem
{
    private readonly ILocalMapMarker _marker;

    public ProxyMarker(ILocalMapMarker marker) { _marker = marker; }

    // The marker instance is the stable identity: LocalMapModel.Markers holds the same object across the per-press
    // list rebuilds within an area, so ReferenceEquals selection tracking (IndexOfSelected) survives the rebuild.
    public override object Key => _marker;

    public override Vector3 Position => _marker.GetPosition();

    public override string Name
    {
        get
        {
            try { var n = TextUtil.StripRichText(_marker.GetDescription()); return string.IsNullOrWhiteSpace(n) ? "Landmark" : n; }
            catch { return "Landmark"; }
        }
    }

    // The type word ("point of interest" / "loot" / "objective" / "important") — the base Describe slots it after
    // the name, giving the marker readout line composed by InteractableDescriber.DescribeMarker.
    public override string Detail => InteractableDescriber.MarkerTypeLabel(_marker.GetMarkerType());

    // Inert: landmark items are sourced by Scanner.MarkerList (the marker-backed "Points of interest" category),
    // never matched against a taxonomy predicate, so Primary/Nodes are never consulted — provided only to satisfy
    // the abstract contract.
    public override string Primary => ScanTaxonomy.Exits;
    public override IEnumerable<string> Nodes { get { yield return ScanTaxonomy.Exits; } }
}
