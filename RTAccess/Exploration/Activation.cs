using System.Linq;
using System.Text;
using Kingmaker.EntitySystem.Entities;   // MapObjectEntity
using Kingmaker.Pathfinding;             // CustomGridNodeBase
using RTAccess.Accessibility;            // InteractableDescriber
using RTAccess.Screens;                  // ChoiceSubmenuScreen
using RTAccess.Speech;                   // Speaker
using UnityEngine;                       // Vector3

namespace RTAccess.Exploration;

/// <summary>
/// The single activation path both interact verbs funnel through — the tile cursor's Enter
/// (<see cref="RTAccess.Accessibility.TileExplorer.InteractAtCursor"/>) and the scanner selection's I
/// (<see cref="Scanner"/>). Keeping resolve+act in one place is what makes the two symmetric: whatever one key can
/// activate, the other can too. They differ ONLY in which cursor they try FIRST — Enter the tile cursor, I the
/// review selection — then fall back to the other (Enter → <see cref="TryCursorObject"/> then
/// <see cref="Scanner.TryActivateSelection"/>; I the reverse). Every branch drives the game's own object activation
/// (<see cref="ProxyMapObject.Interact"/>) and speaks the shared outcome line, so an object opens identically
/// however it was reached.
/// </summary>
internal static class Activation
{
    /// <summary>
    /// Activate the actionable object(s) nearest the tile cursor. Returns true when it handled the press: zero
    /// objects → false (the caller falls back to the other cursor, or says nothing); exactly one → activate it;
    /// two or more within reach → pop a chooser so the player picks WHICH to activate (interactables are off-grid,
    /// so several can share reach of a single tile — clustered loot, a door beside a lever). The chooser is a child
    /// screen, so it takes the keyboard while open and hands it back on pick/cancel.
    /// </summary>
    public static bool TryCursorObject(CustomGridNodeBase node)
        => node != null && TryCursorObject((Vector3)node.position);

    /// <summary>As <see cref="TryCursorObject(CustomGridNodeBase)"/> but around an arbitrary world point — the I
    /// key's fallback resolves the interactable(s) at the review SELECTION's position when the selection entity
    /// itself couldn't be activated (a co-located real interactable), automating the "Home then Enter" workaround.</summary>
    public static bool TryCursorObject(Vector3 origin)
    {
        var objs = InteractableDescriber.InteractablesAt(origin);
        if (objs.Count == 0) return false;
        if (objs.Count == 1) { ActivateObject(objs[0]); return true; }

        var labels = objs.Select(o => Label(o, origin)).ToList();
        ChoiceSubmenuScreen.Open(Loc.T("scan.choose_object"), labels, 0, i => ActivateObject(objs[i]));
        return true;
    }

    /// <summary>Drive the game's own click activation of one object and speak the shared outcome line — the atom
    /// both keys (and the chooser) end on, so activation reads identically however the object was reached. Self-
    /// guards: the chooser's pick fires this from a UI callback OUTSIDE the interact handlers' try/catch, and the
    /// chosen object may have despawned in the interim.</summary>
    public static void ActivateObject(MapObjectEntity o)
    {
        try
        {
            var item = new ProxyMapObject(o);
            SpeakOutcome(item.Interact(), item.Name);
        }
        catch (Exception e) { Main.Log?.Error("Activation.ActivateObject failed: " + e); }
    }

    /// <summary>The shared interaction-outcome line — "Interacting with X." / "Can't interact with X." — spoken by
    /// both interact keys and the chooser, so activation reads identically however the object was reached.</summary>
    public static void SpeakOutcome(bool ok, string name)
        => Speaker.Speak(Loc.T(ok ? "scan.interacting" : "scan.cant_interact", new { name }), interrupt: true);

    // A terse chooser label: name + verb/state + bearing from the cursor, so two same-named containers stay
    // distinguishable by where they sit. Reuses the same name/detail/compass the scanner speaks elsewhere.
    private static string Label(MapObjectEntity o, Vector3 origin)
    {
        var item = new ProxyMapObject(o);
        var sb = new StringBuilder(item.Name);
        var detail = item.Detail;
        if (!string.IsNullOrWhiteSpace(detail)) sb.Append(", ").Append(detail);
        sb.Append(", ").Append(InteractableDescriber.DirectionAndDistance(origin, o.Position));
        return sb.ToString();
    }
}
