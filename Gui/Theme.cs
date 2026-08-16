using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ck3MapGen.Gui;

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
    public static readonly Color Danger = Color.FromArgb(200, 65, 55);

    public static readonly Font Ui = new("Segoe UI", 9f);
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
    }
}