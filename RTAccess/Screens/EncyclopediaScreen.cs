using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints;                                              // BlueprintScriptableObject
using Kingmaker.Blueprints.Encyclopedia;                                 // IPage
using Kingmaker.Blueprints.Root.Strings;                                 // UIStrings (planet/astropath labels — passed through)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                          // ServiceWindowsVM, ServiceWindowsType
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Encyclopedia;             // EncyclopediaVM, EncyclopediaNavigationElementVM, EncyclopediaPageVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Encyclopedia.Blocks;      // block VMs
using Kingmaker.PubSubSystem;                                            // IEncyclopediaHandler
using Kingmaker.PubSubSystem.Core;                                       // EventBus
using RTAccess.Accessibility;                                            // GlossaryLinks
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The Encyclopedia service window (<see cref="EncyclopediaVM"/>), graph-native. The game has no
    /// search; its navigation is a fully-expandable hierarchy (chapters → pages → subpages), so we mirror
    /// that as nested GROUPS (children materialize lazily — a collapsed chapter's child VMs are never
    /// built, gated on <see cref="GraphBuilder.IsExpanded"/>) plus the current page below — title, its
    /// typed content blocks, the page-footer glossary blurb, and links to its child topics. Everything is
    /// keyed per page, so navigating re-keys the page stop while the tree keeps its expansion and position.
    /// Enter on any node loads its page and lands focus on the page top (announced by the differ).
    /// Navigation drives the game's own path (<see cref="EncyclopediaNavigationElementVM.SelectPage"/> /
    /// the <see cref="IEncyclopediaHandler"/> EventBus) so the on-screen visuals + viewed-state stay in
    /// sync. We keep our own history so Escape goes back from a drilled page (from the tree — a
    /// jump-anywhere navigator — it closes).
    ///
    /// Ported from the WrathAccess EncyclopediaScreen recipe onto RT's richer VM/block set. RT differs:
    /// the nav-element has no IsCanCollapse (we use <c>IPage.IsChilds()</c>); the title accessor is
    /// <c>GetTitle()</c>; navigation is single-arg (no scrollToCenter); and RT adds planet / astropath /
    /// book-event / glossary-entry / class-progression blocks + a per-page GlossaryText, none of which
    /// WOTR handled. Bestiary unit blocks render through <see cref="RTAccess.Accessibility.BestiaryReader"/>
    /// (knowledge-gated). Read-only apart from navigation; ScreenName is null — ServiceWindowAnnounce
    /// already speaks "Encyclopedia". Layer 10 (service window). See docs/plans/mirrored-surfacing-engelbart.md.
    /// </summary>
    public sealed class EncyclopediaScreen : Screen
    {
        private bool _navigated;      // an Enter/back happened: land on the page once its VM swaps
        private int _navFrame;        // the frame the nav was issued (a one-frame budget for same-page re-selects)
        private IPage _lastPage;      // the page last seen (detects the swap by reference)
        private readonly Stack<IPage> _history = new Stack<IPage>();

        public override string Key => "service.Encyclopedia";
        public override string ScreenName => null; // ServiceWindowAnnounce speaks "Encyclopedia"
        public override int Layer => 10;

        public override bool IsActive()
        {
            var sw = UiContexts.ServiceWindows();
            return sw != null && sw.CurrentWindow == ServiceWindowsType.Encyclopedia && sw.EncyclopediaVM?.Value != null;
        }

        public override void OnPush() { _navigated = false; _lastPage = null; _history.Clear(); }
        public override void OnPop() { _history.Clear(); }

        // After a navigation, land focus on the page top (announced via the differ). We wait for the VM's
        // page to actually swap so we don't focus the OLD page's nodes — but re-selecting the page already
        // shown is a no-op in the game VM (no swap ever comes), so a one-frame budget also lands then and
        // keeps the flag from sticking true.
        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            var page = vm.Page?.Value?.Page;
            bool swapped = !ReferenceEquals(page, _lastPage);
            _lastPage = page;
            if (_navigated && (swapped || UnityEngine.Time.frameCount > _navFrame))
            {
                _navigated = false;
                Navigation.Active?.FocusStop("page");
            }
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // History-back is a page-view notion: only when focus is in the page and we've drilled via a
            // link. From the tree (a jump-anywhere navigator), or with nothing to return to, Escape closes.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ =>
            {
                if (!Equals(Navigation.Active?.FocusedStopKey, "tree") && _history.Count > 0) Back();
                else UiContexts.ServiceWindows()?.HandleCloseAll();
            });
        }

        private static EncyclopediaVM Vm() => UiContexts.ServiceWindows()?.EncyclopediaVM?.Value;

        // ---- navigation (drive the game's own path so the visuals + viewed-state stay in sync) ----

        private void MarkNavigated() { _navigated = true; _navFrame = UnityEngine.Time.frameCount; }

        // Jumping via the tree resets history — it's a "go anywhere" navigator, not a drill-down. The game
        // calls are guarded: navigation indexes a top-level-chapters dictionary in game code, so a
        // hypothetical nested chapter would throw there — never crash the screen over odd encyclopedia data.
        private void NavigateJump(EncyclopediaNavigationElementVM element)
        {
            if (element == null) return;
            _history.Clear();
            MarkNavigated();
            UiSound.Play(Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick);
            try { element.SelectPage(); } // the game's own click path (chapters drill to first available child)
            catch (Exception e) { Main.Log?.Log("Encyclopedia: SelectPage failed: " + e.Message); }
        }

        // Drilling via an in-page child link pushes history so Escape can return.
        private void NavigateDrill(IPage page)
        {
            if (page == null) return;
            var cur = Vm()?.Page?.Value?.Page;
            if (cur != null) _history.Push(cur);
            MarkNavigated();
            try { EventBus.RaiseEvent<IEncyclopediaHandler>(h => h.HandleEncyclopediaPage(page)); }
            catch (Exception e) { Main.Log?.Log("Encyclopedia: drill failed: " + e.Message); }
        }

        private void Back()
        {
            if (_history.Count == 0) return;
            MarkNavigated();
            var prev = _history.Pop();
            try { EventBus.RaiseEvent<IEncyclopediaHandler>(h => h.HandleEncyclopediaPage(prev)); }
            catch (Exception e) { Main.Log?.Log("Encyclopedia: back failed: " + e.Message); }
        }

        // ---- build (immediate mode) ----

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            // The hierarchy tree: nested groups keyed by page label (stable), children built lazily only
            // while their group is expanded (the child VMs are created lazily by the game).
            b.BeginStop("tree").PushContext(
                GameText.Or(() => UIStrings.Instance.MainMenu.Encyclopedia, "screen.encyclopedia"),
                role: null, positions: false);
            var chapters = vm.NavigationVM?.NavigationChapters;
            if (chapters != null) EmitChildren(b, chapters, "ency:");
            b.PopContext();

            BuildPage(b, vm);
        }

        // One sibling level of the tree, deduping labels so a repeated title can't collide the ids.
        private void EmitChildren(GraphBuilder b, IEnumerable<EncyclopediaNavigationElementVM> elements, string prefix)
        {
            var seen = new HashSet<string>();
            foreach (var el in elements)
            {
                if (el == null || !HasVisibleTitle(el.Page)) continue;
                EmitNavNode(b, el, UniqueKey(seen, prefix + PageLabel(el.Page)));
            }
        }

        // One tree node: expandable (a group whose children build lazily on expand) or a leaf; Enter loads
        // its page either way (Right/Left expand/collapse). Unread reads live as a trailing "new".
        private void EmitNavNode(GraphBuilder b, EncyclopediaNavigationElementVM element, string key)
        {
            var id = ControlId.Structural(key);
            var vt = new NodeVtable
            {
                ControlType = ControlTypes.Text, // no role word; the announcer appends expanded/collapsed on groups
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => NavLabel(element)),
                    new NodeAnnouncement(() => element.IsViewed.Value ? null : Loc.T("ency.unread"),
                        live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => NavLabel(element),
                OnActivate = () => NavigateJump(element),
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };

            var page = element.Page;
            if (page != null && page.IsChilds())
            {
                b.BeginGroup(id, vt);
                if (b.IsExpanded(id)) // materialize child VMs only while open
                    EmitChildren(b, element.GetOrCreateChildsVM(), key + "/");
                b.EndGroup();
            }
            else b.AddItem(id, vt);
        }

        // The current page: title, its typed content blocks, the page-footer glossary blurb, and links to
        // child topics. Keyed per page, so navigating re-keys it while the tree keeps expansion + position.
        private void BuildPage(GraphBuilder b, EncyclopediaVM vm)
        {
            b.BeginStop("page").PushContext(Loc.T("ency.page"), role: null, positions: false);
            var page = vm.Page?.Value;
            if (page == null)
            {
                b.AddItem(ControlId.Structural("ency:noselect"), GraphNodes.Text(() => Loc.T("ency.select_topic")));
                b.PopContext();
                return;
            }
            string pk = "ency:page:" + PageLabel(page.Page) + ":";

            var heading = !string.IsNullOrWhiteSpace(page.Title) ? page.Title : PageLabel(page.Page);
            b.AddItem(ControlId.Structural(pk + "title"), GraphNodes.Text(() => TextUtil.StripRichText(heading)));

            int bi = 0;
            foreach (var block in page.BlockVMs) { EmitBlock(b, pk + "block:" + bi, block); bi++; }

            // The page-level glossary blurb (a glossary-entry page's own definition), rendered after blocks.
            if (!string.IsNullOrWhiteSpace(page.GlossaryText))
                b.AddItem(ControlId.Structural(pk + "glossary"), Prose(page.GlossaryText));

            // Child topics as drill-in links — for index / category pages (the game's own "child pages"
            // block carries no data, so read the page's children directly).
            var childs = page.Page?.GetChilds();
            if (childs != null && childs.Count > 0)
            {
                b.PushContext(Loc.T("encyclopedia.topics"));
                var seen = new HashSet<string>();
                foreach (var child in childs)
                {
                    if (child == null || !HasVisibleTitle(child)) continue;
                    var c = child; // capture
                    b.AddItem(ControlId.Structural(UniqueKey(seen, pk + "topic:" + PageLabel(c))),
                        GraphNodes.Button(() => PageLabel(c), () => NavigateDrill(c)));
                }
                b.PopContext();
            }
            b.PopContext();
        }

        // Dispatch one content block to its reader. Image blocks are skipped (no alt-text exists).
        private void EmitBlock(GraphBuilder b, string bkey, EncyclopediaPageBlockVM block)
        {
            switch (block)
            {
                case EncyclopediaPageBlockTextVM t:
                    if (!string.IsNullOrWhiteSpace(t.Text))
                        b.AddItem(ControlId.Structural(bkey), Prose(t.Text));
                    break;
                case EncyclopediaPageBlockClassProgressionVM cp:
                    // "SkillTable" is a misnomer — it carries only the class/archetype description (no row
                    // data exists anywhere), so render that; there's no table to omit.
                    if (!string.IsNullOrWhiteSpace(cp.Description))
                        b.AddItem(ControlId.Structural(bkey), Prose(cp.Description));
                    break;
                case EncyclopediaPageBlockGlossaryEntryVM g:
                    b.AddItem(ControlId.Structural(bkey), GlossaryEntryNode(g));
                    break;
                case EncyclopediaPageBlockBookEventVM be:
                    if (!string.IsNullOrWhiteSpace(be.Text))
                        b.AddItem(ControlId.Structural(bkey), BookEventNode(be));
                    break;
                case EncyclopediaPageBlockPlanetVM p:
                    EmitPlanet(b, bkey, p);
                    break;
                case EncyclopediaPageBlockAstropathBriefVM a:
                    EmitAstropath(b, bkey, a);
                    break;
                case EncyclopediaPageBlockUnitVM u:
                    BestiaryReader.Emit(b, bkey, u);
                    break;
                // EncyclopediaPageBlockImageVM: skipped — no caption/alt anywhere.
            }
        }

        // A prose line: the block's text, TMP markup stripped; Space follows any inline glossary <link>
        // terms (deferred to the press — cheap only-if-present check gates the wiring).
        private static NodeVtable Prose(string raw)
        {
            var vt = GraphNodes.Text(() => TextUtil.StripRichTextSpaced(raw));
            if (HasLinks(raw))
                vt.OnTooltip = () => TooltipChooser.Open(
                    TextUtil.StripRichTextSpaced(raw), null, sections: null, links: GlossaryLinks.Gather(raw));
            return vt;
        }

        // A glossary A–Z entry, rendered inline on a letter-index page: "Term. Definition". Search matches
        // the term so type-ahead finds glossary entries by name; Space follows links inside the definition.
        private static NodeVtable GlossaryEntryNode(EncyclopediaPageBlockGlossaryEntryVM g)
        {
            Func<string> title = () => TextUtil.StripRichText(g.Title);
            var vt = GraphNodes.Text(() =>
            {
                var desc = TextUtil.StripRichTextSpaced(g.Description);
                return string.IsNullOrWhiteSpace(desc) ? title() : title() + ". " + desc;
            });
            vt.SearchText = title;
            if (HasLinks(g.Description))
                vt.OnTooltip = () => TooltipChooser.Open(
                    title(), TextUtil.StripRichTextSpaced(g.Description), sections: null,
                    links: GlossaryLinks.Gather(g.Description));
            return vt;
        }

        // One book-event log line: the player's chosen answers get a role prefix so they read apart from
        // the narrative cues.
        private static NodeVtable BookEventNode(EncyclopediaPageBlockBookEventVM be)
        {
            var vt = GraphNodes.Text(() =>
            {
                var text = TextUtil.StripRichTextSpaced(be.Text);
                return be.IsAnswer ? Loc.T("ency.book_answer") + " " + text : text;
            });
            if (HasLinks(be.Text))
                vt.OnTooltip = () => TooltipChooser.Open(
                    TextUtil.StripRichTextSpaced(be.Text), null, sections: null, links: GlossaryLinks.Gather(be.Text));
            return vt;
        }

        // A scanned planet, mirroring the game's own card fields (its own localized labels, passed through).
        private void EmitPlanet(GraphBuilder b, string bkey, EncyclopediaPageBlockPlanetVM p)
        {
            b.AddItem(ControlId.Structural(bkey + ":name"),
                GraphNodes.Text(() => TextUtil.StripRichText(p.Title.Value)));
            b.AddItem(ControlId.Structural(bkey + ":admin"), GraphNodes.Text(() => p.AdminKnowAboutIt.Value
                ? GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.EncyclopediaIsReportedToAdministratum, "ency.reported")
                : GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.EncyclopediaIsNotReportedToAdministratum, "ency.not_reported")));
            b.AddItem(ControlId.Structural(bkey + ":system"), GraphNodes.Text(() => string.Format(
                GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.EncyclopediaPlanetPageSystem, "ency.system"),
                p.SystemName.Value)));
            if (p.HaveColony.Value)
            {
                b.AddItem(ControlId.Structural(bkey + ":colony"), GraphNodes.Text(() =>
                    GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.EncyclopediaPlanetPageIsColonized, "ency.colonized")
                    + " " + GameText.Or(() => UIStrings.Instance.SettingsUI.DialogYes, "value.yes")));
                b.AddItem(ControlId.Structural(bkey + ":security"), GraphNodes.Text(() => string.Format(
                    GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.EncyclopediaPlanetPageSecurity, "ency.security"),
                    p.Security.Value)));
            }
            if (p.HaveQuest.Value && !string.IsNullOrWhiteSpace(p.QuestObjectiveName.Value))
                b.AddItem(ControlId.Structural(bkey + ":quest"), GraphNodes.Text(() =>
                    GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.EncyclopediaPlanetPageHaveQuest, "ency.quests")
                    + " " + p.QuestObjectiveName.Value));
            if (p.HaveRumour.Value && !string.IsNullOrWhiteSpace(p.RumourObjectiveName.Value))
                b.AddItem(ControlId.Structural(bkey + ":rumour"), GraphNodes.Text(() =>
                    GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.EncyclopediaPlanetPageHaveRumour, "ency.rumours")
                    + " " + p.RumourObjectiveName.Value));
        }

        // An astropathic brief, mirroring the card's labeled fields (the game's own strings).
        private void EmitAstropath(GraphBuilder b, string bkey, EncyclopediaPageBlockAstropathBriefVM a)
        {
            b.AddItem(ControlId.Structural(bkey + ":loc"), GraphNodes.Text(() => Field(
                GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.AstropathBriefLocation, "ency.astropath_location"),
                a.MessageLocation.Value)));
            b.AddItem(ControlId.Structural(bkey + ":date"), GraphNodes.Text(() => Field(
                GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.AstropathBriefDate, "ency.astropath_date"),
                a.MessageDate.Value)));
            b.AddItem(ControlId.Structural(bkey + ":sender"), GraphNodes.Text(() => Field(
                GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.AstropathBriefSender, "ency.astropath_sender"),
                a.MessageSender.Value)));
            if (!string.IsNullOrWhiteSpace(a.MessageBody.Value))
                b.AddItem(ControlId.Structural(bkey + ":body"), Prose(a.MessageBody.Value));
            if (a.IsMessageRead.Value)
                b.AddItem(ControlId.Structural(bkey + ":read"), GraphNodes.Text(() =>
                    GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.AstropathBriefIsRead, "ency.astropath_read")));
        }

        // ---- labels / helpers ----

        // The nav element's shown title (it carries the planet [count] suffix), else the page label.
        private static string NavLabel(EncyclopediaNavigationElementVM element)
            => !string.IsNullOrWhiteSpace(element.Title)
                ? TextUtil.StripRichText(element.Title)
                : PageLabel(element.Page);

        // An entry whose title resolves EMPTY renders as a blank, affordance-free row in the game's own view
        // — effectively invisible to sighted players. Mirror that: skip such entries entirely.
        internal static bool HasVisibleTitle(IPage page) => !string.IsNullOrWhiteSpace(page?.GetTitle());

        // Label fallback for anything that slips through with no title (defensive): the blueprint's dev name,
        // prettified ("Combat_Maneuvers" → "Combat Maneuvers").
        internal static string PageLabel(IPage page)
        {
            var t = page?.GetTitle();
            if (!string.IsNullOrWhiteSpace(t)) return TextUtil.StripRichText(t);
            if (page is BlueprintScriptableObject bp && !string.IsNullOrEmpty(bp.name)) return bp.name.Replace('_', ' ');
            return Loc.T("ency.untitled");
        }

        // "Label value" (label only when the value is empty).
        private static string Field(string label, string value)
            => string.IsNullOrEmpty(value) ? label : label + " " + value;

        // A cheap gate: only wire the Space glossary-follow when the raw text actually carries a <link>.
        private static bool HasLinks(string raw)
            => !string.IsNullOrEmpty(raw) && raw.IndexOf("<link=", StringComparison.Ordinal) >= 0;

        // Disambiguate a repeated label within a sibling group (MakeNode throws on duplicate ids); the first
        // occurrence keeps the unsuffixed key so focus stays position-stable.
        private static string UniqueKey(HashSet<string> seen, string baseKey)
        {
            if (seen.Add(baseKey)) return baseKey;
            int i = 2;
            while (!seen.Add(baseKey + "#" + i)) i++;
            return baseKey + "#" + i;
        }
    }
}
