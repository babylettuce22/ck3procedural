namespace Ck3MapGen.Emit;

/// <summary>
/// Copies the hand-kept files in BaseFilesToCopy/ into the mod.
///
/// That folder is not itself a mod root. Each immediate subfolder is one *file set*, and each set
/// is a mod root, so a file's path below its set folder is its path in the mod. Sets exist so a
/// feature can be switched off wholesale from <see cref="Config.MapConfig"/> without the writer
/// needing to know a single filename — <see cref="Wilderness"/> ships only when the wilderness and
/// colonisation system is enabled, and adding a second optional system means adding a folder and a
/// bool, not editing this class.
///
/// Runs last, and never overwrites: anything the pipeline generated wins over anything kept
/// here, so dropping a file in this folder can add to the mod but cannot silently replace part
/// of it.
/// </summary>
public static class StaticFileWriter
{
    /// <summary>Folder name, both in the repo and beside the built executable.</summary>
    public const string SourceFolder = "BaseFilesToCopy";

    /// <summary>Vanilla files nothing regenerates. Always copied.</summary>
    public const string Core = "Core";

    /// <summary>The static half of the wilderness and colonisation system. Gated.</summary>
    public const string Wilderness = "Wilderness";

    /// <summary>
    /// County ruination: the decay counter, the collapse, and the discovery when the stones come
    /// out again. Gated on <see cref="Config.MapConfig.EnableRuins"/> AND on
    /// <see cref="Wilderness"/>, never on its own.
    ///
    /// It is a separate set rather than more files in <see cref="Wilderness"/> because it is a much
    /// larger claim on a game than wilderness is: wilderness only ever says some ground was never
    /// taken, while this can take ground away from a player who was holding it. That deserves its
    /// own switch, and a switch is only honest if the files it governs can actually be withheld.
    ///
    /// It cannot stand alone. Everything here hands counties to a dummy under
    /// <c>wilderness_government</c>, marks the ground with buildings declared in
    /// <c>00_wilderness_buildings.txt</c>, and expects the colonisation flow to be the way back —
    /// so without the wilderness set every file in it dangles.
    /// </summary>
    public const string Ruins = "Ruins";

    /// <summary>
    /// The static half of the fantasy race system: the phenotype traits (including the visible
    /// Human trait), their assignment scripts, the long-lived races' fading, and the race trait
    /// icons. Gated on fantasy ethnicities being enabled, so a realistic map ships none of it —
    /// no race chips in the ruler designer, no fading events, no phenotype pulses.
    /// (The gen_race_skin GENE stays in Core: PortraitWriter writes it into every persistent DNA
    /// record on every map, so the declaration must always exist.)
    /// </summary>
    public const string Fantasy = "Fantasy";

    /// <summary>
    /// The society system: Restorationists sworn to a fallen house, and a cult derived from
    /// its host religion. The static half — events, interactions, effects, the panel — reads
    /// everything per-world through global variables that
    /// <see cref="RestorationWriter"/> and <see cref="CultWriter"/> set at game start, so no file here names a generated key.
    /// Enabled by default (<see cref="Config.MapConfig.EnableSocieties"/>).
    /// </summary>
    public const string Societies = "Societies";

    /// <summary>
    /// The hand-written society prototype the system grew out of: one membership trait with a rank
    /// track, one rite gated on it, and the approach event that puts the first member in the world.
    /// Kept as the reference implementation of the CK3 mechanisms it proved (activity gating, the
    /// cloned abduct scheme, the exposure-driven secret). Never shipped beside <see cref="Societies"/>:
    /// both define the society panel's scripted_guis, which the HUD tab calls by name.
    /// </summary>
    public const string SocietyPrototype = "SocietyPrototype";

    /// <summary>
    /// Overrides that are right only for a world with no vanilla peoples in it: today, the
    /// ecclesiastical government and its clergy re-worded without Christianity. Shipped unless
    /// Content Source is VanillaWorld, whose real Christian faiths need vanilla's text.
    /// <see cref="HierarchyFlavourWriter"/> writes the generated half under the same condition.
    /// </summary>
    public const string Procedural = "Procedural";

    /// <summary>
    /// Files that document or configure a set rather than belong in a mod.
    /// </summary>
    private static readonly string[] NotModContent = ["README.txt", "ignore.txt", ".ignore.txt"];

    /// <summary>
    /// The sets a world built with <paramref name="cfg"/> ships, in copy order. One list for the
    /// full write, <c>--static-only</c> and <see cref="StaticRefresh"/>, so the three cannot drift.
    /// </summary>
    public static List<string> SetsFor(Config.MapConfig cfg)
    {
        List<string> sets = [Core];
        if (cfg.EnableWilderness) sets.Add(Wilderness);

        // ANDed, never implied. Ruins hand counties to a dummy under wilderness_government and
        // expect the colonisation flow to be the way back, so shipping them without the wilderness
        // set would ship a system whose every reference dangles.
        if (cfg.EnableWilderness && cfg.EnableRuins) sets.Add(Ruins);
        if (cfg.EnableFantasyEthnicities && cfg.RaceMode != Config.MapConfig.FantasyRaceMode.HumanOnly)
            sets.Add(Fantasy);
        if (cfg.EnableSocieties) sets.Add(Societies);
        else if (cfg.EnableSocietyPrototype) sets.Add(SocietyPrototype);
        if (cfg.ContentSource != Config.MapConfig.ContentSourceMode.VanillaWorld) sets.Add(Procedural);
        return sets;
    }

    /// <summary>
    /// Paths a set carries that a full write then generates over, per world: the descriptor (the
    /// mod's name), the map tables (rescaled to the map's size by <see cref="MapTableWriter"/>) and
    /// the disaster regions (<see cref="CompatibilityWriter"/> fills them from the world's own
    /// provinces). A full write never copies these over its fresh output, because it skips files
    /// written during the run; a refresh of an old world has no run, so it has to be told.
    ///
    /// Found by comparing every set file against a fresh mod (2026-10-03). A new collision would
    /// show up the same way: a static path whose copy in a just-written mod differs from its source.
    /// </summary>
    public static bool WrittenPerWorld(string relativePath)
    {
        string path = relativePath.Replace('\\', '/');
        return path.Equals("descriptor.mod", StringComparison.OrdinalIgnoreCase)
               || path.Equals("map_data/geographical_regions/10_natural_disaster_regions.txt", StringComparison.OrdinalIgnoreCase)
               || (path.StartsWith("gfx/map/map_object_data/map_table_", StringComparison.OrdinalIgnoreCase)
                   && path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every mod file a set holds, as (source path, path in the mod), READMEs and ignore lists left out.</summary>
    public static IEnumerable<(string Source, string Relative)> FilesOf(string set)
    {
        string sourceDir = SetDirectory(set);
        if (!Directory.Exists(sourceDir)) yield break;
        foreach (string sourceFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (NotModContent.Contains(Path.GetFileName(sourceFile), StringComparer.OrdinalIgnoreCase)) continue;
            yield return (sourceFile, Path.GetRelativePath(sourceDir, sourceFile));
        }
    }

    /// <summary>
    /// Where a set lives beside the executable.
    /// </summary>
    public static string SetDirectory(string set)
        => Path.Combine(AppContext.BaseDirectory, SourceFolder, set);

    /// <summary>
    /// Root directory containing all file sets (BaseFilesToCopy).
    /// </summary>
    public static string BaseDirectory
        => Path.Combine(AppContext.BaseDirectory, SourceFolder);

    /// <param name="keep">
    /// Files to leave as they are even though they are older than the run, by set and path in the
    /// mod. Only <see cref="StaticRefresh"/> passes one.
    /// </param>
    /// <returns>How many files were copied, and how many of those replaced an older copy.</returns>
    public static (int Copied, int Refreshed) WriteAll(string modDir, IEnumerable<string> sets, DateTime runStarted,
        Func<string, string, bool>? keep = null)
    {
        int copied = 0, skipped = 0, refreshed = 0, ignored = 0;
        var written = new List<string>();

        // 1. Load global ignore patterns from BaseFilesToCopy/ignore.txt (if present)
        var globalIgnores = LoadIgnoreRules(BaseDirectory);

        foreach (string set in sets)
        {
            string sourceDir = SetDirectory(set);

            if (!Directory.Exists(sourceDir))
            {
                Console.WriteLine($"  static files: SKIPPED set '{set}' " +
                                  $"({SourceFolder}/{set} not found beside the executable)");
                continue;
            }

            // 2. Load set-specific ignore patterns from BaseFilesToCopy/<set>/ignore.txt (if present)
            var setIgnores = LoadIgnoreRules(sourceDir);
            var activeIgnores = globalIgnores.Concat(setIgnores).ToList();

            int setCopied = 0;

            foreach (string sourceFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDir, sourceFile);
                string fileName = Path.GetFileName(relativePath);

                // Skip READMEs and ignore lists
                if (NotModContent.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    continue;

                // Check against ignore rules
                if (IsFileIgnored(relativePath, fileName, activeIgnores))
                {
                    ignored++;
                    continue;
                }

                string targetFile = Path.Combine(modDir, relativePath);

                if (File.Exists(targetFile))
                {
                    if (File.GetLastWriteTimeUtc(targetFile) >= runStarted.AddMinutes(-1)
                        || keep?.Invoke(set, relativePath) == true)
                    {
                        skipped++;
                        continue;
                    }

                    File.Delete(targetFile);
                    refreshed++;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                File.Copy(sourceFile, targetFile);
                setCopied++;
            }

            copied += setCopied;
            written.Add($"{setCopied} from {set}");
        }

        Console.WriteLine($"  copied {copied} static files ({string.Join(", ", written)}; " +
                          (refreshed > 0 ? $"{refreshed} refreshed from an earlier run; " : "") +
                          (ignored > 0 ? $"{ignored} excluded via ignore.txt; " : "") +
                          $"{skipped} left alone, already generated)");
        return (copied, refreshed);
    }

    /// <summary>
    /// Reads ignore patterns from ignore.txt or .ignore.txt in the target directory.
    /// Supports comments (#) and trims whitespace.
    /// </summary>
    private static List<string> LoadIgnoreRules(string directory)
    {
        var rules = new List<string>();
        if (!Directory.Exists(directory)) return rules;

        foreach (string candidate in new[] { "ignore.txt", ".ignore.txt" })
        {
            string path = Path.Combine(directory, candidate);
            if (!File.Exists(path)) continue;

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                // Normalize slashes to forward slashes for consistent matching
                rules.Add(line.Replace('\\', '/').Trim('/'));
            }
        }

        return rules;
    }

    /// <summary>
    /// Checks if a file matches any of the ignore rules (by exact relative path, filename, or directory prefix).
    /// </summary>
    private static bool IsFileIgnored(string relativePath, string fileName, List<string> rules)
    {
        string normalizedRelPath = relativePath.Replace('\\', '/');

        foreach (string rule in rules)
        {
            // 1. Exact filename match (e.g. "wilderness_genes.txt")
            if (fileName.Equals(rule, StringComparison.OrdinalIgnoreCase))
                return true;

            // 2. Exact relative path match (e.g. "gui/window_character.gui")
            if (normalizedRelPath.Equals(rule, StringComparison.OrdinalIgnoreCase))
                return true;

            // 3. Directory / prefix match (e.g. "gui/unused" or "common/buildings/")
            if (normalizedRelPath.StartsWith(rule + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}