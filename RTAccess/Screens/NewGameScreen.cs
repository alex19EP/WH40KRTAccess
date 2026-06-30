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

        protected override void BuildContent(Container content)
        {
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

        private static void BuildStory(Container content, NewGamePhaseStoryVM story)
        {
            // Campaign choices (unlabeled list → reads as "<name>, radio button, selected, N of M").
            var campaigns = new ListContainer();
            foreach (var e in story.SelectionGroup.EntitiesCollection)
            {
                var ent = e; // capture for the live closures
                campaigns.Add(new ProxySelectionItem(ent, () => ent.Title,
                    available: () => ent.IsStoryIsAvailable.Value));
            }
            content.Add(campaigns);

            // Live description of the currently-selected campaign (updates as you pick).
            content.Add(new TextElement(() => story.Description != null ? story.Description.Value : ""));
        }
    }
}
