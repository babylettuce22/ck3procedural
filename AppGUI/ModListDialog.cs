using Ck3MapGen.Core;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The launcher's mod checkboxes, without the launcher. Lists everything in the mod folder —
/// workshop subscriptions and local mods alike, since <see cref="ModLibrary"/> cannot tell the
/// difference and neither can the game — and hands back what was ticked, for
/// <see cref="DlcLoad.Enable"/> to write.
///
/// It exists because this tool starts <c>ck3.exe</c> directly, which honours <c>dlc_load.json</c>
/// and nothing else. Before this, the only thing the tool could say about that file was "the
/// generated map, alone", and anyone wanting the map beside a debug-menu mod had to go through the
/// Paradox launcher — which rewrites the file from its own playsets and so undoes whatever was set
/// here. One list in one place is the fix.
///
/// The order shown is the order written, and load order is what decides which mod wins a file both
/// of them ship. The generated map is pinned to the bottom for that reason: last loaded, so its
/// landed_titles and history are the ones that survive. There is no reordering beyond that yet, and
/// no conflict analysis — the warning under the list is the honest summary of what stacking a total
/// conversion costs, and a real per-mod verdict wants the file scan that is not written yet.
/// </summary>
internal sealed class ModListDialog : Form
{
    private readonly CheckedListBox _list = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Surface,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        CheckOnClick = true,
        IntegralHeight = false,
    };

    private readonly Label _count = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        ForeColor = Theme.TextDim,
        Font = Theme.Ui,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private readonly Label _warning = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        ForeColor = Theme.TextDim,
        Font = Theme.Ui,
        Text = "A generated map replaces landed titles, provinces and history outright. A mod that "
               + "does the same, or that names vanilla provinces, will either be ignored or hang the "
               + "load. Anything that only adds interface, events or traits is usually fine.",
    };

    private readonly Button _ok = Theme.MakeButton("Apply", 84, primary: true);
    private readonly Button _cancel = Theme.MakeButton("Cancel", 76);

    private readonly List<Row> _rows = [];

    /// <summary>What was ticked, in load order, as <c>dlc_load.json</c> names it.</summary>
    public IReadOnlyList<string> Selected =>
        _rows.Where((_, i) => _list.GetItemChecked(i)).Select(r => r.Mod.Entry).ToList();

    public ModListDialog(
        IReadOnlyList<ModEntry> mods,
        IReadOnlyList<ModEntry> unregistered,
        IReadOnlyList<string> enabled,
        string? ourEntry)
    {
        BuildRows(mods, unregistered, enabled, ourEntry);

        Text = "Mods";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        ClientSize = new Size(560, 470);
        MinimumSize = new Size(440, 340);
        AcceptButton = _ok;
        CancelButton = _cancel;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(14, 12, 14, 10),
            BackColor = Theme.Background,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // caption
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // the list, and the slack
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // the count
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));  // the warning
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons

        var caption = new Label
        {
            Text = "What Crusader Kings III loads on the next start, top to bottom.",
            AutoSize = true,
            ForeColor = Theme.TextDim,
            Margin = new Padding(0, 0, 0, 6),
        };

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

        layout.Controls.Add(caption, 0, 0);
        layout.Controls.Add(_list, 0, 1);
        layout.Controls.Add(_count, 0, 2);
        layout.Controls.Add(_warning, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);

        _list.ItemCheck += OnItemCheck;
        _ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Describe(null, false);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.ApplyLightTitleBar(this);
    }

    /// <summary>
    /// The rows, in the order they will be written: what is enabled now first, so the list opens
    /// looking like the state it describes, then everything else by name, then the generated map
    /// last of all, then the mods that cannot be ticked at all.
    /// </summary>
    private void BuildRows(
        IReadOnlyList<ModEntry> mods,
        IReadOnlyList<ModEntry> unregistered,
        IReadOnlyList<string> enabled,
        string? ourEntry)
    {
        var on = enabled.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byEntry = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods) byEntry.TryAdd(Key(mod.Entry), mod);

        bool Ours(ModEntry m) => ourEntry is not null && Key(m.Entry) == Key(ourEntry);

        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ModEntry>();

        foreach (string entry in enabled)
            if (byEntry.TryGetValue(Key(entry), out var mod) && !Ours(mod) && placed.Add(Key(mod.Entry)))
                ordered.Add(mod);

        foreach (var mod in mods)
            if (!Ours(mod) && placed.Add(Key(mod.Entry)))
                ordered.Add(mod);

        if (mods.FirstOrDefault(Ours) is { } ours) ordered.Add(ours);

        foreach (var mod in ordered) _rows.Add(new Row(mod, Ours(mod), Locked: false));
        foreach (var mod in unregistered) _rows.Add(new Row(mod, Ours: false, Locked: true));

        foreach (var row in _rows)
            _list.Items.Add(row, !row.Locked && on.Contains(Key(row.Mod.Entry)));
    }

    /// <summary>
    /// Refuses the tick on a row that has nothing to tick — a workshop item with no descriptor in
    /// the mod folder has no name <c>dlc_load.json</c> could call it by, so a checkbox that stayed
    /// down would be a promise the write cannot keep.
    /// </summary>
    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        var row = _rows[e.Index];

        if (row.Locked)
        {
            e.NewValue = CheckState.Unchecked;
            Describe(null, false);
            return;
        }

        Describe(e.Index, e.NewValue == CheckState.Checked);
    }

    /// <summary>
    /// The running count under the list. <see cref="OnItemCheck"/> fires before the box changes, so
    /// the row being clicked is counted from the event rather than from the control.
    /// </summary>
    private void Describe(int? changing, bool nowChecked)
    {
        int enabled = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            bool ticked = i == changing ? nowChecked : _list.GetItemChecked(i);
            if (ticked) enabled++;
        }

        int listable = _rows.Count(r => !r.Locked);
        int locked = _rows.Count - listable;

        string text = $"{enabled} of {listable} enabled";
        if (locked > 0)
            text += $" · {locked} subscribed but not registered — open the Paradox launcher once";

        _count.Text = text;
        _count.ForeColor = enabled == 0 ? Theme.Danger : Theme.TextDim;
    }

    /// <summary>Entries are compared the way <see cref="DlcLoad"/> compares them.</summary>
    private static string Key(string entry) => entry.Replace('\\', '/').Trim();

    private sealed record Row(ModEntry Mod, bool Ours, bool Locked)
    {
        public override string ToString()
        {
            string note =
                Locked ? "not registered"
                : Ours ? "this generator's map — loads last"
                : Mod.ContentMissing ? "content missing"
                : Mod.IsWorkshop ? "workshop"
                : "local";

            return $"{Mod.Name}  —  {note}";
        }
    }
}
