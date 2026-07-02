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
/// nearest party/enemy/neutral/object of interest (Shift reverses); Z = cycle live area effects (hazards + buff
/// zones, Shift reverses); I = interact with selection (an object; a landmark → walk to it; otherwise the object at
/// the cursor); O = re-announce the current
/// selection; Home/Slash = plant the movement cursor on the selection; X = where am I; P = party readout. ' / Y
/// inspect the cursor / the selection (see <see cref="Inspect"/>).
/// </summary>
internal static class Scanner
{
    // The browse categories cycled by Ctrl+PageUp/Down. Most filter the WorldModel registry by a taxonomy predicate;
    // the "points of interest" category is instead marker-sourced (Marker == true) — the area-wide local-map pins
    // (objective / POI / important / loot) that have no interaction part to bin on. Area exits appear as their real
    // world objects under "taxonomy.exits" (activatable), so there is no separate marker-exits category.
    private static readonly (string Key, bool Marker, Func<ScanItem, bool> Pred)[] Categories =
    {
        ("taxonomy.units.party",    false, it => it.Primary == ScanTaxonomy.UnitsParty),
        ("taxonomy.units.enemies",  false, it => it.Primary == ScanTaxonomy.UnitsEnemies),
        ("taxonomy.units.neutrals", false, it => it.Primary == ScanTaxonomy.UnitsNeutrals),
        ("taxonomy.containers",     false, it => it.HasNode(ScanTaxonomy.Containers)),
        ("taxonomy.doors",          false, it => it.HasNode(ScanTaxonomy.Doors)),
        ("taxonomy.exits",          false, it => it.HasNode(ScanTaxonomy.Exits)),
        ("taxonomy.poi",            true,  null),   // area-wide local-map landmark pins (travel-to; see MarkerList)
        ("taxonomy.searchpoints",   false, it => it.HasNode(ScanTaxonomy.SearchPoints)),
        ("taxonomy.traps",          false, it => it.HasNode(ScanTaxonomy.Traps)),
        ("taxonomy.mechanisms",     false, it => it.HasNode(ScanTaxonomy.Mechanisms)),
        ("taxonomy.scenery",        false, it => it.HasNode(ScanTaxonomy.Scenery)),
        ("taxonomy.hazards",        false, it => it.HasNode(ScanTaxonomy.Hazards)),
        ("taxonomy.buffzones",      false, it => it.HasNode(ScanTaxonomy.BuffZones)),
    };

    // Party/Enemies/Neutrals/Objects/Zones come from the WorldModel snapshot (units + reachable interactables +
    // live area effects). Zones covers ALL area effects (hazards + buff zones) so one cycle answers "what AoEs are
    // near me" — the Detail says which. (Landmarks are NOT a review group — they live only in the category browse.)
    private enum Group { Party, Enemies, Neutrals, Objects, Zones }

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
    // Cycle the live area effects (hazards + buff zones) nearest the cursor — the AoE-awareness cycle for combat.
    internal static void ReviewZones(bool back) => Safe(() => Review(Group.Zones, back ? -1 : 1));
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

        _categoryIndex = Wrap(_categoryIndex + dir, Categories.Length);
        var list = CategoryList(_categoryIndex, refPos);
        if (list.Count == 0) { _selectedKey = null; Speak(Loc.T("scan.category_empty", new { label = CategoryLabel })); return; }

        Select(list, 0, refPos, CategoryLabel + ", " + list.Count + ". ");
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

    // I interacts with the review selection when it's an actionable object, and otherwise falls back to the object
    // at the cursor — so it never dead-ends. Both branches drive the SAME in-game activation (ProxyMapObject.Interact
    // → area-transition / variative / ClickMapObjectHandler), which is also exactly what the cursor's Enter fires.
    private static void Interact()
    {
        var sel = ResolveSelected();

        // 1. Primary: the review selection, when it is an actionable interactable object (CanInteract is the game's
        //    own gate — see ProxyMapObject). Distance-agnostic — you can act on a cycled object across the room —
        //    so only a genuinely cross-area selection is refused.
        if (sel != null && sel.CanInteract)
        {
            var anchor = Anchor();
            if (anchor != null && !Geo.SameArea(anchor.Position, sel.Position))
            { Speak(Loc.T("scan.cant_reach_area", new { name = sel.Name })); return; }
            SpeakOutcome(sel.Interact(), sel.Name);
            return;
        }

        // 2. A landmark's only supported action is to TRAVEL to it — the game's local-map pin isn't clickable
        //    (verified: no marker view handles a click; LocalMapVM.OnClick just walks the party to the point), so
        //    I walks the party toward it rather than trying to "activate" it.
        if (sel is ProxyMarker) { TravelTo(sel); return; }

        // 3. Fallback: the nearest actionable object to the tile cursor (or, when the cursor is unplanted, to the
        //    anchor unit) — the SAME object the cursor's Enter acts on. So I still activates the object you're
        //    pointing at when the selection is a unit / area effect / non-actionable object / nothing.
        var near = InteractableDescriber.InteractableAt(MapCursor.Node ?? Anchor()?.CurrentUnwalkableNode);
        if (near != null) { var item = new ProxyMapObject(near); SpeakOutcome(item.Interact(), item.Name); return; }

        Speak(Loc.T("scan.nothing_nearby"));
    }

    /// <summary>The shared interaction-outcome line — "Interacting with X." / "Can't interact with X." — spoken by
    /// both the I key here and the cursor's Enter (<see cref="TileExplorer.InteractAtCursor"/>), so activation reads
    /// identically however the object was reached.</summary>
    private static void SpeakOutcome(bool ok, string name)
        => Speak(Loc.T(ok ? "scan.interacting" : "scan.cant_interact", new { name }));

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
        Speak(parts.Count > 0 ? string.Join(", ", parts) : Loc.T("where.unknown"));
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
        // The points-of-interest category is area-wide local-map pins, not WorldModel entities (see MarkerList).
        if (cat.Marker) return MarkerList(refPos);

        var list = new List<ScanItem>();
        foreach (var it in WorldModel.Items)
        {
            // !IsDead keeps corpses out of the party/enemy/neutral categories too (the unit categories); the object
            // categories are unaffected (a map object is never dead). Corpses stay under the tile cursor, labelled dead.
            if (it.IsVisible && !it.IsDead && cat.Pred(it)) list.Add(it);
        }
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        return list;
    }

    private static List<ScanItem> GroupList(Group group, Vector3 refPos)
    {
        var list = new List<ScanItem>();
        foreach (var it in WorldModel.Items)
        {
            // !IsDead drops corpses from the party/enemy/neutral review cycles (comma/period/N/M) — you don't cycle to
            // the dead. Only affects units (objects/zones report IsDead false), and matches Summarize's count filter.
            if (it.IsVisible && it.CurrentlySeen && !it.IsDead && InGroup(it, group)) list.Add(it);
        }
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        return list;
    }

    // The "points of interest" category: area-wide local-map landmark pins (objective / POI / important / loot),
    // wrapped as ScanItems and sourced from LocalMapModel.Markers, NOT the WorldModel snapshot. Do NOT filter on
    // marker.IsVisible() — it's a perception check that hides ordinary markers. Exit markers are deliberately
    // excluded: area exits are surfaced as their real (activatable) world objects in the Exits category. Creature/Unit
    // markers are excluded too — they belong to the party/enemies/neutrals cycles.
    private static List<ScanItem> MarkerList(Vector3 refPos)
    {
        var list = new List<ScanItem>();
        foreach (var m in LocalMapModel.Markers)
        {
            if (m == null) continue;
            var type = m.GetMarkerType();
            if (!LocalMapModel.IsInCurrentArea(m.GetPosition())) continue;
            if (type == LocalMapMarkType.Poi || type == LocalMapMarkType.Loot
                || type == LocalMapMarkType.DestinationMark || type == LocalMapMarkType.VeryImportantThing)
                list.Add(new ProxyMarker(m));
        }
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        return list;
    }

    private static bool InGroup(ScanItem it, Group group)
    {
        switch (group)
        {
            case Group.Party: return it.Primary == ScanTaxonomy.UnitsParty;
            case Group.Enemies: return it.Primary == ScanTaxonomy.UnitsEnemies;
            case Group.Neutrals: return it.Primary == ScanTaxonomy.UnitsNeutrals;
            case Group.Zones: return it.HasNode(ScanTaxonomy.Hazards) || it.HasNode(ScanTaxonomy.BuffZones);
            default:
                // Objects (M): EVERY interactable map object, so any object is reachable by cycle + I — not just
                // containers/doors/exits/search points. Mechanisms (levers/consoles/buttons) and traps (disarm)
                // carry real interactions too; they used to be reachable only via the cursor's Enter or the
                // Ctrl+PageUp/Down category browse, which is what made activation feel inconsistent. Scenery (an
                // object with no interaction) is still excluded — there is nothing to activate.
                return it.HasNode(ScanTaxonomy.Containers) || it.HasNode(ScanTaxonomy.Doors)
                    || it.HasNode(ScanTaxonomy.Exits) || it.HasNode(ScanTaxonomy.SearchPoints)
                    || it.HasNode(ScanTaxonomy.Mechanisms) || it.HasNode(ScanTaxonomy.Traps);
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
        if (_selectedKey is ILocalMapMarker marker)
            return LocalMapModel.Markers.Contains(marker) ? new ProxyMarker(marker) : null;
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

    private static BaseUnitEntity Anchor()
        => Game.Instance?.SelectionCharacter?.SelectedUnit?.Value ?? Game.Instance?.Player?.MainCharacterEntity;

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
            case Group.Zones: return Loc.T("taxonomy.zones");
            default: return Loc.T("review.others");
        }
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    private static void Speak(string msg)
    {
        if (!string.IsNullOrEmpty(msg)) Speaker.Speak(msg, interrupt: true);
    }
}
