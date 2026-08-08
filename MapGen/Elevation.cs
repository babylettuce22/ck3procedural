using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Port of the elevation half of js/all/math.js plus js/mapgen/changeElevation.js.
///
/// This is ck2rpg's *second*, independent way of making terrain: fBm simplex noise biased by
/// gaussian "hotspots" (randomMap / constrainedMap in the UI), as opposed to the tectonic
/// simulation in <see cref="Tectonics"/>. Both write into the same elevation field.
/// </summary>
public static class Elevation
{
    public readonly record struct Hotspot(int X, int Y, int R);

    /// <summary>
    /// Port of generateAutoHotspots() — three to five continent seeds in each map quadrant.
    /// Radii are authored for an 8192-wide grid and scaled down to the simulation grid.
    /// </summary>
    public static List<Hotspot> GenerateAutoHotspots(WorldGrid w, Rng rng)
    {
        var hotspots = new List<Hotspot>();
        double div = w.Width / 8192.0;
        int continentCount = rng.Int(3, 5); // drawn once, reused by all four quadrants

        int halfW = w.Width / 2;
        int halfH = w.Height / 2;

        // Bottom-left and bottom-right use a tighter radius band than the top two.
        AddQuadrant(0, halfW, 0, halfH, 100, 200, 200, 800);
        AddQuadrant(0, halfW, halfH, w.Height - 1, 100, 300, 300, 800);
        AddQuadrant(halfW, w.Width - 1, 0, halfH, 100, 200, 200, 800);
        AddQuadrant(halfW, w.Width - 1, halfH, w.Height - 1, 100, 300, 300, 800);

        return hotspots;

        void AddQuadrant(int x0, int x1, int y0, int y1, int lowMin, int lowMax, int highMin, int highMax)
        {
            for (int i = 0; i < continentCount; i++)
            {
                int low = (int)Math.Floor(rng.Int(lowMin, lowMax) * div);
                int high = (int)Math.Floor(rng.Int(highMin, highMax) * div);
                int x = rng.Int(x0, x1);
                int y = rng.Int(y0, y1);
                hotspots.Add(new Hotspot(x, y, rng.Int(low, high)));
            }
        }
    }

    /// <summary>Port of continentFunction() — summed gaussians, one per hotspot.</summary>
    public static double ContinentFunction(int x, int y, List<Hotspot> hotspots)
    {
        double sum = 0;
        foreach (var h in hotspots)
        {
            double dx = h.X - x;
            double dy = h.Y - y;
            sum += Math.Exp(-(dx * dx + dy * dy) / (3.0 * h.R * h.R));
        }
        return sum;
    }

    /// <summary>
    /// Port of getColor(elevation). Despite the name it returns an elevation, not a colour —
    /// the JS has a lot of dead code after the early return. Below 0.6 everything is deep sea.
    /// </summary>
    public static double ElevationCurve(double elevation)
    {
        if (elevation < 0.6) return 1;
        int num = (int)Math.Floor(elevation * 100);
        int num2 = 100 - num;
        int num3 = 8 * num2;
        return 300 - (37 + num3);
    }

    /// <summary>Port of generateElevations().</summary>
    public static void Generate(WorldGrid w, MapConfig cfg, Rng rng, List<Hotspot> hotspots)
    {
        var noise = new SimplexNoise(rng);

        for (int y = 0; y < w.Height; y++)
        {
            for (int x = 0; x < w.Width; x++)
            {
                double elevation = 0;
                double amplitude = 1.0;
                double frequency = 0.0075;

                // 12 octaves of fBm — the fine detail.
                for (int octave = 0; octave < 12; octave++)
                {
                    elevation += amplitude * noise.Unit(x * frequency, y * frequency);
                    amplitude *= 0.6;
                    frequency *= 2;
                }

                // Continent bias: a very low-frequency layer gated by the hotspot field.
                double continentFactor = ContinentFunction(x, y, hotspots);
                elevation += noise.Unit(x * 0.002, y * 0.002) * continentFactor * 2.0;

                elevation /= 3.0;

                double a = Math.Floor(ElevationCurve(elevation) / 1.8);
                if (a < 37) a = 10;
                w.Elevation[w.Idx(x, y)] = (int)a;
            }
        }
    }

    /// <summary>Port of randomMap().</summary>
    public static void RandomMap(WorldGrid w, MapConfig cfg, Rng rng)
    {
        var hotspots = GenerateAutoHotspots(w, rng);
        Generate(w, cfg, rng, hotspots);
        Climate.SetMoisture(w, cfg, rng);
    }

    /// <summary>Port of constrainedMap() — randomMap plus a cleanup pass.</summary>
    public static void ConstrainedMap(WorldGrid w, MapConfig cfg, Rng rng)
    {
        RandomMap(w, cfg, rng);
        Terrain.CleanupAll(w, cfg);
    }

    // --- js/mapgen/changeElevation.js: bulk editing operations ---

    public static void Raise(WorldGrid w, int amount = 1)
    {
        for (int i = 0; i < w.Count; i++) w.Elevation[i] += amount;
    }

    public static void Lower(WorldGrid w, int amount = 1)
    {
        for (int i = 0; i < w.Count; i++) w.Elevation[i] -= amount;
    }

    public static void Randomize(WorldGrid w, Rng rng)
    {
        for (int i = 0; i < w.Count; i++) w.Elevation[i] += rng.Int(-20, 20);
    }

    /// <summary>Port of lowerElevationIfLand() — sinks land but never below sea level.</summary>
    public static void LowerIfLand(WorldGrid w, MapConfig cfg, int amount)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        for (int i = 0; i < w.Count; i++)
        {
            if (w.Elevation[i] - amount <= sea) continue;
            w.Elevation[i] -= amount;
            if (w.Elevation[i] < sea) w.Elevation[i] = sea + 3;
        }
    }

    /// <summary>Port of lowerElevationIfMountain().</summary>
    public static void LowerIfMountain(WorldGrid w, MapConfig cfg, int amount)
    {
        int mtn = cfg.Limits.Mountains.Lower;
        for (int i = 0; i < w.Count; i++)
        {
            if (w.Elevation[i] - amount <= mtn) continue;
            w.Elevation[i] -= amount;
            if (w.Elevation[i] < mtn) w.Elevation[i] = mtn + 1;
        }
    }

    /// <summary>Port of freshBase() — flattens all land to just above sea level.</summary>
    public static void FreshBase(WorldGrid w, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        for (int i = 0; i < w.Count; i++)
            if (w.Elevation[i] > sea) w.Elevation[i] = sea + 1;
    }

    /// <summary>Port of sharpenMountains().</summary>
    public static void SharpenMountains(WorldGrid w, MapConfig cfg, Rng rng)
    {
        int mtn = cfg.Limits.Mountains.Lower;
        for (int i = 0; i < w.Count; i++)
            if (w.Elevation[i] > mtn) w.Elevation[i] += rng.Int(1, 5);
    }

    /// <summary>
    /// Port of softenMountains() — pushes elevation from a cell to any neighbour more than
    /// `sorter` below it, with the threshold tightening as the cell gets higher.
    /// </summary>
    public static void SoftenMountains(WorldGrid w, Rng rng)
    {
        Span<int> neighbors = stackalloc int[8];
        var order = new int[8];

        for (int y = 0; y < w.Height; y++)
        {
            for (int x = 0; x < w.Width; x++)
            {
                int cell = w.Idx(x, y);
                int elevation = w.Elevation[cell];
                int sorter = elevation > 230 ? 5
                    : elevation > 210 ? 10
                    : elevation > 180 ? 15
                    : 20;

                int count = w.NeighborsOf(x, y, neighbors);
                for (int k = 0; k < count; k++) order[k] = neighbors[k];
                Array.Sort(order, 0, count, Comparer<int>.Create((a, b) => w.Elevation[a].CompareTo(w.Elevation[b])));

                for (int k = 0; k < count; k++)
                {
                    int diff = w.Elevation[cell] - w.Elevation[order[k]];
                    if (diff <= sorter) continue;
                    int num = rng.Int(1, diff / 2);
                    w.Elevation[cell] -= num + sorter;
                    w.Elevation[order[k]] += num + sorter;
                }
            }
        }
    }
}
