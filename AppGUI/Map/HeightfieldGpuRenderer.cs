using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace Ck3MapGen.AppGUI;

internal interface IHeightfieldGpuRenderer : IDisposable
{
    PreviewRenderer.Image Render(Heightfield field, HeightfieldView view, int width, int height,
        int supersample, PreviewRenderer.Image? drape);
}

/// <summary>
/// Per-viewport Direct3D compute renderer. The heightfield and drape stay on the device until
/// replaced; camera updates upload only 128 bytes. Readback preserves the existing bitmap display,
/// compass, frame export and input handling. All device access is serialized by the backend.
/// </summary>
internal sealed class HeightfieldGpuRenderer : IHeightfieldGpuRenderer
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11ComputeShader? _columns, _resolve;
    private ID3D11Buffer? _constants, _heights, _drape, _frame, _resolved, _readback;
    private ID3D11ShaderResourceView? _heightView, _drapeView, _frameView, _cameraView;
    private ID3D11UnorderedAccessView? _frameOutput, _resolvedOutput;
    private Heightfield? _field;
    private PreviewRenderer.Image? _texture;
    private (int Width, int Height, int Supersample) _size;

    public HeightfieldGpuRenderer()
    {
        try
        {
            // Hardware only: failure goes to our existing CPU renderer, never to D3D's software GPU.
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.None,
                [FeatureLevel.Level_11_0], out _device, out _, out _context).CheckError();
            using var stream = typeof(HeightfieldGpuRenderer).Assembly.GetManifestResourceStream(
                "Ck3MapGen.AppGUI.Map.HeightfieldGpu.hlsl")
                ?? throw new InvalidOperationException("Missing terrain compute shader.");
            using var reader = new StreamReader(stream);
            string source = reader.ReadToEnd();
            _columns = _device.CreateComputeShader(Compiler.Compile(source, "Columns", "HeightfieldGpu.hlsl", "cs_5_0").Span);
            _resolve = _device.CreateComputeShader(Compiler.Compile(source, "Resolve", "HeightfieldGpu.hlsl", "cs_5_0").Span);
            _constants = _device.CreateBuffer(128, BindFlags.ShaderResource,
                ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16);
            _cameraView = _device.CreateShaderResourceView(_constants);
        }
        catch { Dispose(); throw; }
    }

    public unsafe PreviewRenderer.Image Render(Heightfield field, HeightfieldView view,
        int width, int height, int supersample, PreviewRenderer.Image? drape)
    {
        var device = _device ?? throw new ObjectDisposedException(nameof(HeightfieldGpuRenderer));
        var context = _context!;
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        if (field.Cols < 4 || field.Rows < 4 || supersample is < 1 or > 2)
            throw new NotSupportedException("Unsupported GPU heightfield dimensions or sampling.");
        int sw = checked(width * supersample), sh = checked(height * supersample);

        if (!ReferenceEquals(_field, field))
        {
            _heightView?.Dispose(); _heights?.Dispose();
            var data = new uint[field.Samples.Length];
            for (int i = 0; i < data.Length; i++) data[i] = field.Samples[i];
            _heights = Input(data);
            _heightView = device.CreateShaderResourceView(_heights);
            _field = field;
        }
        if (_drapeView is null || _texture != drape)
        {
            _drapeView?.Dispose(); _drape?.Dispose();
            var data = new uint[drape is { } t ? checked(t.Width * t.Height) : 1];
            if (drape is { } tex)
                for (int i = 0; i < data.Length; i++)
                    data[i] = (uint)(tex.Rgb[i * 3] | tex.Rgb[i * 3 + 1] << 8 | tex.Rgb[i * 3 + 2] << 16);
            _drape = Input(data);
            _drapeView = device.CreateShaderResourceView(_drape);
            _texture = drape;
        }
        if (_size != (width, height, supersample))
        {
            ReleaseFrames();
            _frame = Output(checked(sw * sh));
            _frameView = device.CreateShaderResourceView(_frame);
            _frameOutput = device.CreateUnorderedAccessView(_frame);
            _resolved = Output(checked(width * height));
            _resolvedOutput = device.CreateUnorderedAccessView(_resolved);
            _readback = device.CreateBuffer(checked((uint)(width * height * 4)), BindFlags.None,
                ResourceUsage.Staging, CpuAccessFlags.Read);
            _size = (width, height, supersample);
        }

        context.UpdateSubresource(Camera(field, view, width, height, supersample, drape), _constants!);
        context.CSSetShaderResource(3, _cameraView);
        context.CSSetShaderResource(0, _heightView);
        context.CSSetShaderResource(1, _drapeView);
        context.CSSetUnorderedAccessView(0, _frameOutput);
        context.CSSetShader(_columns);
        context.Dispatch((uint)((sw + 63) / 64), 1, 1);
        context.CSSetUnorderedAccessView(0, null);
        context.CSSetShaderResource(2, _frameView);
        context.CSSetUnorderedAccessView(0, _resolvedOutput);
        context.CSSetShader(_resolve);
        context.Dispatch((uint)((width + 7) / 8), (uint)((height + 7) / 8), 1);
        context.CSSetUnorderedAccessView(0, null);
        context.CSSetShaderResource(0, null);
        context.CSSetShaderResource(1, null);
        context.CSSetShaderResource(2, null);
        context.CSSetShaderResource(3, null);
        context.CopyResource(_readback!, _resolved!);

        var rgb = new byte[checked(width * height * 3)];
        var mapped = context.Map(_readback!, Vortice.Direct3D11.MapMode.Read);
        try
        {
            uint* pixels = (uint*)mapped.DataPointer;
            for (int i = 0; i < width * height; i++)
            {
                uint p = pixels[i];
                rgb[i * 3] = (byte)p;
                rgb[i * 3 + 1] = (byte)(p >> 8);
                rgb[i * 3 + 2] = (byte)(p >> 16);
            }
        }
        finally { context.Unmap(_readback!, 0); }
        device.DeviceRemovedReason.CheckError();
        return new(rgb, width, height);
    }

    private ID3D11Buffer Input(uint[] data) => _device!.CreateBuffer(data, BindFlags.ShaderResource,
        ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, structureByteStride: 4);

    private ID3D11Buffer Output(int count) => _device!.CreateBuffer(checked((uint)count * 4),
        BindFlags.UnorderedAccess | BindFlags.ShaderResource, ResourceUsage.Default,
        CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4);

    // Use the CPU renderer's calibration and orbit fit. The shader implements the same projection,
    // nearest drape sampling, lighting, fog, byte quantization and integer box resolve.
    private static float[] Camera(Heightfield field, HeightfieldView view, int width, int height,
        int ss, PreviewRenderer.Image? drape)
    {
        int cols = field.Cols, rows = field.Rows, sw = width * ss, sh = height * ss;
        var p = HeightfieldRenderer.Project(field, view, sw, sh);
        return Array.ConvertAll<double, float>([
            cols, rows, sw, sh, p.ZScale, p.LandSpan, Emit.MapDataWriter.WaterLevel16, ss,
            p.SinP, p.CosP, p.Focal, p.Cy, p.DirX, p.DirY, p.RightX, p.RightY,
            p.CamX, p.CamY, p.CamZ, p.BaseZ, p.Near, p.Far, p.Above, p.FogNear,
            drape?.Width ?? 1, drape?.Height ?? 1, drape is null ? 0 : 1, p.FogSpan,
            width, height, 0, 0], v => (float)v);
    }

    private void ReleaseFrames()
    {
        _frameView?.Dispose(); _frameView = null;
        _frameOutput?.Dispose(); _frameOutput = null;
        _resolvedOutput?.Dispose(); _resolvedOutput = null;
        _frame?.Dispose(); _frame = null;
        _resolved?.Dispose(); _resolved = null;
        _readback?.Dispose(); _readback = null;
        _size = default;
    }

    public void Dispose()
    {
        _context?.ClearState();
        ReleaseFrames();
        _heightView?.Dispose(); _heightView = null;
        _drapeView?.Dispose(); _drapeView = null;
        _heights?.Dispose(); _heights = null;
        _drape?.Dispose(); _drape = null;
        _cameraView?.Dispose(); _cameraView = null;
        _constants?.Dispose(); _constants = null;
        _columns?.Dispose(); _columns = null;
        _resolve?.Dispose(); _resolve = null;
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
        _field = null; _texture = null;
    }
}

/// <summary>Lazy hardware detection on the render worker; a failed device is never retried per frame.</summary>
internal sealed class HeightfieldRenderBackend : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<IHeightfieldGpuRenderer> _createGpu;
    private IHeightfieldGpuRenderer? _gpu;
    private bool _attempted, _disposed;
    internal bool UsingGpu { get; private set; }
    internal string? FallbackReason { get; private set; }

    public HeightfieldRenderBackend(Func<IHeightfieldGpuRenderer>? createGpu = null)
        => _createGpu = createGpu ?? (() => new HeightfieldGpuRenderer());

    public PreviewRenderer.Image Render(Heightfield field, HeightfieldView view, int width, int height,
        int supersample, PreviewRenderer.Image? drape)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_attempted)
            {
                _attempted = true;
                if (string.Equals(Environment.GetEnvironmentVariable("CK3MAPGEN_RENDERER"), "cpu", StringComparison.OrdinalIgnoreCase))
                    FallbackReason = "CPU renderer requested by CK3MAPGEN_RENDERER.";
                else
                {
                    try { _gpu = _createGpu(); }
                    catch (Exception ex) { FallBack(ex); }
                }
            }
            if (_gpu is not null)
            {
                try
                {
                    var frame = _gpu.Render(field, view, width, height, supersample, drape);
                    UsingGpu = true;
                    return frame;
                }
                catch (Exception ex) { FallBack(ex); }
            }
            return HeightfieldRenderer.Render(field, view, width, height, supersample, drape);
        }
    }

    private void FallBack(Exception ex)
    {
        FallbackReason = ex.Message;
        UsingGpu = false;
        try { _gpu?.Dispose(); }
        catch (Exception cleanup) { Console.WriteLine($"3D GPU cleanup: {cleanup.Message}"); }
        _gpu = null;
        Console.WriteLine($"3D view using CPU fallback: {FallbackReason}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _gpu?.Dispose();
            _gpu = null;
        }
    }
}
