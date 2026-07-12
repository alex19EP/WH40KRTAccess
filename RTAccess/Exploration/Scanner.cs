using Kingmaker;                       // Game
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Utils; // LocalMapModel, ILocalMapMarker, LocalMapMarkType
using Kingmaker.Controllers.Units;     // UnitCommandsRunner (landmark travel)
using Kingmaker.EntitySystem;          // DistanceToInCells (EntityHelper ext)
using Kingmaker.EntitySystem.Entities; // BaseUnitEntity
using Kingmaker.UnitLogic;             // IsThreat (AttackOfOpportunityHelper ext)
using RTAccess.Accessibility;          // InteractableDescriber, CombatReads
using RTAccess.Speech;                 // Speaker
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// The scanner / review cursor: a keyboard-driven, categorized, distance-sorted browse of everything in the
/// current area (units + interactable map objects), plus tactical "nearest party / enemy / neutral / object"
/// review cycles. Its selection is a look-without-moving cursor — I interacts with it (and never moves your
/// position), falling back to the object at the tile cursor when the selection isn't itself an actionable object,
/// so the same key activates any object the same way; walking the party to a tile is the tile cursor's job
/// (Backspace; see TileExplorer). Both interact keys drive the game's own object activation
/// (<see cref="ProxyMapObject.Interact"/>). Distances and
/// bearings are relative to the selected (or lead) unit and
/// are spoken via <see cref="InteractableDescriber"/> so the compass matches the other navigators.
///
/// Lists rebuild on every key from the live <see cref="WorldModel.Items"/> registry (kept fresh each frame by
/// <see cref="WorldModel.Tick"/>) and the user's selection is tracked by the backing entity so it survives the rebuild. Its actions are registered in the
/// <see cref="RTAccess.Input.InputCategory.Exploration"/> category (driven by <see cref="RTAccess.Input.InputManager"/>
/// and the dev harness's /input), so they are live only while the in-game screen has world control — dead in
/// windows/dialogue/cutscenes — and work in exploration AND surface tactical combat.
///
/// Landmarks (area exits and points of interest) are the local-map markers: exits are surfaced as their real
/// (activatable) world objects in the Exits category, and the marker-only pins (objective / point of interest /
/// important / loot) live in the "Points of interest" category — both browsed like every other category, with no
/// dedicated cycle keys. A landmark isn't a reach-interactable (the game's map pin isn't clickable — verified), so
/// I on one WALKS the party toward it (the only thing a landmark supports).
///
/// Keys: PageUp/Down = previous/next item; Ctrl+PageUp/Down = previous/next category; Comma/Period/N/M = cycle
/// nearest party/enemy/neutral/object of interest (Shift reverses). Live area effects (hazards + buff zones) have no
/// dedicated cycle key — they browse as the Hazards / Buff zones categories in the Ctrl+PageUp/Down list, and the
/// tile explorer names the hazard on the cursor tile. I = interact with selection (an object; a landmark → walk to
/// it; otherwise the object at the cursor); O = re-announce the current
/// selection; Home/Slash = plant the movement cursor on the selection; X = where am I; P = party readout. ' / Y
/// inspect the cursor / the selection (see <see cref="Inspect"/>). V / Shift+V = cycle the current room's ways
/// out — doors, area transitions, and uncovered geometric openings, merged into one distance-sorted review that
/// drives the shared selection (see <see cref="CycleExit"/>).
/// </summary>
internal static class Scanner
{
    // Where a browse category's items come from: most filter the WorldModel registry by a taxonomy predicate;
    // two are special-sourced lists with no backing registry entity (see MarkerList / FrontierList).
    private enum Source { Registry, Markers, Frontier }

    // The browse categories cycled by Ctrl+PageUp/Down. Most filter the WorldModel registry by a taxonomy predicate;
    // the "points of interest" category is instead marker-sourced — the area-wide local-map pins
    // (objective / POI / important / loot) that have no interaction part to bin on — and the "unexplored space"
    // category is frontier-sourced (fog-edge openings; see FrontierModel). Area exits appear as their real
    // world objects under "taxonomy.exits" (activatable), so there is no separate marker-exits category.
    private static readonly (string Key, Source Src, Func<ScanItem, bool> Pred)[] Categories =
    {
        ("taxonomy.units.party",    Source.Registry, it => it.Primary == ScanTaxonomy.UnitsParty),
        ("taxonomy.units.enemies",  Source.Registry, it => it.Primary == ScanTaxonomy.UnitsEnemies),
        ("taxonomy.units.neutrals", Source.Registry, it => it.Primary == ScanTaxonomy.UnitsNeutrals),
        ("taxonomy.containers",     Source.Registry, it => it.HasNode(ScanTaxonomy.Containers)),
        ("taxonomy.corpses",        Source.Registry, it => it.HasNode(ScanTaxonomy.Corpses)),   // dead-with-loot bodies (I loots)
        ("taxonomy.doors",          Source.Registry, it => it.HasNode(ScanTaxonomy.Doors)),
        ("taxonomy.exits",          Source.Registry, it => it.HasNode(ScanTaxonomy.Exits)),
        ("taxonomy.poi",            Source.Markers,  null),   // area-wide local-map landmark pins (travel-to; see MarkerList)
        ("taxonomy.searchpoints",   Source.Registry, it => it.HasNode(ScanTaxonomy.SearchPoints)),
        ("taxonomy.traps",          Source.Registry, it => it.HasNode(ScanTaxonomy.Traps)),
        ("taxonomy.mechanisms",     Source.Registry, it => it.HasNode(ScanTaxonomy.Mechanisms)),
        // Attackable destructible scenery (fuel tanks / valves / destructible walls) — what a sighted player
        // shoots to open a path (e.g. a fire-trap escape). Arm an attack, browse here, I fires on the selection.
        ("taxonomy.destructibles",  Source.Registry, it => it.HasNode(ScanTaxonomy.Destructibles)),
        // No "scenery" category: a map object with no live interaction / exit / marker is no longer scannable
        // (see ProxyMapObject.IsScannable) and NodeSet has no Scenery fallback, so nothing produces that node.
        // Real interactions (incl. bark/examine volumes) land in their own bucket — bark → Mechanisms.
        ("taxonomy.hazards",        Source.Registry, it => it.HasNode(ScanTaxonomy.Hazards)),
        ("taxonomy.buffzones",      Source.Registry, it => it.HasNode(ScanTaxonomy.BuffZones)),
        // Last so the established category order is untouched: fog-edge openings where exploration can continue.
        ("taxonomy.unexplored",     Source.Frontier, null),
    };

    // Party/Enemies/Neutrals/Objects come from the WorldModel snapshot (units + reachable interactables). Area
    // effects (hazards + buff zones) are NOT a review group — they browse as the Hazards / Buff zones categories in
    // the Ctrl+PageUp/Down list, and the tile explorer names the hazard on the cursor tile. (Landmarks likewise live
    // only in the category browse.)
    private enum Group { Party, Enemies, Neutrals, Objects }

    private static int _categoryIndex;     // index into Categories (Ctrl+PageUp/Down)
    private static object _selectedKey;     // the backing entity of the current selection (survives rebuilds)

    // ---- registered action entry points (InputCategory.Exploration; see InputBindings.RegisterDefaults) ----
    // Each is wired to an InputAction so the dev harness /input can drive it and the framework's chord shadowing
    // decides HUD-vs-exploration ownership of the shared Home chord (vs ui.home). The old manual
    // `ExplorationActive && !Navigation.HasFocus` gate is now the Exploration category's liveness: it is live
    // only while the in-game screen has world control (see ControlState), so the scanner goes dead in
    // windows/dialogue/cutscenes automatically. The read-only browse chords (PageUp/Down, comma/period/N/M, X, P,
    // and the inspect ' / Y) work whether or not the HUD is focused; Home yields to ui.home when the HUD is
    // focused (chord shadowing). InteractSelected (I) mutates the world, so it self-guards on Navigation.HasFocus
    // (it has no UI twin to shadow it).
    internal static void ItemPrev() => Safe(() => StepItem(-1));
    internal static void ItemNext() => Safe(() => StepItem(1));
    internal static void CategoryPrev() => Safe(() => StepCategory(-1));
    internal static void CategoryNext() => Safe(() => StepCategory(1));
    internal static void ReviewParty(bool back) => Safe(() => Review(Group.Party, back ? -1 : 1));
    internal static void ReviewEnemies(bool back) => Safe(() => Review(Group.Enemies, back ? -1 : 1));
    internal static void ReviewNeutrals(bool back) => Safe(() => Review(Group.Neutrals, back ? -1 : 1));
    internal static void ReviewObjects(bool back) => Safe(() => Review(Group.Objects, back ? -1 : 1));
    internal static void ExitNext() => Safe(() => CycleExit(1));
    internal static void ExitPrev() => Safe(() => CycleExit(-1));
    internal static void InteractSelected() => Safe(() =>
    {
        // While an ability is armed, I commits the aim on the review selection instead of interacting (see Targeting).
        if (Targeting.Aiming) { Targeting.CommitOnSelection(ResolveSelected()); return; }
        if (RTAccess.UI.Navigation.HasFocus) return;
        Interact();
    });
    internal static void CursorToSelection() => Safe(PlantCursorOnSelection);
    internal static void WhereAmINow() => Safe(WhereAmI);
    internal static void ReadParty() => Safe(PartyReadout);
    // Re-speak the current selection from the live cursor origin (any group — unit, object, or landmark), so the
    // player can recover what they last cycled without stepping the list. Resolves through ResolveSelected (which
    // is marker-aware), so it works on a landmark (points-of-interest) selection too; drops the "N of M" ordinal.
    internal static void AnnounceSelection() => Safe(ReSpeakSelection);
    // Battlefield summary (C5): one aggregate sentence — enemy/ally counts, and in combat how many enemies the
    // acting unit can reach and how many threaten it, plus the nearest enemy's range. The whole-board glance a
    // sighted player gets from the initiative tracker + overtips at once, without stepping the review cycle.
    internal static void BattlefieldSummary() => Safe(Summarize);

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Main.Log?.Error("Scanner failed: " + e); }
    }

    // ---- browsing ----

    private static void StepItem(int dir)
    {
        var anchor = Anchor();
        if (anchor == null) { Speak(Loc.T("status.no_selection")); return; }
        var refPos = ScanFrom();

        var list = CategoryList(_categoryIndex, refPos);
        if (list.Count == 0) { _selectedKey = null; Speak(Loc.T("scan.category_empty", new { label = CategoryLabel })); return; }

        int idx = IndexOfSelected(list);
        idx = idx < 0 ? 0 : Wrap(idx + dir, list.Count);
        Select(list, idx, refPos);
    }

    private static void StepCategory(int dir)
    {
        var anchor = Anchor();
        if (anchor == null) { Speak(Loc.T("status.no_selection")); return; }
        var refPos = ScanFrom();

        // Skip empty categories: land on the next category (in the step direction) that currently has
        // something to browse, so the player never cycles onto a dead "…, empty" stop (mirrors WrathAccess's
        // NextCategoryIndex). When NOTHING in the area populates any category, stay put and say so.
        int next = NextNonEmptyCategory(_categoryIndex, dir, refPos);
        if (next < 0) { _selectedKey = null; Speak(Loc.T("scan.nothing_to_scan")); return; }

        _categoryIndex = next;
        var list = CategoryList(_categoryIndex, refPos);
        Select(list, 0, refPos, CategoryLabel + ", " + list.Count + ". ");
    }

    /// <summary>The index of the next category (from <paramref name="from"/>, stepping by <paramref name="dir"/>)
    /// that currently holds at least one item, or -1 when every category is empty. Scans at most one full loop, so
    /// it always terminates. Category lists are cheap to rebuild (a single pass over the live registry), so we probe
    /// them directly rather than caching counts.</summary>
    private static int NextNonEmptyCategory(int from, int dir, Vector3 refPos)
    {
        for (int step = 1; step <= Categories.Length; step++)
        {
            int i = Wrap(from + dir * step, Categories.Length);
            if (CategoryList(i, refPos).Count > 0) return i;
        }
        return -1;
    }

    private static void Review(Group group, int dir)
    {
        var anchor = Anchor();
        if (anchor == null) { Speak(Loc.T("status.no_selection")); return; }
        var refPos = ScanFrom();

        var list = GroupList(group, refPos);
        if (list.Count == 0) { Speak(Loc.T("scan.none_in_sight", new { label = GroupLabel(group) })); return; }

        int idx = IndexOfSelected(list);
        idx = idx < 0 ? (dir >= 0 ? 0 : list.Count - 1) : Wrap(idx + dir, list.Count);
        Select(list, idx, refPos);
    }

    // ---- actions on the selection ----

    // I is the review-selection half of the interact pair; Enter is the tile-cursor half (see TileExplorer). Both
    // now funnel through ONE activation path (Activation / TryActivateSelection), so their capability is symmetric —
    // whatever one key can activate, the other can too. They differ only in ORDER: I tries the review selection
    // first, then the tile-cursor object; Enter the reverse. Every branch drives the SAME in-game activation
    // (ProxyMapObject.Interact → area-transition / variative / ClickMapObjectHandler), so it never dead-ends.
    private static void Interact()
    {
        var sel = ResolveSelected();

        // A landmark (local-map pin) isn't a reach-interactable — the only thing it supports is walking to it.
        if (sel is ProxyMarker) { TravelTo(sel); return; }

        // 1) The review selection itself, when it's an actionable object. NO same-area/navmesh pre-guard: the
        //    game's own Interact (ApproachAndInteract) walks a unit to the object and handles reachability itself,
        //    and the selection is always in the current area (a cross-area key resolves to null in ResolveSelected).
        //    The old Geo.SameArea guard compared navmesh CONNECTED COMPONENTS, so it wrongly refused same-area
        //    objects whose position snaps to a disconnected island the party can't stand ON — a pedestal
        //    (PostamentsObsidian), an object behind a low wall, an elevated prop — with a bogus "Can't reach". The
        //    tile cursor's Enter never had this guard, which is exactly why it interacted those objects fine.
        if (sel != null && sel.CanInteract)
        {
            if (sel.Interact()) { Activation.SpeakOutcome(true, sel.Name); return; }
            // The selection reported actionable but its OWN interaction didn't fire — a co-located decorative /
            // proxy object, a restriction, or the wrong actor picked up. Don't dead-end on "can't interact":
            // fall through to the proximity resolve at its TILE — exactly the "plant the cursor on it, then
            // Enter" the player was doing by hand (which is why that workaround succeeds where a bare I did not).
        }

        // 2) The interactable object(s) co-located with the selection (its tile) — or, with no usable selection,
        //    the movement cursor / anchor tile. Proximity resolve; pops a chooser when several share reach.
        Vector3? origin = sel?.Position;
        if (origin == null)
        {
            var node = MapCursor.Node ?? Anchor()?.CurrentUnwalkableNode;
            if (node != null) origin = (Vector3)node.position;
        }
        if (origin is Vector3 o && Activation.TryCursorObject(o)) return;

        Speak(Loc.T("scan.nothing_nearby"));
    }

    /// <summary>
    /// Selection-tier activation, shared with the tile cursor's Enter (<see cref="TileExplorer.InteractAtCursor"/>)
    /// so both interact keys reach the same targets. An actionable review selection → interact through the game's
    /// own click path — distance-agnostic (you can act on a cycled object across the room); reachability is left to
    /// the game's own approach-and-interact rather than a pre-guard, so a same-area object on a disconnected navmesh
    /// island (a pedestal, an elevated prop) is no longer wrongly refused. A landmark
    /// → walk the party toward it (the local-map pin isn't clickable; that is all it supports). Returns true when it
    /// handled the press, false when there is no selection to act on (null / a unit / a non-actionable object) so the
    /// caller falls back to the tile cursor's object.
    /// </summary>
    internal static bool TryActivateSelection()
    {
        var sel = ResolveSelected();
        if (sel != null && sel.CanInteract)
        {
            // No same-area pre-guard (see Interact): the game's Interact handles approach/reachability, and the
            // Geo.SameArea navmesh-component test wrongly refused same-area objects on a disconnected island.
            Activation.SpeakOutcome(sel.Interact(), sel.Name);
            return true;
        }
        if (sel is ProxyMarker) { TravelTo(sel); return true; }
        return false;
    }

    /// <summary>Walk the party toward a landmark — the only action a local-map pin supports. Off-mesh pins (far
    /// exits, floating markers) would make the pathfinder drop a direct move, so it heads as far toward the pin as
    /// continuous walkable floor allows (<see cref="Geo.SnapToWalkable"/>) and issues the game's own formation move.
    /// Refused in combat (travelling across the area mid-fight makes no sense — mirrors the old landmark walk gate).</summary>
    private static void TravelTo(ScanItem landmark)
    {
        if (Game.Instance?.Player?.IsInCombat == true) { Speak(Loc.T("travel.combat")); return; }
        var self = Anchor();
        if (self == null) { Speak(Loc.T("status.no_selection")); return; }

        var from = Geo.Live(self);
        var dest = Geo.SnapToWalkable(landmark.Position, from);
        if (Geo.Distance(from, dest) < 1.5f) { Speak(Loc.T("landmark.cant_head")); return; }
        UnitCommandsRunner.MoveSelectedUnitsToPoint(dest);
        Speak(Loc.T("landmark.walking_to", new { dest = landmark.Name }));
    }

    /// <summary>Re-speak the resolved selection (any group) from the current scan origin — the O key. While aiming
    /// an attack at this unit it also appends the FULL hit breakdown (base hit, each avoidance, damage, per-shot
    /// burst), so O is "tell me more about this shot" versus the terse line the cycle gives.</summary>
    private static void ReSpeakSelection()
    {
        var item = ResolveSelected();
        if (item == null) { Speak(Loc.T("scan.no_selection")); return; }
        var line = item.Describe(ScanFrom());
        var pred = Targeting.PredictLine(item, verbose: true);
        if (!string.IsNullOrEmpty(pred)) line += ". " + pred;
        Speak(line);
    }

    /// <summary>Home/Slash: plant the shared cursor on the current review selection's tile — the coupling core.
    /// The movement cursor follows the selection on demand; the selection itself (<see cref="_selectedKey"/>) is
    /// unchanged. The tile readout + camera-follow are the tile explorer's (<see cref="TileExplorer.PlantOn"/>).</summary>
    private static void PlantCursorOnSelection()
    {
        var item = ResolveSelected();
        if (item == null) { Speak(Loc.T("scan.no_selection_plant")); return; }
        TileExplorer.PlantOn(item.Position);
    }

    private static void WhereAmI()
    {
        var parts = new List<string>();
        var area = Game.Instance?.CurrentlyLoadedArea;
        var name = area != null ? TextUtil.StripRichText(area.AreaDisplayName) : null;
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
        if (IsIndoors()) parts.Add(Loc.T("where.indoors"));

        var anchor = Anchor();
        var areaPart = Game.Instance?.CurrentlyLoadedAreaPart;
        if (anchor != null && areaPart != null && areaPart.Bounds != null)
        {
            var b = areaPart.Bounds.LocalMapBounds;
            if (b.size.x > 1f && b.size.z > 1f)
            {
                var pos = anchor.Position;
                float fx = Mathf.Clamp01((pos.x - b.min.x) / b.size.x);
                float fz = Mathf.Clamp01((pos.z - b.min.z) / b.size.z);
                parts.Add(Geo.RegionWord(fx, fz));
            }
        }

        // Room name (RoomMap watershed): the planted cursor's room when scouting ahead, else the anchor's. Ready is
        // false for the first few frames after an area load (the map self-builds once the grid streams in).
        // Parity gate (main-HUD audit L4): the room map is fog-free by construction, but a sighted player sees only
        // blackness on never-seen ground — suppress the room id/class there (the "unexplored" tag below still fires).
        if (RoomMap.Ready)
        {
            var rpos = MapCursor.Has ? MapCursor.Position : (anchor != null ? anchor.Position : Vector3.zero);
            if (FogProbe.Classify(rpos) != FogProbe.FogState.NeverSeen)
            {
                var room = RoomMap.RoomAt(rpos);
                if (room != null) parts.Add(RoomMap.Describe(room));
            }
        }

        // Fog "unexplored": query the tile the player is oriented to — the planted cursor when the tile explorer is
        // active (scouting ahead into the unknown), otherwise the anchor's live position (which, being a party unit,
        // is always revealed, so the word only ever fires for a planted cursor sitting on never-seen ground).
        Vector3? probe = MapCursor.Has ? (Vector3?)MapCursor.Position
                       : anchor != null ? (Vector3?)Geo.Live(anchor)
                       : null;
        if (probe is Vector3 p && FogProbe.Classify(p) == FogProbe.FogState.NeverSeen)
            parts.Add(Loc.T("where.unexplored"));

        Speak(parts.Count > 0 ? string.Join(", ", parts) : Loc.T("where.unknown"));
    }

    // A door/transition item within this of a geometric opening COVERS it (the thing is the exit); also the
    // radius that resolves a covered opening's destination for a door's "leads to" tail. ~2 tiles.
    private const float ExitCoverSq = 2.5f * 2.5f;

    // V / Shift+V: cycle everything that leads OUT of the current room — door and area-transition items in (or
    // adjacent to) the room, plus the bare geometric openings no such item covers (RoomMap exits; a doorway with
    // a door in it cycles as the DOOR — it names, opens, and travels, so a duplicate bare opening is noise).
    // The target becomes the shared review selection like any other cycle: O re-announces it, I opens the door /
    // takes the transition, Home/Slash plants the cursor, Backspace then walks. The cursor is NEVER moved
    // implicitly (the old auto-plant silently flipped ScanFrom for every later scanner action). The room resolves
    // from the scan origin (planted cursor, else anchor) — review the ways out of the room you are LOOKING at —
    // and candidates are distance-sorted each press, continuing from the current selection. Mirrors WrathAccess's
    // DoCycleRoomExits. See RTAccess.Exploration.RoomMap.
    private static void CycleExit(int dir)
    {
        if (!RoomMap.Ready) { Speak(Loc.T("scan.no_rooms")); return; }
        var refPos = ScanFrom();
        var room = RoomMap.RoomAt(refPos);
        if (room == null) { Speak(Loc.T("scan.no_exits")); return; }

        // Door / area-transition items in (or adjacent to) this room — probe around the thing, since a closed
        // door's cells can be cut out of the walkable grid and resolve to either side or nothing. Reveal-latched
        // (IsVisible), same as the category browse, so an undiscovered transition never leaks.
        var things = new List<ScanItem>();
        foreach (var it in WorldModel.Items)
        {
            if (it == null || !it.IsVisible) continue;
            if (!it.HasNode(ScanTaxonomy.Doors) && !it.HasNode(ScanTaxonomy.Exits)) continue;
            if (InOrAdjacentTo(it.Position, room)) things.Add(it);
        }

        var list = new List<ScanItem>(things);
        foreach (var exit in room.Exits)
        {
            bool covered = false;
            foreach (var t in things)
            {
                float dx = t.Position.x - exit.Position.x, dz = t.Position.z - exit.Position.z;
                if (dx * dx + dz * dz < ExitCoverSq) { covered = true; break; }
            }
            if (!covered) list.Add(new ProxyRoomExit(exit));
        }
        if (list.Count == 0) { Speak(Loc.T("scan.no_exits")); return; }
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));

        int idx = ExitIndexOfSelected(list);
        idx = idx < 0 ? (dir >= 0 ? 0 : list.Count - 1) : Wrap(idx + dir, list.Count);
        var item = list[idx];
        _selectedKey = item.Key;

        // A bare opening announces its destination itself ("Exit to Room N…"); a door or transition speaks the
        // THING, so append where it leads — fog-gated like the openings (main-HUD audit L4).
        string line = item.Describe(refPos);
        if (!(item is ProxyRoomExit))
        {
            var dest = ExitDestination(item.Position, room);
            if (dest != null)
                line += ", " + (FogProbe.Classify(dest.Centroid) == FogProbe.FogState.NeverSeen
                    ? Loc.T("exit.leads_to_unexplored")
                    : Loc.T("exit.leads_to", new { room = RoomMap.Describe(dest) }));
        }
        Speak(line + ", " + Loc.T("nav.position", new { index = idx + 1, count = list.Count }));
    }

    // The V cycle's continuation: real items match by key identity (the standard rule), but bare openings match
    // by POSITION — the two sides of one threshold hold DISTINCT Exit objects at the same point, and RoomAt on a
    // boundary can resolve to either room, so an identity match would restart the cycle whenever the room flips.
    private static int ExitIndexOfSelected(List<ScanItem> list)
    {
        if (_selectedKey == null) return -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i].Key, _selectedKey)) return i;
            if (_selectedKey is RoomMap.Exit prev && list[i].Key is RoomMap.Exit cur
                && (cur.Position - prev.Position).sqrMagnitude < 0.05f) return i;
        }
        return -1;
    }

    // Is a thing's position in (or within one probe step of) the room? A closed door's own cells can be cut out
    // of the walkable grid, so its position may resolve to no room / the far side — probe the four cardinal
    // offsets too, so a door on this room's boundary counts as one of its exits.
    private static bool InOrAdjacentTo(Vector3 p, RoomMap.Room room)
    {
        for (int k = 0; k < 5; k++)
        {
            var probe = p;
            if (k == 1) probe.x += 1.5f; else if (k == 2) probe.x -= 1.5f;
            else if (k == 3) probe.z += 1.5f; else if (k == 4) probe.z -= 1.5f;
            if (RoomMap.RoomAt(probe) == room) return true;
        }
        return false;
    }

    /// <summary>The room on the far side of a door/transition item in the V cycle: the covered geometric
    /// opening's destination when one is nearby, else probe around the thing — a CLOSED door can cut the
    /// walkable grid, so no Exit record exists there. Null when nothing resolves (an area transition — the
    /// map just ends).</summary>
    private static RoomMap.Room ExitDestination(Vector3 p, RoomMap.Room from)
    {
        foreach (var exit in from.Exits)
        {
            float dx = exit.Position.x - p.x, dz = exit.Position.z - p.z;
            if (dx * dx + dz * dz < ExitCoverSq) return exit.To;
        }
        for (int radius = 0; radius < 2; radius++)
            for (int k = 0; k < 4; k++)
            {
                var probe = p;
                float d = radius == 0 ? 1.5f : 2.5f;
                if (k == 0) probe.x += d; else if (k == 1) probe.x -= d;
                else if (k == 2) probe.z += d; else probe.z -= d;
                var r = RoomMap.RoomAt(probe);
                if (r != null && r != from) return r;
            }
        return null;
    }

    // Is the loaded area part flagged indoors? Read directly from the publicized blueprint IndoorType (any value
    // but None is an interior). Fail-safe: a missing area part → outdoors (the word is omitted). (The fog
    // "unexplored" branch is handled above via FogProbe; the room name via RoomMap.RoomAt above it.)
    private static bool IsIndoors()
    {
        var areaPart = Game.Instance?.CurrentlyLoadedAreaPart;
        return areaPart != null && areaPart.m_IndoorType != Kingmaker.Blueprints.Area.IndoorType.None;
    }

    private static void PartyReadout()
    {
        var player = Game.Instance?.Player;
        var members = player?.PartyAndPets;
        if (members == null || members.Count == 0) { Speak(Loc.T("scan.no_party")); return; }

        var reference = player.MainCharacterEntity;
        var refPos = reference != null ? reference.Position : members[0].Position;

        var parts = new List<string>();
        foreach (var member in members)
        {
            if (member == null) continue;
            // Tag a downed/dead companion so the roster doesn't read them as a healthy member — the Party review cycle
            // (comma) now skips the dead entirely, but this roster still lists everyone, so it must say who is down.
            var line = member.CharacterName;
            if (member.LifeState.IsDead) line += ", dead";
            else if (!member.LifeState.IsConscious) line += ", unconscious";
            parts.Add(line + ", " + InteractableDescriber.DirectionAndDistance(refPos, member.Position));
        }
        Speak(Loc.T("scan.party", new { list = string.Join("; ", parts) }));
    }

    // Battlefield summary (C5): counts + combat reach/threat vs the acting unit, in one sentence. Enemies must be
    // currently seen (fog-gated); allies are always known. The in-range / threatening tallies use the shared
    // CombatReads (same numbers the per-enemy cycle speaks), and only in combat — out of combat it's just counts.
    private static void Summarize()
    {
        var me = Game.Instance?.TurnController?.CurrentUnit as BaseUnitEntity ?? Anchor();
        bool combat = Game.Instance?.Player?.IsInCombat == true && me != null;

        int enemies = 0, allies = 0, inRange = 0, threats = 0;
        int nearestCells = int.MaxValue;   // select the nearest by the SAME footprint-aware cell metric we speak,
        bool haveNearest = false;          // so a large multi-tile enemy can't be mis-ranked by raw centre distance.

        foreach (var it in WorldModel.Items)
        {
            if (!it.IsVisible || !it.IsUnit) continue;
            var u = it.TargetUnit;
            if (u == null || u.LifeState.IsDead) continue;

            if (it.Primary == ScanTaxonomy.UnitsParty) { allies++; continue; }
            if (it.Primary != ScanTaxonomy.UnitsEnemies || !it.CurrentlySeen) continue;

            enemies++;
            if (me != null)
            {
                int c = me.DistanceToInCells(u);
                if (c < nearestCells) { nearestCells = c; haveNearest = true; }
            }
            if (combat)
            {
                if (u.IsThreat(me)) threats++;
                if (CombatReads.InRange(me, u)) inRange++;
            }
        }

        if (enemies == 0 && allies == 0) { Speak(Loc.T("scan.no_one")); return; }

        var sb = new System.Text.StringBuilder();
        sb.Append(Loc.T(enemies == 1 ? "scan.sum_enemy_one" : "scan.sum_enemies", new { count = enemies }));
        if (combat && enemies > 0)
        {
            sb.Append(", ").Append(Loc.T("scan.sum_in_range", new { count = inRange }));
            if (threats > 0) sb.Append(", ").Append(Loc.T("scan.sum_threatening", new { count = threats }));
        }
        sb.Append(". ").Append(Loc.T(allies == 1 ? "scan.sum_ally_one" : "scan.sum_allies", new { count = allies })).Append('.');
        if (haveNearest)
            sb.Append(' ').Append(Loc.T("scan.sum_nearest", new { cells = nearestCells }));
        Speak(sb.ToString());
    }

    // ---- list building ----

    private static List<ScanItem> CategoryList(int categoryIndex, Vector3 refPos)
    {
        var cat = Categories[categoryIndex];
        // Two categories aren't WorldModel entities: local-map pins and fog-frontier openings (special-sourced).
        if (cat.Src == Source.Markers) return MarkerList(refPos);
        if (cat.Src == Source.Frontier) return FrontierList(refPos);

        var list = new List<ScanItem>();
        foreach (var it in WorldModel.Items)
        {
            // Dead units are kept out of the party/enemy/neutral categories — EXCEPT a lootable corpse, which the
            // game lets you loot: it flips its Primary to Corpses (so it never matches a faction category) and shows
            // in the Corpses category instead. An emptied/lootless corpse stays hidden. Object categories are
            // unaffected (a map object is never dead). Corpses also stay under the tile cursor, labelled dead.
            if (it.IsVisible && (!it.IsDead || it.LootableCorpse) && cat.Pred(it)) list.Add(it);
        }
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        return list;
    }

    private static List<ScanItem> GroupList(Group group, Vector3 refPos)
    {
        var list = new List<ScanItem>();
        foreach (var it in WorldModel.Items)
        {
            // DetectableFrom = currently seen OR a remembered (reveal-latched) thing under fog with a CLEAR line of
            // sight from the cursor — so a revealed-but-fogged interactable (a crime-scene skill check across the
            // room) re-enters the review cycles once you'd actually have a straight path to it, instead of being
            // hard-dropped by the old fog test. The category browse stays reveal-latched (IsVisible); this is the
            // narrower tactical cycle. Dead units still drop (you don't cycle to the dead) — but a lootable corpse
            // rides the OBJECT cycle (M) via its Corpses node. Only the dead gate affects units; objects/zones are
            // never dead.
            if (it.DetectableFrom(refPos) && (!it.IsDead || it.LootableCorpse) && InGroup(it, group)) list.Add(it);
        }
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        return list;
    }

    // The "points of interest" category: area-wide local-map landmark pins (objective / POI / important / loot),
    // wrapped as ScanItems and sourced from LocalMapModel.Markers, NOT the WorldModel snapshot. EVERY pin type is
    // perception-gated on the game's own marker.IsVisible() (main-HUD audit L3): the sighted local map hides any
    // pin whose IsVisible() is false — quest pins toggled Hidden by scripting (MarkOnLocalMap.SetHidden), owners
    // not yet revealed/awareness-passed (LocalMapMarkerPart), dead/unconscious owners (AddLocalMapMarker) — and
    // hidden pins STAY in LocalMapModel.Markers (SetHidden never detaches), so an ungated walk enumerates exactly
    // the withheld ones. Suppressed owner entities are skipped too, matching LocalMapVM.SetMarkers. (The loot-pin
    // half of this gate was verified in-game earlier: two undiscovered GoodLoot caches surfaced with
    // IsVisible()==false.) Exit markers are deliberately excluded: area exits are surfaced as their real
    // (activatable) world objects in the Exits category. Creature/Unit markers are excluded too — they belong to
    // the party/enemies/neutrals cycles.
    private static List<ScanItem> MarkerList(Vector3 refPos)
    {
        var list = new List<ScanItem>();
        foreach (var m in LocalMapModel.Markers)
        {
            if (m == null) continue;
            var type = m.GetMarkerType();
            if (type != LocalMapMarkType.Loot && type != LocalMapMarkType.Poi
                && type != LocalMapMarkType.DestinationMark && type != LocalMapMarkType.VeryImportantThing) continue;
            if (!LocalMapModel.IsInCurrentArea(m.GetPosition())) continue;
            if (MarkerHidden(m)) continue;
            list.Add(new ProxyMarker(m));
        }
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        return list;
    }

    // The "unexplored space" category: frontier blobs — openings where walkable never-seen ground borders explored
    // ground (see FrontierModel) — sourced from the frontier cache, not the WorldModel registry. Refresh is
    // TTL-cached, so the full-grid recompute runs at most once per burst of presses (key-press work, mirroring
    // WrathAccess's unexplored cycle); wrappers are fresh each press but key on the STABLE blob object, so the
    // selection survives rebuilds like every other category. No fog gate — the blobs ARE the fog edge.
    private static List<ScanItem> FrontierList(Vector3 refPos)
    {
        FrontierModel.Refresh();
        var list = new List<ScanItem>();
        foreach (var b in FrontierModel.Current) list.Add(new ProxyFrontier(b));
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        return list;
    }

    // The full sighted-map gate for one pin: the game's own perception check (LocalMapCommonMarkerVM feeds
    // IsVisible() to the view's SetActive) plus the Suppressed-entity filter from LocalMapVM.SetMarkers.
    private static bool MarkerHidden(ILocalMapMarker m)
    {
        if (!SafeMarkerVisible(m)) return true;
        try { return m.GetEntity()?.Suppressed == true; }
        catch { return true; } // unreadable owner → treat as hidden, the safe side
    }

    // marker.IsVisible() is a perception check; guard it so a marker whose check throws doesn't sink the whole list
    // (best-effort — treat an unreadable marker as hidden, the safe side for a spoiler-sensitive loot pin).
    private static bool SafeMarkerVisible(ILocalMapMarker m)
    {
        try { return m.IsVisible(); }
        catch { return false; }
    }

    private static bool InGroup(ScanItem it, Group group)
    {
        switch (group)
        {
            case Group.Party: return it.Primary == ScanTaxonomy.UnitsParty;
            case Group.Enemies: return it.Primary == ScanTaxonomy.UnitsEnemies;
            case Group.Neutrals: return it.Primary == ScanTaxonomy.UnitsNeutrals;
            default:
                // Objects (M): EVERY interactable map object, so any object is reachable by cycle + I — not just
                // containers/doors/exits/search points. Mechanisms (levers/consoles/buttons) and traps (disarm)
                // carry real interactions too; they used to be reachable only via the cursor's Enter or the
                // Ctrl+PageUp/Down category browse, which is what made activation feel inconsistent. Scenery (an
                // object with no interaction) is still excluded — there is nothing to activate.
                return it.HasNode(ScanTaxonomy.Containers) || it.HasNode(ScanTaxonomy.Doors)
                    || it.HasNode(ScanTaxonomy.Exits) || it.HasNode(ScanTaxonomy.SearchPoints)
                    || it.HasNode(ScanTaxonomy.Mechanisms) || it.HasNode(ScanTaxonomy.Traps)
                    || it.HasNode(ScanTaxonomy.Corpses);   // lootable bodies loot like containers via I
        }
    }

    // ---- selection plumbing ----

    private static void Select(List<ScanItem> list, int idx, Vector3 refPos, string prefix = null)
    {
        var item = list[idx];
        _selectedKey = item.Key;
        var line = item.Describe(refPos) + ", " + Loc.T("nav.position", new { index = idx + 1, count = list.Count });
        if (!string.IsNullOrEmpty(prefix)) line = prefix + line;
        // While aiming an attack, cycling doubles as picking a target: append the terse hit prediction (B3/B4).
        var pred = Targeting.PredictLine(item, verbose: false);
        if (!string.IsNullOrEmpty(pred)) line += ". " + pred;
        Speak(line);
    }

    private static int IndexOfSelected(List<ScanItem> list)
    {
        if (_selectedKey == null) return -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i].Key, _selectedKey)) return i;
        }
        return -1;
    }

    private static ScanItem ResolveSelected()
    {
        if (_selectedKey == null) return null;
        // Landmark selections (the points-of-interest category) aren't in the WorldModel registry — re-wrap the live
        // marker so Home-plant and the O re-announce keep working on them; null once it leaves the current area's set.
        // Re-apply the sighted-map gate here too (main-HUD audit L3): hidden pins remain enumerable in Markers, so a
        // selection made while visible must go stale the moment the game hides it — otherwise I (TravelTo) keeps
        // working on a pin the sighted map has withdrawn.
        if (_selectedKey is ILocalMapMarker marker)
            return LocalMapModel.Markers.Contains(marker) && !MarkerHidden(marker) ? new ProxyMarker(marker) : null;
        // Frontier selections (the unexplored-space category) aren't in the registry either — re-wrap the blob
        // while the cached frontier still holds it (the blob object survives recomputes while its opening
        // persists); null once the fog is pushed past it or the area changes.
        if (_selectedKey is FrontierModel.Blob blob)
            return FrontierModel.Contains(blob) ? new ProxyFrontier(blob) : null;
        // Bare-opening selections (the V cycle) aren't in the registry either — re-wrap while the current room
        // map still holds the exit; a map rebuild (area/part change) orphans it → null.
        if (_selectedKey is RoomMap.Exit exit)
            return RoomMap.ContainsExit(exit) ? new ProxyRoomExit(exit) : null;
        // Everything else keys on its backing entity — the persistent registry re-finds the SAME stable proxy in
        // O(1); null once it despawns or the area changes.
        return WorldModel.Find(_selectedKey);
    }

    /// <summary>The currently-selected scan item as a unit, if it is one and still present. A unit item's
    /// <see cref="ScanItem.Key"/> is its <see cref="BaseUnitEntity"/> (see <c>ProxyUnit.Key</c>); map-object
    /// items key on their entity, so this returns null for them. Resolves through the live
    /// <see cref="WorldModel.Items"/> registry (like the other selection consumers), so a selection that has left the
    /// area, despawned, or died returns null instead of a stale cross-area entity. Used by <see cref="Inspect"/>
    /// to inspect whatever the player is currently browsing in the scanner.</summary>
    internal static BaseUnitEntity SelectedUnit() => ResolveSelected()?.Key as BaseUnitEntity;

#if DEBUG
    // Read-only diagnostic (F8 / DevApi.DebugScannerInteract): explains why the review SELECTION's I key and the
    // tile cursor's Enter can disagree — the "I can M-select it but I says can't interact, yet Home+Enter works"
    // report. Dumps, for the current selection AND every interactable object co-located with it, each interaction
    // part's Enabled vs live CanInteract() vs whether the game's own ClickMapObjectHandler.Interact could actually
    // fire it (SelectUnit non-null + not preparation turn). No world mutation. See [[rt-scanner-consistency]].
    internal static string DebugInteract()
    {
        var sb = new System.Text.StringBuilder();
        var g = Game.Instance;
        sb.Append("=== Scanner interact diagnostic ===\n");
        sb.Append("combat=").Append(g?.Player?.IsInCombat)
          .Append(" tb=").Append(g?.TurnController?.TurnBasedModeActive)
          .Append(" playerTurn=").Append(g?.TurnController?.IsPlayerTurn)
          .Append(" prep=").Append(g?.TurnController?.IsPreparationTurn)
          .Append(" controllerMouse=").Append(g?.IsControllerMouse).Append('\n');

        var units = new List<BaseUnitEntity>();
        var su = g?.SelectionCharacter?.SelectedUnits;
        if (su != null) foreach (var u in su) units.Add(u);
        sb.Append("selectedUnits=");
        for (int i = 0; i < units.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(units[i]?.CharacterName); }
        sb.Append('\n');

        var sel = ResolveSelected();
        sb.Append("selection=").Append(sel?.Name ?? "<none>")
          .Append(" proxy=").Append(sel?.GetType().Name ?? "-")
          .Append(" CanInteract=").Append(sel?.CanInteract).Append('\n');

        if (sel?.Key is MapObjectEntity selEntity) DumpInteractObject(sb, "SELECTION", selEntity, units);
        else sb.Append("  (selection is not a map object)\n");

        if (sel != null)
        {
            sb.Append("-- InteractablesAt(selection.Position), reach~2m --\n");
            var here = InteractableDescriber.InteractablesAt(sel.Position);
            if (here.Count == 0) sb.Append("  (none)\n");
            for (int i = 0; i < here.Count; i++) DumpInteractObject(sb, "TILE[" + i + "]", here[i], units);
        }

        var s = sb.ToString();
        Main.Log?.Log(s);
        return s;
    }

    private static void DumpInteractObject(System.Text.StringBuilder sb, string tag, MapObjectEntity o, List<BaseUnitEntity> units)
    {
        var view = o?.View;
        bool has = view != null
            && Kingmaker.Controllers.Clicks.Handlers.ClickMapObjectHandler.HasAvailableInteractions(view.gameObject);
        sb.Append("  ").Append(tag).Append(" '").Append(view != null ? view.name : (o?.ToString() ?? "?"))
          .Append("' HasAvailableInteractions=").Append(has).Append('\n');
        if (o == null) return;
        foreach (var part in o.Interactions)
        {
            if (part == null) continue;
            BaseUnitEntity picked = null;
            try { picked = part.SelectUnit(units); } catch { }
            string can; try { can = part.CanInteract().ToString(); } catch (Exception e) { can = "err:" + e.GetType().Name; }
            sb.Append("      part=").Append(part.GetType().Name)
              .Append(" Enabled=").Append(part.Enabled)
              .Append(" CanInteract=").Append(can)
              .Append(" Type=").Append(part.Type)
              .Append(" ShowOvertip=").Append(part.Settings.ShowOvertip)
              .Append(" SelectUnit=").Append(picked != null ? picked.CharacterName : "null")
              .Append('\n');
        }
    }
#endif

    private static BaseUnitEntity Anchor()
    {
        var game = Game.Instance;
        // In turn-based combat the scan origin follows the ACTING unit (whose turn it is) when it's one of yours, so
        // distances / where-am-I / the unplanted sort measure from that unit even if the player hasn't (re)selected it
        // — matching the combat cover/range tail (ProxyUnit.CombatSuffix / Summarize), which already reads CurrentUnit.
        // On an enemy's turn (CurrentUnit not directly controllable) we fall back to the selection so the player keeps a
        // stable own-unit origin instead of measuring from the enemy.
        if (game?.TurnController?.TurnBasedModeActive == true
            && game.TurnController.CurrentUnit is BaseUnitEntity acting && acting.IsDirectlyControllable)
            return acting;
        return game?.SelectionCharacter?.SelectedUnit?.Value ?? game?.Player?.MainCharacterEntity;
    }

    /// <summary>The origin the scanner measures and sorts from: the shared <see cref="MapCursor"/> when it is
    /// planted (tile explorer active — you browse relative to where you are looking), otherwise the anchor unit's
    /// live position. This is the two-cursor discipline — the review SELECTION (<see cref="_selectedKey"/>) is
    /// tracked separately and is unaffected by where this origin sits.</summary>
    private static Vector3 ScanFrom()
    {
        if (MapCursor.Has) return MapCursor.Position;
        var a = Anchor();
        return a != null ? Geo.Live(a) : Vector3.zero;
    }

    private static string CategoryLabel => Loc.T(Categories[_categoryIndex].Key);

    private static string GroupLabel(Group group)
    {
        switch (group)
        {
            case Group.Party: return Loc.T("taxonomy.units.party");
            case Group.Enemies: return Loc.T("taxonomy.units.enemies");
            case Group.Neutrals: return Loc.T("taxonomy.units.neutrals");
            default: return Loc.T("review.others");
        }
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    private static void Speak(string msg)
    {
        if (!string.IsNullOrEmpty(msg)) Speaker.Speak(msg, interrupt: true);
    }
}
