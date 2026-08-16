using Ck3MapGen.Core;

namespace Ck3MapGen.Gui;

/// <summary>
/// Asks what the mod is called and where it goes, immediately before a write.
///
/// Writing was previously unconditional into a folder called <c>proceduralmap</c>, which meant two
/// maps could not coexist: the second write landed on top of the first with no warning, and the
/// launcher had one entry for whichever was most recent. A name per map is the whole fix, and asking
/// at the moment of writing is the only place it can be asked without becoming another setting in a
/// grid of two hundred.
///
/// The dialog is also where the destination is shown at all. The full path is spelled out under the
/// box and updates as it is typed, so the answer to "where did it go" is on screen before the write
/// rather than in the log after it — which matters more now that the mod folder is searched for
/// rather than assumed and is therefore not necessarily on C:.
/// </summary>
internal sealed class ModNameDialog : Form
{
    private readonly TextBox _name = new()
    {
        Width = 380,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Surface,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
    };

    private readonly Label _path = new()
    {
        AutoSize = false,
        Width = 460,
        Height = 34,
        ForeColor = Theme.TextDim,
        Font = Theme.Mono,
    };

    private readonly Label _note = new()
    {
        AutoSize = false,
        Width = 460,
        Height = 32,
        ForeColor = Theme.TextDim,
        Font = Theme.Ui,
    };

    private readonly Button _ok = Theme.MakeButton("Write mod", 96, primary: true);
    private readonly Button _cancel = Theme.MakeButton("Cancel", 76);
    private readonly Button _browse = Theme.MakeButton("Change…", 84);

    private string _root;

    /// <summary>The folder the mod will be written into, once the dialog has been accepted.</summary>
    public string ModDir => Path.Combine(_root, FolderName(_name.Text));

    /// <summary>What the launcher will show, which is the typed text rather than the folder.</summary>
    public string ModDisplayName => _name.Text.Trim();

    /// <summary>The mod folder the chosen destination sits in, so it can be remembered.</summary>
    public string ModRoot => _root;

    public ModNameDialog(string root, string name)
    {
        _root = root;
        _name.Text = name;

        Text = "Write mod";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        ClientSize = new Size(500, 230);
        AcceptButton = _ok;
        CancelButton = _cancel;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(14, 12, 14, 10),
            BackColor = Theme.Background,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Spelled out rather than left to the default. An unstyled TableLayoutPanel divides its
        // height evenly, which puts the buttons in the middle of the dialog.
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // caption
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // the name box
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // the path it resolves to
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // the note, and the slack
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons, pinned to the bottom

        var caption = new Label
        {
            Text = "Mod name",
            AutoSize = true,
            ForeColor = Theme.TextDim,
            Margin = new Padding(0, 0, 0, 2),
        };

        layout.Controls.Add(caption, 0, 0);
        layout.SetColumnSpan(caption, 2);
        layout.Controls.Add(_name, 0, 1);
        layout.Controls.Add(_browse, 1, 1);
        layout.Controls.Add(_path, 0, 2);
        layout.SetColumnSpan(_path, 2);
        layout.Controls.Add(_note, 0, 3);
        layout.SetColumnSpan(_note, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Theme.Background,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        buttons.Controls.Add(_ok);
        buttons.Controls.Add(_cancel);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);

        _name.TextChanged += (_, _) => Describe();
        _browse.Click += (_, _) => PickRoot();
        _ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Describe();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.ApplyLightTitleBar(this);

        // Selected rather than merely focused: the common case is replacing the last map's name
        // outright, not editing it.
        _name.Focus();
        _name.SelectAll();
    }

    private void PickRoot()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Where the mod folder is created — normally the launcher's mod folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_root) ? _root : "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _root = dialog.SelectedPath;
        Describe();
    }

    /// <summary>
    /// Keeps the path preview, the overwrite warning and the OK button in step with the box.
    /// </summary>
    private void Describe()
    {
        string folder = FolderName(_name.Text);

        if (folder.Length == 0)
        {
            _path.Text = "";
            _note.ForeColor = Theme.Danger;
            _note.Text = "Give the mod a name.";
            _ok.Enabled = false;
            return;
        }

        string dir = Path.Combine(_root, folder);
        _path.Text = dir;
        _ok.Enabled = true;

        if (Directory.Exists(dir))
        {
            _note.ForeColor = Theme.Danger;
            _note.Text = "This folder already exists. Writing replaces the map files in it — "
                         + "anything else you have put there is left alone.";
        }
        else if (!folder.Equals(_name.Text.Trim(), StringComparison.Ordinal))
        {
            // Only worth saying when the two actually differ, which is rare: the launcher will show
            // the typed name and the disk will show the sanitised one.
            _note.ForeColor = Theme.TextDim;
            _note.Text = $"The launcher will list this as “{_name.Text.Trim()}”.";
        }
        else
        {
            _note.ForeColor = Theme.TextDim;
            _note.Text = "A new folder, listed in the launcher under this name.";
        }
    }

    /// <summary>
    /// The typed name as a folder name.
    ///
    /// Only the characters Windows refuses are replaced, and trailing dots and spaces dropped —
    /// spaces and capitals are left alone, because CK3 does not care and a folder called
    /// <c>My Second Map</c> is easier to find than <c>my_second_map</c>. A name that sanitises away
    /// to nothing is rejected by the caller rather than silently renamed.
    /// </summary>
    public static string FolderName(string typed)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(typed.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.TrimEnd('.', ' ');
    }
}
