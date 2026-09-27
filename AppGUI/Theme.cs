using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// A light and a dark palette, and the handful of places WinForms needs to be told about them by hand.
///
/// WinForms has no theming of its own: a control either honours <c>BackColor</c> or it paints
/// itself from system colours and ignores you. The three that ignore you are the ones handled here
/// — the title bar (a DWM attribute, not a control property), <see cref="PropertyGrid"/> (a dozen
/// separate colour properties, none of which are BackColor), and anything drawn by a
/// <see cref="ToolStripRenderer"/> (a colour *table*, not properties at all). Everything else in
/// the window is a Panel, a Button or a TextBox, which take BackColor and need nothing from here.
///
/// The palette is chosen once, when this class is first touched, and never changes afterwards:
/// every control copies its colours when it is built, so switching live would mean walking every
/// control tree in every open window. The setting takes effect on the next launch instead.
/// </summary>
internal static class Theme
{
    /// <summary>
    /// Read straight from the state file rather than handed in, so no window can be built before
    /// the choice is made. Declared first: the colours below read it in their initialisers, which
    /// run in the order they are written.
    /// </summary>
    public static readonly bool Dark = GuiState.Load().DarkMode;

    public static readonly Color Background = Pick(Color.FromArgb(245, 246, 248), Color.FromArgb(30, 31, 34));
    public static readonly Color Surface = Pick(Color.FromArgb(255, 255, 255), Color.FromArgb(43, 45, 49));
    public static readonly Color SurfaceHigh = Pick(Color.FromArgb(235, 238, 242), Color.FromArgb(56, 59, 64));

    /// <summary>A surface under the mouse: a shade off <see cref="Surface"/>, short of <see cref="SurfaceHigh"/>.</summary>
    public static readonly Color SurfaceHover = Pick(Color.FromArgb(250, 251, 253), Color.FromArgb(49, 51, 56));
    public static readonly Color Border = Pick(Color.FromArgb(205, 212, 222), Color.FromArgb(70, 74, 81));

    /// <summary>An outline that has to show against <see cref="Background"/> with no fill behind it: an empty drop slot.</summary>
    public static readonly Color BorderStrong = Pick(Color.FromArgb(170, 182, 200), Color.FromArgb(100, 106, 116));

    /// <summary>A hairline between rows: grid lines, a list's dividers. Fainter than <see cref="Border"/>.</summary>
    public static readonly Color Rule = Pick(Color.FromArgb(225, 230, 238), Color.FromArgb(58, 61, 67));
    public static readonly Color Text = Pick(Color.FromArgb(44, 48, 56), Color.FromArgb(222, 225, 230));
    public static readonly Color TextDim = Pick(Color.FromArgb(115, 122, 132), Color.FromArgb(150, 156, 165));

    /// <summary>Dimmer than <see cref="TextDim"/>: disabled text, and marks that only punctuate.</summary>
    public static readonly Color TextFaint = Pick(Color.FromArgb(175, 181, 190), Color.FromArgb(102, 107, 116));
    public static readonly Color Accent = Pick(Color.FromArgb(30, 110, 210), Color.FromArgb(62, 136, 230));
    public static readonly Color AccentText = Color.FromArgb(255, 255, 255);

    /// <summary>
    /// A pale wash of <see cref="Accent"/>, for "selected" one level above a leaf: the map category
    /// whose modes are showing, the settings section being read. The solid accent stays with the
    /// one thing actually on screen, so a row of selectors reads top-down instead of all at once.
    /// </summary>
    public static readonly Color AccentSoft = Pick(Color.FromArgb(222, 234, 250), Color.FromArgb(38, 57, 84));

    /// <summary>The fill of a chosen card: fainter than <see cref="AccentSoft"/>, since the card's accent border already says it.</summary>
    public static readonly Color SelectedWash = Pick(Color.FromArgb(244, 248, 255), Color.FromArgb(37, 47, 62));

    /// <summary>A switch's track while it is off.</summary>
    public static readonly Color Track = Pick(Color.FromArgb(196, 202, 212), Color.FromArgb(82, 87, 95));

    /// <inheritdoc cref="Track"/>
    public static readonly Color TrackHover = Pick(Color.FromArgb(170, 177, 188), Color.FromArgb(104, 110, 119));
    public static readonly Color Danger = Pick(Color.FromArgb(200, 65, 55), Color.FromArgb(232, 98, 88));

    /// <summary>
    /// A soft amber wash for the "there is something unsaved" bar.
    ///
    /// Deliberately not <see cref="Accent"/>: that blue already means "selected" on the view strip
    /// and in the title tree, and a notice painted in it would read as another selected thing
    /// rather than as a state the window is in.
    /// </summary>
    public static readonly Color Notice = Pick(Color.FromArgb(255, 247, 219), Color.FromArgb(62, 52, 26));

    public static readonly Color NoticeBorder = Pick(Color.FromArgb(230, 203, 122), Color.FromArgb(122, 100, 42));
    public static readonly Color NoticeText = Pick(Color.FromArgb(94, 71, 16), Color.FromArgb(240, 214, 150));

    private static Color Pick(Color light, Color dark) => Dark ? dark : light;

    /// <summary>
    /// Tells WinForms which system colours to hand out, for the little it still paints itself:
    /// scrollbars, tooltips, the stock TextBox and CheckedListBox. Must run before the first window
    /// is created, so it is called at startup beside <c>ApplicationConfiguration.Initialize</c>.
    /// </summary>
    public static void ApplyColorMode()
    {
#pragma warning disable WFO5001 // Still marked experimental; the classic mode is the fallback if it ever goes.
        Application.SetColorMode(Dark ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
    }

    public static readonly Font Ui = new("Segoe UI", 9f);
    public static readonly Font UiBold = new("Segoe UI", 9f, FontStyle.Bold);
    /// <summary>Consolas rather than anything newer: it is on every Windows install, and a font
    /// family that is not silently falls back to a proportional face, which is worse than plain.</summary>
    public static readonly Font Mono = new("Consolas", 9f);

    /// <summary>
    /// Sets DWMWA_USE_IMMERSIVE_DARK_MODE to match the palette, so the window frame follows the
    /// app's choice rather than the host OS's.
    /// </summary>
    public static void ApplyTitleBar(Form form)
    {
        const int UseImmersiveDarkMode = 20;
        int on = Dark ? 1 : 0;
        try
        {
            DwmSetWindowAttribute(form.Handle, UseImmersiveDarkMode, ref on, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>A flat button that reads as part of the toolbar rather than as a Windows 95 relic.</summary>
    public static Button MakeButton(string text, int width, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            Font = Ui,
            BackColor = primary ? Accent : Surface,
            ForeColor = primary ? AccentText : Text,
            UseVisualStyleBackColor = false,
            Margin = new Padding(3, 3, 3, 3),
        };

        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary
            ? ControlPaint.Light(Accent, 0.15f)
            : SurfaceHigh;
        button.FlatAppearance.MouseDownBackColor = primary
            ? ControlPaint.Dark(Accent, 0.08f)
            : Border;

        // Disabled flat buttons keep their BackColor and only grey the text.
        button.EnabledChanged += (_, _) =>
        {
            button.ForeColor = button.Enabled ? (primary ? AccentText : Text) : TextDim;
            button.BackColor = button.Enabled ? (primary ? Accent : Surface) : SurfaceHigh;
        };

        return button;
    }

    /// <summary>
    /// The PropertyGrid, in the palette's surfaces and soft borders.
    /// </summary>
    public static void Apply(PropertyGrid grid)
    {
        grid.BackColor = Surface;
        grid.ViewBackColor = Surface;
        grid.ViewForeColor = Text;
        grid.ViewBorderColor = Border;
        grid.LineColor = Rule;
        grid.CategoryForeColor = Accent;
        grid.CategorySplitterColor = Border;
        grid.HelpBackColor = Background;
        grid.HelpForeColor = TextDim;
        grid.HelpBorderColor = Border;
        grid.CommandsBackColor = Surface;
        grid.CommandsForeColor = Text;
        grid.CommandsBorderColor = Border;
        grid.DisabledItemForeColor = TextDim;
    }

    /// <summary>
    /// Menus are drawn by a renderer rather than from control properties, so the palette needs a
    /// colour table rather than a BackColor.
    /// </summary>
    public static ContextMenuStrip MakeMenu()
        => new()
        {
            Renderer = new ToolStripProfessionalRenderer(new MenuColours()) { RoundedEdges = false },
            BackColor = Surface,
            ForeColor = Text,
            Font = Ui,
        };

    /// <summary>
    /// The window's menu bar. Same colour table as <see cref="MakeMenu"/>, because a
    /// <see cref="MenuStrip"/> is the same renderer with a second set of table entries for the row
    /// of top-level names — miss those and the bar keeps the Office-blue gradient while its
    /// dropdowns come out white.
    /// </summary>
    public static MenuStrip MakeMenuBar()
        => new()
        {
            Dock = DockStyle.Top,
            Renderer = new ToolStripProfessionalRenderer(new MenuColours()) { RoundedEdges = false },
            BackColor = Surface,
            ForeColor = Text,
            Font = Ui,
            Padding = new Padding(6, 2, 0, 2),
        };

    /// <summary>
    /// A segmented control: a grey track holding a row of options, the chosen one lifted out in
    /// white. A shape of its own, so it cannot be mistaken for the workspace row above it or the
    /// map-mode buttons below it. Returns the track; the host adds it and restyles on change with
    /// <see cref="StyleSegment"/>.
    /// </summary>
    public static FlowLayoutPanel MakeSegmented(IEnumerable<Button> options)
    {
        var track = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(2),
            Margin = new Padding(3, 4, 3, 3),
            BackColor = SurfaceHigh,
        };

        foreach (var option in options)
        {
            option.AutoSize = false;
            option.Height = 24;
            option.FlatStyle = FlatStyle.Flat;
            option.FlatAppearance.BorderSize = 0;
            option.UseVisualStyleBackColor = false;
            option.Margin = new Padding(0);
            option.Font = Ui;
            track.Controls.Add(option);
        }

        return track;
    }

    /// <summary>
    /// A segment option. It never takes focus: a flat button that has it draws a black box round
    /// itself, and a click on the view switch should not pull focus out of the grid being edited.
    /// It paints its own text, because a disabled Button draws its text in a system grey that
    /// ignores ForeColor and is barely lighter than the enabled options beside it.
    /// </summary>
    public sealed class SegmentButton : Button
    {
        private bool _hover;

        public SegmentButton() => SetStyle(ControlStyles.Selectable, false);
        protected override bool ShowFocusCues => false;

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var back = _hover && Enabled ? FlatAppearance.MouseOverBackColor : BackColor;
            e.Graphics.Clear(back.IsEmpty ? BackColor : back);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
                Enabled ? ForeColor : TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    public static void StyleSegment(Button option, bool on)
    {
        option.BackColor = on ? Surface : SurfaceHigh;
        option.ForeColor = !option.Enabled ? TextDim : on ? Accent : Text;
        option.Font = on ? UiBold : Ui;
        option.FlatAppearance.MouseOverBackColor = on ? Surface : Border;
    }

    private sealed class MenuColours : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuItemSelected => SurfaceHigh;
        public override Color MenuItemSelectedGradientBegin => SurfaceHigh;
        public override Color MenuItemSelectedGradientEnd => SurfaceHigh;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;

        // The menu bar itself, and the top-level name whose dropdown is currently open — separate
        // entries from the dropdown ones above, and left alone they paint the default gradient.
        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color ToolStripBorder => Border;
        public override Color MenuItemPressedGradientBegin => SurfaceHigh;
        public override Color MenuItemPressedGradientMiddle => SurfaceHigh;
        public override Color MenuItemPressedGradientEnd => SurfaceHigh;
        public override Color CheckBackground => SurfaceHigh;
        public override Color CheckSelectedBackground => Border;
    }
}