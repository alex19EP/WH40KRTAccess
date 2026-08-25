namespace RTAccess.Settings
{
    /// <summary>
    /// In-code settings-tree defaults. Declares the exploration + audio categories and their entries on the
    /// <see cref="ModSettingsRegistry"/> BEFORE <see cref="ModSettings.Initialize"/> runs, so Reindex indexes
    /// them and Load applies any saved values over these defaults. Split out of <see cref="Main.Load"/> (which
    /// stays orchestration); add new feature settings here rather than on the boot path. Idempotent — each
    /// entry is only added when absent, so a re-Register is a no-op.
    /// </summary>
    internal static class Defaults
    {
        public static void Register()
        {
            // speech.voiced_lines — let the game's own voice-over stand in for the mod's reading on the lines it
            // actually recorded (dialogue cues, book-event paragraphs, companion banter, cutscene subtitles).
            // ON by default: reading a voiced line in TTS plays every voiced conversation twice at once, which
            // testers reported as the loudest source of noise in the mod. It suppresses SPEECH ONLY — every line
            // keeps its full text in the graph and reads when you arrow onto it — and the never-recorded parts
            // (the skill-check result) stay spoken. Self-disables where the voice is no substitute for the text:
            // a non-English text locale (the voice-over ships in English only) or a muted voice slider.
            // See RTAccess/Accessibility/VoiceOver.cs.
            var speechCat = ModSettingsRegistry.EnsureCategory("speech", "Speech", "category.speech");
            if (speechCat.GetByKey("voiced_lines") == null)
                speechCat.Add(new BoolSetting("voiced_lines", "Let voice-over replace speech", true, "speech.voiced_lines"));

            // exploration.camera_follow (Off/On, default On) gates the tile-cursor follow-cam (TileExplorer.ScrollTo).
            var explCat = ModSettingsRegistry.EnsureCategory("exploration", "Exploration");
            if (explCat.GetByKey("camera_follow") == null)
                explCat.Add(new BoolSetting("camera_follow", "Camera follows cursor", true, "exploration.camera_follow"));
            // exploration.cursor_mode — the exploration cursor's movement style (Exploration/CursorGlide.cs):
            // tiled (default) = arrows step tile-by-tile with a spoken readout each landing; free = holding the
            // arrows GLIDES a continuous world point at cursor_speed m/s along walkable ground (WrathAccess's
            // continuous cursor), silent while moving — the audio bed carries the picture and whatever the cursor
            // rests inside is spoken on release (hover_announce below). Turn-based combat and deployment ALWAYS
            // use tiles regardless (the grid is the combat substrate); Shift+arrows stay tile steps in both modes.
            if (explCat.GetByKey("cursor_mode") == null)
                explCat.Add(new ChoiceSetting("cursor_mode", "Cursor movement", new[]
                {
                    new Choice("tiled", "Tile stepping", "exploration.cursor_mode.tiled"),
                    new Choice("free", "Free glide", "exploration.cursor_mode.free"),
                }, "tiled", "exploration.cursor_mode"));
            if (explCat.GetByKey("cursor_speed") == null)
                explCat.Add(new IntSetting("cursor_speed", "Glide speed (m/s)", 5, 1, 15, 1, "exploration.cursor_speed"));
            // Object enter/exit earcon (Exploration/ObjectCue.cs) — a one-shot blip as the cursor crosses a unit's
            // or interactable's footprint (WrathAccess's object cue, same shipped wavs). Fires in BOTH cursor modes.
            // ON by default per the audio-ON-by-default policy; hover_announce is the free cursor's resting speech.
            if (explCat.GetByKey("object_cue") == null)
                explCat.Add(new BoolSetting("object_cue", "Object enter/exit cue", true, "exploration.object_cue"));
            if (explCat.GetByKey("object_cue_volume") == null)
                explCat.Add(new IntSetting("object_cue_volume", "Object cue volume", 60, 0, 100, 5, "exploration.object_cue_volume"));
            if (explCat.GetByKey("hover_announce") == null)
                explCat.Add(new BoolSetting("hover_announce", "Announce hover when idle", true, "exploration.hover_announce"));
            // Ambient sonar (Exploration/Sonar.cs) — the first spatial-audio system. ON by default (When moving)
            // as of the audio-ON-by-default policy (2026-07-25): the soundscape is a core accessibility feature, so
            // it ships live once ear-tuned rather than gated off. Off / When moving / Continuous; the maintainer runs
            // When moving. See docs/plans/echoing-charting-lovelace.md (audio pass, Phase G).
            if (explCat.GetByKey("sonar") == null)
                explCat.Add(new ChoiceSetting("sonar", "Sonar", new[]
                {
                    new Choice("off", "Off", "overlay.mode.off"),
                    new Choice("when_moving", "When moving", "overlay.mode.when_moving"),
                    new Choice("continuous", "Continuous", "overlay.mode.continuous"),
                }, "when_moving", "exploration.sonar"));
            if (explCat.GetByKey("sonar_volume") == null)
                explCat.Add(new IntSetting("sonar_volume", "Sonar volume", 60, 0, 100, 5, "exploration.sonar_volume"));
            // Sonar CADENCE + RANGE knobs (read live by Exploration/Sonar.cs) — ported from WrathAccess's tunables
            // so the sweep can be shaped by ear. rest = the silence BETWEEN sweeps (set 0 for a continuous sweep);
            // gap_min/max bound the per-ping spacing (which compresses as the crowd grows); ref/max distance set
            // the volume rolloff and the sense radius. Defaults match WA (rest 400 ms, gaps 100–200 ms) with the
            // radius tightened to WA's ~12 m / 3 m (RT previously sensed to 25 m, a sparser, gappier sweep).
            if (explCat.GetByKey("sonar_rest") == null)
                explCat.Add(new IntSetting("sonar_rest", "Sonar rest between sweeps (ms)", 400, 0, 1500, 50, "exploration.sonar_rest"));
            if (explCat.GetByKey("sonar_gap_min") == null)
                explCat.Add(new IntSetting("sonar_gap_min", "Sonar minimum ping gap (ms)", 100, 30, 400, 10, "exploration.sonar_gap_min"));
            if (explCat.GetByKey("sonar_gap_max") == null)
                explCat.Add(new IntSetting("sonar_gap_max", "Sonar maximum ping gap (ms)", 200, 50, 600, 10, "exploration.sonar_gap_max"));
            if (explCat.GetByKey("sonar_ref_dist") == null)
                explCat.Add(new IntSetting("sonar_ref_dist", "Sonar reference distance (m)", 3, 1, 30, 1, "exploration.sonar_ref_dist"));
            if (explCat.GetByKey("sonar_max_dist") == null)
                explCat.Add(new IntSetting("sonar_max_dist", "Sonar maximum distance (m)", 12, 3, 60, 1, "exploration.sonar_max_dist"));
            // Review-cursor ping (Exploration/Sonar.PlayReview, fired from Scanner.Select) — a one-shot positional
            // sound at the item you land on while cycling the scanner (M / , / . / N / Ctrl+PageUp/Down / O), so you
            // hear WHERE it is, not just its spoken line. Separate from the ambient sweep. ON by default (review.wav)
            // as of the audio-ON-by-default policy; pick tracking.wav or Silent to change. Mirrors WA's overlay.sonar.review_sound.
            if (explCat.GetByKey("sonar_review_sound") == null)
                explCat.Add(new ChoiceSetting("sonar_review_sound", "Review cursor sound", new[]
                {
                    new Choice("silent", "Silent", "exploration.sonar_review_sound.silent"),
                    new Choice("review", "Review ping", "exploration.sonar_review_sound.review"),
                    new Choice("tracking", "Tracking tone", "exploration.sonar_review_sound.tracking"),
                }, "review", "exploration.sonar_review_sound"));
            // Fog-of-war boundary cue (Exploration/FogCue.cs) — a brief tone as the cursor crosses the edge of the
            // party's current sight. ON by default: it's a discrete event, not a continuous bed, so it ships live
            // without the ear-tuning pass (no keybind — toggle it here). Pitch/length match WrathAccess's fog wavs.
            if (explCat.GetByKey("fog_cue") == null)
                explCat.Add(new BoolSetting("fog_cue", "Fog boundary cue", true, "exploration.fog_cue"));
            // Room-change announcement (Exploration/RoomMap.cs) — speak "Room 12, large hall" as the party (or the
            // planted cursor) crosses into a differently-classified room. ON by default: a discrete event, dwell-gated
            // so a boundary graze doesn't flap. The label rides the pre-staged overlay.cursor.announce_rooms key.
            // Internal object names (Accessibility/InteractableDescriber.DevName) — append the scene object's own
            // name, and its blueprint when that is not the generic shared asset, to every interactable's spoken
            // label: "Search point [LadderUp]". OFF by default: it is untranslated developer English. On, it is the
            // only way to tell apart the anonymous interactables RT ships when a designer leaves DisplayName empty
            // (the game shows a sighted player no name for those either).
            if (explCat.GetByKey("dev_names") == null)
                explCat.Add(new BoolSetting("dev_names", "Show internal object names", false, "exploration.dev_names"));
            // Two-axis tile breakdown behind a diagonal bearing (Accessibility/InteractableDescriber.AxisOffset) —
            // "7 tiles, north-east, 6 north, 3 east". ON by default: a 45° compass sector is a wedge covering most
            // of a room, and the breakdown is the only thing that pins the target down. Self-suppressing on cardinal
            // bearings (where the compass word is already exact), so the cost is a few words on diagonals only.
            if (explCat.GetByKey("axis_offsets") == null)
                explCat.Add(new BoolSetting("axis_offsets", "Speak tile offsets on diagonals", true, "exploration.axis_offsets"));
            if (explCat.GetByKey("announce_rooms") == null)
                explCat.Add(new BoolSetting("announce_rooms", "Announce room changes", true, "overlay.cursor.announce_rooms"));
            // Directional wall tones (Exploration/WallTones.cs) — the continuous "shape of the room" bed: four
            // looping cardinal voices whose volume rises as a wall nears. ON by default (When moving) as of the
            // audio-ON-by-default policy — NOT Continuous, since an always-on bed is fatiguing; Ctrl+F1 cycles
            // Off → When moving → Continuous (same chord as WrathAccess) and the volume defaults low. See the audio pass, Phase H.
            if (explCat.GetByKey("walltones") == null)
                explCat.Add(new ChoiceSetting("walltones", "Wall tones", new[]
                {
                    new Choice("off", "Off", "overlay.mode.off"),
                    new Choice("when_moving", "When moving", "overlay.mode.when_moving"),
                    new Choice("continuous", "Continuous", "overlay.mode.continuous"),
                }, "when_moving", "exploration.walltones"));
            if (explCat.GetByKey("walltones_volume") == null)
                explCat.Add(new IntSetting("walltones_volume", "Wall tone volume", 25, 0, 100, 5, "exploration.walltones_volume"));
            if (explCat.GetByKey("walltones_set") == null)
                explCat.Add(new ChoiceSetting("walltones_set", "Wall tone set", new[]
                {
                    new Choice("1", "Set 1", "exploration.walltones_set.1"),
                    new Choice("2", "Set 2", "exploration.walltones_set.2"),
                }, "1", "exploration.walltones_set"));
            // Spatial-audio realism toggles (read by Audio/Spatializer.Cue) — the object sonar's per-source 3D on
            // top of the capped-ILD pan: an interaural time delay (headphone left/right sharpness), a rear
            // high-shelf cut (darker = behind), and a far-ear head-shadow shelf (a head between the ears). All
            // default ON; separated so the maintainer can A/B each by ear. See the audio pass.
            var audioCat = ModSettingsRegistry.EnsureCategory("audio", "Audio");
            // Master output volume — one global attenuation over ALL mod audio (sonar, wall tones, earcons, fog),
            // applied at the mixer's final stage (AudioMixer.Master). Default 100 (no change) so it only ever pulls
            // the whole soundscape down; the per-feature volumes still balance the mix under it.
            if (audioCat.GetByKey("master_volume") == null)
                audioCat.Add(new IntSetting("master_volume", "Master volume", 100, 0, 100, 5, "audio.master_volume"));
            if (audioCat.GetByKey("itd") == null)
                audioCat.Add(new BoolSetting("itd", "Interaural time delay (stereo depth)", true, "audio.itd"));
            if (audioCat.GetByKey("front_back_filter") == null)
                audioCat.Add(new BoolSetting("front_back_filter", "Front/back muffling", true, "audio.front_back_filter"));
            if (audioCat.GetByKey("head_shadow") == null)
                audioCat.Add(new BoolSetting("head_shadow", "Ear shadowing (head between the ears)", true, "audio.head_shadow"));
        }
    }
}
