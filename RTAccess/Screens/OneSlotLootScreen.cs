using System.Text;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Loot;
using Kingmaker.Code.UI.MVVM.VM.Slots;   // ItemSlotVM
using Kingmaker.UI.Common;               // InventoryHelper
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's OneSlot loot window (<see cref="LootVM.IsOneSlot"/>) — a device/mechanism you INSERT a party item
    /// into (a fuse, a sacred cog, a key component) — as a mod-owned navigable screen. Interacting with such a device
    /// already opens this window today, invisibly swallowing the keyboard; this screen makes it usable.
    ///
    /// Unlike a chest (read + take), OneSlot has a target slot on the device and a list of party items you may put in
    /// it. So the body is: an optional <b>remove-what's-inserted</b> row (when the slot is filled), then the party
    /// items that satisfy the device's insert condition (<see cref="InsertableLootSlotVM.CanInsert"/>), each of which
    /// Enter INSERTS via the game's own <see cref="InventoryHelper.InsertToInteractionSlot"/> (the same call
    /// <c>LootVM.HandleTryInsertSlot</c> makes for the sighted click — it ejects any current item back to the party,
    /// then transfers the chosen one in). Inserting does NOT close the window (an authored put-trigger may fire); Escape
    /// closes via the window's own <see cref="LootVM.Close"/>.
    ///
    /// Exclusive, layer 24 — alongside the other world-interaction modals (LootScreen / Variative / Transition); loot is
    /// triggered from exploration, so it never stacks with a service window. The plain <see cref="LootScreen"/> gates
    /// OneSlot out of its supported modes, so exactly one of the two is ever active for a given window.
    /// </summary>
    public sealed class OneSlotLootScreen : Screen
    {
        public override string Key => "loot.oneslot";
        public override int Layer => 24;
        public override bool Exclusive => true;

        // Spoken on open (OnFocus): the device's own name + prompt (e.g. "Reliquary. Insert the sacred cog.").
        public override string ScreenName
        {
            get
            {
                var slot = Vm()?.InteractionSlot;
                if (slot == null) return null;
                var name = Join(slot.Name, slot.Description);
                return string.IsNullOrWhiteSpace(name) ? Loc.T("insert.title") : name;
            }
        }

        public override bool IsActive() { var vm = Vm(); return vm != null && vm.IsOneSlot; }

        private Panel _content;
        private FlowSheet _sheet;
        private bool _built;
        private string _sig;
        private string _lastRestoreLabel; // dedupe the restore announce across a multi-frame settle burst

        public override void OnPush() { _built = false; _sig = null; _lastRestoreLabel = null; }
        public override void OnPop() { Clear(); _content = null; _sheet = null; _built = false; }

        // Back (Escape) closes the device window via its own close callback (the game's OnLootClosed + DisposeLoot).
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
            else _lastRestoreLabel = null; // settled: the next change is a fresh insert/eject, so announce its landing
        }

        // OneSlot opens on the planet surface AND in the star-system/space context; resolve from whichever static
        // part is live (the LootContextVM is a sibling of ServiceWindowsVM on both).
        private static LootVM Vm()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.SurfaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value
                ?? rc?.SpaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value;
        }

        // Rebuild when the device slot's contents change (an item inserted/ejected) or the set of insertable party
        // items changes (inserting removes one from the party; ejecting adds one back).
        private static string ContentSig(LootVM vm)
        {
            var sb = new StringBuilder();
            var inSlot = vm.InteractionSlot?.ItemSlot?.Value;
            sb.Append("cur:").Append(inSlot != null && inSlot.HasItem ? inSlot.DisplayName.Value : "-").Append('|');
            var vis = vm.InventoryStash?.InsertableSlotsGroup?.VisibleCollection;
            if (vis != null)
                foreach (var s in vis)
                    if (s != null && s.HasItem && s.CanInsert.Value)
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

        private void RefillContent(LootVM vm)
        {
            if (_content == null) return;

            // The insertable list is virtualized and rebuilt on every change, so capture where the cursor sits and
            // restore it afterwards (the next item after an insert, the remove row after an eject).
            var cap = CaptureFocus();

            _content.Clear();

            var slot = vm.InteractionSlot;
            var sheet = new FlowSheet();
            var title = slot != null && !string.IsNullOrWhiteSpace(slot.Name) ? slot.Name : Loc.T("insert.title");
            var list = sheet.List(title);

            // If the device slot already holds an item, offer to pull it back out (the game's own eject: clicking the
            // filled slot collects it to the party — LootVM.HandleChangeLoot → InventoryHelper.TryCollectLootSlot).
            var inSlot = slot?.ItemSlot?.Value;
            if (inSlot != null && inSlot.HasItem)
            {
                var ejectName = inSlot.DisplayName.Value;
                list.Item(new ProxyActionButton(
                    () => Loc.T("insert.inserted", new { name = ejectName }),
                    () => true,
                    () => Eject(inSlot, ejectName),
                    actionVerb: "remove"));
            }

            // The party items that satisfy the device's insert condition; Enter puts one in.
            bool any = false;
            var vis = vm.InventoryStash?.InsertableSlotsGroup?.VisibleCollection;
            if (vis != null && slot != null)
                foreach (var s in vis)
                    if (s != null && s.HasItem && s.CanInsert.Value)
                    {
                        list.Item(new ProxyInsertItem(s, slot));
                        any = true;
                    }

            // Nothing to insert and nothing to remove — a focusable line so Enter or Escape both dismiss.
            if (!any && (inSlot == null || !inSlot.HasItem))
                list.Item(new ProxyActionButton(Loc.T("insert.none"), () => true, () => Vm()?.Close()));

            sheet.Reflow();
            _sheet = sheet;
            _content.Add(sheet);

            RestoreFocus(cap);
        }

        // Eject the item currently in the device slot back to the party — the game's own single-slot collect (the same
        // call OneSlot's HandleChangeLoot makes when the sighted UI clicks the filled slot).
        private static void Eject(ItemSlotVM inSlot, string name)
        {
            if (InventoryHelper.TryCollectLootSlot(inSlot))
                Tts.Speak(Loc.T("insert.removed", new { name }), interrupt: true);
        }

        private static string Join(params string[] parts)
        {
            var bits = new List<string>();
            foreach (var p in parts) if (!string.IsNullOrWhiteSpace(p)) bits.Add(p);
            return string.Join(". ", bits.ToArray());
        }

        // (row, col) of the focused cell within the sheet, or row = -1 when focus is outside it (first build).
        private (int row, int col) CaptureFocus()
        {
            var cur = Navigation.Current;
            if (cur != null && _sheet != null && _sheet.TryCoords(cur, out int r, out int c)) return (r, c);
            return (-1, 0);
        }

        // Re-focus the same position (clamped) in the rebuilt sheet, announcing the landing — but suppress the
        // repeat while the virtualized collection settles over several frames onto the same row. Falls back to
        // the first focusable if that slot's gone (the item inserted/ejected).
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
