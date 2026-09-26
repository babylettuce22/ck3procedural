namespace Ck3MapGen.AppGUI;

/// <summary>The three things the window can be doing, in the order a world is made.</summary>
internal enum Workspace { Terrain, Climate, World }

/// <summary>
/// The window's top level: one row naming the workspaces, with the commands that act on the
/// finished mod at the far end.
///
/// It replaced a tab strip that sat <em>inside</em> the generator's right-hand pane, under its
/// settings sidebar and above its log. That placement gave every tab a frame only one of them used
/// — the Heightmap and Climate tabs each brought their own sidebar, so the window showed three
/// columns of controls, two seed boxes and two preset pairs at once, and their canvases got a fifth
/// of the window. Up here each workspace owns everything below the bar.
///
/// Drawn as underlined labels rather than filled buttons on purpose: every selector below it (the
/// view switch, the map categories, the map modes) is some kind of filled button, and the top level
/// has to be told apart from them at a glance.
/// </summary>
internal sealed class WorkspaceBar : Panel
{
    private readonly Dictionary<Workspace, Item> _items = [];
    private readonly List<Label> _chevrons = [];
    private readonly FlowLayoutPanel _leading;
    private bool _single;

    public WorkspaceBar()
    {
        Dock = DockStyle.Top;
        Height = 42;
        BackColor = Theme.Surface;

        _leading = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = Color.Transparent,
        };

        Trailing = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 6, 8, 0),
            BackColor = Color.Transparent,
        };

        Add(Workspace.Terrain, 1, "Terrain", "beta", "Build a heightmap from noise and paint (Ctrl+1)");
        AddChevron();
        Add(Workspace.Climate, 2, "Climate", null, "Paint the climate over the heightmap (Ctrl+2)");
        AddChevron();
        Add(Workspace.World, 3, "World", null, "Settings, preview and the finished map (Ctrl+3)");

        Controls.Add(_leading);
        Controls.Add(Trailing);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
    }

    /// <summary>Where the host puts the commands that belong to no one workspace.</summary>
    public FlowLayoutPanel Trailing { get; }

    public Workspace Current { get; private set; } = Workspace.World;

    /// <summary>Raised by a click on a workspace other than the current one.</summary>
    public event Action<Workspace>? Picked;

    public void SetCurrent(Workspace workspace)
    {
        Current = workspace;
        foreach (var (key, item) in _items) item.Selected = key == workspace;
    }

    public void SetLabel(Workspace workspace, string label) => _items[workspace].Label = label;

    /// <summary>
    /// One workspace only — an opened mod, which is edited rather than generated. The step numbers
    /// and chevrons describe a pipeline, and with a single stop there is none to describe.
    /// </summary>
    public void SetSingle(bool single)
    {
        _single = single;
        _items[Workspace.Terrain].Visible = !single;
        _items[Workspace.Climate].Visible = !single;
        foreach (var chevron in _chevrons) chevron.Visible = !single;
        foreach (var item in _items.Values) item.ShowStep = !single;
    }

    public bool IsAvailable(Workspace workspace) => !_single || workspace == Workspace.World;

    private void Add(Workspace workspace, int step, string label, string? badge, string tip)
    {
        var item = new Item(step, label, badge);
        item.Click += (_, _) => { if (workspace != Current) Picked?.Invoke(workspace); };
        new ToolTip { InitialDelay = 500 }.SetToolTip(item, tip);
        _items[workspace] = item;
        _leading.Controls.Add(item);
    }

    private void AddChevron()
    {
        var chevron = new Label
        {
            Text = "›",
            AutoSize = true,
            Font = new Font("Segoe UI", 12f),
            ForeColor = Color.FromArgb(170, 176, 186),
            Margin = new Padding(0, 8, 0, 0),
        };
        _chevrons.Add(chevron);
        _leading.Controls.Add(chevron);
    }

    /// <summary>
    /// One workspace label: a dim step number, the name, an optional badge, and an accent underline
    /// when selected. Painted by hand because a Button cannot draw text in two colours.
    /// </summary>
    private sealed class Item : Control
    {
        private static readonly Font NameFont = new("Segoe UI", 10f);
        private static readonly Font NameBold = new("Segoe UI", 10f, FontStyle.Bold);
        private static readonly Font BadgeFont = new("Segoe UI", 7.5f);

        private readonly int _step;
        private readonly string? _badge;
        private string _label;
        private bool _selected;
        private bool _hover;
        private bool _showStep = true;

        public Item(int step, string label, string? badge)
        {
            _step = step;
            _label = label;
            _badge = badge;
            Height = 41;
            Margin = new Padding(0);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Measure();
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Label { get => _label; set { if (_label == value) return; _label = value; Measure(); Invalidate(); } }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Measure(); Invalidate(); } }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowStep { get => _showStep; set { if (_showStep == value) return; _showStep = value; Measure(); Invalidate(); } }

        private string StepText => _showStep ? _step.ToString() : "";

        /// <summary>Sized to its text, always at the bold width, so selecting it never shifts its neighbours.</summary>
        private void Measure()
        {
            const TextFormatFlags flags = TextFormatFlags.NoPadding;
            int width = 14;
            if (_showStep) width += TextRenderer.MeasureText(StepText, NameFont, Size.Empty, flags).Width + 6;
            width += TextRenderer.MeasureText(_label, NameBold, Size.Empty, flags).Width;
            if (_badge is not null) width += TextRenderer.MeasureText(_badge, BadgeFont, Size.Empty, flags).Width + 14;
            Width = width + 14;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_hover && !_selected ? Theme.Background : Theme.Surface);

            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
            var area = new Rectangle(14, 0, Width - 14, Height - 3);
            int x = area.X;

            if (_showStep)
            {
                var size = TextRenderer.MeasureText(StepText, NameFont, Size.Empty, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, StepText, NameFont, new Rectangle(x, area.Y, size.Width, area.Height),
                    _selected ? Theme.Accent : Theme.TextDim, flags);
                x += size.Width + 6;
            }

            var font = _selected ? NameBold : NameFont;
            var nameSize = TextRenderer.MeasureText(_label, font, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _label, font, new Rectangle(x, area.Y, nameSize.Width, area.Height),
                _selected ? Theme.Accent : Theme.Text, flags);
            x += nameSize.Width + 7;

            if (_badge is not null)
            {
                var badgeSize = TextRenderer.MeasureText(_badge, BadgeFont, Size.Empty, TextFormatFlags.NoPadding);
                var pill = new Rectangle(x, (area.Height - badgeSize.Height) / 2 - 1, badgeSize.Width + 8, badgeSize.Height + 2);
                using (var back = new SolidBrush(Theme.Notice)) g.FillRectangle(back, pill);
                TextRenderer.DrawText(g, _badge, BadgeFont, pill, Theme.NoticeText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            if (_selected)
            {
                using var line = new SolidBrush(Theme.Accent);
                g.FillRectangle(line, 8, Height - 3, Width - 16, 3);
            }
        }
    }
}
