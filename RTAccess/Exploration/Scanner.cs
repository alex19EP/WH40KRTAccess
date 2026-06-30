using Kingmaker;                       // Game
using Kingmaker.Controllers.Units;     // UnitCommandsRunner
using Kingmaker.EntitySystem.Entities; // BaseUnitEntity
using Kingmaker.UnitLogic;             // UnitHelper.MoveCommandStatus, MoveCommandSettings, TryCreateMoveCommandTB
using RTAccess.Accessibility;          // InteractableDescriber
using RTAccess.Speech;                 // Speaker
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// The scanner / review cursor: a keyboard-driven, categorized, distance-sorted browse of everything in the
/// current area (units + interactable map objects), plus tactical "nearest party / enemy / neutral / object"
/// review cycles. Its selection is a look-without-moving cursor — End interacts with it, Insert walks the party
/// to it — that never moves your position. Distances/bearings are relative to the selected (or lead) unit and
/// are spoken via <see cref="InteractableDescriber"/> so the compass matches the other navigators.
///
/// Lists rebuild on every key (cheap; always fresh, via <see cref="WorldModel.Snapshot"/>) and the user's
/// selection is tracked by the backing entity so it survives the rebuild. Active while exploration owns the
/// keyboard (the same gate as the sibling navigators); works in exploration AND surface tactical combat.
///
/// Keys: PageUp/Down = previous/next item; Ctrl+PageUp/Down = previous/next category; Comma/Period/N/M = cycle
/// nearest party/enemy/neutral/object of interest (Shift reverses); End = interact; Insert = move party to it;
/// Home = where am I; P = party readout.
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
    };

    private enum Group { Party, Enemies, Neutrals, Objects }

    private static int _categoryIndex;     // index into Categories (Ctrl+PageUp/Down)
    private static object _selectedKey;     // the backing entity of the current selection (survives rebuilds)

    /// <summary>Polled from Main.OnUpdate. Gated to exploration owning the keyboard, mirroring the siblings.</summary>
    public static void Update()
    {
        if (!RTAccess.Screens.InGameScreen.ExplorationActive || RTAccess.UI.Navigation.HasFocus) return;

        // NB: bare `Input` resolves to the RTAccess.Input NAMESPACE here, not UnityEngine.Input — fully
        // qualify, the same as the sibling navigators (ExplorationNav/TileExplorer).
        bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
        bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);

        try
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.PageUp)) { if (ctrl) StepCategory(-1); else StepItem(-1); }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.PageDown)) { if (ctrl) StepCategory(1); else StepItem(1); }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Comma)) Review(Group.Party, shift ? -1 : 1);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Period)) Review(Group.Enemies, shift ? -1 : 1);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.N)) Review(Group.Neutrals, shift ? -1 : 1);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.M)) Review(Group.Objects, shift ? -1 : 1);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.End)) Interact();
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Insert)) MoveToSelected();
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Home)) WhereAmI();
            else if (UnityEngine.Input.GetKeyDown(KeyCode.P)) PartyReadout();
        }
        catch (Exception e)
        {
            Main.Log?.Error("Scanner failed: " + e);
        }
    }

    // ---- browsing ----

    private static void StepItem(int dir)
    {
        var anchor = Anchor();
        if (anchor == null) { Speak("No character selected."); return; }
        var refPos = anchor.Position;

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
        var refPos = anchor.Position;

        _categoryIndex = Wrap(_categoryIndex + dir, Categories.Length);
        var list = CategoryList(_categoryIndex, refPos);
        if (list.Count == 0) { _selectedKey = null; Speak(CategoryLabel + ", empty."); return; }

        Select(list, 0, refPos, CategoryLabel + ", " + list.Count + ". ");
    }

    private static void Review(Group group, int dir)
    {
        var anchor = Anchor();
        if (anchor == null) { Speak("No character selected."); return; }
        var refPos = anchor.Position;

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
        var anchor = Anchor();
        if (anchor == null) { Speak("No character selected."); return; }

        if (!Geo.SameArea(anchor.Position, item.Position)) { Speak("Can't reach " + item.Name + "."); return; }

        if (item.Interact()) Speak("Interacting with " + item.Name + ".");
        else Speak("Can't interact with " + item.Name + ".");
    }

    private static void MoveToSelected()
    {
        var item = ResolveSelected();
        if (item == null) { Speak("No item selected."); return; }
        var dest = item.Position;
        var game = Game.Instance;

        if (game.TurnController.TurnBasedModeActive)
        {
            var unit = game.TurnController.CurrentUnit as BaseUnitEntity;
            if (unit == null) { Speak("No active unit."); return; }
            var cmd = unit.TryCreateMoveCommandTB(
                new MoveCommandSettings { Destination = dest, DisableApproachRadius = true },
                showMovePrediction: false, out var status);
            if (cmd != null)
            {
                unit.Commands.Run(cmd);
                Speak("Moving to " + item.Name + ".");
            }
            else
            {
                Speak(MoveFailure(status));
            }
            return;
        }

        var anchor = Anchor();
        if (anchor == null) { Speak("No character selected."); return; }
        if (!Geo.OnNavmesh(dest)) { Speak("Not walkable."); return; }
        if (!Geo.SameArea(anchor.Position, dest)) { Speak("Can't reach that."); return; }
        UnitCommandsRunner.MoveSelectedUnitsToPoint(dest);
        Speak("Moving to " + item.Name + ".");
    }

    private static string MoveFailure(UnitHelper.MoveCommandStatus status)
    {
        switch (status)
        {
            case UnitHelper.MoveCommandStatus.NotEnoughMovementPoints: return "Not enough movement points.";
            case UnitHelper.MoveCommandStatus.DestinationUnreachable: return "Path blocked.";
            case UnitHelper.MoveCommandStatus.CannotMove: return "Can't move.";
            case UnitHelper.MoveCommandStatus.SamePath: return "Already moving there.";
            default: return "Can't reach that tile.";
        }
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
        var list = new List<ScanItem>();
        foreach (var it in WorldModel.Snapshot())
        {
            if (it.IsVisible && it.CurrentlySeen && InGroup(it, group)) list.Add(it);
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
        foreach (var it in WorldModel.Snapshot())
        {
            if (ReferenceEquals(it.Key, _selectedKey)) return it;
        }
        return null;
    }

    private static BaseUnitEntity Anchor()
        => Game.Instance?.SelectionCharacter?.SelectedUnit?.Value ?? Game.Instance?.Player?.MainCharacterEntity;

    private static string CategoryLabel => Categories[_categoryIndex].Label;

    private static string GroupLabel(Group group)
    {
        switch (group)
        {
            case Group.Party: return "Party";
            case Group.Enemies: return "Enemies";
            case Group.Neutrals: return "Neutrals";
            default: return "Objects";
        }
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    private static void Speak(string msg)
    {
        if (!string.IsNullOrEmpty(msg)) Speaker.Speak(msg, interrupt: true);
    }
}
