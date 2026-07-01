using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Utils; // ILocalMapMarker
using RTAccess.Accessibility;                                   // InteractableDescriber (MarkerTypeLabel)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A scannable local-map landmark — an area exit/transition or a point of interest (loot, objective, important
/// thing). Unlike <see cref="ProxyMapObject"/> (a world interactable within reach), landmarks come from the game's
/// area-wide <see cref="LocalMapModel.Markers"/> set — the same markers the local map and
/// <see cref="RTAccess.Accessibility.LandmarkNav"/> show — so the scanner's Exits / points-of-interest review
/// groups (V / B) can browse the whole area, sort from the cursor, and hand a world position to Home-plant.
///
/// Landmarks are not reach-interactables: you travel TO one (Home-plant then Backspace, or LandmarkNav's walk key),
/// so <see cref="ScanItem.Interact"/> stays the base no-op and the scanner routes the I key to a guiding hint
/// instead. The spoken line is composed by the base <see cref="ScanItem.Describe"/> from <see cref="Name"/> +
/// <see cref="Detail"/>, which reproduces <see cref="InteractableDescriber.DescribeMarker"/> verbatim
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

    // The type word ("exit" / "point of interest" / "loot" / "objective" / "important") — the base Describe slots
    // it after the name, giving the same line LandmarkNav speaks via InteractableDescriber.DescribeMarker.
    public override string Detail => InteractableDescriber.MarkerTypeLabel(_marker.GetMarkerType());

    // Inert: landmark items are sourced by Scanner.MarkerList, never the WorldModel-backed category browse
    // (CategoryList / InGroup), so Primary/Nodes are never consulted — provided only to satisfy the abstract contract.
    public override string Primary => ScanTaxonomy.Exits;
    public override IEnumerable<string> Nodes { get { yield return ScanTaxonomy.Exits; } }
}
