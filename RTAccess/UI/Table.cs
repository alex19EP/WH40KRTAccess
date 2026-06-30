using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.UI.Announcements;

namespace RTAccess.UI
{
    /// <summary>What a grid cell acts as for nearest-header lookup (a cell may also be plain data).</summary>
    public enum CellRole { None, Column, Row, Group }

    /// <summary>
    /// RETAINED ALTERNATE GRID — no live screen builds a Table today (FlowSheet's TableRegion is the
    /// grid primitive in use); it remains the substrate of the PanelCharSheetSink/GridCharSheetLayout
    /// presentation, kept deliberately, and the navigator still handles it (GridArrow).
    ///
    /// A 2-D data grid (Excel-with-a-screen-reader). Arrows move a cell cursor — Left/Right change
    /// column, Up/Down change row — and the navigator announces the header for whichever axis changed
    /// plus the cell. The whole table is one Tab-stop.
    ///
    /// Headers are NOT confined to the edges: any cell can be flagged a <see cref="CellRole.Column"/>,
    /// <see cref="CellRole.Row"/>, or <see cref="CellRole.Group"/> header, and a data cell resolves its
    /// headers by proximity — column header = nearest Column cell scanning UP its column; row header =
    /// nearest Row cell scanning LEFT along its row; group = nearest Group cell scanning UP column 0.
    /// That lets many blocks live in one grid: each block restates its own "Level 1…N" header row (with
    /// the group name in the leftmost cell) followed by its data rows, and every cell binds to whichever
    /// headers are nearest. Missing data cells are filled with a focusable "blank". Cells are ordinary
    /// <see cref="UIElement"/>s (e.g. <see cref="TextElement"/> with a tooltip → Space drills in).
    /// </summary>
    public sealed class Table : Container
    {
        private struct Cell { public UIElement Element; public CellRole Role; }

        private readonly List<List<Cell>> _rows = new List<List<Cell>>(); // [r][c]
        private readonly List<Func<TooltipBaseTemplate>> _rowTooltips = new List<Func<TooltipBaseTemplate>>(); // per row, parallel to _rows
        private int _cols;

        public Table(string label = null) : base(ContainerShape.Grid, label) { }

        public int RowCount => _rows.Count;
        public int ColCount => _cols;

        /// <summary>A block header row: the group name in the leftmost cell, then the column headers
        /// (e.g. the level ruler). Restate this at the top of every block.</summary>
        public void AddHeaderRow(UIElement group, IEnumerable<UIElement> columnHeaders)
        {
            var row = new List<Cell> { Make(group, CellRole.Group) };
            if (columnHeaders != null)
                foreach (var h in columnHeaders) row.Add(Make(h, CellRole.Column));
            Append(row, null);
        }

        /// <summary>A data row: the row label in the leftmost cell, then its cells (null → blank). An
        /// optional <paramref name="rowTooltip"/> applies to the whole row — Space on any cell that has
        /// no tooltip of its own drills into it (resolved live).</summary>
        public void AddDataRow(UIElement rowLabel, IEnumerable<UIElement> cells,
            Func<TooltipBaseTemplate> rowTooltip = null)
        {
            var row = new List<Cell> { Make(rowLabel, CellRole.Row) };
            if (cells != null)
                foreach (var c in cells) row.Add(Make(c, CellRole.None));
            Append(row, rowTooltip);
        }

        private Cell Make(UIElement e, CellRole role)
        {
            var el = e ?? new BlankCell();
            Add(el); // register as a focusable child so the navigator can path to it
            return new Cell { Element = el, Role = role };
        }

        // Grow to the widest row, back-padding shorter rows with blanks so every position is landable.
        private void Append(List<Cell> row, Func<TooltipBaseTemplate> rowTooltip)
        {
            _rows.Add(row);
            _rowTooltips.Add(rowTooltip);
            if (row.Count > _cols) { _cols = row.Count; foreach (var r in _rows) Pad(r); }
            else Pad(row);
        }

        private void Pad(List<Cell> row)
        {
            while (row.Count < _cols) row.Add(Make(null, CellRole.None));
        }

        public UIElement CellAt(int r, int c)
        {
            if (r < 0 || r >= _rows.Count || c < 0 || c >= _rows[r].Count) return null;
            return _rows[r][c].Element;
        }

        public CellRole RoleAt(int r, int c)
        {
            if (r < 0 || r >= _rows.Count || c < 0 || c >= _rows[r].Count) return CellRole.None;
            return _rows[r][c].Role;
        }

        /// <summary>The (row, col) of an element, or false if absent.</summary>
        public bool TryCoords(UIElement e, out int row, out int col)
        {
            for (int r = 0; r < _rows.Count; r++)
                for (int c = 0; c < _rows[r].Count; c++)
                    if (_rows[r][c].Element == e) { row = r; col = c; return true; }
            row = 0; col = 0; return false;
        }

        // Nearest Column header scanning UP column c (excludes the cell itself).
        public string ColumnHeaderText(int r, int c)
        {
            for (int rr = r - 1; rr >= 0; rr--)
                if (RoleAt(rr, c) == CellRole.Column) return CellAt(rr, c)?.GetLabelText();
            return null;
        }

        // Nearest Row header scanning LEFT along row r (excludes the cell itself).
        public string RowHeaderText(int r, int c)
        {
            for (int cc = c - 1; cc >= 0; cc--)
                if (RoleAt(r, cc) == CellRole.Row) return CellAt(r, cc)?.GetLabelText();
            return null;
        }

        // Nearest Group header scanning UP column 0 (includes the cell's own row, so a group-header
        // cell reports itself and a data cell finds its block's header above).
        public string GroupText(int r, int c)
        {
            for (int rr = r; rr >= 0; rr--)
                if (RoleAt(rr, 0) == CellRole.Group) return CellAt(rr, 0)?.GetLabelText();
            return null;
        }

        // The row-level tooltip for whichever row a cell sits in, resolved live — so Space on any cell
        // in a row (the value, a stepper, the header) drills into the row's detail when the cell has none.
        public TooltipBaseTemplate RowTooltipForCell(UIElement cell)
        {
            if (!TryCoords(cell, out int r, out _)) return null;
            return (r >= 0 && r < _rowTooltips.Count) ? _rowTooltips[r]?.Invoke() : null;
        }

        public override UIElement FirstFocusable() => CellAt(0, 0) ?? base.FirstFocusable();

        // Cell position is 2-D (the grid nav announces it), so don't tack on a flat "n of m".
        public override bool AnnouncePosition => false;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            if (!string.IsNullOrEmpty(Label)) yield return new LabelAnnouncement(Message.Raw(Label));
            yield return new RoleAnnouncement("table"); // localized via "ui"/"role.table" (dims dropped, matching FlowSheet)
        }

        /// <summary>A focusable empty cell — read as "blank" by the grid navigator.</summary>
        private sealed class BlankCell : UIElement
        {
            public override bool CanFocus => true;
        }
    }
}
