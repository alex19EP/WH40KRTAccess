using Kingmaker;                       // Game
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Utils; // LocalMapModel, ILocalMapMarker, LocalMapMarkType
using Kingmaker.EntitySystem.Entities; // BaseUnitEntity
using RTAccess.Accessibility;          // InteractableDescriber
using RTAccess.Speech;                 // Speaker
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// The scanner / review cursor: a keyboard-driven, categorized, distance-sorted browse of everything in the
/// current area (units + interactable map objects), plus tactical "nearest party / enemy / neutral / object"
/// review cycles. Its selection is a look-without-moving cursor — I interacts with it and never moves your
/// position; walking the party to a tile is the tile cursor's job (Backspace; see TileExplorer). Distances and
/// bearings are relative to the selected (or lead) unit and
/// are spoken via <see cref="InteractableDescriber"/> so the compass matches the other navigators.
///
/// Lists rebuild on every key (cheap; always fresh, via <see cref="WorldModel.Snapshot"/>) and the user's
/// selection is tracked by the backing entity so it survives the rebuild. Its actions are registered in the
/// <see cref="RTAccess.Input.InputCategory.Exploration"/> category (driven by <see cref="RTAccess.Input.InputManager"/>
/// and the dev harness's /input), so they are live only while the in-game screen has world control — dead in
/// windows/dialogue/cutscenes — and work in exploration AND surface tactical combat.
///
/// Keys: PageUp/Down = previous/next item; Ctrl+PageUp/Down = previous/next category; Comma/Period/N/M = cycle
/// nearest party/enemy/neutral/object of interest (Shift reverses); V/B = cycle area-wide exits / points of
/// interest (the local-map landmarks, Shift reverses); Z = cycle live area effects (hazards + buff zones, Shift
/// reverses); I = interact with selection; O = re-announce the current
/// selection; Home/Slash = plant the movement cursor on the selection; X = where am I; P = party readout. ' / Y
/// inspect the cursor / the selection (see <see cref="Inspect"/>).
/// </summary>
internal static class Scanner
{
    // The browse categories cycled by Ctrl+PageUp/Down, each a label + a membership predicate over a ScanItem.
    private static readonly (string Label, Func<ScanItem, bool> Pred)[] Categories =
    {
        ("Party",         it => it.Primary == ScanTaxonomy.UnitsParty),
        ("Enemies",       it => it.Primary == ScanTaxonomy.UnitsEnemies),
        ("Neutrals",      it => it.Primary == ScanTaxonomy.UnitsNeutrals),
        ("Containers",    it => it.HasNode(ScanTaxonomy.Containers)),
        ("Doors",         it => it.HasNode(ScanTaxonomy.Doors)),
        ("Exits",         it => it.HasNode(ScanTaxonomy.Exits)),
        ("Search points", it => it.HasNode(ScanTaxonomy.SearchPoints)),
        ("Traps",         it => it.HasNode(ScanTaxonomy.Traps)),
        ("Mechanisms",    it => it.HasNode(ScanTaxonomy.Mechanisms)),
        ("Scenery",       it => it.HasNode(ScanTaxonomy.Scenery)),
        ("Hazards",       it => it.HasNode(ScanTaxonomy.Hazards)),
        ("Buff zones",    it => it.HasNode(ScanTaxonomy.BuffZones)),
    };

    // Party/Enemies/Neutrals/Objects/Zones come from the WorldModel snapshot (units + reachable interactables +
    // live area effects); Exits/Poi are area-wide local-map landmarks sourced from LocalMapModel.Markers instead
    // (see GroupList / MarkerList). Zones covers ALL area effects (hazards + buff zones) so one cycle answers "what
    // AoEs are near me" — the Detail says which.
    private enum Group { Party, Enemies, Neutrals, Objects, Exits, Poi, Zones }

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
    // Area-wide landmark cycles (V / B, Shift reverses) — the coupled, reversible, cursor-relative twin of
    // LandmarkNav's raw [ / ] ring: they sort from the cursor, land as the review selection, and Home/Slash plants
    // the cursor on them (see the marker source branch in GroupList).
    internal static void ReviewExits(bool back) => Safe(() => Review(Group.Exits, back ? -1 : 1));
    internal static void ReviewPoi(bool back) => Safe(() => Review(Group.Poi, back ? -1 : 1));
    // Cycle the live area effects (hazards + buff zones) nearest the cursor — the AoE-awareness cycle for combat.
    internal static void ReviewZones(bool back) => Safe(() => Review(Group.Zones, back ? -1 : 1));
    internal static void InteractSelected() => Safe(() => { if (RTAccess.UI.Navigation.HasFocus) return; Interact(); });
    internal static void CursorToSelection() => Safe(PlantCursorOnSelection);
    internal static void WhereAmINow() => Safe(WhereAmI);
    internal static void ReadParty() => Safe(PartyReadout);
    // Re-speak the current selection from the live cursor origin (any group — unit, object, or landmark), so the
    // player can recover what they last cycled without stepping the list. Resolves through ResolveSelected (which
    // is marker-aware), so it works after a V/B landmark cycle too; drops the "N of M" ordinal (contextual to a cycle).
    internal static void AnnounceSelection() => Safe(ReSpeakSelection);

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Main.Log?.Error("Scanner failed: " + e); }
    }

    // ---- browsing ----

    private static void StepItem(int dir)
    {
        var anchor = Anchor();
        if (anchor == null) { Speak("No character selected."); return; }
        var refPos = ScanFrom();

        var list = CategoryList(_categoryIndex, refPos);
        if (list.Count == 0) { _selectedKey = null; Speak(CategoryLabel + ", empty."); return; }

        int idx = IndexOfSelected(list);
        idx = idx < 0 ? 0 : Wrap(idx + dir, list.Count);
        Select(list, idx, refPos);
    }

    private static void StepCategory(int dir)
    {
        var anchor = Anchor();
        if (anchor == null) { Speak("No character selected."); return; }
        var refPos = ScanFrom();

        _categoryIndex = Wrap(_categoryIndex + dir, Categories.Length);
        var list = CategoryList(_categoryIndex, refPos);
        if (list.Count == 0) { _selectedKey = null; Speak(CategoryLabel + ", empty."); return; }

        Select(list, 0, refPos, CategoryLabel + ", " + list.Count + ". ");
    }

    private static void Review(Group group, int dir)
    {
        var anchor = Anchor();
        if (anchor == null) { Speak("No character selected."); return; }
        var refPos = ScanFrom();

        var list = GroupList(group, refPos);
        if (list.Count == 0) { Speak(GroupLabel(group) + ", none in sight."); return; }

        int idx = IndexOfSelected(list);
        idx = idx < 0 ? (dir >= 0 ? 0 : list.Count - 1) : Wrap(idx + dir, list.Count);
        Select(list, idx, refPos);
    }

    // ---- actions on the selection ----

    private static void Interact()
    {
        var item = ResolveSelected();
        if (item == null) { Speak("No item selected."); return; }
        // Landmarks aren't reach-interactables — you travel TO them. Point the player at the coupling verbs
        // rather than saying "can't interact", which reads as an error for a valid landmark.
        if (item is ProxyMarker) { Speak(item.Name + ", landmark. Press Home to move the cursor there, then Backspace to walk."); return; }
        var anchor = Anchor();
        if (anchor == null) { Speak("No character selected."); return; }

        if (!Geo.SameArea(anchor.Position, item.Position)) { Speak("Can't reach " + item.Name + "."); return; }

        if (item.Interact()) Speak("Interacting with " + item.Name + ".");
        else Speak("Can't interact with " + item.Name + ".");
    }

    /// <summary>Re-speak the resolved selection (any group) from the current scan origin — the O key.</summary>
    private static void ReSpeakSelection()
    {
        var item = ResolveSelected();
        if (item == null) { Speak("No selection."); return; }
        Speak(item.Describe(ScanFrom()));
    }

    /// <summary>Home/Slash: plant the shared cursor on the current review selection's tile — the coupling core.
    /// The movement cursor follows the selection on demand; the selection itself (<see cref="_selectedKey"/>) is
    /// unchanged. The tile readout + camera-follow are the tile explorer's (<see cref="TileExplorer.PlantOn"/>).</summary>
    private static void PlantCursorOnSelection()
    {
        var item = ResolveSelected();
        if (item == null) { Speak("No selection to plant the cursor on."); return; }
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
        Speak(parts.Count > 0 ? string.Join(", ", parts) : "Unknown location.");
    }

    private static void PartyReadout()
    {
        var player = Game.Instance?.Player;
        var members = player?.PartyAndPets;
        if (members == null || members.Count == 0) { Speak("No party."); return; }

        var reference = player.MainCharacterEntity;
        var refPos = reference != null ? reference.Position : members[0].Position;

        var parts = new List<string>();
        foreach (var member in members)
        {
            if (member == null) continue;
            parts.Add(member.CharacterName + ", " + InteractableDescriber.DirectionAndDistance(refPos, member.Position));
        }
        Speak("Party: " + string.Join("; ", parts));
    }

    // ---- list building ----

    private static List<ScanItem> CategoryList(int categoryIndex, Vector3 refPos)
    {
        var pred = Categories[categoryIndex].Pred;
        var list = new List<ScanItem>();
        foreach (var it in WorldModel.Snapshot())
        {
            if (it.IsVisible && pred(it)) list.Add(it);
        }
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        return list;
    }

    private static List<ScanItem> GroupList(Group group, Vector3 refPos)
    {
        if (group == Group.Exits || group == Group.Poi) return MarkerList(group, refPos);

        var list = new List<ScanItem>();
        foreach (var it in WorldModel.Snapshot())
        {
            if (it.IsVisible && it.CurrentlySeen && InGroup(it, group)) list.Add(it);
        }
        list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        return list;
    }

    // Area-wide local-map landmarks (the same set LandmarkNav reads), wrapped as ScanItems so the Exits/Poi review
    // groups behave like the unit/object cycles. Sourced from LocalMapModel.Markers, NOT the WorldModel snapshot.
    // Do NOT filter on marker.IsVisible() — it's a perception check that hides ordinary exits (see
    // LandmarkNav.BuildList). Exits group = Exit markers; Poi group = Poi/Loot/objective/important (creature/Unit
    // markers are excluded — they belong to the party/enemies/neutrals cycles).
    private static List<ScanItem> MarkerList(Group group, Vector3 refPos)
    {
        var list = new List<ScanItem>();
        foreach (var m in LocalMapModel.Markers)
        {
            if (m == null) continue;
            var type = m.GetMarkerType();
            if (type == LocalMapMarkType.Invalid || type == LocalMapMarkType.PlayerCharacter) continue;
            if (!LocalMapModel.IsInCurrentArea(m.GetPosition())) continue;
            bool match = group == Group.Exits
                ? type == LocalMapMarkType.Exit
                : type == LocalMapMarkType.Poi || type == LocalMapMarkType.Loot
                  || type == LocalMapMarkType.DestinationMark || type == LocalMapMarkType.VeryImportantThing;
            if (match) list.Add(new ProxyMarker(m));
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
                return it.HasNode(ScanTaxonomy.Containers) || it.HasNode(ScanTaxonomy.Doors)
                    || it.HasNode(ScanTaxonomy.Exits) || it.HasNode(ScanTaxonomy.SearchPoints);
        }
    }

    // ---- selection plumbing ----

    private static void Select(List<ScanItem> list, int idx, Vector3 refPos, string prefix = null)
    {
        var item = list[idx];
        _selectedKey = item.Key;
        var line = item.Describe(refPos) + ", " + (idx + 1) + " of " + list.Count;
        Speak(string.IsNullOrEmpty(prefix) ? line : prefix + line);
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
        // Landmark selections (Exits/Poi groups) aren't in the WorldModel snapshot — re-wrap the live marker so
        // Home-plant and the O re-announce keep working on them; null once it leaves the current area's set.
        if (_selectedKey is ILocalMapMarker marker)
            return LocalMapModel.Markers.Contains(marker) ? new ProxyMarker(marker) : null;
        foreach (var it in WorldModel.Snapshot())
        {
            if (ReferenceEquals(it.Key, _selectedKey)) return it;
        }
        return null;
    }

    /// <summary>The currently-selected scan item as a unit, if it is one and still present. A unit item's
    /// <see cref="ScanItem.Key"/> is its <see cref="BaseUnitEntity"/> (see <c>ProxyUnit.Key</c>); map-object
    /// items key on their entity, so this returns null for them. Resolves through the live
    /// <see cref="WorldModel.Snapshot"/> (like the other selection consumers), so a selection that has left the
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

    private static string CategoryLabel => Categories[_categoryIndex].Label;

    private static string GroupLabel(Group group)
    {
        switch (group)
        {
            case Group.Party: return "Party";
            case Group.Enemies: return "Enemies";
            case Group.Neutrals: return "Neutrals";
            case Group.Exits: return "Exits";
            case Group.Poi: return "Points of interest";
            case Group.Zones: return "Zones";
            default: return "Objects";
        }
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    private static void Speak(string msg)
    {
        if (!string.IsNullOrEmpty(msg)) Speaker.Speak(msg, interrupt: true);
    }
}
