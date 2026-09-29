using Ck3MapGen.Config;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The Calendar page of the settings list: the world's era and its twelve months, typed. It fills
/// the settings column in place of the grid, so it lays out to whatever width that column is.
///
/// Every box may be left blank, and a blank one is generated at build time in the language of the
/// world's most widespread people (<see cref="WorldCalendar.Build"/>). That language does not exist
/// until the cultures do, so before a world is written a blank box says "generated"; after one,
/// its greyed placeholder is the name that world gave it (<see cref="ShowGenerated"/>), still
/// overridden by anything typed over it.
///
/// Writes straight into the live <see cref="MapConfig"/>, the same object the settings grid edits,
/// so presets carry the names with everything else. Shown only while
/// <see cref="MapConfig.CalendarEnabled"/> is on; see MainForm.SyncCalendarSection.
/// </summary>
public sealed class CalendarPanel : UserControl
{
    private const string Placeholder = "generated";

    private MapConfig? _config;
    private WorldCalendar? _generated;
    private bool _loading;

    private readonly TextBox _eraName = Box(Placeholder);
    private readonly TextBox _eraShort = Box(Placeholder);
    private readonly TextBox[] _months = [.. WorldCalendar.EnglishMonths.Select(_ => Box(Placeholder))];
    private readonly Label _preview = new()
    {
        AutoSize = true, Font = Theme.UiBold, ForeColor = Theme.Text, Margin = new Padding(3, 10, 3, 3),
    };

    public CalendarPanel()
    {
        BackColor = Theme.Background;
        AutoScroll = true;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 16),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Span(Control control)
        {
            layout.Controls.Add(control);
            layout.SetColumnSpan(control, 2);
        }

        void Row(string label, Control control)
        {
            layout.Controls.Add(new Label
            {
                Text = label, AutoSize = true, Font = Theme.Ui, ForeColor = Theme.Text,
                Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3),
            });
            layout.Controls.Add(control);
        }

        Span(Note("Anything left blank is generated when the map is built, in the language of the " +
                  "world's most widespread people. An Azgaar export's own era is used before a " +
                  "generated one; a typed era is used before either."));

        Span(Heading("Era"));
        Row("Name", _eraName);
        Row("After each year", _eraShort);
        Span(Note("Blank uses the name's initials: \"Talvek Reckoning\" becomes \"TR\"."));

        Span(Heading("Months"));
        for (int m = 0; m < _months.Length; m++) Row(WorldCalendar.EnglishMonths[m], _months[m]);
        Span(Note("Short dates use the first three letters, or more where two months would read the same."));

        Span(_preview);

        var clear = Theme.MakeButton("Clear all", 90);
        clear.Click += (_, _) =>
        {
            _eraName.Clear();
            _eraShort.Clear();
            foreach (var box in _months) box.Clear();
        };
        Span(clear);

        Controls.Add(layout);

        foreach (var box in _months.Append(_eraName).Append(_eraShort))
            box.TextChanged += (_, _) => Store();
        _eraName.TextChanged += (_, _) => ShowEraShortPlaceholder();
    }

    /// <summary>
    /// Greys the names the last written world generated into the blank boxes, so leaving a box
    /// blank shows what it becomes. Null — a preview, or a mod opened from disk — puts
    /// "generated" back, since a new world may draw different names.
    /// </summary>
    public void ShowGenerated(WorldCalendar? calendar)
    {
        _generated = calendar;
        for (int m = 0; m < _months.Length; m++)
            _months[m].PlaceholderText = UntypedMonth(m) ?? Placeholder;
        _eraName.PlaceholderText = UntypedEra?.Name is { Length: > 0 } name ? name : Placeholder;
        ShowEraShortPlaceholder();
        RefreshPreview();
    }

    private string? UntypedMonth(int m)
        => _generated?.UntypedMonths is { } names && m < names.Count ? names[m] : null;

    private (string Name, string Short)? UntypedEra
        => _generated?.UntypedEra is { Short.Length: > 0 } era ? era : null;

    /// <summary>A typed era name abbreviates itself; only a blank one falls back to the world's.</summary>
    private void ShowEraShortPlaceholder()
        => _eraShort.PlaceholderText = _eraName.Text.Trim() is { Length: > 0 } typed ? WorldCalendar.Initials(typed)
            : UntypedEra?.Short ?? Placeholder;

    /// <summary>Points the tab at a config and shows what it holds; again after a preset loads.</summary>
    public void Bind(MapConfig config)
    {
        _config = config;
        _loading = true;
        try
        {
            _eraName.Text = config.CalendarEraName;
            _eraShort.Text = config.CalendarEraShort;
            for (int m = 0; m < _months.Length; m++)
                _months[m].Text = m < config.CalendarMonths.Length ? config.CalendarMonths[m] : "";
        }
        finally { _loading = false; }
        RefreshPreview();
    }

    /// <summary>The start year moves the preview's date; the grid calls this when it changes.</summary>
    public void RefreshPreview()
    {
        if (_config is null) return;

        string month = _months[0].Text.Trim() is { Length: > 0 } typed ? typed
            : UntypedMonth(0) ?? "(generated month)";
        string era = _eraShort.Text.Trim() is { Length: > 0 } s ? s
            : _eraName.Text.Trim() is { Length: > 0 } n ? WorldCalendar.Initials(n)
            : UntypedEra?.Short ?? "(generated era)";
        _preview.Text = $"The game's first day:  1 {month}, {Math.Max(1, _config.StartYear)} {era}";
    }

    private void Store()
    {
        if (_loading || _config is null) return;

        _config.CalendarEraName = _eraName.Text.Trim();
        _config.CalendarEraShort = _eraShort.Text.Trim();
        _config.CalendarMonths = [.. _months.Select(b => b.Text.Trim())];
        RefreshPreview();
    }

    /// <summary>The notes wrap to the column: a label's wrap width is a maximum size, not its container's.</summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        int width = Math.Max(120, ClientSize.Width - 36);
        foreach (var note in _notes)
            if (note.MaximumSize.Width != width) note.MaximumSize = new Size(width, 0);
        base.OnLayout(e);
    }

    private readonly List<Label> _notes = [];

    private static TextBox Box(string placeholder) => new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Font = Theme.Ui,
        BackColor = Theme.Surface,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = placeholder,
        Margin = new Padding(3, 3, 3, 3),
    };

    private static Label Heading(string text) => new()
    {
        Text = text, AutoSize = true, Font = Theme.UiBold, ForeColor = Theme.Text,
        Margin = new Padding(3, 14, 3, 4),
    };

    private Label Note(string text)
    {
        var note = new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(300, 0), Font = Theme.Ui,
            ForeColor = Theme.TextDim, Margin = new Padding(3, 3, 3, 6),
        };
        _notes.Add(note);
        return note;
    }
}
