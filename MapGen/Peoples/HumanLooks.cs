using System.Globalization;
using System.Text.RegularExpressions;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using static Ck3MapGen.Config.MapConfig;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Where the world's humans get their faces: which vanilla look each people wears, chosen for the
/// ground it lives on, with the four broad looks kept in an even split across the world.
///
/// **What this replaced.** A human heritage used to draw its vanilla template at random: one of the
/// four families uniformly, then a template inside it. Nothing read the map. An audit of seed 478829
/// found papuan, east_african and south_indian peoples at 45–67° dressed in Sámi and Ugric furs, and
/// a circumpolar people on the equator in African dress. The clothing had been fitted to climate for
/// a while (<see cref="ClothingClimate"/>); the faces under it had not. Humans also carried our own
/// hair and eye palettes over the template's, and those were measurably wrong: the eye swatches ran
/// 1.5–2x brighter than vanilla's, and "Black" and "BlueBlack" hair sit at luminance 6 and 4, so
/// cultures whose two variants leant on them had no visible hair variety at all.
///
/// **What it does now**, in three layers:
///
/// 1. <b>Heritages are placed.</b> The human heritages are dealt an even hand of the four families
///    (or the preset's templates, in the preset's proportions), and a local search swaps hands
///    between heritages until each family sits where its templates' home climates fit, neighbouring
///    peoples tend to share a family, and each family's share of the LAND stays near its share of
///    heritages. Swaps only permute the hand, so the split by heritage count is exact whatever the
///    map — the even split is a guarantee, climate decides only who gets what. The families are
///    what a player sees, which is why the balance is over families and not over the twenty-odd
///    vanilla templates: Asia has nine templates and Africa three, and a flat draw over templates
///    would make a third of every world East Asian.
/// 2. <b>A template is chosen inside the family</b> by climate — circumpolar in the taiga, the
///    Mediterranean on dry-summer coasts, Tibetan on cold uplands — and a culture whose own ground
///    fits its heritage's template badly takes a better-fitting sibling from the same family, the
///    kin-first rule the clothing uses.
/// 3. <b>Each culture is dressed from vanilla.</b> Colouring is vanilla's own: a culture's list is
///    one real vanilla people's weighted mix over that template's variants (Swedish in the cold,
///    French in the temperate west — whichever vanilla people's dress suits the culture's climate),
///    so hair and eyes are never ours again. On top of it each culture gets a window of the
///    template's own skin range, lighter or darker by how much warmer or colder it lives than the
///    template's home, and a few gene "leans" — vanilla's own weighted curve for, say, the jaw, with
///    its weight tipped toward one side. Three leans are shared by a heritage, one is the culture's
///    own, so kin peoples look like kin and neighbours do not look identical.
///
/// Nothing here writes a value vanilla does not already write for that template: skin windows are
/// sub-rectangles of the template's own rectangles, leans reweight the template's own entries and
/// never move a range, and every key a culture points at is a real vanilla ethnicity. That is what
/// keeps inheritance well behaved across mixed marriages — the lesson <see cref="RaceSkin"/> learnt
/// the hard way.
/// </summary>
public static class HumanLooks
{
    /// <summary>A vanilla template a people can be placed on: its look family and the climate of the
    /// real-world homeland it was drawn for.</summary>
    /// <remarks><see cref="Prior"/> scales how often a template is chosen among those that fit. The
    /// Tibetan look is the one below 1: its home is cold and dry because it is HIGH, and climate
    /// alone cannot tell a plateau from the tundra, so without it every lowland Arctic people in the
    /// Asian family came out Tibetan.</remarks>
    public sealed record Root(string Key, string Family, ClothingClimate.Climate Home, double Prior = 1.0,
        bool Evidence = true);

    /// <summary>
    /// Every vanilla human template, grouped into the four families, with the climate it was made
    /// for. Figures are rounded climatologies of the homeland (coldest month, warmest month, annual
    /// rain), on the same scale <see cref="ClothingClimate"/> uses for dress so the two agree.
    ///
    /// Papuan sits with the African family: the family is what the even split is over, and a split
    /// that counts Melanesians as East Asians hands the wet tropics to the Asian quota. Every family
    /// spans more than one climate on purpose — the European one runs from the Sámi to the Aegean,
    /// the Asian one from Ainu to Malay — so an even split never has to force a family onto ground
    /// none of its templates suits. The African family is the narrowest, and gets the hottest land.
    /// </summary>
    public static IReadOnlyList<Root> Roots { get; } =
    [
        new("circumpolar", "caucasian", new(-14, 14, 450)),
        new("slavic", "caucasian", new(-7, 19, 600)),
        new("caucasian", "caucasian", new(1, 17, 750)),
        new("byzantine", "caucasian", new(6, 25, 600)),
        new("mediterranean", "caucasian", new(9, 25, 600)),

        new("african", "african", new(24, 28, 1300)),
        new("east_african", "african", new(19, 25, 700)),
        new("papuan", "african", new(26, 27, 3000)),

        new("asian_emishi_ainu", "asian", new(-6, 20, 1100)),
        new("asian_mongol", "asian", new(-20, 19, 250)),
        new("asian_manchu_korean", "asian", new(-12, 23, 650)),
        new("asian_tibetan", "asian", new(-8, 13, 450), Prior: 0.25, Evidence: false),
        new("asian_han_chinese", "asian", new(1, 27, 800)),
        new("asian_japanese", "asian", new(4, 26, 1500)),
        new("asian", "asian", new(8, 26, 1300)),
        new("asian_austronesian", "asian", new(25, 28, 2200)),
        new("asian_malay", "asian", new(26, 28, 2500)),

        new("turkic", "mena", new(-10, 23, 280)),
        new("turkic_west", "mena", new(-2, 24, 400)),
        new("arab", "mena", new(14, 33, 120)),
        new("indian", "mena", new(15, 31, 900)),
        new("south_indian", "mena", new(24, 30, 1300)),
    ];

    public static readonly string[] Families = ["caucasian", "african", "asian", "mena"];

    private static readonly Dictionary<string, Root> RootByKey =
        Roots.ToDictionary(r => r.Key, StringComparer.Ordinal);

    public static Root? RootInfo(string key) => RootByKey.GetValueOrDefault(key);

    /// <summary>The look family of a template, or "caucasian" for anything unknown.</summary>
    public static string FamilyOf(string template)
        => RootByKey.TryGetValue(RootOf(template) ?? template, out var r) ? r.Family : "caucasian";

    /// <summary>
    /// The template a vanilla ethnicity key colours: <c>caucasian_northern_blond</c> is a caucasian,
    /// <c>slavic_dark_hair</c> a slavic. Null for keys no people should be pointed at — the
    /// ruler-designer and vanity entries, and <c>caucasian_base</c>, which carries no colouring.
    /// </summary>
    public static string? RootOf(string key)
    {
        if (RootByKey.ContainsKey(key)) return key;
        if (key.Contains("ruler_designer", StringComparison.Ordinal) || key.EndsWith("_base", StringComparison.Ordinal))
            return null;

        foreach (string family in (string[])["caucasian", "slavic", "circumpolar", "mediterranean"])
            if (key.StartsWith(family + "_", StringComparison.Ordinal)) return family;

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Vanilla data
    // ---------------------------------------------------------------------------------------------

    /// <summary>One vanilla ethnicity with its template chain resolved: every colour and gene block
    /// it ends up with, whether written on it or inherited.</summary>
    public sealed class VanillaEthnicity
    {
        public required string Key { get; init; }
        public Dictionary<string, List<ColorPaletteRange>> Colors { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<GeneMorphEntry>> Genes { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A real vanilla people's ethnicity list, and the climate its dress was made for.</summary>
    public sealed record Recipe(string Source, List<(int Weight, string Key)> Entries, ClothingClimate.Climate? Home);

    public sealed class Data
    {
        public Dictionary<string, VanillaEthnicity> Ethnicities { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<Recipe>> RecipesByRoot { get; } = new(StringComparer.Ordinal);

        private readonly Dictionary<string, List<ClothingClimate.Climate>> _evidence = new(StringComparer.Ordinal);

        /// <summary>The climates at least two vanilla peoples on this template are dressed for.</summary>
        public List<ClothingClimate.Climate> EvidenceHomes(string root)
        {
            lock (_evidence)
            {
                if (_evidence.TryGetValue(root, out var cached)) return cached;
                var homes = RecipesByRoot.TryGetValue(root, out var recipes)
                    ? recipes.Where(r => r.Home is not null).GroupBy(r => r.Home!.Value)
                        .Where(g => g.Count() >= 2).Select(g => g.Key).ToList()
                    : [];
                _evidence[root] = homes;
                return homes;
            }
        }
    }

    private static readonly Dictionary<string, Data> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock CacheLock = new();

    /// <summary>
    /// Vanilla's ethnicities and its peoples' mixes of them, read from the install. Harvested rather
    /// than transcribed for the same reason <see cref="VanillaVocabulary"/> is: a gene curve copied
    /// by hand goes stale on the next patch, and a lean written against a stale curve would quietly
    /// override the new one.
    /// </summary>
    public static Data Read(string gameDir, VanillaVocabulary? vocabulary)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(gameDir, out var cached)) return cached;

            var data = new Data();
            ReadEthnicities(Path.Combine(gameDir, "common", "ethnicities"), data);
            if (vocabulary is not null) ReadRecipes(vocabulary, data);

            Cache[gameDir] = data;
            return data;
        }
    }

    private static readonly HashSet<string> ColorKeys = ["skin_color", "eye_color", "hair_color"];

    private static void ReadEthnicities(string dir, Data data)
    {
        if (!Directory.Exists(dir)) return;

        var raw = new Dictionary<string, (ScriptNode Node, Dictionary<string, string> Consts)>(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
        {
            ScriptNode root;
            try { root = ScriptTree.ParseFile(path); }
            catch (Exception) { continue; }

            // @constants are file-local in the engine; every vanilla file that uses one declares it.
            var consts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in root.Children!)
                if (c.Key.StartsWith('@') && c.Value is not null) consts[c.Key] = c.Value;

            foreach (var c in root.Children!)
                if (c.IsBlock && c.Key.Length > 0 && !c.Key.StartsWith('@'))
                    raw[c.Key] = (c, consts);
        }

        var resolving = new HashSet<string>(StringComparer.Ordinal);

        VanillaEthnicity? Resolve(string key)
        {
            if (data.Ethnicities.TryGetValue(key, out var done)) return done;
            if (!raw.TryGetValue(key, out var entry) || !resolving.Add(key)) return null;

            var eth = new VanillaEthnicity { Key = key };
            if (entry.Node.Get("template") is { } template && Resolve(template) is { } parent)
            {
                foreach (var (k, v) in parent.Colors) eth.Colors[k] = v;
                foreach (var (k, v) in parent.Genes) eth.Genes[k] = v;
            }

            float Num(string token)
                => float.Parse(entry.Consts.TryGetValue(token, out string? v) ? v : token, CultureInfo.InvariantCulture);

            foreach (var block in entry.Node.Children!.Where(n => n.IsBlock && n.Key.Length > 0))
            {
                try
                {
                    if (ColorKeys.Contains(block.Key))
                    {
                        var rects = block.Children!
                            .Where(e => e.IsBlock && int.TryParse(e.Key, out _) && e.Bare.Count == 4)
                            .Select(e => new ColorPaletteRange
                            {
                                Weight = int.Parse(e.Key, CultureInfo.InvariantCulture),
                                X1 = Num(e.Bare[0]), Y1 = Num(e.Bare[1]), X2 = Num(e.Bare[2]), Y2 = Num(e.Bare[3])
                            }).ToList();
                        if (rects.Count > 0) eth.Colors[block.Key] = rects;
                        continue;
                    }

                    var entries = block.Children!
                        .Where(e => e.IsBlock && int.TryParse(e.Key, out _) && e.Get("name") is not null
                                    && e.First("range") is { } r && r.Bare.Count == 2)
                        .Select(e =>
                        {
                            var range = e.First("range")!.Bare;
                            return new GeneMorphEntry
                            {
                                Weight = int.Parse(e.Key, CultureInfo.InvariantCulture),
                                SubGeneName = e.Get("name")!,
                                Min = Num(range[0]),
                                Max = Num(range[1])
                            };
                        }).ToList();
                    if (entries.Count > 0) eth.Genes[block.Key] = entries;
                }
                catch (FormatException)
                {
                    // An unresolvable constant: leave the inherited block in place rather than
                    // guess at it.
                }
            }

            resolving.Remove(key);
            data.Ethnicities[key] = eth;
            return eth;
        }

        foreach (string key in raw.Keys.OrderBy(k => k, StringComparer.Ordinal)) Resolve(key);
    }

    private static readonly Regex RecipeEntry = new(@"(\d+)\s*=\s*([A-Za-z0-9_]+)", RegexOptions.Compiled);

    /// <summary>
    /// Every vanilla people whose list stays inside one template — Swedes over the four caucasian
    /// hair variants, Komi over two circumpolar ones — filed under that template with the climate
    /// its dress was made for. A people that mixes templates (an Andalusi 60/40 arab/mediterranean)
    /// is not a recipe for either; the mixing here is done by placement, not borrowed.
    /// </summary>
    private static void ReadRecipes(VanillaVocabulary vocabulary, Data data)
    {
        foreach (var look in vocabulary.Looks)
        {
            var entries = RecipeEntry.Matches(look.Ethnicities)
                .Select(m => (Weight: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Key: m.Groups[2].Value))
                .Where(e => e.Weight > 0)
                .ToList();
            if (entries.Count == 0) continue;

            var roots = entries.Select(e => RootOf(e.Key)).Distinct().ToList();
            if (roots.Count != 1 || roots[0] is not { } root) continue;
            if (data.Ethnicities.Count > 0 && entries.Any(e => !data.Ethnicities.ContainsKey(e.Key))) continue;

            if (!data.RecipesByRoot.TryGetValue(root, out var list))
                data.RecipesByRoot[root] = list = [];
            list.Add(new Recipe(look.SourceCulture, entries, ClothingClimate.HomeOf(look.ClothingGfx)));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Placement
    // ---------------------------------------------------------------------------------------------

    /// <summary>What placement needs from the world: each culture's climate and each county's place.</summary>
    public sealed record Inputs(
        ClothingClimate.Climate[]? ProvinceClimate,
        Func<Title, (double X, double Y)?>? CountyPosition,
        Data? Data);

    /// <summary>
    /// Kept on the <see cref="EthnicityMap"/> so the editor can dress a retemplated culture by the
    /// same rules generation used.
    /// </summary>
    public sealed class Context
    {
        public required Data? Data { get; init; }
        public required ClothingClimate.Climate[]? ProvinceClimate { get; init; }
        public required int Seed { get; init; }

        public ClothingClimate.Climate? ClimateOf(Culture culture)
            => ClothingClimate.Of(culture.Counties, ProvinceClimate);
    }

    /// <summary>A share of the world's peoples: a family (Varied) or one preset template.</summary>
    private sealed record Group(string Name, string Family, string[] Templates, double Target);

    private sealed class Unit
    {
        public required Heritage Heritage { get; init; }
        public required List<Culture> Cultures { get; init; }
        public required ClothingClimate.Climate? Climate { get; init; }
        public required double Land { get; init; }
        public required (double X, double Y)? Pos { get; init; }
        public double[] Misfit = [];
        public int Group;
    }

    private static List<Group> GroupsFor(HumanLook look, IReadOnlyList<(string Template, int Weight)> preset)
    {
        if (look == HumanLook.Varied || preset.Count == 0)
            return Families.Select(f => new Group(f, f,
                Roots.Where(r => r.Family == f).Select(r => r.Key).ToArray(), 1.0 / Families.Length)).ToList();

        double total = preset.Sum(p => p.Weight);
        return preset.Select(p => new Group(p.Template, FamilyOf(p.Template), [p.Template], p.Weight / total)).ToList();
    }

    /// <summary>How far one heritage's neighbours reach, in nearest heritages.</summary>
    private const int Neighbours = 3;

    /// <summary>Cost of a neighbouring pair wearing different families, against <see cref="Misfit"/>.</summary>
    private const double NeighbourCost = 0.25;

    /// <summary>
    /// Cost of a family's land share straying from its target, per heritage and per squared unit of
    /// share. At 4 a family holding 40% of the land where 25% is its due costs about a tenth of a
    /// heritage's worth per heritage — enough to stop one family taking a continent because its
    /// heritages happen to be the big ones, not enough to move a people into a climate it misfits.
    /// </summary>
    private const double LandBalanceCost = 4.0;

    /// <summary>Below this fit a culture's own ground is wrong for its heritage's template.</summary>
    private const double KeepFit = 0.30;

    /// <summary>
    /// Places every human people and returns, per culture, the template it wears. Cultures that
    /// hold no land (<paramref name="ghosts"/>) take their heritage's template without counting
    /// toward anything.
    /// </summary>
    public static Dictionary<Culture, string> Place(
        IReadOnlyList<Heritage> heritages, IReadOnlyList<Culture> humanCultures, ISet<Culture> ghosts,
        Func<Culture, int> landOf, HumanLook look, IReadOnlyList<(string Template, int Weight)> preset,
        Inputs inputs, int seed, Action<string> say)
    {
        var rng = Rng.For(seed, 0x4C0C, 0);
        var groups = GroupsFor(look, preset);

        ClothingClimate.Climate? ClimateOf(IEnumerable<Culture> cs)
            => ClothingClimate.Of(cs.SelectMany(c => c.Counties), inputs.ProvinceClimate);

        (double X, double Y)? PosOf(IEnumerable<Culture> cs)
        {
            if (inputs.CountyPosition is null) return null;
            double x = 0, y = 0; int n = 0;
            foreach (var county in cs.SelectMany(c => c.Counties))
                if (inputs.CountyPosition(county) is { } p) { x += p.X; y += p.Y; n++; }
            return n == 0 ? null : (x / n, y / n);
        }

        var units = new List<Unit>();
        foreach (var heritage in heritages)
        {
            var members = humanCultures.Where(c => c.Heritage == heritage && !ghosts.Contains(c)).ToList();
            if (members.Count == 0) continue;

            var climate = ClimateOf(members);
            units.Add(new Unit
            {
                Heritage = heritage,
                Cultures = members,
                Climate = climate,
                Land = Math.Max(1, members.Sum(landOf)),
                Pos = PosOf(members),
                Misfit = groups.Select(g => climate is { } at
                    ? Misfit(g.Templates.Max(t => TemplateFit(t, at, inputs.Data)))
                    : 0.0).ToArray()
            });
        }

        var result = new Dictionary<Culture, string>();
        if (units.Count == 0) return result;

        int n = units.Count;
        double meanLand = units.Average(u => u.Land);
        double totalLand = units.Sum(u => u.Land);
        var weightOf = units.Select(u => Math.Sqrt(u.Land / meanLand)).ToArray();

        // The hand: floor(target * n) of each group, the remainders to the largest fractional parts,
        // ties broken in a shuffled group order so no family is always the one that gets the spare.
        var order = Enumerable.Range(0, groups.Count).ToList();
        rng.Shuffle(order);
        var quota = groups.Select(g => (int)Math.Floor(g.Target * n)).ToArray();
        foreach (int g in order.OrderByDescending(g => groups[g].Target * n - quota[g]).Take(n - quota.Sum()))
            quota[g]++;
        var floor = groups.Select(g => (int)Math.Floor(g.Target * n)).ToArray();
        var ceil = groups.Select(g => (int)Math.Ceiling(g.Target * n)).ToArray();

        // Deal the hand greedily, biggest peoples first, each to its best-fitting group with room.
        var left = (int[])quota.Clone();
        foreach (int u in Enumerable.Range(0, n).OrderByDescending(u => units[u].Land).ThenBy(u => u))
        {
            int best = -1;
            for (int g = 0; g < groups.Count; g++)
                if (left[g] > 0 && (best < 0 || units[u].Misfit[g] < units[u].Misfit[best])) best = g;
            units[u].Group = best;
            left[best]--;
        }

        // Neighbours: each heritage's nearest few, as undirected pairs.
        var pairs = new HashSet<(int, int)>();
        for (int u = 0; u < n; u++)
        {
            if (units[u].Pos is not { } pu) continue;
            foreach (int v in Enumerable.Range(0, n)
                         .Where(v => v != u && units[v].Pos is not null)
                         .OrderBy(v => Dist2(pu, units[v].Pos!.Value)).ThenBy(v => v)
                         .Take(Neighbours))
                pairs.Add(u < v ? (u, v) : (v, u));
        }
        var pairList = pairs.OrderBy(p => p.Item1).ThenBy(p => p.Item2).ToList();

        double Cost()
        {
            double j = 0;
            var land = new double[groups.Count];
            for (int u = 0; u < n; u++)
            {
                j += weightOf[u] * units[u].Misfit[units[u].Group];
                land[units[u].Group] += units[u].Land;
            }
            foreach (var (a, b) in pairList)
                if (groups[units[a].Group].Family != groups[units[b].Group].Family) j += NeighbourCost;
            for (int g = 0; g < groups.Count; g++)
            {
                double d = land[g] / totalLand - groups[g].Target;
                j += LandBalanceCost * n * d * d;
            }
            return j;
        }

        // Local search: swap two peoples' groups, or move one to a group with room inside the
        // floor/ceiling band, whenever it lowers the cost. Deterministic, and bounded — it settles in
        // a handful of passes on any real map.
        var count = new int[groups.Count];
        foreach (var u in units) count[u.Group]++;
        double current = Cost();
        for (int pass = 0; pass < 40; pass++)
        {
            bool improved = false;
            for (int a = 0; a < n; a++)
            {
                for (int b = a + 1; b < n; b++)
                {
                    if (units[a].Group == units[b].Group) continue;
                    (units[a].Group, units[b].Group) = (units[b].Group, units[a].Group);
                    double c = Cost();
                    if (c < current - 1e-9) { current = c; improved = true; }
                    else (units[a].Group, units[b].Group) = (units[b].Group, units[a].Group);
                }

                int from = units[a].Group;
                for (int g = 0; g < groups.Count; g++)
                {
                    if (g == from || count[g] + 1 > ceil[g] || count[from] - 1 < floor[from]) continue;
                    units[a].Group = g;
                    double c = Cost();
                    if (c < current - 1e-9)
                    {
                        current = c; improved = true;
                        count[from]--; count[g]++;
                        from = g;
                    }
                    else units[a].Group = from;
                }
            }
            if (!improved) break;
        }

        // A template for each people, inside its group, by climate; then each culture keeps it or
        // takes a better-fitting sibling of the same family where its own ground is wrong for it.
        int suited = 0, placedCultures = 0;
        double fitSum = 0;
        foreach (var unit in units)
        {
            var group = groups[unit.Group];
            string template = PickByFit(group.Templates, unit.Climate, inputs.Data, rng);

            var siblings = look == HumanLook.Varied
                ? group.Templates
                : groups.Where(g => g.Family == group.Family).SelectMany(g => g.Templates).ToArray();

            foreach (var culture in unit.Cultures)
            {
                string mine = template;
                if (ClothingClimate.Of(culture.Counties, inputs.ProvinceClimate) is { } at)
                {
                    double kept = TemplateFit(template, at, inputs.Data);
                    if (kept < KeepFit)
                    {
                        var better = siblings.Where(t => TemplateFit(t, at, inputs.Data) > kept).ToArray();
                        if (better.Length > 0) mine = PickByFit(better, at, inputs.Data, rng);
                    }

                    double fit = TemplateFit(mine, at, inputs.Data);
                    fitSum += fit;
                    if (fit >= KeepFit) suited++;
                    placedCultures++;
                }
                result[culture] = mine;
            }

            // Landless members follow their heritage.
            foreach (var ghost in humanCultures.Where(c => c.Heritage == unit.Heritage && ghosts.Contains(c)))
                result[ghost] = template;
        }

        var byFamily = units.GroupBy(u => groups[u.Group].Family)
            .OrderBy(g => Array.IndexOf(Families, g.Key))
            .Select(g => $"{g.Key} {g.Count()} ({g.Sum(u => u.Land) / totalLand:P0} of land)");
        say($"  human looks: {n} heritage(s) placed by climate — {string.Join(", ", byFamily)}");
        if (placedCultures > 0)
            say($"  human looks: {suited} of {placedCultures} culture(s) wear a look made for their climate " +
                $"(mean fit {fitSum / placedCultures:0.00})");

        return result;
    }

    /// <summary>
    /// The cost of a people wearing a look whose best fit to its climate is <paramref name="fit"/>:
    /// zero at a perfect fit, about 0.5 at 0.6, 1.1 at 0.3, and 3 at none. Logarithmic, not linear,
    /// because the failures that matter are the total ones. With 1 − fit an African people in Sámi
    /// furs at 61° cost only twice a middling fit, and the land-balance term bought that swap on the
    /// first test world (seed 478829); on this scale it is six times one.
    /// </summary>
    private static double Misfit(double fit) => Math.Log(1.05 / (fit + 0.05));

    private static double Dist2((double X, double Y) a, (double X, double Y) b)
        => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    /// <summary>
    /// How well a template suits a climate: the better of its own home and each place vanilla's
    /// peoples who wear it are dressed for, the second counted at 0.8. The evidence is what puts the
    /// Nivkh's subarctic Amur coast on <c>asian_manchu_korean</c> and the Buryats' taiga edge on
    /// <c>asian_mongol</c>; with one home each, the cold-summer Tibetan home out-fitted every other
    /// Asian look on lowland tundra and took every Arctic people in the family.
    /// </summary>
    ///
    /// Two witnesses, as in <see cref="ClothingClimate"/>'s building evidence: a home counts only when
    /// at least two vanilla peoples on the template are dressed for it. Single oddities are real —
    /// one arab-template people wears HRE dress, another Mongol — and on the first run with evidence
    /// they put an Arab-looking people at 51°. Tibetan takes no evidence at all: vanilla dresses its
    /// Tibetans in Mongol clothes for want of their own, which says nothing about where they live.
    /// </summary>
    public static double TemplateFit(string template, ClothingClimate.Climate at, Data? data)
    {
        var root = RootByKey[template];
        double fit = ClothingClimate.Fit(at, root.Home);
        if (root.Evidence && data?.EvidenceHomes(template) is { } homes)
            foreach (var home in homes)
                fit = Math.Max(fit, 0.8 * ClothingClimate.Fit(at, home));
        return fit;
    }

    /// <summary>Roulette over templates by squared climate fit — the best-suited usually, not always.</summary>
    private static string PickByFit(IReadOnlyList<string> templates, ClothingClimate.Climate? at, Data? data, Rng rng)
    {
        if (templates.Count == 1) return templates[0];
        if (at is not { } climate) return rng.Pick(templates);

        var weights = templates.Select(t =>
        {
            double f = TemplateFit(t, climate, data);
            return (f * f + 1e-4) * RootByKey[t].Prior;
        }).ToArray();
        return templates[Roulette(weights, rng.NextDouble())];
    }

    private static int Roulette(double[] weights, double roll)
    {
        double target = roll * weights.Sum();
        for (int i = 0; i < weights.Length; i++)
        {
            target -= weights[i];
            if (target < 0) return i;
        }
        return weights.Length - 1;
    }

    // ---------------------------------------------------------------------------------------------
    // Dressing one culture
    // ---------------------------------------------------------------------------------------------

    /// <summary>A tilt of one vanilla gene curve toward its low or high side.</summary>
    public sealed record Lean(string Gene, bool High, double Strength);

    /// <summary>
    /// Genes a people may lean on: the large, readable features — build, head, jaw, chin, nose,
    /// mouth, cheeks. Left out on purpose: eye distance (close-set reads as wrong at any strength),
    /// the eye fold (the one feature vanilla uses to separate its families, which a lean would blur),
    /// ears, body shape and bust, and ageing.
    /// </summary>
    private static readonly string[] LeanGenes =
    [
        "gene_height", "gene_head_width", "gene_head_height", "gene_jaw_width", "gene_jaw_forward",
        "gene_chin_forward", "gene_chin_width", "gene_bs_nose_size", "gene_bs_nose_profile",
        "gene_bs_nose_length", "gene_bs_nose_ridge_width", "gene_bs_nose_nostril_width",
        "gene_mouth_width", "gene_mouth_upper_lip_size", "gene_mouth_lower_lip_size",
        "gene_bs_cheek_height", "gene_bs_cheek_width", "gene_forehead_height", "gene_bs_forehead_brow_forward",
    ];

    /// <summary>How hard a heritage's shared leans and a culture's own lean tip the curve.</summary>
    private const double HeritageLean = 0.6;
    private const double CultureLean = 0.4;

    public static List<Lean> HeritageLeans(int seed, Heritage heritage)
    {
        var rng = Rng.For(seed, 0x4C0D, Rng.StableHash(heritage.Key));
        var genes = LeanGenes.ToList();
        rng.Shuffle(genes);
        return genes.Take(3).Select(g => new Lean(g, rng.Chance(0.5), HeritageLean)).ToList();
    }

    /// <summary>
    /// A culture's ethnicity list: one variant per entry of the vanilla recipe it borrows, each
    /// templated on the vanilla key itself, carrying the culture's skin window and the leans.
    /// </summary>
    public static List<EthnicityVariant> Dress(
        Culture culture, string template, string keyPrefix, Data? data, ClothingClimate.Climate? at,
        IReadOnlyList<Lean> heritageLeans, int seed)
    {
        var rng = Rng.For(seed, 0x4C0E, Rng.StableHash(culture.Key));

        // Which vanilla people's mix this culture borrows: one whose dress suits the culture's
        // climate, by the same squared-fit roulette as the template.
        List<(int Weight, string Key)> entries = [(100, template)];
        if (data?.RecipesByRoot.TryGetValue(template, out var recipes) == true && recipes.Count > 0)
        {
            var weights = recipes.Select(r =>
            {
                double f = at is { } c ? ClothingClimate.Fit(c, r.Home) : 1.0;
                return f * f + 1e-3;
            }).ToArray();
            entries = recipes[Roulette(weights, rng.NextDouble())].Entries;
        }

        // The culture's own lean, on a gene its heritage does not already lean on.
        var leans = heritageLeans.ToList();
        var free = LeanGenes.Where(g => leans.All(l => l.Gene != g)).ToList();
        if (free.Count > 0) leans.Add(new Lean(rng.Pick(free), rng.Chance(0.5), CultureLean));

        // Where in the template's skin range this people sits: warmer than the template's home is
        // darker, colder is lighter, with a little of its own on top.
        double t = 0.5;
        if (at is { } climate && RootInfo(template) is { } root)
        {
            double delta = (climate.ColdC + climate.WarmC) / 2 - (root.Home.ColdC + root.Home.WarmC) / 2;
            t = delta / 16.0 + 0.5;
        }
        t = Math.Clamp(t + rng.Double(-0.12, 0.12), 0.0, 1.0);

        var variants = new List<EthnicityVariant>();
        for (int i = 0; i < entries.Count; i++)
        {
            var (weight, key) = entries[i];
            var variant = new EthnicityVariant
            {
                Key = $"{keyPrefix}_{i}",
                LocalizedName = culture.Name,
                Template = key,
                Weight = weight
            };

            if (data?.Ethnicities.TryGetValue(key, out var vanilla) == true)
            {
                if (vanilla.Colors.TryGetValue("skin_color", out var skin))
                    variant.ColorGenes["skin_color"] = SkinWindow(skin, t);

                // Hair and eyes are vanilla's as the template has them. Written out rather than left
                // to inherit only so the bookmark DNA writer sees the same colouring the game will.
                foreach (string colour in (string[])["hair_color", "eye_color"])
                    if (vanilla.Colors.TryGetValue(colour, out var rects))
                        variant.ColorGenes[colour] = rects.Select(Copy).ToList();

                foreach (var lean in leans)
                    if (vanilla.Genes.TryGetValue(lean.Gene, out var curve))
                        variant.MorphGenes[lean.Gene] = Tilt(lean, curve);
            }

            variants.Add(variant);
        }

        return variants;
    }

    /// <summary>
    /// Share of each skin rectangle's lightness span one people covers. The rest of the span is
    /// where its neighbours on the same template sit.
    /// </summary>
    private const float SkinWindowShare = 0.6f;

    /// <summary>The same rectangles narrowed in lightness to a window at <paramref name="t"/> (0 lightest).</summary>
    private static List<ColorPaletteRange> SkinWindow(List<ColorPaletteRange> skin, double t)
        => skin.Select(r =>
        {
            float span = r.Y2 - r.Y1;
            float y1 = r.Y1 + (float)t * (1 - SkinWindowShare) * span;
            return new ColorPaletteRange
            {
                Weight = r.Weight, X1 = r.X1, X2 = r.X2,
                Y1 = Round(y1), Y2 = Round(y1 + SkinWindowShare * span)
            };
        }).ToList();

    private static float Round(float v) => MathF.Round(v, 3);

    private static ColorPaletteRange Copy(ColorPaletteRange r)
        => new() { Weight = r.Weight, X1 = r.X1, Y1 = r.Y1, X2 = r.X2, Y2 = r.Y2 };

    /// <summary>
    /// Vanilla's curve for a gene, reweighted toward one side. Ranges and template names are never
    /// changed, so every value a leaning people can roll is one the template could already roll.
    ///
    /// The side of an entry: for a blend-shape gene the template names it (<c>_neg</c> or <c>_pos</c>;
    /// a third shape such as <c>nose_profile_hawk</c> is left alone); for every other gene it is where
    /// the range sits against the 0.5 neutral — <c>gene_height</c> writes a single template across
    /// the whole curve, so a name-based rule would find nothing to tilt.
    /// </summary>
    private static List<GeneMorphEntry> Tilt(Lean lean, List<GeneMorphEntry> curve)
    {
        bool blendShape = lean.Gene.StartsWith("gene_bs_", StringComparison.Ordinal);

        int SideOf(GeneMorphEntry e)
        {
            if (blendShape)
                return e.SubGeneName.EndsWith("_pos", StringComparison.Ordinal) ? 1
                     : e.SubGeneName.EndsWith("_neg", StringComparison.Ordinal) ? -1 : 0;
            float mid = (e.Min + e.Max) / 2;
            return mid > 0.505f ? 1 : mid < 0.495f ? -1 : 0;
        }

        int dir = lean.High ? 1 : -1;
        return curve.Select(e =>
        {
            int side = SideOf(e);
            double factor = side == dir ? 1 + lean.Strength : side == -dir ? 1 - 0.6 * lean.Strength : 1;
            int w = (int)Math.Round(e.Weight * factor);
            if (e.Weight > 0 && w < 1) w = 1;
            return new GeneMorphEntry { SubGeneName = e.SubGeneName, Min = e.Min, Max = e.Max, Weight = w };
        }).ToList();
    }
}
