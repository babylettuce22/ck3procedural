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
                "This tool builds a CK3 mod around a heightmap; it does not generate terrain.");
            return 1;
        }

        var result = Generator.Generate(options);
        if (modDir is not null) Generator.WriteMod(result, options, modDir);
        Generator.WriteDebugImages(result, outDir, scale);
        return 0;
    }
}
