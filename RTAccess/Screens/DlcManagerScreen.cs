using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Code.UI.MVVM.VM.DlcManager;
using Kingmaker.Code.UI.MVVM.VM.DlcManager.Dlcs;
using Kingmaker.Code.UI.MVVM.VM.DlcManager.Mods;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's "Mods and DLC" window (<c>CommonVM.DlcManagerVM</c>) as a navigable screen — the native
    /// entry point for the mod's own settings. The window's Mods tab lists every installed mod (the game's
    /// <c>ModInitializer.GetAllModsInfo</c> merges the Owlcat mod manager AND Unity Mod Manager, so this mod
    /// appears there too); a blind player selects RTAccess, opens its Settings, and lands in the accessible
    /// <see cref="ModSettingsScreen"/> instead of the inaccessible UMM IMGUI overlay the game would otherwise
    /// raise (<c>DlcManagerModEntityVM.OpenModSettings → UnityModManagerAdapter.OpenModInfoWindow</c>). The
    /// game advertises "settings" for a UMM mod only when it registers an <c>OnGUI</c> handler
    /// (<c>ExtendedModInfo.HasSettings = modEntry.OnGUI != null</c>), which <see cref="Main"/> now does.
    ///
    /// Reachable from both the main-menu "Mods" button and the in-game Esc-menu "Mods and DLC" entry (both
    /// route through <c>CommonVM.HandleOpenDlcManager</c>), so mod settings are configurable before loading a
    /// save and mid-game. Layer 25 — a full-screen window over the menu/in-game context, like the settings
    /// window it sits beside.
    ///
    /// Tab-stops: the tab strip (DLC / Mods — the game's own menu entities), then the selected tab's content
    /// (keys carry the tab, so switching re-keys content only). The Mods tab is fully driven — each row's
    /// Enter opens a small menu (Settings / Enable-Disable / Description) so the accessibility mod can't be
    /// disabled by a stray keypress; Space reads the description. The DLC tab is a READ-ONLY informational
    /// list for now (name + state) — store/purchase and switch-on-in-save flows are out of scope for this
    /// screen.
    /// </summary>
    public sealed class DlcManagerScreen : Screen
    {
        public DlcManagerScreen() { Wrap = true; } // Tab wraps around the window

        public override string Key => "overlay.dlcmanager";
        public override string ScreenName => GameText.Or(() => UIStrings.Instance.EscapeMenu.ModsAndDlc, "screen.mods");
        public override int Layer => 25;

        public override bool IsActive() => Vm() != null;

        private static DlcManagerVM Vm()
            => Game.Instance?.RootUiContext?.CommonVM?.DlcManagerVM?.Value;

        // Back (Escape) closes the window through the VM's own close (which runs the reload/resave checks).
        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.OnClose());
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            // The tab strip: the game's menu entities (DLC / Mods). The graph STARTS on the selected tab.
            b.BeginStop("tabs").PushContext(Loc.T("label.tabs"), Loc.T("role.list"));
            var menu = vm.MenuSelectionGroup?.EntitiesCollection;
            if (menu != null)
            {
                int i = 0;
                foreach (var entity in menu)
                {
                    var e = entity; // capture
                    var id = ControlId.Referenced(e, "dlcmgr:tab:" + i);
                    b.AddItem(id, TabNode(vm, e));
                    if (ReferenceEquals(vm.SelectedMenuEntity.Value, e)) b.SetStart(id);
                    i++;
                }
            }
            b.PopContext();

            // The selected tab's content. Keys carry which tab is showing, so switching re-keys the content
            // (tab focus survives). IsModsWindow is the game's own "the Mods tab is active" flag.
            bool mods = vm.IsModsWindow.Value;
            b.BeginStop("content").PushContext(
                mods ? GameText.Or(() => UIStrings.Instance.DlcManager.InstalledMods, "mods.installed")
                     : GameText.Or(() => UIStrings.Instance.DlcManager.DlcManagerLabel, "screen.mods"),
                Loc.T("role.list"));
            if (mods) BuildModsTab(b, vm, "dlcmgr:mods:");
            else BuildDlcTab(b, vm, "dlcmgr:dlc:");
            b.PopContext();
        }

        private static NodeVtable TabNode(DlcManagerVM vm, DlcManagerMenuEntityVM e)
        {
            System.Func<string> label = () => e.Title;
            return new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new System.Collections.Generic.List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(label),
                    GraphNodes.SelectedPart(() => ReferenceEquals(vm.SelectedMenuEntity.Value, e)),
                },
                SearchText = label,
                // Selecting the menu entity drives the game's own tab switch (its DoSelectMe callback).
                OnActivate = () => vm.SelectedMenuEntity.Value = e,
            };
        }

        // ---- Mods tab: fully driven ----

        private void BuildModsTab(GraphBuilder b, DlcManagerVM vm, string k)
        {
            var list = vm.ModsVM?.SelectionGroup?.EntitiesCollection;
            if (list == null || !vm.ModsVM.HaveMods)
            {
                b.AddItem(ControlId.Structural(k + "empty"),
                    GraphNodes.Text(() => GameText.Or(() => UIStrings.Instance.DlcManager.YouDontHaveAnyMods, "mods.none")));
                return;
            }
            foreach (var mod in list)
            {
                var e = mod; // capture
                b.AddItem(ControlId.Referenced(e, k + "mod:" + e.ModInfo.Id), ModRow(e));
            }
        }

        private NodeVtable ModRow(DlcManagerModEntityVM e)
        {
            System.Func<string> value = () => Loc.T(e.ModSwitchState.Value ? "mods.state.enabled" : "mods.state.disabled");
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new System.Collections.Generic.List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => (e.ModInfo.DisplayName + " " + e.ModInfo.Version).Trim()),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                    // A restart-pending mod says so (the sighted cue is the reload-warning mark). Kindless
                    // custom part = always spoken (never suppressed by the tooltip/value announcement toggles).
                    new NodeAnnouncement(() => e.WarningReloadGame.Value ? Loc.T("mods.reload_pending") : null,
                        live: true),
                },
                SearchText = () => e.ModInfo.DisplayName,
                OnActivate = () => OpenModMenu(e),   // Enter: the per-mod action menu
                OnTooltip = () => ReadDescription(e), // Space: read the description
            };
        }

        // Enter on a mod row opens a small menu — Settings (when available) / Enable-Disable / Description —
        // so a stray Enter never toggles (disabling the accessibility mod itself).
        private void OpenModMenu(DlcManagerModEntityVM e)
        {
            var rows = new System.Collections.Generic.List<ChoiceSubmenuScreen.Row>();
            if (CanSettings(e))
                rows.Add(ChoiceSubmenuScreen.Row.Action(() => Loc.T("mods.settings"), () => OpenModSettings(e)));
            rows.Add(ChoiceSubmenuScreen.Row.Action(
                () => Loc.T(e.ModSwitchState.Value ? "mods.disable" : "mods.enable"), () => e.ChangeValue()));
            rows.Add(ChoiceSubmenuScreen.Row.Action(() => Loc.T("mods.description"), () => ReadDescription(e)));
            ChoiceSubmenuScreen.OpenRows(e.ModInfo.DisplayName, rows);
        }

        // Our own row always offers Settings (we have the accessible screen); other mods offer it only when
        // the game reports one (their UMM OnGUI overlay).
        private static bool CanSettings(DlcManagerModEntityVM e)
            => e.ModInfo.Id == Main.ModId || e.ModSettingsAvailable.Value;

        private void OpenModSettings(DlcManagerModEntityVM e)
        {
            if (e.ModInfo.Id == Main.ModId) { ModSettingsScreen.Open(); return; }
            // A third-party mod: fall back to the game's own opener (the raw UMM IMGUI overlay — not
            // accessible, but the best available for other mods).
            e.OpenModSettings();
            Tts.Speak(Loc.T("mods.opening_umm"), interrupt: true);
        }

        private static void ReadDescription(DlcManagerModEntityVM e)
        {
            var desc = e.ModInfo.Description;
            if (string.IsNullOrWhiteSpace(desc)) { Tts.Speak(Loc.T("mods.no_description"), interrupt: true); return; }
            TooltipChooser.Open(e.ModInfo.DisplayName, desc, sections: null, links: null);
        }

        // ---- DLC tab: read-only informational list (name + state) ----

        private void BuildDlcTab(GraphBuilder b, DlcManagerVM vm, string k)
        {
            if (vm.InGame)
            {
                var list = vm.SwitchOnDlcsVM?.SelectionGroup?.EntitiesCollection;
                if (list != null)
                    foreach (var dlc in list)
                    {
                        var e = dlc;
                        b.AddItem(ControlId.Referenced(e, k + e.GetHashCode()), GraphNodes.Text(() =>
                            e.Title + ", " + Loc.T(e.DlcSwitchState.Value ? "value.on" : "value.off")
                            + (e.ItIsLateToSwitchDlcOn ? ", " + Loc.T("dlc.too_late") : "")));
                    }
            }
            else
            {
                var list = vm.DlcsVM?.SelectionGroup?.EntitiesCollection;
                if (list != null)
                    foreach (var dlc in list)
                    {
                        var e = dlc;
                        b.AddItem(ControlId.Referenced(e, k + e.GetHashCode()),
                            GraphNodes.Text(() => e.Title + ", " + DlcState(e)));
                    }
            }
        }

        private static string DlcState(DlcManagerDlcEntityVM e)
            => Loc.T(e.DlcIsInstalled.Value ? "dlc.installed"
                : e.DlcIsBoughtAndNotInstalled.Value ? "dlc.not_installed"
                : "dlc.available");
    }
}
