using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Code.UI.MVVM;
using Kingmaker.Code.UI.MVVM.VM.FeedbackPopup;
using Kingmaker.Code.UI.MVVM.View.MainMenu.PC;
using Kingmaker.GameInfo;
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
    /// Three Tab-stops, mirroring what the sighted sidebar actually carries: the ENTRY list (only the
    /// entries the view binds as sidebar buttons), the LINK block under it (the Website / Licence /
    /// Discord paper buttons, plus Feedback — the outward-facing entries live together, out of the way of
    /// the play commands), and the INFO block the screen prints without any button — the message of the
    /// day (the welcome text block, fetched asynchronously into the live view) and the game version stamp.
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
            // The Dark Heresy promo is the one popup whose VM is never nulled — closing it only hides the
            // view — so ask whether it is actually on screen (see DarkHeresyScreen), not whether the VM
            // exists; otherwise dismissing it would strand the menu for the rest of the session.
            if (DarkHeresyScreen.IsShowing()) return false;
            return true;
        }


        public override void Build(GraphBuilder b)
        {
            var sidebar = RootUIContext.Instance?.MainMenuVM?.MainMenuSideBarVM;
            if (sidebar == null) return; // nothing declared = closed until the VM exists

            // The same labeled level the old ListContainer provided: focusing into the list announces
            // "Main Menu, list" (the context) then the first entry — via the focus-path diff.
            b.BeginStop("menu").PushContext(Loc.T("screen.main_menu"), Loc.T("role.list"));
            // The sidebar proper: exactly the entries the sighted sidebar view binds as buttons. License
            // and Feedback are NOT among them — they belong to the link block below.
            var entries = new[]
            {
                sidebar.ContinueVm, sidebar.NewGameVm, sidebar.LoadVm, sidebar.DlcManagerVm,
                sidebar.NetVm, sidebar.OptionsVm, sidebar.CreditVm, sidebar.ExitVm,
            };
            for (int i = 0; i < entries.Length; i++)
            {
                var vm = entries[i];
                if (vm == null || vm.IsSeparator) continue; // a separator was never focusable
                b.AddItem(ControlId.Referenced(vm, "mainmenu:" + i), GraphNodes.MenuEntry(vm));
            }
            b.PopContext();

            // The link block: the paper buttons beside the sidebar (MainMenuSideBarView's Website /
            // Licence / Discord, in that order) plus Feedback, which reaches the same family of external
            // links through its popup. Website and Discord are plain OwlcatButtons with no VM entry of
            // their own, so they open the same URL the view's click handler does; Licence and Feedback run
            // their sidebar entry VMs — the game's own commands — so they keep their live enabled state.
            b.BeginStop("links").PushContext(Loc.T("label.links"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural("mainmenu:link:website"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.FeedbackPopupTexts.Website, "link.website"),
                () => sidebar.OpenUrl(FeedbackPopupItemType.Website)));
            if (sidebar.LicenseVm != null)
                b.AddItem(ControlId.Referenced(sidebar.LicenseVm, "mainmenu:link:license"),
                    GraphNodes.MenuEntry(sidebar.LicenseVm));
            b.AddItem(ControlId.Structural("mainmenu:link:discord"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.FeedbackPopupTexts.Discord, "link.discord"),
                () => sidebar.OpenUrl(FeedbackPopupItemType.Discord)));
            if (sidebar.FeedbackVm != null)
                b.AddItem(ControlId.Referenced(sidebar.FeedbackVm, "mainmenu:link:feedback"),
                    GraphNodes.MenuEntry(sidebar.FeedbackVm));
            b.PopContext();

            // The text the menu shows without a control: the welcome block ("message of the day") and the
            // version stamp. The message is downloaded asynchronously into the live view's label, so it is
            // read off the VIEW (the VM only pushes it through a callback) — the same "some state lives on
            // the view" rule the control screens follow; absent (offline, not yet arrived) = no node.
            b.BeginStop("info");
            var motd = MessageOfTheDay();
            if (!string.IsNullOrEmpty(motd))
                b.AddItem(ControlId.Structural("mainmenu:motd"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new System.Collections.Generic.List<NodeAnnouncement>
                    {
                        GraphNodes.LabelPart(() => Loc.T("mainmenu.message_of_the_day")),
                        GraphNodes.TooltipPart(() => motd),
                    },
                    SearchText = () => Loc.T("mainmenu.message_of_the_day"),
                    OnTooltip = () => TooltipChooser.Open(Loc.T("mainmenu.message_of_the_day"), motd,
                        sections: null, links: null),
                });
            b.AddItem(ControlId.Structural("mainmenu:version"), GraphNodes.Text(
                () => Loc.T("mainmenu.version", new { version = GameVersion.GetVersion() })));
        }

        // The welcome text block's current content, cleaned of the TMP link/colour markup the view feeds it.
        private static string MessageOfTheDay()
        {
            var view = LiveView.Find<MainMenuSideBarPCView>();
            var raw = view != null && view.m_MotivationText != null ? view.m_MotivationText.text : null;
            return string.IsNullOrWhiteSpace(raw) ? null : TextUtil.StripRichTextSpaced(raw);
        }
    }
}
