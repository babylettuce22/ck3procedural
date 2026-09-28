using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// One view of a launcher page: a step, the run, or the finished world. Its children are placed by
/// <see cref="Arrange"/> on every layout, and it draws the white cards and hairlines the
/// arrangement asks for underneath them.
///
/// Shared by the Quick and Azgaar pages and by <see cref="RunScreen"/>, so every launcher view is
/// laid out the same way and looks like one thing.
///
/// <see cref="Arrange"/> lays out in content coordinates, from the top of the view. When the view
/// is taller than the window it scrolls (<see cref="ArrangeScrolling"/>) rather than letting the
/// rows at its foot fall off the bottom.
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
        WheelFollowsMouse.Install();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        Unanchor(e.Control);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        ArrangeScrolling(this, () =>
        {
            Cards.Clear();
            Rules.Clear();
            Arrange?.Invoke(this);
            int bottom = Cards.Count == 0 ? 0 : Cards.Max(c => c.Bottom);
            foreach (Control c in Controls)
                if (c.Visible && c.Height > 0) bottom = Math.Max(bottom, c.Bottom);
            // The margin the side columns keep from the foot, so a column sized to the window
            // never asks for a scrollbar by itself.
            return bottom + S(this, 10);
        }, () => AdjustFormScrollbars(AutoScroll));
        Invalidate();
    }

    /// <summary>Each time a view comes on screen it starts at its top.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && IsHandleCreated && AutoScrollPosition != Point.Empty) AutoScrollPosition = Point.Empty;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.TranslateTransform(0, AutoScrollPosition.Y);
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
    /// It is never shorter than <paramref name="minHeight"/> (width allowing): past that a short
    /// window scrolls the view rather than shrinking the map to a sliver.
    /// </summary>
    public static Rectangle Map(int x, int y, int maxWidth, int maxHeight, int minHeight = 0)
    {
        int h = Math.Max(1, Math.Min(maxWidth / 2, Math.Max(maxHeight, minHeight)));
        return new Rectangle(x, y, h * 2, h);
    }

    /// <summary>The shortest a map is drawn before the view scrolls instead, in 96-dpi pixels.</summary>
    public const int MinMapHeight = 150;

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

    /// <summary>
    /// How tall a wrapping label is at a width: the label's own measure, so the bounds hold every
    /// line it draws. A measure of the bare text came out a line short whenever the text only
    /// just fitted, because a Label pads its text and so breaks it sooner, and the last line was
    /// cut off.
    /// </summary>
    public static int Wrapped(Label label, int width)
        => string.IsNullOrEmpty(label.Text) ? 0 : label.GetPreferredSize(new Size(Math.Max(1, width), 0)).Height;

    /// <summary>
    /// Puts <paramref name="label"/> at (<paramref name="x"/>, <paramref name="y"/>), wrapped to
    /// <paramref name="width"/> and as tall as its lines. Returns the height it took.
    /// </summary>
    public static int Place(Label label, int x, int y, int width)
    {
        label.AutoSize = false;
        label.Bounds = new Rectangle(x, y, Math.Max(1, width), Wrapped(label, width));
        return label.Height;
    }

    /// <summary>
    /// A row's hint beside its title when it fits there, or wrapped under it when it does not.
    /// Returns where what comes under the pair starts.
    /// </summary>
    public static int TitleAndHint(Label title, Label hint, int x, int y, int width)
    {
        title.Location = new Point(x, y);
        int beside = x + title.PreferredWidth + S(title, 10);
        hint.AutoSize = false;
        var single = hint.GetPreferredSize(Size.Empty);
        if (string.IsNullOrEmpty(hint.Text) || single.Width <= x + width - beside)
        {
            hint.Bounds = new Rectangle(beside, y + (title.PreferredHeight - single.Height) / 2 + S(title, 1), single.Width, single.Height);
            return y + title.PreferredHeight;
        }

        y += title.PreferredHeight + S(title, 2);
        return y + Place(hint, x, y, width);
    }

    /// <summary>
    /// A review card's rows — a name, its value, and a Change link — from the top of the card at
    /// (<paramref name="x"/>, <paramref name="y"/>), <paramref name="width"/> wide. A value too long
    /// for its line wraps and its row grows to hold it, rather than the value being cut short; the
    /// name and the link stay level with its first line. Returns the card's height.
    /// </summary>
    public static int Summary(StepPanel panel, IReadOnlyList<(Label Key, Label Value, TextLink Change)> rows,
        int x, int y, int width, int pad, int keyWidth)
    {
        int ry = y + pad;
        for (int i = 0; i < rows.Count; i++)
        {
            var (key, value, change) = rows[i];
            int vx = x + pad + keyWidth;
            int vw = x + width - pad - change.Width - S(panel, 8) - vx;
            value.AutoEllipsis = false;
            int line = value.GetPreferredSize(Size.Empty).Height;
            int vh = Math.Max(line, Place(value, vx, 0, vw));
            int rowH = Math.Max(S(panel, 38), vh + S(panel, 20));
            int top = ry + (rowH - vh) / 2;
            value.Top = top;
            key.Location = new Point(x + pad, top + (line - key.PreferredHeight) / 2);
            change.Location = new Point(x + width - pad - change.Width, top + (line - change.Height) / 2);
            if (i > 0) panel.Rules.Add(new Rectangle(x + pad, ry, width - 2 * pad, 1));
            ry += rowH;
        }
        return ry + pad - y;
    }

    // ------------------------------------------------------------------ scrolling

    private static readonly HashSet<ScrollableControl> Laying = [];

    /// <summary>
    /// Lays a hand-arranged panel out and makes it scroll when what it holds is taller than it is.
    /// <paramref name="arrange"/> places the children in content coordinates — from the top, as if
    /// nothing were scrolled — and returns how tall the content is. The panel is then given a
    /// vertical scrollbar just when that is more than it has room for, and the children are moved
    /// up by however far it is scrolled.
    ///
    /// Only the height that <paramref name="arrange"/> reports decides the scrollbar: the children
    /// are unanchored (<see cref="Unanchor"/>), and an unanchored child counts for nothing in
    /// WinForms' own reckoning of how far to scroll. So there is never a horizontal scrollbar, and a
    /// child the arrangement has not yet moved back to content coordinates cannot stretch the range.
    ///
    /// Called from the panel's OnLayout, where WinForms drops any further layout the panel asks
    /// for, including the one setting AutoScrollMinSize asks for to fit the scrollbar to it. So
    /// <paramref name="adjustScrollbars"/> — the panel's own AdjustFormScrollbars — is called
    /// directly; without it the range lagged a layout behind, and a row that had just appeared at
    /// the foot could not be scrolled to.
    /// </summary>
    public static void ArrangeScrolling(ScrollableControl panel, Func<int> arrange, Action adjustScrollbars)
    {
        if (!Laying.Add(panel)) return;
        try
        {
            // A scrollbar coming or going changes the width to lay out in, and so what the
            // content comes to: arrange again until the range stands, so the children are always
            // left where the last arrangement put them.
            for (int pass = 0; ; pass++)
            {
                int content = arrange();
                var min = content > panel.ClientSize.Height ? new Size(0, content) : Size.Empty;
                if (panel.AutoScrollMinSize == min || pass == 3) break;
                panel.AutoScrollMinSize = min;
                adjustScrollbars();
            }

            int dy = panel.AutoScrollPosition.Y;
            if (dy != 0)
                foreach (Control c in panel.Controls) c.Top += dy;
        }
        finally
        {
            Laying.Remove(panel);
        }
    }

    /// <summary>See <see cref="ArrangeScrolling"/>: a child of a scrolling panel counts nothing toward its scroll range.</summary>
    public static void Unanchor(Control? child)
    {
        if (child is not null && child.Dock == DockStyle.None) child.Anchor = AnchorStyles.None;
    }
}
