using Kingmaker.Blueprints.Root.Strings;   // UIStrings (header / rewards caption / Accept)
using Kingmaker.UI.MVVM.VM.TwitchDrops;    // TwitchDropsRewardsVM — note: Kingmaker.UI, NOT Kingmaker.Code.UI
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The Twitch-drops reward popup (<see cref="TwitchDropsRewardsVM"/>, a <c>LootContextVM</c> child raised by
    /// <c>HandleItemRewardsShow</c> — the drops terminal map object's interaction). One list: the awaiting line
    /// while the claim round-trips to Twitch, then EITHER the granted items OR the game's own status reason
    /// (not linked / no connection / no rewards / already received), and the Accept button.
    ///
    /// The item rows are ordinary loot rows (<see cref="ItemNodes.ItemLabel"/> + the card tooltip on Space) —
    /// the same <c>ItemSlotVM</c> grid every other reward window uses. The rewards are already granted by the
    /// time this shows; the rows are display-only.
    ///
    /// Accept and Escape are both gated on <c>IsAwaiting</c>, exactly as the sighted view hides its Accept
    /// button until the request settles — closing mid-await would dispose the VM the pending claim is still
    /// writing into.
    ///
    /// Layer 26, Exclusive: a blocking reward modal above the loot windows (24) it is a sibling of.
    /// </summary>
    public sealed class TwitchDropsScreen : Screen
    {
        public TwitchDropsScreen() { Wrap = true; } // one modal list — Tab wraps

        public override string Key => "overlay.twitchdrops";
        public override int Layer => 26;
        public override bool Exclusive => true;
        public override string ScreenName => Vm() != null ? Title() : null;

        // Surface OR space — the loot context hangs off whichever static part is live.
        private static TwitchDropsRewardsVM Vm()
            => UiContexts.FromLiveStaticPart(
                s => s.LootContextVM?.TwitchDropsRewardsVM?.Value,
                s => s.LootContextVM?.TwitchDropsRewardsVM?.Value);

        public override bool IsActive() => Vm() != null;

        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null && !vm.IsAwaiting.Value)
                yield return new ElementAction(ActionIds.Back, Message.Raw(AcceptLabel()), _ => vm.Close());
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "twitch:" + vm.GetHashCode() + ":";

            b.BeginStop("rewards").PushContext(Title(), Loc.T("role.list"));

            if (vm.IsAwaiting.Value)
                b.AddLabel(ControlId.Structural(k + "awaiting"), () => Loc.T("twitchdrops.awaiting"));

            // The status block and the item grid are mutually exclusive in the VM (a claim either receives
            // items or sets a reason), and each is bound to its own visibility flag — mirror both flags.
            if (vm.HasStatus.Value)
                b.AddLabel(ControlId.Structural(k + "status"), () => Vm()?.StatusText?.Value ?? "");

            var items = vm.HasItems.Value ? vm.SlotsGroup?.VisibleCollection : null;
            if (items != null && items.Count > 0)
            {
                b.AddLabel(ControlId.Structural(k + "itemshdr"), () => GameText.Or(
                    () => UIStrings.Instance.ColonyProjectsRewards.LootRewardsHeader, "twitchdrops.items"));
                foreach (var slot in items)
                {
                    if (slot == null || !slot.HasItem) continue;
                    var s = slot; // loop-local for the closures
                    var ent = s.Item.Value;
                    var vt = GraphNodes.Text(() => ItemNodes.ItemLabel(s));
                    vt.OnTooltip = () => ItemNodes.OpenItemTooltip(s);
                    b.AddItem(ControlId.Referenced(ent, k + "item:" + (ent?.UniqueId ?? "slot")), vt);
                }
            }

            b.PopContext();

            if (vm.IsAwaiting.Value) return; // the sighted Accept button is hidden until the claim settles
            b.BeginStop("actions").PushContext(Loc.T("hud.actions"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(k + "accept"), GraphNodes.Button(AcceptLabel, () => Vm()?.Close()));
            b.PopContext();
        }

        private static string Title()
            => GameText.Or(() => UIStrings.Instance.CargoTexts.CargoRewardsHeader, "twitchdrops.screen");

        private static string AcceptLabel()
            => GameText.Or(() => UIStrings.Instance.CommonTexts.Accept, "action.accept");
    }
}
