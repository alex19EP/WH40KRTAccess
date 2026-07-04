using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints.Root;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;
using Kingmaker.GameCommands;
using Kingmaker.UI.Common;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The inventory service window (<see cref="InventoryVM"/>) as a mod-owned navigable screen: the
    /// currently-viewed character's equipment doll, a load/money summary, a Filters/Sort control bar, and the
    /// shared party stash — all in ONE <see cref="FlowSheet"/> so the arrows walk each in turn (Ctrl+Up/Down
    /// jumps between them). The stash is a plain LIST — one focusable row per item, its label mirroring the
    /// card (name + badges + count); Type/Weight/Value are tooltip-only on the card, so they stay on Space
    /// (the item's own tooltip). The game stash is a filtered/sorted flat list of icon cards (2-D position is
    /// sort-order only), so a list — not a column table — is the faithful model. The bar's combo boxes drive
    /// the game's own filter VM / sort command; each stash row (<see cref="ProxyInventoryItem"/>) carries the
    /// item tooltip + actions (Equip on Enter, the full menu on the secondary key); doll slots
    /// (<see cref="ProxyEquipSlot"/>) take the item off on Enter. The content refills when the viewed unit,
    /// the active weapon set, the filter/sort, or the stash contents change, restoring grid focus to the same
    /// position so the cursor survives an equip/unequip/filter. Escape closes. Layer 10 → sits above the
    /// in-game context, below the settings overlay. ScreenName stays null: the existing ServiceWindowAnnounce
    /// Harmony patch already speaks "Inventory" on open, so the screen doesn't double it.
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
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
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
            var stash = vm.StashVM;
            if (stash != null)
            {
                // Filter/sort are part of the signature so a change forces a refill even if the resulting
                // visible set happens to look the same — and the refill re-announces the landing.
                sb.Append("f:").Append((int)stash.CurrentFilter.Value).Append('|');
                sb.Append("s:").Append((int)stash.CurrentSorter.Value).Append('|');
                var vis = stash.ItemSlotsGroup?.VisibleCollection;
                if (vis != null)
                    foreach (var s in vis)
                        if (s != null && s.HasItem)
                            sb.Append(s.DisplayName.Value).Append('#').Append(s.Count.Value).Append(',');
            }
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
            BuildDefenses(sheet, vm);
            BuildSummary(sheet, vm.StashVM);
            BuildStashControls(sheet, vm.StashVM);
            BuildStash(sheet, vm.StashVM);
            sheet.Reflow();
            _sheet = sheet;
            _content.Add(sheet);

            RestoreFocus(cap);
        }

        // The equipment doll: the weapon sets, then the worn gear and the quick slots, as a flat "Slot: item"
        // list (each line read as its own control via the list region).
        private static void BuildEquipment(FlowSheet sheet, InventoryDollVM doll)
        {
            if (doll == null) return;
            var list = sheet.List(Loc.T("inv.equipment"));
            BuildWeaponSets(list, doll);
            AddSlot(list, Loc.T("slot.armor"), doll.Armor, doll);
            AddSlot(list, Loc.T("slot.head"), doll.Head, doll);
            AddSlot(list, Loc.T("slot.neck"), doll.Neck, doll);
            AddSlot(list, Loc.T("slot.shoulders"), doll.Shoulders, doll);
            AddSlot(list, Loc.T("slot.wrist"), doll.Wrist, doll);
            AddSlot(list, Loc.T("slot.gloves"), doll.Gloves, doll);
            AddSlot(list, Loc.T("slot.belt"), doll.Belt, doll);
            AddSlot(list, Loc.T("slot.ring1"), doll.Ring1, doll);
            AddSlot(list, Loc.T("slot.ring2"), doll.Ring2, doll);
            AddSlot(list, Loc.T("slot.feet"), doll.Feet, doll);
            AddSlot(list, Loc.T("slot.glasses"), doll.Glasses, doll);
            AddSlot(list, Loc.T("slot.shirt"), doll.Shirt, doll);
            if (doll.QuickSlots != null)
                for (int i = 0; i < doll.QuickSlots.Length; i++)
                    AddSlot(list, Loc.T("slot.quick", new { index = i + 1 }), doll.QuickSlots[i], doll);
        }

        // The two hand loadouts (I/II). When both are usable, a combo box drives the game's own set
        // selection (SetSelected → SwitchHandEquipment) and BOTH sets' hands are listed, set-numbered, so the
        // inactive loadout is visible without committing a switch. A unit with a single usable set
        // (mechadendrites / a unique companion) just gets the active hands, unlabelled.
        private static void BuildWeaponSets(ListRegion list, InventoryDollVM doll)
        {
            var sets = doll.WeaponSets;
            if (sets == null || sets.Count == 0)
            {
                var only = doll.CurrentSet?.Value;
                AddSlot(list, Loc.T("slot.primary_hand"), only?.Primary, doll);
                AddSlot(list, Loc.T("slot.secondary_hand"), only?.Secondary, doll);
                return;
            }

            var usable = new List<WeaponSetVM>();
            foreach (var s in sets) if (s != null && s.IsEnabled.Value) usable.Add(s);
            if (usable.Count == 0) usable.Add(sets[0]);

            if (usable.Count > 1)
            {
                var opts = usable;
                list.Item(new ProxyChoiceCycler(
                    () => Loc.T("inv.weapon_sets"),
                    () => opts.ConvertAll(s => Loc.T("inv.weapon_set", new { index = s.Index + 1 })),
                    () => Math.Max(0, opts.IndexOf(doll.CurrentSet?.Value)),
                    i => { if (i >= 0 && i < opts.Count && opts[i] != doll.CurrentSet?.Value) opts[i].SetSelected(true); }));
                foreach (var s in usable)
                {
                    AddSlot(list, Loc.T("slot.set_primary", new { set = s.Index + 1 }), s.Primary, doll);
                    AddSlot(list, Loc.T("slot.set_secondary", new { set = s.Index + 1 }), s.Secondary, doll);
                }
            }
            else
            {
                var only = doll.CurrentSet?.Value ?? usable[0];
                AddSlot(list, Loc.T("slot.primary_hand"), only?.Primary, doll);
                AddSlot(list, Loc.T("slot.secondary_hand"), only?.Secondary, doll);
            }
        }

        // The derived defensive stats the game shows beside the doll — read live from the reachable
        // InventoryDollAdditionalStatsVM (already formatted strings + breakdown tooltips on Space). Resolve is
        // hidden for pets (the VM reports "—"); empty values self-skip (TextElement isn't focusable when blank).
        private static void BuildDefenses(FlowSheet sheet, InventoryVM vm)
        {
            var s = vm.LevelClassScoresVM?.AdditionalStatsVM;
            if (s == null) return;
            var list = sheet.List(Loc.T("inv.defenses"));
            list.Item(new TextElement(() => Loc.T("stat.deflection", new { value = s.ArmorDeflection.Value }),
                tooltip: () => s.DeflectionTooltip.Value));
            list.Item(new TextElement(() => Loc.T("stat.absorption", new { value = s.ArmorAbsorption.Value }),
                tooltip: () => s.AbsorptionTooltip.Value));
            list.Item(new TextElement(() => Loc.T("stat.dodge", new { value = s.Dodge.Value }),
                tooltip: () => s.DodgeTooltip.Value));
            list.Item(new TextElement(() => Loc.T("stat.dodge_reduction", new { value = s.DodgeReduction.Value })));
            if (!string.IsNullOrEmpty(s.Resolve.Value) && s.Resolve.Value != "—")
                list.Item(new TextElement(() => Loc.T("stat.resolve", new { value = s.Resolve.Value })));
            list.Item(new TextElement(() => Loc.T("stat.parry", new { value = s.Parry.Value })));
        }

        private static void AddSlot(ListRegion list, string name, EquipSlotVM slot, InventoryDollVM doll)
        {
            if (slot != null) list.Item(new ProxyEquipSlot(name, slot, doll));
        }

        // Party-wide readout: carry weight + load status, and money. These live on the shared stash, so they
        // sit between the per-character equipment and the stash list.
        private static void BuildSummary(FlowSheet sheet, InventoryStashVM stash)
        {
            if (stash == null) return;
            var list = sheet.List(Loc.T("inv.inventory"));
            var enc = stash.EncumbranceVM;
            if (enc != null)
                list.Item(new TextElement(() =>
                {
                    var status = enc.LoadStatus.Value;
                    var load = enc.LoadWeight.Value + (string.IsNullOrEmpty(status) ? "" : ", " + status);
                    return Loc.T("inv.encumbrance", new { value = load });
                }));
            list.Item(new TextElement(() => Loc.T("inv.gold", new { value = stash.Money.Value })));
        }

        // The filter + sort control bar above the stash — the real chrome a sighted player uses to operate a
        // 120-slot list. Each is a combo box (Enter → a submenu of localized options) that drives the game's
        // OWN filter VM / sort command; both persist to UISettings and rebuild the visible collection, which
        // trips a content refill that re-announces the landing. (A search field lands in a later slice — it
        // needs an accessible text-entry source.)
        private static void BuildStashControls(FlowSheet sheet, InventoryStashVM stash)
        {
            var filter = stash?.ItemsFilter;
            if (filter == null) return;
            var bar = sheet.Bar(Loc.T("inv.filters"));

            var filters = FilterOptions;
            bar.Cell(new ProxyChoiceCycler(
                () => Loc.T("inv.filters"),
                () => filters.ConvertAll(t => LocalizedTexts.Instance.ItemsFilter.GetText(t)),
                () => Math.Max(0, filters.IndexOf(stash.CurrentFilter.Value)),
                i => { if (i >= 0 && i < filters.Count) filter.SetCurrentFilter(filters[i]); }));

            var sorters = SortOptions;
            bar.Cell(new ProxyChoiceCycler(
                () => Loc.T("inv.sort"),
                () => sorters.ConvertAll(t => LocalizedTexts.Instance.ItemsFilter.GetText(t)),
                () => Math.Max(0, sorters.IndexOf(stash.CurrentSorter.Value)),
                i => { if (i >= 0 && i < sorters.Count) Game.Instance.GameCommandQueue.SetInventorySorter(sorters[i]); }));
        }

        // The personal-inventory filter set (mirrors ItemsFilterPCView.m_SortedFiltersList) and the sort modes
        // (every ItemsSorterType except the cargo-only CargoValue, matching the game's sort dropdown).
        private static readonly List<ItemsFilterType> FilterOptions = new List<ItemsFilterType>
        {
            ItemsFilterType.NoFilter, ItemsFilterType.Weapon, ItemsFilterType.Armor,
            ItemsFilterType.Accessories, ItemsFilterType.Usable, ItemsFilterType.Notable,
            ItemsFilterType.NonUsable, ItemsFilterType.AugmentationsAll, ItemsFilterType.ShipNoFilter,
        };

        private static readonly List<ItemsSorterType> SortOptions = new List<ItemsSorterType>
        {
            ItemsSorterType.NotSorted, ItemsSorterType.TypeUp, ItemsSorterType.TypeDown,
            ItemsSorterType.CharacteristicsUp, ItemsSorterType.CharacteristicsDown,
            ItemsSorterType.NameUp, ItemsSorterType.NameDown,
            ItemsSorterType.DateUp, ItemsSorterType.DateDown, ItemsSorterType.Favorite,
        };

        // The stash: one focusable row per item, its label mirroring the card (name + badges + count via
        // ProxyInventoryItem). Type/weight/value are tooltip-only on the card, so they stay on Space (the
        // item's own tooltip). Empty (e.g. an active filter matched nothing) reads a placeholder.
        private static void BuildStash(FlowSheet sheet, InventoryStashVM stash)
        {
            var group = stash?.ItemSlotsGroup;
            var list = sheet.List(Loc.T("inv.stash"));
            bool any = false;
            if (group?.VisibleCollection != null)
                foreach (var slot in group.VisibleCollection)
                {
                    if (slot == null || !slot.HasItem) continue;
                    any = true;
                    list.Item(new ProxyInventoryItem(slot));
                }
            if (!any) list.Item(new TextElement(Loc.T("inv.no_items")));
        }

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
