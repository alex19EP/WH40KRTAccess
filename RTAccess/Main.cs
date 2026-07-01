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
    /// <summary>The mod's install directory (UMM modEntry.Path) — root for bundled assets (locale JSON, …).</summary>
    internal static string ModDir;
    // Engage focus mode once, on the first frame the game's keyboard exists (it doesn't yet at Load).
    private static bool _bootFocusPending = true;

    public static bool Load(UnityModManager.ModEntry modEntry) {
        Log = modEntry.Logger;
        ModDir = modEntry.Path;
        try {
            Logs.Init(modEntry.Path); // fresh logs each game launch
            Speaker.Initialize(modEntry);
            // Load the mod's own speech strings (roles, positions, structural glue) and wire them into the
            // Message pipeline. Game CONTENT is already localized by the game; this is just our framework text.
            Localization.LocalizationManager.Initialize();
            // Mod settings tree + JSON persistence under persistentDataPath/RTAccess. Wire this BEFORE any feature
            // that reads a persisted toggle; the tree is mostly empty today, but the map-viewer overlay/scanner
            // prefs will hang off it. Initialize loads settings.json over the in-code defaults (+ Reindex inside).
            // Declare settings on the tree BEFORE Initialize so Reindex indexes them and Load applies saved values.
            // exploration.camera_follow (Off/On, default On) gates the tile-cursor follow-cam (TileExplorer.ScrollTo).
            var explCat = Settings.ModSettingsRegistry.EnsureCategory("exploration", "Exploration");
            if (explCat.GetByKey("camera_follow") == null)
                explCat.Add(new Settings.BoolSetting("camera_follow", "Camera follows cursor", true, "exploration.camera_follow"));
            Settings.ModSettings.Initialize(System.IO.Path.Combine(Application.persistentDataPath, "RTAccess"));
            // Parallel accessible-UI framework (Phase 2): register input actions + the screens whose
            // ScreenManager resolves over RootUiContext each frame (MainMenu first).
            Input.InputBindings.RegisterDefaults();
            Screens.ScreenManager.Initialize();
            HarmonyInstance = new Harmony(modEntry.Info.Id);
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            // Voice world-exploration state (chosen interactable + area/loading transitions). One persistent
            // subscriber for the whole session; unsubscribed in OnUnload.
            EventBus.Subscribe(ExplorationEvents.Instance);
            // Voice barks — overhead speech bubbles + subtitles (see BarkEvents / [[rt-bark-system]]). Also a
            // persistent session subscriber; unsubscribed in OnUnload.
            EventBus.Subscribe(BarkEvents.Instance);
            // Voice combat events (damage / heal / death / buffs) + refusal toasts ("not enough action points").
            // Passive event streams → queued speech, flushed once per frame by CombatEvents.Tick (see CombatEvents
            // / WarningReader). Persistent session subscribers; unsubscribed in OnUnload.
            EventBus.Subscribe(CombatEvents.Instance);
            EventBus.Subscribe(WarningReader.Instance);
            // Voice interaction outcomes the player can't see — currently lock-pick success/fail (an interaction runs
            // a skill check with no audible result of its own). Persistent session subscriber; unsubscribed below.
            EventBus.Subscribe(InteractionEvents.Instance);
            // Build the review-buffer set (Alt+arrows query a unit's live HP/AP/defenses/buffs without losing
            // UI focus); resolvers read the live selected unit / combat target each refresh.
            Buffers.BufferManager.Instance.RegisterDefaults();
#if DEBUG
            // Dev-only loopback driver (REPL + speech tap + load-save). Inert unless RTACCESS_DEV=1 or the
            // marker file is present; compiled out entirely in Release. See RTAccess/Dev/DevServer.cs.
            RTAccess.Dev.DevServer.Instance.Start();
#endif
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

    // Game.Keyboard's getter constructs KeyboardAccess, which dereferences UISettingsRoot.Instance — null
    // for a window during load (before the settings root is ready), so the getter throws rather than
    // returning null. Swallow that so the per-frame tick isn't broken while we wait to engage focus mode.
    private static bool KeyboardReady() {
        try { return Kingmaker.Game.Instance?.Keyboard != null; }
        catch { return false; }
    }

    private static void OnUpdate(UnityModManager.ModEntry modEntry, float dt) {
#if DEBUG
        // Run any queued /eval jobs on the main thread before our own per-frame work (dev harness).
        RTAccess.Dev.DevServer.Instance.Pump();
#endif

        // Pick up a mid-session game-language change so our framework strings follow it.
        Localization.LocalizationManager.Tick();

        // Reconcile this frame's buff churn, then flush queued combat-event lines in arrival order (passive →
        // never interrupt). WarningReader is reactive (no tick).
        CombatEvents.Instance.Tick();

        // Announce the post-load "press any key to continue" prompt (a silent barrier for blind players on
        // every area transition). Edge-detected; any key dismisses it.
        LoadingScreenAnnounce.Update();

        // The live world registry: diff the entity pools into stable per-entity scan proxies (units + map objects +
        // placed area effects), raising Added/Removed. Ticked BEFORE the input tick so the scanner's handlers read a
        // current-frame registry; the persistent proxies are what future object/sonar cues attach to. See
        // RTAccess.Exploration.WorldModel.
        Exploration.WorldModel.Tick();

        // ---- Parallel accessible-UI framework (Phase 2) ----
        // Engage focus mode once the keyboard exists (suppresses the game's own KeyboardAccess hotkeys so
        // our navigator owns the keys), and re-assert it across scene reloads.
        if (_bootFocusPending && KeyboardReady()) {
            _bootFocusPending = false;
            FocusMode.Set(true);
        }
        FocusMode.Tick();
        Input.InputManager.Tick();        // poll our input → navigator (UI) / handlers
        Screens.ScreenManager.Tick();     // resolve the screen stack from RootUiContext + attach the navigator
        UI.Navigation.TickTypeahead();    // typed letters → type-ahead search (after dispatch)

        // Service-window keys (I/C/J/M/L/Y/V/B) are handled by the GAME's own KeyboardAccess in console mode —
        // we don't open them ourselves (a duplicate open toggled the window shut). We only announce the real
        // open via the ServiceWindowAnnounce patch. See the rt-radial-menus memory.

        // Shift+A/D + Alt+1..6 — switch the selected character in console mode (the keyboard equivalent of the
        // gamepad L2 party selector). The game's own select keys live in the PC HUD, which is inactive in
        // console mode, so this is the sole handler — no double-fire. Gated to console mode.
        PartyHotkeys.Update();

        // The scanner / review cursor (the self-built replacement for the engine's mouse-mode-dead interactable
        // ring) is now registered in the Exploration input category and driven by InputManager.Tick above — no
        // direct poll here. The old engine-ring cycler (ExplorationNav) has been retired. PageUp/Down browse a
        // categorized, distance-sorted list of the area; Ctrl+PageUp/Down change category; , . N M cycle nearest
        // party/enemy/neutral/object; I interacts with the selection, Home/Slash plants the cursor on it, X reads
        // location, ' / Y inspect the cursor / the selection, P reads the party. See RTAccess.Exploration.Scanner /
        // RTAccess.Input.InputBindings.

        // [ / ] — cycle whole-area local-map landmarks (exits/POI/objective); \ — walk the party to the
        // selected one. Map-relative directions. Gated to console mode + exploration (see LandmarkNav).
        LandmarkNav.Update();

        // Ctrl+P — re-announce the current character-creation phase (name + position + progress). Gated to
        // CharGen being open; phase changes auto-announce (see CharGenAnnounce).
        CharGenAnnounce.Update();

        // The tile explorer (the always-active virtual grid cursor) is registered in the Exploration input category
        // and driven by InputManager.Tick above — no direct poll here, and no toggle. Arrow keys step it tile by
        // tile (Shift+arrows are the shadow-immune slot), C recenters on the party, Delete re-reads the tile,
        // Backspace issues the guarded move-to, and Enter / KeypadEnter interact with the nearest interactable to the
        // cursor. See RTAccess.Accessibility.TileExplorer / RTAccess.Input.InputBindings.

        // Ctrl+I — read the full tooltip / details of the focused element (item/ability description, etc.).
        // Controller trigger is still TBD.
        if ((UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl)) && UnityEngine.Input.GetKeyDown(KeyCode.I))
            SetFocusedPatch.ReadDetailsOfCurrent();

        // F6 — toggle the game between console (gamepad) UI mode and mouse mode. The mod no longer forces
        // console mode: the game boots in mouse mode and we drive our own parallel tree; F6 flips the live
        // mode for A/B testing vs the game's console focus ring (see ConsoleMode).
        if (UnityEngine.Input.GetKeyDown(KeyCode.F6)) ConsoleMode.Toggle();

        // F7 — re-read the currently focused element.
        if (UnityEngine.Input.GetKeyDown(KeyCode.F7)) SetFocusedPatch.RereadCurrent();

        // F9 / F10 — diagnostics dumps (Rewired config / keybindings). Key-driven, so interrupt
        // ([[rt-interrupt-speech-rule]]).
        if (UnityEngine.Input.GetKeyDown(KeyCode.F9)) {
            RewiredDump.Dump(modEntry.Path);
            Speaker.Speak("Rewired config dumped.", interrupt: true);
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.F10)) {
            KeybindingsDump.Dump(modEntry.Path);
            Speaker.Speak("Keybindings dumped.", interrupt: true);
        }

        // F12 — speech self-test (moved off F8, which is the game's QuickLoad).
        if (UnityEngine.Input.GetKeyDown(KeyCode.F12)) {
            Speaker.Speak("RTAccess speech test. Backend is " + Speaker.ActiveBackend + ".", interrupt: true);
        }
    }

    private static bool OnUnload(UnityModManager.ModEntry modEntry) {
        EventBus.Unsubscribe(ExplorationEvents.Instance);
        EventBus.Unsubscribe(BarkEvents.Instance);
        EventBus.Unsubscribe(CombatEvents.Instance);
        EventBus.Unsubscribe(WarningReader.Instance);
        EventBus.Unsubscribe(InteractionEvents.Instance);
        Speaker.Stop();
        Speaker.Shutdown();
        HarmonyInstance?.UnpatchAll(HarmonyInstance.Id);
        return true;
    }
}
