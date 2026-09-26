using System.Diagnostics;
using System.Drawing.Imaging;
using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// One window: choose a heightmap, tune the settings, look at what they produce, write the mod.
/// </summary>
public sealed partial class MainForm : ChromeForm
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
    // Both live in the Mod menu rather than on the toolbar, so they are ToolStrip items rather
    // than controls. Their .Text/.Checked/.Enabled read the same from the calling code either way.
    private readonly ToolStripTextBox _launchArgs = new()
    {
        Size = new Size(200, 23),
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.SurfaceHigh,
        ForeColor = Theme.Text,
        ToolTipText = "Launch arguments passed to ck3.exe (e.g. -debug_mode -mapeditor -novid)",
    };

    private readonly ToolStripMenuItem _closeOnLaunch = new("Close the generator on launch")
    {
        CheckOnClick = true,
        ToolTipText = "Close the map generator when launching Crusader Kings III",
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

    // The walkthrough is also under Help and inside the welcome page, but neither is where anyone
    // looks while staring at an Azgaar chip they don't know how to fill. One click, next to it.
    private readonly Button _azgaarHelp = Theme.MakeButton("Azgaar?", 62);

    private AzgaarGuide? _guide;
    private WelcomeGuide? _welcome;

    /// <summary>The top row; see <see cref="WorkspaceBar"/> for why it is up there.</summary>
    private readonly WorkspaceBar _workspaceBar = new();
    private readonly Dictionary<Workspace, Control> _workspacePages = [];
    private Workspace _workspace = Workspace.World;

    /// <summary>What the World workspace's canvas is showing. The settings sidebar stays for all three.</summary>
    private enum WorldView { Map, ThreeD, Titles }

    private WorldView _worldView = WorldView.Map;
    private readonly Dictionary<WorldView, Control> _worldViewPages = [];
    private readonly Dictionary<WorldView, Button> _worldViewButtons = [];

    /// <summary>The Terrain workspace: CK3 Heightmap Forge, embedded. See <see cref="Forge.ForgePanel"/>.</summary>
    private readonly Forge.ForgePanel _forge = new() { Dock = DockStyle.Fill };

    /// <summary>The Climate workspace: paint the climate over the heightmap. See <see cref="ClimatePanel"/>.</summary>
    private readonly ClimatePanel _climate = new() { Dock = DockStyle.Fill };

    /// <summary>The History workspace: the written world run on past its start date. See <see cref="HistoryPanel"/>.</summary>
    private readonly HistoryPanel _history = new() { Dock = DockStyle.Fill };

    /// <summary>
    /// The world's calendar, shown in place of the settings grid when its entry in the sections
    /// list is picked. It is a short form, not a workspace, so it lives with the other settings.
    /// </summary>
    private readonly CalendarPanel _calendar = new() { Dock = DockStyle.Fill, Visible = false };

    private const string CalendarSection = "Calendar";

    /// <summary>Which source the Climate tab was last given terrain for; null when it needs a fresh one.</summary>
    private string? _climateStamp;
    private int _climateGeneration;

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

    /// <summary>The World canvas over the log. The log half collapses; see <see cref="SetLogOpen"/>.</summary>
    private SplitContainer _right = null!;

    private Panel _viewArea = null!;
    private Panel _logPane = null!;
    private Control _logBar = null!;
    private readonly Button _logToggle = Theme.MakeButton("Log", 64);
    private bool _logOpen;

    /// <summary>The log's height while open, kept across collapses and sessions.</summary>
    private int _logHeight = 200;

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
        SyncCalendarSection();

        _sections.SelectedIndex =
            _state.SettingsSection is { } saved && _sections.Items.IndexOf(saved) is var found and > 0
                ? found
                : 0;
        ApplySection();

        _sections.SelectedIndexChanged += (_, _) => ApplySection();
        StyleSections();
        _settingsSearch.TextChanged += (_, _) =>
        {
            // The search runs over the grid, so it cannot be answered while the calendar hides it.
            if (_calendar.Visible && _settingsSearch.TextLength > 0) _sections.SelectedIndex = 0;
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

            if (changed is nameof(MapConfig.CalendarEnabled) or nameof(MapConfig.ContentSource)) SyncCalendarSection();
            if (changed == nameof(MapConfig.StartYear)) _calendar.RefreshPreview();

            // The Climate tab's prediction runs on the same settings; its cached model is stale.
            _climate.InvalidateModel();

            if (!NormalizationSettings.Contains(changed)) return;
            InvalidateProcessed();
            if (_sourceShown) ShowSourceAsync().Forget("source view");
        };

        _options.Config.Seed = Random.Shared.Next(1, int.MaxValue);
        _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
        _seed.ValueChanged += (_, _) => _options.Config.Seed = (int)_seed.Value;

        _launchArgs.Text = _state.LaunchArgs ?? "-debug_mode";
        _closeOnLaunch.Checked = _state.CloseOnLaunch;

        _browse.Click += (_, _) => PickHeightmap();
        _recent.Click += (_, _) => ShowRecentHeightmaps();
        _tips.SetToolTip(_recent, "Recent heightmaps");
        _azgaar.AutoSize = true;
        _azgaar.MaximumSize = new Size(130, 0);
        _azgaar.AutoEllipsis = true;
        _azgaar.Click += (_, _) => ShowAzgaarMenu();
        ApplyAzgaarChip();

        // Left enabled during a run on purpose: it only opens a window to read, and a run is
        // exactly when someone has time to read it.
        _azgaarHelp.Click += (_, _) => ShowAzgaarGuide();
        _tips.SetToolTip(_azgaarHelp, "How to export a map from Azgaar and import it here");

        _roll.Click += (_, _) => RollSeed();
        _preview.Click += async (_, _) => await PreviewAsync();
        _writeMod.Click += async (_, _) => await WriteModAsync();
        _cancel.Click += (_, _) => RequestCancel();

        // Hidden until a run is actually going, so the row never shows a button that is a no-op.
        _cancel.Enabled = false;
        _cancel.Visible = false;

        // The chip wears the chosen file's name, so it has to be free to grow — but only so far.
        // Unbounded, a long filename is what pushes Preview and Write mod off the end of the bar
        // on a narrow window; capped, the name is what gets an ellipsis and the buttons stay put.
        // The full path is on the tooltip either way.
        _browse.AutoSize = true;
        _browse.MaximumSize = new Size(190, 0);
        _browse.AutoEllipsis = true;
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

        // The start page goes in first so it docks last: it fills whatever the caption row leaves.
        // It starts hidden, so the first layout sizes the workspaces; OnLoad shows it afterwards.
        Controls.Add(BuildStartPage());
        Controls.Add(BuildQuickPage());
        Controls.Add(_workspaceHost = BuildWorkspaces());
        Controls.Add(BuildWorkspaceBar());

        // Docking lays out from the last control back, so the title row has to be added after the
        // workspace bar to end up above it. The menu bar lives inside it; see ChromeForm.
        Controls.Add(CaptionBar = new TitleBar(this, BuildMenuBar()));
        Controls.Add(_statusBar = BuildStatusBar());

        _lastModDir = _state.LastModDir;

        ApplySource();
        SelectView(_view);

        Console.SetOut(new TextBoxWriter(_log));
        Stage.Entering += OnStageEntered;
        Stage.Detailing += OnStageDetail;
        _tick.Tick += (_, _) => ShowProgress();
    }

    /// <summary>
    /// The menu bar: every command the window has, in the place Windows users look for it first.
    ///
    /// It exists because the toolbar could not hold them. Measured, that single row wanted about
    /// 1490 px to lay out while the window may be 1000 px wide, and the left-hand group was a
    /// wrapping <see cref="FlowLayoutPanel"/> — so below that width Preview, Write mod and Cancel
    /// silently wrapped to a second row inside a 40 px bar and vanished. The bar now carries the
    /// handful of commands a run actually needs at hand and everything else lives here.
    ///
    /// Menu and toolbar share handlers rather than controls: a command written once shows up in
    /// both, and <see cref="SetEnabled"/> stays the only place that decides what a run disables —
    /// the items read their state off the buttons when the menu opens.
    /// </summary>
    private MenuStrip BuildMenuBar()
    {
        var menu = Theme.MakeMenuBar();

        // ---- File -----------------------------------------------------------------------
        var chooseHeightmap = MenuItem("Choose heightmap…", PickHeightmap);
        var editWorld = MenuItem("Open generated world… (WIP)", () => OpenGeneratedWorldAsync().Forget("open generated world"));
        var closeWorld = MenuItem("Return to generator", CloseLoadedWorld);
        var recent = Submenu("Recent heightmaps", RecentMenuItems);
        var azgaar = Submenu("Azgaar export", AzgaarMenuItems);
        var exportView = MenuItem("Export the current view…", ExportView, "Ctrl+E");
        var savePreset = MenuItem("Save preset…", SavePreset);
        var loadPreset = MenuItem("Load preset…", LoadPreset);
        var exit = MenuItem("Exit", Close);
        var startPage = MenuItem("Start page", ShowStartPage);

        var file = TopMenu(menu, "&File",
            startPage, new ToolStripSeparator(),
            editWorld, closeWorld, new ToolStripSeparator(), chooseHeightmap, recent, azgaar, new ToolStripSeparator(),
            exportView, new ToolStripSeparator(),
            savePreset, loadPreset, new ToolStripSeparator(),
            exit);

        file.DropDownOpening += (_, _) =>
        {
            startPage.Enabled = !_busy;
            chooseHeightmap.Enabled = _browse.Enabled;
            editWorld.Enabled = !_busy;
            closeWorld.Enabled = !_busy && _loadedWorld is not null;
            recent.Enabled = _recent.Enabled;
            azgaar.Enabled = _azgaar.Enabled;
            exportView.Enabled = !_busy;
            savePreset.Enabled = _savePreset.Enabled;
            loadPreset.Enabled = _loadPreset.Enabled;
        };

        // ---- View -----------------------------------------------------------------------
        var terrain = MenuItem("Terrain", () => SelectWorkspace(Workspace.Terrain), "Ctrl+1");
        var climate = MenuItem("Climate", () => SelectWorkspace(Workspace.Climate), "Ctrl+2");
        var world = MenuItem("World", () => SelectWorkspace(Workspace.World), "Ctrl+3");
        var history = MenuItem("History", () => SelectWorkspace(Workspace.History), "Ctrl+4");
        var mapView = MenuItem("Map", () => SelectWorldView(WorldView.Map));
        var solidView = MenuItem("3D", () => SelectWorldView(WorldView.ThreeD));
        var titlesView = MenuItem("Titles", () => SelectWorldView(WorldView.Titles));
        var showLog = MenuItem("Log", () => SetLogOpen(!_logOpen, remember: true), "Ctrl+L");

        var view = TopMenu(menu, "&View",
            terrain, climate, world, history, new ToolStripSeparator(),
            mapView, solidView, titlesView, new ToolStripSeparator(),
            showLog);

        view.DropDownOpening += (_, _) =>
        {
            terrain.Visible = _workspaceBar.IsAvailable(Workspace.Terrain);
            climate.Visible = _workspaceBar.IsAvailable(Workspace.Climate);
            history.Visible = _workspaceBar.IsAvailable(Workspace.History);
            terrain.Checked = _workspace == Workspace.Terrain;
            climate.Checked = _workspace == Workspace.Climate;
            world.Checked = _workspace == Workspace.World;
            history.Checked = _workspace == Workspace.History;

            bool inWorld = _workspace == Workspace.World;
            mapView.Checked = inWorld && _worldView == WorldView.Map;
            solidView.Checked = inWorld && _worldView == WorldView.ThreeD;
            titlesView.Checked = inWorld && _worldView == WorldView.Titles;
            solidView.Enabled = _worldViewButtons[WorldView.ThreeD].Enabled;
            showLog.Checked = _logOpen;
        };

        // ---- Generate -------------------------------------------------------------------
        var preview = MenuItem("Preview", () => PreviewAsync().Forget("preview"), "F5");
        var writeMod = MenuItem("Write mod", () => WriteModAsync().Forget("write mod"), "Ctrl+S");
        var cancelRun = MenuItem("Cancel run", RequestCancel, "Esc");
        var roll = MenuItem("New random seed", RollSeed);

        var generate = TopMenu(menu, "&Generate",
            preview, writeMod, cancelRun, new ToolStripSeparator(), roll);

        generate.DropDownOpening += (_, _) =>
        {
            preview.Enabled = _preview.Enabled;
            writeMod.Enabled = _writeMod.Enabled;
            cancelRun.Enabled = _busy;
            roll.Enabled = _roll.Enabled;
        };

        // ---- Mod ------------------------------------------------------------------------
        var openMod = MenuItem("Open mod folder", OpenModFolder);
        var modList = MenuItem("Manage mod list…", ShowModList);
        var launch = MenuItem("Launch CK3", LaunchGame);
        var launchOptions = new ToolStripMenuItem("Launch options")
        {
            DropDownItems =
            {
                new ToolStripMenuItem("Arguments passed to ck3.exe") { Enabled = false },
                _launchArgs,
                new ToolStripSeparator(),
                _closeOnLaunch,
            },
        };
        var gameFolder = MenuItem("Game folder…", PickGameFolder);

        var mod = TopMenu(menu, "&Mod",
            openMod, modList, new ToolStripSeparator(),
            launch, launchOptions, new ToolStripSeparator(),
            gameFolder);

        mod.DropDownOpening += (_, _) =>
        {
            openMod.Enabled = _openMod.Enabled;
            modList.Enabled = !_busy;
            launch.Enabled = _launchGame.Enabled;
            launchOptions.Enabled = !_busy;

            // The same warning the toolbar chip wears when the install could not be found, so the
            // menu route to fixing it says so too rather than looking like a settings detour.
            bool found = Core.GameLocator.IsGameDir(_options.GameDir);
            gameFolder.Text = found ? "Game folder…" : "Game folder — not found ⚠";
            gameFolder.Enabled = _gameFolder.Enabled;
        };

        // ---- Help -----------------------------------------------------------------------
        TopMenu(menu, "&Help",
            MenuItem("Getting started…", ShowWelcomeGuide),
            MenuItem("How to export from Azgaar…", ShowAzgaarGuide));

        MainMenuStrip = menu;
        return menu;
    }

    /// <summary>
    /// A menu item that runs <paramref name="click"/>.
    ///
    /// <paramref name="shortcut"/> is drawn on the item but deliberately not registered as a
    /// <see cref="ToolStripMenuItem.ShortcutKeys"/>: the keys are already handled in
    /// <see cref="ProcessCmdKey"/>, where each one carries the guard that says when it applies,
    /// and a menu item claiming the same chord would fire it a second time without the guard.
    /// </summary>
    private static ToolStripMenuItem MenuItem(string text, Action click, string? shortcut = null)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => click();

        if (shortcut is not null)
        {
            item.ShortcutKeyDisplayString = shortcut;
            item.ShowShortcutKeys = true;
        }

        return item;
    }

    /// <summary>
    /// A submenu filled the moment it is opened, so a list that changes between openings — the
    /// recent files, whether an Azgaar export is loaded — is never a stale one. The placeholder is
    /// load-bearing: a dropdown with no items at all is not openable, so the fill would never run.
    /// </summary>
    private static ToolStripMenuItem Submenu(string text, Func<ToolStripItem[]> items)
    {
        var parent = new ToolStripMenuItem(text);
        parent.DropDownItems.Add(new ToolStripSeparator());

        parent.DropDownOpening += (_, _) =>
        {
            parent.DropDownItems.Clear();
            parent.DropDownItems.AddRange(items());
        };

        return parent;
    }

    private static ToolStripMenuItem TopMenu(MenuStrip bar, string text, params ToolStripItem[] items)
    {
        var top = new ToolStripMenuItem(text);
        top.DropDownItems.AddRange(items);
        bar.Items.Add(top);
        return top;
    }

    private void RollSeed() => _seed.Value = Random.Shared.Next(1, int.MaxValue);

    /// <summary>
    /// The top row: the workspaces on the left, and on the right what to do with the built mod —
    /// commands that belong to no one workspace, so they stay put whichever is showing.
    ///
    /// The game folder chip appears only when CK3 could not be found, in red, because that is the
    /// one time anyone needs to go looking for it. Mod ▸ Game folder… works either way.
    /// </summary>
    private Control BuildWorkspaceBar()
    {
        // Right to left, so the group hugs the window edge; visually it reads
        // "Open mod folder · Launch CK3", with the game folder chip ahead of them only when the
        // install could not be found.
        _workspaceBar.Trailing.Controls.Add(_gameFolder);
        _workspaceBar.Trailing.Controls.Add(_launchGame);
        _workspaceBar.Trailing.Controls.Add(_openMod);

        _workspaceBar.Picked += SelectWorkspace;
        return _workspaceBar;
    }

    /// <summary>
    /// The World workspace's toolbar, cut back to the commands a run needs within reach: what the
    /// world is built *from* and the runs that build it on the left, which view of it is showing on
    /// the right. Everything else lives in <see cref="BuildMenuBar"/>.
    ///
    /// The Azgaar button is a chip: once an export is loaded it wears the export's name.
    /// </summary>
    private Control BuildWorldToolbar()
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
            // Same reason the group opposite sets it: wrapping in a 40 px bar does not wrap, it
            // hides. Clipping at the window edge at least leaves the row's own order intact.
            WrapContents = false,
        };

        build.Controls.Add(_browse);
        build.Controls.Add(_recent);
        build.Controls.Add(_azgaar);
        build.Controls.Add(_azgaarHelp);
        build.Controls.Add(Separator());
        build.Controls.Add(Caption("Seed"));
        build.Controls.Add(_seed);
        build.Controls.Add(_roll);
        build.Controls.Add(Separator());
        build.Controls.Add(_preview);
        build.Controls.Add(_writeMod);
        build.Controls.Add(_cancel);

        var views = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            // Without this the docked panel settles at one button wide and quietly wraps the
            // rest below the 40 px bar, where they render as nothing at all.
            WrapContents = false,
            Padding = new Padding(0, 3, 8, 3),
            BackColor = Color.Transparent,
        };

        var options = new (WorldView View, string Text, string Tip)[]
        {
            (WorldView.Map, "Map", "The generated map, one mode at a time"),
            (WorldView.ThreeD, "3D", "The heightmap in relief — as loaded before a build, as shipped after one"),
            (WorldView.Titles, "Titles", "The title tree: rename, recolour and rearrange"),
        };

        foreach (var (view, text, tip) in options)
        {
            var button = new Theme.SegmentButton { Text = text, Width = 64, Name = $"view{view}" };
            button.Click += (_, _) => SelectWorldView(view);
            button.EnabledChanged += (_, _) => Theme.StyleSegment(button, view == _worldView);
            _tips.SetToolTip(button, tip);
            _worldViewButtons[view] = button;
        }

        views.Controls.Add(Theme.MakeSegmented(_worldViewButtons.Values));

        bar.Controls.Add(build);
        bar.Controls.Add(views);
        bar.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });

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

    /// <summary>
    /// The three workspaces, stacked in one host with only the current one visible. Each owns the
    /// whole area under the workspace bar: Terrain and Climate are their panels alone, with none of
    /// the generator's settings, seed or log around them; World is the generator.
    /// </summary>
    private Control BuildWorkspaces()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

        _forge.UseForGeneration += UseForgeForGeneration;
        _forge.PresetDir = _state.ForgePresetDir;

        // Climate paints over whatever heightmap is chosen. An unvisited workspace changes nothing
        // — see ClimatePanel.EffectivePaint — and a painted one says so in its label.
        _climate.PaintDir = _state.ClimatePaintDir;
        _climate.UseAutomatic = _state.ClimateAutomatic;
        _climate.PaintChanged += RefreshClimateLabel;

        _workspacePages[Workspace.Terrain] = Page(_forge);
        _workspacePages[Workspace.Climate] = Page(_climate);
        _workspacePages[Workspace.World] = BuildWorld();
        _workspacePages[Workspace.History] = Page(_history);
        _history.ApplyRequested += applied => ApplyHistoryAsync(applied).Forget("apply history");
        _history.DiscardRequested += DiscardHistory;

        // All of them start visible, so the first layout sizes every one of them — the splitters
        // placed in OnLoad clamp to the size they are given, and a page that has never been laid
        // out is 150 px wide. OnLoad hides all but the current one once they are placed.
        foreach (var page in _workspacePages.Values) host.Controls.Add(page);
        return host;

        static Control Page(Control content)
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            page.Controls.Add(content);
            return page;
        }
    }

    /// <summary>
    /// The World workspace: its toolbar across the top, settings on the left, and on the right the
    /// canvas — map, 3D or titles — over a log that folds away to one row.
    /// </summary>
    private Control BuildWorld()
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
        settings.Controls.Add(_calendar);
        _calendar.Bind(_options.Config);
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

        var titles = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        titles.Controls.Add(_titles);

        _worldViewPages[WorldView.Map] = viewer;
        _worldViewPages[WorldView.ThreeD] = BuildSourceView();
        _worldViewPages[WorldView.Titles] = titles;

        var canvas = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        foreach (var (view, page) in _worldViewPages)
        {
            page.Visible = view == _worldView;
            canvas.Controls.Add(page);
        }

        _logPane = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        _logPane.Controls.Add(_log);

        // The log keeps its own height when the window is resized: it is a drawer under the map,
        // and the map is what should take the extra room.
        _right = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = Theme.Border,
            FixedPanel = FixedPanel.Panel2,
        };
        _right.Panel1.Controls.Add(canvas);
        _right.Panel2.Controls.Add(_logPane);
        _right.SplitterMoved += (_, _) =>
        {
            if (_logOpen) _logHeight = _right.Panel2.Height;
        };

        _viewArea = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        _viewArea.Controls.Add(_right);
        _logBar = BuildLogHeader();

        // Open for the first layout, like the workspaces, so OnLoad can place the splitter against
        // real sizes; it folds the log to the saved state from there.
        _logOpen = true;
        _logPane.Controls.Add(_logBar);

        _body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Border,
            FixedPanel = FixedPanel.Panel1,
        };
        _body.Panel1.Controls.Add(settings);
        _body.Panel2.Controls.Add(_viewArea);

        var world = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        world.Controls.Add(_body);
        world.Controls.Add(BuildPendingBar());
        world.Controls.Add(BuildWorldToolbar());
        return world;
    }

    /// <summary>
    /// Shows one workspace and hides the other two. Terrain and Climate are filled the first time
    /// they are opened, not at startup: the Forge's first preview is a noise pass and the Climate
    /// panel decodes the heightmap, and neither is worth paying for until someone looks.
    /// </summary>
    private void SelectWorkspace(Workspace workspace)
    {
        // Every route to a workspace — the Complex card, Ctrl+1..4, the View menu, opening a
        // world — is also a way off the start page, and off the Quick page.
        LeaveLauncher();

        if (!_workspaceBar.IsAvailable(workspace))
        {
            _status.Text = "An opened mod is edited in the World workspace; terrain and climate tools are for generating.";
            return;
        }

        bool changed = workspace != _workspace || !_workspacePages[workspace].Visible;
        _workspace = workspace;
        _workspaceBar.SetCurrent(workspace);

        if (!changed) return;

        SuspendLayout();
        foreach (var (key, page) in _workspacePages) page.Visible = key == workspace;
        ResumeLayout();

        if (workspace == Workspace.Terrain) _forge.EnsureStarted();
        if (workspace == Workspace.Climate) ShowClimateAsync().Forget("climate view");
        if (workspace == Workspace.World && _worldView == WorldView.ThreeD) EnsureSourceShown();
    }

    /// <summary>Switches the World canvas between the map, the 3D view and the title tree.</summary>
    private void SelectWorldView(WorldView view)
    {
        if (_workspace != Workspace.World) SelectWorkspace(Workspace.World);
        if (!_worldViewButtons[view].Enabled) return;

        _worldView = view;
        foreach (var (key, page) in _worldViewPages) page.Visible = key == view;
        foreach (var (key, button) in _worldViewButtons) Theme.StyleSegment(button, key == view);

        if (view == WorldView.ThreeD) EnsureSourceShown();
    }

    /// <summary>
    /// Loaded when the 3D view is first opened, not at startup: decoding a vanilla-sized heightmap
    /// is several seconds, and a window that takes that long to appear for a view nobody asked
    /// for is a worse trade than a view that takes a moment to fill in.
    /// </summary>
    private void EnsureSourceShown()
    {
        if (_sourceShown) return;
        _sourceShown = true;

        if (_processedSource is not null)
        {
            SetSourceStage(processed: true);
            _solid.SetField(_processedSource, _processedPacked, "Nothing to show.");
        }
        else if (!_processedPending)
        {
            ShowSourceAsync().Forget("source view");
        }
        // else: a build just finished and its processed heightmap is still being prepared; that
        // task publishes here itself when it lands, now that the view is live.
    }

    private bool ShowingSolid => _workspace == Workspace.World && _worldView == WorldView.ThreeD;

    /// <summary>
    /// Opens or folds the log. Folded, its header row stays at the foot of the canvas — Clear,
    /// Copy and Search still in reach — and the map takes the room. <paramref name="remember"/>
    /// is for the user's own clicks; a run opening it for its duration does not change what the
    /// next session starts with.
    /// </summary>
    private void SetLogOpen(bool open, bool remember = false)
    {
        if (remember) _state.LogOpen = open;
        if (open == _logOpen) return;
        _logOpen = open;

        SuspendLayout();
        if (open)
        {
            _right.Panel2Collapsed = false;
            _logPane.Controls.Add(_logBar);      // added last, so it docks first: above the text
            Place(_right, 120, 80, _right.Height - _right.SplitterWidth - _logHeight);
        }
        else
        {
            _right.Panel2Collapsed = true;
            _viewArea.Controls.Add(_logBar);     // under the canvas, at the foot of the view
        }

        _logBar.Dock = open ? DockStyle.Top : DockStyle.Bottom;
        _logToggle.Text = open ? "Log  ▾" : "Log  ▴";
        _tips.SetToolTip(_logToggle, open ? "Fold the log away (Ctrl+L)" : "Show the log (Ctrl+L)");
        ResumeLayout();
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

        _drape.Items.Add("Terrain preview");
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
                    Emit.HeightmapPacker.Reconstruct(
                        levels, image.Width, image.Height, cfg.HeightmapSagBudget,
                        Emit.HeightmapPacker.TileStepFor(cfg), cfg.BalanceNeighbourLods),
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
                    Emit.HeightmapPacker.Reconstruct(
                        full, cfg.Width, cfg.Height, cfg.HeightmapSagBudget,
                        Emit.HeightmapPacker.TileStepFor(cfg), cfg.BalanceNeighbourLods),
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

        _logToggle.FlatAppearance.BorderSize = 0;
        _logToggle.BackColor = Theme.Surface;
        _logToggle.Font = Theme.UiBold;
        _logToggle.Click += (_, _) => SetLogOpen(!_logOpen, remember: true);

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(4, 2, 4, 0),
            BackColor = Theme.Surface,
        };
        row.Controls.Add(_logToggle);
        row.Controls.Add(Separator());
        row.Controls.Add(clear);
        row.Controls.Add(copy);
        row.Controls.Add(Caption("Search"));
        row.Controls.Add(_logSearch);

        var header = new Panel { Dock = DockStyle.Top, Height = 33, BackColor = Theme.Surface };
        header.Controls.Add(row);
        header.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
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

    private bool Available(MapMode mode) => _loadedWorld is not null ? _loadedWorld.Available(mode.Name) : !mode.AfterWrite || _written is not null;

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

        // Every page is still visible here (see BuildWorkspaces), so each splitter is placed
        // against its real size before the pages that are not current are hidden.
        PerformLayout();
        Place(_body, 300, 400, _state.SettingsWidth);
        if (_state.LogHeight > 0) _logHeight = _state.LogHeight;
        Place(_right, 120, 80, _right.Height - _right.SplitterWidth - _logHeight);
        if (_state.ForgeLeftWidth > 0) _forge.LeftWidth = _state.ForgeLeftWidth;

        _logOpen = !_state.LogOpen;          // so the call below always applies, either way
        SetLogOpen(_state.LogOpen);

        // An opened mod can be adopted before the window is shown (--edit-world), and it has only
        // the World workspace; a saved Terrain or Climate would leave no page showing at all.
        var workspace = Enum.TryParse(_state.Workspace, out Workspace saved) && _workspaceBar.IsAvailable(saved)
            ? saved
            : Workspace.World;
        var worldView = Enum.TryParse(_state.WorldView, out WorldView savedView) ? savedView : WorldView.Map;
        SelectWorldView(worldView);

        foreach (var page in _workspacePages.Values) page.Visible = false;
        SelectWorkspace(workspace);

        ReportFolders();
        RestoreClimatePaint();

        // Last, once every splitter above has been placed against a visible page.
        if (OpensToStartPage) ShowStartPage();
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

        // Only on the bar when there is something wrong with it. Found, it is a settings detail
        // nobody needs to see; not found, it is the one thing standing between the user and a run,
        // so it earns a red chip rather than a line in a menu. Mod ▸ Game folder… works either way.
        _gameFolder.Visible = !found;
        _gameFolder.Text = found ? "Game folder…" : "Game folder ⚠";
        _gameFolder.ForeColor = found ? Theme.Text : Theme.Danger;

        UpdateStartPageFolders();
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
        if (_loadedWorld is not null && !ConfirmLoadedEdits()) { e.Cancel = true; return; }
        // On the start page the window is a compact launcher; what is saved is the generator's
        // own placement, held aside when the page shrank the window (see PlacementToSave).
        var (bounds, maximized) = PlacementToSave();
        _state.Left = bounds.X;
        _state.Top = bounds.Y;
        _state.Width = bounds.Width;
        _state.Height = bounds.Height;
        _state.Maximized = maximized;
        _state.SettingsWidth = _body.SplitterDistance;
        _state.LogHeight = _logHeight;
        _state.Workspace = _workspace.ToString();
        _state.WorldView = _worldView.ToString();
        _state.ForgeLeftWidth = _forge.LeftWidth;
        _state.ForgePresetDir = _forge.PresetDir;
        _state.ClimatePaintDir = _climate.PaintDir;
        _state.ClimateAutomatic = _climate.UseAutomatic;
        try
        {
            string autosave = GuiState.ClimatePaintAutosave;
            if (!_climate.SavePaint(autosave) && File.Exists(autosave)) File.Delete(autosave);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not save the climate paint: {ex.Message}");
        }
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
        // The launcher pages hide the generator, so its shortcuts would act on something off
        // screen. Only the workspace keys get through, and they leave the page — except during a
        // Quick run, which is watched on its page; there Escape cancels, as it does anywhere else.
        if (_inLauncher)
        {
            if (_quickRunning)
            {
                if (key == Keys.Escape) { RequestCancel(); _quick.CancelPending(); return true; }
                return base.ProcessCmdKey(ref message, key);
            }

            if (key is not ((Keys.Control | Keys.D1) or (Keys.Control | Keys.D2)
                            or (Keys.Control | Keys.D3) or (Keys.Control | Keys.D4)))
                return base.ProcessCmdKey(ref message, key);
        }

        // Terrain and Climate own the brush keys while they are on screen, and say so by handling
        // them; anything they pass on falls through to the window's own shortcuts.
        if (_workspace == Workspace.Terrain && !TypingInText() && _forge.HandleKey(key)) return true;
        if (_workspace == Workspace.Climate && !TypingInText() && _climate.HandleKey(key)) return true;
        if (_workspace == Workspace.History && !TypingInText() && _history.HandleKey(key)) return true;

        bool onMap = _workspace == Workspace.World && _worldView == WorldView.Map;

        switch (key)
        {
            case Keys.Control | Keys.D1:
                SelectWorkspace(Workspace.Terrain);
                return true;

            case Keys.Control | Keys.D2:
                SelectWorkspace(Workspace.Climate);
                return true;

            case Keys.Control | Keys.D3:
                SelectWorkspace(Workspace.World);
                return true;

            case Keys.Control | Keys.D4:
                SelectWorkspace(Workspace.History);
                return true;

            case Keys.Control | Keys.L:
                SetLogOpen(!_logOpen, remember: true);
                return true;

            case Keys.F5 when _preview.Enabled:
                PreviewAsync().Forget("preview");
                return true;

            case Keys.Control | Keys.S when _writeMod.Enabled:
                WriteModAsync().Forget("write mod");
                return true;

            case Keys.Escape when _busy:
                RequestCancel();
                return true;

            case Keys.Escape when onMap && _view == "Realms" && _realmFocus.Count > 0:
                _realmFocus.RemoveAt(_realmFocus.Count - 1);
                SelectView("Realms");
                return true;

            case Keys.Oem4 when onMap && !TypingInText():   // [
                CycleMode(-1);
                return true;

            case Keys.Oem6 when onMap && !TypingInText():   // ]
                CycleMode(+1);
                return true;

            case Keys.Control | Keys.Oem4 when onMap:
                CycleCategory(-1);
                return true;

            case Keys.Control | Keys.Oem6 when onMap:
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
        if (_sourceShown) ShowSourceAsync().Forget("source view");

        // Climate paints over the source; a new one is read the next time the workspace is looked
        // at, or now if it is the one on screen.
        _climateStamp = null;
        if (_workspace == Workspace.Climate) ShowClimateAsync().Forget("climate view");
    }

    /// <summary>
    /// Hands the Climate tab the current source at province resolution, decoding it if no run has
    /// yet. Shares the decode cache with the run and the 3D tab, so a heightmap already read is
    /// not read again; only the province downsample and the land mask are computed here.
    /// </summary>
    private async Task ShowClimateAsync()
    {
        if (_source is not { } source)
        {
            _climate.SetTerrain(null);
            return;
        }

        var cfg = _options.Config;
        string stamp = source.Stamp;
        if (_climateStamp == stamp)
        {
            _climate.Activated();
            return;
        }

        // Not during a run: the run is decoding and normalising the same heightmap on the same
        // config, and a second decode beside it would race it for the cache and the settings.
        // The stamp stays unset, so the tab is filled in when the run ends — see SetEnabled.
        if (_busy)
        {
            _status.Text = "Climate will load its heightmap when the current run finishes.";
            return;
        }

        int generation = ++_climateGeneration;
        _status.Text = "Reading the heightmap for Climate…";

        try
        {
            var (image, terrain) = await Task.Run(() =>
            {
                var loaded = _loaded is not null && _loadedStamp == stamp
                    ? _loaded
                    : source.Produce(cfg, CancellationToken.None, MapGen.ConsoleProgress.Instance);

                MapGen.HeightmapSource.Apply(loaded, cfg);
                var elevation = loaded.ToElevation(cfg);
                var province = MapGen.Raster.ProvinceElevation(elevation, cfg);
                var land = MapGen.Raster.LandMask(elevation, cfg);

                return (loaded, new ClimatePanel.Terrain(cfg, province, land, stamp));
            });

            if (generation != _climateGeneration) return;

            _loaded = image;
            _loadedStamp = stamp;
            _climateStamp = stamp;
            _climate.SetTerrain(terrain);
            _status.Text = $"Climate ready — {source.Label}";
        }
        catch (Exception error)
        {
            if (generation != _climateGeneration) return;
            Console.WriteLine($"Could not read the heightmap for Climate: {error.Message}");
            _status.Text = "Could not read the heightmap for Climate — see log";
        }
    }

    /// <summary>The workspace says when its paint will change the next run, the way editable map modes do.</summary>
    private void RefreshClimateLabel()
        => _workspaceBar.SetLabel(Workspace.Climate, _climate.EffectivePaint is not null ? "Climate ✎" : "Climate");

    /// <summary>Where a preset's climate paint lives: beside it, named after it.</summary>
    private static string ClimateSidecar(string presetPath)
        => Path.Combine(Path.GetDirectoryName(presetPath) ?? "",
            Path.GetFileNameWithoutExtension(presetPath) + ".climate.png");

    /// <summary>
    /// Last session's paint, if the window closed with any. Restored rather than dropped because
    /// losing an afternoon's painting to a restart is worse than a tab that remembers; the title
    /// carries the pencil so it is never a silent influence on the next run.
    /// </summary>
    private void RestoreClimatePaint()
    {
        string path = GuiState.ClimatePaintAutosave;
        if (!File.Exists(path)) return;

        try
        {
            var paint = MapGen.ClimatePaint.Load(path);
            if (paint.IsEmpty) return;

            _climate.AdoptPaint(paint, "Climate paint restored from last session.");
            Console.WriteLine("Climate paint restored from last session" +
                              (_climate.UseAutomatic ? " (automatic climate switch is on, so it is not in use)" : "") + ".");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not restore last session's climate paint: {ex.Message}");
        }
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
        _status.Text = $"Building from the Terrain workspace's pipeline ({_forge.Session.Name}) — " +
                       "press Preview to generate from it" +
                       (allowUnverifiedSize ? " (at a size CK3 is not known to render, to test it)" : "");
    }

    private void ShowAzgaarMenu()
    {
        var menu = Theme.MakeMenu();
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Items.AddRange(AzgaarMenuItems());
        menu.Show(_azgaar, new Point(0, _azgaar.Height));
    }

    /// <summary>
    /// Shared by the toolbar chip and File ▸ Azgaar export, so the two routes cannot drift.
    /// Rebuilt per opening: whether "stop using" belongs on it depends on what is loaded now.
    /// </summary>
    private ToolStripItem[] AzgaarMenuItems()
    {
        var items = new List<ToolStripItem> { MenuItem("Choose Full export (.json)…", PickAzgaar) };

        if (!string.IsNullOrWhiteSpace(_options.Config.AzgaarJsonPath))
            items.Add(MenuItem("Stop using the export", () => SetAzgaar("")));

        items.Add(new ToolStripSeparator());
        items.Add(MenuItem("How to export from Azgaar…", ShowAzgaarGuide));

        return [.. items];
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
        if (_sourceShown) ShowSourceAsync().Forget("source view");
    }

    private void ApplyAzgaarChip()
    {
        string path = _options.Config.AzgaarJsonPath;
        bool loaded = !string.IsNullOrWhiteSpace(path);

        // A chip, not a button: it is on the bar to name the export in use, so with no export in
        // use it has nothing to say and the row is better off without it. File ▸ Azgaar export
        // is where you go to load one.
        _azgaar.Visible = loaded;
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

        // On the start page the walkthrough waits for the Complex card; see EnterComplex.
        if (_state.WelcomeShown || _onStart) return;
        _state.WelcomeShown = true;
        ShowWelcomeGuide();
    }

    private void ShowRecentHeightmaps()
    {
        var menu = Theme.MakeMenu();
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Items.AddRange(RecentMenuItems());
        menu.Show(_recent, new Point(0, _recent.Height));
    }

    /// <summary>
    /// Shared by the toolbar's ▾ and File ▸ Recent heightmaps. Rebuilt per opening: the list is
    /// filtered against what is on disk and against the file already in use.
    /// </summary>
    private ToolStripItem[] RecentMenuItems()
    {
        var recent = (_state.RecentHeightmaps ?? [])
            .Where(p => File.Exists(p) && !string.Equals(p, _options.HeightmapPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var items = new List<ToolStripItem>();

        if (recent.Count == 0)
            items.Add(new ToolStripMenuItem("(no other recent heightmaps)") { Enabled = false });

        foreach (string path in recent)
            items.Add(MenuItem(
                $"{Path.GetFileName(path)}   ({Clipped(Path.GetDirectoryName(path) ?? "", 44)})",
                () => SetHeightmap(path)));

        items.Add(new ToolStripSeparator());

        var forge = MenuItem("Use the Terrain workspace's pipeline", () =>
        {
            // Through the panel, so an export size CK3 is not known to render gets the same
            // question here as from the panel's own button, rather than none.
            _forge.EnsureStarted();
            _forge.RequestUseForGeneration();
            SelectWorkspace(Workspace.Terrain);
        });
        forge.Enabled = _source is not MapGen.ForgeHeightmapProvider;
        items.Add(forge);

        return [.. items];
    }

    /// <summary>Saves whatever the World canvas is showing — a map mode or the 3D frame — as a PNG.</summary>
    private void ExportView()
    {
        Bitmap? frame;
        string name;

        if (ShowingSolid)
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
        _drape.Items.Add("Terrain preview");

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

        _tips.SetToolTip(_browse, _source?.Detail ?? "The heightmap the whole mod is built from: a 16-bit PNG, or the Terrain workspace's pipeline.");

        _openMod.Enabled = ModFolderToOpen() is not null;
        SetEnabled(!_busy);

        ShowGameFolder();
    }

    private void ApplySection()
    {
        string? picked = _sections.SelectedIndex > 0 ? _sections.SelectedItem as string : null;

        bool calendar = picked == CalendarSection;
        _calendar.Visible = calendar;
        _grid.Visible = !calendar;
        if (calendar)
        {
            _calendar.RefreshPreview();
            return;
        }

        // By name rather than position: the Calendar entry sits among the sections when it is shown.
        _settingsView.Section = picked is null
            ? null
            : SettingsView.Sections.FirstOrDefault(s => SettingsView.DisplayName(s) == picked);
        RefreshSettings();
    }

    /// <summary>
    /// The sections list, owner-drawn for the room a stock ListBox will not give: a stock row is
    /// the font's height with the text against the window edge, and its selection is the same
    /// solid blue as the map mode on screen. Here the chosen section wears the pale accent.
    /// </summary>
    private void StyleSections()
    {
        _sections.DrawMode = DrawMode.OwnerDrawFixed;
        _sections.ItemHeight = 24;
        _sections.Width = 132;
        _sections.EnabledChanged += (_, _) => _sections.Invalidate();

        _sections.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool on = (e.State & DrawItemState.Selected) != 0;
            string text = _sections.Items[e.Index] as string ?? "";

            using (var back = new SolidBrush(on ? Theme.AccentSoft : Theme.Surface))
                e.Graphics.FillRectangle(back, e.Bounds);
            if (on)
            {
                using var edge = new SolidBrush(Theme.Accent);
                e.Graphics.FillRectangle(edge, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            }

            var area = Rectangle.FromLTRB(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Right - 4, e.Bounds.Bottom);
            TextRenderer.DrawText(e.Graphics, text, on ? Theme.UiBold : Theme.Ui, area,
                !_sections.Enabled ? Theme.TextDim : on ? Theme.Accent : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
    }

    /// <summary>
    /// Makes the grid re-ask the view for its rows. Reassignment rather than
    /// <see cref="PropertyGrid.Refresh"/>, which repaints the values of the rows it already has.
    /// </summary>
    private void RefreshSettings() => _grid.SelectedObject = _settingsView;

    /// <summary>
    /// Puts Calendar in the sections list, after World State, exactly while the world will have a
    /// calendar of its own: World Calendar on, and not a world of vanilla peoples, which keeps
    /// CK3's. Typed names survive the entry being hidden; they are on the config, not the panel.
    /// </summary>
    private void SyncCalendarSection()
    {
        var cfg = _options.Config;
        bool wanted = cfg.CalendarEnabled && cfg.ContentSource != MapConfig.ContentSourceMode.VanillaWorld;
        int at = _sections.Items.IndexOf(CalendarSection);

        if (wanted && at < 0)
        {
            int after = _sections.Items.IndexOf("World State");
            _sections.Items.Insert(after >= 0 ? after + 1 : _sections.Items.Count, CalendarSection);
        }
        else if (!wanted && at >= 0)
        {
            if (_sections.SelectedIndex == at) _sections.SelectedIndex = 0;
            _sections.Items.RemoveAt(at);
        }
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

        // The climate paint travels with the preset, as a PNG beside it. Written when there is
        // paint; removed when there is none, so a preset saved again after "Clear all" does not
        // bring the old strokes back the next time it is loaded.
        string sidecar = ClimateSidecar(dialog.FileName);
        string note = "";
        try
        {
            if (_climate.SavePaint(sidecar)) note = " and its climate paint";
            else if (File.Exists(sidecar)) { File.Delete(sidecar); note = " (removed its old climate paint)"; }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not write {sidecar}: {ex.Message}");
            note = " — climate paint could not be written, see log";
        }

        _status.Text = $"Saved settings to {Path.GetFileName(dialog.FileName)}{note}";
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
            _climate.InvalidateModel();
            _calendar.Bind(_options.Config);
            SyncCalendarSection();

            // The paint beside the preset replaces what is on the tab; a preset with none clears
            // it, so the preset means the same map every time it is loaded. Both are undoable.
            string sidecar = ClimateSidecar(dialog.FileName);
            string note = "";
            if (File.Exists(sidecar))
            {
                // Its own try: the settings above are already applied, and a bad sidecar should
                // say so without the status line claiming the whole preset failed.
                try
                {
                    _climate.AdoptPaint(MapGen.ClimatePaint.Load(sidecar), $"Climate paint loaded with {Path.GetFileName(dialog.FileName)}.");
                    note = " and its climate paint";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not read {sidecar}: {ex.Message}");
                    note = " — its climate paint could not be read, see log";
                }
            }
            else if (_climate.HasPaint)
            {
                _climate.ClearPaint();
                note = " — it carries no climate paint, so the painted climate was cleared (Undo in Climate restores it)";
            }

            _status.Text = $"Loaded {applied} settings from {Path.GetFileName(dialog.FileName)}{note}";
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

    private async Task OpenGeneratedWorldAsync()
    {
        if (_busy) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a generated CK3 mod to edit",
            UseDescriptionForTitle = true,
            SelectedPath = ModFolderToOpen() ?? _state.ModRoot ?? "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (_loadedWorld is not null && !ConfirmLoadedEdits()) return;
            if (_edits.HasPending)
            {
                var answer = MessageBox.Show(this, "Save your current edits before opening another world?", "Unsaved edits", MessageBoxButtons.YesNoCancel);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes) { OverwriteTitles(); if (_edits.HasPending) return; }
            }
            UseWaitCursor = true;
            _busy = true;
            SetEnabled(false);
            string path = dialog.SelectedPath;
            string gameDir = _options.GameDir;
            var world = await Task.Run(() =>
            {
                // The dropdowns for pillars, looks, doctrines and icons read the same harvest the
                // generator writes from; an opened mod has no world of its own to have built it.
                if (MapGen.VanillaVocabulary.Current is null && Core.GameLocator.IsGameDir(gameDir))
                    MapGen.VanillaVocabulary.Read(gameDir);
                return new LoadedWorldView(Core.LoadedWorld.Open(path));
            });
            UseWaitCursor = false;
            AdoptLoadedWorld(world);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open generated world",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; _busy = false; SetEnabled(true); }
    }

    private void OpenModFolder()
    {
        if (ModFolderToOpen() is not { } dir) return;

        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    /// <summary>
    /// The mod list, and what it decides written straight into <c>dlc_load.json</c>.
    ///
    /// Opened against the launcher's own mod folder rather than <see cref="_modRoot"/>, which the
    /// user is free to point somewhere else: <c>dlc_load.json</c> names its entries relative to the
    /// folder it sits beside, so that folder is the only one whose contents it can talk about.
    ///
    /// The file is the state, so it is also the source of the ticks — there is no remembered
    /// selection to drift out of step with what the game will actually load, and a list changed in
    /// the Paradox launcher shows up here on the next open.
    /// </summary>
    private void ShowModList()
    {
        string modRoot = Core.GameLocator.FindModRoot();
        string? file = Core.DlcLoad.FileForRoot(modRoot);

        if (file is null)
        {
            MessageBox.Show(this,
                $"Could not find the launcher's mod folder, so there is no dlc_load.json to edit.\n\n"
                + $"Looked in:\n{modRoot}\n\n"
                + "Starting Crusader Kings III once through the Paradox launcher creates it.",
                "No mod list", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var mods = Core.ModLibrary.InFolder(modRoot);
        var unregistered = Core.ModLibrary.Unregistered(Core.GameLocator.FindWorkshopRoot(), mods);

        if (mods.Count == 0 && unregistered.Count == 0)
        {
            MessageBox.Show(this,
                $"No mods found in:\n\n{modRoot}\n\nWrite the map first, or subscribe to something.",
                "No mods", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Matched on the folder the descriptor points at rather than on the .mod file's name, so a
        // map written under one name and listed under another still finds itself — and so a map
        // written outside this mod folder correctly finds nothing.
        string? ours = _lastModDir is null
            ? null
            : mods.FirstOrDefault(m => SamePath(m.ContentPath, _lastModDir))?.Entry;

        using var dialog = new ModListDialog(mods, unregistered, Core.DlcLoad.Enabled(file), ours);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var chosen = dialog.Selected;
        if (Core.DlcLoad.IsExactly(file, chosen))
        {
            Console.WriteLine("Mod list unchanged.");
            return;
        }

        try
        {
            Core.DlcLoad.Enable(file, chosen);

            Console.WriteLine();
            if (chosen.Count == 0)
            {
                Console.WriteLine("Every mod turned off — Crusader Kings III will start on the "
                                  + "vanilla map.");
            }
            else
            {
                Console.WriteLine($"Crusader Kings III will load {Count(chosen.Count, "mod")} on the "
                                  + "next start, in this order:");
                foreach (string entry in chosen) Console.WriteLine($"    {entry}");
                Console.WriteLine("Opening the Paradox launcher afterwards can undo this — it "
                                  + "rewrites dlc_load.json from its own playsets.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(ex);

            MessageBox.Show(this,
                $"dlc_load.json could not be updated:\n\n{ex.Message}",
                "Could not change the mod list", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Two paths naming the same folder, or false for anything unresolvable.</summary>
    private static bool SamePath(string? a, string? b)
    {
        if (a is null || b is null) return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
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
        if (_loadedWorld is not null) { SaveLoadedWorld(); return; }
        if (_busy || _source is null) return;

        // The edits are carried across the rebuild rather than lost to it: exported now, while the
        // objects they were made to still exist, and laid back over the new world after the write.
        EditOverlay? carried = null;

        if (_edits.EditedCount > 0)
        {
            var keep = MessageBox.Show(this,
                $"Writing the mod rebuilds the world from the current settings. Your "
                + $"{Count(_edits.EditedCount, "edit")} will be laid back over the new world wherever "
                + "the same title, culture, faith or ruler is generated again, and written with it. "
                + "Anything the new settings no longer produce is dropped — the log says how many.\n\n"
                + "Write the mod?",
                "Edits carry over", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

            if (keep != DialogResult.OK) return;
            carried = _edits.Export(_source.Detail);
        }

        if (!EnsureGameFolder()) return;

        using var dialog = new ModNameDialog(_modRoot, _modName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _modRoot = dialog.ModRoot;
        _modName = dialog.ModDisplayName;
        _options.ModName = dialog.ModDisplayName;

        await WriteModIntoAsync(dialog.ModDir, carried);
    }

    /// <summary>
    /// Everything Write mod does once it knows where the mod goes: build and write, then hand the
    /// world to the editor and offer to enable the mod. Split out of <see cref="WriteModAsync"/> so
    /// the Quick page, which asks for the name itself, writes through exactly the same path.
    /// </summary>
    private async Task WriteModIntoAsync(string modDir, EditOverlay? carried)
    {
        // A history this mod was last written with is written again, unless one is already in
        // hand. Discarding it removes the file, so this never brings back what the user let go of.
        if (_options.AppliedHistory is null && MapGen.AppliedHistory.Load(modDir) is { } saved)
        {
            _options.AppliedHistory = saved;
            _history.ShowApplied(saved);
            Console.WriteLine($"Applied history of {saved.Year} restored from {MapGen.AppliedHistory.FileName} beside the mod");
        }

        await BuildAsync(modDir);

        if (Directory.Exists(modDir)) _lastModDir = modDir;

        if (_result is not null && _written is not null && Directory.Exists(modDir))
        {
            // The file says what the mod on disk was written with, so it follows every write.
            if (_options.AppliedHistory is { } applied) applied.Save(modDir);
            else MapGen.AppliedHistory.Forget(modDir);

            _edits.Attach(_result, _written, modDir);
            Console.WriteLine();
            Console.WriteLine("The world can now be edited — click any title, culture or faith map.");
            RestoreEdits(carried, modDir);

            OfferToEnableMod(modDir);
        }

        ApplySource();
    }

    /// <summary>
    /// Makes the realms the History workspace has run on to the world's start. With a written mod in
    /// hand that is a re-emit — <see cref="Emit.ContentWriter.ApplyHistory"/> rewrites only the files
    /// that follow who rules what, in seconds, and the World workspace and its editor move onto the
    /// result. Without one it is the ordinary write, with the history on
    /// <see cref="Core.GenerationOptions.AppliedHistory"/>; the two produce the same files.
    /// </summary>
    private async Task ApplyHistoryAsync(MapGen.AppliedHistory applied)
    {
        if (_busy) return;

        // Re-emitted into the written mod when there is one to hand; a full write only when there is
        // not. The re-emit rewrites the files a pending edit would also be rewriting, so those go
        // first — or the edits would be half on disk and half not.
        var target = _edits.Target is { Written.World: not null } t && Directory.Exists(t.ModDir) ? t : ((GenerationResult Result, Emit.WrittenContent Written, string ModDir)?)null;
        if (target is not null && _edits.HasPending)
        {
            MessageBox.Show(this,
                $"{Count(_edits.EditedCount, "edit")} to this world {(_edits.EditedCount == 1 ? "is" : "are")} not "
                + "written yet. Press Overwrite to write them, or revert them, then apply the history.",
                "Unsaved edits", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var cfg = _options.Config;
        var answer = MessageBox.Show(this,
            $"Make the realms of {applied.Year} this world's start?\n\n"
            + (target is not null
                ? $"Only what follows from who rules what is rewritten in {target.Value.ModDir}, dated "
                  + $"{applied.Year}: title and province history, rulers and families, artifacts, bookmarks "
                  + "and the chronicle. Terrain, titles, cultures, faiths and everything else stay as written.\n\n"
                : $"The mod is written starting in {applied.Year}. Titles, cultures, faiths, development and "
                  + "wilderness stay as generated; rulers, families, governments and title history are drawn "
                  + "fresh for the new realms.\n\n")
            + $"Advancement stays at {cfg.EraYear}"
            + (cfg.EraAnchorYear <= 0 ? " — for this world it no longer follows the World Year" : "")
            + ". Change Advancement Year in the settings to move it.\n\n"
            + (cfg.UsesAdditionalBookmarks ? "Additional bookmarks are not written for an applied history yet.\n\n" : "")
            + "The settings keep their own World Year. The History workspace says which history is in use "
            + "until it is discarded there.",
            "Apply history", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

        if (answer != DialogResult.OK) return;

        _options.AppliedHistory = applied;
        _history.ShowApplied(applied);
        SelectWorkspace(Workspace.World);

        if (target is not { } written)
        {
            await WriteModAsync();
            return;
        }

        _busy = true;
        SetEnabled(false);
        _status.Text = $"Applying the history of {applied.Year}…";
        var clock = Stopwatch.StartNew();
        try
        {
            Stage.Begin();
            string gameDir = _options.GameDir;
            var (result, content) = await Task.Run(() => Emit.ContentWriter.ApplyHistory(
                written.ModDir, gameDir, written.Result, written.Written, applied));
            Stage.Report();

            // ShowResult reads _written for the History workspace, so it goes first; the editor is
            // attached again after, to the world as now written.
            _written = content;
            ShowResult(result);
            _edits.Attach(result, content, written.ModDir);
            applied.Save(written.ModDir);
            _status.Text = $"History of {applied.Year} applied to {written.ModDir} in {clock.ElapsedMilliseconds / 1000.0:F1} s";
            Console.WriteLine($"History of {applied.Year} applied in {clock.ElapsedMilliseconds / 1000.0:F1} s — "
                              + "restart the game, not just the mod, to see it");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(ex);
            _status.Text = "Applying the history failed — see log";
            MessageBox.Show(this, $"The history could not be applied:\n\n{ex.Message}",
                "Apply history", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
            SetEnabled(true);
        }
    }

    /// <summary>
    /// Back to the generated realms. The mod's files change at the next write; the record beside it
    /// goes now, so that write — in this session or after a restart — does not restore it.
    /// </summary>
    private void DiscardHistory()
    {
        _options.AppliedHistory = null;
        _history.ShowApplied(null);
        if ((_edits.Target?.ModDir ?? _lastModDir) is { } modDir && Directory.Exists(modDir))
            MapGen.AppliedHistory.Forget(modDir);
        _status.Text = "Applied history discarded — the next write uses the realms the generator grows";
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
        if (Core.DlcLoad.IsExactly(file, [entry]))
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
            Core.DlcLoad.Enable(file, [entry]);
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

    /// <summary>
    /// Lays the previous world's edits over the one just written — the ones carried through the
    /// rebuild, or failing that the overlay saved beside the mod the last time it was overwritten —
    /// and pushes them straight into the files. "Edits survive a regenerate" has to mean the mod on
    /// disk has them, not just the window; the overwrite is the same milliseconds the button costs.
    /// </summary>
    private void RestoreEdits(EditOverlay? carried, string modDir)
    {
        var overlay = carried ?? EditOverlay.Load(Path.Combine(modDir, EditOverlay.FileName));
        if (overlay is null || overlay.Count == 0) return;

        // A saved file from another heightmap is another world; its keys would land on strangers.
        if (_source is not null && overlay.Heightmap is not null && overlay.Heightmap != _source.Detail)
        {
            Console.WriteLine($"  {Count(overlay.Count, "saved edit")} belong to another heightmap "
                              + $"({overlay.Heightmap}) and were left alone");
            return;
        }

        var (applied, missed) = _edits.Import(overlay);
        string dropped = missed > 0 ? $"; {missed} had nothing to land on in this world" : "";

        if (applied == 0)
        {
            Console.WriteLine($"  edits: none carried over{dropped}");
            return;
        }

        if (_edits.Target is not { } target) return;

        try
        {
            using (new WaitCursorFor(this))
                Emit.WorldOverwrite.Apply(target.ModDir, target.Result, target.Written,
                    _edits.Pending, _options.GameDir);

            _edits.MarkWritten();
            SaveEdits(modDir);
            Console.WriteLine($"  edits: {applied} carried over and written into the mod{dropped}");
        }
        catch (Exception ex)
        {
            // Left pending: the bar offers Overwrite, and the edits are in the window either way.
            Console.WriteLine();
            Console.WriteLine(ex);
            Console.WriteLine($"  edits: {applied} carried over but not written — use Overwrite{dropped}");
        }
    }

    /// <summary>
    /// Keeps the overlay beside the mod, current with what is on disk, so writing the same mod again
    /// in a later session gets its edits back. Removed when nothing is edited any more.
    /// </summary>
    private void SaveEdits(string modDir)
    {
        string path = Path.Combine(modDir, EditOverlay.FileName);

        if (_edits.EditedCount == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        _edits.Export(_source?.Detail).Save(path);
    }

    private void OverwriteTitles()
    {
        if (_loadedWorld is not null) { SaveLoadedWorld(); return; }
        if (_busy || !_edits.HasPending || _edits.Target is not { } target) return;

        var aspects = _edits.Pending;
        int edited = _edits.EditedCount;

        try
        {
            using (new WaitCursorFor(this))
                Emit.WorldOverwrite.Apply(target.ModDir, target.Result, target.Written, aspects,
                    _options.GameDir);

            _edits.MarkWritten();
            SaveEdits(target.ModDir);
            Emit.WorldOverwrite.Report(aspects, edited, target.ModDir, target.Written);
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

            // The Quick page shows the world filling in; it gets a copy of its own to keep.
            if (_quickRunning) _quick.OfferLiveImage(viewName, new Bitmap(bitmap));

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
        // A Quick world skips the question: its page already says the run takes a few minutes.
        if (_source is MapGen.ForgeHeightmapProvider && !_onQuick && !_forge.ConfirmStaleBakes(this)) return;

        // Read on the UI thread before the run, and cloned: the tab stays enabled for panning but
        // the paint must not change under the model mid-run. A Quick world's climate is the one its
        // page chose, so paint left on the Climate workspace does not reach it.
        var climatePaint = _onQuick ? null : _climate.EffectivePaint?.Clone();

        var (result, cancelled) = await RunAsync(
            modDir is null ? "Building preview…" : "Writing mod…",
            () =>
            {
                // A history applied in the History workspace moves the start date, on a copy: the
                // grid keeps the user's own World Year, and discarding the history needs no undo.
                var cfg = _options.AppliedHistory is { } applied
                    ? _options.Config.AtStartYear(applied.Year)
                    : _options.Config;
                if (_options.AppliedHistory is not null)
                    Console.WriteLine($"Applied history: written as {cfg.StartYear}, as advanced as {cfg.EraYear}"
                        + (_options.Config.UsesAdditionalBookmarks ? "; additional bookmarks are not written for it" : ""));
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

                var r = Generator.FromTerrain(terra, cfg, OnProgressivePreview,
                    climatePaint: climatePaint);

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
        ShowProcessedAsync(result).Forget("processed preview");

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

        // A run is watched from World, whichever workspace it was started from (F5 works in all
        // three), and its log opens for the duration. It folds again afterwards unless the run
        // failed — then the log is the thing worth reading. A run started from the Quick page is
        // watched there instead, so it stays put.
        if (!_onQuick) SelectWorkspace(Workspace.World);
        bool logWasOpen = _logOpen;
        bool failed = false;
        SetLogOpen(true);

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
        ConsoleFork.Install();
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
            _lastRun = RunOutcome.Completed;
            return (result, false);
        }
        catch (OperationCanceledException)
        {
            _lastRun = RunOutcome.Cancelled;
            Console.WriteLine();
            Console.WriteLine($"Cancelled after {clock.ElapsedMilliseconds / 1000.0:F1} s");
            if (modDir is not null) RunLog.Write(modDir, _options, "cancelled — the mod folder may be half written");
            return (null, true);
        }
        catch (Exception ex)
        {
            _lastRun = RunOutcome.Failed;
            _lastRunError = ex.Message;
            Console.WriteLine();
            Console.WriteLine(ex);
            _status.Text = "Failed — see log";
            failed = true;
            if (modDir is not null) RunLog.Write(modDir, _options, $"failed: {ex.Message}");
            return (null, false);
        }
        finally
        {
            if (!failed && !logWasOpen) SetLogOpen(false);

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

        // The Quick page watches the same estimate. An uncalibrated one has no fraction to give.
        if (_quickRunning)
        {
            bool calibrated = remaining is not null && _progress.Style == ProgressBarStyle.Blocks;
            _quick.SetProgress(calibrated ? fraction : null,
                calibrated ? RunProgress.Describe(remaining!.Value) : null,
                _progressModel.Elapsed, _phase);
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
        _climate.Enabled = enabled;
        ShowPending();

        // A Climate tab opened mid-run was told to wait; the run is over.
        if (enabled && _workspace == Workspace.Climate && _climateStamp is null)
            ShowClimateAsync().Forget("climate view");

        bool ready = enabled && _source is not null;
        _writeMod.Enabled = ready;
        _preview.Enabled = ready;
        if (_loadedWorld is not null) ConfigureLoadedControls(enabled);
    }

    private void OnStageEntered(string name) => Post(() =>
    {
        if (!_busy) return;

        _progressModel?.Enter(name);
        _phase = Sentence(name);
        _status.Text = $"{_phase}…";
        if (_quickRunning) _quick.EnterStage(name);
        ShowProgress();
    });

    private void OnStageDetail(string name) => Post(() =>
    {
        if (_busy && _phase is not null) _status.Text = $"{_phase} · {name.Trim(' ', '·')}…";
        if (_quickRunning) _quick.EnterStage(name);
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

        // After BuildAsync has set _written, so a write hands History its world and a preview
        // takes the previous one away.
        _history.Attach(result, _written);
        _calendar.ShowGenerated(_written?.Calendar);

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
            // An opened mod's graph is read from its title history when it is opened, and lives
            // with the view; a generated one is built lazily from the last write.
            if (_loadedWorld is not null) return _loadedWorld.Realm;

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
        if (_loadedWorld is not null) { SelectLoadedView(name); return; }
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
        if (switched && !_busy && _result is not null)
        {
            if (mode.Pick is { } pick)
            {
                _status.Text = pick.Kind switch
                {
                    MapPick.Culture => "Click a county to inspect and edit its culture",
                    MapPick.Faith => "Click a county to inspect and edit its faith",
                    MapPick.Realm => "Click a realm to focus it · Ctrl+click jumps to a county · Esc steps back",
                    MapPick.Dynasty => "Click a county to inspect its holder's dynasty and house",
                    _ => $"Click a {TierWord(pick.Tier)} to inspect and edit it",
                };
            }
            else if (mode.Estimate && _written is null)
            {
                _status.Text = $"{mode.Name} — estimated from the current world. " +
                               "Write the mod to see what it ships.";
            }
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
        "h" => "hegemony",
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
            button.Enabled = _loadedWorld is null || MapModes.All.Any(m => m.Category == key && Available(m));
            // The pale accent, one step quieter than the mode under it: the category only says which
            // row of modes is showing, and the mode is the thing actually on the map.
            bool on = key == _category;
            button.BackColor = on ? Theme.AccentSoft : Theme.Surface;
            button.ForeColor = on ? Theme.Accent : Theme.Text;
            button.FlatAppearance.MouseOverBackColor = on ? Theme.AccentSoft : Theme.SurfaceHigh;
        }

        _modeStrip.SuspendLayout();
        _modeStrip.Controls.Clear();

        foreach (var mode in MapModes.All)
        {
            if (mode.Category != _category) continue;

            var button = _viewButtons[mode.Name];
            bool on = mode.Name == _view;
            bool available = Available(mode);
            button.Enabled = _loadedWorld is null || available;

            button.BackColor = on ? Theme.Accent : available ? Theme.SurfaceHigh : Theme.Surface;
            button.ForeColor = on ? Theme.AccentText : available ? Theme.Text : Theme.TextDim;
            button.FlatAppearance.MouseOverBackColor = on ? Theme.Accent : Theme.Border;

            _tips.SetToolTip(button, !available
                ? _loadedWorld is not null ? "This simulation layer was not saved in the mod; it cannot be reconstructed without regenerating." : "Shows written content — available after Write mod"
                : mode.Estimate && _written is null
                    ? "Estimated from the current world — write the mod to see what it actually ships"
                    : mode.Pick?.Kind switch
                    {
                        MapPick.Realm => "Click a realm to focus and drill into it — Ctrl+click for the county",
                        not null => "Click the map in this mode to inspect and edit",
                        null => null,
                    });

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

        // An estimate mode before a write paints a recomputation of what a write would decide, so
        // it says so beside its own key. In the amber rather than the dim grey the legend labels
        // use: the point of the line is that it is not one of them.
        bool estimate = mode.Estimate && _written is null && _loadedWorld is null;
        if (estimate)
        {
            _legendBar.Controls.Add(new Label
            {
                Text = "   estimate — recomputed, not read from a written mod",
                AutoSize = true,
                Font = Theme.Ui,
                ForeColor = Theme.NoticeText,
                Margin = new Padding(12, 3, 0, 0),
            });
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

        _legendBar.Visible = mode.Legend is not null || breadcrumb || estimate;
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
        if (_busy) return;
        if (MapModes.Find(_view)?.Pick is not { } view) return;

        // The barony under the cursor, from whichever world is showing. Everything after this —
        // the walk up to the view's tier, the realm drill, the inspectors — is the same for an
        // opened mod as for a generated world, because both hand over Title objects.
        MapGen.Title? barony;
        if (_loadedWorld is { } loaded)
        {
            barony = loaded.BaronyAt(pixel);
            if (barony is null)
            {
                _status.Text = "Nothing there — that is water or impassable";
                return;
            }
        }
        else
        {
            if (_edits.Target is null || _result is null) return;

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

            barony = MapGen.Titles.Flatten(_result.Titles)
                .FirstOrDefault(t => t.Tier == "b" && t.ProvinceId == id);
            if (barony is null) return;
        }

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

            // The county's direct holder, whose inspector carries the dynasty and house.
            case MapPick.Dynasty when _loadedWorld is not null:
                if (Realm is { } loadedGraph && _loadedWorld.HolderOf(loadedGraph.Primary(loadedGraph.SeatOfCounty(title))) is { } loadedHolder)
                {
                    Inspect([loadedHolder]);
                    _status.Text = $"{loadedHolder.Name} — {title.Name}";
                }
                else _status.Text = $"Nobody holds {title.Name}";
                break;

            case MapPick.Dynasty:
                if (Realm is { } graph && _written?.Rulers is { } rulers && rulers.TryGet(graph.SeatOfCounty(title), out var holder))
                {
                    Inspect([holder]);
                    _status.Text = $"{holder.Name} — {title.Name}";
                }
                else _status.Text = $"Nobody holds {title.Name}";
                break;

            case MapPick.Culture when _loadedWorld is not null:
                if (_loadedWorld.CultureOf(title) is { } loadedCulture)
                {
                    Inspect([loadedCulture]);
                    _status.Text = $"Culture {loadedCulture.Name} — {title.Name}";
                }
                break;

            case MapPick.Faith when _loadedWorld is not null:
                if (_loadedWorld.FaithOf(title) is { } loadedFaith)
                {
                    Inspect([loadedFaith]);
                    _status.Text = $"Faith {loadedFaith.Name} — {title.Name}";
                }
                break;

            case MapPick.Culture:
                var culture = _edits.Target!.Value.Written.Cultures.For(title);
                Inspect([culture]);
                _status.Text = $"Culture {culture.Name} — {title.Name}";
                break;

            case MapPick.Faith:
                var faith = _edits.Target!.Value.Written.Faiths.For(title);
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

        // An opened mod's things arrive as Title shells from the tree and the map, or as entries
        // from a link; either way the inspector window is picked by what kind of thing it is, so
        // a county opens in the same Title window it would in a generated world.
        IReadOnlyList<Core.WorldEntry>? loaded = null;
        Type kind;
        if (_loadedWorld is not null)
        {
            loaded = _loadedWorld.EntriesOf(targets);
            if (loaded.Count == 0) return;
            kind = loaded[0].Kind switch
            {
                "Culture" => typeof(MapGen.Culture),
                "Faith" or "Religion" => typeof(MapGen.Faith),
                "Character" => typeof(MapGen.Ruler),
                _ => typeof(MapGen.Title),
            };
        }
        else kind = targets[0].GetType();

        if (!_inspectors.TryGetValue(kind, out var inspector) || inspector.IsDisposed)
        {
            inspector = kind.Name switch
            {
                nameof(MapGen.Culture) => new CultureInspector(_edits),
                nameof(MapGen.Faith) => new FaithInspector(_edits),
                nameof(MapGen.Ruler) => new RulerInspector(_edits),
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
        if (inspector is RulerInspector rulers) rulers.Realm = Realm;

        if (loaded is not null) inspector.InspectLoaded(loaded, _loadedWorld!, LoadedEditsChanged);
        else inspector.Inspect(targets);

        if (!inspector.Visible) inspector.Show(this);
        inspector.BringToFront();
    }

    private void ShowPending()
    {
        if (_loadedWorld is not null) { ShowLoadedPending(); return; }
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
        if (_loadedWorld is not null) { _loadedWorld.World.Revert(); LoadedEditsChanged(); return; }
        if (_edits.EditedCount == 0) return;

        var answer = MessageBox.Show(this,
            "Put everything edited — titles, cultures, faiths, rulers and governments — back to how "
            + "it was generated?",
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
        if (_loadedWorld is not null)
        {
            string under = pixel is { } loadedPixel ? _loadedWorld.Probe(_view, loadedPixel) : "";
            _readout.Text = under.Length > 0 ? $"{under}   ·   {zoom * 100:F0}%" : $"{_view}   {zoom * 100:F0}%";
            return;
        }
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
