namespace Ck3MapGen.MapGen;

/// <summary>A market: a duchy capital, at its seat barony's province.</summary>
public sealed class RouteHub
{
    public required int Index { get; init; }
    public required Title Duchy { get; init; }
    public required Title County { get; init; }
    public required Title Barony { get; init; }
    public required int ProvinceId { get; init; }
    public required (double X, double Y) Position { get; init; }

    /// <summary>Whether this is also its kingdom's capital — a trunk hub.</summary>
    public required bool KingdomSeat { get; init; }

    /// <summary>Whether the duchy's capital county is wilderness: a market with nobody in it.</summary>
    public required bool Wilderness { get; init; }

    /// <summary>Whether the seat touches navigable water, and so can be a lane's end.</summary>
    public required bool Port { get; init; }
}

public enum RouteKind { Land, Sea }

/// <summary>One road or sea lane: the provinces it runs through, end to end, hub to hub.</summary>
public sealed class RouteEdge
{
    public required int A { get; init; }
    public required int B { get; init; }
    public required RouteKind Kind { get; init; }

    /// <summary>Province ids from hub A's to hub B's, both included; water provinces in between for a lane.</summary>
    public required List<int> Provinces { get; init; }

    /// <summary>The path's cost — pixels weighted by the ground or water crossed.</summary>
    public required double Cost { get; init; }

    /// <summary>The path's length in pixels, unweighted.</summary>
    public required double Length { get; init; }

    /// <summary>How many straits or river crossings a land road uses.</summary>
    public int Crossings { get; init; }

    /// <summary>Whether this was part of the spanning backbone rather than a shortcut added to it.</summary>
    public bool Backbone { get; set; }

    /// <summary>How many hub-to-hub shortest journeys pass over it.</summary>
    public int Traffic { get; set; }

    /// <summary>A trunk route: among the most travelled on the map.</summary>
    public bool Primary { get; set; }
}

/// <summary>
/// The route network: every market and every road and sea lane between them.
///
/// Representation-agnostic on purpose. Today its one game-facing consumer is the
/// connection-arrows file the Silk Road map mode draws; Silk and Silver describes roads as a
/// static graph between markets, land and sea, with levels that rise with use, which is the
/// same object, and the emitter for whatever file that ships in is a second consumer over the
/// same paths.
/// </summary>
public sealed class RouteNetwork
{
    public required IReadOnlyList<RouteHub> Hubs { get; init; }
    public required IReadOnlyList<RouteEdge> Edges { get; init; }

    /// <summary>How many separate systems there are once lanes are counted — ideally one.</summary>
    public required int Components { get; init; }

    /// <summary>How many hub pairs touched by land but had no land path between them, crossings included.</summary>
    public required int Unreachable { get; init; }

    public static RouteNetwork Empty => new() { Hubs = [], Edges = [], Components = 0, Unreachable = 0 };
}

public static class Routes
{
    /// <summary>
    /// A shortcut is kept only if it beats the routes already there by this much: one that
    /// saves less than a quarter of the journey is a second road beside the first.
    /// </summary>
    private const double ShortcutRatio = 0.75;

    /// <summary>A route that shares more than this of its interior with routes already kept is the same route.</summary>
    private const double MaxOverlap = 0.5;

    /// <summary>The share of routes, by traffic, that count as trunk routes.</summary>
    private const double PrimaryShare = 0.2;

    /// <summary>Roads keep out of the wild where they can.</summary>
    private const double WildernessPenalty = 1.35;

    /// <summary>How much a river valley discounts the ground it runs through, at full flow.</summary>
    private const double RiverDiscount = 0.25;

    /// <summary>A ferry over a strait costs this many times its width in pixels; a river crossing less.</summary>
    private const double StraitFerry = 3.0;
    private const double RiverFerry = 1.5;

    /// <summary>Water near a coast is cheap to sail; open water is not, so lanes hug the shore.</summary>
    private const double CoastalWater = 1.0;
    private const double OpenWater = 1.8;
    private const double RiverWater = 1.3;

    /// <summary>How many other ports each port reaches for, nearest first.</summary>
    private const int LanesPerPort = 4;

    /// <summary>The longest lane, as a share of the map's width in weighted pixels.</summary>
    private const double MaxLaneShare = 0.12;

    /// <summary>
    /// The cost of a mile over each kind of ground, relative to open plain. Not the culture
    /// table: a border stalls on the same hills a road merely climbs, and a desert a people will
    /// not settle is still crossed by caravans.
    /// </summary>
    private static double Ground(TerrainClass t) => t switch
    {
        TerrainClass.Farmlands => 1.0,
        TerrainClass.Plains => 1.0,
        TerrainClass.Oasis => 1.0,
        TerrainClass.Floodplains => 1.1,
        TerrainClass.Beach => 1.1,
        TerrainClass.Steppe => 1.15,
        TerrainClass.Drylands => 1.3,
        TerrainClass.Forest => 1.6,
        TerrainClass.Hills => 1.9,
        TerrainClass.Taiga => 2.0,
        TerrainClass.Desert => 2.4,
        TerrainClass.Jungle => 2.6,
        TerrainClass.Wetlands => 2.6,
        TerrainClass.Arctic => 4.0,
        TerrainClass.Mountains => 4.5,
        TerrainClass.DesertMountains => 5.5,
        _ => 1.5,
    };

    /// <summary>
    /// Builds the network.
    ///
    /// Hubs are the duchy capitals. A candidate road exists between every two duchies that touch
    /// by land, along the cheapest path over the barony graph, so a road follows valleys and
    /// coasts and climbs a pass only where the duchies meet across one. Impassable provinces
    /// carry no barony and so are simply not in the graph. A strait or river crossing from
    /// <see cref="Crossings"/> is a link like any other, priced as a ferry, so the far bank is
    /// reachable exactly where an army could march.
    ///
    /// A candidate lane exists from every port to its few nearest ports by water, along the
    /// cheapest path over the sea provinces, with open water dearer than coastal water so lanes
    /// hug the shore and cross only where they must.
    ///
    /// Then pruning: the cheapest spanning tree over everything is the backbone — which is what
    /// joins the landmasses, since a lane is the only candidate between them — and a further
    /// candidate is kept only if it shortens the journey between its hubs by at least a quarter
    /// and does not mostly retrace routes already kept.
    /// </summary>
    public static RouteNetwork Build(List<Title> empires, ProvinceMap provinces, int[] order,
        int baronyCount, TerrainClass[] provinceTerrain, Drainage? drainage, WildernessMap wilderness,
        CrossingMap crossings)
    {
        var survey = ProvinceSurvey.Take(provinces, order, baronyCount, drainage);
        var adjacency = Titles.BuildAdjacency(provinces, baronyCount, order);
        var water = WaterGraph(provinces, order, baronyCount);

        var position = new (double X, double Y)[provinces.Count + 1];
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= provinces.Count && label < provinces.Seeds.Count)
                position[id] = (provinces.Seeds[label].X, provinces.Seeds[label].Y);
        }

        // --- Hubs ---------------------------------------------------------------------------
        var hubs = new List<RouteHub>();
        var hubOfProvince = new Dictionary<int, int>();
        var duchyOfProvince = new Dictionary<int, int>();
        var duchies = Titles.Flatten(empires).Where(t => t.Tier == "d").ToList();

        for (int d = 0; d < duchies.Count; d++)
        {
            var duchy = duchies[d];
            foreach (var county in duchy.Children.Where(c => c.Tier == "c"))
                foreach (var b in county.Children)
                    if (b.ProvinceId >= 1 && b.ProvinceId <= baronyCount) duchyOfProvince[b.ProvinceId] = d;

            var seat = Capitals.CapitalCounty(duchy);
            var barony = seat?.Capital;
            if (seat is null || barony is null || barony.ProvinceId < 1 || barony.ProvinceId > baronyCount) continue;
            if (hubOfProvince.ContainsKey(barony.ProvinceId)) continue;

            var kingdom = duchy.Parent;
            hubs.Add(new RouteHub
            {
                Index = hubs.Count,
                Duchy = duchy,
                County = seat,
                Barony = barony,
                ProvinceId = barony.ProvinceId,
                Position = position[barony.ProvinceId],
                KingdomSeat = kingdom is { Tier: "k" } && ReferenceEquals(kingdom.Capital, duchy),
                Wilderness = wilderness.Contains(seat),
                Port = water.Shore.TryGetValue(barony.ProvinceId, out var shoreWater) && shoreWater.Count > 0,
            });
            hubOfProvince[barony.ProvinceId] = hubs.Count - 1;
        }

        if (hubs.Count < 2) return RouteNetwork.Empty;

        var hubOfDuchy = new Dictionary<int, int>();
        foreach (var hub in hubs) hubOfDuchy[duchies.IndexOf(hub.Duchy)] = hub.Index;

        // --- Ground cost --------------------------------------------------------------------
        var wildProvince = new bool[baronyCount + 1];
        foreach (var county in wilderness.Counties)
            foreach (var b in county.Children)
                if (b.ProvinceId >= 1 && b.ProvinceId <= baronyCount) wildProvince[b.ProvinceId] = true;

        var ground = new double[baronyCount + 1];
        for (int id = 1; id <= baronyCount; id++)
        {
            var terrain = id < provinceTerrain.Length ? provinceTerrain[id] : TerrainClass.Plains;
            double g = Ground(terrain) * (1 - RiverDiscount * survey.River(id));
            if (wildProvince[id]) g *= WildernessPenalty;
            ground[id] = g;
        }

        // --- Land candidates: duchies that touch by land or by a crossing ---------------------
        var wanted = new HashSet<int>[hubs.Count];
        for (int i = 0; i < wanted.Length; i++) wanted[i] = [];

        void Want(int provinceA, int provinceB)
        {
            if (!duchyOfProvince.TryGetValue(provinceA, out int da) || !hubOfDuchy.TryGetValue(da, out int ha)) return;
            if (!duchyOfProvince.TryGetValue(provinceB, out int db) || db == da || !hubOfDuchy.TryGetValue(db, out int hb)) return;
            wanted[ha].Add(hb);
            wanted[hb].Add(ha);
        }

        foreach (var (province, others) in adjacency)
            foreach (int other in others) Want(province, other);
        foreach (var c in crossings.Crossings) Want(c.From, c.To);

        var candidates = new List<RouteEdge>();
        var seen = new HashSet<(int, int)>();
        int unreachable = 0;

        var dist = new double[baronyCount + 1];
        var prev = new int[baronyCount + 1];
        var ferries = new int[baronyCount + 1];

        foreach (var hub in hubs)
        {
            var targets = wanted[hub.Index].Where(t => t > hub.Index).ToHashSet();
            if (targets.Count == 0) continue;

            Array.Fill(dist, double.PositiveInfinity);
            Array.Fill(prev, 0);
            Array.Fill(ferries, 0);
            var queue = new PriorityQueue<int, double>();
            dist[hub.ProvinceId] = 0;
            queue.Enqueue(hub.ProvinceId, 0);
            int remaining = targets.Count;

            while (remaining > 0 && queue.TryDequeue(out int at, out double d))
            {
                if (d > dist[at]) continue;

                if (hubOfProvince.TryGetValue(at, out int reached) && targets.Remove(reached))
                {
                    remaining--;
                    var path = new List<int>();
                    for (int p = at; p != 0; p = prev[p]) path.Add(p);
                    path.Reverse();

                    if (seen.Add((hub.Index, reached)))
                        candidates.Add(new RouteEdge
                        {
                            A = hub.Index, B = reached, Kind = RouteKind.Land, Provinces = path,
                            Cost = d, Length = Length(path, position), Crossings = ferries[at],
                        });
                }

                if (adjacency.TryGetValue(at, out var around))
                    foreach (int next in around)
                        Relax(next, d + Distance(position[at], position[next]) * 0.5 * (ground[at] + ground[next]), at, ferries[at]);

                if (crossings.ByProvince.TryGetValue(at, out var over))
                    foreach (var c in over)
                    {
                        int next = c.From == at ? c.To : c.From;
                        double ferry = c.Width * (c.Kind == CrossingKind.Strait ? StraitFerry : RiverFerry);
                        Relax(next, d + ferry + Distance(position[at], position[next]) * 0.5 * (ground[at] + ground[next]), at, ferries[at] + 1);
                    }

                void Relax(int next, double candidate, int from, int crossed)
                {
                    if (candidate >= dist[next]) return;
                    dist[next] = candidate;
                    prev[next] = from;
                    ferries[next] = crossed;
                    queue.Enqueue(next, candidate);
                }
            }

            unreachable += remaining;
        }

        // --- Sea candidates: each port to its nearest ports by water --------------------------
        double maxLane = provinces.Width * MaxLaneShare;
        var portsByWater = new Dictionary<int, List<int>>();
        foreach (var hub in hubs.Where(h => h.Port))
            foreach (int w in water.Shore[hub.ProvinceId])
            {
                if (!portsByWater.TryGetValue(w, out var list)) portsByWater[w] = list = [];
                list.Add(hub.Index);
            }

        var waterDist = new Dictionary<int, double>();
        var waterPrev = new Dictionary<int, int>();

        foreach (var hub in hubs.Where(h => h.Port))
        {
            waterDist.Clear();
            waterPrev.Clear();
            var queue = new PriorityQueue<int, double>();
            var found = new HashSet<int>();

            foreach (int w in water.Shore[hub.ProvinceId])
            {
                double d0 = Distance(position[hub.ProvinceId], position[w]) * water.Cost[w];
                waterDist[w] = d0;
                waterPrev[w] = 0;
                queue.Enqueue(w, d0);
            }

            while (found.Count < LanesPerPort && queue.TryDequeue(out int at, out double d))
            {
                if (d > waterDist[at] || d > maxLane) continue;

                if (portsByWater.TryGetValue(at, out var ports))
                    foreach (int other in ports)
                    {
                        if (other == hub.Index || !found.Add(other)) continue;
                        if (other < hub.Index && seen.Contains((other, hub.Index))) continue;

                        var path = new List<int> { hub.ProvinceId };
                        var lane = new List<int>();
                        for (int p = at; p != 0; p = waterPrev[p]) lane.Add(p);
                        lane.Reverse();
                        path.AddRange(lane);
                        path.Add(hubs[other].ProvinceId);

                        double cost = d + Distance(position[at], position[hubs[other].ProvinceId]) * CoastalWater;
                        int a = Math.Min(hub.Index, other), b = Math.Max(hub.Index, other);
                        if (!seen.Add((a, b))) continue;
                        if (a != hub.Index) path.Reverse();

                        candidates.Add(new RouteEdge
                        {
                            A = a, B = b, Kind = RouteKind.Sea, Provinces = path,
                            Cost = cost, Length = Length(path, position),
                        });
                    }

                if (!water.Links.TryGetValue(at, out var around)) continue;
                foreach (int next in around)
                {
                    double candidate = d + Distance(position[at], position[next]) * 0.5 * (water.Cost[at] + water.Cost[next]);
                    if (waterDist.TryGetValue(next, out double known) && candidate >= known) continue;
                    waterDist[next] = candidate;
                    waterPrev[next] = at;
                    queue.Enqueue(next, candidate);
                }
            }
        }

        // --- Pruning ------------------------------------------------------------------------
        candidates.Sort((x, y) => x.Cost.CompareTo(y.Cost));

        var parent = new int[hubs.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }

        var kept = new List<RouteEdge>();
        var links = new List<(int To, double Cost)>[hubs.Count];
        for (int i = 0; i < links.Length; i++) links[i] = [];
        var used = new HashSet<int>();

        void Keep(RouteEdge e)
        {
            kept.Add(e);
            links[e.A].Add((e.B, e.Cost));
            links[e.B].Add((e.A, e.Cost));
            for (int i = 1; i < e.Provinces.Count - 1; i++) used.Add(e.Provinces[i]);
        }

        // Backbone first: the cheapest spanning tree over everything, lanes included.
        foreach (var e in candidates)
        {
            int a = Find(e.A), b = Find(e.B);
            if (a == b) continue;
            parent[a] = b;
            e.Backbone = true;
            Keep(e);
        }

        // Then shortcuts, cheapest first, each judged against the network as it stands.
        foreach (var e in candidates)
        {
            if (e.Backbone) continue;

            int interior = Math.Max(1, e.Provinces.Count - 2);
            int shared = 0;
            for (int i = 1; i < e.Provinces.Count - 1; i++) if (used.Contains(e.Provinces[i])) shared++;
            if ((double)shared / interior > MaxOverlap) continue;

            double viaNetwork = NetworkDistance(links, e.A, e.B, e.Cost / ShortcutRatio);
            if (e.Cost >= ShortcutRatio * viaNetwork) continue;

            Keep(e);
        }

        // --- Traffic: how many hub-to-hub journeys each route carries ------------------------
        var edgeAt = new Dictionary<(int, int), RouteEdge>();
        foreach (var e in kept) { edgeAt[(e.A, e.B)] = e; edgeAt[(e.B, e.A)] = e; }

        foreach (var source in hubs)
        {
            var (hubDist, hubPrev) = ShortestPaths(links, source.Index);
            for (int t = 0; t < hubs.Count; t++)
            {
                if (t == source.Index || double.IsPositiveInfinity(hubDist[t])) continue;
                for (int at = t; hubPrev[at] >= 0; at = hubPrev[at])
                    edgeAt[(hubPrev[at], at)].Traffic++;
            }
        }

        if (kept.Count > 0)
        {
            int primaries = Math.Max(1, (int)Math.Round(kept.Count * PrimaryShare));
            foreach (var e in kept.OrderByDescending(e => e.Traffic).ThenBy(e => e.Cost).Take(primaries))
                e.Primary = true;
        }

        int components = hubs.Select(h => Find(h.Index)).Distinct().Count();

        return new RouteNetwork
        {
            Hubs = hubs,
            Edges = kept,
            Components = components,
            Unreachable = unreachable,
        };
    }

    private sealed class Water
    {
        /// <summary>Water province to the water and barony provinces it touches.</summary>
        public required Dictionary<int, List<int>> Links { get; init; }

        /// <summary>Barony province to the water provinces it touches.</summary>
        public required Dictionary<int, List<int>> Shore { get; init; }

        /// <summary>Sailing cost per water province, indexed by id.</summary>
        public required double[] Cost { get; init; }
    }

    /// <summary>
    /// The sea as a graph: water provinces linked to each other and to the baronies on their
    /// shores, with coastal water cheap and open water dear. Major rivers are navigable, at a
    /// price, so a lane can run up a great river to an inland port.
    /// </summary>
    private static Water WaterGraph(ProvinceMap provinces, int[] order, int baronyCount)
    {
        var links = new Dictionary<int, HashSet<int>>();
        var shore = new Dictionary<int, HashSet<int>>();

        bool IsWater(int label) => !provinces.Seeds[label].IsLand;
        bool IsBarony(int id) => id >= 1 && id <= baronyCount;

        int w = provinces.Width, h = provinces.Height;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int cell = y * w + x;
                int label = provinces.Label[cell];
                if (x + 1 < w) Link(label, provinces.Label[cell + 1]);
                if (y + 1 < h) Link(label, provinces.Label[cell + w]);
            }
        }

        void Link(int la, int lb)
        {
            if (la == lb) return;
            bool wa = IsWater(la), wb = IsWater(lb);
            if (!wa && !wb) return;
            int a = order[la], b = order[lb];

            if (wa && wb)
            {
                Add(links, a, b);
                Add(links, b, a);
            }
            else
            {
                int water = wa ? a : b, land = wa ? b : a;
                if (!IsBarony(land)) return;
                Add(links, water, land);
                Add(shore, land, water);
            }
        }

        static void Add(Dictionary<int, HashSet<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var set)) map[key] = set = [];
            set.Add(value);
        }

        var cost = new double[provinces.Count + 1];
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id < 1 || id > provinces.Count || !IsWater(label)) continue;
            var seed = provinces.Seeds[label];
            bool coastal = links.TryGetValue(id, out var around) && around.Any(IsBarony);
            cost[id] = seed.IsMajorRiver ? RiverWater : coastal ? CoastalWater : OpenWater;
        }

        return new Water
        {
            Links = links.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
            Shore = shore.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
            Cost = cost,
        };
    }

    /// <summary>Cheapest journey between two hubs over the routes kept so far, or infinity past <paramref name="limit"/>.</summary>
    private static double NetworkDistance(List<(int To, double Cost)>[] links, int from, int to, double limit)
    {
        var dist = new Dictionary<int, double> { [from] = 0 };
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(from, 0);

        while (queue.TryDequeue(out int at, out double d))
        {
            if (d > dist[at]) continue;
            if (at == to) return d;
            if (d > limit) return double.PositiveInfinity;

            foreach (var (next, cost) in links[at])
            {
                double candidate = d + cost;
                if (dist.TryGetValue(next, out double known) && candidate >= known) continue;
                dist[next] = candidate;
                queue.Enqueue(next, candidate);
            }
        }

        return double.PositiveInfinity;
    }

    private static (double[] Dist, int[] Prev) ShortestPaths(List<(int To, double Cost)>[] links, int from)
    {
        var dist = new double[links.Length];
        var prev = new int[links.Length];
        Array.Fill(dist, double.PositiveInfinity);
        Array.Fill(prev, -1);

        var queue = new PriorityQueue<int, double>();
        dist[from] = 0;
        queue.Enqueue(from, 0);

        while (queue.TryDequeue(out int at, out double d))
        {
            if (d > dist[at]) continue;
            foreach (var (next, cost) in links[at])
            {
                double candidate = d + cost;
                if (candidate >= dist[next]) continue;
                dist[next] = candidate;
                prev[next] = at;
                queue.Enqueue(next, candidate);
            }
        }

        return (dist, prev);
    }

    private static double Length(List<int> path, (double X, double Y)[] position)
    {
        double length = 0;
        for (int i = 1; i < path.Count; i++) length += Distance(position[path[i - 1]], position[path[i]]);
        return length;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
