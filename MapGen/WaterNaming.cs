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
        Rng rng,
        List<MajorRiverPath>? majorRivers = null,
        AzgaarImport? azgaar = null)
    {
        var names = new Dictionary<int, string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var byId = new int[provinces.Count + 1];
        for (int label = 0; label < provinces.Count; label++)
            byId[order[label]] = label;

        // 8-way adjacency so diagonal water connections are linked
        var adjacency = BuildAdjacency(provinces, order);

        // 1. Name Major River Provinces
        NameMajorRivers(provinces, order, landCount, riverCount, byId, adjacency, cultures, empires,
            majorRivers, names, usedNames, rng, azgaar);

        // 2. Name Sea Zones by Agglomerative Clustering
        NameSeaZones(provinces, order, riverCount, byId, adjacency, cultures, empires, cfg, names,
            usedNames, rng, azgaar);

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
        List<MajorRiverPath>? majorRivers,
        Dictionary<int, string> names,
        HashSet<string> usedNames,
        Rng rng,
        AzgaarImport? azgaar)
    {
        if (riverCount <= landCount) return;

        var systems = GroupRiverProvinces(provinces, order, landCount, riverCount, byId,
            adjacency, majorRivers);

        foreach (var system in systems)
        {
            // The export's own name for whichever of its rivers this system mostly follows, when it
            // has one and nothing else has taken it. Asked of the whole system at once rather than
            // province by province — see AzgaarImport.RiverFor, which is why one river does not come
            // out wearing four names down its length.
            //
            // No article stripping here, unlike the sea zones below: a river name never takes a
            // directional qualifier in front of it, so "the Aldwater" survives intact.
            string? imported = azgaar?.RiverFor(system)?.Name;

            string baseName = imported is { Length: > 0 } && !usedNames.Contains(imported)
                ? Unique(imported, usedNames)
                : UniqueFrom(() => FindNeighborCulture(system, adjacency, byId, cultures, provinces, empires)
                                       .Tongue.Word(rng, 1, 2), usedNames);

            Name(system, baseName, names);
        }
    }

    /// <summary>
    /// One name per river, applied along its whole course.
    ///
    /// The river keeps a single identity from source to mouth — every province is "River X" — and
    /// only the two ends are qualified, which is how a real river reads on a map. The previous
    /// scheme alternated "River X" and "X Reach" on every other province, so following one
    /// downstream looked like the name changing under you even though the root word never did.
    ///
    /// <paramref name="system"/> must already be ordered source-first; see
    /// <see cref="GroupRiverProvinces"/>.
    /// </summary>
    private static void Name(List<int> system, string baseName, Dictionary<int, string> names)
    {
        if (system.Count == 1)
        {
            names[system[0]] = $"River {baseName}";
            return;
        }

        if (system.Count == 2)
        {
            names[system[0]] = $"Upper {baseName}";
            names[system[1]] = $"Lower {baseName}";
            return;
        }

        // The headwaters get "Upper", the mouth "Delta" and the province above it "Lower". Long
        // rivers earn a proportionate headwater stretch; short ones give up just the first province,
        // so a four-province river does not end up entirely made of qualifiers.
        int upper = Math.Clamp(system.Count / 5, 1, Math.Max(1, system.Count - 3));

        for (int i = 0; i < system.Count; i++)
        {
            names[system[i]] =
                  i < upper                ? $"Upper {baseName}"
                : i == system.Count - 1    ? $"{baseName} Delta"
                : i == system.Count - 2    ? $"Lower {baseName}"
                :                            $"River {baseName}";
        }
    }

    /// <summary>
    /// The river provinces, grouped into rivers and ordered from source to mouth.
    ///
    /// Ordering is the whole point. The provinces themselves know nothing about which way the water
    /// runs, so this used to sort a connected component by latitude — which on an east-west river is
    /// very nearly constant, making the order arbitrary and scattering "Upper" and "Lower" at random
    /// points along the course.
    ///
    /// <see cref="MajorRiverPath"/> already holds the answer: <see cref="MajorRiverPath.Points"/> is
    /// the traced course in province pixels, source first. Each river province is matched to the
    /// nearest point on the nearest course, which gives it both an identity — which river it belongs
    /// to — and a position along that river to sort by. Each path is a single trunk, because
    /// TraceUpstream follows only the strongest feeder at every junction, so the ordering is
    /// unambiguous.
    ///
    /// Falls back to the old connected-component grouping when no paths are available, so a run with
    /// major rivers disabled, or an older saved world, still names whatever river provinces exist.
    /// </summary>
    private static List<List<int>> GroupRiverProvinces(
        ProvinceMap provinces,
        int[] order,
        int landCount,
        int riverCount,
        int[] byId,
        Dictionary<int, HashSet<int>> adjacency,
        List<MajorRiverPath>? majorRivers)
    {
        if (majorRivers is { Count: > 0 })
        {
            // Nearest course point per province: which river, and how far along it.
            var along = new Dictionary<int, (int System, int Position)>();

            for (int id = landCount + 1; id <= riverCount; id++)
            {
                var seed = provinces.Seeds[byId[id]];
                float bestDistance = float.MaxValue;
                int bestSystem = -1, bestPosition = 0;

                for (int s = 0; s < majorRivers.Count; s++)
                {
                    var points = majorRivers[s].Points;
                    for (int p = 0; p < points.Count; p++)
                    {
                        float dx = points[p].X - seed.X;
                        float dy = points[p].Y - seed.Y;
                        float distance = dx * dx + dy * dy;

                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestSystem = s;
                            bestPosition = p;
                        }
                    }
                }

                if (bestSystem >= 0) along[id] = (bestSystem, bestPosition);
            }

            var grouped = new List<List<int>>();
            foreach (var bySystem in along.GroupBy(kv => kv.Value.System).OrderBy(g => g.Key))
            {
                grouped.Add([.. bySystem.OrderBy(kv => kv.Value.Position).Select(kv => kv.Key)]);
            }

            if (grouped.Count > 0) return grouped;
        }

        // No courses to match against: fall back to connected components, ordered by latitude.
        var systems = new List<List<int>>();
        var assigned = new bool[provinces.Count + 1];

        for (int id = landCount + 1; id <= riverCount; id++)
        {
            if (assigned[id]) continue;

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

            system.Sort((a, b) => provinces.Seeds[byId[b]].Y.CompareTo(provinces.Seeds[byId[a]].Y));
            systems.Add(system);
        }

        return systems;
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
        Rng rng,
        AzgaarImport? azgaar)
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

            // Azgaar writes some of these with an article — "the Sundering Sea". A CK3 title name is
            // a bare noun phrase the game puts its own words in front of, and the directional
            // qualifiers below prepend theirs directly, so leaving it produces the "Northeastern the
            // Sundering Sea" that made stripping it necessary.
            string? importedBody = azgaar?.WaterBodyFor(cluster)?.Name;
            if (importedBody is { Length: > 0 }) importedBody = AzgaarNaming.StripArticle(importedBody);

            // Two names, because they are used for different things: the full one titles the body,
            // and the bare one is what the directional qualifiers below are built on.
            string bodyFullName;
            string baseName;

            if (importedBody is { Length: > 0 } && !usedNames.Contains(importedBody))
            {
                // Azgaar's name already carries its own body word — "Sundering Sea", "Gulf of Kehl"
                // — so nothing is appended, and the bare form is recovered by taking that word back
                // off. Building "Eastern Sundering Sea Sea" was the alternative.
                bodyFullName = Unique(importedBody, usedNames);
                baseName = StripBodyType(bodyFullName);
            }
            else
            {
                string word = UniqueFrom(() => culture.Tongue.Word(rng), usedNames);
                baseName = word;

                // Rolled only here. Doing it above, before the branch, would spend the same rng draw
                // on imported bodies that never use it and shift every later name on the map.
                string bodyType = enclosure switch
                {
                    > 3.0 => rng.Pick(["Gulf of", "Bay of", "Sound of"]),
                    > 1.5 => rng.Pick(["Sea", "Sea of", "Gulf of"]),
                    _ => rng.Pick(["Ocean", "Sea", "Great Sea of"]),
                };

                bodyFullName = bodyType.EndsWith("of")
                    ? $"{bodyType} {word}"
                    : $"{word} {bodyType}";
            }

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

    /// <summary>
    /// Takes the body word off a water name, leaving the bare one underneath.
    ///
    /// Only needed for imported names, which arrive complete — Azgaar writes "Gulf of Kehl" and
    /// "Sundering Sea", while a generated name is built from a bare word this program still has. The
    /// directional qualifiers want that bare word, so this recovers it.
    ///
    /// Both word orders, because both occur: the "of" forms lead with the body and the rest trail
    /// it. Anything unrecognised is returned whole, which costs a slightly long qualified name and
    /// never a wrong one.
    /// </summary>
    private static string StripBodyType(string full)
    {
        string[] prefixes = ["Gulf of ", "Bay of ", "Sound of ", "Sea of ", "Strait of ", "Great Sea of "];
        foreach (string prefix in prefixes)
        {
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return full[prefix.Length..].Trim();
        }

        string[] suffixes = [" Ocean", " Sea", " Gulf", " Bay", " Sound", " Strait", " Lake"];
        foreach (string suffix in suffixes)
        {
            if (full.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return full[..^suffix.Length].Trim();
        }

        return full;
    }

    /// <summary>A fresh draw until one is free: a collision costs a re-roll, not a numeral on the map.</summary>
    private static string UniqueFrom(Func<string> draw, HashSet<string> used)
    {
        string name = draw();
        for (int attempt = 0; attempt < 16 && used.Contains(name); attempt++) name = draw();
        return Unique(name, used);
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