using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Kingmaker.Settings;
using Kingmaker.Settings.Entities;

namespace RTAccess.Diagnostics;

/// <summary>
/// Diagnostic: dumps the game's keyboard keybindings (the rebindable hotkey system, separate from the
/// Rewired console-UI map). Walks SettingsRoot.Controls.Keybindings via reflection and records, per action,
/// the current and DEFAULT key (KeyCode + Ctrl/Alt/Shift) and its game-mode context. Authoritative answer
/// to "which keys are already used". Triggered by a hotkey; dev tooling, not shipped.
/// </summary>
internal static class KeybindingsDump
{
    public static void Dump(string outDir)
    {
        if (!SettingsRoot.Initialized)
        {
            Main.Log?.Log("KeybindingsDump: SettingsRoot not initialized yet.");
            return;
        }

        object keybindings;
        try { keybindings = SettingsRoot.Controls.Keybindings; }
        catch (System.Exception e) { Main.Log?.Log("KeybindingsDump: cannot reach Keybindings — " + e.Message); return; }

        var groups = new Dictionary<string, object>();
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

        // Auto-discover each keybinding context (General, ActionBar, Dialog, SelectCharacter, ...).
        foreach (var subField in keybindings.GetType().GetFields(Flags))
        {
            var subObj = subField.GetValue(keybindings);
            if (subObj == null) continue;

            var entries = new List<object>();
            foreach (var fi in subObj.GetType().GetFields(Flags))
            {
                var v = fi.GetValue(subObj);
                if (v is SettingsEntityKeyBindingPair e)
                {
                    entries.Add(Describe(fi.Name, e));
                }
                else if (v is SettingsEntityKeyBindingPair[] arr)
                {
                    for (int i = 0; i < arr.Length; i++)
                        if (arr[i] != null) entries.Add(Describe($"{fi.Name}[{i}]", arr[i]));
                }
            }
            if (entries.Count > 0) groups[subField.Name] = entries;
        }

        var path = System.IO.Path.Combine(outDir ?? ".", "Keybindings_dump.json");
        System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(groups, Formatting.Indented));
        var total = groups.Values.OfType<List<object>>().Sum(l => l.Count);
        Main.Log?.Log($"KeybindingsDump: wrote {path} ({total} bindings in {groups.Count} contexts).");
    }

    private static Dictionary<string, object> Describe(string name, SettingsEntityKeyBindingPair e)
    {
        var cur = e.GetValue();
        var def = e.DefaultValue;
        return new Dictionary<string, object>
        {
            ["action"] = name,
            ["current"] = Join(Fmt(cur.Binding1), Fmt(cur.Binding2)),
            ["default"] = Join(Fmt(def.Binding1), Fmt(def.Binding2)),
            ["gameModes"] = cur.GameModesGroup.ToString(),
            ["triggerOnHold"] = cur.TriggerOnHold,
        };
    }

    private static string Join(string a, string b) =>
        string.IsNullOrEmpty(b) ? a : (string.IsNullOrEmpty(a) ? b : a + " / " + b);

    private static string Fmt(KeyBindingData b)
    {
        if (b.Key == UnityEngine.KeyCode.None) return "";
        var mods = "";
        if (b.IsCtrlDown) mods += "Ctrl+";
        if (b.IsAltDown) mods += "Alt+";
        if (b.IsShiftDown) mods += "Shift+";
        return mods + b.Key;
    }
}
