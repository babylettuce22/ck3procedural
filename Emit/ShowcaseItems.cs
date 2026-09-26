using System.Globalization;
using System.Text.RegularExpressions;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// What each stage shows to someone watching the run (<see cref="Showcase"/>): a handful of the
/// best things it made, turned into <see cref="ShowcaseItem"/>s.
///
/// Every method here only reads. Nothing takes an <see cref="Rng"/> or calls a method that could
/// draw from one — a language is never asked for a fresh name, only for names a stage already
/// stored — so what a run writes is exactly the same with a listener as without. Choices of
/// "which few" are by size or rank, never by chance, for the same reason.
/// </summary>
internal static class ShowcaseItems
{
    /// <summary>The largest realms on the map, named.</summary>
    public static IEnumerable<ShowcaseItem> Realms(RealmMap realms)
    {
        var greatest = realms.Greatest
            .Where(t => t.Tier is "h" or "e" or "k" && !string.IsNullOrWhiteSpace(t.Name))
            .Take(6)
            .ToList();
        if (greatest.Count == 0) yield break;

        yield return new ShowcaseItem
        {
            Kind = "Great powers",
            Title = greatest[0].Name,
            Subtitle = $"The greatest realm, among {greatest.Count - 1} other great powers",
            Chips = greatest.Skip(1).Select(t => $"{t.Name} · {TierWord(t.Tier)}").ToList(),
        };
    }

    /// <summary>The world's wonders, with the game's building art.</summary>
    public static IEnumerable<ShowcaseItem> Wonders(WorldCenterMap centers, string gameDir)
    {
        foreach (var center in centers.Centers.Take(3))
        {
            var wonder = center.Wonder;
            yield return new ShowcaseItem
            {
                Kind = "Wonder",
                Title = wonder.Name,
                Subtitle = $"In {center.County.Name}",
                Body = Clip(wonder.Description, 170),
                Images = [GamePath(gameDir, wonder.IconTexture)],
            };
        }
    }

    /// <summary>The largest peoples: their colour, their heritage, and names in their language.</summary>
    public static IEnumerable<ShowcaseItem> Cultures(CultureMap cultures)
    {
        var sizes = new Dictionary<Culture, int>();
        foreach (var culture in cultures.ByCounty.Values)
            sizes[culture] = sizes.GetValueOrDefault(culture) + 1;

        var largest = cultures.Cultures
            .Where(c => sizes.ContainsKey(c))
            .OrderByDescending(c => sizes[c])
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .Take(4);

        foreach (var culture in largest)
        {
            string? tongue = string.IsNullOrWhiteSpace(culture.Tongue.Name) ? null : culture.Tongue.Name;
            var names = culture.MaleNames.Take(3).Concat(culture.FemaleNames.Take(2)).ToList();
            var houses = culture.DynastyNames.Take(3).ToList();

            var body = new List<string>();
            if (names.Count > 0) body.Add("Names like " + string.Join(", ", names));
            if (houses.Count > 0) body.Add("houses such as " + string.Join(", ", houses));

            yield return new ShowcaseItem
            {
                Kind = "Culture",
                Title = culture.Name,
                Subtitle = $"{culture.Heritage.Name} heritage" + (tongue is null ? "" : $" · speaks {tongue}")
                         + $" · {sizes[culture]} counties",
                Body = body.Count == 0 ? null : string.Join("; ", body) + ".",
                Chips = new[] { culture.Ethos }.Concat(culture.Traditions.Take(3)).Select(Readable)
                    .Where(s => s.Length > 0).ToList(),
                Color = culture.Color,
            };
        }
    }

    /// <summary>The calendar the world counts its days by.</summary>
    public static IEnumerable<ShowcaseItem> Calendar(WorldCalendar? calendar)
    {
        if (calendar is null) yield break;
        yield return new ShowcaseItem
        {
            Kind = "Calendar",
            Title = calendar.EraName,
            Subtitle = $"Years are counted in the {calendar.EraName} ({calendar.EraShort})",
            Chips = calendar.Months?.ToList() ?? [],
        };
    }

    /// <summary>
    /// The regiments this world's peoples field, elites first, each over the game's own
    /// illustration of its unit type.
    /// </summary>
    public static IEnumerable<ShowcaseItem> Regiments(RetinueMap retinues, string gameDir)
    {
        var picked = retinues.Regiments
            .OrderBy(r => r.IsElite ? 0 : 1)
            .ThenByDescending(r => r.Damage + r.Toughness)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .Take(4);

        foreach (var regiment in picked)
        {
            var stats = new List<string>();
            if (regiment.Damage > 0) stats.Add($"Damage {regiment.Damage}");
            if (regiment.Toughness > 0) stats.Add($"Toughness {regiment.Toughness}");
            if (regiment.Pursuit > 0) stats.Add($"Pursuit {regiment.Pursuit}");
            if (regiment.Screen > 0) stats.Add($"Screen {regiment.Screen}");

            var images = new List<string>();
            if (regiment.Icon is { } icon)
            {
                images.Add(GamePath(gameDir, $"gfx/interface/illustrations/men_at_arms_big/{icon}.dds"));
                images.Add(GamePath(gameDir, $"gfx/interface/icons/regimenttypes/{icon}.dds"));
            }

            yield return new ShowcaseItem
            {
                Kind = regiment.IsElite ? "Elite men-at-arms" : "Men-at-arms",
                Title = regiment.Name,
                Subtitle = $"{Readable(regiment.Archetype)} of the {regiment.Culture.Name}",
                Body = Clip(StripMarkup(regiment.Flavor), 190),
                Chips = stats,
                Images = images,
                Illustration = true,
            };
        }
    }

    /// <summary>
    /// The largest faiths, with the icon just drawn for each. Called once the religion files are on
    /// disk, since that is when a generated icon exists to show.
    /// </summary>
    public static IEnumerable<ShowcaseItem> Faiths(FaithMap faiths, string modDir, string gameDir)
    {
        var largest = faiths.Faiths
            .Where(f => f.Counties.Count > 0)
            .OrderByDescending(f => f.Counties.Count)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .Take(4);

        foreach (var faith in largest)
        {
            string file = faith.Icon.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? faith.Icon : faith.Icon + ".dds";
            var (r, g, b) = faith.Color;
            yield return new ShowcaseItem
            {
                Kind = "Faith",
                Title = faith.Name,
                Subtitle = $"{faith.Religion.Name} · {faith.Counties.Count} counties"
                         + (faith.HolySites.Count > 0 ? $" · {faith.HolySites.Count} holy sites" : ""),
                Chips = faith.Tenets.Take(3).Select(Readable).Where(s => s.Length > 0).ToList(),
                Color = (ToByte(r), ToByte(g), ToByte(b)),
                Images =
                [
                    Path.Combine(modDir, "gfx", "interface", "icons", "faith", file),
                    GamePath(gameDir, "gfx/interface/icons/faith/" + file),
                ],
            };
        }
    }

    /// <summary>A few of the names the seas, lakes and great rivers were given.</summary>
    public static IEnumerable<ShowcaseItem> Waters(IReadOnlyDictionary<int, string> names)
    {
        var distinct = names
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (distinct.Count == 0) yield break;

        yield return new ShowcaseItem
        {
            Kind = "Waters",
            Title = distinct[0],
            Subtitle = $"{distinct.Count} seas, lakes and rivers were named",
            Chips = distinct.Skip(1).Take(8).ToList(),
        };
    }

    /// <summary>
    /// The finest weapons in the world's treasuries, with their icons: a forged weapon's own
    /// drawing from the mod, otherwise the stock art from the game.
    /// </summary>
    public static IEnumerable<ShowcaseItem> Treasures(ArtifactMap artifacts, IReadOnlyList<WeaponAsset> forged,
        string modDir, string gameDir)
    {
        var icons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in WeaponAssets.All) icons[asset.VisualKey] = asset.Icon;
        foreach (var asset in forged) icons[asset.VisualKey] = asset.Icon;

        var finest = artifacts.AllArtifacts
            .Where(a => icons.ContainsKey(a.Visuals))
            .OrderByDescending(a => a.Rarity)
            .ThenByDescending(a => a.Quality)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .Take(4);

        foreach (var artifact in finest)
        {
            string icon = icons[artifact.Visuals];
            yield return new ShowcaseItem
            {
                Kind = "Treasure",
                Title = artifact.LocalizedName,
                Subtitle = $"{artifact.Rarity} {Readable(artifact.Type)} · made in {artifact.CreatedYear}",
                Body = Clip(StripMarkup(artifact.LocalizedDescription), 190),
                Images =
                [
                    Path.Combine(modDir, "gfx", "interface", "icons", "artifact", icon),
                    GamePath(gameDir, "gfx/interface/icons/artifact/" + icon),
                ],
                // Artifact icons are strips of four, one frame per rarity, common first.
                ImageFrames = 4,
                ImageFrame = Math.Clamp((int)artifact.Rarity, 0, 3),
            };
        }
    }

    // ------------------------------------------------------------------ wording

    private static string TierWord(string tier) => tier switch
    {
        "h" => "hegemony",
        "e" => "empire",
        "k" => "kingdom",
        "d" => "duchy",
        _ => "realm",
    };

    private static string GamePath(string gameDir, string relative)
        => Path.Combine(gameDir, relative.Replace('/', Path.DirectorySeparatorChar));

    private static byte ToByte(double channel) => (byte)Math.Clamp((int)Math.Round(channel * 255), 0, 255);

    private static readonly string[] KeyPrefixes = ["ethos_", "tradition_", "tenet_", "doctrine_", "heritage_"];

    /// <summary>A script key read aloud: "tradition_forest_folk" as "Forest folk".</summary>
    private static string Readable(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        string s = key;
        foreach (string prefix in KeyPrefixes)
            if (s.StartsWith(prefix, StringComparison.Ordinal)) { s = s[prefix.Length..]; break; }
        s = s.Replace('_', ' ').Trim();
        return s.Length == 0 ? "" : char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
    }

    /// <summary>CK3 text formatting taken out: "#F …#!" styling and [bracketed] script calls.</summary>
    private static string StripMarkup(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string s = Regex.Replace(text, @"#![ ]?", "");
        s = Regex.Replace(s, @"#[A-Za-z_]+ ", "");
        s = Regex.Replace(s, @"\[[^\]]*\]", "");
        s = s.Replace("\\n", " ").Replace("\n", " ");
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    private static string Clip(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
}
