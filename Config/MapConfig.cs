namespace Ck3MapGen.Config;

/// <summary>
/// Port of the <c>settings</c> and <c>limits</c> globals in js/all/initialState.js.
///
/// Two resolutions matter and they are not the same thing:
///   * <see cref="Width"/>/<see cref="Height"/> are the exported raster size (settings.width/height).
///   * <see cref="WorldWidth"/>/<see cref="WorldHeight"/> are the tectonic simulation grid
///     (world.width/height), which ck2rpg runs coarse and upsamples on export.
/// Climate limits are expressed in *raster* space; hotspot radii in *world* space.
/// </summary>
public sealed class MapConfig
{
    // --- Export raster (settings.width / settings.height) ---
    public int Width = 8192;
    public int Height = 4096;

    // --- Simulation grid (world.width / world.height) ---
    public int WorldWidth = 1024;
    public int WorldHeight = 512;

    /// <summary>Seed for every random decision. ck2rpg used unseeded Math.random.</summary>
    public int Seed = 1;

    public int TooSmallProvince = 900;
    public bool HorizontalSpread = false;
    public bool VerticalSpread = true;

    /// <summary>When true, land provinces may override water during province fill.</summary>
    public bool FixBlockiness = false;

    public int RiversDistance = 10;
    public int RiverIntoOcean = 1;
    public bool VaryElevation = false;
    public int LandProvinceLimit = 6000;
    public int WaterProvinceLimit = 10000;
    public int FillInLimit = 20;
    public int MassBrushAdjuster = 1;
    public bool OverrideWithFlatmap = false;
    public int ElevationToHeightmap = 2;
    public string Ethnicities = "vanilla";

    /// <summary>
    /// Fraction of the grid that should end up above sea level. ck2rpg has no such setting:
    /// its startup() sequence leaves an archipelago (~6% land) and the user grows continents by
    /// clicking the "spread" button, which runs three more emit/spread rounds per press. This
    /// automates that loop so the tool can run unattended. Set to 0 to stop after startup().
    /// </summary>
    public double TargetLandFraction = 0.40;

    /// <summary>Safety cap on the automated growth loop.</summary>
    public int MaxExtraSpreadRounds = 400;

    /// <summary>
    /// Reshape land heights onto vanilla's measured hypsometric curve instead of stretching them
    /// linearly to whatever the tallest simulated peak happens to be.
    ///
    /// The linear stretch made the map as mountainous as its most extreme accident: measured on
    /// 2026-08-07, it put 18x more land in the top elevation band than vanilla has, and always
    /// drove the highest pixel to 255 where vanilla's tops out at 192. The remap is monotonic, so
    /// it changes only the height scale, never where anything is. Off restores the old behaviour
    /// for bisecting.
    /// </summary>
    public bool MatchVanillaHypsometry = true;

    /// <summary>
    /// Run rainErosion() before rivers. It carves valleys and is the only thing in ck2rpg that
    /// creates lakes, but its UI button is commented out, so a stock ck2rpg run never calls it
    /// and lakes only ever come from hand-painting. Off by default to match that.
    /// </summary>
    public bool EnableRainErosion = false;

    /// <summary>settings.equator — in raster space, deliberately off-centre.</summary>
    public double Equator => Height - Height / 10.0;

    /// <summary>settings.pixelSize — raster pixels per simulation cell.</summary>
    public double PixelSize => (double)Height / WorldHeight;

    // --- Province map. CK3's provinces.png and rivers.png are half the heightmap resolution
    // (vanilla: heightmap 18432x9216, provinces 9216x4608). ---
    public int ProvinceWidth => Width / 2;
    public int ProvinceHeight => Height / 2;

    /// <summary>Roughly settings.landProvinceLimit; actual counts vary with coastline shape.</summary>
    public int TargetLandProvinces = 5000;

    /// <summary>Sea zones. Vanilla has a few hundred; ck2rpg's cap is 10000.</summary>
    public int TargetSeaProvinces = 760;

    /// <summary>
    /// Smallest allowed province in pixels. Below this CK3 cannot derive borders, a centroid or
    /// locator positions and crashes in geometry code without logging anything.
    /// </summary>
    public int MinProvincePixels = 32;

    /// <summary>
    /// Rows and columns of forced ocean around the edge of the province map, in province pixels.
    ///
    /// Vanilla has water along every edge — its top and bottom rows are entirely sea, and its
    /// province map has only a handful of large ocean provinces touching them. A generated map
    /// happily runs land off the poles instead: on seed 1 at vanilla size, 33 land provinces
    /// touched the top edge and 17 the bottom. A province clipped by the map boundary has an
    /// open border, which is the sort of thing a boundary-following walk cannot close.
    /// </summary>
    public int OceanBorder = 1;

    public Limits Limits { get; } = new();

    /// <summary>
    /// Port of resetClimateLimits(). Rescales the climate bands, which are authored against a
    /// 8192-wide map, to the configured raster width.
    /// </summary>
    public void ResetClimateLimits(Core.Rng rng)
    {
        double mod = Width / 8192.0;
        Limits.Tropical.Rescale(mod);
        Limits.SubTropical.Rescale(mod);
        Limits.Temperate.Rescale(mod);
        Limits.Cold.Rescale(mod);
        ResetVaryRanges(rng);
    }

    /// <summary>Port of resetVaryRanges() — per-column jitter so climate bands are not straight lines.</summary>
    public void ResetVaryRanges(Core.Rng rng)
    {
        Limits.Tropical.VaryRange = CreateVaryRange(rng);
        Limits.SubTropical.VaryRange = CreateVaryRange(rng);
        Limits.Temperate.VaryRange = CreateVaryRange(rng);
        Limits.Cold.VaryRange = CreateVaryRange(rng);
    }

    /// <summary>Port of createVaryRange() — a bounded random walk of length world.width.</summary>
    private int[] CreateVaryRange(Core.Rng rng)
    {
        var arr = new int[WorldWidth];
        int last = 0;
        for (int i = 0; i < WorldWidth; i++)
        {
            last += rng.Int(-1, 1);
            if (last > 15) last = 15;
            if (last < -15) last = -15;
            arr[i] = last;
        }
        return arr;
    }
}

/// <summary>Port of the <c>limits</c> global.</summary>
public sealed class Limits
{
    public Range PineTree = new(10, 255);
    public Range Hills = new(205, 255);
    public MountainRange Mountains = new(255, 510, 450);
    public int RaindropsLower = 600;

    /// <summary>Sea level. Note the comment in the JS: elevation is halved when written to the heightmap.</summary>
    public int SeaLevelUpper = 36;

    public ClimateBand Tropical = new(0, 1007);
    public ClimateBand SubTropical = new(1008, 1520);
    public ClimateBand Temperate = new(1521, 2865);
    // cold.upper starts at 4096 but upperBase is 8000, so rescaling at width 8192 lifts it.
    public ClimateBand Cold = new(2866, 4096, upperBase: 8000, plains: 3300);

    public readonly record struct Range(int Lower, int Upper);
    public readonly record struct MountainRange(int Lower, int Upper, int SnowLine);
}

/// <summary>A latitude band measured as distance-from-equator in raster pixels.</summary>
public sealed class ClimateBand
{
    public int Lower;
    public int Upper;
    public readonly int LowerBase;
    public readonly int UpperBase;
    public int? Plains;
    private readonly int? _plainsBase;

    public int[] VaryRange = [];

    public ClimateBand(int lower, int upper, int? upperBase = null, int? plains = null)
    {
        Lower = LowerBase = lower;
        Upper = upper;
        UpperBase = upperBase ?? upper;
        Plains = plains;
        _plainsBase = plains;
    }

    /// <summary>Port of modifyClimate().</summary>
    public void Rescale(double mod)
    {
        Lower = (int)Math.Floor(LowerBase * mod);
        Upper = (int)Math.Floor(UpperBase * mod);
        if (_plainsBase.HasValue) Plains = (int)Math.Floor(_plainsBase.Value * mod);
    }
}
