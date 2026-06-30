using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using RTAccess.Settings;

namespace RTAccess.UI.Announcements
{
    /// <summary>
    /// Builds the announcement settings (ported from SayTheSpire2). For EVERY concrete
    /// <see cref="Announcement"/> subclass: a global category <c>announcements.{key}</c> with an
    /// <c>enabled</c> toggle + <c>include_suffix</c> toggle (+ anything a static
    /// <c>RegisterSettings(CategorySetting)</c> on the announcement declares), hidden from the UI unless
    /// the announcement carries <see cref="ShowInGlobalSettingsAttribute"/>. For EVERY concrete
    /// <see cref="UIElement"/> with an <c>[AnnouncementOrder]</c>: per-element-type overrides at
    /// <c>ui.{element}.announcements.{ann}.{setting}</c> — a <see cref="NullableBoolSetting"/> mirroring
    /// each global setting (inherits the global until the user overrides it). <see cref="AnnouncementContext"/>
    /// resolves per-element override → global → default. (WotR has no buffer/hotkey contexts; the
    /// announcement-reorder feature is not ported yet.)
    /// </summary>
    public static class AnnouncementRegistry
    {
        public static void RegisterDefaults()
        {
            var asm = Assembly.GetExecutingAssembly();
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            // Globals first — per-element overrides reference them as fallbacks.
            foreach (var t in types.Where(t => !t.IsAbstract && typeof(Announcement).IsAssignableFrom(t)))
            {
                try { RegisterGlobal(t); }
                catch (Exception e) { Main.Log?.Error("[ann] global register failed for " + t.Name + ": " + e.Message); }
            }

            foreach (var t in types.Where(t => !t.IsAbstract && typeof(UIElement).IsAssignableFrom(t)
                                               && !typeof(RTAccess.Screens.Screen).IsAssignableFrom(t) // screens aren't focusable elements
                                               && t.GetCustomAttribute<AnnouncementOrderAttribute>(true) != null))
            {
                try { RegisterElementOverrides(t); }
                catch (Exception e) { Main.Log?.Error("[ann] per-element register failed for " + t.Name + ": " + e.Message); }
            }
        }

        private static void RegisterGlobal(Type annType)
        {
            var key = DeriveAnnouncementKey(annType);
            var display = DeriveDisplayName(StripSuffix(annType.Name, "Announcement"));
            var category = ModSettingsRegistry.EnsureCategory("announcements." + key, "Announcements/" + display,
                "/announcement." + key); // "announcements" root segment skipped (empty), leaf gets the loc key

            // Created either way (per-element overrides need it as a fallback); shown only if opted in.
            category.Hidden = annType.GetCustomAttribute<ShowInGlobalSettingsAttribute>() == null;

            if (category.GetByKey("enabled") == null)
                category.Add(new BoolSetting("enabled", "Announce", true, "ann.enabled"));
            if (category.GetByKey("include_suffix") == null)
                category.Add(new BoolSetting("include_suffix", "Include suffix punctuation", true, "ann.suffix"));

            annType.GetMethod("RegisterSettings", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(CategorySetting) }, null)?.Invoke(null, new object[] { category });
        }

        private static void RegisterElementOverrides(Type elType)
        {
            var orderAttr = elType.GetCustomAttribute<AnnouncementOrderAttribute>(true);
            if (orderAttr == null) return;

            var elementKey = DeriveElementKey(elType);
            var elementDisplay = DeriveElementDisplayName(elType);

            // PositionAnnouncement is injected universally (any element with a parent), so every element
            // gets an override entry for it even though it's not in the [AnnouncementOrder].
            var annTypes = orderAttr.Types.Concat(new[] { typeof(PositionAnnouncement) }).Distinct().ToList();

            foreach (var annType in annTypes)
            {
                var annKey = DeriveAnnouncementKey(annType);
                var annDisplay = DeriveDisplayName(StripSuffix(annType.Name, "Announcement"));

                // The global category RegisterGlobal created above — fetched directly via EnsureCategory
                // (returns the existing one). We can't use ModSettings.GetSetting here: the path index isn't
                // built until ModSettings.Initialize, which runs AFTER registration, and it indexes only leaves.
                var global = ModSettingsRegistry.EnsureCategory("announcements." + annKey, "Announcements/" + annDisplay);

                // Under an "Announcements" subnode, so each element node's root stays free for future
                // element-specific (non-announcement) settings.
                var perEl = ModSettingsRegistry.EnsureCategory(
                    "ui." + elementKey + ".announcements." + annKey,
                    "UI/" + elementDisplay + "/Announcements/" + annDisplay,
                    // "ui" root segment skipped (empty); element node, the Announcements subnode, and the
                    // per-announcement leaf each get a "settings"-table loc key (English fallback if absent).
                    "/element." + elementKey + "/announcements_group/announcement." + annKey);

                foreach (var child in global.Children)
                {
                    if (perEl.GetByKey(child.Key) != null) continue;
                    var ov = CreateOverride(child);
                    if (ov != null) perEl.Add(ov);
                }
            }
        }

        // Mirror a global setting as a Nullable* override that inherits from it. (Only Bool globals exist
        // today — enabled / include_suffix; extend for Int/Choice if an announcement declares them.)
        private static Setting CreateOverride(Setting global)
        {
            switch (global)
            {
                case BoolSetting b: return new NullableBoolSetting(b.Key, b.Label, b, b.LocalizationKey);
                default: return null;
            }
        }

        // ---- key / label derivation ----

        public static string DeriveAnnouncementKey(Type annType) => ToSnake(StripSuffix(annType.Name, "Announcement"));

        public static string DeriveElementKey(Type elType)
        {
            var attr = elType.GetCustomAttribute<ElementSettingsKeyAttribute>();
            if (attr != null) return attr.Key;
            return ToSnake(StripPrefix(StripSuffix(elType.Name, "Element"), "Proxy"));
        }

        private static string DeriveElementDisplayName(Type elType)
        {
            var attr = elType.GetCustomAttribute<ElementSettingsKeyAttribute>();
            if (attr != null) return SnakeToTitle(attr.Key);
            return DeriveDisplayName(StripPrefix(StripSuffix(elType.Name, "Element"), "Proxy"));
        }

        private static string DeriveDisplayName(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i])) sb.Append(' ');
                sb.Append(pascal[i]);
            }
            return sb.ToString();
        }

        private static string SnakeToTitle(string snake)
        {
            var parts = snake.Split('_');
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p.Substring(1));
            }
            return sb.ToString();
        }

        private static string ToSnake(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(pascal[i]));
            }
            return sb.ToString();
        }

        private static string StripSuffix(string name, string suffix)
            => name.EndsWith(suffix) ? name.Substring(0, name.Length - suffix.Length) : name;
        private static string StripPrefix(string name, string prefix)
            => name.StartsWith(prefix) ? name.Substring(prefix.Length) : name;
    }
}
