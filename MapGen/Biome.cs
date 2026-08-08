using Ck3MapGen.Config;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The climate bands, ported from the predicates in js/mapgen/createProvinceTerrain.js.
///
/// Latitude bands are measured in *raster* space as distance from settings.equator, which sits
/// at 90% of map height rather than the middle — ck2rpg's equator is deliberately off-centre.
/// Each band edge is jittered per column by a shared random walk, which on its own only makes a
/// gently wavy line; see <see cref="TerrainClassifier"/> for the altitude and noise terms that
/// turn it into a contour.
/// </summary>
public static class Biome
{
    /// <summary>Port of eqDist(y) — raster-space distance from the equator line.</summary>
    public static double EqDist(MapConfig cfg, double rasterY) => Math.Abs(rasterY - cfg.Equator);

    /// <summary>
    /// The per-column jitter. The JS guards with `if (x)`, so column 0 is falsy and never gets
    /// a modifier; that off-by-one is preserved.
    /// </summary>
    private static double Vary(MapConfig cfg, ClimateBand band, int? x)
        => x is > 0 ? band.VaryRange[x.Value % band.VaryRange.Length] * cfg.PixelSize : 0;

    /// <summary>Port of isBelowPlainsLimit(y) — the far-polar cut-off.</summary>
    public static bool IsBelowPlainsLimitAt(MapConfig cfg, double eqDist)
        => cfg.Limits.Cold.Plains is { } plains && eqDist >= plains;

    public enum ClimateZone : byte { Tropical, SubTropical, Temperate, Cold }

    /// <summary>
    /// Which band a given distance-from-equator falls in.
    ///
    /// Takes the distance rather than a row so the caller can hand in an *effective* latitude —
    /// one displaced by altitude and by noise — instead of the row's own. That single indirection
    /// is what turns the band edge from a horizontal line into a contour: the predicates below are
    /// unchanged, they are simply evaluated against a field that varies in both axes.
    ///
    /// Only the upper edges are tested, in order. The bands tile by construction (each one's lower
    /// edge borrows the band below's jitter), so a cascade cannot leave a gap between them the way
    /// independent range tests can.
    /// </summary>
    public static ClimateZone ZoneOf(MapConfig cfg, double eqDist, int? x = null)
    {
        if (eqDist <= cfg.Limits.Tropical.Upper + Vary(cfg, cfg.Limits.Tropical, x))
            return ClimateZone.Tropical;
        if (eqDist <= cfg.Limits.SubTropical.Upper + Vary(cfg, cfg.Limits.SubTropical, x))
            return ClimateZone.SubTropical;
        if (eqDist <= cfg.Limits.Temperate.Upper + Vary(cfg, cfg.Limits.Temperate, x))
            return ClimateZone.Temperate;
        return ClimateZone.Cold;
    }
}
