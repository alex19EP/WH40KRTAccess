namespace RTAccess.UI.Announcements
{
    /// <summary>The control's name/label.</summary>
    [ShowInGlobalSettings]
    public sealed class LabelAnnouncement : Announcement
    {
        public override string Key => "label";
    }

    /// <summary>The control type: "button", "toggle", "slider", "list"…</summary>
    [ShowInGlobalSettings]
    public sealed class RoleAnnouncement : Announcement
    {
        public override string Key => "role";
    }

    /// <summary>Interactability — spoken only when the control can't be used ("disabled").</summary>
    public sealed class EnabledAnnouncement : Announcement
    {
        public override string Key => "enabled";
    }

    /// <summary>Selection state — "selected" only when selected (else silent), like Enabled.</summary>
    public sealed class SelectedAnnouncement : Announcement
    {
        public override string Key => "selected";
    }

    /// <summary>The control's current value/state: "on"/"off", a slider amount, a dropdown option.</summary>
    [ShowInGlobalSettings]
    public sealed class ValueAnnouncement : Announcement
    {
        public override string Key => "value";
    }

    /// <summary>A "simple" tooltip — the header/body description text of a control.</summary>
    [ShowInGlobalSettings]
    public sealed class TooltipAnnouncement : Announcement
    {
        public override string Key => "tooltip";
    }

    /// <summary>Position within the parent context, e.g. "2 of 8".</summary>
    [ShowInGlobalSettings]
    public sealed class PositionAnnouncement : Announcement
    {
        public override string Key => "position";
    }

    /// <summary>Item count for a group, e.g. "8 items".</summary>
    public sealed class CountAnnouncement : Announcement
    {
        public override string Key => "count";
    }
}
