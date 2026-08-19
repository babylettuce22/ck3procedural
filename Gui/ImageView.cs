using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Ck3MapGen.Gui;

/// <summary>
/// The preview surface: wheel to zoom, drag to pan, and the view survives a rebuild.
///
/// This replaces a <see cref="PictureBox"/> in Zoom mode, which could only ever show the whole map
/// scaled into a pane a fraction of its size. Every question these views exist to answer is about a
/// place — does that coastline fray, do the provinces near that mountain follow the ridge, is that
/// desert where the climate model says it should be — and none of them can be answered from a
/// fit-to-window thumbnail of a 4608-pixel-tall raster.
///
/// Two decisions matter more than the rest:
///
/// The zoom and pan are *not* reset when the image changes. Tuning is a loop of nudge a setting,
/// rebuild, look at the same place, and a viewer that snapped back to fit on every rebuild would
/// make that loop unusable. The view resets only when the image's dimensions change, because then
/// the old transform means something different.
///
/// Magnified pixels are drawn nearest-neighbour. These rasters are classifications, not
/// photographs: a province boundary or a climate class edge is the thing being judged, and
/// interpolating across it invents intermediate colours that correspond to no class on the map.
/// </summary>
public sealed class ImageView : Control
{
    private Bitmap? _image;

    /// <summary>
    /// The image's size, kept beside it.
    ///
    /// Reading <c>_size.Width</c> during a paint is what made the preview crash: generation hands
    /// this control a new bitmap and disposes the old one, and a repaint already in flight then asks
    /// a disposed object for its size. The size never changes while an image is set, so caching it
    /// removes every one of those reads from the paint path.
    /// </summary>
    private Size _size;
    private float _zoom = 1f;
    private PointF _origin;          // Where the image's top-left sits, in control pixels.
    private bool _fit = true;        // Track the pane on resize until the user zooms.
    private Point _dragFrom;
    private bool _dragging;

    /// <summary>Fires on any view change and on mouse movement, for the status readout.</summary>
    public event Action<float, Point?>? ViewChanged;

    /// <summary>
    /// A click on a place, in image pixels — not a drag that happened to end.
    ///
    /// Left-drag pans, so a plain mouse-up cannot be a click on its own: without the movement test
    /// below, every pan would select whatever it finished over. The threshold is in control pixels
    /// so it is a physical distance regardless of zoom.
    /// </summary>
    public event Action<Point>? PixelClicked;

    private const int ClickSlop = 3;
    private Point _pressAt;
    private bool _moved;

    public ImageView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        BackColor = Theme.Background;
        ForeColor = Theme.TextDim;
        Font = Theme.Ui;

        var menu = Theme.MakeMenu();
        menu.Items.Add("Fit to window", null, (_, _) => Fit());
        menu.Items.Add("Actual size (1:1)", null, (_, _) => SetZoom(1f, Centre()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Save view as PNG…", null, (_, _) => Export());
        menu.Items.Add("Copy view", null, (_, _) => Copy());
        ContextMenuStrip = menu;
    }

    /// <summary>
    /// The name shown in the export dialog's default filename. Hidden from designer serialization
    /// because this control is only ever built in code, and the WinForms analyzer requires every
    /// public property of a Control to say which it is.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string ViewName { get; set; } = "preview";

    /// <summary>
    /// WinForms delivers the wheel to whichever control has focus, so without this the map only
    /// zoomed after it had been clicked. Focusing on mouse-enter instead would fix it by stealing
    /// focus out of the settings grid every time the cursor crossed the pane, mid-edit; a message
    /// filter routes the wheel by what it is over and leaves the keyboard where the user put it.
    /// </summary>
    private sealed class WheelFilter(ImageView view) : IMessageFilter
    {
        private const int WmMouseWheel = 0x020A;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmMouseWheel || view.IsDisposed || !view.IsHandleCreated || !view.Visible)
                return false;

            int packed = (int)(long)m.LParam;
            var screen = new Point((short)(packed & 0xFFFF), (short)(packed >> 16));
            if (!view.RectangleToScreen(view.ClientRectangle).Contains(screen)) return false;

            var local = view.PointToClient(screen);
            int delta = (short)((long)m.WParam >> 16);
            view.OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, local.X, local.Y, delta));
            return true;
        }
    }

    private IMessageFilter? _wheel;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _wheel ??= new WheelFilter(this);
        Application.AddMessageFilter(_wheel);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_wheel is not null) Application.RemoveMessageFilter(_wheel);
        base.OnHandleDestroyed(e);
    }

    public float Zoom => _zoom;

    /// <summary>
    /// Shows an image. Does <em>not</em> take ownership: the caller keeps a cache of one bitmap per
    /// view and switching between them must not destroy the one being left, so disposal belongs
    /// with whoever owns that cache.
    /// </summary>
    public void SetImage(Bitmap? image)
    {
        bool sameShape = _image is not null && image is not null
            && _size.Width == image.Width && _size.Height == image.Height;

        _image = image;
        _size = image?.Size ?? Size.Empty;

        if (!sameShape) _fit = true;
        if (_fit) FitNow();

        Invalidate();
        Announce(null);
    }

    public void Fit()
    {
        _fit = true;
        FitNow();
        Invalidate();
        Announce(null);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_fit) return;

        // Announce as well as refit. Dragging the splitter changes the fit zoom, and without this
        // the percentage in the status bar kept whatever it read before the drag.
        FitNow();
        Announce(null);
    }

    private void FitNow()
    {
        if (_image is null || Width <= 0 || Height <= 0) return;

        _zoom = Math.Min((float)Width / _size.Width, (float)Height / _size.Height);
        _origin = new PointF(
            (Width - _size.Width * _zoom) / 2f,
            (Height - _size.Height * _zoom) / 2f);
    }

    private PointF Centre() => new(Width / 2f, Height / 2f);

    /// <summary>Zooms about a fixed point of the control, so the pixel under the cursor stays put.</summary>
    private void SetZoom(float zoom, PointF anchor)
    {
        if (_image is null) return;

        float fit = Math.Min((float)Width / _size.Width, (float)Height / _size.Height);

        // Down to a quarter of fit — enough to pull back off the map — and up to 32x, which is
        // where the province raster's own pixels are the size of a fingertip.
        zoom = Math.Clamp(zoom, Math.Min(fit, 1f) / 4f, 32f);

        var before = ToImage(anchor);
        _zoom = zoom;
        _origin = new PointF(anchor.X - before.X * _zoom, anchor.Y - before.Y * _zoom);
        _fit = false;

        ClampOrigin();
        Invalidate();
        Announce(null);
    }

    /// <summary>
    /// Keeps the image from being dragged off screen entirely: it centres on any axis where it is
    /// smaller than the pane, and otherwise stays covering it.
    /// </summary>
    private void ClampOrigin()
    {
        if (_image is null) return;

        float w = _size.Width * _zoom, h = _size.Height * _zoom;

        _origin.X = w <= Width
            ? (Width - w) / 2f
            : Math.Clamp(_origin.X, Width - w, 0);
        _origin.Y = h <= Height
            ? (Height - h) / 2f
            : Math.Clamp(_origin.Y, Height - h, 0);
    }

    private PointF ToImage(PointF control)
        => new((control.X - _origin.X) / _zoom, (control.Y - _origin.Y) / _zoom);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_image is null) return;

        float step = e.Delta > 0 ? 1.25f : 1 / 1.25f;
        SetZoom(_zoom * step, e.Location);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button != MouseButtons.Left || _image is null) return;
        _dragging = true;
        _dragFrom = e.Location;
        _pressAt = e.Location;
        _moved = false;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            if (Math.Abs(e.X - _pressAt.X) > ClickSlop || Math.Abs(e.Y - _pressAt.Y) > ClickSlop)
                _moved = true;

            _origin.X += e.X - _dragFrom.X;
            _origin.Y += e.Y - _dragFrom.Y;
            _dragFrom = e.Location;
            _fit = false;
            ClampOrigin();
            Invalidate();
        }

        Announce(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        bool clicked = _dragging && !_moved && e.Button == MouseButtons.Left;

        _dragging = false;
        Cursor = Cursors.Default;

        if (!clicked || _image is null) return;

        var p = ToImage(e.Location);
        if (p.X >= 0 && p.Y >= 0 && p.X < _size.Width && p.Y < _size.Height)
            PixelClicked?.Invoke(new Point((int)p.X, (int)p.Y));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Announce(null);
    }

    /// <summary>Double-click toggles between filling the pane and the raster's own pixels.</summary>
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;

        if (_fit) SetZoom(1f, e.Location);
        else Fit();
    }

    private void Announce(Point? cursor)
    {
        Point? pixel = null;
        if (_image is not null && cursor is { } c)
        {
            var p = ToImage(c);
            if (p.X >= 0 && p.Y >= 0 && p.X < _size.Width && p.Y < _size.Height)
                pixel = new Point((int)p.X, (int)p.Y);
        }

        ViewChanged?.Invoke(_zoom, pixel);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Background);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        if (_image is null)
        {
            TextRenderer.DrawText(g, "Choose a heightmap and press Preview.",
                Font, ClientRectangle, Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // Nearest-neighbour once a source pixel covers more than one screen pixel; these views are
        // classifications and a blended class boundary is a colour that is on no map.
        g.InterpolationMode = _zoom >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;

        // The bitmap can still be disposed between the null check above and the draw — generation
        // replaces it from another thread — and GDI+ reports that as an ArgumentException rather
        // than anything more specific. Dropping the reference and repainting empty beats taking the
        // window down over a preview frame.
        try
        {
            g.DrawImage(_image,
                new RectangleF(_origin.X, _origin.Y, _size.Width * _zoom, _size.Height * _zoom));
        }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException)
        {
            _image = null;
            _size = Size.Empty;
            Console.WriteLine("Preview dropped a stale image: " + ex.Message);
        }
    }

    private void Export()
    {
        if (_image is null) return;

        using var dialog = new SaveFileDialog
        {
            Title = "Save this view",
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{ViewName.ToLowerInvariant()}.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // The rendered preview, which is the downsample the pane has been showing — not a re-render
        // at full map resolution. --out already writes those, and quietly handing back something
        // other than what is on screen from a button labelled "save view" would be a lie.
        _image.Save(dialog.FileName, ImageFormat.Png);
        Console.WriteLine($"Saved {ViewName} view to {dialog.FileName}");
    }

    private void Copy()
    {
        if (_image is not null) Clipboard.SetImage(_image);
    }
}
