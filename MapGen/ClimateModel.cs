using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Temperature and rainfall over the whole map, from atmospheric circulation rather than from
/// latitude alone.
///
/// What this replaces and why. ck2rpg decides climate by asking which latitude band a row is in,
/// and moisture by marching one cloud west to east along each row independently. Both are functions
/// of y, so both draw stripes; the only thing that ever bent one was an altitude term added to the
/// effective latitude, which is why the old maps had bands that ran clean across a continent and
/// then wrapped around a mountain range. No amount of noise fixes that, because a boundary that is
/// a function of y is still one seam across the map however much it wobbles.
///
/// A real climate map is not banded because the *ground* is banded — it is banded because the
/// atmosphere is, and then continents, oceans and mountains cut the bands to pieces. So this models
/// the pieces:
///
///   * **Three circulation cells per hemisphere.** Easterly trades to 30 degrees, westerlies to 60,
///     polar easterlies beyond. Air rises at the equator and at the polar front and sinks at 30 and
///     at the pole, which is why every large desert on Earth sits near 30 degrees and why the
///     latitudes either side of it are wet.
///   * **Moisture is advected, not assigned.** A parcel picks up water over sea, carries it downwind
///     across the map and rains it out as it goes, so a coast is wet, an interior is dry, and *which*
///     coast is wet depends on which way the wind blows at that latitude. This is the whole reason
///     western Europe and eastern China are both wet while the land between them is not.
///   * **Rain shadow falls out rather than being drawn.** Air cools as it climbs, cold air holds less
///     water, so the surplus condenses on the windward slope and the parcel arrives on the lee side
///     dry. Nothing here looks for a mountain; the range simply takes the water.
///   * **Two seasons.** The circulation and the thermal equator shift with the sun, which is what
///     produces a monsoon coast and a Mediterranean dry summer — and Koppen cannot be evaluated at
///     all without knowing which half of the year the rain falls in.
///
/// Everything is computed on a fixed-width coarse grid, deliberately: an air parcel then crosses the
/// same number of cells on every map, so a setting means the same thing at any resolution. The
/// fields are upsampled at the end and the altitude terms reapplied at full resolution, so a
/// mountain is as cold and as sharp-edged as the heightmap says it is.
/// </summary>
public sealed class ClimateField
{
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Annual mean temperature at the pixel's own altitude, degrees Celsius.</summary>
    public required float[] MeanC { get; init; }

    /// <summary>Warmest and coldest month. Koppen's tier boundaries are all one or the other.</summary>
    public required float[] WarmC { get; init; }
    public required float[] ColdC { get; init; }

    /// <summary>Rainfall in millimetres per year, and how it splits across the two half-years.</summary>
    public required float[] AnnualMm { get; init; }
    public required float[] SummerMm { get; init; }
    public required float[] WinterMm { get; init; }

    /// <summary>Signed latitude of each row, north positive.</summary>
    public required float[] LatitudeDeg { get; init; }
}

public static class ClimateModel
{
    /// <summary>
    /// Cells across, fixed rather than derived from the map. An air parcel crossing a continent then
    /// takes the same number of steps whatever size the map is rendered at, which is what makes
    /// <see cref="MapConfig.RainoutPer100Pixels"/> mean one thing everywhere. Deriving the grid from
    /// the raster instead would quietly make small maps wetter.
    /// </summary>
    private const int GridWidth = 1024;

    /// <summary>
    /// Sweeps of the advection. One sweep already carries moisture the full width of the map,
    /// because it is solved in wind order rather than iterated blindly; the extra passes are for
    /// the north-south coupling, which only moves a couple of cells per sweep.
    /// </summary>
    private const int Sweeps = 6;

    /// <summary>Degrees lost per kilometre of altitude. The standard environmental lapse rate.</summary>
    private const double LapseCPerKm = 6.5;

    /// <summary>How far the thermal equator and the whole cell structure move between the two
    /// seasons. Earth's ITCZ swings about this far either side of the equator.</summary>
    private const double ItczShiftDeg = 6.0;

    /// <summary>
    /// How much extra water descending air can hold before any of it condenses. 1 means a parcel
    /// under the strongest subsidence has to carry twice what it could at the same temperature under
    /// rising air before it rains at all. This is the subtropical desert, and nothing else in the
    /// model produces one.
    /// </summary>
    private const double SubsidenceDrying = 1.0;

    /// <summary>
    /// Share of the surplus that actually condenses in the cell where the air is first unable to
    /// hold it. Below 1 because condensation takes time and the parcel is still moving: see the note
    /// at the call site, where dumping all of it at once put a whole range's rain in one cell.
    /// </summary>
    private const double CondensationRate = 0.35;

    /// <summary>How far the rising/sinking pattern is biased toward sinking. See the note at the
    /// call site: a Hadley cell rises narrowly and descends broadly.</summary>
    private const double RisingBranchBias = 0.15;

    // The rest of the model's constants. These are settings in the sense that changing them changes
    // the map, but not in the sense that anyone should: they are properties of how an atmosphere
    // works rather than of the world being made, and every one of them is a number the real
    // atmosphere has. Exposing them would be eleven more knobs in the window that only ever want to
    // be left alone.

    /// <summary>Seasonal swing at the equator, where day length barely changes all year.</summary>
    private const double EquatorialSeasonalRangeC = 2.0;

    /// <summary>How much of the seasonal swing is down to continentality rather than latitude. At
    /// 0.45 a deep interior swings roughly three times as far as an open coast.</summary>
    private const double ContinentalAmplification = 0.4;

    /// <summary>
    /// How much warmer than its latitude an open coast runs at the pole, in degrees. The sea holds
    /// its heat through the winter and gives it back, which is why coastal Norway at 65 north is
    /// above freezing on the year and Yakutsk at 62 is nine degrees below it.
    ///
    /// This is not a refinement. Without it every high-latitude coast on the map comes out tundra:
    /// damping the seasonal range on a coast without also lifting its mean pulls the warmest month
    /// down as hard as it pulls the coldest month up, and the warmest month is what Koppen tests for
    /// polar. Measured, the whole map above 60 degrees classified as tundra.
    /// </summary>
    private const double MaritimeWarmingC = 8.0;

    /// <summary>Meridional surface wind as a share of the zonal. The real atmosphere's ratio is
    /// about this; it is what makes the trades blow from the north-east rather than due east.</summary>
    private const double MeridionalWindShare = 0.35;

    /// <summary>Noise on the wind direction, so a circulation cell has a frayed edge rather than a
    /// ruled one. Deliberately small — the point is to break the line, not to invent weather.</summary>
    private const double WindWanderStrength = 0.15;

    /// <summary>How fast a parcel over the sea recharges toward saturation, per cell crossed.</summary>
    private const double OceanEvaporation = 0.25;

    /// <summary>
    /// Share of what falls on land that goes straight back up and rains again further downwind.
    /// About a third of the rain over a continental interior on Earth was already inland, and
    /// without any recycling an interior comes out an absolute void rather than merely dry.
    ///
    /// Deliberately a share of the *rain that just fell*, not a relaxation toward saturation. Those
    /// are not the same model and the difference decides whether the map has deserts: relaxing
    /// toward saturation recharges hardest exactly where the air is hottest, so it pumps water into
    /// a desert that by construction has none on the ground to give. Measured, that alone was enough
    /// to leave the map with no arid class at all.
    /// </summary>
    private const double LandRecycling = 0.32;

    /// <summary>
    /// How far the relief is blurred, in vanilla province pixels, before the lapse rate reads it.
    /// See the note in <see cref="Build"/>: this is what keeps the classification off the drainage
    /// network.
    /// </summary>
    private const double ReliefBlurPixels = 45;

    public static ClimateField Build(MapConfig cfg, float[] provinceElevation, byte[] landMask, Rng rng)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        int pw = cfg.ProvinceWidth, ph = cfg.ProvinceHeight;
        int cw = Math.Min(GridWidth, pw);
        int ch = Math.Max(2, (int)((long)cw * ph / pw));

        float sea = cfg.Limits.SeaLevelUpper;

        // Elevation in kilometres above sea level, which is the only unit the lapse rate and the
        // saturation curve can both be written in. The heightmap carries no absolute scale, so the
        // map's own highest land is taken to be PeakElevationMetres.
        float peak = sea;
        for (int i = 0; i < provinceElevation.Length; i++)
            if (landMask[i] != 0 && provinceElevation[i] > peak) peak = provinceElevation[i];
        double metresPerUnit = cfg.PeakElevationMetres / Math.Max(1.0, peak - sea);

        // The relief the climate is allowed to see, which is not the relief the map ships.
        //
        // Applied at full resolution, the lapse rate turns every gully into its own climate: six and
        // a half degrees per kilometre is enough to push a valley floor across a Koppen boundary its
        // ridge is on the other side of, and the classification then comes out as a dendritic mess
        // of single-pixel zones tracing the drainage. Real climate does do this — a mountain valley
        // is genuinely warmer than the ridge above it — but not at a scale a strategy map can use.
        // Blurring first keeps the massifs and plateaus, which are what carry their own climate, and
        // discards the texture.
        // Water is flattened to sea level before the blur rather than blurred as it lies. Otherwise
        // every coastal land pixel is dragged toward the sea floor two hundred and fifty units below
        // it, and the blurred height comes out at zero along every shore — which the lapse rate then
        // reads as a whole coastline several degrees warmer than the ground behind it.
        int blur = (int)Math.Round(cfg.Scaled(ReliefBlurPixels));
        var relief = provinceElevation;

        if (blur >= 1)
        {
            var flattened = new float[provinceElevation.Length];
            for (int i = 0; i < flattened.Length; i++)
                flattened[i] = Math.Max(provinceElevation[i], sea);
            relief = Field.Blur(flattened, pw, ph, blur, 2);
        }

        var pixelKm = new float[relief.Length];
        for (int i = 0; i < pixelKm.Length; i++)
            pixelKm[i] = (float)(Math.Max(0, relief[i] - sea) * metresPerUnit / 1000.0);

        // The coarse grid takes the same smoothed relief, and that is not a detail.
        //
        // Its own area-averaging leaves plenty of cell-scale roughness, and the orographic term is a
        // first difference, so it answers to whatever the roughest thing in the field is. At half a
        // millimetre-equivalent per kilometre lifted, a hundred-metre bump between two cells buys
        // ten times the rain the base rain-out does over the same cell — so every scrap of texture
        // in the heightmap became a rainfall anomaly, and Koppen turned each one into its own patch
        // of climate. That was the mottling: the classifier was faithfully reporting a rainfall
        // field that was itself speckled.
        var kilometres = Resample(pixelKm, pw, ph, cw, ch);
        var water = ResampleMask(landMask, pw, ph, cw, ch);

        var latitude = Latitudes(cfg, ch);
        var continentality = Continentality(water, cw, ch, cfg);

        // Long-wavelength warmth and cold that latitude cannot account for: on Earth this is what
        // ocean currents do, and it is why Norway and Labrador sit at the same latitude in different
        // climates. Without it every isotherm is a parallel, and a temperature map of parallels is
        // half of what made the old output look ruled.
        var drift = TemperatureDrift(cw, ch, cfg, rng);

        var (annualC, seasonalRange) = Temperature(cfg, latitude, kilometres, continentality, drift, cw, ch);

        // July and January. Each run gets the season's own temperatures — which decide how much
        // water the air can hold — and its own displaced circulation.
        var july = Precipitation(cfg, latitude, kilometres, water, annualC, seasonalRange,
            cw, ch, ItczShiftDeg, rng);
        var january = Precipitation(cfg, latitude, kilometres, water, annualC, seasonalRange,
            cw, ch, -ItczShiftDeg, rng);

        var field = Assemble(cfg, pixelKm, kilometres, landMask, july, january, annualC,
            seasonalRange, cw, ch, pw, ph);

        Report(field, landMask, cfg, sw.ElapsedMilliseconds);
        return field;
    }

    /// <summary>
    /// Signed latitude of every coarse row. The equator sits where
    /// <see cref="MapConfig.EquatorPosition"/> puts it and the map spans
    /// <see cref="MapConfig.MapLatitudeSpan"/> degrees top to bottom.
    ///
    /// This one setting replaced the old band-width scales, and it is worth saying why. Those were
    /// authored in raster pixels against vanilla's 18432-wide map, so on anything smaller a single
    /// band could be wider than the whole map — and then no climate setting appeared to do anything
    /// at all, because there was no boundary on the map to move. Degrees have no such failure mode:
    /// a map is however much of a world its author says it is, at any resolution.
    /// </summary>
    private static float[] Latitudes(MapConfig cfg, int rows)
    {
        var latitude = new float[rows];
        double span = Math.Clamp(cfg.MapLatitudeSpan, 1, 180);
        double equatorRow = Math.Clamp(cfg.EquatorPosition, 0, 1) * rows;

        for (int y = 0; y < rows; y++)
            latitude[y] = (float)((equatorRow - (y + 0.5)) / rows * span);

        return latitude;
    }

    /// <summary>
    /// How far from the sea each cell is, as a 0-1 continentality. Drives the seasonal swing:
    /// the ocean is a flywheel, so a coast has mild winters and hot summers happen inland. It is
    /// what separates an oceanic climate from a continental one at the same latitude, and Koppen's
    /// C/D boundary is exactly that separation.
    /// </summary>
    private static float[] Continentality(byte[] water, int width, int height, MapConfig cfg)
    {
        var distance = new int[width * height];
        Array.Fill(distance, int.MaxValue);
        var frontier = new Queue<int>();

        for (int i = 0; i < water.Length; i++)
            if (water[i] == 0) { distance[i] = 0; frontier.Enqueue(i); }

        // An all-land map has no sea to be far from; treat it as uniformly continental.
        if (frontier.Count == 0)
        {
            var solid = new float[width * height];
            Array.Fill(solid, 1f);
            return solid;
        }

        while (frontier.Count > 0)
        {
            int cell = frontier.Dequeue();
            int x = cell % width, y = cell / width;

            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height) continue;

                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = ((x + dx) % width + width) % width;
                    int next = ny * width + nx;
                    if (distance[next] != int.MaxValue) continue;

                    distance[next] = distance[cell] + 1;
                    frontier.Enqueue(next);
                }
            }
        }

        // Saturating rather than linear: the first few hundred kilometres inland do nearly all the
        // work, and past that a continent does not keep getting more continental.
        double reach = Math.Max(1, cfg.ContinentalityPixels) / VanillaPixelsPerCell(width);
        var result = new float[width * height];
        for (int i = 0; i < result.Length; i++)
            result[i] = (float)(1.0 - Math.Exp(-distance[i] / reach));

        return result;
    }

    /// <summary>Width of one coarse cell in vanilla province pixels. Fixed by the grid width alone,
    /// which is the point of fixing the grid width.</summary>
    private static double VanillaPixelsPerCell(int width)
        => (double)MapConfig.ReferenceProvinceWidth / width;

    /// <summary>
    /// Warmth that latitude does not explain, at continental wavelength — the standing anomalies
    /// ocean currents leave. Warped fBm rather than plain, so the anomaly has arms and lobes instead
    /// of being a field of round blobs.
    /// </summary>
    private static float[] TemperatureDrift(int width, int height, MapConfig cfg, Rng rng)
    {
        var field = new float[width * height];
        double amplitude = cfg.TemperatureDriftC;
        if (amplitude <= 0) return field;

        var noise = new SimplexNoise(rng);
        var warp = new SimplexNoise(rng);
        double frequency = 1.5 / width;
        double warpAmplitude = width * 0.04;

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double qx = warp.Noise2D(x * frequency, y * frequency) * warpAmplitude;
                double qy = warp.Noise2D(x * frequency + 5.1, y * frequency - 8.3) * warpAmplitude;
                field[y * width + x] = (float)(amplitude *
                    Field.Fbm(noise, (x + qx) * frequency, (y + qy) * frequency, 3));
            }
        });

        return field;
    }

    /// <summary>
    /// Annual mean and seasonal swing on the coarse grid.
    ///
    /// The latitude curve is sine to the power 3.5, fitted against Earth's measured zonal annual
    /// means rather than picked for tidiness. The obvious choices are both badly wrong where it
    /// matters most: sine-squared is five degrees too cold across the whole subtropics, which pulls
    /// the coldest month below eighteen and costs the map its entire tropical tier, and a parabola
    /// in latitude is far too warm at the pole. This form holds within about three degrees from the
    /// equator to 50 and within three at the pole, which is inside what Koppen's thresholds resolve.
    /// </summary>
    private static (float[] Mean, float[] Range) Temperature(MapConfig cfg, float[] latitude,
        float[] kilometres, float[] continentality, float[] drift, int width, int height)
    {
        var mean = new float[width * height];
        var range = new float[width * height];

        double equatorC = cfg.EquatorTemperatureC;
        double poleC = cfg.PoleTemperatureC;

        Parallel.For(0, height, y =>
        {
            double phi = Math.Min(90, Math.Abs(latitude[y]));
            double radians = phi * Math.PI / 180.0;
            double fall = Math.Pow(Math.Sin(radians), 3.5);
            double sealevel = equatorC - (equatorC - poleC) * fall;

            // Seasonality is a latitude effect amplified by continentality: the tropics barely have
            // seasons at all, and the same latitude swings twice as far inland as it does on a coast.
            double swing = cfg.SeasonalRangeC * Math.Sin(radians);

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                mean[i] = (float)(sealevel + drift[i]
                                  + MaritimeWarmingC * (1.0 - continentality[i]) * fall
                                  - LapseCPerKm * kilometres[i]);
                range[i] = (float)((EquatorialSeasonalRangeC + swing)
                                   * (1.0 - ContinentalAmplification
                                      + ContinentalAmplification * 2.0 * continentality[i]));
            }
        });

        return (mean, range);
    }

    /// <summary>
    /// One season's rainfall, by advecting moisture across the map along the surface wind.
    ///
    /// The parcel is solved in wind order rather than relaxed: each sweep walks the grid downwind so
    /// a cell always reads an upwind neighbour that has already been updated this pass, which is why
    /// six sweeps suffice where blind iteration would need hundreds. Rows sweeping in opposite
    /// directions is not a bug — that *is* the subtropical high, and the divergence between them is
    /// what leaves the 30th parallel dry on both sides.
    ///
    /// The upwind sample is bilinear, at a fractional row. That single detail is what stops this
    /// producing ck2rpg's stripes: every cell mixes the two rows upwind of it, so a row can never
    /// carry its own independent moisture history the way a per-row cloud march does.
    /// </summary>
    private static float[] Precipitation(MapConfig cfg, float[] latitude, float[] kilometres,
        byte[] water, float[] annualC, float[] seasonalRange, int width, int height,
        double itczShift, Rng rng)
    {
        int n = width * height;
        var u = new float[n];
        var v = new float[n];
        var capacity = new float[n];
        var rain = new float[n];
        var moisture = new float[n];

        // A little noise on the wind so the cell boundaries are not ruled lines. Small: the point is
        // to fray the edge of a circulation cell, not to invent a different circulation.
        var noise = new SimplexNoise(rng);
        double frequency = 2.0 / width;
        double wobble = WindWanderStrength;

        Parallel.For(0, height, y =>
        {
            double phi = Math.Clamp(latitude[y] - itczShift, -90, 90);
            double abs = Math.Abs(phi);
            double hemisphere = phi >= 0 ? 1 : -1;

            // Zonal wind: easterly in the trades, westerly in the mid-latitudes, easterly again at
            // the pole. Zero at 0, 30, 60 and 90, which are the cell boundaries.
            double zonal = -Math.Cos(Math.PI * (abs - 15.0) / 30.0);

            // Meridional surface flow: equatorward in the trades and the polar easterlies,
            // poleward in the westerlies. Weaker than the zonal component, as on Earth.
            double poleward = -Math.Sin(Math.PI * abs / 30.0);

            // Where the air is rising, and therefore raining — the equator and the polar front —
            // against where it is sinking and therefore not: the subtropics and the pole.
            //
            // Biased downward rather than a plain cosine, because a Hadley cell is not symmetric.
            // Air rises in a narrow band a few degrees wide and comes back down spread across
            // twenty or thirty, which is why Earth's equatorial rainforest is a thin strip and its
            // subtropical desert belt is enormous. The bias moves the zero crossings from 15 and 45
            // degrees to about 13 and 47, narrowing the wet tropics and widening the deserts to
            // something like their real proportions.
            double uplift = Math.Clamp(Math.Cos(Math.PI * abs / 30.0) - RisingBranchBias, -1, 1);

            // The season's own temperature, which is what sets how much water the air can carry.
            double seasonSign = Math.Clamp(latitude[y] / 10.0, -1, 1) * Math.Sign(itczShift);

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                double du = noise.Noise2D(x * frequency, y * frequency) * wobble;
                double dv = noise.Noise2D(x * frequency + 11.3, y * frequency + 4.7) * wobble;

                double ux = zonal + du;
                // Never let a cell go purely meridional: the sweep steps one cell in x each time, so
                // a vanishing zonal component would ask for an infinite vertical step.
                if (Math.Abs(ux) < 0.15) ux = ux >= 0 ? 0.15 : -0.15;

                u[i] = (float)ux;
                v[i] = (float)(MeridionalWindShare * (poleward * hemisphere + dv));

                float temperature = (float)(annualC[i] + seasonalRange[i] * 0.5 * seasonSign);

                // Descending air is warming, and warming air moves *away* from saturation however
                // much water it is carrying. Raising the capacity under a sinking branch is what
                // encodes that, and it is what actually makes a subtropical desert: a parcel over
                // the Sahara is not short of water, it is short of any reason to give it up. Without
                // this the arriving air still condenses whatever it cannot hold and rains on the
                // desert, and the whole belt comes out temperate.
                capacity[i] = (float)(Saturation(temperature)
                                      * (1.0 + SubsidenceDrying * Math.Max(0, -uplift)));

                // Convective rainfall rides on the vertical motion of the circulation cell, so it is
                // strong under the rising branches and all but absent under the sinking ones.
                moisture[i] = capacity[i];
                rain[i] = (float)uplift;   // parked here; consumed as the uplift term below
            }
        });

        var uplifts = rain;
        rain = new float[n];

        double perCell = RainoutPerCell(cfg, width);
        double orographic = cfg.OrographicRainStrength;
        double convective = cfg.ConvectiveRainStrength;
        double oceanRecharge = OceanEvaporation;
        double landRecharge = LandRecycling;

        for (int sweep = 0; sweep < Sweeps; sweep++)
        {
            Advect(+1);
            Advect(-1);
        }

        return rain;

        void Advect(int direction)
        {
            Parallel.For(0, height, y =>
            {
                for (int step = 0; step < width; step++)
                {
                    int x = direction > 0 ? step : width - 1 - step;
                    int i = y * width + x;
                    if (Math.Sign(u[i]) != direction) continue;

                    // One cell upwind, at the fractional row the wind actually came from.
                    double slope = Math.Clamp(v[i] / Math.Abs(u[i]), -2.0, 2.0);
                    double sx = x - direction;
                    double sy = y - slope * direction;

                    float carried = Sample(moisture, width, height, sx, sy);
                    float upwindKm = Sample(kilometres, width, height, sx, sy);

                    float limit = capacity[i];

                    // What the air can no longer hold comes out. This is the rain shadow: climbing
                    // cools the parcel, a cooler parcel holds less, so the surplus is left on the
                    // windward slope and the lee side is handed dry air.
                    //
                    // Only part of the surplus falls per cell, though, and that matters. Dumping all
                    // of it the instant the parcel crosses a contour puts an entire mountain range's
                    // rain into the single cell where the air first rose — measured at a 90th
                    // percentile of over seven metres a year, which is four times the wettest
                    // ordinary ground on Earth. Condensing at a rate instead spreads the rain up the
                    // whole windward slope, which is where it falls in reality.
                    double condensed = Math.Max(0, carried - limit) * CondensationRate;
                    double remaining = carried - condensed;

                    if (water[i] == 0)
                    {
                        // Over the sea the parcel recharges toward saturation, which is where all
                        // the moisture on the map comes from.
                        moisture[i] = (float)(remaining + (limit - remaining) * oceanRecharge);
                        rain[i] = (float)condensed;
                        continue;
                    }

                    // The convective multiplier is allowed to reach zero, and that is the whole
                    // reason the map has subtropical deserts. Reducing the rain-out *fraction* under
                    // sinking air is not enough on its own: a parcel that rains more slowly still
                    // rains everything it carries eventually, just further along, so the total over
                    // a traverse barely changes. Air under a descending branch is warming, so
                    // nothing in it condenses at all — the moisture is exported to the rising branch
                    // instead, which is exactly why the Sahara is dry and the Congo is not.
                    double climb = Math.Max(0, kilometres[i] - upwindKm);
                    double fraction = Math.Clamp(
                        perCell * Math.Max(0, 1.0 + convective * uplifts[i]) + orographic * climb,
                        0, 1);

                    double fell = condensed + remaining * fraction;
                    double left = remaining - remaining * fraction;

                    // Land gives some of it straight back. See LandRecycling: a share of what just
                    // fell, so ground with no rain on it cannot moisten the air above it.
                    moisture[i] = (float)Math.Min(limit, left + fell * landRecharge);
                    rain[i] = (float)fell;
                }
            });
        }
    }

    /// <summary>
    /// Share of its remaining water a parcel drops per cell of land crossed, converted from the
    /// setting's per-100-vanilla-pixels form. Expressed that way so the number means a distance on
    /// the ground rather than a number of grid steps.
    /// </summary>
    private static double RainoutPerCell(MapConfig cfg, int width)
    {
        double perHundred = Math.Clamp(cfg.RainoutPer100Pixels, 0, 0.9);
        double cells = 100.0 / VanillaPixelsPerCell(width);
        return 1.0 - Math.Pow(1.0 - perHundred, 1.0 / Math.Max(1e-6, cells));
    }

    /// <summary>
    /// How much water air at a given temperature can hold, relative to 15 degrees, and the reason
    /// cold climates are dry ones however much ocean they face.
    ///
    /// Clausius-Clapeyron doubles about every eleven degrees. This uses fourteen, deliberately: the
    /// column of air a rain gauge sees does not scale with saturation vapour pressure alone, and at
    /// eleven the equator holds sixty times what the pole does, which put the map's wet tail four
    /// times above anything on Earth. Fourteen gives a pole-to-equator ratio near seventeen, which
    /// is about what the measured precipitable water column does.
    /// </summary>
    private static double Saturation(double celsius)
        => Math.Clamp(Math.Pow(2.0, (celsius - 15.0) / 14.0), 0.05, 6.0);

    /// <summary>
    /// Upsamples the coarse fields to province resolution and puts the altitude back in at full
    /// detail. Temperature is *recomputed* per pixel from the upsampled sea-level value rather than
    /// interpolated, so a ridge is as cold as its own height says and not as cold as the average of
    /// the cells around it.
    /// </summary>
    private static ClimateField Assemble(MapConfig cfg, float[] pixelKm, float[] coarseKm,
        byte[] landMask, float[] julyRain, float[] januaryRain, float[] annualC,
        float[] seasonalRange, int cw, int ch, int pw, int ph)
    {
        // Blurred by a cell or two before it is upsampled. The advection is solved cell by cell in
        // wind order, so neighbouring cells can end up with visibly different totals where their
        // upwind histories diverge — and Koppen turns any such step into a class boundary, which is
        // what left the arid belt speckled with single-cell rainforest. A cell is the model's own
        // resolution; smoothing at that scale discards nothing it actually resolved.
        var meanUp = Field.Upsample(Field.Blur(annualC, cw, ch, 1, 2), cw, ch, pw, ph);
        var rangeUp = Field.Upsample(Field.Blur(seasonalRange, cw, ch, 1, 2), cw, ch, pw, ph);
        var julyUp = Field.Upsample(Field.Blur(julyRain, cw, ch, 2, 3), cw, ch, pw, ph);
        var januaryUp = Field.Upsample(Field.Blur(januaryRain, cw, ch, 2, 3), cw, ch, pw, ph);

        // The coarse grid already applied a lapse for its own averaged height; the correction below
        // is only the difference between the pixel's height and that average, so nothing is counted
        // twice and the coarse and fine views agree wherever the ground is flat.
        var coarseKmUp = Field.Upsample(coarseKm, cw, ch, pw, ph);

        var latitude = Latitudes(cfg, ph);

        var mean = new float[pw * ph];
        var warm = new float[pw * ph];
        var cold = new float[pw * ph];
        var summer = new float[pw * ph];
        var winter = new float[pw * ph];
        var annual = new float[pw * ph];

        Parallel.For(0, ph, y =>
        {
            // Which half of the year is summer flips at the equator.
            bool northern = latitude[y] >= 0;

            for (int x = 0; x < pw; x++)
            {
                int i = y * pw + x;

                double correction = LapseCPerKm * (pixelKm[i] - coarseKmUp[i]);

                float m = (float)(meanUp[i] - correction);
                float half = Math.Max(0f, rangeUp[i]) * 0.5f;

                mean[i] = m;
                warm[i] = m + half;
                cold[i] = m - half;

                summer[i] = Math.Max(0f, northern ? julyUp[i] : januaryUp[i]);
                winter[i] = Math.Max(0f, northern ? januaryUp[i] : julyUp[i]);
                annual[i] = summer[i] + winter[i];
            }
        });

        // The model's rainfall is in no particular unit, so it is put onto millimetres by its own
        // *median* over land. Scaling rather than stretching keeps the shape of the distribution —
        // an arid world stays arid relative to itself — while giving Koppen's thresholds, which are
        // in real millimetres, something real to test against.
        //
        // The median and not the mean, and the difference is not cosmetic. Rainfall is heavily
        // right-skewed: a windward slope or an equatorial coast takes several thousand millimetres
        // where ordinary land takes a few hundred, so the mean sits far above the middle of the map
        // and pinning it drags everything else down. Measured, pinning the mean at 750 left the
        // median land pixel on 351 mm — below Koppen's arid threshold nearly everywhere, so half the
        // world came out steppe or desert. The median is not moved by the wet tail at all.
        var sample = new List<float>();
        for (int i = 0; i < annual.Length; i += 7)
            if (landMask[i] != 0) sample.Add(annual[i]);

        if (sample.Count > 0)
        {
            sample.Sort();
            float median = sample[sample.Count / 2];

            if (median > 0)
            {
                float scale = (float)(cfg.MedianRainfallMm / median);
                for (int i = 0; i < annual.Length; i++)
                {
                    summer[i] *= scale;
                    winter[i] *= scale;
                    annual[i] *= scale;
                }
            }
        }

        return new ClimateField
        {
            Width = pw,
            Height = ph,
            MeanC = mean,
            WarmC = warm,
            ColdC = cold,
            AnnualMm = annual,
            SummerMm = summer,
            WinterMm = winter,
            LatitudeDeg = latitude,
        };
    }

    /// <summary>
    /// States what the model produced, in the units it is supposed to produce them in. A climate
    /// model whose numbers are never printed is a climate model nobody can tell is wrong: these are
    /// directly comparable with real zonal means, and a map whose warmest land is 45 degrees or
    /// whose driest decile is 900 mm has a setting wrong somewhere.
    /// </summary>
    private static void Report(ClimateField field, byte[] landMask, MapConfig cfg, long elapsedMs)
    {
        var temperatures = new List<float>();
        var rainfall = new List<float>();

        for (int i = 0; i < landMask.Length; i += 7)
        {
            if (landMask[i] == 0) continue;
            temperatures.Add(field.MeanC[i]);
            rainfall.Add(field.AnnualMm[i]);
        }

        if (temperatures.Count == 0) return;

        temperatures.Sort();
        rainfall.Sort();

        double north = field.LatitudeDeg[0];
        double south = field.LatitudeDeg[^1];

        Console.WriteLine($"  climate: map spans {north:F0}° to {south:F0}° latitude " +
                          $"({cfg.MapLatitudeSpan:F0}° tall, equator at " +
                          $"{cfg.EquatorPosition * 100:F0}% down)");
        Console.WriteLine($"    land temperature p10 {Percentile(temperatures, 0.1):F0}°C / " +
                          $"median {Percentile(temperatures, 0.5):F0}°C / " +
                          $"p90 {Percentile(temperatures, 0.9):F0}°C");
        Console.WriteLine($"    land rainfall p10 {Percentile(rainfall, 0.1):F0} / " +
                          $"median {Percentile(rainfall, 0.5):F0} / " +
                          $"p90 {Percentile(rainfall, 0.9):F0} mm ({elapsedMs} ms)");

        static float Percentile(List<float> sorted, double q)
            => sorted[Math.Clamp((int)(sorted.Count * q), 0, sorted.Count - 1)];
    }

    /// <summary>Area-average resample to an arbitrary smaller size. Averaging rather than point
    /// sampling, so a single peak cannot decide a whole cell's climate.</summary>
    private static float[] Resample(float[] source, int sw, int sh, int dw, int dh)
    {
        var result = new float[dw * dh];

        Parallel.For(0, dh, y =>
        {
            int y0 = (int)((long)y * sh / dh), y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * sh / dh));

            for (int x = 0; x < dw; x++)
            {
                int x0 = (int)((long)x * sw / dw), x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * sw / dw));

                double sum = 0;
                int n = 0;
                for (int j = y0; j < y1 && j < sh; j++)
                    for (int i = x0; i < x1 && i < sw; i++) { sum += source[j * sw + i]; n++; }

                result[y * dw + x] = n == 0 ? 0 : (float)(sum / n);
            }
        });

        return result;
    }

    /// <summary>Majority-vote resample of the land mask, so a coarse cell is land when most of it
    /// is. Returns 1 for land, 0 for water — the same convention the mask itself uses.</summary>
    private static byte[] ResampleMask(byte[] source, int sw, int sh, int dw, int dh)
    {
        var result = new byte[dw * dh];

        Parallel.For(0, dh, y =>
        {
            int y0 = (int)((long)y * sh / dh), y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * sh / dh));

            for (int x = 0; x < dw; x++)
            {
                int x0 = (int)((long)x * sw / dw), x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * sw / dw));

                int land = 0, total = 0;
                for (int j = y0; j < y1 && j < sh; j++)
                    for (int i = x0; i < x1 && i < sw; i++) { land += source[j * sw + i]; total++; }

                result[y * dw + x] = total > 0 && land * 2 >= total ? (byte)1 : (byte)0;
            }
        });

        return result;
    }

    /// <summary>Bilinear sample that wraps in X and clamps in Y — the wind crosses the date line.</summary>
    private static float Sample(float[] field, int width, int height, double x, double y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;

        int xa = ((x0 % width) + width) % width, xb = (((x0 + 1) % width) + width) % width;
        int ya = Math.Clamp(y0, 0, height - 1), yb = Math.Clamp(y0 + 1, 0, height - 1);

        double top = field[ya * width + xa] * (1 - fx) + field[ya * width + xb] * fx;
        double bottom = field[yb * width + xa] * (1 - fx) + field[yb * width + xb] * fx;
        return (float)(top + (bottom - top) * fy);
    }
}
