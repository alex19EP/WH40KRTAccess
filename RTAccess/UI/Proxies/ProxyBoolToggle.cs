using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// A boolean checkbox backed by getter/toggle delegates — a MOD-owned flag, not a game
    /// <c>SettingsEntityBoolVM</c> (cf. <see cref="ProxyToggle"/>, which wraps the settings VM).
    /// Announced "Label, toggle, on/off"; activating flips it and re-announces the new value in place
    /// (<see cref="UIElement.ReannounceOnActivate"/>).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(PositionAnnouncement))]
    public sealed class ProxyBoolToggle : UIElement
    {
        private readonly Func<string> _label;
        private readonly Func<bool> _get;
        private readonly Action _toggle;
        private readonly Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? _hoverSoundType;
        private readonly Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? _clickSoundType;
        private readonly Func<Kingmaker.UI.Sound.BlueprintUISound.UISound> _activateSound;

        public ProxyBoolToggle(string label, Func<bool> get, Action toggle,
            Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? hoverSoundType = null,
            Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? clickSoundType = null,
            Func<Kingmaker.UI.Sound.BlueprintUISound.UISound> activateSound = null)
            : this(() => label, get, toggle, hoverSoundType, clickSoundType, activateSound) { }

        public ProxyBoolToggle(Func<string> label, Func<bool> get, Action toggle,
            Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? hoverSoundType = null,
            Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? clickSoundType = null,
            Func<Kingmaker.UI.Sound.BlueprintUISound.UISound> activateSound = null)
        {
            _label = label;
            _get = get;
            _toggle = toggle;
            _hoverSoundType = hoverSoundType;
            _clickSoundType = clickSoundType;
            _activateSound = activateSound;
        }

        public override bool ReannounceOnActivate => true; // toggling flips the value in place

        // The game silences checkbox hover/click on the tutorial & message-box "don't show again" toggles
        // (SetClickAndHoverSound NoSound); settings toggles stay generic — so the use-site decides the type.
        public override Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? HoverSoundType => _hoverSoundType;
        public override Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? ClickSoundType => _clickSoundType;

        // A one-off blueprint sound to replay on activate (e.g. the tutorial ban toggle's BanTutorialType,
        // which the game plays from the live toggle's pointer-click — a handler our local flip never hits).
        // Null ⇒ the default generic click. Ignored when ClickSoundType is set (that takes precedence).
        public override Kingmaker.UI.Sound.BlueprintUISound.UISound ActivateSound
            => _activateSound != null ? _activateSound() : base.ActivateSound;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label != null ? _label() : ""));
            yield return new RoleAnnouncement("toggle");
            yield return new ValueAnnouncement(_get != null && _get()
                ? Message.Localized("ui", "value.on") : Message.Localized("ui", "value.off"));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.toggle"),
                _ => _toggle?.Invoke());
        }
    }
}
