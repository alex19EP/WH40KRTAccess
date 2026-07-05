using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;                 // UIStrings (the journal tab labels, passed through)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;          // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Journal;  // JournalVM, JournalQuestVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The journal service window (<see cref="JournalVM"/>), graph-native: the grouped quest list on top
    /// (one region + context per quest group — Ctrl+arrows jump groups, entering one announces its title;
    /// each quest a radio reading its state and the "updated" attention flag; Enter selects) and the
    /// selected quest's detail below — title, description, completion text for finished quests, then its
    /// objectives (and their addendums) with their state. Everything renders live; the detail keys carry
    /// the selected quest, so selecting re-keys the detail only and quest-list focus stays put — the old
    /// signature/capture/restore machinery is deleted (the differ's identity keys give it for free).
    /// Escape closes.
    ///
    /// RT differs from WOTR in three ways handled here: (1) the service-window VM hangs off Surface OR
    /// Space (no single property) — resolved by <see cref="ServiceWindows"/>; (2) <see cref="JournalVM"/>
    /// has no detail-VM property, only <c>SelectedQuest</c> (the Quest model) — the detail VM is found by
    /// matching <c>q.Quest</c> against it (<see cref="Detail"/>); (3) the quest list is split across three
    /// tab collections (<c>NavigationGroups</c> + <c>Rumors</c> + <c>Orders</c>) — flattened into one
    /// grouped list, the fixed two labeled with the game's own tab strings. The screen is read-only
    /// (reading VM fields has no side effects); <c>SelectQuest()</c> is the only mutation and is the
    /// intended user action. ScreenName is null: <c>ServiceWindowAnnounce</c> already speaks "Journal".
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

        // ---- VM access (Surface OR Space — the journal opens in both contexts) ----
        private static ServiceWindowsVM ServiceWindows()
        {
            var ctx = Game.Instance?.RootUiContext;
            return ctx?.SurfaceVM?.StaticPartVM?.ServiceWindowsVM
                ?? ctx?.SpaceVM?.StaticPartVM?.ServiceWindowsVM;
        }

        private static JournalVM Jv() => ServiceWindows()?.JournalVM?.Value;

        // The three tab collections flattened: grouped quests, then rumours, then orders.
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

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var jv = Jv();
            if (jv == null) return;
            string k = "journal:" + jv.GetHashCode() + ":";

            BuildQuestList(b, jv, k);
            BuildDetail(b, jv, k);
        }

        // The grouped quest list: one stop; a region + context level per quest group (Ctrl+arrows jump
        // between groups, entering one announces its title). Quests key by VM (reference tier), so focus
        // follows a quest that moves within its group.
        private static void BuildQuestList(GraphBuilder b, JournalVM jv, string k)
        {
            b.BeginStop("quests").PushContext(Loc.T("journal.quests"), Loc.T("role.list"), positions: false);
            var nav = jv.Navigation;
            bool any = false;
            int gi = 0;
            if (nav?.NavigationGroups != null)
                foreach (var g in nav.NavigationGroups)
                {
                    if (g?.Quests == null || g.Quests.Count == 0) { gi++; continue; }
                    b.SetRegion(k + "group:" + gi);
                    b.PushContext(g.Title);
                    int qi = 0;
                    foreach (var q in g.Quests)
                    {
                        if (q == null) { qi++; continue; }
                        b.AddItem(ControlId.Referenced(q, k + "q:" + gi + ":" + qi), QuestNode(q));
                        any = true;
                        qi++;
                    }
                    b.PopContext();
                    gi++;
                }
            // Rumours and Orders live on their own game tabs — surfaced here as two more groups, labeled
            // with the game's own tab strings (already localized; ui.json only carries the fallback).
            any |= AppendGroup(b, k, "rumours",
                GameText.Or(() => UIStrings.Instance.QuesJournalTexts.Rumours, "journal.rumours"), nav?.Rumors);
            any |= AppendGroup(b, k, "orders",
                GameText.Or(() => UIStrings.Instance.QuesJournalTexts.Orders, "journal.orders"), nav?.Orders);
            if (!any)
                b.AddItem(ControlId.Structural(k + "noquests"), GraphNodes.Text(() => Loc.T("journal.no_quests")));
            b.PopContext();
        }

        private static bool AppendGroup(GraphBuilder b, string k, string gkey, string label,
            List<JournalQuestVM> quests)
        {
            if (quests == null || quests.Count == 0) return false;
            b.SetRegion(k + "group:" + gkey);
            b.PushContext(label);
            bool any = false;
            int qi = 0;
            foreach (var q in quests)
            {
                if (q == null) { qi++; continue; }
                b.AddItem(ControlId.Referenced(q, k + "q:" + gkey + ":" + qi), QuestNode(q));
                any = true;
                qi++;
            }
            b.PopContext();
            return any;
        }

        // One quest: a radio button — the quests form a selection group (the selected one drives the
        // detail panel) — reading "selected" for the shown quest plus its live state (active / completed /
        // failed, and "updated" when it needs attention; RT exposes these as bools, not WOTR's loc keys).
        // Enter selects it via the game's own SelectQuest, which updates JournalVM.SelectedQuest and so
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
                    new NodeAnnouncement(() => QuestState(q), live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => q.Title,
                StateText = () => q.IsSelected.Value ? Loc.T("state.selected") : null,
                OnActivate = () => q.SelectQuest(),
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        private static string QuestState(JournalQuestVM q)
        {
            var s = StateWord(q.IsCompleted, q.IsFailed);
            if (q.IsAttention) s += ", " + Loc.T("journal.updated");
            return s;
        }

        // The selected quest's detail: title + description (+ completion text for finished quests), then
        // its objectives and their addendums, each with its state. Keys carry the selected quest, so a
        // selection change re-keys the detail while quest-list focus stays put.
        private static void BuildDetail(GraphBuilder b, JournalVM jv, string k)
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
            if (!string.IsNullOrWhiteSpace(q.Description))
                b.AddItem(ControlId.Structural(dk + "desc"), GraphNodes.Text(() => q.Description));
            if (q.IsCompleted && !string.IsNullOrWhiteSpace(q.CompletionText))
                b.AddItem(ControlId.Structural(dk + "completion"), GraphNodes.Text(() => q.CompletionText));

            if (q.Objectives != null && q.Objectives.Count > 0)
            {
                b.SetRegion(dk + "objectives");
                b.PushContext(Loc.T("journal.objectives"));
                int oi = 0;
                foreach (var o in q.Objectives)
                {
                    if (o == null) { oi++; continue; }
                    var ob = o; // capture
                    b.AddItem(ControlId.Structural(dk + "obj:" + oi), GraphNodes.Text(
                        () => (string.IsNullOrWhiteSpace(ob.Description) ? ob.Title : ob.Description)
                            + " (" + StateWord(ob.IsCompleted, ob.IsFailed) + ")"));
                    if (ob.Addendums != null)
                    {
                        int ai = 0;
                        foreach (var a in ob.Addendums)
                        {
                            if (a == null) { ai++; continue; }
                            var ad = a; // capture
                            b.AddItem(ControlId.Structural(dk + "obj:" + oi + ":add:" + ai), GraphNodes.Text(
                                () => ad.Description + " (" + StateWord(ad.IsCompleted, ad.IsFailed) + ")"));
                            ai++;
                        }
                    }
                    oi++;
                }
                b.PopContext();
            }
            b.PopContext();
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

        private static string StateWord(bool completed, bool failed)
            => Loc.T(completed ? "journal.completed" : failed ? "journal.failed" : "journal.active");
    }
}
