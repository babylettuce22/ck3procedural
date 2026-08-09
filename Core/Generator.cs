using Ck3MapGen.Config;
using Ck3MapGen.MapGen;
using Ck3MapGen.World;

namespace Ck3MapGen.Core;

/// <summary>
/// Everything the CLI used to do inline, as a pair of callable steps.
///
/// The point of the split is that reading the heightmap and writing the mod are separately useful:
/// the GUI re-derives constantly while a parameter is being tuned and writes a mod only when asked,
/// and writing is by far the slower half at full map sizes. Both front ends drive this same code,
/// so there is one definition of the pipeline rather than a console one and a window one that drift.
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
    /// The heightmap the whole mod is built around. Required — this tool no longer makes terrain,
    /// it interprets it. The image is authoritative about map size.
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

    /// <summary>
    /// Land/water as the finished province partition sees it, which is the only authority on it.
    /// <see cref="LandMask"/> is what went *into* the partition; the build then drowns islands too
    /// small to be provinces, so the two disagree on those pixels and everything downstream must
    /// use this one.
    /// </summary>
    public required byte[] ProvinceLandMask { get; init; }

    /// <summary>
    /// The climate and the terrain painted from it. Computed once here rather than by each consumer,
    /// because it was being derived three times a run - the mod writer, the debug images and the GUI
    /// preview each built their own - and the climate model is not cheap.
    /// </summary>
    public required MapGen.TerrainClassifier.Result Terrain { get; init; }
    public required TerrainData Terra { get; init; }
    public long ElapsedMs { get; init; }
}

public static class Generator
{
    /// <summary>
    /// Reads the heightmap and builds the world and the province partition from it. Does not touch
    /// the mod folder.
    /// </summary>
    public static GenerationResult Generate(GenerationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HeightmapPath))
            throw new InvalidOperationException(
                "No heightmap given. This tool builds a CK3 mod around a heightmap and does not " +
                "generate terrain itself.");

        var cfg = options.Config;
        var rng = new Rng(cfg.Seed);

        // The province map is derived from the same field the heightmap is read from, so the
        // coastline in provinces.png is the coastline in heightmap.png by construction.
        var image = Stage.Time("heightmap decode", () => HeightmapSource.Read(options.HeightmapPath, cfg));
        var terra = Stage.Time("drainage, rivers and lakes",
            () => TerrainData.FromElevation(image.Elevation, cfg, rng));

        return FromTerrain(terra, cfg);
    }

    /// <summary>
    /// Everything downstream of the heightmap: the coarse world summary, moisture, the land mask
    /// and the province partition. Terrain in, a full result out.
    ///
    /// Split out from <see cref="Generate"/> so it can be re-run against a heightmap already in
    /// memory. That is what makes previewing a settings change — climate, province size, the
    /// terrain classifier — take the seconds this does rather than re-reading and re-draining the
    /// image every time. Re-seeded from the config rather than taking a live Rng, so a preview and
    /// a later write of the same settings agree.
    /// </summary>
    public static GenerationResult FromTerrain(TerrainData terra, MapConfig cfg)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rng = new Rng(cfg.Seed);

        var world = Stage.Time("coarse world summary", () => WorldBridge.Populate(terra, cfg, rng));
        var provinceElevation = terra.ProvinceElevation;

        // From the full heightmap, not from provinceElevation: the coastline has to be the one the
        // renderer draws, and a 2x2 average of it is not that coastline. See Raster.LandMask.
        var landMask = Stage.Time("land mask", () => Raster.LandMask(terra.Elevation, cfg));
        var provinces = Stage.Time("province partition",
            () => Provinces.Build(landMask, provinceElevation, cfg.ProvinceWidth,
                cfg.ProvinceHeight, cfg, rng));
        Console.WriteLine($"  {provinces.Count} provinces total");

        var provinceLandMask = ProvinceLandMask(cfg, provinces);

        // Its own seed rather than whatever state the shared Rng happens to be in. The noise fields
        // the classifier lays down should not depend on how many random numbers the steps before it
        // happened to draw, which is what made moving this call change the map.
        var terrain = Stage.Time("climate and terrain classification",
            () => MapGen.TerrainClassifier.Classify(cfg, provinceElevation, provinceLandMask,
                new Rng(cfg.Seed ^ 0x0C11)));

        return new GenerationResult
        {
            Config = cfg,
            World = world,
            ProvinceElevation = provinceElevation,
            LandMask = landMask,
            ProvinceLandMask = provinceLandMask,
            Provinces = provinces,
            Terrain = terrain,
            Terra = terra,
            ElapsedMs = sw.ElapsedMilliseconds,
        };
    }

    /// <summary>
    /// Land/water straight off the finished partition. A province is land or it is not, and the
    /// ordering the emitters use to turn labels into ids says exactly the same thing, so this does
    /// not need to wait for it.
    /// </summary>
    private static byte[] ProvinceLandMask(MapConfig cfg, ProvinceMap provinces)
    {
        var mask = new byte[cfg.ProvinceWidth * cfg.ProvinceHeight];
        Parallel.For(0, mask.Length, i => mask[i] = provinces.Seeds[provinces.Label[i]].IsLand ? (byte)1 : (byte)0);
        return mask;
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

        var (order, baronyCount, landCount) = Stage.Time("map_data (heightmap, provinces, rivers)",
            () => Emit.MapDataWriter.WriteAll(
                modDir, cfg, result.Provinces, options.WritePacked, result.Terra));

        // Titles get the narrower count: an impassable province has no barony.
        var empires = Stage.Time("title hierarchy",
            () => Titles.Build(result.Provinces, baronyCount, order, cfg, rng));
        Emit.ContentWriter.WriteAll(
            modDir, options.GameDir, cfg, result.Provinces, order, landCount,
            empires, result.ProvinceElevation, result.Terrain.Terrain, rng, options.WriteHistory);

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
        var classified = result.Terrain;

        Io.DebugRender.WriteTerrainClasses(Path.Combine(outDir, "debug_classes.png"),
            classified.Terrain, result.Config.ProvinceWidth, result.Config.ProvinceHeight);

        // The climate behind it, in Koppen's own colours — the only view that can be checked
        // against a real atlas rather than judged by eye.
        Io.DebugRender.WriteKoppen(Path.Combine(outDir, "debug_climate.png"), classified.Climate,
            result.Config.ProvinceWidth, result.Config.ProvinceHeight);

        // And the two fields the classification is a function of, so a mottled climate map can be
        // traced to whichever of them is mottled.
        Io.DebugRender.WriteField(Path.Combine(outDir, "debug_rainfall.png"),
            classified.Field.AnnualMm, result.ProvinceLandMask,
            result.Config.ProvinceWidth, result.Config.ProvinceHeight);
        Io.DebugRender.WriteField(Path.Combine(outDir, "debug_temperature.png"),
            classified.Field.MeanC, result.ProvinceLandMask,
            result.Config.ProvinceWidth, result.Config.ProvinceHeight);

        Console.WriteLine($"Wrote debug images to {outDir}");
    }
}
