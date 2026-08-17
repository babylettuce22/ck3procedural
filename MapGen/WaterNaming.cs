using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public static class WaterNaming
{
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

        var byId = new int[provinces.Count + 1];
        for (int label = 0; label < provinces.Count; label++)
            byId[order[label]] = label;

        // 8-way adjacency so diagonal water connections are linked
        var adjacency = BuildAdjacency(provinces, order);

        // 1. Name Major River Provinces
        NameMajorRivers(provinces, order, landCount, riverCount, byId, adjacency, cultures, empires, names, usedNames, rng);

        // 2. Name Sea Zones by Agglomerative Clustering
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
        List<Title> empires,
        Dictionary<int, string> names,
        HashSet<string> usedNames,
        Rng rng)
    {
        if (riverCount <= landCount) return;

        var assigned = new bool[provinces.Count + 1];

        for (int id = landCount + 1; id <= riverCount; id++)
        {
            if (assigned[id]) continue;

            // Gather all connected provinces in this river system
            var system = new List<int> { id };
            assigned[id] = true;

            var frontier = new Queue<int>();
            frontier.Enqueue(id);

            while (frontier.Count > 0)
            {
                int curr = frontier.Dequeue();
                if (!adjacency.TryGetValue(curr, out var neighbours)) continue;

                foreach (int nb in neighbours)
                {
                    if (nb > landCount && nb <= riverCount && !assigned[nb])
                    {
                        assigned[nb] = true;
                        system.Add(nb);
                        frontier.Enqueue(nb);
                    }
                }
            }

            var localCulture = FindNeighborCulture(system, adjacency, byId, cultures, provinces, empires);
            string baseName = Unique(localCulture.Language.Word(rng, 1, 2), usedNames);

            // Sort downstream: highest Y to lowest Y (or position)
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
                        names[system[i]] = (i % 2 == 1) ? $"River {baseName}" : $"{baseName} Reach";
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

        var assigned = new bool[totalProvinces + 1];
        var clusters = new List<List<int>>();

        // Complete agglomerative clustering (guarantees NO dropped nodes)
        for (int startId = riverCount + 1; startId <= totalProvinces; startId++)
        {
            if (assigned[startId]) continue;

            var cluster = new List<int> { startId };
            assigned[startId] = true;

            int targetSize = rng.Int(3, 7);
            var candidates = new List<int>();

            void Offer(int id)
            {
                if (!adjacency.TryGetValue(id, out var nbs)) return;
                foreach (int n in nbs)
                    if (n > riverCount && !assigned[n] && !candidates.Contains(n))
                        candidates.Add(n);
            }

            Offer(startId);

            while (cluster.Count < targetSize && candidates.Count > 0)
            {
                int chosen = candidates[0];
                candidates.RemoveAt(0);

                if (assigned[chosen]) continue;
                assigned[chosen] = true;
                cluster.Add(chosen);
                Offer(chosen);
            }

            clusters.Add(cluster);
        }

        // Name each water body and assign unique directional qualifiers to its provinces
        foreach (var cluster in clusters)
        {
            int landContact = 0;
            foreach (int seaId in cluster)
            {
                if (adjacency.TryGetValue(seaId, out var nbs))
                    landContact += nbs.Count(n => n <= riverCount);
            }

            double enclosure = (double)landContact / Math.Max(1, cluster.Count);

            var culture = FindNeighborCulture(cluster, adjacency, byId, cultures, provinces, empires);
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

            if (cluster.Count == 1)
            {
                names[cluster[0]] = bodyFullName;
                continue;
            }

            // Calculate cluster centroid
            double cx = 0, cy = 0;
            foreach (int seaId in cluster)
            {
                var seed = provinces.Seeds[byId[seaId]];
                cx += seed.X;
                cy += seed.Y;
            }
            cx /= cluster.Count;
            cy /= cluster.Count;

            var clusterNamesUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (int seaId in cluster)
            {
                var seed = provinces.Seeds[byId[seaId]];
                double dx = seed.X - cx;
                double dy = seed.Y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                string qualifier;
                if (dist < 15.0)
                {
                    qualifier = "Central ";
                }
                else
                {
                    // 8-way angle classification
                    double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI; // -180 .. 180
                    qualifier = angle switch
                    {
                        >= -22.5 and < 22.5 => "Eastern ",
                        >= 22.5 and < 67.5 => "Southeastern ",
                        >= 67.5 and < 112.5 => "Southern ",
                        >= 112.5 and < 157.5 => "Southwestern ",
                        >= -67.5 and < -22.5 => "Northeastern ",
                        >= -112.5 and < -67.5 => "Northern ",
                        >= -157.5 and < -112.5 => "Northwestern ",
                        _ => "Western ",
                    };
                }

                string candidate = $"{qualifier}{bodyFullName}";

                // Fallback discriminators if two provinces share an angle quadrant
                if (!clusterNamesUsed.Add(candidate))
                {
                    string[] suffixes = ["Coast", "Waters", "Deep", "Shoals", "Narrows"];
                    foreach (var s in suffixes)
                    {
                        candidate = $"{baseName} {s}";
                        if (clusterNamesUsed.Add(candidate)) break;
                    }
                }

                names[seaId] = candidate;
            }
        }
    }

    private static Culture FindNeighborCulture(
        List<int> waterIds,
        Dictionary<int, HashSet<int>> adjacency,
        int[] byId,
        CultureMap cultures,
        ProvinceMap provinces,
        List<Title> empires)
    {
        var baronies = Titles.Flatten(empires)
            .Where(t => t.Tier == "b" && t.ProvinceId > 0)
            .ToDictionary(b => b.ProvinceId);

        var neighborCultures = new Dictionary<Culture, int>();

        foreach (int wid in waterIds)
        {
            if (!adjacency.TryGetValue(wid, out var nbs)) continue;

            foreach (int nb in nbs)
            {
                if (baronies.TryGetValue(nb, out var barony))
                {
                    var c = cultures.For(barony);
                    neighborCultures[c] = neighborCultures.GetValueOrDefault(c) + 1;
                }
            }
        }

        if (neighborCultures.Count > 0)
        {
            return neighborCultures.OrderByDescending(kv => kv.Value).First().Key;
        }

        return cultures.Cultures.Count > 0 ? cultures.Cultures[0] : null!;
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
                if (x + 1 < w && y + 1 < h) Link(a, order[map.Label[(y + 1) * w + x + 1]]);
                if (x > 0 && y + 1 < h) Link(a, order[map.Label[(y + 1) * w + x - 1]]);
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