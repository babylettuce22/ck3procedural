using System.Drawing.Imaging;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The Climate tab: paint the climate you want over the heightmap, in Koppen-map colours, and
/// watch the model's prediction follow.
///
/// The palette on the left is a set of climate *profiles* — see <see cref="ClimateBrush"/> — not
/// temperature sliders; the canvas on the right shows the heightmap as shaded relief with the
/// paint washed over it, or the climate the model now predicts, or an approximate landscape. The
/// distinction between the three views is the point of having them: the paint is what was asked
/// for, the prediction is what blended edges and elevation make of it, and seeing both is what
/// makes the second understandable.
///
/// Nothing here runs until the tab is opened, and a tab that is never painted on changes nothing:
/// <see cref="EffectivePaint"/> is null until a stroke lands, and the host passes exactly that to
/// the generator. The "Use automatic climate" switch bypasses the paint without discarding it.
///
/// The prediction is computed off the UI thread after each stroke, with <see cref="ClimateModel.Base"/>
/// cached per heightmap and settings so that only the paint-dependent half of the model re-runs.
/// </summary>
public sealed class ClimatePanel : UserControl
{
    /// <summary>What the tab paints over: the heightmap at province resolution, and the settings it was read under.</summary>
    public sealed record Terrain(MapConfig Config, float[] ProvinceElevation, byte[] LandMask, string Stamp);

    public enum View { Paint, Climate, Landscape }

    // ------------------------------------------------------------------ state

    private Terrain? _terrain;
    private ClimatePaint? _paint;
    private ClimatePaint? _pending;        // loaded before the terrain was known; adopted when it is
    private readonly ClimateHistory _history = new();

    private ClimateModel.Base? _base;
    private string? _baseKey;
    private int _modelVersion;
    private AzgaarImport? _azgaar;
    private string? _azgaarPath;

    private View _view = View.Paint;
    private int _brush;                    // index into ClimateBrush.All, or -1 for the eraser
    private bool _showAll;

    private ClimateStroke? _stroke;
    private readonly List<PointF> _strokePath = [];

    private byte[]? _baseRgb;              // shaded relief at paint resolution
    private Bitmap? _paintBitmap;
    private Bitmap? _climateBitmap;
    private Bitmap? _landscapeBitmap;
    private KoppenClass[]? _predicted;     // at paint resolution
    private TerrainClass[]? _landscape;    // at paint resolution
    private int _predictedVersion = -1, _landscapeVersion = -1;
    private bool _predictedAutomatic;

    private int _previewGeneration;
    private bool _previewRunning;
    private bool _previewDirty;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 220 };

    // ------------------------------------------------------------------ controls

    private readonly ImageView _canvas = new() { Dock = DockStyle.Fill, ViewName = "climate" };
    private readonly FlowLayoutPanel _palette = new()
    {
        Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 2, 6, 2),
    };
    private readonly List<Button> _swatches = [];
    private readonly Button _more = Theme.MakeButton("More climates ▾", 200);
    private readonly Button _erase = Theme.MakeButton("Restore automatic", 200);
    private readonly TrackBar _size = new() { Minimum = 4, Maximum = 300, Value = 40, TickStyle = TickStyle.None, Width = 190, AutoSize = false, Height = 26 };
    private readonly TrackBar _strength = new() { Minimum = 5, Maximum = 100, Value = 100, TickStyle = TickStyle.None, Width = 190, AutoSize = false, Height = 26 };
    private readonly TrackBar _softness = new() { Minimum = 0, Maximum = 100, Value = 75, TickStyle = TickStyle.None, Width = 190, AutoSize = false, Height = 26 };
    private readonly Button _undo = Theme.MakeButton("Undo", 62);
    private readonly Button _redo = Theme.MakeButton("Redo", 62);
    private readonly Button _clear = Theme.MakeButton("Clear all", 70);
    private readonly CheckBox _automatic = new() { Text = "Use automatic climate", AutoSize = true, Margin = new Padding(8, 8, 0, 4) };
    private readonly Button _import = Theme.MakeButton("Import…", 96);
    private readonly Button _export = Theme.MakeButton("Export…", 96);
    private readonly Label _hint;
    private readonly Label _status;
    private readonly Label _readout;
    private readonly Label _viewNote;
    private readonly Dictionary<View, Button> _viewButtons = [];
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000, InitialDelay = 400 };
    private readonly SplitContainer _split;
    private bool _splitPlaced;

    /// <summary>The palette column, in pixels. Wide enough for the longest brush name and the sliders.</summary>
    private const int LeftWidth = 236;

    /// <summary>
    /// The splitter is placed here, not in the constructor: a SplitContainer clamps its distance
    /// to the width it has at the time, and at construction that is the default 150, which left
    /// the palette a third as wide as asked for.
    /// </summary>
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

    /// <summary>The paint changed, or the automatic switch did — anything that changes what generation will get.</summary>
    public event Action? PaintChanged;

    public ClimatePanel()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;

        _hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 66,
            Padding = new Padding(8, 6, 8, 0),
            ForeColor = Theme.TextDim,
            Text = "Choose a climate and paint a region. Climate follows your paint; elevation adds local variation.",
        };

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
            BackColor = Theme.Surface,
            ForeColor = Theme.TextDim,
            AutoEllipsis = true,
            Text = "Choose a heightmap first — the Climate tab paints over it.",
        };

        _readout = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            AutoEllipsis = true,
        };

        _viewNote = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextDim,
            Margin = new Padding(12, 8, 0, 0),
        };

        var split = _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            BackColor = Theme.Border,
        };
        split.Panel1.BackColor = Theme.Surface;
        split.Panel2.BackColor = Theme.Background;

        split.Panel1.Controls.Add(BuildLeft());
        split.Panel2.Controls.Add(BuildRight());

        Controls.Add(split);
        _canvas.EmptyText = "Choose a heightmap, then open this tab to paint its climate.";

        WireCanvas();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RunPreviewAsync(); };

        SelectBrush(FindBrush(KoppenClass.Oceanic));
        SelectView(View.Paint);
        UpdateHistoryButtons();
    }

    // ------------------------------------------------------------------ layout

    private Control BuildLeft()
    {
        var column = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface };

        // Docked Top controls stack in reverse order of addition; build bottom-up.
        var file = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 2, 4, 6) };
        file.Controls.Add(_import);
        file.Controls.Add(_export);
        _tips.SetToolTip(_import, "Load a climate paint PNG saved from this tab or beside a preset");
        _tips.SetToolTip(_export, "Save the paint as a PNG — usable with --climate-paint on the command line");
        _import.Click += (_, _) => ImportPaint();
        _export.Click += (_, _) => ExportPaint();

        var automatic = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0) };
        automatic.Controls.Add(_automatic);
        _tips.SetToolTip(_automatic, "Generate with the model's own climate everywhere, keeping the paint for later. For comparing the two.");
        _automatic.CheckedChanged += (_, _) => OnAutomaticChanged();

        var history = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 4, 4, 2) };
        history.Controls.Add(_undo);
        history.Controls.Add(_redo);
        history.Controls.Add(_clear);
        _undo.Click += (_, _) => ApplyHistory(undo: true);
        _redo.Click += (_, _) => ApplyHistory(undo: false);
        _clear.Click += (_, _) => ClearPaint();
        _tips.SetToolTip(_undo, "Undo the last stroke (Ctrl+Z)");
        _tips.SetToolTip(_redo, "Redo (Ctrl+Y)");
        _tips.SetToolTip(_clear, "Erase every stroke and go back to the automatic climate everywhere");

        var brush = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(6, 4, 6, 0),
        };
        brush.Controls.Add(Section("Brush"));
        brush.Controls.Add(Caption("Size"));
        brush.Controls.Add(_size);
        brush.Controls.Add(Caption("Strength"));
        brush.Controls.Add(_strength);
        brush.Controls.Add(Caption("Softness"));
        brush.Controls.Add(_softness);
        _tips.SetToolTip(_size, "Brush radius, as a share of the map's height ( [ and ] )");
        _tips.SetToolTip(_strength, "How completely one stroke establishes its climate. Full strength in the interior; lower to tint.");
        _tips.SetToolTip(_softness, "How wide the blended edge is. Soft edges give transition regions instead of borders.");
        foreach (var bar in new[] { _size, _strength, _softness })
            bar.ValueChanged += (_, _) => _canvas.Invalidate();

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 2, 4, 0) };
        tools.Controls.Add(_erase);
        _erase.Click += (_, _) => SelectBrush(-1);
        _tips.SetToolTip(_erase, "Erase climate paint, letting the model's own climate come back through");

        _more.Click += (_, _) => { _showAll = !_showAll; BuildPalette(); };
        BuildPalette();

        var paletteHeader = Section("Climate");

        column.Controls.Add(file);
        column.Controls.Add(automatic);
        column.Controls.Add(history);
        column.Controls.Add(brush);
        column.Controls.Add(tools);
        column.Controls.Add(_palette);
        column.Controls.Add(paletteHeader);
        column.Controls.Add(_hint);

        return column;
    }

    private Control BuildRight()
    {
        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(6, 4, 4, 0),
            BackColor = Theme.Surface,
            WrapContents = false,
        };

        strip.Controls.Add(Caption("View"));
        foreach (var view in new[] { View.Paint, View.Climate, View.Landscape })
        {
            var button = Theme.MakeButton(view.ToString(), 84);
            var captured = view;
            button.Click += (_, _) => SelectView(captured);
            strip.Controls.Add(button);
            _viewButtons[view] = button;
        }
        _tips.SetToolTip(_viewButtons[View.Paint], "Your strokes over the heightmap — what you asked for");
        _tips.SetToolTip(_viewButtons[View.Climate], "The climate classification the model now predicts");
        _tips.SetToolTip(_viewButtons[View.Landscape], "An approximate vegetation and terrain preview. Provinces and the world come from Preview.");
        strip.Controls.Add(_viewNote);

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        host.Controls.Add(_canvas);
        host.Controls.Add(_status);
        host.Controls.Add(_readout);
        host.Controls.Add(strip);
        return host;
    }

    private void BuildPalette()
    {
        _palette.SuspendLayout();
        _palette.Controls.Clear();
        _swatches.Clear();

        var brushes = ClimateBrush.All;
        for (int k = 0; k < brushes.Count; k++)
        {
            var brush = brushes[k];
            if (!brush.Compact && !_showAll) continue;

            var swatch = new Button
            {
                Text = brush.Name,
                Width = 200,
                Height = 26,
                Margin = new Padding(0, 1, 0, 1),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(30, 0, 0, 0),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Ui,
                UseVisualStyleBackColor = false,
                Tag = k,
            };
            swatch.FlatAppearance.BorderColor = Theme.Border;
            swatch.FlatAppearance.BorderSize = 1;
            swatch.Paint += (s, e) => PaintSwatch((Button)s!, e.Graphics, brush.Colour);
            int captured = k;
            swatch.Click += (_, _) => SelectBrush(captured);
            _tips.SetToolTip(swatch, brush.Hint);
            _palette.Controls.Add(swatch);
            _swatches.Add(swatch);
        }

        _more.Text = _showAll ? "Fewer climates ▴" : "More climates ▾";
        _palette.Controls.Add(_more);
        _palette.ResumeLayout();
        RefreshSelection();
    }

    private static void PaintSwatch(Button button, Graphics g, (byte R, byte G, byte B) colour)
    {
        using var fill = new SolidBrush(Color.FromArgb(colour.R, colour.G, colour.B));
        var square = new Rectangle(6, (button.Height - 16) / 2, 18, 16);
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
        Margin = new Padding(0, 6, 0, 2),
        Padding = new Padding(2, 0, 0, 0),
        Dock = DockStyle.Top,
    };

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.TextDim,
        Margin = new Padding(4, 7, 3, 0),
    };

    // ------------------------------------------------------------------ host API

    /// <summary>
    /// Paint as generation should receive it: null when there is none or the automatic switch is
    /// on. A paint restored from disk that the tab has not yet been opened to size counts — the
    /// model samples it by position, so it lands on the map whatever size it was drawn at.
    /// </summary>
    public ClimatePaint? EffectivePaint => _automatic.Checked || Paint is not { IsEmpty: false } ? null : Paint;

    /// <summary>The paint itself, whatever the switch says. Null before any terrain or file has sized it.</summary>
    public ClimatePaint? Paint => _paint ?? _pending;

    public bool HasPaint => Paint is { IsEmpty: false };

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool UseAutomatic
    {
        get => _automatic.Checked;
        set => _automatic.Checked = value;
    }

    /// <summary>
    /// Gives the tab something to paint over, or takes it away. A paint made at another size is
    /// resampled onto the new one — the same strokes, stretched to the new map — and said so.
    /// </summary>
    public void SetTerrain(Terrain? terrain)
    {
        _terrain = terrain;
        _base = null;
        _baseKey = null;

        if (terrain is null)
        {
            _baseRgb = null;
            ReplaceBitmap(ref _paintBitmap, null);
            ReplaceBitmap(ref _climateBitmap, null);
            ReplaceBitmap(ref _landscapeBitmap, null);
            _predicted = null;
            _landscape = null;
            _canvas.SetImage(null);
            _status.Text = "Choose a heightmap first — the Climate tab paints over it.";
            return;
        }

        var cfg = terrain.Config;
        var (w, h) = ClimatePaint.SizeFor(cfg.ProvinceWidth, cfg.ProvinceHeight);

        string note = "";
        var source = _paint ?? _pending;
        if (source is null)
        {
            _paint = new ClimatePaint(w, h);
        }
        else if (source.Width != w || source.Height != h)
        {
            _paint = source.Resampled(w, h);
            if (!source.IsEmpty) note = $" — paint resampled from {source.Width}×{source.Height} to fit this map";
        }
        else if (!ReferenceEquals(source, _paint))
        {
            _paint = source;
        }
        _pending = null;
        _history.Clear();
        UpdateHistoryButtons();

        _baseRgb = ShadedRelief(terrain, w, h);
        ReplaceBitmap(ref _paintBitmap, new Bitmap(w, h, PixelFormat.Format32bppArgb));
        Composite(new Rectangle(0, 0, w, h));

        ReplaceBitmap(ref _climateBitmap, null);
        ReplaceBitmap(ref _landscapeBitmap, null);
        _predicted = null;
        _landscape = null;
        _predictedVersion = _landscapeVersion = -1;

        _canvas.SetImage(_paintBitmap);
        _canvas.Mode = ImageView.Interaction.Paint;
        _status.Text = $"Painting over {cfg.ProvinceWidth}×{cfg.ProvinceHeight} at {w}×{h}{note}.";

        if (_view != View.Paint || HasPaint) QueuePreview();
        PaintChanged?.Invoke();
    }

    /// <summary>
    /// The settings changed under the model: the cached base is stale and the next preview
    /// rebuilds it. Recomputed now only if the tab is on screen; otherwise when it next is.
    /// </summary>
    public void InvalidateModel()
    {
        _modelVersion++;
        _base = null;
        _baseKey = null;
        if (_terrain is not null && Visible && (_view != View.Paint || HasPaint)) QueuePreview();
    }

    /// <summary>The tab came back on screen with the terrain it already had; catch the prediction up if it fell behind.</summary>
    public void Activated()
    {
        if (_terrain is null) return;
        if ((_view != View.Paint || HasPaint) && VersionShown(_view == View.Landscape ? View.Landscape : View.Climate) != CurrentVersion)
            QueuePreview();
    }

    /// <summary>
    /// Takes a paint loaded from disk — a preset's sidecar, or last session's autosave. Adopted
    /// straight away when the terrain is known and held until it is otherwise. Replacing what
    /// was on the tab is one undoable step, so a preset loaded by mistake costs nothing.
    /// </summary>
    public void AdoptPaint(ClimatePaint paint, string note)
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

        // Copied into the existing paint rather than swapping the reference, so the edit records
        // as before/after on one object and nothing else holding the paint goes stale.
        var before = new byte[_paint.Layers.Length][];
        for (int k = 0; k < before.Length; k++)
        {
            before[k] = (byte[])_paint.Layers[k].Clone();
            Array.Copy(incoming.Layers[k], _paint.Layers[k], _paint.Layers[k].Length);
        }
        _paint.Touch();

        _history.Push(ClimateEdit.Capture(_paint, before, new Rectangle(0, 0, _paint.Width, _paint.Height)));
        UpdateHistoryButtons();

        Composite(new Rectangle(0, 0, _paint.Width, _paint.Height));
        _canvas.Invalidate();
        _status.Text = note;
        AfterPaintChanged();
    }

    /// <summary>Erases everything, as an undoable step.</summary>
    public void ClearPaint()
    {
        if (_paint is null || _paint.IsEmpty)
        {
            if (_pending is not null) { _pending = null; PaintChanged?.Invoke(); }
            return;
        }

        var edit = ClimateEdit.Clear(_paint);
        edit.Redo(_paint);
        _history.Push(edit);
        UpdateHistoryButtons();

        Composite(new Rectangle(0, 0, _paint.Width, _paint.Height));
        _canvas.Invalidate();
        _status.Text = "Cleared — automatic climate everywhere. Undo brings the paint back.";
        AfterPaintChanged();
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
                NudgeSize(-6);
                return true;
            case Keys.OemCloseBrackets:
                NudgeSize(+6);
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

    private void NudgeSize(int delta)
    {
        int next = Math.Clamp(_size.Value + delta, _size.Minimum, _size.Maximum);
        if (next != _size.Value) _size.Value = next;
    }

    // ------------------------------------------------------------------ palette and views

    private static int FindBrush(KoppenClass cls)
    {
        for (int k = 0; k < ClimateBrush.All.Count; k++)
            if (ClimateBrush.All[k].Class == cls) return k;
        return 0;
    }

    private void SelectBrush(int index)
    {
        _brush = index;
        if (index >= 0 && !ClimateBrush.All[index].Compact && !_showAll)
        {
            _showAll = true;
            BuildPalette();
        }
        RefreshSelection();
        _canvas.Invalidate();
    }

    private void RefreshSelection()
    {
        foreach (var swatch in _swatches)
        {
            bool active = (int)swatch.Tag! == _brush;
            swatch.FlatAppearance.BorderColor = active ? Theme.Accent : Theme.Border;
            swatch.FlatAppearance.BorderSize = active ? 2 : 1;
            swatch.BackColor = active ? Theme.SurfaceHigh : Theme.Surface;
            swatch.Font = active ? Theme.UiBold : Theme.Ui;
        }

        bool erasing = _brush < 0;
        _erase.BackColor = erasing ? Theme.Accent : Theme.Surface;
        _erase.ForeColor = erasing ? Theme.AccentText : Theme.Text;
        _erase.FlatAppearance.BorderColor = erasing ? Theme.Accent : Theme.Border;
    }

    private void SelectView(View view)
    {
        _view = view;
        foreach (var (v, button) in _viewButtons)
        {
            bool active = v == view;
            button.BackColor = active ? Theme.Accent : Theme.Surface;
            button.ForeColor = active ? Theme.AccentText : Theme.Text;
            button.FlatAppearance.BorderColor = active ? Theme.Accent : Theme.Border;
        }

        _viewNote.Text = view switch
        {
            View.Paint => "Your strokes. Paint in any view.",
            View.Climate => "Predicted Koppen classification — blended edges and altitude mean it will not exactly match the paint.",
            _ => "Approximate landscape. Provinces, rivers and the world itself come from Preview.",
        };

        ShowCurrentView();
        if (_terrain is not null && view != View.Paint && VersionShown(view) != CurrentVersion) QueuePreview();
    }

    private int CurrentVersion => (_paint?.Version ?? 0) * 2 + (_automatic.Checked ? 1 : 0) + _modelVersion * 100_000;

    private int VersionShown(View view) => view == View.Landscape ? _landscapeVersion : _predictedVersion;

    private void ShowCurrentView()
    {
        var image = _view switch
        {
            View.Climate => _climateBitmap,
            View.Landscape => _landscapeBitmap,
            _ => _paintBitmap,
        };

        _canvas.SetImage(image ?? _paintBitmap);
    }

    // ------------------------------------------------------------------ painting

    private void WireCanvas()
    {
        _canvas.Mode = ImageView.Interaction.Paint;
        _canvas.StrokeBegan += OnStrokeBegan;
        _canvas.StrokeMoved += OnStrokeMoved;
        _canvas.StrokeEnded += OnStrokeEnded;
        _canvas.Overlay += DrawOverlay;
        _canvas.ViewChanged += (_, cursor) => UpdateReadout(cursor);
    }

    private float RadiusPixels => _paint is null ? 1f : _size.Value / 1000f * _paint.Height;

    private void OnStrokeBegan(PointF imagePoint)
    {
        if (_paint is null || _terrain is null) return;

        _stroke = new ClimateStroke(_paint, _brush, RadiusPixels, _strength.Value / 100f, _softness.Value / 100f);
        _strokePath.Clear();
        _strokePath.Add(imagePoint);
        Dab(imagePoint);
    }

    private void OnStrokeMoved(PointF imagePoint)
    {
        if (_stroke is null) return;
        _strokePath.Add(imagePoint);
        Dab(imagePoint);
    }

    private void Dab(PointF imagePoint)
    {
        if (_stroke is null) return;
        var dirty = _stroke.MoveTo(imagePoint.X, imagePoint.Y);
        if (dirty is { } rect && _view == View.Paint) Composite(rect);
        _canvas.Invalidate();
    }

    private void OnStrokeEnded()
    {
        if (_stroke is null) return;

        var edit = _stroke.End();
        _stroke = null;
        _strokePath.Clear();

        if (edit is null) { _canvas.Invalidate(); return; }

        _history.Push(edit);
        UpdateHistoryButtons();

        if (_view != View.Paint) Composite(edit.Area);
        _canvas.Invalidate();
        AfterPaintChanged();
    }

    private void ApplyHistory(bool undo)
    {
        if (_paint is null) return;
        bool changed = undo ? _history.Undo(_paint) : _history.Redo(_paint);
        if (!changed) return;

        UpdateHistoryButtons();
        Composite(new Rectangle(0, 0, _paint.Width, _paint.Height));
        _canvas.Invalidate();
        AfterPaintChanged();
    }

    private void UpdateHistoryButtons()
    {
        _undo.Enabled = _history.CanUndo;
        _redo.Enabled = _history.CanRedo;
        _clear.Enabled = HasPaint;
    }

    private void OnAutomaticChanged()
    {
        if (_paint is not null) Composite(new Rectangle(0, 0, _paint.Width, _paint.Height));
        _canvas.Invalidate();
        if (HasPaint)
            _status.Text = _automatic.Checked
                ? "Automatic climate — the paint is kept but not used until this is switched off."
                : "Painted climate in use.";
        AfterPaintChanged();
    }

    private void AfterPaintChanged()
    {
        _clear.Enabled = HasPaint;
        PaintChanged?.Invoke();
        if (_terrain is not null) QueuePreview();
    }

    // ------------------------------------------------------------------ canvas image

    /// <summary>The heightmap as shaded relief at paint resolution, sea in blue so the coast reads.</summary>
    private static byte[] ShadedRelief(Terrain terrain, int w, int h)
    {
        var cfg = terrain.Config;
        int pw = cfg.ProvinceWidth, ph = cfg.ProvinceHeight;
        float sea = cfg.Limits.SeaLevelUpper;
        float peak = Math.Max(sea + 1f, cfg.PeakElevation);
        var elevation = terrain.ProvinceElevation;
        var land = terrain.LandMask;

        // Box-average the province field down to the paint grid, then shade the result.
        var field = new float[w * h];
        var dry = new float[w * h];
        Parallel.For(0, h, y =>
        {
            int y0 = (int)((long)y * ph / h), y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * ph / h));
            for (int x = 0; x < w; x++)
            {
                int x0 = (int)((long)x * pw / w), x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * pw / w));
                double sum = 0; int n = 0, landCount = 0;
                for (int j = y0; j < y1 && j < ph; j++)
                    for (int i = x0; i < x1 && i < pw; i++)
                    {
                        sum += elevation[j * pw + i];
                        if (land[j * pw + i] != 0) landCount++;
                        n++;
                    }
                field[y * w + x] = n == 0 ? sea : (float)(sum / n);
                dry[y * w + x] = n == 0 ? 0 : (float)landCount / n;
            }
        });

        // A paint pixel spans several province pixels, so a one-pixel difference here is that
        // many times the slope the preview renderer shades at; scale the shading down to match,
        // and keep it gentle either way — the relief is the ground the paint has to read over.
        float slopeScale = 0.05f * w / pw;
        var rgb = new byte[w * h * 3];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float e = field[i];
                byte r, g, b;

                if (dry[i] < 0.5f)
                {
                    float depth = Math.Clamp((sea - e) / Math.Max(1f, sea - cfg.SeaFloorElevation), 0, 1);
                    r = (byte)(38 + 26 * (1 - depth)); g = (byte)(70 + 44 * (1 - depth)); b = (byte)(104 + 48 * (1 - depth));
                }
                else
                {
                    float left = field[y * w + Math.Max(0, x - 1)];
                    float up = field[Math.Max(0, y - 1) * w + x];
                    double shade = Math.Clamp(0.9 - ((e - left) + (e - up)) * slopeScale, 0.55, 1.12);
                    double t = Math.Clamp((e - sea) / (peak - sea), 0, 1);

                    // Neutral greys with a faint warmth, so the paint's colour is the only hue on
                    // the land and reads true instead of mixing with a hypsometric tint.
                    double v = 148 + 62 * Math.Sqrt(t);
                    r = (byte)Math.Clamp((v + 6) * shade, 0, 255);
                    g = (byte)Math.Clamp(v * shade, 0, 255);
                    b = (byte)Math.Clamp((v - 8) * shade, 0, 255);
                }

                rgb[i * 3] = r; rgb[i * 3 + 1] = g; rgb[i * 3 + 2] = b;
            }
        });

        return rgb;
    }

    /// <summary>Recomposites the paint wash over the relief inside <paramref name="area"/>.</summary>
    private void Composite(Rectangle area)
    {
        if (_paint is null || _paintBitmap is null || _baseRgb is null) return;

        int w = _paint.Width, h = _paint.Height;
        area.Intersect(new Rectangle(0, 0, w, h));
        if (area.Width <= 0 || area.Height <= 0) return;

        float opacity = _automatic.Checked ? 0.22f : 0.62f;

        var data = _paintBitmap.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[area.Width * 4];
            for (int y = 0; y < area.Height; y++)
            {
                int py = area.Y + y;
                for (int x = 0; x < area.Width; x++)
                {
                    int px = area.X + x;
                    int i = py * w + px;
                    float r = _baseRgb[i * 3], g = _baseRgb[i * 3 + 1], b = _baseRgb[i * 3 + 2];

                    var (cr, cg, cb, weight) = _paint.ColourAt(i);
                    if (weight > 0f)
                    {
                        float a = weight * opacity;
                        r += (cr - r) * a; g += (cg - g) * a; b += (cb - b) * a;
                    }

                    row[x * 4] = (byte)b; row[x * 4 + 1] = (byte)g; row[x * 4 + 2] = (byte)r; row[x * 4 + 3] = 255;
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            _paintBitmap.UnlockBits(data);
        }
    }

    private static void ReplaceBitmap(ref Bitmap? slot, Bitmap? next)
    {
        var old = slot;
        slot = next;
        old?.Dispose();
    }

    /// <summary>The brush ring, and — outside the Paint view — the stroke in progress in its own colour.</summary>
    private void DrawOverlay(Graphics g, float zoom)
    {
        if (_paint is null) return;

        var tint = _brush < 0
            ? Color.FromArgb(150, 255, 255, 255)
            : Color.FromArgb(150, ClimateBrush.All[_brush].Colour.R, ClimateBrush.All[_brush].Colour.G, ClimateBrush.All[_brush].Colour.B);

        if (_view != View.Paint && _strokePath.Count > 0)
        {
            float width = MathF.Max(RadiusPixels * 2f * zoom, 2f);
            using var pen = new Pen(tint, width)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
            };

            if (_strokePath.Count == 1)
            {
                var only = _canvas.ToControlPoint(_strokePath[0]);
                using var fill = new SolidBrush(tint);
                g.FillEllipse(fill, only.X - width / 2f, only.Y - width / 2f, width, width);
            }
            else
            {
                var points = new PointF[_strokePath.Count];
                for (int i = 0; i < points.Length; i++) points[i] = _canvas.ToControlPoint(_strokePath[i]);
                g.DrawLines(pen, points);
            }
        }

        if (_canvas.CursorAt is not { } cursor) return;

        float radius = RadiusPixels * zoom;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var ring = new Pen(Color.FromArgb(220, 255, 255, 255), 1.5f);
        using var shadow = new Pen(Color.FromArgb(120, 0, 0, 0), 3.5f);
        g.DrawEllipse(shadow, cursor.X - radius, cursor.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(ring, cursor.X - radius, cursor.Y - radius, radius * 2, radius * 2);

        // The soft edge starts here; inside it the stroke is full strength.
        float hard = radius * (1f - _softness.Value / 100f);
        if (hard > 2f)
        {
            using var inner = new Pen(Color.FromArgb(110, 255, 255, 255), 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawEllipse(inner, cursor.X - hard, cursor.Y - hard, hard * 2, hard * 2);
        }
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
        var parts = new List<string>();

        var painted = _paint.Dominant(i);
        parts.Add(painted is { } d
            ? $"Painted: {d.Brush.Name} {d.Weight * 100:F0}%" + (d.Share < 0.99f ? " (blended)" : "")
            : "Painted: automatic");

        if (_predicted is not null && _predicted.Length == _paint.Layers[0].Length && _predicted[i] != KoppenClass.Ocean)
            parts.Add($"Predicted: {Describe(_predicted[i])}");

        if (_landscape is not null && _landscape.Length == _paint.Layers[0].Length && _landscape[i] != TerrainClass.Sea)
            parts.Add($"Landscape: {_landscape[i]}");

        _readout.Text = string.Join("   ·   ", parts);
    }

    private static string Describe(KoppenClass cls)
    {
        foreach (var brush in ClimateBrush.All)
            if (brush.Class == cls) return brush.Name;
        return cls.ToString();
    }

    // ------------------------------------------------------------------ prediction

    private void QueuePreview()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RunPreviewAsync()
    {
        if (_terrain is null || _paint is null) return;

        if (_previewRunning)
        {
            _previewDirty = true;
            return;
        }

        _previewRunning = true;
        _previewDirty = false;
        int generation = ++_previewGeneration;

        var terrain = _terrain;
        var cfg = terrain.Config;
        var paint = EffectivePaint?.Clone();
        bool automatic = _automatic.Checked;
        int version = CurrentVersion;
        bool wantLandscape = _view == View.Landscape;

        // Sizes are read here, on the UI thread, and used as read: a new heightmap chosen while
        // this runs rewrites the config's dimensions under the task, and the arrays in
        // <paramref name="terrain"/> are the old size.
        int w = _paint.Width, h = _paint.Height;
        int pw = cfg.ProvinceWidth, ph = cfg.ProvinceHeight;

        // The seed is in the key because the model's temperature drift and rain wobble are
        // seeded; a prediction on the old seed would differ from the run on the new one.
        string key = $"{terrain.Stamp}|{_modelVersion}|{cfg.Seed}|{cfg.AzgaarJsonPath}";
        var cachedBase = _baseKey == key ? _base : null;
        var azgaar = _azgaar;
        string? azgaarPath = _azgaarPath;

        _status.Text = cachedBase is null ? "Predicting climate (building the model for this map)…" : "Predicting climate…";

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (b, field, predicted, climateImage, landscape, landscapeImage, loadedAzgaar) = await Task.Run(() =>
            {
                string? path = string.IsNullOrWhiteSpace(cfg.AzgaarJsonPath) ? null : cfg.AzgaarJsonPath;
                if (path != azgaarPath)
                {
                    azgaar = path is null ? null : AzgaarImport.Load(path, cfg);
                    azgaarPath = path;
                }

                var built = cachedBase ?? ClimateModel.Prepare(cfg, terrain.ProvinceElevation, terrain.LandMask,
                    new Rng(cfg.Seed ^ 0x0C11), azgaar);

                var f = ClimateModel.Finish(built, paint, report: false);

                var classes = new KoppenClass[w * h];
                var rgb = new byte[w * h * 3];

                Parallel.For(0, h, y =>
                {
                    int sy = Math.Min(ph - 1, (int)(((long)y * 2 + 1) * ph / (2L * h)));
                    for (int x = 0; x < w; x++)
                    {
                        int sx = Math.Min(pw - 1, (int)(((long)x * 2 + 1) * pw / (2L * w)));
                        int j = sy * pw + sx, i = y * w + x;
                        var cls = terrain.LandMask[j] == 0
                            ? KoppenClass.Ocean
                            : Koppen.Classify(f.WarmC[j], f.ColdC[j], f.MeanC[j], f.AnnualMm[j], f.SummerMm[j], f.WinterMm[j]);
                        classes[i] = cls;
                        var c = Koppen.Colour(cls);
                        rgb[i * 3] = c.R; rgb[i * 3 + 1] = c.G; rgb[i * 3 + 2] = c.B;
                    }
                });

                TerrainClass[]? land = null;
                PreviewRenderer.Image? landImage = null;
                if (wantLandscape)
                {
                    var classified = TerrainClassifier.Classify(cfg, terrain.ProvinceElevation, terrain.LandMask, f,
                        new Rng(cfg.Seed ^ 0x7E44), azgaar);
                    var full = PreviewRenderer.RenderTerrain(classified.Terrain, cfg);
                    landImage = Resample(full, w, h);

                    land = new TerrainClass[w * h];
                    Parallel.For(0, h, y =>
                    {
                        int sy = Math.Min(ph - 1, (int)(((long)y * 2 + 1) * ph / (2L * h)));
                        for (int x = 0; x < w; x++)
                        {
                            int sx = Math.Min(pw - 1, (int)(((long)x * 2 + 1) * pw / (2L * w)));
                            land[y * w + x] = classified.Terrain[sy * pw + sx];
                        }
                    });
                }

                return (built, f, classes, new PreviewRenderer.Image(rgb, w, h), land, landImage, azgaar);
            });

            if (generation != _previewGeneration || IsDisposed) return;

            _base = b;
            _baseKey = key;
            _azgaar = loadedAzgaar;
            _azgaarPath = azgaarPath;
            _ = field;

            _predicted = predicted;
            _predictedVersion = version;
            _predictedAutomatic = automatic;
            ReplaceBitmap(ref _climateBitmap, PreviewRenderer.ToBitmap(climateImage));

            if (landscapeImage is { } li)
            {
                _landscape = landscape;
                _landscapeVersion = version;
                ReplaceBitmap(ref _landscapeBitmap, PreviewRenderer.ToBitmap(li));
            }

            ShowCurrentView();
            UpdateReadout(_canvas.CursorAt);

            double coverage = paint?.Coverage(terrain.LandMask, cfg.ProvinceWidth, cfg.ProvinceHeight) ?? 0;
            _status.Text = paint is null
                ? (automatic && HasPaint
                    ? $"Automatic climate shown — the paint is bypassed ({sw.ElapsedMilliseconds} ms)."
                    : $"Automatic climate — nothing painted yet ({sw.ElapsedMilliseconds} ms).")
                : $"Prediction up to date — {coverage * 100:F0}% of land painted ({sw.ElapsedMilliseconds} ms).";
        }
        catch (Exception ex)
        {
            if (generation != _previewGeneration || IsDisposed) return;
            Console.WriteLine($"Climate preview failed: {ex}");
            _status.Text = $"Prediction failed: {ex.Message}";
        }
        finally
        {
            _previewRunning = false;
        }

        if (_previewDirty || (_view != View.Paint && VersionShown(_view) != CurrentVersion))
            QueuePreview();
    }

    /// <summary>Nearest-pixel resample of a rendered image to the paint grid.</summary>
    private static PreviewRenderer.Image Resample(PreviewRenderer.Image image, int w, int h)
    {
        if (image.Width == w && image.Height == h) return image;

        var rgb = new byte[w * h * 3];
        Parallel.For(0, h, y =>
        {
            int sy = Math.Min(image.Height - 1, (int)((long)y * image.Height / h));
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(image.Width - 1, (int)((long)x * image.Width / w));
                int from = (sy * image.Width + sx) * 3, to = (y * w + x) * 3;
                rgb[to] = image.Rgb[from]; rgb[to + 1] = image.Rgb[from + 1]; rgb[to + 2] = image.Rgb[from + 2];
            }
        });

        return new PreviewRenderer.Image(rgb, w, h);
    }

    // ------------------------------------------------------------------ files

    private void ImportPaint()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import climate paint",
            Filter = "Climate paint (*.png)|*.png|All files (*.*)|*.*",
            InitialDirectory = PaintDir ?? "",
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var paint = ClimatePaint.Load(dialog.FileName);
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
            Title = "Export climate paint",
            Filter = "Climate paint (*.png)|*.png",
            FileName = "climate.png",
            InitialDirectory = PaintDir ?? "",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            SavePaint(dialog.FileName);
            PaintDir = Path.GetDirectoryName(dialog.FileName);
            _status.Text = $"Exported to {Path.GetFileName(dialog.FileName)} — pass it to --climate-paint to build from a terminal.";
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
            _debounce.Dispose();
            _tips.Dispose();
            _paintBitmap?.Dispose();
            _climateBitmap?.Dispose();
            _landscapeBitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
