using System.Runtime.InteropServices;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// A window frame without its caption strip; the <see cref="CaptionBar"/> row draws one in its place.
/// The main window and the inspectors both use it, so every window of the tool wears the same top.
///
/// The frame is otherwise untouched — same window styles, same resize borders, same DWM shadow and
/// rounded corners — so everything Windows does with a window still works. Only three messages are
/// intercepted: WM_NCCALCSIZE, to hand the caption's height to the client area; WM_NCHITTEST, to
/// say which parts of the new row drag, resize or are buttons; and the non-client mouse messages
/// for those buttons, which Windows would otherwise draw classic ones for.
///
/// Until a subclass sets <see cref="CaptionBar"/> the window is a plain Form with the stock caption.
/// </summary>
public class ChromeForm : Form
{
    private TitleBar? _caption;
    private bool _trackingCaptionLeave;

    /// <summary>
    /// The drawn caption row. The subclass creates it and adds it to its controls, since only the
    /// subclass knows its docking order; setting it here is what takes the stock caption away.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal TitleBar? CaptionBar
    {
        get => _caption;
        set
        {
            _caption = value;
            if (IsHandleCreated) RecalculateFrame();
        }
    }

    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmNcLButtonDblClk = 0x00A3;
    private const int WmNcRButtonDown = 0x00A4;
    private const int WmNcRButtonUp = 0x00A5;
    private const int WmNcMouseLeave = 0x02A2;

    private const int HtTop = 12, HtTopLeft = 13, HtTopRight = 14;

    /// <summary>
    /// Makes Windows ask WM_NCCALCSIZE again now the handle exists. Without it the first frame keeps
    /// the stock caption until something happens to resize the window.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RecalculateFrame();
    }

    private void RecalculateFrame()
        => SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);

    protected override void WndProc(ref Message m)
    {
        if (_caption is null)
        {
            base.WndProc(ref m);
            return;
        }

        switch (m.Msg)
        {
            case WmNcCalcSize when m.WParam != IntPtr.Zero:
                RemoveCaption(ref m);
                return;

            case WmNcHitTest:
                base.WndProc(ref m);
                if ((int)m.Result == TitleBar.HtClient) m.Result = CaptionHitTest(m.LParam);
                return;

            case WmNcMouseMove:
                _caption.SetHover(TitleBar.IsButton((int)m.WParam) ? (int)m.WParam : 0);
                TrackCaptionLeave();
                base.WndProc(ref m);
                return;

            case WmNcMouseLeave:
                _trackingCaptionLeave = false;
                _caption.SetHover(0);
                _caption.Pressed = 0;
                base.WndProc(ref m);
                return;

            // The buttons are ours: passing these on would have Windows track and paint the classic
            // caption buttons over the drawn ones.
            case WmNcLButtonDown when TitleBar.IsButton((int)m.WParam):
                _caption.Pressed = (int)m.WParam;
                m.Result = IntPtr.Zero;
                return;

            case WmNcLButtonUp when TitleBar.IsButton((int)m.WParam):
                int hit = (int)m.WParam;
                bool clicked = _caption.Pressed == hit;
                _caption.Pressed = 0;
                m.Result = IntPtr.Zero;
                if (clicked) CaptionButton(hit);
                return;

            case WmNcLButtonDblClk or WmNcRButtonDown or WmNcRButtonUp when TitleBar.IsButton((int)m.WParam):
                m.Result = IntPtr.Zero;
                return;
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// Lets Windows lay out the frame, then gives the caption strip back to the client area. The
    /// sides and bottom keep their borders, so resizing from them is still the system's.
    ///
    /// A maximised window hangs over the monitor edge by its border thickness on every side. The
    /// sides and bottom already allow for that; the top has to be told, or the row would start
    /// above the screen. The overhang is read off how far Windows moved the left edge in, which is
    /// the same border at whatever DPI the monitor has.
    /// </summary>
    private void RemoveCaption(ref Message m)
    {
        var proposed = Marshal.PtrToStructure<Rect>(m.LParam);
        base.WndProc(ref m);

        var client = Marshal.PtrToStructure<Rect>(m.LParam);
        client.Top = proposed.Top + (IsZoomed(Handle) ? client.Left - proposed.Left : 0);
        Marshal.StructureToPtr(client, m.LParam, false);
        m.Result = IntPtr.Zero;
    }

    /// <summary>
    /// What a point in the client area means to Windows. The top few pixels resize, since the top
    /// border went with the caption; the title row answers for itself; anything else is client.
    /// </summary>
    private IntPtr CaptionHitTest(IntPtr lParam)
    {
        long packed = lParam.ToInt64();
        var point = PointToClient(new Point(unchecked((short)packed), unchecked((short)(packed >> 16))));

        bool sizable = FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;
        if (sizable && WindowState == FormWindowState.Normal)
        {
            int band = LogicalToDeviceUnits(5);
            if (point.Y < band)
            {
                int corner = LogicalToDeviceUnits(12);
                return point.X < corner ? HtTopLeft
                    : point.X >= ClientSize.Width - corner ? HtTopRight
                    : HtTop;
            }
        }

        if (_caption is { } caption && caption.Bounds.Contains(point))
            return caption.HitTest(new Point(point.X - caption.Left, point.Y - caption.Top));

        return TitleBar.HtClient;
    }

    private void CaptionButton(int hit)
    {
        switch (hit)
        {
            case TitleBar.HtMinButton:
                WindowState = FormWindowState.Minimized;
                break;
            case TitleBar.HtMaxButton:
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                break;
            case TitleBar.HtClose:
                Close();
                break;
        }
    }

    /// <summary>
    /// Asks for WM_NCMOUSELEAVE, so a hovered button un-hovers when the mouse leaves the row for a
    /// child window or for somewhere else entirely. The request lapses once it fires.
    /// </summary>
    private void TrackCaptionLeave()
    {
        if (_trackingCaptionLeave) return;

        var track = new TrackMouseEventInfo
        {
            Size = Marshal.SizeOf<TrackMouseEventInfo>(),
            Flags = TmeLeave | TmeNonClient,
            Window = Handle,
        };
        _trackingCaptionLeave = TrackMouseEvent(ref track);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Window;
        public int HoverTime;
    }

    private const int TmeLeave = 0x02, TmeNonClient = 0x10;
    private const int SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpFrameChanged = 0x0020;

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TrackMouseEventInfo track);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);
}
