using System.Text;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

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
    public static void WriteAll(string modDir, CultureMap cultures, VanillaVocabulary vocab, Rng rng)
    {
        WritePillars(modDir, cultures);
        WriteCultures(modDir, cultures);
        WriteNameLists(modDir, cultures);
        WriteHistory(modDir, cultures, vocab, rng);
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
            if (heritage.LanguageColor is { } color) sb.Append($"\tcolor = {color}\n");
            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_pillars.txt"), sb.ToString());
    }

    private static void WriteCultures(string modDir, CultureMap cultures)
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

            sb.Append("\tethnicities = {\n");
            foreach (string line in look.Ethnicities.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) sb.Append($"\t\t{trimmed}\n");
            }
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
                sb.Append($"\t\t\"dynn_{name}\"\n");
            sb.Append("\t}\n\n");

            Append("male_names", culture.MaleNames);
            Append("female_names", culture.FemaleNames);

            sb.Append("\tdynasty_names = {\n");
            foreach (string name in culture.DynastyNames) sb.Append($"\t\t\"dynn_{name}\"\n");
            sb.Append("\t}\n\n");

            sb.Append($"\tdynasty_of_location_prefix = \"dynnp_{culture.Key}\"\n\n");
            sb.Append($"\tpatronym_suffix_male = \"dynnpat_suf_{culture.Key}_male\"\n");
            sb.Append($"\tpatronym_suffix_female = \"dynnpat_suf_{culture.Key}_female\"\n");
            if (culture.AlwaysUsePatronym) sb.Append("\talways_use_patronym = yes\n");
            sb.Append('\n');

            // Vanilla's own comment on these: the male and the female set must each sum to at most
            // 100, and what is left over is the chance of an unrelated name.
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
                    sb.Append("\t\t").Append(string.Join(' ', names.Skip(i).Take(8))).Append('\n');
                sb.Append("\t}\n\n");
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_name_lists.txt"), sb.ToString());
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
    private static void WriteHistory(string modDir, CultureMap cultures, VanillaVocabulary vocab,
        Rng rng)
    {
        if (vocab.InnovationFrequency.Count == 0) return;

        string dir = Path.Combine(modDir, "history", "cultures");
        Directory.CreateDirectory(dir);

        int total = 0;

        foreach (var culture in cultures.Cultures)
        {
            var chosen = vocab.InnovationFrequency
                .Where(kv => rng.Chance(kv.Value))
                .Select(kv => kv.Key)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.Append($"# {culture.Name}, of the {culture.Heritage.Name} heritage.\n\n");
            sb.Append($"{HistoryWriter.StartDate} = {{\n");
            foreach (string innovation in chosen)
                sb.Append($"\tdiscover_innovation = {innovation}\n");
            sb.Append("}\n");

            total += chosen.Count;
            ParadoxText.WriteBom(Path.Combine(dir, $"{culture.Key}.txt"), sb.ToString());
        }

        Console.WriteLine($"  culture history: {(double)total / cultures.Cultures.Count:F1} " +
                          $"starting innovations per culture (vanilla averages 6.7)");
    }

    /// <summary>
    /// Every generated string the culture layer introduces.
    ///
    /// Name tokens are shared across cultures on purpose. Two cultures that happen to coin the same
    /// short word both want the same displayed text, so the key is emitted once and they share it —
    /// which is what vanilla does too, where several cultures list the same given name.
    /// </summary>
    private static void WriteLocalisation(string modDir, CultureMap cultures)
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
            entries[$"{culture.Key}_collective_noun"] = Plural(culture.Name);
            entries[$"mercenary_company_{culture.Key}"] = $"{culture.Name} Company";

            // The trailing space is part of the value: CK3 concatenates the prefix onto the place
            // name without adding one, so "av" would render "avOslo".
            entries[$"dynnp_{culture.Key}"] = culture.LocationPrefix + " ";
            entries[$"dynnpat_suf_{culture.Key}_male"] = culture.PatronymSuffixMale;
            entries[$"dynnpat_suf_{culture.Key}_female"] = culture.PatronymSuffixFemale;

            foreach (string name in culture.MaleNames) entries[name] = name;
            foreach (string name in culture.FemaleNames) entries[name] = name;
            foreach (string name in culture.DynastyNames) entries[$"dynn_{name}"] = name;
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
