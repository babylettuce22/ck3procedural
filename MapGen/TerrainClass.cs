using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The terrain vocabulary the map is painted from, and the set CK3's common/province_terrain
/// accepts. Deliberately richer than <see cref="BiomeType"/>, which is a port of ck2rpg's
/// <c>biome()</c> — a function ck2rpg only uses to pick preview colours. Its real terrain
/// assignment is <c>cell.terrain</c> in createProvinceTerrain.js, which is what this mirrors.
/// </summary>
public enum TerrainClass : byte
{
    Sea,
    Beach,
    Plains,
    Farmlands,
    Steppe,
    Drylands,
    Desert,
    Jungle,
    Forest,
    Taiga,
    Wetlands,
    Floodplains,
    Hills,
    Mountains,
    DesertMountains,
    Arctic,
}

/// <summary>
/// Classifies every pixel of the province-resolution raster into a <see cref="TerrainClass"/>.
///
/// The previous scheme sampled <see cref="Biome.Classify"/> once per province, at that province's
/// seed cell, and painted the whole province with the result. That is why a single river pixel
/// landing on a seed turned an entire county into floodplains — measured at 7% of all land. Terrain
/// is a property of the ground, not of the administrative unit drawn on top of it, so it is
/// resolved per pixel here and provinces take a majority vote afterwards.
///
/// Ported in spirit from js/mapgen/createProvinceTerrain.js: a climate band from latitude, then
/// elevation tiers, then moisture and water adjacency, with noise breaking up every boundary so
/// nothing lands on a straight line.
/// </summary>
public static class TerrainClassifier
{
    /// <summary>How far inland, in province pixels, a beach may reach.</summary>
    private const int BeachReach = 5;

    /// <summary>How far from a river course, in province pixels, floodplains may reach.</summary>
    private const int FloodplainReach = 1;

    /// <summary>
    /// Share of land above the hill and mountain lines, measured off vanilla's own heightmap:
    /// its 121-170 band is 3.3% of land and its 81-120 band a further 9.4%.
    ///
    /// These are *percentiles of our own land*, not absolute elevations, for the same reason the
    /// heightmap is remapped onto vanilla's hypsometry — the simulation's raw elevation scale
    /// depends on how far the tectonic sim happened to run, so a fixed threshold classifies a
    /// wildly different fraction of the map from one seed to the next. Taking a fixed *fraction*
    /// keeps the terrain tiers aligned with what the heightmap actually renders. Using
    /// Limits.Mountains.Lower directly put 30.7% of land in the mountain class while the emitted
    /// heightmap showed 3.2%, so mountain rock was painted across ground that renders flat.
    /// </summary>
    private const double MountainShareOfLand = 0.033;
    private const double HillShareOfLand = 0.127;

    /// <summary>
    /// Moisture cut-offs, also as percentiles of land rather than absolute values. The simulation
    /// emits moisture on no fixed scale — measured on seed 1, an absolute threshold ported from
    /// ck2rpg left 0.1% of land as desert and 0.0% as drylands, because almost everything sat
    /// above it. Ranking makes "dry" mean dry relative to this world, which is what actually reads
    /// as a desert belt.
    /// </summary>
    private const double AridShareOfLand = 0.18;
    private const double SemiAridShareOfLand = 0.38;
    private const double WetShareOfLand = 0.72;

    public static TerrainClass[] Classify(WorldGrid world, MapConfig cfg, float[] elevation,
        byte[] landMask, byte[] riverMask, Rng rng)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int sea = cfg.Limits.SeaLevelUpper;

        var moisture = UpsampleMoisture(world, width, height);

        // Thresholds derived from this map's own distributions.
        float hills = LandPercentile(elevation, landMask, 1.0 - HillShareOfLand);
        float mountains = LandPercentile(elevation, landMask, 1.0 - MountainShareOfLand);
        int arid = (int)LandPercentile(moisture, landMask, AridShareOfLand);
        int semiArid = (int)LandPercentile(moisture, landMask, SemiAridShareOfLand);
        int wet = (int)LandPercentile(moisture, landMask, WetShareOfLand);

        Console.WriteLine($"  terrain thresholds: hills {hills:F0}, mountains {mountains:F0}, " +
                          $"moisture arid {arid} / semi-arid {semiArid} / wet {wet}");
        var coastDistance = DistanceToWater(landMask, width, height, BeachReach);

        // Distance from each pixel to the nearest river, so a floodplain can be a valley rather
        // than a line. Rivers are traced on the coarse simulation grid and drawn with Bresenham at
        // export resolution, so the raw mask is a one-pixel staircase — painted directly as
        // floodplains it reads as a thin stepped seam across the terrain, not a river valley.
        // DistanceToWater measures distance to zeroes, so the mask is inverted first.
        var riverDistance = DistanceToWater(Invert(riverMask), width, height, FloodplainReach);

        // Independent fields so wetlands, forest and the lowland sub-variants do not all switch
        // at the same place. ck2rpg uses five separate simplex instances for exactly this.
        var wetNoise = new SimplexNoise(rng);
        var forestNoise = new SimplexNoise(rng);
        var farmNoise = new SimplexNoise(rng);
        var edgeNoise = new SimplexNoise(rng);

        double coarse = 26.0 / width;    // biome-sized patches
        double fine = 90.0 / width;      // ragged edges between them

        var result = new TerrainClass[width * height];

        Parallel.For(0, height, y =>
        {
            // Climate bands are authored in raster space, and the province map is half of it.
            double rasterY = y * 2.0;

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                if (landMask[i] == 0) { result[i] = TerrainClass.Sea; continue; }

                float e = elevation[i];

                // The per-column jitter that keeps band edges ragged is indexed in simulation
                // space, so the province column has to be mapped back onto it.
                int worldX = (int)((long)x * world.Width / width);

                bool tropical = Biome.IsTropical(cfg, rasterY, worldX);
                bool subTropical = !tropical && Biome.IsSubTropical(cfg, rasterY, worldX);
                bool temperate = !tropical && !subTropical && Biome.IsTemperate(cfg, rasterY, worldX);
                bool cold = !tropical && !subTropical && !temperate;

                int m = moisture[i];
                double nWet = wetNoise.Unit(x * coarse, y * coarse);
                double nForest = forestNoise.Unit(x * coarse * 1.7, y * coarse * 1.7);
                double nFarm = farmNoise.Unit(x * coarse * 2.3, y * coarse * 2.3);
                double nEdge = edgeNoise.Unit(x * fine, y * fine);

                // Water features win over everything: they are the ground being visibly wet. The
                // valley narrows and widens along its length rather than running at one width,
                // and the noisy edge hides the staircase the upscaled river course would
                // otherwise leave.
                if (riverDistance[i] <= FloodplainReach &&
                    riverDistance[i] <= 1 + nEdge * FloodplainReach)
                {
                    result[i] = TerrainClass.Floodplains;
                    continue;
                }

                // Beaches hug the coast, with a noisy inland edge so the shore is not a uniform
                // ribbon. Only below the hill line — a cliff coast is not a beach.
                if (coastDistance[i] <= BeachReach && e < hills &&
                    coastDistance[i] <= 1 + nEdge * BeachReach)
                {
                    result[i] = TerrainClass.Beach;
                    continue;
                }

                // The tier boundaries are softened by noise so a range does not end on a clean
                // contour line — without it every hill/mountain edge is a visible iso-height ring.
                float wobble = (float)((nEdge - 0.5) * (mountains - hills) * 0.35);

                if (e >= mountains + wobble)
                {
                    result[i] = !cold && m <= semiArid
                        ? TerrainClass.DesertMountains
                        : TerrainClass.Mountains;
                    continue;
                }

                // Permanent snow: high ground in the cold band, with the line dropping as latitude
                // rises so it is not one altitude everywhere.
                if (cold && e >= hills + wobble - (mountains - hills) * 0.35)
                {
                    result[i] = TerrainClass.Arctic;
                    continue;
                }

                if (e >= hills + wobble)
                {
                    result[i] = TerrainClass.Hills;
                    continue;
                }

                if (cold)
                {
                    // The far polar cut-off: beyond it, tundra rather than forest.
                    result[i] = Biome.IsBelowPlainsLimit(cfg, rasterY) && nForest > 0.45
                        ? TerrainClass.Arctic
                        : TerrainClass.Taiga;
                    continue;
                }

                // Lowland elevation as a fraction of the way to the hill line, used to keep
                // wetlands and farmland off the upper slopes.
                double lowness = (e - sea) / Math.Max(1.0, hills - sea);

                if (tropical)
                {
                    if (m <= arid) result[i] = TerrainClass.Desert;
                    else if (m <= semiArid) result[i] = nWet > 0.55 ? TerrainClass.Drylands : TerrainClass.Plains;
                    else result[i] = m >= wet || nForest > 0.42 ? TerrainClass.Jungle : TerrainClass.Plains;
                    continue;
                }

                if (subTropical)
                {
                    if (m <= arid) result[i] = nWet > 0.72 ? TerrainClass.Drylands : TerrainClass.Desert;
                    else if (m <= semiArid) result[i] = nWet > 0.45 ? TerrainClass.Drylands : TerrainClass.Steppe;
                    else if (m >= wet && nForest > 0.55) result[i] = TerrainClass.Jungle;
                    else result[i] = TerrainClass.Plains;
                    continue;
                }

                // Temperate.
                if (m <= arid)
                    result[i] = TerrainClass.Steppe;
                else if (m <= semiArid)
                    result[i] = nForest > 0.62 ? TerrainClass.Forest : TerrainClass.Steppe;
                else if (m >= wet && nWet > 0.72 && lowness < 0.25)
                    result[i] = TerrainClass.Wetlands;
                else if (m >= semiArid && nFarm > 0.66 && lowness < 0.45)
                    result[i] = TerrainClass.Farmlands;
                else if (nForest > 0.5)
                    result[i] = TerrainClass.Forest;
                else
                    result[i] = TerrainClass.Plains;
            }
        });

        return result;
    }

    /// <summary>
    /// The value at <paramref name="fraction"/> of the land distribution, via a histogram over the
    /// observed range. Exact enough for a classification threshold and single-pass.
    /// </summary>
    private static float LandPercentile(float[] values, byte[] landMask, double fraction)
    {
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < values.Length; i++)
        {
            if (landMask[i] == 0) continue;
            if (values[i] < min) min = values[i];
            if (values[i] > max) max = values[i];
        }
        if (min > max) return 0;
        if (max - min < 1e-6f) return min;

        const int Bins = 4096;
        var histogram = new long[Bins];
        double scale = (Bins - 1) / (double)(max - min);

        for (int i = 0; i < values.Length; i++)
        {
            if (landMask[i] == 0) continue;
            histogram[Math.Clamp((int)((values[i] - min) * scale), 0, Bins - 1)]++;
        }

        long total = 0;
        foreach (long n in histogram) total += n;
        if (total == 0) return min;

        long target = (long)(total * Math.Clamp(fraction, 0, 1));
        long running = 0;
        for (int b = 0; b < Bins; b++)
        {
            running += histogram[b];
            if (running >= target) return (float)(min + b / scale);
        }
        return max;
    }

    /// <summary>
    /// Integer overload, for the moisture field. Histograms the values directly rather than
    /// widening the array first — at province resolution that copy is a 170 MB allocation on top
    /// of the four map-sized buffers the writers already hold, which was enough to run the process
    /// out of memory.
    /// </summary>
    private static float LandPercentile(int[] values, byte[] landMask, double fraction)
    {
        int min = int.MaxValue, max = int.MinValue;
        for (int i = 0; i < values.Length; i++)
        {
            if (landMask[i] == 0) continue;
            if (values[i] < min) min = values[i];
            if (values[i] > max) max = values[i];
        }
        if (min > max) return 0;
        if (min == max) return min;

        int bins = Math.Min(4096, max - min + 1);
        var histogram = new long[bins];
        double scale = (bins - 1) / (double)(max - min);

        long total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (landMask[i] == 0) continue;
            histogram[Math.Clamp((int)((values[i] - min) * scale), 0, bins - 1)]++;
            total++;
        }
        if (total == 0) return min;

        long target = (long)(total * Math.Clamp(fraction, 0, 1));
        long running = 0;
        for (int b = 0; b < bins; b++)
        {
            running += histogram[b];
            if (running >= target) return (float)(min + b / scale);
        }
        return max;
    }

    /// <summary>
    /// Smooth the simulation's moisture field, then bilinearly upsample it.
    ///
    /// The smoothing is not cosmetic — without it the terrain comes out in long horizontal
    /// strands. <see cref="Climate.SetMoisture"/> is a faithful port of ck2rpg's moisture.js, and
    /// that algorithm marches one cloud west-to-east **per row**, resetting to 50 at the start of
    /// every row and never coupling a row to the one above it. A row that crosses open ocean
    /// before making landfall therefore arrives far wetter than the row above it that hit an
    /// island early, and the difference is a hard step. Upsampled 9x, one simulation row becomes
    /// nine province rows of a different terrain class: thin, enormously elongated, sharp-edged in
    /// Y and soft in X.
    ///
    /// It was invisible until now only because the old classifier tested <c>Moisture > 0</c>,
    /// which is nearly always true. Making moisture a real driver exposed it.
    ///
    /// The fix is applied here rather than in SetMoisture because that field is shared — the
    /// desert flags, rivers and erosion all read it, and ck2rpg's exact behaviour there is
    /// load-bearing. Blurring the copy used for terrain keeps the large-scale structure that
    /// matters (dry continental interiors, wet windward coasts, rain shadows behind ranges) while
    /// removing a row-quantisation artefact that was never physical in the first place.
    /// </summary>
    private static int[] UpsampleMoisture(WorldGrid w, int width, int height)
    {
        var smooth = new float[w.Count];
        for (int i = 0; i < w.Count; i++) smooth[i] = w.Moisture[i];

        // Three box-blur passes approximate a gaussian. Radius 2 on a 1024-wide grid is well under
        // a percent of the map, so continental-scale wet/dry structure survives intact.
        smooth = BlurWrapped(smooth, w.Width, w.Height, passes: 3, radius: 2);

        var result = new int[width * height];
        double sx = (double)w.Width / width;
        double sy = (double)w.Height / height;

        Parallel.For(0, height, y =>
        {
            double gy = (y + 0.5) * sy - 0.5;
            int y0 = (int)Math.Floor(gy);
            double fy = gy - y0;
            int y0c = Math.Clamp(y0, 0, w.Height - 1);
            int y1c = Math.Clamp(y0 + 1, 0, w.Height - 1);

            for (int x = 0; x < width; x++)
            {
                double gx = (x + 0.5) * sx - 0.5;
                int x0 = (int)Math.Floor(gx);
                double fx = gx - x0;
                int x0w = ((x0 % w.Width) + w.Width) % w.Width;
                int x1w = (((x0 + 1) % w.Width) + w.Width) % w.Width;

                double m00 = smooth[y0c * w.Width + x0w];
                double m10 = smooth[y0c * w.Width + x1w];
                double m01 = smooth[y1c * w.Width + x0w];
                double m11 = smooth[y1c * w.Width + x1w];

                double top = m00 + (m10 - m00) * fx;
                double bottom = m01 + (m11 - m01) * fx;
                result[y * width + x] = (int)(top + (bottom - top) * fy);
            }
        });

        return result;
    }

    /// <summary>
    /// Separable box blur that wraps in X — the map is a cylinder, so blurring must not treat the
    /// date line as an edge — and clamps in Y at the poles.
    /// </summary>
    private static float[] BlurWrapped(float[] source, int width, int height, int passes, int radius)
    {
        var a = source;
        var b = new float[source.Length];
        int span = radius * 2 + 1;

        for (int p = 0; p < passes; p++)
        {
            // Horizontal.
            Parallel.For(0, height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float sum = 0;
                    for (int d = -radius; d <= radius; d++)
                        sum += a[row + (((x + d) % width) + width) % width];
                    b[row + x] = sum / span;
                }
            });
            (a, b) = (b, a);

            // Vertical.
            Parallel.For(0, height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float sum = 0;
                    for (int d = -radius; d <= radius; d++)
                        sum += a[Math.Clamp(y + d, 0, height - 1) * width + x];
                    b[row + x] = sum / span;
                }
            });
            (a, b) = (b, a);
        }

        return a;
    }

    /// <summary>
    /// Flips a 0/1 mask, so <see cref="DistanceToWater"/> — which measures distance to zeroes —
    /// can be reused to measure distance to a feature.
    /// </summary>
    private static byte[] Invert(byte[] mask)
    {
        var inverted = new byte[mask.Length];
        for (int i = 0; i < mask.Length; i++) inverted[i] = mask[i] == 0 ? (byte)1 : (byte)0;
        return inverted;
    }

    /// <summary>
    /// Chebyshev distance from each land pixel to the nearest water, capped at
    /// <paramref name="maxDistance"/>. A capped dilation is far cheaper than a full distance
    /// transform and the beach only ever needs the first few pixels.
    /// </summary>
    private static byte[] DistanceToWater(byte[] landMask, int width, int height, int maxDistance)
    {
        var distance = new byte[landMask.Length];
        byte cap = (byte)(maxDistance + 1);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                distance[i] = landMask[i] == 0 ? (byte)0 : cap;
            }
        });

        // Iterative dilation: one ring of land per pass.
        for (byte d = 1; d <= maxDistance; d++)
        {
            byte previous = (byte)(d - 1);
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (distance[i] != cap) continue;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= height) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = ((x + dx) % width + width) % width;
                            if (distance[yy * width + xx] == previous) { distance[i] = d; goto next; }
                        }
                    }
                    next: ;
                }
            });
        }

        return distance;
    }

    /// <summary>The CK3 terrain id for common/province_terrain.</summary>
    public static string Name(TerrainClass t) => t switch
    {
        TerrainClass.Sea => "sea",
        TerrainClass.Beach => "plains",           // CK3 has no beach terrain; it is a material
        TerrainClass.Plains => "plains",
        TerrainClass.Farmlands => "farmlands",
        TerrainClass.Steppe => "steppe",
        TerrainClass.Drylands => "drylands",
        TerrainClass.Desert => "desert",
        TerrainClass.Jungle => "jungle",
        TerrainClass.Forest => "forest",
        TerrainClass.Taiga => "taiga",
        TerrainClass.Wetlands => "wetlands",
        TerrainClass.Floodplains => "floodplains",
        TerrainClass.Hills => "hills",
        TerrainClass.Mountains => "mountains",
        TerrainClass.DesertMountains => "desert_mountains",
        TerrainClass.Arctic => "taiga",           // no arctic terrain type; taiga is the closest
        _ => "plains",
    };
}
