using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ck3MapGen.Emit;

/// <summary>
/// Populates the world with rulers and gives it a start date.
/// </summary>
public static class HistoryWriter
{
    public const string StartDate = "867.1.1";
    private const string BirthDate = "830.1.1";

    public const string BookmarkCharacter = "bookmark_generated_ruler";
    public const string ChallengeCharacter = "challenge_character_generated";

    /// <summary>
    /// The name a ruler and their house carry, drawn from their own culture's stock.
    ///
    /// Both come out of the pools <see cref="CultureWriter"/> has already written into the culture's
    /// name list and localised, so a count is named the same way his culture names anyone else and
    /// no extra localisation is needed here. Seeded off the county index rather than off a shared
    /// stream so the same county yields the same ruler between runs.
    /// </summary>
    private static (string FirstName, string DynastyName) RulerNames(Title county, Culture culture)
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

    public static void WriteAll(string modDir, MapConfig cfg, List<Title> empires,
        Dictionary<Title, int> development, CultureMap cultures, FaithMap faiths)
    {
        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (counties.Count == 0) return;

        // Who wears which of the de jure titles, and who owes whom. Seeded off the config rather
        // than a live stream so the same seed always produces the same political map.
        var realms = Realms.Build(empires, development, cfg, new Rng(cfg.Seed ^ 0x2E17));

        // The bookmark and the challenge character go to the two greatest rulers rather than to
        // whichever counties happened to be first — a start screen offering a random count while
        // an emperor sits elsewhere on the map advertises the wrong game.
        var bookmarkCounty = realms.Greatest.Count > 0 ? realms.Greatest[0] : counties[0];
        var challengeCounty = realms.Greatest.Count > 1 ? realms.Greatest[1] : bookmarkCounty;

        WriteDynasties(modDir, counties, cultures);
        WriteCharacters(modDir, counties, cultures, faiths);
        WriteTitleHistory(modDir, empires, development, realms);
        WriteBookmark(modDir, cfg, bookmarkCounty, realms, cultures, faiths);
        WriteChallengeCharacter(modDir, challengeCounty, realms, cultures, faiths);
        WriteBookmarkLocalisation(modDir, bookmarkCounty, challengeCounty, cultures);

        Console.WriteLine($"  history: {counties.Count} rulers, {counties.Count} dynasties, " +
                          $"1 bookmark at {StartDate} on {Primary(bookmarkCounty, realms).Key}");
    }

    /// <summary>The highest title a county's ruler wears, which is the title they are known by.</summary>
    private static Title Primary(Title county, RealmMap realms)
    {
        var best = county;
        foreach (var (title, holder) in realms.HolderCounty)
            if (holder == county && Rank(title) > Rank(best)) best = title;

        return best;
    }

    private static int Rank(Title title) => title.Tier switch
    {
        "e" => 4, "k" => 3, "d" => 2, "c" => 1, _ => 0,
    };

    private static string CharacterId(Title county) => $"gen_char_{county.Index}";

    private static string DynastyId(Title county) => $"gen_dynasty_{county.Index}";

    /// <summary>
    /// One house per county, named from its culture's dynasty stock.
    ///
    /// The `name` is the localisation key `dynn_&lt;Name&gt;`, which <see cref="CultureWriter"/>
    /// already emits for every dynasty name in every culture's list — so unlike the previous
    /// scheme there is no per-dynasty localisation to write here, and two counties that draw the
    /// same house name correctly share one displayed name.
    /// </summary>
    private static void WriteDynasties(string modDir, List<Title> counties, CultureMap cultures)
    {
        string dir = Path.Combine(modDir, "common", "dynasties");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        foreach (var county in counties)
        {
            var culture = cultures.For(county);
            var (_, dynastyName) = RulerNames(county, culture);

            // NEW: Clean the dynasty name for the internal key, but leave the visual one alone.
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
        // Convert spaces and hyphens to underscores (e.g. Al-Fariq -> al_fariq)
        string cleaned = input.ToLowerInvariant().Replace(" ", "_").Replace("-", "_");

        // Flatten accents (e.g. ö -> o, á -> a) so keys remain standard a-z ASCII
        cleaned = RemoveDiacritics(cleaned);

        // Strip out anything else (like apostrophes)
        return Regex.Replace(cleaned, "[^a-z0-9_]", "");
    }
    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder(capacity: normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private static void WriteCharacters(string modDir, List<Title> counties, CultureMap cultures,
        FaithMap faiths)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        foreach (var county in counties)
        {
            var culture = cultures.For(county);
            var (firstName, _) = RulerNames(county, culture);

            sb.Append($"{CharacterId(county)} = {{\n");
            sb.Append($"\tname = \"{firstName}\"\n");
            sb.Append($"\tdynasty = {DynastyId(county)}\n");
            sb.Append($"\treligion = {faiths.For(county).Key}\n");
            sb.Append($"\tculture = {culture.Key}\n");
            sb.Append($"\t{BirthDate} = {{ birth = yes }}\n");
            sb.Append("\t900.1.1 = { death = yes }\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_characters.txt"), sb.ToString());
    }

    /// <summary>
    /// The two frontend characters keep fixed identifiers because
    /// <see cref="PortraitWriter"/> names their portrait files after them, so their displayed names
    /// have to be localised here rather than coming out of a culture's name list.
    /// </summary>
    private static void WriteBookmarkLocalisation(string modDir, Title bookmarkCounty,
        Title challengeCounty, CultureMap cultures)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var (bookmarkName, _) = RulerNames(bookmarkCounty, cultures.For(bookmarkCounty));
        var (challengeName, _) = RulerNames(challengeCounty, cultures.For(challengeCounty));

        ParadoxText.WriteBom(Path.Combine(dir, "gen_history_l_english.yml"),
            $"""
             l_english:
              {BookmarkCharacter}:0 "{bookmarkName}"
              {ChallengeCharacter}:0 "{challengeName}"

             """);
    }

    /// <summary>
    /// Who holds each title at the start date, and whose vassal they are.
    ///
    /// Only titles somebody actually wears appear here. A de jure duchy with no duke is simply
    /// absent, which is what leaves its counties standing as independent counts — writing it with
    /// no holder would be the same thing said at greater length.
    ///
    /// <c>liege</c> is what makes a vassal, not the de jure nesting: two characters can sit one
    /// inside the other's duchy all game and remain strangers until this line says otherwise.
    /// </summary>
    private static void WriteTitleHistory(string modDir, List<Title> empires,
        Dictionary<Title, int> development, RealmMap realms)
    {
        string dir = Path.Combine(modDir, "history", "titles");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();

        foreach (var title in Titles.Flatten(empires))
        {
            if (!realms.HolderCounty.TryGetValue(title, out var holder)) continue;

            // Development is a county property and belongs in the same dated block as the holder.
            // CK3 applies change_development_level as a delta from zero at that date, so this is
            // the level the county starts at.
            int level = title.Tier == "c" ? development.GetValueOrDefault(title) : 0;
            realms.Liege.TryGetValue(title, out var liege);

            sb.Append($"{title.Key} = {{\n");
            sb.Append($"\t{StartDate} = {{\n");
            sb.Append($"\t\tholder = {CharacterId(holder)}\n");
            if (liege is not null) sb.Append($"\t\tliege = {liege.Key}\n");
            if (level > 0) sb.Append($"\t\tchange_development_level = {level}\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_titles.txt"), sb.ToString());
    }

    private static void WriteChallengeCharacter(string modDir, Title county, RealmMap realms,
        CultureMap cultures, FaithMap faiths)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "challenge_characters");
        Directory.CreateDirectory(dir);

        string culture = cultures.For(county).Key;
        string faith = faiths.For(county).Key;
        var title = Primary(county, realms);

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
              		title = {{title.Key}}
              		government = feudal_government
              		culture = {{culture}}
              		religion = {{faith}}
              		difficulty = "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM"
              		history_id = {{CharacterId(county)}}
              	}
              }

              """);
    }

    private static void WriteBookmark(string modDir, MapConfig cfg, Title county, RealmMap realms,
        CultureMap cultures, FaithMap faiths)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "bookmarks");
        Directory.CreateDirectory(dir);

        string culture = cultures.For(county).Key;
        string faith = faiths.For(county).Key;
        var title = Primary(county, realms);
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
              		title = {{title.Key}}
              		government = feudal_government
              		culture = {{culture}}
              		religion = {{faith}}
              		difficulty = "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM"
              		history_id = {{CharacterId(county)}}
              		position = { {{x}} {{y}} }
              	}
              }

              """);
    }
}