using Ck3MapGen.Config;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The Calendar tab: the world's era and its twelve months, typed.
///
/// Every box may be left blank, and a blank one is generated at build time in the language of the
/// world's most widespread people (<see cref="WorldCalendar.Build"/>). So the tab cannot show the
/// generated names — that language does not exist until the cultures do — and says "generated"
/// instead. What it can show is the typed half, as a date the game would render.
///
/// Writes straight into the live <see cref="MapConfig"/>, the same object the settings grid edits,
/// so presets carry the names with everything else. Shown only while
/// <see cref="MapConfig.CalendarEnabled"/> is on; see MainForm.SyncCalendarTab.
/// </summary>
public sealed class CalendarPanel : UserControl
{
    private MapConfig? _config;
    private bool _loading;

    private readonly TextBox _eraName = Box("generated");
    private readonly TextBox _eraShort = Box("generated");
    private readonly TextBox[] _months = [.. WorldCalendar.EnglishMonths.Select(_ => Box("generated"))];
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
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 12, 16, 16),
            Location = new Point(0, 0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));

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
    }

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

        string month = _months[0].Text.Trim() is { Length: > 0 } typed ? typed : "(generated month)";
        string era = _eraShort.Text.Trim() is { Length: > 0 } s ? s
            : _eraName.Text.Trim() is { Length: > 0 } n ? WorldCalendar.Initials(n)
            : "(generated era)";
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

    private static TextBox Box(string placeholder) => new()
    {
        Width = 230,
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

    private static Label Note(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(370, 0), Font = Theme.Ui,
        ForeColor = Theme.TextDim, Margin = new Padding(3, 3, 3, 6),
    };
}
