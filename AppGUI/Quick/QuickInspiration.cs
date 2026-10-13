using System.Text.Json.Serialization;
using Ck3MapGen.Config;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Which corners of the real world a Quick world's invented peoples are modelled on. Two settings
/// sit behind it, and between them they reach most of what makes a people read as from somewhere:
/// <list type="bullet">
/// <item><see cref="MapConfig.CultureAestheticsTheme"/> (or <see cref="MapConfig.CultureThemes"/>
/// for several), the <em>culture</em>: which vanilla looks the dress, buildings, army models and
/// heraldry are drawn from, and which language styles the names are coined in
/// (LanguageFlavour.Pool).</item>
/// <item><see cref="MapConfig.DominantLook"/> (or <see cref="MapConfig.FaceRegions"/> for a set no
/// preset makes), the <em>faces</em>: which vanilla ethnicities the world's humans inherit their
/// colouring and features from.</item>
/// </list>
/// Edited in the People step's inspiration dialog (<see cref="InspirationDialog"/>), mostly through
/// its <see cref="Presets"/>, which set both at once; the two can also be set apart there, and
/// each takes any number of picks — two regions, or every region but one.
///
/// Anywhere for both is not an inspiration at all: the page drops it (<see cref="IsDefault"/>), so a
/// world made without opening the dialog is the world Quick always made. Only invented peoples have
/// one; see <see cref="QuickChoices.InspirationInWorld"/>.
/// </summary>
public sealed class QuickInspiration
{
    /// <summary>The culture themes picked. Empty, or every one of them, is Anywhere.</summary>
    public List<MapConfig.CultureLookTheme> Themes { get; set; } = [];

    /// <summary>The face regions picked. Empty, or every one of them, is Anywhere.</summary>
    public List<MapConfig.FaceRegion> Regions { get; set; } = [];

    /// <summary>Choices saved before multi-select held one theme. Read into <see cref="Themes"/>, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MapConfig.CultureLookTheme Theme
    {
        get => default;
        set { if (value != default) Themes = [value]; }
    }

    /// <summary>Choices saved before multi-select held one face preset. Read into <see cref="Regions"/>, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MapConfig.HumanLook Look
    {
        get => default;
        set { if (value != default) Regions = [.. Ethnicities.Footprint(value).Order()]; }
    }

    public QuickInspiration Clone() => new() { Themes = [.. Themes], Regions = [.. Regions] };

    /// <summary>Every culture theme a player can pick, Anywhere aside.</summary>
    internal static readonly MapConfig.CultureLookTheme[] AllThemes =
        [.. Enum.GetValues<MapConfig.CultureLookTheme>().Where(t => t != MapConfig.CultureLookTheme.VariedGlobal)];

    internal static readonly MapConfig.FaceRegion[] AllRegions = Enum.GetValues<MapConfig.FaceRegion>();

    /// <summary>
    /// The themes in force, in enum order; empty for Anywhere. All seven is Anywhere too: between
    /// them they take every vanilla look but four Japanese ones, and Anywhere takes those as well.
    /// </summary>
    internal List<MapConfig.CultureLookTheme> ThemeSet
    {
        get
        {
            var set = AllThemes.Where(Themes.Contains).ToList();
            return set.Count == AllThemes.Length ? [] : set;
        }
    }

    /// <summary>The face regions in force, in enum order; every region for Anywhere.</summary>
    internal List<MapConfig.FaceRegion> RegionSet
    {
        get
        {
            var set = AllRegions.Where(Regions.Contains).ToList();
            return set.Count == 0 ? [.. AllRegions] : set;
        }
    }

    private bool AnyCulture => ThemeSet.Count == 0;
    private bool AnyFaces => RegionSet.Count == AllRegions.Length;

    /// <summary>Anywhere for both: the generator's own varied world.</summary>
    public bool IsDefault => AnyCulture && AnyFaces;

    /// <summary>
    /// Several themes go to <see cref="MapConfig.CultureThemes"/>, one to the single setting. A face
    /// set that is exactly a preset's footprint is written as that preset, so a pick a tile could
    /// have made makes the tile's world to the seed.
    /// </summary>
    public void ApplyTo(MapConfig cfg)
    {
        var themes = ThemeSet;
        cfg.CultureAestheticsTheme = themes.Count == 0 ? MapConfig.CultureLookTheme.VariedGlobal : themes[0];
        cfg.CultureThemes = themes.Count > 1 ? themes : [];

        var regions = RegionSet;
        if (Ethnicities.PresetFor(regions.ToHashSet()) is { } look)
        {
            cfg.DominantLook = look;
            cfg.FaceRegions = [];
        }
        else
        {
            cfg.DominantLook = MapConfig.HumanLook.Varied;
            cfg.FaceRegions = regions;
        }
    }

    /// <summary>
    /// One of the dialog's tiles: a region, a line on what it brings, and the culture and faces
    /// that make it. Each pairing is the closest the two settings come to one place — the faces a
    /// preset's own vanilla templates, the culture the theme whose wardrobe and tongues are that
    /// region's. Western Europe takes all of Europe's faces because its wardrobe runs from Iberia to
    /// the Slavs; Northern Europe takes the fair and Sámi ones its Norse and Finnic tongues go with.
    /// </summary>
    internal sealed record Preset(string Key, string Name, string Blurb,
        MapConfig.CultureLookTheme Theme, MapConfig.HumanLook Look);

    /// <summary>The tiles, in the dialog's reading order: west to east, north to south.</summary>
    internal static readonly Preset[] Presets =
    [
        new("anywhere", "Anywhere", "Every corner of the medieval world, each people where its climate suits it",
            MapConfig.CultureLookTheme.VariedGlobal, MapConfig.HumanLook.Varied),
        new("northern", "Northern Europe", "Norse and Finnic tongues, dressed for the long winter",
            MapConfig.CultureLookTheme.NorthernNorse, MapConfig.HumanLook.WesternEuropean),
        new("western", "Western Europe", "Frankish, English, Celtic and Iberian peoples",
            MapConfig.CultureLookTheme.WesternEuropean, MapConfig.HumanLook.MixedEuropean),
        new("byzantium", "Byzantium", "Greek, Latin and Slavic tongues around the eastern sea",
            MapConfig.CultureLookTheme.ByzantineGreek, MapConfig.HumanLook.Mediterranean),
        new("middle-east", "Middle East", "Arabic and Persian peoples, from the desert to the highlands",
            MapConfig.CultureLookTheme.MiddleEasternMena, MapConfig.HumanLook.MiddleEastern),
        new("steppe", "The Steppe", "Turkic and Mongol peoples of the open grassland",
            MapConfig.CultureLookTheme.SteppeNomadic, MapConfig.HumanLook.Steppe),
        new("africa", "Sub-Saharan Africa", "Savanna and forest kingdoms, the Sahel and the Horn",
            MapConfig.CultureLookTheme.SubSaharanAfrican, MapConfig.HumanLook.SubSaharan),
        new("asia", "India & East Asia", "From the courts of India to China and Korea",
            MapConfig.CultureLookTheme.IndianEastAsian, MapConfig.HumanLook.MixedAsian),
        new("southeast-asia", "Southeast Asia", "Island and river kingdoms of the monsoon",
            MapConfig.CultureLookTheme.IndianEastAsian, MapConfig.HumanLook.SoutheastAsian),
    ];

    /// <summary>The tile this pairing is, or null for one set by hand.</summary>
    internal Preset? MatchingPreset
    {
        get
        {
            var themes = ThemeSet;
            var regions = RegionSet.ToHashSet();
            return Presets.FirstOrDefault(p =>
                (p.Theme == MapConfig.CultureLookTheme.VariedGlobal ? themes.Count == 0 : themes.SequenceEqual([p.Theme]))
                && Ethnicities.Footprint(p.Look).SetEquals(regions));
        }
    }

    /// <summary>
    /// The inspiration in a few words, for the link, the Invented card and the Review row: the
    /// tile's name, or the two halves of a hand-set pairing — "Norse culture, Mediterranean faces",
    /// "Norse and Steppe culture, faces from anywhere but Sub-Saharan".
    /// </summary>
    public string Describe() => MatchingPreset?.Name ?? $"{DescribeCulture()} culture, {DescribeFaces()}";

    private string DescribeCulture()
        => AnyCulture ? "Varied" : Join(ThemeSet.Select(t => Cultures.First(c => c.Theme == t).Short));

    private string DescribeFaces()
    {
        var regions = RegionSet;
        if (AnyFaces) return "varied faces";
        // Most of the list is said by what it leaves out.
        var left = AllRegions.Except(regions).ToList();
        if (regions.Count > left.Count && left.Count <= 3)
            return "faces from anywhere but " + Join(left.Select(r => Faces.First(f => f.Region == r).Short));
        return Join(regions.Select(r => Faces.First(f => f.Region == r).Short)) + " faces";
    }

    /// <summary>"A", "A and B", "A, B and C".</summary>
    internal static string Join(IEnumerable<string> words)
    {
        var list = words.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }

    // ------------------------------------------------------------------ the two halves

    /// <summary>
    /// One culture theme, as the dialog offers it, with the weather its wardrobe has anything for.
    /// </summary>
    /// <param name="Name">Its chip in the dialog.</param>
    /// <param name="Short">Its word in a hand-set pairing's description.</param>
    /// <param name="Dress">What the peoples wear, for the climate note.</param>
    /// <param name="Unsuited">
    /// Quick climates on which the wardrobe dresses less than half the land it dresses on its best
    /// one. Measured, not judged: the generator's own climate model over three maps (height.png,
    /// the Continents and Pangaea presets) at each Quick climate, and for every land pixel the best
    /// ClothingClimate fit in the theme's filtered pool, counted against the 0.30 keep line. Share of
    /// land dressed, averaged over the maps (Northern / Temperate / Warm / Whole globe):
    /// <code>
    /// Anywhere            74  91  99  90      Middle Eastern      39  48  54  47
    /// Western European    53  59  50  60      Steppe              27  26   2  24
    /// Norse               27  25   5  26      Sub-Saharan         23  39  72  42
    /// Byzantine           23  31  26  28      Indian &amp; E. Asian  51  69  90  70
    /// </code>
    /// Every regional wardrobe dresses a minority of any Quick map — each spans 70° of latitude or
    /// more, and a region is one band of it — so "suits" cannot mean covers the map, or the note
    /// would show for everything and be read for nothing. It means that one pick is far worse than
    /// another the player could make: Norse and Steppe dress on a Warm map, African on a Northern.
    /// </param>
    /// <param name="Lacks">Where that leaves the peoples, for the note: "hot lands".</param>
    /// <param name="Suits">The climate the note offers instead: the theme's best, above.</param>
    internal sealed record CultureOption(MapConfig.CultureLookTheme Theme, string Name, string Short, string Dress,
        QuickClimate[] Unsuited, string Lacks, QuickClimate Suits);

    internal static readonly CultureOption[] Cultures =
    [
        new(MapConfig.CultureLookTheme.VariedGlobal, "Anywhere", "Varied", "", [], "", QuickClimate.Temperate),
        new(MapConfig.CultureLookTheme.NorthernNorse, "Norse", "Norse", "Norse",
            [QuickClimate.Warm], "hot lands", QuickClimate.Northern),
        new(MapConfig.CultureLookTheme.WesternEuropean, "Western European", "Western European", "", [], "", QuickClimate.Temperate),
        new(MapConfig.CultureLookTheme.ByzantineGreek, "Byzantine", "Byzantine", "", [], "", QuickClimate.Temperate),
        new(MapConfig.CultureLookTheme.MiddleEasternMena, "Middle Eastern", "Middle Eastern", "", [], "", QuickClimate.Warm),
        new(MapConfig.CultureLookTheme.SteppeNomadic, "Steppe", "Steppe", "Steppe",
            [QuickClimate.Warm], "hot lands", QuickClimate.Northern),
        new(MapConfig.CultureLookTheme.SubSaharanAfrican, "Sub-Saharan", "Sub-Saharan", "African",
            [QuickClimate.Northern], "cold lands", QuickClimate.Warm),
        new(MapConfig.CultureLookTheme.IndianEastAsian, "Indian & East Asian", "Indian and East Asian", "", [], "", QuickClimate.Warm),
    ];

    /// <summary>
    /// One face region, as the dialog offers it: its chip and its word in a description. The regions
    /// are disjoint (see <see cref="MapConfig.FaceRegion"/>); the old mixed presets — all of Europe,
    /// all of Asia, Mediterranean and African — are now sets of these, which the tiles still make.
    /// </summary>
    internal sealed record FaceOption(MapConfig.FaceRegion Region, string Name, string Short);

    /// <summary>West to east.</summary>
    internal static readonly FaceOption[] Faces =
    [
        new(MapConfig.FaceRegion.WesternEuropean, "Western European", "Western European"),
        new(MapConfig.FaceRegion.Slavic, "Slavic", "Slavic"),
        new(MapConfig.FaceRegion.Mediterranean, "Mediterranean", "Mediterranean"),
        new(MapConfig.FaceRegion.MiddleEastern, "Middle Eastern", "Middle Eastern"),
        new(MapConfig.FaceRegion.Steppe, "Steppe", "Steppe"),
        new(MapConfig.FaceRegion.SubSaharan, "Sub-Saharan", "Sub-Saharan"),
        new(MapConfig.FaceRegion.Indian, "Indian", "Indian"),
        new(MapConfig.FaceRegion.EastAsian, "East Asian", "East Asian"),
        new(MapConfig.FaceRegion.SoutheastAsian, "Southeast Asian", "Southeast Asian"),
    ];

    /// <summary>The picked themes' options; Anywhere's alone for Anywhere.</summary>
    private List<CultureOption> CultureOptions
        => AnyCulture ? [Cultures[0]] : [.. ThemeSet.Select(t => Cultures.First(c => c.Theme == t))];

    /// <summary>
    /// Whether the culture's wardrobe has little for much of a climate's map: every picked theme's,
    /// since one that dresses the climate is enough to dress the peoples who live in it.
    /// </summary>
    public bool Unsuits(QuickClimate climate) => CultureOptions.All(o => o.Unsuited.Contains(climate));

    /// <summary>The wardrobe, for the climate note: "Norse dress", "Norse and Steppe dress".</summary>
    internal string Dress => Join(CultureOptions.Select(o => o.Dress).Where(d => d.Length > 0).Distinct()) + " dress";

    /// <summary>What an unsuited climate leaves the peoples without; the picks that unsuit it share it.</summary>
    internal string Lacks => CultureOptions[0].Lacks;

    /// <summary>The climate the note offers instead: the first pick's best.</summary>
    internal QuickClimate Suits => CultureOptions[0].Suits;

    /// <summary>The climates this culture's wardrobe covers, for a surprise to draw from.</summary>
    public QuickClimate[] SuitedClimates => [.. Enum.GetValues<QuickClimate>().Where(c => !Unsuits(c))];
}
