namespace Ck3MapGen.MapGen;

/// <summary>
/// One edge of the road as the caravans travel it: the network edge, and the hub at the end they
/// leave from. A <see cref="RouteEdge"/> holds its provinces in its own A-to-B order, which has
/// nothing to do with the direction of travel, so the road has to remember which way it walked —
/// otherwise an arrow drawn from it points back the way the caravans came.
/// </summary>
public readonly record struct RouteStep(RouteEdge Edge, int From);

/// <summary>One of the six stops of the Silk Road: a bazaar and the band of country around it.</summary>
public sealed class SilkRoadStop
{
    /// <summary>Vanilla's suffix — <c>china</c>, <c>tibet</c> … — which every key below is built from.</summary>
    public required string Suffix { get; init; }

    public string SubRegionKey => $"region_silk_road_proper_{Suffix}";
    public string RegionKey => $"tgp_silk_road_{Suffix}_region";
    public string RouteRegionKey => $"dlc_tgp_silk_road_route_{Suffix}";

    /// <summary>The vanilla county key the bazaar county is given, so hardcoded script finds it.</summary>
    public required string CountyKey { get; init; }

    /// <summary>The vanilla market building placed at the bazaar.</summary>
    public required string Market { get; init; }

    public required RouteHub Hub { get; init; }
    public Title County => Hub.County;

    /// <summary>The barony that holds the market: the seat, unless a wonder already stands there.</summary>
    public required Title MarketBarony { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// The empire whose whole de jure land is this sub-region, when the stop is the source and
    /// its empire is small enough to be the road's heartland — see <see cref="SilkRoad.HeartlandShare"/>.
    /// Null for a band along the road.
    /// </summary>
    public Title? Heartland { get; init; }

    /// <summary>The counties of the sub-region.</summary>
    public required List<Title> Counties { get; init; }

    /// <summary>The counties the road itself runs through, in order outward from the source.</summary>
    public required List<Title> RouteCounties { get; init; }

    public required (int R, int G, int B) Color { get; init; }
}

/// <summary>
/// Where the Silk Road situation lives on this map.
///
/// All Under Heaven binds it through six sub-regions on hardcoded keys, each with a bazaar
/// province where innovations arrive, and a hardwired stream between them: innovations appear
/// at the source and move outward to its neighbours, and from those to theirs. Six vanilla
/// county keys are referenced by name in the decisions, events and effects, so the six bazaar
/// counties are given those keys; their names stay generated.
/// </summary>
public sealed class SilkRoadMap
{
    private readonly Dictionary<Title, string> _marketAt;

    /// <summary>The stops in vanilla's declaration order.</summary>
    public IReadOnlyList<SilkRoadStop> Stops { get; }

    /// <summary>The edges the road runs along, source outward, each with the end it leaves from.</summary>
    public IReadOnlyList<RouteStep> Chain { get; }

    /// <summary>Why the source is where it is, for the log.</summary>
    public string SourceNote { get; }

    internal SilkRoadMap(List<SilkRoadStop> stops, List<RouteStep> chain, string sourceNote)
    {
        Stops = stops;
        Chain = chain;
        SourceNote = sourceNote;
        _marketAt = stops.ToDictionary(s => s.MarketBarony, s => s.Market);
    }

    public static SilkRoadMap Empty => new([], [], "");

    public bool IsEmpty => Stops.Count == 0;

    /// <summary>The market building on a barony, or null.</summary>
    public string? MarketAt(Title barony) => _marketAt.GetValueOrDefault(barony);

    /// <summary>
    /// Every vanilla region key the road gives a meaning to: each stop's region and route region,
    /// and vanilla's two unions of them.
    /// </summary>
    public Dictionary<string, List<Title>> RegionMembers()
    {
        var members = new Dictionary<string, List<Title>>(StringComparer.Ordinal);
        if (IsEmpty) return members;

        foreach (var s in Stops)
        {
            members[s.RegionKey] = s.Counties;
            members[s.RouteRegionKey] = s.RouteCounties;
        }
        members["tgp_silk_road_region"] = Stops.SelectMany(s => s.Counties).Distinct().ToList();
        members["dlc_tgp_silk_road_route_region"] = Stops.SelectMany(s => s.RouteCounties).Distinct().ToList();
        return members;
    }
}

public static class SilkRoad
{
    /// <summary>
    /// Vanilla's six stops, in its declaration order, each with the county key its script names,
    /// the market building placed there, and the colour its map mode paints. The stream is
    /// china → tibet → india and china → central_asia → transcaspia → occident.
    /// </summary>
    private static readonly (string Suffix, string CountyKey, string Market, (int R, int G, int B) Color)[] Slots =
    [
        ("china", "c_jingzhao", "changan_market_01", (0, 168, 107)),
        ("tibet", "c_lhasa", "lhasa_market_01", (239, 208, 40)),
        ("india", "c_lahur", "lahur_bazaar_01", (237, 103, 89)),
        ("central_asia", "c_shazhou", "dunhuang_market_01", (57, 105, 168)),
        ("transcaspia", "c_khiva", "khiva_bazaar_01", (95, 161, 51)),
        ("occident", "c_dvin", "dvin_shuka_01", (164, 72, 138)),
    ];

    private const int China = 0, Tibet = 1, India = 2, CentralAsia = 3, Transcaspia = 4, Occident = 5;

    /// <summary>
    /// Vanilla's china sub-region is the whole of China — twenty-three kingdoms, a third of the
    /// situation's counties — where the other five are bands a few duchies wide along the road.
    /// The source's de jure empire plays that part here, unless it is more than this share of
    /// the world's counties, when it gets a band like the others rather than half the map.
    /// </summary>
    public const double HeartlandShare = 0.15;

    /// <summary>A laid road: which hub is each stop, the two paths, and the graph they were found on.</summary>
    private sealed record Layout(int[] StopHub, List<int> MainPath, List<int> Branch,
        List<(int To, double Cost, RouteEdge Edge)>[] Links);

    /// <summary>
    /// Lays the road.
    ///
    /// The source is the greatest court on the map — the hegemony's seat, or the largest
    /// empire's — when it stands on the main land road system, and otherwise the richest market
    /// there. The far end of the main road is the market farthest from it by road, and the two
    /// stops between are the markets a third and two thirds of the way along. The branch goes to
    /// the market farthest from the whole main road, with its own middle stop halfway.
    ///
    /// The road is laid over land roads only, so that it is a caravan road and every county it
    /// crosses is on it; sea lanes are used only on a map whose land cannot carry six stops.
    /// Nomad camps and wilderness are never stops: the road's own participant groups exclude
    /// nomads, and a market in a herd camp is not a bazaar.
    ///
    /// Each stop's sub-region is then the country the road crosses: the de jure duchies its
    /// route counties belong to and the duchies touching those, the way vanilla builds these
    /// regions from duchies rather than from a radius. The source's whole empire is its region
    /// when it is small enough to be a heartland. Anything cut off from its bazaar by water
    /// joins the stop it touches instead, so each sub-region is one piece of country.
    /// </summary>
    public static SilkRoadMap Build(List<Title> empires, RouteNetwork routes, CrossingMap crossings,
        Dictionary<Title, int> development, WorldCenterMap worldCenters, GovernmentMap governments,
        ProvinceMap provinces, int[] order, int baronyCount)
    {
        if (routes.Hubs.Count < Slots.Length) return SilkRoadMap.Empty;

        var hubs = routes.Hubs;
        bool Eligible(RouteHub h) => !h.Wilderness && !governments.IsNomad(h.County);

        var (court, crown) = GreatestCourt(empires);
        var courtHub = court is null ? null : hubs.FirstOrDefault(h => ReferenceEquals(h.County, court));

        // The court's own road system first, so the road begins at the emperor's seat by rule
        // rather than by luck on a world with two continents; the largest system when the
        // court's cannot carry six stops; and lanes only when no land system can.
        var land = routes.Edges.Where(e => e.Kind == RouteKind.Land).ToList();
        var all = routes.Edges.ToList();
        var fromCourt = courtHub is not null && Eligible(courtHub);
        var layout = (fromCourt ? Lay(hubs, land, Eligible, development, courtHub) : null)
                  ?? Lay(hubs, land, Eligible, development, null)
                  ?? (fromCourt ? Lay(hubs, all, Eligible, development, courtHub) : null)
                  ?? Lay(hubs, all, Eligible, development, null);
        if (layout is null) return SilkRoadMap.Empty;

        var (stopHub, mainPath, branch, links) = layout;

        string crownName = crown is null ? "" : crown.Tier == "h" ? "the hegemony's seat" : $"the seat of {crown.Name}";
        string sourceNote = court is null ? "the richest market on the main road system"
            : stopHub[China] == courtHub?.Index ? $"{crownName}, {court.Name}"
            : $"the richest market on the main road system — {crownName}, {court.Name}, "
              + (courtHub is null ? "is not a market"
                 : courtHub.Wilderness ? "is wilderness"
                 : governments.IsNomad(court) ? "is a nomad camp"
                 : "is on a road system that cannot carry six stops");

        // --- The road's counties, and which stop each belongs to ------------------------------
        var countyOf = new Dictionary<int, Title>();
        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        foreach (var county in counties)
            foreach (var b in county.Children)
                if (b.ProvinceId >= 1 && b.ProvinceId <= baronyCount) countyOf[b.ProvinceId] = county;

        var chain = new List<RouteStep>();
        var routeCounties = new List<Title>[Slots.Length];
        for (int s = 0; s < routeCounties.Length; s++) routeCounties[s] = [];
        var stopOfCounty = new Dictionary<Title, int>();

        // Each segment of road between two stops is split at its middle: the near half is the
        // first stop's, the far half the second's. Segments are walked source outward, so each
        // stop's route list reads in the direction of travel.
        void Segment(int fromStop, int toStop, List<int> along)
        {
            int a = stopHub[fromStop], b = stopHub[toStop];
            int ia = along.IndexOf(a), ib = along.IndexOf(b);
            var hubSeq = along.GetRange(ia, ib - ia + 1);

            var road = new List<Title>();
            for (int i = 1; i < hubSeq.Count; i++)
            {
                var link = links[hubSeq[i - 1]].First(l => l.To == hubSeq[i]);
                if (!chain.Any(step => ReferenceEquals(step.Edge, link.Edge)))
                    chain.Add(new RouteStep(link.Edge, hubSeq[i - 1]));

                var provinces = link.Edge.A == hubSeq[i - 1] ? link.Edge.Provinces : Enumerable.Reverse(link.Edge.Provinces).ToList();
                foreach (int p in provinces)
                    if (countyOf.TryGetValue(p, out var county) && (road.Count == 0 || !ReferenceEquals(road[^1], county)))
                        road.Add(county);
            }

            int half = road.Count / 2;
            for (int i = 0; i < road.Count; i++)
            {
                int stop = i < half ? fromStop : toStop;
                var county = road[i];
                if (stopOfCounty.TryAdd(county, stop) && !routeCounties[stop].Contains(county))
                    routeCounties[stop].Add(county);
            }
        }

        // The bazaar counties belong to their own stop before any segment can claim them.
        for (int s = 0; s < stopHub.Length; s++)
        {
            stopOfCounty[hubs[stopHub[s]].County] = s;
            routeCounties[s].Add(hubs[stopHub[s]].County);
        }

        Segment(China, CentralAsia, mainPath);
        Segment(CentralAsia, Transcaspia, mainPath);
        Segment(Transcaspia, Occident, mainPath);
        Segment(China, Tibet, branch);
        Segment(Tibet, India, branch);

        // --- Sub-regions ---------------------------------------------------------------------
        var adjacent = CountyAdjacency(counties, countyOf, provinces, order, baronyCount, crossings);
        var region = new Dictionary<Title, int>(stopOfCounty);

        // The duchies the road crosses, each to the stop with the most road in it.
        var routeDuchyStop = new Dictionary<Title, int>();
        foreach (var byDuchy in stopOfCounty.Where(kv => kv.Key.Parent is { Tier: "d" }).GroupBy(kv => kv.Key.Parent!))
        {
            int stop = byDuchy.GroupBy(kv => kv.Value)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .First().Key;
            routeDuchyStop[byDuchy.Key] = stop;
            foreach (var county in byDuchy.Key.Children) region.TryAdd(county, stop);
        }

        // The source's heartland: its whole empire, when that is not most of the world. The
        // corridors of the other stops were claimed first, as vanilla carves the corridor
        // kingdoms out of China for Central Asia.
        Title? heartland = null;
        var sourceCounty = hubs[stopHub[China]].County;
        if (sourceCounty.Parent?.Parent?.Parent is { Tier: "e" } empire)
        {
            var members = Titles.Flatten([empire]).Where(t => t.Tier == "c").ToList();
            if (members.Count <= HeartlandShare * counties.Count)
            {
                heartland = empire;
                foreach (var county in members) region.TryAdd(county, China);
            }
        }

        // One ring of neighbouring duchies around each band; a duchy two bands both touch goes
        // to the one touching it from more duchies.
        var duchyAdjacent = new Dictionary<Title, HashSet<Title>>();
        foreach (var (a, others) in adjacent)
        {
            if (a.Parent is not { Tier: "d" } da) continue;
            foreach (var b in others)
            {
                if (b.Parent is not { Tier: "d" } db || ReferenceEquals(da, db)) continue;
                if (!duchyAdjacent.TryGetValue(da, out var set)) duchyAdjacent[da] = set = [];
                set.Add(db);
            }
        }

        var claims = new Dictionary<Title, List<int>>();
        foreach (var (duchy, stop) in routeDuchyStop)
        {
            if (stop == China && heartland is not null) continue;
            if (!duchyAdjacent.TryGetValue(duchy, out var around)) continue;
            foreach (var next in around)
            {
                if (routeDuchyStop.ContainsKey(next)) continue;
                if (!claims.TryGetValue(next, out var list)) claims[next] = list = [];
                list.Add(stop);
            }
        }
        foreach (var (duchy, claimants) in claims)
        {
            int stop = claimants.GroupBy(s => s).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            foreach (var county in duchy.Children) region.TryAdd(county, stop);
        }

        // Each sub-region is one piece of country. A piece cut off from its bazaar — across a
        // strait a road ferries over, or an empire's island — joins the stop it touches, or is
        // left out. Two passes, so a piece handed on is judged again where it landed.
        var bazaar = stopHub.Select(i => hubs[i].County).ToList();
        for (int pass = 0; pass < 2; pass++)
        {
            for (int s = 0; s < Slots.Length; s++)
            {
                var owned = region.Where(kv => kv.Value == s).Select(kv => kv.Key).OrderBy(c => c.Index).ToList();
                foreach (var piece in Pieces(owned, adjacent))
                {
                    if (piece.Contains(bazaar[s])) continue;

                    var touching = piece
                        .SelectMany(c => adjacent.TryGetValue(c, out var around) ? around : [])
                        .Where(n => region.TryGetValue(n, out int r) && r != s)
                        .GroupBy(n => region[n])
                        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                        .FirstOrDefault();

                    foreach (var county in piece)
                    {
                        if (touching is not null) region[county] = touching.Key;
                        else region.Remove(county);
                    }
                }
            }
        }

        var wonderBaronies = worldCenters.Centers.Select(c => c.CapitalBarony).ToHashSet();
        var stops = new List<SilkRoadStop>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (int s = 0; s < Slots.Length; s++)
        {
            var slot = Slots[s];
            var hub = hubs[stopHub[s]];
            var owned = region.Where(kv => kv.Value == s).Select(kv => kv.Key).OrderBy(c => c.Index).ToList();

            // A province carries one special building slot, so a bazaar shares no barony with a
            // wonder; the seat is preferred and the next barony over takes it otherwise.
            var market = wonderBaronies.Contains(hub.Barony)
                ? hub.County.Children.FirstOrDefault(b => b.Tier == "b" && !wonderBaronies.Contains(b)) ?? hub.Barony
                : hub.Barony;

            var own = s == China ? heartland : null;
            string name = (own?.Name is { Length: > 0 } ? own.Name : null) ?? KingdomName(owned) ?? hub.County.Name;
            if (!names.Add(name))
            {
                name = $"{name} of {hub.County.Name}";
                names.Add(name);
            }

            // The rename that makes vanilla's script find the bazaar. Keys are internal; the
            // county keeps its generated name, and nothing has written the old key yet — the
            // landed titles, history and localisation are all written after this runs.
            hub.County.Key = slot.CountyKey;

            stops.Add(new SilkRoadStop
            {
                Suffix = slot.Suffix,
                CountyKey = slot.CountyKey,
                Market = slot.Market,
                Hub = hub,
                MarketBarony = market,
                Name = name,
                Heartland = own,
                Counties = owned,
                RouteCounties = routeCounties[s],
                Color = slot.Color,
            });
        }

        return new SilkRoadMap(stops, chain, sourceNote);
    }

    /// <summary>
    /// The county of the greatest court, and the crown it seats: the hegemony's, or the largest
    /// empire's when the world has no hegemony. Both are seated on their most developed county
    /// already, so this is the richest market by construction as well as by rank — but by rank
    /// on purpose, so the road begins where the world's emperor sits and vanilla's china-realm
    /// effects, written for one, land on them.
    /// </summary>
    private static (Title? Court, Title? Crown) GreatestCourt(List<Title> empires)
    {
        var crown = Titles.HegemonyOf(empires)
            ?? empires.OrderByDescending(e => Titles.Flatten([e]).Count(t => t.Tier == "c"))
                      .ThenBy(e => e.Index)
                      .FirstOrDefault();
        return (crown is null ? null : Capitals.CapitalCounty(crown), crown);
    }

    /// <summary>
    /// Finds the six stops on a graph of the given edges, or null when it cannot carry them.
    /// With a <paramref name="court"/> the source is that hub and the road its system; without
    /// one, the largest system and the richest eligible market on it.
    /// </summary>
    private static Layout? Lay(IReadOnlyList<RouteHub> hubs, List<RouteEdge> edges, Func<RouteHub, bool> eligible,
        Dictionary<Title, int> development, RouteHub? court)
    {
        var links = new List<(int To, double Cost, RouteEdge Edge)>[hubs.Count];
        for (int i = 0; i < links.Length; i++) links[i] = [];
        foreach (var e in edges)
        {
            links[e.A].Add((e.B, e.Cost, e));
            links[e.B].Add((e.A, e.Cost, e));
        }

        // --- The source: the court, else the richest market on the largest road system ---------
        var component = Components(links);
        int largest = component.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;

        var china = court
            ?? hubs.Where(h => eligible(h) && component[h.Index] == largest)
                .OrderByDescending(h => development.GetValueOrDefault(h.County))
                .ThenByDescending(h => h.KingdomSeat)
                .ThenBy(h => h.Index)
                .FirstOrDefault();
        if (china is null) return null;

        var (fromChina, prevFromChina, _) = Dijkstra(links, china.Index);

        // --- The main road: to the farthest market, with stops at thirds ---------------------
        List<int>? mainPath = null;
        int centralAsia = -1, transcaspia = -1;
        foreach (var far in hubs.Where(h => h.Index != china.Index && eligible(h) && !double.IsPositiveInfinity(fromChina[h.Index]))
                                .OrderByDescending(h => fromChina[h.Index]))
        {
            var path = PathTo(prevFromChina, far.Index);
            var interior = path.Skip(1).Take(path.Count - 2).Where(i => eligible(hubs[i])).ToList();
            if (interior.Count < 2) continue;

            centralAsia = Nearest(path, fromChina, interior, fromChina[far.Index] / 3.0, after: -1);
            transcaspia = Nearest(path, fromChina, interior, 2.0 * fromChina[far.Index] / 3.0, after: centralAsia);
            if (centralAsia < 0 || transcaspia < 0) continue;
            mainPath = path;
            break;
        }
        if (mainPath is null) return null;
        int occident = mainPath[^1];

        // --- The branch: to the market farthest from the main road, with a stop halfway ------
        var (fromMain, _, _) = Dijkstra(links, mainPath);
        var onMain = mainPath.ToHashSet();
        List<int>? branch = null;
        int india = -1, tibet = -1;
        foreach (var far in hubs.Where(h => eligible(h) && !onMain.Contains(h.Index) && !double.IsPositiveInfinity(fromMain[h.Index]) && fromMain[h.Index] > 0)
                                .OrderByDescending(h => fromMain[h.Index])
                                .ThenByDescending(h => development.GetValueOrDefault(h.County)))
        {
            var path = PathTo(prevFromChina, far.Index);
            var interior = path.Skip(1).Take(path.Count - 2)
                .Where(i => eligible(hubs[i]) && !onMain.Contains(i) && i != centralAsia && i != transcaspia)
                .ToList();
            if (interior.Count == 0) continue;

            tibet = Nearest(path, fromChina, interior, fromChina[far.Index] / 2.0, after: -1);
            if (tibet < 0) continue;
            india = far.Index;
            branch = path;
            break;
        }
        if (branch is null) return null;

        int[] stopHub = [china.Index, tibet, india, centralAsia, transcaspia, occident];
        if (stopHub.Distinct().Count() != stopHub.Length) return null;

        return new Layout(stopHub, mainPath, branch, links);
    }

    /// <summary>The hub on <paramref name="path"/> among <paramref name="candidates"/> whose distance from the source is nearest <paramref name="target"/>, later along the path than <paramref name="after"/>.</summary>
    private static int Nearest(List<int> path, double[] dist, List<int> candidates, double target, int after)
    {
        int afterAt = after < 0 ? -1 : path.IndexOf(after);
        int best = -1;
        double bestGap = double.PositiveInfinity;
        foreach (int c in candidates)
        {
            if (path.IndexOf(c) <= afterAt) continue;
            double gap = Math.Abs(dist[c] - target);
            if (gap < bestGap) { bestGap = gap; best = c; }
        }
        return best;
    }

    private static List<int> PathTo(int[] prev, int target)
    {
        var path = new List<int>();
        for (int at = target; at >= 0; at = prev[at]) path.Add(at);
        path.Reverse();
        return path;
    }

    private static (double[] Dist, int[] Prev, RouteEdge?[] Edge) Dijkstra(List<(int To, double Cost, RouteEdge Edge)>[] links, int source)
        => Dijkstra(links, [source]);

    private static (double[] Dist, int[] Prev, RouteEdge?[] Edge) Dijkstra(List<(int To, double Cost, RouteEdge Edge)>[] links, IEnumerable<int> sources)
    {
        var dist = new double[links.Length];
        var prev = new int[links.Length];
        var edge = new RouteEdge?[links.Length];
        Array.Fill(dist, double.PositiveInfinity);
        Array.Fill(prev, -1);

        var queue = new PriorityQueue<int, double>();
        foreach (int s in sources) { dist[s] = 0; queue.Enqueue(s, 0); }

        while (queue.TryDequeue(out int at, out double d))
        {
            if (d > dist[at]) continue;
            foreach (var (next, cost, e) in links[at])
            {
                double candidate = d + cost;
                if (candidate >= dist[next]) continue;
                dist[next] = candidate;
                prev[next] = at;
                edge[next] = e;
                queue.Enqueue(next, candidate);
            }
        }

        return (dist, prev, edge);
    }

    private static int[] Components(List<(int To, double Cost, RouteEdge Edge)>[] links)
    {
        var component = new int[links.Length];
        Array.Fill(component, -1);
        int next = 0;
        for (int start = 0; start < links.Length; start++)
        {
            if (component[start] >= 0) continue;
            var stack = new Stack<int>();
            stack.Push(start);
            component[start] = next;
            while (stack.Count > 0)
            {
                int at = stack.Pop();
                foreach (var (to, _, _) in links[at])
                    if (component[to] < 0) { component[to] = next; stack.Push(to); }
            }
            next++;
        }
        return component;
    }

    /// <summary>The connected pieces of a set of counties, in the order the set was given.</summary>
    private static List<List<Title>> Pieces(List<Title> counties, Dictionary<Title, HashSet<Title>> adjacent)
    {
        var members = counties.ToHashSet();
        var seen = new HashSet<Title>();
        var pieces = new List<List<Title>>();

        foreach (var start in counties)
        {
            if (!seen.Add(start)) continue;
            var piece = new List<Title>();
            var stack = new Stack<Title>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                piece.Add(at);
                if (!adjacent.TryGetValue(at, out var around)) continue;
                foreach (var next in around)
                    if (members.Contains(next) && seen.Add(next)) stack.Push(next);
            }
            pieces.Add(piece);
        }

        return pieces;
    }

    /// <summary>
    /// County-to-county adjacency as the game has it: the barony raster, plus the straits and
    /// river crossings adjacencies.csv declares. Without the crossings a road that ferries over
    /// a river would cut its own sub-region in two at the bank.
    /// </summary>
    private static Dictionary<Title, HashSet<Title>> CountyAdjacency(List<Title> counties,
        Dictionary<int, Title> countyOf, ProvinceMap provinces, int[] order, int baronyCount, CrossingMap crossings)
    {
        var adjacent = new Dictionary<Title, HashSet<Title>>();
        void Link(int a, int b)
        {
            if (!countyOf.TryGetValue(a, out var ca) || !countyOf.TryGetValue(b, out var cb) || ReferenceEquals(ca, cb)) return;
            if (!adjacent.TryGetValue(ca, out var set)) adjacent[ca] = set = [];
            set.Add(cb);
        }

        foreach (var (province, others) in Titles.BuildAdjacency(provinces, baronyCount, order))
            foreach (int other in others)
                Link(province, other);

        foreach (var c in crossings.Crossings)
        {
            Link(c.From, c.To);
            Link(c.To, c.From);
        }
        return adjacent;
    }

    /// <summary>The de jure kingdom holding most of a set of counties, by name, or null.</summary>
    private static string? KingdomName(List<Title> counties)
        => counties.Select(c => c.Parent?.Parent)
            .Where(k => k is { Tier: "k" } && k.Name.Length > 0)
            .GroupBy(k => k!)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Index)
            .Select(g => g.Key.Name)
            .FirstOrDefault();
}
