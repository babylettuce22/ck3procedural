using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// One sub-region of the Great Steppe situation: a contiguous run of counties that shares a season.
/// </summary>
public sealed class SteppeSubRegion
{
    /// <summary>The sub-region key — vanilla's <c>steppe_west</c> family, plus two of ours at the ends.</summary>
    public required string Key { get; init; }

    /// <summary>The geographical region the sub-region is bound to — <c>world_steppe_west</c> and so on.</summary>
    public required string RegionKey { get; init; }

    /// <summary>
    /// Whether vanilla declares <see cref="RegionKey"/>. A vanilla key is re-declared by the
    /// compatibility writer with our counties; one of ours has to be written by the steppe writer.
    /// </summary>
    public required bool VanillaRegion { get; init; }

    /// <summary>Display name, replacing vanilla's "Western Steppe" family.</summary>
    public required string Name { get; init; }

    public required List<Title> Counties { get; init; }

    /// <summary>The sub-region's map-mode colour, vanilla's for the same slot.</summary>
    public required (int R, int G, int B) Color { get; init; }
}

/// <summary>
/// One of the regions vanilla's Expand the Steppe decision can add to a sub-region.
///
/// The decision is a fixed list of seventeen vanilla region keys, each hardwired to the
/// sub-region it joins, so the keys cannot change; what can is the ground under them. Each is
/// given a run of counties along the outside of the sub-region it is wired to, so the decision
/// offers real frontier rather than the one arbitrary county the key would otherwise carry.
/// </summary>
public sealed class SteppeExpansion
{
    /// <summary>The vanilla region key the decision item names.</summary>
    public required string Key { get; init; }

    /// <summary>The sub-region key the decision adds it to.</summary>
    public required string SubRegionKey { get; init; }

    /// <summary>Null when the region is parked out of reach because its sub-region is absent or its frontier is full.</summary>
    public string? Name { get; init; }

    public required List<Title> Counties { get; init; }

    public bool Parked => Name is null;
}

/// <summary>
/// Where the Great Steppe situation lives on this map.
///
/// Vanilla binds the situation to the map through geographical regions alone: three sub-regions,
/// each a list of duchies across the Pontic, Kazakh and Mongolian steppe. Nothing in it names a
/// province. This is the generated equivalent — the counties the situation covers, split into
/// sub-regions ordered west to east so vanilla's own keys can be reused, since a handful of
/// base-game script hardcodes <c>situation_sub_region:steppe_west</c>.
/// </summary>
public sealed class SteppeMap
{
    private readonly Dictionary<Title, int> _subRegionOf;

    public IReadOnlyList<SteppeSubRegion> SubRegions { get; }

    public IReadOnlyList<SteppeExpansion> Expansions { get; }

    /// <summary>Counties that are nomadic and sit inside the belt, for the run log.</summary>
    public int NomadCount { get; }

    internal SteppeMap(List<SteppeSubRegion> subRegions, List<SteppeExpansion> expansions, int nomadCount)
    {
        SubRegions = subRegions;
        Expansions = expansions;
        NomadCount = nomadCount;
        _subRegionOf = [];
        for (int i = 0; i < subRegions.Count; i++)
            foreach (var county in subRegions[i].Counties) _subRegionOf[county] = i;
    }

    public static SteppeMap Empty => new([], [], 0);

    public bool IsEmpty => SubRegions.Count == 0;

    public int Count => _subRegionOf.Count;

    public bool Contains(Title county) => _subRegionOf.ContainsKey(county);

    /// <summary>Index into <see cref="SubRegions"/>, or -1 outside the belt.</summary>
    public int SubRegionOf(Title county) => _subRegionOf.GetValueOrDefault(county, -1);

    /// <summary>Every county in the situation, across all sub-regions.</summary>
    public IEnumerable<Title> Counties => SubRegions.SelectMany(s => s.Counties);

    /// <summary>
    /// The vanilla geographical region keys this map gives a meaning to, each with its counties:
    /// the sub-regions bound to vanilla keys, vanilla's <c>world_steppe</c> parent (which script
    /// reads as "anywhere on the steppe" and which vanilla declares as the union of its
    /// sub-regions), and the seventeen expansion regions. Sub-regions on keys of our own are
    /// left out; the steppe writer declares those itself.
    /// </summary>
    public Dictionary<string, List<Title>> RegionMembers()
    {
        var members = new Dictionary<string, List<Title>>(StringComparer.Ordinal);
        foreach (var s in SubRegions.Where(s => s.VanillaRegion)) members[s.RegionKey] = s.Counties;
        if (SubRegions.Count > 0) members[Steppe.ParentRegionKey] = Counties.ToList();
        foreach (var e in Expansions) members[e.Key] = e.Counties;
        return members;
    }
}

public static class Steppe
{
    public const string ParentRegionKey = "world_steppe";

    private sealed record Slot(string Key, string Region, bool Vanilla, string Compass, (int R, int G, int B) Color);

    /// <summary>
    /// The slots, west to east. The middle three are vanilla's: its script hardcodes the keys,
    /// its map mode paints the colours, so a player who knows the base game reads ours the same
    /// way. The two at the ends are ours, for a world whose steppe is in more pieces than
    /// Eurasia's — the schema allows 255 sub-regions, and only the three vanilla keys are ever
    /// named in script.
    /// </summary>
    private static readonly Slot[] Slots =
    [
        new("steppe_far_west", "world_steppe_far_west", false, "Far Western", (150, 40, 160)),
        new("steppe_west", "world_steppe_west", true, "Western", (205, 169, 0)),
        new("steppe_central", "world_steppe_central", true, "Central", (14, 122, 25)),
        new("steppe_east", "world_steppe_east", true, "Eastern", (10, 47, 202)),
        new("steppe_far_east", "world_steppe_far_east", false, "Far Eastern", (220, 110, 20)),
    ];

    public static int MaxSubRegions => Slots.Length;

    /// <summary>
    /// Which slots a belt of <paramref name="count"/> sub-regions occupies, west to east. The
    /// vanilla keys are always taken first — <c>steppe_west</c> above all, since it is the one
    /// key script reaches for by name — and the outer two only when the belt needs them.
    /// </summary>
    private static int[] SlotOrder(int count) => count switch
    {
        1 => [1],
        2 => [1, 3],
        3 => [1, 2, 3],
        4 => [1, 2, 3, 4],
        _ => [0, 1, 2, 3, 4],
    };

    /// <summary>
    /// Vanilla's Expand the Steppe items, in the order the decision lists them, each with the
    /// sub-region the decision's effect adds it to. Both halves are hardcoded in base-game
    /// script (<c>expanding_steppe_effect</c>), so neither can be generated.
    /// </summary>
    private static readonly (string Key, string SubRegion)[] ExpansionSlots =
    [
        ("custom_eastern_balkans", "steppe_west"),
        ("ghw_region_northern_russia", "steppe_west"),
        ("ghw_region_southern_russia", "steppe_west"),
        ("dlc_mpo_steppe_caucasus_expansion", "steppe_west"),
        ("ghw_region_poland", "steppe_west"),
        ("custom_hungary", "steppe_west"),
        ("ghw_region_anatolia", "steppe_west"),
        ("ghw_region_baltic", "steppe_west"),
        ("world_transoxiana", "steppe_central"),
        ("dlc_mpo_steppe_siberia_further_expansion", "steppe_central"),
        ("world_khorasan", "steppe_central"),
        ("dlc_mpo_steppe_persia_expansion", "steppe_central"),
        ("dlc_mpo_steppe_hexi_tarim_expansion", "steppe_east"),
        ("dlc_mpo_steppe_north_china_expansion", "steppe_east"),
        ("dlc_mpo_steppe_central_china_expansion", "steppe_east"),
        ("world_asia_korea", "steppe_east"),
        ("world_asia_japan", "steppe_east"),
    ];

    /// <summary>
    /// A fragment of steppe with no nomad on it is left out below this size. Vanilla's smallest
    /// sub-region is about a hundred counties; a patch of four grassland counties inside a feudal
    /// kingdom is a meadow, not a steppe, and giving it its own weather reads as a bug.
    /// </summary>
    private const int MinFragment = 4;

    /// <summary>Roughly how many counties earn a sub-region of their own.</summary>
    private const int CountiesPerSubRegion = 25;

    /// <summary>A component this large gets its own sub-region even when the total is small.</summary>
    private const int LargeComponent = 8;

    /// <summary>How deep past the belt's edge an expansion region may reach, in counties.</summary>
    private const int FrontierDepth = 2;

    /// <summary>The smallest run of counties worth offering as a place to expand into.</summary>
    private const int MinExpansion = 5;

    /// <summary>
    /// Chooses and partitions the belt.
    ///
    /// Membership is decided by ground and by government together. Every county whose dominant
    /// terrain is steppe is in; so is every nomadic county wherever it stands, because the
    /// Migrate interaction requires the actor to be a capital-group participant of a migration
    /// situation, and a nomad outside the belt is a nomad who can never move. Enclosed holes and
    /// the arid fringe are then filled so a settled valley inside the grassland shares its
    /// seasons, which is what vanilla's duchy-granular lists do too.
    ///
    /// The result is split with the same cost-weighted growth that draws cultures, so the seams
    /// between sub-regions fall on the ground that resists crossing rather than on a meridian.
    /// </summary>
    public static SteppeMap Build(List<Title> counties, ProvinceMap provinces, int[] order,
        int landCount, TerrainClass[] provinceTerrain, GovernmentMap governments, Rng rng)
    {
        if (counties.Count == 0) return SteppeMap.Empty;

        // Terrain weight 1.0: the same resistance cultures feel. The belt is mostly flat and
        // cheap, so the partition seams land on the hills and deserts that cross it.
        var graph = Cultures.BuildCountyGraph(counties, provinces, order, landCount, provinceTerrain, 1.0);

        var isMember = new bool[counties.Count];
        var isNomad = new bool[counties.Count];

        for (int i = 0; i < counties.Count; i++)
        {
            var county = counties[i];
            isNomad[i] = governments.IsNomad(county);
            isMember[i] = isNomad[i]
                       || Development.DominantTerrain(county, provinceTerrain) == TerrainClass.Steppe;
        }

        FillHoles(graph, counties, provinceTerrain, isMember);

        // Fragments: a component with no nomad and too few counties is dropped. One with a
        // nomad is kept at any size, since dropping it strands a horde.
        var components = Components(graph, isMember)
            .Where(c => c.Count >= MinFragment || c.Any(i => isNomad[i]))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c[0])
            .ToList();

        if (components.Count == 0) return SteppeMap.Empty;

        // Sub-regions are cut from connected pieces, never across them. A single partition over
        // everything sweeps each unreachable fragment into whichever seed is nearest, which put
        // one "region" on three separate landmasses in testing — and a sub-region is a place
        // that shares a season, which two steppes an ocean apart do not.
        //
        // So each landmass-sized piece gets at least one sub-region, and the slots left over go
        // to whichever piece has the most counties per sub-region, as long as splitting it still
        // leaves each half a real steppe. A belt too small to be anything but one piece stays one.
        var large = components.Where(c => c.Count >= LargeComponent).Take(MaxSubRegions).ToList();
        if (large.Count == 0) large = [components[0]];

        var groups = Split(graph, large, MaxSubRegions, CountiesPerSubRegion, rng);

        // The rest are scraps: a nomadic one joins the nearest sub-region whatever the distance,
        // because a horde must be in the situation to migrate at all; a plain one joins only if
        // it sits within reach of that sub-region's own extent, and is otherwise a meadow.
        var centroids = groups.Select(g => Centroid(graph, g)).ToList();
        var reach = groups.Select((g, i) => Reach(graph, g, centroids[i])).ToList();

        foreach (var scrap in components.Where(c => !large.Contains(c)))
        {
            int nearest = Nearest(Centroid(graph, scrap), centroids, out double distance);
            bool nomadic = scrap.Any(i => isNomad[i]);
            if (nomadic || distance <= reach[nearest]) groups[nearest].AddRange(scrap);
        }

        // West to east by centroid, so the westernmost is always in the westernmost slot and
        // the key names stay truthful on the map.
        var ordered = groups
            .OrderBy(g => g.Average(i => graph.Position[i].X))
            .Take(Slots.Length)
            .ToList();

        var slots = SlotOrder(ordered.Count);
        var owned = ordered.Select(g => g.Select(i => counties[i]).ToList()).ToList();
        var names = owned.Select(g => KingdomName(g, "Steppe")).ToList();

        // One kingdom split across two sub-regions would name both after itself; the compass
        // word vanilla uses for all of its own tells them apart.
        for (int s = 0; s < names.Count; s++)
            if (names.Count(n => n == names[s]) > 1)
                names[s] = $"{Slots[slots[s]].Compass} {names[s]}";

        var subRegions = new List<SteppeSubRegion>();
        for (int s = 0; s < owned.Count; s++)
        {
            var slot = Slots[slots[s]];
            subRegions.Add(new SteppeSubRegion
            {
                Key = slot.Key,
                RegionKey = slot.Region,
                VanillaRegion = slot.Vanilla,
                Name = names[s],
                Counties = owned[s],
                Color = slot.Color,
            });
        }

        var inBelt = new bool[counties.Count];
        foreach (var g in ordered) foreach (int i in g) inBelt[i] = true;

        var expansions = Expansions(graph, counties, ordered, subRegions, inBelt, rng);

        int nomadCount = ordered.Sum(g => g.Count(i => isNomad[i]));
        return new SteppeMap(subRegions, expansions, nomadCount);
    }

    /// <summary>
    /// Cuts up to <paramref name="slots"/> groups from connected pieces, never across them.
    ///
    /// Each piece gets one group; the slots left over go to whichever piece has the most
    /// counties per group, as long as splitting it still leaves each part at least
    /// <paramref name="countiesPerGroup"/>. Within one piece every member is reachable, so
    /// nothing is swept to a seed by distance.
    /// </summary>
    private static List<List<int>> Split(RegionGrowth.Graph graph, List<List<int>> pieces,
        int slots, int countiesPerGroup, Rng rng)
    {
        var quota = new int[pieces.Count];
        Array.Fill(quota, 1);
        for (int spare = slots - pieces.Count; spare > 0; spare--)
        {
            int best = -1;
            double bestShare = countiesPerGroup;
            for (int i = 0; i < pieces.Count; i++)
            {
                double share = (double)pieces[i].Count / (quota[i] + 1);
                if (share >= bestShare) { bestShare = share; best = i; }
            }
            if (best < 0) break;
            quota[best]++;
        }

        var groups = new List<List<int>>();
        for (int c = 0; c < pieces.Count; c++)
        {
            // Partition returns one slot per graph node, indexed by node id — read it as
            // owner[node], never positionally over members. See MapGen/Faiths.cs for the bug that
            // rule prevents.
            var owner = RegionGrowth.Partition(graph, pieces[c], quota[c], rng, out _);

            var split = new List<int>[quota[c]];
            for (int r = 0; r < split.Length; r++) split[r] = [];
            foreach (int m in pieces[c])
                if (owner[m] >= 0 && owner[m] < split.Length) split[owner[m]].Add(m);

            groups.AddRange(split.Where(g => g.Count > 0));
        }

        return groups;
    }

    /// <summary>
    /// Ground for vanilla's Expand the Steppe decision.
    ///
    /// For each sub-region the decision knows how to grow, the counties just outside it — up to
    /// <see cref="FrontierDepth"/> deep, and not in any other sub-region — are cut into as many
    /// runs as its wired keys can use, and each key is given one. A key left over, or wired to a
    /// sub-region this map does not have, is parked on the one county farthest from the belt,
    /// where the decision's adjacency check cannot see it: every vanilla region key has to be
    /// declared with a member or the engine refuses the whole database, so "nowhere" has to be
    /// somewhere.
    /// </summary>
    private static List<SteppeExpansion> Expansions(RegionGrowth.Graph graph, List<Title> counties,
        List<List<int>> groups, List<SteppeSubRegion> subRegions, bool[] inBelt, Rng rng)
    {
        var result = new List<SteppeExpansion>();

        // Farthest from the belt by hops, so a parked key is not accidentally next door.
        var hops = HopsFrom(graph, groups.SelectMany(g => g), inBelt);
        int parkedAt = 0;
        for (int i = 0; i < hops.Length; i++)
            if (hops[i] > hops[parkedAt] || (hops[parkedAt] < 0 && hops[i] >= 0)) parkedAt = i;
        var parked = new List<Title> { counties[parkedAt] };

        var claimed = new bool[counties.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var wiredKey in ExpansionSlots.Select(e => e.SubRegion).Distinct())
        {
            var keys = ExpansionSlots.Where(e => e.SubRegion == wiredKey).Select(e => e.Key).ToList();
            int index = subRegions.FindIndex(s => s.Key == wiredKey);

            var runs = new List<List<int>>();
            if (index >= 0)
            {
                // The frontier: outside every sub-region, within reach of this one.
                var near = HopsFrom(graph, groups[index], inBelt);
                var frontier = new List<int>();
                for (int i = 0; i < near.Length; i++)
                    if (!inBelt[i] && !claimed[i] && near[i] >= 1 && near[i] <= FrontierDepth)
                        frontier.Add(i);

                var pieces = Components(graph, Flags(frontier, counties.Count))
                    .Where(p => p.Count >= MinExpansion)
                    .OrderByDescending(p => p.Count)
                    .ThenBy(p => p[0])
                    .Take(keys.Count)
                    .ToList();

                if (pieces.Count > 0)
                    runs = Split(graph, pieces, keys.Count, MinExpansion, rng);

                // The growth can leave a run of one or two counties where a seed was boxed in;
                // a decision offering a single county as a "region" reads as broken, so runts
                // are folded into the nearest run large enough to stand.
                var standing = runs.Where(r => r.Count >= MinExpansion).ToList();
                if (standing.Count > 0)
                {
                    var centres = standing.Select(r => Centroid(graph, r)).ToList();
                    foreach (var runt in runs.Where(r => r.Count < MinExpansion))
                        standing[Nearest(Centroid(graph, runt), centres, out _)].AddRange(runt);
                    runs = standing;
                }

                runs = runs.OrderBy(r => r.Average(i => graph.Position[i].X)).ToList();
                foreach (var run in runs) foreach (int i in run) claimed[i] = true;
            }

            for (int k = 0; k < keys.Count; k++)
            {
                if (k < runs.Count)
                {
                    var owned = runs[k].Select(i => counties[i]).ToList();
                    string name = DuchyName(owned);
                    if (!names.Add(name))
                    {
                        name = KingdomName(owned, DuchyName(owned));
                        names.Add(name);
                    }

                    result.Add(new SteppeExpansion
                    {
                        Key = keys[k], SubRegionKey = wiredKey, Name = name, Counties = owned,
                    });
                }
                else
                {
                    result.Add(new SteppeExpansion
                    {
                        Key = keys[k], SubRegionKey = wiredKey, Name = null, Counties = parked,
                    });
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Breadth-first hop count from a set of nodes, walking only through nodes outside the belt
    /// once past the sources. -1 where unreachable.
    /// </summary>
    private static int[] HopsFrom(RegionGrowth.Graph graph, IEnumerable<int> sources, bool[] inBelt)
    {
        var hops = new int[graph.Count];
        Array.Fill(hops, -1);
        var queue = new Queue<int>();

        foreach (int s in sources)
        {
            if (hops[s] >= 0) continue;
            hops[s] = 0;
            queue.Enqueue(s);
        }

        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            foreach (int next in graph.Neighbours[node])
            {
                if (hops[next] >= 0 || inBelt[next]) continue;
                hops[next] = hops[node] + 1;
                queue.Enqueue(next);
            }
        }

        return hops;
    }

    private static bool[] Flags(List<int> nodes, int count)
    {
        var flags = new bool[count];
        foreach (int i in nodes) flags[i] = true;
        return flags;
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

    private static int Nearest((double X, double Y) at, List<(double X, double Y)> centroids,
        out double distance)
    {
        int nearest = 0;
        distance = double.PositiveInfinity;
        for (int g = 0; g < centroids.Count; g++)
        {
            double d = Distance(at, centroids[g]);
            if (d < distance) { distance = d; nearest = g; }
        }
        return nearest;
    }

    /// <summary>
    /// How far from a sub-region's centre a scrap may lie and still belong to it: half again its
    /// own extent, with a floor of a few county widths so a compact sub-region can still pick
    /// up the fragment across the strait from it.
    /// </summary>
    private static double Reach(RegionGrowth.Graph graph, List<int> nodes, (double X, double Y) centre)
    {
        double extent = 0, edges = 0;
        int edgeCount = 0;
        var inside = Flags(nodes, graph.Count);

        foreach (int i in nodes)
        {
            extent = Math.Max(extent, Distance(graph.Position[i], centre));
            foreach (int n in graph.Neighbours[i])
            {
                if (n <= i || !inside[n]) continue;
                edges += Distance(graph.Position[i], graph.Position[n]);
                edgeCount++;
            }
        }

        double countyWidth = edgeCount == 0 ? 0 : edges / edgeCount;
        return Math.Max(1.5 * extent, 3 * countyWidth);
    }

    /// <summary>
    /// Closes the belt over the counties it surrounds.
    ///
    /// Two rules, iterated to a fixed point: a county with every neighbour in the belt joins it,
    /// whatever its ground, because a hole in a situation region is a county whose seasons stop
    /// at its border; and a dry county with half its neighbours in the belt joins it too, because
    /// drylands and desert at the edge of the grass are the same pastoral world in vanilla's own
    /// lists. Fertile ground at the edge stays out — a farmland county with steppe on two sides
    /// is the settled frontier, and it is that frontier the situation is about.
    /// </summary>
    private static void FillHoles(RegionGrowth.Graph graph, List<Title> counties,
        TerrainClass[] provinceTerrain, bool[] isMember)
    {
        const int MaxPasses = 8;

        for (int pass = 0; pass < MaxPasses; pass++)
        {
            var joining = new List<int>();

            for (int i = 0; i < counties.Count; i++)
            {
                if (isMember[i]) continue;

                var around = graph.Neighbours[i];
                if (around.Count == 0) continue;

                int inside = around.Count(n => isMember[n]);
                if (inside == around.Count)
                {
                    joining.Add(i);
                    continue;
                }

                var terrain = Development.DominantTerrain(counties[i], provinceTerrain);
                bool arid = terrain is TerrainClass.Drylands or TerrainClass.Desert
                                    or TerrainClass.DesertMountains;
                if (arid && inside * 2 >= around.Count && around.Count >= 2) joining.Add(i);
            }

            if (joining.Count == 0) return;
            foreach (int i in joining) isMember[i] = true;
        }
    }

    /// <summary>Connected groups of flagged nodes, in node order.</summary>
    private static List<List<int>> Components(RegionGrowth.Graph graph, bool[] flagged)
    {
        var seen = new bool[graph.Count];
        var components = new List<List<int>>();

        for (int start = 0; start < graph.Count; start++)
        {
            if (!flagged[start] || seen[start]) continue;

            var component = new List<int>();
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;

            while (stack.Count > 0)
            {
                int node = stack.Pop();
                component.Add(node);
                foreach (int next in graph.Neighbours[node])
                {
                    if (!flagged[next] || seen[next]) continue;
                    seen[next] = true;
                    stack.Push(next);
                }
            }

            components.Add(component);
        }

        return components;
    }

    /// <summary>
    /// A sub-region is named for the de jure kingdom that holds most of it, so the situation
    /// window reads as this world's rather than as "Western Steppe" over a place that is not in
    /// the west of anything. A belt no kingdom claims falls back to the duchy.
    /// </summary>
    private static string KingdomName(List<Title> counties, string suffix)
    {
        var kingdom = Dominant(counties.Select(c => c.Parent?.Parent), "k");
        var anchor = kingdom ?? Dominant(counties.Select(c => c.Parent), "d");
        return anchor is null ? $"The Great {suffix}" : $"{anchor.Name} {suffix}";
    }

    /// <summary>An expansion region is a duchy or two, so it is named for the duchy that holds most of it.</summary>
    private static string DuchyName(List<Title> counties)
        => Dominant(counties.Select(c => c.Parent), "d")?.Name ?? "The Marches";

    private static Title? Dominant(IEnumerable<Title?> titles, string tier)
        => titles.Where(t => t is not null && t.Tier == tier && t.Name.Length > 0)
            .GroupBy(t => t!)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();
}
