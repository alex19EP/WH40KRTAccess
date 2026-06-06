namespace RTAccess.Accessibility;

/// <summary>
/// Implemented by synthetic console-navigation entities (e.g. <see cref="VirtualNavItem"/>) that supply their
/// own spoken string instead of being read by scraping a Unity component. <see cref="UiTextReader.Describe"/>
/// checks this first, so an injected item reads through the existing focus path with no special casing.
/// </summary>
internal interface IAccessibleTextProvider
{
    /// <summary>Text to speak when this entity is focused. Evaluated lazily so a re-read reflects current state.</summary>
    string GetAccessibleText();
}
