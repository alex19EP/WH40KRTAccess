using RTAccess.Audio;    // AudioMixer, AudioAssets
using RTAccess.Input;    // InputManager.Held
using RTAccess.Screens;  // InGameScreen.ExplorationActive
using RTAccess.Settings;
using RTAccess.Speech;   // Speaker
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A one-shot earcon when the shared cursor enters or leaves a thing's footprint (units and interactables;
/// nearest centre wins a tie) — WrathAccess's <c>ObjectCueSystem</c>, playing the same <c>object_enter.wav</c> /
/// <c>object_exit.wav</c> stems already shipped in <c>assets/audio</c>. Enter fires on any change TO a real thing
/// (including swapping straight from one footprint into another); exit fires only when leaving to none. Fires in
/// BOTH cursor modes — a tile step onto a unit blips under its spoken readout, a glide brushes footprints purely
/// by ear.
///
/// Also the free cursor's <b>idle hover announce</b>: gliding is too fast to narrate, so nothing speaks on move —
/// but once no movement key is held, whatever the cursor rests inside is spoken (on key release, or when something
/// walks under the parked cursor). Tile mode already announces every landing, so the announce half only runs while
/// <see cref="CursorGlide.FreeModeActive"/>. Spoken QUEUED: the walk-under case is passive, and after a glide the
/// line follows whatever the release interrupted into silence anyway.
///
/// Identity is the <see cref="ScanItem.Key"/> (the backing engine entity), NOT the proxy reference — RT's
/// <see cref="WorldModel"/> can rebuild proxies while the entity stands still, and a reference compare would
/// re-blip every rebuild. Visibility rides <see cref="ScanItem.IsVisible"/>, the same reveal model the scanner
/// uses, so nothing cues that a sighted player couldn't see; the control gate swallows cutscenes (a parked cursor
/// with scripted units streaming under it would otherwise fire hundreds of blips — WA's hard-won guard).
/// </summary>
internal static class ObjectCue
{
    private const float LevelGap = 3f; // ignore things on another level for the "inside" test (WA's constant)

    private static object _insideKey;  // the thing the cursor is currently inside, or null
    private static object _spokenKey;  // what the idle hover announce last spoke (null = armed)
    private static bool _baselined;    // false until the first active tick (don't blip on (re)activation)

    private static bool Enabled => ModSettings.GetSetting<BoolSetting>("exploration.object_cue")?.Get() ?? true;
    private static bool HoverEnabled => ModSettings.GetSetting<BoolSetting>("exploration.hover_announce")?.Get() ?? true;
    private static float Volume => (ModSettings.GetSetting<IntSetting>("exploration.object_cue_volume")?.Get() ?? 60) / 100f;

    /// <summary>Per-frame: track which footprint the cursor is inside, blip the boundary crossings, and speak the
    /// resting hover in free mode. Never throws out of the update loop.</summary>
    public static void Tick()
    {
        try
        {
            if (!Enabled || !InGameScreen.ExplorationActive || !ControlState.HasControl)
            { _insideKey = null; _spokenKey = null; _baselined = false; return; }

            var c = MapCursor.Position;
            ScanItem inside = null;
            float best = float.MaxValue;
            foreach (var it in WorldModel.Items)
            {
                if (!it.IsVisible) continue;
                // Things with a sonar identity, plus all units (WA's gate) — silent scenery doesn't blip.
                if (!it.IsUnit && !Sonar.HasStem(it.Primary)) continue;
                var p = it.Position;
                if (Mathf.Abs(p.y - c.y) > LevelGap) continue; // another level
                if (!it.Contains(c)) continue;                 // cursor inside the actual footprint
                float dx = p.x - c.x, dz = p.z - c.z, d = dx * dx + dz * dz;
                if (d < best) { best = d; inside = it; }
            }

            var key = inside?.Key;
            if (!_baselined) { _insideKey = key; _spokenKey = key; _baselined = true; return; }
            if (!Equals(key, _insideKey))
            {
                AudioMixer.Instance.PlayFile(AudioAssets.Cue(inside != null ? "object_enter" : "object_exit"), Volume);
                _insideKey = key;
            }

            // Idle hover announce (free mode only — tile landings already speak). While the HUD owns the arrows,
            // freeze the spoken state: a held arrow there is UI nav, and re-arming on it would re-announce on release.
            if (RTAccess.UI.Navigation.HasFocus) return;
            if (!CursorGlide.FreeModeActive || !HoverEnabled) { _spokenKey = null; return; }
            if (MoveKeyHeld() || inside == null) { _spokenKey = null; return; }   // moving / over nothing → re-arm
            if (!Equals(key, _spokenKey))
            {
                Speaker.Speak(inside.DescribeInPlace());
                _spokenKey = key;
            }
        }
        catch (Exception e) { Main.Log?.Error("ObjectCue.Tick failed: " + e); }
    }

    private static bool MoveKeyHeld()
        => InputManager.Held("cursor.up") || InputManager.Held("cursor.down")
        || InputManager.Held("cursor.left") || InputManager.Held("cursor.right")
        || InputManager.Held("cursor.up2") || InputManager.Held("cursor.down2")
        || InputManager.Held("cursor.left2") || InputManager.Held("cursor.right2");
}
