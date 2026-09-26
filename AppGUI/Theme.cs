using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// One light palette, and the handful of places WinForms needs to be told about it by hand.
///
/// WinForms has no theming of its own: a control either honours <c>BackColor</c> or it paints
/// itself from system colours and ignores you. The three that ignore you are the ones handled here
/// — the title bar (a DWM attribute, not a control property), <see cref="PropertyGrid"/> (a dozen
/// separate colour properties, none of which are BackColor), and anything drawn by a
/// <see cref="ToolStripRenderer"/> (a colour *table*, not properties at all). Everything else in
/// the window is a Panel, a Button or a TextBox, which take BackColor and need nothing from here.
/// </summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(245, 246, 248);
    public static readonly Color Surface = Color.FromArgb(255, 255, 255);
    public static readonly Color SurfaceHigh = Color.FromArgb(235, 238, 242);
    public static readonly Color Border = Color.FromArgb(205, 212, 222);
    public static readonly Color Text = Color.FromArgb(44, 48, 56);
    public static readonly Color TextDim = Color.FromArgb(115, 122, 132);
    public static readonly Color Accent = Color.FromArgb(30, 110, 210);
    public static readonly Color AccentText = Color.FromArgb(255, 255, 255);

    /// <summary>
    /// A pale wash of <see cref="Accent"/>, for "selected" one level above a leaf: the map category
    /// whose modes are showing, the settings section being read. The solid accent stays with the
    /// one thing actually on screen, so a row of selectors reads top-down instead of all at once.
    /// </summary>
    public static readonly Color AccentSoft = Color.FromArgb(222, 234, 250);
    public static readonly Color Danger = Color.FromArgb(200, 65, 55);

    /// <summary>
    /// A soft amber wash for the "there is something unsaved" bar.
    ///
    /// Deliberately not <see cref="Accent"/>: that blue already means "selected" on the view strip
    /// and in the title tree, and a notice painted in it would read as another selected thing
    /// rather than as a state the window is in.
    /// </summary>
    public static readonly Color Notice = Color.FromArgb(255, 247, 219);

    public static readonly Color NoticeBorder = Color.FromArgb(230, 203, 122);
    public static readonly Color NoticeText = Color.FromArgb(94, 71, 16);

    public static readonly Font Ui = new("Segoe UI", 9f);
    public static readonly Font UiBold = new("Segoe UI", 9f, FontStyle.Bold);
    /// <summary>Consolas rather than anything newer: it is on every Windows install, and a font
    /// family that is not silently falls back to a proportional face, which is worse than plain.</summary>
    public static readonly Font Mono = new("Consolas", 9f);

    /// <summary>
    /// Explicitly requests a light title bar and window frame. This turns off DWMWA_USE_IMMERSIVE_DARK_MODE
    /// so the window frame remains light even if the host OS is configured for dark mode.
    /// </summary>
    public static void ApplyLightTitleBar(Form form)
    {
        const int UseImmersiveDarkMode = 20;
        int on = 0; // 0 explicitly forces light/standard mode
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
    /// The PropertyGrid, configured with lighter background tones and soft borders.
    /// </summary>
    public static void ApplyLight(PropertyGrid grid)
    {
        grid.BackColor = Surface;
        grid.ViewBackColor = Surface;
        grid.ViewForeColor = Text;
        grid.ViewBorderColor = Border;
        grid.LineColor = Color.FromArgb(225, 230, 238);
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
    /// Menus are drawn by a renderer rather than from control properties, so a light one needs a
    /// colour table rather than a BackColor.
    /// </summary>
    public static ContextMenuStrip MakeMenu()
        => new()
        {
            Renderer = new ToolStripProfessionalRenderer(new LightColours()) { RoundedEdges = false },
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
            Renderer = new ToolStripProfessionalRenderer(new LightColours()) { RoundedEdges = false },
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
                Enabled ? ForeColor : Color.FromArgb(175, 181, 190),
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

    private sealed class LightColours : ProfessionalColorTable
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