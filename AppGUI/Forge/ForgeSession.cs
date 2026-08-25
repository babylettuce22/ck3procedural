using Ck3MapGen.MapGen;
using NoiseTool.Core;
using NoiseTool.Pipeline;
using NoiseTool.Stages;

namespace Ck3MapGen.AppGUI.Forge;

/// <summary>One rendered preview: the picture, the field it was made from, and what to say about it.</summary>
public sealed record ForgePreview(
    Bitmap Image,
    HeightField Field,
    PipelineResult Result,
    long ElapsedMs,
    float LandShare,
    string Note,
    bool Incomplete);

/// <summary>
/// The Heightmap tab's model: one Forge pipeline, the preview loop that keeps a picture of it on
/// screen, and the slow deliberate operations — bake, export — that the picture is an
/// approximation of. No controls live here; <see cref="ForgePanel"/> is the view and this is what
/// it drives, so the logic reads the same as the standalone Forge app's and can be exercised
/// without a window.
///
/// Lives on the UI thread like the panel does: the debounce is a WinForms timer and every event
/// is raised there. Only <see cref="HeightPipeline.Run"/> and the renderer go to the pool.
/// </summary>
public sealed class ForgeSession : IDisposable
{
    public HeightPipeline Pipeline { get; } = new();

    /// <summary>What the pipeline is called in the toolbar chip and the log: the preset's file name, or "untitled".</summary>
    public string Name { get; private set; } = "untitled";

    /// <summary>The preset this pipeline was loaded from or last saved to, if any.</summary>
    public string? PresetPath { get; private set; }

    /// <summary>Changed since it was last loaded or saved.</summary>
    public bool Dirty { get; private set; }

    /// <summary>Regenerate the preview on every change, or only on request.</summary>
    public bool AutoPreview { get; set; } = true;

    public RenderMode ViewMode { get; private set; } = RenderMode.Hypsometric;

    /// <summary>The last preview's field, kept so a view-mode change can re-render without regenerating.</summary>
    public HeightField? LastField { get; private set; }

    public bool Baking => _baking;
    public bool Exporting => _exporting;

    /// <summary>Undo and redo for every stroke in this project.</summary>
    public UndoStack History { get; } = new();

    /// <summary>
    /// The authoring resolution paint layers live at, and the aspect brushes are round in.
    /// Follows the project's base size but is capped: strokes are drawn by hand at a scale far
    /// coarser than a vanilla-sized map, and a layer per channel at 18432x9216 would cost more
    /// than the heightmap does.
    /// </summary>
    public (int Width, int Height) LayerSize
    {
        get
        {
            int w = Pipeline.BaseWidth, h = Pipeline.BaseHeight;
            const int cap = 2048;
            if (w <= cap && h <= cap) return (w, h);

            double scale = (double)cap / Math.Max(w, h);
            return (Math.Max(64, (int)Math.Round(w * scale)), Math.Max(64, (int)Math.Round(h * scale)));
        }
    }

    /// <summary>Prepares a stage's layers at the project's authoring size. Called before painting begins.</summary>
    public void PrepareLayers(IPaintable stage)
    {
        var (w, h) = LayerSize;
        stage.EnsureLayers(w, h);
    }

    /// <summary>A stroke changed a layer: the pipeline has to re-run from the painted stage on.</summary>
    public void NotifyPainted()
    {
        Dirty = true;
        Pipeline.NotifyChanged();
    }

    /// <summary>The pipeline's contents or project settings changed; lists and readouts should refresh.</summary>
    public event Action? Changed;

    /// <summary>A preview landed. The receiver owns the bitmap from here on.</summary>
    public event Action<ForgePreview>? PreviewReady;

    /// <summary>Status line text, and whether a long operation is in flight.</summary>
    public event Action<string, bool>? Status;

    /// <summary>Something the user has to be told about failed: the exception and what it was doing.</summary>
    public event Action<Exception, string>? Failed;

    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 160 };
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _redrawCts;
    private bool _baking;
    private bool _exporting;
    private bool _loading;

    public ForgeSession()
    {
        Pipeline.Changed += (_, _) => OnPipelineChanged();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RunPreviewAsync(); };
    }

    private void OnPipelineChanged()
    {
        if (_loading) return;
        Dirty = true;
        Changed?.Invoke();
        QueuePreview();
    }

    // ------------------------------------------------------------------ project

    /// <summary>
    /// The starting pipeline: Forge's default stack at a size that previews instantly and exports
    /// in a minute. 2048x1024 with a 2x Upscale lands on 4096x2048 — the same shape as the
    /// heightmaps this tool has been built around, and a multiple of 64 on both axes for the
    /// packer. Raise it in the Project box when the map is right.
    /// </summary>
    public void NewDefault()
    {
        _loading = true;
        try
        {
            Pipeline.BaseWidth = 2048;
            Pipeline.BaseHeight = 1024;
            Pipeline.MasterSeed = Random.Shared.Next(1, 1_000_000);
            Pipeline.SeaLevel = Ck3.SeaLevelNormalised;
            Pipeline.PreviewLongEdge = 1024;
            // Hand Paint ships in the default stack rather than waiting behind the Add menu: the
            // brushes are the point of the tab, and a pipeline you have to modify before you can
            // paint hides them. It costs nothing until painted — an empty paint stage returns its
            // input untouched. It sits after the terrain passes so strokes are the last word on
            // relief, and before Upscale so they are upscaled with everything else.
            Pipeline.ReplaceAll(
            [
                new ContinentStage(),
                new BaseReliefStage(),
                new RidgeStage(),
                new HillStage(),
                new HeightPaintStage(),
                new ContrastStage(),
                new UpscaleStage(),
            ]);
            Name = "untitled";
            PresetPath = null;
        }
        finally
        {
            _loading = false;
        }

        Dirty = false;
        Changed?.Invoke();
        QueuePreview();
    }

    public PresetLoadResult LoadPreset(string path)
    {
        _loading = true;
        PresetLoadResult result;
        try
        {
            result = PresetIO.Load(Pipeline, path);

            // The generator's whole height scale assumes CK3's plane; a preset saved with the
            // old Forge default of 0.075 would build a map whose coast the game disagrees with.
            // Pin it here, where the user can see it happen, rather than refuse later.
            Pipeline.SeaLevel = Ck3.SeaLevelNormalised;

            Name = Path.GetFileNameWithoutExtension(path);
            PresetPath = path;
        }
        finally
        {
            _loading = false;
        }

        Dirty = false;
        Changed?.Invoke();
        QueuePreview();
        return result;
    }

    public void SavePreset(string path)
    {
        PresetIO.Save(Pipeline, path);
        Name = Path.GetFileNameWithoutExtension(path);
        PresetPath = path;
        Dirty = false;
        Changed?.Invoke();
    }

    public void SetBaseSize(int width, int height)
    {
        if (Pipeline.BaseWidth == width && Pipeline.BaseHeight == height) return;
        Pipeline.BaseWidth = width;
        Pipeline.BaseHeight = height;
        Pipeline.NotifyChanged();
    }

    public void SetSeed(int seed)
    {
        if (Pipeline.MasterSeed == seed) return;
        Pipeline.MasterSeed = seed;
        Pipeline.NotifyChanged();
    }

    /// <summary>A view setting, not a map setting: it does not dirty the preset.</summary>
    public void SetPreviewLongEdge(int longEdge)
    {
        if (Pipeline.PreviewLongEdge == longEdge) return;
        Pipeline.PreviewLongEdge = longEdge;
        Changed?.Invoke();
        QueuePreview();
    }

    public void SetView(RenderMode mode)
    {
        if (ViewMode == mode) return;
        ViewMode = mode;
        _ = RedrawAsync();
    }

    // ------------------------------------------------------------------- stages

    public PipelineStage AddStage(StageDescriptor descriptor)
    {
        var stage = descriptor.Create();
        Pipeline.Add(stage);
        return stage;
    }

    public void RemoveStage(PipelineStage stage) => Pipeline.Remove(stage);

    public void MoveStage(int index, int delta) => Pipeline.Move(index, delta);

    public void SetStageEnabled(PipelineStage stage, bool enabled)
    {
        if (stage.Enabled == enabled) return;
        stage.Enabled = enabled;
        Pipeline.NotifyChanged();
    }

    public void ResetStage(PipelineStage stage) => stage.Params.ResetToDefaults();

    /// <summary>The heightmap source the generator builds from: this pipeline, by reference, so later edits flow through.</summary>
    /// <param name="allowUnverifiedSize">Build even at an export size CK3 is not known to render;
    /// the panel asks before passing true. See <see cref="MapGen.TileFit.Known"/>.</param>
    public ForgeHeightmapProvider ProviderForGeneration(bool allowUnverifiedSize = false)
        => new(Pipeline, Name, allowUnverifiedSize);

    // ------------------------------------------------------------------ preview

    /// <summary>
    /// Half of what the last run cost, clamped: a drag then spends about a third of its time
    /// coalescing and the rest generating, whatever the resolution is. (See Forge's MainForm.)
    /// </summary>
    private void AdaptDebounce(long lastRunMs)
    {
        int target = (int)Math.Clamp(lastRunMs / 2, 35, 500);
        if (Math.Abs(target - _debounce.Interval) > 8) _debounce.Interval = target;
    }

    public void QueuePreview()
    {
        if (_loading || !AutoPreview) return;
        _debounce.Stop();
        _debounce.Start();
    }

    public async Task RunPreviewAsync()
    {
        _debounce.Stop();
        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;

        var (w, h) = Pipeline.PreviewBaseSize();
        Status?.Invoke($"Generating {w} × {h}…", true);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await Task.Run(() => Pipeline.Run(w, h, isPreview: true, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;

            var field = result.Field;
            float sea = Pipeline.SeaLevel;
            var mode = ViewMode;
            var bmp = await Task.Run(() => HeightRenderer.Render(field, sea, mode), cts.Token);
            if (cts.IsCancellationRequested) { bmp.Dispose(); return; }

            LastField = field;

            float land = field.FractionAbove(sea);
            string note = "";

            var (ow, oh) = Pipeline.OutputSize();
            if (field.Width == ow && field.Height == oh && result.SkippedForBake.Count == 0)
                note += "  ·  export resolution — this is what the generator gets";
            if (result.ResumedFrom is not null)
                note += $"  ·  from baked {result.ResumedFrom.DisplayName}";
            if (result.SkippedForBake.Count > 0)
                note += "  ·  NOT SHOWN: " + string.Join(", ", result.SkippedForBake.Select(s => s.DisplayName))
                      + " — press Bake";

            var preview = new ForgePreview(bmp, field, result, sw.ElapsedMilliseconds, land, note,
                Incomplete: result.SkippedForBake.Count > 0);

            AdaptDebounce(sw.ElapsedMilliseconds);
            PreviewReady?.Invoke(preview);
            Status?.Invoke($"Preview {field.Width} × {field.Height} in {sw.ElapsedMilliseconds} ms  ·  land {land * 100:0.0}%{note}", false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request.
        }
        catch (Exception ex)
        {
            Status?.Invoke("Preview failed: " + ex.Message, false);
            Failed?.Invoke(ex, "Preview");
        }
        finally
        {
            if (ReferenceEquals(_previewCts, cts)) _previewCts = null;
            cts.Dispose();
        }
    }

    /// <summary>Re-renders the field in hand — a view-mode change — without regenerating it.</summary>
    public async Task RedrawAsync()
    {
        var field = LastField;
        if (field is null) return;

        var mode = ViewMode;
        float sea = Pipeline.SeaLevel;

        _redrawCts?.Cancel();
        var cts = new CancellationTokenSource();
        _redrawCts = cts;

        try
        {
            var bmp = await Task.Run(() => HeightRenderer.Render(field, sea, mode), cts.Token);
            if (cts.IsCancellationRequested) { bmp.Dispose(); return; }

            PreviewReady?.Invoke(new ForgePreview(bmp, field, new PipelineResult(field, [], null), 0,
                field.FractionAbove(sea), "", false));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_redrawCts, cts)) _redrawCts = null;
            cts.Dispose();
        }
    }

    // --------------------------------------------------------------------- bake

    /// <summary>
    /// Computes a <see cref="PipelineStage.RequiresBake"/> stage at final resolution and caches
    /// it. The slow, deliberate path: baked stages are resolution-dependent, so they are never
    /// approximated at preview size.
    /// </summary>
    public async Task BakeAsync(PipelineStage stage)
    {
        if (_baking || !stage.RequiresBake) return;

        // A bake reads pipeline state on a worker thread; don't let a preview run concurrently
        // and mutate caches underneath it.
        _previewCts?.Cancel();
        _debounce.Stop();
        _baking = true;

        var (w, h) = Pipeline.ResolutionLeaving(stage);
        var cts = new CancellationTokenSource();
        var progress = new Progress<string>(s => Status?.Invoke($"Baking {stage.DisplayName} at {w} × {h} — {s}", true));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Status?.Invoke($"Baking {stage.DisplayName} at {w} × {h}…", true);

        try
        {
            await Task.Run(() => Pipeline.Bake(stage, cts.Token, progress), cts.Token);
            Status?.Invoke($"Baked {stage.DisplayName} at {w} × {h} in {sw.Elapsed.TotalSeconds:0.0} s", false);
        }
        catch (OperationCanceledException)
        {
            Status?.Invoke("Bake cancelled.", false);
        }
        catch (Exception ex)
        {
            stage.DiscardBake();
            Status?.Invoke("Bake failed: " + ex.Message, false);
            Failed?.Invoke(ex, "Bake");
        }
        finally
        {
            _baking = false;
            cts.Dispose();
            Changed?.Invoke();
        }

        await RunPreviewAsync();
    }

    // ------------------------------------------------------------------- export

    /// <summary>Runs the full chain and writes a 16-bit PNG; returns the size actually written.</summary>
    public async Task<(int Width, int Height)?> ExportAsync(string path)
    {
        if (_exporting) return null;
        _exporting = true;

        var (ow, oh) = Pipeline.OutputSize();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Status?.Invoke($"Exporting {ow} × {oh}…", true);

        try
        {
            var progress = new Progress<string>(s => Status?.Invoke($"Exporting → {ow} × {oh} — {s}", true));

            // The written size comes from the field itself, not from OutputSize(): a stage could
            // resize in a way TransformSize did not predict, and a mismatched header would be a
            // corrupt PNG rather than a wrong one.
            var written = await Task.Run(() =>
            {
                var result = Pipeline.Run(Pipeline.BaseWidth, Pipeline.BaseHeight, isPreview: false,
                    CancellationToken.None, status: progress);
                var f = result.Field;
                Png16.WriteGrayscale(path, f.Width, f.Height, f.ToUInt16());
                return (f.Width, f.Height);
            });

            Status?.Invoke($"Exported {written.Item1} × {written.Item2} in {sw.Elapsed.TotalSeconds:0.0} s → {Path.GetFileName(path)}", false);
            Changed?.Invoke();
            return written;
        }
        catch (Exception ex)
        {
            Status?.Invoke("Export failed: " + ex.Message, false);
            Failed?.Invoke(ex, "Export");
            return null;
        }
        finally
        {
            _exporting = false;
        }
    }

    public void CancelWork()
    {
        _debounce.Stop();
        _previewCts?.Cancel();
        _redrawCts?.Cancel();
    }

    public void Dispose()
    {
        CancelWork();
        _debounce.Dispose();
    }
}
