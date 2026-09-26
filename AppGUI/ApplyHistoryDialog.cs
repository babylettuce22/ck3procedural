using System.Globalization;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Asks, before a history is applied, which year the world should start in — and says what
/// applying will and will not touch.
///
/// A history stops wherever the player pressed pause, and that year is rarely the one they want
/// on the bookmark. Writing it as another year is a relabel, not more simulation: every date in the
/// history moves together (see <see cref="MapGen.AppliedHistory.ShiftedBy"/>), and a world grows
/// the same realms from any start with the same formation epochs, so the range offered is exactly
/// the one within which the world stays itself. The same dialog changes the year of a history
/// already applied.
///
/// Wears the app's drawn caption (<see cref="ChromeForm"/>), like every other window of the tool.
/// </summary>
internal sealed class ApplyHistoryDialog : ChromeForm
{
    private const int Inner = 492;

    private readonly TextBox _year = new()
    {
        Width = 90,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Surface,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
    };

    private readonly Label _preview = new() { AutoSize = true, ForeColor = Theme.TextDim, Font = Theme.Ui, Margin = new Padding(10, 5, 0, 0) };
    private readonly Label _range = new() { AutoSize = true, MaximumSize = new Size(Inner, 0), Font = Theme.Ui, Margin = new Padding(0, 4, 0, 8) };
    private readonly Button _ok = Theme.MakeButton("Apply", 90, primary: true);
    private readonly Button _cancel = Theme.MakeButton("Cancel", 76);

    private readonly int _stopped, _min, _max;
    private readonly MapGen.WorldCalendar? _calendar;

    /// <summary>The year chosen, once the dialog has been accepted.</summary>
    public int Year { get; private set; }

    /// <param name="stopped">The year the history stands at: where the History workspace stopped, or
    /// the year an applied one is written as.</param>
    /// <param name="range">The years it can be written as — see <see cref="MapGen.AppliedHistory.YearRange"/>.</param>
    /// <param name="what">What applying rewrites, in a sentence or two.</param>
    /// <param name="changing">True to change the year of a history already applied.</param>
    public ApplyHistoryDialog(int stopped, (int Min, int Max) range, MapGen.WorldCalendar? calendar, string what,
        int advancement, bool changing)
    {
        (_stopped, _min, _max, _calendar) = (stopped, range.Min, range.Max, calendar);
        Year = stopped;

        Text = changing ? "Change the start year" : "Apply history";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        AcceptButton = _ok;
        CancelButton = _cancel;
        _ok.Text = changing ? "Change" : "Apply";

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14, 12, 14, 10),
            BackColor = Theme.Background,
        };

        Label Para(string text, Color colour, Padding margin) => new()
        {
            Text = text, AutoSize = true, MaximumSize = new Size(Inner, 0), ForeColor = colour, Font = Theme.Ui, Margin = margin,
        };

        list.Controls.Add(Para("Start the world in", Theme.TextDim, new Padding(0, 0, 0, 2)));

        var yearRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Background };
        yearRow.Controls.Add(_year);
        yearRow.Controls.Add(_preview);
        list.Controls.Add(yearRow);
        list.Controls.Add(_range);

        list.Controls.Add(Para(changing
            ? "Every date in the applied history moves with it — births, reigns, deaths, wars, truces and "
              + "claims. Nothing is simulated again: the realms, rulers and families stay as they are."
            : $"The history stopped in {stopped}. Written as another year, every date in it moves with it — "
              + "births, reigns, wars, truces — and nothing is simulated again.",
            Theme.Text, new Padding(0, 0, 0, 8)));
        list.Controls.Add(Para(what, Theme.TextDim, new Padding(0, 0, 0, 8)));
        list.Controls.Add(Para($"Advancement stays at {advancement}: innovations, development and the era "
            + "the world is judged against do not move with the year.", Theme.TextDim, new Padding(0, 0, 0, 8)));

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Width = Inner,
            Margin = new Padding(0, 4, 0, 0),
            BackColor = Theme.Background,
        };
        buttons.Controls.Add(_ok);
        buttons.Controls.Add(_cancel);
        list.Controls.Add(buttons);

        Controls.Add(list);

        // Added last so they dock first: the caption row on top, a hairline under it. No icon — a
        // dialog of the main window, which already shows it.
        ShowIcon = false;
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(CaptionBar = new TitleBar(this));

        // Tall enough for whatever the paragraphs wrapped to.
        int height = list.Padding.Vertical + list.Controls.Cast<Control>().Sum(c => c.GetPreferredSize(new Size(Inner, 0)).Height + c.Margin.Vertical);
        ClientSize = new Size(Inner + list.Padding.Horizontal, height + CaptionBar.Height + 1);

        _year.Text = stopped.ToString(CultureInfo.InvariantCulture);
        _year.TextChanged += (_, _) => Describe();
        _ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Describe();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.ApplyLightTitleBar(this);
        _year.Focus();
        _year.SelectAll();
    }

    /// <summary>Keeps the calendar line, the range note and the button in step with the box.</summary>
    private void Describe()
    {
        bool valid = int.TryParse(_year.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int year)
                     && year >= _min && year <= _max;
        _ok.Enabled = valid;

        if (valid)
        {
            Year = year;
            string month = _calendar?.Months is { Count: > 0 } months ? months[0] : "January";
            string era = _calendar?.EraShort.Trim() is { Length: > 0 } short_ ? $" {short_}" : "";
            _preview.Text = $"The game's first day:  1 {month}, {year}{era}"
                            + (year == _stopped ? "" : $"  ({(year > _stopped ? "+" : "−")}{Math.Abs(year - _stopped)} years)");
        }
        else _preview.Text = "";

        _range.ForeColor = valid ? Theme.TextDim : Theme.Danger;
        _range.Text = $"Any year from {_min} to {_max}. Earlier than that, the centuries of realm formation "
                      + "before the history would no longer fit, and the world would grow different realms.";
    }
}
