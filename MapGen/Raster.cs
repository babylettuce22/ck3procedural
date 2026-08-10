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
    /// Heightmap rows the province grid is offset by, relative to the obvious block
    /// [2y, 2y+1]. Every province-resolution field derived from the heightmap goes through
    /// <see cref="ProvinceBlock"/> so they all share this one number.
    ///
    /// -1 is measured, not chosen. Sweeping the sampling offset against vanilla 1.19's own map_data
    /// — reading province pixel (x,y) as heightmap (2y+oy, 2x+ox) — two independently authored
    /// rasters peak at the same place:
    ///
    ///     oy    agreement with vanilla provinces.png    depth of its drawn river channels
    ///     -1                     99.8829%                             62
    ///      0                     99.8615%                             57
    ///     +1                     99.7160%                             43
    ///
    /// The coastline in provinces.png and the channels under rivers.png were drawn by different
    /// means, so both landing on the same optimum is what rules out a rasterising artefact in one
    /// of them. The offset is vertical only — ox=0 and ox=+1 tie to four decimals, so horizontally
    /// the block is already right. A one-pixel vertical shift with no horizontal component is what
    /// an off-by-one in a vertical flip looks like: image rows run one way and the game's Z axis
    /// the other.
    ///
    /// The caveat, stated because it cannot be measured away: this is vanilla's *internal*
    /// relationship between its own files, not a reading of the engine. The inference is that
    /// vanilla renders correctly, so its relationship is the one the engine expects. Our own output
    /// cannot confirm it — provinces.png is derived from our heightmap by thresholding, so it
    /// agrees with itself at any offset (measured 100.0000% at both 0 and +1) and self-consistency
    /// is not the same thing as matching the engine.
    /// </summary>
    public const int ProvinceRowOffset = -1;

    /// <summary>
    /// The heightmap rows and columns behind one province pixel, clamped at the edges. The single
    /// definition of how the two resolutions line up — if this and
    /// <see cref="ProvinceElevation"/> ever disagreed, the coastline and the province map would
    /// drift apart by a pixel and nothing would say so.
    /// </summary>
    public static (int Y0, int X0) ProvinceBlock(int x, int y, int scaleX, int scaleY,
        int fullWidth, int fullHeight)
    {
        int y0 = Math.Clamp(y * scaleY + ProvinceRowOffset, 0, fullHeight - scaleY);
        int x0 = Math.Clamp(x * scaleX, 0, fullWidth - scaleX);
        return (y0, x0);
    }

    /// <summary>
    /// The heightmap at province resolution, box-averaged over <see cref="ProvinceBlock"/>.
    ///
    /// Not <see cref="Field.Downsample"/>, which is a plain block average and knows nothing about
    /// where CK3 expects the two grids to line up. Everything the partition does — seeds, costs,
    /// the terrain the borders follow — is measured on this field, so it has to be sampled where
    /// the land actually is.
    /// </summary>
    public static float[] ProvinceElevation(float[] elevation, MapConfig cfg)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int scaleX = cfg.Width / width, scaleY = cfg.Height / height;
        float inv = 1f / (scaleX * scaleY);

        var province = new float[width * height];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                var (y0, x0) = ProvinceBlock(x, y, scaleX, scaleY, cfg.Width, cfg.Height);

                float sum = 0;
                for (int j = 0; j < scaleY; j++)
                {
                    long row = (long)(y0 + j) * cfg.Width + x0;
                    for (int i = 0; i < scaleX; i++) sum += elevation[row + i];
                }

                province[y * width + x] = sum * inv;
            }
        });

        return province;
    }

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
                var (y0, x0) = ProvinceBlock(x, y, scaleX, scaleY, cfg.Width, cfg.Height);

                int dry = 0;
                for (int j = 0; j < scaleY; j++)
                {
                    long row = (long)(y0 + j) * cfg.Width + x0;
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
