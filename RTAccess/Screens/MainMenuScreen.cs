using Kingmaker;
using Kingmaker.Code.UI.MVVM;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The main menu — our first navigable RT screen. Its root is a vertical list of the sidebar entries
    /// (Continue / New Game / Load / …) read from <c>MainMenuSideBarVM</c>, so the navigator can arrow
    /// through them and confirm to run each entry's real command — letting a blind player start/load a
    /// game with our own nav and unlock the downstream screens.
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

        public override void OnPush()
        {
            Clear();
            var sidebar = RootUIContext.Instance?.MainMenuVM?.MainMenuSideBarVM;
            if (sidebar == null)
            {
                Main.Log?.Error("MainMenuScreen: sidebar VM was null at OnPush.");
                return;
            }

            // Sidebar entries in a labeled list, so focusing into it announces "Main Menu" (the container)
            // then the first entry — exercising the path diff.
            var list = new ListContainer(Loc.T("screen.main_menu"));
            list.Add(MainMenuButton.For(sidebar.ContinueVm));
            list.Add(MainMenuButton.For(sidebar.NewGameVm));
            list.Add(MainMenuButton.For(sidebar.LoadVm));
            list.Add(MainMenuButton.For(sidebar.DlcManagerVm));
            list.Add(MainMenuButton.For(sidebar.NetVm));
            list.Add(MainMenuButton.For(sidebar.OptionsVm));
            list.Add(MainMenuButton.For(sidebar.CreditVm));
            list.Add(MainMenuButton.For(sidebar.FeedbackVm));
            list.Add(MainMenuButton.For(sidebar.LicenseVm));
            list.Add(MainMenuButton.For(sidebar.ExitVm));
            Add(list);
        }

        public override void OnPop() => Clear();
    }
}
