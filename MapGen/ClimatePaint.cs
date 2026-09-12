using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;

namespace Ck3MapGen.MapGen;

/// <summary>
/// One climate brush: a Koppen class the user can ask for by name, and the climate *profile* that
/// asking for it paints.
///
/// The profile is the point. A brush does not stamp its class onto the map — it supplies a target
/// sea-level temperature, a seasonal swing, a yearly rainfall and how much of that rain falls in
/// summer, and <see cref="ClimateModel"/> blends those into the modelled climate by the paint's
/// weight. The class the map ends up with is then *predicted* by <see cref="Koppen.Classify"/>
/// from the blended numbers, at the elevation the pixel actually has. So a rainforest brush over
/// a plateau gives tropical lowland and a cooler upland rather than jungle on every summit, and
/// two brushes overlapping give a real transition rather than a border.
///
/// Every profile here is chosen so that, at sea level and undiluted, it classifies as its own
/// class — checked against <see cref="Koppen.Classify"/>'s thresholds, which is why some numbers
/// sit where they do (the Mediterranean summer share, for instance, has to stay well under the
/// 40 mm driest-summer-month line).
/// </summary>
/// <param name="Class">What the brush asks for, and what it is named after.</param>
/// <param name="Name">The palette label.</param>
/// <param name="Colour">The paint colour on the canvas: the familiar Koppen-map family.</param>
/// <param name="Hint">One line for the tooltip, in the user's terms rather than the model's.</param>
/// <param name="SeaLevelC">Annual mean temperature at sea level, in Celsius.</param>
/// <param name="RangeC">Warmest month minus coldest month, in Celsius.</param>
/// <param name="AnnualMm">Yearly rainfall, in millimetres.</param>
/// <param name="SummerShare">Fraction of the rain that falls in the local summer half-year.</param>
/// <param name="Compact">Shown in the short palette; the rest sit under "More climates".</param>
public sealed record ClimateBrush(
    KoppenClass Class,
    string Name,
    (byte R, byte G, byte B) Colour,
    string Hint,
    float SeaLevelC,
    float RangeC,
    float AnnualMm,
    float SummerShare,
    bool Compact)
{
    /// <summary>
    /// The palette, in the order it is shown. Index into this is what the paint layers are keyed
    /// by, and what the file format stores, so entries are appended and never reordered.
    /// </summary>
    public static readonly IReadOnlyList<ClimateBrush> All =
    [
        new(KoppenClass.TropicalRainforest, "Rainforest", (0, 0, 254),
            "Hot and wet all year. Jungle.",
            26f, 3f, 2500f, 0.50f, true),
        new(KoppenClass.TropicalMonsoon, "Monsoon", (0, 119, 255),
            "Hot, with a short dry season and a drenching wet one. Jungle and open ground.",
            26f, 4f, 2000f, 0.85f, true),
        new(KoppenClass.TropicalSavanna, "Savanna", (70, 169, 250),
            "Hot, with a long dry season. Grassland with patches of forest.",
            25f, 5f, 1000f, 0.85f, true),
        new(KoppenClass.HotDesert, "Hot desert", (254, 0, 0),
            "Hot and nearly rainless. Sand and rock.",
            24f, 14f, 80f, 0.50f, true),
        new(KoppenClass.HotSteppe, "Steppe", (245, 163, 1),
            "Warm and dry, with just enough rain for grass. Drylands.",
            20f, 14f, 350f, 0.60f, true),
        new(KoppenClass.Mediterranean, "Mediterranean", (255, 255, 0),
            "Warm, dry summers and mild, wetter winters. Scrub, groves and open forest.",
            17f, 14f, 550f, 0.12f, true),
        new(KoppenClass.Oceanic, "Temperate oceanic", (102, 255, 51),
            "Mild all year, rain in every season. Deciduous forest and meadow.",
            10f, 10f, 900f, 0.45f, true),
        new(KoppenClass.HumidSubtropical, "Humid subtropical", (198, 255, 78),
            "Hot, humid summers and mild winters. Dense forest.",
            18f, 18f, 1200f, 0.60f, true),
        new(KoppenClass.HumidContinental, "Continental", (120, 60, 200),
            "Warm summers and freezing winters. Mixed forest.",
            6f, 30f, 700f, 0.60f, true),
        new(KoppenClass.Subarctic, "Subarctic", (190, 160, 240),
            "Short cool summers and long hard winters. Taiga.",
            -3f, 40f, 450f, 0.60f, true),
        new(KoppenClass.Tundra, "Tundra", (178, 178, 178),
            "Too cold for trees in any month. Moss, rock and snow.",
            -8f, 26f, 250f, 0.55f, true),

        // The expanded palette. Cold desert and cold steppe are the same aridity at continental
        // temperatures; ice cap is tundra that never thaws.
        new(KoppenClass.ColdDesert, "Cold desert", (254, 150, 149),
            "Dry, with hot summers and cold winters. Bare steppe.",
            10f, 26f, 120f, 0.50f, false),
        new(KoppenClass.ColdSteppe, "Cold steppe", (255, 219, 99),
            "Dry grassland with a real winter. Steppe.",
            8f, 28f, 300f, 0.60f, false),
        new(KoppenClass.IceCap, "Ice cap", (104, 104, 104),
            "Below freezing all year. Permanent ice.",
            -25f, 30f, 150f, 0.50f, false),
    ];

    /// <summary>The Koppen class this profile classifies as at sea level and full strength.</summary>
    public KoppenClass Predicted()
    {
        float half = RangeC * 0.5f;
        float summer = AnnualMm * SummerShare;
        return Koppen.Classify(SeaLevelC + half, SeaLevelC - half, SeaLevelC, AnnualMm, summer,
            AnnualMm - summer);
    }
}

/// <summary>
/// The painted climate: one weight layer per brush, at an authoring resolution of the map.
///
/// Weights, not a class per pixel, because strokes have to blend. A pixel carries up to 255 of
/// each brush and the layers together never exceed 255 — a stroke composites "over" what is there,
/// scaling every other layer down by its own alpha — so the sum is how strongly the pixel is
/// painted at all, and the layers' proportions are what it is painted *as*. Where nothing has been
/// painted the sum is zero and the model's own climate stands untouched; that is what makes an
/// unvisited tab change nothing.
///
/// Sized to the climate model's coarse grid — 1024 across for any map at least that wide — which
/// is the resolution the model blends at anyway. Painting finer than that would be lost.
/// </summary>
public sealed class ClimatePaint
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>One byte per pixel per brush, indexed as <see cref="ClimateBrush.All"/>.</summary>
    public byte[][] Layers { get; }

    /// <summary>Bumped on every change, so a cache can tell whether it is looking at this paint.</summary>
    public int Version { get; private set; }

    public static int BrushCount => ClimateBrush.All.Count;

    public ClimatePaint(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Layers = new byte[BrushCount][];
        for (int k = 0; k < Layers.Length; k++) Layers[k] = new byte[Width * Height];
    }

    /// <summary>
    /// The authoring size for a map whose province raster is this big: the climate model's own
    /// coarse grid, so a paint pixel is one model cell.
    /// </summary>
    public static (int Width, int Height) SizeFor(int provinceWidth, int provinceHeight)
    {
        int w = Math.Min(1024, Math.Max(1, provinceWidth));
        int h = Math.Max(2, (int)((long)w * Math.Max(1, provinceHeight) / Math.Max(1, provinceWidth)));
        return (w, h);
    }

    /// <summary>True when nothing has been painted anywhere.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var layer in Layers)
                foreach (byte b in layer)
                    if (b != 0) return false;
            return true;
        }
    }

    /// <summary>Total paint at a pixel, 0..1.</summary>
    public float WeightAt(int index)
    {
        int sum = 0;
        foreach (var layer in Layers) sum += layer[index];
        return Math.Min(255, sum) / 255f;
    }

    /// <summary>The brush with the most paint at a pixel and its share of the paint, or null where there is none.</summary>
    public (ClimateBrush Brush, float Share, float Weight)? Dominant(int index)
    {
        int sum = 0, best = -1, bestValue = 0;
        for (int k = 0; k < Layers.Length; k++)
        {
            int v = Layers[k][index];
            sum += v;
            if (v > bestValue) { bestValue = v; best = k; }
        }

        if (best < 0 || sum == 0) return null;
        return (ClimateBrush.All[best], bestValue / (float)sum, Math.Min(255, sum) / 255f);
    }

    /// <summary>The blended paint colour at a pixel and how opaque it is, for the canvas wash.</summary>
    public (float R, float G, float B, float Weight) ColourAt(int index)
    {
        float r = 0, g = 0, b = 0;
        int sum = 0;
        for (int k = 0; k < Layers.Length; k++)
        {
            int v = Layers[k][index];
            if (v == 0) continue;
            var c = ClimateBrush.All[k].Colour;
            r += c.R * v; g += c.G * v; b += c.B * v;
            sum += v;
        }

        if (sum == 0) return (0, 0, 0, 0);
        return (r / sum, g / sum, b / sum, Math.Min(255, sum) / 255f);
    }

    /// <summary>Share of pixels (or of the given mask's set pixels) that carry any paint.</summary>
    public double Coverage(byte[]? mask = null, int maskWidth = 0, int maskHeight = 0)
    {
        long painted = 0, total = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (mask is not null)
                {
                    int mx = (int)((long)x * maskWidth / Width), my = (int)((long)y * maskHeight / Height);
                    if (mask[Math.Clamp(my, 0, maskHeight - 1) * maskWidth + Math.Clamp(mx, 0, maskWidth - 1)] == 0)
                        continue;
                }

                total++;
                if (WeightAt(y * Width + x) > 0.02f) painted++;
            }
        }

        return total == 0 ? 0 : (double)painted / total;
    }

    public void Touch() => Version++;

    /// <summary>A copy at another size, resampled by nearest pixel. The same paint on a different map.</summary>
    public ClimatePaint Resampled(int width, int height)
    {
        if (width == Width && height == Height) return Clone();

        var result = new ClimatePaint(width, height);
        for (int y = 0; y < height; y++)
        {
            int sy = Math.Min(Height - 1, (int)((long)y * Height / height));
            for (int x = 0; x < width; x++)
            {
                int sx = Math.Min(Width - 1, (int)((long)x * Width / width));
                int from = sy * Width + sx, to = y * width + x;
                for (int k = 0; k < Layers.Length; k++) result.Layers[k][to] = Layers[k][from];
            }
        }

        return result;
    }

    public ClimatePaint Clone()
    {
        var copy = new ClimatePaint(Width, Height);
        for (int k = 0; k < Layers.Length; k++) Array.Copy(Layers[k], copy.Layers[k], Layers[k].Length);
        return copy;
    }

    // ------------------------------------------------------------------ targets for the model

    /// <summary>
    /// The paint evaluated on a grid of another size: how strongly each cell is painted, and the
    /// profile it is painted towards. Bilinear across paint pixels, so a coarse paint on a fine
    /// grid does not step.
    /// </summary>
    public ClimatePaintTargets Targets(int width, int height)
    {
        var weight = new float[width * height];
        var seaC = new float[width * height];
        var rangeC = new float[width * height];
        var annual = new float[width * height];
        var summer = new float[width * height];

        var brushes = ClimateBrush.All;

        Parallel.For(0, height, y =>
        {
            float py = ((y + 0.5f) * Height / height) - 0.5f;
            int y0 = (int)MathF.Floor(py);
            float fy = py - y0;
            int ya = Math.Clamp(y0, 0, Height - 1), yb = Math.Clamp(y0 + 1, 0, Height - 1);

            for (int x = 0; x < width; x++)
            {
                float px = ((x + 0.5f) * Width / width) - 0.5f;
                int x0 = (int)MathF.Floor(px);
                float fx = px - x0;
                int xa = Math.Clamp(x0, 0, Width - 1), xb = Math.Clamp(x0 + 1, 0, Width - 1);

                float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
                int i00 = ya * Width + xa, i10 = ya * Width + xb, i01 = yb * Width + xa, i11 = yb * Width + xb;

                float total = 0, t = 0, r = 0, a = 0, s = 0;
                for (int k = 0; k < brushes.Count; k++)
                {
                    var layer = Layers[k];
                    float v = layer[i00] * w00 + layer[i10] * w10 + layer[i01] * w01 + layer[i11] * w11;
                    if (v <= 0f) continue;

                    var b = brushes[k];
                    total += v;
                    t += v * b.SeaLevelC;
                    r += v * b.RangeC;
                    a += v * b.AnnualMm;
                    s += v * b.SummerShare;
                }

                int i = y * width + x;
                if (total <= 0f) continue;

                // Premultiplied by the weight, so that sampling between a painted cell and an
                // empty one interpolates the *paint* and not the paint's numbers against zero.
                float w = Math.Min(1f, total / 255f);
                weight[i] = w;
                seaC[i] = w * t / total;
                rangeC[i] = w * r / total;
                annual[i] = w * a / total;
                summer[i] = w * s / total;
            }
        });

        return new ClimatePaintTargets(width, height, weight, seaC, rangeC, annual, summer);
    }

    // ------------------------------------------------------------------ file

    /// <summary>
    /// Writes the layers as one 8-bit greyscale PNG, stacked top to bottom in brush order. Plain
    /// enough to open in any image editor and small on disk, because most of a painted map is
    /// still zero.
    /// </summary>
    public void Save(string path)
    {
        using var image = new SixLabors.ImageSharp.Image<L8>(Width, Height * Layers.Length);
        image.ProcessPixelRows(accessor =>
        {
            for (int k = 0; k < Layers.Length; k++)
            {
                var layer = Layers[k];
                for (int y = 0; y < Height; y++)
                {
                    var row = accessor.GetRowSpan(k * Height + y);
                    int src = y * Width;
                    for (int x = 0; x < Width; x++) row[x] = new L8(layer[src + x]);
                }
            }
        });

        // The layer count travels in a text chunk, so a reader never has to guess it from the
        // height: a 7-layer file twice as tall divides by 14 just as cleanly.
        SixLabors.ImageSharp.ImageMetadataExtensions.GetPngMetadata(image.Metadata).TextData.Add(
            new SixLabors.ImageSharp.Formats.Png.Chunks.PngTextData(LayerCountKey, Layers.Length.ToString(), "", ""));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
    }

    private const string LayerCountKey = "ck3mapgen-climate-layers";

    /// <summary>
    /// Reads a file written by <see cref="Save"/>. A file with fewer layers than the palette now
    /// has — written before a brush was added — loads with the missing layers empty; one with more
    /// is refused rather than guessed at.
    /// </summary>
    public static ClimatePaint Load(string path)
    {
        using var image = SharpImage.Load<L8>(path);

        int count = 0;
        foreach (var text in SixLabors.ImageSharp.ImageMetadataExtensions.GetPngMetadata(image.Metadata).TextData)
            if (text.Keyword == LayerCountKey && int.TryParse(text.Value, out int declared)) count = declared;

        if (count > BrushCount || (count > 0 && image.Height % count != 0))
            throw new InvalidDataException($"Not a climate paint file this build can read: it declares {count} layers.");
        if (count == 0) count = LayerCount(image.Height);

        int height = image.Height / count;

        var paint = new ClimatePaint(image.Width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int k = 0; k < count; k++)
            {
                var layer = paint.Layers[k];
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(k * height + y);
                    int dst = y * paint.Width;
                    for (int x = 0; x < paint.Width; x++) layer[dst + x] = row[x].PackedValue;
                }
            }
        });

        // Layers past 255 in total were never written by this class; clamp on the way in so a
        // hand-edited file cannot push a pixel past fully painted.
        for (int i = 0; i < paint.Width * paint.Height; i++)
        {
            int sum = 0;
            for (int k = 0; k < count; k++) sum += paint.Layers[k][i];
            if (sum <= 255) continue;

            for (int k = 0; k < count; k++)
                paint.Layers[k][i] = (byte)(paint.Layers[k][i] * 255 / sum);
        }

        return paint;
    }

    private static int LayerCount(int imageHeight)
    {
        // The stack height is a multiple of the layer count. Try the current palette first, then
        // every smaller count, so an older file still divides cleanly.
        for (int count = BrushCount; count >= 1; count--)
            if (imageHeight % count == 0 && count <= BrushCount) return count;

        throw new InvalidDataException("Not a climate paint file: its height is not a stack of layers.");
    }
}

/// <summary>
/// <see cref="ClimatePaint"/> evaluated on the climate model's grid. See
/// <see cref="ClimatePaint.Targets"/>. The four profile channels are premultiplied by
/// <see cref="Weight"/>; <see cref="Sample"/> divides it back out.
/// </summary>
public sealed record ClimatePaintTargets(
    int Width, int Height,
    float[] Weight, float[] SeaLevelC, float[] RangeC, float[] AnnualMm, float[] SummerShare)
{
    /// <summary>The paint at one pixel of a field of another size, bilinear across this grid.</summary>
    public (float Weight, float SeaLevelC, float RangeC, float AnnualMm, float SummerShare) Sample(
        int x, int y, int fieldWidth, int fieldHeight)
    {
        float gx = ((x + 0.5f) * Width / fieldWidth) - 0.5f;
        float gy = ((y + 0.5f) * Height / fieldHeight) - 0.5f;

        float w = Field.Sample(Weight, Width, Height, gx, gy);
        if (w <= 1e-4f) return (0f, 0f, 0f, 0f, 0f);

        return (w,
            Field.Sample(SeaLevelC, Width, Height, gx, gy) / w,
            Field.Sample(RangeC, Width, Height, gx, gy) / w,
            Field.Sample(AnnualMm, Width, Height, gx, gy) / w,
            Field.Sample(SummerShare, Width, Height, gx, gy) / w);
    }
}

/// <summary>
/// How strongly each place was painted, carried on the finished <see cref="ClimateField"/> so
/// that later stages can tell painted ground from modelled ground — the terrain classifier lets
/// paint override an imported biome, and nothing else.
/// </summary>
public sealed class ClimatePaintInfluence(int width, int height, float[] weight)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>Paint weight, 0..1, at a pixel of a field of another size.</summary>
    public float At(int x, int y, int fieldWidth, int fieldHeight)
        => Field.Sample(weight, Width, Height,
            ((x + 0.5f) * Width / fieldWidth) - 0.5f,
            ((y + 0.5f) * Height / fieldHeight) - 0.5f);
}

/// <summary>
/// A stroke in progress on a <see cref="ClimatePaint"/>: one brush (or the eraser), a size, a
/// strength and an edge softness, laid down as dabs along the mouse path.
///
/// Dabs do not pile up inside one stroke. The stroke keeps its own alpha mask, each dab raises
/// the mask to at least its own falloff, and the pixel is recomposited from what it was *before
/// the stroke* by that alpha — so painting back and forth over the same place in one gesture
/// lays down the strength once, the way a real brush behaves, while a second stroke over it does
/// build. Full-strength strokes converge on the brush in their interior; soft edges and lower
/// strengths leave a mix.
/// </summary>
public sealed class ClimateStroke
{
    private readonly ClimatePaint _paint;
    private readonly byte[][] _before;
    private readonly float[] _alpha;
    private readonly int _brush;      // -1 erases
    private readonly float _radius;   // in paint pixels
    private readonly float _strength;
    private readonly float _hardness; // 0 = all edge, 1 = no edge
    private float _lastX = float.NaN, _lastY = float.NaN;

    private int _minX = int.MaxValue, _minY = int.MaxValue, _maxX = -1, _maxY = -1;

    /// <summary>The pixels this stroke has touched so far, for the canvas to recomposite.</summary>
    public Rectangle? Dirty => _maxX < 0
        ? null
        : new Rectangle(_minX, _minY, _maxX - _minX + 1, _maxY - _minY + 1);

    /// <summary>Everything the stroke has touched; grows as it goes.</summary>
    public Rectangle? Touched => Dirty;

    public ClimateStroke(ClimatePaint paint, int brush, float radiusPixels, float strength, float softness)
    {
        _paint = paint;
        _brush = brush;
        _radius = Math.Max(0.5f, radiusPixels);
        _strength = Math.Clamp(strength, 0f, 1f);
        _hardness = 1f - Math.Clamp(softness, 0f, 1f);

        _before = new byte[paint.Layers.Length][];
        for (int k = 0; k < _before.Length; k++) _before[k] = (byte[])paint.Layers[k].Clone();
        _alpha = new float[paint.Width * paint.Height];
    }

    /// <summary>Extends the stroke to a point in paint pixels, dabbing along the way from the last one.</summary>
    public Rectangle? MoveTo(float x, float y)
    {
        int dirtyMinX = int.MaxValue, dirtyMinY = int.MaxValue, dirtyMaxX = -1, dirtyMaxY = -1;

        if (float.IsNaN(_lastX))
        {
            Dab(x, y);
        }
        else
        {
            float dx = x - _lastX, dy = y - _lastY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            float spacing = Math.Max(0.75f, _radius * 0.2f);
            int steps = Math.Max(1, (int)MathF.Ceiling(distance / spacing));
            for (int s = 1; s <= steps; s++)
                Dab(_lastX + dx * s / steps, _lastY + dy * s / steps);
        }

        _lastX = x;
        _lastY = y;

        void Track(int px, int py)
        {
            dirtyMinX = Math.Min(dirtyMinX, px); dirtyMaxX = Math.Max(dirtyMaxX, px);
            dirtyMinY = Math.Min(dirtyMinY, py); dirtyMaxY = Math.Max(dirtyMaxY, py);
        }

        void Dab(float cx, float cy)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(cx - _radius)), x1 = Math.Min(_paint.Width - 1, (int)MathF.Ceiling(cx + _radius));
            int y0 = Math.Max(0, (int)MathF.Floor(cy - _radius)), y1 = Math.Min(_paint.Height - 1, (int)MathF.Ceiling(cy + _radius));
            if (x0 > x1 || y0 > y1) return;

            for (int py = y0; py <= y1; py++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    float ddx = px + 0.5f - cx, ddy = py + 0.5f - cy;
                    float t = MathF.Sqrt(ddx * ddx + ddy * ddy) / _radius;
                    if (t >= 1f) continue;

                    float falloff = t <= _hardness ? 1f
                        : Smooth(1f - (t - _hardness) / Math.Max(1e-4f, 1f - _hardness));
                    float a = _strength * falloff;

                    int i = py * _paint.Width + px;
                    if (a <= _alpha[i]) continue;

                    _alpha[i] = a;
                    Composite(i, a);
                    Track(px, py);
                }
            }

            _minX = Math.Min(_minX, x0); _maxX = Math.Max(_maxX, x1);
            _minY = Math.Min(_minY, y0); _maxY = Math.Max(_maxY, y1);
        }

        _paint.Touch();

        return dirtyMaxX < 0 ? null
            : new Rectangle(dirtyMinX, dirtyMinY, dirtyMaxX - dirtyMinX + 1, dirtyMaxY - dirtyMinY + 1);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private void Composite(int i, float a)
    {
        var layers = _paint.Layers;
        for (int k = 0; k < layers.Length; k++)
        {
            float v = _before[k][i] * (1f - a);
            if (k == _brush) v += 255f * a;
            layers[k][i] = (byte)Math.Clamp(MathF.Round(v), 0f, 255f);
        }
    }

    /// <summary>Finishes the stroke and returns what it changed, for the undo stack. Null if it touched nothing.</summary>
    public ClimateEdit? End()
    {
        if (Touched is not { } rect) return null;
        return ClimateEdit.Capture(_paint, _before, rect);
    }
}

/// <summary>
/// One reversible change to a <see cref="ClimatePaint"/>: the bytes of every layer inside a
/// rectangle, before and after. Small for a small stroke and never larger than the paint itself.
/// </summary>
public sealed class ClimateEdit
{
    public Rectangle Area { get; }
    private readonly byte[][] _before;
    private readonly byte[][] _after;

    private ClimateEdit(Rectangle area, byte[][] before, byte[][] after)
    {
        Area = area;
        _before = before;
        _after = after;
    }

    public static ClimateEdit Capture(ClimatePaint paint, byte[][] beforeLayers, Rectangle area)
    {
        var before = new byte[paint.Layers.Length][];
        var after = new byte[paint.Layers.Length][];
        for (int k = 0; k < paint.Layers.Length; k++)
        {
            before[k] = Cut(beforeLayers[k], paint.Width, area);
            after[k] = Cut(paint.Layers[k], paint.Width, area);
        }

        return new ClimateEdit(area, before, after);
    }

    /// <summary>An edit that clears everything, recorded so that it can be undone.</summary>
    public static ClimateEdit Clear(ClimatePaint paint)
    {
        var area = new Rectangle(0, 0, paint.Width, paint.Height);
        var before = new byte[paint.Layers.Length][];
        var after = new byte[paint.Layers.Length][];
        for (int k = 0; k < paint.Layers.Length; k++)
        {
            before[k] = (byte[])paint.Layers[k].Clone();
            after[k] = new byte[paint.Layers[k].Length];
        }

        return new ClimateEdit(area, before, after);
    }

    public void Undo(ClimatePaint paint) => Paste(paint, _before);
    public void Redo(ClimatePaint paint) => Paste(paint, _after);

    private void Paste(ClimatePaint paint, byte[][] bytes)
    {
        for (int k = 0; k < paint.Layers.Length; k++)
        {
            var layer = paint.Layers[k];
            var src = bytes[k];
            for (int y = 0; y < Area.Height; y++)
                Array.Copy(src, y * Area.Width, layer, (Area.Y + y) * paint.Width + Area.X, Area.Width);
        }

        paint.Touch();
    }

    private static byte[] Cut(byte[] layer, int width, Rectangle area)
    {
        var result = new byte[area.Width * area.Height];
        for (int y = 0; y < area.Height; y++)
            Array.Copy(layer, (area.Y + y) * width + area.X, result, y * area.Width, area.Width);
        return result;
    }
}

/// <summary>Undo and redo over <see cref="ClimateEdit"/>s.</summary>
public sealed class ClimateHistory
{
    private readonly List<ClimateEdit> _edits = [];
    private int _cursor;
    private const int Limit = 64;

    public bool CanUndo => _cursor > 0;
    public bool CanRedo => _cursor < _edits.Count;

    public void Push(ClimateEdit edit)
    {
        if (_cursor < _edits.Count) _edits.RemoveRange(_cursor, _edits.Count - _cursor);
        _edits.Add(edit);
        if (_edits.Count > Limit) _edits.RemoveAt(0);
        _cursor = _edits.Count;
    }

    public bool Undo(ClimatePaint paint)
    {
        if (!CanUndo) return false;
        _edits[--_cursor].Undo(paint);
        return true;
    }

    public bool Redo(ClimatePaint paint)
    {
        if (!CanRedo) return false;
        _edits[_cursor++].Redo(paint);
        return true;
    }

    public void Clear()
    {
        _edits.Clear();
        _cursor = 0;
    }
}
