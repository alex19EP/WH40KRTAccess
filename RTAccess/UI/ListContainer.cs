namespace RTAccess.UI
{
    /// <summary>
    /// An arrow-navigated list: arrows move among items, and the whole list is a
    /// single Tab-stop (Tab enters once → first/remembered item; Tab again leaves).
    /// </summary>
    public sealed class ListContainer : Container
    {
        public ListContainer(string label = null)
            : base(ContainerShape.VerticalList, label) { }

        public ListContainer(ContainerShape orientation, string label = null)
            : base(orientation, label) { }
    }
}
