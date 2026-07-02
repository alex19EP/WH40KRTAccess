#if DEBUG
using System.Reflection;
using RTAccess.Speech;

namespace RTAccess.Dev;

/// <summary>
/// A tiny PUBLIC surface for eval'd code to reach from. Mono.CSharp eval runs in its own dynamic
/// assembly and sees only PUBLIC members of the mod, while almost all of RTAccess is internal/static — so
/// rather than mirror the codebase, this exposes a handle to reflect from (<see cref="Asm"/>) plus a
/// couple of high-use probes. Reflection into internals is expected and fine (we reflect into the game
/// throughout the mod anyway); this just removes the boilerplate from the common cases. DEBUG-only.
///
/// <see cref="DevApi.Screen"/> and other tree probes land in Phase 2 once the ScreenManager exists.
/// </summary>
public static class DevApi
{
    /// <summary>The mod assembly — reflect into internals with
    /// <c>DevApi.Asm.GetType("RTAccess.Accessibility.UiTextReader")</c> etc.</summary>
    public static Assembly Asm => typeof(DevApi).Assembly;

    /// <summary>Speak a probe line through the real speech path (also lands in /speech).</summary>
    public static void Say(string text) => Speaker.Speak(text, true);

    /// <summary>Dump the scanner's live registry with the per-surface visibility gates, returning the summary +
    /// the list of "phantom" items (IsVisible but not CurrentlySeen — in the category browse yet missing from the
    /// M object-cycle / tile exploration) and writing a JSON report to the mod dir. The /eval-callable twin of the
    /// F11 dev key; diagnoses "shows in the scanner but not the M-cycle" reports. See
    /// <see cref="RTAccess.Diagnostics.ScannerDump"/>.</summary>
    public static string DumpScanner() => RTAccess.Diagnostics.ScannerDump.Dump(RTAccess.Main.ModDir);

    /// <summary>Explain why the scanner's I key (interact the review SELECTION) and the tile cursor's Enter can
    /// disagree — the "M-select it, I says can't interact, but Home+Enter works" report. Dumps the selection and
    /// every interactable co-located with it, per interaction part: Enabled vs live CanInteract() vs whether the
    /// game's own Interact could fire it (SelectUnit + turn state). Read-only. The /eval twin of the F8 dev key.</summary>
    public static string DebugScannerInteract() => RTAccess.Exploration.Scanner.DebugInteract();

    /// <summary>Fog-of-war reveal-mask probe for the one-time B4-fog live check (plan §B4): dumps the raw mask bytes,
    /// the uv/texel mapping, and the classification at (1) the main character — expect <c>red≥128</c> (currently
    /// visible) — and (2) the planted map cursor, if any. To confirm the channel semantics, plant the tile explorer
    /// on ground the party <i>saw then left</i> (expect <c>red&lt;128, green≥128</c> → explored) and on a
    /// <i>never-visited</i> tile (expect both <c>&lt;128</c> → never-seen), and re-run. This is what validates
    /// green=explored on the LIVE RT and that <c>ReadPixels</c> works on the fog <c>RTHandle</c> before the
    /// "unexplored" readout is trusted; if green does not hold explored, switch to the fallback in the plan.</summary>
    public static string DumpFog()
    {
        var lines = new System.Collections.Generic.List<string>();
        var mc = Kingmaker.Game.Instance?.Player?.MainCharacterEntity;
        lines.Add(mc != null
            ? "party:  " + RTAccess.Exploration.FogProbe.Dump(mc.Position)
            : "party:  (no main character)");
        lines.Add(RTAccess.Exploration.MapCursor.Has
            ? "cursor: " + RTAccess.Exploration.FogProbe.Dump(RTAccess.Exploration.MapCursor.Position)
            : "cursor: (not planted — plant the tile explorer on a seen-then-left / never-visited tile to test explored vs never-seen)");
        return string.Join("\n", lines);
    }
}
#endif
