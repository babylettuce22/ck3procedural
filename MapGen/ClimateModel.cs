using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public sealed class ClimateField
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required float[] MeanC { get; init; }
    public required float[] WarmC { get; init; }
    public required float[] ColdC { get; init; }
    public required float[] AnnualMm { get; init; }
    public required float[] SummerMm { get; init; }
    public required float[] WinterMm { get; init; }
    public required float[] LatitudeDeg { get; init; }
}

public static class ClimateModel
{
    private const int GridWidth = 1024;
    private const int Sweeps = 6;
    private const double LapseCPerKm = 6.5;
    private const double ItczShiftDeg = 6.0;

    // Subsidence drying is now modulated by continentality so coasts stay humid
    private const double SubsidenceDrying = 0.60;
    private const double CondensationRate = 0.35;
    private const double RisingBranchBias = 0.10;

    private const double EquatorialSeasonalRangeC = 2.0;
    private const double ContinentalAmplification = 0.4;
    private const double MaritimeWarmingC = 8.0;
    private const double MeridionalWindShare = 0.35;
    private const double WindWanderStrength = 0.05;
    private const double OceanEvaporation = 0.30;
    private const double LandRecycling = 0.48;
    private const double ReliefBlurPixels = 140;

    public static ClimateField Build(MapConfig cfg, float[] provinceElevation, byte[] landMask, Rng rng)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        int pw = cfg.ProvinceWidth, ph = cfg.ProvinceHeight;
        int cw = Math.Min(GridWidth, pw);
        int ch = Math.Max(2, (int)((long)cw * ph / pw));

        float sea = cfg.Limits.SeaLevelUpper;

        float peak = sea;
        for (int i = 0; i < provinceElevation.Length; i++)
            if (landMask[i] != 0 && provinceElevation[i] > peak) peak = provinceElevation[i];
        double metresPerUnit = cfg.PeakElevationMetres / Math.Max(1.0, peak - sea);

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

        var kilometres = Resample(pixelKm, pw, ph, cw, ch);
        var water = ResampleMask(landMask, pw, ph, cw, ch);

        var latitude = Latitudes(cfg, ch);
        var continentality = Continentality(water, cw, ch, cfg);
        var drift = TemperatureDrift(cw, ch, cfg, rng);

        var (annualC, seasonalRange) = Temperature(cfg, latitude, kilometres, continentality, drift, cw, ch);

        var july = Precipitation(cfg, latitude, kilometres, water, continentality, annualC, seasonalRange,
            cw, ch, ItczShiftDeg, rng);
        var january = Precipitation(cfg, latitude, kilometres, water, continentality, annualC, seasonalRange,
            cw, ch, -ItczShiftDeg, rng);

        var field = Assemble(cfg, pixelKm, kilometres, landMask, july, january, annualC,
            seasonalRange, cw, ch, pw, ph);

        Report(field, landMask, cfg, sw.ElapsedMilliseconds);
        return field;
    }

    private static float[] Latitudes(MapConfig cfg, int rows)
    {
        var latitude = new float[rows];
        double span = Math.Clamp(cfg.MapLatitudeSpan, 1, 180);
        double equatorRow = cfg.EquatorPosition * rows;

        for (int y = 0; y < rows; y++)
            latitude[y] = (float)((equatorRow - (y + 0.5)) / rows * span);

        return latitude;
    }

    private static float[] Continentality(byte[] water, int width, int height, MapConfig cfg)
    {
        var distance = new int[width * height];
        Array.Fill(distance, int.MaxValue);
        var frontier = new Queue<int>();

        for (int i = 0; i < water.Length; i++)
            if (water[i] == 0) { distance[i] = 0; frontier.Enqueue(i); }

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

        double reach = Math.Max(1, cfg.ContinentalityPixels) / VanillaPixelsPerCell(width);
        var result = new float[width * height];
        for (int i = 0; i < result.Length; i++)
            result[i] = (float)(1.0 - Math.Exp(-distance[i] / reach));

        return result;
    }

    private static double VanillaPixelsPerCell(int width)
        => (double)MapConfig.ReferenceProvinceWidth / width;

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

    private static float[] Precipitation(MapConfig cfg, float[] latitude, float[] kilometres,
        byte[] water, float[] continentality, float[] annualC, float[] seasonalRange, int width, int height,
        double itczShift, Rng rng)
    {
        int n = width * height;
        var u = new float[n];
        var v = new float[n];
        var capacity = new float[n];
        var rain = new float[n];
        var moisture = new float[n];

        var noise = new SimplexNoise(rng);
        double frequency = 2.0 / width;
        double wobble = WindWanderStrength;

        Parallel.For(0, height, y =>
        {
            double phi = Math.Clamp(latitude[y] - itczShift, -90, 90);
            double abs = Math.Abs(phi);
            double hemisphere = phi >= 0 ? 1 : -1;

            double zonal = -Math.Cos(Math.PI * (abs - 15.0) / 30.0);
            double poleward = -Math.Sin(Math.PI * abs / 30.0);
            double uplift = Math.Clamp(Math.Cos(Math.PI * abs / 30.0) - RisingBranchBias, -1, 1);

            double seasonSign = Math.Clamp(latitude[y] / 10.0, -1, 1) * Math.Sign(itczShift);

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                double du = noise.Noise2D(x * frequency, y * frequency) * wobble;
                double dv = noise.Noise2D(x * frequency + 11.3, y * frequency + 4.7) * wobble;

                double ux = zonal + du;
                if (Math.Abs(ux) < 0.15) ux = ux >= 0 ? 0.15 : -0.15;

                u[i] = (float)ux;
                v[i] = (float)(MeridionalWindShare * (poleward * hemisphere + dv));

                float temperature = (float)(annualC[i] + seasonalRange[i] * 0.5 * seasonSign);

                // Subsidence drying scales with continentality:
                // Coastal maritime areas resist subsidence; deep interiors become arid deserts
                float cont = continentality[i];
                double effectiveSubsidence = SubsidenceDrying * (0.2 + 0.8 * cont);

                capacity[i] = (float)(Saturation(temperature)
                                      * (1.0 + effectiveSubsidence * Math.Max(0, -uplift)));

                moisture[i] = capacity[i];
                rain[i] = (float)uplift;
            }
        });

        var uplifts = rain;
        rain = new float[n];

        double perCell = RainoutPerCell(cfg, width);
        double orographic = cfg.OrographicRainStrength;
        double convective = cfg.ConvectiveRainStrength;

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

                    double slope = Math.Clamp(v[i] / Math.Abs(u[i]), -2.0, 2.0);
                    double sx = x - direction;
                    double sy = y - slope * direction;

                    float carried = Sample(moisture, width, height, sx, sy);
                    float upwindKm = Sample(kilometres, width, height, sx, sy);

                    float limit = capacity[i];
                    double condensed = Math.Max(0, carried - limit) * CondensationRate;
                    double remaining = carried - condensed;

                    if (water[i] == 0)
                    {
                        // Warm tropical and subtropical waters evaporate more rapidly
                        float temp = annualC[i];
                        double warmWaterBonus = Math.Clamp((temp - 10.0) / 20.0, 0.0, 1.0) * 0.20;
                        double oceanRecharge = OceanEvaporation + warmWaterBonus;

                        moisture[i] = (float)(remaining + (limit - remaining) * oceanRecharge);
                        rain[i] = (float)condensed;
                        continue;
                    }

                    double climb = Math.Max(0, kilometres[i] - upwindKm);
                    float cont = continentality[i];

                    // Maritime onshore air retains convective lift even under mild subtropical subsidence
                    double upliftFactor = Math.Max(0.15, 1.0 + convective * uplifts[i] * (0.3 + 0.7 * cont));
                    double fraction = Math.Clamp(perCell * upliftFactor + orographic * climb, 0, 1);

                    double fell = condensed + remaining * fraction;
                    double left = remaining - remaining * fraction;

                    moisture[i] = (float)Math.Min(limit, left + fell * LandRecycling);
                    rain[i] = (float)fell;
                }
            });
        }
    }

    private static double RainoutPerCell(MapConfig cfg, int width)
    {
        double perHundred = Math.Clamp(cfg.RainoutPer100Pixels, 0, 0.9);
        double cells = 100.0 / VanillaPixelsPerCell(width);
        return 1.0 - Math.Pow(1.0 - perHundred, 1.0 / Math.Max(1e-6, cells));
    }

    private static double Saturation(double celsius)
        => Math.Clamp(Math.Pow(2.0, (celsius - 15.0) / 14.0), 0.05, 6.0);

    private static ClimateField Assemble(MapConfig cfg, float[] pixelKm, float[] coarseKm,
        byte[] landMask, float[] julyRain, float[] januaryRain, float[] annualC,
        float[] seasonalRange, int cw, int ch, int pw, int ph)
    {
        var meanUp = Field.Upsample(Field.Blur(annualC, cw, ch, 3, 3), cw, ch, pw, ph);
        var rangeUp = Field.Upsample(Field.Blur(seasonalRange, cw, ch, 3, 3), cw, ch, pw, ph);
        var julyUp = Field.Upsample(Field.Blur(julyRain, cw, ch, 4, 3), cw, ch, pw, ph);
        var januaryUp = Field.Upsample(Field.Blur(januaryRain, cw, ch, 4, 3), cw, ch, pw, ph);

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