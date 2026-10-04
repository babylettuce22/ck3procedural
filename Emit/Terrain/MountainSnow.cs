using Ck3MapGen.Config;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Permanent mountain snow controls, sanitised once per texture write. Auto replaces the height
/// ramp with summer temperature; precipitation gently reduces its strength. Ridge shape and the
/// summit breakup remain TerrainPalette's job. This is an artistic coverage model, not a glacier
/// simulation, and does not control CK3's seasonal snow mask or the Arctic terrain palette.
/// </summary>
public readonly record struct MountainSnow(
    bool Automatic, double Amount, double StartPercentile, double FullPercentile,
    double SummerStartC, double SummerFullC, double PrecipitationInfluence)
{
    public static MountainSnow FromConfig(MapConfig cfg)
    {
        double start = Value(cfg.MountainSnowStartPercentile, 0.95, 0, 1);
        double full = Value(cfg.MountainSnowFullPercentile, 0.993, 0, 1);
        double warm = Value(cfg.MountainSnowSummerStartC, 5, -100, 100);
        double cold = Value(cfg.MountainSnowSummerFullC, -3, -100, 100);
        return new(cfg.MountainSnowMode != MountainSnowMode.Manual,
            Value(cfg.MountainSnowAmount, 1, 0, 4), Math.Min(start, full), Math.Max(start, full),
            Math.Max(warm, cold), Math.Min(warm, cold),
            Value(cfg.MountainSnowPrecipitationInfluence, 0.4, 0, 1));
    }

    public double TemperaturePeak(double warmestC)
    {
        if (!double.IsFinite(warmestC)) return 0;
        if (SummerStartC == SummerFullC) return warmestC < SummerStartC ? 1 : 0;
        return 1 - Field.SmoothStep(SummerFullC, SummerStartC, warmestC);
    }

    public double MoistureStrength(double annualMm)
        => 1 - PrecipitationInfluence * (1 - Field.SmoothStep(100, 1000,
            double.IsFinite(annualMm) ? Math.Max(0, annualMm) : 0));

    private static double Value(double value, double fallback, double min, double max)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
