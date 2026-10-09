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
/// opened heightmap.png instead (twice the size) or a resized copy gets it resampled onto the
/// province raster rather than rejected, with a console line saying so: nearest when shrinking, a
/// smooth vote when enlarging (see <see cref="Sample"/>), so a small paint doesn't come out blocky.
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

    /// <summary>
    /// The mask on the province raster. Shrinking (a heightmap-sized mask) is nearest-neighbour:
    /// each pixel takes the source cell its centre falls on. Enlarging (the Masks tab's paint is at
    /// most 2048 across, so 4x on a vanilla-sized map) is a smooth vote instead: each pixel weighs
    /// the source cells around it by a small Gaussian and takes the state with the most weight.
    /// Nearest turned every paint cell into a 4x4 block, and Snap cuts the partition along those
    /// blocks, so painted walls came out as stairs. The vote rounds the stairs into the curve the
    /// brush meant, and a stroke one cell wide stays about one cell wide.
    /// </summary>
    private static byte[] Sample(int sourceWidth, int sourceHeight, int width, int height, Func<int, int, byte> at)
    {
        bool sameSize = sourceWidth == width && sourceHeight == height;
        if (!sameSize && (sourceWidth < width || sourceHeight < height))
            return SampleSmooth(sourceWidth, sourceHeight, width, height, at);

        var cells = new byte[width * height];
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

    // The vote's kernel: a Gaussian half a source cell wide, integrated over each cell it covers.
    // Wide enough to round a staircase into a curve, narrow enough that a stroke one cell wide
    // still out-weighs its surroundings across its whole width. Two cells either side is past 3σ.
    private const float SmoothSigma = 0.5f;
    private const int SmoothReach = 2;
    private const int SmoothTaps = SmoothReach * 2 + 1;

    private static byte[] SampleSmooth(int sourceWidth, int sourceHeight, int width, int height, Func<int, int, byte> at)
    {
        var source = new byte[sourceWidth * sourceHeight];
        for (int sy = 0; sy < sourceHeight; sy++)
            for (int sx = 0; sx < sourceWidth; sx++)
                source[sy * sourceWidth + sx] = at(sx, sy);

        // A cell with a different state within reach is on an edge; anywhere else the vote is
        // unanimous, so most of the map is a plain lookup.
        var edge = new bool[source.Length];
        Parallel.For(0, sourceHeight, sy =>
        {
            int y0 = Math.Max(0, sy - SmoothReach), y1 = Math.Min(sourceHeight - 1, sy + SmoothReach);
            for (int sx = 0; sx < sourceWidth; sx++)
            {
                byte self = source[sy * sourceWidth + sx];
                int x0 = Math.Max(0, sx - SmoothReach), x1 = Math.Min(sourceWidth - 1, sx + SmoothReach);
                for (int ny = y0; ny <= y1 && !edge[sy * sourceWidth + sx]; ny++)
                    for (int nx = x0; nx <= x1; nx++)
                        if (source[ny * sourceWidth + nx] != self) { edge[sy * sourceWidth + sx] = true; break; }
            }
        });

        // A painted cell too thin to win the vote at its own centre (a one-cell diagonal stroke) is
        // kept whole, as nearest drew it. Smoothing may round a wall off, never open a gap in one.
        var centre = new float[SmoothTaps];
        for (int t = 0; t < SmoothTaps; t++)
            centre[t] = (float)(Phi((t - SmoothReach + 0.5) / SmoothSigma) - Phi((t - SmoothReach - 0.5) / SmoothSigma));
        var keep = new bool[source.Length];
        Parallel.For(0, sourceHeight, sy =>
        {
            for (int sx = 0; sx < sourceWidth; sx++)
            {
                int c = sy * sourceWidth + sx;
                byte self = source[c];
                if (!edge[c] || self == Auto) continue;
                float own = 0;
                for (int j = 0; j < SmoothTaps; j++)
                {
                    int ny = Math.Clamp(sy - SmoothReach + j, 0, sourceHeight - 1);
                    for (int i = 0; i < SmoothTaps; i++)
                    {
                        int nx = Math.Clamp(sx - SmoothReach + i, 0, sourceWidth - 1);
                        if (source[ny * sourceWidth + nx] == self) own += centre[j] * centre[i];
                    }
                }
                keep[c] = own < 0.7f;
            }
        });

        var (colCell, colFirst, colWeight) = Taps(width, sourceWidth);
        var (rowCell, rowFirst, rowWeight) = Taps(height, sourceHeight);
        var cells = new byte[width * height];

        Parallel.For(0, height, y =>
        {
            Span<float> weight = stackalloc float[3];
            int rowBase = rowCell[y] * sourceWidth;
            for (int x = 0; x < width; x++)
            {
                if (!edge[rowBase + colCell[x]] || keep[rowBase + colCell[x]])
                {
                    cells[y * width + x] = source[rowBase + colCell[x]];
                    continue;
                }

                weight.Clear();
                for (int j = 0; j < SmoothTaps; j++)
                {
                    float wy = rowWeight[y * SmoothTaps + j];
                    if (wy == 0) continue;
                    int sy = Math.Clamp(rowFirst[y] + j, 0, sourceHeight - 1);
                    for (int i = 0; i < SmoothTaps; i++)
                    {
                        int sx = Math.Clamp(colFirst[x] + i, 0, sourceWidth - 1);
                        weight[source[sy * sourceWidth + sx]] += wy * colWeight[x * SmoothTaps + i];
                    }
                }

                // Ties go to paint over Auto and to walls over passable, so a stroke never thins
                // below what nearest would have drawn and a diagonal wall stays joined at its corners.
                byte best = Wall;
                if (weight[Passable] > weight[best]) best = Passable;
                if (weight[Auto] > weight[best]) best = Auto;
                cells[y * width + x] = best;
            }
        });
        return cells;

        // Per output pixel along one axis: the source cell its centre falls in, the first of the
        // taps around it, and each tap's share of the kernel (the Gaussian's mass over that cell).
        static (int[] Cell, int[] First, float[] Weight) Taps(int size, int sourceSize)
        {
            var cell = new int[size];
            var first = new int[size];
            var weight = new float[size * SmoothTaps];
            for (int i = 0; i < size; i++)
            {
                double f = (i + 0.5) * sourceSize / size;   // in source cells; cell k spans [k, k+1)
                int c = Math.Min(sourceSize - 1, (int)f);
                cell[i] = c;
                first[i] = c - SmoothReach;
                for (int t = 0; t < SmoothTaps; t++)
                {
                    int k = c - SmoothReach + t;
                    weight[i * SmoothTaps + t] = (float)(Phi((k + 1 - f) / SmoothSigma) - Phi((k - f) / SmoothSigma));
                }
            }
            return (cell, first, weight);
        }

        // Standard normal CDF (Abramowitz & Stegun 7.1.26, error under 1.5e-7).
        static double Phi(double z)
        {
            double x = Math.Abs(z) / Math.Sqrt(2);
            double t = 1 / (1 + 0.3275911 * x);
            double erf = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496735) * t + 0.254829592) * t * Math.Exp(-x * x);
            return z >= 0 ? 0.5 * (1 + erf) : 0.5 * (1 - erf);
        }
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
