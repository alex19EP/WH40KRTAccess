using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A geometric room exit (<see cref="RoomMap.Exit"/> — an open threshold between two rooms) as a scanner item
/// for the V cycle: it becomes the review selection like anything else, so O re-announces it and Home/Slash
/// plants the cursor on it (Backspace then walks the party through). Not interactable — there is nothing there,
/// it's an opening; doors and area transitions ride the same cycle as their own (actionable) items instead.
/// Distance/bearing read the nearest part of the FULL threshold (the boundary-cell cloud), so a wide opening
/// reads by its near edge while the cursor still targets the centre. Keys on the Exit object (stable per map
/// build); Scanner.ResolveSelected re-wraps it while the current map still holds it.
/// </summary>
internal sealed class ProxyRoomExit : ScanItem
{
    private readonly RoomMap.Exit _exit;
    private readonly ScanBounds _bounds;

    public ProxyRoomExit(RoomMap.Exit exit)
    {
        _exit = exit;
        _bounds = exit.Boundary != null && exit.Boundary.Length > 0
            ? ScanBounds.Cloud(exit.Position, exit.Boundary)
            : ScanBounds.Point(exit.Position);
    }

    public override object Key => _exit;

    /// <summary>Parity gate (main-HUD audit L4): a wholly-unexplored destination room's id/class must not
    /// leak — degrade to the class-less "unexplored" line (centroid probe; a partially explored destination
    /// keeps its class, which the sighted map's explored layout already reveals).</summary>
    public override string Name
        => FogProbe.Classify(_exit.To.Centroid) == FogProbe.FogState.NeverSeen
            ? Loc.T("exit.to_unexplored")
            : Loc.T("exit.to_room", new { room = RoomMap.Describe(_exit.To) });

    public override Vector3 Position => _exit.Position;

    public override ScanBounds Bounds => _bounds;
    public override Vector3 NearestPoint(Vector3 from) => _bounds.NearestPoint(from);

    public override IEnumerable<string> Nodes
    {
        get { yield return ScanTaxonomy.Exits; }
    }

    public override string Primary => ScanTaxonomy.Exits;
}
