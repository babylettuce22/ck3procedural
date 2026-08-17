using Ck3MapGen.Config;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

public static class HeightmapNormalizer
{
    public static ushort[] Normalize(ushort[] raw, MapConfig cfg)
    {
        if (cfg.Normalization == HeightmapNormalization.Off) return raw;

        int width = cfg.Width;
        int height = cfg.Height;
        int sourceSea = (int)Math.Round(Math.Clamp(cfg.SourceSeaLevel, 0, 254) * MapDataWriter.Step255);

        var histogram = new int[65536];
        long landCount = 0;
        int landMin = ushort.MaxValue;

        foreach (ushort v in raw)
        {
            if (v <= sourceSea) continue;
            histogram[v]++;
            landCount++;
            if (v < landMin) landMin = v;
        }

        if (landCount == 0)
        {
            Console.WriteLine($"  WARNING: normalisation skipped — no pixel sits above the source " +
                              $"sea level of {cfg.SourceSeaLevel:F0}/255.");
            return raw;
        }

        const int water16 = MapDataWriter.WaterLevel16;        // ~4883 (19/255)
        const int lowestLand = water16 + MapDataWriter.Step255; // ~5140 (20/255)

        int landFloor = DetectLandFloor(histogram, landMin, cfg.LandFloorDensity);

        long clippedBelow = 0;
        for (int v = landMin; v < landFloor; v++) clippedBelow += histogram[v];

        long anchored = landCount - clippedBelow;
        var want = (long)(anchored * Math.Clamp(cfg.LandTopPercentile, 0, 100) / 100.0);
        long running = 0;
        int landTop = landFloor;

        for (int v = landFloor; v < histogram.Length; v++)
        {
            running += histogram[v];
            if (running < want) continue;
            landTop = v;
            break;
        }

        if (landTop <= landFloor)
        {
            int landMax = landFloor;
            for (int v = histogram.Length - 1; v > landFloor; v--)
                if (histogram[v] != 0) { landMax = v; break; }

            landTop = Math.Max(landFloor + 1, landMax);
        }

        long clippedAbove = 0;
        for (int v = landTop + 1; v < histogram.Length; v++) clippedAbove += histogram[v];

        int topLand = Math.Clamp(
            (int)Math.Round(cfg.LandTop * MapDataWriter.Step255), lowestLand, 65535);

        bool stretch = cfg.Normalization == HeightmapNormalization.Stretch;

        double landSpan = Math.Max(1, landTop - landFloor);
        double landRange = topLand - lowestLand;
        double seaSpan = Math.Max(1, sourceSea);
        double drop = Math.Max(0, landFloor - lowestLand);

        var result = new ushort[raw.Length];
        const int maxWaterAllowed = water16 - MapDataWriter.Step255 * 6;

        // 1. Base normalisation pass
        Parallel.For(0, raw.Length, i =>
        {
            int v = raw[i];
            double scaled;

            if (v > sourceSea)
            {
                if (stretch)
                {
                    if (v < landFloor)
                    {
                        double t = (double)(v - sourceSea) / Math.Max(1, landFloor - sourceSea);
                        scaled = lowestLand + t * (MapDataWriter.Step255 * 2.0);
                    }
                    else
                    {
                        scaled = lowestLand + Math.Min(1.0, (double)(v - landFloor) / landSpan) * landRange;
                    }
                }
                else
                {
                    scaled = v - drop;
                }

                if (scaled < lowestLand) scaled = lowestLand;
            }
            else
            {
                if (sourceSea == 0 || v == 0)
                {
                    scaled = 0;
                }
                else
                {
                    scaled = (double)v / seaSpan * maxWaterAllowed;
                    if (v >= sourceSea - MapDataWriter.Step255 * 2) scaled = 0;
                }
            }

            result[i] = (ushort)Math.Clamp(Math.Round(scaled), 0, 65535);
        });

        // 2. Configurable Inward Coastal Cliff Bevel / Smoothing
        if (cfg.CoastalCliffSmoothing > 0 && cfg.CoastalCliffReach > 0)
        {
            ApplyInwardShoreCliffBevel(result, width, height, lowestLand, cfg.CoastalCliffReach, (float)cfg.CoastalCliffSmoothing);
        }

        double sourceWaterShare = 100.0 * (raw.LongLength - landCount) / raw.LongLength;
        double floor255 = (double)landFloor / MapDataWriter.Step255;
        double top255 = (double)landTop / MapDataWriter.Step255;

        Console.WriteLine($"  normalised ({cfg.Normalization}): source sea " +
                          $"{cfg.SourceSeaLevel:F0}/255 " +
                          $"({sourceWaterShare:F2}% of the map at or below it) → " +
                          $"{MapDataWriter.WaterLevel255}/255");

        Console.WriteLine($"  land floor detected at {floor255:F2}/255 " +
                          $"(lowest land pixel {(double)landMin / MapDataWriter.Step255:F2})");

        return result;
    }

    private static void ApplyInwardShoreCliffBevel(
        ushort[] heightmap,
        int width,
        int height,
        int lowestLand,
        int reach,
        float strength)
    {
        int maxDist = Math.Clamp(reach, 1, 16);
        strength = Math.Clamp(strength, 0.0f, 1.0f);

        var distToWater = new byte[heightmap.Length];

        // Pass 1: Tag immediate shore land pixels (distance = 1)
        Parallel.For(0, height, y =>
        {
            long row = (long)y * width;
            for (int x = 0; x < width; x++)
            {
                long idx = row + x;
                if (heightmap[idx] < lowestLand)
                {
                    distToWater[idx] = 0; // Water
                    continue;
                }

                bool touchesWater = false;
                for (int dy = -1; dy <= 1 && !touchesWater; dy++)
                {
                    int ny = Math.Clamp(y + dy, 0, height - 1);
                    long nrow = (long)ny * width;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = Math.Clamp(x + dx, 0, width - 1);
                        if (heightmap[nrow + nx] < lowestLand)
                        {
                            touchesWater = true;
                            break;
                        }
                    }
                }

                distToWater[idx] = touchesWater ? (byte)1 : (byte)255;
            }
        });

        // Pass 2: Expand distance rings inland up to maxDist
        for (byte d = 1; d < maxDist; d++)
        {
            byte cur = d;
            byte next = (byte)(d + 1);
            Parallel.For(0, height, y =>
            {
                long row = (long)y * width;
                for (int x = 0; x < width; x++)
                {
                    long idx = row + x;
                    if (distToWater[idx] != 255) continue;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = Math.Clamp(y + dy, 0, height - 1);
                        long nrow = (long)ny * width;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = Math.Clamp(x + dx, 0, width - 1);
                            if (distToWater[nrow + nx] == cur)
                            {
                                distToWater[idx] = next;
                                goto advanced;
                            }
                        }
                    }

                    // Jump to the next pixel, not out of the row. This was `return`, which exits
                    // the Parallel.For body — so each expansion pass advanced at most one pixel
                    // per row and every ring above 1 stayed unreachable. Pass 3 then skipped all
                    // of them, and the configured ramp was in practice a one-pixel notch cut into
                    // the shore with the cliff left standing behind it, whatever CoastalCliffReach
                    // was set to.
                    advanced: ;
                }
            });
        }

        // Pass 3: Smoothstep inward ramp
        Parallel.For(0, height, y =>
        {
            long row = (long)y * width;
            for (int x = 0; x < width; x++)
            {
                long idx = row + x;
                byte d = distToWater[idx];
                if (d == 0 || d > maxDist) continue; // Water or untouched interior

                int original = heightmap[idx];
                int excess = original - lowestLand;
                if (excess <= 0) continue;

                // Normalized distance fraction [0..1]
                float t = (float)d / (maxDist + 1);

                // Hermite smoothstep curve: zero derivative at coast and interior
                float curve = t * t * (3.0f - 2.0f * t);

                // Calculate beveled elevation and blend by user strength
                int ramped = lowestLand + (int)Math.Round(excess * curve);
                int finalElev = (int)Math.Round(original * (1.0f - strength) + ramped * strength);

                heightmap[idx] = (ushort)Math.Clamp(finalElev, lowestLand, 65535);
            }
        });
    }

    private static int DetectLandFloor(int[] histogram, int landMin, double density)
    {
        if (density <= 0) return landMin;

        var coarse = new long[256];
        for (int v = landMin; v < histogram.Length; v++)
            coarse[v / MapDataWriter.Step255] += histogram[v];

        int mode = 0;
        for (int b = 1; b < coarse.Length; b++)
            if (coarse[b] > coarse[mode]) mode = b;

        double collapse = coarse[mode] * Math.Clamp(density, 0, 1);
        int lowest = landMin / MapDataWriter.Step255;

        int floor = mode;
        while (floor > lowest && coarse[floor - 1] >= collapse) floor--;

        return Math.Max(landMin, floor * MapDataWriter.Step255);
    }
}