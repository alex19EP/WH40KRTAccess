using System.Collections.Generic;
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

    public static string GetTitle(Component comp) => Read(comp, TooltipTemplateType.Tooltip, titleOnly: true);

    public static string GetFull(Component comp) => Read(comp, TooltipTemplateType.Info, titleOnly: false);

    private static string Read(Component comp, TooltipTemplateType type, bool titleOnly)
    {
        var templates = GetTemplates(comp);
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

    private static string BrickText(ITooltipBrick brick)
    {
        TooltipBaseBrickVM vm;
        try { vm = brick?.GetVM(); } catch { return null; }
        string raw;
        switch (vm)
        {
            case null: return null;
            case TooltipBrickTitleVM t: raw = t.Title; break;
            case TooltipBrickTextVM t: raw = t.Text; break;
            case TooltipBrickIconStatValueVM t: raw = Join(t.Name, t.Value); break;
            case TooltipBrickFeatureVM t: raw = t.Name; break;
            default: return null;
        }
        return Clean(raw);
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
