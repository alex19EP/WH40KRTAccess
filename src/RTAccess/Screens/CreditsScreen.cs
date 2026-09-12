using System.Collections.Generic;
using Access.Core;          // TextUtil
using Kingmaker;
using Kingmaker.Blueprints.Credits;
using Kingmaker.Blueprints.Root.Strings;     // UIStrings (the window's own page / search / play labels)
using Kingmaker.Code.UI.MVVM.VM.Credits;
using Kingmaker.Code.UI.MVVM.View.Credits;   // CreditsPCView / CreditsBaseView (the live pager), PageGenerator (row tags)
using RTAccess.UI;
using Access.Core.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The credits (<c>CreditsVM</c>) — the main menu's Credits book AND the Lord-Captain's Infolog
    /// "Memorial Sanctuary" (the same window in <c>onlyBakers</c> mode: the backer roll, ~16,800 names).
    /// The sighted window is a self-turning illustrated book: a section selector down the side, ONE PAGE of
    /// the section at a time (the view's <see cref="PageGenerator"/> cuts each section into pages of ~34
    /// rows), previous/next-page buttons under a "3 / 490" counter, a play/pause toggle and a name search.
    /// Read with a screen reader it becomes three Tab-stops mirroring exactly that:
    /// <list type="bullet">
    /// <item>the SECTION list — each entry drives the game's own selection, which re-pages the book;</item>
    /// <item>the PAGE controls — the counter, previous / next page (the view's own <c>OnPrevPage</c> /
    /// <c>OnNextPage</c>, which also pause the roll, as the sighted buttons do), the auto-play toggle
    /// (<c>CreditsVM.TogglePause</c>), the search field (the window's own input, typed through
    /// <see cref="TextEntry"/>) and Find (<c>CreditsBaseView.OnFind</c> — the game's own search; its
    /// not-found warning is voiced by WarningReader);</item>
    /// <item>the CURRENT PAGE's rows, unwrapped from the very page string the view renders — a team
    /// heading, "person — role", a free-text paragraph, or a bare backer name.</item>
    /// </list>
    /// Only the page under the sighted reader's eyes is declared. The old screen flattened the WHOLE section
    /// into one node per person, which on the backer roll meant a 16,801-node graph rebuilt every frame (a
    /// stall) and a list no one could find a name in. Auto-play is paused on open: a page that turns itself
    /// every five seconds moves under a reader who reads at their own pace; the toggle hands it back. A Find
    /// lands the book on the page carrying the match (the view's own jump) and focus on that row.
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

        // The live pager. The pages, the current page and the search all live on the VIEW, not the VM
        // (CreditsBaseView.m_Pages / m_CurrentPage / m_SearchField): the VM only knows the groups and the
        // pause flag. One PC view instance serves both hosts; it is inactive while no credits are open.
        private static CreditsBaseView View()
        {
            var v = LiveView.Find<CreditsPCView>();
            return v != null && v.isActiveAndEnabled ? v : null;
        }

        public override bool IsActive() => Vm() != null;

        // Auto-play is paused once per window (a new VM per open); the toggle hands it back to the player.
        private static CreditsVM s_Paused;

        public override void OnPush() => s_Paused = null;

        public override IEnumerable<ElementAction> GetActions()
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
            if (!ReferenceEquals(s_Paused, vm)) { s_Paused = vm; vm.SetPauseState(true); }

            // The section selector. Selecting drives the game's own selection group (which re-pages the
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

            var selected = selectedIndex >= 0 && selectedIndex < vm.Groups.Count ? vm.Groups[selectedIndex] : null;
            if (selected == null) return;

            // The page controls. Keyed to the WINDOW, not the page: turning a page keeps focus on the
            // button while the counter's live label and the rows below re-key.
            const string pk = "credits:pager:";
            b.BeginStop("pager").PushContext(Loc.T("label.pages"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(pk + "counter"), GraphNodes.Text(PageCounter));
            b.AddItem(ControlId.Structural(pk + "prev"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.Credits.PreviousPage, "credits.prev_page"),
                () => View()?.OnPrevPage(),
                enabled: () => (View()?.m_CurrentPage ?? 0) > 0));
            b.AddItem(ControlId.Structural(pk + "next"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.Credits.NextPage, "credits.next_page"),
                () => View()?.OnNextPage(),
                enabled: () => { var v = View(); return v?.m_Pages != null && v.m_CurrentPage < v.m_Pages.Count - 1; }));
            b.AddItem(ControlId.Structural(pk + "autoplay"), GraphNodes.Toggle(
                () => GameText.Or(() => UIStrings.Instance.Credits.PlayRoles, "credits.autoplay"),
                () => !(Vm()?.Pause.Value ?? true),
                () => Vm()?.TogglePause()));
            b.AddItem(ControlId.Structural(pk + "search"), GraphNodes.Button(
                () =>
                {
                    var q = View()?.m_SearchField?.text;
                    return string.IsNullOrEmpty(q)
                        ? GameText.Or(() => UIStrings.Instance.CommonTexts.Search, "credits.search")
                        : Loc.T("credits.search_active", new { query = q });
                },
                BeginSearch));
            b.AddItem(ControlId.Structural(pk + "find"), GraphNodes.Button(
                () => Loc.T("credits.find"), Find,
                enabled: () => !string.IsNullOrWhiteSpace(View()?.m_SearchField?.text)));
            b.PopContext();

            // The current page's rows. Keyed by section + page, so a page turn re-keys the rows (focus on
            // a row slides to the new page's first row) while a same-page re-render leaves them put.
            var view = View();
            var pages = view?.m_Pages;
            int page = view?.m_CurrentPage ?? 0;
            b.BeginStop("content").PushContext(selected.HeaderText, Loc.T("role.list"));
            if (!string.IsNullOrWhiteSpace(selected.PageText))
                b.AddItem(ControlId.Structural("credits:pagetext:" + selected.name),
                    GraphNodes.Text(() => TextUtil.StripRichTextSpaced(selected.PageText)));
            if (pages == null || page < 0 || page >= pages.Count)
                b.AddItem(ControlId.Structural("credits:page:none"), GraphNodes.Text(() => Loc.T("credits.no_page")));
            else
            {
                var rows = PageRows(selected, pages[page]);
                for (int r = 0; r < rows.Count; r++)
                {
                    var text = rows[r]; // capture
                    b.AddItem(ControlId.Structural(RowKey(selected, page, r)), GraphNodes.Text(() => text));
                }
            }
            b.PopContext();
        }

        private static string PageCounter()
        {
            var v = View();
            int count = v?.m_Pages?.Count ?? 0;
            return count == 0 ? Loc.T("credits.no_page")
                : Loc.T("credits.page", new { page = v.m_CurrentPage + 1, count });
        }

        private static string RowKey(BlueprintCreditsGroup group, int page, int row)
            => "credits:page:" + group.name + ":" + page + ":" + row;

        // Focus the game's own search field and let TextEntry own the keyboard (the inventory-search recipe).
        private static void BeginSearch()
        {
            var field = View()?.m_SearchField;
            if (field != null) TextEntry.Begin(field, Loc.T("credits.search"));
            else Tts.Speak(Loc.T("text.unavailable"), interrupt: true);
        }

        // The window's own Find: SearchPerson pauses the roll, (re)builds the result list for a new term,
        // turns the book to the result's page and pings its row, then advances its cursor to the next
        // result (wrapping) — so a repeated Find walks the matches. Not found → the game's own warning
        // (WarningReader speaks it). The result just used is therefore the one BEFORE the view's cursor;
        // its page/row give the row node to land focus on — a request the navigator applies once the view's
        // page-turn coroutine has run and the row is in the render.
        private static void Find()
        {
            var v = View();
            if (v == null) return;
            v.OnFind();
            if (v.ResultSearch.Count == 0) return;
            var used = v.m_ResultSearchView?.Previous ?? v.ResultSearch.Last;
            if (used == null || used.Value.Group == null) return;
            Navigation.FocusNode(ControlId.Structural(RowKey(used.Value.Group, used.Value.Page, used.Value.Row)), announce: true);
        }

        // One spoken line per row of the page string, exactly the rows the view appends (it stops at the
        // first empty line — mirror that, so row indexes match the view's search pings).
        private static List<string> PageRows(BlueprintCreditsGroup group, string page)
        {
            var rows = new List<string>();
            if (string.IsNullOrEmpty(page)) return rows;
            using (var reader = new System.IO.StringReader(page))
            {
                string line;
                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    rows.Add(Unwrap(line, group));
            }
            return rows;
        }

        // A generated row is one of: <header>team</header>, <person>name</person><role>key</role>,
        // <text>paragraph</text>, or a bare <person>name</person> (bakers). The role tag carries the KEY
        // (possibly several, pipe-separated); the group's roles blueprint resolves it to display names,
        // newline-joined — read comma-joined.
        private static string Unwrap(string line, BlueprintCreditsGroup group)
        {
            string header = PageGenerator.ReadHeader(line);
            if (!string.IsNullOrWhiteSpace(header)) return TextUtil.StripRichTextSpaced(header);

            string person = PageGenerator.ReadPerson(line);
            if (!string.IsNullOrWhiteSpace(person))
            {
                string role = null;
                try { role = group?.RolesData?.GetRole(PageGenerator.ReadRole(line)); } catch { }
                role = string.IsNullOrWhiteSpace(role) ? null : role.Replace("\n", ", ").Trim();
                person = person.Trim();
                return role == null ? person : person + " — " + role;
            }

            string text = PageGenerator.ReadText(line);
            if (!string.IsNullOrWhiteSpace(text)) return TextUtil.StripRichTextSpaced(text);

            return TextUtil.StripRichTextSpaced(line);
        }
    }
}
