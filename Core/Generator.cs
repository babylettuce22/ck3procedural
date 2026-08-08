using Ck3MapGen.Config;
using Ck3MapGen.MapGen;
using Ck3MapGen.MapGen.Terra;
using Ck3MapGen.World;

namespace Ck3MapGen.Core;

/// <summary>
/// Everything the CLI used to do inline, as a pair of callable steps.
///
/// The point of the split is that generating and writing the mod are separately useful: the GUI
/// regenerates constantly while a parameter is being tuned and writes a mod only when asked, and
/// writing is by far the slower half at full map sizes. Both front ends drive this same code, so
/// there is one definition of the pipeline rather than a console one and a window one that drift.
/// </summary>
public sealed class GenerationOptions
{
    public MapConfig Config { get; set; } = new();

    public string GameDir { get; set; } =
        @"C:\Program Files (x86)\Steam\steamapps\common\Crusader Kings III\game";

    /// <summary>The ck2rpg noise/hotspot path. Only meaningful when Terra is off.</summary>
    public bool UseNoise { get; set; }

    /// <summary>Skip characters, title history, dynasties and the bookmark.</summary>
    public bool WriteHistory { get; set; } = true;

    /// <summary>Hand-build the packed/indirection pair rather than leaving them to the editor.</summary>
    public bool WritePacked { get; set; } = true;

    /// <summary>
    /// Emit the mod around this heightmap instead of generating terrain. The image is
    /// authoritative about map size, so the size preset is ignored when it is set.
    /// </summary>
    public string? HeightmapPath { get; set; }

    public static string DefaultModDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Paradox Interactive", "Crusader Kings III", "mod", "proceduralmap");
}

public sealed class GenerationResult
{
    public required MapConfig Config { get; init; }
    public required WorldGrid World { get; init; }
    public required float[] ProvinceElevation { get; init; }
    public required byte[] LandMask { get; init; }
    public required ProvinceMap Provinces { get; init; }
    public TerrainData? Terra { get; init; }
    public long ElapsedMs { get; init; }
}

public static class Generator
{
    /// <summary>
    /// Builds the world and the province partition. Does not touch the mod folder.
    /// </summary>
    public static GenerationResult Generate(GenerationOptions options)
    {
        var cfg = options.Config;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rng = new Rng(cfg.Seed);

        WorldGrid world;
        float[] provinceElevation;
        TerrainData? terra = null;

        if (options.HeightmapPath is not null)
        {
            // Everything downstream reads TerrainData and nothing else, so an imported heightmap
            // travels the same path as a generated one from here on.
            terra = HeightmapSource.Load(options.HeightmapPath, cfg, rng);
            world = WorldBridge.Populate(terra, cfg, rng);
            provinceElevation = terra.ProvinceElevation;
        }
        else if (cfg.UseTerra)
        {
            // Terrain is generated at heightmap resolution and summarised down onto the coarse
            // grid, rather than simulated coarse and stretched up. The province map is derived from
            // the same field the heightmap is written from, so the coastline in provinces.png is
            // the coastline in heightmap.png by construction.
            terra = TerraPipeline.Generate(cfg, rng);
            world = WorldBridge.Populate(terra, cfg, rng);
            provinceElevation = terra.ProvinceElevation;
        }
        else
        {
            world = options.UseNoise
                ? Pipeline.GenerateWorldFromNoise(cfg, rng)
                : Pipeline.GenerateWorld(cfg, rng);

            Console.WriteLine($"Rasterising heightmap {cfg.Width}x{cfg.Height}, " +
                              $"provinces {cfg.ProvinceWidth}x{cfg.ProvinceHeight}");
            provinceElevation = Raster.UpsampleElevation(world, cfg.ProvinceWidth, cfg.ProvinceHeight);
        }

        var landMask = Raster.LandMask(provinceElevation, cfg);
        var provinces = Provinces.Build(landMask, cfg.ProvinceWidth, cfg.ProvinceHeight, cfg, rng);
        Console.WriteLine($"  {provinces.Count} provinces total");

        return new GenerationResult
        {
            Config = cfg,
            World = world,
            ProvinceElevation = provinceElevation,
            LandMask = landMask,
            Provinces = provinces,
            Terra = terra,
            ElapsedMs = sw.ElapsedMilliseconds,
        };
    }

    /// <summary>
    /// Writes the whole mod. The RNG is re-seeded from the config so a given seed produces the
    /// same mod whether or not the world was generated in the same process.
    /// </summary>
    public static void WriteMod(GenerationResult result, GenerationOptions options, string modDir)
    {
        var cfg = result.Config;
        var rng = new Rng(cfg.Seed);

        Console.WriteLine($"Writing mod to {modDir}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Directory.CreateDirectory(modDir);

        Emit.ModWriter.WriteDescriptors(modDir);

        var (order, landCount) = Emit.MapDataWriter.WriteAll(
            modDir, result.World, cfg, result.Provinces, result.ProvinceElevation,
            options.WritePacked, result.Terra);

        var empires = Titles.Build(result.Provinces, landCount, order, rng);
        Emit.ContentWriter.WriteAll(
            modDir, options.GameDir, result.World, cfg, result.Provinces, order, landCount,
            empires, result.ProvinceElevation, rng, options.WriteHistory, result.Terra);

        Console.WriteLine($"  done in {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>The debug PNG dump the CLI has always produced.</summary>
    public static void WriteDebugImages(GenerationResult result, string outDir, int scale)
    {
        Directory.CreateDirectory(outDir);
        var rng = new Rng(result.Config.Seed);

        Io.DebugRender.WriteElevation(Path.Combine(outDir, "debug_elevation.png"), result.World, scale);
        Io.DebugRender.WriteTerrain(Path.Combine(outDir, "debug_terrain.png"), result.World,
            result.Config, scale);
        Io.DebugRender.WriteProvinces(Path.Combine(outDir, "debug_provinces.png"), result.Provinces, rng);

        // Hillshaded relief at the resolution the erosion ran at. Worth more than the greyscale
        // dumps: flat grey hides whether erosion produced valley networks, shading shows them the
        // way the game's lighting will.
        //
        // Guarded on Preview, not on Terra. An imported heightmap produces a perfectly good
        // TerrainData with a null Preview — there is no coarse world behind it — so testing Terra
        // for null passes and then dereferences nothing.
        if (result.Terra?.Preview is { } preview) Io.TerraPreview.WriteAll(outDir, preview);

        Console.WriteLine($"Wrote debug images to {outDir}");
    }
}
