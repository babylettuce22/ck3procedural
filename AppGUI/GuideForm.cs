namespace Ck3MapGen.AppGUI;

/// <summary>
/// The shared shape of a guide window: a fixed-width scrolling column of headings, numbered steps
/// and notes, with an action bar underneath. The walkthroughs differ only in their words and
/// their buttons, and the layout fiddliness — wrapping labels, indent, spacing — is exactly the
/// part worth writing once.
/// </summary>
public abstract class GuideForm : Form
{
    protected const int Body = 520;

    private readonly FlowLayoutPanel _steps = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(16, 10, 16, 10),
        BackColor = Theme.Background,
    };

    private readonly FlowLayoutPanel _bar = new()
    {
        Dock = DockStyle.Bottom,
        Height = 38,
        Padding = new Padding(12, 5, 4, 4),
        BackColor = Theme.Surface,
    };

    protected GuideForm(string title, int height)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ClientSize = new Size(Body + 60, height);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        Controls.Add(_steps);
        Controls.Add(_bar);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.ApplyLightTitleBar(this);
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
            Margin = new Padding(0, 12, 0, 4),
        });

    protected void Step(int number, string text)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 2, 0, 2),
            BackColor = Color.Transparent,
        };

        row.Controls.Add(new Label
        {
            Text = $"{number}.",
            AutoSize = true,
            Width = 22,
            Font = Theme.UiBold,
            ForeColor = Theme.TextDim,
            Margin = new Padding(0, 0, 4, 0),
        });

        row.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(Body - 30, 0),
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
            Margin = new Padding(0, 1, 0, 1),
            BackColor = Color.Transparent,
        };

        row.Controls.Add(new Label
        {
            Text = keys,
            AutoSize = false,
            Width = 110,
            Font = Theme.UiBold,
            ForeColor = Theme.TextDim,
            Margin = new Padding(0, 0, 4, 0),
        });

        row.Controls.Add(new Label
        {
            Text = what,
            AutoSize = true,
            MaximumSize = new Size(Body - 120, 0),
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
            Margin = new Padding(0, 14, 0, 6),
        });
}
