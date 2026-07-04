using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Exploration;      // AnomalyVM
using Kingmaker.PubSubSystem;                     // IAnomalyUIHandler
using Kingmaker.PubSubSystem.Core;                // EventBus
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The anomaly research window (the side panel <c>IAnomalyUIHandler.OpenAnomalyScreen</c> raises on the
    /// system map — auto-opened when anomaly research starts, or from an overtip's info button). Mirrors
    /// <see cref="AnomalyVM"/> (on <c>SpaceStaticPartVM</c>): name, description, explored state, and the
    /// Visit button (the VM's own MoveShip). Its stat-check sub-flow is the shared StatCheckLootScreen.
    ///
    /// The VM keeps NO shown-state — only Show/Hide ReactiveCommands the game's view listens to — so this
    /// screen tracks the same commands (subscribed lazily per VM instance from <see cref="IsActive"/>, which
    /// the ScreenManager polls every frame). Escape closes through the game's own close handler event.
    /// </summary>
    public sealed class AnomalyScreen : Screen
    {
        public override string Key => "anomaly";
        public override int Layer => 9;             // sibling of the exploration tablet (never coexists)
        public override bool Exclusive => true;
        public override string ScreenName
            => (Vm()?.AnomalyName.Value ?? "") + ", " + Loc.T("systemmap.type_anomaly");

        private AnomalyVM _tracked;    // the VM instance the Show/Hide subscriptions belong to
        private IDisposable _showSub, _hideSub;
        private bool _shown;

        public override bool IsActive()
        {
            var vm = Vm();
            if (vm == null) { _shown = false; return false; }
            if (!ReferenceEquals(vm, _tracked))
            {
                _showSub?.Dispose();
                _hideSub?.Dispose();
                _tracked = vm;
                _shown = false; // missed opens before we subscribed are unrecoverable; fresh VM starts hidden
                _showSub = UniRx.ObservableExtensions.Subscribe(vm.Show, _ => _shown = true);
                _hideSub = UniRx.ObservableExtensions.Subscribe(vm.Hide, _ => _shown = false);
            }
            return _shown;
        }

        private static AnomalyVM Vm() => Game.Instance?.RootUiContext?.SpaceVM?.StaticPartVM?.AnomalyVM;

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "anom:" + vm.GetHashCode() + ":";

            b.AddLabel(ControlId.Structural(k + "name"), () =>
                vm.AnomalyName.Value
                + (vm.IsFullyScanned.Value ? ", " + Loc.T("systemmap.explored") : ""));
            b.AddLabel(ControlId.Structural(k + "desc"), () => vm.AnomalyDescription.Value ?? "");
            b.AddItem(ControlId.Structural(k + "visit"), GraphNodes.Button(
                () => vm.VisitButtonLabel.Value,        // the game's own Visit/Explored label
                () =>
                {
                    Accessibility.SpaceEvents.MarkCommandedMove();
                    vm.VisitAnomaly();                  // the VM's own MoveShip command
                    Tts.Speak(Loc.T("systemmap.traveling_to", new { name = vm.AnomalyName.Value }), interrupt: true);
                    EventBus.RaiseEvent<IAnomalyUIHandler>(h => h.CloseAnomalyScreen());
                }));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Raw(GameText.Action("close")),
                _ => EventBus.RaiseEvent<IAnomalyUIHandler>(h => h.CloseAnomalyScreen()));
        }
    }
}
