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

            // A little noise so that a map of uniform terrain does not produce a perfectly
            // rectangular wilderness, and so two seeds differ where the inputs cannot.
            score[i] = cfg.WildernessTerrainWeight * hostility
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

        var result = new HashSet<Title>();
        foreach (var clump in kept)
            foreach (int i in clump)
                result.Add(counties[i]);

        Console.WriteLine($"  wilderness: {result.Count} counties in {kept.Count} regions "
            + $"({(double)result.Count / counties.Count:P1} of the map, target {cfg.WildernessShare:P0})");

        return new WildernessMap(result);
    }

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
