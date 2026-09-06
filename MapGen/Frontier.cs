using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// One frontier: a connected stretch of wilderness plus the ring of settled counties that touch it.
/// Becomes one sub-region of the Wilds situation, with its own phase and its own participants.
/// </summary>
public sealed class FrontierSubRegion
{
    /// <summary>Sub-region key inside the situation type, <c>wilds_1</c> onward, west to east.</summary>
    public required string Key { get; init; }

    /// <summary>The geographical region the sub-region is bound to, <c>gen_wilds_region_N</c>.</summary>
    public required string RegionKey { get; init; }

    public required string Name { get; init; }

    /// <summary>The unsettled counties. What the counters count and what the phases are about.</summary>
    public required List<Title> Wild { get; init; }

    /// <summary>
    /// Settled counties one hop out. Inside the region so that the lords holding them are
    /// participants — a ruler's own domain has to touch the sub-region to join, and until a
    /// colony is founded nobody's domain touches the wild counties themselves.
    /// </summary>
    public required List<Title> Ring { get; init; }

    public required (int R, int G, int B) Color { get; init; }

    public IEnumerable<Title> Counties => Wild.Concat(Ring);
}

public sealed class FrontierMap
{
    public IReadOnlyList<FrontierSubRegion> SubRegions { get; }

    /// <summary>Every land county on the map, the denominator of "how much of the known world".</summary>
    public int LandCounties { get; }

    public FrontierMap(List<FrontierSubRegion> subRegions, int landCounties)
    {
        SubRegions = subRegions;
        LandCounties = landCounties;
    }

    public static FrontierMap Empty => new([], 0);

    public bool IsEmpty => SubRegions.Count == 0;

    public int WildCount => SubRegions.Sum(s => s.Wild.Count);

    // No RegionMembers() here, unlike SteppeMap and SilkRoadMap. Those two hand their members to
    // CompatibilityWriter because some of their region keys are VANILLA keys, which that writer
    // re-declares from vanilla's own files and so has to be told about. Every key here is ours and
    // appears in no vanilla file, so FrontierWriter writes the region file itself — the same route
    // SteppeWriter.WriteOwnRegions takes for its two non-vanilla keys.
}

/// <summary>
/// Cuts the wilderness into frontiers for the Wilds situation.
///
/// The situation binds to the map through geographical regions, which are static lists, so what
/// is decided here is decided for the whole game: which wild counties share an era, and which
/// settled lords are on the edge of it. Everything that changes in play — how many counties are
/// still wild, how many colonies stand — is counted by script at runtime, not read from the
/// region.
/// </summary>
public static class Frontier
{
    /// <summary>
    /// Vanilla runs three to six sub-regions per situation and the window is laid out for
    /// about that many. The engine allows 255; the tab does not.
    /// </summary>
    private const int MaxSubRegions = 6;

    /// <summary>A piece of wilderness worth its own era. Smaller pieces join a neighbour's.</summary>
    private const int LargeComponent = 6;

    /// <summary>The fewest settled counties a frontier is given on its edge.</summary>
    private const int MinRing = 4;

    /// <summary>
    /// One colour per frontier, for the situation's <c>sub_regions</c> map mode.
    ///
    /// Six evenly spaced hues at high saturation. The first attempt was six dark, muted earth
    /// tones, and three of them were green — measured in CIE Lab the closest pair sat at dE 14,
    /// which is a difference you cannot see across two counties on opposite sides of the map, and
    /// the mean L* was 42, dark enough that the whole mode read as mud. These are dE 41 apart at
    /// worst with mean L* 62. Vanilla's Steppe is the calibration: three sub-regions in bright
    /// gold, green and strong blue, not earth tones.
    ///
    /// Order matters — frontiers are keyed west to east, so adjacent keys are usually adjacent on
    /// the map, and consecutive entries here are the ones furthest apart in hue.
    /// </summary>
    private static readonly (int R, int G, int B)[] Palette =
    [
        (235, 161, 35),
        (106, 184, 55),
        (40, 199, 183),
        (49, 125, 224),
        (172, 92, 204),
        (219, 61, 109),
    ];

    public static FrontierMap Build(List<Title> counties, ProvinceMap provinces, int[] order,
        int landCount, TerrainClass[] provinceTerrain, WildernessMap wilderness)
    {
        if (counties.Count == 0 || wilderness.Count == 0) return FrontierMap.Empty;

        var graph = Cultures.BuildCountyGraph(counties, provinces, order, landCount, provinceTerrain, 1.0);

        // Unsettled only. A ruin is wilderness to every other writer, but a ruin is a county
        // inside somebody's kingdom that fell, and the frontier is the ground nobody ever held.
        // Folding ruins in would make a one-county "frontier" in the middle of a realm.
        var isWild = new bool[counties.Count];
        for (int i = 0; i < counties.Count; i++)
            isWild[i] = wilderness.Contains(counties[i]) && !wilderness.IsRuin(counties[i]);

        var components = Steppe.Components(graph, isWild)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c[0])
            .ToList();
        if (components.Count == 0) return FrontierMap.Empty;

        // Each large piece is a frontier of its own. The rest join the nearest one whatever the
        // distance, because a wild county outside every sub-region is a county whose colonist
        // is not a participant and whose founding fires no catalyst — and unlike the Steppe,
        // where a sub-region is a place that shares weather, a frontier is a place that shares
        // an era, which two scraps an ocean apart can do without looking wrong.
        var groups = components.Where(c => c.Count >= LargeComponent).Take(MaxSubRegions).ToList();
        if (groups.Count == 0) groups = [components[0]];

        var centroids = groups.Select(g => Centroid(graph, g)).ToList();
        foreach (var scrap in components.Where(c => !groups.Contains(c)))
        {
            var at = Centroid(graph, scrap);
            int nearest = 0;
            double best = double.MaxValue;
            for (int g = 0; g < centroids.Count; g++)
            {
                double d = Distance(at, centroids[g]);
                if (d < best) { best = d; nearest = g; }
            }
            groups[nearest].AddRange(scrap);
        }

        // Recomputed now that the scraps are in. The pre-merge centroids above are the right thing
        // to measure a scrap against — they are where the frontier proper is — but everything
        // below (the ring top-up, and the west-to-east ordering that decides the keys) is about
        // where the whole sub-region ended up.
        for (int g = 0; g < groups.Count; g++) centroids[g] = Centroid(graph, groups[g]);

        // The ring: every SETTLED neighbour of a wild county, claimed by the first (largest)
        // frontier that reaches it, since sub-regions may not overlap.
        //
        // "Settled" excludes ruins as well as wilderness. Every non-ruin wild county is already in
        // a group, so a ruin is the only kind of wilderness a neighbour walk can still reach — and
        // a ruined county is not a lord on the edge of anything. Its holder is the ruins dummy,
        // which the participant groups reject anyway, so letting one in would only paint it as
        // frontier on the map mode and pad the region for nothing.
        var settled = new bool[counties.Count];
        for (int i = 0; i < counties.Count; i++) settled[i] = !wilderness.Contains(counties[i]);

        var claimed = new bool[counties.Count];
        foreach (var g in groups) foreach (int i in g) claimed[i] = true;

        var rings = new List<List<int>>();
        foreach (var g in groups)
        {
            var ring = new List<int>();
            foreach (int i in g)
                foreach (int n in graph.Neighbours[i])
                {
                    if (claimed[n] || !settled[n]) continue;
                    claimed[n] = true;
                    ring.Add(n);
                }
            rings.Add(ring);
        }

        // A wild island has no neighbours and a wild peninsula may have one, and a frontier with
        // no edge has no marcher lords: nobody's domain touches it until a colony stands. The
        // lords across the strait are the natural colonisers, so a thin ring is topped up with
        // the nearest settled counties by distance until it has a few.
        for (int g = 0; g < groups.Count; g++)
        {
            if (rings[g].Count >= MinRing) continue;
            var centre = centroids[g];
            // Materialised before the loop below writes to `claimed`, which this query reads.
            var nearest = Enumerable.Range(0, counties.Count)
                .Where(i => !claimed[i] && settled[i])
                .OrderBy(i => Distance(graph.Position[i], centre))
                .Take(MinRing - rings[g].Count)
                .ToList();
            foreach (int i in nearest)
            {
                claimed[i] = true;
                rings[g].Add(i);
            }
        }

        // West to east, so wilds_1 is always the westernmost and the order is stable to read.
        var indices = Enumerable.Range(0, groups.Count)
            .OrderBy(g => centroids[g].X)
            .ToList();

        // One kingdom split across two frontiers would name both after itself; a compass word
        // tells them apart. Twins are found on the base names before any is renamed, or the
        // second of a pair would look unique by the time its turn came.
        var baseNames = indices.Select(g => BaseName(groups[g].Select(i => counties[i]).ToList())).ToList();
        var names = new List<string>(baseNames);
        for (int s = 0; s < names.Count; s++)
        {
            var twins = Enumerable.Range(0, baseNames.Count).Where(t => baseNames[t] == baseNames[s]).ToList();
            if (twins.Count == 1) continue;
            var c = centroids[indices[s]];
            double meanX = twins.Average(t => centroids[indices[t]].X);
            double meanY = twins.Average(t => centroids[indices[t]].Y);
            names[s] = Compass(c, (meanX, meanY)) + " " + baseNames[s];
        }

        var subRegions = new List<FrontierSubRegion>();
        for (int s = 0; s < indices.Count; s++)
        {
            int g = indices[s];
            subRegions.Add(new FrontierSubRegion
            {
                Key = $"wilds_{s + 1}",
                RegionKey = $"gen_wilds_region_{s + 1}",
                Name = names[s],
                Wild = groups[g].Select(i => counties[i]).ToList(),
                Ring = rings[g].Select(i => counties[i]).ToList(),
                Color = Palette[s % Palette.Length],
            });
        }

        return new FrontierMap(subRegions, counties.Count);
    }

    /// <summary>
    /// Named for the de jure kingdom that holds most of the wild ground, the way the Steppe
    /// sub-regions are, so the situation window reads as this world's. A stretch no kingdom
    /// claims falls back to the duchy, and then to the bare word.
    /// </summary>
    private static string BaseName(List<Title> wild)
    {
        var kingdom = Dominant(wild.Select(c => c.Parent?.Parent), "k");
        var anchor = kingdom ?? Dominant(wild.Select(c => c.Parent), "d");
        return anchor is null ? "The Wilds" : $"{anchor.Name} Wilds";
    }

    private static Title? Dominant(IEnumerable<Title?> titles, string tier)
        => titles.Where(t => t is not null && t.Tier == tier && t.Name.Length > 0)
            .GroupBy(t => t!)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();

    /// <summary>Image coordinates: smaller Y is further north.</summary>
    private static string Compass((double X, double Y) at, (double X, double Y) mean)
    {
        double dx = at.X - mean.X, dy = at.Y - mean.Y;
        if (Math.Abs(dx) >= Math.Abs(dy)) return dx < 0 ? "Western" : "Eastern";
        return dy < 0 ? "Northern" : "Southern";
    }

    private static (double X, double Y) Centroid(RegionGrowth.Graph graph, List<int> nodes)
    {
        double x = 0, y = 0;
        foreach (int i in nodes) { x += graph.Position[i].X; y += graph.Position[i].Y; }
        return (x / nodes.Count, y / nodes.Count);
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
