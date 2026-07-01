using Kingmaker; // Game

namespace RTAccess.Exploration;

/// <summary>
/// The one live registry of everything in the current area: a persistent <see cref="ScanItem"/> proxy per unit,
/// interactable map object, and PLACED (ground) area effect present. NOT fog-filtered — membership is "is it here"
/// (in-area presence); <see cref="ScanItem.IsVisible"/> / <see cref="ScanItem.CurrentlySeen"/> are live per-item
/// lenses each consumer applies.
///
/// Ticked every frame from <c>Main.OnUpdate</c>: it diffs the game's entity pools against the held set and raises
/// <see cref="Added"/>/<see cref="Removed"/>, keeping ONE proxy instance per entity STABLE across frames. That
/// cross-frame identity is the point — it is what a future object/sonar cue attaches to so it can track a thing
/// enter/exit (a per-call snapshot would make fresh proxies each frame, so every frame would falsely look like an
/// enter/exit). The scanner and tile view just read <see cref="Items"/> and apply their own visibility lens. Only a
/// GENUINELY NEW entity builds a proxy (the ContainsKey guard keeps the common already-tracked path allocation-free);
/// the kept proxy reads the entity's live state on demand, so a door opening / HP change / zone growing is still seen
/// instantly. Replaces the old per-call <c>Snapshot()</c> enumerator.
/// </summary>
internal static class WorldModel
{
    private static readonly Dictionary<object, ScanItem> _items = new Dictionary<object, ScanItem>();
    private static readonly HashSet<object> _present = new HashSet<object>();
    private static readonly List<object> _gone = new List<object>();

    /// <summary>Every in-area item (units + map objects + placed area effects), unfiltered by fog. A live view over
    /// the registry — consumers copy into their own list (the scanner does) before sorting/holding across a Tick.</summary>
    public static IReadOnlyCollection<ScanItem> Items => _items.Values;

    /// <summary>Fired once when a genuinely new entity enters the registry / when a tracked one leaves — the
    /// cross-frame identity hook the cue and sonar systems subscribe to (Phase G). Raised on the main thread from
    /// <see cref="Tick"/>.</summary>
    public static event Action<ScanItem> Added;
    public static event Action<ScanItem> Removed;

    /// <summary>The stable proxy for an entity key (its backing entity), or null when it is not currently tracked.
    /// A <see cref="ScanItem.Key"/> IS its dictionary key, so the scanner re-finds its selection by identity in O(1)
    /// without re-scanning.</summary>
    public static ScanItem Find(object key)
        => key != null && _items.TryGetValue(key, out var item) ? item : null;

    /// <summary>Diff the live entity pools against the held set: build a proxy only for a genuinely new entity, and
    /// drop anything gone (despawned, or left when the area changed). Safe to call every frame; empties the registry
    /// when no area is loaded (menu / global map). Never throws out of the tick.</summary>
    public static void Tick()
    {
        try
        {
            var state = Game.Instance?.State;
            if (state == null) { ClearAll(); return; } // no area loaded (menu / global map)

            _present.Clear();
            foreach (var u in state.AllBaseUnits)
            {
                if (u == null) continue;
                if (!_items.ContainsKey(u)) Ensure(u, () => new ProxyUnit(u));
                _present.Add(u);
            }
            foreach (var o in state.MapObjects)
            {
                if (o == null) continue;
                if (!_items.ContainsKey(o)) Ensure(o, () => new ProxyMapObject(o));
                _present.Add(o);
            }
            // Placed ground zones only — skip on-unit auras (they follow a unit and there can be many, so they read
            // as noise among "zones"; the unit carries them). ProxyAreaEffect reads the real runtime shape.
            foreach (var ae in state.AreaEffects)
            {
                if (ae == null) continue;
                if (ae.View != null && ae.View.OnUnit) continue;
                if (!_items.ContainsKey(ae)) Ensure(ae, () => new ProxyAreaEffect(ae));
                _present.Add(ae);
            }

            // Drop anything no longer present (despawned, or gone when the area changed).
            _gone.Clear();
            foreach (var key in _items.Keys) if (!_present.Contains(key)) _gone.Add(key);
            for (int i = 0; i < _gone.Count; i++)
            {
                var key = _gone[i];
                var item = _items[key];
                _items.Remove(key);
                Removed?.Invoke(item);
            }
        }
        catch (Exception e) { Main.Log?.Error("WorldModel.Tick failed: " + e); }
    }

    private static void Ensure(object key, Func<ScanItem> make)
    {
        if (_items.ContainsKey(key)) return; // callers guard, but keep Ensure safe standalone
        var item = make();
        _items[key] = item;
        Added?.Invoke(item);
    }

    private static void ClearAll()
    {
        if (_items.Count == 0) return;
        var snapshot = new List<ScanItem>(_items.Values);
        _items.Clear();
        for (int i = 0; i < snapshot.Count; i++) Removed?.Invoke(snapshot[i]);
    }
}
