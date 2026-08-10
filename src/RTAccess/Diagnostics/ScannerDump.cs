using Kingmaker;                          // Game
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.Utils; // LocalMapModel, ILocalMapMarker, LocalMapMarkType
using Kingmaker.EntitySystem.Entities;    // MapObjectEntity, BaseUnitEntity
using Newtonsoft.Json;
using RTAccess.Exploration;               // WorldModel, ScanItem, ProxyMarker
using UnityEngine;                        // Vector3

namespace RTAccess.Diagnostics;

/// <summary>
/// Diagnostic for the "scanner shows items that the M-cycle / tile-exploration don't" report: dumps every thing the
/// scanner can surface, tagged with the exact visibility gate each surface applies, so the divergent items are
/// obvious in one place.
///
/// TWO sources, because the scanner reads two:
/// <list type="bullet">
/// <item>the live registry <see cref="WorldModel.Items"/> (units + map objects + area effects) — the category browse
/// gates on <see cref="ScanItem.IsVisible"/> (reveal-latched), the M/review cycles gate on <see cref="ScanItem.DetectableFrom"/>
/// (currently seen OR fogged-but-clear-line-of-sight). An item with <c>IsVisible &amp;&amp; !DetectableFrom(origin)</c> is a
/// <b>phantom</b>: in the browse, gone from M. Each is tagged UNIT vs OBJECT — the fix differs (a fogged UNIT in the browse
/// breaks visual parity → tighten; a fogged OBJECT is legit local-map memory → keep). See the rt-scanner-consistency memory.</item>
/// <item>the area-wide local-map pins <see cref="LocalMapModel.Markers"/> feeding the "Points of interest" category —
/// which RT lists WITHOUT the marker perception gate (<c>Scanner.MarkerList</c>), so an undiscovered pin
/// (<c>marker.IsVisible() == false</c>) leaks into the browse yet never reaches M / tile-walk.</item>
/// </list>
///
/// Writes a JSON report to the mod dir, logs a summary + every phantom/leak line to Player.log, and RETURNS the
/// summary so the DEBUG /eval server can pull it (<c>DevApi.DumpScanner</c>). Bound to F11 in DEBUG. Mirrors and
/// extends WrathAccess's <c>Scanner.DumpObjectNames</c>. Dev tooling, not shipped in Release.
/// </summary>
internal static class ScannerDump
{
    public static string Dump(string outDir)
    {
        if (Game.Instance?.State == null) return "ScannerDump: no area loaded.";

        var origin = Game.Instance?.Player?.MainCharacterEntity?.Position ?? Vector3.zero;
        var records = new List<object>();
        var flagged = new List<string>();       // phantom registry items + leaking POI pins, for the log
        int catBrowse = 0, mCycle = 0, phantoms = 0;

        // ---- source 1: the live registry (units / map objects / area effects) ----
        foreach (var it in WorldModel.Items)
        {
            try
            {
                bool vis = it.IsVisible;
                bool seen = it.CurrentlySeen;
                // The M / review cycles gate on DetectableFrom (Scanner.GroupList), NOT bare CurrentlySeen: a fogged
                // item with a CLEAR line of sight from the origin is RE-ADMITTED. So the true "in browse, gone from M"
                // divergence is IsVisible && !DetectableFrom — using !CurrentlySeen over-counted the fogged-but-LOS-clear
                // items the cycles actually keep. (origin ≈ the anchor the cycles measure from.)
                bool detectable = Safe(() => it.DetectableFrom(origin), seen);
                bool deadOk = !it.IsDead || it.LootableCorpse;      // the shared dead-unit filter every surface applies
                bool inBrowse = vis && deadOk;                      // the category browse lists it (some category)
                bool inMCycle = inBrowse && detectable;             // the M / review cycles' real gate (DetectableFrom)
                bool phantom = inBrowse && !detectable;             // browse-only: the TRUE reported inconsistency
                bool tileEligible = Safe(() => it.CanInteract, false); // the tile-walk/Enter gate is actionability
                if (inBrowse) catBrowse++;
                if (inMCycle) mCycle++;
                if (phantom) phantoms++;

                var engine = EngineGates(it.Key);
                records.Add(new Dictionary<string, object>
                {
                    ["name"] = Safe(() => it.Name, "(name error)"),
                    ["kind"] = it.GetType().Name,
                    ["source"] = "registry",
                    ["primary"] = Safe(() => it.Primary, "(primary error)"),
                    ["nodes"] = Safe(() => string.Join(",", it.Nodes), "(nodes error)"),
                    ["isVisible"] = vis,
                    ["currentlySeen"] = seen,
                    ["detectable"] = detectable,
                    ["isUnit"] = Safe(() => it.IsUnit, false),
                    ["isDead"] = it.IsDead,
                    ["lootableCorpse"] = it.LootableCorpse,
                    ["inCategoryBrowse"] = inBrowse,
                    ["inMCycle"] = inMCycle,
                    ["tileEligible"] = tileEligible,
                    ["phantom"] = phantom,
                    ["metres"] = System.Math.Round(it.DistanceTo(origin), 1),
                    ["engine"] = engine,
                });

                if (phantom)
                    flagged.Add($"PHANTOM {(Safe(() => it.IsUnit, false) ? "UNIT" : "OBJECT")} '{Safe(() => it.Name, "?")}' [{Safe(() => it.Primary, "?")}] {JsonConvert.SerializeObject(engine)} tileEligible={tileEligible} {System.Math.Round(it.DistanceTo(origin), 1)}m");
            }
            catch (Exception e) { Main.Log?.Error("ScannerDump item failed: " + e); }
        }
        int registryCount = records.Count;

        // ---- source 2: the local-map POI pins (the "Points of interest" category, RT-ungated) ----
        int markers = 0, markerLeaks = 0;
        try
        {
            foreach (var m in LocalMapModel.Markers)
            {
                if (m == null) continue;
                Vector3 mp; LocalMapMarkType type;
                try { mp = m.GetPosition(); type = m.GetMarkerType(); }
                catch { continue; }
                if (!LocalMapModel.IsInCurrentArea(mp)) continue;
                if (type != LocalMapMarkType.Poi && type != LocalMapMarkType.Loot
                    && type != LocalMapMarkType.DestinationMark && type != LocalMapMarkType.VeryImportantThing) continue;

                var proxy = new ProxyMarker(m);
                bool perceptionVisible = Safe(() => m.IsVisible(), true); // the gate RT's POI browse bypasses
                double metres = System.Math.Round(proxy.DistanceTo(origin), 1);
                if (!perceptionVisible) markerLeaks++;
                markers++;

                records.Add(new Dictionary<string, object>
                {
                    ["name"] = Safe(() => proxy.Name, "(marker)"),
                    ["kind"] = "ProxyMarker",
                    ["source"] = "marker",
                    ["markerType"] = type.ToString(),
                    ["perceptionVisible"] = perceptionVisible, // false = undiscovered pin leaking into the POI browse
                    ["inCategoryBrowse"] = true,               // the POI category lists it unconditionally
                    ["inMCycle"] = false,                      // markers never enter the object cycle
                    ["tileEligible"] = false,                  // and are never read by tile-walk
                    ["metres"] = metres,
                });

                if (!perceptionVisible)
                    flagged.Add($"POI-LEAK '{Safe(() => proxy.Name, "?")}' [{type}] perceptionVisible=false {metres}m");
            }
        }
        catch (Exception e) { Main.Log?.Error("ScannerDump markers failed: " + e); }

        string path = null;
        try
        {
            path = System.IO.Path.Combine(outDir ?? ".", "Scanner_visibility_dump.json");
            System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(records, Formatting.Indented));
        }
        catch (Exception e) { Main.Log?.Error("ScannerDump write failed: " + e); }

        var summary = $"ScannerDump: {registryCount} registry items ({catBrowse} browse / {mCycle} M-cycle / {phantoms} PHANTOM) "
                    + $"+ {markers} POI pins ({markerLeaks} ungated-undiscovered LEAK)."
                    + (path != null ? " Wrote " + path + "." : "");
        Main.Log?.Log("[scannerdump] " + summary);
        foreach (var line in flagged) Main.Log?.Log("[scannerdump] " + line);
        return summary + (flagged.Count > 0 ? "\n" + string.Join("\n", flagged) : "");
    }

    // The raw engine visibility gates behind IsVisible / CurrentlySeen, so the dump says WHY a verdict landed —
    // the fields ProxyMapObject / ProxyUnit read. A ScanItem's Key IS its backing entity (see ProxyMapObject.Key).
    private static Dictionary<string, object> EngineGates(object key)
    {
        var d = new Dictionary<string, object>();
        try
        {
            switch (key)
            {
                case MapObjectEntity mo:
                    d["type"] = "mapObject";
                    d["inGame"] = mo.IsInGame;
                    d["revealed"] = mo.IsRevealed;
                    d["awarenessPassed"] = mo.IsAwarenessCheckPassed;
                    d["inFog"] = mo.IsInFogOfWar;
                    break;
                case BaseUnitEntity bu:
                    d["type"] = "unit";
                    d["playerFaction"] = bu.IsPlayerFaction;
                    d["visibleForPlayer"] = bu.IsVisibleForPlayer;
                    d["inFog"] = bu.IsInFogOfWar;
                    d["dead"] = bu.LifeState.IsDead;
                    break;
                default:
                    d["type"] = key?.GetType().Name ?? "null";
                    break;
            }
        }
        catch (Exception e) { d["error"] = e.Message; }
        return d;
    }

    private static T Safe<T>(Func<T> get, T fallback)
    {
        try { return get(); }
        catch { return fallback; }
    }
}
