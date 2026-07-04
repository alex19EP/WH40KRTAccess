using System.Collections.Generic;
using RTAccess.UI.Graph;

namespace RTAccess.UI
{
    /// <summary>
    /// Compiles the retained <see cref="Container"/> tree of a screen into a <see cref="GraphRender"/> —
    /// the migration bridge that lets every existing screen run on the graph navigator unchanged. The
    /// tree stays the authoring format; this derives the navigation topology from the same geometry the
    /// old navigator consulted imperatively:
    ///
    ///  - Panels are structure: recursed through, contributing a context level when labeled.
    ///  - A non-panel container (list/tree/grid) is ONE Tab-stop; loose focusable leaves are their own stops.
    ///  - Lists become menu rows (a horizontal list/Bar = one row; nested same-shape lists spill inline).
    ///  - Trees flatten to their VISIBLE nodes: a tree node is a builder GROUP (header = parent of its
    ///    children) whose expansion is the retained Container's state, driven through OnExpand/OnCollapse —
    ///    expand/collapse rebuilds the graph and reconciliation keeps focus on the header.
    ///  - FlowSheet / Table cells become raw nodes wired by the sheet's own Visitable/CellAt rules, so
    ///    cell topology matches the old FlowArrow/GridArrow scans exactly.
    ///
    /// Node identity is <see cref="ControlId.ForObject"/> on the element instance: the retained tree keeps
    /// instances across reflows, and when a screen rebuilds its elements wholesale the nearest-survivor
    /// reconciliation takes over (an improvement on the old dropped-focus behavior).
    /// </summary>
    public static class TreeGraphAdapter
    {
        public static ControlId IdFor(UIElement e) => ControlId.ForObject(e);

        /// <summary>Build the screen's graph, or null when it has no focusable content yet.</summary>
        public static GraphRender Build(Screens.Screen screen)
        {
            if (screen == null) return null;
            var b = new GraphBuilder();
            EmitPanel(b, screen);
            return b.Build();
        }

        // One composed announcement per element (its GetFocusMessage, resolved live) — the adapter keeps
        // today's readouts verbatim; graph-native screens supply structured per-part lists (with Live
        // flags) instead.
        private static NodeVtable Vt(UIElement e) => new NodeVtable
        {
            Announcements = new[] { new NodeAnnouncement(() => e.GetFocusMessage().Resolve()) },
            SearchText = e.GetLabelText,
            SpeaksOwnPosition = true, // GetFocusMessage already appends the container's "n of m"
        };

        // A panel (or the screen root): recurse, each non-panel child container = a stop, loose leaves =
        // leaf stops. A labeled panel contributes a context level (it sat on the old announce path).
        private static void EmitPanel(GraphBuilder b, Container panel)
        {
            bool labeled = !string.IsNullOrEmpty(panel.Label) && !(panel is Screens.Screen);
            if (labeled) b.PushContext(panel.Label);
            foreach (var child in panel.Children)
            {
                if (child is Container c)
                {
                    if (c.Shape == ContainerShape.Panel) { EmitPanel(b, c); continue; }
                    if (c.FirstFocusable() == null && !c.CanFocus) continue; // empty structure — nothing to land on
                    b.BeginStop(c);
                    EmitStop(b, c);
                }
                else if (child.CanFocus)
                {
                    b.BeginStop(child);
                    b.AddItem(IdFor(child), Vt(child));
                }
            }
            if (labeled) b.PopContext();
        }

        private static void EmitStop(GraphBuilder b, Container c)
        {
            bool pushed = PushStopContext(b, c);
            switch (c.Shape)
            {
                case ContainerShape.Tree:
                    EmitTreeRows(b, c);
                    break;
                case ContainerShape.Grid:
                    if (c is FlowSheet sheet) EmitFlowSheet(b, sheet);
                    else if (c is Table table) EmitTable(b, table);
                    else EmitListRows(b, c); // unknown grid — degrade to a list
                    break;
                case ContainerShape.HorizontalList:
                    EmitBarRow(b, c);
                    break;
                default: // VerticalList
                    EmitListRows(b, c);
                    break;
            }
            if (pushed) b.PopContext();
        }

        // Context for a stop container mirrors what its focus announcements contributed on the old path:
        // label (+ "list" for list shapes — Container.GetFocusAnnouncements' role word).
        private static bool PushStopContext(GraphBuilder b, Container c)
        {
            if (string.IsNullOrEmpty(c.Label)) return false;
            string role = c.Shape == ContainerShape.VerticalList || c.Shape == ContainerShape.HorizontalList
                ? Loc.T("role.list") : null; // localized — the old path spoke RoleAnnouncement("list")
            b.PushContext(c.Label, role);
            return true;
        }

        // A vertical list: each focusable child is a row; a horizontal child (Bar) is one row of its
        // cells; a nested same-shape list spills inline (the old Move() walked up through same-shape
        // parents); a nested tree emits its visible rows inline.
        private static void EmitListRows(GraphBuilder b, Container list)
        {
            foreach (var child in list.Children)
            {
                if (!child.CanFocus && !(child is Container)) continue;
                if (child is Container cc)
                {
                    if (cc.Shape == ContainerShape.HorizontalList) { EmitBarRow(b, cc); continue; }
                    if (cc.Shape == ContainerShape.VerticalList) { EmitListRows(b, cc); continue; }
                    if (cc.Shape == ContainerShape.Tree) { EmitTreeRows(b, cc); continue; }
                    if (cc.Shape == ContainerShape.Panel) { EmitListRows(b, cc); continue; }
                }
                if (child.CanFocus) b.AddItem(IdFor(child), Vt(child));
            }
        }

        private static void EmitBarRow(GraphBuilder b, Container bar)
        {
            bool any = false;
            foreach (var cell in bar.Children) if (cell.CanFocus) { any = true; break; }
            if (!any) return;
            b.StartRow();
            foreach (var cell in bar.Children)
                if (cell.CanFocus) b.AddItem(IdFor(cell), Vt(cell));
            b.EndRow();
        }

        // The visible tree rows (DFS pre-order over expanded branches): a tree node becomes a GROUP —
        // its header is the parent of its children, so the announcer's path diff matches the old
        // retained-path behavior exactly (descending from a header re-announces nothing). Expansion is
        // the retained Container's state, and the engine's tree operations drive it through the
        // OnExpand/OnCollapse overrides. A non-tree container node (a Bar in the tree) is one row.
        private static void EmitTreeRows(GraphBuilder b, Container root)
        {
            foreach (var child in root.Children)
            {
                if (child is Container cc && cc.Shape == ContainerShape.Tree)
                {
                    if (!child.CanFocus) continue;
                    var vt = Vt(child);
                    var container = cc;
                    vt.OnExpand = () => container.Expand();
                    vt.OnCollapse = () => container.Collapse();
                    vt.SpeaksOwnExpansion = true; // GetFocusMessage already reads expanded/collapsed
                    b.BeginGroup(IdFor(child), vt, expanded: cc.Expanded);
                    EmitTreeRows(b, cc);
                    b.EndGroup();
                }
                else if (child is Container bar && bar.Shape == ContainerShape.HorizontalList)
                {
                    EmitBarRow(b, bar);
                }
                else if (child.CanFocus)
                {
                    b.AddItem(IdFor(child), Vt(child));
                }
            }
        }

        // FlowSheet: a raw node per visitable cell, wired with the same scans FlowArrow used — left/right
        // to the next visitable cell in the row; up/down prefer the same column, else the landing row's
        // leftmost visitable, skipping rows with nothing landable. Region jumps ride RegionKey.
        private static void EmitFlowSheet(GraphBuilder b, FlowSheet sheet)
        {
            for (int r = 0; r < sheet.RowCount; r++)
            {
                var region = sheet.RegionAt(r);
                b.SetRegion(region);
                for (int c = 0; c < sheet.ColCount; c++)
                {
                    if (!sheet.Visitable(r, c)) continue;
                    var cell = sheet.CellAt(r, c);
                    if (cell == null) continue;
                    b.AddNode(IdFor(cell), Vt(cell));
                }
            }
            b.SetRegion(null);

            for (int r = 0; r < sheet.RowCount; r++)
                for (int c = 0; c < sheet.ColCount; c++)
                {
                    if (!sheet.Visitable(r, c)) continue;
                    var cell = sheet.CellAt(r, c);
                    if (cell == null) continue;
                    var id = IdFor(cell);

                    for (int cc = c + 1; cc < sheet.ColCount; cc++)
                        if (sheet.Visitable(r, cc)) { Wire(b, sheet, id, GraphDir.Right, r, cc); break; }
                    for (int cc = c - 1; cc >= 0; cc--)
                        if (sheet.Visitable(r, cc)) { Wire(b, sheet, id, GraphDir.Left, r, cc); break; }
                    WireVertical(b, sheet, id, r, c, +1, GraphDir.Down);
                    WireVertical(b, sheet, id, r, c, -1, GraphDir.Up);
                }
        }

        private static void WireVertical(GraphBuilder b, FlowSheet sheet, ControlId from, int r, int c, int step, GraphDir dir)
        {
            for (int nr = r + step; nr >= 0 && nr < sheet.RowCount; nr += step)
            {
                if (sheet.Visitable(nr, c)) { Wire(b, sheet, from, dir, nr, c); return; }
                int lc = sheet.LeftmostVisitable(nr);
                if (lc >= 0) { Wire(b, sheet, from, dir, nr, lc); return; }
            }
        }

        private static void Wire(GraphBuilder b, FlowSheet sheet, ControlId from, GraphDir dir, int r, int c)
        {
            var target = sheet.CellAt(r, c);
            if (target != null) b.Connect(from, dir, IdFor(target));
        }

        // Table: a raw node per cell, edges stepping one cell (the old GridArrow's ±1 with no wrap).
        private static void EmitTable(GraphBuilder b, Table table)
        {
            for (int r = 0; r < table.RowCount; r++)
                for (int c = 0; c < table.ColCount; c++)
                {
                    var cell = table.CellAt(r, c);
                    if (cell == null) continue;
                    b.AddNode(IdFor(cell), Vt(cell));
                }

            for (int r = 0; r < table.RowCount; r++)
                for (int c = 0; c < table.ColCount; c++)
                {
                    var cell = table.CellAt(r, c);
                    if (cell == null) continue;
                    var id = IdFor(cell);
                    var right = table.CellAt(r, c + 1);
                    var left = table.CellAt(r, c - 1);
                    var down = table.CellAt(r + 1, c);
                    var up = table.CellAt(r - 1, c);
                    if (right != null) b.Connect(id, GraphDir.Right, IdFor(right));
                    if (left != null) b.Connect(id, GraphDir.Left, IdFor(left));
                    if (down != null) b.Connect(id, GraphDir.Down, IdFor(down));
                    if (up != null) b.Connect(id, GraphDir.Up, IdFor(up));
                }
        }
    }
}
