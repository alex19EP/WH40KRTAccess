using HarmonyLib;
using Owlcat.Runtime.UI.ConsoleTools;
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Speaks (and logs) UI elements as keyboard/gamepad focus lands on them — the console-UI evaluation harness.
///
/// Hook point: <c>ConsoleEntityExtensions.SetFocused(IConsoleEntity, bool)</c> — a single public static
/// choke point the navigation system calls once whenever an entity gains/loses focus. Only fires in
/// gamepad/console mode (connect a controller, or toggle it with F6 — see <see cref="Main"/>).
/// Unlabeled-but-focusable elements are announced as "Unlabeled: &lt;type&gt;" so coverage gaps are audible.
/// </summary>
[HarmonyPatch(typeof(ConsoleEntityExtensions), nameof(ConsoleEntityExtensions.SetFocused))]
internal static class SetFocusedPatch
{
    // The navigation system can re-assert focus on the same entity (recursive FocusOnEntity); dedupe it.
    private static IConsoleEntity _last;

    private static void Postfix(IConsoleEntity entity, bool value)
    {
        if (!value || entity == null || ReferenceEquals(entity, _last)) return;
        _last = entity;
        Announce(entity, navigated: true);
    }

    /// <summary>Re-read the currently focused element (bound to a hotkey).</summary>
    public static void RereadCurrent()
    {
        if (_last != null) Announce(_last, navigated: false);
        else Speaker.Speak("No focused element.", interrupt: true);
    }

    /// <summary>Read the full tooltip/details of the currently focused element (Ctrl+I; controller trigger TBD).</summary>
    public static void ReadDetailsOfCurrent()
    {
        if (_last == null) { Speaker.Speak("Nothing focused.", interrupt: true); return; }
        try
        {
            var comp = UiTextReader.ResolveComponent(_last);
            var details = comp != null ? TooltipReader.GetFull(comp) : null;
            // Fallback for CharGen options (Homeworld/Occupation/etc.) whose focusable item carries no tooltip
            // of its own — the description lives only in the phase's info panel, which the keyboard can't reach.
            if (string.IsNullOrWhiteSpace(details))
                details = CharGenAnnounce.GetActivePhaseDescription();
            // Fallback for the pre-CharGen New Game screens (campaign synopsis, difficulty/settings descriptions),
            // read straight from the focused item's view model.
            if (string.IsNullOrWhiteSpace(details) && comp != null)
                details = UiTextReader.ReadFocusedDescription(comp);
            Speaker.Speak(!string.IsNullOrWhiteSpace(details) ? details : "No details.", interrupt: true);
        }
        catch (Exception e)
        {
            Main.Log?.Log("details read failed: " + e.Message);
        }
    }

    private static void Announce(IConsoleEntity entity, bool navigated)
    {
        try
        {
            var r = UiTextReader.Describe(entity);
            FocusLog.Write(r, navigated); // structured focus log; speech goes to speech_log.txt via Speaker
            // While a wheel/radial is open, an unlabeled focus is its centre placeholder (e.g. the in-game
            // menu's m_FirstSelection) — stay silent rather than announce "Unlabeled". Wheel entries are
            // always labeled, so this hides only the placeholder, not a real coverage gap.
            if (!r.HasText && WheelMenus.ActiveWheel != null) return;
            // Interrupt only when this focus was caused by a navigation keypress. An explicit re-read (F7,
            // navigated:false) always counts. For focus driven by the game's nav system (navigated:true), it
            // counts only if a directional input fired this frame (NavInputProbe) — that's an active move, so
            // cut the now-stale readout. Focus the game sets automatically while opening/restoring/rebuilding a
            // screen runs no directional handler, so it queues instead of clipping the current line.
            bool interrupt = !navigated || NavInputProbe.FiredThisFrame;
            Speaker.Speak(r.HasText ? r.Text : ("Unlabeled: " + r.Source), interrupt: interrupt);
        }
        catch (Exception e)
        {
            Main.Log?.Log("focus read failed: " + e.Message);
        }
    }
}
