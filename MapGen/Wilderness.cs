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
    /// <summary>
    /// Wilderness straight off the export: the counties standing on ground Azgaar gave to no state.
    ///
    /// Better than the habitability heuristic below whenever there is an export, because it is not a
    /// guess. Azgaar already decided which ground nobody settled, and the heuristic — which picks
    /// remote, poor, edge-of-the-map counties — would otherwise carve wilderness out of the middle
    /// of a country the export drew as inhabited.
    ///
    /// Returns null rather than an empty map when the export claims everything, so the caller falls
    /// back to generating wilderness instead of shipping a map with none.
    /// </summary>
    private static WildernessMap? FromExport(List<Title> counties, AzgaarImport azgaar)
    {
        var unclaimed = new List<Title>();

        foreach (var county in counties)
        {
            int total = 0, ownerless = 0;

            foreach (var barony in county.Children)
            {
                if (barony.ProvinceId < 1) continue;
                total++;
                if (azgaar.StateOfBarony(barony.ProvinceId) <= 0) ownerless++;
            }

            // A simple majority, since a county straddling a border is partly claimed by
            // construction and only the ones mostly outside every state are truly wild.
            if (total > 0 && ownerless * 2 > total) unclaimed.Add(county);
        }

        if (unclaimed.Count == 0) return null;

        Console.WriteLine($"  wilderness: {unclaimed.Count} counties on ground azgaar left unclaimed " +
                          $"({100.0 * unclaimed.Count / counties.Count:F1} % of counties)");

        return new WildernessMap([.. unclaimed]);
    }

    public static WildernessMap Build(List<Title> counties, ProvinceMap provinces, int[] order,
        int landCount, TerrainClass[] provinceTerrain, Dictionary<Title, int> development,
        MapConfig cfg, Rng rng, AzgaarImport? azgaar = null)
    {
        if (!cfg.EnableWilderness || counties.Count == 0) return WildernessMap.Empty;

        if (azgaar is not null && FromExport(counties, azgaar) is { } imported) return imported;

        var (neighbours, centroid) = CountyGraph(counties, provinces, order, landCount);

        // --- Pass 1: score every county -------------------------------------------------------
        double edgeWeight = Math.Abs(cfg.WildernessEdgeBias);
        bool towardEdge = cfg.WildernessEdgeBias >= 0;

        var score = new double[counties.Count];
        for (int i = 0; i < counties.Count; i++)
        {
            double hostility = MeanHostility(counties[i], provinceTerrain);
            double edgeness = Edgeness(centroid[i]);
            double placement = towardEdge ? edgeness : 1.0 - edgeness;

            // Measures edge distance from both kingdom and empire borders to prevent
            // bisecting multi-kingdom empires.
            double interior = Interiority(i, neighbours, counties);

            score[i] = cfg.WildernessTerrainWeight * hostility
                     - cfg.WildernessAvoidRealmInteriors * interior
                     + edgeWeight * placement
                     + rng.Decimal(0.0, 0.05);
        }

        // --- Pass 2: grow clumps from the worst ground ------------------------------------------
        int target = Math.Max(1, (int)Math.Round(cfg.WildernessShare * counties.Count));

        var ranked = Enumerable.Range(0, counties.Count).OrderByDescending(i => score[i]).ToList();
        double floor = score[ranked[Math.Min(target, ranked.Count - 1)]];

        var chosen = new HashSet<int>();
        var clumps = new List<List<int>>();

        foreach (int seed in ranked)
        {
            if (chosen.Count >= target) break;
            if (chosen.Contains(seed)) continue;

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
        var kept = clumps.Where(c => c.Count >= cfg.WildernessMinClump).ToList();
        if (kept.Count == 0 && clumps.Count > 0)
            kept = [clumps.OrderByDescending(c => c.Count).First()];

        // --- Give back the ones that cut a realm in half ----------------------------------------
        int budget = Math.Max(target, (int)Math.Round(target * 1.35));

        int refused = 0, absorbed = 0;
        var accepted = new List<List<int>>();
        var taken = new HashSet<int>();

        foreach (var clump in kept.OrderByDescending(c => c.Average(i => score[i])))
        {
            if (taken.Count >= target) break;

            var trial = new HashSet<int>(taken);
            foreach (int i in clump) trial.Add(i);

            var stranded = StrandedBy(trial, neighbours, counties);

            // Only refuse a clump if it causes runaway stranding that exceeds budget
            if (stranded.Count > clump.Count * 2 && taken.Count + clump.Count + stranded.Count > budget)
            {
                refused++;
                continue;
            }

            accepted.Add(clump);
            foreach (int i in clump) taken.Add(i);

            if (stranded.Count > 0 && taken.Count + stranded.Count <= budget)
            {
                accepted[^1].AddRange(stranded);
                foreach (int i in stranded) taken.Add(i);
                absorbed += stranded.Count;
            }
        }

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
    /// The share of a county's neighbours that answer to the same kingdom/empire it does.
    /// </summary>
    private static double Interiority(int index, List<int>[] neighbours, List<Title> counties)
    {
        var myKingdom = Kingdom(counties[index]);
        var myEmpire = Empire(counties[index]);
        if (neighbours[index].Count == 0) return 0;

        double kRatio = myKingdom != null
            ? (double)neighbours[index].Count(n => ReferenceEquals(Kingdom(counties[n]), myKingdom)) / neighbours[index].Count
            : 0.0;
        double eRatio = myEmpire != null
            ? (double)neighbours[index].Count(n => ReferenceEquals(Empire(counties[n]), myEmpire)) / neighbours[index].Count
            : 0.0;

        return Math.Max(kRatio, eRatio * 0.85);
    }

    /// <summary>
    /// Would making this clump wilderness leave some de jure title in two disconnected pieces?
    /// </summary>
    private static HashSet<int> StrandedBy(HashSet<int> wild, List<int>[] neighbours,
        List<Title> counties)
    {
        var stranded = new HashSet<int>();

        for (int pass = 0; pass < 8; pass++)
        {
            var before = stranded.Count;

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

    /// <summary>A county's de jure empire, or null if the tree is shallower than that.</summary>
    private static Title? Empire(Title county) => county.Parent?.Parent?.Parent;

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
    /// </summary>
    private static double Edgeness((double X, double Y) position)
        => Math.Max(Math.Abs(position.X - 0.5), Math.Abs(position.Y - 0.5)) * 2.0;

    /// <summary>
    /// County adjacency, plus each county's centre in normalised 0-1 map coordinates.
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