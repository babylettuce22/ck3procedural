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
/// whose land occupies all of it come out the same, because in both cases the level the land mass
/// begins at is pinned one step above the water plane and the top of it to
/// <see cref="MapConfig.LandTop"/>.
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
/// **The bottom anchor is a detected floor, not the minimum.** This is the same argument as the one
/// above, arrived at a playtest later: for as long as the bottom was a true minimum, the top was
/// protected from a stray sample and the bottom was not, and a map whose land began at 128/255 was
/// anchored on 585 pixels sitting at 20/255 — 0.026% of its land. The affine map then had nothing
/// to do, the continent shipped as a plateau, and every coastline was a vertical wall of its full
/// height. See <see cref="MapConfig.LandFloorDensity"/> for why the fix walks down from the land
/// mode rather than taking a percentile, which looks like the obvious answer and is not.
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
        if (cfg.Normalization == HeightmapNormalization.Off) return raw;

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

        const int water16 = MapDataWriter.WaterLevel16;

        // One whole 0-255 step above the plane. CK3 tests `> WaterLevel16` for land, so this is the
        // lowest value the engine will still render dry, and every land pixel lands at or above it.
        const int lowestLand = water16 + MapDataWriter.Step255;

        int landFloor = DetectLandFloor(histogram, landMin, cfg.LandFloorDensity);

        // Everything under the floor is coastal fringe that will be flattened onto the plane. It is
        // counted here rather than in the map below, because the histogram already knows.
        long clippedBelow = 0;
        for (int v = landMin; v < landFloor; v++) clippedBelow += histogram[v];

        // The percentile is of the population the walk can actually reach — land at or above the
        // floor — and not of all land. This is load-bearing rather than pedantic: `want` taken over
        // the whole population can exceed what a walk starting at the floor accumulates, and then
        // the walk runs off the end of the histogram without ever tripping, leaving landTop at the
        // floor, landSpan at 1, and every land pixel on the map clipped flat onto LandTop.
        long anchored = landCount - clippedBelow;
        var want = (long)(anchored * Math.Clamp(cfg.LandTopPercentile, 0, 100) / 100.0);
        long running = 0;
        int landTop = landFloor;

        for (int v = landFloor; v < histogram.Length; v++)
        {
            running += histogram[v];
            if (running < want) continue;
            landTop = v;
            break;
        }

        // Only reachable at LandTopPercentile 0, where `want` is 0 and the walk breaks on its first
        // bin. Anchoring on the highest land pixel is the sane reading of "clip nothing below".
        if (landTop <= landFloor)
        {
            int landMax = landFloor;
            for (int v = histogram.Length - 1; v > landFloor; v--)
                if (histogram[v] != 0) { landMax = v; break; }

            landTop = Math.Max(landFloor + 1, landMax);
        }

        long clippedAbove = 0;
        for (int v = landTop + 1; v < histogram.Length; v++) clippedAbove += histogram[v];

        int topLand = Math.Clamp(
            (int)Math.Round(cfg.LandTop * MapDataWriter.Step255), lowestLand, 65535);

        bool stretch = cfg.Normalization == HeightmapNormalization.Stretch;

        double landSpan = Math.Max(1, landTop - landFloor);
        double landRange = topLand - lowestLand;
        double seaSpan = Math.Max(1, sourceSea);

        // Shift only ever brings land *down* onto the plane. A source whose floor already sits below
        // the lowest dry value has nothing to shift, and raising it instead would push its peaks off
        // the top of the range.
        double drop = Math.Max(0, landFloor - lowestLand);

        var result = new ushort[raw.Length];

        Parallel.For(0, raw.Length, i =>
        {
            int v = raw[i];
            double scaled;

            if (v > sourceSea)
            {
                scaled = stretch
                    ? lowestLand + Math.Min(1.0, (v - landFloor) / landSpan) * landRange
                    : v - drop;

                // The floor is a detected value rather than a minimum, so land below it is ordinary
                // input rather than an impossibility, and both branches above run it *under* the
                // plane. Left unclamped it would arrive as sea — which then disagrees with
                // provinces.png, since the partition was derived before any of this ran.
                if (scaled < lowestLand) scaled = lowestLand;
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
        });

        double sourceWaterShare = 100.0 * (raw.LongLength - landCount) / raw.LongLength;
        double floor255 = (double)landFloor / MapDataWriter.Step255;
        double top255 = (double)landTop / MapDataWriter.Step255;

        Console.WriteLine($"  normalised ({cfg.Normalization}): source sea " +
                          $"{cfg.SourceSeaLevel:F0}/255 " +
                          $"({sourceWaterShare:F2}% of the map at or below it) → " +
                          $"{MapDataWriter.WaterLevel255}/255");

        // The floor is reported as *detected*, never as a minimum, because the two differ by a
        // hundred steps on exactly the maps this exists for and reading it as a minimum is what
        // made the fault invisible: the raw minimum was 20.00/255 on a map whose land begins at 128.
        Console.WriteLine($"  land floor detected at {floor255:F2}/255 " +
                          $"(lowest land pixel {(double)landMin / MapDataWriter.Step255:F2}, " +
                          $"{clippedBelow:N0} px below the floor flattened onto " +
                          $"{lowestLand / MapDataWriter.Step255})");

        Console.WriteLine(stretch
            ? $"  land {floor255:F0}..{top255:F0} → {lowestLand / MapDataWriter.Step255}.." +
              $"{topLand / MapDataWriter.Step255} (p{cfg.LandTopPercentile:0.####} anchor, " +
              $"{landRange / landSpan:F2}x amplification, {clippedAbove:N0} px clipped above it)"
            : $"  land shifted down {drop / MapDataWriter.Step255:F2}/255, relief 1:1, nothing " +
              $"clipped above");

        if (stretch && landRange / landSpan > 2.0)
            Console.WriteLine("  WARNING: land is being amplified more than twofold. The source's " +
                              "land occupies a narrow band, and every slope on the map is being " +
                              "exaggerated by that factor. Consider Shift, or a lower LandTop.");

        return result;
    }

    /// <summary>
    /// The value the land mass actually begins at, which is not the lowest land pixel.
    ///
    /// Walks down from the busiest land level while the population stays above
    /// <paramref name="density"/> of that peak, and stops where it collapses. On the map this was
    /// written for, the land mode is 129/255 holding 304,450 px and 128 holds 265,298, while
    /// everything below 127 runs at roughly 500 per level — a coastal ramp one pixel wide, not
    /// terrain. It found 128/255 in all eighteen combinations of six source sea levels and three
    /// thresholds, where a bottom percentile over the same inputs swung by 31 steps.
    ///
    /// Binned to 0-255 rather than run over the 16-bit histogram directly, and that is the whole
    /// trick. Density at 16-bit resolution is meaningless: a well-formed heightmap spreads its land
    /// over thirty thousand distinct levels, so every level looks like a collapse relative to its
    /// neighbours and the walk stops immediately. The 0-255 scale is the one a land mass is dense on.
    /// </summary>
    private static int DetectLandFloor(int[] histogram, int landMin, double density)
    {
        if (density <= 0) return landMin;

        var coarse = new long[256];
        for (int v = landMin; v < histogram.Length; v++)
            coarse[v / MapDataWriter.Step255] += histogram[v];

        int mode = 0;
        for (int b = 1; b < coarse.Length; b++)
            if (coarse[b] > coarse[mode]) mode = b;

        double collapse = coarse[mode] * Math.Clamp(density, 0, 1);
        int lowest = landMin / MapDataWriter.Step255;

        int floor = mode;
        while (floor > lowest && coarse[floor - 1] >= collapse) floor--;

        // A map already on CK3's scale walks all the way back down to its own lowest band, where
        // this is the identity and the anchor is the minimum after all.
        return Math.Max(landMin, floor * MapDataWriter.Step255);
    }
}
