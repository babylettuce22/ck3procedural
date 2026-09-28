using System.Drawing.Drawing2D;
using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The milestones of a run, as a grid of steps that light up as the generator passes them: the
/// land, its climate and rivers, then the peoples, realms and faiths, and so on to the finish.
///
/// Driven by the generator's own stage names (<see cref="Core.Stage"/>). Milestones are listed in
/// the order a run actually reaches them and only ever move forward, so a late stage that happens
/// to share a word with an early milestone cannot send the display backwards.
/// </summary>
internal sealed class MilestoneStrip : Control
{
    private static readonly Font LabelFont = new("Segoe UI", 9f);
    private static readonly Font LabelBold = new("Segoe UI Semibold", 9f);
    private static readonly Font Mark = new(GlyphFamily, 7.5f);
    private static readonly Font Number = new("Segoe UI Semibold", 8f);

    /// <summary>Each milestone and the stage names (by prefix) that mean the run has reached it.</summary>
    private static readonly (string Label, string[] Stages)[] Milestones =
    [
        ("Land", ["province elevation", "coarse world", "land mask", "heightmap"]),
        ("Climate", ["climate"]),
        ("Rivers", ["drainage", "major rivers", "recompute"]),
        ("Provinces", ["province partition", "terrain classification", "title hierarchy", "province terrain", "county seats"]),
        ("Peoples", ["cultures", "ethnicities", "world centers"]),
        ("Realms", ["realm capitals", "realms", "governments"]),
        ("Faiths", ["faiths", "gender", "cultivation"]),
        ("Armies", ["retinues", "culture files", "men-at-arms", "generated innovations"]),
        ("Scenery", ["map graphics", "flatmap", "terrain textures", "terrain masks", "trees", "animals", "bridges", "city scatter", "holding models"]),
        ("History", ["prehistory", "rulers", "starting retinues", "vanilla characters"]),
        ("Treasures", ["weapon forge", "artifacts", "weapon icons", "artifact visuals", "armour forge"]),
        ("Finishing", ["bookmarks", "character history", "chronicle", "struggles", "portraits", "static files", "debug panel", "watermark"]),
    ];

    private int _current = -1;
    private bool _finished;
    private float _pulse;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };

    public MilestoneStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Background;
        _timer.Tick += (_, _) => { _pulse = (_pulse + 0.05f) % 1f; Invalidate(); };
    }

    private int S(int logical) => LaunchUi.S(this, logical);

    public const int Columns = 6;

    /// <summary>
    /// Columns at <paramref name="width"/>: six, or fewer when a cell would be too narrow for its
    /// name (in the bold the current step takes), so no name is ever cut short.
    /// </summary>
    public int ColumnsFor(int width)
    {
        foreach (int columns in (int[])[Columns, 4, 3, 2])
        {
            int label = (width - S(8) * (columns - 1)) / columns - S(38);
            if (Milestones.All(m => TextRenderer.MeasureText(m.Label, LabelBold, Size.Empty, TextFormatFlags.NoPadding).Width <= label))
                return columns;
        }
        return 1;
    }

    /// <summary>Height wanted for the grid at <paramref name="width"/> and the current DPI.</summary>
    public int GridHeight(int width)
    {
        int rows = (Milestones.Length + ColumnsFor(width) - 1) / ColumnsFor(width);
        return rows * S(40) + (rows - 1) * S(8);
    }

    public void Reset()
    {
        _current = -1;
        _finished = false;
        _timer.Start();
        Invalidate();
    }

    public void Finish(bool completed)
    {
        _finished = completed;
        _timer.Stop();
        Invalidate();
    }

    /// <summary>A stage the generator entered. Moves the display forward, never back.</summary>
    public void Enter(string stage)
    {
        string name = stage.Trim(' ', '·').ToLowerInvariant();
        for (int i = Milestones.Length - 1; i > _current; i--)
        {
            if (Milestones[i].Stages.Any(s => name.StartsWith(s, StringComparison.Ordinal)))
            {
                _current = i;
                Invalidate();
                return;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int gap = S(8);
        int columns = ColumnsFor(Width);
        int cellW = (Width - gap * (columns - 1)) / columns;
        int cellH = S(40);

        for (int i = 0; i < Milestones.Length; i++)
        {
            int col = i % columns, row = i / columns;
            var cell = new Rectangle(col * (cellW + gap), row * (cellH + gap), cellW, cellH);
            bool done = _finished || i < _current;
            bool current = !_finished && i == _current;

            using (var path = Rounded(new RectangleF(cell.X + 0.5f, cell.Y + 0.5f, cell.Width - 1, cell.Height - 1), S(8)))
            {
                var fill = current ? SelectedWash : done ? Theme.Surface : Theme.Background;
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
                using var pen = new Pen(current ? Theme.Accent : Theme.Border, current ? 1.5f : 1f);
                if (!done || current) pen.DashStyle = current ? DashStyle.Solid : DashStyle.Dot;
                g.DrawPath(pen, path);
            }

            int dot = S(20);
            var circle = new Rectangle(cell.X + S(9), cell.Y + (cellH - dot) / 2, dot, dot);
            if (done)
            {
                using var brush = new SolidBrush(Good);
                g.FillEllipse(brush, circle);
                TextRenderer.DrawText(g, "", Mark, circle, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else if (current)
            {
                // A soft pulse around the step being worked on.
                float grow = S(4) * (float)Math.Sin(_pulse * Math.PI);
                using (var halo = new SolidBrush(Color.FromArgb((int)(70 * (1 - _pulse)), Theme.Accent)))
                    g.FillEllipse(halo, circle.X - grow, circle.Y - grow, circle.Width + 2 * grow, circle.Height + 2 * grow);
                using var brush = new SolidBrush(Theme.Accent);
                g.FillEllipse(brush, circle);
                TextRenderer.DrawText(g, (i + 1).ToString(), Number, circle, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                using var pen = new Pen(Theme.Border, 1.5f);
                g.DrawEllipse(pen, circle);
                TextRenderer.DrawText(g, (i + 1).ToString(), Number, circle, Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            var label = new Rectangle(circle.Right + S(7), cell.Y, cell.Right - circle.Right - S(9), cellH);
            TextRenderer.DrawText(g, Milestones[i].Label, current ? LabelBold : LabelFont, label,
                done || current ? Theme.Text : Theme.TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>
/// "At a glance": the finished world counted — counties, kingdoms, peoples, faiths — as a row of
/// tiles, each a big number over its name.
/// </summary>
internal sealed class TallyRow : Control
{
    private static readonly Font NumberFont = new("Segoe UI Semibold", 15f);
    private static readonly Font LabelFont = new("Segoe UI", 8.5f);
    private IReadOnlyList<(string Label, int Count)> _tallies = [];

    public TallyRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Background;
    }

    private int S(int logical) => LaunchUi.S(this, logical);

    public const int Columns = 6;

    /// <summary>Tiles per row. Six under a finished world; the Azgaar page counts five things.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int ColumnCount { get; set; } = Columns;

    /// <summary>
    /// Tiles per row at <paramref name="width"/>: <see cref="ColumnCount"/>, or fewer when a tile
    /// would be too narrow for its number or its name — spread evenly over the rows that takes, so
    /// six tiles fall to two rows of three rather than four and two.
    /// </summary>
    public int ColumnsFor(int width)
    {
        int n = Math.Max(1, _tallies.Count);
        for (int columns = Math.Max(1, ColumnCount); columns > 1; columns--)
        {
            int room = (width - S(8) * (columns - 1)) / columns - S(16);
            if (_tallies.All(t => TextRenderer.MeasureText(t.Label, LabelFont, Size.Empty, TextFormatFlags.NoPadding).Width <= room
                                  && TextRenderer.MeasureText(t.Count.ToString("N0"), NumberFont, Size.Empty, TextFormatFlags.NoPadding).Width <= room))
            {
                if (columns == ColumnCount) return columns;
                int rows = (n + columns - 1) / columns;
                return (n + rows - 1) / rows;
            }
        }
        return 1;
    }

    /// <summary>Height wanted for the tiles at <paramref name="width"/>; nothing when there are none.</summary>
    public int GridHeight(int width)
    {
        if (_tallies.Count == 0) return 0;
        int columns = ColumnsFor(width);
        return ((_tallies.Count + columns - 1) / columns) * (S(58) + S(8)) - S(8);
    }

    public void Set(IReadOnlyList<(string Label, int Count)> tallies)
    {
        _tallies = tallies;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int gap = S(8);
        int columns = ColumnsFor(Width);
        int cellW = (Width - gap * (columns - 1)) / columns;
        int cellH = S(58);

        for (int i = 0; i < _tallies.Count; i++)
        {
            int col = i % columns, row = i / columns;
            var cell = new Rectangle(col * (cellW + gap), row * (cellH + gap), cellW, cellH);
            using (var path = Rounded(new RectangleF(cell.X + 0.5f, cell.Y + 0.5f, cell.Width - 1, cell.Height - 1), S(8)))
            {
                using var brush = new SolidBrush(Theme.Surface);
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.Border);
                g.DrawPath(pen, path);
            }

            var (label, count) = _tallies[i];
            TextRenderer.DrawText(g, count.ToString("N0"), NumberFont, new Rectangle(cell.X + S(12), cell.Y + S(6), cell.Width - S(16), S(28)),
                Theme.Text, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, label, LabelFont, new Rectangle(cell.X + S(12), cell.Y + S(34), cell.Width - S(16), S(18)),
                Theme.TextDim, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
