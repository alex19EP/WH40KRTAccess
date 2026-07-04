using System.Text;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Loot;
using Kingmaker.Code.UI.MVVM.VM.Slots;   // ItemSlotVM, SlotsGroupVM
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's PlayerChest loot window (<see cref="LootVM.IsPlayerStash"/>) — the player's personal shared stash — as
    /// a mod-owned navigable screen. It is a two-way store: the <b>Chest</b> holds items you've stowed, and your
    /// <b>Inventory</b> is the party's carried gear; you move items either way. So the body is two regions in one
    /// <see cref="FlowSheet"/> (arrows walk a region, Ctrl+Up/Down jumps between them): the chest items (Enter WITHDRAWS
    /// to inventory) and the party items (Enter DEPOSITS to the chest) — each a <see cref="ProxyStashItem"/> driving the
    /// game's own per-slot handler (see there). Both panels are always shown by the sighted UI too.
    ///
    /// v1 covers the two-way stash; the window's separate <b>cargo</b> panel (ship trade goods, <c>CargoInventory</c>) is
    /// deferred — cargo is a distinct system the inventory screen doesn't surface yet either. Escape closes via the
    /// window's own <see cref="LootVM.Close"/>. Exclusive, layer 24 — alongside the other loot / world-interaction
    /// modals; the plain <see cref="LootScreen"/> and <see cref="OneSlotLootScreen"/> both exclude PlayerChest, so
    /// exactly one loot screen is ever active.
    /// </summary>
    public sealed class PlayerChestScreen : Screen
    {
        public override string Key => "loot.playerchest";
        public override int Layer => 24;
        public override bool Exclusive => true;

        // Spoken on open (OnFocus): the chest's own display name (no ServiceWindowAnnounce fires for loot).
        public override string ScreenName
        {
            get { var vm = Vm(); return vm == null ? null : (Nz(vm.PlayerStash?.LootDisplayName) ?? Loc.T("stash.title")); }
        }

        public override bool IsActive() { var vm = Vm(); return vm != null && vm.IsPlayerStash; }

        private Panel _content;
        private FlowSheet _sheet;
        private bool _built;
        private string _sig;
        private string _lastRestoreLabel; // dedupe the restore announce across a multi-frame settle burst

        public override void OnPush() { _built = false; _sig = null; _lastRestoreLabel = null; }
        public override void OnPop() { Clear(); _content = null; _sheet = null; _built = false; }

        // Back (Escape) closes the chest window via its own close callback (the game's OnLootClosed + DisposeLoot).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.Close());
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (!_built) BuildShell();
            var sig = ContentSig(vm);
            if (sig != _sig) { _sig = sig; RefillContent(vm); }
            else _lastRestoreLabel = null; // settled: the next change is a fresh withdraw/deposit, so announce its landing
        }

        // Loot opens on the planet surface AND in the star-system/space context; resolve from whichever static part is
        // live (the LootContextVM is a sibling of ServiceWindowsVM on both).
        private static LootVM Vm()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.SurfaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value
                ?? rc?.SpaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value;
        }

        // The chest slot group is the first ContextLoot view (all stash items in PlayerChest mode); the party inventory
        // is InventoryStash's group. Both rebuild their visible collection on every transfer.
        private static SlotsGroupVM<ItemSlotVM> ChestGroup(LootVM vm)
            => vm.ContextLoot != null && vm.ContextLoot.Count > 0 ? vm.ContextLoot[0]?.SlotsGroup : null;

        private static SlotsGroupVM<ItemSlotVM> PartyGroup(LootVM vm) => vm.InventoryStash?.ItemSlotsGroup;

        private static string ContentSig(LootVM vm)
        {
            var sb = new StringBuilder();
            Append(sb, "c:", ChestGroup(vm));
            Append(sb, "p:", PartyGroup(vm));
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string tag, SlotsGroupVM<ItemSlotVM> group)
        {
            sb.Append(tag);
            var vis = group?.VisibleCollection;
            if (vis != null)
                foreach (var s in vis)
                    if (s != null && s.HasItem)
                        sb.Append(s.DisplayName.Value).Append('#').Append(s.Count.Value).Append(',');
            sb.Append('|');
        }

        private void BuildShell()
        {
            _built = true;
            Clear();
            _content = new Panel();
            Add(_content);
        }

        private void RefillContent(LootVM vm)
        {
            if (_content == null) return;

            // Both lists are virtualized and rebuilt on every transfer, so capture where the cursor sits and restore
            // it afterwards (the next item after a withdraw/deposit).
            var cap = CaptureFocus();

            _content.Clear();

            var sheet = new FlowSheet();
            BuildSide(sheet, Loc.T("stash.chest"), ChestGroup(vm), fromChest: true, Loc.T("stash.chest_empty"));
            BuildSide(sheet, Loc.T("stash.inventory"), PartyGroup(vm), fromChest: false, Loc.T("stash.inventory_empty"));
            sheet.Reflow();
            _sheet = sheet;
            _content.Add(sheet);

            RestoreFocus(cap);
        }

        private static void BuildSide(FlowSheet sheet, string title, SlotsGroupVM<ItemSlotVM> group, bool fromChest, string emptyText)
        {
            var list = sheet.List(title);
            bool any = false;
            var vis = group?.VisibleCollection;
            if (vis != null)
                foreach (var slot in vis)
                    if (slot != null && slot.HasItem)
                    {
                        list.Item(new ProxyStashItem(slot, fromChest));
                        any = true;
                    }
            if (!any) list.Item(new TextElement(() => emptyText));
        }

        private static string Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        // (row, col) of the focused cell within the sheet, or row = -1 when focus is outside it (first build).
        private (int row, int col) CaptureFocus()
        {
            var cur = Navigation.Current;
            if (cur != null && _sheet != null && _sheet.TryCoords(cur, out int r, out int c)) return (r, c);
            return (-1, 0);
        }

        // Re-focus the same position (clamped) in the rebuilt sheet, announcing the landing — but suppress the repeat
        // while the virtualized collection settles over several frames onto the same row. Falls back to the first
        // focusable if that slot's gone (the item moved to the other side).
        private void RestoreFocus((int row, int col) cap)
        {
            if (cap.row < 0 || _sheet == null) return;
            UIElement cell = null;
            if (_sheet.RowCount > 0)
            {
                int r = Math.Min(cap.row, _sheet.RowCount - 1);
                int c = _sheet.Visitable(r, cap.col) ? cap.col : _sheet.LeftmostVisitable(r);
                if (c >= 0) cell = _sheet.CellAt(r, c);
            }
            cell = cell ?? _sheet.FirstFocusable();
            if (cell == null) return;
            var label = cell.GetLabelText();
            bool announce = label != _lastRestoreLabel;
            _lastRestoreLabel = label;
            Navigation.Focus(cell, announce);
        }
    }
}
