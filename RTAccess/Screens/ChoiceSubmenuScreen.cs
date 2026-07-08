using System;
using System.Collections.Generic;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// A list of options to pick from (e.g. a dropdown's values), pushed as a CHILD SCREEN of whatever
    /// screen opened it (<see cref="Screen.PushChild"/>). As a child it's the focused screen while open and
    /// owns the keyboard; selecting an option or backing out removes it, and ScreenManager re-focuses the
    /// parent on its remembered control (the dropdown) automatically. Reusable for any "open a list and
    /// pick one" interaction.
    ///
    /// Two shapes:
    /// <list type="bullet">
    /// <item><b>Value picker</b> (<see cref="Open"/>): a flat list of strings with a CURRENT selection —
    /// each row a radio option, focus landing on the current value. The dropdown path.</item>
    /// <item><b>Rich submenu</b> (<see cref="OpenRows"/>): <see cref="Row"/>s — non-selectable header lines
    /// and gated action buttons — the host for <see cref="ContextMenuNodes"/> (game context menus).</item>
    /// </list>
    ///
    /// Graph-native: the option list is immutable per instance, and (value-picker only) <c>SetStart</c>
    /// lands focus on the CURRENT option (the graph's start node), so opening reads the selected value first.
    /// </summary>
    public sealed class ChoiceSubmenuScreen : Screen
    {
        /// <summary>One row of a rich submenu. A HEADER row (<see cref="OnSelect"/> null) is a non-selectable
        /// label — announced so a category / separator that carries text is never silently dropped; an ACTION
        /// row is a button, greyed (announced "disabled", Enter inert) when <see cref="Enabled"/> returns
        /// false — mirroring the game, which shows a valid-but-non-interactable entry rather than hiding it.</summary>
        public readonly struct Row
        {
            public readonly Func<string> Label;
            public readonly Action OnSelect;     // null => header (non-selectable label row)
            public readonly Func<bool> Enabled;  // null => always enabled; read only for action rows

            private Row(Func<string> label, Action onSelect, Func<bool> enabled)
            {
                Label = label; OnSelect = onSelect; Enabled = enabled;
            }

            public static Row Header(Func<string> label) => new Row(label, null, null);
            public static Row Action(Func<string> label, Action onSelect, Func<bool> enabled = null)
                => new Row(label, onSelect, enabled);

            public bool IsHeader => OnSelect == null;
        }

        private readonly string _title;
        private readonly IReadOnlyList<string> _options;
        private readonly int _current;
        private readonly Action<int> _onSelect;
        private readonly IReadOnlyList<Row> _rows;

        public ChoiceSubmenuScreen(string title, IReadOnlyList<string> options, int current, Action<int> onSelect)
        {
            _title = title;
            _options = options;
            _current = current;
            _onSelect = onSelect;
            Wrap = true;
        }

        private ChoiceSubmenuScreen(string title, IReadOnlyList<Row> rows)
        {
            _title = title;
            _rows = rows;
            _current = -1;
            Wrap = true;
        }

        /// <summary>Open a value-picker submenu (a dropdown's options) as a child of the current screen.</summary>
        public static void Open(string title, IReadOnlyList<string> options, int current, Action<int> onSelect)
            => ScreenManager.Current?.PushChild(new ChoiceSubmenuScreen(title, options, current, onSelect));

        /// <summary>Open a rich submenu (headers + gated action rows — the context-menu driver's host) as a
        /// child of the current screen.</summary>
        public static void OpenRows(string title, IReadOnlyList<Row> rows)
            => ScreenManager.Current?.PushChild(new ChoiceSubmenuScreen(title, rows));

        public override string Key => "overlay.choicesubmenu";
        public override string ScreenName => _title;
        public override bool IsActive() => false; // never poll-pushed — only ever a child screen

        public override IEnumerable<ElementAction> GetActions()
        {
            // Back closes the submenu without changing the value.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        private void Close() => ParentScreen?.RemoveChild(this);


        public override void Build(GraphBuilder b)
        {
            if (_rows != null) { BuildRows(b); return; }
            for (int i = 0; i < _options.Count; i++)
            {
                int idx = i;
                string label = _options[i];
                // Snapshot is safe: the submenu is ephemeral (a fresh instance per open, closed by the
                // selection itself), so the selected state can't change while it lives.
                b.AddItem(ControlId.Structural("choice:" + i), GraphNodes.ChoiceOption(
                    () => label, () => idx == _current,
                    () => { _onSelect?.Invoke(idx); Close(); }));
            }
            if (_current >= 0 && _current < _options.Count)
                b.SetStart(ControlId.Structural("choice:" + _current)); // land on the current option
        }

        // Rich-submenu layout: headers as read-only text lines, actions as buttons that run then close.
        // GraphNodes.Button self-gates on Enabled (a disabled row advertises no action and Enter is inert),
        // so a non-interactable entry lands as a focusable "disabled" row rather than executing.
        private void BuildRows(GraphBuilder b)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var id = ControlId.Structural("choice:" + i);
                if (row.IsHeader) { b.AddItem(id, GraphNodes.Text(row.Label)); continue; }
                var act = row.OnSelect;
                b.AddItem(id, GraphNodes.Button(row.Label, () => { act(); Close(); }, row.Enabled));
            }
        }
    }
}
