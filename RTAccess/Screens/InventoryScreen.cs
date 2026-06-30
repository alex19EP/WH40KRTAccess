using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The inventory service window (<see cref="InventoryVM"/>) as a mod-owned navigable screen: the
    /// currently-viewed character's equipment doll, a load/money summary, and the shared party stash —
    /// all in ONE <see cref="FlowSheet"/> so the arrows walk the doll, the summary and the stash item table
    /// (Ctrl+Up/Down jumps between them). Each stash row's Name cell (<see cref="ProxyInventoryItem"/>)
    /// carries the item tooltip + actions (Equip on Enter, the full menu on the secondary key); doll slots
    /// (<see cref="ProxyEquipSlot"/>) take the item off on Enter. The content refills when the viewed unit,
    /// the active weapon set, or the stash contents change, restoring grid focus to the same position so the
    /// cursor survives an equip/unequip. Escape closes. Layer 10 → sits above the in-game context, below the
    /// settings overlay. ScreenName stays null: the existing ServiceWindowAnnounce Harmony patch already
    /// speaks "Inventory" on open, so the screen doesn't double it.
    /// </summary>
    public sealed class InventoryScreen : Screen
    {
        public override string Key => "service.inventory";
        public override string ScreenName => null; // ServiceWindowAnnounce speaks "Inventory" on open
        public override int Layer => 10;

        public override bool IsActive() => Vm() != null;

        private Panel _content;
        private FlowSheet _sheet;
        private bool _built;
        private string _sig;
        private string _lastRestoreLabel; // dedupe the restore announce across a multi-frame settle burst

        public override void OnPush() { _built = false; _sig = null; }
        public override void OnPop() { Clear(); _content = null; _sheet = null; _built = false; }

        // Back (Escape) closes the whole service-window stack — the same call the window's own close uses.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Raw("Close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (!_built) BuildShell();
            var sig = ContentSig(vm);
            if (sig != _sig) { _sig = sig; RefillContent(vm); }
            else _lastRestoreLabel = null; // settled: the next change is a fresh action, so announce its landing
        }

        // Inventory opens on the planet surface AND in the star-system/space context; resolve from whichever
        // static part is live (RootUIContext checks both everywhere — IsInventoryShow / CurrentServiceWindow).
        private static InventoryVM Vm()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.SurfaceVM?.StaticPartVM?.ServiceWindowsVM?.InventoryVM?.Value
                ?? rc?.SpaceVM?.StaticPartVM?.ServiceWindowsVM?.InventoryVM?.Value;
        }

        private static ServiceWindowsVM ServiceWindows()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.SurfaceVM?.StaticPartVM?.ServiceWindowsVM
                ?? rc?.SpaceVM?.StaticPartVM?.ServiceWindowsVM;
        }

        // Refill on a viewed-unit change (doll), a weapon-set swap (hands only, stash untouched), or any
        // stash change (equip/unequip/move/drop/sort all rebuild the visible collection).
        private static string ContentSig(InventoryVM vm)
        {
            var sb = new StringBuilder();
            sb.Append(vm.Unit?.Value?.UniqueId).Append('|');
            var set = vm.DollVM?.CurrentSet?.Value;
            if (set != null) sb.Append("set:").Append(set.Index).Append('|');
            var vis = vm.StashVM?.ItemSlotsGroup?.VisibleCollection;
            if (vis != null)
                foreach (var s in vis)
                    if (s != null && s.HasItem)
                        sb.Append(s.DisplayName.Value).Append('#').Append(s.Count.Value).Append(',');
            return sb.ToString();
        }

        private void BuildShell()
        {
            _built = true;
            Clear();
            _content = new Panel();
            Add(_content);
        }

        private void RefillContent(InventoryVM vm)
        {
            if (_content == null) return;

            // The stash list is virtualized and rebuilt on every change, so capture where the cursor sits
            // in the grid and restore it to the same position afterwards (the next item after an equip, the
            // now-empty slot after an unequip).
            var cap = CaptureFocus();

            _content.Clear();

            var sheet = new FlowSheet();
            BuildEquipment(sheet, vm.DollVM);
            BuildSummary(sheet, vm.StashVM);
            BuildStash(sheet, vm.StashVM);
            sheet.Reflow();
            _sheet = sheet;
            _content.Add(sheet);

            RestoreFocus(cap);
        }

        // The equipment doll: hands from the active weapon set, then the worn gear and the quick slots, as a
        // flat "Slot: item" list (each line read as its own control via the list region).
        private static void BuildEquipment(FlowSheet sheet, InventoryDollVM doll)
        {
            if (doll == null) return;
            var list = sheet.List("Equipment");
            var set = doll.CurrentSet?.Value;
            AddSlot(list, "Primary hand", set?.Primary);
            AddSlot(list, "Secondary hand", set?.Secondary);
            AddSlot(list, "Armor", doll.Armor);
            AddSlot(list, "Head", doll.Head);
            AddSlot(list, "Neck", doll.Neck);
            AddSlot(list, "Shoulders", doll.Shoulders);
            AddSlot(list, "Wrist", doll.Wrist);
            AddSlot(list, "Gloves", doll.Gloves);
            AddSlot(list, "Belt", doll.Belt);
            AddSlot(list, "Ring 1", doll.Ring1);
            AddSlot(list, "Ring 2", doll.Ring2);
            AddSlot(list, "Feet", doll.Feet);
            AddSlot(list, "Glasses", doll.Glasses);
            AddSlot(list, "Shirt", doll.Shirt);
            if (doll.QuickSlots != null)
                for (int i = 0; i < doll.QuickSlots.Length; i++)
                    AddSlot(list, "Quick slot " + (i + 1), doll.QuickSlots[i]);
        }

        private static void AddSlot(ListRegion list, string name, EquipSlotVM slot)
        {
            if (slot != null) list.Item(new ProxyEquipSlot(name, slot));
        }

        // Party-wide readout: carry weight + load status, and money. These live on the shared stash, so they
        // sit between the per-character equipment and the stash list.
        private static void BuildSummary(FlowSheet sheet, InventoryStashVM stash)
        {
            if (stash == null) return;
            var list = sheet.List("Inventory");
            var enc = stash.EncumbranceVM;
            if (enc != null)
                list.Item(new TextElement(() =>
                {
                    var status = enc.LoadStatus.Value;
                    return "Load " + enc.LoadWeight.Value + (string.IsNullOrEmpty(status) ? "" : ", " + status);
                }));
            list.Item(new TextElement(() => "Money " + stash.Money.Value));
        }

        // The stash panel: a Name/Type/Quantity/Weight/Value item table. The Name cell carries the tooltip +
        // actions; the value cells share the item's row (Associate(0)), so up/down reads the item then the
        // column, and Enter/secondary on any cell falls through to the item.
        private static void BuildStash(FlowSheet sheet, InventoryStashVM stash)
        {
            var group = stash?.ItemSlotsGroup;
            var items = sheet.Table("Stash", "Type", "Quantity", "Weight", "Value");
            bool any = false;
            if (group?.VisibleCollection != null)
                foreach (var slot in group.VisibleCollection)
                {
                    if (slot == null || !slot.HasItem) continue;
                    any = true;
                    var s = slot;
                    items.Row(new ProxyInventoryItem(s), new UIElement[]
                    {
                        new TextElement(() => s.TypeName.Value),
                        new TextElement(() => s.Count.Value > 1 ? s.Count.Value.ToString() : "1"),
                        new TextElement(() => Weight(s.Weight.Value)),
                        new TextElement(() => Cost(s.CurrentCostPF.Value)),
                    });
                }
            if (!any) items.Row(new TextElement(() => "No items"), new UIElement[0]);
            items.Associate(0);
        }

        private static string Weight(float w) => w <= 0f ? "0" : w.ToString("0.#");
        private static string Cost(double v) => v.ToString("0.#");

        // (row, col) of the focused cell within the sheet, or row = -1 when focus is outside it (first build,
        // before anything's focused — EnsureFocus homes the initial focus instead).
        private (int row, int col) CaptureFocus()
        {
            var cur = Navigation.Current;
            if (cur != null && _sheet != null && _sheet.TryCoords(cur, out int r, out int c)) return (r, c);
            return (-1, 0);
        }

        // Re-focus the same grid position (clamped) in the rebuilt sheet, announcing the landing — but
        // suppress the repeat while a virtualized collection settles over several frames onto the same row.
        // Falls back to the first focusable if that slot's gone (e.g. the stash emptied).
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
