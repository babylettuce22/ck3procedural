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
    /// <summary>Frozen. Every other file references a faith by this.</summary>
    public required string Key { get; init; }

    public required string Name { get; set; }
    public required Religion Religion { get; init; }
    public required (double R, double G, double B) Color { get; set; }
    public required string Icon { get; set; }

    public required List<string> Tenets { get; set; }

    public List<Title> Counties { get; } = [];
    public List<(string Key, Title County)> HolySites { get; } = [];

    public HeadOfFaith? Head { get; set; }

    public bool IsOrganized { get; set; } = true;
    public bool IsDominant { get; set; } = false;
}

/// <summary>A generated religion: a liturgical language, a doctrine baseline, and its faiths.</summary>
public sealed class Religion
{
    /// <summary>Frozen, for the same reason a faith's is.</summary>
    public required string Key { get; init; }

    public required string Name { get; set; }

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

    /// <summary>
    /// True when the structure and geography came from an Azgaar export. The Tier 1 renaming pass
    /// checks this and stands down — the names are already the export's, and renaming them against
    /// a majority vote they were built from could only move them away.
    /// </summary>
    public bool ImportedStructure { get; init; }

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
        MapConfig cfg, WorldCenterMap? worldCenters, Rng rng, AzgaarImport? azgaar = null)
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

        // The export's religion tree, when there is one to read. The plan replaces the two
        // partition passes below - which religions exist and which counties they hold - while
        // everything CK3-specific (doctrines, tenets, heads, holy sites) is still generated by the
        // same code either way. See AzgaarFaiths for the mapping.
        var plan = azgaar is null ? null : AzgaarFaiths.BuildPlan(azgaar, counties, graph);

        if (plan is not null)
        {
            foreach (var planned in plan)
            {
                var groupCounties = planned.Faiths.SelectMany(f => f.Counties).ToList();
                double tribalShare = TribalShare(groupCounties);

                var religion = CreateReligion(religions.Count, tribalShare, vocab, usedNames, cfg,
                    rng, monotheistOverride: AzgaarFaiths.IsMonotheist(planned.Root),
                    theismPreference: AzgaarFaiths.TheismPreference(planned.Root));
                religions.Add(religion);

                // The religion answers to the root tradition's form word - "Shamanism",
                // "Monotheism" - which is what the Tier 1 renamer gave a lucky majority and this
                // gives by construction. A dozen folk religions share half as many form words,
                // though, so a taken form is qualified by the tradition's own culture - "Trow
                // Shamanism" - rather than by the numeral Unique() would append, because
                // "Shamanism3" in a tooltip is the generator showing through.
                string form = AzgaarNaming.StripParenthetical(AzgaarNaming.StripArticle(
                    planned.Root.Form is { Length: > 0 } f ? f : planned.Root.Name));

                usedNames.Remove(religion.Name);
                if (usedNames.Add(form))
                {
                    religion.Name = form;
                }
                else
                {
                    string? culture = azgaar!.World.Culture(planned.Root.Culture)?.Name;
                    religion.Name = Unique(culture is { Length: > 0 }
                        ? $"{AzgaarNaming.StripParenthetical(AzgaarNaming.StripArticle(culture))} {form}"
                        : form, usedNames);
                }

                var religionFaiths = new List<Faith>();
                int groupTotal = groupCounties.Count;

                foreach (var member in planned.Faiths)
                {
                    var faith = CreateFaith(religion, faiths.Count + religionFaiths.Count, vocab,
                        usedNames, cfg, rng);

                    usedNames.Remove(faith.Name);
                    faith.Name = Unique(AzgaarNaming.StripParenthetical(
                        AzgaarNaming.StripArticle(member.Source.Name)), usedNames);

                    if (AzgaarColors.TryParseColor(member.Source.Color, out var rgb))
                        faith.Color = (rgb.R / 255.0, rgb.G / 255.0, rgb.B / 255.0);

                    faith.Counties.AddRange(member.Counties.Where(c => !wilderness.Contains(c)));
                    foreach (var county in member.Counties) byCounty[county] = faith;

                    religionFaiths.Add(faith);
                }

                if (religionFaiths.Count == 0) continue;

                // The founding faith reads as dominant when it holds most of the group - the same
                // judgement the generated path makes when it plans a dominant orthodoxy, made here
                // after the fact from what the export actually drew.
                bool hasDominant = religionFaiths.Count > 1
                    && planned.Faiths[0].Counties.Count * 5 >= groupTotal * 3;
                religionFaiths[0].IsDominant = hasDominant;

                // The one prose fact the export has that we otherwise invent: the god's name.
                ApplyDeity(religion, planned);

                OrganizeAndMintHeads(religion, religionFaiths, hasDominant);
            }

            Console.WriteLine($"  azgaar faiths: {faiths.Count} faiths in {religions.Count} " +
                              $"religions built from the export's tree " +
                              $"({plan.Count(p => p.Faiths.Count > 1)} with heresies or cults attached)");
        }
        else
        // Step 1: Create Religions and Faiths
        for (int r = 0; r < religionTarget; r++)
        {
            var members = all.Where(i => religionOf[i] == r).ToList();
            if (members.Count == 0) continue;

            double tribalShare = TribalShare(members.Select(i => counties[i]));
            var religion = CreateReligion(religions.Count, tribalShare, vocab, usedNames, cfg, rng);
            religions.Add(religion);

            // Determine Faith Distribution Archetype
            // Archetype Roll: 0 = Hegemonic Dominant Orthodoxy, 1 = Monolithic (Single Faith), 2 = Pluralistic
            double archetypeRoll = rng.NextDouble();
            int faithCount;
            bool hasDominantFaith = false;

            if (members.Count <= 6 || tribalShare > 0.70 && archetypeRoll < 0.45)
            {
                // Monolithic: Small realms or conservative tribal pantheons stay unified
                faithCount = 1;
            }
            else if (archetypeRoll < 0.60)
            {
                // Dominant Orthodoxy: 1 dominant giant + 1-3 regional schisms/minorities
                hasDominantFaith = true;
                faithCount = Math.Clamp(rng.Int(2, 4), 2, Math.Max(2, members.Count / 3));
            }
            else
            {
                // Pluralistic: Multiple competing regional branches
                int baseEstimate = (int)Math.Round(members.Count / cfg.CountiesPerFaith);
                faithCount = Math.Clamp(rng.Int(baseEstimate - 1, baseEstimate + 1), 2, Math.Max(2, members.Count / 3));
            }

            var religionFaiths = new List<Faith>();
            var faithOf = new Dictionary<int, int>();

            if (faithCount == 1)
            {
                for (int i = 0; i < members.Count; i++) faithOf[members[i]] = 0;
            }
            else if (hasDominantFaith)
            {
                // Partition with an asymmetric share: Dominant faith gets 65-85% of counties
                faithOf = PartitionWithDominant(graph, members, faithCount, rng);
            }
            else
            {
                var rawPartition = RegionGrowth.Partition(graph, members, faithCount, rng, out _);
                for (int i = 0; i < members.Count; i++) faithOf[members[i]] = rawPartition[i];
            }

            for (int f = 0; f < faithCount; f++)
            {
                var owned = members.Where(i => faithOf.GetValueOrDefault(i, 0) == f).Select(i => counties[i]).ToList();
                if (owned.Count == 0) continue;

                var faith = CreateFaith(religion, faiths.Count + religionFaiths.Count, vocab, usedNames, cfg, rng);
                faith.IsDominant = (f == 0 && hasDominantFaith);

                faith.Counties.AddRange(owned.Where(c => !wilderness.Contains(c)));
                foreach (var county in owned) byCounty[county] = faith;

                religionFaiths.Add(faith);
            }

            if (religionFaiths.Count == 0) continue;

            OrganizeAndMintHeads(religion, religionFaiths, hasDominantFaith);
        }

        // Step 4: Place Holy Sites (Hierarchically by Religion & Faith)
        PlaceAllHolySites(religions, development, counties, cfg.HolySitesPerFaith,
            wilderness, cfg.WildernessHolySiteShare, worldCenters, rng);

        foreach (var faith in faiths)
        {
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
        return new FaithMap
        {
            Religions = religions,
            Faiths = faiths,
            ByCounty = byCounty,
            ImportedStructure = plan is not null,
        };

        // Steps 2 and 3, shared by both paths: which faiths count as organized, and which get a
        // head. Organization stays a judgement about government rather than about the export's
        // type field, because CK3's "unreformed" is about how the holder rules, not about theology
        // - a folk faith whose people are settled feudal lords plays wrong as unreformed pagan.
        void OrganizeAndMintHeads(Religion religion, List<Faith> religionFaiths, bool hasDominantFaith)
        {
            // Step 2: Determine Organization & Hierarchy
            var primaryFaith = religionFaiths.OrderByDescending(f => f.Counties.Count).First();

            foreach (var faith in religionFaiths)
            {
                faith.IsOrganized = TribalShare(faith.Counties) < cfg.UnreformedTribalShare;
                religion.Faiths.Add(faith);
                faiths.Add(faith);
            }

            // Step 3: Mint Heads of Faith (Favor Dominant / Primary Faiths)
            double headShare = cfg.HeadOfFaithShare * (religion.Monotheist ? 2.0 : 1.0);

            if (primaryFaith.IsOrganized && rng.Chance(headShare * (hasDominantFaith ? 1.4 : 1.0)))
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
                if (rng.Chance(headShare * 0.25))
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

    /// <summary>
    /// Partitions religion members into 1 large dominant orthodoxy (65-85% share) 
    /// and 1 to N-1 minor schisms hugging periphery borderlands.
    /// </summary>
    private static Dictionary<int, int> PartitionWithDominant(
        RegionGrowth.Graph graph, List<int> members, int faithCount, Rng rng)
    {
        var result = new Dictionary<int, int>();
        if (members.Count == 0) return result;
        if (faithCount <= 1)
        {
            foreach (int m in members) result[m] = 0;
            return result;
        }

        int dominantTarget = (int)Math.Round(members.Count * rng.Double(0.68, 0.84));
        dominantTarget = Math.Clamp(dominantTarget, members.Count - (faithCount - 1) * 3, members.Count - (faithCount - 1));

        // Find the most central node in members for the dominant faith
        int centerNode = members.OrderBy(i =>
        {
            double distSum = 0;
            for (int k = 0; k < Math.Min(members.Count, 8); k++)
            {
                int other = members[k];
                double dx = graph.Position[i].X - graph.Position[other].X;
                double dy = graph.Position[i].Y - graph.Position[other].Y;
                distSum += dx * dx + dy * dy;
            }
            return distSum;
        }).First();

        // Grow dominant faith outward from center
        var dominantAssigned = new HashSet<int> { centerNode };
        var frontier = new PriorityQueue<int, double>();

        foreach (int nbr in graph.Neighbours[centerNode])
        {
            if (members.Contains(nbr)) frontier.Enqueue(nbr, graph.EnterCost[nbr]);
        }

        while (frontier.Count > 0 && dominantAssigned.Count < dominantTarget)
        {
            int current = frontier.Dequeue();
            if (!dominantAssigned.Add(current)) continue;

            foreach (int nbr in graph.Neighbours[current])
            {
                if (members.Contains(nbr) && !dominantAssigned.Contains(nbr))
                {
                    frontier.Enqueue(nbr, graph.EnterCost[nbr]);
                }
            }
        }

        foreach (int m in dominantAssigned) result[m] = 0;

        // Partition the remaining fringe counties among the minority faiths
        var remaining = members.Where(m => !dominantAssigned.Contains(m)).ToList();
        if (remaining.Count > 0)
        {
            int minorFaithCount = faithCount - 1;
            var minorPartition = RegionGrowth.Partition(graph, remaining, minorFaithCount, rng, out _);
            for (int i = 0; i < remaining.Count; i++)
            {
                result[remaining[i]] = minorPartition[i] + 1;
            }
        }

        return result;
    }

    private static void PlaceAllHolySites(
        List<Religion> religions,
        Dictionary<Title, int> development,
        List<Title> allCounties,
        int targetCount,
        WildernessMap wilderness,
        double wildShare,
        WorldCenterMap? worldCenters,
        Rng rng)
    {
        var globalHolySites = new List<Title>();

        foreach (var religion in religions)
        {
            var religionCounties = religion.Faiths
                .SelectMany(f => f.Counties)
                .Distinct()
                .ToList();

            if (religionCounties.Count == 0) continue;

            // 1. Pick Shared Core Holy Sites for the Religion (2 to 3 sites if multi-faith)
            var coreSites = new List<Title>();

            int coreTarget = religion.Faiths.Count > 1
                ? Math.Clamp((int)Math.Round(targetCount * 0.5), 2, targetCount - 1)
                : 0;

            if (coreTarget > 0)
            {
                // Prioritize a World Center within the religion's domain (e.g. Rome, Mecca, Jerusalem)
                if (worldCenters is not null)
                {
                    var religionCenters = religionCounties.Where(c => worldCenters.IsCenter(c)).ToList();
                    if (religionCenters.Count > 0)
                    {
                        coreSites.Add(rng.Pick(religionCenters));
                    }
                    else if (globalHolySites.Count > 0 && rng.Chance(religion.Monotheist ? 0.45 : 0.20))
                    {
                        // Chance to share an existing renowned holy site from another religion
                        coreSites.Add(rng.Pick(globalHolySites.Take(4).ToList()));
                    }
                }

                // Chance for an ancient wilderness holy site (e.g. Mount Sinai, Stonehenge)
                if (wilderness.Count > 0 && rng.Chance(wildShare))
                {
                    var duchies = religionCounties.Select(c => c.Parent).Where(p => p is not null).ToHashSet();
                    var kingdoms = religionCounties.Select(c => c.Parent?.Parent).Where(p => p is not null).ToHashSet();

                    var nearbyWild = wilderness.Counties
                        .Where(c => !coreSites.Contains(c))
                        .Where(c => duchies.Contains(c.Parent) || kingdoms.Contains(c.Parent?.Parent))
                        .OrderBy(c => c.Index)
                        .ToList();

                    if (nearbyWild.Count > 0)
                    {
                        coreSites.Add(rng.Pick(nearbyWild));
                    }
                }

                // Fill remaining core slots from the highest-development counties across the religion
                var topReligionCounties = religionCounties
                    .Where(c => !coreSites.Contains(c))
                    .OrderByDescending(c => development.GetValueOrDefault(c))
                    .ThenBy(c => c.Index);

                foreach (var c in topReligionCounties)
                {
                    if (coreSites.Count >= coreTarget) break;
                    coreSites.Add(c);
                }

                foreach (var site in coreSites)
                {
                    if (!globalHolySites.Contains(site))
                        globalHolySites.Add(site);
                }
            }

            // 2. Populate each Faith's holy sites (inheriting the core sites + adding local shrines)
            foreach (var faith in religion.Faiths)
            {
                var chosenCounties = new List<Title>(coreSites);

                // If the faith has a local World Center of its own not yet picked, include it
                if (worldCenters is not null)
                {
                    var localCenters = faith.Counties
                        .Where(c => worldCenters.IsCenter(c) && !chosenCounties.Contains(c))
                        .ToList();

                    if (localCenters.Count > 0)
                    {
                        chosenCounties.Add(rng.Pick(localCenters));
                    }
                }

                // Add top development counties specific to this faith
                var localRanked = faith.Counties
                    .Where(c => !chosenCounties.Contains(c))
                    .OrderByDescending(c => development.GetValueOrDefault(c))
                    .ThenBy(c => c.Index);

                foreach (var county in localRanked)
                {
                    if (chosenCounties.Count >= targetCount) break;
                    chosenCounties.Add(county);
                }

                // Fallback 1: If faith is very small, pull from other counties in the parent religion
                if (chosenCounties.Count < targetCount)
                {
                    var otherReligionCounties = religionCounties
                        .Where(c => !chosenCounties.Contains(c))
                        .OrderByDescending(c => development.GetValueOrDefault(c))
                        .ThenBy(c => c.Index);

                    foreach (var county in otherReligionCounties)
                    {
                        if (chosenCounties.Count >= targetCount) break;
                        chosenCounties.Add(county);
                    }
                }

                // Fallback 2: Any global county if still short
                if (chosenCounties.Count < targetCount)
                {
                    foreach (var county in allCounties.Where(c => !chosenCounties.Contains(c)))
                    {
                        if (chosenCounties.Count >= targetCount) break;
                        chosenCounties.Add(county);
                    }
                }

                // Fallback 3: Single emergency fallback
                if (chosenCounties.Count == 0 && allCounties.Count > 0)
                {
                    chosenCounties.Add(allCounties[0]);
                }

                foreach (var county in chosenCounties.Take(targetCount))
                {
                    faith.HolySites.Add(($"gen_hs_{county.Key}", county));
                    if (!globalHolySites.Contains(county))
                        globalHolySites.Add(county);
                }
            }
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

    /// <param name="monotheistOverride">
    /// The import's answer to the one doctrine question it can answer, or null to roll it. Rolled
    /// from settledness otherwise, exactly as before.
    /// </param>
    /// <param name="theismPreference">
    /// Doctrine keys to try first for the theism slot, for imported forms with a CK3 counterpart
    /// beyond the monotheist/polytheist pair (dualism). Prefer() ignores keys the install lacks.
    /// </param>
    private static Religion CreateReligion(int index, double tribalShare, VanillaVocabulary vocab,
        HashSet<string> usedNames, MapConfig cfg, Rng rng, bool? monotheistOverride = null,
        string[]? theismPreference = null, string? keyOverride = null)
    {
        var language = Language.Create($"religion_tongue_{keyOverride ?? index.ToString()}", rng);
        string key = keyOverride ?? $"gen_religion_{index}";

        double settled = 1.0 - tribalShare;
        bool monotheist = monotheistOverride
            ?? rng.Chance(Math.Clamp(cfg.MonotheistShare * (0.15 + 1.45 * settled), 0.0, 1.0));

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
                    theismPreference
                    ?? (monotheist ? ["doctrine_monotheist"] : ["doctrine_polytheist"]), rng),

                "doctrine_pluralism" => Prefer(members, monotheist
                    ? ["doctrine_pluralism_fundamentalist", "doctrine_pluralism_righteous"]
                    : ["doctrine_pluralism_pluralistic", "doctrine_pluralism_righteous"], rng),

                "doctrine_pilgrimage" => Prefer(
                    members.Where(d => d != "doctrine_pilgrimage_mandatory_hajj").ToList(),
                    ["doctrine_pilgrimage_encouraged", "doctrine_pilgrimage_mandatory"], rng),

                "doctrine_theocracy" => Prefer(members, ["doctrine_theocracy_temporal"], rng),

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
        HashSet<string> usedNames, MapConfig cfg, Rng rng)
    {
        // Natural primitivism reads as a slur on a generated people rather than as flavour, so it is
        // filterable. Filtered here rather than out of the vocabulary itself because the vocabulary
        // is what the install actually has, and the rest of the program is entitled to see it whole.
        var pool = cfg.AllowNaturalPrimitivism
            ? vocab.Tenets
            : vocab.Tenets.Where(t => !t.Contains("natural_primitivism", StringComparison.OrdinalIgnoreCase)).ToList();

        return new Faith
        {
            Key = $"gen_faith_{index}",
            Name = Unique(religion.Language.Word(rng, 2, 3), usedNames),
            Religion = religion,
            Color = (rng.Decimal(0.1, 0.9), rng.Decimal(0.1, 0.9), rng.Decimal(0.1, 0.9)),
            Icon = vocab.FaithIcons.Count > 0 ? rng.Pick(vocab.FaithIcons) : "germanic",
            Tenets = Sample(pool, 3, rng),
        };
    }

    /// <summary>
    /// Puts the export's god behind the religion's HighGodName localisation keys, replacing the
    /// generated word. Per religion rather than per faith because that is where the localisation
    /// template lives; the deity comes from the largest faith that names one, which for every
    /// ordinary group is the founding tradition, and heresies share their parent's god exactly as
    /// CK3's own heresies do.
    /// </summary>
    private static void ApplyDeity(Religion religion, AzgaarFaiths.PlannedReligion planned)
    {
        string? deity = planned.Faiths.Select(f => AzgaarFaiths.DeityName(f.Source))
                            .FirstOrDefault(d => d is not null)
                        ?? AzgaarFaiths.DeityName(planned.Root);
        if (deity is null) return;

        foreach (var (tag, value) in religion.Localization)
        {
            if (!religion.LocalizationText.ContainsKey(value)) continue;

            religion.LocalizationText[value] = tag switch
            {
                "HighGodName" or "HighGodNameAlternate" => deity,
                "HighGodNamePossessive" or "HighGodNameAlternatePossessive" => deity + "'s",
                _ => religion.LocalizationText[value],
            };
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
                          $"smallest {sizes[0]}, median {sizes[sizes.Count / 2]}, largest {sizes[^1]} counties " +
                          $"({elapsedMs} ms)");
    }
}