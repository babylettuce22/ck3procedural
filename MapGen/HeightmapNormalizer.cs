using Ck3MapGen.Config;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

public static class HeightmapNormalizer
{
    public static ushort[] Normalize(ushort[] raw, MapConfig cfg)
    {
        if (cfg.Normalization == HeightmapNormalization.Off) return raw;

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
                              $"sea level of {cfg.SourceSeaLevel:F0}/255. Either the heightmap is " +
                              $"entirely ocean or SourceSeaLevel is set too high for it.");
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
                scaled = stretch
                    ? lowestLand + Math.Min(1.0, (v - landFloor) / landSpan) * landRange
                    : v - drop;

                if (scaled < lowestLand) scaled = lowestLand;
            }
            else
            {
                // Compress source water band onto CK3's water scale (0..water16)
                scaled = v / seaSpan * water16;

                // --- SHORELINE HARDENING (PULL OUT OF MUD ZONE) ---
                // If water is within the shallow 0..3 unit band of the water plane,
                // accelerate the drop so it gets deep enough to render as clean blue water
                // rather than shallow mud/sand flats.
                const double mudBand = MapDataWriter.Step255 * 3.0;
                if (scaled > 0 && scaled > water16 - mudBand)
                {
                    double t = (water16 - scaled) / mudBand; // 0.0 at shoreline, 1.0 at edge of band
                    scaled = water16 - MapDataWriter.Step255 * 1.5 - (mudBand - MapDataWriter.Step255 * 1.5) * Math.Sqrt(t);
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
                          $"(lowest land pixel {(double)landMin / MapDataWriter.Step255:F2}, " +
                          $"{clippedBelow:N0} px below the floor flattened onto " +
                          $"{lowestLand / MapDataWriter.Step255})");

        Console.WriteLine(stretch
            ? $"  land {floor255:F0}..{top255:F0} → {lowestLand / MapDataWriter.Step255}.." +
              $"{topLand / MapDataWriter.Step255} (p{cfg.LandTopPercentile:0.####} anchor, " +
              $"{landRange / landSpan:F2}x amplification, {clippedAbove:N0} px clipped above it)"
            : $"  land shifted down {drop / MapDataWriter.Step255:F2}/255, relief 1:1, nothing " +
              $"clipped above");

        if (stretch && landRange / landSpan > 2.0)
            Console.WriteLine("  WARNING: land is being amplified more than twofold. The source's " +
                              "land occupies a narrow band, and every slope on the map is being " +
                              "exaggerated by that factor. Consider Shift, or a lower LandTop.");

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