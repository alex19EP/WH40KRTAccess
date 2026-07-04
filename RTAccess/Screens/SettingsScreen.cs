using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Settings;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The settings window (shared CommonVM screen — covers the main-menu options AND the in-game pause
    /// menu's options). Tab-stops: the tab strip (Game / Difficulty / Controls / Graphics / …), then the
    /// current tab's settings as a tree of collapsible header sections (one stop you arrow through), then
    /// the Apply / Reset-to-default / Close buttons. Layer 25 → sits above the base context (e.g. the
    /// main menu) on the stack.
    ///
    /// Graph-native: declared fresh from the live VM every render. The graph starts on the SELECTED tab;
    /// content node keys carry the selected tab's identity, so switching tabs re-keys only the content
    /// (focus on the tab strip is untouched) and section expansion is remembered per tab by key. A VM
    /// swap while open (locale/theme apply rebuilds it) just changes every key — focus re-homes with no
    /// rebuild bookkeeping.
    /// </summary>
    public sealed class SettingsScreen : Screen
    {
        public SettingsScreen() { Wrap = true; } // Tab wraps around the whole dialog

        public override string Key => "overlay.settings";
        public override string ScreenName => Loc.T("screen.settings");
        public override int Layer => 25;

        public override bool IsActive() => Vm() != null;

        private static SettingsVM Vm()
        {
            var cvm = Game.Instance?.RootUiContext?.CommonVM;
            return cvm?.SettingsVM.Value;
        }

        // Back (Escape) closes settings — through the VM's own Close, which prompts the save-changes
        // dialog when there are unconfirmed changes (MessageBoxScreen makes it navigable).
        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.Close());
        }

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "settings:" + vm.GetHashCode() + ":";

            // The tab strip: one stop, arrows between tabs; the graph STARTS on the selected tab.
            b.BeginStop("tabs").PushContext(Loc.T("label.tabs"), Loc.T("role.list"));
            var tabs = vm.MenuEntitiesList;
            for (int i = 0; i < tabs.Count; i++)
            {
                var id = ControlId.Referenced(tabs[i], k + "tab:" + i);
                b.AddItem(id, GraphNodes.SettingsTab(tabs[i], vm));
                if (ReferenceEquals(vm.SelectedMenuEntity.Value, tabs[i])) b.SetStart(id);
            }
            b.PopContext();

            // The current tab's settings: header sections as collapsible groups in one stop. Keys carry
            // the selected tab, so a tab switch re-keys the content and expansion is remembered per tab.
            var selected = vm.SelectedMenuEntity.Value;
            string contentKey = k + "tab" + (selected != null ? selected.GetHashCode().ToString() : "?") + ":";
            b.BeginStop("content");
            SettingsEntityGraph.Emit(b, vm.SettingEntities, contentKey);

            // Apply / Reset-to-default / Close: individual stops, like a Windows dialog. Apply and Reset
            // each open the game's own confirm dialog (MessageBoxScreen makes it navigable); Apply is
            // live-gated on there being unsaved changes, Reset on the tab supporting it. The game's
            // settings chrome buttons are Plastick (SettingsPCView.SetButtonsSounds).
            var plastick = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound;
            b.BeginStop("apply").AddItem(ControlId.Structural(k + "apply"),
                GraphNodes.Button(() => Loc.T("settings.apply"),
                    () => vm.OpenApplySettingsDialog(),
                    () => Kingmaker.Settings.SettingsController.Instance.HasUnconfirmedSettings(),
                    hoverSound: plastick, clickSound: plastick));
            b.BeginStop("reset").AddItem(ControlId.Structural(k + "reset"),
                GraphNodes.Button(() => Loc.T("action.reset"),
                    () => vm.OpenDefaultSettingsDialog(),
                    () => vm.IsDefaultButtonInteractable.Value,
                    hoverSound: plastick, clickSound: plastick));
            b.BeginStop("close").AddItem(ControlId.Structural(k + "close"),
                GraphNodes.Button(() => Loc.T("action.close"), () => vm.Close(),
                    hoverSound: plastick, clickSound: plastick));
        }
    }
}
