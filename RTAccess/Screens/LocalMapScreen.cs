using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;                                  // UIStrings (the window's own button hints)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                           // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap;                  // LocalMapVM, LocalMapLegendBlockItemVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Markers;          // LocalMap*MarkerVM
using Kingmaker.EntitySystem.Entities;                                    // BaseUnitEntity
using RTAccess.Accessibility;                                             // InteractableDescriber
using RTAccess.Exploration;                                               // Scanner (the shared travel verb)
using RTAccess.UI;
using RTAccess.UI.Graph;
using UnityEngine;

namespace RTAccess.Screens
{
    /// <summary>
    /// The Local Map service window (<see cref="LocalMapVM"/>) as a navigable list of what the map shows.
    ///
    /// The sighted window is a baked top-down picture of the area part (<c>WarhammerLocalMapRenderer</c>, 5 px
    /// per world unit, fog-masked in the shader) with pins laid over it, and its only load-bearing verb is
    /// right-click → <c>UnitCommandsRunner.MoveSelectedUnitsToPoint</c>; left-click merely scrolls the camera,
    /// and the pins themselves are not clickable (no marker view handles a click). So the accessible mirror is
    /// the PIN SET plus that one verb — the image itself carries no text to read, and the mod already models
    /// area layout far better through the scanner, the room map and the tile cursor.
    ///
    /// Everything is read off <see cref="LocalMapVM.MarkersVm"/> — the game's own composed, already-gated
    /// marker collection — rather than re-deriving it from <c>LocalMapModel.Markers</c>. That is both the
    /// "read the game's own VM" law and free visual parity: <c>LocalMapMarkerPart.IsVisible()</c> is
    /// <c>!Hidden &amp;&amp; IsRevealed &amp;&amp; IsInGame &amp;&amp; IsAwarenessCheckPassed</c>, hostile pins are gated on
    /// <c>IsVisibleForPlayer</c> and dropped live by <c>LocalMapVM.OnUpdateHandler</c>, and party pins are the
    /// party. Nothing here can leak what a sighted player can't see, so no extra <c>FogProbe</c> gate is needed.
    ///
    /// Zones (one <c>BeginStop</c> each, Tab cycles): overview, party, landmarks, hostiles, legend, actions.
    /// ScreenName is null — <c>ServiceWindowAnnounce</c> already speaks "Local Map" from
    /// <see cref="ServiceWindowInfo"/>.
    ///
    /// Note: <c>LocalMapBaseView.AddLocalMapMarker</c> rewrites a pin's <c>MarkerType</c> from <c>Poi</c> to
    /// <c>VeryImportantThing</c> as a side effect of picking its prefab, and the game's view is alive beneath
    /// us — so a POI may already read as "important". That is what the sighted player's icon shows, so the
    /// type is passed through as-is rather than second-guessed.
    /// </summary>
    public sealed class LocalMapScreen : Screen
    {
        public override string Key => "service.LocalMap";
        public override int Layer => 10;
        public override string ScreenName => null; // ServiceWindowAnnounce speaks "Local Map"

        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.LocalMap;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        // ---- VM access (Surface OR Space, like every other service window) ----
        private static ServiceWindowsVM ServiceWindows() => UiContexts.ServiceWindows();

        private static LocalMapVM Vm() => UiContexts.LocalMap();

        /// <summary>Where distances and bearings are measured from — the same origin the scanner uses (the
        /// selected character), falling back to the Rogue Trader so the readout still works with no selection.</summary>
        private static BaseUnitEntity Origin()
            => Game.Instance?.SelectionCharacter?.SelectedUnit?.Value
               ?? Game.Instance?.Player?.MainCharacterEntity;

        // Only pins the sighted map is actually drawing. IsVisible is the game's own per-pin gate and is kept
        // live by each marker VM's OnUpdateHandler, so this is a straight pass-through.
        private static IEnumerable<T> Shown<T>(LocalMapVM vm) where T : LocalMapMarkerVM
            => vm.MarkersVm.OfType<T>().Where(m => m != null && m.IsVisible.Value);

        // ---- build (immediate mode) ----

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "localmap:" + vm.GetHashCode() + ":"; // re-keys on a window swap → focus re-homes
            var origin = Origin();
            Vector3 from = origin != null ? Geo.Live(origin) : Vector3.zero;

            BuildOverview(b, vm, k);
            BuildParty(b, vm, k, origin, from);
            BuildLandmarks(b, vm, k, from);
            BuildHostiles(b, vm, k, from);
            BuildLegend(b, vm, k);
            BuildActions(b, vm, k);
        }

        // The window's title block: the area name the map is showing, plus how much is pinned on it.
        private static void BuildOverview(GraphBuilder b, LocalMapVM vm, string k)
        {
            b.BeginStop("overview");
            b.AddItem(ControlId.Structural(k + "area"), GraphNodes.Text(() =>
            {
                var title = vm.Title?.Value;
                return string.IsNullOrWhiteSpace(title) ? Loc.T("screen.map") : title;
            }));
            b.AddItem(ControlId.Structural(k + "counts"), GraphNodes.Text(() => Loc.T("localmap.counts", new
            {
                landmarks = Shown<LocalMapCommonMarkerVM>(vm).Count(),
                hostiles = Shown<LocalMapUnitMarkerVM>(vm).Count(),
            })));
        }

        // The party pins. Each row reads the character, whether they're the selected one, where they are
        // relative to you, and — the map's second pin per character — where they are currently headed.
        private static void BuildParty(GraphBuilder b, LocalMapVM vm, string k, BaseUnitEntity origin, Vector3 from)
        {
            b.BeginStop("party").PushContext(Loc.T("localmap.party"), Loc.T("role.list"));
            bool any = false;
            foreach (var m in Shown<LocalMapCharacterMarkerVM>(vm))
            {
                var marker = m;
                b.AddItem(ControlId.Referenced(marker, k + "party:" + (marker.m_Unit?.UniqueId ?? "?")),
                    TravelNode(() => PartyLabel(vm, marker, origin, from), () => marker.Position.Value,
                        () => marker.Description.Value));
                any = true;
            }
            if (!any) b.AddItem(ControlId.Structural(k + "party:none"),
                GraphNodes.Text(() => Loc.T("localmap.no_party")));
            b.PopContext();
        }

        private static string PartyLabel(LocalMapVM vm, LocalMapCharacterMarkerVM m,
            BaseUnitEntity origin, Vector3 from)
        {
            var parts = new List<string>();
            var name = m.Description?.Value;
            if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
            if (m.IsSelected.Value) parts.Add(Loc.T("state.selected"));
            // Their own position — skipped for the character we are measuring FROM ("0 tiles from yourself").
            if (m.m_Unit != origin)
            {
                var where = InteractableDescriber.DirectionAndDistance(from, m.Position.Value);
                if (!string.IsNullOrWhiteSpace(where)) parts.Add(where);
            }
            // The destination pin the map draws for a character with a pending move order.
            var dest = Destination(vm, m.m_Unit);
            if (dest.HasValue)
                parts.Add(Loc.T("localmap.heading", new
                {
                    where = InteractableDescriber.DirectionAndDistance(from, dest.Value),
                }));
            return string.Join(", ", parts);
        }

        // A character's pending move destination, if the map is drawing one for them. The game keeps one
        // LocalMapDestinationMarkerVM per party member and flips its IsVisible from
        // ClickPointerManager.UnitMarksLocalMap, so an invisible one simply means "not going anywhere".
        private static Vector3? Destination(LocalMapVM vm, BaseUnitEntity unit)
        {
            if (unit == null) return null;
            foreach (var d in Shown<LocalMapDestinationMarkerVM>(vm))
                if (d.m_Unit == unit) return d.Position.Value;
            return null;
        }

        // The landmark pins — objectives, points of interest, loot caches, exits — nearest first.
        private static void BuildLandmarks(GraphBuilder b, LocalMapVM vm, string k, Vector3 from)
        {
            b.BeginStop("landmarks").PushContext(Loc.T("localmap.landmarks"), Loc.T("role.list"));
            var shown = Shown<LocalMapCommonMarkerVM>(vm)
                .OrderBy(m => (m.Position.Value - from).sqrMagnitude)
                .ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                var m = shown[i];
                b.AddItem(ControlId.Referenced(m, k + "mark:" + i),
                    TravelNode(() => MarkerLabel(m, from), () => m.Position.Value,
                        () => m.Description.Value));
            }
            if (shown.Count == 0) b.AddItem(ControlId.Structural(k + "mark:none"),
                GraphNodes.Text(() => Loc.T("localmap.no_landmarks")));
            b.PopContext();
        }

        // "<description>, <type>, <distance>, <bearing>" — the same shape InteractableDescriber.DescribeMarker
        // gives the scanner's landmark cycle, so the two surfaces phrase a pin identically.
        private static string MarkerLabel(LocalMapMarkerVM m, Vector3 from)
        {
            var parts = new List<string>();
            var desc = m.Description?.Value;
            if (!string.IsNullOrWhiteSpace(desc)) parts.Add(desc);
            var type = InteractableDescriber.MarkerTypeLabel(m.MarkerType);
            if (type != null) parts.Add(type);
            var where = InteractableDescriber.DirectionAndDistance(from, m.Position.Value);
            if (!string.IsNullOrWhiteSpace(where)) parts.Add(where);
            return string.Join(", ", parts);
        }

        // The hostile pins: units the party has actually noticed AND can currently see (the game gates these on
        // CombatGroup.Memory + IsVisibleForPlayer and removes them the frame that goes false).
        private static void BuildHostiles(GraphBuilder b, LocalMapVM vm, string k, Vector3 from)
        {
            b.BeginStop("hostiles").PushContext(Loc.T("localmap.hostiles"), Loc.T("role.list"));
            var shown = Shown<LocalMapUnitMarkerVM>(vm)
                .OrderBy(m => (m.Position.Value - from).sqrMagnitude)
                .ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                var m = shown[i];
                b.AddItem(ControlId.Referenced(m, k + "unit:" + i),
                    TravelNode(() => HostileLabel(m, from), () => m.Position.Value,
                        () => m.Description.Value));
            }
            if (shown.Count == 0) b.AddItem(ControlId.Structural(k + "unit:none"),
                GraphNodes.Text(() => Loc.T("localmap.no_hostiles")));
            b.PopContext();
        }

        private static string HostileLabel(LocalMapUnitMarkerVM m, Vector3 from)
        {
            var parts = new List<string>();
            var name = m.Description?.Value;
            if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
            parts.Add(Loc.T(m.IsEnemy.Value ? "scan.faction.enemy" : "scan.faction.neutral"));
            var where = InteractableDescriber.DirectionAndDistance(from, m.Position.Value);
            if (!string.IsNullOrWhiteSpace(where)) parts.Add(where);
            return string.Join(", ", parts);
        }

        // The legend the map's hover panel shows (BlueprintUILocalMapLegend) — "this icon means loot". The
        // sprite is meaningless to us; the authored description is the whole content.
        private static void BuildLegend(GraphBuilder b, LocalMapVM vm, string k)
        {
            var items = vm.LocalMapLegendBlockVM?.LocalMapItemsVMs;
            if (items == null || items.Count == 0) return;
            b.BeginStop("legend").PushContext(
                GameText.Or(() => UIStrings.Instance.LocalMapTexts.ShowLegend, "localmap.legend"),
                Loc.T("role.list"));
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) continue;
                b.AddItem(ControlId.Structural(k + "legend:" + i), GraphNodes.Text(() => it.ItemLabel ?? ""));
            }
            b.PopContext();
        }

        // The window's own buttons that mean anything without a picture. Zoom in/out is pure presentation of a
        // texture we never render, so it is dropped rather than faked; centring the camera is real (it is what
        // the game's own button does, and it moves the camera the rest of the HUD reads from).
        private static void BuildActions(GraphBuilder b, LocalMapVM vm, string k)
        {
            b.BeginStop("actions").PushContext(Loc.T("localmap.actions"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(k + "centre"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.LocalMapTexts.CenterOnRogueTrader, "localmap.centre"),
                () => vm.ScrollCameraToRogueTrader()));
            b.AddItem(ControlId.Structural(k + "close"), GraphNodes.Button(
                () => GameText.Action("close"),
                () => ServiceWindows()?.HandleCloseAll()));
            b.PopContext();
        }

        // Every pin row carries the map's one real verb: order the selected units to that point — exactly what
        // LocalMapVM.OnClick does on a right-click. Routed through the scanner's travel helper so the combat
        // guard, the walkable snap and the spoken confirmation match the landmark cycle's I key.
        private static NodeVtable TravelNode(System.Func<string> label, System.Func<Vector3> position,
            System.Func<string> name)
        {
            var vt = GraphNodes.Button(label, () => Scanner.TravelToPoint(position(), name()));
            vt.SearchText = label;
            return vt;
        }
    }
}
