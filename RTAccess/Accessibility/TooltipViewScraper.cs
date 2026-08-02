using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    // The prefab registry is a component that lives on the tooltip UI; find once, re-find if it's torn down.
    private static TooltipBricksView s_Config;

    private static TooltipBricksView Config =>
        s_Config != null ? s_Config : (s_Config = Resources.FindObjectsOfTypeAll<TooltipBricksView>().FirstOrDefault());

    /// <summary>True when the game's brick-view registry is reachable (i.e. scraping can run this frame).</summary>
    public static bool Available => Config != null;

    /// <summary>One scraped spoken LINE: the clean text, the raw form carrying the line's own
    /// <c>&lt;link&gt;</c> anchors, and — for an icon row (a granted talent, a stat bonus) — the nested
    /// tooltip its BRICK hangs off itself, so the row's card follows from the row too.</summary>
    internal sealed class ScrapedLine
    {
        public string Clean;
        public string Raw;
        public TooltipRef? Nested;
    }

    /// <summary>A scraped render as the reader's page source: the lines, plus the whole raw text (every
    /// fragment, dropped noise included) for the page assembler's nothing-lost safety net.</summary>
    internal sealed class ScrapeResult
    {
        public readonly List<ScrapedLine> Lines = new List<ScrapedLine>();
        public readonly StringBuilder Raw = new StringBuilder();
    }

    /// <summary>Render <paramref name="tpl"/>'s bricks through the game's factory ONCE and return the
    /// per-line page source, or null when the registry is unreachable / nothing was scraped (caller should
    /// fall back to the brick-walk). <see cref="Read"/>/<see cref="ReadRaw"/> are views over the same
    /// pass.</summary>
    public static ScrapeResult ReadPage(TooltipBaseTemplate tpl, TooltipTemplateType type)
    {
        var cfg = Config;
        if (cfg == null || tpl == null) return null;
        try { tpl.Prepare(type); } catch { }

        var r = new ScrapeResult();
        Harvest(cfg, tpl.GetHeader(type), r);
        Harvest(cfg, tpl.GetBody(type), r);
        Harvest(cfg, tpl.GetFooter(type), r);
        return r.Lines.Count > 0 || r.Raw.Length > 0 ? r : null;
    }

    /// <summary>The joined visible text (one line per '\n'), or null if nothing was scraped.</summary>
    public static string Read(TooltipBaseTemplate tpl, TooltipTemplateType type)
    {
        var page = ReadPage(tpl, type);
        if (page == null || page.Lines.Count == 0) return null;
        var sb = new StringBuilder();
        foreach (var line in page.Lines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line.Clean);
        }
        return sb.ToString();
    }

    /// <summary>Like <see cref="Read"/> but MARKUP-INTACT (no tag strip, no placeholder filter): the raw TMP
    /// source text of the rendered bricks. The link-extraction source for template-backed (factory) tooltips —
    /// <see cref="GlossaryLinks"/> matches the inline <c>&lt;link&gt;</c> tags the clean read strips. Same
    /// on-demand cost profile as <see cref="Read"/> (a Space press, never per-frame).</summary>
    public static string ReadRaw(TooltipBaseTemplate tpl, TooltipTemplateType type)
    {
        var page = ReadPage(tpl, type);
        return page != null && page.Raw.Length > 0 ? page.Raw.ToString() : null;
    }

    private static void Harvest(TooltipBricksView cfg, IEnumerable<ITooltipBrick> bricks, ScrapeResult r)
    {
        if (bricks == null) return;
        foreach (var brick in bricks)
        {
            TooltipBaseBrickVM vm;
            try { vm = brick?.GetVM(); } catch { continue; }
            if (vm == null) continue;

            MonoBehaviour view = null;
            // Line assembly per BRICK: a brick's TMP fragments are the cells of one visual row (a stat
            // brick binds name/value/bonus as sibling TMPs), so a fragment's FIRST segment joins the open
            // line with ", " — or a bare " " when the line already ends a sentence, so we never emit "., "
            // runs — while a break INSIDE a fragment (a prose brick's paragraph gap) closes the line and
            // starts the next. Splitting the RAW at the same boundaries keeps each line's <link> anchors
            // paired with the clean text they appear in.
            var lineClean = new StringBuilder();
            var lineRaw = new StringBuilder();
            int firstLineOfBrick = r.Lines.Count;
            try
            {
                view = TooltipEngine.GetBrickView(cfg, vm);
                if (view == null) continue;
                // Only ACTIVE TMP children are what a sighted player sees (bind logic disables absent fields).
                foreach (var tmp in view.GetComponentsInChildren<TMP_Text>(includeInactive: false))
                {
                    var rt = tmp?.text;
                    if (string.IsNullOrWhiteSpace(rt)) continue;
                    // The whole-raw mirror keeps EVERY fragment (newline-joined so tags never glue across
                    // fields) — dropped noise segments may still carry followable links.
                    if (r.Raw.Length > 0) r.Raw.Append('\n');
                    r.Raw.Append(rt);

                    var segs = TextUtil.SplitRichLines(rt);
                    for (int j = 0; j < segs.Count; j++)
                    {
                        var clean = TextUtil.StripRichTextLines(segs[j]);
                        // Drop prefab design-time placeholders left in active-but-unbound fields ("+++",
                        // "-//---", bare separators): a real value carries at least one letter or digit.
                        if (!TextUtil.HasLetterOrDigit(clean)) continue;
                        if (j > 0) Flush(r, lineClean, lineRaw); // a break inside the fragment = a new line
                        if (lineClean.Length > 0)
                        {
                            lineClean.Append(EndsSentence(lineClean) ? " " : ", ");
                            lineRaw.Append(' ');
                        }
                        lineClean.Append(clean);
                        lineRaw.Append(segs[j]);
                    }
                }
            }
            catch { }
            finally { if (view != null) TooltipEngine.DestroyBrickView(view); }
            // Flush outside the try so a mid-scrape fault still keeps the fragments already harvested.
            Flush(r, lineClean, lineRaw);
            // An icon row's brick hangs a nested tooltip off itself (a granted talent's card, a stat
            // bonus's glossary page) — attach it to the brick's ROW so the card follows from the row,
            // not from a References entry at the bottom of the page.
            if (r.Lines.Count > firstLineOfBrick)
                r.Lines[firstLineOfBrick].Nested = NestedTooltips.RefFor(vm);
        }
    }

    private static void Flush(ScrapeResult r, StringBuilder clean, StringBuilder raw)
    {
        if (clean.Length > 0) r.Lines.Add(new ScrapedLine { Clean = clean.ToString(), Raw = raw.ToString() });
        clean.Length = 0;
        raw.Length = 0;
    }

    /// <summary>True when the buffered brick text already ends a sentence, so the next fragment must not
    /// be glued on with ", " (that would read "., ").</summary>
    private static bool EndsSentence(StringBuilder sb)
    {
        var c = sb[sb.Length - 1];
        return c == '.' || c == '!' || c == '?' || c == '…';
    }
}
