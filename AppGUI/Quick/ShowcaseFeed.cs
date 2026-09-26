using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Ck3MapGen.Core;
using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The column of discoveries beside the map while a Quick world is made: cultures, faiths,
/// regiments, treasures, as the generator publishes them (<see cref="Showcase"/>).
///
/// Items arrive in bursts — a stage finishes and hands over four cultures at once — so they are
/// queued and revealed one at a time, quicker when the queue is long, so the column reads as a
/// steady stream rather than a dump. Each new card unrolls from the top and glows briefly.
///
/// Painted as one control rather than a stack of child controls: the cards are pictures, not
/// buttons, and one surface is what lets them animate, clip and scroll without flicker. Images are
/// decoded off the UI thread; a card shows without its picture until it lands, and keeps its
/// layout if the file cannot be read.
/// </summary>
internal sealed class ShowcaseFeed : Control
{
    private static readonly Font KindFont = new("Segoe UI Semibold", 7.5f);
    private static readonly Font TitleFont = new("Segoe UI Semibold", 11f);
    private static readonly Font SubtitleFont = new("Segoe UI", 8.5f);
    private static readonly Font BodyFont = new("Segoe UI", 8.5f);
    private static readonly Font ChipFont = new("Segoe UI", 8f);
    private static readonly Font GlyphFont = new(GlyphFamily, 14f);
    private static readonly Font InitialFont = new("Segoe UI Semibold", 13f);
    private static readonly Font EmptyFont = new("Segoe UI", 9f);

    private sealed class Entry(ShowcaseItem item)
    {
        public ShowcaseItem Item { get; } = item;
        public Bitmap? Image { get; set; }
        public float Reveal { get; set; }
        public float Glow { get; set; } = 1f;
    }

    private readonly Queue<ShowcaseItem> _pending = new();
    private readonly List<Entry> _entries = [];          // newest first
    private readonly System.Windows.Forms.Timer _reveal = new() { Interval = 1400 };
    private readonly System.Windows.Forms.Timer _animate = new() { Interval = 16 };
    private int _scroll;
    private int _contentHeight;

    public ShowcaseFeed()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Background;
        _reveal.Tick += (_, _) => RevealNext();
        _animate.Tick += (_, _) => Animate();
    }

    /// <summary>What to say while nothing has arrived yet.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "Peoples, faiths, armies and treasures appear here as the world is made.";

    /// <summary>How many things have been shown or are waiting to be.</summary>
    public int Count => _entries.Count + _pending.Count;

    private int S(int logical) => LaunchUi.S(this, logical);

    public void Clear()
    {
        _reveal.Stop();
        _animate.Stop();
        _pending.Clear();
        foreach (var entry in _entries) entry.Image?.Dispose();
        _entries.Clear();
        _scroll = 0;
        Invalidate();
    }

    public void Offer(ShowcaseItem item)
    {
        _pending.Enqueue(item);
        if (!_reveal.Enabled)
        {
            RevealNext();
            _reveal.Start();
        }
    }

    /// <summary>Shows everything still queued at once — the run is over, nothing is worth waiting for.</summary>
    public void Flush()
    {
        while (_pending.Count > 0) RevealNext();
        _reveal.Stop();
    }

    private void RevealNext()
    {
        if (_pending.Count == 0)
        {
            _reveal.Stop();
            return;
        }

        var entry = new Entry(_pending.Dequeue());
        _entries.Insert(0, entry);
        _scroll = 0;
        LoadImage(entry);

        // Faster when a burst is waiting, never so fast a card cannot be read.
        _reveal.Interval = Math.Clamp(1500 - 160 * _pending.Count, 450, 1500);
        _animate.Start();
        Invalidate();
    }

    private void Animate()
    {
        bool moving = false;
        foreach (var entry in _entries)
        {
            if (entry.Reveal < 1f) { entry.Reveal = Math.Min(1f, entry.Reveal + 0.07f); moving = true; }
            if (entry.Glow > 0f) { entry.Glow = Math.Max(0f, entry.Glow - 0.012f); moving = true; }
        }
        if (!moving) _animate.Stop();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int max = Math.Max(0, _contentHeight - Height);
        _scroll = Math.Clamp(_scroll - e.Delta / 2, 0, max);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        // The wheel goes to the focused control; hovering the column should scroll it.
        if (_contentHeight > Height) Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reveal.Dispose();
            _animate.Dispose();
            foreach (var entry in _entries) entry.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ images

    private void LoadImage(Entry entry)
    {
        var paths = entry.Item.Images;
        if (paths.Count == 0) return;

        Task.Run(() =>
        {
            foreach (string path in paths)
            {
                var bitmap = Frame(Decode(path), entry.Item.ImageFrames, entry.Item.ImageFrame);
                if (bitmap is null) continue;
                if (IsDisposed || !IsHandleCreated) { bitmap.Dispose(); return; }
                BeginInvoke(() =>
                {
                    if (!_entries.Contains(entry)) { bitmap.Dispose(); return; }
                    entry.Image = bitmap;
                    Invalidate();
                });
                return;
            }
        });
    }

    /// <summary>
    /// One frame of a horizontal strip, when the image really is one: frames wide enough to divide
    /// evenly. Anything else is shown whole, which is right for an icon that turned out not to be
    /// a strip after all.
    /// </summary>
    private static Bitmap? Frame(Bitmap? image, int frames, int frame)
    {
        if (image is null || frames <= 1 || image.Width % frames != 0 || image.Width / frames < image.Height / 2)
            return image;

        int w = image.Width / frames;
        var cropped = image.Clone(new Rectangle(Math.Clamp(frame, 0, frames - 1) * w, 0, w, image.Height), image.PixelFormat);
        image.Dispose();
        return cropped;
    }

    private static Bitmap? Decode(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                if (Io.DdsReader.Load(path) is not { } image || image.Width <= 0 || image.Height <= 0) return null;
                var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                var data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < image.Height; y++)
                        Marshal.Copy(image.Bgra, y * image.Width * 4, data.Scan0 + y * data.Stride, image.Width * 4);
                }
                finally { bitmap.UnlockBits(data); }
                return bitmap;
            }

            using var loaded = System.Drawing.Image.FromFile(path);
            return new Bitmap(loaded);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        if (_entries.Count == 0)
        {
            var box = new Rectangle(S(8), S(8), Width - S(16), S(120));
            using (var path = Rounded(box, S(10)))
            using (var pen = new Pen(Theme.Border) { DashStyle = DashStyle.Dash })
                g.DrawPath(pen, path);
            TextRenderer.DrawText(g, EmptyText, EmptyFont, Rectangle.Inflate(box, -S(16), -S(10)), Theme.TextDim,
                TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            _contentHeight = 0;
            return;
        }

        int y = -_scroll;
        int gap = S(10);
        int width = Width - S(2);
        foreach (var entry in _entries)
        {
            int full = Card(null, entry, 0, width);
            int shown = (int)Math.Round(full * Ease(entry.Reveal));
            if (shown <= 0) continue;

            if (y < Height && y + shown > 0)
            {
                var state = g.Save();
                g.SetClip(new Rectangle(0, y, Width, shown));
                Card(g, entry, y, width);
                g.Restore(state);
            }
            y += shown + gap;
        }
        _contentHeight = y + _scroll;

        // A fade at the foot, so a card cut off by the bottom edge reads as "more below".
        if (_contentHeight - _scroll > Height)
        {
            var fade = new Rectangle(0, Height - S(28), Width, S(28));
            using var brush = new LinearGradientBrush(fade, Color.FromArgb(0, BackColor), BackColor, LinearGradientMode.Vertical);
            g.FillRectangle(brush, fade);
        }
    }

    private static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    /// <summary>
    /// Lays one card out, and paints it when given a Graphics. One walk does both, so the height
    /// used for layout is always the height drawn: painting first measures (a walk with no
    /// Graphics), fills the card to that height, then walks again drawing the content over it.
    /// </summary>
    private int Card(Graphics? g, Entry entry, int top, int width)
    {
        var item = entry.Item;
        int x0 = S(1);

        if (g is not null)
        {
            int height = Card(null, entry, top, width);
            using var card = Rounded(new Rectangle(x0, top, width - 1, height), S(10));
            using (var fill = new SolidBrush(Theme.Surface)) g.FillPath(fill, card);
            using (var pen = new Pen(Blend(Theme.Border, Theme.Accent, entry.Glow), entry.Glow > 0.05f ? 1.5f : 1f))
                g.DrawPath(pen, card);
        }

        int pad = S(12);
        int inner = width - 2 * pad;
        int y = top + pad;

        // An illustration runs across the top, cropped to fill.
        if (item.Illustration && entry.Image is { } art)
        {
            int h = Math.Min(S(150), (int)((width - 3) * (double)art.Height / Math.Max(1, art.Width)));
            if (g is not null)
            {
                var dest = new Rectangle(x0 + 1, top + 1, width - 3, h);
                var state = g.Save();
                using var clip = RoundedTop(dest, S(10));
                g.SetClip(clip, CombineMode.Intersect);
                float scale = Math.Max((float)dest.Width / art.Width, (float)dest.Height / art.Height);
                float w = art.Width * scale, hh = art.Height * scale;
                g.DrawImage(art, new RectangleF(dest.X + (dest.Width - w) / 2, dest.Y + (dest.Height - hh) / 2, w, hh));
                g.Restore(state);
            }
            y = top + h + S(10);
        }

        // Otherwise a badge beside the text: the icon, a colour, or a glyph for the kind.
        int badge = S(48);
        bool hasBadge = !item.Illustration;
        int textX = x0 + pad + (hasBadge ? badge + S(12) : 0);
        int textW = x0 + pad + inner - textX;
        int badgeY = y;
        if (hasBadge && g is not null) DrawBadge(g, entry, new Rectangle(x0 + pad, badgeY, badge, badge));

        const TextFormatFlags wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        const TextFormatFlags line = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        int Text(string text, Font font, Color color, int maxLines, TextFormatFlags flags, int x, int w, int yy)
        {
            int lineH = TextRenderer.MeasureText("Ag", font, Size.Empty, TextFormatFlags.NoPadding).Height;
            int h = Math.Min(maxLines * lineH, TextRenderer.MeasureText(text, font, new Size(w, 0), wrap).Height);
            if (g is not null) TextRenderer.DrawText(g, text, font, new Rectangle(x, yy, w, h), color, flags);
            return h;
        }

        y += Text(item.Kind.ToUpperInvariant(), KindFont, Theme.Accent, 1, line, textX, textW, y) + S(2);
        y += Text(item.Title, TitleFont, Theme.Text, 2, wrap, textX, textW, y) + S(1);
        if (!string.IsNullOrEmpty(item.Subtitle))
            y += Text(item.Subtitle, SubtitleFont, Theme.TextDim, 2, wrap, textX, textW, y) + S(4);
        if (hasBadge) y = Math.Max(y, badgeY + badge + S(8));

        if (!string.IsNullOrEmpty(item.Body))
            y += Text(item.Body, BodyFont, Theme.Text, 4, wrap, x0 + pad, inner, y) + S(8);

        // Chips, wrapping onto at most two rows.
        if (item.Chips.Count > 0)
        {
            int cx = x0 + pad, rows = 1;
            int chipH = TextRenderer.MeasureText("Ag", ChipFont, Size.Empty, TextFormatFlags.NoPadding).Height + S(6);
            foreach (string chip in item.Chips)
            {
                int cw = Math.Min(TextRenderer.MeasureText(chip, ChipFont, Size.Empty, TextFormatFlags.NoPadding).Width + S(14), inner);
                if (cx + cw > x0 + pad + inner)
                {
                    if (rows == 2) break;
                    rows++;
                    cx = x0 + pad;
                    y += chipH + S(5);
                }
                if (g is not null)
                {
                    var r = new Rectangle(cx, y, cw, chipH);
                    using var path = Rounded(r, S(6));
                    using var fill = new SolidBrush(Theme.Background);
                    g.FillPath(fill, path);
                    TextRenderer.DrawText(g, chip, ChipFont, r, Theme.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                cx += cw + S(5);
            }
            y += chipH + S(4);
        }

        return y + pad - S(4) - top;
    }

    private void DrawBadge(Graphics g, Entry entry, Rectangle box)
    {
        var item = entry.Item;
        if (entry.Image is { } icon)
        {
            // Icons are square art on transparency; drawn whole, inside a soft tile.
            using (var tile = Rounded(box, S(8)))
            using (var fill = new SolidBrush(Theme.Background))
                g.FillPath(fill, tile);
            float scale = Math.Min((float)(box.Width - S(4)) / icon.Width, (float)(box.Height - S(4)) / icon.Height);
            float w = icon.Width * scale, h = icon.Height * scale;
            g.DrawImage(icon, new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h));
            return;
        }

        if (item.Color is { } c)
        {
            var colour = Color.FromArgb(c.R, c.G, c.B);
            using var fill = new SolidBrush(colour);
            g.FillEllipse(fill, box);
            bool dark = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 150;
            string initial = item.Title.Length > 0 ? item.Title[..1].ToUpperInvariant() : "?";
            TextRenderer.DrawText(g, initial, InitialFont, box, dark ? Color.White : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }

        string glyph = item.Kind switch
        {
            "Calendar" => "",
            "Waters" => "",
            "Great powers" => "",
            "Wonder" => "",
            _ => "",
        };
        using (var fill = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(fill, box);
        TextRenderer.DrawText(g, glyph, GlyphFont, box, Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static GraphicsPath RoundedTop(Rectangle r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        path.CloseFigure();
        return path;
    }

    private static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}
