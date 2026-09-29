namespace Ck3MapGen.MapGen;

/// <summary>
/// County-level adjacency lifted from <see cref="Titles.LandAdjacency"/>, for the stages that walk
/// counties by index — culture and faith growth, the wilderness, world centres. Each of them used to
/// carry its own copy of these loops; what differs between them (how a county's centre is taken,
/// what it costs to enter) stays with them.
/// </summary>
internal static class CountyNetwork
{
    /// <summary>
    /// For each county in <paramref name="counties"/>, the indices of the counties it touches, each
    /// pair linked once. List order is the order links are first met walking the province graph,
    /// which the region growers depend on for their tie-breaks — do not sort or dedupe differently.
    /// </summary>
    public static List<int>[] Neighbours(IReadOnlyList<Title> counties, ProvinceMap provinces,
        int[] order, int provinceCount)
    {
        var countyOfProvince = new Dictionary<int, int>();
        for (int i = 0; i < counties.Count; i++)
            foreach (var barony in counties[i].Children)
                if (barony.ProvinceId > 0) countyOfProvince[barony.ProvinceId] = i;

        var neighbours = new List<int>[counties.Count];
        for (int i = 0; i < neighbours.Length; i++) neighbours[i] = [];

        var linked = new HashSet<(int, int)>();
        foreach (var (province, others) in Titles.LandAdjacency(provinces, provinceCount, order))
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

        return neighbours;
    }

    /// <summary>
    /// Counties as nodes, linked where their provinces touch, each entered at its baronies' mean
    /// <paramref name="resistance"/> and placed at their seeds' centre — the graph cultures and
    /// faiths grow over, each with its own idea of what terrain holds it back.
    ///
    /// Impassable provinces carry no barony and so belong to no county, which means they silently
    /// drop out of the graph rather than bridging across it. That is the behaviour we want and it
    /// is worth stating: a mountain wall the game routes armies around also stops a language, so
    /// culture borders land on it without anything here having to look for ridgelines.
    /// </summary>
    public static RegionGrowth.Graph Graph(List<Title> counties, ProvinceMap provinces, int[] order, int landCount,
        TerrainClass[] provinceTerrain, Func<TerrainClass, double> resistance, double terrainWeight)
    {
        var neighbours = Neighbours(counties, provinces, order, landCount);
        var seedOfProvince = SeedOfProvince(order, landCount);

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

                total += resistance(provinceTerrain[id]);
                var seed = provinces.Seeds[seedOfProvince[id]];
                x += seed.X;
                y += seed.Y;
                counted++;
            }

            double mean = counted == 0 ? 1.5 : total / counted;

            // Terrain resistance is interpolated against flat ground rather than used raw, so one
            // weight dials the whole map between "borders ignore terrain" and "borders are terrain".
            cost[i] = Math.Max(0.1, 1.0 + (mean - 1.0) * terrainWeight);

            position[i] = counted == 0 ? (0, 0) : (x / counted, y / counted);
        }

        return new RegionGrowth.Graph { Neighbours = neighbours, EnterCost = cost, Position = position };
    }

    /// <summary>
    /// County-to-county adjacency keyed by the titles themselves, lifted from one or more province
    /// graphs — the land graph, and the sea bridges or crossings a caller also counts — plus any
    /// extra province pairs. Every county in <paramref name="counties"/> has an entry, empty when it
    /// touches nothing, and each link is added both ways as the graphs are walked in the order given.
    ///
    /// Four stages built this by hand (Realms, Prehistory, Capitals, the Silk Road), and they
    /// differed only in whether a county with no neighbours got an entry and whether a link went in
    /// both ways at once — neither of which any of them depended on.
    /// </summary>
    /// <param name="maxProvince">Provinces above this are not counties' (the land or barony count).</param>
    public static Dictionary<Title, HashSet<Title>> ByTitle(IEnumerable<Title> counties, int maxProvince,
        IEnumerable<IReadOnlyDictionary<int, HashSet<int>>> graphs, IEnumerable<(int A, int B)>? links = null)
    {
        var countyOf = new Dictionary<int, Title>();
        var adjacent = new Dictionary<Title, HashSet<Title>>();
        foreach (var county in counties)
        {
            adjacent[county] = [];
            foreach (var barony in county.Children)
                if (barony.ProvinceId >= 1 && barony.ProvinceId <= maxProvince) countyOf[barony.ProvinceId] = county;
        }

        void Link(int a, int b)
        {
            if (!countyOf.TryGetValue(a, out var ca) || !countyOf.TryGetValue(b, out var cb) || ReferenceEquals(ca, cb))
                return;
            adjacent[ca].Add(cb);
            adjacent[cb].Add(ca);
        }

        foreach (var graph in graphs)
            foreach (var (province, others) in graph)
                foreach (int other in others)
                    Link(province, other);

        foreach (var (a, b) in links ?? [])
            Link(a, b);

        return adjacent;
    }

    /// <summary>
    /// Province id 1..<paramref name="provinceCount"/> back to the index of the seed that made it,
    /// so a county can be given a position without another pass over the raster. Every id in range
    /// has one: <paramref name="order"/> numbers every province exactly once.
    /// </summary>
    public static int[] SeedOfProvince(int[] order, int provinceCount)
    {
        var seedOfProvince = new int[provinceCount + 1];
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= provinceCount) seedOfProvince[id] = label;
        }

        return seedOfProvince;
    }

    /// <summary>Whether <paramref name="id"/> is a province <see cref="SeedOfProvince"/> covers.</summary>
    public static bool Covers(int[] seedOfProvince, int id) => id >= 1 && id < seedOfProvince.Length;
}
