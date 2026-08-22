using System.Globalization;
using System.Text.RegularExpressions;
using Ck3MapGen.Config;
using Ck3MapGen.Core;

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

    /// <summary>
    /// This one title's own word for itself — "Sultanate", "United Provinces" — in place of
    /// whatever its holder's people would call a realm of its rank, with the style of its holder
    /// in each gender beside it. Null for a title that takes its culture's word, which is nearly
    /// all of them; an import sets it for the titles its countries named. Empires, kingdoms and
    /// duchies only. See <see cref="Emit.TitleTierWriter"/>.
    /// </summary>
    public string? Form;

    /// <inheritdoc cref="Form"/>
    public string? Holder;

    /// <inheritdoc cref="Form"/>
    public string? HolderFemale;
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

    /// <summary>
    /// Builds barony-to-barony land adjacency graph.
    /// Only directly touching playable land baronies (1..baronyCount) share an edge.
    /// </summary>
    public static Dictionary<int, HashSet<int>> BuildAdjacency(ProvinceMap map, int baronyCount, int[] order)
    {
        var adjacency = new Dictionary<int, HashSet<int>>();
        for (int i = 1; i <= baronyCount; i++) adjacency[i] = [];

        int w = map.Width, h = map.Height;
        var label = map.Label;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            int nextRow = (y + 1) * w;

            for (int x = 0; x < w; x++)
            {
                int a = order[label[row + x]];
                if (a < 1 || a > baronyCount) continue;

                if (x + 1 < w) Link(a, order[label[row + x + 1]]);
                if (y + 1 < h) Link(a, order[label[nextRow + x]]);
            }
        }

        return adjacency;

        void Link(int a, int b)
        {
            if (a == b || b < 1 || b > baronyCount) return;
            if (!adjacency.TryGetValue(a, out var sa)) adjacency[a] = sa = [];
            if (!adjacency.TryGetValue(b, out var sb)) adjacency[b] = sb = [];
            sa.Add(b);
            sb.Add(a);
        }
    }

    /// <summary>
    /// Province pairs that face each other across a short stretch of OPEN SEA water.
    /// Major rivers are explicitly excluded so realms do not treat river channels as overseas straits.
    /// </summary>
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

        // Only open sea/ocean zones count for overseas links — NOT inland major rivers!
        bool IsSeaWater(int cell)
        {
            var seed = map.Seeds[map.Label[cell]];
            return !seed.IsLand && !seed.IsMajorRiver;
        }

        int BaroniedLand(int cell)
        {
            int id = order[map.Label[cell]];
            return map.Seeds[map.Label[cell]].IsLand && id >= 1 && id <= baronyCount ? id : 0;
        }

        // Every sea pixel touching a coast starts the flood, carrying the province behind it.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;
                if (!IsSeaWater(cell)) continue;

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
                if (owner[next] != 0 || !IsSeaWater(next)) continue;

                owner[next] = owner[cell];
                dist[next] = dist[cell] + 1;
                frontier.Enqueue(next);
            }
        }

        // Where two fronts meet, the water between their coasts is as wide as the two distances added together.
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

    internal static Dictionary<int, HashSet<int>> Union(
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

    private const int SettlePasses = 12;

    internal static List<List<int>> Cluster(
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

            int targetSize = rng.Int(minSize, maxSize);

            var cluster = new List<int> { start };
            var (cx, cy) = At(positions, start);

            candidates.Clear();
            Offer(start);

            while (cluster.Count < targetSize && candidates.Count > 0)
            {
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

    private static double DistanceSquared((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static (double X, double Y) At((double X, double Y)[] positions, int member)
        => member >= 0 && member < positions.Length ? positions[member] : (0, 0);

    internal static List<List<int>> AbsorbUndersized(List<List<int>> clusters,
        Dictionary<int, HashSet<int>> adjacency, int minSize, int maxSize,
        (double X, double Y)[]? positions = null)
    {
        var owner = new Dictionary<int, int>();
        for (int i = 0; i < clusters.Count; i++)
            foreach (int member in clusters[i]) owner[member] = i;

        var stranded = new HashSet<int>();

        while (true)
        {
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

    internal static (double X, double Y) Centre(List<int> cluster, (double X, double Y)[] positions)
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

    internal static (double X, double Y)[] Roll(List<List<int>> clusters,
        (double X, double Y)[] positions)
        => [.. clusters.Select(c => Centre(c, positions))];

    internal static void Shuffle<T>(List<T> list, Rng rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Math.Clamp(rng.Int(0, i), 0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    internal static Dictionary<int, HashSet<int>> LiftAdjacency(
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

    public static List<Title> Build(ProvinceMap map, int baronyCount, int[] order, MapConfig cfg, Rng rng)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Strict land-to-land adjacency for baronies (no crossing major rivers)
        var adjacency = BuildAdjacency(map, baronyCount, order);

        // 2. Sea-only bridges (strait crossings) for higher tiers
        int bridge = (int)Math.Round(cfg.Scaled(cfg.SeaBridgePixelsAtVanilla));
        var seaAdjacency = BuildSeaAdjacency(map, baronyCount, order, bridge);

        var provinceIds = Enumerable.Range(1, baronyCount).ToList();
        var baronies = new List<Title>(baronyCount);
        for (int i = 0; i < baronyCount; i++)
            baronies.Add(new Title { Tier = "b", Index = i, ProvinceId = provinceIds[i] });

        var byProvince = baronies.ToDictionary(b => b.ProvinceId);

        var provincePosition = new (double X, double Y)[baronyCount + 1];
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= baronyCount)
                provincePosition[id] = (map.Seeds[label].X, map.Seeds[label].Y);
        }

        // Counties: strictly clustered along land banks
        // The consts above are the defaults; the settings are what a map actually runs with, so a
        // county can be retuned without a rebuild. Clamped rather than trusted: a ceiling below the
        // floor would make Cluster grow counties it then immediately rejects.
        int minBaronies = Math.Max(1, cfg.MinBaroniesPerCounty);
        int maxBaronies = Math.Max(minBaronies, cfg.MaxBaroniesPerCounty);

        var countyClusters = Cluster(provinceIds, adjacency, minBaronies,
            maxBaronies, rng, provincePosition);
        var counties = Wrap("c", countyClusters, c => c.Select(p => byProvince[p]));

        var countyPosition = Roll(countyClusters, provincePosition);

        // Duchies: clustered with land adjacency and absorbed across straits if needed
        var duchyAdjacency = LiftAdjacency(countyClusters, adjacency);
        var duchySea = LiftAdjacency(countyClusters, seaAdjacency);
        var duchyClusters = AbsorbUndersized(
            Cluster(Enumerable.Range(0, counties.Count).ToList(), duchyAdjacency,
                MinCountiesPerDuchy, MaxCountiesPerDuchy, rng, countyPosition),
            Union(duchyAdjacency, duchySea), cfg.MinChildrenPerTitle, MaxCountiesPerDuchy);
        var duchies = Wrap("d", duchyClusters, c => c.Select(i => counties[i]));
        var duchyPosition = Roll(duchyClusters, countyPosition);

        // Kingdoms: can span across maritime sea links
        var kingdomAdjacency = LiftAdjacency(duchyClusters, duchyAdjacency);
        var kingdomSea = LiftAdjacency(duchyClusters, duchySea);
        var kingdomClusters = AbsorbUndersized(
            Cluster(Enumerable.Range(0, duchies.Count).ToList(), Union(kingdomAdjacency, kingdomSea),
                MinDuchiesPerKingdom, MaxDuchiesPerKingdom, rng, duchyPosition),
            Union(kingdomAdjacency, kingdomSea), cfg.MinChildrenPerTitle, MaxDuchiesPerKingdom,
            duchyPosition);
        var kingdoms = Wrap("k", kingdomClusters, c => c.Select(i => duchies[i]));
        var kingdomPosition = Roll(kingdomClusters, duchyPosition);

        // Empires: grand realms across landmasses
        var empireAdjacency = LiftAdjacency(kingdomClusters, kingdomAdjacency);
        var empireSea = LiftAdjacency(kingdomClusters, kingdomSea);
        var empireClusters = AbsorbUndersized(
            Cluster(Enumerable.Range(0, kingdoms.Count).ToList(), Union(empireAdjacency, empireSea),
                MinKingdomsPerEmpire, MaxKingdomsPerEmpire, rng, kingdomPosition),
            Union(empireAdjacency, empireSea), cfg.MinChildrenPerTitle, MaxKingdomsPerEmpire,
            kingdomPosition);
        var empires = Wrap("e", empireClusters, c => c.Select(i => kingdoms[i]));

        AssignColors(empires, rng, cfg.DeJureColorCoding);

        int seaLinked = seaAdjacency.Values.Sum(s => s.Count) / 2;
        Console.WriteLine($"  titles: {empires.Count} empires, {kingdoms.Count} kingdoms, " +
                          $"{duchies.Count} duchies, {counties.Count} counties, {baronies.Count} baronies " +
                          $"({sw.ElapsedMilliseconds} ms)");
        Console.WriteLine($"  sea links: {seaLinked} province pairs within {bridge} px of water — " +
                          $"{Overseas(kingdomClusters, duchyAdjacency)} of {kingdoms.Count} kingdoms and " +
                          $"{Overseas(empireClusters, kingdomAdjacency)} of {empires.Count} empires " +
                          $"span more than one landmass");

        Console.WriteLine($"  singleton titles left stranded: " +
                          $"{duchyClusters.Count(c => c.Count == 1)} duchies, " +
                          $"{kingdomClusters.Count(c => c.Count == 1)} kingdoms, " +
                          $"{empireClusters.Count(c => c.Count == 1)} empires");
        return empires;

    }

    /// <summary>
    /// Turns clusters of child indices into titles of one tier, parenting as it goes.
    ///
    /// Lifted out of <see cref="Build"/> so the Azgaar-constrained builder can wrap its own clusters
    /// the same way rather than growing a second, subtly different version of this.
    /// </summary>
    internal static List<Title> Wrap(string tier, List<List<int>> clusters,
        Func<List<int>, IEnumerable<Title>> resolve)
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

    /// <summary>
    /// Colours a finished hierarchy. The Azgaar-constrained builder assembles its own roots and so
    /// never reaches the colouring at the end of <see cref="Build"/>; this is that step, exposed.
    /// </summary>
    internal static void AssignColorsTo(List<Title> empires, Rng rng, bool deJure = true)
        => AssignColors(empires, rng, deJure);

    public static void RecolorChildren(Title parent, Rng rng)
        => DistributeChildren(parent, Hsl.FromRgb(parent.Color), rng);

    private const float GoldenAngle = 137.507764f;

    private static void AssignColors(List<Title> empires, Rng rng, bool deJure = true)
    {
        if (empires.Count == 0) return;

        float baseHue = rng.Float(0f, 360f);

        if (!deJure) { AssignIndependent(empires, baseHue, rng); return; }

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
    /// Gives every title in the tree its own place in the golden-angle sequence rather than a shade
    /// of its parent's colour.
    ///
    /// Walking the tree depth-first is what makes this useful: consecutive indices land far apart in
    /// hue, and a title's neighbours are mostly its siblings and its parent's neighbours, so the
    /// titles that share a border are the ones the sequence separates hardest. The cost is that the
    /// colour no longer says anything about who a title's liege is, which is the whole point of the
    /// option.
    /// </summary>
    private static void AssignIndependent(List<Title> roots, float baseHue, Rng rng)
    {
        int n = 0;

        void Paint(Title title)
        {
            float hue = (baseHue + n++ * GoldenAngle + rng.Float(-6f, 6f)) % 360f;
            title.Color = new Hsl(hue, rng.Float(0.50f, 0.85f), rng.Float(0.38f, 0.62f)).ToRgb();
            foreach (var child in title.Children) Paint(child);
        }

        foreach (var root in roots) Paint(root);
    }

    private static void DistributeChildren(Title parent, Hsl parentHsl, Rng rng)
    {
        int count = parent.Children.Count;
        if (count == 0) return;

        var (maxHueShift, maxLitShift, maxSatShift) = parent.Tier switch
        {
            "e" => (28f, 0.14f, 0.12f),
            "k" => (16f, 0.11f, 0.10f),
            "d" => (8f, 0.07f, 0.06f),
            _ => (2f, 0.03f, 0.03f),
        };

        for (int i = 0; i < count; i++)
        {
            var child = parent.Children[i];

            float t = count == 1 ? 0f : (float)i / (count - 1) * 2f - 1f;
            float hueDelta = t * maxHueShift + rng.Float(-3f, 3f);

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

    private readonly struct Hsl
    {
        public readonly float H;
        public readonly float S;
        public readonly float L;

        public Hsl(float h, float s, float l)
        {
            H = ((h % 360f) + 360f) % 360f;
            S = Math.Clamp(s, 0.25f, 0.90f);
            L = Math.Clamp(l, 0.22f, 0.78f);
        }

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

    public static string GenerateName(Title title, CultureMap cultures, Rng rng)
    {
        var language = cultures.For(title).Language;

        if (title.Tier == "e")
            return language.CompoundName(rng);

        if (title.Tier == "k")
        {
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

    /// <param name="preferred">
    /// Names borrowed from an import, by title. A title listed here takes its name from the export
    /// instead of the generator; everything absent is named as usual. Null on a generated map.
    /// </param>
    public static void AssignNames(List<Title> roots, CultureMap cultures, Rng rng,
        Dictionary<Title, string>? preferred = null)
    {
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots) Visit(root);

        void Visit(Title title)
        {
            string GenerateName() => Titles.GenerateName(title, cultures, rng);

            string name = preferred?.GetValueOrDefault(title) is { Length: > 0 } imported
                ? imported
                : GenerateName();

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
        string cleaned = input.ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        cleaned = RemoveDiacritics(cleaned);
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