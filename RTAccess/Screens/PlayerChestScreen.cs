using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Loot;
using Kingmaker.Code.UI.MVVM.VM.Slots;   // ItemSlotVM, SlotsGroupVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's PlayerChest loot window (<see cref="LootVM.IsPlayerStash"/>) — the player's personal shared stash —
    /// as a mod-owned navigable screen. It is a two-way store: the <b>Chest</b> holds items you've stowed, and your
    /// <b>Inventory</b> is the party's carried gear; you move items either way. So the body — graph-native, declared
    /// fresh from the live VM each render — is two regions in one stop (arrows walk through both, Ctrl+Up/Down jumps
    /// between them): the chest items (Enter WITHDRAWS to inventory) and the party items (Enter DEPOSITS to the
    /// chest) — each an <see cref="ItemNodes.StashItem"/> driving the game's own per-slot handler (see there). Both
    /// panels are always shown by the sighted UI too. A moved item's node vanishes from its side on the next render,
    /// so focus slides to the nearest remaining item there and announces it (it re-emits on the other side under a
    /// fresh key, so focus never chases it across).
    ///
    /// v1 covers the two-way stash; the window's separate <b>cargo</b> panel (ship trade goods, <c>CargoInventory</c>)
    /// is deferred — cargo is a distinct system the inventory screen doesn't surface yet either. Escape closes via the
    /// window's own <see cref="LootVM.Close"/>. Exclusive, layer 24 — alongside the other loot / world-interaction
    /// modals; the plain <see cref="LootScreen"/> and <see cref="OneSlotLootScreen"/> both exclude PlayerChest, so
    /// exactly one loot screen is ever active.
    /// </summary>
    public sealed class PlayerChestScreen : Screen
    {
        public override string Key => "loot.playerchest";
        public override int Layer => 24;
        public override bool Exclusive => true;

        // Spoken on open (OnFocus): the chest's own display name (no ServiceWindowAnnounce fires for loot).
        public override string ScreenName
        {
            get { var vm = Vm(); return vm == null ? null : (Nz(vm.PlayerStash?.LootDisplayName) ?? Loc.T("stash.title")); }
        }

        public override bool IsActive() { var vm = Vm(); return vm != null && vm.IsPlayerStash; }

        // Back (Escape) closes the chest window via its own close callback (the game's OnLootClosed + DisposeLoot).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.Close());
        }

        // Loot opens on the planet surface AND in the star-system/space context; resolve from whichever static part is
        // live (the LootContextVM is a sibling of ServiceWindowsVM on both).
        private static LootVM Vm()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.SurfaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value
                ?? rc?.SpaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value;
        }

        // The chest slot group is the first ContextLoot view (all stash items in PlayerChest mode); the party inventory
        // is InventoryStash's group. Both rebuild their visible collection on every transfer.
        private static SlotsGroupVM<ItemSlotVM> ChestGroup(LootVM vm)
            => vm.ContextLoot != null && vm.ContextLoot.Count > 0 ? vm.ContextLoot[0]?.SlotsGroup : null;

        private static SlotsGroupVM<ItemSlotVM> PartyGroup(LootVM vm) => vm.InventoryStash?.ItemSlotsGroup;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "chest:" + vm.GetHashCode() + ":"; // a new LootVM = a fresh window = fresh keys

            BuildSide(b, k + "c", Loc.T("stash.chest"), ChestGroup(vm), fromChest: true, "stash.chest_empty");
            BuildSide(b, k + "p", Loc.T("stash.inventory"), PartyGroup(vm), fromChest: false, "stash.inventory_empty");
        }

        // One side of the stash: a region (a Ctrl+arrow jump target) whose context title announces which side
        // you're entering. Item keys are ENTITY-based and STRUCTURAL only: the game rebuilds every slot VM on
        // each transfer, so an entity key keeps the cursor put while the list churns — but deliberately NO
        // reference tier, because a moved entity re-emits on the OTHER side and a reference key would
        // tier-1-drag focus across with it; the two-list rule is "stay in the list you're working in".
        private static void BuildSide(GraphBuilder b, string k, string title,
            SlotsGroupVM<ItemSlotVM> group, bool fromChest, string emptyKey)
        {
            b.SetRegion(k);
            b.PushContext(title, Loc.T("role.list"));
            bool any = false;
            var vis = group?.VisibleCollection;
            if (vis != null)
                foreach (var slot in vis)
                {
                    if (slot == null || !slot.HasItem) continue;
                    var ent = slot.Item.Value;
                    b.AddItem(ControlId.Structural(k + ":" + ent.UniqueId),
                        ItemNodes.StashItem(slot, fromChest));
                    any = true;
                }
            if (!any)
                b.AddItem(ControlId.Structural(k + ":empty"), GraphNodes.Text(() => Loc.T(emptyKey)));
            b.PopContext();
        }

        private static string Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
