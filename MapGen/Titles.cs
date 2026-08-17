using System.Text.RegularExpressions;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using System.Globalization;

namespace Ck3MapGen.MapGen;

public sealed class Title
{
    public required string Tier;      // b, c, d, k, e
    public required int Index;        // ordinal within its tier
    public string Key = "";
    public string Name = "";

    public int ProvinceId = -1;

    public List<Title> Children = [];
    public Title? Parent;

    public (byte R, byte G, byte B) Color;
}

public static class Titles
{
    public const int MinBaroniesPerCounty = 3;
    public const int MaxBaroniesPerCounty = 7;

    public const int MinCountiesPerDuchy = 4;
    public const int MaxCountiesPerDuchy = 6;

    public const int MinDuchiesPerKingdom = 5;
    public const int MaxDuchiesPerKingdom = 7;

    public const int MinKingdomsPerEmpire = 3;
    public const int MaxKingdomsPerEmpire = 5;

    public static Dictionary<int, HashSet<int>> BuildAdjacency(ProvinceMap map, int landCount, int[] order)
    {
        var adjacency = new Dictionary<int, HashSet<int>>();

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int a = order[map.Label[y * map.Width + x]];
                if (a > landCount) continue;

                if (x + 1 < map.Width) Link(a, order[map.Label[y * map.Width + x + 1]]);
                if (y + 1 < map.Height) Link(a, order[map.Label[(y + 1) * map.Width + x]]);
            }
        }

        return adjacency;

        void Link(int a, int b)
        {
            if (a == b || b > landCount) return;
            if (!adjacency.TryGetValue(a, out var sa)) adjacency[a] = sa = [];
            if (!adjacency.TryGetValue(b, out var sb)) adjacency[b] = sb = [];
            sa.Add(b);
            sb.Add(a);
        }
    }

    /// <summary>
    /// Province pairs that face each other across a short stretch of water.
    ///
    /// Without this every title is landlocked by construction: <see cref="BuildAdjacency"/> links
    /// provinces only where their pixels touch, so no kingdom can hold both sides of a strait and
    /// no empire can be a thalassocracy. That is wrong at the top of the hierarchy specifically —
    /// Britannia, Sicily, Denmark and Byzantium are all one realm across water — while remaining
    /// right at the bottom, which is why the two adjacencies are kept apart and only merged for the
    /// kingdom and empire tiers.
    ///
    /// Found by flooding outward from every coast at once and noting where two different provinces'
    /// fronts meet: the meeting cost is how far apart the two coastlines are. Flooding stops at
    /// <paramref name="maxDistance"/>, so the open ocean is never visited and the cost is
    /// proportional to coastline rather than to sea area.
    ///
    /// Impassable provinces are not sources and not traversable — they are land, so a mountain wall
    /// blocks a sea link exactly as it blocks a land one.
    /// </summary>
    /// <summary>8-neighbour offsets. Diagonals count as one step, which slightly understates a
    /// diagonal crossing — immaterial against a threshold measured in tens of pixels.</summary>
    private static readonly (int Dx, int Dy)[] Neighbourhood =
        [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    public static Dictionary<int, HashSet<int>> BuildSeaAdjacency(ProvinceMap map, int baronyCount,
        int[] order, int maxDistance)
    {
        var links = new Dictionary<int, HashSet<int>>();
        if (maxDistance <= 0) return links;

        int width = map.Width, height = map.Height;
        var owner = new int[width * height];
        var dist = new int[width * height];
        var frontier = new Queue<int>();

        bool IsOpenWater(int cell) => !map.Seeds[map.Label[cell]].IsLand;
        int BaroniedLand(int cell)
        {
            int id = order[map.Label[cell]];
            return map.Seeds[map.Label[cell]].IsLand && id <= baronyCount ? id : 0;
        }

        // Every water pixel touching a coast starts the flood, carrying the province behind it.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;
                if (!IsOpenWater(cell)) continue;

                foreach (var (dx, dy) in Neighbourhood)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                    int id = BaroniedLand(ny * width + nx);
                    if (id == 0) continue;

                    owner[cell] = id;
                    dist[cell] = 1;
                    frontier.Enqueue(cell);
                    break;
                }
            }
        }

        while (frontier.Count > 0)
        {
            int cell = frontier.Dequeue();
            if (dist[cell] >= maxDistance) continue;

            int x = cell % width, y = cell / width;
            foreach (var (dx, dy) in Neighbourhood)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int next = ny * width + nx;
                if (owner[next] != 0 || !IsOpenWater(next)) continue;

                owner[next] = owner[cell];
                dist[next] = dist[cell] + 1;
                frontier.Enqueue(next);
            }
        }

        // Where two fronts meet, the water between their coasts is as wide as the two distances
        // added together.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;
                int a = owner[cell];
                if (a == 0) continue;

                if (x + 1 < width) Meet(cell, cell + 1);
                if (y + 1 < height) Meet(cell, cell + width);
            }
        }

        return links;

        void Meet(int cell, int other)
        {
            int a = owner[cell], b = owner[other];
            if (b == 0 || a == b) return;
            if (dist[cell] + dist[other] > maxDistance) return;

            if (!links.TryGetValue(a, out var sa)) links[a] = sa = [];
            if (!links.TryGetValue(b, out var sb)) links[b] = sb = [];
            sa.Add(b);
            sb.Add(a);
        }
    }

    /// <summary>
    /// How many clusters are in more than one piece when only land links count — that is, how many
    /// realms actually used a sea link. Reported rather than asserted, because the right number
    /// depends on the map: a single-continent world should show zero and an archipelago most of them.
    /// </summary>
    private static int Overseas(List<List<int>> clusters, Dictionary<int, HashSet<int>> landAdjacency)
    {
        int split = 0;

        foreach (var cluster in clusters)
        {
            if (cluster.Count <= 1) continue;

            var members = cluster.ToHashSet();
            var seen = new HashSet<int> { cluster[0] };
            var stack = new Stack<int>([cluster[0]]);

            while (stack.Count > 0)
            {
                if (!landAdjacency.TryGetValue(stack.Pop(), out var neighbours)) continue;
                foreach (int next in neighbours)
                    if (members.Contains(next) && seen.Add(next)) stack.Push(next);
            }

            if (seen.Count < cluster.Count) split++;
        }

        return split;
    }

    /// <summary>Land and sea links together, for the tiers allowed to cross water.</summary>
    private static Dictionary<int, HashSet<int>> Union(
        Dictionary<int, HashSet<int>> land, Dictionary<int, HashSet<int>> sea)
    {
        var merged = new Dictionary<int, HashSet<int>>(land.Count);
        foreach (var (key, values) in land) merged[key] = [.. values];

        foreach (var (key, values) in sea)
        {
            if (!merged.TryGetValue(key, out var set)) merged[key] = set = [];
            set.UnionWith(values);
        }

        return merged;
    }

    /// <summary>How many settling passes to run. Movement dies out well before this on every map
    /// measured; the cap is only there so a pair of members cannot trade places forever.</summary>
    private const int SettlePasses = 12;

    /// <summary>
    /// Groups members into clusters of <paramref name="minSize"/> to <paramref name="maxSize"/>,
    /// keeping each one as close to round as the adjacency allows.
    ///
    /// Compactness is the point, and it is not cosmetic. CK3 draws land at *county* level, so what
    /// the player sees is the union of a county's baronies — provinces.png is never rendered on its
    /// own, and a barony has no outline in game. That makes this function, not the province
    /// partitioner, the thing that decides what the map looks like.
    ///
    /// It used to flood the adjacency graph in shuffled order, which is unconstrained by geometry:
    /// a chain of eight baronies was exactly as likely as a blob of eight. Measured on the
    /// 1024x512 test map, 11-18% of counties had a waist under 16 px against 0-5% of the baronies
    /// making them up, and counties were *more* ragged than their own parts despite being three
    /// times the area. Pinched counties over clean provinces is the signature.
    ///
    /// So growth takes the candidate nearest the cluster's running centre instead of the next one
    /// off a shuffled queue, and <see cref="Settle"/> then trades boundary members between
    /// neighbours while that keeps lowering the same distance. It is the graph-space counterpart of
    /// the Lloyd relaxation <see cref="Provinces"/> already runs in pixel space.
    ///
    /// The seeds are still shuffled. Where a cluster starts should be arbitrary; only how it grows
    /// from there should not be.
    /// </summary>
    private static List<List<int>> Cluster(
        IReadOnlyList<int> members,
        Dictionary<int, HashSet<int>> adjacency,
        int minSize,
        int maxSize,
        Rng rng,
        (double X, double Y)[] positions)
    {
        var assigned = new HashSet<int>();
        var clusters = new List<List<int>>();

        var seeds = members.ToList();
        Shuffle(seeds, rng);

        var candidates = new List<int>();

        foreach (int start in seeds)
        {
            if (!assigned.Add(start)) continue;

            int targetSize = rng.Int(minSize, maxSize + 1);

            var cluster = new List<int> { start };
            var (cx, cy) = At(positions, start);

            candidates.Clear();
            Offer(start);

            while (cluster.Count < targetSize && candidates.Count > 0)
            {
                // Nearest to the centre *so far*, so the cluster fills in around itself. Taking the
                // nearest to the seed instead would let it reach past a gap and come back.
                int bestAt = -1;
                double best = double.PositiveInfinity;

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (assigned.Contains(candidates[i])) continue;

                    double cost = DistanceSquared((cx, cy), At(positions, candidates[i]));
                    if (cost >= best) continue;

                    best = cost;
                    bestAt = i;
                }

                // Everything on the frontier was taken by a cluster seeded earlier.
                if (bestAt < 0) break;

                int chosen = candidates[bestAt];
                candidates.RemoveAt(bestAt);
                assigned.Add(chosen);
                cluster.Add(chosen);

                var (px, py) = At(positions, chosen);
                cx += (px - cx) / cluster.Count;
                cy += (py - cy) / cluster.Count;

                Offer(chosen);
            }

            clusters.Add(cluster);

            void Offer(int member)
            {
                if (!adjacency.TryGetValue(member, out var links)) return;
                foreach (int n in links)
                    if (!assigned.Contains(n)) candidates.Add(n);
            }
        }

        Settle(clusters, adjacency, positions, minSize, maxSize);
        return clusters;
    }

    /// <summary>
    /// Hands boundary members to a neighbouring cluster whose centre is nearer, which is what turns
    /// the tendrils growth could not avoid into something round.
    ///
    /// Two guards keep it from trading one defect for a worse one. A cluster at
    /// <paramref name="minSize"/> cannot give anything away and one at <paramref name="maxSize"/>
    /// cannot take anything, so settling never undoes the size distribution growth just produced.
    /// And a member is only moved if what it leaves behind is still one connected piece — a county
    /// in two halves is not an improvement on a pinched one, and CK3 will happily draw it.
    /// </summary>
    private static void Settle(List<List<int>> clusters, Dictionary<int, HashSet<int>> adjacency,
        (double X, double Y)[] positions, int minSize, int maxSize)
    {
        var owner = new Dictionary<int, int>();
        for (int i = 0; i < clusters.Count; i++)
            foreach (int member in clusters[i]) owner[member] = i;

        var centre = new (double X, double Y)[clusters.Count];
        for (int i = 0; i < clusters.Count; i++) centre[i] = Centre(clusters[i], positions);

        var order = owner.Keys.ToList();

        for (int pass = 0; pass < SettlePasses; pass++)
        {
            int moved = 0;

            foreach (int member in order)
            {
                int from = owner[member];
                if (clusters[from].Count <= minSize) continue;
                if (!adjacency.TryGetValue(member, out var links)) continue;

                var here = At(positions, member);
                double bestCost = DistanceSquared(centre[from], here);
                int best = -1;

                // Only clusters this member actually touches, so the receiver stays connected for
                // free — it is gaining a member adjacent to one it already holds.
                foreach (int link in links)
                {
                    if (!owner.TryGetValue(link, out int to) || to == from) continue;
                    if (clusters[to].Count >= maxSize) continue;

                    double cost = DistanceSquared(centre[to], here);
                    if (cost >= bestCost) continue;

                    bestCost = cost;
                    best = to;
                }

                if (best < 0) continue;
                if (!StaysConnected(clusters[from], member, adjacency)) continue;

                clusters[from].Remove(member);
                clusters[best].Add(member);
                owner[member] = best;
                centre[from] = Centre(clusters[from], positions);
                centre[best] = Centre(clusters[best], positions);
                moved++;
            }

            if (moved == 0) break;
        }
    }

    /// <summary>Whether what is left of a cluster is still one piece once a member is taken out.</summary>
    private static bool StaysConnected(List<int> cluster, int dropped,
        Dictionary<int, HashSet<int>> adjacency)
    {
        var remaining = new HashSet<int>(cluster);
        remaining.Remove(dropped);
        if (remaining.Count <= 1) return true;

        var seen = new HashSet<int>();
        var queue = new Queue<int>();

        int start = remaining.First();
        seen.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            if (!adjacency.TryGetValue(queue.Dequeue(), out var links)) continue;
            foreach (int n in links)
                if (remaining.Contains(n) && seen.Add(n)) queue.Enqueue(n);
        }

        return seen.Count == remaining.Count;
    }

    /// <summary>Squared, because only the ordering is ever used and a square root would not change it.</summary>
    private static double DistanceSquared((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>Guarded like <see cref="Centre"/>: county members are province ids and every tier
    /// above indexes from zero, so the two share this array shape but not its origin.</summary>
    private static (double X, double Y) At((double X, double Y)[] positions, int member)
        => member >= 0 && member < positions.Length ? positions[member] : (0, 0);

    /// <summary>
    /// Folds undersized clusters into a neighbour, so a scrap of land does not get a title of its
    /// own at every tier above it.
    ///
    /// <see cref="Cluster"/> grows toward a target size but stops when it runs out of neighbours,
    /// and then gives whatever is left its own cluster. On an island that means a lone county
    /// becomes a duchy, the only duchy in a kingdom, and the only kingdom in an empire — three
    /// titles conjured out of one province, none of which a player would ever draw that way.
    /// Absorption is the counterpart to growth: growth decides who joins whom while there is room,
    /// this decides where the leftovers go.
    ///
    /// The adjacency passed in should include sea links at every tier. Being *built* across water
    /// is a privilege of the top tiers — see <see cref="BuildSeaAdjacency"/> — but being *absorbed*
    /// across water is how a small island ends up inside a mainland duchy, which is exactly where
    /// small islands are.
    ///
    /// Merging into the smallest available neighbour rather than the nearest keeps the tier even;
    /// merging into the nearest lets one cluster snowball by being adjacent to a lot of coastline.
    /// </summary>
    /// <param name="positions">
    /// Member positions, enabling a last-resort merge into the nearest cluster when an island lies
    /// beyond every sea link. Supplied for kingdoms and empires, where CK3 expects de jure cover
    /// over the whole map and a remote island belonging to a distant crown is normal. Left null for
    /// duchies, where the same fallback would draw a duchy across an ocean — a lone island duchy is
    /// a real thing (Iceland is one) and is where the stack should stop.
    /// </param>
    private static List<List<int>> AbsorbUndersized(List<List<int>> clusters,
        Dictionary<int, HashSet<int>> adjacency, int minSize, int maxSize,
        (double X, double Y)[]? positions = null)
    {
        var owner = new Dictionary<int, int>();
        for (int i = 0; i < clusters.Count; i++)
            foreach (int member in clusters[i]) owner[member] = i;

        // Clusters with nothing to join — an island beyond every sea link. They keep their own
        // title because there is genuinely nowhere else to put them, and they are set aside so one
        // of them cannot stall the pass for everyone else.
        var stranded = new HashSet<int>();

        while (true)
        {
            // Smallest undersized cluster first, so the most obviously wrong ones are resolved
            // while there is still the widest choice of host.
            int source = -1;
            for (int i = 0; i < clusters.Count; i++)
            {
                if (clusters[i].Count == 0 || clusters[i].Count >= minSize || stranded.Contains(i))
                    continue;
                if (source < 0 || clusters[i].Count < clusters[source].Count) source = i;
            }

            if (source < 0) break;

            var neighbours = new HashSet<int>();
            foreach (int member in clusters[source])
            {
                if (!adjacency.TryGetValue(member, out var links)) continue;
                foreach (int link in links)
                    if (owner.TryGetValue(link, out int other) && other != source) neighbours.Add(other);
            }

            if (neighbours.Count == 0)
            {
                int nearest = positions is null ? -1 : Nearest(clusters, positions, source);
                if (nearest < 0) { stranded.Add(source); continue; }
                neighbours.Add(nearest);
            }

            // Prefer a host that still has room. If every neighbour is already at its maximum the
            // merge happens anyway: an oversized duchy is a lesser evil than a one-province one.
            int target = -1;
            foreach (int candidate in neighbours.OrderBy(n => n))
            {
                if (clusters[candidate].Count == 0) continue;
                bool roomy = clusters[candidate].Count + clusters[source].Count <= maxSize;
                bool targetRoomy = target >= 0
                    && clusters[target].Count + clusters[source].Count <= maxSize;

                if (target < 0
                    || (roomy && !targetRoomy)
                    || (roomy == targetRoomy && clusters[candidate].Count < clusters[target].Count))
                    target = candidate;
            }

            if (target < 0) { stranded.Add(source); continue; }

            foreach (int member in clusters[source])
            {
                clusters[target].Add(member);
                owner[member] = target;
            }

            clusters[source].Clear();
        }

        return [.. clusters.Where(c => c.Count > 0)];
    }

    /// <summary>
    /// The cluster whose members' mean position is closest to this one's. Only reached when a
    /// cluster touches nothing at all, so the answer is "which crown is this island nearest to".
    /// </summary>
    private static int Nearest(List<List<int>> clusters, (double X, double Y)[] positions, int source)
    {
        var (x, y) = Centre(clusters[source], positions);

        int best = -1;
        double bestDistance = double.PositiveInfinity;

        for (int i = 0; i < clusters.Count; i++)
        {
            if (i == source || clusters[i].Count == 0) continue;

            var (ox, oy) = Centre(clusters[i], positions);
            double dx = ox - x, dy = oy - y;
            double distance = dx * dx + dy * dy;

            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }

        return best;
    }

    private static (double X, double Y) Centre(List<int> cluster, (double X, double Y)[] positions)
    {
        double x = 0, y = 0;
        int counted = 0;

        foreach (int member in cluster)
        {
            if (member < 0 || member >= positions.Length) continue;
            x += positions[member].X;
            y += positions[member].Y;
            counted++;
        }

        return counted == 0 ? (0, 0) : (x / counted, y / counted);
    }

    /// <summary>Mean position of each cluster, in the index space of the tier above it.</summary>
    private static (double X, double Y)[] Roll(List<List<int>> clusters,
        (double X, double Y)[] positions)
        => [.. clusters.Select(c => Centre(c, positions))];

    private static void Shuffle<T>(List<T> list, Rng rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Math.Clamp(rng.Int(0, i), 0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static Dictionary<int, HashSet<int>> LiftAdjacency(
        List<List<int>> clusters, Dictionary<int, HashSet<int>> below)
    {
        var owner = new Dictionary<int, int>();
        for (int i = 0; i < clusters.Count; i++)
            foreach (int m in clusters[i]) owner[m] = i;

        var lifted = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < clusters.Count; i++) lifted[i] = [];

        foreach (var (member, neighbors) in below)
        {
            if (!owner.TryGetValue(member, out int a)) continue;
            foreach (int n in neighbors)
            {
                if (!owner.TryGetValue(n, out int b) || a == b) continue;
                lifted[a].Add(b);
                lifted[b].Add(a);
            }
        }

        return lifted;
    }

    public static List<Title> Build(ProvinceMap map, int landCount, int[] order, MapConfig cfg, Rng rng)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adjacency = BuildAdjacency(map, landCount, order);

        // Kept separate from the land adjacency all the way up, and merged in only where a realm is
        // allowed to cross water — see BuildSeaAdjacency.
        int bridge = (int)Math.Round(cfg.Scaled(cfg.SeaBridgePixelsAtVanilla));
        var seaAdjacency = BuildSeaAdjacency(map, landCount, order, bridge);

        var provinceIds = Enumerable.Range(1, landCount).ToList();
        var baronies = new List<Title>(landCount);
        for (int i = 0; i < landCount; i++)
            baronies.Add(new Title { Tier = "b", Index = i, ProvinceId = provinceIds[i] });

        var byProvince = baronies.ToDictionary(b => b.ProvinceId);

        // Where each province sits, rolled up a tier at a time. Two jobs: it is what keeps a
        // cluster compact as it grows, and it is how a stranded island is given to the nearest
        // crown when no adjacency reaches it.
        var provincePosition = new (double X, double Y)[landCount + 1];
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= landCount)
                provincePosition[id] = (map.Seeds[label].X, map.Seeds[label].Y);
        }

        var countyClusters = Cluster(provinceIds, adjacency, MinBaroniesPerCounty,
            MaxBaroniesPerCounty, rng, provincePosition);
        var counties = Wrap("c", countyClusters, c => c.Select(p => byProvince[p]));

        var countyPosition = Roll(countyClusters, provincePosition);

        // Every tier above the county absorbs its leftovers before the next tier is lifted off it,
        // so an island scrap joins a real duchy instead of founding a duchy, a kingdom and an
        // empire on the way up. Counties are left alone: a one-province county is ordinary.
        var duchyAdjacency = LiftAdjacency(countyClusters, adjacency);
        var duchySea = LiftAdjacency(countyClusters, seaAdjacency);
        var duchyClusters = AbsorbUndersized(
            Cluster(Enumerable.Range(0, counties.Count).ToList(), duchyAdjacency,
                MinCountiesPerDuchy, MaxCountiesPerDuchy, rng, countyPosition),
            Union(duchyAdjacency, duchySea), cfg.MinChildrenPerTitle, MaxCountiesPerDuchy);
        var duchies = Wrap("d", duchyClusters, c => c.Select(i => counties[i]));
        var duchyPosition = Roll(duchyClusters, countyPosition);

        // From here up, water is a road rather than a wall for growth too, not only for absorption.
        var kingdomAdjacency = LiftAdjacency(duchyClusters, duchyAdjacency);
        var kingdomSea = LiftAdjacency(duchyClusters, duchySea);
        var kingdomClusters = AbsorbUndersized(
            Cluster(Enumerable.Range(0, duchies.Count).ToList(), Union(kingdomAdjacency, kingdomSea),
                MinDuchiesPerKingdom, MaxDuchiesPerKingdom, rng, duchyPosition),
            Union(kingdomAdjacency, kingdomSea), cfg.MinChildrenPerTitle, MaxDuchiesPerKingdom,
            duchyPosition);
        var kingdoms = Wrap("k", kingdomClusters, c => c.Select(i => duchies[i]));
        var kingdomPosition = Roll(kingdomClusters, duchyPosition);

        var empireAdjacency = LiftAdjacency(kingdomClusters, kingdomAdjacency);
        var empireSea = LiftAdjacency(kingdomClusters, kingdomSea);
        var empireClusters = AbsorbUndersized(
            Cluster(Enumerable.Range(0, kingdoms.Count).ToList(), Union(empireAdjacency, empireSea),
                MinKingdomsPerEmpire, MaxKingdomsPerEmpire, rng, kingdomPosition),
            Union(empireAdjacency, empireSea), cfg.MinChildrenPerTitle, MaxKingdomsPerEmpire,
            kingdomPosition);
        var empires = Wrap("e", empireClusters, c => c.Select(i => kingdoms[i]));

        AssignColors(empires, rng);

        int seaLinked = seaAdjacency.Values.Sum(s => s.Count) / 2;
        Console.WriteLine($"  titles: {empires.Count} empires, {kingdoms.Count} kingdoms, " +
                          $"{duchies.Count} duchies, {counties.Count} counties, {baronies.Count} baronies " +
                          $"({sw.ElapsedMilliseconds} ms)");
        Console.WriteLine($"  sea links: {seaLinked} province pairs within {bridge} px of water — " +
                          $"{Overseas(kingdomClusters, duchyAdjacency)} of {kingdoms.Count} kingdoms and " +
                          $"{Overseas(empireClusters, kingdomAdjacency)} of {empires.Count} empires " +
                          $"span more than one landmass");

        // Whatever is left here is genuinely unreachable rather than merely small, so it is worth
        // seeing: a stubborn count means the sea-crossing limit is too tight for this map's islands.
        Console.WriteLine($"  singleton titles left stranded: " +
                          $"{duchyClusters.Count(c => c.Count == 1)} duchies, " +
                          $"{kingdomClusters.Count(c => c.Count == 1)} kingdoms, " +
                          $"{empireClusters.Count(c => c.Count == 1)} empires");
        return empires;

        static List<Title> Wrap(string tier, List<List<int>> clusters, Func<List<int>, IEnumerable<Title>> resolve)
        {
            var result = new List<Title>(clusters.Count);
            for (int i = 0; i < clusters.Count; i++)
            {
                var title = new Title { Tier = tier, Index = i };
                foreach (var child in resolve(clusters[i]))
                {
                    child.Parent = title;
                    title.Children.Add(child);
                }
                result.Add(title);
            }
            return result;
        }
    }

    /// <summary>
    /// Re-derives every descendant's colour from this title's current one, exactly as generation
    /// would have.
    ///
    /// The generated palette is hierarchical — a duchy's counties are shades of the duchy, spread
    /// within a tolerance that narrows with each tier — so a hand-picked colour leaves its children
    /// as shades of a colour that is no longer there. This puts them back in step.
    ///
    /// Deliberately not automatic on every edit: it discards any hand-picked colour below the
    /// title it is called on, which is not something a recolour should do without being asked.
    /// </summary>
    public static void RecolorChildren(Title parent, Rng rng)
        => DistributeChildren(parent, Hsl.FromRgb(parent.Color), rng);

    private static void AssignColors(List<Title> empires, Rng rng)
    {
        if (empires.Count == 0) return;

        // Spread empire base hues evenly across the color wheel using golden ratio offsets
        float baseHue = rng.Float(0f, 360f);
        const float GoldenAngle = 137.507764f;

        for (int i = 0; i < empires.Count; i++)
        {
            float hue = (baseHue + i * GoldenAngle + rng.Float(-15f, 15f)) % 360f;
            float sat = rng.Float(0.55f, 0.85f);
            float lit = rng.Float(0.42f, 0.58f);

            var empireHsl = new Hsl(hue, sat, lit);
            empires[i].Color = empireHsl.ToRgb();

            DistributeChildren(empires[i], empireHsl, rng);
        }
    }

    /// <summary>
    /// Spreads a parent's colour over its children and down the tree.
    ///
    /// A static method rather than a local function inside <see cref="AssignColors"/> because
    /// <see cref="RecolorChildren"/> re-runs it against a hand-picked colour long afterwards.
    /// </summary>
    private static void DistributeChildren(Title parent, Hsl parentHsl, Rng rng)
    {
        int count = parent.Children.Count;
        if (count == 0) return;

        // Define variation tolerances based on hierarchy depth
        var (maxHueShift, maxLitShift, maxSatShift) = parent.Tier switch
        {
            "e" => (28f, 0.14f, 0.12f), // Kingdoms under Empire
            "k" => (16f, 0.11f, 0.10f), // Duchies under Kingdom
            "d" => (8f, 0.07f, 0.06f),  // Counties under Duchy
            _ => (2f, 0.03f, 0.03f),  // Baronies under County
        };

        // Stratify sibling colors to prevent two neighboring siblings getting the same shade
        for (int i = 0; i < count; i++)
        {
            var child = parent.Children[i];

            // Spread sibling hues evenly within [-maxHueShift, +maxHueShift]
            float t = count == 1 ? 0f : (float)i / (count - 1) * 2f - 1f; // [-1.0 .. +1.0]
            float hueDelta = t * maxHueShift + rng.Float(-3f, 3f);

            // Alternate lightness so neighboring titles alternate lighter / darker
            float litDirection = (i % 2 == 0) ? 1f : -1f;
            float litDelta = litDirection * rng.Float(0.04f, maxLitShift);
            float satDelta = (i % 3 == 0 ? 1f : -1f) * rng.Float(0.02f, maxSatShift);

            var childHsl = new Hsl(
                parentHsl.H + hueDelta,
                parentHsl.S + satDelta,
                parentHsl.L + litDelta
            );

            child.Color = childHsl.ToRgb();
            DistributeChildren(child, childHsl, rng);
        }
    }

    /// <summary>
    /// Compact HSL representation for perceptual de jure color harmonization.
    /// </summary>
    private readonly struct Hsl
    {
        public readonly float H; // [0 .. 360)
        public readonly float S; // [0 .. 1]
        public readonly float L; // [0 .. 1]

        public Hsl(float h, float s, float l)
        {
            H = ((h % 360f) + 360f) % 360f;
            S = Math.Clamp(s, 0.25f, 0.90f);
            L = Math.Clamp(l, 0.22f, 0.78f);
        }

        /// <summary>
        /// The inverse of <see cref="ToRgb"/>, for re-deriving children from a hand-picked colour.
        ///
        /// Not an exact round trip: the constructor clamps saturation and lightness into the band
        /// the generated palette lives in, so a colour picked at the extremes comes back pulled
        /// toward that band. That is the wanted behaviour here — the children should sit in the
        /// same range as every other title on the map, whatever the parent was set to.
        /// </summary>
        public static Hsl FromRgb((byte R, byte G, byte B) rgb)
        {
            float r = rgb.R / 255f, g = rgb.G / 255f, b = rgb.B / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float l = (max + min) / 2f;

            if (Math.Abs(max - min) < 1e-6f) return new Hsl(0f, 0f, l);

            float d = max - min;
            float s = l > 0.5f ? d / (2f - max - min) : d / (max + min);

            float h = max == r ? (g - b) / d + (g < b ? 6f : 0f)
                    : max == g ? (b - r) / d + 2f
                    : (r - g) / d + 4f;

            return new Hsl(h * 60f, s, l);
        }

        public (byte R, byte G, byte B) ToRgb()
        {
            if (S < 1e-4f)
            {
                byte grey = (byte)Math.Round(L * 255f);
                return (grey, grey, grey);
            }

            float q = L < 0.5f ? L * (1f + S) : L + S - L * S;
            float p = 2f * L - q;
            float hk = H / 360f;

            float r = HueToRgb(p, q, hk + 1f / 3f);
            float g = HueToRgb(p, q, hk);
            float b = HueToRgb(p, q, hk - 1f / 3f);

            return (
                (byte)Math.Clamp((int)Math.Round(r * 255f), 15, 240),
                (byte)Math.Clamp((int)Math.Round(g * 255f), 15, 240),
                (byte)Math.Clamp((int)Math.Round(b * 255f), 15, 240)
            );

            static float HueToRgb(float p, float q, float t)
            {
                if (t < 0f) t += 1f;
                if (t > 1f) t -= 1f;
                if (t < 1f / 6f) return p + (q - p) * 6f * t;
                if (t < 1f / 2f) return q;
                if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
                return p;
            }
        }
    }

    /// <summary>
    /// Names every title in the language of whoever lives there.
    ///
    /// This is a separate pass from <see cref="Build"/> because it cannot run until cultures exist,
    /// and cultures cannot be assigned until the county hierarchy does — so the order is structure,
    /// then culture, then names. Nothing reads a title's name or key in between.
    ///
    /// The alternative, which this replaces, was a single hand-written pool of Norse place names
    /// used everywhere. That is fine on a map with one culture on it and actively misleading on a
    /// map with forty: a player reads place names as evidence of who settled a place, so a Norse
    /// county name in the middle of a desert culture's territory is a false statement about the
    /// world. Drawing from the local culture's own phonology means the map's names carry the same
    /// information its colours do.
    /// </summary>
    /// <summary>
    /// One name for one title, in the language of whoever lives there.
    ///
    /// Public because the title editor rerolls a single name with it long after
    /// <see cref="AssignNames"/> has run. Kept as the one definition of what each tier sounds like:
    /// a second copy in the editor would drift, and a rerolled name that no longer matched the
    /// naming of its neighbours is precisely the thing the editor exists to avoid.
    /// </summary>
    public static string GenerateName(Title title, CultureMap cultures, Rng rng)
    {
        var language = cultures.For(title).Language;

        if (title.Tier == "e")
            return language.CompoundName(rng); // Empires always get grand compound names

        if (title.Tier == "k")
        {
            // Kingdoms are 50/50: either a compound name or an affixed place name
            return rng.Chance(0.5)
                ? language.CompoundName(rng)
                : language.PlaceName(rng, language.KingdomAffixes);
        }

        var affixes = title.Tier switch
        {
            "d" => language.DuchyAffixes,
            "c" => language.CountyAffixes,
            _ => language.BaronyAffixes,
        };

        return language.PlaceName(rng, affixes);
    }

    public static void AssignNames(List<Title> roots, CultureMap cultures, Rng rng)
    {
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots) Visit(root);

        void Visit(Title title)
        {
            // Qualified: an unqualified call would bind to this local function rather than to the
            // static above it, and recurse until the stack ran out.
            string GenerateName() => Titles.GenerateName(title, cultures, rng);

            string name = GenerateName();
            string key = $"{title.Tier}_gen_{CleanKey(name)}_{title.Index}";

            for (int attempt = 0; attempt < 24 && (name.Length < 3 || usedKeys.Contains(key)); attempt++)
            {
                name = GenerateName();
                key = $"{title.Tier}_gen_{CleanKey(name)}";
            }

            for (int suffix = 2; usedKeys.Contains(key); suffix++)
                key = $"{title.Tier}_gen_{CleanKey(name)}_{suffix}";

            usedKeys.Add(key);
            title.Name = name;
            title.Key = key;

            foreach (var child in title.Children) Visit(child);
        }
    }

    private static string CleanKey(string input)
    {
        // Convert spaces and hyphens to underscores (e.g. Al-Fariq -> al_fariq)
        string cleaned = input.ToLowerInvariant().Replace(" ", "_").Replace("-", "_");

        // Flatten accents (e.g. ö -> o, á -> a) so keys remain standard a-z ASCII
        cleaned = RemoveDiacritics(cleaned);

        // Strip out anything else (like apostrophes)
        return Regex.Replace(cleaned, "[^a-z0-9_]", "");
    }
    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder(capacity: normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
    public static IEnumerable<Title> Flatten(IEnumerable<Title> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var descendant in Flatten(root.Children)) yield return descendant;
        }
    }
}