using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen;

/// <summary>
/// Map size presets, shared by the CLI and the GUI so they cannot disagree about what "small" is.
/// </summary>
public static class MapPreset
{
    public static readonly string[] Names = ["tiny", "small", "full", "vanilla"];

    public static bool Apply(string name, MapConfig cfg)
    {
        switch (name)
        {
            // Bisection size: generates in seconds and loads far faster, so it is the right scale
            // for chasing load errors. Province counts scale with the map, so they follow.
            case "tiny":
                (cfg.WorldWidth, cfg.WorldHeight) = (256, 128);
                (cfg.Width, cfg.Height) = (2048, 1024);
                return true;

            case "small":
                (cfg.WorldWidth, cfg.WorldHeight) = (512, 256);
                (cfg.Width, cfg.Height) = (4096, 2048);
                return true;

            case "full":
                (cfg.WorldWidth, cfg.WorldHeight) = (512, 256);
                (cfg.Width, cfg.Height) = (8192, 4096);
                return true;

            // Vanilla's exact dimensions: heightmap 18432x9216, provinces 9216x4608. Using them
            // removes map resolution as a variable altogether — WORLD_EXTENTS, PANNING_* and every
            // vanilla-sized terrain texture are then correct without an override.
            case "vanilla":
                (cfg.WorldWidth, cfg.WorldHeight) = (1024, 512);
                (cfg.Width, cfg.Height) = (18432, 9216);
                return true;

            default:
                return false;
        }
    }

    /// <summary>The preset whose dimensions match this config, or null if it has been customised.</summary>
    public static string? Match(MapConfig cfg)
    {
        var probe = new MapConfig();
        foreach (string name in Names)
        {
            Apply(name, probe);
            if (probe.Width == cfg.Width && probe.Height == cfg.Height) return name;
        }
        return null;
    }
}

public static class Program
{
    // WinForms requires a single-threaded apartment, which cannot be expressed with top-level
    // statements — hence the explicit entry point.
    [STAThread]
    public static int Main(string[] args)
    {
        var options = new GenerationOptions();
        var cfg = options.Config;

        string outDir = Path.Combine(AppContext.BaseDirectory, "out");
        int scale = 2;
        string? modDir = null;
        bool gui = args.Length == 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--gui":
                    gui = true;
                    break;

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
                        : GenerationOptions.DefaultModDir;
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
                    options.UseNoise = true;
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
                    options.WriteHistory = false;
                    break;

                // Ship only heightmap.png and let -mapeditor's repack build the packed/indirection
                // pair, which is what both the wiki and ck2rpg's tutorial prescribe.
                case "--no-packed":
                    options.WritePacked = false;
                    break;

                default:
                    if (MapPreset.Apply(args[i], cfg)) break;
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 1;
            }
        }

        if (gui)
        {
            ApplicationConfiguration.Initialize();
            System.Windows.Forms.Application.Run(new Gui.MainForm(options));
            return 0;
        }

        var result = Generator.Generate(options);
        if (modDir is not null) Generator.WriteMod(result, options, modDir);
        Generator.WriteDebugImages(result, outDir, scale);
        return 0;
    }
}
