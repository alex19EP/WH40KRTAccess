using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings;                    // UIStrings (the window's own title + toggle labels)
using Kingmaker.UI.MVVM.VM.ServiceWindows.Inventory;        // CharacterVisualSettingsVM (+EntityVM) — note: NOT Code.UI
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The doll's character-visual-settings window (<see cref="CharacterVisualSettingsVM"/>) as a
    /// graph-native screen — the cosmetics panel the doll's own button opens (helmet / backpack / gloves /
    /// boots / armor visibility plus the outfit color swatches). Raised from the InventoryScreen's
    /// "Show visual settings" opener via the doll's own <c>ShowVisualSettings()</c>; each toggle drives the
    /// entity VM's own <c>Switch()</c> (which flips the unit's UISettings and re-dresses the doll), reading
    /// its live <c>IsOn</c>/<c>Locked</c> reactives — the lock mirrors the game greying Backpack for
    /// mechadendrite units. The Cloth entity exists only on the CharGen path (the unit ctor leaves it null)
    /// and pets get no toggles at all (the game's view binds none for <c>IsPet</c>) — both mirrored by
    /// construction. The outfit color selector is a combo box over the game's swatch collection ("color N" —
    /// the swatches are unnamed ramp textures), selecting through the game's own SelectionGroup (whose
    /// item's OnSelect queues SetEquipmentColor); an empty collection reads the game's own no-items
    /// explanation (enable clothes / disabled for this character). Labels and title are all the game's
    /// UIStrings — nothing to re-localize.
    ///
    /// Exclusive, layer 13 — above the InventoryScreen (10) it's raised from and clear of the
    /// EquipSelectorScreen (12), so it owns input while open. Escape closes through the VM's own Close()
    /// (→ the doll's HideVisualSettings). Carries its own ScreenName (the game's window title).
    /// </summary>
    public sealed class VisualSettingsScreen : Screen
    {
        public override string Key => "inventory.visualsettings";
        public override int Layer => 13;
        public override bool Exclusive => true;
        public override string ScreenName
            => Vm() != null ? UIStrings.Instance.CharacterSheet.VisualSettingsTitle.Text : null;

        public override bool IsActive() => Vm() != null;

        // Back (Escape) closes through the VM's own callback (the game's close button path).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.Close());
        }

        // The settings live on the currently-viewed doll of whichever inventory window is live (surface or
        // space), mirroring InventoryScreen.Vm()'s resolution; the doll disposes them on a character switch.
        private static CharacterVisualSettingsVM Vm()
            => UiContexts.Inventory()?.DollVM?.VisualSettingsVM?.Value;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "visual:" + vm.GetHashCode() + ":"; // a new window = fresh keys

            b.PushContext(UIStrings.Instance.CharacterSheet.VisualSettingsTitle.Text, Loc.T("role.list"));

            if (!vm.IsPet) // the game's view binds no toggles for pets
            {
                var cs = UIStrings.Instance.CharacterSheet;
                AddToggle(b, k, "cloth", cs.VisualSettingsShowCloth.Text, vm.Cloth); // CharGen-only (null here)
                AddToggle(b, k, "helmet", cs.VisualSettingsShowHelmet.Text, vm.Helmet);
                AddToggle(b, k, "backpack", cs.VisualSettingsShowBackpack.Text, vm.Backpack);
                AddToggle(b, k, "helmetaboveall", cs.VisualSettingsShowHelmetAboveAll.Text, vm.HelmetAboveAll);
                AddToggle(b, k, "gloves", cs.VisualSettingsShowGloves.Text, vm.Gloves);
                AddToggle(b, k, "boots", cs.VisualSettingsShowBoots.Text, vm.Boots);
                AddToggle(b, k, "armor", cs.VisualSettingsShowArmor.Text, vm.Armor);
            }

            BuildColorSelector(b, k, vm);
            b.PopContext();
        }

        private static void AddToggle(GraphBuilder b, string k, string key, string label,
            CharacterVisualSettingsEntityVM e)
        {
            if (e == null) return;
            b.AddItem(ControlId.Structural(k + key), GraphNodes.Toggle(
                () => label, () => e.IsOn.Value, e.Switch, enabled: () => !e.Locked.Value));
        }

        // The outfit color swatches: a combo box over the game's own selection group. The swatches are
        // unnamed ramp textures, so options read "color N"; selection rides the game's TrySelectEntity
        // (the item's OnSelect queues the SetEquipmentColor command). Empty → the game's own explanation.
        private static void BuildColorSelector(GraphBuilder b, string k, CharacterVisualSettingsVM vm)
        {
            var sel = vm.OutfitMainColorSelector;
            var col = sel?.SelectionGroup?.EntitiesCollection;
            if (col == null || col.Count == 0)
            {
                var why = sel?.NoItemsDesc?.Value;
                if (!string.IsNullOrEmpty(why))
                    b.AddItem(ControlId.Structural(k + "color:none"), GraphNodes.Text(() => why));
                return;
            }
            b.AddItem(ControlId.Structural(k + "color"), GraphNodes.Cycler(
                () => sel.Title?.Value ?? "",
                () =>
                {
                    var names = new List<string>(col.Count);
                    foreach (var e in col)
                        names.Add(Loc.T("visual.color_option", new { number = e.Number + 1 }));
                    return names;
                },
                () => System.Math.Max(0, col.IndexOf(sel.SelectionGroup.SelectedEntity?.Value)),
                i => { if (i >= 0 && i < col.Count) sel.SelectionGroup.TrySelectEntity(col[i]); }));
        }
    }
}
