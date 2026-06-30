using Kingmaker; // Game

namespace RTAccess.Exploration;

/// <summary>
/// The scanner's read surface: a fresh snapshot of every unit and interactable map object in the current area,
/// wrapped as <see cref="ScanItem"/> proxies. Rebuilt per action (cheap; always current) — there is no looping
/// audio to keep proxy instances stable for, so the persistent-diff registry the WrathAccess original used is
/// unnecessary in v1. Each consumer applies its own visibility lens (<see cref="ScanItem.IsVisible"/> /
/// <see cref="ScanItem.CurrentlySeen"/>).
/// </summary>
internal static class WorldModel
{
    public static List<ScanItem> Snapshot()
    {
        var list = new List<ScanItem>();
        var state = Game.Instance?.State;
        if (state == null) return list; // no area loaded (menu / global map)

        foreach (var unit in state.AllBaseUnits)
        {
            if (unit != null) list.Add(new ProxyUnit(unit));
        }
        foreach (var obj in state.MapObjects)
        {
            if (obj != null) list.Add(new ProxyMapObject(obj));
        }
        return list;
    }
}
