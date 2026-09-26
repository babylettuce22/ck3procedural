using NoiseTool.Pipeline;
using NoiseTool.Stages;

namespace Ck3MapGen.AppGUI;

/// <summary>How rugged the land is, whatever its shape.</summary>
public enum QuickRelief { Lowlands, Standard, Highlands }

/// <summary>
/// The relief choice, applied to a Forge pipeline: the same map type, flatter or more
/// mountainous.
///
/// It works on the relief stages every shipped preset shares — Ridges, Hills and, on the layout
/// presets, Base Relief — by scaling their parameters relative to what the preset was tuned with,
/// so a map type keeps its own character and only its ruggedness moves. Nothing touches the
/// coastline generator: the land is the same land, higher or lower.
///
/// This is half of the choice. The other half is in <see cref="QuickChoices.ApplyTo"/>: CK3's
/// hills and mountains are handed out by rank at fixed shares (see MapConfig.MountainProvinceShare),
/// so a taller heightmap on its own would look more rugged and play exactly the same. The shares
/// move with the relief so the game's terrain follows what the map shows.
/// </summary>
public static class QuickTerrain
{
    /// <summary>
    /// Multipliers over the preset's own values: range coverage, range height, hill height, base
    /// relief height, and — for presets whose high ground comes from the base noise itself rather
    /// than from ranges — the land profile (above 1 flattens toward plains), peak height and
    /// contrast.
    /// </summary>
    private readonly record struct Shift(float Coverage, float Ranges, float Hills, float Swells,
        float Profile, float Peak, float Contrast);

    private static Shift Factors(QuickRelief relief) => relief switch
    {
        QuickRelief.Lowlands => new(0.55f, 0.8f, 0.6f, 0.7f, 1.22f, 0.9f, 0.94f),
        QuickRelief.Highlands => new(1.7f, 1.2f, 1.5f, 1.4f, 0.86f, 1f, 1.04f),
        _ => new(1f, 1f, 1f, 1f, 1f, 1f, 1f),
    };

    public static void Apply(HeightPipeline pipeline, QuickRelief relief)
    {
        if (relief == QuickRelief.Standard) return;
        var (coverage, ranges, hills, swells, profile, peak, contrast) = Factors(relief);

        foreach (var stage in pipeline.Stages)
        {
            switch (stage)
            {
                case BaseNoiseStage:
                    Scale(stage.Params, "landCurve", profile);
                    Scale(stage.Params, "maxLand", peak);
                    break;
                case ContrastStage:
                    Scale(stage.Params, "contrast", contrast);
                    break;
                case RidgeStage:
                    Scale(stage.Params, "coverage", coverage);
                    Scale(stage.Params, "amplitude", ranges);
                    break;
                case HillStage:
                    Scale(stage.Params, "amplitude", hills);
                    break;
                case BaseReliefStage:
                    Scale(stage.Params, "amplitude", swells);
                    break;
            }
        }
    }

    /// <summary>Multiplies a float parameter, held inside the range its definition allows.</summary>
    private static void Scale(ParamSet parameters, string key, float factor)
    {
        if (parameters.Definitions.FirstOrDefault(d => d.Key == key) is not FloatParam definition) return;
        float value = parameters.GetFloat(key) * factor;
        parameters.Set(key, Math.Clamp(value, definition.Min, definition.Max));
    }
}
