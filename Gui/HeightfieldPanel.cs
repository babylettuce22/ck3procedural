using System.Drawing.Drawing2D;

namespace Ck3MapGen.Gui;

/// <summary>
/// The interactive 3D view of a loaded heightmap: drag to orbit, right-drag to pan, wheel to zoom.
///
/// Rendering runs off the UI thread, at half resolution while the mouse is down and at full
/// resolution once it settles. A frame at half resolution costs a quarter as much, which is what
/// makes a software renderer feel like a viewport rather than like a slideshow; the sharp frame
/// arrives a few tens of milliseconds after the drag stops and nobody sees the seam.
///
/// Only one render is ever in flight. Requests that arrive during one are collapsed into a single
/// "do it again when you land" flag, so spinning the wheel or throwing the mouse across the control
/// queues one more frame rather than a hundred.
/// </summary>
public sealed class HeightfieldPanel : Control
{
    private Heightfield? _source;
    private Heightfield? _packed;
    private HeightfieldView _view = HeightfieldView.Default;

    private Bitmap? _frame;
    private bool _running;
    private bool _dirty;
    private bool _draft;

    private Point _drag;
    private MouseButtons _dragging;

    private string _empty = "Choose a heightmap to see it in 3D.";

    /// <summary>Show the packer's output — what CK3 will actually draw — instead of the source.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowAsCk3Renders { get; set; }

    /// <summary>Raised when the view changes, so a host can show the camera state.</summary>
    public event Action<HeightfieldView>? ViewChanged;

    public HeightfieldPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(24, 28, 38);
        TabStop = true;

        // Wired here rather than in OnHandleCreated, which runs again on every handle recreation
        // and would stack a second subscription each time.
        _sharpen.Tick += (_, _) =>
        {
            _sharpen.Stop();
            if (_source is not null && _dragging == MouseButtons.None) Request(draft: false);
        };
    }

    /// <summary>
    /// Hands over a newly loaded heightmap. Both fields are set together: the packed one is built
    /// by the caller off the UI thread, because <see cref="Emit.HeightmapPacker.Reconstruct"/> on a
    /// full-size map is a second or two of work and doing it here would freeze the window.
    /// </summary>
    public void SetField(Heightfield? source, Heightfield? packed, string emptyMessage)
    {
        _source = source;
        _packed = packed;
        _empty = emptyMessage;

        if (source is null)
        {
            _frame?.Dispose();
            _frame = null;
            Invalidate();
            return;
        }

        Request(draft: false);
    }

    public HeightfieldView View => _view;

    /// <summary>
    /// Drafted, because this is driven by a slider: a full-resolution frame per tick of the drag
    /// would queue up behind the drag and the handle would stutter.
    /// </summary>
    public void SetExaggeration(double value)
    {
        _view = _view with { Exaggeration = value };
        Sharpen();
    }

    /// <summary>Draft now, sharp once the input settles.</summary>
    private void Sharpen()
    {
        Request(draft: true);
        _sharpen.Stop();
        _sharpen.Start();
    }

    public void ResetView()
    {
        _view = HeightfieldView.Default with { Exaggeration = _view.Exaggeration };
        Request(draft: false);
        ViewChanged?.Invoke(_view);
    }

    private Heightfield? Active => ShowAsCk3Renders ? _packed ?? _source : _source;

    public void Refresh3d() => Request(draft: false);

    private void Request(bool draft)
    {
        _dirty = true;
        _draft = draft;
        Pump();
    }

    private void Pump()
    {
        if (_running || !_dirty) return;

        var field = Active;
        if (field is null || Width < 24 || Height < 24) return;

        _dirty = false;
        _running = true;

        var view = _view;
        int scale = _draft ? 2 : 1;
        int w = Math.Max(24, Width / scale);
        int h = Math.Max(24, Height / scale);

        Task.Run(() => HeightfieldRenderer.Render(field, view, w, h))
            .ContinueWith(task =>
            {
                _running = false;

                if (task.IsCompletedSuccessfully)
                {
                    _frame?.Dispose();
                    _frame = PreviewRenderer.ToBitmap(task.Result);
                    Invalidate();
                }

                Pump();
            }, CancellationToken.None, TaskContinuationOptions.None,
               TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_source is not null) Request(draft: true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _drag = e.Location;
        _dragging = e.Button;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging == MouseButtons.None || _source is null) return;

        int dx = e.X - _drag.X, dy = e.Y - _drag.Y;
        if (dx == 0 && dy == 0) return;
        _drag = e.Location;

        if (_dragging == MouseButtons.Left)
        {
            _view = _view.Orbited(-dx * 0.006, -dy * 0.004);
        }
        else
        {
            // Scaled by the zoom so a drag moves the same distance across the screen however far
            // in the camera is, and divided by the pitch because the ground is foreshortened
            // vertically — without that, a pan feels sluggish looking down and wild looking along.
            double reach = _view.Distance * 1.1;
            double squash = Math.Clamp(Math.Sin(_view.Pitch), 0.4, 1.0);

            // Both signs move the ground with the cursor. Forward is +dy, not -dy: nearer ground
            // projects lower on the screen, so dragging down has to bring the camera forward for
            // the map to follow the mouse rather than run away from it.
            _view = _view.Panned(-dx / (double)Width * reach,
                                  dy / (double)Height * reach / squash);
        }

        ViewChanged?.Invoke(_view);
        Request(draft: true);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging == MouseButtons.None) return;

        _dragging = MouseButtons.None;
        if (_source is not null) Request(draft: false);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_source is null) return;

        _view = _view.Zoomed(e.Delta > 0 ? 0.88 : 1.0 / 0.88);
        ViewChanged?.Invoke(_view);

        // The wheel arrives in bursts, and a full-resolution frame per notch is what makes a zoom
        // feel like it is fighting back.
        Sharpen();
    }

    private readonly System.Windows.Forms.Timer _sharpen = new() { Interval = 180 };

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        ResetView();
    }

    protected override bool IsInputKey(Keys key) => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        if (_frame is null)
        {
            g.Clear(BackColor);
            TextRenderer.DrawText(g, _empty, Theme.Ui, ClientRectangle,
                Color.FromArgb(150, 158, 172),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // Draft frames are half size, so they are stretched back up here rather than re-rendered.
        g.InterpolationMode = _frame.Width < Width
            ? InterpolationMode.HighQualityBilinear
            : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        g.DrawImage(_frame, new Rectangle(0, 0, Width, Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _frame?.Dispose();
            _sharpen.Dispose();
        }
        base.Dispose(disposing);
    }
}
