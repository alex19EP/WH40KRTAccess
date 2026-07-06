using System;
using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.ContextMenu;
using Kingmaker.Code.UI.MVVM.VM.Settings;                       // SettingsVM
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities;              // SettingsEntity*VM
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities.Difficulty;   // difficulty dropdown / only-one-save
using Kingmaker.Code.UI.MVVM.VM.Settings.KeyBindSetupDialog;    // GetPrettyString
using Kingmaker.Code.UI.MVVM.VM.Settings.Menu;                  // SettingsMenuEntityVM
using RTAccess.UI.Graph;

namespace RTAccess.UI
{
    /// <summary>
    /// Node factories for graph-native screens: assemble <see cref="NodeVtable"/>s with the same spoken
    /// conventions the element proxies used — role words through the control types' <c>role.*</c> locale
    /// keys, <c>state.selected</c>/<c>state.disabled</c>, the builder's auto-stamped "n of m" positions —
    /// and the game's UI sounds through the vtable sound slots the navigator plays at its chokepoints
    /// (the sounds normally lived in view click handlers we bypass). As screens migrate, the VM-contract
    /// knowledge in each proxy moves into a factory here and the proxy is deleted once its last user is
    /// gone.
    /// </summary>
    public static class GraphNodes
    {
        /// <summary>The label part (always first in the standard order).</summary>
        public static NodeAnnouncement LabelPart(Func<string> label)
            => new NodeAnnouncement(label, kind: AnnouncementKinds.Label);

        /// <summary>The selected-state part: "selected" when selected, silent otherwise — LIVE, so a
        /// selection moving under focus (another option chosen elsewhere) announces it.</summary>
        public static NodeAnnouncement SelectedPart(Func<bool> selected)
            => new NodeAnnouncement(() => selected != null && selected() ? Loc.T("state.selected") : null,
                live: true, kind: AnnouncementKinds.Selected);

        /// <summary>The disabled-state part: silent while enabled, "disabled" otherwise — LIVE, so a
        /// control graying out (or lighting up) under focus announces it.</summary>
        public static NodeAnnouncement DisabledPart(Func<bool> enabled)
            => new NodeAnnouncement(() => enabled == null || enabled() ? null : Loc.T("state.disabled"),
                live: true, kind: AnnouncementKinds.Enabled);

        /// <summary>The spoken tooltip/description part ("what this setting does", read after the value —
        /// the old TooltipAnnouncement). User-togglable via the "tooltip" kind settings; Space still opens
        /// the reader.</summary>
        public static NodeAnnouncement TooltipPart(Func<string> description)
            => new NodeAnnouncement(description, kind: AnnouncementKinds.Tooltip);

        /// <summary>A plain read-only text line (a modal body, a help paragraph): label only — no role
        /// word (<see cref="ControlTypes.Text"/> carries none), no actions, no sound — the TextElement
        /// readout. Focusable so it can be re-read.</summary>
        public static NodeVtable Text(Func<string> text) => new NodeVtable
        {
            ControlType = ControlTypes.Text,
            Announcements = new List<NodeAnnouncement> { LabelPart(text) },
        };

        /// <summary>One option of a single-select group ("label, radio button[, selected][, n of m]") —
        /// a dropdown option, a tab row entry. Activation selects it; the navigator plays the generic
        /// button click (the sound ProxyChoiceOption inherited from UIElement). Positions come from the
        /// builder's auto-stamp, so callers declare none.</summary>
        public static NodeVtable ChoiceOption(Func<string> label, Func<bool> selected, Action select)
            => new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    LabelPart(label),
                    SelectedPart(selected),
                },
                SearchText = label,
                OnActivate = select,
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };

        /// <summary>A push button ("label, button[, disabled][, n of m]"). The vtable is declared fresh
        /// every render, so gating on <paramref name="enabled"/> HERE reads live state: a disabled button
        /// advertises no action and no sound — Enter consumes silently (the retired ProxyActionButton
        /// widget kept the same convention). Sounds ride the vtable slots the navigator plays at
        /// its chokepoints: a themed hover/click sound-type where the card carries one (the main-menu
        /// sidebar's Analog), else the generic blueprint ButtonClick — UIElement's default pair.
        /// <paramref name="tooltip"/> is a template FACTORY resolved live on each Space press (the
        /// tooltips-live-not-cached rule) for buttons that carry one (a travel destination's quest
        /// objectives) — opened through the shared <see cref="TooltipChooser"/>: the rendered body plus
        /// any inline glossary links as drill-in entries, exactly what the adapter path offers; ungated
        /// by enabled (a grayed card still reads).</summary>
        public static NodeVtable Button(Func<string> label, Action activate, Func<bool> enabled = null,
            Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> tooltip = null,
            Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? hoverSound = null,
            Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? clickSound = null)
        {
            bool isEnabled = enabled == null || enabled();
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new List<NodeAnnouncement>
                {
                    LabelPart(label),
                    DisabledPart(enabled),
                },
                SearchText = label,
                OnActivate = isEnabled ? activate : null,
                OnTooltip = tooltip == null ? (Action)null : () => TooltipChooser.OpenTemplate(label(), tooltip()),
                HoverSound = hoverSound,
                ClickSound = isEnabled ? clickSound : null,
                ActivateSound = isEnabled && clickSound == null
                    ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick
                    : null,
            };
        }

        /// <summary>A checkbox over an arbitrary boolean ("label, toggle, on/off[, disabled]") — the
        /// ProxyBoolToggle conventions. Activation flips it and re-announces the new value synchronously
        /// (<see cref="NodeVtable.StateText"/> — the ReannounceOnActivate equivalent); for toggles whose
        /// effect settles ASYNCHRONOUSLY in the game, pass <paramref name="announceOnActivate"/> false and
        /// let the LIVE value part speak the settled truth (safe alongside StateText — VtableActivate
        /// rebaselines the live watch after speaking). Sounds ride the vtable slots: default = the generic
        /// click (UIElement's pair, the settings-toggle convention); pass a themed hover/click sound-type
        /// or a one-off blueprint <paramref name="activateSound"/> where the game's card differs (the
        /// tutorial ban toggle's NoSound hover + BanTutorialType sting).</summary>
        public static NodeVtable Toggle(Func<string> label, Func<bool> isChecked, Action onToggle,
            Func<bool> enabled = null, bool announceOnActivate = true,
            Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? hoverSound = null,
            Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? clickSound = null,
            Kingmaker.UI.Sound.BlueprintUISound.UISound activateSound = null)
        {
            bool isEnabled = enabled == null || enabled();
            Func<string> value = () => Loc.T(isChecked != null && isChecked() ? "value.on" : "value.off");
            return new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new List<NodeAnnouncement>
                {
                    LabelPart(label),
                    // Always LIVE: a game-driven flip under focus announces itself.
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                    DisabledPart(enabled),
                },
                SearchText = label,
                StateText = announceOnActivate && isEnabled ? value : null,
                OnActivate = isEnabled ? onToggle : null,
                HoverSound = hoverSound,
                ClickSound = isEnabled ? clickSound : null,
                ActivateSound = !isEnabled ? null
                    : activateSound != null ? activateSound
                    : clickSound == null ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick
                    : null,
            };
        }

        /// <summary>A game context-menu entry (<see cref="ContextMenuEntityVM"/> — the main-menu sidebar):
        /// label + live enabled + Execute. Enabled reads the LIVE entity (<c>m_Entity.IsEnabled</c>
        /// re-invokes the entry's Condition each call) rather than the VM's cached <c>IsEnabled</c>
        /// reactive (stale until RefreshEnabling — the MainMenuButton lesson); hover/click replay the
        /// entry's own themed sounds (Analog for Continue/New Game/…, generic for License/Feedback — the
        /// game sets them on the OwlcatButton via SetClickSound/SetHoverSound from the same entity
        /// fields). Callers skip separators (<c>vm.IsSeparator</c>) when enumerating — a separator was
        /// never focusable.</summary>
        public static NodeVtable MenuEntry(ContextMenuEntityVM vm)
        {
            var entity = vm?.m_Entity; // reachable directly: Code.dll is publicized
            Func<bool> enabled = () => entity != null ? entity.IsEnabled : (vm != null && vm.IsEnabled.Value);
            return Button(
                () =>
                {
                    var t = vm?.Title?.Value;
                    if (!string.IsNullOrEmpty(t)) return t;
                    return entity?.Title?.Text ?? entity?.TitleText ?? "";
                },
                () => vm?.Execute(),
                enabled,
                hoverSound: entity?.HoverSoundType,
                clickSound: entity?.ClickSoundType);
        }

        // ---- game settings entities (the Settings window; SettingsEntityGraph dispatches here) ----

        /// <summary>An expandable group header (a tree section): label only — the announcer appends the
        /// expanded/collapsed state word. Use with <see cref="GraphBuilder"/>'s BeginGroup.</summary>
        public static NodeVtable Group(Func<string> label) => new NodeVtable
        {
            ControlType = ControlTypes.Group,
            Announcements = new List<NodeAnnouncement> { LabelPart(label) },
            SearchText = label,
        };

        /// <summary>A boolean GAME setting (<see cref="SettingsEntityBoolVM"/>): toggles via the VM's own
        /// ChangeValue (the click path), value read live from GetTempValue; the description reads as the
        /// tooltip part and Space opens it — the ProxyToggle conventions, factory-shaped.
        /// (ModificationAllowed is live per render — the Game tab's UpdateInteractable flips it.)</summary>
        public static NodeVtable GameToggle(SettingsEntityBoolVM vm)
        {
            var vt = Toggle(
                () => vm?.Title?.Text ?? "",
                () => vm != null && vm.GetTempValue(),
                () => vm?.ChangeValue(),
                () => vm != null && vm.ModificationAllowed.Value);
            vt.Announcements = With(vt.Announcements, TooltipPart(() => vm?.Description));
            vt.OnTooltip = SettingTooltip(() => vm?.Title?.Text, () => vm?.Description);
            return vt;
        }

        /// <summary>The "only one save" / grim-darkness toggle (<see cref="SettingsEntityBoolOnlyOneSaveVM"/>
        /// — NOT a <see cref="SettingsEntityBoolVM"/>): its ChangeValue may detour through the game's own
        /// confirm box before flipping, so nothing is announced synchronously — the LIVE value part speaks
        /// the settled truth (and stays silent on a declined confirm).</summary>
        public static NodeVtable GameToggle(SettingsEntityBoolOnlyOneSaveVM vm)
        {
            var vt = Toggle(
                () => vm?.Title?.Text ?? "",
                () => vm != null && vm.TempValue.Value,
                () => vm?.ChangeValue(),
                () => vm != null && vm.ModificationAllowed.Value,
                announceOnActivate: false);
            vt.Announcements = With(vt.Announcements, TooltipPart(() => vm?.Description));
            vt.OnTooltip = SettingTooltip(() => vm?.Title?.Text, () => vm?.Description);
            return vt;
        }

        /// <summary>A dropdown ("label, combo box, current option[, disabled][, description]"): Enter opens
        /// the option submenu (<see cref="RTAccess.Screens.ChoiceSubmenuScreen"/>) with the click the old
        /// proxy's default activation sound provided; the value part is LIVE, so returning from the submenu
        /// with a new pick announces itself. <paramref name="adjust"/> (optional) keeps ProxyDropdown's
        /// inline Left/Right stepping — an RT divergence from the WA recipe, which reserved Left/Right for
        /// tree collapse/ascend; here adjust has always taken priority on these nodes (the adapter's rule),
        /// and the live value part speaks each step.</summary>
        public static NodeVtable Dropdown(Func<string> label, Func<string> value, Action openSubmenu,
            Func<bool> enabled = null, Func<string> description = null, Action<int> adjust = null)
        {
            bool isEnabled = enabled == null || enabled();
            var anns = new List<NodeAnnouncement>
            {
                LabelPart(label),
                new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                DisabledPart(enabled),
            };
            if (description != null) anns.Add(TooltipPart(description));
            return new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = anns,
                SearchText = label,
                StateText = isEnabled && adjust != null ? value : null, // spoken (interrupting) after each adjust — keypress provenance, key-repeat friendly (same rationale as Slider)
                OnActivate = isEnabled ? openSubmenu : null,
                OnAdjust = isEnabled && adjust != null
                    ? (Action<int, bool>)((sign, large) => adjust(sign))
                    : null,
                ActivateSound = isEnabled ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick : null,
                OnTooltip = description == null ? null : SettingTooltip(label, description),
            };
        }

        /// <summary>A plain dropdown GAME setting (<see cref="SettingsEntityDropdownVM"/>).</summary>
        public static NodeVtable GameDropdown(SettingsEntityDropdownVM vm)
        {
            Func<string> value = () =>
            {
                if (vm == null) return "";
                var vals = vm.LocalizedValues;
                int i = vm.GetTempValue();
                return (vals != null && i >= 0 && i < vals.Count) ? vals[i] : "";
            };
            return Dropdown(() => vm?.Title?.Text ?? "", value,
                () => RTAccess.Screens.ChoiceSubmenuScreen.Open(vm.Title?.Text, vm.LocalizedValues,
                    vm.GetTempValue(), i => vm.SetTempValue(i)),
                () => vm != null && vm.ModificationAllowed.Value,
                () => vm?.Description,
                sign => { if (sign < 0) vm.SetPrevValue(); else vm.SetNextValue(); });
        }

        /// <summary>The game-difficulty picker (<see cref="SettingsEntityDropdownGameDifficultyVM"/>): a
        /// dropdown whose submenu options read "Title. Description" (each difficulty explains itself);
        /// selection goes through the VM's own SetValue (the item view's click path).</summary>
        public static NodeVtable GameDifficulty(SettingsEntityDropdownGameDifficultyVM vm)
        {
            Func<string> value = () =>
            {
                if (vm == null) return "";
                int i = vm.GetTempValue();
                var items = vm.Items;
                return (items != null && i >= 0 && i < items.Count) ? items[i].Title : "";
            };
            return Dropdown(() => vm?.Title?.Text ?? "", value,
                () =>
                {
                    var items = vm?.Items;
                    if (items == null || items.Count == 0) return;
                    var options = new List<string>(items.Count);
                    foreach (var it in items)
                        options.Add(it.Title + (string.IsNullOrEmpty(it.Description) ? "" : ". " + it.Description));
                    RTAccess.Screens.ChoiceSubmenuScreen.Open(vm.Title?.Text, options, vm.GetTempValue(),
                        i => vm.SetValue(i));
                },
                () => vm != null && vm.ModificationAllowed.Value,
                () => vm?.Description,
                sign => { if (sign < 0) vm.SetPrevValue(); else vm.SetNextValue(); });
        }

        /// <summary>A numeric game-settings slider (<see cref="SettingsEntitySliderVM"/>): Left/Right step
        /// by the game's own SetPrev/SetNextValue with the slider-move tick replayed per step (the game
        /// plays it from the view's SetValueFromUI, which the VM-direct adjust bypasses — the ProxySlider
        /// convention); the value (formatted by IsInt/DecimalPlaces) is spoken as immediate state feedback
        /// after each step. Disabled ⇒ no OnAdjust, so Left/Right fall through to tree navigation, exactly
        /// as the actionless disabled proxy behaved.</summary>
        public static NodeVtable Slider(SettingsEntitySliderVM sv)
        {
            bool isEnabled = sv != null && sv.ModificationAllowed.Value;
            Func<string> value = () =>
            {
                if (sv == null) return "";
                float v = sv.GetTempValue();
                return sv.IsInt ? ((int)Math.Round(v)).ToString() : v.ToString("F" + sv.DecimalPlaces);
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new List<NodeAnnouncement>
                {
                    LabelPart(() => sv?.Title?.Text ?? ""),
                    new NodeAnnouncement(value, kind: AnnouncementKinds.Value),
                    DisabledPart(() => sv != null && sv.ModificationAllowed.Value),
                    TooltipPart(() => sv?.Description),
                },
                SearchText = () => sv?.Title?.Text ?? "",
                StateText = isEnabled ? value : null, // spoken (interrupting) after each adjust — key-repeat friendly
                OnAdjust = !isEnabled ? (Action<int, bool>)null : (sign, large) =>
                {
                    if (sign < 0) sv.SetPrevValue(); else sv.SetNextValue();
                    UiSound.Play(Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Settings?.SettingsSliderMove);
                },
                OnTooltip = SettingTooltip(() => sv?.Title?.Text, () => sv?.Description),
            };
        }

        /// <summary>A settings tab (Game / Controls / …): activation replicates the game's own click flow
        /// (SetSelectedFromView → the selection group updates SelectedMenuEntity → SetSettingsList, which
        /// raises the save-changes box itself when needed) and announces "selected" synchronously; the
        /// selected state reads the SELECTION (SelectedMenuEntity), not the tab's own reactive.</summary>
        public static NodeVtable SettingsTab(SettingsMenuEntityVM tab, SettingsVM settings)
        {
            Func<bool> selected = () => settings != null && ReferenceEquals(settings.SelectedMenuEntity.Value, tab);
            return new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new List<NodeAnnouncement>
                {
                    LabelPart(() => tab?.Title?.Value ?? ""),
                    SelectedPart(selected),
                },
                SearchText = () => tab?.Title?.Value ?? "",
                StateText = () => selected() ? Loc.T("state.selected") : null,
                OnActivate = () => tab?.SetSelectedFromView(true),
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        /// <summary>One binding slot of a game key-binding row (index 0 = primary, 1 = secondary):
        /// value = the bound combo (LIVE — the capture dialog or a clear changes it under focus and it
        /// announces itself); Enter rebinds (opens the game's own KeyBindingSetupDialog, made navigable by
        /// <see cref="RTAccess.Screens.KeyBindCaptureScreen"/>), Backspace clears (opens the dialog and
        /// drives its own Unbind within the frame, never surfacing the capture screen).</summary>
        public static NodeVtable KeyBindingSlot(SettingEntityKeyBindingVM vm, int index, string label)
        {
            bool isEnabled = vm != null && vm.ModificationAllowed.Value;
            Func<string> value = () =>
            {
                if (vm == null) return null;
                var data = index == 0 ? vm.TempBindingValue1.Value : vm.TempBindingValue2.Value;
                string p = data.GetPrettyString(); // empty for an unbound slot (the row view shows "---")
                return string.IsNullOrEmpty(p) ? Loc.T("value.not_bound") : p;
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.KeyBinding,
                Announcements = new List<NodeAnnouncement>
                {
                    LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                    DisabledPart(() => vm != null && vm.ModificationAllowed.Value),
                },
                SearchText = () => vm?.Title?.Text ?? label,
                OnActivate = !isEnabled ? (Action)null : () =>
                {
                    RTAccess.Screens.KeyBindCaptureScreen.PendingLabel = (vm.Title?.Text ?? "") + ", " + label;
                    vm.OpenBindingDialogVM(index);
                },
                OnSecondary = !isEnabled ? (Action)null : () =>
                {
                    vm.OpenBindingDialogVM(index);
                    RTAccess.Screens.KeyBindCaptureScreen.Dialog()?.Unbind();
                },
            };
        }

        /// <summary>Placeholder for setting kinds without a factory yet (the display/accessibility image
        /// rows): "label, setting, not accessible yet" — the ProxyUnsupportedSetting readout.</summary>
        public static NodeVtable UnsupportedSetting(Func<string> label) => new NodeVtable
        {
            ControlType = ControlTypes.Text,
            Announcements = new List<NodeAnnouncement>
            {
                LabelPart(label),
                new NodeAnnouncement(() => Loc.T("role.setting"), kind: AnnouncementKinds.Role),
                new NodeAnnouncement(() => Loc.T("value.not_accessible"), kind: AnnouncementKinds.Value),
            },
            SearchText = label,
        };

        // A copy of the announcements with one more part appended (the type's kind order slots it).
        private static IReadOnlyList<NodeAnnouncement> With(IReadOnlyList<NodeAnnouncement> anns,
            NodeAnnouncement extra)
        {
            var list = anns != null ? new List<NodeAnnouncement>(anns) : new List<NodeAnnouncement>();
            list.Add(extra);
            return list;
        }

        // Space on a setting: its plain-text description through the shared chooser — with no template to
        // mine and no sections this is always the single-tooltip case (open the body, or say there's none),
        // matching the adapter's plain GetTooltipText behavior.
        private static Action SettingTooltip(Func<string> title, Func<string> description) => () =>
            TooltipChooser.Open(title?.Invoke(), description?.Invoke(), sections: null, links: null);
    }
}
