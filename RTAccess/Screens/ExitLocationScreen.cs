using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings.CommonTexts — the card's Accept / Cancel texts
using Kingmaker.Code.UI.MVVM.VM.Loot;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's "collect all before you leave?" confirm (<see cref="ExitLocationWindowVM"/>) surfaced accessibly.
    /// It is raised by the game's own collect-all path on a ZoneExit loot prompt
    /// (<c>LootCollectorVM.CollectAll → LootVM.HandleOpenExitWindow</c>) — which our <see cref="LootScreen"/> Take-all
    /// invokes verbatim rather than reimplementing. Header + description + hint are read as the screen name;
    /// <b>Accept</b> = <see cref="ExitLocationWindowVM.Confirm"/> (collect everything + area transition),
    /// <b>Cancel</b> / Back = <see cref="ExitLocationWindowVM.Decline"/> (return to the loot list, no transition).
    /// Button labels mirror the card: <c>ExitLocationWindowBaseView.BindViewImplementation</c> writes
    /// <c>UIStrings.CommonTexts.Accept</c>/<c>Cancel</c> on the buttons, so they pass through here (and follow
    /// the game's language), with the mod's locale keys as fallbacks.
    ///
    /// Graph-native: the two buttons declared fresh from the live VM every render. Node keys carry the VM's
    /// identity, so a confirm swapped under us drops the old keys and focus re-homes with a fresh readout —
    /// no rebuild bookkeeping. Exclusive, layer 26 — directly above the LootScreen (24) that spawned it.
    /// </summary>
    public sealed class ExitLocationScreen : Screen
    {
        public override string Key => "loot.exit";
        public override int Layer => 26;
        public override bool Exclusive => true;

        // Read the whole prompt on open: "Exit area. <description>. <collect-all hint>."
        public override string ScreenName
        {
            get { var vm = Vm(); return vm == null ? null : Join(vm.Header, vm.Description, vm.AdditionalInformation); }
        }

        public override bool IsActive() => Vm() != null;

        private static ExitLocationWindowVM Vm() => UiContexts.Loot()?.ExitLocationWindowVM?.Value;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "exitloc:" + vm.GetHashCode() + ":";

            // Accept → the game's Confirm(): collect everything, close the loot window, fire the area
            // transition. The fallback keeps the old descriptive label should the game string go missing.
            b.AddItem(ControlId.Structural(k + "accept"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.CommonTexts.Accept, "loot.exit_accept"),
                () => Vm()?.Confirm()));
            // Cancel → return to the loot list (no transition). Also on Escape (Back), below.
            b.AddItem(ControlId.Structural(k + "cancel"), GraphNodes.Button(
                () => GameText.Action("cancel"),
                () => Vm()?.Decline()));
        }

        // Back (Escape) = decline: dismiss the confirm and return to the loot list; the transition does NOT fire.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.cancel"), _ => Vm()?.Decline());
        }

        private static string Join(params string[] parts)
        {
            var bits = new List<string>();
            foreach (var p in parts) if (!string.IsNullOrWhiteSpace(p)) bits.Add(p);
            return string.Join(". ", bits.ToArray());
        }
    }
}
