using Kingmaker;                                          // Game
using Kingmaker.Controllers.Clicks.Handlers;              // ClickMapObjectHandler (the game's own click dispatch)
using Kingmaker.EntitySystem.Entities;                    // MapObjectEntity, BaseUnitEntity
using Kingmaker.GameCommands;                             // AreaTransitionHelper
using Kingmaker.View.MapObjects;                          // InteractionDoorPart/LootPart/SkillCheckPart, AreaTransitionPart
using Kingmaker.View.MapObjects.InteractionComponentBase; // InteractionPart
using RTAccess.Accessibility;                             // InteractableDescriber (name/verb reuse)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A scannable interactable map object. Its categories come from the interaction parts it carries (loot →
/// containers, door → doors, skill check → search points, anything else → mechanisms; an area transition adds
/// exits; nothing → scenery). Name + verb are resolved by <see cref="InteractableDescriber"/> (the same mapping
/// the overtip uses). Interact mirrors a click: an exit runs the area-transition flow, everything else is dispatched
/// through the game's own click handler (<see cref="ClickMapObjectHandler.Interact"/>) — the same path a mouse click
/// takes once an object is chosen, so unit-selection, Direct-vs-Approach, trap handling and AP/range warnings all
/// match the base game.
/// </summary>
internal sealed class ProxyMapObject : ScanItem
{
    private readonly MapObjectEntity _obj;

    public ProxyMapObject(MapObjectEntity obj) { _obj = obj; }

    public override object Key => _obj;

    public override Vector3 Position => _obj.Position;

    // Mirror the local map / overtip reveal gate: listed once seen, gated by perception (awareness) so secret
    // objects don't leak. CurrentlySeen adds the live fog test for the review cycles.
    public override bool IsVisible => _obj.IsInGame && _obj.IsRevealed && _obj.IsAwarenessCheckPassed;

    public override bool CurrentlySeen => IsVisible && !_obj.IsInFogOfWar;

    public override string Name
    {
        get
        {
            var view = _obj.View;
            if (view == null) return "Object";
            try { return InteractableDescriber.ResolveName(view, out _); }
            catch { return "Object"; }
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
            foreach (var part in _obj.Interactions)
            {
                if (part is InteractionDoorPart door && door.IsOpen) { bits.Add("open"); break; }
            }
            return bits.Count > 0 ? string.Join(", ", bits) : null;
        }
    }

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
    // don't leak. Traps have no single concrete part type exposed, so detect by part type name (the same
    // heuristic InteractableDescriber uses).
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
            else if (part.GetType().Name.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0) nodes.Add(ScanTaxonomy.Traps);
            else nodes.Add(ScanTaxonomy.Mechanisms);
        }
        if (_obj.GetOptional<AreaTransitionPart>() != null) nodes.Add(ScanTaxonomy.Exits);
        if (nodes.Count == 0) nodes.Add(ScanTaxonomy.Scenery);
        return nodes;
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
