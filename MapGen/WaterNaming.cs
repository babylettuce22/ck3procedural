using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public static class WaterNaming
{
    /// <summary>
    /// Generates localized names for all sea zones and major river provinces.
    /// Returns a dictionary mapping Province ID (1-based) -> Localized Name.
    /// </summary>
    public static Dictionary<int, string> Generate(
        ProvinceMap provinces,
        int[] order,
        int landCount,
        int riverCount,
        CultureMap cultures,
        List<Title> empires,
        MapConfig cfg,
        Rng rng)
    {
        var names = new Dictionary<int, string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Build adjacency graph for water provinces
        var byId = new int[provinces.Count + 1];
        for (int label = 0; label < provinces.Count; label++)
            byId[order[label]] = label;

        var adjacency = BuildAdjacency(provinces, order);

        // 2. Name Major River Provinces
        NameMajorRivers(provinces, order, landCount, riverCount, byId, adjacency, cultures, names, usedNames, rng);

        // 3. Name Sea Zones by Clustering into Macro Water Bodies
        NameSeaZones(provinces, order, riverCount, byId, adjacency, cultures, empires, cfg, names, usedNames, rng);

        return names;
    }

    private static void NameMajorRivers(
        ProvinceMap provinces,
        int[] order,
        int landCount,
        int riverCount,
        int[] byId,
        Dictionary<int, HashSet<int>> adjacency,
        CultureMap cultures,
        Dictionary<int, string> names,
        HashSet<string> usedNames,
        Rng rng)
    {
        if (riverCount <= landCount) return;

        var visited = new bool[provinces.Count + 1];

        for (int id = landCount + 1; id <= riverCount; id++)
        {
            if (visited[id]) continue;

            // Gather all connected provinces in this river system
            var system = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(id);
            visited[id] = true;

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                system.Add(curr);

                if (adjacency.TryGetValue(curr, out var neighbours))
                {
                    foreach (int nb in neighbours)
                    {
                        if (nb > landCount && nb <= riverCount && !visited[nb])
                        {
                            visited[nb] = true;
                            queue.Enqueue(nb);
                        }
                    }
                }
            }

            // Find neighboring land cultures to name the river in their native tongue
            var localCulture = FindNeighborCulture(system, adjacency, byId, cultures, provinces);
            string baseName = Unique(localCulture.Language.Word(rng, 1, 2), usedNames);

            // Sort system from highest to lowest Y/position to order downstream
            system.Sort((a, b) => provinces.Seeds[byId[b]].Y.CompareTo(provinces.Seeds[byId[a]].Y));

            if (system.Count == 1)
            {
                names[system[0]] = $"River {baseName}";
            }
            else if (system.Count == 2)
            {
                names[system[0]] = $"Upper {baseName}";
                names[system[1]] = $"Lower {baseName}";
            }
            else
            {
                for (int i = 0; i < system.Count; i++)
                {
                    if (i == 0)
                        names[system[i]] = $"Upper {baseName}";
                    else if (i == system.Count - 1)
                        names[system[i]] = rng.Chance(0.5) ? $"Lower {baseName}" : $"{baseName} Delta";
                    else
                        names[system[i]] = rng.Chance(0.5) ? $"River {baseName}" : $"{baseName} Reach";
                }
            }
        }
    }

    private static void NameSeaZones(
        ProvinceMap provinces,
        int[] order,
        int riverCount,
        int[] byId,
        Dictionary<int, HashSet<int>> adjacency,
        CultureMap cultures,
        List<Title> empires,
        MapConfig cfg,
        Dictionary<int, string> names,
        HashSet<string> usedNames,
        Rng rng)
    {
        int totalProvinces = provinces.Count;
        if (totalProvinces <= riverCount) return;

        var visited = new bool[totalProvinces + 1];

        // Cluster sea zones into bodies of 3 to 7 provinces
        for (int startId = riverCount + 1; startId <= totalProvinces; startId++)
        {
            if (visited[startId]) continue;

            var cluster = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(startId);
            visited[startId] = true;

            int targetSize = rng.Int(3, 7);

            while (queue.Count > 0 && cluster.Count < targetSize)
            {
                int curr = queue.Dequeue();
                cluster.Add(curr);

                if (adjacency.TryGetValue(curr, out var neighbours))
                {
                    foreach (int nb in neighbours)
                    {
                        if (nb > riverCount && !visited[nb])
                        {
                            visited[nb] = true;
                            queue.Enqueue(nb);
                        }
                    }
                }
            }

            // Determine body type based on land enclosure
            int landContact = 0;
            foreach (int seaId in cluster)
            {
                if (adjacency.TryGetValue(seaId, out var nbs))
                    landContact += nbs.Count(n => n <= riverCount);
            }

            double enclosure = (double)landContact / Math.Max(1, cluster.Count);

            var culture = FindNeighborCulture(cluster, adjacency, byId, cultures, provinces);
            string baseName = Unique(culture.Language.Word(rng, 2, 3), usedNames);

            string bodyType = enclosure switch
            {
                > 3.0 => rng.Pick(["Gulf of", "Bay of", "Sound of"]),
                > 1.5 => rng.Pick(["Sea", "Sea of", "Gulf of"]),
                _ => rng.Pick(["Ocean", "Sea", "Great Sea of"]),
            };

            string bodyFullName = bodyType.EndsWith("of")
                ? $"{bodyType} {baseName}"
                : $"{baseName} {bodyType}";

            // Calculate centroid of the sea body to give directional qualifiers
            double cx = 0, cy = 0;
            foreach (int seaId in cluster)
            {
                var seed = provinces.Seeds[byId[seaId]];
                cx += seed.X;
                cy += seed.Y;
            }
            cx /= cluster.Count;
            cy /= cluster.Count;

            if (cluster.Count == 1)
            {
                names[cluster[0]] = bodyFullName;
            }
            else
            {
                foreach (int seaId in cluster)
                {
                    var seed = provinces.Seeds[byId[seaId]];
                    double dx = seed.X - cx;
                    double dy = seed.Y - cy;

                    string prefix = "";
                    if (Math.Abs(dy) > Math.Abs(dx) * 1.3)
                        prefix = dy < 0 ? "Northern " : "Southern ";
                    else if (Math.Abs(dx) > Math.Abs(dy) * 1.3)
                        prefix = dx < 0 ? "Western " : "Eastern ";

                    names[seaId] = $"{prefix}{bodyFullName}";
                }
            }
        }
    }

    private static Culture FindNeighborCulture(
        List<int> waterIds,
        Dictionary<int, HashSet<int>> adjacency,
        int[] byId,
        CultureMap cultures,
        ProvinceMap provinces)
    {
        var neighborCounties = new Dictionary<Culture, int>();

        foreach (int wid in waterIds)
        {
            if (!adjacency.TryGetValue(wid, out var nbs)) continue;

            foreach (int nb in nbs)
            {
                if (nb >= 1 && nb < provinces.Count && provinces.Seeds[byId[nb]].IsLand)
                {
                    // Find culture of neighboring land province
                    var seed = provinces.Seeds[byId[nb]];
                    if (seed.IsLand)
                    {
                        var c = cultures.Cultures[0]; // fallback
                        neighborCounties[c] = neighborCounties.GetValueOrDefault(c) + 1;
                    }
                }
            }
        }

        if (neighborCounties.Count > 0)
        {
            return neighborCounties.OrderByDescending(kv => kv.Value).First().Key;
        }

        return cultures.Cultures[0];
    }

    private static Dictionary<int, HashSet<int>> BuildAdjacency(ProvinceMap map, int[] order)
    {
        var adj = new Dictionary<int, HashSet<int>>();
        int w = map.Width, h = map.Height;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int a = order[map.Label[y * w + x]];

                if (x + 1 < w) Link(a, order[map.Label[y * w + x + 1]]);
                if (y + 1 < h) Link(a, order[map.Label[(y + 1) * w + x]]);
            }
        }

        return adj;

        void Link(int u, int v)
        {
            if (u == v) return;
            if (!adj.TryGetValue(u, out var su)) adj[u] = su = [];
            if (!adj.TryGetValue(v, out var sv)) adj[v] = sv = [];
            su.Add(v);
            sv.Add(u);
        }
    }

    private static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name)) return name;

        for (int suffix = 2; suffix < 100; suffix++)
        {
            string candidate = $"{name}{suffix}";
            if (used.Add(candidate)) return candidate;
        }

        return name;
    }
}