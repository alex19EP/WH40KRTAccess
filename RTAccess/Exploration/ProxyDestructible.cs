using Kingmaker.EntitySystem.Entities; // DestructibleEntity, MechanicEntity
using Kingmaker.Enums;                 // DestructionStage
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// Attackable destructible scenery — the promethium tank / valve / collapsing wall a sighted player shoots to
/// open a path (<see cref="DestructibleEntity"/>, incl. its cover subclasses). Not an interactable: it has no
/// interaction parts, only <c>PartHealth</c>, so <see cref="ProxyMapObject.IsScannable"/> would drop it — this
/// proxy surfaces it in its own "Destructible objects" category instead. The spoken label mirrors the game's
/// own overtip card (name + health bar → name, "destructible", HP); listing is gated on the SAME attackability
/// the game's click pipeline checks (<see cref="DestructibleEntity.CanBeAttackedDirectly"/>, not yet destroyed),
/// so the scanner never offers a shot the game would refuse outright. <see cref="TargetEntity"/> is what makes
/// the aim commit work: <see cref="AbilityTargeting.CommitAt"/> hands the entity's view GameObject to the game's
/// own <c>ClickWithSelectedAbilityHandler.OnClick</c>, which resolves any <c>MechanicEntityView</c> — a
/// destructible is targeted exactly like a unit, with all validation/refusal messaging reused verbatim.
/// </summary>
internal sealed class ProxyDestructible : ScanItem
{
    private readonly DestructibleEntity _d;

    public ProxyDestructible(DestructibleEntity d) { _d = d; }

    public override object Key => _d;

    public override Vector3 Position => _d.Position;

    // The view's authored XZ bounds (the same rect DestructibleEntity.Position/SizeRect derive from), as a
    // nearest-edge radius so a multi-tile wall/tank reads by its closest edge. Capped like ProxyMapObject's
    // collider-derived footprint so an oversized decorative bound never claims half the room. Cached — the
    // bounds are static and WorldModel keeps this proxy stable across frames.
    private const float FootprintRadiusCap = 2.75f; // ~2 cells
    private float? _footprint;
    public override float Footprint => _footprint ??= ComputeFootprint();

    private float ComputeFootprint()
    {
        try
        {
            var view = _d.View;
            if (view == null) return 0f;
            var size = view.Bounds.size;
            return Mathf.Clamp(Mathf.Max(size.x, size.y) * 0.5f, 0f, FootprintRadiusCap);
        }
        catch { return 0f; }
    }

    // Attackable = the same gate the game's own target resolution applies (GetTarget requires
    // CanBeAttackedDirectly) plus "still standing" — a destroyed object leaves its overtip
    // (DestructibleObjectOvertipsCollectionVM removes it on DestructionStage.Destroyed), so it leaves the
    // scanner the same way instead of lingering as an unshootable ghost.
    private bool Attackable
    {
        get
        {
            try { return _d.CanBeAttackedDirectly && _d.DestructionStages.Stage != DestructionStage.Destroyed; }
            catch { return false; }
        }
    }

    // Reveal + awareness mirror ProxyMapObject (the local-map/overtip reveal gate); CurrentlySeen adds the live
    // fog test. Fog-parity law: a sighted player only sees the object once revealed.
    public override bool IsVisible => _d.IsInGame && _d.IsRevealed && _d.IsAwarenessCheckPassed && Attackable;

    public override bool CurrentlySeen => IsVisible && !_d.IsInFogOfWar;

    // The game's own localized entity name (description part → blueprint). Covers/props can be nameless —
    // fall back to the generic object word rather than reading silence.
    public override string Name
    {
        get
        {
            try
            {
                var name = _d.Name;
                return string.IsNullOrWhiteSpace(name) ? Loc.T("scan.singular.object") : name;
            }
            catch { return Loc.T("scan.singular.object"); }
        }
    }

    // Card-faithful: the overtip shows a health bar under the name → "destructible, N of M HP". The word
    // "destructible" is the affordance cue (this is a thing you shoot, not open).
    public override string Detail
    {
        get
        {
            var word = Loc.T("object.destructible");
            try
            {
                var health = _d.Health;
                return word + ", " + Loc.T("scan.unit_hp", new { current = health.HitPointsLeft, max = health.MaxHitPoints });
            }
            catch { return word; }
        }
    }

    public override IEnumerable<string> Nodes
    {
        get { yield return ScanTaxonomy.Destructibles; }
    }

    public override string Primary => ScanTaxonomy.Destructibles;

    /// <summary>What the aim commit fires on — the destructible itself (the game's click handler resolves its
    /// view GameObject to a TargetWrapper exactly as it does a hovered click).</summary>
    public override MechanicEntity TargetEntity => _d;
}
