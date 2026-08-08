using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>A generated religion: a liturgical language, a doctrine baseline, and its faiths.</summary>
public sealed class Religion
{
    public required string Key { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// The tongue its gods are named in — deliberately not any culture's vernacular. A religion
    /// crosses culture borders, so naming its gods in one member culture's language would make the
    /// other members look like colonies of it.
    /// </summary>
    public required Language Language { get; init; }

    public required string GraphicalFaith { get; init; }
    public required bool Monotheist { get; init; }

    /// <summary>Doctrines shared by every faith in the religion, as `group = doctrine`.</summary>
    public required Dictionary<string, string> Doctrines { get; init; }

    public required List<string> Virtues { get; init; }
    public required List<string> Sins { get; init; }

    /// <summary>The religion-level localization block, already resolved to key/value pairs.</summary>
    public required List<(string Tag, string Value)> Localization { get; init; }

    /// <summary>Generated loc keys this religion introduces, and the text behind them.</summary>
    public required Dictionary<string, string> LocalizationText { get; init; }

    public List<Faith> Faiths { get; } = [];
}

/// <summary>One faith of a religion, and the ground that holds it.</summary>
public sealed class Faith
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required Religion Religion { get; init; }
    public required (double R, double G, double B) Color { get; init; }
    public required string Icon { get; init; }

    /// <summary>Its three core tenets — the doctrines a player actually reads off the faith screen.</summary>
    public required List<string> Tenets { get; init; }

    public List<Title> Counties { get; } = [];

    /// <summary>Holy site keys, resolved to counties this faith holds.</summary>
    public List<(string Key, Title County)> HolySites { get; } = [];
}

/// <summary>The finished religious geography.</summary>
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

/// <summary>
/// Grows religions and faiths over the same county graph the cultures used.
///
/// The one thing this deliberately does *not* do is follow the culture map. Seeds are placed
/// independently and the frontier is weighted differently — see
/// <see cref="MapConfig.FaithTerrainWeight"/>, which is lower than the culture equivalent, so a
/// faith crosses a mountain range a language would have stopped at. That mismatch is the entire
/// point. A world where every culture border is also a faith border reads as a world with one
/// axis of difference; the interesting map is the one where a kingdom holds two faiths, or where
/// one faith spans four languages and its adherents have that in common and nothing else. Every
/// religious war CK3 can generate lives in the gap between the two maps.
///
/// Religions are coarser than cultures, roughly matching vanilla's ratio: it ships ~193 cultures
/// against ~120 faiths in ~48 religions.
/// </summary>
public static class Faiths
{
    /// <summary>
    /// Doctrine groups a generated faith fills, and nothing else.
    ///
    /// Vanilla's group list also holds religion-specific machinery — the Muslim succession split,
    /// the Jewish temple authorities, the Zoroastrian branches — which exist to model one real
    /// religion each and mean nothing on an invented one. The special single-doctrine groups
    /// (is_christian_faith, has_jizya_doctrine and friends) are opt-in flags and are left off, which
    /// is what vanilla's own pagan religions do.
    ///
    /// Members are read from the install rather than listed here; only the choice of *which groups
    /// to answer* is ours.
    /// </summary>
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

    /// <summary>
    /// Doctrines forced regardless of what the group offers.
    ///
    /// <c>doctrine_no_head</c> is the load-bearing one. Taking a spiritual or temporal head instead
    /// obliges the faith to name a <c>religious_head</c> title that actually exists, and CK3 does
    /// not warn when it does not — it just leaves the faith holding a null title. Minting head-of-
    /// faith titles is a piece of work in its own right, so until it is done every generated faith
    /// is headless, which is both safe and the commonest arrangement among vanilla's pagans anyway.
    ///
    /// The hostility doctrine is pinned to the pagan one to match the family; see
    /// <see cref="Family"/>.
    /// </summary>
    private static readonly Dictionary<string, string> ForcedDoctrines = new()
    {
        ["doctrine_head_of_faith"] = "doctrine_no_head",
        ["hostility_group"] = "pagan_hostility_doctrine",
    };

    /// <summary>
    /// Every generated religion is scripted as a pagan-family one.
    ///
    /// Not for flavour — the family drives which hostility doctrine is legal and which great holy
    /// war content applies, and the Abrahamic family in particular assumes machinery (heads of
    /// faith, crusade targets) a generated world has none of. Doctrines carry the actual variety a
    /// player reads: whether the faith is monotheist, how it treats outsiders, what it makes a crime.
    /// </summary>
    public const string Family = "rf_pagan";

    public static FaithMap Build(List<Title> empires, ProvinceMap provinces, int[] order,
        int landCount, TerrainClass[] provinceTerrain, Dictionary<Title, int> development,
        VanillaVocabulary vocab, MapConfig cfg, Rng rng)
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

        for (int r = 0; r < religionTarget; r++)
        {
            var members = all.Where(i => religionOf[i] == r).ToList();
            if (members.Count == 0) continue;

            var religion = CreateReligion(religions.Count, vocab, usedNames, rng);
            religions.Add(religion);

            int within = Math.Max(1, (int)Math.Round(members.Count / cfg.CountiesPerFaith));
            var faithOf = RegionGrowth.Partition(graph, members, within, rng, out _);

            for (int f = 0; f < within; f++)
            {
                var owned = members.Where(i => faithOf[i] == f).Select(i => counties[i]).ToList();
                if (owned.Count == 0) continue;

                var faith = CreateFaith(religion, faiths.Count, vocab, usedNames, rng);
                religion.Faiths.Add(faith);
                faiths.Add(faith);
                faith.Counties.AddRange(owned);
                foreach (var county in owned) byCounty[county] = faith;

                PlaceHolySites(faith, development, cfg.HolySitesPerFaith);
            }
        }

        Report(religions, faiths, counties.Count, sw.ElapsedMilliseconds);
        return new FaithMap { Religions = religions, Faiths = faiths, ByCounty = byCounty };
    }

    /// <summary>
    /// The same county graph the cultures grew on, but with terrain flattened by a smaller weight.
    ///
    /// Kept as its own function rather than shared with <see cref="Cultures"/> because the two are
    /// only incidentally the same shape: what stops a language and what stops a creed are different
    /// forces, and the day one of them wants to follow rivers or trade routes instead, it should be
    /// free to without disturbing the other.
    /// </summary>
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

    /// <summary>
    /// How much a terrain slows a creed down. Flatter than the culture figures across the board,
    /// and pointedly cheap along coasts: a faith travels with merchants and missionaries, which is
    /// how the real ones crossed seas long before the languages behind them did.
    /// </summary>
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

    private static Religion CreateReligion(int index, VanillaVocabulary vocab,
        HashSet<string> usedNames, Rng rng)
    {
        var language = Language.Create($"religion_tongue_{index}", rng);
        string key = $"gen_religion_{index}";
        bool monotheist = rng.Chance(0.35);

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

                // A monotheism that tolerates everything and a polytheism that tolerates nothing
                // are both possible, just less usual — the weighting says so without forbidding it.
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

    /// <summary>
    /// Fills the religion's localization block by walking a real one's tag list.
    ///
    /// Two kinds of tag are passed through untouched. The obvious ones are values in shouting case
    /// — CHARACTER_HERHIS_HIS and its family — which are engine vocabulary rather than content. The
    /// less obvious ones are the *grammatical* tags, which have to be recognised by tag name and
    /// not by the case of their value: several vanilla religions point SheHe and HerHis at ordinary
    /// lowercase keys like `paganism_devil_shehe`, and a case test alone replaces those with an
    /// invented word, so the game renders a god's name where a pronoun belongs.
    ///
    /// Everything else is a name this religion should have its own word for, so it gets a generated
    /// key and a word out of the liturgical language.
    ///
    /// Possessives and plurals are derived from the tag they belong to rather than generated
    /// independently, so the god named in one line is recognisably the same god in the next.
    /// </summary>
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

    /// <summary>
    /// Whether a template value is engine vocabulary to pass through rather than content to replace.
    /// Vanilla writes those in upper case throughout, and a multi-value `{ A B }` list is always
    /// one of them.
    /// </summary>
    private static bool IsConstant(string value)
        => value.StartsWith('{') || value.All(c => !char.IsLower(c));

    /// <summary>
    /// Tags that select a pronoun or a kinship word rather than naming anything. A closed set, and
    /// recognised by the tag rather than by its value — see <see cref="BuildLocalization"/>.
    /// </summary>
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
        // Faiths of one religion should read as variants of each other, so the name comes from the
        // religion's own tongue rather than from a fresh one.
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

    /// <summary>
    /// Holy sites go to the faith's richest counties.
    ///
    /// Development is the closest thing the generator has to "somewhere that matters" — it already
    /// encodes terrain, coastal access and size — and putting the shrines in the backwaters would
    /// make every holy war a fight over nothing. Fewer than the requested number is fine and
    /// happens on small faiths; a site pointing at a county that does not exist is not.
    /// </summary>
    private static void PlaceHolySites(Faith faith, Dictionary<Title, int> development, int count)
    {
        var ranked = faith.Counties
            .OrderByDescending(c => development.GetValueOrDefault(c))
            .ThenBy(c => c.Index)
            .Take(Math.Max(1, count));

        int n = 0;
        foreach (var county in ranked)
            faith.HolySites.Add(($"{faith.Key}_site_{n++}", county));
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
        var sizes = faiths.Select(f => f.Counties.Count).OrderBy(n => n).ToList();

        Console.WriteLine($"  faiths: {faiths.Count} in {religions.Count} religions " +
                          $"({monotheist} monotheist) over {counties} counties — " +
                          $"median {sizes[sizes.Count / 2]}, largest {sizes[^1]} counties " +
                          $"({elapsedMs} ms)");
    }
}
