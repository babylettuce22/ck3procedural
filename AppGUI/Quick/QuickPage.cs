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
/// <see cref="Run"/>, the run and done views it shares with the Azgaar page. That is why
/// "Customize in Complex" can hand over the very same world.
/// </summary>
internal sealed class QuickPage : Panel
{
    public event Action? BackToStart;
    public event Action? CreateRequested;
    public event Action? CancelRequested;
    public event Action? LaunchRequested;
    public event Action? OpenFolderRequested;
    public event Action? CustomizeRequested;
    public event Action? See3DRequested;
    public event Action? GameFolderRequested;

    // the history run on after the world is written; see RunScreen's history view
    public event Action? PlayPauseRequested;
    public event Action? PaceRequested;
    public event Action? AcceptRequested;
    public event Action? ContinueHistoryRequested;

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

    // map step
    private readonly List<(QuickMapType Type, MapTile Tile)> _tiles = [];
    private readonly TileStrip _tileStrip = new() { Name = "quickTypes" };
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
    private readonly WrappingToolTip _reliefTips = new() { InitialDelay = 400 };

    // The mountain style: a small, secondary switch under Relief. See QuickMountains.
    private readonly Label _mountainsCaption = MakeLabel("Mountains", Small, Theme.TextDim);
    private readonly Dictionary<QuickMountains, Theme.SegmentButton> _mountainButtons = new()
    {
        [QuickMountains.Ranges] = new() { Text = "Ranges", Name = "quickMountainsRanges" },
        [QuickMountains.Classic] = new() { Text = "Classic", Name = "quickMountainsClassic" },
    };
    private FlowLayoutPanel? _mountainsTrack;
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
    private readonly ChoiceGroup<QuickFantasy> _fantasy;
    private readonly ChoiceGroup<QuickPolitics> _politics;
    private readonly ChoiceGroup<GenderPreference> _rulers;
    // Beside the Fantasy heading while the world has races: opens the race mix (RaceMixDialog).
    private readonly TextLink _mixLink = new() { Name = "quickRaceMix", LinkFont = Small, Glyph = "", AccessibleName = "Race mix" };
    // Beside the Cultures & faiths heading while the peoples are invented: opens the inspiration
    // (InspirationDialog), the corner of the real world they are modelled on.
    private readonly TextLink _inspirationLink = new() { Name = "quickInspiration", LinkFont = Small, Glyph = "", AccessibleName = "Inspiration" };
    private const string InventedSubtitle = "New cultures, faiths, languages and names";
    private static readonly Dictionary<QuickFantasy, string> FantasySubtitles = new()
    {
        [QuickFantasy.None] = "Humans only, as in vanilla",
        [QuickFantasy.Low] = "Mostly human, a few other races",
        [QuickFantasy.High] = "Elves, dwarves, orcs and more",
    };
    private readonly ToggleCard _wilderness = new() { Name = "quickWilderness", Text = "Wilderness", Description = "Unsettled lands to clear, claim and colonise." };
    private readonly ToggleCard _wars = new() { Name = "quickWars", Text = "Wars at the start", Description = "Rivals already at war on the first day." };
    private readonly ToggleCard _nativeTitles = new() { Name = "quickNativeTitles", Text = "Native titles", Description = "Kings and dukes titled in their people's own language, and sees and archbishops in their faith's holy tongue." };
    private readonly ToggleCard _nativeRealms = new() { Name = "quickNativeRealms", Text = "Native realm names", Description = "Kingdoms and duchies named in it too." };
    private readonly ToggleCard _detailedPaperMap = new() { Name = "quickDetailedPaperMap", Text = "Detailed paper map", Description = "Roads, mountains, waterlines and a compass rose inked on the zoomed-out map." };

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

    // the run and the finished world, shared with the Azgaar page
    private readonly RunScreen _run = new("quick");

    public QuickPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        Name = "quickPage";

        _size = new ChoiceGroup<QuickSize>("Size", "How big the map is. Bigger worlds take longer to make.", QuickSize.Standard)
            .Add(QuickSize.Small, "Small", "4096 × 2048 · quickest to make", "quickSizeSmall")
            .Add(QuickSize.Standard, "Standard", "8192 × 4096 · recommended", "quickSizeStandard")
            .Add(QuickSize.Large, "Large", "9216 × 4608 · half of vanilla", "quickSizeLarge")
            .Add(QuickSize.Vanilla, "Vanilla", "18432 × 9216 · full vanilla size, slowest to make", "quickSizeVanilla")
            .Add(QuickSize.Custom, "Custom", "", "quickSizeCustom");
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
            .Add(QuickPeople.Invented, "Invented", InventedSubtitle, "quickPeopleInvented")
            .Add(QuickPeople.RealCk3, "Real CK3", "Vanilla's cultures and faiths laid onto this map", "quickPeopleReal");
        _fantasy = new ChoiceGroup<QuickFantasy>("Fantasy", "Whether other races share the world with humans.", QuickFantasy.None)
            .Add(QuickFantasy.None, "None", FantasySubtitles[QuickFantasy.None], "quickFantasyNone")
            .Add(QuickFantasy.Low, "Low fantasy", FantasySubtitles[QuickFantasy.Low], "quickFantasyLow")
            .Add(QuickFantasy.High, "High fantasy", FantasySubtitles[QuickFantasy.High], "quickFantasyHigh");
        _politics = new ChoiceGroup<QuickPolitics>("Politics", "How the realms stand on the first day.", QuickPolitics.Kingdoms)
            .Add(QuickPolitics.Fragmented, "Fragmented", "Every count rules alone", "quickPoliticsFragmented")
            .Add(QuickPolitics.Kingdoms, "Kingdoms", "Realms great and small", "quickPoliticsKingdoms")
            .Add(QuickPolitics.Hegemony, "Hegemony", "One realm claims the whole world", "quickPoliticsHegemony");
        _rulers = new ChoiceGroup<GenderPreference>("Rulers", "Who tends to hold land and titles.", GenderPreference.Historical)
            .Add(GenderPreference.Historical, "Historical", "Mostly men, as in vanilla", "quickRulersHistorical")
            .Add(GenderPreference.Mixed, "Mixed", "Varies from faith to faith", "quickRulersMixed")
            .Add(GenderPreference.FemaleDominated, "Women rule", "Women hold the land and titles", "quickRulersWomen")
            .Add(GenderPreference.Equal, "Equal", "Men and women alike, everywhere", "quickRulersEqual");

        _size.Changed += v =>
        {
            // Custom asks for its size every time it is picked, so picking it again edits it.
            if (v == QuickSize.Custom && !EditCustomSize())
            {
                _size.Value = _choices.Size;
        ShowCustomSize();
                return;
            }
            _choices.Size = v;
        };
        _era.Changed += v => _choices.Era = v;
        _climate.Changed += v => _choices.Climate = v;
        _density.Changed += v => _choices.Density = v;
        _peopleGroup.Changed += v =>
        {
            _choices.People = v;
            ShowNativeToggles();
            ShowInspiration();
            ShowFantasy();
        };
        _fantasy.Changed += v =>
        {
            // A new level brings its own human share; the races' weights are taste and stay.
            // One left at its defaults by that is no mix at all.
            if (v != _choices.Fantasy && _choices.Mix is { } mix)
            {
                mix.HumanShare = 0;
                if (mix.IsDefault) _choices.Mix = null;
            }
            _choices.Fantasy = v;
            ShowFantasy();
        };
        _mixLink.Click += (_, _) => EditRaceMix();
        _inspirationLink.Click += (_, _) => EditInspiration();
        _politics.Changed += v => _choices.Politics = v;
        _rulers.Changed += v => _choices.Rulers = v;
        _wilderness.Toggled += on => _choices.Wilderness = on;
        _wars.Toggled += on => _choices.Wars = on;
        _nativeTitles.Toggled += on => _choices.NativeTitles = on;
        _detailedPaperMap.Toggled += on => _choices.DetailedPaperMap = on;
        _nativeRealms.Toggled += on => _choices.NativeRealms = on;

        BuildChrome();
        BuildMapStep();
        BuildGroupsStep(_worldPanel, "Shape the world", "Size, era and climate. The defaults make a good first world.",
            [_size, _era, _climate, _density], toggles: null);
        BuildGroupsStep(_peoplePanel, "People and politics", "Who lives here, who rules, and what else the world holds.",
            [_peopleGroup, _fantasy, _politics, _rulers], toggles: [_wilderness, _wars, _nativeTitles, _nativeRealms, _detailedPaperMap],
            headingLinks: new()
            {
                [_peopleGroup] = (_inspirationLink, () => InspirationLinkShown),
                [_fantasy] = (_mixLink, () => MixLinkShown),
            });
        BuildReviewStep();
        WireRunScreen();

        foreach (var panel in (StepPanel[])[_mapPanel, _worldPanel, _peoplePanel, _reviewPanel])
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
    public QuickChoices Choices => _choices.Clone();

    public string ModRoot => _modRoot;
    public string ModName => _name.Text.Trim();
    public string ModDir => Path.Combine(_modRoot, ModNameDialog.FolderName(_name.Text));

    /// <summary>The run and done views; the main window reports the run to it directly.</summary>
    public RunScreen Run => _run;

    /// <summary>
    /// The size the page wants to be shown at: the column, its margins, and the tallest step. That
    /// is People since it gained the Fantasy row, with every group shown and every extra on.
    /// </summary>
    public Size PreferredPageSize => new(S(StepPanel.PreferredColumn) + 2 * S(32), S(60) + 1 + S(616) + 1 + S(68));

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
        SetGameFolder(gameFound, gameDir);

        SyncControls();
        Go(MapStep, resetReached: true);
        StartThumbnails();
        RenderPreview();
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

    /// <summary>Swaps the steps for the run view. The picture starts as the preview just approved.</summary>
    public void ShowRunning()
    {
        _mode = Mode.Running;
        var type = CurrentType;
        var (w, h) = _choices.Pixels;
        _run.ShowRunning("Making your world",
            $"{type?.Title ?? _choices.MapType} · seed {_choices.Seed} · {w} × {h}. This takes a few minutes.",
            _preview.Image is { } img ? new Bitmap(img) : null,
            "Raising the land",
            "Peoples, faiths, armies and treasures appear here as the world is made. The land comes first.");
        ShowView(_run);
        UpdateChrome();
    }

    /// <summary>The run finished and the mod is on disk.</summary>
    /// <param name="history">What the history left the world as, for the done screen's line; see <see cref="RunScreen.ShowDone"/>.</param>
    public void ShowDone(string modDir, TimeSpan took, string? history = null)
    {
        _mode = Mode.Done;
        _run.ShowDone(ModName, modDir, took, history);
        ShowView(_run);
        UpdateChrome();
    }

    /// <summary>The mod is on disk and its history is running on; see <see cref="RunScreen.ShowHistory"/>.</summary>
    public void ShowHistory(int began)
    {
        _mode = Mode.Done;
        _run.ShowHistory(began);
        ShowView(_run);
        UpdateChrome();
    }

    /// <summary>
    /// The world is being written again as the history left it: the page cannot be left until it
    /// is, the way it cannot during the first run.
    /// </summary>
    public void SetWriting(bool writing)
    {
        _mode = writing ? Mode.Running : Mode.Done;
        UpdateChrome();
    }

    /// <summary>The run stopped short: cancelled, or failed.</summary>
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
        _run.See3DRequested += () => See3DRequested?.Invoke();
        _run.RetryRequested += () => Go(ReviewStep);
        _run.PlayPauseRequested += () => PlayPauseRequested?.Invoke();
        _run.PaceRequested += () => PaceRequested?.Invoke();
        _run.AcceptRequested += () => AcceptRequested?.Invoke();
        _run.ContinueHistoryRequested += () => ContinueHistoryRequested?.Invoke();
        _run.AnotherRequested += () =>
        {
            _choices.Seed = Random.Shared.Next(1, 1_000_000);
            _previousSeeds.Clear();
            _nameTouched = false;
            SyncControls();
            Go(MapStep, resetReached: true);
            RenderPreview();
        };
    }

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

    private static (int X, int Width) Column(Control host) => StepPanel.Column(host);

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
        _run.Reset();
        _step = Math.Clamp(step, 0, ReviewStep);
        _reached = resetReached ? _step : Math.Max(_reached, _step);

        if (_step == ReviewStep) RefreshReview();

        ShowView(_step switch
        {
            MapStep => _mapPanel,
            WorldStep => _worldPanel,
            PeopleStep => _peoplePanel,
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

    /// <summary>
    /// Everything re-drawn at random except the choices that decide how long the run takes or
    /// what the game needs installed: size, density and where the people come from stay put.
    /// </summary>
    private void Surprise()
    {
        var kept = _choices;
        _choices = QuickChoices.Surprise(Random.Shared, _types);
        _choices.Size = kept.Size;
        _choices.CustomWidth = kept.CustomWidth;
        _choices.CustomHeight = kept.CustomHeight;
        _choices.Density = kept.Density;
        _choices.People = kept.People;
        // A matter of taste rather than of the world, so a surprise keeps what the player chose.
        _choices.NativeTitles = kept.NativeTitles;
        _choices.NativeRealms = kept.NativeRealms;
        _choices.DetailedPaperMap = kept.DetailedPaperMap;
        _choices.Mountains = kept.Mountains;
        // So is fantasy: a surprise should not put elves into a player's historical game. Nor
        // should it throw away a race mix set by hand.
        _choices.Fantasy = kept.Fantasy;
        _choices.Mix = kept.Mix;
        // And the inspiration. The climate drawn for the surprise is then one its wardrobe covers,
        // so a Norse world is not surprised into the tropics.
        _choices.Inspiration = kept.Inspiration;
        if (_choices.InspirationInWorld is { } inspiration && inspiration.Unsuits(_choices.Climate))
        {
            var suited = inspiration.SuitedClimates;
            _choices.Climate = suited[Random.Shared.Next(suited.Length)];
        }
        _previousSeeds.Push(kept.Seed);
        SyncControls();
        RenderPreview();
        if (_step == ReviewStep) RefreshReview();
        UpdateChrome();
    }

    /// <summary>
    /// Native titles only mean something for invented peoples — real CK3 cultures already have
    /// vanilla's words and no generated language — so the two switches are shown only then.
    /// Hiding a card re-lays the step, whose Extras row spreads over the visible cards only.
    /// </summary>
    private void ShowNativeToggles()
    {
        bool invented = _choices.People == QuickPeople.Invented;
        _nativeTitles.Visible = invented;
        _nativeRealms.Visible = invented;
    }

    /// <summary>
    /// Fantasy is only offered for invented peoples: vanilla's cultures are human, and the
    /// generator refuses races on them. Hidden rather than greyed, like the native switches, and
    /// the step closes up around it.
    /// </summary>
    private void ShowFantasy()
    {
        _fantasy.Shown = _choices.People == QuickPeople.Invented;

        // The race mix goes with races: offered beside the heading once Low or High is picked, and
        // named "custom" once set, with the chosen card saying so too.
        _mixLink.Text = _choices.MixInWorld is null ? "Race mix…" : "Custom race mix…";
        _mixLink.Visible = MixLinkShown;
        foreach (var (level, subtitle) in FantasySubtitles)
            _fantasy.SetSubtitle(level, level == _choices.Fantasy && _choices.MixInWorld is { } mix
                ? $"Custom mix  ·  {mix.HumanPercent(level)}% human"
                : subtitle);
        _peoplePanel.PerformLayout();
    }

    /// <summary>
    /// The inspiration goes with invented peoples: real CK3 ones already look, dress and speak as
    /// vanilla has them. The link names the inspiration once one is set, and the Invented card says
    /// so too, the way the race mix shows on the Fantasy card.
    /// </summary>
    private void ShowInspiration()
    {
        var inspiration = _choices.Inspiration;
        _inspirationLink.Text = inspiration is null ? "Inspiration…"
            : inspiration.MatchingPreset is { } preset ? $"Inspired by {preset.Name}…"
            : "Custom inspiration…";
        _inspirationLink.Visible = InspirationLinkShown;
        _peopleGroup.SetSubtitle(QuickPeople.Invented, inspiration is null ? InventedSubtitle
            : inspiration.MatchingPreset is { } p ? $"New peoples inspired by {p.Name}"
            : $"New peoples  ·  {inspiration.Describe()}");
        _peoplePanel.PerformLayout();
    }

    /// <summary>Whether the inspiration link is on the step: only for invented peoples.</summary>
    private bool InspirationLinkShown => _choices.People == QuickPeople.Invented;

    /// <summary>
    /// Opens the inspiration over the page. Anywhere is dropped rather than kept, so the world is
    /// exactly the one Quick makes without it. The dialog may also bring back the climate its note
    /// offered, which the World step then shows.
    /// </summary>
    private void EditInspiration()
    {
        if (!InspirationLinkShown) return;

        using var dialog = new InspirationDialog(_choices.Inspiration ?? new QuickInspiration(), _choices.Climate,
            races: _choices.FantasyInWorld != QuickFantasy.None);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        _choices.Inspiration = dialog.Inspiration.IsDefault ? null : dialog.Inspiration;
        _choices.Climate = dialog.Climate;
        _climate.Value = dialog.Climate;
        ShowInspiration();
    }

    /// <summary>Whether the race mix link is on the step: only for a world with races.</summary>
    private bool MixLinkShown => _fantasy.Shown && _choices.FantasyInWorld != QuickFantasy.None;

    /// <summary>
    /// Opens the race mix over the page. A mix brought back to its defaults is dropped rather than
    /// kept, so the world is exactly the one the fantasy choice alone makes.
    /// </summary>
    private void EditRaceMix()
    {
        var level = _choices.FantasyInWorld;
        if (level == QuickFantasy.None) return;

        using var dialog = new RaceMixDialog(_choices.Mix ?? new QuickRaceMix(), level);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        _choices.Mix = dialog.Mix.IsDefault ? null : dialog.Mix;
        ShowFantasy();
    }

    /// <summary>
    /// Opens the Custom size over the page. False when it was cancelled, which leaves the size
    /// that was picked before.
    /// </summary>
    private bool EditCustomSize()
    {
        using var dialog = new CustomSizeDialog(_choices.CustomWidth, _choices.CustomHeight);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return false;
        (_choices.CustomWidth, _choices.CustomHeight) = dialog.Pixels;
        ShowCustomSize();
        return true;
    }

    /// <summary>The Custom card's line: its size, and whether CK3 is known to render it.</summary>
    private void ShowCustomSize()
    {
        var (w, h) = QuickChoices.SnapCustom(_choices.CustomWidth, _choices.CustomHeight);
        _size.SetSubtitle(QuickSize.Custom, $"{w} × {h} · "
            + (MapGen.TileFit.Fits(w, h) ? "your own pick" : "untested in CK3"));
        _worldPanel.PerformLayout();
    }

    /// <summary>Puts every control in step with <see cref="_choices"/>.</summary>
    private void SyncControls()
    {
        foreach (var (type, tile) in _tiles) tile.Selected = type.Key == _choices.MapType;
        if (_tiles.FirstOrDefault(t => t.Tile.Selected).Tile is { } picked) _tileStrip.EnsureVisible(picked);
        _size.Value = _choices.Size;
        _era.Value = _choices.Era;
        _climate.Value = _choices.Climate;
        _density.Value = _choices.Density;
        _peopleGroup.Value = _choices.People;
        ShowInspiration();
        _fantasy.Value = _choices.Fantasy;
        ShowFantasy();
        _politics.Value = _choices.Politics;
        _rulers.Value = _choices.Rulers;
        _wilderness.On = _choices.Wilderness;
        _wars.On = _choices.Wars;
        _nativeTitles.On = _choices.NativeTitles;
        _nativeRealms.On = _choices.NativeRealms;
        _detailedPaperMap.On = _choices.DetailedPaperMap;
        ShowNativeToggles();
        _seedBox.Text = _choices.Seed.ToString();
        foreach (var (relief, button) in _reliefButtons) Theme.StyleSegment(button, relief == _choices.Relief);
        foreach (var (style, button) in _mountainButtons)
        {
            Theme.StyleSegment(button, style == _choices.Mountains);
            button.Font = Small;
        }
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
        _mapPanel.Controls.AddRange([title, subtitle, _tileStrip, _preview, _typeName, _typeBlurb, _seedCaption, _seedBox, _reroll, _previous, _mapHint]);

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

        _mountainsTrack = Theme.MakeSegmented(_mountainButtons.Values);
        _mountainsTrack.Margin = new Padding(0);
        _mapPanel.Controls.Add(_mountainsCaption);
        _mapPanel.Controls.Add(_mountainsTrack);
        _reliefTips.SetToolTip(_mountainButtons[QuickMountains.Ranges], "Peaks rise from ridged ranges and keep their slopes all the way up.");
        _reliefTips.SetToolTip(_mountainButtons[QuickMountains.Classic], "The earlier tuning: the highest ground is cut flat into broad snow-capped tables.");
        foreach (var (style, button) in _mountainButtons)
            button.Click += (_, _) => PickMountains(style);

        foreach (var type in _types)
        {
            var tile = new MapTile { Text = type.Title, Name = "quickType-" + type.Key, AccessibleName = type.Title };
            tile.Click += (_, _) => PickType(type);
            new WrappingToolTip { InitialDelay = 400 }.SetToolTip(tile, type.Blurb);
            _tiles.Add((type, tile));
            _tileStrip.Add(tile);
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
            y += StepPanel.Place(subtitle, x, y, w) + S(16);

            // The tiles span the column at the size six of them would take; a tile's picture is as
            // tall as half its width, so past a point they stop growing and spread apart instead,
            // and the preview keeps its height. More types than fit scroll sideways (TileStrip).
            // A name too long for one line takes two, on every tile, so the row stays even.
            int stripH = _tileStrip.Measure(w);
            _tileStrip.Bounds = new Rectangle(x, y, w, stripH);
            if (_tiles.FirstOrDefault(t => t.Tile.Selected).Tile is { } chosen)
                _tileStrip.EnsureVisible(chosen, animate: false);
            y += stripH + S(18);

            // The seed and relief controls keep to the right edge at a readable width; the preview
            // takes the rest, as large as the height allows, centred in it.
            int sw = Math.Clamp(w * 21 / 100, S(200), S(280));
            int sx = x + w - sw;
            int area = sx - S(24) - x;
            var map = StepPanel.Map(x, y, area, panel.ClientSize.Height - y - S(12), S(StepPanel.MinMapHeight));
            map.X = x + (area - map.Width) / 2;
            _preview.Bounds = map;
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
                sy += track.Height + S(8);
            }

            // Secondary to Relief, so smaller: its caption sits on the same line, left of it.
            if (_mountainsTrack is { } styles)
            {
                int buttonH = S(20);
                int each = S(58);
                foreach (var button in _mountainButtons.Values) button.Size = new Size(each, buttonH);
                styles.AutoSize = false;
                int trackW = each * _mountainButtons.Count + S(4);
                styles.Bounds = new Rectangle(sx + sw - trackW, sy, trackW, buttonH + S(4));
                _mountainsCaption.Location = new Point(sx, sy + (styles.Height - _mountainsCaption.PreferredHeight) / 2);
                sy += styles.Height + S(14);
            }

            // In full, even when that runs below the preview: on a short window the view scrolls.
            StepPanel.Place(_mapHint, sx, sy, sw);
        };
    }

    private static int Wrapped(Label label, int width) => StepPanel.Wrapped(label, width);

    private void PickRelief(QuickRelief relief)
    {
        if (_choices.Relief == relief) return;
        _choices.Relief = relief;
        SyncControls();
        RenderPreview();
    }

    private void PickMountains(QuickMountains style)
    {
        if (_choices.Mountains == style) return;
        _choices.Mountains = style;
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
        string path = type.PresetPathFor(_choices.Mountains);
        _preview.Chip = MapChip(type);

        var feature = type.Feature;
        bool regional = _choices.RegionalRelief;
        Task.Run(() => ForgePreview.Render(path, seed, 1024, 512, cts.Token, relief, feature, regional)).ContinueWith(task =>
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

    /// <summary>The line on the map pictures: the type, whatever differs from the defaults, the seed.</summary>
    private string MapChip(QuickMapType? type)
    {
        var parts = new List<string> { type?.Title ?? _choices.MapType };
        if (_choices.Relief != QuickRelief.Standard) parts.Add(_choices.Relief.ToString());
        if (_choices.Mountains == QuickMountains.Classic) parts.Add("Classic mountains");
        parts.Add($"seed {_choices.Seed}");
        return string.Join("  ·  ", parts);
    }

    /// <summary>One small picture per map type, drawn once, one after another, at a fixed seed.</summary>
    private void StartThumbnails()
    {
        if (_thumbnailsStarted) return;
        _thumbnailsStarted = true;
        var work = _tiles.Select(t => (t.Type.PresetPath, t.Type.Feature, t.Tile)).ToList();

        Task.Run(() =>
        {
            foreach (var (path, feature, tile) in work)
            {
                var bitmap = ForgePreview.Render(path, ThumbnailSeed, 384, 192, CancellationToken.None,
                    feature: feature, regional: true);
                if (bitmap is null) continue;
                if (IsDisposed || !IsHandleCreated) { bitmap.Dispose(); return; }
                BeginInvoke(() => tile.Thumbnail = bitmap);
            }
        });
    }

    // ================================================================ choice steps

    /// <param name="headingLinks">A link to place at the right end of a group's heading, and
    /// whether it is showing: the Fantasy row's race mix.</param>
    private void BuildGroupsStep(StepPanel panel, string title, string subtitle, IChoiceGroup[] views, ToggleCard[]? toggles,
        Dictionary<IChoiceGroup, (TextLink Link, Func<bool> Shown)>? headingLinks = null)
    {
        var titleLabel = MakeLabel(title, Title, Theme.Text);
        var subtitleLabel = MakeLabel(subtitle, Subtitle, Theme.TextDim);
        panel.Controls.AddRange([titleLabel, subtitleLabel]);

        var headers = new List<(IChoiceGroup View, Label Title, Label Hint, List<ChoiceCard> Cards)>();
        foreach (var view in views)
        {
            var t = MakeLabel(view.Title, GroupTitle, Theme.Text);
            var h = MakeLabel(view.Hint, Small, Theme.TextDim);
            var cards = view.Cards.ToList();
            panel.Controls.Add(t);
            panel.Controls.Add(h);
            foreach (var card in cards) panel.Controls.Add(card);
            if (headingLinks?.TryGetValue(view, out var link) == true) panel.Controls.Add(link.Link);
            headers.Add((view, t, h, cards));
        }

        Label? extrasTitle = null, extrasHint = null;
        if (toggles is not null)
        {
            extrasTitle = MakeLabel("Extras", GroupTitle, Theme.Text);
            extrasHint = MakeLabel("Switch each on or off to suit the game you want.", Small, Theme.TextDim);
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
            y += StepPanel.Place(subtitleLabel, x, y, w) + S(18);

            // Every card in a row is as tall as the one with the most to say, so none is cut short.
            // A heading link takes the right end of the title's line, and the hint the room left.
            void Row(Label t, Label h, IReadOnlyList<Control> cards, TextLink? link = null)
            {
                int linkRoom = link is null ? 0 : link.Width + S(16);
                if (link is not null)
                    link.Location = new Point(x + w - link.Width, y + (t.PreferredHeight - link.Height) / 2);
                y = StepPanel.TitleAndHint(t, h, x, y, w - linkRoom) + S(6);
                int gap = S(12);
                int n = Math.Max(1, cards.Count);
                int cw = (w - gap * (n - 1)) / n;
                int cardH = cards.Count == 0 ? 0 : cards.Max(c => CardHeight(c, cw));
                for (int i = 0; i < cards.Count; i++)
                    cards[i].Bounds = new Rectangle(x + i * (cw + gap), y, i == n - 1 ? w - i * (cw + gap) : cw, cardH);
                y += cardH + S(16);
            }

            // A hidden group takes its heading with it, and the rows below close up.
            foreach (var (view, t, h, cards) in headers)
            {
                t.Visible = h.Visible = view.Shown;
                TextLink? link = null;
                if (headingLinks?.TryGetValue(view, out var entry) == true)
                {
                    // Read from the flag, not back off Visible: on a step not yet showing, Visible
                    // reports false whatever it was set to.
                    bool shown = entry.Shown();
                    entry.Link.Visible = shown;
                    if (shown) link = entry.Link;
                }
                if (view.Shown) Row(t, h, cards, link);
            }
            if (toggles is not null && extrasTitle is not null && extrasHint is not null)
                Row(extrasTitle, extrasHint, [.. toggles.Where(c => c.Visible)]);
        };
    }

    /// <summary>How tall a choice or toggle card needs to be at a width to show all it says.</summary>
    internal static int CardHeight(Control card, int width) => card switch
    {
        ChoiceCard choice => choice.HeightFor(width),
        ToggleCard toggle => toggle.HeightFor(width),
        _ => card.Height,
    };

    // ================================================================ review

    private static readonly Dictionary<QuickSize, string> SizeNames = new()
        { [QuickSize.Small] = "Small", [QuickSize.Standard] = "Standard", [QuickSize.Large] = "Large", [QuickSize.Vanilla] = "Vanilla", [QuickSize.Custom] = "Custom" };
    private static readonly Dictionary<QuickEra, string> EraNames = new()
        { [QuickEra.Early] = "Early medieval", [QuickEra.High] = "High medieval", [QuickEra.Late] = "Late medieval" };
    private static readonly Dictionary<QuickClimate, string> ClimateNames = new()
        { [QuickClimate.Northern] = "Northern", [QuickClimate.Temperate] = "Temperate", [QuickClimate.Warm] = "Warm", [QuickClimate.Globe] = "Whole globe" };
    private static readonly Dictionary<QuickDensity, string> DensityNames = new()
        { [QuickDensity.Fewer] = "Fewer, larger counties", [QuickDensity.Balanced] = "Balanced", [QuickDensity.More] = "Many, smaller counties" };
    private static readonly Dictionary<QuickPeople, string> PeopleNames = new()
        { [QuickPeople.Invented] = "Invented", [QuickPeople.RealCk3] = "Real CK3 cultures and faiths" };
    private static readonly Dictionary<QuickFantasy, string> FantasyNames = new()
        { [QuickFantasy.None] = "None  ·  humans only", [QuickFantasy.Low] = "Low fantasy  ·  mostly human", [QuickFantasy.High] = "High fantasy  ·  many races" };
    private static readonly Dictionary<QuickPolitics, string> PoliticsNames = new()
        { [QuickPolitics.Fragmented] = "Fragmented", [QuickPolitics.Kingdoms] = "Kingdoms", [QuickPolitics.Hegemony] = "Hegemony" };
    private static readonly Dictionary<GenderPreference, string> RulerNames = new()
        { [GenderPreference.Historical] = "Historical", [GenderPreference.Mixed] = "Mixed", [GenderPreference.FemaleDominated] = "Women rule", [GenderPreference.Equal] = "Equal everywhere" };

    private (string Key, int Step)[] SummaryRows =>
    [
        ("Map", MapStep), ("Relief", MapStep), ("Size", WorldStep), ("Era", WorldStep), ("Climate", WorldStep), ("Provinces", WorldStep),
        ("Cultures & faiths", PeopleStep), ("Fantasy", PeopleStep), ("Politics", PeopleStep), ("Rulers", PeopleStep), ("Extras", PeopleStep),
    ];

    private string[] SummaryValues()
    {
        var (w, h) = _choices.Pixels;
        var extras = new List<string>();
        if (_choices.Wilderness) extras.Add("Wilderness");
        if (_choices.Wars) extras.Add("Wars at the start");
        if (_choices.People == QuickPeople.Invented && _choices.NativeTitles) extras.Add("Native titles");
        if (_choices.People == QuickPeople.Invented && _choices.NativeRealms) extras.Add("Native realm names");
        extras.Add(_choices.DetailedPaperMap ? "Detailed paper map" : "Plain paper map");
        return
        [
            $"{CurrentType?.Title ?? _choices.MapType}  ·  seed {_choices.Seed}",
            _choices.Relief switch
            {
                QuickRelief.Lowlands => "Lowlands  ·  fewer hills and mountains",
                QuickRelief.Highlands => "Highlands  ·  more hills and mountains",
                _ => "Standard",
            } + (_choices.Mountains == QuickMountains.Classic ? "  ·  classic mountains" : ""),
            $"{SizeNames[_choices.Size]}  ·  {w} × {h}" + (_choices.SizeVerified ? "" : "  ·  untested in CK3"),
            $"{EraNames[_choices.Era]}  ·  {_choices.StartYear}",
            ClimateNames[_choices.Climate],
            DensityNames[_choices.Density],
            _choices.InspirationInWorld is { } inspiration
                ? $"Invented  ·  {(inspiration.MatchingPreset is null ? inspiration.Describe() : $"inspired by {inspiration.Describe()}")}"
                : PeopleNames[_choices.People],
            _choices.MixInWorld is { } mix
                ? $"{(_choices.FantasyInWorld == QuickFantasy.Low ? "Low" : "High")} fantasy, custom mix  ·  {mix.Describe(_choices.FantasyInWorld)}"
                : FantasyNames[_choices.FantasyInWorld],
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
            y += StepPanel.Place(subtitle, x, y, w) + S(18);

            int gap = S(28);
            int leftW = (w - gap) * 52 / 100;
            int rightX = x + leftW + gap, rightW = x + w - rightX;

            // The summary, on a white card.
            int cardPad = S(16);
            int cardH = StepPanel.Summary(panel, _summary, x, y, leftW, cardPad, S(130));
            panel.Cards.Add(new Rectangle(x, y, leftW, cardH));

            // The map, the name, and what the write will touch.
            int ty = y;
            var map = StepPanel.Map(rightX, ty, rightW, Math.Max(S(120), panel.ClientSize.Height - ty - ReviewFootHeight(rightW)));
            _reviewMap.Bounds = map;
            _reviewMap.Chip = MapChip(CurrentType);
            ty += map.Height + S(18);
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
               + Math.Max(Wrapped(_nameNote, noteW), _changeFolder.Height) + S(14)
               + Wrapped(_gameLine, gameW) + S(10)
               + (string.IsNullOrEmpty(_complexNote.Text) ? 0 : Wrapped(_complexNote, rightW)) + S(12);
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
        if (disposing) _previewCts?.Cancel();
        base.Dispose(disposing);
    }
}

/// <summary>
/// The mod-folder checks both launcher pages make as the name is typed: the same ones the Write mod
/// dialog makes, worded for a page.
/// </summary>
internal static class ModFolderCheck
{
    /// <returns>
    /// Whether the name can be written to, the folder it names, the line to show under it, and
    /// whether that line is a warning.
    /// </returns>
    public static (bool Ok, string Dir, string Note, bool Danger) Check(string modRoot, string name)
    {
        string folder = ModNameDialog.FolderName(name);
        if (folder.Length == 0) return (false, "", "Give the mod a name.", true);

        string dir = Path.Combine(modRoot, folder);
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() && !Core.RunLog.WroteFolder(dir))
            return (false, dir, "That folder holds something this tool did not write. Choose another name.", true);
        if (Directory.Exists(dir))
            return (true, dir, "A mod this tool wrote is already there. Creating replaces it.", true);
        return (true, dir, "A new mod, listed in the launcher under this name.", false);
    }

    /// <summary>Asks where mod folders go. Null when the dialog was dismissed.</summary>
    public static string? PickRoot(IWin32Window? owner, string current)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Where the mod folder is created — normally the launcher's mod folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(current) ? current : "",
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
