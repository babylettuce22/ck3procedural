namespace Ck3MapGen.AppGUI;

/// <summary>
/// The shared shape of a guide window: a fixed-width scrolling column of headings, numbered steps
/// and notes, with an action bar underneath. The walkthroughs differ only in their words and
/// their buttons, and the layout fiddliness — wrapping labels, indent, spacing — is exactly the
/// part worth writing once.
///
/// Wears the app's drawn caption (<see cref="ChromeForm"/>), like every other window of the tool.
/// </summary>
public abstract class GuideForm : ChromeForm
{
    protected static readonly int Body = Dpi.S(520);

    private readonly FlowLayoutPanel _steps = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = Dpi.Pad(16, 10, 16, 10),
        BackColor = Theme.Background,
    };

    private readonly FlowLayoutPanel _bar = new()
    {
        Dock = DockStyle.Bottom,
        Height = Dpi.S(38),
        Padding = Dpi.Pad(12, 5, 4, 4),
        BackColor = Theme.Surface,
    };

    /// <param name="height">In 96-DPI pixels, like <see cref="Body"/> was written.</param>
    protected GuideForm(string title, int height)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 4096, 4096);
        ClientSize = new Size(Body + Dpi.S(60), Math.Min(Dpi.S(height), work.Height - Dpi.S(40)));
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        // A guide belongs to the main window, which already shows the application's icon; the
        // caption row carries the guide's title alone, as an inspector's does.
        ShowIcon = false;

        Controls.Add(_steps);
        Controls.Add(_bar);

        // Added last so they dock first: the caption row on top, a hairline under it.
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(CaptionBar = new TitleBar(this));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.ApplyTitleBar(this);
    }

    protected void AddAction(Button button) => _bar.Controls.Add(button);

    protected void AddCloseAction()
    {
        var close = Theme.MakeButton("Close", 70);
        close.Click += (_, _) => Close();
        AddAction(close);
    }

    protected void Heading(string text)
        => _steps.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Font = Theme.UiBold,
            ForeColor = Theme.Text,
            Margin = Dpi.Pad(0, 12, 0, 4),
        });

    protected void Step(int number, string text)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = Dpi.Pad(0, 2, 0, 2),
            BackColor = Color.Transparent,
        };

        row.Controls.Add(new Label
        {
            Text = $"{number}.",
            AutoSize = true,
            Width = Dpi.S(22),
            Font = Theme.UiBold,
            ForeColor = Theme.TextDim,
            Margin = Dpi.Pad(0, 0, 4, 0),
        });

        row.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(Body - Dpi.S(30), 0),
            ForeColor = Theme.Text,
            Margin = new Padding(0),
        });

        _steps.Controls.Add(row);
    }

    /// <summary>One shortcut line: the keys in bold, what they do beside them.</summary>
    protected void Shortcut(string keys, string what)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = Dpi.Pad(0, 1, 0, 1),
            BackColor = Color.Transparent,
        };

        row.Controls.Add(new Label
        {
            Text = keys,
            AutoSize = false,
            Width = Dpi.S(110),
            Height = Dpi.S(23),
            Font = Theme.UiBold,
            ForeColor = Theme.TextDim,
            Margin = Dpi.Pad(0, 0, 4, 0),
        });

        row.Controls.Add(new Label
        {
            Text = what,
            AutoSize = true,
            MaximumSize = new Size(Body - Dpi.S(120), 0),
            ForeColor = Theme.Text,
            Margin = new Padding(0),
        });

        _steps.Controls.Add(row);
    }

    protected void Note(string text)
        => _steps.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(Body, 0),
            ForeColor = Theme.TextDim,
            Margin = Dpi.Pad(0, 14, 0, 6),
        });
}
