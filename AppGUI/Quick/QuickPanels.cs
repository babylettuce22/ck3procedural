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

    /// <summary>Height wanted for the grid at the current DPI.</summary>
    public int PreferredGridHeight => 2 * S(40) + S(8);

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
        int cellW = (Width - gap * (Columns - 1)) / Columns;
        int cellH = S(40);

        for (int i = 0; i < Milestones.Length; i++)
        {
            int col = i % Columns, row = i / Columns;
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

    public int PreferredGridHeight
        => _tallies.Count == 0 ? 0 : ((_tallies.Count + Columns - 1) / Columns) * (S(58) + S(8)) - S(8);

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
        int cellW = (Width - gap * (Columns - 1)) / Columns;
        int cellH = S(58);

        for (int i = 0; i < _tallies.Count; i++)
        {
            int col = i % Columns, row = i / Columns;
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
