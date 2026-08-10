using Owlcat.Runtime.Visual.FogOfWar;
using Unity.Collections;
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// Area-wide "has the player ever seen this ground?" snapshot — the FULL-GRID companion to the per-texel
/// <see cref="FogProbe"/>. FogProbe's 1×1 readback is right for a single keypress query but the wrong shape for a
/// pass over every walkable cell (thousands of GPU stalls); this instead blits the game's fog mask
/// (<see cref="FogOfWarArea.FogOfWarMapRT"/>) down to one 256² scratch texture and reads it back ONCE, so a whole
/// grid pass costs one readback and then plain array lookups. Ported from WrathAccess's <c>FogExplored</c> (the
/// blit path FogProbe's own docs name as the proven fallback), with RT's live-verified channel semantics
/// (see FogProbe: green = explored-ever and persists through save/load, red = currently visible, linear read,
/// threshold 128, no vertical flip).
///
/// On-demand only, never per-frame: consumers call <see cref="Ensure"/> before a burst of
/// <see cref="IsExplored"/> lookups (a keypress / room-change announce), and the snapshot is TTL-cached so a burst
/// costs one blit. <see cref="IsExplored"/> defaults to <c>true</c> whenever there is no data — no fog area
/// (ship decks), an off-bounds point, or a failed read — so we never falsely report revealed ground as unexplored.
/// </summary>
internal static class FogExplored
{
    private const int N = 256;             // snapshot resolution per axis (cell ≈ boundsSize/256)
    private const byte Threshold = 128;    // 0.5 on the linear live read — FogProbe's verified scale; the blit
                                           // downsample averages border texels, so 0.5 = "mostly explored here"
    private const float TtlSec = 2f;       // key-press bursts reuse one snapshot

    private static byte[] _g;              // latest snapshot's explored channel (N*N, row-major, row 0 = min z)
    private static string _key;            // fog area key (scene name) the snapshot belongs to
    private static Bounds _bounds;         // that area's world bounds (the shader's own uv mapping)
    private static bool _ready;
    private static float _staleAt;

    private static RenderTexture _small;   // reused NxN scratch for the downsample blit
    private static Texture2D _read;        // reused NxN CPU-side readback target

    /// <summary>Snapshot the game's fog mask if the cache is stale. Returns false when there is no fog to read
    /// (no active area / no mask / degenerate bounds) — callers can skip their pass entirely, since every point
    /// then reads as explored anyway. Any failure is swallowed → false.</summary>
    public static bool Ensure()
    {
        try
        {
            var area = FogOfWarArea.Active;
            if (area == null || !area.isActiveAndEnabled) { _ready = false; return false; }
            var rt = (RenderTexture)area.FogOfWarMapRT;  // game casts the RTHandle likewise (see FogProbe)
            if (rt == null) { _ready = false; return false; }

            string key = area.gameObject.scene.name;
            if (string.IsNullOrEmpty(key)) key = area.name;
            if (key != _key) { _key = key; _ready = false; }
            if (_ready && Time.unscaledTime < _staleAt) return true;

            var wb = area.GetWorldBounds();
            if (wb.size.x <= 0f || wb.size.z <= 0f) { _ready = false; return false; }
            _bounds = wb;
            Snapshot(rt);
            _staleAt = Time.unscaledTime + TtlSec;
            return _ready;
        }
        catch { _ready = false; return false; }
    }

    private static void Snapshot(RenderTexture rt)
    {
        // Linear scratch + linear readback keep FogProbe's verified byte scale (an sRGB round-trip would move
        // the 0.5 boundary to ~188 — see the FogProbe threshold note).
        if (_small == null)
        {
            _small = new RenderTexture(N, N, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _small.Create();
        }
        if (_read == null) _read = new Texture2D(N, N, TextureFormat.RGBA32, mipChain: false, linear: true);
        if (_g == null) _g = new byte[N * N];

        var prev = RenderTexture.active;
        try
        {
            Graphics.Blit(rt, _small);                     // GPU downsample of the fog mask to NxN
            RenderTexture.active = _small;
            _read.ReadPixels(new Rect(0, 0, N, N), 0, 0);  // no Apply(): we only read the CPU copy back
        }
        finally { RenderTexture.active = prev; }

        NativeArray<Color32> raw = _read.GetRawTextureData<Color32>(); // view, no managed allocation
        var g = _g;
        int lim = Mathf.Min(raw.Length, g.Length);
        for (int i = 0; i < lim; i++)
        {
            var p = raw[i];
            g[i] = p.g >= p.r ? p.g : p.r; // explored = green; red folded in for the freshest reveal edge
        }
        _ready = true;
    }

    /// <summary>Has this world point ever been revealed? Answers from the latest snapshot — call
    /// <see cref="Ensure"/> first. Defaults to <c>true</c> with no data or off the fog bounds, so revealed
    /// ground is never misreported as unexplored.</summary>
    public static bool IsExplored(Vector3 world)
    {
        var g = _g;
        if (!_ready || g == null) return true;
        var b = _bounds;
        if (b.size.x < 1e-3f || b.size.z < 1e-3f) return true;
        float u = (world.x - b.min.x) / b.size.x;              // same mapping FogProbe verified live
        float v = (world.z - b.min.z) / b.size.z;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return true; // outside the fog area — don't claim unexplored
        int x = Mathf.Clamp((int)(u * N), 0, N - 1);
        int y = Mathf.Clamp((int)(v * N), 0, N - 1);
        return g[y * N + x] >= Threshold;
    }
}
