using System.Text.RegularExpressions;
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

    private static List<List<int>> Cluster(
        IReadOnlyList<int> members,
        Dictionary<int, HashSet<int>> adjacency,
        int minSize,
        int maxSize,
        Rng rng)
    {
        var assigned = new HashSet<int>();
        var clusters = new List<List<int>>();

        var shuffledMembers = members.ToList();
        Shuffle(shuffledMembers, rng);

        foreach (int start in shuffledMembers)
        {
            if (!assigned.Add(start)) continue;

            int targetSize = rng.Int(minSize, maxSize + 1);

            var cluster = new List<int> { start };
            var frontier = new Queue<int>();
            frontier.Enqueue(start);

            while (cluster.Count < targetSize && frontier.Count > 0)
            {
                int current = frontier.Dequeue();
                if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                var shuffledNeighbors = neighbors.ToList();
                Shuffle(shuffledNeighbors, rng);

                foreach (int n in shuffledNeighbors)
                {
                    if (cluster.Count >= targetSize) break;
                    if (!assigned.Add(n)) continue;
                    cluster.Add(n);
                    frontier.Enqueue(n);
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

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

    public static List<Title> Build(ProvinceMap map, int landCount, int[] order, Rng rng)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var adjacency = BuildAdjacency(map, landCount, order);

        var provinceIds = Enumerable.Range(1, landCount).ToList();
        var baronies = new List<Title>(landCount);
        for (int i = 0; i < landCount; i++)
            baronies.Add(new Title { Tier = "b", Index = i, ProvinceId = provinceIds[i] });

        var byProvince = baronies.ToDictionary(b => b.ProvinceId);

        var countyClusters = Cluster(provinceIds, adjacency, MinBaroniesPerCounty, MaxBaroniesPerCounty, rng);
        var counties = Wrap("c", countyClusters, c => c.Select(p => byProvince[p]));

        var duchyAdjacency = LiftAdjacency(countyClusters, adjacency);
        var duchyClusters = Cluster(Enumerable.Range(0, counties.Count).ToList(), duchyAdjacency, MinCountiesPerDuchy, MaxCountiesPerDuchy, rng);
        var duchies = Wrap("d", duchyClusters, c => c.Select(i => counties[i]));

        var kingdomAdjacency = LiftAdjacency(duchyClusters, duchyAdjacency);
        var kingdomClusters = Cluster(Enumerable.Range(0, duchies.Count).ToList(), kingdomAdjacency, MinDuchiesPerKingdom, MaxDuchiesPerKingdom, rng);
        var kingdoms = Wrap("k", kingdomClusters, c => c.Select(i => duchies[i]));

        var empireAdjacency = LiftAdjacency(kingdomClusters, kingdomAdjacency);
        var empireClusters = Cluster(Enumerable.Range(0, kingdoms.Count).ToList(), empireAdjacency, MinKingdomsPerEmpire, MaxKingdomsPerEmpire, rng);
        var empires = Wrap("e", empireClusters, c => c.Select(i => kingdoms[i]));

        AssignColors(empires, rng);

        Console.WriteLine($"  titles: {empires.Count} empires, {kingdoms.Count} kingdoms, " +
                          $"{duchies.Count} duchies, {counties.Count} counties, {baronies.Count} baronies " +
                          $"({sw.ElapsedMilliseconds} ms)");
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

    private static void AssignColors(List<Title> roots, Rng rng)
    {
        foreach (var root in roots) Visit(root);

        void Visit(Title title)
        {
            title.Color = ((byte)rng.Int(20, 235), (byte)rng.Int(20, 235), (byte)rng.Int(20, 235));
            foreach (var child in title.Children) Visit(child);
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
    public static void AssignNames(List<Title> roots, CultureMap cultures, Rng rng)
    {
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots) Visit(root);

        void Visit(Title title)
        {
            var language = cultures.For(title).Language;
            var suffixes = title.Tier switch
            {
                "e" => language.KingdomSuffixes,
                "k" => language.KingdomSuffixes,
                "d" => language.DuchySuffixes,
                "c" => language.CountySuffixes,
                _ => language.BaronySuffixes,
            };

            // A generated language has effectively unlimited names, so a collision is answered by
            // drawing again rather than by decorating the clashing name. Only if the language is
            // genuinely too small to produce a fresh one does this fall back to numbering.
            string name = language.PlaceName(rng, suffixes);
            string key = $"{title.Tier}_{CleanKey(name)}";

            // The length test is on the *name*, not the key — a one-letter place called O still
            // makes a perfectly unique key, and "c_o" on the map is what a reader notices.
            for (int attempt = 0; attempt < 24 && (name.Length < 3 || usedKeys.Contains(key)); attempt++)
            {
                name = language.PlaceName(rng, suffixes);
                key = $"{title.Tier}_{CleanKey(name)}";
            }

            for (int suffix = 2; usedKeys.Contains(key); suffix++)
                key = $"{title.Tier}_{CleanKey(name)}_{suffix}";

            usedKeys.Add(key);
            title.Name = name;
            title.Key = key;

            foreach (var child in title.Children) Visit(child);
        }
    }

    private static string CleanKey(string input)
    {
        string cleaned = input.ToLower().Replace(" ", "_");
        return Regex.Replace(cleaned, "[^a-z0-9_]", "");
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