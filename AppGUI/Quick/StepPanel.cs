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
        using var rule = new SolidBrush(Color.FromArgb(232, 236, 242));
        foreach (var r in Rules) g.FillRectangle(rule, r);
    }

    /// <summary>The launcher column: at most 940 wide, centred, with 32 either side.</summary>
    public static (int X, int Width) Column(Control host)
    {
        int width = Math.Min(host.ClientSize.Width - 2 * S(host, 32), S(host, 940));
        return ((host.ClientSize.Width - width) / 2, Math.Max(width, 100));
    }

    /// <summary>How tall a wrapping label is at a width.</summary>
    public static int Wrapped(Label label, int width)
        => TextRenderer.MeasureText(label.Text ?? "", label.Font, new Size(Math.Max(1, width), 0),
               TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 2;
}
