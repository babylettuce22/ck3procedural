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
///   * <b>Latitude.</b> Warm and wet near the equator, dead at the poles, and dry in the belt
///     around 25 degrees where the Hadley cells come back down — which is where every one of
///     Earth's great deserts is, and the single cheapest way to get a plausible desert.
///
/// Deliberately *not* the climate model, though it would be better. Climate is classified after the
/// partition, because it needs the province land mask, so using it here would mean reordering the
/// pipeline around a field that only sets province sizes. Latitude plus elevation plus distance to
/// water is the same shape of answer for none of that cost — the climate model's own aridity is
/// mostly a latitude effect too.
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

    /// <summary>Latitude of the dry belt, in degrees either side of the equator. Earth's deserts
    /// sit here because this is where the Hadley circulation descends.</summary>
    private const double DesertLatitude = 25;

    /// <summary>How wide that belt is, in degrees.</summary>
    private const double DesertWidth = 12;

    /// <summary>What is left of habitability in the middle of the dry belt.</summary>
    private const double DesertFloor = 0.30;

    /// <summary>Latitude past which nothing much lives, and where the falloff starts.</summary>
    private const double PolarLatitude = 72;
    private const double TemperateLatitude = 45;
    private const double PolarFloor = 0.12;

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
        int width, int height, MapConfig cfg)
    {
        double coastRange = Math.Max(1, cfg.Scaled(CoastRangeAtVanilla));
        double freshRange = Math.Max(1, cfg.Scaled(FreshwaterRangeAtVanilla));

        var toSea = DistanceTo(width, height, cell => mask[cell] != 1);
        var toFresh = DistanceTo(width, height,
            cell => rivers[cell] != 0 || lakes[cell] != 0 || mask[cell] != 1);

        double span = Math.Clamp(cfg.MapLatitudeSpan, 1, 180);
        double equatorRow = Math.Clamp(cfg.EquatorPosition, 0, 1) * height;

        var field = new float[width * height];

        Parallel.For(0, height, y =>
        {
            double latitude = Math.Abs((equatorRow - (y + 0.5)) / height * span);
            double climate = Climate(latitude);

            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;

                double coast = CoastWeight * Falloff(toSea[cell], coastRange);
                double fresh = FreshwaterWeight * Falloff(toFresh[cell], freshRange);
                double slope = Slope(elevation, mask, width, height, x, y);

                field[cell] = (float)Math.Clamp((BaseFertility + coast + fresh) * climate * slope,
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
    /// The latitude term: a wet tropical belt, a dry belt over the descending branch of the Hadley
    /// cells, a temperate optimum, then a slide into the polar dead zone.
    /// </summary>
    private static double Climate(double latitude)
    {
        // Dry belt, as a notch rather than a step so its edges are not lines on the map.
        double fromDesert = Math.Abs(latitude - DesertLatitude) / DesertWidth;
        double desert = 1 - (1 - DesertFloor) * Math.Exp(-fromDesert * fromDesert);

        // Cold. Nothing below the temperate line, then a smooth fall to the polar floor.
        double cold = latitude <= TemperateLatitude
            ? 1
            : 1 - (1 - PolarFloor) * Field.SmoothStep(TemperateLatitude, PolarLatitude, latitude);

        return desert * cold;
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
