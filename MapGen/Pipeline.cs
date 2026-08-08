using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The world-generation sequence, ported from startup() in js/mapgen/startup.js.
///
/// Ordering here is load-bearing and not obvious: the 10 bare emit/spread rounds build the
/// gross landmasses, spreadProcess adds 20 more rounds that also track moisture, and only then
/// is the map cleaned up. Reordering changes the shape of every continent.
/// </summary>
public static class Pipeline
{
    public static WorldGrid GenerateWorld(MapConfig cfg, Rng rng)
    {
        Console.WriteLine($"Generating {cfg.WorldWidth}x{cfg.WorldHeight} simulation grid (seed {cfg.Seed})");

        var w = WorldGrid.CreateBlank(cfg, rng);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Tectonics.Initialize(w, cfg, rng);
        Console.WriteLine($"  {w.SpreadingCenters.Count} spreading centres, {w.SpreadingLine.Count} ridge cells");

        // Ten rounds of pure tectonic build-up.
        for (int i = 0; i < 10; i++)
        {
            Tectonics.SpreadingCenterEmits(w, rng);
            Tectonics.Spread(w, rng);
        }

        // Twenty more, now tracking moisture as the terrain settles.
        Tectonics.SpreadProcess(w, cfg, rng, 20);
        Console.WriteLine($"  tectonics done in {sw.ElapsedMilliseconds} ms " +
                          $"({LandFraction(w, cfg) * 100:F1}% land)");

        GrowLandToTarget(w, cfg, rng);

        Terrain.CleanupAll(w, cfg);
        Terrain.GetBeaches(w, cfg);
        Climate.SetMoisture(w, cfg, rng);
        Terrain.FloodFillMountains(w, cfg);

        CarveWater(w, cfg, rng);

        Report(w, cfg);
        return w;
    }

    /// <summary>
    /// Erosion, rivers and landmass identification. Not part of startup() — in ck2rpg these are
    /// separate buttons the user presses once the coastline looks right.
    /// </summary>
    private static void CarveWater(WorldGrid w, MapConfig cfg, Rng rng)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (cfg.EnableRainErosion)
        {
            Climate.RainErosion(w, cfg);
            Terrain.CleanupAll(w, cfg);
            Terrain.FloodFillRivers(w, cfg, rng);
            Console.WriteLine($"  rain erosion + {w.Waters.Count} water bodies ({sw.ElapsedMilliseconds} ms)");
            sw.Restart();
        }

        Rivers.Rerun(w, cfg);
        Console.WriteLine($"  {w.Rivers.Count} rivers traced ({sw.ElapsedMilliseconds} ms)");

        Terrain.FloodFillContinents(w, cfg, rng);
    }

    /// <summary>
    /// The alternative terrain path: noise plus hotspot continents (randomMap in the JS UI).
    /// Produces smoother, rounder continents and never generates mountains, because the
    /// elevation curve tops out well below the mountain threshold.
    /// </summary>
    public static WorldGrid GenerateWorldFromNoise(MapConfig cfg, Rng rng)
    {
        Console.WriteLine($"Generating {cfg.WorldWidth}x{cfg.WorldHeight} noise grid (seed {cfg.Seed})");

        var w = WorldGrid.CreateBlank(cfg, rng);
        Elevation.ConstrainedMap(w, cfg, rng);
        Terrain.FloodFillMountains(w, cfg);

        CarveWater(w, cfg, rng);

        Report(w, cfg);
        return w;
    }

    /// <summary>
    /// Automated stand-in for the user hammering the "spread" button (clickables.js
    /// spread-icon), which is how continents actually grow in ck2rpg. Each pass is one click:
    /// three rounds of emit/spread/moisture.
    /// </summary>
    private static void GrowLandToTarget(WorldGrid w, MapConfig cfg, Rng rng)
    {
        if (cfg.TargetLandFraction <= 0) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int rounds = 0;
        double fraction = LandFraction(w, cfg);

        while (fraction < cfg.TargetLandFraction && rounds < cfg.MaxExtraSpreadRounds)
        {
            for (int i = 0; i < 3; i++)
            {
                Tectonics.SpreadingCenterEmits(w, rng);
                Tectonics.Spread(w, rng);
                Climate.SetMoisture(w, cfg, rng);
            }
            rounds += 3;
            fraction = LandFraction(w, cfg);
        }

        Console.WriteLine($"  grew land to {fraction * 100:F1}% in {rounds} extra spread rounds " +
                          $"({sw.ElapsedMilliseconds} ms)");
        if (fraction < cfg.TargetLandFraction)
            Console.WriteLine($"  WARNING: hit the {cfg.MaxExtraSpreadRounds}-round cap short of " +
                              $"the {cfg.TargetLandFraction * 100:F0}% target");
    }

    private static double LandFraction(WorldGrid w, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int land = 0;
        for (int i = 0; i < w.Count; i++)
            if (w.Elevation[i] > sea) land++;
        return (double)land / w.Count;
    }

    private static void Report(WorldGrid w, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int land = 0, mountains = 0, beaches = 0, desert = 0;
        int min = int.MaxValue, max = int.MinValue;

        for (int i = 0; i < w.Count; i++)
        {
            int e = w.Elevation[i];
            if (e < min) min = e;
            if (e > max) max = e;
            if (e > sea) land++;
            if (e > cfg.Limits.Mountains.Lower) mountains++;
            if (w.Beach[i]) beaches++;
            if (w.Desert[i]) desert++;
        }

        double landPct = 100.0 * land / w.Count;
        Console.WriteLine($"  elevation {min}..{max}, sea level {sea}");
        Console.WriteLine($"  land {land} cells ({landPct:F1}%), mountains {mountains}, beach {beaches}, desert {desert}");
        int riverCells = 0, lakeCells = 0;
        for (int i = 0; i < w.Count; i++)
        {
            if (w.River[i]) riverCells++;
            if (w.Lake[i]) lakeCells++;
        }

        int biggest = w.Continents.Count == 0 ? 0 : w.Continents.Max(c => c.Cells.Count);
        Console.WriteLine($"  {w.Mountains.Count} mountain ranges, {w.Continents.Count} landmasses " +
                          $"(largest {biggest} cells)");
        Console.WriteLine($"  {w.Rivers.Count} rivers over {riverCells} cells, {lakeCells} lake cells");
    }
}
