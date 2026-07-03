using System.Collections.Generic;
using RTAccess.Input;

namespace RTAccess.UI
{
    /// <summary>
    /// Windows-screen-reader-style navigation:
    /// - Tab / Shift-Tab traverse Panel tab-stops (descending through nested panels;
    ///   a List counts as one stop).
    /// - Arrows move within a List (or adjust a focused slider/dropdown).
    /// - Confirm activates the focused leaf.
    /// On entering a container, auto-focus its first leaf (recursively).
    /// </summary>
    public sealed class TraditionalNavigator : Navigator
    {
        private readonly TypeAheadSearch _search = new TypeAheadSearch();
        private readonly List<UIElement> _searchItems = new List<UIElement>();
        private UIElement _searchFocus; // where the search last landed (staleness check)
        private RTAccess.Screens.Screen _lastTypeaheadScreen; // screen typeahead last ran for; a change = a new screen took focus

        public TraditionalNavigator()
        {
            _search.OnNoMatch = text => Speak(Loc.T("search.no_match", new { text }), interrupt: true);
        }

        // ---- focus-path state + machinery (this navigator's model: the focus path within the
        //      screen, excluding the screen root itself — the screen name is announced separately).
        //      Focus mutations are silent: a navigation step snapshots the path, mutates it
        //      (including any recursive auto-descend), then announces the diff ONCE — never on each
        //      intermediate SetFocus. ----

        private readonly List<UIElement> Path = new List<UIElement>();

        // The screen we still owe an INITIAL-focus announcement for (set on screen entry, cleared once the
        // landing is announced or a screen takes over via Focus). Lets EnsureFocus deliver the landing even
        // when a screen's own build-time Attach established focus silently — the "read the thing you landed
        // on" after the screen name.
        private RTAccess.Screens.Screen _awaitingAnnounce;

        public override UIElement Current => Path.Count > 0 ? Path[Path.Count - 1] : null;

        /// <summary>Bind to a screen and set initial focus (silently). A NEW screen
        /// entry arms the initial-focus announcement (delivered by EnsureFocus once focus exists); a
        /// re-attach to the SAME screen (e.g. a content rebuild) doesn't re-arm it, so rebuilds stay quiet.</summary>
        public override void Attach(RTAccess.Screens.Screen screen)
        {
            if (!ReferenceEquals(screen, Screen)) _awaitingAnnounce = screen;
            Screen = screen;
            Path.Clear();
            ResetFocusPump(); // a (re)attach re-arms the focus pump, so it re-fires on the new landing
            if (screen != null) BuildInitialFocus();
        }

        /// <summary>Drop focus back to the screen's unfocused state — the same place Tab-off-the-end lands.
        /// Silent. On a <see cref="RTAccess.Screens.Screen.StartUnfocused"/> screen the keyboard returns to
        /// exploration and stays there (EnsureFocus won't re-seat it); on other screens EnsureFocus re-focuses
        /// next frame, so callers should only blur exploration-capable screens.</summary>
        public override void Blur()
        {
            Path.Clear();
            Screen?.SetFocusedChild(null);
        }

        /// <summary>
        /// Re-establish initial focus when the focused screen has focusable content but nothing is focused.
        /// Screens that build their content lazily (an empty shell at <see cref="Attach"/>, filled a frame
        /// later in OnUpdate) have nothing to focus when first attached; without this they'd sit unfocused
        /// until the user tabbed in. <see cref="BuildInitialFocus"/> bows out for StartUnfocused screens
        /// (exploration), so they stay unfocused. Called once per frame after the screen updates; announces
        /// the landing when focus mode owns the keyboard. A no-op once something is focused.
        /// </summary>
        public override void EnsureFocus()
        {
            if (Screen == null) return;
            // "No real focus" = nothing focused, OR focus stranded on a transparent Panel — which happens when
            // initial focus ran before a lazily-built screen filled its content panel (a Panel reports
            // focusable, so the descent stops on it). In both cases re-establish focus now that content exists.
            bool stranded = Current is Container c && c.Shape == ContainerShape.Panel;
            if (Current != null && !stranded)
            {
                // Focus is already established — but if a screen's build set it SILENTLY (build-time Attach)
                // and we still owe this screen's initial-focus announcement, deliver it now. This is the
                // landing read after the screen name (was missing for every Attach-only screen).
                if (ReferenceEquals(_awaitingAnnounce, Screen) && FocusMode.Active) AnnounceCurrent();
                return;
            }

            BuildInitialFocus();          // descends through the (now-populated) panels to a real leaf/cell
            var target = Current;
            Path.Clear();
            // Still no real target (content not built yet, or screen intentionally unfocused) — retry next
            // frame; don't announce or leave focus parked on a Panel.
            if (target == null || (target is Container tc && tc.Shape == ContainerShape.Panel)) return;
            // Re-seat through Focus so every intermediate cursor (e.g. a grid's cell) is set. Announce only
            // when focus mode owns the keyboard.
            Focus(target, announce: FocusMode.Active);
        }

        private static readonly List<UIElement> EmptyPath = new List<UIElement>();

        /// <summary>
        /// Announce the full focus path (e.g. when focus mode engages or the screen
        /// changes) — diff from empty, so container labels + the focused leaf are read,
        /// e.g. "Main Menu, Continue".
        /// </summary>
        public override void AnnounceCurrent()
        {
            _awaitingAnnounce = null; // announcing settles the initial-focus debt
            AnnounceDelta(EmptyPath);
        }

        /// <summary>Move focus to a specific element (e.g. a node just inserted into the tree) and announce
        /// the change. Queues (doesn't interrupt) so a preceding feedback line still plays.</summary>
        public override void Focus(UIElement target, bool announce = true)
        {
            if (target == null) return;
            _awaitingAnnounce = null; // the screen set focus itself — it owns the initial announce (or its silence)
            var snapshot = new List<UIElement>(Path);
            BuildPathTo(target);
            if (announce) AnnounceDelta(snapshot);
        }

        /// <summary>Append an element to the path; if it's a container, descend to the INNERMOST
        /// remembered/selected element. A tree NODE is only descended into when it's expanded AND
        /// actually remembers (or has selected) a deeper target — otherwise focus lands on the node
        /// itself (we never auto-dive into expanded nodes via the first-focusable fallback).</summary>
        private void AppendWithDescend(UIElement element)
        {
            if (element == null) return;
            Path.Add(element);
            DescendFrom(element);
        }

        /// <summary>Continue descending from an element ALREADY on the path to the innermost
        /// remembered/selected element (same rules as <see cref="AppendWithDescend"/>). Used after a
        /// Tab lands on a stop, so re-entering a tree restores the deep position, not the top node.</summary>
        private void DescendFrom(UIElement element)
        {
            while (true)
            {
                var container = element as Container;
                if (container == null) return;

                UIElement next;
                // A tree NODE (a tree-shaped container nested inside the tree; the tree ROOT always
                // exposes its children).
                bool isTreeNode = container.Shape == ContainerShape.Tree
                    && container.Parent is Container parent && parent.Shape == ContainerShape.Tree;
                if (isTreeNode)
                {
                    if (!container.Expanded) return; // collapsed → its children aren't navigable
                    next = RememberedOrSelected(container);
                    if (next == null) return;        // nothing remembered/selected → stay on the node
                }
                else
                {
                    next = RepresentativeChild(container);
                    if (next == null) return;
                }
                container.SetFocusedChild(next);
                Path.Add(next);
                element = next;
            }
        }

        /// <summary>
        /// Diff a pre-move snapshot against the settled path and speak the delta:
        /// newly-entered nodes in path order (descend/sibling), or just the new
        /// innermost element (ascend). Called once, after the move is complete.
        /// </summary>
        private void AnnounceDelta(List<UIElement> oldPath, bool interrupt = false)
        {
            // interrupt == true marks an actual focus MOVE (arrow/tab), not a screen-entry readout —
            // so it's where the game would play its control-hover sound. Current is already the
            // destination here, so its HoverSoundType picks the themed sound (Analog/Plastick/…) the
            // mouse would produce on that control (null ⇒ generic hover).
            if (interrupt) RTAccess.UiSound.Hover(Current?.HoverSoundType);

            int i = 0;
            while (i < oldPath.Count && i < Path.Count && oldPath[i] == Path[i]) i++;

            if (i < Path.Count)
            {
                var sb = new List<string>();
                for (int j = i; j < Path.Count; j++)
                {
                    // Skip a container whose label just duplicates the node beneath it (e.g. a
                    // "Game difficulty" section wrapping the "Game difficulty" control).
                    if (j + 1 < Path.Count)
                    {
                        var label = Path[j].GetLabelText();
                        if (!string.IsNullOrEmpty(label) && label == Path[j + 1].GetLabelText())
                            continue;
                    }
                    // The leaf cell of an associated-element table reads as the whole row (element focus +
                    // columns), so first-focus / focus-restore match arrowing. Its FlowSheet container is
                    // already a path node above, so the region label is omitted here.
                    var d = (j == Path.Count - 1 ? (Path[j].Parent as FlowSheet)?.ComposeAssociatedReadout(Path[j], false) : null)
                            ?? Path[j].GetFocusMessage().Resolve();
                    if (!string.IsNullOrEmpty(d)) sb.Add(d);
                }
                if (sb.Count > 0) Speak(string.Join(", ", sb), interrupt);
            }
            else if (Current != null)
            {
                var ar = (Current.Parent as FlowSheet)?.ComposeAssociatedReadout(Current, false);
                Speak(ar ?? Current.GetFocusMessage().Resolve(), interrupt); // ascended: announce the now-innermost focus
            }
        }

        /// <summary>Rebuild the focus path as the ancestor chain from the screen down to <paramref name="target"/>.</summary>
        private void BuildPathTo(UIElement target)
        {
            Path.Clear();
            if (target == null) return;
            var chain = new List<UIElement>();
            var e = target;
            while (e != null && e != Screen)
            {
                chain.Add(e);
                if (e.Parent != null) e.Parent.SetFocusedChild(e);
                e = e.Parent;
            }
            chain.Reverse();
            Path.AddRange(chain);
        }

        /// <summary>
        /// Ordered Tab-stops for the screen: descend through Panels; a List/Grid is a
        /// single stop (its current/first item); leaves under a Panel are stops.
        /// </summary>
        private List<UIElement> ComputeTabStops()
        {
            var stops = new List<UIElement>();
            if (Screen != null) AddStops(Screen, stops);
            return stops;
        }

        private static void AddStops(Container c, List<UIElement> stops)
        {
            if (c.Shape != ContainerShape.Panel)
            {
                var item = RepresentativeChild(c);
                if (item != null) stops.Add(item);
                return;
            }
            var children = c.Children;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is Container cc) AddStops(cc, stops);
                else if (child.CanFocus) stops.Add(child);
            }
        }

        private void BuildInitialFocus()
        {
            ClearSearch(announce: false); // a (re)attached screen starts with no live search
            // An unfocused screen (exploration) starts with nothing focused — arrows bubble to the overlay,
            // and Tab enters the HUD. But if it has remembered focus (you were in the HUD and a sub-screen
            // covered it, e.g. the log), restore it instead of dropping back to exploration. Tabbing out of
            // the HUD clears that remembered focus, so a fresh entry stays unfocused.
            if (Screen != null && Screen.StartUnfocused && Screen.FocusedChild == null) return;

            // Restore remembered focus (each container's FocusedChild), falling back to the
            // first focusable — descending to the INNERMOST remembered/selected element, trees
            // included (AppendWithDescend owns the rules). So returning to a screen lands back
            // exactly where you were, not on the outermost node.
            var first = RepresentativeChild(Screen);
            if (first == null) return;
            Screen.SetFocusedChild(first);
            AppendWithDescend(first);
        }

        public override bool OnInputJustPressed(InputAction action)
        {
            // A live type-ahead search reserves specific PHYSICAL KEYS — letters (the buffer), Space
            // (multi-word), Up/Down (walk matches), Home/End (jump), Escape (clear) — handled by
            // TickTypeahead's raw polling. Suppression is therefore KEY-based, not action-based: an
            // action FIRED FROM a reserved key is consumed here (so rebinding navigation to WASD never
            // steals those letters from typing, and a Space-bound tooltip stays typeable), while the
            // same action fired from any other binding (F1) passes through. Any non-reserved input
            // silently ends the search and acts normally — Enter activates the found item, Backspace
            // stays the secondary action, Tab leaves, etc.
            if (_search.IsSearchActive)
            {
                if (_searchFocus != null && Current != _searchFocus)
                    ClearSearch(announce: false); // focus moved under us → results are stale
                else if (action.Key == "ui.home" && _search.ResultCount > 0) { _search.JumpToFirstResult(); return true; }
                else if (action.Key == "ui.end" && _search.ResultCount > 0) { _search.JumpToLastResult(); return true; }
                else if (FiredFromSearchKey(action)) return true; // reserved key — TickTypeahead owns it
                else ClearSearch(announce: false);
            }

            switch (action.Key)
            {
                case "ui.up": return Arrow(NavDirection.Up);
                case "ui.down": return Arrow(NavDirection.Down);
                case "ui.left": return Arrow(NavDirection.Left);
                case "ui.right": return Arrow(NavDirection.Right);
                case "ui.next": return Tab(1);
                case "ui.prev": return Tab(-1);
                // Ctrl+Up/Down jump between FlowSheet regions; ignored (bubble) when not in a sheet.
                case "ui.home": return JumpEdge(first: true);
                case "ui.end": return JumpEdge(first: false);
                case "ui.regionPrev": return Current?.Parent is FlowSheet sp && RegionJump(sp, -1);
                case "ui.regionNext": return Current?.Parent is FlowSheet sn && RegionJump(sn, 1);
                case "ui.activate":
                    // Nothing focused → don't consume (chord shadowing normally routes Enter to
                    // explore.interact before this can happen; the guard is belt-and-braces).
                    // A cell with no action of its own falls through to its row's associated element (a table
                    // value cell → the row's radio/control); a cell that owns an action keeps priority.
                    if (Current == null) return false;
                    if (!TryActivate(Current)) TryActivate(Associated(Current));
                    return true;
                case "ui.secondary":
                    if (Current != null && !TryContext(Current)) TryContext(Associated(Current));
                    return true;
                case "ui.back":
                    // Screen-level back/close (e.g. Settings → Close). Consume only if the screen handles it.
                    return Screen != null && Screen.InvokeAction(ActionIds.Back);
                case "ui.tooltip":
                case "ui.tooltip.space":
                {
                    // Nothing focused → don't consume, and (for Space) don't claim: the arbitration patch then
                    // lets the game's Space through as Pause / End-turn (ui.tooltip.space is YieldsWhenUnfocused).
                    if (Current == null) return false;
                    OpenTooltipOrLinks(Current);
                    return true;
                }
                default:
                    return false; // not a nav key → bubble to globals
            }
        }

        // Space on an element offers everything it can drill into: its own tooltip (the brick document)
        // PLUS any inline glossary <link> targets in its text. One target → open it straight away (the
        // common item case); several → a menu page in the tooltip reader (its own tooltip first, then
        // each link). The menu lives inside the reader's own drill stack, so selecting/backing keeps
        // focus and announces once (no separate chooser screen).
        private void OpenTooltipOrLinks(UIElement el)
        {
            if (el == null) { Speak(Loc.T("nav.no_tooltip")); return; }

            // The element's own tooltip: plain description text, or — for icon-only in-game elements
            // (action-bar slots, inventory/equipment items) whose name + stats live in a rich brick TEMPLATE
            // — that template read to text via TooltipReader.
            var body = el.GetTooltipText();
            if (string.IsNullOrWhiteSpace(body))
            {
                var tpl = el.GetTooltipTemplate();
                if (tpl != null) body = RTAccess.Accessibility.TooltipReader.GetFull(tpl);
            }

            // Inline glossary terms embedded in the element's game text (dialogue cues wrap terms like
            // "psyker" / "Golden Throne" as <link>s), resolved to their definitions.
            var links = RTAccess.Accessibility.GlossaryLinks.Gather(el);

            // Extra rendered sections the element carries (e.g. an inventory item's compare-vs-equipped cards).
            var sections = el.GetTooltipSections();
            bool hasSections = sections != null && sections.Count > 0;

            if (links.Count == 0 && !hasSections)
            {
                // Nothing extra → the single-tooltip case (unchanged): open the body directly, or say none.
                if (string.IsNullOrWhiteSpace(body)) { Speak(Loc.T("nav.no_tooltip")); return; }
                RTAccess.Screens.TooltipScreen.Open(el.GetLabelText(), body);
                return;
            }

            // A drill chooser: the element's own tooltip first (if any), then its extra sections, then terms.
            var items = new List<(string, string)>();
            if (!string.IsNullOrWhiteSpace(body))
                items.Add((el.GetLabelText() ?? Loc.T("nav.details"), body));
            if (hasSections) items.AddRange(sections);
            foreach (var e in links) items.Add((e.Label, e.Body));
            // Title by the element (its own name) when it carries sections; glossary-only keeps "References".
            RTAccess.Screens.DrillMenuScreen.Open(
                hasSections ? (el.GetLabelText() ?? Loc.T("nav.references")) : Loc.T("nav.references"), items);
        }

        private bool Arrow(NavDirection dir)
        {
            if (Current == null) return false;

            // A focused slider/dropdown advertises increase/decrease; Left/Right invoke them,
            // then announce just the new value (not the whole control/path). Takes priority over a
            // tree's collapse/expand so adjusting a setting still works inside the tree.
            if (dir == NavDirection.Left && Current.InvokeAction(ActionIds.Decrease)) { Speak(Current.GetStateMessage().Resolve(), interrupt: true); return true; }
            if (dir == NavDirection.Right && Current.InvokeAction(ActionIds.Increase)) { Speak(Current.GetStateMessage().Resolve(), interrupt: true); return true; }

            // Inside a horizontal sub-list (e.g. a keybinding Bar): Left/Right move between its items.
            if ((dir == NavDirection.Left || dir == NavDirection.Right)
                && Current.Parent != null && Current.Parent.Shape == ContainerShape.HorizontalList)
            {
                var snap = new List<UIElement>(Path);
                if (Move(dir)) { AnnounceDelta(snap, interrupt: true); return true; }
                // at the bar's edge → fall through so the tree can collapse/ascend
            }

            // Table (grid): 2-D cursor with Excel-style header+cell announce.
            if (Current.Parent is Table grid) return GridArrow(dir, grid);

            // FlowSheet (regioned grid): 2-D cursor across stacked regions, region/header announce.
            if (Current.Parent is FlowSheet sheet) return FlowArrow(dir, sheet);

            // Treeview region: Up/Down over expanded nodes (DFS); Right/Left expand/collapse/descend/ascend.
            var root = TreeRootOf(Current);
            if (root != null) return TreeArrow(dir, root);

            var snapshot = new List<UIElement>(Path);
            if (!Move(dir)) return false;
            AnnounceDelta(snapshot, interrupt: true);
            return true;
        }

        // ---- Treeview navigation (ContainerShape.Tree) ----

        // The topmost Tree-shaped ancestor (the tab-stop the tree lives in), or null if not in a tree.
        private static Container TreeRootOf(UIElement e)
        {
            Container root = null;
            var c = (e as Container) ?? e?.Parent;
            while (c != null) { if (c.Shape == ContainerShape.Tree) root = c; c = c.Parent; }
            return root;
        }

        // The visible-list node representing the focused element: itself if it's a direct child of a
        // tree group (a group node or leaf), else the ancestor that is (e.g. the Bar holding a focused
        // keybinding slot).
        private static UIElement TreeNodeOf(UIElement e, Container root)
        {
            var cur = e;
            while (cur != null && cur != root)
            {
                if (cur.Parent != null && cur.Parent.Shape == ContainerShape.Tree) return cur;
                cur = cur.Parent;
            }
            return e;
        }

        // Nodes currently visible (DFS pre-order): a tree group contributes itself, then — only if
        // expanded — its children, recursively. A non-tree container (a Bar) is a single node.
        private static void CollectVisible(Container c, List<UIElement> outList)
        {
            foreach (var child in c.Children)
            {
                if (child.CanFocus) outList.Add(child);
                if (child is Container cc && cc.Shape == ContainerShape.Tree && cc.Expanded)
                    CollectVisible(cc, outList);
            }
        }

        // Focus a tree node; if it's a non-tree container (a Bar), descend to its first actionable leaf.
        private void FocusTreeNode(UIElement node)
        {
            BuildPathTo(node);
            if (node is Container c && c.Shape != ContainerShape.Tree)
            {
                var first = c.FirstFocusable();
                if (first != null) { c.SetFocusedChild(first); AppendWithDescend(first); }
            }
        }

        private bool TreeArrow(NavDirection dir, Container root)
        {
            var node = TreeNodeOf(Current, root);
            var group = node as Container;
            bool isGroup = group != null && group.Shape == ContainerShape.Tree && group.Expandable;

            if (dir == NavDirection.Right)
            {
                if (isGroup && !group.Expanded)
                {
                    group.Expand();
                    // A lazy drill-in can resolve to nothing (e.g. a skill whose glossary key has no
                    // entry). Don't leave a silent empty-expanded node — recollapse and say so.
                    if (group.Children.Count == 0) { group.Collapse(); Speak(Loc.T("nav.no_details"), interrupt: true); }
                    else Speak(node.GetFocusMessage().Resolve(), interrupt: true);
                    return true;
                }
                if (!(isGroup && group.Expanded)) return true; // leaf/empty → nothing to descend into
                dir = NavDirection.Down; // expanded group → descend = step to first child (next visible)
            }
            else if (dir == NavDirection.Left)
            {
                if (isGroup && group.Expanded) { group.Collapse(); Speak(node.GetFocusMessage().Resolve(), interrupt: true); return true; }
                var parent = node.Parent; // ascend to the enclosing group
                if (parent != null && parent != root && parent.CanFocus)
                {
                    var snap = new List<UIElement>(Path);
                    FocusTreeNode(parent);
                    AnnounceDelta(snap, interrupt: true);
                }
                return true;
            }

            var vis = new List<UIElement>();
            CollectVisible(root, vis);
            int idx = vis.IndexOf(node);
            if (idx < 0) return true;
            int ni = dir == NavDirection.Down ? idx + 1 : idx - 1;
            if (ni < 0 || ni >= vis.Count) return true; // at an end; consume
            var snapshot = new List<UIElement>(Path);
            FocusTreeNode(vis[ni]);
            AnnounceDelta(snapshot, interrupt: true);
            return true;
        }

        // Move the cell cursor in a Table and announce Excel-style. Headers resolve by proximity
        // (nearest Column above / Row to the left / Group up column 0). We speak: the group when it
        // changes (crossing into a new block), the column header when the column changed, the row
        // header when the row changed, then the cell ("blank" if empty). A header cell just reads
        // itself (its own text is the header), plus the group if that changed.
        private bool GridArrow(NavDirection dir, Table table)
        {
            if (!table.TryCoords(Current, out int r, out int c)) return false;
            int nr = r, nc = c;
            switch (dir)
            {
                case NavDirection.Down: nr++; break;
                case NavDirection.Up: nr--; break;
                case NavDirection.Right: nc++; break;
                case NavDirection.Left: nc--; break;
            }
            var next = table.CellAt(nr, nc);
            if (next == null) return true; // edge → consume (no wrap)

            BuildPathTo(next);
            var parts = new List<string>();
            var role = table.RoleAt(nr, nc);

            var group = table.GroupText(nr, nc);
            if (group != table.GroupText(r, c) && !string.IsNullOrEmpty(group) && role != CellRole.Group)
                parts.Add(group); // entered a new block (the group cell itself already reads the name)

            if (role == CellRole.None) // data cell → tack on the axis header(s) that changed
            {
                if (nc != c) { var h = table.ColumnHeaderText(nr, nc); if (!string.IsNullOrEmpty(h)) parts.Add(h); }
                if (nr != r) { var h = table.RowHeaderText(nr, nc); if (!string.IsNullOrEmpty(h)) parts.Add(h); }
            }

            if (!string.IsNullOrEmpty(next.Role)) parts.Add(next.Role); // e.g. an actionable cell → "button"
            var cell = next.GetLabelText();
            parts.Add(string.IsNullOrWhiteSpace(cell) ? "blank" : cell);

            RTAccess.UiSound.Hover(next.HoverSoundType); // a focus move, like AnnounceDelta's interrupt path
            Speak(string.Join(", ", parts), interrupt: true);
            return true;
        }

        // Move the cell cursor in a FlowSheet. Left/Right step to the next visitable cell in the row;
        // Up/Down step a row, landing on the same column or — if it isn't visitable there (a narrower
        // region below) — the furthest-left visitable cell of that row. Announce: the region when it
        // changes (entry), the column header when the column changed, the row label when the row changed
        // (unless we're on the label cell itself), then the cell ("blank" if empty).
        private bool FlowArrow(NavDirection dir, FlowSheet sheet)
        {
            if (!sheet.TryCoords(Current, out int r, out int c)) return false;
            int nr = r, nc = c;
            UIElement next = null;
            switch (dir)
            {
                case NavDirection.Right:
                    for (int cc = c + 1; cc < sheet.ColCount; cc++)
                        if (sheet.Visitable(r, cc)) { nc = cc; next = sheet.CellAt(r, cc); break; }
                    break;
                case NavDirection.Left:
                    for (int cc = c - 1; cc >= 0; cc--)
                        if (sheet.Visitable(r, cc)) { nc = cc; next = sheet.CellAt(r, cc); break; }
                    break;
                case NavDirection.Down:
                case NavDirection.Up:
                    // Scan past rows with nothing currently landable (e.g. a list row whose only
                    // element is an unfocusable hidden button) — stopping on one would wall off the
                    // rest of the sheet. Prefer the current column in the landing row, else leftmost.
                    int step = dir == NavDirection.Down ? 1 : -1;
                    for (nr = r + step; nr >= 0 && nr < sheet.RowCount; nr += step)
                    {
                        if (sheet.Visitable(nr, c)) { nc = c; next = sheet.CellAt(nr, c); break; }
                        int lc = sheet.LeftmostVisitable(nr);
                        if (lc >= 0) { nc = lc; next = sheet.CellAt(nr, lc); break; }
                    }
                    break;
            }
            if (next == null) return true; // no neighbour that way → consume (no wrap)

            BuildPathTo(next);
            AnnounceFlowCell(sheet, r, c, nr, nc, next, regionEntry: false);
            return true;
        }

        // Ctrl+Up/Down: jump to the previous/next region's first landable cell.
        private bool RegionJump(FlowSheet sheet, int dir)
        {
            if (!sheet.TryCoords(Current, out int r, out int c)) return false;
            var target = sheet.StepRegion(sheet.RegionAt(r), dir);
            if (target == null) return true; // no region that way → consume
            int fr = sheet.RegionFirstRow(target);
            int fc = fr >= 0 ? sheet.LeftmostVisitable(fr) : -1;
            if (fc < 0) return true;
            var cell = sheet.CellAt(fr, fc);
            BuildPathTo(cell);
            AnnounceFlowCell(sheet, r, c, fr, fc, cell, regionEntry: true);
            return true;
        }

        // Home/End: jump to the first/last item of the structure the focus is in — a FlowSheet's very
        // first/last visitable cell (regardless of region), a tree's first/last node AT THE CURRENT
        // DEPTH (the focused node's siblings), or a list's first/last item. Outside any of those the
        // key is consumed and does nothing.
        private bool JumpEdge(bool first)
        {
            if (Current == null) return false;

            if (Current.Parent is FlowSheet sheet)
            {
                if (!sheet.TryCoords(Current, out int r, out int c)) return true;
                int nr = -1, nc = -1;
                if (first)
                {
                    for (int row = 0; row < sheet.RowCount && nr < 0; row++)
                    {
                        int col = sheet.LeftmostVisitable(row);
                        if (col >= 0) { nr = row; nc = col; }
                    }
                }
                else
                {
                    for (int row = sheet.RowCount - 1; row >= 0 && nr < 0; row--)
                        for (int col = sheet.ColCount - 1; col >= 0; col--)
                            if (sheet.Visitable(row, col)) { nr = row; nc = col; break; }
                }
                if (nr < 0 || (nr == r && nc == c)) return true;
                var cell = sheet.CellAt(nr, nc);
                if (cell == null) return true;
                BuildPathTo(cell);
                AnnounceFlowCell(sheet, r, c, nr, nc, cell, regionEntry: false);
                return true;
            }

            var root = TreeRootOf(Current);
            if (root != null)
            {
                var node = TreeNodeOf(Current, root);
                var parent = node.Parent ?? root;
                var target = first ? parent.FirstFocusable() : parent.LastFocusable();
                if (target == null || target == node) return true;
                var snap = new List<UIElement>(Path);
                FocusTreeNode(target);
                AnnounceDelta(snap, interrupt: true);
                return true;
            }

            var container = Current.Parent;
            if (container != null
                && (container.Shape == ContainerShape.VerticalList || container.Shape == ContainerShape.HorizontalList))
            {
                var target = first ? container.FirstFocusable() : container.LastFocusable();
                if (target == null || target == Current) return true;
                var snapshot = new List<UIElement>(Path);
                int idx = Path.IndexOf(Current);
                if (idx >= 0) Path.RemoveRange(idx, Path.Count - idx);
                AppendWithDescend(target);
                container.SetFocusedChild(target);
                AnnounceDelta(snapshot, interrupt: true);
                return true;
            }

            return true; // focused but in no jumpable structure — consume, do nothing
        }

        // Run an element's primary action with its sound + in-place reannounce; returns whether it handled it.
        private bool TryActivate(UIElement e)
        {
            if (e == null || !e.InvokeAction(ActionIds.Activate)) return false;
            // Themed OwlcatButtons (main menu, window chrome) carry a click sound-TYPE the game replays
            // via PlayButtonClickSound(type); everything else keeps the blueprint-typed ActivateSound.
            // An element uses one or the other, so the type takes precedence when present.
            if (e.ClickSoundType is Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum ct)
                RTAccess.UiSound.Click(ct);
            else
            {
                var sound = e.ActivateSound; // game plays this in the view handler we bypass
                if (sound != null) RTAccess.UiSound.Play(sound);
            }
            if (e.ReannounceOnActivate) Speak(e.GetStateMessage().Resolve(), interrupt: true);
            return true;
        }

        private bool TryContext(UIElement e)
        {
            if (e == null || !e.InvokeAction(ActionIds.Context)) return false;
            if (e.ReannounceOnContext) Speak(e.GetStateMessage().Resolve(), interrupt: true);
            return true;
        }

        // The interactive element a cell defers to — its row's associated element in a table — or null.
        private static UIElement Associated(UIElement cell)
            => cell?.Parent is FlowSheet fs ? fs.AssociatedElementForCell(cell) : null;

        private void AnnounceFlowCell(FlowSheet sheet, int r, int c, int nr, int nc, UIElement next, bool regionEntry)
        {
            var parts = new List<string>();
            var region = sheet.RegionAt(nr);
            bool entered = regionEntry || region != sheet.RegionAt(r);

            // A bar reads each cell as its own full focus message (so a radio button announces as one), with
            // the group label only on entry and only when it isn't just the control's own label repeated.
            if (region != null && region.ReadCellFocusMessage)
            {
                if (entered && !string.IsNullOrEmpty(region.Label) && region.Label != next.GetLabelText())
                    parts.Add(region.Label);
                var msg = next.GetFocusMessage().Resolve();
                parts.Add(string.IsNullOrWhiteSpace(msg) ? "blank" : msg);
                RTAccess.UiSound.Hover(next.HoverSoundType);
                Speak(string.Join(", ", parts), interrupt: true);
                return;
            }

            // An associated-element table: on a ROW change (up/down, or region entry) announce the row's
            // element focus + the column data (see FlowSheet.ComposeAssociatedReadout). Same-row moves
            // (left/right) fall through to the normal header+value framing below, so column headers still
            // read on column change.
            if (nr != r)
            {
                var assoc = sheet.ComposeAssociatedReadout(next, withRegionLabel: entered);
                if (assoc != null) { RTAccess.UiSound.Hover(next.HoverSoundType); Speak(assoc, interrupt: true); return; }
            }

            if (entered && region != null && !string.IsNullOrEmpty(region.Label))
                parts.Add(region.Label + ", " + region.TypeName);
            if (nc != c) { var h = sheet.ColumnHeader(nr, nc); if (!string.IsNullOrEmpty(h)) parts.Add(h); }
            if (nr != r && nc != 0) { var rl = sheet.RowLabel(nr); if (!string.IsNullOrEmpty(rl)) parts.Add(rl); }
            if (!string.IsNullOrEmpty(next.Role)) parts.Add(next.Role);
            var text = next.GetLabelText();
            parts.Add(string.IsNullOrWhiteSpace(text) ? "blank" : text);
            RTAccess.UiSound.Hover(next.HoverSoundType); // a focus move
            Speak(string.Join(", ", parts), interrupt: true);
        }

        private bool Tab(int step)
        {
            var stops = ComputeTabStops();
            if (stops.Count == 0) return false;
            // Current may be deeper than its tab-stop (a node inside a tree/list, where the stop is an
            // ancestor representative), so walk up to the nearest element that IS a stop — otherwise
            // IndexOf returns -1 and Tab wrongly jumps to the first stop.
            int idx = -1;
            for (var e = Current; e != null && idx < 0; e = e.Parent)
                idx = stops.IndexOf(e);

            // Unfocused (e.g. exploration): Tab enters the HUD at the first/last stop.
            if (idx < 0)
            {
                var snap = new List<UIElement>(Path);
                var entry = stops[step >= 0 ? 0 : stops.Count - 1];
                BuildPathTo(entry);
                DescendFrom(entry); // land on the innermost remembered/selected element, not the stop
                AnnounceDelta(snap, interrupt: true);
                return true;
            }

            int ni = idx + step;
            if (ni < 0 || ni >= stops.Count)
            {
                // On an unfocused-capable screen, tabbing off either end drops back to the unfocused
                // state (exploration owns the arrows again) rather than sticking at the edge.
                if (Screen != null && Screen.StartUnfocused)
                {
                    Path.Clear();
                    Screen.SetFocusedChild(null); // truly unfocused → a later re-entry stays in exploration
                    if (!string.IsNullOrEmpty(Screen.ScreenName)) Speak(Screen.ScreenName, interrupt: true);
                    return true;
                }
                if (Screen != null && Screen.Wrap)
                    ni = ((ni % stops.Count) + stops.Count) % stops.Count; // wrap
                else
                    return true; // at the end; consume, no wrap
            }
            var snapshot = new List<UIElement>(Path);
            BuildPathTo(stops[ni]);
            DescendFrom(stops[ni]); // land on the innermost remembered/selected element, not the stop
            AnnounceDelta(snapshot, interrupt: true);
            return true;
        }

        // Arrow movement within list-shaped containers, spilling into a same-shape parent.
        private bool Move(NavDirection dir)
        {
            var movingFrom = Current;
            var container = movingFrom?.Parent;
            while (container != null)
            {
                var next = container.GetNeighbor(movingFrom, dir);
                if (next != null)
                {
                    int idx = Path.IndexOf(movingFrom);
                    if (idx >= 0) Path.RemoveRange(idx, Path.Count - idx);
                    AppendWithDescend(next);
                    container.SetFocusedChild(next);
                    return true;
                }
                var parent = container.Parent;
                if (parent != null && parent.Shape == container.Shape)
                {
                    movingFrom = container;
                    container = parent;
                    continue;
                }
                return false;
            }
            return false;
        }

        // ---- Type-ahead search (engine in TypeAheadSearch; this is the glue) ----

        /// <summary>Per-frame: the search's whole keyboard surface, RAW (binding-independent). Letters
        /// (and Space, once a buffer exists) come from Unity's <c>inputString</c>; while a search is
        /// live, Up/Down walk the matches, Home/End jump, Escape clears — by physical key, so they work
        /// the same no matter what actions those keys are bound to (the navigator suppresses any action
        /// a reserved key fires; see <see cref="FiredFromSearchKey"/>). Ctrl/Alt chords are hotkeys,
        /// never search input.</summary>
        public override void TickTypeahead()
        {
            if (Screen == null || Screen.CapturesRawInput || !Screen.AllowsTypeahead
                || !FocusMode.Active || Current == null)
            {
                if (_search.IsSearchActive || _search.HasBuffer) ClearSearch(announce: false);
                _lastTypeaheadScreen = Screen;
                return;
            }

            // A different screen just took focus (e.g. opened by the very key the user pressed — a letter
            // like Y for inspect). Unity's inputString still holds that key and the new screen auto-focuses
            // an element, so without this it would type the opening key into a fresh search. Swallow this
            // frame's typed input on the changeover.
            if (!ReferenceEquals(Screen, _lastTypeaheadScreen))
            {
                _lastTypeaheadScreen = Screen;
                if (_search.IsSearchActive || _search.HasBuffer) ClearSearch(announce: false);
                return;
            }

            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftControl) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightControl)
                || UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftAlt) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightAlt))
                return;

            bool shift = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
            if (_search.IsSearchActive && !shift)
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape)) { ClearSearch(announce: true); return; }
                if (_search.ResultCount > 0)
                {
                    if (TickResultArrows()) return; // Up/Down with OS-typematic auto-repeat
                    // (Home/End ride the ui.home/ui.end ACTIONS — intercepted in OnInputJustPressed —
                    // so they follow the user's bindings.)
                }
            }
            else
            {
                _searchHeldDir = 0; // search ended / modifier down → forget the held arrow
            }

            var typed = UnityEngine.Input.inputString;
            if (string.IsNullOrEmpty(typed)) return;

            foreach (var c in typed)
            {
                if (char.IsLetter(c)) TypeChar(c);
                else if (c == ' ' && _search.HasBuffer) TypeChar(c); // multi-word; a lone Space stays tooltip
            }
        }

        private int _searchHeldDir;    // -1 = Up held, +1 = Down held, 0 = none (result auto-repeat)
        private float _searchRepeatIn; // seconds until the held arrow repeats

        // Up/Down over the results with OS-style typematic repeat (raw GetKeyDown never repeats, and
        // the game's lists run long): step on the press, then after the OS initial delay repeat at the
        // OS repeat interval while held — same feel as TileStep's cursor stepping.
        private bool TickResultArrows()
        {
            int dir = UnityEngine.Input.GetKey(UnityEngine.KeyCode.UpArrow) ? -1
                : UnityEngine.Input.GetKey(UnityEngine.KeyCode.DownArrow) ? 1 : 0;
            if (dir == 0) { _searchHeldDir = 0; return false; }

            if (dir != _searchHeldDir)
            {
                _searchHeldDir = dir;
                _searchRepeatIn = RTAccess.Input.OsKeyboard.InitialDelay;
                _search.NavigateResults(dir);
                return true;
            }

            _searchRepeatIn -= UnityEngine.Time.unscaledDeltaTime;
            if (_searchRepeatIn <= 0f)
            {
                _searchRepeatIn = RTAccess.Input.OsKeyboard.RepeatInterval;
                _search.NavigateResults(dir);
            }
            return true;
        }

        // Did this action fire from a key the live search reserves? Letters always type (Shift just
        // capitalises); Space/arrows/Home/End/Escape count only unmodified. Ctrl/Alt chords are never
        // search input. Checks Held (not JustPressed) so a Repeating action's auto-repeat dispatches
        // are suppressed too while the key stays down.
        private static bool FiredFromSearchKey(InputAction action)
        {
            foreach (var b in action.Bindings)
            {
                if (!(b is KeyboardBinding kb) || kb.Ctrl || kb.Alt || !kb.Held()) continue;
                if (kb.Key >= UnityEngine.KeyCode.A && kb.Key <= UnityEngine.KeyCode.Z) return true;
                if (!kb.Shift && (kb.Key == UnityEngine.KeyCode.Space
                    || kb.Key == UnityEngine.KeyCode.UpArrow || kb.Key == UnityEngine.KeyCode.DownArrow
                    || kb.Key == UnityEngine.KeyCode.Escape)) return true;
            }
            return false;
        }

        private void TypeChar(char c)
        {
            RebuildSearchScope(); // fresh snapshot — the UI is live, items may have changed since last key
            if (_searchItems.Count == 0) return;
            _search.AddChar(c);
            _search.Search(_searchItems.Count, i => _searchItems[i].GetLabelText(), SearchFocusResult);
        }

        // The searchable scope = the tab-stop-level region the focus lives in: ascend until the parent
        // is the screen or a Panel (the same carve-up ComputeTabStops uses). A tree searches its VISIBLE
        // nodes (collapsed branches stay hidden, matching what arrows can reach); anything else (list,
        // bar, table, flowsheet) searches its focusable leaves.
        private void RebuildSearchScope()
        {
            _searchItems.Clear();
            var scope = (UIElement)Current;
            if (scope == null) return;
            while (scope.Parent != null && scope.Parent != Screen && scope.Parent.Shape != ContainerShape.Panel)
                scope = scope.Parent;
            if (scope is Container c)
            {
                if (c.Shape == ContainerShape.Tree) CollectVisible(c, _searchItems);
                else CollectSearchLeaves(c, _searchItems);
            }
            else if (scope.CanFocus) _searchItems.Add(scope);
        }

        private static void CollectSearchLeaves(Container c, List<UIElement> outList)
        {
            foreach (var child in c.Children)
            {
                if (child is Container cc)
                {
                    if (cc.Shape == ContainerShape.Tree)
                    {
                        if (cc.CanFocus) outList.Add(cc);
                        if (cc.Expanded) CollectVisible(cc, outList);
                    }
                    else CollectSearchLeaves(cc, outList);
                }
                else if (child.CanFocus) outList.Add(child);
            }
        }

        // Land on a result: a real focus move (path + remembered focus), announced interrupting — typing
        // is rapid, each refinement should cut off the last. Land like arrow navigation does
        // (FocusTreeNode): a matched GROUP node is the result itself — never auto-descend into its
        // remembered child, which would re-announce a slider the user just visited; only a Bar
        // descends to its first leaf.
        private void SearchFocusResult(int index)
        {
            if (index < 0 || index >= _searchItems.Count) return;
            var target = _searchItems[index];
            var snap = new List<UIElement>(Path);
            FocusTreeNode(target);
            _searchFocus = Current;
            AnnounceDelta(snap, interrupt: true);
        }

        private void ClearSearch(bool announce)
        {
            bool had = _search.IsSearchActive || _search.HasBuffer;
            _search.Clear();
            _searchItems.Clear();
            _searchFocus = null;
            _searchHeldDir = 0;
            if (announce && had) Speak(Loc.T("search.cleared"), interrupt: true);
        }
    }
}
