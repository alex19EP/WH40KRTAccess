namespace RTAccess.UI
{
    /// <summary>
    /// A collapsible group in a treeview (Shape = Tree). Holds child nodes/controls; the navigator
    /// reveals its children only while <see cref="Container.Expanded"/>, and Right/Left
    /// expand/collapse it. Reads its label + expanded/collapsed state (via <see cref="Container"/>).
    /// An unlabeled instance also serves as the tree root (structural, never focused directly).
    /// </summary>
    public sealed class TreeGroup : Container
    {
        public TreeGroup(string label = null) : base(ContainerShape.Tree, label) { }
    }
}
