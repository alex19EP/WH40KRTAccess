using RTAccess.Settings;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The mod's OWN settings screen — the tabbed category browser over <see cref="ModSettings.Root"/>.
    /// Opened from the native Mods list (<see cref="DlcManagerScreen"/> → the RTAccess row's "Settings"
    /// action), so a blind player configures the accessibility mod through the game's own "Mods and DLC"
    /// window instead of the inaccessible Unity Mod Manager IMGUI overlay. Mod-pushed: <see cref="IsActive"/>
    /// reads a static flag <see cref="Open"/> sets; it sits at a high layer above the mods window and
    /// Escape closes it, returning there.
    ///
    /// Graph-native and immediate-mode: a CATEGORIES tab stop, the selected tab's settings tree (content
    /// keys carry the tab, so switching re-keys content only and expansion is remembered per tab), then the
    /// two Reset stops. The tabs mirror the top of the settings tree — Exploration and Audio render flat
    /// (their leaves are plain toggles/sliders/dropdowns), while Announcements curates the announcement
    /// verbosity + per-element overrides the same way WrathAccess's "UI" tab does.
    ///
    /// The category/preset/reset labels live in the <c>settings</c> locale table (shared with
    /// <c>Setting.LocalizationKey</c>), reached via <see cref="S"/>; only the screen NAME is a <c>ui</c> key.
    /// </summary>
    public sealed class ModSettingsScreen : Screen
    {
        private static bool s_open;
        public static void Open() { s_open = true; }
        public static void CloseMenu() { s_open = false; }

        public ModSettingsScreen() { Wrap = true; }

        public override string Key => "overlay.modsettings";
        public override string ScreenName => Loc.T("screen.mod_settings");
        public override int Layer => 40; // above the mods window (25) it's launched from
        public override bool Exclusive => true; // a pure mod overlay: own the whole keyboard while up
        public override bool IsActive() => s_open;

        private int _active; // the selected category tab (view state)

        // The tabs, in order. Exploration + Audio render their category flat; Announcements is curated.
        private static readonly (string key, string loc)[] Tabs =
        {
            ("exploration", "category.exploration"),
            ("audio", "category.audio"),
            ("announcements", "ui.announcements"),
        };

        // Settings-table string (category/preset/reset labels share the "settings" table with the tree).
        private static string S(string key) => Message.Localized("settings", key).Resolve();
        private static string S(string key, object args) => Message.Localized("settings", key, args).Resolve();

        public override void OnPush() { _active = 0; }

        // Escape closes the whole menu.
        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => CloseMenu());
        }


        public override void Build(GraphBuilder b)
        {
            // The category tabs: one stop, arrows between tabs; the graph STARTS on the selected tab.
            b.BeginStop("tabs").PushContext(S("menu.categories"), Loc.T("role.list"));
            for (int i = 0; i < Tabs.Length; i++)
            {
                var id = ControlId.Structural("modset:tab:" + Tabs[i].key);
                b.AddItem(id, TabNode(i));
                if (i == _active) b.SetStart(id);
            }
            b.PopContext();

            // The selected category's settings tree. Keys carry the tab, so a tab switch re-keys the
            // content (tab focus survives) and expansion is remembered per tab.
            string tabKey = _active >= 0 && _active < Tabs.Length ? Tabs[_active].key : null;
            b.BeginStop("content");
            BuildTab(b, tabKey, "modset:" + tabKey + ":");

            // Two standing stops after the tree: reset THIS tab, and reset everything.
            b.BeginStop("resettab").AddItem(ControlId.Structural("modset:resettab"),
                GraphNodes.Button(() => S("reset.tab", new { name = ActiveTabLabel() }), ResetActiveTab));
            b.BeginStop("resetall").AddItem(ControlId.Structural("modset:resetall"),
                GraphNodes.Button(() => S("reset.all"), ResetAllSettings));
        }

        private NodeVtable TabNode(int idx)
        {
            System.Func<string> label = () => S(Tabs[idx].loc);
            return new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new System.Collections.Generic.List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(label),
                    GraphNodes.SelectedPart(() => _active == idx),
                },
                SearchText = label,
                OnActivate = () => _active = idx,
            };
        }

        private static void BuildTab(GraphBuilder b, string key, string k)
        {
            switch (key)
            {
                case "exploration":
                case "audio":
                    // Flat: the category's leaves render directly (no redundant top-level group — the tab
                    // already names the section).
                    var cat = ModSettings.Root.Get<CategorySetting>(key);
                    if (cat != null)
                        foreach (var s in cat.Children) ModSettingNodes.Emit(b, s, k);
                    break;
                case "announcements":
                    BuildAnnouncementsTab(b, k);
                    break;
            }
        }

        // The announcements tab (WrathAccess's "UI" tab shape): a verbosity preset + a plain on/off per
        // announcement kind (the 90% case), then the full per-element detail tucked at the bottom.
        private static void BuildAnnouncementsTab(GraphBuilder b, string k)
        {
            var annRoot = ModSettings.Root.Get<CategorySetting>("announcements");

            if (annRoot != null)
            {
                b.BeginGroup(ControlId.Structural(k + "announcements"),
                    GraphNodes.Group(() => S("ui.announcements")));
                b.AddItem(ControlId.Structural(k + "verbosity"), BuildVerbosityDropdown(annRoot));
                foreach (var child in annRoot.Children)
                {
                    if (!(child is CategorySetting annCat) || annCat.Hidden) continue;
                    var enabled = annCat.Get<BoolSetting>("enabled");
                    if (enabled != null)
                    {
                        var en = enabled;
                        b.AddItem(ControlId.Structural(k + "ann." + annCat.Key),
                            GraphNodes.Toggle(() => annCat.Label, en.Get, () => en.Set(!en.Get())));
                    }
                }
                b.EndGroup();
            }

            // Per-element overrides: the Global node carries each announcement kind's FULL settings (suffix
            // punctuation etc.), then every control type's inherit/on/off overrides, alphabetical.
            b.BeginGroup(ControlId.Structural(k + "overrides"),
                GraphNodes.Group(() => S("ui.element_overrides")));
            if (annRoot != null)
            {
                b.BeginGroup(ControlId.Structural(k + "overrides.global"),
                    GraphNodes.Group(() => S("global.group")));
                foreach (var s in annRoot.Children) ModSettingNodes.Emit(b, s, k + "g.");
                b.EndGroup();
            }
            var ui = ModSettings.Root.Get<CategorySetting>("ui");
            if (ui != null)
                foreach (var s in ui.Children.OrderBy(c => c.Label, System.StringComparer.CurrentCultureIgnoreCase))
                    ModSettingNodes.Emit(b, s, k + "o.");
            b.EndGroup();
        }

        // Verbosity presets: each names the announcement kinds it turns OFF (everything else on). The
        // dropdown derives its state from the live toggles — hand-edits read as "Custom".
        private static readonly (string loc, string[] off)[] VerbosityPresets =
        {
            ("preset.verbose", new string[0]),
            ("preset.standard", new[] { "position" }),
            ("preset.concise", new[] { "role", "tooltip", "position" }),
        };

        private static NodeVtable BuildVerbosityDropdown(CategorySetting annRoot)
        {
            var visible = annRoot.Children.OfType<CategorySetting>().Where(c => !c.Hidden)
                .Select(c => (c.Key, Enabled: c.Get<BoolSetting>("enabled")))
                .Where(t => t.Enabled != null).ToList();

            var labels = VerbosityPresets.Select(p => S(p.loc)).ToList();
            labels.Add(S("preset.custom"));

            int Current()
            {
                for (int i = 0; i < VerbosityPresets.Length; i++)
                {
                    var off = VerbosityPresets[i].off;
                    bool match = true;
                    foreach (var t in visible)
                        if (t.Enabled.Get() == (System.Array.IndexOf(off, t.Key) >= 0)) { match = false; break; }
                    if (match) return i;
                }
                return VerbosityPresets.Length; // Custom
            }

            void Apply(int idx)
            {
                if (idx < 0 || idx >= VerbosityPresets.Length) return; // choosing Custom = keep as-is
                var off = VerbosityPresets[idx].off;
                ModSettings.Batch(() =>
                {
                    foreach (var t in visible)
                        t.Enabled.Set(System.Array.IndexOf(off, t.Key) < 0);
                });
            }

            // "Custom" is a derived display state, not a choice — mark it virtual (selectableCount = presets).
            return ModSettingNodes.ChoiceDropdown(S("ui.verbosity"), labels, Current, Apply,
                selectableCount: VerbosityPresets.Length);
        }

        private string ActiveTabLabel()
            => _active >= 0 && _active < Tabs.Length ? S(Tabs[_active].loc) : "";

        // The settings roots each tab renders — what its Reset button restores.
        private static string[] ResetRootsFor(string key)
        {
            switch (key)
            {
                case "exploration": return new[] { "exploration" };
                case "audio": return new[] { "audio" };
                case "announcements": return new[] { "announcements", "ui" };
                default: return new string[0];
            }
        }

        private void ResetActiveTab()
        {
            var key = _active >= 0 && _active < Tabs.Length ? Tabs[_active].key : null;
            if (key == null) return;
            ModSettings.Batch(() =>
            {
                foreach (var path in ResetRootsFor(key))
                    ModSettings.GetCategory(path)?.ResetToDefault();
            });
            Tts.Speak(S("reset.tab_done", new { name = ActiveTabLabel() }), interrupt: true);
        }

        private void ResetAllSettings()
        {
            ModSettings.Batch(() =>
            {
                foreach (var s in ModSettings.Root.Children) s.ResetToDefault();
            });
            Tts.Speak(S("reset.all_done"), interrupt: true);
        }
    }
}
