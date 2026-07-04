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

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "exp:" + vm.GetHashCode() + ":";

            BuildHeader(b, vm, k);
            if (vm.ExplorationScanButtonWrapperVM.ActiveOnScreen.Value) BuildScan(b, vm, k);
            if (vm.ExplorationPointOfInterestListWrapperVM.ActiveOnScreen.Value) BuildPois(b, vm, k);
            if (vm.ExplorationColonyStatsWrapperVM.ActiveOnScreen.Value) BuildColony(b, vm, k);
            if (vm.ExplorationColonyProjectsWrapperVM.ActiveOnScreen.Value) BuildProjects(b, vm, k);
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

        // ---- Colony section (stats / traits / events / projects button / built list) ----

        private static void BuildColony(GraphBuilder b, ExplorationVM vm, string k)
        {
            b.BeginStop("colony").PushContext(Loc.T("exploration.colony"), role: "list");

            var stats = vm.ExplorationColonyStatsWrapperVM.ColonyStatsVM;
            int si = 0;
            if (stats != null)
                foreach (var stat in stats.StatVMs)
                {
                    var s = stat; // capture
                    if (s == null) continue;
                    b.AddItem(ControlId.Referenced(s, k + "stat:" + si++), GraphNodes.Button(
                        () => s.StatName.Value + ": " + s.StatValue.Value
                              + (s.IsNegativelyModified.Value ? ", " + Loc.T("exploration.stat_reduced") : ""),
                        () => { },
                        () => false,
                        tooltip: () => s.Tooltip.Value));
                }

            var traits = vm.ExplorationColonyTraitsWrapperVM.ColonyTraitsVM;
            int ti = 0;
            if (traits != null)
                foreach (var trait in traits.TraitsVMs)
                {
                    var t = trait; // capture
                    if (t == null) continue;
                    b.AddItem(ControlId.Referenced(t, k + "trait:" + ti++), GraphNodes.Button(
                        () => Loc.T("exploration.trait") + ": " + t.Name.Value,
                        () => { },
                        () => false,
                        tooltip: () => t.Tooltip.Value));
                }

            var events = vm.ExplorationColonyEventsWrapperVM.ColonyEventsVM;
            int ei = 0;
            if (events != null)
                foreach (var ev in events.EventsVMs)
                {
                    var e = ev; // capture
                    if (e == null) continue;
                    b.AddItem(ControlId.Referenced(e, k + "event:" + ei++), GraphNodes.Button(
                        () => Loc.T("exploration.event") + ": " + e.Name.Value,
                        () => { if (!Locked(vm)) e.HandleColonyEvent(); }, // starts the event dialog in-system
                        tooltip: () => e.Tooltip?.Value));
                }

            var built = vm.ExplorationColonyProjectsBuiltListWrapperVM.ColonyProjectsBuiltListVM;
            int bi = 0;
            if (built != null)
                foreach (var proj in built.ProjectsVMs)
                {
                    var p = proj; // capture
                    if (p == null) continue;
                    b.AddLabel(ControlId.Referenced(p, k + "built:" + bi++), () => BuiltProjectLabel(p));
                }

            b.AddItem(ControlId.Structural(k + "projects"), GraphNodes.Button(
                () => Loc.T("exploration.open_projects"),
                () => { if (!Locked(vm)) vm.OpenColonyProjects(); }));
            b.PopContext();
        }

        private static string BuiltProjectLabel(Kingmaker.UI.MVVM.VM.Colonization.Projects.ColonyProjectVM p)
        {
            var s = p.Title.Value ?? "";
            if (p.IsBuilding.Value)
                s += ", " + Loc.T("exploration.project_building",
                    new { done = p.Progress.Value, total = p.SegmentsToBuild.Value });
            return s;
        }

        // ---- ColonyProjects section: the rank-tiered picker + the selected project's page ----

        private static void BuildProjects(GraphBuilder b, ExplorationVM vm, string k)
        {
            var pvm = vm.ExplorationColonyProjectsWrapperVM.ColonyProjectsVM;
            if (pvm == null) return;

            b.BeginStop("projlist").PushContext(Loc.T("exploration.projects"), role: "list");
            int pi = 0;
            foreach (var proj in pvm.NavigationVM.NavigationElements)
            {
                var p = proj; // capture
                if (p == null || !p.ShouldShow.Value) continue;
                b.AddItem(ControlId.Referenced(p, k + "proj:" + pi++), GraphNodes.ChoiceOption(
                    () => ProjectCardLabel(p),
                    () => p.IsSelected.Value,
                    () => p.SelectPage()));
            }
            b.PopContext();

            var page = pvm.ColonyProjectPageVM;
            b.BeginStop("projpage").PushContext(Loc.T("exploration.project_page"), role: "list");
            b.AddLabel(ControlId.Structural(k + "pg:title"), () => page.Title.Value ?? "");
            b.AddLabel(ControlId.Structural(k + "pg:desc"), () => page.Description.Value ?? "");
            int i = 0;
            foreach (var req in page.Requirements)
            {
                var r = req; // capture
                if (r == null) continue;
                b.AddLabel(ControlId.Referenced(r, k + "pg:req:" + i++), () =>
                    Loc.T("exploration.requires") + ": " + r.Description.Value
                    + (string.IsNullOrEmpty(r.CountText.Value) ? "" : " " + r.CountText.Value)
                    + ", " + Loc.T(r.IsChecked.Value ? "exploration.req_met" : "exploration.req_unmet"));
            }
            int j = 0;
            foreach (var rew in page.Rewards)
            {
                var r = rew; // capture
                if (r == null) continue;
                b.AddLabel(ControlId.Referenced(r, k + "pg:rew:" + j++), () =>
                    Loc.T("exploration.reward") + ": " + r.Description.Value
                    + (string.IsNullOrEmpty(r.CountText.Value) ? "" : " " + r.CountText.Value));
            }
            b.AddItem(ControlId.Structural(k + "pg:start"), GraphNodes.Button(
                () => Loc.T("exploration.start_project"),
                () => { if (!Locked(vm)) pvm.StartProject(); },
                () => pvm.StartAvailable.Value));
            b.AddItem(ControlId.Structural(k + "pg:blocked"), GraphNodes.Toggle(
                () => Loc.T("exploration.show_blocked"),
                () => pvm.ShowBlockedProjects.Value,
                () => pvm.SwitchBlockedProjects()));
            b.AddItem(ControlId.Structural(k + "pg:finished"), GraphNodes.Toggle(
                () => Loc.T("exploration.show_finished"),
                () => pvm.ShowFinishedProjects.Value,
                () => pvm.SwitchFinishedProjects()));
            b.PopContext();
        }

        private static string ProjectCardLabel(Kingmaker.UI.MVVM.VM.Colonization.Projects.ColonyProjectVM p)
        {
            var parts = new List<string> { p.Title.Value ?? "" };
            if (p.IsFinished.Value) parts.Add(Loc.T("exploration.project_finished"));
            else if (p.IsBuilding.Value)
                parts.Add(Loc.T("exploration.project_building",
                    new { done = p.Progress.Value, total = p.SegmentsToBuild.Value }));
            if (p.IsExcluded.Value) parts.Add(Loc.T("state.disabled"));
            else if (p.IsNotMeetRequirements.Value) parts.Add(Loc.T("exploration.req_unmet"));
            return string.Join(", ", parts);
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
