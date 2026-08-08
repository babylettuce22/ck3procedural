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
    /// <summary>
    /// Bilinearly upsample the simulation elevation to <paramref name="width"/> x
    /// <paramref name="height"/>. Wraps horizontally (the map is a cylinder) and clamps at the
    /// poles.
    /// </summary>
    public static float[] UpsampleElevation(WorldGrid w, int width, int height)
    {
        var result = new float[width * height];
        double sx = (double)w.Width / width;
        double sy = (double)w.Height / height;

        Parallel.For(0, height, y =>
        {
            // Sample at pixel centres so the result is not shifted by half a cell.
            double gy = (y + 0.5) * sy - 0.5;
            int y0 = (int)Math.Floor(gy);
            double fy = gy - y0;
            int y0c = Math.Clamp(y0, 0, w.Height - 1);
            int y1c = Math.Clamp(y0 + 1, 0, w.Height - 1);

            for (int x = 0; x < width; x++)
            {
                double gx = (x + 0.5) * sx - 0.5;
                int x0 = (int)Math.Floor(gx);
                double fx = gx - x0;
                int x0w = Wrap(x0, w.Width);
                int x1w = Wrap(x0 + 1, w.Width);

                float e00 = w.Elevation[y0c * w.Width + x0w];
                float e10 = w.Elevation[y0c * w.Width + x1w];
                float e01 = w.Elevation[y1c * w.Width + x0w];
                float e11 = w.Elevation[y1c * w.Width + x1w];

                float top = (float)(e00 + (e10 - e00) * fx);
                float bottom = (float)(e01 + (e11 - e01) * fx);
                result[y * width + x] = (float)(top + (bottom - top) * fy);
            }
        });

        return result;
    }

    private static int Wrap(int v, int n) => ((v % n) + n) % n;

    /// <summary>
    /// Land/sea mask at raster resolution, from the same sea-level threshold the simulation
    /// uses. 1 = land.
    /// </summary>
    public static byte[] LandMask(float[] elevation, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        var mask = new byte[elevation.Length];
        for (int i = 0; i < elevation.Length; i++)
            mask[i] = elevation[i] > sea ? (byte)1 : (byte)0;

        ForceOceanBorder(mask, cfg.ProvinceWidth, cfg.ProvinceHeight, cfg.OceanBorder);
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
