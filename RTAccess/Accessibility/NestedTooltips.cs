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
    /// Bounded on purpose: every entry costs one render through the game's tooltip engine, and this runs on
    /// a keypress.
    /// </summary>
    internal static class NestedTooltips
    {
        private const int MaxEntries = 12;

        // Name-carrying members, in the order a brick VM is worth asking. Name is the feature/ability
        // bricks; Text/Label cover the stat-value and titled ones.
        private static readonly string[] LabelMembers = { "Name", "Text", "Label" };

        /// <summary>
        /// The nested tooltips <paramref name="tpl"/>'s body bricks hang off themselves, as (label, body)
        /// pairs in render order, deduped by label. Empty for a tooltip whose rows drill nowhere — which is
        /// most of them, so this stays free where it buys nothing.
        /// </summary>
        public static List<(string label, string body)> Gather(TooltipBaseTemplate tpl)
        {
            var outList = new List<(string, string)>();
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

                if (!(Member(vm, "Tooltip") is TooltipBaseTemplate nested)) continue;
                var label = Label(vm);
                if (string.IsNullOrEmpty(label)) continue;

                seen ??= new HashSet<string>();
                if (!seen.Add(label)) continue; // the same talent listed twice drills once

                string body;
                try { body = TooltipReader.GetFull(nested); }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(body)) continue;
                outList.Add((label, body));
            }
            return outList;
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
