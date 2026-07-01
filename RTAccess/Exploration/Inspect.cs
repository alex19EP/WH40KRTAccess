using Kingmaker;                       // Game
using Kingmaker.EntitySystem.Entities; // BaseUnitEntity
using Kingmaker.Inspect;               // InspectUnitsHelper
using Kingmaker.Pathfinding;           // CustomGridNodeBase.GetUnit
using Kingmaker.PubSubSystem;          // IUnitClickUIHandler
using Kingmaker.PubSubSystem.Core;     // EventBus
using Owlcat.Runtime.UI.Tooltips;      // TooltipBaseTemplate
using RTAccess.Accessibility;          // TooltipReader
using RTAccess.Speech;                 // Speaker

namespace RTAccess.Exploration;

/// <summary>
/// Speak the game's Inspect panel for a unit — the same full readout (wounds, deflection, armour, dodge,
/// move points, weapons, abilities, feats, status effects) a sighted player gets from the inspect tooltip.
/// Two independent verbs target the world, with no fallback between them: inspect.review (the Y key) inspects the
/// area scanner's review selection; inspect.cursor (the ' key) inspects the tile cursor's occupant. An empty
/// source just says "Nothing to inspect" (it never consults the other source).
///
/// In RT, opening Inspect IS the knowledge reveal — both <see cref="Kingmaker.UI.MVVM.VM.Tooltip.Templates"/>
/// inspect-template ctors call <c>InspectUnitsManager.ForceRevealUnitInfo</c> (the 2-arg one chains the 1-arg)
/// — so there is no knowledge to "respect" or cheat past: we just do exactly what the game's own inspect does.
/// We raise the same <see cref="IUnitClickUIHandler"/> event the console inspect button raises, which the live
/// <c>InGameInspectVM</c> turns into the template (built with proper reactive data) and a visible panel; then we
/// read that template aloud via <see cref="TooltipReader.GetFull(TooltipBaseTemplate)"/>. That gives byte-for-byte
/// sighted parity AND pops the panel for a sighted helper, with no separate reveal of our own. Only
/// <c>InspectVM</c> handles this event, so raising it has no other side effects (no selection/camera/command).
/// Gated by <c>InspectUnitsHelper.IsInspectAllow</c>, exactly like the game's inspect affordance.
///
/// The two halves of the inspect verb pair are registered in <see cref="RTAccess.Input.InputCategory.Exploration"/>
/// (live only while the in-game screen owns world control — dead in windows/dialogue/cutscenes):
/// <c>inspect.review</c> (the Y key) inspects the scanner's review SELECTION; <c>inspect.cursor</c> (the ' key)
/// inspects the tile cursor's occupant. Lightweight crowd (not a <see cref="BaseUnitEntity"/>) and empty tiles are
/// not inspectable.
/// </summary>
internal static class Inspect
{
    /// <summary>Inspect the tile cursor's occupant (the ' key).</summary>
    internal static void InspectCursor() => Safe(() => Run(MapCursor.Node?.GetUnit()));

    /// <summary>Inspect the scanner's current review selection (the Y key).</summary>
    internal static void InspectReview() => Safe(() => Run(Scanner.SelectedUnit()));

    private static void Run(BaseUnitEntity unit)
    {
        if (unit == null) { Speak("Nothing to inspect."); return; }
        if (!InspectUnitsHelper.IsInspectAllow(unit)) { Speak("Can't inspect " + unit.CharacterName + "."); return; }

        // Drive the game's own inspect. This is the only handler of the event (InspectVM), so the sole effects are
        // building the inspect template on the live VM and showing the visual panel — no selection or camera move.
        EventBus.RaiseEvent<IUnitClickUIHandler>(h => h.HandleUnitConsoleInvoke(unit));

        // OnUnitInvoke set Tooltip.Value synchronously (surface path), so the template is ready to read now.
        var text = ReadInspect();
        Speak(string.IsNullOrWhiteSpace(text) ? (unit.CharacterName + ", " + Faction(unit)) : text);
    }

    /// <summary>The inspect template the live <c>InGameInspectVM</c> just built, rendered to text.</summary>
    private static string ReadInspect()
    {
        TooltipBaseTemplate template = Game.Instance?.RootUiContext?.SurfaceVM?.StaticPartVM?.SurfaceHUDVM?.InspectVM?.Tooltip?.Value;
        return template != null ? TooltipReader.GetFull(template) : null;
    }

    private static string Faction(BaseUnitEntity u)
        => u.Faction != null && u.Faction.IsPlayerEnemy ? "enemy" : (u.IsInPlayerParty ? "ally" : "neutral");

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Main.Log?.Error("Inspect failed: " + e); }
    }

    private static void Speak(string msg)
    {
        // Key-driven, so it interrupts prior speech (per [[rt-interrupt-speech-rule]]).
        if (!string.IsNullOrEmpty(msg)) Speaker.Speak(msg, interrupt: true);
    }
}
