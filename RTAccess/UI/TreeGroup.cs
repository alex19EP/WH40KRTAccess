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

        /// <summary>Optional themed hover/click sound-type for this node — e.g. <c>NoSound</c> to silence a
        /// node the game deliberately keeps quiet (dense stat grids). Null ⇒ the generic hover / default
        /// click. Settable so a build-site can tag nodes without a bespoke subclass.</summary>
        public Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? HoverSound { get; set; }
        public Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? ClickSound { get; set; }

        public override Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? HoverSoundType => HoverSound;
        public override Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? ClickSoundType => ClickSound;
    }
}
