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
            // exploration.camera_follow (Off/On, default On) gates the tile-cursor follow-cam (TileExplorer.ScrollTo).
            var explCat = Settings.ModSettingsRegistry.EnsureCategory("exploration", "Exploration");
            if (explCat.GetByKey("camera_follow") == null)
                explCat.Add(new Settings.BoolSetting("camera_follow", "Camera follows cursor", true, "exploration.camera_follow"));
            // Ambient sonar (Exploration/Sonar.cs) — the first spatial-audio system. GATED OFF by default: audio
            // quality is un-self-verifiable, so it ships silent for the maintainer's ear-tuning pass (Off / When
            // moving / Continuous). See docs/plans/echoing-charting-lovelace.md (audio pass, Phase G).
            if (explCat.GetByKey("sonar") == null)
                explCat.Add(new Settings.ChoiceSetting("sonar", "Sonar", new[]
                {
                    new Settings.Choice("off", "Off", "overlay.mode.off"),
                    new Settings.Choice("when_moving", "When moving", "overlay.mode.when_moving"),
                    new Settings.Choice("continuous", "Continuous", "overlay.mode.continuous"),
                }, "off", "exploration.sonar"));
            if (explCat.GetByKey("sonar_volume") == null)
                explCat.Add(new Settings.IntSetting("sonar_volume", "Sonar volume", 60, 0, 100, 5, "exploration.sonar_volume"));
            // Fog-of-war boundary cue (Exploration/FogCue.cs) — a brief tone as the cursor crosses the edge of the
            // party's current sight. ON by default: it's a discrete event, not a continuous bed, so it ships live
            // without the ear-tuning pass (no keybind — toggle it here). Pitch/length match WrathAccess's fog wavs.
            if (explCat.GetByKey("fog_cue") == null)
                explCat.Add(new Settings.BoolSetting("fog_cue", "Fog boundary cue", true, "exploration.fog_cue"));
            // Room-change announcement (Exploration/RoomMap.cs) — speak "Room 12, large hall" as the party (or the
            // planted cursor) crosses into a differently-classified room. ON by default: a discrete event, dwell-gated
            // so a boundary graze doesn't flap. The label rides the pre-staged overlay.cursor.announce_rooms key.
            if (explCat.GetByKey("announce_rooms") == null)
                explCat.Add(new Settings.BoolSetting("announce_rooms", "Announce room changes", true, "overlay.cursor.announce_rooms"));
            // Directional wall tones (Exploration/WallTones.cs) — the continuous "shape of the room" bed: four
            // looping cardinal voices whose volume rises as a wall nears. Ships OFF: the continuous bed is
            // ambient/fatiguing, so the maintainer opts in with the Ctrl+F1 toggle (Off → When moving →
            // Continuous, same chord as WrathAccess) and the volume defaults low. See the audio pass, Phase H.
            if (explCat.GetByKey("walltones") == null)
                explCat.Add(new Settings.ChoiceSetting("walltones", "Wall tones", new[]
                {
                    new Settings.Choice("off", "Off", "overlay.mode.off"),
                    new Settings.Choice("when_moving", "When moving", "overlay.mode.when_moving"),
                    new Settings.Choice("continuous", "Continuous", "overlay.mode.continuous"),
                }, "off", "exploration.walltones"));
            if (explCat.GetByKey("walltones_volume") == null)
                explCat.Add(new Settings.IntSetting("walltones_volume", "Wall tone volume", 25, 0, 100, 5, "exploration.walltones_volume"));
            if (explCat.GetByKey("walltones_set") == null)
                explCat.Add(new Settings.ChoiceSetting("walltones_set", "Wall tone set", new[]
                {
                    new Settings.Choice("1", "Set 1", "exploration.walltones_set.1"),
                    new Settings.Choice("2", "Set 2", "exploration.walltones_set.2"),
                }, "1", "exploration.walltones_set"));
            // Spatial-audio realism toggles (read by Audio/Spatializer.Cue) — the object sonar's per-source 3D on
            // top of pan: an interaural time delay (headphone left/right sharpness) and a rear low-pass (muffled =
            // behind). Both default ON; separated so the maintainer can A/B each by ear. See the audio pass.
            var audioCat = Settings.ModSettingsRegistry.EnsureCategory("audio", "Audio");
            if (audioCat.GetByKey("itd") == null)
                audioCat.Add(new Settings.BoolSetting("itd", "Interaural time delay (stereo depth)", true, "audio.itd"));
            if (audioCat.GetByKey("front_back_filter") == null)
                audioCat.Add(new Settings.BoolSetting("front_back_filter", "Front/back muffling", true, "audio.front_back_filter"));
            // UI = per-announcement settings (global toggles) + per-element-type overrides discovered by
            // reflection + the graph control-type registry (ControlTypes.All). Creates the "announcements"
            // + "ui" categories under the settings Root — declared BEFORE Initialize like everything else.
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
            // Voice conviction (soul-mark) shifts — the one dialogue notification the game never logs, so it
            // can't ride LogTap like the rest; everything else in the message log is voiced by LogTap (the
            // universal AddMessage tap) into CombatEvents' queue. Persistent subscriber; unsubscribed below.
            EventBus.Subscribe(ConvictionEvents.Instance);
            // Voice system-map travel state (ship movement, scan results, research %, proximity cues). The
            // per-frame proximity poll rides OnUpdate (SpaceEvents.Tick). Persistent subscriber; unsubscribed
            // below. See docs/plans/orbital-listing-wilkes.md (M1).
            EventBus.Subscribe(SpaceEvents.Instance);
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

    // Enable/disable from the UMM UI. Disabling must leave the player with a VANILLA keyboard: drop focus mode
    // (so the KeyboardArbitration prefix stops claiming chords) and hand the game's service-window keys back to
    // their bare letters (GameKeybinds.Revert un-does the Ctrl+letter rebind it persisted). Re-enabling re-engages
    // focus mode; OnUpdate re-applies the rebind (Revert cleared GameKeybinds' applied-guard).
    private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled) {
        try {
            FocusMode.Set(enabled);
            if (!enabled) Input.GameKeybinds.Revert();
        } catch (Exception e) {
            Log.Error("OnToggle failed: " + e);
        }
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

        // System-map proximity cues (the game's three HUD interference icons, edge-detected). No-op off the map.
        SpaceEvents.Instance.Tick();

        // The live world registry: diff the entity pools into stable per-entity scan proxies (units + map objects +
        // placed area effects), raising Added/Removed. Ticked BEFORE the input tick so the scanner's handlers read a
        // current-frame registry; the persistent proxies are what future object/sonar cues attach to. See
        // RTAccess.Exploration.WorldModel.
        Exploration.WorldModel.Tick();

        // The room map: segment the area's walkable grid into orientation ROOMS ("Room 12, large hall") via a
        // persistence watershed, and announce room changes (dwell-gated). Self-latches its build on area-part change
        // (the grid streams in late) and rebuilds once per load. See RTAccess.Exploration.RoomMap.
        Exploration.RoomMap.Tick();

        // Ambient sonar sweep: ping the perceivable things around the shared cursor with their recorded per-type
        // stems (the "feel the room" layer). Gated OFF by default (exploration.sonar); reads the current-frame
        // WorldModel registry above. See RTAccess.Exploration.Sonar.
        Exploration.Sonar.Tick(dt);

        // Fog-boundary cue: a brief tone as the shared cursor crosses the edge of the party's current sight
        // (into fog / back into view). ON by default; fog-gated so it's inherently visual-parity-safe. See FogCue.
        Exploration.FogCue.Tick(dt);

        // Live-track every sonar ping still sounding: re-pan / re-attenuate it in 3D against the moving cursor +
        // the item's nearest edge until it drains, so a source follows you instead of freezing. See SpatialSources.
        Audio.SpatialSources.Tick();

        // Directional wall tones: the continuous "shape of the room" bed — four looping cardinal voices whose volume
        // rises as a wall nears the shared cursor. Ships OFF (Ctrl+F1 toggles Off/When-moving/Continuous); volume
        // slews ~0.5 s so a tile step doesn't jump it. See RTAccess.Exploration.WallTones.
        Exploration.WallTones.Tick(dt);

        // Ability targeting: the moment an action-bar ability arms (SetAbility → aiming), hand the keyboard from the
        // HUD to the cursor/scanner so the player can commit the aim (Enter at the cursor, I on the selection,
        // Backspace to cancel). See RTAccess.Exploration.Targeting.
        Exploration.Targeting.Tick();

        // Accessible pre-combat deployment: while the game's preparation turn is active, the tile cursor's Enter
        // places the selected character and B starts the battle; announce entry (controls + budget) / exit. See
        // RTAccess.Exploration.DeploymentMode.
        Exploration.DeploymentMode.Tick();

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
        Input.GameKeybinds.ApplyWindowOpenerRebinds();
        Input.InputManager.Tick();        // poll our input → navigator (UI) / handlers
        Screens.ScreenManager.Tick();     // resolve the screen stack from RootUiContext + attach the navigator
        UI.Navigation.TickTypeahead();    // typed letters → type-ahead search (after dispatch)
        UI.Navigation.PumpFocus();        // settled focus → OnFocusEnter (focus-selects for opt-in controls, e.g. save slots)

        // Announce the primary selection when it changes from a source the keyboard paths don't already speak
        // (mouse click, HUD portrait, or the game re-selecting on its own). Deduped against the explicit selectors
        // (which set the same guard) and silenced in turn-based combat. See RTAccess.Accessibility.SelectionAnnouncer.
        SelectionAnnouncer.Tick();

        // Announce a weapon-set swap (index + weapons now in hand). The game's ChangeWeaponSet (now on Ctrl+X after
        // the P/X/R relocation) gives no audio; this polls the controlled unit's active set and speaks the change.
        WeaponSetAnnouncer.Tick();

        // Passively announce when a party member becomes eligible to level up (the game only shows a silent
        // portrait badge). Edge-detected per unit, out-of-combat, passive. See RTAccess.Accessibility.LevelUpAnnouncer.
        LevelUpAnnouncer.Tick();

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
        EventBus.Unsubscribe(ExplorationEvents.Instance);
        EventBus.Unsubscribe(BarkEvents.Instance);
        EventBus.Unsubscribe(CombatEvents.Instance);
        EventBus.Unsubscribe(WarningReader.Instance);
        EventBus.Unsubscribe(InteractionEvents.Instance);
        EventBus.Unsubscribe(ConvictionEvents.Instance);
        EventBus.Unsubscribe(SpaceEvents.Instance);
        Speaker.Stop();
        Speaker.Shutdown();
        HarmonyInstance?.UnpatchAll(HarmonyInstance.Id);
        return true;
    }
}
