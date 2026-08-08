using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Adds real terrain detail at heightmap resolution.
/// </summary>
public static class HeightDetail
{
    /// <summary>
    /// Adds detail in place. <paramref name="elevation"/> is at heightmap resolution and in the
    /// simulation's elevation units, the same scale <see cref="MapConfig.Limits"/> is authored in.
    /// </summary>
    public static void Apply(float[] elevation, MapConfig cfg, Rng rng)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int width = cfg.Width, height = cfg.Height;

        var warpNoise = new SimplexNoise(rng);
        var ridgeNoise = new SimplexNoise(rng);
        var fbmNoise = new SimplexNoise(rng);
        var beltNoise = new SimplexNoise(rng);
        var crestNoise = new SimplexNoise(rng);

        double baseFreq = 220.0 / width;
        double warpFreq = 60.0 / width;
        double beltFreq = 14.0 / width;
        double crestFreq = 26.0 / width;

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float b = elevation[i];

                if (b <= sea) continue;

                int mountainLower = cfg.Limits.Mountains.Lower;
                double relief = Math.Clamp((b - sea) / (double)Math.Max(1, mountainLower - sea), 0, 1);
                double shore = Math.Clamp((b - sea) / 18.0, 0, 1);

                double wx = x * warpFreq, wy = y * warpFreq;
                double warpX = warpNoise.Noise2D(wx, wy) * 40.0;
                double warpY = warpNoise.Noise2D(wx + 31.4, wy - 17.2) * 40.0;

                double nx = (x + warpX) * baseFreq, ny = (y + warpY) * baseFreq;

                double ridge = Ridged(ridgeNoise, nx, ny, octaves: 6);
                double rolling = Fbm(fbmNoise, nx * 0.6, ny * 0.6, octaves: 5);

                double belt = Ridged(beltNoise, x * beltFreq, y * beltFreq, octaves: 2);
                belt = Math.Clamp(belt * 0.5 + 0.5, 0, 1);
                belt = belt * belt * (3.0 - 2.0 * belt);

                double crest = crestNoise.Unit(x * crestFreq, y * crestFreq);
                crest = 0.45 + 0.85 * crest;

                double mountainous = relief * relief * belt;
                double detail = ridge * mountainous + rolling * (1.0 - mountainous) * 0.45;

                double amplitude = (10.0 + 95.0 * mountainous * crest) * shore;
                elevation[i] = (float)(b + detail * amplitude);
            }
        });
    }

    /// <summary>
    /// Sinks and smooths the seafloor into a shelf that deepens with distance from land.
    /// </summary>
    public static void ShapeSeafloor(float[] elevation, MapConfig cfg, int shelfPixels)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int width = cfg.Width, height = cfg.Height;

        int step = Math.Max(1, shelfPixels / 4);
        int cw = (width + step - 1) / step, ch = (height + step - 1) / step;

        var land = new float[cw * ch];
        Parallel.For(0, ch, cy =>
        {
            for (int cx = 0; cx < cw; cx++)
            {
                int x0 = cx * step, y0 = cy * step;
                int x1 = Math.Min(x0 + step, width), y1 = Math.Min(y0 + step, height);
                int total = 0, hits = 0;
                for (int y = y0; y < y1; y += 2)
                    for (int x = x0; x < x1; x += 2)
                    {
                        total++;
                        if (elevation[y * width + x] > sea) hits++;
                    }
                land[cy * cw + cx] = total == 0 ? 0 : (float)hits / total;
            }
        });

        var blurred = Blur(land, cw, ch, passes: 4, radius: 2);

        Parallel.For(0, height, y =>
        {
            double gy = (double)y / step - 0.5;
            int y0 = (int)Math.Floor(gy);
            double fy = gy - y0;
            int y0c = Math.Clamp(y0, 0, ch - 1), y1c = Math.Clamp(y0 + 1, 0, ch - 1);

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (elevation[i] > sea) continue;

                double gx = (double)x / step - 0.5;
                int x0 = (int)Math.Floor(gx);
                double fx = gx - x0;
                int x0c = Math.Clamp(x0, 0, cw - 1), x1c = Math.Clamp(x0 + 1, 0, cw - 1);

                double top = blurred[y0c * cw + x0c] * (1 - fx) + blurred[y0c * cw + x1c] * fx;
                double bottom = blurred[y1c * cw + x0c] * (1 - fx) + blurred[y1c * cw + x1c] * fx;
                double nearness = Math.Clamp(top * (1 - fy) + bottom * fy, 0, 1);

                // ACCENTUATED DROP-OFF:
                // Using (1.0 - nearness^3) causes the shelf to drop off rapidly near the coast, 
                // transitioning quickly into deep water and then leveling out.
                double depth = 1.0 - (nearness * nearness * nearness);

                elevation[i] = (float)(sea - 1 - depth * (sea * 0.8));
            }
        });
    }

    /// <summary>
    /// Cuts a valley into the terrain along every river course.
    /// </summary>
    public static void CarveRivers(float[] elevation, MapConfig cfg, byte[] riverMask,
        int maskWidth, int maskHeight)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int width = cfg.Width, height = cfg.Height;

        double scaleX = (double)maskWidth / width, scaleY = (double)maskHeight / height;
        int radius = Math.Max(3, width / 1400);
        double depth = 9.0;

        var influence = new float[(long)width * height];

        Parallel.For(0, height, y =>
        {
            int my = Math.Clamp((int)(y * scaleY), 0, maskHeight - 1);
            for (int x = 0; x < width; x++)
            {
                int mx = Math.Clamp((int)(x * scaleX), 0, maskWidth - 1);

                int best = radius + 1;
                for (int dy = -radius; dy <= radius && best > 0; dy++)
                {
                    int yy = my + dy;
                    if (yy < 0 || yy >= maskHeight) continue;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int xx = mx + dx;
                        if (xx < 0 || xx >= maskWidth) continue;
                        if (riverMask[yy * maskWidth + xx] == 0) continue;

                        int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        if (d < best) best = d;
                    }
                }

                if (best > radius) continue;
                influence[(long)y * width + x] = (float)(1.0 - (double)best / (radius + 1));
            }
        });

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                long i = (long)y * width + x;
                float w = influence[i];
                if (w <= 0) continue;

                float e = elevation[i];
                if (e <= sea) continue;

                double cut = depth * w * w;
                elevation[i] = (float)Math.Max(sea + 1, e - cut);
            }
        });
    }

    private static float[] Blur(float[] src, int w, int h, int passes, int radius)
    {
        var a = (float[])src.Clone();
        var b = new float[src.Length];

        for (int p = 0; p < passes; p++)
        {
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = 0; int n = 0;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            sum += a[yy * w + xx];
                            n++;
                        }
                    }
                    b[y * w + x] = sum / n;
                }
            });
            (a, b) = (b, a);
        }

        return a;
    }

    private static double Ridged(SimplexNoise noise, double x, double y, int octaves)
    {
        double sum = 0, amp = 0.5, freq = 1.0, norm = 0;
        double weight = 1.0;

        for (int o = 0; o < octaves; o++)
        {
            double n = 1.0 - Math.Abs(noise.Noise2D(x * freq, y * freq));
            n *= n;
            n *= weight;
            weight = Math.Clamp(n * 2.0, 0, 1);

            sum += n * amp;
            norm += amp;
            freq *= 2.0;
            amp *= 0.5;
        }

        return norm == 0 ? 0 : sum / norm * 2.0 - 1.0;
    }

    private static double Fbm(SimplexNoise noise, double x, double y, int octaves)
    {
        double sum = 0, amp = 0.5, freq = 1.0, norm = 0;

        for (int o = 0; o < octaves; o++)
        {
            sum += noise.Noise2D(x * freq, y * freq) * amp;
            norm += amp;
            freq *= 2.0;
            amp *= 0.5;
        }

        return norm == 0 ? 0 : sum / norm;
    }
}