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
