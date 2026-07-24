using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Code.UI.MVVM.VM.DlcManager;
using Kingmaker.Code.UI.MVVM.VM.DlcManager.Dlcs;
using Kingmaker.Code.UI.MVVM.VM.DlcManager.Mods;
using Kingmaker.Code.UI.MVVM.VM.DlcManager.SwitchOnDlcs;
using Kingmaker.DLC;
using Kingmaker.Stores;
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
    /// Tab-stops: the tab strip (DLC / Mods — the game's own menu entities), the selected tab's content
    /// (keys carry the tab, so switching re-keys content only), the Mods tab's "discover more mods" links,
    /// and the window's bottom Apply / Default buttons (present exactly when the game shows them — the Mods
    /// and switch-on-DLC tabs). Each tab mirrors its card:
    /// <list type="bullet">
    /// <item><b>Mods</b> — name + version + on/off + the update-required / restart-required warning marks;
    /// Enter opens a small menu (Settings / Enable-Disable / Description) so the accessibility mod can't be
    /// disabled by a stray keypress, Space reads the info panel (name, author, version, description).</item>
    /// <item><b>DLC</b> (main menu) — title + type + story campaign + purchase/download state + the "new!"
    /// mark; Enter selects it (as clicking the card does) and offers the bottom-block buttons the sighted
    /// panel shows for it (Purchase / Install / Delete), Space reads the description.</item>
    /// <item><b>DLC</b> (in game) — the switch-on-in-this-save toggles, driven through the game's own
    /// ChangeValue so a refused switch explains itself in the game's own words; the lock the card draws on
    /// an already-on / too-late / no-save-allowed entry reads as "disabled", with the reason on Space.</item>
    /// </list>
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
        public override IEnumerable<ElementAction> GetActions()
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
            b.BeginStop("content").PushContext(ContentHeader(vm, mods), Loc.T("role.list"));
            if (mods) BuildModsTab(b, vm, "dlcmgr:mods:");
            else if (vm.InGame) BuildSwitchOnDlcsTab(b, vm, "dlcmgr:switch:");
            else BuildDlcTab(b, vm, "dlcmgr:dlc:");
            b.PopContext();

            // The Mods tab's bottom link block ("Discover more mods": Nexus Mods, and Steam Workshop where
            // the game shows it).
            if (mods) BuildModLinks(b, vm);

            // The window's bottom buttons — the game shows them for the Mods and switch-on-DLC tabs only.
            if (mods || vm.IsSwitchOnDlcsWindow.Value) BuildBottomButtons(b, vm);
        }

        // The header the game prints over the tab's content ("Installed mods" / "Installed DLCs" / "DLC").
        private static string ContentHeader(DlcManagerVM vm, bool mods)
        {
            if (mods) return GameText.Or(() => UIStrings.Instance.DlcManager.InstalledMods, "mods.installed");
            if (vm.InGame) return GameText.Or(() => UIStrings.Instance.DlcManager.InstalledDlcs, "dlc.installed_dlcs");
            return GameText.Or(() => UIStrings.Instance.DlcManager.DlcManagerLabel, "screen.mods");
        }

        private static NodeVtable TabNode(DlcManagerVM vm, DlcManagerMenuEntityVM e)
        {
            Func<string> label = () => e.Title;
            return new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new List<NodeAnnouncement>
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
            Func<string> value = () => Loc.T(e.ModSwitchState.Value ? "mods.state.enabled" : "mods.state.disabled");
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => (e.ModInfo.DisplayName + " " + e.ModInfo.Version).Trim()),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                    // The two warning marks the card draws beside the name. Kindless custom parts = always
                    // spoken (never suppressed by the tooltip/value announcement toggles). The greyed-out
                    // toggle (saving not allowed) is NOT announced here — the row's Enter still opens the
                    // menu; the lock belongs to the switch, and is stated there.
                    new NodeAnnouncement(() => e.WarningUpdateMod.Value
                        ? GameText.Or(() => UIStrings.Instance.DlcManager.NeedToUpdateThisMod, "mods.update_required")
                        : null, live: true),
                    new NodeAnnouncement(() => e.WarningReloadGame.Value
                        ? GameText.Or(() => UIStrings.Instance.DlcManager.ModChangedNeedToReloadGame, "mods.reload_pending")
                        : null, live: true),
                },
                SearchText = () => e.ModInfo.DisplayName,
                OnActivate = () => OpenModMenu(e),   // Enter: the per-mod action menu
                OnTooltip = () => ReadDescription(e), // Space: read the info panel
            };
        }

        // Enter on a mod row opens a small menu — Settings (when available) / Enable-Disable / Description —
        // so a stray Enter never toggles (disabling the accessibility mod itself).
        private void OpenModMenu(DlcManagerModEntityVM e)
        {
            var rows = new List<ChoiceSubmenuScreen.Row>();
            if (CanSettings(e))
                rows.Add(ChoiceSubmenuScreen.Row.Action(() => Loc.T("mods.settings"), () => OpenModSettings(e)));
            // Greyed exactly as the game greys the toggle, with its own reason spelled out above it.
            if (!e.IsSaveAllowed)
                rows.Add(ChoiceSubmenuScreen.Row.Header(
                    () => GameText.Or(() => UIStrings.Instance.DlcManager.CannotChangeModSwitchState, "mods.cannot_change")));
            rows.Add(ChoiceSubmenuScreen.Row.Action(
                () => Loc.T(e.ModSwitchState.Value ? "mods.disable" : "mods.enable"), () => e.ChangeValue(),
                () => e.IsSaveAllowed));
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

        // Space on a mod = the info panel the game fills on hover/focus: its title line is
        // "name / author - version" (ShowDescription), then the description body.
        private static void ReadDescription(DlcManagerModEntityVM e)
        {
            var info = e.ModInfo;
            var byline = (info.Author + " - " + info.Version).Trim(' ', '-');
            var desc = info.Description;
            if (string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(byline))
            {
                Tts.Speak(Loc.T("mods.no_description"), interrupt: true);
                return;
            }
            var body = string.IsNullOrWhiteSpace(desc) ? byline
                : string.IsNullOrWhiteSpace(byline) ? desc
                : byline + Environment.NewLine + desc;
            TooltipChooser.Open(info.DisplayName, body, sections: null, links: null);
        }

        // The bottom link block of the Mods tab: the game's own "discover more mods" buttons.
        private void BuildModLinks(GraphBuilder b, DlcManagerVM vm)
        {
            var mods = vm.ModsVM;
            if (mods == null) return;
            b.BeginStop("modlinks").PushContext(
                GameText.Or(() => UIStrings.Instance.DlcManager.DiscoverMoreMods, "mods.discover"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural("dlcmgr:mods:nexus"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.DlcManager.NexusMods, "mods.nexus"),
                () => mods.OpenNexusMods()));
            if (mods.IsSteam.Value)
                b.AddItem(ControlId.Structural("dlcmgr:mods:workshop"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.DlcManager.SteamWorkshop, "mods.workshop"),
                    () => mods.OpenSteamWorkshop()));
            b.PopContext();
        }

        // The window's Apply / Default buttons: live only while a change is pending, exactly as the game
        // gates their interactable state on NeedReload / NeedResave.
        private void BuildBottomButtons(GraphBuilder b, DlcManagerVM vm)
        {
            Func<bool> pending = () => (vm.ModsVM != null && vm.ModsVM.NeedReload.Value)
                || (vm.InGame && vm.SwitchOnDlcsVM != null && vm.SwitchOnDlcsVM.NeedResave.Value);
            b.BeginStop("bottom");
            b.AddItem(ControlId.Structural("dlcmgr:apply"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.SettingsUI.Apply, "action.apply"),
                () => vm.CheckToReloadGame(null), pending));
            b.AddItem(ControlId.Structural("dlcmgr:default"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.SettingsUI.Default, "action.default"),
                () => vm.RestoreAllToPreviousState(), pending));
        }

        // ---- DLC tab (main menu): the store list + the selected entry's bottom-block actions ----

        private void BuildDlcTab(GraphBuilder b, DlcManagerVM vm, string k)
        {
            var tab = vm.DlcsVM;
            var list = tab?.SelectionGroup?.EntitiesCollection;
            if (list == null || !list.Any())
            {
                b.AddItem(ControlId.Structural(k + "empty"),
                    GraphNodes.Text(() => GameText.Or(() => UIStrings.Instance.DlcManager.YouDontHaveAnyInstalledDlcs, "dlc.none")));
                return;
            }
            foreach (var dlc in list)
            {
                var e = dlc; // capture
                b.AddItem(ControlId.Referenced(e, k + DlcKey(e.BlueprintDlc)), DlcRow(tab, e));
            }
        }

        private NodeVtable DlcRow(DlcManagerTabDlcsVM tab, DlcManagerDlcEntityVM e)
        {
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => e.Title),
                    // The card's second label: the DLC type, plus the "story campaign is X" line it prints
                    // under an additional-content DLC.
                    new NodeAnnouncement(() => UIStrings.Instance.DlcManager.GetDlcTypeLabel(e.DlcType)),
                    new NodeAnnouncement(() => StoryCampaignLine(e.BlueprintDlc)),
                    new NodeAnnouncement(() => DlcStateLine(e), live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.SelectedPart(() => ReferenceEquals(tab.SelectedEntity.Value, e)),
                    // The "New!" flash the card shows until the entry has been opened once.
                    new NodeAnnouncement(() => e.SawThisDlc.Value ? null
                        : GameText.Or(() => UIStrings.Instance.QuestNotificationTexts.New, "state.new"), live: true),
                },
                SearchText = () => e.Title,
                OnActivate = () => { tab.SelectedEntity.Value = e; OpenDlcMenu(tab, e); },
                OnTooltip = () => ReadDlcDescription(e),
            };
        }

        // What the card / bottom block says about this entry's availability: downloading and
        // bought-but-not-installed replace the purchase state, exactly as the views swap the labels.
        private static string DlcStateLine(DlcManagerDlcEntityVM e)
        {
            var strings = UIStrings.Instance.DlcManager;
            if (e.DownloadingInProgress.Value)
                return GameText.Or(() => strings.DlcDownloading, "value.downloading");
            if (e.DlcIsBoughtAndNotInstalled.Value)
                return GameText.Or(() => strings.DlcBoughtAndNotInstalled, "value.not_installed");
            return strings.GetDlcPurchaseStateLabel(e.BlueprintDlc.GetPurchaseState());
        }

        // "*Story company is <campaign>" — from the DLC's own campaign reward, or its parent DLC.
        private static string StoryCampaignLine(BlueprintDlc dlc)
        {
            if (dlc == null || dlc.DlcType != DlcTypeEnum.AdditionalContentDlc) return null;
            string name = dlc.ParentDlc != null
                ? dlc.ParentDlc.GetDlcName()
                : (dlc.Rewards?.OfType<BlueprintDlcRewardCampaignAdditionalContent>()
                      .FirstOrDefault()?.Campaign?.Title);
            if (string.IsNullOrWhiteSpace(name)) return null;
            return GameText.Or(() => UIStrings.Instance.DlcManager.StoryCompanyIs, "dlc.story_campaign") + " " + name;
        }

        // Enter on a DLC selects it (what clicking the card does — it also clears the "new" mark) and offers
        // the buttons the sighted bottom block shows for that state. All three act on the tab VM's current
        // DLC, which the selection above has just set.
        private void OpenDlcMenu(DlcManagerTabDlcsVM tab, DlcManagerDlcEntityVM e)
        {
            var strings = UIStrings.Instance.DlcManager;
            var rows = new List<ChoiceSubmenuScreen.Row>();
            bool busy = e.DownloadingInProgress.Value || e.DlcIsBoughtAndNotInstalled.Value;
            var state = e.BlueprintDlc.GetPurchaseState();
            if (!busy && state == BlueprintDlc.DlcPurchaseState.AvailableToPurchase)
                rows.Add(ChoiceSubmenuScreen.Row.Action(
                    () => GameText.Or(() => strings.Purchase, "dlc.purchase"), () => tab.ShowInStore()));
            if (e.DlcIsBoughtAndNotInstalled.Value)
                rows.Add(ChoiceSubmenuScreen.Row.Action(
                    () => GameText.Or(() => strings.Install, "dlc.install"), () => tab.InstallDlc()));
            if (e.IsDlcCanBeDeleted.Value)
                rows.Add(ChoiceSubmenuScreen.Row.Action(
                    () => GameText.Or(() => strings.DeleteDlc, "action.delete"), () => tab.DeleteDlc()));
            rows.Add(ChoiceSubmenuScreen.Row.Action(() => Loc.T("mods.description"), () => ReadDlcDescription(e)));
            ChoiceSubmenuScreen.OpenRows(e.Title, rows);
        }

        private static void ReadDlcDescription(DlcManagerDlcEntityVM e)
            => OpenDlcText(e.Title, e.BlueprintDlc?.GetDescription(), null);

        // ---- DLC tab (in game): switch a DLC on in the current save ----

        private void BuildSwitchOnDlcsTab(GraphBuilder b, DlcManagerVM vm, string k)
        {
            var tab = vm.SwitchOnDlcsVM;
            var list = tab?.SelectionGroup?.EntitiesCollection;
            if (list == null || !tab.HaveDlcs)
            {
                b.AddItem(ControlId.Structural(k + "empty"),
                    GraphNodes.Text(() => GameText.Or(() => UIStrings.Instance.DlcManager.YouDontHaveAnyInstalledDlcs, "dlc.none")));
                return;
            }
            foreach (var dlc in list)
            {
                var e = dlc; // capture
                b.AddItem(ControlId.Referenced(e, k + DlcKey(e.BlueprintDlc)), SwitchOnDlcRow(e));
            }
        }

        private NodeVtable SwitchOnDlcRow(DlcManagerSwitchOnDlcEntityVM e)
        {
            Func<string> value = () => Loc.T(e.DlcSwitchState.Value ? "value.on" : "value.off");
            // The card draws a locked toggle when the DLC is already on, when it is too late to switch it
            // on, or while saving isn't allowed — but the button still reacts, answering with the game's
            // own warning. So: announce the lock, keep Enter live, and let the LIVE value part speak the
            // settled truth (a refused switch changes nothing and stays silent).
            Func<bool> unlocked = () => !e.GetActualDlcState() && e.IsSaveAllowed && !e.ItIsLateToSwitchDlcOn;
            return new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => e.Title),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(unlocked),
                    new NodeAnnouncement(() => e.WarningResaveGame.Value ? Loc.T("dlc.resave_pending") : null, live: true),
                },
                SearchText = () => e.Title,
                OnActivate = () => e.ChangeValue(),
                OnTooltip = () => OpenDlcText(e.Title, e.BlueprintDlc?.GetDescription(), LockReason(e)),
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        // Why this entry's toggle is locked — the very message the game's own ChangeValue would raise.
        private static string LockReason(DlcManagerSwitchOnDlcEntityVM e)
        {
            var strings = UIStrings.Instance.DlcManager;
            if (e.GetActualDlcState()) return GameText.Or(() => strings.CannotChangeDlcSwitchState, "dlc.cannot_change");
            if (e.ItIsLateToSwitchDlcOn)
                return string.IsNullOrWhiteSpace(e.ToLateReason) ? Loc.T("dlc.too_late") : e.ToLateReason;
            if (!e.IsSaveAllowed)
                return GameText.Or(() => strings.CannotChangeDlcSwitchStateRightNowBecauseSaveNotAllowed, "dlc.cannot_change");
            return null;
        }

        // ---- shared ----

        // Space on a DLC row: its store description, plus (when locked) the reason it can't be switched on.
        private static void OpenDlcText(string title, string description, string extra)
        {
            var body = string.IsNullOrWhiteSpace(description) ? null : TextUtil.StripRichTextSpaced(description);
            if (!string.IsNullOrWhiteSpace(extra))
                body = string.IsNullOrWhiteSpace(body) ? extra : body + Environment.NewLine + extra;
            if (string.IsNullOrWhiteSpace(body)) { Tts.Speak(Loc.T("mods.no_description"), interrupt: true); return; }
            TooltipChooser.Open(title, body, sections: null, links: null);
        }

        // A stable per-DLC key: the blueprint asset id (the entity VMs are rebuilt on every store refresh).
        private static string DlcKey(BlueprintDlc dlc)
            => dlc != null ? dlc.Id : "?";
    }
}
