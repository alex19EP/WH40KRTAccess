using HarmonyLib;
using Kingmaker;
using Kingmaker.GameModes;           // GameModeType
using Kingmaker.Sound;               // DefaultListener, AudioListenerPositionController
using Owlcat.Runtime.Core.Registry;  // ObjectRegistry
using RTAccess.Exploration;          // MapCursor, MapFrame
using RTAccess.Settings;
using UnityEngine;

namespace RTAccess.Audio
{
    /// <summary>
    /// The "virtual head" for the GAME's own 3D audio — move the ears, not the camera. Ported from WrathAccess's
    /// <c>ListenerAnchor</c> + <c>ListenerSourcePatch</c>.
    ///
    /// The game hears everything (footsteps, barks, combat, ambience) from one Wwise listener
    /// (<see cref="DefaultListener"/>), which <see cref="AudioListenerPositionController"/>.LateUpdate snaps onto the
    /// camera every frame. RT's camera rig is mounted looking BACK along its own yaw (measured live 2026-10-18: rig
    /// yaw 78.75 → screen-up = world yaw -101.25), and its yaw is per area and player-rotatable — so the game's ears
    /// face a different way in almost every area. The mod's sounds and compass words are fixed to the map frame
    /// (<see cref="MapFrame"/>), so with the camera at rig yaw 0 a sound the mod pans right the game plays on the
    /// left: the tester's "left and right are reversed".
    ///
    /// While the override is active the controller writes OUR pose instead (prefix, skip original): the ears stand
    /// over the shared cursor (the soundscape's listening point), level, facing map north — one spatial frame for
    /// the mod and the game. Patching at the source, not re-snapping afterwards, is WA's lesson: AkAudioService's
    /// own LateUpdate consumes the transform in the same phase, so a later re-snap was script-ordering luck. When
    /// the override is off (setting "camera", a cutscene / dialogue / game over, off the surface) the original
    /// camera snap runs untouched — vanilla audio with zero cleanup. The camera itself is never touched.
    /// </summary>
    [HarmonyPatch(typeof(AudioListenerPositionController), "LateUpdate")]
    internal static class ListenerAnchor
    {
        /// <summary>Height of the ears above the listening point, in metres (audio.listener_height). The game's
        /// attenuation curves are calibrated for a listener on the camera boom, so ears at head height make
        /// medium-range things sound too close; straight up keeps the compass symmetric.</summary>
        private static float HeightMetres => ModSettings.GetSetting<IntSetting>("audio.listener_height")?.Get() ?? 3;

        /// <summary>The override pose for this frame; false = leave the game's camera snap in charge.</summary>
        internal static bool TryGetOverride(out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = default;
            var game = Game.Instance;
            if (game?.CurrentlyLoadedArea == null || game.RootUiContext == null) return false;

            var choice = ModSettings.GetSetting<ChoiceSetting>("audio.listener")?.Current?.Id ?? "cursor";
            if (choice == "camera") return false;

            // Space (combat, system and sector maps) keeps its own camera-framed mix; the map frame is the world
            // frame there anyway (MapFrame), and the soundscape is on-foot only.
            if (!game.RootUiContext.IsSurface) return false;

            // Cutscenes and dialogue are framed and mixed for the camera, and nobody is navigating.
            if (game.IsModeActive(GameModeType.Cutscene) || game.IsModeActive(GameModeType.Dialog)
                || game.IsModeActive(GameModeType.GameOver) || game.CurrentMode == GameModeType.None)
                return false;

            var at = choice == "party" ? MapCursor.PlayerPosition : MapCursor.ListenPosition;
            pos = at + Vector3.up * HeightMetres;
            rot = Quaternion.Euler(0f, MapFrame.Offset, 0f);   // forward = map north, right ear = map east
            return true;
        }

        private static bool Prefix()
        {
            try
            {
                if (!TryGetOverride(out var pos, out var rot)) return true;
                var listener = ObjectRegistry<DefaultListener>.Instance?.MaybeSingle;
                if (listener == null) return true;
                listener.transform.SetPositionAndRotation(pos, rot);
                return false;   // camera snap skipped: our pose is the frame's single source of truth
            }
            catch (Exception e)
            {
                if (!_reported) { _reported = true; Main.Log?.Error("listener anchor failed: " + e); }
                return true;    // never leave the game's ears unplaced
            }
        }

        private static bool _reported; // a per-frame hook: report the first failure only
    }
}
