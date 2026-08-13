using Ck3MapGen.Config;
using Ck3MapGen.Core;
using SixLabors.ImageSharp.PixelFormats;

// WinForms drags in System.Drawing, which has its own Image. Alias rather than rely on using
// order, so the reference cannot silently rebind to the wrong one later.
using SharpImage = SixLabors.ImageSharp.Image;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Builds <see cref="TerrainData"/> from a heightmap on disk, so the whole mod can be emitted
/// around a map somebody drew rather than one this program generated.
///
/// Everything downstream — the province partition, rivers.png, the terrain textures, the title
/// hierarchy — reads <see cref="TerrainData"/> and nothing else, so it cannot tell the difference.
///
/// Reading is done with ImageSharp rather than by hand. The project writes its own PNGs because
/// CK3 needs an exact pixel format per file and a general imaging library will not guarantee one;
/// reading has no such constraint, and hand-rolling an inflate to avoid a dependency already in
/// the project would be its own bug surface.
/// </summary>
/// <summary>
/// A decoded heightmap, and nothing derived from one.
///
/// This is deliberately the *only* thing worth caching between runs, because it is the only thing
/// that is a pure function of the file. Everything else the image leads to depends on settings the
/// user is in the middle of tuning, so caching any of it means a setting that silently does
/// nothing. That was learned from the drainage network in particular, back when it was cached along
/// with the image and every river setting therefore appeared to do nothing at all.
///
/// What is held is the raw 16-bit samples, which is as far as "pure function of the file" reaches.
/// It used to be the simulation-scale elevation field, and that was already a step too far:
/// <see cref="ToElevation"/> reads <see cref="MapConfig.PeakElevation"/> and
/// <see cref="MapConfig.SeaFloorElevation"/>, so both of those were settings the GUI let you change
/// and then ignored for as long as the same heightmap stayed loaded. Normalisation would have been
/// a third and much more visible one.
///
/// The file's timestamp and length are kept so a heightmap re-exported over the same path is seen
/// as a different image. Keying on the path alone is what makes "I regenerated my heightmap and the
/// preview did not change" happen, and it is the sort of bug that reads as the whole tool being
/// broken.
/// </summary>
/// <summary>
/// One thing wrong with an imported heightmap, in a form that can be shown as well as logged.
///
/// <paramref name="Title"/> is the finding in a sentence; <paramref name="Detail"/> explains what it
/// will look like in game and names the setting that fixes it, in paragraphs split on a blank line.
/// </summary>
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

    /// <summary>
    /// One bucket per 16-bit value. Cached with the decode because it is, like
    /// <see cref="Raw"/> itself, a pure function of the file — no setting is consulted to build it.
    ///
    /// It is what lets <see cref="HeightmapSource.Diagnose"/> run on every build rather than only
    /// on a fresh decode. Every distribution question the diagnostics ask is answerable from this
    /// in 65,536 steps instead of a pass over thirty million pixels, so re-checking a cached image
    /// costs nothing and a setting that changes the diagnosis actually changes it.
    /// </summary>
    public required int[] Histogram { get; init; }

    /// <summary>Whether this decode still stands for what is on disk at <paramref name="path"/>.</summary>
    public bool StillStandsFor(string path)
    {
        if (!string.Equals(Path, path, StringComparison.OrdinalIgnoreCase)) return false;

        var info = new FileInfo(path);
        return info.Exists && info.LastWriteTimeUtc == Written && info.Length == Length;
    }

    /// <summary>
    /// The samples on the simulation's elevation scale, by way of normalisation.
    ///
    /// Every step here reads settings, which is why it is a method run per-run rather than a
    /// property computed once at decode.
    /// </summary>
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
    /// <summary>
    /// How much coarser than the heightmap the climate grid is. 16 reproduces what the old size
    /// presets used: an 8192-wide map got a 512-wide grid, a vanilla-sized one 1024.
    /// </summary>
    private const int CoarseGridDivisor = 16;

    /// <summary>
    /// Loads a heightmap and derives everything from it. The one-shot path, for the CLI.
    /// </summary>
    public static TerrainData Load(string path, MapConfig cfg)
        => TerrainData.FromElevation(Read(path, cfg).ToElevation(cfg), cfg);

    /// <summary>
    /// Decodes the image and puts its dimensions on the config. No setting is consulted, so the
    /// result can be held across runs.
    ///
    /// The image is authoritative about map size: <paramref name="cfg"/>'s Width and Height are set
    /// from it, because provinces.png, rivers.png and every terrain texture are sized off those and
    /// a mismatch is a silent CK3 failure. Dimensions must be even, since the province map is
    /// exactly half the heightmap.
    /// </summary>
    public static HeightmapImage Read(string path, MapConfig cfg)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var image = SharpImage.Load<L16>(path);

        if (image.Width % 2 != 0 || image.Height % 2 != 0)
            throw new InvalidOperationException(
                $"Heightmap is {image.Width}x{image.Height}; both dimensions must be even because " +
                "provinces.png and rivers.png are exactly half the heightmap's resolution.");

        var raw = ReadRaw(image);
        var info = new FileInfo(path);
        var loaded = new HeightmapImage
        {
            Path = path,
            Written = info.LastWriteTimeUtc,
            Length = info.Length,
            Width = image.Width,
            Height = image.Height,
            Raw = raw,
            Histogram = Histogram(raw),
        };

        Apply(loaded, cfg);

        Console.WriteLine($"  decoded in {sw.ElapsedMilliseconds} ms");
        return loaded;
    }

    /// <summary>One bucket per 16-bit value. A pure function of the file, so it rides the decode.</summary>
    private static int[] Histogram(ushort[] raw)
    {
        var histogram = new int[65536];
        foreach (ushort v in raw) histogram[v]++;
        return histogram;
    }

    /// <summary>
    /// Everything wrong with an imported heightmap that can be seen before generating anything,
    /// returned rather than only printed.
    ///
    /// Returned, because printing was measurably not enough. Every fault below was already visible
    /// in the "as decoded" line — a playtester read that line, shipped the map, and reported the
    /// result as a bug in the tool. A number printed beside a reference number is not a warning: it
    /// asks the reader to already know which way is bad and by how much. These say what is wrong,
    /// what it will look like in game, and which setting fixes it, and the GUI puts them in front of
    /// the user instead of in a log they have no reason to read.
    ///
    /// Settings-dependent, so it runs on every build rather than riding the cached decode — the
    /// whole point is that changing Normalization and rebuilding must change what this says. It is
    /// cheap enough to do that with because it reads <see cref="HeightmapImage.Histogram"/>, which
    /// is a pure function of the file and therefore *is* cached.
    /// </summary>
    public static IReadOnlyList<HeightmapWarning> Diagnose(HeightmapImage image, MapConfig cfg)
    {
        const int water16 = Emit.MapDataWriter.WaterLevel16;
        const int water255 = Emit.MapDataWriter.WaterLevel255;
        const int step = Emit.MapDataWriter.Step255;

        var histogram = image.Histogram;
        long total = image.Raw.LongLength;
        var found = new List<HeightmapWarning>();

        var hypsometry = Hypsometry.FromHistogram(histogram, total);
        Console.WriteLine($"  as decoded: {hypsometry.Describe()}");

        // --- The land sits far above the water plane -------------------------------------------
        //
        // Vanilla's land sits at a median of 36/255. Triple it before saying anything: a genuinely
        // mountainous map drawn on CK3's own scale can run high, and this must not cry wolf on one.
        const int Plateau = 108;
        int median = hypsometry.Percentile(50);

        if (median >= Plateau && cfg.Normalization != Config.HeightmapNormalization.Stretch)
            found.Add(new HeightmapWarning(
                $"The land is not on CK3's height scale (median {median}/255, vanilla's is 36).",
                (cfg.Normalization == Config.HeightmapNormalization.Off
                    ? "Normalisation is off, so the whole landmass will ship as a plateau with a "
                      + "vertical cliff at every shoreline, and the climate model will read the "
                      + "continental interior as kilometres up and chill every biome on the map."
                    : "Shift will bring it down onto the water plane, which fixes the shoreline, "
                      + "but relief stays exactly as compressed as the source drew it — measured "
                      + "on such a map, a highest pixel of 147/255 against vanilla's 191.")
                + "\n\nFix: set Normalization to Stretch, under 11 Height scale."));

        // --- The ocean has no depth ---------------------------------------------------------------
        //
        // A different fault from the one above and it reads completely differently in game.
        // Normalisation cannot fix it in any mode: the pixels are already on the correct side of
        // the plane and simply have no relief below it.
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

        // --- Part of that ocean is on the land side of the plane ---------------------------------
        //
        // The fault the 0-255 scale everything else is quoted on cannot show, which is why it needs
        // its own check. The heightmap is 16-bit and the plane is at 4883, so every raw value from
        // 4884 to 5139 is land that still rounds to 19/255: it prints as sitting exactly on the
        // water plane while CK3 makes provinces, counties and terrain out of it. Measured on the map
        // that prompted this, 2.12% of the raster — some 900,000 px — at raw 4884, forming a shelf
        // fifteen to twenty pixels wide around every coast.
        //
        // **Gated on the ocean being depthless, and that gate is the whole check.** On its own the
        // histogram signature is worthless, because "the ocean drawn a unit too high" and "flat
        // lowland at the lowest value that is still land" are the same distribution. A map came in
        // with 10.40% of its raster at exactly 4884 — twenty times the threshold here — and it was
        // correct: a generator that floors its lowland at sea level plus one, rendering perfectly
        // in game. What separated it was the sea itself. Flood-filled from the border, its ocean had
        // a median depth of 0.00/255 and 60.68% of it at exactly 0, against the bad map's ocean
        // sitting entirely *on* the plane at 19.00. Only 1.51% of the good map's flat band touched
        // open ocean, against 8.39% of the bad map's shelf.
        //
        // So the claim "this is the ocean, drawn on the wrong side of the line" is only coherent
        // when the ocean is not already drawn correctly on the right side of it. If the sea is
        // properly deep, the coastline is where its author put it and a flat band above it is land.
        int modal = 0;
        long modalCount = 0;

        for (int v = water16 + 1; v < histogram.Length; v++)
            if (histogram[v] > modalCount) { modalCount = histogram[v]; modal = v; }

        // Big enough to be a drawn surface rather than a few stray samples.
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

        // 8-bit data in a 16-bit pipeline. Not an error — it round-trips correctly — but the stretch
        // cannot create levels it was not given, and MapDataWriter's terracing note is measured at
        // twice this many. A note rather than a warning, so it stays out of the dialog.
        int distinct = 0;
        foreach (int count in histogram) if (count != 0) distinct++;

        if (distinct < 1000)
            Console.WriteLine($"  NOTE: only {distinct:N0} distinct values in the decoded source " +
                              "(vanilla's heightmap has 31,516). This is 8-bit data in a 16-bit " +
                              "file; normalising cannot add levels back and will spend some of " +
                              "these, which reads in game as terracing on gentle slopes.");

        return found;
    }

    /// <summary>The image's 16-bit samples, row-major, without interpreting any of them.</summary>
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

    /// <summary>
    /// Puts a decoded image's dimensions back on the config. Idempotent, and cheap enough to call on
    /// every run — which is the point: a cached image must still be able to size the config, because
    /// nothing downstream works if Width and Height disagree with the raster.
    /// </summary>
    public static void Apply(HeightmapImage image, MapConfig cfg)
    {
        cfg.Width = image.Width;
        cfg.Height = image.Height;

        // The coarse climate grid follows the image too. It used to come from a size preset, which
        // no longer exists now that the image is the only source of truth about size — and a fixed
        // coarse grid against a variable heightmap would mean the landmass summary sampled at a
        // different resolution on every map. Clamped at the top because the grid is a summary: past
        // about a thousand cells across it stops being cheaper than the field it summarises.
        cfg.WorldWidth = Math.Clamp(cfg.Width / CoarseGridDivisor, 128, 1024);
        cfg.WorldHeight = Math.Max(64, cfg.WorldWidth / 2);

        Console.WriteLine($"Heightmap {Path.GetFileName(image.Path)}: {cfg.Width}x{cfg.Height}, " +
                          $"provinces {cfg.ProvinceWidth}x{cfg.ProvinceHeight}, " +
                          $"climate grid {cfg.WorldWidth}x{cfg.WorldHeight}");
    }

    /// <summary>
    /// CK3's 16-bit height scale back onto the simulation's elevation units.
    ///
    /// The inverse of what <c>MapDataWriter.ElevationTo16</c> does on the way out, piecewise about
    /// the water plane so that a pixel at exactly <c>WaterLevel16</c> comes back at exactly sea
    /// level and the coastline survives the round trip.
    ///
    /// With normalisation off the round trip is now the identity: nothing on either side reshapes
    /// what it converts. It used to remap land onto vanilla's measured hypsometric curve on the way
    /// out, and the note here still said so long after that was removed.
    /// </summary>
    internal static float[] ToSimulationScale(ushort[] raw, MapConfig cfg)
    {
        var elevation = new float[raw.Length];

        float sea = cfg.Limits.SeaLevelUpper;
        float floor = cfg.SeaFloorElevation;
        float top = cfg.PeakElevation;
        const float water = Emit.MapDataWriter.WaterLevel16;

        Parallel.For(0, raw.Length, i =>
        {
            float v = raw[i];
            elevation[i] = v <= water
                ? floor + v / water * (sea - floor)
                : sea + 1f + (v - water) / (65535f - water) * (top - sea - 1f);
        });

        return elevation;
    }

    /// <summary>
    /// What the field looks like once it is on the simulation's scale — the last chance to notice a
    /// heightmap that will produce an empty world before anything spends time on one.
    /// </summary>
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
                          $"{Emit.MapDataWriter.WaterLevel16}.");

        if (cfg.Normalization == Config.HeightmapNormalization.Off)
            Console.WriteLine("  A heightmap drawn outside this program almost certainly is not. " +
                              "Set Normalization to Stretch and give SourceSeaLevel the 0-255 " +
                              "value its own coastline sits at — 51 for an Azgaar export.");
    }
}
