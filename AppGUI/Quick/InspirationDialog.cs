using System.Drawing.Drawing2D;
using Ck3MapGen.Config;
using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The People step's inspiration: which corner of the real world the invented peoples are modelled
/// on. Opened from the link beside the Cultures &amp; faiths heading; edits a copy of the page's
/// <see cref="QuickInspiration"/>, which the page takes back on Done.
///
/// Nine tiles do almost all of it, each setting the culture (names, dress, buildings, heraldry) and
/// the faces together. Under them, folded away, the two can be set apart as rows of chips; a pairing
/// no tile makes leaves no tile selected. The folded part opens by itself when the inspiration it is
/// given is one of those.
///
/// The note under it all watches the world's climate, which the World step already chose: a Norse
/// wardrobe on a Warm map puts most of the peoples in the least-wrong furs, and the note says so
/// and offers the climate that suits. The climate is only changed if the player takes it up, and
/// only on Done (<see cref="Climate"/>).
///
/// Wears the app's drawn caption (<see cref="ChromeForm"/>), like the race mix dialog beside it.
/// </summary>
internal sealed class InspirationDialog : ChromeForm
{
    /// <summary>The content column's width, in 96-dpi pixels: the race mix dialog's.</summary>
    private const int Inner = 580;

    private const int Gutter = 22;
    private const int Columns = 3;

    private static readonly Dictionary<QuickClimate, string> ClimateNames = new()
        { [QuickClimate.Northern] = "Northern", [QuickClimate.Temperate] = "Temperate", [QuickClimate.Warm] = "Warm", [QuickClimate.Globe] = "Whole globe" };

    private static readonly Dictionary<QuickClimate, string> MapOf = new()
        { [QuickClimate.Northern] = "a northern map", [QuickClimate.Temperate] = "a temperate map", [QuickClimate.Warm] = "a warm map", [QuickClimate.Globe] = "a whole-globe map" };

    private readonly QuickInspiration _inspiration;
    private readonly QuickClimate _startClimate;
    private readonly bool _races;
    private QuickClimate _climate;
    private bool _apart;

    private readonly Canvas _body = new();
    private readonly Label _intro = MakeLabel(
        "Model the world's peoples on a corner of the real one. Their names, dress, buildings, "
        + "heraldry and faces all follow it. Each people still dresses for its own ground, from what the region wears.",
        Small, Theme.TextDim, wrap: true);
    private readonly List<(QuickInspiration.Preset Preset, ChoiceCard Card)> _tiles = [];
    private readonly TextLink _apartLink = new() { Name = "inspirationApart", LinkFont = Small };

    private readonly Label _cultureTitle = MakeLabel("Culture", Strong, Theme.Text);
    private readonly Label _cultureHint = MakeLabel("Names, dress, buildings and heraldry", Small, Theme.TextDim);
    private readonly List<(QuickInspiration.CultureOption Option, Chip Chip)> _cultureChips = [];
    private readonly Label _facesTitle = MakeLabel("Faces", Strong, Theme.Text);
    private readonly Label _facesHint = MakeLabel("The vanilla faces humans are drawn from", Small, Theme.TextDim);
    private readonly List<(QuickInspiration.FaceOption Option, Chip Chip)> _faceChips = [];

    private readonly Label _note = MakeLabel("", Small, Theme.TextDim, wrap: true);
    private readonly TextLink _climateLink = new() { Name = "inspirationClimate", LinkFont = Small };
    private readonly TextLink _reset = new() { Name = "inspirationReset", Text = "Reset to Anywhere", LinkFont = Small };
    private readonly PillButton _cancel = new() { Name = "inspirationCancel", Text = "Cancel", Kind = PillKind.Secondary, MinWidth = 96 };
    private readonly PillButton _done = new() { Name = "inspirationDone", Text = "Done", Kind = PillKind.Primary, MinWidth = 96 };

    /// <summary>The inspiration as the dialog left it. Read it after Done.</summary>
    public QuickInspiration Inspiration => _inspiration;

    /// <summary>The world's climate after Done: the one it came with, unless the note's offer was taken.</summary>
    public QuickClimate Climate => _climate;

    /// <param name="races">Whether the world has fantasy races, which the note then speaks for.</param>
    public InspirationDialog(QuickInspiration inspiration, QuickClimate climate, bool races)
    {
        _inspiration = inspiration.Clone();
        _startClimate = _climate = climate;
        _races = races;
        _apart = _inspiration.MatchingPreset is null;

        Text = "Inspiration";
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

        foreach (var preset in QuickInspiration.Presets)
        {
            var card = new ChoiceCard { Text = preset.Name, Subtitle = preset.Blurb, Name = "inspiration-" + preset.Key, AccessibleName = preset.Name };
            card.Click += (_, _) => Set(preset.Theme, preset.Look);
            _tiles.Add((preset, card));
        }

        foreach (var option in QuickInspiration.Cultures)
        {
            var chip = new Chip { Text = option.Name, Name = "inspirationCulture-" + option.Theme, AccessibleName = option.Name + " culture" };
            chip.Click += (_, _) => Set(option.Theme, _inspiration.Look);
            _cultureChips.Add((option, chip));
        }

        foreach (var option in QuickInspiration.Faces)
        {
            var chip = new Chip { Text = option.Name, Name = "inspirationFaces-" + option.Look, AccessibleName = option.Name + " faces" };
            chip.Click += (_, _) => Set(_inspiration.Theme, option.Look);
            _faceChips.Add((option, chip));
        }

        _apartLink.Click += (_, _) =>
        {
            _apart = !_apart;
            Describe();
            Refit();
        };
        _climateLink.Click += (_, _) =>
        {
            // An offer taken can be taken back: the link then puts the World step's climate back.
            _climate = _climate == _startClimate ? _inspiration.Culture.Suits : _startClimate;
            Describe();
        };
        _reset.Click += (_, _) => Set(MapConfig.CultureLookTheme.VariedGlobal, MapConfig.HumanLook.Varied);
        _done.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        _body.Controls.AddRange([_intro, _apartLink, _cultureTitle, _cultureHint, _facesTitle, _facesHint,
            _note, _climateLink, _reset, _cancel, _done]);
        foreach (var (_, card) in _tiles) _body.Controls.Add(card);
        foreach (var (_, chip) in _cultureChips) _body.Controls.Add(chip);
        foreach (var (_, chip) in _faceChips) _body.Controls.Add(chip);
        _body.Layout += (_, _) =>
        {
            // The load-time measure of the wrapped labels comes out lines too tall (measured: 45 to
            // 105 px of dead space under the buttons), so the window follows whatever the content
            // really came to. After this layout, not inside it: WinForms drops a resize asked for
            // from within one.
            int height = Arrange(_body.ClientSize.Width);
            if (IsHandleCreated && height != _body.ClientSize.Height) BeginInvoke(Refit);
        };

        Controls.Add(_body);

        // Added last so they dock first: the caption row on top, a hairline under it. No icon — a
        // dialog of the main window, which already shows it.
        ShowIcon = false;
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(CaptionBar = new TitleBar(this));

        Describe();
    }

    private int S(int logical) => LaunchUi.S(this, logical);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Refit();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.ApplyTitleBar(this);
        (_tiles.FirstOrDefault(t => t.Card.Selected).Card ?? (Control)_apartLink).Focus();
    }

    /// <summary>As tall as the content comes to, which the folded part changes.</summary>
    /// <remarks>
    /// By how far the content and the body differ, not by adding up the rows above it: the drawn
    /// frame hands the system caption's height to the client area on top of the caption row, so
    /// "content + caption row" came out a system caption too tall.
    /// </remarks>
    private void Refit()
    {
        if (IsDisposed) return;
        int width = S(Inner) + 2 * S(Gutter);
        int delta = Arrange(width) - _body.ClientSize.Height;
        if (delta != 0 || ClientSize.Width != width) ClientSize = new Size(width, ClientSize.Height + delta);
    }

    // ------------------------------------------------------------------ state

    private void Set(MapConfig.CultureLookTheme theme, MapConfig.HumanLook look)
    {
        _inspiration.Theme = theme;
        _inspiration.Look = look;
        // A climate taken for the last culture is not owed to this one; the note offers again if it must.
        _climate = _startClimate;
        Describe();
    }

    /// <summary>The selections, the fold, and the note, from the inspiration as it stands.</summary>
    private void Describe()
    {
        var match = _inspiration.MatchingPreset;
        foreach (var (preset, card) in _tiles) card.Selected = ReferenceEquals(preset, match);
        foreach (var (option, chip) in _cultureChips) chip.Selected = option.Theme == _inspiration.Theme;
        foreach (var (option, chip) in _faceChips) chip.Selected = option.Look == _inspiration.Look;

        _apartLink.Glyph = _apart ? "" : "";
        // Folded over a pairing no tile makes, the link is the only place it shows.
        _apartLink.Text = match is null && !_apart
            ? $"Set apart  ·  {_inspiration.Describe()}"
            : "Set culture and faces apart";
        foreach (var c in (Control[])[_cultureTitle, _cultureHint, _facesTitle, _facesHint]) c.Visible = _apart;
        foreach (var (_, chip) in _cultureChips) chip.Visible = _apart;
        foreach (var (_, chip) in _faceChips) chip.Visible = _apart;

        _reset.Enabled = !_inspiration.IsDefault;

        var culture = _inspiration.Culture;
        string races = _races
            ? " Elves, dwarves and the other races keep their own faces and tongues, but dress and build like the peoples around them."
            : "";
        if (_climate != _startClimate)
        {
            _note.ForeColor = Theme.TextDim;
            _note.Text = $"The world's climate becomes {ClimateNames[_climate]}, to suit {culture.Dress}.{races}";
            _climateLink.Text = $"Keep {ClimateNames[_startClimate]} climate";
            _climateLink.Visible = true;
        }
        else if (_inspiration.Unsuits(_climate))
        {
            // Full text colour rather than danger: advice, not an error. Done still goes ahead.
            _note.ForeColor = Theme.Text;
            _note.Text = $"{culture.Dress} has nothing made for {culture.Lacks}, and there is much of that on "
                         + $"{MapOf[_climate]}. The peoples who live there wear the nearest it has.{races}";
            _climateLink.Text = $"Use {ClimateNames[culture.Suits]} climate";
            _climateLink.Visible = true;
        }
        else
        {
            _note.ForeColor = Theme.TextDim;
            _note.Text = _inspiration.IsDefault
                ? "Anywhere deals every region's looks out across the map, each people where its climate suits it." + races
                : races.TrimStart();
            _climateLink.Visible = false;
        }

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

        // The tiles, three to a row, each row as tall as its wordiest card.
        int gap = S(10);
        int cw = (w - gap * (Columns - 1)) / Columns;
        for (int row = 0; row * Columns < _tiles.Count; row++)
        {
            var cards = _tiles.Skip(row * Columns).Take(Columns).Select(t => t.Card).ToList();
            int h = cards.Max(c => c.HeightFor(cw));
            for (int i = 0; i < cards.Count; i++)
                cards[i].Bounds = new Rectangle(x + i * (cw + gap), y, i == Columns - 1 ? w - i * (cw + gap) : cw, h);
            y += h + gap;
        }

        y += S(2);
        _apartLink.Location = new Point(x - S(2), y);
        y += _apartLink.Height + S(4);

        if (_apart)
        {
            y = Section(_cultureTitle, _cultureHint, _cultureChips.Select(c => c.Chip), x, y + S(4), w) + S(10);
            y = Section(_facesTitle, _facesHint, _faceChips.Select(c => c.Chip), x, y, w);
        }

        y += S(12);
        if (!string.IsNullOrEmpty(_note.Text))
        {
            y += StepPanel.Place(_note, x, y, w) + S(2);
            _note.Visible = true;
        }
        else _note.Visible = false;
        if (_climateLink.Visible)
        {
            _climateLink.Location = new Point(x - S(2), y);
            y += _climateLink.Height;
        }
        y += S(14);

        _done.Location = new Point(x + w - _done.Width, y);
        _cancel.Location = new Point(_done.Left - S(8) - _cancel.Width, y);
        _reset.Location = new Point(x - S(2), y + (_done.Height - _reset.Height) / 2);
        return y + _done.Height + S(16);
    }

    /// <summary>A title with its hint beside it, then chips wrapping over as many lines as they need.</summary>
    private int Section(Label title, Label hint, IEnumerable<Chip> chips, int x, int y, int w)
    {
        y = StepPanel.TitleAndHint(title, hint, x, y, w) + S(6);
        int cx = x, gap = S(8), rowH = 0;
        foreach (var chip in chips)
        {
            chip.FitSize();
            if (cx > x && cx + chip.Width > x + w)
            {
                cx = x;
                y += rowH + gap;
            }
            chip.Location = new Point(cx, y);
            cx += chip.Width + gap;
            rowH = Math.Max(rowH, chip.Height);
        }
        return y + rowH;
    }

    /// <summary>A panel that paints without flicker.</summary>
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

    /// <summary>
    /// One option of a folded-away row: a small rounded pill, washed in the accent when chosen.
    /// A <see cref="ChoiceCard"/> in miniature, for rows too long for cards.
    /// </summary>
    private sealed class Chip : PaintedButton
    {
        private bool _selected;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; AccessibleDescription = value ? "Selected" : null; Invalidate(); }
        }

        private Font TextFont => _selected ? SmallStrong : Small;

        /// <summary>The bold it takes when chosen, measured for either way so a chip never changes width.</summary>
        private static readonly Font SmallStrong = new("Segoe UI Semibold", 8.5f);

        public void FitSize()
        {
            int text = TextRenderer.MeasureText(Text ?? "", SmallStrong, Size.Empty, TextFormatFlags.NoPadding).Width;
            Size = new Size(text + S(24), S(28));
        }

        protected override void Draw(Graphics g)
        {
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = box.Height / 2;
            using var path = Rounded(box, radius);

            var fill = _selected ? SelectedWash : Hover && Enabled ? Theme.SurfaceHover : Theme.Surface;
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            var edge = _selected ? Theme.Accent : Hover && Enabled ? Color.FromArgb(150, Theme.Accent) : Theme.Border;
            using (var pen = new Pen(edge, _selected ? 1.5f : 1f)) g.DrawPath(pen, path);

            TextRenderer.DrawText(g, Text, TextFont, Rectangle.Round(box), _selected ? Theme.Accent : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            DrawFocus(g, box, radius);
        }
    }
}
