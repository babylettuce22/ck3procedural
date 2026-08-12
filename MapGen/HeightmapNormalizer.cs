using Ck3MapGen.Config;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Rescales a heightmap drawn on somebody else's height scale onto CK3's, before anything reads it.
///
/// The problem this exists for is Azgaar's, though it is not only Azgaar's. Its exports carry
/// elevation on a 0-100 scale with sea level at 20, so a fifth of the range is ocean floor — and
/// the ocean floor is the part CK3 cares least about, because the engine renders everything at or
/// below the water plane as sea regardless of how far below it sits. Handed to us unchanged, such a
/// map reads as very nearly all land: our water plane is at 19/255 and theirs is at the equivalent
/// of 51/255, so two and a half times the byte range comes in above the line CK3 tests against.
/// Land is then crammed into whatever is left above it, which is what produces the familiar
/// pancake-with-a-cliff-at-the-shore.
///
/// The fix is the one AzgaarToCK3 arrived at, applied to a raster rather than to Voronoi cells:
/// **renormalise land against the land population only**, so where the source put its sea level
/// stops participating in the scale at all. A map whose land occupies a tenth of its range and one
/// whose land occupies all of it come out the same, because in both cases the lowest land pixel is
/// pinned one step above the water plane and the highest to <see cref="MapConfig.LandTop"/>.
///
/// Two deliberate departures from that tool:
///
/// **The seabed is kept, not discarded.** It flattens every water pixel to zero. We compress the
/// source's water band into ours instead, because <c>MapDataWriter.ShapeCoastline</c> is built
/// around not overwriting a seabed somebody drew — it grades only water lying within one step of
/// the plane, precisely so a real one survives. Flattening here would hand that pass a plate to
/// grade on every map and throw away the distinction it exists to make.
///
/// **The top anchor is a percentile, not the maximum.** Anchoring on the highest land pixel means
/// one stray sample decides the scale for the whole map, and stray samples are common in exported
/// heightmaps. <see cref="MapConfig.LandTopPercentile"/> anchors below the tail and clips what is
/// above, which costs the very top of a peak and protects everything else from it. It defaults
/// close to the maximum on purpose — see the note there for why a percentile that sounds cautious
/// flattens whole mountain ranges once land is counted in millions of pixels.
///
/// This is emphatically not the hypsometric remap that <c>MapDataWriter.ElevationTo16</c> used to
/// do and no longer does. That reshaped the *distribution*, ranking land and forcing it
/// onto vanilla's curve, so the map that loaded was not the map its author drew. This is linear and
/// monotonic: it changes what the numbers mean, never their order or their relative spacing. Every
/// ridge, plain and valley keeps its shape, and only the scale they are expressed on moves.
/// </summary>
public static class HeightmapNormalizer
{
    /// <summary>
    /// The imported field on CK3's scale. Returns <paramref name="raw"/> itself when normalisation
    /// is off, so the default path costs nothing and stays bit-exact.
    ///
    /// Settings-dependent, and therefore called on every run rather than cached with the decode —
    /// see <see cref="HeightmapImage"/> for why that distinction is load-bearing.
    /// </summary>
    public static ushort[] Normalize(ushort[] raw, MapConfig cfg)
    {
        if (!cfg.NormalizeImportedHeightmap) return raw;

        int sourceSea = (int)Math.Round(Math.Clamp(cfg.SourceSeaLevel, 0, 254) * MapDataWriter.Step255);

        // The land population, and nothing else, decides the land scale. A 65536-bin histogram is
        // exact for 16-bit input and cheap enough at any map size to be worth preferring over a
        // sort of thirty million samples.
        var histogram = new int[65536];
        long landCount = 0;
        int landMin = ushort.MaxValue;

        foreach (ushort v in raw)
        {
            if (v <= sourceSea) continue;
            histogram[v]++;
            landCount++;
            if (v < landMin) landMin = v;
        }

        if (landCount == 0)
        {
            Console.WriteLine($"  WARNING: normalisation skipped — no pixel sits above the source " +
                              $"sea level of {cfg.SourceSeaLevel:F0}/255. Either the heightmap is " +
                              $"entirely ocean or SourceSeaLevel is set too high for it.");
            return raw;
        }

        // Anchor the top just below the tail rather than on it. Walking up from landMin rather than
        // from zero keeps this over the land population only, which is what the percentile is of.
        var want = (long)(landCount * Math.Clamp(cfg.LandTopPercentile, 0, 100) / 100.0);
        long running = 0;
        int landTop = landMin;

        for (int v = landMin; v < histogram.Length; v++)
        {
            running += histogram[v];
            if (running < want) continue;
            landTop = v;
            break;
        }

        const int water16 = MapDataWriter.WaterLevel16;

        // One whole 0-255 step above the plane. CK3 tests `> WaterLevel16` for land, so this is the
        // lowest value the engine will still render dry, and every land pixel lands at or above it.
        const int lowestLand = water16 + MapDataWriter.Step255;

        int topLand = Math.Clamp(
            (int)Math.Round(cfg.LandTop * MapDataWriter.Step255), lowestLand, 65535);

        double landSpan = Math.Max(1, landTop - landMin);
        double landRange = topLand - lowestLand;
        double seaSpan = Math.Max(1, sourceSea);

        var result = new ushort[raw.Length];
        long clipped = 0;

        Parallel.For(0, raw.Length, () => 0L, (i, _, local) =>
        {
            int v = raw[i];
            double scaled;

            if (v > sourceSea)
            {
                double t = (v - landMin) / landSpan;
                if (t > 1.0) { t = 1.0; local++; }
                scaled = lowestLand + t * landRange;
            }
            else
            {
                // Water keeps its shape and loses its depth: the source's whole below-sea band is
                // compressed into ours. Both ends are fixed points — 0 stays 0, and a pixel exactly
                // at the source's sea level arrives exactly on our water plane — so the coastline
                // the author drew is the coastline that comes out.
                scaled = v / seaSpan * water16;
            }

            result[i] = (ushort)Math.Clamp(Math.Round(scaled), 0, 65535);
            return local;
        }, local => Interlocked.Add(ref clipped, local));

        double sourceWaterShare = 100.0 * (raw.LongLength - landCount) / raw.LongLength;

        Console.WriteLine($"  normalised: source sea {cfg.SourceSeaLevel:F0}/255 " +
                          $"({sourceWaterShare:F2}% of the map at or below it) → " +
                          $"{MapDataWriter.WaterLevel255}/255; land " +
                          $"{(double)landMin / MapDataWriter.Step255:F0}.." +
                          $"{(double)landTop / MapDataWriter.Step255:F0} → " +
                          $"{lowestLand / MapDataWriter.Step255}..{topLand / MapDataWriter.Step255} " +
                          $"(p{cfg.LandTopPercentile:0.####} anchor, {clipped:N0} px clipped above it)");

        return result;
    }
}
