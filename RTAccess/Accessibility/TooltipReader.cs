using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Kingmaker.Blueprints.Root.Strings.GameLog;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.Localization;
using UnityEngine;
// Aliases: brick VMs are split across two "Tooltip.Bricks" namespaces plus a combat-log sub-namespace with
// overlapping simple names — alias them so the switch below can reference both without ambiguity.
using AS = Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.AbilityScores;
using CL = Kingmaker.UI.MVVM.VM.Tooltip.Bricks.CombatLog;
using UIB = Kingmaker.UI.MVVM.VM.Tooltip.Bricks;

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

    public static string GetFull(Component comp) => ReadFull(GetTemplates(comp));

    /// <summary>Read a tooltip template directly (e.g. a CharGen phase's info-panel description, which has no
    /// owning focusable widget). Same read path as the component overload.</summary>
    public static string GetFull(TooltipBaseTemplate template) => ReadFull(Wrap(template));

    /// <summary>Full-detail read (Space, the browse-label drill-in). Primary path renders each template through
    /// the game's OWN view factory and scrapes the visible text (<see cref="TooltipViewScraper"/>) — the fix that
    /// mirrors exactly what sighted players see, for every brick type. Falls back per-template to the curated
    /// brick-walk (<see cref="BrickText"/>) when the scraper can't run (registry not yet loaded).</summary>
    private static string ReadFull(List<TooltipBaseTemplate> templates)
    {
        if (templates == null) return null;
        var sb = new StringBuilder();
        foreach (var tpl in templates)
        {
            if (tpl == null) continue;
            var text = TooltipViewScraper.Read(tpl, TooltipTemplateType.Info);
            if (string.IsNullOrWhiteSpace(text))
            {
                // Fallback: the curated typed-case walk (still covers ~half the bricks, and all of GetTitle).
                try { tpl.Prepare(TooltipTemplateType.Info); } catch { }
                var fb = new StringBuilder();
                AppendAll(fb, tpl.GetHeader(TooltipTemplateType.Info));
                AppendAll(fb, tpl.GetBody(TooltipTemplateType.Info));
                AppendAll(fb, tpl.GetFooter(TooltipTemplateType.Info));
                text = fb.Length > 0 ? fb.ToString() : null;
            }
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (sb.Length > 0) sb.Append(". ");
            sb.Append(text);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

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
        // Typed cases below mirror what each brick's View shows sighted players — Owlcat bricks keep their
        // content in domain-named fields, nested VMs and lists that the reflection fallback (7 exact names)
        // silently drops. Ordering is load-bearing: a C# switch matches top-to-bottom, so every derived brick
        // MUST precede its base or its case is shadowed. See docs/tooltip-reader-audit.md for the full survey.
        switch (vm)
        {
            case null: return null;
            case TooltipBrickTitleVM t: return Clean(t.Title);
            case TooltipBrickTextVM t: return Clean(t.Text);
            // Buff precedes Feature (subclass). Mirrors the buff card: name + remaining duration + stack + source.
            case TooltipBrickBuffVM b: return Clean(Concat(b.Name, b.Duration?.Value, b.Stack, b.SourceName));
            // Feature/ability/talent: name + the two on-card context lines (AP cost / ability type; shots / ammo).
            case TooltipBrickFeatureVM t: return Clean(Concat(t.Name, t.AdditionalField1, t.AdditionalField2));
            case TooltipBrickIconStatValueVM t: return Clean(IconStatText(t));
            // Character stats / skills: the whole grid lives in a nested VM's Stats list (name + value each).
            case TooltipBrickAbilityScoresBlockVM t: return Clean(StatsBlockText(t.AbilityScoresBlock));
            case TooltipBrickAbilityScoresVM t: return Clean(StatsBlockText(t.AbilityScoresBlock));
            case TooltipBrickSkillsVM t: return Clean(StatsBlockText(t.AbilityScoresBlock));
            // Column/row text bricks (weight/price/"usable by" footer rows, stat pairs). Triple precedes its
            // DoubleText base; ItemFooter (also a DoubleText) is covered by the base case's Left/Right read.
            case TooltipBrickTripleTextVM t: return Clean(Concat(t.LeftLine, t.MiddleLine, t.RightLine));
            case TooltipBrickDoubleTextVM t: return Clean(Join(t.LeftLine, t.RightLine));
            case TooltipBrickTwoColumnsStatVM t: return Clean(Concat(Join(t.NameLeft, t.ValueLeft), Join(t.NameRight, t.ValueRight)));
            case TooltipBrickEntityHeaderVM t: return Clean(Concat(t.MainTitle, t.Title, t.LeftLabel, t.RightLabel, t.RightLabelClassification));
            // Formula "Value Symbol Name" — space-joined so the operator glyph survives (e.g. "5 + Strength").
            case TooltipBrickValueStatFormulaVM t: return Clean(SpaceJoin(t.Value, t.Symbol, t.Name));
            case TooltipBrickArmorStatsVM t: return Clean(ArmorText(t));
            case TooltipBrickWeaponSetVM t: return Clean(t.Weapon?.Name);
            case UIB.TooltipBrickRankEntrySelectionVM t: return Clean(t.RankEntrySelectionVM?.SelectedFeature?.Value?.DisplayName);
            // The prerequisite brick keeps its content in a list of entry VMs (each a Text/Value), not a flat
            // string member — so the reflection fallback below misses it and the requirement (e.g. a locked
            // career path's "requires completing X") gets silently dropped. Render the entries explicitly.
            case TooltipBrickPrerequisiteVM p: return Clean(PrereqText(p));
            // Combat-log bricks: specific numeric payloads precede the shared base (a subclass would be shadowed).
            case CL.TooltipBrickChanceVM c: return Clean(Concat(c.Name, ChanceText(c), ResultOf(c)));
            case CL.TooltipBrickDamageRangeVM d: return Clean(Concat(d.Name, d.CurrentValue + " (" + d.MinValue + "-" + d.MaxValue + ")", ResultOf(d)));
            case CL.TooltipBrickIconTextValueVM i: return Clean(Concat(i.Name, i.Value, ResultOf(i)));
            case CL.TooltipBrickDamageNullifierVM d: return Clean(NullifierText(d));
            case CL.TooltipBrickMinimalAdmissibleDamageVM m: return Clean(Concat(GameLogStrings.Instance.TooltipBrickStrings.MinimalAdmissibleDamageHeader.Text, "=" + m.MinimalAdmissibleDamage, m.ReasonValue));
            case CL.TooltipBrickShotDirectionWithNameVM d: return Clean(Concat(Loc.T("tooltip.shot", new { n = d.ShotNumber }), Loc.T("tooltip.deviation", new { value = d.DeviationValue, min = d.DeviationMin, max = d.DeviationMax })));
            case CL.TooltipBrickShotDirectionVM d: return Clean(Loc.T("tooltip.deviation", new { value = d.DeviationValue, min = d.DeviationMin, max = d.DeviationMax }));
            case CL.TooltipBrickTriggeredAutoVM t: return Clean(BuffListText(t.TriggeredAutoText, t.ReasonBuffItems));
            case CL.TooltipBrickNestedMessageVM n: return Clean(n.ShotNumber > 0 ? Concat(Loc.T("tooltip.shot", new { n = n.ShotNumber }), n.Text) : n.Text);
            case CL.TooltipBrickTextSignatureValueVM t: return Clean(Concat(t.Text, t.SignatureText, t.Value));
            case CL.TooltipBrickCombatLogBaseVM b: return Clean(Concat(b.Name, ResultOf(b)));
            // Unknown brick (e.g. TooltipBrickItemHeaderVM — the item name — in a different namespace, plus
            // the many other RT brick VMs): reflect common string members so we don't silently drop content.
            default: return Clean(ReflectText(vm));
        }
    }

    // Flatten a prerequisite brick into a spoken requirement line: each entry's Text (a group entry already
    // pre-joins its children), joined with "; "; "one of" when the brick is an OR-composition. This is where a
    // locked career path's reason surfaces (the game only emits this brick when the path is not unlocked).
    private static string PrereqText(TooltipBrickPrerequisiteVM p)
    {
        var entries = p?.PrerequisiteEntries;
        if (entries == null || entries.Count == 0) return null;
        var parts = new List<string>();
        foreach (var e in entries) CollectPrereq(e, parts);
        if (parts.Count == 0) return null;
        return Loc.T(p.OneFromList ? "tooltip.requires_one" : "tooltip.requires",
            new { text = string.Join("; ", parts) });
    }

    private static void CollectPrereq(PrerequisiteEntryVM e, List<string> parts)
    {
        if (e == null) return;
        var t = e.Text;
        if (!string.IsNullOrWhiteSpace(t))
        {
            t = t.Replace("\n", "; ").Trim();
            if (!string.IsNullOrWhiteSpace(e.Value) && !t.Contains(e.Value)) t += " " + e.Value.Trim();
            parts.Add(t);
        }
        else if (e.Prerequisites != null)
            foreach (var c in e.Prerequisites) CollectPrereq(c, parts);
    }

    // A character-stats / skills block keeps its rows in a nested VM's Stats list (each a name + value). The
    // View shows every enabled stat; a disabled one renders "-". Mirror that.
    private static string StatsBlockText(AS.CharInfoBaseAbilityScoresBlockVM block)
    {
        if (block?.Stats == null) return null;
        var parts = new List<string>();
        foreach (var s in block.Stats)
        {
            if (s == null) continue;
            var name = s.Name?.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;
            parts.Add(Join(name, s.IsValueEnabled.Value ? s.StatValue.Value.ToString() : "-"));
        }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    // Icon-stat brick: name + value (+ an optional bonus/delta and icon caption). Prefer the live reactive value.
    private static string IconStatText(TooltipBrickIconStatValueVM t)
    {
        var val = t.ReactiveValue?.Value ?? t.Value;
        var add = t.ReactiveAddValue?.Value ?? t.AddValue;
        var s = Join(t.Name, val);
        if (!string.IsNullOrWhiteSpace(add)) s = Join(s, add);
        if (!string.IsNullOrWhiteSpace(t.IconText)) s = Join(s, t.IconText);
        return s;
    }

    // Armour brick shows deflection / absorption / dodge; the values are game-computed strings, the row labels
    // are ours (the game keeps them view-side). Map each value to its semantically-correct label.
    private static string ArmorText(TooltipBrickArmorStatsVM a)
        => Concat(Join(Loc.T("tooltip.armor_deflection"), a.ArmorDeflection),
                  Join(Loc.T("tooltip.armor_absorption"), a.ArmorAbsorption),
                  Join(Loc.T("tooltip.dodge"), a.Dodge));

    // Chance brick: reproduce the RollSlider readout — "current op sufficient%" (op is =/</>), or just the
    // target "% " when there is no current roll.
    private static string ChanceText(CL.TooltipBrickChanceVM c)
    {
        if (!c.CurrentValue.HasValue) return c.SufficientValue + "%";
        string op = c.CurrentValue.Value == c.SufficientValue ? "=" : (c.CurrentValue.Value < c.SufficientValue ? "<" : ">");
        return c.CurrentValue.Value + " " + op + " " + c.SufficientValue + "%";
    }

    // The trailing result value shared by every combat-log brick (shown only when flagged).
    private static string ResultOf(CL.TooltipBrickCombatLogBaseVM b)
        => b.IsResultValue && !string.IsNullOrEmpty(b.ResultValue) ? b.ResultValue : null;

    private static string NullifierText(CL.TooltipBrickDamageNullifierVM d)
    {
        var parts = new List<string> { d.ReasonText, "=" + d.ResultValue, d.ResultText };
        if (d.ReasonBuffItems != null)
            foreach (var r in d.ReasonBuffItems) if (r != null) parts.Add(r.Name);
        return Concat(parts.ToArray());
    }

    private static string BuffListText(string lead, List<CL.ReasonBuffItemVM> items)
    {
        var parts = new List<string> { lead };
        if (items != null)
            foreach (var r in items) if (r != null) parts.Add(r.Name);
        return Concat(parts.ToArray());
    }

    // Trim, drop empties, join. Concat = comma-separated (list of fields); SpaceJoin keeps operator glyphs.
    private static string Concat(params string[] parts) => JoinParts(", ", parts);

    private static string SpaceJoin(params string[] parts) => JoinParts(" ", parts);

    private static string JoinParts(string sep, params string[] parts)
    {
        var list = new List<string>();
        foreach (var p in parts) { var s = p?.Trim(); if (!string.IsNullOrEmpty(s)) list.Add(s); }
        return list.Count == 0 ? null : string.Join(sep, list);
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
