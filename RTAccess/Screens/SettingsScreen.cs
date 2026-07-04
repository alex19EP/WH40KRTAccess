using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Settings;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The settings window (shared CommonVM screen — covers the main-menu options AND the in-game pause
    /// menu's options). Tree: root Panel = [tabs List, content TreeGroup of per-header sections, Close].
    /// The content is rebuilt when the tab changes (poll-detected); the tabs/Close stay stable so tab-list
    /// focus survives the rebuild. Layer 25 → sits above the base context (e.g. the main menu) on the stack.
    /// </summary>
    public sealed class SettingsScreen : Screen
    {
        public SettingsScreen() { Wrap = true; } // Tab wraps around the whole dialog

        public override string Key => "overlay.settings";
        public override string ScreenName => Loc.T("screen.settings");
        public override int Layer => 25;

        public override bool IsActive() => Vm() != null;

        private SettingsVM _builtFrom;
        private object _lastTab;
        private TreeGroup _content;

        private static SettingsVM Vm()
        {
            var cvm = Game.Instance?.RootUiContext?.CommonVM;
            return cvm?.SettingsVM.Value;
        }

        public override void OnPush() { _builtFrom = null; _lastTab = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtFrom = null; _content = null; }

        // Back (Escape) closes settings. (Close prompts a save dialog if there are unconfirmed changes —
        // that modal isn't navigable yet.)
        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.Close());
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (vm != _builtFrom)
            {
                // Settings VM swapped while open (e.g. locale/theme apply rebuilds it). Re-home focus.
                Rebuild();
                Navigation.Attach(this);
                return;
            }
            if (!ReferenceEquals(vm.SelectedMenuEntity.Value, _lastTab))
                RebuildContent(vm);
        }

        private void Rebuild()
        {
            Clear();
            _content = null;
            var vm = Vm();
            _builtFrom = vm;
            if (vm == null) return;

            var tabs = new ListContainer(Loc.T("label.tabs"));
            foreach (var tab in vm.MenuEntitiesList)
                tabs.Add(new ProxySettingsTab(tab));
            Add(tabs);

            // The current tab's settings as a treeview: each header group is one collapsible node, so it's
            // a single Tab-stop you arrow through (and can collapse to skip), not dozens of stops.
            _content = new TreeGroup();
            Add(_content);
            RebuildContent(vm);

            // Apply / Reset-to-default — each opens the game's own confirm dialog (the MessageBoxScreen
            // makes it navigable). Apply is live-gated on there being unsaved changes; Default on the tab
            // supporting it. Close (and Escape) prompts the same save dialog if there are unsaved changes.
            // The game's settings chrome buttons are Plastick (SettingsPCView.SetButtonsSounds).
            var plastick = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound;
            Add(new ProxyActionButton(() => Loc.T("settings.apply"),
                () => Kingmaker.Settings.SettingsController.Instance.HasUnconfirmedSettings(),
                () => vm.OpenApplySettingsDialog(), hoverSoundType: plastick, clickSoundType: plastick));
            Add(new ProxyActionButton(() => Loc.T("action.reset"),
                () => vm.IsDefaultButtonInteractable.Value, () => vm.OpenDefaultSettingsDialog(),
                hoverSoundType: plastick, clickSoundType: plastick));
            Add(new ProxyActionButton(() => Loc.T("action.close"), () => true, () => vm.Close(),
                hoverSoundType: plastick, clickSoundType: plastick));
        }

        // Refills only the content (tabs/Close stay put), so tab-list focus survives a tab switch.
        private void RebuildContent(SettingsVM vm)
        {
            _lastTab = vm.SelectedMenuEntity.Value;
            if (_content == null) return;
            _content.Clear();
            SettingsEntityBuilder.BuildInto(_content, vm.SettingEntities, tree: true);
        }
    }
}
