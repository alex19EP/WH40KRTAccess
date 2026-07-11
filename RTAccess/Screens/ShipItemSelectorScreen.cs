using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings;                    // UIStrings (the "Choose item" header)
using Kingmaker.Code.UI.MVVM.VM.SelectorWindow;             // ShipItemSelectorWindowVM
using Kingmaker.Code.UI.MVVM.VM.ShipCustomization;          // ShipComponentItemSlotVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The ship component picker (<see cref="ShipItemSelectorWindowVM"/>) as a graph-native screen. Enter
    /// on a component slot of the ship window's Components tab calls the game's own
    /// <c>ShipUpgradeVm.HandleChangeItem</c>, which builds the compatible party items (the currently
    /// installed component, if any, at the head) into this selector. Each row equips through the window's
    /// own callbacks, exactly like the sighted flow (<c>ShipSelectorWindowPCView</c>): Enter on a candidate
    /// = <c>Confirm</c> (→ <c>slot.InsertItem</c> → the queued EquipItem command) then close; Enter on the
    /// installed head row = <c>Unequip</c> (the game's guarded take-off) then close; Escape = <c>Back</c>
    /// (the game's HideSelectionWindow). Space reads the candidate's item card.
    ///
    /// Exclusive, layer 12 — directly above the <see cref="ShipCustomizationScreen"/> (10) it's raised
    /// from, the <see cref="EquipSelectorScreen"/> shape. Not a ServiceWindowsType, so it carries its own
    /// ScreenName (the game's "Choose item" header).
    /// </summary>
    public sealed class ShipItemSelectorScreen : Screen
    {
        public override string Key => "ship.itemselector";
        public override int Layer => 12;
        public override bool Exclusive => true;
        public override string ScreenName
            => Selector() != null ? UIStrings.Instance.InventoryScreen.ChooseItem.Text : null;

        public override bool IsActive() => Selector() != null;

        public override IEnumerable<ElementAction> GetActions()
        {
            var sel = Selector();
            if (sel != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => sel.Back());
        }

        // The selector hangs off the Upgrade tab's VM (which the window keeps alive on every tab; the
        // picker itself only ever opens from the Components tab).
        private static ShipItemSelectorWindowVM Selector()
            => ShipCustomizationScreen.Vm()?.ShipUpgradeVm?.ShipSelectorWindowVM?.Value;


        public override void Build(GraphBuilder b)
        {
            var sel = Selector();
            if (sel == null) return;
            string k = "shipsel:" + sel.GetHashCode() + ":"; // a new picker window = fresh keys

            b.PushContext(UIStrings.Instance.InventoryScreen.ChooseItem.Text, Loc.T("role.list"));
            var col = sel.EntitiesCollection;
            if (col != null)
            {
                int i = 0;
                foreach (var c in col)
                {
                    if (c == null) continue;
                    var cand = c;
                    var ent = cand.Item;
                    var id = ent != null
                        ? ControlId.Referenced(ent, k + "cand:" + ent.UniqueId)
                        : ControlId.Structural(k + "cand:" + i);
                    b.AddItem(id, CandidateNode(sel, cand));
                    i++;
                }
            }
            b.PopContext();
        }

        // One candidate row — "name[, (equipped)]". The installed component is detected the way the PC
        // view does (its ItemEntity has an Owner); Enter takes it off, any other row confirm-equips. Both
        // then close through the window's own Back, mirroring ShipSelectorWindowPCView.OnConfirm/Unequip.
        private static NodeVtable CandidateNode(ShipItemSelectorWindowVM sel, ShipComponentItemSlotVM cand)
        {
            System.Func<bool> equipped = () => cand.Item?.Owner != null;
            System.Func<string> label = () =>
            {
                var name = cand.DisplayName;
                if (string.IsNullOrEmpty(name)) name = cand.Item?.Name;
                if (string.IsNullOrEmpty(name)) name = Loc.T("item.unknown");
                return equipped() ? name + " (" + Loc.T("inv.equipped") + ")" : name;
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                },
                SearchText = label,
                OnActivate = () =>
                {
                    if (equipped()) sel.Unequip();
                    else { sel.SetCurrentSelected(cand); sel.Confirm(cand); }
                    sel.Back(); // the PC view closes after either verb (OnConfirm/Unequip → OnClose)
                },
                OnTooltip = () => TooltipChooser.OpenTemplate(label(), cand.Tooltip?.Value),
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }
    }
}
