namespace RTAccess.UI
{
    /// <summary>
    /// A WinForms-style container: Tab / Shift-Tab traverse its focusable
    /// descendants in order (descending through nested panels; a child List counts
    /// as one tab-stop). Optionally labeled (e.g. a settings section header), so
    /// Tab-ing into it announces the label via the focus-path diff.
    /// </summary>
    public sealed class Panel : Container
    {
        public Panel(string label = null) : base(ContainerShape.Panel, label) { }
    }
}
