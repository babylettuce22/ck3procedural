namespace Ck3MapGen.AppGUI;

/// <summary>
/// The window's own caption: icon, menu bar (or, on a palette, its title) and the caption buttons, in one row.
///
/// The Windows frame is kept, not replaced — <see cref="ChromeForm"/> only takes away its caption
/// strip (WM_NCCALCSIZE) — so resizing, Aero Snap, the shadow and the rounded corners are the
/// system's own. A borderless form would have to rebuild all of those by hand and still get snap
/// wrong.
///
/// The row itself does no mouse handling. It answers every hit test with HTTRANSPARENT, which hands
/// the question to the form underneath, and the form answers with <see cref="HitTest"/>: HTCAPTION
/// over empty space, so dragging, double-click to maximise and the right-click system menu are
/// Windows' own too, and HTMINBUTTON / HTMAXBUTTON / HTCLOSE over the buttons. HTMAXBUTTON is what
/// makes Windows 11 show its snap layouts when the maximise button is hovered; a button that only
/// looked like one would never get them. The menu strip is a child window of its own, so it never
/// sees any of this and works as a normal menu.
/// </summary>
internal sealed class TitleBar : Control
{
    public const int HtClient = 1, HtCaption = 2, HtSysMenu = 3, HtMinButton = 8, HtMaxButton = 9, HtClose = 20;
    private const int HtTransparent = -1;
    private const int WmNcHitTest = 0x0084;

    /// <summary>
    /// The caption glyphs Windows itself draws them with. MDL2 Assets is on Windows 10 and 11 alike;
    /// Windows 11's Fluent Icons carries the same code points.
    /// </summary>
    private static readonly Font Glyphs = new("Segoe MDL2 Assets", 7.5f);
    private const string Minimize = "", Maximize = "", Restore = "", CloseGlyph = "";

    private static readonly Color CloseHover = Color.FromArgb(196, 43, 28);
    private static readonly Color ClosePressed = Color.FromArgb(200, 70, 58);

    private readonly Form _form;
    private readonly MenuStrip? _menu;
    private int _hover;
    private int _pressed;
    private Icon? _icon;
    private Icon? _iconSource;

    /// <summary>
    /// With a <paramref name="menu"/>, the main window's row: menus beside the icon and no title
    /// at all. Without one, a palette's row — the inspectors — where the title is the name of
    /// what is being inspected and so is the thing to read: left-aligned, full strength.
    /// </summary>
    public TitleBar(Form form, MenuStrip? menu = null)
    {
        _form = form;
        _menu = menu;

        Dock = DockStyle.Top;
        Height = Scale(32);
        BackColor = Theme.Surface;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        if (menu is not null)
        {
            menu.Dock = DockStyle.None;
            menu.AutoSize = true;
            menu.Padding = new Padding(0);
            Controls.Add(menu);
        }

        // What the row shows follows the form: its title, whether it is the active window (Windows
        // dims an inactive caption), and whether it is maximised (the middle glyph).
        form.TextChanged += (_, _) => Invalidate();
        form.Activated += (_, _) => Invalidate();
        form.Deactivate += (_, _) => Invalidate();
        form.Resize += (_, _) => Invalidate();
    }

    private int Scale(int logical) => logical * DeviceDpi / 96;

    private int ButtonWidth => Scale(46);

    /// <summary>
    /// The buttons the form asks for, right to left from the corner: close always, then maximise
    /// and minimise where the form has them. A palette with neither keeps just the close button in
    /// the corner, as a stock tool window does.
    /// </summary>
    private IEnumerable<(int Hit, Rectangle Bounds)> Buttons()
    {
        int right = Width;
        foreach (int hit in (int[])[HtClose, HtMaxButton, HtMinButton])
        {
            if (hit == HtMaxButton && !_form.MaximizeBox) continue;
            if (hit == HtMinButton && !_form.MinimizeBox) continue;
            right -= ButtonWidth;
            yield return (hit, new Rectangle(right, 0, ButtonWidth, Height));
        }
    }

    private int ButtonsLeft => Buttons().Min(b => b.Bounds.Left);

    private Rectangle IconBounds => _form.ShowIcon ? new(0, 0, Scale(38), Height) : new(0, 0, Scale(6), Height);

    private bool Active => Form.ActiveForm == _form || _form.ContainsFocus;

    /// <summary>What Windows should take a point in this row to be. Points are in this control's coordinates.</summary>
    public int HitTest(Point point)
    {
        foreach (var (hit, bounds) in Buttons())
            if (bounds.Contains(point)) return hit;
        if (_form.ShowIcon && IconBounds.Contains(point)) return HtSysMenu;
        return HtCaption;
    }

    public static bool IsButton(int hit) => hit is HtMinButton or HtMaxButton or HtClose;

    /// <summary>The button under the mouse, or 0. Fed from the form's non-client mouse messages.</summary>
    public void SetHover(int hit)
    {
        if (_hover == hit) return;
        _hover = hit;
        Invalidate();
    }

    /// <summary>The button the mouse went down on, or 0; it acts only if the mouse comes up on it too.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Pressed
    {
        get => _pressed;
        set { if (_pressed == value) return; _pressed = value; Invalidate(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icon?.Dispose();
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
        {
            m.Result = HtTransparent;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_menu is not null) _menu.Location = new Point(IconBounds.Right - Scale(4), (Height - _menu.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        bool active = Active;

        if (_form.ShowIcon && _form.Icon is { } icon)
        {
            int size = Scale(16);
            // The small frame of the .ico, not the large one squeezed: 16 px art is drawn for 16 px.
            if (_icon is null || _iconSource != icon)
            {
                _icon?.Dispose();
                _icon = new Icon(icon, size, size);
                _iconSource = icon;
            }

            var box = IconBounds;
            g.DrawIcon(_icon, new Rectangle(box.X + (box.Width - size) / 2 + Scale(2), (Height - size) / 2, size, size));
        }

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
            | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        // The main window's row carries no title: the icon and the menus say what the application
        // is, and the name still shows in the taskbar and Alt+Tab. A palette's title is the name of
        // what is being inspected, which is worth the room.
        if (_menu is null)
        {
            var area = Rectangle.FromLTRB(IconBounds.Right + Scale(6), 0, ButtonsLeft - Scale(8), Height);
            if (area.Width > 0)
                TextRenderer.DrawText(g, _form.Text, Theme.UiBold, area, active ? Theme.Text : Theme.TextDim, flags);
        }

        foreach (var (hit, bounds) in Buttons())
        {
            string glyph = hit switch
            {
                HtMinButton => Minimize,
                HtMaxButton => _form.WindowState == FormWindowState.Maximized ? Restore : Maximize,
                _ => CloseGlyph,
            };
            DrawButton(g, bounds, hit, glyph, active);
        }
    }

    private void DrawButton(Graphics g, Rectangle bounds, int hit, string glyph, bool active)
    {
        bool hover = _hover == hit;
        bool pressed = hover && _pressed == hit;
        bool close = hit == HtClose;

        Color back = !hover ? BackColor
            : close ? (pressed ? ClosePressed : CloseHover)
            : pressed ? Theme.Border : Theme.SurfaceHigh;
        Color fore = close && hover ? Color.White : active || hover ? Theme.Text : Theme.TextDim;

        using (var brush = new SolidBrush(back)) g.FillRectangle(brush, bounds);
        TextRenderer.DrawText(g, glyph, Glyphs, bounds, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
