using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Which counties nobody lives in.
///
/// This runs between development and cultures, and that position is forced from both sides: it
/// needs development (the strongest single signal for where people are not) and everything after it
/// needs to know which counties to skip. A wilderness county is not a county with an unusual ruler —
/// it has no ruler, no culture of its own, no government and no holdings, so cultures, faiths,
/// governments, realms and the history writers all have to agree to leave it alone. One set, passed
/// down, is what makes that agreement checkable.
///
/// Placement is deliberately two passes rather than one ranking. Scoring every county and taking the
/// worst N produces single wild counties scattered through settled land, which reads as a generation
/// fault; the second pass grows them into runs and gives back anything too small to look intended.
/// The map should say "the north is empty", not "county 1,447 is empty".
/// </summary>
public sealed class WildernessMap
{
    /// <summary>
    /// The dummy every wilderness county is held by.
    ///
    /// One character for the whole map rather than one per county: the holder exists only so the
    /// titles are not ownerless, and CK3 is happy with a single character holding thousands of
    /// counties as long as its trait says it may (see <c>domain_limit</c> in the wilderness trait).
    /// The scripts in BaseFilesToCopy find it by <c>has_trait = wilderness</c> and never by id, so
    /// this name is ours to choose and is not part of any contract with them.
    /// </summary>
    public const string HolderId = "gen_wilderness_holder";

    /// <summary>
    /// A titular kingdom the dummy holds, purely so its realm has a name.
    ///
    /// Without it the dummy's primary title is whichever county it happens to hold first, so every
    /// unsettled county on the map is labelled with one arbitrary county's name — "Breostdon" over
    /// the whole northern waste. A ruler's realm takes its name from their primary title, and a
    /// titular kingdom outranks every county, so this becomes it.
    ///
    /// Landless, in the vanilla sense: it has no de jure counties and exists only to be held. That
    /// is the same device vanilla uses for head-of-faith titles like <c>k_orthodox</c>, and the
    /// generator already emits those, so this needs no new machinery.
    ///
    /// The key is referenced by the localisation in BaseFilesToCopy/Wilderness — the one place a
    /// static file names something the generator defines. See the note beside it there.
    /// </summary>
    public const string TitleKey = "k_gen_wilderness";

    private readonly HashSet<Title> counties;

    internal WildernessMap(HashSet<Title> counties) => this.counties = counties;

    /// <summary>Is this county unsettled?</summary>
    public bool Contains(Title county) => counties.Contains(county);

    /// <summary>How many counties were left unsettled.</summary>
    public int Count => counties.Count;

    /// <summary>Every unsettled county, for the writers that have to enumerate them.</summary>
    public IEnumerable<Title> Counties => counties;

    /// <summary>Nothing is wilderness. Used when the feature is switched off.</summary>
    public static WildernessMap Empty => new([]);
}

public static class Wilderness
{
    /// <summary>
    /// How unliveable each terrain is, 0 (prime farmland) to 1 (nobody lives here).
    ///
    /// These are not the same numbers as <see cref="Development"/>'s terrain weighting even though
    /// they rank similarly, and they should not be merged with it. Development asks "how rich is
    /// this county"; this asks "would anyone have bothered at all". Hills are poor but settled
    /// everywhere in the real world; arctic is not poor so much as empty. The two questions
    /// diverge most exactly where this matters.
    /// </summary>
    private static double Hostility(TerrainClass terrain) => terrain switch
    {
        TerrainClass.Arctic => 1.00,
        TerrainClass.Mountains => 0.85,
        TerrainClass.DesertMountains => 0.85,
        TerrainClass.Desert => 0.80,
        TerrainClass.Jungle => 0.65,
        TerrainClass.Wetlands => 0.60,
        TerrainClass.Taiga => 0.55,
        TerrainClass.Steppe => 0.35,
        TerrainClass.Forest => 0.30,
        TerrainClass.Drylands => 0.30,
        TerrainClass.Hills => 0.25,
        TerrainClass.Beach => 0.15,
        TerrainClass.Floodplains => 0.05,
        TerrainClass.Plains => 0.05,
        TerrainClass.Farmlands => 0.00,
        _ => 0.20,
    };

    /// <summary>
    /// Picks the unsettled counties.
    /// </summary>
    public static WildernessMap Build(List<Title> counties, ProvinceMap provinces, int[] order,
        int landCount, TerrainClass[] provinceTerrain, Dictionary<Title, int> development,
        MapConfig cfg, Rng rng)
    {
        if (!cfg.EnableWilderness || counties.Count == 0) return WildernessMap.Empty;

        var (neighbours, centroid) = CountyGraph(counties, provinces, order, landCount);

        // --- Pass 1: score every county -------------------------------------------------------
        //
        // Two terms. Hostility says what the ground is like; placement says where on the map it
        // sits. Development is deliberately NOT a third term: this codebase derives development
        // from terrain in the first place, so feeding it back in here would weight terrain twice
        // under two names and quietly overwhelm the edge bias.
        double edgeWeight = Math.Abs(cfg.WildernessEdgeBias);
        bool towardEdge = cfg.WildernessEdgeBias >= 0;

        var score = new double[counties.Count];
        for (int i = 0; i < counties.Count; i++)
        {
            double hostility = MeanHostility(counties[i], provinceTerrain);
            double edgeness = Edgeness(centroid[i]);
            double placement = towardEdge ? edgeness : 1.0 - edgeness;

            // How buried this county is inside its own kingdom: 0 on a kingdom border, 1 when
            // every neighbour answers to the same crown.
            //
            // Subtracted, because `Edgeness` above measures the wrong edge for this purpose. It is
            // distance from the middle of the MAP, so a mountain range through the middle of a
            // kingdom that happens to sit near the map's rim scores high on both existing terms.
            // This is the edge that matters for not cutting realms in half.
            double interior = Interiority(i, neighbours, counties);

            // A little noise so that a map of uniform terrain does not produce a perfectly
            // rectangular wilderness, and so two seeds differ where the inputs cannot.
            score[i] = cfg.WildernessTerrainWeight * hostility
                     - cfg.WildernessAvoidRealmInteriors * interior
                     + edgeWeight * placement
                     + rng.Decimal(0.0, 0.05);
        }

        // --- Pass 2: grow clumps from the worst ground ------------------------------------------
        int target = Math.Max(1, (int)Math.Round(cfg.WildernessShare * counties.Count));

        // Everything above this is fair game to grow into. Taken from the ranking itself rather
        // than fixed, so the floor means the same thing on an ice world and a garden world.
        var ranked = Enumerable.Range(0, counties.Count).OrderByDescending(i => score[i]).ToList();
        double floor = score[ranked[Math.Min(target, ranked.Count - 1)]];

        var chosen = new HashSet<int>();
        var clumps = new List<List<int>>();

        foreach (int seed in ranked)
        {
            if (chosen.Count >= target) break;
            if (chosen.Contains(seed)) continue;

            // Grow outward, always taking the best available neighbour, so a clump follows the
            // mountain range rather than spilling evenly in all directions.
            var clump = new List<int>();
            var frontier = new PriorityQueue<int, double>();
            frontier.Enqueue(seed, -score[seed]);

            while (frontier.Count > 0 && chosen.Count + clump.Count < target)
            {
                int at = frontier.Dequeue();
                if (chosen.Contains(at) || clump.Contains(at)) continue;
                if (clump.Count > 0 && score[at] < floor) continue;

                clump.Add(at);

                foreach (int next in neighbours[at])
                    if (!chosen.Contains(next) && !clump.Contains(next) && score[next] >= floor)
                        frontier.Enqueue(next, -score[next]);
            }

            foreach (int i in clump) chosen.Add(i);
            clumps.Add(clump);
        }

        // --- Give back the runts ----------------------------------------------------------------
        //
        // Except never all of them. With the feature enabled the map must contain wilderness
        // somewhere, or the scripts ship with nothing to point at and the whole system is
        // untestable from inside the game — which is exactly the state this stage exists to end.
        var kept = clumps.Where(c => c.Count >= cfg.WildernessMinClump).ToList();
        if (kept.Count == 0 && clumps.Count > 0)
            kept = [clumps.OrderByDescending(c => c.Count).First()];

        // --- Give back the ones that cut a realm in half ----------------------------------------
        //
        // The mirror of the runt rule above: that one rejects a clump for being too SMALL to read
        // as deliberate, this one rejects it for being too DISRUPTIVE regardless of size.
        //
        // Needed because the scoring does not merely ignore realm shape, it actively seeks the
        // worst case. Hostile ground forms ridges — mountain ranges, marsh belts — and the growth
        // pass deliberately follows them, because that is where the score stays above the floor.
        // A ridge across a kingdom is a bisection by definition, so the thing that makes wilderness
        // look intentional is the same thing that severs realms. The interiority term above biases
        // against it; this is the guarantee.
        //
        // Clumps are offered in descending order of how wild they are, so when two conflict the
        // more deserving one is kept.
        // A split is repaired by ABSORBING the stranded side, not by refusing the clump. Refusing
        // was the first attempt and it fails at exactly the settings that need it most: at a high
        // share the first clump grows to most of a continent, severs one duchy somewhere, and is
        // thrown away whole — a 90% target delivered 13%, and the only regions that survived were
        // islands, which sever nothing because their titles are entirely wild.
        //
        // Absorbing scales in both directions. A belt that strands three counties takes those three
        // with it; a continent-wide region simply finishes the job. Either way the outcome satisfies
        // the rule, because a title that is *entirely* wilderness was always allowed — it is only
        // settled land on both sides of a belt that reads as two realms sharing a name.
        //
        // The budget is what stops absorption running away. A clump that would strand half a
        // kingdom is still refused, because swallowing half a kingdom to place one region is worse
        // than not placing it.
        int budget = Math.Max(target, (int)Math.Round(target * 1.35));

        int refused = 0, absorbed = 0;
        var accepted = new List<List<int>>();
        var taken = new HashSet<int>();

        foreach (var clump in kept.OrderByDescending(c => c.Average(i => score[i])))
        {
            var trial = new HashSet<int>(taken);
            foreach (int i in clump) trial.Add(i);

            var stranded = StrandedBy(trial, neighbours, counties);
            if (taken.Count + clump.Count + stranded.Count > budget)
            {
                refused++;
                continue;
            }

            accepted.Add(clump);
            foreach (int i in clump) taken.Add(i);

            if (stranded.Count > 0)
            {
                // Stranded counties join the region that stranded them, so the run log's region
                // count stays the number of places a player would point at.
                accepted[^1].AddRange(stranded);
                foreach (int i in stranded) taken.Add(i);
                absorbed += stranded.Count;
            }
        }

        // Same floor as the runt rule: the map must contain wilderness somewhere or the scripts
        // ship with nothing to point at.
        if (accepted.Count == 0 && kept.Count > 0)
            accepted = [kept.OrderByDescending(c => c.Count).First()];

        kept = accepted;

        var result = new HashSet<Title>();
        foreach (var clump in kept)
            foreach (int i in clump)
                result.Add(counties[i]);

        Console.WriteLine($"  wilderness: {result.Count} counties in {kept.Count} regions "
            + $"({(double)result.Count / counties.Count:P1} of the map, target {cfg.WildernessShare:P0})"
            + (absorbed > 0 ? $", {absorbed} absorbed to keep titles whole" : "")
            + (refused > 0 ? $", {refused} regions refused on budget" : ""));

        return new WildernessMap(result);
    }

    /// <summary>
    /// The share of a county's neighbours that answer to the same kingdom it does.
    ///
    /// 0 for a county on a kingdom border or a coast, 1 for one buried in the middle of its own
    /// realm. Measured at kingdom level rather than duchy because duchies are small enough that
    /// most counties are interior to one, which would flatten the term to a constant.
    /// </summary>
    private static double Interiority(int index, List<int>[] neighbours, List<Title> counties)
    {
        var mine = Kingdom(counties[index]);
        if (mine is null || neighbours[index].Count == 0) return 0;

        int same = neighbours[index].Count(n => ReferenceEquals(Kingdom(counties[n]), mine));
        return (double)same / neighbours[index].Count;
    }

    /// <summary>
    /// Would making this clump wilderness leave some de jure title in two disconnected pieces?
    ///
    /// The rule, per title: the counties it still has left must be reachable from one another
    /// WITHOUT leaving the title. A title that ends up entirely wilderness is fine — that is a
    /// legitimate wild region, and the whole point of the feature. A title left with settled land on
    /// both sides of a wilderness belt is not, because on the map it reads as two realms sharing a
    /// name.
    ///
    /// Only the titles this clump actually touches are checked. A clump cannot disconnect a duchy it
    /// has no county in, so the cost is proportional to the clump rather than to the map.
    /// </summary>
    private static HashSet<int> StrandedBy(HashSet<int> wild, List<int>[] neighbours,
        List<Title> counties)
    {
        var stranded = new HashSet<int>();

        // Absorbing a county can strand another — it belongs to a duchy and a kingdom, and taking
        // it may cut either — so this repeats until nothing new falls out. Bounded because a
        // pathological map could otherwise walk the whole continent one county at a time; hitting
        // the cap simply means the caller sees a large stranded set and refuses on budget, which is
        // the right answer anyway.
        for (int pass = 0; pass < 8; pass++)
        {
            var before = stranded.Count;

            // Duchies and kingdoms both, because they fragment independently: a belt can leave
            // every duchy intact and still cut the kingdom above them into two halves.
            var titles = new HashSet<Title>();
            foreach (int i in wild.Concat(stranded))
            {
                if (counties[i].Parent is { } duchy) titles.Add(duchy);
                if (Kingdom(counties[i]) is { } kingdom) titles.Add(kingdom);
            }

            foreach (var title in titles)
            {
                var members = new HashSet<int>();
                for (int i = 0; i < counties.Count; i++)
                {
                    if (wild.Contains(i) || stranded.Contains(i)) continue;
                    if (ReferenceEquals(counties[i].Parent, title)
                        || ReferenceEquals(Kingdom(counties[i]), title))
                        members.Add(i);
                }

                if (members.Count <= 1) continue;

                // Every connected piece of what the title has left, flooding without ever stepping
                // outside it.
                var pieces = new List<List<int>>();
                var seen = new HashSet<int>();

                foreach (int start in members)
                {
                    if (!seen.Add(start)) continue;

                    var piece = new List<int> { start };
                    var queue = new Queue<int>();
                    queue.Enqueue(start);

                    while (queue.Count > 0)
                        foreach (int next in neighbours[queue.Dequeue()])
                            if (members.Contains(next) && seen.Add(next))
                            {
                                piece.Add(next);
                                queue.Enqueue(next);
                            }

                    pieces.Add(piece);
                }

                if (pieces.Count <= 1) continue;

                // Keep the largest piece as the title's real territory; everything else is a
                // remnant the wilderness has cut off, and goes wild with it.
                foreach (var piece in pieces.OrderByDescending(p => p.Count).Skip(1))
                    foreach (int i in piece)
                        stranded.Add(i);
            }

            if (stranded.Count == before) break;
        }

        return stranded;
    }

    /// <summary>A county's de jure kingdom, or null if the tree is shallower than that.</summary>
    private static Title? Kingdom(Title county) => county.Parent?.Parent;

    /// <summary>Mean hostility over a county's baronies.</summary>
    private static double MeanHostility(Title county, TerrainClass[] provinceTerrain)
    {
        double total = 0;
        int counted = 0;

        foreach (var barony in county.Children)
        {
            int id = barony.ProvinceId;
            if (id <= 0 || id >= provinceTerrain.Length) continue;
            total += Hostility(provinceTerrain[id]);
            counted++;
        }

        return counted == 0 ? 0.2 : total / counted;
    }

    /// <summary>
    /// How close to the rim of the map a point sits, 0 at the centre and 1 at any edge.
    ///
    /// Chebyshev rather than Euclidean distance, so the measure follows the rectangle the map
    /// actually is: a point near the left edge is at the rim whatever its latitude, which a radial
    /// measure would deny.
    /// </summary>
    private static double Edgeness((double X, double Y) position)
        => Math.Max(Math.Abs(position.X - 0.5), Math.Abs(position.Y - 0.5)) * 2.0;

    /// <summary>
    /// County adjacency, plus each county's centre in normalised 0-1 map coordinates.
    ///
    /// Cultures and Faiths each build a comparable graph for RegionGrowth, with a traversal cost
    /// this has no use for. Kept separate rather than folded into theirs because those two are
    /// working code and the shared part is fifteen lines; if a fourth caller appears it is worth
    /// lifting all three into Titles.
    /// </summary>
    private static (List<int>[] Neighbours, (double X, double Y)[] Centroid) CountyGraph(
        List<Title> counties, ProvinceMap provinces, int[] order, int landCount)
    {
        var countyOfProvince = new Dictionary<int, int>();
        for (int i = 0; i < counties.Count; i++)
            foreach (var barony in counties[i].Children)
                if (barony.ProvinceId > 0) countyOfProvince[barony.ProvinceId] = i;

        var seedOfProvince = new Dictionary<int, int>();
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

        var centroid = new (double X, double Y)[counties.Count];
        for (int i = 0; i < counties.Count; i++)
        {
            double x = 0, y = 0;
            int counted = 0;

            foreach (var barony in counties[i].Children)
            {
                if (!seedOfProvince.TryGetValue(barony.ProvinceId, out int label)) continue;
                var seed = provinces.Seeds[label];
                x += seed.X;
                y += seed.Y;
                counted++;
            }

            centroid[i] = counted == 0
                ? (0.5, 0.5)
                : (x / counted / provinces.Width, y / counted / provinces.Height);
        }

        return (neighbours, centroid);
    }
}
