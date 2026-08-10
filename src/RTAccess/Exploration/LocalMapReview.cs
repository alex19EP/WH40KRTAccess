using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Utils; // LocalMapModel, ILocalMapMarker, LocalMapMarkType
using RTAccess.Localization;   // Loc
using RTAccess.Speech;         // Speaker
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// The local-map window's REVIEW cursor — single-key cycles over what the map shows, nearest-first from the
/// <see cref="LocalMapCursor"/> (not from the party): comma = party, period = enemies, N = neutrals, B = the
/// map's pins, M = exits only; Shift reverses. It is deliberately its own state, isolated from the in-area
/// <see cref="Scanner"/>, so a map peek leaves your exploration selection exactly where you left it — the same
/// two-surface separation the sector map keeps.
///
/// The selection is what <see cref="LocalMapCursor.JumpToSelection"/> snaps to, which is the whole point of the
/// pairing: cycle until you hear the thing you want, then jump the map cursor onto it and plant / travel from
/// there.
///
/// <b>Sources, and why each is the right one.</b> Units come from the shared <see cref="WorldModel"/> — the same
/// proxies the in-area scanner reviews, so a creature is phrased identically on both surfaces — while pins come
/// straight from the game's own <c>LocalMapModel.Markers</c> through <see cref="ProxyMarker"/>. Pins are
/// ANNOTATIONS, never interactable (no marker view handles a click), which is why they live here and not in the
/// in-area object cycles.
///
/// <b>Visual parity.</b> Unlike WrathAccess — whose map lists everything DISCOVERED via a latched per-unit reveal
/// flag — this gates units on <see cref="ScanItem.IsVisible"/>, which for a creature is the game's
/// <c>IsVisibleForPlayer</c> and never latches. That is not a downgrade but RT's own rule: the game's local map
/// drops a hostile pin the moment the unit re-enters fog (<c>LocalMapVM.OnUpdateHandler</c>), so a latched list
/// would tell a blind player where enemies are after a sighted player has lost sight of them. What is NOT applied
/// is any distance or line-of-sight narrowing from the cursor: a map legitimately shows the whole area at once.
/// </summary>
internal static class LocalMapReview
{
    internal enum Group { Party, Enemies, Neutrals, Markers, Exits }

    // The pin cache doubles as part of the movement cursor's per-frame hover set, so it stays a stable list
    // rebuilt on open and on each cycle press (a reveal can add a pin mid-browse).
    private static readonly List<ScanItem> _markers = new List<ScanItem>();
    private static readonly List<ScanItem> _cycle = new List<ScanItem>();
    private static Group _group;
    private static int _index = -1;
    private static ScanItem _selected;

    /// <summary>The current selection, or null when nothing has been cycled to.</summary>
    public static ScanItem Selected => _selected;

    public static void Reset()
    {
        _cycle.Clear(); _selected = null; _index = -1; _group = Group.Party;
        RebuildMarkers();
    }

    public static void Clear()
    {
        _cycle.Clear(); _markers.Clear(); _selected = null; _index = -1;
    }

    /// <summary>Everything the map cursor can land ON — the pins plus the units the map draws. Enumerated per
    /// step, so it reads the live unit registry rather than a snapshot that could strand a walking creature.</summary>
    public static IEnumerable<ScanItem> Hoverable
    {
        get
        {
            foreach (var m in _markers) yield return m;
            foreach (var u in Units(null)) yield return u;
        }
    }

    // ---- registered handlers (InputCategory.LocalMap; see InputBindings) ----

    public static void CycleParty(bool back) => Cycle(Group.Party, back ? -1 : 1);
    public static void CycleEnemies(bool back) => Cycle(Group.Enemies, back ? -1 : 1);
    public static void CycleNeutrals(bool back) => Cycle(Group.Neutrals, back ? -1 : 1);
    public static void CycleMarkers(bool back) => Cycle(Group.Markers, back ? -1 : 1);
    public static void CycleExits(bool back) => Cycle(Group.Exits, back ? -1 : 1);

    private static void Cycle(Group g, int dir)
    {
        try
        {
            RebuildMarkers();
            var from = LocalMapCursor.Position;

            _cycle.Clear();
            switch (g)
            {
                case Group.Party: _cycle.AddRange(Units(ScanTaxonomy.UnitsParty)); break;
                case Group.Enemies: _cycle.AddRange(Units(ScanTaxonomy.UnitsEnemies)); break;
                case Group.Neutrals: _cycle.AddRange(Units(ScanTaxonomy.UnitsNeutrals)); break;
                case Group.Markers: _cycle.AddRange(_markers); break;
                case Group.Exits:
                    foreach (var m in _markers) if (IsExit(m)) _cycle.Add(m);
                    break;
            }

            if (_cycle.Count == 0)
            {
                _selected = null; _index = -1; _group = g;
                Speaker.Speak(Loc.T("localmap.none", new { group = GroupWord(g) }), interrupt: true);
                return;
            }

            _cycle.Sort((a, b) => a.DistanceTo(from).CompareTo(b.DistanceTo(from)));
            // A fresh group starts at its nearest entry (or its farthest, cycling backwards); staying in a group
            // steps. The index is re-homed on the SELECTION rather than kept blind, so a list that changed shape
            // between presses (a pin revealed, a creature lost to fog) resumes where the player actually was.
            if (g != _group || _index < 0) _index = dir > 0 ? 0 : _cycle.Count - 1;
            else
            {
                int at = IndexOfSelected();
                _index = at >= 0 ? at + dir : (dir > 0 ? 0 : _cycle.Count - 1);
                _index = ((_index % _cycle.Count) + _cycle.Count) % _cycle.Count;
            }
            _group = g;
            _selected = _cycle[_index];

            Sonar.PlayReview(_selected, from);   // positional ping from the MAP cursor, as in-area review does
            Speaker.Speak(_selected.Describe(from), interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("LocalMapReview.Cycle failed: " + e); }
    }

    // Where the current selection sits in the freshly-sorted list (by the proxy's stable identity key, since the
    // proxies themselves are recreated each press).
    private static int IndexOfSelected()
    {
        if (_selected == null) return -1;
        for (int i = 0; i < _cycle.Count; i++)
            if (ReferenceEquals(_cycle[i].Key, _selected.Key)) return i;
        return -1;
    }

    /// <summary>The units the map draws: alive, currently perceivable (see the parity note on the type), and — when
    /// <paramref name="primary"/> is given — of that faction. No cursor-relative narrowing: a map shows the area.</summary>
    private static IEnumerable<ScanItem> Units(string primary)
    {
        foreach (var it in WorldModel.Items)
        {
            if (!it.IsUnit || it.IsDead || !it.IsVisible) continue;
            if (primary != null && it.Primary != primary) continue;
            yield return it;
        }
    }

    /// <summary>Fresh proxies for every live, revealed, in-area pin. The party dots and the per-member destination
    /// flags are dropped: the party has its own cycle, and a destination pin is a transient echo of an order.</summary>
    private static void RebuildMarkers()
    {
        _markers.Clear();
        foreach (var m in LocalMapModel.Markers)
        {
            if (m == null) continue;
            var type = m.GetMarkerType();
            if (type != LocalMapMarkType.Exit && type != LocalMapMarkType.VeryImportantThing
                && type != LocalMapMarkType.Loot && type != LocalMapMarkType.Poi) continue;
            Vector3 pos;
            try { pos = m.GetPosition(); } catch { continue; }
            if (!InCurrentArea(pos)) continue;
            if (!ProxyMarker.Listable(m)) continue;   // the shared spoiler gate (also the scanner's)
            _markers.Add(new ProxyMarker(m));
        }
    }

    // LocalMapModel.IsInCurrentArea dereferences the loaded area part unguarded, so it throws during a transition.
    private static bool InCurrentArea(Vector3 pos)
    {
        try { return LocalMapModel.IsInCurrentArea(pos); }
        catch { return false; }
    }

    private static bool IsExit(ScanItem it)
        => it is ProxyMarker pm && pm.MarkType == LocalMapMarkType.Exit;

    private static string GroupWord(Group g)
    {
        switch (g)
        {
            case Group.Party: return Loc.T("localmap.group.party");
            case Group.Enemies: return Loc.T("localmap.group.enemies");
            case Group.Neutrals: return Loc.T("localmap.group.neutrals");
            case Group.Exits: return Loc.T("localmap.group.exits");
            default: return Loc.T("localmap.group.markers");
        }
    }
}
