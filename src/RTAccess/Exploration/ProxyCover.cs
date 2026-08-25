using Kingmaker;                  // Game (the movable-area read)
using RTAccess.Accessibility;     // CombatReads (the "if I stood here" tail)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// One cover position (<see cref="CoverModel.Spot"/>) as a scanner item — the J cycle and the "Cover" browse
/// category. Its <see cref="Position"/> is the COVER SIDE: the walkable tile you would stand on to be behind the
/// thing, not the thing itself. That is what makes the shared verbs do the right thing for free — Home/Slash
/// plants the cursor on that tile (so the tile readout then names the same edges back to you), Backspace walks
/// the party there, and in turn-based combat Backslash plants the move preview toward it.
///
/// Not interactable: cover is level geometry, there is nothing to activate. Base
/// <see cref="ScanItem.IsVisible"/>/<see cref="ScanItem.CurrentlySeen"/> stay <c>true</c> — the model has already
/// applied the fog gate cell by cell (never-seen ground yields no spots at all), and the whole surface is gated
/// on the game's own cover overlay by its callers, so nothing here can leak what a sighted player cannot see.
/// Keys on the spot, which <see cref="CoverModel.Resolve"/> re-finds across recomputes.
/// </summary>
internal sealed class ProxyCover : ScanItem
{
    private readonly CoverModel.Spot _spot;

    public ProxyCover(CoverModel.Spot spot) { _spot = spot; }

    public override object Key => _spot;

    /// <summary>"Half cover north, full cover east" — the sides, in the tile readout's own phrasing.</summary>
    public override string Name => CoverModel.SidesLine(_spot);

    public override Vector3 Position => _spot.Position;

    /// <summary>How wide the same cover runs, plus the additive "you cannot get there this turn" cue. The
    /// reachability note is read exactly as the tile readout reads it (<c>UnitMovableAreaController</c>, the
    /// blue move-highlight) and is deliberately a NOTE, not a filter: out-of-range cover is precisely what the
    /// player is planning toward.</summary>
    public override string Detail
    {
        get
        {
            var bits = new List<string>();
            if (_spot.Cells > 1) bits.Add(Loc.T("cover.run", new { count = _spot.Cells }));

            var controller = Game.Instance?.UnitMovableAreaController;
            if (controller?.CurrentUnit != null)
            {
                var node = NavmeshProbe.NodeAt(Position);
                if (node != null && controller.CurrentUnitMovableArea?.Contains(node) == false)
                    bits.Add(Loc.T("tile.unreachable"));
            }
            return bits.Count > 0 ? string.Join(", ", bits) : null;
        }
    }

    public override IEnumerable<string> Nodes
    {
        get { yield return ScanTaxonomy.Cover; }
    }

    public override string Primary => ScanTaxonomy.Cover;

    /// <summary>The combat tail that makes a cover spot a DECISION rather than a fact: the holographic "if I stood
    /// here" read for the acting unit — cover against the nearest enemy, how many enemies I would be in range of,
    /// how many would threaten the cell (<see cref="CombatReads.VantageFrom"/>, the same answer the cursor's
    /// vantage key gives for a tile). Cover is directional, so a cell's edges alone never say whether it helps
    /// against the enemy who is actually shooting at you; this does.</summary>
    protected override string CombatSuffix() => CombatReads.VantageFrom(Position, CoverModel.Observer());
}
