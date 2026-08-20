using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;
using SixLabors.ImageSharp.PixelFormats;

// WinForms drags in System.Drawing, which has its own Image. Alias rather than rely on using
// order, so the reference cannot silently rebind to the wrong one later.
using SharpImage = SixLabors.ImageSharp.Image;

namespace Ck3MapGen.MapGen;

public sealed record HeightmapWarning(string Title, string Detail);

public sealed class HeightmapImage
{
    public required string Path { get; init; }
    public required DateTime Written { get; init; }
    public required long Length { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>The image's own 16-bit samples, exactly as they were decoded.</summary>
    public required ushort[] Raw { get; init; }

    /// <summary>One bucket per 16-bit value.</summary>
    public required int[] Histogram { get; init; }

    public bool StillStandsFor(string path)
    {
        if (!string.Equals(Path, path, StringComparison.OrdinalIgnoreCase)) return false;

        var info = new FileInfo(path);
        return info.Exists && info.LastWriteTimeUtc == Written && info.Length == Length;
    }

    public float[] ToElevation(MapConfig cfg)
    {
        var normalized = HeightmapNormalizer.Normalize(Raw, cfg);
        var elevation = HeightmapSource.ToSimulationScale(normalized, cfg);

        HeightmapSource.ReportElevation(elevation, cfg);
        return elevation;
    }
}

public static class HeightmapSource
{
    private const int CoarseGridDivisor = 16;

    public static TerrainData Load(string path, MapConfig cfg)
        => TerrainData.FromElevation(Read(path, cfg).ToElevation(cfg), cfg);

    public static HeightmapImage Read(string path, MapConfig cfg)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var image = SharpImage.Load<L16>(path);

        if (image.Width % 2 != 0 || image.Height % 2 != 0)
            throw new InvalidOperationException(
                $"Heightmap is {image.Width}x{image.Height}; both dimensions must be even because " +
                "provinces.png and rivers.png are exactly half the heightmap's resolution.");

        var raw = ReadRaw(image);
        var histogram = Histogram(raw);

        int distinct = 0;
        foreach (int count in histogram) if (count != 0) distinct++;

        // Auto-interpolate 8-bit heightmaps to true 16-bit using the configured SourceSeaLevel threshold
        int sourceSea = (int)Math.Round(Math.Clamp(cfg.SourceSeaLevel, 0, 254) * MapDataWriter.Step255);
        if (distinct < 1000)
        {
            raw = Upscale8To16Bit(raw, image.Width, image.Height, distinct, sourceSea);
            histogram = Histogram(raw); // Update histogram with the new smooth values
        }

        var info = new FileInfo(path);
        var loaded = new HeightmapImage
        {
            Path = path,
            Written = info.LastWriteTimeUtc,
            Length = info.Length,
            Width = image.Width,
            Height = image.Height,
            Raw = raw,
            Histogram = histogram,
        };

        Apply(loaded, cfg);

        Console.WriteLine($"  decoded in {sw.ElapsedMilliseconds} ms");
        return loaded;
    }

    private static int[] Histogram(ushort[] raw)
    {
        var histogram = new int[65536];
        foreach (ushort v in raw) histogram[v]++;
        return histogram;
    }

    public static IReadOnlyList<HeightmapWarning> Diagnose(HeightmapImage image, MapConfig cfg)
    {
        const int water16 = MapDataWriter.WaterLevel16;
        const int water255 = MapDataWriter.WaterLevel255;
        const int step = MapDataWriter.Step255;

        var histogram = image.Histogram;
        long total = image.Raw.LongLength;
        var found = new List<HeightmapWarning>();

        var hypsometry = Hypsometry.FromHistogram(histogram, total);
        Console.WriteLine($"  as decoded: {hypsometry.Describe()}");

        // 1. High land check
        const int Plateau = 108;
        int median = hypsometry.Percentile(50);

        if (median >= Plateau && cfg.Normalization != HeightmapNormalization.Stretch)
            found.Add(new HeightmapWarning(
                $"The land is not on CK3's height scale (median {median}/255, vanilla's is 36).",
                (cfg.Normalization == HeightmapNormalization.Off
                    ? "Normalisation is off, so the whole landmass will ship as a plateau with a "
                      + "vertical cliff at every shoreline, and the climate model will read the "
                      + "continental interior as kilometres up and chill every biome on the map."
                    : "Shift will bring it down onto the water plane, which fixes the shoreline, "
                      + "but relief stays exactly as compressed as the source drew it — measured "
                      + "on such a map, a highest pixel of 147/255 against vanilla's 191.")
                + "\n\nFix: set Normalization to Stretch, under 11 Height scale."));

        // 2. Depthless ocean check
        int waterMedian = hypsometry.WaterPercentile(50);
        bool depthlessOcean = hypsometry.Water > 0 && waterMedian >= water255 - 1;

        if (depthlessOcean)
            found.Add(new HeightmapWarning(
                $"The ocean has no depth (median water pixel {waterMedian}/255, sitting on the "
                + $"water plane at {water255}; vanilla's is 0).",
                "CK3 draws the sea surface at the water plane, so a seabed resting on that same "
                + "plane is coplanar with it and the ocean renders as open ground rather than "
                + "water.\n\nCoastline shaping grades the near-shore seabed automatically, but only "
                + "within a shelf's reach of land — open ocean past that keeps whatever the source "
                + "drew.\n\nFix: give the ocean a floor well below sea level, 0 being the usual "
                + "convention, and redraw."));

        // 3. Modal land-side check (uses water16 and step)
        int modal = 0;
        long modalCount = 0;

        for (int v = water16 + 1; v < histogram.Length; v++)
            if (histogram[v] > modalCount) { modalCount = histogram[v]; modal = v; }

        if (depthlessOcean && modal != 0 && modal / step == water255 && modalCount >= total / 200)
            found.Add(new HeightmapWarning(
                $"Part of that ocean is on the land side of the plane (raw {modal} = "
                + $"{(double)modal / step:F4}/255, {modal - water16} unit(s) above the plane at "
                + $"{water16}, holding {100.0 * modalCount / total:F2}% of the map).",
                "It rounds to 19/255, so it reads as water in every figure the tool prints. CK3 "
                + "does not round: it is land, and the generator will cut provinces, counties and "
                + "terrain out of it.\n\nFix: the same redraw as above. Moving SourceSeaLevel to "
                + $"{(double)modal / step:F4} would reclassify it as water, but that is not worth "
                + "doing — it adds no depth, so the band simply lands flat on the plane and renders "
                + "as ground anyway. Give the ocean a floor instead."));

        foreach (var warning in found)
        {
            Console.WriteLine();
            Console.WriteLine($"  WARNING: {warning.Title}");
            foreach (string line in warning.Detail.Split('\n'))
                Console.WriteLine($"  {line}");
        }

        if (found.Count != 0) Console.WriteLine();

        int distinct = 0;
        foreach (int count in histogram) if (count != 0) distinct++;

        if (distinct < 1000)
            Console.WriteLine($"  NOTE: only {distinct:N0} distinct values in the decoded source " +
                              "(vanilla's heightmap has 31,516). This is 8-bit data in a 16-bit " +
                              "file; normalising cannot add levels back and will spend some of " +
                              "these, which reads in game as terracing on gentle slopes.");

        return found;
    }

    private static ushort[] ReadRaw(SixLabors.ImageSharp.Image<L16> image)
    {
        var raw = new ushort[(long)image.Width * image.Height];

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                long offset = (long)y * accessor.Width;

                for (int x = 0; x < row.Length; x++)
                    raw[offset + x] = row[x].PackedValue;
            }
        });

        return raw;
    }

    public static void Apply(HeightmapImage image, MapConfig cfg)
    {
        cfg.Width = image.Width;
        cfg.Height = image.Height;
        cfg.WorldWidth = Math.Clamp(cfg.Width / CoarseGridDivisor, 128, 1024);
        cfg.WorldHeight = Math.Max(64, cfg.WorldWidth / 2);

        Console.WriteLine($"Heightmap {Path.GetFileName(image.Path)}: {cfg.Width}x{cfg.Height}, " +
                          $"provinces {cfg.ProvinceWidth}x{cfg.ProvinceHeight}, " +
                          $"climate grid {cfg.WorldWidth}x{cfg.WorldHeight}");
    }

    internal static float[] ToSimulationScale(ushort[] raw, MapConfig cfg)
    {
        var elevation = new float[raw.Length];

        float sea = cfg.Limits.SeaLevelUpper;
        float floor = cfg.SeaFloorElevation;
        float top = cfg.PeakElevation;
        const float water = MapDataWriter.WaterLevel16;

        Parallel.For(0, raw.Length, i =>
        {
            float v = raw[i];
            elevation[i] = v <= water
                ? floor + v / water * (sea - floor)
                : sea + 1f + (v - water) / (65535f - water) * (top - sea - 1f);
        });

        return elevation;
    }

    internal static void ReportElevation(float[] elevation, MapConfig cfg)
    {
        float sea = cfg.Limits.SeaLevelUpper;
        long land = 0;
        float min = float.MaxValue, max = float.MinValue;

        foreach (float e in elevation)
        {
            if (e > sea) land++;
            if (e < min) min = e;
            if (e > max) max = e;
        }

        Console.WriteLine($"  elevation {min:F0}..{max:F0} (sea {sea:F0}), " +
                          $"{100.0 * land / elevation.Length:F1}% land");

        if (land != 0) return;

        Console.WriteLine("  WARNING: no pixel is above the water plane. Expected a 16-bit " +
                          "greyscale heightmap on CK3's scale, where water is at or below " +
                          $"{MapDataWriter.WaterLevel16}.");

        if (cfg.Normalization == HeightmapNormalization.Off)
            Console.WriteLine("  A heightmap drawn outside this program almost certainly is not. " +
                              "Set Normalization to Stretch and give SourceSeaLevel the 0-255 " +
                              "value its own coastline sits at — 51 for an Azgaar export.");
    }

    public static ushort[] Upscale8To16Bit(ushort[] raw, int width, int height, int distinct, int sourceSea)
    {
        if (distinct >= 1000) return raw;

        Console.WriteLine($"  -> Auto-interpolating 8-bit heightmap ({distinct} levels) into smooth 16-bit gradients with sea threshold {sourceSea}...");

        var smoothed = new ushort[raw.Length];
        var temp = new float[raw.Length];

        // 1. Horizontal Pass (Land blurs only with land; ocean remains strictly untouched)
        Parallel.For(0, height, y =>
        {
            long row = (long)y * width;
            for (int x = 0; x < width; x++)
            {
                long idx = row + x;
                ushort center = raw[idx];

                if (center <= sourceSea)
                {
                    temp[idx] = center;
                    continue;
                }

                float sum = 0f;
                float weightSum = 0f;

                for (int dx = -3; dx <= 3; dx++)
                {
                    int nx = Math.Clamp(x + dx, 0, width - 1);
                    ushort val = raw[row + nx];

                    if (val > sourceSea)
                    {
                        float w = MathF.Exp(-0.5f * (dx * dx) / (1.8f * 1.8f));
                        sum += val * w;
                        weightSum += w;
                    }
                }

                temp[idx] = weightSum > 0f ? (sum / weightSum) : center;
            }
        });

        // 2. Vertical Pass
        Parallel.For(0, height, y =>
        {
            long row = (long)y * width;
            for (int x = 0; x < width; x++)
            {
                long idx = row + x;
                ushort center = raw[idx];

                if (center <= sourceSea)
                {
                    // Snap any anti-aliased water fringe pixels directly to ocean floor (0)
                    smoothed[idx] = (center == 0 || center > sourceSea - MapDataWriter.Step255 * 2)
                        ? (ushort)0
                        : center;
                    continue;
                }

                float sum = 0f;
                float weightSum = 0f;

                for (int dy = -3; dy <= 3; dy++)
                {
                    int ny = Math.Clamp(y + dy, 0, height - 1);
                    float val = temp[(long)ny * width + x];

                    if (val > sourceSea)
                    {
                        float w = MathF.Exp(-0.5f * (dy * dy) / (1.8f * 1.8f));
                        sum += val * w;
                        weightSum += w;
                    }
                }

                smoothed[idx] = (ushort)Math.Clamp(MathF.Round(weightSum > 0f ? (sum / weightSum) : center), 0, 65535);
            }
        });

        return smoothed;
    }
}