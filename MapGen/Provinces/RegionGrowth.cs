using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Carves a set of counties into contiguous regions that respect the ground between them.
///
/// Both cultures and faiths are partitions of the same county graph, differing only in how many
/// regions they want and in how much the terrain slows them down, so the mechanism lives here once.
/// It is a cost-weighted multi-source Dijkstra — the same shape as the province partitioner in
/// <see cref="Provinces"/>, and for the same reason: a plain voronoi puts boundaries wherever seeds
/// happen to be equidistant, which cuts straight over mountain ranges and produces regions that
/// look drawn rather than settled. Weighting the frontier by what it costs to cross makes it stall
/// at ridgelines and deserts and run freely down river valleys and coasts, so borders land where a
/// reader expects to find them.
///
/// Seeds are chosen greedy-farthest-first rather than at random. Random seeds clump — two cultures
/// sharing a valley while a subcontinent goes unclaimed — and clumping is exactly what a player
/// reads as "generated".
/// </summary>
public static class RegionGrowth
{
    /// <summary>A node's neighbours, what it costs to enter, and where it is.</summary>
    public sealed class Graph
    {
        public required List<int>[] Neighbours { get; init; }

        /// <summary>Cost of expanding *into* this node. Cheap ground spreads, hard ground resists.</summary>
        public required double[] EnterCost { get; init; }

        public required (double X, double Y)[] Position { get; init; }

        public int Count => Neighbours.Length;
    }

    /// <summary>
    /// Assigns every member to one of <paramref name="regionCount"/> regions.
    ///
    /// Returns region index per node, or -1 for nodes outside <paramref name="members"/>. Members
    /// are given explicitly so the same graph can be re-partitioned inside one region — which is how
    /// cultures are grown within a heritage, and faiths within a religion.
    /// </summary>
    public static int[] Partition(Graph graph, IReadOnlyList<int> members, int regionCount,
        Rng rng, out List<int> seeds)
    {
        var owner = new int[graph.Count];
        var dist = new double[graph.Count];
        Array.Fill(owner, -1);
        Array.Fill(dist, double.PositiveInfinity);

        var isMember = new bool[graph.Count];
        foreach (int m in members) isMember[m] = true;

        seeds = [];
        regionCount = Math.Clamp(regionCount, 1, members.Count);
        if (members.Count == 0) return owner;

        // Seeds are shared out between landmasses in proportion to their size *before* any are
        // placed, and farthest-first then runs inside each one separately.
        //
        // Doing it globally does not work, and the failure is worth recording: an unreachable
        // county is infinitely far from every seed, so farthest-first always prefers one, and on a
        // map with islands every seed after the first lands on a different island while the
        // mainland stays a single undivided region. Measured on a 98-county map asking for five
        // cultures: one culture of 70 counties and four of 1-4. Per-component quotas give the
        // mainland the four seeds its size earns.
        var components = Components(graph, members, isMember);
        var quota = Allocate(components, regionCount);

        for (int c = 0; c < components.Count; c++)
        {
            var component = components[c];

            for (int i = 0; i < quota[c]; i++)
            {
                // Within one landmass everything is reachable, so after the first seed the
                // farthest county is a genuinely distant one rather than a disconnected one.
                int seed = i == 0
                    ? component[rng.Int(0, component.Count - 1)]
                    : Farthest(component, dist);

                Relax(graph, isMember, owner, dist, seed, seeds.Count);
                seeds.Add(seed);
            }
        }

        AssignUnreached(graph, members, owner, seeds);
        return owner;
    }

    /// <summary>Connected groups of members — one per landmass, in practice.</summary>
    private static List<List<int>> Components(Graph graph, IReadOnlyList<int> members, bool[] isMember)
    {
        var seen = new bool[graph.Count];
        var components = new List<List<int>>();

        foreach (int start in members)
        {
            if (seen[start]) continue;

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
                    if (!isMember[next] || seen[next]) continue;
                    seen[next] = true;
                    stack.Push(next);
                }
            }

            components.Add(component);
        }

        return components;
    }

    /// <summary>
    /// Shares <paramref name="regionCount"/> seeds between components by size, largest remainder
    /// first. Components too small to earn one get none and are swept up by
    /// <see cref="AssignUnreached"/> — an islet joining the nearest mainland region rather than
    /// being a culture of its own.
    /// </summary>
    private static int[] Allocate(List<List<int>> components, int regionCount)
    {
        int total = components.Sum(c => c.Count);
        var quota = new int[components.Count];
        var remainder = new List<(int Index, double Fraction)>();

        int assigned = 0;
        for (int i = 0; i < components.Count; i++)
        {
            double exact = regionCount * (double)components[i].Count / total;
            quota[i] = Math.Min((int)exact, components[i].Count);
            assigned += quota[i];
            remainder.Add((i, exact - (int)exact));
        }

        foreach (var (index, _) in remainder.OrderByDescending(r => r.Fraction)
                                            .ThenBy(r => r.Index))
        {
            if (assigned >= regionCount) break;
            if (quota[index] >= components[index].Count) continue;

            quota[index]++;
            assigned++;
        }

        // Rounding can leave every component on zero when one dwarfs the rest; the map still needs
        // at least one region.
        if (assigned == 0) quota[components.IndexOf(components.MaxBy(c => c.Count)!)] = 1;

        return quota;
    }

    /// <summary>
    /// Dijkstra from one seed, keeping the assignment only where this seed is the closest so far.
    /// Running it once per seed rather than from all seeds at once is what lets the caller pick
    /// each seed with knowledge of the ones before it.
    /// </summary>
    private static void Relax(Graph graph, bool[] isMember, int[] owner, double[] dist,
        int seed, int region)
    {
        var queue = new PriorityQueue<int, double>();
        dist[seed] = 0;
        owner[seed] = region;
        queue.Enqueue(seed, 0);

        while (queue.TryDequeue(out int node, out double d))
        {
            if (d > dist[node]) continue;

            foreach (int next in graph.Neighbours[node])
            {
                if (!isMember[next]) continue;

                double candidate = d + graph.EnterCost[next];
                if (candidate >= dist[next]) continue;

                dist[next] = candidate;
                owner[next] = region;
                queue.Enqueue(next, candidate);
            }
        }
    }

    private static int Farthest(IReadOnlyList<int> members, double[] dist)
    {
        int best = members[0];
        double bestDist = -1;

        foreach (int m in members)
        {
            // Strictly greater keeps the choice deterministic when a whole archipelago is tied at
            // infinity: the first such county in member order always wins.
            if (dist[m] > bestDist) { bestDist = dist[m]; best = m; }
        }

        return best;
    }

    /// <summary>
    /// Sweeps up counties no seed could reach — islands, and anything cut off behind impassable
    /// province chains — by giving each to the region whose seed is physically nearest.
    ///
    /// They cannot be left unassigned: a county with no culture is a county CK3 falls back on and
    /// complains about. Handing an island to the nearest mainland region is also the historically
    /// sensible answer, since that is generally who settled it.
    /// </summary>
    private static void AssignUnreached(Graph graph, IReadOnlyList<int> members, int[] owner,
        List<int> seeds)
    {
        foreach (int m in members)
        {
            if (owner[m] >= 0) continue;

            var (x, y) = graph.Position[m];
            int best = 0;
            double bestDist = double.PositiveInfinity;

            for (int s = 0; s < seeds.Count; s++)
            {
                var (sx, sy) = graph.Position[seeds[s]];
                double dx = sx - x, dy = sy - y;
                double d = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; best = s; }
            }

            owner[m] = best;
        }
    }
}
