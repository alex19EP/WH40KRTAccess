using System.Text;
using Kingmaker;                                       // Game (colonization controller, player state)
using Kingmaker.Blueprints;                            // GetComponents<T> (the blueprint component enumerator)
using Kingmaker.Blueprints.Root.Strings;               // UIStrings (every caption on this panel)
using Kingmaker.Code.UI.MVVM.VM.SectorMap;             // SpaceSystemInformationWindowVM + its three item VMs
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;     // TooltipTemplateSystemMapPlanet (the planet card's own)
using Kingmaker.Globalmap.Blueprints;                  // BlueprintPlanet
using Kingmaker.Globalmap.Blueprints.Colonization;     // ResourceData
using Kingmaker.Globalmap.Blueprints.Exploration;      // BlueprintAnomaly.AnomalyObjectType
using Kingmaker.Globalmap.Blueprints.SystemMap;        // BlueprintStarSystemMap
using Kingmaker.Globalmap.Colonization;                // ColoniesState
using Kingmaker.Globalmap.Interaction;                 // BasePointOfInterestComponent
using Kingmaker.UI.Common;                             // UIUtilitySpaceQuests
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The sector map's per-system information panel (<see cref="SpaceSystemInformationWindowVM"/>, a
    /// <c>SectorMapVM</c> child). In mouse mode the game slides it in BY ITSELF the moment a warp jump ends on a
    /// star system (<c>OvertipEntitySystemVM.HandleWarpTravelStopped</c>) — it is the arrival dossier — and it
    /// closes on its close button, on Escape, or when the next jump starts.
    ///
    /// Four Tab stops, each declared only when the panel actually shows it: the system SUMMARY (name, the
    /// unknown-system / no-data plate, colonised, quests, rumours, rumours within range, "enemies in system"),
    /// then PLANETS, OTHER OBJECTS and ANOMALIES — the three widget lists, all of which the game hides
    /// wholesale until the system has been VISITED. That visited gate is the panel's own fog rule and is
    /// mirrored exactly ([[rt-visual-parity]]); so is the per-planet one, where an unexplored planet reads the
    /// game's "not explored" string instead of its name.
    ///
    /// A planet row mirrors its card: the name, a colonised marker, the resource chips (with the mining marker
    /// the card draws), the colony-event chips and the points-of-interest chips — all of which are TEXT on the
    /// card, not icons — with the card's own <c>TooltipTemplateSystemMapPlanet</c> on Space.
    ///
    /// Layer 11, above the service windows (10) that also open on the sector map and below dialogue (15). Not
    /// Exclusive: it is a side panel, not a modal — the map stays live underneath, exactly as it does for a
    /// sighted player.
    /// </summary>
    public sealed class SectorSystemInfoScreen : Screen
    {
        public SectorSystemInfoScreen() { Wrap = true; }

        public override string Key => "sectormap.systeminfo";
        public override string ScreenName => Loc.T("sysinfo.screen");
        public override int Layer => 11;

        internal static SpaceSystemInformationWindowVM Vm()
        {
            var vm = SectorMapScreen.Sector()?.SpaceSystemInformationWindowVM;
            if (vm == null || !vm.ShowSystemWindow.Value) return null;
            return vm.SectorMapObjectEntity?.Value != null ? vm : null;
        }

        public override bool IsActive() => Vm() != null;

        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Raw(CloseLabel()), _ => vm.CloseWindow());
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var entity = vm.SectorMapObjectEntity.Value;
            bool visited = vm.IsVisitedSystem.Value;
            string k = "sysinfo:" + (entity.UniqueId ?? "?") + ":";

            BuildSummary(b, k, vm, visited);
            if (!visited) return; // the game hides all three lists until the system has been visited

            BuildPlanets(b, k, vm);
            BuildObjects(b, k, vm);
            BuildAnomalies(b, k, vm);
        }

        // -- the summary block: everything the panel prints above the lists --
        private static void BuildSummary(GraphBuilder b, string k, SpaceSystemInformationWindowVM vm, bool visited)
        {
            var entity = vm.SectorMapObjectEntity.Value;
            var view = entity.View;
            b.BeginStop("system").PushContext(Loc.T("sysinfo.screen"), Loc.T("role.list"));

            string name = entity.Name ?? "";
            b.AddLabel(ControlId.Structural(k + "name"), () => name);

            if (!visited)
            {
                // The panel replaces its body with the "unknown system" plate and a NO DATA line.
                b.AddLabel(ControlId.Structural(k + "unknown"),
                    () => GameText.Or(() => UIStrings.Instance.GlobalMap.UnknownSystem, "sysinfo.unknown"));
                b.AddLabel(ControlId.Structural(k + "nodata"),
                    () => GameText.Or(() => UIStrings.Instance.QuesJournalTexts.NoData, "sysinfo.no_data"));
            }
            else if (SafeColonized(view))
            {
                b.AddLabel(ControlId.Structural(k + "colonized"),
                    () => GameText.Or(() => UIStrings.Instance.GlobalMap.SystemColonized, "sysinfo.colonized"));
            }

            // Rumours whose range covers this system — printed above the quest block, visited or not.
            string inRange = RumoursInRangeLine(view);
            if (inRange != null) b.AddLabel(ControlId.Structural(k + "inrange"), () => inRange);

            AddLines(b, k + "quest:", "sysinfo.quests", QuestLines(view, SafeArea(vm)));
            AddLines(b, k + "rumour:", "sysinfo.rumours", RumourLines(view));

            if (visited && SafeHasEnemies(vm))
                b.AddLabel(ControlId.Structural(k + "enemies"),
                    () => GameText.Or(() => UIStrings.Instance.GlobalMap.HasEnemiesInSystem, "sysinfo.enemies"));

            b.PopContext();
        }

        // A captioned block of lines (the panel's "- [ Quests ] -" heading plus its multi-line body), one row
        // per entry so each reads on its own.
        private static void AddLines(GraphBuilder b, string k, string captionKey, List<string> lines)
        {
            if (lines == null || lines.Count == 0) return;
            b.AddLabel(ControlId.Structural(k + "hdr"), () => Loc.T(captionKey));
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i]; // capture
                b.AddLabel(ControlId.Structural(k + i), () => l);
            }
        }

        // -- planets: the richest card on the panel --
        private static void BuildPlanets(GraphBuilder b, string k, SpaceSystemInformationWindowVM vm)
        {
            var planets = vm.Planets;
            if (planets == null || planets.Count == 0) return;
            b.BeginStop("planets").PushContext(
                GameText.Or(() => UIStrings.Instance.GlobalMap.Planets, "sysinfo.planets"), Loc.T("role.list"));
            for (int i = 0; i < planets.Count; i++)
            {
                var p = planets[i];
                if (p == null) continue;
                string label = PlanetLabel(p); // computed once per render, not once per announcement
                var vt = GraphNodes.Text(() => label);
                var planet = p; // capture
                vt.OnTooltip = () => TooltipChooser.OpenTemplate(label,
                    new TooltipTemplateSystemMapPlanet(null, null, planet.BlueprintPlanet,
                        planet.BlueprintStarSystemMap));
                b.AddItem(ControlId.Referenced(p, k + "planet:" + i), vt);
            }
            b.PopContext();
        }

        // -- other objects (asteroid fields and the like): name-only rows, under the game's own caption --
        private static void BuildObjects(GraphBuilder b, string k, SpaceSystemInformationWindowVM vm)
        {
            var objects = vm.OtherObjects;
            if (objects == null || objects.Count == 0) return;
            b.BeginStop("objects").PushContext(
                GameText.Or(() => UIStrings.Instance.GlobalMap.AsteroidsFieldDetected, "sysinfo.objects"),
                Loc.T("role.list"));
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                if (o == null) continue;
                string text = o.Name ?? "";
                b.AddItem(ControlId.Referenced(o, k + "obj:" + i), GraphNodes.Text(() => text));
            }
            b.PopContext();
        }

        // -- additional anomalies: the already-interacted ones the panel still lists --
        private static void BuildAnomalies(GraphBuilder b, string k, SpaceSystemInformationWindowVM vm)
        {
            var anomalies = vm.AdditionalAnomalies;
            if (anomalies == null || anomalies.Count == 0) return;
            b.BeginStop("anomalies").PushContext(Loc.T("sysinfo.anomalies"), Loc.T("role.list"));
            for (int i = 0; i < anomalies.Count; i++)
            {
                var a = anomalies[i];
                if (a == null) continue;
                string text = a.Name ?? "";
                b.AddItem(ControlId.Referenced(a, k + "anom:" + i), GraphNodes.Text(() => text));
            }
            b.PopContext();
        }

        // "Janus, colonised, Adamantine, Prometium (mining), Void Storm, Cargo cache" — the card's own chips,
        // in the order it draws them. Unexplored planets are not named at all (the game's own gate).
        private static string PlanetLabel(PlanetInfoSpaceSystemInformationWindowVM p)
        {
            try
            {
                var sb = new StringBuilder(p.IsExplored
                    ? (p.Name ?? "")
                    : GameText.Or(() => UIStrings.Instance.ExplorationTexts.ExploNotExplored, "sysinfo.not_explored"));
                if (p.IsColonized) sb.Append(", ").Append(Loc.T("sysinfo.colonised_planet"));
                if (!p.IsExplored) return sb.ToString(); // the card draws no chips for an unexplored planet

                AppendResources(sb, p.BlueprintPlanet);
                AppendColonyEvents(sb, p.BlueprintPlanet, p.BlueprintStarSystemMap);
                AppendPointsOfInterest(sb, p.BlueprintPlanet, p.BlueprintStarSystemMap);
                return sb.ToString();
            }
            catch (Exception e) { Main.Log?.Error("SectorSystemInfoScreen.PlanetLabel: " + e); return p.Name ?? ""; }
        }

        // The resource chips: name, plus the card's miner marker when a miner is already working it here.
        private static void AppendResources(StringBuilder sb, BlueprintPlanet planet)
        {
            var resources = planet?.Resources;
            if (resources == null) return;
            var miners = Game.Instance?.Player?.ColoniesState?.Miners;
            foreach (ResourceData data in resources)
            {
                if (data == null || data.Count <= 0) continue;
                var bp = data.Resource?.Get();
                if (bp == null) continue;
                bool mined = false;
                if (miners != null)
                    foreach (var m in miners)
                        if (m.Sso == planet && m.Resource == bp) { mined = true; break; }
                sb.Append(", ").Append(bp.Name);
                if (mined) sb.Append(' ').Append(Loc.T("sysinfo.mining"));
            }
        }

        // The colony "traits" chips — the started events of a colony on this planet.
        private static void AppendColonyEvents(StringBuilder sb, BlueprintPlanet planet, BlueprintStarSystemMap area)
        {
            var colonies = Game.Instance?.Player?.ColoniesState?.Colonies;
            if (colonies == null) return;
            foreach (ColoniesState.ColonyData data in colonies)
            {
                if (data == null || data.Area != area || data.Planet != planet) continue;
                var events = data.Colony?.StartedEvents;
                if (events == null) return;
                foreach (var e in events)
                    if (e != null) sb.Append(", ").Append(e.Name);
                return;
            }
        }

        // The points-of-interest chips: still-visible, not-yet-interacted points, named as the card names them.
        private static void AppendPointsOfInterest(StringBuilder sb, BlueprintPlanet planet, BlueprintStarSystemMap area)
        {
            if (planet == null) return;
            List<Kingmaker.Globalmap.Blueprints.Exploration.BlueprintPointOfInterest> done = null;
            var interacted = Game.Instance?.Player?.StarSystemsState?.InteractedPoints;
            if (interacted != null && area != null) interacted.TryGetValue(area, out done);
            foreach (BasePointOfInterestComponent c in planet.GetComponents<BasePointOfInterestComponent>())
            {
                var bp = c?.PointBlueprint;
                if (bp == null || !bp.IsVisible()) continue;
                if (done != null && done.Contains(bp)) continue;
                string name = bp.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    try { name = UIStrings.Instance.ExplorationTexts.GetPointOfInterestTypeName(bp); } catch { }
                }
                if (!string.IsNullOrWhiteSpace(name)) sb.Append(", ").Append(name);
            }
        }

        // -- the summary's own data, each guarded: these all reach deep into live game state --

        private static BlueprintStarSystemMap SafeArea(SpaceSystemInformationWindowVM vm)
        {
            try { return vm.Area; }
            catch { return null; }
        }

        private static bool SafeColonized(Kingmaker.Globalmap.SectorMap.SectorMapObject view)
        {
            try { return Game.Instance?.ColonizationController?.GetColony(view) != null; }
            catch { return false; }
        }

        private static bool SafeHasEnemies(SpaceSystemInformationWindowVM vm)
        {
            try
            {
                var list = vm.GetActiveAnomalies(false, BlueprintAnomaly.AnomalyObjectType.Enemy);
                return list != null && list.Count > 0;
            }
            catch { return false; }
        }

        private static List<string> QuestLines(Kingmaker.Globalmap.SectorMap.SectorMapObject view,
            BlueprintStarSystemMap area)
        {
            try
            {
                var here = UIUtilitySpaceQuests.GetQuestsForSystem(view);
                var inSystem = area != null ? UIUtilitySpaceQuests.GetQuestsForSpaceSystem(area) : null;
                bool any = (here != null && here.Count > 0) || (inSystem != null && inSystem.Count > 0);
                return any ? UIUtilitySpaceQuests.GetQuestsStringList(here, inSystem) : null;
            }
            catch (Exception e) { Main.Log?.Log("sysinfo quests failed: " + e.Message); return null; }
        }

        private static List<string> RumourLines(Kingmaker.Globalmap.SectorMap.SectorMapObject view)
        {
            try
            {
                var rumours = UIUtilitySpaceQuests.GetRumoursForSystem(view);
                if (rumours == null || rumours.Count == 0) return null;
                var lines = new List<string>();
                foreach (var r in rumours)
                {
                    string title = r?.Blueprint?.GetTitile()?.Text;
                    if (!string.IsNullOrWhiteSpace(title)) lines.Add((lines.Count + 1) + ". " + title);
                }
                return lines.Count > 0 ? lines : null;
            }
            catch (Exception e) { Main.Log?.Log("sysinfo rumours failed: " + e.Message); return null; }
        }

        // "Within rumour range: A, B" — the panel's own one-liner, the game's caption included.
        private static string RumoursInRangeLine(Kingmaker.Globalmap.SectorMap.SectorMapObject view)
        {
            try
            {
                var rumours = UIUtilitySpaceQuests.GetRumoursForSectorMap(view);
                if (rumours == null || rumours.Count == 0) return null;
                var sb = new StringBuilder();
                foreach (var r in rumours)
                {
                    string title = r?.Blueprint?.GetTitile()?.Text;
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(title);
                }
                if (sb.Length == 0) return null;
                return GameText.Or(() => UIStrings.Instance.GlobalMap.WithinRumourRange, "sysinfo.rumour_range")
                       + ": " + sb;
            }
            catch (Exception e) { Main.Log?.Log("sysinfo rumour range failed: " + e.Message); return null; }
        }

        private static string CloseLabel()
            => GameText.Or(() => UIStrings.Instance.CommonTexts.CloseWindow, "action.close");
    }
}
