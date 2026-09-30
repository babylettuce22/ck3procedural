using System.Drawing.Drawing2D;
using Ck3MapGen.Config;
using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The People step's race mix: how much of the land humans hold, and how the other races share the
/// rest. Opened from the "Race mix" link beside the Fantasy heading; edits a copy of the page's
/// <see cref="QuickRaceMix"/>, which the page takes back on Done.
///
/// Humans are a real percentage and every other race is a weight, because that is how the
/// generator spends land (see Ethnicities.MixBudget): humans' share is set aside, and the races
/// divide what is left in proportion. So the sliders never pull on each other. Each row shows the
/// share of the whole land its weight comes to, and the bar across the top shows them all, both
/// updating as a slider moves; the thumbs stay where they were put.
///
/// Wears the app's drawn caption (<see cref="ChromeForm"/>), like every other window of the tool,
/// and the launcher's controls, since it opens over a launcher page.
/// </summary>
internal sealed class RaceMixDialog : ChromeForm
{
    /// <summary>The content column's width, in 96-dpi pixels.</summary>
    private const int Inner = 580;

    private const int Gutter = 22;

    /// <summary>
    /// One colour per people, for the bar and the slider fills. Literals on purpose: they are
    /// swatches, like a map's, and mid-toned so they read on both palettes.
    /// </summary>
    private static readonly Color HumanColour = Color.FromArgb(140, 150, 168);

    private static readonly Dictionary<MapGen.RaceArchetype, Color> RaceColours = new()
    {
        [MapGen.RaceArchetype.Dwarf] = Color.FromArgb(194, 135, 63),
        [MapGen.RaceArchetype.HighElf] = Color.FromArgb(216, 185, 67),
        [MapGen.RaceArchetype.WoodElf] = Color.FromArgb(95, 160, 82),
        [MapGen.RaceArchetype.Orc] = Color.FromArgb(161, 75, 61),
        [MapGen.RaceArchetype.Gnome] = Color.FromArgb(208, 126, 170),
        [MapGen.RaceArchetype.Giantkin] = Color.FromArgb(110, 169, 204),
        [MapGen.RaceArchetype.DuskElf] = Color.FromArgb(124, 98, 172),
        [MapGen.RaceArchetype.Hornkin] = Color.FromArgb(63, 144, 137),
    };

    private readonly QuickRaceMix _mix;
    private readonly QuickFantasy _level;

    private readonly Canvas _body = new();
    private readonly Label _intro = MakeLabel(
        "How much of the land each people holds. Humans take their share first, and the other races divide the rest by weight. "
        + "Where each one settles is still decided by the ground it favours.",
        Small, Theme.TextDim, wrap: true);
    private readonly MixBar _bar = new();
    private readonly Line _humans;
    private readonly Label _restCaption = MakeLabel("", Small, Theme.TextDim);
    private readonly List<(QuickRaceMix.Row Race, Line Line)> _races = [];
    private readonly Label _note = MakeLabel("", Small, Theme.TextDim, wrap: true);
    private readonly TextLink _reset = new() { Name = "raceMixReset", LinkFont = Small };
    private readonly PillButton _cancel = new() { Name = "raceMixCancel", Text = "Cancel", Kind = PillKind.Secondary, MinWidth = 96 };
    private readonly PillButton _done = new() { Name = "raceMixDone", Text = "Done", Kind = PillKind.Primary, MinWidth = 96 };

    /// <summary>The mix as the dialog left it. Read it after Done.</summary>
    public QuickRaceMix Mix => _mix;

    public RaceMixDialog(QuickRaceMix mix, QuickFantasy level)
    {
        _mix = mix.Clone();
        _level = level;

        Text = "Race mix";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        AcceptButton = _done;
        CancelButton = _cancel;
        _cancel.DialogResult = DialogResult.Cancel;

        _humans = new Line("Humans", "of all the land", HumanColour,
            MapConfig.MinRaceMixHumanShare, MapConfig.MaxRaceMixHumanShare, _mix.HumanPercent(level), "raceMixHumans");
        _humans.Slider.ValueChanged += v => { _mix.HumanShare = v; Describe(); };

        foreach (var race in QuickRaceMix.Rows)
        {
            var line = new Line(race.Name, race.Ground, RaceColours[race.Race], 0, 100, race.Get(_mix),
                "raceMix" + race.Race);
            line.Slider.ValueChanged += v => { race.Set(_mix, v); Describe(); };
            _races.Add((race, line));
        }

        _reset.Text = $"Reset to {(level == QuickFantasy.Low ? "low" : "high")} fantasy's mix";
        _reset.Click += (_, _) => Reset();
        _done.Click += (_, _) =>
        {
            // The level's own share, dragged back to, is the level's share: kept as "follow" so a
            // change of level later moves it along like an untouched mix.
            if (_mix.HumanShare == QuickRaceMix.DefaultHumanShare(_level)) _mix.HumanShare = 0;
            DialogResult = DialogResult.OK;
            Close();
        };

        _body.Controls.AddRange([_intro, _bar, _restCaption, _note, _reset, _cancel, _done]);
        _humans.AddTo(_body);
        foreach (var (_, line) in _races) line.AddTo(_body);
        _body.Layout += (_, _) => Arrange(_body.ClientSize.Width);
        _body.Paint += (_, e) => PaintDots(e.Graphics);

        Controls.Add(_body);

        // Added last so they dock first: the caption row on top, a hairline under it. No icon — a
        // dialog of the main window, which already shows it.
        ShowIcon = false;
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(CaptionBar = new TitleBar(this));

        Describe();
    }

    private int S(int logical) => LaunchUi.S(this, logical);

    /// <summary>Sized once the window knows its DPI: the column, and as tall as the rows come to.</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        int width = S(Inner) + 2 * S(Gutter);
        ClientSize = new Size(width, Arrange(width) + (CaptionBar?.Height ?? 0) + 1);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.ApplyTitleBar(this);
        _humans.Slider.Focus();
    }

    // ------------------------------------------------------------------ state

    private void Reset()
    {
        _mix.HumanShare = 0;
        foreach (var (race, line) in _races)
        {
            race.Set(_mix, QuickRaceMix.DefaultWeight);
            line.Slider.Value = QuickRaceMix.DefaultWeight;
        }
        _humans.Slider.Value = _mix.HumanPercent(_level);
        Describe();
    }

    /// <summary>Every figure, the bar and the note, from the mix as it stands.</summary>
    private void Describe()
    {
        int human = _mix.HumanPercent(_level);
        var shares = _mix.RaceShares(_level);

        _humans.Value.Text = $"{human}%";
        for (int i = 0; i < _races.Count; i++)
            _races[i].Line.Value.Text = _races[i].Race.Get(_mix) <= 0 ? "Off"
                : shares[i] < 0.5 ? "<1%"
                : $"{Math.Round(shares[i]):0}%";

        _restCaption.Text = $"The other {100 - human}% of the land, shared out by weight";

        var segments = new List<(Color, double)> { (HumanColour, human) };
        for (int i = 0; i < _races.Count; i++) segments.Add((RaceColours[_races[i].Race.Race], shares[i]));
        _bar.Segments = segments;

        bool empty = _mix.NoOtherRaces;
        _done.Enabled = !empty;
        _note.ForeColor = empty ? Theme.Danger : Theme.TextDim;
        _note.Text = empty
            ? "With every other race at 0 this is a world of humans only. Choose None under Fantasy for that instead."
            : "Races settle whole peoples, so these are targets rather than exact figures. A small share often "
              + "turns up as a minority living among another people instead of a realm of its own.";

        _body.PerformLayout();
        _body.Invalidate();
    }

    // ------------------------------------------------------------------ layout

    /// <summary>Places everything for a client width and returns the height it came to.</summary>
    private int Arrange(int width)
    {
        int x = S(Gutter);
        int w = width - 2 * x;
        int y = S(16);

        y += StepPanel.Place(_intro, x, y, w) + S(14);
        _bar.Bounds = new Rectangle(x, y, w, S(18));
        y += _bar.Height + S(18);

        y = _humans.Arrange(this, x, y, w, S(34), Strong) + S(8);
        _restCaption.Location = new Point(x, y);
        y += _restCaption.PreferredHeight + S(6);
        foreach (var (_, line) in _races) y = line.Arrange(this, x, y, w, S(30), Body);

        y += S(12);
        y += StepPanel.Place(_note, x, y, w) + S(16);

        _done.Location = new Point(x + w - _done.Width, y);
        _cancel.Location = new Point(_done.Left - S(8) - _cancel.Width, y);
        _reset.Location = new Point(x - S(2), y + (_done.Height - _reset.Height) / 2);
        return y + _done.Height + S(16);
    }

    private void PaintDots(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var line in _races.Select(r => r.Line).Prepend(_humans))
        {
            using var brush = new SolidBrush(line.Colour);
            g.FillEllipse(brush, line.Dot);
        }
    }

    /// <summary>One people's row: a colour dot, its name, the ground it favours, a slider and its share.</summary>
    private sealed class Line
    {
        public readonly Color Colour;
        public readonly Label Name;
        public readonly Label Ground;
        public readonly MixSlider Slider;
        public readonly Label Value;
        public Rectangle Dot;

        public Line(string name, string ground, Color colour, int min, int max, int value, string id)
        {
            Colour = colour;
            Name = MakeLabel(name, Body, Theme.Text);
            Ground = MakeLabel(ground, Small, Theme.TextDim);
            Value = MakeLabel("", Strong, Theme.Text);
            Value.TextAlign = ContentAlignment.MiddleRight;
            Slider = new MixSlider
            {
                Minimum = min, Maximum = max, Value = value, Fill = colour,
                Name = id, AccessibleName = name,
            };
        }

        public void AddTo(Control host) => host.Controls.AddRange([Name, Ground, Slider, Value]);

        /// <summary>Lays the row out at <paramref name="y"/> and returns where the next one starts.</summary>
        public int Arrange(Control host, int x, int y, int w, int height, Font nameFont)
        {
            int S(int logical) => LaunchUi.S(host, logical);
            int nameW = S(96), groundW = S(160), valueW = S(48), gap = S(10);
            int mid = y + height / 2;

            int dot = S(10);
            Dot = new Rectangle(x, mid - dot / 2, dot, dot);

            Name.Font = nameFont;
            Name.Location = new Point(x + S(18), mid - Name.PreferredHeight / 2);
            Ground.Location = new Point(x + S(18) + nameW, mid - Ground.PreferredHeight / 2);

            int sx = x + S(18) + nameW + groundW + gap;
            int sw = x + w - valueW - gap - sx;
            Slider.Bounds = new Rectangle(sx, mid - S(12), Math.Max(S(60), sw), S(24));

            Value.AutoSize = false;
            Value.Bounds = new Rectangle(x + w - valueW, mid - Value.PreferredHeight / 2, valueW, Value.PreferredHeight);
            return y + height;
        }
    }

    /// <summary>A panel that paints without flicker, for the dots under the rows.</summary>
    private sealed class Canvas : Panel
    {
        public Canvas()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    /// <summary>The whole land as one bar, each people's share in its colour.</summary>
    private sealed class MixBar : Control
    {
        private List<(Color Colour, double Share)> _segments = [];

        public MixBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Share of the land";
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public List<(Color Colour, double Share)> Segments
        {
            get => _segments;
            set { _segments = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var frame = new RectangleF(0, 0, Width - 1, Height - 1);
            using var path = Rounded(frame, Height / 2f);
            using (var track = new SolidBrush(Theme.SurfaceHigh)) g.FillPath(track, path);

            double total = _segments.Sum(s => Math.Max(0, s.Share));
            if (total <= 0) return;

            g.SetClip(path);
            float x = 0;
            foreach (var (colour, share) in _segments)
            {
                if (share <= 0) continue;
                float w = (float)(frame.Width * share / total);
                using var brush = new SolidBrush(colour);
                g.FillRectangle(brush, x, 0, w + 1, Height);
                x += w;
                // A hairline between neighbours so two similar colours still read as two.
                using var gap = new Pen(Theme.Background, 1.5f);
                g.DrawLine(gap, x, 0, x, Height);
            }
            g.ResetClip();
        }
    }
}

/// <summary>
/// A flat slider in the launcher's look: a thin track filled up to a round thumb. Drag or click
/// along it; arrows move it by one, Page Up and Down by ten, Home and End to the ends.
///
/// Painted rather than a TrackBar, which draws itself from system colours and ignores the dark
/// palette. It is focusable and says it is a slider to screen readers, with its value.
/// </summary>
internal sealed class MixSlider : Control
{
    private int _value;
    private bool _dragging;
    private bool _hover;

    public MixSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable
                 | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = true;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.Slider;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Minimum { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum { get; set; } = 100;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Theme.Accent;

    /// <summary>Raised when the user moves the slider; setting <see cref="Value"/> in code does not raise it.</summary>
    public event Action<int>? ValueChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value) return;
            _value = v;
            AccessibleDescription = v.ToString();
            Invalidate();
        }
    }

    private int S(int logical) => LaunchUi.S(this, logical);

    private void SetByUser(int value)
    {
        int before = _value;
        Value = value;
        if (_value != before) ValueChanged?.Invoke(_value);
    }

    private float Radius => S(8);
    private float TrackLeft => Radius + S(2);
    private float TrackRight => Width - Radius - S(2);

    private void MoveTo(int x)
    {
        float span = Math.Max(1f, TrackRight - TrackLeft);
        double frac = Math.Clamp((x - TrackLeft) / span, 0, 1);
        SetByUser(Minimum + (int)Math.Round(frac * (Maximum - Minimum)));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Capture = true;
        MoveTo(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) MoveTo(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Left or Keys.Right or Keys.Up or Keys.Down => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int? to = e.KeyCode switch
        {
            Keys.Left or Keys.Down => _value - 1,
            Keys.Right or Keys.Up => _value + 1,
            Keys.PageDown => _value - 10,
            Keys.PageUp => _value + 10,
            Keys.Home => Minimum,
            Keys.End => Maximum,
            _ => null,
        };
        if (to is not { } v) return;
        SetByUser(v);
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float cy = Height / 2f;
        float frac = Maximum > Minimum ? (float)(_value - Minimum) / (Maximum - Minimum) : 0f;
        float tx = TrackLeft + frac * (TrackRight - TrackLeft);

        var track = new RectangleF(TrackLeft, cy - S(2), Math.Max(1, TrackRight - TrackLeft), S(4));
        using (var path = Rounded(track, track.Height / 2))
        using (var brush = new SolidBrush(_hover || _dragging ? Theme.TrackHover : Theme.Track))
            g.FillPath(brush, path);

        if (tx > TrackLeft)
        {
            var filled = new RectangleF(TrackLeft, track.Y, tx - TrackLeft, track.Height);
            using var path = Rounded(filled, track.Height / 2);
            using var brush = new SolidBrush(Enabled ? Fill : Theme.TextFaint);
            g.FillPath(brush, path);
        }

        float r = Radius;
        var thumb = new RectangleF(tx - r, cy - r, 2 * r, 2 * r);
        using (var brush = new SolidBrush(Theme.Surface)) g.FillEllipse(brush, thumb);
        using (var pen = new Pen(Enabled ? Fill : Theme.TextFaint, _hover || _dragging ? 3f : 2f)) g.DrawEllipse(pen, thumb);

        if (Focused && ShowFocusCues)
        {
            var ring = thumb;
            ring.Inflate(S(3), S(3));
            using var pen = new Pen(Color.FromArgb(140, Theme.Accent), 1.5f) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(pen, ring);
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessible(this);

    /// <summary>Its value, readable and settable, so a screen reader hears "Dwarves, slider, 50".</summary>
    private sealed class SliderAccessible(MixSlider owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Slider;

        public override string? Value
        {
            get => owner.Value.ToString();
            set { if (int.TryParse(value, out int v)) owner.SetByUser(v); }
        }
    }
}
