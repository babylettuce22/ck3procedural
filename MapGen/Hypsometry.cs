using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

/// <summary>
/// A 16-bit height field's distribution, on the 0-255 scale heightmaps are actually discussed in.
///
/// Deliberately the same measurement on both sides of the pipeline. On import it answers the only
/// question that matters about a heightmap somebody else drew — is this on CK3's scale at all — and
/// on emit it says what came out the other end. Reading the two against each other is the whole
/// diagnostic for <see cref="HeightmapNormalizer"/>: the land percentiles are supposed to move and
/// the water share is supposed to arrive at vanilla's.
///
/// Everything is measured against <see cref="MapDataWriter.WaterLevel16"/>, i.e. against *our*
/// water plane rather than the source's. That is the point on import. A heightmap that puts its own
/// sea level anywhere else reads here as almost no water and absurdly high land, which is exactly
/// the symptom, and it is visible before anything downstream has had a chance to misinterpret it.
///
/// Vanilla's own heightmap, for reference: 40.14% of the map exactly 0, 47.18% at or below the
/// water plane, land percentiles 36 / 57 / 87 / 143 and a highest pixel at 191 rather than 255.
/// Vanilla does not use the top of the byte range at all, which is worth knowing before stretching
/// an imported map onto all of it.
/// </summary>
public sealed class Hypsometry
{
    /// <summary>Vanilla's land percentiles, in the order <see cref="Describe"/> prints ours.</summary>
    public const string VanillaLand = "36 / 57 / 87 / 143 / 191";

    /// <summary>One bucket per value of the 0-255 scale, counting land pixels only.</summary>
    private readonly int[] _landHistogram;

    public long Total { get; }
    public long Zero { get; }
    public long Water { get; }
    public long Land => Total - Water;

    private Hypsometry(long total, long zero, long water, int[] landHistogram)
    {
        Total = total;
        Zero = zero;
        Water = water;
        _landHistogram = landHistogram;
    }

    public static Hypsometry Measure(ushort[] height)
    {
        long zero = 0, water = 0;
        var landHistogram = new int[256];

        foreach (ushort v in height)
        {
            if (v == 0) zero++;
            if (v <= MapDataWriter.WaterLevel16) { water++; continue; }
            landHistogram[v / MapDataWriter.Step255]++;
        }

        return new Hypsometry(height.LongLength, zero, water, landHistogram);
    }

    /// <summary>
    /// The value on the 0-255 scale at or below which <paramref name="q"/> percent of *land* sits.
    /// Water is excluded entirely, because half the map being ocean would otherwise put every
    /// percentile below the water plane and say nothing about the terrain.
    /// </summary>
    public int Percentile(double q)
    {
        var want = (long)(Land * q / 100.0);
        long running = 0;

        for (int b = 0; b < _landHistogram.Length; b++)
        {
            running += _landHistogram[b];
            if (running >= want) return b;
        }

        return 255;
    }

    public string Describe()
        => $"{100.0 * Zero / Total:F2}% exactly 0 (vanilla 40.14), " +
           $"{100.0 * Water / Total:F2}% water (vanilla 47.18); land p50 {Percentile(50)}, " +
           $"p75 {Percentile(75)}, p90 {Percentile(90)}, p99 {Percentile(99)}, " +
           $"max {Percentile(100)} (vanilla {VanillaLand})";
}
