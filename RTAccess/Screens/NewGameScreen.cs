using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.NewGame;
using Kingmaker.Code.UI.MVVM.VM.NewGame.Difficulty;
using Kingmaker.Code.UI.MVVM.VM.NewGame.Story;
using Kingmaker.Stores.DlcInterfaces;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The New Game wizard (MainMenuVM.NewGameVM): Story → Difficulty, on the shared graph-native
    /// <see cref="WizardScreen"/> shell. Next/Back delegate to the VM's OnButtonNext/Back (which route
    /// through the selected menu entity's phase); the Next label reads "Begin" on the final (Difficulty)
    /// step. Advancing past Difficulty enters character generation (handled by <see cref="CharGenScreen"/>);
    /// backing past Story exits to the menu. Story is a campaign/DLC picker + live description; Difficulty
    /// reuses the settings-entity graph path (same VMs and node factories as the Settings screen) as a
    /// FLAT list — the phase label already carries the page's one header, so a group node to expand (or a
    /// redundant header line) is pure friction.
    /// </summary>
    public sealed class NewGameScreen : WizardScreen
    {
        public override string Key => "ctx.newgame";
        public override string ScreenName => Loc.T("screen.new_game");
        public override int Layer => 5; // above the main-menu sidebar (which stays resolved underneath)

        private static NewGameVM Vm()
        {
            var mm = Game.Instance?.RootUiContext?.MainMenuVM;
            if (mm == null || mm.NewGameVM == null) return null;
            // Once character generation opens, the wizard is done — hand off to CharGenScreen.
            if (mm.CharGenContextVM?.CharGenVM?.Value != null) return null;
            return mm.NewGameVM.Value;
        }

        protected override object WizardVm() => Vm();
        protected override object CurrentPhase() => Vm()?.SelectedMenuEntity.Value;
        protected override string PhaseLabel() => Vm()?.SelectedMenuEntity.Value?.Title;

        // The scenario entity we last ran SelectMe() for — a dedupe latch for a game-driving side
        // effect (see BuildStory), NOT view state: Build runs every render (immediate mode) and
        // SelectMe re-executes the game's ChangeStory command per call, so an unlatched call would
        // fire it every frame. Keyed by VM instance, so a rebuilt VM re-syncs.
        private object _dlcSynced;
        // The scenario/DLC VM we last drove the shared description panel for, so OnPhaseTick only
        // re-pushes when focus actually lands on a different scenario/DLC row.
        private object _lastDescribedFor;

        public override void OnPop() { base.OnPop(); _dlcSynced = null; _lastDescribedFor = null; }

        protected override void BuildContent(GraphBuilder b, string k)
        {
            var phase = Vm()?.SelectedMenuEntity.Value?.NewGamePhaseVM;
            if (phase is NewGamePhaseStoryVM story)
            {
                BuildStory(b, k, story);
            }
            else if (phase is NewGamePhaseDifficultyVM difficulty)
            {
                // Same settings-entity VMs as the Settings screen, through the same graph emitter —
                // flat: the game's single "Difficulty" header is skipped (the phase labels the page).
                SettingsEntityGraph.Emit(b, difficulty.SettingEntities, k, flat: true);
                _lastDescribedFor = null; // re-entering Story re-pushes the description on first landing
            }
            else
            {
                b.AddItem(ControlId.Structural(k + "unavailable"),
                    GraphNodes.Text(() => Loc.T("wizard.step_unavailable")));
                _lastDescribedFor = null;
            }
        }

        // The game's story description panel follows focus: focusing a DLC shows that DLC's blurb,
        // focusing the campaign shows the campaign's. Our description node reads story.Description live,
        // so we just drive it here (via the same HandleNewGameChangeDlc the game's view fires on focus).
        // Focus on anything else (Next, the description line itself) leaves it — the game's panel sticks
        // too. Focus is read off the graph by node REFERENCE (the campaign/DLC VM the node was built
        // from), the graph-native analog of the old proxy-type dispatch.
        protected override void OnPhaseTick()
        {
            if (!FocusMode.Active) return; // never drive game VM state while the mod doesn't own focus
            if (!(Vm()?.SelectedMenuEntity.Value?.NewGamePhaseVM is NewGamePhaseStoryVM story)) return;
            var anchor = DlcAnchor(story);
            if (anchor == null) return; // no DLC switches — the panel just shows the campaign

            var cur = Navigation.Active?.FocusedNodeReference;
            if (cur == null || ReferenceEquals(cur, _lastDescribedFor)) return;
            if (cur is NewGamePhaseStoryScenarioEntityIntegralDlcVM dlc)
                story.HandleNewGameChangeDlc(dlc.Campaign, dlc.BlueprintDlc);
            else if (cur is NewGamePhaseStoryScenarioEntityVM)
                story.HandleNewGameChangeDlc(anchor.Campaign, null); // campaign radio → campaign blurb
            else
                return; // other focus → leave the description on the last scenario/DLC
            _lastDescribedFor = cur;
        }

        // Any DLC vm of the selected (or first) campaign supplies the campaign for the description
        // handler; null when the campaign has no DLC switches (then nothing follows focus — old behavior).
        private static NewGamePhaseStoryScenarioEntityIntegralDlcVM DlcAnchor(NewGamePhaseStoryVM story)
        {
            var sel = SelectedOrFirst(story);
            var dlcs = sel?.IntegralDlcVms;
            return dlcs != null && dlcs.Count > 0 ? dlcs[0] : null;
        }

        private static NewGamePhaseStoryScenarioEntityVM SelectedOrFirst(NewGamePhaseStoryVM story)
        {
            NewGamePhaseStoryScenarioEntityVM first = null;
            foreach (var ent in story.SelectionGroup.EntitiesCollection)
            {
                if (first == null) first = ent;
                if (ent.IsSelected.Value) return ent;
            }
            return first;
        }

        protected override void OnBack() => Vm()?.OnButtonBack();
        protected override void OnNext() => Vm()?.OnButtonNext();

        // The final step (Difficulty) leads into character creation, so name its Next "Begin".
        protected override string NextLabel() => IsLastPhase() ? Loc.T("wizard.begin") : Loc.T("wizard.next");

        protected override bool NextEnabled()
        {
            // Only the Story phase gates Next (a selectable/available campaign must be chosen). The
            // Difficulty phase never sets IsNextButtonAvailable (inherits the false default), and the game
            // always lets you proceed from it — so it's unconditionally enabled.
            var phase = Vm()?.SelectedMenuEntity.Value?.NewGamePhaseVM;
            var story = phase as NewGamePhaseStoryVM;
            return story == null || story.IsNextButtonAvailable.Value;
        }

        private static bool IsLastPhase()
        {
            var vm = Vm();
            var ents = vm?.MenuSelectionGroup?.EntitiesCollection;
            return ents != null && ents.Count > 0
                && ReferenceEquals(vm.SelectedMenuEntity.Value, ents[ents.Count - 1]);
        }

        private void BuildStory(GraphBuilder b, string k, NewGamePhaseStoryVM story)
        {
            // Campaign choices (in the content stop, under the phase-label context → reads as
            // "<name>, radio button, selected, N of M").
            int i = 0;
            foreach (var e in story.SelectionGroup.EntitiesCollection)
            {
                var ent = e; // capture for the live closures
                b.AddItem(ControlId.Referenced(ent, k + "camp:" + i++),
                    CharGenNodes.SelectionItem(ent, () => ent.Title,
                        available: () => ent.IsStoryIsAvailable.Value));
            }

            // The selected campaign's integral-DLC on/off switches — the game draws these beneath the
            // story so you can enable/disable DLC content for this playthrough. Rendering only the
            // campaign radio (and never these) stranded a blind player on the base game with no way to
            // turn a DLC on. The switches both default (via SelectMe) and read/write their on/off through
            // Game.Instance.Player's additional-content set, so gate the whole block on a live Player.
            // It's invariant non-null at this main-menu screen (the game dereferences it unconditionally
            // here), so this only keeps us crash-safe — never render a switch we'd then NRE reading.
            var selected = SelectedOrFirst(story);
            if (selected != null && Game.Instance?.Player != null)
            {
                // Selecting a story defaults every owned+available DLC to ON — but that sync
                // (NewGamePhaseStoryScenarioEntityVM.UpdateDlcSelectionStatus) lives in the scenario
                // VIEW's OnChangeSelectedState, which our parallel UI doesn't drive; left un-run, the
                // player's DLC set is empty and every switch below (and the actual new game) reads OFF.
                // SelectMe runs that sync so the switches start where the sighted UI shows them.
                // Idempotent on the DLC set (only ADDS newly-available as on, prunes unavailable —
                // re-entry keeps whatever the player has since toggled off), but it also re-executes
                // the game's ChangeStory command, so it's latched to once per scenario instance.
                if (!ReferenceEquals(_dlcSynced, selected))
                {
                    selected.SelectMe();
                    _dlcSynced = selected;
                }

                // The DLC switches keep their own Tab-stop (the old page's Tab topology: campaigns,
                // additional content, description were sibling regions — never collapse them into one).
                var dlcs = selected.IntegralDlcVms;
                if (dlcs != null && dlcs.Count > 0)
                {
                    b.BeginStop("dlc").PushContext(Loc.T("story.additional_content"), Loc.T("role.list"));
                    for (int d = 0; d < dlcs.Count; d++)
                        b.AddItem(ControlId.Referenced(dlcs[d], k + "dlc:" + d), DlcToggle(dlcs[d]));
                    b.PopContext();
                }
            }

            // Shared description panel as its own Tab-stop: the currently-focused scenario/DLC's blurb,
            // kept in step by OnPhaseTick driving story.Description (campaign by default, a DLC while
            // its switch is focused). Skipped while empty (the old TextElement self-hid).
            if (!string.IsNullOrEmpty(story.Description?.Value))
                b.BeginStop("desc").AddItem(ControlId.Structural(k + "desc"),
                    GraphNodes.Text(() => story.Description?.Value ?? ""));
        }

        /// <summary>One integral-DLC on/off switch (the retired ProxyDlcToggle contract as a vtable):
        /// "&lt;DLC name&gt;, toggle, on/off[, disabled]" — with a status ("not owned", "downloading",
        /// "not installed", "coming soon") in place of on/off when the DLC can't be switched. Activating
        /// a switchable DLC flips its inclusion (<c>BlueprintDlc.SwitchDlcValue</c>) and re-announces the
        /// new value in place. The switch is live only for an owned, fully-installed DLC — matching the
        /// game's story view, which shows the on/off button only then (and offers purchase/install
        /// otherwise; those store/network flows stay out of scope — a not-owned DLC reads as disabled
        /// with its status so the player still learns it exists). The DLC's own description shows in the
        /// shared story panel while the switch is focused (driven by OnPhaseTick).</summary>
        private static NodeVtable DlcToggle(NewGamePhaseStoryScenarioEntityIntegralDlcVM vm)
        {
            var dlc = vm?.BlueprintDlc;
            Func<bool> switchable = () =>
            {
                if (dlc == null || !dlc.IsPurchased) return false;
                var state = dlc.GetDownloadState();
                return state != DownloadState.NotLoaded && state != DownloadState.Loading;
            };
            Func<string> value = () =>
            {
                if (dlc == null) return null;
                if (switchable())
                    return Loc.T(dlc.GetDlcSwitchOnOffState() ? "value.on" : "value.off");
                if (!dlc.IsPurchased)
                    return Loc.T(dlc.GetPurchaseState() == Kingmaker.DLC.BlueprintDlc.DlcPurchaseState.ComingSoon
                        ? "value.coming_soon" : "value.not_owned");
                // Owned but not ready: either still downloading or not installed at all.
                return Loc.T(dlc.GetDownloadState() == DownloadState.Loading
                    ? "value.downloading" : "value.not_installed");
            };
            bool canSwitch = switchable(); // fresh per render (immediate mode)
            return new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => vm?.Title ?? ""),
                    // LIVE: a download finishing (or a store refresh) under focus announces itself.
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(switchable),
                },
                SearchText = () => vm?.Title ?? "",
                // Switching flips the value in place — re-announce it synchronously (the
                // ReannounceOnActivate convention).
                StateText = canSwitch ? value : null,
                OnActivate = canSwitch
                    ? (Action)(() => dlc.SwitchDlcValue(!dlc.GetDlcSwitchOnOffState()))
                    : null,
                ActivateSound = canSwitch
                    ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick
                    : null,
            };
        }
    }
}
