namespace Ck3MapGen.Emit;

/// <summary>
/// The one definition of how a province-map pixel becomes a position in the world CK3 places
/// objects in — locators, trees, animals and environment effects all go through here. Image rows
/// run top-down and the map's Z axis runs bottom-up, hence the flip; X needs no conversion.
///
/// <b>The half-pixel question, and why it is settled the way it is.</b> A province pixel can be
/// read two ways, and they differ by half a pixel:
///
///   - <i>area</i>: pixel <c>i</c> is the square <c>[i, i+1)</c>, so world <c>w</c> is pixel
///     <c>floor(w)</c> and the flip is <c>height - y</c>. This is what is implemented here.
///   - <i>centre</i>: world coordinate <c>i</c> *is* pixel index <c>i</c>, so the flip is
///     <c>height - 1 - y</c>.
///
/// The centre reading is what <c>WORLD_EXTENTS_X/Z</c> looks like it implies —
/// <see cref="CompatibilityWriter.WriteDefines"/> writes them as <c>size - 1</c>, and a grid of
/// squares would run to <c>size</c>. It was tried, and it is wrong: shifting the scatter passes
/// half a province pixel — one heightmap texel, since the heightmap is 2x — visibly moved foliage
/// off the ground it had been tested against. The engine agrees with the area reading.
///
/// Measuring it from the shipped files alone is not conclusive, which is worth recording so the
/// argument is not had a third time. Sweeping the sampling offset for vanilla's 549,099 generated
/// foliage instances against its own water provinces, and taking the crossing point between
/// north-facing and south-facing coasts so that coastline asymmetry cancels, puts the offset at
/// x +0.33, y -0.23 — between the two readings. That measurement carries
/// <see cref="MapGen.Raster.ProvinceRowOffset"/> inside it, though: provinces.png is itself a
/// measured one heightmap row off vanilla's heightmap, and correcting for that pulls the estimate
/// back to the area reading. Two rasters and one unknown between them is not a proof, so what
/// decides it is the render.
///
/// <b>Still open:</b> whether a locator should sit at its pixel's corner, as it does now, or be
/// centred in it (+0.5, -0.5). Under the area reading both land inside the correct pixel, so this
/// is a half-pixel of polish rather than a correctness fix, and it moves holdings south-east.
/// </summary>
internal static class WorldSpace
{
    /// <summary>
    /// A position in province-map image space — X rightward, Y down, either a whole pixel index or
    /// a jittered sub-pixel position inside one — as a position in the world.
    /// </summary>
    public static (double X, double Z) FromImage(double x, double y, int provinceHeight) =>
        (x, provinceHeight - y);
}
