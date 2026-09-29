using Ck3MapGen.Config;
using Ck3MapGen.Core;
using static Ck3MapGen.MapGen.UniqueNames;

namespace Ck3MapGen.MapGen;

/// <summary>
/// A named place bigger than a title and shaped by the land rather than by who holds it: a crater
/// basin, a rift, a mountain range. <see cref="Kind"/> says which, and picks the word its name ends
/// in and what holding its land is worth; <see cref="Baronies"/> are the land provinces inside it;
/// <see cref="Label"/> is a line to letter its name along on the flat map, as shares of the map's
/// width and height, y down.
/// </summary>
public sealed record Landmark(string Key, string Kind, string Name, int[] Baronies, (double X, double Y)[] Label);

/// <summary>
/// Finds the map's landmarks and names them. The rest of their life is in the existing writers:
/// <see cref="Emit.PassWriter.WriteLandmarks"/> gives each province modifiers (lowland and heights) and a geographical
/// region, and Emit.FlatmapInk letters its name on the parchment.
///
/// Where they come from is the one part that is not general. For now that is the set-piece the
/// Quick page drew (<see cref="MapConfig.SetPiece"/>), which knows its own shape exactly. Anything
/// else that can say "this kind of place, these points, this line" — a range found in an ordinary
/// map's walls, an enclosed sea — is another source in <see cref="Sources"/>, and everything after
/// it works unchanged.
/// </summary>
public static class Landmarks
{
    /// <summary>
    /// Every landmark, named in the tongue of the culture holding most of it. Draws only from
    /// <paramref name="rng"/>, its own stream, so no other name on the map moves. A drowned crater
    /// also names the sea zones inside its rim after itself, in <paramref name="waterNames"/>.
    /// </summary>
    public static List<Landmark> Build(MapConfig cfg, ProvinceMap provinces, int[] order, int baronyCount,
        int riverCount, List<Title> empires, CultureMap cultures, Dictionary<int, string> waterNames, Rng rng,
        IReadOnlyList<(string Name, List<int> Zones)>? seas = null)
    {
        var found = new List<Landmark>();
        var renamed = new HashSet<int>();
        NameSetPieces(found, renamed, cfg, provinces, order, baronyCount, riverCount, empires, cultures, waterNames, rng);
        if (seas is { Count: > 0 }) AddSeas(found, renamed, provinces, order, seas);
        return found;
    }

    /// <summary>The set-piece's landmarks, named. See <see cref="Build"/>.</summary>
    private static void NameSetPieces(List<Landmark> found, HashSet<int> renamed, MapConfig cfg,
        ProvinceMap provinces, int[] order, int baronyCount, int riverCount, List<Title> empires,
        CultureMap cultures, Dictionary<int, string> waterNames, Rng rng)
    {
        var sources = Sources(cfg, provinces).ToList();
        if (sources.Count == 0) return;

        var byId = new int[provinces.Count + 1];
        for (int label = 0; label < provinces.Count; label++)
            byId[order[label]] = label;
        var baronies = Titles.Flatten(empires).Where(t => t.Tier == "b" && t.ProvinceId > 0)
            .ToDictionary(t => t.ProvinceId);
        var used = new HashSet<string>(waterNames.Values, StringComparer.OrdinalIgnoreCase);

        bool Inside(Func<double, double, bool> contains, int id)
        {
            var seed = provinces.Seeds[byId[id]];
            return contains((seed.X + 0.5) / provinces.Width, (seed.Y + 0.5) / provinces.Height);
        }

        foreach (var (kind, contains, label) in sources)
        {
            var members = Enumerable.Range(1, baronyCount).Where(id => Inside(contains, id)).ToArray();
            if (members.Length == 0) continue;

            var culture = members.Where(baronies.ContainsKey)
                .GroupBy(id => cultures.For(baronies[id]))
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Name, StringComparer.Ordinal)
                .Select(g => g.Key).FirstOrDefault();
            if (culture is null) continue;

            string word = UniqueFrom(() => culture.Tongue.Word(rng), used);
            string name = kind switch
            {
                "basin" => $"{word} Basin",
                "sea" => $"{word} Sea",
                "scar" => $"{word} Scar",
                "rift" => $"{word} Rift",
                "range" => $"{word} Mountains",
                "wall" => $"{word} Wall",
                _ => word,
            };
            found.Add(new Landmark($"gen_landmark_{found.Count}_{kind}", kind, name, members, label));

            // The water a drowned crater holds takes its name, so hovering the sea says what it is.
            if (kind == "sea")
                for (int id = riverCount + 1; id <= provinces.Count; id++)
                    if (Inside(contains, id)) { waterNames[id] = name; renamed.Add(id); }
        }
    }

    /// <summary>How many seas the flat map names at most: the largest, not every body of water.</summary>
    private const int MaxSeaNames = 4;

    /// <summary>
    /// The least open water, as a share of the map's height from the clearest point to the nearest
    /// coast, that a sea needs to be named: enough to set a name in without it touching land. Bays
    /// and straits fall under it, and so do lakes.
    /// </summary>
    private const double MinSeaClearance = 0.04;

    /// <summary>How far apart, as a share of the map's height, two sea names must stand.</summary>
    private const double SeaNameSpacing = 0.32;

    /// <summary>
    /// The largest seas, as landmarks with no land: lettered on the flat map, and nothing more. The
    /// names are the ones <see cref="WaterNaming"/> already gave each body of sea zones. "Largest"
    /// is measured as room to letter — how far the clearest water in a body lies from any coast or
    /// the map's frame — which ranks open seas above long thin ones and bays of the same area.
    /// Bodies the set-piece renamed are left to the set-piece's own name.
    /// </summary>
    private static void AddSeas(List<Landmark> found, HashSet<int> renamed, ProvinceMap provinces, int[] order,
        IReadOnlyList<(string Name, List<int> Zones)> seas)
    {
        int width = provinces.Width, height = provinces.Height;

        // A coarse grid is plenty for placing a few names, and keeps the distance pass cheap.
        int q = Math.Max(2, height / 512);
        int gw = (width + q - 1) / q, gh = (height + q - 1) / q;
        int margin = (int)Math.Ceiling(0.035 * height / q);

        var zoneBody = new Dictionary<int, int>();
        for (int b = 0; b < seas.Count; b++)
            foreach (int zone in seas[b].Zones) zoneBody[zone] = b;

        var body = new int[gw * gh];
        var dist = new float[gw * gh];
        for (int cy = 0; cy < gh; cy++)
            for (int cx = 0; cx < gw; cx++)
            {
                int c = cy * gw + cx;
                int px = Math.Min(width - 1, cx * q + q / 2), py = Math.Min(height - 1, cy * q + q / 2);
                int label = provinces.Label[py * width + px];
                bool water = label >= 0 && label < provinces.Seeds.Count
                             && provinces.Seeds[label] is { IsLand: false, IsMajorRiver: false };
                bool edge = cx < margin || cy < margin || cx >= gw - margin || cy >= gh - margin;
                body[c] = water ? zoneBody.GetValueOrDefault(order[label], -1) : -1;
                dist[c] = water && !edge ? float.MaxValue : 0;
            }
        Chamfer(dist, gw, gh);

        // Each body's clearest cell.
        var best = new (int Cell, float Clearance)[seas.Count];
        for (int c = 0; c < body.Length; c++)
            if (body[c] >= 0 && dist[c] > best[body[c]].Clearance) best[body[c]] = (c, dist[c]);

        var placed = new List<(double X, double Y)>();
        foreach (int b in Enumerable.Range(0, seas.Count)
                     .OrderByDescending(b => best[b].Clearance).ThenBy(b => seas[b].Name, StringComparer.Ordinal))
        {
            if (placed.Count >= MaxSeaNames) break;
            double clearance = best[b].Clearance * q;
            if (clearance < MinSeaClearance * height) break;
            if (seas[b].Zones.Any(renamed.Contains)) continue;

            double x = (best[b].Cell % gw + 0.5) * q, y = (best[b].Cell / gw + 0.5) * q;
            if (placed.Any(p => Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)) < SeaNameSpacing * height)) continue;
            placed.Add((x, y));

            // A shallow arch across the clearest water, well inside the coast on either side.
            double half = 0.85 * clearance;
            var line = Enumerable.Range(0, 17)
                .Select(i => i / 8.0 - 1)
                .Select(u => ((x + u * half) / width, (y - 0.1 * clearance * (1 - u * u)) / height))
                .ToArray();
            found.Add(new Landmark($"gen_landmark_{found.Count}_water", "water", seas[b].Name, [], line));
        }
    }

    /// <summary>Two-pass chamfer distance, in cells, from every zero cell.</summary>
    private static void Chamfer(float[] d, int w, int h)
    {
        const float s = 1f, g = 1.41421356f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (d[i] == 0) continue;
                float v = d[i];
                if (x > 0) v = Math.Min(v, d[i - 1] + s);
                if (y > 0) v = Math.Min(v, d[i - w] + s);
                if (x > 0 && y > 0) v = Math.Min(v, d[i - w - 1] + g);
                if (x < w - 1 && y > 0) v = Math.Min(v, d[i - w + 1] + g);
                d[i] = v;
            }
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                if (d[i] == 0) continue;
                float v = d[i];
                if (x < w - 1) v = Math.Min(v, d[i + 1] + s);
                if (y < h - 1) v = Math.Min(v, d[i + w] + s);
                if (x < w - 1 && y < h - 1) v = Math.Min(v, d[i + w + 1] + g);
                if (x > 0 && y < h - 1) v = Math.Min(v, d[i + w - 1] + g);
                d[i] = v;
            }
    }

    /// <summary>
    /// What there is to name, before it is named. The set-piece is looked up by its map type and
    /// redrawn from its seed: the same shapes the heightmap was painted with.
    /// </summary>
    private static IEnumerable<(string Kind, Func<double, double, bool> Contains, (double X, double Y)[] Label)> Sources(
        MapConfig cfg, ProvinceMap provinces)
    {
        string[] parts = (cfg.SetPiece ?? "").Split('@');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int seed)) yield break;
        var feature = AppGUI.QuickCatalogue.FeatureOf(parts[0]);
        double aspect = (double)provinces.Width / provinces.Height;
        foreach (var source in AppGUI.QuickFeatures.Landmarks(feature, seed, aspect))
            yield return source;
    }
}
