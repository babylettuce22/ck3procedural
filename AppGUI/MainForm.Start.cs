using Ck3MapGen.Core;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The start page: what the window opens on, and how it hands over to the generator.
///
/// While it is up, the rest of the window is hidden rather than covered — the workspace bar, the
/// workspaces and the status bar — so nothing behind it can be tabbed into or take a shortcut,
/// and the caption row shows the window's title in place of the menus. Leaving it puts everything
/// back exactly as it was; the workspaces are laid out once, in OnLoad, before it is shown, so
/// their splitters are already where they belong (see <see cref="OnLoad"/>).
/// </summary>
public sealed partial class MainForm
{
    private readonly StartPage _start = new() { Visible = false };
    private Control? _workspaceHost;
    private Control? _statusBar;
    private bool _onStart;

    /// <summary>
    /// Where the generator window was, and whether it was maximised, before the start page shrank
    /// the window down to a launcher. Put back on the way out, and saved in its place if the
    /// window is closed from the start page, so the compact size never becomes the generator's.
    /// </summary>
    private Rectangle _generatorBounds;
    private bool _generatorMaximized;
    private Size _generatorMinimum;

    /// <summary>Smallest the launcher may be dragged to; below it the page drops its banner first.</summary>
    private static readonly Size StartMinimum = new(720, 520);

    /// <summary>
    /// Whether the window opens on the start page. The --edit-world launch turns it off: that
    /// window already knows what it is for, and adopts its world as soon as it is shown.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool OpensToStartPage { get; set; } = true;

    private StartPage BuildStartPage()
    {
        // Quick has no page of its own yet; the card says so itself when clicked.
        _start.ComplexPicked += EnterComplex;
        _start.OpenWorldPicked += () => OpenGeneratedWorldAsync().Forget("open generated world");
        _start.GuidePicked += ShowWelcomeGuide;
        _start.GameFolderPicked += PickGameFolder;
        return _start;
    }

    private void ShowStartPage()
    {
        if (_busy) return;

        SuspendLayout();
        _onStart = true;
        _workspaceBar.Visible = false;
        if (_workspaceHost is not null) _workspaceHost.Visible = false;
        if (_statusBar is not null) _statusBar.Visible = false;
        if (CaptionBar is { } caption) caption.MenuHidden = true;
        _start.Visible = true;
        ResumeLayout();

        ShrinkToLauncher();

        _start.Present(Core.GameLocator.IsGameDir(_options.GameDir), _options.GameDir, _modRoot);
        _start.Focus();
    }

    /// <summary>
    /// Pulls the window in around the page: exactly the page's preferred size plus the caption
    /// row, centred where the generator window was, so it stays on the same monitor.
    ///
    /// The frame is measured off the live window rather than asked of ClientSize. ChromeForm hands
    /// the caption strip to the client area, and the ClientSize setter would still add a stock
    /// caption's height on top.
    /// </summary>
    private void ShrinkToLauncher()
    {
        _generatorBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _generatorMaximized = WindowState == FormWindowState.Maximized;
        _generatorMinimum = MinimumSize;

        WindowState = FormWindowState.Normal;
        MinimumSize = StartMinimum;

        var frame = Size - ClientSize;
        var page = _start.PreferredPageSize;
        int captionH = CaptionBar?.Height ?? 0;
        var size = new Size(page.Width + frame.Width, page.Height + captionH + frame.Height);

        var centre = new Point(_generatorBounds.X + _generatorBounds.Width / 2, _generatorBounds.Y + _generatorBounds.Height / 2);
        var work = Screen.FromPoint(centre).WorkingArea;
        size = new Size(Math.Min(size.Width, work.Width), Math.Min(size.Height, work.Height));
        int x = Math.Clamp(centre.X - size.Width / 2, work.Left, work.Right - size.Width);
        int y = Math.Clamp(centre.Y - size.Height / 2, work.Top, work.Bottom - size.Height);

        Bounds = new Rectangle(new Point(x, y), size);
    }

    /// <summary>Gives the generator its window back: the size, place and maximised state it had.</summary>
    private void RestoreGeneratorWindow()
    {
        if (_generatorBounds.IsEmpty) return;

        WindowState = FormWindowState.Normal;
        MinimumSize = _generatorMinimum;
        Bounds = _generatorBounds;
        if (_generatorMaximized) WindowState = FormWindowState.Maximized;
    }

    /// <summary>
    /// The placement to remember when the window closes. On the start page that is the generator's,
    /// held aside, not the launcher's compact size.
    /// </summary>
    private (Rectangle Bounds, bool Maximized) PlacementToSave()
        => _onStart && !_generatorBounds.IsEmpty
            ? (_generatorBounds, _generatorMaximized)
            : (WindowState == FormWindowState.Normal ? Bounds : RestoreBounds, WindowState == FormWindowState.Maximized);

    /// <summary>Puts the generator back on screen. Does nothing when the start page is not up.</summary>
    private void LeaveStartPage()
    {
        if (!_onStart) return;

        SuspendLayout();
        _onStart = false;
        _start.Visible = false;
        if (_workspaceHost is not null) _workspaceHost.Visible = true;
        if (_statusBar is not null) _statusBar.Visible = true;
        _workspaceBar.Visible = true;
        if (CaptionBar is { } caption) caption.MenuHidden = false;
        ResumeLayout();

        RestoreGeneratorWindow();
    }

    /// <summary>
    /// The Complex card: the generator as it has always been, on whichever workspace was last in
    /// use. The walkthrough that used to greet the very first launch waits for this moment
    /// instead, since it describes this screen and not the start page.
    /// </summary>
    private void EnterComplex()
    {
        SelectWorkspace(_workspace);

        if (_state.WelcomeShown) return;
        _state.WelcomeShown = true;
        ShowWelcomeGuide();
    }

    private void UpdateStartPageFolders()
        => _start.SetGameFolder(Core.GameLocator.IsGameDir(_options.GameDir), _options.GameDir, _modRoot);
}
