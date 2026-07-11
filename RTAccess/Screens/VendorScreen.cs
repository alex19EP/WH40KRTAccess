using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;                              // UIStrings (vendor texts pass-through)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CargoManagement.Components; // CargoSlotVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;             // InventoryStashVM
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;                    // TooltipTemplateProfitFactor
using Kingmaker.Code.UI.MVVM.VM.Vendor;                               // VendorVM + part VMs
using Kingmaker.Controllers;                                          // ReputationHelper
using Kingmaker.Items;                                                // VendorLogic
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The vendor / trade window (<see cref="VendorVM"/> on the live static part) as a graph-native
    /// screen. RT trading has two mechanics a blind player must hear up front: <b>Profit Factor is a
    /// threshold, not money</b> (a purchase requires cost ≤ your PF total and deducts nothing), and
    /// <b>items are never sold</b> — full cargo holds are exchanged for faction reputation on the
    /// window's Reputation tab, and reputation levels unlock the vendor's higher ware tiers.
    ///
    /// One Tab-stop per zone (Tab cycles, arrows stay inside — the project convention):
    /// <b>Status</b> (who + reputation + Profit Factor + discount, and the Trade/Reputation section
    /// switch driving the game's own <see cref="VendorTabNavigationVM"/>) — then per the active tab:
    /// Trade = <b>Wares</b> (a GraphSheet: one region per reputation tier, Type/Cost columns; Enter
    /// opens the game's own quantity/confirm transition window, surfaced by <see cref="VendorBuyScreen"/>)
    /// and <b>Your inventory</b> (send-to-cargo — the sighted drop zone's verb); Reputation =
    /// <b>Accepted cargo</b> (what this vendor pays reputation for) and <b>Your cargo</b> (Enter toggles
    /// a full hold's sale mark via the game's <see cref="CargoSlotVM.HandleCheck"/>, whose refusal
    /// warnings speak through WarningReader); finally <b>Actions</b> (Buy all available / the running
    /// selected-reputation total + Select all/Unselect all/Sell cargo, per tab / Close).
    ///
    /// Everything reads the live VMs immediate-mode; wares/stash rows key by item ENTITY and cargo rows
    /// by the cargo entity, so a bought/converted/sold row vanishes and the differ reads the landing.
    /// Purchases and reputation gains confirm through the game's own warning/log lines (WarningReader /
    /// LogTap), never hand-spoken. Escape routes <see cref="VendorVM.Close"/> — the game raises its own
    /// confirm box (MessageBoxScreen) when a deal is mid-flight. Layer 24, Exclusive (a full-screen UI,
    /// same family as loot); hidden-info vendors (<c>NeedHidePfAndReputation</c> /
    /// <c>NeedHideReputationCompletely</c>) suppress exactly the lines the sighted window hides.
    /// </summary>
    public sealed class VendorScreen : Screen
    {
        public VendorScreen() { Wrap = true; }

        public override string Key => "ctx.vendor";
        public override int Layer => 24;
        public override bool Exclusive => true;

        public override string ScreenName
        {
            get
            {
                var l = Logic();
                var name = l != null && l.IsTrading ? l.VendorName : null;
                return string.IsNullOrEmpty(name)
                    ? Loc.T("screen.vendor")
                    : Loc.T("vendor.screen_named", new { name });
            }
        }

        public override bool IsActive() => Vm() != null;

        private static VendorVM Vm() => UiContexts.Vendor();
        private static VendorLogic Logic() => Game.Instance?.Vendor;

        // Back (Escape) = the window's own close — raises the game's "cancel the deal?" confirm when
        // a deal is pending (surfaced by MessageBoxScreen), else EndTrading.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Vm()?.Close());
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            var logic = Logic();
            if (vm == null || logic == null || !logic.IsTrading) return;
            string k = "vendor:" + vm.GetHashCode() + ":";

            BuildTabs(b, k, logic);
            BuildStatus(b, k, vm, logic);
            if (vm.ActiveTab.Value == VendorWindowsTab.Reputation)
            {
                BuildAccepts(b, k, vm.VendorReputationPartVM);
                BuildCargo(b, k, vm.VendorReputationPartVM);
            }
            else
            {
                BuildWares(b, k, vm.VendorTradePartVM);
                BuildStash(b, k, vm.StashVM);
            }
            BuildActions(b, k, vm);
        }

        // ---- The tab strip — the sighted window's Trade / Factions Reputation buttons, mirrored as
        // real tabs (the Settings idiom): Enter selects, "selected" reads the live ActiveTab, and the
        // graph STARTS on the selected tab so reopening lands where the window is. Skipped entirely for
        // vendors whose blueprint hides the Reputation side (a one-tab strip is noise).
        private static void BuildTabs(GraphBuilder b, string k, VendorLogic logic)
        {
            if (logic.NeedHideReputationCompletely) return;
            b.BeginStop("tabs").PushContext(Loc.T("label.tabs"), Loc.T("role.list"));
            var current = Vm()?.ActiveTab?.Value ?? VendorWindowsTab.Trade;
            foreach (var tab in new[] { VendorWindowsTab.Trade, VendorWindowsTab.Reputation })
            {
                var id = ControlId.Structural(k + "tabs:" + tab);
                b.AddItem(id, TabNode(tab));
                if (tab == current) b.SetStart(id);
            }
            b.PopContext();
        }

        // One tab button: label = the game's own button text, selection read from the live ActiveTab
        // (the SELECTION, not a cached flag), Enter drives the game's own VendorTabNavigationVM and
        // confirms "selected" synchronously; the swapped-in content stops ride the next render.
        private static NodeVtable TabNode(VendorWindowsTab tab)
        {
            Func<bool> selected = () => (Vm()?.ActiveTab?.Value ?? VendorWindowsTab.Trade) == tab;
            return new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => TabName(tab)),
                    GraphNodes.SelectedPart(selected),
                },
                SearchText = () => TabName(tab),
                StateText = () => selected() ? Loc.T("state.selected") : null,
                OnActivate = () => Vm()?.VendorTabNavigationVM?.SetActiveTab(tab),
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        // ---- Status: who you trade with, reputation standing, Profit Factor, discount.
        // Hidden-info vendors: NeedHidePfAndReputation blanks PF + reputation (the sighted window shows
        // "no data"); NeedHideReputationCompletely additionally removes the Reputation tab entirely.
        private static void BuildStatus(GraphBuilder b, string k, VendorVM vm, VendorLogic logic)
        {
            bool hidePf = logic.NeedHidePfAndReputation;
            bool hideRepTab = logic.NeedHideReputationCompletely;
            bool onTrade = vm.ActiveTab.Value == VendorWindowsTab.Trade;

            b.BeginStop("status").PushContext(Loc.T("vendor.about"), Loc.T("role.list"));

            b.AddItem(ControlId.Structural(k + "who"), GraphNodes.Text(() =>
            {
                var l = Logic();
                if (l == null || !l.IsTrading) return "";
                var name = l.VendorName;
                var faction = l.VendorFaction?.DisplayName?.Text;
                return string.IsNullOrEmpty(faction)
                    ? Loc.T("vendor.trading_with_unknown", new { name })
                    : Loc.T("vendor.trading_with", new { name, faction });
            }));

            if (!hidePf && !hideRepTab)
                b.AddItem(ControlId.Structural(k + "rep"), GraphNodes.Text(RepLine));

            if (!hidePf)
            {
                // The PF readout; Space drills into the game's own modifier-breakdown template (the
                // sighted hover on the PF bar). The template needs the trade part's ProfitFactorVM.
                var pf = GraphNodes.Text(() => Loc.T("vendor.stat_line", new
                {
                    title = UIStrings.Instance.ProfitFactorTexts.Title.Text,
                    value = (Game.Instance?.Player?.ProfitFactor?.Total ?? 0f).ToString("0.#"),
                }));
                pf.OnTooltip = () =>
                {
                    var pfVm = Vm()?.VendorTradePartVM?.ProfitFactorVM;
                    if (pfVm != null)
                        TooltipChooser.OpenTemplate(UIStrings.Instance.ProfitFactorTexts.Title.Text,
                            new TooltipTemplateProfitFactor(pfVm));
                    else Tts.Speak(Loc.T("text.unavailable"), interrupt: true);
                };
                b.AddItem(ControlId.Structural(k + "pf"), pf);

                // The discount block the trade card shows when a faction discount is held.
                var trade = vm.VendorTradePartVM;
                if (onTrade && trade != null && trade.HasDiscount)
                    b.AddItem(ControlId.Structural(k + "discount"), GraphNodes.Text(() => Loc.T("vendor.stat_line", new
                    {
                        title = UIStrings.Instance.Vendor.Discount.Text,
                        value = Vm()?.VendorTradePartVM?.DiscountValue ?? 0,
                    })));
            }

            b.PopContext();
        }

        private static string TabName(VendorWindowsTab tab)
            => tab == VendorWindowsTab.Trade
                ? GameText.Or(() => UIStrings.Instance.Vendor.Trade, "vendor.tab_trade")
                : GameText.Or(() => UIStrings.Instance.CharacterSheet.FactionsReputation, "vendor.tab_reputation");

        // "Reputation level N, cur of next" / "Reputation level N, maximum" — the trade card's
        // level + progress readout, read fresh off the game's own ReputationHelper.
        private static string RepLine()
        {
            var l = Logic();
            if (l == null || !l.IsTrading) return "";
            var ft = l.VendorFactionType;
            int level = ReputationHelper.GetCurrentReputationLevel(ft);
            if (ReputationHelper.IsMaxReputation(ft))
                return Loc.T("vendor.rep_max", new { level });
            return Loc.T("vendor.rep_line", new
            {
                level,
                current = ReputationHelper.GetCurrentReputationPoints(ft),
                next = ReputationHelper.GetNextLevelReputationPoints(ft) ?? 0,
            });
        }

        // ---- Wares: the vendor's stock as a FLAT LIST — a header line per reputation tier (locked
        // tiers announce what unlocks them), then that tier's items, each row speaking everything
        // (name, type, cost, lock) as its own parts. Deliberately NOT a 2-D sheet: the ear test
        // showed column-cell navigation reading bare values ("6 Profit Factor") with no item context.
        // EnableSlots is rebuilt by the game on every completed deal; item rows key by entity so
        // focus follows the item, tier headers by their reputation level.
        private static void BuildWares(GraphBuilder b, string k, VendorTradePartVM trade)
        {
            b.BeginStop("wares").PushContext(Loc.T("vendor.wares"), Loc.T("role.list"));
            bool any = false;
            var tiers = trade?.EnableSlots;
            if (tiers != null)
                foreach (var tier in tiers)
                {
                    if (tier == null || tier.VendorSlots == null) continue;
                    bool tierHasItems = false;
                    foreach (var s in tier.VendorSlots)
                        if (s != null && s.HasItem) { tierHasItems = true; break; }
                    if (!tierHasItems) continue;

                    var lv = tier.ReputationLevelVM;
                    int level = lv?.ReputationLevel ?? 0;
                    string title = lv != null && lv.Locked
                        ? Loc.T("vendor.tier_locked", new { level, points = lv.NextLevelReputationPoints })
                        : Loc.T("vendor.tier", new { level });
                    b.AddItem(ControlId.Structural(k + "wares:tier:" + level),
                        GraphNodes.Text(() => title));

                    foreach (var slot in tier.VendorSlots)
                    {
                        if (slot == null || !slot.HasItem) continue;
                        var ent = slot.Item?.Value;
                        if (ent == null) continue;
                        b.AddItem(ControlId.Referenced(ent, k + "wares:" + ent.UniqueId),
                            ItemNodes.VendorWaresItem(slot));
                        any = true;
                    }
                }
            if (!any)
                b.AddItem(ControlId.Structural(k + "wares:empty"),
                    GraphNodes.Text(() => Loc.T("vendor.no_items")));
            b.PopContext();
        }

        // ---- Your inventory: the party stash, here so items can be CONVERTED TO CARGO (the sighted
        // drag-to-drop-zone; there is no item selling in RT). Rows key by entity — a converted item's
        // row vanishes when the deferred transfer settles and the differ reads the landing.
        private static void BuildStash(GraphBuilder b, string k, InventoryStashVM stash)
        {
            b.BeginStop("stash").PushContext(Loc.T("vendor.your_inventory"), Loc.T("role.list"));
            bool any = false;
            var vis = stash?.ItemSlotsGroup?.VisibleCollection;
            if (vis != null)
                foreach (var slot in vis)
                {
                    if (slot == null || !slot.HasItem) continue;
                    var ent = slot.Item?.Value;
                    if (ent == null) continue;
                    b.AddItem(ControlId.Referenced(ent, k + "inv:" + ent.UniqueId), ItemNodes.VendorStashItem(slot));
                    any = true;
                }
            if (!any)
                b.AddItem(ControlId.Structural(k + "inv:empty"), GraphNodes.Text(() => Loc.T("inv.no_items")));
            b.PopContext();
        }

        // ---- Accepted cargo: the origin types this vendor pays reputation for, most valuable first —
        // the window the sighted "cargo demand" panel shows. Space reads the type's own cargo card.
        private static void BuildAccepts(GraphBuilder b, string k, VendorReputationPartVM rep)
        {
            b.BeginStop("accepts").PushContext(
                GameText.Or(() => UIStrings.Instance.Vendor.DemandCargo, "vendor.accepts"), Loc.T("role.list"));
            bool any = false;
            var win = rep?.VendorReputationForItemWindow;
            if (win?.AcceptItems != null)
            {
                int i = 0;
                foreach (var accept in win.AcceptItems)
                {
                    if (accept == null) continue;
                    var a = accept;
                    var vt = GraphNodes.Text(() => Loc.T("vendor.accepts_line",
                        new { type = a.TypeLabel, points = a.ReputationCost }));
                    vt.OnTooltip = () => TooltipChooser.OpenTemplate(a.TypeLabel, a.Tooltip);
                    // Keyed by list position, NOT a.Type: the game never assigns the VM's Type field
                    // (its ctor sets label/cost/tooltip only), so every entry reads ItemsItemOrigin.None
                    // and an origin-keyed id duplicates — which threw and killed the whole tab's build.
                    // The list is fixed for the window's lifetime, so position is a stable key.
                    b.AddItem(ControlId.Structural(k + "accept:" + i), vt);
                    i++;
                    any = true;
                }
            }
            if (!any)
                b.AddItem(ControlId.Structural(k + "accept:none"),
                    GraphNodes.Text(() => Loc.T("vendor.accepts_none")));
            b.PopContext();
        }

        // ---- Your cargo: one row per hold — type, fill %, reputation worth, sale mark. Enter toggles
        // the mark via the game's own HandleCheck (only a FULL hold of an accepted type is markable;
        // the game's refusal warnings speak through WarningReader). Rows key by cargo entity.
        private static void BuildCargo(GraphBuilder b, string k, VendorReputationPartVM rep)
        {
            b.BeginStop("cargo").PushContext(
                GameText.Or(() => UIStrings.Instance.Vendor.CargoCompartment, "vendor.cargo"), Loc.T("role.list"));
            bool any = false;
            var slots = rep?.InventoryCargoVM?.CargoSlots;
            if (slots != null)
                foreach (var slot in slots)
                {
                    if (slot?.CargoEntity == null) continue;
                    var c = slot;
                    b.AddItem(ControlId.Referenced(c.CargoEntity, k + "cargo:" + c.CargoEntity.UniqueId), CargoHold(c));
                    any = true;
                }
            if (!any)
                b.AddItem(ControlId.Structural(k + "cargo:none"),
                    GraphNodes.Text(() => GameText.Or(() => UIStrings.Instance.Vendor.NoValidCargos, "vendor.no_cargo")));
            b.PopContext();
        }

        // One cargo hold's row: the card's type label + fill %, the hold's reputation worth, the "new"
        // badge, and the live sale-mark state — "selected" when marked, else the game's own reason the
        // mark is unavailable (wrong type / not full). StateText confirms a toggle immediately
        // (HandleCheck flips IsChecked synchronously; the command settles deferred); an ineligible
        // Enter stays silent here and the game's warning speaks instead.
        private static NodeVtable CargoHold(CargoSlotVM c)
        {
            Func<string> label = () => c.TypeLabel;
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(() => Loc.T("vendor.cargo_fill", new { percent = c.TotalFillValue.Value }), live: true),
                    new NodeAnnouncement(() => Loc.T("vendor.cargo_worth", new { points = c.CargoEntity?.ReputationPointsCost ?? 0 })),
                    new NodeAnnouncement(() => c.IsNew ? Loc.T("vendor.cargo_new") : ""),
                    // NOT live: the mark only flips under the row's own Enter, whose StateText already
                    // speaks the change — a live watch here would double-speak it.
                    new NodeAnnouncement(() => CargoState(c)),
                },
                SearchText = label,
                OnActivate = c.HandleCheck,
                StateText = () => c.CanCheck
                    ? Loc.T(c.IsChecked ? "vendor.cargo_selected" : "vendor.cargo_unselected")
                    : null,
                OnTooltip = () => TooltipChooser.OpenTemplate(c.TypeLabel, c.Tooltip),
                HoverSound = Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound,
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        private static string CargoState(CargoSlotVM c)
        {
            if (c.IsChecked) return Loc.T("vendor.cargo_selected");
            if (c.CanCheck) return "";
            // The game's own reasons, in its own words (the warning HandleCheck would toast).
            if (!c.CanVendorBuyCargo) return UIStrings.Instance.Vendor.VendorDontTakeThisCargo.Text;
            return UIStrings.Instance.Vendor.CargoIsNotFull.Text;
        }

        // ---- Actions: per-tab commands + Close. Labels are the game's own button texts.
        private static void BuildActions(GraphBuilder b, string k, VendorVM vm)
        {
            b.BeginStop("actions").PushContext(Loc.T("vendor.actions"), Loc.T("role.list"));
            if (vm.ActiveTab.Value == VendorWindowsTab.Reputation)
            {
                b.AddItem(ControlId.Structural(k + "total"), GraphNodes.Text(() => Loc.T("vendor.selected_total",
                    new { points = Vm()?.VendorReputationPartVM?.ExchangeValue?.Value ?? 0 })));
                b.AddItem(ControlId.Structural(k + "selectall"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.Vendor.SelectAllRelevant, "vendor.select_all"),
                    () => Vm()?.VendorReputationPartVM?.SelectAll()));
                b.AddItem(ControlId.Structural(k + "unselectall"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.Vendor.UnselectAllRelevant, "vendor.unselect_all"),
                    () => Vm()?.VendorReputationPartVM?.UnselectAll()));
                b.AddItem(ControlId.Structural(k + "sell"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.Vendor.Exchange, "vendor.sell_cargo"),
                    () => Vm()?.VendorReputationPartVM?.SellCargo(),
                    () => Vm()?.VendorReputationPartVM?.CanSellCargo?.Value == true));
            }
            else
            {
                b.AddItem(ControlId.Structural(k + "buyall"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.Vendor.BuyAllAvailable, "vendor.buy_all"),
                    () => Vm()?.BuyAllAvailable(),
                    () => Vm()?.HasItemsToBuy?.Value == true));
            }
            b.AddItem(ControlId.Structural(k + "close"), GraphNodes.Button(
                () => Loc.T("action.close"), () => Vm()?.Close()));
            b.PopContext();
        }
    }
}
