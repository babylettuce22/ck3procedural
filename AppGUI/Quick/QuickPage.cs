using Ck3MapGen.Config;
using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The Quick generator: four short steps — Map, World, People, Review — then a run you can watch,
/// then a finished world with the obvious next moves.
///
/// It asks only what changes the feel of a world, in words rather than numbers, with a sensible
/// answer already picked for everything. Each answer is a card saying what it means, so the page
/// reads as a set of decisions rather than a form. The map step is the one worth lingering on: the
/// terrain previews in about a second, so rerolling the coastline until it looks right is cheap,
/// and that happens before the minutes-long run rather than after it.
///
/// The page decides nothing about generation itself. It hands <see cref="Choices"/> and the mod
/// folder to the main window (<see cref="CreateRequested"/>), which fills in the ordinary settings
/// from them and runs the ordinary write; progress and pictures come back through
/// <see cref="SetProgress"/> and <see cref="OfferLiveImage"/>. That is why "Customize in Complex"
/// can hand over the very same world.
/// </summary>
internal sealed class QuickPage : Panel
{
    public event Action? BackToStart;
    public event Action? CreateRequested;
    public event Action? CancelRequested;
    public event Action? LaunchRequested;
    public event Action? OpenFolderRequested;
    public event Action? CustomizeRequested;
    public event Action? GameFolderRequested;

    private const int MapStep = 0, WorldStep = 1, PeopleStep = 2, ReviewStep = 3;
    private static readonly string[] StepNames = ["Map", "World", "People", "Review"];

    /// <summary>Thumbnails use one fixed seed, so the tiles look the same every visit.</summary>
    private const int ThumbnailSeed = 20251;

    private enum Mode { Steps, Running, Done }

    private readonly IReadOnlyList<QuickMapType> _types = QuickCatalogue.All();
    private QuickChoices _choices = new();
    private readonly Stack<int> _previousSeeds = new();
    private int _step;
    private int _reached;
    private Mode _mode;
    private bool _gameFound;
    private string _modRoot = "";
    private bool _nameTouched;
    private bool _settingName;
    private CancellationTokenSource? _previewCts;
    private bool _thumbnailsStarted;

    // ---- chrome
    private readonly Panel _header = new() { Dock = DockStyle.Top, BackColor = Theme.Background };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, BackColor = Theme.Background };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Stepper _stepper = new(StepNames);
    private readonly TextLink _toStart = new() { Name = "quickToStart", Text = "Start", Glyph = "" };
    private readonly TextLink _surprise = new() { Name = "quickSurprise", Text = "Surprise me", Glyph = "" };
    private readonly PillButton _back = new() { Name = "quickBack", Text = "Back", Kind = PillKind.Secondary, Glyph = "" };
    private readonly PillButton _next = new() { Name = "quickNext", Text = "Next", Kind = PillKind.Primary, Glyph = "", GlyphAfter = true, MinWidth = 120 };
    private readonly Label _stepCount = MakeLabel("", Small, Theme.TextDim);

    // ---- steps
    private readonly StepPanel _mapPanel = new();
    private readonly StepPanel _worldPanel = new();
    private readonly StepPanel _peoplePanel = new();
    private readonly StepPanel _reviewPanel = new();
    private readonly StepPanel _runPanel = new();
    private readonly StepPanel _donePanel = new();

    // map step
    private readonly List<(QuickMapType Type, MapTile Tile)> _tiles = [];
    private readonly MapPreview _preview = new();
    private readonly Label _typeName = MakeLabel("", new Font("Segoe UI Semibold", 13f), Theme.Text);
    private readonly Label _typeBlurb = MakeLabel("", Body, Theme.TextDim, wrap: true);
    private readonly Label _seedCaption = MakeLabel("Seed", Small, Theme.TextDim);
    private readonly TextBox _seedBox = new() { BorderStyle = BorderStyle.FixedSingle, Font = Body, Name = "quickSeed" };
    private readonly PillButton _reroll = new() { Name = "quickReroll", Text = "Roll again", Kind = PillKind.Primary, Glyph = "" };
    private readonly PillButton _previous = new() { Name = "quickPrevious", Text = "Previous", Kind = PillKind.Quiet, Glyph = "" };
    private readonly Label _reliefCaption = MakeLabel("Relief", Small, Theme.TextDim);
    private readonly Dictionary<QuickRelief, Theme.SegmentButton> _reliefButtons = new()
    {
        [QuickRelief.Lowlands] = new() { Text = "Lowlands", Name = "quickReliefLowlands" },
        [QuickRelief.Standard] = new() { Text = "Standard", Name = "quickReliefStandard" },
        [QuickRelief.Highlands] = new() { Text = "Highlands", Name = "quickReliefHighlands" },
    };
    private FlowLayoutPanel? _reliefTrack;
    private readonly ToolTip _reliefTips = new() { InitialDelay = 400 };
    private readonly Label _mapHint = MakeLabel(
        "Every seed is a different world. This is the bare terrain: rivers, climate and erosion are added when the world is made.",
        Small, Theme.TextDim, wrap: true);

    // world step
    private readonly ChoiceGroup<QuickSize> _size;
    private readonly ChoiceGroup<QuickEra> _era;
    private readonly ChoiceGroup<QuickClimate> _climate;
    private readonly ChoiceGroup<QuickDensity> _density;

    // people step
    private readonly ChoiceGroup<QuickPeople> _peopleGroup;
    private readonly ChoiceGroup<QuickPolitics> _politics;
    private readonly ChoiceGroup<GenderPreference> _rulers;
    private readonly ToggleCard _wilderness = new() { Name = "quickWilderness", Text = "Wilderness", Description = "Unsettled lands to clear, claim and colonise." };
    private readonly ToggleCard _wars = new() { Name = "quickWars", Text = "Wars at the start", Description = "Rivals already at war on the first day." };

    // review step
    private readonly List<(Label Key, Label Value, TextLink Change)> _summary = [];
    private readonly MapPreview _reviewMap = new();
    private readonly Label _nameCaption = MakeLabel("Mod name", Strong, Theme.Text);
    private readonly TextBox _name = new() { BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11f), Name = "quickModName" };
    private readonly PathText _path = new() { Font = new Font("Consolas", 8.5f) };
    private readonly Label _nameNote = MakeLabel("", Small, Theme.TextDim, wrap: true);
    private readonly TextLink _changeFolder = new() { Name = "quickChangeFolder", Text = "Change folder…", LinkFont = Small };
    private readonly Label _gameLine = MakeLabel("", Small, Theme.TextDim, wrap: true);
    private readonly TextLink _gameFix = new() { Name = "quickGameFolder", Text = "Set game folder…", LinkFont = Small };
    private readonly Label _complexNote = MakeLabel("", Small, Theme.TextDim, wrap: true);

    // discoveries, shared by the run and done views
    private readonly ShowcaseFeed _feed = new() { Name = "quickFeed" };
    private readonly Label _feedTitle = MakeLabel("Discoveries", GroupTitle, Theme.Text);
    private readonly Label _feedCount = MakeLabel("", Small, Theme.TextDim);

    // what fills the space under the map: the run's milestones, then the finished world's tallies
    private readonly MilestoneStrip _milestones = new() { Name = "quickMilestones" };
    private readonly TallyRow _tallies = new() { Name = "quickTallies" };
    private readonly Label _talliesTitle = MakeLabel("At a glance", GroupTitle, Theme.Text);

    // run view
    private readonly MapPreview _runMap = new();
    private readonly ProgressLine _bar = new();
    private readonly Label _percent = MakeLabel("", new Font("Segoe UI Semibold", 12f), Theme.Text);
    private readonly Label _eta = MakeLabel("", Body, Theme.TextDim);
    private readonly Label _phase = MakeLabel("", Small, Theme.TextDim);
    private readonly PillButton _cancel = new() { Name = "quickCancel", Text = "Cancel", Kind = PillKind.Secondary, Glyph = "" };
    private readonly Label _runTitle = MakeLabel("Making your world", Title, Theme.Text);
    private readonly Label _runSubtitle = MakeLabel("", Subtitle, Theme.TextDim);

    // done view
    private readonly Label _doneTitle = MakeLabel("Your world is ready", Title, Theme.Text);
    private readonly Label _doneSubtitle = MakeLabel("", Subtitle, Theme.TextDim, wrap: true);
    private readonly PathText _donePath = new() { Font = new Font("Consolas", 8.5f) };
    private readonly MapPreview _doneMap = new();
    private readonly PillButton _launch = new() { Name = "quickLaunch", Text = "Launch Crusader Kings III", Kind = PillKind.Primary, Glyph = "" };
    private readonly PillButton _openFolder = new() { Name = "quickOpenFolder", Text = "Open mod folder", Glyph = "" };
    private readonly PillButton _customize = new() { Name = "quickCustomize", Text = "Customize in Complex", Glyph = "" };
    private readonly PillButton _another = new() { Name = "quickAnother", Text = "Make another", Kind = PillKind.Quiet, Glyph = "" };
    private readonly PillButton _retry = new() { Name = "quickRetry", Text = "Back to review", Kind = PillKind.Primary, Glyph = "" };
    private readonly PillButton _details = new() { Name = "quickDetails", Text = "Show details in Complex", Glyph = "" };
    private bool _failed;

    public QuickPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        Name = "quickPage";

        _size = new ChoiceGroup<QuickSize>("Size", "How big the map is. Bigger worlds take longer to make.", QuickSize.Standard)
            .Add(QuickSize.Small, "Small", "4096 × 2048 · quickest to make", "quickSizeSmall")
            .Add(QuickSize.Standard, "Standard", "8192 × 4096 · recommended", "quickSizeStandard")
            .Add(QuickSize.Large, "Large", "9216 × 4608 · half of vanilla", "quickSizeLarge");
        _era = new ChoiceGroup<QuickEra>("Era", "When the game begins, and how advanced its cultures are.", QuickEra.High)
            .Add(QuickEra.Early, "Early medieval", "867 · tribes and young kingdoms", "quickEraEarly")
            .Add(QuickEra.High, "High medieval", "1066 · feudal realms at their height", "quickEraHigh")
            .Add(QuickEra.Late, "Late medieval", "1178 · old dynasties, rich courts", "quickEraLate");
        _climate = new ChoiceGroup<QuickClimate>("Climate", "Which latitudes the map spans.", QuickClimate.Temperate)
            .Add(QuickClimate.Northern, "Northern", "Cold north down to a warm south", "quickClimateNorthern")
            .Add(QuickClimate.Temperate, "Temperate", "Mild lands with a cold far north", "quickClimateTemperate")
            .Add(QuickClimate.Warm, "Warm", "Deserts, savannas and tropics", "quickClimateWarm")
            .Add(QuickClimate.Globe, "Whole globe", "Ice at both edges, tropics between", "quickClimateGlobe");
        _density = new ChoiceGroup<QuickDensity>("Provinces", "How finely the land is divided into counties.", QuickDensity.Balanced)
            .Add(QuickDensity.Fewer, "Fewer, larger", "Bigger counties · faster to make", "quickDensityFewer")
            .Add(QuickDensity.Balanced, "Balanced", "The generator's own default", "quickDensityBalanced")
            .Add(QuickDensity.More, "Many, smaller", "Vanilla-sized counties · slower", "quickDensityMore");

        _peopleGroup = new ChoiceGroup<QuickPeople>("Cultures & faiths", "Who lives here.", QuickPeople.Invented)
            .Add(QuickPeople.Invented, "Invented", "New cultures, faiths, languages and names", "quickPeopleInvented")
            .Add(QuickPeople.RealCk3, "Real CK3", "Vanilla's cultures and faiths laid onto this map", "quickPeopleReal");
        _politics = new ChoiceGroup<QuickPolitics>("Politics", "How the realms stand on the first day.", QuickPolitics.Kingdoms)
            .Add(QuickPolitics.Fragmented, "Fragmented", "Every count rules alone", "quickPoliticsFragmented")
            .Add(QuickPolitics.Kingdoms, "Kingdoms", "Realms great and small", "quickPoliticsKingdoms")
            .Add(QuickPolitics.Hegemony, "Hegemony", "One realm claims the whole world", "quickPoliticsHegemony");
        _rulers = new ChoiceGroup<GenderPreference>("Rulers", "Who tends to hold land and titles.", GenderPreference.Historical)
            .Add(GenderPreference.Historical, "Historical", "Mostly men, as in vanilla", "quickRulersHistorical")
            .Add(GenderPreference.Mixed, "Mixed", "Varies from faith to faith", "quickRulersMixed")
            .Add(GenderPreference.FemaleDominated, "Women rule", "Women hold the land and titles", "quickRulersWomen");

        _size.Changed += v => _choices.Size = v;
        _era.Changed += v => _choices.Era = v;
        _climate.Changed += v => _choices.Climate = v;
        _density.Changed += v => _choices.Density = v;
        _peopleGroup.Changed += v => _choices.People = v;
        _politics.Changed += v => _choices.Politics = v;
        _rulers.Changed += v => _choices.Rulers = v;
        _wilderness.Toggled += on => _choices.Wilderness = on;
        _wars.Toggled += on => _choices.Wars = on;

        BuildChrome();
        BuildMapStep();
        BuildGroupsStep(_worldPanel, "Shape the world", "Size, era and climate. The defaults make a good first world.",
            [_size, _era, _climate, _density], toggles: null);
        BuildGroupsStep(_peoplePanel, "People and politics", "Who lives here, who rules, and what else the world holds.",
            [_peopleGroup, _politics, _rulers], toggles: [_wilderness, _wars]);
        BuildReviewStep();
        BuildRunView();
        BuildDoneView();

        foreach (var panel in (StepPanel[])[_mapPanel, _worldPanel, _peoplePanel, _reviewPanel, _runPanel, _donePanel])
            _body.Controls.Add(panel);

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
    public QuickChoices Choices => _choices.Clone();

    public string ModRoot => _modRoot;
    public string ModName => _name.Text.Trim();
    public string ModDir => Path.Combine(_modRoot, ModNameDialog.FolderName(_name.Text));
    public bool IsRunning => _mode == Mode.Running;

    /// <summary>The size the page wants to be shown at: the column, its margins, and the tallest step.</summary>
    public Size PreferredPageSize => new(S(940) + 2 * S(32), S(60) + 1 + S(606) + 1 + S(68));

    /// <summary>
    /// Opens the page on its first step, with the choices the last Quick world was made with and a
    /// fresh seed. Nothing is carried from a previous visit's run.
    /// </summary>
    public void Begin(QuickChoices? remembered, bool gameFound, string gameDir, string modRoot, string? complexNote)
    {
        _choices = remembered?.Clone() ?? new QuickChoices();
        if (_types.Count > 0 && !_types.Any(t => t.Key == _choices.MapType)) _choices.MapType = _types[0].Key;
        _choices.Seed = Random.Shared.Next(1, 1_000_000);
        _previousSeeds.Clear();
        _modRoot = modRoot;
        _nameTouched = false;
        _complexNote.Text = complexNote ?? "";
        _failed = false;
        SetGameFolder(gameFound, gameDir);

        SyncControls();
        Go(MapStep, resetReached: true);
        StartThumbnails();
        RenderPreview();
    }

    public void SetGameFolder(bool found, string gameDir)
    {
        _gameFound = found;
        _gameLine.Text = found
            ? $"✓  Crusader Kings III found at {gameDir}"
            : "Crusader Kings III was not found. It is needed to write the mod.";
        _gameLine.ForeColor = found ? Good : Theme.Danger;
        _gameFix.Visible = !found;
        UpdateNameState();
        _reviewPanel.PerformLayout();
    }

    public void SetModRoot(string root)
    {
        _modRoot = root;
        UpdateNameState();
    }

    /// <summary>Swaps the steps for the run view. The picture starts as the preview just approved.</summary>
    public void ShowRunning()
    {
        _mode = Mode.Running;
        var type = CurrentType;
        var (w, h) = _choices.Pixels;
        _runSubtitle.Text = $"{type?.Title ?? _choices.MapType} · seed {_choices.Seed} · {w} × {h}. This takes a few minutes.";
        _runMap.Image = _preview.Image is { } img ? new Bitmap(img) : null;
        _runMap.Chip = "Raising the land";
        _bar.Fraction = null;
        _percent.Text = "Starting…";
        _eta.Text = "";
        _phase.Text = "";
        _cancel.Enabled = true;
        _cancel.Text = "Cancel";
        _milestones.Reset();
        _tallies.Set([]);
        _feed.Clear();
        _feed.EmptyText = "Peoples, faiths, armies and treasures appear here as the world is made. "
                        + "The land comes first.";
        _feedTitle.Text = "Discoveries";
        UpdateFeedCount();
        MoveFeedTo(_runPanel);
        ShowPanel(_runPanel);
        UpdateChrome();
    }

    /// <summary>Progress from the run. A null fraction means the estimate has nothing to go on yet.</summary>
    public void SetProgress(double? fraction, string? remaining, TimeSpan elapsed, string? phase)
    {
        if (_mode != Mode.Running) return;
        _bar.Fraction = fraction;
        _percent.Text = fraction is { } f ? $"{f * 100:F0}%" : $"{elapsed.TotalSeconds:F0} s";
        _eta.Text = remaining ?? "working out how long this takes…";
        if (phase is not null) _phase.Text = phase;
        _runPanel.PerformLayout();
    }

    private static readonly Dictionary<string, string> LiveViews = new()
    {
        ["Relief"] = "Raising the land",
        ["Climate"] = "Settling the climate",
        ["Terrain"] = "Painting the terrain",
        ["Counties"] = "Drawing the counties",
        ["Duchies"] = "Drawing the duchies",
    };

    /// <summary>
    /// A picture the run just drew. The page keeps the ones that read well as a map filling in and
    /// disposes the rest. Takes ownership either way.
    /// </summary>
    public void OfferLiveImage(string view, Bitmap bitmap)
    {
        if (_mode != Mode.Running || !LiveViews.TryGetValue(view, out string? caption))
        {
            bitmap.Dispose();
            return;
        }

        _runMap.Image = bitmap;
        _runMap.Chip = caption;
    }

    public void CancelPending()
    {
        _cancel.Enabled = false;
        _cancel.Text = "Stopping…";
        _phase.Text = "Stopping at the end of this step…";
    }

    /// <summary>The run finished and the mod is on disk.</summary>
    public void ShowDone(string modDir, TimeSpan took)
    {
        _mode = Mode.Done;
        _failed = false;
        _doneTitle.Text = "Your world is ready";
        _doneSubtitle.Text = $"“{ModName}” was made in {Describe(took)}. "
                           + "Launch the game, or open it in Complex to fine-tune anything.";
        _donePath.Text = modDir;
        _doneMap.Image = _runMap.Image is { } img ? new Bitmap(img) : null;
        _doneMap.Chip = "Drawing the realms…";
        foreach (var b in (Control[])[_launch, _openFolder, _customize, _another]) b.Visible = true;
        foreach (var b in (Control[])[_retry, _details]) b.Visible = false;
        _launch.Enabled = _gameFound;
        HandFeedToDone("Discovered in this world");
        ShowPanel(_donePanel);
        UpdateChrome();
    }

    public void SetDoneImage(Bitmap bitmap, string chip)
    {
        if (_mode != Mode.Done) { bitmap.Dispose(); return; }
        _doneMap.Image = bitmap;
        _doneMap.Chip = chip;
    }

    /// <summary>The run stopped short: cancelled, or failed.</summary>
    public void ShowFailed(bool cancelled, string? message)
    {
        _mode = Mode.Done;
        _failed = true;
        _doneTitle.Text = cancelled ? "Stopped" : "The world could not be made";
        _doneSubtitle.Text = cancelled
            ? "The run was cancelled. The mod folder may be half written; making the world again replaces it."
            : $"{message ?? "Something went wrong."} The log in Complex has the details.";
        _donePath.Text = "";
        _doneMap.Image = _runMap.Image is { } img ? new Bitmap(img) : null;
        _doneMap.Chip = cancelled ? "Cancelled" : "Stopped here";
        foreach (var b in (Control[])[_launch, _openFolder, _customize, _another]) b.Visible = false;
        foreach (var b in (Control[])[_retry, _details]) b.Visible = true;
        HandFeedToDone("Made before it stopped");
        ShowPanel(_donePanel);
        UpdateChrome();
    }

    private static string Describe(TimeSpan t)
        => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{t.TotalSeconds:F0} s";

    // ================================================================ chrome

    private void BuildChrome()
    {
        _header.Height = S(60);
        _footer.Height = S(68);

        _header.Controls.AddRange([_toStart, _stepper, _surprise]);
        _footer.Controls.AddRange([_back, _stepCount, _next]);
        _header.Layout += (_, _) => LayoutHeader();
        _footer.Layout += (_, _) => LayoutFooter();

        _toStart.Click += (_, _) => { if (_mode != Mode.Running) BackToStart?.Invoke(); };
        _surprise.Click += (_, _) => Surprise();
        _stepper.StepClicked += i => Go(i);
        _back.Click += (_, _) => { if (_step > 0) Go(_step - 1); else BackToStart?.Invoke(); };
        _next.Click += (_, _) => { if (_step < ReviewStep) Go(_step + 1); else if (CanCreate) CreateRequested?.Invoke(); };
    }

    private (int X, int Width) Column(Control host)
    {
        int width = Math.Min(host.ClientSize.Width - 2 * S(32), S(940));
        return ((host.ClientSize.Width - width) / 2, Math.Max(width, 100));
    }

    private void LayoutHeader()
    {
        var (x, w) = Column(_header);
        int h = _header.Height;
        _toStart.Location = new Point(x - S(4), (h - _toStart.Height) / 2);
        _surprise.Location = new Point(x + w - _surprise.Width, (h - _surprise.Height) / 2);
        int stepW = Math.Min(_stepper.NaturalWidth + S(8), w - _toStart.Width - _surprise.Width - S(24));
        _stepper.Bounds = new Rectangle((_header.Width - stepW) / 2, (h - S(40)) / 2, stepW, S(40));
    }

    private void LayoutFooter()
    {
        var (x, w) = Column(_footer);
        int h = _footer.Height;
        _back.Location = new Point(x, (h - _back.Height) / 2);
        _next.Location = new Point(x + w - _next.Width, (h - _next.Height) / 2);
        _stepCount.Location = new Point((_footer.Width - _stepCount.PreferredWidth) / 2, (h - _stepCount.PreferredHeight) / 2);
    }

    private void UpdateChrome()
    {
        bool steps = _mode == Mode.Steps;
        _footer.Visible = steps;
        _surprise.Visible = steps;
        _toStart.Enabled = _mode != Mode.Running;
        _stepper.Set(steps ? _step : ReviewStep, _reached, locked: !steps);

        _back.Text = _step == MapStep ? "Start" : "Back";
        _next.Text = _step switch
        {
            MapStep => "Next: World",
            WorldStep => "Next: People",
            PeopleStep => "Next: Review",
            _ => "Create my world",
        };
        _next.Glyph = _step == ReviewStep ? "" : "";
        _next.GlyphAfter = _step != ReviewStep;
        _next.MinWidth = _step == ReviewStep ? 170 : 120;
        _next.FitWidth();
        _next.Enabled = _step != ReviewStep || CanCreate;
        _stepCount.Text = $"Step {_step + 1} of {StepNames.Length}";
        _footer.PerformLayout();
        _header.PerformLayout();
    }

    private void Go(int step, bool resetReached = false)
    {
        _mode = Mode.Steps;
        _step = Math.Clamp(step, 0, ReviewStep);
        _reached = resetReached ? _step : Math.Max(_reached, _step);

        if (_step == ReviewStep) RefreshReview();

        ShowPanel(_step switch
        {
            MapStep => _mapPanel,
            WorldStep => _worldPanel,
            PeopleStep => _peoplePanel,
            _ => _reviewPanel,
        });
        UpdateChrome();
        _next.Focus();
    }

    private void ShowPanel(StepPanel panel)
    {
        SuspendLayout();
        foreach (Control c in _body.Controls) c.Visible = ReferenceEquals(c, panel);
        ResumeLayout();
        panel.PerformLayout();
    }

    /// <summary>
    /// Everything re-drawn at random except the choices that decide how long the run takes or
    /// what the game needs installed: size, density and where the people come from stay put.
    /// </summary>
    private void Surprise()
    {
        var kept = _choices;
        _choices = QuickChoices.Surprise(Random.Shared, _types);
        _choices.Size = kept.Size;
        _choices.Density = kept.Density;
        _choices.People = kept.People;
        _previousSeeds.Push(kept.Seed);
        SyncControls();
        RenderPreview();
        if (_step == ReviewStep) RefreshReview();
        UpdateChrome();
    }

    /// <summary>Puts every control in step with <see cref="_choices"/>.</summary>
    private void SyncControls()
    {
        foreach (var (type, tile) in _tiles) tile.Selected = type.Key == _choices.MapType;
        _size.Value = _choices.Size;
        _era.Value = _choices.Era;
        _climate.Value = _choices.Climate;
        _density.Value = _choices.Density;
        _peopleGroup.Value = _choices.People;
        _politics.Value = _choices.Politics;
        _rulers.Value = _choices.Rulers;
        _wilderness.On = _choices.Wilderness;
        _wars.On = _choices.Wars;
        _seedBox.Text = _choices.Seed.ToString();
        foreach (var (relief, button) in _reliefButtons) Theme.StyleSegment(button, relief == _choices.Relief);
        var type2 = CurrentType;
        _typeName.Text = type2?.Title ?? "";
        _typeBlurb.Text = type2?.Blurb ?? "";
        _previous.Enabled = _previousSeeds.Count > 0;
        SuggestName();
        _mapPanel.PerformLayout();
    }

    private QuickMapType? CurrentType => _types.FirstOrDefault(t => t.Key == _choices.MapType);

    // ================================================================ map step

    private void BuildMapStep()
    {
        var title = MakeLabel("Choose a map", Title, Theme.Text);
        var subtitle = MakeLabel("Pick the shape of the land, then roll until the coastline feels right.", Subtitle, Theme.TextDim);
        _mapPanel.Controls.AddRange([title, subtitle, _preview, _typeName, _typeBlurb, _seedCaption, _seedBox, _reroll, _previous, _mapHint]);

        // Relief: how rugged the land is, whatever its shape. Changes the preview and, through the
        // game's hill and mountain shares, how the world plays. See QuickTerrain.
        _reliefTrack = Theme.MakeSegmented(_reliefButtons.Values);
        _reliefTrack.Margin = new Padding(0);
        _mapPanel.Controls.Add(_reliefCaption);
        _mapPanel.Controls.Add(_reliefTrack);
        _reliefTips.SetToolTip(_reliefButtons[QuickRelief.Lowlands], "Broad plains and low hills; few mountains. More farmland, easier marching.");
        _reliefTips.SetToolTip(_reliefButtons[QuickRelief.Standard], "The map type as it was designed.");
        _reliefTips.SetToolTip(_reliefButtons[QuickRelief.Highlands], "Rugged country: more ranges, more hills, more impassable peaks.");
        foreach (var (relief, button) in _reliefButtons)
            button.Click += (_, _) => PickRelief(relief);

        foreach (var type in _types)
        {
            var tile = new MapTile { Text = type.Title, Name = "quickType-" + type.Key, AccessibleName = type.Title };
            tile.Click += (_, _) => PickType(type);
            new ToolTip { InitialDelay = 400 }.SetToolTip(tile, type.Blurb);
            _tiles.Add((type, tile));
            _mapPanel.Controls.Add(tile);
        }

        _reroll.Click += (_, _) => Reroll();
        _previous.Click += (_, _) => PreviousSeed();
        _seedBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplySeedBox(); } };
        _seedBox.Leave += (_, _) => ApplySeedBox();

        _mapPanel.Arrange = panel =>
        {
            var (x, w) = Column(panel);
            int y = S(18);
            title.Location = new Point(x - S(2), y);
            y += title.PreferredHeight + S(2);
            subtitle.Location = new Point(x, y);
            y += subtitle.PreferredHeight + S(16);

            int n = Math.Max(1, _tiles.Count);
            int gap = S(10);
            int tileW = (w - gap * (n - 1)) / n;
            int tileH = (tileW - S(12)) / 2 + S(12) + S(28);
            for (int i = 0; i < _tiles.Count; i++)
                _tiles[i].Tile.Bounds = new Rectangle(x + i * (tileW + gap), y, tileW, tileH);
            y += tileH + S(18);

            int side = S(200);
            int previewW = w - side - S(24);
            int previewH = Math.Min(previewW / 2, panel.ClientSize.Height - y - S(12));
            previewW = previewH * 2;
            _preview.Bounds = new Rectangle(x, y, previewW, previewH);

            int sx = x + previewW + S(24), sw = x + w - sx;
            int sy = y;
            _typeName.Location = new Point(sx - S(1), sy);
            sy += _typeName.PreferredHeight + S(4);
            _typeBlurb.Bounds = new Rectangle(sx, sy, sw, Wrapped(_typeBlurb, sw));
            sy += _typeBlurb.Height + S(18);
            _seedCaption.Location = new Point(sx, sy);
            sy += _seedCaption.PreferredHeight + S(3);
            _seedBox.Bounds = new Rectangle(sx, sy, sw, _seedBox.PreferredHeight);
            sy += _seedBox.Height + S(10);
            _reroll.MinWidth = sw * 96 / DeviceDpi;
            _reroll.FitWidth();
            _reroll.Location = new Point(sx, sy);
            sy += _reroll.Height + S(6);
            _previous.MinWidth = sw * 96 / DeviceDpi;
            _previous.FitWidth();
            _previous.Location = new Point(sx, sy);
            sy += _previous.Height + S(12);

            if (_reliefTrack is { } track)
            {
                _reliefCaption.Location = new Point(sx, sy);
                sy += _reliefCaption.PreferredHeight + S(3);
                // Sized exactly: the track is an auto-sizing panel, and left to itself it grows to
                // whatever it once measured and never shrinks back.
                int each = (sw - S(4)) / _reliefButtons.Count;
                int buttonH = S(26);
                foreach (var button in _reliefButtons.Values) button.Size = new Size(each, buttonH);
                track.AutoSize = false;
                track.Bounds = new Rectangle(sx, sy, each * _reliefButtons.Count + S(4), buttonH + S(4));
                sy += track.Height + S(14);
            }

            _mapHint.Bounds = new Rectangle(sx, sy, sw, Math.Min(Wrapped(_mapHint, sw), y + previewH - sy));
        };
    }

    private static int Wrapped(Label label, int width)
        => TextRenderer.MeasureText(label.Text ?? "", label.Font, new Size(Math.Max(1, width), 0),
               TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 2;

    private void PickRelief(QuickRelief relief)
    {
        if (_choices.Relief == relief) return;
        _choices.Relief = relief;
        SyncControls();
        RenderPreview();
    }

    private void PickType(QuickMapType type)
    {
        if (_choices.MapType == type.Key) return;
        _choices.MapType = type.Key;
        SyncControls();
        RenderPreview();
    }

    private void Reroll()
    {
        _previousSeeds.Push(_choices.Seed);
        _choices.Seed = Random.Shared.Next(1, 1_000_000);
        SyncControls();
        RenderPreview();
    }

    private void PreviousSeed()
    {
        if (_previousSeeds.Count == 0) return;
        _choices.Seed = _previousSeeds.Pop();
        SyncControls();
        RenderPreview();
    }

    private void ApplySeedBox()
    {
        if (!int.TryParse(_seedBox.Text.Trim(), out int seed) || seed < 0)
        {
            _seedBox.Text = _choices.Seed.ToString();
            return;
        }
        if (seed == _choices.Seed) return;
        _previousSeeds.Push(_choices.Seed);
        _choices.Seed = seed;
        SyncControls();
        RenderPreview();
    }

    /// <summary>
    /// Draws the chosen type at the chosen seed off the UI thread. A newer request cancels an older
    /// one, so rolling quickly never queues up stale pictures.
    /// </summary>
    private void RenderPreview()
    {
        if (CurrentType is not { } type) return;

        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        int seed = _choices.Seed;
        _preview.Busy = "Drawing…";
        var relief = _choices.Relief;
        _preview.Chip = relief == QuickRelief.Standard
            ? $"{type.Title}  ·  seed {seed}"
            : $"{type.Title}  ·  {relief}  ·  seed {seed}";

        Task.Run(() => ForgePreview.Render(type.PresetPath, seed, 1024, 512, cts.Token, relief)).ContinueWith(task =>
        {
            var bitmap = task.Result;
            if (IsDisposed || !IsHandleCreated) { bitmap?.Dispose(); return; }
            BeginInvoke(() =>
            {
                if (cts.IsCancellationRequested) { bitmap?.Dispose(); return; }
                _preview.Busy = bitmap is null ? "Could not draw this map" : null;
                if (bitmap is null) return;
                _preview.Image = bitmap;
                _reviewMap.Image = new Bitmap(bitmap);
            });
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    /// <summary>One small picture per map type, drawn once, one after another, at a fixed seed.</summary>
    private void StartThumbnails()
    {
        if (_thumbnailsStarted) return;
        _thumbnailsStarted = true;
        var work = _tiles.Select(t => (t.Type.PresetPath, t.Tile)).ToList();

        Task.Run(() =>
        {
            foreach (var (path, tile) in work)
            {
                var bitmap = ForgePreview.Render(path, ThumbnailSeed, 384, 192, CancellationToken.None);
                if (bitmap is null) continue;
                if (IsDisposed || !IsHandleCreated) { bitmap.Dispose(); return; }
                BeginInvoke(() => tile.Thumbnail = bitmap);
            }
        });
    }

    // ================================================================ choice steps

    private void BuildGroupsStep(StepPanel panel, string title, string subtitle, IChoiceGroup[] views, ToggleCard[]? toggles)
    {
        var titleLabel = MakeLabel(title, Title, Theme.Text);
        var subtitleLabel = MakeLabel(subtitle, Subtitle, Theme.TextDim);
        panel.Controls.AddRange([titleLabel, subtitleLabel]);

        var headers = new List<(Label Title, Label Hint, List<ChoiceCard> Cards)>();
        foreach (var view in views)
        {
            var t = MakeLabel(view.Title, GroupTitle, Theme.Text);
            var h = MakeLabel(view.Hint, Small, Theme.TextDim);
            var cards = view.Cards.ToList();
            panel.Controls.Add(t);
            panel.Controls.Add(h);
            foreach (var card in cards) panel.Controls.Add(card);
            headers.Add((t, h, cards));
        }

        Label? extrasTitle = null, extrasHint = null;
        if (toggles is not null)
        {
            extrasTitle = MakeLabel("Extras", GroupTitle, Theme.Text);
            extrasHint = MakeLabel("Switch off anything you would rather play without.", Small, Theme.TextDim);
            panel.Controls.Add(extrasTitle);
            panel.Controls.Add(extrasHint);
            foreach (var toggle in toggles) panel.Controls.Add(toggle);
        }

        panel.Arrange = p =>
        {
            var (x, w) = Column(p);
            int y = S(18);
            titleLabel.Location = new Point(x - S(2), y);
            y += titleLabel.PreferredHeight + S(2);
            subtitleLabel.Location = new Point(x, y);
            y += subtitleLabel.PreferredHeight + S(18);

            void Row(Label t, Label h, IReadOnlyList<Control> cards, int cardH)
            {
                t.Location = new Point(x, y);
                h.Location = new Point(x + t.PreferredWidth + S(10), y + (t.PreferredHeight - h.PreferredHeight) / 2 + S(1));
                y += t.PreferredHeight + S(6);
                int gap = S(12);
                int n = Math.Max(1, cards.Count);
                int cw = (w - gap * (n - 1)) / n;
                for (int i = 0; i < cards.Count; i++)
                    cards[i].Bounds = new Rectangle(x + i * (cw + gap), y, i == n - 1 ? w - i * (cw + gap) : cw, cardH);
                y += cardH + S(16);
            }

            foreach (var (t, h, cards) in headers) Row(t, h, cards, S(62));
            if (toggles is not null && extrasTitle is not null && extrasHint is not null) Row(extrasTitle, extrasHint, toggles, S(66));
        };
    }

    // ================================================================ review

    private static readonly Dictionary<QuickSize, string> SizeNames = new()
        { [QuickSize.Small] = "Small", [QuickSize.Standard] = "Standard", [QuickSize.Large] = "Large" };
    private static readonly Dictionary<QuickEra, string> EraNames = new()
        { [QuickEra.Early] = "Early medieval", [QuickEra.High] = "High medieval", [QuickEra.Late] = "Late medieval" };
    private static readonly Dictionary<QuickClimate, string> ClimateNames = new()
        { [QuickClimate.Northern] = "Northern", [QuickClimate.Temperate] = "Temperate", [QuickClimate.Warm] = "Warm", [QuickClimate.Globe] = "Whole globe" };
    private static readonly Dictionary<QuickDensity, string> DensityNames = new()
        { [QuickDensity.Fewer] = "Fewer, larger counties", [QuickDensity.Balanced] = "Balanced", [QuickDensity.More] = "Many, smaller counties" };
    private static readonly Dictionary<QuickPeople, string> PeopleNames = new()
        { [QuickPeople.Invented] = "Invented", [QuickPeople.RealCk3] = "Real CK3 cultures and faiths" };
    private static readonly Dictionary<QuickPolitics, string> PoliticsNames = new()
        { [QuickPolitics.Fragmented] = "Fragmented", [QuickPolitics.Kingdoms] = "Kingdoms", [QuickPolitics.Hegemony] = "Hegemony" };
    private static readonly Dictionary<GenderPreference, string> RulerNames = new()
        { [GenderPreference.Historical] = "Historical", [GenderPreference.Mixed] = "Mixed", [GenderPreference.FemaleDominated] = "Women rule" };

    private (string Key, int Step)[] SummaryRows =>
    [
        ("Map", MapStep), ("Relief", MapStep), ("Size", WorldStep), ("Era", WorldStep), ("Climate", WorldStep), ("Provinces", WorldStep),
        ("Cultures & faiths", PeopleStep), ("Politics", PeopleStep), ("Rulers", PeopleStep), ("Extras", PeopleStep),
    ];

    private string[] SummaryValues()
    {
        var (w, h) = _choices.Pixels;
        var extras = new List<string>();
        if (_choices.Wilderness) extras.Add("Wilderness");
        if (_choices.Wars) extras.Add("Wars at the start");
        return
        [
            $"{CurrentType?.Title ?? _choices.MapType}  ·  seed {_choices.Seed}",
            _choices.Relief switch
            {
                QuickRelief.Lowlands => "Lowlands  ·  fewer hills and mountains",
                QuickRelief.Highlands => "Highlands  ·  more hills and mountains",
                _ => "Standard",
            },
            $"{SizeNames[_choices.Size]}  ·  {w} × {h}",
            $"{EraNames[_choices.Era]}  ·  {_choices.StartYear}",
            ClimateNames[_choices.Climate],
            DensityNames[_choices.Density],
            PeopleNames[_choices.People],
            PoliticsNames[_choices.Politics],
            RulerNames[_choices.Rulers],
            extras.Count == 0 ? "None" : string.Join("  ·  ", extras),
        ];
    }

    private void BuildReviewStep()
    {
        var title = MakeLabel("Ready to make your world", Title, Theme.Text);
        var subtitle = MakeLabel("Check the choices, name the mod, and create it. You can change anything first.", Subtitle, Theme.TextDim);
        _reviewPanel.Controls.AddRange([title, subtitle, _reviewMap, _nameCaption, _name, _path, _nameNote, _changeFolder, _gameLine, _gameFix, _complexNote]);

        foreach (var (key, step) in SummaryRows)
        {
            var k = MakeLabel(key, Body, Theme.TextDim);
            var v = MakeLabel("", Strong, Theme.Text);
            var change = new TextLink
            {
                Text = "Change", LinkFont = Small, Backdrop = Theme.Surface,
                Name = "quickChange-" + key.Replace(' ', '-').Replace("&", "and"),
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
            var (x, w) = Column(panel);
            int y = S(18);
            title.Location = new Point(x - S(2), y);
            y += title.PreferredHeight + S(2);
            subtitle.Location = new Point(x, y);
            y += subtitle.PreferredHeight + S(18);

            int gap = S(28);
            int leftW = (w - gap) * 52 / 100;
            int rightX = x + leftW + gap, rightW = x + w - rightX;

            // The summary, on a white card.
            int rowH = S(38);
            int cardPad = S(16);
            var card = new Rectangle(x, y, leftW, cardPad * 2 + rowH * _summary.Count);
            panel.Cards.Add(card);
            int ry = y + cardPad;
            for (int i = 0; i < _summary.Count; i++)
            {
                var (k, v, change) = _summary[i];
                int cy = ry + i * rowH;
                k.Location = new Point(x + cardPad, cy + (rowH - k.PreferredHeight) / 2);
                change.Location = new Point(x + leftW - cardPad - change.Width, cy + (rowH - change.Height) / 2);
                int vx = x + cardPad + S(130);
                v.AutoSize = false;
                v.AutoEllipsis = true;
                v.Bounds = new Rectangle(vx, cy + (rowH - v.PreferredHeight) / 2, change.Left - vx - S(8), v.PreferredHeight);
                if (i > 0) panel.Rules.Add(new Rectangle(x + cardPad, cy, leftW - 2 * cardPad, 1));
            }

            // The map, the name, and what the write will touch.
            int ty = y;
            int mapH = rightW / 2;
            _reviewMap.Bounds = new Rectangle(rightX, ty, rightW, mapH);
            _reviewMap.Chip = _choices.Relief == QuickRelief.Standard
                ? $"{CurrentType?.Title}  ·  seed {_choices.Seed}"
                : $"{CurrentType?.Title}  ·  {_choices.Relief}  ·  seed {_choices.Seed}";
            ty += mapH + S(18);
            _nameCaption.Location = new Point(rightX, ty);
            ty += _nameCaption.PreferredHeight + S(4);
            _name.Bounds = new Rectangle(rightX, ty, rightW, _name.PreferredHeight);
            ty += _name.Height + S(6);
            _path.Bounds = new Rectangle(rightX, ty, rightW, TextRenderer.MeasureText("Ag", _path.Font).Height + S(2));
            ty += _path.Height + S(2);
            _nameNote.Bounds = new Rectangle(rightX, ty, rightW - _changeFolder.Width - S(8), Wrapped(_nameNote, rightW - _changeFolder.Width - S(8)));
            _changeFolder.Location = new Point(rightX + rightW - _changeFolder.Width, ty - S(4));
            ty += Math.Max(_nameNote.Height, _changeFolder.Height) + S(14);
            _gameLine.Bounds = new Rectangle(rightX, ty, rightW - (_gameFix.Visible ? _gameFix.Width + S(8) : 0), Wrapped(_gameLine, rightW - (_gameFix.Visible ? _gameFix.Width + S(8) : 0)));
            _gameFix.Location = new Point(rightX + rightW - _gameFix.Width, ty - S(4));
            ty += _gameLine.Height + S(10);
            _complexNote.Bounds = new Rectangle(rightX, ty, rightW, string.IsNullOrEmpty(_complexNote.Text) ? 0 : Wrapped(_complexNote, rightW));
        };
    }

    private void RefreshReview()
    {
        var values = SummaryValues();
        for (int i = 0; i < _summary.Count; i++) _summary[i].Value.Text = values[i];
        SuggestName();
        UpdateNameState();
        _reviewPanel.PerformLayout();
    }

    /// <summary>
    /// The name follows the map and seed until it is typed over, so a fresh world never lands on
    /// the folder of the last one by accident.
    /// </summary>
    private void SuggestName()
    {
        if (_nameTouched) return;
        _settingName = true;
        _name.Text = $"{CurrentType?.Title ?? "World"} {_choices.Seed}";
        _settingName = false;
    }

    private bool _nameOk;

    private bool CanCreate => _nameOk && _gameFound;

    /// <summary>The same checks the Write mod dialog makes, shown as the name is typed.</summary>
    private void UpdateNameState()
    {
        string folder = ModNameDialog.FolderName(_name.Text);
        if (folder.Length == 0)
        {
            _path.Text = "";
            SetNote("Give the mod a name.", Theme.Danger);
            _nameOk = false;
        }
        else
        {
            string dir = Path.Combine(_modRoot, folder);
            _path.Text = dir;
            _nameOk = true;

            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() && !Core.RunLog.WroteFolder(dir))
            {
                SetNote("That folder holds something this tool did not write. Choose another name.", Theme.Danger);
                _nameOk = false;
            }
            else if (Directory.Exists(dir))
            {
                SetNote("A mod this tool wrote is already there. Creating replaces it.", Theme.Danger);
            }
            else
            {
                SetNote("A new mod, listed in the launcher under this name.", Theme.TextDim);
            }
        }

        if (_step == ReviewStep && _mode == Mode.Steps) _next.Enabled = CanCreate;
        _reviewPanel.PerformLayout();
    }

    private void SetNote(string text, Color color)
    {
        _nameNote.Text = text;
        _nameNote.ForeColor = color;
    }

    private void PickModRoot()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Where the mod folder is created — normally the launcher's mod folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_modRoot) ? _modRoot : "",
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        _modRoot = dialog.SelectedPath;
        UpdateNameState();
    }

    // ================================================================ run and done

    private void BuildRunView()
    {
        _runPanel.Controls.AddRange([_runTitle, _runSubtitle, _runMap, _bar, _percent, _eta, _phase, _cancel, _milestones]);
        _cancel.Click += (_, _) => CancelRequested?.Invoke();

        _runPanel.Arrange = panel =>
        {
            var (x, w) = Column(panel);
            int y = S(18);
            _runTitle.Location = new Point(x - S(2), y);
            y += _runTitle.PreferredHeight + S(2);
            _runSubtitle.Location = new Point(x, y);
            y += _runSubtitle.PreferredHeight + S(18);

            // The map on the left, the discoveries in a column on the right.
            int feedW = S(310), gap = S(24);
            int left = w - feedW - gap;
            PlaceFeed(panel, x + left + gap, y, feedW);

            int mapH = Math.Min(left / 2, panel.ClientSize.Height - y - S(100));
            int mapW = mapH * 2;
            int mx = x;
            _runMap.Bounds = new Rectangle(mx, y, mapW, mapH);
            y += mapH + S(20);
            _bar.Bounds = new Rectangle(mx, y, mapW, S(8));
            y += S(8) + S(12);
            _percent.Location = new Point(mx, y);
            _eta.Location = new Point(_percent.Right + S(12), y + (_percent.PreferredHeight - _eta.PreferredHeight) / 2);
            _cancel.Location = new Point(mx + mapW - _cancel.Width, y - S(2));
            y += _percent.PreferredHeight + S(2);
            _phase.Location = new Point(mx, y);
            y += _phase.PreferredHeight + S(16);
            _milestones.Bounds = new Rectangle(mx, y, mapW, _milestones.PreferredGridHeight);
        };
    }

    private void BuildDoneView()
    {
        _donePanel.Controls.AddRange([_doneTitle, _doneSubtitle, _donePath, _doneMap, _launch, _openFolder, _customize, _another, _retry, _details,
            _talliesTitle, _tallies]);
        _launch.Click += (_, _) => LaunchRequested?.Invoke();
        _openFolder.Click += (_, _) => OpenFolderRequested?.Invoke();
        _customize.Click += (_, _) => CustomizeRequested?.Invoke();
        _details.Click += (_, _) => CustomizeRequested?.Invoke();
        _another.Click += (_, _) =>
        {
            _choices.Seed = Random.Shared.Next(1, 1_000_000);
            _previousSeeds.Clear();
            _nameTouched = false;
            SyncControls();
            Go(MapStep, resetReached: true);
            RenderPreview();
        };
        _retry.Click += (_, _) => Go(ReviewStep);

        _donePanel.Arrange = panel =>
        {
            var (x, w) = Column(panel);
            int y = S(18);
            _doneTitle.Location = new Point(x - S(2), y);
            y += _doneTitle.PreferredHeight + S(4);
            _doneSubtitle.Bounds = new Rectangle(x, y, w, Wrapped(_doneSubtitle, w));
            y += _doneSubtitle.Height + S(2);
            int pathH = string.IsNullOrEmpty(_donePath.Text) ? 0 : TextRenderer.MeasureText("Ag", _donePath.Font).Height + S(2);
            _donePath.Bounds = new Rectangle(x, y, w, pathH);
            y += pathH + S(14);

            // The world on the left with what to do next under it; everything discovered on the right.
            int feedW = S(310), gap = S(24);
            int left = w - feedW - gap;
            PlaceFeed(panel, x + left + gap, y, feedW);

            var buttons = (_failed ? (Control[])[_retry, _details] : [_launch, _openFolder, _customize, _another]).ToList();
            foreach (var b in buttons) if (b is PillButton p) p.FitWidth();

            // Buttons flow into as many rows as the map's width needs.
            var rows = new List<List<Control>> { new() };
            int rowW = 0;
            foreach (var b in buttons)
            {
                int need = b.Width + (rows[^1].Count > 0 ? S(10) : 0);
                if (rows[^1].Count > 0 && rowW + need > left) { rows.Add([]); rowW = 0; need = b.Width; }
                rows[^1].Add(b);
                rowW += need;
            }

            int buttonsH = rows.Count * S(36) + (rows.Count - 1) * S(10);
            bool tallies = _tallies.PreferredGridHeight > 0;
            int talliesH = tallies ? _talliesTitle.PreferredHeight + S(8) + _tallies.PreferredGridHeight + S(18) : 0;
            int mapH = Math.Min(left / 2, panel.ClientSize.Height - y - buttonsH - talliesH - S(34));
            int mapW = mapH * 2;
            _doneMap.Bounds = new Rectangle(x, y, mapW, mapH);
            y += mapH + S(18);

            foreach (var row in rows)
            {
                int bx = x;
                foreach (var b in row)
                {
                    b.Location = new Point(bx, y);
                    bx += b.Width + S(10);
                }
                y += S(36) + S(10);
            }

            _talliesTitle.Visible = _tallies.Visible = tallies;
            if (tallies)
            {
                y += S(8);
                _talliesTitle.Location = new Point(x, y);
                y += _talliesTitle.PreferredHeight + S(8);
                _tallies.Bounds = new Rectangle(x, y, mapW, _tallies.PreferredGridHeight);
            }
        };
    }

    /// <summary>
    /// Puts the discoveries column in whichever of the run and done views is laying out. One feed
    /// serves both, moved across when the run ends, so everything found during it is still there.
    /// </summary>
    private void PlaceFeed(StepPanel panel, int x, int y, int width)
    {
        if (!ReferenceEquals(_feed.Parent, panel)) return;
        _feedTitle.Location = new Point(x, y);
        _feedCount.Location = new Point(x + width - _feedCount.PreferredWidth, y + (_feedTitle.PreferredHeight - _feedCount.PreferredHeight) / 2);
        int top = y + _feedTitle.PreferredHeight + S(8);
        _feed.Bounds = new Rectangle(x, top, width, Math.Max(S(60), panel.ClientSize.Height - top - S(10)));
    }

    /// <summary>The run is over: everything still queued is shown at once, and the column moves across.</summary>
    private void HandFeedToDone(string title)
    {
        _milestones.Finish(completed: !_failed);
        _feed.Flush();
        _feed.EmptyText = "Nothing was shown for this run.";
        _feedTitle.Text = title;
        MoveFeedTo(_donePanel);
        UpdateFeedCount();
    }

    private void MoveFeedTo(StepPanel panel)
    {
        panel.Controls.Add(_feedTitle);
        panel.Controls.Add(_feedCount);
        panel.Controls.Add(_feed);
        panel.PerformLayout();
    }

    /// <summary>A stage the generator entered, for the milestones under the map.</summary>
    public void EnterStage(string stage)
    {
        if (_mode == Mode.Running) _milestones.Enter(stage);
    }

    /// <summary>The finished world counted, for the "At a glance" row. Empty hides the row.</summary>
    public void SetTallies(IReadOnlyList<(string Label, int Count)> tallies)
    {
        _tallies.Set(tallies);
        _donePanel.PerformLayout();
    }

    /// <summary>Something the generator just made; see <see cref="ShowcaseFeed"/>.</summary>
    public void OfferShowcase(Core.ShowcaseItem item)
    {
        _feed.Offer(item);
        UpdateFeedCount();
    }

    private void UpdateFeedCount()
    {
        int n = _feed.Count;
        _feedCount.Text = n == 0 ? "" : n == 1 ? "1 so far" : $"{n} so far";
        if (_mode == Mode.Done) _feedCount.Text = n == 0 ? "" : $"{n} in all";
        if (_feed.Parent is StepPanel panel) panel.PerformLayout();
    }

    // ================================================================ layout plumbing

    /// <summary>
    /// A step's area. Its children are placed by <see cref="Arrange"/> on every layout, and it draws
    /// the white cards and hairlines the arrangement asks for underneath them.
    /// </summary>
    private sealed class StepPanel : Panel
    {
        public Action<StepPanel>? Arrange;
        public readonly List<Rectangle> Cards = [];
        public readonly List<Rectangle> Rules = [];

        public StepPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            Visible = false;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Cards.Clear();
            Rules.Clear();
            Arrange?.Invoke(this);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            foreach (var card in Cards)
            {
                using var path = Rounded(new RectangleF(card.X + 0.5f, card.Y + 0.5f, card.Width - 1, card.Height - 1), LaunchUi.S(this, 12));
                using var fill = new SolidBrush(Theme.Surface);
                g.FillPath(fill, path);
                using var pen = new Pen(Theme.Border);
                g.DrawPath(pen, path);
            }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            using var rule = new SolidBrush(Color.FromArgb(232, 236, 242));
            foreach (var r in Rules) g.FillRectangle(rule, r);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _previewCts?.Cancel();
        base.Dispose(disposing);
    }
}
