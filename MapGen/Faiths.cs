using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public sealed class HeadOfFaith
{
    public required string TitleKey { get; init; }
    public required string Name { get; init; }
    public required Title Seat { get; init; }
}

public sealed class Faith
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required Religion Religion { get; init; }
    public required (double R, double G, double B) Color { get; init; }
    public required string Icon { get; init; }

    public required List<string> Tenets { get; init; }

    public List<Title> Counties { get; } = [];
    public List<(string Key, Title County)> HolySites { get; } = [];

    public HeadOfFaith? Head { get; set; }

    public bool IsOrganized { get; set; } = true;
}

/// <summary>A generated religion: a liturgical language, a doctrine baseline, and its faiths.</summary>
public sealed class Religion
{
    public required string Key { get; init; }
    public required string Name { get; init; }

    public required Language Language { get; init; }
    public required string GraphicalFaith { get; init; }
    public required bool Monotheist { get; init; }

    public required Dictionary<string, string> Doctrines { get; init; }
    public required List<string> Virtues { get; init; }
    public required List<string> Sins { get; init; }

    public required List<(string Tag, string Value)> Localization { get; init; }
    public required Dictionary<string, string> LocalizationText { get; init; }

    public List<Faith> Faiths { get; } = [];
}

public sealed class FaithMap
{
    public required List<Religion> Religions { get; init; }
    public required List<Faith> Faiths { get; init; }
    public required Dictionary<Title, Faith> ByCounty { get; init; }

    public Faith For(Title title)
    {
        if (ByCounty.TryGetValue(title, out var direct)) return direct;

        for (var p = title.Parent; p is not null; p = p.Parent)
            if (ByCounty.TryGetValue(p, out var inherited)) return inherited;

        var votes = new Dictionary<Faith, int>();
        foreach (var county in Titles.Flatten([title]).Where(t => t.Tier == "c"))
            if (ByCounty.TryGetValue(county, out var f))
                votes[f] = votes.GetValueOrDefault(f) + 1;

        return votes.Count == 0
            ? Faiths[0]
            : votes.OrderByDescending(kv => kv.Value)
                   .ThenBy(kv => kv.Key.Key, StringComparer.Ordinal).First().Key;
    }
}

public static class Faiths
{
    private static readonly string[] FilledGroups =
    [
        "hostility_group", "doctrine_theism", "doctrine_head_of_faith", "doctrine_gender",
        "doctrine_pluralism", "doctrine_theocracy", "doctrine_marriage_type", "doctrine_divorce",
        "doctrine_bastardry", "doctrine_consanguinity", "doctrine_homosexuality",
        "doctrine_adultery_men", "doctrine_adultery_women", "doctrine_kinslaying",
        "doctrine_deviancy", "doctrine_witchcraft", "doctrine_clerical_function",
        "doctrine_clerical_gender", "doctrine_clerical_marriage", "doctrine_clerical_succession",
        "doctrine_pilgrimage", "doctrine_funeral", "doctrine_coronation",
    ];

    private static readonly Dictionary<string, string> ForcedDoctrines = new()
    {
        ["doctrine_head_of_faith"] = "doctrine_no_head",
        ["hostility_group"] = "pagan_hostility_doctrine",
    };

    public const string Family = "rf_pagan";

    public static FaithMap Build(List<Title> empires, ProvinceMap provinces, int[] order,
        int landCount, TerrainClass[] provinceTerrain, Dictionary<Title, int> development,
        GovernmentMap governments, VanillaVocabulary vocab, WildernessMap wilderness,
        MapConfig cfg, Rng rng)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        var graph = CountyGraph(counties, provinces, order, landCount, provinceTerrain,
            cfg.FaithTerrainWeight);

        int faithTarget = Math.Max(1, (int)Math.Round(counties.Count / cfg.CountiesPerFaith));
        int religionTarget = Math.Max(1, (int)Math.Round(faithTarget / cfg.FaithsPerReligion));

        var all = Enumerable.Range(0, counties.Count).ToList();
        var religionOf = RegionGrowth.Partition(graph, all, religionTarget, rng, out _);

        var religions = new List<Religion>();
        var faiths = new List<Faith>();
        var byCounty = new Dictionary<Title, Faith>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Step 1: Create Religions and Faiths
        for (int r = 0; r < religionTarget; r++)
        {
            var members = all.Where(i => religionOf[i] == r).ToList();
            if (members.Count == 0) continue;

            var religion = CreateReligion(religions.Count, TribalShare(members.Select(i => counties[i])),
                vocab, usedNames, cfg, rng);
            religions.Add(religion);

            int within = Math.Max(1, (int)Math.Round(members.Count / cfg.CountiesPerFaith));
            var faithOf = RegionGrowth.Partition(graph, members, within, rng, out _);

            var religionFaiths = new List<Faith>();

            for (int f = 0; f < within; f++)
            {
                var owned = members.Where(i => faithOf[i] == f).Select(i => counties[i]).ToList();
                if (owned.Count == 0) continue;

                var faith = CreateFaith(religion, faiths.Count + religionFaiths.Count, vocab, usedNames, rng);

                faith.Counties.AddRange(owned.Where(c => !wilderness.Contains(c)));
                foreach (var county in owned) byCounty[county] = faith;

                religionFaiths.Add(faith);
            }

            if (religionFaiths.Count == 0) continue;

            // Step 2: Determine Organization & Hierarchy
            var primaryFaith = religionFaiths.OrderByDescending(f => f.Counties.Count).First();

            foreach (var faith in religionFaiths)
            {
                faith.IsOrganized = TribalShare(faith.Counties) < cfg.UnreformedTribalShare;
                religion.Faiths.Add(faith);
                faiths.Add(faith);
            }

            // Step 3: Mint Heads of Faith (ONLY for Organized Faiths)
            double headShare = cfg.HeadOfFaithShare * (religion.Monotheist ? 2.0 : 1.0);

            if (primaryFaith.IsOrganized && rng.Chance(headShare))
            {
                primaryFaith.Head = new HeadOfFaith
                {
                    TitleKey = $"d_{primaryFaith.Key}_head",
                    Name = GenerateHeadTitleName(religion, rng),
                    Seat = null!,
                };
            }

            foreach (var faith in religionFaiths.Where(f => f != primaryFaith && f.IsOrganized))
            {
                if (rng.Chance(headShare * 0.35))
                {
                    faith.Head = new HeadOfFaith
                    {
                        TitleKey = $"d_{faith.Key}_head",
                        Name = GenerateHeadTitleName(religion, rng),
                        Seat = null!,
                    };
                }
            }
        }

        // Step 4: Place Holy Sites
        foreach (var faith in faiths)
        {
            PlaceHolySites(faith, development, faiths, counties, cfg.HolySitesPerFaith,
                wilderness, cfg.WildernessHolySiteShare, rng);

            if (faith.Head is not null && faith.HolySites.Count > 0)
            {
                faith.Head = new HeadOfFaith
                {
                    TitleKey = faith.Head.TitleKey,
                    Name = faith.Head.Name,
                    Seat = faith.HolySites[0].County,
                };
            }
        }

        Report(religions, faiths, counties.Count, sw.ElapsedMilliseconds);
        return new FaithMap { Religions = religions, Faiths = faiths, ByCounty = byCounty };

        double TribalShare(IEnumerable<Title> of)
        {
            int tribal = 0, total = 0;
            foreach (var county in of)
            {
                total++;
                if (governments.IsTribal(county)) tribal++;
            }

            return total == 0 ? 1.0 : tribal / (double)total;
        }
    }

    private static string GenerateHeadTitleName(Religion religion, Rng rng)
    {
        string word = religion.Language.Word(rng, 2, 3);
        string prefix = religion.Monotheist
            ? rng.Pick([
                "the High Seat of", "the Sacred Throne of", "the Prime Apex of",
                "the First Sanctum of", "the Grand Exaltate of", "the Sole Pinnacle of",
                "the Eternal Canopy of", "the Crown Sanctum of", "the Supreme Spire of"
            ])
            : rng.Pick([
                "the Great Conclave of", "the High Circle of", "the Vault of",
                "the Eternal Hearth of", "the Radiant Mirror of", "the Silent Order of",
                "the Grand Coven of", "the Star Shrine of", "the Sacred Spire of"
            ]);

        return $"{prefix} {word}";
    }

    private static RegionGrowth.Graph CountyGraph(List<Title> counties, ProvinceMap provinces,
        int[] order, int landCount, TerrainClass[] provinceTerrain, double terrainWeight)
    {
        var countyOfProvince = new Dictionary<int, int>();
        for (int i = 0; i < counties.Count; i++)
            foreach (var barony in counties[i].Children)
                if (barony.ProvinceId > 0) countyOfProvince[barony.ProvinceId] = i;

        var seedOfProvince = new int[landCount + 1];
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= landCount) seedOfProvince[id] = label;
        }

        var neighbours = new List<int>[counties.Count];
        for (int i = 0; i < neighbours.Length; i++) neighbours[i] = [];

        var linked = new HashSet<(int, int)>();
        foreach (var (province, others) in Titles.BuildAdjacency(provinces, landCount, order))
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

                total += Resistance(provinceTerrain[id]);
                var seed = provinces.Seeds[seedOfProvince[id]];
                x += seed.X;
                y += seed.Y;
                counted++;
            }

            double mean = counted == 0 ? 1.5 : total / counted;
            cost[i] = Math.Max(0.1, 1.0 + (mean - 1.0) * terrainWeight);
            position[i] = counted == 0 ? (0, 0) : (x / counted, y / counted);
        }

        return new RegionGrowth.Graph { Neighbours = neighbours, EnterCost = cost, Position = position };
    }

    private static double Resistance(TerrainClass t) => t switch
    {
        TerrainClass.DesertMountains => 5.0,
        TerrainClass.Mountains => 4.0,
        TerrainClass.Arctic => 4.0,
        TerrainClass.Desert => 3.0,
        TerrainClass.Jungle => 2.6,
        TerrainClass.Wetlands => 2.0,
        TerrainClass.Taiga => 1.8,
        TerrainClass.Hills => 1.5,
        TerrainClass.Forest => 1.4,
        TerrainClass.Drylands => 1.3,
        TerrainClass.Steppe => 1.1,
        TerrainClass.Beach => 0.6,
        _ => 1.0,
    };

    public const string UnsettledFaithKey = "gen_faith_unsettled";

    public static (Religion Religion, Faith Faith) CreateUnsettled(VanillaVocabulary vocab,
        HashSet<string> usedNames, MapConfig cfg, Rng rng)
    {
        var religion = CreateReligion(0, 0, vocab, usedNames, cfg, rng,
            keyOverride: "gen_religion_unsettled");

        var faith = new Faith
        {
            Key = UnsettledFaithKey,
            Name = "Unsettled",
            Religion = religion,
            Color = (0.42, 0.40, 0.37),
            Icon = vocab.FaithIcons.Count > 0 ? rng.Pick(vocab.FaithIcons) : "germanic",
            Tenets = Sample(vocab.Tenets, 3, rng),
            IsOrganized = false,
        };

        religion.Faiths.Add(faith);
        return (religion, faith);
    }

    private static Religion CreateReligion(int index, double tribalShare, VanillaVocabulary vocab,
        HashSet<string> usedNames, MapConfig cfg, Rng rng, string? keyOverride = null)
    {
        var language = Language.Create($"religion_tongue_{keyOverride ?? index.ToString()}", rng);
        string key = keyOverride ?? $"gen_religion_{index}";

        double settled = 1.0 - tribalShare;
        bool monotheist = rng.Chance(Math.Clamp(cfg.MonotheistShare * (0.15 + 1.45 * settled), 0.0, 1.0));

        var doctrines = new Dictionary<string, string>();
        foreach (string group in FilledGroups)
        {
            if (ForcedDoctrines.TryGetValue(group, out string? forced)
                && vocab.DoctrineGroups.TryGetValue(group, out var forcedMembers)
                && forcedMembers.Contains(forced))
            {
                doctrines[group] = forced;
                continue;
            }

            if (!vocab.DoctrineGroups.TryGetValue(group, out var members) || members.Count == 0)
                continue;

            doctrines[group] = group switch
            {
                "doctrine_theism" => Prefer(members,
                    monotheist ? ["doctrine_monotheist"] : ["doctrine_polytheist"], rng),

                "doctrine_pluralism" => Prefer(members, monotheist
                    ? ["doctrine_pluralism_fundamentalist", "doctrine_pluralism_righteous"]
                    : ["doctrine_pluralism_pluralistic", "doctrine_pluralism_righteous"], rng),

                _ => rng.Pick(members),
            };
        }

        var localization = new List<(string, string)>();
        var text = new Dictionary<string, string>();
        BuildLocalization(key, language, vocab, rng, localization, text);

        return new Religion
        {
            Key = key,
            Name = Unique(language.Word(rng, 2, 3), usedNames),
            Language = language,
            GraphicalFaith = vocab.GraphicalFaiths.Count > 0
                ? rng.Pick(vocab.GraphicalFaiths)
                : "pagan_gfx",
            Monotheist = monotheist,
            Doctrines = doctrines,
            Virtues = Sample(vocab.Virtues, rng.Int(3, 5), rng),
            Sins = Sample(vocab.Sins, rng.Int(3, 5), rng),
            Localization = localization,
            LocalizationText = text,
        };
    }

    private static void BuildLocalization(string religionKey, Language language,
        VanillaVocabulary vocab, Rng rng, List<(string, string)> into, Dictionary<string, string> text)
    {
        var words = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (tag, value) in vocab.ReligionLocTemplate)
        {
            if (IsConstant(value) || IsGrammatical(tag)) { into.Add((tag, value)); continue; }

            string locKey = $"{religionKey}_{tag.ToLowerInvariant()}";
            string word;

            if (tag.EndsWith("Possessive", StringComparison.Ordinal)
                && words.TryGetValue(tag[..^"Possessive".Length], out string? baseName))
                word = baseName + "'s";
            else if (tag.EndsWith("Plural", StringComparison.Ordinal)
                     && words.TryGetValue(tag[..^"Plural".Length], out string? singular))
                word = singular + "s";
            else
                word = language.Word(rng, 2, 3);

            words[tag] = word;
            text[locKey] = word;
            into.Add((tag, locKey));
        }
    }

    private static bool IsConstant(string value)
        => value.StartsWith('{') || value.All(c => !char.IsLower(c));

    private static bool IsGrammatical(string tag) =>
        tag.EndsWith("SheHe", StringComparison.Ordinal)
        || tag.EndsWith("HerHis", StringComparison.Ordinal)
        || tag.EndsWith("HerHim", StringComparison.Ordinal)
        || tag.EndsWith("HerselfHimself", StringComparison.Ordinal)
        || tag.EndsWith("MistressMaster", StringComparison.Ordinal)
        || tag.EndsWith("MotherFather", StringComparison.Ordinal);

    private static Faith CreateFaith(Religion religion, int index, VanillaVocabulary vocab,
        HashSet<string> usedNames, Rng rng)
    {
        return new Faith
        {
            Key = $"gen_faith_{index}",
            Name = Unique(religion.Language.Word(rng, 2, 3), usedNames),
            Religion = religion,
            Color = (rng.Decimal(0.1, 0.9), rng.Decimal(0.1, 0.9), rng.Decimal(0.1, 0.9)),
            Icon = vocab.FaithIcons.Count > 0 ? rng.Pick(vocab.FaithIcons) : "germanic",
            Tenets = Sample(vocab.Tenets, 3, rng),
        };
    }

    private static void PlaceHolySites(Faith faith, Dictionary<Title, int> development,
        List<Faith> allFaiths, List<Title> allCounties, int targetCount,
        WildernessMap wilderness, double wildShare, Rng rng)
    {
        var chosenCounties = new List<Title>();
        int abroad = Math.Max(0, targetCount - 4);

        var existingHolySites = allFaiths
            .Where(f => f != faith && f.HolySites.Count > 0)
            .SelectMany(f => f.HolySites.Select(hs => hs.County))
            .Distinct()
            .OrderByDescending(c => development.GetValueOrDefault(c))
            .ToList();

        if (chosenCounties.Count < abroad && existingHolySites.Count > 0 && rng.Chance(0.75))
        {
            chosenCounties.Add(rng.Pick(existingHolySites.Take(4).ToList()));
        }

        var foreignCounties = allCounties
            .Where(c => !faith.Counties.Contains(c) && !chosenCounties.Contains(c))
            .OrderByDescending(c => development.GetValueOrDefault(c))
            .Take(25)
            .ToList();

        if (chosenCounties.Count < abroad && foreignCounties.Count > 0 && rng.Chance(0.85))
        {
            chosenCounties.Add(rng.Pick(foreignCounties.Take(5).ToList()));
        }

        Title? wildSite = null;
        bool wantWild = wilderness.Count > 0
            && faith.Counties.Count > 0
            && rng.Chance(wildShare);

        if (wantWild)
        {
            var duchies = faith.Counties.Select(c => c.Parent).Where(p => p is not null).ToHashSet();
            var kingdoms = faith.Counties.Select(c => c.Parent?.Parent).Where(p => p is not null).ToHashSet();

            var nearby = wilderness.Counties
                .Where(c => !chosenCounties.Contains(c))
                .Where(c => duchies.Contains(c.Parent) || kingdoms.Contains(c.Parent?.Parent))
                .OrderBy(c => c.Index)
                .ToList();

            if (nearby.Count > 0) wildSite = rng.Pick(nearby);
        }

        int localTarget = wildSite is null ? targetCount : targetCount - 1;

        var localRanked = faith.Counties
            .Where(c => !chosenCounties.Contains(c))
            .OrderByDescending(c => development.GetValueOrDefault(c))
            .ThenBy(c => c.Index);

        foreach (var county in localRanked)
        {
            if (chosenCounties.Count >= localTarget) break;
            chosenCounties.Add(county);
        }

        if (wildSite is not null && chosenCounties.Count > 0) chosenCounties.Add(wildSite);

        // Fallback: If a faith has no counties at all (e.g. dummy/unsettled), ensure it gets at least 1 site
        if (chosenCounties.Count == 0 && allCounties.Count > 0)
        {
            chosenCounties.Add(allCounties[0]);
        }

        foreach (var county in chosenCounties)
        {
            faith.HolySites.Add(($"site_{county.Key}", county));
        }
    }

    private static string Prefer(List<string> members, string[] preferred, Rng rng)
    {
        var usable = preferred.Where(members.Contains).ToList();
        return usable.Count > 0 ? rng.Pick(usable) : rng.Pick(members);
    }

    private static List<string> Sample(List<string> pool, int count, Rng rng)
    {
        if (pool.Count == 0) return [];

        var copy = pool.ToList();
        rng.Shuffle(copy);
        return [.. copy.Take(Math.Min(count, copy.Count))];
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

    private static void Report(List<Religion> religions, List<Faith> faiths, int counties,
        long elapsedMs)
    {
        if (faiths.Count == 0) return;

        int monotheist = religions.Count(r => r.Monotheist);
        int heads = faiths.Count(f => f.Head is not null);
        int unreformed = faiths.Count(f => !f.IsOrganized);
        var sizes = faiths.Select(f => f.Counties.Count).OrderBy(n => n).ToList();

        Console.WriteLine($"  faiths: {faiths.Count} in {religions.Count} religions " +
                          $"({monotheist} monotheist, {unreformed} unreformed, {heads} heads of faith) " +
                          $"over {counties} counties — " +
                          $"median {sizes[sizes.Count / 2]}, largest {sizes[^1]} counties " +
                          $"({elapsedMs} ms)");
    }
}