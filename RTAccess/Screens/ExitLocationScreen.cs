using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Loot;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's "collect all before you leave?" confirm (<see cref="ExitLocationWindowVM"/>) surfaced accessibly.
    /// It is raised by the game's own collect-all path on a ZoneExit loot prompt
    /// (<c>LootCollectorVM.CollectAll → LootVM.HandleOpenExitWindow</c>) — which our <see cref="LootScreen"/> Take-all
    /// invokes verbatim rather than reimplementing. Header + description + hint are read as the screen name;
    /// <b>Collect all and leave</b> = <see cref="ExitLocationWindowVM.Confirm"/> (collect everything + area transition),
    /// <b>Cancel</b> / Back = <see cref="ExitLocationWindowVM.Decline"/> (return to the loot list, no transition).
    ///
    /// Built as a plain <see cref="ListContainer"/> of buttons in OnPush — the same proven modal shape as
    /// <see cref="VariativeInteractionScreen"/> (a FlowSheet is for item tables and left this two-choice confirm
    /// without a reachable focus target). Exclusive, layer 26 — directly above the LootScreen (24) that spawned it.
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

        private static ExitLocationWindowVM Vm()
        {
            var rc = Game.Instance?.RootUiContext;
            var loot = rc?.SurfaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value
                    ?? rc?.SpaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value;
            return loot?.ExitLocationWindowVM?.Value;
        }

        private ExitLocationWindowVM _built;

        // Build in OnPush so a focus target exists before the navigator attaches; rebuild only if the game swaps
        // the confirm VM under us.
        public override void OnPush() { _built = null; Rebuild(); }
        public override void OnPop() { Clear(); _built = null; }
        public override void OnUpdate() { Rebuild(); }

        private void Rebuild()
        {
            var vm = Vm();
            if (vm == null || vm == _built) return;
            _built = vm;
            Clear();
            var list = new ListContainer();
            // Accept → the game's Confirm(): collect everything, close the loot window, fire the area transition.
            list.Add(new ProxyActionButton(Loc.T("loot.exit_accept"), () => true, () => Vm()?.Confirm(), actionVerb: "choose"));
            // Decline → return to the loot list (no transition). Also on Escape (Back), below.
            list.Add(new ProxyActionButton(Loc.T("action.cancel"), () => true, () => Vm()?.Decline(), actionVerb: "choose"));
            Add(list);
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
