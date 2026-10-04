namespace Ck3MapGen.AppGUI;

/// <summary>
/// The Masks workspace: the maps painted over the heightmap before a run. One page at a time,
/// picked from a strip along the top — <see cref="ClimatePanel"/> and <see cref="ImpassablePanel"/>.
/// Both are given the same terrain by the host; each keeps its own paint, undo and files.
/// </summary>
public sealed class MasksPanel : UserControl
{
    public enum Page { Climate, Impassable }

    private readonly Dictionary<Page, Control> _pages;
    private readonly Dictionary<Page, Button> _buttons = [];

    public ClimatePanel Climate { get; }
    public ImpassablePanel Impassable { get; }

    public Page Current { get; private set; } = Page.Climate;

    /// <summary>The page on screen changed.</summary>
    public event Action<Page>? PageChanged;

    public MasksPanel(ClimatePanel climate, ImpassablePanel impassable)
    {
        Climate = climate;
        Impassable = impassable;
        BackColor = Theme.Background;
        _pages = new() { [Page.Climate] = climate, [Page.Impassable] = impassable };

        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Dpi.S(34),
            Padding = Dpi.Pad(6, 4, 4, 0),
            BackColor = Theme.Surface,
            WrapContents = false,
        };

        var tips = new WrappingToolTip { InitialDelay = 400 };
        foreach (var (page, label, tip) in new[]
        {
            (Page.Climate, "Climate", "Paint the climate over the heightmap"),
            (Page.Impassable, "Impassable", "Paint where the impassable mountains go"),
        })
        {
            var button = Theme.MakeButton(label, 96);
            var captured = page;
            button.Click += (_, _) => Select(captured);
            tips.SetToolTip(button, tip);
            strip.Controls.Add(button);
            _buttons[page] = button;
        }
        Disposed += (_, _) => tips.Dispose();

        climate.Dock = DockStyle.Fill;
        impassable.Dock = DockStyle.Fill;
        Controls.Add(climate);
        Controls.Add(impassable);
        // Docked last-added first, so the strip sits on top and the rule under it.
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(strip);

        Select(Page.Climate, force: true);
    }

    public void Select(Page page, bool force = false)
    {
        if (page == Current && !force) return;
        Current = page;

        foreach (var (key, control) in _pages) control.Visible = key == page;
        foreach (var (key, button) in _buttons)
        {
            bool active = key == page;
            button.BackColor = active ? Theme.Accent : Theme.Surface;
            button.ForeColor = active ? Theme.AccentText : Theme.Text;
            button.FlatAppearance.BorderColor = active ? Theme.Accent : Theme.Border;
        }

        if (force) return;
        // A hidden page skips its previews; catch it up.
        if (page == Page.Climate) Climate.Activated();
        else Impassable.Activated();
        PageChanged?.Invoke(page);
    }

    /// <summary>Brush keys go to the page on screen.</summary>
    public bool HandleKey(Keys key) => Current == Page.Climate ? Climate.HandleKey(key) : Impassable.HandleKey(key);

    /// <summary>The workspace came back on screen with the terrain it already had.</summary>
    public void Activated()
    {
        Climate.Activated();
        Impassable.Activated();
    }

    public void SetTerrain(ClimatePanel.Terrain? terrain)
    {
        Climate.SetTerrain(terrain);
        Impassable.SetTerrain(terrain);
    }

    public void InvalidateModel()
    {
        Climate.InvalidateModel();
        Impassable.InvalidateModel();
    }
}
