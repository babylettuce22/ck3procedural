using Ck3MapGen.Config;
using NoiseTool.Core;
using NoiseTool.Pipeline;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Where the heights come from. The generator only ever sees a <see cref="HeightmapImage"/>; this
/// is the thing that makes one, and the two kinds of thing that can — a 16-bit PNG on disk, and a
/// Forge pipeline run in memory — differ only in how they answer <see cref="Produce"/> and in what
/// identity they give the decode cache.
/// </summary>
public abstract class HeightmapProvider
{
    /// <summary>Short name for the toolbar chip and the status bar: a file name, or "Forge · …".</summary>
    public abstract string Label { get; }

    /// <summary>The long form, for tooltips: the full path, or what the pipeline will produce.</summary>
    public abstract string Detail { get; }

    /// <summary>
    /// Identity of what <see cref="Produce"/> would return right now. Two equal stamps mean the
    /// cached image still stands; anything that changes the result — a file rewritten, a slider
    /// nudged — changes the stamp. Replaces <see cref="HeightmapImage.StillStandsFor"/> for
    /// sources that are not files.
    /// </summary>
    public abstract string Stamp { get; }

    /// <summary>The <see cref="Core.Stage"/> name the produce step reports itself under.</summary>
    public abstract string PhaseName { get; }

    /// <summary>
    /// Makes the image and applies its size to <paramref name="cfg"/>. May take seconds for a
    /// vanilla-sized file and much longer for a pipeline with an unbaked erosion stage, so it is
    /// always called off the UI thread and honours <paramref name="ct"/>.
    /// </summary>
    public abstract HeightmapImage Produce(MapConfig cfg, CancellationToken ct, IProgress<string>? status);
}

/// <summary>A heightmap PNG on disk, decoded by <see cref="HeightmapSource.Read"/>.</summary>
public sealed class FileHeightmapProvider(string path, (int Width, int Height)? fitTo = null,
    bool allowUnverifiedSize = false)
    : HeightmapProvider
{
    public string Path { get; } = path;

    /// <summary>
    /// Build at the file's own size even though <see cref="TileFit"/> does not know it to render.
    /// The other answer to the same offer <see cref="FitTo"/> comes from, and kept here for the
    /// same reason: it is a decision about this file, asked again for the next one.
    /// </summary>
    public bool AllowUnverifiedSize { get; } = allowUnverifiedSize;

    /// <summary>
    /// The size to resample the file to on the way in, or null to take it as it is.
    ///
    /// Set when the file's own size is one <see cref="TileFit"/> refuses and the offer to fix it
    /// was accepted. It belongs to the provider rather than to the config because it is a fact
    /// about <em>this file</em>, not a preference about maps: point the tool at a different
    /// heightmap and the answer has to be asked again.
    /// </summary>
    public (int Width, int Height)? FitTo { get; } = fitTo;

    public override string Label => System.IO.Path.GetFileName(Path);

    public override string Detail
        => FitTo is { } fit
            ? $"{Path}\n\nResampled to {fit.Width}x{fit.Height} as it loads, because CK3 only "
              + "renders the whole of a map at certain sizes. The file itself is untouched."
            : AllowUnverifiedSize
                ? $"{Path}\n\nBuilt at its own size, which CK3 is not known to render correctly, "
                  + "to find out whether it does. Check the map's north and east edges in game."
                : Path;

    public override string PhaseName => "heightmap decode";

    public override string Stamp
    {
        get
        {
            // The fit is part of the identity, not a detail of how the image was made: change it
            // and the cached image no longer describes what Produce would return.
            string fit = (FitTo is { } f ? $"|fit={f.Width}x{f.Height}" : "")
                       + (AllowUnverifiedSize ? "|unverified" : "");
            var info = new FileInfo(Path);

            return info.Exists
                ? $"{Path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}{fit}"
                : $"{Path}|missing{fit}";
        }
    }

    public override HeightmapImage Produce(MapConfig cfg, CancellationToken ct, IProgress<string>? status)
        => HeightmapSource.Read(Path, cfg, FitTo, AllowUnverifiedSize);
}

/// <summary>
/// A Forge pipeline run at its full base resolution. The field comes out in the pipeline's 0..1
/// units with the waterline at CK3's own plane, so <see cref="HeightField.ToUInt16"/> is already
/// the 16-bit heightmap the game reads and nothing is normalised afterwards
/// (<see cref="HeightmapImage.Ck3Scale"/>).
/// </summary>
public sealed class ForgeHeightmapProvider(HeightPipeline pipeline, string name,
    bool allowUnverifiedSize = false) : HeightmapProvider
{
    public HeightPipeline Pipeline { get; } = pipeline;

    /// <summary>
    /// Build at the pipeline's output size even though <see cref="TileFit"/> does not know it to
    /// render. Set by the Heightmap tab's Use for generation when the user chose to test the size
    /// rather than change it.
    /// </summary>
    public bool AllowUnverifiedSize { get; } = allowUnverifiedSize;

    /// <summary>The project or preset name, for the label; not a path.</summary>
    public string Name { get; } = name;

    public override string Label => $"Forge · {Name}";

    public override string Detail
    {
        get
        {
            var (w, h) = Pipeline.OutputSize();
            return $"Heightmap produced by the Forge pipeline on the Heightmap tab: {w}×{h}, " +
                   $"{Pipeline.Stages.Count(s => s.Enabled)} stage(s), seed {Pipeline.MasterSeed}.";
        }
    }

    public override string PhaseName => "heightmap forge";

    /// <summary>
    /// Everything the output depends on: the base size, the project settings and every enabled
    /// stage's fingerprint, in order. Whether a bake is cached does not enter into it — a run
    /// with or without the cache produces the same field, only at a different cost.
    /// </summary>
    public override string Stamp
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("forge|").Append(Pipeline.BaseWidth).Append('x').Append(Pipeline.BaseHeight)
              .Append("|seed=").Append(Pipeline.MasterSeed)
              .Append("|sea=").Append(Pipeline.SeaLevel.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

            foreach (var stage in Pipeline.Stages)
                if (stage.Enabled) sb.Append('|').Append(stage.Fingerprint());

            if (AllowUnverifiedSize) sb.Append("|unverified");

            return sb.ToString();
        }
    }

    public override HeightmapImage Produce(MapConfig cfg, CancellationToken ct, IProgress<string>? status)
    {
        // The whole elevation scale downstream assumes the water plane sits at 4883/65535. A
        // pipeline with its sea level moved would ship a coastline the game disagrees with, and
        // nothing after this point could tell.
        if (MathF.Abs(Pipeline.SeaLevel - Ck3.SeaLevelNormalised) > 1e-5f)
            throw new InvalidOperationException(
                $"The Forge pipeline's sea level is {Pipeline.SeaLevel:F4}; it must be " +
                $"{Ck3.SeaLevelNormalised:F4} (19/255, CK3's water plane) to build a mod from.");

        var result = Pipeline.Run(Pipeline.BaseWidth, Pipeline.BaseHeight, isPreview: false, ct, status: status);

        var field = result.Field;
        int width = field.Width, height = field.Height;
        var raw = field.ToUInt16();

        // The float field is 680 MB at vanilla size and the generator is about to allocate its
        // own buffers of that order; nothing here needs it once the samples exist.
        field = null!;
        result = null!;

        return HeightmapSource.FromRaw(raw, width, height, Label, cfg, AllowUnverifiedSize);
    }
}

/// <summary>Routes a producer's status lines to the log, from whatever thread they arrive on.</summary>
internal sealed class ConsoleProgress : IProgress<string>
{
    public static readonly ConsoleProgress Instance = new();

    public void Report(string value) => Console.WriteLine("  " + value);
}
