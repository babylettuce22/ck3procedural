using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using Ck3MapGen.MapGen.Terra;

var cfg = new MapConfig();
string outDir = Path.Combine(AppContext.BaseDirectory, "out");
int scale = 2;
bool useNoise = false;
string? modDir = null;
string gameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Crusader Kings III\game";

// Bisection switch: skip characters, title history, dynasties and the bookmark.
bool writeHistory = true;

// Whether to hand-build packed_heightmap.png + indirection_heightmap.png, or leave them to the
// map editor's repack step.
bool writePacked = true;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed" when i + 1 < args.Length:
            cfg.Seed = int.Parse(args[++i]);
            break;

        case "--out" when i + 1 < args.Length:
            outDir = args[++i];
            break;

        // Emit the mod itself. Defaults to the launcher's mod folder when given no value.
        case "--mod":
            modDir = i + 1 < args.Length && !args[i + 1].StartsWith("--")
                ? args[++i]
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Paradox Interactive", "Crusader Kings III", "mod", "proceduralmap");
            break;

        case "--scale" when i + 1 < args.Length:
            scale = int.Parse(args[++i]);
            break;

        // Target land coverage, 0..1. Pass 0 to stop at ck2rpg's raw startup() output.
        case "--land" when i + 1 < args.Length:
            cfg.TargetLandFraction = double.Parse(args[++i],
                System.Globalization.CultureInfo.InvariantCulture);
            break;

        // The noise/hotspot terrain path (randomMap) instead of the tectonic simulation.
        case "--noise":
            useNoise = true;
            cfg.UseTerra = false;
            break;

        // The ck2rpg magma simulation, for comparison against the tectonics-and-erosion
        // generator that replaced it.
        case "--legacy-terrain":
            cfg.UseTerra = false;
            break;

        case "--erosion":
            cfg.EnableRainErosion = true;
            break;

        // How big a barony is relative to vanilla's. 2 makes each one twice as wide and a
        // quarter as numerous; the rest of the title hierarchy follows.
        case "--county-scale" when i + 1 < args.Length:
            cfg.CountyScale = double.Parse(args[++i],
                System.Globalization.CultureInfo.InvariantCulture);
            break;

        case "--no-history":
            writeHistory = false;
            break;

        // Ship only heightmap.png and let -mapeditor's repack build the packed/indirection pair,
        // which is what both the wiki and ck2rpg's tutorial prescribe.
        case "--no-packed":
            writePacked = false;
            break;

        // Bisection size: generates in seconds and loads far faster, so it is the right scale
        // for chasing load errors. Province counts are scaled down to match.
        case "tiny":
            (cfg.WorldWidth, cfg.WorldHeight) = (256, 128);
            (cfg.Width, cfg.Height) = (2048, 1024);
            break;

        case "small":
            (cfg.WorldWidth, cfg.WorldHeight) = (512, 256);
            (cfg.Width, cfg.Height) = (4096, 2048);
            break;

        case "full":
            (cfg.WorldWidth, cfg.WorldHeight) = (512, 256);
            (cfg.Width, cfg.Height) = (8192, 4096);
            break;

        // Vanilla's exact dimensions: heightmap 18432x9216, provinces 9216x4608. Using them
        // removes map resolution as a variable altogether — WORLD_EXTENTS, PANNING_* and every
        // vanilla-sized terrain texture are then correct without an override.
        case "vanilla":
            (cfg.WorldWidth, cfg.WorldHeight) = (1024, 512);
            (cfg.Width, cfg.Height) = (18432, 9216);
            break;

        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

var rng = new Rng(cfg.Seed);

Ck3MapGen.World.WorldGrid world;
float[] provinceElevation;
TerraResult? terra = null;

if (cfg.UseTerra)
{
    // Terrain is generated at heightmap resolution and summarised down onto the coarse grid,
    // rather than simulated on the coarse grid and stretched up. The province map is derived
    // from the same field the heightmap is written from, so the coastline in provinces.png is
    // the coastline in heightmap.png by construction.
    terra = TerraPipeline.Generate(cfg, rng);
    world = WorldBridge.Populate(terra, cfg, rng);
    provinceElevation = terra.ProvinceElevation;
}
else
{
    world = useNoise
        ? Pipeline.GenerateWorldFromNoise(cfg, rng)
        : Pipeline.GenerateWorld(cfg, rng);

    Console.WriteLine($"Rasterising heightmap {cfg.Width}x{cfg.Height}, " +
                      $"provinces {cfg.ProvinceWidth}x{cfg.ProvinceHeight}");
    provinceElevation = Raster.UpsampleElevation(world, cfg.ProvinceWidth, cfg.ProvinceHeight);
}

var landMask = Raster.LandMask(provinceElevation, cfg);
var provinces = Provinces.Build(landMask, cfg.ProvinceWidth, cfg.ProvinceHeight, cfg, rng);
Console.WriteLine($"  {provinces.Count} provinces total");

if (modDir is not null)
{
    Console.WriteLine($"Writing mod to {modDir}");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    Directory.CreateDirectory(modDir);

    Ck3MapGen.Emit.ModWriter.WriteDescriptors(modDir);

    var (order, landCount) = Ck3MapGen.Emit.MapDataWriter.WriteAll(
        modDir, world, cfg, provinces, provinceElevation, writePacked, terra);

    var empires = Titles.Build(provinces, landCount, order, rng);
    Ck3MapGen.Emit.ContentWriter.WriteAll(
        modDir, gameDir, world, cfg, provinces, order, landCount, empires,
        provinceElevation, rng, writeHistory, terra);

    Console.WriteLine($"  done in {sw.ElapsedMilliseconds} ms");
}

Directory.CreateDirectory(outDir);
DebugRender.WriteElevation(Path.Combine(outDir, "debug_elevation.png"), world, scale);
DebugRender.WriteTerrain(Path.Combine(outDir, "debug_terrain.png"), world, cfg, scale);
DebugRender.WriteProvinces(Path.Combine(outDir, "debug_provinces.png"), provinces, rng);

// Hillshaded relief at the resolution the erosion ran at. Worth more than the greyscale dumps:
// flat grey hides whether erosion produced valley networks, shading shows them the way the
// game's lighting will.
if (terra is not null) TerraPreview.WriteAll(outDir, terra.Preview);

Console.WriteLine($"Wrote debug images to {outDir}");

return 0;
