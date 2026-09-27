using System.ComponentModel;
using Ck3MapGen.Config;
using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The Azgaar importer: bring the two files, override anything you want different, name the mod —
/// then the same run and finished world as Quick.
///
/// Everything Complex asks of an import is either answered here in words or answered for you.
/// The two files can be chosen or dropped anywhere on the page, and whichever comes first finds
/// its partner if it is lying beside it under Azgaar's own naming. As soon as both are in, the
/// page draws the world as it will be imported — the relief, coloured by the export's countries —
/// and says whether the two halves describe the same map, which is the one mistake that would
/// otherwise cost a whole run to discover. The size CK3 needs is fitted without asking, and the
/// height scale every Azgaar map wants is simply used.
///
/// The middle step is overrides, not options: every setting on it starts on "Azgaar", the export's
/// own answer, and says what that answer is. A world made without touching it is the export's.
///
/// Like <see cref="QuickPage"/>, it decides nothing about generation: it hands
/// <see cref="Choices"/> to the main window, which fills in the ordinary settings and runs the
/// ordinary write. That is why "Customize in Complex" hands over the same world.
/// </summary>
internal sealed class AzgaarPage : Panel
{
    public event Action? BackToStart;
    public event Action? CreateRequested;
    public event Action? CancelRequested;
    public event Action? LaunchRequested;
    public event Action? OpenFolderRequested;
    public event Action? CustomizeRequested;
    public event Action? GameFolderRequested;
    public event Action? GuideRequested;

    private const int FilesStep = 0, OptionsStep = 1, ReviewStep = 2;
    private static readonly string[] StepNames = ["Files", "Overrides", "Review"];

    private enum Mode { Steps, Running, Done }

    private AzgaarChoices _choices = new();
    private int _step;
    private int _reached;
    private Mode _mode;
    private bool _gameFound;
    private string _modRoot = "";
    private bool _nameTouched;
    private bool _settingName;

    // what is known about the two files
    private AzgaarImageSummary? _image;
    private AzgaarExportSummary? _export;
    private string? _imageError;
    private string? _exportError;
    private bool _readingExport;
    private Io.AzgaarWorld? _drawnWorld;
    private string? _drawnImage;
    private MapGen.AzgaarRaster.Alignment? _alignment;
    private AzgaarFiles.Picture? _picture;
    private bool _drawing;
    private CancellationTokenSource? _exportCts;
    private CancellationTokenSource? _drawCts;

    // ---- chrome
    private readonly Panel _header = new() { Dock = DockStyle.Top, BackColor = Theme.Background };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, BackColor = Theme.Background };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Stepper _stepper = new(StepNames);
    private readonly TextLink _toStart = new() { Name = "azgaarToStart", Text = "Start", Glyph = "" };
    private readonly TextLink _howTo = new() { Name = "azgaarHowTo", Text = "How to export", Glyph = "" };
    private readonly PillButton _back = new() { Name = "azgaarBack", Text = "Back", Kind = PillKind.Secondary, Glyph = "" };
    private readonly PillButton _next = new() { Name = "azgaarNext", Text = "Next", Kind = PillKind.Primary, Glyph = "", GlyphAfter = true, MinWidth = 120 };
    private readonly Label _stepCount = MakeLabel("", Small, Theme.TextDim);

    // ---- steps
    private readonly StepPanel _filesPanel = new();
    private readonly StepPanel _optionsPanel = new();
    private readonly StepPanel _reviewPanel = new();

    // files step
    private readonly FileSlot _imageSlot = new() { Name = "azgaarImage", Glyph = "", Text = "Heightmap image", AccessibleName = "Heightmap image" };
    private readonly FileSlot _exportSlot = new() { Name = "azgaarExport", Glyph = "", Text = "Full JSON export", AccessibleName = "Full JSON export" };
    private readonly Label _pairing = MakeLabel("", Body, Theme.TextDim, wrap: true);
    private readonly MapPreview _preview = new();
    private readonly TallyRow _counts = new() { Name = "azgaarCounts", ColumnCount = 5 };
    private readonly Label _worldLine = MakeLabel("", Small, Theme.TextDim, wrap: true);
    private readonly Label _helpTitle = MakeLabel("New to this?", Strong, Theme.Text);
    private readonly Label _helpText = MakeLabel(
        "In Azgaar, set the canvas to 1920 × 960 and apply the CK3 style, then export the PNG at 4–5× and the Full JSON from the same view.",
        Small, Theme.TextDim, wrap: true);
    private readonly TextLink _guide = new() { Name = "azgaarGuide", Text = "Step-by-step guide", LinkFont = Small, Backdrop = Theme.Surface, Glyph = "" };
    private readonly TextLink _openAzgaar = new() { Name = "azgaarOpenSite", Text = "Open Azgaar", LinkFont = Small, Backdrop = Theme.Surface, Glyph = "" };

    // overrides step: every group's first card is "Azgaar", the export's own answer
    private readonly ChoiceGroup<int> _advancement;
    private readonly ChoiceGroup<QuickDensity?> _density;
    private readonly ChoiceGroup<bool> _wilderness;
    private readonly ChoiceGroup<bool> _races;
    private readonly ChoiceCard _advancementAzgaar;
    private readonly ChoiceCard _densityAzgaar;
    private readonly ChoiceCard _racesAzgaar;
    private readonly ToolTip _tips = new() { InitialDelay = 400, AutoPopDelay = 15000 };

    // review step
    private readonly List<(Label Key, Label Value, TextLink Change)> _summary = [];
    private readonly MapPreview _reviewMap = new();
    private readonly Label _nameCaption = MakeLabel("Mod name", Strong, Theme.Text);
    private readonly TextBox _name = new() { BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11f), Name = "azgaarModName" };
    private readonly PathText _path = new() { Font = new Font("Consolas", 8.5f) };
    private readonly Label _nameNote = MakeLabel("", Small, Theme.TextDim, wrap: true);
    private readonly TextLink _changeFolder = new() { Name = "azgaarChangeFolder", Text = "Change folder…", LinkFont = Small };
    private readonly Label _gameLine = MakeLabel("", Small, Theme.TextDim, wrap: true);
    private readonly TextLink _gameFix = new() { Name = "azgaarGameFolder", Text = "Set game folder…", LinkFont = Small };
    private readonly Label _complexNote = MakeLabel("", Small, Theme.TextDim, wrap: true);

    // the run and the finished world, shared with the Quick page
    private readonly RunScreen _run = new("azgaar") { AnotherText = "Import another" };

    public AzgaarPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        Name = "azgaarPage";
        AllowDrop = true;

        _advancement = new ChoiceGroup<int>("Advancement", "How far along its cultures are. The calendar stays the export's own.", 0)
            .Add(0, "Azgaar", "Read from how its peoples live", "azgaarEraAzgaar")
            .Add(867, "Early medieval", "Like 867 · tribes and young kingdoms", "azgaarEraEarly")
            .Add(1066, "High medieval", "Like 1066 · feudal realms at their height", "azgaarEraHigh")
            .Add(1178, "Late medieval", "Like 1178 · old dynasties, rich courts", "azgaarEraLate");
        _density = new ChoiceGroup<QuickDensity?>("Provinces", "How many baronies each of the export's provinces is cut into.", null)
            .Add(null, "Azgaar", "Each province one county", "azgaarDensityAzgaar")
            .Add(QuickDensity.Fewer, "Fewer, larger", "Bigger baronies · faster to make", "azgaarDensityFewer")
            .Add(QuickDensity.Balanced, "Balanced", "The generator's own default", "azgaarDensityBalanced")
            .Add(QuickDensity.More, "Many, smaller", "Vanilla-sized baronies · slower", "azgaarDensityMore");
        _wilderness = new ChoiceGroup<bool>("Unclaimed land", "Land no country holds.", true)
            .Add(true, "Azgaar", "Starts wild, to clear, claim and settle", "azgaarWildAzgaar")
            .Add(false, "Settled", "Held by counts like everywhere else", "azgaarWildSettled");
        _races = new ChoiceGroup<bool>("Peoples", "How they look.", true)
            .Add(true, "Azgaar", "As the export tags them", "azgaarRacesAzgaar")
            .Add(false, "Humans only", "Every people is human", "azgaarRacesHuman");
        _advancementAzgaar = _advancement.Cards.First();
        _densityAzgaar = _density.Cards.First();
        _racesAzgaar = _races.Cards.First();

        _advancement.Changed += v => _choices.Advancement = v;
        _density.Changed += v => _choices.Density = v;
        _wilderness.Changed += v => _choices.Wilderness = v;
        _races.Changed += v => _choices.FantasyRaces = v;

        BuildChrome();
        BuildFilesStep();
        BuildOptionsStep();
        BuildReviewStep();
        WireRunScreen();

        foreach (var panel in (StepPanel[])[_filesPanel, _optionsPanel, _reviewPanel])
            _body.Controls.Add(panel);
        _body.Controls.Add(_run);

        Controls.Add(_body);
        Controls.Add(_footer);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(_header);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        _footer.SendToBack();
    }

    private int S(int logical) => LaunchUi.S(this, logical);

    // ================================================================ public surface

    /// <summary>What the page has chosen, as a copy the caller can keep.</summary>
    public AzgaarChoices Choices => _choices.Clone();

    /// <summary>Whether the export tags any people with a race we can draw.</summary>
    public bool HasRaceTags => _export?.Races.Count > 0;

    /// <summary>The size the heightmap is resampled to on the way in, or null to build it as it is.</summary>
    public (int Width, int Height)? ImageFit => _image?.Fit;

    public string ModRoot => _modRoot;
    public string ModName => _name.Text.Trim();
    public string ModDir => Path.Combine(_modRoot, ModNameDialog.FolderName(_name.Text));

    /// <summary>The run and done views; the main window reports the run to it directly.</summary>
    public RunScreen Run => _run;

    /// <summary>The same page size as Quick, so moving between launcher pages never resizes the window.</summary>
    public Size PreferredPageSize => new(S(StepPanel.PreferredColumn) + 2 * S(32), S(60) + 1 + S(606) + 1 + S(68));

    /// <summary>
    /// Opens the page on its first step with the files used last time and every override on Azgaar.
    /// Files that have since moved or vanished are simply left empty.
    /// </summary>
    public void Begin(AzgaarChoices? remembered, bool gameFound, string gameDir, string modRoot, string? complexNote)
    {
        _choices = new AzgaarChoices
        {
            HeightmapPath = remembered?.HeightmapPath ?? "",
            ExportPath = remembered?.ExportPath ?? "",
        };
        _picture = null;
        _modRoot = modRoot;
        _nameTouched = false;
        _complexNote.Text = complexNote ?? "";
        SetGameFolder(gameFound, gameDir);

        _image = null;
        _imageError = null;
        _export = null;
        _exportError = null;
        _alignment = null;
        _drawnImage = null;
        _drawnWorld = null;
        _preview.Image = null;
        _reviewMap.Image = null;

        string image = File.Exists(_choices.HeightmapPath) ? _choices.HeightmapPath : "";
        string export = File.Exists(_choices.ExportPath) ? _choices.ExportPath : "";
        _choices.HeightmapPath = "";
        _choices.ExportPath = "";

        SyncControls();
        Go(FilesStep, resetReached: true);
        if (image.Length > 0) SetImage(image, findPartner: false);
        if (export.Length > 0) SetExport(export, findPartner: false);
        UpdateFiles();
    }

    public void SetGameFolder(bool found, string gameDir)
    {
        _gameFound = found;
        _run.SetGameFound(found);
        _gameLine.Text = found
            ? $"✓  Crusader Kings III found at {gameDir}"
            : "Crusader Kings III was not found. It is needed to write the mod.";
        _gameLine.ForeColor = found ? Good : Theme.Danger;
        _gameFix.Visible = !found;
        UpdateNameState();
        _reviewPanel.PerformLayout();
    }

    /// <summary>Opens the file dialog for the export, as the guide's "Choose export…" does.</summary>
    public void ChooseExport() => PickFile(image: false);

    public void ShowRunning()
    {
        _mode = Mode.Running;
        var built = _image is null ? ((int, int)?)null : _image.Fit ?? (_image.Width, _image.Height);
        string size = built is var (bw, bh) ? $" · {bw} × {bh}" : "";
        _run.ShowRunning($"Importing {WorldName}",
            $"From Azgaar{size}. This takes a few minutes.",
            _preview.Image is { } img ? new Bitmap(img) : null,
            "As drawn in Azgaar",
            "Peoples, faiths, armies and treasures appear here as the world is made. The land comes first.");
        ShowView(_run);
        UpdateChrome();
    }

    public void ShowDone(string modDir, TimeSpan took)
    {
        _mode = Mode.Done;
        _run.ShowDone(ModName, modDir, took);
        ShowView(_run);
        UpdateChrome();
    }

    public void ShowFailed(bool cancelled, string? message)
    {
        _mode = Mode.Done;
        _run.ShowFailed(cancelled, message);
        ShowView(_run);
        UpdateChrome();
    }

    private void WireRunScreen()
    {
        _run.CancelRequested += () => CancelRequested?.Invoke();
        _run.LaunchRequested += () => LaunchRequested?.Invoke();
        _run.OpenFolderRequested += () => OpenFolderRequested?.Invoke();
        _run.CustomizeRequested += () => CustomizeRequested?.Invoke();
        _run.RetryRequested += () => Go(ReviewStep);
        _run.AnotherRequested += () =>
        {
            _nameTouched = false;
            Go(FilesStep, resetReached: true);
        };
    }

    private string WorldName => _export is { MapName.Length: > 0 } e ? e.MapName
        : _image is { } i ? Path.GetFileNameWithoutExtension(i.Path) : "your world";

    // ================================================================ chrome

    private void BuildChrome()
    {
        _header.Height = S(60);
        _footer.Height = S(68);

        _header.Controls.AddRange([_toStart, _stepper, _howTo]);
        _footer.Controls.AddRange([_back, _stepCount, _next]);
        _header.Layout += (_, _) => LayoutHeader();
        _footer.Layout += (_, _) => LayoutFooter();

        _toStart.Click += (_, _) => { if (_mode != Mode.Running) BackToStart?.Invoke(); };
        _howTo.Click += (_, _) => GuideRequested?.Invoke();
        _stepper.StepClicked += i => { if (i <= FilesStep || FilesReady) Go(i); };
        _back.Click += (_, _) => { if (_step > 0) Go(_step - 1); else BackToStart?.Invoke(); };
        _next.Click += (_, _) =>
        {
            if (_step == FilesStep && !FilesReady) return;
            if (_step < ReviewStep) Go(_step + 1);
            else if (CanCreate) CreateRequested?.Invoke();
        };
    }

    private void LayoutHeader()
    {
        var (x, w) = StepPanel.Column(_header);
        int h = _header.Height;
        _toStart.Location = new Point(x - S(4), (h - _toStart.Height) / 2);
        _howTo.Location = new Point(x + w - _howTo.Width, (h - _howTo.Height) / 2);
        int stepW = Math.Min(_stepper.NaturalWidth + S(8), w - _toStart.Width - _howTo.Width - S(24));
        _stepper.Bounds = new Rectangle((_header.Width - stepW) / 2, (h - S(40)) / 2, stepW, S(40));
    }

    private void LayoutFooter()
    {
        var (x, w) = StepPanel.Column(_footer);
        int h = _footer.Height;
        _back.Location = new Point(x, (h - _back.Height) / 2);
        _next.Location = new Point(x + w - _next.Width, (h - _next.Height) / 2);
        _stepCount.Location = new Point((_footer.Width - _stepCount.PreferredWidth) / 2, (h - _stepCount.PreferredHeight) / 2);
    }

    private void UpdateChrome()
    {
        bool steps = _mode == Mode.Steps;
        _footer.Visible = steps;
        _howTo.Visible = steps;
        _toStart.Enabled = _mode != Mode.Running;
        _stepper.Set(steps ? _step : ReviewStep, FilesReady ? _reached : FilesStep, locked: !steps);

        _back.Text = _step == FilesStep ? "Start" : "Back";
        _next.Text = _step switch
        {
            FilesStep => "Next: Overrides",
            OptionsStep => "Next: Review",
            _ => "Create my world",
        };
        _next.Glyph = _step == ReviewStep ? "" : "";
        _next.GlyphAfter = _step != ReviewStep;
        _next.MinWidth = _step == ReviewStep ? 170 : 120;
        _next.FitWidth();
        _next.Enabled = _step switch
        {
            FilesStep => FilesReady,
            ReviewStep => CanCreate,
            _ => true,
        };
        _stepCount.Text = $"Step {_step + 1} of {StepNames.Length}";
        _footer.PerformLayout();
        _header.PerformLayout();
    }

    private void Go(int step, bool resetReached = false)
    {
        if (step > FilesStep && !FilesReady) step = FilesStep;
        _mode = Mode.Steps;
        _run.Reset();
        _step = Math.Clamp(step, 0, ReviewStep);
        _reached = resetReached ? _step : Math.Max(_reached, _step);

        if (_step == ReviewStep) RefreshReview();

        ShowView(_step switch
        {
            FilesStep => _filesPanel,
            OptionsStep => _optionsPanel,
            _ => _reviewPanel,
        });
        UpdateChrome();
        _next.Focus();
    }

    private void ShowView(Control view)
    {
        SuspendLayout();
        foreach (Control c in _body.Controls) c.Visible = ReferenceEquals(c, view);
        ResumeLayout();
        view.PerformLayout();
    }

    private void SyncControls()
    {
        _advancement.Value = _choices.Advancement;
        _density.Value = _choices.Density;
        _wilderness.Value = _choices.Wilderness;
        _races.Value = _choices.FantasyRaces;
        SyncOverrides();
    }

    /// <summary>
    /// Each "Azgaar" card says what the export's answer actually is, once there are files to read
    /// it from — and the province scale that answer comes to is worked out here.
    /// </summary>
    private void SyncOverrides()
    {
        _advancementAzgaar.Subtitle = _export?.Advancement is { } reading
            ? $"Like {reading.Year} · read from its peoples"
            : "Read from how its peoples live";
        _tips.SetToolTip(_advancementAzgaar, _export?.Advancement is { } why
            ? $"{Math.Round(why.Settled * 100):F0}% of people settled rather than tribal, "
              + $"{Math.Round(why.Organized * 100):F0}% in organised faiths, "
              + $"{Math.Round(why.Walled * 100):F0}% of townsfolk behind walls."
            : null);

        if (ProvinceReading() is { } fit)
        {
            _choices.AzgaarCountyScale = fit.Scale;
            _densityAzgaar.Subtitle = $"A barony per town · ~{fit.Baronies:N0} baronies";
            _tips.SetToolTip(_densityAzgaar,
                $"Each of the export's {_export!.Provinces} provinces becomes one county, with a barony for each of its towns "
                + $"({_export.Burgs:N0} in all). Thinly-settled provinces get {fit.FromCap:N0} more so no barony passes "
                + $"{new MapConfig().AzgaarMaxBaronyArea:0.#}× vanilla's size"
                + (fit.Split > 0 ? $"; the {fit.Split} largest then hold more than a county can and are split." : "."));
        }
        else
        {
            _densityAzgaar.Subtitle = "Each province one county";
            _tips.SetToolTip(_densityAzgaar, null);
        }

        _racesAzgaar.Subtitle = _export is null ? "As the export tags them"
            : HasRaceTags ? (_export.Races.Count > 2 ? $"{_export.Races[0]}, {_export.Races[1].ToLowerInvariant()} and more" : string.Join(" and ", _export.Races))
            : "It tags no races, so all human";

        foreach (var card in (ChoiceCard[])[_advancementAzgaar, _densityAzgaar, _racesAzgaar]) card.Invalidate();
    }

    /// <summary>What "Azgaar" comes to for provinces, once the land and the province count are both known.</summary>
    private AzgaarFiles.ProvinceFit? ProvinceReading()
    {
        if (_image is not { } image || _export is not { } export || _picture is not { } picture) return null;
        var built = image.Fit ?? (image.Width, image.Height);
        return AzgaarFiles.ProvinceScale(picture, built, export.World, new MapConfig().AzgaarMaxBaronyArea);
    }

    /// <summary>The advancement as the summary says it.</summary>
    private string AdvancementLine()
    {
        if (_choices.Advancement > 0)
            return $"{EraNames.GetValueOrDefault(_choices.Advancement, "Custom")}  ·  like {_choices.Advancement}";
        return _export?.Advancement is { } reading ? $"Azgaar  ·  like {reading.Year}" : "Azgaar";
    }

    private string ProvincesLine()
        => _choices.Density is { } density
            ? DensityNames[density]
            : ProvinceReading() is { } p ? $"Azgaar  ·  a barony per town  ·  ~{p.Baronies:N0} baronies" : "Azgaar";

    // ================================================================ files step

    private void BuildFilesStep()
    {
        var title = MakeLabel("Bring your Azgaar map", Title, Theme.Text);
        var subtitle = MakeLabel("Two files from the same map: the heightmap image and the Full JSON export. Drop them anywhere on this page.",
            Subtitle, Theme.TextDim);
        _filesPanel.Controls.AddRange([title, subtitle, _imageSlot, _exportSlot, _pairing, _preview, _counts, _worldLine,
            _helpTitle, _helpText, _guide, _openAzgaar]);

        _imageSlot.Click += (_, _) => PickFile(image: true);
        _exportSlot.Click += (_, _) => PickFile(image: false);
        _guide.Click += (_, _) => GuideRequested?.Invoke();
        _openAzgaar.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://azgaar.github.io/Fantasy-Map-Generator/") { UseShellExecute = true });
            }
            catch (Exception) { }
        };

        _filesPanel.Arrange = panel =>
        {
            var (x, w) = StepPanel.Column(panel);
            int y = S(18);
            title.Location = new Point(x - S(2), y);
            y += title.PreferredHeight + S(2);
            subtitle.Location = new Point(x, y);
            y += subtitle.PreferredHeight + S(18);

            // The files on the left, the world as it will be imported on the right.
            int leftW = Math.Clamp(w * 35 / 100, S(330), S(440)), gap = S(24);
            int rightX = x + leftW + gap, rightW = x + w - rightX;
            int top = y;

            // The preview as large as the column allows, but short enough to leave the counts and
            // the world line under it on screen; they take the preview's width.
            int countsH = _counts.PreferredGridHeight;
            int worldH = StepPanel.Wrapped(_worldLine, rightW);
            var map = StepPanel.Map(rightX, top, rightW,
                Math.Max(S(120), panel.ClientSize.Height - top - (countsH > 0 ? countsH + S(24) : S(12)) - worldH - S(12)));
            int previewH = map.Height;
            rightW = map.Width;

            int slotH = S(92);
            _imageSlot.Bounds = new Rectangle(x, y, leftW, slotH);
            y += slotH + S(12);
            _exportSlot.Bounds = new Rectangle(x, y, leftW, slotH);
            y += slotH + S(14);
            _pairing.Bounds = new Rectangle(x + S(2), y, leftW - S(4), StepPanel.Wrapped(_pairing, leftW - S(4)));

            // The help card sits at the foot of the column, level with the bottom of the counts.
            int countsTop = top + previewH + S(14);
            int columnFoot = countsH > 0 ? countsTop + countsH : top + previewH;
            int pad = S(14);
            int textW = leftW - 2 * pad;
            int helpH = pad + _helpTitle.PreferredHeight + S(4) + StepPanel.Wrapped(_helpText, textW) + S(6) + _guide.Height + pad - S(4);
            int helpY = Math.Max(_pairing.Bottom + S(12), columnFoot - helpH);
            panel.Cards.Add(new Rectangle(x, helpY, leftW, helpH));
            int hy = helpY + pad;
            _helpTitle.Location = new Point(x + pad, hy);
            hy += _helpTitle.PreferredHeight + S(4);
            _helpText.Bounds = new Rectangle(x + pad, hy, textW, StepPanel.Wrapped(_helpText, textW));
            hy += _helpText.Height + S(6);
            _guide.Location = new Point(x + pad - S(2), hy);
            _openAzgaar.Location = new Point(_guide.Right + S(14), hy);

            _preview.Bounds = map;
            _counts.Bounds = new Rectangle(rightX, countsTop, rightW, countsH);
            int ly = countsH > 0 ? countsTop + countsH + S(10) : top + previewH + S(12);
            _worldLine.Bounds = new Rectangle(rightX, ly, rightW, StepPanel.Wrapped(_worldLine, rightW));
        };
    }

    private void PickFile(bool image)
    {
        string current = image ? _choices.HeightmapPath : _choices.ExportPath;
        string other = image ? _choices.ExportPath : _choices.HeightmapPath;
        string dir = !string.IsNullOrEmpty(current) ? Path.GetDirectoryName(current) ?? ""
                   : !string.IsNullOrEmpty(other) ? Path.GetDirectoryName(other) ?? ""
                   : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { } home && Directory.Exists(Path.Combine(home, "Downloads"))
                       ? Path.Combine(home, "Downloads") : "";

        using var dialog = new OpenFileDialog
        {
            Title = image ? "Choose the heightmap image exported from Azgaar" : "Choose Azgaar's Full JSON export",
            Filter = image ? "Heightmap image (*.png)|*.png|All files (*.*)|*.*" : "Azgaar Full export (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = dir,
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        if (image) SetImage(dialog.FileName, findPartner: true);
        else SetExport(dialog.FileName, findPartner: true);
    }

    private void SetImage(string path, bool findPartner)
    {
        _choices.HeightmapPath = path;
        try
        {
            _image = AzgaarImageSummary.Measure(path);
            _imageError = null;
        }
        catch (Exception ex)
        {
            _image = null;
            _imageError = $"Could not read this image: {ex.Message}";
        }

        if (findPartner && _image is not null && _export is null && _exportError is null && AzgaarFiles.FindPartner(path) is { } partner)
            SetExport(partner, findPartner: false, found: true);

        UpdateFiles();
        Redraw();
    }

    private void SetExport(string path, bool findPartner, bool found = false)
    {
        _choices.ExportPath = path;
        _export = null;
        _exportError = null;
        _readingExport = true;
        _exportFound = found;
        UpdateFiles();

        _exportCts?.Cancel();
        var cts = _exportCts = new CancellationTokenSource();

        Task.Run(() => AzgaarExportSummary.Load(path)).ContinueWith(task =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                if (cts.IsCancellationRequested) return;
                _readingExport = false;
                if (task.IsFaulted)
                {
                    _exportError = task.Exception?.GetBaseException().Message ?? "This file could not be read.";
                }
                else
                {
                    _export = task.Result;
                    if (findPartner && _image is null && _imageError is null && AzgaarFiles.FindPartner(path) is { } partner)
                        SetImage(partner, findPartner: false);
                }

                SyncOverrides();
                SuggestName();
                UpdateFiles();
                Redraw();
            });
        });
    }

    private bool _exportFound;

    /// <summary>
    /// Draws the pair as it stands, off the UI thread. A newer request cancels an older one; the
    /// same pair is not drawn twice.
    /// </summary>
    private void Redraw()
    {
        string? imagePath = _image?.Path;
        var world = _export?.World;
        if (imagePath == _drawnImage && ReferenceEquals(world, _drawnWorld) && _preview.Image is not null) return;

        _drawCts?.Cancel();
        _alignment = null;
        if (imagePath is null && world is null)
        {
            _preview.Image = null;
            _preview.Busy = null;
            _preview.Chip = null;
            _drawnImage = null;
            _drawnWorld = null;
            UpdateFiles();
            return;
        }

        var cts = _drawCts = new CancellationTokenSource();
        _drawing = true;
        _preview.Busy = "Drawing…";
        UpdateFiles();

        Task.Run(() => AzgaarFiles.Draw(imagePath, world, 1024, 512, cts.Token), cts.Token).ContinueWith(task =>
        {
            if (IsDisposed || !IsHandleCreated) { if (task.IsCompletedSuccessfully) task.Result?.Image.Dispose(); return; }
            BeginInvoke(() =>
            {
                if (cts.IsCancellationRequested) { if (task.IsCompletedSuccessfully) task.Result?.Image.Dispose(); return; }
                _drawing = false;
                _preview.Busy = task.IsCompletedSuccessfully && task.Result is not null ? null : "Could not draw this map";
                if (task.IsFaulted && imagePath is not null)
                    _imageError = $"Could not read this image: {task.Exception?.GetBaseException().Message}";
                if (task.IsCompletedSuccessfully && task.Result is { } picture)
                {
                    _preview.Image = picture.Image;
                    _reviewMap.Image = new Bitmap(picture.Image);
                    _alignment = picture.Alignment;
                    _picture = picture;
                    SyncOverrides();
                    _drawnImage = imagePath;
                    _drawnWorld = world;
                }
                UpdateFiles();
            });
        }, TaskContinuationOptions.None);
    }

    /// <summary>Both files read, drawn, and agreeing with each other.</summary>
    private bool FilesReady
        => _image is not null && _export is not null && !_drawing && _alignment is { LooksAligned: true };

    /// <summary>Brings the slots, the pairing line, the counts and the chip up to date.</summary>
    private void UpdateFiles()
    {
        // The image slot.
        if (_imageError is not null)
            _imageSlot.Set(FileSlot.State.Error, Path.GetFileName(_choices.HeightmapPath), _imageError);
        else if (_image is { } image)
            _imageSlot.Set(FileSlot.State.Ok, Path.GetFileName(image.Path),
                image.SizeLine + (image.OddShape ? "  ·  not 2:1, so the map will look stretched" : ""));
        else
            _imageSlot.Set(FileSlot.State.Empty, null, "The .png exported from Azgaar. Choose it, or drop it here.");

        // The export slot.
        if (_readingExport)
            _exportSlot.Set(FileSlot.State.Busy, Path.GetFileName(_choices.ExportPath), "Reading the export…");
        else if (_exportError is not null)
            _exportSlot.Set(FileSlot.State.Error, Path.GetFileName(_choices.ExportPath), _exportError);
        else if (_export is { } export)
            _exportSlot.Set(FileSlot.State.Ok, Path.GetFileName(export.Path),
                (export.MapName.Length > 0 ? $"“{export.MapName}” · " : "") + $"{export.States} countries"
                + (_exportFound ? "  ·  found beside it" : ""));
        else
            _exportSlot.Set(FileSlot.State.Empty, null, "Menu ▸ Export ▸ Full, in Azgaar. Choose it, or drop it here.");

        // Whether the pair agrees.
        if (_image is null || _export is null)
        {
            _pairing.Text = _image is null && _export is null
                ? "Both files are needed: the image for the land, the export for everything on it."
                : _image is null ? "Now the heightmap image from the same map." : "Now the Full JSON export from the same map.";
            _pairing.ForeColor = Theme.TextDim;
        }
        else if (_drawing || _alignment is null)
        {
            _pairing.Text = "Checking that the two files match…";
            _pairing.ForeColor = Theme.TextDim;
        }
        else if (_alignment.LooksAligned)
        {
            _pairing.Text = $"✓  The two files match: {100 * _alignment.LandAgreement:F0}% of the map agrees on land and sea.";
            _pairing.ForeColor = Good;
        }
        else
        {
            _pairing.Text = $"These files don't describe the same map — only {100 * _alignment.LandAgreement:F0}% agrees on land and sea. "
                          + "Export both from the same unzoomed view of the same map, with the CK3 style applied.";
            _pairing.ForeColor = Theme.Danger;
        }

        // What the export holds.
        if (_export is { } e)
        {
            _counts.Set(
            [
                ("Countries", e.States), ("Provinces", e.Provinces), ("Towns", e.Burgs),
                ("Cultures", e.Cultures), ("Religions", e.Religions),
            ]);
            var parts = new List<string>();
            if (e.CalendarLine is { } calendar) parts.Add($"{calendar} — the game starts then.");
            if (e.Races.Count > 0) parts.Add($"Peoples tagged as {string.Join(", ", e.Races).ToLowerInvariant()}.");
            _worldLine.Text = string.Join("  ", parts);
            _preview.Chip = e.MapName.Length > 0 ? e.MapName : null;
        }
        else
        {
            _counts.Set([]);
            _worldLine.Text = "";
            _preview.Chip = _image is not null ? "Relief only, until the export is in" : null;
        }

        if (_mode == Mode.Steps) UpdateChrome();
        _filesPanel.PerformLayout();
    }

    // ---- drag and drop, anywhere on the page

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effect = _mode == Mode.Steps && DroppedFiles(e).Any() ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (_mode != Mode.Steps) return;
        var files = DroppedFiles(e).ToList();
        string? image = files.FirstOrDefault(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        string? export = files.FirstOrDefault(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        if (image is null && export is null) return;

        if (_step != FilesStep) Go(FilesStep);
        if (image is not null) SetImage(image, findPartner: export is null);
        if (export is not null) SetExport(export, findPartner: image is null);
    }

    private readonly HashSet<Control> _dropTargets = [];

    private static IEnumerable<string> DroppedFiles(DragEventArgs e)
        => (e.Data?.GetData(DataFormats.FileDrop) as string[] ?? [])
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        AcceptDropsOn(e.Control);
    }

    /// <summary>
    /// A drop lands on whatever child is under the cursor, not the page, so every child takes drops
    /// and hands them up.
    /// </summary>
    private void AcceptDropsOn(Control? control)
    {
        // Once per control: the run screen moves its discoveries column between its two views,
        // and every move is another ControlAdded.
        if (control is null || control is TextBox || !_dropTargets.Add(control)) return;
        control.AllowDrop = true;
        control.DragEnter += (_, e) => OnDragEnter(e);
        control.DragDrop += (_, e) => OnDragDrop(e);
        control.ControlAdded += (_, e) => AcceptDropsOn(e.Control);
        foreach (Control child in control.Controls) AcceptDropsOn(child);
    }

    // ================================================================ overrides step

    private void BuildOptionsStep()
    {
        var titleLabel = MakeLabel("Anything to change?", Title, Theme.Text);
        var subtitleLabel = MakeLabel("Everything starts as your map has it. Pick something else only where you want the world to differ.",
            Subtitle, Theme.TextDim);
        _optionsPanel.Controls.AddRange([titleLabel, subtitleLabel]);

        (Label Title, Label Hint, List<ChoiceCard> Cards) Group(IChoiceGroup view)
        {
            var t = MakeLabel(view.Title, GroupTitle, Theme.Text);
            var h = MakeLabel(view.Hint, Small, Theme.TextDim);
            var cards = view.Cards.ToList();
            _optionsPanel.Controls.Add(t);
            _optionsPanel.Controls.Add(h);
            foreach (var card in cards) _optionsPanel.Controls.Add(card);
            return (t, h, cards);
        }

        var advancement = Group(_advancement);
        var density = Group(_density);
        var wilderness = Group(_wilderness);
        var races = Group(_races);

        _optionsPanel.Arrange = p =>
        {
            var (x, w) = StepPanel.Column(p);
            int y = S(18);
            titleLabel.Location = new Point(x - S(2), y);
            y += titleLabel.PreferredHeight + S(2);
            subtitleLabel.Location = new Point(x, y);
            y += subtitleLabel.PreferredHeight + S(18);

            int cardH = S(62), gap = S(12);

            // One group across a span of the column; returns the height it took.
            int Place((Label Title, Label Hint, List<ChoiceCard> Cards) g, int gx, int gw, int gy)
            {
                g.Title.Location = new Point(gx, gy);
                g.Hint.Location = new Point(gx + g.Title.PreferredWidth + S(10), gy + (g.Title.PreferredHeight - g.Hint.PreferredHeight) / 2 + S(1));
                g.Hint.MaximumSize = new Size(Math.Max(1, gx + gw - g.Hint.Left), 0);
                int cy = gy + g.Title.PreferredHeight + S(6);
                int n = Math.Max(1, g.Cards.Count);
                int cw = (gw - gap * (n - 1)) / n;
                for (int i = 0; i < g.Cards.Count; i++)
                    g.Cards[i].Bounds = new Rectangle(gx + i * (cw + gap), cy, i == n - 1 ? gw - i * (cw + gap) : cw, cardH);
                return cy - gy + cardH;
            }

            y += Place(advancement, x, w, y) + S(18);
            y += Place(density, x, w, y) + S(18);

            // The two two-way choices share a row, each in half the column.
            int half = (w - S(24)) / 2;
            Place(wilderness, x, half, y);
            Place(races, x + half + S(24), w - half - S(24), y);
        };
    }

    // ================================================================ review

    private static readonly Dictionary<int, string> EraNames = new()
        { [867] = "Early medieval", [1066] = "High medieval", [1178] = "Late medieval" };
    private static readonly Dictionary<QuickDensity, string> DensityNames = new()
        { [QuickDensity.Fewer] = "Fewer, larger baronies", [QuickDensity.Balanced] = "Balanced", [QuickDensity.More] = "Many, smaller baronies" };
    private static readonly (string Key, int Step)[] SummaryRows =
    [
        ("World", FilesStep), ("Heightmap", FilesStep), ("Calendar", FilesStep),
        ("Advancement", OptionsStep), ("Provinces", OptionsStep), ("Unclaimed land", OptionsStep), ("Peoples", OptionsStep),
    ];

    private string[] SummaryValues()
    {
        var e = _export;
        return
        [
            e is null ? "—" : $"{(e.MapName.Length > 0 ? e.MapName : "Unnamed")}  ·  {e.States} countries  ·  {e.Cultures} cultures",
            _image?.SizeLine ?? "—",
            e?.CalendarLine ?? "No year in the export",
            AdvancementLine(),
            ProvincesLine(),
            _choices.Wilderness ? "Azgaar  ·  starts wild" : "Settled",
            !_choices.FantasyRaces ? "Humans only"
                : HasRaceTags ? $"Azgaar  ·  {string.Join(", ", e!.Races).ToLowerInvariant()}" : "Azgaar  ·  all human",
        ];
    }

    private void BuildReviewStep()
    {
        var title = MakeLabel("Ready to import", Title, Theme.Text);
        var subtitle = MakeLabel("Check the choices, name the mod, and create it. You can change anything first.", Subtitle, Theme.TextDim);
        _reviewPanel.Controls.AddRange([title, subtitle, _reviewMap, _nameCaption, _name, _path, _nameNote, _changeFolder, _gameLine, _gameFix, _complexNote]);

        foreach (var (key, step) in SummaryRows)
        {
            var k = MakeLabel(key, Body, Theme.TextDim);
            var v = MakeLabel("", Strong, Theme.Text);
            var change = new TextLink
            {
                Text = "Change", LinkFont = Small, Backdrop = Theme.Surface,
                Name = "azgaarChange-" + key.Replace(' ', '-'),
                AccessibleName = $"Change {key.ToLowerInvariant()}",
            };
            int target = step;
            change.Click += (_, _) => Go(target);
            _summary.Add((k, v, change));
            _reviewPanel.Controls.AddRange([k, v, change]);
        }

        _name.TextChanged += (_, _) =>
        {
            if (!_settingName) _nameTouched = true;
            UpdateNameState();
        };
        _changeFolder.Click += (_, _) => PickModRoot();
        _gameFix.Click += (_, _) => GameFolderRequested?.Invoke();

        _reviewPanel.Arrange = panel =>
        {
            var (x, w) = StepPanel.Column(panel);
            int y = S(18);
            title.Location = new Point(x - S(2), y);
            y += title.PreferredHeight + S(2);
            subtitle.Location = new Point(x, y);
            y += subtitle.PreferredHeight + S(18);

            int gap = S(28);
            int leftW = (w - gap) * 52 / 100;
            int rightX = x + leftW + gap, rightW = x + w - rightX;

            int rowH = S(38);
            int cardPad = S(16);
            panel.Cards.Add(new Rectangle(x, y, leftW, cardPad * 2 + rowH * _summary.Count));
            int ry = y + cardPad;
            for (int i = 0; i < _summary.Count; i++)
            {
                var (k, v, change) = _summary[i];
                int cy = ry + i * rowH;
                k.Location = new Point(x + cardPad, cy + (rowH - k.PreferredHeight) / 2);
                change.Location = new Point(x + leftW - cardPad - change.Width, cy + (rowH - change.Height) / 2);
                int vx = x + cardPad + S(110);
                v.AutoSize = false;
                v.AutoEllipsis = true;
                v.Bounds = new Rectangle(vx, cy + (rowH - v.PreferredHeight) / 2, change.Left - vx - S(8), v.PreferredHeight);
                if (i > 0) panel.Rules.Add(new Rectangle(x + cardPad, cy, leftW - 2 * cardPad, 1));
            }

            int ty = y;
            var map = StepPanel.Map(rightX, ty, rightW, Math.Max(S(120), panel.ClientSize.Height - ty - ReviewFootHeight(rightW)));
            _reviewMap.Bounds = map;
            _reviewMap.Chip = _export is { MapName.Length: > 0 } e ? e.MapName : null;
            ty += map.Height + S(18);
            _nameCaption.Location = new Point(rightX, ty);
            ty += _nameCaption.PreferredHeight + S(4);
            _name.Bounds = new Rectangle(rightX, ty, rightW, _name.PreferredHeight);
            ty += _name.Height + S(6);
            _path.Bounds = new Rectangle(rightX, ty, rightW, TextRenderer.MeasureText("Ag", _path.Font).Height + S(2));
            ty += _path.Height + S(2);
            int noteW = rightW - _changeFolder.Width - S(8);
            _nameNote.Bounds = new Rectangle(rightX, ty, noteW, StepPanel.Wrapped(_nameNote, noteW));
            _changeFolder.Location = new Point(rightX + rightW - _changeFolder.Width, ty - S(4));
            ty += Math.Max(_nameNote.Height, _changeFolder.Height) + S(14);
            int gameW = rightW - (_gameFix.Visible ? _gameFix.Width + S(8) : 0);
            _gameLine.Bounds = new Rectangle(rightX, ty, gameW, StepPanel.Wrapped(_gameLine, gameW));
            _gameFix.Location = new Point(rightX + rightW - _gameFix.Width, ty - S(4));
            ty += _gameLine.Height + S(10);
            _complexNote.Bounds = new Rectangle(rightX, ty, rightW, string.IsNullOrEmpty(_complexNote.Text) ? 0 : StepPanel.Wrapped(_complexNote, rightW));
        };
    }

    /// <summary>
    /// How much the review's right column needs under its map: the name, the folder, the game line
    /// and the note. The map gives way to it, so on a wide, short window it is the map that shrinks.
    /// </summary>
    private int ReviewFootHeight(int rightW)
    {
        int noteW = rightW - _changeFolder.Width - S(8);
        int gameW = rightW - (_gameFix.Visible ? _gameFix.Width + S(8) : 0);
        return S(18) + _nameCaption.PreferredHeight + S(4) + _name.PreferredHeight + S(6)
               + TextRenderer.MeasureText("Ag", _path.Font).Height + S(4)
               + Math.Max(StepPanel.Wrapped(_nameNote, noteW), _changeFolder.Height) + S(14)
               + StepPanel.Wrapped(_gameLine, gameW) + S(10)
               + (string.IsNullOrEmpty(_complexNote.Text) ? 0 : StepPanel.Wrapped(_complexNote, rightW)) + S(12);
    }

    private void RefreshReview()
    {
        var values = SummaryValues();
        for (int i = 0; i < _summary.Count; i++) _summary[i].Value.Text = values[i];
        SuggestName();
        UpdateNameState();
        _reviewPanel.PerformLayout();
    }

    /// <summary>The name follows the export's own until it is typed over.</summary>
    private void SuggestName()
    {
        if (_nameTouched) return;
        _settingName = true;
        _name.Text = WorldName == "your world" ? "Azgaar world" : WorldName;
        _settingName = false;
    }

    private bool _nameOk;

    private bool CanCreate => FilesReady && _nameOk && _gameFound;

    private void UpdateNameState()
    {
        var (ok, dir, note, danger) = ModFolderCheck.Check(_modRoot, _name.Text);
        _path.Text = dir;
        _nameNote.Text = note;
        _nameNote.ForeColor = danger ? Theme.Danger : Theme.TextDim;
        _nameOk = ok;

        if (_step == ReviewStep && _mode == Mode.Steps) _next.Enabled = CanCreate;
        _reviewPanel.PerformLayout();
    }

    private void PickModRoot()
    {
        if (ModFolderCheck.PickRoot(FindForm(), _modRoot) is not { } root) return;
        _modRoot = root;
        UpdateNameState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _exportCts?.Cancel();
            _drawCts?.Cancel();
        }
        base.Dispose(disposing);
    }

    // ================================================================ file slot

    /// <summary>
    /// One of the two files: a card that is the button for choosing it. Empty, it is a dashed
    /// outline saying what goes there; filled, it names the file and what was found in it, with a
    /// tick, a warning, or a note that it is still being read.
    /// </summary>
    private sealed class FileSlot : PaintedButton
    {
        public enum State { Empty, Busy, Ok, Error }

        private static readonly Font GlyphFont = new(GlyphFamily, 15f);
        private static readonly Font MarkFont = new(GlyphFamily, 8f);
        private static readonly Font FileFont = new("Segoe UI Semibold", 9.5f);

        private State _state;
        private string? _file;
        private string _detail = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Glyph { get; set; } = "";

        public void Set(State state, string? file, string detail)
        {
            _state = state;
            _file = file;
            _detail = detail;
            AccessibleDescription = state switch
            {
                State.Ok => $"{file}: {detail}",
                State.Error => $"Problem: {detail}",
                State.Busy => detail,
                _ => "Not chosen yet",
            };
            Invalidate();
        }

        protected override void Draw(Graphics g)
        {
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = S(12);
            using var path = Rounded(box, radius);

            bool empty = _state == State.Empty;
            var fill = empty ? (Hover ? SelectedWash : Theme.Background) : Hover ? Color.FromArgb(250, 251, 253) : Theme.Surface;
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);

            var edge = _state == State.Error ? Theme.Danger
                     : empty ? (Hover ? Theme.Accent : Color.FromArgb(170, 182, 200))
                     : Hover ? Color.FromArgb(150, Theme.Accent) : Theme.Border;
            using (var pen = new Pen(edge, empty ? 1.5f : 1f) { DashStyle = empty ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid })
                g.DrawPath(pen, path);

            int pad = S(16);
            int disc = S(40);
            var discRect = new Rectangle(pad, (Height - disc) / 2, disc, disc);
            using (var db = new SolidBrush(empty ? Theme.SurfaceHigh : Theme.AccentSoft)) g.FillEllipse(db, discRect);
            TextRenderer.DrawText(g, Glyph, GlyphFont, discRect, empty ? Theme.TextDim : Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // The state mark, top right.
            int mark = S(18);
            var markRect = new Rectangle(Width - pad - mark, S(14), mark, mark);
            if (_state is State.Ok or State.Error)
            {
                using var mb = new SolidBrush(_state == State.Ok ? Good : Theme.Danger);
                g.FillEllipse(mb, markRect);
                TextRenderer.DrawText(g, _state == State.Ok ? "" : "!", _state == State.Ok ? MarkFont : FileFont, markRect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            int tx = discRect.Right + S(14);
            int tw = markRect.Left - S(8) - tx;
            const TextFormatFlags line = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int ty = S(14);
            TextRenderer.DrawText(g, Text, GroupTitle, new Rectangle(tx, ty, tw, S(20)), Theme.Text, line);
            ty += S(22);

            if (!empty && _file is not null)
            {
                TextRenderer.DrawText(g, _file, FileFont, new Rectangle(tx, ty, Width - pad - tx, S(18)), Theme.Text,
                    line | TextFormatFlags.PathEllipsis);
                ty += S(19);
            }

            var detailColor = _state == State.Error ? Theme.Danger : Theme.TextDim;
            TextRenderer.DrawText(g, _detail, Small, new Rectangle(tx, ty, Width - pad - tx, Height - ty - S(8)), detailColor,
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            DrawFocus(g, box, radius);
        }
    }
}
