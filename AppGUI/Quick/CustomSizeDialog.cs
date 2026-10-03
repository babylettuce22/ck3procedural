using static Ck3MapGen.AppGUI.LaunchUi;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The World step's Custom size: a width and height of the player's own. Opened by picking the
/// Custom card (or picking it again); the page takes the size back on Done.
///
/// Any size in range is allowed, snapped to <see cref="QuickChoices.CustomStep"/>, but the dialog
/// says plainly whether CK3 is known to render it (<see cref="MapGen.TileFit.Known"/>), and lists
/// the sizes that are, one click each. An unknown size is built anyway, as a test: that is how the
/// list grows.
///
/// The height follows the width at 2:1 while it is at 2:1, the only shape tested so far; typing a
/// height of its own stops that.
/// </summary>
internal sealed class CustomSizeDialog : ChromeForm
{
    private const int Inner = 440;
    private const int Gutter = 22;

    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Label _intro = MakeLabel(
        "The heightmap's size in pixels. Provinces are drawn at half of it. Bigger maps take longer to make.",
        Small, Theme.TextDim, wrap: true);
    private readonly Label _widthCaption = MakeLabel("Width", Small, Theme.TextDim);
    private readonly Label _heightCaption = MakeLabel("Height", Small, Theme.TextDim);
    private readonly TextBox _width = new() { BorderStyle = BorderStyle.FixedSingle, Font = Body, Name = "customSizeWidth" };
    private readonly TextBox _height = new() { BorderStyle = BorderStyle.FixedSingle, Font = Body, Name = "customSizeHeight" };
    private readonly Label _times = MakeLabel("×", Body, Theme.TextDim);
    private readonly Label _builds = MakeLabel("", Strong, Theme.Text);
    private readonly Label _knownCaption = MakeLabel("Known to render in CK3", Small, Theme.TextDim);
    private readonly List<TextLink> _known = [];
    private readonly Label _note = MakeLabel("", Small, Theme.TextDim, wrap: true);
    private readonly PillButton _cancel = new() { Name = "customSizeCancel", Text = "Cancel", Kind = PillKind.Secondary, MinWidth = 96 };
    private readonly PillButton _done = new() { Name = "customSizeDone", Text = "Done", Kind = PillKind.Primary, MinWidth = 96 };

    private bool _setting;

    /// <summary>Whether the height follows the width at 2:1; see the class summary.</summary>
    private bool _follow;

    /// <summary>The size as the dialog left it, snapped. Read it after Done.</summary>
    public (int Width, int Height) Pixels { get; private set; }

    public CustomSizeDialog(int width, int height)
    {
        Pixels = QuickChoices.SnapCustom(width, height);

        Text = "Custom size";
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

        // Smallest first, as TileFit.KnownList reads them.
        foreach (var (w, h) in MapGen.TileFit.Known.Reverse())
        {
            var link = new TextLink { Text = $"{w} × {h}", LinkFont = Small, Name = $"customSize{w}" };
            link.Click += (_, _) => Set(w, h);
            _known.Add(link);
        }

        _width.TextChanged += (_, _) =>
        {
            if (_setting) return;
            if (_follow && Parse(_width, out int w))
            {
                _setting = true;
                _height.Text = (w / 2).ToString();
                _setting = false;
            }
            Describe();
        };
        _height.TextChanged += (_, _) =>
        {
            if (_setting) return;
            // Typed back to 2:1, it follows again.
            _follow = Parse(_width, out int w) && Parse(_height, out int h) && h * 2 == w;
            Describe();
        };
        _width.Leave += (_, _) => Set(Pixels.Width, Pixels.Height);
        _height.Leave += (_, _) => Set(Pixels.Width, Pixels.Height);

        _done.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        _body.Controls.AddRange([_intro, _widthCaption, _heightCaption, _width, _times, _height, _builds, _knownCaption, _note, _cancel, _done]);
        foreach (var link in _known) _body.Controls.Add(link);
        _body.Layout += (_, _) =>
        {
            // As RaceMixDialog: follow what the content came to, after this layout rather than in it.
            int contentH = Arrange(_body.ClientSize.Width);
            if (IsHandleCreated && contentH != _body.ClientSize.Height) BeginInvoke(Refit);
        };
        Controls.Add(_body);

        ShowIcon = false;
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(CaptionBar = new TitleBar(this));

        Set(Pixels.Width, Pixels.Height);
    }

    private int S(int logical) => LaunchUi.S(this, logical);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Refit();
    }

    private void Refit()
    {
        if (IsDisposed) return;
        int width = S(Inner) + 2 * S(Gutter);
        int delta = Arrange(width) - _body.ClientSize.Height;
        if (delta != 0 || ClientSize.Width != width) ClientSize = new Size(width, ClientSize.Height + delta);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.ApplyTitleBar(this);
        _width.Focus();
        _width.SelectAll();
    }

    private static bool Parse(TextBox box, out int value)
        => int.TryParse(box.Text.Trim().Replace(",", "").Replace(" ", ""), out value) && value > 0;

    /// <summary>Puts a size in both boxes, snapped.</summary>
    private void Set(int width, int height)
    {
        var (w, h) = QuickChoices.SnapCustom(width, height);
        _setting = true;
        _width.Text = w.ToString();
        _height.Text = h.ToString();
        _setting = false;
        _follow = h * 2 == w;
        Describe();
    }

    /// <summary>The snapped size, the line under the boxes, and whether CK3 is known to render it.</summary>
    private void Describe()
    {
        bool ok = Parse(_width, out int w) & Parse(_height, out int h);
        _done.Enabled = ok;
        if (!ok)
        {
            _builds.Text = "";
            _note.ForeColor = Theme.Danger;
            _note.Text = "Enter a width and a height in pixels.";
            _body.PerformLayout();
            return;
        }

        Pixels = QuickChoices.SnapCustom(w, h);
        var (pw, ph) = Pixels;
        _builds.Text = (pw, ph) == (w, h) ? "" : $"Builds at {pw} × {ph}";

        bool known = MapGen.TileFit.Fits(pw, ph);
        foreach (var link in _known)
        {
            link.LinkFont = link.Text == $"{pw} × {ph}" ? Strong : Small;
            link.FitWidth();
            link.Invalidate();
        }

        string range = $"Sides snap to multiples of {QuickChoices.CustomStep}, from {QuickChoices.MinCustomWidth} × {QuickChoices.MinCustomHeight} "
                       + $"to {QuickChoices.MaxCustomWidth} × {QuickChoices.MaxCustomHeight}.";
        _note.ForeColor = known ? Good : Theme.Danger;
        _note.Text = known
            ? $"✓  CK3 is known to render this size. {range}"
            : "CK3 is not known to render this size. Others have left terrain undrawn along the map's north and east "
              + "edges, with nothing logged, and every size between 9216 and 18432 wide tried so far has. It will be built "
              + $"anyway, as a test: check those edges in game. {range}";
        _body.PerformLayout();
    }

    /// <summary>Places everything for a client width and returns the height it came to.</summary>
    private int Arrange(int width)
    {
        int x = S(Gutter);
        int w = width - 2 * x;
        int y = S(16);

        y += StepPanel.Place(_intro, x, y, w) + S(14);

        int boxW = S(110), timesW = S(24);
        _widthCaption.Location = new Point(x, y);
        _heightCaption.Location = new Point(x + boxW + timesW, y);
        y += _widthCaption.PreferredHeight + S(3);
        _width.Bounds = new Rectangle(x, y, boxW, _width.PreferredHeight);
        _times.Location = new Point(x + boxW + (timesW - _times.PreferredWidth) / 2, y + (_width.Height - _times.PreferredHeight) / 2);
        _height.Bounds = new Rectangle(x + boxW + timesW, y, boxW, _height.PreferredHeight);
        _builds.Location = new Point(_height.Right + S(14), y + (_height.Height - _builds.PreferredHeight) / 2);
        y += _width.Height + S(16);

        _knownCaption.Location = new Point(x, y);
        y += _knownCaption.PreferredHeight + S(2);
        int lx = x - S(4);
        foreach (var link in _known)
        {
            if (lx + link.Width > x + w) { lx = x - S(4); y += link.Height; }
            link.Location = new Point(lx, y);
            lx += link.Width + S(4);
        }
        y += (_known.Count > 0 ? _known[0].Height : 0) + S(12);

        y += StepPanel.Place(_note, x, y, w) + S(16);

        _done.Location = new Point(x + w - _done.Width, y);
        _cancel.Location = new Point(_done.Left - S(8) - _cancel.Width, y);
        return y + _done.Height + S(16);
    }
}
