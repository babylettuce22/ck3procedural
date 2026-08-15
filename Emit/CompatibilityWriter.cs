using System.Text;
using System.Text.RegularExpressions;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Keeps vanilla and DLC script working against a map that shares none of its identifiers.
///
/// The rule learned the hard way: **do not blank vanilla data — re-declare its identifiers.**
/// A missing key is a hard script error, not a warning, because base-game and DLC content
/// hardcodes region and title keys everywhere.
/// </summary>
public static class CompatibilityWriter
{
    private static readonly System.Globalization.CultureInfo Invariant =
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>
    /// Vanilla's camera extents, and the province map they are authored against. Camera space is
    /// provinces space, so all four scale with <see cref="Config.MapConfig.MapScale"/>.
    ///
    /// Neither panning bound is the map's own size, which is why they are copied rather than
    /// assumed: 9090 is inside a 9216-wide map while 4696 is outside a 4608-tall one. The bound is
    /// on the camera's centre, so the horizontal one stops short of the edge and the vertical one
    /// overshoots to let the view sit past the poles. Scaling vanilla's numbers keeps that
    /// asymmetry instead of inventing a model for it.
    /// </summary>
    private const double VanillaPanningWidth = 9090;
    private const double VanillaPanningHeight = 4696;
    private const int VanillaProvinceHeight = 4608;

    /// <summary>
    /// Vanilla's ZOOM_STEPS ladder, purely so a starting step can be chosen from it. The ladder
    /// itself is left alone — it is shared with five parallel tilt arrays that have to stay the
    /// same length, and camera height is absolute, so the steps mean the same thing on any map.
    /// </summary>
    private static readonly int[] ZoomSteps =
    [
        70, 90, 114, 142, 174, 210, 250, 295, 344, 396, 453, 513, 576, 643, 713, 787, 865, 948,
        1036, 1130, 1233, 1345, 1470, 1609, 1768, 1949, 2159, 2406, 2699, 3050, 3477, 4000, 4649,
        5464, 6500
    ];

    /// <summary>Vanilla's START_ZOOM_STEP, 33, is this height on its 9216-wide map.</summary>
    private const double VanillaStartZoomHeight = 5464;

    /// <summary>
    /// Vanilla's FLAT_MAP_ZOOM_STEP — the step at which the terrain gives way to the paper map on
    /// the tabletop.
    ///
    /// This has to be overridden for the same reason every other zoom step does: a step is an
    /// absolute camera height, and on a smaller map the whole world is in view far below step 21.
    ///
    /// It is specifically load-bearing for <see cref="MapTableWriter"/>. Vanilla's map-table layers
    /// fade in at exactly 21, so the tabletop appears on the same frame the map goes flat. Scaling
    /// the layer fade — which MapTableWriter does — while leaving this at 21 pulls the two apart and
    /// leaves a window of nine-odd zoom steps where the physical table is drawn under a map that is
    /// still 3D terrain. That is worse than either error alone, so the pair moves together.
    /// </summary>
    private const int VanillaFlatMapZoomStep = 21;

    /// <summary>
    /// Vanilla's government list, plus ours.
    ///
    /// A government type declared in <c>common/governments</c> is NOT registered until its key also
    /// appears in <c>NGovernment.GOVERNMENT_TYPES</c>. Miss it and the game logs a wall of
    /// "Could not find the preregistered modifier type 'x_government_opinion'" — one per contract
    /// modifier — and the government half-exists thereafter. ck3-tiger does not catch this: the
    /// script files are all valid, and the missing piece is an engine registration list.
    ///
    /// Read from the installed game rather than hardcoded. The list is thirty-odd entries that
    /// Paradox adds to every major patch, and a stale copy would silently *remove* whichever
    /// governments were added since — a far worse failure than the one it fixes.
    /// </summary>
    private static string GovernmentTypes(string gameDir, Config.MapConfig cfg)
    {
        string source = Path.Combine(gameDir, "common", "defines", "00_defines.txt");
        if (!cfg.EnableWilderness || !File.Exists(source)) return "";

        string text = File.ReadAllText(source);

        int start = text.IndexOf("GOVERNMENT_TYPES", StringComparison.Ordinal);
        if (start < 0) return "";

        int open = text.IndexOf('{', start);
        int close = text.IndexOf('}', open);
        if (open < 0 || close < 0) return "";

        var entries = System.Text.RegularExpressions.Regex
            .Matches(text[(open + 1)..close], "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

        if (entries.Count == 0) return "";
        entries.Add("wilderness_government");

        var sb = new StringBuilder();
        sb.Append("\n# Vanilla's list, read from the installed game, plus the wilderness government.\n");
        sb.Append("# A government absent from here is never registered, whatever common/governments says.\n");
        sb.Append("NGovernment = {\n\tGOVERNMENT_TYPES = {\n");
        foreach (string entry in entries) sb.Append($"\t\t\"{entry}\"\n");
        sb.Append("\t}\n}\n");

        Console.WriteLine($"  defines: GOVERNMENT_TYPES {entries.Count} entries "
                          + $"({entries.Count - 1} vanilla + wilderness)");

        return sb.ToString();
    }

    /// <summary>
    /// Overrides NJominiMap so the engine's world size matches the province map we actually
    /// ship. This is not optional and it is easy to miss.
    ///
    /// WORLD_EXTENTS_X/Z are in *provinces-map* space and vanilla's values (9215 / 4607) are
    /// size-minus-one for its 9216x4608 map. Leaving them alone means CK3 addresses a world
    /// several times larger than our provinces.png, so every province centroid, locator,
    /// pathfinding node and terrain lookup lands in the wrong place — with nothing logged,
    /// because none of it is a script error.
    /// </summary>
    public static void WriteDefines(string modDir, string gameDir, Config.MapConfig cfg)
    {
        string dir = Path.Combine(modDir, "common", "defines");
        Directory.CreateDirectory(dir);

        // Sorts last on purpose. Defines are merged across every file in the directory and the
        // last one loaded wins, so a baseline file like ck2rpg's 01_gen_defines.txt would
        // otherwise silently override our world size with the template map's.

        // WORLD_EXTENTS_Y and WATERLEVEL stay at vanilla's values on every map size.
        //
        // A heightmap value has to mean the same height everywhere: a smaller map is a smaller
        // *region* at the same scale, not the same world shrunk, so one pixel is the same distance
        // and one height step is the same height. These were briefly scaled by map size, which was
        // an attempt to cancel out terrain that generated too steep on small maps — two errors
        // pointing opposite ways rather than one fix. The terrain side is corrected in
        // MapConfig.SlopeScaleFor; this side goes back to being constant.
        //
        // The ratio is load-bearing either way: vanilla's own comment pins it, `WATERLEVEL = 3 ###
        // 0.06 in 0-1, 19 in 0-255`, and 3/50 is exactly 0.06. Move one without the other and the
        // waterline stops landing on 19/255, which MapDataWriter.WaterLevel16 and both hypsometric
        // curves are built around.
        const string extentY = "50";
        const string waterLevel = "3";

        ParadoxText.WriteBom(Path.Combine(dir, "zz_generated_defines.txt"),
            $$"""
              # World size must match map_data/provinces.png, not vanilla's map.
              NJominiMap = {
              	WORLD_EXTENTS_X = {{cfg.ProvinceWidth - 1}}
              	WORLD_EXTENTS_Y = {{extentY}}
              	WORLD_EXTENTS_Z = {{cfg.ProvinceHeight - 1}}
              	WATERLEVEL = {{waterLevel}}
              }
              {{GovernmentTypes(gameDir, cfg)}}
              """);

        Console.WriteLine($"  defines: WORLD_EXTENTS {cfg.ProvinceWidth - 1} x {extentY} x {cfg.ProvinceHeight - 1}, " +
                          $"WATERLEVEL {waterLevel} (vanilla 9215 x 50 x 4607, 3)");

        WriteCameraDefines(modDir, cfg);
    }

    /// <summary>
    /// Overrides NCamera so the camera is bounded by the map we ship rather than by vanilla's.
    ///
    /// Written into <c>common/defines/graphic/</c>, next to vanilla's own 00_graphics.txt, rather
    /// than alongside our NJominiMap override one directory up. Defines merge across the whole
    /// tree and the last file loaded wins, so being in the same directory is what makes "sorts
    /// after 00_graphics.txt" a fact about one directory listing instead of an assumption about
    /// how the loader walks subdirectories.
    ///
    /// START_LOOK_AT is the reason this matters beyond tidiness. Vanilla opens the camera at
    /// { 5000 0 2300 }, which is the middle of a 9216x4608 map and *off* every smaller one — at
    /// the standard 3072x1536 province raster it is past the eastern edge by more than half the
    /// map's width. It is set to the centre here rather than scaled from vanilla's, whose 0.54
    /// along x is Europe rather than anything a generated map has.
    /// </summary>
    private static void WriteCameraDefines(string modDir, Config.MapConfig cfg)
    {
        string dir = Path.Combine(modDir, "common", "defines", "graphic");
        Directory.CreateDirectory(dir);

        double panWidth = Math.Round(cfg.Scaled(VanillaPanningWidth));
        double panHeight = Math.Round(VanillaPanningHeight * cfg.ProvinceHeight / VanillaProvinceHeight);

        double lookX = cfg.ProvinceWidth / 2.0;
        double lookZ = cfg.ProvinceHeight / 2.0;

        int startStep = NearestZoomStep(VanillaStartZoomHeight * ViewScale(cfg));
        int flatStep = ScaleZoomStep(VanillaFlatMapZoomStep, cfg);

        ParadoxText.WriteBom(Path.Combine(dir, "zz_generated_graphics.txt"),
            $$"""
              # Camera extents must match map_data/provinces.png, not vanilla's map.
              NCamera = {
              	PANNING_WIDTH = {{panWidth.ToString(Invariant)}}
              	PANNING_HEIGHT = {{panHeight.ToString(Invariant)}}
              	START_LOOK_AT = { {{lookX.ToString("F1", Invariant)}} 0 {{lookZ.ToString("F1", Invariant)}} }
              	START_ZOOM_STEP = {{startStep}}
              }

              """);

        Console.WriteLine($"  camera: panning {panWidth} x {panHeight}, look at " +
                          $"{lookX:F0},{lookZ:F0}, zoom step {startStep} ({ZoomSteps[startStep]}), " +
                          $"flat map at step {flatStep} ({ZoomSteps[flatStep]}) " +
                          $"(vanilla 9090 x 4696, 5000,2300, 33, 21)");
    }

    /// <summary>
    /// A zoom-ladder index authored against vanilla's map, moved onto this one — step to camera
    /// height, height scaled, back to the nearest step.
    ///
    /// Indices outside the ladder come back untouched. That is not defensiveness: vanilla's map
    /// table layers use <c>fade_out=80</c> against a 35-step ladder, which is how the format spells
    /// "never", and scaling it would land it on a real step and start fading the table out.
    /// </summary>
    internal static int ScaleZoomStep(int step, Config.MapConfig cfg)
        => step < 0 || step >= ZoomSteps.Length ? step : NearestZoomStep(ZoomSteps[step] * ViewScale(cfg));

    /// <summary>
    /// The ratio a camera *height* scales by: the larger of the two axis ratios.
    ///
    /// Camera height buys a footprint of ground with the screen's aspect, so "the whole map is in
    /// view" is governed by whichever axis runs out last. On vanilla's 2:1 map that is the width,
    /// which is why the width ratio alone was enough for a long time. On a square 5000x5000 map the
    /// height ratio is twice the width ratio, and scaling by width alone opens the camera too low
    /// and drops FLAT_MAP_ZOOM_STEP — and with it the map table's fade — below the height where the
    /// map actually fits.
    ///
    /// Same rule as the map table's mesh, and for the same reason: cover the demanding axis and let
    /// the other one have slack.
    /// </summary>
    private static double ViewScale(Config.MapConfig cfg)
        => Math.Max(cfg.MapScale, (double)cfg.ProvinceHeight / VanillaProvinceHeight);

    /// <summary>
    /// The ladder step closest to <paramref name="height"/>. Camera height buys a fixed amount of
    /// ground at a fixed field of view, so opening on the same *share* of the map as vanilla means
    /// scaling its start height by the map scale and then landing on a real step.
    /// </summary>
    private static int NearestZoomStep(double height)
    {
        int best = 0;
        for (int i = 1; i < ZoomSteps.Length; i++)
            if (Math.Abs(ZoomSteps[i] - height) < Math.Abs(ZoomSteps[best] - height))
                best = i;
        return best;
    }

    /// <summary>
    /// Re-declares every vanilla empire, kingdom, duchy and holy-order title as a landless
    /// titular, so base-game and DLC script that hardcodes those keys still resolves.
    ///
    /// A missing title key is not a warning. It produces `title_links.cpp:214 Failed to fetch a
    /// valid landed title` once per reference (~12,900 of them) and, more dangerously,
    /// `coat_of_arms_dynamic_definitions.cpp:44 Could not find title 'k_england'` — the coat of
    /// arms system then holds a null title while it builds arms for the world.
    ///
    /// Only e_/k_/d_/h_ are emitted. Counties and baronies **cannot** be titular: they must own
    /// land, so the only way to satisfy a hardcoded c_/b_ reference is to name a real generated
    /// title after it, which is a separate piece of work.
    ///
    /// Every landless title needs `capital`, or CK3 logs "has no capital defined. Needed to
    /// ensure proper on-map location".
    /// </summary>
    public static void WriteVanillaTitulars(string modDir, string gameDir, List<Title> empires)
    {
        string source = Path.Combine(gameDir, "common", "landed_titles");
        if (!Directory.Exists(source)) return;

        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (counties.Count == 0) return;

        var generated = Titles.Flatten(empires).Select(t => t.Key).ToHashSet(StringComparer.Ordinal);

        // Paradox identifiers are not [a-z_0-9]: title keys carry hyphens and uppercase
        // (e_caspian-pontic_steppe, c_SUM_bangka-belitung, b_al-fayyum). A stricter pattern
        // silently drops keys, and every dropped key stays dangling.
        var keyPattern = new Regex(@"^\s*([ekdh]_[A-Za-z_0-9&-]+)\s*=\s*\{", RegexOptions.Multiline);

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in Directory.GetFiles(source, "*.txt"))
            foreach (Match m in keyPattern.Matches(File.ReadAllText(path)))
            {
                string key = m.Groups[1].Value;
                if (generated.Contains(key) || !seen.Add(key)) continue;
                keys.Add(key);
            }

        if (keys.Count == 0) return;

        var sb = new StringBuilder();
        sb.Append("# Vanilla e_/k_/d_/h_ keys re-declared as landless titulars.\n");
        sb.Append("# Base-game and DLC content hardcodes these; a missing key is a hard error,\n");
        sb.Append("# and the coat of arms system dereferences the null it gets back.\n\n");

        for (int i = 0; i < keys.Count; i++)
        {
            var (r, g, b) = MapDataWriter.ProvinceColor(i + 1);
            sb.Append($"{keys[i]} = {{\n");
            sb.Append("\tlandless = yes\n");
            sb.Append($"\tcapital = {counties[i % counties.Count].Key}\n");
            sb.Append($"\tcolor = {{ {r} {g} {b} }}\n");
            sb.Append("}\n");
        }

        string dir = Path.Combine(modDir, "common", "landed_titles");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_vanilla_titulars.txt"), sb.ToString());

        Console.WriteLine($"  titulars: {keys.Count} vanilla e_/k_/d_/h_ keys re-declared as landless");
    }

    /// <summary>
    /// Rebinds vanilla's 322 holy sites onto generated counties.
    ///
    /// Every faith names its holy sites, so a holy site whose county does not exist leaves the
    /// faith holding an object with no county — "No county found for holy site 'jerusalem'",
    /// once per site. Blanking the file is not an option either: faiths would then reference
    /// holy sites that do not exist at all, and the character modifiers declared here are
    /// referenced by name elsewhere.
    ///
    /// The rewrite is deliberately line-based so every modifier, parameter and flag survives
    /// untouched — only the `county` target changes, and `barony` lines are dropped because our
    /// barony keys never match vanilla's.
    /// </summary>
    public static void WriteHolySites(string modDir, string gameDir, List<Title> empires)
    {
        string source = Path.Combine(gameDir, "common", "religion", "holy_site_types");
        string destination = Path.Combine(modDir, "common", "religion", "holy_site_types");
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);

        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (counties.Count == 0) return;

        int rebound = 0, sites = 0;

        foreach (string path in Directory.GetFiles(source, "*.txt"))
        {
            var output = new StringBuilder();

            foreach (string line in File.ReadAllLines(path))
            {
                string code = line;
                int hash = code.IndexOf('#');
                if (hash >= 0) code = code[..hash];

                // Drop barony targets outright; ours never share vanilla's keys.
                if (Regex.IsMatch(code, @"^\s*barony\s*=")) continue;

                var match = Regex.Match(code, @"^(\s*)county\s*=\s*[A-Za-z_0-9&-]+");
                if (match.Success)
                {
                    output.Append($"{match.Groups[1].Value}county = {counties[rebound++ % counties.Count].Key}\n");
                    continue;
                }

                if (Regex.IsMatch(code, @"^[A-Za-z_0-9&-]+\s*=\s*\{")) sites++;
                output.Append(line).Append('\n');
            }

            ParadoxText.WriteBom(Path.Combine(destination, Path.GetFileName(path)), output.ToString());
        }

        Console.WriteLine($"  holy sites: {sites} re-declared, {rebound} rebound onto generated counties");
    }

    /// <summary>
    /// Re-declares every vanilla geographical region against generated titles.
    ///
    /// Blanking these files does not work: CK3 then reports "no visual geographical region" once
    /// per province (observed as exactly one error per land province) and every script_value
    /// that scopes into a region fails with "Invalid geographical region". An *empty* region
    /// block is no better — it parses but never registers in CGeographicalRegionDatabase, which
    /// breaks the geographical_region trigger and the region-derived modifiers, surfacing as a
    /// baffling "Unexpected token" error in an unrelated file. Every region needs a member.
    /// </summary>
    public static void WriteGeographicalRegions(string modDir, string gameDir, List<Title> empires)
    {
        string source = Path.Combine(gameDir, "map_data", "geographical_regions");
        string destination = Path.Combine(modDir, "map_data", "geographical_regions");
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);

        var all = Titles.Flatten(empires).ToList();
        var counties = all.Where(t => t.Tier == "c").ToList();
        var provinceIds = all.Where(t => t.Tier == "b" && t.ProvinceId > 0)
                             .Select(t => t.ProvinceId).ToList();
        if (counties.Count == 0 || provinceIds.Count == 0) return;

        // Pass 1: read every region key and the properties that must survive re-declaration.
        var files = new Dictionary<string, List<Region>>();
        var graphical = new List<Region>();

        foreach (string path in Directory.GetFiles(source, "*.txt"))
        {
            var regions = ScanRegions(File.ReadAllText(path));
            files[Path.GetFileName(path)] = regions;
            // Detect by the flag, not the key name: `graphical = yes` is what makes a region
            // visual, and it is the property CK3 actually looks for.
            graphical.AddRange(regions.Where(r => r.Graphical));
        }

        // Every province must belong to exactly one graphical region or CK3 complains about it
        // individually, so split them evenly across the graphical keys.
        var graphicalProvinces = new Dictionary<string, List<int>>();
        if (graphical.Count > 0)
        {
            foreach (var region in graphical) graphicalProvinces[region.Key] = [];
            for (int i = 0; i < provinceIds.Count; i++)
                graphicalProvinces[graphical[i % graphical.Count].Key].Add(provinceIds[i]);
        }

        int written = 0;
        foreach (var (fileName, regions) in files)
        {
            var sb = new StringBuilder();
            sb.Append("# Vanilla region keys re-declared against generated titles.\n");
            sb.Append("# Keys are preserved because base-game and DLC script hardcodes them.\n\n");

            int counter = 0;
            foreach (var region in regions)
            {
                string key = region.Key;
                sb.Append($"{key} = {{\n");
                if (region.GenerateModifiers) sb.Append("\tgenerate_modifiers = yes\n");

                // Without these two a graphical region is not a visual region, and every land
                // province ends up unassigned.
                if (region.Graphical) sb.Append("\tgraphical = yes\n");
                if (region.Color is not null) sb.Append($"\tcolor = {{ {region.Color} }}\n");

                if (graphicalProvinces.TryGetValue(key, out var provinces))
                {
                    sb.Append("\tprovinces = {");
                    for (int i = 0; i < provinces.Count; i++)
                    {
                        if (i % 20 == 0) sb.Append("\n\t\t");
                        sb.Append(provinces[i]).Append(' ');
                    }
                    sb.Append("\n\t}\n");
                }
                else
                {
                    // One real member is the minimum for the region to register at all.
                    sb.Append($"\tcounties = {{ {counties[counter++ % counties.Count].Key} }}\n");
                }

                sb.Append("}\n\n");
                written++;
            }

            ParadoxText.WriteBom(Path.Combine(destination, fileName), sb.ToString());
        }

        Console.WriteLine($"  re-declared {written} geographical regions " +
                          $"({graphical.Count} graphical covering {provinceIds.Count} provinces)");
    }

    /// <summary>
    /// Finds top-level `key = {` blocks and reports whether each declares generate_modifiers.
    ///
    /// That flag must be preserved exactly: it is what creates the
    /// &lt;region&gt;_development_growth[_factor] modifiers that
    /// common/modifier_definition_formats/00_region_definitions.txt declares. Dropping it makes
    /// those modifier types unknown, which then breaks 00_traits.txt, common/modifiers/* and
    /// holy_site_types with errors pointing at completely unrelated files.
    ///
    /// Paradox identifiers are not [a-z_0-9] — region keys contain ampersands
    /// (ghw_region_finland_&amp;_estonia), so a stricter pattern silently drops keys and every
    /// dropped key becomes a dangling reference.
    /// </summary>
    private static List<Region> ScanRegions(string text)
    {
        var result = new List<Region>();
        var lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            // Top-level blocks start at column 0.
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '#') continue;

            int equals = line.IndexOf('=');
            if (equals <= 0 || !line.Contains('{')) continue;

            string key = line[..equals].Trim();
            if (key.Length == 0 || !key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '&')) continue;

            // Walk the block to its closing brace, noting the flags we must preserve.
            bool generateModifiers = false;
            bool graphical = false;
            string? color = null;
            int depth = 0;
            for (int j = i; j < lines.Length; j++)
            {
                string body = lines[j];
                int hash = body.IndexOf('#');
                if (hash >= 0) body = body[..hash];

                if (body.Contains("generate_modifiers")) generateModifiers = true;
                if (body.Contains("graphical") && body.Contains("yes")) graphical = true;

                int colorAt = body.IndexOf("color", StringComparison.Ordinal);
                if (colorAt >= 0)
                {
                    int open = body.IndexOf('{', colorAt);
                    int close = open >= 0 ? body.IndexOf('}', open) : -1;
                    if (close > open) color = body[(open + 1)..close].Trim();
                }

                depth += body.Count(c => c == '{') - body.Count(c => c == '}');
                if (depth <= 0) { i = j; break; }
            }

            result.Add(new Region(key, generateModifiers, graphical, color));
        }

        return result;
    }

    /// <summary>
    /// A vanilla region key and the properties that must survive re-declaration.
    ///
    /// <paramref name="Graphical"/> is the one that bites: a region is only a *visual* region if
    /// it carries `graphical = yes`. Re-declaring the seven graphical_* keys with province lists
    /// but without the flag leaves CK3 with no visual regions at all, and it then logs
    /// "Province N has no visual geographical region assigned" once for every land province.
    /// </summary>
    private readonly record struct Region(
        string Key, bool GenerateModifiers, bool Graphical, string? Color);
}
