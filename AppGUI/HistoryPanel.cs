using System.Diagnostics;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The History workspace: the written world's realms, run on past the start date a year at a time
/// while the player watches. See <see cref="HistorySim"/> for the simulation itself.
///
/// The simulation runs on the UI thread, from a timer, inside a time budget per frame. A year costs
/// well under three milliseconds on a thousand-county map, so a small budget covers even the
/// fastest speed with room to draw, and nothing is ever touched by two threads: the map, the
/// chronicle and the simulation cannot race because they cannot overlap. A slow machine simply
/// plays slower than the speed it was asked for, rather than falling behind and catching up in
/// lurches.
///
/// Never touches the world it was given. The simulation copies the start date's realms, and
/// <see cref="Reset"/> goes back to them, so the written mod, the World workspace and the editor all
/// go on reading exactly what they read before this workspace was opened.
/// </summary>
internal sealed class HistoryPanel : Panel
{
    /// <summary>Years per second the speed buttons offer.</summary>
    private static readonly int[] Speeds = [1, 5, 25, 100];

    /// <summary>
    /// Most milliseconds of simulation one frame may spend before it draws. Well under the timer's
    /// interval on purpose: a frame that fills its whole interval leaves a timer message always
    /// waiting, and the window then does nothing but simulate.
    /// </summary>
    private const double FrameBudgetMs = 12;

    /// <summary>How many chronicle lines are kept on screen; the simulation keeps every event.</summary>
    private const int ChronicleLimit = 2000;

    private readonly ImageView _view = new()
    {
        Dock = DockStyle.Fill,
        ViewName = "history",
        EmptyText = "Generate a world and write the mod to give it a history to run.",
    };

    private readonly ListBox _chronicle = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        Font = Theme.Ui,
        BackColor = Theme.Surface,
        ForeColor = Theme.Text,
        IntegralHeight = false,
        HorizontalScrollbar = true,
    };

    private readonly CheckBox _showConquests = new()
    {
        Text = "County conquests",
        AutoSize = true,
        Font = Theme.Ui,
        ForeColor = Theme.TextDim,
        Margin = new Padding(12, 7, 3, 3),
    };

    private readonly Button _play = Theme.MakeButton("▶  Play", 84, primary: true);
    private readonly Button _step = Theme.MakeButton("+1 year", 70);
    private readonly Button _reset = Theme.MakeButton("Reset", 64);
    private readonly Button _apply = Theme.MakeButton("Apply to World…", 120);
    private readonly Button _discard = Theme.MakeButton("Discard", 70);
    private readonly Label _appliedNote = new() { AutoSize = true, Font = Theme.Ui, ForeColor = Theme.NoticeText, Margin = new Padding(8, 5, 3, 3) };
    private readonly FlowLayoutPanel _appliedBar = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        WrapContents = false,
        Padding = new Padding(6, 2, 6, 0),
        BackColor = Theme.Notice,
        Visible = false,
    };
    private readonly Dictionary<int, Button> _speedButtons = [];
    private readonly Label _year = new() { AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Theme.Text, Margin = new Padding(14, 6, 3, 3) };
    private readonly Label _stats = new() { AutoSize = true, Font = Theme.Ui, ForeColor = Theme.TextDim, Margin = new Padding(10, 10, 3, 3) };
    private readonly Label _readout = new() { Dock = DockStyle.Bottom, Height = 24, Font = Theme.Ui, ForeColor = Theme.TextDim, BackColor = Theme.Surface, Padding = new Padding(8, 4, 0, 0) };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };

    private GenerationResult? _result;
    private RealmMap? _realms;
    private MapGen.WildernessMap? _wilderness;

    /// <summary>The start date's people, whose houses realms that endure are still ruled by. See <see cref="AppliedHistory.Capture"/>.</summary>
    private RulerMap? _rulers;
    private PrehistoryMap? _prehistory;
    private int _startYear;

    private CountyCanvas? _canvas;
    private int _canvasFor;
    private HistorySim? _sim;
    private Bitmap? _frame;

    private int _speed = 5;
    private double _owed;
    private long _lastTick;
    private int _shownEvents;

    /// <summary>Colour per realm id, handed out once and kept for the realm's whole life.</summary>
    private readonly Dictionary<int, (byte R, byte G, byte B)> _colourOf = [];
    private int _nextColour;

    public HistoryPanel()
    {
        BackColor = Theme.Background;

        foreach (int speed in Speeds)
        {
            var button = new Theme.SegmentButton { Text = $"{speed}", Width = speed >= 100 ? 40 : 32 };
            button.Click += (_, _) => SetSpeed(speed);
            _speedButtons[speed] = button;
        }

        _play.Click += (_, _) => TogglePlay();
        _step.Click += (_, _) => StepOnce();
        _reset.Click += (_, _) => Reset();
        _apply.Click += (_, _) =>
        {
            if (_sim is null || _canvas is null) return;
            Pause();
            ApplyRequested?.Invoke(AppliedHistory.Capture(_sim, _canvas.Counties, _rulers, _prehistory));
        };
        _discard.Click += (_, _) => DiscardRequested?.Invoke();
        _showConquests.CheckedChanged += (_, _) => RebuildChronicle();
        _timer.Tick += (_, _) => OnFrame();
        _view.ViewChanged += (_, pixel) => ShowReadout(pixel);

        var tips = new ToolTip { InitialDelay = 500 };
        tips.SetToolTip(_play, "Run history forward, or stop it (Space)");
        tips.SetToolTip(_step, "Advance one year (→)");
        tips.SetToolTip(_reset, "Go back to the start date. The same history plays out again unless something is changed.");
        tips.SetToolTip(_showConquests, "List every county that changes hands, not only the realm-level events");
        tips.SetToolTip(_apply, "Make the realms as they stand now the world's start, and write the mod with them");
        tips.SetToolTip(_discard, "Go back to the realms the generator grows; takes effect when the mod is next written");

        var speedLabel = new Label { Text = "years / second", AutoSize = true, Font = Theme.Ui, ForeColor = Theme.TextDim, Margin = new Padding(2, 10, 3, 3) };

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            WrapContents = false,
            Padding = new Padding(6, 4, 6, 0),
            BackColor = Theme.Surface,
        };
        toolbar.Controls.Add(_play);
        toolbar.Controls.Add(_step);
        toolbar.Controls.Add(_reset);
        toolbar.Controls.Add(Theme.MakeSegmented(_speedButtons.Values));
        toolbar.Controls.Add(speedLabel);
        toolbar.Controls.Add(_apply);
        toolbar.Controls.Add(_year);
        toolbar.Controls.Add(_stats);

        _appliedBar.Controls.Add(_appliedNote);
        _appliedBar.Controls.Add(_discard);

        var chronicleHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            WrapContents = false,
            BackColor = Theme.Surface,
        };
        chronicleHeader.Controls.Add(new Label { Text = "Chronicle", AutoSize = true, Font = Theme.UiBold, ForeColor = Theme.Text, Margin = new Padding(8, 7, 3, 3) });
        chronicleHeader.Controls.Add(_showConquests);

        var chronicle = new Panel { Dock = DockStyle.Right, Width = 380, BackColor = Theme.Surface, Padding = new Padding(1, 0, 0, 0) };
        chronicle.Controls.Add(_chronicle);
        chronicle.Controls.Add(chronicleHeader);
        chronicle.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 0, 0, 0, chronicle.Height); };

        var map = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        map.Controls.Add(_view);
        map.Controls.Add(_readout);

        Controls.Add(map);
        Controls.Add(chronicle);
        Controls.Add(_appliedBar);
        Controls.Add(toolbar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });

        SetSpeed(_speed);
        RefreshControls();
    }

    public bool Playing => _timer.Enabled;

    /// <summary>The realms as they stand now, to be made the world's start. The host decides what to do with them.</summary>
    public event Action<AppliedHistory>? ApplyRequested;

    /// <summary>The applied history is to be dropped.</summary>
    public event Action? DiscardRequested;

    /// <summary>
    /// Says, above the map, which history the next write will use. Shown whenever one is pending
    /// or in force, so the World Year the mod is written with never differs from the settings grid
    /// without the window saying why.
    /// </summary>
    public void ShowApplied(AppliedHistory? applied)
    {
        _appliedBar.Visible = applied is not null;
        if (applied is null) return;

        int years = applied.Year - applied.FromYear;
        _appliedNote.Text = $"Applied history: the world is written starting in {applied.Year}, "
            + $"{years} years on from {applied.FromYear}. Titles, cultures and faiths are the generated ones; "
            + "advancement is unchanged.";
    }

    /// <summary>
    /// Hands the panel the world the window now holds. A preview has no peoples, faiths or realms
    /// yet — those are decided when the mod is written — so the panel only offers history once
    /// <paramref name="written"/> exists, and says why when it does not.
    /// </summary>
    public void Attach(GenerationResult? result, Emit.WrittenContent? written)
    {
        Pause();
        _canvasFor++;
        _canvas = null;
        _sim = null;
        _result = result;
        _realms = written?.Realms;
        _wilderness = written?.Wilderness;
        _rulers = written?.Rulers;
        _prehistory = written?.Prehistory;
        _startYear = result?.Config.StartYear ?? 0;

        _view.SetImage(null);
        _frame?.Dispose();
        _frame = null;
        _chronicle.Items.Clear();
        _readout.Text = "";

        _view.EmptyText =
            result is null ? "Generate a world and write the mod to give it a history to run."
            : written is null ? "Write the mod to give this world a history to run. A preview builds the map "
                              + "only; its peoples, faiths and realms are decided when the mod is written."
            : _realms is null ? "This mod was written without history (--no-history), so it has no realms to run."
            : _realms.History?.Rules is null ? "This world's realms were not grown by the formation simulation — "
                              + "they came from an Azgaar export, the de jure tree or a shattered world — so there "
                              + "is no history to carry on from."
            : "Preparing the map…";

        _view.Invalidate();
        RefreshControls();
        if (Visible) EnsureReady();
    }

    /// <summary>Stops the clock. Safe to call at any time.</summary>
    public void Pause()
    {
        if (!_timer.Enabled) return;
        _timer.Stop();
        RefreshControls();
    }

    /// <summary>The workspace's keys while it is on screen. False for anything it does not own.</summary>
    public bool HandleKey(Keys key)
    {
        if (_sim is null) return false;
        switch (key)
        {
            case Keys.Space: TogglePlay(); return true;
            case Keys.Right when !Playing: StepOnce(); return true;
            default: return false;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) EnsureReady();
        else Pause();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _frame?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Builds the fast canvas the first time the workspace is shown for a world, off the UI thread:
    /// it walks the whole province raster once, which is a second or two on a large map and not
    /// worth paying for a world nobody opens the History workspace on.
    /// </summary>
    private async void EnsureReady()
    {
        if (_canvas is not null || _result is null || _realms?.History?.Rules is null) return;

        int ticket = ++_canvasFor;
        var raster = PreviewRenderer.ProvinceRaster.From(_result, _wilderness);
        UseWaitCursor = true;
        try
        {
            var canvas = await Task.Run(() => new CountyCanvas(raster));
            if (ticket != _canvasFor) return;
            _canvas = canvas;
            Reset();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(ex);
            _view.EmptyText = "The history map could not be prepared — see the log.";
            _view.Invalidate();
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>Back to the start date, with the start date's colours.</summary>
    private void Reset()
    {
        if (_realms is null || _canvas is null) return;

        Pause();
        _sim = HistorySim.Resume(_realms, _startYear);
        _colourOf.Clear();
        _nextColour = 0;
        _shownEvents = 0;
        _chronicle.Items.Clear();

        // Year zero wears the World workspace's own realm colours, asked of its palette rather than
        // imitated: the palette numbers its sequence over every county's top seat, wilderness
        // included, so a copy of the formula drifts off it by one slot per wild county. Realms born
        // later carry on the same sequence from where the palette's ends.
        if (_sim is not null)
        {
            var counties = _canvas.Counties;
            var graph = RealmGraph.From(_realms, counties);
            var palette = new RealmPalette(graph, counties);

            foreach (var root in _sim.Realms.Where(p => p.Suzerain is null))
                _colourOf[root.Id] = palette.Colour(graph.PathFromTop(graph.SeatOfCounty(root.Capital))[0]);

            _nextColour = counties.Select(c => graph.PathFromTop(graph.SeatOfCounty(c))[0]).Distinct().Count();
        }

        Redraw();
    }

    private void TogglePlay()
    {
        if (_sim is null) return;
        if (Playing) { Pause(); return; }

        _owed = 0;
        _lastTick = Stopwatch.GetTimestamp();
        _timer.Start();
        RefreshControls();
    }

    private void StepOnce()
    {
        if (_sim is null || Playing) return;
        Advance();
        Redraw();
    }

    private void SetSpeed(int speed)
    {
        _speed = speed;
        foreach (var (s, button) in _speedButtons) Theme.StyleSegment(button, s == speed);
    }

    private void OnFrame()
    {
        if (_sim is null) { Pause(); return; }

        long now = Stopwatch.GetTimestamp();
        double seconds = (double)(now - _lastTick) / Stopwatch.Frequency;
        _lastTick = now;

        // A frame's worth of years, never more than half a second's: after a stall the clock
        // carries on from where it was rather than racing to make up the gap.
        _owed = Math.Min(_owed + seconds * _speed, Math.Max(1.0, _speed * 0.5));

        var budget = Stopwatch.StartNew();
        bool advanced = false;
        while (_owed >= 1 && budget.Elapsed.TotalMilliseconds < FrameBudgetMs)
        {
            _owed -= 1;
            if (!Advance()) return;
            advanced = true;
        }

        if (advanced) Redraw();
    }

    /// <summary>One year. False when a debug build caught the simulation breaking an invariant.</summary>
    private bool Advance()
    {
        _sim!.Tick();

#if DEBUG
        // Cheap next to the tick itself, and the one place a broken rule would otherwise go unseen
        // until a mod written from it failed to load.
        var problems = _sim.Check();
        if (problems.Count > 0)
        {
            Pause();
            Console.WriteLine($"history: invariant broken in {_sim.Year}:");
            foreach (var problem in problems.Take(10)) Console.WriteLine($"  {problem}");
            _readout.Text = $"Stopped in {_sim.Year}: the simulation broke one of its own rules — see the log.";
            Redraw();
            return false;
        }
#endif
        return true;
    }

    private void Redraw()
    {
        if (_sim is null || _canvas is null) return;

        var colours = new (byte R, byte G, byte B)?[_canvas.Counties.Count];
        for (int c = 0; c < colours.Length; c++)
            if (_sim.OwnerOf(_canvas.Counties[c]) is { } owner) colours[c] = ColourOf(owner.Root);

        var old = _frame;
        _frame = PreviewRenderer.ToBitmap(_canvas.Render(colours));
        _view.SetImage(_frame);
        old?.Dispose();

        AppendChronicle();
        RefreshControls();
    }

    private (byte R, byte G, byte B) ColourOf(Polity root)
    {
        if (_colourOf.TryGetValue(root.Id, out var known)) return known;

        // RealmPalette's sequence for independent realms, carried on past the start date's: the
        // golden angle round the wheel from the same base hue, with saturation and lightness
        // stepped on cycles of three and two so neighbours in the sequence differ in all three.
        int i = _nextColour++;
        float hue = ((228f + i * Titles.GoldenAngle) % 360f + 360f) % 360f;
        var colour = Titles.FromHsl(hue, 0.60f + i % 3 * 0.09f, 0.44f + i % 2 * 0.13f);
        _colourOf[root.Id] = colour;
        return colour;
    }

    private void RefreshControls()
    {
        bool ready = _sim is not null;
        _play.Enabled = ready;
        _step.Enabled = ready && !Playing;
        _reset.Enabled = ready;
        _apply.Enabled = ready && _sim!.Year > _sim.StartYear;
        _play.Text = Playing ? "❚❚  Pause" : "▶  Play";

        if (_sim is null)
        {
            _year.Text = "";
            _stats.Text = "";
            return;
        }

        int years = _sim.Year - _sim.StartYear;
        _year.Text = years == 0 ? $"{_sim.Year}" : $"{_sim.Year}  (+{years})";

        var realms = _sim.Realms.ToList();
        var vassalsOf = realms.Where(p => p.Suzerain is not null).ToLookup(p => p.Suzerain!);
        int Size(Polity p) => p.Counties.Count + vassalsOf[p].Sum(Size);
        var roots = realms.Where(p => p.Suzerain is null).ToList();
        var largest = roots.OrderByDescending(Size).ThenBy(p => p.Capital.Index).FirstOrDefault();

        _stats.Text = $"{realms.Count} realms · {roots.Count} independent"
                      + (largest is null ? "" : $" · largest: {largest.Capital.Name}, {Size(largest)} counties");
    }

    private void ShowReadout(Point? pixel)
    {
        if (_sim is null || _canvas is null || pixel is not { } p) return;

        var county = _canvas.CountyAt(p.X, p.Y);
        if (county is null) { _readout.Text = ""; return; }

        if (_sim.OwnerOf(county) is not { } owner)
        {
            _readout.Text = $"{county.Name} — wilderness";
            return;
        }

        string text = $"{county.Name} — held by the realm of {owner.Capital.Name} ({owner.Counties.Count} counties, founded {owner.Founded})";
        if (owner.Suzerain is { } lord) text += $", sworn to {lord.Capital.Name}";
        if (owner.Root != owner && owner.Root != owner.Suzerain) text += $" under {owner.Root.Capital.Name}";
        _readout.Text = text;
    }

    // --- Chronicle -------------------------------------------------------------------------------

    private void AppendChronicle()
    {
        if (_sim is null) return;
        var events = _sim.Events;
        if (_shownEvents >= events.Count) return;

        _chronicle.BeginUpdate();
        for (; _shownEvents < events.Count; _shownEvents++)
            if (Describe(events[_shownEvents]) is { } line) _chronicle.Items.Insert(0, line);
        while (_chronicle.Items.Count > ChronicleLimit) _chronicle.Items.RemoveAt(_chronicle.Items.Count - 1);
        _chronicle.EndUpdate();
    }

    private void RebuildChronicle()
    {
        _chronicle.Items.Clear();
        _shownEvents = 0;
        AppendChronicle();
    }

    /// <summary>
    /// One line of the chronicle, or null for an event filtered out. Realms are named for their
    /// capital county as it stood on the day: they have no titles of their own until the history is
    /// written out, and the capital is the one name the map can point at.
    /// </summary>
    private string? Describe(FormationEvent e)
    {
        string subject = e.Subject.Name;
        string actor = e.Actor?.Name ?? "a realm";
        string other = e.Counterpart?.Name ?? "its lord";

        string? text = e.Kind switch
        {
            FormationKind.Conquest => _showConquests.Checked ? $"{actor} took {subject} from {other}" : null,
            FormationKind.Vassalized => $"{subject} swore fealty to {other}",
            FormationKind.Freed => $"{subject} broke free of {other}",
            FormationKind.Fragmented => $"{subject} broke away from {other}",
            FormationKind.Collapsed => $"The realm of {subject} came apart; its vassals went their own ways",
            FormationKind.Absorbed => $"The realm of {actor} fell to {other}",
            _ => null,
        };

        return text is null ? null : $"{e.Year}   {text}";
    }
}
