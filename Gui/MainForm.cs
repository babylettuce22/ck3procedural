using System.ComponentModel;
using System.Drawing.Imaging;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.Gui;

/// <summary>
/// One window: choose a heightmap, tune the settings, look at what they produce, write the mod.
///
/// There used to be a second tab that generated terrain. Heightmaps are now made elsewhere, so
/// every setting applies to every run and there is nothing left to split the window along.
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

    private readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _seed = new() { Minimum = 0, Maximum = int.MaxValue, Width = 90 };
    private readonly Button _browse = new() { Text = "Heightmap…", Width = 100 };
    private readonly Label _sourceName =
        new() { AutoSize = true, Padding = new Padding(6, 7, 0, 0), ForeColor = Color.DimGray };
    private readonly Button _preview = new() { Text = "Preview", Width = 80 };
    private readonly Button _writeMod = new() { Text = "Write mod", Width = 100 };
    private readonly TabControl _views = new() { Dock = DockStyle.Fill };

    private readonly TextBox _log =
        new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly ToolStripStatusLabel _status = new() { Text = "Ready" };

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

        _grid.PropertySort = PropertySort.Categorized;
        _grid.HelpVisible = true;
        _grid.SelectedObject = _options.Config;

        _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
        _seed.ValueChanged += (_, _) => _options.Config.Seed = (int)_seed.Value;

        _preview.Click += async (_, _) => await BuildAsync(null);
        _writeMod.Click += async (_, _) => await BuildAsync(GenerationOptions.DefaultModDir);
        _browse.Click += (_, _) => PickHeightmap();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4) };
        bar.Controls.Add(_browse);
        bar.Controls.Add(new Label { Text = "Seed", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        bar.Controls.Add(_seed);
        bar.Controls.Add(_preview);
        bar.Controls.Add(_writeMod);
        bar.Controls.Add(_sourceName);

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_grid);
        left.Controls.Add(bar);

        var main = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 430 };
        main.Panel1.Controls.Add(left);
        main.Panel2.Controls.Add(_views);

        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 660,
        };
        body.Panel1.Controls.Add(main);
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

    private void PickHeightmap()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Build the mod around a heightmap",
            Filter = "Heightmap PNG (*.png)|*.png|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _heightmapPath = dialog.FileName;
        if (_heightmapPath != _loadedFrom) { _loaded = null; _loadedFrom = null; }
        ApplySource();
    }

    private void ApplySource()
    {
        _sourceName.Text = _heightmapPath is null
            ? "(no heightmap chosen)"
            : Path.GetFileName(_heightmapPath);

        bool ready = !_busy && _heightmapPath is not null;
        _writeMod.Enabled = ready;
        _preview.Enabled = ready;
    }

    /// <summary>
    /// Derives everything the mod is made of — moisture, the land mask, provinces, the terrain
    /// classification, cultures, faiths and titles — and optionally writes it out.
    ///
    /// The heightmap is read once and cached, so tuning a setting and previewing again pays only
    /// for the deriving. That is what makes the preview worth using: none of these settings change
    /// the heightmap, so re-reading and re-draining the image every time would be paying the slow
    /// cost to look at the fast one.
    /// </summary>
    private async Task BuildAsync(string? modDir)
    {
        var result = await RunAsync(
            modDir is null ? "Building preview…" : "Writing mod…",
            () =>
            {
                var cfg = _options.Config;

                if (_loaded is null || _loadedFrom != _heightmapPath)
                {
                    _loaded = MapGen.HeightmapSource.Load(_heightmapPath!, cfg, new Rng(cfg.Seed));
                    _loadedFrom = _heightmapPath;
                }

                var r = Generator.FromTerrain(_loaded, cfg);
                if (modDir is not null) Generator.WriteMod(r, _options, modDir);
                return r;
            });

        if (result is null) return;

        ShowPreviews(result);
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
        _grid.Enabled = enabled;
        _seed.Enabled = enabled;
        _browse.Enabled = enabled;

        bool ready = enabled && _heightmapPath is not null;
        _writeMod.Enabled = ready;
        _preview.Enabled = ready;
    }

    /// <summary>Rebuilds the view tabs, keeping whichever one was open.</summary>
    private void ShowPreviews(GenerationResult result)
    {
        string? selected = _views.SelectedTab?.Text;

        // Dispose before dropping the pages. Preview is meant to be clicked repeatedly while a
        // setting is tuned, and a Bitmap is unmanaged memory the collector is in no hurry about —
        // leaking one per view per click adds up over a tuning session.
        foreach (TabPage page in _views.TabPages)
            foreach (Control control in page.Controls)
                if (control is PictureBox { Image: { } image }) image.Dispose();

        _views.TabPages.Clear();

        AddView(_views, "Height", PreviewRenderer.RenderElevation(result));
        AddView(_views, "Terrain", PreviewRenderer.RenderTerrain(result));
        AddView(_views, "Provinces", PreviewRenderer.RenderProvinces(result));

        foreach (TabPage page in _views.TabPages)
            if (page.Text == selected) { _views.SelectedTab = page; break; }
    }

    private static void AddView(TabControl views, string name, PreviewRenderer.Image image)
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
    private static Bitmap ToBitmap(PreviewRenderer.Image image)
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
