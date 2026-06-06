using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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

        // 1) Scrape visible TextMeshPro text under the focused object.
        var sb = new StringBuilder();
        foreach (var tmp in comp.GetComponentsInChildren<TMP_Text>(includeInactive: false))
        {
            var raw = tmp != null ? tmp.text : null;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var clean = Whitespace.Replace(RichText.Replace(raw, " "), " ").Trim();
            if (clean.Length == 0) continue;
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

        // Nothing readable — surface it so coverage gaps are visible in the test.
        return new FocusReading(null, source + " (no text)");
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
