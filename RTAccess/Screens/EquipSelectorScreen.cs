using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;                    // UIStrings
using Kingmaker.Code.UI.MVVM.VM.SelectorWindow;             // InventorySelectorWindowVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;   // InventoryVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's empty-slot equip selector (<see cref="InventorySelectorWindowVM"/>) as a graph-native screen.
    /// Opening a doll slot — Enter on an empty one, or the "Choose item" context action on a filled one — calls
    /// <c>InventoryDollVM.HandleChangeItem(slot)</c>, which builds the equippable party items (the currently worn
    /// item, if any, at the head so it can be taken off) and sets the doll's <c>InventorySelectorWindowVM</c>.
    /// This screen mirrors that collection immediate-mode: each row is an <see cref="ItemNodes.EquipCandidate"/>
    /// whose Enter equips (or, on the equipped head row, unequips) through the window's own Confirm/Unequip
    /// callbacks — the game's EquipItem command — which close the window on success. The equipped head row is
    /// detected LIVE, so an unequip that keeps the window open flips it back to a plain candidate and the live
    /// label announces the flip. Candidates key by their item ENTITY, so the list re-homes sensibly across an
    /// unequip. Escape backs out (<see cref="SelectorWindowVM{T}.Back"/> → the doll's HideSelectionWindow).
    ///
    /// Exclusive, layer 12 — just above the <see cref="InventoryScreen"/> (10) it's raised from, so it owns input
    /// while open and suppresses the doll beneath. Not a ServiceWindowsType, so it carries its own ScreenName
    /// announce (the game's own "Choose item" header).
    /// </summary>
    public sealed class EquipSelectorScreen : Screen
    {
        public override string Key => "inventory.equipselector";
        public override int Layer => 12;
        public override bool Exclusive => true;
        public override string ScreenName
            => Selector() != null ? UIStrings.Instance.InventoryScreen.ChooseItem.Text : null;

        public override bool IsActive() => Selector() != null;

        // Back (Escape) declines the selector through its own callback (the game's HideSelectionWindow → Dispose).
        public override IEnumerable<ElementAction> GetActions()
        {
            var sel = Selector();
            if (sel != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => sel.Back());
        }

        // The selector lives on the currently-viewed doll of whichever inventory window is live (surface or space),
        // mirroring InventoryScreen.Vm()'s resolution.
        private static InventorySelectorWindowVM Selector()
        {
            var rc = Game.Instance?.RootUiContext;
            var vm = rc?.SurfaceVM?.StaticPartVM?.ServiceWindowsVM?.InventoryVM?.Value
                  ?? rc?.SpaceVM?.StaticPartVM?.ServiceWindowsVM?.InventoryVM?.Value;
            return vm?.DollVM?.InventorySelectorWindowVM?.Value;
        }


        public override void Build(GraphBuilder b)
        {
            var sel = Selector();
            if (sel == null) return;
            string k = "equipsel:" + sel.GetHashCode() + ":"; // a new selector window = fresh keys

            b.PushContext(UIStrings.Instance.InventoryScreen.ChooseItem.Text, Loc.T("role.list"));
            var col = sel.EntitiesCollection;
            if (col != null)
            {
                int i = 0;
                foreach (var c in col)
                {
                    if (c == null) continue;
                    var cand = c;
                    // Key by the candidate's item entity so an unequip (which keeps the window open and re-deals
                    // the list) re-homes focus by identity; structural index is the fallback for a null item.
                    var ent = cand.Item;
                    var id = ent != null
                        ? ControlId.Referenced(ent, k + "cand:" + ent.UniqueId)
                        : ControlId.Structural(k + "cand:" + i);
                    b.AddItem(id, ItemNodes.EquipCandidate(cand, sel));
                    i++;
                }
            }
            b.PopContext();
        }
    }
}
