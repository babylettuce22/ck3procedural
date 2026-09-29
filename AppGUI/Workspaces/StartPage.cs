using System.Drawing.Drawing2D;
using System.Globalization;
using NoiseTool.Core;
using NoiseTool.Pipeline;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// What the window shows when it opens: a choice between importing an Azgaar map, the Quick
/// generator and the Complex one, with the few things worth doing before any of them (opening a world already written, the guide, and
/// telling the tool where the game is).
///
/// It is a page of the main window rather than a window of its own. A launcher shown ahead of the
/// main form would flash a second window up and away, and there would be no way back to it
/// once dismissed. Inside the main window it wears the same drawn caption as everything else and
/// File ▸ Start page brings it back.
///
/// The banner is a real map. On each visit a shipped Forge preset is previewed at a random seed,
/// the same kind of map the Quick generator will pick from. It runs off the UI thread at preview
/// size, so it costs about a second of background work, and a failure just leaves the plain
/// placeholder.
/// </summary>
internal sealed class StartPage : Panel
{
    /// <summary>The Azgaar card: import a map made in Azgaar's Fantasy Map Generator.</summary>
    public event Action? AzgaarPicked;
    /// <summary>The Quick card. The host decides what a click means.</summary>
    public event Action? QuickPicked;
    public event Action? ComplexPicked;
    public event Action? OpenWorldPicked;
    public event Action? GuidePicked;
    public event Action? GameFolderPicked;

    /// <summary>The "Remember my choice" switch was flipped by the user.</summary>
    public event Action<bool>? RememberChanged;

    /// <summary>The corner's dark-mode glyph was clicked. The host flips the saved choice and calls <see cref="SetDarkMode"/>.</summary>
    public event Action? DarkModeToggled;

    private static readonly Font TitleFont = new("Segoe UI Semibold", 20f);
    private static readonly Font SubtitleFont = new("Segoe UI", 10.5f);
    private static readonly Font FooterFont = new("Segoe UI", 9f);

    private readonly MapBanner _banner = new();
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly ModeCard _azgaar;
    private readonly ModeCard _quick;
    private readonly ModeCard _complex;
    private readonly LinkButton _openWorld;
    private readonly LinkButton _guide;
    private readonly Panel _rule = new() { BackColor = Theme.Border };
    private readonly StatusGlyph _gameGlyph = new();
    private readonly FooterText _gameText = new();
    private readonly LinkButton _gameChange;
    private readonly RememberSwitch _remember = new() { Name = "startRemember" };
    private readonly ThemeGlyph _theme = new() { Name = "startTheme" };
    private readonly WrappingToolTip _tips = new() { InitialDelay = 400 };

    public StartPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        Name = "startPage";

        _title = new Label
        {
            Text = "Make a world",
            Font = TitleFont,
            ForeColor = Theme.Text,
            AutoSize = true,
            BackColor = Color.Transparent,
        };

        // Wraps onto a second line on a narrow window rather than being cut short.
        _subtitle = new Label
        {
            Text = "Generate a new Crusader Kings III map — terrain, realms, cultures and faiths — as a playable mod.",
            Font = SubtitleFont,
            ForeColor = Theme.TextDim,
            AutoSize = false,
            BackColor = Color.Transparent,
        };

        _azgaar = new ModeCard
        {
            Name = "startAzgaar",
            Glyph = "",
            Title = "Azgaar",
            Tagline = "Bring a map drawn in Azgaar. Its countries, peoples and faiths come too.",
            Features = ["Your map", "Countries", "Cultures"],
            Action = "Import in three steps",
            Available = true,
        };

        _quick = new ModeCard
        {
            Name = "startQuick",
            Glyph = "",
            Title = "Quick",
            Badge = "Recommended",
            Tagline = "Pick a map type and a few basics, and let good defaults do the rest.",
            Features = ["Map types", "Size", "Era", "Climate"],
            Action = "Make a world in four steps",
            Available = true,
        };

        _complex = new ModeCard
        {
            Name = "startComplex",
            Glyph = "",
            Title = "Complex",
            Tagline = "The full generator: shape terrain, paint the climate, tune every setting.",
            Features = ["Terrain forge", "Climate paint", "History"],
            Action = "Open the generator",
            Available = true,
        };

        _openWorld = new LinkButton { Name = "startOpenWorld", Glyph = "", Text = "Open a generated world…" };
        _guide = new LinkButton { Name = "startGuide", Glyph = "", Text = "Getting started" };

        _gameChange = new LinkButton { Name = "startGameFolder", Text = "Change…" };

        _azgaar.Click += (_, _) => AzgaarPicked?.Invoke();
        _quick.Click += (_, _) => QuickPicked?.Invoke();
        _complex.Click += (_, _) => ComplexPicked?.Invoke();
        _openWorld.Click += (_, _) => OpenWorldPicked?.Invoke();
        _guide.Click += (_, _) => GuidePicked?.Invoke();
        _gameChange.Click += (_, _) => GameFolderPicked?.Invoke();
        _remember.Click += (_, _) =>
        {
            _remember.On = !_remember.On;
            if (!_remember.On) _remember.Remembered = null;
            RememberChanged?.Invoke(_remember.On);
            PerformLayout();
        };
        _tips.SetToolTip(_remember,
            "Open straight into whichever you pick next — Azgaar, Quick or Complex — from now on.\n"
            + "The Start link on the Quick page, or File ▸ Start page, brings this page back.");
        _theme.Click += (_, _) => DarkModeToggled?.Invoke();

        // A window too short for the page scrolls it; see OnLayout.
        WheelFollowsMouse.Install();
        Controls.AddRange([_banner, _title, _subtitle, _azgaar, _quick, _complex, _openWorld, _guide, _remember,
                           _rule, _gameGlyph, _gameText, _gameChange, _theme]);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        StepPanel.Unanchor(e.Control);
    }

    /// <summary>
    /// What the corner glyph shows: the saved choice, which differs from the palette on screen
    /// between a click and the next launch — and then says so, since nothing else on the page moves.
    /// </summary>
    public void SetDarkMode(bool saved)
    {
        _theme.Saved = saved;
        string tip = saved ? "Switch to light mode" : "Switch to dark mode";
        if (saved != Theme.Dark) tip = (saved ? "Dark" : "Light") + " mode on the next launch";
        _tips.SetToolTip(_theme, tip);
    }

    private int S(int logical) => logical * DeviceDpi / 96;

    /// <summary>Brings the footer up to date and draws a fresh banner. Called each time the page comes on screen.</summary>
    public void Present(bool gameFound, string gameDir, string modRoot)
    {
        SetGameFolder(gameFound, gameDir, modRoot);
        _banner.Draw();
    }

    /// <summary>What the "Remember my choice" switch shows: whether it is on, and what it remembers.</summary>
    public void SetRemember(bool on, string? remembered)
    {
        _remember.On = on;
        _remember.Remembered = on ? remembered : null;
        PerformLayout();
    }

    /// <summary>
    /// The one prerequisite either generator has, stated up front: a mod can be previewed without
    /// the game, but not written, and finding that out after a five-minute run is the wrong time.
    /// </summary>
    public void SetGameFolder(bool found, string gameDir, string modRoot)
    {
        _gameGlyph.Ok = found;
        _gameText.ForeColor = found ? Theme.TextDim : Theme.Danger;
        _gameText.Parts = found
            ? [$"Crusader Kings III found at {gameDir}", $"Mods are written to {modRoot}"]
            : ["Crusader Kings III was not found. Set the game folder before writing a mod."];
        _gameText.Text = string.Join("     ·     ", _gameText.Parts);
        _tips.SetToolTip(_gameText, found ? string.Join("\n", _gameText.Parts) : null);
        _gameChange.Text = found ? "Change…" : "Set game folder…";
        PerformLayout();
    }

    /// <summary>
    /// One centred column, top to bottom: banner, title, subtitle, the two cards, the links and
    /// the footer. The banner is the part that gives: on a short window it shrinks first and then
    /// goes, so the cards, which are the page's purpose, never fall off the bottom.
    /// </summary>
    /// <summary>
    /// The column's width at the size the window opens at, and the margin kept either side of it.
    /// The page is meant to be seen as a small launch window, so <see cref="PreferredPageSize"/> is
    /// this column plus these margins and nothing more. A window made wider widens the column
    /// with it, as every launcher page does (<see cref="StepPanel.Column"/>).
    /// </summary>
    private int ColumnWidth => S(StepPanel.PreferredColumn);
    private int SideMargin => S(28);
    private int TopPad => S(26);
    private int BottomPad => S(20);
    private int BannerGap => S(26);
    private int FullBannerHeight(int width) => Math.Min(S(230), width * 30 / 100);

    /// <summary>Everything in the column below the banner, top of the title to foot of the footer, at a column width.</summary>
    private int BodyHeight(int width)
        => _title.PreferredHeight + S(4) + StepPanel.Wrapped(_subtitle, width) + S(24)
           + CardHeight(width) + S(16) + LinkRowHeight + S(18) + S(1) + S(10) + FooterHeight(width);

    private ModeCard[] Cards => [_azgaar, _quick, _complex];
    private int CardGap => S(16);
    private int CardWidth(int width) => (width - 2 * CardGap) / 3;

    /// <summary>
    /// Each card's tagline block: as tall as the longest tagline wraps to, so the chips under the
    /// taglines stay level across the three cards.
    /// </summary>
    private int TaglineBlock(int width) => Math.Max(S(44), Cards.Max(c => c.TaglineHeight(CardWidth(width))));

    /// <summary>The cards' height: as designed, or taller when a narrow window wraps what one says onto more lines.</summary>
    private int CardHeight(int width)
        => Math.Max(S(226), Cards.Max(c => c.HeightFor(CardWidth(width), TaglineBlock(width))));

    private int LinkRowHeight => S(30);

    /// <summary>One line of footer, or two when the game's folder and the mod folder do not fit on one.</summary>
    private int FooterHeight(int width) => _gameText.HeightFor(FooterTextWidth(width));
    private int FooterTextWidth(int width) => width - S(18) - S(8) - S(12) - _gameChange.Width;

    /// <summary>
    /// The size the page wants to be shown at: the full column, the full banner and the margins,
    /// with no slack. The host sizes the window to this while the page is up.
    /// </summary>
    public Size PreferredPageSize
        => new(ColumnWidth + 2 * SideMargin,
               TopPad + FullBannerHeight(ColumnWidth) + BannerGap + BodyHeight(ColumnWidth) + BottomPad);

    /// <summary>
    /// Lays the column out, centred in the window when it fits; when it does not even without the
    /// banner, the page scrolls (<see cref="StepPanel.ArrangeScrolling"/>) rather than cutting off the foot.
    /// </summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_title is null) return;
        StepPanel.ArrangeScrolling(this, Arrange, () => AdjustFormScrollbars(AutoScroll));
    }

    /// <summary>Places everything in content coordinates and returns how tall the content is.</summary>
    private int Arrange()
    {
        int margin = Math.Max(SideMargin, ClientSize.Width * 3 / 100);
        int width = Math.Min(ClientSize.Width - 2 * margin, S(StepPanel.MaxColumn));
        if (width <= 0) return 0;
        int x = (ClientSize.Width - width) / 2;

        // Out of the column altogether, tucked in the page's corner above where the banner starts.
        _theme.Bounds = new Rectangle(ClientSize.Width - S(6) - S(24), S(2), S(24), S(24));

        int titleH = _title.PreferredHeight;
        int cardH = CardHeight(width);
        int linkH = LinkRowHeight;
        int footerH = FooterHeight(width);

        int rest = BodyHeight(width);
        int topPad = TopPad;
        int bottomPad = BottomPad;
        int spare = ClientSize.Height - topPad - bottomPad - rest - BannerGap;
        int bannerH = Math.Min(FullBannerHeight(width), spare);
        bool showBanner = bannerH >= S(90);

        int used = rest + (showBanner ? bannerH + BannerGap : 0);
        int y = Math.Max(topPad, (ClientSize.Height - used - topPad - bottomPad) / 2 + topPad);

        _banner.Visible = showBanner;
        if (showBanner)
        {
            _banner.Bounds = new Rectangle(x, y, width, bannerH);
            y += bannerH + BannerGap;
        }

        _title.Location = new Point(x - S(3), y);
        y += titleH + S(4);
        y += StepPanel.Place(_subtitle, x, y, width) + S(24);

        // Three ways in, left to right from the most given to the most made: a map brought from
        // Azgaar, a Quick world, the full generator.
        int gap = CardGap;
        int cardW = CardWidth(width);
        int block = TaglineBlock(width);
        foreach (var card in Cards) card.TaglineBlock = block;
        _azgaar.Bounds = new Rectangle(x, y, cardW, cardH);
        _quick.Bounds = new Rectangle(x + cardW + gap, y, cardW, cardH);
        _complex.Bounds = new Rectangle(x + 2 * (cardW + gap), y, width - 2 * (cardW + gap), cardH);
        y += cardH + S(16);

        _openWorld.Location = new Point(x, y + (linkH - _openWorld.Height) / 2);
        _guide.Location = new Point(_openWorld.Right + S(24), _openWorld.Top);
        _remember.FitWidth();
        _remember.Location = new Point(x + width - _remember.Width, y + (linkH - _remember.Height) / 2);
        y += linkH + S(18);

        _rule.Bounds = new Rectangle(x, y, width, S(1));
        y += S(1) + S(10);

        int glyph = S(18);
        _gameGlyph.Bounds = new Rectangle(x, y + (footerH - glyph) / 2, glyph, glyph);
        _gameChange.Location = new Point(x + width - _gameChange.Width, y + (footerH - _gameChange.Height) / 2);
        _gameText.Bounds = Rectangle.FromLTRB(_gameGlyph.Right + S(8), y, _gameChange.Left - S(12), y + footerH);
        return y + footerH + bottomPad;
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// The glyph font the caption buttons already use; see <see cref="TitleBar"/>. Present on
    /// Windows 10 and 11, and 11's Fluent Icons keep its code points.
    /// </summary>
    private const string GlyphFamily = "Segoe MDL2 Assets";

    // ------------------------------------------------------------------ banner

    /// <summary>
    /// A map drawn from a random shipped preset at a random seed, clipped to a rounded frame and
    /// labelled with what it is. Clicking it draws another.
    /// </summary>
    private sealed class MapBanner : Control
    {
        private static readonly Font ChipFont = new("Segoe UI Semibold", 9f);
        private static readonly Font HintFont = new("Segoe UI", 9f);

        private Bitmap? _image;
        private string _label = "";
        private CancellationTokenSource? _cts;
        private bool _drawing;
        private bool _hover;

        public MapBanner()
        {
            Name = "startBanner";
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Example map";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            new WrappingToolTip { InitialDelay = 400 }.SetToolTip(this, "An example of what the generator's map types look like. Click for another.");
        }

        private int S(int logical) => logical * DeviceDpi / 96;

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Draw(); }

        /// <summary>Starts a render, replacing any still running. The old picture stays up until the new one lands.</summary>
        public void Draw()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "assets", "forge-presets");
            string[] presets;
            try { presets = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : []; }
            catch (IOException) { presets = []; }
            if (presets.Length == 0) return;

            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();

            string preset = presets[Random.Shared.Next(presets.Length)];
            int seed = Random.Shared.Next(1, 1_000_000);
            string name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                Path.GetFileNameWithoutExtension(preset).Replace('-', ' '));

            _drawing = true;
            Invalidate();

            Task.Run(() => Render(preset, seed, cts.Token), cts.Token).ContinueWith(task =>
            {
                if (IsDisposed || !IsHandleCreated) { task.Result?.Dispose(); return; }
                BeginInvoke(() =>
                {
                    if (cts.IsCancellationRequested || task.Result is null) { task.Result?.Dispose(); return; }
                    _image?.Dispose();
                    _image = task.Result;
                    _label = $"{name}  ·  seed {seed}";
                    _drawing = false;
                    Invalidate();
                });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        /// <summary>
        /// A preview run of the preset, exactly as the Terrain workspace would preview it. Erosion
        /// is a bake-only stage and is skipped here, as it is in any preview.
        /// </summary>
        private static Bitmap? Render(string presetPath, int seed, CancellationToken token)
        {
            try
            {
                var pipeline = new HeightPipeline();
                PresetIO.Load(pipeline, presetPath);
                pipeline.SeaLevel = Ck3.SeaLevelNormalised;
                pipeline.MasterSeed = seed;
                var result = pipeline.Run(1024, 512, isPreview: true, token);
                return HeightRenderer.Render(result.Field, pipeline.SeaLevel, RenderMode.Hypsometric);
            }
            catch (Exception)
            {
                // A missing or unreadable preset costs the page its picture, nothing more.
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Cancel();
                _image?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var frame = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using var path = Rounded(frame, S(10));

            if (_image is { } image)
            {
                // Cover: fill the frame and crop the overflow, keeping the map's middle band.
                float scale = Math.Max((float)Width / image.Width, (float)Height / image.Height);
                float w = image.Width * scale, h = image.Height * scale;
                var dest = new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);

                g.SetClip(path);
                g.DrawImage(image, dest);
                if (_hover)
                    using (var wash = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillPath(wash, path);
                g.ResetClip();

                DrawChip(g, _label, S(12), Height - S(12));
                if (_drawing) DrawChip(g, "Drawing another…", Width - S(12), Height - S(12), alignRight: true);
            }
            else
            {
                using var fill = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                    Color.FromArgb(22, 52, 104), Color.FromArgb(34, 84, 150), LinearGradientMode.Vertical);
                g.FillPath(fill, path);
                TextRenderer.DrawText(g, _drawing ? "Drawing a world…" : "", HintFont, ClientRectangle,
                    Color.FromArgb(200, 215, 235), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            using var border = new Pen(Color.FromArgb(40, 0, 0, 0));
            g.DrawPath(border, path);
        }

        private void DrawChip(Graphics g, string text, int x, int bottom, bool alignRight = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            var size = TextRenderer.MeasureText(text, ChipFont, Size.Empty, TextFormatFlags.NoPadding);
            var chip = new Rectangle(0, bottom - size.Height - S(10), size.Width + S(20), size.Height + S(10));
            chip.X = alignRight ? x - chip.Width : x;

            using var path = Rounded(chip, chip.Height / 2f);
            using var back = new SolidBrush(Color.FromArgb(215, Theme.Surface));
            g.FillPath(back, path);
            TextRenderer.DrawText(g, text, ChipFont, chip, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ------------------------------------------------------------------ cards

    /// <summary>
    /// One of the two ways in. The whole card is the button — a real <see cref="Button"/> that
    /// paints itself, like <see cref="Theme.SegmentButton"/>, rather than a Control that acts like
    /// one. That buys focus, Enter and Space, and a UI Automation push button whose automation id
    /// is its Name. A plain Control shows up there as an anonymous pane whatever its role says.
    /// </summary>
    private sealed class ModeCard : Button
    {
        private static readonly Font GlyphFont = new(GlyphFamily, 16f);
        private static readonly Font CardTitle = new("Segoe UI Semibold", 14f);
        private static readonly Font Body = new("Segoe UI", 9.5f);
        private static readonly Font ChipFont = new("Segoe UI", 8.5f);
        private static readonly Font BadgeFont = new("Segoe UI", 7.5f);
        private static readonly Font ActionFont = new("Segoe UI Semibold", 9.5f);
        private static readonly Font ArrowFont = new(GlyphFamily, 9f);

        private bool _hover;
        private bool _pressed;

        public ModeCard()
        {
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            BackColor = Theme.Background;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Glyph { get; init; } = "";
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Title { get => Text; init { Text = value; AccessibleName = value; } }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? Badge { get; init; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Tagline { get; init; } = "";
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string[] Features { get; init; } = [];
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Action { get; init; } = "";
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Available { get; init; } = true;

        /// <summary>
        /// How tall the tagline's block is: the page sets it to the longest of the three cards'
        /// taglines, so the chips under them line up. Never less than two lines.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int TaglineBlock { get; set; }

        private int S(int logical) => logical * DeviceDpi / 96;

        private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        /// <summary>The width inside the card's padding, at a width for the whole control.</summary>
        private int Inner(int width) => width - S(8) - 1 - 2 * S(22);

        /// <summary>How tall the tagline wraps to at a width for the whole control.</summary>
        public int TaglineHeight(int width)
            => TextRenderer.MeasureText(Tagline, Body, new Size(Math.Max(1, Inner(width)), 0), Wrap).Height;

        /// <summary>
        /// How tall the card has to be at <paramref name="width"/> to show everything it says, with
        /// the tagline in a block <paramref name="taglineBlock"/> tall: the chips wrap onto more rows
        /// and the action onto more lines rather than running off the card.
        /// </summary>
        public int HeightFor(int width, int taglineBlock)
        {
            int inner = Inner(width);
            return S(4) + S(22) + S(44) + S(14) + Math.Max(S(44), taglineBlock) + S(4)
                   + ChipsHeight(inner) + S(24) + ActionHeight(inner) + S(22) + S(6) + 1;
        }

        private int ChipHeight => TextRenderer.MeasureText("Ag", ChipFont, Size.Empty, TextFormatFlags.NoPadding).Height + S(8);

        /// <summary>The feature chips, row by row, each as wide as its text and no wider than the card.</summary>
        private List<List<(string Text, int Width)>> ChipRows(int inner)
        {
            var rows = new List<List<(string Text, int Width)>> { new() };
            int cx = 0;
            foreach (string feature in Features)
            {
                int w = Math.Min(inner, TextRenderer.MeasureText(feature, ChipFont, Size.Empty, TextFormatFlags.NoPadding).Width + S(16));
                if (cx > 0 && cx + w > inner)
                {
                    rows.Add([]);
                    cx = 0;
                }
                rows[^1].Add((feature, w));
                cx += w + S(6);
            }
            return rows;
        }

        private int ChipsHeight(int inner)
        {
            int rows = Features.Length == 0 ? 0 : ChipRows(inner).Count;
            return rows == 0 ? 0 : rows * ChipHeight + (rows - 1) * S(6);
        }

        /// <summary>Whether the action and its arrow fit on one line.</summary>
        private bool ActionOnOneLine(int inner)
            => TextRenderer.MeasureText(Action, ActionFont, Size.Empty, TextFormatFlags.NoPadding).Width + S(8) + S(16) + S(4) <= inner;

        private int ActionHeight(int inner)
            => ActionOnOneLine(inner) ? S(18) : TextRenderer.MeasureText(Action, ActionFont, new Size(Math.Max(1, inner), 0), Wrap).Height;

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool lit = (_hover || Focused) && Available;
            int lift = lit && !_pressed ? S(2) : 0;
            var card = new RectangleF(S(4), S(4) - lift, Width - S(8) - 1, Height - S(10) - 1);

            // A soft shadow, stronger when lifted: a few offset translucent fills rather than a
            // blur, which WinForms has no cheap way to do.
            int layers = lit ? 6 : 3;
            for (int i = layers; i >= 1; i--)
            {
                var shadow = card;
                shadow.Offset(0, i * (lit ? 1.2f : 0.8f));
                shadow.Inflate(i * 0.6f, i * 0.4f);
                using var sp = Rounded(shadow, S(12) + i);
                using var sb = new SolidBrush(Color.FromArgb(lit ? 7 : 5, 20, 40, 80));
                g.FillPath(sb, sp);
            }

            using var path = Rounded(card, S(12));
            using (var fill = new SolidBrush(Theme.Surface)) g.FillPath(fill, path);
            using (var pen = new Pen(lit ? Theme.Accent : Theme.Border, lit ? 1.5f : 1f)) g.DrawPath(pen, path);

            int pad = S(22);
            int left = (int)card.X + pad;
            int top = (int)card.Y + pad;
            int right = (int)card.Right - pad;

            // Icon disc.
            int disc = S(44);
            var discRect = new Rectangle(left, top, disc, disc);
            using (var db = new SolidBrush(Available ? Theme.AccentSoft : Theme.SurfaceHigh)) g.FillEllipse(db, discRect);
            TextRenderer.DrawText(g, Glyph, GlyphFont, discRect, Available ? Theme.Accent : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Title, and the badge beside it — or, on a card too narrow for both, under it, the pair
            // centred on the disc together.
            const TextFormatFlags single = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            int titleX = discRect.Right + S(14);
            var titleSize = TextRenderer.MeasureText(Text, CardTitle, Size.Empty, TextFormatFlags.NoPadding);
            var badgeSize = Badge is null ? Size.Empty : TextRenderer.MeasureText(Badge, BadgeFont, Size.Empty, TextFormatFlags.NoPadding);
            var pillSize = new Size(badgeSize.Width + S(12), badgeSize.Height + S(4));
            bool badgeBeside = Badge is null || titleX + titleSize.Width + S(10) + pillSize.Width <= right;
            int block = badgeBeside ? titleSize.Height : titleSize.Height + S(4) + pillSize.Height;
            var titleRect = new Rectangle(titleX, top + (disc - block) / 2 - S(1), titleSize.Width, titleSize.Height);
            TextRenderer.DrawText(g, Text, CardTitle, titleRect, Theme.Text, single);

            if (Badge is not null)
            {
                var pill = badgeBeside
                    ? new Rectangle(new Point(titleRect.Right + S(10), titleRect.Top + (titleRect.Height - pillSize.Height) / 2), pillSize)
                    : new Rectangle(new Point(titleX, titleRect.Bottom + S(4)), pillSize);
                using var pp = Rounded(pill, pill.Height / 2f);
                using var pb = new SolidBrush(Theme.Notice);
                g.FillPath(pb, pp);
                TextRenderer.DrawText(g, Badge, BadgeFont, pill, Theme.NoticeText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            // Tagline, wrapped, in a block as tall as the longest of the three.
            int y = top + disc + S(14);
            int tagBlock = Math.Max(S(44), TaglineBlock);
            TextRenderer.DrawText(g, Tagline, Body, new Rectangle(left, y, right - left, tagBlock), Theme.TextDim, Wrap);
            y += tagBlock + S(4);

            // Feature chips, onto another row when a narrow card has no room left on this one.
            foreach (var row in ChipRows(right - left))
            {
                int cx = left;
                foreach (var (feature, width) in row)
                {
                    var chip = new Rectangle(cx, y, width, ChipHeight);
                    using var cp = Rounded(chip, S(6));
                    using var cb = new SolidBrush(Theme.Background);
                    g.FillPath(cb, cp);
                    using var cpen = new Pen(Theme.Rule);
                    g.DrawPath(cpen, cp);
                    TextRenderer.DrawText(g, feature, ChipFont, chip, Theme.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    cx = chip.Right + S(6);
                }
                y += ChipHeight + S(6);
            }

            // The action line along the foot of the card, with its arrow when both fit on one
            // line; on a narrow card the words wrap and the arrow is left off.
            int inner = right - left;
            bool oneLine = ActionOnOneLine(inner);
            int actionH = ActionHeight(inner);
            int actionY = (int)card.Bottom - pad - actionH;
            var aSize = TextRenderer.MeasureText(Action, ActionFont, Size.Empty, TextFormatFlags.NoPadding);
            var actionRect = oneLine ? new Rectangle(left, actionY, aSize.Width, S(18)) : new Rectangle(left, actionY, inner, actionH);
            TextRenderer.DrawText(g, Action, ActionFont, actionRect, Available ? Theme.Accent : Theme.TextDim, oneLine ? single : Wrap);
            if (Available && oneLine)
            {
                int shift = lit ? S(4) : 0;
                TextRenderer.DrawText(g, "", ArrowFont,
                    new Rectangle(actionRect.Right + S(8) + shift, actionY, S(16), S(18)), Theme.Accent, single);
            }

            if (Focused && ShowFocusCues)
            {
                var ring = card;
                ring.Inflate(S(3), S(3));
                using var rp = Rounded(ring, S(14));
                using var rpen = new Pen(Color.FromArgb(120, Theme.Accent), 2f) { DashStyle = DashStyle.Dot };
                g.DrawPath(rpen, rp);
            }
        }
    }

    // ------------------------------------------------------------------ links

    /// <summary>
    /// A quiet text command: an optional glyph, accent text, underlined on hover. A self-painted
    /// <see cref="Button"/> for the same reasons as <see cref="ModeCard"/>.
    /// </summary>
    private sealed class LinkButton : Button
    {
        private static readonly Font LinkFont = new("Segoe UI", 9.5f);
        private static readonly Font LinkGlyph = new(GlyphFamily, 10f);

        private bool _hover;

        public LinkButton()
        {
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            BackColor = Theme.Background;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Height = 26;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? Glyph { get; init; }

        private int S(int logical) => logical * DeviceDpi / 96;

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleName = Text; Measure(); Invalidate(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Measure(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Measure(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        private int GlyphWidth => Glyph is null ? 0 : S(22);

        private void Measure()
        {
            var size = TextRenderer.MeasureText(Text ?? "", LinkFont, Size.Empty, TextFormatFlags.NoPadding);
            Size = new Size(GlyphWidth + size.Width + S(4), Math.Max(S(26), size.Height + S(8)));
            Parent?.PerformLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Background);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            var color = _hover ? ControlPaint.Dark(Theme.Accent, 0.1f) : Theme.Accent;

            if (Glyph is not null)
                TextRenderer.DrawText(g, Glyph, LinkGlyph, new Rectangle(0, 0, GlyphWidth, Height), color, flags);

            var font = _hover ? new Font(LinkFont, FontStyle.Underline) : LinkFont;
            TextRenderer.DrawText(g, Text, font, new Rectangle(GlyphWidth, 0, Width - GlyphWidth, Height), color, flags);
            if (!ReferenceEquals(font, LinkFont)) font.Dispose();

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width, Height));
        }
    }

    /// <summary>
    /// The footer's text, centred on the row. Drawn here because a Label with AutoEllipsis lays its
    /// text out a few pixels high of its own middle, which put the words visibly above the tick and
    /// the link on either side of them.
    ///
    /// It is one line when that fits. When it does not — the game's folder and the mod folder are
    /// both long paths — each of <see cref="Parts"/> takes a line of its own, and a path still too
    /// long for its line is shortened in the middle, the way Explorer does, rather than losing its
    /// end. The page's tooltip has both in full.
    /// </summary>
    private sealed class FooterText : Control
    {
        private const TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        public FooterText()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = FooterFont;
            ForeColor = Theme.TextDim;
        }

        /// <summary>The lines the text breaks into when it does not fit on one.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string[] Parts { get; set; } = [];

        private int S(int logical) => logical * DeviceDpi / 96;

        private int LineHeight => TextRenderer.MeasureText("Ag", Font, Size.Empty, TextFormatFlags.NoPadding).Height;

        private bool OneLine(int width) => Parts.Length <= 1 || TextRenderer.MeasureText(Text, Font, Size.Empty, Line).Width <= width;

        /// <summary>The row's height at <paramref name="width"/>: as designed for one line, a line more for each more.</summary>
        public int HeightFor(int width) => S(28) + (OneLine(width) ? 0 : (Parts.Length - 1) * (LineHeight + S(2)));

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (OneLine(Width))
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                    Line | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }

            int lineH = LineHeight + S(2);
            int y = (Height - Parts.Length * lineH + S(2)) / 2;
            foreach (string part in Parts)
            {
                TextRenderer.DrawText(e.Graphics, part, Font, new Rectangle(0, y, Width, LineHeight), ForeColor,
                    Line | TextFormatFlags.PathEllipsis);
                y += lineH;
            }
        }
    }

    /// <summary>
    /// "Remember my choice": a small switch and its label, deliberately quieter than the links
    /// beside it — it is a preference, not a destination. When on and a choice has been made, the
    /// label says which ("Opens in Quick") so the page explains its own absence next time.
    /// </summary>
    private sealed class RememberSwitch : Button
    {
        private static readonly Font LabelFont = new("Segoe UI", 9f);
        private bool _on;
        private string? _remembered;
        private bool _hover;

        public RememberSwitch()
        {
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            UseMnemonic = false;
            BackColor = Theme.Background;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Text = "Remember my choice";
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool On
        {
            get => _on;
            set { _on = value; AccessibleDescription = value ? "On" : "Off"; Text = Label; FitWidth(); Refresh(); }
        }

        /// <summary>"Quick" or "Complex" once a choice is remembered; shown in the label.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? Remembered
        {
            get => _remembered;
            // The label is the button's text too, so a screen reader hears what the page shows.
            set { _remembered = value; Text = Label; FitWidth(); Invalidate(); }
        }

        private string Label => _on && _remembered is not null ? $"Remember my choice · opens in {_remembered}" : "Remember my choice";

        private int S(int logical) => logical * DeviceDpi / 96;

        public void FitWidth()
        {
            int text = TextRenderer.MeasureText(Label, LabelFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            Size = new Size(S(30) + S(8) + text + S(4), S(26));
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); FitWidth(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var track = new RectangleF(0.5f, (Height - S(16)) / 2f, S(30), S(16));
            var off = _hover ? Theme.TrackHover : Theme.Track;
            using (var path = Rounded(track, track.Height / 2))
            using (var fill = new SolidBrush(_on ? Theme.Accent : off))
                g.FillPath(fill, path);
            float knob = track.Height - S(4);
            float kx = _on ? track.Right - S(2) - knob : track.X + S(2);
            using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, kx, track.Y + S(2), knob, knob);

            var textRect = new Rectangle((int)track.Right + S(8), 0, Width - (int)track.Right - S(8), Height);
            TextRenderer.DrawText(g, Label, LabelFont, textRect, _on || _hover ? Theme.Text : Theme.TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width, Height));
        }
    }

    /// <summary>
    /// The dark-mode switch: a lone moon (or sun, once dark is chosen) in the page's corner, faint
    /// until hovered. A preference nobody comes to this page for, so it stays out of the column.
    /// A small accent dot beside it marks a choice still waiting for the next launch.
    /// </summary>
    private sealed class ThemeGlyph : Button
    {
        private static readonly Font GlyphFont = new(GlyphFamily, 11f);
        private bool _saved = Theme.Dark;
        private bool _hover;

        public ThemeGlyph()
        {
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            BackColor = Theme.Background;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            AccessibleName = "Dark mode";
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Saved
        {
            get => _saved;
            set { _saved = value; AccessibleDescription = value ? "On" : "Off"; Invalidate(); }
        }

        private int S(int logical) => logical * DeviceDpi / 96;

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Background);

            // The mode a click leads to: a moon while light is chosen, a sun while dark is.
            string glyph = _saved ? "" : "";
            TextRenderer.DrawText(g, glyph, GlyphFont, ClientRectangle, _hover ? Theme.TextDim : Theme.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (_saved != Theme.Dark)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int d = S(5);
                using var dot = new SolidBrush(Theme.Accent);
                g.FillEllipse(dot, Width - d - S(2), S(2), d, d);
            }

            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width, Height));
        }
    }

    /// <summary>A tick in a green disc, or a warning in a red one.</summary>
    private sealed class StatusGlyph : Control
    {
        private static readonly Font Mark = new(GlyphFamily, 7.5f);
        private static readonly Font Bang = new("Segoe UI", 8f, FontStyle.Bold);
        private static readonly Color Good = Color.FromArgb(46, 140, 87);
        private bool _ok = true;

        public StatusGlyph()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Ok { get => _ok; set { _ok = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_ok ? Good : Theme.Danger);
            g.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(g, _ok ? "" : "!", _ok ? Mark : Bang, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
