using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;
using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The run view's map played as a show rather than swapped as a slideshow. Three things go on it:
/// <list type="bullet">
/// <item><b>Pictures</b> take their turn: each waits until the one before has been seen, then fades
/// in over it.</item>
/// <item><b>The province partition</b> plays out as it happened (<see cref="PartitionSketch"/>): the
/// seeds scatter, the provinces grow from them in the order the partition reached each pixel,
/// relaxation glides every seed towards the middle of what it grew while the borders give way, and
/// the tidy-up settles the rest and turns the impassable mountains to stone.</item>
/// <item><b>Pins</b> for discoveries that have a place — a people at its heartland, a faith's
/// medallion, a wonder's glyph — popped on as each card is shown, in the badges the World
/// workspace's map modes draw. They belong to the picture they landed on and leave with it.</item>
/// </list>
/// Everything waits in one queue, so the order the run made things in is the order they are shown,
/// and the queue plays faster when it falls behind. The map control only paints; this drives it
/// through <see cref="MapPreview.Overlay"/>.
///
/// The partition is composed on a canvas of its own at about half the preview's width, a few
/// milliseconds a frame, and handed to the control as one bitmap; the settled provinces replace it
/// at full preview size once the show is over.
/// </summary>
internal sealed class LiveMap : IDisposable
{
    private const double PictureSeconds = 1.2, FadeSeconds = 0.45;
    private const double PinSeconds = 0.35, PinPop = 0.5, PinRipple = 1.3, PinLabel = 3.4, PinLeave = 0.45;

    private static readonly Font LabelFont = new("Segoe UI Semibold", 8.5f);
    private static readonly Font InitialFont = new("Segoe UI Semibold", 10f);

    private readonly MapPreview _view;
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 15 };
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly Queue<Beat> _queue = new();
    private Beat? _beat;
    private double _beatTime, _last;
    private bool _finishing;

    // The partition, while it is on screen: the canvas it is composed on, the sketch it is heading
    // to, the one it is leaving, and the canvas pixels that differ between those two.
    private Canvas? _canvas;
    private Layer? _layer, _from;
    private int[]? _changed;

    // The picture a new one is fading in over, already at the size it was on screen.
    private Bitmap? _outgoing;
    private float _outgoingAlpha;

    private readonly List<Marker> _pins = [];
    private RectangleF _bounds;

    public LiveMap(MapPreview view)
    {
        _view = view;
        _view.Overlay = Paint;
        _clock.Tick += (_, _) => Tick();
    }

    private int S(int logical) => LaunchUi.S(_view, logical);

    private double Now => _watch.Elapsed.TotalSeconds;

    /// <summary>Faster as the queue backs up, so the map never falls far behind the run.</summary>
    private double Pace => Math.Min(4, 1 + 0.45 * _queue.Count);

    // ================================================================ what the run offers

    /// <summary>A new run: everything waiting is dropped, and <paramref name="picture"/> shown at once.</summary>
    public void Reset(Bitmap? picture, string chip)
    {
        _clock.Stop();
        _beat = null;
        DiscardQueue();
        DropCanvas();
        DropOutgoing();
        ClearPins();
        _view.Image = picture;
        _view.Chip = chip;
    }

    /// <summary>A picture the run drew, to fade in when its turn comes. Takes ownership.</summary>
    public void Show(Bitmap picture, string chip) => Enqueue(new Beat
    {
        Seconds = PictureSeconds,
        Begin = () => Replace(picture, chip),
        Frame = t => _outgoingAlpha = (float)(1 - Smooth(Math.Min(1, t * PictureSeconds / FadeSeconds))),
        End = DropOutgoing,
        Discard = picture.Dispose,
    });

    public void Sketch(PartitionSketch sketch)
    {
        switch (sketch.Step)
        {
            case PartitionStep.Seeded:
                Enqueue(new Beat { Seconds = 1.2, Begin = () => BeginSketch(sketch, "Sowing the provinces"), Frame = SowFrame });
                break;

            case PartitionStep.Grown when sketch.Rgb is not null:
                Enqueue(new Beat { Seconds = 3.2, Begin = () => BeginSketch(sketch, "Growing the provinces"), Frame = GrowFrame });
                break;

            case PartitionStep.Relaxed when sketch.Rgb is not null:
                string round = sketch.Passes > 1 ? $" · round {sketch.Pass} of {sketch.Passes}" : "";
                Enqueue(new Beat
                {
                    Seconds = 1.6,
                    Begin = () => BeginSketch(sketch, "Settling the borders" + round),
                    Frame = t => ShiftFrame(t, settling: false),
                });
                break;

            case PartitionStep.Settled when sketch.Rgb is not null:
                Enqueue(new Beat
                {
                    Seconds = 1.2,
                    Begin = () => BeginSketch(sketch, "Marking the mountains"),
                    Frame = t => ShiftFrame(t, settling: true),
                    End = EndPartition,
                });
                break;
        }
    }

    /// <summary>A discovery just shown in the column; it gets a pin when it has a place.</summary>
    public void Pin(ShowcaseItem item)
    {
        if (item.MapAt is null) return;
        Enqueue(new Beat { Seconds = PinSeconds, Begin = () => AddPin(item) });
    }

    /// <summary>
    /// The run is over: whatever the queue still held is played out at once, so the map is left
    /// where the show would have got to, as a plain picture with nothing animating over it.
    /// </summary>
    public void Finish()
    {
        _finishing = true;
        try
        {
            while (true)
            {
                if (_beat is null)
                {
                    if (_queue.Count == 0) break;
                    Next();
                }
                var beat = _beat!;
                _beat = null;
                beat.Frame?.Invoke(1);
                beat.End?.Invoke();
            }
            CommitCanvas();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (live map: {ex.GetType().Name}: {ex.Message})");
            _beat = null;
            DiscardQueue();
            DropCanvas();
        }
        finally
        {
            _finishing = false;
        }

        DropOutgoing();
        ClearPins();
        _clock.Stop();
        _view.Invalidate();
    }

    public void Dispose()
    {
        _clock.Dispose();
        DiscardQueue();
        DropCanvas();
        DropOutgoing();
        ClearPins();
        _view.Overlay = null;
    }

    // ================================================================ the queue

    private sealed class Beat
    {
        public required double Seconds { get; init; }
        public Action? Begin { get; init; }

        /// <summary>Called each frame with how far through the beat it is, 0 to 1.</summary>
        public Action<double>? Frame { get; init; }
        public Action? End { get; init; }

        /// <summary>Frees what a beat that never began was holding.</summary>
        public Action? Discard { get; init; }
    }

    private void Enqueue(Beat beat)
    {
        _queue.Enqueue(beat);
        Wake();
    }

    private void Wake()
    {
        if (_clock.Enabled) return;
        _last = Now;
        _clock.Start();
    }

    private void Next()
    {
        if (_queue.Count == 0) return;
        _beat = _queue.Dequeue();
        _beatTime = 0;
        _beat.Begin?.Invoke();
    }

    private void Tick()
    {
        double now = Now;

        // A stalled UI thread must not skip a whole beat when it comes back.
        double dt = Math.Clamp(now - _last, 0, 0.1);
        _last = now;

        try
        {
            if (_beat is null) Next();
            while (_beat is { } beat)
            {
                _beatTime += dt * Pace;
                dt = 0;
                double t = Math.Min(1, _beatTime / beat.Seconds);
                beat.Frame?.Invoke(t);
                if (t < 1) break;
                beat.End?.Invoke();
                _beat = null;
                Next();
            }
        }
        catch (Exception ex)
        {
            // A show that went wrong costs the watcher the show, and nothing else.
            Console.WriteLine($"  (live map: {ex.GetType().Name}: {ex.Message})");
            _beat = null;
            DropCanvas();
        }

        _pins.RemoveAll(pin =>
        {
            bool gone = !double.IsNaN(pin.Leaving) && now - pin.Leaving > PinLeave;
            if (gone) pin.Dispose();
            return gone;
        });

        _view.Invalidate();
        if (_beat is null && _queue.Count == 0 && !PinsMoving(now)) _clock.Stop();
    }

    private void DiscardQueue()
    {
        while (_queue.Count > 0) _queue.Dequeue().Discard?.Invoke();
    }

    // ================================================================ pictures

    private void Replace(Bitmap picture, string chip)
    {
        CommitCanvas();
        LeavePins();
        DropOutgoing();
        if (!_finishing && _view.Image is { } old) _outgoing = Snapshot(old);
        _view.Image = picture;
        _view.Chip = chip;
    }

    /// <summary>The picture as it stood on screen, kept to fade out over its replacement.</summary>
    private Bitmap Snapshot(Bitmap picture)
    {
        var size = _bounds.Width >= 1 && _bounds.Height >= 1 ? Size.Round(_bounds.Size) : picture.Size;
        var copy = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(copy);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var wrap = new ImageAttributes();
        wrap.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(picture, new Rectangle(Point.Empty, size), 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel, wrap);
        return copy;
    }

    private void DropOutgoing()
    {
        _outgoing?.Dispose();
        _outgoing = null;
        _outgoingAlpha = 0;
    }

    // ================================================================ the partition

    /// <summary>A sketch at canvas size: what each canvas pixel shows, and where the seeds are.</summary>
    private sealed class Layer
    {
        public required PartitionSketch Sketch { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }

        /// <summary>Opaque colour per canvas pixel; null on the seeds-only sketch.</summary>
        public int[]? Colour { get; init; }
        public ushort[]? Arrival { get; init; }

        /// <summary>Seed positions in canvas pixels, by slot; NaN for a seed that has gone.</summary>
        public required float[] X { get; init; }
        public required float[] Y { get; init; }

        public static Layer From(PartitionSketch s)
        {
            // About half the preview's width: plenty for a picture a thousand-odd pixels wide on
            // screen, and a quarter of the pixels to compose each frame.
            int k = s.Width > 1400 ? 2 : 1;
            int w = Math.Max(1, s.Width / k), h = Math.Max(1, s.Height / k);

            int[]? colour = null;
            ushort[]? arrival = null;
            if (s.Rgb is { } rgb)
            {
                colour = new int[w * h];
                var sourceArrival = s.Arrival;
                if (sourceArrival is not null) arrival = new ushort[w * h];
                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int src = y * k * s.Width + x * k, o = src * 3;
                        colour[y * w + x] = Opaque(rgb[o], rgb[o + 1], rgb[o + 2]);
                        if (arrival is not null) arrival[y * w + x] = sourceArrival![src];
                    }
                });
            }

            var xs = new float[s.SeedX.Length];
            var ys = new float[s.SeedY.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] = s.SeedX[i] / k;
                ys[i] = s.SeedY[i] / k;
            }

            return new Layer { Sketch = s, Width = w, Height = h, Colour = colour, Arrival = arrival, X = xs, Y = ys };
        }
    }

    private sealed class Canvas : IDisposable
    {
        public readonly int Width, Height;

        /// <summary>Premultiplied ARGB, row by row.</summary>
        public readonly int[] Pixels;
        public readonly Bitmap Bitmap;

        public Canvas(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new int[width * height];
            Bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        }

        public void Commit()
        {
            var data = Bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                if (data.Stride == Width * 4) Marshal.Copy(Pixels, 0, data.Scan0, Pixels.Length);
                else
                    for (int y = 0; y < Height; y++)
                        Marshal.Copy(Pixels, y * Width, data.Scan0 + y * data.Stride, Width);
            }
            finally
            {
                Bitmap.UnlockBits(data);
            }
        }

        public void Dispose() => Bitmap.Dispose();
    }

    private void BeginSketch(PartitionSketch sketch, string chip)
    {
        _from = _layer;
        _layer = Layer.From(sketch);
        _changed = Changed(_from?.Colour, _layer.Colour);

        if (_canvas is null || _canvas.Width != _layer.Width || _canvas.Height != _layer.Height)
        {
            _canvas?.Dispose();
            _canvas = new Canvas(_layer.Width, _layer.Height);
        }

        DropOutgoing();
        _view.Chip = chip;
    }

    /// <summary>The canvas pixels whose colour differs between two sketches: where the borders moved.</summary>
    private static int[]? Changed(int[]? from, int[]? to)
    {
        if (from is null || to is null || from.Length != to.Length) return null;
        var changed = new List<int>();
        for (int i = 0; i < to.Length; i++)
            if (from[i] != to[i]) changed.Add(i);
        return [.. changed];
    }

    /// <summary>The seeds scatter over the picture, each popping in at its own moment.</summary>
    private void SowFrame(double t)
    {
        var canvas = _canvas!;
        var layer = _layer!;
        Array.Clear(canvas.Pixels);

        var kind = layer.Sketch.SeedKind;
        for (int i = 0; i < layer.X.Length; i++)
            Dot(canvas, layer.X[i], layer.Y[i], kind[i], (float)Math.Clamp((t - 0.7 * Scatter(i)) / 0.3, 0, 1));
        canvas.Commit();
    }

    /// <summary>
    /// The provinces grow from their seeds, each pixel appearing when the partition reached it, a
    /// bright band running along the growing edge.
    /// </summary>
    private void GrowFrame(double t)
    {
        var canvas = _canvas!;
        var layer = _layer!;
        var pixels = canvas.Pixels;
        var colour = layer.Colour!;

        if (layer.Arrival is { } arrival)
        {
            const int Band = 1800;
            int front = t >= 1 ? int.MaxValue : (int)(t * ushort.MaxValue);
            Parallel.For(0, canvas.Height, y =>
            {
                int row = y * canvas.Width;
                for (int i = row; i < row + canvas.Width; i++)
                {
                    int behind = front - arrival[i];
                    pixels[i] = behind < 0 ? 0
                        : behind >= Band ? colour[i]
                        : Lighten(colour[i], 0.6f * (1 - behind / (float)Band));
                }
            });
        }
        else
        {
            // No distances came with it: the provinces simply fade in.
            float alpha = (float)t;
            Parallel.For(0, pixels.Length / canvas.Width, y =>
            {
                for (int i = y * canvas.Width; i < (y + 1) * canvas.Width; i++) pixels[i] = Fade(colour[i], alpha);
            });
        }

        var kind = layer.Sketch.SeedKind;
        for (int i = 0; i < layer.X.Length; i++) Dot(canvas, layer.X[i], layer.Y[i], kind[i], 1);
        canvas.Commit();
    }

    /// <summary>
    /// From one sketch to the next: the borders that moved blend across, and the seeds glide from
    /// where they were to where they are — or, as the partition settles, fade away.
    /// </summary>
    private void ShiftFrame(double t, bool settling)
    {
        var canvas = _canvas!;
        var to = _layer!;
        var from = _from;
        float e = (float)Smooth(t);

        Array.Copy(to.Colour!, canvas.Pixels, canvas.Pixels.Length);
        if (from?.Colour is { } old && _changed is { } changed)
            foreach (int i in changed) canvas.Pixels[i] = Mix(old[i], to.Colour![i], e);
        else if (from?.Colour is null)
            for (int i = 0; i < canvas.Pixels.Length; i++) canvas.Pixels[i] = Fade(to.Colour![i], e);

        var kind = to.Sketch.SeedKind;
        float alpha = settling ? 1 - e : 1;
        for (int i = 0; i < to.X.Length; i++)
        {
            float x = to.X[i], y = to.Y[i];
            if (from is not null && i < from.X.Length && float.IsFinite(from.X[i]))
            {
                if (!float.IsFinite(x)) (x, y) = (from.X[i], from.Y[i]);
                else if (!settling) (x, y) = (from.X[i] + (x - from.X[i]) * e, from.Y[i] + (y - from.Y[i]) * e);
            }
            Dot(canvas, x, y, kind[i], alpha);
        }
        canvas.Commit();
    }

    /// <summary>The show is over: the settled provinces become the picture, at full preview size.</summary>
    private void EndPartition()
    {
        var sketch = _layer!.Sketch;
        _view.Image = PreviewRenderer.ToBitmap(new PreviewRenderer.Image(sketch.Rgb!, sketch.Width, sketch.Height));
        DropCanvas();
    }

    /// <summary>A partition still on screen when something else takes over becomes the picture as it stands.</summary>
    private void CommitCanvas()
    {
        if (_canvas is not { } canvas) return;

        var flat = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(flat))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (_view.Image is { } under) g.DrawImage(under, new Rectangle(0, 0, canvas.Width, canvas.Height));
            g.DrawImage(canvas.Bitmap, new Rectangle(0, 0, canvas.Width, canvas.Height));
        }
        _view.Image = flat;
        DropCanvas();
    }

    private void DropCanvas()
    {
        _canvas?.Dispose();
        _canvas = null;
        _layer = _from = null;
        _changed = null;
    }

    // ================================================================ pins

    private sealed class Marker : IDisposable
    {
        public required ShowcaseItem Item { get; init; }
        public required PointF At { get; init; }
        public required double Born { get; init; }
        public double Leaving { get; set; } = double.NaN;

        /// <summary>When its name stops showing: after a while, or as soon as the next pin lands.</summary>
        public required double LabelEnd { get; set; }
        public Bitmap? Glyph { get; init; }
        public Bitmap? Icon { get; set; }

        /// <summary>A dark icon — obsidian, iron — sits on parchment, as the faith map's badges do.</summary>
        public bool DarkIcon { get; set; }

        /// <summary>A race's badge, ringed in its people's colour.</summary>
        public bool Badge => Item.PinImages.Count > 0;

        public void Dispose()
        {
            Glyph?.Dispose();
            Icon?.Dispose();
        }
    }

    private void AddPin(ShowcaseItem item)
    {
        if (_finishing || item.MapAt is not { } at) return;

        // Only the newest pin is named: two names landing close together would be read as neither.
        double now = Now;
        foreach (var older in _pins) older.LabelEnd = Math.Min(older.LabelEnd, now + 0.3);

        var placed = _pins.Where(p => double.IsNaN(p.Leaving)).Select(p => p.At).ToList();
        var pin = new Marker
        {
            Item = item,
            At = Nudge(new PointF(at.X, at.Y), placed),
            Born = now,
            LabelEnd = now + PinLabel,
            Glyph = Enum.TryParse<WonderArchetype>(item.PinGlyph, out var glyph) ? GlyphBitmap(glyph) : null,
        };
        _pins.Add(pin);
        if (pin.Glyph is null) LoadIcon(pin);
    }

    private void LoadIcon(Marker pin)
    {
        var item = pin.Item;
        bool own = item.PinImages.Count > 0;
        var paths = own ? item.PinImages : item.Illustration ? Array.Empty<string>() : item.Images;
        if (paths.Count == 0) return;
        int frames = own ? 1 : item.ImageFrames, frame = own ? 0 : item.ImageFrame;

        Task.Run(() =>
        {
            foreach (string path in paths)
            {
                if (ShowcaseFeed.Frame(ShowcaseFeed.Decode(path), frames, frame) is not { } icon) continue;
                bool dark = MeanLuminance(icon) < 85;
                try
                {
                    _view.BeginInvoke(() =>
                    {
                        if (!_pins.Contains(pin)) { icon.Dispose(); return; }
                        pin.Icon = icon;
                        pin.DarkIcon = dark;
                        _view.Invalidate();
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
                {
                    icon.Dispose();
                }
                return;
            }
        });
    }

    private void LeavePins()
    {
        double now = Now;
        foreach (var pin in _pins)
            if (double.IsNaN(pin.Leaving)) pin.Leaving = now;
    }

    private void ClearPins()
    {
        foreach (var pin in _pins) pin.Dispose();
        _pins.Clear();
    }

    private bool PinsMoving(double now)
        => _pins.Any(p => !double.IsNaN(p.Leaving) || now - p.Born < PinRipple || now < p.LabelEnd);

    /// <summary>
    /// The nearest point to <paramref name="want"/> that keeps clear of every pin already down, on a
    /// widening ring of candidates, in fractions of the map with its height counted at half weight
    /// (the map is twice as wide as it is tall). Overlaps rather than wander far from its place.
    /// </summary>
    private static PointF Nudge(PointF want, List<PointF> placed)
    {
        const float Gap = 0.034f;
        bool Clear(PointF p) => placed.All(q =>
        {
            float dx = p.X - q.X, dy = (p.Y - q.Y) * 0.5f;
            return dx * dx + dy * dy >= Gap * Gap;
        });

        if (Clear(want)) return want;
        for (int ring = 1; ring <= 4; ring++)
        {
            float r = ring * Gap * 0.6f;
            for (int k = 0; k < 12; k++)
            {
                double a = k * Math.PI / 6;
                var p = new PointF(want.X + r * (float)Math.Cos(a), want.Y + 2 * r * (float)Math.Sin(a));
                if (Clear(p)) return p;
            }
        }
        return want;
    }

    // ================================================================ painting

    private void Paint(Graphics g, RectangleF bounds)
    {
        _bounds = bounds;
        var dest = Rectangle.Round(bounds);

        if (_canvas is { } canvas)
        {
            var (mode, offset) = (g.InterpolationMode, g.PixelOffsetMode);
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using var wrap = new ImageAttributes();
            wrap.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(canvas.Bitmap, dest, 0, 0, canvas.Width, canvas.Height, GraphicsUnit.Pixel, wrap);
            (g.InterpolationMode, g.PixelOffsetMode) = (mode, offset);
        }

        if (_outgoing is { } old && _outgoingAlpha > 0.004f)
            DrawAlpha(g, old, dest, _outgoingAlpha);

        if (_pins.Count > 0) DrawPins(g, bounds, Now);
    }

    private static void DrawAlpha(Graphics g, Bitmap image, RectangleF dest, float alpha)
    {
        if (alpha >= 0.996f)
        {
            g.DrawImage(image, dest);
            return;
        }
        using var fade = new ImageAttributes();
        fade.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
        g.DrawImage(image, Rectangle.Round(dest), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, fade);
    }

    private void DrawPins(Graphics g, RectangleF bounds, double now)
    {
        var (smoothing, hint, mode) = (g.SmoothingMode, g.TextRenderingHint, g.InterpolationMode);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        foreach (var pin in _pins) DrawPin(g, bounds, pin, now);
        (g.SmoothingMode, g.TextRenderingHint, g.InterpolationMode) = (smoothing, hint, mode);
    }

    private void DrawPin(Graphics g, RectangleF bounds, Marker pin, double now)
    {
        double age = now - pin.Born;
        bool leaving = !double.IsNaN(pin.Leaving);
        float stay = leaving ? 1f - (float)Math.Clamp((now - pin.Leaving) / PinLeave, 0, 1) : 1f;
        if (stay <= 0) return;

        float pop = (float)EaseOutBack(Math.Clamp(age / PinPop, 0, 1));
        float d = S(32) * pop * (0.7f + 0.3f * stay);
        if (d < 1) return;

        var item = pin.Item;
        var centre = new PointF(bounds.X + pin.At.X * bounds.Width, bounds.Y + pin.At.Y * bounds.Height);
        var tint = item.Color is { } c ? Color.FromArgb(c.R, c.G, c.B) : Color.FromArgb(206, 200, 186);
        int A(float a) => (int)Math.Clamp(a * 255 * stay, 0, 255);

        // A ring spreading from where it landed: something happened here.
        if (age < PinRipple && !leaving)
        {
            float k = (float)(age / PinRipple);
            float r = S(14) + k * S(44);
            using var ring = new Pen(Color.FromArgb(A(0.85f * (1 - k)), Lift(tint)), Math.Max(1f, S(2) * (1 - k * 0.5f)));
            g.DrawEllipse(ring, centre.X - r, centre.Y - r, 2 * r, 2 * r);
        }

        var disc = new RectangleF(centre.X - d / 2, centre.Y - d / 2, d, d);
        using (var shadow = new SolidBrush(Color.FromArgb(A(0.35f), 0, 0, 0)))
            g.FillEllipse(shadow, disc.X, disc.Y + S(2), d, d);

        // The disc the map modes seat every badge on: dark, parchment under a dark icon, or the
        // thing's own colour while it has no picture.
        bool art = pin.Glyph is not null || pin.Icon is not null;
        var fill = pin.Glyph is not null || (pin.Icon is not null && !pin.DarkIcon) ? Color.FromArgb(20, 22, 26)
            : pin.Icon is not null ? Color.FromArgb(188, 180, 160)
            : tint;
        using (var brush = new SolidBrush(Color.FromArgb(A(0.92f), fill))) g.FillEllipse(brush, disc);

        float rimWidth = pin.Badge ? S(3) : Math.Max(1.5f, S(2) * 0.75f);
        var rimColour = pin.Badge && item.Color is not null ? tint : Color.FromArgb(206, 200, 186);
        using (var rim = new Pen(Color.FromArgb(A(0.95f), rimColour), rimWidth))
            g.DrawEllipse(rim, RectangleF.Inflate(disc, -rimWidth / 2, -rimWidth / 2));

        if (art)
        {
            // A card's icon — a faith's medallion — carries its own rim and fills the disc, as on
            // the Faiths map mode; a glyph or a race badge sits inside it.
            float size = d * (pin.Glyph is not null ? 0.74f : pin.Badge ? 0.66f : 1.08f);
            DrawAlpha(g, (pin.Glyph ?? pin.Icon)!, new RectangleF(centre.X - size / 2, centre.Y - size / 2, size, size), stay);
        }
        else
        {
            // Until its picture lands, or with none: its initial, as the discoveries' badges show it.
            string initial = item.Title.Length > 0 ? item.Title[..1].ToUpperInvariant() : "?";
            bool dark = (fill.R * 299 + fill.G * 587 + fill.B * 114) / 1000 < 150;
            using var ink = new SolidBrush(Color.FromArgb(A(1), dark ? Color.White : Color.FromArgb(30, 30, 34)));
            using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(initial, InitialFont, ink, disc, centred);
        }

        // Its name, for a moment, under it.
        if (now < pin.LabelEnd && !leaving)
        {
            float a = (float)(Math.Clamp(age / 0.25, 0, 1) * Math.Clamp((pin.LabelEnd - now) / 0.5, 0, 1));
            DrawLabel(g, item.Title, centre.X, disc.Bottom + S(5), bounds, a);
        }
    }

    private void DrawLabel(Graphics g, string text, float cx, float top, RectangleF bounds, float alpha)
    {
        var size = g.MeasureString(text, LabelFont);
        var box = new RectangleF(cx - size.Width / 2 - S(7), top, size.Width + S(14), size.Height + S(4));
        box.X = Math.Clamp(box.X, bounds.X + S(4), Math.Max(bounds.X + S(4), bounds.Right - box.Width - S(4)));
        box.Y = Math.Min(box.Y, bounds.Bottom - box.Height - S(4));

        using var path = Rounded(box, box.Height / 2);
        using (var back = new SolidBrush(Color.FromArgb((int)(215 * alpha), Theme.Surface))) g.FillPath(back, path);
        using var ink = new SolidBrush(Color.FromArgb((int)(255 * alpha), Theme.Text));
        using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, LabelFont, ink, box, centred);
    }

    // ================================================================ pixels

    /// <summary>A seed: a pale dot with a soft dark halo, so it reads on bare relief and on bright provinces alike.</summary>
    private static void Dot(Canvas canvas, float x, float y, SketchSeed kind, float alpha)
    {
        if (alpha <= 0.01f || !float.IsFinite(x) || !float.IsFinite(y)) return;

        var (r, g, b) = kind switch
        {
            SketchSeed.River => (150, 228, 255),
            SketchSeed.Sea => (186, 208, 236),
            SketchSeed.Impassable => (214, 208, 196),
            _ => (255, 250, 236),
        };
        float radius = kind == SketchSeed.Sea ? 1.1f : 1.45f, halo = radius + 1f;

        int x0 = Math.Max(0, (int)MathF.Floor(x - halo)), x1 = Math.Min(canvas.Width - 1, (int)MathF.Ceiling(x + halo));
        int y0 = Math.Max(0, (int)MathF.Floor(y - halo)), y1 = Math.Min(canvas.Height - 1, (int)MathF.Ceiling(y + halo));
        var pixels = canvas.Pixels;
        for (int py = y0; py <= y1; py++)
        {
            for (int px = x0; px <= x1; px++)
            {
                float dx = px + 0.5f - x, dy = py + 0.5f - y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float core = Math.Clamp(radius + 0.5f - dist, 0, 1);
                float ring = Math.Clamp(halo + 0.5f - dist, 0, 1) - core;
                int i = py * canvas.Width + px;
                if (ring > 0) pixels[i] = Over(pixels[i], 20, 22, 26, ring * 0.45f * alpha);
                if (core > 0) pixels[i] = Over(pixels[i], r, g, b, core * alpha);
            }
        }
    }

    private static int Opaque(int r, int g, int b) => unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;

    /// <summary>An opaque colour moved toward white.</summary>
    private static int Lighten(int argb, float k)
    {
        int r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255;
        return Opaque(r + (int)((255 - r) * k), g + (int)((255 - g) * k), b + (int)((255 - b) * k));
    }

    /// <summary>Between two opaque colours.</summary>
    private static int Mix(int a, int b, float t)
    {
        int ar = (a >> 16) & 255, ag = (a >> 8) & 255, ab = a & 255;
        int br = (b >> 16) & 255, bg = (b >> 8) & 255, bb = b & 255;
        return Opaque(ar + (int)((br - ar) * t), ag + (int)((bg - ag) * t), ab + (int)((bb - ab) * t));
    }

    /// <summary>An opaque colour made partly transparent, premultiplied.</summary>
    private static int Fade(int argb, float alpha)
    {
        int a = (int)(Math.Clamp(alpha, 0, 1) * 255 + 0.5f);
        int r = ((argb >> 16) & 255) * a / 255, g = ((argb >> 8) & 255) * a / 255, b = (argb & 255) * a / 255;
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    /// <summary>A straight colour at <paramref name="alpha"/> laid over a premultiplied pixel.</summary>
    private static int Over(int dst, int r, int g, int b, float alpha)
    {
        float keep = 1 - alpha;
        int da = (dst >> 24) & 255, dr = (dst >> 16) & 255, dg = (dst >> 8) & 255, db = dst & 255;
        int oa = Math.Min(255, (int)(255 * alpha + da * keep + 0.5f));
        int or = Math.Min(255, (int)(r * alpha + dr * keep + 0.5f));
        int og = Math.Min(255, (int)(g * alpha + dg * keep + 0.5f));
        int ob = Math.Min(255, (int)(b * alpha + db * keep + 0.5f));
        return (oa << 24) | (or << 16) | (og << 8) | ob;
    }

    /// <summary>A fixed scatter in [0, 1) per seed slot, so the seeds pop in unevenly but the same way every time.</summary>
    private static double Scatter(int i) => ((uint)i * 2654435761u >> 8) / 16777216.0;

    /// <summary>A wonder's glyph as the map modes draw it, antialiased, pale on transparency.</summary>
    private static Bitmap GlyphBitmap(WonderArchetype archetype)
    {
        const int Size = 64, Sub = 3;
        const double Half = Size / 2.0;
        var pixels = new int[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < Sub; sy++)
                    for (int sx = 0; sx < Sub; sx++)
                        if (PreviewRenderer.InWonderGlyph(archetype,
                                (x + (sx + 0.5) / Sub) / Half - 1, (y + (sy + 0.5) / Sub) / Half - 1))
                            hits++;
                if (hits > 0) pixels[y * Size + x] = Over(0, 240, 236, 222, hits / (float)(Sub * Sub));
            }
        }

        var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, Size, Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (int y = 0; y < Size; y++) Marshal.Copy(pixels, y * Size, data.Scan0 + y * data.Stride, Size);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    /// <summary>Mean luminance, 0-255, of an icon's solid pixels, its shadow and soft edges ignored.</summary>
    private static double MeanLuminance(Bitmap icon)
    {
        var data = icon.LockBits(new Rectangle(0, 0, icon.Width, icon.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[icon.Width * 4];
            double sum = 0;
            int n = 0;
            for (int y = 0; y < icon.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int i = 0; i < row.Length; i += 4)
                {
                    if (row[i + 3] < 250) continue;
                    sum += 0.0722 * row[i] + 0.7152 * row[i + 1] + 0.2126 * row[i + 2];
                    n++;
                }
            }
            return n == 0 ? 255 : sum / n;
        }
        finally
        {
            icon.UnlockBits(data);
        }
    }

    private static Color Lift(Color c) => Color.FromArgb(c.R + (255 - c.R) / 3, c.G + (255 - c.G) / 3, c.B + (255 - c.B) / 3);

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static double EaseOutBack(double t)
    {
        const double C1 = 1.70158, C3 = C1 + 1;
        double u = t - 1;
        return 1 + C3 * u * u * u + C1 * u * u;
    }
}
