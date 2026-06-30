using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;          // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Journal;  // JournalVM, JournalQuestVM
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The journal service window (<see cref="JournalVM"/>): the grouped quest list (each quest reads its
    /// state; Enter selects it) and the selected quest's detail below — title, description, completion text
    /// for finished quests, then its objectives (and their addendums) with their state. Content refills when
    /// the selection or any quest/objective state changes, restoring the cursor by grid position. Escape
    /// closes.
    ///
    /// Ported from WrathAccess; adapted to RT's VM shape. RT differs from WOTR in three ways handled here:
    /// (1) the service-window VM hangs off Surface OR Space (no single property) — resolved by
    /// <see cref="ServiceWindows"/>; (2) <see cref="JournalVM"/> has no detail-VM property, only
    /// <c>SelectedQuest</c> (the Quest model) — the detail VM is found by matching <c>q.Quest</c> against it
    /// (<see cref="Detail"/>); (3) the quest list is split across three tab collections
    /// (<c>NavigationGroups</c> + <c>Rumors</c> + <c>Orders</c>) — flattened into one grouped list. The screen
    /// is read-only (reading VM fields has no side effects); <c>SelectQuest()</c> is the only mutation and is
    /// the intended user action. ScreenName is null: <c>ServiceWindowAnnounce</c> already speaks "Journal".
    /// </summary>
    public sealed class JournalScreen : Screen
    {
        public override string Key => "service.Journal";
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.Journal;

        private Container _content;
        private bool _built;
        private string _sig;
        private string _lastRestoreLabel;

        public override void OnPush() { _built = false; _sig = null; _lastRestoreLabel = null; }
        public override void OnPop() { Clear(); _content = null; _built = false; }

        public override void OnUpdate()
        {
            var jv = Jv();
            if (jv == null) return;
            if (!_built) BuildShell();
            var sig = Sig(jv);
            if (sig != _sig) { _sig = sig; RefillContent(jv); }
            else _lastRestoreLabel = null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Raw("Close"),
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

        // Refreshes on selection change and on any quest / objective state (or attention) change.
        private static string Sig(JournalVM jv)
        {
            var sb = new StringBuilder();
            sb.Append(jv.SelectedQuest?.Value?.Blueprint?.name).Append('|');
            foreach (var q in AllQuestVMs(jv))
                sb.Append(q.Title).Append(q.IsCompleted ? 'C' : q.IsFailed ? 'F' : 'A')
                  .Append(q.IsAttention ? '!' : '.').Append(',');
            sb.Append('|');
            var det = Detail(jv);
            if (det?.Objectives != null)
                foreach (var o in det.Objectives)
                    if (o != null) sb.Append(o.IsCompleted ? 'C' : o.IsFailed ? 'F' : 'A');
            return sb.ToString();
        }

        private void BuildShell()
        {
            _built = true;
            Clear();
            _content = new Panel();
            Add(_content);
        }

        private void RefillContent(JournalVM jv)
        {
            if (_content == null) return;
            var cap = CaptureFocus();
            _content.Clear();
            BuildQuestList(jv);
            BuildDetail(jv);
            RestoreFocus(cap);
        }

        // The grouped quest list: a List region per quest group (then Rumours, Orders), each quest selectable.
        private void BuildQuestList(JournalVM jv)
        {
            var sheet = new FlowSheet("Quests");
            bool any = false;
            var nav = jv.Navigation;
            if (nav?.NavigationGroups != null)
                foreach (var g in nav.NavigationGroups)
                {
                    if (g?.Quests == null || g.Quests.Count == 0) continue;
                    var r = sheet.List(g.Title);
                    foreach (var q in g.Quests) if (q != null) { r.Item(new ProxyJournalQuest(q)); any = true; }
                }
            any |= AppendGroup(sheet, "Rumours", nav?.Rumors);
            any |= AppendGroup(sheet, "Orders", nav?.Orders);
            if (!any) sheet.List(null).Item(new TextElement("No quests."));
            sheet.Reflow();
            _content.Add(sheet);
        }

        private static bool AppendGroup(FlowSheet sheet, string label, List<JournalQuestVM> quests)
        {
            if (quests == null || quests.Count == 0) return false;
            var r = sheet.List(label);
            bool any = false;
            foreach (var q in quests) if (q != null) { r.Item(new ProxyJournalQuest(q)); any = true; }
            return any;
        }

        // The selected quest's detail: title + description (+ completion text), then its objectives and their
        // addendums, each with its state.
        private void BuildDetail(JournalVM jv)
        {
            var q = Detail(jv);
            if (q == null) { _content.Add(new TextElement("Select a quest.")); return; }

            var sheet = new FlowSheet("Quest");
            var head = sheet.List(null);
            head.Item(new TextElement(q.Title, "heading"));
            if (!string.IsNullOrWhiteSpace(q.Description)) head.Item(new TextElement(q.Description));
            if (q.IsCompleted && !string.IsNullOrWhiteSpace(q.CompletionText))
                head.Item(new TextElement(q.CompletionText));

            if (q.Objectives != null && q.Objectives.Count > 0)
            {
                var obj = sheet.List("Objectives");
                foreach (var o in q.Objectives)
                {
                    if (o == null) continue;
                    var text = string.IsNullOrWhiteSpace(o.Description) ? o.Title : o.Description;
                    obj.Item(new TextElement(text + " (" + StateWord(o.IsCompleted, o.IsFailed) + ")"));
                    if (o.Addendums != null)
                        foreach (var a in o.Addendums)
                            if (a != null)
                                obj.Item(new TextElement("  " + a.Description + " (" + StateWord(a.IsCompleted, a.IsFailed) + ")"));
                }
            }

            sheet.Reflow();
            _content.Add(sheet);
        }

        private static string StateWord(bool completed, bool failed)
            => completed ? "completed" : failed ? "failed" : "active";

        // (contentChildIndex, row, col) of the focused cell, or child = -1 when focus is outside the content.
        private (int child, int row, int col) CaptureFocus()
        {
            var cur = Navigation.Current;
            if (cur != null)
                for (int i = 0; i < _content.Children.Count; i++)
                    if (_content.Children[i] is FlowSheet fs && fs.TryCoords(cur, out int r, out int c))
                        return (i, r, c);
            return (-1, 0, 0);
        }

        private void RestoreFocus((int child, int row, int col) cap)
        {
            if (cap.child < 0) return;
            UIElement cell = null;
            if (cap.child < _content.Children.Count && _content.Children[cap.child] is FlowSheet fs && fs.RowCount > 0)
            {
                int r = System.Math.Min(cap.row, fs.RowCount - 1);
                int c = fs.Visitable(r, cap.col) ? cap.col : fs.LeftmostVisitable(r);
                if (c >= 0) cell = fs.CellAt(r, c);
            }
            cell = cell ?? _content.FirstFocusable();
            if (cell == null) return;
            var label = cell.GetLabelText();
            bool announce = label != _lastRestoreLabel;
            _lastRestoreLabel = label;
            Navigation.Focus(cell, announce);
        }
    }
}
