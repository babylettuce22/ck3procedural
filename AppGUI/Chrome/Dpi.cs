using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Pixel sizes written for a 96-DPI screen, turned into pixels for this one.
///
/// The app runs SystemAware (the .NET default): Windows hands it the real DPI and stops stretching
/// the window, so fonts — sized in points — come out 1.5x or 2x as tall on a scaled display, while
/// a literal <c>Height = 24</c> stays 24 pixels and clips the text inside it. Every pixel size the
/// GUI code writes by hand goes through here. SystemAware means one DPI for the whole session (the
/// primary screen's at launch), which is why this can be a static and needs no control to ask —
/// it is the same number <see cref="Control.DeviceDpi"/> reports for every control.
/// </summary>
internal static class Dpi
{
    public static readonly int System = Read();

    /// <summary>A 96-DPI length in this screen's pixels.</summary>
    public static int S(int logical) => (int)Math.Round(logical * System / 96.0);

    /// <inheritdoc cref="S(int)"/>
    public static float S(float logical) => logical * System / 96f;

    public static Size S(int width, int height) => new(S(width), S(height));

    public static Padding Pad(int all) => new(S(all));

    public static Padding Pad(int left, int top, int right, int bottom) => new(S(left), S(top), S(right), S(bottom));

    /// <summary>A 96-DPI size, scaled, then shrunk to fit <paramref name="area"/> if a scaled display has made it too big.</summary>
    public static Size Fit(int width, int height, Rectangle area)
        => new(Math.Min(S(width), area.Width), Math.Min(S(height), area.Height));

    private static int Read()
    {
        try
        {
            int dpi = (int)GetDpiForSystem();
            if (dpi > 0) return dpi;
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (DllNotFoundException)
        {
        }

        using var screen = Graphics.FromHwnd(IntPtr.Zero);
        return Math.Max(96, (int)Math.Round(screen.DpiX));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
