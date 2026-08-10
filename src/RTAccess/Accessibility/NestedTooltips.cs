using Access.Core;          // TextUtil
using System.Reflection;
using Owlcat.Runtime.UI.Tooltips;          // TooltipBaseTemplate, ITooltipBrick, TooltipTemplateType

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Pulls the DRILL-IN tooltips a rendered tooltip hangs off its own rows, so they can be offered as
    /// entries beside the body (see <see cref="RTAccess.UI.TooltipChooser"/>).
    ///
    /// The game builds a tooltip from bricks, and several brick kinds carry a nested
    /// <see cref="TooltipBaseTemplate"/> of their own — a homeworld's granted talents
    /// (<c>TooltipBrickFeature</c> → <c>TooltipTemplateFeature</c>), the stat bonuses it hands out
    /// (<c>TooltipBrickIconStatValue</c> → <c>TooltipTemplateGlossary</c>), and so on. On screen those are
    /// icons you HOVER for the explanation; flattened to text they are bare names, which is how a homeworld
    /// could list "Luck" with no way to find out what Luck does. Gathering them restores the hover.
    ///
    /// Read by reflection rather than by type: the brick VMs are split across two namespaces
    /// (<c>Kingmaker.Code.UI.MVVM.VM.Tooltip.Bricks</c> and <c>Kingmaker.UI.MVVM.VM.Tooltip.Bricks</c>) and
    /// eight of them declare the same <c>Tooltip</c> + name pair independently, with no shared interface —
    /// the same reason <see cref="TooltipReader"/> checks field OR property.
    ///
    /// Each entry carries the nested TEMPLATE, not its rendered text, so the page it opens can be drilled
    /// again (a granted talent's card links the buff it applies, and so on) — see <see cref="TooltipRef"/>.
    /// That also makes gathering nearly free: it used to render every nested tooltip through the game's
    /// engine up front, on a keypress, for entries you would mostly never open. The cap is now only a
    /// runaway guard on a pathological brick list, not a cost ceiling.
    ///
    /// The deliberate cost of going lazy: an entry whose template renders to nothing can no longer be
    /// dropped up front, so it answers "No tooltip" when opened instead of being absent. Proving it empty
    /// means rendering it, which is the very work being deferred. In practice a nested template is a
    /// feature/ability card and always carries at least its own name.
    /// </summary>
    internal static class NestedTooltips
    {
        private const int MaxEntries = 64;

        // Name-carrying members, in the order a brick VM is worth asking. Name is the feature/ability
        // bricks; Text/Label cover the stat-value and titled ones.
        private static readonly string[] LabelMembers = { "Name", "Text", "Label" };

        /// <summary>
        /// The nested tooltips <paramref name="tpl"/>'s body bricks hang off themselves, in render order,
        /// deduped by label. Empty for a tooltip whose rows drill nowhere — which is most of them, so this
        /// stays free where it buys nothing. Entries carry the same label-derived Id
        /// <see cref="RefFor"/> stamps, so a nested tooltip already attached to its own body LINE is
        /// filtered out of the trailing References list (see <see cref="RTAccess.UI.TooltipChooser"/>).
        /// </summary>
        public static List<TooltipRef> Gather(TooltipBaseTemplate tpl)
        {
            var outList = new List<TooltipRef>();
            if (tpl == null) return outList;

            IEnumerable<ITooltipBrick> bricks;
            try { bricks = tpl.GetBody(TooltipTemplateType.Info); }
            catch { return outList; }
            if (bricks == null) return outList;

            HashSet<string> seen = null;
            foreach (var brick in bricks)
            {
                if (outList.Count >= MaxEntries) break;
                object vm;
                try { vm = brick?.GetVM(); }
                catch { continue; }
                if (vm == null) continue;

                var r = RefFor(vm);
                if (r == null) continue;

                seen ??= new HashSet<string>();
                if (!seen.Add(r.Value.Label)) continue; // the same talent listed twice drills once

                outList.Add(r.Value);
            }
            return outList;
        }

        /// <summary>The drill-in reference ONE brick VM hangs off itself (its nested template + its own
        /// row label), or null when the brick drills nowhere. The per-line attachment source for the
        /// scrape pipeline — the row's card follows from the row itself, like everything else on a line.
        /// The Id is label-derived so the line attachment and <see cref="Gather"/>'s page-level sweep
        /// dedup against each other.</summary>
        public static TooltipRef? RefFor(object vm)
        {
            if (!(Member(vm, "Tooltip") is TooltipBaseTemplate nested)) return null;
            var label = Label(vm);
            if (string.IsNullOrEmpty(label)) return null;
            return new TooltipRef(label, () => nested, "nested:" + label);
        }

        private static string Label(object vm)
        {
            foreach (var name in LabelMembers)
            {
                var s = Member(vm, name) as string;
                if (string.IsNullOrWhiteSpace(s)) continue;
                var clean = TextUtil.StripRichTextSpaced(s);
                if (!string.IsNullOrWhiteSpace(clean)) return clean;
            }
            return null;
        }

        // Field OR property — the brick VMs expose these as public readonly FIELDS as often as properties.
        private static object Member(object o, string name)
        {
            var t = o?.GetType();
            if (t == null) return null;
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            try
            {
                var f = t.GetField(name, Flags);
                if (f != null) return f.GetValue(o);
                var p = t.GetProperty(name, Flags);
                return p != null && p.CanRead ? p.GetValue(o) : null;
            }
            catch { return null; }
        }
    }
}
