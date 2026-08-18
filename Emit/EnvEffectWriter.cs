using System.Globalization;
using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes gfx/map/map_object_data/env_effects.txt — atmospheric particles and environmental visual effects
/// scattered across biomes (desert dust plumes, forest mist sunbeams, snow clouds, and mountain effects).
///
/// Automatically modulates instance density and billboard scale based on the imported heightmap's
/// resolution relative to vanilla's reference size (18432x9216 heightmap / 9216x4608 province raster).
/// </summary>
public static class EnvEffectWriter
{
    private sealed record EffectSpec(
        string Name,
        string Entity,
        string Layer,
        (TerrainClass Terrain, double Density)[] Habitat,
        double MinElevation,
        double MaxElevation,
        double MinScale,
        double MaxScale);

    private static readonly EffectSpec[] Specs =
    [
        // Desert heat haze and close ambient dust
        new("env_desert_ambient_close", "env_desert_plains_ambient", "env_effect_layer",
            [(TerrainClass.Desert, 1.2), (TerrainClass.Oasis, 1.0)],
            0, 220, 0.35, 1.20),

        // Arid rocky mountain ridges with blowing sand
        new("env_desert_mountains", "env_desert_mountains", "env_effect_mountains_layer",
            [(TerrainClass.Desert, 6.0), (TerrainClass.Drylands, 4.0), (TerrainClass.Mountains, 2.5)],
            180, 510, 0.25, 0.75),

        // Wide desert dust devils and wind sweeps
        new("env_desert_plains", "env_desert_plains", "env_effect_layer",
            [(TerrainClass.Desert, 1.0), (TerrainClass.Drylands, 0.5)],
            0, 220, 0.60, 1.00),

        // Desert and dry steppe atmospheric dust cover
        new("env_desert_plains_ambient", "env_desert_plains_ambient", "env_effect_layer",
            [(TerrainClass.Desert, 5.0), (TerrainClass.Drylands, 2.5), (TerrainClass.Steppe, 1.0)],
            0, 240, 0.80, 2.40),

        // Forest mist in highland valleys and hills
        new("env_forest_mountains", "env_mist_sun", "env_effect_mountains_layer",
            [(TerrainClass.Forest, 1.5), (TerrainClass.Taiga, 2.0), (TerrainClass.Hills, 1.0)],
            160, 380, 0.60, 1.50),

        // Sunbeams and atmospheric canopy mist over temperate/tropical woods and marshes
        new("env_mist_forest", "env_mist_sun", "env_effect_layer",
            [(TerrainClass.Forest, 4.5), (TerrainClass.Jungle, 6.0), (TerrainClass.Wetlands, 4.0), (TerrainClass.Taiga, 2.0)],
            0, 240, 0.80, 2.80),

        // Frozen peaks, blizzards, and snow drifts on high mountain crests
        new("env_snow_mountains", "env_snow_mountains", "env_effect_mountains_layer",
            [(TerrainClass.Mountains, 12.0), (TerrainClass.Taiga, 4.0), (TerrainClass.Hills, 2.0)],
            220, 510, 0.30, 1.00),

        // Low-ground tundra blizzards
        new("env_snow_plains", "env_snow_plains", "env_effect_layer",
            [(TerrainClass.Taiga, 0.4)],
            0, 200, 0.90, 1.20),

        // Widespread northern ambient snowfall and cold fog
        new("env_snow_plains_ambient", "env_snow_plains_ambient", "env_effect_layer",
            [(TerrainClass.Taiga, 3.0), (TerrainClass.Wetlands, 0.8)],
            0, 260, 0.80, 1.50),

        // Vanilla declares waterfall entities with count=0 for custom manual placement
        new("waterfall", "waterfall", "coast_foam_layer", [], 0, 0, 1.0, 1.0),
        new("waterfall_bottom", "waterfall_bottom", "coast_foam_layer", [], 0, 0, 1.0, 1.0),
    ];

    public static void WriteAll(string modDir, MapConfig cfg, TerrainClass[] terrain,
        float[] elevation, Rng rng)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "map_object_data");
        Directory.CreateDirectory(dir);

        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        var sb = new StringBuilder(1 << 16);
        long total = 0;

        // Resolution scaling factors relative to vanilla reference (18432x9216 heightmap / 9216x4608 provinces)
        // cfg.MapScale is (ProvinceWidth / 9216.0).
        double resolutionScale = cfg.MapScale;
        double densityMultiplier = cfg.EnvEffectDensity;
        double scaleMultiplier = cfg.EnvEffectScale * resolutionScale;

        // Clamp minimum visual scale so small maps don't shrink particle quads below engine visibility thresholds
        float minSafeScaleFloor = 0.12f;

        foreach (var spec in Specs)
        {
            var instances = new List<(float X, float Z, float Angle, float Scale)>();

            if (spec.Habitat.Length > 0 && densityMultiplier > 0)
            {
                var chance = new double[Enum.GetValues<TerrainClass>().Length];
                foreach (var (t, density) in spec.Habitat)
                {
                    chance[(int)t] = (density * densityMultiplier) / 100_000.0;
                }

                float minS = (float)Math.Max(minSafeScaleFloor, spec.MinScale * scaleMultiplier);
                float maxS = (float)Math.Max(minSafeScaleFloor, spec.MaxScale * scaleMultiplier);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        double p = chance[(int)terrain[y * width + x]];
                        if (p <= 0 || rng.NextDouble() >= p) continue;

                        double jx = x + rng.NextDouble();
                        double jy = y + rng.NextDouble();

                        if (!ScatterGround.IsDryLand(elevation, cfg, jx, jy)) continue;

                        float h = ScatterGround.HeightAt(elevation, cfg, (int)jx, (int)jy);
                        if (h < spec.MinElevation || h > spec.MaxElevation) continue;

                        float px = (float)jx;
                        float pz = (float)(height - jy); // Engine Z runs bottom-up

                        float scale = (float)(minS + rng.NextDouble() * (maxS - minS));

                        instances.Add((px, pz, (float)(rng.NextDouble() * Math.Tau), scale));
                    }
                }
            }

            total += instances.Count;
            AppendBlock(sb, spec, instances);
        }

        ParadoxText.WriteBom(Path.Combine(dir, "env_effects.txt"), sb.ToString());

        Console.WriteLine($"  env_effects: {total:N0} instances across {Specs.Length} effect groups " +
                          $"(scaled x{scaleMultiplier:F2} for {cfg.Width}x{cfg.Height} map)");
    }

    private static void AppendBlock(StringBuilder sb, EffectSpec spec,
        List<(float X, float Z, float Angle, float Scale)> instances)
    {
        var culture = CultureInfo.InvariantCulture;

        sb.Append("object={\n");
        sb.Append($"\tname=\"{spec.Name}\"\n");
        sb.Append("\trender_pass=Map\n");
        sb.Append("\tclamp_to_water_level=yes\n");
        sb.Append("\tgenerated_content=no\n");
        sb.Append($"\tlayer=\"{spec.Layer}\"\n");
        sb.Append($"\tentity=\"{spec.Entity}\"\n");
        sb.Append($"\tcount={instances.Count}\n");

        if (instances.Count > 0)
        {
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
            sb.Append("\"\n");
        }

        sb.Append("}\n");
    }
}