using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Kingmaker.Code.UI.MVVM.VM.NewGame.Story;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities.Difficulty;
using Kingmaker.Code.UI.MVVM.View.Settings.Console.Entities;
using Owlcat.Runtime.UI.ConsoleTools;
using Owlcat.Runtime.UI.ConsoleTools.NavigationTool;
using Owlcat.Runtime.UI.MVVM;
using Owlcat.Runtime.UI.VirtualListSystem;
using TMPro;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>Result of reading a focused entity: the spoken text plus where it came from (for the test log).</summary>
internal readonly struct FocusReading
{
    public readonly string Text;
    public readonly string Source; // resolved component/entity type + how we read it
    public FocusReading(string text, string source) { Text = text; Source = source; }
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// Turns a focused console-navigation entity into a spoken string, for evaluating RT's console focus UI.
///
/// Layered read: (1) scrape visible TMP text under the focused object; (2) fall back to the ViewModel's
/// common label properties when there is no TMP text. Resolves <c>IConsoleEntityProxy</c> wrappers and
/// <c>IMonoBehaviour</c> entities (e.g. SimpleConsoleNavigationEntity, which exposes its button via
/// <c>IMonoBehaviour.MonoBehaviour</c> rather than being a Component itself).
/// </summary>
internal static class UiTextReader
{
    private static readonly Regex RichText = new Regex("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly string[] VmTextProps = { "Name", "Title", "DisplayName", "Label", "Text", "Header", "Value" };

    public static FocusReading Describe(IConsoleEntity entity)
    {
        // Synthetic items we injected into the nav ring carry their own spoken text — read it directly,
        // before any component scraping (see VirtualNavItem / DialogNavAugmentor).
        if (entity is IAccessibleTextProvider provider)
            return new FocusReading(provider.GetAccessibleText(), "VirtualNavItem");

        var entityType = entity?.GetType().Name ?? "null";
        var comp = ResolveComponent(entity);
        if (comp == null) return new FocusReading(null, entityType + " (unresolved)");

        var source = comp.GetType().Name;

        // 0) Dropdown/preset selectors render EVERY option as an active child label, so the generic TMP scrape
        //    below would read the whole option list (the difficulty preset selector read all of
        //    "Custom. Story. Normal. Daring. Hard. Unfair" instead of the chosen one). Read the setting name +
        //    the SELECTED option from the view model instead.
        var dropdown = TryReadDropdown(comp);
        if (!string.IsNullOrWhiteSpace(dropdown)) return new FocusReading(dropdown, source + " (dropdown)");

        // 1) Scrape visible TextMeshPro text under the focused object.
        var sb = new StringBuilder();
        foreach (var tmp in comp.GetComponentsInChildren<TMP_Text>(includeInactive: false))
        {
            var raw = tmp != null ? tmp.text : null;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var clean = Whitespace.Replace(RichText.Replace(raw, " "), " ").Trim();
            if (clean.Length == 0) continue;
            // Skip decorative/placeholder fragments with no spoken content — e.g. the unbound subtitle label in
            // the pregen/ship/appearance selector prefabs that sits at its design-time default "- //" (a dash +
            // the game's "//" value separator with nothing filled in). Numbers/letters (e.g. "+5", "-60%") stay.
            if (!HasLetterOrDigit(clean)) continue;
            if (sb.Length > 0) sb.Append(". ");
            sb.Append(clean);
        }
        if (sb.Length > 0) return new FocusReading(sb.ToString(), source);

        // 2) Fallback: read the ViewModel's common label members.
        var vmText = TryReadViewModel(comp);
        if (!string.IsNullOrWhiteSpace(vmText)) return new FocusReading(vmText, source + " (vm)");

        // 3) Fallback: the item/ability name from the tooltip header (icon-only slots have no TMP and
        //    no top-level name member — e.g. inventory/ability slots). Just the title; details on Ctrl+I.
        var tipTitle = TooltipReader.GetTitle(comp);
        if (!string.IsNullOrWhiteSpace(tipTitle)) return new FocusReading(tipTitle, source + " (tip)");

        // 4) An item/equip slot that holds nothing produces no TMP/VM/tooltip text — but that is an EMPTY
        //    slot, not a coverage gap. Announce it explicitly (a blind player needs "empty", not silence),
        //    which also disambiguates the log: an empty slot reads "Empty", a genuinely unread widget "(no text)".
        if (IsEmptyItemSlot(comp)) return new FocusReading("Empty", source + " (empty)");

        // Nothing readable — surface it so coverage gaps are visible in the test.
        return new FocusReading(null, source + " (no text)");
    }

    /// <summary>Read a settings dropdown/selector as "name. selected option" from its view model, rather than
    /// scraping the (all-options-active) child labels. Covers the generic <see cref="SettingsEntityDropdownVM"/>
    /// and the difficulty preset selector (<see cref="SettingsEntityDropdownGameDifficultyVM"/>, whose inline
    /// item layout is what caused the whole difficulty list to be read). Returns null for non-dropdowns.</summary>
    private static string TryReadDropdown(Component comp)
    {
        try
        {
            if (!(GetViewModelOf(comp) is SettingsEntityDropdownVM dd)) return null;
            var name = CleanInline(dd.Title);
            var selected = GetSelectedOption(dd);
            if (string.IsNullOrEmpty(selected)) return name; // name only when the value can't be resolved
            return string.IsNullOrEmpty(name) ? selected : name + ". " + selected;
        }
        catch { return null; }
    }

    /// <summary>Read ONLY the current value of an adjustable settings widget — the selected dropdown option or
    /// the slider's value label — for re-announcing in-place Left/Right changes without repeating the (often
    /// long) setting name, which was already spoken when focus landed. Returns null for non-value widgets.</summary>
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

    /// <summary>True if the string has any letter or digit (Unicode-aware, so Cyrillic counts). Used to drop
    /// punctuation-only TMP fragments that carry no spoken information.</summary>
    private static bool HasLetterOrDigit(string s)
    {
        foreach (var c in s) if (char.IsLetterOrDigit(c)) return true;
        return false;
    }

    /// <summary>Ctrl+I description for the pre-CharGen New Game screens, read straight from the focused item's
    /// view model (so there's no staleness — it only fires when one of these items is actually focused):
    /// the difficulty preset's per-option description, any settings widget's tooltip text, and a story
    /// campaign's synopsis. Returns null for anything else (the caller then says "No details").</summary>
    public static string ReadFocusedDescription(Component comp)
    {
        try
        {
            switch (GetViewModelOf(comp))
            {
                // Difficulty preset selector — the selected preset's own title + description (the meat).
                case SettingsEntityDropdownGameDifficultyVM diff:
                {
                    int i = diff.GetTempValue();
                    if (diff.Items != null && i >= 0 && i < diff.Items.Count)
                        return Join(diff.Items[i].Title, diff.Items[i].Description);
                    return null;
                }
                // Any other settings widget (sliders, dropdowns) — its tooltip description, when it has one.
                case SettingsEntityVM setting:
                    return string.IsNullOrWhiteSpace(setting.Description) ? null : Join((string)setting.Title, setting.Description);
                // Story / campaign picker — the campaign synopsis (Campaign is private, reachable via publicize).
                case NewGamePhaseStoryScenarioEntityVM story:
                {
                    var campaign = story.Campaign;
                    return Join(story.Title, campaign != null ? (string)campaign.Description : null);
                }
                default:
                    return null;
            }
        }
        catch { return null; }
    }

    private static string Join(string a, string b)
    {
        a = CleanInline(a);
        b = CleanInline(b);
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        return a + ". " + b;
    }

    private static IViewModel GetViewModelOf(Component comp) =>
        (comp as IHasViewModel)?.GetViewModel() ?? comp.GetComponentInParent<IHasViewModel>()?.GetViewModel();

    /// <summary>True if the focused widget is an item/equip/ability slot whose VM reports no item
    /// (ItemSlotVM and friends expose <c>bool HasItem</c>). Read reflectively so we don't bind the reader to
    /// every slot VM type. Returns false when there is no <c>HasItem</c> member, so non-slot widgets are
    /// never mislabelled "Empty".</summary>
    private static bool IsEmptyItemSlot(Component comp)
    {
        try
        {
            var vm = GetViewModelOf(comp);
            if (vm == null) return false;
            var hasItem = vm.GetType().GetProperty("HasItem", BindingFlags.Public | BindingFlags.Instance);
            return hasItem != null && hasItem.PropertyType == typeof(bool) && !(bool)hasItem.GetValue(vm);
        }
        catch { return false; }
    }

    private static string GetSelectedOption(SettingsEntityDropdownVM dd)
    {
        int index = dd.GetTempValue();
        // The difficulty selector keeps authoritative per-option titles (and uses them for its own on-focus
        // description), so prefer those; every other dropdown exposes the parallel LocalizedValues list.
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

    private static string TryReadViewModel(Component comp)
    {
        try
        {
            var hasVm = comp.GetComponent<IHasViewModel>() ?? comp.GetComponentInParent<IHasViewModel>();
            var vm = hasVm?.GetViewModel();
            if (vm == null) return null;
            var t = vm.GetType();
            foreach (var name in VmTextProps)
            {
                // Owlcat VMs hold their text in public readonly FIELDS (ReactiveProperty<string>), not
                // properties — so check both (e.g. ItemSlotVM.DisplayName is a field).
                var val = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(vm)
                          ?? t.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(vm);
                var s = ExtractString(val);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        catch { }
        return null;
    }

    // Unwrap a string, a UniRx ReactiveProperty<T> (.Value), or anything else via ToString().
    private static string ExtractString(object val)
    {
        if (val == null) return null;
        if (val is string s) return s;
        var valueProp = val.GetType().GetProperty("Value");
        if (valueProp != null)
        {
            var inner = valueProp.GetValue(val);
            return inner is string vs ? vs : inner?.ToString();
        }
        return val.ToString();
    }

    /// <summary>Resolve proxy wrappers, then return the underlying Unity component (Component or IMonoBehaviour).</summary>
    internal static Component ResolveComponent(IConsoleEntity entity)
    {
        var guard = 0;
        while (entity is IConsoleEntityProxy proxy
               && proxy.ConsoleEntityProxy != null
               && !ReferenceEquals(proxy.ConsoleEntityProxy, entity)
               && guard++ < 16)
        {
            entity = proxy.ConsoleEntityProxy;
        }
        if (entity is Component c) return c;
        if (entity is IMonoBehaviour mb) return mb.MonoBehaviour; // SimpleConsoleNavigationEntity, etc.
        // Virtual-list rows proxy to their row view via .View when it isn't itself an IConsoleEntity.
        if (entity is VirtualListElement vle && vle.View != null) return vle.View.RectTransform;
        return null;
    }
}
