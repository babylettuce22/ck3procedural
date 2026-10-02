using NoiseTool.Core;
using NoiseTool.Pipeline;
using NoiseTool.Stages;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The relief choice applied region by region instead of map-wide: some regions plains, some
/// highland, with the choice deciding how much of the map is rugged.
///
/// <see cref="QuickTerrain"/> on its own bends every relief stage by the same factor everywhere, so
/// Highlands lifts the plains as much as the ranges and the map comes out evenly rugged — measured
/// on Twin Continents 387524 at 9216, region-to-region contrast (p90/p10 of per-region median
/// barony relief) fell from 14x at Standard to 4.1x at Highlands, against vanilla's 8.1x.
///
/// Here the preset runs up to erosion three times on one seed — plains (Lowlands applied twice),
/// Standard and Highlands — and a ruggedness map blends them per pixel: 0 plains, 0.5 Standard, 1
/// Highlands. The map is two octaves of low-frequency noise, rank-flattened and then cut at
/// 1 − <see cref="RuggedShare"/> with a soft edge, so a region commits to plain or rugged rather
/// than averaging to the middle everywhere. Erosion runs once, on the blend. Same world at
/// Highlands: contrast 4.1x → 10.2x, median barony relief 6620 → 5336 (vanilla 4942).
///
/// Nothing in the Forge knows about it: the three runs are ordinary pipelines. The stages that
/// draw the coastline are untouched by <see cref="QuickTerrain"/> on the layout presets, so the
/// three agree on land and sea there; on the noise presets the plains run's flatter land profile
/// moves the coast a little inside plains regions, which the blend follows.
/// </summary>
public static class RegionalRelief
{
    /// <summary>Cycles of the ruggedness map per map height: about one region per continent.</summary>
    private const float Frequency = 1.2f;

    /// <summary>Half the width of the soft edge between plain and rugged, in rank units.</summary>
    private const float Edge = 0.12f;

    /// <summary>The share of the map that is rugged, for each choice.</summary>
    public static float RuggedShare(QuickRelief relief) => relief switch
    {
        QuickRelief.Lowlands => 0.25f,
        QuickRelief.Highlands => 0.75f,
        _ => 0.5f,
    };

    /// <summary>
    /// Runs <paramref name="preset"/> as regional relief. <paramref name="preset"/> must return a
    /// fresh pipeline each call — loaded, seeded and with its set piece drawn, but with no relief
    /// choice applied — since each of the three runs bends its own copy.
    /// </summary>
    public static HeightField Run(Func<HeightPipeline> preset, QuickRelief relief, int width, int height,
        bool isPreview, CancellationToken token, IProgress<string>? status = null)
    {
        var plains = preset();
        QuickTerrain.Apply(plains, QuickRelief.Lowlands);
        QuickTerrain.Apply(plains, QuickRelief.Lowlands);
        var standard = preset();
        var highland = preset();
        QuickTerrain.Apply(highland, QuickRelief.Highlands);

        status?.Report("regional relief: plains");
        var low = BeforeErosion(plains, width, height, isPreview, token, out _);
        status?.Report("regional relief: standard");
        var mid = BeforeErosion(standard, width, height, isPreview, token, out var erosion);
        status?.Report("regional relief: highlands");
        var high = BeforeErosion(highland, width, height, isPreview, token, out _);

        int w = mid.Width, h = mid.Height;
        var mask = Ruggedness(standard.MasterSeed, relief, w, h);
        var blend = new HeightField(w, h);
        Parallel.For(0, blend.Data.Length, i =>
        {
            float r = mask[i];
            blend.Data[i] = r < 0.5f
                ? low.Data[i] + (mid.Data[i] - low.Data[i]) * (r * 2f)
                : mid.Data[i] + (high.Data[i] - mid.Data[i]) * ((r - 0.5f) * 2f);
        });

        // A preview skips erosion as every Forge preview does (it is bake-only).
        if (erosion is null || isPreview) return blend;

        token.ThrowIfCancellationRequested();
        status?.Report("regional relief: erosion");
        return erosion.Process(blend, new StageContext
        {
            Width = w,
            Height = h,
            SeaLevel = standard.SeaLevel,
            MasterSeed = standard.MasterSeed,
            IsPreview = false,
            PreviewLongEdge = 0,
            Cancellation = token,
            Status = status,
        });
    }

    /// <summary>
    /// A shipped preset as the Quick page runs it, before any relief choice: loaded, on CK3's
    /// waterline, seeded, sized, erosion off without a GPU (as <see cref="Forge.ForgePanel.AdoptPreset"/>
    /// does), set piece drawn for the seed.
    /// </summary>
    public static HeightPipeline Preset(string path, int seed, int width, int height, QuickFeature feature)
    {
        var pipeline = new HeightPipeline();
        PresetIO.Load(pipeline, path);
        pipeline.SeaLevel = Ck3.SeaLevelNormalised;
        pipeline.MasterSeed = seed;
        pipeline.BaseWidth = width;
        pipeline.BaseHeight = height;
        if (!HydraulicErosionStage.GpuAvailable)
            foreach (var stage in pipeline.Stages.Where(s => s is HydraulicErosionStage)) stage.Enabled = false;
        QuickFeatures.Draw(pipeline, feature, seed);
        return pipeline;
    }

    /// <summary>
    /// Runs every stage before the pipeline's closing erosion, and hands that stage back; runs the
    /// whole pipeline, with no stage handed back, when it does not end in an enabled erosion.
    /// </summary>
    private static HeightField BeforeErosion(HeightPipeline pipeline, int width, int height, bool isPreview,
        CancellationToken token, out HydraulicErosionStage? erosion)
    {
        var active = pipeline.Stages.Where(s => s.Enabled).ToList();
        erosion = active.Count > 1 ? active[^1] as HydraulicErosionStage : null;
        var stop = erosion is null ? null : active[^2];
        return pipeline.Run(width, height, isPreview, token, stopAfter: stop).Field;
    }

    /// <summary>
    /// 0..1 per pixel: 0 plains, 1 rugged. Sampled in map-height units, so a preview and the full
    /// run draw the same regions. The rank is taken against a fixed grid of samples, not a random
    /// draw, so the map depends on the seed alone.
    /// </summary>
    private static float[] Ruggedness(int seed, QuickRelief relief, int width, int height)
    {
        var noise = new SimplexNoise(seed ^ 0x5E6107);
        var mask = new float[width * height];
        Parallel.For(0, height, y =>
        {
            float sy = (float)y / height * Frequency;
            for (int x = 0; x < width; x++)
                mask[y * width + x] = noise.Fbm((float)x / height * Frequency, sy, 2, 2.0f, 0.5f);
        });

        int stride = Math.Max(1, mask.Length / 65536);
        var sample = new float[(mask.Length + stride - 1) / stride];
        for (int i = 0, j = 0; i < mask.Length; i += stride, j++) sample[j] = mask[i];
        Array.Sort(sample);

        float threshold = 1f - RuggedShare(relief);
        Parallel.For(0, mask.Length, i =>
        {
            int at = Array.BinarySearch(sample, mask[i]);
            if (at < 0) at = ~at;
            float u = (float)at / sample.Length;
            float e = Math.Clamp((u - (threshold - Edge)) / (2f * Edge), 0f, 1f);
            mask[i] = e * e * (3f - 2f * e);
        });
        return mask;
    }
}

/// <summary>
/// A Quick world's heightmap with regional relief: the Forge preset run through
/// <see cref="RegionalRelief"/> at full size. Stands in for the Terrain workspace's
/// <see cref="MapGen.ForgeHeightmapProvider"/>, whose single pipeline can only hold the map-wide form
/// of the choice — so the workspace shows the preset with the choice applied uniformly, and this is
/// what a Quick world is actually built from. Using the workspace's pipeline again (its Use for
/// generation) replaces it.
/// </summary>
/// <param name="upscale">Enlarges the finished field this many times, after erosion: the Forge runs at
/// width x height and the world is built at the product. See <see cref="QuickChoices.ForgeUpscale"/>.</param>
public sealed class QuickReliefProvider(string presetPath, int seed, int width, int height,
    QuickFeature feature, QuickRelief relief, string name, int upscale = 1) : MapGen.HeightmapProvider
{
    public override string Label => $"Forge · {name} (regional relief)";

    public override string Detail =>
        $"The {name} preset, {width}×{height}{(upscale > 1 ? $" upscaled ×{upscale}" : "")}, seed {seed}, with {relief} relief applied region by region: "
        + $"about {RegionalRelief.RuggedShare(relief):P0} of the map rugged, the rest plains.";

    public override string PhaseName => "heightmap forge";

    public override string Stamp =>
        $"quick-regional|{presetPath}|{File.GetLastWriteTimeUtc(presetPath).Ticks}|{width}x{height}"
        + $"|seed={seed}|{relief}|{feature}" + (upscale > 1 ? $"|x{upscale}" : "");

    public override MapGen.HeightmapImage Produce(Config.MapConfig cfg, CancellationToken ct, IProgress<string>? status)
    {
        var field = RegionalRelief.Run(() => RegionalRelief.Preset(presetPath, seed, width, height, feature),
            relief, width, height, isPreview: false, ct, status);
        if (upscale > 1)
        {
            // As the Forge's own Upscale stage does it: Catmull-Rom, then clamped.
            status?.Report($"upscaling to {width * upscale} x {height * upscale}");
            field = field.ResampleCubic(width * upscale, height * upscale);
            field.Clamp01();
        }
        var raw = field.ToUInt16();
        return MapGen.HeightmapSource.FromRaw(raw, field.Width, field.Height, Label, cfg);
    }
}
