using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen;

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

                // The heightmap the whole mod is built around. Required outside the GUI.
                case "--heightmap" when i + 1 < args.Length:
                    options.HeightmapPath = args[++i];
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

                // Rescale a heightmap drawn on somebody else's height scale onto CK3's. The value
                // is where the source puts its own sea level on the 0-255 scale — 51 for an Azgaar
                // export. It is advisory now that the land floor is detected rather than taken as a
                // minimum: it decides which pixels count as water, not what the land is anchored on.
                case "--normalize-heightmap":
                    cfg.Normalization = Config.HeightmapNormalization.Stretch;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                        cfg.SourceSeaLevel = double.Parse(args[++i],
                            System.Globalization.CultureInfo.InvariantCulture);
                    break;

                // Move land down onto the water plane without rescaling it. For a source whose
                // relief is already correct and only sits too high; see HeightmapNormalization.
                case "--shift-heightmap":
                    cfg.Normalization = Config.HeightmapNormalization.Shift;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                        cfg.SourceSeaLevel = double.Parse(args[++i],
                            System.Globalization.CultureInfo.InvariantCulture);
                    break;

                // What the highest land pixel becomes, on the 0-255 scale. Vanilla's own is 191.
                case "--land-top" when i + 1 < args.Length:
                    cfg.LandTop = double.Parse(args[++i],
                        System.Globalization.CultureInfo.InvariantCulture);
                    break;

                // Which percentile of land the top anchor is taken at. 100 anchors on the true
                // maximum and clips nothing.
                case "--land-top-percentile" when i + 1 < args.Length:
                    cfg.LandTopPercentile = double.Parse(args[++i],
                        System.Globalization.CultureInfo.InvariantCulture);
                    break;

                default:
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

        if (options.HeightmapPath is null)
        {
            Console.Error.WriteLine(
                "Usage: Ck3MapGen --heightmap <file.png> [--mod [dir]] [--seed n] [--out dir]");
            Console.Error.WriteLine(
                "       [--normalize-heightmap | --shift-heightmap [source sea level 0-255]]");
            Console.Error.WriteLine(
                "       [--land-top 0-255] [--land-top-percentile 0-100]");
            Console.Error.WriteLine(
                "This tool builds a CK3 mod around a heightmap; it does not generate terrain.");
            return 1;
        }

        Core.Stage.Begin();
        var result = Generator.Generate(options);
        if (modDir is not null) Generator.WriteMod(result, options, modDir);
        Core.Stage.Time("debug images", () => Generator.WriteDebugImages(result, outDir, scale));
        Core.Stage.Report();
        return 0;
    }
}
