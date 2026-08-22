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

    private readonly CheckBox _closeOnLaunch = new()
    {
        Text = "Close On Launch",
        AutoSize = true,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        Margin = new Padding(2, 6, 4, 0),
    };

    private readonly Button _gameFolder = Theme.MakeButton("Game folder…", 104);

    private readonly ToolTip _tips = new() { AutoPopDelay = 20000, InitialDelay = 400 };
    private readonly Button _savePreset = Theme.MakeButton("Save preset…", 110);
    private readonly Button _loadPreset = Theme.MakeButton("Load preset…", 110);

    private SettingsView _settingsView = null!;

    private readonly ListBox _sections = new()
    {
        Dock = DockStyle.Left,
        Width = 118,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Surface,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        IntegralHeight = false,
    };

    private readonly TextBox _settingsSearch = new()
    {
        Width = 170,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.SurfaceHigh,
        ForeColor = Theme.Text,
        Margin = new Padding(3, 5, 3, 3),
    };

    private readonly CheckBox _advanced = new()
    {
        Text = "Advanced",
        AutoSize = true,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        Margin = new Padding(10, 6, 0, 0),
    };

    private readonly ComboBox _drape = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 132,
        Font = Theme.Ui,
        FlatStyle = FlatStyle.Flat,
        Enabled = false,
    };

    private bool _drapeRefreshing;

    private readonly Button _recent = Theme.MakeButton("▾", 26);
    private readonly Button _azgaar = Theme.MakeButton("Azgaar…", 74);
    private readonly Button _help = Theme.MakeButton("?", 26);
    private AzgaarGuide? _guide;
    private WelcomeGuide? _welcome;
    private TabControl _tabs = null!;
    private TabPage _sourceTab = null!;
    private TabPage _forgeTab = null!;

    /// <summary>The Heightmap tab: CK3 Heightmap Forge, embedded. See <see cref="Forge.ForgePanel"/>.</summary>
    private readonly Forge.ForgePanel _forge = new() { Dock = DockStyle.Fill };

    private readonly ImageView _viewer = new() { Dock = DockStyle.Fill };

    private readonly FlowLayoutPanel _categoryStrip = new()
    {
        Dock = DockStyle.Top,
        Height = 30,
        Padding = new Padding(4, 3, 4, 0),
        BackColor = Theme.Surface,
    };

    private readonly FlowLayoutPanel _modeStrip = new()
    {
        Dock = DockStyle.Top,
        Height = 30,
        Padding = new Padding(4, 2, 4, 0),
        BackColor = Theme.Surface,
    };

    // Wraps rather than clips: Terrain and Climate carry a dozen-plus classes each, and vertical
    // space is the cheap axis under a 2:1 map. Hidden entirely for modes with no fixed palette.
    private readonly FlowLayoutPanel _legendBar = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        WrapContents = true,
        Padding = new Padding(6, 2, 4, 2),
        BackColor = Theme.Surface,
        Visible = false,
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
        Width = 560,
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

    private readonly Dictionary<string, Button> _viewButtons = [];
    private readonly Dictionary<string, Button> _categoryButtons = [];
    private readonly Dictionary<string, string> _lastInCategory = [];
    private readonly Dictionary<string, Bitmap> _rendered = [];
    private GenerationResult? _result;
    private string _view = "Counties";
    private string _category = "De Jure";

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

    // The shipped heightmap of the newest build, ready for the 3D tab. Built eagerly after every
    // build so opening the tab later is instant, and dropped the moment the source or a
    // normalisation setting changes, because it no longer describes what the next build will ship.
    private Heightfield? _processedSource;
    private Heightfield? _processedPacked;
    private bool _processedPending;

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

    /// <summary>The <see cref="MapGen.HeightmapProvider.Stamp"/> that <see cref="_loaded"/> was made from.</summary>
    private string? _loadedStamp;
    private IReadOnlyList<MapGen.HeightmapWarning> _warnings = [];
    private Emit.WrittenContent? _written;
    /// <summary>Where the heights come from: a PNG, or the Forge pipeline on the Heightmap tab.</summary>
    private MapGen.HeightmapProvider? _source;

    /// <summary>
    /// The last heightmap <em>file</em> chosen, kept apart from <see cref="_source"/> so the file
    /// dialogs still open beside it and the saved state still remembers it while a Forge source is
    /// the one in use.
    /// </summary>
    private string? _lastHeightmapFile;
    private bool _busy;
    private CancellationTokenSource? _cancellation;
    private string _modRoot = "";
    private string _modName = GenerationOptions.DefaultModName;
    private string? _lastModDir;

    public MainForm(GenerationOptions options)
    {
        _options = options;

        _source = options.Heightmap;
        if (_source is null && File.Exists(_state.HeightmapPath))
        {
            var (fit, unverified) = RestoredSizeChoice(_state);
            _source = new MapGen.FileHeightmapProvider(_state.HeightmapPath!, fit, unverified);
        }
        options.Heightmap = _source;
        _lastHeightmapFile = options.HeightmapPath ?? _state.HeightmapPath;

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

        _settingsView = new SettingsView(_options.Config);
        _grid.SelectedObject = _settingsView;

        _sections.Items.Add("All");
        foreach (var section in SettingsView.Sections)
            _sections.Items.Add(SettingsView.DisplayName(section));

        _sections.SelectedIndex =
            _state.SettingsSection is { } saved && _sections.Items.IndexOf(saved) is var found and > 0
                ? found
                : 0;
        ApplySection();

        _sections.SelectedIndexChanged += (_, _) => ApplySection();
        _settingsSearch.TextChanged += (_, _) =>
        {
            _settingsView.Search = _settingsSearch.Text;
            RefreshSettings();
        };

        _advanced.Checked = _options.Config.ShowAdvancedSettings;
        _tips.SetToolTip(_advanced, "Also show the fine-tuning knobs. Saved with presets.");
        _advanced.CheckedChanged += (_, _) =>
        {
            _options.Config.ShowAdvancedSettings = _advanced.Checked;
            RefreshSettings();
        };

        // The 3D view shows the *normalised* heightmap, so the settings that decide normalisation
        // change what it shows. Rebuilding on those and only those: everything else on this grid
        // affects generation, which this view deliberately runs ahead of.
        _grid.PropertyValueChanged += (_, e) =>
        {
            string? changed = e.ChangedItem?.PropertyDescriptor?.Name;
            if (changed is null) return;

            // The toolbar chip mirrors the grid row, whichever of them took the edit.
            if (changed == nameof(MapConfig.AzgaarJsonPath)) ApplyAzgaarChip();

            if (!NormalizationSettings.Contains(changed)) return;
            InvalidateProcessed();
            if (_sourceShown) _ = ShowSourceAsync();
        };

        _options.Config.Seed = Random.Shared.Next(1, int.MaxValue);
        _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
        _seed.ValueChanged += (_, _) => _options.Config.Seed = (int)_seed.Value;

        _launchArgs.Text = _state.LaunchArgs ?? "-debug_mode";
        _tips.SetToolTip(_launchArgs, "Launch arguments passed to ck3.exe (e.g. -debug_mode -mapeditor -novid)");

        _closeOnLaunch.Checked = _state.CloseOnLaunch;
        _tips.SetToolTip(_closeOnLaunch, "Close the map generator when launching Crusader Kings III");

        _browse.Click += (_, _) => PickHeightmap();
        _recent.Click += (_, _) => ShowRecentHeightmaps();
        _tips.SetToolTip(_recent, "Recent heightmaps");
        _azgaar.AutoSize = true;
        _azgaar.Click += (_, _) => ShowAzgaarMenu();
        ApplyAzgaarChip();

        _help.Click += (_, _) => ShowWelcomeGuide();
        _tips.SetToolTip(_help, "How this tool works — the walkthrough from first launch");
        _roll.Click += (_, _) => _seed.Value = Random.Shared.Next(1, int.MaxValue);
        _preview.Click += async (_, _) => await PreviewAsync();
        _writeMod.Click += async (_, _) => await WriteModAsync();
        _cancel.Click += (_, _) => RequestCancel();

        // Hidden until a run is actually going, so the row never shows a button that is a no-op.
        _cancel.Enabled = false;
        _cancel.Visible = false;

        // The chip wears the chosen file's name, so it has to be free to grow.
        _browse.AutoSize = true;
        _tips.SetToolTip(_preview, "Generate and preview without writing anything (F5)");
        _tips.SetToolTip(_writeMod, "Generate and write the mod to disk (Ctrl+S)");
        _tips.SetToolTip(_cancel, "Stop the run in progress. It stops at the next step boundary, so a long step can take a few seconds to let go.");
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

        foreach (var (category, mode) in _state.CategoryViews ?? new Dictionary<string, string>())
            if (MapModes.Find(mode)?.Category == category) _lastInCategory[category] = mode;

        if (_state.View is { } remembered && MapModes.Find(remembered) is { } rememberedMode)
        {
            _view = remembered;
            _category = rememberedMode.Category;
        }

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

    /// <summary>
    /// The toolbar, in two anchored groups that read left to right as a sentence: what the world is
    /// built *from* and the runs that build it on the left; what to do with the built mod — open
    /// it, launch the game into it, point at the install — on the right. One flat row used to hold
    /// all of it in arrival order, with the heightmap's name orphaned at the far end from the
    /// button that chooses it; the name now lives on the button itself.
    /// </summary>
    private Control BuildToolbar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Theme.Surface,
        };

        var build = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 5, 0, 5),
            BackColor = Color.Transparent,
        };

        // First, not tucked in the far corner: it is the "start here" for anyone who has not.
        build.Controls.Add(_help);
        build.Controls.Add(Separator());
        build.Controls.Add(_browse);
        build.Controls.Add(_recent);
        build.Controls.Add(_azgaar);
        build.Controls.Add(Separator());
        build.Controls.Add(Caption("Seed"));
        build.Controls.Add(_seed);
        build.Controls.Add(_roll);
        build.Controls.Add(Separator());
        build.Controls.Add(_preview);
        build.Controls.Add(_writeMod);
        build.Controls.Add(_cancel);

        // Right to left, so the group hugs the window edge; visually it reads
        // "Open mod folder · Launch CK3 [args] | Game folder…".
        var made = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            // Without this the docked panel settles at one button wide and quietly wraps the
            // rest below the 40 px bar, where they render as nothing at all.
            WrapContents = false,
            Padding = new Padding(0, 5, 6, 5),
            BackColor = Color.Transparent,
        };

        made.Controls.Add(_gameFolder);
        made.Controls.Add(Separator());
        made.Controls.Add(_closeOnLaunch);
        made.Controls.Add(_launchArgs);
        made.Controls.Add(_launchGame);
        made.Controls.Add(_openMod);

        bar.Controls.Add(build);
        bar.Controls.Add(made);

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

        var settingsHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(4, 3, 4, 0),
            BackColor = Theme.Surface,
        };
        settingsHeader.Controls.Add(Caption("Search"));
        settingsHeader.Controls.Add(_settingsSearch);
        settingsHeader.Controls.Add(_advanced);

        // Fill first, so docking (which lays out from the last control back) gives the bottom,
        // top and left bars their edges before the grid takes what remains.
        var settings = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        settings.Controls.Add(_grid);
        settings.Controls.Add(_sections);
        settings.Controls.Add(settingsHeader);
        settings.Controls.Add(presets);

        foreach (string category in MapModes.Categories)
        {
            var button = StripButton(category, bold: true);
            button.Click += (_, _) => SelectCategory(category);
            _categoryButtons[category] = button;
            _categoryStrip.Controls.Add(button);
        }

        var exportMap = Theme.MakeButton("Export…", 74);
        exportMap.Margin = new Padding(18, 1, 1, 1);
        _tips.SetToolTip(exportMap, "Save the current view as a PNG (Ctrl+E)");
        exportMap.Click += (_, _) => ExportView();
        _categoryStrip.Controls.Add(exportMap);

        foreach (var mode in MapModes.All)
        {
            var button = StripButton(mode.Clickable ? $"{mode.Name} ✎" : mode.Name, bold: false);
            button.Click += (_, _) => OnModeClicked(mode);
            _viewButtons[mode.Name] = button;
        }

        // The fill is added first and each Top bar after, so the bars stack top-down in reverse
        // order of addition: categories, then modes, then the legend, with the map under them all.
        var viewer = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        viewer.Controls.Add(_viewer);
        viewer.Controls.Add(_legendBar);
        viewer.Controls.Add(_modeStrip);
        viewer.Controls.Add(_categoryStrip);

        var tabs = _tabs = Theme.MakeTabs();

        // Loaded when the tab is first opened, not at startup: decoding a vanilla-sized heightmap
        // is several seconds, and a window that takes that long to appear for a view nobody asked
        // for is a worse trade than a view that takes a moment to fill in. The Heightmap tab is
        // lazy for the same reason — its first preview is a noise pass nobody asked for until
        // they open it.
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab == _forgeTab) _forge.EnsureStarted();

            if (tabs.SelectedTab == _sourceTab && !_sourceShown)
            {
                _sourceShown = true;

                if (_processedSource is not null)
                {
                    SetSourceStage(processed: true);
                    _solid.SetField(_processedSource, _processedPacked, "Nothing to show.");
                }
                else if (!_processedPending)
                {
                    _ = ShowSourceAsync();
                }
                // else: a build just finished and its processed heightmap is still being prepared;
                // that task publishes here itself when it lands, now that the tab is live.
            }
        };

        var mapTab = new TabPage("Map") { BackColor = Theme.Background };
        mapTab.Controls.Add(viewer);

        var titleTab = new TabPage("Titles") { BackColor = Theme.Background };
        titleTab.Controls.Add(_titles);

        var sourceTab = _sourceTab = new TabPage("3D render") { BackColor = Theme.Background };
        sourceTab.Controls.Add(BuildSourceView());

        var forgeTab = _forgeTab = new TabPage("Heightmap (WIP)") { BackColor = Theme.Background };
        forgeTab.Controls.Add(_forge);
        _forge.UseForGeneration += UseForgeForGeneration;
        _forge.PresetDir = _state.ForgePresetDir;

        tabs.TabPages.Add(mapTab);
        tabs.TabPages.Add(forgeTab);
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
    /// The 3D view: the heightmap as loaded before anything is generated, then the processed
    /// heightmap once a build has produced one.
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

        _drape.Items.Add("Terrain shading");
        _drape.SelectedIndex = 0;
        _drape.SelectedIndexChanged += (_, _) => { if (!_drapeRefreshing) UpdateDrape(); };
        _tips.SetToolTip(_drape,
            "What the terrain wears: the built-in height tints, or any generated map mode " +
            "draped over the relief. Fills in after a preview.");

        var export3d = Theme.MakeButton("Export…", 74);
        _tips.SetToolTip(export3d, "Save the current view as a PNG (Ctrl+E)");
        export3d.Click += (_, _) => ExportView();

        strip.Controls.Add(_sourceMode);
        strip.Controls.Add(new Label
        {
            Text = "Surface",
            AutoSize = false,
            Width = 50,
            Height = 24,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui,
        });
        strip.Controls.Add(_drape);
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
        strip.Controls.Add(export3d);
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
        if (_source is null)
        {
            _solid.SetField(null, null, "Choose a heightmap to see it in 3D.");
            return;
        }

        var source = _source;
        var cfg = _options.Config;

        // Settings can be changed faster than a full-size heightmap can be normalised and packed,
        // and the tasks do not finish in the order they started. Only the newest one is allowed to
        // publish, or a stale frame silently wins and the view stops matching the settings. A
        // Forge source can also be genuinely slow — it runs the whole pipeline at full size — so
        // the superseded task is cancelled rather than just ignored.
        int generation = ++_sourceGeneration;
        _sourceCts?.Cancel();
        _sourceCts?.Dispose();
        var cts = _sourceCts = new CancellationTokenSource();

        _solid.SetField(null, null, source is MapGen.ForgeHeightmapProvider
            ? "Running the Forge pipeline at full resolution…"
            : "Reading the heightmap…");

        try
        {
            var (field3d, packed, warnings, loaded, stamp) = await Task.Run(() =>
            {
                string stamp = source.Stamp;
                var image = _loaded is not null && _loadedStamp == stamp
                    ? _loaded
                    : source.Produce(cfg, cts.Token, MapGen.ConsoleProgress.Instance);

                MapGen.HeightmapSource.Apply(image, cfg);
                var found = MapGen.HeightmapSource.Diagnose(image, cfg);

                // Normalised, because that is what the game is handed. A heightmap drawn on
                // somebody else's height scale looks perfectly reasonable as a PNG and ships as a
                // plateau with a wall at every shoreline, and this view exists to show that.
                var levels = image.Levels(cfg);

                var field = Heightfield.Downsample(levels, image.Width, image.Height, Heightfield.PreviewCols);
                var asRendered = Heightfield.Downsample(
                    Emit.HeightmapPacker.Reconstruct(levels, image.Width, image.Height),
                    image.Width, image.Height, Heightfield.PreviewCols);

                return (field, asRendered, found, image, stamp);
            }, cts.Token);

            if (generation != _sourceGeneration) return;

            _loaded = loaded;
            _loadedStamp = stamp;
            _warnings = warnings;

            SetSourceStage(processed: false);
            _solid.SetField(field3d, packed, "Nothing to show.");
            _grid.Refresh();

            _status.Text = $"{source.Label} — {loaded.Width}×{loaded.Height}, " +
                           $"{100 * field3d.LandShare:F1}% land" +
                           (warnings.Count == 0 ? "" : $", {warnings.Count} warning(s)");
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request, which is showing its own message.
        }
        catch (Exception error)
        {
            if (generation != _sourceGeneration) return;
            _solid.SetField(null, null, $"Could not read it: {error.Message}");
        }
    }

    private CancellationTokenSource? _sourceCts;

    /// <summary>
    /// Prepares the heightmap a build actually produced — coastline forced to the provinces,
    /// shoreline shaped, exactly what lands in heightmap.png — and swaps the 3D tab over to it.
    /// The raw source comes back through <see cref="ShowSourceAsync"/> the moment the source file
    /// or a normalisation setting changes, so the tab always shows the newest thing the pipeline
    /// has made of the map.
    /// </summary>
    private async Task ShowProcessedAsync(GenerationResult result)
    {
        int generation = ++_sourceGeneration;
        _processedPending = true;
        _processedSource = _processedPacked = null;

        try
        {
            var (source, packed) = await Task.Run(() =>
            {
                var cfg = result.Config;
                var full = Emit.MapDataWriter.ShippedHeightmap(
                    cfg, result.Provinces, result.ProvinceOrder, result.LandCount, result.Terra);

                var field = Heightfield.Downsample(
                    full, cfg.Width, cfg.Height, Heightfield.PreviewCols);
                var asRendered = Heightfield.Downsample(
                    Emit.HeightmapPacker.Reconstruct(full, cfg.Width, cfg.Height),
                    cfg.Width, cfg.Height, Heightfield.PreviewCols);

                return (field, asRendered);
            });

            if (generation != _sourceGeneration) return;

            _processedPending = false;
            _processedSource = source;
            _processedPacked = packed;

            if (_sourceShown)
            {
                SetSourceStage(processed: true);
                _solid.SetField(source, packed, "Nothing to show.");
            }
        }
        catch (Exception error)
        {
            if (generation != _sourceGeneration) return;
            _processedPending = false;

            // Not worth wiping a good frame over; the tab just keeps whatever it was showing.
            Console.WriteLine($"Could not prepare the processed heightmap for the 3D view: {error.Message}");
        }
    }

    /// <summary>Drops a processed heightmap that no longer describes what the next build ships.</summary>
    private void InvalidateProcessed()
    {
        _processedSource = _processedPacked = null;
        _processedPending = false;
        _sourceGeneration++;   // orphans any in-flight processed build
    }

    /// <summary>
    /// Renames the first view mode so the strip says what the 3D tab is looking at. The second mode
    /// — the packer round-trip — applies to either stage, so it keeps its name.
    /// </summary>
    private void SetSourceStage(bool processed)
    {
        string label = processed ? "Processed heightmap" : "Heightmap as loaded";
        if (!label.Equals(_sourceMode.Items[0])) _sourceMode.Items[0] = label;
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

    private static Button StripButton(string text, bool bold)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 24,
            Padding = new Padding(6, 0, 6, 0),
            FlatStyle = FlatStyle.Flat,
            Font = bold ? Theme.UiBold : Theme.Ui,
            BackColor = Theme.SurfaceHigh,
            ForeColor = Theme.Text,
            UseVisualStyleBackColor = false,
            Margin = new Padding(1, 1, 1, 1),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.Border;
        return button;
    }

    private bool Available(MapMode mode) => !mode.AfterWrite || _written is not null;

    private void OnModeClicked(MapMode mode)
    {
        if (!Available(mode))
        {
            _status.Text = $"{mode.Name} shows written content — write the mod first";
            return;
        }

        SelectView(mode.Name);
    }

    private void SelectCategory(string category)
    {
        var modes = MapModes.All.Where(m => m.Category == category).ToList();

        string pick = _lastInCategory.TryGetValue(category, out var last)
                      && MapModes.Find(last) is { } remembered && Available(remembered)
            ? last
            : (modes.FirstOrDefault(Available) ?? modes[0]).Name;

        SelectView(pick);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.ApplyLightTitleBar(this);
        RestorePlacement();

        Place(_body, 300, 400, _state.SettingsWidth);
        Place(_right, 200, 80, _state.ViewerHeight);
        if (_state.ForgeLeftWidth > 0) _forge.LeftWidth = _state.ForgeLeftWidth;

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
        _state.ForgeLeftWidth = _forge.LeftWidth;
        _state.ForgePresetDir = _forge.PresetDir;
        _state.HeightmapPath = _lastHeightmapFile;
        _state.View = _view;
        _state.CategoryViews = new Dictionary<string, string>(_lastInCategory);
        _state.SettingsSection = _sections.SelectedIndex > 0
            ? _sections.Items[_sections.SelectedIndex] as string
            : null;

        _state.GameDir = Core.GameLocator.IsGameDir(_options.GameDir) ? _options.GameDir : null;
        _state.ModRoot = _modRoot;
        _state.ModName = _modName;
        _state.LastModDir = _lastModDir;
        _state.LaunchArgs = _launchArgs.Text;
        _state.CloseOnLaunch = _closeOnLaunch.Checked;
        _state.Save();

        Stage.Entering -= OnStageEntered;
        Stage.Detailing -= OnStageDetail;
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys key)
    {
        // The Heightmap tab owns the brush keys while it is the one on screen, and says so by
        // handling them; anything it passes on falls through to the window's own shortcuts.
        if (_tabs.SelectedTab == _forgeTab && !TypingInText() && _forge.HandleKey(key)) return true;

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

            case Keys.Escape when _view == "Realms" && _realmFocus.Count > 0:
                _realmFocus.RemoveAt(_realmFocus.Count - 1);
                SelectView("Realms");
                return true;

            case Keys.Oem4 when !TypingInText():   // [
                CycleMode(-1);
                return true;

            case Keys.Oem6 when !TypingInText():   // ]
                CycleMode(+1);
                return true;

            case Keys.Control | Keys.Oem4:
                CycleCategory(-1);
                return true;

            case Keys.Control | Keys.Oem6:
                CycleCategory(+1);
                return true;

            case Keys.Control | Keys.E when !_busy:
                ExportView();
                return true;
        }

        return base.ProcessCmdKey(ref message, key);
    }

    private void CycleMode(int step)
    {
        var modes = MapModes.All.Where(m => m.Category == _category && Available(m)).ToList();
        if (modes.Count == 0) return;

        int at = modes.FindIndex(m => m.Name == _view);
        SelectView(modes[(Math.Max(0, at) + step + modes.Count) % modes.Count].Name);
    }

    private void CycleCategory(int step)
    {
        int at = Math.Max(0, Array.IndexOf(MapModes.Categories, _category));
        int next = (at + step + MapModes.Categories.Length) % MapModes.Categories.Length;
        SelectCategory(MapModes.Categories[next]);
    }

    /// <summary>The bracket keys cycle map modes — except while the user is typing somewhere.</summary>
    private bool TypingInText()
    {
        Control? active = ActiveControl;
        while (active is ContainerControl container && container.ActiveControl is not null)
            active = container.ActiveControl;

        return active is TextBoxBase or NumericUpDown or ComboBox or PropertyGrid;
    }

    private void PickHeightmap()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Build the mod around a heightmap",
            Filter = "Heightmap PNG (*.png)|*.png|All files (*.*)|*.*",
            InitialDirectory = LastHeightmapDir(),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        SetHeightmap(dialog.FileName);
    }

    /// <summary>
    /// How to load the saved heightmap: resampled to the fit agreed against it or to a fresh one,
    /// or built at its own size if that was the answer given.
    ///
    /// Re-measured rather than trusted, in both directions. A saved fit is only ever the right
    /// answer for the file it was chosen against and for the rule in force when it was chosen —
    /// a heightmap since redrawn at a size that ships on its own would otherwise keep being
    /// resampled, moving its coastline for nothing.
    ///
    /// And a fit that no longer satisfies <see cref="MapGen.TileFit"/> is replaced rather than
    /// dropped. <see cref="MapGen.TileFit.Known"/> has been narrowed more than once as sizes were
    /// found to clip, which strands every fit agreed under an older rule. Dropping those left the
    /// provider with no fit at all, so the size check inside
    /// <see cref="MapGen.HeightmapSource.Read"/> failed on the build thread and surfaced as an
    /// unhandled exception instead of as anything anyone could act on.
    ///
    /// "Build at its own size anyway" is the third answer, and it outranks a saved fit: it was
    /// given knowingly, and a test build that silently came back resampled would be a test of the
    /// wrong thing.
    /// </summary>
    private static ((int Width, int Height)? Fit, bool AllowUnverified) RestoredSizeChoice(GuiState state)
    {
        if (state.HeightmapPath is not { } path) return (null, false);

        int fileWidth, fileHeight;
        try
        {
            (fileWidth, fileHeight) = MapGen.TileFit.Measure(path);
        }
        catch
        {
            // A file that cannot even be identified is the decode's problem to report.
            return (null, false);
        }

        if (MapGen.TileFit.Fits(fileWidth, fileHeight)) return (null, false);
        if (state.HeightmapAllowUnverifiedSize) return (null, true);

        return state.HeightmapFitWidth is { } width && state.HeightmapFitHeight is { } height
            && MapGen.TileFit.Fits(width, height)
                ? ((width, height), false)
                : (MapGen.TileFit.Nearest(fileWidth, fileHeight), false);
    }

    /// <summary>
    /// Where a heightmap or Azgaar file dialog should open: beside the heightmap in use, else
    /// beside the most recent one that still exists, else let the shell decide.
    ///
    /// Reconstructed after being deleted by accident, so it is worth saying what it is for rather
    /// than what it was: an empty <c>InitialDirectory</c> is not an error, it just drops the user
    /// wherever the shell last was, which for a file picked once per project is rarely useful.
    /// </summary>
    private string LastHeightmapDir()
    {
        foreach (string? candidate in new[] { _options.HeightmapPath, _state.HeightmapPath })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            string? dir = Path.GetDirectoryName(candidate);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) return dir;
        }

        foreach (string recent in _state.RecentHeightmaps ?? [])
        {
            string? dir = Path.GetDirectoryName(recent);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) return dir;
        }

        return "";
    }

    private void SetHeightmap(string path)
    {
        // Asked before the file is adopted, because declining leaves nothing usable: a size the
        // packer cannot tile fails the decode, so the 3D view and every build would error too.
        var (load, fit, unverified) = OfferTileFit(path);
        if (!load) return;

        _lastHeightmapFile = path;

        var recent = _state.RecentHeightmaps ?? [];
        recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, path);
        if (recent.Count > 8) recent.RemoveRange(8, recent.Count - 8);
        _state.RecentHeightmaps = recent;

        _state.HeightmapFitWidth = fit?.Width;
        _state.HeightmapFitHeight = fit?.Height;
        _state.HeightmapAllowUnverifiedSize = unverified;

        SetSource(new MapGen.FileHeightmapProvider(path, fit, unverified));
    }

    /// <summary>
    /// The one size rule a heightmap file can break silently, offered as a fix at the moment it
    /// becomes relevant rather than left to fail at the end of a build.
    ///
    /// Same shape as <see cref="OfferStretch"/> and for the same reason, with one difference:
    /// this one is not optional. <see cref="MapGen.TileFit"/> explains what a size it refuses does
    /// in game — a clipped map edge and province borders drifting off the terrain, neither logged
    /// — so declining means not loading the file at all rather than loading it and hoping.
    ///
    /// The third answer, building at the file's own size, is how <see cref="MapGen.TileFit.Known"/>
    /// grows: whether a size renders can only be learned by building it and looking.
    /// </summary>
    /// <returns>
    /// Whether to load the file at all, the size to resample it to, and whether to build it at its
    /// own size regardless. Three values rather than a nullable size, because "no fit needed", "the
    /// offer was declined" and "build it anyway" are different answers that a null size alone
    /// cannot tell apart.
    /// </returns>
    private (bool Load, (int Width, int Height)? Fit, bool AllowUnverified) OfferTileFit(string path)
    {
        int width, height;
        try
        {
            (width, height) = MapGen.TileFit.Measure(path);
        }
        catch (Exception error)
        {
            // Not this method's problem: load it and let the decode report it properly.
            Console.WriteLine($"Could not read the size of {path}: {error.Message}");
            return (true, null, false);
        }

        if (MapGen.TileFit.Fits(width, height)) return (true, null, false);

        var target = MapGen.TileFit.Nearest(width, height);
        string name = Path.GetFileName(path);

        // A TaskDialog rather than a MessageBox, because the third answer needs a button that says
        // what it does. MessageBox can only offer Yes/No/Cancel, and "No means build it anyway" is
        // the kind of mapping that gets the wrong button pressed.
        var resample = new TaskDialogButton($"Resample to {target.Width} x {target.Height}");
        var anyway = new TaskDialogButton("Build at this size anyway");
        var cancel = TaskDialogButton.Cancel;

        var page = new TaskDialogPage
        {
            Caption = "This heightmap is not a size CK3 renders",
            Heading = $"{name} is {width} x {height}",
            Text = "That is not one of the sizes CK3 is known to render correctly "
                 + $"({MapGen.TileFit.KnownList}). At other sizes the engine leaves terrain undrawn "
                 + "along the north and east edges, in the map editor as much as in game, and "
                 + "whatever the heightmap is packed with. Nothing is logged when it happens.\n\n"
                 + $"Resampling lands it on {target.Width} x {target.Height}; the file on disk is "
                 + "not touched. Building anyway is how to find out whether a new size renders: "
                 + "look at the north and east edges in game, and if it is clean the size can join "
                 + "the known list.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { resample, anyway, cancel },
            DefaultButton = resample,
        };

        var answer = TaskDialog.ShowDialog(this, page);

        if (answer == anyway)
        {
            _status.Text = $"{name}: building at {width} x {height}, a size CK3 is not known to "
                         + "render, to test whether it does";
            return (true, null, true);
        }

        if (answer != resample)
        {
            _status.Text = $"{name} not loaded: {width} x {height} is not "
                         + $"one of {MapGen.TileFit.KnownList}. Resize it to "
                         + $"{target.Width} x {target.Height} and choose it again.";
            return (false, null, false);
        }

        _status.Text = $"{name}: resampling {width} x {height} to "
                     + $"{target.Width} x {target.Height}, a size CK3 renders correctly.";
        return (true, target, false);
    }

    /// <summary>
    /// The one route a heightmap source comes in by — a file from <see cref="SetHeightmap"/> or
    /// the Forge pipeline from the Heightmap tab. Everything that depends on "built from what?"
    /// is refreshed here and nowhere else.
    /// </summary>
    private void SetSource(MapGen.HeightmapProvider source)
    {
        _source = source;
        _options.Heightmap = source;

        ApplySource();
        InvalidateProcessed();
        if (_sourceShown) _ = ShowSourceAsync();
    }

    /// <summary>
    /// The Heightmap tab's pipeline becomes the source. By reference: every later edit on that tab
    /// changes the provider's stamp, so the next Preview or Write runs the pipeline again at full
    /// size, and an unchanged one is served from the same decode cache a file is.
    /// </summary>
    /// <param name="allowUnverifiedSize">The panel already asked; true means the user chose to
    /// build at an export size CK3 is not known to render, to test it.</param>
    private void UseForgeForGeneration(bool allowUnverifiedSize)
    {
        _forge.EnsureStarted();
        SetSource(_forge.Session.ProviderForGeneration(allowUnverifiedSize));
        _status.Text = $"Building from the Heightmap tab's pipeline ({_forge.Session.Name}) — " +
                       "press Preview to generate from it" +
                       (allowUnverifiedSize ? " (at a size CK3 is not known to render, to test it)" : "");
    }

    private void ShowAzgaarMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);

        var choose = new ToolStripMenuItem("Choose Full export (.json)…");
        choose.Click += (_, _) => PickAzgaar();
        menu.Items.Add(choose);

        if (!string.IsNullOrWhiteSpace(_options.Config.AzgaarJsonPath))
        {
            var clear = new ToolStripMenuItem("Stop using the export");
            clear.Click += (_, _) => SetAzgaar("");
            menu.Items.Add(clear);
        }

        menu.Items.Add(new ToolStripSeparator());

        var guide = new ToolStripMenuItem("How to export from Azgaar…");
        guide.Click += (_, _) => ShowAzgaarGuide();
        menu.Items.Add(guide);

        menu.Show(_azgaar, new Point(0, _azgaar.Height));
    }

    private void PickAzgaar()
    {
        string current = _options.Config.AzgaarJsonPath;

        using var dialog = new OpenFileDialog
        {
            Title = "Choose an Azgaar 'Full' JSON export",
            Filter = "Azgaar export (*.json)|*.json|All files (*.*)|*.*",
            // Exports usually land beside the heightmap PNG from the same map.
            InitialDirectory = !string.IsNullOrWhiteSpace(current)
                ? Path.GetDirectoryName(current) ?? ""
                : LastHeightmapDir(),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        SetAzgaar(dialog.FileName);
    }

    /// <summary>The one route the export comes in by — the menu, the guide and the grid row agree.</summary>
    private void SetAzgaar(string path)
    {
        _options.Config.AzgaarJsonPath = path;
        ApplyAzgaarChip();
        RefreshSettings();   // the rows the export decides grey out, or come back

        if (path.Length == 0)
        {
            _status.Text = "Azgaar export cleared — every name and state is generated again";
            return;
        }

        _status.Text = $"Azgaar export: {Path.GetFileName(path)} — pair it with the heightmap "
                       + "PNG exported from the same view of the same map";
        OfferStretch();
    }

    /// <summary>
    /// The one settings change every Azgaar import needs, offered at the moment it becomes
    /// needed instead of left as folklore. Offered, not applied: normalisation belongs to the
    /// user, and there is more dialling-in to do on the code side before it can be silent.
    /// </summary>
    private void OfferStretch()
    {
        if (_options.Config.Normalization == HeightmapNormalization.Stretch) return;

        var answer = MessageBox.Show(this,
            "Azgaar heightmaps sit compressed against CK3's height scale, and Stretch "
            + "normalization is what makes the relief land right in game.\n\n"
            + "Set Normalization to Stretch now? It lives under Height scale if you change "
            + "your mind.",
            "Azgaar export chosen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes) return;

        _options.Config.Normalization = HeightmapNormalization.Stretch;
        RefreshSettings();
        InvalidateProcessed();
        if (_sourceShown) _ = ShowSourceAsync();
    }

    private void ApplyAzgaarChip()
    {
        string path = _options.Config.AzgaarJsonPath;
        bool loaded = !string.IsNullOrWhiteSpace(path);

        _azgaar.Text = loaded ? $"Azgaar: {Clipped(Path.GetFileName(path), 22)}" : "Azgaar…";
        _tips.SetToolTip(_azgaar, loaded
            ? path
            : "Borrow names, states and cultures from an Azgaar 'Full' JSON export — optional. "
              + "The menu has the full walkthrough.");
    }

    private void ShowAzgaarGuide()
    {
        if (_guide is null || _guide.IsDisposed)
        {
            _guide = new AzgaarGuide();
            _guide.ChooseExport += PickAzgaar;
        }

        // Show(owner) throws on a form that is already visible, so re-opening only fronts it.
        if (!_guide.Visible) _guide.Show(this);
        _guide.BringToFront();
    }

    private void ShowWelcomeGuide()
    {
        if (_welcome is null || _welcome.IsDisposed)
        {
            _welcome = new WelcomeGuide();
            _welcome.ChooseHeightmap += PickHeightmap;
            _welcome.OpenAzgaarGuide += ShowAzgaarGuide;
        }

        if (!_welcome.Visible) _welcome.Show(this);
        _welcome.BringToFront();
    }

    /// <summary>
    /// The walkthrough's one uninvited appearance, on the very first launch. From then on it
    /// waits behind the ? in the toolbar. The flag is set before the window opens, so closing
    /// the app mid-read does not earn a second ambush.
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_state.WelcomeShown) return;
        _state.WelcomeShown = true;
        ShowWelcomeGuide();
    }

    private void ShowRecentHeightmaps()
    {
        var recent = (_state.RecentHeightmaps ?? [])
            .Where(p => File.Exists(p) && !string.Equals(p, _options.HeightmapPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var menu = new ContextMenuStrip();
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);

        if (recent.Count == 0)
            menu.Items.Add(new ToolStripMenuItem("(no other recent heightmaps)") { Enabled = false });

        foreach (string path in recent)
        {
            var item = new ToolStripMenuItem(
                $"{Path.GetFileName(path)}   ({Clipped(Path.GetDirectoryName(path) ?? "", 44)})");
            item.Click += (_, _) => SetHeightmap(path);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var forge = new ToolStripMenuItem("Use the Heightmap tab's pipeline")
        {
            Enabled = _source is not MapGen.ForgeHeightmapProvider,
        };
        forge.Click += (_, _) =>
        {
            // Through the panel, so an export size CK3 is not known to render gets the same
            // question here as from the panel's own button, rather than none.
            _forge.EnsureStarted();
            _forge.RequestUseForGeneration();
            _tabs.SelectedTab = _forgeTab;
        };
        menu.Items.Add(forge);

        menu.Show(_recent, new Point(0, _recent.Height));
    }

    /// <summary>Saves whatever the active tab is showing — a map mode or the 3D frame — as a PNG.</summary>
    private void ExportView()
    {
        Bitmap? frame;
        string name;

        if (_tabs.SelectedTab == _sourceTab)
        {
            frame = _solid.CurrentFrame;
            name = "terrain-3d";
        }
        else
        {
            frame = _focusFrame ?? _rendered.GetValueOrDefault(_view);
            name = _view.ToLowerInvariant();
        }

        if (frame is null)
        {
            _status.Text = "Nothing to export yet — open the view first";
            return;
        }

        // Cloned before the dialog opens: the frame belongs to a view that may re-render and
        // dispose it while the dialog holds the message loop.
        using var copy = new Bitmap(frame);

        using var dialog = new SaveFileDialog
        {
            Title = "Export the current view",
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{name}.png",
            InitialDirectory = _state.ExportDir ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        copy.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
        _state.ExportDir = Path.GetDirectoryName(dialog.FileName);
        _status.Text = $"Exported {Path.GetFileName(dialog.FileName)} ({copy.Width}×{copy.Height})";
    }

    /// <summary>
    /// Rebuilds the 3D drape choices for the current result: the built-in tints plus every map
    /// mode that can render right now. Post-write modes appear once written content exists.
    /// </summary>
    private void RefreshDrapeChoices()
    {
        string? keep = _drape.SelectedIndex > 0 ? _drape.SelectedItem as string : null;

        _drapeRefreshing = true;
        _drape.Items.Clear();
        _drape.Items.Add("Terrain shading");

        if (_result is not null)
            foreach (var mode in MapModes.All)
                if (Available(mode)) _drape.Items.Add(mode.Name);

        _drape.SelectedIndex = Math.Max(0, keep is null ? 0 : _drape.Items.IndexOf(keep));
        _drape.Enabled = _result is not null;
        _drapeRefreshing = false;

        // Explicit rather than via the event: a kept selection keeps its index, and the drape
        // still has to be re-rendered against the new result.
        UpdateDrape();
    }

    private void UpdateDrape()
    {
        if (_result is null || _drape.SelectedIndex <= 0
            || _drape.SelectedItem is not string name
            || MapModes.Find(name) is not { } mode || !Available(mode))
        {
            _solid.SetDrape(null);
            return;
        }

        using (new WaitCursorFor(this)) _solid.SetDrape(mode.Render(_result, _written));
    }

    private void ApplySource()
    {
        // The button is the label: it always answers "built from what?" without a trip elsewhere.
        _browse.Text = _source is null
            ? "Choose heightmap…"
            : Clipped(_source.Label, 30);

        _tips.SetToolTip(_browse, _source?.Detail ?? "The heightmap the whole mod is built from: a 16-bit PNG, or the Heightmap tab's pipeline.");

        _openMod.Enabled = ModFolderToOpen() is not null;
        SetEnabled(!_busy);

        ShowGameFolder();
    }

    private void ApplySection()
    {
        int index = _sections.SelectedIndex;
        _settingsView.Section = index <= 0 ? null : SettingsView.Sections[index - 1];
        RefreshSettings();
    }

    /// <summary>
    /// Makes the grid re-ask the view for its rows. Reassignment rather than
    /// <see cref="PropertyGrid.Refresh"/>, which repaints the values of the rows it already has.
    /// </summary>
    private void RefreshSettings() => _grid.SelectedObject = _settingsView;

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

            // The preset may have flipped the advanced flag; the checkbox follows the config, and
            // its CheckedChanged (when it fires) or this call (when it does not) rebuilds the rows.
            _advanced.Checked = _options.Config.ShowAdvancedSettings;
            ApplyAzgaarChip();
            RefreshSettings();

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
        if (_closeOnLaunch.Checked && _edits.HasPending)
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

            if (_closeOnLaunch.Checked)
            {
                Close();
            }
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
        if (_busy || _source is null) return;

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

            _rendered.TryGetValue(viewName, out var old);
            _rendered[viewName] = bitmap;

            // Instantly update on-screen if this is the active tab
            if (_view == viewName)
            {
                _viewer.SetImage(bitmap);
                ShowReadout(_viewer.Zoom, null);
            }

            // Disposed last, after the viewer is already holding the replacement. Disposing before
            // SetImage frees a bitmap the control may still be mid-paint on, which is the stale-image
            // crash ImageView now catches — this is the other half of that fix, and the half that
            // stops it happening rather than surviving it.
            old?.Dispose();
        });
    }

    private async Task BuildAsync(string? modDir)
    {
        // A Forge source with an unbaked erosion stage will bake it inside the run, which can be
        // the longest phase of the lot. Say so first, as the tab's own export does.
        if (_source is MapGen.ForgeHeightmapProvider && !_forge.ConfirmStaleBakes(this)) return;

        var (result, cancelled) = await RunAsync(
            modDir is null ? "Building preview…" : "Writing mod…",
            () =>
            {
                var cfg = _options.Config;
                var source = _source!;

                Stage.Time(source.PhaseName, () =>
                {
                    string stamp = source.Stamp;
                    if (_loaded is null || _loadedStamp != stamp)
                    {
                        _loaded = source.Produce(cfg, Stage.Cancellation, MapGen.ConsoleProgress.Instance);
                        _loadedStamp = stamp;
                    }
                    else
                    {
                        MapGen.HeightmapSource.Apply(_loaded, cfg);
                    }

                    _warnings = MapGen.HeightmapSource.Diagnose(_loaded, cfg);
                });

                var terra = Stage.Time("province elevation",
                    () => MapGen.TerrainData.FromElevation(_loaded!.ToElevation(cfg), cfg));

                var r = Generator.FromTerrain(terra, cfg, OnProgressivePreview);

                _written = null;
                if (modDir is not null) _written = Generator.WriteMod(r, _options, modDir);
                return r;
            },
            writing: modDir is not null,
            modDir: modDir);

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

        // The 3D tab tracks the pipeline: the raw heightmap before a build, the shipped one after.
        _ = ShowProcessedAsync(result);

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
        string message, Func<GenerationResult> work, bool writing, string? modDir = null)
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
        RunLog.Begin();
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
            if (modDir is not null) RunLog.Write(modDir, _options, "completed");
            return (result, false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine($"Cancelled after {clock.ElapsedMilliseconds / 1000.0:F1} s");
            if (modDir is not null) RunLog.Write(modDir, _options, "cancelled — the mod folder may be half written");
            return (null, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(ex);
            _status.Text = "Failed — see log";
            if (modDir is not null) RunLog.Write(modDir, _options, $"failed: {ex.Message}");
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
        _status.Text = "Cancelling — stopping at the end of this step…";
    }

    private void SetEnabled(bool enabled)
    {
        _grid.Enabled = enabled;
        _sections.Enabled = enabled;
        _settingsSearch.Enabled = enabled;
        _advanced.Enabled = enabled;
        _seed.Enabled = enabled;
        _roll.Enabled = enabled;
        _browse.Enabled = enabled;
        _recent.Enabled = enabled;
        _azgaar.Enabled = enabled;
        _drape.Enabled = enabled && _result is not null;
        _savePreset.Enabled = enabled;
        _loadPreset.Enabled = enabled;
        _gameFolder.Enabled = enabled;
        _launchGame.Enabled = enabled;
        _closeOnLaunch.Enabled = enabled;
        _launchArgs.Enabled = enabled;
        _cancel.Enabled = !enabled;
        _cancel.Visible = !enabled;

        _titles.Enabled = enabled;
        _forge.Enabled = enabled;
        ShowPending();

        bool ready = enabled && _source is not null;
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
        _probeBaronies = null;

        _realmGraph = null;
        _realmGraphBuilt = false;
        _realmFocus.Clear();

        _edits.Detach();

        _viewer.SetImage(null);
        foreach (var bitmap in _rendered.Values) bitmap.Dispose();
        _rendered.Clear();

        _focusFrame?.Dispose();
        _focusFrame = null;

        SelectView(_view);
        RefreshDrapeChoices();
    }

    // --- Realm navigation -----------------------------------------------------------------------

    private RealmGraph? _realmGraph;
    private bool _realmGraphBuilt;

    /// <summary>
    /// The seats the user has drilled into on the Realms view, top realm first. Empty means the
    /// unfocused world view. Cleared with every new result — the seats are Title references into
    /// the written world, and a rebuild replaces that world wholesale.
    /// </summary>
    private readonly List<MapGen.Title> _realmFocus = [];

    /// <summary>The last focused-realm frame, owned here because it bypasses the render cache.</summary>
    private Bitmap? _focusFrame;

    private RealmGraph? Realm
    {
        get
        {
            if (!_realmGraphBuilt && _result is not null)
            {
                _realmGraph = RealmGraph.Build(_written, _result);
                _realmGraphBuilt = true;
            }

            return _realmGraph;
        }
    }

    private void SelectView(string name)
    {
        var mode = MapModes.Find(name) ?? MapModes.All[0];

        // A remembered or restored mode can point at written content that does not exist yet;
        // land on the nearest thing in its category that does.
        if (!Available(mode))
        {
            mode = MapModes.All.FirstOrDefault(m => m.Category == mode.Category && Available(m))
                   ?? MapModes.All[0];
        }

        bool switched = _view != mode.Name;

        _view = mode.Name;
        _category = mode.Category;
        _lastInCategory[_category] = _view;

        RestyleStrip();
        ShowLegend(mode);

        _viewer.ViewName = mode.Name;
        _viewer.Cursor = mode.Clickable && _result is not null ? Cursors.Hand : Cursors.Default;

        // Only on an actual switch: this also runs to repaint after an edit, and the hint would
        // otherwise stamp over the "Culture Suebi — Lugia" confirmation the edit just wrote.
        if (switched && !_busy && _result is not null && mode.Pick is { } pick)
        {
            _status.Text = pick.Kind switch
            {
                MapPick.Culture => "Click a county to inspect and edit its culture",
                MapPick.Faith => "Click a county to inspect and edit its faith",
                MapPick.Realm => "Click a realm to focus it · Ctrl+click jumps to a county · Esc steps back",
                _ => $"Click a {TierWord(pick.Tier)} to inspect and edit it",
            };
        }

        // Disposed at the end, after the viewer holds its replacement — never before SetImage,
        // for the same stale-paint crash the progressive preview's dispose-last comment explains.
        var oldFocus = _focusFrame;
        _focusFrame = null;

        if (_result is null && !_rendered.ContainsKey(mode.Name))
        {
            _viewer.SetImage(null);
            oldFocus?.Dispose();
            return;
        }

        Bitmap? bitmap;
        if (mode.Name == "Realms" && _realmFocus.Count > 0 && Realm is { } graph && _result is not null)
        {
            // Focused frames bypass the render cache: they are ~100 ms to draw and keyed by a
            // whole focus stack, and a cache the edit-invalidation would have to understand is a
            // worse deal than just drawing.
            using (new WaitCursorFor(this))
                bitmap = ToBitmap(PreviewRenderer.RenderRealmsFocused(
                    _result, graph, _written?.Wilderness, _realmFocus[^1]));
            _focusFrame = bitmap;
        }
        else if (!_rendered.TryGetValue(mode.Name, out bitmap) && _result is not null)
        {
            using (new WaitCursorFor(this)) bitmap = ToBitmap(mode.Render(_result, _written));
            _rendered[mode.Name] = bitmap;
        }

        _viewer.SetImage(bitmap);
        oldFocus?.Dispose();
        ShowReadout(_viewer.Zoom, null);
    }

    /// <summary>Middle-ellipsis, so both the start of a long file name and its extension survive.</summary>
    private static string Clipped(string text, int max)
        => text.Length <= max ? text : $"{text[..(max / 2 - 1)]}…{text[^(max / 2 - 1)..]}";

    private static string TierWord(string tier) => tier switch
    {
        "e" => "empire",
        "k" => "kingdom",
        "d" => "duchy",
        _ => "county",
    };

    /// <summary>
    /// Restyles both strip rows for the current selection, and reparents the mode row to the
    /// active category. Runs whole rather than incrementally because availability can change out
    /// from under any button — a preview build clears <see cref="_written"/> and every post-write
    /// mode dims at once.
    /// </summary>
    private void RestyleStrip()
    {
        foreach (var (key, button) in _categoryButtons)
        {
            bool on = key == _category;
            button.BackColor = on ? Theme.Accent : Theme.SurfaceHigh;
            button.ForeColor = on ? Theme.AccentText : Theme.Text;
            button.FlatAppearance.MouseOverBackColor = on ? Theme.Accent : Theme.Border;
        }

        _modeStrip.SuspendLayout();
        _modeStrip.Controls.Clear();

        foreach (var mode in MapModes.All)
        {
            if (mode.Category != _category) continue;

            var button = _viewButtons[mode.Name];
            bool on = mode.Name == _view;
            bool available = Available(mode);

            button.BackColor = on ? Theme.Accent : available ? Theme.SurfaceHigh : Theme.Surface;
            button.ForeColor = on ? Theme.AccentText : available ? Theme.Text : Theme.TextDim;
            button.FlatAppearance.MouseOverBackColor = on ? Theme.Accent : Theme.Border;

            _tips.SetToolTip(button, available
                ? mode.Pick?.Kind switch
                {
                    MapPick.Realm => "Click a realm to focus and drill into it — Ctrl+click for the county",
                    not null => "Click the map in this mode to inspect and edit",
                    null => null,
                }
                : "Shows written content — available after Write mod");

            _modeStrip.Controls.Add(button);
        }

        _modeStrip.ResumeLayout();
    }

    private void ShowLegend(MapMode mode)
    {
        _legendBar.SuspendLayout();

        var old = _legendBar.Controls.Cast<Control>().ToList();
        _legendBar.Controls.Clear();
        foreach (var control in old) control.Dispose();

        if (mode.Legend is { } legend)
        {
            foreach (var ((r, g, b), label) in legend)
            {
                _legendBar.Controls.Add(new Panel
                {
                    Width = 10,
                    Height = 10,
                    BackColor = Color.FromArgb(r, g, b),
                    Margin = new Padding(8, 5, 3, 0),
                });
                _legendBar.Controls.Add(new Label
                {
                    Text = label,
                    AutoSize = true,
                    Font = Theme.Ui,
                    ForeColor = Theme.TextDim,
                    Margin = new Padding(0, 3, 0, 0),
                });
            }
        }

        // The Realms drill-down borrows this bar for its breadcrumb — the mode has no legend, and
        // a second bar that exists for one mode would spend height on every other one.
        bool breadcrumb = mode.Name == "Realms" && _realmFocus.Count > 0 && Realm is { } graph;
        if (breadcrumb)
        {
            AddCrumb("World", 0);

            for (int i = 0; i < _realmFocus.Count; i++)
            {
                _legendBar.Controls.Add(new Label
                {
                    Text = "▸",
                    AutoSize = true,
                    Font = Theme.Ui,
                    ForeColor = Theme.TextDim,
                    Margin = new Padding(2, 3, 2, 0),
                });

                var primary = Realm!.Primary(_realmFocus[i]);
                AddCrumb($"{TitleInspector.TierName(primary)} {primary.Name}", i + 1);
            }

            _legendBar.Controls.Add(new Label
            {
                Text = "   Esc steps back · Ctrl+click jumps to a county",
                AutoSize = true,
                Font = Theme.Ui,
                ForeColor = Theme.TextDim,
                Margin = new Padding(12, 3, 0, 0),
            });
        }

        _legendBar.Visible = mode.Legend is not null || breadcrumb;
        _legendBar.ResumeLayout();

        void AddCrumb(string text, int keep)
        {
            var link = new Label
            {
                Text = text,
                AutoSize = true,
                Font = keep == _realmFocus.Count ? Theme.UiBold : Theme.Ui,
                ForeColor = Theme.Text,
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 3, 2, 0),
            };
            link.Click += (_, _) => SetRealmFocusDepth(keep);
            _legendBar.Controls.Add(link);
        }
    }

    /// <summary>Truncates the drill-down to a breadcrumb level and repaints. Zero is the world.</summary>
    private void SetRealmFocusDepth(int keep)
    {
        if (_realmFocus.Count <= keep) return;
        _realmFocus.RemoveRange(keep, _realmFocus.Count - keep);
        SelectView("Realms");
    }

    private void PickTitleAt(Point pixel)
    {
        if (_busy || _edits.Target is not { } target || _result is null) return;
        if (MapModes.Find(_view)?.Pick is not { } view) return;

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
            // The colours on the Realms view are whole de facto realms, so a plain click resolves
            // to the realm and drills a rung at a time; Ctrl held takes the county under the
            // cursor directly, and the focus with it.
            case MapPick.Realm when ModifierKeys.HasFlag(Keys.Control) || Realm is null:
                FocusCounty(title);
                break;

            case MapPick.Realm:
                PickRealm(title);
                break;

            case MapPick.Title:
                _titles.Reveal(title);
                _status.Text = $"{TitleInspector.TierName(title)} {title.Name}";
                break;

            case MapPick.Culture:
                var culture = target.Written.Cultures.For(title);
                Inspect([culture]);
                _status.Text = $"Culture {culture.Name} — {title.Name}";
                break;

            case MapPick.Faith:
                var faith = target.Written.Faiths.For(title);
                Inspect([faith]);
                _status.Text = $"Faith {faith.Name} — {title.Name}";
                break;
        }
    }

    /// <summary>
    /// One click's worth of realm drilling, given the county under the cursor.
    ///
    /// Unfocused, a click focuses the whole realm the county belongs to. Focused, a click inside
    /// the realm descends one structural level toward the clicked county — into the direct vassal
    /// whose subtree it sits in — until it lands on the focused ruler's own demesne, where the only
    /// thing left to open is the county itself. A click outside the focused realm steps back out
    /// one level, which together with Esc makes the drill reversible from either hand.
    /// </summary>
    private void PickRealm(MapGen.Title county)
    {
        var graph = Realm!;
        var path = graph.PathFromTop(graph.SeatOfCounty(county));

        if (_realmFocus.Count == 0)
        {
            _realmFocus.Add(path[0]);
            InspectRealm(path[0]);
        }
        else
        {
            int at = -1;
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i] == _realmFocus[^1]) { at = i; break; }
            }

            if (at < 0)
            {
                _realmFocus.RemoveAt(_realmFocus.Count - 1);
                if (_realmFocus.Count > 0) InspectRealm(_realmFocus[^1]);
                else _status.Text = "Back to all realms";
            }
            else if (at < path.Count - 1)
            {
                _realmFocus.Add(path[at + 1]);
                InspectRealm(path[at + 1]);
            }
            else
            {
                _titles.Reveal(county);
                _status.Text =
                    $"Demesne of {graph.Primary(path[at]).Name} — county {county.Name}";
            }
        }

        SelectView("Realms");
    }

    /// <summary>
    /// Ctrl+click: the county itself, and the map focused on whoever holds it.
    ///
    /// The plain click descends a rung at a time, which is the right pace for a hierarchy you do
    /// not know yet. This is the other question — one county already in view, and "who holds this,
    /// and what else do they hold?" — so it takes the whole chain in one step and leaves the
    /// breadcrumb as the way back up. Landing focused on the holder rather than merely opening the
    /// county is what makes the answer visible: their other holdings light up around the one that
    /// was clicked.
    /// </summary>
    private void FocusCounty(MapGen.Title county)
    {
        _titles.Reveal(county);

        // Before a write there is no realm structure to focus, and the county is the whole answer.
        if (Realm is not { } graph)
        {
            _status.Text = $"{TitleInspector.TierName(county)} {county.Name}";
            return;
        }

        var seat = graph.SeatOfCounty(county);

        _realmFocus.Clear();
        _realmFocus.AddRange(graph.PathFromTop(seat));

        var holder = graph.Primary(seat);
        _status.Text = $"County {county.Name} — held by {TitleInspector.TierName(holder)} "
                       + holder.Name + (county == seat ? " (their seat)" : "");

        SelectView("Realms");
    }

    private void InspectRealm(MapGen.Title seat)
    {
        var graph = Realm!;
        var primary = graph.Primary(seat);

        Inspect([primary]);
        _status.Text = $"{TitleInspector.TierName(primary)} {primary.Name} — " +
                       $"{graph.RealmSize(seat)} counties, {graph.VassalSeats(seat).Count} direct vassals";
    }

    /// <summary>The inspector's "focus map" path: jump the Realms view straight to this ruler.</summary>
    private void FocusRealmOnMap(MapGen.Title seat)
    {
        if (Realm is not { } graph) return;

        _realmFocus.Clear();
        _realmFocus.AddRange(graph.PathFromTop(seat));
        SelectView("Realms");
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
            if (inspector is TitleInspector created) created.FocusRealm += FocusRealmOnMap;
            _inspectors[kind] = inspector;

            inspector.Show(this);
            PlaceInspector(inspector);
        }

        // Refreshed on every visit rather than at creation: the graph is rebuilt with each write,
        // and the window outlives many of them.
        if (inspector is TitleInspector titles) titles.Realm = Realm;

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

        if (_redrawQueued || !MapModes.All.Any(
                m => m.RepaintKind is { } kind && MapModes.Repaints(kind, touched))) return;

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

        bool showing = MapModes.Find(_view)?.RepaintKind is { } kind
                       && MapModes.Repaints(kind, stale);

        if (showing) _viewer.SetImage(null);

        foreach (var mode in MapModes.All)
        {
            if (mode.RepaintKind is not { } repaint || !MapModes.Repaints(repaint, stale)) continue;
            if (_rendered.Remove(mode.Name, out var dead)) dead.Dispose();
        }

        if (showing) SelectView(_view);
    }

    private void ShowReadout(float zoom, Point? pixel)
    {
        if (_result is null)
        {
            _readout.Text = "";
            return;
        }

        string probe = pixel is { } p ? Probe(p) : "";
        _readout.Text = probe.Length > 0
            ? $"{probe}   ·   {zoom * 100:F0}%"
            : $"{_view}   {zoom * 100:F0}%";
    }

    private MapGen.Title?[]? _probeBaronies;

    /// <summary>Baronies by province id, built once per result so a mouse move costs lookups only.</summary>
    private MapGen.Title?[] ProbeBaronies()
    {
        if (_probeBaronies is not null) return _probeBaronies;

        var byId = new MapGen.Title?[_result!.BaronyCount + 1];
        foreach (var title in MapGen.Titles.Flatten(_result.Titles))
            if (title.Tier == "b" && title.ProvinceId >= 1 && title.ProvinceId <= _result.BaronyCount)
                byId[title.ProvinceId] = title;

        return _probeBaronies = byId;
    }

    /// <summary>
    /// What is under the cursor: county, duchy and kingdom on land, the water's written name at
    /// sea, plus whatever line the active mode adds — terrain class, temperature, culture and so
    /// on. Coordinates go through the current bitmap's size rather than a fixed step because the
    /// heightmap mode renders at a different resolution from every other view.
    /// </summary>
    private string Probe(Point pixel)
    {
        // The focused-realm frame bypasses the render cache, so the size lookup has to as well.
        var bitmap = _focusFrame
                     ?? (_rendered.TryGetValue(_view, out var cached) ? cached : null);
        if (_result is null || bitmap is null) return "";

        var map = _result.Provinces;
        int mx = Math.Clamp(pixel.X * map.Width / Math.Max(1, bitmap.Width), 0, map.Width - 1);
        int my = Math.Clamp(pixel.Y * map.Height / Math.Max(1, bitmap.Height), 0, map.Height - 1);
        int cell = my * map.Width + mx;
        int id = _result.ProvinceOrder[map.Label[cell]];

        MapGen.Title? county = null;
        string place;

        if (id >= 1 && id <= _result.BaronyCount)
        {
            var barony = ProbeBaronies()[id];
            string? duchy = null, kingdom = null;

            for (var walk = barony; walk is not null; walk = walk.Parent)
            {
                if (walk.Tier == "c") county = walk;
                else if (walk.Tier == "d") duchy = walk.Name;
                else if (walk.Tier == "k") kingdom = walk.Name;
            }

            place = county is null
                ? barony?.Name ?? $"province {id}"
                : string.Join(" · ", new[] { county.Name, duchy, kingdom }.Where(n => n is not null));
        }
        else if (id <= _result.LandCount)
        {
            place = "Impassable";
        }
        else
        {
            place = _written is not null && _written.WaterNames.TryGetValue(id, out var water)
                ? water
                : "Sea";
        }

        if (MapModes.Find(_view)?.Probe is { } probe
            && probe(_result, _written, cell, county) is { } extra)
        {
            place = $"{place} · {extra}";
        }

        // The realm line lives here rather than in the registry because it needs the graph, which
        // is this form's to build and invalidate.
        if (_view == "Realms" && county is not null && Realm is { } graph)
        {
            var holder = graph.Primary(graph.SeatOfCounty(county));
            var top = graph.Primary(graph.PathFromTop(graph.SeatOfCounty(county))[0]);

            place = holder == top
                ? $"{place} · {TitleInspector.TierName(top)} {top.Name}"
                : $"{place} · {TitleInspector.TierName(top)} {top.Name} · " +
                  $"held by {TitleInspector.TierName(holder)} {holder.Name}";
        }

        return place;
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