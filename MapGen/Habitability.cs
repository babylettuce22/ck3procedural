using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// How closely a place can be settled, as a field over the province raster.
///
/// This is what decides where provinces are small. Province size follows population density and
/// always has: the Nile delta, Flanders and the Po valley are crowded with counties because they
/// fed crowds, and the steppe, the Sahara and the Norwegian interior are drawn in vast tracts
/// because a ruler needed vast tracts to raise anything at all. A generated map that spaces its
/// seeds by noise gets the *unevenness* right and the *reasons* wrong, so the small provinces land
/// nowhere in particular and the map reads as patterned rather than settled.
///
/// It is a weighting, not a simulation. Four terms, each of which is either already computed or
/// costs one pass over the raster:
///
///   * <b>Coastal access.</b> A coast is a road that needs no upkeep, so the shore is where people
///     are. Decays inland over <see cref="CoastRangeAtVanilla"/>.
///   * <b>Freshwater.</b> Rivers and lakes, on the same footing and over a shorter range — a river
///     valley is narrow, which is the point of it.
///   * <b>Slope.</b> Flat ground is farmed and steep ground is not. This is also what keeps a
///     mountain range from filling with tiny provinces just because it is near a coast.
///   * <b>Climate.</b> Rainfall and mean temperature, straight off the circulation model. Dry
///     ground and cold ground both go empty, and the two multiply: a cold desert is not the sum of
///     two problems but the product of them.
///
/// The climate terms read the model's *continuous* fields rather than the Koppen class, on purpose.
/// Koppen is a set of thresholds, so classifying first and weighting after would put a step in
/// province size wherever a class boundary falls — the map would grow visible seams along the
/// BSh/BWh line that no amount of noise would hide. The fields the classes are cut from have no
/// such edges.
/// </summary>
public static class Habitability
{
    /// <summary>How far inland a coast is still worth being near, in vanilla province pixels.</summary>
    private const double CoastRangeAtVanilla = 140;

    /// <summary>The same for a river or lake shore. Shorter, because a valley is narrow.</summary>
    private const double FreshwaterRangeAtVanilla = 55;

    /// <summary>Ground everywhere is worth something before any of the terms below apply, so the
    /// driest corner of the map is thinly settled rather than empty.</summary>
    private const double BaseFertility = 0.35;

    private const double CoastWeight = 0.35;
    private const double FreshwaterWeight = 0.30;

    /// <summary>
    /// Rainfall at which the moisture term reaches 1-1/e, i.e. most of the way. Saturating rather
    /// than linear because the step from 200 mm to 500 mm is the difference between steppe and
    /// farmland, while the step from 1200 mm to 1500 mm is the difference between two kinds of
    /// good farmland and settles nobody extra.
    /// </summary>
    private const double MoistureScaleMm = 500;

    /// <summary>What is left of habitability on ground that gets no rain at all. Not zero: an oasis
    /// belt is thin, not empty, and a zero here would make the Sahara one province.</summary>
    private const double DesertFloor = 0.10;

    /// <summary>Mean annual temperature over which the cold term climbs from nothing to full. Below
    /// the first figure is permafrost.</summary>
    private const double ColdDeadC = -8;
    private const double ColdFullC = 6;
    private const double PolarFloor = 0.08;

    /// <summary>Where heat starts to cost something on its own, in mean annual degrees, and what is
    /// left at the top. Mild, because in practice hot ground is punished through rainfall.</summary>
    private const double HotStartC = 25;
    private const double HotEndC = 33;
    private const double HotFloor = 0.75;

    /// <summary>Rise over run, in elevation units per pixel, at which ground is written off as
    /// unfarmable. Measured against the partition's own elevation field.</summary>
    private const double SteepSlope = 14;
    private const double SlopeFloor = 0.25;

    /// <summary>
    /// A 0-1 field over the province raster, higher where people can pack in tighter. Sea pixels
    /// carry the land value nearest them rather than a value of their own, so sampling near a coast
    /// never falls off a cliff.
    /// </summary>
    public static float[] Build(byte[] mask, float[] elevation, byte[] rivers, byte[] lakes,
        ClimateField climate, int width, int height, MapConfig cfg)
    {
        double coastRange = Math.Max(1, cfg.Scaled(CoastRangeAtVanilla));
        double freshRange = Math.Max(1, cfg.Scaled(FreshwaterRangeAtVanilla));

        var toSea = DistanceTo(width, height, cell => mask[cell] != 1);
        var toFresh = DistanceTo(width, height,
            cell => rivers[cell] != 0 || lakes[cell] != 0 || mask[cell] != 1);

        var field = new float[width * height];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;

                double coast = CoastWeight * Falloff(toSea[cell], coastRange);
                double fresh = FreshwaterWeight * Falloff(toFresh[cell], freshRange);
                double slope = Slope(elevation, mask, width, height, x, y);
                double weather = Weather(climate.AnnualMm[cell], climate.MeanC[cell]);

                field[cell] = (float)Math.Clamp((BaseFertility + coast + fresh) * weather * slope,
                    0.01, 1.0);
            }
        });

        return field;
    }

    /// <summary>Exponential rather than linear, so the shore is sharply better than a day inland
    /// and the far interior is uniformly indifferent rather than sloping forever.</summary>
    private static double Falloff(double distance, double range)
        => Math.Exp(-distance / range);

    /// <summary>
    /// What the climate is worth: rainfall and warmth multiplied, not added.
    ///
    /// Multiplied because they are not independent problems a place can trade off. Rain on frozen
    /// ground grows nothing and heat without rain grows nothing, so either one at zero should take
    /// the whole term with it — which addition would not do, and which is the difference between
    /// Siberia reading as half-habitable and reading as empty.
    ///
    /// The dry belt the old latitude term faked is now wherever the circulation model actually put
    /// it, which is the point of the reorder: a desert lands in the rain shadow and on the
    /// descending branch, not on a parallel.
    /// </summary>
    private static double Weather(double annualMm, double meanC)
    {
        double moisture = DesertFloor
            + (1 - DesertFloor) * (1 - Math.Exp(-Math.Max(0, annualMm) / MoistureScaleMm));

        double cold = PolarFloor + (1 - PolarFloor) * Field.SmoothStep(ColdDeadC, ColdFullC, meanC);
        double heat = 1 - (1 - HotFloor) * Field.SmoothStep(HotStartC, HotEndC, meanC);

        return moisture * cold * heat;
    }

    /// <summary>
    /// Local steepness as a central difference over the partition's own elevation, so the term
    /// answers to the same ground the province borders do. Sea neighbours are skipped rather than
    /// counted as a cliff, or every coast would read as unfarmable.
    /// </summary>
    private static double Slope(float[] elevation, byte[] mask, int width, int height, int x, int y)
    {
        int cell = y * width + x;
        if (mask[cell] != 1) return 1;

        double worst = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            int ny = y + dy;
            if (ny < 0 || ny >= height) continue;

            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                if (nx < 0 || nx >= width || (dx == 0 && dy == 0)) continue;

                int next = ny * width + nx;
                if (mask[next] != 1) continue;

                double run = dx != 0 && dy != 0 ? 1.41421356 : 1;
                worst = Math.Max(worst, Math.Abs(elevation[next] - elevation[cell]) / run);
            }
        }

        return 1 - (1 - SlopeFloor) * Math.Clamp(worst / SteepSlope, 0, 1);
    }

    /// <summary>
    /// Distance in pixels from every cell to the nearest source, by the 3-4 chamfer: two raster
    /// passes, and close enough to Euclidean that nothing downstream can tell. A breadth-first
    /// flood would be simpler and would give square rings, which show up as square provinces.
    /// </summary>
    private static float[] DistanceTo(int width, int height, Func<int, bool> isSource)
    {
        const int Near = 3, Diagonal = 4;
        int far = width * height * Near;

        var d = new int[width * height];
        for (int i = 0; i < d.Length; i++) d[i] = isSource(i) ? 0 : far;

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;
                if (d[cell] == 0) continue;

                if (y > 0)
                {
                    if (x > 0) d[cell] = Math.Min(d[cell], d[cell - width - 1] + Diagonal);
                    d[cell] = Math.Min(d[cell], d[cell - width] + Near);
                    if (x + 1 < width) d[cell] = Math.Min(d[cell], d[cell - width + 1] + Diagonal);
                }
                if (x > 0) d[cell] = Math.Min(d[cell], d[cell - 1] + Near);
            }

        for (int y = height - 1; y >= 0; y--)
            for (int x = width - 1; x >= 0; x--)
            {
                int cell = y * width + x;
                if (d[cell] == 0) continue;

                if (y + 1 < height)
                {
                    if (x + 1 < width) d[cell] = Math.Min(d[cell], d[cell + width + 1] + Diagonal);
                    d[cell] = Math.Min(d[cell], d[cell + width] + Near);
                    if (x > 0) d[cell] = Math.Min(d[cell], d[cell + width - 1] + Diagonal);
                }
                if (x + 1 < width) d[cell] = Math.Min(d[cell], d[cell + 1] + Near);
            }

        var pixels = new float[d.Length];
        for (int i = 0; i < d.Length; i++) pixels[i] = d[i] / (float)Near;
        return pixels;
    }
}
