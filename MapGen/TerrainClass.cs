using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The terrain vocabulary the map is painted from, and the set CK3's common/province_terrain
/// accepts. Mirrors <c>cell.terrain</c> in ck2rpg's createProvinceTerrain.js.
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

    /// <summary>
    /// A watered pocket in a desert. Appended rather than slotted in beside Desert because the
    /// numeric values are used as array indices in the province-terrain vote, so inserting one
    /// would silently renumber every class above it.
    ///
    /// Like <see cref="Farmlands"/>, nothing in the climate classifier assigns this — it is placed
    /// later, from drainage, by <see cref="Cultivation"/>.
    /// </summary>
    Oasis,
}

/// <summary>
/// Classifies every pixel of the province-resolution raster into a <see cref="TerrainClass"/>.
///
/// The previous scheme sampled a biome once per province, at that province's seed cell, and
/// painted the whole province with the result. That is why a single river pixel
/// landing on a seed turned an entire county into floodplains — measured at 7% of all land. Terrain
/// is a property of the ground, not of the administrative unit drawn on top of it, so it is
/// resolved per pixel here and provinces take a majority vote afterwards.
///
/// Climate is no longer a latitude band. <see cref="ClimateModel"/> produces temperature and
/// rainfall from atmospheric circulation and <see cref="Koppen"/> turns those into a vegetation
/// class, which is what this paints. Elevation, coastline and a noise mosaic are layered on top —
/// those are properties of the ground rather than of the climate, and CK3 wants them as terrain
/// types all the same.
///
/// What that replaced: a band from latitude, displaced by altitude and noise, plus ck2rpg's per-row
/// moisture march. Both were functions of y, so both drew stripes, and no amount of noise on a
/// function of y makes anything other than a wavy stripe.
/// </summary>
public static class TerrainClassifier
{
    /// <summary>
    /// The painted map and the climate behind it. The climate is returned rather than thrown away
    /// because it is the only way to check the model: a terrain map can look plausible while the
    /// temperatures behind it are nonsense, and <c>debug_climate.png</c> is directly comparable
    /// with any published Koppen map.
    ///
    /// <see cref="Climate"/> is also what the ground is *painted* from — it picks the material
    /// family in <see cref="Emit.TerrainPalette"/> and the tree species in
    /// <see cref="Emit.TreeWriter"/> — so on an imported map it is the reconciled zone rather than
    /// the raw one, and the run reports how many pixels that moved. <see cref="Field"/> is
    /// untouched either way and is where the temperatures and rainfall themselves live.
    /// </summary>
    public sealed class Result
    {
        public required TerrainClass[] Terrain { get; init; }
        public required KoppenClass[] Climate { get; init; }
        public required ClimateField Field { get; init; }
    }

    /// <summary>
    /// How far inland a beach may reach, in *vanilla* province pixels. Scaled to the map being
    /// generated via <see cref="MapConfig.Scaled"/> — left absolute, a beach at <c>tiny</c> would
    /// be nine times wider relative to the continent it is on than the same beach at
    /// <c>vanilla</c>.
    /// </summary>
    private const int BeachReachAtVanilla = 5;

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
    /// Where a lowland is wet enough to be marsh rather than merely well-watered. A percentile of
    /// this map's own rainfall, not an absolute: wetlands are the wettest ground *here*, and a
    /// millimetre threshold would carpet a rainforest world and leave a dry one with none.
    ///
    /// Not the top decile, which is what it was, and which produced exactly zero wetlands. The
    /// wettest tenth of a map is its windward mountain slopes almost by definition, and the test
    /// also demands flat low ground — so the two conditions could not both hold anywhere. A quarter
    /// is wet enough to mean something and common enough to include a floodplain.
    /// </summary>
    private const double WetlandShareOfLand = 0.75;

    /// <summary>
    /// Paints terrain from an already-built climate.
    ///
    /// The climate is passed in rather than built here because the province partition needs it too
    /// — province size follows habitability, which follows rainfall and temperature — and it now
    /// runs first. Building it in both places would be the "derived twice in two places" mistake
    /// the rest of this pipeline is careful to avoid.
    /// </summary>
    /// <param name="azgaar">
    /// An export whose biome map decides the vegetation, or null to classify it from the climate as
    /// before. Only the vegetation: beach, hills, mountains and the snow line are relief, and stay
    /// ours either way. See <see cref="AzgaarBiome"/>.
    /// </param>
    public static Result Classify(MapConfig cfg, float[] elevation, byte[] landMask,
        ClimateField climate, Rng rng, AzgaarImport? azgaar = null)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int sea = cfg.Limits.SeaLevelUpper;

        // The export's biome table, or null for a generated world. Resolved once here rather than
        // per pixel: it is a dozen entries and the per-pixel cost is one array index.
        var biomeTable = azgaar is null ? null : AzgaarBiome.Table(azgaar.World);
        var biomeRaster = azgaar?.Raster;
        var imported = new int[height];
        var reconciled = new int[height];

        // Which biome each correction was made under, per row so the loop stays lock-free. Worth
        // keeping rather than counting only the total: a single over-strict rule and a genuinely
        // cold climate model produce the same headline number and want opposite fixes.
        int kinds = Enum.GetValues<AzgaarBiome.Kind>().Length;
        var reconciledBy = new int[height * kinds];

        // Thresholds derived from this map's own distributions.
        float hills = LandPercentile(elevation, landMask, 1.0 - HillShareOfLand);
        float mountains = LandPercentile(elevation, landMask, 1.0 - MountainShareOfLand);
        float marsh = LandPercentile(climate.AnnualMm, landMask, WetlandShareOfLand);

        Console.WriteLine($"  terrain thresholds: hills {hills:F0}, mountains {mountains:F0}, " +
                          $"wetland rainfall {marsh:F0} mm");

        int BeachReach = Math.Max(1, (int)Math.Round(cfg.Scaled(BeachReachAtVanilla)));

        var coastDistance = DistanceToWater(landMask, width, height, BeachReach);

        // Independent fields so wetlands, forest and the lowland sub-variants do not all switch
        // at the same place. ck2rpg uses five separate simplex instances for exactly this.
        var wetNoise = new SimplexNoise(rng);
        var forestNoise = new SimplexNoise(rng);
        var edgeNoise = new SimplexNoise(rng);

        // Referenced to vanilla's province map, so a biome patch is a fixed pixel size.
        int reference = MapConfig.ReferenceProvinceWidth;
        double coarse = 16.0 / reference; // biome-sized patches
        double fine = 90.0 / reference;   // ragged edges between them

        // Patch shape comes from warped fBm rather than a single simplex octave. One octave
        // thresholded at a fixed level gives round, evenly-sized blobs of one characteristic
        // diameter, and three of those at three frequencies interleaving is what reads as
        // splotchiness. Warping the sample position and summing octaves gives regions with
        // arms, inlets and a range of sizes, which is what a real biome boundary looks like.
        var warpField = new SimplexNoise(rng);
        double warpFrequency = 11.0 / reference;
        double warpAmplitude = reference * 0.014;

        double Patch(SimplexNoise noise, double px, double py, double frequency)
        {
            double qx = warpField.Noise2D(px * warpFrequency, py * warpFrequency) * warpAmplitude;
            double qy = warpField.Noise2D(px * warpFrequency + 7.7, py * warpFrequency - 3.3)
                        * warpAmplitude;
            return Field.Fbm(noise, (px + qx) * frequency, (py + qy) * frequency, 4,
                       gain: 0.55) * 0.5 + 0.5;
        }

        var result = new TerrainClass[width * height];
        var zones = new KoppenClass[width * height];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                if (landMask[i] == 0)
                {
                    result[i] = TerrainClass.Sea;
                    zones[i] = KoppenClass.Ocean;
                    continue;
                }

                var zone = Koppen.Classify(climate.WarmC[i], climate.ColdC[i], climate.MeanC[i],
                    climate.AnnualMm[i], climate.SummerMm[i], climate.WinterMm[i]);

                var biome = AzgaarBiome.Kind.Unknown;
                if (biomeTable is not null)
                {
                    int id = biomeRaster!.BiomeAt(i);
                    if (id >= 0 && id < biomeTable.Length) biome = biomeTable[id];
                }

                bool stated = AzgaarBiome.HasOpinion(biome);
                if (stated) imported[y]++;

                // The zone travels with the vegetation or the map paints itself wrong.
                //
                // It is tempting to leave the climate map alone on the grounds that a biome is a
                // statement about what grows and not a second opinion on the Koppen class behind it.
                // That is true of the classification and false of everything downstream: the zone is
                // what picks the *material family* the ground is painted from, and what trees get
                // planted on it. Import the vegetation and leave the zone and a forest gets steppe
                // soil under its canopy, with nothing in either layer individually wrong. Only a
                // flat contradiction is corrected — see AzgaarBiome.Reconcile, which keeps our
                // finer answer wherever the two can both be true.
                var painted = AzgaarBiome.Reconcile(biome, zone);
                if (painted != zone)
                {
                    reconciled[y]++;
                    reconciledBy[y * kinds + (int)biome]++;
                }
                zone = painted;

                zones[i] = zone;

                // Aridity is regional and the export states it better than we infer it, so where it
                // has spoken it decides outright. Polar is the other way round — it is an altitude
                // fact, and our elevation resolves peaks inside a cell that Azgaar cannot — so the
                // two are ORed and a summit keeps its snow whatever biome the cell around it carries.
                bool arid = stated ? AzgaarBiome.IsArid(biome) : Koppen.IsArid(zone);
                bool polar = Koppen.IsPolar(zone) || AzgaarBiome.IsPolar(biome);

                float e = elevation[i];

                double nWet = Patch(wetNoise, x, y, coarse);
                double nForest = Patch(forestNoise, x, y, coarse * 1.7);
                double nEdge = edgeNoise.Unit(x * fine, y * fine);

                // Nor is farmland painted as a biome any more. It was assigned wherever temperate
                // ground was moist, low and inside a noise patch, which spreads it across whole
                // regions — but farmland is not a climate, it is what people have cleared, so it
                // belongs in a ring around a settlement and should be scarce even there. Drawing it
                // from moisture put fields across empty countryside. TerrainClass.Farmlands stays
                // in the vocabulary, like Floodplains below, for whenever it is placed from the
                // barony layer instead.

                // Terrain is deliberately no longer painted along river courses. Floodplains used
                // to be stamped on every pixel within FloodplainReach of a course, which at map
                // scale reads as a coloured seam tracing each river rather than as a valley floor.
                // The heightmap already carves the valley and rivers.png already draws the water,
                // so the paint added nothing but a stripe. TerrainClass.Floodplains stays in the
                // vocabulary — CK3 accepts it and it is a valid hand-authored choice — but nothing
                // assigns it now.

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
                    result[i] = arid ? TerrainClass.DesertMountains : TerrainClass.Mountains;
                    continue;
                }

                // Permanent snow, on high ground that is already polar at its own altitude. The
                // snow line therefore falls as latitude rises without anything having to say so —
                // the lapse rate in ClimateModel has already put it there.
                if (polar && e >= hills + wobble - (mountains - hills) * 0.35)
                {
                    result[i] = TerrainClass.Arctic;
                    continue;
                }

                if (e >= hills + wobble)
                {
                    result[i] = TerrainClass.Hills;
                    continue;
                }

                // Marsh: the wettest tenth of the map, on flat low ground, where it is warm enough
                // for the water to be liquid and the vegetation rank.
                //
                // Only where the export has not already drawn the marshes. Azgaar has a wetland
                // biome of its own and places it from its own moisture and depression model, so
                // running both would put our marshes beside its marshes and double the map's
                // wetland — and the rainfall percentile behind ours is a statement about this map's
                // own distribution, which an imported rainfall field has already changed the shape of.
                double lowness = (e - sea) / Math.Max(1.0, hills - sea);
                if (!stated && climate.AnnualMm[i] >= marsh && lowness < 0.25 && nWet > 0.72 &&
                    !polar && !arid)
                {
                    result[i] = TerrainClass.Wetlands;
                    continue;
                }

                result[i] = stated
                    ? AzgaarBiome.Terrain(biome, nForest)
                    : Koppen.Terrain(zone, nForest);
            }
        });

        if (biomeTable is not null)
        {
            long stated = 0, moved = 0, land = 0;
            foreach (int n in imported) stated += n;
            foreach (int n in reconciled) moved += n;
            foreach (byte m in landMask) if (m != 0) land++;

            // The shortfall is the coastline the two maps draw differently, plus whatever the export
            // left on a biome nobody could read. A few per cent is normal; a large number means the
            // heightmap and the JSON are not the same view, which CheckAlignment has already said.
            Console.WriteLine($"    vegetation follows the export on {stated} of {land} land pixels " +
                              $"({(land == 0 ? 0 : 100.0 * stated / land):F1}%)");

            // The second number is the honest measure of how far the climate model and the export
            // disagree, and it is worth watching rather than hiding: it should be small, because the
            // climate is already reanchored on the same temperature and rainfall the export derived
            // its biomes from. If it climbs, the reanchoring has drifted, not the biome table.
            Console.WriteLine($"    climate zone pulled to the export's vegetation on {moved} " +
                              $"({(land == 0 ? 0 : 100.0 * moved / land):F1}%) — the rest already agreed");

            if (moved > 0)
            {
                var byKind = new long[kinds];
                for (int y = 0; y < height; y++)
                    for (int k = 0; k < kinds; k++) byKind[k] += reconciledBy[y * kinds + k];

                string worst = string.Join(", ", Enumerable.Range(0, kinds)
                    .Where(k => byKind[k] > 0)
                    .OrderByDescending(k => byKind[k])
                    .ThenBy(k => k)
                    .Take(4)
                    .Select(k => $"{(AzgaarBiome.Kind)k} {100.0 * byKind[k] / moved:F0}%"));

                Console.WriteLine($"      of those: {worst}");
            }
        }

        return new Result { Terrain = result, Climate = zones, Field = climate };
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
    /// Chebyshev distance from each land pixel to the nearest water, capped at
    /// <paramref name="maxDistance"/>. A capped dilation is far cheaper than a full distance
    /// transform and the beach only ever needs the first few pixels.
    ///
    /// Water reads 0 and anything past the cap reads <c>maxDistance + 1</c>. Cylindrical in x, so
    /// a coast running off one edge of the map is still a coast.
    /// </summary>
    internal static byte[] DistanceToWater(byte[] landMask, int width, int height, int maxDistance)
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
        TerrainClass.Oasis => "oasis",
        TerrainClass.Arctic => "taiga",           // no arctic terrain type; taiga is the closest
        _ => "plains",
    };
}
