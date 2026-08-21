using System.Globalization;
using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes gfx/map/map_object_data/animals.txt — the grazing herds vanilla scatters over the map.
///
/// Like the foliage in TreeWriter this is per-map data that replace_path drops, and
/// nothing regenerated it, so generated worlds had no animals at all. Unlike the foliage it is
/// worth having for a reason beyond parity: animals sit on unit_layer, which is
/// fade_out=9 masks="" — no graphics-settings gate and visible out to the same distance as
/// armies. They are the only decorative map object that is reliably drawn at the zoom people
/// actually play at.
/// </summary>
public static class AnimalWriter
{
    private sealed record Species(
        string Name,
        Variant[] Variants,
        bool ClampToWater,
        (TerrainClass Terrain, double Density)[] Habitat,
        int MinHerd,
        int MaxHerd,
        double Spread,
        double MinScale,
        double MaxScale);

    private sealed record Variant(string Entity, int Weight, bool NeedsOpenGround = false);

    private static readonly Species[] SpeciesTable =
    [
        // Flocks, and the densest of the three — sheep are the animal of settled land.
        new("sheep",
            [new("map_mpo_sheep_female_01_entity", 149), new("map_mpo_sheep_female_02_entity", 147),
             new("map_mpo_sheep_male_01_entity", 46), new("map_mpo_sheep_male_02_entity", 47)],
            false,
            [(TerrainClass.Farmlands, 26), (TerrainClass.Hills, 15), (TerrainClass.Plains, 13),
             (TerrainClass.Steppe, 7), (TerrainClass.Drylands, 4)],
            5, 9, 15.0, 0.44, 0.57),

        // Deliberately lopsided toward steppe: wild horse herds.
        new("horse",
            [new("unit_horse_01_a_eating_grass_entity", 20), new("unit_horse_01_b_eating_grass_entity", 20),
             new("unit_horse_01_c_eating_grass_entity", 20), new("unit_horse_01_d_eating_grass_entity", 20),
             new("unit_horse_01_a_galloping_entity", 8, true), new("unit_horse_01_b_galloping_entity", 8, true),
             new("unit_horse_01_c_galloping_entity", 7, true), new("unit_horse_01_d_galloping_entity", 8, true)],
            false,
            [(TerrainClass.Steppe, 30), (TerrainClass.Plains, 9), (TerrainClass.Farmlands, 6),
             (TerrainClass.Drylands, 4)],
            2, 5, 10.0, 0.42, 0.57),

        // Solitary jungle/floodplain elephants.
        new("elephant",
            [new("elephant_entity", 1)],
            true,
            [(TerrainClass.Jungle, 8), (TerrainClass.Floodplains, 3)],
            1, 1, 0.0, 0.228709, 0.228709),
    ];

    private static readonly TerrainClass[] OpenGround =
        [TerrainClass.Steppe, TerrainClass.Plains, TerrainClass.Farmlands, TerrainClass.Drylands];

    private const int GallopClearance = 24;
    private const int GallopStep = 4;
    private const float GallopRelief = 25f;

    public static void WriteAll(string modDir, MapConfig cfg, TerrainClass[] terrain,
        float[] elevation, Rng rng)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "map_object_data");
        Directory.CreateDirectory(dir);

        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        var sb = new StringBuilder(1 << 16);
        long total = 0;
        int herds = 0, galloping = 0;

        bool enabled = cfg.EnableAnimals && cfg.AnimalDensity > 0;
        double densityMultiplier = enabled ? cfg.AnimalDensity : 0.0;
        // Scaled with the map for the same reason the trees are: the meshes are authored against
        // vanilla's world size, and an unscaled herd on a larger map reads as vermin.
        double scaleMultiplier = cfg.AnimalScale * cfg.MapScale;

        foreach (var species in SpeciesTable)
        {
            var buckets = new List<(float X, float Z, float Angle, float Scale)>[species.Variants.Length];
            for (int i = 0; i < buckets.Length; i++) buckets[i] = [];

            if (enabled)
            {
                int weightOpen = 0, weightPenned = 0;
                foreach (var variant in species.Variants)
                {
                    bool allowVariant = !variant.NeedsOpenGround || cfg.EnableGallopingHorses;
                    if (allowVariant)
                    {
                        weightOpen += variant.Weight;
                        if (!variant.NeedsOpenGround) weightPenned += variant.Weight;
                    }
                }

                bool gated = weightPenned != weightOpen;

                var chance = new double[Enum.GetValues<TerrainClass>().Length];
                foreach (var (t, density) in species.Habitat)
                {
                    chance[(int)t] = (density * densityMultiplier) / 1_000_000.0;
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        double p = chance[(int)terrain[y * width + x]];
                        if (p <= 0 || rng.NextDouble() >= p) continue;

                        bool open = gated && cfg.EnableGallopingHorses && IsOpenGround(terrain, elevation, cfg, x, y);
                        int weightTotal = open ? weightOpen : weightPenned;

                        if (weightTotal <= 0) continue;

                        herds++;
                        if (open) galloping++;

                        int size = rng.Int(species.MinHerd, species.MaxHerd);

                        for (int member = 0; member < size; member++)
                        {
                            if (!Place(species, x, y, elevation, cfg, rng, out float px, out float pz))
                                continue;

                            float baseScale = (float)(species.MinScale +
                                rng.NextDouble() * (species.MaxScale - species.MinScale));
                            float scale = (float)(baseScale * scaleMultiplier);

                            buckets[PickVariant(species, weightTotal, open, cfg.EnableGallopingHorses, rng)]
                                .Add((px, pz, (float)(rng.NextDouble() * Math.Tau), scale));
                        }
                    }
                }
            }

            foreach (var bucket in buckets) total += bucket.Count;
            Append(sb, species, buckets);
        }

        ParadoxText.WriteBom(Path.Combine(dir, "animals.txt"), sb.ToString());

        if (enabled)
        {
            Console.WriteLine($"  animals: {total:N0} instances in {herds:N0} herds " +
                              $"across {SpeciesTable.Length} species ({galloping:N0} on ground open " +
                              "enough to gallop)");
        }
        else
        {
            Console.WriteLine("  animals: disabled (0 instances)");
        }
    }

    private static bool Place(Species species, int x, int y, float[] elevation, MapConfig cfg,
        Rng rng, out float px, out float pz)
    {
        const int Attempts = 6;

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            double angle = rng.NextDouble() * Math.Tau;
            double radius = species.Spread * Math.Sqrt(rng.NextDouble());

            double fx = x + 0.5 + radius * Math.Cos(angle);
            double fy = y + 0.5 + radius * Math.Sin(angle);

            if (!ScatterGround.IsDryLand(elevation, cfg, fx, fy)) continue;

            var (wx, wz) = WorldSpace.FromImage(fx, fy, cfg.ProvinceHeight);
            px = (float)wx;
            pz = (float)wz;
            return true;
        }

        px = pz = 0;
        return false;
    }

    private static bool IsOpenGround(TerrainClass[] terrain, float[] elevation, MapConfig cfg,
        int x, int y)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        float low = float.MaxValue, high = float.MinValue;

        for (int dy = -GallopClearance; dy <= GallopClearance; dy += GallopStep)
        {
            for (int dx = -GallopClearance; dx <= GallopClearance; dx += GallopStep)
            {
                if (dx * dx + dy * dy > GallopClearance * GallopClearance) continue;

                int sx = x + dx, sy = y + dy;
                if (sx < 0 || sx >= width || sy < 0 || sy >= height) return false;

                if (Array.IndexOf(OpenGround, terrain[sy * width + sx]) < 0) return false;

                float h = ScatterGround.HeightAt(elevation, cfg, sx, sy);
                if (h <= cfg.Limits.SeaLevelUpper) return false;

                low = Math.Min(low, h);
                high = Math.Max(high, h);

                if (high - low > GallopRelief) return false;
            }
        }

        return true;
    }

    private static int PickVariant(Species species, int weightTotal, bool open, bool allowGallop, Rng rng)
    {
        int roll = rng.Int(1, weightTotal);
        for (int i = 0; i < species.Variants.Length; i++)
        {
            var v = species.Variants[i];
            if (v.NeedsOpenGround && (!open || !allowGallop)) continue;

            roll -= v.Weight;
            if (roll <= 0) return i;
        }

        for (int i = species.Variants.Length - 1; i >= 0; i--)
        {
            var v = species.Variants[i];
            if (!v.NeedsOpenGround || (open && allowGallop)) return i;
        }

        return 0;
    }

    private static void Append(StringBuilder sb, Species species,
        List<(float X, float Z, float Angle, float Scale)>[] buckets)
    {
        var culture = CultureInfo.InvariantCulture;
        string clamp = species.ClampToWater ? "yes" : "no";

        for (int b = 0; b < buckets.Length; b++)
        {
            var instances = buckets[b];
            string name = species.Variants[b].Entity.Replace("_entity", "");

            sb.Append("object={\n");
            sb.Append($"\tname=\"{name}\"\n");
            sb.Append("\trender_pass=Map\n");
            sb.Append($"\tclamp_to_water_level={clamp}\n");
            sb.Append("\tgenerated_content=no\n");
            sb.Append("\tlayer=\"unit_layer\"\n");
            sb.Append($"\tentity=\"{species.Variants[b].Entity}\"\n");
            sb.Append($"\tcount={instances.Count}\n");
            sb.Append("\ttransform=\"");

            for (int i = 0; i < instances.Count; i++)
            {
                var (x, z, angle, scale) = instances[i];

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
    }
}