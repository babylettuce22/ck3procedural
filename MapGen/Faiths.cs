using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public sealed class HeadOfFaith
{
    public required string TitleKey { get; init; }
    public required string Name { get; init; }
    public required Title Seat { get; init; }

    /// <summary>
    /// A temporal head — vanilla's caliph shape — rather than a spiritual one. The title is held
    /// by the faith's strongest landed ruler instead of a theocrat of its own, and it is written
    /// with <c>doctrine_temporal_head</c>, which vanilla only allows beside lay clergy. Only an
    /// Abrahamic-shaped religion with lay clergy mints one.
    /// </summary>
    public bool Temporal { get; init; }
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

    /// <summary>
    /// Written in vanilla's Abrahamic shape: the <c>rf_abrahamic</c> family and its hostility
    /// doctrine, no pagan roots, and every faith organised. Decided in
    /// <see cref="Faiths.CreateReligion"/> from theism and settledness under
    /// <see cref="MapConfig.FaithShape.Shaped"/>; always false under
    /// <see cref="MapConfig.FaithShape.PaganOnly"/>.
    ///
    /// The family itself gates only flavour. What this changes is the hostility doctrine, which
    /// decides who a faith may holy-war: a pagan faith sees its own religion's heresies as Astray
    /// and every other religion as merely Hostile, an Abrahamic one sees heresies as Hostile and
    /// everything else as Evil.
    /// </summary>
    public required bool Abrahamic { get; init; }

    /// <summary>
    /// Rulers own the temples (vanilla's Islamic model) rather than a clergy holding them. Only
    /// rolled for Abrahamic-shaped religions, and the precondition for a temporal head.
    /// </summary>
    public required bool LayClergy { get; init; }

    public required Dictionary<string, string> Doctrines { get; init; }
    public required List<string> Virtues { get; init; }
    public required List<string> Sins { get; init; }

    /// <summary>
    /// Whether this religion crowns a new ruler or invests them with regalia — true for a crown.
    ///
    /// CK3 keeps this in two hardcoded religion lists inside
    /// <c>coronation_proper_artifact_crown_trigger</c> and its regalia twin, which name only vanilla
    /// religions. A generated religion matches neither and falls into their <c>trigger_else</c>,
    /// where both count — and "both" is the one answer that leaves the ceremony worse off, because
    /// <c>coronation_being_crowned_trigger</c> (the officiator places the crown, event 6100) needs
    /// the crown list to say yes. Deciding it here, per religion, lets
    /// <see cref="Emit.CoronationWriter"/> re-declare those two triggers with real answers.
    ///
    /// It also picks the slot of the sovereign artifact its kings start with, so the regalia a
    /// realm owns is the regalia its faith expects to see. Roughly two in three crown, which is
    /// vanilla's own split (12 religions to 7).
    /// </summary>
    public required bool CoronationCrown { get; init; }

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
        ["hostility_group"] = PaganHostility,
    };

    public const string Family = "rf_pagan";
    public const string AbrahamicFamily = "rf_abrahamic";

    public const string PaganHostility = "pagan_hostility_doctrine";
    public const string AbrahamicHostility = "abrahamic_hostility_doctrine";

    /// <summary>
    /// The two tenets that carry <c>great_holy_wars_active</c>. Vanilla shows the first only to
    /// Christianity and Judaism and the second only to Islam, but <c>is_shown</c> gates the
    /// creation screen, not script, so a generated faith can hold either. The first suits a
    /// spiritual head, the second a temporal one.
    /// </summary>
    private const string SpiritualWarTenet = "tenet_armed_pilgrimages";
    private const string TemporalWarTenet = "tenet_struggle_submission";

    /// <summary>
    /// Temple art vanilla files under Abrahamic religions. Every other set on the install is
    /// treated as the polytheist pool.
    /// </summary>
    private static readonly HashSet<string> MonotheistGraphics = new(StringComparer.Ordinal)
    {
        "catholic_gfx", "orthodox_gfx", "islamic_gfx",
    };

    public static FaithMap Build(List<Title> empires, ProvinceMap provinces, int[] order,
        int landCount, TerrainClass[] provinceTerrain, Dictionary<Title, int> development,
        GovernmentMap governments, VanillaVocabulary vocab, WildernessMap wilderness,
        MapConfig cfg, WorldCenterMap? worldCenters, Rng rng, AzgaarImport? azgaar = null,
        CultureMap? cultures = null)
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

            // The faith's words come from the tongue of the people who hold most of its ground —
            // a liturgical register of it, a little archaic — rather than from a language nobody
            // on the map speaks. A god's name should sound like the country that prays to him.
            Language? liturgical = null;
            if (cultures is not null)
            {
                var votes = new Dictionary<Heritage, int>();
                foreach (int i in members)
                    if (cultures.ByCounty.TryGetValue(counties[i], out var holder))
                        votes[holder.Heritage] = votes.GetValueOrDefault(holder.Heritage) + 1;

                if (votes.Count > 0)
                    liturgical = votes.OrderByDescending(kv => kv.Value)
                                      .ThenBy(kv => kv.Key.Key, StringComparer.Ordinal).First().Key.Language;
            }

            var religion = CreateReligion(religions.Count, tribalShare, vocab, usedNames, cfg, rng,
                liturgical: liturgical);
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
                // Partition returns one slot per *graph node*, not one per member, so it is read at
                // the county's own index. Reading it positionally instead handed the first
                // members.Count nodes' labels to the members - mostly -1, because those nodes
                // belong to some other religion - and a religion all of whose counties came back
                // -1 ended up written with an empty `faiths = { }`.
                var rawPartition = RegionGrowth.Partition(graph, members, faithCount, rng, out _);
                foreach (int m in members) faithOf[m] = rawPartition[m];
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
            if (faith.Head is null) continue;

            // A head with nowhere to sit is dropped rather than carried with a null seat:
            // ContentWriter writes `capital = <seat>` for every head title and would throw on it.
            // Only reachable for a faith holding no land at all — every other faith gets holy sites
            // from PlaceAllHolySites' fallbacks — and a landless faith was already headless before
            // Abrahamic religions began organising their faiths regardless of tribal share.
            if (faith.HolySites.Count == 0)
            {
                faith.Head = null;
                continue;
            }

            faith.Head = new HeadOfFaith
            {
                TitleKey = faith.Head.TitleKey,
                Name = faith.Head.Name,
                Seat = faith.HolySites[0].County,
                Temporal = faith.Head.Temporal,
            };
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
                // An Abrahamic-shaped religion organises every faith: it has no pagan roots for the
                // unreformed doctrine's reform flow to stand on, and it was only given the shape
                // because its land as a whole is settled enough that this rarely overrides anything.
                faith.IsOrganized = religion.Abrahamic
                    || TribalShare(faith.Counties) < cfg.UnreformedTribalShare;
                religion.Faiths.Add(faith);
                faiths.Add(faith);
            }

            // Heresies share most of the primary faith's creed. Before the heads, so a seeded war
            // tenet lands on top of the shared pair rather than being overwritten by it.
            AlignSiblingTenets(religion, primaryFaith, religionFaiths.Where(f => f != primaryFaith),
                vocab, cfg, rng);

            // Step 3: Mint Heads of Faith (Favor Dominant / Primary Faiths)
            double headShare = cfg.HeadOfFaithShare * (religion.Monotheist ? 2.0 : 1.0);

            if (primaryFaith.IsOrganized && rng.Chance(headShare * (hasDominantFaith ? 1.4 : 1.0)))
                Mint(primaryFaith);

            foreach (var faith in religionFaiths.Where(f => f != primaryFaith && f.IsOrganized))
            {
                if (rng.Chance(headShare * 0.25)) Mint(faith);
            }

            // The head's kind and its war tenet are only drawn for Abrahamic-shaped religions, so a
            // PaganOnly run consumes the stream exactly as before this existed. One in three heads
            // of a lay-clergy religion is temporal: a ruler who is also the faith's head, as
            // vanilla's caliphs are.
            void Mint(Faith faith)
            {
                bool temporal = religion.Abrahamic && religion.LayClergy && rng.Chance(1.0 / 3.0);

                faith.Head = new HeadOfFaith
                {
                    TitleKey = $"d_{faith.Key}_head",
                    Name = GenerateHeadTitleName(religion, rng, temporal),
                    Seat = null!,
                    Temporal = temporal,
                };

                if (religion.Abrahamic) SeedWarTenet(faith, temporal, vocab, cfg, rng);
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
            // Node-indexed, as above.
            var minorPartition = RegionGrowth.Partition(graph, remaining, minorFaithCount, rng, out _);
            foreach (int m in remaining)
            {
                result[m] = minorPartition[m] + 1;
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

    private static string GenerateHeadTitleName(Religion religion, Rng rng, bool temporal = false)
    {
        string word = religion.Language.Word(rng, 2, 3);
        string prefix = temporal
            ? rng.Pick([
                "the Sovereign Seat of", "the Faithful Crown of", "the Anointed Throne of",
                "the Sword and Sanctum of", "the Dominion of", "the Guardianship of",
                "the Successorship of", "the Sceptre of", "the Commandery of"
            ])
            : religion.Monotheist
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
            keyOverride: "gen_religion_unsettled", shapeable: false);

        var faith = new Faith
        {
            Key = UnsettledFaithKey,
            Name = "Unsettled",
            Religion = religion,
            Color = (0.42, 0.40, 0.37),
            Icon = vocab.FaithIcons.Count > 0 ? rng.Pick(vocab.FaithIcons) : "germanic",

            // Through the same pool every other faith draws from. Drawing from the raw vocabulary
            // here let the wilderness faith hold the one tenet the map was told to keep off it —
            // natural primitivism strips its holder's portrait — and, under Shaped, a syncretism
            // tenet naming a vanilla religion this map does not have.
            Tenets = SampleCompatible(TenetPool(religion, vocab, cfg), 3, religion.Doctrines.Values,
                vocab, rng),
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
    /// <param name="shapeable">
    /// Whether this religion may take the Abrahamic shape at all. False for the unsettled
    /// religion, whose one faith is unreformed by construction and must keep its pagan roots.
    /// </param>
    /// <summary>
    /// Temple art by theism: a pantheon does not get a mosque. Under <see cref="MapConfig.FaithShape.Shaped"/>
    /// monotheists draw from the sets vanilla files under Abrahamic religions and polytheists from
    /// the rest; either pool falls back to the whole install when it is empty. PaganOnly draws from
    /// everything, as it always did.
    /// </summary>
    private static string PickGraphicalFaith(VanillaVocabulary vocab, MapConfig cfg, bool monotheist, Rng rng)
    {
        if (vocab.GraphicalFaiths.Count == 0) return "pagan_gfx";
        if (cfg.FaithShaping != MapConfig.FaithShape.Shaped) return rng.Pick(vocab.GraphicalFaiths);

        var pool = vocab.GraphicalFaiths.Where(g => MonotheistGraphics.Contains(g) == monotheist).ToList();
        return rng.Pick(pool.Count > 0 ? pool : vocab.GraphicalFaiths);
    }

    private static Religion CreateReligion(int index, double tribalShare, VanillaVocabulary vocab,
        HashSet<string> usedNames, MapConfig cfg, Rng rng, bool? monotheistOverride = null,
        string[]? theismPreference = null, string? keyOverride = null, Language? liturgical = null,
        bool shapeable = true)
    {
        string tongueKey = $"religion_tongue_{keyOverride ?? index.ToString()}";
        var language = liturgical is not null
            ? liturgical.Derive(tongueKey, rng, 0.4)
            : Language.Create(tongueKey, rng);
        string key = keyOverride ?? $"gen_religion_{index}";

        double settled = 1.0 - tribalShare;
        bool monotheist = monotheistOverride
            ?? rng.Chance(Math.Clamp(cfg.MonotheistShare * (0.15 + 1.45 * settled), 0.0, 1.0));

        // The Abrahamic shape goes to settled monotheists only. Vanilla has no unreformed
        // Abrahamic faith and the reform flow assumes pagan roots, so a religion tribal enough to
        // be written unreformed keeps the pagan shape whatever it worships — the same threshold
        // OrganizeAndMintHeads reads per faith, read here over the whole religion. Nothing below
        // draws from the stream unless this is true, so PaganOnly reproduces the old output
        // exactly rather than merely resembling it.
        bool abrahamic = cfg.FaithShaping == MapConfig.FaithShape.Shaped && shapeable && monotheist
            && tribalShare < cfg.UnreformedTribalShare
            && vocab.DoctrineGroups.TryGetValue("hostility_group", out var hostilities)
            && hostilities.Contains(AbrahamicHostility);

        // Half of the Abrahamic religions let rulers own the temples, which is what makes a
        // temporal head possible later: vanilla refuses doctrine_temporal_head beside temporal
        // theocracy or a spiritually appointed clergy, so both groups follow this one roll.
        bool layClergy = abrahamic && rng.Chance(0.5);

        // Rolled before the loop because two groups read it: the clergy's sex follows the faith's,
        // rather than being drawn again from a hat and leaving a faith that bars women from land
        // with a female-only priesthood.
        string gender = GenderDoctrine(cfg.Gender, rng);

        var doctrines = new Dictionary<string, string>();
        foreach (string group in FilledGroups)
        {
            if (group == "hostility_group" && abrahamic)
            {
                doctrines[group] = AbrahamicHostility;
                continue;
            }

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

                "doctrine_theocracy" => Prefer(members, layClergy
                    ? ["doctrine_theocracy_lay_clergy"]
                    : ["doctrine_theocracy_temporal"], rng),

                // Lay clergy is appointed by the ruler in vanilla's own model, and the temporal
                // succession pair is what leaves doctrine_temporal_head pickable. Everyone else
                // draws from the whole group as before.
                "doctrine_clerical_succession" when layClergy => Prefer(members,
                    ["doctrine_clerical_succession_temporal_appointment",
                     "doctrine_clerical_succession_temporal_fixed_appointment"], rng),

                "doctrine_gender" => Prefer(members, [gender], rng),

                // Clergy of the sex the faith already favours, most of the time. An open
                // priesthood is the interesting exception rather than the rule, so it is what the
                // remaining fifth gets.
                "doctrine_clerical_gender" => Prefer(members, gender switch
                {
                    "doctrine_gender_female_dominated" => rng.Chance(0.8)
                        ? ["doctrine_clerical_gender_female_only"]
                        : ["doctrine_clerical_gender_either"],
                    "doctrine_gender_equal" => rng.Chance(0.8)
                        ? ["doctrine_clerical_gender_either"]
                        : ["doctrine_clerical_gender_male_only"],
                    _ => rng.Chance(0.8)
                        ? ["doctrine_clerical_gender_male_only"]
                        : ["doctrine_clerical_gender_either"],
                }, rng),

                _ => rng.Pick(members),
            };

            // A doctrine already taken can rule this one out — doctrine_no_head beside an
            // anointment rite is the pair this catches most — so a clashing pick is drawn again
            // from what is still compatible. Groups are visited in a fixed order, so the earlier
            // group wins; a group with nothing compatible left keeps its pick rather than being
            // dropped, because a faith missing a doctrine is worse than a faith with a contradiction.
            var others = doctrines.Where(d => d.Key != group).Select(d => d.Value).ToList();
            if (!vocab.Compatible(doctrines[group], others))
            {
                var free = members.Where(m => vocab.Compatible(m, others)).ToList();
                if (free.Count > 0) doctrines[group] = rng.Pick(free);
            }
        }

        // Read back rather than trusting the roll. The repair loop above may replace any pick, and
        // a temporal head beside temporal theocracy or a spiritually appointed clergy is exactly
        // what CK3's own can_pick forbids — so what the religion is recorded as having must be what
        // was written, not what was intended. Neither doctrine carries a can_pick in the current
        // install, so this changes nothing today; it is what keeps a patch that adds one from
        // quietly producing a faith the game would not have let a player build.
        layClergy = layClergy
            && doctrines.GetValueOrDefault("doctrine_theocracy") == "doctrine_theocracy_lay_clergy"
            && doctrines.GetValueOrDefault("doctrine_clerical_succession", "")
                .StartsWith("doctrine_clerical_succession_temporal", StringComparison.Ordinal);

        // Drawn here rather than in the initializer below so that when it is drawn is visible: it
        // shares the stream with everything else in this method, and a reader cannot tell where an
        // object initializer's members fall in that order.
        bool coronationCrown = rng.Chance(0.67);

        var localization = new List<(string, string)>();
        var text = new Dictionary<string, string>();
        BuildLocalization(key, language, monotheist, gender, vocab, rng, localization, text);

        return new Religion
        {
            Key = key,
            Name = UniqueFrom(() => language.Word(rng, 2, 3), usedNames),
            Language = language,
            GraphicalFaith = PickGraphicalFaith(vocab, cfg, monotheist, rng),
            Monotheist = monotheist,
            Abrahamic = abrahamic,
            LayClergy = layClergy,
            CoronationCrown = coronationCrown,
            Doctrines = doctrines,
            Virtues = Sample(vocab.Virtues, rng.Int(3, 5), rng),
            Sins = Sample(vocab.Sins, rng.Int(3, 5), rng),
            Localization = localization,
            LocalizationText = text,
        };
    }

    /// <summary>
    /// Only the <em>tags</em> of <see cref="VanillaVocabulary.ReligionLocTemplate"/> are reusable.
    /// Its values name the template religion's own gods, so any that survives into a generated
    /// religion verbatim is a vanilla deity — or a vanilla deity's pronoun — wearing the generated
    /// religion's clothes, identically in every religion on the map. The three kinds of value that
    /// used to leak through are handled up front here: the god-name lists, the pronoun sets and the
    /// pantheon's verb agreement.
    /// </summary>
    private static void BuildLocalization(string religionKey, Language language, bool monotheist,
        string gender, VanillaVocabulary vocab, Rng rng, List<(string, string)> into,
        Dictionary<string, string> text)
    {
        var words = new Dictionary<string, string>(StringComparer.Ordinal);
        var genders = new Dictionary<string, DeityGender>(StringComparer.Ordinal);
        var godLists = new List<int>();

        foreach (var (tag, value) in vocab.ReligionLocTemplate)
        {
            // Filled in below, once every deity's name key is known.
            if (tag is "GoodGodNames" or "EvilGodNames")
            {
                godLists.Add(into.Count);
                into.Add((tag, value));
                continue;
            }

            // "has" or "have", agreeing with this pantheon's size rather than the template's.
            if (tag is "PantheonTermHasHave")
            {
                into.Add((tag, monotheist ? "pantheon_term_has" : "pantheon_term_have"));
                continue;
            }

            string? suffix = PronounSuffixes.FirstOrDefault(s => tag.EndsWith(s, StringComparison.Ordinal));
            if (suffix is not null)
            {
                // Every tag for one deity shares a slot name, so the whole set is rolled once and
                // stays internally consistent: the war god cannot be "he" and "itself" at once.
                string slot = tag[..^suffix.Length];
                if (slot.EndsWith("Name", StringComparison.Ordinal)) slot = slot[..^"Name".Length];

                if (!genders.TryGetValue(slot, out var deity))
                    genders[slot] = deity = RollDeityGender(gender, rng);

                into.Add((tag, suffix switch
                {
                    "SheHe" => deity.SheHe,
                    "HerHis" => deity.HerHis,
                    "HerHim" => deity.HerHim,
                    "HerselfHimself" => deity.Self,
                    "MistressMaster" => deity.Title,
                    _ => deity.Parent,
                }));
                continue;
            }

            if (IsConstant(value)) { into.Add((tag, value)); continue; }

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

        foreach (int index in godLists)
        {
            var (tag, fallback) = into[index];
            string[] slots = tag is "GoodGodNames"
                ? monotheist ? MonotheistGoodGodSlots : GoodGodSlots
                : monotheist ? MonotheistEvilGodSlots : EvilGodSlots;

            var keys = slots
                .Select(s => $"{religionKey}_{s.ToLowerInvariant()}")
                .Where(text.ContainsKey)
                .ToList();

            // An empty list would be worse than the template's: CK3 picks from these at random and
            // has nothing to fall back on. Only reachable if the template drops the name tags.
            into[index] = keys.Count > 0 ? (tag, "{ " + string.Join(' ', keys) + " }") : (tag, fallback);
        }
    }

    private static bool IsConstant(string value)
        => value.StartsWith('{') || value.All(c => !char.IsLower(c));

    /// <summary>
    /// The pantheon slots a faith swears by and swears at. CK3 reads <c>GoodGodNames</c> and
    /// <c>EvilGodNames</c> as lists of localisation keys and picks one at random, so they have to
    /// name this religion's own gods. Monotheists get the short lists for the reason vanilla's do —
    /// there is one god to swear by, under its two names.
    /// </summary>
    private static readonly string[] GoodGodSlots =
    [
        "HighGodName", "HighGodNameAlternate", "CreatorName", "HealthGodName", "FertilityGodName",
        "WealthGodName", "HouseholdGodName", "FateGodName", "KnowledgeGodName", "WarGodName",
        "TricksterGodName", "NightGodName", "WaterGodName",
    ];

    private static readonly string[] MonotheistGoodGodSlots = ["HighGodName", "HighGodNameAlternate"];
    private static readonly string[] EvilGodSlots = ["DevilName", "DeathDeityName"];
    private static readonly string[] MonotheistEvilGodSlots = ["DevilName"];

    /// <summary>
    /// One deity's grammar. The four pronouns are CK3's own constants; the last two are the nouns
    /// the witch god's <c>MistressMaster</c> and <c>MotherFather</c> tags want, which vanilla keeps
    /// in step with that god's pronouns and so do we.
    /// </summary>
    private readonly record struct DeityGender(
        string SheHe, string HerHis, string HerHim, string Self, string Title, string Parent);

    private static readonly DeityGender[] DeityGenders =
    [
        new("CHARACTER_SHEHE_HE", "CHARACTER_HERHIS_HIS", "CHARACTER_HERHIM_HIM",
            "CHARACTER_HIMSELF", "master", "father"),
        new("CHARACTER_SHEHE_SHE", "CHARACTER_HERHIS_HER", "CHARACTER_HERHIM_HER",
            "CHARACTER_HERSELF", "mistress", "mother"),
        new("CHARACTER_SHEHE_THEY", "CHARACTER_HERHIS_THEIR", "CHARACTER_HERHIM_THEM",
            "CHARACTER_THEMSELF", "witch_spirit", "witch_source"),
        new("CHARACTER_SHEHE_IT", "CHARACTER_HERHIS_ITS", "CHARACTER_HERHIM_IT",
            "CHARACTER_ITSELF", "witch_spirit", "witch_source"),
    ];

    /// <summary>Longest first, so <c>HerselfHimself</c> is never read as <c>HerHis</c>.</summary>
    private static readonly string[] PronounSuffixes =
        ["HerselfHimself", "MistressMaster", "MotherFather", "SheHe", "HerHis", "HerHim"];

    /// <summary>
    /// She and he carry a pantheon between them; they and it are the rare god that is a force
    /// rather than a person, and split the last tenth. Leaned by the faith's own gender doctrine so
    /// a female-dominated religion reads as one from its flavour text and not only from its
    /// succession law.
    /// </summary>
    private static DeityGender RollDeityGender(string genderDoctrine, Rng rng)
    {
        (double male, double female) = genderDoctrine switch
        {
            "doctrine_gender_female_dominated" => (0.30, 0.60),
            "doctrine_gender_equal" => (0.45, 0.45),
            _ => (0.60, 0.30),
        };

        double roll = rng.Double();
        if (roll < male) return DeityGenders[0];
        if (roll < male + female) return DeityGenders[1];
        return DeityGenders[rng.Chance(0.5) ? 2 : 3];
    }

    /// <summary>
    /// The tenets a faith of this religion may draw.
    ///
    /// The six syncretism tenets each grant opinion with, and soften hostility towards, one vanilla
    /// religion family by name — Christian, Islamic, Jewish, Eastern, Sinitic, unreformed. None of
    /// the first five exists on a generated map, so a faith holding one carries a dead tenet in a
    /// slot that could have held a live one. They go under Shaped. The unreformed one stays for
    /// Abrahamic-shaped faiths only, which is who vanilla shows it to (its is_shown is "not
    /// pagan"), and unreformed faiths do exist here for it to point at. PaganOnly keeps the pool
    /// whole, as it always was.
    /// </summary>
    private static List<string> TenetPool(Religion religion, VanillaVocabulary vocab, MapConfig cfg)
    {
        IEnumerable<string> pool = vocab.Tenets;

        if (!cfg.AllowNaturalPrimitivism)
            pool = pool.Where(t => !t.Contains("natural_primitivism", StringComparison.OrdinalIgnoreCase));

        if (cfg.FaithShaping == MapConfig.FaithShape.Shaped)
            pool = pool.Where(t => !t.EndsWith("_syncretism", StringComparison.Ordinal)
                || (religion.Abrahamic && t == "tenet_unreformed_syncretism"));

        return pool.ToList();
    }

    /// <summary>
    /// Puts the great-holy-war tenet first for a faith that has just been given a head.
    ///
    /// <c>great_holy_wars_active</c> lives on exactly two tenets, and a head of faith without one
    /// is a title that never launches anything. The tenets the faith already holds are kept where
    /// the seed allows them — they may be what it shares with its sister faiths — and only the
    /// ones the seed rules out (pacifism, human sacrifice, gruesome festivals) are drawn again.
    /// No-op when the install lacks the tenet or the faith already holds one of the pair.
    /// </summary>
    private static void SeedWarTenet(Faith faith, bool temporal, VanillaVocabulary vocab,
        MapConfig cfg, Rng rng)
    {
        if (faith.Tenets.Contains(SpiritualWarTenet) || faith.Tenets.Contains(TemporalWarTenet)) return;

        string seed = temporal ? TemporalWarTenet : SpiritualWarTenet;
        if (!vocab.Tenets.Contains(seed)) return;

        var held = new List<string>(faith.Religion.Doctrines.Values) { seed };
        var kept = new List<string>();
        foreach (string tenet in faith.Tenets)
        {
            if (kept.Count == 2 || !vocab.Compatible(tenet, held)) continue;
            kept.Add(tenet);
            held.Add(tenet);
        }

        var rest = TenetPool(faith.Religion, vocab, cfg)
            .Where(t => t != seed && !kept.Contains(t)).ToList();
        faith.Tenets = [seed, .. kept, .. SampleCompatible(rest, 2 - kept.Count, held, vocab, rng)];
    }

    /// <summary>
    /// Makes a religion's other faiths read as heresies of its primary one: each keeps two of the
    /// primary faith's three tenets and draws one the primary does not hold. Vanilla's own
    /// siblings are built this way — Catholic and Orthodox share two tenets and differ in one —
    /// and without it two faiths of one religion could look nothing alike. Shaped only; under
    /// PaganOnly every faith keeps its independent draw, and nothing here touches the stream.
    /// </summary>
    private static void AlignSiblingTenets(Religion religion, Faith primary, IEnumerable<Faith> siblings,
        VanillaVocabulary vocab, MapConfig cfg, Rng rng)
    {
        if (cfg.FaithShaping != MapConfig.FaithShape.Shaped || primary.Tenets.Count < 3) return;

        var pool = TenetPool(religion, vocab, cfg).Where(t => !primary.Tenets.Contains(t)).ToList();

        foreach (var faith in siblings)
        {
            var kept = primary.Tenets.ToList();
            rng.Shuffle(kept);
            kept.RemoveAt(kept.Count - 1);

            var own = SampleCompatible(pool, 1, religion.Doctrines.Values.Concat(kept), vocab, rng);
            faith.Tenets = [.. kept, .. own];
        }
    }

    private static Faith CreateFaith(Religion religion, int index, VanillaVocabulary vocab,
        HashSet<string> usedNames, MapConfig cfg, Rng rng)
    {
        // Natural primitivism reads as a slur on a generated people rather than as flavour, so it is
        // filterable. Filtered here rather than out of the vocabulary itself because the vocabulary
        // is what the install actually has, and the rest of the program is entitled to see it whole.
        var pool = TenetPool(religion, vocab, cfg);

        return new Faith
        {
            Key = $"gen_faith_{index}",
            Name = UniqueFrom(() => religion.Language.Word(rng, 2, 3), usedNames),
            Religion = religion,
            Color = (rng.Decimal(0.1, 0.9), rng.Decimal(0.1, 0.9), rng.Decimal(0.1, 0.9)),
            Icon = vocab.FaithIcons.Count > 0 ? rng.Pick(vocab.FaithIcons) : "germanic",

            // Drawn against each other *and* against the religion's doctrines: CK3's own can_pick
            // rules forbid human sacrifice beside pacifism, or natural primitivism beside a faith
            // that criminalises witchcraft. The faith-creation screen would refuse both, and the
            // generator used to write them anyway because a scripted faith is never checked.
            Tenets = SampleCompatible(pool, 3, religion.Doctrines.Values, vocab, rng),
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

    /// <summary>
    /// The <c>doctrine_gender</c> key <see cref="MapConfig.Gender"/> asks for this time.
    ///
    /// Weighted rather than absolute even at the ends of the scale, because a world where every
    /// single faith answers the question the same way has nothing to notice about any of them: the
    /// two percent that lean the other way are what make the other ninety-eight legible as a
    /// choice. Equal is the middle rung on CK3's own scale and is never rare.
    /// </summary>
    private static string GenderDoctrine(GenderPreference preference, Rng rng)
    {
        double roll = rng.NextDouble();

        return preference switch
        {
            GenderPreference.FemaleDominated => roll switch
            {
                < 0.86 => "doctrine_gender_female_dominated",
                < 0.98 => "doctrine_gender_equal",
                _ => "doctrine_gender_male_dominated",
            },
            GenderPreference.Mixed => roll switch
            {
                < 0.42 => "doctrine_gender_male_dominated",
                < 0.72 => "doctrine_gender_equal",
                _ => "doctrine_gender_female_dominated",
            },
            _ => roll switch
            {
                < 0.86 => "doctrine_gender_male_dominated",
                < 0.98 => "doctrine_gender_equal",
                _ => "doctrine_gender_female_dominated",
            },
        };
    }

    /// <summary>
    /// Which way a faith leans, for everything downstream that has to agree with it — the culture
    /// on the same ground, the sex of the ruler who holds it, which of their children inherits.
    ///
    /// Answers <c>doctrine_gender_male_dominated</c> for a faith whose religion never got the
    /// group, which is what an install missing the doctrine and the wilderness faith both look
    /// like, and is the answer that changes nothing.
    /// </summary>
    public static string GenderOf(Faith faith)
        => faith.Religion.Doctrines.GetValueOrDefault("doctrine_gender", "doctrine_gender_male_dominated");

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

    /// <summary>
    /// <see cref="Sample"/>, refusing anything CK3 would not let sit beside what is already held —
    /// the doctrines in <paramref name="alongside"/> and the earlier picks themselves. Greedy over
    /// one shuffle rather than a search: the tenet pool is seventy-odd wide against three picks, so
    /// the first compatible run is always found, and a short draw means the install harvested
    /// almost nothing rather than that the constraints were tight.
    /// </summary>
    private static List<string> SampleCompatible(List<string> pool, int count,
        IEnumerable<string> alongside, VanillaVocabulary vocab, Rng rng)
    {
        if (pool.Count == 0) return [];

        var copy = pool.ToList();
        rng.Shuffle(copy);

        var held = new List<string>(alongside);
        var picked = new List<string>();

        foreach (string candidate in copy)
        {
            if (picked.Count == count) break;
            if (!vocab.Compatible(candidate, held)) continue;

            picked.Add(candidate);
            held.Add(candidate);
        }

        return picked;
    }

    /// <summary>A fresh draw until one is free: a collision costs a re-roll, not a numeral on the map.</summary>
    private static string UniqueFrom(Func<string> draw, HashSet<string> used)
    {
        string name = draw();
        for (int attempt = 0; attempt < 16 && used.Contains(name); attempt++) name = draw();
        return Unique(name, used);
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
        int abrahamic = religions.Count(r => r.Abrahamic);
        int layClergy = religions.Count(r => r.LayClergy);
        int heads = faiths.Count(f => f.Head is not null);
        int temporal = faiths.Count(f => f.Head is { Temporal: true });
        int unreformed = faiths.Count(f => !f.IsOrganized);
        var sizes = faiths.Select(f => f.Counties.Count).OrderBy(n => n).ToList();

        Console.WriteLine($"  faiths: {faiths.Count} in {religions.Count} religions " +
                          $"({monotheist} monotheist, {abrahamic} Abrahamic-shaped, {layClergy} lay clergy, " +
                          $"{unreformed} unreformed, {heads} heads of faith, {temporal} temporal) " +
                          $"over {counties} counties — " +
                          $"smallest {sizes[0]}, median {sizes[sizes.Count / 2]}, largest {sizes[^1]} counties " +
                          $"({elapsedMs} ms)");
    }
}