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

    private enum Mode { Idle, Running, Done }
    private Mode _mode;
    private bool _failed;
    private bool _gameFound = true;

    private readonly StepPanel _runPanel = new();
    private readonly StepPanel _donePanel = new();

    // discoveries, shared by the run and done views
    private readonly ShowcaseFeed _feed = new();
    private readonly Label _feedTitle = MakeLabel("Discoveries", GroupTitle, Theme.Text);
    private readonly Label _feedCount = MakeLabel("", Small, Theme.TextDim);

    // what fills the space under the map: the run's milestones, then the finished world's tallies
    private readonly MilestoneStrip _milestones = new();
    private readonly TallyRow _tallies = new();
    private readonly Label _talliesTitle = MakeLabel("At a glance", GroupTitle, Theme.Text);

    // run view
    private readonly MapPreview _runMap = new();
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

        BuildRunView();
        BuildDoneView();

        Controls.Add(_runPanel);
        Controls.Add(_donePanel);
    }

    private int S(int logical) => LaunchUi.S(this, logical);

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
        _runMap.Image = picture;
        _runMap.Chip = chip;
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

    private static readonly Dictionary<string, string> LiveViews = new()
    {
        ["Relief"] = "Raising the land",
        ["Climate"] = "Settling the climate",
        ["Terrain"] = "Painting the terrain",
        ["Counties"] = "Drawing the counties",
        ["Duchies"] = "Drawing the duchies",
    };

    /// <summary>
    /// A picture the run just drew. The screen keeps the ones that read well as a map filling in and
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
    public void ShowDone(string modName, string modDir, TimeSpan took)
    {
        _mode = Mode.Done;
        _failed = false;
        _doneTitle.Text = "Your world is ready";
        _doneSubtitle.Text = $"“{modName}” was made in {Describe(took)}. "
                           + "Launch the game, or open it in Complex to fine-tune anything.";
        _donePath.Text = modDir;
        _doneMap.Image = _runMap.Image is { } img ? new Bitmap(img) : null;
        _doneMap.Chip = "Drawing the realms…";
        foreach (var b in (Control[])[_launch, _openFolder, _customize, _another]) b.Visible = true;
        foreach (var b in (Control[])[_retry, _details]) b.Visible = false;
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
        Show(_donePanel);
    }

    /// <summary>Back to idle, as the page returns to its steps.</summary>
    public void Reset() => _mode = Mode.Idle;

    private static string Describe(TimeSpan t)
        => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{t.TotalSeconds:F0} s";

    private void Show(StepPanel panel)
    {
        SuspendLayout();
        _runPanel.Visible = ReferenceEquals(panel, _runPanel);
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
}
