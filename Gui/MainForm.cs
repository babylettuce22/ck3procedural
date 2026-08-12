using System.Diagnostics;
using System.Drawing.Imaging;
using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.Gui;

/// <summary>
/// One window: choose a heightmap, tune the settings, look at what they produce, write the mod.
///
/// There used to be a second tab that generated terrain. Heightmaps are now made elsewhere, so
/// every setting applies to every run and there is nothing left to split the window along.
///
/// The whole reason this is WinForms is <see cref="PropertyGrid"/>: pointing it at
/// <c>MapConfig</c> yields an editable, categorised editor for every setting with no per-parameter
/// UI code, which is what makes the terrain tunable without an edit-rebuild-run cycle. That is
/// also why MapConfig's fields became auto-properties — the grid reflects over properties only.
///
/// Work runs on a worker thread. It takes seconds at <c>tiny</c> and minutes at <c>vanilla</c>, so
/// doing it on the UI thread would freeze the window for the whole run. What the window shows while
/// that happens is <see cref="Stage.Entering"/> — the phase names the pipeline was already printing
/// — because "province partition…" and a moving bar is the difference between working and hung.
///
/// The views are a strip of buttons over a single <see cref="ImageView"/> rather than a TabControl.
/// One viewer means one zoom and one pan across all seven, so switching from climate to terrain
/// compares the same coastline instead of two whole-world thumbnails, and it is the reason the
/// renders are cached per view rather than rebuilt on every switch.
/// </summary>
public sealed class MainForm : Form
{
    private readonly GenerationOptions _options;
    private readonly GuiState _state = GuiState.Load();

    private readonly PropertyGrid _grid = new()
    {
        Dock = DockStyle.Fill,
        PropertySort = PropertySort.Categorized,
        HelpVisible = true,
        ToolbarVisible = false,
    };

    private readonly NumericUpDown _seed = new()
    {
        Minimum = 0,
        Maximum = int.MaxValue,
        Width = 92,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.SurfaceHigh,
        ForeColor = Theme.Text,
        Margin = new Padding(3, 5, 3, 3),
    };

    private readonly Button _browse = Theme.MakeButton("Heightmap…", 100);
    private readonly Button _roll = Theme.MakeButton("Roll", 52);
    private readonly Button _preview = Theme.MakeButton("Preview", 84, primary: true);
    private readonly Button _writeMod = Theme.MakeButton("Write mod", 96);
    private readonly Button _cancel = Theme.MakeButton("Cancel", 72);
    private readonly Button _openMod = Theme.MakeButton("Open mod folder", 120);
    private readonly Button _savePreset = Theme.MakeButton("Save preset…", 110);
    private readonly Button _loadPreset = Theme.MakeButton("Load preset…", 110);

    private readonly Label _sourceName = new()
    {
        AutoSize = true,
        Margin = new Padding(8, 8, 0, 0),
        ForeColor = Theme.TextDim,
        Font = Theme.Ui,
    };

    private readonly ImageView _viewer = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _viewStrip = new()
    {
        Dock = DockStyle.Top,
        Height = 32,
        Padding = new Padding(4, 3, 4, 0),
        BackColor = Theme.Surface,
    };

    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Background,
        ForeColor = Theme.TextDim,
        Font = Theme.Mono,
    };

    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        Padding = new Padding(8, 0, 0, 0),
        Text = "Ready",
    };

    private readonly Label _readout = new()
    {
        Dock = DockStyle.Right,
        Width = 260,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Theme.TextDim,
        Font = Theme.Ui,
        Padding = new Padding(0, 0, 8, 0),
    };

    private readonly ProgressBar _progress = new()
    {
        Dock = DockStyle.Right,
        Width = 150,
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 25,
        Maximum = 1000,
        Visible = false,
        Margin = new Padding(0),
    };

    private readonly Label _eta = new()
    {
        Dock = DockStyle.Right,
        Width = 170,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Theme.TextDim,
        Font = Theme.Ui,
        Padding = new Padding(0, 0, 8, 0),
        Visible = false,
    };

    /// <summary>
    /// Drives the bar between phase boundaries. Without it the bar would only move when a phase
    /// ended, which on the province partition is a minute of nothing happening.
    /// </summary>
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 200 };

    private RunProgress? _progressModel;

    private SplitContainer _body = null!;
    private SplitContainer _right = null!;

    /// <summary>
    /// The views, in the order the pipeline produces what they show. Rendered on demand and cached:
    /// a vanilla-size map is seven renders of 42 million pixels each and only one of them is ever
    /// on screen, so building all seven per click paid for six nobody looked at.
    /// </summary>
    private static readonly (string Name, Func<GenerationResult, PreviewRenderer.Image> Render)[] Views =
        [
        ("Height", PreviewRenderer.RenderElevation),
        ("Terrain", PreviewRenderer.RenderTerrain),
        ("Climate", PreviewRenderer.RenderClimate),
        ("Drainage", PreviewRenderer.RenderDrainage),
        ("Rivers", PreviewRenderer.RenderRivers),
        ("Provinces", PreviewRenderer.RenderProvinces),
        ("Counties", PreviewRenderer.RenderCounties),
        ("Duchies", PreviewRenderer.RenderDuchies),
        ("Kingdoms", PreviewRenderer.RenderKingdoms),
        ("Empires", PreviewRenderer.RenderEmpires),
        ("Government", PreviewRenderer.RenderGovernment),
    ];

    private readonly Dictionary<string, Button> _viewButtons = [];
    private readonly Dictionary<string, Bitmap> _rendered = [];
    private GenerationResult? _result;
    private string _view = "Counties";

    /// <summary>
    /// The last heightmap decoded from disk, kept so previewing a settings change does not pay to
    /// decode the image again. Only the decode is cached — see <see cref="MapGen.HeightmapImage"/>
    /// for why nothing derived from it may be.
    /// </summary>
    private MapGen.HeightmapImage? _loaded;
    private string? _heightmapPath;
    private bool _busy;
    private CancellationTokenSource? _cancellation;

    public MainForm(GenerationOptions options)
    {
        _options = options;

        // A heightmap named on the command line is still the chosen one when the window opens.
        // Without this, `--heightmap x.png --gui` came up with both buttons greyed out and no
        // indication why. Failing that, the one from last session, if it is still there.
        _heightmapPath = options.HeightmapPath
            ?? (File.Exists(_state.HeightmapPath) ? _state.HeightmapPath : null);
        options.HeightmapPath = _heightmapPath;

        Text = "CK3 Procedural Map";
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(1000, 640);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        KeyPreview = true;

        Theme.ApplyLight(_grid);
        _grid.SelectedObject = _options.Config;

        _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
        _seed.ValueChanged += (_, _) => _options.Config.Seed = (int)_seed.Value;

        _browse.Click += (_, _) => PickHeightmap();
        _roll.Click += (_, _) => _seed.Value = Random.Shared.Next(1, int.MaxValue);
        _preview.Click += async (_, _) => await BuildAsync(null);
        _writeMod.Click += async (_, _) => await BuildAsync(GenerationOptions.DefaultModDir);
        _cancel.Click += (_, _) => RequestCancel();
        _openMod.Click += (_, _) => OpenModFolder();
        _savePreset.Click += (_, _) => SavePreset();
        _loadPreset.Click += (_, _) => LoadPreset();

        _viewer.ViewChanged += ShowReadout;

        if (_state.View is { } remembered && Views.Any(v => v.Name == remembered)) _view = remembered;

        Controls.Add(BuildBody());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildStatusBar());

        ApplySource();
        SelectView(_view);

        // Everything in the generator reports progress with Console.WriteLine. Redirecting the
        // console is what lets all of that reach the log pane without touching a single call site.
        Console.SetOut(new TextBoxWriter(_log));
        Stage.Entering += OnStageEntered;
        Stage.Detailing += OnStageDetail;
        _tick.Tick += (_, _) => ShowProgress();
    }

    // --- Layout ---------------------------------------------------------------------------------
    //
    // Fill-docked children are added before edge-docked ones throughout. WinForms lays docking out
    // in reverse z-order, so the fill has to be the first thing in the collection or it claims the
    // whole client area and everything else is drawn over the top of it.

    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(6, 5, 6, 5),
            BackColor = Theme.Surface,
        };

        bar.Controls.Add(_browse);
        bar.Controls.Add(Separator());
        bar.Controls.Add(Caption("Seed"));
        bar.Controls.Add(_seed);
        bar.Controls.Add(_roll);
        bar.Controls.Add(Separator());
        bar.Controls.Add(_preview);
        bar.Controls.Add(_writeMod);
        bar.Controls.Add(_cancel);
        bar.Controls.Add(_openMod);
        bar.Controls.Add(_sourceName);

        return bar;
    }

    private Control BuildBody()
    {
        var presets = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(3, 3, 3, 3),
            BackColor = Theme.Surface,
        };
        presets.Controls.Add(_savePreset);
        presets.Controls.Add(_loadPreset);

        var settings = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        settings.Controls.Add(_grid);
        settings.Controls.Add(presets);

        foreach (var (name, _) in Views)
        {
            var button = ViewButton(name);
            _viewButtons[name] = button;
            _viewStrip.Controls.Add(button);
        }

        var viewer = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        viewer.Controls.Add(_viewer);
        viewer.Controls.Add(_viewStrip);

        var logPane = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        logPane.Controls.Add(_log);
        logPane.Controls.Add(BuildLogHeader());

        // Neither the minimum sizes nor the splitter positions are set here. A SplitContainer that
        // has not been laid out yet is 150 px wide, and both properties throw outright if the value
        // will not fit inside that — see Place, called from OnLoad once the panes are real.
        _right = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = Theme.Border,
        };
        _right.Panel1.Controls.Add(viewer);
        _right.Panel2.Controls.Add(logPane);

        _body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Border,
            FixedPanel = FixedPanel.Panel1,
        };
        _body.Panel1.Controls.Add(settings);
        _body.Panel2.Controls.Add(_right);

        return _body;
    }

    private Control BuildLogHeader()
    {
        var clear = Theme.MakeButton("Clear", 60);
        clear.Click += (_, _) => _log.Clear();

        var copy = Theme.MakeButton("Copy", 60);
        copy.Click += (_, _) =>
        {
            if (_log.TextLength > 0) Clipboard.SetText(_log.Text);
        };

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(4, 3, 4, 0),
            BackColor = Theme.Surface,
        };
        header.Controls.Add(Caption("Log"));
        header.Controls.Add(clear);
        header.Controls.Add(copy);
        return header;
    }

    private Control BuildStatusBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Theme.Surface };
        bar.Controls.Add(_status);
        bar.Controls.Add(_readout);
        bar.Controls.Add(_eta);
        bar.Controls.Add(_progress);
        return bar;
    }

    private static Label Caption(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui,
            Margin = new Padding(6, 9, 4, 0),
        };

    private static Control Separator()
        => new Panel { Width = 1, Height = 22, BackColor = Theme.Border, Margin = new Padding(8, 4, 8, 0) };

    /// <summary>
    /// A view button, built by hand rather than through <see cref="Theme.MakeButton"/>: these carry
    /// a selected state, and that helper repaints on enable to keep disabled buttons legible, which
    /// would wipe the highlight off the selected view every time a run finished.
    /// </summary>
    private Button ViewButton(string name)
    {
        var button = new Button
        {
            Text = name,
            Width = 84,
            Height = 24,
            FlatStyle = FlatStyle.Flat,
            Font = Theme.Ui,
            BackColor = Theme.SurfaceHigh,
            ForeColor = Theme.Text,
            UseVisualStyleBackColor = false,
            Margin = new Padding(1, 1, 1, 1),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.Border;
        button.Click += (_, _) => SelectView(name);
        return button;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.ApplyLightTitleBar(this);
        RestorePlacement();

        Place(_body, 300, 400, _state.SettingsWidth);
        Place(_right, 200, 80, _state.ViewerHeight);
    }

    /// <summary>
    /// Sizes a splitter, once the pane it lives in has a real size.
    ///
    /// Every one of these three properties throws rather than clamping if the value does not fit the
    /// current width — including against a width the control has not been laid out to yet — so the
    /// order matters and so does clamping every value before it goes in. The minimums give way
    /// first: on a window too narrow to honour both, a cramped pane beats a crash.
    /// </summary>
    private static void Place(SplitContainer split, int min1, int min2, int wanted)
    {
        int size = (split.Orientation == Orientation.Vertical ? split.Width : split.Height)
                   - split.SplitterWidth;
        if (size <= 0) return;

        min1 = Math.Min(min1, size / 3);
        min2 = Math.Min(min2, size / 3);

        split.Panel1MinSize = min1;
        split.Panel2MinSize = min2;
        split.SplitterDistance = Math.Clamp(wanted, min1, size - min2);
    }

    /// <summary>
    /// Restores the window where it was, unless that is off every screen it can see — a monitor
    /// that has been unplugged since should not open the window somewhere invisible.
    /// </summary>
    private void RestorePlacement()
    {
        var saved = new Rectangle(_state.Left, _state.Top, _state.Width, _state.Height);

        if (saved.Width >= MinimumSize.Width && saved.Height >= MinimumSize.Height
            && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(saved)))
        {
            Bounds = saved;
        }
        else
        {
            Size = new Size(1500, 950);
            var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1500, 950);
            Location = new Point(work.X + (work.Width - Width) / 2, work.Y + (work.Height - Height) / 2);
        }

        if (_state.Maximized) WindowState = FormWindowState.Maximized;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // RestoreBounds, not Bounds, when maximised: saving the maximised rectangle would make the
        // window un-restore to full screen the next time it opened.
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _state.Left = bounds.X;
        _state.Top = bounds.Y;
        _state.Width = bounds.Width;
        _state.Height = bounds.Height;
        _state.Maximized = WindowState == FormWindowState.Maximized;
        _state.SettingsWidth = _body.SplitterDistance;
        _state.ViewerHeight = _right.SplitterDistance;
        _state.HeightmapPath = _heightmapPath;
        _state.View = _view;
        _state.Save();

        Stage.Entering -= OnStageEntered;
        Stage.Detailing -= OnStageDetail;
        base.OnFormClosing(e);
    }

    /// <summary>F5 previews, Ctrl+S writes the mod, Escape cancels.</summary>
    protected override bool ProcessCmdKey(ref Message message, Keys key)
    {
        switch (key)
        {
            case Keys.F5 when _preview.Enabled:
                _ = BuildAsync(null);
                return true;

            case Keys.Control | Keys.S when _writeMod.Enabled:
                _ = BuildAsync(GenerationOptions.DefaultModDir);
                return true;

            case Keys.Escape when _busy:
                RequestCancel();
                return true;
        }

        return base.ProcessCmdKey(ref message, key);
    }

    // --- Sources, presets and the mod folder -----------------------------------------------------

    private void PickHeightmap()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Build the mod around a heightmap",
            Filter = "Heightmap PNG (*.png)|*.png|All files (*.*)|*.*",
            InitialDirectory = _heightmapPath is null ? "" : Path.GetDirectoryName(_heightmapPath) ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _heightmapPath = dialog.FileName;
        _options.HeightmapPath = _heightmapPath;
        ApplySource();
    }

    private void ApplySource()
    {
        _sourceName.Text = _heightmapPath is null
            ? "(no heightmap chosen)"
            : Path.GetFileName(_heightmapPath);

        _openMod.Enabled = Directory.Exists(GenerationOptions.DefaultModDir);
        SetEnabled(!_busy);
    }

    private void SavePreset()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save these settings",
            Filter = "Map settings (*.json)|*.json",
            FileName = "preset.json",
            InitialDirectory = _state.PresetDir ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        Preset.Save(_options.Config, dialog.FileName);
        _state.PresetDir = Path.GetDirectoryName(dialog.FileName);
        _status.Text = $"Saved settings to {Path.GetFileName(dialog.FileName)}";
    }

    private void LoadPreset()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Load settings",
            Filter = "Map settings (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = _state.PresetDir ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            int applied = Preset.Load(_options.Config, dialog.FileName);
            _state.PresetDir = Path.GetDirectoryName(dialog.FileName);

            // The seed lives in the config but has its own box on the toolbar, so it needs pushing
            // back out; the grid needs telling that the object under it changed beneath its feet.
            _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
            _options.Config.StartYear = Math.Clamp(_options.Config.StartYear, 1, 9999);

            _grid.Refresh();

            _status.Text = $"Loaded {applied} settings from {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not read {dialog.FileName}: {ex.Message}");
            _status.Text = "Preset could not be read — see log";
        }
    }

    private void OpenModFolder()
    {
        string dir = GenerationOptions.DefaultModDir;
        if (!Directory.Exists(dir)) return;

        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    // --- Running --------------------------------------------------------------------------------

    /// <summary>
    /// Derives everything the mod is made of — the drainage network, the land mask, provinces, the
    /// climate, cultures, faiths and titles — and optionally writes it out.
    ///
    /// Only the image *decode* is reused between runs, and only while the file on disk is byte-for-
    /// byte the one that was decoded. Everything downstream is rebuilt from scratch every time.
    ///
    /// That is narrower than it used to be, and deliberately. The cache used to hold the whole
    /// <see cref="MapGen.TerrainData"/> and key it on the file path alone, which produced two
    /// silent failures: re-exporting a heightmap over the same path left the old one on screen, and
    /// every river and lake setting appeared to do nothing at all, because those are consumed while
    /// deriving TerrainData and the cache never let that run again. A cache that can make a setting
    /// do nothing is worse than no cache; this one can only ever save the decode.
    /// </summary>
    private async Task BuildAsync(string? modDir)
    {
        var (result, cancelled) = await RunAsync(
            modDir is null ? "Building preview…" : "Writing mod…",
            () =>
            {
                var cfg = _options.Config;

                // Phased like the rest, and by the same names Generator.Generate uses. These are
                // seconds at vanilla size, and time outside a phase is time the progress estimate
                // cannot see — it showed up as a bar that sat at zero and then leapt.
                Stage.Time("heightmap decode", () =>
                {
                    if (_loaded is null || !_loaded.StillStandsFor(_heightmapPath!))
                        _loaded = MapGen.HeightmapSource.Read(_heightmapPath!, cfg);
                    else
                        MapGen.HeightmapSource.Apply(_loaded, cfg);
                });

                var terra = Stage.Time("province elevation",
                    () => MapGen.TerrainData.FromElevation(_loaded!.ToElevation(cfg), cfg));

                var r = Generator.FromTerrain(terra, cfg);
                if (modDir is not null) Generator.WriteMod(r, _options, modDir);
                return r;
            },
            writing: modDir is not null);

        if (cancelled)
        {
            // A cancelled write stops between files rather than rolling back, and a half-written
            // mod that CK3 will still try to load is worth saying out loud.
            _status.Text = modDir is null
                ? "Cancelled — nothing written"
                : "Cancelled — the mod folder may be half written";
            return;
        }

        if (result is null) return;

        ShowResult(result);
        ApplySource();
        _status.Text = modDir is null
            ? $"Preview — {result.Provinces.Count} provinces. Nothing written."
            : $"Mod written to {modDir} — {result.Provinces.Count} provinces";
    }

    /// <summary>Runs work off the UI thread, with the buttons locked and failures sent to the log.</summary>
    private async Task<(GenerationResult? Result, bool Cancelled)> RunAsync(
        string message, Func<GenerationResult> work, bool writing)
    {
        if (_busy) return (null, false);

        _busy = true;
        SetEnabled(false);
        _log.Clear();
        _status.Text = message;

        // Province megapixels, read live: the heightmap decides the map size and has not been read
        // yet when this starts, so the first phase or two are predicted against the last run's size.
        _progressModel = new RunProgress(
            Plan(writing),
            () => _options.Config.ProvinceWidth / 1000.0 * _options.Config.ProvinceHeight / 1000.0);

        // With a shipped profile behind it there is always something to predict against, so the
        // marquee is now only the fallback for a profile that has somehow been emptied.
        _progress.Style = _progressModel.Calibrated ? ProgressBarStyle.Blocks : ProgressBarStyle.Marquee;
        _progress.Value = 0;
        _progress.Visible = true;
        _eta.Visible = true;
        _tick.Start();

        _cancellation = new CancellationTokenSource();
        Stage.Begin();
        Stage.Cancellation = _cancellation.Token;

        var clock = Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(work, _cancellation.Token);

            // Only a run that finished teaches anything: a cancelled or failed one has a truncated
            // phase list, and storing it would predict the next run as that much shorter.
            var measured = _progressModel.Finish();
            if (writing) _state.WriteRun = measured; else _state.PreviewRun = measured;

            // Saved now rather than only on close. A write is a long, rare run and the thing most
            // worth having measured; losing it because the window was killed rather than closed
            // would send the next first write back to the shipped numbers.
            _state.Save();

            Stage.Report();
            Console.WriteLine();
            Console.WriteLine($"Finished in {clock.ElapsedMilliseconds / 1000.0:F1} s");
            return (result, false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine($"Cancelled after {clock.ElapsedMilliseconds / 1000.0:F1} s");
            return (null, true);
        }
        catch (Exception ex)
        {
            // A failure must not take the window with it; the message is far more useful sitting
            // in the log next to the parameters that caused it.
            Console.WriteLine();
            Console.WriteLine(ex);
            _status.Text = "Failed — see log";
            return (null, false);
        }
        finally
        {
            Stage.Cancellation = CancellationToken.None;
            _cancellation.Dispose();
            _cancellation = null;

            _tick.Stop();
            _progressModel = null;

            _busy = false;
            _progress.Visible = false;
            _eta.Visible = false;
            SetEnabled(true);
        }
    }

    /// <summary>
    /// What this run is expected to cost, best evidence first: a completed run of the same kind on
    /// this machine, then the shipped profile with whatever previews have measured here folded into
    /// it, then the shipped profile alone.
    ///
    /// The middle case is the one that matters. Writes are rare — a session is a dozen previews and
    /// maybe one write — so the write profile was empty far more often than not, and a first write
    /// is precisely when "how long will this take" is worth answering.
    /// </summary>
    private RunProfile Plan(bool writing)
    {
        var learned = writing ? _state.WriteRun : _state.PreviewRun;
        if (learned.Phases.Count > 0) return learned;

        var shipped = RunProgress.Shipped(writing);
        return writing ? RunProgress.Blend(shipped, _state.PreviewRun) : shipped;
    }

    /// <summary>Moves the bar between phase boundaries and says how much longer it thinks it has.</summary>
    private void ShowProgress()
    {
        if (_progressModel is null) return;

        var (fraction, remaining) = _progressModel.Sample();

        if (remaining is { } left && _progress.Style == ProgressBarStyle.Blocks)
        {
            _progress.Value = (int)Math.Clamp(fraction * _progress.Maximum, 0, _progress.Maximum);
            _eta.Text = $"{fraction * 100:F0}%   {RunProgress.Describe(left)}";
        }
        else
        {
            // No plan yet. Elapsed time is the only true thing there is to show.
            _eta.Text = $"{_progressModel.Elapsed.TotalSeconds:F0}s elapsed";
        }
    }

    private void RequestCancel()
    {
        if (_cancellation is null || _cancellation.IsCancellationRequested) return;

        _cancellation.Cancel();
        _cancel.Enabled = false;

        // Not instant, and saying so is better than looking ignored: cancellation lands at the next
        // phase boundary, which on a vanilla-size map can be the better part of a minute away.
        _status.Text = "Cancelling — stopping at the end of this phase…";
    }

    private void SetEnabled(bool enabled)
    {
        _grid.Enabled = enabled;
        _seed.Enabled = enabled;
        _roll.Enabled = enabled;
        _browse.Enabled = enabled;
        _savePreset.Enabled = enabled;
        _loadPreset.Enabled = enabled;
        _cancel.Enabled = !enabled;

        bool ready = enabled && _heightmapPath is not null;
        _writeMod.Enabled = ready;
        _preview.Enabled = ready;
    }

    /// <summary>
    /// A phase boundary: the estimate advances and the status bar names what is now running.
    ///
    /// The accounting is done on the UI thread rather than on the worker that raised the event, so
    /// <see cref="RunProgress"/> never has to be thread-safe — it is only ever touched from here and
    /// from the timer, which are the same thread. The cost is that a boundary is timed a few
    /// milliseconds late, against phases measured in seconds.
    /// </summary>
    private void OnStageEntered(string name) => Post(() =>
    {
        if (!_busy) return;

        _progressModel?.Enter(name);
        _phase = Sentence(name);
        _status.Text = $"{_phase}…";
        ShowProgress();
    });

    /// <summary>A span inside the running phase — shown, but never counted toward progress.</summary>
    private void OnStageDetail(string name) => Post(() =>
    {
        if (_busy && _phase is not null) _status.Text = $"{_phase} · {name.Trim(' ', '·')}…";
    });

    private string? _phase;

    private static string Sentence(string name)
        => $"{char.ToUpperInvariant(name[0])}{name[1..]}";

    /// <summary>Runs an action on the UI thread, tolerating the window having gone away.</summary>
    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;

        try
        {
            BeginInvoke(action);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The window went away between the check above and the post. Nothing to update.
        }
    }

    // --- Views ----------------------------------------------------------------------------------

    /// <summary>Takes a finished run and drops every cached render of the previous one.</summary>
    private void ShowResult(GenerationResult result)
    {
        _result = result;

        // A Bitmap is unmanaged memory the collector is in no hurry about, and preview is meant to
        // be clicked repeatedly while a setting is tuned; leaking one per view per click adds up.
        _viewer.SetImage(null);
        foreach (var bitmap in _rendered.Values) bitmap.Dispose();
        _rendered.Clear();

        SelectView(_view);
    }

    private void SelectView(string name)
    {
        _view = name;

        foreach (var (key, button) in _viewButtons)
        {
            bool on = key == name;
            button.BackColor = on ? Theme.Accent : Theme.SurfaceHigh;
            button.ForeColor = on ? Theme.AccentText : Theme.Text;
            button.FlatAppearance.MouseOverBackColor = on ? Theme.Accent : Theme.Border;
        }

        _viewer.ViewName = name;

        if (_result is null)
        {
            _viewer.SetImage(null);
            return;
        }

        if (!_rendered.TryGetValue(name, out var bitmap))
        {
            var render = Views.First(v => v.Name == name).Render;
            using (new WaitCursorFor(this)) bitmap = ToBitmap(render(_result));
            _rendered[name] = bitmap;
        }

        _viewer.SetImage(bitmap);
        ShowReadout(_viewer.Zoom, null);
    }

    private void ShowReadout(float zoom, Point? pixel)
    {
        string where = pixel is { } p ? $"   {p.X}, {p.Y} px" : "";
        _readout.Text = _result is null ? "" : $"{_view}   {zoom * 100:F0}%{where}";
    }

    private sealed class WaitCursorFor : IDisposable
    {
        private readonly Form _form;
        public WaitCursorFor(Form form) { _form = form; form.Cursor = Cursors.WaitCursor; }
        public void Dispose() => _form.Cursor = Cursors.Default;
    }

    /// <summary>Packed RGB to a 24bpp bitmap, one row at a time to respect the stride.</summary>
    private static Bitmap ToBitmap(PreviewRenderer.Image image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, image.Width, image.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            var row = new byte[image.Width * 3];
            for (int y = 0; y < image.Height; y++)
            {
                // Bitmap wants BGR; the renderers produce RGB.
                int src = y * image.Width * 3;
                for (int x = 0; x < image.Width; x++)
                {
                    row[x * 3 + 0] = image.Rgb[src + x * 3 + 2];
                    row[x * 3 + 1] = image.Rgb[src + x * 3 + 1];
                    row[x * 3 + 2] = image.Rgb[src + x * 3 + 0];
                }
                System.Runtime.InteropServices.Marshal.Copy(
                    row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>Routes Console output into the log pane, marshalling back onto the UI thread.</summary>
    private sealed class TextBoxWriter(TextBox target) : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value) => Write(value.ToString());

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value) || target.IsDisposed) return;

            if (target.InvokeRequired) target.BeginInvoke(() => Append(value));
            else Append(value);
        }

        public override void WriteLine(string? value) => Write((value ?? string.Empty) + "\r\n");

        private void Append(string value)
        {
            if (target.IsDisposed) return;
            target.AppendText(value.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }
    }
}
