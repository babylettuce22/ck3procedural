using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Populates the world with rulers and gives it a start date.
/// </summary>
public static class HistoryWriter
{
    public const string StartDate = "867.1.1";
    private const string BirthDate = "830.1.1";
    public const string Culture = "norwegian";
    public const string Faith = "norse_pagan";

    public const string BookmarkCharacter = "bookmark_generated_ruler";
    public const string ChallengeCharacter = "challenge_character_generated";

    // Authentic Norse/Norwegian male first names
    private static readonly string[] MaleFirstNames = [
        "Harald", "Bjorn", "Sigurd", "Ragnar", "Ivar", "Halfdan", "Erik", "Hastein",
        "Guthred", "Knut", "Olaf", "Hakon", "Magnus", "Torstein", "Vidar", "Leif",
        "Gunnar", "Arvid", "Egil", "Frode", "Gorm", "Halvar", "Orm", "Skarde",
        "Snorri", "Trygve", "Ulf", "Alfr", "Asgautr", "Asbjorn", "Asmundr", "Birger",
        "Eysteinn", "Gautr", "Grimr", "Hallvardr", "Helgi", "Ketill", "Runolf",
        "Sveinn", "Thorir", "Thorgils", "Valdemar", "Yngvar"
    ];

    // Historical Norse dynasty/clan bases
    private static readonly string[] DynastyBases = [
        "Yngling", "Knytling", "Munso", "Skjoldung", "Lodbrok", "Harfagre", "Crovan",
        "Giske", "Sudreim", "Bolt", "Ramsay", "Somerled", "Orkney", "Hlada", "Skuleson",
        "Ulfsson", "Sigurdsson", "Ivarsson", "Ragnarsson", "Eiriksson", "Olafsson"
    ];

    /// <summary>
    /// Generates highly thematic first and dynasty names deterministically for each county.
    /// </summary>
    private static (string FirstName, string DynastyName) GetRulerNames(Title county)
    {
        // Seeding with the county index ensures name generation is consistent between builds
        var rand = new Random(county.Index + 505);

        string firstName = MaleFirstNames[rand.Next(MaleFirstNames.Length)];
        string dynastyName;

        int style = rand.Next(3);
        if (style == 0)
        {
            // Style 1: Prestigious historical clan name
            dynastyName = DynastyBases[rand.Next(DynastyBases.Length)];
        }
        else if (style == 1)
        {
            // Style 2: Patronymic (e.g. "Sigurdsson")
            string fatherName = MaleFirstNames[rand.Next(MaleFirstNames.Length)];
            string suffix = fatherName.EndsWith("s") || fatherName.EndsWith("r") ? "son" : "sson";
            dynastyName = fatherName + suffix;
        }
        else
        {
            // Style 3: Territorial ("af [County Name]")
            // Fall back to a normal dynasty name if the county names are still system placeholders
            if (county.Name.StartsWith("Generated County"))
            {
                dynastyName = DynastyBases[rand.Next(DynastyBases.Length)];
            }
            else
            {
                dynastyName = $"af {county.Name}";
            }
        }

        return (firstName, dynastyName);
    }

    public static void WriteAll(string modDir, MapConfig cfg, List<Title> empires,
        Dictionary<string, int> development)
    {
        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (counties.Count == 0) return;

        WriteDynasties(modDir, counties);
        WriteCharacters(modDir, counties);
        WriteTitleHistory(modDir, counties, development);
        WriteBookmark(modDir, cfg, counties);
        WriteChallengeCharacter(modDir, counties);

        Console.WriteLine($"  history: {counties.Count} rulers holding {counties.Count} counties, " +
                          $"{counties.Count} dynasties, 1 bookmark at {StartDate}");
    }

    private static string CharacterId(Title county) => $"gen_char_{county.Index}";

    private static string DynastyId(Title county) => $"gen_dynasty_{county.Index}";

    private static void WriteDynasties(string modDir, List<Title> counties)
    {
        string dir = Path.Combine(modDir, "common", "dynasties");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        foreach (var county in counties)
        {
            sb.Append($"{DynastyId(county)} = {{\n");
            sb.Append($"\tname = \"dynn_{DynastyId(county)}\"\n");
            sb.Append($"\tculture = \"{Culture}\"\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_dynasties.txt"), sb.ToString());

        string locDir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(locDir);

        var loc = new StringBuilder();
        loc.Append("l_english:\n");
        foreach (var county in counties)
        {
            var (_, dynastyName) = GetRulerNames(county);
            loc.Append($" dynn_{DynastyId(county)}: \"{dynastyName}\"\n");
        }

        ParadoxText.WriteBom(Path.Combine(locDir, "gen_dynasties_l_english.yml"), loc.ToString());
    }

    private static void WriteCharacters(string modDir, List<Title> counties)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        foreach (var county in counties)
        {
            sb.Append($"{CharacterId(county)} = {{\n");
            sb.Append($"\tname = \"{CharacterId(county)}\"\n");
            sb.Append($"\tdynasty = {DynastyId(county)}\n");
            sb.Append($"\treligion = {Faith}\n");
            sb.Append($"\tculture = {Culture}\n");
            sb.Append($"\t{BirthDate} = {{ birth = yes }}\n");
            sb.Append("\t900.1.1 = { death = yes }\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_characters.txt"), sb.ToString());

        string locDir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(locDir);

        var loc = new StringBuilder();
        loc.Append("l_english:\n");
        foreach (var county in counties)
        {
            var (firstName, _) = GetRulerNames(county);
            loc.Append($" {CharacterId(county)}: \"{firstName}\"\n");
        }

        ParadoxText.WriteBom(Path.Combine(locDir, "gen_characters_l_english.yml"), loc.ToString());
    }

    private static void WriteTitleHistory(string modDir, List<Title> counties,
        Dictionary<string, int> development)
    {
        string dir = Path.Combine(modDir, "history", "titles");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        foreach (var county in counties)
        {
            // Development is a county property and belongs in the same dated block as the holder.
            // CK3 applies change_development_level as a delta from zero at that date, so this is
            // the level the county starts at.
            int level = development.GetValueOrDefault(county.Key);

            sb.Append($"{county.Key} = {{\n");
            sb.Append($"\t{StartDate} = {{\n");
            sb.Append($"\t\tholder = {CharacterId(county)}\n");
            if (level > 0) sb.Append($"\t\tchange_development_level = {level}\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_titles.txt"), sb.ToString());
    }

    private static void WriteChallengeCharacter(string modDir, List<Title> counties)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "challenge_characters");
        Directory.CreateDirectory(dir);

        var county = counties[counties.Count > 1 ? 1 : 0];

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_challenge.txt"),
            $$"""
              {{ChallengeCharacter}} = {
              	start_date = {{StartDate}}

              	character = {
              		name = "{{ChallengeCharacter}}"
              		dynasty = {{DynastyId(county)}}
              		dynasty_splendor_level = 1
              		type = male
              		birth = {{BirthDate}}
              		title = {{county.Key}}
              		government = feudal_government
              		culture = {{Culture}}
              		religion = {{Faith}}
              		difficulty = "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM"
              		history_id = {{CharacterId(county)}}
              	}
              }

              """);
    }

    private static void WriteBookmark(string modDir, MapConfig cfg, List<Title> counties)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "bookmarks");
        Directory.CreateDirectory(dir);

        var county = counties[0];
        int x = cfg.ProvinceWidth / 2;
        int y = cfg.ProvinceHeight / 2;

        ParadoxText.WriteBom(Path.Combine(dir, "00_bookmarks.txt"),
            $$"""
              bm_generated = {
              	start_date = {{StartDate}}
              	is_playable = yes
              	group = bm_group_867

              	weight = {
              		value = 100
              	}

              	character = {
              		name = "{{BookmarkCharacter}}"
              		dynasty = {{DynastyId(county)}}
              		dynasty_splendor_level = 1
              		type = male
              		birth = {{BirthDate}}
              		title = {{county.Key}}
              		government = feudal_government
              		culture = {{Culture}}
              		religion = {{Faith}}
              		difficulty = "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM"
              		history_id = {{CharacterId(county)}}
              		position = { {{x}} {{y}} }
              	}
              }

              """);
    }
}