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

    // --- Handcrafted Names ---
    private static readonly string[] NamesE = ["Nordriki", "Vestrriki", "Austrriki", "Skandaland", "Midgardr", "Skandinavia", "Gautariki", "Sveariki"];
    private static readonly string[] NamesK = ["Noregr", "Svitjod", "Danmork", "Island", "Gautland", "Jamtland", "Trondelag", "Halogaland", "Agder", "Rogaland", "Viken"];
    private static readonly string[] NamesD = ["Hordafylki", "Rogafylki", "Sygnafylki", "Raumariki", "Ranriki", "Alvheimar", "Grenland", "Telemark", "Valdres", "Hadaland"];
    private static readonly string[] NamesC = ["Oslo", "Bergen", "Nidaros", "Tonsberg", "Stavanger", "Tromso", "Alesund", "Skien", "Hamar", "Sarpsborg", "Geilo", "Flam", "Eidfjord"];
    private static readonly string[] NamesB = ["Heim", "Vik", "Nes", "Stad", "Dal", "Berg", "Voll", "Haugr", "Bru", "Tun", "Gardr", "Akr", "Sandr", "Myrr", "Skogr"];

    // --- Expanded Procedural Syllable Pools ---
    private static readonly string[] Prefixes = [
        "As", "Arn", "Bjorn", "Dag", "Egil", "Fjord", "Gunn", "Hald", "Ing", "Ketil",
        "Lind", "Mund", "Nord", "Ost", "Sig", "Tor", "Ulf", "Val", "Vest", "Alv",
        "Brand", "Einar", "Frey", "Grim", "Haakon", "Ivar", "Jarl", "Kare", "Leif", "Magn",
        "Odd", "Ragn", "Sten", "Tryg", "Vig", "Yng", "Aki", "Bror", "Gud", "Hjalm",
        "Karl", "Loke", "Orm", "Rune", "Sverr", "Thor", "Vidar", "Sigh", "Kjell", "Bryn"
    ];

    private static readonly string[] Infixes = [
        "ar", "en", "is", "al", "or", "un", "in", "at", "um", "ald",
        "var", "vald", "gard", "fyr", "sjo", "sten", "vold", "dal", "berg"
    ];

    private static readonly string[] SuffixesB = [
        "heim", "vik", "nes", "stad", "dal", "berg", "voll", "bru", "tun", "set",
        "haugr", "akr", "sandr", "myrr", "skogr", "gardr", "torp", "holt", "kilde", "moen"
    ];

    private static readonly string[] SuffixesC = [
        "by", "stad", "fjord", "sund", "gard", "nes", "dal", "berg", "vik", "heim",
        "tun", "hus", "kaupang", "torp", "holt", "vang", "moen", "land", "var", "kile"
    ];

    private static readonly string[] SuffixesD = ["fylki", "sysla", "mark", "rike", "land", "heimen", "bygd", "fylke"];
    private static readonly string[] SuffixesK = ["land", "riki", "veldi", "mork", "reich", "velde"];

    // Thematic prefix modifiers to resolve collisions naturally before falling back to numbers
    private static readonly string[] DescriptivePrefixes = [
        "Efra",   // Upper
        "Nedra",  // Lower
        "Nyr",    // New
        "Gammel", // Old
        "Austr",  // East
        "Vestr",  // West
        "Sydr",   // South
        "Nordr",  // North
        "Stora",  // Great
        "Lilla"   // Little
    ];

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

        AssignKeysAndColors(empires, rng);

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

    private static void AssignKeysAndColors(List<Title> roots, Rng rng)
    {
        var usedKeys = new HashSet<string>();

        foreach (var root in roots) Visit(root);

        void Visit(Title title)
        {
            var (name, key) = GenerateThematicName(title.Tier, rng, usedKeys);
            title.Name = name;
            title.Key = key;

            title.Color = ((byte)rng.Int(20, 235), (byte)rng.Int(20, 235), (byte)rng.Int(20, 235));
            foreach (var child in title.Children) Visit(child);
        }
    }

    private static (string Name, string Key) GenerateThematicName(string tier, Rng rng, HashSet<string> usedKeys)
    {
        string baseName = "";
        string[] pool = tier switch
        {
            "e" => NamesE,
            "k" => NamesK,
            "d" => NamesD,
            "c" => NamesC,
            _ => NamesB
        };

        // Try hand-crafted names first
        var unusedFromPool = pool.Where(p => !usedKeys.Contains($"{tier}_{CleanKey(p)}")).ToList();
        if (unusedFromPool.Count > 0)
        {
            baseName = PickRandom(unusedFromPool, rng);
        }
        else
        {
            // Grammatical generation with multiple patterns to produce huge variance
            string prefix = PickRandom(Prefixes, rng);
            string suffix = tier switch
            {
                "e" => PickRandom(SuffixesK, rng) + "r",
                "k" => PickRandom(SuffixesK, rng),
                "d" => PickRandom(SuffixesD, rng),
                "c" => PickRandom(SuffixesC, rng),
                _ => PickRandom(SuffixesB, rng)
            };

            int pattern = rng.Int(0, 3);
            if (pattern == 0)
            {
                // Prefix + Infix + Suffix (e.g. As-ar-by)
                string infix = PickRandom(Infixes, rng);
                baseName = prefix + infix + suffix;
            }
            else if (pattern == 1)
            {
                // Modifier + Prefix + Suffix (e.g. Efra As-by)
                string modifier = PickRandom(DescriptivePrefixes, rng);
                baseName = $"{modifier} {prefix}{suffix}";
            }
            else
            {
                // Standard Prefix + Suffix (e.g. As-by)
                baseName = prefix + suffix;
            }
        }

        string cleanKey = $"{tier}_{CleanKey(baseName)}";
        string finalKey = cleanKey;
        string finalName = baseName;

        // Smart deduplication: try applying descriptive prefixes first before appending raw numbers
        int modifierIndex = 0;
        int numericCounter = 1;

        while (!usedKeys.Add(finalKey))
        {
            if (modifierIndex < DescriptivePrefixes.Length)
            {
                // Converts "Oslo" -> "Efra Oslo", "Vestr Oslo", etc.
                string prefix = DescriptivePrefixes[modifierIndex++];
                finalName = $"{prefix} {baseName}";
                finalKey = $"{tier}_{CleanKey(finalName)}";
            }
            else
            {
                // Last resort fallback: "Oslo 2", "Oslo 3"
                numericCounter++;
                finalKey = $"{cleanKey}_{numericCounter}";
                finalName = $"{baseName} {numericCounter}";
            }
        }

        return (finalName, finalKey);
    }

    private static string CleanKey(string input)
    {
        string cleaned = input.ToLower().Replace(" ", "_");
        return Regex.Replace(cleaned, "[^a-z0-9_]", "");
    }

    private static T PickRandom<T>(IReadOnlyList<T> list, Rng rng)
    {
        if (list.Count == 0) return default!;
        int idx = Math.Clamp(rng.Int(0, list.Count - 1), 0, list.Count - 1);
        return list[idx];
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