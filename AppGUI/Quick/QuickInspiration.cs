using Ck3MapGen.Config;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Which corner of the real world a Quick world's invented peoples are modelled on. Two settings
/// sit behind it, and between them they reach most of what makes a people read as from somewhere:
/// <list type="bullet">
/// <item><see cref="MapConfig.CultureAestheticsTheme"/>, the <em>culture</em>: which vanilla looks
/// the dress, buildings, army models and heraldry are drawn from, and which language styles the
/// names are coined in (LanguageFlavour.Pool).</item>
/// <item><see cref="MapConfig.DominantLook"/>, the <em>faces</em>: which vanilla ethnicities the
/// world's humans inherit their colouring and features from.</item>
/// </list>
/// Edited in the People step's inspiration dialog (<see cref="InspirationDialog"/>), mostly through
/// its <see cref="Presets"/>, which set both at once; the two can also be set apart there.
///
/// Anywhere for both is not an inspiration at all: the page drops it (<see cref="IsDefault"/>), so a
/// world made without opening the dialog is the world Quick always made. Only invented peoples have
/// one; see <see cref="QuickChoices.InspirationInWorld"/>.
/// </summary>
public sealed class QuickInspiration
{
    public MapConfig.CultureLookTheme Theme { get; set; } = MapConfig.CultureLookTheme.VariedGlobal;
    public MapConfig.HumanLook Look { get; set; } = MapConfig.HumanLook.Varied;

    public QuickInspiration Clone() => (QuickInspiration)MemberwiseClone();

    /// <summary>Anywhere for both: the generator's own varied world.</summary>
    public bool IsDefault => Theme == MapConfig.CultureLookTheme.VariedGlobal && Look == MapConfig.HumanLook.Varied;

    public void ApplyTo(MapConfig cfg)
    {
        cfg.CultureAestheticsTheme = Theme;
        cfg.DominantLook = Look;
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
    internal Preset? MatchingPreset => Presets.FirstOrDefault(p => p.Theme == Theme && p.Look == Look);

    /// <summary>
    /// The inspiration in a few words, for the link, the Invented card and the Review row: the
    /// tile's name, or the two halves of a hand-set pairing — "Norse culture, Mediterranean faces".
    /// </summary>
    public string Describe() => MatchingPreset?.Name
        ?? $"{Cultures.First(c => c.Theme == Theme).Short} culture, {Faces.First(f => f.Look == Look).Short} faces";

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
        new(MapConfig.CultureLookTheme.NorthernNorse, "Norse", "Norse", "Norse dress",
            [QuickClimate.Warm], "hot lands", QuickClimate.Northern),
        new(MapConfig.CultureLookTheme.WesternEuropean, "Western European", "Western European", "", [], "", QuickClimate.Temperate),
        new(MapConfig.CultureLookTheme.ByzantineGreek, "Byzantine", "Byzantine", "", [], "", QuickClimate.Temperate),
        new(MapConfig.CultureLookTheme.MiddleEasternMena, "Middle Eastern", "Middle Eastern", "", [], "", QuickClimate.Warm),
        new(MapConfig.CultureLookTheme.SteppeNomadic, "Steppe", "Steppe", "Steppe dress",
            [QuickClimate.Warm], "hot lands", QuickClimate.Northern),
        new(MapConfig.CultureLookTheme.SubSaharanAfrican, "Sub-Saharan", "Sub-Saharan", "African dress",
            [QuickClimate.Northern], "cold lands", QuickClimate.Warm),
        new(MapConfig.CultureLookTheme.IndianEastAsian, "Indian & East Asian", "Indian and East Asian", "", [], "", QuickClimate.Warm),
    ];

    /// <summary>One face preset, as the dialog offers it: its chip and its word in a description.</summary>
    internal sealed record FaceOption(MapConfig.HumanLook Look, string Name, string Short);

    /// <summary>West to east, the regional presets before the mixed ones that span them.</summary>
    internal static readonly FaceOption[] Faces =
    [
        new(MapConfig.HumanLook.Varied, "Anywhere", "varied"),
        new(MapConfig.HumanLook.WesternEuropean, "Western European", "Western European"),
        new(MapConfig.HumanLook.MixedEuropean, "All of Europe", "European"),
        new(MapConfig.HumanLook.Mediterranean, "Mediterranean", "Mediterranean"),
        new(MapConfig.HumanLook.MixedMediterranean, "Mediterranean & African", "Mediterranean and African"),
        new(MapConfig.HumanLook.MiddleEastern, "Middle Eastern", "Middle Eastern"),
        new(MapConfig.HumanLook.Steppe, "Steppe", "Steppe"),
        new(MapConfig.HumanLook.SubSaharan, "Sub-Saharan", "Sub-Saharan"),
        new(MapConfig.HumanLook.EastAsian, "East Asian", "East Asian"),
        new(MapConfig.HumanLook.SoutheastAsian, "Southeast Asian", "Southeast Asian"),
        new(MapConfig.HumanLook.MixedAsian, "All of Asia", "Asian"),
    ];

    internal CultureOption Culture => Cultures.First(c => c.Theme == Theme);

    /// <summary>Whether the culture's wardrobe has little for much of a climate's map.</summary>
    public bool Unsuits(QuickClimate climate) => Culture.Unsuited.Contains(climate);

    /// <summary>The climates this culture's wardrobe covers, for a surprise to draw from.</summary>
    public QuickClimate[] SuitedClimates => [.. Enum.GetValues<QuickClimate>().Where(c => !Unsuits(c))];
}
