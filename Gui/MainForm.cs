using System.ComponentModel;
using System.Drawing.Imaging;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.Gui;

/// <summary>
/// Parameter panel on the left, preview on the right.
///
/// The whole reason this is WinForms is <see cref="PropertyGrid"/>: pointing it at
/// <c>MapConfig</c> yields an editable, categorised editor for all sixty-five settings with no
/// per-parameter UI code, which is what makes the terrain tunable without an edit-rebuild-run
/// cycle. That is also why MapConfig's fields became auto-properties — the grid reflects over
/// properties only and would otherwise show an empty panel.
///
/// Generation runs on a worker thread. It takes seconds at <c>tiny</c> and minutes at
/// <c>vanilla</c>, so doing it on the UI thread would freeze the window for the whole run.
/// </summary>
public sealed class MainForm : Form
{
    private readonly GenerationOptions _options;

    private readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly NumericUpDown _seed = new() { Minimum = 0, Maximum = int.MaxValue, Width = 90 };
    private readonly Button _generate = new() { Text = "Generate", Width = 90 };
    private readonly Button _writeMod = new() { Text = "Write mod", Width = 90 };
    private readonly TabControl _views = new() { Dock = DockStyle.Fill };
    private readonly TextBox _log =
        new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly ToolStripStatusLabel _status = new() { Text = "Ready" };

    private bool _busy;

    public MainForm(GenerationOptions options)
    {
        _options = options;

        Text = "CK3 Procedural Map";
        Width = 1500;
        Height = 950;
        StartPosition = FormStartPosition.CenterScreen;

        _grid.SelectedObject = _options.Config;
        _grid.PropertySort = PropertySort.Categorized;
        _grid.HelpVisible = true;

        _preset.Items.AddRange(MapPreset.Names);
        _preset.SelectedItem = MapPreset.Match(_options.Config) ?? "small";
        _preset.SelectedIndexChanged += (_, _) =>
        {
            MapPreset.Apply((string)_preset.SelectedItem!, _options.Config);
            _grid.Refresh();
        };

        _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
        _seed.ValueChanged += (_, _) => _options.Config.Seed = (int)_seed.Value;

        _generate.Click += async (_, _) => await RunAsync(null);
        _writeMod.Click += async (_, _) => await RunAsync(GenerationOptions.DefaultModDir);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4) };
        bar.Controls.Add(new Label { Text = "Size", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        bar.Controls.Add(_preset);
        bar.Controls.Add(new Label { Text = "Seed", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        bar.Controls.Add(_seed);
        bar.Controls.Add(_generate);
        bar.Controls.Add(_writeMod);

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_grid);
        left.Controls.Add(bar);

        var right = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 640,
        };
        right.Panel1.Controls.Add(_views);
        right.Panel2.Controls.Add(_log);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 430 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        var status = new StatusStrip();
        status.Items.Add(_status);

        Controls.Add(split);
        Controls.Add(status);

        // Everything in the generator reports progress with Console.WriteLine. Redirecting the
        // console is what lets all of that reach the log pane without touching a single call site.
        Console.SetOut(new TextBoxWriter(_log));
    }

    private async Task RunAsync(string? modDir)
    {
        if (_busy) return;
        _busy = true;
        SetEnabled(false);
        _log.Clear();
        _status.Text = modDir is null ? "Generating…" : "Generating and writing mod…";

        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(() =>
            {
                var r = Generator.Generate(_options);
                if (modDir is not null) Generator.WriteMod(r, _options, modDir);
                return r;
            });

            ShowPreviews(result);
            _status.Text = $"Done in {clock.ElapsedMilliseconds / 1000.0:F1} s — " +
                           $"{result.Provinces.Count} provinces" +
                           (modDir is null ? "" : $", mod written to {modDir}");
        }
        catch (Exception ex)
        {
            // A generation failure must not take the window with it; the message is far more
            // useful sitting in the log next to the parameters that caused it.
            Console.WriteLine();
            Console.WriteLine(ex);
            _status.Text = "Failed — see log";
        }
        finally
        {
            _busy = false;
            SetEnabled(true);
        }
    }

    private void SetEnabled(bool enabled)
    {
        // "Write mod" generates first, so it needs no prior result to be useful.
        _generate.Enabled = enabled;
        _writeMod.Enabled = enabled;
        _grid.Enabled = enabled;
        _preset.Enabled = enabled;
        _seed.Enabled = enabled;
    }

    private void ShowPreviews(GenerationResult result)
    {
        string? selected = _views.SelectedTab?.Text;
        _views.TabPages.Clear();

        if (result.Terra is not null)
        {
            var preview = result.Terra.Preview;
            AddView("Relief", TerraPreview.RenderRelief(preview));
            AddView("Height", TerraPreview.RenderHeight(preview));
            AddView("Rivers", TerraPreview.RenderRivers(preview));
            AddView("Moisture", TerraPreview.RenderMoisture(preview));
        }

        AddView("Terrain", PreviewRenderer.RenderTerrain(result));
        AddView("Provinces", PreviewRenderer.RenderProvinces(result));

        foreach (TabPage page in _views.TabPages)
            if (page.Text == selected) { _views.SelectedTab = page; break; }
    }

    private void AddView(string name, TerraPreview.Image image)
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
        _views.TabPages.Add(page);
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
