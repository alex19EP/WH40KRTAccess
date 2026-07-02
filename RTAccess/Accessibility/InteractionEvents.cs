using Kingmaker.PubSubSystem;      // IPickLockHandler
using Kingmaker.View.MapObjects;   // MapObjectView
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Voices the OUTCOME of a world interaction the player cannot see. Right now this is lock-picking: interacting
/// with a locked door/container walks a unit over and runs a skill check, but the result has no audible cue of its
/// own — the object either opens or silently stays shut. We subscribe globally to the game's own
/// <see cref="IPickLockHandler"/> (the same event the combat log's <c>GameLogEventPickLock</c> rides), so a result
/// is spoken whether the interaction came from our cursor/scanner (see <see cref="RTAccess.Exploration.ProxyMapObject"/>)
/// or an ordinary mouse click. Event-driven and the direct consequence of the player's interact keypress, so it
/// interrupts (see the interrupt-speech rule). One persistent session subscriber; unsubscribed in
/// <see cref="Main.OnUnload"/> alongside the other <c>EventBus</c> subscribers.
/// </summary>
internal sealed class InteractionEvents : IPickLockHandler
{
    public static readonly InteractionEvents Instance = new InteractionEvents();

    private InteractionEvents() { }

    public void HandlePickLockSuccess(MapObjectView mapObjectView)
        => Speak(Name(mapObjectView) + Loc.T("interact.lock_picked"));

    // The game only raises non-critical fails here (SkillUseRestrictionPart), but honour the flag for forward-compat.
    public void HandlePickLockFail(MapObjectView mapObjectView, bool critical)
        => Speak(Name(mapObjectView) + Loc.T(critical ? "interact.lock_jammed" : "interact.lock_pick_failed"));

    /// <summary>"&lt;object&gt;, " prefix (e.g. "Door, ") — the same name mapping the overtip/scanner use, or empty.</summary>
    private static string Name(MapObjectView view)
    {
        if (view == null) return string.Empty;
        try
        {
            var name = InteractableDescriber.ResolveName(view, out _);
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name + ", ";
        }
        catch { return string.Empty; }
    }

    private static void Speak(string msg)
    {
        if (!string.IsNullOrEmpty(msg)) Speaker.Speak(msg, interrupt: true);
    }
}
