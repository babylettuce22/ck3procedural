using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ck3MapGen.Emit;

public static class HistoryWriter
{
    public static void WriteAll(
        string modDir, MapConfig cfg, List<Title> empires,
        RealmMap realms, Dictionary<Title, int> development,
        CultureMap cultures, FaithMap faiths, GovernmentMap governments,
        WildernessMap wilderness, IReadOnlyDictionary<Title, string>? bookmarkDnaMap = null)
    {
        var all = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (all.Count == 0) return;

        var counties = all.Where(c => !wilderness.Contains(c)).ToList();
        var wild = all.Where(wilderness.Contains).ToList();

        if (counties.Count == 0) return;

        var dnaMap = bookmarkDnaMap ?? new Dictionary<Title, string>();

        WriteDynasties(modDir, counties, cultures);
        WriteCharacters(modDir, cfg, counties, cultures, faiths, realms, governments, dnaMap);
        WriteHeadOfFaithCharacters(modDir, cfg, faiths, cultures, counties);
        WriteWildernessHolder(modDir, cfg, wild);
        WriteTitleHistory(modDir, cfg, empires, development, realms, governments, faiths, wild);
    }

    public static (string FirstName, string DynastyName) RulerNames(Title county, Culture culture)
    {
        var rng = new Rng(county.Index ^ 0x5A17);

        string first = culture.MaleNames.Count > 0
            ? culture.MaleNames[rng.Int(0, culture.MaleNames.Count - 1)]
            : culture.Name;

        string dynasty = culture.DynastyNames.Count > 0
            ? culture.DynastyNames[rng.Int(0, culture.DynastyNames.Count - 1)]
            : culture.Name;

        return (first, dynasty);
    }

    public static Title Primary(Title county, RealmMap realms)
    {
        var best = county;
        foreach (var (title, holder) in realms.HolderCounty)
        {
            if (holder == county && Rank(title) > Rank(best)) best = title;
        }

        return best;
    }

    public static int Rank(Title title) => title.Tier switch
    {
        "e" => 4,
        "k" => 3,
        "d" => 2,
        "c" => 1,
        _ => 0,
    };

    public static string CharacterId(Title county) => $"gen_char_{county.Index}";

    public static string DynastyId(Title county) => $"gen_dynasty_{county.Index}";

    private static void WriteDynasties(string modDir, List<Title> counties, CultureMap cultures)
    {
        string dir = Path.Combine(modDir, "common", "dynasties");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        foreach (var county in counties)
        {
            var culture = cultures.For(county);
            var (_, dynastyName) = RulerNames(county, culture);

            string safeKey = CleanKey(dynastyName);

            sb.Append($"{DynastyId(county)} = {{\n");
            sb.Append($"\tname = \"dynn_{safeKey}\"\n");
            sb.Append($"\tculture = \"{culture.Key}\"\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_dynasties.txt"), sb.ToString());
    }

    private static string CleanKey(string input)
    {
        string cleaned = input.ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        cleaned = RemoveDiacritics(cleaned);
        return Regex.Replace(cleaned, "[^a-z0-9_]", "");
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void WriteCharacters(string modDir, MapConfig cfg, List<Title> counties,
        CultureMap cultures, FaithMap faiths, RealmMap realms, GovernmentMap governments,
        IReadOnlyDictionary<Title, string> bookmarkDnaMap)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        foreach (var county in counties)
        {
            var culture = cultures.For(county);
            var (firstName, _) = RulerNames(county, culture);
            var primaryTitle = Primary(county, realms);
            var rng = new Rng(county.Index ^ 0x3E2D);

            int gold = primaryTitle.Tier switch
            {
                "e" => rng.Int(400, 600),
                "k" => rng.Int(240, 360),
                "d" => rng.Int(120, 180),
                _ => rng.Int(60, 90)
            };
            int prestige = primaryTitle.Tier switch
            {
                "e" => rng.Int(300, 500),
                "k" => rng.Int(175, 325),
                "d" => rng.Int(80, 120),
                _ => rng.Int(35, 65)
            };
            int renown = primaryTitle.Tier switch
            {
                "e" => rng.Int(3500, 6500),
                "k" => rng.Int(1800, 3500),
                "d" => rng.Int(800, 1500),
                _ => rng.Int(150, 450)
            };

            sb.Append($"{CharacterId(county)} = {{\n");
            sb.Append($"\tname = \"{firstName}\"\n");

            // Direct in-game DNA reference
            if (bookmarkDnaMap.TryGetValue(county, out string? dnaKey))
            {
                sb.Append($"\tdna = {dnaKey}\n");
            }

            sb.Append($"\tdynasty = {DynastyId(county)}\n");
            sb.Append($"\treligion = {faiths.For(county).Key}\n");
            sb.Append($"\tculture = {culture.Key}\n");
            sb.Append($"\t{cfg.BirthDate} = {{ birth = yes }}\n");

            sb.Append($"\t{cfg.StartDate} = {{\n");
            sb.Append("\t\teffect = {\n");

            switch (governments.For(county))
            {
                case GovernmentMap.Tribal:
                    gold = (int)(gold * 0.45);
                    prestige = (int)(prestige * 1.6);
                    break;
                case GovernmentMap.Republic:
                    gold = (int)(gold * 1.8);
                    prestige = (int)(prestige * 0.7);
                    break;
            }

            sb.Append($"\t\t\tadd_gold = {gold}\n");
            sb.Append($"\t\t\tadd_prestige = {prestige}\n");

            if (renown > 0)
            {
                sb.Append($"\t\t\tdynasty = {{ add_dynasty_prestige = {renown} }}\n");
            }

            bool isIndependent = !realms.Liege.ContainsKey(primaryTitle);
            bool isHigherTier = primaryTitle.Tier is "d" or "k" or "e";

            if (isIndependent || isHigherTier)
            {
                sb.Append("\t\t\tadd_character_modifier = {\n");
                sb.Append("\t\t\t\tmodifier = gen_early_realm_stability\n");
                sb.Append("\t\t\t\tyears = 3\n");
                sb.Append("\t\t\t}\n");
            }

            sb.Append("\t\t}\n");
            sb.Append("\t}\n");

            sb.Append($"\t{cfg.DeathDate} = {{ death = yes }}\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_characters.txt"), sb.ToString());
    }

    private static void WriteHeadOfFaithCharacters(string modDir, MapConfig cfg,
        FaithMap faiths, CultureMap cultures, List<Title> counties)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        int hofIndex = 0;

        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null) continue;

            var sampleCounty = counties.FirstOrDefault(c => faiths.For(c) == faith) ?? counties[0];
            var culture = cultures.For(sampleCounty);
            var (firstName, _) = RulerNames(sampleCounty, culture);

            sb.Append($"gen_hof_{hofIndex++} = {{\n");
            sb.Append($"\tname = \"{firstName}\"\n");
            sb.Append($"\treligion = {faith.Key}\n");
            sb.Append($"\tculture = {culture.Key}\n");
            sb.Append($"\t{cfg.BirthDate} = {{ birth = yes }}\n");
            sb.Append($"\t{cfg.StartDate} = {{\n");
            sb.Append("\t\teffect = {\n");
            sb.Append("\t\t\tadd_gold = 150\n");
            sb.Append("\t\t\tadd_piety = 250\n");
            sb.Append("\t\t}\n");
            sb.Append("\t}\n");
            sb.Append($"\t{cfg.DeathDate} = {{ death = yes }}\n");
            sb.Append("}\n");
        }

        if (hofIndex > 0)
        {
            ParadoxText.WriteBom(Path.Combine(dir, "02_generated_head_of_faith.txt"), sb.ToString());
        }
    }

    private static void WriteWildernessHolder(string modDir, MapConfig cfg, List<Title> wild)
    {
        if (wild.Count == 0) return;

        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# The holder of every unsettled county. See MapGen/Wilderness.cs.\n\n");
        sb.Append($"{WildernessMap.HolderId} = {{\n");
        sb.Append("\tname = \"wilderness_holder_name\"\n");
        sb.Append($"\treligion = {MapGen.Faiths.UnsettledFaithKey}\n");
        sb.Append($"\tculture = {MapGen.Cultures.UnsettledKey}\n");
        sb.Append("\tdisallow_random_traits = yes\n");
        sb.Append("\tsexuality = asexual\n");

        sb.Append($"\t{Math.Max(1, cfg.StartYear - 1000)}.1.1 = {{\n");
        sb.Append("\t\tbirth = yes\n");
        sb.Append("\t\ttrait = wilderness\n");
        sb.Append("\t\ttrait = immortal\n");
        sb.Append("\t}\n");
        sb.Append("}\n");

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_wilderness.txt"), sb.ToString());
    }

    private static void WriteTitleHistory(string modDir, MapConfig cfg, List<Title> empires,
        Dictionary<Title, int> development, RealmMap realms, GovernmentMap governments,
        FaithMap faiths, List<Title> wild)
    {
        string dir = Path.Combine(modDir, "history", "titles");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();

        int reignStartYear = Math.Max(1, cfg.StartYear - 5);
        string titleGrantDate = $"{reignStartYear}.1.1";

        foreach (var title in Titles.Flatten(empires))
        {
            if (!realms.HolderCounty.TryGetValue(title, out var holder)) continue;

            int level = title.Tier == "c" ? development.GetValueOrDefault(title) : 0;
            realms.Liege.TryGetValue(title, out var liege);
            string government = governments.For(holder);

            sb.Append($"{title.Key} = {{\n");
            sb.Append($"\t{titleGrantDate} = {{\n");
            sb.Append($"\t\tholder = {CharacterId(holder)}\n");

            if (government != GovernmentMap.Feudal)
            {
                sb.Append($"\t\tgovernment = {government}\n");
            }

            if (liege is not null)
            {
                sb.Append($"\t\tliege = {liege.Key}\n");
            }

            if (level > 0)
            {
                sb.Append($"\t\tchange_development_level = {level}\n");
            }

            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        if (wild.Count > 0)
        {
            sb.Append($"{WildernessMap.TitleKey} = {{\n");
            sb.Append($"\t{cfg.StartDate} = {{\n");
            sb.Append($"\t\tholder = {WildernessMap.HolderId}\n");
            sb.Append("\t\tgovernment = wilderness_government\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        foreach (var county in wild)
        {
            sb.Append($"{county.Key} = {{\n");
            sb.Append($"\t{cfg.StartDate} = {{\n");
            sb.Append($"\t\tholder = {WildernessMap.HolderId}\n");
            sb.Append("\t\tgovernment = wilderness_government\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        int hofIndex = 0;
        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null) continue;

            sb.Append($"{faith.Head.TitleKey} = {{\n");
            sb.Append($"\t{titleGrantDate} = {{\n");
            sb.Append($"\t\tholder = gen_hof_{hofIndex++}\n");
            sb.Append("\t\tgovernment = theocracy_government\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_titles.txt"), sb.ToString());
    }
}