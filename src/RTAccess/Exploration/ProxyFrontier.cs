using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// One frontier blob (<see cref="FrontierModel.Blob"/>) as a scanner item — the "Unexplored space" category.
/// Base <see cref="ScanItem.IsVisible"/>/<see cref="ScanItem.CurrentlySeen"/> stay <c>true</c>: the blob IS the
/// fog edge, the player's own map knowledge, so no fog gate applies (a sighted player sees exactly this boundary
/// on screen). Keys on the blob object, which survives recomputes while its opening persists, so the selection
/// holds across presses. Home/Slash plants the cursor on it as usual; Backspace then walks the party there.
/// </summary>
internal sealed class ProxyFrontier : ScanItem
{
    private readonly FrontierModel.Blob _blob;

    public ProxyFrontier(FrontierModel.Blob blob) { _blob = blob; }

    public override object Key => _blob;

    public override string Name
    {
        get
        {
            // Parity gate (the CycleExit precedent, main-HUD audit L4): a blob can sit in a room the player has
            // never entered — a wholly-unexplored room's id/class is pure blackness to a sighted player, so name
            // the room only when its ground is at least partly revealed (centroid probe, like the exit cycle).
            var room = _blob.Room;
            return room != null && FogProbe.Classify(room.Centroid) != FogProbe.FogState.NeverSeen
                ? Loc.T("scan.unexplored_in", new { room = RoomMap.Describe(room) })
                : Loc.T("scan.unexplored_space");
        }
    }

    public override Vector3 Position => _blob.Position;

    /// <summary>The blob's spatial extent — distance/bearing read to the nearest edge of the opening, so a long
    /// fog ribbon reads by where it starts, not its middle.</summary>
    public override float Footprint => _blob.Reach;

    public override IEnumerable<string> Nodes
    {
        get { yield return ScanTaxonomy.Unexplored; }
    }

    public override string Primary => ScanTaxonomy.Unexplored;
}
