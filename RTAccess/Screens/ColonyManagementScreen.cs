using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                     // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.ColonyManagement;    // ColonyManagementVM + page/navigation
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The Colony Management service window (Ctrl+Y / the HUD opener — remote colony administration from the
    /// ship). The game composes it from the SAME component VMs as the exploration tablet's Colony section,
    /// each set with <c>isColonyManagement: true</c> (colony events answer with the "needs visit" warning
    /// instead of starting their dialog — already voiced by WarningReader), so the content renders through
    /// the shared <see cref="ColonyNodes"/> builders. One radio row per colony (selection = the element's own
    /// SelectPage → the net-synced SelectColony command), then the selected colony's rewards popup / stats /
    /// traits / events / built projects / projects window. ScreenName is null: ServiceWindowAnnounce already
    /// speaks the window name. M3 of docs/plans/orbital-listing-wilkes.md.
    /// </summary>
    public sealed class ColonyManagementScreen : Screen
    {
        public override string Key => "colonymgmt";
        public override int Layer => 10;            // service window, like Inventory/Journal
        public override string ScreenName => null;

        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.ColonyManagement
               && Vm() != null;

        private static ServiceWindowsVM ServiceWindows()
        {
            var ctx = Game.Instance?.RootUiContext;
            return ctx?.SurfaceVM?.StaticPartVM?.ServiceWindowsVM
                ?? ctx?.SpaceVM?.StaticPartVM?.ServiceWindowsVM;
        }

        private static ColonyManagementVM Vm() => ServiceWindows()?.ColonyManagementVM?.Value;

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "cm:" + vm.GetHashCode() + ":";

            if (!vm.HasColonies.Value)
            {
                b.AddLabel(ControlId.Structural(k + "none"), () => Loc.T("colonymgmt.no_colonies"));
                return;
            }

            b.BeginStop("colonies").PushContext(Loc.T("colonymgmt.colonies"), role: "list");
            int i = 0;
            foreach (var nav in vm.NavigationVM.NavigationElements)
            {
                var n = nav; // capture
                if (n == null) continue;
                b.AddItem(ControlId.Referenced(n, k + "col:" + i++), GraphNodes.ChoiceOption(
                    () => n.Title,
                    () => n.IsSelected.Value,
                    () => n.SelectPage()));
            }
            b.PopContext();

            var page = vm.ColonyManagementPage.Value;
            if (page == null) return;
            Func<bool> locked = () => vm.IsLockUIForDialog.Value;

            ColonyNodes.BuildRewards(b, k, page.ColonyRewardsVM);
            if (page.ColonyProjectsVM != null && page.ColonyProjectsVM.ShouldShow.Value)
                ColonyNodes.BuildProjects(b, k, page.ColonyProjectsVM, locked);
            else
                ColonyNodes.BuildComponents(b, k,
                    page.ColonyStatsVM, page.ColonyTraitsVM, page.ColonyEventsVM,
                    page.ColonyProjectsBuiltListVM,
                    page.ColonyProjectsButtonVM.OpenColonyProjects,
                    locked);
        }

        // Escape: leave the projects sub-window first (the game's own close handler), else close the
        // service window like every other one.
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm == null) yield break;
            yield return new ElementAction(ActionIds.Back, Message.Raw(GameText.Action("close")), _ =>
            {
                var page = vm.ColonyManagementPage.Value;
                if (page?.ColonyProjectsVM != null && page.ColonyProjectsVM.ShouldShow.Value)
                    ColonyNodes.CloseProjects();
                else
                    ServiceWindows()?.HandleCloseAll();
            });
        }
    }
}
