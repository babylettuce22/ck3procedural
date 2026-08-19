using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ck3MapGen.Emit;

/// <summary>
/// Declares the generated cultures, the pillars they stand on, the names they use and the
/// technology they start with.
///
/// Everything here is **additive**. Vanilla's cultures are left declared and untouched, which is
/// not laziness — base-game and DLC script names them 1,890 times, and unlike a title a culture
/// carries no province id, so an unused culture template costs nothing but a dangling reference
/// costs a script error apiece. The generated cultures simply become the only ones anybody holds.
/// </summary>
public static class CultureWriter
{
    public static void WriteAll(string modDir, MapConfig cfg, CultureMap cultures,
        EthnicityMap ethnicityMap, VanillaVocabulary vocab, Rng rng)
    {
        WritePillars(modDir, cultures);
        WriteCultures(modDir, cultures, ethnicityMap);
        WriteNameLists(modDir, cultures);
        WriteHistory(modDir, cfg, cultures, vocab, rng);
        WriteLocalisation(modDir, cultures);

        Console.WriteLine($"  cultures written: {cultures.Cultures.Count} cultures, " +
                          $"{cultures.Heritages.Count} heritages and languages");
    }

    /// <summary>
    /// One heritage and one language per generated culture group.
    ///
    /// Both are near-empty declarations by design: a pillar's gameplay lives in the triggers and
    /// modifiers that reference it, and the two scripted triggers here are vanilla's own, so the
    /// generated pillars behave exactly as base-game ones do in the culture interface.
    /// </summary>
    private static void WritePillars(string modDir, CultureMap cultures)
    {
        string dir = Path.Combine(modDir, "common", "culture", "pillars");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Generated heritages and languages.\n\n");

        foreach (var heritage in cultures.Heritages)
        {
            sb.Append($"{heritage.Key} = {{\n");
            sb.Append("\ttype = heritage\n");
            sb.Append("\t\taudio_parameter = european\n");
            sb.Append("\tparameters = {\n");
            sb.Append("\t}\n");
            sb.Append("\tis_shown = {\n");
            sb.Append($"\t\theritage_is_shown_trigger = {{ HERITAGE = {heritage.Key} }}\n");
            sb.Append("\t}\n");
            sb.Append("}\n\n");

            var language = heritage.Language;
            sb.Append($"{language.Key} = {{\n");
            sb.Append("\ttype = language\n");
            sb.Append("\tis_shown = {\n");
            sb.Append($"\t\tlanguage_is_shown_trigger = {{ LANGUAGE = {language.Key} }}\n");
            sb.Append("\t}\n");
            sb.Append("\tai_will_do = {\n");
            sb.Append("\t\tvalue = 10\n");
            sb.Append("\t\tif = {\n");
            sb.Append($"\t\t\tlimit = {{ has_cultural_pillar = {language.Key} }}\n");
            sb.Append("\t\t\tmultiply = 10\n");
            sb.Append("\t\t}\n");
            sb.Append("\t}\n");
            if (heritage.LanguageColor is { } color && color != "tungusic")
            {
                sb.Append($"\tcolor = {color}\n");
            }
            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_pillars.txt"), sb.ToString());
    }

    /// <summary>
    /// Not private: a culture's name, colour, ethos and traditions all live in this file, so
    /// editing one after the mod is written re-runs exactly this. See <see cref="WorldOverwrite"/>.
    /// </summary>
    internal static void WriteCultures(string modDir, CultureMap cultures, EthnicityMap ethnicityMap)
    {
        string dir = Path.Combine(modDir, "common", "culture", "cultures");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Generated cultures. Vanilla's are left declared but unheld.\n\n");

        foreach (var culture in cultures.Cultures)
        {
            var look = culture.Heritage.Look;

            sb.Append($"{culture.Key} = {{\n");
            sb.Append($"\tcolor = {{ {culture.Color.R} {culture.Color.G} {culture.Color.B} }}\n\n");
            sb.Append($"\tethos = {culture.Ethos}\n");
            sb.Append($"\theritage = {culture.Heritage.Key}\n");
            sb.Append($"\tlanguage = {culture.Language.Key}\n");
            sb.Append($"\tmartial_custom = {culture.MartialCustom}\n");
            sb.Append($"\thead_determination = {culture.HeadDetermination}\n\n");

            sb.Append("\ttraditions = {\n");
            foreach (string tradition in culture.Traditions) sb.Append($"\t\t{tradition}\n");
            sb.Append("\t}\n\n");

            sb.Append($"\tname_list = {culture.NameListKey}\n\n");

            // Borrowed whole off one vanilla culture so the four sets and the ethnicities agree.
            sb.Append($"\tcoa_gfx = {look.CoaGfx}\n");
            sb.Append($"\tbuilding_gfx = {look.BuildingGfx}\n");
            sb.Append($"\tclothing_gfx = {look.ClothingGfx}\n");
            sb.Append($"\tunit_gfx = {look.UnitGfx}\n\n");

            // One generated ethnicity rather than the vanilla culture's whole weighted list. The
            // borrowed list describes the people it was lifted from; a generated culture has its own
            // look to declare, and pointing at a single definition is what lets it.
            sb.Append("\tethnicities = {\n");
            sb.Append($"\t\t100 = {ethnicityMap.For(culture).Key}\n");
            sb.Append("\t}\n");

            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_cultures.txt"), sb.ToString());
    }

    /// <summary>
    /// The name grammar for each culture: its stock of given names, its dynasty names, and the
    /// affixes that build a patronymic.
    ///
    /// Names are emitted as bare tokens, which are localisation keys — CK3 looks each one up and
    /// falls back to printing the key when it misses, so a missing entry shows as a raw string
    /// rather than as an error. <see cref="WriteLocalisation"/> is what stops that happening.
    /// </summary>
    private static void WriteNameLists(string modDir, CultureMap cultures)
    {
        string dir = Path.Combine(modDir, "common", "culture", "name_lists");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Generated name lists, one per culture.\n\n");

        foreach (var culture in cultures.Cultures)
        {
            sb.Append($"{culture.NameListKey} = {{\n");

            sb.Append("\tcadet_dynasty_names = {\n");
            foreach (string name in culture.DynastyNames.Take(12))
                sb.Append($"\t\t\"dynn_{CleanKey(name)}\"\n"); // NEW: Apply CleanKey
            sb.Append("\t}\n\n");

            Append("male_names", culture.MaleNames);
            Append("female_names", culture.FemaleNames);

            sb.Append("\tdynasty_names = {\n");
            foreach (string name in culture.DynastyNames)
                sb.Append($"\t\t\"dynn_{CleanKey(name)}\"\n"); // NEW: Apply CleanKey
            sb.Append("\t}\n\n");

            sb.Append($"\tdynasty_of_location_prefix = \"dynnp_{culture.Key}\"\n\n");
            sb.Append($"\tpatronym_suffix_male = \"dynnpat_suf_{culture.Key}_male\"\n");
            sb.Append($"\tpatronym_suffix_female = \"dynnpat_suf_{culture.Key}_female\"\n");
            if (culture.AlwaysUsePatronym) sb.Append("\talways_use_patronym = yes\n");
            sb.Append('\n');

            sb.Append("\tpat_grf_name_chance = 40\n");
            sb.Append("\tmat_grf_name_chance = 10\n");
            sb.Append("\tfather_name_chance = 5\n\n");
            sb.Append("\tpat_grm_name_chance = 10\n");
            sb.Append("\tmat_grm_name_chance = 40\n");
            sb.Append("\tmother_name_chance = 5\n\n");

            sb.Append("\tmercenary_names = {\n");
            sb.Append($"\t\t{{ name = \"mercenary_company_{culture.Key}\" }}\n");
            sb.Append("\t}\n");

            sb.Append("}\n\n");
            continue;

            void Append(string field, List<string> names)
            {
                sb.Append($"\t{field} = {{\n");
                for (int i = 0; i < names.Count; i += 8)
                {
                    // Clean the keys and prefix with "cul_" to prevent Murmur3A hash collisions
                    var cleanNames = names.Skip(i).Take(8).Select(n => $"cul_{CleanKey(n)}");
                    sb.Append("\t\t").Append(string.Join(' ', cleanNames)).Append('\n');
                }
                sb.Append("\t}\n\n");
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_name_lists.txt"), sb.ToString());
    }
    private static string CleanKey(string input)
    {
        string cleaned = input.ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        cleaned = RemoveDiacritics(cleaned);
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
    /// <summary>
    /// What each culture has already worked out by the start date.
    ///
    /// The file *name* identifies the culture — there is no key inside — which is why each gets its
    /// own. Skipping this entirely is not an option: a culture with no innovations cannot build
    /// most holdings or raise most men-at-arms, so the world would start poorer than any vanilla
    /// bookmark and stay that way.
    ///
    /// Each innovation is rolled at the frequency vanilla cultures actually hold it, so a generated
    /// culture ends up with a plausible number of them and the common ones stay common — without
    /// every culture starting from an identical technological position.
    /// </summary>
    // In CultureWriter.cs

    // In CultureWriter.cs

    // In CultureWriter.cs

    // In CultureWriter.cs

    private static void WriteHistory(string modDir, MapConfig cfg, CultureMap cultures, VanillaVocabulary vocab, Rng rng)
    {
        if (vocab.InnovationDefs.Count == 0) return;

        string dir = Path.Combine(modDir, "history", "cultures");
        Directory.CreateDirectory(dir);

        foreach (var file in Directory.GetFiles(dir, "gen_culture_*.txt"))
        {
            File.Delete(file);
        }

        int startYear = ParseStartYear(cfg.StartDate);
        var (frequencies, _) = vocab.GetFrequenciesAtYear(startYear);

        var eraMilestones = new (string EraKey, int StartYear, int EndYear, string DateStr)[]
        {
        ("culture_era_tribal", 0, 900, "1.1.1"),
        ("culture_era_early_medieval", 900, 1050, "900.1.1"),
        ("culture_era_high_medieval", 1050, 1200, "1050.1.1"),
        ("culture_era_late_medieval", 1200, 1453, "1200.1.1")
        };

        int currentEraIndex = startYear switch
        {
            < 900 => 0,
            < 1050 => 1,
            < 1200 => 2,
            _ => 3
        };

        int totalAssigned = 0;

        foreach (var culture in cultures.Cultures)
        {
            var chosenByEra = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i <= currentEraIndex; i++)
            {
                chosenByEra[eraMilestones[i].EraKey] = [];
            }

            // Normalized development factor: 0.0 (backwater/wilderness) to 1.0 (metropolis)
            double devNormalized = Math.Clamp(culture.MeanDevelopment / 30.0, 0.0, 1.0);

            // 1. Process Past Completed Eras
            for (int e = 0; e < currentEraIndex; e++)
            {
                string pastEra = eraMilestones[e].EraKey;
                var pastEraPool = vocab.InnovationDefs.Values
                    .Where(def => def.Era == pastEra)
                    .Select(def => def.Key)
                    .ToList();

                if (pastEraPool.Count == 0) continue;

                // CK3 requires at least 8 innovations (or 50%) of the era to qualify for the next era
                int minRequired = Math.Min(pastEraPool.Count, Math.Max(8, (int)Math.Ceiling(pastEraPool.Count * 0.5)));

                // Completion share: ~55%-65% for poor ground, up to ~85%-95% for wealthy ground
                double completionRate = 0.55 + 0.35 * devNormalized + (rng.NextDouble() * 0.1 - 0.05);
                int targetPastCount = (int)Math.Round(pastEraPool.Count * Math.Clamp(completionRate, 0.50, 0.95));
                targetPastCount = Math.Clamp(targetPastCount, minRequired, pastEraPool.Count);

                SampleWeightedInnovations(chosenByEra[pastEra], pastEraPool, targetPastCount, culture, vocab, frequencies, rng);
            }

            // 2. Process Current Active Era
            var currentMilestone = eraMilestones[currentEraIndex];
            var currentEraPool = vocab.InnovationDefs.Values
                .Where(def => def.Era == currentMilestone.EraKey)
                .Select(def => def.Key)
                .ToList();

            if (currentEraPool.Count > 0)
            {
                int targetCurrentCount;

                if (currentEraIndex == 0)
                {
                    // In Tribal era: Scale from Year 1 (1-2 techs) up to Year 867-899 (7-9 techs)
                    double timeProgress = Math.Clamp(startYear / 900.0, 0.0, 1.0);
                    double baseTribal = 1.0 + timeProgress * 7.0; // 1 at yr 0, ~8 at yr 900
                    double devBonus = (culture.MeanDevelopment - 8.0) * 0.2;
                    targetCurrentCount = (int)Math.Round(baseTribal + devBonus + (rng.NextDouble() * 2.0 - 1.0));
                    targetCurrentCount = Math.Clamp(targetCurrentCount, 1, currentEraPool.Count);
                }
                else
                {
                    // In a Medieval era: Scale by how far into the era's time window the bookmark is
                    double eraDuration = Math.Max(1, currentMilestone.EndYear - currentMilestone.StartYear);
                    double eraProgress = Math.Clamp((startYear - currentMilestone.StartYear) / eraDuration, 0.0, 1.0);

                    double baseMedieval = 2.0 + eraProgress * 6.0; // 2 at era start, 8 near era end
                    double devBonus = (culture.MeanDevelopment - 10.0) * 0.25;
                    targetCurrentCount = (int)Math.Round(baseMedieval + devBonus + (rng.NextDouble() * 2.0 - 1.0));
                    targetCurrentCount = Math.Clamp(targetCurrentCount, 1, currentEraPool.Count);
                }

                SampleWeightedInnovations(chosenByEra[currentMilestone.EraKey], currentEraPool, targetCurrentCount, culture, vocab, frequencies, rng);
            }

            // 3. Write History Output
            var sb = new StringBuilder();
            sb.Append($"# {culture.Name}, of the {culture.Heritage.Name} heritage (Mean Dev: {culture.MeanDevelopment:F1}).\n\n");

            for (int i = 0; i <= currentEraIndex; i++)
            {
                var (eraKey, eraStart, _, dateStr) = eraMilestones[i];
                var eraInns = chosenByEra[eraKey];

                string blockDate = (i == currentEraIndex && startYear >= eraStart)
                    ? cfg.StartDate
                    : dateStr;

                sb.Append($"{blockDate} = {{\n");

                foreach (string inn in eraInns.OrderBy(k => k, StringComparer.Ordinal))
                {
                    sb.Append($"\tdiscover_innovation = {inn}\n");
                }

                // Promote to next era at the end of the completed era block
                if (i < currentEraIndex)
                {
                    string nextEra = eraMilestones[i + 1].EraKey;
                    sb.Append($"\tjoin_era = {nextEra}\n");
                }

                sb.Append("}\n\n");
                totalAssigned += eraInns.Count;
            }

            ParadoxText.WriteBom(Path.Combine(dir, $"{culture.Key}.txt"), sb.ToString());
        }

        Console.WriteLine($"  culture history: {(double)totalAssigned / cultures.Cultures.Count:F1} " +
                          $"starting innovations per culture across {currentEraIndex + 1} eras");
    }

    private static void SampleWeightedInnovations(
        List<string> destination,
        List<string> candidatePool,
        int targetCount,
        Culture culture,
        VanillaVocabulary vocab,
        Dictionary<string, double> frequencies,
        Rng rng)
    {
        var weightedCandidates = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (string key in candidatePool)
        {
            if (destination.Contains(key)) continue;

            double baseWeight = frequencies.TryGetValue(key, out double freq) ? Math.Max(0.15, freq) : 0.35;

            double ethosWeight = 1.0;
            if (vocab.InnovationDefs.TryGetValue(key, out var def))
            {
                if (culture.Ethos == "ethos_bellicose" && def.Group == "culture_group_military") ethosWeight = 1.6;
                else if (culture.Ethos is "ethos_bureaucratic" or "ethos_courtly" or "ethos_spiritual" && def.Group == "culture_group_civic") ethosWeight = 1.4;
            }

            weightedCandidates[key] = baseWeight * ethosWeight;
        }

        while (destination.Count < targetCount && weightedCandidates.Count > 0)
        {
            double totalWeight = weightedCandidates.Values.Sum();
            if (totalWeight <= 0) break;

            double roll = rng.NextDouble() * totalWeight;
            double cumulative = 0.0;
            string? selected = null;

            foreach (var (key, weight) in weightedCandidates)
            {
                cumulative += weight;
                if (roll <= cumulative)
                {
                    selected = key;
                    break;
                }
            }

            if (selected is not null)
            {
                destination.Add(selected);
                weightedCandidates.Remove(selected);
            }
            else break;
        }
    }
    private static int ParseStartYear(string startDate)
    {
        var m = Regex.Match(startDate, @"^\s*(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out int year) ? year : 867;
    }

    /// <summary>
    /// Every generated string the culture layer introduces.
    ///
    /// Name tokens are shared across cultures on purpose. Two cultures that happen to coin the same
    /// short word both want the same displayed text, so the key is emitted once and they share it —
    /// which is what vanilla does too, where several cultures list the same given name.
    /// </summary>
    /// <inheritdoc cref="WriteCultures"/>
    internal static void WriteLocalisation(string modDir, CultureMap cultures)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var heritage in cultures.Heritages)
        {
            entries[$"{heritage.Key}_name"] = heritage.Name;
            entries[$"{heritage.Key}_collective_noun"] = Plural(heritage.Name);
            entries[$"{heritage.Language.Key}_name"] = heritage.Language.Name;
        }

        foreach (var culture in cultures.Cultures)
        {
            entries[culture.Key] = culture.Name;
            entries[$"{culture.Key}_name"] = culture.Name;
            entries[$"{culture.Key}_collective_noun"] = Plural(culture.Name);
            entries[$"{culture.Key}_prefix"] = culture.Prefix;
            entries[$"mercenary_company_{culture.Key}"] = $"{culture.Name} Company";

            entries[$"dynnp_{culture.Key}"] = culture.LocationPrefix + " ";
            entries[$"dynnpat_suf_{culture.Key}_male"] = culture.PatronymSuffixMale;
            entries[$"dynnpat_suf_{culture.Key}_female"] = culture.PatronymSuffixFemale;

            // Apply the "cul_" prefix to character names in the localization file
            foreach (string name in culture.MaleNames) entries[$"cul_{CleanKey(name)}"] = name;
            foreach (string name in culture.FemaleNames) entries[$"cul_{CleanKey(name)}"] = name;
            foreach (string name in culture.DynastyNames) entries[$"dynn_{CleanKey(name)}"] = name;
        }

        var sb = new StringBuilder();
        sb.Append("l_english:\n");
        foreach (var (key, value) in entries) sb.Append($" {key}:0 \"{value}\"\n");

        ParadoxText.WriteBom(Path.Combine(dir, "gen_cultures_l_english.yml"), sb.ToString());
    }

    /// <summary>
    /// Crude English pluralisation, which is all a collective noun needs. The names are invented,
    /// so there is no correct answer to get wrong — only a jarring one, and "Aldrichs" is worse
    /// than "Aldriches" often enough to be worth the two cases.
    /// </summary>
    private static string Plural(string name)
        => name.EndsWith('s') || name.EndsWith("ch", StringComparison.Ordinal)
           || name.EndsWith("sh", StringComparison.Ordinal) || name.EndsWith('x')
            ? name + "es"
            : name + "s";
}
