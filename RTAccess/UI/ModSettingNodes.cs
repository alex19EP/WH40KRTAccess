using RTAccess.Settings;
using RTAccess.UI.Graph;

namespace RTAccess.UI
{
    /// <summary>
    /// Node factories for the MOD's own setting model (<see cref="Setting"/> family) + the recursive tree
    /// emitter — the graph analog of a settings-tree renderer, ported from WrathAccess. Categories become
    /// expandable groups keyed by their settings PATH (stable across renders, so expansion and focus
    /// persist); leaves become typed controls whose values are read live. Inherit-aware settings
    /// (<see cref="NullableBoolSetting"/>) speak one consistent form — the resolved value first, then
    /// "overridden" or "inherited" — and reset to inherit on Backspace, with LIVE value parts so a reset
    /// announces the new state under focus. Serves the mod's own <see cref="Screens.ModSettingsScreen"/>.
    ///
    /// The RT settings tree only uses Bool / Int / Choice / NullableBool leaves (mod key-bindings are NOT
    /// persisted here — they live in <see cref="RTAccess.Input"/>), so the WA binding / nullable-choice
    /// cases are intentionally absent.
    /// </summary>
    internal static class ModSettingNodes
    {
        /// <summary>Emit one setting (recursively for categories) under path-based keys. Hidden settings
        /// and empty categories are skipped.</summary>
        public static void Emit(GraphBuilder b, Setting s, string prefix)
        {
            if (s == null || s.Hidden) return;
            switch (s)
            {
                case CategorySetting cat:
                    if (!HasVisibleLeaf(cat)) return; // skip empty groups
                    b.BeginGroup(ControlId.Structural(prefix + cat.Key), GraphNodes.Group(() => cat.Label));
                    foreach (var c in cat.Children) Emit(b, c, prefix + cat.Key + ".");
                    b.EndGroup();
                    break;
                case BoolSetting bo:
                    b.AddItem(ControlId.Structural(prefix + bo.Key),
                        GraphNodes.Toggle(() => bo.Label, bo.Get, () => bo.Set(!bo.Get())));
                    break;
                case NullableBoolSetting nb:
                    b.AddItem(ControlId.Structural(prefix + nb.Key), OverrideToggle(nb));
                    break;
                case NullableIntSetting ni:
                    b.AddItem(ControlId.Structural(prefix + ni.Key), NullableIntSlider(ni));
                    break;
                case IntSetting i:
                    b.AddItem(ControlId.Structural(prefix + i.Key), IntSlider(i));
                    break;
                case ChoiceSetting c:
                    b.AddItem(ControlId.Structural(prefix + c.Key), ChoiceSettingDropdown(c));
                    break;
            }
        }

        /// <summary>Does this category render anything (a visible non-category leaf anywhere below)?</summary>
        public static bool HasVisibleLeaf(CategorySetting cat)
        {
            foreach (var c in cat.Children)
            {
                if (c.Hidden) continue;
                if (c is CategorySetting sub) { if (HasVisibleLeaf(sub)) return true; }
                else return true;
            }
            return false;
        }

        /// <summary>A mod <see cref="IntSetting"/> as a slider: Left/Right step by the setting's Step,
        /// clamped by the setting itself; the new value speaks synchronously.</summary>
        public static NodeVtable IntSlider(IntSetting setting)
        {
            Func<string> value = () => setting.Get().ToString();
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => setting.Label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => setting.Label,
                StateText = value,
                OnAdjust = (sign, large) => setting.Set(setting.Get() + sign * setting.Step),
            };
        }

        /// <summary>A <see cref="NullableIntSetting"/> — a slider that follows a fallback until overridden:
        /// speaks the RESOLVED value plus the state word ("5, inherited"); Left/Right write an explicit
        /// override from the resolved value; Backspace resets to inherit (the live part re-reads it).</summary>
        public static NodeVtable NullableIntSlider(NullableIntSetting setting)
        {
            Func<string> value = () => setting.Resolved + ", "
                + Loc.T(setting.IsOverridden ? "value.overridden" : "value.inherited");
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => setting.Label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => setting.Label,
                StateText = value,
                OnAdjust = (sign, large) => setting.SetExplicit(setting.Resolved + sign * setting.Step),
                OnSecondary = () => setting.Reset(),
            };
        }

        /// <summary>A <see cref="NullableBoolSetting"/> — the per-type announcement override: a checkbox of
        /// the RESOLVED value ("on, overridden" / "on, inherited"); Enter writes an explicit on/off,
        /// Backspace resets to inherit.</summary>
        public static NodeVtable OverrideToggle(NullableBoolSetting setting)
        {
            Func<string> value = () => Loc.T(setting.Resolved ? "value.on" : "value.off") + ", "
                + Loc.T(setting.IsOverridden ? "value.overridden" : "value.inherited");
            return new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => setting.Label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => setting.Label,
                StateText = value,
                OnActivate = () => setting.ToggleExplicit(),
                OnSecondary = () => setting.Reset(), // live part re-reads the now-inherited value
            };
        }

        /// <summary>A combo box over a fixed list of strings, opening the shared choice submenu — generic,
        /// delegate-driven. Options beyond <paramref name="selectableCount"/> are VIRTUAL: displayable as
        /// the current value (a derived "Custom" state) but not offered in the chooser.</summary>
        public static NodeVtable ChoiceDropdown(string label, List<string> options, Func<int> current,
            Action<int> onSelect, int selectableCount = -1)
        {
            int selectable = (selectableCount < 0 || options == null) ? (options?.Count ?? 0) : selectableCount;
            Func<string> value = () =>
            {
                int i = current != null ? current() : -1;
                return options != null && i >= 0 && i < options.Count ? options[i] : "";
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => label,
                OnActivate = () =>
                {
                    int cur = current != null ? current() : -1;
                    Screens.ChoiceSubmenuScreen.Open(label, options.GetRange(0, selectable),
                        cur < selectable ? cur : -1, // a virtual current value preselects nothing
                        onSelect);
                },
            };
        }

        /// <summary>A plain <see cref="ChoiceSetting"/> as a dropdown. Choices are re-read live per open
        /// (runtime rosters).</summary>
        public static NodeVtable ChoiceSettingDropdown(ChoiceSetting c, string labelOverride = null)
        {
            Func<string> label = () => labelOverride ?? c.Label;
            return new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(() => c.Current?.Label ?? "", live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = label,
                OnActivate = () =>
                {
                    var choices = c.Choices;
                    var labels = new List<string>(choices.Count);
                    foreach (var ch in choices) labels.Add(ch.Label);
                    Screens.ChoiceSubmenuScreen.Open(label(), labels, IndexOfChoice(c),
                        idx => { if (idx >= 0 && idx < c.Choices.Count) c.Set(c.Choices[idx].Id); });
                },
            };
        }

        public static int IndexOfChoice(ChoiceSetting c)
        {
            for (int i = 0; i < c.Choices.Count; i++)
                if (c.Choices[i].Id == c.ValueId) return i;
            return -1;
        }
    }
}
