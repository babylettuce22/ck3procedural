using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// A hand-painted impassable mask — the Masks tab's <see cref="ImpassablePaint"/>, or
/// <see cref="MapConfig.ImpassableMaskPath"/> — read onto the province raster as one of three
/// states per pixel.
///
/// The contract is deliberately the simplest thing a paint program can produce: any image, white
/// for impassable and black for passable, and transparent (alpha under half) for "no opinion",
/// which <see cref="MapConfig.ImpassablePaintMode"/> resolves. Opaque pixels are thresholded on
/// brightness at mid grey, so an anti-aliased brush edge, a grey stroke or a colour accidentally
/// left on the layer all still read as one of the two. An image without alpha is all opaque, so an
/// old black-and-white mask means exactly what it did; and in Manual mode transparent is passable,
/// as black is.
///
/// Size is forgiven as well. The mask is meant to be painted over provinces.png, but a user who
/// opened heightmap.png instead (twice the size) or a resized copy gets it nearest-sampled onto the
/// province raster rather than rejected, with a console line saying so — a wall drawn a few pixels
/// thick survives the resample because it only has to touch a province, not cover it.
/// </summary>
public static class ImpassableMask
{
    /// <summary>Unpainted: the mode decides.</summary>
    public const byte Auto = 0;

    /// <summary>Painted black: never impassable.</summary>
    public const byte Passable = 1;

    /// <summary>Painted white: always impassable.</summary>
    public const byte Wall = 2;

    /// <summary>One pixel's state from its colour.</summary>
    public static byte Classify(byte r, byte g, byte b, byte a)
    {
        if (a < 128) return Auto;
        // Rec. 601 luma, integer, ≥128 is white.
        int luma = (r * 299 + g * 587 + b * 114) / 1000;
        return luma >= 128 ? Wall : Passable;
    }

    /// <summary>
    /// One state per province-raster pixel; null when no mask is set. An in-memory
    /// <paramref name="paint"/> (the GUI's) wins over the path. A path that is set but unreadable
    /// throws, as the Azgaar loader does — a user who has pointed at a mask wants the run stopped
    /// on a typo, not quietly given the relief scoring.
    /// </summary>
    public static byte[]? Load(MapConfig cfg, int width, int height, ImpassablePaint? paint = null)
    {
        if (paint is not null && !paint.IsEmpty)
        {
            var sampled = Sample(paint.Width, paint.Height, width, height, (sx, sy) => paint.Cells[sy * paint.Width + sx]);
            Report("painted mask", $"{paint.Width}x{paint.Height}, resampled onto {width}x{height}", sampled, cfg);
            return sampled;
        }

        string path = cfg.ImpassableMaskPath;
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Impassable mask not found: {path}. Clear ImpassableMaskPath to use the built-in relief scoring.", path);

        var image = Io.DdsReader.Load(path)
            ?? throw new InvalidDataException($"Impassable mask could not be decoded as an image: {path}");

        // BGRA.
        var cells = Sample(image.Width, image.Height, width, height, (sx, sy) =>
        {
            int p = (sy * image.Width + sx) * 4;
            return Classify(image.Bgra[p + 2], image.Bgra[p + 1], image.Bgra[p], image.Bgra[p + 3]);
        });

        string size = image.Width == width && image.Height == height
            ? $"{image.Width}x{image.Height}"
            : $"{image.Width}x{image.Height}, resampled onto {width}x{height}";
        Report(Path.GetFileName(path), size, cells, cfg);
        return cells;
    }

    /// <summary>Nearest-neighbour: each pixel takes the source cell its centre falls on.</summary>
    private static byte[] Sample(int sourceWidth, int sourceHeight, int width, int height, Func<int, int, byte> at)
    {
        var cells = new byte[width * height];
        bool sameSize = sourceWidth == width && sourceHeight == height;
        Parallel.For(0, height, y =>
        {
            int sy = sameSize ? y : Math.Min(sourceHeight - 1, (int)((y + 0.5) * sourceHeight / height));
            for (int x = 0; x < width; x++)
            {
                int sx = sameSize ? x : Math.Min(sourceWidth - 1, (int)((x + 0.5) * sourceWidth / width));
                cells[y * width + x] = at(sx, sy);
            }
        });
        return cells;
    }

    private static void Report(string name, string size, byte[] cells, MapConfig cfg)
    {
        long walls = 0, passable = 0;
        foreach (byte c in cells)
        {
            if (c == Wall) walls++;
            else if (c == Passable) passable++;
        }

        bool combine = cfg.ImpassablePaintMode == ImpassablePaintMode.ManualPlusAuto;
        Console.WriteLine($"  impassable mask: {name} ({size}), {walls} white / {passable} black / " +
                          $"{cells.Length - walls - passable} transparent px, " +
                          (combine ? "automatic walls fill the transparent ground" : "manual only"));
        if (walls == 0 && !combine)
            Console.WriteLine("  impassable mask: WARNING — no white pixels; nothing will be impassable");
        if (combine && walls + passable == cells.Length)
            Console.WriteLine("  impassable mask: WARNING — no transparent pixels, so the automatic walls have nowhere to go " +
                              "(a mask without alpha is all black and white)");
    }

    /// <summary>The walls alone: white is impassable, everything else passable. Manual mode.</summary>
    public static bool[] Walls(byte[] cells)
    {
        var walls = new bool[cells.Length];
        for (int i = 0; i < cells.Length; i++) walls[i] = cells[i] == Wall;
        return walls;
    }

    /// <summary>
    /// ManualPlusAuto: the auto-cut where unpainted, white added, black removed. A pass whose
    /// corridor was painted white is dropped — the user walled it — so no pinned seed lands in a wall.
    /// </summary>
    public static (bool[] Mask, AutoCutDiagnostics Diagnostics) Combine(bool[] auto, AutoCutDiagnostics diagnostics, byte[] cells)
    {
        var mask = new bool[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            mask[i] = cells[i] == Wall || (cells[i] == Auto && auto[i]);

        if (diagnostics.Passes is not { Count: > 0 } passes) return (mask, diagnostics);

        var kept = passes.Where(p => !p.Corridor.Any(i => cells[i] == Wall)).ToList();
        if (kept.Count < passes.Count)
            Console.WriteLine($"  impassable mask: {passes.Count - kept.Count} mountain pass(es) painted over and dropped");
        return (mask, diagnostics with { Passes = kept });
    }
}
