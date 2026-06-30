using HarmonyLib;
using Owlcat.Runtime.UI.ConsoleTools.NavigationTool;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Records the frame on which the console navigation system processed a <b>directional</b> user input
/// (stick / d-pad / arrow move). This is the provenance signal <see cref="SetFocusedPatch"/> uses to tell a
/// user-driven focus move (you pressed a direction → cut the now-stale readout) apart from a focus the game
/// sets <i>automatically</i> while building, opening, restoring, or rebuilding a screen (→ queue, don't clip).
/// It is provenance, not timing — no guessing.
///
/// Hook: prefix on <c>ConsoleNavigationBehaviour.HandlePressed</c>, the single per-frame dispatcher that the
/// nav system's <c>Tick</c> calls ONLY when a direction is actually pressed/repeated. The path is
/// <c>OnLeftIsPressed → Tick → HandlePressed → HandleLeft → FocusOnEntity → SetFocused</c> (decompiled
/// ConsoleNavigationBehaviour), all synchronous within one frame, so a same-frame match means "this focus came
/// from a navigation keypress". Automatic focus runs <c>SetFocus(true) → FocusOnCurrentEntity</c> and never
/// touches <c>HandlePressed</c>, so it never matches. <c>HandlePressed</c> lives only on the base class (Grid /
/// Float behaviours inherit it), so this one hook covers every console navigation block.
/// </summary>
[HarmonyPatch(typeof(ConsoleNavigationBehaviour), "HandlePressed")]
internal static class NavInputProbe
{
    private static int _lastFrame = -1;

    /// <summary>True iff the console nav processed a directional input on the current frame.</summary>
    internal static bool FiredThisFrame => _lastFrame == Time.frameCount;

    // Prefix (not postfix): the stamp must be set before HandlePressed calls FocusOnEntity → SetFocused, so the
    // focus reader sees it on the same frame.
    private static void Prefix() => _lastFrame = Time.frameCount;
}
