using Ck3MapGen.Config;
using Ck3MapGen.Emit;
using Ck3MapGen.Gui;
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

    /// <summary>
    /// An Azgaar "Full" JSON export to borrow names, borders and politics from. Optional and always
    /// has been: with no path the generator behaves exactly as it does without one.
    /// </summary>
    public string? AzgaarJsonPath { get; set; }
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

    /// <summary>The imported Azgaar world, or null when none was given.</summary>
    public MapGen.AzgaarImport? Azgaar { get; init; }

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

        return FromTerrain(terra, cfg, azgaarJsonPath: options.AzgaarJsonPath);
    }

    public static GenerationResult FromTerrain(
            TerrainData terra,
            MapConfig cfg,
            Action<string, PreviewRenderer.Image>? onPreview = null,
            string? azgaarJsonPath = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rng = new Rng(cfg.Seed);

        // 1. Initial Relief
        onPreview?.Invoke("Relief", PreviewRenderer.RenderRelief(terra.ProvinceElevation, cfg));

        var world = Stage.Time("coarse world summary", () => WorldBridge.Populate(terra, cfg, rng));
        var provinceElevation = terra.ProvinceElevation;
        var landMask = Stage.Time("land mask", () => Raster.LandMask(terra.Elevation, cfg));

        // 2. The Azgaar export, if there is one.
        //
        // Loaded before anything consumes it rather than beside its first reader, because its
        // readers now sit on both sides of the pipeline: the climate is reanchored on grid.temp
        // and grid.prec, and the province partition is cut inside the export's own borders, so by
        // the time the province grid exists it is already too late. Nothing here depends on that
        // grid — AzgaarRaster keys off cfg.ProvinceWidth/Height, which are config — so reading it
        // first costs nothing.
        //
        // The alignment check moves with it, and gains by the move. It compares the export's land
        // against the heightmap's, and landMask is exactly that, more directly than the
        // post-partition mask it used to read. Reporting it here means a heightmap exported
        // cropped or zoomed is caught before the run is spent on it rather than after.
        var azgaar = MapGen.AzgaarImport.Load(azgaarJsonPath ?? cfg.AzgaarJsonPath, cfg);
        azgaar?.CheckAlignment(landMask);

        // 3. Climate
        var climate = Stage.Time("climate",
            () => MapGen.ClimateModel.Build(cfg, provinceElevation, landMask, new Rng(cfg.Seed ^ 0x0C11), azgaar));
        onPreview?.Invoke("Climate", PreviewRenderer.RenderClimate(climate, cfg));

        // 4. Drainage & Major Rivers
        var drainage = Stage.Time("drainage",
            () => MapGen.Drainage.Build(cfg, provinceElevation, landMask, climate.AnnualMm, rng));
        onPreview?.Invoke("Drainage", PreviewRenderer.RenderDrainage(drainage, provinceElevation, cfg));

        var majorRivers = Stage.Time("major rivers carve",
            () => MajorRivers.ExtractAndCarve(terra.Elevation, cfg.Width, cfg.Height, drainage, cfg, rng));
        terra.MajorRiversList = majorRivers;

        provinceElevation = Stage.Time("recompute province elevation",
            () => Raster.ProvinceElevation(terra.Elevation, cfg));
        terra.ProvinceElevation = provinceElevation;

        landMask = Stage.Time("recompute land mask",
            () => Raster.LandMask(terra.Elevation, cfg));

        // 5. Partition provinces with river seeds
        var provinces = Stage.Time("province partition",
            () => Provinces.Build(landMask, provinceElevation, climate,
                cfg.ProvinceWidth, cfg.ProvinceHeight, cfg, rng, majorRivers, drainage, azgaar));
        Console.WriteLine($"  {provinces.Count} provinces total");

        // --- PREVIEWS READY HERE ---
        onPreview?.Invoke("Provinces", PreviewRenderer.RenderProvinces(provinces, cfg));
        onPreview?.Invoke("Rivers", PreviewRenderer.RenderRivers(
            Emit.MapDataWriter.RiverIndices(cfg, provinces, drainage), cfg));

        var provinceLandMask = ProvinceLandMask(cfg, provinces);

        // 6. Terrain
        var terrain = Stage.Time("terrain classification",
            () => MapGen.TerrainClassifier.Classify(cfg, provinceElevation, provinceLandMask,
                climate, new Rng(cfg.Seed ^ 0x7E44)));
        onPreview?.Invoke("Terrain", PreviewRenderer.RenderTerrain(terrain.Terrain, cfg));

        var order = MapDataWriter.BuildProvinceOrder(provinces, out int baronies, out int landCount, out int riverCount);

        // Before the hierarchy, not after: this decides what shape the hierarchy can be built in,
        // and it is the last point at which re-exporting a larger heightmap is still cheaper than
        // finishing the run.
        Stage.Time("azgaar hierarchy plan", () => azgaar?.PlanHierarchy(provinces, order, baronies));

        // 7. Titles — inside the export's borders when there is one, geometrically when there is not.
        var titles = Stage.Time("title hierarchy",
            () => azgaar?.Plan is not null
                ? MapGen.AzgaarHierarchy.Build(provinces, baronies, order, cfg,
                                               new Rng(cfg.Seed ^ 0x71C1), azgaar)
                : MapGen.Titles.Build(provinces, baronies, order, cfg, new Rng(cfg.Seed ^ 0x71C1)));

        onPreview?.Invoke("Counties", PreviewRenderer.RenderTitles(provinces, order, baronies, landCount, titles, "c"));
        onPreview?.Invoke("Duchies", PreviewRenderer.RenderTitles(provinces, order, baronies, landCount, titles, "d"));
        onPreview?.Invoke("Kingdoms", PreviewRenderer.RenderTitles(provinces, order, baronies, landCount, titles, "k"));
        onPreview?.Invoke("Empires", PreviewRenderer.RenderTitles(provinces, order, baronies, landCount, titles, "e"));

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
            Azgaar = azgaar,
            ElapsedMs = sw.ElapsedMilliseconds,
        };
    }
    private static byte[] ProvinceLandMask(MapConfig cfg, ProvinceMap provinces)
    {
        var mask = new byte[cfg.ProvinceWidth * cfg.ProvinceHeight];
        Parallel.For(0, mask.Length, i => mask[i] = provinces.Seeds[provinces.Label[i]].IsLand ? (byte)1 : (byte)0);
        return mask;
    }

    /// <returns>
    /// What a later edit to the mod needs to re-emit part of it without generating again — see
    /// <see cref="Emit.WrittenContent"/>. The command line ignores it.
    /// </returns>
    public static Emit.WrittenContent WriteMod(GenerationResult result, GenerationOptions options,
        string modDir)
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

        // The array comes back so the scatter passes can read the surface the game will
        // actually render; see ContentWriter.
        var shippedHeightmap = Stage.Time("map_data (heightmap, provinces, rivers)",
                    () => Emit.MapDataWriter.WriteAll(modDir, cfg, result.Provinces, result.ProvinceOrder,
                        result.BaronyCount, result.LandCount, result.RiverCount, options.WritePacked,
                        result.Terra, result.Drainage));

        var written = Emit.ContentWriter.WriteAll(
                            modDir, options.GameDir, cfg, result.Provinces, result.ProvinceOrder,
                            result.BaronyCount, result.LandCount,
                            result.RiverCount, result.Titles, result.Terra, result.Terrain, rng,
                            shippedHeightmap,
                            options.WriteHistory, result.Drainage, result.Azgaar);

        Console.WriteLine($"  done in {sw.ElapsedMilliseconds} ms");
        return written;
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