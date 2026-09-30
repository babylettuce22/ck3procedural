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
    /// <summary>
    /// Where things are, for the pins a watching screen puts on its map: provinces and counties as
    /// fractions of the map's width and height. Read off the partition's seeds, because the point a
    /// province grew from is always inside it, where a centroid need not be.
    /// </summary>
    public sealed class Places
    {
        private readonly ProvinceMap _map;
        private readonly int[] _labelOf;

        public Places(ProvinceMap map, int[] order)
        {
            _map = map;
            _labelOf = new int[order.Length == 0 ? 1 : Math.Max(1, order.Max() + 1)];
            Array.Fill(_labelOf, -1);
            for (int label = 0; label < order.Length; label++)
                if (order[label] >= 0) _labelOf[order[label]] = label;
        }

        /// <summary>A province's seed, by province id.</summary>
        public (float X, float Y)? Province(int id)
        {
            if (id < 0 || id >= _labelOf.Length) return null;
            int label = _labelOf[id];
            if (label < 0 || label >= _map.Count) return null;

            var seed = _map.Seeds[label];
            return ((seed.X + 0.5f) / _map.Width, (seed.Y + 0.5f) / _map.Height);
        }

        /// <summary>A county's seat.</summary>
        public (float X, float Y)? County(Title? county)
            => county?.Capital is { ProvinceId: > 0 } seat ? Province(seat.ProvinceId) : null;

        /// <summary>
        /// The middle of a spread of counties, pinned to a real seat: the seat nearest their mean,
        /// wilderness left out — the place the map modes put a faith's or a people's badge.
        /// </summary>
        public (float X, float Y)? Heart(IEnumerable<Title> counties, WildernessMap? wilderness)
        {
            var seats = counties.Where(c => wilderness?.Contains(c) != true)
                .Select(County).OfType<(float X, float Y)>().ToList();
            if (seats.Count == 0) return null;

            float mx = seats.Average(p => p.X), my = seats.Average(p => p.Y);
            return seats.MinBy(p => (p.X - mx) * (p.X - mx) + (p.Y - my) * (p.Y - my) * 0.25f);
        }
    }

    /// <summary>
    /// Every county in the colour <paramref name="colourOf"/> gives it, wilderness in its usual tint:
    /// the whole-map picture of a layer, drawn the way the World workspace's map modes draw it.
    /// </summary>
    public static AppGUI.PreviewRenderer.Image CountyPicture(ProvinceMap provinces, int[] order, int baronyCount,
        int landCount, List<Title> empires, WildernessMap? wilderness, Func<Title, (byte R, byte G, byte B)?> colourOf,
        (byte R, byte G, byte B)? wildColour = null)
        => AppGUI.PreviewRenderer.RenderByCounty(
            new AppGUI.PreviewRenderer.ProvinceRaster(provinces.Width, provinces.Height, i => order[provinces.Label[i]],
                baronyCount, landCount, empires, wilderness is null ? null : wilderness.Contains),
            colourOf, wildColour);

    /// <summary>The peoples' map: every county in its culture's colour.</summary>
    public static AppGUI.PreviewRenderer.Image CulturePicture(CultureMap cultures, ProvinceMap provinces, int[] order,
        int baronyCount, int landCount, List<Title> empires, WildernessMap? wilderness)
        => CountyPicture(provinces, order, baronyCount, landCount, empires, wilderness,
            county => cultures.ByCounty.TryGetValue(county, out var culture) ? culture.Color : null);

    /// <summary>The faiths' map: every county in its faith's colour.</summary>
    public static AppGUI.PreviewRenderer.Image FaithPicture(FaithMap faiths, ProvinceMap provinces, int[] order,
        int baronyCount, int landCount, List<Title> empires, WildernessMap? wilderness)
        => CountyPicture(provinces, order, baronyCount, landCount, empires, wilderness,
            county => faiths.ByCounty.TryGetValue(county, out var faith)
                ? (ToByte(faith.Color.R), ToByte(faith.Color.G), ToByte(faith.Color.B))
                : null);

    /// <summary>
    /// The sees' map: every county inside a clerical region in the colour of the rite it keeps (its
    /// faith's own colour for the main rite), each see a shade lighter or darker than its neighbours
    /// in the list so the regions read apart. Counties outside every see are left in parchment.
    /// </summary>
    public static AppGUI.PreviewRenderer.Image SeePicture(FaithMap faiths, ProvinceMap provinces, int[] order,
        int baronyCount, int landCount, List<Title> empires, WildernessMap? wilderness)
    {
        float[] shades = [1.0f, 0.78f, 1.18f, 0.9f];
        var colourOf = new Dictionary<Title, (byte R, byte G, byte B)>();
        foreach (var faith in faiths.Faiths)
        {
            var riteOf = new Dictionary<Title, Rite>();
            foreach (var rite in faith.Rites)
                foreach (var county in rite.Counties) riteOf[county] = rite;

            for (int i = 0; i < faith.Sees.Count; i++)
            {
                var see = faith.Sees[i];
                foreach (var county in see.Counties)
                {
                    var (r, g, b) = riteOf.TryGetValue(county, out var rite) ? rite.Color
                        : see.Rite?.Color ?? faith.Color;
                    float s = shades[i % shades.Length];
                    colourOf[county] = (ToByte(Math.Min(1, r * s)), ToByte(Math.Min(1, g * s)), ToByte(Math.Min(1, b * s)));
                }
            }
        }

        // Land outside every see, wilderness included, in plain parchment: the usual wilderness gold
        // would read as one more see beside a gold faith's.
        return CountyPicture(provinces, order, baronyCount, landCount, empires, wilderness,
            county => colourOf.TryGetValue(county, out var c) ? c : null, ((byte)196, (byte)188, (byte)168));
    }

    /// <summary>
    /// The faiths with the most sees, each shown by its primate see with its great sees listed, then
    /// the largest regional rites with what sets them apart. Nothing when no faith has sees.
    /// </summary>
    public static IEnumerable<ShowcaseItem> Sees(FaithMap faiths, Places? places = null,
        WildernessMap? wilderness = null)
    {
        var organised = faiths.Faiths
            .Where(f => f.Sees.Count > 0)
            .OrderByDescending(f => f.Sees.Count)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .Take(3);

        foreach (var faith in organised)
        {
            var words = faith.Religion.SeeWords;
            var primate = faith.Sees.FirstOrDefault(s => s.Rank == SeeRank.Primate)
                          ?? faith.Sees.MaxBy(s => s.Counties.Count)!;
            string word = primate.Rank == SeeRank.Primate ? words?.Primacy ?? "Primacy" : words?.See ?? "See";
            var (r, g, b) = faith.Color;
            yield return new ShowcaseItem
            {
                Kind = "See",
                Title = $"{word} of {primate.Seat.Name}",
                Subtitle = $"{faith.Name} · {Plural(faith.Sees.Count, "see")}"
                         + (faith.Rites.Count > 0 ? $" · {faith.Rites.Count + 1} rites" : ""),
                Chips = faith.Sees.Where(s => s != primate)
                    .OrderByDescending(s => s.Rank)
                    .ThenByDescending(s => s.Counties.Count)
                    .Take(4)
                    .Select(s => s.Rank == SeeRank.Great
                        ? $"{words?.GreatSee ?? "Great See"} of {s.Seat.Name}"
                        : $"{words?.See ?? "See"} of {s.Seat.Name}")
                    .ToList(),
                Color = (ToByte(r), ToByte(g), ToByte(b)),
                MapAt = places?.County(primate.Seat),
                PinGlyph = nameof(WonderArchetype.Sanctuary),
            };
        }

        var rites = faiths.Faiths.SelectMany(f => f.Rites)
            .OrderByDescending(r => r.Counties.Count)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .Take(3);

        foreach (var rite in rites)
        {
            var keeps = rite.Tenets.Except(rite.Faith.Tenets).Select(Readable)
                .Concat(rite.DoctrineOverrides.Values.Select(Readable))
                .Where(s => s.Length > 0)
                .ToList();
            var (r, g, b) = rite.Color;
            yield return new ShowcaseItem
            {
                Kind = "Rite",
                Title = rite.Name,
                Subtitle = $"Of the {rite.Faith.Name} · founded at {rite.Founder.Seat.Name} · {rite.Counties.Count} counties",
                Chips = keeps,
                Color = (ToByte(r), ToByte(g), ToByte(b)),
                MapAt = places?.Heart(rite.Counties, wilderness),
            };
        }
    }

    /// <summary>The largest realms on the map, named, with a crown on the greatest one's seat.</summary>
    public static IEnumerable<ShowcaseItem> Realms(RealmMap realms, Places? places = null)
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
            MapAt = places?.County(realms.HolderCounty.GetValueOrDefault(greatest[0])),
            PinGlyph = nameof(WonderArchetype.ImperialPalace),
        };
    }

    /// <summary>The world's wonders, with the game's building art.</summary>
    public static IEnumerable<ShowcaseItem> Wonders(WorldCenterMap centers, string gameDir, Places? places = null)
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
                MapAt = places?.Province(center.CapitalBarony.ProvinceId),
                PinGlyph = wonder.Archetype.ToString(),
            };
        }
    }

    /// <summary>
    /// The largest peoples: their colour, their heritage, and names in their language. Pinned at
    /// their heartland, wearing their race's badge when they are not human.
    /// </summary>
    public static IEnumerable<ShowcaseItem> Cultures(CultureMap cultures, Places? places = null,
        EthnicityMap? ethnicities = null, WildernessMap? wilderness = null)
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
                MapAt = places?.Heart(culture.Counties, wilderness),
                PinImages = ethnicities is not null
                            && AppGUI.PreviewRenderer.PhenotypeIconPath(ethnicities.For(culture).Archetype) is { } badge
                    ? [badge]
                    : [],
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
    public static IEnumerable<ShowcaseItem> Faiths(FaithMap faiths, string modDir, string gameDir,
        Places? places = null, WildernessMap? wilderness = null)
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
                MapAt = places?.Heart(faith.Counties, wilderness),
            };
        }
    }

    /// <summary>A few of the names the seas, lakes and great rivers were given; an anchor on the first.</summary>
    public static IEnumerable<ShowcaseItem> Waters(IReadOnlyDictionary<int, string> names, Places? places = null)
    {
        var named = names
            .OrderBy(kv => kv.Key)
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToList();
        var distinct = named.Select(kv => kv.Value).Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0) yield break;

        yield return new ShowcaseItem
        {
            Kind = "Waters",
            Title = distinct[0],
            Subtitle = $"{distinct.Count} seas, lakes and rivers were named",
            Chips = distinct.Skip(1).Take(8).ToList(),
            MapAt = places?.Province(named[0].Key),
            PinGlyph = nameof(WonderArchetype.GreatHarbor),
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

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

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
