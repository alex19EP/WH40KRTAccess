using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.UI.Announcements;

namespace RTAccess.UI
{
    /// <summary>How a region treats positions with no real content.</summary>
    public enum BlankFill
    {
        /// <summary>Every declared position is landable (empties read "blank") — keeps table columns aligned.</summary>
        Visit,
        /// <summary>Only real focusable cells are landable (pads/empties skipped) — list behaviour.</summary>
        Skip,
    }

    /// <summary>
    /// A <see cref="FlowSheet"/> region: a contiguous block of rows with its own columns and read-out
    /// behaviour. Regions stack vertically into the sheet's one matrix. Authored via the sheet's
    /// <see cref="FlowSheet.Table"/>/<see cref="FlowSheet.List"/> builders.
    /// </summary>
    public abstract class Region
    {
        public string Label { get; }
        public BlankFill Blanks { get; protected set; } = BlankFill.Visit;
        public abstract string TypeName { get; } // spoken on entry: "table", "list"

        // Authored rows: each is the cells left→right (null = an explicit blank). Parallel tooltip per row.
        internal readonly List<UIElement[]> Rows = new List<UIElement[]>();
        internal readonly List<Func<TooltipBaseTemplate>> RowTooltips = new List<Func<TooltipBaseTemplate>>();

        protected Region(string label) { Label = label; }

        /// <summary>The column header announced when the cursor enters data column <paramref name="col"/>
        /// (0-based across the row), or null. Metadata — not a focusable cell.</summary>
        public virtual string ColumnHeader(int col) => null;

        /// <summary>When true, the navigator reads each cell as its own full focus message (label, role,
        /// selected/value, …) instead of the table-style region/column-header/row-label framing — so a row
        /// of controls (a <see cref="BarRegion"/>) announces them as the controls they are.</summary>
        public virtual bool ReadCellFocusMessage => false;
    }

    /// <summary>A grid block: column-0 is the row label, columns 1..n are named data columns.</summary>
    public sealed class TableRegion : Region
    {
        private readonly string[] _columns; // data column names (column 0 is the row label, unnamed)
        public override string TypeName => "table";

        // Optional "associated element" column: the column whose cell holds each row's interactive control
        // (e.g. a radio). Input/tooltip on any cell in the row falls through to it, and arrowing UP/DOWN
        // announces its focus string instead of the plain cell value — so the control reads as what it is.
        public int AssociatedColumn { get; private set; } = -1;
        public Type[] AssociatedAnnouncements { get; private set; } // which element announcement types to read (null = all)
        private int[] _extraColumns;                                 // columns the element column appends (null = all others)

        public TableRegion(string label, string[] columns) : base(label) => _columns = columns ?? new string[0];

        public override string ColumnHeader(int col)
            => (col >= 1 && col - 1 < _columns.Length) ? _columns[col - 1] : null;

        /// <summary>Mark which column holds each row's interactive element. <paramref name="announcements"/>
        /// limits which of the element's announcement types are read on up/down (null = all);
        /// <paramref name="extraColumns"/> are the columns the element column appends on up/down
        /// (null = every other column, in order).</summary>
        public TableRegion Associate(int column, Type[] announcements = null, int[] extraColumns = null)
        {
            AssociatedColumn = column;
            AssociatedAnnouncements = announcements;
            _extraColumns = extraColumns;
            return this;
        }

        /// <summary>The columns the element column appends on up/down — the configured list, or all others.</summary>
        public IEnumerable<int> ExtraColumns(int totalCols)
        {
            if (_extraColumns != null) return _extraColumns;
            var list = new List<int>();
            for (int i = 0; i < totalCols; i++) if (i != AssociatedColumn) list.Add(i);
            return list;
        }

        /// <summary>A data row: the row label, then one cell per data column (null → blank). Optional
        /// row-level tooltip (Space on a cell with none of its own drills into it, resolved live).</summary>
        public TableRegion Row(UIElement rowLabel, UIElement[] cells, Func<TooltipBaseTemplate> tooltip = null)
        {
            var row = new UIElement[1 + (cells?.Length ?? 0)];
            row[0] = rowLabel;
            if (cells != null) Array.Copy(cells, 0, row, 1, cells.Length);
            Rows.Add(row);
            RowTooltips.Add(tooltip);
            return this;
        }
    }

    /// <summary>A single-column block; pads and empties are skipped, so it behaves like a list.</summary>
    public sealed class ListRegion : Region
    {
        public override string TypeName => "list";
        // A list is a column of controls, not a column-table — read each as its own full focus message
        // (label, role, on/off, value…) so a control's state isn't dropped to a table-style label+role.
        public override bool ReadCellFocusMessage => true;
        public ListRegion(string label) : base(label) => Blanks = BlankFill.Skip;

        public ListRegion Item(UIElement element, Func<TooltipBaseTemplate> tooltip = null)
        {
            Rows.Add(new[] { element });
            RowTooltips.Add(tooltip);
            return this;
        }
    }

    /// <summary>A horizontal bar: ONE row of cells (a filter toggle row, a sort/swap control bar). Cells
    /// read as their own full focus message — "Weapon, radio button, selected" — not table cells, so the
    /// controls announce as what they are and nothing reads as a "table". Pads/empties are skipped.</summary>
    public sealed class BarRegion : Region
    {
        private readonly List<UIElement> _cells = new List<UIElement>();
        public override string TypeName => "bar";
        public override bool ReadCellFocusMessage => true;
        public BarRegion(string label) : base(label) => Blanks = BlankFill.Skip;

        /// <summary>Append a control to the bar's single row.</summary>
        public BarRegion Cell(UIElement element)
        {
            _cells.Add(element);
            Rows.Clear(); RowTooltips.Clear();   // the bar is one row, rebuilt as cells are added
            Rows.Add(_cells.ToArray());
            RowTooltips.Add(null);               // each control supplies its own tooltip
            return this;
        }
    }

    /// <summary>A region you can fold up: collapsed it's just a header button ("Abilities, group,
    /// collapsed"); activating the header expands its contents on the rows below (and collapses again).
    /// Toggling reflows the owning sheet. Used for the action bar's spell/ability/item group flyouts.</summary>
    public sealed class CollapsibleRegion : Region
    {
        private readonly List<UIElement> _content = new List<UIElement>();
        private readonly CollapsibleHeader _header;
        public override string TypeName => "group";
        public override bool ReadCellFocusMessage => true; // header + contents read as their full focus message
        public bool Expanded { get; private set; }
        internal Action OnToggle; // wired by FlowSheet.Collapsible to reflow the sheet

        public CollapsibleRegion(string label) : base(label)
        {
            Blanks = BlankFill.Skip;
            _header = new CollapsibleHeader(this);
            Rebuild();
        }

        /// <summary>Add a content row (single cell), shown only while expanded.</summary>
        public CollapsibleRegion Item(UIElement element)
        {
            if (element != null) { _content.Add(element); Rebuild(); }
            return this;
        }

        public void Toggle()
        {
            Expanded = !Expanded;
            Rebuild();
            OnToggle?.Invoke();
        }

        private void Rebuild()
        {
            Rows.Clear(); RowTooltips.Clear();
            Rows.Add(new[] { (UIElement)_header }); RowTooltips.Add(null);
            if (Expanded)
                foreach (var e in _content) { Rows.Add(new[] { e }); RowTooltips.Add(null); }
        }
    }

    /// <summary>The header button of a <see cref="CollapsibleRegion"/>: label + expanded/collapsed state;
    /// Enter toggles the region (reflowing the sheet — this header instance is reused so focus stays put).</summary>
    internal sealed class CollapsibleHeader : UIElement
    {
        private readonly CollapsibleRegion _region;
        public CollapsibleHeader(CollapsibleRegion region) => _region = region;

        public override bool CanFocus => true;
        public override bool ReannounceOnActivate => true; // speak the new expanded/collapsed state in place

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_region.Label));
            yield return new RoleAnnouncement("group");
            yield return new ValueAnnouncement(Message.Localized("ui", _region.Expanded ? "value.expanded" : "value.collapsed"));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate,
                Message.Localized("ui", _region.Expanded ? "action.collapse" : "action.expand"),
                _ => _region.Toggle());
        }
    }

    /// <summary>
    /// A customizable 2-D grid built from stacked <see cref="Region"/>s — the unified replacement for a
    /// screen's bundle of tabbed lists/tables. The whole sheet is ONE Tab-stop; arrows move a cell cursor
    /// across the flattened matrix (crossing region edges), Ctrl+Up/Down jump between regions, and the
    /// navigator announces the region (on entry), the column header (on column change) and row label (on
    /// row change), then the cell. Regions are authoring/semantic groupings painted onto one global
    /// matrix, so navigation stays flat. Reflow (<see cref="Reflow"/>) re-lays the matrix from the
    /// regions' existing cells, so a focused cell survives a region being added/removed.
    /// </summary>
    public sealed class FlowSheet : Container
    {
        private struct GCell { public UIElement Element; public Region Region; public bool Blank; public bool Pad; }

        private readonly List<Region> _regions = new List<Region>();
        private readonly List<List<GCell>> _grid = new List<List<GCell>>();      // [row][col]
        private readonly List<Func<TooltipBaseTemplate>> _rowTip = new List<Func<TooltipBaseTemplate>>();
        private int _cols;

        public FlowSheet(string label = null) : base(ContainerShape.Grid, label) { }

        // ---- authoring ----
        public TableRegion Table(string label, params string[] columns)
        {
            var r = new TableRegion(label, columns); _regions.Add(r); return r;
        }
        public ListRegion List(string label) { var r = new ListRegion(label); _regions.Add(r); return r; }
        public CollapsibleRegion Collapsible(string label) { var r = new CollapsibleRegion(label) { OnToggle = Reflow }; _regions.Add(r); return r; }
        public BarRegion Bar(string label) { var r = new BarRegion(label); _regions.Add(r); return r; }
        public void AddRegion(Region r) { if (r != null) _regions.Add(r); }
        public void RemoveRegion(Region r) { _regions.Remove(r); }
        public void ClearRegions() => _regions.Clear(); // rebuild a sheet in place (keeps the instance/tab-stop)
        public bool HasRegion(Region r) => _regions.Contains(r);

        /// <summary>(Re)build the matrix + child set from the regions' current cells. Cell instances are
        /// reused, so a focused cell survives — the navigator re-locates it via <see cref="TryCoords"/>.</summary>
        public void Reflow()
        {
            Clear(); // reset Children (cells are re-added below; their instances persist across this)
            _grid.Clear();
            _rowTip.Clear();
            _cols = 0;
            foreach (var region in _regions)
            {
                for (int i = 0; i < region.Rows.Count; i++)
                {
                    var src = region.Rows[i];
                    var row = new List<GCell>(src.Length);
                    foreach (var e in src)
                    {
                        var el = e ?? new BlankCell();
                        Add(el); // register as a focusable child so the navigator can path to it
                        row.Add(new GCell { Element = el, Region = region, Blank = e == null });
                    }
                    _grid.Add(row);
                    _rowTip.Add(i < region.RowTooltips.Count ? region.RowTooltips[i] : null);
                    if (row.Count > _cols) _cols = row.Count;
                }
            }
            // Pad rows narrower than the widest so the matrix is rectangular. These pads exist only because
            // ANOTHER region is wider (e.g. a filter bar over a 5-column item table); they're filler, not
            // real cells, so they're never landable (Pad). Explicit in-row blanks (a null cell for column
            // alignment WITHIN a table) are a different thing and stay visitable in a Visit region.
            for (int r = 0; r < _grid.Count; r++)
            {
                var region = _grid[r].Count > 0 ? _grid[r][0].Region : null;
                while (_grid[r].Count < _cols)
                {
                    var b = new BlankCell(); Add(b);
                    _grid[r].Add(new GCell { Element = b, Region = region, Blank = true, Pad = true });
                }
            }
        }

        // ---- navigation support (used by the navigator) ----
        public int RowCount => _grid.Count;
        public int ColCount => _cols;

        public UIElement CellAt(int r, int c)
            => (r >= 0 && r < _grid.Count && c >= 0 && c < _grid[r].Count) ? _grid[r][c].Element : null;

        public Region RegionAt(int r)
            => (r >= 0 && r < _grid.Count && _grid[r].Count > 0) ? _grid[r][0].Region : null;

        /// <summary>The interactive element a cell defers to: its row's associated-element cell, in a table
        /// that declared one (<see cref="TableRegion.Associate"/>). Null otherwise (or for the element itself
        /// it returns itself). Lets input/tooltip on a plain value cell fall through to the row's control.</summary>
        public UIElement AssociatedElementForCell(UIElement cell)
        {
            if (!TryCoords(cell, out int r, out _)) return null;
            var region = RegionAt(r) as TableRegion;
            if (region == null || region.AssociatedColumn < 0) return null;
            return CellAt(r, region.AssociatedColumn);
        }

        /// <summary>The up/down spoken readout for a cell in an associated-element table, or null if the cell
        /// isn't in one. On the element column: the element's focus (configured announcements) + the appended
        /// columns ("Fireball, toggle, on, Level 1, School evocation"). On any other column: that cell's value
        /// FIRST, then the element focus ("3, Fireball, toggle, on") — so you hear the column value you came
        /// for, then the row's identity. Used by both arrow-nav and first-focus so they read the same.</summary>
        public string ComposeAssociatedReadout(UIElement cell, bool withRegionLabel)
        {
            if (!TryCoords(cell, out int r, out int c)) return null;
            var tr = RegionAt(r) as TableRegion;
            if (tr == null || tr.AssociatedColumn < 0) return null;

            var parts = new List<string>();
            if (withRegionLabel && !string.IsNullOrEmpty(tr.Label)) parts.Add(tr.Label + ", " + tr.TypeName);
            var elem = CellAt(r, tr.AssociatedColumn);
            var et = elem != null ? elem.GetFocusText(tr.AssociatedAnnouncements) : null;

            if (c == tr.AssociatedColumn)
            {
                if (!string.IsNullOrEmpty(et)) parts.Add(et);
                foreach (int col in tr.ExtraColumns(_cols))
                {
                    var h = ColumnHeader(r, col);
                    var v = CellAt(r, col)?.GetLabelText();
                    if (!string.IsNullOrWhiteSpace(v)) parts.Add(string.IsNullOrEmpty(h) ? v : h + " " + v);
                }
            }
            else
            {
                var v = CellAt(r, c)?.GetLabelText();
                if (!string.IsNullOrWhiteSpace(v)) parts.Add(v);     // the column value you arrowed to, first
                if (!string.IsNullOrEmpty(et)) parts.Add(et);        // then the row's element identity
            }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        /// <summary>Can the cursor land on (r, c)? Skip-regions only allow real focusable cells; Visit-
        /// regions allow every position (empties/pads read "blank") so table columns stay aligned.</summary>
        public bool Visitable(int r, int c)
        {
            if (r < 0 || r >= _grid.Count || c < 0 || c >= _grid[r].Count) return false;
            var cell = _grid[r][c];
            if (cell.Pad) return false; // width-normalizing filler — never a target
            if (cell.Region != null && cell.Region.Blanks == BlankFill.Skip)
                return !cell.Blank && cell.Element != null && cell.Element.CanFocus;
            return true;
        }

        public int LeftmostVisitable(int r)
        {
            for (int c = 0; c < _cols; c++) if (Visitable(r, c)) return c;
            return -1;
        }

        public bool TryCoords(UIElement e, out int row, out int col)
        {
            for (int r = 0; r < _grid.Count; r++)
                for (int c = 0; c < _grid[r].Count; c++)
                    if (_grid[r][c].Element == e) { row = r; col = c; return true; }
            row = 0; col = 0; return false;
        }

        public string ColumnHeader(int r, int c) => RegionAt(r)?.ColumnHeader(c);
        public string RowLabel(int r) => CellAt(r, 0)?.GetLabelText();

        // ---- region jumps (Ctrl+Up/Down) ----
        public int RegionFirstRow(Region region)
        {
            for (int r = 0; r < _grid.Count; r++) if (RegionAt(r) == region) return r;
            return -1;
        }
        public Region StepRegion(Region from, int dir)
        {
            int idx = _regions.IndexOf(from);
            int ni = idx + dir;
            return (idx >= 0 && ni >= 0 && ni < _regions.Count) ? _regions[ni] : null;
        }

        // Row-level tooltip for whichever row a cell sits in, resolved live (Space fallback).
        public TooltipBaseTemplate RowTooltipForCell(UIElement cell)
        {
            if (!TryCoords(cell, out int r, out _)) return null;
            return (r >= 0 && r < _rowTip.Count) ? _rowTip[r]?.Invoke() : null;
        }

        public override UIElement FirstFocusable()
        {
            for (int r = 0; r < _grid.Count; r++)
            {
                int c = LeftmostVisitable(r);
                if (c >= 0) return CellAt(r, c);
            }
            return base.FirstFocusable();
        }

        // Cell position is 2-D (the grid nav announces region/headers), so no flat "n of m".
        public override bool AnnouncePosition => false;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            if (!string.IsNullOrEmpty(Label)) yield return new LabelAnnouncement(Message.Raw(Label));
        }

        /// <summary>A focusable empty cell — read as "blank" by the grid navigator.</summary>
        private sealed class BlankCell : UIElement
        {
            public override bool CanFocus => true;
        }
    }
}
