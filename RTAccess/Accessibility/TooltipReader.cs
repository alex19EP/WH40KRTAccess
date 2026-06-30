using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Reads text out of a widget's TOOLTIP without showing any UI — for icon-only slots (inventory items,
/// ability bar) whose name/description live in the tooltip rather than in visible TMP text. The focused
/// view implements <see cref="IHasTooltipTemplate"/>/<see cref="IHasTooltipTemplates"/>; we Prepare the
/// template and walk its bricks' VMs for strings.
///
/// <see cref="GetTitle"/> = just the name (used as an on-focus fallback). <see cref="GetFull"/> = name +
/// stats + description (used by the Ctrl+I "details" key — verbose, so on demand only).
/// </summary>
internal static class TooltipReader
{
    private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);

    public static string GetTitle(Component comp) => ReadTemplates(GetTemplates(comp), TooltipTemplateType.Tooltip, titleOnly: true);

    public static string GetFull(Component comp) => ReadTemplates(GetTemplates(comp), TooltipTemplateType.Info, titleOnly: false);

    /// <summary>Read a tooltip template directly (e.g. a CharGen phase's info-panel description, which has no
    /// owning focusable widget). Same brick walk as the component path.</summary>
    public static string GetFull(TooltipBaseTemplate template) => ReadTemplates(Wrap(template), TooltipTemplateType.Info, titleOnly: false);

    private static string ReadTemplates(List<TooltipBaseTemplate> templates, TooltipTemplateType type, bool titleOnly)
    {
        if (templates == null) return null;

        var sb = new StringBuilder();
        foreach (var tpl in templates)
        {
            if (tpl == null) continue;
            try { tpl.Prepare(type); } catch { }

            if (titleOnly)
            {
                var t = FirstText(tpl.GetHeader(type)) ?? FirstText(tpl.GetBody(type));
                if (!string.IsNullOrWhiteSpace(t)) return t;
                continue;
            }

            AppendAll(sb, tpl.GetHeader(type));
            AppendAll(sb, tpl.GetBody(type));
            AppendAll(sb, tpl.GetFooter(type));
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static List<TooltipBaseTemplate> GetTemplates(Component comp)
    {
        if (comp == null) return null;
        if (comp is IHasTooltipTemplates multi) return multi.TooltipTemplates();
        if (comp is IHasTooltipTemplate single) return Wrap(single.TooltipTemplate());

        var pMulti = comp.GetComponentInParent<IHasTooltipTemplates>();
        if (pMulti != null) return pMulti.TooltipTemplates();
        var pSingle = comp.GetComponentInParent<IHasTooltipTemplate>();
        if (pSingle != null) return Wrap(pSingle.TooltipTemplate());
        return null;
    }

    private static List<TooltipBaseTemplate> Wrap(TooltipBaseTemplate t) =>
        t != null ? new List<TooltipBaseTemplate> { t } : null;

    private static string FirstText(IEnumerable<ITooltipBrick> bricks)
    {
        if (bricks == null) return null;
        foreach (var brick in bricks)
        {
            var t = BrickText(brick);
            if (!string.IsNullOrWhiteSpace(t)) return t;
        }
        return null;
    }

    private static void AppendAll(StringBuilder sb, IEnumerable<ITooltipBrick> bricks)
    {
        if (bricks == null) return;
        foreach (var brick in bricks)
        {
            var t = BrickText(brick);
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (sb.Length > 0) sb.Append(". ");
            sb.Append(t);
        }
    }

    // Curated string member names to harvest from an unrecognized brick VM (covers the item-name header
    // TooltipBrickItemHeaderVM.Text and other RT brick types we don't enumerate). Order = reading order.
    private static readonly string[] BrickTextMembers = { "Title", "Header", "Name", "Text", "Value", "Label", "Description" };

    private static string BrickText(ITooltipBrick brick)
    {
        TooltipBaseBrickVM vm;
        try { vm = brick?.GetVM(); } catch { return null; }
        switch (vm)
        {
            case null: return null;
            // Known bricks: keep precise formatting (stat bricks read "Name: Value").
            case TooltipBrickTitleVM t: return Clean(t.Title);
            case TooltipBrickTextVM t: return Clean(t.Text);
            case TooltipBrickIconStatValueVM t: return Clean(Join(t.Name, t.Value));
            case TooltipBrickFeatureVM t: return Clean(t.Name);
            // Unknown brick (e.g. TooltipBrickItemHeaderVM — the item name — in a different namespace, plus
            // the many other RT brick VMs): reflect common string members so we don't silently drop content.
            default: return Clean(ReflectText(vm));
        }
    }

    /// <summary>Harvest the non-empty string members (fields or props, unwrapping ReactiveProperty) of a brick VM.</summary>
    private static string ReflectText(object vm)
    {
        var t = vm.GetType();
        string result = null;
        foreach (var name in BrickTextMembers)
        {
            object val = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(vm)
                         ?? t.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(vm);
            var s = Unwrap(val);
            if (string.IsNullOrWhiteSpace(s)) continue;
            // Join distinct members (e.g. a "Name: Value" pair) and avoid duplicate echoes.
            if (result == null) result = s.Trim();
            else if (!result.Contains(s.Trim())) result = result + ": " + s.Trim();
        }
        return result;
    }

    // Unwrap a string or a UniRx ReactiveProperty<string> (.Value).
    private static string Unwrap(object val)
    {
        if (val == null) return null;
        if (val is string s) return s;
        var valueProp = val.GetType().GetProperty("Value");
        var inner = valueProp?.GetValue(val);
        return inner as string;
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return Tags.Replace(s, " ").Trim();
    }

    private static string Join(string a, string b)
    {
        a = a?.Trim(); b = b?.Trim();
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        return a + ": " + b;
    }
}
