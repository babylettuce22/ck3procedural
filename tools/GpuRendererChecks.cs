using System.Diagnostics;
using Ck3MapGen.AppGUI;
using Ck3MapGen.Core;

namespace Ck3MapGen.Tools;

/// <summary>Runs real hardware shaders against the CPU reference, plus injected device failures.</summary>
internal static class GpuRendererChecks
{
    public static int Run(string? mod)
    {
        string output = Path.Combine(AppContext.BaseDirectory, "gpu-render-checks");
        Directory.CreateDirectory(output);
        string? requestedRenderer = Environment.GetEnvironmentVariable("CK3MAPGEN_RENDERER");
        Environment.SetEnvironmentVariable("CK3MAPGEN_RENDERER", null);
        try
        {
            var field = Fixture(512, 256);
            var drape = Texture(257, 129);
            var view = HeightfieldView.Default;
            var reference = HeightfieldRenderer.Render(field, view, 320, 200, 2, drape);
            int attempts = 0;
            Environment.SetEnvironmentVariable("CK3MAPGEN_RENDERER", "cpu");
            using (var forced = new HeightfieldRenderBackend(() =>
                   { attempts++; throw new Exception("Forced CPU attempted GPU initialization"); }))
            {
                Check(forced.Render(field, view, 320, 200, 2, drape).Rgb.SequenceEqual(reference.Rgb)
                    && !forced.UsingGpu && attempts == 0, "CPU override bypasses hardware detection");
            }
            Environment.SetEnvironmentVariable("CK3MAPGEN_RENDERER", null);
            using (var missing = new HeightfieldRenderBackend(() =>
                   { attempts++; throw new NotSupportedException("Injected missing GPU"); }))
            {
                Check(missing.Render(field, view, 320, 200, 2, drape).Rgb.SequenceEqual(reference.Rgb), "Initialization failure uses exact CPU output");
                missing.Render(field, view, 320, 200, 2, drape);
                Check(attempts == 1 && !missing.UsingGpu, "Failed hardware detection is attempted once");
            }
            var lost = new LostDevice();
            using (var backend = new HeightfieldRenderBackend(() => lost))
            {
                Check(backend.Render(field, view, 320, 200, 2, drape).Rgb.SequenceEqual(reference.Rgb), "Device loss recovers the same frame on CPU");
                backend.Render(field, view, 320, 200, 2, drape);
                Check(lost.Disposed && lost.Calls == 1 && !backend.UsingGpu, "Failed GPU is disposed and not reused");
                backend.Dispose();
                try { backend.Render(field, view, 320, 200, 2, drape); throw new Exception("Disposed renderer accepted work"); }
                catch (ObjectDisposedException) { Console.WriteLine("PASS Disposed renderer rejects queued work"); }
            }

            var initialization = Stopwatch.StartNew();
            using var gpu = new HeightfieldGpuRenderer();
            Console.WriteLine($"GPU initialization and shader compilation: {initialization.Elapsed.TotalMilliseconds:F1} ms");
            Compare(gpu, field, view, null, "tints", output);
            Compare(gpu, field, view, drape, "drape", output);
            Compare(gpu, field, view with { Yaw = 0, Pitch = 1.55 }, drape, "top-down", output);
            Compare(gpu, field, view with { Yaw = 2.4, Pitch = 0.12 }, drape, "low-angle", output);
            Compare(gpu, field, view with { Distance = 0.10, PanX = 0.12, PanY = -0.03 }, drape, "zoom-pan", output);
            Compare(gpu, field, view with { Exaggeration = 4, Yaw = -1.2 }, null, "exaggerated", output);
            Compare(gpu, Fixture(256, 512), view, Texture(73, 137), "portrait", output);
            Compare(gpu, field, view with { PanX = 1.25, PanY = -1.25 }, drape, "off-map", output);
            Compare(gpu, Flat(128, 64, 0), view, null, "all-water", output);
            Compare(gpu, Flat(128, 64, 30000), view, null, "flat-land", output);
            VerifyPanel(field, drape);

            if (mod is not null)
            {
                var loaded = new LoadedWorldView(LoadedWorld.Open(mod));
                var (_, packed) = loaded.ReadHeightfields();
                var ground = loaded.RenderImage("CK3 ground");
                Compare(gpu, packed, view, ground, "ck3-ground", output, 1280, 800);
                Compare(gpu, packed, view with { Distance = 0.22, PanX = 0.1, Pitch = 0.4 }, ground,
                    "ck3-ground-close", output, 1280, 800);
                field = packed; drape = ground;
            }
            else { field = Fixture(4096, 2048); drape = Texture(2048, 1024); }

            // Warm both paths; report medians including GPU readback, not just dispatch time.
            foreach (int ss in new[] { 1, 2 })
            {
                double Time(Func<PreviewRenderer.Image> render)
                {
                    render();
                    var times = new double[7];
                    for (int i = 0; i < times.Length; i++)
                    { var watch = Stopwatch.StartNew(); render(); times[i] = watch.Elapsed.TotalMilliseconds; }
                    Array.Sort(times); return times[times.Length / 2];
                }
                double cpuMs = Time(() => HeightfieldRenderer.Render(field, view, 1280, 800, ss, drape));
                double gpuMs = Time(() => gpu.Render(field, view, 1280, 800, ss, drape));
                Console.WriteLine($"1280x800 {ss}x sampling: CPU {cpuMs:F1} ms, GPU {gpuMs:F1} ms ({cpuMs / gpuMs:F2}x)");
            }
            Console.WriteLine($"PASS GPU renderer checks; comparison images: {output}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Environment.SetEnvironmentVariable("CK3MAPGEN_RENDERER", requestedRenderer); }
    }

    private static void Compare(HeightfieldGpuRenderer gpu, Heightfield field, HeightfieldView view,
        PreviewRenderer.Image? drape, string name, string output, int width = 640, int height = 400)
    {
        foreach (int ss in new[] { 1, 2 })
        {
            var watch = Stopwatch.StartNew();
            var cpu = HeightfieldRenderer.Render(field, view, width, height, ss, drape);
            double cpuMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var actual = gpu.Render(field, view, width, height, ss, drape);
            double gpuMs = watch.Elapsed.TotalMilliseconds;
            long total = 0; int changed = 0;
            for (int i = 0; i < cpu.Rgb.Length; i++)
            { int delta = Math.Abs(cpu.Rgb[i] - actual.Rgb[i]); total += delta; if (delta > 16) changed++; }
            double mean = total / (double)cpu.Rgb.Length, large = changed / (double)cpu.Rgb.Length;
            Console.WriteLine($"{name} {ss}x: mean channel error {mean:F4}/255; >16 error {large:P4}; CPU {cpuMs:F1} ms, GPU {gpuMs:F1} ms");
            using var c = PreviewRenderer.ToBitmap(cpu);
            using var g = PreviewRenderer.ToBitmap(actual);
            using var pair = new Bitmap(width * 2, height);
            using (var graphics = Graphics.FromImage(pair))
            { graphics.DrawImageUnscaled(c, 0, 0); graphics.DrawImageUnscaled(g, width, 0); }
            pair.Save(Path.Combine(output, $"{name}-{ss}x.png"));
            Check(mean < 0.1 && large < 0.001, $"{name} {ss}x matches CPU presentation");
        }
    }

    private static void VerifyPanel(Heightfield field, PreviewRenderer.Image drape)
    {
        ApplicationConfiguration.Initialize();
        var faults = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        void Unobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        { faults.Enqueue(e.Exception); e.SetObserved(); }
        TaskScheduler.UnobservedTaskException += Unobserved;
        try
        {
            using var panel = new HeightfieldPanel { Size = new Size(320, 200) };
            _ = panel.Handle;
            panel.SetDrape(drape);
            panel.SetField(field, field, "Fixture");
            var timeout = Stopwatch.StartNew();
            while (panel.CurrentFrame is null && timeout.Elapsed.TotalSeconds < 15)
            { Application.DoEvents(); Thread.Sleep(2); }
            Check(panel.CurrentFrame is { Width: 320, Height: 200 } && panel.UsingGpu,
                "WinForms viewport displays a hardware frame through its existing bitmap interface");
            panel.Size = new Size(321, 203);
            panel.SetExaggeration(2);
            panel.Dispose();
            var settle = Stopwatch.StartNew();
            while (settle.Elapsed.TotalMilliseconds < 200)
            { Application.DoEvents(); Thread.Sleep(2); }
            GC.Collect(); GC.WaitForPendingFinalizers();
            Application.DoEvents();
            Check(panel.IsDisposed, "Viewport closes safely with a render queued or in flight");
            Check(faults.IsEmpty, "Viewport completion and disposal leave no unobserved task faults");
        }
        finally { TaskScheduler.UnobservedTaskException -= Unobserved; }
    }

    private static Heightfield Fixture(int cols, int rows)
    {
        var samples = new ushort[cols * rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                double u = x / (double)cols, v = y / (double)rows;
                double peaks = Math.Exp(-Math.Pow((u - 0.55) * 9, 2) - Math.Pow((v - 0.5) * 7, 2));
                double h = 800 + 44000 * peaks + 6500 * Math.Sin(u * 15) * Math.Cos(v * 12);
                samples[y * cols + x] = (ushort)Math.Clamp(h, 0, 65535);
            }
        return new() { Samples = samples, Cols = cols, Rows = rows, LandMax = samples.Max(), LandTop = samples.Max(), LandShare = 0.5 };
    }

    private static Heightfield Flat(int cols, int rows, ushort h) => new()
    { Samples = Enumerable.Repeat(h, cols * rows).ToArray(), Cols = cols, Rows = rows, LandMax = h, LandTop = h, LandShare = h == 0 ? 0 : 1 };

    private static PreviewRenderer.Image Texture(int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            { int at = (y * width + x) * 3; rgb[at] = (byte)(x * 255 / width); rgb[at + 1] = (byte)(y * 255 / height); rgb[at + 2] = (byte)(((x / 9 + y / 7) % 2) * 100 + 80); }
        return new(rgb, width, height);
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }

    private sealed class LostDevice : IHeightfieldGpuRenderer
    {
        public int Calls;
        public bool Disposed;
        public PreviewRenderer.Image Render(Heightfield field, HeightfieldView view, int width, int height, int supersample, PreviewRenderer.Image? drape)
        { Calls++; throw new InvalidOperationException("Injected device loss"); }
        public void Dispose() => Disposed = true;
    }
}
