using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Paints each struggle phase the picture the struggle window draws behind itself.
///
/// The path is not declarable. <c>Struggle.GetCurrentPhase.GetBackground</c> resolves by convention
/// to <c>gfx/interface/illustrations/struggle_backgrounds/&lt;phase key&gt;_bg.dds</c>, exactly as
/// the phase icon does, so a generated phase key means a file only this run can name. Vanilla's
/// fallback for a name it cannot find is <c>_default_bg.dds</c>, which is a 128x128 magenta
/// placeholder — the missing-texture colour — so this is not a polish pass. Without it the struggle
/// window is bright pink.
///
/// The picture is a crop of the flatmap around the struggle's own ground, with the region tinted by
/// mood. That is the one image nobody could have shipped in advance: the whole point of a generated
/// struggle is that it is somewhere in particular, and a stock illustration would say only that a
/// struggle is happening, not where. It also costs nothing to draw — the parchment was already
/// rendered for the map itself.
/// </summary>
public static class StruggleArt
{
    /// <summary>
    /// Output edge, in pixels. Vanilla's phase backgrounds are 1240 square.
    ///
    /// This is the expensive constant in the feature: <see cref="DdsWriter"/> writes uncompressed
    /// BGRA, so each file is <c>Size * Size * 4</c> bytes — four megabytes at 1024, and four files
    /// per struggle. Worth knowing before raising either this or <c>MaxStruggles</c>.
    /// </summary>
    private const int Size = 1024;

    /// <summary>
    /// How much wider than the struggle itself the view is.
    ///
    /// Some margin is not decoration. A crop pulled tight to the region shows a shape with no
    /// coastline, no neighbours and no sense of scale, which reads as an abstract blob rather than
    /// as a place; a little of what surrounds it is what makes it legible as territory.
    /// </summary>
    private const double Margin = 1.55;

    /// <summary>How far the tint pulls the region's own ground toward its mood colour.</summary>
    private const double TintStrength = 0.55;

    /// <summary>How much the land outside the struggle is pushed down, so the region reads first.</summary>
    private const double OutsideDim = 0.62;

    /// <summary>
    /// A flat multiplier over the whole picture.
    ///
    /// The flatmap is drawn to be read at full brightness on its own; the struggle window is dark
    /// panelling, and parchment at full strength behind it glares badly enough to make the text on
    /// top hard to read. The window's own masks fade the edges but do not touch the middle.
    /// </summary>
    private const double Dim = 0.78;

    /// <summary>
    /// The colour each mood stains its ground with, as RGB.
    ///
    /// Applied as a colourise rather than a paint — see <see cref="Blend"/> — so the hillshade and
    /// the parchment grain survive it. A flat fill would erase the relief that makes the crop worth
    /// cutting in the first place.
    /// </summary>
    private static (double R, double G, double B) Tint(StruggleMood mood) => mood switch
    {
        StruggleMood.Bloodshed => (158, 44, 34),
        StruggleMood.Ambition => (181, 128, 40),
        StruggleMood.Accommodation => (78, 100, 128),
        _ => (74, 122, 78),
    };

    /// <returns>How many files were written.</returns>
    public static int WriteBackgrounds(
        string modDir, StruggleMap struggles, Flatmap flat, ProvinceMap provinces, int[] order)
    {
        // The flatmap is rendered at province resolution and so is the label grid, which is what
        // lets a pixel be tested for membership at all. If they ever disagree the crop would be
        // sampling one map and colouring from another, so it is checked rather than assumed.
        if (flat.Width != provinces.Width || flat.Height != provinces.Height) return 0;

        string dir = Path.Combine(modDir, "gfx", "interface", "illustrations", "struggle_backgrounds");
        Directory.CreateDirectory(dir);

        int written = 0;

        foreach (var struggle in struggles.Struggles)
        {
            var owned = Provinces(struggle);
            if (owned.Count == 0) continue;

            var window = Window(owned, flat, provinces, order);
            if (window is null) continue;

            foreach (var phase in struggle.Phases)
            {
                var pixels = Render(window.Value, phase.Mood, owned, flat, provinces, order);
                DdsWriter.WriteBgra(Path.Combine(dir, $"{phase.Key}_bg.dds"), Size, Size, pixels);
                written++;
            }
        }

        return written;
    }

    /// <summary>Every province id the struggle's counties are made of.</summary>
    private static HashSet<int> Provinces(GeneratedStruggle struggle)
    {
        var owned = new HashSet<int>();

        foreach (var barony in struggle.Duchies
                     .SelectMany(d => d.Children)
                     .Where(c => c.Tier == "c")
                     .SelectMany(c => c.Children)
                     .Where(b => b.Tier == "b" && b.ProvinceId > 0))
        {
            owned.Add(barony.ProvinceId);
        }

        return owned;
    }

    /// <summary>The square of the flatmap the picture is cut from, or null if the region has no
    /// pixels on the map at all.</summary>
    private static (int X, int Y, int Side)? Window(
        HashSet<int> owned, Flatmap flat, ProvinceMap provinces, int[] order)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

        for (int y = 0; y < flat.Height; y++)
        {
            int row = y * flat.Width;
            for (int x = 0; x < flat.Width; x++)
            {
                if (!owned.Contains(order[provinces.Label[row + x]])) continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (minX > maxX) return null;

        // A square window, because the target is square and letting the crop be oblong would mean
        // choosing between stretching it and letting centercrop throw away one axis.
        double side = Math.Max(maxX - minX + 1, maxY - minY + 1) * Margin;

        // A floor as well as a ceiling. A struggle over a small kingdom on a small map can cover
        // sixty pixels, and blowing sixty up to a thousand is not a map, it is four large squares.
        // Widening the view instead trades a tight framing for something that still reads as land.
        side = Math.Max(side, Math.Max(256, flat.Width / 6.0));
        side = Math.Min(side, Math.Min(flat.Width, flat.Height));

        int edge = (int)Math.Round(side);
        int cx = (minX + maxX) / 2;
        int cy = (minY + maxY) / 2;

        int x0 = Math.Clamp(cx - edge / 2, 0, flat.Width - edge);
        int y0 = Math.Clamp(cy - edge / 2, 0, flat.Height - edge);

        return (x0, y0, edge);
    }

    private static byte[] Render(
        (int X, int Y, int Side) window, StruggleMood mood, HashSet<int> owned,
        Flatmap flat, ProvinceMap provinces, int[] order)
    {
        var output = new byte[Size * Size * 4];
        var tint = Tint(mood);
        double step = (double)window.Side / Size;

        Parallel.For(0, Size, oy =>
        {
            double sy = window.Y + (oy + 0.5) * step;

            for (int ox = 0; ox < Size; ox++)
            {
                double sx = window.X + (ox + 0.5) * step;

                var (r, g, b) = Sample(flat, sx, sy);

                // Coverage rather than a yes/no test. One output pixel covers several source
                // pixels once the view is wider than the target, so a hard test would draw the
                // region's border as a staircase; averaging the four nearest labels gives the edge
                // a pixel of softness, which is all it needs behind a mask and an alpha of 0.9.
                double inside = Coverage(sx, sy, owned, provinces, order);

                if (inside > 0)
                {
                    var stained = Blend(r, g, b, tint);
                    double t = inside * TintStrength;
                    r += (stained.R - r) * t;
                    g += (stained.G - g) * t;
                    b += (stained.B - b) * t;
                }

                // Everything outside the struggle is pushed down toward the same neutral, so the
                // eye lands on the region regardless of what the terrain around it happens to be.
                double dim = Dim * (OutsideDim + (1 - OutsideDim) * inside);
                r *= dim; g *= dim; b *= dim;

                int o = (oy * Size + ox) * 4;
                output[o + 0] = (byte)Math.Clamp(b, 0, 255);
                output[o + 1] = (byte)Math.Clamp(g, 0, 255);
                output[o + 2] = (byte)Math.Clamp(r, 0, 255);
                output[o + 3] = 255;
            }
        });

        return output;
    }

    /// <summary>
    /// Colourises rather than paints: the tint is scaled by how bright the source pixel already is.
    ///
    /// Straight interpolation toward a flat colour flattens the hillshade, and the hillshade is the
    /// only thing telling the viewer this is terrain. Scaling by luminance keeps every ridge and
    /// coastal stroke and changes only the hue they are drawn in.
    /// </summary>
    private static (double R, double G, double B) Blend(
        double r, double g, double b, (double R, double G, double B) tint)
    {
        // Divided by a mid-parchment value rather than by 255: the flatmap's land sits high in the
        // range, and normalising against white would make every tint come out washed out.
        double luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 190.0;
        return (tint.R * luminance, tint.G * luminance, tint.B * luminance);
    }

    private static (double R, double G, double B) Sample(Flatmap flat, double x, double y)
    {
        int x0 = Math.Clamp((int)Math.Floor(x - 0.5), 0, flat.Width - 1);
        int y0 = Math.Clamp((int)Math.Floor(y - 0.5), 0, flat.Height - 1);
        int x1 = Math.Min(x0 + 1, flat.Width - 1);
        int y1 = Math.Min(y0 + 1, flat.Height - 1);

        double fx = Math.Clamp(x - 0.5 - x0, 0, 1);
        double fy = Math.Clamp(y - 0.5 - y0, 0, 1);

        double r = 0, g = 0, b = 0;

        foreach (var (px, py, weight) in new[]
                 {
                     (x0, y0, (1 - fx) * (1 - fy)),
                     (x1, y0, fx * (1 - fy)),
                     (x0, y1, (1 - fx) * fy),
                     (x1, y1, fx * fy),
                 })
        {
            int o = (py * flat.Width + px) * 4;
            b += flat.Bgra[o + 0] * weight;
            g += flat.Bgra[o + 1] * weight;
            r += flat.Bgra[o + 2] * weight;
        }

        return (r, g, b);
    }

    /// <summary>What fraction of the four source pixels around a sample belong to the struggle.</summary>
    private static double Coverage(
        double x, double y, HashSet<int> owned, ProvinceMap provinces, int[] order)
    {
        int x0 = Math.Clamp((int)Math.Floor(x - 0.5), 0, provinces.Width - 1);
        int y0 = Math.Clamp((int)Math.Floor(y - 0.5), 0, provinces.Height - 1);
        int x1 = Math.Min(x0 + 1, provinces.Width - 1);
        int y1 = Math.Min(y0 + 1, provinces.Height - 1);

        int hits = 0;
        foreach (var (px, py) in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) })
            if (owned.Contains(order[provinces.Label[py * provinces.Width + px]])) hits++;

        return hits / 4.0;
    }
}
