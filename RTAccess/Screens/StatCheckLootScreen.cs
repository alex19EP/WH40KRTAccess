using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.StatCheckLoot;    // StatCheckLootVM + pages
using RTAccess.UI;
using RTAccess.UI.Graph;
using UnityEngine;

namespace RTAccess.Screens
{
    /// <summary>
    /// The stat-check-for-loot modal that anomaly research and StatCheckLoot POIs open (a party member rolls
    /// a skill check; the resulting loot window — with the check's outcome — is the existing LootScreen).
    /// Two live VM instances share this one screen: the POI one (view-owned:
    /// <c>ExplorationStatCheckLootPCView.ViewModel.StatCheckLootPointOfInterestVM</c>) and the anomaly one
    /// (<c>SpaceStaticPartVM.AnomalyVM.StatCheckLootAnomalyVM</c>); whichever says <c>ShouldShow</c> wins.
    ///
    /// <b>Main page</b>: one card per required stat — the game preselects the living member with the highest
    /// value; Enter on the card ROLLS the check (the card's own CheckStat), the companion "change character"
    /// button opens the <b>Units page</b> (every candidate as a radio option, Confirm/Back — the game's own
    /// SwitchUnit flow). Escape = the page's own close/back.
    /// </summary>
    public sealed class StatCheckLootScreen : Screen
    {
        public override string Key => "statcheckloot";
        public override int Layer => 26;            // above the tablet (9) / anomaly (9) that raised it
        public override bool Exclusive => true;
        public override string ScreenName => Loc.T("statcheck.screen");

        public override bool IsActive() => Vm() != null;

        // The active instance: POI-flavoured (owned by the tablet's own component view) or anomaly-flavoured.
        private static StatCheckLootVM Vm()
        {
            var a = Game.Instance?.RootUiContext?.SpaceVM?.StaticPartVM?.AnomalyVM?.StatCheckLootAnomalyVM;
            if (a != null && a.ShouldShow.Value) return a;
            var p = PoiVm();
            return p != null && p.ShouldShow.Value ? p : null;
        }

        private static Kingmaker.UI.MVVM.View.Exploration.PC.ExplorationStatCheckLootPCView _poiView;
        private static StatCheckLootPointOfInterestVM PoiVm()
        {
            if (_poiView == null)
                _poiView = UnityEngine.Object.FindAnyObjectByType<Kingmaker.UI.MVVM.View.Exploration.PC.ExplorationStatCheckLootPCView>(
                    FindObjectsInactive.Include);
            return _poiView != null ? _poiView.ViewModel?.StatCheckLootPointOfInterestVM : null;
        }

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "scl:" + vm.GetHashCode() + ":";

            if (vm.CurrentPageType.Value == StatCheckLootPageType.Units)
            {
                var units = vm.StatCheckLootUnitsPageVM;
                b.PushContext(Loc.T("statcheck.pick_unit"), role: "list");
                int u = 0;
                foreach (var card in units.SmallUnitSlotsVMs)
                {
                    var c = card; // capture
                    if (c == null) continue;
                    b.AddItem(ControlId.Referenced(c, k + "unit:" + u++), GraphNodes.ChoiceOption(
                        () => c.UnitName.Value + ", " + c.StatValue.Value,
                        () => c.IsSelected.Value,
                        () => c.SelectUnit()));
                }
                b.AddItem(ControlId.Structural(k + "confirm"), GraphNodes.Button(
                    () => Loc.T("statcheck.confirm_unit"), () => units.ConfirmUnit()));
                b.PopContext();
                return;
            }

            var main = vm.StatCheckLootMainPageVM;
            b.PushContext(Loc.T("statcheck.screen"), role: "list");
            foreach (var slot in main.UnitSlotVMByStatType)
            {
                var card = slot.Value;
                if (card == null) continue;
                string stat = slot.Key.ToString();
                // Enter ROLLS the check with the shown member; the sibling button swaps the member first.
                b.AddItem(ControlId.Referenced(card, k + "check:" + stat), GraphNodes.Button(
                    () => Loc.T("statcheck.roll", new
                    {
                        stat = card.StatName.Value,
                        name = card.UnitName.Value,
                        value = card.StatValue.Value,
                    }),
                    () => card.CheckStat()));
                b.AddItem(ControlId.Referenced(card, k + "switch:" + stat), GraphNodes.Button(
                    () => Loc.T("statcheck.switch", new { stat = card.StatName.Value }),
                    () => card.SwitchUnit()));
            }
            b.PopContext();
        }

        // Escape: the units page backs out without confirming; the main page closes the dialog (the POI
        // stays uninteracted, retryable) — both the game's own paths.
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm == null) yield break;
            yield return new ElementAction(ActionIds.Back, Message.Raw(GameText.Action("close")), _ =>
            {
                if (vm.CurrentPageType.Value == StatCheckLootPageType.Units)
                    vm.StatCheckLootUnitsPageVM.BackWithoutConfirmUnit();
                else
                    vm.StatCheckLootMainPageVM.CloseDialog();
            });
        }
    }
}
