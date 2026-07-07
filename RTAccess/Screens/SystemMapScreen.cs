using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;                      // UIStrings (game-localized labels)
using Kingmaker.Code.UI.MVVM.VM.Overtips.SystemMap;           // OvertipEntityPlanetVM / AnomalyVM / SystemObjectVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;               // ServiceWindowsType
using Kingmaker.Code.UI.MVVM.VM.Space;                        // SpaceStaticPartVM, ZoneExitVM
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;            // TooltipTemplateSystemMapPlanet
using Kingmaker.GameCommands;                                 // GameCommandQueue extensions (MoveShip, Stop…)
using Kingmaker.GameModes;                                    // GameModeType
using Kingmaker.Globalmap.Blueprints;                         // BlueprintStarSystemMap (the area type)
using Kingmaker.Globalmap.Blueprints.Exploration;             // BlueprintAnomaly
using Kingmaker.Globalmap.Exploration;                        // AnomalyEntityData
using Kingmaker.Globalmap.SystemMap;                          // StarSystemObjectEntity + subtypes
using RTAccess.UI;
using RTAccess.UI.Graph;
using UnityEngine;

namespace RTAccess.Screens
{
    /// <summary>
    /// The in-system space map (<c>GameModeType.StarSystem</c> — the ship flying among planets/anomalies) as a
    /// navigable base context: the space sibling of <see cref="InGameScreen"/>. Design + M0 harness findings in
    /// docs/plans/orbital-listing-wilkes.md: the whole system is visible to a sighted player at once (no fog),
    /// so the Objects list carries every <c>IsInGame</c> object, gated only the way the overtips gate.
    ///
    /// Graph-native, three Tab stops:
    /// <b>Objects</b> — a flat list, order FROZEN nearest-first at area entry (no reshuffling under the cursor
    /// as the ship flies; the WA lesson), labels live and mirroring the overtip card (name gated by scan state,
    /// type word, the card's state icons as flag words, bearing + distance from the ship). Enter flies there —
    /// the overtip VM's own <c>RequestVisit()</c> (the card's click). Space reads the planet hover tooltip.
    /// <b>Status</b> — system name + research %, Profit Factor, resource pool, ship state.
    /// <b>Actions</b> — warp jump / stop ship / return to bridge (the <see cref="ZoneExitVM"/> cluster) + the
    /// service-window openers the game offers in this mode.
    ///
    /// UNFOCUSED by default (like the surface context): the arrows stay with the game's map camera until Tab
    /// enters the list; Escape while unfocused stays the game's (stop-ship while moving, else pause menu).
    /// Verbs act only while the map is the CURRENT mode (<see cref="Interactive"/>) — dialogs/cutscenes over
    /// the map keep the context alive but must not fly the ship (the WA mode-stack lesson).
    /// </summary>
    public sealed class SystemMapScreen : Screen
    {
        public override string Key => "ctx.systemmap";
        public override string ScreenName => Loc.T("systemmap.screen");
        public override int Layer => 0;                     // base context, sibling of ctx.ingame / mainmenu
        public override bool StartUnfocused => true;        // camera keeps the arrows; Tab enters the list

        public override bool IsActive()
            => Game.Instance?.CurrentlyLoadedArea is BlueprintStarSystemMap
               && Game.Instance.RootUiContext?.SpaceVM != null;

        /// <summary>Map verbs may act: this screen is the live top screen and the map is the CURRENT mode —
        /// not a dialog/cutscene/book event layered over it (acting there desyncs the game; WA lesson).</summary>
        public static bool Interactive =>
            ScreenManager.Current?.Key == "ctx.systemmap"
            && Game.Instance?.CurrentMode == GameModeType.StarSystem;

        // ---- frozen list order (nearest-first at entry; newcomers append, also by distance) ----

        private readonly List<string> _order = new List<string>();  // entity UniqueIds, stable across renders
        private object _orderArea;                                   // the area the order was built for

        public override void OnPush()
        {
            _order.Clear();
            _orderArea = null;
            // Seed the research % (the game recomputes + raises IStarSystemMapResearchProgress; SpaceEvents
            // caches the baseline without announcing).
            try { Game.Instance?.StarSystemMapController?.RecalculateResearchProgress(); }
            catch (Exception e) { Main.Log?.Log("research seed failed: " + e.Message); }
        }

        // ---- the graph ----

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var game = Game.Instance;
            var staticPart = game?.RootUiContext?.SpaceVM?.StaticPartVM;
            if (game == null || staticPart == null) return;

            var live = LiveObjects(game);
            SyncOrder(game, live);
            var overtips = OvertipsByEntity(game);

            // -- Objects --
            b.BeginStop("objects").PushContext(Loc.T("systemmap.objects"), role: "list");
            bool any = false;
            foreach (var uid in _order)
            {
                if (!live.TryGetValue(uid, out var entity)) continue; // left the map; stays in order for return
                var e = entity; // capture per iteration
                overtips.TryGetValue(uid, out var vm);
                var overtipVm = vm;
                b.AddItem(ControlId.Referenced(e, "sso:" + uid), GraphNodes.Button(
                    () => ObjectLabel(e, overtipVm),
                    () => FlyTo(e, overtipVm),
                    tooltip: PlanetTooltip(e, overtipVm as OvertipEntityPlanetVM)));
                any = true;
            }
            if (!any)
                b.AddItem(ControlId.Structural("sso:none"), GraphNodes.Button(
                    () => Loc.T("systemmap.no_objects"), () => { }, () => false));
            b.PopContext();

            // -- Status --
            b.BeginStop("status").PushContext(Loc.T("systemmap.status"), role: "list");
            b.AddLabel(ControlId.Structural("status:system"), () => SystemLine(game));
            b.AddLabel(ControlId.Structural("status:pf"), () => Loc.T("systemmap.profit_factor",
                new { n = Mathf.RoundToInt(game.Player?.ProfitFactor?.Total ?? 0f) }));
            b.AddLabel(ControlId.Structural("status:resources"), ResourcesLine);
            b.AddLabel(ControlId.Structural("status:ship"), () => Loc.T(
                ZoneExit()?.ShipIsMoving.Value == true ? "systemmap.ship_underway" : "systemmap.ship_holding"));
            b.AddLabel(ControlId.Structural("status:radar"), RadarLine);
            b.PopContext();

            // -- Actions --
            b.BeginStop("actions").PushContext(Loc.T("systemmap.actions"), role: "list");
            b.AddItem(ControlId.Structural("act:warp"), GraphNodes.Button(
                () => Loc.T("systemmap.warp_jump"),
                () => { if (Interactive) ZoneExit()?.ExitToWarp(); },
                () => ZoneExit()?.IsWarpJumpAvailable.Value == true));
            b.AddItem(ControlId.Structural("act:stop"), GraphNodes.Button(
                () => Loc.T("systemmap.stop_ship"),
                () => { if (Interactive) Game.Instance?.GameCommandQueue?.StopStarSystemStarShip(); },
                () => ZoneExit()?.ShipIsMoving.Value == true));
            b.AddItem(ControlId.Structural("act:bridge"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.SpaceCombatTexts.BackToShipBridge, "systemmap.to_bridge"),
                () => { if (Interactive) ZoneExit()?.ExitToShip(); },
                () => ZoneExit()?.IsExitAvailable.Value == true));
            foreach (var t in WindowButtons)
            {
                var type = t; // capture
                b.AddItem(ControlId.Structural("act:win:" + type), GraphNodes.Button(
                    () => InGameScreen.WindowLabel(type),
                    () => staticPart.ServiceWindowsVM?.HandleOpenWindowOfType(type),
                    () => InGameScreen.WindowEnabled(type),
                    hoverSound: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound,
                    clickSound: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound));
            }
            b.AddItem(ControlId.Structural("act:log"), GraphNodes.Button(
                () => Loc.T("hud.log"), LogReviewScreen.Open));
            b.PopContext();
        }

        // The service windows the game's own HUD offers on the system map (the StarSystem-mode keybind set,
        // M0 dump). LocalMap is not one of them; the surface-only windows stay on ctx.ingame.
        private static readonly ServiceWindowsType[] WindowButtons =
        {
            ServiceWindowsType.Inventory, ServiceWindowsType.CharacterInfo, ServiceWindowsType.Journal,
            ServiceWindowsType.Encyclopedia, ServiceWindowsType.ShipCustomization,
            ServiceWindowsType.ColonyManagement, ServiceWindowsType.CargoManagement,
            ServiceWindowsType.Augmentations,
        };

        // ---- object enumeration + order ----

        // Every object a sighted player can see/click: IsInGame, minus HideInUI anomalies (hidden from the
        // whole visual layer). M0: no fog on the system map, so there is no positional gate.
        private static Dictionary<string, StarSystemObjectEntity> LiveObjects(Game game)
        {
            var result = new Dictionary<string, StarSystemObjectEntity>();
            foreach (var e in game.State.StarSystemObjects.All)
            {
                if (e == null || !e.IsInGame) continue;
                if (e is AnomalyEntityData && (e.Blueprint as BlueprintAnomaly)?.HideInUI == true) continue;
                result[e.UniqueId] = e;
            }
            return result;
        }

        private void SyncOrder(Game game, Dictionary<string, StarSystemObjectEntity> live)
        {
            var area = game.CurrentlyLoadedArea;
            if (!ReferenceEquals(area, _orderArea)) { _order.Clear(); _orderArea = area; }

            var known = new HashSet<string>(_order);
            var newcomers = new List<StarSystemObjectEntity>();
            foreach (var kv in live)
                if (!known.Contains(kv.Key)) newcomers.Add(kv.Value);
            if (newcomers.Count == 0) return;

            var ship = ShipPos();
            newcomers.Sort((a2, b2) =>
                RTAccess.Exploration.Geo.Distance(ship, a2.Position)
                    .CompareTo(RTAccess.Exploration.Geo.Distance(ship, b2.Position)));
            foreach (var e in newcomers) _order.Add(e.UniqueId);
        }

        private static Vector3 ShipPos()
        {
            var ship = Game.Instance?.StarSystemMapController?.StarSystemShip;
            return ship != null ? ship.Position : Vector3.zero;
        }

        // The live overtip VMs, keyed by entity — the exact card state the game renders (label law:
        // the browse label mirrors the card). Rebuilt per render; the collections churn on area load.
        private static Dictionary<string, object> OvertipsByEntity(Game game)
        {
            var result = new Dictionary<string, object>();
            try
            {
                var overtips = game.RootUiContext?.SpaceVM?.DynamicPartVM?.SpaceOvertipsVM?.Value?.SystemMapOvertipsVM;
                if (overtips == null) return result;
                foreach (var vm in overtips.PlanetOvertipsCollectionVM.Overtips)
                    if (vm?.PlanetObject != null) result[vm.PlanetObject.UniqueId] = vm;
                foreach (var vm in overtips.AnomalyOvertipsCollectionVM.Overtips)
                    if (vm?.SystemMapObject != null) result[vm.SystemMapObject.UniqueId] = vm;
                foreach (var vm in overtips.SystemObjectOvertipsCollectionVM.Overtips)
                    if (vm?.SystemMapObject != null) result[vm.SystemMapObject.UniqueId] = vm;
            }
            catch (Exception e) { Main.Log?.Log("overtip lookup failed: " + e.Message); }
            return result;
        }

        // ---- labels (mirror the overtip card; [[rt-label-mirror-visual]]) ----

        private static string ObjectLabel(StarSystemObjectEntity entity, object overtipVm)
        {
            try
            {
                var parts = new List<string>();
                switch (overtipVm)
                {
                    case OvertipEntityPlanetVM p:
                        parts.Add(p.PlanetIsScanned.Value
                            ? (string.IsNullOrWhiteSpace(p.PlanetName.Value) ? "???" : p.PlanetName.Value)
                            : Loc.T("systemmap.unknown_planet"));
                        parts.Add(Loc.T("systemmap.type_planet"));
                        if (p.HasColony.Value) parts.Add(Loc.T("systemmap.has_colony"));
                        if (p.HasQuest.Value) parts.Add(Loc.T("systemmap.has_quest"));
                        if (p.HasRumour.Value) parts.Add(Loc.T("systemmap.has_rumour"));
                        if (p.HasResource.Value) parts.Add(Loc.T("systemmap.has_resources"));
                        if (p.HasExtractor.Value) parts.Add(Loc.T("systemmap.has_extractor"));
                        if (p.HasPoi.Value) parts.Add(PoiCount(p.PoiNamesList));
                        if (entity.IsFullyExplored && p.PlanetIsScanned.Value) parts.Add(Loc.T("systemmap.fully_explored"));
                        break;

                    case OvertipEntityAnomalyVM a:
                        parts.Add(string.IsNullOrWhiteSpace(a.AnomalyName.Value)
                            ? Loc.T("systemmap.type_anomaly") : a.AnomalyName.Value);
                        parts.Add(AnomalyTypeWord(entity));
                        if (a.HasQuest.Value) parts.Add(Loc.T("systemmap.has_quest"));
                        if (a.IsExplored.Value) parts.Add(Loc.T("systemmap.explored"));
                        break;

                    case OvertipEntitySystemObjectVM o:
                        parts.Add(o.IsScanned.Value
                            ? (string.IsNullOrWhiteSpace(entity.Blueprint?.Name) ? "???" : entity.Blueprint.Name)
                            : "???"); // the card's literal unscanned label
                        parts.Add(TypeWord(entity));
                        if (o.IsPoi.Value) parts.Add(PoiCount(o.PoiNamesList));
                        if (entity.IsFullyExplored && o.IsScanned.Value) parts.Add(Loc.T("systemmap.fully_explored"));
                        break;

                    default: // no overtip VM (star/comet/cloud, or the collections not built yet)
                        bool scanned = entity.IsScanned || entity.IsScannedOnStart;
                        parts.Add(scanned && !string.IsNullOrWhiteSpace(entity.Blueprint?.Name)
                            ? entity.Blueprint.Name : "???");
                        parts.Add(TypeWord(entity));
                        break;
                }
                parts.Add(BearingAndUnits(ShipPos(), entity.Position));
                return string.Join(", ", parts);
            }
            catch (Exception e)
            {
                Main.Log?.Error("SystemMapScreen.ObjectLabel: " + e);
                return Loc.T("systemmap.type_anomaly");
            }
        }

        private static string PoiCount(List<string> names)
        {
            int n = names?.Count ?? 0;
            return n == 1 ? Loc.T("systemmap.pois_one") : Loc.T("systemmap.pois_many", new { n });
        }

        private static string TypeWord(StarSystemObjectEntity entity)
        {
            switch (entity)
            {
                case PlanetEntity _: return Loc.T("systemmap.type_planet");
                case StarEntity _: return Loc.T("systemmap.type_star");
                case AsteroidEntity _: return Loc.T("systemmap.type_asteroid");
                case CometEntity _: return Loc.T("systemmap.type_comet");
                case CloudEntity _: return Loc.T("systemmap.type_cloud");
                case ArtificialObjectEntity _: return Loc.T("systemmap.type_artificial");
                case AnomalyEntityData _: return Loc.T("systemmap.type_anomaly");
                default: return Loc.T("systemmap.type_artificial");
            }
        }

        // Anomaly type word exactly as the card's icon hint speaks it: the game's own localized type name.
        private static string AnomalyTypeWord(StarSystemObjectEntity entity)
        {
            try
            {
                var bp = entity.Blueprint as BlueprintAnomaly;
                if (bp != null)
                {
                    var name = UIStrings.Instance?.ExplorationTexts?.GetAnomalyTypeName(bp.AnomalyType);
                    if (!string.IsNullOrWhiteSpace(name))
                        return Loc.T("systemmap.type_anomaly") + ", " + name;
                }
            }
            catch { /* fall through to the plain word */ }
            return Loc.T("systemmap.type_anomaly");
        }

        /// <summary>Bearing + distance from the ship, e.g. "14 units, north-east". Map plane is XZ (M0);
        /// same compass as every other readout. Distances are the map's schematic units — an orrery diagram,
        /// not metres.</summary>
        private static string BearingAndUnits(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            var s = Loc.T("systemmap.units", new { n = Mathf.RoundToInt(dist) });
            if (dist > 0.5f)
            {
                float angle = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg; // 0 = north(+Z), +90 = east(+X)
                int sector = ((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8;
                s += ", " + Accessibility.InteractableDescriber.Compass8[sector];
            }
            return s;
        }

        // ---- verbs ----

        // Fly to the object — the overtip card's click. Through the VM when the card exists (its
        // RequestVisit is the game's own MoveShip command + coop gate); the raw command covers the
        // overtip-less objects (stars). Spoken on the keypress → interrupt; the movement-started event
        // is suppressed via MarkCommandedMove.
        private static void FlyTo(StarSystemObjectEntity entity, object overtipVm)
        {
            if (!Interactive) { Tts.Speak(Loc.T("systemmap.not_now"), interrupt: true); return; }
            try
            {
                Accessibility.SpaceEvents.MarkCommandedMove();
                switch (overtipVm)
                {
                    case OvertipEntityPlanetVM p: p.RequestVisit(); break;
                    case OvertipEntityAnomalyVM a: a.RequestVisit(); break;
                    case OvertipEntitySystemObjectVM o: o.RequestVisit(); break;
                    default:
                        Game.Instance.GameCommandQueue.MoveShip(entity, MoveShipGameCommand.VisitType.MovePlayerShip);
                        break;
                }
                string name = ObjectShortName(entity, overtipVm);
                Tts.Speak(Loc.T("systemmap.traveling_to", new { name }), interrupt: true);
            }
            catch (Exception e) { Main.Log?.Error("SystemMapScreen.FlyTo failed: " + e); }
        }

        private static string ObjectShortName(StarSystemObjectEntity entity, object overtipVm)
        {
            switch (overtipVm)
            {
                case OvertipEntityPlanetVM p when p.PlanetIsScanned.Value && !string.IsNullOrWhiteSpace(p.PlanetName.Value):
                    return p.PlanetName.Value;
                case OvertipEntityPlanetVM _:
                    return Loc.T("systemmap.unknown_planet");
                case OvertipEntityAnomalyVM a when !string.IsNullOrWhiteSpace(a.AnomalyName.Value):
                    return a.AnomalyName.Value;
                default:
                    bool scanned = entity.IsScanned || entity.IsScannedOnStart;
                    return scanned && !string.IsNullOrWhiteSpace(entity.Blueprint?.Name)
                        ? entity.Blueprint.Name : TypeWord(entity);
            }
        }

        // Space on a planet: the full hover tooltip (explored state, colony, quests, rumours, POIs,
        // resources — TooltipTemplateSystemMapPlanet, the card's own template). Other kinds carry no
        // tooltip on the card, so none here either.
        private static Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> PlanetTooltip(
            StarSystemObjectEntity entity, OvertipEntityPlanetVM planetVm)
        {
            if (planetVm == null) return null;
            return () =>
            {
                var view = entity.View;
                var planetView = planetVm.PlanetView.Value;
                return view != null ? new TooltipTemplateSystemMapPlanet(view, planetView) : null;
            };
        }

        // ---- status lines ----

        private static string SystemLine(Game game)
        {
            string name = game?.CurrentlyLoadedArea?.AreaDisplayName;
            var s = string.IsNullOrWhiteSpace(name) ? Loc.T("systemmap.screen") : name;
            int pct = Accessibility.SpaceEvents.ResearchPercent;
            if (pct >= 0) s += ", " + Loc.T("systemmap.research", new { pct });
            return s;
        }

        private static string ResourcesLine()
        {
            try
            {
                var pool = Game.Instance?.ColonizationController?.AllResourcesInPool();
                if (pool != null)
                {
                    var parts = new List<string>();
                    foreach (var kv in pool)
                        if (kv.Value != 0 && kv.Key != null)
                            parts.Add(kv.Key.Name + " " + kv.Value);
                    if (parts.Count > 0)
                        return Loc.T("systemmap.resources", new { list = string.Join(", ", parts) });
                }
            }
            catch (Exception e) { Main.Log?.Log("resources line failed: " + e.Message); }
            return Loc.T("systemmap.resources_none");
        }

        // The anomaly radar's readout — sighted players get every un-interacted, non-hidden anomaly as a
        // radar blip plus a type list after the sweep (SystemScannerVM), so the count + types are parity.
        private static string RadarLine()
        {
            try
            {
                var scanner = Game.Instance?.RootUiContext?.SpaceVM?.StaticPartVM?.SystemScannerVM;
                int n = scanner?.ObjectsList?.Count ?? 0;
                if (n == 0) return Loc.T("systemmap.no_anomalies");
                var s = Loc.T("systemmap.radar", new { n });
                var types = scanner.AnomaliesTypesText;
                if (types != null && types.Count > 0) s += ": " + string.Join(", ", types);
                return s;
            }
            catch (Exception e) { Main.Log?.Log("radar line failed: " + e.Message); return Loc.T("systemmap.no_anomalies"); }
        }

        private static ZoneExitVM ZoneExit() => Game.Instance?.RootUiContext?.SpaceVM?.StaticPartVM?.ZoneExitVM;

        // ---- input ----

        // Escape while focused backs out of the list to the bare map (arrows return to the camera); while
        // unfocused the yield hands Escape to the game — its own Esc stops a moving ship, else the pause menu.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Raw("Back"), _ =>
            {
                // Node-based HasFocus, not Navigation.Current — graph-native nodes have no backing UIElement.
                if (!Navigation.HasFocus) return; // unfocused → the game's Escape (stop ship / menu)
                Navigation.Blur();
                Tts.Speak(Loc.T("systemmap.screen"), interrupt: true);
            });
        }
    }
}
