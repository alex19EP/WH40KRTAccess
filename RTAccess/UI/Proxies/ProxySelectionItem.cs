using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.SelectionGroup;
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// The shared control for ANY <see cref="SelectionGroupEntityVM"/> (campaign, race, gender, portrait,
    /// voice, …) — it encapsulates the game's selection contract (IsSelected / IsAvailable /
    /// SetSelectedFromView) so no call site reimplements it. The caller supplies the label (and optional
    /// drill-in tooltip), which live on the concrete item type. A single-select group is a <b>radio
    /// button</b> (default); pass <c>role</c> "tab" for a tab, "toggle" for a multi-select group (announces
    /// on/off instead of selected), or "item" for a plain list entry that carries no selection state (the
    /// "selected" readout is suppressed — e.g. a save slot, which is acted on directly, not selected).
    /// <c>available</c> overrides the default availability (e.g. a campaign's IsStoryIsAvailable);
    /// <c>onActivate</c> overrides the default select (e.g. a save slot's load/overwrite, a multi-select
    /// toggle, or replaying a voice sample when already chosen); <c>onContext</c> adds a secondary
    /// (Backspace) action (e.g. a save slot's delete).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(SelectedAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    [ElementSettingsKey("radio_button")] // shared settings identity with ProxyChoiceOption + ControlTypes.RadioButton
    public sealed class ProxySelectionItem : UIElement
    {
        private readonly SelectionGroupEntityVM _vm;
        private readonly Func<string> _label;
        private readonly Func<TooltipBaseTemplate> _tooltip; // optional Space drill-in, resolved live
        private readonly Func<string> _detail;               // optional plain-text Space drill-in, resolved live
        private readonly string _role;
        private readonly Func<bool> _available;
        private readonly Action _activate;
        private readonly bool _suppressSound;
        private readonly Action _onFocusEnter;
        private readonly Action _onContext;       // optional secondary (Backspace) action
        private readonly Func<string> _contextLabel;

        public ProxySelectionItem(SelectionGroupEntityVM vm, Func<string> label,
            Func<TooltipBaseTemplate> tooltip = null, string role = "radio button",
            Func<bool> available = null, Action onActivate = null, bool suppressActivateSound = false,
            Action onFocusEnter = null, Func<string> detail = null,
            Action onContext = null, Func<string> contextLabel = null)
        {
            _vm = vm;
            _label = label;
            _tooltip = tooltip;
            _detail = detail;
            _role = role;
            _available = available;
            _activate = onActivate;
            _suppressSound = suppressActivateSound;
            _onFocusEnter = onFocusEnter;
            _onContext = onContext;
            _contextLabel = contextLabel;
        }

        // Optional focus callback: run when the cursor settles on this item (once, via the navigator's focus
        // pump). Lets a screen TRACK the cursor without committing the game's selection — e.g. a save/load
        // screen records the slot under the cursor so Load/Delete act on it, but does NOT call
        // SetSelectedFromView on focus (that flips the game view's Selected layer and plays its select sound on
        // every arrow — browsing should be silent). Committing stays the explicit Enter/button path.
        public override void OnFocusEnter() => _onFocusEnter?.Invoke();

        public override TooltipBaseTemplate GetTooltipTemplate() => _tooltip != null ? _tooltip() : null;

        // Plain-text Space drill-in (resolved live): card-invisible detail that has no game tooltip template of
        // its own — e.g. a save slot's required-DLC names. Space reads this before falling back to a template.
        public override string GetTooltipText() => _detail != null ? _detail() : null;
        public override bool ReannounceOnActivate => true; // selecting/toggling flips it in place

        // Some items play their own selection sound off the VM path — don't double it.
        public override Kingmaker.UI.Sound.BlueprintUISound.UISound ActivateSound
            => _suppressSound ? null : base.ActivateSound;

        private bool Available => _available != null ? _available() : (_vm != null && _vm.IsAvailable.Value);
        private bool IsSelected => _vm != null && _vm.IsSelected.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label?.Invoke() ?? ""));
            yield return new RoleAnnouncement(_role);
            if (_role == "toggle")
                yield return new ValueAnnouncement(IsSelected
                    ? Message.Localized("ui", "value.on") : Message.Localized("ui", "value.off"));
            else if (_role != "item") // an "item" carries no selection state — don't read "selected"
                yield return new SelectedAnnouncement(IsSelected);
            yield return new EnabledAnnouncement(Available);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Available)
                yield return new ElementAction(ActionIds.Activate,
                    Message.Localized("ui", _role == "toggle" ? "action.toggle" : "action.select"),
                    _ => { if (_activate != null) _activate(); else _vm?.SetSelectedFromView(true); });
            // Secondary action is independent of availability (e.g. delete a save that can't be over-written).
            if (_onContext != null)
                yield return new ElementAction(ActionIds.Context,
                    _contextLabel != null ? Message.Raw(_contextLabel()) : Message.Localized("ui", "action.menu"),
                    _ => _onContext());
        }
    }
}
