using System.Text.RegularExpressions;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities.Difficulty;
using Kingmaker.Code.UI.MVVM.View.Settings.Console.Entities;
using Owlcat.Runtime.UI.MVVM;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Reads the current VALUE of an adjustable settings widget — the selected dropdown option or a slider's value
/// label — for re-announcing an in-place Left/Right change without repeating the (often long) setting name,
/// which was already spoken when focus landed. Used by <see cref="SettingsValueAnnounce"/>.
///
/// The broader "describe a focused console-navigation entity" reader that used to live here was part of the
/// console-focus ride and was removed with it.
/// </summary>
internal static class UiTextReader
{
    private static readonly Regex RichText = new Regex("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

    /// <summary>Read ONLY the current value of an adjustable settings widget — the selected dropdown option or
    /// the slider's value label. Returns null for non-value widgets.</summary>
    public static string ReadAdjustableValue(Component comp)
    {
        if (comp == null) return null;
        try
        {
            if (GetViewModelOf(comp) is SettingsEntityDropdownVM dd)
            {
                var sel = GetSelectedOption(dd);
                if (!string.IsNullOrWhiteSpace(sel)) return sel;
            }
        }
        catch { }
        if (comp is SettingsEntitySliderConsoleView slider && slider.LabelSliderValue != null)
            return CleanInline(slider.LabelSliderValue.text);
        return null;
    }

    private static IViewModel GetViewModelOf(Component comp) =>
        (comp as IHasViewModel)?.GetViewModel() ?? comp.GetComponentInParent<IHasViewModel>()?.GetViewModel();

    private static string GetSelectedOption(SettingsEntityDropdownVM dd)
    {
        int index = dd.GetTempValue();
        // The difficulty selector keeps authoritative per-option titles; every other dropdown exposes the
        // parallel LocalizedValues list.
        if (dd is SettingsEntityDropdownGameDifficultyVM diff && diff.Items != null
            && index >= 0 && index < diff.Items.Count)
            return CleanInline(diff.Items[index].Title);
        if (dd.LocalizedValues != null && index >= 0 && index < dd.LocalizedValues.Count)
            return CleanInline(dd.LocalizedValues[index]);
        return null;
    }

    private static string CleanInline(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var clean = Whitespace.Replace(RichText.Replace(s, " "), " ").Trim();
        return clean.Length > 0 ? clean : null;
    }
}
