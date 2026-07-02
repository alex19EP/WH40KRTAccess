using System;
using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// A numeric setting → slider. Left/Right step by the game's own SetPrev/SetNextValue; setValue sets
    /// directly. Value read live and formatted by IsInt/DecimalPlaces.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(TooltipAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxySlider : UIElement
    {
        private readonly SettingsEntitySliderVM _vm;

        public ProxySlider(SettingsEntitySliderVM vm) { _vm = vm; }

        private bool Enabled => _vm != null && _vm.ModificationAllowed.Value;

        private string ValueText()
        {
            if (_vm == null) return "";
            float v = _vm.GetTempValue();
            return _vm.IsInt ? ((int)Math.Round(v)).ToString() : v.ToString("F" + _vm.DecimalPlaces);
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title?.Text ?? ""));
            yield return new RoleAnnouncement("slider");
            yield return new ValueAnnouncement(Message.Raw(ValueText()));
            yield return new EnabledAnnouncement(Enabled);
            yield return new TooltipAnnouncement(Message.MaybeRaw(_vm?.Description));
        }

        public override string GetTooltipText() => _vm?.Description;

        public override IEnumerable<ElementAction> GetActions()
        {
            if (!Enabled) yield break;
            yield return new ElementAction(ActionIds.Decrease, Message.Localized("ui", "action.decrease"), _ => { _vm.SetPrevValue(); PlayMove(); });
            yield return new ElementAction(ActionIds.Increase, Message.Localized("ui", "action.increase"), _ => { _vm.SetNextValue(); PlayMove(); });
            yield return new ElementAction(ActionIds.SetValue, Message.Localized("ui", "action.set_value"),
                a => { _vm.SetTempValue((float)ActionArgs.Get<double>(a, "value")); PlayMove(); });
        }

        // The game plays SettingsSliderMove from the slider view's SetValueFromUI; our VM-direct adjust
        // (SetPrev/SetNext/SetTempValue) bypasses the view, so replay the move tick on each step.
        private static void PlayMove()
            => RTAccess.UiSound.Play(Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Settings?.SettingsSliderMove);
    }
}
