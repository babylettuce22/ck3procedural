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
    public static void WriteDefines(string modDir, Config.MapConfig cfg)
    {
        string dir = Path.Combine(modDir, "common", "defines");
        Directory.CreateDirectory(dir);

        // Sorts last on purpose. Defines are merged across every file in the directory and the
        // last one loaded wins, so a baseline file like ck2rpg's 01_gen_defines.txt would
        // otherwise silently override our world size with the template map's.
        ParadoxText.WriteBom(Path.Combine(dir, "zz_generated_defines.txt"),
            $$"""
              # World size must match map_data/provinces.png, not vanilla's map.
              NJominiMap = {
              	WORLD_EXTENTS_X = {{cfg.ProvinceWidth - 1}}
              	WORLD_EXTENTS_Y = 50
              	WORLD_EXTENTS_Z = {{cfg.ProvinceHeight - 1}}
              	WATERLEVEL = 3
              }

              # Camera limits are map-sized too, and live in a different namespace and a
              # different file (common/defines/graphic/00_graphics.txt). Vanilla ships 9090 x 4696
              # for its 9216x4608 map; leaving those in place lets the frontend camera address a
              # world several times larger than ours.
              NCamera = {
              	PANNING_WIDTH = {{cfg.ProvinceWidth}}
              	PANNING_HEIGHT = {{cfg.ProvinceHeight}}
              }

              """);

        Console.WriteLine($"  defines: WORLD_EXTENTS {cfg.ProvinceWidth - 1} x {cfg.ProvinceHeight - 1} " +
                          $"(vanilla ships 9215 x 4607)");
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
