using System;
using System.Collections.Generic;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>A generic button driven by delegates — for Apply / Close / etc.</summary>
    // Canonical "button": ProxyStepper shares this settings category + announcement order
    // (it sets AnnouncementOrderType => typeof(ProxyActionButton)). ProxyKeyBindingSlot is
    // deliberately kept separate (its key-rebind announcements are tuned on their own).
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    [ElementSettingsKey("button")]
    public sealed class ProxyActionButton : UIElement
    {
        private readonly Func<string> _label;
        private readonly Func<bool> _enabled;
        private readonly Action _activate;
        private readonly Func<bool> _canFocus;
        private readonly bool _suppressSound;
        private readonly string _actionVerb;
        private readonly Func<string, string[], Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> _linkResolver;
        private readonly Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> _tooltip;

        public ProxyActionButton(string label, Func<bool> enabled, Action activate,
            Func<bool> canFocus = null, bool suppressActivateSound = false, string actionVerb = "activate",
            Func<string, string[], Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> linkResolver = null,
            Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> tooltip = null)
            : this(() => label, enabled, activate, canFocus, suppressActivateSound, actionVerb, linkResolver, tooltip) { }

        // Live label — for buttons whose text changes (e.g. a wizard's Next → "Start"). canFocus skips
        // structural entries (e.g. a context-menu separator); suppressActivateSound is for buttons whose
        // activation plays the game's own sound (e.g. a dialogue answer plays NextDialogLine). actionVerb
        // is a "ui" table key under "action." (e.g. "activate", "choose"). linkResolver handles any
        // non-glossary inline links in the label (e.g. a dialogue answer's skill-check DC link). tooltip is
        // a FACTORY resolved live on each drill-in (per the tooltips-live-not-cached rule) — for buttons that
        // carry one (e.g. a vendor action's glossary hint), like every other interactive control.
        public ProxyActionButton(Func<string> label, Func<bool> enabled, Action activate,
            Func<bool> canFocus = null, bool suppressActivateSound = false, string actionVerb = "activate",
            Func<string, string[], Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> linkResolver = null,
            Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> tooltip = null)
        {
            _label = label;
            _enabled = enabled;
            _activate = activate;
            _canFocus = canFocus;
            _suppressSound = suppressActivateSound;
            _actionVerb = actionVerb;
            _linkResolver = linkResolver;
            _tooltip = tooltip;
        }

        public override Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate GetTooltipTemplate()
            => _tooltip != null ? _tooltip() : null;

        public override Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate ResolveLink(string id, string[] keys)
            => _linkResolver != null ? _linkResolver(id, keys) : null;

        public override bool CanFocus => _canFocus == null || _canFocus();

        public override Kingmaker.UI.Sound.BlueprintUISound.UISound ActivateSound
            => _suppressSound ? null : base.ActivateSound;

        private bool Enabled => _enabled == null || _enabled();

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label != null ? _label() : ""));
            yield return new RoleAnnouncement("button");
            yield return new EnabledAnnouncement(Enabled);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Enabled)
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action." + _actionVerb), _ => _activate?.Invoke());
        }
    }
}
