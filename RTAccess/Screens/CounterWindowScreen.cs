using System;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;         // UIStrings (the window's own operation / close labels)
using Kingmaker.Code.UI.MVVM.VM.CounterWindow;   // CounterWindowVM, CounterWindowType
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's quantity-picker modal (<see cref="CounterWindowVM"/> on <c>CommonVM.CounterWindowVM</c>) —
    /// raised by Split (our context menus → the game's <c>InventoryHelper.TrySplitSlot</c>), by Shift+drag
    /// onto an empty slot, and by partial Drop/Move flows. Mirrors the window: the item line (name + stack
    /// count), a SLIDER over the count (Left/Right step 1, Ctrl = 10, clamped to the window's own 1..Max;
    /// the spoken value mirrors the card's "{take}/{other}" readout — for Split the second number is what
    /// STAYS behind, exactly what the sighted player sees), the operation button (the game's own
    /// Split/Drop/Move label → <c>Accept()</c>, which fires the flow's count callback and closes), and the
    /// Close button. The VM's <c>CurrentValue</c> is a plain field the game's slider mutates directly — we
    /// write it the same way. No Back action on purpose: the game's own EscHotkeyManager already closes
    /// this window on Escape (the MessageBoxScreen convention), so declaring our own would double-Close.
    ///
    /// Layer 29, Exclusive — a modal above the service windows / loot screens that raise it, just under
    /// the MessageBoxScreen (30) so a confirm box on top would win. ScreenName = the operation's own label.
    /// </summary>
    public sealed class CounterWindowScreen : Screen
    {
        public CounterWindowScreen() { Wrap = true; } // Tab cycles item ↔ count ↔ buttons

        public override string Key => "overlay.counter";
        public override int Layer => 29;
        public override bool Exclusive => true; // a modal owns the keyboard
        public override string ScreenName
        {
            get { var vm = Vm(); return vm != null ? OperationLabel(vm) : null; }
        }

        public override bool IsActive() => Vm() != null;

        private static CounterWindowVM Vm()
            => Game.Instance?.RootUiContext?.CommonVM?.CounterWindowVM?.Value;

        // The operation name the window titles its accept button with (the game's own strings).
        private static string OperationLabel(CounterWindowVM vm) => vm.OperationType switch
        {
            CounterWindowType.Drop => UIStrings.Instance.ActionTexts.DropItem.Text,
            CounterWindowType.Split => UIStrings.Instance.ActionTexts.SplitItem.Text,
            CounterWindowType.Move => UIStrings.Instance.ActionTexts.MoveItem.Text,
            _ => "",
        };


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "counter:" + vm.GetHashCode() + ":"; // a new window = fresh keys

            // The item being counted: name + stack size (the window's header line).
            b.BeginStop("item").AddItem(ControlId.Structural(k + "item"), GraphNodes.Text(
                () => vm.ItemName + " (" + Loc.T("item.count", new { count = vm.ItemCount }) + ")"));

            // The count slider. Value speech mirrors the card's "{0}/{1}" counter text verbatim
            // (CounterWindowPCView.SetCounterText): for Split the right-hand number is the remainder that
            // stays behind, otherwise it's the maximum.
            Func<string> value = () => vm.CurrentValue + "/" +
                (vm.OperationType == CounterWindowType.Split ? vm.MaxValue - vm.CurrentValue + 1 : vm.MaxValue);
            b.BeginStop("count").AddItem(ControlId.Structural(k + "count"), new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new System.Collections.Generic.List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => Loc.T("counter.count")),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = value,
                StateText = value, // spoken (interrupting) after each adjust — key-repeat friendly
                OnAdjust = (sign, large) =>
                {
                    int step = large ? 10 : 1;
                    vm.CurrentValue = Math.Max(1, Math.Min(vm.MaxValue, vm.CurrentValue + sign * step));
                    UiSound.Play(Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Settings?.SettingsSliderMove);
                },
            });

            // The operation button (Split/Drop/Move — the game's own label): Accept() invokes the flow's
            // count callback (e.g. GameCommandQueue.SplitSlot) and closes the window.
            b.BeginStop("accept").AddItem(ControlId.Structural(k + "accept"),
                GraphNodes.Button(() => OperationLabel(vm), vm.Accept));
            b.BeginStop("close").AddItem(ControlId.Structural(k + "close"),
                GraphNodes.Button(() => UIStrings.Instance.CommonTexts.CloseWindow.Text, vm.Close));
        }
    }
}
