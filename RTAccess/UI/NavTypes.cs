namespace RTAccess.UI
{
    public enum NavDirection { Up, Down, Left, Right }

    /// <summary>
    /// Container shape — how a navigator traverses it.
    /// List/Grid: arrows move among items; the whole container is a single Tab-stop.
    /// Panel: Tab/Shift-Tab traverse its focusable descendants (WinForms-style).
    /// Tree: a treeview — one Tab-stop; Up/Down over expanded nodes (DFS), Right/Left expand/collapse.
    /// </summary>
    public enum ContainerShape { VerticalList, HorizontalList, Grid, Panel, Tree }
}
