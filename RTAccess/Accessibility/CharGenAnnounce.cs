using System.Reflection;
using System.Text;
using HarmonyLib;
using Kingmaker.EntitySystem.Stats.Base;
using Kingmaker.UI.MVVM.VM.CharGen;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Stats;
using Kingmaker.UI.MVVM.View.CharGen.Common;
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.Speech;
using RTAccess.UI;

namespace RTAccess.Accessibility;

/// <summary>
/// Character-creation orientation for blind players. CharGen is a sequence of phases shown in a top "roadmap"
/// strip (<c>CharGenRoadmapMenuView</c>) that is NOT in the console focus ring — so nothing was ever spoken
/// when entering a phase or moving Enter/Esc between phases; the player was dropped into a phase's content
/// with no "where am I" cue. This adds three things, all key-driven and therefore interrupting per
/// [[rt-interrupt-speech-rule]] (the stat readout interrupts each prior value as you hold advance/retreat):
///
/// 1. <b>Phase orientation</b> — announce phase name + position + completion whenever the phase changes
///    (postfix on <see cref="CharGenView.CurrentPhaseChangedImpl"/>, the choke point both the gamepad Next/Back
///    buttons and the Confirm/Decline advance route through). Ctrl+P re-announces it on demand.
/// 2. <b>Live stat values + points pool</b> — when a stat is advanced/retreated in the Attributes phase the
///    value changes in place WITHOUT re-firing focus, so it was spoken nowhere; we re-speak the new value and
///    the remaining points (postfix on <c>CharGenAttributesPhaseVM.HandleTryAdvanceStat</c>, which runs once
///    per adjust after the VM's own state update).
/// 3. <b>Phase description source</b> — <see cref="GetActivePhaseDescription"/> exposes the active phase's
///    InfoVM tooltip (the description of options like Homeworld/Occupation whose selector items carry no
///    tooltip of their own). Consumed by the graph-native phase contents (Selection/Career) as the Space
///    fallback on the selected-description line.
/// </summary>
internal static class CharGenAnnounce
{
    private static CharGenPhaseBaseVM _phase;
    private static CharGenVM _vm;

    /// <summary>True while the CharGen window is open (a phase is active).</summary>
    public static bool IsActive => _phase != null;

    /// <summary>Ctrl+P (registered action; see InputBindings) — re-announce the current phase + position while
    /// CharGen is open. No-op otherwise, so it's safe as an always-live Global binding.</summary>
    public static void ReAnnounce()
    {
        if (IsActive) Speak(reentry: false);
    }

    internal static void OnPhaseChanged(CharGenPhaseBaseVM phase, CharGenVM vm)
    {
        if (phase == null) return;
        bool fresh = _phase == null; // first phase after the window opened → prefix "Character creation."
        _phase = phase;
        _vm = vm;
        Speak(reentry: fresh);
    }

    internal static void OnClose()
    {
        _phase = null;
        _vm = null;
    }

    /// <summary>P3: re-speak a stat's new value and the remaining points after an advance/retreat.</summary>
    internal static void OnStatAdvanced(CharGenAttributesPhaseVM vm, StatType statType, bool advance)
    {
        try
        {
            CharGenAttributesItemVM item = null;
            foreach (var it in vm.Items)
                if (it != null && it.StatType == statType) { item = it; break; }
            if (item == null) return;
            var name = string.IsNullOrWhiteSpace(item.DisplayName) ? statType.ToString() : item.DisplayName;
            int points = vm.AvailablePointsLeft.Value;
            // Button-driven: each advance/retreat cuts the previous readout so holding the key stays responsive.
            Speaker.Speak(Loc.T("chargen.stat_readout",
                new { name, value = item.StatValue.Value, points }), interrupt: true);
        }
        catch (Exception e)
        {
            Main.Log?.Log("chargen stat read failed: " + e.Message);
        }
    }

    /// <summary>P2: the description of the currently selected option, pulled from the active phase's info panel
    /// (which the keyboard can't otherwise reach — it lives behind the gamepad-only "Information" button).</summary>
    public static string GetActivePhaseDescription()
    {
        var phase = _phase;
        if (phase == null) return null;
        var s = ReadTip(phase.InfoVM?.CurrentTooltip);
        return !string.IsNullOrWhiteSpace(s) ? s : ReadTip(phase.SecondaryInfoVM?.CurrentTooltip);
    }

    private static string ReadTip(TooltipBaseTemplate t) => t != null ? TooltipReader.GetFull(t) : null;

    private static void Speak(bool reentry)
    {
        // Phase change (Next/Back/Confirm) and Ctrl+P are both key-driven, so interrupt. The orientation line is
        // spoken first, then the phase's own screen reads its content. Per [[rt-interrupt-speech-rule]].
        try { Speaker.Speak(BuildPhaseLine(_phase, _vm, reentry), interrupt: true); }
        catch (Exception e) { Main.Log?.Log("chargen phase read failed: " + e.Message); }
    }

    private static string BuildPhaseLine(CharGenPhaseBaseVM phase, CharGenVM vm, bool reentry)
    {
        var name = phase.PhaseName?.Value;
        if (string.IsNullOrWhiteSpace(name)) name = phase.PhaseType.ToString();

        int index = -1, count = 0;
        if (vm?.PhasesCollection != null)
        {
            foreach (var p in vm.PhasesCollection)
            {
                if (ReferenceEquals(p, phase)) index = count;
                count++;
            }
        }

        var sb = new StringBuilder();
        if (reentry) sb.Append(Loc.T("chargen.intro")).Append(' ');
        sb.Append(name);
        if (index >= 0) sb.Append(". ").Append(Loc.T("nav.position", new { index = index + 1, count }));
        sb.Append('.');
        if (phase.IsCompletedAndAvailable != null && phase.IsCompletedAndAvailable.Value)
            sb.Append(' ').Append(Loc.T("chargen.phase_completed"));
        // The point-buy phase: announce the budget on entry so the player knows how much there is to spend.
        if (phase is CharGenAttributesPhaseVM attr && attr.AvailablePointsLeft.Value > 0)
            sb.Append(' ').Append(Loc.T("chargen.points_to_distribute", new { points = attr.AvailablePointsLeft.Value }));
        return sb.ToString();
    }
}

/// <summary>Announce the phase whenever CharGen switches phase (covers entry + Next/Back + Confirm/Decline).</summary>
[HarmonyPatch(typeof(CharGenView), nameof(CharGenView.CurrentPhaseChangedImpl))]
internal static class CharGenPhaseChangePatch
{
    private static void Postfix(CharGenView __instance, CharGenPhaseBaseVM viewModel)
        => CharGenAnnounce.OnPhaseChanged(viewModel, __instance.ViewModel);
}

/// <summary>Clear orientation state when the CharGen window closes.</summary>
[HarmonyPatch(typeof(CharGenView), "DestroyViewImplementation")]
internal static class CharGenClosePatch
{
    private static void Postfix() => CharGenAnnounce.OnClose();
}

/// <summary>
/// P3: re-speak the stat value + points pool after each advance/retreat. The target is the explicit interface
/// implementation <c>CharGenAttributesPhaseVM.ICharGenAttributesPhaseHandler.HandleTryAdvanceStat</c>, resolved
/// via the interface map so we don't depend on the mangled explicit-impl method name. It runs once per real
/// adjust (the view guards CanAdvance/CanRetreat before issuing the command) and after the VM's own state update.
/// </summary>
[HarmonyPatch]
internal static class CharGenStatAdvancePatch
{
    private static MethodBase TargetMethod()
    {
        var map = typeof(CharGenAttributesPhaseVM).GetInterfaceMap(typeof(ICharGenAttributesPhaseHandler));
        for (int i = 0; i < map.InterfaceMethods.Length; i++)
            if (map.InterfaceMethods[i].Name == nameof(ICharGenAttributesPhaseHandler.HandleTryAdvanceStat))
                return map.TargetMethods[i];
        return null;
    }

    private static void Postfix(CharGenAttributesPhaseVM __instance, StatType statType, bool advance)
        => CharGenAnnounce.OnStatAdvanced(__instance, statType, advance);
}
