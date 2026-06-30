using System;
using System.Collections.Generic;
using System.Reflection;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// A "&lt; value &gt;" cycler over one appearance component (gender, face/body type, skin/hair/eyebrow/
    /// beard, scars, tattoos, implant ports …). They all derive from the engine's open-generic
    /// <c>SequentialSelectorVM&lt;T&gt;</c>, so we drive it by reflection (Title / CurrentIndex / TotalCount
    /// / OnLeft / OnRight / IsAvailable — all on the generic base, no shared non-generic accessor). Most
    /// values are purely visual (no name), so we announce "Title, N of M"; a caller can supply an explicit
    /// value reader for the ones that have a meaningful value (e.g. gender → "Male"). Left/Right cycle.
    /// A component with a single option (IsAvailable == false) drops out of nav.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxySequentialSelector : UIElement
    {
        private readonly object _vm;
        private readonly Func<string> _value;       // optional explicit value text (else "N of M")
        private readonly string _fallbackLabel;     // used when the component has no Title

        public ProxySequentialSelector(object vm, Func<string> value = null, string fallbackLabel = null)
        {
            _vm = vm;
            _value = value;
            _fallbackLabel = fallbackLabel;
        }

        /// <summary>True if <paramref name="vm"/> is a sequential cycler (has the Left/Right + index API).</summary>
        public static bool Handles(object vm)
        {
            var t = vm?.GetType();
            return t != null && t.GetMethod("OnLeft") != null && t.GetMethod("OnRight") != null
                && t.GetProperty("CurrentIndex") != null && t.GetProperty("TotalCount") != null;
        }

        private static object RpValue(object rp) => rp?.GetType().GetProperty("Value")?.GetValue(rp);
        private object Prop(string name) => _vm?.GetType().GetProperty(name)?.GetValue(_vm);
        private void Call(string name) => _vm?.GetType().GetMethod(name)?.Invoke(_vm, null);

        private string TitleText()
        {
            var s = RpValue(Prop("Title")) as string;
            return string.IsNullOrEmpty(s) ? (_fallbackLabel ?? "") : s;
        }
        private int Index() => RpValue(Prop("CurrentIndex")) is int i ? i : 0;
        private int Total() => Prop("TotalCount") is int i ? i : 0;
        private bool Available() => !(RpValue(Prop("IsAvailable")) is bool b) || b;

        public override bool CanFocus => Available();
        public override bool ReannounceOnActivate => true;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(TitleText()));
            yield return new RoleAnnouncement("slider");
            var v = _value != null ? _value()
                : Loc.T("nav.position", new { index = Index() + 1, count = Total() });
            yield return new ValueAnnouncement(Message.Raw(v));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Decrease, Message.Localized("ui", "action.previous"), _ => Call("OnLeft"));
            yield return new ElementAction(ActionIds.Increase, Message.Localized("ui", "action.next"), _ => Call("OnRight"));
        }
    }
}
