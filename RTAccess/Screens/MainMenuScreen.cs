using Kingmaker;
using Kingmaker.Code.UI.MVVM;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The main menu — the screen the game boots into. A vertical list of the sidebar entries
    /// (Continue / New Game / Load / …) read from <c>MainMenuSideBarVM</c>, so the navigator can arrow
    /// through them and confirm to run each entry's real command — letting a blind player start/load a
    /// game with our own nav and unlock the downstream screens.
    ///
    /// Graph-native: the entries are declared fresh from the live VMs every render — enabled state reads
    /// the live entity per entry (<see cref="UI.GraphNodes.MenuEntry"/>), and entry identity rides the
    /// entry VMs (tier 1).
    /// </summary>
    public sealed class MainMenuScreen : Screen
    {
        public override string Key => "ctx.mainmenu";
        public override int Layer => 0;
        // No ScreenName: the sidebar lives in a labeled "Main Menu" list, so the navigator announces it
        // via the focus-path diff instead of the screen self-announcing.

        public override bool IsActive()
        {
            var mm = Game.Instance?.RootUiContext?.MainMenuVM;
            if (mm == null) return false; // RootUiContext.IsMainMenu == (MainMenuVM != null)

            // Stop being navigable whenever a main-menu sub-window / popup covers the sidebar (each gets
            // its own screen in a later phase).
            if (mm.NewGameVM.Value != null) return false;
            if (mm.CharGenContextVM?.CharGenVM?.Value != null) return false;
            if (mm.CreditsVM.Value != null) return false;
            if (mm.FirstLaunchSettings.Value != null) return false;
            if (mm.TermsOfUseVM.Value != null) return false;
            if (mm.FeedbackPopupVM.Value != null) return false;
            if (mm.DarkHeresyPopUpVM.Value != null) return false;
            return true;
        }


        public override void Build(GraphBuilder b)
        {
            var sidebar = RootUIContext.Instance?.MainMenuVM?.MainMenuSideBarVM;
            if (sidebar == null) return; // nothing declared = closed until the VM exists

            // The same labeled level the old ListContainer provided: focusing into the list announces
            // "Main Menu, list" (the context) then the first entry — via the focus-path diff.
            b.PushContext(Loc.T("screen.main_menu"), Loc.T("role.list"));
            var entries = new[]
            {
                sidebar.ContinueVm, sidebar.NewGameVm, sidebar.LoadVm, sidebar.DlcManagerVm,
                sidebar.NetVm, sidebar.OptionsVm, sidebar.CreditVm, sidebar.FeedbackVm,
                sidebar.LicenseVm, sidebar.ExitVm,
            };
            for (int i = 0; i < entries.Length; i++)
            {
                var vm = entries[i];
                if (vm == null || vm.IsSeparator) continue; // a separator was never focusable
                b.AddItem(ControlId.Referenced(vm, "mainmenu:" + i), GraphNodes.MenuEntry(vm));
            }
            b.PopContext();
        }
    }
}
