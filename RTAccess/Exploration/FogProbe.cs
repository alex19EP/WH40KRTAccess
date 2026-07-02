using Owlcat.Runtime.Visual.FogOfWar;
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// Per-tile fog-of-war query: "has the player ever seen this patch of ground?" — the empty-floor counterpart to the
/// per-entity <c>Entity.IsInFogOfWar</c> the scanner already reads for units/objects. RT keeps NO CPU reveal grid
/// (the culling system only writes per-entity visibility); the one per-cell reveal+explored source is the GPU fog
/// mask <see cref="FogOfWarArea.FogOfWarMapRT"/> — the very RenderTexture the fog shader samples. We read one texel
/// of it synchronously on demand (a keypress), map world XZ → texel via the fog area's own world bounds (the same
/// transform the shader's <c>_FogOfWarMask_ST</c> encodes), and threshold the channels. A single-pixel readback is
/// ~sub-millisecond and the 1×1 destination texture is reused, so a query costs no per-call allocation.
///
/// <para><b>Channel semantics</b> (derived from the decompiled <c>Owlcat.Runtime.Visual</c> fog pipeline — see
/// <c>docs/plans/echoing-charting-lovelace.md</c> §B4): on the LIVE <c>R8G8B8A8_UNorm</c> mask, <b>green</b>
/// accumulates "ever explored" (it is the only channel the coop <c>FogOfWarAreaCompressor</c> transfers, and the
/// channel <c>RestoreFogOfWarMask</c>/the history-copy pass persist across frames and save/load), and <b>red</b> is
/// "currently visible". Threshold at 128 = 0.5 on the linear live read. Blue/alpha are border/blur/scratch — ignore.</para>
///
/// <para><b>LIVE-PROBE PASSED</b> (2026-07-02, via <see cref="Dump"/> on VoidshipOfficersDeck): a grid sample around
/// the party confirmed the party's own tile reads <c>(255,255,0)</c> → Visible (red), an explored ring reads
/// <c>(0,255,·)</c> → Explored (green), and outer tiles read <c>(0,0,·)</c> → NeverSeen (blue ignored) — i.e. green =
/// explored-ever, red = currently-visible, threshold 128, and NO vertical flip. The explored tiles were restored from
/// the save on load, so green also round-trips save/load. Both open questions (green semantics + <c>ReadPixels</c> on
/// this <c>RTHandle</c>) are settled. Fallbacks remain documented in plan §B4 (Blit-to-scratch; WrathAccess FogExplored)
/// should a future area/pipeline change break the direct read.</para>
/// </summary>
internal static class FogProbe
{
    internal enum FogState
    {
        /// <summary>No active fog area, fog disabled (e.g. ship interiors), or point off the fog bounds — treated as revealed.</summary>
        NoFow,
        /// <summary>Genuinely never seen — the only state that reads as "unexplored".</summary>
        NeverSeen,
        /// <summary>Seen before but not currently in line of sight.</summary>
        Explored,
        /// <summary>Currently lit / in a revealer's sight.</summary>
        Visible,
    }

    // 0.5 on a LINEAR live-RT read. NOTE: do NOT reuse this for the persisted .fog / RequestData() bytes — those go
    // through R8G8B8_SRGB, which pushes the 0.5-linear boundary to ~byte 188. We read the live RT, so 128 is correct.
    private const byte RevealThreshold = 128;

    // ReadPixels' source rect has a bottom-left origin and the fog RT stores row 0 = bounds min.z (also bottom-up),
    // so col/row map straight through — CONFIRMED by the live probe (2026-07-02: the party's own tile read Visible at
    // its computed texel with a spatially-coherent explored/never-seen surround). Kept as a knob (static, not const,
    // so no dead-branch warning) in case a future area/pipeline flips vertically: set true → row := height-1-row.
    private static readonly bool FlipRow = false;

    private static Texture2D _px;   // reused 1×1 readback destination; no per-call allocation

    /// <summary>False only when the tile has genuinely never been seen. Fog-off areas and off-map points read as
    /// explored, matching the engine forcing reveal where there is no fog. Any failure is swallowed → explored
    /// (we never emit a false "unexplored").</summary>
    internal static bool IsExplored(Vector3 world) => Classify(world) != FogState.NeverSeen;

    /// <summary>Full reveal state at a world point. Best-effort: a missing/inactive fog area, an off-bounds point,
    /// or any read failure returns <see cref="FogState.NoFow"/> (treated as revealed by callers).</summary>
    internal static FogState Classify(Vector3 world)
    {
        try
        {
            var area = FogOfWarArea.Active;
            if (area == null || !area.isActiveAndEnabled) return FogState.NoFow;   // fog disabled (e.g. ship deck)
            var rt = (RenderTexture)area.FogOfWarMapRT;                            // game casts the RTHandle likewise
            if (rt == null) return FogState.NoFow;

            var wb = area.GetWorldBounds();                                        // = Bounds shifted by transform.position
            if (wb.size.x <= 0f || wb.size.z <= 0f) return FogState.NoFow;
            float u = (world.x - wb.min.x) / wb.size.x;                            // == world.x * _FogOfWarMask_ST.x + .z
            float v = (world.z - wb.min.z) / wb.size.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return FogState.NoFow;       // off-map → revealed

            var c = ReadTexel(rt, u, v);
            if (c.r >= RevealThreshold) return FogState.Visible;                   // currently lit (red)
            if (c.g >= RevealThreshold) return FogState.Explored;                  // ever-seen (persisted green)
            return FogState.NeverSeen;
        }
        catch { return FogState.NoFow; }
    }

    // Synchronous 1×1 readback of the fog RT. The query is keypress-driven and must answer THIS frame, so we accept
    // the ~sub-ms single-pixel GPU stall rather than AsyncGPUReadback's frames of latency + callback plumbing (that
    // path — FogOfWarArea.RequestData() — reads the WHOLE up-to-2048² RT and only runs at save/area-transition). If a
    // direct sub-rect ReadPixels ever rejects this RTHandle-backed RT, the fallback is a Graphics.Blit of the target
    // texel into a 1×1 ARGB32 scratch then ReadPixels that (proven live in WrathAccess) — see plan §B4.
    private static Color32 ReadTexel(RenderTexture rt, float u, float v)
    {
        int w = rt.width, h = rt.height;                                           // NON-square: X→width, Z→height
        int col = Mathf.Clamp((int)(u * w), 0, w - 1);
        int row = Mathf.Clamp((int)(v * h), 0, h - 1);
        if (FlipRow) row = h - 1 - row;

        if (_px == null) _px = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false, linear: true);
        var prev = RenderTexture.active;
        try
        {
            RenderTexture.active = rt;
            _px.ReadPixels(new Rect(col, row, 1, 1), 0, 0);                        // no Apply() needed for GetPixel
            return _px.GetPixel(0, 0);
        }
        finally { RenderTexture.active = prev; }
    }

    /// <summary>Dev-only readout for the one-time live probe (invoke via the dev server /eval): dumps the raw mask
    /// bytes, the uv/texel mapping, and the classification at a world point, so a maintainer can confirm
    /// green = explored / red = visible and the vertical orientation before the readout is trusted in-game. See the
    /// live-probe checklist in plan §B4.</summary>
    internal static string Dump(Vector3 world)
    {
        var area = FogOfWarArea.Active;
        if (area == null) return "FogProbe: no active FogOfWarArea (fog off / not an outdoor-style area)";
        var rt = (RenderTexture)area.FogOfWarMapRT;
        if (rt == null) return "FogProbe: FogOfWarMapRT is null";
        var wb = area.GetWorldBounds();
        float u = (world.x - wb.min.x) / wb.size.x;
        float v = (world.z - wb.min.z) / wb.size.z;
        int w = rt.width, h = rt.height;
        int col = Mathf.Clamp((int)(u * w), 0, w - 1);
        int row = FlipRow ? h - 1 - Mathf.Clamp((int)(v * h), 0, h - 1) : Mathf.Clamp((int)(v * h), 0, h - 1);
        var c = ReadTexel(rt, u, v);
        return $"FogProbe @ world({world.x:F1},{world.z:F1}) uv=({u:F3},{v:F3}) texel=({col},{row}) of {w}x{h} " +
               $"rgba=({c.r},{c.g},{c.b},{c.a}) → {Classify(world)}";
    }
}
