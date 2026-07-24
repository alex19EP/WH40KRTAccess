using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.NavigatorResource;          // SectorMapBottomHudVM
using Kingmaker.Code.UI.MVVM.VM.Overtips.SectorMap;          // OvertipEntitySystemVM, SectorMapOvertipsVM
using Kingmaker.Code.UI.MVVM.VM.SectorMap;                   // SectorMapVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;              // ServiceWindowsType
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;          // TooltipTemplateSimple (the Space readout)
using Kingmaker.Code.UI.MVVM.VM.Space;                       // SpaceStaticPartVM, SpaceStaticComponentType
using Kingmaker.Controllers.GlobalMap;                       // SectorMapController
using Kingmaker.GameCommands;                                // GameCommandQueue.CreateNewWarpRoute / LowerWarpRouteDifficulty
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

        // Type-ahead letter-search is OFF here: the link-walk keys (m / Shift+m step links, c = home) are bare
        // letters that the Systems-list search would otherwise swallow. Search isn't worth much on the sector map
        // (the frozen nearest-first list + the walk cover navigation), so we drop it to free the letters.
        public override bool AllowsTypeahead => false;

        // Declares the WorldMap input category (alongside UI) so the sector-map link-walk keys (registered as
        // "sectormap.*" in InputBindings, handled by SectorMapWalk) are live ONLY while this screen is on top.
        private static readonly IReadOnlyList<RTAccess.Input.InputCategory> Categories =
            new[] { RTAccess.Input.InputCategory.UI, RTAccess.Input.InputCategory.WorldMap };
        public override IReadOnlyList<RTAccess.Input.InputCategory> InputCategories => Categories;

        // The sector map is its own area type — naturally mutually exclusive with the star-system map and the
        // surface (you are in exactly one loaded area), so no extra exclusion predicate is needed.
        public override bool IsActive()
            => Game.Instance?.CurrentlyLoadedArea is BlueprintSectorMapArea
               && Game.Instance.RootUiContext?.SpaceVM != null;

        /// <summary>Verbs may act: the sector map is the CURRENT game mode and we are not mid-jump or under a
        /// dialog/book-event (acting there desyncs the game / loops the resume — the WA mode-stack lesson). GlobalMap
        /// mode is the authoritative "we're on the warp map" signal — do NOT also require ctx.sectormap to be the TOP
        /// mod screen: the Travel/Visit verbs are invoked from OUR ChoiceSubmenuScreen picker, which sits on top of
        /// the sector map (Current == the submenu, not ctx.sectormap) yet leaves the game mode GlobalMap — gating on
        /// the top screen there wrongly rejected our own verb picker with "Not available now". READING is NOT gated by
        /// this — labels and status read during a jump too.</summary>
        public static bool Interactive
        {
            get
            {
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
            SectorMapWalk.Reset();
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
                    () => Activate(v, overtipVm),
                    tooltip: SystemTooltip(v, overtipVm)));   // Space → the system dossier
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
        // the game surfaces the ship/roster/journal here too). ShipCustomization is intentionally NOT here — it has
        // its own dedicated "Ship customization" action (Hud().OpenShipCustomization); listing it as a window too
        // double-voiced it in-game.
        private static readonly ServiceWindowsType[] WindowButtons =
        {
            ServiceWindowsType.Inventory, ServiceWindowsType.CharacterInfo, ServiceWindowsType.Journal,
            ServiceWindowsType.Encyclopedia,
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

        // The display name, or "Unknown system" when it shouldn't be named yet.
        private static string SystemName(SectorMapObject view)
            => view.IsExploredOrHasQuests && !string.IsNullOrWhiteSpace(view.Name)
                ? view.Name : Loc.T("sectormap.unknown_system");

        // Status / route word — mirrors the card's risk indicator (0–3 skulls → difficulty word). Difficulty ONLY:
        // the encounter % is an internal PassagesGenerator roll shown NOWHERE to sighted players, and travel time
        // only surfaces in the history log after committing — so we reveal neither (parity; user call 2026-07-14).
        private static string StatusWord(SectorMapObject view)
        {
            var entity = view.Data;
            var ctrl = Ctrl;
            var current = ctrl?.CurrentStarSystem;
            if (current != null && entity == current) return Loc.T("sectormap.here");
            if (entity.IsVisited) return Loc.T("sectormap.visited");
            var passage = current != null ? ctrl.FindPassageBetween(current, entity) : null;
            return passage != null && passage.IsExplored
                ? Loc.T("sectormap.route", new { difficulty = DifficultyWord(passage.CurrentDifficulty) })
                : Loc.T("sectormap.no_route");
        }

        private static string SystemLabel(SectorMapObject view, OvertipEntitySystemVM overtip)
        {
            try
            {
                var parts = new List<string> { SystemName(view), StatusWord(view) };
                // Card flags, read from live game state (robust regardless of overtip refresh timing).
                if (Game.Instance?.ColonizationController?.GetColony(view) != null) parts.Add(Loc.T("systemmap.has_colony"));
                if (SafeCheckQuests(view)) parts.Add(Loc.T("systemmap.has_quest"));
                if (SafeCheckRumours(overtip)) parts.Add(Loc.T("systemmap.has_rumour"));
                var bearing = Bearing(CurrentPos(), view.Data.Position);
                if (!string.IsNullOrEmpty(bearing)) parts.Add(bearing);
                return string.Join(", ", parts);
            }
            catch (Exception e)
            {
                Main.Log?.Error("SectorMapScreen.SystemLabel: " + e);
                return string.IsNullOrWhiteSpace(view?.Name) ? Loc.T("sectormap.unknown_system") : view.Name;
            }
        }

        // Space on a system → a short dossier: name (title) + status/route word + the quest/rumour objective TITLES
        // the card itself shows (OvertipEntitySystemVM.QuestObjectiveName / RumourObjectiveName — sighted card text)
        // + colony. Same difficulty-only parity as the browse label. Replaces the bare "No tooltip".
        private static Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> SystemTooltip(
            SectorMapObject view, OvertipEntitySystemVM overtip)
            => () => new TooltipTemplateSimple(SystemName(view), SystemDetail(view, overtip));

        private static string SystemDetail(SectorMapObject view, OvertipEntitySystemVM overtip)
        {
            var lines = new List<string> { StatusWord(view) };
            if (overtip != null)
            {
                try { if (overtip.CheckQuests() && !string.IsNullOrWhiteSpace(overtip.QuestObjectiveName.Value)) lines.Add(overtip.QuestObjectiveName.Value); } catch { }
                try { if (overtip.CheckRumours() && !string.IsNullOrWhiteSpace(overtip.RumourObjectiveName.Value)) lines.Add(overtip.RumourObjectiveName.Value); } catch { }
            }
            if (Game.Instance?.ColonizationController?.GetColony(view) != null) lines.Add(Loc.T("systemmap.has_colony"));
            lines.AddRange(ExploredLinks(view));
            return string.Join("\n", lines);
        }

        // The explored warp links radiating from THIS system — the exact set of lines a sighted player sees drawn
        // around the node. SectorMapPassageView.UpdateVisibility draws a passage iff Data.IsExplored, so we mirror
        // that filter precisely: every fully-explored passage touching the system, listed as the OTHER endpoint's
        // name + difficulty word. Half-explored (one-side) and unexplored passages draw no line, so they are omitted
        // here too — no topology the sighted player can't already see ([[rt-visual-parity]]). Closes the mod's
        // narrower-than-sighted gap: the browse label only carries the current→system route, but a sighted player
        // reads the whole explored graph, including links between two non-current systems.
        private static List<string> ExploredLinks(SectorMapObject view)
        {
            var result = new List<string>();
            var mine = view?.Data;
            if (mine == null) return result;
            try
            {
                var links = new List<string>();
                foreach (var n in ExploredNeighbors(mine))
                    links.Add(Loc.T("sectormap.link", new { name = n.system.View.Name, difficulty = DifficultyWord(n.passage.CurrentDifficulty) }));
                if (links.Count > 0)
                {
                    links.Sort(StringComparer.CurrentCultureIgnoreCase);
                    result.Add(Loc.T("sectormap.links_header"));
                    result.AddRange(links);
                }
            }
            catch (Exception e) { Main.Log?.Log("sector links enum failed: " + e.Message); }
            return result;
        }

        // The explored warp links radiating from a system, as (neighbour, passage) pairs — the ONE parity-correct
        // adjacency source, shared by the tooltip's ExploredLinks and the SectorMapWalk link-walk. Mirrors
        // SectorMapPassageView.UpdateVisibility exactly: a passage is drawn (and so exposed here) iff it IsExplored
        // and the far endpoint is named/known (IsExploredOrHasQuests) — half-explored and unexplored links draw no
        // line for a sighted player, so we omit them too ([[rt-visual-parity]]).
        internal static List<(SectorMapObjectEntity system, SectorMapPassageEntity passage)> ExploredNeighbors(SectorMapObjectEntity mine)
        {
            var result = new List<(SectorMapObjectEntity, SectorMapPassageEntity)>();
            var ctrl = Ctrl;
            if (ctrl == null || mine == null) return result;
            foreach (var p in ctrl.AllPassagesForSystem(mine))
            {
                if (p == null || !p.IsExplored) continue;
                var e1 = p.View?.StarSystem1Entity;
                var e2 = p.View?.StarSystem2Entity;
                var other = (e1 != null && e1.UniqueId != mine.UniqueId) ? e1
                          : (e2 != null && e2.UniqueId != mine.UniqueId) ? e2 : null;
                var nv = other?.View;
                if (nv == null || !nv.IsExploredOrHasQuests || string.IsNullOrWhiteSpace(nv.Name)) continue;
                result.Add((other, p));
            }
            return result;
        }

        // Shortest number of explored-passage hops from the ship's current system to `target` (BFS over the same
        // parity-filtered adjacency the walk uses). 0 if it IS the current system, -1 if unreachable through
        // explored links. Cheap — the explored graph is small.
        internal static int JumpsFromCurrent(SectorMapObjectEntity target)
        {
            var start = Ctrl?.CurrentStarSystem;
            if (start == null || target == null) return -1;
            if (start == target) return 0;
            var seen = new HashSet<string> { start.UniqueId };
            var queue = new Queue<(SectorMapObjectEntity sys, int dist)>();
            queue.Enqueue((start, 0));
            while (queue.Count > 0)
            {
                var (sys, dist) = queue.Dequeue();
                foreach (var n in ExploredNeighbors(sys))
                {
                    if (!seen.Add(n.system.UniqueId)) continue;
                    if (n.system == target) return dist + 1;
                    queue.Enqueue((n.system, dist + 1));
                }
            }
            return -1;
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

        // Compass bearing from the ship to the system, e.g. "north-east". No numeric distance: on a passage-based
        // map the straight-line distance is misleading (a near system can be a longer warp), so only the direction
        // is spoken (user call 2026-07-14). Empty at/near the current system. Map plane is XZ (like the system map).
        private static string Bearing(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            if (Exploration.Geo.Distance(from, to) <= 0.5f) return "";
            return Exploration.Geo.CompassSector(dx, dz, out int sector)
                ? Loc.T(Accessibility.InteractableDescriber.Compass8[sector]) : "";
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
                bool hasRoute = passage != null && passage.IsExplored;
                bool canVisit = isCurrent && view.StarSystemToTransit != null;
                string name = view.Name;

                var rows = new List<ChoiceSubmenuScreen.Row>();

                // Travel an existing explored route.
                if (hasRoute)
                    rows.Add(ChoiceSubmenuScreen.Row.Action(
                        () => Loc.T("sectormap.verb_travel", new { name }), () => DoTravel(view, overtip, name)));

                // Visit the current system's own area.
                if (canVisit)
                    rows.Add(ChoiceSubmenuScreen.Row.Action(
                        () => Loc.T("sectormap.verb_visit", new { name }), () => DoVisit(view, overtip, name)));

                // Make an existing route safer — one row per REACHABLE safer tier, mirroring the sighted popup's
                // up-to-3 upgrade buttons (SpaceSystemNavigationButtonsBaseView.CheckUpgradeButtonsVisible): target
                // tier T in [Safe .. CurrentDifficulty-1], cost = (current - T) * per-tier, enabled iff affordable.
                if (hasRoute && passage.CurrentDifficulty > SectorMapPassageEntity.PassageDifficulty.Safe)
                {
                    var p = passage;
                    for (int t = 0; t < (int)p.CurrentDifficulty && t < 3; t++)
                    {
                        var target = (SectorMapPassageEntity.PassageDifficulty)t; // fresh per iteration (closure-safe)
                        rows.Add(ChoiceSubmenuScreen.Row.Action(
                            () => UpgradeLabel(name, p, target),
                            () => DoUpgradeRoute(entity, p, target, name),
                            () => NavResource >= UpgradeCostTo(p, target)));
                    }
                }

                // Create a new route to a non-current, un-routed system. Mirrors the sighted "Create way" button:
                // enabled only when IsScannedFrom && IsAvailable, else a DISABLED "scan required" row (the game's
                // own ScanRequired hint). Cost = WarpTravelState.CreateNewPassageCost.
                if (current != null && !isCurrent && !hasRoute)
                {
                    var from = current;
                    var to = entity;
                    // The game gates Create Way on the CURRENT system's scan flag (have I scanned from where I am),
                    // NOT the destination's — see SpaceSystemNavigationButtonsVM.CheckEverything /
                    // SpaceSystemNavigationButtonsBaseView.BindViewImplementation. IsAvailable is the target's.
                    rows.Add(ChoiceSubmenuScreen.Row.Action(
                        () => CreateLabel(name),
                        () => DoCreateRoute(from, to, name),
                        () => from.IsScannedFrom && to.IsAvailable));
                }

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

        // ---- warp-route shop (Navigator's Resource): create / make-safer, driven through the game's own
        // networked GameCommandQueue — the exact path the sighted Create/Upgrade popup buttons use. Both commands
        // are async (queued, run over later frames); WarpEvents speaks the charted / safer line on completion. ----

        private static int NavResource => Game.Instance?.Player?.WarpTravelState?.NavigatorResource ?? 0;
        private static int CreateCost => Game.Instance?.Player?.WarpTravelState?.CreateNewPassageCost ?? 0;
        private static int UpgradeCost
            => Kingmaker.Blueprints.Root.BlueprintWarhammerRoot.Instance?.WarpRoutesSettings?.LowerPassageDifficultyCost ?? 0;

        // "Create route to X, costs N" when scanned + affordable; "…scan required" when unscanned (the row is
        // disabled, so the announcer adds "disabled" — the game's ScanRequired hint); "…not enough" when scanned
        // but you can't pay (still enabled, mirroring the sighted button that stays clickable and warns).
        private static string CreateLabel(string name)
        {
            // "Scan required" keys off the CURRENT system's scan flag (matching the sighted button hint), not the target.
            if (Ctrl?.CurrentStarSystem?.IsScannedFrom != true) return Loc.T("sectormap.verb_create_scan", new { name });
            int cost = CreateCost;
            return NavResource >= cost
                ? Loc.T("sectormap.verb_create", new { name, cost })
                : Loc.T("sectormap.verb_create_poor", new { name, cost });
        }

        // Cost to lower a passage to a target tier — (current - target) * per-tier cost, matching the game's own
        // SectorMapController.LowerPassageDifficulty.
        private static int UpgradeCostTo(SectorMapPassageEntity passage, SectorMapPassageEntity.PassageDifficulty target)
            => ((int)passage.CurrentDifficulty - (int)target) * UpgradeCost;

        private static string UpgradeLabel(string name, SectorMapPassageEntity passage,
            SectorMapPassageEntity.PassageDifficulty target)
        {
            int cost = UpgradeCostTo(passage, target);
            string tier = TierWord(target);
            return NavResource >= cost
                ? Loc.T("sectormap.verb_upgrade", new { name, tier, cost })
                : Loc.T("sectormap.verb_upgrade_poor", new { name, tier, cost });
        }

        // The bare target-tier adjective (safe / unsafe / dangerous). Target is always < CurrentDifficulty, so Deadly
        // never appears here.
        private static string TierWord(SectorMapPassageEntity.PassageDifficulty d)
        {
            switch (d)
            {
                case SectorMapPassageEntity.PassageDifficulty.Safe: return Loc.T("sectormap.tier_safe");
                case SectorMapPassageEntity.PassageDifficulty.Unsafe: return Loc.T("sectormap.tier_unsafe");
                case SectorMapPassageEntity.PassageDifficulty.Dangerous: return Loc.T("sectormap.tier_dangerous");
                default: return Loc.T("sectormap.tier_unsafe");
            }
        }

        private static void DoCreateRoute(SectorMapObjectEntity from, SectorMapObjectEntity to, string name)
        {
            if (!Interactive) { Tts.Speak(Loc.T("sectormap.not_now"), interrupt: true); return; }
            if (!from.IsScannedFrom) { Tts.Speak(Loc.T("sectormap.verb_create_scan", new { name }), interrupt: true); return; }
            if (NavResource < CreateCost) { Tts.Speak(Loc.T("sectormap.no_resource"), interrupt: true); return; }
            try
            {
                Game.Instance.GameCommandQueue.CreateNewWarpRoute(from, to);
                Tts.Speak(Loc.T("sectormap.verb_create_go", new { name }), interrupt: true);
            }
            catch (Exception e) { Main.Log?.Error("SectorMapScreen.DoCreateRoute failed: " + e); }
        }

        private static void DoUpgradeRoute(SectorMapObjectEntity to, SectorMapPassageEntity passage,
            SectorMapPassageEntity.PassageDifficulty target, string name)
        {
            if (!Interactive) { Tts.Speak(Loc.T("sectormap.not_now"), interrupt: true); return; }
            if (target >= passage.CurrentDifficulty) return; // not actually safer than the current tier
            if (NavResource < UpgradeCostTo(passage, target)) { Tts.Speak(Loc.T("sectormap.no_resource"), interrupt: true); return; }
            try
            {
                Game.Instance.GameCommandQueue.LowerWarpRouteDifficulty(to, target);
                Tts.Speak(Loc.T("sectormap.verb_upgrade_go", new { name }), interrupt: true);
            }
            catch (Exception e) { Main.Log?.Error("SectorMapScreen.DoUpgradeRoute failed: " + e); }
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
