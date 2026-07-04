using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root;                   // LocalizedTexts
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;
using Kingmaker.GameCommands;
using Kingmaker.UI.Common;                         // ItemsFilterType, ItemsSorterType
using Owlcat.Runtime.UI.Tooltips;                  // TooltipBaseTemplate
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The inventory service window (<see cref="InventoryVM"/>) as a graph-native screen: the currently-viewed
    /// character's equipment doll, a defensive-stat readout, a party load/money summary, a Filters/Sort control
    /// bar, and the shared party stash — all declared IMMEDIATE-MODE from the live VM on every render (no
    /// content signature, no focus-capture/restore dance). Five REGIONS in one Tab-stop: the arrows walk each
    /// in turn and Ctrl+Up/Down jumps between them. The stash is a plain LIST — one focusable row per item, its
    /// label mirroring the card (name + badges + count); Type/Weight/Value are tooltip-only on the card, so they
    /// stay on Space (the item's own tooltip). The game stash is a filtered/sorted flat list of icon cards (2-D
    /// position is sort-order only), so a list — not a column table — is the faithful model.
    ///
    /// Doll slots read their item LIVE from the doll VM inside the node each render (re-fetching
    /// <c>DollVM.Armor</c> etc. every <see cref="Build"/>) and are keyed STRUCTURALLY by slot position + the
    /// viewed unit — never by the <see cref="EquipSlotVM"/> object, which the doll replaces on every equip. That
    /// keeps focus put across an equip while the shown item updates live (the differ re-announces the now-filled
    /// slot under focus), fixing the old adapter bug where the doll read EMPTY until the window was reopened
    /// (its ContentSig sampled the stash but not the doll). Stash rows key by the item ENTITY, so an equipped /
    /// dropped / moved item's node vanishes and focus slides to a genuinely different row the differ reads out.
    /// Doll + defense keys carry the viewed unit so a character switch re-keys them; party-wide summary / stash
    /// keys don't. Escape closes the whole service-window stack. Layer 10. ScreenName stays null — the existing
    /// ServiceWindowAnnounce Harmony patch already speaks "Inventory" on open.
    /// </summary>
    public sealed class InventoryScreen : Screen
    {
        public override string Key => "service.inventory";
        public override string ScreenName => null; // ServiceWindowAnnounce speaks "Inventory" on open
        public override int Layer => 10;

        public override bool IsActive() => Vm() != null;

        // Back (Escape) closes the whole service-window stack — the same call the window's own close uses.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
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

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "inv:" + vm.GetHashCode() + ":";           // a new window = fresh keys
            var unit = vm.Unit?.Value;
            string uk = k + "u:" + (unit?.UniqueId ?? "") + ":";  // per-character keys re-home on a unit switch

            BuildEquipment(b, k, uk, vm.DollVM);
            BuildDefenses(b, k, uk, vm);
            BuildSummary(b, k, vm.StashVM);
            BuildStashControls(b, k, vm.StashVM);
            BuildStash(b, k, vm.StashVM);
        }

        // The equipment doll: the weapon sets, then the worn gear and quick slots, as a flat "Slot: item" list.
        // Each slot's EquipSlotVM is resolved FRESH here every render (the doll replaces them on equip); the node
        // keys are structural by slot position + viewed unit, so focus stays put across an equip.
        private static void BuildEquipment(GraphBuilder b, string k, string uk, InventoryDollVM doll)
        {
            if (doll == null) return;
            b.SetRegion(k + "equip");
            b.PushContext(Loc.T("inv.equipment"), Loc.T("role.list"));
            BuildWeaponSets(b, uk, doll);
            AddDollSlot(b, uk, "armor", Loc.T("slot.armor"), doll.Armor, doll);
            AddDollSlot(b, uk, "head", Loc.T("slot.head"), doll.Head, doll);
            AddDollSlot(b, uk, "neck", Loc.T("slot.neck"), doll.Neck, doll);
            AddDollSlot(b, uk, "shoulders", Loc.T("slot.shoulders"), doll.Shoulders, doll);
            AddDollSlot(b, uk, "wrist", Loc.T("slot.wrist"), doll.Wrist, doll);
            AddDollSlot(b, uk, "gloves", Loc.T("slot.gloves"), doll.Gloves, doll);
            AddDollSlot(b, uk, "belt", Loc.T("slot.belt"), doll.Belt, doll);
            AddDollSlot(b, uk, "ring1", Loc.T("slot.ring1"), doll.Ring1, doll);
            AddDollSlot(b, uk, "ring2", Loc.T("slot.ring2"), doll.Ring2, doll);
            AddDollSlot(b, uk, "feet", Loc.T("slot.feet"), doll.Feet, doll);
            AddDollSlot(b, uk, "glasses", Loc.T("slot.glasses"), doll.Glasses, doll);
            AddDollSlot(b, uk, "shirt", Loc.T("slot.shirt"), doll.Shirt, doll);
            if (doll.QuickSlots != null)
                for (int i = 0; i < doll.QuickSlots.Length; i++)
                    AddDollSlot(b, uk, "quick:" + i, Loc.T("slot.quick", new { index = i + 1 }), doll.QuickSlots[i], doll);
            b.PopContext();
        }

        // The two hand loadouts (I/II). When both are usable, a combo box drives the game's own set selection
        // (SetSelected → SwitchHandEquipment) and BOTH sets' hands are listed, set-numbered, so the inactive
        // loadout is visible without committing a switch. A unit with a single usable set (mechadendrites / a
        // unique companion) just gets the active hands, unlabelled.
        private static void BuildWeaponSets(GraphBuilder b, string uk, InventoryDollVM doll)
        {
            var sets = doll.WeaponSets;
            if (sets == null || sets.Count == 0)
            {
                var only = doll.CurrentSet?.Value;
                AddDollSlot(b, uk, "primary", Loc.T("slot.primary_hand"), only?.Primary, doll);
                AddDollSlot(b, uk, "secondary", Loc.T("slot.secondary_hand"), only?.Secondary, doll);
                return;
            }

            var usable = new List<WeaponSetVM>();
            foreach (var s in sets) if (s != null && s.IsEnabled.Value) usable.Add(s);
            if (usable.Count == 0) usable.Add(sets[0]);

            if (usable.Count > 1)
            {
                var opts = usable;
                b.AddItem(ControlId.Structural(uk + "wset"), Cycler(
                    () => Loc.T("inv.weapon_sets"),
                    () => opts.ConvertAll(s => Loc.T("inv.weapon_set", new { index = s.Index + 1 })),
                    () => Math.Max(0, opts.IndexOf(doll.CurrentSet?.Value)),
                    i => { if (i >= 0 && i < opts.Count && opts[i] != doll.CurrentSet?.Value) opts[i].SetSelected(true); }));
                foreach (var s in usable)
                {
                    AddDollSlot(b, uk, "set" + (s.Index + 1) + ":primary",
                        Loc.T("slot.set_primary", new { set = s.Index + 1 }), s.Primary, doll);
                    AddDollSlot(b, uk, "set" + (s.Index + 1) + ":secondary",
                        Loc.T("slot.set_secondary", new { set = s.Index + 1 }), s.Secondary, doll);
                }
            }
            else
            {
                var only = doll.CurrentSet?.Value ?? usable[0];
                AddDollSlot(b, uk, "primary", Loc.T("slot.primary_hand"), only?.Primary, doll);
                AddDollSlot(b, uk, "secondary", Loc.T("slot.secondary_hand"), only?.Secondary, doll);
            }
        }

        private static void AddDollSlot(GraphBuilder b, string uk, string posKey, string name,
            EquipSlotVM slot, InventoryDollVM doll)
        {
            if (slot == null) return;
            // Structural key = slot position + viewed unit (uk). NEVER key by the EquipSlotVM object — the doll
            // rebuilds it on equip, which would teleport focus; the item is read live inside the node instead.
            b.AddItem(ControlId.Structural(uk + "doll:" + posKey), ItemNodes.EquipSlot(name, slot, doll));
        }

        // The derived defensive stats the game shows beside the doll — read live from the reachable
        // InventoryDollAdditionalStatsVM (already-formatted strings + breakdown tooltips on Space). Resolve is
        // hidden for pets (the VM reports "—"). Per-character, so keyed on the viewed unit.
        private static void BuildDefenses(GraphBuilder b, string k, string uk, InventoryVM vm)
        {
            var s = vm.LevelClassScoresVM?.AdditionalStatsVM;
            if (s == null) return;
            b.SetRegion(k + "defenses");
            b.PushContext(Loc.T("inv.defenses"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(uk + "def:deflection"),
                StatLine(() => Loc.T("stat.deflection", new { value = s.ArmorDeflection.Value }), () => s.DeflectionTooltip.Value));
            b.AddItem(ControlId.Structural(uk + "def:absorption"),
                StatLine(() => Loc.T("stat.absorption", new { value = s.ArmorAbsorption.Value }), () => s.AbsorptionTooltip.Value));
            b.AddItem(ControlId.Structural(uk + "def:dodge"),
                StatLine(() => Loc.T("stat.dodge", new { value = s.Dodge.Value }), () => s.DodgeTooltip.Value));
            b.AddItem(ControlId.Structural(uk + "def:dodge_reduction"),
                StatLine(() => Loc.T("stat.dodge_reduction", new { value = s.DodgeReduction.Value })));
            if (!string.IsNullOrEmpty(s.Resolve.Value) && s.Resolve.Value != "—")
                b.AddItem(ControlId.Structural(uk + "def:resolve"),
                    StatLine(() => Loc.T("stat.resolve", new { value = s.Resolve.Value })));
            b.AddItem(ControlId.Structural(uk + "def:parry"),
                StatLine(() => Loc.T("stat.parry", new { value = s.Parry.Value })));
            b.PopContext();
        }

        // A read-only stat line whose Space drills into the game's own breakdown template (rendered body +
        // inline glossary links) through the shared chooser — the game shows a full stat breakdown on hover.
        private static NodeVtable StatLine(Func<string> text, Func<TooltipBaseTemplate> tooltip = null)
        {
            var vt = GraphNodes.Text(text);
            if (tooltip != null) vt.OnTooltip = () => TooltipChooser.OpenTemplate(text(), tooltip());
            return vt;
        }

        // Party-wide readout: carry weight + load status, and money. These live on the shared stash, so they sit
        // between the per-character equipment and the stash list; keyed party-wide (not on the viewed unit).
        private static void BuildSummary(GraphBuilder b, string k, InventoryStashVM stash)
        {
            if (stash == null) return;
            b.SetRegion(k + "summary");
            b.PushContext(Loc.T("inv.inventory"), Loc.T("role.list"));
            var enc = stash.EncumbranceVM;
            if (enc != null)
                b.AddItem(ControlId.Structural(k + "sum:enc"), GraphNodes.Text(() =>
                {
                    var status = enc.LoadStatus.Value;
                    var load = enc.LoadWeight.Value + (string.IsNullOrEmpty(status) ? "" : ", " + status);
                    return Loc.T("inv.encumbrance", new { value = load });
                }));
            b.AddItem(ControlId.Structural(k + "sum:gold"),
                GraphNodes.Text(() => Loc.T("inv.gold", new { value = stash.Money.Value })));
            b.PopContext();
        }

        // The filter + sort control bar above the stash — the real chrome a sighted player uses to operate a
        // 120-slot list. Each is a combo box (Enter → a submenu of localized options) that drives the game's OWN
        // filter VM / sort command; both persist to UISettings and rebuild the visible collection, which the
        // next immediate-mode render reflects (the live value part re-announces the landing). A horizontal row,
        // so Left/Right walks between the two boxes. (A search field lands in a later slice — it needs an
        // accessible text-entry source.)
        private static void BuildStashControls(GraphBuilder b, string k, InventoryStashVM stash)
        {
            var filter = stash?.ItemsFilter;
            if (filter == null) return;
            b.SetRegion(k + "filters");
            b.PushContext(Loc.T("inv.filters"), Loc.T("role.list"));
            b.StartRow(k + "filtersrow");

            var filters = FilterOptions;
            b.AddItem(ControlId.Structural(k + "filter"), Cycler(
                () => Loc.T("inv.filters"),
                () => filters.ConvertAll(t => LocalizedTexts.Instance.ItemsFilter.GetText(t)),
                () => Math.Max(0, filters.IndexOf(stash.CurrentFilter.Value)),
                i => { if (i >= 0 && i < filters.Count) filter.SetCurrentFilter(filters[i]); }));

            var sorters = SortOptions;
            b.AddItem(ControlId.Structural(k + "sort"), Cycler(
                () => Loc.T("inv.sort"),
                () => sorters.ConvertAll(t => LocalizedTexts.Instance.ItemsFilter.GetText(t)),
                () => Math.Max(0, sorters.IndexOf(stash.CurrentSorter.Value)),
                i => { if (i >= 0 && i < sorters.Count) Game.Instance.GameCommandQueue.SetInventorySorter(sorters[i]); }));

            b.EndRow();
            b.PopContext();
        }

        // A combo box over a fixed option list: value = the current option (LIVE — a submenu pick or a filter
        // change re-announces itself on the landing); Enter opens a submenu to pick. Activate-only (no Left/Right
        // cycle), so in the filters bar Left/Right walks between cells and in a list Left/Right stays free.
        private static NodeVtable Cycler(Func<string> label, Func<IReadOnlyList<string>> options,
            Func<int> current, Action<int> select)
        {
            Func<string> value = () =>
            {
                var o = options?.Invoke();
                int i = current?.Invoke() ?? -1;
                return o != null && i >= 0 && i < o.Count ? o[i] : "";
            };
            return GraphNodes.Dropdown(label, value, () =>
            {
                var o = options?.Invoke();
                if (o == null || o.Count == 0) return;
                ChoiceSubmenuScreen.Open(label(), o, current?.Invoke() ?? -1, i => select?.Invoke(i));
            });
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
        // ItemNodes.InventoryItem). Type/weight/value are tooltip-only on the card, so they stay on Space (the
        // item's own tooltip). Keyed by the item ENTITY, so equipping / dropping / moving an item removes its
        // node and focus slides to a genuinely different row the differ reads. Empty (an active filter matched
        // nothing) reads a placeholder.
        private static void BuildStash(GraphBuilder b, string k, InventoryStashVM stash)
        {
            b.SetRegion(k + "stash");
            b.PushContext(Loc.T("inv.stash"), Loc.T("role.list"));
            bool any = false;
            var vis = stash?.ItemSlotsGroup?.VisibleCollection;
            if (vis != null)
                foreach (var slot in vis)
                {
                    if (slot == null || !slot.HasItem) continue;
                    var ent = slot.Item.Value;
                    if (ent == null) continue;
                    b.AddItem(ControlId.Referenced(ent, k + "stash:" + ent.GetHashCode()), ItemNodes.InventoryItem(slot));
                    any = true;
                }
            if (!any) b.AddItem(ControlId.Structural(k + "stash:empty"), GraphNodes.Text(() => Loc.T("inv.no_items")));
            b.PopContext();
        }
    }
}
