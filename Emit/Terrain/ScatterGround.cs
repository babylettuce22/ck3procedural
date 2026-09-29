using Ck3MapGen.Config;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The dry-land test the three scatter passes — <see cref="TreeWriter"/>,
/// <see cref="AnimalWriter"/> and <see cref="EnvEffectWriter"/> — share.
///
/// All three seed instances from <see cref="TerrainClass"/>, which is province resolution, and
/// then jitter to a sub-pixel position. That last step is where foliage ended up standing in
/// the sea.
/// <see cref="Raster.LandMask"/> calls a province pixel land when *half or more* of the four
/// heightmap pixels behind it clear the waterline — a deliberate choice, since thresholding the
/// averaged copy instead eats every headland and spit — so a two-of-four coastal pixel is
/// legitimately classified Steppe or Plains while half of its area is genuinely under water.
/// Scatter uniformly across that pixel and half of what lands there sits below the water plane,
/// which in game reads as bushes floating offshore.
///
/// The fix is to stop asking the province-resolution class and ask the heightmap the engine
/// actually renders, at the sub-pixel the instance is going to. Sampling goes through
/// <see cref="Raster.ProvinceBlock"/> rather than a fresh 2x scaling, so there stays exactly one
/// definition of how the two grids line up — including its measured -1 row offset. Deriving it
/// again here is how the coastline and the scatter would drift apart by a pixel with nothing
/// saying so.
///
/// The elevation passed in must be the surface the engine renders, not the one the simulation
/// computed: <see cref="MapDataWriter.ShippedHeightmap"/> round-tripped through
/// <see cref="HeightmapPacker.Reconstruct"/>. ContentWriter builds it once and hands the same
/// array to all three. Passing the raw elevation instead compiles and looks right, and puts
/// the trees back in the sea.
/// </summary>
internal static class ScatterGround
{
    /// <summary>
    /// Whether the heightmap clears the waterline at a fractional province-space position.
    /// </summary>
    /// <param name="px">Column in province space, fractional — the jittered position itself.</param>
    /// <param name="py">Row in province space, fractional, still in image order (top-down).</param>
    public static bool IsDryLand(float[] elevation, MapConfig cfg, double px, double py)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;

        int x = (int)px, y = (int)py;
        if (x < 0 || x >= width || y < 0 || y >= height) return false;

        int scaleX = cfg.Width / width, scaleY = cfg.Height / height;
        var (y0, x0) = Raster.ProvinceBlock(x, y, scaleX, scaleY, cfg.Width, cfg.Height);

        // Which heightmap pixel inside the block the jitter actually landed on.
        int sx = Math.Clamp((int)((px - x) * scaleX), 0, scaleX - 1);
        int sy = Math.Clamp((int)((py - y) * scaleY), 0, scaleY - 1);

        return elevation[(long)(y0 + sy) * cfg.Width + x0 + sx] > cfg.Limits.SeaLevelUpper;
    }

    /// <summary>
    /// The rendered elevation, in simulation units, under a fractional province-space position —
    /// the same heightmap texel <see cref="IsDryLand"/> tests, so a caller walking a line of
    /// samples sees the same ground the dry-land test does. NaN outside the map.
    /// </summary>
    public static float SampleHeight(float[] elevation, MapConfig cfg, double px, double py)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;

        int x = (int)Math.Floor(px), y = (int)Math.Floor(py);
        if (x < 0 || x >= width || y < 0 || y >= height) return float.NaN;

        int scaleX = cfg.Width / width, scaleY = cfg.Height / height;
        var (y0, x0) = Raster.ProvinceBlock(x, y, scaleX, scaleY, cfg.Width, cfg.Height);

        int sx = Math.Clamp((int)((px - x) * scaleX), 0, scaleX - 1);
        int sy = Math.Clamp((int)((py - y) * scaleY), 0, scaleY - 1);

        return elevation[(long)(y0 + sy) * cfg.Width + x0 + sx];
    }

    /// <summary>
    /// Whether the ground under a scatter position is level enough to stand its mesh on.
    ///
    /// The engine plants each instance's origin on the terrain and leaves the mesh upright, so on
    /// a slope the downhill side of its base hangs in the air and the uphill side is buried. Every
    /// generated mesh has the problem; what differs is how wide the base is and how much tilt the
    /// mesh can hide — a pine's trunk forgives more than a fallen log does.
    ///
    /// Relief across the footprint, rather than a gradient at the centre: what matters is whether
    /// any part of the ground the mesh covers is far off the height it is planted at, and a ridge
    /// crossing the footprint fails that while reading as flat at the middle. Every heightmap
    /// texel inside the footprint is read — the engine drops the mesh onto that surface, not onto
    /// a sample of it — and the footprint is centred on the texel the jittered position lands
    /// on, through the same mapping <see cref="IsDryLand"/> uses, so the two tests look at the
    /// same ground.
    ///
    /// The tolerance is in world units, which is the only frame in which one number means the
    /// same thing on every map: <c>WORLD_EXTENTS_Y</c> is 50 whatever the map size or its
    /// <c>PeakElevation</c> (see <see cref="CompatibilityWriter.WriteDefines"/>), so a world unit
    /// of relief is the same drop under a tree everywhere. Elevation arrives in simulation units
    /// and is restated through the exact mapping <see cref="MapDataWriter"/> ships it with.
    /// </summary>
    /// <param name="px">Column in province space, fractional — the jittered position itself.</param>
    /// <param name="py">Row in province space, fractional, in image order (top-down).</param>
    /// <param name="radius">Half the footprint, in province pixels — one province pixel is one
    /// world unit, the same frame the mesh's own extents are measured in.</param>
    /// <param name="maxRelief">Largest tolerable spread, in world units. For scale: vanilla's
    /// own foliage sits on at most ~1.1 units across a one-pixel radius at the 95th percentile,
    /// its mountain line is around 20 units above the sea, and the tallest peak is 50.</param>
    public static bool IsFlatEnough(float[] elevation, MapConfig cfg, double px, double py,
        int radius, float maxRelief)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int x = (int)px, y = (int)py;
        if (x < 0 || x >= width || y < 0 || y >= height) return false;

        int scaleX = cfg.Width / width, scaleY = cfg.Height / height;
        var (y0, x0) = Raster.ProvinceBlock(x, y, scaleX, scaleY, cfg.Width, cfg.Height);
        int cx = x0 + Math.Clamp((int)((px - x) * scaleX), 0, scaleX - 1);
        int cy = y0 + Math.Clamp((int)((py - y) * scaleY), 0, scaleY - 1);

        int rx = radius * scaleX, ry = radius * scaleY;
        if (cx - rx < 0 || cx + rx >= cfg.Width || cy - ry < 0 || cy + ry >= cfg.Height) return false;

        float low = float.MaxValue, high = float.MinValue;
        for (int sy = cy - ry; sy <= cy + ry; sy++)
        {
            long row = (long)sy * cfg.Width;
            for (int sx = cx - rx; sx <= cx + rx; sx++)
            {
                float h = WorldHeight(elevation[row + sx], cfg);
                low = Math.Min(low, h);
                high = Math.Max(high, h);
                if (high - low > maxRelief) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A simulation-unit elevation as the height the engine renders it at, in world units. The
    /// same piecewise map <see cref="MapDataWriter"/> uses to write heightmap.png, then the 16-bit
    /// range onto <c>WORLD_EXTENTS_Y</c>.
    /// </summary>
    public static float WorldHeight(float elevation, MapConfig cfg)
    {
        float sea = cfg.Limits.SeaLevelUpper;
        float floor = cfg.SeaFloorElevation;
        float peak = cfg.PeakElevation;
        const float water = MapDataWriter.WaterLevel16;

        float v = elevation <= sea
            ? (elevation - floor) / Math.Max(1e-3f, sea - floor) * water
            : water + (elevation - sea - 1f) / Math.Max(1e-3f, peak - sea - 1f) * (65535f - water);

        return Math.Clamp(v, 0f, 65535f) * (50f / 65535f);
    }

    /// <summary>
    /// The heightmap value under the middle of a province pixel, for relief comparisons between
    /// pixels. Not an average: this is asked once per sample of a scan and only ever differenced
    /// against other samples of the same scan, so a consistent point beats a smoothed one.
    /// </summary>
    public static float HeightAt(float[] elevation, MapConfig cfg, int x, int y)
    {
        int scaleX = cfg.Width / cfg.ProvinceWidth, scaleY = cfg.Height / cfg.ProvinceHeight;
        var (y0, x0) = Raster.ProvinceBlock(x, y, scaleX, scaleY, cfg.Width, cfg.Height);

        return elevation[(long)(y0 + scaleY / 2) * cfg.Width + x0 + scaleX / 2];
    }
}
