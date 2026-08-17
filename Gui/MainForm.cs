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
    private readonly Button _launchGame = Theme.MakeButton("Launch CK3", 100);
    private readonly Button _gameFolder = Theme.MakeButton("Game folder…", 104);


    /// <summary>Carries the resolved game folder, which is far too long to sit on a button.</summary>
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
        HideSelection = false, // Keeps the match highlighted when the search box is focused
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
    /// <param name="Render">
    /// Takes the write's capture as well as the run, because the landed-realm view is drawn from
    /// something only a write produces. Null for every other view, and for Realms before a write.
    /// </param>
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

    /// <summary>What clicking a view selects.</summary>
    private enum Pick { Title, Culture, Faith }

    /// <summary>
    /// The views a click selects something on, and the ones an edit can make stale.
    ///
    /// Every one paints land by a colour that is editable, so its cached render has to be dropped
    /// when that colour moves. The tier is the one the view draws — clicking the Duchies map picks
    /// a duchy — and is the county for anything looked up per county.
    /// </summary>
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

    /// <summary>Whether an edit of this kind changes what a view paints.</summary>
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

    /// <summary>
    /// The edit session over the mod last written, shared by the tree and the inspector.
    ///
    /// Gated on a write rather than on a preview because a title has no name until one is written —
    /// see <see cref="TitleEditor"/>. <see cref="ShowResult"/> detaches it on every finished run, so
    /// a preview after a write correctly locks it again; <see cref="WriteModAsync"/> is the only
    /// thing that ever attaches it.
    /// </summary>
    private readonly WorldEdits _edits = new();

    private readonly TitleEditor _titles;

    /// <summary>Hidden until something is pending. See <see cref="BuildPendingBar"/>.</summary>
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
    /// The last heightmap decoded from disk, kept so previewing a settings change does not pay to
    /// decode the image again. Only the decode is cached — see <see cref="MapGen.HeightmapImage"/>
    /// for why nothing derived from it may be.
    /// </summary>
    private MapGen.HeightmapImage? _loaded;

    /// <summary>
    /// What the last build found wrong with the heightmap. Set on the worker thread and read on the
    /// UI thread once it has finished, which is the same handoff <see cref="_loaded"/> uses.
    /// </summary>
    private IReadOnlyList<MapGen.HeightmapWarning> _warnings = [];

    /// <summary>
    /// What the last write produced, for the title editor. Null after a preview, which writes
    /// nothing there is anything to edit in. Same worker-to-UI handoff as <see cref="_warnings"/>.
    /// </summary>
    private Emit.WrittenContent? _written;

    private string? _heightmapPath;
    private bool _busy;
    private CancellationTokenSource? _cancellation;

    /// <summary>Where mod folders are created, and what the next one is called by default.</summary>
    private string _modRoot = "";
    private string _modName = GenerationOptions.DefaultModName;

    /// <summary>The last mod folder written this session, or the one written last session.</summary>
    private string? _lastModDir;

    public MainForm(GenerationOptions options)
    {
        _options = options;

        // A heightmap named on the command line is still the chosen one when the window opens.
        // Without this, `--heightmap x.png --gui` came up with both buttons greyed out and no
        // indication why. Failing that, the one from last session, if it is still there.
        _heightmapPath = options.HeightmapPath
            ?? (File.Exists(_state.HeightmapPath) ? _state.HeightmapPath : null);
        options.HeightmapPath = _heightmapPath;

        // A folder picked by hand last session beats whatever the search would turn up, but only
        // while it is still a game folder — an install that has since moved must not pin the tool to
        // where it used to be. See LocateFolders, which reports whichever won.
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

        Theme.ApplyLight(_grid);
        _grid.SelectedObject = _options.Config;

        // 1. Randomize the seed on program startup
        _options.Config.Seed = Random.Shared.Next(1, int.MaxValue);

        // 2. Assign the randomized seed to the UI field
        _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
        _seed.ValueChanged += (_, _) => _options.Config.Seed = (int)_seed.Value;

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
        bar.Controls.Add(_launchGame);
        bar.Controls.Add(_gameFolder);
        bar.Controls.Add(_sourceName);

        return bar;
    }

    /// <summary>
    /// The unsaved-changes bar: absent entirely until an edit is pending, then impossible to miss.
    ///
    /// This replaces an Overwrite button that lived permanently on the toolbar and spent almost all
    /// of its life greyed out — a disabled button in a row of a dozen is furniture, and nothing
    /// about it said whether there was anything to press it for. A strip that does not exist until
    /// there is something to do costs no chrome and states the count.
    ///
    /// Revert all lives here too rather than in the Titles tab, where it was before. It is a global
    /// action over the whole edit session — titles, cultures and faiths — and it belongs next to
    /// the other one, not inside one of the surfaces that feeds it.
    /// </summary>
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

        // The tabs wrap only the map pane, not the whole right-hand side, so the log stays visible
        // under both of them — an overwrite reports into it, and watching that happen is the only
        // confirmation the mod on disk actually changed.
        var tabs = Theme.MakeTabs();

        var mapTab = new TabPage("Map") { BackColor = Theme.Background };
        mapTab.Controls.Add(viewer);

        var titleTab = new TabPage("Titles") { BackColor = Theme.Background };
        titleTab.Controls.Add(_titles);

        tabs.TabPages.Add(mapTab);
        tabs.TabPages.Add(titleTab);

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

    private Control BuildLogHeader()
    {
        var clear = Theme.MakeButton("Clear", 60);
        clear.Click += (_, _) => _log.Clear();

        var copy = Theme.MakeButton("Copy", 60);
        copy.Click += (_, _) =>
        {
            if (_log.TextLength > 0) Clipboard.SetText(_log.Text);
        };

        // Trigger search on every keypress
        _logSearch.TextChanged += (_, _) => SearchLog(next: false);

        // Find the next match when pressing Enter
        _logSearch.KeyDown += (sender, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // Stop the beep sound on Enter
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
            // Advance past the current match if we are finding the next instance
            start += _log.SelectionLength > 0 ? 1 : 0;
        }
        else
        {
            start = 0;
        }

        int index = _log.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);

        // Wrap around to the beginning if no match was found from the current cursor position
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

        ReportFolders();
    }

    /// <summary>
    /// Says where the game and the mod folder were found, in the log, on every launch.
    ///
    /// The search itself has already run — <see cref="GenerationOptions.GameDir"/> is set from
    /// <see cref="Core.GameLocator"/> when the options are constructed, and the mod root in this
    /// window's constructor — so this is the report rather than the search. It is worth printing
    /// even when everything is found: the first thing a wrong answer looks like is a run that fails
    /// three phases in, and the first thing anyone asks is which install it was reading.
    /// </summary>
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

    /// <summary>Keeps the game-folder button's tooltip and colour honest about what it points at.</summary>
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

    /// <summary>
    /// Points the tool at a CK3 install by hand, for when the search came back empty or came back
    /// with the wrong one of two installs.
    /// </summary>
    private void PickGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "The 'game' folder of your Crusader Kings III install",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_options.GameDir) ? _options.GameDir : "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // Normalize, not the raw pick: the folder people recognise is the one named after the game,
        // and the one this needs is 'game' inside it.
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

    /// <summary>
    /// Blocks a write that is going to fail, and offers the fix rather than only the diagnosis.
    /// </summary>
    /// <returns>Whether the game folder is now usable.</returns>
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

        // Only a game folder that is really one, so a bad hand-picked path cannot outlive the
        // session that set it and pre-empt the search next launch.
        _state.GameDir = Core.GameLocator.IsGameDir(_options.GameDir) ? _options.GameDir : null;
        _state.ModRoot = _modRoot;
        _state.ModName = _modName;
        _state.LastModDir = _lastModDir;
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

        _openMod.Enabled = ModFolderToOpen() is not null;
        SetEnabled(!_busy);

        // After SetEnabled, which repaints buttons from their enabled state and would otherwise
        // take the warning colour off the game-folder button every time a run finished.
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

    /// <summary>
    /// The mod folder written last, or failing that the folder mods live in.
    ///
    /// The second case is not a consolation prize: before anything has been written, the useful
    /// thing to open is the launcher's mod folder, which is also the quickest way to see whether
    /// the tool has found the right one.
    /// </summary>
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

    /// <summary>
    /// Launches Crusader Kings III directly, bypassing the launcher to load the game quickly.
    /// </summary>
    private void LaunchGame()
    {
        if (string.IsNullOrWhiteSpace(_options.GameDir) || !Core.GameLocator.IsGameDir(_options.GameDir))
        {
            MessageBox.Show(this,
                "Please configure a valid game folder before launching Crusader Kings III.",
                "Game folder not configured", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // The game directory points to '...\Crusader Kings III\game'. 
        // We look for '...\Crusader Kings III\binaries\ck3.exe'.
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

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                WorkingDirectory = Path.Combine(gameRoot, "binaries"),
                UseShellExecute = true
            });
            _status.Text = "Crusader Kings III launched";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to launch CK3: {ex.Message}");
            MessageBox.Show(this,
                $"An error occurred while launching Crusader Kings III:\n\n{ex.Message}",
                "Launch Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Asks what the mod is called and where it goes, then writes it.
    ///
    /// The name is asked for every write rather than once and remembered, because writing twice in
    /// one session is normally two different maps — a seed rolled, a setting changed — and the old
    /// behaviour of always writing into a folder called <c>proceduralmap</c> meant the second one
    /// quietly ate the first. The box comes up filled with the last name used, so agreeing to
    /// overwrite is still one keypress.
    /// </summary>
    private async Task WriteModAsync()
    {
        if (_busy || _heightmapPath is null) return;

        // A wider test than the preview's, and deliberately. Writing regenerates the world and
        // rewrites every file an edit could have reached, so edits already pushed with Overwrite
        // are destroyed on disk too — being saved is no protection here, which is the opposite of
        // what "saved" usually implies and so worth saying out loud.
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

        // Before the dialog rather than after: being asked to name a mod and only then told the
        // game is missing is two dead ends where one would do.
        if (!EnsureGameFolder()) return;

        using var dialog = new ModNameDialog(_modRoot, _modName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _modRoot = dialog.ModRoot;
        _modName = dialog.ModDisplayName;
        _options.ModName = dialog.ModDisplayName;

        string modDir = dialog.ModDir;
        await BuildAsync(modDir);

        // Only if it is actually there. A cancelled or failed write must not leave the button
        // pointing at a folder that was never created.
        if (Directory.Exists(modDir)) _lastModDir = modDir;

        // After BuildAsync, not inside it: ShowResult unloads the editor on every finished run,
        // including this one, so loading any earlier would be undone a moment later.
        if (_result is not null && _written is not null && Directory.Exists(modDir))
        {
            _edits.Attach(_result, _written, modDir);
            Console.WriteLine();
            Console.WriteLine("The world can now be edited — click any title, culture or faith map.");
        }

        ApplySource();
    }

    /// <summary>
    /// Pushes renamed titles back into the mod on disk.
    ///
    /// Runs on the UI thread rather than through <see cref="RunAsync"/>: this is two localisation
    /// files, which is milliseconds even at vanilla size, and borrowing the progress machinery for
    /// it would put a bar and an ETA on screen for less time than they take to appear.
    /// </summary>
    private void OverwriteTitles()
    {
        if (_busy || !_edits.HasPending || _edits.Target is not { } target) return;

        var aspects = _edits.Pending;
        int edited = _edits.EditedCount;

        try
        {
            using (new WaitCursorFor(this))
                Emit.WorldOverwrite.Apply(target.ModDir, target.Result, target.Written, aspects);

            // Only on success: a throw must leave the edits pending so they can be tried again.
            _edits.MarkWritten();
            Emit.WorldOverwrite.Report(aspects, edited, target.ModDir);
            _status.Text = $"Edits written to {target.ModDir}";
        }
        catch (Exception ex)
        {
            // Same rule as a failed run: the message is worth more in the log next to what caused
            // it than in a dialog that takes the window with it.
            Console.WriteLine();
            Console.WriteLine(ex);
            _status.Text = "Overwrite failed — see log";

            MessageBox.Show(this,
                $"The mod could not be updated:\n\n{ex.Message}",
                "Overwrite failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
    /// <summary>
    /// Previews, having first checked that nothing unsaved is about to be thrown away.
    ///
    /// The check lives here rather than in <see cref="BuildAsync"/> so the write path can ask its
    /// own, differently worded question *before* the mod naming dialog rather than after it.
    /// </summary>
    private async Task PreviewAsync()
    {
        // Only what is genuinely at risk. A preview never touches the mod folder, so edits that
        // have already been overwritten survive it on disk — asking about those would be crying
        // wolf, and a confirmation people learn to click through protects nothing.
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

                    // Every build, not just a fresh decode: these depend on the height settings, so
                    // changing one and rebuilding has to change what they say.
                    _warnings = MapGen.HeightmapSource.Diagnose(_loaded, cfg);
                });

                var terra = Stage.Time("province elevation",
                    () => MapGen.TerrainData.FromElevation(_loaded!.ToElevation(cfg), cfg));

                var r = Generator.FromTerrain(terra, cfg);

                // Set on the worker and read on the UI thread once it has finished, the same
                // handoff _loaded and _warnings use. Cleared first so a failed write cannot leave
                // the previous run's capture behind for the title editor to load.
                _written = null;
                if (modDir is not null) _written = Generator.WriteMod(r, _options, modDir);
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

        ShowHeightmapWarnings(modDir);
    }

    /// <summary>
    /// Puts the import diagnostics in front of the user rather than in the log.
    ///
    /// These are the faults that produce a map which loads, generates without error, and is wrong
    /// in game — an ocean rendered as open ground, a continent that is one plateau with a cliff at
    /// every shore. Each one was already printed, and printing was measurably not enough: the log
    /// scrolls past during a build nobody is watching, and the reports that reached us were of a
    /// broken map rather than of a warning ignored.
    ///
    /// Shown after the preview is on screen, so the map behind the dialog is the map being
    /// complained about, and the title says whether anything was written.
    /// </summary>
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
        _gameFolder.Enabled = enabled;
        _launchGame.Enabled = enabled;
        _cancel.Enabled = !enabled;

        // The editor holds the titles a run is about to rebuild, so it must not be edited while
        // one is in flight. It gates itself on having a written mod on top of this.
        _titles.Enabled = enabled;
        ShowPending();

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
    /// <returns>Whether it was actually queued. Callers holding a "already pending" flag need to
    /// know, or a post that never lands leaves the flag set and suppresses every later one.</returns>
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
            // The window went away between the check above and the post. Nothing to update.
            return false;
        }
    }

    // --- Views ----------------------------------------------------------------------------------

    /// <summary>Takes a finished run and drops every cached render of the previous one.</summary>
    private void ShowResult(GenerationResult result)
    {
        _result = result;

        // Whatever the editor was holding belongs to a run that is no longer on screen, and its
        // titles may not even be named — a preview leaves every one of them blank. WriteModAsync
        // loads it again immediately afterwards on the one path where there is a mod to edit.
        _edits.Detach();

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
            using (new WaitCursorFor(this)) bitmap = ToBitmap(render(_result, _written));
            _rendered[name] = bitmap;
        }

        _viewer.SetImage(bitmap);
        ShowReadout(_viewer.Zoom, null);
    }

    /// <summary>
    /// Turns a click on the preview into the title that was clicked.
    ///
    /// Four hops: undo the downsample to get a source pixel, read the province label there, map the
    /// label through the write order to a province id, and walk up from that barony to the tier the
    /// current view draws. Only meaningful on a view that draws titles at all — clicking the
    /// climate map selects nothing, which is right.
    /// </summary>
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
            // Water, or the impassable land above the last barony. Neither has anything on it.
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
                // Through the tree, so both surfaces agree on what is selected and the tree scrolls
                // to show it. Reveal raises SelectionChanged, which is what opens the inspector.
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

    /// <summary>
    /// The inspectors, one per kind of thing, built on first use and reused thereafter.
    ///
    /// Keyed by type rather than by instance: a window per object becomes window soup within a
    /// minute, and a single window that navigated between kinds would lose the thing the split is
    /// for — having a county and the culture living in it open at once.
    /// </summary>
    private readonly Dictionary<Type, InspectorForm> _inspectors = [];

    /// <summary>Shows the right inspector for whatever is selected, creating it the first time.</summary>
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

    /// <summary>
    /// Keeps the unsaved-changes bar honest, and hides it the moment there is nothing to say.
    /// </summary>
    private void ShowPending()
    {
        _pendingBar.Visible = _edits.HasPending;
        if (!_edits.HasPending) return;

        int edited = _edits.EditedCount;

        // Zero edited with something still pending is the revert-after-overwrite case: the objects
        // match what was generated again, but the files on disk are still holding the edit.
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

    /// <summary>Routes a navigation from one inspector to the one that owns that kind.</summary>
    private void Inspect(object target) => Inspect([target]);

    /// <summary>
    /// Opens the inspector beside the window rather than on top of it, falling back to inside it
    /// where there is no room — the map is the thing being clicked and covering it defeats the
    /// point of a separate window.
    /// </summary>
    private void PlaceInspector(Form inspector)
    {
        var screen = Screen.FromControl(this).WorkingArea;
        int right = Bounds.Right + 8;

        inspector.Location = right + inspector.Width <= screen.Right
            ? new Point(right, Bounds.Top + 80)
            : new Point(Math.Max(screen.Left, Bounds.Right - inspector.Width - 24), Bounds.Top + 80);
    }

    /// <summary>
    /// Keeps the window in step with the edit session: the Overwrite button, and the cached renders
    /// that a recolour has just made wrong.
    /// </summary>
    private void OnEditsChanged(Emit.WorldAspect touched)
    {
        ShowPending();

        if (_redrawQueued || !ClickableViews.Values.Any(v => Repaints(v.Kind, touched))) return;

        // Deferred rather than done here, and coalesced. This is raised from inside a PropertyGrid
        // value commit — freeing the bitmap under the viewer and spending a full-map render while
        // the grid is still mid-edit is asking for trouble — and one user action can raise it more
        // than once, which would otherwise queue a redundant render of forty million pixels.
        // Accumulated rather than replaced: a second edit before the post lands must not narrow
        // what the first one invalidated.
        _staleAspects |= touched;
        _redrawQueued = Post(RedrawTitleViews);
    }

    private bool _redrawQueued;
    private Emit.WorldAspect _staleAspects;

    /// <summary>Drops the renders an edit has invalidated and rebuilds whichever is on screen.</summary>
    private void RedrawTitleViews()
    {
        _redrawQueued = false;

        var stale = _staleAspects;
        _staleAspects = Emit.WorldAspect.None;

        bool showing = ClickableViews.TryGetValue(_view, out var current)
                       && Repaints(current.Kind, stale);

        // Detached before anything is disposed, and this order is not optional. The viewer is
        // almost certainly holding one of these bitmaps, and a Bitmap freed out from under it does
        // not fault where it was freed — it throws GDI+ "Parameter is not valid" out of the next
        // paint or mouse move, in a stack that has nothing to do with colours. ShowResult clears
        // the viewer before emptying the same cache, for exactly this reason.
        if (showing) _viewer.SetImage(null);

        // Only the views this edit actually made wrong, and only those already rendered. A title
        // recolour has no bearing on the culture map, and dropping it would cost a needless
        // full-map pass the next time that button was pressed.
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
