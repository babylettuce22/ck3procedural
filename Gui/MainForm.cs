using System.Diagnostics;
using System.Drawing.Imaging;
using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.Gui;

/// <summary>
/// One window: choose a heightmap, tune the settings, look at what they produce, write the mod.
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

    private readonly Button _launchGame = Theme.MakeButton("Launch CK3", 100);
    private readonly TextBox _launchArgs = new()
    {
        Width = 115,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.SurfaceHigh,
        ForeColor = Theme.Text,
        Margin = new Padding(2, 5, 4, 3),
    };

    private readonly Button _gameFolder = Theme.MakeButton("Game folder…", 104);

    private readonly ToolTip _tips = new() { AutoPopDelay = 20000, InitialDelay = 400 };
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
        HideSelection = false,
    };

    private readonly TextBox _logSearch = new()
    {
        Width = 120,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.SurfaceHigh,
        ForeColor = Theme.Text,
        Margin = new Padding(8, 5, 3, 3),
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

    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 200 };

    private RunProgress? _progressModel;
    private SplitContainer _body = null!;
    private SplitContainer _right = null!;

    private static readonly (string Name,
        Func<GenerationResult, Emit.WrittenContent?, PreviewRenderer.Image> Render)[] Views =
        [
        ("Relief", (r, _) => PreviewRenderer.RenderRelief(r)),
        ("Heightmap", (r, _) => PreviewRenderer.RenderHeightmap(r)),
        ("Terrain", (r, _) => PreviewRenderer.RenderTerrain(r)),
        ("Climate", (r, _) => PreviewRenderer.RenderClimate(r)),
        ("Drainage", (r, _) => PreviewRenderer.RenderDrainage(r)),
        ("Rivers", (r, _) => PreviewRenderer.RenderRivers(r)),
        ("Provinces", (r, _) => PreviewRenderer.RenderProvinces(r)),
        ("Counties", (r, _) => PreviewRenderer.RenderCounties(r)),
        ("Duchies", (r, _) => PreviewRenderer.RenderDuchies(r)),
        ("Kingdoms", (r, _) => PreviewRenderer.RenderKingdoms(r)),
        ("Empires", (r, _) => PreviewRenderer.RenderEmpires(r)),
        ("Realms", (r, w) => PreviewRenderer.RenderRealms(r, w?.Realms, w?.Wilderness)),
        ("Cultures", (r, w) => PreviewRenderer.RenderCultures(r, w?.Cultures, w?.Wilderness)),
        ("Faiths", (r, w) => PreviewRenderer.RenderFaiths(r, w?.Faiths, w?.Wilderness)),
        ("Government", (r, _) => PreviewRenderer.RenderGovernment(r)),
        ("Wilderness", (r, _) => PreviewRenderer.RenderWilderness(r)),
    ];

    private enum Pick { Title, Culture, Faith }

    private static readonly Dictionary<string, (Pick Kind, string Tier)> ClickableViews = new()
    {
        ["Counties"] = (Pick.Title, "c"),
        ["Duchies"] = (Pick.Title, "d"),
        ["Kingdoms"] = (Pick.Title, "k"),
        ["Empires"] = (Pick.Title, "e"),
        ["Realms"] = (Pick.Title, "c"),
        ["Cultures"] = (Pick.Culture, "c"),
        ["Faiths"] = (Pick.Faith, "c"),
    };

    private static bool Repaints(Pick kind, Emit.WorldAspect touched) => kind switch
    {
        Pick.Title => touched.HasFlag(Emit.WorldAspect.TitleColors),
        Pick.Culture => touched.HasFlag(Emit.WorldAspect.Cultures),
        _ => touched.HasFlag(Emit.WorldAspect.Faiths),
    };

    private readonly Dictionary<string, Button> _viewButtons = [];
    private readonly Dictionary<string, Bitmap> _rendered = [];
    private GenerationResult? _result;
    private string _view = "Counties";

    private readonly WorldEdits _edits = new();
    private readonly TitleEditor _titles;

    private readonly Panel _pendingBar = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        BackColor = Theme.Notice,
        Visible = false,
    };

    private readonly Button _overwrite = Theme.MakeButton("Overwrite mod", 116, primary: true);
    private readonly Button _revertAll = Theme.MakeButton("Revert all", 84);

    private readonly Label _pendingText = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.NoticeText,
        Font = Theme.Ui,
        Padding = new Padding(8, 0, 0, 0),
    };

    /// <summary>
    /// The settings <see cref="MapGen.HeightmapNormalizer"/> reads. Changing any of them changes
    /// what the game would be handed, so the 3D view has to be rebuilt.
    /// </summary>
    private static readonly HashSet<string> NormalizationSettings =
    [
        nameof(MapConfig.Normalization),
        nameof(MapConfig.SourceSeaLevel),
        nameof(MapConfig.LandTop),
        nameof(MapConfig.LandTopPercentile),
        nameof(MapConfig.LandFloorDensity),
    ];

    private bool _sourceShown;
    private int _sourceGeneration;
    private readonly HeightfieldPanel _solid = new() { Dock = DockStyle.Fill };

    private readonly ComboBox _sourceMode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 158,
        Font = Theme.Ui,
        FlatStyle = FlatStyle.Flat,
    };

    private readonly TrackBar _exaggeration = new()
    {
        Minimum = 20,
        Maximum = 400,
        Value = 100,
        TickStyle = TickStyle.None,
        Width = 110,
        Height = 24,
    };

    private readonly Label _sourceReadout = new()
    {
        AutoSize = true,
        ForeColor = Theme.TextDim,
        Font = Theme.Ui,
        Padding = new Padding(0, 5, 0, 0),
    };

    private MapGen.HeightmapImage? _loaded;
    private IReadOnlyList<MapGen.HeightmapWarning> _warnings = [];
    private Emit.WrittenContent? _written;
    private string? _heightmapPath;
    private bool _busy;
    private CancellationTokenSource? _cancellation;
    private string _modRoot = "";
    private string _modName = GenerationOptions.DefaultModName;
    private string? _lastModDir;

    public MainForm(GenerationOptions options)
    {
        _options = options;

        _heightmapPath = options.HeightmapPath
            ?? (File.Exists(_state.HeightmapPath) ? _state.HeightmapPath : null);
        options.HeightmapPath = _heightmapPath;

        if (Core.GameLocator.IsGameDir(_state.GameDir)) options.GameDir = _state.GameDir!;

        _modRoot = Directory.Exists(_state.ModRoot) ? _state.ModRoot! : GenerationOptions.ModRoot;
        _modName = _state.ModName ?? GenerationOptions.DefaultModName;

        Text = "CK3 Procedural Map";
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(1000, 640);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        KeyPreview = true;

        if (File.Exists("app.ico"))
        {
            Icon = new Icon("app.ico");
        }
        else if (Icon.ExtractAssociatedIcon(Application.ExecutablePath) is { } exeIcon)
        {
            Icon = exeIcon;
        }

        Theme.ApplyLight(_grid);
        _grid.SelectedObject = _options.Config;

        // The 3D view shows the *normalised* heightmap, so the settings that decide normalisation
        // change what it shows. Rebuilding on those and only those: everything else on this grid
        // affects generation, which this view deliberately runs ahead of.
        _grid.PropertyValueChanged += (_, e) =>
        {
            string? changed = e.ChangedItem?.PropertyDescriptor?.Name;
            if (changed is null || !NormalizationSettings.Contains(changed)) return;
            if (_sourceShown) _ = ShowSourceAsync();
        };

        _options.Config.Seed = Random.Shared.Next(1, int.MaxValue);
        _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
        _seed.ValueChanged += (_, _) => _options.Config.Seed = (int)_seed.Value;

        _launchArgs.Text = _state.LaunchArgs ?? "-debug_mode";
        _tips.SetToolTip(_launchArgs, "Launch arguments passed to ck3.exe (e.g. -debug_mode -mapeditor -novid)");

        _browse.Click += (_, _) => PickHeightmap();
        _roll.Click += (_, _) => _seed.Value = Random.Shared.Next(1, int.MaxValue);
        _preview.Click += async (_, _) => await PreviewAsync();
        _writeMod.Click += async (_, _) => await WriteModAsync();
        _cancel.Click += (_, _) => RequestCancel();
        _openMod.Click += (_, _) => OpenModFolder();
        _launchGame.Click += (_, _) => LaunchGame();
        _gameFolder.Click += (_, _) => PickGameFolder();
        _savePreset.Click += (_, _) => SavePreset();
        _loadPreset.Click += (_, _) => LoadPreset();

        _titles = new TitleEditor(_edits) { Dock = DockStyle.Fill };

        _viewer.ViewChanged += ShowReadout;
        _viewer.PixelClicked += PickTitleAt;
        _titles.SelectionChanged += titles => { if (titles.Count > 0) Inspect([.. titles]); };
        _edits.Changed += OnEditsChanged;

        if (_state.View is { } remembered && Views.Any(v => v.Name == remembered)) _view = remembered;

        Controls.Add(BuildBody());
        Controls.Add(BuildPendingBar());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildStatusBar());

        _lastModDir = _state.LastModDir;

        ApplySource();
        SelectView(_view);

        Console.SetOut(new TextBoxWriter(_log));
        Stage.Entering += OnStageEntered;
        Stage.Detailing += OnStageDetail;
        _tick.Tick += (_, _) => ShowProgress();
    }

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
        bar.Controls.Add(_launchGame);
        bar.Controls.Add(_launchArgs);
        bar.Controls.Add(_gameFolder);
        bar.Controls.Add(_sourceName);

        return bar;
    }

    private Control BuildPendingBar()
    {
        _overwrite.Click += (_, _) => OverwriteTitles();
        _revertAll.Click += (_, _) => RevertAll();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            Padding = new Padding(6, 3, 0, 0),
            BackColor = Color.Transparent,
        };
        buttons.Controls.Add(_overwrite);
        buttons.Controls.Add(_revertAll);

        _pendingBar.Controls.Add(_pendingText);
        _pendingBar.Controls.Add(buttons);
        _pendingBar.Controls.Add(new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = Theme.NoticeBorder,
        });

        return _pendingBar;
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

        var tabs = Theme.MakeTabs();

        // Loaded when the tab is first opened, not at startup: decoding a vanilla-sized heightmap
        // is several seconds, and a window that takes that long to appear for a view nobody asked
        // for is a worse trade than a view that takes a moment to fill in.
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab?.Text == "Source 3D" && !_sourceShown)
            {
                _sourceShown = true;
                _ = ShowSourceAsync();
            }
        };

        var mapTab = new TabPage("Map") { BackColor = Theme.Background };
        mapTab.Controls.Add(viewer);

        var titleTab = new TabPage("Titles") { BackColor = Theme.Background };
        titleTab.Controls.Add(_titles);

        var sourceTab = new TabPage("Source 3D") { BackColor = Theme.Background };
        sourceTab.Controls.Add(BuildSourceView());

        tabs.TabPages.Add(mapTab);
        tabs.TabPages.Add(sourceTab);
        tabs.TabPages.Add(titleTab);

        var logPane = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        logPane.Controls.Add(_log);
        logPane.Controls.Add(BuildLogHeader());

        _right = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = Theme.Border,
        };
        _right.Panel1.Controls.Add(tabs);
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

    /// <summary>
    /// The 3D view of the heightmap as loaded, before anything is generated.
    ///
    /// Its own tab rather than another entry in <see cref="Views"/> because every one of those
    /// takes a <see cref="GenerationResult"/>, and the entire point of this one is that it works
    /// with nothing but a file on disk.
    /// </summary>
    private Control BuildSourceView()
    {
        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(4, 3, 4, 0),
            BackColor = Theme.Surface,
        };

        _sourceMode.Items.AddRange(["Heightmap as loaded", "As CK3 will render it"]);
        _sourceMode.SelectedIndex = 0;
        _sourceMode.SelectedIndexChanged += (_, _) =>
        {
            _solid.ShowAsCk3Renders = _sourceMode.SelectedIndex == 1;
            _solid.Refresh3d();
        };

        _exaggeration.ValueChanged += (_, _) =>
        {
            _solid.SetExaggeration(_exaggeration.Value / 100.0);
            _sourceReadout.Text = Readout();
        };

        var reset = Theme.MakeButton("Reset view", 82);
        reset.Click += (_, _) => _solid.ResetView();

        strip.Controls.Add(_sourceMode);
        strip.Controls.Add(new Label
        {
            Text = "Relief",
            AutoSize = false,
            Width = 40,
            Height = 24,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui,
        });
        strip.Controls.Add(_exaggeration);
        strip.Controls.Add(reset);
        strip.Controls.Add(_sourceReadout);

        _solid.ViewChanged += _ => _sourceReadout.Text = Readout();
        _sourceReadout.Text = Readout();

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        host.Controls.Add(_solid);
        host.Controls.Add(strip);
        return host;
    }

    private string Readout()
    {
        var v = _solid.View;
        // 1.00x is CK3's own vertical scale, so it is worth naming rather than leaving the reader
        // to guess which end of the slider is the truthful one.
        string relief = Math.Abs(v.Exaggeration - 1.0) < 0.005
            ? "relief 1.00× (approximate to game)"
            : $"relief {v.Exaggeration:F2}×";

        return $"   {v.Yaw * 180 / Math.PI % 360:F0}°  ·  pitch {v.Pitch * 180 / Math.PI:F0}°  " +
               $"·  zoom {1 / v.Distance:F2}×  ·  {relief}" +
               "      drag to orbit · right-drag to pan · wheel to zoom · double-click to reset";
    }

    /// <summary>
    /// Loads the chosen heightmap and hands it to the 3D view, without generating anything.
    ///
    /// The decode, the normalisation and the packer reconstruction all run off the UI thread — the
    /// first is seconds on a vanilla-sized map and the last is not much quicker. The result is
    /// cached in <see cref="_loaded"/>, which is the same field the generator reads, so opening the
    /// 3D view first makes the subsequent build faster rather than slower.
    /// </summary>
    private async Task ShowSourceAsync()
    {
        if (_heightmapPath is null)
        {
            _solid.SetField(null, null, "Choose a heightmap to see it in 3D.");
            return;
        }

        string path = _heightmapPath;
        var cfg = _options.Config;

        // Settings can be changed faster than a full-size heightmap can be normalised and packed,
        // and the tasks do not finish in the order they started. Only the newest one is allowed to
        // publish, or a stale frame silently wins and the view stops matching the settings.
        int generation = ++_sourceGeneration;

        _solid.SetField(null, null, "Reading the heightmap…");

        try
        {
            var (source, packed, warnings, loaded) = await Task.Run(() =>
            {
                var image = _loaded is not null && _loaded.StillStandsFor(path)
                    ? _loaded
                    : MapGen.HeightmapSource.Read(path, cfg);

                MapGen.HeightmapSource.Apply(image, cfg);
                var found = MapGen.HeightmapSource.Diagnose(image, cfg);

                // Normalised, because that is what the game is handed. A heightmap drawn on
                // somebody else's height scale looks perfectly reasonable as a PNG and ships as a
                // plateau with a wall at every shoreline, and this view exists to show that.
                var levels = MapGen.HeightmapNormalizer.Normalize(image.Raw, cfg);

                var field = Heightfield.Downsample(levels, image.Width, image.Height, Heightfield.PreviewCols);
                var asRendered = Heightfield.Downsample(
                    Emit.HeightmapPacker.Reconstruct(levels, image.Width, image.Height),
                    image.Width, image.Height, Heightfield.PreviewCols);

                return (field, asRendered, found, image);
            });

            if (generation != _sourceGeneration) return;

            _loaded = loaded;
            _warnings = warnings;

            _solid.SetField(source, packed, "Nothing to show.");
            _grid.Refresh();

            _status.Text = $"{Path.GetFileName(path)} — {loaded.Width}×{loaded.Height}, " +
                           $"{100 * source.LandShare:F1}% land" +
                           (warnings.Count == 0 ? "" : $", {warnings.Count} warning(s)");
        }
        catch (Exception error)
        {
            if (generation != _sourceGeneration) return;
            _solid.SetField(null, null, $"Could not read it: {error.Message}");
        }
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

        _logSearch.TextChanged += (_, _) => SearchLog(next: false);
        _logSearch.KeyDown += (sender, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SearchLog(next: true);
            }
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
        header.Controls.Add(Caption("Search"));
        header.Controls.Add(_logSearch);
        return header;
    }

    private void SearchLog(bool next)
    {
        string query = _logSearch.Text;
        if (string.IsNullOrEmpty(query) || _log.TextLength == 0) return;

        int start = _log.SelectionStart;
        if (next && start >= 0)
        {
            start += _log.SelectionLength > 0 ? 1 : 0;
        }
        else
        {
            start = 0;
        }

        int index = _log.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);

        if (index == -1 && start > 0)
        {
            index = _log.Text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
        }

        if (index != -1)
        {
            _log.Select(index, query.Length);
            _log.ScrollToCaret();
        }
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

        ReportFolders();
    }

    private void ReportFolders()
    {
        bool found = Core.GameLocator.IsGameDir(_options.GameDir);

        Console.WriteLine(found
            ? $"Game folder: {_options.GameDir}"
            : $"Game folder: not found (looked in the usual Steam, GOG and Epic places on every drive)");
        Console.WriteLine($"Mod folder:  {_modRoot}");
        Console.WriteLine();

        ShowGameFolder();

        if (!found) _status.Text = "Crusader Kings III not found — set the game folder before writing a mod";
    }

    private void ShowGameFolder()
    {
        bool found = Core.GameLocator.IsGameDir(_options.GameDir);

        _tips.SetToolTip(_gameFolder, found
            ? $"Reading the game's own data from:\n{_options.GameDir}"
            : $"Crusader Kings III was not found. Click to point the tool at the 'game' folder "
              + $"of your install.\n\nLast tried: {_options.GameDir}");

        _gameFolder.Text = found ? "Game folder…" : "Game folder ⚠";
        _gameFolder.ForeColor = found ? Theme.Text : Theme.Danger;
    }

    private void PickGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "The 'game' folder of your Crusader Kings III install",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_options.GameDir) ? _options.GameDir : "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string? resolved = Core.GameLocator.Normalize(dialog.SelectedPath);
        if (resolved is null)
        {
            MessageBox.Show(this,
                $"There is no Crusader Kings III game data in\n\n{dialog.SelectedPath}\n\n"
                + "The folder wanted is the one holding common, map_data and gfx — normally "
                + @"…\steamapps\common\Crusader Kings III\game.",
                "Not a game folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _options.GameDir = resolved;
        Console.WriteLine($"Game folder: {resolved}");
        ShowGameFolder();
        _status.Text = "Game folder set";
    }

    private bool EnsureGameFolder()
    {
        if (Core.GameLocator.IsGameDir(_options.GameDir)) return true;

        var answer = MessageBox.Show(this,
            "Crusader Kings III could not be found on this machine, and the mod is generated "
            + "against the game's own culture, religion and map data — so it cannot be written "
            + "without it.\n\n"
            + $"Last tried:\n{_options.GameDir}\n\n"
            + "Point the tool at the 'game' folder of your install now?",
            "Game folder not found", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

        if (answer != DialogResult.OK) return false;

        PickGameFolder();
        return Core.GameLocator.IsGameDir(_options.GameDir);
    }

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

        _state.GameDir = Core.GameLocator.IsGameDir(_options.GameDir) ? _options.GameDir : null;
        _state.ModRoot = _modRoot;
        _state.ModName = _modName;
        _state.LastModDir = _lastModDir;
        _state.LaunchArgs = _launchArgs.Text;
        _state.Save();

        Stage.Entering -= OnStageEntered;
        Stage.Detailing -= OnStageDetail;
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys key)
    {
        switch (key)
        {
            case Keys.F5 when _preview.Enabled:
                _ = PreviewAsync();
                return true;

            case Keys.Control | Keys.S when _writeMod.Enabled:
                _ = WriteModAsync();
                return true;

            case Keys.Escape when _busy:
                RequestCancel();
                return true;
        }

        return base.ProcessCmdKey(ref message, key);
    }

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

        if (_sourceShown) _ = ShowSourceAsync();
    }

    private void ApplySource()
    {
        _sourceName.Text = _heightmapPath is null
            ? "(no heightmap chosen)"
            : Path.GetFileName(_heightmapPath);

        _openMod.Enabled = ModFolderToOpen() is not null;
        SetEnabled(!_busy);

        ShowGameFolder();
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

    private string? ModFolderToOpen()
    {
        if (Directory.Exists(_lastModDir)) return _lastModDir;
        return Directory.Exists(_modRoot) ? _modRoot : null;
    }

    private void OpenModFolder()
    {
        if (ModFolderToOpen() is not { } dir) return;

        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private void LaunchGame()
    {
        if (string.IsNullOrWhiteSpace(_options.GameDir) || !Core.GameLocator.IsGameDir(_options.GameDir))
        {
            MessageBox.Show(this,
                "Please configure a valid game folder before launching Crusader Kings III.",
                "Game folder not configured", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? gameRoot = Path.GetDirectoryName(_options.GameDir);
        if (gameRoot is null) return;

        string exePath = Path.Combine(gameRoot, "binaries", "ck3.exe");
        if (!File.Exists(exePath))
        {
            MessageBox.Show(this,
                $"Could not find the game executable at:\n\n{exePath}\n\nPlease check your game folder configuration.",
                "Executable not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Launching closes this window, and the pending edits are the one thing it holds that only
        // lives in memory — everything else is on disk or saved by OnFormClosing on the way out.
        if (_edits.HasPending)
        {
            var answer = MessageBox.Show(this,
                $"Launching closes the generator, which discards "
                + $"{Count(_edits.EditedCount, "unsaved edit")}.\n\n"
                + "Press Overwrite first to push them into the mod folder.\n\n"
                + "Launch anyway?",
                "Unsaved edits", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            if (answer != DialogResult.OK) return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(exePath)
            {
                WorkingDirectory = Path.Combine(gameRoot, "binaries"),
                UseShellExecute = true
            };

            string args = _launchArgs.Text.Trim();
            if (!string.IsNullOrEmpty(args))
            {
                startInfo.Arguments = args;
            }

            Process.Start(startInfo);

            // ShellExecute has already handed the game to the OS by the time Start returns, and
            // nothing about it is tied to this process, so closing now leaves it running. Going
            // out through Close() rather than Application.Exit is what keeps OnFormClosing — and
            // so the saved window state — exactly as it is for a window closed by hand.
            Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to launch CK3: {ex.Message}");
            MessageBox.Show(this,
                $"An error occurred while launching Crusader Kings III:\n\n{ex.Message}",
                "Launch Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task WriteModAsync()
    {
        if (_busy || _heightmapPath is null) return;

        if (_edits.EditedCount > 0)
        {
            var discard = MessageBox.Show(this,
                $"Writing the mod rebuilds the world from the current settings, so all "
                + $"{Count(_edits.EditedCount, "edit")} go back to generated values.\n\n"
                + "This includes edits already pushed with Overwrite: the files holding them are "
                + "rewritten as part of the run.\n\n"
                + "Write the mod anyway?",
                "Edits will be regenerated", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            if (discard != DialogResult.OK) return;
        }

        if (!EnsureGameFolder()) return;

        using var dialog = new ModNameDialog(_modRoot, _modName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _modRoot = dialog.ModRoot;
        _modName = dialog.ModDisplayName;
        _options.ModName = dialog.ModDisplayName;

        string modDir = dialog.ModDir;
        await BuildAsync(modDir);

        if (Directory.Exists(modDir)) _lastModDir = modDir;

        if (_result is not null && _written is not null && Directory.Exists(modDir))
        {
            _edits.Attach(_result, _written, modDir);
            Console.WriteLine();
            Console.WriteLine("The world can now be edited — click any title, culture or faith map.");

            OfferToEnableMod(modDir);
        }

        ApplySource();
    }

    /// <summary>
    /// Asks whether the game should be set to load what was just written, and edits
    /// <c>dlc_load.json</c> if so. Only ever runs after a write that actually landed, and only
    /// asks when the answer would change something — a mod already listed says so in the log and
    /// costs no click.
    /// </summary>
    private void OfferToEnableMod(string modDir)
    {
        string? file = Core.DlcLoad.FileFor(modDir);
        if (file is null)
        {
            Console.WriteLine("Written outside the launcher's mod folder — dlc_load.json left alone. "
                              + "Enable the mod from the launcher.");
            return;
        }

        string entry = Core.DlcLoad.EntryFor(modDir);
        if (Core.DlcLoad.IsOnly(file, entry))
        {
            Console.WriteLine($"Crusader Kings III is already set to load {entry}, and only it.");
            return;
        }

        // Named rather than counted. Turning off somebody's mod list is a thing they should be able
        // to put back by hand, and the file that recorded it is the file about to be overwritten.
        var dropped = Core.DlcLoad.Enabled(file)
            .Where(m => !string.Equals(m, entry, StringComparison.OrdinalIgnoreCase))
            .ToList();

        string turningOff = dropped.Count == 0 ? "" :
            "A generated map is a total conversion, so it has to load on its own — "
            + $"{Count(dropped.Count, "other enabled mod")} will be turned off:\n"
            + string.Join("\n", dropped.Take(8).Select(m => $"    {m}"))
            + (dropped.Count > 8 ? $"\n    ...and {dropped.Count - 8} more" : "")
            + "\n\n";

        var answer = MessageBox.Show(this,
            $"Crusader Kings III can be set to load \"{_modName}\" the next time it starts.\n\n"
            + turningOff
            + "This only edits dlc_load.json — the game is not launched and no mod is deleted, so "
            + "anything turned off here can be ticked again in the launcher.\n\n"
            + "Note that opening the Paradox launcher afterwards can undo it, because the launcher "
            + "rewrites that file from its own playsets.\n\n"
            + "Enable it?",
            "Enable the mod?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
        {
            Console.WriteLine("Left disabled — dlc_load.json unchanged.");
            return;
        }

        try
        {
            Core.DlcLoad.EnableOnly(file, entry);
            Console.WriteLine($"Enabled {entry} — Crusader Kings III will load it, and only it, "
                              + "on the next start.");
            foreach (string mod in dropped) Console.WriteLine($"  turned off: {mod}");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(ex);

            MessageBox.Show(this,
                $"dlc_load.json could not be updated:\n\n{ex.Message}\n\n"
                + "The mod itself is written, so it can still be enabled from the launcher.",
                "Could not enable the mod", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OverwriteTitles()
    {
        if (_busy || !_edits.HasPending || _edits.Target is not { } target) return;

        var aspects = _edits.Pending;
        int edited = _edits.EditedCount;

        try
        {
            using (new WaitCursorFor(this))
                Emit.WorldOverwrite.Apply(target.ModDir, target.Result, target.Written, aspects);

            _edits.MarkWritten();
            Emit.WorldOverwrite.Report(aspects, edited, target.ModDir);
            _status.Text = $"Edits written to {target.ModDir}";
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(ex);
            _status.Text = "Overwrite failed — see log";

            MessageBox.Show(this,
                $"The mod could not be updated:\n\n{ex.Message}",
                "Overwrite failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task PreviewAsync()
    {
        if (_edits.HasPending)
        {
            int edited = _edits.EditedCount;

            var answer = MessageBox.Show(this,
                $"Previewing rebuilds the world from the current settings, which discards "
                + $"{Count(edited, "unsaved edit")}.\n\n"
                + "The mod folder keeps whatever you last pressed Overwrite for — only the unsaved "
                + "changes are lost.\n\n"
                + "Preview anyway?",
                "Unsaved edits", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            if (answer != DialogResult.OK) return;
        }

        await BuildAsync(null);
    }

    private static string Count(int n, string noun)
        => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private void OnProgressivePreview(string viewName, PreviewRenderer.Image image)
    {
        Post(() =>
        {
            var bitmap = ToBitmap(image);

            if (_rendered.TryGetValue(viewName, out var old))
            {
                old.Dispose();
            }
            _rendered[viewName] = bitmap;

            // Instantly update on-screen if this is the active tab
            if (_view == viewName)
            {
                _viewer.SetImage(bitmap);
                ShowReadout(_viewer.Zoom, null);
            }
        });
    }

    private async Task BuildAsync(string? modDir)
    {
        var (result, cancelled) = await RunAsync(
            modDir is null ? "Building preview…" : "Writing mod…",
            () =>
            {
                var cfg = _options.Config;

                Stage.Time("heightmap decode", () =>
                {
                    if (_loaded is null || !_loaded.StillStandsFor(_heightmapPath!))
                        _loaded = MapGen.HeightmapSource.Read(_heightmapPath!, cfg);
                    else
                        MapGen.HeightmapSource.Apply(_loaded, cfg);

                    _warnings = MapGen.HeightmapSource.Diagnose(_loaded, cfg);
                });

                var terra = Stage.Time("province elevation",
                    () => MapGen.TerrainData.FromElevation(_loaded!.ToElevation(cfg), cfg));

                var r = Generator.FromTerrain(terra, cfg, OnProgressivePreview);

                _written = null;
                if (modDir is not null) _written = Generator.WriteMod(r, _options, modDir);
                return r;
            },
            writing: modDir is not null);

        if (cancelled)
        {
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

        ShowHeightmapWarnings(modDir);
    }

    private void ShowHeightmapWarnings(string? modDir)
    {
        if (_warnings.Count == 0) return;

        var text = new System.Text.StringBuilder();
        text.AppendLine(_warnings.Count == 1
            ? "This heightmap has a problem that will be visible in game:"
            : $"This heightmap has {_warnings.Count} problems that will be visible in game:");

        foreach (var warning in _warnings)
        {
            text.AppendLine();
            text.AppendLine($"• {warning.Title}");
            text.AppendLine();
            text.AppendLine(warning.Detail);
        }

        if (modDir is not null)
        {
            text.AppendLine();
            text.AppendLine("The mod has been written anyway, so it is loadable — fix the settings "
                            + "or the source and write it again.");
        }

        MessageBox.Show(this, text.ToString(),
            modDir is null ? "Heightmap warnings" : "Heightmap warnings — mod written",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async Task<(GenerationResult? Result, bool Cancelled)> RunAsync(
        string message, Func<GenerationResult> work, bool writing)
    {
        if (_busy) return (null, false);

        _busy = true;
        SetEnabled(false);
        _log.Clear();
        _status.Text = message;

        _progressModel = new RunProgress(
            Plan(writing),
            () => _options.Config.ProvinceWidth / 1000.0 * _options.Config.ProvinceHeight / 1000.0);

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

            var measured = _progressModel.Finish();
            if (writing) _state.WriteRun = measured; else _state.PreviewRun = measured;

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

    private RunProfile Plan(bool writing)
    {
        var learned = writing ? _state.WriteRun : _state.PreviewRun;
        if (learned.Phases.Count > 0) return learned;

        var shipped = RunProgress.Shipped(writing);
        return writing ? RunProgress.Blend(shipped, _state.PreviewRun) : shipped;
    }

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
            _eta.Text = $"{_progressModel.Elapsed.TotalSeconds:F0}s elapsed";
        }
    }

    private void RequestCancel()
    {
        if (_cancellation is null || _cancellation.IsCancellationRequested) return;

        _cancellation.Cancel();
        _cancel.Enabled = false;
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
        _gameFolder.Enabled = enabled;
        _launchGame.Enabled = enabled;
        _launchArgs.Enabled = enabled;
        _cancel.Enabled = !enabled;

        _titles.Enabled = enabled;
        ShowPending();

        bool ready = enabled && _heightmapPath is not null;
        _writeMod.Enabled = ready;
        _preview.Enabled = ready;
    }

    private void OnStageEntered(string name) => Post(() =>
    {
        if (!_busy) return;

        _progressModel?.Enter(name);
        _phase = Sentence(name);
        _status.Text = $"{_phase}…";
        ShowProgress();
    });

    private void OnStageDetail(string name) => Post(() =>
    {
        if (_busy && _phase is not null) _status.Text = $"{_phase} · {name.Trim(' ', '·')}…";
    });

    private string? _phase;

    private static string Sentence(string name)
        => $"{char.ToUpperInvariant(name[0])}{name[1..]}";

    private bool Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return false;

        try
        {
            BeginInvoke(action);
            return true;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    private void ShowResult(GenerationResult result)
    {
        _result = result;

        _edits.Detach();

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

        if (_result is null && !_rendered.ContainsKey(name))
        {
            _viewer.SetImage(null);
            return;
        }

        if (!_rendered.TryGetValue(name, out var bitmap) && _result is not null)
        {
            var render = Views.First(v => v.Name == name).Render;
            using (new WaitCursorFor(this)) bitmap = ToBitmap(render(_result, _written));
            _rendered[name] = bitmap;
        }

        _viewer.SetImage(bitmap);
        ShowReadout(_viewer.Zoom, null);
    }

    private void PickTitleAt(Point pixel)
    {
        if (_busy || _edits.Target is not { } target || _result is null) return;
        if (!ClickableViews.TryGetValue(_view, out var view)) return;

        var map = _result.Provinces;
        int step = PreviewRenderer.StepFor(map.Width);

        int x = Math.Clamp(pixel.X * step, 0, map.Width - 1);
        int y = Math.Clamp(pixel.Y * step, 0, map.Height - 1);

        int id = _result.ProvinceOrder[map.Label[y * map.Width + x]];
        if (id < 1 || id > _result.BaronyCount)
        {
            _status.Text = "Nothing there — that is water or impassable";
            return;
        }

        var barony = MapGen.Titles.Flatten(_result.Titles)
            .FirstOrDefault(t => t.Tier == "b" && t.ProvinceId == id);
        if (barony is null) return;

        var title = barony;
        while (title is not null && title.Tier != view.Tier) title = title.Parent;
        if (title is null) return;

        switch (view.Kind)
        {
            case Pick.Title:
                _titles.Reveal(title);
                _status.Text = $"{TitleInspector.TierName(title)} {title.Name}";
                break;

            case Pick.Culture:
                var culture = target.Written.Cultures.For(title);
                Inspect([culture]);
                _status.Text = $"Culture {culture.Name} — {title.Name}";
                break;

            case Pick.Faith:
                var faith = target.Written.Faiths.For(title);
                Inspect([faith]);
                _status.Text = $"Faith {faith.Name} — {title.Name}";
                break;
        }
    }

    private readonly Dictionary<Type, InspectorForm> _inspectors = [];

    private void Inspect(IReadOnlyList<object> targets)
    {
        if (targets.Count == 0) return;

        var kind = targets[0].GetType();

        if (!_inspectors.TryGetValue(kind, out var inspector) || inspector.IsDisposed)
        {
            inspector = kind.Name switch
            {
                nameof(MapGen.Culture) => new CultureInspector(_edits),
                nameof(MapGen.Faith) => new FaithInspector(_edits),
                _ => new TitleInspector(_edits),
            };

            inspector.Navigate += Inspect;
            _inspectors[kind] = inspector;

            inspector.Show(this);
            PlaceInspector(inspector);
        }

        inspector.Inspect(targets);

        if (!inspector.Visible) inspector.Show(this);
        inspector.BringToFront();
    }

    private void ShowPending()
    {
        _pendingBar.Visible = _edits.HasPending;
        if (!_edits.HasPending) return;

        int edited = _edits.EditedCount;

        _pendingText.Text = edited switch
        {
            0 => "Edits reverted — the mod on disk has not caught up",
            1 => "1 unsaved edit — the mod on disk still has the generated value",
            _ => $"{edited} unsaved edits — the mod on disk still has the generated values",
        };

        _overwrite.Enabled = !_busy;
        _revertAll.Enabled = !_busy && edited > 0;
    }

    private void RevertAll()
    {
        if (_edits.EditedCount == 0) return;

        var answer = MessageBox.Show(this,
            "Put everything edited — titles, cultures and faiths — back to how it was generated?",
            "Revert all", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

        if (answer == DialogResult.OK) _edits.RevertAll();
    }

    private void Inspect(object target) => Inspect([target]);

    private void PlaceInspector(Form inspector)
    {
        var screen = Screen.FromControl(this).WorkingArea;
        int right = Bounds.Right + 8;

        inspector.Location = right + inspector.Width <= screen.Right
            ? new Point(right, Bounds.Top + 80)
            : new Point(Math.Max(screen.Left, Bounds.Right - inspector.Width - 24), Bounds.Top + 80);
    }

    private void OnEditsChanged(Emit.WorldAspect touched)
    {
        ShowPending();

        if (_redrawQueued || !ClickableViews.Values.Any(v => Repaints(v.Kind, touched))) return;

        _staleAspects |= touched;
        _redrawQueued = Post(RedrawTitleViews);
    }

    private bool _redrawQueued;
    private Emit.WorldAspect _staleAspects;

    private void RedrawTitleViews()
    {
        _redrawQueued = false;

        var stale = _staleAspects;
        _staleAspects = Emit.WorldAspect.None;

        bool showing = ClickableViews.TryGetValue(_view, out var current)
                       && Repaints(current.Kind, stale);

        if (showing) _viewer.SetImage(null);

        foreach (var (name, view) in ClickableViews)
        {
            if (!Repaints(view.Kind, stale)) continue;
            if (_rendered.Remove(name, out var dead)) dead.Dispose();
        }

        if (showing) SelectView(_view);
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

    private static Bitmap ToBitmap(PreviewRenderer.Image image) => PreviewRenderer.ToBitmap(image);

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