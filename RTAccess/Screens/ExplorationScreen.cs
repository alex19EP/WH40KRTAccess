using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;                  // UIStrings (the game's own labels)
using Kingmaker.Code.UI.MVVM.VM.Exploration;              // ExplorationVM, ExplorationPointOfInterestVM
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;        // TooltipTemplateSimple
using Kingmaker.GameCommands;                             // CloseExplorationScreen
using Kingmaker.Globalmap.Blueprints.SystemMap;           // BlueprintPlanet
using Kingmaker.PubSubSystem;                             // IColonizationProjectsUIHandler
using Kingmaker.PubSubSystem.Core;                        // EventBus
using Kingmaker.UI.MVVM.VM.Exploration;                   // ExplorationResourceVM, wrappers
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The planet-exploration "tablet" (the fullscreen window that auto-opens when the ship lands on a system
    /// object; <c>SpaceStaticPartVM.ExplorationVM.IsExploring</c>) — M2 of docs/plans/orbital-listing-wilkes.md.
    /// Mirrors the game's own section machine by reading each wrapper VM's <c>ActiveOnScreen</c> (set from
    /// <c>ExplorationVM.UpdateComponentsVisibility</c>), so our sections appear exactly when the game's do:
    /// <b>NotScanned</b> → header + the Begin-scan button (the wrapper's own Interact, the two-phase scan whose
    /// completion SpaceEvents summarizes); <b>Exploration</b> → the POI ring as a list (Enter = the VM's
    /// Interact — the game itself refuses not-interactable/explored ones with warning toasts WarningReader
    /// already voices) + the resource points (Enter = install/remove miner through the game's confirm boxes);
    /// <b>Colony</b> → stats/traits/events/built projects + the projects button; <b>ColonyProjects</b> → the
    /// rank-tiered project picker (select a card, read its page, Start).
    ///
    /// The header block (name / world type / Tithe Grade / Aestimare) is VIEW-owned in the game — read straight
    /// from the blueprint with the same unscanned gating ("???" / "undetermined"). Book-event POIs raise
    /// dialogs (DialogueScreen), loot POIs the loot window (LootScreen), stat-check POIs the StatCheckLoot
    /// modal, expeditions their slider modal, ground ops the party picker (GroupChangerScreen) — all layered
    /// above this screen. Escape closes through the game's own CloseExplorationScreen command (which also
    /// raises the cancel-scan confirm mid-animation — MessageBoxScreen covers it).
    /// </summary>
    public sealed class ExplorationScreen : Screen
    {
        public override string Key => "exploration";
        public override int Layer => 9;              // above ctx.systemmap (0); below service windows (10),
        public override bool Exclusive => true;      // dialogue (15) and loot (24) that open on top of it

        public override string ScreenName
        {
            get
            {
                var vm = Vm();
                if (vm == null) return Loc.T("exploration.screen");
                return ObjectName(vm) + ", " + Loc.T("exploration.screen");
            }
        }

        public override bool IsActive() => Vm()?.IsExploring.Value == true;

        private static ExplorationVM Vm() => Game.Instance?.RootUiContext?.SpaceVM?.StaticPartVM?.ExplorationVM;

        // The game greys the tablet behind a dialog overlay while a POI dialog runs — gate our verbs the same.
        private static bool Locked(ExplorationVM vm) => vm.IsLockUIForDialog.Value;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "exp:" + vm.GetHashCode() + ":";

            BuildHeader(b, vm, k);
            if (vm.ExplorationScanButtonWrapperVM.ActiveOnScreen.Value) BuildScan(b, vm, k);
            if (vm.ExplorationPointOfInterestListWrapperVM.ActiveOnScreen.Value) BuildPois(b, vm, k);
            if (vm.ExplorationColonyRewardsWrapperVM.ActiveOnScreen.Value)
                ColonyNodes.BuildRewards(b, k, vm.ExplorationColonyRewardsWrapperVM.ColonyRewardsVM);
            if (vm.ExplorationColonyStatsWrapperVM.ActiveOnScreen.Value)
                ColonyNodes.BuildComponents(b, k,
                    vm.ExplorationColonyStatsWrapperVM.ColonyStatsVM,
                    vm.ExplorationColonyTraitsWrapperVM.ColonyTraitsVM,
                    vm.ExplorationColonyEventsWrapperVM.ColonyEventsVM,
                    vm.ExplorationColonyProjectsBuiltListWrapperVM.ColonyProjectsBuiltListVM,
                    vm.OpenColonyProjects,
                    () => Locked(vm));
            if (vm.ExplorationColonyProjectsWrapperVM.ActiveOnScreen.Value)
                ColonyNodes.BuildProjects(b, k, vm.ExplorationColonyProjectsWrapperVM.ColonyProjectsVM,
                    () => Locked(vm));
        }

        // ---- header (VIEW-owned in the game: read the blueprint with the view's own gating) ----

        private static void BuildHeader(GraphBuilder b, ExplorationVM vm, string k)
        {
            b.BeginStop("header").PushContext(ObjectName(vm), role: "list");
            b.AddLabel(ControlId.Structural(k + "hdr:name"), () => ObjectName(vm));
            var planet = vm.PlanetView != null ? vm.PlanetView.Data?.Blueprint as BlueprintPlanet : null;
            if (planet != null && !planet.DontShowWorldType)
            {
                bool Scanned() => vm.StarSystemObjectView?.Data is Kingmaker.Globalmap.SystemMap.StarSystemObjectEntity e
                                  && (e.IsScanned || e.IsScannedOnStart);
                string Undetermined() => GameText.Or(() => UIStrings.Instance.ExplorationTexts.TitheGradeUndetermined,
                    "exploration.undetermined");
                b.AddLabel(ControlId.Structural(k + "hdr:type"), () =>
                    Scanned() ? planet.WorldType.Text : Undetermined());
                b.AddLabel(ControlId.Structural(k + "hdr:tithe"), () =>
                    GameText.Or(() => UIStrings.Instance.ExplorationTexts.TitheGrade, "exploration.tithe")
                    + ": " + (Scanned() ? planet.TitheGrade.Text : Undetermined()));
                b.AddLabel(ControlId.Structural(k + "hdr:aestimare"), () =>
                    GameText.Or(() => UIStrings.Instance.ExplorationTexts.Aestimare, "exploration.aestimare")
                    + ": " + (Scanned() ? planet.Aestimare.Text : Undetermined()));
            }
            // The miner counter the tablet shows while the planet has no colony (hidden with one).
            if (!vm.HasColony.Value)
                b.AddLabel(ControlId.Structural(k + "hdr:miners"), () =>
                    GameText.Or(() => UIStrings.Instance.ExplorationTexts.ResourceMiner, "exploration.miners")
                    + " x" + (vm.ResourceMinersVM?.Count.Value ?? 0));
            b.PopContext();
        }

        private static string ObjectName(ExplorationVM vm)
        {
            var data = vm.StarSystemObjectView?.Data;
            if (data == null) return Loc.T("exploration.screen");
            bool scanned = data.IsScanned || data.IsScannedOnStart;
            string name = data.Blueprint?.Name;
            return scanned && !string.IsNullOrWhiteSpace(name) ? name : "???"; // the header's own unscanned label
        }

        // ---- NotScanned: the Begin-scan button ----

        private static void BuildScan(GraphBuilder b, ExplorationVM vm, string k)
        {
            b.BeginStop("scan");
            b.AddItem(ControlId.Structural(k + "scan"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.ExplorationTexts.ExploBeginScan, "exploration.scan"),
                () => { if (!Locked(vm)) vm.ExplorationScanButtonWrapperVM.Interact(); }));
        }

        // ---- Exploration/Colony: the POI ring + resource points ----

        private static void BuildPois(GraphBuilder b, ExplorationVM vm, string k)
        {
            var list = vm.ExplorationPointOfInterestListVM;
            if (list == null) return;

            b.BeginStop("pois").PushContext(
                GameText.Or(() => UIStrings.Instance.ExplorationTexts.ExploPointsOfInterest, "exploration.pois"),
                role: "list");
            bool any = false;
            int i = 0;
            foreach (var poi in list.PointsOfInterestVMs)
            {
                var p = poi; // capture
                if (p == null || !p.IsVisible.Value) continue;
                b.AddItem(ControlId.Referenced(p, k + "poi:" + i), GraphNodes.Button(
                    () => PoiLabel(p),
                    () => { if (!Locked(vm)) p.Interact(); })); // the VM refuses audibly when not interactable
                any = true;
                i++;
            }
            if (!any)
                b.AddLabel(ControlId.Structural(k + "poi:none"), () => Loc.T("exploration.no_pois"));
            b.PopContext();

            b.BeginStop("resources").PushContext(
                GameText.Or(() => UIStrings.Instance.ExplorationTexts.ExploObjectResources, "exploration.resources"),
                role: "list");
            bool anyRes = false;
            int j = 0;
            foreach (var res in list.ResourcesVMs)
            {
                var r = res; // capture
                if (r == null) continue;
                b.AddItem(ControlId.Referenced(r, k + "res:" + j), GraphNodes.Button(
                    () => ResourceLabel(r),
                    () => { if (!Locked(vm)) r.Interact(); }, // install/remove miner via the game's confirm boxes
                    tooltip: () => new TooltipTemplateSimple(r.Name.Value, r.Description.Value)));
                anyRes = true;
                j++;
            }
            if (!anyRes)
                b.AddLabel(ControlId.Structural(k + "res:none"), () =>
                    GameText.Or(() => UIStrings.Instance.ExplorationTexts.ExploObjectResourcesEmpty,
                        "exploration.no_resources"));
            b.PopContext();
        }

        // "<name>[, quest][, rumour], <explored | not interactable | not explored>" — the icon-button's own
        // hint states, folded into the browse label since the ring encodes them visually (alpha/overlay).
        private static string PoiLabel(ExplorationPointOfInterestVM p)
        {
            var parts = new List<string> { string.IsNullOrWhiteSpace(p.Name.Value) ? "???" : p.Name.Value };
            if (p.IsRumour.Value) parts.Add(Loc.T("systemmap.has_rumour"));
            else if (p.IsQuest.Value) parts.Add(Loc.T("systemmap.has_quest"));
            if (p.IsExplored.Value)
                parts.Add(GameText.Or(() => UIStrings.Instance.ExplorationTexts.ExploAlreadyExplored, "exploration.explored"));
            else if (!p.IsInteractable.Value)
                parts.Add(GameText.Or(() => UIStrings.Instance.ExplorationTexts.ExploNotInteractable, "exploration.unavailable"));
            return string.Join(", ", parts);
        }

        private static string ResourceLabel(ExplorationResourceVM r)
        {
            var s = (string.IsNullOrWhiteSpace(r.Name.Value) ? "???" : r.Name.Value) + ", " + r.Count.Value;
            if (r.IsBeingMined.Value) s += ", " + Loc.T("systemmap.has_extractor");
            return s;
        }

        // ---- input ----

        // Escape: leave the projects sub-window first (the game's own close handler), else close the tablet
        // through its command (which also owns the mid-scan cancel confirm).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm == null) yield break;
            yield return new ElementAction(ActionIds.Back, Message.Raw(GameText.Action("close")), _ =>
            {
                if (vm.ExplorationColonyProjectsWrapperVM.ActiveOnScreen.Value)
                    EventBus.RaiseEvent<IColonizationProjectsUIHandler>(h => h.HandleColonyProjectsUIClose());
                else
                    Game.Instance?.GameCommandQueue?.CloseExplorationScreen();
            });
        }
    }
}
