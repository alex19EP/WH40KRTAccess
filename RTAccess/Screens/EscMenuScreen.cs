using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Code.UI.MVVM.VM.EscMenu;
using Kingmaker.DLC;
using Kingmaker.Networking;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The in-game pause / Escape menu (<c>CommonVM.EscMenuContextVM.EscMenu</c>) as a navigable screen —
    /// so a blind player can pause, save, reach the options, and quit.
    ///
    /// Unlike WOTR (whose EscMenuVM held a collection of ContextMenuEntityVM buttons reusable via
    /// <see cref="UI.GraphNodes.MenuEntry"/>), RT's <see cref="EscMenuVM"/> exposes NO button collection: it has
    /// direct command methods (<c>OnSave</c>/<c>OnLoad</c>/<c>OnSettings</c>/<c>OnMainMenu</c>/<c>OnQuit</c>/
    /// …) plus plain gating flags (<c>IsSavingAllowed</c>/<c>IsOptionsAllowed</c>/…). So the buttons are
    /// declared straight off those methods via <see cref="UI.GraphNodes.Button"/>, labels passing through
    /// the game's own card strings (<c>UIStrings.EscapeMenu</c> — the view's <c>SetButtonsTexts</c> source,
    /// so they follow the game's language) and <c>enabled</c> funcs reading the flags — mirroring the
    /// buttons <c>EscMenuBaseView</c> wires (verified against the decompiled view). We list the
    /// single-player entries; multiplayer/roles/bug-report are skipped. Save / Load / Options each close
    /// this menu and open a screen we already make navigable (SaveLoad / Settings); Main Menu and Exit
    /// raise the game's confirm box (navigable via <see cref="MessageBoxScreen"/>).
    ///
    /// Graph-native: declared fresh from the live VM every render — the Formation entry simply isn't
    /// declared in space (the view hides it there, mirroring <c>IsInSpace</c>), and the gating flags read
    /// live, so the async <c>IsSavingAllowed</c> settling under an open menu just shows up.
    ///
    /// Layer 20: above the in-game context (0) and service windows (10), below Settings (25) and the
    /// MessageBox confirm (30) that the Main Menu / Exit entries raise WHILE this menu stays open. Escape
    /// resumes by closing through the VM's own <c>OnClose</c> (the same path the game's close uses).
    /// </summary>
    public sealed class EscMenuScreen : Screen
    {
        public override string Key => "ctx.escmenu";
        public override string ScreenName => Loc.T("screen.game_menu");
        public override int Layer => 20;

        public override bool IsActive() => Vm() != null;

        private static EscMenuVM Vm()
            => Game.Instance?.RootUiContext?.CommonVM?.EscMenuContextVM?.EscMenu?.Value;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            // Save / Load — close this menu and open our SaveLoadScreen (Save / Load mode).
            b.AddItem(ControlId.Structural("escmenu:save"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.EscapeMenu.EscMenuSaveGame, "escmenu.save"),
                () => vm.OnSave(), () => vm.IsSavingAllowed));
            b.AddItem(ControlId.Structural("escmenu:load"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.EscapeMenu.EscMenuLoadGame, "escmenu.load"),
                () => vm.OnLoad()));
            // Formation — surface only (the view hides it in space, mirroring IsInSpace).
            if (!vm.IsInSpace.Value)
                b.AddItem(ControlId.Structural("escmenu:formation"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.EscapeMenu.EscMenuFormation, "hudmenu.formation"),
                    () => vm.OpenFormation(), () => vm.IsFormationAllowed));
            // Options — opens our SettingsScreen.
            b.AddItem(ControlId.Structural("escmenu:options"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.EscapeMenu.EscMenuOptions, "escmenu.options"),
                () => vm.OnSettings(), () => vm.IsOptionsAllowed));
            // Mods and DLC — opens the DLC manager (not navigable yet; listed for menu parity, like the
            // in-game window buttons whose screens land in a later phase). The card carries a count badge
            // of integral DLCs still switchable on — mirrored into the label (label mirrors the card).
            b.AddItem(ControlId.Structural("escmenu:mods"), GraphNodes.Button(
                ModsLabel, () => vm.OnMods(), () => vm.IsModsAllowed));
            // Main Menu / Exit — each raises a confirm MessageBox (navigable via MessageBoxScreen) before
            // it acts, so a blind player gets a read-back prompt rather than an instant quit.
            b.AddItem(ControlId.Structural("escmenu:mainmenu"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.EscapeMenu.EscMenuMainMenu, "screen.main_menu"),
                () => vm.OnMainMenu()));
            b.AddItem(ControlId.Structural("escmenu:exit"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.EscapeMenu.EscMenuExit, "escmenu.exit"),
                () => vm.OnQuit()));
        }

        private static string ModsLabel()
        {
            string label = GameText.Or(() => UIStrings.Instance.EscapeMenu.ModsAndDlc, "escmenu.mods");
            int count = SwitchableDlcCount();
            return count > 0 ? label + ", " + Loc.T("escmenu.dlc_badge", new { count }) : label;
        }

        // The card's badge query (EscMenuBaseView.BindViewImplementation): integral DLCs for the current
        // campaign not yet switched on and not too late to switch — hidden in a multiplayer lobby. Read
        // lazily when the label is spoken; any hiccup just drops the badge (it's ornamental).
        private static int SwitchableDlcCount()
        {
            try
            {
                if (PhotonManager.Lobby.IsActive) return 0;
                int n = 0;
                foreach (var dlc in Game.Instance.Player.GetAvailableAdditionalContentDlcForCurrentCampaign())
                {
                    var bp = dlc as BlueprintDlc;
                    if ((bp == null || !bp.CheckIsLateToSwitch()) && !(bp?.GetDlcSwitchOnOffState() ?? false))
                        n++;
                }
                return n;
            }
            catch { return 0; }
        }

        // Escape resumes — close through the VM's own action (same path the game's close / re-press uses).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                    _ => vm.OnClose());
        }
    }
}
