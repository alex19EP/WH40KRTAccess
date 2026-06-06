using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Rewired;

namespace RTAccess.Diagnostics;

/// <summary>
/// One-shot diagnostic: walks the LIVE Rewired configuration (ReInput) and writes it to JSON so we can
/// see exactly which keyboard keys map to which UI navigation actions (action ids 4-11 etc.).
/// Reads already-deserialized data via Rewired's public API — no asset parsing. Triggered by a hotkey;
/// this is dev tooling, not shipped behaviour.
/// </summary>
internal static class RewiredDump
{
    public static void Dump(string outDir)
    {
        if (!ReInput.isReady)
        {
            Main.Log?.Log("RewiredDump: ReInput not ready yet.");
            return;
        }

        var mapping = ReInput.mapping;
        var actionCats = mapping.ActionCategories.ToDictionary(c => c.id, c => c.name);
        var mapCats = mapping.MapCategories.ToDictionary(c => c.id, c => c.name);

        var actions = mapping.Actions
            .OrderBy(a => a.id)
            .Select(a => new Dictionary<string, object>
            {
                ["id"] = a.id,
                ["name"] = a.name,
                ["descriptiveName"] = a.descriptiveName,
                ["type"] = a.type.ToString(),
                ["category"] = actionCats.TryGetValue(a.categoryId, out var cn) ? cn : a.categoryId.ToString(),
            })
            .ToList();

        var maps = new List<object>();
        var player = ReInput.players.GetPlayer(0);
        if (player != null)
        {
            foreach (var map in player.controllers.maps.GetAllMaps())
            {
                if (map == null) continue;
                var bindings = map.AllMaps
                    .OrderBy(ae => ae.actionId)
                    .Select(ae => new Dictionary<string, object>
                    {
                        ["actionId"] = ae.actionId,
                        ["action"] = mapping.GetAction(ae.actionId)?.name,
                        ["element"] = ae.elementIdentifierName,
                        ["keyCode"] = ae.keyCode.ToString(),               // Unity KeyCode (keyboard maps)
                        ["keyboardKeyCode"] = ae.keyboardKeyCode.ToString(),
                        ["modifiers"] = ae.modifierKeyFlags.ToString(),
                        ["axisContribution"] = ae.axisContribution.ToString(),
                        ["elementType"] = ae.elementType.ToString(),
                    })
                    .ToList();

                maps.Add(new Dictionary<string, object>
                {
                    ["controllerType"] = map.controllerType.ToString(),
                    ["categoryId"] = map.categoryId,
                    ["category"] = mapCats.TryGetValue(map.categoryId, out var mcn) ? mcn : map.categoryId.ToString(),
                    ["enabled"] = map.enabled,
                    ["bindingCount"] = bindings.Count,
                    ["bindings"] = bindings,
                });
            }
        }

        var dump = new Dictionary<string, object>
        {
            ["controllerModeAtDump"] = Kingmaker.Game.Instance != null ? Kingmaker.Game.Instance.ControllerMode.ToString() : "<no Game>",
            ["actionCategories"] = actionCats.Select(kv => new Dictionary<string, object> { ["id"] = kv.Key, ["name"] = kv.Value }).ToList(),
            ["mapCategories"] = mapCats.Select(kv => new Dictionary<string, object> { ["id"] = kv.Key, ["name"] = kv.Value }).ToList(),
            ["actions"] = actions,
            ["player0Maps"] = maps,
        };

        var path = System.IO.Path.Combine(outDir ?? ".", "Rewired_dump.json");
        System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(dump, Formatting.Indented));
        Main.Log?.Log($"RewiredDump: wrote {path} ({actions.Count} actions, {maps.Count} maps).");
    }
}
