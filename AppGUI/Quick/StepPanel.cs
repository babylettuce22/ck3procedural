using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// One view of a launcher page: a step, the run, or the finished world. Its children are placed by
/// <see cref="Arrange"/> on every layout, and it draws the white cards and hairlines the
/// arrangement asks for underneath them.
///
/// Shared by the Quick and Azgaar pages and by <see cref="RunScreen"/>, so every launcher view is
/// laid out the same way and looks like one thing.
/// </summary>
internal sealed class StepPanel : Panel
{
    public Action<StepPanel>? Arrange;
    public readonly List<Rectangle> Cards = [];
    public readonly List<Rectangle> Rules = [];

    public StepPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        Visible = false;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Cards.Clear();
        Rules.Clear();
        Arrange?.Invoke(this);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        foreach (var card in Cards)
        {
            using var path = Rounded(new RectangleF(card.X + 0.5f, card.Y + 0.5f, card.Width - 1, card.Height - 1), S(this, 12));
            using var fill = new SolidBrush(Theme.Surface);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        using var rule = new SolidBrush(Theme.Rule);
        foreach (var r in Rules) g.FillRectangle(rule, r);
    }

    /// <summary>The column's width at the size the launcher window opens at.</summary>
    public const int PreferredColumn = 940;

    /// <summary>
    /// The widest the column gets. Past it the page stays centred rather than stretching cards and
    /// lines across an ultrawide screen.
    /// </summary>
    public const int MaxColumn = 1600;

    /// <summary>
    /// The launcher column: the window's width less a margin either side — 32, or 3% of a wide
    /// window — up to <see cref="MaxColumn"/>, centred. Every launcher view lays out in it, and
    /// anchors to its edges, so all of them widen and narrow with the window together. At the size
    /// the window opens at it is <see cref="PreferredColumn"/> wide.
    /// </summary>
    public static (int X, int Width) Column(Control host)
    {
        int client = host.ClientSize.Width;
        int margin = Math.Max(S(host, 32), client * 3 / 100);
        int width = Math.Min(client - 2 * margin, S(host, MaxColumn));
        return ((client - width) / 2, Math.Max(width, 100));
    }

    /// <summary>
    /// The largest 2:1 rectangle — a map — that fits <paramref name="maxWidth"/> by
    /// <paramref name="maxHeight"/>, at (<paramref name="x"/>, <paramref name="y"/>). A map is sized
    /// by whichever runs out first, so a wide window never pushes what is under it off the bottom.
    /// </summary>
    public static Rectangle Map(int x, int y, int maxWidth, int maxHeight)
    {
        int h = Math.Max(1, Math.Min(maxWidth / 2, maxHeight));
        return new Rectangle(x, y, h * 2, h);
    }

    /// <summary>
    /// Places <paramref name="count"/> items in a row across <paramref name="width"/>, each at most
    /// <paramref name="maxItem"/> wide. When they reach it the gaps grow instead, so the first item
    /// stays on the left edge and the last on the right.
    /// </summary>
    public static (int Item, int Gap) Spread(int width, int count, int gap, int maxItem)
    {
        count = Math.Max(1, count);
        int item = Math.Min((width - gap * (count - 1)) / count, maxItem);
        if (count > 1) gap = (width - item * count) / (count - 1);
        return (item, gap);
    }

    /// <summary>How tall a wrapping label is at a width.</summary>
    public static int Wrapped(Label label, int width)
        => TextRenderer.MeasureText(label.Text ?? "", label.Font, new Size(Math.Max(1, width), 0),
               TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 2;
}
