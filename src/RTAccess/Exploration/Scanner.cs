using Access.Core;          // TextUtil
using Kingmaker;                       // Game
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Utils; // LocalMapModel, ILocalMapMarker, LocalMapMarkType
using Kingmaker.Controllers.Units;     // UnitCommandsRunner (landmark travel)
using Kingmaker.EntitySystem;          // DistanceToInCells (EntityHelper ext)
using Kingmaker.EntitySystem.Entities; // BaseUnitEntity
using Kingmaker.Pathfinding;           // CustomGridNodeBase (the blast-position cycle's cell identity)
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
/// so the same key activates any object the same way; going TO the selection is Backslash's job
/// (<see cref="ApproachSelection"/>), the same two-step Backspace runs on the tile cursor
/// (see TileExplorer). Both interact keys drive the game's own object activation
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
/// I on one acts on the real interactable the pin SITS ON (a loot pin marks the corpse or container it points at)
/// and falls back to WALKING the party toward it only when nothing actionable is under the pin.
///
/// Keys: PageUp/Down = previous/next item; Ctrl+PageUp/Down = previous/next category; Comma/Period/N/M = cycle
/// nearest party/enemy/neutral/object of interest (Shift reverses). Live area effects (hazards + buff zones) have no
/// dedicated cycle key — they browse as the Hazards / Buff zones categories in the Ctrl+PageUp/Down list, and the
/// tile explorer names the hazard on the cursor tile. I = interact with selection (an object; a landmark → the
/// object under its pin, else walk to it; otherwise the object at the cursor); O = re-announce the current
/// selection; Home/Slash = plant the movement cursor on the selection; X = where am I; P = party readout. ' / Y
/// inspect the cursor / the selection (see <see cref="Inspect"/>). V / Shift+V = cycle the current room's ways
/// out — doors, area transitions, and uncovered geometric openings, merged into one distance-sorted review that
/// drives the shared selection (see <see cref="CycleExit"/>). J / Shift+J = cycle the cover POSITIONS around the
/// origin — the tiles you could stand on to be behind something, in or out of this turn's reach (see
/// <see cref="CycleCover"/> / <see cref="CoverModel"/>); Home/Slash then plants the cursor on the chosen cover side.
/// </summary>
internal static class Scanner
{
    // Where a browse category's items come from: most filter the WorldModel registry by a taxonomy predicate;
    // three are special-sourced lists with no backing registry entity (see MarkerList / FrontierList / CoverList).
    private enum Source { Registry, Markers, Frontier, Cover }

    // The browse categories cycled by Ctrl+PageUp/Down. Most filter the WorldModel registry by a taxonomy predicate;
    // the "points of interest" category is instead marker-sourced — the area-wide local-map pins
    // (objective / POI / important / loot) that have no interaction part to bin on — and the "unexplored space"
    // category is frontier-sourced (fog-edge openings; see FrontierModel). Area exits appear as their real
    // world objects under "taxonomy.exits" (activatable), so there is no separate marker-exits category.
    private static readonly (string Key, Source Src, Func<ScanItem, bool> Pred)[] Categories =
    {
        ("taxonomy.units.party",    Source.Registry, it => it.Primary == ScanTaxonomy.UnitsParty),
        // Friendly units that are NOT mine to command (capital-area companion NPCs, summons, scripted allies).
        // Their own category so "Party" can mean the party and nothing else; the comma cycle still reaches them.
        ("taxonomy.units.allies",   Source.Registry, it => it.Primary == ScanTaxonomy.UnitsAllies),
        ("taxonomy.units.enemies",  Source.Registry, it => it.Primary == ScanTaxonomy.UnitsEnemies),
        ("taxonomy.units.neutrals", Source.Registry, it => it.Primary == ScanTaxonomy.UnitsNeutrals),
        ("taxonomy.containers",     Source.Registry, it => it.HasNode(ScanTaxonomy.Containers)),
        ("taxonomy.corpses",        Source.Registry, it => it.HasNode(ScanTaxonomy.Corpses)),   // dead-with-loot bodies (I loots)
        ("taxonomy.doors",          Source.Registry, it => it.HasNode(ScanTaxonomy.Doors)),
        ("taxonomy.exits",          Source.Registry, it => it.HasNode(ScanTaxonomy.Exits)),
        ("taxonomy.poi",            Source.Markers,  null),   // area-wide local-map landmark pins (travel-to; see MarkerList)
        // Ways between floors (ladders, holes, drops, stairs) — placed next to exits because that is what they are
        // in a multi-level area: the way out of the level you are stuck on.
        ("taxonomy.levelchanges",   Source.Registry, it => it.HasNode(ScanTaxonomy.LevelChanges)),
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
        // Cover positions — the walkable tiles whose edges give half/full cover, in or out of this turn's reach
        // (see CoverModel / ProxyCover). Grid geometry, not a registry entity, and empty unless the game's own
        // cover overlay is up, so out of your turn the category simply skips like any other empty one.
        ("taxonomy.cover",          Source.Cover,    null),
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
    // The enemy key has a second gear. While an AREA ability is armed, the useful question stops being "which
    // enemy" and becomes "where do I put the template" — so the same key cycles ranked blast positions instead
    // (best first; see BlastPlan). Nothing new to learn, no key to find, and it self-cancels the moment the aim
    // ends. Single-target abilities and unarmed browsing are untouched.
    internal static void ReviewEnemies(bool back) => Safe(() =>
    {
        if (BlastPlan.Active) CycleBlast(back ? -1 : 1);
        else Review(Group.Enemies, back ? -1 : 1);
    });
    internal static void ReviewNeutrals(bool back) => Safe(() => Review(Group.Neutrals, back ? -1 : 1));
    internal static void ReviewObjects(bool back) => Safe(() => Review(Group.Objects, back ? -1 : 1));
    internal static void ExitNext() => Safe(() => CycleExit(1));
    internal static void ExitPrev() => Safe(() => CycleExit(-1));
    internal static void CoverNext() => Safe(() => CycleCover(1));
    internal static void CoverPrev() => Safe(() => CycleCover(-1));
    internal static void InteractSelected() => Safe(() =>
    {
        // While an ability is armed, I commits the aim on the review selection instead of interacting (see Targeting).
        if (Targeting.Aiming) { Targeting.CommitOnSelection(ResolveSelected()); return; }
        if (RTAccess.UI.Navigation.HasFocus) return;
        Interact();
    });
    // Backslash — the MOVEMENT half of the selection verb pair (I acts on the selection, Backslash goes to it),
    // sitting under Backspace, which is the same two-step aimed at the tile cursor instead. Out of combat it walks
    // the party toward the selection (Home + Backspace in one press); in turn-based combat it plants the acting
    // unit's best reachable tile toward it and a second press commits — the auto-approach for "the enemy is up a
    // ladder and finding a route by hand is tedious". Both routes funnel through TravelToPoint.
    internal static void ApproachSelection() => Safe(() =>
    {
        // Same rule the other movement key has: while an ability is armed, this cancels the aim instead of moving
        // (see Targeting / TileExplorer's Backspace). A second press then approaches.
        if (Targeting.Aiming) { Targeting.Cancel(); return; }
        if (RTAccess.UI.Navigation.HasFocus) return;
        var sel = ResolveSelected();
        if (sel == null) { Speak(Loc.T("scan.no_selection")); return; }
        // "Go to the selection" means something sharper when the selection is an enemy and there is a turn to
        // spend: go to where you can SHOOT it, not merely as close as the legs reach. This is also the engine's
        // own reading of the verb — out of combat, clicking a distant enemy walks to the ability's RangeCells and
        // fires (UnitCommandsRunner.TryUnitUseAbility with shouldApproach), stopping at weapon range rather than
        // closing on the target. Turn-based combat simply never had that path; this gives it one.
        var foe = FiringTarget();
        if (foe != null && ReferenceEquals(sel.Key, foe)) { ApproachToFire(foe); return; }
        TravelToPoint(sel.Position, sel.Name);
    });

    /// <summary>
    /// Plant the SAFEST stance from which the acting unit can shoot the selected enemy (second press commits, via
    /// the same two-step every other move uses). Already able to shoot from here → say so and stay put; nothing
    /// reachable can shoot it → say that, then fall through to the plain closest-approach so the answer is still
    /// "here is what you CAN do", not a dead end. That fallback line is the one the August field report needed:
    /// it ends a hopeless approach in one keypress instead of minutes of arrowing.
    /// </summary>
    private static void ApproachToFire(BaseUnitEntity foe)
    {
        var me = RTAccess.Combat.CommandDispatch.ActingUnit();
        if (me == null) return;   // refusal already spoken

        if (FiringPositions.InRangeNow(me, foe, out int hitNow, out var coverNow))
        {
            Speak(Loc.T("firing.already", new { hit = hitNow, cover = FiringPositions.CoverWord(coverNow) }));
            return;
        }

        var list = FiringPositions.Find(me, foe);
        if (list.Count == 0) { ApproachInCombat(foe.Position, Accessibility.UnitNames.Of(foe), Loc.T("firing.none")); return; }

        var spot = list[0];
        _firingNode = spot.Node;
        var r = RTAccess.Combat.CommandDispatch.MoveStep(spot.Node);
        if (r == RTAccess.Combat.CommandDispatch.MoveStepResult.Committed)
        {
            Speak(Loc.T("path.moving"));
        }
        else if (r == RTAccess.Combat.CommandDispatch.MoveStepResult.Planted)
        {
            // The move preview's own line carries the attacks of opportunity and the hazards on the way.
            Speak(FiringLine(me, spot) + ". " + PathInfo.Preview(me, spot.Node, out _)
                  + " " + Loc.T("path.preview.press_again"));
        }
        // Refused: MoveStep already spoke the engine's reason.
    }
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

        // A landmark (local-map pin) isn't itself clickable, but a loot / objective pin SITS ON the real
        // interactable it marks — so resolve that object at the pin's position and act on it, exactly as the
        // tile cursor would. Only a pin with nothing actionable under it falls back to travelling. Without
        // this the pin dead-ends on arrival and the player has to re-find the same body in the Corpses
        // category to loot it (reported independently by two testers — docs/feedback/2026-07-discord-triage.md).
        if (sel is ProxyMarker)
        {
            if (Activation.TryCursorObject(sel.Position)) return;
            TravelTo(sel);
            return;
        }

        // 1) The review selection itself, when it's an actionable object. NO same-area/navmesh pre-guard: the
        //    game's own Interact (ApproachAndInteract) walks a unit to the object and handles reachability itself,
        //    and the selection is always in the current area (a cross-area key resolves to null in ResolveSelected).
        //    The old Geo.SameArea guard compared navmesh CONNECTED COMPONENTS, so it wrongly refused same-area
        //    objects whose position snaps to a disconnected island the party can't stand ON — a pedestal
        //    (PostamentsObsidian), an object behind a low wall, an elevated prop — with a bogus "Can't reach". The
        //    tile cursor's Enter never had this guard, which is exactly why it interacted those objects fine.
        if (sel != null && sel.CanInteract)
        {
            if (sel.Interact()) { Activation.SpeakOutcome(true, sel.Name, sel.Reach); return; }
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

        // 3) Turn-based combat: a selection with nothing to click is still a DESTINATION — but that's the approach
        //    verb's job, not this key's (Backslash / scan.approach; see ApproachSelection). I stays a pure interact
        //    key so it never turns "shoot that enemy" into "walk at it". Point the player at the key rather than
        //    dead-ending on "nothing nearby"; the chord is read live so a rebind keeps the hint true.
        if (sel != null && Game.Instance?.TurnController?.TurnBasedModeActive == true)
        {
            var chord = RTAccess.Input.InputManager.Actions
                .FirstOrDefault(a => a.Key == "scan.approach")?.BindingsDisplay;
            Speak(chord == null ? Loc.T("scan.nothing_nearby") : Loc.T("scan.approach_hint", new { key = chord }));
            return;
        }

        Speak(Loc.T("scan.nothing_nearby"));
    }

    /// <summary>Plant/commit the acting unit's approach toward <paramref name="target"/> — the reachable tile
    /// this turn that lands closest to it (<see cref="PathInfo.FindApproachNode"/>), driven through the game's
    /// own two-press TB move flow (<see cref="RTAccess.Combat.CommandDispatch.MoveStep"/>): first press plants
    /// the holo preview and speaks where it stops (short distance, cost, provokes), the second press commits.
    /// All refusals are spoken (guards by <see cref="RTAccess.Combat.CommandDispatch.ActingUnit"/> /
    /// <c>MoveStep</c>). Starships keep the plain refusal — their inertial ShipPath movement has no
    /// "nearest tile" notion (see ShipPathInfo).</summary>
    /// <summary><paramref name="prelude"/> leads every line this speaks, so a caller that fell through to the
    /// plain approach can explain WHY in the same utterance ("No firing position this turn. Closest approach…")
    /// rather than in a second one the first would interrupt.</summary>
    private static void ApproachInCombat(Vector3 target, string label, string prelude = null)
    {
        void Say(string s) => Speak(string.IsNullOrEmpty(prelude) ? s : prelude + " " + s);

        var unit = RTAccess.Combat.CommandDispatch.ActingUnit();
        if (unit == null) return; // refusal spoken (not player turn / wrong selection)
        if (unit is StarshipEntity) { Say(Loc.T("travel.combat")); return; }

        var best = PathInfo.FindApproachNode(unit, target, out int shortTiles, out bool alreadyClosest);
        if (best == null) { Say(Loc.T("path.preview.out_of_movement")); return; }
        if (alreadyClosest) { Say(Loc.T("approach.no_closer", new { dest = label })); return; }

        var r = RTAccess.Combat.CommandDispatch.MoveStep(best);
        if (r == RTAccess.Combat.CommandDispatch.MoveStepResult.Committed)
        {
            Say(Loc.T("path.moving"));
        }
        else if (r == RTAccess.Combat.CommandDispatch.MoveStepResult.Planted)
        {
            string lead = shortTiles <= 1
                ? Loc.T("approach.reaches", new { dest = label })
                : Loc.T("approach.short", new { dest = label, tiles = shortTiles });
            Say(lead + " " + PathInfo.Preview(unit, best, out _) + " " + Loc.T("path.preview.press_again"));
        }
        // Refused: MoveStep already spoke the engine's reason.
    }

    /// <summary>
    /// Selection-tier activation, shared with the tile cursor's Enter (<see cref="TileExplorer.InteractAtCursor"/>)
    /// so both interact keys reach the same targets. An actionable review selection → interact through the game's
    /// own click path — distance-agnostic (you can act on a cycled object across the room); reachability is left to
    /// the game's own approach-and-interact rather than a pre-guard, so a same-area object on a disconnected navmesh
    /// island (a pedestal, an elevated prop) is no longer wrongly refused. A landmark
    /// → activate the real interactable the local-map pin marks, falling back to walking the party toward it when
    /// nothing actionable sits under the pin. Returns true when it
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
            Activation.SpeakOutcome(sel.Interact(), sel.Name, sel.Reach);
            return true;
        }
        // Same landmark rule as Interact: act on whatever real interactable the pin marks, travel only when the
        // pin has nothing actionable under it.
        if (sel is ProxyMarker)
        {
            if (Activation.TryCursorObject(sel.Position)) return true;
            TravelTo(sel);
            return true;
        }
        return false;
    }

    /// <summary>Walk the party toward a landmark — the only action a local-map pin supports. Off-mesh pins (far
    /// exits, floating markers) would make the pathfinder drop a direct move, so it heads as far toward the pin as
    /// continuous walkable floor allows (<see cref="Geo.SnapToWalkable"/>) and issues the game's own formation move.
    /// Refused in combat (travelling across the area mid-fight makes no sense — mirrors the old landmark walk gate).</summary>
    private static void TravelTo(ScanItem landmark)
        => TravelToPoint(landmark.Position, landmark.Name);

    /// <summary>The travel verb by raw world point, for callers that hold a position rather than a
    /// <see cref="ScanItem"/> — the Local Map window's pin rows, whose sighted twin is a right-click on the map
    /// running the very same <c>MoveSelectedUnitsToPoint</c> (<c>LocalMapVM.OnClick</c>). Shares the combat
    /// refusal, the walkable snap and the spoken confirmation with the landmark cycle's I key so the two
    /// surfaces behave identically.</summary>
    internal static void TravelToPoint(Vector3 target, string label)
    {
        if (Game.Instance?.Player?.IsInCombat == true)
        {
            // In turn-based combat the long walk becomes an APPROACH: the acting unit's best reachable tile
            // toward the target, through the game's own two-press move preview. Any non-TB combat edge keeps
            // the old refusal.
            if (Game.Instance.TurnController.TurnBasedModeActive) ApproachInCombat(target, label);
            else Speak(Loc.T("travel.combat"));
            return;
        }
        var self = Anchor();
        if (self == null) { Speak(Loc.T("status.no_selection")); return; }

        var from = Geo.Live(self);
        var dest = Geo.SnapToWalkable(target, from);
        if (Geo.Distance(from, dest) < 1.5f) { Speak(Loc.T("landmark.cant_head")); return; }
        UnitCommandsRunner.MoveSelectedUnitsToPoint(dest);
        Speak(Loc.T("landmark.walking_to", new { dest = label }));
    }

    /// <summary>Re-speak the resolved selection (any group) from the current scan origin — the O key. While aiming
    /// an attack at this unit it also appends the FULL hit breakdown (base hit, each avoidance, damage, per-shot
    /// burst), so O is "tell me more about this shot" versus the terse line the cycle gives.</summary>
    private static void ReSpeakSelection()
    {
        var item = ResolveSelected();
        if (item == null) { Speak(Loc.T("scan.no_selection")); return; }
        var refPos = ScanFrom();
        var line = item.Describe(refPos);
        var pred = Targeting.PredictLine(item, verbose: true);
        if (!string.IsNullOrEmpty(pred)) line += ". " + pred;
        Speak(line);
        Sonar.PlayReview(item, refPos); // re-ping on O re-announce, same as a cycle landing
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
            // Level changers count as exits from this room: in a stacked area the ladder out of the gallery IS the
            // way on, and the room graph cannot see it (the floors are separate walkable components, joined by an
            // interaction rather than by a connection the segmenter can walk).
            if (!it.HasNode(ScanTaxonomy.Doors) && !it.HasNode(ScanTaxonomy.Exits)
                && !it.HasNode(ScanTaxonomy.LevelChanges)) continue;
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

    // J / Shift+J: cycle the COVER POSITIONS nearest the scan origin — the walkable tiles whose edges give half or
    // full cover (see CoverModel), distance-sorted and continuing from the current selection like every other
    // cycle. The hit becomes the shared review selection, so O re-announces it and Home/Slash plants the cursor on
    // the COVER SIDE — the tile you would stand on — from where the tile readout names the same edges back and
    // Backspace walks the party there (in turn-based combat, Backslash plants the move preview toward it). The
    // cursor is NEVER moved implicitly, matching the V exit cycle: moving it would silently flip ScanFrom for
    // every later scanner action.
    private static void CycleCover(int dir)
    {
        // J is the positioning key, and with an enemy under the review cursor the position question is no longer
        // "where is there cover" but "where can I stand and still shoot THAT" — so the key changes gear, the same
        // way the enemy cycle does while an area ability is armed. Falls back to the plain cover cycle whenever no
        // enemy is selected. See FiringPositions.
        var foe = FiringTarget();
        if (foe != null) { CycleFiring(foe, dir); return; }

        if (!InteractableDescriber.CoverOverlayActive)
        {
            // In combat the key has something worth explaining — it is an enemy's turn, or an ability is armed,
            // and the answer will come back in a moment. Out of combat there is no cover overlay to speak of at
            // all, so J stays SILENT rather than nagging on every press: a dead key that talks back is worse than
            // one that doesn't. (Deployment counts as "overlay active" above, so scouting cover before the first
            // shot still works.)
            if (Game.Instance?.Player?.IsInCombat == true) Speak(Loc.T("scan.cover_unavailable"));
            return;
        }
        var refPos = ScanFrom();
        var list = CoverList(refPos);
        if (list.Count == 0) { Speak(Loc.T("scan.no_cover")); return; }

        int idx = CoverIndexOfSelected(list);
        idx = idx < 0 ? (dir >= 0 ? 0 : list.Count - 1) : Wrap(idx + dir, list.Count);
        Select(list, idx, refPos);
    }

    // The J cycle's continuation. Identity first — a burst of presses reuses one cached scan, so the SAME Spot
    // objects come back — then the origin-independent Id + signature, which re-finds the same cluster after a
    // recompute (the cursor moved, so the scan window did too). Mirrors ExitIndexOfSelected's identity-then-value
    // shape for the same reason: these items have no backing entity to key on.
    private static int CoverIndexOfSelected(List<ScanItem> list)
    {
        if (_selectedKey == null) return -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i].Key, _selectedKey)) return i;
            if (_selectedKey is CoverModel.Spot prev && list[i].Key is CoverModel.Spot cur
                && cur.Id == prev.Id && cur.Sig == prev.Sig) return i;
        }
        return -1;
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

    // Scratch for the room probes below — keypress work on a handful of rooms, so one shared buffer is plenty.
    private static readonly List<RoomMap.Room> _nearRooms = new List<RoomMap.Room>();

    // Is a thing's position in (or one step from) the room? A closed door's own cells can be cut out of the
    // walkable grid, so its position may resolve to no room or to the far side — RoomsNear seeds from the whole
    // cell ring around it for exactly that reason, but then expands only over edges the ENGINE says are
    // crossable. The old version probed ±1.5 m blind in four directions on top of RoomAt's own 2-cell ring, an
    // effective ~4 m radius that reached straight through thin walls and pulled the neighbouring corridor's
    // doors into this room's exit list.
    private static bool InOrAdjacentTo(Vector3 p, RoomMap.Room room)
    {
        // Tight seed: this decides whether a door belongs to YOUR room's exit list, so a thin wall must be enough
        // to keep the neighbouring corridor's doors out of it.
        RoomMap.RoomsNear(p, seedRadius: 1, steps: 2, into: _nearRooms);
        return _nearRooms.Contains(room);
    }

    /// <summary>The room on the far side of a door/transition item in the V cycle: the covered geometric
    /// opening's destination when one is nearby, else the nearest OTHER room reachable from the thing's own
    /// cell ring — a CLOSED door can cut the walkable grid, so no Exit record exists there, but the ring still
    /// straddles it. Null when nothing resolves (an area transition — the map just ends).</summary>
    private static RoomMap.Room ExitDestination(Vector3 p, RoomMap.Room from)
    {
        foreach (var exit in from.Exits)
        {
            float dx = exit.Position.x - p.x, dz = exit.Position.z - p.z;
            if (dx * dx + dz * dz < ExitCoverSq) return exit.To;
        }
        // Permissive seed: this only decorates the spoken line with where the door leads, and a door set in a
        // thick bulkhead has no walkable cell of its own on either side within one cell of its centre.
        RoomMap.RoomsNear(p, seedRadius: 2, steps: 3, into: _nearRooms);
        foreach (var r in _nearRooms)
            if (r != from) return r;
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
        // The game's OWN controllable group, not Player.PartyAndPets — in a capital-mode hub area the roster
        // collapses to the main character and the companions become ambient NPCs, and only this list tracks
        // that (it is what the game's party HUD and ours both bind to). See UnitFaction.
        var members = UnitFaction.Group();
        if (player == null || members == null || members.Count == 0) { Speak(Loc.T("scan.no_party")); return; }

        var reference = player.MainCharacterEntity;
        var refPos = reference != null ? reference.Position : members[0].Position;

        var parts = new List<string>();
        foreach (var member in members)
        {
            if (member == null) continue;
            // Tag a downed/dead companion so the roster doesn't read them as a healthy member — the Party review cycle
            // (comma) now skips the dead entirely, but this roster still lists everyone, so it must say who is down.
            var line = Accessibility.UnitNames.Of(member);
            if (member.LifeState.IsDead) line += ", " + Loc.T("unit.dead");
            else if (!member.LifeState.IsConscious) line += ", " + Loc.T("unit.unconscious");
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

            // "Allies" in the battlefield summary means everyone fighting on my side — my party AND any
            // non-commandable friendly (summon, scripted ally), so both friendly nodes count here.
            if (it.Primary == ScanTaxonomy.UnitsParty || it.Primary == ScanTaxonomy.UnitsAllies) { allies++; continue; }
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
        // The fight's own gauges close the line — momentum, veil, boss HP, turn / Necron timers, objective counter —
        // so U alone answers "what is the state of this fight" instead of U-then-K. Each gauge self-hides, so out of
        // combat this is null and the summary reads exactly as before; K still reads the full set (profit factor
        // included). See HudGauges.CombatSummary.
        var gauges = HudGauges.CombatSummary();
        if (!string.IsNullOrWhiteSpace(gauges)) sb.Append(' ').Append(gauges).Append('.');
        Speak(sb.ToString());
    }

    // ---- list building ----

    private static List<ScanItem> CategoryList(int categoryIndex, Vector3 refPos)
    {
        var cat = Categories[categoryIndex];
        // Three categories aren't WorldModel entities: local-map pins, fog-frontier openings and cover positions
        // (special-sourced).
        if (cat.Src == Source.Markers) return MarkerList(refPos);
        if (cat.Src == Source.Frontier) return FrontierList(refPos);
        if (cat.Src == Source.Cover) return CoverList(refPos);

        var list = new List<ScanItem>();
        foreach (var it in WorldModel.Items)
        {
            // Dead units are kept out of the party/enemy/neutral categories — EXCEPT a lootable corpse, which the
            // game lets you loot: it flips its Primary to Corpses (so it never matches a faction category) and shows
            // in the Corpses category instead. An emptied/lootless corpse stays hidden. Object categories are
            // unaffected (a map object is never dead). Corpses also stay under the tile cursor, labelled dead.
            if (it.IsVisible && (!it.IsDead || it.LootableCorpse) && cat.Pred(it)) list.Add(it);
        }
        SortByReachThenDistance(list, refPos);
        return list;
    }

    // Walkable things first, then unplaceable ones, then things on another level — and only then by distance. In a
    // multi-level area a flat distance sort buries the one reachable object behind a dozen nearer ones a floor away
    // (measured in the Kiava Gamma manufactorum: 20 of 21 search points were on a different walkable island). The
    // reach class is computed ONCE per item up front rather than inside the comparer, which would re-classify
    // O(n log n) times. Selection survives the reorder — the scanner re-finds it by Key identity, not by index.
    private static readonly Dictionary<ScanItem, int> _reachRank = new Dictionary<ScanItem, int>();

    private static void SortByReachThenDistance(List<ScanItem> list, Vector3 refPos)
    {
        _reachRank.Clear();
        for (int i = 0; i < list.Count; i++) _reachRank[list[i]] = Reachability.Rank(list[i].Reach);
        list.Sort((a, b) =>
        {
            int byReach = _reachRank[a].CompareTo(_reachRank[b]);
            return byReach != 0 ? byReach : a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos));
        });
        _reachRank.Clear();
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
        SortByReachThenDistance(list, refPos);
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

    // The "Cover" category / J cycle: cover POSITIONS — walkable tiles whose edges give half or full cover, found
    // by scanning the live grid around the scan origin (see CoverModel), not by filtering the registry. Gated on
    // the game's own cover overlay (InteractableDescriber.CoverOverlayActive), the same predicate that decides
    // whether the tile readout may name a tile's cover, so the mod never speaks cover a sighted player has no way
    // to read off the screen — and the two surfaces can never contradict each other. Empty (not refused) here, so
    // the category browse just skips it like any other empty category; the J cycle says why instead.
    // Deliberately NOT range-gated: an out-of-reach tile is flagged "unreachable" and kept, because planning a
    // position you cannot reach yet is the point (docs/feedback/2026-07-discord-triage.md, request 2).
    private static List<ScanItem> CoverList(Vector3 refPos)
    {
        var list = new List<ScanItem>();
        if (!InteractableDescriber.CoverOverlayActive) return list;
        foreach (var spot in CoverModel.Near(refPos)) list.Add(new ProxyCover(spot));
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        return list;
    }

    // The full sighted-map gate for one pin — the game's own perception check (LocalMapCommonMarkerVM feeds
    // IsVisible() to the view's SetActive) plus the Suppressed-entity filter from LocalMapVM.SetMarkers — now
    // owned by ProxyMarker so the local map's marker/exit cycles gate identically (see ProxyMarker.Listable).
    private static bool MarkerHidden(ILocalMapMarker m) => !ProxyMarker.Listable(m);

    private static bool InGroup(ScanItem it, Group group)
    {
        switch (group)
        {
            // The comma cycle is the coarse "friendly units" ring, like Objects below is the coarse
            // interactable ring: party members AND non-commandable allies, so no friendly unit becomes
            // unreachable by a single key. Each one names itself ("party member" / "ally") when selected,
            // and the Ctrl+PageUp/Down browse splits them into their own two categories.
            case Group.Party: return it.Primary == ScanTaxonomy.UnitsParty || it.Primary == ScanTaxonomy.UnitsAllies;
            // The period cycle is a TARGET PICKER, so it mirrors what the sighted reticle will lock onto: a unit
            // the game has flagged untargetable takes no click (ClickUnitHandler scores it 0) and shows no
            // overtip, so parking the cycle on it hands the player a decoy that silently eats a turn. It stays in
            // the registry — the tile cursor still reads it, tagged, and the Ctrl+PageUp/Down category browse
            // still lists it — because the unit IS on screen for a sighted player. See UnitFaction.Untargetable.
            case Group.Enemies: return it.Primary == ScanTaxonomy.UnitsEnemies
                                    && !UnitFaction.Untargetable(it.TargetUnit);
            case Group.Neutrals: return it.Primary == ScanTaxonomy.UnitsNeutrals;
            default:
                // Objects (M): EVERY interactable map object, so any object is reachable by cycle + I — not just
                // containers/doors/exits/search points. Mechanisms (levers/consoles/buttons) and traps (disarm)
                // carry real interactions too; they used to be reachable only via the cursor's Enter or the
                // Ctrl+PageUp/Down category browse, which is what made activation feel inconsistent. Scenery (an
                // object with no interaction) is still excluded — there is nothing to activate.
                return it.HasNode(ScanTaxonomy.Containers) || it.HasNode(ScanTaxonomy.Doors)
                    || it.HasNode(ScanTaxonomy.Exits) || it.HasNode(ScanTaxonomy.SearchPoints)
                    || it.HasNode(ScanTaxonomy.LevelChanges)  // ladders/climbs left SearchPoints; keep them in M
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
        Sonar.PlayReview(item, refPos); // review-cursor ping: hear WHERE the selection is (off unless review_sound set)
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
        // Cover selections (the J cycle / Cover category) are grid geometry, not entities — re-wrap while the
        // cached scan still holds the spot, so Home/Slash plants the cursor on its cover side and O re-announces
        // it; null once the spot is out of the scanned window or the graph rebuilt under it.
        if (_selectedKey is CoverModel.Spot spot)
        {
            var live = CoverModel.Resolve(spot);
            return live != null ? new ProxyCover(live) : null;
        }
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
            case Group.Party: return Loc.T("taxonomy.units.friendly");   // party + non-commandable allies
            case Group.Enemies: return Loc.T("taxonomy.units.enemies");
            case Group.Neutrals: return Loc.T("taxonomy.units.neutrals");
            default: return Loc.T("review.others");
        }
    }

    // ---- firing positions (the positioning key's enemy gear; see FiringPositions) ----

    // The stance last landed on, so the cycle resumes across re-ranks. Same reference-identity rule the blast
    // cycle uses: grid nodes are stable for the life of an area, and a stale one restarts the cycle at the
    // safest option — which is what should happen once the battlefield has moved.
    private static CustomGridNodeBase _firingNode;

    /// <summary>The enemy the positioning key should plan against: the review selection, while it is a live,
    /// visible enemy and the player actually has a turn to spend. Null in every other case, which is what makes
    /// J fall back to its ordinary cover cycle.</summary>
    private static BaseUnitEntity FiringTarget()
    {
        try
        {
            var tc = Game.Instance?.TurnController;
            if (tc == null || !tc.TurnBasedModeActive || !tc.IsPlayerTurn) return null;
            var sel = SelectedUnit();
            if (sel == null || sel.LifeState.IsDead || !sel.IsPlayerEnemy) return null;
            // Same parity lens as everywhere else — never plan a shot at something the sighted player cannot see.
            return (sel.IsPlayerFaction || sel.IsVisibleForPlayer) ? sel : null;
        }
        catch (Exception e) { Main.Log?.Error("Scanner.FiringTarget failed: " + e); return null; }
    }

    /// <summary>
    /// Step the ranked stances for shooting the selected enemy and plant the cursor on the chosen one, so
    /// Backspace commits the move, Semicolon reads the full vantage from it, and — once the move preview pins the
    /// virtual position — the enemy cycle answers "what else could I hit from there".
    /// </summary>
    private static void CycleFiring(BaseUnitEntity foe, int dir)
    {
        var me = RTAccess.Combat.CommandDispatch.ActingUnit();
        if (me == null) return;   // refusal already spoken (not your turn / wrong unit selected)

        var list = FiringPositions.Find(me, foe);
        // The unit's own cell is in the movable area, so "stay where you are" appears in the list by itself at
        // zero cost whenever it can shoot — no special case needed for "already in range".
        if (list.Count == 0) { Speak(Loc.T("firing.none")); return; }

        int idx = -1;
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i].Node, _firingNode)) { idx = i; break; }
        idx = idx < 0 ? (dir >= 0 ? 0 : list.Count - 1) : Wrap(idx + dir, list.Count);

        var spot = list[idx];
        _firingNode = spot.Node;
        Accessibility.TileExplorer.PlantOn(spot.Position, announce: false);
        Speak(FiringLine(me, spot) + ", " + Loc.T("nav.position", new { index = idx + 1, count = list.Count }));
    }

    /// <summary>"Half cover, 61 percent, 4 tiles north-east, 3 north 2 east, costs 3 of 6 movement" — the five
    /// facts a sighted player reads off the reticle, the blue area and the threat overlay at a glance. Measured
    /// from the ACTING UNIT, not the scan origin: the bearing is how far it has to walk. Attacks of opportunity
    /// are not repeated here — the move preview's own line carries them when the move is planted.</summary>
    private static string FiringLine(BaseUnitEntity me, FiringPositions.Spot spot)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Loc.T("firing.spot", new { cover = FiringPositions.CoverWord(spot.Cover), hit = spot.HitChance }));
        sb.Append(", ").Append(spot.Cost == 0
            ? Loc.T("firing.here")
            : InteractableDescriber.DirectionAndDistance(me.Position, spot.Position));
        int budget = UnityEngine.Mathf.RoundToInt(me.CombatState.ActionPointsBlue);
        if (spot.Cost > 0) sb.Append(", ").Append(Loc.T("firing.cost", new { cost = spot.Cost, budget }));
        return sb.ToString();
    }

    // ---- blast positions (the enemy key's area-ability gear; see BlastPlan) ----

    // The cell the player last landed on, so the cycle resumes where it left off across re-ranks. Grid nodes are
    // stable objects for the life of an area, so reference identity is enough; a stale one simply isn't found and
    // the cycle restarts at the best cell — which is also what should happen when the battlefield has moved on.
    private static CustomGridNodeBase _blastNode;

    /// <summary>
    /// Step the ranked blast positions and plant the cursor on the chosen one, so Enter fires the template there
    /// through the ordinary aim commit and Delete re-reads the cell with the full pattern tail. The spoken line is
    /// the decision, not the geometry: who it catches, whether it catches our own, and where it is.
    /// </summary>
    private static void CycleBlast(int dir)
    {
        var list = BlastPlan.Rank();
        if (list.Count == 0) { Speak(Loc.T("blast.none")); return; }

        int idx = -1;
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i].Node, _blastNode)) { idx = i; break; }
        idx = idx < 0 ? (dir >= 0 ? 0 : list.Count - 1) : Wrap(idx + dir, list.Count);

        var cell = list[idx];
        _blastNode = cell.Node;
        Accessibility.TileExplorer.PlantOn(cell.Position, announce: false);

        var sb = new System.Text.StringBuilder();
        sb.Append(Accessibility.UnitNames.Of(cell.Seed));
        sb.Append(", ").Append(Loc.T(cell.Enemies == 1 ? "blast.enemies_one" : "blast.enemies", new { count = cell.Enemies }));
        // Friendly fire speaks only when there IS some — silence means clear, the same rule the reachability and
        // cover words follow. The full per-target readout stays on the cursor (Delete) and the aim tail.
        if (cell.Allies > 0)
            sb.Append(", ").Append(Loc.T(cell.Allies == 1 ? "blast.allies_one" : "blast.allies", new { count = cell.Allies }));
        sb.Append(", ").Append(InteractableDescriber.DirectionAndDistance(ScanFrom(), cell.Position));
        sb.Append(", ").Append(Loc.T("nav.position", new { index = idx + 1, count = list.Count }));
        Speak(sb.ToString());
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    private static void Speak(string msg)
    {
        if (!string.IsNullOrEmpty(msg)) Speaker.Speak(msg, interrupt: true);
    }
}
