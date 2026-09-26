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
    /// <param name="generated">Innovations this run invented, so a culture's history can record
    /// the ones it already holds. Null when nothing generated any — the histories then contain
    /// only vanilla's, exactly as before <see cref="InnovationMap"/> existed.</param>
    public static void WriteAll(string modDir, MapConfig cfg, CultureMap cultures,
        EthnicityMap ethnicityMap, VanillaVocabulary vocab, Rng rng,
        InnovationMap? generated = null)
    {
        WritePillars(modDir, cultures);
        WriteCultures(modDir, cultures, ethnicityMap);
        WriteNameLists(modDir, cultures);
        WriteHistory(modDir, cfg, cultures, vocab, rng, generated);
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

        var b = new JominiBuilder();
        b.Comment("Generated heritages and languages.");
        b.Blank();

        foreach (var heritage in cultures.Heritages)
        {
            using (b.Block(heritage.Key))
            {
                b.Field("type", "heritage");

                // Indented one level too deep until the builder made that unrepresentable. CK3
                // ignores whitespace, so this was never a functional bug -- only an invisible one.
                b.Field("audio_parameter", "european");

                using (b.Block("parameters")) { }

                using (b.Block("is_shown"))
                    b.Inline("heritage_is_shown_trigger", $"HERITAGE = {heritage.Key}");
            }

            b.Blank();

            var language = heritage.Language;

            using (b.Block(language.Key))
            {
                b.Field("type", "language");

                using (b.Block("is_shown"))
                    b.Inline("language_is_shown_trigger", $"LANGUAGE = {language.Key}");

                using (b.Block("ai_will_do"))
                {
                    b.Field("value", "10");

                    using (b.Block("if"))
                    {
                        b.Inline("limit", $"has_cultural_pillar = {language.Key}");
                        b.Field("multiply", "10");
                    }
                }

                // No exclusion list here any more. `tungusic` used to be named and skipped, which
                // was the right instinct against the wrong scope — it is one of two vanilla
                // colours that are referenced but never declared, and `khitan` one line below it
                // in vanilla's file went on leaking through. Both are now filtered at the harvest
                // against what common/named_colors actually declares. See
                // VanillaVocabulary.NamedColors.
                if (heritage.LanguageColor is { } color) b.Field("color", color);
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_pillars.txt"), b.ToString());
    }

    /// <summary>
    /// Not private: a culture's name, colour, ethos and traditions all live in this file, so
    /// editing one after the mod is written re-runs exactly this. See <see cref="WorldOverwrite"/>.
    /// </summary>
    internal static void WriteCultures(string modDir, CultureMap cultures, EthnicityMap ethnicityMap)
    {
        string dir = Path.Combine(modDir, "common", "culture", "cultures");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Generated cultures. Vanilla's are left declared but unheld.");
        b.Blank();

        foreach (var culture in cultures.Cultures)
        {
            using (b.Block(culture.Key))
            {
                b.Color("color", culture.Color.R, culture.Color.G, culture.Color.B);
                b.Blank();

                b.Field("ethos", culture.Ethos);
                b.Field("heritage", culture.Heritage.Key);
                b.Field("language", culture.Language.Key);
                b.Field("martial_custom", culture.MartialCustom);
                b.Field("head_determination", culture.HeadDetermination);
                b.Blank();

                using (b.Block("traditions"))
                    foreach (string tradition in culture.Traditions) b.Token(tradition);

                b.Blank();

                b.Field("name_list", culture.NameListKey);
                b.Blank();

                // Borrowed whole off one vanilla culture so the four sets and the ethnicities agree.
                // Culture visual graphics and holding models
                b.Field("coa_gfx", culture.CoaGfx);
                b.Field("building_gfx", culture.BuildingGfx);
                b.Field("clothing_gfx", culture.ClothingGfx);
                b.Field("unit_gfx", culture.UnitGfx);

                if (HouseFrameFor(culture) is { } frame)
                {
                    b.Field("house_coa_frame", frame.Frame);
                    if (frame.MaskOffset is not null) b.Field("house_coa_mask_offset", frame.MaskOffset);
                    if (frame.MaskScale is not null) b.Field("house_coa_mask_scale", frame.MaskScale);
                }

                b.Blank();

                // One generated ethnicity rather than the vanilla culture's whole weighted list. The
                // borrowed list describes the people it was lifted from; a generated culture has its own
                // look to declare, and pointing at a single definition is what lets it.
                using (b.Block("ethnicities"))
                    foreach (var (variantKey, weight) in ethnicityMap.VariantsFor(culture))
                        b.Field($"{weight}", variantKey);
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_cultures.txt"), b.ToString());
    }

    /// <summary>
    /// The shield this culture's houses are drawn on, or null for the engine's standard one.
    ///
    /// CK3 has no per-dynasty or per-house shape: the frame belongs to the culture, and the house
    /// window reads it off the culture of the house. So variety across a map comes from its peoples,
    /// as it does in vanilla.
    ///
    /// Read off the vanilla culture whose heraldry this one wears — its <c>coa_gfx</c>, not the
    /// whole look, so that retyping the heraldry in the inspector brings the matching shield with
    /// it. The heritage's own source culture first, so sister cultures share one shield; then any
    /// vanilla culture with the same heraldry that sets a frame, since ten do not.
    ///
    /// <see cref="VanillaVocabulary.Current"/> rather than a parameter because the editor's
    /// rewrite path (<see cref="WorldOverwrite"/>) has no vocabulary in hand; it is always read
    /// before anything is generated.
    /// </summary>
    internal static VanillaVocabulary.HouseFrame? HouseFrameFor(Culture culture)
    {
        if (VanillaVocabulary.Current is not { } vocab) return null;

        var sources = vocab.Looks.Where(l => l.CoaGfx == culture.CoaGfx).Select(l => l.SourceCulture);
        if (culture.Heritage.Look.CoaGfx == culture.CoaGfx) sources = sources.Prepend(culture.Heritage.Look.SourceCulture);

        foreach (string source in sources)
            if (vocab.HouseFrames.TryGetValue(source, out var frame)) return frame;

        return null;
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

        var b = new JominiBuilder();
        b.Comment("Generated name lists, one per culture.");
        b.Blank();

        // Names go out eight to a line: they are bare tokens, and one per line would make a file
        // thousands of lines long that nobody could scan.
        void NameBlock(string field, List<string> names)
        {
            using (b.Block(field))
                for (int i = 0; i < names.Count; i += 8)
                {
                    // Clean the keys and prefix with "cul_" to prevent Murmur3A hash collisions
                    var cleanNames = names.Skip(i).Take(8).Select(n => $"cul_{CleanKey(n)}");
                    b.Token(string.Join(' ', cleanNames));
                }

            b.Blank();
        }

        void DynastyBlock(string field, IEnumerable<string> names)
        {
            using (b.Block(field))
                foreach (string name in names) b.Token($"\"dynn_{CleanKey(name)}\"");

            b.Blank();
        }

        foreach (var culture in cultures.Cultures)
        {
            using (b.Block(culture.NameListKey))
            {
                DynastyBlock("cadet_dynasty_names", culture.DynastyNames.Take(12));

                NameBlock("male_names", culture.MaleNames);
                NameBlock("female_names", culture.FemaleNames);

                DynastyBlock("dynasty_names", culture.DynastyNames);

                b.Quoted("dynasty_of_location_prefix", $"dynnp_{culture.Key}");
                b.Blank();

                // A language without a patronymic (Latin, German) simply has none: CK3 then
                // names by dynasty alone, which is what those cultures do in vanilla too.
                if (culture.PatronymSuffixMale.Length > 0)
                {
                    string kind = culture.PatronymIsPrefix ? "prefix" : "suffix";
                    b.Quoted($"patronym_{kind}_male", $"dynnpat_suf_{culture.Key}_male");
                    b.Quoted($"patronym_{kind}_female", $"dynnpat_suf_{culture.Key}_female");
                    if (culture.AlwaysUsePatronym) b.Field("always_use_patronym", "yes");
                }
                b.Blank();

                b.Field("pat_grf_name_chance", "40");
                b.Field("mat_grf_name_chance", "10");
                b.Field("father_name_chance", "5");
                b.Blank();

                b.Field("pat_grm_name_chance", "10");
                b.Field("mat_grm_name_chance", "40");
                b.Field("mother_name_chance", "5");
                b.Blank();

                using (b.Block("mercenary_names"))
                    b.Token($"{{ name = \"mercenary_company_{culture.Key}\" }}");
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_name_lists.txt"), b.ToString());
    }
    private static string CleanKey(string input) => Core.Ascii.Fold(input, keepSeparators: true);

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

    private static void WriteHistory(string modDir, MapConfig cfg, CultureMap cultures,
        VanillaVocabulary vocab, Rng rng, InnovationMap? generated = null)
    {
        if (vocab.InnovationDefs.Count == 0) return;

        string dir = Path.Combine(modDir, "history", "cultures");
        Directory.CreateDirectory(dir);

        foreach (var file in Directory.GetFiles(dir, "gen_culture_*.txt"))
        {
            File.Delete(file);
        }

        // The era year, not the calendar year. Everything in this method is asking how advanced the
        // world is — which innovations exist yet, how far through its era it is — and that question
        // is answered on vanilla's timeline whatever the world calls the year.
        int startYear = cfg.EraYear;
        var (frequencies, _) = vocab.GetFrequenciesAtYear(startYear);

        var eraMilestones = new (string EraKey, int StartYear, int EndYear)[]
        {
        ("culture_era_tribal", 0, 900),
        ("culture_era_early_medieval", 900, 1050),
        ("culture_era_high_medieval", 1050, 1200),
        ("culture_era_late_medieval", 1200, 1453)
        };

        int currentEraIndex = startYear switch
        {
            < 900 => 0,
            < 1050 => 1,
            < 1200 => 2,
            _ => 3
        };

        int totalAssigned = 0;

        // Vanilla's own men-at-arms unlocks, kept out of the sampled pool when this world is
        // writing a roster of its own.
        //
        // The tradition filter in Cultures closed the narrower of the two routes to a vanilla
        // named regiment; this is the wider one. Sixteen vanilla innovations land in each
        // culture's history, so before this a generated people with no camel tradition anywhere
        // still opened the game able to recruit Camel Riders — `innovation_war_camels` grants them
        // and `camel_rider` has no can_recruit of its own to fail — and a jungle people drew
        // `innovation_elephantry` and vanilla's war elephants beside the elephants this generator
        // had just invented for them. See VanillaVocabulary.GrantsVanillaRegiment for why those
        // two are caught by different tests and why the generic roster is caught by neither.
        bool blockVanillaMaa = MapGen.Retinues.ReplacesVanillaRosters(vocab, cfg);

        List<VanillaVocabulary.InnovationDef> EraPool(string era)
            => [.. vocab.InnovationDefs.Values.Where(def => def.Era == era)];

        List<string> Sampleable(List<VanillaVocabulary.InnovationDef> pool)
            => [.. pool.Where(def => !blockVanillaMaa || !vocab.GrantsVanillaRegiment(def))
                       .Select(def => def.Key)];

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
                var wholeEra = EraPool(pastEra);
                var pastEraPool = Sampleable(wholeEra);

                if (pastEraPool.Count == 0) continue;

                // CK3 requires at least 8 innovations (or 50%) of the era to qualify for the next
                // era — of the era as the engine counts it, which is why the threshold is measured
                // against the whole era and only the *sampling* is done from the filtered pool.
                int minRequired = Math.Min(pastEraPool.Count, Math.Max(8, (int)Math.Ceiling(wholeEra.Count * 0.5)));

                // Completion share: ~55%-65% for poor ground, up to ~85%-95% for wealthy ground
                double completionRate = 0.55 + 0.35 * devNormalized + (rng.NextDouble() * 0.1 - 0.05);
                int targetPastCount = (int)Math.Round(pastEraPool.Count * Math.Clamp(completionRate, 0.50, 0.95));
                targetPastCount = Math.Clamp(targetPastCount, minRequired, pastEraPool.Count);

                SampleWeightedInnovations(chosenByEra[pastEra], pastEraPool, targetPastCount, culture, vocab, frequencies, rng);
            }

            // 2. Process Current Active Era
            var currentMilestone = eraMilestones[currentEraIndex];
            var currentEraPool = Sampleable(EraPool(currentMilestone.EraKey));

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
            var b = new JominiBuilder();
            b.Comment($"{culture.Name}, of the {culture.Heritage.Name} heritage (Mean Dev: {culture.MeanDevelopment:F1}).");
            b.Blank();

            for (int i = 0; i <= currentEraIndex; i++)
            {
                var (eraKey, eraStart, _) = eraMilestones[i];
                var eraInns = chosenByEra[eraKey];

                // The era boundaries above are vanilla's; the block dates have to be the world's,
                // because this is history and history is read on the game clock. On a run that has
                // not moved the two apart the offset is zero and these come out as the literal
                // 1.1.1 / 900.1.1 / 1050.1.1 / 1200.1.1 they always were.
                string blockDate = (i == currentEraIndex && startYear >= eraStart)
                    ? cfg.StartDate
                    : $"{EraDate(eraStart, cfg)}.1.1";

                // Innovations this run invented that this culture already holds, slotted into the
                // block for their own era. Anything dated past the world's current era would be
                // discovered before the culture had reached it, so those are dropped rather than
                // clamped — an elite regiment nobody starts with is the intended outcome, and it
                // is still there in the tree to be worked towards.
                List<string> invented = generated is null
                    ? []
                    : [.. generated.StartingFor(culture)
                                   .Where(inv => Innovations.IndexOf(inv.Era) == i)
                                   .Select(inv => inv.Key)];

                var held = eraInns.Concat(invented).OrderBy(k => k, StringComparer.Ordinal).ToList();

                // With a bookmark before the start date each era's innovations arrive over its span
                // rather than all at once, and a finished era is joined when it ends rather than
                // when it begins — otherwise every bookmark opens as advanced as the last one. By
                // the start date the culture holds exactly what it would have without them.
                if (cfg.UsesAdditionalBookmarks && cfg.AdditionalBookmarkYears.Any(y => y < cfg.StartYear))
                {
                    int began = EraDate(eraStart, cfg);
                    if (i < currentEraIndex)
                    {
                        int ended = EraDate(eraMilestones[i].EndYear, cfg);
                        held = WriteEarlierDiscoveries(b, cfg, held, began, ended, frequencies);
                        blockDate = $"{ended}.1.1";
                    }
                    else if (blockDate == cfg.StartDate)
                    {
                        held = WriteEarlierDiscoveries(b, cfg, held, began, cfg.StartYear, frequencies);
                    }
                }

                using (b.Block(blockDate))
                {
                    foreach (string inn in held)
                        b.Field("discover_innovation", inn);

                    // Promote to next era at the end of the completed era block
                    if (i < currentEraIndex) b.Field("join_era", eraMilestones[i + 1].EraKey);
                }

                b.Blank();
                totalAssigned += eraInns.Count;
            }

            // 4. Bookmarks after the start date: the culture keeps learning. Dated after the start,
            // on a stream of the culture's own, so the start date's culture is untouched.
            var laterYears = cfg.UsesAdditionalBookmarks
                ? cfg.AdditionalBookmarkYears.Where(y => y > cfg.StartYear).ToList()
                : [];
            if (laterYears.Count > 0)
            {
                var later = new Rng(Rng.StableHash(culture.Key) ^ 0x1A7EUL);
                int eraIndex = currentEraIndex;

                foreach (int year in laterYears)
                {
                    int advanced = cfg.EraYearAt(year);
                    var (laterFrequencies, _) = vocab.GetFrequenciesAtYear(advanced);

                    // Every era this date has passed the end of: completed as a past era is, and
                    // joined on its own end date.
                    while (eraIndex < eraMilestones.Length - 1 && advanced >= eraMilestones[eraIndex].EndYear)
                    {
                        var (key, _, end) = eraMilestones[eraIndex];
                        var whole = EraPool(key);
                        var pool = Sampleable(whole);
                        var have = Holding(key);
                        int before = have.Count;

                        int minRequired = Math.Min(pool.Count, Math.Max(8, (int)Math.Ceiling(whole.Count * 0.5)));
                        double rate = 0.55 + 0.35 * devNormalized + (later.NextDouble() * 0.1 - 0.05);
                        int target = Math.Clamp((int)Math.Round(pool.Count * Math.Clamp(rate, 0.50, 0.95)),
                            minRequired, pool.Count);
                        SampleWeightedInnovations(have, pool, Math.Max(target, before), culture, vocab,
                            laterFrequencies, later);

                        int joined = Math.Clamp(end + cfg.EraOffset, cfg.StartYear + 1, year);
                        using (b.Block($"{joined}.1.1"))
                        {
                            foreach (string inn in have.Skip(before).Order(StringComparer.Ordinal))
                                b.Field("discover_innovation", inn);
                            b.Field("join_era", eraMilestones[eraIndex + 1].EraKey);
                        }

                        b.Blank();
                        eraIndex++;
                    }

                    // Then as far into its current era as this date reaches, on the same scale the
                    // start date's current era is graded on.
                    var (curKey, curStart, curEnd) = eraMilestones[eraIndex];
                    var curPool = Sampleable(EraPool(curKey));
                    var curHave = Holding(curKey);
                    int had = curHave.Count;
                    if (curPool.Count == 0) continue;

                    double progress = Math.Clamp((advanced - curStart) / (double)Math.Max(1, curEnd - curStart), 0.0, 1.0);
                    double baseCount = eraIndex == 0
                        ? 1.0 + Math.Clamp(advanced / 900.0, 0.0, 1.0) * 7.0 + (culture.MeanDevelopment - 8.0) * 0.2
                        : 2.0 + progress * 6.0 + (culture.MeanDevelopment - 10.0) * 0.25;
                    int curTarget = Math.Clamp((int)Math.Round(baseCount), 1, curPool.Count);
                    SampleWeightedInnovations(curHave, curPool, Math.Max(curTarget, had), culture, vocab,
                        laterFrequencies, later);

                    if (curHave.Count == had) continue;
                    using (b.Block($"{year}.1.1"))
                        foreach (string inn in curHave.Skip(had).Order(StringComparer.Ordinal))
                            b.Field("discover_innovation", inn);
                    b.Blank();
                }

                List<string> Holding(string era)
                {
                    if (!chosenByEra.TryGetValue(era, out var list)) chosenByEra[era] = list = [];
                    return list;
                }
            }

            ParadoxText.WriteBom(Path.Combine(dir, $"{culture.Key}.txt"), b.ToString());
        }

        Console.WriteLine($"  culture history: {(double)totalAssigned / cultures.Cultures.Count:F1} " +
                          $"starting innovations per culture across {currentEraIndex + 1} eras");
    }

    /// <summary>
    /// Writes the share of an era's innovations each bookmark before the start date already has, in
    /// proportion to how far through the era it falls, and returns what is left for the era's last
    /// block. The commonest go first — what most of vanilla's cultures hold is what a culture learned
    /// early. At least one arrives by the first bookmark inside the era, so no culture there opens
    /// with nothing it can build on.
    /// </summary>
    private static List<string> WriteEarlierDiscoveries(JominiBuilder b, MapConfig cfg, List<string> held,
        int eraBegan, int eraEnds, Dictionary<string, double> frequencies)
    {
        var ordered = held
            .OrderByDescending(k => frequencies.GetValueOrDefault(k))
            .ThenBy(k => k, StringComparer.Ordinal)
            .ToList();

        int done = 0;
        foreach (int year in cfg.AdditionalBookmarkYears.Where(y => y > eraBegan && y < eraEnds))
        {
            double share = (double)(year - eraBegan) / Math.Max(1, eraEnds - eraBegan);
            int upTo = Math.Max(Math.Min(1, ordered.Count), (int)Math.Round(ordered.Count * share));
            if (upTo <= done) continue;

            using (b.Block($"{year}.1.1"))
                foreach (string inn in ordered.Skip(done).Take(upTo - done).OrderBy(k => k, StringComparer.Ordinal))
                    b.Field("discover_innovation", inn);

            b.Blank();
            done = upTo;
        }

        var rest = ordered.Skip(done).ToHashSet(StringComparer.Ordinal);
        return [.. held.Where(rest.Contains)];
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
    /// <summary>
    /// A vanilla era boundary, moved onto the world's own calendar.
    ///
    /// Clamped at both ends and for different reasons. Below 1 there is no such thing as a date, and
    /// a world whose calendar starts near zero would otherwise ask the game to apply history in year
    /// -400. At the top, a block dated on or after the bookmark is a block the game never applies —
    /// the innovations in it would silently not exist — so an era that the offset pushes past the
    /// start date is pulled back to the year before it.
    /// </summary>
    private static int EraDate(int vanillaYear, MapConfig cfg)
        => Math.Clamp(vanillaYear + cfg.EraOffset, 1, Math.Max(1, cfg.StartYear - 1));

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
            // A prefix patronymic is a separate word ("ibn Yusuf"); a suffix is glued on ("Sigurdsson").
            string pad = culture.PatronymIsPrefix ? " " : "";
            entries[$"dynnpat_suf_{culture.Key}_male"] = culture.PatronymSuffixMale + pad;
            entries[$"dynnpat_suf_{culture.Key}_female"] = culture.PatronymSuffixFemale + pad;

            // Apply the "cul_" prefix to character names in the localization file
            foreach (string name in culture.MaleNames) entries[$"cul_{CleanKey(name)}"] = name;
            foreach (string name in culture.FemaleNames) entries[$"cul_{CleanKey(name)}"] = name;
            foreach (string name in culture.DynastyNames) entries[$"dynn_{CleanKey(name)}"] = name;
        }

        var loc = new LocFile();
        foreach (var (key, value) in entries) loc.AddBuilt(key, value);

        loc.Write(Path.Combine(dir, "gen_cultures_l_english.yml"));
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
