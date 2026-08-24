using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>A generated culture group: one language, one look, and the cultures that share them.</summary>
public sealed class Heritage
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required Language Language { get; init; }
    public required VanillaVocabulary.Look Look { get; init; }

    /// <summary>
    /// Named colour for the language map mode, borrowed from a vanilla language pillar. Null when
    /// the install had none to borrow, in which case the entry is written without one — the pillar
    /// still works and only the map mode loses its shading.
    /// </summary>
    public string? LanguageColor { get; init; }

    public List<Culture> Cultures { get; } = [];

    /// <summary>
    /// The race a fantasy-preset export tagged this heritage's dominant culture with, or null on a
    /// generated map or an untagged export. Consumed by <see cref="Ethnicities"/>, which otherwise
    /// guesses races from terrain — the export's own answer outranks the guess.
    /// </summary>
    public RaceArchetype? ImportedArchetype { get; set; }
}

/// <summary>
/// One generated culture, with everything CK3 needs to declare it and everything we need to name
/// its people and its land.
/// </summary>
public sealed class Culture
{
    public required double MeanDevelopment { get; init; }
    /// <summary>Frozen. Every other file references a culture by this.</summary>
    public required string Key { get; init; }

    public required string Name { get; set; }

    /// <summary>Same as <see cref="Heritage.ImportedArchetype"/>, at culture grain — set by the
    /// renamer when the export tags this culture's own ground, which can disagree with the
    /// heritage's majority where two races share a region.</summary>
    public RaceArchetype? ImportedArchetype { get; set; }

    /// <summary>Frozen: it owns the language every name here is drawn from.</summary>
    public required Heritage Heritage { get; init; }
    public Language Language => Heritage.Language;

    public required (byte R, byte G, byte B) Color { get; set; }
    public required string Ethos { get; set; }
    public required string MartialCustom { get; set; }
    public required string HeadDetermination { get; set; }
    public required List<string> Traditions { get; set; }

    public required string CoaGfx { get; set; }
    public required string BuildingGfx { get; set; }
    public required string ClothingGfx { get; set; }
    public required string UnitGfx { get; set; }

    /// <summary>
    /// This people's words for its realms and their holders — Tsardom and Tsar rather than Kingdom
    /// and King — keyed by government token (feudal, clan, tribal, republic, theocracy,
    /// administrative, nomad). A government absent here uses vanilla's own words. Decided by
    /// <see cref="Emit.TitleTierWriter.Assign"/>, editable, and written by
    /// <see cref="Emit.TitleTierWriter.WriteAll"/>.
    ///
    /// Applies to every realm whose <em>top liege</em> is of this culture, whatever the vassal's
    /// own people — see the writer for why — so it is a statement about a realm's style, not a
    /// character's.
    /// </summary>
    public Dictionary<string, Emit.TitleVocabulary> RealmWords { get; set; } = [];

    public string NameListKey => $"name_list_{Key}";

    /// <summary>
    /// The combining form CK3 splices into a hybrid culture's name ("Burgundo" + "-French"). Without
    /// it the game renders the raw <c>{key}_prefix</c> token, so every culture needs one. Vanilla's
    /// are all the name with its final vowel traded for an -o.
    /// </summary>
    public string Prefix
        => "aeiouy".Contains(char.ToLowerInvariant(Name[^1]))
            ? Name[..^1] + "o"
            : Name + "o";

    public required List<string> MaleNames { get; init; }
    public required List<string> FemaleNames { get; init; }
    public required List<string> DynastyNames { get; init; }

    /// <summary>Localisation keys, with the words they stand for, for this culture's name grammar.</summary>
    public required string PatronymSuffixMale { get; init; }

    public required string PatronymSuffixFemale { get; init; }
    public required string LocationPrefix { get; init; }
    public required bool AlwaysUsePatronym { get; init; }

    /// <summary>Counties speaking this culture at the start date.</summary>
    public List<Title> Counties { get; } = [];
}

/// <summary>The finished cultural geography, and the lookup everything downstream reads.</summary>
public sealed class CultureMap
{
    public required List<Heritage> Heritages { get; init; }
    public required List<Culture> Cultures { get; init; }
    public required Dictionary<Title, Culture> ByCounty { get; init; }

    /// <summary>
    /// The culture of any title, by majority of the counties beneath it. A duchy is named in the
    /// language most of it speaks, which is the whole point of naming after cultures rather than
    /// from one global pool.
    /// </summary>
    public Culture For(Title title)
    {
        if (ByCounty.TryGetValue(title, out var direct)) return direct;

        var votes = new Dictionary<Culture, int>();
        foreach (var county in Titles.Flatten([title]).Where(t => t.Tier == "c"))
            if (ByCounty.TryGetValue(county, out var c))
                votes[c] = votes.GetValueOrDefault(c) + 1;

        // Baronies sit below a county and so have no counties of their own to count.
        if (votes.Count == 0)
        {
            for (var p = title.Parent; p is not null; p = p.Parent)
                if (ByCounty.TryGetValue(p, out var inherited)) return inherited;
            return Cultures[0];
        }

        return votes.OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key.Key, StringComparer.Ordinal).First().Key;
    }
}

/// <summary>
/// Grows cultures out of the map's own geography.
///
/// The structure is deliberately two levels deep — heritages first, then cultures inside each — and
/// that nesting is the load-bearing part. CK3's cultural acceptance, hybridisation and divergence
/// all key off shared heritage and shared language, so cultures scattered independently across the
/// map produce a world where no two neighbours can ever get along and no hybrid is ever possible.
/// Seeding heritages first and subdividing them means a culture's neighbours are usually its
/// cousins, exactly as on the real map, and it comes free: the sub-partition is bounded by the
/// parent region, so contiguity is structural rather than something to check for afterwards.
///
/// It also gives names for free. A heritage owns one <see cref="Language"/>, so every culture under
/// it draws from the same sound inventory and they come out sounding related without any similarity
/// metric anywhere.
/// </summary>
public static class Cultures
{
    /// <summary>
    /// What it costs a people to spread across a given terrain, as a multiplier on distance.
    ///
    /// These are not the carrying-capacity numbers <see cref="Development"/> ranks counties by, and
    /// must not be unified with them: rich and passable are different questions. Steppe is poor
    /// ground that
    /// carries a culture a very long way, and a floodplain is rich ground that a language crosses no
    /// faster than any other flat country.
    /// </summary>
    private static double Crossing(TerrainClass t) => t switch
    {
        TerrainClass.DesertMountains => 11.0,
        TerrainClass.Mountains => 8.0,
        TerrainClass.Arctic => 7.5,
        TerrainClass.Desert => 6.0,
        TerrainClass.Jungle => 4.5,
        TerrainClass.Wetlands => 3.5,
        TerrainClass.Taiga => 3.0,
        TerrainClass.Hills => 2.2,
        TerrainClass.Forest => 1.9,
        TerrainClass.Drylands => 1.7,
        TerrainClass.Steppe => 1.2,   // poor, but nothing carries a people further
        TerrainClass.Beach => 0.8,    // a coast is a road when roads are bad
        TerrainClass.Plains => 1.0,
        TerrainClass.Farmlands => 1.0,
        TerrainClass.Floodplains => 0.9,
        _ => 1.5,
    };

    /// <summary>
    /// Traditions that a culture on this ground would plausibly hold. Every key is filtered against
    /// what the install actually has before use, so a missing DLC costs variety and nothing else.
    /// </summary>
    private static readonly Dictionary<TerrainClass, string[]> TerrainTraditions = new()
    {
        [TerrainClass.Mountains] =
            ["tradition_mountain_homes", "tradition_mountaineers", "tradition_highland_warriors",
             "tradition_sacred_mountains", "tradition_ancient_miners", "tradition_mountain_herding"],
        [TerrainClass.DesertMountains] =
            ["tradition_mountain_homes", "tradition_warriors_of_the_dry", "tradition_ancient_miners",
             "tradition_upland_skirmishing"],
        [TerrainClass.Hills] =
            ["tradition_hill_dwellers", "tradition_upland_skirmishing", "tradition_mountaineers"],
        [TerrainClass.Desert] =
            ["tradition_desert_nomads", "tradition_dryland_dwellers", "tradition_caravaneers",
             "tradition_warriors_of_the_dry", "tradition_saharan_nomads", "tradition_hidden_cities"],
        [TerrainClass.Drylands] =
            ["tradition_dryland_dwellers", "tradition_caravaneers", "tradition_pastoralists"],
        [TerrainClass.Steppe] =
            ["tradition_horse_breeder", "tradition_hit_and_run", "tradition_pastoralists",
             "tradition_mobile_guards", "tradition_hussar"],
        [TerrainClass.Forest] =
            ["tradition_forest_fighters", "tradition_forest_folk", "tradition_forest_wardens",
             "tradition_hunters", "tradition_sacred_groves"],
        [TerrainClass.Taiga] =
            ["tradition_winter_warriors", "tradition_hunters", "tradition_forest_folk",
             "tradition_sacred_groves"],
        [TerrainClass.Jungle] =
            ["tradition_jungle_dwellers", "tradition_jungle_warriors", "tradition_bush_hunting",
             "tradition_medicinal_plants"],
        [TerrainClass.Wetlands] =
            ["tradition_wetlanders", "tradition_polders", "tradition_fishermen"],
        [TerrainClass.Arctic] =
            ["tradition_winter_warriors", "tradition_hunters", "tradition_stalwart_defenders"],
        [TerrainClass.Beach] =
            ["tradition_seafaring", "tradition_maritime_mercantilism", "tradition_fishermen",
             "tradition_practiced_pirates"],
        [TerrainClass.Farmlands] =
            ["tradition_agrarian", "tradition_collective_lands", "tradition_gardening",
             "tradition_hard_working"],
        [TerrainClass.Floodplains] =
            ["tradition_agrarian", "tradition_fishermen", "tradition_gardening"],
        [TerrainClass.Plains] =
            ["tradition_agrarian", "tradition_hard_working", "tradition_collective_lands",
             "tradition_horse_breeder"],
    };

    /// <summary>Traditions a settled, wealthy culture picks up regardless of its ground.</summary>
    private static readonly string[] ProsperityTraditions =
    [
        "tradition_city_keepers", "tradition_artisans", "tradition_metal_craftsmanship",
        "tradition_philosopher_culture", "tradition_music_theory", "tradition_poetry",
        "tradition_language_scholars", "tradition_castle_keepers", "tradition_culinary_art",
    ];

    public static CultureMap Build(List<Title> empires, ProvinceMap provinces, int[] order,
        int landCount, TerrainClass[] provinceTerrain, Dictionary<Title, int> development,
        VanillaVocabulary vocab, MapConfig cfg, Rng rng, AzgaarImport? azgaar = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        var graph = BuildCountyGraph(counties, provinces, order, landCount, provinceTerrain,
            cfg.CultureTerrainWeight);

        // An export decides its own peoples, exactly as it decides its own borders.
        //
        // The density knobs below are how a *generated* world is given a plausible number of
        // languages; they are the wrong authority over a map somebody drew, and letting them keep it
        // is why a twelve-culture export came out with forty-seven cultures whose names it had never
        // heard of. Where the export has cultures, they are the cultures.
        if (azgaar is not null
            && ImportedCultures(counties, graph, provinceTerrain, development, vocab, cfg, rng, azgaar)
               is { } importedMap)
        {
            Report(importedMap.Heritages, importedMap.Cultures, counties.Count, sw.ElapsedMilliseconds);
            return importedMap;
        }

        var allowedTraditions = AllowedTraditions(vocab, cfg);

        int cultureTarget = Math.Max(1, (int)Math.Round(counties.Count / cfg.CountiesPerCulture));
        int heritageTarget = Math.Max(1, (int)Math.Round(cultureTarget / cfg.CulturesPerHeritage));

        // When race follows heritage, the heritage count is also the race count, and the density
        // knobs above have no idea about that — a small map can ask for two heritages and then have
        // nowhere to put the other six races. Raised to the guarantee, but never past the county
        // count, since a heritage with no counties is not a people.
        if (cfg.EnableFantasyEthnicities
            && cfg.RaceMode != MapConfig.FantasyRaceMode.HumanOnly
            && cfg.TieRaceToHeritage)
        {
            heritageTarget = Math.Max(heritageTarget, Math.Min(cfg.GuaranteedRaceCount, counties.Count));
        }

        var all = Enumerable.Range(0, counties.Count).ToList();
        var heritageOf = RegionGrowth.Partition(graph, all, heritageTarget, rng, out _);

        var heritages = new List<Heritage>();
        var cultures = new List<Culture>();
        var byCounty = new Dictionary<Title, Culture>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Where each heritage name was first planted, for the compass qualifier below.
        var firstClaim = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);

        var eligibleLooks = FilterLooks(vocab.Looks, cfg.CultureAestheticsTheme);
        var lookPool = eligibleLooks.Count > 0 ? eligibleLooks : vocab.Looks;

        for (int h = 0; h < heritageTarget; h++)
        {
            var members = all.Where(i => heritageOf[i] == h).ToList();
            if (members.Count == 0) continue;

            // Which of the export's cultures holds most of this heritage's ground, and therefore
            // whose name base its language should be built from. None on a generated map, and none
            // where our region growth landed a heritage on water or on unclaimed ground.
            var imported = azgaar?.Across(members.Select(i => counties[i]), b => b.Culture)
                           ?? AzgaarShare.None;
            var importedNames = imported.Exists ? azgaar!.NamesForCulture(imported.Id) : null;

            string languageKey = $"language_gen_{heritages.Count}";

            // The language's own name is generated from the corpus rather than taken from it. The
            // corpus is called things like "Arabic" and "Nordic" — real-world labels with no place
            // on a fantasy map's language list — and the heritage claims the export's culture name
            // just below, which the two are meant to differ from anyway.
            // Guarantee the first generated heritage gets the English-like language
            var language = importedNames is not null
                ? Language.FromNameBase(languageKey, importedNames, rng)
                : heritages.Count == 0
                    ? Language.CreateAnglic(languageKey, rng)
                    : Language.Create(languageKey, rng); usedNames.Add(language.Name);

            // A separate word from the language's own name, because they are separate things —
            // vanilla pairs the North Germanic heritage with the Norse language, not with the
            // North Germanic language, and naming both the same reads as a bug.
            string baseName =
                imported.Exists && azgaar!.World.Culture(imported.Id)?.Name is { Length: > 0 } n
                    && AzgaarNaming.StripParenthetical(AzgaarNaming.StripArticle(n)) is { Length: > 0 } stripped
                    ? stripped
                    : language.Word(rng, 2, 3);

            // Centre of the heritage's ground, for the qualifier a name collision resolves to.
            double cx = 0, cy = 0;
            foreach (int i in members) { cx += graph.Position[i].X; cy += graph.Position[i].Y; }
            cx /= members.Count; cy /= members.Count;

            var heritage = new Heritage
            {
                Key = $"heritage_gen_{heritages.Count}",
                Name = RegionalName(baseName, (cx, cy), firstClaim, usedNames),
                Language = language,
                Look = rng.Pick(lookPool),
                LanguageColor = vocab.LanguageColors.Count > 0 ? rng.Pick(vocab.LanguageColors) : null,
            };
            heritage.ImportedArchetype = imported.Exists
                && azgaar!.World.Culture(imported.Id)?.Name is { Length: > 0 } tagged
                    ? AzgaarNaming.ParseRace(tagged)
                    : null;

            heritages.Add(heritage);

            int within = Math.Max(1, (int)Math.Round(members.Count / cfg.CountiesPerCulture));
            var cultureOf = RegionGrowth.Partition(graph, members, within, rng, out _);

            for (int c = 0; c < within; c++)
            {
                var owned = members.Where(i => cultureOf[i] == c).Select(i => counties[i]).ToList();
                if (owned.Count == 0) continue;

                var culture = Create(heritage, owned, provinceTerrain, development, vocab,
                    allowedTraditions, usedNames, cultures.Count, rng);

                heritage.Cultures.Add(culture);
                cultures.Add(culture);
                culture.Counties.AddRange(owned);
                foreach (var county in owned) byCounty[county] = culture;
            }
        }

        Report(heritages, cultures, counties.Count, sw.ElapsedMilliseconds);
        return new CultureMap { Heritages = heritages, Cultures = cultures, ByCounty = byCounty };
    }

    /// <summary>
    /// The export's own peoples, as heritages and cultures, or null when it has none to give.
    ///
    /// One CK3 culture per live Azgaar culture, over the counties that culture actually holds.
    ///
    /// The grouping above them is <see cref="AzgaarFamilies"/>'s: Azgaar's <c>origins</c> ancestry
    /// where the export drew one — a culture whose origin is another live culture joins that one's
    /// heritage — and, where every culture descends from Wildlands and the ancestry therefore says
    /// nothing, shared name corpus and then geography. On the Lumbaris export the ancestry alone
    /// turns twelve cultures into seven families; on Fleunland, whose origins are the generator's
    /// degenerate default, the corpus puts both Elven cultures and both Dwarven ones together and
    /// geography joins the two human peoples. Either way it is the relationship CK3 means by
    /// heritage.
    ///
    /// Deliberately ignores <see cref="MapConfig.CountiesPerCulture"/> and
    /// <see cref="MapConfig.CulturesPerHeritage"/>. Those exist to give an invented world a plausible
    /// density of peoples; against an export they are a second opinion nobody asked for, and honouring
    /// them is what produced thirty-six cultures the export had never heard of alongside the eleven
    /// it had.
    /// </summary>
    private static CultureMap? ImportedCultures(List<Title> counties, RegionGrowth.Graph graph,
        TerrainClass[] provinceTerrain, Dictionary<Title, int> development,
        VanillaVocabulary vocab, MapConfig cfg, Rng rng, AzgaarImport azgaar)
    {
        var live = azgaar.World.RealCultures.ToDictionary(c => c.I);
        if (live.Count == 0) return null;

        var allowedTraditions = AllowedTraditions(vocab, cfg);

        // --- Which people holds each county -------------------------------------------------------
        var held = new Dictionary<int, List<Title>>();
        var homeless = new List<Title>();

        for (int i = 0; i < counties.Count; i++)
        {
            int id = azgaar.For(counties[i])?.Culture.Id ?? 0;

            // Culture 0 is Wildlands — Azgaar's word for ground no people has claimed, not a people.
            // Left in a bucket of its own and handed to the neighbours below, because a "Wildlands"
            // culture with traditions and a name list is a thing the export never described.
            if (id <= 0 || !live.ContainsKey(id)) { homeless.Add(counties[i]); continue; }

            if (!held.TryGetValue(id, out var list)) held[id] = list = [];
            list.Add(counties[i]);
        }

        if (held.Count == 0) return null;

        Spread(counties, graph, held, homeless);

        var index = new Dictionary<Title, int>();
        for (int i = 0; i < counties.Count; i++) index[counties[i]] = i;

        // --- Families -----------------------------------------------------------------------------
        //
        // Ancestry where the export drew one, shared name corpus and then geography where it did not.
        // See AzgaarFamilies for why the second and third exist at all: Azgaar's generator writes
        // every culture as descended from Wildlands, so the ancestry it ships is a statement that no
        // culture is related to any other, which is not what its author meant and is not a world CK3
        // can do anything with.
        var (family, basis) = AzgaarFamilies.Group(live, held, graph, index, cfg.CulturesPerHeritage);

        var heritages = new List<Heritage>();
        var cultures = new List<Culture>();
        var byCounty = new Dictionary<Title, Culture>();

        // Culture names are the export's, verbatim, and unique within it — so the pool they are
        // checked against starts empty and nothing here can rename one. Heritages are checked against
        // a pool of their own: a family and the culture it is named after share a word on purpose,
        // and merging the two namespaces turns "Ignisari" into "North Ignisari" to avoid a clash
        // that only existed inside this method.
        var usedCultureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedHeritageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstClaim = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);

        var eligibleLooks = FilterLooks(vocab.Looks, cfg.CultureAestheticsTheme);
        var lookPool = eligibleLooks.Count > 0 ? eligibleLooks : vocab.Looks;

        foreach (var (root, members) in family.GroupBy(kv => kv.Value, kv => kv.Key)
                                              .ToDictionary(g => g.Key, g => g.OrderBy(i => i).ToList())
                                              .OrderBy(kv => kv.Key))
        {
            var founder = live[root];
            string languageKey = $"language_gen_{heritages.Count}";

            // From the founder's name base, so every culture in the family names its people from one
            // corpus — which is what makes them read as related rather than as neighbours.
            var language = azgaar.NamesForBase(founder.Base) is { } corpus
                ? Language.FromNameBase(languageKey, corpus, rng)
                : heritages.Count == 0
                    ? Language.CreateAnglic(languageKey, rng)
                    : Language.Create(languageKey, rng);

            double cx = 0, cy = 0;
            int counted = 0;
            foreach (int id in members)
                foreach (var county in held[id])
                    if (index.TryGetValue(county, out int at))
                    {
                        cx += graph.Position[at].X;
                        cy += graph.Position[at].Y;
                        counted++;
                    }

            if (counted > 0) { cx /= counted; cy /= counted; }

            string family_ = AzgaarNaming.StripParenthetical(AzgaarNaming.StripArticle(founder.Name));
            if (family_.Length == 0) family_ = language.Name;

            var heritage = new Heritage
            {
                Key = $"heritage_gen_{heritages.Count}",
                Name = RegionalName(family_, (cx, cy), firstClaim, usedHeritageNames),
                Language = language,
                Look = rng.Pick(lookPool),
                LanguageColor = vocab.LanguageColors.Count > 0 ? rng.Pick(vocab.LanguageColors) : null,
                ImportedArchetype = AzgaarNaming.ParseRace(founder.Name),
            };

            heritages.Add(heritage);

            foreach (int id in members)
            {
                var owned = held[id];
                if (owned.Count == 0) continue;

                var source = live[id];
                var culture = Create(heritage, owned, provinceTerrain, development, vocab,
                                     allowedTraditions, usedCultureNames, cultures.Count, rng);

                // The export's word for this people, and its own colour, over the generated ones.
                // Written after Create rather than threaded through it so the character the ground
                // gives a culture — its ethos, its traditions, its terrain — is still measured the
                // same way for an imported culture as for an invented one.
                string name = AzgaarNaming.StripParenthetical(AzgaarNaming.StripArticle(source.Name));
                if (name.Length > 0)
                {
                    usedCultureNames.Remove(culture.Name);
                    culture.Name = Unique(name, usedCultureNames);
                }

                if (AzgaarColors.TryParseColor(source.Color, out var rgb)) culture.Color = rgb;
                culture.ImportedArchetype = AzgaarNaming.ParseRace(source.Name) ?? heritage.ImportedArchetype;

                heritage.Cultures.Add(culture);
                cultures.Add(culture);
                culture.Counties.AddRange(owned);
                foreach (var county in owned) byCounty[county] = culture;
            }
        }

        if (heritages.Count == 0) return null;

        Console.WriteLine($"    cultures follow the export: {cultures.Count} of its peoples in " +
                          $"{heritages.Count} families from {basis}" +
                          (homeless.Count > 0 ? $", {homeless.Count} wildlands counties joined a neighbour" : ""));

        return new CultureMap { Heritages = heritages, Cultures = cultures, ByCounty = byCounty };
    }

    /// <summary>
    /// Hands every county the export left on Wildlands to whichever neighbouring people surrounds it.
    ///
    /// Grown outward one ring at a time rather than assigned by nearest centre, so a pocket of
    /// unclaimed ground is split along the border already running through it instead of all going to
    /// whichever culture happens to have the closest midpoint. Anything with no settled neighbour at
    /// all — an island nobody reached — falls back to the largest culture, since leaving a county
    /// with no culture at all is not something CK3 will load.
    /// </summary>
    private static void Spread(List<Title> counties, RegionGrowth.Graph graph,
        Dictionary<int, List<Title>> held, List<Title> homeless)
    {
        if (homeless.Count == 0) return;

        var index = new Dictionary<Title, int>();
        for (int i = 0; i < counties.Count; i++) index[counties[i]] = i;

        var owner = new int[counties.Count];
        foreach (var (id, members) in held)
            foreach (var county in members)
                if (index.TryGetValue(county, out int at)) owner[at] = id;

        var pending = homeless.Where(index.ContainsKey).Select(c => index[c]).ToHashSet();

        while (pending.Count > 0)
        {
            // Every claim in a round is decided against the same board, so the result does not depend
            // on which county the enumeration reached first.
            var claimed = new Dictionary<int, int>();

            foreach (int at in pending.OrderBy(i => i))
            {
                var votes = new Dictionary<int, int>();
                foreach (int near in graph.Neighbours[at])
                    if (owner[near] > 0) votes[owner[near]] = votes.GetValueOrDefault(owner[near]) + 1;

                if (votes.Count == 0) continue;
                claimed[at] = votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key).First().Key;
            }

            if (claimed.Count == 0) break;

            foreach (var (at, id) in claimed)
            {
                owner[at] = id;
                held[id].Add(counties[at]);
                pending.Remove(at);
            }
        }

        if (pending.Count == 0) return;

        int biggest = held.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key).First().Key;
        foreach (int at in pending.OrderBy(i => i)) held[biggest].Add(counties[at]);
    }

    /// <summary>
    /// Counties as nodes, linked where their provinces touch.
    ///
    /// Impassable provinces carry no barony and so belong to no county, which means they silently
    /// drop out of the graph rather than bridging across it. That is the behaviour we want and it
    /// is worth stating: a mountain wall the game routes armies around also stops a language, so
    /// culture borders land on it without anything here having to look for ridgelines.
    /// </summary>
    private static RegionGrowth.Graph BuildCountyGraph(List<Title> counties, ProvinceMap provinces,
        int[] order, int landCount, TerrainClass[] provinceTerrain, double terrainWeight)
    {
        var countyIndex = new Dictionary<Title, int>();
        for (int i = 0; i < counties.Count; i++) countyIndex[counties[i]] = i;

        var countyOfProvince = new Dictionary<int, int>();
        foreach (var (county, index) in countyIndex)
            foreach (var barony in county.Children)
                if (barony.ProvinceId > 0) countyOfProvince[barony.ProvinceId] = index;

        // Province id back to the seed that made it, so a county can be given a position without
        // another pass over the raster.
        var seedOfProvince = new int[landCount + 1];
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= landCount) seedOfProvince[id] = label;
        }

        var neighbours = new List<int>[counties.Count];
        for (int i = 0; i < neighbours.Length; i++) neighbours[i] = [];

        var linked = new HashSet<(int, int)>();
        var provinceAdjacency = Titles.BuildAdjacency(provinces, landCount, order);

        foreach (var (province, others) in provinceAdjacency)
        {
            if (!countyOfProvince.TryGetValue(province, out int a)) continue;

            foreach (int other in others)
            {
                if (!countyOfProvince.TryGetValue(other, out int b) || a == b) continue;

                var pair = a < b ? (a, b) : (b, a);
                if (!linked.Add(pair)) continue;

                neighbours[a].Add(b);
                neighbours[b].Add(a);
            }
        }

        var cost = new double[counties.Count];
        var position = new (double X, double Y)[counties.Count];

        for (int i = 0; i < counties.Count; i++)
        {
            double total = 0, x = 0, y = 0;
            int counted = 0;

            foreach (var barony in counties[i].Children)
            {
                int id = barony.ProvinceId;
                if (id <= 0 || id >= provinceTerrain.Length) continue;

                total += Crossing(provinceTerrain[id]);
                var seed = provinces.Seeds[seedOfProvince[id]];
                x += seed.X;
                y += seed.Y;
                counted++;
            }

            double mean = counted == 0 ? 1.5 : total / counted;

            // Terrain resistance is interpolated against flat ground rather than used raw, so one
            // weight dials the whole map between "borders ignore terrain" and "borders are terrain".
            cost[i] = 1.0 + (mean - 1.0) * terrainWeight;
            if (cost[i] < 0.1) cost[i] = 0.1;

            position[i] = counted == 0 ? (0, 0) : (x / counted, y / counted);
        }

        return new RegionGrowth.Graph { Neighbours = neighbours, EnterCost = cost, Position = position };
    }

    private static Culture Create(Heritage heritage, List<Title> counties,
        TerrainClass[] provinceTerrain, Dictionary<Title, int> development,
        VanillaVocabulary vocab, List<string> allowedTraditions, HashSet<string> usedNames,
        int index, Rng rng)
    {
        var language = heritage.Language;

        // What this culture actually lives on, and how well it lives, decide its character.
        var terrainCounts = new Dictionary<TerrainClass, int>();
        double developmentTotal = 0;

        foreach (var county in counties)
        {
            developmentTotal += development.GetValueOrDefault(county);
            foreach (var barony in county.Children)
            {
                int id = barony.ProvinceId;
                if (id <= 0 || id >= provinceTerrain.Length) continue;
                var t = provinceTerrain[id];
                terrainCounts[t] = terrainCounts.GetValueOrDefault(t) + 1;
            }
        }

        double meanDevelopment = counties.Count == 0 ? 0 : developmentTotal / counties.Count;
        var dominant = terrainCounts.Count == 0
            ? TerrainClass.Plains
            : terrainCounts.OrderByDescending(kv => kv.Value)
                           .ThenBy(kv => (int)kv.Key).First().Key;

        string name = Unique(language.Word(rng, 2, 3), usedNames);

        return new Culture
        {
            Key = $"gen_culture_{index}",
            Name = name,
            Heritage = heritage,
            MeanDevelopment = meanDevelopment,
            Color = ((byte)rng.Int(30, 225), (byte)rng.Int(30, 225), (byte)rng.Int(30, 225)),
            Ethos = PickEthos(dominant, meanDevelopment, vocab, rng),
            MartialCustom = PickMartialCustom(vocab, rng),
            HeadDetermination = PickHeadDetermination(dominant, vocab, rng),
            Traditions = PickTraditions(terrainCounts, meanDevelopment, allowedTraditions, rng),
            CoaGfx = heritage.Look.CoaGfx,
            BuildingGfx = heritage.Look.BuildingGfx,
            ClothingGfx = heritage.Look.ClothingGfx,
            UnitGfx = heritage.Look.UnitGfx,
            MaleNames = Names(language, rng, 60, male: true, usedNames: null),
            FemaleNames = Names(language, rng, 45, male: false, usedNames: null),
            DynastyNames = Names(language, rng, 40, male: true, usedNames: null),
            PatronymSuffixMale = Particle(language, rng),
            PatronymSuffixFemale = Particle(language, rng),
            LocationPrefix = Particle(language, rng),
            AlwaysUsePatronym = rng.Chance(0.35),
        };
    }

    /// <summary>The culture wilderness counties carry. Fixed, so history and script can name it.</summary>
    public const string UnsettledKey = "gen_culture_unsettled";

    /// <summary>
    /// The culture of land nobody lives on.
    ///
    /// It has to be a real culture and not a null, because CK3 requires one on every land province
    /// and because the wilderness dummy that holds these counties is a character who needs one. But
    /// it must not read as a *people*: the whole point of an empty county is that there is nobody
    /// there, and a generated name like "Braemoth" would make the map claim the opposite — a
    /// culture that happens to hold the mountains rather than mountains that hold nobody. Hence the
    /// fixed name.
    ///
    /// Built off an existing heritage rather than its own pillar. A dedicated heritage would show
    /// up in the culture tree as a branch of the world's peoples, which is exactly the impression
    /// worth avoiding, and it would need its own language and loc for a culture nobody plays.
    ///
    /// Scored as arctic on purpose. It never influences any county's terrain, but the ethos and
    /// traditions it picks are what a player sees if they open the culture screen, and the harsh
    /// end of the table is the honest answer for ground that defeated everyone who tried.
    /// </summary>
    public static Culture CreateUnsettled(Heritage heritage, VanillaVocabulary vocab,
        MapConfig cfg, Rng rng)
    {
        var language = heritage.Language;
        var terrain = new Dictionary<TerrainClass, int> { [TerrainClass.Arctic] = 1 };

        return new Culture
        {
            Key = UnsettledKey,
            Name = "Unsettled",
            Heritage = heritage,
            MeanDevelopment = 0,
            Color = ((byte)108, (byte)104, (byte)96),
            Ethos = PickEthos(TerrainClass.Arctic, 0, vocab, rng),
            MartialCustom = PickMartialCustom(vocab, rng),
            HeadDetermination = PickHeadDetermination(TerrainClass.Arctic, vocab, rng),
            Traditions = PickTraditions(terrain, 0, AllowedTraditions(vocab, cfg), rng),
            CoaGfx = heritage.Look.CoaGfx,
            BuildingGfx = heritage.Look.BuildingGfx,
            ClothingGfx = heritage.Look.ClothingGfx,
            UnitGfx = heritage.Look.UnitGfx,
            // Short lists rather than none. Nobody is born into this culture, but the dummy holder
            // belongs to it and CK3 will read a name for him; an empty list is a crash waiting for
            // the one character that uses it.
            MaleNames = Names(language, rng, 8, male: true, usedNames: null),
            FemaleNames = Names(language, rng, 8, male: false, usedNames: null),
            DynastyNames = Names(language, rng, 6, male: true, usedNames: null),

            PatronymSuffixMale = Particle(language, rng),
            PatronymSuffixFemale = Particle(language, rng),
            LocationPrefix = Particle(language, rng),
            AlwaysUsePatronym = false,
        };
    }

    private static string PickEthos(TerrainClass dominant, double development,
        VanillaVocabulary vocab, Rng rng)
    {
        // Weighted preferences, then filtered to what exists. Hard country breeds a warlike or
        // enduring people; wealth breeds courts and clerks.
        List<string> preferred = dominant switch
        {
            TerrainClass.Mountains or TerrainClass.DesertMountains or TerrainClass.Arctic
                => ["ethos_bellicose", "ethos_stoic", "ethos_communal"],
            TerrainClass.Desert or TerrainClass.Drylands or TerrainClass.Steppe
                => ["ethos_bellicose", "ethos_communal", "ethos_spiritual"],
            TerrainClass.Jungle or TerrainClass.Wetlands or TerrainClass.Taiga
                => ["ethos_spiritual", "ethos_stoic", "ethos_egalitarian"],
            _ => development >= 12
                ? ["ethos_courtly", "ethos_bureaucratic", "ethos_egalitarian"]
                : ["ethos_communal", "ethos_egalitarian", "ethos_spiritual", "ethos_bellicose"],
        };

        return Choose(preferred, vocab.Ethos, rng);
    }

    private static string PickMartialCustom(VanillaVocabulary vocab, Rng rng)
    {
        double roll = rng.NextDouble();
        List<string> preferred = roll switch
        {
            < 0.70 => ["martial_custom_male_only"],
            < 0.93 => ["martial_custom_equal"],
            _ => ["martial_custom_female_only"],
        };

        return Choose(preferred, vocab.MartialCustoms, rng);
    }

    private static string PickHeadDetermination(TerrainClass dominant, VanillaVocabulary vocab, Rng rng)
    {
        // Herd determination is how CK3 models a people whose wealth walks with them.
        bool pastoral = dominant is TerrainClass.Steppe or TerrainClass.Desert or TerrainClass.Drylands;
        List<string> preferred = pastoral && rng.Chance(0.6)
            ? ["head_determination_herd", "head_determination_domain"]
            : ["head_determination_domain"];

        return Choose(preferred, vocab.HeadDeterminations, rng);
    }

    /// <summary>
    /// The traditions a generated culture may be given, which is not quite every tradition the
    /// install declares.
    ///
    /// Traditions that hand out a *vanilla cultural regiment* are removed when the world is
    /// writing its own men-at-arms. They are the one place vanilla's vocabulary shows through as a
    /// borrowing: a tradition gives a culture an ethos and a modifier, which are abstract enough
    /// to belong to anyone, and it also gives them Danish Huscarls, which are not. The terrain
    /// tables above reach for several by name — hussars for steppe, bush hunting for jungle,
    /// upland skirmishing for hills — so the leak was frequent rather than incidental.
    ///
    /// Only safe because the generated roster replaces what it removes: every heritage gets a
    /// regiment of its own and martial cultures earn a second. With the roster switched off the
    /// filter is switched off with it, or generated cultures would simply be poorer. See
    /// <see cref="Retinues.ReplacesVanillaRosters"/>, which is also the condition
    /// <see cref="Emit.CultureWriter"/> closes the *innovation* route on — the two have to move
    /// together or a culture simply reaches vanilla's regiments by the other road.
    /// </summary>
    private static List<string> AllowedTraditions(VanillaVocabulary vocab, MapConfig cfg)
        => Retinues.ReplacesVanillaRosters(vocab, cfg)
            ? [.. vocab.Traditions.Where(t => !vocab.TraditionsUnlockingMaa.Contains(t))]
            : vocab.Traditions;

    /// <summary>
    /// Three to five traditions, drawn from what the culture's ground suggests and topped up from
    /// <paramref name="allowed"/> so no two cultures on the same terrain are identical.
    /// </summary>
    private static List<string> PickTraditions(Dictionary<TerrainClass, int> terrainCounts,
        double development, List<string> allowed, Rng rng)
    {
        var available = allowed.ToHashSet(StringComparer.Ordinal);
        var candidates = new List<string>();

        // Weighted by how much of the culture actually sits on each terrain, so a mostly-coastal
        // people is likely to be seafaring and a merely-partly-coastal one only sometimes is.
        int total = terrainCounts.Values.Sum();
        foreach (var (terrain, count) in terrainCounts)
        {
            if (!TerrainTraditions.TryGetValue(terrain, out var themed)) continue;

            int weight = total == 0 ? 1 : 1 + (int)Math.Round(6.0 * count / total);
            for (int i = 0; i < weight; i++) candidates.AddRange(themed);
        }

        if (development >= 10) candidates.AddRange(ProsperityTraditions);

        var chosen = new List<string>();
        int target = rng.Int(3, 5);

        for (int attempt = 0; attempt < 60 && chosen.Count < target; attempt++)
        {
            string pick = candidates.Count > 0 && rng.Chance(0.75)
                ? rng.Pick(candidates)
                : rng.Pick(allowed);

            if (available.Contains(pick) && !chosen.Contains(pick)) chosen.Add(pick);
        }

        return chosen;
    }

    /// <summary>First preference that the install actually has, else anything valid.</summary>
    private static string Choose(List<string> preferred, List<string> available, Rng rng)
    {
        var usable = preferred.Where(available.Contains).ToList();
        return usable.Count > 0 ? rng.Pick(usable) : rng.Pick(available);
    }

    private static List<string> Names(Language language, Rng rng, int count, bool male,
        HashSet<string>? usedNames)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        for (int attempt = 0; attempt < count * 8 && result.Count < count; attempt++)
        {
            string name = male ? language.MaleName(rng) : language.FemaleName(rng);

            // Two-letter names read as typos and long ones crowd every interface they appear in.
            if (name.Length is < 4 or > 11) continue;
            if (usedNames is not null && !usedNames.Add(name)) continue;
            result.Add(name);
        }

        return [.. result];
    }

    /// <summary>A short grammatical word — a patronymic ending, or a nobiliary particle.</summary>
    private static string Particle(Language language, Rng rng)
    {
        string word = language.Word(rng, 1, 1).ToLowerInvariant();
        return word.Length > 4 ? word[..4] : word;
    }

    /// <summary>
    /// Keeps every displayed name distinct across the world. Two cultures called the same thing is
    /// the single most obvious generation artefact there is, and the phonology will collide
    /// eventually because a language only has so many short words in it.
    /// </summary>
    /// <summary>
    /// Claims a heritage name, resolving a collision the way history does rather than the way a
    /// symbol table does.
    ///
    /// The collision is ordinary on an imported map: heritage grouping is geographic, so one
    /// export culture that dominates three regions hands the same name to three heritages, and
    /// Unique()'s answer — "Yotunn2", "Yotunn3" — is the generator showing through. But three
    /// regions of one people is exactly what "East Francia" and "West Francia" were coined for, so
    /// a later claim takes the compass direction from the name's first ground to its own: Yotunn,
    /// then East Yotunn, then whichever of the third's directions is still free. The first keeps
    /// the bare name, as the Franks kept Francia. Numbering survives only as the last resort for a
    /// pathological map where every direction word is spoken for.
    /// </summary>
    private static string RegionalName(string baseName, (double X, double Y) centre,
        Dictionary<string, (double X, double Y)> firstClaim, HashSet<string> used)
    {
        if (used.Add(baseName))
        {
            firstClaim[baseName] = centre;
            return baseName;
        }

        var origin = firstClaim.TryGetValue(baseName, out var o) ? o : centre;
        double dx = centre.X - origin.X;
        double dy = centre.Y - origin.Y;

        // The raster's y grows downward, so positive dy is south. The dominant axis names first;
        // the other axis is the fallback when two later regions lie the same way.
        string alongX = dx >= 0 ? "East" : "West";
        string alongY = dy >= 0 ? "South" : "North";
        string primary = Math.Abs(dx) >= Math.Abs(dy) ? alongX : alongY;
        string secondary = Math.Abs(dx) >= Math.Abs(dy) ? alongY : alongX;

        if (used.Add($"{primary} {baseName}")) return $"{primary} {baseName}";
        if (used.Add($"{secondary} {baseName}")) return $"{secondary} {baseName}";

        // Both cardinals spoken for: compound them, north-south first as compasses are read.
        string compound = $"{alongY}{alongX.ToLowerInvariant()} {baseName}";
        if (used.Add(compound)) return compound;

        return Unique(baseName, used);
    }

    /// <summary>
    /// Filters vanilla culture aesthetic looks against the user's selected theme.
    /// Checks both the source culture name and the gfx definitions.
    /// </summary>
    public static List<VanillaVocabulary.Look> FilterLooks(
        List<VanillaVocabulary.Look> looks, MapConfig.CultureLookTheme theme)
    {
        if (theme == MapConfig.CultureLookTheme.VariedGlobal) return looks;

        return looks.Where(l => MatchesTheme(l, theme)).ToList();

        static bool MatchesTheme(VanillaVocabulary.Look l, MapConfig.CultureLookTheme t)
        {
            string src = l.SourceCulture.ToLowerInvariant();
            string cloth = l.ClothingGfx.ToLowerInvariant();
            string unit = l.UnitGfx.ToLowerInvariant();
            string bld = l.BuildingGfx.ToLowerInvariant();

            return t switch
            {
                MapConfig.CultureLookTheme.NorthernNorse =>
                    cloth.Contains("norse") || cloth.Contains("northern") || src.Contains("norse")
                    || src.Contains("swedish") || src.Contains("norwegian") || src.Contains("danish"),

                MapConfig.CultureLookTheme.WesternEuropean =>
                    cloth.Contains("western") || cloth.Contains("frankish") || cloth.Contains("iberian")
                    || cloth.Contains("english") || cloth.Contains("german") || cloth.Contains("french")
                    || unit.Contains("western") || bld.Contains("western"),

                MapConfig.CultureLookTheme.ByzantineGreek =>
                    cloth.Contains("byzantine") || cloth.Contains("greek") || cloth.Contains("roman")
                    || unit.Contains("byzantine") || bld.Contains("byzantine") || src.Contains("greek"),

                MapConfig.CultureLookTheme.MiddleEasternMena =>
                    cloth.Contains("mena") || cloth.Contains("arabic") || cloth.Contains("persian")
                    || cloth.Contains("bedouin") || cloth.Contains("berber") || unit.Contains("mena")
                    || bld.Contains("mena"),

                MapConfig.CultureLookTheme.SteppeNomadic =>
                    cloth.Contains("steppe") || cloth.Contains("mongol") || cloth.Contains("turkic")
                    || cloth.Contains("cuman") || unit.Contains("steppe") || bld.Contains("yurt"),

                MapConfig.CultureLookTheme.SubSaharanAfrican =>
                    cloth.Contains("african") || cloth.Contains("ethiopian") || cloth.Contains("nubian")
                    || cloth.Contains("sahelian") || unit.Contains("african") || bld.Contains("african"),

                MapConfig.CultureLookTheme.IndianEastAsian =>
                    cloth.Contains("indian") || cloth.Contains("dravidian") || cloth.Contains("chinese")
                    || cloth.Contains("han") || cloth.Contains("tibetan") || unit.Contains("indian")
                    || bld.Contains("indian") || bld.Contains("asian"),

                _ => true
            };
        }
    }

    private static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name)) return name;

        for (int suffix = 2; suffix < 100; suffix++)
        {
            string candidate = $"{name}{suffix}";
            if (used.Add(candidate)) return candidate;
        }

        return name;
    }

    private static void Report(List<Heritage> heritages, List<Culture> cultures, int counties,
        long elapsedMs)
    {
        var sizes = cultures.Select(c => c.Counties.Count).OrderBy(n => n).ToList();
        if (sizes.Count == 0) return;

        Console.WriteLine($"  cultures: {cultures.Count} in {heritages.Count} heritages over " +
                          $"{counties} counties — smallest {sizes[0]}, median {sizes[sizes.Count / 2]}, " +
                          $"largest {sizes[^1]} counties ({elapsedMs} ms)");
    }
}
