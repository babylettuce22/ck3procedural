using Ck3MapGen.Config;
using Ck3MapGen.Emit;
using Ck3MapGen.MapGen;
using Ck3MapGen.World;

namespace Ck3MapGen.Core;

public sealed class GenerationOptions
{
    public MapConfig Config { get; set; } = new();
    public string GameDir { get; set; } = GameLocator.FindGameDir() ?? GameLocator.DefaultGameDir;
    public bool WriteHistory { get; set; } = true;
    public bool WritePacked { get; set; } = true;
    public string? HeightmapPath { get; set; }
    public string ModName { get; set; } = DefaultModName;

    public const string DefaultModName = "Procedural Map";
    public static string ModRoot => GameLocator.FindModRoot();
    public static string DefaultModDir => Path.Combine(ModRoot, "proceduralmap");
}

public sealed class GenerationResult
{
    public required MapConfig Config { get; init; }
    public required WorldGrid World { get; init; }
    public required float[] ProvinceElevation { get; init; }
    public required byte[] LandMask { get; init; }
    public required ProvinceMap Provinces { get; init; }
    public required int[] ProvinceOrder { get; init; }
    public required int BaronyCount { get; init; }
    public required int LandCount { get; init; }
    public required int RiverCount { get; init; }
    public required List<MapGen.Title> Titles { get; init; }
    public required byte[] ProvinceLandMask { get; init; }
    public required MapGen.Drainage Drainage { get; init; }
    public required MapGen.TerrainClassifier.Result Terrain { get; init; }
    public required TerrainData Terra { get; init; }
    public long ElapsedMs { get; init; }
}

public static class Generator
{
    public static GenerationResult Generate(GenerationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HeightmapPath))
            throw new InvalidOperationException("No heightmap given.");

        var cfg = options.Config;
        var image = Stage.Time("heightmap decode", () => HeightmapSource.Read(options.HeightmapPath, cfg));
        HeightmapSource.Diagnose(image, cfg);

        var terra = Stage.Time("province elevation",
            () => TerrainData.FromElevation(image.ToElevation(cfg), cfg));

        return FromTerrain(terra, cfg);
    }

    public static GenerationResult FromTerrain(TerrainData terra, MapConfig cfg)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rng = new Rng(cfg.Seed);

        var world = Stage.Time("coarse world summary", () => WorldBridge.Populate(terra, cfg, rng));
        var provinceElevation = terra.ProvinceElevation;
        var landMask = Stage.Time("land mask", () => Raster.LandMask(terra.Elevation, cfg));

        var climate = Stage.Time("climate",
            () => MapGen.ClimateModel.Build(cfg, provinceElevation, landMask, new Rng(cfg.Seed ^ 0x0C11)));

        // 1. Naturalized drainage network (no 45° lines)
        var drainage = Stage.Time("drainage",
            () => MapGen.Drainage.Build(cfg, provinceElevation, landMask, climate.AnnualMm, rng));

        // 2. Extract major rivers from top drainage trunks and carve into heightmap
        var majorRivers = Stage.Time("major rivers carve",
            () => MajorRivers.ExtractAndCarve(terra.Elevation, cfg.Width, cfg.Height, drainage, cfg, rng));
        terra.MajorRiversList = majorRivers;

        // 3. Re-sample province elevation and land mask so channels are seen as water
        provinceElevation = Stage.Time("recompute province elevation",
            () => Raster.ProvinceElevation(terra.Elevation, cfg));
        terra.ProvinceElevation = provinceElevation;

        landMask = Stage.Time("recompute land mask",
            () => Raster.LandMask(terra.Elevation, cfg));

        // 4. Partition provinces with river seeds
        var provinces = Stage.Time("province partition",
            () => Provinces.Build(landMask, provinceElevation, climate,
                cfg.ProvinceWidth, cfg.ProvinceHeight, cfg, rng, majorRivers));
        Console.WriteLine($"  {provinces.Count} provinces total");

        var provinceLandMask = ProvinceLandMask(cfg, provinces);

        var terrain = Stage.Time("terrain classification",
            () => MapGen.TerrainClassifier.Classify(cfg, provinceElevation, provinceLandMask,
                climate, new Rng(cfg.Seed ^ 0x7E44)));

        var order = MapDataWriter.BuildProvinceOrder(provinces, out int baronies, out int landCount, out int riverCount);

        var titles = Stage.Time("title hierarchy",
            () => MapGen.Titles.Build(provinces, baronies, order, cfg, new Rng(cfg.Seed ^ 0x71C1)));

        return new GenerationResult
        {
            Config = cfg,
            World = world,
            ProvinceElevation = provinceElevation,
            LandMask = landMask,
            ProvinceLandMask = provinceLandMask,
            Provinces = provinces,
            ProvinceOrder = order,
            BaronyCount = baronies,
            LandCount = landCount,
            RiverCount = riverCount,
            Titles = titles,
            Drainage = drainage,
            Terrain = terrain,
            Terra = terra,
            ElapsedMs = sw.ElapsedMilliseconds,
        };
    }

    private static byte[] ProvinceLandMask(MapConfig cfg, ProvinceMap provinces)
    {
        var mask = new byte[cfg.ProvinceWidth * cfg.ProvinceHeight];
        Parallel.For(0, mask.Length, i => mask[i] = provinces.Seeds[provinces.Label[i]].IsLand ? (byte)1 : (byte)0);
        return mask;
    }

    public static void WriteMod(GenerationResult result, GenerationOptions options, string modDir)
    {
        var cfg = result.Config;
        var rng = new Rng(cfg.Seed);

        if (!GameLocator.IsGameDir(options.GameDir))
            throw new DirectoryNotFoundException(
                $"'{options.GameDir}' is not a Crusader Kings III game folder.");

        Console.WriteLine($"Writing mod to {modDir}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Directory.CreateDirectory(modDir);

        Emit.ModWriter.WriteDescriptors(modDir, options.ModName);

        Stage.Time("map_data (heightmap, provinces, rivers)",
                    () => Emit.MapDataWriter.WriteAll(modDir, cfg, result.Provinces, result.ProvinceOrder,
                        result.BaronyCount, result.LandCount, result.RiverCount, options.WritePacked,
                        result.Terra, result.Drainage));

        Emit.ContentWriter.WriteAll(
            modDir, options.GameDir, cfg, result.Provinces, result.ProvinceOrder, result.LandCount,
            result.Titles, result.ProvinceElevation, result.Terrain, rng,
            options.WriteHistory);

        Console.WriteLine($"  done in {sw.ElapsedMilliseconds} ms");
    }

    public static void WriteDebugImages(GenerationResult result, string outDir, int scale)
    {
        Directory.CreateDirectory(outDir);
        var rng = new Rng(result.Config.Seed);

        Io.DebugRender.WriteElevation(Path.Combine(outDir, "debug_elevation.png"), result.World, scale);
        Io.DebugRender.WriteTerrain(Path.Combine(outDir, "debug_terrain.png"), result.World, result.Config, scale);
        Io.DebugRender.WriteProvinces(Path.Combine(outDir, "debug_provinces.png"), result.Provinces, rng);

        var classified = result.Terrain;
        Io.DebugRender.WriteTerrainClasses(Path.Combine(outDir, "debug_classes.png"),
            classified.Terrain, result.Config.ProvinceWidth, result.Config.ProvinceHeight);

        Io.DebugRender.WriteKoppen(Path.Combine(outDir, "debug_climate.png"), classified.Climate,
            result.Config.ProvinceWidth, result.Config.ProvinceHeight);

        Io.DebugRender.WriteDrainage(Path.Combine(outDir, "debug_drainage.png"), result.Drainage,
            result.ProvinceElevation, result.Config);

        Io.DebugRender.WriteRivers(Path.Combine(outDir, "debug_rivers.png"),
                    Emit.MapDataWriter.RiverIndices(result.Config, result.Provinces, result.Drainage),
                    result.Config.ProvinceWidth, result.Config.ProvinceHeight);

        Io.DebugRender.WriteField(Path.Combine(outDir, "debug_rainfall.png"),
            classified.Field.AnnualMm, result.ProvinceLandMask,
            result.Config.ProvinceWidth, result.Config.ProvinceHeight);
        Io.DebugRender.WriteField(Path.Combine(outDir, "debug_temperature.png"),
            classified.Field.MeanC, result.ProvinceLandMask,
            result.Config.ProvinceWidth, result.Config.ProvinceHeight);

        Console.WriteLine($"Wrote debug images to {outDir}");
    }
}