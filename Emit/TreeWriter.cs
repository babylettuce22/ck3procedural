using System.Globalization;
using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes gfx/map/map_object_data/generated — the foliage instances CK3 scatters across the map.
///
/// These are per-map data addressed in world coordinates, and replace_path="gfx/map/map_object_data"
/// drops vanilla's, so shipping nothing means a world with no trees at all anywhere. ck2rpg writes
/// all 18 files with <c>count=0</c>, which is exactly that; there was nothing to port, so this is
/// built from vanilla's format instead.
///
/// Format, measured from vanilla's own files:
/// <code>
/// object={
///     name="tree_palm_generator_1_0"
///     render_pass=Map
///     clamp_to_water_level=no
///     generated_content=yes
///     layer="tree_high_layer"
///     pdxmesh="tree_palm_01_a_mesh"
///     count=12739
///     transform="X Y Z  qx qy qz qw  sx sy sz
/// ...one line per instance...
/// "}
/// </code>
/// UTF-8 with BOM, tab-indented, and every instance is one line inside a single quoted string.
///
/// Coordinates are **provinces-space**, the same frame as the locators: measured across vanilla's
/// 138,645-instance leaf generator, x spans 81..8707 and z spans 106..4454 against
/// WORLD_EXTENTS_X 9215 and WORLD_EXTENTS_Z 4607. Y is always exactly 0 — the engine drops each
/// instance onto the terrain. Z runs bottom-up while image rows run top-down, hence the flip.
///
/// The rotation is always about the vertical axis, so the quaternion is (0, sin(t/2), 0, cos(t/2)),
/// and the scale is uniform — vanilla's mean is 0.80.
///
/// <b>On layers, and why steppe used to look bare.</b> The layer is not just a fade curve, it is a
/// graphics-settings gate. <c>grass_layer</c> is <c>fade_out=6 masks="high"</c>, so anything on it
/// is drawn only on High foliage and only when the camera is almost on the ground; the tree layers
/// are <c>fade_out=9 masks="low|medium|high"</c>. Vanilla puts its one steppe prop on
/// <c>grass_layer</c> and gets away with it because vanilla's steppe is a corner of the map. On a
/// generated world where steppe can be a third of the land, it means the biome renders as bare
/// terrain texture at every zoom anyone plays at.
///
/// The answer is not to move that bush up a layer, which was tried and looked far worse than bare
/// ground did — the mesh is a flat 26x28-unit ground patch and reads as debris strewn over the map
/// at any distance a player looks from. What steppe needs at that range is an upright silhouette,
/// so the scrub habitats hang off the lone-tree generators instead, sparse enough to stay steppe.
///
/// Generators are still grouped by file rather than owning one each, because several vanilla files
/// legitimately hold more than one <c>object={}</c> block.
///
/// <b>On climate.</b> Habitat used to be terrain alone, and terrain is too coarse a thing to
/// choose a species from: <c>Forest</c> is every wood on the map, so a boreal one was scattered
/// with the same oaks as a temperate one and pine was confined to the narrow <c>Taiga</c> strip the
/// classifier paints inside the subarctic. Each generator now also declares what kind of plant it
/// draws — see <see cref=Flora/> — and the Koppen class the terrain was derived from scales its
/// density per pixel. The species boundaries that result are gradients rather than lines, because
/// the two curves overlap through the continental belt instead of handing off at one value.
/// </summary>
public static class TreeWriter
{
    private sealed record Generator(
        string File,
        string Prefix,
        string Layer,
        string[] Meshes,
        (TerrainClass Terrain, double Density)[] Habitat,
        double MinScale,
        double MaxScale,
        Flora Plant = Flora.Insensitive,
        int Footprint = 0,
        float MaxRelief = 0f);

    /// <summary>
    /// What kind of plant a generator draws, which is the only thing needed to decide how the
    /// climate at a pixel scales its density.
    ///
    /// Terrain alone cannot make this call. <see cref="TerrainClass.Forest"/> is one class covering
    /// everything from a Norwegian valley to a Galician oak wood, and the terrain painter only
    /// splits off <see cref="TerrainClass.Taiga"/> inside the subarctic — so a boreal forest and a
    /// temperate one arrive here indistinguishable, and every one of them was getting the same
    /// broadleaf scatter. The Koppen class the terrain was derived from still knows the difference.
    /// </summary>
    private enum Flora
    {
        /// <summary>Ground cover that follows its terrain and ignores climate — reeds, scrub.</summary>
        Insensitive,
        /// <summary>Pines and firs: the further north and colder, the more of them.</summary>
        Conifer,
        /// <summary>Deciduous broadleaf: temperate, and all but absent from the boreal north.</summary>
        Broadleaf,
        /// <summary>Palms, jungle and cypress — frost-intolerant, and absent from cold coasts.</summary>
        Warm,
    }

    /// <summary>
    /// The density multiplier for one kind of plant in one climate.
    ///
    /// This is what puts pine in the north rather than everywhere: conifer and broadleaf are given
    /// overlapping curves rather than a hard boundary, so the treeline reads as a mixed belt across
    /// the humid continental band — pine dominant above it, oak dominant below — instead of a line
    /// that snaps from one mesh to the other along the terrain edge.
    ///
    /// Broadleaf keeps a token presence in the subarctic (birch and aspen do grow there) and
    /// conifer keeps one in the Mediterranean and the tropics (there are pines on both), because
    /// zero anywhere makes the transition visible as an edge.
    /// </summary>
    private static double ClimateFactor(Flora flora, KoppenClass climate) => flora switch
    {
        Flora.Conifer => climate switch
        {
            KoppenClass.Subarctic => 1.00,
            KoppenClass.Tundra or KoppenClass.IceCap => 1.00,
            KoppenClass.HumidContinental => 0.85,
            KoppenClass.ColdDesert or KoppenClass.ColdSteppe => 0.55,
            KoppenClass.Oceanic => 0.35,
            KoppenClass.Mediterranean => 0.15,
            KoppenClass.HumidSubtropical => 0.08,
            _ => 0.0,
        },

        Flora.Broadleaf => climate switch
        {
            KoppenClass.Oceanic or KoppenClass.HumidSubtropical => 1.00,
            KoppenClass.Mediterranean => 0.80,
            KoppenClass.HumidContinental => 0.55,
            KoppenClass.TropicalMonsoon or KoppenClass.TropicalSavanna => 0.70,
            KoppenClass.TropicalRainforest => 0.50,
            KoppenClass.HotSteppe => 0.50,
            KoppenClass.ColdSteppe => 0.30,
            KoppenClass.Subarctic => 0.08,
            _ => 0.0,
        },

        Flora.Warm => climate switch
        {
            KoppenClass.TropicalRainforest or KoppenClass.TropicalMonsoon
                or KoppenClass.TropicalSavanna => 1.00,
            KoppenClass.HotDesert or KoppenClass.HotSteppe => 1.00,
            KoppenClass.Mediterranean or KoppenClass.HumidSubtropical => 1.00,
            KoppenClass.Oceanic => 0.35,
            KoppenClass.ColdDesert or KoppenClass.ColdSteppe => 0.25,
            _ => 0.0,
        },

        // Insensitive: ground cover follows its terrain and nothing else.
        _ => 1.0,
    };

    /// <summary>
    /// Every field here was read off vanilla's own generator files rather than chosen.
    ///
    /// <b>Meshes</b> — several generators are not one object but two or three, named
    /// <c>_0</c>, <c>_1</c>, <c>_2</c> within the same file, each drawing a different mesh variant
    /// of the same plant and holding roughly a third of the instances. That is where vanilla's
    /// silhouette variety comes from, so instances are split across the variants the same way.
    /// ck2rpg's table lists only the <c>_0</c> mesh of each, which would have lost it.
    ///
    /// <b>Scale</b> — measured per generator, and it matters: several are *fixed*, not random.
    /// tree_pine_01_b is always 0.40 and tree_cypress always 1.00, so scattering them over a
    /// generic 0.65-1.35 range would have drawn those meshes at two to three times their intended
    /// size. Ranges below are centred on vanilla's measured mean, tightened at the extremes where
    /// vanilla has a few degenerate near-zero instances.
    ///
    /// <b>Density</b> is instances per 1000 province pixels of that terrain class, tuned so the
    /// total lands near vanilla's ~468,000 once scaled for how much less land we have.
    /// </summary>
    private static readonly Generator[] Generators =
    [
        // Ground cover. Reeds mean 1.00 across three mesh variants.
        new("reeds_01_generator_1.txt", "reeds_01_generator_1", "grass_layer",
            ["reeds_06_grass_mesh", "reeds_07_grass_mesh", "reeds_01_tall_grass_mesh"],
            [(TerrainClass.Wetlands, 55), (TerrainClass.Floodplains, 36)], 0.55, 1.45),

        // Steppe scrub, exactly as vanilla draws it: close-up ground cover on grass_layer, at
        // vanilla's own measured scale.
        new("steppe_bush_01_generator.txt", "steppe_bush_01_generator", "grass_layer",
            ["steppe_bush_01_mesh"],
            [(TerrainClass.Steppe, 18), (TerrainClass.Drylands, 9)], 0.25, 0.75),

        // There used to be a second pass here that put the same bush on tree_high_layer, to give
        // steppe something visible at play zoom — see the class remarks on why grass_layer alone
        // leaves it bare. It has been removed, because the mesh cannot do that job: measured off
        // the geometry, steppe_bush_01 is 26 x 28 units in plan and 4.1 units tall across 96
        // vertices — a couple of crossed quads laid on the ground, four times wider than a pine is
        // and barely taller than the grass. It is a ground *patch*, authored to be looked down on
        // from a metre away, and vanilla accordingly only ever draws it on the close-up layer.
        //
        // On the tree layer, at map zoom, those crossed quads read edge-on as flat tan slabs lying
        // in the grass — the map looked strewn with fallen logs, most visibly across the cold
        // steppe of the north where the pass was densest. Shrinking it would only have made the
        // slabs smaller. The horizon silhouette comes from the upright scatter below instead.

        // Mediterranean / subtropical uplands. Vanilla fixes cypress scale at exactly 1.00.
        new("tree_cypress_01_generator_1.txt", "tree_cypress_01_generator_1", "tree_high_layer",
            ["tree_cypress_01_a_mesh", "tree_cypress_01_b_mesh", "tree_cypress_01_c_mesh"],
            [(TerrainClass.Drylands, 4), (TerrainClass.Hills, 3.5)], 1.0, 1.0, Flora.Warm),

        // Jungle. Vanilla runs the c and d variants at 1,227 against 38,113 — c is the occasional
        // accent, not a co-equal variant — so the densities keep that ~1:31 ratio.
        new("tree_jungle_01_c_generator_1.txt", "tree_jungle_01_c_generator_1", "tree_high_layer",
            ["tree_jungle_01_c_mesh"],
            [(TerrainClass.Jungle, 1.4)], 0.70, 1.10, Flora.Warm),
        new("tree_jungle_01_d_generator_1.txt", "tree_jungle_01_d_generator_1", "tree_high_layer",
            ["tree_jungle_01_d_mesh"],
            [(TerrainClass.Jungle, 42)], 0.50, 1.00, Flora.Warm),

        // Scattered broadleaf on open ground. The steppe entry is what replaces the bush patch
        // that used to stand in for a horizon: a lone tree is a real upright silhouette, and
        // tree_leaf_01_single is the one broadleaf mesh vanilla authored to stand by itself.
        new("tree_leaf_01_single_generator_1.txt", "tree_leaf_01_single_generator_1",
            "tree_high_layer", ["tree_leaf_01_single_a_mesh"],
            [(TerrainClass.Plains, 2.4), (TerrainClass.Farmlands, 1.6), (TerrainClass.Steppe, 2.0),
             (TerrainClass.Drylands, 1.2)], 0.50, 0.90, Flora.Broadleaf),

        // Temperate broadleaf forest — vanilla's densest group by a wide margin, and the group that
        // used to cover the boreal north as well, because nothing here could tell the two apart.
        // The Broadleaf tag is what now keeps them out of the subarctic; their densities relative
        // to each other are vanilla's still, carrying the global increase.
        new("tree_leaf_2_high_generator_1.txt", "tree_leaf_2_high_generator_1", "tree_high_layer",
            ["tree_leaf_01_a_mesh", "tree_leaf_01_b_mesh"],
            [(TerrainClass.Forest, 12), (TerrainClass.Hills, 3)], 0.40, 0.80, Flora.Broadleaf),
        new("tree_leaf_high_generator_1.txt", "tree_leaf_high_generator_1", "tree_high_layer",
            ["tree_leaf_01_a_mesh"],
            [(TerrainClass.Forest, 36), (TerrainClass.Hills, 6)], 0.60, 1.00, Flora.Broadleaf),
        new("tree_leaf_high_generator_2.txt", "tree_leaf_high_generator_2", "tree_high_layer",
            ["tree_leaf_01_b_mesh"],
            [(TerrainClass.Forest, 17), (TerrainClass.Farmlands, 2)], 0.21, 0.60, Flora.Broadleaf),
        new("tree_leaf_high_generator_3.txt", "tree_leaf_high_generator_3", "tree_high_layer",
            ["tree_leaf_01_c_mesh"],
            [(TerrainClass.Forest, 22), (TerrainClass.Plains, 1.8)], 0.56, 1.04, Flora.Broadleaf),

        // Palms: tropical shore, a token scatter on open desert, and the oasis itself — which is
        // the one place they should actually stand thick, now that oases are a terrain class
        // rather than a figure of speech. Tagged Warm, which also stops palms standing on the
        // beach of an arctic fjord: Beach is one terrain class from the equator to the pole.
        new("tree_palm_generator_1.txt", "tree_palm_generator_1", "tree_high_layer",
            ["tree_palm_01_a_mesh"],
            [(TerrainClass.Beach, 4), (TerrainClass.Desert, 0.3), (TerrainClass.Oasis, 26)],
            0.50, 0.90, Flora.Warm),

        // Conifers, and the reason for the climate table above. These used to be effectively a
        // Taiga-only group, and Taiga is painted only inside the subarctic and only where the
        // forest patch noise clears 0.25 — so every boreal wood that came out as plain Forest was
        // scattered with oaks. They now carry Forest, Hills and Plains habitats as heavy as the
        // broadleaf ones, and the Conifer factor decides which of the two wins at each pixel: pine
        // north of the humid-continental belt, oak south of it, both through the middle.
        //
        // Vanilla fixes both pine_01_a (1.00) and pine_01_b (0.40); those are not free to retune.
        new("tree_pine_01_a_generator_1.txt", "tree_pine_01_a_generator_1", "tree_high_layer",
            ["tree_pine_single_01_a_mesh", "tree_pine_single_01_b_mesh", "tree_pine_single_01_c_mesh"],
            [(TerrainClass.Taiga, 14), (TerrainClass.Forest, 12), (TerrainClass.Hills, 2),
             (TerrainClass.Plains, 1.2), (TerrainClass.Steppe, 2.5)], 1.0, 1.0, Flora.Conifer),
        new("tree_pine_01_b_generator_1.txt", "tree_pine_01_b_generator_1", "tree_high_layer",
            ["tree_pine_01_b_mesh"],
            [(TerrainClass.Taiga, 46), (TerrainClass.Forest, 30), (TerrainClass.Hills, 4),
             (TerrainClass.Plains, 1.5)], 0.40, 0.40, Flora.Conifer),
        // The impassable mesh is not a tree. It is a piece of forest scenery — a standing pine
        // with a fallen log beside it, 23 x 21 units in plan, which is nine province pixels across
        // even at vanilla's fixed 0.40. Vanilla gets away with it because it packs them: measured
        // over its own file, 30,406 instances sit in 493 cells of 64 pixels, about 62 to a cell,
        // dense enough that the logs read as forest floor. Scattered thinly, each one is instead a
        // single log lying by itself in open country, which is exactly how it looked.
        //
        // So it is kept, at a third of the density it had on the two terrains it keeps, and only
        // where the ground under the whole footprint is level — a log half-buried in a hillside or
        // cantilevered off a ridge is the other half of why these read as debris. Measured against
        // a generated heightmap, a 14-unit tolerance over this footprint accepts a little over
        // half of all land, so the two together leave roughly a sixth of what was there.
        //
        // Mountains are dropped outright rather than left to the flatness test: there is no ground
        // that level up there, so it would only ever be paying for the scan to reject them.
        new("tree_pine_impassable_01_a_generator_1.txt", "tree_pine_impassable_01_a_generator_1",
            "tree_high_layer", ["tree_pine_impassable_01_a_mesh"],
            [(TerrainClass.Taiga, 2.5), (TerrainClass.Forest, 1.5)],
            0.40, 0.40, Flora.Conifer, Footprint: 5, MaxRelief: 14f),

        // Sakura is region flavour with no generated equivalent, but the files still have to exist
        // so vanilla's copies — which are placed over Japan — are displaced rather than loaded.
        new("tree_sakura_01_generator.txt", "tree_sakura_01_generator", "tree_high_layer",
            ["tree_sakura_01_mesh"], [], 1.0, 1.0),
        new("tree_sakura_02_generator.txt", "tree_sakura_02_generator", "tree_high_layer",
            ["tree_sakura_02_mesh"], [], 1.0, 1.0),
        new("tree_sakura_03_generator.txt", "tree_sakura_03_generator", "tree_high_layer",
            ["tree_sakura_03_mesh"], [], 1.0, 1.0),
        new("tree_sakura_forest_generator.txt", "tree_sakura_forest_generator", "tree_high_layer",
            ["tree_sakura_01_mesh", "tree_sakura_02_mesh", "tree_sakura_03_mesh"], [], 1.0, 1.0),
    ];

    public static void WriteAll(string modDir, MapConfig cfg, TerrainClass[] terrain,
        KoppenClass[] climate, float[] elevation, Rng rng)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "map_object_data", "generated");
        Directory.CreateDirectory(dir);

        // One canopy field for the whole map, shared by every generator, so a stand of oak and the
        // birch scattered through it thin out together instead of each rolling its own noise and
        // averaging back into an even carpet.
        var field = CanopyField.Create(cfg);

        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        long total = 0, drowned = 0, steep = 0;

        // A file holds one object block per generator that names it, in table order.
        var byFile = new Dictionary<string,
            List<(Generator Generator, List<(float X, float Z, float Angle, float Scale)>[] Buckets)>>();

        foreach (var generator in Generators)
        {
            // One bucket per mesh variant; each becomes its own object block, as vanilla does.
            var buckets = new List<(float X, float Z, float Angle, float Scale)>[generator.Meshes.Length];
            for (int i = 0; i < buckets.Length; i++) buckets[i] = [];

            if (generator.Habitat.Length > 0 && cfg.TreeDensity > 0)
            {
                // Probability per pixel, indexed by terrain class for a cheap inner loop.
                var chance = new double[Enum.GetValues<TerrainClass>().Length];
                foreach (var (t, density) in generator.Habitat)
                    chance[(int)t] = density * cfg.TreeDensity / 1000.0;

                // And the climate multiplier, resolved once per class rather than per pixel: this
                // is the innermost loop on the map and runs the full raster once per generator.
                var factor = new double[Enum.GetValues<KoppenClass>().Length];
                for (int k = 0; k < factor.Length; k++)
                    factor[k] = ClimateFactor(generator.Plant, (KoppenClass)k);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;
                        double p = chance[(int)terrain[i]] * factor[(int)climate[i]];

                        // Open ground clumps into groves and closed ground only thins; the two read
                        // very differently and a single factor cannot do both. Insensitive plants —
                        // the ones that are not really canopy — opt out entirely.
                        if (generator.Plant != Flora.Insensitive)
                        {
                            double canopy = CanopyField.At(field, x, y);
                            p *= IsOpenCountry(terrain[i])
                                ? CanopyField.GroveFactor(canopy)
                                : CanopyField.ScatterFactor(canopy);
                        }

                        if (p <= 0 || rng.NextDouble() >= p) continue;

                        // Jitter inside the pixel so instances do not sit on a lattice.
                        double jx = x + rng.NextDouble(), jy = y + rng.NextDouble();

                        // The terrain class is a property of the whole province pixel and a coastal
                        // one is called land on a half-or-better vote, so the jitter can land on a
                        // heightmap pixel that is genuinely under water. See ScatterGround.
                        if (!ScatterGround.IsDryLand(elevation, cfg, jx, jy)) { drowned++; continue; }

                        // Wide scenery meshes need the ground level under all of themselves, not
                        // just under their origin.
                        if (generator.Footprint > 0 && !ScatterGround.IsFlatEnough(
                                elevation, cfg, x, y, generator.Footprint, generator.MaxRelief))
                        {
                            steep++;
                            continue;
                        }

                        float px = (float)jx;

                        // Image rows run top-down; the map's Z axis runs bottom-up.
                        float pz = (float)(height - jy);

                        float scale = (float)(generator.MinScale +
                            rng.NextDouble() * (generator.MaxScale - generator.MinScale));

                        buckets[rng.Int(0, buckets.Length - 1)]
                            .Add((px, pz, (float)(rng.NextDouble() * Math.Tau), scale));
                    }
                }
            }

            if (!byFile.TryGetValue(generator.File, out var blocks))
                byFile[generator.File] = blocks = [];
            blocks.Add((generator, buckets));

            foreach (var bucket in buckets) total += bucket.Count;
        }

        foreach (var (file, blocks) in byFile) Write(Path.Combine(dir, file), blocks);

        Console.WriteLine($"  trees: {total:N0} instances across {Generators.Length} generators " +
                          $"in {byFile.Count} files ({drowned:N0} rejected below the waterline, " +
                          $"{steep:N0} on ground too steep for their footprint)");
    }

    /// <summary>
    /// Ground where trees gather into stands rather than spreading evenly — the classes whose real
    /// counterparts carry copses and windbreaks instead of forest.
    /// </summary>
    private static bool IsOpenCountry(TerrainClass t)
        => t is TerrainClass.Plains or TerrainClass.Farmlands or TerrainClass.Steppe
             or TerrainClass.Oasis;

    private static void Write(string path,
        List<(Generator Generator, List<(float X, float Z, float Angle, float Scale)>[] Buckets)> blocks)
    {
        int capacity = 256 * blocks.Count;
        foreach (var (_, buckets) in blocks)
            foreach (var bucket in buckets) capacity += bucket.Count * 96;

        var sb = new StringBuilder(capacity);
        var culture = CultureInfo.InvariantCulture;

        foreach (var (generator, buckets) in blocks)
        for (int b = 0; b < buckets.Length; b++)
        {
            var instances = buckets[b];

            sb.Append("object={\n");
            sb.Append($"\tname=\"{generator.Prefix}_{b}\"\n");
            sb.Append("\trender_pass=Map\n");
            sb.Append("\tclamp_to_water_level=no\n");
            sb.Append("\tgenerated_content=yes\n");
            sb.Append($"\tlayer=\"{generator.Layer}\"\n");
            sb.Append($"\tpdxmesh=\"{generator.Meshes[b]}\"\n");
            sb.Append($"\tcount={instances.Count}\n");
            sb.Append("\ttransform=\"");

            for (int i = 0; i < instances.Count; i++)
            {
                var (x, z, angle, scale) = instances[i];

                // Rotation about the vertical axis only, so the quaternion has no X or Z part.
                double qy = Math.Sin(angle / 2.0);
                double qw = Math.Cos(angle / 2.0);

                if (i > 0) sb.Append('\n');
                sb.Append(x.ToString("F6", culture)).Append(" 0.000000 ")
                  .Append(z.ToString("F6", culture)).Append(" 0.000000 ")
                  .Append(qy.ToString("F6", culture)).Append(" 0.000000 ")
                  .Append(qw.ToString("F6", culture)).Append(' ')
                  .Append(scale.ToString("F6", culture)).Append(' ')
                  .Append(scale.ToString("F6", culture)).Append(' ')
                  .Append(scale.ToString("F6", culture));
            }

            sb.Append("\"}\n");
        }

        ParadoxText.WriteBom(path, sb.ToString());
    }
}
