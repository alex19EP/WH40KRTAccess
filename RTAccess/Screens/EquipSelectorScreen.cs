using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;                    // UIStrings
using Kingmaker.Code.UI.MVVM.VM.SelectorWindow;             // InventorySelectorWindowVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;   // InventoryVM
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's empty-slot equip selector (<see cref="InventorySelectorWindowVM"/>) as a mod-owned navigable
    /// screen. Opening a doll slot — Enter on an empty one, or the "Choose item" context action on a filled one —
    /// calls <c>InventoryDollVM.HandleChangeItem(slot)</c>, which builds the equippable party items (the currently
    /// worn item, if any, at the head so it can be taken off) and sets the doll's <c>InventorySelectorWindowVM</c>.
    /// This screen mirrors that collection: each row is a <see cref="ProxyEquipCandidate"/> whose Enter equips
    /// (or, on the equipped head row, unequips) through the window's own Confirm/Unequip callbacks — the game's
    /// EquipItem command — which close the window on success. Escape backs out (<see cref="SelectorWindowVM{T}.Back"/>
    /// → the doll's HideSelectionWindow).
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

        private Panel _content;
        private FlowSheet _sheet;
        private object _builtFor;  // the selector VM instance we last built for
        private int _builtCount;   // its candidate count when built (rebuild if it changes, e.g. after an unequip)

        public override void OnPush() { _builtFor = null; }
        public override void OnPop() { Clear(); _content = null; _sheet = null; _builtFor = null; }

        // Back (Escape) declines the selector through its own callback (the game's HideSelectionWindow → Dispose).
        public override IEnumerable<ElementAction> GetActions()
        {
            var sel = Selector();
            if (sel != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => sel.Back());
        }

        public override void OnUpdate()
        {
            var sel = Selector();
            if (sel == null) return;
            int count = sel.EntitiesCollection?.Count ?? 0;
            if (!ReferenceEquals(sel, _builtFor) || count != _builtCount)
            {
                _builtFor = sel;
                _builtCount = count;
                Build(sel);
            }
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

        private void Build(InventorySelectorWindowVM sel)
        {
            Clear();
            _content = new Panel();
            var sheet = new FlowSheet();
            var list = sheet.List(UIStrings.Instance.InventoryScreen.ChooseItem.Text);
            var col = sel.EntitiesCollection;
            if (col != null)
                foreach (var c in col)
                    if (c != null) list.Item(new ProxyEquipCandidate(c, sel));
            sheet.Reflow();
            _sheet = sheet;
            _content.Add(sheet);
            Add(_content);
        }
    }
}
