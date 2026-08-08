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
    public required TerrainData Terra { get; init; }
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
        var rng = new Rng(cfg.Seed);

        // Everything downstream reads TerrainData and nothing else, so an imported heightmap
        // travels the same path as a generated one from here on.
        //
        // Terrain is generated at heightmap resolution and summarised down onto the coarse grid,
        // rather than simulated coarse and stretched up. The province map is derived from the same
        // field the heightmap is written from, so the coastline in provinces.png is the coastline
        // in heightmap.png by construction.
        var terra = options.HeightmapPath is not null
            ? HeightmapSource.Load(options.HeightmapPath, cfg, rng)
            : TerraPipeline.Generate(cfg, rng);

        return FromTerrain(terra, cfg);
    }

    /// <summary>
    /// Everything downstream of the heightmap: the coarse world summary, moisture, the land mask
    /// and the province partition. Terrain in, a full result out.
    ///
    /// Split out from <see cref="Generate"/> so it can be re-run against terrain that already
    /// exists. That is what makes the GUI's Mod tab able to preview a settings change — climate,
    /// province size, the terrain classifier — in the seconds this takes rather than by
    /// regenerating terrain, which is the slow half and which none of those settings affect.
    /// Re-seeded from the config rather than taking a live Rng, so a preview and a later write of
    /// the same settings agree.
    /// </summary>
    public static GenerationResult FromTerrain(TerrainData terra, MapConfig cfg)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rng = new Rng(cfg.Seed);

        var world = WorldBridge.Populate(terra, cfg, rng);
        var provinceElevation = terra.ProvinceElevation;

        var landMask = Raster.LandMask(provinceElevation, cfg);
        var provinces = Provinces.Build(landMask, provinceElevation, cfg.ProvinceWidth,
            cfg.ProvinceHeight, cfg, rng);
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

        var (order, baronyCount, landCount) = Emit.MapDataWriter.WriteAll(
            modDir, cfg, result.Provinces, options.WritePacked, result.Terra);

        // Titles get the narrower count: an impassable province has no barony.
        var empires = Titles.Build(result.Provinces, baronyCount, order, cfg, rng);
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

        // The classification the mod is painted from, which debug_terrain.png above is NOT — that
        // one is ck2rpg's coarse biome() and exists only as a port check.
        Io.DebugRender.WriteTerrainClasses(Path.Combine(outDir, "debug_classes.png"),
            MapGen.TerrainClassifier.Classify(result.World, result.Config, result.ProvinceElevation,
                result.LandMask, new Rng(result.Config.Seed)),
            result.Config.ProvinceWidth, result.Config.ProvinceHeight);

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
