using Kingmaker;                                          // Game
using Kingmaker.Code.UI.MVVM.VM.VariativeInteraction;    // VariativeInteractionVM.HasVariativeInteraction
using Kingmaker.Controllers.Clicks.Handlers;              // ClickMapObjectHandler (the game's own click dispatch)
using Kingmaker.EntitySystem.Entities;                    // MapObjectEntity, BaseUnitEntity
using Kingmaker.GameCommands;                             // AreaTransitionHelper
using Kingmaker.PubSubSystem;                             // IVariativeInteractionUIHandler
using Kingmaker.PubSubSystem.Core;                        // EventBus
using Kingmaker.View.MapObjects;                          // InteractionDoorPart/LootPart/SkillCheckPart, AreaTransitionPart
using Kingmaker.View.MapObjects.Traps;                    // TrapObjectView (trap ↔ disarm-device link)
using RTAccess.Accessibility;                             // InteractableDescriber (name/verb reuse)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A scannable interactable map object. Its categories come from the interaction parts it carries (loot →
/// containers, door → doors, skill check → search points, any other live interaction — lever, bark/examine — →
/// mechanisms; an area transition adds exits). An object with no live interaction, exit, or map marker — a
/// script / trigger volume, a secret (disabled) door, a looted container, a decorative prop — is NOT scannable
/// (see <see cref="IsScannable"/>) and never appears. Name + verb are resolved by <see cref="InteractableDescriber"/> (the same mapping
/// the overtip uses). Interact mirrors a click: an exit runs the area-transition flow; a locked/variative object
/// (one offering a choice of actor — skill vs Tech-Use vs Key vs Destroy) raises the game's variative-interaction
/// request so our <see cref="RTAccess.Screens.VariativeInteractionScreen"/> can surface the choice; everything else
/// is dispatched through the game's own click handler (<see cref="ClickMapObjectHandler.Interact"/>) — the same
/// path a mouse click takes once an object is chosen, so unit-selection, Direct-vs-Approach, trap handling and
/// AP/range warnings all match the base game.
/// </summary>
internal sealed class ProxyMapObject : ScanItem
{
    private readonly MapObjectEntity _obj;

    public ProxyMapObject(MapObjectEntity obj) { _obj = obj; }

    public override object Key => _obj;

    public override Vector3 Position => _obj.Position;

    // Nearest-edge footprint radius (metres), so distance/bearing report the object's nearest EDGE (a wide door /
    // large console reads by its edge, not its authored centre). Derived from the collider NEAREST the object's
    // Position (its clickable body), GUARDED: a probe of a live area found most objects have one body collider on
    // Position, but a multi-part object (e.g. a warp-storm with 60 colliders) can have its tightest collider ~12 m
    // off — using that would make it read "here" across the room. So we reject a collider whose centre is more than
    // ~1 cell from Position (→ point, today's behaviour) and clamp the radius to ~2 cells. Cached: collider geometry
    // is static per map object and this proxy is stable across frames (WorldModel keeps one per entity).
    private const float FootprintOffsetCap = 1.5f;   // ~1 cell (1.35 m): collider must sit on the authored point
    private const float FootprintRadiusCap = 2.75f;  // ~2 cells: never claim a wider body than that from a collider
    private float? _footprint;
    public override float Footprint => _footprint ??= ComputeFootprint();

    private float ComputeFootprint()
    {
        try
        {
            var view = _obj.View;
            if (view == null) return 0f;
            var pos = _obj.Position;
            Collider best = null; float bestOff = float.MaxValue;
            foreach (var c in view.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger) continue;   // skip script-zone / trigger volumes; want the solid body
                var ctr = c.bounds.center; float dx = ctr.x - pos.x, dz = ctr.z - pos.z;
                float off = Mathf.Sqrt(dx * dx + dz * dz);
                if (off < bestOff) { bestOff = off; best = c; }
            }
            if (best == null || bestOff > FootprintOffsetCap) return 0f;   // no body collider on the point → stay a point
            var e = best.bounds.extents;
            return Mathf.Clamp(Mathf.Max(e.x, e.z), 0f, FootprintRadiusCap);
        }
        catch { return 0f; }
    }

    // Mirror the local map / overtip reveal gate (revealed + awareness), AND require the object to be a real,
    // player-relevant interactable (IsScannable) — so invisible trigger / script volumes (no interaction parts),
    // SECRET doors, and looted/inactive containers (nothing to do, and a spoiler for a sighted player who hasn't
    // found them) never leak into the scanner. Bark/examine volumes DO surface: they are clickable interactions a
    // sighted player can use (see IsScannable). CurrentlySeen adds the live fog test for the review cycles;
    // DetectableFrom (base) then re-admits a fogged-but-line-of-sight-clear object to those cycles.
    public override bool IsVisible => _obj.IsInGame && _obj.IsRevealed && _obj.IsAwarenessCheckPassed && IsScannable;

    public override bool CurrentlySeen => IsVisible && !_obj.IsInFogOfWar;

    // Is this a real, player-relevant interactable the scanner should surface? The gate mirrors the game's own click
    // availability (ClickMapObjectHandler.HasAvailableInteractions = ANY interaction part CanInteract): a live
    // (enabled) interaction — loot, skill check, lever, trap, OR a bark/examine (InteractionBarkPart is a genuine
    // UIInteractionType.Info interaction a sighted player can click, verified in the decompiled InteractionPart) —
    // an open door (a landmark opening), an area exit, or a local-map marker. That excludes exactly the sighted-
    // invisible noise the scanner-visibility dump surfaced: 0-renderer script zones (no interaction parts), SECRET
    // doors (door part disabled, not open), looted/inactive containers (loot part disabled), and props with no
    // interaction at all — none of which a sighted player can interact with. Non-allocating — runs per item per key
    // (and per sonar frame).
    private bool IsScannable
    {
        get
        {
            foreach (var part in _obj.Interactions)
            {
                if (part == null) continue;
                if (part is InteractionDoorPart door) { if (part.Enabled || door.IsOpen) return true; continue; }
                // A disarmed/triggered trap keeps its part Enabled but flips TrapActive=false (TrapObjectData.Deactivate);
                // it is no longer a live interaction (the game's own CanInteract() goes false), so a pure-trap object
                // drops out of the scanner once disarmed instead of lingering as a spent, un-actionable "trap".
                if (part is DisableTrapInteractionPart trapPart) { if (part.Enabled && trapPart.Owner?.TrapActive == true) return true; continue; }
                if (part.Enabled) return true;
            }
            return _obj.GetOptional<AreaTransitionPart>() != null
                || _obj.GetOptional<LocalMapMarkerPart>() != null;
        }
    }

    public override string Name
    {
        get
        {
            var view = _obj.View;
            if (view == null) return Loc.T("scan.singular.object");
            try { return InteractableDescriber.ResolveName(view, out _); }
            catch { return Loc.T("scan.singular.object"); }
        }
    }

    public override string Detail
    {
        get
        {
            var bits = new List<string>();
            var view = _obj.View;
            if (view != null)
            {
                try
                {
                    InteractableDescriber.ResolveName(view, out var interaction);
                    var verb = InteractableDescriber.Verb(interaction);
                    if (!string.IsNullOrEmpty(verb)) bits.Add(verb);
                }
                catch { /* name/verb best-effort; position still announces */ }
            }

            // State qualifiers a blind player needs BEFORE walking over to interact: an open door, a container's
            // kind (chest / environment / stash / single-slot / cargo), and an ARMED, detected trap. Per-part
            // best-effort — a single part read that throws is skipped so the object still announces name + position.
            foreach (var part in _obj.Interactions)
            {
                try
                {
                    switch (part)
                    {
                        case InteractionDoorPart door when door.IsOpen:
                            bits.Add(Loc.T("object.open"));
                            break;
                        case InteractionLootPart loot:
                            var kind = LootKindWord(loot.Settings.LootContainerType);
                            if (kind != null) bits.Add(kind);
                            // The game's only "you already opened this" cue is a highlight-color swap
                            // (VisitedLootColor vs StandartLootColor in MapObjectView.GetHighlightColor) —
                            // color-only on screen, so it must be voiced.
                            if (loot.LootViewed) bits.Add(Loc.T("scan.already_opened"));
                            break;
                        // A trap is only flagged when its disarm interaction is live (part.Enabled == detected) and
                        // still armed — an undetected trap never lists the object (awareness gate), so this is no spoiler.
                        case DisableTrapInteractionPart trap when part.Enabled && trap.Owner?.TrapActive == true:
                            bits.Add(Loc.T("object.trapped"));
                            var trapInfo = InteractableDescriber.CheckInfo(part);
                            if (trapInfo != null) bits.Add(trapInfo);
                            break;
                        // The skill-check card line (short description + "[Skill: NN%]" chance, or the after-use
                        // passed/failed description) — what a sighted hover shows under the name.
                        case InteractionSkillCheckPart when part.Enabled:
                            var checkInfo = InteractableDescriber.CheckInfo(part);
                            if (checkInfo != null) bits.Add(checkInfo);
                            break;
                    }
                }
                catch { /* per-part best-effort */ }
            }

            // "locked" — the object gates its interaction behind a CHOICE of actor (skill vs Tech-Use vs Key vs
            // Destroy), the exact condition Interact() routes to the variative-interaction choice screen. Same signal
            // the interact path uses, so the spoken label and the behaviour never diverge.
            if (view != null)
            {
                try { if (VariativeInteractionVM.HasVariativeInteraction(view)) bits.Add(Loc.T("object.locked")); }
                catch { /* best-effort */ }
            }

            // Trap ↔ disarm-device link (main-HUD audit #8): the game draws a ground spline connecting the pair
            // whenever EITHER end is revealed AND EITHER end awareness-passed AND EITHER end armed
            // (TrapObjectView.UpdateLinkLine — all three conditions are either-end ORs; mirror them exactly).
            // Voice the topology from both ends, so the mechanism ten tiles away that neutralizes the trap in
            // your path — often the safe alternative to an in-place disarm — is discoverable.
            if (view is TrapObjectView trapView)
            {
                try
                {
                    // On the DEVICE end LinkedTrap points at its trap; on the TRAP end Device points back.
                    var other = trapView.LinkedTrap != null ? trapView.LinkedTrap : trapView.Device;
                    var a = trapView.Data;
                    var b = other != null ? other.Data : null;
                    if (b != null
                        && (a.IsRevealed || b.IsRevealed)
                        && (a.IsAwarenessCheckPassed || b.IsAwarenessCheckPassed)
                        && (a.TrapActive || b.TrapActive))
                    {
                        var where = InteractableDescriber.DirectionAndDistance(
                            MapCursor.PlayerPosition, other.ViewTransform.position);
                        bits.Add(trapView.LinkedTrap != null
                            ? Loc.T("trap.controls", new { where })   // this end is the device
                            : Loc.T("trap.device", new { where }));   // this end is the trap
                    }
                }
                catch { /* best-effort */ }
            }

            return bits.Count > 0 ? string.Join(", ", bits) : null;
        }
    }

    // The container kind as a terse word, mirroring the game's own LootContainerType. DefaultLoot adds nothing (a
    // plain container reads by its name/verb) and Unit is a corpse (ProxyUnit labels those), so both return null.
    private static string LootKindWord(LootContainerType type)
        => type switch
        {
            LootContainerType.Chest => Loc.T("object.kind.chest"),
            LootContainerType.Environment => Loc.T("object.kind.environment"),
            LootContainerType.PlayerChest => Loc.T("object.kind.stash"),
            LootContainerType.OneSlot => Loc.T("object.kind.single_slot"),
            LootContainerType.StarSystemObject => Loc.T("object.kind.cargo"),
            _ => null, // DefaultLoot, Unit → no extra word
        };

    public override IEnumerable<string> Nodes => NodeSet();

    public override string Primary
    {
        get
        {
            var nodes = NodeSet();
            if (nodes.Contains(ScanTaxonomy.Exits)) return ScanTaxonomy.Exits;
            if (nodes.Contains(ScanTaxonomy.Containers)) return ScanTaxonomy.Containers;
            if (nodes.Contains(ScanTaxonomy.Doors)) return ScanTaxonomy.Doors;
            if (nodes.Contains(ScanTaxonomy.Traps)) return ScanTaxonomy.Traps;
            if (nodes.Contains(ScanTaxonomy.SearchPoints)) return ScanTaxonomy.SearchPoints;
            if (nodes.Contains(ScanTaxonomy.Mechanisms)) return ScanTaxonomy.Mechanisms;
            return ScanTaxonomy.Scenery;
        }
    }

    // Categories from the interaction parts. A door is kept as a door even when its part is disabled-but-open
    // (a one-way door is still a landmark); other disabled parts are skipped so hidden/secret interactions
    // don't leak. A bark/examine interaction falls into the "other interaction" bucket (mechanisms), since it is a
    // real clickable interaction (see IsScannable). Traps have no single concrete part type exposed, so detect by
    // part type name (the same heuristic InteractableDescriber uses). There is deliberately NO "scenery" fallback:
    // an object with no live interaction / exit produces no node and so is not scannable (see IsScannable /
    // IsVisible) — it never reaches a browse category.
    private HashSet<string> NodeSet()
    {
        var nodes = new HashSet<string>();
        foreach (var part in _obj.Interactions)
        {
            if (part == null) continue;
            if (part is InteractionDoorPart door)
            {
                if (part.Enabled || door.IsOpen) nodes.Add(ScanTaxonomy.Doors);
                continue;
            }
            if (!part.Enabled) continue;
            if (part is InteractionLootPart) nodes.Add(ScanTaxonomy.Containers);
            else if (part is InteractionSkillCheckPart) nodes.Add(ScanTaxonomy.SearchPoints);
            // Only an ARMED trap is a live "trap" node. A disarmed/triggered trap keeps its part Enabled but flips
            // TrapActive=false, so it contributes NO node — dropping out of the Traps category and Sonar exactly like a
            // looted container leaves the Containers category (mirrors the TrapActive gate in Detail). Any other
            // trap-ish part (name heuristic) with no TrapActive to read still classifies as a trap.
            else if (part is DisableTrapInteractionPart trapPart) { if (trapPart.Owner?.TrapActive == true) nodes.Add(ScanTaxonomy.Traps); }
            else if (part.GetType().Name.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0) nodes.Add(ScanTaxonomy.Traps);
            else nodes.Add(ScanTaxonomy.Mechanisms);
        }
        if (_obj.GetOptional<AreaTransitionPart>() != null) nodes.Add(ScanTaxonomy.Exits);
        return nodes;
    }

    // The game's own actionability gate — an available interaction, or an area-transition exit (which carries no
    // InteractionPart). Mirrors InteractableDescriber.IsActionable exactly so the scanner's I key and the cursor's
    // Enter share ONE notion of "actionable" (Interact() then routes exits / variative / plain clicks identically).
    public override bool CanInteract
    {
        get
        {
            var view = _obj.View;
            if (view != null && ClickMapObjectHandler.HasAvailableInteractions(view.gameObject)) return true;
            return _obj.GetOptional<AreaTransitionPart>() != null;
        }
    }

    // Same as clicking the object: an exit runs the party area-transition flow; everything else is dispatched
    // through the game's own click handler, exactly as a mouse click does once it has picked an object. We pass
    // forceOvertipInteractions: true so overtip-only interactions (which in mouse mode wait for a hover button a
    // blind player can't click) still fire directly — the console/gamepad behaviour.
    public override bool Interact()
    {
        if (_obj.GetOptional<AreaTransitionPart>() != null)
        {
            AreaTransitionHelper.StartAreaTransition(_obj);
            return true;
        }

        var view = _obj.View;
        if (view == null) return false;

        // A locked/variative object offers a CHOICE of actor (a skill check vs Tech-Use vs a Key vs a Melta charge
        // vs Destroy, each with its own success chance). The static ClickMapObjectHandler.Interact below would
        // auto-run the first available actor and skip that choice — the choice branch lives only in the mouse
        // OnClick / overtip paths, not the static entry the mod calls. So raise the request ourselves: the game
        // builds SurfaceDynamicPartVM.VariativeInteractionVM.Value and our VariativeInteractionScreen mirrors it
        // into accessible buttons (mirrors WrathAccess's lockpick guard). Outcome is voiced by the game's own
        // combat log (PickLockLogThread etc.) via LogTap.
        if (VariativeInteractionVM.HasVariativeInteraction(view))
        {
            EventBus.RaiseEvent<IVariativeInteractionUIHandler>(h => h.HandleInteractionRequest(view));
            return true;
        }

        var units = Game.Instance?.SelectionCharacter?.SelectedUnits?.ToList();
        if (units == null || units.Count == 0)
        {
            var main = Game.Instance?.Player?.MainCharacterEntity;
            units = main != null ? new List<BaseUnitEntity> { main } : null;
        }
        if (units == null || units.Count == 0) return false;

        return ClickMapObjectHandler.Interact(view.gameObject, units, forceOvertipInteractions: true);
    }
}
