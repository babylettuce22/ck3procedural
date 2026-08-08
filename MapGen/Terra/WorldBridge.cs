using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// Fills the existing coarse <see cref="WorldGrid"/> from a Terra result.
///
/// Everything downstream of terrain — <see cref="Climate"/>, <see cref="Biome"/>,
/// <see cref="TerrainClassifier"/>, the province partition, titles and every emitter — reads that
/// grid, so replacing the terrain generator does not have to mean touching any of them. The grid
/// stops being where terrain is *made* and becomes a coarse summary of it.
///
/// Elevations are sampled from the province-resolution field rather than re-derived, so the coarse
/// grid can never disagree with the heightmap about where the coast is.
/// </summary>
public static class WorldBridge
{
    public static WorldGrid Populate(TerraResult terra, MapConfig cfg, Rng rng)
    {
        var world = WorldGrid.CreateBlank(cfg, rng);
        int pw = cfg.ProvinceWidth, ph = cfg.ProvinceHeight;

        float sx = (float)pw / world.Width, sy = (float)ph / world.Height;
        int stepX = Math.Max(1, (int)sx), stepY = Math.Max(1, (int)sy);

        Parallel.For(0, world.Height, y =>
        {
            for (int x = 0; x < world.Width; x++)
            {
                int px = (int)((x + 0.5f) * sx), py = (int)((y + 0.5f) * sy);
                px = Math.Clamp(px, 0, pw - 1);
                py = Math.Clamp(py, 0, ph - 1);

                int cell = world.Idx(x, y);
                world.Elevation[cell] = (int)MathF.Round(terra.ProvinceElevation[py * pw + px]);

                // A coarse cell covers a whole block of province pixels, so a river running
                // through it is only visible if the whole block is checked.
                bool river = false, lake = false;
                for (int j = 0; j < stepY && !river; j++)
                {
                    int yy = Math.Min(py + j - stepY / 2, ph - 1);
                    if (yy < 0) continue;
                    for (int i = 0; i < stepX; i++)
                    {
                        int xx = Math.Min(px + i - stepX / 2, pw - 1);
                        if (xx < 0) continue;
                        int k = yy * pw + xx;
                        if (terra.RiverMask[k] != 0) river = true;
                        if (terra.LakeMask[k] != 0) lake = true;
                    }
                }

                world.River[cell] = river;
                world.DrawableRiver[cell] = river;
                world.Lake[cell] = lake && !river;
            }
        });

        // The feature passes the rest of the project expects to have been run.
        Terrain.GetBeaches(world, cfg);
        Climate.SetMoisture(world, cfg, rng);
        Terrain.FloodFillMountains(world, cfg);
        Terrain.FloodFillContinents(world, cfg, rng);

        Report(world, cfg, terra);
        return world;
    }

    private static void Report(WorldGrid w, MapConfig cfg, TerraResult terra)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int mountainLine = cfg.Limits.Mountains.Lower;
        int land = 0, mountains = 0, beaches = 0, rivers = 0, lakes = 0;
        int min = int.MaxValue, max = int.MinValue;

        for (int i = 0; i < w.Count; i++)
        {
            int e = w.Elevation[i];
            if (e < min) min = e;
            if (e > max) max = e;
            if (e > sea) land++;
            if (e > mountainLine) mountains++;
            if (w.Beach[i]) beaches++;
            if (w.River[i]) rivers++;
            if (w.Lake[i]) lakes++;
        }

        int biggest = w.Continents.Count == 0 ? 0 : w.Continents.Max(c => c.Cells.Count);
        Console.WriteLine($"  world grid {w.Width}x{w.Height}: elevation {min}..{max}, " +
                          $"land {100.0 * land / w.Count:F1}%, mountain cells {mountains}");
        Console.WriteLine($"  {w.Mountains.Count} mountain ranges, {w.Continents.Count} landmasses " +
                          $"(largest {biggest} cells), {beaches} beach, {rivers} river, {lakes} lake");
    }
}
