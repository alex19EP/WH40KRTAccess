using Access.Core;          // TextUtil
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
/// a click), so <see cref="ScanItem.Interact"/> stays the base no-op. The scanner's I key instead resolves the real
/// interactable the pin SITS ON (<c>Activation.TryCursorObject</c> at the pin's position — a loot pin marks a corpse
/// or a container) and acts on that; only a pin with nothing actionable under it falls back to walking the party
/// toward it (see <c>Scanner.TravelTo</c>). The
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

    /// <summary>The pin's kind as the game classifies it — read by the local map's exits-only cycle, which is a
    /// filter over the same pin set rather than a second source.</summary>
    public LocalMapMarkType MarkType
    {
        get { try { return _marker.GetMarkerType(); } catch { return LocalMapMarkType.Invalid; } }
    }

    // The type word ("point of interest" / "loot" / "objective" / "important") — the base Describe slots it after
    // the name, giving the marker readout line composed by InteractableDescriber.DescribeMarker.
    public override string Detail => InteractableDescriber.MarkerTypeLabel(_marker.GetMarkerType());

    // Inert: landmark items are sourced by Scanner.MarkerList (the marker-backed "Points of interest" category),
    // never matched against a taxonomy predicate, so Primary/Nodes are never consulted — provided only to satisfy
    // the abstract contract.
    public override string Primary => ScanTaxonomy.Exits;
    public override IEnumerable<string> Nodes { get { yield return ScanTaxonomy.Exits; } }

    /// <summary>
    /// May this pin be listed at all — the one spoiler gate every marker-sourced browse shares (the scanner's
    /// "Points of interest" category and the local map's marker/exit cycles), so the two can never drift into
    /// disagreeing about which pins a blind player is allowed to hear about.
    ///
    /// <c>IsVisible()</c> is the game's own per-pin perception check (the same one that decides whether the
    /// sighted map draws the icon), and <c>Suppressed</c> drops a pin whose owning entity has been switched off.
    /// Both are guarded: a marker whose check throws is treated as HIDDEN, the safe side for a loot pin.
    /// </summary>
    internal static bool Listable(ILocalMapMarker m)
    {
        if (m == null) return false;
        try { if (!m.IsVisible()) return false; }
        catch { return false; }
        try { return m.GetEntity()?.Suppressed != true; }
        catch { return false; }
    }
}
