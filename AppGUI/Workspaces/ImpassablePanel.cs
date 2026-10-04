using System.Drawing.Imaging;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The Masks tab's impassable page: paint where the impassable mountains go, over the heightmap.
///
/// Three states, as in the file: wall (white), passable (black) and automatic (transparent). In
/// <see cref="ImpassablePaintMode.Manual"/> automatic means passable; in
/// <see cref="ImpassablePaintMode.ManualPlusAuto"/> the auto-cut decides it, and the canvas can
/// show where it would put its walls so the paint can add to them or veto them. The mode lives on
/// the config (<see cref="MapConfig.ImpassablePaintMode"/>) so presets and the command line share
/// it; the host keeps the two in step.
///
/// As with the climate page, a page never painted on changes nothing: <see cref="EffectivePaint"/>
/// is null until a stroke lands.
/// </summary>
public sealed class ImpassablePanel : UserControl
{
    private enum Tool { Wall, Passable, Auto }

    // ------------------------------------------------------------------ state

    private ClimatePanel.Terrain? _terrain;
    private ImpassablePaint? _paint;
    private ImpassablePaint? _pending;     // loaded before the terrain was known; adopted when it is
    private readonly ImpassableHistory _history = new();
    private ImpassablePaintMode _mode = ImpassablePaintMode.Manual;

    private Tool _tool = Tool.Wall;
    private ImpassableStroke? _stroke;

    private byte[]? _baseRgb;              // shaded relief at paint resolution
    private Bitmap? _bitmap;

    private bool[]? _autoWalls;            // the auto-cut at paint resolution
    private string? _autoKey;
    private int _modelVersion;
    private int _autoGeneration;
    private bool _autoRunning;

    // ------------------------------------------------------------------ controls

    private readonly ImageView _canvas = new() { Dock = DockStyle.Fill, ViewName = "impassable" };
    private readonly Button _wall = Theme.MakeButton("Wall", 200);
    private readonly Button _passable = Theme.MakeButton("Passable", 200);
    private readonly Button _auto = Theme.MakeButton("Automatic (erase)", 200);
    private readonly TrackBar _size = new() { Minimum = 1, Maximum = 200, Value = 12, TickStyle = TickStyle.None, Width = Dpi.S(190), AutoSize = false, Height = Dpi.S(26) };
    private readonly RadioButton _manual = new() { Text = "Manual", AutoSize = true, Margin = Dpi.Pad(8, 4, 0, 0) };
    private readonly RadioButton _manualPlusAuto = new() { Text = "Manual + auto", AutoSize = true, Margin = Dpi.Pad(8, 2, 0, 0) };
    private readonly CheckBox _showAuto = new() { Text = "Show automatic walls", AutoSize = true, Checked = true, Margin = Dpi.Pad(8, 6, 0, 4) };
    private readonly Button _undo = Theme.MakeButton("Undo", 62);
    private readonly Button _redo = Theme.MakeButton("Redo", 62);
    private readonly Button _clear = Theme.MakeButton("Clear all", 70);
    private readonly Button _import = Theme.MakeButton("Import…", 96);
    private readonly Button _export = Theme.MakeButton("Export…", 96);
    private readonly Label _status;
    private readonly Label _readout;
    private readonly WrappingToolTip _tips = new() { AutoPopDelay = 20000, InitialDelay = 400 };
    private readonly SplitContainer _split;
    private bool _splitPlaced;
    private bool _syncingMode;

    // Display colours: white walls would vanish against the relief's pale peaks.
    private static readonly (byte R, byte G, byte B) WallColour = (214, 48, 49);
    private static readonly (byte R, byte G, byte B) PassableColour = (40, 170, 90);
    private static readonly (byte R, byte G, byte B) AutoWallColour = (240, 150, 40);

    private static int LeftWidth => Dpi.S(236);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_splitPlaced || _split is null || Width < LeftWidth * 2) return;
        _splitPlaced = true;
        _split.SplitterDistance = LeftWidth;
    }

    /// <summary>Where the import/export dialogs open. The host persists it.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? PaintDir { get; set; }

    /// <summary>The paint changed — anything that changes what generation will get.</summary>
    public event Action? PaintChanged;

    /// <summary>The mode switch was flipped here; the host writes it to the config.</summary>
    public event Action<ImpassablePaintMode>? ModeChanged;

    public ImpassablePanel()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Dpi.S(24),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = Dpi.Pad(8, 0, 8, 0),
            BackColor = Theme.Surface,
            ForeColor = Theme.TextDim,
            AutoEllipsis = true,
            Text = "Choose a heightmap first (World ▸ Choose heightmap, or Terrain) — walls are painted over it.",
        };

        _readout = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Dpi.S(22),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = Dpi.Pad(8, 0, 8, 0),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            AutoEllipsis = true,
        };

        var split = _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, BackColor = Theme.Border };
        split.Panel1.BackColor = Theme.Surface;
        split.Panel2.BackColor = Theme.Background;
        split.Panel1.Controls.Add(BuildLeft());

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        host.Controls.Add(_canvas);
        host.Controls.Add(_status);
        host.Controls.Add(_readout);
        split.Panel2.Controls.Add(host);

        Controls.Add(split);
        _canvas.EmptyText = "Choose a heightmap, then come back here to paint its impassable walls.";
        _canvas.Mode = ImageView.Interaction.Paint;
        _canvas.StrokeBegan += OnStrokeBegan;
        _canvas.StrokeMoved += OnStrokeMoved;
        _canvas.StrokeEnded += OnStrokeEnded;
        _canvas.Overlay += DrawOverlay;
        _canvas.ViewChanged += (_, cursor) => UpdateReadout(cursor);

        SelectTool(Tool.Wall);
        SyncModeButtons();
        UpdateHistoryButtons();
    }

    // ------------------------------------------------------------------ layout

    private Control BuildLeft()
    {
        var column = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface };

        // Docked Top controls stack in reverse order of addition; build bottom-up.
        var file = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Dpi.Pad(4, 2, 4, 6) };
        file.Controls.Add(_import);
        file.Controls.Add(_export);
        _tips.SetToolTip(_import, "Load a mask PNG: white = wall, black = passable, transparent = automatic");
        _tips.SetToolTip(_export, "Save the paint as a PNG — usable with --impassable-mask on the command line");
        _import.Click += (_, _) => ImportPaint();
        _export.Click += (_, _) => ExportPaint();

        var history = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Dpi.Pad(4, 4, 4, 2) };
        history.Controls.Add(_undo);
        history.Controls.Add(_redo);
        history.Controls.Add(_clear);
        _undo.Click += (_, _) => ApplyHistory(undo: true);
        _redo.Click += (_, _) => ApplyHistory(undo: false);
        _clear.Click += (_, _) => ClearPaint();
        _tips.SetToolTip(_undo, "Undo the last stroke (Ctrl+Z)");
        _tips.SetToolTip(_redo, "Redo (Ctrl+Y)");
        _tips.SetToolTip(_clear, "Erase every stroke, back to automatic everywhere");

        var mode = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = Dpi.Pad(6, 4, 6, 0) };
        mode.Controls.Add(Section("Mode"));
        mode.Controls.Add(_manual);
        mode.Controls.Add(_manualPlusAuto);
        mode.Controls.Add(_showAuto);
        _tips.SetToolTip(_manual, "Only your walls. Unpainted ground is passable and the automatic walls do not run.");
        _tips.SetToolTip(_manualPlusAuto, "The automatic walls fill in unpainted ground; your walls add to them and passable paint removes them.");
        _tips.SetToolTip(_showAuto, "Show where the automatic cut would put walls (Manual + auto only). Approximate: the run carves rivers first.");
        _manual.CheckedChanged += (_, _) => OnModeButton();
        _manualPlusAuto.CheckedChanged += (_, _) => OnModeButton();
        _showAuto.CheckedChanged += (_, _) => { if (_showAuto.Checked) QueueAuto(); RecompositeAll(); };

        var brush = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = Dpi.Pad(6, 4, 6, 0) };
        brush.Controls.Add(Caption("Size"));
        brush.Controls.Add(_size);
        _tips.SetToolTip(_size, "Brush radius, as a share of the map's height ( [ and ] )");
        _size.ValueChanged += (_, _) => _canvas.Invalidate();

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = Dpi.Pad(6, 2, 6, 0) };
        tools.Controls.Add(Section("Brush"));
        foreach (var (button, tool, colour, tip) in new[]
        {
            (_wall, Tool.Wall, WallColour, "Impassable, whatever the terrain. White in the saved file."),
            (_passable, Tool.Passable, PassableColour, "Never impassable — vetoes the automatic walls. Black in the saved file."),
            (_auto, Tool.Auto, ((byte)0, (byte)0, (byte)0), "Back to unpainted: the mode decides. Transparent in the saved file."),
        })
        {
            var captured = tool;
            var c = colour;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = Dpi.Pad(30, 0, 0, 0);
            if (tool != Tool.Auto) button.Paint += (s, e) => PaintSwatch((Button)s!, e.Graphics, c);
            button.Click += (_, _) => SelectTool(captured);
            _tips.SetToolTip(button, tip);
            tools.Controls.Add(button);
        }

        var hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = Dpi.S(66),
            Padding = Dpi.Pad(8, 6, 8, 0),
            ForeColor = Theme.TextDim,
            Text = "Paint walls where provinces should be impassable, and passable ground where they must not be. Paint at least a barony wide.",
        };

        column.Controls.Add(file);
        column.Controls.Add(history);
        column.Controls.Add(mode);
        column.Controls.Add(brush);
        column.Controls.Add(tools);
        column.Controls.Add(hint);
        return column;
    }

    private static void PaintSwatch(Button button, Graphics g, (byte R, byte G, byte B) colour)
    {
        using var fill = new SolidBrush(Color.FromArgb(colour.R, colour.G, colour.B));
        var square = new Rectangle(Dpi.S(6), (button.Height - Dpi.S(16)) / 2, Dpi.S(18), Dpi.S(16));
        g.FillRectangle(fill, square);
        using var pen = new Pen(Color.FromArgb(90, 0, 0, 0));
        g.DrawRectangle(pen, square);
    }

    private static Label Section(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.UiBold,
        ForeColor = Theme.Accent,
        Margin = Dpi.Pad(0, 6, 0, 2),
        Padding = Dpi.Pad(2, 0, 0, 0),
    };

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.TextDim,
        Margin = Dpi.Pad(4, 7, 3, 0),
    };

    // ------------------------------------------------------------------ host API

    /// <summary>Paint as generation should receive it: null when nothing is painted.</summary>
    public ImpassablePaint? EffectivePaint => Paint is { IsEmpty: false } p ? p : null;

    /// <summary>The paint itself. Null before any terrain or file has sized it.</summary>
    public ImpassablePaint? Paint => _paint ?? _pending;

    public bool HasPaint => Paint is { IsEmpty: false };

    /// <summary>Set by the host from the config; setting it raises no <see cref="ModeChanged"/>.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ImpassablePaintMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            SyncModeButtons();
            AfterModeChanged();
        }
    }

    public void SetTerrain(ClimatePanel.Terrain? terrain)
    {
        _terrain = terrain;
        _autoWalls = null;
        _autoKey = null;

        if (terrain is null)
        {
            _baseRgb = null;
            ReplaceBitmap(null);
            _canvas.SetImage(null);
            _status.Text = "Choose a heightmap first (World ▸ Choose heightmap, or Terrain) — walls are painted over it.";
            return;
        }

        var cfg = terrain.Config;
        var (w, h) = ImpassablePaint.SizeFor(cfg.ProvinceWidth, cfg.ProvinceHeight);

        string note = "";
        var source = _paint ?? _pending;
        if (source is null)
        {
            _paint = new ImpassablePaint(w, h);
        }
        else if (source.Width != w || source.Height != h)
        {
            _paint = source.Resampled(w, h);
            if (!source.IsEmpty) note = $" — paint resampled from {source.Width}×{source.Height} to fit this map";
        }
        else
        {
            _paint = source;
        }
        _pending = null;
        _history.Clear();
        UpdateHistoryButtons();

        _baseRgb = ClimatePanel.ShadedRelief(terrain, w, h);
        ReplaceBitmap(new Bitmap(w, h, PixelFormat.Format32bppArgb));
        RecompositeAll();
        _canvas.SetImage(_bitmap);
        _canvas.Mode = ImageView.Interaction.Paint;
        _status.Text = $"Painting over {cfg.ProvinceWidth}×{cfg.ProvinceHeight} at {w}×{h}{note}.";

        QueueAuto();
        PaintChanged?.Invoke();
    }

    /// <summary>The settings changed: the automatic walls preview is stale.</summary>
    public void InvalidateModel()
    {
        _modelVersion++;
        if (_terrain is not null && Visible) QueueAuto();
    }

    /// <summary>The page came back on screen with the terrain it already had.</summary>
    public void Activated() => QueueAuto();

    /// <summary>Takes a paint loaded from disk. Replacing what was on the page is one undoable step.</summary>
    public void AdoptPaint(ImpassablePaint paint, string note)
    {
        if (_terrain is null || _paint is null)
        {
            _pending = paint;
            _status.Text = note;
            PaintChanged?.Invoke();
            return;
        }

        var incoming = paint.Width == _paint.Width && paint.Height == _paint.Height
            ? paint
            : paint.Resampled(_paint.Width, _paint.Height);

        var before = (byte[])_paint.Cells.Clone();
        Array.Copy(incoming.Cells, _paint.Cells, _paint.Cells.Length);
        _paint.Touch();
        _history.Push(ImpassableEdit.Capture(_paint, before, new Rectangle(0, 0, _paint.Width, _paint.Height)));

        _status.Text = note;
        AfterPaintChanged(new Rectangle(0, 0, _paint.Width, _paint.Height));
    }

    /// <summary>Erases everything, as an undoable step.</summary>
    public void ClearPaint()
    {
        if (_paint is null || _paint.IsEmpty)
        {
            if (_pending is not null) { _pending = null; PaintChanged?.Invoke(); }
            return;
        }

        var edit = ImpassableEdit.Fill(_paint, ImpassableMask.Auto);
        edit.Redo(_paint);
        _history.Push(edit);
        _status.Text = "Cleared — automatic everywhere. Undo brings the paint back.";
        AfterPaintChanged(new Rectangle(0, 0, _paint.Width, _paint.Height));
    }

    /// <summary>Saves the paint to <paramref name="path"/>. False when there is nothing to save.</summary>
    public bool SavePaint(string path)
    {
        var paint = Paint;
        if (paint is null || paint.IsEmpty) return false;
        paint.Save(path);
        return true;
    }

    public bool HandleKey(Keys key)
    {
        switch (key)
        {
            case Keys.OemOpenBrackets:
                _size.Value = Math.Max(_size.Minimum, _size.Value - 2);
                return true;
            case Keys.OemCloseBrackets:
                _size.Value = Math.Min(_size.Maximum, _size.Value + 2);
                return true;
            case Keys.Control | Keys.Z:
                ApplyHistory(undo: true);
                return true;
            case Keys.Control | Keys.Y:
                ApplyHistory(undo: false);
                return true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ tools and mode

    private void SelectTool(Tool tool)
    {
        _tool = tool;
        foreach (var (button, t) in new[] { (_wall, Tool.Wall), (_passable, Tool.Passable), (_auto, Tool.Auto) })
        {
            bool active = t == tool;
            button.FlatAppearance.BorderColor = active ? Theme.Accent : Theme.Border;
            button.FlatAppearance.BorderSize = active ? 2 : 1;
            button.BackColor = active ? Theme.SurfaceHigh : Theme.Surface;
            button.Font = active ? Theme.UiBold : Theme.Ui;
        }
        _canvas.Invalidate();
    }

    private void SyncModeButtons()
    {
        _syncingMode = true;
        _manual.Checked = _mode == ImpassablePaintMode.Manual;
        _manualPlusAuto.Checked = _mode == ImpassablePaintMode.ManualPlusAuto;
        _showAuto.Enabled = _mode == ImpassablePaintMode.ManualPlusAuto;
        _syncingMode = false;
    }

    private void OnModeButton()
    {
        if (_syncingMode) return;
        var next = _manualPlusAuto.Checked ? ImpassablePaintMode.ManualPlusAuto : ImpassablePaintMode.Manual;
        if (next == _mode) return;
        _mode = next;
        _showAuto.Enabled = _mode == ImpassablePaintMode.ManualPlusAuto;
        ModeChanged?.Invoke(next);
        AfterModeChanged();
    }

    private void AfterModeChanged()
    {
        QueueAuto();
        RecompositeAll();
        PaintChanged?.Invoke();
    }

    private bool ShowingAuto => _mode == ImpassablePaintMode.ManualPlusAuto && _showAuto.Checked;

    // ------------------------------------------------------------------ painting

    private float RadiusPixels => _paint is null ? 1f : _size.Value / 1000f * _paint.Height;

    private byte ToolValue => _tool switch
    {
        Tool.Wall => ImpassableMask.Wall,
        Tool.Passable => ImpassableMask.Passable,
        _ => ImpassableMask.Auto,
    };

    private void OnStrokeBegan(PointF imagePoint)
    {
        if (_paint is null || _terrain is null) return;
        _stroke = new ImpassableStroke(_paint, ToolValue, RadiusPixels);
        Dab(imagePoint);
    }

    private void OnStrokeMoved(PointF imagePoint)
    {
        if (_stroke is not null) Dab(imagePoint);
    }

    private void Dab(PointF imagePoint)
    {
        if (_stroke?.MoveTo(imagePoint.X, imagePoint.Y) is { } rect) Composite(rect);
        _canvas.Invalidate();
    }

    private void OnStrokeEnded()
    {
        if (_stroke is null) return;
        var edit = _stroke.End();
        _stroke = null;
        if (edit is null) { _canvas.Invalidate(); return; }

        _history.Push(edit);
        AfterPaintChanged(edit.Area);
    }

    private void ApplyHistory(bool undo)
    {
        if (_paint is null) return;
        if (!(undo ? _history.Undo(_paint) : _history.Redo(_paint))) return;
        AfterPaintChanged(new Rectangle(0, 0, _paint.Width, _paint.Height));
    }

    private void UpdateHistoryButtons()
    {
        _undo.Enabled = _history.CanUndo;
        _redo.Enabled = _history.CanRedo;
        _clear.Enabled = HasPaint;
    }

    private void AfterPaintChanged(Rectangle area)
    {
        UpdateHistoryButtons();
        Composite(area);
        _canvas.Invalidate();
        PaintChanged?.Invoke();
    }

    // ------------------------------------------------------------------ canvas image

    private void RecompositeAll()
    {
        if (_paint is not null) Composite(new Rectangle(0, 0, _paint.Width, _paint.Height));
        _canvas.Invalidate();
    }

    /// <summary>Recomposites the paint, and the automatic walls under it, over the relief inside <paramref name="area"/>.</summary>
    private void Composite(Rectangle area)
    {
        if (_paint is null || _bitmap is null || _baseRgb is null) return;

        int w = _paint.Width, h = _paint.Height;
        area.Intersect(new Rectangle(0, 0, w, h));
        if (area.Width <= 0 || area.Height <= 0) return;

        var auto = ShowingAuto && _autoWalls is { } a && a.Length == _paint.Cells.Length ? a : null;
        bool manualOnly = _mode == ImpassablePaintMode.Manual;

        var data = _bitmap.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[area.Width * 4];
            for (int y = 0; y < area.Height; y++)
            {
                int py = area.Y + y;
                for (int x = 0; x < area.Width; x++)
                {
                    int i = py * w + area.X + x;
                    float r = _baseRgb[i * 3], g = _baseRgb[i * 3 + 1], b = _baseRgb[i * 3 + 2];

                    (byte R, byte G, byte B)? tint = null;
                    float alpha = 0f;
                    switch (_paint.Cells[i])
                    {
                        case ImpassableMask.Wall:
                            tint = WallColour; alpha = 0.72f;
                            break;
                        case ImpassableMask.Passable:
                            // In Manual mode passable paint changes nothing; show it faintly.
                            tint = PassableColour; alpha = manualOnly ? 0.25f : 0.5f;
                            break;
                        default:
                            if (auto is not null && auto[i]) { tint = AutoWallColour; alpha = 0.45f; }
                            break;
                    }

                    if (tint is { } c)
                    {
                        r += (c.R - r) * alpha; g += (c.G - g) * alpha; b += (c.B - b) * alpha;
                    }

                    row[x * 4] = (byte)b; row[x * 4 + 1] = (byte)g; row[x * 4 + 2] = (byte)r; row[x * 4 + 3] = 255;
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }
    }

    private void ReplaceBitmap(Bitmap? next)
    {
        var old = _bitmap;
        _bitmap = next;
        old?.Dispose();
    }

    private void DrawOverlay(Graphics g, float zoom)
    {
        if (_paint is null || _canvas.CursorAt is not { } cursor) return;

        float radius = RadiusPixels * zoom;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var ring = new Pen(Color.FromArgb(220, 255, 255, 255), 1.5f);
        using var shadow = new Pen(Color.FromArgb(120, 0, 0, 0), 3.5f);
        g.DrawEllipse(shadow, cursor.X - radius, cursor.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(ring, cursor.X - radius, cursor.Y - radius, radius * 2, radius * 2);
    }

    private void UpdateReadout(Point? cursor)
    {
        if (_paint is null || cursor is not { } at)
        {
            _readout.Text = "";
            return;
        }

        var p = _canvas.ToImagePoint(at);
        int x = (int)p.X, y = (int)p.Y;
        if (x < 0 || y < 0 || x >= _paint.Width || y >= _paint.Height)
        {
            _readout.Text = "";
            return;
        }

        int i = y * _paint.Width + x;
        bool autoWall = _autoWalls is { } a && a.Length == _paint.Cells.Length && a[i];
        _readout.Text = _paint.Cells[i] switch
        {
            ImpassableMask.Wall => "Painted: wall",
            ImpassableMask.Passable => _mode == ImpassablePaintMode.Manual
                ? "Painted: passable (same as unpainted in Manual mode)"
                : "Painted: passable" + (autoWall ? " — vetoes an automatic wall" : ""),
            _ => _mode == ImpassablePaintMode.Manual
                ? "Unpainted: passable"
                : "Unpainted: automatic" + (_autoWalls is null ? "" : autoWall ? " — wall" : " — passable"),
        };
    }

    // ------------------------------------------------------------------ automatic walls preview

    private void QueueAuto()
    {
        if (!ShowingAuto || _terrain is null || _paint is null) return;
        string key = $"{_terrain.Stamp}|{_modelVersion}|{_paint.Width}x{_paint.Height}";
        if (key == _autoKey || _autoRunning) return;
        RunAutoAsync(key).Forget("impassable auto preview");
    }

    /// <summary>
    /// The auto-cut on the terrain this page was given. The run cuts after the major rivers are
    /// carved, so this is close but not exact near them.
    /// </summary>
    private async Task RunAutoAsync(string key)
    {
        var terrain = _terrain!;
        var cfg = terrain.Config;
        int w = _paint!.Width, h = _paint.Height;
        int pw = cfg.ProvinceWidth, ph = cfg.ProvinceHeight;
        int generation = ++_autoGeneration;

        _autoRunning = true;
        _status.Text = "Finding the automatic walls…";
        try
        {
            var walls = await Task.Run(() =>
            {
                if (terrain.ProvinceElevation.Length != pw * ph) return null;
                if (ImpassableAutoCut.Build(terrain.LandMask, terrain.ProvinceElevation, pw, ph, cfg) is not { } cut)
                    return Array.Empty<bool>();

                var sampled = new bool[w * h];
                Parallel.For(0, h, y =>
                {
                    int sy = Math.Min(ph - 1, (int)(((long)y * 2 + 1) * ph / (2L * h)));
                    for (int x = 0; x < w; x++)
                    {
                        int sx = Math.Min(pw - 1, (int)(((long)x * 2 + 1) * pw / (2L * w)));
                        sampled[y * w + x] = cut.Mask[sy * pw + sx];
                    }
                });
                return sampled;
            });

            if (generation != _autoGeneration || IsDisposed || walls is null) return;

            _autoKey = key;
            _autoWalls = walls.Length == 0 ? new bool[w * h] : walls;
            _status.Text = walls.Length == 0
                ? "The automatic cut finds no walls on this map — only painted walls will be impassable."
                : "Automatic walls shown in orange. Paint walls to add to them, passable to remove them.";
            RecompositeAll();
            UpdateReadout(_canvas.CursorAt);
        }
        catch (Exception ex)
        {
            if (generation != _autoGeneration || IsDisposed) return;
            Console.WriteLine($"Impassable preview failed: {ex}");
            _status.Text = $"Automatic walls preview failed: {ex.Message}";
        }
        finally
        {
            _autoRunning = false;
        }

        // Settings or terrain moved while it ran.
        QueueAuto();
    }

    // ------------------------------------------------------------------ files

    private void ImportPaint()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import impassable mask",
            Filter = "Impassable mask (*.png)|*.png|All files (*.*)|*.*",
            InitialDirectory = PaintDir ?? "",
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var paint = ImpassablePaint.Load(dialog.FileName);
            PaintDir = Path.GetDirectoryName(dialog.FileName);
            AdoptPaint(paint, $"Imported {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), $"Could not read {dialog.FileName}:\n\n{ex.Message}",
                "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExportPaint()
    {
        if (!HasPaint)
        {
            _status.Text = "Nothing painted yet — nothing to export.";
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Export impassable mask",
            Filter = "Impassable mask (*.png)|*.png",
            FileName = "impassable.png",
            InitialDirectory = PaintDir ?? "",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            SavePaint(dialog.FileName);
            PaintDir = Path.GetDirectoryName(dialog.FileName);
            _status.Text = $"Exported to {Path.GetFileName(dialog.FileName)} — pass it to --impassable-mask to build from a terminal.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), $"Could not write {dialog.FileName}:\n\n{ex.Message}",
                "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
            _bitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
