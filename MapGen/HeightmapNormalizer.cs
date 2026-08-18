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

        const int water16 = MapDataWriter.WaterLevel16;
        const int lowestLand = water16 + MapDataWriter.Step255;

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
                    // Scale water smoothly down from WaterLevel16 (19/255) to 0 (deep sea)
                    scaled = (double)v / seaSpan * (water16 - 1);
                }
            }

            result[i] = (ushort)Math.Clamp(Math.Round(scaled), 0, 65535);
        });

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