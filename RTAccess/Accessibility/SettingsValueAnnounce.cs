using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Code.UI.MVVM.View.Settings.Console.Entities;
using Kingmaker.Code.UI.MVVM.View.Settings.Console.Entities.Difficulty;
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Re-announce a settings value when it's changed in place with Left/Right. The console settings value widgets
/// (sliders, dropdowns, and the difficulty preset selector) adjust their value WITHOUT moving focus, so
/// <c>ConsoleEntityExtensions.SetFocused</c> never re-fires and the new value was spoken nowhere — you had to
/// F7 re-read to hear what you'd changed it to.
///
/// We postfix each widget's <c>HandleLeft</c>/<c>HandleRight</c> (the console nav adjust handlers) and speak
/// ONLY the new value (<see cref="UiTextReader.ReadAdjustableValue"/>) — not the full "name. value" focus line,
/// because these setting names are long and would be unbearable to repeat on every step. User-driven, so it
/// interrupts the previous readout (latest value wins while you arrow); see [[rt-interrupt-speech-rule]].
/// </summary>
[HarmonyPatch]
internal static class SettingsValueAnnounce
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var types = new[]
        {
            typeof(SettingsEntitySliderConsoleView),               // combat-difficulty sliders, options sliders
            typeof(SettingsEntityDropdownConsoleView),             // generic options dropdowns
            typeof(SettingsEntityDropdownGameDifficultyConsoleView) // the difficulty preset selector
        };
        foreach (var t in types)
        {
            if (AccessTools.Method(t, "HandleLeft") is MethodBase l) yield return l;
            if (AccessTools.Method(t, "HandleRight") is MethodBase r) yield return r;
        }
    }

    private static void Postfix(Component __instance)
    {
        try
        {
            var value = UiTextReader.ReadAdjustableValue(__instance);
            if (!string.IsNullOrWhiteSpace(value)) Speaker.Speak(value, interrupt: true);
        }
        catch (Exception e)
        {
            Main.Log?.Log("settings value read failed: " + e.Message);
        }
    }
}
