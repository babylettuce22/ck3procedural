using Ck3MapGen.Config;

namespace Ck3MapGen.AppGUI;

/// <summary>How big the map is. Each is a size CK3 is known to render; see MapGen.TileFit.</summary>
public enum QuickSize { Small, Standard, Large }

/// <summary>When the game starts. Sets the World Year; advancement follows it.</summary>
public enum QuickEra { Early, High, Late }

/// <summary>Which stretch of latitude the map covers, which decides every climate band on it.</summary>
public enum QuickClimate { Northern, Temperate, Warm, Globe }

/// <summary>How large a county is: fewer and larger, vanilla-ish, or many and small.</summary>
public enum QuickDensity { Fewer, Balanced, More }

/// <summary>How the realms stand on day one.</summary>
public enum QuickPolitics { Fragmented, Kingdoms, Hegemony }

/// <summary>Where cultures and faiths come from.</summary>
public enum QuickPeople { Invented, RealCk3 }

/// <summary>
/// Everything the Quick generator asks, and nothing else. It is a way of <em>filling in</em> the
/// normal settings, not a generator of its own: <see cref="ApplyTo"/> writes these onto a
/// <see cref="MapConfig"/> that has just been reset to defaults, the chosen Forge preset becomes
/// the heightmap source, and from there it is an ordinary run. That is what lets "Customize in
/// Complex" hand over the exact world, with every setting the Quick page chose visible and
/// adjustable in the grid.
///
/// The ranges behind each friendly label are deliberately conservative: each one is a value the
/// generator is tuned around, so no combination of Quick choices lands on an odd corner of the
/// two hundred settings underneath.
///
/// Remembered across sessions in <see cref="GuiState.Quick"/>, so the page opens on the last world
/// made. The seed is not kept; a fresh one is drawn each visit.
/// </summary>
public sealed class QuickChoices
{
    public string MapType { get; set; } = "continents";
    public int Seed { get; set; } = 1;
    public QuickSize Size { get; set; } = QuickSize.Standard;
    public QuickEra Era { get; set; } = QuickEra.High;
    public QuickClimate Climate { get; set; } = QuickClimate.Temperate;
    public QuickDensity Density { get; set; } = QuickDensity.Balanced;
    public QuickPeople People { get; set; } = QuickPeople.Invented;
    public QuickPolitics Politics { get; set; } = QuickPolitics.Kingdoms;
    public GenderPreference Rulers { get; set; } = GenderPreference.Historical;
    public bool Wilderness { get; set; } = true;
    public bool Wars { get; set; } = true;

    public QuickChoices Clone() => (QuickChoices)MemberwiseClone();

    /// <summary>The heightmap's pixel size, which becomes the map's.</summary>
    public (int Width, int Height) Pixels => Size switch
    {
        QuickSize.Small => (4096, 2048),
        QuickSize.Large => (9216, 4608),
        _ => (8192, 4096),
    };

    public int StartYear => Era switch
    {
        QuickEra.Early => 867,
        QuickEra.Late => 1178,
        _ => 1066,
    };

    /// <summary>
    /// Equator position (fraction of the height, from the top) and the degrees of latitude the map
    /// spans. The equator is kept inside the map, which MapConfig.Equator clamps to anyway.
    /// <list type="bullet">
    /// <item>Northern: 90°N at the top down to the equator at the bottom edge.</item>
    /// <item>Temperate: the generator's own default, about 72°N to 8°S.</item>
    /// <item>Warm: about 42°N to 28°S, tropics through the lower middle.</item>
    /// <item>Globe: 75°N to 75°S, cold at both edges and tropics in the middle.</item>
    /// </list>
    /// </summary>
    public (double Equator, double Span) Latitudes => Climate switch
    {
        QuickClimate.Northern => (1.0, 90),
        QuickClimate.Warm => (0.6, 70),
        QuickClimate.Globe => (0.5, 150),
        _ => (0.9, 80),
    };

    /// <summary>Barony size relative to vanilla's. 1.25 is the generator's default.</summary>
    public double CountyScale => Density switch
    {
        QuickDensity.Fewer => 1.6,
        QuickDensity.More => 1.0,
        _ => 1.25,
    };

    /// <summary>
    /// Writes these choices over a config. Call it on a config just reset to defaults, so nothing a
    /// Complex session left behind leaks into a Quick world.
    /// </summary>
    public void ApplyTo(MapConfig cfg)
    {
        cfg.Seed = Seed;

        cfg.StartYear = StartYear;
        cfg.EraAnchorYear = 0;   // "Follow World Year": the era choice decides both

        var (equator, span) = Latitudes;
        cfg.EquatorPosition = equator;
        cfg.MapLatitudeSpan = span;

        cfg.CountyScale = CountyScale;

        cfg.ContentSource = People == QuickPeople.RealCk3
            ? MapConfig.ContentSourceMode.VanillaWorld
            : MapConfig.ContentSourceMode.Procedural;

        cfg.ShatteredWorld = Politics == QuickPolitics.Fragmented;
        cfg.StartingHegemony = Politics == QuickPolitics.Hegemony;
        cfg.Gender = Rulers;

        // Magic is not a finished feature yet, so a Quick world never ships it and the page does
        // not offer it. Complex still has the switch, for working on it.
        cfg.EnableMagic = false;
        cfg.EnableWilderness = Wilderness;
        cfg.EnableStartingWars = Wars;
    }

    /// <summary>A different world with the same page layout: every choice drawn at random.</summary>
    public static QuickChoices Surprise(Random random, IReadOnlyList<QuickMapType> types)
    {
        T Pick<T>() where T : struct, Enum
        {
            var values = Enum.GetValues<T>();
            return values[random.Next(values.Length)];
        }

        return new QuickChoices
        {
            MapType = types.Count > 0 ? types[random.Next(types.Count)].Key : "continents",
            Seed = random.Next(1, 1_000_000),
            // Size stays Standard in a surprise: it changes how long the run takes, not the world.
            Size = QuickSize.Standard,
            Era = Pick<QuickEra>(),
            Climate = Pick<QuickClimate>(),
            Density = QuickDensity.Balanced,
            People = QuickPeople.Invented,
            Politics = Pick<QuickPolitics>(),
            Rulers = Pick<GenderPreference>(),
            Wilderness = random.Next(4) != 0,
            Wars = random.Next(3) != 0,
        };
    }
}

/// <summary>One of the shipped map types: a Forge preset, with the words the page shows for it.</summary>
public sealed record QuickMapType(string Key, string Title, string Blurb)
{
    public string PresetPath => Path.Combine(QuickCatalogue.PresetDirectory, Key + ".json");
}

/// <summary>
/// The map types, in the order the page shows them. Anything else found in the presets folder is
/// listed after, under its file name, so a preset dropped in there shows up without a code change.
/// </summary>
public static class QuickCatalogue
{
    public static string PresetDirectory => Path.Combine(AppContext.BaseDirectory, "assets", "forge-presets");

    private static readonly QuickMapType[] Curated =
    [
        new("continents", "Continents", "Several great landmasses parted by open sea. The classic."),
        new("pangaea", "Pangaea", "One vast supercontinent, its coasts cut by gulfs and inland seas."),
        new("twin-continents", "Twin Continents", "Two great landmasses facing each other across a strait."),
        new("inland-sea", "Inland Sea", "A ring of lands around one great central sea."),
        new("one-great-island", "One Great Island", "A single island realm with open ocean on every side."),
        new("archipelago", "Archipelago", "Islands and island chains without end. The sea is the road."),
    ];

    public static IReadOnlyList<QuickMapType> All()
    {
        var found = new List<QuickMapType>();
        foreach (var type in Curated)
            if (File.Exists(type.PresetPath)) found.Add(type);

        try
        {
            if (Directory.Exists(PresetDirectory))
            {
                foreach (string file in Directory.GetFiles(PresetDirectory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    string key = Path.GetFileNameWithoutExtension(file);
                    if (found.Any(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;
                    string title = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace('-', ' '));
                    found.Add(new QuickMapType(key, title, "A Forge preset from the presets folder."));
                }
            }
        }
        catch (IOException) { }

        return found;
    }
}
