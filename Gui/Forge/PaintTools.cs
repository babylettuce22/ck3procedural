using NoiseTool.Pipeline;

// WinForms drags in System.Drawing, which has its own Brush. Alias rather than rely on using
// order, so the reference cannot silently rebind to the wrong one later.
using PaintBrush = NoiseTool.Pipeline.Brush;

namespace Ck3MapGen.Gui.Forge;

/// <summary>
/// The brush palette: which channel of the selected stage is being painted, with what tool, and
/// how big and hard the brush is. Appears only when the selected stage is <see cref="IPaintable"/>.
///
/// Holds no layer state of its own — the stage owns the layers, this owns the choice of brush.
/// </summary>
public sealed class PaintToolStrip : Panel
{
    private readonly ComboBox _channel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 132 };
    private readonly FlowLayoutPanel _tools = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    private readonly TrackBar _radius = new() { Minimum = 2, Maximum = 300, Value = 30, TickStyle = TickStyle.None, Width = 104, AutoSize = false, Height = 26 };
    private readonly TrackBar _strength = new() { Minimum = 5, Maximum = 100, Value = 60, TickStyle = TickStyle.None, Width = 84, AutoSize = false, Height = 26 };
    private readonly TrackBar _hardness = new() { Minimum = 0, Maximum = 100, Value = 50, TickStyle = TickStyle.None, Width = 84, AutoSize = false, Height = 26 };
    private readonly Label _hint = new() { AutoSize = false, Dock = DockStyle.Bottom, Height = 18, ForeColor = Theme.TextDim, Padding = new Padding(6, 0, 0, 0) };
    private readonly Button _undo = Theme.MakeButton("Undo", 54);
    private readonly Button _redo = Theme.MakeButton("Redo", 54);
    private readonly Button _clear = Theme.MakeButton("Clear layer", 82);
    private readonly ToolTip _tips = new() { AutoPopDelay = 15000, InitialDelay = 400 };

    private readonly Panel _controls = new();
    private readonly Label _idle = new()
    {
        Dock = DockStyle.Top,
        Height = 24,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0),
        ForeColor = Theme.TextDim,
        Visible = false,
    };

    private readonly List<Button> _toolButtons = new();
    private IReadOnlyList<PaintChannel> _channels = [];
    private PaintTool _tool = PaintTool.Raise;
    private bool _loading;

    /// <summary>The brush changed, or a different channel or tool was chosen.</summary>
    public event Action? BrushChanged;

    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? ClearRequested;

    public PaintToolStrip()
    {
        BackColor = Theme.Surface;
        Height = 84;
        Dock = DockStyle.Top;
        Visible = false;

        _channel.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            BuildToolButtons();
            BrushChanged?.Invoke();
        };

        foreach (var bar in new[] { _radius, _strength, _hardness })
            bar.ValueChanged += (_, _) => { if (!_loading) BrushChanged?.Invoke(); };

        _tips.SetToolTip(_radius, "Brush radius, as a fraction of map height ( [ and ] )");
        _tips.SetToolTip(_strength, "How much one stroke lays down");
        _tips.SetToolTip(_hardness, "0 = soft edge, 100 = hard edge");

        _undo.Click += (_, _) => UndoRequested?.Invoke();
        _redo.Click += (_, _) => RedoRequested?.Invoke();
        _clear.Click += (_, _) => ClearRequested?.Invoke();
        _tips.SetToolTip(_undo, "Undo the last stroke (Ctrl+Z)");
        _tips.SetToolTip(_redo, "Redo (Ctrl+Y)");
        _tips.SetToolTip(_clear, "Erase everything painted in this channel");

        var row1 = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            WrapContents = false,
            Padding = new Padding(4, 3, 4, 0),
        };
        row1.Controls.Add(Caption("Paint"));
        row1.Controls.Add(_channel);
        row1.Controls.Add(_tools);

        var row2 = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            WrapContents = false,
            Padding = new Padding(4, 2, 4, 0),
        };
        row2.Controls.Add(Caption("Size"));
        row2.Controls.Add(_radius);
        row2.Controls.Add(Caption("Strength"));
        row2.Controls.Add(_strength);
        row2.Controls.Add(Caption("Hardness"));
        row2.Controls.Add(_hardness);
        row2.Controls.Add(_undo);
        row2.Controls.Add(_redo);
        row2.Controls.Add(_clear);

        _controls.Dock = DockStyle.Fill;
        _controls.Controls.Add(_hint);
        _controls.Controls.Add(row2);
        _controls.Controls.Add(row1);

        Controls.Add(_controls);
        Controls.Add(_idle);
    }

    /// <summary>
    /// Points the palette at a stage's channels. A stage with nothing to paint leaves the strip
    /// in place showing where the brushes are instead — a palette that vanishes tells the user
    /// nothing about why.
    /// </summary>
    /// <param name="elsewhere">What to say when this stage cannot be painted.</param>
    public void Bind(IPaintable? stage, string elsewhere)
    {
        _loading = true;
        try
        {
            _channels = stage?.Channels ?? [];
            _channel.Items.Clear();
            foreach (var channel in _channels) _channel.Items.Add(channel.Label);
            if (_channel.Items.Count > 0) _channel.SelectedIndex = 0;
        }
        finally
        {
            _loading = false;
        }

        bool paintable = _channels.Count > 0;
        Visible = true;
        Height = paintable ? 84 : 26;

        _controls.Visible = paintable;
        _idle.Visible = !paintable;
        _idle.Text = "Nothing to paint on this stage.  " + elsewhere;

        if (paintable) BuildToolButtons();
        else _hint.Text = "";
    }

    public PaintChannel? Channel
        => _channel.SelectedIndex >= 0 && _channel.SelectedIndex < _channels.Count
            ? _channels[_channel.SelectedIndex]
            : null;

    public PaintTool Tool => _tool;

    public PaintBrush Brush => new(
        Radius: _radius.Value / 1000f,
        Hardness: _hardness.Value / 100f,
        Strength: _strength.Value / 100f);

    /// <summary>Brush radius in normalised map-height units, for the cursor ring.</summary>
    public float RadiusNormalised => _radius.Value / 1000f;

    public void NudgeRadius(int delta)
    {
        int next = Math.Clamp(_radius.Value + delta, _radius.Minimum, _radius.Maximum);
        if (next != _radius.Value) _radius.Value = next;
    }

    public void SetHistoryState(bool canUndo, bool canRedo)
    {
        _undo.Enabled = canUndo;
        _redo.Enabled = canRedo;
    }

    private void BuildToolButtons()
    {
        _tools.Controls.Clear();
        _toolButtons.Clear();

        var channel = Channel;
        if (channel is null) return;

        _hint.Text = channel.Hint;

        foreach (var tool in channel.Tools)
        {
            var button = Theme.MakeButton(tool.ToString(), 62);
            var captured = tool;
            button.Click += (_, _) => SelectTool(captured);
            _tools.Controls.Add(button);
            _toolButtons.Add(button);
        }

        SelectTool(channel.Tools.Count > 0 ? channel.Tools[0] : PaintTool.Erase);
    }

    private void SelectTool(PaintTool tool)
    {
        _tool = tool;

        // The chosen tool is the one drawn as primary; MakeButton bakes its colours in, so the
        // selection is shown by hand rather than by rebuilding the row.
        foreach (var button in _toolButtons)
        {
            bool active = button.Text == tool.ToString();
            button.BackColor = active ? Theme.Accent : Theme.Surface;
            button.ForeColor = active ? Theme.AccentText : Theme.Text;
            button.FlatAppearance.BorderColor = active ? Theme.Accent : Theme.Border;
        }

        BrushChanged?.Invoke();
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.TextDim,
        Margin = new Padding(6, 7, 3, 0),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
