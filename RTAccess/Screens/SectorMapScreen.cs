using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.NavigatorResource;          // SectorMapBottomHudVM
using Kingmaker.Code.UI.MVVM.VM.Overtips.SectorMap;          // OvertipEntitySystemVM, SectorMapOvertipsVM
using Kingmaker.Code.UI.MVVM.VM.SectorMap;                   // SectorMapVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;              // ServiceWindowsType
using Kingmaker.Code.UI.MVVM.VM.Space;                       // SpaceStaticPartVM, SpaceStaticComponentType
using Kingmaker.Controllers.GlobalMap;                       // SectorMapController
using Kingmaker.GameModes;                                   // GameModeType
using Kingmaker.Globalmap.Blueprints;                        // BlueprintSectorMapArea (the area type)
using Kingmaker.Globalmap.SectorMap;                         // SectorMapObject(Entity), SectorMapPassageEntity
using RTAccess.Localization;
using RTAccess.UI;
using RTAccess.UI.Graph;
using UnityEngine;

namespace RTAccess.Screens
{
    /// <summary>
    /// The SECTOR MAP — the warp/galaxy view (<c>GameModeType.GlobalMap</c>, a <see cref="BlueprintSectorMapArea"/>):
    /// the ship among star-system NODES joined by warp PASSAGES. The galaxy-scale sibling of
    /// <see cref="SystemMapScreen"/> (in-system) and built to the same recipe: a flat, frozen, nearest-first list
    /// of graph nodes, labels mirroring the overtip card, verbs driving the game's own VM/controller.
    ///
    /// Three Tab stops:
    /// <b>Systems</b> — every known system (<c>IsExploredOrHasQuests</c>, the card-visibility gate), order FROZEN
    /// nearest-first at area entry (the WA no-reshuffle-under-the-cursor lesson). The label mirrors the card:
    /// name, status word (here / visited / reachable+route / no route), colony/quest/rumour flags, bearing +
    /// distance. Enter opens a verb submenu (Travel / Enter system) driven through the overtip VM's own methods.
    /// <b>Status</b> — Navigator's Resource, current system, travel/scan state.
    /// <b>Actions</b> — scan from here, exit to ship, ship customization, service windows, log review.
    ///
    /// UNFOCUSED by default (the camera keeps the arrows; Tab enters the list), like the system map. Verbs act
    /// only while the sector map is the CURRENT mode and NOT mid-jump / under a dialog (<see cref="Interactive"/>);
    /// READING (labels/status) stays live throughout, including during a jump — only the verbs gate. Warp travel
    /// is non-cancellable once started (the game exposes no abort), so there is deliberately no "stop travel" verb.
    ///
    /// Passive narration (entering/leaving warp, scan results, route changes) is owned by
    /// <see cref="RTAccess.Accessibility.WarpEvents"/>, the <see cref="RTAccess.Accessibility.SpaceEvents"/> sibling.
    ///
    /// Status: BUILT FROM THE DECOMPILE, UNTESTED IN-HARNESS (warp travel is quest-gated). Items flagged
    /// TODO(harness) below need live verification before they are trusted — see
    /// docs/plans/warp-sector-map-accessibility.md §"In-game test checklist".
    /// </summary>
    public sealed class SectorMapScreen : Screen
    {
        public override string Key => "ctx.sectormap";
        public override string ScreenName => Loc.T("sectormap.screen");
        public override int Layer => 0;                     // base context, sibling of ctx.systemmap / ctx.ingame
        public override bool StartUnfocused => true;        // camera keeps the arrows; Tab enters the list

        // The sector map is its own area type — naturally mutually exclusive with the star-system map and the
        // surface (you are in exactly one loaded area), so no extra exclusion predicate is needed.
        public override bool IsActive()
            => Game.Instance?.CurrentlyLoadedArea is BlueprintSectorMapArea
               && Game.Instance.RootUiContext?.SpaceVM != null;

        /// <summary>Verbs may act: this is the live top screen, the sector map is the CURRENT mode, and we are not
        /// mid-jump or under a dialog/book-event layered over it (acting there desyncs the game / loops the resume
        /// — the WA mode-stack lesson). READING is NOT gated by this — labels and status read during a jump too.</summary>
        public static bool Interactive
        {
            get
            {
                if (ScreenManager.Current?.Key != "ctx.sectormap") return false;
                if (Game.Instance?.CurrentMode != GameModeType.GlobalMap) return false;
                var vm = Sector();
                if (vm == null) return true; // no VM to consult → assume actable (defensive; IsActive already true)
                return vm.IsTraveling.Value != true && vm.IsDialogActive.Value != true;
            }
        }

        // ---- frozen list order (nearest-first at entry; newcomers append, also by distance) ----

        private readonly List<string> _order = new List<string>();  // entity UniqueIds, stable across renders
        private object _orderArea;                                   // the area the order was built for

        public override void OnPush()
        {
            _order.Clear();
            _orderArea = null;
        }

        // ---- the graph ----

        public override void Build(GraphBuilder b)
        {
            var game = Game.Instance;
            var staticPart = game?.RootUiContext?.SpaceVM?.StaticPartVM;
            if (game == null || staticPart == null) return;

            var live = LiveSystems();
            SyncOrder(live);
            var overtips = OvertipsByEntity();

            // -- Systems --
            b.BeginStop("systems").PushContext(Loc.T("sectormap.systems"), role: "list");
            bool any = false;
            foreach (var uid in _order)
            {
                if (!live.TryGetValue(uid, out var view)) continue; // gone from the visible set; stays in order
                var v = view; // capture per iteration
                overtips.TryGetValue(uid, out var vm);
                var overtipVm = vm;
                b.AddItem(ControlId.Referenced(v.Data, "sms:" + uid), GraphNodes.Button(
                    () => SystemLabel(v, overtipVm),
                    () => Activate(v, overtipVm)));
                any = true;
            }
            if (!any)
                b.AddItem(ControlId.Structural("sms:none"), GraphNodes.Button(
                    () => Loc.T("sectormap.no_systems"), () => { }, () => false));
            b.PopContext();

            // -- Status --
            b.BeginStop("status").PushContext(Loc.T("sectormap.status"), role: "list");
            b.AddLabel(ControlId.Structural("status:nav"), NavigatorResourceLine);
            b.AddLabel(ControlId.Structural("status:current"), CurrentSystemLine);
            b.AddLabel(ControlId.Structural("status:state"), TravelStateLine);
            b.PopContext();

            // -- Actions --
            b.BeginStop("actions").PushContext(Loc.T("sectormap.actions"), role: "list");
            b.AddItem(ControlId.Structural("act:scan"), GraphNodes.Button(
                () => Loc.T("sectormap.act_scan"),
                () => { if (Interactive) Hud()?.ScanSystem(); },
                () => Hud()?.IsScanAvailable.Value == true));
            b.AddItem(ControlId.Structural("act:exit"), GraphNodes.Button(
                () => Loc.T("sectormap.act_exit"),
                () => { if (Interactive) Hud()?.ExitToShip(); },
                () => Hud()?.IsExitAvailable.Value == true));
            b.AddItem(ControlId.Structural("act:ship"), GraphNodes.Button(
                () => Loc.T("sectormap.act_ship"),
                () => { if (Interactive) Hud()?.OpenShipCustomization(); }));
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

        // The service windows offered from the sector-map HUD (same GlobalMap-adjacent set the system map uses;
        // the game surfaces the ship/roster/journal here too). TODO(harness): confirm which are actually enabled
        // in GlobalMap mode and prune.
        private static readonly ServiceWindowsType[] WindowButtons =
        {
            ServiceWindowsType.Inventory, ServiceWindowsType.CharacterInfo, ServiceWindowsType.Journal,
            ServiceWindowsType.Encyclopedia, ServiceWindowsType.ShipCustomization,
            ServiceWindowsType.ColonyManagement, ServiceWindowsType.CargoManagement,
            ServiceWindowsType.Augmentations,
        };

        // ---- system enumeration + order ----

        private static SectorMapController Ctrl => Game.Instance?.SectorMapController;

        // The SectorMapVM, reached through the space static-part component dictionary (it is disposed and recreated
        // on mode change — never cache it).
        private static SectorMapVM Sector()
            => Game.Instance?.RootUiContext?.SpaceVM?.StaticPartVM
                ?.TryGetComponentVM(SpaceStaticComponentType.SectorMap) as SectorMapVM;

        private static SectorMapBottomHudVM Hud() => SectorMapBottomHudVM.Instance;

        // Every star system a sighted player can see on the map: IsExploredOrHasQuests (the same gate the node's
        // SetVisible / SetPlanetVisualState uses). Keyed by entity UniqueId → the view (which carries Data, Name,
        // StarSystemToTransit, IsExploredOrHasQuests).
        private static Dictionary<string, SectorMapObject> LiveSystems()
        {
            var result = new Dictionary<string, SectorMapObject>();
            var ctrl = Ctrl;
            if (ctrl == null) return result;
            try
            {
                foreach (var view in ctrl.GetAllStarSystems())
                {
                    if (view?.Data == null || !view.IsExploredOrHasQuests) continue;
                    result[view.Data.UniqueId] = view;
                }
            }
            catch (Exception e) { Main.Log?.Log("sector systems enum failed: " + e.Message); }
            return result;
        }

        private void SyncOrder(Dictionary<string, SectorMapObject> live)
        {
            var area = Game.Instance?.CurrentlyLoadedArea;
            if (!ReferenceEquals(area, _orderArea)) { _order.Clear(); _orderArea = area; }

            var known = new HashSet<string>(_order);
            var newcomers = new List<SectorMapObject>();
            foreach (var kv in live)
                if (!known.Contains(kv.Key)) newcomers.Add(kv.Value);
            if (newcomers.Count == 0) return;

            var origin = CurrentPos();
            newcomers.Sort((a, b) =>
                RTAccess.Exploration.Geo.Distance(origin, a.Data.Position)
                    .CompareTo(RTAccess.Exploration.Geo.Distance(origin, b.Data.Position)));
            foreach (var v in newcomers) _order.Add(v.Data.UniqueId);
        }

        private static Vector3 CurrentPos()
        {
            var cur = Ctrl?.CurrentStarSystem;
            return cur != null ? cur.Position : Vector3.zero;
        }

        // The live overtip VMs, keyed by entity UniqueId — the exact card state the game renders.
        private static Dictionary<string, OvertipEntitySystemVM> OvertipsByEntity()
        {
            var result = new Dictionary<string, OvertipEntitySystemVM>();
            try
            {
                var overtips = SectorMapOvertipsVM.Instance?.SystemOvertipsCollectionVM?.Overtips;
                if (overtips == null) return result;
                foreach (var vm in overtips)
                    if (vm?.SectorMapObject != null) result[vm.SectorMapObject.UniqueId] = vm;
            }
            catch (Exception e) { Main.Log?.Log("sector overtip lookup failed: " + e.Message); }
            return result;
        }

        // ---- labels (mirror the overtip card; [[rt-label-mirror-visual]]) ----

        private static string SystemLabel(SectorMapObject view, OvertipEntitySystemVM overtip)
        {
            try
            {
                var entity = view.Data;
                var ctrl = Ctrl;
                var current = ctrl?.CurrentStarSystem;
                var parts = new List<string>
                {
                    view.IsExploredOrHasQuests && !string.IsNullOrWhiteSpace(view.Name)
                        ? view.Name : Loc.T("sectormap.unknown_system"),
                };

                bool isCurrent = current != null && entity == current;
                SectorMapPassageEntity passage = (!isCurrent && current != null)
                    ? ctrl.FindPassageBetween(current, entity) : null;
                bool reachable = passage != null && passage.IsExplored;

                if (isCurrent) parts.Add(Loc.T("sectormap.here"));
                else if (entity.IsVisited) parts.Add(Loc.T("sectormap.visited"));
                else if (reachable)
                    parts.Add(Loc.T("sectormap.route", new
                    {
                        difficulty = DifficultyWord(passage.CurrentDifficulty),
                        chance = Mathf.RoundToInt(passage.EncounterChance),
                    }));
                else parts.Add(Loc.T("sectormap.no_route"));

                // Card flags, read from live game state (robust regardless of overtip refresh timing).
                if (Game.Instance?.ColonizationController?.GetColony(view) != null) parts.Add(Loc.T("systemmap.has_colony"));
                if (SafeCheckQuests(view)) parts.Add(Loc.T("systemmap.has_quest"));
                if (SafeCheckRumours(overtip)) parts.Add(Loc.T("systemmap.has_rumour"));

                parts.Add(BearingAndUnits(CurrentPos(), entity.Position));
                return string.Join(", ", parts);
            }
            catch (Exception e)
            {
                Main.Log?.Error("SectorMapScreen.SystemLabel: " + e);
                return string.IsNullOrWhiteSpace(view?.Name) ? Loc.T("sectormap.unknown_system") : view.Name;
            }
        }

        private static bool SafeCheckQuests(SectorMapObject view)
        {
            try { return view.CheckQuests(); } catch { return false; }
        }

        // Rumour presence via the overtip VM (the card's rumour icon). Best-effort: CheckRumours recomputes and
        // returns whether any rumour targets this system. TODO(harness): confirm this reads correctly under our
        // forced Mouse mode (the overtip collection is built on the same path the system map already uses).
        private static bool SafeCheckRumours(OvertipEntitySystemVM overtip)
        {
            if (overtip == null) return false;
            try { return overtip.CheckRumours(); } catch { return false; }
        }

        /// <summary>The passage-difficulty word (Safe / Unsafe / Dangerous / Deadly). Localized on our side for
        /// certainty; TODO(harness): consider switching to the game's own <c>UIStrings.GlobalMapPassages</c>
        /// string once verified, so it matches the sighted skull-tier wording exactly.</summary>
        internal static string DifficultyWord(SectorMapPassageEntity.PassageDifficulty d)
        {
            switch (d)
            {
                case SectorMapPassageEntity.PassageDifficulty.Safe: return Loc.T("sectormap.diff_safe");
                case SectorMapPassageEntity.PassageDifficulty.Unsafe: return Loc.T("sectormap.diff_unsafe");
                case SectorMapPassageEntity.PassageDifficulty.Dangerous: return Loc.T("sectormap.diff_dangerous");
                case SectorMapPassageEntity.PassageDifficulty.Deadly: return Loc.T("sectormap.diff_deadly");
                default: return Loc.T("sectormap.diff_unsafe");
            }
        }

        // Bearing + distance from the ship, e.g. "14 units, north-east". Map plane is XZ (same as the system map).
        // Distances are the map's schematic units, not a real measure.
        private static string BearingAndUnits(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            float dist = Exploration.Geo.Distance(from, to);
            var s = Loc.T("systemmap.units", new { n = Mathf.RoundToInt(dist) });
            if (dist > 0.5f && Exploration.Geo.CompassSector(dx, dz, out int sector))
                s += ", " + Loc.T(Accessibility.InteractableDescriber.Compass8[sector]);
            return s;
        }

        // ---- verbs (Enter → the valid actions for that system) ----

        private void Activate(SectorMapObject view, OvertipEntitySystemVM overtip)
        {
            if (!Interactive) { Tts.Speak(Loc.T("sectormap.not_now"), interrupt: true); return; }
            try
            {
                var ctrl = Ctrl;
                var current = ctrl?.CurrentStarSystem;
                var entity = view.Data;
                bool isCurrent = current != null && entity == current;
                var passage = (!isCurrent && current != null) ? ctrl.FindPassageBetween(current, entity) : null;
                bool canTravel = passage != null && passage.IsExplored;
                bool canVisit = isCurrent && view.StarSystemToTransit != null;
                string name = view.Name;

                var rows = new List<ChoiceSubmenuScreen.Row>();
                if (canTravel)
                    rows.Add(ChoiceSubmenuScreen.Row.Action(
                        () => Loc.T("sectormap.verb_travel", new { name }), () => DoTravel(view, overtip, name)));
                if (canVisit)
                    rows.Add(ChoiceSubmenuScreen.Row.Action(
                        () => Loc.T("sectormap.verb_visit", new { name }), () => DoVisit(view, overtip, name)));
                if (rows.Count == 0)
                    rows.Add(ChoiceSubmenuScreen.Row.Header(() => Loc.T("sectormap.verb_none")));

                ChoiceSubmenuScreen.OpenRows(name, rows);
            }
            catch (Exception e) { Main.Log?.Error("SectorMapScreen.Activate failed: " + e); }
        }

        // Warp travel — drive the overtip VM's own path (guards the passage-explored check + coop ping), falling
        // back to the controller for the overtip-less case. WarpEvents' started-line is suppressed since we speak
        // the destination on the keypress here.
        private static void DoTravel(SectorMapObject view, OvertipEntitySystemVM overtip, string name)
        {
            if (!Interactive) { Tts.Speak(Loc.T("sectormap.not_now"), interrupt: true); return; }
            try
            {
                Accessibility.WarpEvents.MarkCommandedTravel();
                if (overtip != null) overtip.TravelToSystemImmediately();
                else Ctrl?.WarpTravel(view);
                Tts.Speak(Loc.T("sectormap.verb_travel", new { name }), interrupt: true);
            }
            catch (Exception e) { Main.Log?.Error("SectorMapScreen.DoTravel failed: " + e); }
        }

        // Enter the system's area (load it) — the current-system "Visit" verb.
        private static void DoVisit(SectorMapObject view, OvertipEntitySystemVM overtip, string name)
        {
            if (!Interactive) { Tts.Speak(Loc.T("sectormap.not_now"), interrupt: true); return; }
            try
            {
                if (overtip != null) overtip.VisitSystem();
                else SectorMapController.VisitStarSystem(view.Data);
                Tts.Speak(Loc.T("sectormap.verb_visit", new { name }), interrupt: true);
            }
            catch (Exception e) { Main.Log?.Error("SectorMapScreen.DoVisit failed: " + e); }
        }

        // ---- status lines ----

        private static string NavigatorResourceLine()
        {
            var hud = Hud();
            int n = hud?.CurrentValue.Value ?? Game.Instance?.Player?.WarpTravelState?.NavigatorResource ?? 0;
            var s = Loc.T("sectormap.navigator_resource", new { n });
            if (hud != null && hud.IsWillChangeNavigatorResource.Value)
                s += ", " + Loc.T("sectormap.nav_will_change", new { n = hud.WillChangeNavigatorResourceCount.Value });
            return s;
        }

        private static string CurrentSystemLine()
        {
            string name = Ctrl?.CurrentStarSystem?.View?.Name;
            return Loc.T("sectormap.current", new { name = string.IsNullOrWhiteSpace(name) ? "?" : name });
        }

        private static string TravelStateLine()
        {
            var vm = Sector();
            if (vm?.IsTraveling.Value == true)
            {
                string dest = Game.Instance?.SectorMapTravelController?.To?.View?.Name;
                return string.IsNullOrWhiteSpace(dest)
                    ? Loc.T("sectormap.in_warp")
                    : Loc.T("sectormap.in_warp_to", new { name = dest });
            }
            if (vm?.IsScanning.Value == true || Ctrl?.IsScanning == true) return Loc.T("sectormap.scanning");
            return Loc.T("sectormap.idle");
        }

        // ---- input ----

        // Escape while focused backs out of the list to the bare map (arrows return to the camera); while
        // unfocused the yield hands Escape to the game (its own sector-map Escape / pause menu).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Raw("Back"), _ =>
            {
                if (!Navigation.HasFocus) return; // unfocused → the game's Escape
                Navigation.Blur();
                Tts.Speak(Loc.T("sectormap.screen"), interrupt: true);
            });
        }
    }
}
