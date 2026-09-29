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
/// <see cref="Emit.PassWriter.WriteLandmarks"/> gives each a province modifier and a geographical
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
        int riverCount, List<Title> empires, CultureMap cultures, Dictionary<int, string> waterNames, Rng rng)
    {
        var found = new List<Landmark>();
        var sources = Sources(cfg, provinces).ToList();
        if (sources.Count == 0) return found;

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
                    if (Inside(contains, id)) waterNames[id] = name;
        }
        return found;
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
