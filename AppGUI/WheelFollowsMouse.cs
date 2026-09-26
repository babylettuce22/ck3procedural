using System.Runtime.InteropServices;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The mouse wheel goes to what the mouse is over.
///
/// WinForms hands WM_MOUSEWHEEL to the focused control. An inspector is a palette the user reads
/// while working the map — it is seldom the focused window — so the wheel over it went to the
/// main window instead, where the map's own filter (<see cref="ImageView"/>) zoomed the map
/// underneath: its test was only "is the point inside the map's rectangle", and an inspector
/// floating over the map is inside it.
///
/// Two halves. <see cref="IsOver"/> is the test the map now uses: the window actually under the
/// cursor, not the rectangle. And the filter installed here sends a wheel that arrived anywhere
/// to the inspector window under the cursor, so an inspector scrolls whether or not it has focus
/// and whether or not Windows' "scroll inactive windows" setting is on.
/// </summary>
internal static class WheelFollowsMouse
{
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;

    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Application.AddMessageFilter(new Filter());
    }

    /// <summary>The screen point carried by a wheel message's lParam.</summary>
    public static Point ScreenPoint(IntPtr lParam)
    {
        long packed = lParam.ToInt64();
        return new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
    }

    /// <summary>
    /// Whether <paramref name="control"/> — or something inside it — is the window under the given
    /// screen point. False when another window covers it there, which a rectangle test cannot see.
    /// </summary>
    public static bool IsOver(Control control, Point screen)
    {
        IntPtr hit = WindowFromPoint(screen);
        return hit == control.Handle || IsChild(control.Handle, hit);
    }

    private sealed class Filter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg is not (WmMouseWheel or WmMouseHWheel)) return false;

            IntPtr hit = WindowFromPoint(ScreenPoint(m.LParam));
            if (hit == IntPtr.Zero || hit == m.HWnd) return false;

            // Only into an inspector: everywhere else keeps WinForms' own routing.
            if (Control.FromChildHandle(hit)?.FindForm() is not InspectorForm) return false;

            // Sent, not posted: a sent message skips the message loop, so this filter never sees
            // its own forward. A control that does not scroll passes it on to its parent itself.
            SendMessage(hit, m.Msg, m.WParam, m.LParam);
            return true;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
