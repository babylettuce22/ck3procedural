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
        double MaxScale);

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
            [(TerrainClass.Wetlands, 40), (TerrainClass.Floodplains, 26)], 0.55, 1.45),

        new("steppe_bush_01_generator.txt", "steppe_bush_01_generator", "grass_layer",
            ["steppe_bush_01_mesh"],
            [(TerrainClass.Steppe, 9), (TerrainClass.Drylands, 5)], 0.25, 0.75),

        // Mediterranean / subtropical uplands. Vanilla fixes cypress scale at exactly 1.00.
        new("tree_cypress_01_generator_1.txt", "tree_cypress_01_generator_1", "tree_high_layer",
            ["tree_cypress_01_a_mesh", "tree_cypress_01_b_mesh", "tree_cypress_01_c_mesh"],
            [(TerrainClass.Drylands, 3), (TerrainClass.Hills, 2.5)], 1.0, 1.0),

        // Jungle. Vanilla runs the c and d variants at 1,227 against 38,113 — c is the occasional
        // accent, not a co-equal variant — so the densities keep that ~1:31 ratio.
        new("tree_jungle_01_c_generator_1.txt", "tree_jungle_01_c_generator_1", "tree_high_layer",
            ["tree_jungle_01_c_mesh"],
            [(TerrainClass.Jungle, 1)], 0.70, 1.10),
        new("tree_jungle_01_d_generator_1.txt", "tree_jungle_01_d_generator_1", "tree_high_layer",
            ["tree_jungle_01_d_mesh"],
            [(TerrainClass.Jungle, 30)], 0.50, 1.00),

        // Scattered broadleaf on open ground.
        new("tree_leaf_01_single_generator_1.txt", "tree_leaf_01_single_generator_1",
            "tree_high_layer", ["tree_leaf_01_single_a_mesh"],
            [(TerrainClass.Plains, 1.6), (TerrainClass.Farmlands, 1.2)], 0.50, 0.90),

        // Temperate broadleaf forest — vanilla's densest group by a wide margin.
        new("tree_leaf_2_high_generator_1.txt", "tree_leaf_2_high_generator_1", "tree_high_layer",
            ["tree_leaf_01_a_mesh", "tree_leaf_01_b_mesh"],
            [(TerrainClass.Forest, 8), (TerrainClass.Hills, 2)], 0.40, 0.80),
        new("tree_leaf_high_generator_1.txt", "tree_leaf_high_generator_1", "tree_high_layer",
            ["tree_leaf_01_a_mesh"],
            [(TerrainClass.Forest, 26), (TerrainClass.Hills, 4)], 0.60, 1.00),
        new("tree_leaf_high_generator_2.txt", "tree_leaf_high_generator_2", "tree_high_layer",
            ["tree_leaf_01_b_mesh"],
            [(TerrainClass.Forest, 12), (TerrainClass.Farmlands, 1.5)], 0.21, 0.60),
        new("tree_leaf_high_generator_3.txt", "tree_leaf_high_generator_3", "tree_high_layer",
            ["tree_leaf_01_c_mesh"],
            [(TerrainClass.Forest, 16), (TerrainClass.Plains, 1.2)], 0.56, 1.04),

        // Palms: tropical shore, a token scatter on open desert, and the oasis itself — which is
        // the one place they should actually stand thick, now that oases are a terrain class
        // rather than a figure of speech.
        new("tree_palm_generator_1.txt", "tree_palm_generator_1", "tree_high_layer",
            ["tree_palm_01_a_mesh"],
            [(TerrainClass.Beach, 3), (TerrainClass.Desert, 0.2), (TerrainClass.Oasis, 22)],
            0.50, 0.90),

        // Conifers. Vanilla fixes both pine_01_a (1.00) and pine_01_b (0.40).
        new("tree_pine_01_a_generator_1.txt", "tree_pine_01_a_generator_1", "tree_high_layer",
            ["tree_pine_single_01_a_mesh", "tree_pine_single_01_b_mesh", "tree_pine_single_01_c_mesh"],
            [(TerrainClass.Taiga, 6), (TerrainClass.Forest, 3)], 1.0, 1.0),
        new("tree_pine_01_b_generator_1.txt", "tree_pine_01_b_generator_1", "tree_high_layer",
            ["tree_pine_01_b_mesh"],
            [(TerrainClass.Taiga, 24), (TerrainClass.Forest, 6), (TerrainClass.Hills, 2)], 0.40, 0.40),
        new("tree_pine_impassable_01_a_generator_1.txt", "tree_pine_impassable_01_a_generator_1",
            "tree_high_layer", ["tree_pine_impassable_01_a_mesh"],
            [(TerrainClass.Mountains, 3), (TerrainClass.Taiga, 4)], 0.40, 0.40),

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

    public static void WriteAll(string modDir, MapConfig cfg, TerrainClass[] terrain, Rng rng)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "map_object_data", "generated");
        Directory.CreateDirectory(dir);

        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        long total = 0;

        foreach (var generator in Generators)
        {
            // One bucket per mesh variant; each becomes its own object block, as vanilla does.
            var buckets = new List<(float X, float Z, float Angle, float Scale)>[generator.Meshes.Length];
            for (int i = 0; i < buckets.Length; i++) buckets[i] = [];

            if (generator.Habitat.Length > 0)
            {
                // Probability per pixel, indexed by terrain class for a cheap inner loop.
                var chance = new double[Enum.GetValues<TerrainClass>().Length];
                foreach (var (t, density) in generator.Habitat) chance[(int)t] = density / 1000.0;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        double p = chance[(int)terrain[y * width + x]];
                        if (p <= 0 || rng.NextDouble() >= p) continue;

                        // Jitter inside the pixel so instances do not sit on a lattice.
                        float px = (float)(x + rng.NextDouble());

                        // Image rows run top-down; the map's Z axis runs bottom-up.
                        float pz = (float)(height - y - rng.NextDouble());

                        float scale = (float)(generator.MinScale +
                            rng.NextDouble() * (generator.MaxScale - generator.MinScale));

                        buckets[rng.Int(0, buckets.Length - 1)]
                            .Add((px, pz, (float)(rng.NextDouble() * Math.Tau), scale));
                    }
                }
            }

            Write(Path.Combine(dir, generator.File), generator, buckets);
            foreach (var bucket in buckets) total += bucket.Count;
        }

        Console.WriteLine($"  trees: {total:N0} instances across {Generators.Length} generators");
    }

    private static void Write(string path, Generator generator,
        List<(float X, float Z, float Angle, float Scale)>[] buckets)
    {
        int capacity = 256;
        foreach (var bucket in buckets) capacity += bucket.Count * 96;

        var sb = new StringBuilder(capacity);
        var culture = CultureInfo.InvariantCulture;

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
