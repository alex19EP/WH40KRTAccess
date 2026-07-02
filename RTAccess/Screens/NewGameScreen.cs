using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.NewGame;
using Kingmaker.Code.UI.MVVM.VM.NewGame.Difficulty;
using Kingmaker.Code.UI.MVVM.VM.NewGame.Menu;
using Kingmaker.Code.UI.MVVM.VM.NewGame.Story;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The New Game wizard (MainMenuVM.NewGameVM): Story → Difficulty, on the shared
    /// <see cref="WizardScreen"/> shell. Next/Back delegate to the VM's OnButtonNext/Back (which route
    /// through the selected menu entity's phase); the Next label reads "Begin" on the final (Difficulty)
    /// step. Advancing past Difficulty enters character generation (handled by <see cref="CharGenScreen"/>);
    /// backing past Story exits to the menu. Story is a campaign/DLC picker + live description; Difficulty
    /// reuses the settings-entity treeview (same VMs as the Settings screen).
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

        // A DLC vm from the current Story step (null off the Story step or when the campaign has no DLCs),
        // used to drive the shared description panel from focus; and the element we last drove it for, so
        // OnPhaseTick only re-pushes when focus actually lands on a different scenario/DLC row.
        private NewGamePhaseStoryScenarioEntityIntegralDlcVM _dlcAnchor;
        private UIElement _lastDescribedFor;

        protected override void BuildContent(Container content)
        {
            _dlcAnchor = null; _lastDescribedFor = null; // recomputed by BuildStory; stays null off the Story step
            var phase = Vm()?.SelectedMenuEntity.Value?.NewGamePhaseVM;
            if (phase is NewGamePhaseStoryVM story)
                BuildStory(content, story);
            else if (phase is NewGamePhaseDifficultyVM difficulty)
                // Same settings-entity VMs as the Settings screen — build them as a treeview (collapsible
                // header groups, one Tab-stop each, arrow within) to match it, not a flat wall of stops.
                SettingsEntityBuilder.BuildInto(content, difficulty.SettingEntities, tree: true);
            else
                content.Add(new TextElement(() => Loc.T("wizard.step_unavailable")));
        }

        // The game's story description panel follows focus: focusing a DLC shows that DLC's blurb, focusing
        // the campaign shows the campaign's. Our shared description TextElement reads story.Description live,
        // so we just drive it here (via the same HandleNewGameChangeDlc the game's view fires on focus).
        // Focus on anything else (Next, the description line itself) leaves it — the game's panel sticks too.
        protected override void OnPhaseTick()
        {
            if (_dlcAnchor == null) return; // not the Story step, or the campaign has no DLC switches
            if (!(Vm()?.SelectedMenuEntity.Value?.NewGamePhaseVM is NewGamePhaseStoryVM story)) return;

            var cur = Navigation.Current;
            if (ReferenceEquals(cur, _lastDescribedFor)) return;
            if (cur is ProxyDlcToggle dlc)
                story.HandleNewGameChangeDlc(dlc.DlcVm.Campaign, dlc.DlcVm.BlueprintDlc);
            else if (cur is ProxySelectionItem)
                story.HandleNewGameChangeDlc(_dlcAnchor.Campaign, null); // campaign radio → campaign blurb
            else
                return; // other focus → leave the description on the last scenario/DLC
            _lastDescribedFor = cur;
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

        private void BuildStory(Container content, NewGamePhaseStoryVM story)
        {
            // Campaign choices (unlabeled list → reads as "<name>, radio button, selected, N of M").
            var campaigns = new ListContainer();
            NewGamePhaseStoryScenarioEntityVM selected = null, first = null;
            foreach (var e in story.SelectionGroup.EntitiesCollection)
            {
                var ent = e; // capture for the live closures
                campaigns.Add(new ProxySelectionItem(ent, () => ent.Title,
                    available: () => ent.IsStoryIsAvailable.Value));
                if (first == null) first = ent;
                if (ent.IsSelected.Value) selected = ent;
            }
            content.Add(campaigns);

            // The selected campaign's integral-DLC on/off switches — the game draws these beneath the
            // story so you can enable/disable DLC content for this playthrough. Rendering only the
            // campaign radio (and never these) stranded a blind player on the base game with no way to
            // turn a DLC on. RT ships a single story campaign, so this tracks it; selecting a different
            // campaign doesn't rebuild the page, but the picker never offers more than one to choose.
            // The switches both default (via SelectMe) and read/write their on/off through
            // Game.Instance.Player's additional-content set, so gate the whole block on a live Player.
            // It's invariant non-null at this main-menu screen (the game dereferences it unconditionally
            // here), so this only keeps us crash-safe — never render a switch we'd then NRE reading on focus.
            selected = selected ?? first;
            if (selected != null && Game.Instance?.Player != null)
            {
                // Selecting a story defaults every owned+available DLC to ON — but that sync
                // (NewGamePhaseStoryScenarioEntityVM.UpdateDlcSelectionStatus) lives in the scenario
                // VIEW's OnChangeSelectedState, which our parallel UI doesn't drive; left un-run, the
                // player's DLC set is empty and every switch below (and the actual new game) reads OFF.
                // SelectMe runs that sync so the switches start where the sighted UI shows them.
                // Idempotent: it only ADDS newly-available DLCs (as on) and prunes unavailable ones —
                // re-entry keeps whatever the player has since toggled off.
                selected.SelectMe();

                if (selected.IntegralDlcVms != null && selected.IntegralDlcVms.Count > 0)
                {
                    var dlc = new ListContainer(Loc.T("story.additional_content"));
                    foreach (var d in selected.IntegralDlcVms)
                        dlc.Add(new ProxyDlcToggle(d));
                    content.Add(dlc);
                    // Arm OnPhaseTick to follow focus onto these switches (any DLC vm supplies the campaign).
                    _dlcAnchor = selected.IntegralDlcVms[0];
                }
            }

            // Shared description panel: the currently-focused scenario/DLC's blurb, kept in step by
            // OnPhaseTick driving story.Description (campaign by default, a DLC while its switch is focused).
            content.Add(new TextElement(() => story.Description != null ? story.Description.Value : ""));
        }
    }
}
