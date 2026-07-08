using System.IO;
using Kingmaker.Pathfinding; // CustomGridNodeBase, GraphParamsMechanicsCache, GetNearestNodeXZ
using RTAccess.Audio;        // AudioMixer, AudioAssets
using RTAccess.Screens;      // InGameScreen.ExplorationActive
using RTAccess.Settings;
using RTAccess.Speech;       // Speaker (toggle announce)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// Directional <b>wall tones</b> — the continuous ambient "shape of the room" bed. Four seamlessly-looping
/// voices (North / South / East / West) sound at once; only their VOLUMES change, each rising as a wall nears
/// in that cardinal direction from the shared <see cref="MapCursor"/>. Pan is a fixed compass — East hard
/// right, West hard left, North and South both centred and told apart by their differing tone timbres — so a
/// corridor reads as loud-left + loud-right + quiet-ahead, a dead-end swells the forward tone, and an open
/// room goes near-silent on all four. This sonifies GEOMETRY / negative space, which no per-object sonar can
/// convey; it is the layer WrathAccess's soundscape is built on and the biggest gap the maintainer's ear-test
/// flagged in the object-only sonar. Ported from WA's <c>WallToneSystem</c> + its 4-channel looping voice,
/// rebuilt on RT's square grid (docs/plans/echoing-charting-lovelace.md, Phase H).
///
/// Wall distance is a GRID raycast, not a navmesh linecast: step cell-by-cell via
/// <see cref="CustomGridNodeBase.GetNeighbourAlongDirection"/> (checkConnectivity true → null when the edge is
/// CUT, i.e. a wall/fence between tiles, or off-grid), stopping on that or a non-walkable tile. Distance →
/// volume is WA's quadratic proximity curve (<c>t = 1 - dist/Range; t*t</c>) so it bites close in.
///
/// Ticked from <c>Main.OnUpdate</c>. Ships <b>Off</b> by default — the continuous bed is fatiguing, so the
/// maintainer opts in with the toggle key (<see cref="ToggleMode"/>, cycling Off → When moving → Continuous);
/// Off / When moving / Continuous are the three modes. Each voice's volume GLIDES to its new target over ~half a
/// second (exponential slew, <see cref="SmoothTau"/>) so a discrete tile-cursor step doesn't jump the volume with
/// a jarring click; faded (not disposed) to silence in menus/cutscenes / When-moving-idle so it resumes seamlessly.
/// Rides the Phase F resident looping voice (<see cref="AudioMixer.PlayLoop"/>), with WrathAccess's own WAV
/// tone sets under <c>assets/audio/walltones/&lt;set&gt;</c>.
/// </summary>
internal static class WallTones
{
    // Cardinal grid-direction index (matches InteractableDescriber's cover map + CoverVisualizer): N=2 E=1 S=0 W=3.
    // Each voice: (grid dir, WAV file, fixed pan). N/S centred (distinguished by timbre), E right, W left.
    private static readonly (int dir, string file, float pan)[] Voices =
    {
        (2, "north.wav", 0f),
        (0, "south.wav", 0f),
        (1, "east.wav", 1f),
        (3, "west.wav", -1f),
    };

    private const float Range = 5f;        // metres: a wall past this is silent (~3.7 tiles on the 1.35 m grid)
    private const float MoveGrace = 0.25f; // "moving recently" window for the When-moving mode
    // Volume slew time-constant: each frame we close the fraction 1-exp(-dt/tau) of the gap to the target, so a
    // volume change settles (~95 %) in about 3·tau ≈ half a second. Stops a tile-cursor step (which can jump the
    // wall distance a whole cell at once) from snapping the volume with a jarring click. The maintainer's request.
    private const float SmoothTau = 0.16f;

    private static readonly object[] _handles = new object[4];
    private static readonly float[] _curVol = new float[4]; // per-voice smoothed volume the slew glides toward target
    private static string _builtSet;

    private static Vector3 _lastPos;
    private static bool _haveLast;
    private static float _sinceMoved = MoveGrace;

    // Cached fog classification of the cursor tile (main-HUD audit L5) — sampled only when the planted cursor's
    // node changes (rate-capped), mirroring FogCue's readback discipline, so the per-frame tick never blocks.
    private static CustomGridNodeBase _fogNode;
    private static bool _fogNeverSeen;
    private static float _fogTimer;

    private enum Playback { Off, WhenMoving, Continuous }

    private static Playback Mode
    {
        get
        {
            var id = ModSettings.GetSetting<ChoiceSetting>("exploration.walltones")?.Current?.Id;
            return id == "off" ? Playback.Off : id == "when_moving" ? Playback.WhenMoving : Playback.Continuous;
        }
    }

    private static float Volume => (ModSettings.GetSetting<IntSetting>("exploration.walltones_volume")?.Get() ?? 50) / 100f;
    private static string ToneSet => ModSettings.GetSetting<ChoiceSetting>("exploration.walltones_set")?.Current?.Id ?? "1";
    private static float CellSize => GraphParamsMechanicsCache.GridCellSize;

    /// <summary>Per-frame update. Off → release the voices; menus/cutscenes / When-moving-idle → glide to silence
    /// (keep the loops alive for a seamless resume); otherwise slew each cardinal voice's volume toward its
    /// wall-distance target over ~half a second. Never throws out of the update loop.</summary>
    public static void Tick(float dt)
    {
        try
        {
            var mode = Mode;
            if (mode == Playback.Off) { Teardown(); return; }

            TrackMotion(dt);

            bool active = InGameScreen.ExplorationActive && ControlState.HasControl;
            bool play = active && (mode == Playback.Continuous || _sinceMoved < MoveGrace);
            // Not playing, or playing but the cursor has no grid node: glide every voice down to silence rather
            // than cutting it — same slew, so a menu open / a step off the grid fades out instead of clicking.
            var node = play ? CursorNode() : null;
            if (node == null) { FadeOut(dt); return; }

            // Parity gate (main-HUD audit L5): a cursor parked in — or stepped through — never-seen fog must not
            // have the surrounding wall geometry sonified; the sighted map draws pure blackness there. Fade to
            // silence exactly like a menu open. Accepted residual: the 5 m ray can still read a short distance
            // into fog from an explored tile near the boundary (the ray itself is not per-tile fog-checked).
            if (CursorNeverSeen(node, dt)) { FadeOut(dt); return; }

            EnsureBuilt(ToneSet);
            float vol = Volume;
            float k = SmoothK(dt);
            for (int i = 0; i < Voices.Length; i++)
            {
                if (_handles[i] == null) continue;
                float target = Curve(DistanceToWall(node, Voices[i].dir)) * vol;
                _curVol[i] += (target - _curVol[i]) * k; // ~0.5 s glide — no jarring step on a tile-cursor jump
                AudioMixer.Instance.SetVoice(_handles[i], _curVol[i], Voices[i].pan);
            }
        }
        catch (Exception e) { Main.Log?.Error("WallTones.Tick failed: " + e); }
    }

    /// <summary>Cycle the wall-tone playback mode Off → When moving → Continuous → Off, speak the new state, and
    /// persist it. Bound to a key (<c>walltones.toggle</c>) so the maintainer can silence the bed instantly when
    /// it's fatiguing, or bring it back, without opening the settings screen.</summary>
    public static void ToggleMode()
    {
        var s = ModSettings.GetSetting<ChoiceSetting>("exploration.walltones");
        if (s == null) return;
        string next = s.Current?.Id switch
        {
            "off" => "when_moving",
            "when_moving" => "continuous",
            _ => "off",
        };
        s.Set(next);
        Speaker.Speak(Loc.T("walltones.mode." + next), interrupt: true);
    }

    /// <summary>Drop the voices on area change / feature reset so nothing stale survives; also clear the motion
    /// tracker so the first frame in the new area doesn't see a huge cursor jump and briefly play in When-moving.</summary>
    public static void Reset() { Teardown(); _haveLast = false; _sinceMoved = MoveGrace; _fogNode = null; _fogNeverSeen = false; }

    // Is the cursor tile never-seen fog? FogProbe.Classify is a synchronous GPU readback, so it must stay
    // keypress-driven: an UNPLANTED cursor tracks the party (whose own tile is always revealed) and is never
    // probed; a planted cursor is sampled only when its node changes, rate-capped, with the classification
    // cached between moves — the same discipline as FogCue.Tick.
    private static bool CursorNeverSeen(CustomGridNodeBase node, float dt)
    {
        _fogTimer -= dt;
        if (!MapCursor.Has) { _fogNode = null; _fogNeverSeen = false; return false; }
        if (!ReferenceEquals(node, _fogNode) && _fogTimer <= 0f)
        {
            _fogTimer = 0.1f;
            _fogNode = node;
            _fogNeverSeen = FogProbe.Classify((Vector3)node.position) == FogProbe.FogState.NeverSeen;
        }
        return _fogNeverSeen;
    }

    // Grid raycast: walk the cardinal from the cursor node until the edge is cut (wall/fence) or the next tile is
    // non-walkable / off-grid; the wall sits ~half a cell past the last open tile. Returns +inf when clear to Range.
    private static float DistanceToWall(CustomGridNodeBase start, int dir)
    {
        int maxCells = Mathf.CeilToInt(Range / CellSize) + 1;
        var cur = start;
        for (int i = 1; i <= maxCells; i++)
        {
            var next = cur.GetNeighbourAlongDirection(dir); // checkConnectivity default true → null on a cut edge
            if (next == null || !next.Walkable) return (i - 0.5f) * CellSize;
            cur = next;
        }
        return float.PositiveInfinity;
    }

    // 0 (no wall within range) → ~1 (right at the wall), curved so it bites close in. WA's WallToneSystem.Curve.
    private static float Curve(float dist)
    {
        if (dist >= Range || float.IsInfinity(dist)) return 0f;
        float t = 1f - dist / Range;
        return t * t;
    }

    private static CustomGridNodeBase CursorNode()
        => MapCursor.Node ?? MapCursor.Position.GetNearestNodeXZ() as CustomGridNodeBase;

    private static void TrackMotion(float dt)
    {
        var p = MapCursor.Position;
        if (_haveLast && (p - _lastPos).sqrMagnitude > 1e-4f) _sinceMoved = 0f;
        else _sinceMoved += dt;
        _lastPos = p; _haveLast = true;
    }

    private static void EnsureBuilt(string set)
    {
        // Build once per tone set. Marked built even if a stem failed to decode, so a (permanently) missing WAV
        // doesn't teardown+rebuild the surviving voices every frame — which would restart their loop position and
        // stutter. Teardown()/set-change/Off re-arm a rebuild by clearing _builtSet.
        if (_builtSet == set) return;
        Teardown();
        var dir = AudioAssets.WallToneSet(set);
        for (int i = 0; i < Voices.Length; i++)
        {
            // Mono-summed: the wall WAVs are stereo, so a hard E/W pan would otherwise drop a channel (WA reads
            // them mono for the same reason). N/S are centred but summed too, for a consistent centred image.
            var buf = AudioMixer.Instance.LoadFileMono(Path.Combine(dir, Voices[i].file));
            _handles[i] = buf != null ? AudioMixer.Instance.PlayLoop(buf, 0f, Voices[i].pan) : null;
        }
        _builtSet = set;
    }

    // Exponential slew factor for this frame: the fraction of the remaining gap to the target volume to close,
    // so the volume glides in ~0.5 s regardless of how big the jump is (dt-based → frame-rate independent).
    private static float SmoothK(float dt) => 1f - Mathf.Exp(-Mathf.Max(dt, 0f) / SmoothTau);

    // Glide every live voice down to silence over the same slew (menus/cutscenes / idle / off-grid) — kept alive
    // and silent so play resumes without a click. No-op before the voices are built (nothing to fade).
    private static void FadeOut(float dt)
    {
        float k = SmoothK(dt);
        for (int i = 0; i < _handles.Length; i++)
        {
            if (_handles[i] == null) continue;
            _curVol[i] += (0f - _curVol[i]) * k;
            if (_curVol[i] < 1e-4f) _curVol[i] = 0f;
            AudioMixer.Instance.SetVoice(_handles[i], _curVol[i], Voices[i].pan);
        }
    }

    private static void Teardown()
    {
        for (int i = 0; i < _handles.Length; i++)
        {
            if (_handles[i] != null) AudioMixer.Instance.Remove(_handles[i]);
            _handles[i] = null;
            _curVol[i] = 0f; // a rebuilt voice starts silent and fades in — no snap to a stale volume
        }
        _builtSet = null;
    }
}
