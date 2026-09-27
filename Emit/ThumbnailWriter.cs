using Ck3MapGen.Io;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using Brushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using Color = SixLabors.ImageSharp.Color;
using Font = SixLabors.Fonts.Font;
using FontFamily = SixLabors.Fonts.FontFamily;
using HorizontalAlignment = SixLabors.Fonts.HorizontalAlignment;
using Image = SixLabors.ImageSharp.Image;
using Pens = SixLabors.ImageSharp.Drawing.Processing.Pens;
using PointF = SixLabors.ImageSharp.PointF;
using SystemFonts = SixLabors.Fonts.SystemFonts;

namespace Ck3MapGen.Emit;

/// <summary>
/// The mod's thumbnail.png: the picture the launcher shows beside the mod and Steam uses as the
/// Workshop preview.
///
/// Cut from the flatmap, because that is the one picture of the whole world this run already
/// draws and the one players will recognise — it is what the map turns into when they zoom out.
/// The map sits in a band across the middle at full width; above and below it is the map's own sea
/// colour, darkened away from the band, so the square is parchment right to its edges and the dark
/// top gives the mod's name somewhere legible to sit.
///
/// The name is set in Paradox King Script, the face CK3 letters realm names on the map with, read
/// out of the game install rather than shipped — the font is Paradox's, the image it draws is ours.
/// </summary>
public static class ThumbnailWriter
{
    public const string FileName = "thumbnail.png";

    /// <summary>Square, because the launcher and the Workshop grid both crop to one.</summary>
    private const int Size = 1024;

    /// <summary>Steam refuses a Workshop preview over a megabyte.</summary>
    private const long MaxBytes = 1_000_000;

    /// <summary>Where the middle of the map band sits, as a fraction of the height. Below centre,
    /// so the title above has more room than the margin below.</summary>
    private const float MapCentre = 0.58f;

    /// <summary>How far the band's top and bottom edges fade into the backdrop, in pixels.</summary>
    private const int Feather = 72;

    private static readonly Color Gold = Color.ParseHex("F2DC9B");
    private static readonly Color GoldDeep = Color.ParseHex("C28F3E");
    private static readonly Color Ink = Color.ParseHex("24170A");

    /// <summary>Writes the thumbnail from the flatmap this run just rendered.</summary>
    public static void Write(string modDir, string gameDir, Flatmap flat)
    {
        using var map = Image.LoadPixelData<Bgra32>(flat.Bgra, flat.Width, flat.Height);
        Write(modDir, gameDir, map);
    }

    /// <summary>
    /// Writes the thumbnail for a mod already on disk, from its flatmap.dds. For a mod generated
    /// before thumbnails were, or one whose name has since changed in descriptor.mod.
    /// </summary>
    public static bool WriteFromDisk(string modDir, string gameDir)
    {
        string path = Path.Combine(modDir, "gfx", "map", "terrain", "flat_maps", "flatmap.dds");
        if (DdsReader.Load(path) is not { } decoded)
        {
            Console.Error.WriteLine($"  thumbnail: no readable flatmap at {path}");
            return false;
        }

        using var map = Image.LoadPixelData<Bgra32>(decoded.Bgra, decoded.Width, decoded.Height);
        Write(modDir, gameDir, map);
        return true;
    }

    private static void Write(string modDir, string gameDir, Image<Bgra32> flat)
    {
        string name = ReadName(modDir)
                      ?? Path.GetFileName(modDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        int bandHeight = (int)Math.Round((double)Size * flat.Height / flat.Width);
        int bandTop = (int)Math.Round(Size * MapCentre - bandHeight / 2.0);

        using var band = flat.CloneAs<Rgba32>();
        band.Mutate(c => c
            .Resize(Size, bandHeight, KnownResamplers.Lanczos3)
            // The flatmap is drawn to sit quietly under the game's own overlays. Alone, and small,
            // it reads washed out; a little contrast and warmth makes the relief carry.
            .Contrast(1.18f)
            .Saturate(1.3f));

        using var canvas = Backdrop(band, bandTop);
        Composite(canvas, band, bandTop);
        Vignette(canvas);

        if (TitleFont(gameDir) is { } title)
            DrawTitle(canvas, name, title, bandTop);
        else
            Console.WriteLine("  thumbnail: no title font found; writing the map alone");

        string output = Path.Combine(modDir, FileName);
        long bytes = Save(canvas, output);
        Console.WriteLine($"  thumbnail: {Size}x{Size}, {bytes / 1024} KB, \"{name}\"");
    }

    /// <summary>
    /// The map's sea colour, darkened with distance from the band, over a faint grain so it reads
    /// as parchment rather than a flat fill.
    ///
    /// Nothing is copied out of the band. Stretching its edge rows was only right on a map with a
    /// margin of sea: where land runs to the map's edge, every land pixel in that row became a
    /// vertical streak to the corner of the picture. Mirroring the band had the same problem as a
    /// ghost continent behind the title.
    /// </summary>
    private static Image<Rgba32> Backdrop(Image<Rgba32> band, int bandTop)
    {
        var sea = SeaColour(band);
        var backdrop = new Image<Rgba32>(Size, Size);
        int h = band.Height;

        backdrop.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < Size; y++)
            {
                int local = y - bandTop;
                int distance = local < 0 ? -local : local >= h ? local - h + 1 : 0;

                // Full strength where it meets the band, so there is no seam to feather away, and
                // dark far from it, where the title has to read.
                float fade = 0.28f + 0.72f * MathF.Exp(-distance / 170f);
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < Size; x++)
                {
                    float k = fade * (1f + Grain(x, y));
                    row[x] = new Rgba32(
                        (byte)Math.Clamp(sea.R * k, 0, 255),
                        (byte)Math.Clamp(sea.G * k, 0, 255),
                        (byte)Math.Clamp(sea.B * k, 0, 255),
                        255);
                }
            }
        });

        // Softens the grain into mottling, which is what parchment looks like at this size.
        backdrop.Mutate(c => c.GaussianBlur(1.6f));
        return backdrop;
    }

    /// <summary>
    /// The average of the band's darker pixels, excluding the very darkest. The flatmap glazes sea
    /// well below land (FlatmapWriter's ocean multiplier tops out at 0.78 of a lifted 1.08 land), and
    /// every CK3 map is at least a third sea, so the darkest third is sea; the darkest few percent are
    /// the coastline's ink stroke and borders, which would pull the colour toward black.
    /// </summary>
    private static (float R, float G, float B) SeaColour(Image<Rgba32> band)
    {
        var pixels = new Rgba32[band.Width * band.Height];
        band.CopyPixelDataTo(pixels);

        var luma = pixels.Select(p => 0.299f * p.R + 0.587f * p.G + 0.114f * p.B).ToArray();
        var sorted = (float[])luma.Clone();
        Array.Sort(sorted);
        float low = sorted[(int)(sorted.Length * 0.05)];
        float high = sorted[(int)(sorted.Length * 0.33)];

        double r = 0, g = 0, b = 0;
        int n = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (luma[i] < low || luma[i] > high) continue;
            r += pixels[i].R; g += pixels[i].G; b += pixels[i].B;
            n++;
        }

        return n == 0 ? (120f, 110f, 80f) : ((float)(r / n), (float)(g / n), (float)(b / n));
    }

    /// <summary>A fixed per-pixel hash in ±4%, so every run of a map draws the same grain.</summary>
    private static float Grain(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return ((h & 0xFFFF) / 65535f - 0.5f) * 0.08f;
    }

    /// <summary>Lays the band over the backdrop, its top and bottom edges feathered.</summary>
    private static void Composite(Image<Rgba32> canvas, Image<Rgba32> band, int bandTop)
    {
        canvas.ProcessPixelRows(band, (dst, src) =>
        {
            for (int y = 0; y < src.Height; y++)
            {
                int cy = bandTop + y;
                if (cy < 0 || cy >= Size) continue;

                float edge = Math.Min(y, src.Height - 1 - y) / (float)Feather;
                float alpha = edge >= 1 ? 1 : edge * edge * (3 - 2 * edge);

                var from = src.GetRowSpan(y);
                var to = dst.GetRowSpan(cy);
                for (int x = 0; x < Size; x++)
                {
                    var a = to[x];
                    var b = from[x];
                    to[x] = new Rgba32(
                        (byte)(a.R + (b.R - a.R) * alpha),
                        (byte)(a.G + (b.G - a.G) * alpha),
                        (byte)(a.B + (b.B - a.B) * alpha),
                        255);
                }
            }
        });
    }

    private static void Vignette(Image<Rgba32> canvas)
    {
        canvas.ProcessPixelRows(rows =>
        {
            float half = Size / 2f;
            for (int y = 0; y < Size; y++)
            {
                var row = rows.GetRowSpan(y);
                float dy = (y - half) / half;
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x - half) / half;
                    float k = 1f - 0.6f * Math.Clamp((dx * dx + dy * dy) / 2f, 0f, 1f);
                    var p = row[x];
                    row[x] = new Rgba32((byte)(p.R * k), (byte)(p.G * k), (byte)(p.B * k), 255);
                }
            }
        });
    }

    /// <summary>
    /// The name, centred in the dark space above the map: a soft ink shadow first, then the letters
    /// in a gold gradient with a thin ink outline, which is how the game's own title cards set it.
    /// </summary>
    private static void DrawTitle(Image<Rgba32> canvas, string name, FontFamily family, int bandTop)
    {
        float maxWidth = Size * 0.86f;
        // A margin at the top of the picture and a smaller one above the band, so a name wrapped
        // onto two lines still clears the frame and the coast.
        const float margin = 48f;
        float maxHeight = Math.Max(bandTop - margin * 1.6f, Size * 0.2f);
        float centreY = margin + maxHeight / 2f;

        // Largest size that fits, wrapping a long name onto a second line rather than shrinking it
        // to nothing.
        Font font = family.CreateFont(180f);
        RichTextOptions options = Options(font);
        for (float size = 180f; size >= 48f; size -= 4f)
        {
            font = family.CreateFont(size);
            options = Options(font);
            var box = TextMeasurer.MeasureBounds(name, options);
            if (box.Width <= maxWidth && box.Height <= maxHeight) break;
        }

        // Centre the ink, not the layout box. Vertical centring goes by line metrics, which for
        // King Script's tall ascender puts a two-line name well above where its letters sit — the
        // first line ran off the top of the picture.
        var drawn = TextMeasurer.MeasureBounds(name, options);
        float originY = centreY + (centreY - (drawn.Top + drawn.Bottom) / 2f);
        options = Options(font, originY);

        RichTextOptions Options(Font f, float? y = null) => new(f)
        {
            Origin = new PointF(Size / 2f, y ?? centreY),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            WrappingLength = maxWidth,
        };

        using (var shadow = new Image<Rgba32>(Size, Size))
        {
            var offset = new RichTextOptions(options) { Origin = new PointF(Size / 2f, originY + font.Size * 0.04f) };
            shadow.Mutate(c => c
                .Paint(canvas => canvas.DrawText(offset, name, Brushes.Solid(Color.Black.WithAlpha(0.9f)),
                    Pens.Solid(Color.Black.WithAlpha(0.9f), font.Size * 0.08f)))
                .GaussianBlur(font.Size * 0.09f));
            canvas.Mutate(c => c.DrawImage(shadow, 1f).DrawImage(shadow, 0.6f));
        }

        var bounds = TextMeasurer.MeasureBounds(name, options);
        var gold = new LinearGradientBrush(
            new PointF(0, bounds.Top), new PointF(0, bounds.Bottom),
            GradientRepetitionMode.None,
            new ColorStop(0f, Gold), new ColorStop(0.55f, Gold), new ColorStop(1f, GoldDeep));

        canvas.Mutate(c => c.Paint(surface =>
            surface.DrawText(options, name, gold, Pens.Solid(Ink, Math.Max(1.5f, font.Size * 0.012f)))));
    }

    /// <summary>
    /// CK3's map-name face if the install has it, the game's calligraphic body face if not, and a
    /// Windows serif as the last resort.
    /// </summary>
    private static FontFamily? TitleFont(string gameDir)
    {
        var collection = new FontCollection();
        foreach (string relative in new[]
                 {
                     Path.Combine("fonts", "mapfont", "Paradox_King_Script.otf"),
                     Path.Combine("fonts", "Fondamento", "Fondamento-Regular.ttf"),
                 })
        {
            string path = Path.Combine(gameDir, relative);
            if (!File.Exists(path)) continue;
            try { return collection.Add(path); }
            catch (Exception e) when (e is IOException or InvalidFontFileException) { }
        }

        foreach (string system in new[] { "Book Antiqua", "Georgia", "Cambria" })
            if (SystemFonts.TryGet(system, out var family))
                return family;

        return null;
    }

    /// <summary>
    /// Full colour when it fits under Steam's limit, which parchment at this size usually does not;
    /// a dithered 256-colour palette when it does not, which on a picture this warm and this soft
    /// is hard to tell from the original.
    /// </summary>
    private static long Save(Image<Rgba32> canvas, string path)
    {
        var encoder = new PngEncoder
        {
            ColorType = PngColorType.Rgb,
            CompressionLevel = PngCompressionLevel.BestCompression,
        };
        canvas.SaveAsPng(path, encoder);

        if (new FileInfo(path).Length > MaxBytes)
        {
            canvas.SaveAsPng(path, new PngEncoder
            {
                ColorType = PngColorType.Palette,
                CompressionLevel = PngCompressionLevel.BestCompression,
                Quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = 256 }),
            });
        }

        return new FileInfo(path).Length;
    }

    /// <summary>The launcher name out of descriptor.mod, which is what the player will know the mod
    /// by — the folder name is often a filesystem-safe spelling of it.</summary>
    private static string? ReadName(string modDir)
    {
        string descriptor = Path.Combine(modDir, "descriptor.mod");
        if (!File.Exists(descriptor)) return null;

        foreach (string line in File.ReadLines(descriptor))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("name", StringComparison.Ordinal)) continue;

            int open = trimmed.IndexOf('"');
            int close = trimmed.LastIndexOf('"');
            if (open >= 0 && close > open) return trimmed[(open + 1)..close];
        }

        return null;
    }
}
