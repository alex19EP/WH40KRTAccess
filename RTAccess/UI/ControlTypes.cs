using RTAccess.UI.Graph;

namespace RTAccess.UI
{
    /// <summary>
    /// The control-type registry: each entry is a <see cref="ControlType"/> VALUE — its settings key, the
    /// speak order of its announcement kinds, and the parts common to every control of the type (the
    /// localized role word). This replaces the legacy per-class identity ([AnnouncementOrder] attributes +
    /// [ElementSettingsKey] collapsing): a node factory just sets the type and gets the role word, the
    /// ordering, and the user's per-type announcement settings for free. Keys deliberately match the
    /// legacy collapsed keys where the concept already existed ("toggle", "slider"), so both systems share
    /// one settings identity during the migration.
    /// </summary>
    public static class ControlTypes
    {
        private static readonly string[] StandardOrder =
        {
            AnnouncementKinds.Label,
            AnnouncementKinds.Role,
            AnnouncementKinds.Value,
            AnnouncementKinds.Selected,
            AnnouncementKinds.Enabled,
            AnnouncementKinds.Tooltip,
            AnnouncementKinds.Position,
        };

        private static NodeAnnouncement[] RoleWord(string word)
            => new[] { new NodeAnnouncement(() => Loc.T("role." + word), kind: AnnouncementKinds.Role) };

        public static readonly ControlType Button = new ControlType
        {
            Key = "button",
            Order = StandardOrder,
            Common = () => RoleWord("button"),
        };

        /// <summary>An action-bar slot (ability / weapon attack / consumable / heroic act). Same behavior
        /// as a button but reordered around availability: it LEADS with the status word and slots the
        /// why-not reason right after the name — "unavailable, {name}, out of range, button, …" — so a
        /// player scanning a bar of mostly-greyed abilities hears whether a slot is usable (and why not)
        /// before its detail. Shares the "button" settings key, so the per-button announcement toggles
        /// cover it too (it is not a separate settings identity — not in <see cref="All"/>).</summary>
        public static readonly ControlType ActionSlot = new ControlType
        {
            Key = "button",
            Order = new[]
            {
                AnnouncementKinds.Enabled,   // "unavailable" status word — first
                AnnouncementKinds.Label,     // the ability name
                AnnouncementKinds.Reason,    // why it's unavailable — right after the name
                AnnouncementKinds.Role,
                AnnouncementKinds.Value,
                AnnouncementKinds.Selected,
                AnnouncementKinds.Tooltip,
                AnnouncementKinds.Position,
            },
            Common = () => RoleWord("button"),
        };

        public static readonly ControlType Toggle = new ControlType
        {
            Key = "toggle",
            Order = StandardOrder,
            Common = () => RoleWord("toggle"),
        };

        public static readonly ControlType Slider = new ControlType
        {
            Key = "slider",
            Order = StandardOrder,
            Common = () => RoleWord("slider"),
        };

        /// <summary>One option of a single-select group (dropdown options, tab rows). Key matches the
        /// legacy collapsed element key ("radio_button" — ProxySelectionItem + ProxyChoiceOption shared
        /// it), so existing user overrides keep applying. The role word's loc key has the legacy space:
        /// "role.radio button".</summary>
        public static readonly ControlType RadioButton = new ControlType
        {
            Key = "radio_button",
            Order = StandardOrder,
            Common = () => RoleWord("radio button"),
        };

        /// <summary>A dropdown (the game's term is combo box): value = the current option; activation
        /// opens the option submenu. Key matches the legacy collapsed element key.</summary>
        public static readonly ControlType ComboBox = new ControlType
        {
            Key = "combo_box",
            Order = StandardOrder,
            Common = () => RoleWord("combo box"),
        };

        /// <summary>A tab in a tab strip (settings pages, service-window pages).</summary>
        public static readonly ControlType Tab = new ControlType
        {
            Key = "tab",
            Order = StandardOrder,
            Common = () => RoleWord("tab"),
        };

        /// <summary>One binding slot of a key-binding row (label + current combo; rebind/clear). No
        /// legacy element carried this concept; the loc key ("element.key_binding") is pre-staged.</summary>
        public static readonly ControlType KeyBinding = new ControlType
        {
            Key = "key_binding",
            Order = StandardOrder,
            Common = () => RoleWord("key binding"),
        };

        /// <summary>An inventory/loot/vendor item ("label, item[, state]").</summary>
        public static readonly ControlType Item = new ControlType
        {
            Key = "item",
            Order = StandardOrder,
            Common = () => RoleWord("item"),
        };

        /// <summary>An expandable group header (a tree section). No role word of its own — the announcer
        /// appends the expanded/collapsed state word.</summary>
        public static readonly ControlType Group = new ControlType
        {
            Key = "group",
            Order = StandardOrder,
        };

        /// <summary>A read-only text line — no role word; typed so its parts are still user-configurable.</summary>
        public static readonly ControlType Text = new ControlType
        {
            Key = "text",
            Order = StandardOrder,
        };

        /// <summary>Every registered type, for settings registration. New types are added here.</summary>
        public static readonly ControlType[] All = { Button, Toggle, Slider, RadioButton, ComboBox, Tab, KeyBinding, Item, Group, Text };
    }
}
