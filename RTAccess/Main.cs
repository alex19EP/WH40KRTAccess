using HarmonyLib;
using System.Reflection;
using Kingmaker.PubSubSystem.Core;
using UnityEngine;
using UnityModManagerNet;
using RTAccess.Speech;
using RTAccess.Accessibility;

namespace RTAccess;

public static class Main {
    internal static Harmony HarmonyInstance;
    /// <summary>Mod logger — forwards to UMM's ModLogger AND mirrors to rtaccess_log.txt (see <see cref="ModLog"/>).</summary>
    internal static ModLog Log;
    /// <summary>The mod's install directory (UMM modEntry.Path) — root for bundled assets (locale JSON, …).</summary>
    internal static string ModDir;
    /// <summary>Master enable flag, toggled from the UMM UI (OnToggle). Defaults true (a freshly loaded mod is
    /// enabled). The <see cref="Speaker"/> chokepoint gates all speech on this, so a UMM-disabled mod goes fully
    /// silent even though its EventBus subscribers / Harmony postfixes stay wired.</summary>
    internal static bool Enabled = true;
    // Engage focus mode once, on the first frame the game's keyboard exists (it doesn't yet at Load).
    private static bool _bootFocusPending = true;

    public static bool Load(UnityModManager.ModEntry modEntry) {
        ModDir = modEntry.Path;
        // Wrap UMM's logger so everything we log also lands in rtaccess_log.txt (there is no UnityModManager.log
        // on disk here). Set up before the try below so an early init crash is captured too.
        Log = new ModLog(modEntry.Logger, modEntry.Path);
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
            // The in-code defaults (exploration + audio categories) live in Settings.Defaults — Load stays orchestration.
            Settings.Defaults.Register();
            // UI = per-announcement settings (global toggles) + the graph control-type override registry
            // (ControlTypes.All). Creates the "announcements" + "ui" categories under the settings Root —
            // declared BEFORE Initialize like everything else.
            UI.Announcements.AnnouncementRegistry.RegisterDefaults();
            // The graph announcer consults the same announcement settings (per control type + per kind).
            UI.Graph.GraphAnnouncer.PartFilter = (type, part) =>
                UI.Announcements.AnnouncementRegistry.PartEnabled(type?.Key, part.Kind);
            // Group headers speak their expanded/collapsed state (localized; same words the legacy tree used).
            UI.Graph.GraphAnnouncer.ExpandedStateText = expanded =>
                Loc.T(expanded ? "role.expanded" : "role.collapsed");
            UI.Graph.GraphAnnouncer.PositionText = (index, count) =>
                Loc.T("nav.position", new { index, count });
            Settings.ModSettings.Initialize(System.IO.Path.Combine(Application.persistentDataPath, "RTAccess"));
            // One-time schema migration: [ElementSettingsKey] moved three shipped proxies' override paths
            // (selection_item/choice_option → radio_button, settings_tab → tab); carry saved user overrides
            // from the old paths so they don't silently become inert unknown keys. AFTER Initialize (needs
            // the path index + the loaded file).
            UI.Announcements.AnnouncementRegistry.MigrateLegacyElementKeys();
            // Parallel accessible-UI framework (Phase 2): register input actions + the screens whose
            // ScreenManager resolves over RootUiContext each frame (MainMenu first).
            Input.InputBindings.RegisterDefaults();
            Screens.ScreenManager.Initialize();
            HarmonyInstance = new Harmony(modEntry.Info.Id);
            PatchAllIsolated();
            // Voice world-exploration state (chosen interactable + area/loading transitions). One persistent
            // subscriber for the whole session; unsubscribed in OnUnload.
            EventBus.Subscribe(ExplorationEvents.Instance);
            // Voice barks — overhead speech bubbles + subtitles (see BarkEvents / [[rt-bark-system]]). Also a
            // persistent session subscriber; unsubscribed in OnUnload.
            EventBus.Subscribe(BarkEvents.Instance);
            // Voice action-refusal toasts ("not enough action points"). Passive event stream → queued speech.
            // Persistent session subscriber; unsubscribed in OnUnload. (Combat events — damage / heal / death /
            // buffs — come from the game log via LogTap into CombatEvents' queue; CombatEvents itself is pumped
            // by CombatEvents.Tick from OnUpdate and is no longer an EventBus subscriber.)
            EventBus.Subscribe(WarningReader.Instance);
            // Voice conviction (soul-mark) shifts — the one dialogue notification the game never logs, so it
            // can't ride LogTap like the rest; everything else in the message log is voiced by LogTap (the
            // universal AddMessage tap) into CombatEvents' queue. Persistent subscriber; unsubscribed below.
            EventBus.Subscribe(ConvictionEvents.Instance);
            // Voice system-map travel state (ship movement, scan results, research %, proximity cues). The
            // per-frame proximity poll rides OnUpdate (SpaceEvents.Tick). Persistent subscriber; unsubscribed
            // below. See docs/plans/orbital-listing-wilkes.md (M1).
            EventBus.Subscribe(SpaceEvents.Instance);
            // Voice sector-map / warp-travel state (entering/leaving warp, pause/resume, scan reveals, route
            // charting) — the SpaceEvents sibling for the GlobalMap layer. Persistent subscriber; unsubscribed
            // below. See docs/plans/warp-sector-map-accessibility.md.
            EventBus.Subscribe(WarpEvents.Instance);
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
        modEntry.OnToggle = OnToggle;
        Log.Log("RTAccess loaded. Speech backend: " + Speaker.ActiveBackend);
        return true;
    }

    // Enable/disable from the UMM UI. Disabling must leave the player with a VANILLA keyboard AND silence: flip the
    // Enabled flag (the Speaker chokepoint then drops every passive line — barks/warnings/conviction/quest/service-
    // window/settings events keep firing on their EventBus subs + Harmony postfixes, but nothing reaches the synth),
    // drop focus mode (so the KeyboardArbitration prefix stops claiming chords), cut any in-progress line, and hand
    // the game's service-window keys back to their bare letters (GameKeybinds.Revert un-does the Ctrl+letter rebind
    // it persisted). Re-enabling clears the gate + re-engages focus mode; OnUpdate re-applies the rebind (Revert
    // cleared GameKeybinds' applied-guard).
    private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled) {
        try {
            Enabled = enabled;
            FocusMode.Set(enabled);
            if (!enabled) {
                Input.GameKeybinds.Revert();
                Speaker.Stop(); // cut the current line immediately; the Enabled gate blocks future ones
                StopExplorationAudio(); // silence the looping wall-tone / sonar beds (see below)
            }
        } catch (Exception e) {
            Log.Error("OnToggle failed: " + e);
        }
        return true;
    }

    // Silence the looping spatial-audio beds. The Enabled flag gates SPEECH, but the NAudio output thread keeps
    // playing whatever voices sit in the mixer even after UMM stops calling OnUpdate — so a disabled/unloaded mod
    // would otherwise leave the wall-tone drones sounding with no way to stop them short of re-enabling. WallTones.Reset
    // removes the drone voices and re-arms a rebuild on re-enable (the same path an area change uses); SpatialSources
    // stops chasing tracked sonar one-shots (they tail out on their own). Both features ship off by default.
    private static void StopExplorationAudio() {
        try { Exploration.WallTones.Reset(); } catch (Exception e) { Log?.Error("StopExplorationAudio walltones: " + e); }
        try { Audio.SpatialSources.Clear(); } catch (Exception e) { Log?.Error("StopExplorationAudio spatial: " + e); }
    }

    // Game.Keyboard's getter constructs KeyboardAccess, which dereferences UISettingsRoot.Instance — null
    // for a window during load (before the settings root is ready), so the getter throws rather than
    // returning null. Swallow that so the per-frame tick isn't broken while we wait to engage focus mode.
    private static bool KeyboardReady() {
        try { return Kingmaker.Game.Instance?.Keyboard != null; }
        catch { return false; }
    }

    // Patch each [HarmonyPatch]-attributed class INDEPENDENTLY (mirrors PatchAll's type set, but per-class) so a
    // single target that a game update renamed/reshaped throws only for its own tap — logged and skipped — instead
    // of aborting the whole PatchAll and bricking the mod at boot. CreateClassProcessor(type).Patch() is a no-op
    // (returns null) for non-patch types, so iterating every type is safe. TargetMethods()/bulk classes are handled
    // by the class processor. A catastrophic "nothing patched" is surfaced loudly.
    private static void PatchAllIsolated() {
        int classes = 0, methods = 0, failed = 0;
        foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly())) {
            try {
                var patched = HarmonyInstance.CreateClassProcessor(type).Patch();
                if (patched != null && patched.Count > 0) { classes++; methods += patched.Count; }
            } catch (Exception e) {
                failed++;
                Log.Error("Harmony patch class '" + type.FullName + "' failed to apply (that feature is degraded, mod continues): " + e);
            }
        }
        if (classes == 0)
            Log.Error("Harmony: NO patch classes applied — the mod is largely inert (" + failed + " class(es) threw).");
        else
            Log.Log("Harmony: applied " + classes + " patch class(es) / " + methods + " method(s)" +
                    (failed > 0 ? "; " + failed + " class(es) failed and were skipped." : "."));
    }

    // Log-once-per-signature guard around a per-frame tick: a throw in one subsystem is caught and logged (only the
    // first time each distinct failure is seen, so a persistent per-frame throw can't flood the log) and the rest of
    // the frame's ticks still run. Mirrors Screens.ScreenManager.Safe. Never let one subsystem mute the whole mod.
    // Key on (label, exception TYPE) only — NOT e.Message. A persistent per-frame throw whose message embeds varying
    // data (an entity name / index) would otherwise slip the dedup and re-flood the log (the storm this prevents) and
    // grow the set unboundedly.
    private static readonly System.Collections.Generic.HashSet<string> _tickErrorsSeen = new System.Collections.Generic.HashSet<string>();
    private static void Safe(Action tick, string label) {
        try { tick(); }
        catch (Exception e) {
            string sig = label + "|" + e.GetType().Name;
            if (_tickErrorsSeen.Add(sig))
                Log?.Error("OnUpdate tick '" + label + "' threw (identical errors suppressed hereafter): " + e);
        }
    }

    private static void OnUpdate(UnityModManager.ModEntry modEntry, float dt) {
#if DEBUG
        // Run any queued /eval jobs on the main thread before our own per-frame work (dev harness).
        Safe(() => RTAccess.Dev.DevServer.Instance.Pump(), "DevServer.Pump");
#endif

        // Pick up a mid-session game-language change so our framework strings follow it.
        Safe(() => Localization.LocalizationManager.Tick(), "Localization");

        // Flush queued combat-event lines (log taps + lifecycle/threshold cues) in arrival order (passive →
        // never interrupt). WarningReader is reactive (no tick).
        Safe(() => CombatEvents.Instance.Tick(), "CombatEvents");

        // Pause / unpause edges of the global clock — Space pause, the three autopauses, scripted pauses, and
        // the silent force-unpause on combat entry (main-HUD audit #5). Passive → queued. See PauseAnnouncer.
        Safe(() => Accessibility.PauseAnnouncer.Tick(), "PauseAnnouncer");

        // Loading screen: announce the tip/description on show (via a view postfix) and the post-load
        // "press any key to continue" prompt (a silent barrier for blind players). Edge-detected; any key dismisses it.
        Safe(() => LoadingScreenReader.Update(), "LoadingScreenReader");

        // System-map proximity cues (the game's three HUD interference icons, edge-detected). No-op off the map.
        Safe(() => SpaceEvents.Instance.Tick(), "SpaceEvents");

        // Sector-map / warp-travel edge-state housekeeping (clears transient pause/scan flags off the map). The
        // warp narration itself is event-driven. No-op off the sector map.
        Safe(() => WarpEvents.Instance.Tick(), "WarpEvents");

        // The live world registry: diff the entity pools into stable per-entity scan proxies (units + map objects +
        // placed area effects), raising Added/Removed. Ticked BEFORE the input tick so the scanner's handlers read a
        // current-frame registry; the persistent proxies are what future object/sonar cues attach to. See
        // RTAccess.Exploration.WorldModel.
        Safe(() => Exploration.WorldModel.Tick(), "WorldModel");

        // The room map: segment the area's walkable grid into orientation ROOMS ("Room 12, large hall") via a
        // persistence watershed, and announce room changes (dwell-gated). Self-latches its build on area-part change
        // (the grid streams in late) and rebuilds once per load. See RTAccess.Exploration.RoomMap.
        Safe(() => Exploration.RoomMap.Tick(), "RoomMap");

        // Ambient sonar sweep: ping the perceivable things around the shared cursor with their recorded per-type
        // stems (the "feel the room" layer). Gated OFF by default (exploration.sonar); reads the current-frame
        // WorldModel registry above. See RTAccess.Exploration.Sonar.
        Safe(() => Exploration.Sonar.Tick(dt), "Sonar");

        // Fog-boundary cue: a brief tone as the shared cursor crosses the edge of the party's current sight
        // (into fog / back into view). ON by default; fog-gated so it's inherently visual-parity-safe. See FogCue.
        Safe(() => Exploration.FogCue.Tick(dt), "FogCue");

        // Live-track every sonar ping still sounding: re-pan / re-attenuate it in 3D against the moving cursor +
        // the item's nearest edge until it drains, so a source follows you instead of freezing. See SpatialSources.
        Safe(() => Audio.SpatialSources.Tick(), "SpatialSources");

        // Directional wall tones: the continuous "shape of the room" bed — four looping cardinal voices whose volume
        // rises as a wall nears the shared cursor. Ships OFF (Ctrl+F1 toggles Off/When-moving/Continuous); volume
        // slews ~0.5 s so a tile step doesn't jump it. See RTAccess.Exploration.WallTones.
        Safe(() => Exploration.WallTones.Tick(dt), "WallTones");

        // Ability targeting: the moment an action-bar ability arms (SetAbility → aiming), hand the keyboard from the
        // HUD to the cursor/scanner so the player can commit the aim (Enter at the cursor, I on the selection,
        // Backspace to cancel). See RTAccess.Exploration.Targeting.
        Safe(() => Exploration.Targeting.Tick(), "Targeting");

        // Accessible pre-combat deployment: while the game's preparation turn is active, the tile cursor's Enter
        // places the selected character and B starts the battle; announce entry (controls + budget) / exit. See
        // RTAccess.Exploration.DeploymentMode.
        Safe(() => Exploration.DeploymentMode.Tick(), "DeploymentMode");

        // ---- Parallel accessible-UI framework (Phase 2) ----
        // Engage focus mode once the keyboard exists. Focus mode no longer blanket-mutes the game keyboard;
        // the KeyboardArbitration patch suppresses only the chords the mod claims each frame (see FocusMode /
        // RTAccess.Accessibility.KeyboardArbitration). The patch targets the KeyboardAccess method, so it
        // automatically covers a fresh KeyboardAccess after a scene reload — no re-assert tick needed.
        if (_bootFocusPending && KeyboardReady()) {
            _bootFocusPending = false;
            FocusMode.Set(true);
        }

        // Vacate the game's bare-letter service-window keys onto Ctrl+letter (C/I/J/M/L/Y/V/B/N), via the game's
        // own keybinding-settings path, so the mod's exploration verbs can own the bare letters and the game's
        // window/tutorial hints auto-update. Idempotent + self-guarded until settings/keyboard are ready. See
        // RTAccess.Input.GameKeybinds and docs/input-system-architecture-review.md.
        Safe(() => Input.GameKeybinds.ApplyWindowOpenerRebinds(), "GameKeybinds"); // vacate C/I/J/… onto Ctrl+letter
        Safe(() => Input.InputManager.Tick(), "InputManager");     // poll our input → navigator (UI) / handlers
        Safe(() => Screens.ScreenManager.Tick(), "ScreenManager"); // resolve the screen stack + attach the navigator
        Safe(() => UI.Navigation.TickTypeahead(), "Typeahead");    // typed letters → type-ahead search (after dispatch)

        // Holographic movement simulation: while it's the player's turn in TB combat, mirror the game's own
        // hover-prediction pipeline at the tile cursor — automatic inside the movable area, anywhere SEEN while
        // Ctrl is held (the sighted Ctrl+hover force) — by writing VirtualPositionController.VirtualPosition, so
        // every position-dependent readout (cover overtips, hit chances, ability range, our vantage/aim reads)
        // answers "as if I stood there". Runs after the input/screen ticks so it reconciles against this frame's
        // cursor + focus state. See RTAccess.Combat.HoloSim.
        Safe(() => Combat.HoloSim.Tick(), "HoloSim");

        // Announce the primary selection when it changes from a source the keyboard paths don't already speak
        // (mouse click, HUD portrait, or the game re-selecting on its own). Deduped against the explicit selectors
        // (which set the same guard) and silenced in turn-based combat. See RTAccess.Accessibility.SelectionAnnouncer.
        Safe(() => SelectionAnnouncer.Tick(), "SelectionAnnouncer");

        // Announce a weapon-set swap (index + weapons now in hand). The game's ChangeWeaponSet (now on Ctrl+X after
        // the P/X/R relocation) gives no audio; this polls the controlled unit's active set and speaks the change.
        Safe(() => WeaponSetAnnouncer.Tick(), "WeaponSetAnnouncer");

        // Passively announce when a party member becomes eligible to level up (the game only shows a silent
        // portrait badge). Edge-detected per unit, out-of-combat, passive. See RTAccess.Accessibility.LevelUpAnnouncer.
        Safe(() => LevelUpAnnouncer.Tick(), "LevelUpAnnouncer");

        // Service windows currently open via the mod's own HUD nav buttons (InGameScreen WindowButtons →
        // HandleOpenWindowOfType); ServiceWindowAnnounce voices the open. Their bare game keys (C/I/J/M/L/Y/V/B/N)
        // are muted while FocusMode holds KeyboardAccess.Disabled — the GameKeybinds rebind above moves them to
        // Ctrl+letter so they come back (and free the bare letters for exploration) once the blanket mute is
        // replaced by per-chord arbitration.

        // Party member-select (Shift+A/D + Alt+1..6) is now REGISTERED in the Exploration input category and
        // driven by InputManager.Tick above — no direct poll here. Registering it (rather than raw-polling) is
        // what lets the keyboard-arbitration patch see the claim and suppress the game's own Prev/NextCharacter
        // + SelectCharacter on the same chords, so they don't double-fire. See RTAccess.Accessibility.PartyHotkeys.

        // The scanner / review cursor (the self-built replacement for the engine's mouse-mode-dead interactable
        // ring) is now registered in the Exploration input category and driven by InputManager.Tick above — no
        // direct poll here. The old engine-ring cycler (ExplorationNav) has been retired. PageUp/Down browse a
        // categorized, distance-sorted list of the area; Ctrl+PageUp/Down change category; , . N M cycle nearest
        // party/enemy/neutral/object; I interacts with the selection, Home/Slash plants the cursor on it, X reads
        // location, ' / Y inspect the cursor / the selection, P reads the party. See RTAccess.Exploration.Scanner /
        // RTAccess.Input.InputBindings.

        // Landmark cycling ([ / ] / \) and the CharGen phase re-announce (Ctrl+P) are now REGISTERED actions
        // driven by InputManager.Tick above — no direct poll here. See RTAccess.Input.InputBindings.

        // The tile explorer (the always-active virtual grid cursor) is registered in the Exploration input category
        // and driven by InputManager.Tick above — no direct poll here, and no toggle. Arrow keys step it tile by
        // tile (Shift+arrows are the shadow-immune slot), C recenters on the party, Delete re-reads the tile,
        // Backspace issues the guarded move-to, and Enter / KeypadEnter interact with the nearest interactable to the
        // cursor. See RTAccess.Accessibility.TileExplorer / RTAccess.Input.InputBindings.

        // Diagnostics keys (F9 Rewired dump / F10 keybindings dump / F12 speech self-test) are now REGISTERED
        // Global actions driven by InputManager.Tick above — no direct poll here. See RTAccess.Input.InputBindings.
    }

    private static bool OnUnload(UnityModManager.ModEntry modEntry) {
        Enabled = false;
        // Restore the player's Controls config: removing the mod while enabled must NOT leave C/I/J/M/L/Y/V/B/N/U/X
        // permanently rebound to Ctrl+letter (the rebind was persisted to disk). Do this first, while the game's
        // settings/keyboard systems are still up.
        try { Input.GameKeybinds.Revert(); } catch (Exception e) { Log?.Error("OnUnload Revert failed: " + e); }
        FocusMode.Set(false);
        StopExplorationAudio(); // stop the looping wall-tone / sonar beds before we tear the rest down
#if DEBUG
        RTAccess.Dev.DevServer.Instance.Stop(); // release the port-8772 socket + join the listener thread (hot-reload safe)
#endif
        EventBus.Unsubscribe(ExplorationEvents.Instance);
        EventBus.Unsubscribe(BarkEvents.Instance);
        EventBus.Unsubscribe(WarningReader.Instance);
        EventBus.Unsubscribe(ConvictionEvents.Instance);
        EventBus.Unsubscribe(SpaceEvents.Instance);
        EventBus.Unsubscribe(WarpEvents.Instance);
        Speaker.Stop();
        Speaker.Shutdown();
        HarmonyInstance?.UnpatchAll(HarmonyInstance.Id);
        return true;
    }
}
