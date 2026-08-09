using Ck3MapGen.Config;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Bridges the coarse simulation grid to the full-size export rasters.
///
/// ck2rpg does this by drawing each cell as a settings.pixelSize block on a canvas, which is
/// why its heightmaps are visibly blocky — and jsck3mapper then partitions provinces on that
/// blocky image. We interpolate instead: CK3 renders the heightmap as 3D terrain, where hard
/// cell edges read as terracing. The land/sea coastline is preserved exactly either way,
/// because interpolation is monotonic and sea level is a fixed threshold.
/// </summary>
public static class Raster
{
    private static int Wrap(int v, int n) => ((v % n) + n) % n;

    /// <summary>
    /// Land/sea mask at province resolution, decided on the heightmap CK3 actually renders. 1 = land.
    ///
    /// Takes the *full* heightmap and applies the sea-level threshold there, then lets the four
    /// heightmap pixels behind each province pixel vote. It used to threshold the province-resolution
    /// copy instead, which is a 2x2 box average — and averaging before thresholding is not the same
    /// test, nor a symmetric one. The sea floor sits hundreds of units below the water plane while a
    /// coastal field sits a handful of units above it, so a block that is three-quarters dry land
    /// still averages out well under sea level. Averaging first therefore eats the coast: every
    /// shoreline creeps inland, headlands and spits vanish entirely, and the province map's
    /// coastline stops agreeing with the one the player can see.
    ///
    /// A tie goes to land, which keeps a one-pixel sandbar rather than quietly deleting it.
    /// </summary>
    public static byte[] LandMask(float[] elevation, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int scaleX = cfg.Width / width, scaleY = cfg.Height / height;
        var mask = new byte[width * height];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int dry = 0;
                for (int j = 0; j < scaleY; j++)
                {
                    long row = (long)(y * scaleY + j) * cfg.Width + x * scaleX;
                    for (int i = 0; i < scaleX; i++)
                        if (elevation[row + i] > sea) dry++;
                }

                mask[y * width + x] = dry * 2 >= scaleX * scaleY ? (byte)1 : (byte)0;
            }
        });

        ForceOceanBorder(mask, width, height, cfg.OceanBorder);

        long land = 0;
        foreach (byte m in mask) land += m;

        // Read against the full-resolution share HeightmapSource prints: the two are the same
        // coastline sampled twice, so they should agree to a fraction of a percent. They did not
        // when this averaged first.
        Console.WriteLine($"  province raster: {100.0 * land / mask.Length:F1}% land");

        return mask;
    }

    /// <summary>
    /// Drowns a margin around the map so no province is clipped by the edge, matching vanilla,
    /// whose top and bottom rows are entirely ocean. See <see cref="MapConfig.OceanBorder"/>.
    /// </summary>
    private static void ForceOceanBorder(byte[] mask, int width, int height, int border)
    {
        if (border <= 0 || mask.Length != width * height) return;

        int b = Math.Min(border, Math.Min(width, height) / 4);

        for (int y = 0; y < b; y++)
            for (int x = 0; x < width; x++)
            {
                mask[y * width + x] = 0;
                mask[(height - 1 - y) * width + x] = 0;
            }

        for (int y = 0; y < height; y++)
            for (int x = 0; x < b; x++)
            {
                mask[y * width + x] = 0;
                mask[y * width + (width - 1 - x)] = 0;
            }
    }

    /// <summary>
    /// Rasterise the traced river courses onto a mask at the given resolution. River cells are
    /// single cells on the simulation grid, so each becomes a small block; the course is then
    /// connected by drawing a line between consecutive cells.
    /// </summary>
    public static byte[] RiverMask(WorldGrid w, int width, int height)
    {
        var mask = new byte[width * height];
        double sx = (double)width / w.Width;
        double sy = (double)height / w.Height;

        foreach (var river in w.Rivers)
        {
            for (int i = 0; i < river.Cells.Count - 1; i++)
            {
                int a = river.Cells[i], b = river.Cells[i + 1];
                DrawLine(mask, width, height,
                    (int)((w.X(a) + 0.5) * sx), (int)((w.Y(a) + 0.5) * sy),
                    (int)((w.X(b) + 0.5) * sx), (int)((w.Y(b) + 0.5) * sy));
            }
        }
        return mask;
    }

    /// <summary>Bresenham, so upscaled river courses stay connected.</summary>
    private static void DrawLine(byte[] mask, int width, int height, int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            if (x0 >= 0 && y0 >= 0 && x0 < width && y0 < height) mask[y0 * width + x0] = 1;
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
