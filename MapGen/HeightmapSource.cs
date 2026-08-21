using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;
using NoiseTool.Core;
using SixLabors.ImageSharp.PixelFormats;

// WinForms drags in System.Drawing, which has its own Image. Alias rather than rely on using
// order, so the reference cannot silently rebind to the wrong one later.
using SharpImage = SixLabors.ImageSharp.Image;

namespace Ck3MapGen.MapGen;

public sealed record HeightmapWarning(string Title, string Detail);

/// <summary>
/// The heightmap sizes <see cref="HeightmapPacker"/> can tile, and the nearest one to a size it
/// cannot.
///
/// The packer walks the map in <see cref="HeightmapPacker.TileStep"/> px tiles, so it covers
/// <c>width / TileStep</c> whole tiles and nothing after them — while <c>heightmap.heightmap</c>
/// declares the map's full size beside the tile size. The two only describe the same grid when
/// both axes are a whole number of tiles. At 3000x1500 the packer covers 2944x1472 and writes a
/// 46x23 indirection texture, and the descriptor still says 3000x1500: the strip past the last
/// whole tile has no tile to render it, and the tile grid the engine derives from the declared
/// size is wider than the indirection texture it is handed. In game that is a clipped map edge
/// plus terrain that drifts further off the province borders the further east and south you look
/// — up to 28 province pixels on that example.
///
/// None of it is logged, because every shipped file is individually well-formed and they only
/// disagree with each other. Hence a hard check rather than a warning.
/// </summary>
public static class TileFit
{
    /// <summary>
    /// The heightmap sizes confirmed to render correctly, and the only ones a mod is built at.
    ///
    /// A list rather than a rule, and that is the point. Successive divisibility rules were fitted
    /// to the sizes known to work at the time, and each was falsified by the next map rendered:
    ///
    ///     rule                        killed by
    ///     multiple of 64              11520x5696
    ///     exactly 2:1                 11520x5760
    ///     multiple of 128             11520x5760
    ///     multiple of 512 (8 tiles)   10240x5120
    ///
    /// 10240x5120 is the one that should end any temptation to try again: a multiple of 512 on
    /// both axes, an exact 160x80 tile grid, 2:1 — and it clips. Whatever the engine keys on is
    /// not a property of the numbers that six data points can see.
    ///
    /// It is not the packed heightmap either. 11520x5760 packs into an exact 180x90 grid with
    /// every tile addressed and every sample round-tripping, and it still clips once CK3's own
    /// map editor has repacked it with its own packer — inside the editor's viewport, not only
    /// in game.
    ///
    /// So: a whitelist of sizes someone has rendered and looked at, and anything else is resampled
    /// to the nearest entry. Add to it only after rendering that size and checking the map's north
    /// and east edges. Do not add one because it looks like it ought to work.
    ///
    /// 4096x2048 is the one entry not verified that way. It is here because a run of maps was
    /// built and played at it before any of this was understood, which is evidence but not the
    /// same evidence: nobody went looking at the north and east edges for missing terrain, and
    /// this whole investigation started because that is exactly the kind of damage that goes
    /// unnoticed. Everything at that size is fast to regenerate, so if it turns out to clip,
    /// deleting the line costs nothing.
    /// </summary>
    public static readonly (int Width, int Height)[] Known =
    [
        (18432, 9216), // vanilla
        (9216, 4608),  // half vanilla
        (4096, 2048),  // added on the strength of maps already built at it; see the note below
    ];

    /// <summary>The sizes, for a message: "9216x4608 or 18432x9216".</summary>
    public static string KnownList
        => string.Join(" or ", Known.Select(k => $"{k.Width}x{k.Height}").Reverse());

    public static bool Fits(int width, int height)
        => Known.Any(k => k.Width == width && k.Height == height);

    /// <summary>
    /// The entry of <see cref="Known"/> closest to a size, compared as a scale factor rather than
    /// as a pixel count: the two candidates are a factor of two apart, so a linear midpoint would
    /// send everything under 13824 down to half vanilla, including sizes a few percent short of
    /// full. The geometric midpoint, 13036, is where "closer to full" stops being true.
    ///
    /// The source's aspect does not enter into it. Every confirmed size is 2:1, so a source that
    /// is not gets stretched to it, per axis, by <see cref="Resample"/>.
    /// </summary>
    public static (int Width, int Height) Nearest(int width, int height)
    {
        var best = Known[0];
        double bestDistance = double.MaxValue;

        foreach (var candidate in Known)
        {
            // Log distance, so being a third too big costs what being a third too small does.
            double distance = Math.Abs(Math.Log((double)width / candidate.Width))
                            + Math.Abs(Math.Log((double)height / candidate.Height));

            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = candidate;
        }

        return best;
    }

    /// <summary>
    /// What the packer would actually cover at this size. Keyed on the packer's own tile rather
    /// than on <see cref="Step"/>: the two rules are separate, and a size can clear this one and
    /// still be refused.
    /// </summary>
    public static (int Width, int Height) Covered(int width, int height)
        => (width / HeightmapPacker.TileStep * HeightmapPacker.TileStep,
            height / HeightmapPacker.TileStep * HeightmapPacker.TileStep);

    /// <summary>The size of a heightmap on disk, read from the PNG header alone.</summary>
    public static (int Width, int Height) Measure(string path)
    {
        var info = SharpImage.Identify(path);
        return (info.Width, info.Height);
    }

    /// <summary>Why a size cannot ship, and what the nearest one that can is.</summary>
    public static string Explain(string label, int width, int height)
    {
        var (nw, nh) = Nearest(width, height);

        return $"{label} is {width}x{height}, which is not one of the sizes CK3 is known to render "
             + $"correctly ({KnownList}). At other sizes the engine leaves terrain undrawn along "
             + "the north and east edges, in the map editor as much as in game, and it does so "
             + "whatever the heightmap is packed with, CK3's own repack included. Nothing is "
             + $"logged when it happens. The nearest size that works is {nw}x{nh}.";
    }

    /// <summary>
    /// Resamples 16-bit samples onto a new grid, through the same Catmull-Rom filter the Forge
    /// pipeline's Upscale stage uses. <see cref="HeightField.ResampleCubic"/> clamps every tap to
    /// the two source samples it sits between, which is the property that matters here: a filter
    /// free to overshoot could lift a sample across the water plane and mint a pixel of land in
    /// open ocean, or punch a hole through a coastline.
    ///
    /// Costs two float copies of the map — 680 MB a side at vanilla's 18432x9216 — so it runs
    /// only when a fit was actually asked for.
    /// </summary>
    public static ushort[] Resample(ushort[] raw, int width, int height, int newWidth, int newHeight)
    {
        var field = new HeightField(width, height);
        for (int i = 0; i < raw.Length; i++) field.Data[i] = raw[i] / 65535f;

        return field.ResampleCubic(newWidth, newHeight).ToUInt16();
    }
}

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

    /// <summary>
    /// True when the samples are already on CK3's own scale — water plane at
    /// <see cref="MapDataWriter.WaterLevel16"/>, land one step above it and up — so the
    /// normaliser must leave them alone. A Forge pipeline produces exactly that; a PNG drawn
    /// elsewhere almost never does, which is what <see cref="MapConfig.Normalization"/> is for.
    /// <see cref="HeightmapNormalizer"/>'s Shift mode would take a Forge continent's flat plateau
    /// for the land floor and drag the whole landmass down onto the waterline.
    /// </summary>
    public bool Ck3Scale { get; init; }

    /// <summary>Samples as the game will be handed them: normalised, unless already on its scale.</summary>
    public ushort[] Levels(MapConfig cfg) => Ck3Scale ? Raw : HeightmapNormalizer.Normalize(Raw, cfg);

    public bool StillStandsFor(string path)
    {
        if (!string.Equals(Path, path, StringComparison.OrdinalIgnoreCase)) return false;

        var info = new FileInfo(path);
        return info.Exists && info.LastWriteTimeUtc == Written && info.Length == Length;
    }

    public float[] ToElevation(MapConfig cfg)
    {
        var normalized = Levels(cfg);
        var elevation = HeightmapSource.ToSimulationScale(normalized, cfg);

        HeightmapSource.ReportElevation(elevation, cfg);
        return elevation;
    }
}

public static class HeightmapSource
{
    private const int CoarseGridDivisor = 16;

    /// <summary>
    /// Wraps samples produced in memory — by the Forge pipeline — as if they had been decoded
    /// from a file, so everything downstream (diagnostics, elevation conversion, the 3D view,
    /// the generator) takes them by the same route a PNG takes. Width and height become the
    /// map size, so they are checked here the way a decoded image's are — including
    /// <see cref="TileFit"/>, which a file gets offered a fix for and a pipeline does not,
    /// because a pipeline's output size is a setting the user can simply change.
    /// </summary>
    /// <param name="label">What to call it in the log and the toolbar; it is not a path.</param>
    public static HeightmapImage FromRaw(ushort[] raw, int width, int height, string label, MapConfig cfg)
    {
        if (width <= 0 || height <= 0 || (long)width * height != raw.LongLength)
            throw new ArgumentException($"{label}: {width}x{height} does not match {raw.LongLength:N0} samples.");

        if (width % 2 != 0 || height % 2 != 0)
            throw new InvalidOperationException(
                $"{label} is {width}x{height}; both dimensions must be even because " +
                "provinces.png and rivers.png are exactly half the heightmap's resolution.");

        if (!TileFit.Fits(width, height))
            throw new InvalidOperationException(
                TileFit.Explain(label, width, height)
                + "\n\nPick a base resolution that works, or add an Upscale stage that lands on one.");

        var loaded = new HeightmapImage
        {
            Path = label,
            Written = DateTime.UtcNow,
            Length = raw.LongLength * 2,
            Width = width,
            Height = height,
            Raw = raw,
            Histogram = Histogram(raw),
            Ck3Scale = true,
        };

        Apply(loaded, cfg);
        return loaded;
    }

    /// <param name="fitTo">
    /// Resample the file to this size on the way in, or null to take it as it is. Only ever a size
    /// <see cref="TileFit"/> accepts — it is how the offer to fix a file the packer cannot tile is
    /// carried from the point it was accepted (MainForm's offer, or <c>--fit-heightmap</c>) to the
    /// decode. Without it a file that does not fit is refused here rather than shipped broken.
    /// </param>
    public static HeightmapImage Read(string path, MapConfig cfg, (int Width, int Height)? fitTo = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var image = SharpImage.Load<L16>(path);

        int width = image.Width, height = image.Height;
        var raw = ReadRaw(image);
        var histogram = Histogram(raw);

        int distinct = 0;
        foreach (int count in histogram) if (count != 0) distinct++;

        // Auto-interpolate 8-bit heightmaps to true 16-bit using the configured SourceSeaLevel threshold
        int sourceSea = (int)Math.Round(Math.Clamp(cfg.SourceSeaLevel, 0, 254) * MapDataWriter.Step255);
        if (distinct < 1000)
        {
            raw = Upscale8To16Bit(raw, width, height, distinct, sourceSea);
            histogram = Histogram(raw); // Update histogram with the new smooth values
        }

        // Before the size checks, so a file that fails them can be fixed rather than refused, and
        // after the 8-bit interpolation, so the resample runs on the smooth field rather than
        // spreading a stair-stepped one.
        if (fitTo is { } target && (target.Width != width || target.Height != height))
        {
            if (!TileFit.Fits(target.Width, target.Height))
                throw new ArgumentException(
                    TileFit.Explain($"The requested fit for {System.IO.Path.GetFileName(path)}",
                                    target.Width, target.Height), nameof(fitTo));

            double before = (double)width / height, after = (double)target.Width / target.Height;
            raw = TileFit.Resample(raw, width, height, target.Width, target.Height);
            (width, height) = target;
            histogram = Histogram(raw);

            Console.WriteLine($"  fitted {image.Width}x{image.Height} -> {width}x{height} " +
                              "onto a size CK3 renders correctly " +
                              $"(aspect {before:F3}:1 -> {after:F3}:1)");
        }

        if (width % 2 != 0 || height % 2 != 0)
            throw new InvalidOperationException(
                $"Heightmap is {width}x{height}; both dimensions must be even because " +
                "provinces.png and rivers.png are exactly half the heightmap's resolution.");

        if (!TileFit.Fits(width, height))
            throw new InvalidOperationException(
                TileFit.Explain("Heightmap", width, height)
                + "\n\nResize it and reload, or let the tool do it: the file dialog offers "
                + "the fit when it loads a size like this, and the command line takes "
                + "--fit-heightmap.");

        var info = new FileInfo(path);
        var loaded = new HeightmapImage
        {
            Path = path,
            Written = info.LastWriteTimeUtc,
            Length = info.Length,
            Width = width,
            Height = height,
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