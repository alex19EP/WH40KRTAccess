using HarmonyLib;
using System.Reflection;
using Kingmaker.PubSubSystem.Core;
using UnityEngine;
using UnityModManagerNet;
using RTAccess.Speech;
using RTAccess.Diagnostics;
using RTAccess.Accessibility;

namespace RTAccess;

public static class Main {
    internal static Harmony HarmonyInstance;
    internal static UnityModManager.ModEntry.ModLogger Log;

    public static bool Load(UnityModManager.ModEntry modEntry) {
        Log = modEntry.Logger;
        try {
            Logs.Init(modEntry.Path); // fresh logs each game launch
            Speaker.Initialize(modEntry);
            HarmonyInstance = new Harmony(modEntry.Info.Id);
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            // Voice world-exploration state (chosen interactable + area/loading transitions). One persistent
            // subscriber for the whole session; unsubscribed in OnUnload.
            EventBus.Subscribe(ExplorationEvents.Instance);
        } catch (Exception e) {
            Log.Error(e.ToString());
            HarmonyInstance?.UnpatchAll(HarmonyInstance.Id);
            throw;
        }

        modEntry.OnUpdate = OnUpdate;
        modEntry.OnUnload = OnUnload;
        Log.Log("RTAccess loaded. Speech backend: " + Speaker.ActiveBackend);
        return true;
    }

    private static void OnUpdate(UnityModManager.ModEntry modEntry, float dt) {
        // I/C/J/M/L/Y/V/B — open service windows in console mode by invoking the game's OWN guarded keyboard
        // callbacks (so unavailable windows stay closed). Announcement is on the real open (ServiceWindowAnnounce).
        WindowHotkeys.Update();

        // Shift+A/D + Alt+1..6 — switch the selected character in console mode (the keyboard equivalent of
        // the gamepad L2 party selector). Also gated to console mode.
        PartyHotkeys.Update();

        // PageUp/PageDown — cycle nearby world interactables (auto-spoken); End — walk to & interact;
        // Home — re-announce the current pick. Gated to console mode + exploration (see ExplorationNav).
        ExplorationNav.Update();

        // Ctrl+I — read the full tooltip / details of the focused element (item/ability description, etc.).
        // Controller trigger is still TBD.
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.I))
            SetFocusedPatch.ReadDetailsOfCurrent();

        // F6 — toggle the game between console (gamepad) UI mode and mouse mode. Console mode is forced
        // by default at launch (see ConsoleMode); F6 flips it live for testing/comparison.
        if (Input.GetKeyDown(KeyCode.F6)) ConsoleMode.Toggle();

        // F7 — re-read the currently focused element.
        if (Input.GetKeyDown(KeyCode.F7)) SetFocusedPatch.RereadCurrent();

        // F9 / F10 — diagnostics dumps (Rewired config / keybindings).
        if (Input.GetKeyDown(KeyCode.F9)) {
            RewiredDump.Dump(modEntry.Path);
            Speaker.Speak("Rewired config dumped.", interrupt: false);
        }
        if (Input.GetKeyDown(KeyCode.F10)) {
            KeybindingsDump.Dump(modEntry.Path);
            Speaker.Speak("Keybindings dumped.", interrupt: false);
        }

        // F12 — speech self-test (moved off F8, which is the game's QuickLoad).
        if (Input.GetKeyDown(KeyCode.F12)) {
            Speaker.Speak("RTAccess speech test. Backend is " + Speaker.ActiveBackend + ".", interrupt: false);
        }
    }

    private static bool OnUnload(UnityModManager.ModEntry modEntry) {
        EventBus.Unsubscribe(ExplorationEvents.Instance);
        Speaker.Stop();
        Speaker.Shutdown();
        HarmonyInstance?.UnpatchAll(HarmonyInstance.Id);
        return true;
    }
}
