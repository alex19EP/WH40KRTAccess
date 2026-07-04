using System;
using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.ContextMenu;
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
        /// advertises no action and no sound — Enter consumes silently, the ProxyActionButton convention
        /// (it didn't advertise the action either). Sounds ride the vtable slots the navigator plays at
        /// its chokepoints: a themed hover/click sound-type where the card carries one (the main-menu
        /// sidebar's Analog), else the generic blueprint ButtonClick — UIElement's default pair.
        /// <paramref name="tooltip"/> is a template FACTORY resolved live on each Space press (the
        /// tooltips-live-not-cached rule) for buttons that carry one (a travel destination's quest
        /// objectives) — rendered through <see cref="RTAccess.Accessibility.TooltipReader"/>, exactly
        /// the ProxyActionButton tooltip path; ungated by enabled (a grayed card still reads).</summary>
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
                OnTooltip = tooltip == null ? (Action)null : () =>
                {
                    var tpl = tooltip();
                    var body = tpl != null ? RTAccess.Accessibility.TooltipReader.GetFull(tpl) : null;
                    if (string.IsNullOrWhiteSpace(body)) { Tts.Speak(Loc.T("nav.no_tooltip"), interrupt: true); return; }
                    RTAccess.Screens.TooltipScreen.Open(label(), body);
                },
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
    }
}
