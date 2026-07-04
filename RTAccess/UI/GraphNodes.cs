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
        /// sidebar's Analog), else the generic blueprint ButtonClick — UIElement's default pair.</summary>
        public static NodeVtable Button(Func<string> label, Action activate, Func<bool> enabled = null,
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
                HoverSound = hoverSound,
                ClickSound = isEnabled ? clickSound : null,
                ActivateSound = isEnabled && clickSound == null
                    ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick
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
