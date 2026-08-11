using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Fills the coarse <see cref="WorldGrid"/> from the heightmap.
///
/// Everything downstream — <see cref="Climate"/>, <see cref="Biome"/>,
/// <see cref="TerrainClassifier"/>, the province partition, titles and every emitter — reads that
/// grid. It is a coarse summary of the heightmap, never a source of terrain in its own right.
///
/// Elevations are sampled from the province-resolution field rather than re-derived, so the coarse
/// grid can never disagree with the heightmap about where the coast is.
/// </summary>
public static class WorldBridge
{
    public static WorldGrid Populate(TerrainData terra, MapConfig cfg, Rng rng)
    {
        var world = WorldGrid.CreateBlank(cfg, rng);
        int pw = cfg.ProvinceWidth, ph = cfg.ProvinceHeight;

        float sx = (float)pw / world.Width, sy = (float)ph / world.Height;

        Parallel.For(0, world.Height, y =>
        {
            for (int x = 0; x < world.Width; x++)
            {
                int px = (int)((x + 0.5f) * sx), py = (int)((y + 0.5f) * sy);
                px = Math.Clamp(px, 0, pw - 1);
                py = Math.Clamp(py, 0, ph - 1);

                int cell = world.Idx(x, y);
                world.Elevation[cell] = (int)MathF.Round(terra.ProvinceElevation[py * pw + px]);
            }
        });

        // The feature passes the rest of the project expects to have been run.
        Terrain.GetBeaches(world, cfg);
        Climate.SetMoisture(world, cfg, rng);
        Terrain.FloodFillMountains(world, cfg);
        Terrain.FloodFillContinents(world, cfg, rng);

        Report(world, cfg);
        return world;
    }

    private static void Report(WorldGrid w, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int mountainLine = cfg.Limits.Mountains.Lower;
        int land = 0, mountains = 0, beaches = 0;
        int min = int.MaxValue, max = int.MinValue;

        for (int i = 0; i < w.Count; i++)
        {
            int e = w.Elevation[i];
            if (e < min) min = e;
            if (e > max) max = e;
            if (e > sea) land++;
            if (e > mountainLine) mountains++;
            if (w.Beach[i]) beaches++;
        }

        int biggest = w.Continents.Count == 0 ? 0 : w.Continents.Max(c => c.Cells.Count);
        Console.WriteLine($"  world grid {w.Width}x{w.Height}: elevation {min}..{max}, " +
                          $"land {100.0 * land / w.Count:F1}%, mountain cells {mountains}");
        Console.WriteLine($"  {w.Mountains.Count} mountain ranges, {w.Continents.Count} landmasses " +
                          $"(largest {biggest} cells), {beaches} beach");
    }
}
