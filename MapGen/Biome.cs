using Ck3MapGen.Config;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

public enum BiomeType
{
    None,
    Ocean,
    Beach,
    Lake,
    River,
    Mountain,
    Arctic,
    Desert,
    Grass,
}

/// <summary>
/// Port of js/mapgen/biome.js and the climate-band predicates from
/// js/mapgen/createProvinceTerrain.js.
///
/// Latitude bands are measured in *raster* space as distance from settings.equator, which sits
/// at 90% of map height rather than the middle — ck2rpg's equator is deliberately off-centre.
/// Each band edge is jittered per column by a shared random walk so the boundaries are ragged.
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

    public static bool IsTropical(MapConfig cfg, double rasterY, int? x = null)
        => EqDist(cfg, rasterY) <= cfg.Limits.Tropical.Upper + Vary(cfg, cfg.Limits.Tropical, x);

    /// <summary>
    /// Note the lower edge borrows the *tropical* band's jitter, so it lines up exactly with
    /// the top of the tropics and the two bands tile without a gap. Same trick below.
    /// </summary>
    public static bool IsSubTropical(MapConfig cfg, double rasterY, int? x = null)
    {
        double d = EqDist(cfg, rasterY);
        double lower = cfg.Limits.SubTropical.Lower + Vary(cfg, cfg.Limits.Tropical, x);
        double upper = cfg.Limits.SubTropical.Upper + Vary(cfg, cfg.Limits.SubTropical, x);
        return d >= lower && d <= upper;
    }

    public static bool IsTemperate(MapConfig cfg, double rasterY, int? x = null)
    {
        double d = EqDist(cfg, rasterY);
        double lower = cfg.Limits.Temperate.Lower + Vary(cfg, cfg.Limits.SubTropical, x);
        double upper = cfg.Limits.Temperate.Upper + Vary(cfg, cfg.Limits.Temperate, x);
        return d >= lower && d <= upper;
    }

    public static bool IsCold(MapConfig cfg, double rasterY, int? x = null)
    {
        double d = EqDist(cfg, rasterY);
        double lower = cfg.Limits.Cold.Lower + Vary(cfg, cfg.Limits.Temperate, x);
        return d >= lower && d <= cfg.Limits.Cold.Upper;
    }

    /// <summary>Port of isBelowPlainsLimit(y) — the far-polar cut-off.</summary>
    public static bool IsBelowPlainsLimit(MapConfig cfg, double rasterY)
        => cfg.Limits.Cold.Plains is { } plains && EqDist(cfg, rasterY) >= plains;

    /// <summary>Port of beachable(cell) — a beach may not touch a lake.</summary>
    public static bool Beachable(WorldGrid w, int cell)
    {
        Span<int> neighbors = stackalloc int[8];
        int count = w.NeighborsOf(w.X(cell), w.Y(cell), neighbors);
        for (int k = 0; k < count; k++)
            if (w.Lake[neighbors[k]]) return false;
        return true;
    }

    /// <summary>Port of biome(cell).</summary>
    public static BiomeType Classify(WorldGrid w, MapConfig cfg, int cell)
    {
        int el = w.Elevation[cell];
        int sea = cfg.Limits.SeaLevelUpper;
        double rasterY = w.Y(cell) * cfg.PixelSize;

        if (w.Beach[cell] && Beachable(w, cell)) return BiomeType.Beach;
        if (w.Lake[cell]) return BiomeType.Lake;
        if (w.River[cell]) return BiomeType.River;
        if (el > cfg.Limits.Mountains.Lower) return BiomeType.Mountain;
        if (el >= sea && el <= 255 && IsCold(cfg, rasterY)) return BiomeType.Arctic;

        // The JS also tests `cell.moisture < 50 && el > limits.seaLevel.lower`, but
        // limits.seaLevel has no `lower` field, so that comparison is against undefined and is
        // always false. Only the desert-flag clause below can actually fire.
        if (el >= sea && el <= 255 && w.Desert[cell]) return BiomeType.Desert;

        if (el >= sea && el <= 255)
            return w.Moisture[cell] > 0 ? BiomeType.Grass : BiomeType.None;

        return BiomeType.Ocean;
    }
}
