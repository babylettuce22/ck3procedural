using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>How a see ranks among its faith's: the head of faith's own, one of the great ones, or neither.</summary>
public enum SeeRank { Ordinary, Great, Primate }

/// <summary>
/// One generated clerical region (CK3 1.20, By God Alone): a landless <c>d_et_gen_N</c> title held by
/// an archbishop of the faith, bound to a geographical region of counties whose rite it spreads. The
/// holder is a vassal of the top liege of the seat's county. See <see cref="Sees"/>.
/// </summary>
public sealed class See
{
    /// <summary>The landless title, <c>d_et_gen_N</c>. Frozen once built.</summary>
    public required string Key { get; init; }

    public required Faith Faith { get; init; }

    /// <summary>The county the see sits in: its title's capital and its holder's seat.</summary>
    public required Title Seat { get; init; }

    /// <summary>The counties of the region, the seat first.</summary>
    public List<Title> Counties { get; } = [];

    public SeeRank Rank { get; set; }

    /// <summary>The rite this see's counties keep: the faith's main rite unless a regional one reaches it.</summary>
    public Rite? Rite { get; set; }

    /// <summary>The geographical region the title history binds on the start date, <c>et_gen_N_region</c>.</summary>
    public string RegionKey => $"et_{Key["d_et_".Length..]}_region";

    /// <summary>
    /// Where the see stands on each additional bookmark, by year, the seat first: absent on a date it
    /// does not stand. Each date binds a region of its own, as vanilla's et_867_* and et_1066_* do.
    /// </summary>
    public Dictionary<int, List<Title>> Eras { get; } = [];

    /// <summary>An additional bookmark's region, <c>et_gen_N_1066_region</c>.</summary>
    public string RegionKeyAt(int year) => $"et_{Key["d_et_".Length..]}_{year}_region";

    /// <summary>
    /// The see's Synod Seat, <c>d_cd_gen_N</c>: the landless elector title its archbishop holds beside
    /// it when the faith elects its head (<see cref="Faith.HasElectors"/>). Separate because the engine
    /// refuses to make a clerical-region title an elector ("already has incompatible special data",
    /// qw 2026-09-30), so it is shaped like vanilla's cardinalates (d_cd_*) instead.
    /// </summary>
    public string SynodSeatKey => $"d_cd_gen_{Key["d_et_gen_".Length..]}";
}

/// <summary>
/// An additional bookmark as the sees see it: what each county is governed as on that date and the
/// frontier then. Sees grow on settled land, so a date that is less tribal has more of them.
/// </summary>
public sealed record SeeDate(int Year, GovernmentMap Governments, WildernessMap? Wilderness);

/// <summary>
/// A regional rite of a faith, founded by one of its great sees (<c>founder = d_et_gen_N</c> in
/// rite_types, as vanilla's Ambrosian rite is founded by d_et_milano). It keeps the faith's core and
/// differs in one tenet and one doctrine, which keeps its divergence well under the heresy line.
/// The faith's main rite is not one of these: it stays keyed like the faith (see ReligionWriter).
/// </summary>
public sealed class Rite
{
    public required string Key { get; init; }
    public required Faith Faith { get; init; }
    public required See Founder { get; init; }

    /// <summary>"Kelian Rite", "Varnianism".</summary>
    public required string Name { get; init; }

    /// <summary>"Kelian"; the adherent reads "Kelian" + the faith's adherent.</summary>
    public required string Adjective { get; init; }

    /// <summary>True for a rite named after a founding saint rather than its seat.</summary>
    public required bool FromSaint { get; init; }

    public required (double R, double G, double B) Color { get; init; }
    public required List<string> Tenets { get; init; }

    /// <summary>The one doctrine this rite holds over the faith's, by doctrine group.</summary>
    public Dictionary<string, string> DoctrineOverrides { get; } = [];

    public List<Title> Counties { get; } = [];
}

/// <summary>
/// The words a religion's own tongue has for its sees, coined when native rank titles are on. The
/// ordinary holder's word is the religion's own "bishop" word, which every generated religion
/// already coins (<c>BishopMale</c>), so only the places are coined here.
/// </summary>
public sealed record SeeWords(string See, string GreatSee, string Primacy);

/// <summary>
/// Generated clerical regions and the regional rites founded from them. The design is in the
/// generated-sees plan (agreed 2026-09-30):
///
/// <list type="bullet">
/// <item><b>Which faiths:</b> organised faiths with a spiritual head of faith, of a religion with
///   institutional clergy, i.e. not lay clergy (vanilla's Islamic shape, whose landed clerics 1.20
///   refuses outside a clerical region anyway), with a settled heartland of at least
///   <see cref="MinHeartland"/> counties. See <see cref="Eligible"/>.</item>
/// <item><b>Coverage:</b> the heartland only: the faith's counties under a settled government.
///   Tribal and nomad fringes stay mission land, as vanilla's 867 map leaves pagan Europe.</item>
/// <item><b>Seats:</b> the head of faith's seat is the primate see; the rest are seeded at holy
///   sites, world centres and the most developed duchies, spaced apart, then grown over de jure
///   duchies to vanilla's grain (about <see cref="CountiesPerSee"/> counties each; vanilla's 867
///   median is ten).</item>
/// <item><b>Great sees:</b> the primate plus three to five of the largest, a generated Pentarchy.</item>
/// <item><b>Rites:</b> about one per hundred counties of the faith (the engine's
///   <c>MAX_FAITH_SIZE_PER_RITE</c>), each founded by a great see, named for its seat
///   ("Kelian Rite") or a founding saint ("Varnianism").</item>
/// <item><b>Bookmarks:</b> each additional bookmark grows its own sees on that date's settled land,
///   keeping the seats of the sees already standing where it can, so a see is the same title across
///   the dates it stands on and only the region it binds moves. A date less tribal than the start
///   has more sees, as vanilla's 1066 has more than its 867.</item>
/// </list>
///
/// Its own random streams throughout, so turning sees off moves nothing else in the world.
/// </summary>
public static class Sees
{
    public const int MinHeartland = 6;

    /// <summary>Sees a faith needs on the start date before its archbishops elect its head (the user's choice, 2026-09-30).</summary>
    public const int MinElectorSees = 3;
    public const double CountiesPerSee = 10;
    public const int CountiesPerRite = 100;

    private const int RiteStream = 0x5EE1;
    private const int WordStream = 0x5EE2;

    /// <summary>
    /// Doctrine groups a regional rite may differ in: devotional practice rather than anything that
    /// decides succession, marriage law or clergy, so a rite never changes who may rule or wed.
    /// Sacraments are left out (the user, 2026-09-30): they decide who may excommunicate, and a see
    /// faith holds them central as a whole (ReligionWriter).
    /// </summary>
    private static readonly string[] RiteDoctrineGroups =
        ["doctrine_pilgrimage", "doctrine_funeral", "monasticism_group", "preservation"];

    /// <summary>
    /// Clears and rebuilds every generated faith's sees and rites, and each religion's see words.
    /// </summary>
    /// <param name="wilderness">
    /// The land no see reaches. Needed when rebuilding after an applied history (see
    /// ContentWriter.ApplyRealms), whose frontier can differ from the generated one.
    /// </param>
    /// <param name="dates">
    /// The additional bookmarks, each grown its own sees after the start date's. Null or empty for a
    /// world with one bookmark, which then comes out exactly as it did before bookmarks had sees.
    /// </param>
    public static void Build(FaithMap faiths, List<Title> counties, RegionGrowth.Graph graph,
        GovernmentMap governments, Dictionary<Title, int> development, WorldCenterMap? worldCenters,
        CultureMap cultures, VanillaVocabulary vocab, MapConfig cfg, WildernessMap? wilderness = null,
        IReadOnlyList<SeeDate>? dates = null)
    {
        foreach (var faith in faiths.Faiths)
        {
            faith.Sees.Clear();
            faith.EraSees.Clear();
            faith.Rites.Clear();
            faith.MainRiteAdjective = null;
            faith.HasClericalRegions = cfg.GeneratedSees && Eligible(faith);
            faith.HasElectors = false;
        }
        foreach (var religion in faiths.Religions)
        {
            religion.SeeWords = null;
            religion.Ecclesiastical = cfg.GeneratedSees && Institutional(religion);
        }
        if (!cfg.GeneratedSees) return;

        var index = new Dictionary<Title, int>();
        for (int i = 0; i < counties.Count; i++) index[counties[i]] = i;

        // Which counties follow each faith, read off the county map rather than Faith.Counties: an
        // applied history's conversions (ContentWriter.ChangePeoples) rewrite the map and leave the
        // lists as generated.
        Dictionary<Faith, List<Title>> Followers(WildernessMap? wild) => counties
            .Where(c => wild?.Contains(c) != true && faiths.ByCounty.ContainsKey(c))
            .GroupBy(c => faiths.ByCounty[c])
            .ToDictionary(g => g.Key, g => g.ToList());
        var followers = Followers(wilderness);

        int seeNumber = 0, riteCount = 0, faithsWith = 0;
        foreach (var faith in faiths.Faiths)
        {
            if (!Eligible(faith) || !followers.TryGetValue(faith, out var own)) continue;

            var heartland = own.Where(c => Settled(governments.For(c))).ToList();
            if (heartland.Count < MinHeartland) continue;

            var grown = Grow(faith, heartland, counties, index, graph, development, worldCenters, []);
            if (grown.Count == 0) continue;

            foreach (var (seat, region) in grown)
            {
                var see = new See { Key = $"d_et_gen_{seeNumber++}", Faith = faith, Seat = seat };
                see.Counties.AddRange(region);
                faith.Sees.Add(see);
            }

            if (faith.Head is { Temporal: false } head && faith.Sees.FirstOrDefault(s => s.Seat == head.Seat) is { } primate)
                primate.Rank = SeeRank.Primate;

            RankSees(faith);
            FoundRites(faith, own, index, graph, cultures, vocab, cfg);

            // Its archbishops elect its head once it has enough of them (see-electors plan, 2026-09-30).
            faith.HasElectors = faith.Sees.Count >= MinElectorSees;
            riteCount += faith.Rites.Count;
            faithsWith++;
        }

        // The other bookmarks, after every start-date see is numbered, so a world's start-date keys
        // are the same with bookmarks or without. Outward from the start, the nearest date first, so
        // each date keeps the seats of the one beside it rather than of a date centuries away.
        int eraSees = 0;
        foreach (var date in (dates ?? []).OrderBy(d => Math.Abs(d.Year - cfg.StartYear)).ThenBy(d => d.Year))
        {
            var followersThen = Followers(date.Wilderness ?? wilderness);
            foreach (var faith in faiths.Faiths)
            {
                if (!Eligible(faith) || !followersThen.TryGetValue(faith, out var own)) continue;

                var heartland = own.Where(c => Settled(date.Governments.For(c))).ToList();
                if (heartland.Count < MinHeartland) continue;

                var standing = faith.AllSees.ToList();
                var grown = Grow(faith, heartland, counties, index, graph, development, worldCenters,
                    standing.Select(s => s.Seat).ToHashSet());

                foreach (var (seat, region) in grown)
                {
                    var see = standing.FirstOrDefault(s => s.Seat == seat);
                    if (see is null)
                    {
                        see = new See { Key = $"d_et_gen_{seeNumber++}", Faith = faith, Seat = seat };
                        faith.EraSees.Add(see);
                        standing.Add(see);
                    }
                    see.Eras[date.Year] = region;
                    eraSees++;
                }
            }
        }

        // A see founded on another date keeps the rite of the nearest founding seat, as the start
        // date's do; it founds none of its own, the rites being the start date's.
        foreach (var faith in faiths.Faiths.Where(f => f.EraSees.Count > 0 && f.Sees.Count > 0))
            foreach (var see in faith.EraSees)
                see.Rite = NearestRite(faith, see.Seat, index, graph);

        if (cfg.NativeRankTitles)
            foreach (var religion in faiths.Religions.Where(r => r.HasSees))
                religion.SeeWords = CoinWords(religion.Language, cfg.Seed);

        Console.WriteLine($"  sees: {faiths.Faiths.Sum(f => f.Sees.Count)} clerical regions for {faithsWith} faiths, {riteCount} regional rites"
            + (dates is { Count: > 0 }
                ? $"; on the other bookmarks {string.Join(", ", dates.OrderBy(d => d.Year).Select(d => $"{d.Year}: {faiths.Faiths.Sum(f => f.AllSees.Count(s => s.Eras.ContainsKey(d.Year)))}"))}"
                  + $" ({faiths.Faiths.Sum(f => f.EraSees.Count)} not standing on the start date)"
                : ""));
    }

    /// <summary>
    /// Organised, generated, of a religion whose clergy is an institution, and led by a spiritual head
    /// of faith: a faith that holds sees, or may found them in play (the clerical-regions doctrine,
    /// see ReligionWriter).
    ///
    /// The head is vanilla's own line: only the churches with a Pope or a patriarch have sees, which is
    /// about a third of its map. Without it every organised faith qualified, and a world of organised
    /// churches was covered end to end (qw, 2026-09-30: 23 sees over 83 % of the settled land, 14 of 17
    /// faiths eligible; with the head, two faiths and about a third). A faith that reforms in play
    /// with a spiritual head gains them (zz_gen_clerical_regions_on_actions.txt).
    /// </summary>
    public static bool Eligible(Faith faith)
        => !faith.Inherited && faith.IsOrganized && Institutional(faith.Religion)
           && faith.Head is { Temporal: false }
           && faith.Key != Faiths.UnsettledFaithKey;

    /// <summary>
    /// A generated religion whose clergy is an institution rather than lay: one whose theocrats are
    /// ecclesiastical, as Christianity's are, whether or not a faith of it has sees yet, since a faith
    /// that reforms in play founds them (BaseFilesToCopy/Core zz_gen_clerical_regions_on_actions.txt).
    /// </summary>
    public static bool Institutional(Religion religion)
        => !religion.LayClergy && !religion.Inherited && religion.Key != Faiths.UnsettledReligionKey;

    /// <summary>Land a church hierarchy reaches: neither tribal nor nomad.</summary>
    private static bool Settled(string government)
        => GovernmentMap.Family(government) is not (GovernmentMap.Tribal or GovernmentMap.Nomad)
           && government != GovernmentMap.Wilderness;

    // --- Growing the sees ----------------------------------------------------------------------

    /// <summary>
    /// Cuts the heartland into sees on de jure grain: whole duchies (the faith's counties in each)
    /// go to the nearest seat, the smallest see claiming first, so sees come out of similar size
    /// and never split a duchy between two archbishops.
    /// </summary>
    /// <param name="keep">
    /// Seats of sees standing on another date. Each outranks any other county of its duchy, so a see
    /// whose seat is still in the heartland stays where it was; only the head of faith's seat comes first.
    /// </param>
    /// <returns>Each see's seat and its region, the seat first.</returns>
    private static List<(Title Seat, List<Title> Region)> Grow(Faith faith, List<Title> heartland, List<Title> counties,
        Dictionary<Title, int> index, RegionGrowth.Graph graph, Dictionary<Title, int> development,
        WorldCenterMap? worldCenters, HashSet<Title> keep)
    {
        // Units: the faith's heartland counties grouped by de jure duchy.
        var units = heartland.GroupBy(c => c.Parent is { Tier: "d" } d ? d : c)
            .Select(g => g.OrderBy(c => c.Index).ToList())
            .OrderBy(u => u[0].Index)
            .ToList();
        var unitOf = new Dictionary<Title, int>();
        for (int u = 0; u < units.Count; u++)
            foreach (var county in units[u]) unitOf[county] = u;

        var unitNeighbours = new HashSet<int>[units.Count];
        for (int u = 0; u < units.Count; u++)
        {
            unitNeighbours[u] = [];
            foreach (var county in units[u])
                foreach (int n in graph.Neighbours[index[county]])
                    if (unitOf.TryGetValue(counties[n], out int other) && other != u) unitNeighbours[u].Add(other);
        }

        var holySites = faith.HolySites.Select(h => h.County).ToHashSet();
        Title? primateSeat = faith.Head is { Temporal: false } head && unitOf.ContainsKey(head.Seat) ? head.Seat : null;

        double CountyScore(Title c)
            => (c == primateSeat ? 1e6 : 0) + (keep.Contains(c) ? 1e5 : 0) + (holySites.Contains(c) ? 60 : 0)
             + (worldCenters?.IsCenter(c) == true ? 40 : 0) + development.GetValueOrDefault(c);

        double UnitScore(int u) => units[u].Sum(CountyScore) / Math.Sqrt(units[u].Count);

        int target = Math.Max(1, (int)Math.Round(heartland.Count / CountiesPerSee));
        target = Math.Min(target, units.Count);

        // Seeds: best-scoring units, at least two duchies apart where the heartland allows it.
        var order = Enumerable.Range(0, units.Count)
            .OrderByDescending(UnitScore).ThenBy(u => units[u][0].Index).ToList();
        var seeds = new List<int>();
        foreach (int minGap in new[] { 3, 2, 1 })
        {
            foreach (int u in order)
            {
                if (seeds.Count == target) break;
                if (seeds.Contains(u)) continue;
                if (seeds.Any(s => UnitDistance(s, u, unitNeighbours, minGap) < minGap)) continue;
                seeds.Add(u);
            }
            if (seeds.Count == target) break;
        }

        // Growth: the smallest see with an unclaimed neighbouring duchy claims it next.
        var owner = Enumerable.Repeat(-1, units.Count).ToArray();
        var size = new int[seeds.Count];
        for (int s = 0; s < seeds.Count; s++)
        {
            owner[seeds[s]] = s;
            size[s] = units[seeds[s]].Count;
        }

        while (true)
        {
            int best = -1, bestUnit = -1;
            for (int s = 0; s < seeds.Count; s++)
            {
                if (best >= 0 && size[s] >= size[best]) continue;
                int candidate = Enumerable.Range(0, units.Count)
                    .Where(u => owner[u] == s)
                    .SelectMany(u => unitNeighbours[u])
                    .Where(n => owner[n] < 0)
                    .DefaultIfEmpty(-1)
                    .OrderByDescending(n => n < 0 ? double.MinValue : UnitScore(n)).First();
                if (candidate < 0) continue;
                best = s;
                bestUnit = candidate;
            }
            if (best < 0) break;
            owner[bestUnit] = best;
            size[best] += units[bestUnit].Count;
        }

        // Duchies no see can walk to (islands, enclaves) join the see whose seat is nearest.
        var seatOfSeed = seeds.Select(s => units[s].OrderByDescending(CountyScore).ThenBy(c => c.Index).First()).ToList();
        for (int u = 0; u < units.Count; u++)
        {
            if (owner[u] >= 0) continue;
            var p = graph.Position[index[units[u][0]]];
            owner[u] = Enumerable.Range(0, seeds.Count)
                .OrderBy(s => Distance(p, graph.Position[index[seatOfSeed[s]]])).First();
        }

        var sees = new List<(Title Seat, List<Title> Region)>();
        for (int s = 0; s < seeds.Count; s++)
        {
            var seat = seatOfSeed[s];
            var region = new List<Title> { seat };
            for (int u = 0; u < units.Count; u++)
                if (owner[u] == s)
                    region.AddRange(units[u].Where(c => c != seat));
            sees.Add((seat, region));
        }

        return sees;
    }

    private static int UnitDistance(int from, int to, HashSet<int>[] neighbours, int cap)
    {
        if (from == to) return 0;
        var seen = new HashSet<int> { from };
        var frontier = new List<int> { from };
        for (int d = 1; d < cap; d++)
        {
            var next = new List<int>();
            foreach (int u in frontier)
                foreach (int n in neighbours[u])
                {
                    if (n == to) return d;
                    if (seen.Add(n)) next.Add(n);
                }
            frontier = next;
        }
        return cap;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The primate (when the faith has a spiritual head seated in its heartland) and the great sees.</summary>
    private static void RankSees(Faith faith)
    {
        var others = faith.Sees.Where(s => s.Rank != SeeRank.Primate)
            .OrderByDescending(s => s.Counties.Count).ThenBy(s => s.Seat.Index).ToList();

        int great = faith.Sees.Count >= 5 ? Math.Clamp(faith.Sees.Count / 3, 3, 5) : Math.Max(0, faith.Sees.Count - 1);
        foreach (var see in others.Take(great)) see.Rank = SeeRank.Great;

        // A faith without a spiritual head still has a first see: the largest great one.
        if (faith.Sees.All(s => s.Rank != SeeRank.Primate))
            (others.FirstOrDefault(s => s.Rank == SeeRank.Great) ?? faith.Sees[0]).Rank = SeeRank.Primate;
    }

    // --- Rites ---------------------------------------------------------------------------------

    /// <summary>
    /// About one rite per <see cref="CountiesPerRite"/> counties of the faith, the main rite counted:
    /// a faith of 250 counties keeps its main rite and founds two regional ones. The founders are the
    /// largest great sees; every see then keeps the rite of the nearest founding seat (the primate's
    /// being the main rite), and so do the faith's counties outside any see.
    /// </summary>
    private static void FoundRites(Faith faith, List<Title> own, Dictionary<Title, int> index,
        RegionGrowth.Graph graph, CultureMap cultures, VanillaVocabulary vocab, MapConfig cfg)
    {
        int wanted = (int)Math.Ceiling(own.Count / (double)CountiesPerRite) - 1;
        var founders = faith.Sees.Where(s => s.Rank == SeeRank.Great)
            .OrderByDescending(s => s.Counties.Count).ThenBy(s => s.Seat.Index)
            .Take(Math.Max(0, wanted)).ToList();
        if (founders.Count == 0) return;

        var rng = Rng.For(cfg.Seed, RiteStream, Rng.StableHash(faith.Key));
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { faith.Name };

        // The main rite is named for the primate see, as the Roman Rite is for Rome.
        var primateSee = faith.Sees.First(s => s.Rank == SeeRank.Primate);
        faith.MainRiteAdjective = cultures.For(primateSee.Seat).Tongue.LanguageNameFor(primateSee.Seat.Name, rng);
        taken.Add(faith.MainRiteAdjective);

        for (int i = 0; i < founders.Count; i++)
        {
            var see = founders[i];
            var tongue = cultures.For(see.Seat).Tongue;
            bool saint = rng.Chance(0.5);

            string adjective = "", name = "";
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (saint)
                {
                    string stem = Stem(rng.Chance(0.7) ? tongue.MaleName(rng) : tongue.FemaleName(rng));
                    bool ian = rng.Chance(0.6);
                    adjective = ian ? $"{stem}ian" : $"{stem}ite";
                    name = ian ? $"{adjective}ism" : $"{adjective} Rite";
                }
                else
                {
                    adjective = tongue.LanguageNameFor(see.Seat.Name, rng);
                    name = $"{adjective} Rite";
                }
                if (taken.Add(adjective)) break;
            }

            var rite = new Rite
            {
                Key = $"{faith.Key}_rite_{i + 1}",
                Faith = faith,
                Founder = see,
                Name = name,
                Adjective = adjective,
                FromSaint = saint,
                Color = Shift(faith.Color, rng),
                Tenets = VaryTenets(faith, vocab, cfg, rng),
            };
            VaryDoctrine(rite, faith, vocab, rng);
            faith.Rites.Add(rite);
        }

        // Each see keeps the rite of the nearest founder, the primate's (the main rite) included.
        var primate = primateSee;
        var seats = new List<(See See, Rite? Rite)> { (primate, null) };
        seats.AddRange(faith.Rites.Select(r => (r.Founder, (Rite?)r)));

        Rite? Nearest(Title county)
        {
            var p = graph.Position[index[county]];
            return seats.OrderBy(s => Distance(p, graph.Position[index[s.See.Seat]])).First().Rite;
        }

        var founded = faith.Rites.ToDictionary(r => r.Founder);
        foreach (var see in faith.Sees)
            see.Rite = founded.TryGetValue(see, out var foundedHere) ? foundedHere : see == primate ? null : Nearest(see.Seat);

        var seeOf = faith.Sees.SelectMany(s => s.Counties.Select(c => (c, s))).ToDictionary(p => p.c, p => p.s);
        foreach (var county in own)
        {
            var rite = seeOf.TryGetValue(county, out var see) ? see.Rite : Nearest(county);
            rite?.Counties.Add(county);
        }
    }

    /// <summary>The rite of the founding seat nearest <paramref name="county"/>, null for the main rite's (the primate's).</summary>
    private static Rite? NearestRite(Faith faith, Title county, Dictionary<Title, int> index, RegionGrowth.Graph graph)
    {
        if (faith.Rites.Count == 0 || faith.Sees.FirstOrDefault(s => s.Rank == SeeRank.Primate) is not { } primate) return null;

        var p = graph.Position[index[county]];
        var seats = new List<(Title Seat, Rite? Rite)> { (primate.Seat, null) };
        seats.AddRange(faith.Rites.Select(r => (r.Founder.Seat, (Rite?)r)));
        return seats.OrderBy(s => Distance(p, graph.Position[index[s.Seat]])).First().Rite;
    }

    /// <summary>A given name as a saint's stem: "Kelo" gives "Kel", so "Kelian" rather than "Keloian".</summary>
    private static string Stem(string name)
    {
        string s = name.Split(' ')[0];
        while (s.Length > 3 && "aeiouy".Contains(char.ToLowerInvariant(s[^1]))) s = s[..^1];
        return s;
    }

    /// <summary>The faith's first tenet (its war tenet, when it has one) is kept; one of the others is redrawn.</summary>
    private static List<string> VaryTenets(Faith faith, VanillaVocabulary vocab, MapConfig cfg, Rng rng)
    {
        var tenets = faith.Tenets.ToList();
        if (tenets.Count < 2) return tenets;

        int swap = rng.Int(1, tenets.Count - 1);
        var kept = tenets.Where((_, i) => i != swap).ToList();
        var pool = Faiths.TenetPool(faith.Religion, vocab, cfg).Where(t => !tenets.Contains(t)).ToList();
        var drawn = Faiths.SampleCompatible(pool, 1, faith.Religion.Doctrines.Values.Concat(kept), vocab, rng);
        if (drawn.Count == 0) return tenets;

        tenets[swap] = drawn[0];
        return tenets;
    }

    private static void VaryDoctrine(Rite rite, Faith faith, VanillaVocabulary vocab, Rng rng)
    {
        var groups = RiteDoctrineGroups.Where(g => vocab.DoctrineGroups.TryGetValue(g, out var m) && m.Count > 1
                                                   && faith.DoctrineOf(g).Length > 0).ToList();
        rng.Shuffle(groups);

        var held = faith.Religion.Doctrines.Values.Concat(rite.Tenets).ToList();
        foreach (string group in groups)
        {
            string current = faith.DoctrineOf(group);
            var options = vocab.DoctrineGroups[group]
                .Where(d => d != current && vocab.Compatible(d, held.Where(h => h != current))).ToList();
            if (options.Count == 0) continue;

            rite.DoctrineOverrides[group] = rng.Pick(options);
            return;
        }
    }

    /// <summary>A rite's colour: the faith's, moved in hue and lightness as vanilla deviates a dynamic rite's.</summary>
    private static (double R, double G, double B) Shift((double R, double G, double B) color, Rng rng)
    {
        double Clamp(double v) => Math.Clamp(v, 0, 1);
        double dr = (rng.Double() - 0.5) * 0.5, dg = (rng.Double() - 0.5) * 0.5, db = (rng.Double() - 0.5) * 0.5;
        return (Clamp(color.R + dr), Clamp(color.G + dg), Clamp(color.B + db));
    }

    // --- Words ---------------------------------------------------------------------------------

    private static readonly string[][] SeeRecipes = [["holy", Language.RealmEnding], ["priest", "hall"], ["temple", Language.RealmEnding]];
    private static readonly string[][] GreatSeeRecipes = [["great", "temple"], ["high", "temple"], ["old", "temple"]];
    private static readonly string[][] PrimacyRecipes = [["blessing", Language.RealmEnding], ["glory", Language.RealmEnding], ["holy", "temple"]];

    /// <summary>The religion's tongue's words for its sees, on its own stream and with its own family grammar.</summary>
    public static SeeWords CoinWords(Language tongue, int seed)
    {
        var (_, _, ending) = NativeTitles.GrammarOf(tongue, seed);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string Spend(string id, string[][] recipes)
        {
            var rng = Rng.For(seed, WordStream, Rng.StableHash($"{tongue.Key}/{id}"));
            foreach (var recipe in recipes)
                if (tongue.RankWord(recipe, ending, rng, minLength: 4) is { } word && taken.Add(word))
                    return word;
            string fresh = tongue.Word(rng, 2, 2);
            for (int attempt = 0; attempt < 8 && !taken.Add(fresh); attempt++) fresh = tongue.Word(rng, 2, 2);
            return fresh;
        }

        return new SeeWords(Spend("see", SeeRecipes), Spend("great_see", GreatSeeRecipes), Spend("primacy", PrimacyRecipes));
    }
}
