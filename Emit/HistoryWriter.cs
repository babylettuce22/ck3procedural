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
        Dictionary<Title, int> development, CultureMap cultures, FaithMap faiths,
        GovernmentMap governments)
    {
        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (counties.Count == 0) return;

        // Who wears which of the de jure titles, and who owes whom. Seeded off the config rather
        // than a live stream so the same seed always produces the same political map.
        var realms = Realms.Build(empires, development, cfg, new Rng(cfg.Seed ^ 0x2E17));

        // The bookmark and the challenge character go to the two greatest rulers rather than to
        // whichever counties happened to be first — a start screen offering a random count while
        // an emperor sits elsewhere on the map advertises the wrong game.
        //
        // Greatest-but-playable, though. A republic or a theocracy has no heir a player could
        // become and CK3 offers neither on the start screen; putting one behind the bookmark would
        // advertise a realm the game will not let anybody take. The unfiltered list is still the
        // fallback, because a world where every great ruler is a doge is a world that has to open
        // on *something*.
        var playable = realms.Greatest.Where(c => IsPlayable(governments.For(c))).ToList();
        if (playable.Count == 0) playable = realms.Greatest;

        var bookmarkCounty = playable.Count > 0 ? playable[0] : counties[0];
        var challengeCounty = playable.Count > 1 ? playable[1] : bookmarkCounty;

        WriteDynasties(modDir, counties, cultures);
        WriteCharacters(modDir, cfg, counties, cultures, faiths, realms, governments);
        WriteTitleHistory(modDir, cfg, empires, development, realms, governments, faiths);
        WriteBookmark(modDir, cfg, bookmarkCounty, realms, cultures, faiths, governments);
        WriteChallengeCharacter(modDir, challengeCounty, realms, cfg, cultures, faiths, governments);
        WriteBookmarkLocalisation(modDir, bookmarkCounty, challengeCounty, cultures);

        Console.WriteLine($"  history: {counties.Count} rulers, " +
                          $"{counties.Count} dynasties, " +
                          $"1 bookmark at {cfg.StartDate} on {Primary(bookmarkCounty, realms).Key}");
    }

    /// <summary>
    /// Whether a government is one a player can be handed at the start screen. The dynastic three
    /// are; a republic passes its seat by election among its patricians and a theocracy among its
    /// clergy, so neither has an heir the player continues as.
    /// </summary>
    private static bool IsPlayable(string government) => government
        is GovernmentMap.Feudal or GovernmentMap.Clan or GovernmentMap.Tribal;

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

    private static void WriteCharacters(string modDir, MapConfig cfg, List<Title> counties,
                CultureMap cultures, FaithMap faiths, RealmMap realms, GovernmentMap governments)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        foreach (var county in counties)
        {
            var culture = cultures.For(county);
            var (firstName, _) = RulerNames(county, culture);

            var rng = new Rng(county.Index ^ 0x3E2D);
            var primaryTitle = Primary(county, realms);

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

            sb.Append("\t\t}\n");
            sb.Append("\t}\n");

            sb.Append($"\t{cfg.DeathDate} = {{ death = yes }}\n");
            sb.Append("}\n");
        }

        int hofIndex = 0;
        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null) continue;

            bool isFemale = faith.Religion.Doctrines.GetValueOrDefault("doctrine_clerical_gender")
                == "doctrine_clerical_gender_female_only";
            var seatCulture = cultures.For(faith.Head.Seat);
            var rng = new Rng(faith.Head.TitleKey.GetHashCode() ^ 0x40F1);
            string firstName = isFemale
                ? (seatCulture.FemaleNames.Count > 0 ? rng.Pick(seatCulture.FemaleNames) : seatCulture.Name)
                : (seatCulture.MaleNames.Count > 0 ? rng.Pick(seatCulture.MaleNames) : seatCulture.Name);

            sb.Append($"gen_hof_{hofIndex++} = {{\n");
            sb.Append($"\tname = \"{firstName}\"\n");
            sb.Append($"\treligion = {faith.Key}\n");
            sb.Append($"\tculture = {seatCulture.Key}\n");
            if (isFemale) sb.Append("\tfemale = yes\n");
            sb.Append($"\t{cfg.BirthDate} = {{ birth = yes }}\n");
            sb.Append($"\t{cfg.DeathDate} = {{ death = yes }}\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_characters.txt"), sb.ToString());
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
    ///
    /// <c>government</c> belongs here too, on the title rather than on the character, which is where
    /// vanilla puts it and on every tier — <c>history/titles/e_china.txt</c> sets it on counties and
    /// duchies alike. Feudal is left unwritten, since <c>feudal_government</c> carries
    /// <c>fallback = 1</c> and an unstated government is already feudal.
    ///
    /// It is read off the *holder's own county*, not off the title: a duke's government is the one
    /// he holds in his capital, and writing the duchy without it would seat him in two governments
    /// at once.
    /// </summary>
    private static void WriteTitleHistory(string modDir, MapConfig cfg, List<Title> empires,
        Dictionary<Title, int> development, RealmMap realms, GovernmentMap governments,
        FaithMap faiths)
    {
        string dir = Path.Combine(modDir, "history", "titles");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();

        foreach (var title in Titles.Flatten(empires))
        {
            if (!realms.HolderCounty.TryGetValue(title, out var holder)) continue;

            int level = title.Tier == "c" ? development.GetValueOrDefault(title) : 0;
            realms.Liege.TryGetValue(title, out var liege);

            sb.Append($"{title.Key} = {{\n");
            sb.Append($"\t{cfg.StartDate} = {{\n");
            sb.Append($"\t\tholder = {CharacterId(holder)}\n");
            string government = governments.For(holder);
            if (government != GovernmentMap.Feudal) sb.Append($"\t\tgovernment = {government}\n");
            if (liege is not null) sb.Append($"\t\tliege = {liege.Key}\n");
            if (level > 0) sb.Append($"\t\tchange_development_level = {level}\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        int hofIndex = 0;
        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null) continue;

            sb.Append($"{faith.Head.TitleKey} = {{\n");
            sb.Append($"\t{cfg.StartDate} = {{\n");
            sb.Append($"\t\tholder = gen_hof_{hofIndex++}\n");
            sb.Append($"\t\tgovernment = theocracy_government\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_titles.txt"), sb.ToString());
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



    private static void WriteChallengeCharacter(string modDir, Title county, RealmMap realms, MapConfig cfg,
        CultureMap cultures, FaithMap faiths, GovernmentMap governments)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "challenge_characters");
        Directory.CreateDirectory(dir);

        string culture = cultures.For(county).Key;
        string faith = faiths.For(county).Key;
        string government = governments.For(county);
        var title = Primary(county, realms);

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_challenge.txt"),
            $$"""
              {{ChallengeCharacter}} = {
              	start_date = {{cfg.StartDate}}

              	character = {
              		name = "{{ChallengeCharacter}}"
              		dynasty = {{DynastyId(county)}}
              		dynasty_splendor_level = 1
              		type = male
              		birth = {{cfg.BirthDate}}
              		title = {{title.Key}}
              		government = {{government}}
              		culture = {{culture}}
              		religion = {{faith}}
              		difficulty = "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM"
              		history_id = {{CharacterId(county)}}
              	}
              }

              """);
    }

    /// <summary>
    /// Unlike title history, the two frontend characters cannot leave their government unwritten —
    /// the bookmark screen names one explicitly — so feudal is spelled out here too. Both read the
    /// same <see cref="GovernmentMap"/> the title history did, or the start screen promises a
    /// feudal realm the save then loads as a tribe.
    /// </summary>
    private static void WriteBookmark(string modDir, MapConfig cfg, Title county, RealmMap realms,
        CultureMap cultures, FaithMap faiths, GovernmentMap governments)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "bookmarks");
        Directory.CreateDirectory(dir);

        string culture = cultures.For(county).Key;
        string faith = faiths.For(county).Key;
        string government = governments.For(county);
        var title = Primary(county, realms);
        int x = cfg.ProvinceWidth / 2;
        int y = cfg.ProvinceHeight / 2;

        ParadoxText.WriteBom(Path.Combine(dir, "00_bookmarks.txt"),
            $$"""
              bm_generated = {
              	start_date = {{cfg.StartDate}}
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
              		birth = {{cfg.BirthDate}}
              		title = {{title.Key}}
              		government = {{government}}
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