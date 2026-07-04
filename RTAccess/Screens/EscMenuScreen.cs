using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.EscMenu;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The in-game pause / Escape menu (<c>CommonVM.EscMenuContextVM.EscMenu</c>) as a navigable screen —
    /// so a blind player can pause, save, reach the options, and quit.
    ///
    /// Unlike WOTR (whose EscMenuVM held a collection of ContextMenuEntityVM buttons we could reuse via
    /// <see cref="UI.GraphNodes.MenuEntry"/>), RT's <see cref="EscMenuVM"/> exposes NO button collection: it has
    /// direct command methods (<c>OnSave</c>/<c>OnLoad</c>/<c>OnSettings</c>/<c>OnMainMenu</c>/<c>OnQuit</c>/
    /// …) plus plain gating flags (<c>IsSavingAllowed</c>/<c>IsOptionsAllowed</c>/…). So we drive
    /// <see cref="ProxyActionButton"/>s straight off those methods, with live <c>enabled</c> funcs reading
    /// the flags — mirroring the buttons <c>EscMenuBaseView</c> wires (verified against the decompiled view).
    /// We list the single-player entries; multiplayer/roles/bug-report are skipped. Save / Load / Options
    /// each close this menu and open a screen we already make navigable (SaveLoad / Settings); Main Menu and
    /// Exit raise the game's confirm box (navigable via <see cref="MessageBoxScreen"/>).
    ///
    /// Layer 20: above the in-game context (0) and service windows (10), below Settings (25) and the
    /// MessageBox confirm (30) that the Main Menu / Exit entries raise WHILE this menu stays open. Escape
    /// resumes by closing through the VM's own <c>OnClose</c> (the same path the game's close uses).
    /// </summary>
    public sealed class EscMenuScreen : Screen
    {
        public override string Key => "ctx.escmenu";
        public override string ScreenName => "Game Menu";
        public override int Layer => 20;

        public override bool IsActive() => Vm() != null;

        private static EscMenuVM Vm()
            => Game.Instance?.RootUiContext?.CommonVM?.EscMenuContextVM?.EscMenu?.Value;

        private EscMenuVM _builtVm;

        // Build in OnPush so the screen-name + first-focus announce right after push (lazy OnUpdate builds
        // would land focus silently, after the announce). Rebuild only if the VM is swapped under us.
        public override void OnPush() { _builtVm = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtVm = null; }
        public override void OnUpdate() { Rebuild(); }

        private void Rebuild()
        {
            var vm = Vm();
            if (vm == null || vm == _builtVm) return;
            _builtVm = vm;
            Clear();

            var list = new ListContainer();
            // Save / Load — close this menu and open our SaveLoadScreen (Save / Load mode).
            list.Add(new ProxyActionButton("Save Game", () => vm.IsSavingAllowed, () => vm.OnSave()));
            list.Add(new ProxyActionButton("Load Game", () => true, () => vm.OnLoad()));
            // Formation — surface only (the view hides it in space, mirroring IsInSpace).
            if (!vm.IsInSpace.Value)
                list.Add(new ProxyActionButton("Formation", () => vm.IsFormationAllowed, () => vm.OpenFormation()));
            // Options — opens our SettingsScreen.
            list.Add(new ProxyActionButton("Options", () => vm.IsOptionsAllowed, () => vm.OnSettings()));
            // Mods and DLC — opens the DLC manager (not navigable yet; listed for menu parity, like the
            // in-game window buttons whose screens land in a later phase).
            list.Add(new ProxyActionButton("Mods and DLC", () => vm.IsModsAllowed, () => vm.OnMods()));
            // Main Menu / Exit — each raises a confirm MessageBox (navigable via MessageBoxScreen) before
            // it acts, so a blind player gets a read-back prompt rather than an instant quit.
            list.Add(new ProxyActionButton("Main Menu", () => true, () => vm.OnMainMenu()));
            list.Add(new ProxyActionButton("Exit Game", () => true, () => vm.OnQuit()));
            Add(list);
        }

        // Escape resumes — close through the VM's own action (same path the game's close / re-press uses).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Raw("Close"), _ => vm.OnClose());
        }
    }
}
