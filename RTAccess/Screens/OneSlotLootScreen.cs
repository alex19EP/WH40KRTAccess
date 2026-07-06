using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Loot;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's OneSlot loot window (<see cref="LootVM.IsOneSlot"/>) — a device/mechanism you INSERT a party item
    /// into (a fuse, a sacred cog, a key component) — as a mod-owned navigable screen. Interacting with such a device
    /// already opens this window today, invisibly swallowing the keyboard; this screen makes it usable.
    ///
    /// Unlike a chest (read + take), OneSlot has a target slot on the device and a list of party items you may put in
    /// it. So the body — graph-native, declared fresh from the live VM each render — is one list: an optional
    /// <b>remove-what's-inserted</b> row when the slot is filled (<see cref="ItemNodes.InsertedItem"/> — the game's
    /// own eject back to the party), then the party items that satisfy the device's insert condition
    /// (<see cref="InsertableLootSlotVM.CanInsert"/>), each of which Enter INSERTS via the game's own
    /// <c>InventoryHelper.InsertToInteractionSlot</c> (<see cref="ItemNodes.InsertItem"/> — the same call
    /// <c>LootVM.HandleTryInsertSlot</c> makes for the sighted click: it ejects any current item back to the party,
    /// then transfers the chosen one in). Inserting does NOT close the window (an authored put-trigger may fire);
    /// the inserted item's row simply vanishes from the next render — focus slides to a survivor and announces it —
    /// while the remove row appears. Escape closes via the window's own <see cref="LootVM.Close"/>.
    ///
    /// Exclusive, layer 24 — alongside the other world-interaction modals (LootScreen / Variative / Transition); loot
    /// is triggered from exploration, so it never stacks with a service window. The plain <see cref="LootScreen"/>
    /// gates OneSlot out of its supported modes, so exactly one of the two is ever active for a given window.
    /// </summary>
    public sealed class OneSlotLootScreen : Screen
    {
        public override string Key => "loot.oneslot";
        public override int Layer => 24;
        public override bool Exclusive => true;

        // Spoken on open (OnFocus): the device's own name + prompt (e.g. "Reliquary. Insert the sacred cog.").
        public override string ScreenName
        {
            get
            {
                var slot = Vm()?.InteractionSlot;
                if (slot == null) return null;
                var name = Join(slot.Name, slot.Description);
                return string.IsNullOrWhiteSpace(name) ? Loc.T("insert.title") : name;
            }
        }

        public override bool IsActive() { var vm = Vm(); return vm != null && vm.IsOneSlot; }

        // Back (Escape) closes the device window via its own close callback (the game's OnLootClosed + DisposeLoot).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.Close());
        }

        // OneSlot opens on the planet surface AND in the star-system/space context; resolve from whichever static
        // part is live (the LootContextVM is a sibling of ServiceWindowsVM on both).
        private static LootVM Vm()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.SurfaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value
                ?? rc?.SpaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value;
        }

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "oneslot:" + vm.GetHashCode() + ":"; // a new LootVM = a fresh window = fresh keys

            var slot = vm.InteractionSlot;
            var title = slot != null && !string.IsNullOrWhiteSpace(slot.Name) ? slot.Name : Loc.T("insert.title");
            b.PushContext(title, Loc.T("role.list"));

            // If the device slot already holds an item, offer to pull it back out (the game's own eject: clicking
            // the filled slot collects it to the party — LootVM.HandleChangeLoot → InventoryHelper.TryCollectLootSlot).
            // Keyed STRUCTURALLY (no reference tier): after an insert the moved entity re-emits HERE, and a
            // reference key on it would tier-1-drag focus from the candidate list onto this row.
            var inSlot = slot?.ItemSlot?.Value;
            bool filled = inSlot != null && inSlot.HasItem;
            if (filled)
                b.AddItem(ControlId.Structural(k + "in"), ItemNodes.InsertedItem(inSlot));

            // The party items that satisfy the device's insert condition; Enter puts one in. Entity-based keys:
            // the game rebuilds every slot VM on each insert/eject, so the entity is what keeps the cursor put
            // while the list churns (and its removal is what slides focus to a survivor).
            bool any = false;
            var vis = vm.InventoryStash?.InsertableSlotsGroup?.VisibleCollection;
            if (vis != null && slot != null)
                foreach (var s in vis)
                {
                    if (s == null || !s.HasItem || !s.CanInsert.Value) continue;
                    var ent = s.Item.Value;
                    b.AddItem(ControlId.Referenced(ent, k + "cand:" + ent.UniqueId),
                        ItemNodes.InsertItem(s, slot));
                    any = true;
                }

            // Nothing to insert and nothing to remove — a focusable line so Enter or Escape both dismiss.
            if (!any && !filled)
                b.AddItem(ControlId.Structural(k + "none"),
                    GraphNodes.Button(() => Loc.T("insert.none"), () => Vm()?.Close()));

            b.PopContext();
        }

        private static string Join(params string[] parts)
        {
            var bits = new List<string>();
            foreach (var p in parts) if (!string.IsNullOrWhiteSpace(p)) bits.Add(p);
            return string.Join(". ", bits.ToArray());
        }
    }
}
