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
            // exploration.camera_follow (Off/On, default On) gates the tile-cursor follow-cam (TileExplorer.ScrollTo).
            var explCat = ModSettingsRegistry.EnsureCategory("exploration", "Exploration");
            if (explCat.GetByKey("camera_follow") == null)
                explCat.Add(new BoolSetting("camera_follow", "Camera follows cursor", true, "exploration.camera_follow"));
            // Ambient sonar (Exploration/Sonar.cs) — the first spatial-audio system. GATED OFF by default: audio
            // quality is un-self-verifiable, so it ships silent for the maintainer's ear-tuning pass (Off / When
            // moving / Continuous). See docs/plans/echoing-charting-lovelace.md (audio pass, Phase G).
            if (explCat.GetByKey("sonar") == null)
                explCat.Add(new ChoiceSetting("sonar", "Sonar", new[]
                {
                    new Choice("off", "Off", "overlay.mode.off"),
                    new Choice("when_moving", "When moving", "overlay.mode.when_moving"),
                    new Choice("continuous", "Continuous", "overlay.mode.continuous"),
                }, "off", "exploration.sonar"));
            if (explCat.GetByKey("sonar_volume") == null)
                explCat.Add(new IntSetting("sonar_volume", "Sonar volume", 60, 0, 100, 5, "exploration.sonar_volume"));
            // Fog-of-war boundary cue (Exploration/FogCue.cs) — a brief tone as the cursor crosses the edge of the
            // party's current sight. ON by default: it's a discrete event, not a continuous bed, so it ships live
            // without the ear-tuning pass (no keybind — toggle it here). Pitch/length match WrathAccess's fog wavs.
            if (explCat.GetByKey("fog_cue") == null)
                explCat.Add(new BoolSetting("fog_cue", "Fog boundary cue", true, "exploration.fog_cue"));
            // Room-change announcement (Exploration/RoomMap.cs) — speak "Room 12, large hall" as the party (or the
            // planted cursor) crosses into a differently-classified room. ON by default: a discrete event, dwell-gated
            // so a boundary graze doesn't flap. The label rides the pre-staged overlay.cursor.announce_rooms key.
            if (explCat.GetByKey("announce_rooms") == null)
                explCat.Add(new BoolSetting("announce_rooms", "Announce room changes", true, "overlay.cursor.announce_rooms"));
            // Directional wall tones (Exploration/WallTones.cs) — the continuous "shape of the room" bed: four
            // looping cardinal voices whose volume rises as a wall nears. Ships OFF: the continuous bed is
            // ambient/fatiguing, so the maintainer opts in with the Ctrl+F1 toggle (Off → When moving →
            // Continuous, same chord as WrathAccess) and the volume defaults low. See the audio pass, Phase H.
            if (explCat.GetByKey("walltones") == null)
                explCat.Add(new ChoiceSetting("walltones", "Wall tones", new[]
                {
                    new Choice("off", "Off", "overlay.mode.off"),
                    new Choice("when_moving", "When moving", "overlay.mode.when_moving"),
                    new Choice("continuous", "Continuous", "overlay.mode.continuous"),
                }, "off", "exploration.walltones"));
            if (explCat.GetByKey("walltones_volume") == null)
                explCat.Add(new IntSetting("walltones_volume", "Wall tone volume", 25, 0, 100, 5, "exploration.walltones_volume"));
            if (explCat.GetByKey("walltones_set") == null)
                explCat.Add(new ChoiceSetting("walltones_set", "Wall tone set", new[]
                {
                    new Choice("1", "Set 1", "exploration.walltones_set.1"),
                    new Choice("2", "Set 2", "exploration.walltones_set.2"),
                }, "1", "exploration.walltones_set"));
            // Spatial-audio realism toggles (read by Audio/Spatializer.Cue) — the object sonar's per-source 3D on
            // top of pan: an interaural time delay (headphone left/right sharpness) and a rear low-pass (muffled =
            // behind). Both default ON; separated so the maintainer can A/B each by ear. See the audio pass.
            var audioCat = ModSettingsRegistry.EnsureCategory("audio", "Audio");
            if (audioCat.GetByKey("itd") == null)
                audioCat.Add(new BoolSetting("itd", "Interaural time delay (stereo depth)", true, "audio.itd"));
            if (audioCat.GetByKey("front_back_filter") == null)
                audioCat.Add(new BoolSetting("front_back_filter", "Front/back muffling", true, "audio.front_back_filter"));
        }
    }
}
