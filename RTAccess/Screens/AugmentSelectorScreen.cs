using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings;                    // UIStrings
using Kingmaker.Code.UI.MVVM.VM.SelectorWindow;             // AugmentationsSelectorWindowVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The augmentations window's item selector (<see cref="AugmentationsSelectorWindowVM"/>) as a
    /// graph-native screen — the <see cref="EquipSelectorScreen"/> shape against the augment window.
    /// Enter on a body-slot row calls <c>AugmentationsVM.HandleChangeItem(slot)</c>, which builds the
    /// suitable augments (the currently installed one, if any and not the slot's default, at the head)
    /// and sets the window's <c>InventorySelectorWindowVM</c>. Each row is an
    /// <see cref="ItemNodes.EquipCandidate"/> whose Enter equips through the window's own Confirm (the
    /// game's EquipItem command — the slot goes DIRTY, to be committed with Install) or, on the equipped
    /// head row, takes it off via the window's own <c>Unequip</c>. Escape backs out
    /// (<c>SelectorWindowVM.Back</c> → the augment VM's HideSelectionWindow).
    ///
    /// Exclusive, layer 12 — just above the <see cref="AugmentationsScreen"/> (10) it's raised from, the
    /// EquipSelectorScreen convention. Not a ServiceWindowsType, so it carries its own ScreenName
    /// announce (the game's own "Choose item" header).
    /// </summary>
    public sealed class AugmentSelectorScreen : Screen
    {
        public override string Key => "augments.equipselector";
        public override int Layer => 12;
        public override bool Exclusive => true;
        public override string ScreenName
            => Selector() != null ? UIStrings.Instance.InventoryScreen.ChooseItem.Text : null;

        public override bool IsActive() => Selector() != null;

        // Back (Escape) declines the selector through its own callback (HideSelectionWindow → Dispose).
        public override IEnumerable<ElementAction> GetActions()
        {
            var sel = Selector();
            if (sel != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => sel.Back());
        }

        private static AugmentationsSelectorWindowVM Selector()
            => UiContexts.Augmentations()?.InventorySelectorWindowVM?.Value;


        public override void Build(GraphBuilder b)
        {
            var sel = Selector();
            if (sel == null) return;
            string k = "augsel:" + sel.GetHashCode() + ":"; // a new selector window = fresh keys

            b.PushContext(UIStrings.Instance.InventoryScreen.ChooseItem.Text, Loc.T("role.list"));
            var col = sel.EntitiesCollection;
            if (col != null)
            {
                int i = 0;
                foreach (var c in col)
                {
                    if (c == null) continue;
                    var cand = c;
                    // Key by the candidate's item entity so an unequip (which keeps the window open and
                    // re-deals the list) re-homes focus by identity; structural index is the fallback.
                    var ent = cand.Item;
                    var id = ent != null
                        ? ControlId.Referenced(ent, k + "cand:" + ent.UniqueId)
                        : ControlId.Structural(k + "cand:" + i);
                    b.AddItem(id, ItemNodes.EquipCandidate(cand, sel, sel.Unequip));
                    i++;
                }
            }
            b.PopContext();
        }
    }
}
