using Kingmaker;
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// The MAP frame: north as the GAME draws it — up on the local map. Every area part carries a
/// <c>LocalMapRotationDeg</c> (0/90/180/270) and the local map view turns the world-aligned map texture by it
/// (<c>LocalMapBaseView.SetMapRotation</c>, a counter-clockwise UI rotation), so map north = world yaw
/// <c>LocalMapRotationDeg</c> (0 = +Z, 90 = +X). Measured live 2026-10-18 on a rotation-0 part (VoidshipOfficersDeck
/// / Navigator): markers 2.7 m toward +X sat 19 px right, 2.7 m toward +Z sat 18 px up. NOTE this is NOT
/// WrathAccess's <c>LocalMapRotation − 180</c>: WotR's local map renders flipped, RT's does not.
///
/// Spoken bearings, the cursor keys, the wall-tone directions and every mod pan live in this frame, so "the door
/// to the north" agrees with the map a sighted helper sees and with the key that walks there. Internally positions
/// stay world XZ; the conversion happens at the edges (<see cref="Geo.CompassSector"/> for words,
/// <see cref="Audio.Spatializer"/> for pans, the step handlers for keys).
///
/// Off the surface (space combat, the system and sector maps) there is no local map to agree with, so the frame
/// is the world frame (offset 0) and nothing there changes.
/// </summary>
internal static class MapFrame
{
    private static float _offset;
    private static int _frame = -1;

    // The game's grid-direction numbering (CustomGridGraph.neighbourXOffsets / neighbourZOffsets):
    // 0 = S, 1 = E, 2 = N, 3 = W, then the diagonals 4 = SE, 5 = NE, 6 = NW, 7 = SW.
    private static readonly int[] DirX = { 0, 1, 0, -1, 1, 1, -1, -1 };
    private static readonly int[] DirZ = { -1, 0, 1, 0, -1, 1, 1, -1 };

    /// <summary>World yaw of map north in degrees (0 = +Z, 90 = +X): the current area part's local-map rotation,
    /// 0 off the surface or mid-load. Cached per frame — read from the game each new frame.</summary>
    public static float Offset
    {
        get
        {
            if (_frame != Time.frameCount)
            {
                _frame = Time.frameCount;
                _offset = Read();
            }
            return _offset;
        }
    }

    private static float Read()
    {
        try
        {
            var game = Game.Instance;
            if (game?.RootUiContext == null || !game.RootUiContext.IsSurface) return 0f;
            var part = game.CurrentlyLoadedAreaPart;
            return part != null ? (float)(int)part.LocalMapRotationDeg : 0f;
        }
        catch (Exception) { return 0f; }
    }

    /// <summary>A WORLD delta (east = +X, north = +Z) re-expressed in the map frame (map east, map north).</summary>
    public static void ToMap(ref float dx, ref float dz)
    {
        float off = Offset;
        if (off == 0f) return;
        float r = off * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        float e = dx * c - dz * s;
        float n = dx * s + dz * c;
        dx = e; dz = n;
    }

    /// <summary>A map-frame vector (map east, map north) — a held cursor key — turned into a world delta.</summary>
    public static void ToWorld(ref float dx, ref float dz)
    {
        float off = Offset;
        if (off == 0f) return;
        float r = off * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        float x = dx * c + dz * s;
        float z = -dx * s + dz * c;
        dx = x; dz = z;
    }

    /// <summary>Integer variant of <see cref="ToWorld(ref float, ref float)"/> for grid steps; exact, since the
    /// offset is always a quarter turn.</summary>
    public static void ToWorld(ref int dx, ref int dz)
    {
        float fx = dx, fz = dz;
        ToWorld(ref fx, ref fz);
        dx = Mathf.RoundToInt(fx); dz = Mathf.RoundToInt(fz);
    }

    /// <summary>Integer variant of <see cref="ToMap(ref float, ref float)"/> — a grid-cell offset in map terms.</summary>
    public static void ToMap(ref int dx, ref int dz)
    {
        float fx = dx, fz = dz;
        ToMap(ref fx, ref fz);
        dx = Mathf.RoundToInt(fx); dz = Mathf.RoundToInt(fz);
    }

    /// <summary>The world grid direction (the game's 0..7 numbering) that points the way of the map-frame
    /// direction <paramref name="mapDir"/> — e.g. which edge to query for the cover the map calls "north".</summary>
    public static int WorldGridDir(int mapDir)
    {
        if (mapDir < 0 || mapDir > 7) return mapDir;
        int dx = DirX[mapDir], dz = DirZ[mapDir];
        ToWorld(ref dx, ref dz);
        return GridDir(dx, dz, mapDir);
    }

    private static int GridDir(int dx, int dz, int fallback)
    {
        for (int i = 0; i < 8; i++)
            if (DirX[i] == dx && DirZ[i] == dz) return i;
        return fallback;
    }
}
