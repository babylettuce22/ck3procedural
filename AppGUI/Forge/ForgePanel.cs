using NoiseTool.Core;
using NoiseTool.Pipeline;
using NoiseTool.Stages;
using NoiseTool.UI;

namespace Ck3MapGen.AppGUI.Forge;

/// <summary>
/// The Heightmap tab: CK3 Heightmap Forge inside the generator. A project box, the stage list
/// and the parameter editor on the left; the live preview on the right with a strip above it for
/// the view, the preview budget, presets, export and — the button the tab exists for — "Use for
/// generation", which hands the pipeline to the main window as its heightmap source.
///
/// Everything that is not a control is in <see cref="ForgeSession"/>; this class only lays out
/// and forwards. The shared controls come from NoiseTool.Ui, so a stage that adds a parameter in
/// the Forge repo grows a slider here without anyone touching this file.
/// </summary>
public sealed class ForgePanel : UserControl
{
    public ForgeSession Session { get; } = new();

    /// <summary>The user pressed "Use for generation": the host should install the session's provider.</summary>
    /// <summary>
    /// Raised by Use for generation. The argument is whether the user agreed to build at an export
    /// size CK3 is not known to render — false on any size in <see cref="MapGen.TileFit.Known"/>.
    /// </summary>
    public event Action<bool>? UseForGeneration;

    /// <summary>Where the preset dialogs open. The host persists it.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? PresetDir { get; set; }

    private readonly SplitContainer _split;
    private readonly StageListBox _stages = new();
    private readonly ParameterPanel _params = new();
    private readonly ImageView _canvas = new() { Dock = DockStyle.Fill, ViewName = "heightmap" };
    private readonly Label _statusLine;
    private readonly Label _banner;
    private readonly ToolTip _tips = new() { AutoPopDelay = 15000, InitialDelay = 400 };

    private readonly ComboBox _resPreset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly NumericUpDown _baseWidth = new();
    private readonly NumericUpDown _baseHeight = new();
    private readonly NumericUpDown _seed = new() { Minimum = int.MinValue, Maximum = int.MaxValue, Width = 110 };
    private readonly Label _resInfo = new() { AutoSize = true, MaximumSize = new Size(320, 0), ForeColor = Theme.TextDim };

    private readonly ComboBox _view = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox _previewRes = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 86 };
    private readonly CheckBox _auto = new() { Text = "Auto", Checked = true, AutoSize = true, Margin = new Padding(6, 6, 6, 0) };
    private readonly Button _generate = Theme.MakeButton("Generate", 80);
    private readonly Button _bake = Theme.MakeButton("Bake", 64);
    private readonly Button _use = Theme.MakeButton("Use for generation", 140, primary: true);
    private readonly Button _loadPreset = Theme.MakeButton("Load preset…", 100);
    private readonly Button _savePreset = Theme.MakeButton("Save preset…", 100);
    private readonly Button _export = Theme.MakeButton("Export PNG…", 96);

    private readonly PaintToolStrip _paint = new();

    private Bitmap? _image;
    private bool _loading;
    private bool _started;
    private string _listSignature = "";

    /// <summary>The stroke in flight, or null when the mouse is not down on the canvas.</summary>
    private PaintStroke? _stroke;

    /// <summary>
    /// The path of the stroke being drawn, in image pixels, and how wide it is there.
    ///
    /// Re-running the pipeline on every mouse move made painting unusable: one move meant a
    /// full generate — a distance transform among it, for the coast brush — so the picture
    /// lagged the cursor by whole seconds. The stroke is drawn straight onto the canvas as it
    /// happens and the pipeline runs once, when the button comes up.
    /// </summary>
    private readonly List<PointF> _strokePath = new();
    private float _strokeRadiusPx;
    private Color _strokeTint = Color.White;

    /// <summary>Base resolutions the pipeline can start from. Export size is this after any Upscale stage.</summary>
    private static readonly (string Label, int W, int H)[] Presets =
    [
        ("1024 × 512", 1024, 512),
        ("2048 × 1024", 2048, 1024),
        ("3072 × 1536", 3072, 1536),
        ("4096 × 2048", 4096, 2048),
        ("4608 × 2304  (quarter vanilla)", 4608, 2304),
        ("6144 × 3072", 6144, 3072),
        ("8192 × 4096", 8192, 4096),
        ("9216 × 4608  (half vanilla)", 9216, 4608),
        ("18432 × 9216  (CK3 vanilla)", 18432, 9216),
        ("Custom", 0, 0),
    ];

    /// <summary>Longest edge the preview generates at; 0 runs the pipeline at export size.</summary>
    private static readonly (string Label, int LongEdge)[] PreviewSizes =
    [
        ("512 px", 512),
        ("768 px", 768),
        ("1024 px", 1024),
        ("1536 px", 1536),
        ("2048 px", 2048),
        ("3072 px", 3072),
        ("4096 px", 4096),
        ("Full", 0),
    ];

    static ForgePanel()
    {
        // The shared parameter editor picks two colours itself; hand it ours.
        UiStyle.DimText = Theme.TextDim;
        UiStyle.GroupHeader = Theme.Accent;
    }

    public ForgePanel()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;

        _statusLine = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
            BackColor = Theme.Surface,
            ForeColor = Theme.TextDim,
            AutoEllipsis = true,
            Text = "Open this tab to generate a heightmap.",
        };

        _banner = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Notice,
            ForeColor = Theme.NoticeText,
            Visible = false,
        };

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            BackColor = Theme.Border,
            SplitterWidth = 4,
        };
        _split.Panel1.BackColor = Theme.Surface;
        _split.Panel2.BackColor = Theme.Background;

        _split.Panel1.Controls.Add(BuildLeftColumn());

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        right.Controls.Add(_canvas);
        right.Controls.Add(_banner);
        right.Controls.Add(_paint);
        right.Controls.Add(BuildStrip());
        right.Controls.Add(_statusLine);
        _split.Panel2.Controls.Add(right);

        WirePainting();

        Controls.Add(_split);
        _split.HandleCreated += (_, _) => _split.SplitterDistance = 372;

        _canvas.EmptyText = "The pipeline's preview appears here.";

        Session.Changed += OnSessionChanged;
        Session.PreviewReady += OnPreviewReady;
        Session.Status += (text, _) => _statusLine.Text = text;
        Session.Failed += (ex, what) =>
            MessageBox.Show(FindForm(), ex.ToString(), $"{what} failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>Width of the left column, for the host to persist and restore.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int LeftWidth
    {
        get => _split.SplitterDistance;
        set { if (value > 100) _split.SplitterDistance = value; }
    }

    /// <summary>
    /// Builds the default pipeline and starts previewing. Called when the tab is first shown rather
    /// than at startup, so a window whose user never opens it never pays for it.
    /// </summary>
    public void EnsureStarted()
    {
        if (_started) return;
        _started = true;
        Session.NewDefault();
        RefreshStageList();
        if (_stages.Items.Count > 0) _stages.SelectedIndex = 0;
        BindPainting();
    }

    public bool Started => _started;

    // ---------------------------------------------------------------- left column

    private Control BuildLeftColumn()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6, 6, 4, 6),
            BackColor = Theme.Surface,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 214f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        layout.Controls.Add(BuildProjectBox());
        layout.Controls.Add(BuildStageBox());

        var paramBox = new GroupBox
        {
            Text = "Stage settings",
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
            ForeColor = Theme.Text,
        };
        _params.Dock = DockStyle.Fill;
        _params.BackColor = Theme.Surface;
        paramBox.Controls.Add(_params);
        layout.Controls.Add(paramBox);

        return layout;
    }

    private Control BuildProjectBox()
    {
        var box = new GroupBox
        {
            Text = "Project",
            Dock = DockStyle.Top,
            Padding = new Padding(8, 4, 8, 8),
            ForeColor = Theme.Text,
        };

        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78f));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _resPreset.Items.AddRange(Presets.Select(p => (object)p.Label).ToArray());
        _resPreset.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            var p = Presets[_resPreset.SelectedIndex];
            if (p.W == 0) return;
            _loading = true;
            _baseWidth.Value = p.W;
            _baseHeight.Value = p.H;
            _loading = false;
            Session.SetBaseSize(p.W, p.H);
        };

        ConfigureSpin(_baseWidth, 256, 32768, 2048, 64);
        ConfigureSpin(_baseHeight, 256, 32768, 1024, 64);
        _baseWidth.ValueChanged += (_, _) => ApplySize();
        _baseHeight.ValueChanged += (_, _) => ApplySize();

        var size = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        _baseWidth.Width = 76;
        _baseHeight.Width = 76;
        size.Controls.Add(_baseWidth);
        size.Controls.Add(new Label { Text = "×", AutoSize = true, Margin = new Padding(4, 6, 4, 0) });
        size.Controls.Add(_baseHeight);

        _seed.ValueChanged += (_, _) => { if (!_loading) Session.SetSeed((int)_seed.Value); };
        var dice = Theme.MakeButton("\U0001F3B2", 32);
        dice.Click += (_, _) => _seed.Value = Random.Shared.Next(1, 1_000_000);
        _tips.SetToolTip(dice, "Randomise the master seed");
        var seedRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        seedRow.Controls.Add(_seed);
        seedRow.Controls.Add(dice);

        _loadPreset.Click += (_, _) => LoadPreset();
        _savePreset.Click += (_, _) => SavePreset();
        var presetRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        presetRow.Controls.Add(_loadPreset);
        presetRow.Controls.Add(_savePreset);

        AddRow(t, "Base size", _resPreset);
        AddRow(t, "", size);
        AddRow(t, "Seed", seedRow);
        AddRow(t, "Output", _resInfo);
        AddRow(t, "Preset", presetRow);

        // A GroupBox's AutoSize ignores a docked child and clipped the last row; size it by hand
        // and follow the table, which grows when the Output readout wraps to another line.
        box.Controls.Add(t);
        t.SizeChanged += (_, _) => box.Height = t.Height + 32;
        box.Height = t.PreferredSize.Height + 32;
        return box;
    }

    private void ApplySize()
    {
        if (_loading) return;
        int w = (int)_baseWidth.Value, h = (int)_baseHeight.Value;

        _loading = true;
        int match = Array.FindIndex(Presets, p => p.W == w && p.H == h);
        _resPreset.SelectedIndex = match >= 0 ? match : Presets.Length - 1;
        _loading = false;

        Session.SetBaseSize(w, h);
    }

    private Control BuildStageBox()
    {
        var box = new GroupBox
        {
            Text = "Pipeline",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 6),
            ForeColor = Theme.Text,
        };

        _stages.Dock = DockStyle.Fill;
        _stages.BackColor = Theme.Surface;
        _stages.ForeColor = Theme.Text;
        _stages.BorderStyle = BorderStyle.FixedSingle;
        _stages.SelectedIndexChanged += (_, _) =>
        {
            _params.Bind(SelectedStage());
            UpdateBakeButton();
            BindPainting();
        };
        _stages.ItemCheck += (_, e) =>
        {
            if (_loading) return;
            int index = e.Index;
            bool enabled = e.NewValue == CheckState.Checked;
            BeginInvoke(() =>
            {
                if (index >= 0 && index < Session.Pipeline.Stages.Count)
                    Session.SetStageEnabled(Session.Pipeline.Stages[index], enabled);
            });
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 0),
        };

        var add = Theme.MakeButton("Add ▾", 62);
        add.Click += (_, _) => ShowAddMenu(add);

        var remove = Theme.MakeButton("Remove", 62);
        remove.Click += (_, _) =>
        {
            if (SelectedStage() is { } s)
            {
                Session.RemoveStage(s);
                RefreshStageList();
            }
        };

        var up = Theme.MakeButton("▲", 30);
        up.Click += (_, _) => MoveSelected(-1);
        var down = Theme.MakeButton("▼", 30);
        down.Click += (_, _) => MoveSelected(1);

        var reset = Theme.MakeButton("Defaults", 66);
        reset.Click += (_, _) =>
        {
            if (SelectedStage() is { } s)
            {
                Session.ResetStage(s);
                _params.Bind(s);
            }
        };
        _tips.SetToolTip(reset, "Reset the selected stage's settings");

        _bake.Visible = false;
        _bake.Click += (_, _) => { if (SelectedStage() is { } s) _ = Session.BakeAsync(s); };

        buttons.Controls.AddRange([add, remove, up, down, reset, _bake]);

        box.Controls.Add(_stages);
        box.Controls.Add(buttons);
        return box;
    }

    private void ShowAddMenu(Control anchor)
    {
        var menu = Theme.MakeMenu();
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);

        foreach (var group in StageRegistry.All.GroupBy(d => d.Category))
        {
            var parent = new ToolStripMenuItem(group.Key);
            foreach (var descriptor in group)
            {
                var item = new ToolStripMenuItem(descriptor.Name);
                var captured = descriptor;
                item.Click += (_, _) =>
                {
                    Session.AddStage(captured);
                    RefreshStageList();
                    _stages.SelectedIndex = _stages.Items.Count - 1;
                };
                parent.DropDownItems.Add(item);
            }
            menu.Items.Add(parent);
        }

        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void MoveSelected(int delta)
    {
        int i = _stages.SelectedIndex;
        if (i < 0) return;
        Session.MoveStage(i, delta);
        RefreshStageList();
        _stages.SelectedIndex = Math.Clamp(i + delta, 0, _stages.Items.Count - 1);
    }

    private PipelineStage? SelectedStage()
    {
        int i = _stages.SelectedIndex;
        var stages = Session.Pipeline.Stages;
        return i >= 0 && i < stages.Count ? stages[i] : null;
    }

    private string ListSignature()
    {
        var pipeline = Session.Pipeline;
        return string.Join("|", pipeline.Stages.Select(s =>
            $"{s.GetType().Name}{(s.Enabled ? "+" : "-")}{(s.RequiresBake ? (pipeline.IsBakeValid(s) ? "b" : "n") : "")}"));
    }

    /// <summary>
    /// Rebuilds the list. Rebinds the parameter panel, which steals focus from a slider mid-drag,
    /// so callers that run on every change go through <see cref="SyncList"/> instead.
    /// </summary>
    private void RefreshStageList()
    {
        bool wasLoading = _loading;
        _loading = true;
        int keep = _stages.SelectedIndex;
        var pipeline = Session.Pipeline;

        _stages.Items.Clear();
        for (int i = 0; i < pipeline.Stages.Count; i++)
        {
            var s = pipeline.Stages[i];
            string tag = !s.RequiresBake ? ""
                : pipeline.IsBakeValid(s) ? "   [baked]"
                : "   [not baked]";

            // The brush marks a stage you can paint into, so the palette appearing on some
            // stages and not others is visible in the list rather than discovered by clicking.
            string brush = s is IPaintable ? "  ✎" : "";
            _stages.Items.Add($"{i + 1}.  {s.DisplayName}{brush}{tag}");
            _stages.SetItemCheckedProgrammatic(i, s.Enabled);
        }

        if (_stages.Items.Count > 0)
            _stages.SelectedIndex = Math.Clamp(keep, 0, _stages.Items.Count - 1);

        _listSignature = ListSignature();
        _loading = wasLoading;
        _params.Bind(SelectedStage());
        UpdateBakeButton();
    }

    /// <summary>Refreshes the list only when its rows would read differently.</summary>
    private void SyncList()
    {
        if (ListSignature() != _listSignature) RefreshStageList();
        else UpdateBakeButton();
    }

    private void UpdateBakeButton()
    {
        var stage = SelectedStage();
        bool applies = stage is { RequiresBake: true, Enabled: true };
        _bake.Visible = applies;
        if (!applies) return;

        bool valid = Session.Pipeline.IsBakeValid(stage!);
        _bake.Text = valid ? "Re-bake" : "Bake";
        _bake.Enabled = !Session.Baking && HydraulicErosionStage.GpuAvailable;
        _tips.SetToolTip(_bake, HydraulicErosionStage.GpuAvailable
            ? "Compute this stage at full resolution and cache the result"
            : "No Direct3D 12 GPU was found, so this stage cannot run here");
    }

    // --------------------------------------------------------------------- strip

    private Control BuildStrip()
    {
        // Wraps rather than clips: the tab shares the window with the settings pane, and at a
        // modest width the strip is two rows.
        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4, 3, 4, 3),
            WrapContents = true,
            BackColor = Theme.Surface,
        };

        _view.Items.AddRange(["Hypsometric + relief", "Greyscale (as exported)", "Land / sea mask"]);
        _view.SelectedIndex = 0;
        _view.SelectedIndexChanged += (_, _) => Session.SetView(_view.SelectedIndex switch
        {
            1 => RenderMode.Greyscale,
            2 => RenderMode.LandSeaMask,
            _ => RenderMode.Hypsometric,
        });

        _previewRes.Items.AddRange(PreviewSizes.Select(p => (object)p.Label).ToArray());
        _previewRes.SelectedIndex = 2;
        _previewRes.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            Session.SetPreviewLongEdge(PreviewSizes[_previewRes.SelectedIndex].LongEdge);
        };
        _tips.SetToolTip(_previewRes,
            "Longest edge the preview is generated at. Full runs the pipeline at export size, " +
            "Upscale stages included — slow, but exactly what the generator will get.");

        _auto.CheckedChanged += (_, _) =>
        {
            Session.AutoPreview = _auto.Checked;
            if (_auto.Checked) Session.QueuePreview();
        };
        _tips.SetToolTip(_auto, "Regenerate the preview automatically when anything changes");

        _generate.Click += (_, _) => _ = Session.RunPreviewAsync();
        _tips.SetToolTip(_generate, "Regenerate the preview now");

        var fit = Theme.MakeButton("Fit", 40);
        fit.Click += (_, _) => _canvas.Fit();

        _export.Click += (_, _) => _ = ExportAsync();
        _tips.SetToolTip(_export, "Run the pipeline at full size and write a 16-bit heightmap PNG");

        _use.Click += (_, _) => RequestUseForGeneration();
        _tips.SetToolTip(_use,
            "Build the mod from this pipeline instead of a PNG. Preview and Write mod on the " +
            "toolbar then run it at full size first, every time it has changed. At an export " +
            "size CK3 is not known to render, this asks before building anyway.");

        strip.Controls.Add(Caption("View"));
        strip.Controls.Add(_view);
        strip.Controls.Add(Caption("Preview"));
        strip.Controls.Add(_previewRes);
        strip.Controls.Add(_auto);
        strip.Controls.Add(_generate);
        strip.Controls.Add(fit);
        strip.Controls.Add(Separator());
        strip.Controls.Add(_export);
        strip.Controls.Add(Separator());
        strip.Controls.Add(_use);

        return strip;
    }

    // ------------------------------------------------------------------- session

    private void OnSessionChanged()
    {
        SyncProjectControls();
        SyncList();
    }

    private void SyncProjectControls()
    {
        var pipeline = Session.Pipeline;
        _loading = true;
        try
        {
            _baseWidth.Value = Math.Clamp(pipeline.BaseWidth, (int)_baseWidth.Minimum, (int)_baseWidth.Maximum);
            _baseHeight.Value = Math.Clamp(pipeline.BaseHeight, (int)_baseHeight.Minimum, (int)_baseHeight.Maximum);
            _seed.Value = pipeline.MasterSeed;

            int preset = Array.FindIndex(Presets, p => p.W == pipeline.BaseWidth && p.H == pipeline.BaseHeight);
            _resPreset.SelectedIndex = preset >= 0 ? preset : Presets.Length - 1;

            int previewIdx = Array.FindIndex(PreviewSizes, p => p.LongEdge == pipeline.PreviewLongEdge);
            if (previewIdx >= 0) _previewRes.SelectedIndex = previewIdx;
        }
        finally
        {
            _loading = false;
        }

        UpdateResInfo();
    }

    /// <summary>
    /// Whether the export size is one CK3 is known to render, so it builds without a question.
    /// Deferred to <see cref="MapGen.TileFit"/>
    /// rather than tested here, so a Forge export and a PNG loaded from disk are held to one rule.
    /// This checked the packer's 64 px tile directly and would have let through sizes the engine
    /// clips.
    /// </summary>
    public bool OutputBuildable
    {
        get
        {
            var (ow, oh) = Session.Pipeline.OutputSize();
            return MapGen.TileFit.Fits(ow, oh);
        }
    }

    /// <summary>
    /// Hands the pipeline to the generator — after one question, if its export size is not one
    /// CK3 is known to render. The button used to be disabled at such a size, which made
    /// <see cref="MapGen.TileFit.Known"/> impossible to grow from this tab: the only way to learn
    /// whether a size renders is to build it and look, and this is where sizes get chosen.
    ///
    /// Public because the main toolbar's heightmap menu offers the same hand-off, and it has to
    /// ask the same question rather than slip past it.
    /// </summary>
    public void RequestUseForGeneration()
    {
        var (ow, oh) = Session.Pipeline.OutputSize();
        bool unverified = !MapGen.TileFit.Fits(ow, oh);

        if (unverified)
        {
            var answer = MessageBox.Show(this,
                $"The export size is {ow} x {oh}, which is not one of the sizes CK3 is known to "
                + $"render correctly ({MapGen.TileFit.KnownList}).\n\n"
                + "At other sizes the engine has left terrain undrawn along the north and east "
                + "edges, in the map editor as much as in game. Nothing is logged when it happens.\n\n"
                + "Build at this size anyway, to test whether CK3 renders it? If it comes out clean, "
                + "the size can be added to the known list.",
                "This export size is not one CK3 is known to render",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;
        }

        UseForGeneration?.Invoke(unverified);
    }

    private void UpdateResInfo()
    {
        var pipeline = Session.Pipeline;
        int bw = pipeline.BaseWidth, bh = pipeline.BaseHeight;
        var (ow, oh) = pipeline.OutputSize();
        var (pw, ph) = pipeline.PreviewBaseSize();
        bool buildable = OutputBuildable;

        string chain = ow == bw && oh == bh ? "" : $"  ·  upscaled from {bw} × {bh}";
        string text = $"Export {ow} × {oh}  ({(double)ow * oh / 1e6:0.0} MP){chain}\nPreview {pw} × {ph}" +
                      $"  ·  sea level {Ck3.WaterLevel255}/255 (CK3's plane, fixed)";
        if (!buildable)
            text += $"\n⚠ not a size CK3 is known to render ({MapGen.TileFit.KnownList}); " +
                    "Use for generation will ask before building it anyway";

        _resInfo.Text = text;
        _resInfo.ForeColor = buildable ? Theme.TextDim : Theme.Danger;
    }

    private void OnPreviewReady(ForgePreview preview)
    {
        var old = _image;
        _image = preview.Image;

        // The banner is set before the bitmap goes in: the setter repaints, and a stale caption
        // over a fresh image is exactly the mismatch it exists to prevent.
        _banner.Text = "PREVIEW INCOMPLETE — a stage is not baked, so the export will not match this";
        _banner.Visible = preview.Incomplete;

        _canvas.SetImage(_image);
        old?.Dispose();   // after SetImage: the control may be mid-paint on the old one
    }

    // ---------------------------------------------------------- presets / export

    private void LoadPreset()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Load a Forge preset",
            Filter = PresetIO.FileFilter,
            InitialDirectory = PresetDir ?? "",
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var result = Session.LoadPreset(dialog.FileName);
            PresetDir = Path.GetDirectoryName(dialog.FileName);
            Session.History.Clear();
            RefreshStageList();
            if (_stages.Items.Count > 0) _stages.SelectedIndex = 0;
            BindPainting();

            if (result.Warnings.Count > 0)
                MessageBox.Show(FindForm(),
                    $"Loaded {result.StagesLoaded} stage(s), skipped {result.StagesSkipped}.\n\n"
                    + string.Join("\n", result.Warnings),
                    "Preset loaded with warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), ex.Message, "Could not load preset", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SavePreset()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save the Forge preset",
            Filter = PresetIO.FileFilter,
            InitialDirectory = PresetDir ?? "",
            FileName = Session.PresetPath is null ? "heightmap-preset.json" : Path.GetFileName(Session.PresetPath),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            Session.SavePreset(dialog.FileName);
            PresetDir = Path.GetDirectoryName(dialog.FileName);
            _statusLine.Text = $"Saved preset {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), ex.Message, "Could not save preset", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Asks before a run that will bake. Returns false if the user declined. Shared with the host,
    /// which has the same question to ask before a generation run.
    /// </summary>
    public bool ConfirmStaleBakes(IWin32Window owner)
    {
        var stale = Session.Pipeline.StaleBakes();
        if (stale.Count == 0) return true;

        var answer = MessageBox.Show(owner,
            "These stages are not baked and will be computed at full resolution first:\n\n" +
            string.Join("\n", stale.Select(s =>
            {
                var (w, h) = Session.Pipeline.ResolutionLeaving(s);
                return $"  • {s.DisplayName}  ({w} × {h})";
            })) +
            "\n\nThis may take a while. Continue?",
            "Bake required", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

        return answer == DialogResult.OK;
    }

    private async Task ExportAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export the heightmap",
            Filter = "16-bit PNG (*.png)|*.png",
            FileName = $"{Session.Name}-heightmap.png",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (!ConfirmStaleBakes(FindForm()!)) return;

        var written = await Session.ExportAsync(dialog.FileName);
        if (written is { } size)
            MessageBox.Show(FindForm(),
                $"Wrote {size.Width} × {size.Height} 16-bit greyscale PNG:\n{dialog.FileName}\n\n" +
                $"Waterline {Ck3.WaterLevel16} / 65535 — CK3's own plane.",
                "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------ painting

    private void WirePainting()
    {
        _canvas.StrokeBegan += OnStrokeBegan;
        _canvas.StrokeMoved += OnStrokeMoved;
        _canvas.StrokeEnded += OnStrokeEnded;
        _canvas.Overlay += DrawBrushCursor;

        _paint.BrushChanged += () => _canvas.Invalidate();
        _paint.UndoRequested += () => ApplyHistory(undo: true);
        _paint.RedoRequested += () => ApplyHistory(undo: false);
        _paint.ClearRequested += ClearChannel;
    }

    /// <summary>Shows or hides the palette for the selected stage, and switches the canvas between pan and paint.</summary>
    private void BindPainting()
    {
        var paintable = SelectedStage() as IPaintable;
        if (paintable is not null) Session.PrepareLayers(paintable);

        _paint.Bind(paintable, PaintableStageNames());
        _canvas.Mode = paintable is null ? ImageView.Interaction.Pan : ImageView.Interaction.Paint;
        _paint.SetHistoryState(Session.History.CanUndo, Session.History.CanRedo);
        _canvas.Invalidate();
    }

    /// <summary>The stages in this pipeline that can be painted, for the "nothing to paint here" hint.</summary>
    private string PaintableStageNames()
    {
        var names = Session.Pipeline.Stages
            .Where(s => s is IPaintable)
            .Select(s => s.DisplayName)
            .ToList();

        return names.Count == 0
            ? "Add Continents or Hand Paint (relief) from the Add menu to paint."
            : "Select " + string.Join(" or ", names) + " in the pipeline to paint.";
    }

    private void OnStrokeBegan(PointF imagePoint)
    {
        var channel = _paint.Channel;
        if (channel is null || Session.LastField is not { } field) return;

        var (nx, ny) = Normalise(imagePoint, field.Width, field.Height);

        // Flatten levels toward whatever was under the cursor when the stroke started, so the
        // height it aims at is picked from the map rather than typed in.
        float value = 0f;
        if (_paint.Tool == PaintTool.Flatten)
        {
            int px = Math.Clamp((int)imagePoint.X, 0, field.Width - 1);
            int py = Math.Clamp((int)imagePoint.Y, 0, field.Height - 1);
            value = field[px, py] * 2f - 1f;   // 0..1 height into the layer's -1..1
        }

        var targets = Painting.Plan(channel, _paint.Tool, value);
        if (targets.Count == 0) return;

        _stroke = new PaintStroke(targets, _paint.Brush);
        _stroke.Begin(nx, ny);

        _strokePath.Clear();
        _strokePath.Add(imagePoint);
        _strokeRadiusPx = _paint.RadiusNormalised * field.Height;
        _strokeTint = TintFor(_paint.Tool);
        _canvas.Invalidate();
    }

    /// <summary>What the live stroke is drawn in, so the gesture reads as the thing it will do.</summary>
    private static Color TintFor(PaintTool tool) => tool switch
    {
        PaintTool.Land => Color.FromArgb(150, 120, 190, 90),
        PaintTool.Sea => Color.FromArgb(150, 70, 130, 200),
        PaintTool.Raise => Color.FromArgb(140, 240, 235, 220),
        PaintTool.Lower => Color.FromArgb(140, 90, 70, 60),
        PaintTool.Smooth => Color.FromArgb(130, 190, 200, 230),
        PaintTool.Flatten => Color.FromArgb(130, 220, 200, 140),
        _ => Color.FromArgb(120, 200, 90, 90),
    };

    private void OnStrokeMoved(PointF imagePoint)
    {
        if (_stroke is null || Session.LastField is not { } field) return;

        var (nx, ny) = Normalise(imagePoint, field.Width, field.Height);
        _stroke.MoveTo(nx, ny);

        // Paint goes into the layer as the mouse moves; the picture of it is the overlay, and
        // the pipeline only re-runs when the stroke finishes.
        _strokePath.Add(imagePoint);
        _canvas.Invalidate();
    }

    private void OnStrokeEnded()
    {
        if (_stroke is null) return;

        var record = _stroke.End();
        _stroke = null;
        _strokePath.Clear();

        if (!record.Layers.Any()) { _canvas.Invalidate(); return; }

        Session.History.Push(record);
        _paint.SetHistoryState(Session.History.CanUndo, Session.History.CanRedo);

        // The one pipeline run per stroke. Immediate rather than debounced: the gesture is
        // over, so there is nothing left to coalesce with.
        Session.NotifyPainted();
        _ = Session.RunPreviewAsync();
    }

    private static (float X, float Y) Normalise(PointF imagePoint, int width, int height)
        => (Math.Clamp(imagePoint.X / width, 0f, 1f), Math.Clamp(imagePoint.Y / height, 0f, 1f));

    private void ApplyHistory(bool undo)
    {
        var changed = undo ? Session.History.Undo() : Session.History.Redo();
        if (changed.Count == 0) return;

        _paint.SetHistoryState(Session.History.CanUndo, Session.History.CanRedo);
        Session.NotifyPainted();
    }

    private void ClearChannel()
    {
        if (_paint.Channel is not { } channel) return;

        foreach (var layer in channel.Layers) layer.Clear();
        Session.History.Clear();
        _paint.SetHistoryState(false, false);
        Session.NotifyPainted();
    }

    /// <summary>The stroke as it is being drawn, plus the brush ring showing where the next dab lands.</summary>
    private void DrawBrushCursor(Graphics g, float zoom)
    {
        if (_canvas.Mode != ImageView.Interaction.Paint) return;
        if (Session.LastField is not { } field) return;

        // The stroke in progress, drawn where the paint is going. It disappears when the button
        // comes up and the regenerated terrain takes its place.
        if (_strokePath.Count > 0)
        {
            float width = MathF.Max(_strokeRadiusPx * 2f * zoom, 2f);
            using var pen = new Pen(_strokeTint, width)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
            };

            if (_strokePath.Count == 1)
            {
                var only = _canvas.ToControlPoint(_strokePath[0]);
                float r = width / 2f;
                using var fill = new SolidBrush(_strokeTint);
                g.FillEllipse(fill, only.X - r, only.Y - r, width, width);
            }
            else
            {
                var points = new PointF[_strokePath.Count];
                for (int i = 0; i < points.Length; i++) points[i] = _canvas.ToControlPoint(_strokePath[i]);
                g.DrawLines(pen, points);
            }
        }

        if (_canvas.CursorAt is not { } cursor) return;

        // The radius is a fraction of map height, which is the field's rows; on screen that is
        // scaled by the zoom like everything else.
        float radius = _paint.RadiusNormalised * field.Height * zoom;
        if (radius < 1.5f) radius = 1.5f;

        using var outer = new Pen(Color.FromArgb(200, 20, 20, 20), 1.6f);
        using var inner = new Pen(Color.FromArgb(210, 250, 250, 250), 1f);
        g.DrawEllipse(outer, cursor.X - radius, cursor.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(inner, cursor.X - radius + 1, cursor.Y - radius + 1, radius * 2 - 2, radius * 2 - 2);
    }

    /// <summary>Keys the tab handles when it is the one on screen. Returns true when it used the key.</summary>
    public bool HandleKey(Keys key)
    {
        switch (key)
        {
            case Keys.Oemtilde | Keys.Shift:
                return false;

            case Keys.OemOpenBrackets:
                _paint.NudgeRadius(-4);
                return _paint.Visible;

            case Keys.OemCloseBrackets:
                _paint.NudgeRadius(+4);
                return _paint.Visible;

            case Keys.Control | Keys.Z when _paint.Visible:
                ApplyHistory(undo: true);
                return true;

            case Keys.Control | Keys.Y when _paint.Visible:
                ApplyHistory(undo: false);
                return true;

            case Keys.F5:
                _ = Session.RunPreviewAsync();
                return true;

            default:
                return false;
        }
    }

    // ------------------------------------------------------------------- helpers

    private static void ConfigureSpin(NumericUpDown n, decimal min, decimal max, decimal value, decimal step)
    {
        n.Minimum = min;
        n.Maximum = max;
        n.Increment = step;
        n.Value = value;
        n.ThousandsSeparator = true;
    }

    private static void AddRow(TableLayoutPanel t, string label, Control editor)
    {
        t.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(2, 7, 4, 4),
            ForeColor = Theme.Text,
        });
        editor.Margin = new Padding(2, 3, 2, 3);
        t.Controls.Add(editor);
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.TextDim,
        Margin = new Padding(6, 7, 3, 0),
    };

    private static Control Separator() => new Panel
    {
        Width = 1,
        Height = 22,
        BackColor = Theme.Border,
        Margin = new Padding(6, 3, 6, 0),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Session.Dispose();
            _image?.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
