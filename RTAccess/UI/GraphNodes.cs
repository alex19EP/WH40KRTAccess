using System;
using System.Collections.Generic;
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
    }
}
