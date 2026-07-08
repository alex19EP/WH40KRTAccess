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
    /// the announcement carries <see cref="ShowInGlobalSettingsAttribute"/>. For EVERY graph control
    /// type: per-type overrides at <c>ui.{type}.announcements.{ann}.{setting}</c> — a
    /// <see cref="NullableBoolSetting"/> mirroring each global setting (inherits the global until the
    /// user overrides it), resolved by <see cref="PartEnabled"/>.
    /// </summary>
    public static class AnnouncementRegistry
    {
        public static void RegisterDefaults()
        {
            var asm = Assembly.GetExecutingAssembly();
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            // Globals first — per-type overrides reference them as fallbacks.
            foreach (var t in types.Where(t => !t.IsAbstract && typeof(Announcement).IsAssignableFrom(t)))
            {
                try { RegisterGlobal(t); }
                catch (Exception e) { Main.Log?.Error("[ann] global register failed for " + t.Name + ": " + e.Message); }
            }

            // Graph control types (the registry): per-type override schema keyed on the registry entry.
            // Keys shared with legacy collapsed element keys ("toggle", "radio_button") land in the SAME
            // categories — the settings identity carried over from the element era.
            foreach (var ct in RTAccess.UI.ControlTypes.All)
            {
                try { RegisterControlTypeOverrides(ct); }
                catch (Exception e) { Main.Log?.Error("[ann] control-type register failed for " + ct.Key + ": " + e.Message); }
            }
        }

        private static void RegisterControlTypeOverrides(RTAccess.UI.Graph.ControlType ct)
        {
            if (ct?.Key == null || ct.Order == null) return;
            var typeDisplay = SnakeToTitle(ct.Key);
            foreach (var kind in ct.Order)
            {
                var kindDisplay = SnakeToTitle(kind);
                var global = ModSettingsRegistry.EnsureCategory("announcements." + kind, "Announcements/" + kindDisplay);
                var perType = ModSettingsRegistry.EnsureCategory(
                    "ui." + ct.Key + ".announcements." + kind,
                    "UI/" + typeDisplay + "/Announcements/" + kindDisplay,
                    "/element." + ct.Key + "/announcements_group/announcement." + kind);
                foreach (var child in global.Children)
                {
                    if (perType.GetByKey(child.Key) != null) continue;
                    var ov = CreateOverride(child);
                    if (ov != null) perType.Add(ov);
                }
            }
        }

        // ---- one-time schema migration: legacy element keys → graph control-type keys ----

        /// <summary>Old per-element settings prefix → new prefix. The graph port retargeted three shipped
        /// proxies' [ElementSettingsKey] onto the graph control-type keys (their override paths moved),
        /// so saved user overrides at the OLD paths would silently become inert unknown keys. Array order
        /// matters: the earlier source wins when two old prefixes map to the same new path.</summary>
        private static readonly (string Old, string New)[] LegacyElementKeyRenames =
        {
            ("ui.selection_item.", "ui.radio_button."),
            ("ui.choice_option.",  "ui.radio_button."),
            ("ui.settings_tab.",   "ui.tab."),
        };

        /// <summary>One-time migration of saved user overrides from the legacy element-key paths onto the
        /// graph control-type paths. Reads the old paths out of the preserved unknown-key set, applies each
        /// to the setting now living at the renamed path (never clobbering a value already saved there),
        /// then drops the old keys. Call AFTER <see cref="ModSettings.Initialize"/> (needs the path index
        /// + the loaded file).</summary>
        public static void MigrateLegacyElementKeys()
        {
            bool touched = false;
            foreach (var rename in LegacyElementKeyRenames)
            {
                foreach (var oldPath in ModSettings.UnknownPaths())
                {
                    if (!oldPath.StartsWith(rename.Old, StringComparison.Ordinal)) continue;
                    touched = true;
                    var newPath = rename.New + oldPath.Substring(rename.Old.Length);
                    var target = ModSettings.GetSetting<Setting>(newPath);
                    // Apply only where the renamed setting exists and holds no persisted value yet
                    // (BoxedValue == null → nothing saved: a NullableBool override still inheriting).
                    if (target != null && target.BoxedValue == null
                        && ModSettings.TryGetUnknown(oldPath, out var token))
                    {
                        try { target.LoadValue(token); }
                        catch (Exception e)
                        {
                            Main.Log?.Error("[ann] migrate " + oldPath + " -> " + newPath + " failed: " + e.Message);
                        }
                    }
                }
                ModSettings.RemoveUnknownWhere(k => k.StartsWith(rename.Old, StringComparison.Ordinal));
            }
            if (touched)
            {
                Main.Log?.Log("[ann] migrated legacy element-key overrides (selection_item/choice_option -> radio_button, settings_tab -> tab)");
                ModSettings.MarkDirty(); // persist the moved values + the dropped old keys
            }
        }

        /// <summary>Is an announcement part enabled for a control type — the graph announcer's
        /// PartFilter resolver: per-type override → global per-kind toggle → true. Kindless (custom)
        /// parts always speak.</summary>
        public static bool PartEnabled(string typeKey, string kind)
        {
            if (kind == null) return true;
            if (typeKey != null)
            {
                var ov = ModSettings.GetSetting<NullableBoolSetting>(
                    "ui." + typeKey + ".announcements." + kind + ".enabled");
                if (ov != null && ov.IsOverridden) return ov.LocalValue.Value;
            }
            var global = ModSettings.GetSetting<BoolSetting>("announcements." + kind + ".enabled");
            return global == null || global.Get();
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
    }
}
