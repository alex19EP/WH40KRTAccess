using Kingmaker;
using Kingmaker.Blueprints.Credits;
using Kingmaker.Code.UI.MVVM.VM.Credits;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The credits (<c>MainMenuVM.CreditsVM</c>). The sighted window is a self-scrolling illustrated book
    /// with a section selector down the side; read with a screen reader that becomes two Tab-stops: the
    /// SECTION list (each entry drives the game's own selection, which is what scrolls the book) and the
    /// selected section's CONTENT, flattened into one navigable line per row exactly as the page generator
    /// lays them out — a team heading, then "person — role" for each credited person (a person's roles are
    /// newline-joined by the game, so they read comma-joined here), plus any free-text rows.
    ///
    /// Bakers sections carry no teams, just names — the game prints them as a plain two-column list, so
    /// they read as a plain list too. Auto-scroll (and its pause button) has no meaning here: the reader
    /// moves at the player's pace, so it is not mirrored.
    /// </summary>
    public sealed class CreditsScreen : Screen
    {
        public CreditsScreen() { Wrap = true; }

        public override string Key => "overlay.credits";
        public override string ScreenName => Loc.T("screen.credits");
        public override int Layer => 26; // over the main menu; below the message modal (30)
        public override bool Exclusive => true;

        // The main menu's Credits entry, and the in-game titles (the backers roll the game plays at the end
        // of the campaign) — the latter hangs off whichever static part is live, like every dual-context
        // window.
        private static CreditsVM Vm()
            => Game.Instance?.RootUiContext?.MainMenuVM?.CreditsVM?.Value
               ?? UiContexts.FromLiveStaticPart(s => s.CreditsVM?.Value, s => s.CreditsVM?.Value);

        public override bool IsActive() => Vm() != null;

        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                    _ => vm.CloseCredits());
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null || vm.Groups == null) return;

            // The section selector. Selecting drives the game's own selection group (which scrolls the
            // book underneath), and the graph starts on whichever section is selected.
            b.BeginStop("sections").PushContext(Loc.T("credits.sections"), Loc.T("role.list"));
            int selectedIndex = vm.SelectedMenuIndex;
            for (int i = 0; i < vm.Groups.Count; i++)
            {
                var group = vm.Groups[i];
                if (group == null) continue;
                int idx = i;
                var id = ControlId.Referenced(group, "credits:group:" + i);
                b.AddItem(id, GraphNodes.ChoiceOption(
                    () => group.HeaderText,
                    () => idx == Vm()?.SelectedMenuIndex,
                    () => Vm()?.SetSelectedGroup(group)));
                if (idx == selectedIndex) b.SetStart(id);
            }
            b.PopContext();

            // The selected section's rows. The key carries the section, so switching sections re-keys the
            // content only (the selector keeps its focus).
            var selected = selectedIndex >= 0 && selectedIndex < vm.Groups.Count ? vm.Groups[selectedIndex] : null;
            if (selected == null) return;
            b.BeginStop("content").PushContext(selected.HeaderText, Loc.T("role.list"));
            string k = "credits:page:" + selected.name + ":";
            int row = 0;

            if (!string.IsNullOrWhiteSpace(selected.PageText))
                b.AddItem(ControlId.Structural(k + "pagetext"),
                    GraphNodes.Text(() => TextUtil.StripRichTextSpaced(selected.PageText)));

            foreach (var line in Rows(selected))
            {
                var l = line; // capture
                b.AddItem(ControlId.Structural(k + row++), GraphNodes.Text(() => l));
            }
            b.PopContext();
        }

        // One line per printed row, in the game's own order: for a section with teams, each team heading
        // followed by its people; for a bakers section (no teams), the names as listed.
        private static System.Collections.Generic.IEnumerable<string> Rows(BlueprintCreditsGroup group)
        {
            var people = group.Persones;
            if (people == null) yield break;

            var teams = group.TeamsData;
            if (teams != null && group.OrderTeams != null && group.OrderTeams.Count > 0)
            {
                foreach (var order in group.OrderTeams)
                {
                    var team = teams.Teams?.Find(t => t != null && SameKey(t.KeyTeam, order));
                    if (team == null) continue;
                    var members = people.FindAll(p => p != null && SameKey(p.KeyTeam, team.KeyTeam));
                    if (members.Count == 0) continue;
                    string heading = team.NameTeam;
                    if (!string.IsNullOrWhiteSpace(heading)) yield return heading;
                    foreach (var line in People(group, members)) yield return line;
                }
                yield break;
            }

            foreach (var line in People(group, people)) yield return line;
        }

        private static System.Collections.Generic.IEnumerable<string> People(
            BlueprintCreditsGroup group, System.Collections.Generic.List<CreditPerson> people)
        {
            var roles = group.RolesData;
            foreach (var person in people)
            {
                if (person == null) continue;
                // A text row is a free paragraph the book prints in place of a name.
                string text = person.Text?.Text;
                if (!string.IsNullOrWhiteSpace(text)) { yield return TextUtil.StripRichTextSpaced(text); continue; }

                string name = person.Name?.Replace("\r", "").Trim();
                if (string.IsNullOrEmpty(name)) continue;
                string role = roles != null && !string.IsNullOrEmpty(person.KeyRole)
                    ? roles.GetRole(person.KeyRole)?.Replace("\n", ", ")
                    : null;
                yield return string.IsNullOrWhiteSpace(role) ? name : name + " — " + role;
            }
        }

        // The game matches team/role keys case- and space-insensitively (PageGenerator.DeleteAllSpaces).
        private static bool SameKey(string a, string b)
            => string.Equals(Strip(a), Strip(b), System.StringComparison.OrdinalIgnoreCase);

        private static string Strip(string s) => s == null ? "" : s.Replace(" ", "");
    }
}
