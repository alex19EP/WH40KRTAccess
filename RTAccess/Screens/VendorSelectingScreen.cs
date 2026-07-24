using Kingmaker;                                             // Game (loaded-area settings + the command queue)
using Kingmaker.Blueprints.Root.Strings;                     // UIStrings (header / captions)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.FactionsReputation; // faction + vendor item VMs
using Kingmaker.Code.UI.MVVM.VM.Vendor;                      // VendorSelectingWindowVM
using Kingmaker.GameCommands;                                // GameCommandQueue.CloseScreen
using Kingmaker.PubSubSystem;                                // IBeginSelectingVendorHandler, IScreenUIHandler
using Kingmaker.PubSubSystem.Core;                           // EventBus
using Kingmaker.UI.Common;                                   // UINetUtility (the co-op control gate)
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The vendor-selecting window (<see cref="VendorSelectingWindowVM"/>, a <c>*StaticPartVM</c> child on BOTH
    /// the surface and the space side) — the "who do you want to trade with?" gate the <c>OpenVendorSelectingWindow</c>
    /// game action raises in front of the trade window <see cref="VendorScreen"/> already covers. One card per
    /// FACTION (the game builds one for every <c>FactionType</c>, vendor-carrying or not), each followed by the
    /// vendors of that faction the action offered; activating a vendor row queues the game's own
    /// <c>StartTrading</c> command.
    ///
    /// Card parity: a faction card shows its name, the reputation caption, the current rank (the Roman numeral)
    /// and the "current / next" progress — with the faction's own description on hover, so that stays on Space
    /// ([[rt-label-mirror-visual]]). A vendor row shows a location and a name and carries a trade button only
    /// when the entry actually resolved to an entity — a vendor-less row is declared as plain text, exactly as
    /// the game hides that button.
    ///
    /// Closing has no VM method: <c>VendorSelectingWindowBaseView.OnCloseClick</c> is a VIEW method that (a)
    /// refuses in co-op capital-party mode unless you control the main character, (b) queues the game's own
    /// <c>CloseScreen(VendorSelecting)</c> command and (c) raises <c>HandleExitSelectingVendor</c>. All three
    /// steps are reproduced in <see cref="Close"/> — driving the VM alone would leave the window half-shut.
    ///
    /// Layer 18, Exclusive: it must clear dialogue (15) — the raising game action commonly fires from an answer —
    /// and the appearance/visual-settings pair (16/17), while staying BELOW <see cref="VendorScreen"/> (24): the
    /// game does NOT dispose this window when a trade starts, so the trade window opens on top of it and closing
    /// the trade returns you here to pick another vendor.
    /// </summary>
    public sealed class VendorSelectingScreen : Screen
    {
        public VendorSelectingScreen() { Wrap = true; }

        public override string Key => "overlay.vendorselecting";
        public override int Layer => 18;
        public override bool Exclusive => true;
        public override string ScreenName => Vm() != null ? Title() : null;

        private static VendorSelectingWindowVM Vm()
            => UiContexts.FromLiveStaticPart(
                s => s.VendorSelectingWindowVM?.Value,
                s => s.VendorSelectingWindowVM?.Value);

        public override bool IsActive() => Vm() != null;

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Vm() != null)
                yield return new ElementAction(ActionIds.Back, Message.Raw(CloseLabel()), _ => Close());
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "vendorsel:" + vm.GetHashCode() + ":";

            b.BeginStop("factions").PushContext(Title(), Loc.T("role.list"));
            var factions = vm.FactionItems;
            int i = 0;
            if (factions != null)
                foreach (var f in factions)
                {
                    if (f == null) { i++; continue; }
                    var fac = f;         // capture
                    int idx = i++;
                    var vt = GraphNodes.Text(() => FactionLine(fac));
                    vt.OnTooltip = () => TooltipChooser.OpenTemplate(fac.Label ?? "", fac.Tooltip);
                    b.AddItem(ControlId.Referenced(fac, k + "fac:" + idx), vt);
                    AddVendors(b, k + "fac:" + idx + ":", fac);
                }
            b.PopContext();

            b.BeginStop("actions").PushContext(Loc.T("hud.actions"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(k + "close"), GraphNodes.Button(CloseLabel, Close));
            b.PopContext();
        }

        // The faction's own vendor rows. Trading drives the row VM's StartTrading (which carries the co-op
        // control gate and the synchronized flag); a row with no resolved entity is display-only, mirroring the
        // game hiding that row's button.
        private static void AddVendors(GraphBuilder b, string k, CharInfoFactionReputationItemVM faction)
        {
            var vendors = faction.Vendors;
            if (vendors == null) return;
            for (int i = 0; i < vendors.Count; i++)
            {
                var v = vendors[i];
                if (v == null) continue;
                var row = v; // capture
                if (row.Vendor != null)
                    b.AddItem(ControlId.Referenced(row, k + "vendor:" + i),
                        GraphNodes.Button(() => VendorLine(row), () => StartTrade(row)));
                else
                    b.AddItem(ControlId.Referenced(row, k + "vendor:" + i),
                        GraphNodes.Text(() => VendorLine(row)));
            }
        }

        // "Kasballica, Reputation: 3, 120 / 400" — the card's own four fields, with the game's caption word.
        private static string FactionLine(CharInfoFactionReputationItemVM f)
        {
            try
            {
                return Loc.T("vendorselect.faction", new
                {
                    name = f.Label ?? "",
                    caption = GameText.Or(() => UIStrings.Instance.CharacterSheet.FactionsReputation,
                        "vendorselect.reputation"),
                    level = f.ReputationLevel.Value,
                    progress = f.GetCurrentAndNextLevelProgress() ?? "",
                });
            }
            catch (Exception e) { Main.Log?.Error("VendorSelectingScreen.FactionLine: " + e); return f.Label ?? ""; }
        }

        // "Aurora Nadira, Footfall" — the row's name and the location it was last detected in.
        private static string VendorLine(FactionVendorInformationVM v)
        {
            string name = v.Name ?? "";
            string where = v.Location;
            return string.IsNullOrWhiteSpace(where)
                ? name : Loc.T("vendorselect.vendor", new { name, location = where });
        }

        private static void StartTrade(FactionVendorInformationVM v)
        {
            try { v.StartTrade(); }
            catch (Exception e) { Main.Log?.Error("VendorSelectingScreen.StartTrade: " + e); }
        }

        /// <summary>The view's own OnCloseClick, step for step: the capital-party co-op gate, the queued
        /// CloseScreen command (which disposes the VM through IScreenUIHandler), then the exit event.</summary>
        private static void Close()
        {
            try
            {
                bool capital = Game.Instance?.LoadedAreaState?.Settings.CapitalPartyMode ?? false;
                if (capital && !UINetUtility.IsControlMainCharacterWithWarning()) return;
                Game.Instance?.GameCommandQueue?.CloseScreen(IScreenUIHandler.ScreenType.VendorSelecting, capital);
                EventBus.RaiseEvent<IBeginSelectingVendorHandler>(h => h.HandleExitSelectingVendor());
            }
            catch (Exception e) { Main.Log?.Error("VendorSelectingScreen.Close: " + e); }
        }

        private static string Title()
            => GameText.Or(() => UIStrings.Instance.Vendor.ChooseVendorForTrade, "vendorselect.screen");

        private static string CloseLabel()
            => GameText.Or(() => UIStrings.Instance.CommonTexts.CloseWindow, "action.close");
    }
}
