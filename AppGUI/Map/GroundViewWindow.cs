namespace Ck3MapGen.AppGUI;

/// <summary>
/// The finished world in 3D, wearing its CK3 ground: the shipped heightmap as the game will draw it
/// (the packer's round-trip, <see cref="HeightfieldPanel.ShowAsCk3Renders"/>) with the
/// <see cref="GroundPreview"/> render draped over it. Opened from the launcher's done screen, so a
/// Quick world can be looked at in relief without leaving for Complex.
///
/// Its own window rather than a trip to the World workspace's 3D tab: the done screen is where the
/// next move is chosen, and looking should not lose that. It owns nothing the main window keeps —
/// the fields and the drape are handed over whole — so closing it is all the cleanup there is.
/// </summary>
internal sealed class GroundViewWindow : ChromeForm
{
    private readonly HeightfieldPanel _solid = new() { Dock = DockStyle.Fill, ShowAsCk3Renders = true };

    private readonly TrackBar _exaggeration = new()
    {
        Minimum = 20,
        Maximum = 400,
        Value = 100,
        TickStyle = TickStyle.None,
        Width = 110,
        Height = 24,
    };

    private readonly Label _readout = new()
    {
        AutoSize = true,
        Padding = new Padding(8, 5, 0, 0),
        ForeColor = Theme.TextDim,
        Font = Theme.Ui,
        Text = "drag to orbit · right-drag to pan · wheel to zoom · double-click to reset",
    };

    public GroundViewWindow(string modName, Heightfield source, Heightfield? packed, PreviewRenderer.Image ground)
    {
        Text = $"{modName} — CK3 ground in 3D";
        // CenterParent only applies to ShowDialog; this one is modeless.
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(480, 360);
        Size = new Size(1280, 800);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(4, 3, 4, 0),
            BackColor = Theme.Surface,
        };

        _exaggeration.ValueChanged += (_, _) => _solid.SetExaggeration(_exaggeration.Value / 100.0);

        var reset = Theme.MakeButton("Reset view", 82);
        reset.Click += (_, _) => _solid.ResetView();

        strip.Controls.Add(new Label
        {
            Text = "Relief",
            AutoSize = false,
            Width = 44,
            Height = 24,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui,
        });
        strip.Controls.Add(_exaggeration);
        strip.Controls.Add(reset);
        strip.Controls.Add(_readout);

        Controls.Add(_solid);
        Controls.Add(strip);

        // Added last so they dock first: the caption row on top, a hairline under it.
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(CaptionBar = new TitleBar(this));

        // The drape before the field, so the first frame is already the ground and not the tints.
        _solid.SetDrape(ground);
        _solid.SetField(source, packed, "Nothing to show.");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.ApplyTitleBar(this);
        _solid.Focus();
    }
}
