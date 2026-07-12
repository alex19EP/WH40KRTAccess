using System;
using System.Text;
using Kingmaker.Blueprints.Root.Strings;                                    // UIStrings (DialogOk / Attune / cargo origin labels)
using Kingmaker.Code.UI.MVVM.VM.ExitBattlePopup;                            // ExitBattlePopupVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CargoManagement.Components;  // CargoRewardSlotVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The space-combat victory popup (Phase 5 of docs/plans/inertial-broadsiding-tsiolkovsky.md) — the
    /// game's <see cref="ExitBattlePopupVM"/> (a child of the SpaceCombat HUD component, flipped active by
    /// <c>IEndSpaceCombatHandler</c> when the battle is won). One list: the ship-experience line (gained XP,
    /// level, "can advance" when a level is banked), the scrap reward, each item reward (name + badges,
    /// card tooltip on Space), each cargo reward (the card shows origin icon + fill % + count — mirrored as
    /// the browse label; detail on Space), then the popup's own two actions with the game's own button
    /// strings: OK (<c>ExitBattle(false)</c> → back to the star system) and Attune
    /// (<c>ExitBattle(true)</c> → straight into the voidship upgrade window, which
    /// <see cref="ShipCustomizationScreen"/> already covers) — the sighted view shows the second button
    /// only while <c>IsUpgradeAvailable</c>. Rewards are already granted by the game when this shows;
    /// the rows are display-only.
    /// </summary>
    public sealed class ExitBattlePopupScreen : Screen
    {
        public ExitBattlePopupScreen() { Wrap = true; } // single modal list — Tab wraps, like MessageBox

        public override string Key => "overlay.exitbattle";
        public override string ScreenName => Loc.T("spacecombat.victory");
        // Blocking end-of-battle modal: above the space-combat base context and the Esc menu (20), below
        // save/load (22) / loot (24) — nothing else stacks with it in practice.
        public override int Layer => 21;
        public override bool Exclusive => true;

        private static ExitBattlePopupVM Vm()
        {
            var vm = SpaceCombatScreen.Component()?.ExitBattlePopupVM;
            return vm != null && vm.IsActive.Value ? vm : null;
        }

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            const string k = "exitbattle:";
            b.BeginStop("victory").PushContext(Loc.T("spacecombat.victory"), Loc.T("role.list"));

            b.AddItem(ControlId.Structural(k + "xp"), GraphNodes.Text(() => XpLine(Vm())));

            if ((vm.ScrapVM?.Amount?.Value ?? 0) > 0)
            {
                var scrapVm = vm.ScrapVM;
                var svt = GraphNodes.Text(() => Loc.T("spacecombat.scrap_reward", new { n = scrapVm.Amount.Value }));
                svt.OnTooltip = () => TooltipChooser.OpenTemplate(
                    Loc.T("spacecombat.scrap_reward", new { n = scrapVm.Amount.Value }), scrapVm.Tooltip?.Value);
                b.AddItem(ControlId.Structural(k + "scrap"), svt);
            }

            var items = vm.ItemsSlotsGroup?.VisibleCollection;
            if (items != null)
                foreach (var slot in items)
                {
                    if (slot == null || !slot.HasItem) continue;
                    var ent = slot.Item.Value;
                    var s = slot; // loop-local for the closures
                    var ivt = GraphNodes.Text(() => ItemNodes.ItemLabel(s));
                    ivt.OnTooltip = () => ItemNodes.OpenItemTooltip(s);
                    b.AddItem(ControlId.Referenced(ent, k + "item:" + (ent?.UniqueId ?? "slot")), ivt);
                }

            var cargos = vm.CargoRewards;
            if (cargos != null)
                for (int i = 0; i < cargos.Count; i++)
                {
                    var c = cargos[i];
                    if (c == null) continue;
                    var cvt = GraphNodes.Text(() => CargoLabel(c));
                    cvt.OnTooltip = () => TooltipChooser.OpenTemplate(CargoLabel(c), c.Tooltip?.Value);
                    b.AddItem(ControlId.Structural(k + "cargo:" + i), cvt);
                }

            // The popup's own two buttons, with the game's own labels: OK = leave to the star system,
            // Attune = leave straight into the voidship upgrade window (shown only with a banked level).
            b.AddItem(ControlId.Structural(k + "continue"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.SettingsUI.DialogOk, "spacecombat.exit_battle"),
                () => Vm()?.ExitBattle(false)));
            if (vm.IsUpgradeAvailable.Value)
                b.AddItem(ControlId.Structural(k + "upgrade"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.ShipCustomization.Attune, "spacecombat.upgrade_ship"),
                    () => Vm()?.ExitBattle(true)));
            b.PopContext();
        }

        // "Gained 120 ship experience, level 2, can advance to level 3, experience 350 of 500."
        private static string XpLine(ExitBattlePopupVM vm)
        {
            try
            {
                if (vm == null) return "";
                var sb = new StringBuilder(Loc.T("spacecombat.xp_gained", new { n = vm.GainedExpAmount.Value }));
                sb.Append(", ").Append(Loc.T("spacecombat.xp_level", new { level = vm.CurrentLevel.Value }));
                if (vm.LevelDiff.Value > 0)
                    sb.Append(", ").Append(Loc.T("spacecombat.xp_advance", new { level = vm.ExpLevel.Value }));
                if (vm.NextLevelExp.Value > 0)
                    sb.Append(", ").Append(Loc.T("spacecombat.xp_progress",
                        new { cur = vm.CurrentExp.Value, next = vm.NextLevelExp.Value }));
                return sb.ToString();
            }
            catch (Exception e) { Main.Log?.Error("ExitBattlePopupScreen.XpLine: " + e); return ""; }
        }

        // The cargo card shows an origin-type icon, a fill % and a count — mirror exactly that (the game's
        // own origin label passed through), detail on Space via the card's tooltip template.
        private static string CargoLabel(CargoRewardSlotVM c)
        {
            try
            {
                string type = null;
                try { type = UIStrings.Instance.CargoTexts.GetLabelByOrigin(c.Origin); } catch { }
                var sb = new StringBuilder(Loc.T("spacecombat.cargo_reward", new
                {
                    type = string.IsNullOrEmpty(type) ? Loc.T("spacecombat.cargo") : type,
                    fill = c.TotalFillValue.Value,
                }));
                if (c.Count.Value > 1) sb.Append(" (").Append(Loc.T("item.count", new { count = c.Count.Value })).Append(')');
                return sb.ToString();
            }
            catch (Exception e) { Main.Log?.Error("ExitBattlePopupScreen.CargoLabel: " + e); return ""; }
        }
    }
}
