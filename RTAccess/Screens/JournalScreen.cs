using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.AreaLogic.QuestSystem;                          // QuestState
using Kingmaker.Blueprints.Root.Strings;                        // UIStrings (journal + quest-notification strings)
using Kingmaker.Code.UI.MVVM.VM.Colonization;                   // ColonyResourceVM (an order's resource pool)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                 // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Journal;         // JournalVM, JournalQuestVM, JournalTab
using Kingmaker.Code.UI.MVVM.View.ServiceWindows.Journal.Base;  // JournalNavigationBaseView (show-completed handler)
using Kingmaker.Enums;                                          // QuestType
using Kingmaker.UI.MVVM.VM.Colonization.Projects;               // requirement / reward element VMs
using RTAccess.UI;
using RTAccess.UI.Graph;
using UnityEngine;                                              // Resources (reaching the live navigation view)

namespace RTAccess.Screens
{
    /// <summary>
    /// The journal service window (<see cref="JournalVM"/>), graph-native and shaped like the sighted
    /// window rather than flattened: a tab strip (Quests / Rumours / Orders + the "show completed quests"
    /// checkbox), the active tab's list, and the selected entry's detail card. Everything renders live
    /// from the VM each frame; the detail keys carry the selected quest, so selecting re-keys the detail
    /// only and list focus stays put. Escape closes.
    ///
    /// Why tabs and not one merged list: <c>JournalNavigationVM.ActiveTab</c> is real state that the VM
    /// itself WRITES — selecting a quest force-switches the tab to match the quest's group — and the three
    /// tabs bind three different detail cards (quest / rumour / order), the order one carrying the whole
    /// contract-completion flow. A single list was modelling one shape over a three-mode VM.
    ///
    /// Two collapsible levels, mirroring the game exactly (grep confirms only these two views carry an
    /// ExpandableCollapseMultiButton — a quest row is NOT expandable, because its objectives live in the
    /// other panel):
    ///  * chapter groups — the game's own <c>QuestGroup.IsCollapse</c>, read and flipped through
    ///    <c>JournalNavigationGroupVM.IsCollapse</c>, so our fold and the sighted one are one state. It
    ///    lives on the <c>BlueprintQuestGroups</c> asset, so it is shared session state, not per-save;
    ///  * objectives — the game DERIVES that one at bind time (open unless completed/failed) and never
    ///    writes <c>QuestBookEntityEntry.IsCollapse</c> back, so we keep our own latch rather than write a
    ///    dead field that is folded into the entity's state hash.
    ///
    /// RT differs from WOTR in three ways handled here: (1) the service-window VM hangs off Surface OR
    /// Space (no single property) — resolved by <see cref="ServiceWindows"/>; (2) <see cref="JournalVM"/>
    /// has no detail-VM property, only <c>SelectedQuest</c> (the Quest model) — the detail VM is found by
    /// matching <c>q.Quest</c> against it (<see cref="Detail"/>); (3) the quest list is split across three
    /// tab collections (<c>NavigationGroups</c> + <c>Rumors</c> + <c>Orders</c>), one per tab. Reading the
    /// VM has no side effects; <c>SelectQuest()</c>, <c>SetActiveTab()</c>, the show-completed handler and
    /// <c>CompleteOrder()</c> are the only mutations and are all the game's own methods. ScreenName is
    /// null: <c>ServiceWindowAnnounce</c> already speaks "Journal".
    /// </summary>
    public sealed class JournalScreen : Screen
    {
        public override string Key => "service.Journal";
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.Journal;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        /// <summary>Objective fold state. Chapter groups mirror the game's own <c>IsCollapse</c> instead;
        /// this latch exists only for the level where the game derives expansion and stores nothing.</summary>
        private readonly Dictionary<string, bool> _fold = new Dictionary<string, bool>();

        // ---- VM access (Surface OR Space — the journal opens in both contexts) ----
        private static ServiceWindowsVM ServiceWindows() => UiContexts.ServiceWindows();

        private static JournalVM Jv() => ServiceWindows()?.JournalVM?.Value;

        /// <summary>The game's own list filter (<c>PlayerUISettings.JournalShowCompletedQuest</c>, default
        /// ON): off hides finished quests AND whole chapter groups that hold nothing active. The game never
        /// SORTS completed quests anywhere — within a group the order is the quest book's insertion order —
        /// so this filter is the entire difference completion makes to the list.</summary>
        private static bool ShowCompleted
        {
            get
            {
                try { return Game.Instance.Player.UISettings.JournalShowCompletedQuest; }
                catch (Exception e) { Main.Log?.Error("JournalScreen.ShowCompleted: " + e); return true; }
            }
        }

        // The three tab collections flattened — for resolving the selected quest's detail VM only; the
        // LIST renders one tab at a time.
        private static IEnumerable<JournalQuestVM> AllQuestVMs(JournalVM jv)
        {
            var nav = jv.Navigation;
            if (nav == null) yield break;
            if (nav.NavigationGroups != null)
                foreach (var g in nav.NavigationGroups)
                    if (g?.Quests != null)
                        foreach (var q in g.Quests) if (q != null) yield return q;
            if (nav.Rumors != null)
                foreach (var q in nav.Rumors) if (q != null) yield return q;
            if (nav.Orders != null)
                foreach (var q in nav.Orders) if (q != null) yield return q;
        }

        // RT's JournalVM exposes only SelectedQuest (the Quest model), so resolve the detail VM by identity.
        private static JournalQuestVM Detail(JournalVM jv)
        {
            var sel = jv.SelectedQuest?.Value;
            if (sel == null) return null;
            foreach (var q in AllQuestVMs(jv)) if (q.Quest == sel) return q;
            return null;
        }

        // ---- build (immediate mode) ----

        public override void Build(GraphBuilder b)
        {
            var jv = Jv();
            var nav = jv?.Navigation;
            if (nav == null) return;
            string k = "journal:" + jv.GetHashCode() + ":";

            BuildTabs(b, nav, k);
            BuildList(b, nav, k);
            BuildDetail(b, jv, k);
        }

        // ---- zone 1: the tab strip + the show-completed checkbox (the game's navigation panel header) ----

        private static void BuildTabs(GraphBuilder b, JournalNavigationVM nav, string k)
        {
            b.BeginStop("tabs").PushContext(Loc.T("label.tabs"), Loc.T("role.list"));
            foreach (var tab in new[] { JournalTab.Quests, JournalTab.Rumors, JournalTab.Orders })
                b.AddItem(ControlId.Structural(k + "tab:" + tab), TabNode(nav, tab));
            b.AddItem(ControlId.Structural(k + "showcompleted"), ShowCompletedNode());
            b.PopContext();
        }

        // One tab: label = the game's own tab string, selection read from the live ActiveTab (the
        // SELECTION, not a cached flag), Enter drives the game's own SetActiveTab — which enforces the
        // contracts lock for us, so a disabled Orders tab simply refuses. The Orders tab additionally
        // carries the "ready to complete" badge the sighted strip shows.
        private static NodeVtable TabNode(JournalNavigationVM nav, JournalTab tab)
        {
            Func<bool> selected = () => nav.ActiveTab.Value == tab;
            Func<bool> enabled = () => tab != JournalTab.Orders || !nav.CannotAccessContracts;
            return new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => TabLabel(nav, tab)),
                    GraphNodes.SelectedPart(selected),
                    GraphNodes.DisabledPart(enabled),
                },
                SearchText = () => TabName(tab),
                StateText = () => selected() ? Loc.T("state.selected") : null,
                OnActivate = enabled() ? (Action)(() => nav.SetActiveTab(tab)) : null,
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        private static string TabLabel(JournalNavigationVM nav, JournalTab tab)
        {
            string name = TabName(tab);
            if (tab != JournalTab.Orders) return name;
            // The strip's ready-to-complete image: at least one contract whose requirements are all met.
            bool ready;
            try { ready = nav.CheckReadyToCompleteOrders() && !nav.CannotAccessContracts; }
            catch (Exception e) { Main.Log?.Error("JournalScreen.TabLabel: " + e); return name; }
            return ready ? name + ", " + Loc.T("journal.ready_to_complete") : name;
        }

        private static string TabName(JournalTab tab)
        {
            switch (tab)
            {
                case JournalTab.Rumors:
                    return GameText.Or(() => UIStrings.Instance.QuesJournalTexts.Rumours, "journal.rumours");
                case JournalTab.Orders:
                    return GameText.Or(() => UIStrings.Instance.QuesJournalTexts.Orders, "journal.orders");
                default:
                    return GameText.Or(() => UIStrings.Instance.QuesJournalTexts.Quests, "journal.quests");
            }
        }

        private static NodeVtable ShowCompletedNode()
            => GraphNodes.Toggle(
                () => GameText.Or(() => UIStrings.Instance.QuesJournalTexts.ShowCompletedQuests,
                    "journal.show_completed"),
                () => ShowCompleted,
                () => SetShowCompleted(!ShowCompleted));

        /// <summary>Flip the filter through the game's own toggle handler. It writes the setting, redraws
        /// the game's list AND — when the tracked quest was completed/failed and has just been hidden —
        /// retargets the current quest to a still-visible one. That retarget is the reason to reach for the
        /// live view rather than poke the setting: without it the detail card would keep showing a quest
        /// that is no longer in the list. Falls back to the raw setting if no bound view is around.</summary>
        private static void SetShowCompleted(bool value)
        {
            try
            {
                foreach (var view in Resources.FindObjectsOfTypeAll<JournalNavigationBaseView>())
                {
                    if (view == null || !view.IsBinded || !view.isActiveAndEnabled) continue;
                    view.OnShowCompletedToggleChanged(value);
                    return;
                }
            }
            catch (Exception e) { Main.Log?.Error("JournalScreen.SetShowCompleted: " + e); }
            try { Game.Instance.Player.UISettings.JournalShowCompletedQuest = value; }
            catch (Exception e) { Main.Log?.Error("JournalScreen.SetShowCompleted fallback: " + e); }
        }

        // ---- zone 2: the active tab's list ----

        private static void BuildList(GraphBuilder b, JournalNavigationVM nav, string k)
        {
            var tab = nav.ActiveTab.Value;
            b.BeginStop("quests").PushContext(TabName(tab), Loc.T("role.list"));
            bool any;
            switch (tab)
            {
                case JournalTab.Rumors: any = BuildRumours(b, nav, k); break;
                case JournalTab.Orders: any = BuildOrders(b, nav, k); break;
                default: any = BuildQuestGroups(b, nav, k); break;
            }
            if (!any)
                b.AddItem(ControlId.Structural(k + "empty"), GraphNodes.Text(() => EmptyText(tab)));
            b.PopContext();
        }

        // Quests tab: one expandable group per chapter, in the blueprint's own group order. Both filters
        // are the game's: whole groups drop when they hold nothing active, then quests drop individually.
        private static bool BuildQuestGroups(GraphBuilder b, JournalNavigationVM nav, string k)
        {
            if (nav.NavigationGroups == null) return false;
            bool show = ShowCompleted;
            bool any = false;
            int gi = 0;
            foreach (var group in nav.NavigationGroups)
            {
                int index = gi++;
                if (group?.Quests == null) continue;
                if (!show && !group.HasActiveQuests) continue;
                var quests = show ? group.Quests : group.Quests.Where(q => q != null && q.IsActive).ToList();
                if (quests.Count == 0) continue;

                var g = group; // capture
                string gkey = k + "group:" + index;
                var vt = GraphNodes.Group(() => g.Title);
                // The game's own fold — one state shared with the sighted panel. (Its view re-reads
                // IsCollapse only when it rebinds, so the sighted strip catches up on reopen.)
                vt.OnExpand = () => g.IsCollapse = false;
                vt.OnCollapse = () => g.IsCollapse = true;
                b.SetRegion(gkey);
                b.BeginGroup(ControlId.Referenced(g, gkey), vt, expanded: !g.IsCollapse);
                AppendQuests(b, gkey, quests);
                b.EndGroup();
                any = true;
            }
            b.SetRegion(null);
            return any;
        }

        // Rumours tab: the game splits it into two labelled sections by quest TYPE — plain rumours first,
        // then rumours about us — and hides a section that is empty. Headings, not folds: the sighted
        // sections carry no expander.
        private static bool BuildRumours(GraphBuilder b, JournalNavigationVM nav, string k)
        {
            if (nav.Rumors == null) return false;
            bool any = AppendSection(b, k, "rumours", QuestType.Rumour,
                GameText.Or(() => UIStrings.Instance.QuesJournalTexts.AllRumoursTitle, "journal.all_rumours"),
                nav.Rumors);
            any |= AppendSection(b, k, "aboutus", QuestType.RumourAboutUs,
                GameText.Or(() => UIStrings.Instance.QuesJournalTexts.RumoursAboutUsTitle,
                    "journal.rumours_about_us"),
                nav.Rumors);
            b.SetRegion(null);
            return any;
        }

        private static bool AppendSection(GraphBuilder b, string k, string skey, QuestType type, string label,
            List<JournalQuestVM> rumours)
        {
            bool show = ShowCompleted;
            var list = rumours.Where(q => q != null && q.Quest?.Blueprint?.Type == type
                                          && (show || q.IsActive)).ToList();
            if (list.Count == 0) return false;
            string rkey = k + "section:" + skey;
            b.SetRegion(rkey);
            b.PushContext(label);
            AppendQuests(b, rkey, list);
            b.PopContext();
            return true;
        }

        // Orders tab: a flat list, same active filter.
        private static bool BuildOrders(GraphBuilder b, JournalNavigationVM nav, string k)
        {
            if (nav.Orders == null) return false;
            bool show = ShowCompleted;
            var list = nav.Orders.Where(q => q != null && (show || q.IsActive)).ToList();
            if (list.Count == 0) return false;
            string okey = k + "orders";
            b.SetRegion(okey);
            AppendQuests(b, okey, list);
            b.SetRegion(null);
            return true;
        }

        // Quests key by VM (reference tier), so focus follows an entry that moves within its list. The
        // selected one is the graph's start: opening the journal lands on the tracked quest, as the
        // sighted window does.
        private static void AppendQuests(GraphBuilder b, string keyPrefix, IList<JournalQuestVM> quests)
        {
            for (int i = 0; i < quests.Count; i++)
            {
                var q = quests[i];
                if (q == null) continue;
                var id = ControlId.Referenced(q, keyPrefix + ":q:" + i);
                b.AddItem(id, QuestNode(q));
                if (q.IsSelected.Value) b.SetStart(id);
            }
        }

        // One quest: a radio button — the entries form a selection group (the selected one drives the
        // detail panel) — reading "selected" for the shown quest plus its live state. Enter selects it via
        // the game's own SelectQuest, which updates JournalVM.SelectedQuest (and the tracked quest) and so
        // the detail stop, and announces "selected" synchronously (keypress provenance).
        private static NodeVtable QuestNode(JournalQuestVM q)
        {
            return new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => q.Title),
                    GraphNodes.SelectedPart(() => q.IsSelected.Value),
                    new NodeAnnouncement(() => QuestStateText(q), live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => q.Title,
                StateText = () => q.IsSelected.Value ? Loc.T("state.selected") : null,
                OnActivate = () => q.SelectQuest(),
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        /// <summary>The entry's state, in the game's own words and by the game's own precedence — the paper
        /// status mark on the list card and the status label on the detail card resolve New → Updated →
        /// Completed → Failed → Postponed, and the strings noun themselves per tab ("New Rumour", "Order
        /// Completed"). Where the game marks nothing we say "in progress": an unmarked icon slot is a
        /// visible state for a sighted player and simply nothing at all for a screen reader.</summary>
        private static string QuestStateText(JournalQuestVM q)
        {
            try
            {
                var bp = q.Quest?.Blueprint;
                if (bp == null) return Loc.T("journal.active");
                int mark = (!q.IsNew || q.QuestIsViewed)
                    ? (q.IsUpdated ? 4 : q.IsCompleted ? 2 : q.IsFailed ? 3 : q.IsPostponed ? 5 : -1)
                    : 0;
                string s = mark < 0
                    ? null
                    : UIStrings.Instance.QuestNotificationTexts.GetQuestHintStateText(mark, bp.Type, bp.Group);
                return string.IsNullOrWhiteSpace(s) ? Loc.T("journal.active") : s;
            }
            catch (Exception e)
            {
                Main.Log?.Error("JournalScreen.QuestStateText: " + e);
                return Loc.T("journal.active");
            }
        }

        private static string EmptyText(JournalTab tab)
        {
            try
            {
                var fmt = UIStrings.Instance.QuesJournalTexts.NoNameOfTheListObjectsAvailable?.Text;
                if (!string.IsNullOrEmpty(fmt)) return string.Format(fmt, TabName(tab));
            }
            catch (Exception e) { Main.Log?.Error("JournalScreen.EmptyText: " + e); }
            return Loc.T("journal.empty", new { tab = TabName(tab) });
        }

        // ---- zone 3: the selected entry's detail card ----

        // Title, status, place, service message, description, completion text — then the card's kind-specific
        // block (rumour marker / order contract block) — then the objectives. Keys carry the selected quest,
        // so a selection change re-keys the detail while list focus stays put.
        private void BuildDetail(GraphBuilder b, JournalVM jv, string k)
        {
            var q = Detail(jv);
            b.BeginStop("detail");
            if (q == null)
            {
                b.AddItem(ControlId.Structural(k + "noselect"),
                    GraphNodes.Text(() => Loc.T("journal.select_quest")));
                return;
            }
            string dk = k + "d:" + (jv.SelectedQuest?.Value?.Blueprint?.name ?? q.Title) + ":";

            b.PushContext(Loc.T("journal.quest"), role: null, positions: false);
            b.AddItem(ControlId.Structural(dk + "title"), Heading(() => q.Title));
            b.AddItem(ControlId.Structural(dk + "status"), GraphNodes.Text(() => QuestStateText(q)));
            // The quest's authored location, rendered on the sighted card. RT has no world-space quest
            // markers at all — no per-entity "this is an objective" flag exists — so this line and the
            // per-objective Destination below are the ONLY "where do I go" guidance the game offers.
            if (!string.IsNullOrWhiteSpace(q.Place))
                b.AddItem(ControlId.Structural(dk + "place"),
                    GraphNodes.Text(() => Loc.T("journal.place", new { place = q.Place })));
            if (!string.IsNullOrWhiteSpace(q.ServiceMessage))
                b.AddItem(ControlId.Structural(dk + "service"), GraphNodes.Text(() => q.ServiceMessage));
            if (!string.IsNullOrWhiteSpace(q.Description))
                b.AddItem(ControlId.Structural(dk + "desc"), GraphNodes.Text(() => q.Description));
            if (!string.IsNullOrWhiteSpace(q.CompletionText))
                b.AddItem(ControlId.Structural(dk + "completion"), GraphNodes.Text(() => q.CompletionText));

            if (q.IsRumour) BuildRumourBlock(b, q, dk);
            if (q.IsOrder) BuildOrderBlock(b, q, dk);
            BuildObjectives(b, q, dk);
            b.PopContext();
        }

        // The rumour card's own header marker: whether the party is standing in a system this rumour points
        // at. The card's destination IMAGE is a picture with no text behind it, so its only readable content
        // is the game's own "no data" placeholder when the rumour carries none.
        private static void BuildRumourBlock(GraphBuilder b, JournalQuestVM q, string dk)
        {
            if (q.IsAtDestinationSystem)
                b.AddItem(ControlId.Structural(dk + "inrange"), GraphNodes.Text(
                    () => GameText.Or(() => UIStrings.Instance.QuesJournalTexts.YouAreWithinRange,
                        "journal.within_range")));
            if (!q.HasDestinationImage)
                b.AddItem(ControlId.Structural(dk + "nodata"), GraphNodes.Text(
                    () => Loc.T("journal.destination_image", new
                    {
                        value = GameText.Or(() => UIStrings.Instance.QuesJournalTexts.NoData, "journal.no_data"),
                    })));
        }

        // The contract block: what it costs, what it pays, the button that hands it in, and the resource
        // pool the sighted card prints underneath so you can see whether you can afford it.
        private static void BuildOrderBlock(GraphBuilder b, JournalQuestVM q, string dk)
        {
            bool handedIn = q.IsOrderCompleted.Value || q.Quest?.State == QuestState.Completed;

            if (q.Requirements != null && q.Requirements.Count > 0)
            {
                b.SetRegion(dk + "requirements");
                b.PushContext(GameText.Or(() => UIStrings.Instance.QuesJournalTexts.RequiredResources,
                    "journal.requirements"));
                for (int i = 0; i < q.Requirements.Count; i++)
                {
                    var r = q.Requirements[i];
                    if (r == null) continue;
                    b.AddItem(ControlId.Structural(dk + "req:" + i), GraphNodes.Text(() => RequirementLine(r)));
                }
                b.PopContext();
            }

            if (q.Rewards != null && q.Rewards.Count > 0)
            {
                b.SetRegion(dk + "rewards");
                b.PushContext(GameText.Or(() => UIStrings.Instance.QuesJournalTexts.RewardsResources,
                    "journal.rewards"));
                for (int i = 0; i < q.Rewards.Count; i++)
                {
                    var r = q.Rewards[i];
                    if (r == null) continue;
                    b.AddItem(ControlId.Structural(dk + "rew:" + i), GraphNodes.Text(() => RewardLine(r)));
                }
                b.PopContext();
            }

            // The game hides the whole completion group once the contract is in, and greys the button until
            // every requirement checks out — CompleteOrder is the game's own hand-in (it queues the
            // CompleteContract command and flips the VM), so we drive exactly that.
            if (!handedIn)
            {
                b.SetRegion(dk + "complete");
                b.AddItem(ControlId.Structural(dk + "complete"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.QuesJournalTexts.CompleteOrder,
                        "journal.complete_order"),
                    () => q.CompleteOrder(),
                    () => q.CanCompleteOrder && !q.IsOrderCompleted.Value));
            }

            if (q.ResourcesVMs != null && q.ResourcesVMs.Count > 0)
            {
                b.SetRegion(dk + "resources");
                b.PushContext(GameText.Or(() => UIStrings.Instance.QuesJournalTexts.OrderResourcesYourResources,
                    "journal.your_resources"));
                for (int i = 0; i < q.ResourcesVMs.Count; i++)
                {
                    var r = q.ResourcesVMs[i];
                    if (r == null) continue;
                    b.AddItem(ControlId.Structural(dk + "res:" + i), GraphNodes.Text(() => ResourceLine(r)));
                }
                var pf = q.JournalOrderProfitFactorVM;
                if (pf != null)
                    b.AddItem(ControlId.Structural(dk + "res:pf"), GraphNodes.Text(
                        // The card prints the raw total (float ToString), so mirror that rather than round.
                        () => Loc.T("gauge.profit_factor", new { value = pf.Count.Value.ToString() })));
                b.PopContext();
            }
            b.SetRegion(null);
        }

        // "<description> <count>, met / not met" — the requirement row is a description, a count and a
        // checkmark; the checkmark is the whole point (it says whether this one is satisfied).
        private static string RequirementLine(ColonyProjectsRequirementElementVM r)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.Description.Value)) parts.Add(r.Description.Value);
            if (!string.IsNullOrWhiteSpace(r.CountText.Value)) parts.Add(r.CountText.Value);
            parts.Add(Loc.T(r.IsChecked.Value ? "journal.requirement_met" : "journal.requirement_unmet"));
            return string.Join(", ", parts.ToArray());
        }

        private static string RewardLine(ColonyProjectsRewardElementVM r)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.Description.Value)) parts.Add(r.Description.Value);
            if (!string.IsNullOrWhiteSpace(r.CountText.Value)) parts.Add(r.CountText.Value);
            return string.Join(", ", parts.ToArray());
        }

        private static string ResourceLine(ColonyResourceVM r)
        {
            string name = null;
            // BlueprintResource.Name dereferences the blueprint's LocalizedString — guard it rather than
            // let a half-built resource blueprint throw out of a browse-label lambda.
            try { name = r.BlueprintResource?.Value?.Name; }
            catch (Exception e) { Main.Log?.Error("JournalScreen.ResourceLine: " + e); }
            return string.IsNullOrWhiteSpace(name)
                ? r.Count.Value.ToString()
                : name + ": " + r.Count.Value;
        }

        // ---- objectives ----

        // One expandable group per objective — the level the sighted card folds, defaulting open unless the
        // objective is finished, exactly as the card's expander does. Inside: the description, its progress
        // counter and destination, then the addendums and clues the card lists under it.
        private void BuildObjectives(GraphBuilder b, JournalQuestVM q, string dk)
        {
            if (q.Objectives == null || q.Objectives.Count == 0) return;
            b.PushContext(Loc.T("journal.objectives"));
            for (int oi = 0; oi < q.Objectives.Count; oi++)
            {
                var o = q.Objectives[oi];
                if (o == null) continue;
                string okey = dk + "obj:" + oi;
                var vt = GraphNodes.Group(() => ObjectiveHeader(o));
                vt.OnExpand = () => _fold[okey] = true;
                vt.OnCollapse = () => _fold[okey] = false;
                b.SetRegion(okey);
                b.BeginGroup(ControlId.Structural(okey), vt,
                    expanded: Fold(okey, !(o.IsCompleted || o.IsFailed)));

                if (!string.IsNullOrWhiteSpace(o.Description))
                    b.AddItem(ControlId.Structural(okey + ":desc"), GraphNodes.Text(() => o.Description));
                string counter = Counter(o.HasEtudeCounter, o.CurrentEtudeCounter, o.MinEtudeCounter,
                    o.EtudeCounterDescription);
                if (counter != null)
                    b.AddItem(ControlId.Structural(okey + ":counter"), GraphNodes.Text(() => counter));
                if (!string.IsNullOrWhiteSpace(o.Destination))
                    b.AddItem(ControlId.Structural(okey + ":dest"), GraphNodes.Text(
                        () => Loc.T("journal.destination", new { place = o.Destination })));

                AppendAddendums(b, okey + ":add", o.Addendums);
                AppendAddendums(b, okey + ":clue", o.Clues);
                b.EndGroup();
            }
            b.SetRegion(null);
            b.PopContext();
        }

        // The objective header: its title plus its state, in the game's own words. The card shows title and
        // description as separate lines, so the title stays here even when a description follows.
        private static string ObjectiveHeader(JournalQuestObjectiveVM o)
        {
            string title = string.IsNullOrWhiteSpace(o.Title) ? o.Description : o.Title;
            string state = ObjectiveState(o);
            return string.IsNullOrWhiteSpace(state) ? title : title + " (" + state + ")";
        }

        /// <summary>The objective's state mark, mirroring <c>JournalQuestObjectiveBaseView.GetHintText</c>:
        /// failed / completed / postponed, else new when unviewed and started once viewed.</summary>
        private static string ObjectiveState(JournalQuestObjectiveVM o)
        {
            try
            {
                var t = UIStrings.Instance.QuestNotificationTexts;
                if (o.IsFailed) return t.QuestFailed;
                if (o.IsCompleted) return t.QuestComplite;
                if (o.IsPostponed) return t.QuestPostponed;
                return o.IsViewed ? (string)t.QuestStarted : (string)t.QuestNew;
            }
            catch (Exception e)
            {
                Main.Log?.Error("JournalScreen.ObjectiveState: " + e);
                return Loc.T("journal.active");
            }
        }

        // Addendums and clues share a row shape on the card (description, destination, counter) and differ
        // only in the mark: a clue reads as a clue once seen, and as new until then.
        private static void AppendAddendums(GraphBuilder b, string keyPrefix,
            List<JournalQuestObjectiveAddendumVM> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null) continue;
                string akey = keyPrefix + ":" + i;
                b.AddItem(ControlId.Structural(akey), GraphNodes.Text(() => AddendumLine(a)));
                string counter = Counter(a.HasEtudeCounter, a.CurrentEtudeCounter, a.MinEtudeCounter,
                    a.EtudeCounterDescription);
                if (counter != null)
                    b.AddItem(ControlId.Structural(akey + ":counter"), GraphNodes.Text(() => counter));
                if (!string.IsNullOrWhiteSpace(a.Destination))
                    b.AddItem(ControlId.Structural(akey + ":dest"), GraphNodes.Text(
                        () => Loc.T("journal.destination", new { place = a.Destination })));
            }
        }

        private static string AddendumLine(JournalQuestObjectiveAddendumVM a)
        {
            string state = AddendumState(a);
            return string.IsNullOrWhiteSpace(state) ? a.Description : a.Description + " (" + state + ")";
        }

        private static string AddendumState(JournalQuestObjectiveAddendumVM a)
        {
            try
            {
                var t = UIStrings.Instance.QuestNotificationTexts;
                bool isClue = a.Addendum?.Blueprint?.IsClue ?? false;
                if (a.IsFailed) return t.QuestFailed;
                if (a.IsCompleted) return t.QuestComplite;
                // A clue's row replaces the state mark with a clue mark once it has been seen.
                if (isClue) return a.IsViewed ? Loc.T("quest.clue") : (string)t.QuestNew;
                return a.IsViewed ? (string)t.QuestStarted : (string)t.QuestNew;
            }
            catch (Exception e)
            {
                Main.Log?.Error("JournalScreen.AddendumState: " + e);
                return null;
            }
        }

        /// <summary>Progress counter for entries that track a tally ("2 of 5, bodies recovered"), rendered on
        /// the sighted card by the etude-counter widget as "current/min description". <c>MinEtudeCounter</c>
        /// is the TARGET (the VM sets it from the condition's <c>MinValue</c>, and clamps Current to it), so
        /// it reads as the total.
        ///
        /// The <c>min &gt; 0</c> guard is load-bearing: the VM sets <c>HasEtudeCounter</c> from merely having
        /// a condition, but only fills the two numbers when that condition is a <c>FlagInRange</c>. Any other
        /// condition type leaves both at zero, which would otherwise announce a bogus "0 of 0".</summary>
        internal static string Counter(bool has, int current, int min, string description)
        {
            if (!has || min <= 0) return null;
            string s = Loc.T("journal.counter", new { current = current, total = min });
            return string.IsNullOrWhiteSpace(description) ? s : s + ", " + description;
        }

        // The quest title as the detail's lead line — role "heading", matching the old heading TextElement.
        private static NodeVtable Heading(Func<string> text)
        {
            var vt = GraphNodes.Text(text);
            vt.Announcements = new List<NodeAnnouncement>(vt.Announcements)
            {
                new NodeAnnouncement(() => Loc.T("role.heading"), kind: AnnouncementKinds.Role),
            };
            return vt;
        }

        // The group's fold state: the user's explicit fold if any, else the default — computed ONCE and
        // latched, so a default that flips mid-session (an objective completing while you read it) doesn't
        // fold the group under focus.
        private bool Fold(string key, bool def)
        {
            bool v;
            if (_fold.TryGetValue(key, out v)) return v;
            _fold[key] = def;
            return def;
        }
    }
}
