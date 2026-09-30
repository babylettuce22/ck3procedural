using System.Drawing.Drawing2D;
using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// A row of map tiles that scrolls sideways once there are more than fit. The tiles keep the size
/// they would have with <see cref="VisibleTiles"/> in the row, so adding a map type adds to the row
/// rather than shrinking every picture in it.
///
/// Scrolled by the wheel (vertical or tilt) while the pointer is over it, by the round arrow
/// buttons at either end, by dragging the thin bar underneath, and to whichever tile is picked or
/// takes the keyboard focus. When everything fits none of that shows, and the row is just a row.
/// At either end the wheel passes on to the page, so the page still scrolls from here.
/// </summary>
internal sealed class TileStrip : Control
{
    /// <summary>How many tiles the row is sized for: the six map types it held before it scrolled.</summary>
    public const int VisibleTiles = 6;

    private const int WmMouseHWheel = 0x020E;

    private readonly List<MapTile> _tiles = [];
    private readonly ScrollArrow _left, _right;
    private readonly System.Windows.Forms.Timer _glide = new() { Interval = 15 };
    private int _tileW, _gap, _tileH;
    private int _offset, _target;
    private bool _dragging, _thumbHover;
    private int _dragFrom, _dragOffset;

    public TileStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _left = new ScrollArrow(left: true) { Visible = false, TabStop = false, AccessibleName = "Scroll map types left" };
        _right = new ScrollArrow(left: false) { Visible = false, TabStop = false, AccessibleName = "Scroll map types right" };
        _left.Click += (_, _) => ScrollTo(_target - Page);
        _right.Click += (_, _) => ScrollTo(_target + Page);
        Controls.Add(_left);
        Controls.Add(_right);
        _glide.Tick += (_, _) => Glide();
    }

    private int S(int logical) => LaunchUi.S(this, logical);

    public void Add(MapTile tile)
    {
        _tiles.Add(tile);
        Controls.Add(tile);
        tile.GotFocus += (_, _) => EnsureVisible(tile);
        _left.BringToFront();
        _right.BringToFront();
    }

    private int ContentWidth => _tiles.Count == 0 ? 0 : _tiles.Count * (_tileW + _gap) - _gap;
    private int MaxOffset => Math.Max(0, ContentWidth - Width);
    private bool Overflows => MaxOffset > 0;

    /// <summary>One click of an arrow: a viewful, less a tile, so the one at the edge stays in sight.</summary>
    private int Page => Math.Max(_tileW + _gap, Width - (_tileW + _gap));

    /// <summary>
    /// Sizes the tiles for a row <paramref name="width"/> wide and returns the height the strip
    /// needs there: the tiles, and the scroll bar under them when they do not all fit. The page
    /// sets the strip's bounds from that.
    /// </summary>
    public int Measure(int width)
    {
        var (tileW, gap) = StepPanel.Spread(width, Math.Clamp(_tiles.Count, 1, VisibleTiles), S(10), S(210));
        _tileW = tileW;
        _gap = gap;
        _tileH = _tiles.Count == 0 ? 0 : _tiles.Max(t => t.HeightFor(tileW));
        bool overflows = ContentWidth > width;
        return _tileH + (overflows ? S(16) : 0);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        _target = Math.Clamp(_target, 0, MaxOffset);
        _offset = Math.Clamp(_offset, 0, MaxOffset);
        Place();
    }

    /// <summary>Scrolls just far enough that <paramref name="tile"/> is wholly in view, clear of the arrows.</summary>
    public void EnsureVisible(MapTile tile, bool animate = true)
    {
        int i = _tiles.IndexOf(tile);
        if (i < 0 || !Overflows) return;
        int left = i * (_tileW + _gap), right = left + _tileW;

        int margin = S(28);
        int target = _target;
        if (left - margin < target) target = left - margin;
        else if (right + margin > target + Width) target = right + margin - Width;

        if (animate) ScrollTo(target);
        else
        {
            _glide.Stop();
            _target = _offset = Math.Clamp(target, 0, MaxOffset);
            Place();
        }
    }

    private void ScrollTo(int target)
    {
        _target = Math.Clamp(target, 0, MaxOffset);
        if (_target != _offset) _glide.Start();
        else Place();
    }

    /// <summary>Eases toward the target a share of the way each tick, so a jump reads as motion.</summary>
    private void Glide()
    {
        int step = (_target - _offset) * 35 / 100;
        if (step == 0) step = Math.Sign(_target - _offset);
        _offset += step;
        if (_offset == _target) _glide.Stop();
        Place();
    }

    private void Place()
    {
        SuspendLayout();
        // Each tile is its own window, and Windows moves a window by copying its old pixels to the
        // new spot. Moved one at a time, a tile can land on a neighbour that has not moved yet and
        // the copy carries pieces of both, which smeared the row while it glided. So the tiles move
        // in the direction of travel, the leading one first, and every one repaints whole after.
        bool rightward = _tiles.Count > 0 && _tiles[0].Left < -_offset;
        for (int n = 0; n < _tiles.Count; n++)
        {
            int i = rightward ? _tiles.Count - 1 - n : n;
            _tiles[i].Bounds = new Rectangle(i * (_tileW + _gap) - _offset, 0, _tileW, _tileH);
        }
        foreach (var tile in _tiles) tile.Invalidate();

        // The arrows sit on the edges of the pictures, halfway down them, only where there is
        // somewhere to go.
        int d = S(30);
        int pictureMid = S(6) + (_tileW - S(12)) / 4;
        _left.Bounds = new Rectangle(S(4), pictureMid - d / 2, d, d);
        _right.Bounds = new Rectangle(Width - d - S(4), pictureMid - d / 2, d, d);
        _left.Visible = Overflows && _target > 0;
        _right.Visible = Overflows && _target < MaxOffset;
        ResumeLayout(false);
        Invalidate();
    }

    private Rectangle Bar => new(0, Height - S(6), Width, S(6));

    private Rectangle Thumb
    {
        get
        {
            var bar = Bar;
            int w = Math.Max(S(40), (int)((long)bar.Width * Width / Math.Max(1, ContentWidth)));
            int x = MaxOffset == 0 ? 0 : (int)((long)(bar.Width - w) * _offset / MaxOffset);
            return new Rectangle(bar.X + x, bar.Y, w, bar.Height);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        if (!Overflows) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var track = Rounded(Bar, Bar.Height / 2f))
        using (var brush = new SolidBrush(Theme.Rule))
            g.FillPath(brush, track);
        using var thumb = Rounded(Thumb, Bar.Height / 2f);
        using var thumbBrush = new SolidBrush(_dragging || _thumbHover ? Theme.TrackHover : Theme.Track);
        g.FillPath(thumbBrush, thumb);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Overflows || e.Button != MouseButtons.Left) return;
        var hit = Bar;
        hit.Inflate(0, S(5));
        if (!hit.Contains(e.Location)) return;

        // A press on the bar beside the thumb brings the thumb there first, then drags from there.
        _glide.Stop();
        if (!Thumb.Contains(e.Location))
        {
            int w = Thumb.Width;
            _offset = _target = Math.Clamp((int)((long)(e.X - w / 2) * MaxOffset / Math.Max(1, Bar.Width - w)), 0, MaxOffset);
            Place();
        }
        _dragging = true;
        _dragFrom = e.X;
        _dragOffset = _offset;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            int travel = Math.Max(1, Bar.Width - Thumb.Width);
            _offset = _target = Math.Clamp(_dragOffset + (int)((long)(e.X - _dragFrom) * MaxOffset / travel), 0, MaxOffset);
            Place();
            return;
        }
        var hit = Thumb;
        hit.Inflate(0, S(5));
        bool over = Overflows && hit.Contains(e.Location);
        if (over != _thumbHover) { _thumbHover = over; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_thumbHover) { _thumbHover = false; Invalidate(); }
    }

    /// <summary>
    /// The wheel, over the strip or passed up from a tile under the pointer. Taken only while the
    /// row can still move that way; at an end it goes on to the page.
    /// </summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Wheel(-e.Delta) && e is HandledMouseEventArgs handled) handled.Handled = true;
        else base.OnMouseWheel(e);
    }

    protected override void WndProc(ref Message m)
    {
        // Tilt wheels and sideways touchpad swipes: positive is to the right, the opposite sign
        // to the vertical wheel's.
        if (m.Msg == WmMouseHWheel)
        {
            int delta = unchecked((short)((long)m.WParam >> 16));
            if (Wheel(delta)) { m.Result = IntPtr.Zero; return; }
        }
        base.WndProc(ref m);
    }

    /// <summary>Scrolls by a wheel delta (120 a notch). False when the row cannot move that way.</summary>
    private bool Wheel(int delta)
    {
        if (!Overflows || delta == 0) return false;
        if (delta < 0 && _target <= 0) return false;
        if (delta > 0 && _target >= MaxOffset) return false;
        ScrollTo(_target + delta * (_tileW + _gap) / 120);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _glide.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>A round button with a chevron, floating over the tiles at one end of the row.</summary>
    private sealed class ScrollArrow(bool left) : PaintedButton
    {
        private static readonly Font Glyph = new(GlyphFamily, 9f);

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // Round, so nothing is painted over the tile beneath outside the circle.
            using var round = new GraphicsPath();
            round.AddEllipse(-1, -1, Width + 1, Height + 1);
            Region = new Region(round);
        }

        protected override void Draw(Graphics g)
        {
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var brush = new SolidBrush(Hover ? Theme.SurfaceHover : Theme.Surface)) g.FillEllipse(brush, box);
            using (var pen = new Pen(Hover ? Theme.Accent : Theme.BorderStrong, 1f)) g.DrawEllipse(pen, box);
            TextRenderer.DrawText(g, left ? "" : "", Glyph, Rectangle.Round(box),
                Hover ? Theme.Accent : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
