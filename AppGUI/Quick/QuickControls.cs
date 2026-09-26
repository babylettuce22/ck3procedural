using System.ComponentModel;
using System.Drawing.Drawing2D;
using NoiseTool.Core;
using NoiseTool.Pipeline;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The visual vocabulary of the launcher pages (the start page and the Quick generator): rounded
/// cards, pills and switches on the app's light palette. Kept together so the pages look like one
/// thing.
///
/// Everything clickable here derives from <see cref="Button"/> and paints itself, the pattern
/// <see cref="Theme.SegmentButton"/> set. That gives it focus, Enter and Space for free, and makes it
/// a real push button to screen readers and UI Automation, with its Name as the automation id. A
/// plain Control shows up there as an anonymous pane whatever its role says.
/// </summary>
internal static class LaunchUi
{
    public const string GlyphFamily = "Segoe MDL2 Assets";

    public static readonly Font Title = new("Segoe UI Semibold", 16f);
    public static readonly Font Subtitle = new("Segoe UI", 10f);
    public static readonly Font GroupTitle = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.5f);

    /// <summary>A selected card's wash: a whisper of the accent, lighter than <see cref="Theme.AccentSoft"/>.</summary>
    public static readonly Color SelectedWash = Color.FromArgb(244, 248, 255);
    public static readonly Color Good = Color.FromArgb(46, 140, 87);

    public static int S(Control control, int logical) => logical * control.DeviceDpi / 96;

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        radius = Math.Max(0.5f, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f));
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>A plain label in the launcher's type, transparent over the page.</summary>
    public static Label MakeLabel(string text, Font font, Color color, bool wrap = false) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoSize = !wrap,
        UseMnemonic = false,
    };

    /// <summary>
    /// Base for the self-painted buttons: flat, owner-drawn, never the system look, and repainted on
    /// hover, press and focus.
    /// </summary>
    internal abstract class PaintedButton : Button
    {
        protected bool Hover { get; private set; }
        protected bool Pressed { get; private set; }

        protected PaintedButton()
        {
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            UseMnemonic = false;
            BackColor = Theme.Background;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected int S(int logical) => LaunchUi.S(this, logical);

        /// <summary>
        /// What the corners and margins are painted with. Defaults to the parent's colour, which is
        /// wrong for a button standing on a card the parent draws itself: set it to the card's.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? Backdrop { get; set; }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Backdrop ?? Parent?.BackColor ?? Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw(g);
        }

        protected abstract void Draw(Graphics g);

        /// <summary>A dotted accent ring, shown only when the keyboard put focus here.</summary>
        protected void DrawFocus(Graphics g, RectangleF around, float radius)
        {
            if (!Focused || !ShowFocusCues) return;
            around.Inflate(S(2), S(2));
            using var path = Rounded(around, radius + S(2));
            using var pen = new Pen(Color.FromArgb(140, Theme.Accent), 1.5f) { DashStyle = DashStyle.Dot };
            g.DrawPath(pen, path);
        }
    }

    public enum PillKind { Primary, Secondary, Quiet }

    /// <summary>A rounded button sized to its text, with an optional glyph ahead of it.</summary>
    internal sealed class PillButton : PaintedButton
    {
        private static readonly Font GlyphFont = new(GlyphFamily, 10f);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public PillKind Kind { get; set; } = PillKind.Secondary;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? Glyph { get; set; }

        /// <summary>Glyph after the text rather than before it — "Next →".</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool GlyphAfter { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int MinWidth { get; set; } = 88;

        private Font TextFont => Kind == PillKind.Primary ? Strong : Body;

        public PillButton() => Height = 36;

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); FitWidth(); }

        public void FitWidth()
        {
            int text = TextRenderer.MeasureText(Text ?? "", TextFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            int glyph = Glyph is null ? 0 : S(22);
            Size = new Size(Math.Max(S(MinWidth), text + glyph + S(36)), S(36));
        }

        protected override void Draw(Graphics g)
        {
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = S(8);
            using var path = Rounded(box, radius);

            Color fill, fore, border;
            switch (Kind)
            {
                case PillKind.Primary:
                    fill = !Enabled ? Theme.SurfaceHigh
                        : Pressed ? ControlPaint.Dark(Theme.Accent, 0.08f)
                        : Hover ? ControlPaint.Light(Theme.Accent, 0.12f) : Theme.Accent;
                    border = fill;
                    fore = Enabled ? Theme.AccentText : Theme.TextDim;
                    break;
                case PillKind.Quiet:
                    fill = !Enabled ? Color.Transparent : Pressed ? Theme.SurfaceHigh : Hover ? Theme.AccentSoft : Color.Transparent;
                    border = fill;
                    fore = Enabled ? Theme.Accent : Theme.TextDim;
                    break;
                default:
                    fill = !Enabled ? Theme.SurfaceHigh : Pressed ? Theme.SurfaceHigh : Hover ? Theme.Background : Theme.Surface;
                    border = Theme.Border;
                    fore = Enabled ? Theme.Text : Theme.TextDim;
                    break;
            }

            if (fill.A > 0) using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            if (border.A > 0 && border != fill) using (var pen = new Pen(border)) g.DrawPath(pen, path);

            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            int textWidth = TextRenderer.MeasureText(Text ?? "", TextFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            int glyphWidth = Glyph is null ? 0 : S(22);
            int x = (Width - textWidth - glyphWidth) / 2;

            if (Glyph is not null && !GlyphAfter)
            {
                TextRenderer.DrawText(g, Glyph, GlyphFont, new Rectangle(x, 0, S(16), Height), fore, flags);
                x += glyphWidth;
            }

            TextRenderer.DrawText(g, Text, TextFont, new Rectangle(x, 0, textWidth + S(2), Height), fore, flags);

            if (Glyph is not null && GlyphAfter)
                TextRenderer.DrawText(g, Glyph, GlyphFont, new Rectangle(x + textWidth + S(8), 0, S(16), Height), fore, flags);

            DrawFocus(g, box, radius);
        }
    }

    /// <summary>A quiet text command: an optional glyph, accent text, underlined on hover.</summary>
    internal sealed class TextLink : PaintedButton
    {
        private static readonly Font GlyphFont = new(GlyphFamily, 9.5f);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? Glyph { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Font LinkFont { get; set; } = Body;

        public TextLink() => Height = 26;

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); FitWidth(); }

        public void FitWidth()
        {
            var size = TextRenderer.MeasureText(Text ?? "", LinkFont, Size.Empty, TextFormatFlags.NoPadding);
            Size = new Size((Glyph is null ? 0 : S(20)) + size.Width + S(6), Math.Max(S(26), size.Height + S(8)));
        }

        protected override void Draw(Graphics g)
        {
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            var color = !Enabled ? Theme.TextDim : Hover ? ControlPaint.Dark(Theme.Accent, 0.1f) : Theme.Accent;
            int x = 0;
            if (Glyph is not null)
            {
                TextRenderer.DrawText(g, Glyph, GlyphFont, new Rectangle(0, 0, S(18), Height), color, flags);
                x = S(20);
            }

            using var font = Hover && Enabled ? new Font(LinkFont, FontStyle.Underline) : (Font)LinkFont.Clone();
            TextRenderer.DrawText(g, Text, font, new Rectangle(x, 0, Width - x, Height), color, flags);
            DrawFocus(g, new RectangleF(0, S(2), Width - 1, Height - S(4) - 1), S(4));
        }
    }

    /// <summary>
    /// One option in a choice: a title, a line under it, and a radio mark. The group it belongs to
    /// (<see cref="ChoiceGroup{T}"/>) decides which one is selected.
    /// </summary>
    internal sealed class ChoiceCard : PaintedButton
    {
        private static readonly Font Mark = new(GlyphFamily, 7f);
        private bool _selected;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Subtitle { get; set; } = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; AccessibleDescription = value ? "Selected" : null; Invalidate(); }
        }

        protected override void Draw(Graphics g)
        {
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = S(10);
            using var path = Rounded(box, radius);

            var fill = _selected ? SelectedWash : Hover && Enabled ? Color.FromArgb(250, 251, 253) : Theme.Surface;
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);

            var edge = _selected ? Theme.Accent : Hover && Enabled ? Color.FromArgb(150, Theme.Accent) : Theme.Border;
            using (var pen = new Pen(edge, _selected ? 2f : 1f)) g.DrawPath(pen, path);

            // The radio mark, top right.
            int dot = S(18);
            var mark = new Rectangle(Width - S(14) - dot, S(12), dot, dot);
            if (_selected)
            {
                using var accent = new SolidBrush(Theme.Accent);
                g.FillEllipse(accent, mark);
                TextRenderer.DrawText(g, "", Mark, mark, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                using var ring = new Pen(Theme.Border, 1.5f);
                g.DrawEllipse(ring, mark);
            }

            int left = S(14);
            var titleRect = new Rectangle(left, S(11), mark.Left - left - S(6), S(22));
            TextRenderer.DrawText(g, Text, GroupTitle, titleRect, Enabled ? Theme.Text : Theme.TextDim,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            var subRect = new Rectangle(left, S(34), Width - left - S(12), Height - S(38));
            TextRenderer.DrawText(g, Subtitle, Small, subRect, Theme.TextDim,
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

            DrawFocus(g, box, radius);
        }
    }

    /// <summary>What a page needs to lay a choice group out, whatever its value type.</summary>
    internal interface IChoiceGroup
    {
        string Title { get; }
        string Hint { get; }
        IEnumerable<ChoiceCard> Cards { get; }
    }

    /// <summary>
    /// A row of <see cref="ChoiceCard"/>s acting as one radio group over an enum-like value.
    /// Clicking a card selects it; <see cref="Changed"/> reports the new value.
    /// </summary>
    internal sealed class ChoiceGroup<T> : IChoiceGroup
    {
        private readonly List<(T Value, ChoiceCard Card)> _options = [];
        private T _value;

        public ChoiceGroup(string title, string hint, T initial)
        {
            Title = title;
            Hint = hint;
            _value = initial;
        }

        public string Title { get; }
        public string Hint { get; }
        public IEnumerable<ChoiceCard> Cards => _options.Select(o => o.Card);
        public event Action<T>? Changed;

        public ChoiceGroup<T> Add(T value, string title, string subtitle, string name)
        {
            var card = new ChoiceCard { Text = title, Subtitle = subtitle, Name = name, AccessibleName = title };
            card.Click += (_, _) => { Value = value; Changed?.Invoke(value); };
            _options.Add((value, card));
            card.Selected = EqualityComparer<T>.Default.Equals(value, _value);
            return this;
        }

        public T Value
        {
            get => _value;
            set
            {
                _value = value;
                foreach (var (v, card) in _options) card.Selected = EqualityComparer<T>.Default.Equals(v, value);
            }
        }

        public void SetEnabled(T value, bool enabled)
        {
            foreach (var (v, card) in _options)
                if (EqualityComparer<T>.Default.Equals(v, value)) card.Enabled = enabled;
        }
    }

    /// <summary>An on/off card: title, a line of description, and a switch.</summary>
    internal sealed class ToggleCard : PaintedButton
    {
        private bool _on;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Description { get; set; } = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool On
        {
            get => _on;
            set { if (_on == value) return; _on = value; AccessibleDescription = value ? "On" : "Off"; Invalidate(); }
        }

        public event Action<bool>? Toggled;

        protected override void OnClick(EventArgs e)
        {
            On = !On;
            base.OnClick(e);
            Toggled?.Invoke(On);
        }

        protected override void Draw(Graphics g)
        {
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = S(10);
            using var path = Rounded(box, radius);
            using (var brush = new SolidBrush(Hover ? Color.FromArgb(250, 251, 253) : Theme.Surface)) g.FillPath(brush, path);
            using (var pen = new Pen(Hover ? Color.FromArgb(150, Theme.Accent) : Theme.Border)) g.DrawPath(pen, path);

            // The switch, top right.
            var track = new RectangleF(Width - S(14) - S(36), S(13), S(36), S(20));
            using (var tp = Rounded(track, track.Height / 2))
            using (var tb = new SolidBrush(_on ? Theme.Accent : Color.FromArgb(196, 202, 212)))
                g.FillPath(tb, tp);
            float knob = track.Height - S(6);
            float kx = _on ? track.Right - S(3) - knob : track.X + S(3);
            using (var kb = new SolidBrush(Color.White)) g.FillEllipse(kb, kx, track.Y + S(3), knob, knob);

            int left = S(14);
            TextRenderer.DrawText(g, Text, GroupTitle, new Rectangle(left, S(11), (int)track.X - left - S(6), S(24)), Theme.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Description, Small, new Rectangle(left, S(36), Width - left - S(12), Height - S(40)), Theme.TextDim,
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

            DrawFocus(g, box, radius);
        }
    }

    /// <summary>A map type: its thumbnail on top, its name underneath, an accent frame when chosen.</summary>
    internal sealed class MapTile : PaintedButton
    {
        private static readonly Font Mark = new(GlyphFamily, 7f);
        private Bitmap? _image;
        private bool _selected;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Bitmap? Thumbnail
        {
            get => _image;
            set { _image?.Dispose(); _image = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; AccessibleDescription = value ? "Selected" : null; Invalidate(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _image?.Dispose();
            base.Dispose(disposing);
        }

        protected override void Draw(Graphics g)
        {
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = S(10);
            using var path = Rounded(box, radius);
            using (var brush = new SolidBrush(_selected ? SelectedWash : Theme.Surface)) g.FillPath(brush, path);

            int pad = S(6);
            var imageRect = new RectangleF(pad, pad, Width - 2 * pad, (Width - 2 * pad) / 2f);
            using (var clip = Rounded(imageRect, S(6)))
            {
                if (_image is not null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SetClip(clip);
                    g.DrawImage(_image, imageRect);
                    g.ResetClip();
                }
                else
                {
                    using var placeholder = new SolidBrush(Color.FromArgb(34, 70, 128));
                    g.FillPath(placeholder, clip);
                }
            }

            var titleRect = new Rectangle(pad, (int)imageRect.Bottom + S(4), Width - 2 * pad, Height - (int)imageRect.Bottom - S(8));
            TextRenderer.DrawText(g, Text, _selected ? Strong : Body, titleRect, _selected ? Theme.Accent : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var edge = _selected ? Theme.Accent : Hover ? Color.FromArgb(150, Theme.Accent) : Theme.Border;
            using (var pen = new Pen(edge, _selected ? 2f : 1f)) g.DrawPath(pen, path);

            if (_selected)
            {
                int dot = S(18);
                var mark = new Rectangle((int)imageRect.Right - dot - S(5), (int)imageRect.Y + S(5), dot, dot);
                using var accent = new SolidBrush(Theme.Accent);
                g.FillEllipse(accent, mark);
                TextRenderer.DrawText(g, "", Mark, mark, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            DrawFocus(g, box, radius);
        }
    }

    /// <summary>
    /// The row of numbered steps across the top of the Quick page. A step already reached can be
    /// clicked to go back to it; one not yet reached cannot be skipped to.
    /// </summary>
    internal sealed class Stepper : Control
    {
        private static readonly Font Number = new("Segoe UI Semibold", 9f);
        private static readonly Font Mark = new(GlyphFamily, 8f);
        private static readonly Font Label = new("Segoe UI", 9.5f);
        private static readonly Font LabelBold = new("Segoe UI Semibold", 9.5f);

        private readonly string[] _steps;
        private int _current;
        private int _reached;
        private bool _locked;
        private int _hover = -1;

        public Stepper(params string[] steps)
        {
            _steps = steps;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
        }

        public event Action<int>? StepClicked;

        private int S(int logical) => LaunchUi.S(this, logical);

        /// <summary>Which step is on screen, and the furthest one reached so far.</summary>
        public void Set(int current, int reached, bool locked = false)
        {
            _current = current;
            _reached = Math.Max(reached, current);
            _locked = locked;
            Invalidate();
        }

        /// <summary>Width wanted to lay every step out at its natural size.</summary>
        public int NaturalWidth
        {
            get
            {
                int width = 0;
                foreach (string step in _steps)
                    width += S(26) + S(8) + TextRenderer.MeasureText(step, LabelBold, Size.Empty, TextFormatFlags.NoPadding).Width;
                return width + (_steps.Length - 1) * S(44);
            }
        }

        private IEnumerable<(int Index, Rectangle Bounds)> Items()
        {
            int x = Math.Max(0, (Width - NaturalWidth) / 2);
            for (int i = 0; i < _steps.Length; i++)
            {
                int w = S(26) + S(8) + TextRenderer.MeasureText(_steps[i], LabelBold, Size.Empty, TextFormatFlags.NoPadding).Width;
                yield return (i, new Rectangle(x, 0, w, Height));
                x += w + S(44);
            }
        }

        private bool Clickable(int index) => !_locked && index != _current && index <= _reached;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = Items().Where(i => i.Bounds.Contains(e.Location) && Clickable(i.Index)).Select(i => i.Index).DefaultIfEmpty(-1).First();
            if (hover == _hover) return;
            _hover = hover;
            Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (var (index, bounds) in Items())
                if (bounds.Contains(e.Location) && Clickable(index)) { StepClicked?.Invoke(index); return; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var items = Items().ToList();
            int cy = Height / 2;

            // Connectors first, so the circles sit on top of them.
            for (int i = 0; i < items.Count - 1; i++)
            {
                int x0 = items[i].Bounds.Right + S(10), x1 = items[i + 1].Bounds.Left - S(10);
                using var pen = new Pen(i < _reached ? Theme.Accent : Theme.Border, 2f);
                g.DrawLine(pen, x0, cy, x1, cy);
            }

            foreach (var (i, bounds) in items)
            {
                bool done = i < _current || (_locked && i <= _current);
                bool current = i == _current && !_locked;
                bool reachable = i <= _reached;

                var circle = new Rectangle(bounds.X, cy - S(13), S(26), S(26));
                if (done || current)
                {
                    using var fill = new SolidBrush(i == _hover ? ControlPaint.Light(Theme.Accent, 0.15f) : Theme.Accent);
                    g.FillEllipse(fill, circle);
                }
                else
                {
                    using var fill = new SolidBrush(Theme.Surface);
                    g.FillEllipse(fill, circle);
                    using var ring = new Pen(reachable ? Theme.Accent : Theme.Border, 1.5f);
                    g.DrawEllipse(ring, circle);
                }

                var glyphColor = done || current ? Color.White : reachable ? Theme.Accent : Theme.TextDim;
                TextRenderer.DrawText(g, done ? "" : (i + 1).ToString(), done ? Mark : Number, circle, glyphColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                var labelRect = new Rectangle(circle.Right + S(8), 0, bounds.Right - circle.Right - S(8) + S(4), Height);
                using var font = i == _hover ? new Font(current ? LabelBold : Label, FontStyle.Underline) : (Font)(current ? LabelBold : Label).Clone();
                TextRenderer.DrawText(g, _steps[i], font, labelRect,
                    current ? Theme.Text : reachable ? Theme.Accent : Theme.TextDim,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>
    /// A map picture in a rounded frame, with a label chip in the bottom-left corner and an optional
    /// busy chip in the top-right. Shows the whole picture, letterboxed if the aspect differs.
    /// </summary>
    internal sealed class MapPreview : Control
    {
        private static readonly Font ChipFont = new("Segoe UI Semibold", 9f);
        private Bitmap? _image;
        private string? _chip;
        private string? _busy;

        public MapPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
        }

        private int S(int logical) => LaunchUi.S(this, logical);

        /// <summary>The picture. The control takes ownership and disposes the one it replaces.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Bitmap? Image
        {
            get => _image;
            set { if (ReferenceEquals(_image, value)) return; var old = _image; _image = value; Invalidate(); old?.Dispose(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? Chip { get => _chip; set { _chip = value; Invalidate(); } }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? Busy { get => _busy; set { _busy = value; Invalidate(); } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _image?.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var frame = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using var path = Rounded(frame, S(10));

            using (var sea = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                       Color.FromArgb(22, 52, 104), Color.FromArgb(34, 84, 150), LinearGradientMode.Vertical))
                g.FillPath(sea, path);

            if (_image is { } image)
            {
                float scale = Math.Min((float)Width / image.Width, (float)Height / image.Height);
                float w = image.Width * scale, h = image.Height * scale;
                g.SetClip(path);
                g.DrawImage(image, new RectangleF((Width - w) / 2, (Height - h) / 2, w, h));
                g.ResetClip();
            }

            using (var border = new Pen(Color.FromArgb(40, 0, 0, 0))) g.DrawPath(border, path);

            if (!string.IsNullOrEmpty(_chip)) DrawChip(g, _chip, S(12), Height - S(12), false);
            if (!string.IsNullOrEmpty(_busy)) DrawChip(g, _busy, Width - S(12), S(12) + ChipHeight, true);
        }

        private int ChipHeight => TextRenderer.MeasureText("Ag", ChipFont, Size.Empty, TextFormatFlags.NoPadding).Height + S(10);

        private void DrawChip(Graphics g, string text, int x, int bottom, bool alignRight)
        {
            var size = TextRenderer.MeasureText(text, ChipFont, Size.Empty, TextFormatFlags.NoPadding);
            var chip = new Rectangle(0, bottom - size.Height - S(10), size.Width + S(20), size.Height + S(10));
            chip.X = alignRight ? x - chip.Width : x;
            using var path = Rounded(chip, chip.Height / 2f);
            using var back = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            g.FillPath(back, path);
            TextRenderer.DrawText(g, text, ChipFont, chip, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// One line of text shortened in the middle when it does not fit, the way Explorer shortens a
    /// path: the drive and the folder name stay readable, and the part cut is the part between.
    /// </summary>
    internal sealed class PathText : Control
    {
        public PathText()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
            ForeColor = Theme.TextDim;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
            => TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis
                | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// A thin rounded progress bar. With no fraction it runs an indeterminate sweep, which is what a
    /// first run shows before the estimate has anything to calibrate on.
    /// </summary>
    internal sealed class ProgressLine : Control
    {
        private readonly System.Windows.Forms.Timer _sweep = new() { Interval = 30 };
        private double? _fraction;
        private float _phase;

        public ProgressLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
            _sweep.Tick += (_, _) => { _phase = (_phase + 0.012f) % 1.4f; Invalidate(); };
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double? Fraction
        {
            get => _fraction;
            set
            {
                _fraction = value is { } f ? Math.Clamp(f, 0, 1) : null;
                if (_fraction is null && Visible) _sweep.Start(); else _sweep.Stop();
                Invalidate();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && _fraction is null) _sweep.Start(); else _sweep.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _sweep.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var tp = Rounded(track, track.Height / 2))
            using (var tb = new SolidBrush(Theme.SurfaceHigh))
                g.FillPath(tb, tp);

            RectangleF bar;
            if (_fraction is { } f)
            {
                bar = new RectangleF(0, 0, Math.Max(Height, (Width - 1) * (float)f), Height - 1);
            }
            else
            {
                float w = Width * 0.3f;
                float x = (_phase - 0.3f) * Width;
                bar = RectangleF.Intersect(new RectangleF(x, 0, w, Height - 1), track);
                if (bar.Width <= 0) return;
            }

            using var bp = Rounded(bar, bar.Height / 2);
            using var bb = new SolidBrush(Theme.Accent);
            g.FillPath(bb, bp);
        }
    }
}

/// <summary>
/// Previews a shipped Forge preset at a seed, exactly as the Terrain workspace would preview it.
/// Erosion is a bake-only stage and is skipped, as in any preview. Returns null rather than
/// throwing: a picture that could not be drawn costs the page a picture and nothing more.
/// </summary>
internal static class ForgePreview
{
    public static Bitmap? Render(string presetPath, int seed, int width, int height, CancellationToken token)
    {
        try
        {
            var pipeline = new HeightPipeline();
            PresetIO.Load(pipeline, presetPath);
            pipeline.SeaLevel = Ck3.SeaLevelNormalised;
            pipeline.MasterSeed = seed;
            var result = pipeline.Run(width, height, isPreview: true, token);
            return HeightRenderer.Render(result.Field, pipeline.SeaLevel, RenderMode.Hypsometric);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
