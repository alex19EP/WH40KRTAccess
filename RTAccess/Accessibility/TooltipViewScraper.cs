using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils;
using Kingmaker.Code.UI.MVVM.View.Tooltip;
using Owlcat.Runtime.UI.Tooltips;
using TMPro;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Reads a tooltip by rendering it through the GAME'S OWN view factory and harvesting the resulting visible
/// text — the true "what sighted players see" source, rather than re-deriving each brick's bind logic by hand
/// (which is what <see cref="TooltipReader"/>'s per-brick cases do and where content silently drifts/drops).
///
/// The game centralizes VM→View instantiation in <see cref="TooltipEngine.GetBrickView"/>: given the prefab
/// registry (<see cref="TooltipBricksView"/>) and a brick VM it instantiates the correct pooled view AND binds
/// it (populating every TMP field), exactly as <c>InfoBaseView.SetPart</c> / <c>TooltipBrickWidgetView</c> do.
/// We borrow each view, scrape its active <see cref="TMP_Text"/> children in hierarchy (≈visual) order, and
/// return it to the pool. Covers all brick types — including nested widget lists — by construction, with no
/// per-brick knowledge, so new/DLC bricks work for free.
///
/// Cost: instantiates+binds a view per brick (pooled after warmup), so this is for the ON-DEMAND full-detail
/// read (Space), NOT per-frame browse labels — those stay on TooltipReader's cheap curated cases. Must run on
/// the main thread (touches Unity objects); always returns borrowed views to the pool, even on error.
/// </summary>
internal static class TooltipViewScraper
{
    private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

    // The prefab registry is a component that lives on the tooltip UI; find once, re-find if it's torn down.
    private static TooltipBricksView s_Config;

    private static TooltipBricksView Config =>
        s_Config != null ? s_Config : (s_Config = Resources.FindObjectsOfTypeAll<TooltipBricksView>().FirstOrDefault());

    /// <summary>True when the game's brick-view registry is reachable (i.e. scraping can run this frame).</summary>
    public static bool Available => Config != null;

    /// <summary>Render <paramref name="tpl"/>'s Info bricks through the game's factory and return the joined
    /// visible text, or null if nothing was scraped (caller should fall back to the brick-walk).</summary>
    public static string Read(TooltipBaseTemplate tpl, TooltipTemplateType type) => Read(tpl, type, raw: false);

    /// <summary>Like <see cref="Read"/> but MARKUP-INTACT (no tag strip, no placeholder filter): the raw TMP
    /// source text of the rendered bricks. The link-extraction source for template-backed (factory) tooltips —
    /// <see cref="GlossaryLinks"/> matches the inline <c>&lt;link&gt;</c> tags the clean read strips. Same
    /// on-demand cost profile as <see cref="Read"/> (a Space press, never per-frame).</summary>
    public static string ReadRaw(TooltipBaseTemplate tpl, TooltipTemplateType type) => Read(tpl, type, raw: true);

    private static string Read(TooltipBaseTemplate tpl, TooltipTemplateType type, bool raw)
    {
        var cfg = Config;
        if (cfg == null || tpl == null) return null;
        try { tpl.Prepare(type); } catch { }

        var sb = new StringBuilder();
        Harvest(cfg, tpl.GetHeader(type), sb, raw);
        Harvest(cfg, tpl.GetBody(type), sb, raw);
        Harvest(cfg, tpl.GetFooter(type), sb, raw);
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static void Harvest(TooltipBricksView cfg, IEnumerable<ITooltipBrick> bricks, StringBuilder sb, bool raw)
    {
        if (bricks == null) return;
        foreach (var brick in bricks)
        {
            TooltipBaseBrickVM vm;
            try { vm = brick?.GetVM(); } catch { continue; }
            if (vm == null) continue;

            MonoBehaviour view = null;
            try
            {
                view = TooltipEngine.GetBrickView(cfg, vm);
                if (view == null) continue;
                // Only ACTIVE TMP children are what a sighted player sees (bind logic disables absent fields).
                foreach (var tmp in view.GetComponentsInChildren<TMP_Text>(includeInactive: false))
                {
                    if (raw)
                    {
                        // Markup-intact harvest: keep the source string as-is (link/color tags survive);
                        // newline-joined so tags never glue across fields.
                        var rt = tmp?.text;
                        if (string.IsNullOrWhiteSpace(rt)) continue;
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(rt);
                        continue;
                    }
                    var t = Clean(tmp?.text);
                    // Drop prefab design-time placeholders left in active-but-unbound fields ("+++", "-//---",
                    // bare separators): a real tooltip value always carries at least one letter or digit.
                    if (t == null || !HasAlnum(t)) continue;
                    if (sb.Length > 0) sb.Append(". ");
                    sb.Append(t);
                }
            }
            catch { }
            finally { if (view != null) TooltipEngine.DestroyBrickView(view); }
        }
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var stripped = Whitespace.Replace(Tags.Replace(s, " "), " ").Trim();
        return stripped.Length > 0 ? stripped : null;
    }

    private static bool HasAlnum(string s)
    {
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) return true;
        return false;
    }
}
