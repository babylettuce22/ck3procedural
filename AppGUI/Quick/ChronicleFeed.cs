using System.ComponentModel;
using System.Drawing.Drawing2D;
using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The chronicle beside the map while a Quick world's history runs: one line per headline, newest
/// at the top, on a card. The year stands in a gutter on the first line of each year only, so a
/// busy year reads as one block; a swatch in the realm's map colour ties each line to the map.
/// A new line glows briefly as it arrives. Hovering a line with a place says so
/// (<see cref="HoverChanged"/>), so the map can light up where it happened.
///
/// Painted as one control, like <see cref="ShowcaseFeed"/>: lines are text, not buttons, and one
/// surface scrolls and animates without flicker. Line heights are measured once per width.
/// </summary>
internal sealed class ChronicleFeed : Control
{
    /// <summary>
    /// Lines kept, highlights or not; older ones fall off the bottom. The written chronicle keeps
    /// its own record. Enough that the highlights alone still reach well back.
    /// </summary>
    private const int Limit = 1000;

    private static readonly Font YearFont = new("Segoe UI Semibold", 9f);
    private static readonly Font LineFont = new("Segoe UI", 9f);
    private static readonly Font EmptyFont = new("Segoe UI", 9f);

    private sealed class Entry(ChronicleLine line)
    {
        public ChronicleLine Line { get; } = line;
        public float Glow { get; set; } = 1f;
        public int MeasuredFor { get; set; } = -1;
        public int Height { get; set; }

        /// <summary>Where the entry was last painted, for finding the one under the mouse.</summary>
        public int Top { get; set; } = int.MinValue;
    }

    /// <summary>The line under the mouse, or null when it leaves them — so the map can show where it happened.</summary>
    public event Action<ChronicleLine?>? HoverChanged;

    private Entry? _hover;

    private readonly List<Entry> _entries = [];          // newest first
    private readonly System.Windows.Forms.Timer _animate = new() { Interval = 30 };
    private int _scroll;
    private int _contentHeight;

    public ChronicleFeed()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Background;
        _animate.Tick += (_, _) => Animate();
    }

    /// <summary>What to say while nothing has happened yet.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "";

    /// <summary>
    /// Show only the lines marked <see cref="ChronicleLine.Highlight"/>. The rest are kept, so
    /// switching back shows them again.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HighlightsOnly
    {
        get => _highlightsOnly;
        set
        {
            if (_highlightsOnly == value) return;
            _highlightsOnly = value;
            _scroll = 0;
            SetHover(null);
            Invalidate();
        }
    }
    private bool _highlightsOnly;

    private bool Shown(Entry entry) => !_highlightsOnly || entry.Line.Highlight;

    /// <summary>The lines showing.</summary>
    public int Count => _highlightsOnly ? _entries.Count(e => e.Line.Highlight) : _entries.Count;

    /// <summary>Every line kept, shown or not.</summary>
    public int Total => _entries.Count;

    private int S(int logical) => LaunchUi.S(this, logical);

    public void Clear()
    {
        _animate.Stop();
        _entries.Clear();
        _scroll = 0;
        SetHover(null);
        Invalidate();
    }

    private void SetHover(Entry? entry)
    {
        if (ReferenceEquals(entry, _hover)) return;
        _hover = entry;
        Invalidate();
        HoverChanged?.Invoke(entry?.Line);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHover(_entries.FirstOrDefault(x => Shown(x) && e.Y >= x.Top && e.Y < x.Top + x.Height && x.Line.Mark is not null));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(null);
    }

    /// <summary>Lines that just happened, oldest first. The view stays put when scrolled back.</summary>
    public void Add(IReadOnlyList<ChronicleLine> lines)
    {
        if (lines.Count == 0) return;

        int added = 0;
        foreach (var line in lines)
        {
            var entry = new Entry(line);
            _entries.Insert(0, entry);
            if (Shown(entry)) added += HeightOf(entry);
        }

        // Only a reader who has scrolled back is kept where they were; at the top, the news shows.
        if (_scroll > 0) _scroll += added;
        while (_entries.Count > Limit) _entries.RemoveAt(_entries.Count - 1);

        _animate.Start();
        Invalidate();
    }

    private void Animate()
    {
        bool moving = false;
        foreach (var entry in _entries)
        {
            if (entry.Glow <= 0f) break;   // newest first: everything below has long faded
            entry.Glow = Math.Max(0f, entry.Glow - 0.03f);
            moving = true;
        }
        if (!moving) _animate.Stop();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int max = Math.Max(0, _contentHeight - Height);
        _scroll = Math.Clamp(_scroll - e.Delta / 2, 0, max);
        // A column with more than it shows keeps the wheel; one that fits lets it scroll the page.
        if (max > 0 && e is HandledMouseEventArgs handled) handled.Handled = true;
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
        if (disposing) _animate.Dispose();
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ layout and paint

    private int Pad => S(14);
    private int Gutter => S(46);
    private int TextLeft => Pad + Gutter + S(14);
    private int TextWidth => Math.Max(S(40), Width - TextLeft - Pad);

    private const TextFormatFlags Wrap = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;

    /// <summary>The entry's height at the current width, measured once per width.</summary>
    private int HeightOf(Entry entry)
    {
        int width = TextWidth;
        if (entry.MeasuredFor != width)
        {
            var size = TextRenderer.MeasureText(entry.Line.Text, LineFont, new Size(width, 0), Wrap);
            entry.Height = size.Height + S(10);
            entry.MeasuredFor = width;
        }
        return entry.Height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var cardPath = Rounded(card, S(10));
        using (var fill = new SolidBrush(Theme.Surface)) g.FillPath(fill, cardPath);

        if (!_entries.Any(Shown))
        {
            string empty = _entries.Count == 0 ? EmptyText
                : "Nothing big enough for the highlights yet. Untick \"Highlights only\" to see everything.";
            _contentHeight = 0;
            TextRenderer.DrawText(g, empty, EmptyFont, new Rectangle(Pad, Pad, Width - 2 * Pad, Height - 2 * Pad),
                Theme.TextDim, Wrap);
        }
        else
        {
            g.SetClip(cardPath);
            int y = S(8) - _scroll;
            int? lastYear = null;
            _contentHeight = S(16);
            foreach (var entry in _entries)
            {
                if (!Shown(entry))
                {
                    entry.Top = int.MinValue;
                    continue;
                }
                int h = HeightOf(entry);
                _contentHeight += h;
                bool firstOfYear = entry.Line.Year != lastYear;
                lastYear = entry.Line.Year;

                entry.Top = y;
                if (y + h > 0 && y < Height) DrawEntry(g, entry, y, h, firstOfYear);
                y += h;
            }
            g.ResetClip();
        }

        using var edge = new Pen(Theme.Border);
        g.DrawPath(edge, cardPath);
    }

    private void DrawEntry(Graphics g, Entry entry, int y, int h, bool firstOfYear)
    {
        if (ReferenceEquals(entry, _hover))
        {
            using var hover = new SolidBrush(Theme.SurfaceHigh);
            g.FillRectangle(hover, 1, y, Width - 2, h);
        }
        if (entry.Glow > 0f)
        {
            using var glow = new SolidBrush(Color.FromArgb((int)(entry.Glow * 255), Theme.AccentSoft));
            g.FillRectangle(glow, 1, y, Width - 2, h);
        }

        if (firstOfYear)
        {
            // A hairline between years, the year itself in the gutter.
            if (y > S(8))
            {
                using var rule = new Pen(Theme.SurfaceHigh);
                g.DrawLine(rule, Pad, y, Width - Pad, y);
            }
            TextRenderer.DrawText(g, $"{entry.Line.Year}", YearFont, new Rectangle(Pad, y + S(5), Gutter, S(18)),
                Theme.Accent, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        if (entry.Line.Colour is { } c)
        {
            int dot = S(8);
            using var swatch = new SolidBrush(Color.FromArgb(c.R, c.G, c.B));
            g.FillEllipse(swatch, Pad + Gutter, y + S(9), dot, dot);
        }

        TextRenderer.DrawText(g, entry.Line.Text, LineFont, new Rectangle(TextLeft, y + S(5), TextWidth, h - S(5)),
            Theme.Text, Wrap);
    }
}
