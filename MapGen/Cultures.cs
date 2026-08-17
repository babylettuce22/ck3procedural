using Ck3MapGen.Config;
using Ck3MapGen.Core;

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

    /// <summary>Frozen: it owns the language every name here is drawn from.</summary>
    public required Heritage Heritage { get; init; }
    public Language Language => Heritage.Language;

    public required (byte R, byte G, byte B) Color { get; set; }
    public required string Ethos { get; set; }
    public required string MartialCustom { get; set; }
    public required string HeadDetermination { get; set; }
    public required List<string> Traditions { get; set; }

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
        VanillaVocabulary vocab, MapConfig cfg, Rng rng)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        var graph = BuildCountyGraph(counties, provinces, order, landCount, provinceTerrain,
            cfg.CultureTerrainWeight);

        int cultureTarget = Math.Max(1, (int)Math.Round(counties.Count / cfg.CountiesPerCulture));
        int heritageTarget = Math.Max(1, (int)Math.Round(cultureTarget / cfg.CulturesPerHeritage));

        var all = Enumerable.Range(0, counties.Count).ToList();
        var heritageOf = RegionGrowth.Partition(graph, all, heritageTarget, rng, out _);

        var heritages = new List<Heritage>();
        var cultures = new List<Culture>();
        var byCounty = new Dictionary<Title, Culture>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int h = 0; h < heritageTarget; h++)
        {
            var members = all.Where(i => heritageOf[i] == h).ToList();
            if (members.Count == 0) continue;

            // Guarantee the first generated heritage gets the English-like language
            var language = heritages.Count == 0
                ? Language.CreateAnglic($"language_gen_{heritages.Count}", rng)
                : Language.Create($"language_gen_{heritages.Count}", rng); usedNames.Add(language.Name);

            var heritage = new Heritage
            {
                Key = $"heritage_gen_{heritages.Count}",

                // A separate word from the language's own name, because they are separate things —
                // vanilla pairs the North Germanic heritage with the Norse language, not with the
                // North Germanic language, and naming both the same reads as a bug.
                Name = Unique(language.Word(rng, 2, 3), usedNames),
                Language = language,
                Look = rng.Pick(vocab.Looks),
                LanguageColor = vocab.LanguageColors.Count > 0 ? rng.Pick(vocab.LanguageColors) : null,
            };
            heritages.Add(heritage);

            int within = Math.Max(1, (int)Math.Round(members.Count / cfg.CountiesPerCulture));
            var cultureOf = RegionGrowth.Partition(graph, members, within, rng, out _);

            for (int c = 0; c < within; c++)
            {
                var owned = members.Where(i => cultureOf[i] == c).Select(i => counties[i]).ToList();
                if (owned.Count == 0) continue;

                var culture = Create(heritage, owned, provinceTerrain, development, vocab,
                    usedNames, cultures.Count, rng);

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
        VanillaVocabulary vocab, HashSet<string> usedNames, int index, Rng rng)
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
            Traditions = PickTraditions(terrainCounts, meanDevelopment, vocab, rng),
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
    public static Culture CreateUnsettled(Heritage heritage, VanillaVocabulary vocab, Rng rng)
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
            Traditions = PickTraditions(terrain, 0, vocab, rng),

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
    /// Three to five traditions, drawn from what the culture's ground suggests and topped up from
    /// the full list so no two cultures on the same terrain are identical.
    /// </summary>
    private static List<string> PickTraditions(Dictionary<TerrainClass, int> terrainCounts,
        double development, VanillaVocabulary vocab, Rng rng)
    {
        var available = vocab.Traditions.ToHashSet(StringComparer.Ordinal);
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
                : rng.Pick(vocab.Traditions);

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
