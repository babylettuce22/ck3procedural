using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;
using SixLabors.ImageSharp.PixelFormats;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Presentation data for an opened mod, shaped the way the generator's own views are shaped so the
/// main window drives both through one set of code paths: <see cref="Title"/> shells for the tree
/// and the map picks, a <see cref="RealmGraph"/> read back from the title history for the Realms
/// drill-down and the inspectors' de facto fields, and a <see cref="PreviewRenderer.ProvinceRaster"/>
/// so every political view paints with the generator's borders, water and palette. Edits always go
/// to the file-backed entries, never to a content writer.
/// </summary>
public sealed class LoadedWorldView
{
    public LoadedWorld World { get; }
    public WorldRaster Raster { get; }
    public List<Title> Roots { get; } = [];
    public Dictionary<string, Title> Titles { get; } = [];
    public RealmGraph? Realm { get; }

    /// <summary>The downsampling the rendered bitmaps use, so a click scales back to the raster.</summary>
    public int Step => PreviewRenderer.StepFor(Raster.Width);

    private readonly Dictionary<(string Kind, string Key), WorldEntry> _entries;
    private readonly Dictionary<string, Title> _seatByCharacter = [];
    private readonly PreviewRenderer.ProvinceRaster _provinces;

    public LoadedWorldView(LoadedWorld world)
    {
        World = world;
        Raster = WorldRaster.Read(world);
        _entries = world.Entries.ToDictionary(e => (e.Kind, e.Key));

        var perTier = new Dictionary<string, int>();
        foreach (var entry in world.Titles.Values)
        {
            string tier = entry.Key[..1];
            var title = new Title
            {
                Tier = tier, Index = perTier.GetValueOrDefault(tier), Key = entry.Key, Name = entry.Name,
                ProvinceId = entry.ProvinceId > 0 ? entry.ProvinceId : -1, Color = Rgb(entry),
            };
            perTier[tier] = title.Index + 1;
            Titles.Add(entry.Key, title);
            if (entry.Parent is { } parent)
            {
                title.Parent = Titles[parent.Key];
                title.Parent.Children.Add(title);
            }
            else Roots.Add(title);
        }

        Realm = BuildRealm();

        int baronyCount = world.Baronies.Keys.DefaultIfEmpty(0).Max();
        int landCount = Math.Max(baronyCount, ImpassableMax(world));
        _provinces = new(Raster.Width, Raster.Height, i => Raster.Ids[i], baronyCount, landCount, Roots, IsWild);
    }

    /// <summary>
    /// The de facto structure, from the title history: every held title mapped to its holder's
    /// seat county, and every ruler's primary title to the title they answer to. A ruler's seat
    /// is the capital of their primary title when they hold it themselves, else the first county
    /// they hold; a holder with no county at all is left out, the way the generator never writes.
    /// </summary>
    private RealmGraph? BuildRealm()
    {
        if (World.Holders.Count == 0) return null;
        var held = new Dictionary<string, List<Title>>();
        foreach (var (key, holder) in World.Holders)
            if (Titles.TryGetValue(key, out var title))
                (held.TryGetValue(holder, out var list) ? list : held[holder] = []).Add(title);

        var holderCounty = new Dictionary<Title, Title>();
        var liege = new Dictionary<Title, Title>();
        foreach (var (character, titles) in held)
        {
            var primary = titles.OrderByDescending(Emit.HistoryWriter.Rank).First();
            var seat = titles.FirstOrDefault(t => t.Tier == "c");
            for (var capital = primary; capital is not null && capital.Tier != "c";)
            {
                string key = World.Titles[capital.Key].Value("capital");
                capital = Titles.GetValueOrDefault(key);
                if (capital is { Tier: "c" } && titles.Contains(capital)) seat = capital;
            }
            if (seat is null) continue;

            _seatByCharacter[character] = seat;
            foreach (var title in titles) holderCounty[title] = seat;
            string? lord = titles.Select(t => World.Lieges.GetValueOrDefault(t.Key)).FirstOrDefault(l => l is not null);
            if (lord is not null && Titles.TryGetValue(lord, out var lordTitle)) liege[primary] = lordTitle;
        }
        return RealmGraph.From(new RealmMap { HolderCounty = holderCounty, Liege = liege }, Counties);
    }

    private IEnumerable<Title> Counties => MapGen.Titles.Flatten(Roots).Where(t => t.Tier == "c");

    /// <summary>The highest impassable province id, from default.map, so the renderer greys it rather than drowning it.</summary>
    private static int ImpassableMax(LoadedWorld world)
    {
        string path = Path.Combine(world.DirectoryPath, "map_data", "default.map");
        if (!File.Exists(path)) return 0;
        int max = 0;
        foreach (Match m in Regex.Matches(File.ReadAllText(path), @"impassable_(?:mountains|seas)\s*=\s*(?:RANGE|LIST)\s*\{([^}]*)\}"))
            foreach (string token in m.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(token, out int id)) max = Math.Max(max, id);
        return max;
    }

    public bool IsWild(Title county)
        => World.Governments.GetValueOrDefault(county.Key) == LoadedWorld.WildernessGovernment
           || Realm?.SeatOf(county) is { } seat && World.Governments.GetValueOrDefault(Realm.Primary(seat).Key) == LoadedWorld.WildernessGovernment;

    public string? GovernmentOf(Title title)
    {
        if (World.Governments.TryGetValue(title.Key, out string? own)) return own;
        return Realm?.SeatOf(title) is { } seat ? World.Governments.GetValueOrDefault(Realm.Primary(seat).Key) : null;
    }

    public bool Available(string mode) => mode is "Provinces" or "Counties" or "Duchies" or "Kingdoms" or "Empires"
        or "Realms" or "Dynasties" or "Cultures" or "Faiths" or "Government" or "Wilderness" or "Development";

    // --- Families, arms, names -------------------------------------------------------------------

    public WorldEntry? HouseOf(WorldEntry character) => World.Houses.GetValueOrDefault(character.Value("dynasty_house"));
    public WorldEntry? DynastyOf(WorldEntry house) => World.Dynasties.GetValueOrDefault(house.Value("dynasty"));
    public WorldEntry? DynastyOfCharacter(WorldEntry character) => HouseOf(character) is { } house ? DynastyOf(house) : null;
    public WorldEntry? CoatOf(WorldEntry owner) => World.Coats.GetValueOrDefault(owner.Key);

    /// <summary>The dynasty holding a county directly, for the Dynasties view.</summary>
    public WorldEntry? DynastyAt(Title county)
        => Realm is { } realm && HolderOf(realm.Primary(realm.SeatOfCounty(county))) is { } holder ? DynastyOfCharacter(holder) : null;

    public int DevelopmentOf(Title county) => int.TryParse(Entry(county).Value("Development"), out int level) ? level : 0;

    /// <summary>The holy sites a faith lists, as the site entries whose county can be moved.</summary>
    public IReadOnlyList<WorldEntry> HolySitesOf(WorldEntry faith)
        => faith.Fields.Where(f => f.Name == "holy_site" || f.Name.StartsWith("holy_site "))
            .Select(f => World.HolySites.GetValueOrDefault(f.Read())).OfType<WorldEntry>().ToList();

    private IReadOnlyList<string> NamesFor(WorldEntry character)
        => World.Names(character.Value("culture"), character.Value("female") == "yes");

    public bool CanReroll(WorldEntry character) => character.Kind == "Character" && NamesFor(character).Count > 0;

    /// <summary>A new given name from the mod's own name list for the character's culture and sex.</summary>
    public void RerollName(WorldEntry character)
    {
        var names = NamesFor(character);
        if (names.Count == 0 || character.Field("Name") is not { Write: { } write }) return;
        write(names[Random.Shared.Next(names.Count)]);
    }

    /// <summary>Names and colours on the shells follow the files after every edit.</summary>
    public void RefreshShells()
    {
        foreach (var (key, title) in Titles)
        {
            var entry = World.Titles[key];
            title.Name = entry.Name;
            title.Color = Rgb(entry);
        }
    }

    // --- Lookups ---------------------------------------------------------------------------------

    public WorldEntry Entry(Title title) => World.Titles[title.Key];
    public Title Shell(WorldEntry entry) => Titles[entry.Key];

    /// <summary>What the main window's Inspect receives — generator-shaped Title shells from the tree and the map, or entries — as file-backed entries.</summary>
    public IReadOnlyList<WorldEntry> EntriesOf(IReadOnlyList<object> targets)
        => targets.Select(t => t is Title title ? World.Titles.GetValueOrDefault(title.Key) : t as WorldEntry).OfType<WorldEntry>().ToList();

    public Title? BaronyAt(Point pixel)
    {
        int x = pixel.X * Step, y = pixel.Y * Step;
        if (x < 0 || y < 0 || x >= Raster.Width || y >= Raster.Height) return null;
        return World.Baronies.TryGetValue(Raster.Ids[y * Raster.Width + x], out var entry) ? Titles[entry.Key] : null;
    }

    public WorldEntry? ProvinceOf(Title title)
    {
        var barony = title.Tier == "b" ? title : MapGen.Titles.Flatten([title]).FirstOrDefault(t => t.Tier == "b");
        return barony is null ? null : World.Provinces.GetValueOrDefault(barony.ProvinceId);
    }

    public WorldEntry? CultureOf(Title title) => _entries.GetValueOrDefault(("Culture", ProvinceOf(title)?.Value("culture") ?? ""));
    public WorldEntry? FaithOf(Title title) => _entries.GetValueOrDefault(("Faith", ProvinceOf(title)?.Value("religion") ?? ""));

    /// <summary>The character holding a title at the start date, as the inspector reaches them.</summary>
    public WorldEntry? HolderOf(Title title)
        => World.Characters.GetValueOrDefault(World.Holders.GetValueOrDefault(title.Key, ""));

    public Title? SeatOfCharacter(WorldEntry character) => _seatByCharacter.GetValueOrDefault(character.Key);
    public Title? PrimaryOf(WorldEntry character)
        => SeatOfCharacter(character) is { } seat && Realm is { } realm ? realm.Primary(seat) : null;

    public WorldEntry? Related(WorldEntry selected, string kind)
    {
        var title = selected.Kind == "Title" ? Titles[selected.Key] : selected.Kind == "Character" ? PrimaryOf(selected) : null;
        return kind switch
        {
            "Ruler" => title is null ? null : HolderOf(title),
            "Title" => title is null ? null : Entry(title),
            "Culture" => title is not null ? CultureOf(title)
                : _entries.GetValueOrDefault(("Culture", selected.Value("culture"))),
            "Faith" => title is not null ? FaithOf(title)
                : _entries.GetValueOrDefault(("Faith", selected.Value("religion"))),
            _ => null
        };
    }

    /// <summary>
    /// What the game will call this title and its holder, decided the way the engine decides it:
    /// the title's own word if one was written, else the top liege's county culture's word for the
    /// holder's government, else vanilla's rules. Mirrors <see cref="RealmStyle.Describe"/>.
    /// </summary>
    public string RendersAs(Title title)
    {
        if (title.Tier is not ("e" or "k" or "d")) return "—";
        if (World.TitleWords(title.Key, title.Tier) is { } own)
            return $"{own.Realm} of {title.Name} — {Styled(own.Male, own.Female)}";
        if (Realm is not { } realm || realm.SeatOf(title) is not { } seat) return "(vanilla words — no holder)";
        var top = realm.PathFromTop(seat)[0];
        string culture = ProvinceOf(top)?.Value("culture") ?? "";
        string government = GovernmentOf(title) ?? MapGen.GovernmentMap.Feudal;
        return World.CultureWords(culture, government, title.Tier) is { } words
            ? $"{words.Realm} of {title.Name} — {Styled(words.Male, words.Female)}"
            : "(vanilla words, by government)";

        string Styled(string male, string female)
        {
            bool isFemale = HolderOf(title)?.Value("female") == "yes";
            return isFemale ? (female.Length > 0 ? female : male) : (male.Length > 0 ? male : female);
        }
    }

    // --- Edits that span entries --------------------------------------------------------------------

    /// <summary>A province field — culture, religion, holding — on every barony under a title.</summary>
    public void SetProvinceField(Title title, string key, string value)
    {
        foreach (var barony in MapGen.Titles.Flatten([title]).Where(t => t.Tier == "b"))
            if (World.Provinces.GetValueOrDefault(barony.ProvinceId)?.Field(key) is { Write: { } write }) write(value);
    }

    /// <summary>The generator's own child-colour spread, written into every descendant's colour field.</summary>
    public void RecolorChildren(Title title)
    {
        MapGen.Titles.RecolorChildren(title, new Rng(Random.Shared.Next(1, int.MaxValue)));
        foreach (var child in MapGen.Titles.Flatten([title]).Where(t => t != title))
            if (Entry(child).Field("color") is { Write: { } write, ColorScale: 255 })
                write($"{child.Color.R} {child.Color.G} {child.Color.B}");
        RefreshShells();
    }

    /// <summary>Every field of these entries back to the last save, with the provinces a title carries.</summary>
    public void Revert(IEnumerable<WorldEntry> entries)
    {
        foreach (var entry in entries)
        {
            foreach (var field in entry.Fields) field.Reset?.Invoke();
            if (entry.Kind != "Title") continue;
            foreach (var barony in MapGen.Titles.Flatten([Titles[entry.Key]]).Where(t => t.Tier == "b"))
                foreach (var field in World.Provinces.GetValueOrDefault(barony.ProvinceId)?.Fields ?? [])
                    field.Reset?.Invoke();
        }
        RefreshShells();
    }

    // --- Rendering --------------------------------------------------------------------------------

    public Bitmap Render(string mode) => PreviewRenderer.ToBitmap(mode switch
    {
        "Provinces" => PreviewRenderer.RenderTitles(_provinces, "b"),
        "Duchies" => PreviewRenderer.RenderTitles(_provinces, "d"),
        "Kingdoms" => PreviewRenderer.RenderTitles(_provinces, "k"),
        "Empires" => PreviewRenderer.RenderTitles(_provinces, "e"),
        "Realms" => PreviewRenderer.RenderRealms(_provinces, Realm),
        "Dynasties" => PreviewRenderer.RenderByCounty(_provinces, c => DynastyAt(c) is { } dynasty ? PreviewRenderer.DynastyColour(dynasty.Key) : null),
        "Development" => PreviewRenderer.RenderByCounty(_provinces, c => PreviewRenderer.DevelopmentColour(DevelopmentOf(c))),
        "Cultures" => PreviewRenderer.RenderByCounty(_provinces, c => Rgb(CultureOf(c))),
        "Faiths" => PreviewRenderer.RenderByCounty(_provinces, c => Rgb(FaithOf(c))),
        "Government" => PreviewRenderer.RenderByCounty(_provinces, c => PreviewRenderer.GovernmentColour(GovernmentOf(c) ?? "")),
        "Wilderness" => PreviewRenderer.RenderByCounty(_provinces, _ => ((byte)108, (byte)114, (byte)122)),
        _ => PreviewRenderer.RenderTitles(_provinces, "c"),
    });

    public Bitmap RenderRealmsFocused(Title seat)
        => PreviewRenderer.ToBitmap(PreviewRenderer.RenderRealmsFocused(_provinces, Realm!, seat));

    /// <summary>What is under the cursor, in the generator's own readout shape.</summary>
    public string Probe(string mode, Point pixel)
    {
        if (BaronyAt(pixel) is not { } barony) return "";
        var parts = new List<string>();
        for (var t = barony; t is not null; t = t.Parent)
            if (t.Tier is "b" or "c" or "d" or "k") parts.Add(t.Name);
        string line = string.Join(" · ", parts);
        var county = barony.Parent;
        if (county is null) return line;
        if (mode == "Cultures" && CultureOf(county) is { } culture) return $"{line} · {culture.Name}";
        if (mode == "Faiths" && FaithOf(county) is { } faith) return $"{line} · {faith.Name}";
        // By the legend's name, not the raw key: the hover text and the colour key beside it were
        // calling the same government two different things.
        if (mode == "Government" && GovernmentOf(county) is { } government)
            return $"{line} · {MapGen.GovernmentMap.DisplayName(government)}";
        if (mode == "Dynasties" && DynastyAt(county) is { } dynasty) return $"{line} · dynasty {dynasty.Name}";
        if (mode == "Development") return $"{line} · development {DevelopmentOf(county)}";
        if (Realm is { } realm && !IsWild(county))
        {
            var primary = realm.Primary(realm.SeatOfCounty(county));
            if (HolderOf(primary) is { } holder) return $"{line} · held by {holder.Name}, {TitleInspector.TierName(primary)} {primary.Name}";
        }
        return IsWild(county) ? $"{line} · wilderness" : line;
    }

    // --- Colours ------------------------------------------------------------------------------------

    private static (byte R, byte G, byte B) Rgb(WorldEntry? entry)
    {
        if (entry is null) return (58, 83, 108);
        if (entry.Field("color") is { } field && ParseColor(field) is { } color) return (color.R, color.G, color.B);
        ulong hash = Rng.StableHash(entry.Key);
        return ((byte)(60 + hash % 170), (byte)(60 + (hash >> 8) % 170), (byte)(60 + (hash >> 16) % 170));
    }

    /// <summary>The colour a colour field holds, scaled to bytes; null when it is not three plain numbers.</summary>
    public static Color? ParseColor(WorldField field)
    {
        string[] pieces = field.Read().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length != 3 || !pieces.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _))) return null;
        var c = pieces.Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        double scale = field.ColorScale is 1 ? 255 : 1;
        return Color.FromArgb((int)Math.Clamp(Math.Round(c[0] * scale), 0, 255), (int)Math.Clamp(Math.Round(c[1] * scale), 0, 255), (int)Math.Clamp(Math.Round(c[2] * scale), 0, 255));
    }

    public static string FormatColor(WorldField field, Color color)
        => field.ColorScale is 1
            ? string.Join(" ", new[] { color.R, color.G, color.B }.Select(v => (v / 255.0).ToString("0.###", CultureInfo.InvariantCulture)))
            : $"{color.R} {color.G} {color.B}";
}

/// <summary>
/// The generic editable face of a file-backed entry, for the kinds without a hand-written wrapper:
/// colours get the colour picker, lists the multi-line editor, and references a dropdown of the
/// keys this world defines, the way the generator's inspectors present the same things.
/// </summary>
internal sealed class LoadedEntryProperties(WorldEntry entry) : CustomTypeDescriptor
{
    public override PropertyDescriptorCollection GetProperties() => GetProperties(null);
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
        => new(entry.Fields.Select(f => new FieldProperty(f)).ToArray());
    public override object GetPropertyOwner(PropertyDescriptor? pd) => this;
    public override string ToString() => entry.Name;

    private sealed class FieldProperty(WorldField binding) : PropertyDescriptor(binding.Name,
        [new CategoryAttribute(binding.Category), new DescriptionAttribute(binding.Description)])
    {
        public override Type ComponentType => typeof(LoadedEntryProperties);
        public override Type PropertyType => binding.ColorScale is not null && binding.Write is not null ? typeof(Color)
            : binding.IsList ? typeof(string[]) : typeof(string);
        public override bool IsReadOnly => binding.Write is null;
        public override TypeConverter Converter => binding.Options is { } options && !binding.IsList
            ? new ChoiceConverter(options) : base.Converter;

        // The generator's own picker over the harvested tradition list, once the vocabulary is in.
        public override object? GetEditor(Type editorBaseType)
            => binding.Name == "traditions" && MapGen.VanillaVocabulary.Current is not null
               && editorBaseType == typeof(System.Drawing.Design.UITypeEditor)
                ? new TraditionListEditor() : base.GetEditor(editorBaseType);

        public override object? GetValue(object? component)
        {
            if (PropertyType == typeof(Color)) return LoadedWorldView.ParseColor(binding) ?? Color.Black;
            if (PropertyType == typeof(string[])) return binding.Read().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return binding.Read();
        }

        public override void SetValue(object? component, object? value)
        {
            if (binding.Write is null) return;
            if (value is Color color) binding.Write(LoadedWorldView.FormatColor(binding, color));
            else if (value is string[] list) binding.Write(string.Join(" ", list.Select(s => s.Trim()).Where(s => s.Length > 0)));
            else binding.Write(value?.ToString() ?? "");
        }

        public override bool CanResetValue(object component) => false;
        public override void ResetValue(object component) { }
        public override bool ShouldSerializeValue(object component) => false;
    }
}

/// <summary>A wrapper whose dropdown properties draw their choices from the opened world.</summary>
public interface ILoadedChoices
{
    IReadOnlyList<(string Key, string Label)> Choices(string property);
}

/// <summary>
/// A dropdown of this world's own keys, shown with their names, typed back as keys. Built with
/// the choices for a generic field, or without arguments for a <c>[TypeConverter]</c> attribute
/// on a wrapper that implements <see cref="ILoadedChoices"/>.
/// </summary>
public sealed class ChoiceConverter : StringConverter
{
    private readonly Func<IReadOnlyList<(string Key, string Label)>>? _options;
    public ChoiceConverter() { }
    public ChoiceConverter(Func<IReadOnlyList<(string Key, string Label)>> options) => _options = options;

    private IReadOnlyList<(string Key, string Label)> Options(ITypeDescriptorContext? context)
    {
        if (_options is not null) return _options();
        var instance = context?.Instance is object[] many ? many.FirstOrDefault() : context?.Instance;
        return instance is ILoadedChoices choices && context?.PropertyDescriptor is { } property
            ? choices.Choices(property.Name) : [];
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        => new(Options(context).Select(o => o.Key).ToArray());

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is string key)
        {
            var match = Options(context).FirstOrDefault(o => o.Key == key);
            return match.Key is null || match.Label == key ? key : $"{match.Label}  [{key}]";
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text)
        {
            var m = Regex.Match(text, @"\[([^\]]+)\]\s*$");
            if (m.Success) return m.Groups[1].Value;
            return Options(context).FirstOrDefault(o => o.Label == text.Trim()).Key ?? text.Trim();
        }
        return base.ConvertFrom(context, culture, value);
    }
}

public sealed class WorldRaster(int width, int height, int[] ids)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int[] Ids { get; } = ids;
    public static WorldRaster Read(LoadedWorld world)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(world.ProvinceMapPath);
        int width = image.Width, height = image.Height;
        var ids = new int[checked(width * height)];
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                    ids[y * width + x] = world.ProvinceByRgb.GetValueOrDefault((row[x].R << 16) | (row[x].G << 8) | row[x].B);
            }
        });
        return new(width, height, ids);
    }
}
