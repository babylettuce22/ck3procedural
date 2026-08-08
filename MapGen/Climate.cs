using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Port of js/mapgen/moisture.js and js/mapgen/rain.js.
///
/// Moisture is modelled as one cloud per row marching west to east, gaining moisture over sea
/// and losing it climbing terrain. Rain erosion is a separate raindrop-tracking pass that
/// carves elevation and pools lakes.
/// </summary>
public static class Climate
{
    /// <summary>Port of setMoisture().</summary>
    public static void SetMoisture(WorldGrid w, MapConfig cfg, Rng rng)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int mtn = cfg.Limits.Mountains.Lower;

        for (int y = 0; y < w.Height; y++)
        {
            int moisture = 50;
            int mountainCount = 0;

            for (int x = 0; x < w.Width - 1; x++)
            {
                int current = w.Idx(x, y);
                int next = w.Idx(x + 1, y);

                // updateMoisture()
                w.Moisture[current] = moisture;
                int elevationDiff = w.Elevation[next] - w.Elevation[current];
                if (elevationDiff > 10) moisture = Math.Max(moisture - 1, 0);
                if (w.Elevation[next] <= sea) moisture += 1;

                // adjustCloudForElevation()
                if (w.Elevation[next] > mtn)
                {
                    mountainCount += 1;
                    if (elevationDiff > 0)
                    {
                        moisture = Math.Max(moisture - 1, 0);
                        w.Moisture[next] = moisture;
                    }
                }
                else
                {
                    mountainCount = Math.Max(mountainCount - 1, 0);
                }

                // adjustCloudForMountains() — rain shadow behind a range.
                if (mountainCount > 0 && w.Elevation[next] < mtn)
                    w.Desert[next] = true;

                // markDesertAreas(). Note this assigns unconditionally, so it overwrites the
                // rain-shadow flag set immediately above — latitude wins over orography in
                // ck2rpg. Kept as-is; changing it would visibly alter desert placement.
                bool inDesertLatitude =
                    y > w.DesertPointBottom + rng.Int(1, 10) &&
                    y < w.DesertPointTop + rng.Int(1, 10);
                w.Desert[next] = inDesertLatitude && moisture < 50;
            }
        }
    }

    /// <summary>Port of clearRain().</summary>
    public static void ClearRain(WorldGrid w) => Array.Clear(w.Raindrops);

    /// <summary>Port of shareRain() — a cell pushes a drop to any lower combined-height neighbour.</summary>
    private static void ShareRain(WorldGrid w, int cell, int neighbor)
    {
        int cellCombined = w.Raindrops[cell] + w.Elevation[cell];
        int neighborCombined = w.Raindrops[neighbor] + w.Elevation[neighbor];
        if (cellCombined > neighborCombined && w.Raindrops[cell] > 0)
            w.Raindrops[neighbor] += 1;
    }

    /// <summary>
    /// Port of trackRain(). Walks downhill through *cardinal* neighbours only (the diagonal
    /// pushes are commented out in the JS), never revisiting a cell, accumulating raindrops.
    /// The walk stops at sea level, at the map edge, or after 100k steps.
    /// </summary>
    private static void TrackRain(WorldGrid w, MapConfig cfg, int startX, int startY)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int next = w.Idx(startX, startY);
        var used = new HashSet<int>();
        var candidates = new List<int>(4);
        Span<int> neighbors = stackalloc int[4];
        int count = 0;

        while (true)
        {
            int x = w.X(next), y = w.Y(next);
            candidates.Clear();

            int n = w.CardinalNeighborsOf(x, y, neighbors);
            for (int k = 0; k < n; k++)
                if (!used.Contains(neighbors[k])) candidates.Add(neighbors[k]);

            candidates.Sort((a, b) => w.Elevation[a].CompareTo(w.Elevation[b]));

            used.Add(next);
            count += 1;
            w.Raindrops[next] += count;

            foreach (int c in candidates) ShareRain(w, next, c);

            if (candidates.Count == 0) break;
            next = candidates[0];
            if (w.Elevation[next] < sea || count > 100_000) break;
            w.DrawableRiver[next] = true;
        }
    }

    /// <summary>Port of worldRain() — one raindrop walk seeded from every land cell.</summary>
    public static void WorldRain(WorldGrid w, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        for (int y = 0; y < w.Height; y++)
        {
            for (int x = 0; x < w.Width; x++)
            {
                int cell = w.Idx(x, y);
                if (w.Elevation[cell] <= sea) continue;
                w.Raindrops[cell] += w.Elevation[cell];
                TrackRain(w, cfg, x, y);
            }
        }
    }

    /// <summary>
    /// Port of erodeFromRaindrops(). Erosion scales with both accumulated water and height, so
    /// high wet ground wears down fastest. Cells holding more water than they can shed pool
    /// into lakes.
    /// </summary>
    public static void ErodeFromRaindrops(WorldGrid w, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        for (int i = 0; i < w.Count; i++)
        {
            if (w.Elevation[i] < sea) continue;

            int drops = w.Raindrops[i];
            int erosion = (drops / 100) * (w.Elevation[i] / 50);
            w.Elevation[i] -= erosion;
            if (w.Elevation[i] < sea) w.Elevation[i] = sea;

            int comp = 1400 - w.Elevation[i];
            w.Lake[i] = w.Raindrops[i] > comp && w.Elevation[i] >= sea;
            w.Raindrops[i] = 0;
        }
    }

    /// <summary>Port of rainErosion().</summary>
    public static void RainErosion(WorldGrid w, MapConfig cfg)
    {
        WorldRain(w, cfg);
        ErodeFromRaindrops(w, cfg);
    }
}
