using Kingmaker.EntitySystem.Entities;          // AreaEffectEntity
using Kingmaker.Pathfinding;                    // CustomGridNodeBase (the Covers per-cell test)
using Kingmaker.View.MapObjects.SriptZones;     // ScriptZoneCylinder, IScriptZoneShape (the "Sript" typo is the game's)
using Kingmaker.View.Mechanics.ScriptZones;     // ScriptZoneBox (a DIFFERENT namespace than the cylinder on RT)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A live area effect — a psychic power AoE (a Smite cloud, a warp rift…), a thrown-grenade field, or a placed
/// environmental hazard — surfaced from <c>Game.Instance.State.AreaEffects</c>. Classified ONCE at creation (the
/// blueprint's target set is fixed for the effect's life) into a HAZARD (a zone that can catch enemies — i.e. a
/// damage/debuff field, the thing to route around) or a BUFF ZONE (an ally-exclusive aura). Because an effect is a
/// ZONE, distance and bearing report the nearest EDGE of its real runtime shape — the bit you are about to step
/// into — not a circle around its centre: a cylinder reads as a circle, a wall (<see cref="ScriptZoneBox"/>) reads
/// as its rotated rectangle. High value in turn-based combat, where stepping one tile into a cloud is a real cost.
/// </summary>
internal sealed class ProxyAreaEffect : ScanItem
{
    private readonly AreaEffectEntity _ae;
    private readonly string _node; // cached classification (blueprint target set is fixed for the effect's life)

    public ProxyAreaEffect(AreaEffectEntity ae) { _ae = ae; _node = Classify(ae); }

    public override object Key => _ae;

    // The casting power/grenade's name when there is one; otherwise the generic kind word so a nameless placed
    // hazard still announces as something.
    private string SpellName => _ae.Context?.SourceAbility?.Name;
    public override string Name => string.IsNullOrEmpty(SpellName) ? TypeWord : SpellName;

    public override Vector3 Position => _ae.Position;

    // Dynamic like a unit (not reveal-latched): list it while it is active, in game, and not fogged.
    public override bool IsVisible => _ae.IsInGame && !_ae.IsEnded && !_ae.IsInFogOfWar;

    public override string Primary => _node;

    public override IEnumerable<string> Nodes { get { yield return _node; } }

    // "hazard" / "buff zone" — dropped when the name already IS that generic word (a nameless placed hazard), so it
    // never reads "hazard, hazard".
    public override string Detail => string.IsNullOrEmpty(SpellName) ? null : TypeWord;

    // The zone's real footprint radius, from the live runtime shape. Cylinder → its radius (metres); any other
    // shape (wall box, pattern, all-area) → half its wider bound as a ballpark for the circle-based systems (the
    // scanner readout below uses the true shape instead). 0 only when there is no attached shape yet.
    public override float Footprint
    {
        get
        {
            var shape = _ae.View?.Shape;
            if (shape is ScriptZoneCylinder cyl) return cyl.Radius; // Radius is world metres (Contains compares raw XZ magnitude)
            if (shape != null) { var e = shape.GetBounds().extents; return Mathf.Max(e.x, e.z); }
            return 0f;
        }
    }

    // Spoken path (per-announce): the real shape as a ScanBounds so distance/bearing report the nearest EDGE.
    // Cylinder → circle; wall → its rotated rectangle; anything else → the base circle(Footprint).
    public override ScanBounds Bounds
    {
        get
        {
            if (_ae.View?.Shape is ScriptZoneCylinder cyl) return ScanBounds.Circle(Position, cyl.Radius);
            if (TryCorners(out var p0, out var p1, out var p2, out var p3))
                return ScanBounds.Rect(Position, new[] { p0, p1, p2, p3 });
            return base.Bounds;
        }
    }

    // Per-frame path (sort / cursor / future cues): the same geometry, NON-ALLOCATING, via the shared ScanBounds
    // statics — one source of "closest point" math with the spoken path above.
    public override Vector3 NearestPoint(Vector3 from)
    {
        if (_ae.View?.Shape is ScriptZoneCylinder cyl) return ScanBounds.NearestOnCircleXZ(Position, cyl.Radius, from);
        if (TryCorners(out var p0, out var p1, out var p2, out var p3))
            return ScanBounds.NearestInQuadXZ(from, p0, p1, p2, p3);
        return base.NearestPoint(from);
    }

    // The wall's four world-space footprint corners (its rotated rectangle), or false for any other shape. The
    // box's Bounds are LOCAL to the shape's own transform (which sits on the same GameObject as the view), so we
    // transform them through that transform — exactly as ScriptZoneBox.ContainsPoint inverse-transforms to test.
    private bool TryCorners(out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
    {
        p0 = p1 = p2 = p3 = default;
        if (!(_ae.View?.Shape is ScriptZoneBox box)) return false;
        var t = box.transform;
        var b = box.Bounds; // local: the wall is Size long × depth deep
        Vector3 c = b.center, e = b.extents;
        p0 = t.TransformPoint(c + new Vector3(-e.x, 0f, -e.z));
        p1 = t.TransformPoint(c + new Vector3( e.x, 0f, -e.z));
        p2 = t.TransformPoint(c + new Vector3( e.x, 0f,  e.z));
        p3 = t.TransformPoint(c + new Vector3(-e.x, 0f,  e.z));
        return true;
    }

    private string TypeWord => Loc.T(_node == ScanTaxonomy.BuffZones ? "areaeffect.buffzone" : "areaeffect.hazard");

    // A zone that can catch enemies (a damage/debuff field, TargetType Enemy or Any) is a HAZARD — the safe default
    // for a blind player, who wants to route around anything dangerous; an ally-EXCLUSIVE zone (TargetType Ally) is
    // a buff zone. RT's BlueprintBuff has no harmful flag, so the target set is the reliable signal (and is what
    // WrathAccess itself fell back to). Classified once — the blueprint is fixed for the effect's life.
    private static string Classify(AreaEffectEntity ae)
        => IsHazard(ae) ? ScanTaxonomy.Hazards : ScanTaxonomy.BuffZones;

    /// <summary>Does the zone's PAINTED pattern cover this cell? — the per-cell containment for tile readouts.
    /// Tests <c>CoveredNodes</c>, the quantized grid pattern that is both what the HUD paints for the sighted
    /// player and what the game's own pass-through mechanic tests (<c>RuleCalculatePassedAreaEffects</c>) —
    /// same rule as <c>PathInfo.HazardWarning</c>. NOT the metric <see cref="ScanItem.Contains"/> shape test:
    /// the raw shape overhangs the pattern by a rim of unpainted cells (a 3 m cylinder paints only ⌊3/1.35⌋ = 2),
    /// and a directional pattern (cone/line) would degrade to its bounding circle. Distance/bearing keep the
    /// metric shape — edge distance is approach guidance, only containment is quantized. The null-shape guard
    /// mirrors PathInfo (<c>CoveredNodes</c> throws while the effect has no runtime shape yet).</summary>
    internal bool Covers(CustomGridNodeBase node)
        => node != null && _ae.View?.Shape != null && _ae.CoveredNodes.Contains(node);

    // Also the path preview's hazard filter (PathInfo.HazardWarning) — one definition of "dangerous zone".
    internal static bool IsHazard(AreaEffectEntity ae)
    {
        var bp = ae.Blueprint;
        if (bp == null) return true;
        return bp.CanTargetEnemies || !bp.CanTargetAllies;
    }
}
