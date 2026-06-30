#if DEBUG
using System;
using System.Text;
using RTAccess.Screens;
using RTAccess.UI;

namespace RTAccess.Dev
{
    /// <summary>
    /// Interpreted dump of the focused screen's element tree — the mod's OWN view, rendered the way the
    /// navigator announces each node (label + role + state, via GetFocusText), with the currently-focused
    /// node marked. The dev driver's /gui: lets me see what nav state the mod is in without the user's ears.
    /// DEBUG-only.
    /// </summary>
    internal static class GuiInspector
    {
        public static string Dump()
        {
            var sb = new StringBuilder();
            var screen = ScreenManager.Current;
            if (screen == null) return "(no active screen)\n";

            var focused = Navigation.Current;
            sb.Append("screen: ").Append(screen.Key).Append(" | ").Append(screen.ScreenName ?? "");
            if (focused == null) sb.Append("  (nothing focused)");
            sb.Append('\n');

            foreach (var child in screen.Children) DumpElement(child, 1, focused, sb);
            return sb.ToString();
        }

        private static void DumpElement(UIElement el, int depth, UIElement focused, StringBuilder sb)
        {
            if (el == null) return;
            sb.Append(' ', depth * 2);
            sb.Append(ReferenceEquals(el, focused) ? "> " : "  ");
            string text = SafeText(el);
            if (!string.IsNullOrEmpty(text)) sb.Append(text).Append("  ");
            sb.Append('[').Append(el.GetType().Name);
            if (!el.CanFocus) sb.Append(" ·structural");
            sb.Append("]\n");
            if (el is Container c)
                foreach (var child in c.Children) DumpElement(child, depth + 1, focused, sb);
        }

        // Some elements resolve live game data when rendering announcements — never let one throw kill the dump.
        private static string SafeText(UIElement el)
        {
            try { return el.GetFocusText(); }
            catch (Exception e) { return "<err: " + e.Message + ">"; }
        }
    }
}
#endif
