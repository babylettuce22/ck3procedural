using System.ComponentModel;
using System.Drawing.Imaging;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.Gui;

/// <summary>
/// Two tabs, because the tool does two separable jobs and conflating them was confusing:
/// <b>Terrain</b> makes a heightmap, <b>Mod</b> turns a heightmap into a CK3 mod. The second does
/// not care whether the first produced its input — a heightmap painted in any other program is
/// just as good, which is the whole point of <see cref="MapGen.TerrainData"/> being the seam.
///
/// The tab split is not cosmetic: it is <see cref="SettingRole"/> made visible. Every setting is
/// already marked as either GenerationOnly (consumed while building terrain, inert once terrain
/// comes from a file) or Always (applies to any heightmap at all), and that line is exactly the
/// line between these two tabs. Each grid is filtered to its own half rather than showing all of
/// them and greying some out.
///
/// The whole reason this is WinForms is <see cref="PropertyGrid"/>: pointing it at
/// <c>MapConfig</c> yields an editable, categorised editor for every setting with no per-parameter
/// UI code, which is what makes the terrain tunable without an edit-rebuild-run cycle. That is
/// also why MapConfig's fields became auto-properties — the grid reflects over properties only.
///
/// Work runs on a worker thread. It takes seconds at <c>tiny</c> and minutes at <c>vanilla</c>, so
/// doing it on the UI thread would freeze the window for the whole run.
/// </summary>
public sealed class MainForm : Form
{
    private readonly GenerationOptions _options;

    // --- Terrain tab ---
    private readonly PropertyGrid _terrainGrid = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly NumericUpDown _seed = new() { Minimum = 0, Maximum = int.MaxValue, Width = 90 };
    private readonly Button _generate = new() { Text = "Generate terrain", Width = 130 };
    private readonly TabControl _terrainViews = new() { Dock = DockStyle.Fill };

    // --- Mod tab ---
    private readonly PropertyGrid _modGrid = new() { Dock = DockStyle.Fill };
    private readonly RadioButton _sourceGenerated =
        new() { Text = "Terrain from the Terrain tab", AutoSize = true, Checked = true, Padding = new Padding(0, 5, 8, 0) };
    private readonly RadioButton _sourceFile =
        new() { Text = "Heightmap file:", AutoSize = true, Padding = new Padding(8, 5, 4, 0) };
    private readonly Button _browse = new() { Text = "Browse…", Width = 80 };
    private readonly Label _sourceName =
        new() { AutoSize = true, Padding = new Padding(6, 7, 0, 0), ForeColor = Color.DimGray };
    private readonly Button _preview = new() { Text = "Preview", Width = 80 };
    private readonly Button _writeMod = new() { Text = "Write mod", Width = 100 };
    private readonly TabControl _modViews = new() { Dock = DockStyle.Fill };

    // --- shared ---
    private readonly TabControl _mode = new() { Dock = DockStyle.Fill };
    private readonly TextBox _log =
        new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly ToolStripStatusLabel _status = new() { Text = "Ready" };

    /// <summary>The last terrain the Terrain tab produced, and the Mod tab's default input.</summary>
    private GenerationResult? _terrain;

    /// <summary>
    /// The last heightmap loaded from disk, kept so previewing a settings change does not re-read
    /// and re-derive the image every time. Cleared whenever the chosen file changes.
    /// </summary>
    private MapGen.TerrainData? _loaded;
    private string? _loadedFrom;
    private string? _heightmapPath;
    private bool _busy;

    public MainForm(GenerationOptions options)
    {
        _options = options;

        Text = "CK3 Procedural Map";
        Width = 1500;
        Height = 950;
        StartPosition = FormStartPosition.CenterScreen;

        // Each grid shows only the settings its own tab is about. BrowsableAttributes filters
        // natively on the marker attribute, so there is no per-property wiring here.
        Configure(_terrainGrid, SettingRole.GenerationOnly);
        Configure(_modGrid, SettingRole.Always);

        _preset.Items.AddRange(MapPreset.Names);
        _preset.SelectedItem = MapPreset.Match(_options.Config) ?? "small";
        _preset.SelectedIndexChanged += (_, _) =>
        {
            MapPreset.Apply((string)_preset.SelectedItem!, _options.Config);
            _terrainGrid.Refresh();
        };

        _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
        _seed.ValueChanged += (_, _) => _options.Config.Seed = (int)_seed.Value;

        _generate.Click += async (_, _) => await GenerateTerrainAsync();
        _preview.Click += async (_, _) => await BuildAsync(null);
        _writeMod.Click += async (_, _) => await BuildAsync(GenerationOptions.DefaultModDir);
        _browse.Click += (_, _) => PickHeightmap();
        _sourceGenerated.CheckedChanged += (_, _) => ApplySource();
        _sourceFile.CheckedChanged += (_, _) => ApplySource();

        _mode.TabPages.Add(BuildTerrainTab());
        _mode.TabPages.Add(BuildModTab());

        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 660,
        };
        body.Panel1.Controls.Add(_mode);
        body.Panel2.Controls.Add(_log);

        var status = new StatusStrip();
        status.Items.Add(_status);

        Controls.Add(body);
        Controls.Add(status);

        ApplySource();

        // Everything in the generator reports progress with Console.WriteLine. Redirecting the
        // console is what lets all of that reach the log pane without touching a single call site.
        Console.SetOut(new TextBoxWriter(_log));
    }

    private static void Configure(PropertyGrid grid, SettingRole role)
    {
        grid.PropertySort = PropertySort.Categorized;
        grid.HelpVisible = true;
        grid.BrowsableAttributes = new AttributeCollection(new SettingRoleAttribute(role));
    }

    private TabPage BuildTerrainTab()
    {
        _terrainGrid.SelectedObject = _options.Config;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4) };
        bar.Controls.Add(new Label { Text = "Size", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        bar.Controls.Add(_preset);
        bar.Controls.Add(new Label { Text = "Seed", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        bar.Controls.Add(_seed);
        bar.Controls.Add(_generate);

        return BuildTab("1 · Terrain", bar, _terrainGrid, _terrainViews);
    }

    private TabPage BuildModTab()
    {
        _modGrid.SelectedObject = _options.Config;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4) };
        bar.Controls.Add(_sourceGenerated);
        bar.Controls.Add(_sourceFile);
        bar.Controls.Add(_browse);
        bar.Controls.Add(_preview);
        bar.Controls.Add(_writeMod);
        bar.Controls.Add(_sourceName);

        return BuildTab("2 · Mod", bar, _modGrid, _modViews);
    }

    /// <summary>Settings on the left with their toolbar above them, previews on the right.</summary>
    private static TabPage BuildTab(string title, Control bar, Control grid, Control views)
    {
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(grid);
        left.Controls.Add(bar);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 430 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(views);

        var page = new TabPage(title);
        page.Controls.Add(split);
        return page;
    }

    private void PickHeightmap()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Build the mod around an existing heightmap",
            Filter = "Heightmap PNG (*.png)|*.png|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _heightmapPath = dialog.FileName;
        if (_heightmapPath != _loadedFrom) { _loaded = null; _loadedFrom = null; }
        _sourceFile.Checked = true;
        ApplySource();
    }

    private void ApplySource()
    {
        bool fromFile = _sourceFile.Checked;

        _sourceGenerated.Enabled = true;
        _browse.Enabled = true;
        _sourceName.Text = fromFile
            ? _heightmapPath is null ? "(no file chosen)" : Path.GetFileName(_heightmapPath)
            : _terrain is null ? "(nothing generated yet)" : $"{_options.Config.Width}x{_options.Config.Height}";

        bool ready = fromFile ? _heightmapPath is not null : _terrain is not null;
        _writeMod.Enabled = !_busy && ready;
        _preview.Enabled = !_busy && ready;
    }

    private async Task GenerateTerrainAsync()
    {
        _options.HeightmapPath = null;
        var result = await RunAsync("Generating terrain…",
            () => Generator.Generate(_options));
        if (result is null) return;

        _terrain = result;
        ShowPreviews(_terrainViews, result, terrainOnly: true);
        _status.Text = $"Terrain generated — {_options.Config.Width}x{_options.Config.Height}. " +
                       "Switch to the Mod tab to build the mod from it.";
        ApplySource();
    }

    /// <summary>
    /// Recomputes everything the mod is made of — moisture, the land mask, provinces, the terrain
    /// classification — and optionally writes it out.
    ///
    /// Terrain is never regenerated here, whichever source is selected: the Terrain tab's result is
    /// already in memory and a file is read once and cached. That is the whole point of the
    /// preview being useful — none of the settings on this tab affect terrain, so paying for
    /// terrain again to see them would be paying for the slow half to look at the fast one.
    /// </summary>
    private async Task BuildAsync(string? modDir)
    {
        bool fromFile = _sourceFile.Checked;

        var result = await RunAsync(
            modDir is null ? "Building preview…" : "Writing mod…",
            () =>
            {
                var cfg = _options.Config;

                MapGen.TerrainData terra;
                if (fromFile)
                {
                    if (_loaded is null || _loadedFrom != _heightmapPath)
                    {
                        _loaded = MapGen.HeightmapSource.Load(_heightmapPath!, cfg, new Rng(cfg.Seed));
                        _loadedFrom = _heightmapPath;
                    }
                    terra = _loaded;
                }
                else
                {
                    terra = _terrain!.Terra;
                }

                var r = Generator.FromTerrain(terra, cfg);
                if (modDir is not null) Generator.WriteMod(r, _options, modDir);
                return r;
            });

        if (result is null) return;

        ShowPreviews(_modViews, result, terrainOnly: false);
        _status.Text = modDir is null
            ? $"Preview — {result.Provinces.Count} provinces. Nothing written."
            : $"Mod written to {modDir} — {result.Provinces.Count} provinces";
    }

    /// <summary>Runs work off the UI thread, with the buttons locked and failures sent to the log.</summary>
    private async Task<GenerationResult?> RunAsync(string message, Func<GenerationResult> work)
    {
        if (_busy) return null;
        _busy = true;
        SetEnabled(false);
        _log.Clear();
        _status.Text = message;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(work);
            Console.WriteLine();
            Console.WriteLine($"Finished in {clock.ElapsedMilliseconds / 1000.0:F1} s");
            return result;
        }
        catch (Exception ex)
        {
            // A failure must not take the window with it; the message is far more useful sitting
            // in the log next to the parameters that caused it.
            Console.WriteLine();
            Console.WriteLine(ex);
            _status.Text = "Failed — see log";
            return null;
        }
        finally
        {
            _busy = false;
            SetEnabled(true);
        }
    }

    private void SetEnabled(bool enabled)
    {
        _generate.Enabled = enabled;
        _terrainGrid.Enabled = enabled;
        _modGrid.Enabled = enabled;
        _preset.Enabled = enabled;
        _seed.Enabled = enabled;
        _sourceGenerated.Enabled = enabled;
        _sourceFile.Enabled = enabled;
        _browse.Enabled = enabled;

        bool ready = _sourceFile.Checked ? _heightmapPath is not null : _terrain is not null;
        _writeMod.Enabled = enabled && ready;
        _preview.Enabled = enabled && ready;
    }

    /// <summary>
    /// Terrain views on the Terrain tab, everything the mod is built from on the Mod tab, so each
    /// tab shows what it is responsible for rather than both showing all six.
    /// </summary>
    private void ShowPreviews(TabControl views, GenerationResult result, bool terrainOnly)
    {
        string? selected = views.SelectedTab?.Text;

        // Dispose before dropping the pages. Preview is meant to be clicked repeatedly while a
        // setting is tuned, and a Bitmap is unmanaged memory the collector is in no hurry about —
        // leaking one per view per click adds up over a tuning session.
        foreach (TabPage page in views.TabPages)
            foreach (Control control in page.Controls)
                if (control is PictureBox { Image: { } image }) image.Dispose();

        views.TabPages.Clear();

        // Guarded on Preview, not on Terra: an imported heightmap gives a perfectly good
        // TerrainData whose Preview is null, because there is no coarse world behind it.
        if (terrainOnly && result.Terra.Preview is { } preview)
        {
            AddView(views, "Relief", TerraPreview.RenderRelief(preview));
            AddView(views, "Rivers", TerraPreview.RenderRivers(preview));
            AddView(views, "Moisture", TerraPreview.RenderMoisture(preview));
        }

        AddView(views, "Height", PreviewRenderer.RenderElevation(result));

        if (!terrainOnly)
        {
            AddView(views, "Terrain", PreviewRenderer.RenderTerrain(result));
            AddView(views, "Provinces", PreviewRenderer.RenderProvinces(result));
        }

        foreach (TabPage page in views.TabPages)
            if (page.Text == selected) { views.SelectedTab = page; break; }
    }

    private static void AddView(TabControl views, string name, TerraPreview.Image image)
    {
        var box = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(24, 26, 30),
            Image = ToBitmap(image),
        };
        var page = new TabPage(name);
        page.Controls.Add(box);
        views.TabPages.Add(page);
    }

    /// <summary>Packed RGB to a 24bpp bitmap, one row at a time to respect the stride.</summary>
    private static Bitmap ToBitmap(TerraPreview.Image image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, image.Width, image.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            var row = new byte[image.Width * 3];
            for (int y = 0; y < image.Height; y++)
            {
                // Bitmap wants BGR; the renderers produce RGB.
                int src = y * image.Width * 3;
                for (int x = 0; x < image.Width; x++)
                {
                    row[x * 3 + 0] = image.Rgb[src + x * 3 + 2];
                    row[x * 3 + 1] = image.Rgb[src + x * 3 + 1];
                    row[x * 3 + 2] = image.Rgb[src + x * 3 + 0];
                }
                System.Runtime.InteropServices.Marshal.Copy(
                    row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>Routes Console output into the log pane, marshalling back onto the UI thread.</summary>
    private sealed class TextBoxWriter(TextBox target) : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value) => Write(value.ToString());

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value) || target.IsDisposed) return;

            if (target.InvokeRequired) target.BeginInvoke(() => Append(value));
            else Append(value);
        }

        public override void WriteLine(string? value) => Write((value ?? string.Empty) + "\r\n");

        private void Append(string value)
        {
            if (target.IsDisposed) return;
            target.AppendText(value.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }
    }
}
