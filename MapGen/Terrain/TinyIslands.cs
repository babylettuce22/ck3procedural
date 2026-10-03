using Ck3MapGen.Config;
using Ck3MapGen.Emit;
using NoiseTool.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// <see cref="MapConfig.RemoveTinyIslands"/> on the generator's 16-bit heights: the adapter over
/// <see cref="IslandCleanup"/>, which the Forge's Remove Tiny Islands stage also runs, so one
/// setting means the same islands in both. Land is what heightmap.png will call land — strictly
/// above <see cref="MapDataWriter.WaterLevel16"/> — and the Forge stage classifies its floats by
/// the same 16-bit rounding, which is why the two agree at the waterline by construction.
/// </summary>
public static class TinyIslands
{
    /// <summary>
    /// <paramref name="levels"/> with every qualifying island sunk to seabed. Returns the input
    /// itself when nothing qualifies, and otherwise a copy: the input can be
    /// <see cref="HeightmapImage.Raw"/> (a Ck3Scale image, or Normalization off), which must stay
    /// exactly as decoded.
    /// </summary>
    public static ushort[] Remove(ushort[] levels, int width, int height, MapConfig cfg)
    {
        double cutoff = IslandCleanup.Cutoff(cfg.TinyIslandMaxArea, width, height);
        long maxArea = IslandCleanup.MaxArea(cutoff);

        const int water16 = MapDataWriter.WaterLevel16;
        var land = IslandCleanup.LandBits(levels, width, height, water16);
        var islands = IslandCleanup.Find(land, width, height, maxArea, null, CancellationToken.None);

        long pixels = 0;
        if (islands.Count > 0)
        {
            levels = (ushort[])levels.Clone();
            pixels = IslandCleanup.Fill(levels, width, height, land, islands, water16);
        }

        Console.WriteLine($"  tiny islands: {new IslandCleanup.Report(islands.Count, pixels, cutoff, width, height)}");
        return levels;
    }
}
