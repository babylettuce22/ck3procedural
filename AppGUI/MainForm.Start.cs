using Ck3MapGen.Core;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The launcher pages — the start page, and the Quick generator it leads to — and how they hand
/// over to the Complex generator.
///
/// While one is up, the rest of the window is hidden rather than covered — the workspace bar, the
/// workspaces and the status bar — so nothing behind it can be tabbed into or take a shortcut,
/// and the caption row shows the window's title in place of the menus. Leaving puts everything
/// back exactly as it was; the workspaces are laid out once, in OnLoad, before a launcher page is
/// shown, so their splitters are already where they belong (see <see cref="OnLoad"/>).
///
/// The two launcher pages share one compact window. Moving between them swaps the page in place;
/// only arriving from, or leaving for, the generator changes the window's size.
/// </summary>
public sealed partial class MainForm
{
    private readonly StartPage _start = new() { Visible = false };
    private Control? _workspaceHost;
    private Control? _statusBar;

    /// <summary>The launcher page on screen, or null when the generator is.</summary>
    private Control? _launcherPage;

    private bool _onStart => _launcherPage is not null && ReferenceEquals(_launcherPage, _start);
    private bool _onQuick => _launcherPage is not null && ReferenceEquals(_launcherPage, _quick);
    private bool _inLauncher => _launcherPage is not null;

    /// <summary>
    /// Where the generator window was, and whether it was maximised, before a launcher page shrank
    /// the window down. Put back on the way out, and saved in its place if the window is closed
    /// from a launcher page, so the compact size never becomes the generator's.
    /// </summary>
    private Rectangle _generatorBounds;
    private bool _generatorMaximized;
    private Size _generatorMinimum;

    /// <summary>Smallest the launcher may be dragged to; below it the pages drop what they can first.</summary>
    private static readonly Size StartMinimum = new(720, 520);

    /// <summary>
    /// Whether the window opens on the start page. The --edit-world launch turns it off: that
    /// window already knows what it is for, and adopts its world as soon as it is shown.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool OpensToStartPage { get; set; } = true;

    private StartPage BuildStartPage()
    {
        _start.QuickPicked += () => { RememberStart("Quick"); ShowQuickPage(); };
        _start.ComplexPicked += () => { RememberStart("Complex"); EnterComplex(); };
        _start.RememberChanged += on =>
        {
            // Saved now rather than on close: it is a choice about the next launch, and a crash
            // between here and closing should not undo it.
            _state.RememberStartChoice = on;
            if (!on) _state.StartWith = null;
            _state.Save();
        };
        _start.OpenWorldPicked += () => OpenGeneratedWorldAsync().Forget("open generated world");
        _start.GuidePicked += ShowWelcomeGuide;
        _start.GameFolderPicked += PickGameFolder;
        return _start;
    }

    private void ShowStartPage()
    {
        if (_busy) return;

        ShowLauncherPage(_start);
        _start.Present(Core.GameLocator.IsGameDir(_options.GameDir), _options.GameDir, _modRoot);
        _start.SetRemember(_state.RememberStartChoice, _state.StartWith);
        _start.Focus();
    }

    /// <summary>
    /// Where the window opens: the start page, or — when "Remember my choice" is on and a card
    /// was picked with it — straight into Quick, or straight into the generator as it was left.
    /// </summary>
    private void OpenOnLaunch()
    {
        if (_state.RememberStartChoice && _state.StartWith == "Complex") return;
        if (_state.RememberStartChoice && _state.StartWith == "Quick")
        {
            ShowQuickPage();
            return;
        }
        ShowStartPage();
    }

    /// <summary>Records the card picked, if the start page is remembering choices.</summary>
    private void RememberStart(string mode)
    {
        if (!_state.RememberStartChoice || _state.StartWith == mode) return;
        _state.StartWith = mode;
        _state.Save();
    }

    /// <summary>
    /// Puts a launcher page on screen. From the generator that hides the generator and shrinks the
    /// window; from the other launcher page it is only a swap.
    /// </summary>
    private void ShowLauncherPage(Control page)
    {
        if (ReferenceEquals(_launcherPage, page)) return;
        bool arriving = _launcherPage is null;

        SuspendLayout();
        if (_launcherPage is not null)
        {
            _launcherPage.Visible = false;
        }
        else
        {
            _workspaceBar.Visible = false;
            if (_workspaceHost is not null) _workspaceHost.Visible = false;
            if (_statusBar is not null) _statusBar.Visible = false;
            if (CaptionBar is { } caption) caption.MenuHidden = true;
        }
        _launcherPage = page;
        page.Visible = true;
        ResumeLayout();

        if (arriving) ShrinkToLauncher();
    }

    /// <summary>
    /// The launcher's size: big enough for whichever launcher page wants more, so moving between
    /// them never resizes the window.
    /// </summary>
    private Size LauncherPageSize()
    {
        var start = _start.PreferredPageSize;
        var quick = _quick.PreferredPageSize;
        return new Size(Math.Max(start.Width, quick.Width), Math.Max(start.Height, quick.Height));
    }

    /// <summary>
    /// Pulls the window in around the launcher: exactly the page size plus the caption row,
    /// centred where the generator window was, so it stays on the same monitor.
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
        var page = LauncherPageSize();
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
    /// The placement to remember when the window closes. On a launcher page that is the
    /// generator's, held aside, not the launcher's compact size.
    /// </summary>
    private (Rectangle Bounds, bool Maximized) PlacementToSave()
        => _inLauncher && !_generatorBounds.IsEmpty
            ? (_generatorBounds, _generatorMaximized)
            : (WindowState == FormWindowState.Normal ? Bounds : RestoreBounds, WindowState == FormWindowState.Maximized);

    /// <summary>Puts the generator back on screen. Does nothing when no launcher page is up.</summary>
    private void LeaveLauncher()
    {
        if (_launcherPage is not { } page) return;

        SuspendLayout();
        _launcherPage = null;
        page.Visible = false;
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
    {
        bool found = Core.GameLocator.IsGameDir(_options.GameDir);
        _start.SetGameFolder(found, _options.GameDir, _modRoot);
        _quick.SetGameFolder(found, _options.GameDir);
    }
}
