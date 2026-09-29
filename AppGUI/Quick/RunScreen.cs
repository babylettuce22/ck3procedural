using System.Drawing.Drawing2D;
using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The end of every launcher page: the run you can watch, then the finished world with the obvious
/// next moves. Shared by the Quick and Azgaar pages, which differ only in how a world is chosen, so
/// both finish the same way.
///
/// Two views, one shown at a time. The run view has the map filling in, the progress, the
/// milestones under the map and a column of discoveries; the done view keeps that column (moved
/// across, so everything found during the run is still there), shows the realms as they stand at
/// the start, and counts the world.
///
/// It decides nothing. Its buttons raise events that the page, or through it the main window,
/// acts on.
/// </summary>
internal sealed class RunScreen : Panel
{
    public event Action? CancelRequested;
    public event Action? LaunchRequested;
    public event Action? OpenFolderRequested;
    public event Action? CustomizeRequested;
    public event Action? AnotherRequested;
    public event Action? RetryRequested;

    /// <summary>The history view's Pause / Resume.</summary>
    public event Action? PlayPauseRequested;

    /// <summary>The history view's pace switch.</summary>
    public event Action? PaceRequested;

    /// <summary>The world is to be written as it stands in the history view now.</summary>
    public event Action? AcceptRequested;

    /// <summary>
    /// The done view's history button: "Simulate its history" the first time, then "Continue its
    /// history", back to the history from where it stopped.
    /// </summary>
    public event Action? ContinueHistoryRequested;

    private enum Mode { Idle, Running, History, Done }
    private Mode _mode;
    private bool _failed;
    private bool _gameFound = true;

    /// <summary>The world has a history to run on; <see cref="_historyStarted"/> once it has been shown.</summary>
    private bool _historyOffered;
    private bool _historyStarted;
    private int _historyBegins;

    private readonly StepPanel _runPanel = new();
    private readonly StepPanel _historyPanel = new();
    private readonly StepPanel _donePanel = new();

    // discoveries, shared by the run and done views
    private readonly ShowcaseFeed _feed = new();
    private readonly Label _feedTitle = MakeLabel("Discoveries", GroupTitle, Theme.Text);
    private readonly Label _feedCount = MakeLabel("", Small, Theme.TextDim);

    // what fills the space under the map: the run's milestones, then the finished world's tallies
    private readonly MilestoneStrip _milestones = new();
    private readonly TallyRow _tallies = new();
    private readonly Label _talliesTitle = MakeLabel("At a glance", GroupTitle, Theme.Text);

    // history view: the world living on past its start date, until the player accepts it
    private readonly Label _historyTitle = MakeLabel("Its history unfolds", Title, Theme.Text);
    private readonly Label _historySubtitle = MakeLabel("", Subtitle, Theme.TextDim, wrap: true);
    private readonly MapPreview _historyMap = new();
    private readonly PillButton _playPause = new() { Text = "Pause", Glyph = "", MinWidth = 104 };
    private readonly PillButton _pace = new() { Text = "Faster", Kind = PillKind.Quiet, MinWidth = 80 };
    private readonly PillButton _accept = new() { Text = "Begin here", Kind = PillKind.Primary, Glyph = "", MinWidth = 150 };
    private readonly Label _standing = MakeLabel("", Small, Theme.TextDim);
    private readonly Label _chronicleTitle = MakeLabel("Chronicle", GroupTitle, Theme.Text);
    private readonly Label _chronicleCount = MakeLabel("", Small, Theme.TextDim);
    private readonly ChronicleFeed _chronicle = new()
    {
        EmptyText = "Wars won, realms divided, thrones seized: the chronicle fills as the years pass.",
    };
    private readonly PillButton _continueHistory = new() { Text = "Continue its history", Glyph = "" };
    private readonly WrappingToolTip _tips = new() { InitialDelay = 500 };

    // run view
    private readonly MapPreview _runMap = new();
    private readonly LiveMap _live;
    private readonly ProgressLine _bar = new();
    private readonly Label _percent = MakeLabel("", new Font("Segoe UI Semibold", 12f), Theme.Text);
    private readonly Label _eta = MakeLabel("", Body, Theme.TextDim);
    private readonly Label _phase = MakeLabel("", Small, Theme.TextDim);
    private readonly PillButton _cancel = new() { Text = "Cancel", Kind = PillKind.Secondary, Glyph = "" };
    private readonly Label _runTitle = MakeLabel("Making your world", Title, Theme.Text);
    private readonly Label _runSubtitle = MakeLabel("", Subtitle, Theme.TextDim);

    // done view
    private readonly Label _doneTitle = MakeLabel("Your world is ready", Title, Theme.Text);
    private readonly Label _doneSubtitle = MakeLabel("", Subtitle, Theme.TextDim, wrap: true);
    private readonly PathText _donePath = new() { Font = new Font("Consolas", 8.5f) };
    private readonly MapPreview _doneMap = new();
    private readonly PillButton _launch = new() { Text = "Launch Crusader Kings III", Kind = PillKind.Primary, Glyph = "" };
    private readonly PillButton _openFolder = new() { Text = "Open mod folder", Glyph = "" };
    private readonly PillButton _customize = new() { Text = "Customize in Complex", Glyph = "" };
    private readonly PillButton _another = new() { Text = "Make another", Kind = PillKind.Quiet, Glyph = "" };
    private readonly PillButton _retry = new() { Text = "Back to review", Kind = PillKind.Primary, Glyph = "" };
    private readonly PillButton _details = new() { Text = "Show details in Complex", Glyph = "" };

    /// <param name="namePrefix">
    /// Put in front of every control's Name ("quick" gives "quickCancel"), so each page's buttons
    /// keep an automation id of their own.
    /// </param>
    public RunScreen(string namePrefix)
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        Visible = false;
        Name = namePrefix + "RunScreen";

        _feed.Name = namePrefix + "Feed";
        _milestones.Name = namePrefix + "Milestones";
        _tallies.Name = namePrefix + "Tallies";
        _cancel.Name = namePrefix + "Cancel";
        _launch.Name = namePrefix + "Launch";
        _openFolder.Name = namePrefix + "OpenFolder";
        _customize.Name = namePrefix + "Customize";
        _another.Name = namePrefix + "Another";
        _retry.Name = namePrefix + "Retry";
        _details.Name = namePrefix + "Details";
        _continueHistory.Name = namePrefix + "ContinueHistory";
        _playPause.Name = namePrefix + "PlayPause";
        _pace.Name = namePrefix + "Pace";
        _accept.Name = namePrefix + "Accept";
        _chronicle.Name = namePrefix + "Chronicle";

        // A discovery with a place gets its pin as its card is shown, not as it arrives: the column
        // paces the cards, and the pin keeps step with the card it belongs to.
        _live = new LiveMap(_runMap);
        _feed.Revealed += item =>
        {
            if (_mode == Mode.Running) _live.Pin(item);
        };

        BuildRunView();
        BuildHistoryView();
        BuildDoneView();

        Controls.Add(_runPanel);
        Controls.Add(_historyPanel);
        Controls.Add(_donePanel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
            _live.Dispose();
            ClearMarks();
            _pulseClock.Dispose();
        }
        base.Dispose(disposing);
    }

    private int S(int logical) => LaunchUi.S(this, logical);

    /// <summary>
    /// The column beside the map — discoveries, or the chronicle — a third of a wide page, never
    /// narrower than it is at the size the window opens at, nor so wide its lines stop reading well.
    /// It keeps to the page's right edge; the map takes the rest.
    /// </summary>
    private int SideColumn(int width) => Math.Clamp(width * 33 / 100, S(310), S(420));

    /// <summary>What "Make another" says on this page: Quick makes another, Azgaar imports another.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string AnotherText { get => _another.Text; set => _another.Text = value; }

    public bool Running => _mode == Mode.Running;
    public bool Done => _mode == Mode.Done;

    /// <summary>Launch is only offered when the game was found.</summary>
    public void SetGameFound(bool found)
    {
        _gameFound = found;
        _launch.Enabled = found;
    }

    /// <summary>
    /// Shows the run view. The picture starts as whatever the page last showed of the world, and is
    /// replaced as the run draws its own.
    /// </summary>
    public void ShowRunning(string title, string subtitle, Bitmap? picture, string chip, string emptyFeedText)
    {
        _mode = Mode.Running;
        _runTitle.Text = title;
        _runSubtitle.Text = subtitle;
        _live.Reset(picture, chip);
        _bar.Fraction = null;
        _percent.Text = "Starting…";
        _eta.Text = "";
        _phase.Text = "";
        _cancel.Enabled = true;
        _cancel.Text = "Cancel";
        _milestones.Reset();
        _tallies.Set([]);
        _feed.Clear();
        _feed.EmptyText = emptyFeedText;
        _feedTitle.Text = "Discoveries";
        UpdateFeedCount();
        MoveFeedTo(_runPanel);

        // A new world, a new history.
        _historyOffered = _historyStarted = false;
        _historyMap.Image = null;
        _chronicle.Clear();
        ClearMarks();
        UpdateChronicleCount();

        Show(_runPanel);
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

    /// <summary>
    /// The pictures that read well as a map filling in, and what each is captioned. The provinces
    /// are not among them: the partition's own sketches show those forming (<see cref="OfferSketch"/>).
    /// </summary>
    private static readonly Dictionary<string, string> LiveViews = new()
    {
        ["Relief"] = "Raising the land",
        ["Climate"] = "Settling the climate",
        ["Drainage"] = "Finding the rivers",
        ["Terrain"] = "Painting the terrain",
        ["Counties"] = "Drawing the counties",
        ["Duchies"] = "Drawing the duchies",
        ["Kingdoms"] = "Drawing the kingdoms",
        ["Empires"] = "Drawing the empires",
        ["Cultures"] = "Placing the peoples",
        ["Faiths"] = "Spreading the faiths",
    };

    /// <summary>
    /// A picture the run just drew. The screen keeps the ones that read well as a map filling in —
    /// each takes its turn on the map — and disposes the rest. Takes ownership either way.
    /// </summary>
    public void OfferLiveImage(string view, Bitmap bitmap)
    {
        if (_mode != Mode.Running || !LiveViews.TryGetValue(view, out string? caption))
        {
            bitmap.Dispose();
            return;
        }

        _live.Show(bitmap, caption);
    }

    /// <summary>The province partition at one of its steps, played out on the map in its turn.</summary>
    public void OfferSketch(Core.PartitionSketch sketch)
    {
        if (_mode == Mode.Running) _live.Sketch(sketch);
    }

    public void CancelPending()
    {
        _cancel.Enabled = false;
        _cancel.Text = "Stopping…";
        _phase.Text = "Stopping at the end of this step…";
    }

    /// <summary>The run finished and the mod is on disk.</summary>
    /// <param name="history">What the history view left the world as — "It begins in 1123, after
    /// 57 years of history." — or null when there was none, or it was accepted where it began.</param>
    public void ShowDone(string modName, string modDir, TimeSpan took, string? history = null)
    {
        _live.Finish();
        bool fromHistory = _mode == Mode.History;
        _mode = Mode.Done;
        _failed = false;
        _doneTitle.Text = "Your world is ready";
        bool ask = _historyOffered && !_historyStarted;
        _doneSubtitle.Text = $"“{modName}” was made in {Describe(took)}. "
                           + (history is null ? "" : history + " ")
                           + (ask
                               ? $"Would you like to simulate its history first? It lives on from {_historyBegins} and the "
                                 + "game begins in the year you stop it. Or launch it as it is, or open it in Complex to fine-tune anything."
                               : "Launch the game, or open it in Complex to fine-tune anything.");
        _donePath.Text = modDir;
        var picture = fromHistory ? _historyMap.Image : _runMap.Image;
        _doneMap.Image = picture is { } img ? new Bitmap(img) : null;
        _doneMap.Chip = "Drawing the realms…";
        foreach (var b in (Control[])[_launch, _openFolder, _customize, _another]) b.Visible = true;
        foreach (var b in (Control[])[_retry, _details]) b.Visible = false;
        _continueHistory.Visible = _historyOffered;
        _continueHistory.Text = ask ? "Simulate its history" : "Continue its history";
        _tips.SetToolTip(_continueHistory, ask
            ? "Watch the world live on from its start date — wars, divided realms, seized thrones — and begin the game in the year you choose."
            : "Back to the history, from where you stopped it. The world is written again when you accept it.");
        _launch.Enabled = _gameFound;
        HandFeedToDone("Discovered in this world");
        Show(_donePanel);
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
        _live.Finish();
        _mode = Mode.Done;
        _failed = true;
        _doneTitle.Text = cancelled ? "Stopped" : "The world could not be made";
        _doneSubtitle.Text = cancelled
            ? "The run was cancelled, and whatever it had written was removed."
            : $"{message ?? "Something went wrong."} The log in Complex has the details.";
        _donePath.Text = "";
        _doneMap.Image = _runMap.Image is { } img ? new Bitmap(img) : null;
        _doneMap.Chip = cancelled ? "Cancelled" : "Stopped here";
        foreach (var b in (Control[])[_launch, _openFolder, _customize, _another, _continueHistory]) b.Visible = false;
        foreach (var b in (Control[])[_retry, _details]) b.Visible = true;
        HandFeedToDone("Made before it stopped");
        Show(_donePanel);
    }

    /// <summary>Back to idle, as the page returns to its steps.</summary>
    public void Reset()
    {
        _live.Finish();
        _mode = Mode.Idle;
    }

    private static string Describe(TimeSpan t)
        => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{t.TotalSeconds:F0} s";

    private void Show(StepPanel panel)
    {
        SuspendLayout();
        _runPanel.Visible = ReferenceEquals(panel, _runPanel);
        _historyPanel.Visible = ReferenceEquals(panel, _historyPanel);
        _donePanel.Visible = ReferenceEquals(panel, _donePanel);
        ResumeLayout();
        panel.PerformLayout();
    }

    // ================================================================ views

    private void BuildRunView()
    {
        _runPanel.Controls.AddRange([_runTitle, _runSubtitle, _runMap, _bar, _percent, _eta, _phase, _cancel, _milestones]);
        _cancel.Click += (_, _) => CancelRequested?.Invoke();

        _runPanel.Arrange = panel =>
        {
            var (x, w) = StepPanel.Column(panel);
            int y = S(18);
            _runTitle.Location = new Point(x - S(2), y);
            y += _runTitle.PreferredHeight + S(2);
            y += StepPanel.Place(_runSubtitle, x, y, w) + S(18);

            // The map on the left, the discoveries in a column on the right.
            int feedW = SideColumn(w), gap = S(24);
            int left = w - feedW - gap;
            PlaceFeed(panel, x + left + gap, y, feedW);

            // The map leaves room under it for the progress and the milestones, which take its
            // width; a narrower map can need more rows of milestones, so it is measured twice.
            int Under(int mapWidth) => S(20) + S(8) + S(12) + _percent.PreferredHeight + S(2) + _phase.PreferredHeight + S(16)
                                       + _milestones.GridHeight(mapWidth) + S(12);
            var map = StepPanel.Map(x, y, left, panel.ClientSize.Height - y - Under(left), S(StepPanel.MinMapHeight));
            if (Under(map.Width) > Under(left))
                map = StepPanel.Map(x, y, left, panel.ClientSize.Height - y - Under(map.Width), S(StepPanel.MinMapHeight));
            int mapW = map.Width;
            int mx = x;
            _runMap.Bounds = map;
            y += map.Height + S(20);
            _bar.Bounds = new Rectangle(mx, y, mapW, S(8));
            y += S(8) + S(12);
            _percent.Location = new Point(mx, y);
            _cancel.Location = new Point(mx + mapW - _cancel.Width, y - S(2));

            // The estimate beside the percentage, wrapping short of Cancel rather than running under it.
            int ex = _percent.Right + S(12);
            int etaY = y + (_percent.PreferredHeight - _eta.GetPreferredSize(Size.Empty).Height) / 2;
            int etaH = StepPanel.Place(_eta, ex, etaY, _cancel.Left - S(8) - ex);
            y = Math.Max(y + _percent.PreferredHeight, etaY + etaH) + S(2);
            // A line kept for the phase even before it has one, so the milestones do not jump when
            // it arrives; beside Cancel it wraps short of the button.
            int phaseW = y < _cancel.Bottom ? _cancel.Left - S(8) - mx : mapW;
            y += Math.Max(_phase.PreferredHeight, StepPanel.Place(_phase, mx, y, phaseW)) + S(16);
            _milestones.Bounds = new Rectangle(mx, y, mapW, _milestones.GridHeight(mapW));
        };
    }

    private void BuildHistoryView()
    {
        _historyPanel.Controls.AddRange([_historyTitle, _historySubtitle, _historyMap, _playPause, _pace, _accept, _standing,
            _chronicleTitle, _chronicleCount, _chronicle]);
        _playPause.Click += (_, _) => PlayPauseRequested?.Invoke();
        _pace.Click += (_, _) => PaceRequested?.Invoke();
        _accept.Click += (_, _) => AcceptRequested?.Invoke();
        _tips.SetToolTip(_accept, "Stop here: the game begins in this year, with the world as it stands.");
        _historyMap.Overlay = DrawMarks;
        _pulseClock.Tick += (_, _) => AgePulses();
        _chronicle.HoverChanged += ShowHovered;

        _historyPanel.Arrange = panel =>
        {
            var (x, w) = StepPanel.Column(panel);
            int y = S(18);
            _historyTitle.Location = new Point(x - S(2), y);
            y += _historyTitle.PreferredHeight + S(4);
            _historySubtitle.Bounds = new Rectangle(x, y, w, StepPanel.Wrapped(_historySubtitle, w));
            y += _historySubtitle.Height + S(16);

            // The map on the left with its controls under it; the chronicle in a column on the right.
            int columnW = SideColumn(w), gap = S(24);
            int left = w - columnW - gap;
            int cx = x + left + gap;
            _chronicleTitle.Location = new Point(cx, y);
            _chronicleCount.Location = new Point(cx + columnW - _chronicleCount.PreferredWidth,
                y + (_chronicleTitle.PreferredHeight - _chronicleCount.PreferredHeight) / 2);
            int top = y + _chronicleTitle.PreferredHeight + S(8);
            _chronicle.Bounds = new Rectangle(cx, top, columnW, Math.Max(S(60), panel.ClientSize.Height - top - S(10)));

            // The map leaves room under it for its controls and the line on how the world stands.
            // Begin keeps to the right of Pause and the pace switch while it fits beside them, and
            // takes a row of its own under a map too narrow for all three.
            foreach (var b in (PillButton[])[_playPause, _pace, _accept]) b.FitWidth();
            bool OneRow(int mapWidth) => _playPause.Width + S(8) + _pace.Width + S(16) + _accept.Width <= mapWidth;
            int Under(int mapWidth) => S(16) + (OneRow(mapWidth) ? S(36) : 2 * S(36) + S(8)) + S(10)
                                       + StepPanel.Wrapped(_standing, mapWidth) + S(12);
            var map = StepPanel.Map(x, y, left, panel.ClientSize.Height - y - Under(left), S(StepPanel.MinMapHeight));
            if (Under(map.Width) > Under(left))
                map = StepPanel.Map(x, y, left, panel.ClientSize.Height - y - Under(map.Width), S(StepPanel.MinMapHeight));
            int mapW = map.Width;
            _historyMap.Bounds = map;
            y += map.Height + S(16);

            _playPause.Location = new Point(x, y);
            _pace.Location = new Point(_playPause.Right + S(8), y);
            if (!OneRow(mapW)) y += S(36) + S(8);
            _accept.Location = new Point(Math.Max(x, x + mapW - _accept.Width), y);
            y += S(36) + S(10);
            StepPanel.Place(_standing, x, y, mapW);
        };
    }

    public bool InHistory => _mode == Mode.History;

    /// <summary>
    /// The world has a history to run on from <paramref name="began"/>, not yet started: the done
    /// view asks whether to simulate it. Called before <see cref="ShowDone"/>.
    /// </summary>
    public void OfferHistory(int began)
    {
        _historyOffered = true;
        _historyStarted = false;
        _historyBegins = began;
    }

    /// <summary>
    /// Shows the history view: the world living on from <paramref name="began"/>. The chronicle
    /// keeps what it had, so coming back from the done view carries straight on.
    /// </summary>
    public void ShowHistory(int began)
    {
        _live.Finish();
        _mode = Mode.History;
        _historyOffered = _historyStarted = true;
        _historySubtitle.Text = $"The world lives on from {began}: wars are won and lost, realms divide, thrones change "
                                + "hands. Stop whenever you like the world you see — the game begins in the year you accept.";
        _historyMap.Busy = null;
        Show(_historyPanel);
    }

    /// <summary>The map as it stands in <paramref name="year"/>. Takes ownership of the picture.</summary>
    public void SetHistoryFrame(Bitmap frame, int year, int began, string standing)
    {
        if (_mode != Mode.History) { frame.Dispose(); return; }
        _historyMap.Image = frame;
        _historyMap.Chip = year > began ? $"{year} · {Years(year - began)} on" : $"{year}";
        _accept.Text = $"Begin in {year}";
        _standing.Text = standing;
        _historyPanel.PerformLayout();
    }

    public void AddChronicle(IReadOnlyList<ChronicleLine> lines)
    {
        _chronicle.Add(lines);
        UpdateChronicleCount();

        // Each line's place flares on the map as it arrives, in the colour of what happened there.
        foreach (var line in lines)
        {
            if (line.Mark is not { } mark || PaintMark?.Invoke(mark) is not { } painted) continue;
            _pulses.Add(new Pulse(painted.Mask, painted.Bounds, Environment.TickCount64));
            while (_pulses.Count > MaxPulses)
            {
                _pulses[0].Mask.Dispose();
                _pulses.RemoveAt(0);
            }
        }
        if (_pulses.Count > 0 && !_pulseClock.Enabled) _pulseClock.Start();
    }

    // --- Marks: where the chronicle's news happened, lit up on the history map ---

    /// <summary>
    /// Paints a chronicle line's place as a picture to lay over the map, on the canvas the frames
    /// are drawn from. Set by the host for the history it is running; without it nothing is marked.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<MapMark, (Bitmap Mask, Rectangle Bounds)?>? PaintMark { get; set; }

    /// <summary>A place flaring up: its picture, where it lies on the canvas, and when it began.</summary>
    private sealed record Pulse(Bitmap Mask, Rectangle Bounds, long Started);

    /// <summary>How long a flare lasts, and how many burn at once — a busy year at the fast pace keeps the newest.</summary>
    private const int PulseMs = 1800, MaxPulses = 12;

    private readonly List<Pulse> _pulses = [];
    private readonly System.Windows.Forms.Timer _pulseClock = new() { Interval = 33 };

    /// <summary>The place of the chronicle line under the mouse, held lit while it is there.</summary>
    private (Bitmap Mask, Rectangle Bounds)? _hovered;

    /// <summary>How bright a flare is <paramref name="ms"/> into its life: up at once, then easing out.</summary>
    private static float Brightness(long ms)
    {
        float t = ms / (float)PulseMs;
        if (t >= 1) return 0;
        return t < 0.08f ? t / 0.08f : MathF.Pow(1 - (t - 0.08f) / 0.92f, 1.6f);
    }

    private void AgePulses()
    {
        long now = Environment.TickCount64;
        for (int i = _pulses.Count - 1; i >= 0; i--)
        {
            if (now - _pulses[i].Started < PulseMs) continue;
            _pulses[i].Mask.Dispose();
            _pulses.RemoveAt(i);
        }
        if (_pulses.Count == 0) _pulseClock.Stop();
        _historyMap.Invalidate();
    }

    private void ShowHovered(ChronicleLine? line)
    {
        _hovered?.Mask.Dispose();
        _hovered = line?.Mark is { } mark ? PaintMark?.Invoke(mark) : null;
        _historyMap.Invalidate();
    }

    private void ClearMarks()
    {
        _pulseClock.Stop();
        foreach (var pulse in _pulses) pulse.Mask.Dispose();
        _pulses.Clear();
        _hovered?.Mask.Dispose();
        _hovered = null;
    }

    private void DrawMarks(Graphics g, RectangleF picture)
    {
        if (_historyMap.Image is not { } image || image.Width <= 0) return;
        float scale = (float)picture.Width / image.Width;
        var quality = (g.InterpolationMode, g.PixelOffsetMode);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        long now = Environment.TickCount64;
        foreach (var pulse in _pulses) Draw(pulse.Mask, pulse.Bounds, Brightness(now - pulse.Started));
        if (_hovered is { } hovered) Draw(hovered.Mask, hovered.Bounds, 1f);

        (g.InterpolationMode, g.PixelOffsetMode) = quality;

        void Draw(Bitmap mask, Rectangle bounds, float alpha)
        {
            if (alpha <= 0.01f) return;
            var dest = Rectangle.Round(new RectangleF(picture.X + bounds.X * scale, picture.Y + bounds.Y * scale,
                bounds.Width * scale, bounds.Height * scale));
            using var fade = new System.Drawing.Imaging.ImageAttributes();
            fade.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha });
            fade.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(mask, dest, 0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel, fade);
        }
    }

    /// <summary>
    /// The history view's controls: playing or paused, at which pace, and whether something is
    /// under way — the world being written, or the history being picked up again. Then nothing can
    /// be pressed, and the map says what is happening.
    /// </summary>
    public void SetHistoryState(bool playing, bool fast, string? busy)
    {
        _playPause.Text = playing ? "Pause" : "Resume";
        _playPause.Glyph = playing ? "" : "";
        _pace.Text = fast ? "Slower" : "Faster";
        _playPause.Enabled = _pace.Enabled = _accept.Enabled = busy is null;
        _historyMap.Busy = busy;
        _tips.SetToolTip(_pace, fast ? "Back to a year a second" : "Five years a second");
        _playPause.Invalidate();
        _historyPanel.PerformLayout();
    }

    private void UpdateChronicleCount()
    {
        int n = _chronicle.Count;
        _chronicleCount.Text = n == 0 ? "" : n == 1 ? "1 entry" : $"{n} entries";
        _historyPanel.PerformLayout();
    }

    private static string Years(int n) => n == 1 ? "1 year" : $"{n} years";

    private void BuildDoneView()
    {
        _donePanel.Controls.AddRange([_doneTitle, _doneSubtitle, _donePath, _doneMap, _launch, _openFolder, _continueHistory, _customize,
            _another, _retry, _details, _talliesTitle, _tallies]);
        _continueHistory.Click += (_, _) => ContinueHistoryRequested?.Invoke();
        _launch.Click += (_, _) => LaunchRequested?.Invoke();
        _openFolder.Click += (_, _) => OpenFolderRequested?.Invoke();
        _customize.Click += (_, _) => CustomizeRequested?.Invoke();
        _details.Click += (_, _) => CustomizeRequested?.Invoke();
        _another.Click += (_, _) => AnotherRequested?.Invoke();
        _retry.Click += (_, _) => RetryRequested?.Invoke();

        _donePanel.Arrange = panel =>
        {
            var (x, w) = StepPanel.Column(panel);
            int y = S(18);
            _doneTitle.Location = new Point(x - S(2), y);
            y += _doneTitle.PreferredHeight + S(4);
            _doneSubtitle.Bounds = new Rectangle(x, y, w, StepPanel.Wrapped(_doneSubtitle, w));
            y += _doneSubtitle.Height + S(2);
            int pathH = string.IsNullOrEmpty(_donePath.Text) ? 0 : TextRenderer.MeasureText("Ag", _donePath.Font).Height + S(2);
            _donePath.Bounds = new Rectangle(x, y, w, pathH);
            y += pathH + S(14);

            // The world on the left with what to do next under it; everything discovered on the right.
            int feedW = SideColumn(w), gap = S(24);
            int left = w - feedW - gap;
            PlaceFeed(panel, x + left + gap, y, feedW);

            List<Control> buttons = _failed ? [_retry, _details] : [_launch, _openFolder, _customize, _another];
            // Beside Launch while it is still a question; after Open mod folder once it has been run.
            if (!_failed && _historyOffered) buttons.Insert(_historyStarted ? 2 : 1, _continueHistory);
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
            bool tallies = _tallies.GridHeight(left) > 0;
            int talliesH = tallies ? _talliesTitle.PreferredHeight + S(8) + _tallies.GridHeight(left) + S(18) : 0;
            var map = StepPanel.Map(x, y, left, panel.ClientSize.Height - y - buttonsH - talliesH - S(34), S(StepPanel.MinMapHeight));
            int mapW = map.Width;
            _doneMap.Bounds = map;
            y += map.Height + S(18);

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
                _tallies.Bounds = new Rectangle(x, y, mapW, _tallies.GridHeight(mapW));
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
}
