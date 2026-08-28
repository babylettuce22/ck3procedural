using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

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
        bool staticOnly = false;
        bool guiOnly = false;
        bool preview3d = false;
        string? previewTarget = null;
        string previewOut = Path.Combine(AppContext.BaseDirectory, "gui-preview.html");
        int previewRows = 1;
        bool fitHeightmap = false;
        bool allowUnverifiedSize = false;

        // Tri-state on purpose. The debug PNGs cost about a tenth of a full run — six seconds on a
        // 9216x4608 map — and a run that is writing a mod almost never wants them, but a run with
        // no --mod has nothing else to show for itself, so the default has to depend on that.
        // null means "nobody said"; --debug-images / --no-debug-images say it, and so does --out,
        // which in this path exists for no other purpose.
        bool? debugImages = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--gui":
                    gui = true;
                    break;

                // Draw a .gui widget to an HTML page instead of generating anything. Takes a widget
                // name from the indexed files, or the path to a .gui file.
                case "--preview" when i + 1 < args.Length:
                    previewTarget = args[++i];
                    gui = false;
                    break;

                case "--preview-out" when i + 1 < args.Length:
                    previewOut = args[++i];
                    break;

                // How many entries to draw for a datamodel list. A list previewed at one row hides
                // every question worth asking about it.
                case "--preview-rows" when i + 1 < args.Length:
                    previewRows = int.Parse(args[++i]);
                    break;

                case "--static-only": 
                    staticOnly = true;
                    break;

                case "--gui-only": 
                    guiOnly = true;
                    break;

                case "--seed" when i + 1 < args.Length:
                    cfg.Seed = int.Parse(args[++i]);
                    break;

                case "--out" when i + 1 < args.Length:
                    outDir = args[++i];
                    debugImages ??= true;
                    break;

                // The debug PNGs — elevation, terrain, provinces, climate, drainage, rivers. Off
                // by default on a run that writes a mod, because nothing downstream reads them and
                // they are the fourth-largest phase in the run.
                case "--debug-images":
                    debugImages = true;
                    break;

                case "--no-debug-images":
                    debugImages = false;
                    break;

                // Emit the mod itself. Defaults to the launcher's mod folder when given no value,
                // and takes a bare name as a folder inside it — `--mod "Second Map"` is by far the
                // commoner intent than a path, now that the mod folder is searched for rather than
                // assumed and so is not necessarily somewhere the caller can spell.
                case "--mod":
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        modDir = ModDir(args[++i]);

                        // A named mod calls itself that in the launcher too. Left alone when no name
                        // was given, so the default folder still ships as "Procedural Map" rather
                        // than as "proceduralmap".
                        options.ModName = Path.GetFileName(modDir.TrimEnd(
                            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    }
                    else
                    {
                        modDir = GenerationOptions.DefaultModDir;
                    }
                    break;

                // Which install to read vanilla data from. Only needed when the search guesses
                // wrong, or picks the wrong one of two installs.
                case "--game" when i + 1 < args.Length:
                    options.GameDir = Core.GameLocator.Normalize(args[++i]) ?? args[i];
                    break;

                case "--scale" when i + 1 < args.Length:
                    scale = int.Parse(args[++i]);
                    break;

                // The heightmap the whole mod is built around. Required outside the GUI, unless
                // --forge produces one instead.
                case "--heightmap" when i + 1 < args.Length:
                    if (options.Heightmap is not null)
                    {
                        Console.Error.WriteLine("--heightmap and --forge are alternatives; give one of them.");
                        return 1;
                    }
                    options.HeightmapPath = args[++i];
                    break;

                // A Forge pipeline preset instead of a PNG: the heightmap is produced in memory at
                // the preset's base resolution, exactly as the GUI's Heightmap tab does it. The
                // preset is the one CK3 Heightmap Forge saves, so a map tuned there can be built
                // from a terminal without exporting it first.
                case "--forge" when i + 1 < args.Length:
                {
                    if (options.Heightmap is not null)
                    {
                        Console.Error.WriteLine("--heightmap and --forge are alternatives; give one of them.");
                        return 1;
                    }

                    string presetPath = args[++i];
                    var pipeline = new NoiseTool.Pipeline.HeightPipeline();
                    var loaded = NoiseTool.Pipeline.PresetIO.Load(pipeline, presetPath);
                    foreach (string warning in loaded.Warnings)
                        Console.Error.WriteLine($"  {Path.GetFileName(presetPath)}: {warning}");

                    options.Heightmap = new MapGen.ForgeHeightmapProvider(
                        pipeline, Path.GetFileNameWithoutExtension(presetPath));
                    break;
                }

                // Optional. An Azgaar "Full" JSON export to borrow from, alongside — never instead
                // of — the heightmap. Without it every name and border is generated as before.
                case "--azgaar" when i + 1 < args.Length:
                    options.AzgaarJsonPath = args[++i];
                    cfg.AzgaarJsonPath = options.AzgaarJsonPath;
                    break;

                // Optional. A black-and-white PNG painted over provinces.png saying where the
                // impassable mountains go, for the ranges a relief score will not find on its own.
                case "--impassable-mask" when i + 1 < args.Length:
                    cfg.ImpassableMaskPath = args[++i];
                    break;

                // Whether the paint cuts the province partition or merely picks provinces out of
                // it; see ImpassableMaskMode. Snap is the default.
                case "--impassable-mask-mode" when i + 1 < args.Length:
                    cfg.ImpassableMaskMode = Enum.Parse<ImpassableMaskMode>(args[++i], ignoreCase: true);
                    break;

                // How big a barony is relative to vanilla's. 2 makes each one twice as wide and a
                // quarter as numerous; the rest of the title hierarchy follows.
                case "--county-scale" when i + 1 < args.Length:
                    cfg.CountyScale = double.Parse(args[++i],
                        System.Globalization.CultureInfo.InvariantCulture);
                    break;

                // Exposed on the command line as well as in the GUI because it is the one weapon
                // setting worth sweeping: it trades disk against variety, and comparing two sizes
                // means two runs.
                case "--weapon-pool" when i + 1 < args.Length:
                    cfg.WeaponPoolSizePerKind = int.Parse(args[++i],
                        System.Globalization.CultureInfo.InvariantCulture);
                    break;

                // The two halves of the calendar. Separate flags because they answer separate
                // questions — what year the world says it is, and how advanced it is — and the
                // whole point of splitting them is that one can move without the other.
                case "--start-year" when i + 1 < args.Length:
                    cfg.StartYear = int.Parse(args[++i],
                        System.Globalization.CultureInfo.InvariantCulture);
                    break;

                case "--era-anchor" when i + 1 < args.Length:
                    cfg.EraAnchorYear = int.Parse(args[++i],
                        System.Globalization.CultureInfo.InvariantCulture);
                    break;

                // Which way the world's faiths, cultures and rulers lean on inheritance and
                // knighthood; see GenderPreference. Historical is the default.
                case "--gender" when i + 1 < args.Length:
                    cfg.Gender = Enum.Parse<GenderPreference>(args[++i], ignoreCase: true);
                    break;

                case "--no-history":
                    options.WriteHistory = false;
                    break;

                // Hands out titles down the de jure tree instead of simulating centuries of
                // conquest first. Here as well as on the config object because the two produce
                // visibly different worlds from one heightmap, and comparing them from a terminal
                // is the only cheap way to see what the simulation actually changed.
                case "--no-formation":
                    cfg.SimulateFormation = false;
                    break;

                // The two dials on that simulation. Exposed for the same reason: the only way to
                // tell whether a world is fragmented because of the terrain or because of the
                // settings is to hold one fixed and move the other.
                case "--formation-years" when i + 1 < args.Length:
                    cfg.FormationYears = int.Parse(args[++i]);
                    break;

                case "--formation-turbulence" when i + 1 < args.Length:
                    cfg.FormationTurbulence = double.Parse(
                        args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;

                // Ships the hand-written society prototype. Here as well as on the config object
                // because the whole point of it is to be looked at in a running game, and the
                // headless loop is how a world gets built to look at it in. See
                // BaseFilesToCopy/Societies/README.txt.
                case "--societies":
                    cfg.EnableSocieties = true;
                    break;

                // Ship only heightmap.png and let -mapeditor's repack build the packed/indirection
                // pair, which is what both the wiki and ck2rpg's tutorial prescribe.
                case "--no-packed":
                    options.WritePacked = false;
                    break;

                // Ships the source heightmap's relief as authored instead of scaling it to the
                // map's size. See MapConfig.ScaleReliefWithMapSize; here so the two can be
                // compared headlessly on one binary, which is the only way to tell the pass apart
                // from everything else that moves when a map is regenerated.
                case "--no-relief-scale":
                    cfg.ScaleReliefWithMapSize = false;
                    break;

                // Kill switch for the city-scatter prototype. See MapConfig.EnableCityScatter.
                case "--no-city-scatter":
                    cfg.EnableCityScatter = false;
                    break;

                // Extra zoom steps of 3D terrain before the map goes flat to the paper map. See
                // MapConfig.FlatMapHandoffBias; 0 is vanilla's own handoff.
                case "--flat-map-bias" when i + 1 < args.Length:
                    cfg.FlatMapHandoffBias = int.Parse(args[++i],
                        System.Globalization.CultureInfo.InvariantCulture);
                    break;

                // Pulls the flat-map handoff in on maps below vanilla's size by the terrain LOD
                // octaves they cannot supply. See MapConfig.ScaleHandoffToDetail. Off by default
                // because it shows *less* of a small map as terrain; it is here so the trade can be
                // compared headlessly on one binary against --flat-map-bias, which is the lever for
                // the opposite preference.
                case "--detail-handoff":
                    cfg.ScaleHandoffToDetail = true;
                    break;

                // The packer's tile step, in heightmap pixels: 32, 64 or 128, or 0 to choose by map
                // width. Here because the three are worth measuring against each other on a real
                // map, and the packer reports its atlas size and worst error on every build.
                case "--tile-step" when i + 1 < args.Length:
                    cfg.HeightmapTileStep = int.Parse(args[++i],
                        System.Globalization.CultureInfo.InvariantCulture);
                    break;

                // Refine tiles so no two neighbours differ by more than one detail level. Off by
                // default because vanilla does not do it; see MapConfig.BalanceNeighbourLods.
                case "--balance-lods":
                    cfg.BalanceNeighbourLods = true;
                    break;

                // The scale, in heightmap pixels, that separates mountains from detail when relief
                // is scaled with map size. 0 goes back to compressing all relief uniformly.
                case "--relief-detail" when i + 1 < args.Length:
                    cfg.ReliefDetailRadius = int.Parse(args[++i],
                        System.Globalization.CultureInfo.InvariantCulture);
                    break;

                // Diagnostic: ship vanilla's camera ladder instead of this map's, to rule the
                // camera overrides out while looking at a rendering artefact.
                case "--vanilla-camera":
                    cfg.VanillaCamera = true;
                    break;

                // Resample --heightmap onto a size the packer can tile, rather than refusing it.
                // A mode rather than a size, because the size to fit to is not known until the
                // file's own dimensions have been read, which happens below.
                case "--fit-heightmap":
                    fitHeightmap = true;
                    break;

                // Build at the heightmap's own size even though it is not one CK3 is known to
                // render, to find out whether it does. The GUI offers the same answer; see
                // MapGen.TileFit for what "known" means and how the list grows.
                case "--allow-unverified-size":
                    allowUnverifiedSize = true;
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

                // Fantasy races. The GUI's checkbox and dropdown, reachable from a terminal —
                // "low", "high" or "exotic" turn ethnicities on at that intensity, "off" is the
                // default human-only palette.
                case "--races" when i + 1 < args.Length:
                    string races = args[++i].ToLowerInvariant();
                    cfg.EnableFantasyEthnicities = races != "off";
                    cfg.RaceMode = races switch
                    {
                        "off" => Config.MapConfig.FantasyRaceMode.HumanOnly,
                        "low" => Config.MapConfig.FantasyRaceMode.LowFantasy,
                        "high" => Config.MapConfig.FantasyRaceMode.HighFantasy,
                        "exotic" => Config.MapConfig.FantasyRaceMode.ExoticSurreal,
                        _ => throw new ArgumentException($"--races {races}: expected off, low, high or exotic"),
                    };
                    break;

                // Render the heightmap in 3D and write the frames out, without generating
                // anything. The same renderer the GUI's Source view drives, reachable headlessly
                // so a heightmap can be judged from a terminal or a script.
                case "--preview3d":
                    preview3d = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                        outDir = args[++i];
                    break;

                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 1;
            }
        }

        if (fitHeightmap && allowUnverifiedSize)
        {
            Console.Error.WriteLine(
                "--fit-heightmap and --allow-unverified-size are alternatives; give one of them.");
            return 1;
        }

        if (fitHeightmap)
        {
            if (options.Heightmap is not MapGen.FileHeightmapProvider file)
            {
                Console.Error.WriteLine(
                    "--fit-heightmap applies to --heightmap. A Forge preset already chooses its "
                    + $"own output size, so set that to {MapGen.TileFit.KnownList} instead.");
                return 1;
            }

            if (!File.Exists(file.Path))
            {
                Console.Error.WriteLine($"No heightmap at {file.Path}");
                return 1;
            }

            var (fileWidth, fileHeight) = MapGen.TileFit.Measure(file.Path);
            var target = MapGen.TileFit.Nearest(fileWidth, fileHeight);

            if (MapGen.TileFit.Fits(fileWidth, fileHeight))
                Console.WriteLine($"--fit-heightmap: {fileWidth}x{fileHeight} is a size CK3 renders; "
                                  + "nothing to resample.");
            else
            {
                options.Heightmap = new MapGen.FileHeightmapProvider(file.Path, target);
                Console.WriteLine($"--fit-heightmap: {fileWidth}x{fileHeight} -> "
                                  + $"{target.Width}x{target.Height}");
            }
        }

        if (allowUnverifiedSize)
        {
            // The provider was built during parsing, before the flag could be known; rebuild it
            // with the flag rather than threading a mutable setting through both providers.
            options.Heightmap = options.Heightmap switch
            {
                MapGen.FileHeightmapProvider file
                    => new MapGen.FileHeightmapProvider(file.Path, null, allowUnverifiedSize: true),
                MapGen.ForgeHeightmapProvider forge
                    => new MapGen.ForgeHeightmapProvider(forge.Pipeline, forge.Name, allowUnverifiedSize: true),
                var other => other,
            };
        }

        if (preview3d)
        {
            if (options.Heightmap is null)
            {
                Console.Error.WriteLine("--preview3d needs --heightmap <path> or --forge <preset.json>.");
                return 1;
            }

            return Preview3d(options.Heightmap, cfg, outDir);
        }

        // Handle static-only copy before checking GUI or Heightmap constraints
        if (staticOnly)
        {
            modDir ??= GenerationOptions.DefaultModDir;

            Console.WriteLine($"Running in static-only mode. Destination: {modDir}");

            var sets = new List<string> { Ck3MapGen.Emit.StaticFileWriter.Core };
            if (cfg.EnableWilderness)
            {
                sets.Add(Ck3MapGen.Emit.StaticFileWriter.Wilderness);
            }
            if (cfg.EnableFantasyEthnicities && cfg.RaceMode != MapConfig.FantasyRaceMode.HumanOnly)
            {
                sets.Add(Ck3MapGen.Emit.StaticFileWriter.Fantasy);
            }
            if (cfg.EnableSocieties)
            {
                sets.Add(Ck3MapGen.Emit.StaticFileWriter.Societies);
            }

            // Using UtcNow as runStarted ensures all previously existing files in the target
            // folder are considered older than this run and will be overwritten/refreshed.
            Ck3MapGen.Emit.StaticFileWriter.WriteAll(modDir, sets, DateTime.UtcNow);
            return 0;
        }

        // Draw a widget and stop. Nothing is generated and nothing is written to the mod, so this
        // is safe to run against a live mod folder while the game is open.
        if (previewTarget is not null)
        {
            if (string.IsNullOrWhiteSpace(options.GameDir) || !Core.GameLocator.IsGameDir(options.GameDir))
            {
                Console.Error.WriteLine("Error: Crusader Kings III game directory not found. Please specify with --game <path>.");
                return 1;
            }

            return Core.GuiPreviewCommand.Run(previewTarget, options.GameDir, modDir, previewOut,
                previewRows);
        }

        if (guiOnly)
        {
            modDir ??= GenerationOptions.DefaultModDir;

            if (string.IsNullOrWhiteSpace(options.GameDir) || !Core.GameLocator.IsGameDir(options.GameDir))
            {
                Console.Error.WriteLine("Error: Crusader Kings III game directory not found. Please specify with --game <path>.");
                return 1;
            }

            Console.WriteLine($"Running in GUI-only mode.");
            Console.WriteLine($"  Game folder: {options.GameDir}");
            Console.WriteLine($"  Mod folder:  {modDir}");

            // 1. Write/patch frontend_main.gui (disabling cold-boot portrait crash & injecting watermark)
            FrontendWriter.WriteFrontend(modDir, options.GameDir);

            // 2. Write/patch in-game views (county view, character view, title view)
            //
            // --societies here as well as on a full run: the HUD tab is a .gui edit, so it is one
            // of the things --gui-only exists to iterate on without regenerating a world.
            GuiWriter.WriteAll(modDir, options.GameDir, cfg.EnableSocieties);

            return 0;
        }

        if (gui)
        {
            ApplicationConfiguration.Initialize();
            System.Windows.Forms.Application.Run(new AppGUI.MainForm(options));
            return 0;
        }

        if (options.Heightmap is null)
        {
            Console.Error.WriteLine(
                "Usage: Ck3MapGen --heightmap <file.png> | --forge <preset.json>");
            Console.Error.WriteLine(
                "       [--mod [name|dir]] [--game dir]");
            Console.Error.WriteLine(
                "       [--seed n] [--out dir]");
            Console.Error.WriteLine(
                "       [--debug-images | --no-debug-images]  debug PNGs; default is on only when no --mod is written");
            Console.Error.WriteLine(
                "       [--normalize-heightmap | --shift-heightmap [source sea level 0-255]]");
            Console.Error.WriteLine(
                $"       [--fit-heightmap]  resamples the PNG to the nearest of {MapGen.TileFit.KnownList}");
            Console.Error.WriteLine(
                "       [--allow-unverified-size]  builds at the heightmap's own size, to test whether CK3 renders it");
            Console.Error.WriteLine(
                "       [--land-top 0-255] [--land-top-percentile 0-100]");
            Console.Error.WriteLine(
                "       [--azgaar <export.json>]  optional; borrows names from an Azgaar map");
            Console.Error.WriteLine(
                "       [--impassable-mask <mask.png>]  optional; white = impassable, black = passable, painted over provinces.png");
            Console.Error.WriteLine(
                "       [--impassable-mask-mode snap|touch]  snap (default) cuts provinces to the paint; touch turns whole provinces");
            Console.Error.WriteLine(
                "       [--gender historical|mixed|femaledominated]  which way the world's laws and rulers lean; historical is the default");
            Console.Error.WriteLine(
                "This tool builds a CK3 mod around a heightmap: one you supply as a 16-bit PNG, or "
                + "one produced from a CK3 Heightmap Forge preset.");
            return 1;
        }

        Console.WriteLine($"Game folder: {options.GameDir}");

        Core.Stage.Begin();
        Core.RunLog.Begin();
        try
        {
            var result = Generator.Generate(options);
            if (modDir is not null) Generator.WriteMod(result, options, modDir);
            if (debugImages ?? modDir is null)
                Core.Stage.Time("debug images", () => Generator.WriteDebugImages(result, outDir, scale));
            Core.Stage.Report();
        }
        catch (Exception ex) when (modDir is not null)
        {
            // The record goes into whatever was written before the failure, so a half-written
            // folder says why. Printed once, to stdout, and the exit code carries the rest.
            Console.WriteLine(ex);
            Core.RunLog.Write(modDir, options, $"failed: {ex.Message}");
            return 1;
        }

        if (modDir is not null) Core.RunLog.Write(modDir, options, "completed");
        return 0;
    }

    /// <summary>
    /// Renders the heightmap in 3D from four sides, plus one frame of what CK3 will actually draw
    /// after the packer has decimated it, and writes them as PNGs.
    /// </summary>
    private static int Preview3d(MapGen.HeightmapProvider source, MapConfig cfg, string outDir)
    {
        if (source is MapGen.FileHeightmapProvider onDisk && !File.Exists(onDisk.Path))
        {
            Console.Error.WriteLine($"No heightmap at {onDisk.Path}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        var loaded = source.Produce(cfg, CancellationToken.None, MapGen.ConsoleProgress.Instance);
        MapGen.HeightmapSource.Diagnose(loaded, cfg);

        // What the game will be handed, not what was drawn — the normaliser is the whole reason a
        // heightmap that looks fine in an image editor can ship as a plateau.
        var normalized = loaded.Levels(cfg);

        const int Width = 1600, Height = 900;
        var field = AppGUI.Heightfield.Downsample(normalized, loaded.Width, loaded.Height, AppGUI.Heightfield.PreviewCols);

        Console.WriteLine($"  field {field.Cols}x{field.Rows}, " +
                          $"{100 * field.LandShare:F1}% land, highest {field.LandMax}/65535");

        var view = AppGUI.HeightfieldView.Default;

        foreach (var (name, yaw) in ((string, double)[])
                 [("ne", 0.7), ("se", 2.4), ("sw", 3.9), ("nw", 5.5)])
        {
            var frame = AppGUI.HeightfieldRenderer.Render(
                field, view with { Yaw = yaw }, Width, Height);

            string file = Path.Combine(outDir, $"preview3d_{name}.png");
            Io.PngWriter.WriteRgb8(file, frame.Width, frame.Height, frame.Rgb);
            Console.WriteLine($"  wrote {file}");
        }

        // The same angle again, through the packer, so the two can be flipped between.
        var packed = Emit.HeightmapPacker.Reconstruct(
            normalized, loaded.Width, loaded.Height, cfg.HeightmapSagBudget,
            Emit.HeightmapPacker.TileStepFor(cfg), cfg.BalanceNeighbourLods);

        long changed = 0, sum = 0;
        int worst = 0;
        for (long i = 0; i < packed.LongLength; i++)
        {
            int d = Math.Abs(packed[i] - normalized[i]);
            if (d == 0) continue;
            changed++;
            sum += d;
            if (d > worst) worst = d;
        }

        Console.WriteLine($"  packing moves {100.0 * changed / packed.LongLength:F1}% of pixels, " +
                          $"mean {(changed == 0 ? 0 : (double)sum / changed) / MapDataWriter.Step255:F2}/255, " +
                          $"worst {(double)worst / MapDataWriter.Step255:F2}/255");

        var packedField = AppGUI.Heightfield.Downsample(packed, loaded.Width, loaded.Height, AppGUI.Heightfield.PreviewCols);
        var packedFrame = AppGUI.HeightfieldRenderer.Render(packedField, view, Width, Height);

        string packedPath = Path.Combine(outDir, "preview3d_as_ck3_renders_it.png");
        Io.PngWriter.WriteRgb8(packedPath, packedFrame.Width, packedFrame.Height, packedFrame.Rgb);
        Console.WriteLine($"  wrote {packedPath}");

        var plain = AppGUI.HeightfieldRenderer.Render(field, view, Width, Height);
        string plainPath = Path.Combine(outDir, "preview3d_source.png");
        Io.PngWriter.WriteRgb8(plainPath, plain.Width, plain.Height, plain.Rgb);
        Console.WriteLine($"  wrote {plainPath}");

        // A straight vertical pan, three frames. The map must slide along one axis and hold its
        // shape; if the two pan components carry different units it skews instead, which is what
        // the vertical drag used to do on any map that was not square.
        var panned = view.Zoomed(0.45);
        for (int s = -1; s <= 1; s++)
        {
            var step = panned.Panned(0, s * 0.22);
            var frame = AppGUI.HeightfieldRenderer.Render(field, step, Width, Height);
            string file = Path.Combine(outDir, $"preview3d_pan{s + 1}.png");
            Io.PngWriter.WriteRgb8(file, frame.Width, frame.Height, frame.Rgb);
            Console.WriteLine($"  wrote {file}");
        }

        // A pitch sweep. The map must stay the same size through it — tilting is an orbit, not a
        // zoom, and re-fitting the distance per frame is what made it behave like one.
        foreach (double pitch in (double[])[0.25, 0.55, 1.05])
        {
            var tilted = view with { Pitch = pitch };
            var frame = AppGUI.HeightfieldRenderer.Render(field, tilted, Width, Height);
            string file = Path.Combine(outDir, $"preview3d_tilt{pitch:F2}.png");
            Io.PngWriter.WriteRgb8(file, frame.Width, frame.Height, frame.Rgb);
            Console.WriteLine($"  wrote {file}");
        }

        // And the same pair zoomed in, which is the only scale the packer's decimation is visible
        // at — across a whole map a 64-pixel tile is smaller than a screen pixel.
        var close = view.Zoomed(0.18) with { PanX = 0.10, PanY = -0.14 };

        foreach (var (name, from) in ((string, AppGUI.Heightfield)[])
                 [("close_source", field), ("close_as_ck3_renders_it", packedField)])
        {
            var frame = AppGUI.HeightfieldRenderer.Render(from, close, Width, Height);
            string file = Path.Combine(outDir, $"preview3d_{name}.png");
            Io.PngWriter.WriteRgb8(file, frame.Width, frame.Height, frame.Rgb);
            Console.WriteLine($"  wrote {file}");
        }

        return 0;
    }

    /// <summary>
    /// A <c>--mod</c> value as a directory: a path is taken as written, a bare name is a folder of
    /// that name inside the launcher's mod folder.
    /// </summary>
    private static string ModDir(string value)
        => Path.IsPathRooted(value) || value.Contains(Path.DirectorySeparatorChar)
           || value.Contains(Path.AltDirectorySeparatorChar)
            ? value
            : Path.Combine(GenerationOptions.ModRoot, value);
}