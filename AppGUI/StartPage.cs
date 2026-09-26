using System.Drawing.Drawing2D;
using System.Globalization;
using NoiseTool.Core;
using NoiseTool.Pipeline;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// What the window shows when it opens: a choice between the Quick generator and the Complex one,
/// with the few things worth doing before either (opening a world already written, the guide, and
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
    /// <summary>The Quick card. It has nowhere to go yet; the host decides what a click means.</summary>
    public event Action? QuickPicked;
    public event Action? ComplexPicked;
    public event Action? OpenWorldPicked;
    public event Action? GuidePicked;
    public event Action? GameFolderPicked;

    private static readonly Font TitleFont = new("Segoe UI Semibold", 20f);
    private static readonly Font SubtitleFont = new("Segoe UI", 10.5f);
    private static readonly Font FooterFont = new("Segoe UI", 9f);

    private readonly MapBanner _banner = new();
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly ModeCard _quick;
    private readonly ModeCard _complex;
    private readonly Label _note;
    private readonly LinkButton _openWorld;
    private readonly LinkButton _guide;
    private readonly Panel _rule = new() { BackColor = Theme.Border };
    private readonly StatusGlyph _gameGlyph = new();
    private readonly FooterText _gameText = new();
    private readonly LinkButton _gameChange;
    private readonly System.Windows.Forms.Timer _noteTimer = new() { Interval = 4500 };

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

        _subtitle = new Label
        {
            Text = "Generate a new Crusader Kings III map — terrain, realms, cultures and faiths — as a playable mod.",
            Font = SubtitleFont,
            ForeColor = Theme.TextDim,
            AutoSize = false,
            AutoEllipsis = true,
            BackColor = Color.Transparent,
        };

        _quick = new ModeCard
        {
            Name = "startQuick",
            Glyph = "",
            Title = "Quick",
            Badge = "Coming soon",
            Tagline = "Pick a map type and a few basics — size, era, climate — and let good defaults do the rest.",
            Features = ["Map types", "Size", "Era", "Climate"],
            Action = "Not available yet",
            Available = false,
        };

        _complex = new ModeCard
        {
            Name = "startComplex",
            Glyph = "",
            Title = "Complex",
            Tagline = "The full generator: shape terrain in the Forge, paint the climate, tune every setting and run history.",
            Features = ["Terrain forge", "Climate paint", "Every setting", "History"],
            Action = "Open the generator",
            Available = true,
        };

        _note = new Label
        {
            Text = "Quick mode is on its way. For now, Complex has everything.",
            Font = FooterFont,
            ForeColor = Theme.NoticeText,
            BackColor = Theme.Notice,
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 4),
            Visible = false,
        };

        _openWorld = new LinkButton { Name = "startOpenWorld", Glyph = "", Text = "Open a generated world…" };
        _guide = new LinkButton { Name = "startGuide", Glyph = "", Text = "Getting started" };

        _gameChange = new LinkButton { Name = "startGameFolder", Text = "Change…" };

        _quick.Click += (_, _) => { ShowNote(); QuickPicked?.Invoke(); };
        _complex.Click += (_, _) => ComplexPicked?.Invoke();
        _openWorld.Click += (_, _) => OpenWorldPicked?.Invoke();
        _guide.Click += (_, _) => GuidePicked?.Invoke();
        _gameChange.Click += (_, _) => GameFolderPicked?.Invoke();
        _noteTimer.Tick += (_, _) => { _noteTimer.Stop(); _note.Visible = false; };

        Controls.AddRange([_banner, _title, _subtitle, _quick, _complex, _note, _openWorld, _guide,
                           _rule, _gameGlyph, _gameText, _gameChange]);
    }

    private int S(int logical) => logical * DeviceDpi / 96;

    /// <summary>Brings the footer up to date and draws a fresh banner. Called each time the page comes on screen.</summary>
    public void Present(bool gameFound, string gameDir, string modRoot)
    {
        SetGameFolder(gameFound, gameDir, modRoot);
        _banner.Draw();
    }

    /// <summary>
    /// The one prerequisite either generator has, stated up front: a mod can be previewed without
    /// the game, but not written, and finding that out after a five-minute run is the wrong time.
    /// </summary>
    public void SetGameFolder(bool found, string gameDir, string modRoot)
    {
        _gameGlyph.Ok = found;
        _gameText.ForeColor = found ? Theme.TextDim : Theme.Danger;
        _gameText.Text = found
            ? $"Crusader Kings III found at {gameDir}     ·     Mods are written to {modRoot}"
            : "Crusader Kings III was not found. Set the game folder before writing a mod.";
        _gameChange.Text = found ? "Change…" : "Set game folder…";
        PerformLayout();
    }

    private void ShowNote()
    {
        _note.Visible = true;
        _noteTimer.Stop();
        _noteTimer.Start();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _note.Visible = false;
    }

    /// <summary>
    /// One centred column, top to bottom: banner, title, subtitle, the two cards, the links and
    /// the footer. The banner is the part that gives: on a short window it shrinks first and then
    /// goes, so the cards, which are the page's purpose, never fall off the bottom.
    /// </summary>
    /// <summary>
    /// The widest the column gets, and the margin kept either side of it. The page is meant to be
    /// seen as a small launch window, so <see cref="PreferredPageSize"/> is this column plus these
    /// margins and nothing more.
    /// </summary>
    private int ColumnWidth => S(940);
    private int SideMargin => S(28);
    private int TopPad => S(26);
    private int BottomPad => S(20);
    private int BannerGap => S(26);
    private int FullBannerHeight(int width) => Math.Min(S(230), width * 30 / 100);

    /// <summary>Everything in the column below the banner, top of the title to foot of the footer.</summary>
    private int BodyHeight
        => _title.PreferredHeight + S(4) + TextRenderer.MeasureText("Ag", SubtitleFont).Height + S(24)
           + CardHeight + S(16) + LinkRowHeight + S(18) + S(1) + S(10) + FooterHeight;

    private int CardHeight => S(226);
    private int LinkRowHeight => S(30);
    private int FooterHeight => S(28);

    /// <summary>
    /// The size the page wants to be shown at: the full column, the full banner and the margins,
    /// with no slack. The host sizes the window to this while the page is up.
    /// </summary>
    public Size PreferredPageSize
        => new(ColumnWidth + 2 * SideMargin,
               TopPad + FullBannerHeight(ColumnWidth) + BannerGap + BodyHeight + BottomPad);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_title is null) return;

        int width = Math.Min(ClientSize.Width - 2 * SideMargin, ColumnWidth);
        if (width <= 0) return;
        int x = (ClientSize.Width - width) / 2;

        int titleH = _title.PreferredHeight;
        int subtitleH = TextRenderer.MeasureText("Ag", SubtitleFont).Height;
        int cardH = CardHeight;
        int linkH = LinkRowHeight;
        int footerH = FooterHeight;

        int rest = BodyHeight;
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
        _subtitle.Bounds = new Rectangle(x, y, width, subtitleH);
        y += subtitleH + S(24);

        int gap = S(20);
        int cardW = (width - gap) / 2;
        _quick.Bounds = new Rectangle(x, y, cardW, cardH);
        _complex.Bounds = new Rectangle(x + cardW + gap, y, width - cardW - gap, cardH);
        y += cardH + S(16);

        _openWorld.Location = new Point(x, y + (linkH - _openWorld.Height) / 2);
        _guide.Location = new Point(_openWorld.Right + S(24), _openWorld.Top);
        _note.Location = new Point(x + width - _note.Width, y + (linkH - _note.Height) / 2);
        y += linkH + S(18);

        _rule.Bounds = new Rectangle(x, y, width, S(1));
        y += S(1) + S(10);

        int glyph = S(18);
        _gameGlyph.Bounds = new Rectangle(x, y + (footerH - glyph) / 2, glyph, glyph);
        _gameChange.Location = new Point(x + width - _gameChange.Width, y + (footerH - _gameChange.Height) / 2);
        _gameText.Bounds = Rectangle.FromLTRB(_gameGlyph.Right + S(8), y, _gameChange.Left - S(12), y + footerH);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _noteTimer.Dispose();
        base.Dispose(disposing);
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
            new ToolTip { InitialDelay = 400 }.SetToolTip(this, "An example of what the generator's map types look like. Click for another.");
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
            using var back = new SolidBrush(Color.FromArgb(215, 255, 255, 255));
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

        private int S(int logical) => logical * DeviceDpi / 96;

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

            // Title, and the badge beside it.
            const TextFormatFlags single = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            int titleX = discRect.Right + S(14);
            var titleSize = TextRenderer.MeasureText(Text, CardTitle, Size.Empty, TextFormatFlags.NoPadding);
            var titleRect = new Rectangle(titleX, top + (disc - titleSize.Height) / 2 - S(1), titleSize.Width, titleSize.Height);
            TextRenderer.DrawText(g, Text, CardTitle, titleRect, Theme.Text, single);

            if (Badge is not null)
            {
                var bs = TextRenderer.MeasureText(Badge, BadgeFont, Size.Empty, TextFormatFlags.NoPadding);
                var pill = new Rectangle(titleRect.Right + S(10), titleRect.Top + (titleRect.Height - bs.Height - S(4)) / 2,
                                         bs.Width + S(12), bs.Height + S(4));
                using var pp = Rounded(pill, pill.Height / 2f);
                using var pb = new SolidBrush(Theme.Notice);
                g.FillPath(pb, pp);
                TextRenderer.DrawText(g, Badge, BadgeFont, pill, Theme.NoticeText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            // Tagline, wrapped.
            int y = top + disc + S(14);
            var tagRect = new Rectangle(left, y, right - left, S(44));
            TextRenderer.DrawText(g, Tagline, Body, tagRect, Theme.TextDim,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            y += S(48);

            // Feature chips.
            int cx = left;
            foreach (string feature in Features)
            {
                var fs = TextRenderer.MeasureText(feature, ChipFont, Size.Empty, TextFormatFlags.NoPadding);
                var chip = new Rectangle(cx, y, fs.Width + S(16), fs.Height + S(8));
                if (chip.Right > right) break;
                using var cp = Rounded(chip, S(6));
                using var cb = new SolidBrush(Theme.Background);
                g.FillPath(cb, cp);
                using var cpen = new Pen(Color.FromArgb(225, 230, 238));
                g.DrawPath(cpen, cp);
                TextRenderer.DrawText(g, feature, ChipFont, chip, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                cx = chip.Right + S(6);
            }

            // The action line along the foot of the card.
            int actionY = (int)card.Bottom - pad - S(18);
            var aSize = TextRenderer.MeasureText(Action, ActionFont, Size.Empty, TextFormatFlags.NoPadding);
            var actionRect = new Rectangle(left, actionY, aSize.Width, S(18));
            TextRenderer.DrawText(g, Action, ActionFont, actionRect, Available ? Theme.Accent : Theme.TextDim, single);
            if (Available)
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
    /// One line of footer text, centred on the row and cut with an ellipsis. Drawn here because a
    /// Label with AutoEllipsis lays its text out a few pixels high of its own middle, which put
    /// the words visibly above the tick and the link on either side of them.
    /// </summary>
    private sealed class FooterText : Control
    {
        public FooterText()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = FooterFont;
            ForeColor = Theme.TextDim;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
            => TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
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
