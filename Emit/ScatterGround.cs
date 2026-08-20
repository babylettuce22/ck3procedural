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
    /// Whether the ground around a province pixel is flat enough to stand a wide object on.
    ///
    /// Most map objects are a metre or two across and can be dropped anywhere the engine will take
    /// them, but a few of vanilla's meshes are scenery rather than props — several province pixels
    /// wide, with their own internal composition — and those only look right where the ground under
    /// the whole footprint is level. Set them down on a slope and the engine still plants the
    /// origin on the terrain, leaving the far side of the mesh buried or hanging in the air.
    ///
    /// Relief across the footprint, rather than a gradient at the centre: what matters is whether
    /// any part of the ground the mesh covers is far off the height it is planted at, and a ridge
    /// crossing the footprint fails that while reading as flat at the middle.
    /// </summary>
    /// <param name="radius">Half the mesh footprint, in province pixels — one province pixel is
    /// one world unit, the same frame the mesh's own extents are measured in.</param>
    /// <param name="maxRelief">Largest tolerable spread in raw heightmap units. For scale, sea
    /// level is 36, hills begin at 205 and mountains at 255.</param>
    public static bool IsFlatEnough(float[] elevation, MapConfig cfg, int x, int y,
        int radius, float maxRelief)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        float low = float.MaxValue, high = float.MinValue;

        // The tolerance is quoted against vanilla's height range, so it has to be restated in this
        // map's. Elevation here is in simulation units, and a map with taller peaks spreads the same
        // real-world slope over more of them — left unscaled, a generous tolerance on a dramatic map
        // silently becomes a strict one and the wide meshes stop being placed at all.
        float range = Math.Max(1f, cfg.PeakElevation - cfg.Limits.SeaLevelUpper);
        float vanillaRange = Math.Max(1f, 236f);
        float tolerance = maxRelief * (range / vanillaRange);

        for (int dy = -radius; dy <= radius; dy += 2)
        {
            for (int dx = -radius; dx <= radius; dx += 2)
            {
                int sx = x + dx, sy = y + dy;
                if (sx < 0 || sx >= width || sy < 0 || sy >= height) return false;

                float h = HeightAt(elevation, cfg, sx, sy);
                low = Math.Min(low, h);
                high = Math.Max(high, h);

                if (high - low > tolerance) return false;
            }
        }

        return true;
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
