using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;         // UIStrings (ProceedTransaction / Cancel pass-through)
using Kingmaker.Code.UI.MVVM.VM.Vendor;          // VendorTransitionWindowVM, VendorWindowsTab
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's vendor purchase dialog (<see cref="VendorTransitionWindowVM"/>) surfaced accessibly —
    /// raised by the game's own buy paths (a wares click → <c>HandleTransitionWindow</c>, or Buy all
    /// available → the multi-item flavour), never by us. Two shapes, mirroring the sighted window:
    /// <b>single item</b> = the item line plus a quantity slider for stacks (Left/Right ±1, coarse ±10;
    /// the readout carries the running total in Profit Factor — the running check a sighted player does
    /// against the slider); <b>multi item</b> (Buy all) = the read-only list of everything about to be
    /// bought. <b>Deal</b> = <see cref="VendorTransitionWindowVM.Deal"/> (the purchase is instant — PF
    /// is a threshold, nothing is deducted; the game's "item gained" warning confirms through
    /// WarningReader and the window closes itself via its deal handler); <b>Cancel</b>/Escape =
    /// <see cref="VendorTransitionWindowVM.Close"/>. Layer 26, Exclusive — directly above the
    /// VendorScreen (24) that hosts the trade tab.
    /// </summary>
    public sealed class VendorBuyScreen : Screen
    {
        public override string Key => "vendor.buy";
        public override int Layer => 26;
        public override bool Exclusive => true;

        public override string ScreenName
        {
            get
            {
                var w = Wvm();
                if (w == null) return null;
                return w.Slot != null
                    ? Loc.T("vendor.purchase_named", new { name = ItemNodes.ItemName(w.Slot) })
                    : Loc.T("vendor.purchase");
            }
        }

        public override bool IsActive() => Wvm() != null;

        // The live transition window: only meaningful while the vendor's Trade tab is up (the trade
        // part VM is disposed and rebuilt on tab switches, taking the window with it).
        private static VendorTransitionWindowVM Wvm()
        {
            var vm = UiContexts.Vendor();
            if (vm == null || vm.ActiveTab.Value != VendorWindowsTab.Trade) return null;
            return vm.VendorTradePartVM?.TransitionWindowVM?.Value;
        }

        // Back (Escape) = the window's own cancel.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.cancel"),
                _ => Wvm()?.Close());
        }

        public override void Build(GraphBuilder b)
        {
            var w = Wvm();
            if (w == null) return;
            string k = "vbuy:" + w.GetHashCode() + ":";

            if (w.Slot != null)
            {
                // Single item. A stack gets the quantity slider; a lone item just reads itself + cost.
                if (w.MaxValue > 1)
                    b.AddItem(ControlId.Structural(k + "qty"), QuantityNode(w));
                else
                    b.AddItem(ControlId.Structural(k + "item"), GraphNodes.Text(() =>
                        ItemNodes.ItemLabel(w.Slot) + ", " + ItemNodes.VendorCostLabel(w.Slot)));
            }
            else if (w.Slots != null)
            {
                // Buy-all: the read-only list of what the deal covers, each with its stack cost.
                b.PushContext(Loc.T("vendor.purchase"), Loc.T("role.list"));
                var list = w.Slots.VisibleCollection;
                if (list != null)
                    foreach (var slot in list)
                    {
                        if (slot == null || !slot.HasItem) continue;
                        var ent = slot.Item?.Value;
                        if (ent == null) continue;
                        var s = slot;
                        b.AddItem(ControlId.Referenced(ent, k + "row:" + ent.UniqueId), GraphNodes.Text(() =>
                            ItemNodes.ItemLabel(s) + ", " + ItemNodes.VendorCostLabel(s)));
                    }
                b.PopContext();
            }

            b.AddItem(ControlId.Structural(k + "deal"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.Vendor.ProceedTransaction, "vendor.deal"),
                () => Wvm()?.Deal()));
            b.AddItem(ControlId.Structural(k + "cancel"), GraphNodes.Button(
                () => GameText.Action("cancel"),
                () => Wvm()?.Close()));
        }

        // The quantity slider: Left/Right ±1 (coarse ±10), announcing "N of M, total X Profit Factor"
        // live — CurrentValue is the window's own field, the exact count Deal() buys.
        private static NodeVtable QuantityNode(VendorTransitionWindowVM w)
        {
            Func<string> line = () =>
            {
                float unit = Game.Instance?.Vendor != null && w.Slot?.ItemEntity != null
                    ? Game.Instance.Vendor.GetItemBuyPrice(w.Slot.ItemEntity)
                    : 0f;
                return Loc.T("vendor.quantity", new
                {
                    count = w.CurrentValue,
                    max = w.MaxValue,
                    cost = (w.CurrentValue * unit).ToString("0.#"),
                });
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => ItemNodes.ItemLabel(w.Slot)),
                    new NodeAnnouncement(line, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => ItemNodes.ItemLabel(w.Slot),
                OnAdjust = (sign, large) =>
                {
                    int step = large ? 10 : 1;
                    w.CurrentValue = Math.Max(1, Math.Min(w.MaxValue, w.CurrentValue + sign * step));
                },
                StateText = line,
            };
        }
    }
}
