using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The two vanilla points of interest that are lists of Earth province ids, fed from the map.
///
/// ---- What was wrong with vanilla's ----
///
/// poi_grand_city (Baghdad, Rome, Constantinople) and poi_battles_historical (48 dated battles,
/// Yarmuk to Hastings) name literal province ids, and the engine rebuilds both lists for every
/// travel plan. On a generated map each id is whatever county happens to sit there or nothing at
/// all, and every miss was an "add_to_list ... Scoped object is not valid" line in error.log —
/// 4,900 in five minutes of play. A static override emptied both; this writer is its follow-up.
///
/// ---- What this writes ----
///
/// Grand cities: the most developed settled county of each kingdom, best first, a handful of them
/// the way vanilla has three. One per kingdom so they spread over the map instead of piling up
/// round the richest capital.
///
/// Historical battlefields: the chronicle's wars from before the start — what an applied history
/// remembers of wars won and lost, and the live wars' opening years. Each is gated on
/// game_start_date the way vanilla's are, so an earlier bookmark does not show a battle that has
/// not been fought yet. The name and description are province custom loc in vanilla
/// (BattlePoiNameHistorical, BattlePoIDescriptionHistorical), which is also a list of Earth ids —
/// left alone it would call a generated county "The Battle of Hastings" — so both are replaced.
///
/// ---- Traps ----
///
/// The grand-city visit fires on_visited_grand_city, whose three events each check for their own
/// Earth province (location = province:4828, Baghdad). Those three ids are never picked, or a
/// generated city would get Baghdad's sight-seeing event, and the "Will trigger a Sight-Seeing
/// Event" tooltip is dropped because nothing will.
///
/// ---- Everything else is vanilla's, read from the installed game ----
///
/// Both objects are whole-object overrides, so everything in them has to be restated. Up to
/// 2026-09-30 the on_visit blocks were 1.19 text pasted in here; 1.20 (Crozier) changed the grand
/// city's gold reward and rebuilt the battlefield visit around an 80/20 roll between martial
/// experience and a commander trait, and the pasted copies went on paying out 1.19. Now each object
/// is taken from the installed game and only three things are replaced: the province list, the
/// Earth sight-seeing tooltip, and the battlefield visit's trait switches, which name Earth
/// provinces (Hastings gives aggressive_attacker). A generated battlefield teaches the commander
/// trait of the ground it was fought on (<see cref="TraitFor"/>), the way Red Cliffs teaches
/// forder. If vanilla stops declaring an object, it is reported and not written.
/// </summary>
public static class PoiWriter
{
    private const string PoiFile = "zz_gen_poi_overrides.txt";

    /// <summary>The provinces vanilla's grand-city events test with <c>location = province:N</c>.</summary>
    private static readonly HashSet<int> CityEventProvinces = [4828, 2575, 496];

    private const int MinCities = 3, MaxCities = 8, MaxBattles = 24;

    /// <summary>
    /// Writes both types and the battlefield names. <paramref name="chronicle"/> is null with
    /// --no-history, which leaves no battlefields and still writes the cities.
    /// <paramref name="provinceTerrain"/> picks each battlefield's trait; without it every
    /// battlefield teaches open_terrain_expert.
    /// </summary>
    public static (int Cities, int Battles) WriteAll(string modDir, string gameDir, List<Title> empires,
        Dictionary<Title, int> development, WildernessMap wilderness, ChronicleMap? chronicle, int startYear,
        TerrainClass[]? provinceTerrain = null)
    {
        var cities = GrandCities(empires, development, wilderness);
        var battles = Battlefields(chronicle, wilderness, startYear, provinceTerrain);

        WritePoiTypes(modDir, gameDir, cities, battles);
        WriteBattleLoc(modDir, battles);

        Console.WriteLine($"  points of interest: {cities.Count} grand cities, {battles.Count} historical battlefields");
        return (cities.Count, battles.Count);
    }

    private sealed record Battlefield(Title County, int Year, string Text, string Trait);

    /// <summary>
    /// The commander trait a battlefield teaches, from the ground it was fought on: the terrain
    /// trait that ground asks for, as vanilla's own list pairs Red Cliffs with forder and Talas with
    /// desert_warrior. Every key is a vanilla 1.20 commander trait.
    /// </summary>
    private static string TraitFor(TerrainClass terrain) => terrain switch
    {
        TerrainClass.Mountains or TerrainClass.Hills or TerrainClass.DesertMountains => "rough_terrain_expert",
        TerrainClass.Desert or TerrainClass.Drylands or TerrainClass.Oasis => "desert_warrior",
        TerrainClass.Forest or TerrainClass.Taiga => "forest_fighter",
        TerrainClass.Jungle => "jungle_stalker",
        TerrainClass.Wetlands or TerrainClass.Floodplains => "forder",
        TerrainClass.Arctic => "winter_soldier",
        _ => "open_terrain_expert",
    };

    private static List<Title> GrandCities(List<Title> empires, Dictionary<Title, int> development,
        WildernessMap wilderness)
    {
        var best = development
            .Where(kv => kv.Key.Tier == "c" && !wilderness.Contains(kv.Key)
                         && kv.Key.Capital is { ProvinceId: > 0 } seat && !CityEventProvinces.Contains(seat.ProvinceId))
            .GroupBy(kv => kv.Key.Parent?.Parent ?? kv.Key)
            .Select(g => g.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Index).First())
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Index)
            .ToList();

        int kingdoms = Titles.Flatten(Titles.Roots(empires)).Count(t => t.Tier == "k");
        int count = Math.Clamp(kingdoms / 4, MinCities, MaxCities);
        return best.Take(count).Select(kv => kv.Key).ToList();
    }

    private static List<Battlefield> Battlefields(ChronicleMap? chronicle, WildernessMap wilderness, int startYear,
        TerrainClass[]? provinceTerrain)
    {
        if (chronicle is null) return [];

        // Tension 2 and up: a war won, or held against a foreign people. A same-people border
        // squabble that was held is not the stuff battlefields are remembered for.
        return chronicle.All
            .Where(e => e.Kind == ChronicleKind.War && e.Year < startYear && e.Tension >= 2)
            .Select(e => (Event: e, County: CountyOf(e.Subject)))
            .Where(x => x.County is { } c && !wilderness.Contains(c) && c.Capital is { ProvinceId: > 0 })
            .GroupBy(x => x.County!)
            .Select(g => g.OrderByDescending(x => x.Event.Tension).ThenByDescending(x => x.Event.Year).First())
            .OrderByDescending(x => x.Event.Tension).ThenByDescending(x => x.Event.Year).ThenBy(x => x.County!.Index)
            .Take(MaxBattles)
            .OrderBy(x => x.Event.Year).ThenBy(x => x.County!.Index)
            .Select(x => new Battlefield(x.County!, Math.Max(1, x.Event.Year), x.Event.Text,
                TraitFor(provinceTerrain is null ? TerrainClass.Plains : Development.DominantTerrain(x.County!, provinceTerrain))))
            .ToList();
    }

    /// <summary>A duchy's war is fought at its capital county.</summary>
    private static Title? CountyOf(Title subject)
    {
        var t = subject;
        while (t is not null && t.Tier != "c") t = t.Capital;
        return t;
    }

    private static int ProvinceOf(Title county) => county.Capital!.ProvinceId;

    private static void WritePoiTypes(string modDir, string gameDir, List<Title> cities, List<Battlefield> battles)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# Generated by PoiWriter. Whole-object overrides of two vanilla points of interest whose\n")
          .Append("# province lists are Earth ids. Everything but the lists and the Earth trait switches is\n")
          .Append("# vanilla's own, read from the installed game when the world was written.\n\n");

        var cityList = new System.Text.StringBuilder("build_province_list = {\n");
        foreach (var county in cities)
            cityList.Append($"\t\t# {county.Name}\n\t\tprovince:{ProvinceOf(county)} = {{ add_to_list = provinces }}\n");
        cityList.Append("\t}");

        var battleList = new System.Text.StringBuilder("build_province_list = {\n");
        foreach (var battle in battles)
            battleList.Append($"\t\tif = {{ # {battle.County.Name}, {battle.Year}\n")
                      .Append($"\t\t\tlimit = {{ game_start_date > {battle.Year}.12.31 }}\n")
                      .Append($"\t\t\tprovince:{ProvinceOf(battle.County)} = {{ add_to_list = provinces }}\n")
                      .Append("\t\t}\n");
        battleList.Append("\t}");

        int written = 0;

        if (VanillaObject(gameDir, "poi_grand_city") is { } city)
        {
            city = ReplaceBlock(city, "build_province_list", cityList.ToString());
            // The sight-seeing tooltip promises one of three events that each test an Earth province.
            city = RemoveEnclosingIf(city, "poi_grand_city_visit_event_tt");
            sb.Append(city).Append("\n\n");
            written++;
        }

        if (VanillaObject(gameDir, "poi_battles_historical") is { } field)
        {
            field = ReplaceBlock(field, "build_province_list", battleList.ToString());
            field = RewriteTraitSwitches(field, battles);
            sb.Append(field).Append('\n');
            written++;
        }

        string dir = Path.Combine(modDir, "common", "travel", "point_of_interest_types");
        string path = Path.Combine(dir, PoiFile);
        if (written == 0)
        {
            // Nothing to override: a stale file from an earlier write must not linger.
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(path, sb.ToString());
    }

    /// <summary>
    /// <paramref name="key"/>'s whole block as the installed game declares it, line endings
    /// normalised, or null (reported) when no vanilla file declares it any more.
    /// </summary>
    private static string? VanillaObject(string gameDir, string key)
    {
        string dir = Path.Combine(gameDir, "common", "travel", "point_of_interest_types");
        var header = new System.Text.RegularExpressions.Regex(
            $@"(?m)^{System.Text.RegularExpressions.Regex.Escape(key)}\s*=\s*\{{");

        if (Directory.Exists(dir))
            foreach (string file in Directory.GetFiles(dir, "*.txt").OrderBy(f => f, StringComparer.Ordinal))
            {
                string text = File.ReadAllText(file).Replace("\r\n", "\n");
                var m = header.Match(text);
                if (!m.Success) continue;

                int end = ScriptScan.BlockEnd(text, m.Index);
                if (end > 0) return text[m.Index..end];
            }

        Console.WriteLine($"  points of interest: WARNING vanilla no longer declares {key}; not overriding it");
        return null;
    }

    /// <summary>The first <c>name = { … }</c> in <paramref name="block"/> swapped for
    /// <paramref name="replacement"/>, or the block unchanged when it has none.</summary>
    private static string ReplaceBlock(string block, string name, string replacement)
    {
        var m = System.Text.RegularExpressions.Regex.Match(block,
            $@"\b{System.Text.RegularExpressions.Regex.Escape(name)}\s*=\s*\{{");
        if (!m.Success) return block;

        int end = ScriptScan.BlockEnd(block, m.Index);
        return end < 0 ? block : block[..m.Index] + replacement + block[end..];
    }

    /// <summary>
    /// <paramref name="block"/> without the innermost <c>if = { … }</c> that mentions
    /// <paramref name="needle"/>, whole lines included, or unchanged when nothing does.
    /// </summary>
    private static string RemoveEnclosingIf(string block, string needle)
    {
        int at = block.IndexOf(needle, StringComparison.Ordinal);
        for (int open = at < 0 ? -1 : block.LastIndexOf("if = {", at, StringComparison.Ordinal);
             open >= 0;
             open = open == 0 ? -1 : block.LastIndexOf("if = {", open - 1, StringComparison.Ordinal))
        {
            int end = ScriptScan.BlockEnd(block, open);
            if (end <= at) continue;

            int start = block.LastIndexOf('\n', open) + 1;
            int stop = block.IndexOf('\n', end - 1);
            return block[..start] + block[(stop < 0 ? block.Length : stop + 1)..];
        }
        return block;
    }

    /// <summary>
    /// Each <c>switch = { … }</c> in the battlefield visit whose arms are Earth provinces, with its
    /// arms rebuilt for the generated battlefields.
    ///
    /// The arm vanilla writes first is the template: its province id and the trait it names
    /// (<c>add_trait = X</c> in the tooltip switch, <c>TRAIT = X</c> in the effect one) are swapped
    /// for each battlefield's, so whatever shape vanilla gives an arm is kept. Vanilla's comments
    /// inside the switch name Earth battles and go with their arms.
    /// </summary>
    private static string RewriteTraitSwitches(string block, List<Battlefield> battles)
    {
        var arm = new System.Text.RegularExpressions.Regex(@"^(\s*)province:\d+\s*=\s*\{.*\}\s*$");
        var trait = new System.Text.RegularExpressions.Regex(@"\b(add_trait|TRAIT)(\s*=\s*)\w+");

        var output = new System.Text.StringBuilder();
        int from = 0;
        for (int at = block.IndexOf("switch = {", StringComparison.Ordinal); at >= 0;
             at = block.IndexOf("switch = {", from, StringComparison.Ordinal))
        {
            int end = ScriptScan.BlockEnd(block, at);
            if (end < 0) break;

            var lines = block[at..end].Split('\n');
            string? template = lines.FirstOrDefault(l => arm.IsMatch(l) && trait.IsMatch(l));

            output.Append(block[from..at]);
            if (template is null)
            {
                output.Append(block[at..end]);
            }
            else
            {
                string indent = arm.Match(template).Groups[1].Value;
                var kept = lines.Skip(1).Take(lines.Length - 2)
                                .Where(l => !arm.IsMatch(l) && !l.TrimStart().StartsWith('#'));
                output.Append(lines[0]).Append('\n');
                foreach (string line in kept) output.Append(line).Append('\n');
                foreach (var battle in battles)
                {
                    string line = System.Text.RegularExpressions.Regex.Replace(template, @"province:\d+",
                        $"province:{ProvinceOf(battle.County)}");
                    line = trait.Replace(line, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{battle.Trait}");
                    output.Append($"{indent}# {battle.County.Name}, {battle.Year}\n").Append(line.TrimEnd()).Append('\n');
                }
                output.Append(lines[^1]);
            }
            from = end;
        }
        output.Append(block[from..]);
        return output.ToString();
    }

    private static void WriteBattleLoc(string modDir, List<Battlefield> battles)
    {
        var b = new JominiBuilder();
        b.Comment("Generated by PoiWriter. Vanilla's two are Earth province ids; these name the");
        b.Comment("generated battlefields in poi_battles_historical instead.");
        b.Blank();

        foreach (var (key, suffix) in new[] { ("BattlePoiNameHistorical", "name"), ("BattlePoIDescriptionHistorical", "desc") })
        {
            using (b.Block(key))
            {
                b.Field("type", "province");
                foreach (var battle in battles)
                    using (b.Block("text"))
                    {
                        using (b.Block("trigger")) b.Field("this", $"province:{ProvinceOf(battle.County)}");
                        b.Field("localization_key", $"gen_poi_battle_{battle.County.Key}_{suffix}");
                    }
            }
            b.Blank();
        }

        string dir = Path.Combine(modDir, "common", "customizable_localization");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_battle_poi_custom_loc.txt"), b.ToString());

        // $c_key$ rather than the name, so a county renamed in the editor renames its battle.
        var loc = new LocFile();
        foreach (var battle in battles)
        {
            loc.AddBuilt($"gen_poi_battle_{battle.County.Key}_name", $"The Battle of ${battle.County.Key}$");
            loc.AddBuilt($"gen_poi_battle_{battle.County.Key}_desc", $"#weak {battle.Text}#!");
        }
        loc.Write(Path.Combine(modDir, "localization", "english", "gen_battle_poi_l_english.yml"));
    }
}
