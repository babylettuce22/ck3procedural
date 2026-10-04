using Ck3MapGen.Config;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// How big the map is. Each preset is a size CK3 is known to render; see MapGen.TileFit. Custom is
/// whatever <see cref="QuickChoices.CustomWidth"/> by <see cref="QuickChoices.CustomHeight"/> says,
/// which may not be. Vanilla and Custom come last only because the choice is remembered as a
/// number in <see cref="GuiState.Quick"/>.
/// </summary>
public enum QuickSize { Small, Standard, Large, Vanilla, Custom }

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
/// Which tuning of the map types' presets to build from. Ranges is the current one: its Contrast
/// curve never reaches the stage ceiling, so the highest ground keeps a slope and ridge crests
/// round off instead of clipping. Classic is the tuning before that, kept in the presets folder's
/// <c>classic</c> subfolder, whose highest ground is cut flat into snow-capped tables.
/// </summary>
public enum QuickMountains { Ranges, Classic }

/// <summary>
/// How much of the fantastic the world holds. For now that is its peoples: whether elves, dwarves,
/// orcs and the other races live alongside humans, and how much of the land they hold
/// (<see cref="MapConfig.RaceMode"/>). Only invented peoples can be anything but human; see
/// <see cref="QuickChoices.FantasyInWorld"/>.
/// </summary>
public enum QuickFantasy { None, Low, High }

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
    /// <summary>A PNG used instead of a Forge template; empty for generated terrain.</summary>
    public string HeightmapPath { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ImportsHeightmap => !string.IsNullOrWhiteSpace(HeightmapPath);
    public int Seed { get; set; } = 1;
    public QuickRelief Relief { get; set; } = QuickRelief.Standard;
    public QuickMountains Mountains { get; set; } = QuickMountains.Ranges;
    public QuickSize Size { get; set; } = QuickSize.Standard;

    /// <summary>
    /// The Custom size, in heightmap pixels. Kept while another size is picked, like the race mix,
    /// so choosing Custom again finds it. Read through <see cref="Pixels"/>, which snaps it.
    /// </summary>
    public int CustomWidth { get; set; } = 6144;
    public int CustomHeight { get; set; } = 3072;

    public QuickEra Era { get; set; } = QuickEra.High;
    public QuickClimate Climate { get; set; } = QuickClimate.Temperate;
    public QuickDensity Density { get; set; } = QuickDensity.Balanced;
    public QuickPeople People { get; set; } = QuickPeople.Invented;
    public QuickFantasy Fantasy { get; set; } = QuickFantasy.None;
    public QuickPolitics Politics { get; set; } = QuickPolitics.Kingdoms;
    public GenderPreference Rulers { get; set; } = GenderPreference.Historical;
    public bool Wilderness { get; set; } = true;
    public bool Wars { get; set; } = true;

    /// <summary>Rulers styled in their own people's language; see <see cref="MapConfig.NativeRankTitles"/>.</summary>
    public bool NativeTitles { get; set; }

    /// <summary>Realms named in it too; see <see cref="MapConfig.NativeRealmNames"/>.</summary>
    public bool NativeRealms { get; set; }

    /// <summary>
    /// The paper map with all its pen work — roads, compass rose, hatching, hachures, waterlines,
    /// lettering and border — or off for the plain parchment it was before any of it was inked.
    /// </summary>
    public bool DetailedPaperMap { get; set; } = true;

    /// <summary>
    /// A hand-set race mix, or null for the fantasy choice's own. Kept while Fantasy is None, like
    /// the pick itself, but only applied with races on; see <see cref="MixInWorld"/>.
    /// </summary>
    public QuickRaceMix? Mix { get; set; }

    /// <summary>
    /// The corner of the real world the invented peoples are modelled on, or null for anywhere.
    /// Kept while the peoples are real CK3 ones, like the fantasy pick, but only applied to
    /// invented ones; see <see cref="InspirationInWorld"/>.
    /// </summary>
    public QuickInspiration? Inspiration { get; set; }

    public QuickChoices Clone()
    {
        var copy = (QuickChoices)MemberwiseClone();
        copy.Mix = Mix?.Clone();
        copy.Inspiration = Inspiration?.Clone();
        return copy;
    }

    /// <summary>The mix the world is actually made with: none unless it has races.</summary>
    public QuickRaceMix? MixInWorld => FantasyInWorld == QuickFantasy.None ? null : Mix;

    /// <summary>
    /// The inspiration the world is actually made with: none for real CK3 peoples, whose looks,
    /// dress and names are vanilla's own.
    /// </summary>
    public QuickInspiration? InspirationInWorld => People == QuickPeople.Invented ? Inspiration : null;

    /// <summary>
    /// Whether the relief choice is applied region by region (<see cref="AppGUI.RegionalRelief"/>)
    /// rather than map-wide. Goes with Ranges: Classic keeps the old presets and the old map-wide
    /// relief together, so a Classic world is still the world it always was.
    /// </summary>
    public bool RegionalRelief => Mountains == QuickMountains.Ranges;

    /// <summary>Every choice on one line, for the run log and the record's header.</summary>
    public string Summary()
    {
        var (w, h) = Pixels;
        return (ImportsHeightmap
                   ? $"Quick world: imported heightmap {HeightmapPath}, seed {Seed}, "
                   : $"Quick world: {MapType}, seed {Seed}, relief {Relief} ({(RegionalRelief ? "regional" : "map-wide")}), mountains {Mountains}, ")
               + $"size {Size} ({w}x{h}{(SizeVerified ? "" : ", unverified")}), era {Era}, climate {Climate}, density {Density}, "
               + $"people {People}"
               + (InspirationInWorld is { } inspiration
                   ? $" (inspiration: {inspiration.Describe()}; theme {inspiration.Theme}, faces {inspiration.Look})" : "")
               + $", fantasy {FantasyInWorld}"
               + (MixInWorld is { } mix ? $" (custom mix: {mix.Describe(FantasyInWorld)})" : "")
               + $", politics {Politics}, rulers {Rulers}, "
               + $"wilderness {(Wilderness ? "on" : "off")}, wars {(Wars ? "on" : "off")}, "
               + $"native titles {(NativeTitles ? "on" : "off")}, native realms {(NativeRealms ? "on" : "off")}, "
               + $"paper map {(DetailedPaperMap ? "detailed" : "plain")}";
    }

    /// <summary>
    /// The fantasy the world is actually made with. Vanilla's cultures are human by construction,
    /// and the generator refuses races on them (see Generator), so a world of real CK3 people is
    /// None whatever was picked. The pick itself is kept, so switching back to invented peoples
    /// finds it again.
    /// </summary>
    public QuickFantasy FantasyInWorld => People == QuickPeople.Invented ? Fantasy : QuickFantasy.None;

    /// <summary>The heightmap's pixel size, which becomes the map's.</summary>
    public (int Width, int Height) Pixels => Size switch
    {
        QuickSize.Small => (4096, 2048),
        QuickSize.Large => (9216, 4608),
        QuickSize.Vanilla => (18432, 9216),
        QuickSize.Custom => SnapCustom(CustomWidth, CustomHeight),
        _ => (8192, 4096),
    };

    /// <summary>The smallest and largest Custom sides, and the step they snap to.</summary>
    public const int MinCustomWidth = 2048, MaxCustomWidth = 18432;
    public const int MinCustomHeight = 1024, MaxCustomHeight = 9216;

    /// <summary>
    /// A multiple of 128 on both sides: the heightmap packer's 64 px tiles must cover the map
    /// exactly (see MapGen.TileFit), and a size forged at half and doubled has to manage that at
    /// half too. Every known size is already one.
    /// </summary>
    public const int CustomStep = 128;

    /// <summary>A Custom size brought inside the range and onto the step.</summary>
    public static (int Width, int Height) SnapCustom(int width, int height)
    {
        static int Snap(int v, int min, int max)
            => Math.Clamp((int)Math.Round(v / (double)CustomStep) * CustomStep, min, max);
        return (Snap(width, MinCustomWidth, MaxCustomWidth), Snap(height, MinCustomHeight, MaxCustomHeight));
    }

    /// <summary>
    /// Whether <see cref="Pixels"/> is a size CK3 is known to render. Only a Custom size can fail
    /// it; one that does is built anyway, as a test, the way the Terrain workspace's "build at this
    /// size anyway" does, and the page says so before the run.
    /// </summary>
    public bool SizeVerified
    {
        get
        {
            var (w, h) = Pixels;
            return MapGen.TileFit.Fits(w, h);
        }
    }

    /// <summary>
    /// How many times the Forge's heightfield is enlarged, after erosion, to reach <see cref="Pixels"/>.
    /// Vanilla is forged at half size and doubled: erosion holds about 56 bytes a cell on the GPU,
    /// 9.5 GB at 18432x9216, which overflows a 10 GB card into shared memory and crawls; and its
    /// step count grows with the height too, so full size would be about 8x Large's erosion work.
    /// Half size is Large's own load. The rest of the generator still builds at full size. A
    /// Custom size bigger than Large is forged at half for the same reason.
    /// </summary>
    public int ForgeUpscale
    {
        get
        {
            if (Size == QuickSize.Vanilla) return 2;
            if (Size != QuickSize.Custom) return 1;
            var (w, h) = Pixels;
            return (long)w * h > 9216L * 4608 ? 2 : 1;
        }
    }

    /// <summary>The size the Forge preset itself runs at; <see cref="Pixels"/> over <see cref="ForgeUpscale"/>.</summary>
    public (int Width, int Height) ForgePixels => (Pixels.Width / ForgeUpscale, Pixels.Height / ForgeUpscale);

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

    /// <summary>Shares of baronies that become mountains and hills, and of land left impassable.</summary>
    public (double Mountains, double Hills, double Impassable) TerrainShares => Relief switch
    {
        QuickRelief.Lowlands => (0.07, 0.12, 0.05),
        QuickRelief.Highlands => (0.21, 0.25, 0.11),
        _ => (0.14, 0.19, 0.08),
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

        // The game half of the relief choice (the terrain half is QuickTerrain). CK3's hills and
        // mountains are handed out by rank at these shares, so without them a Highlands map would
        // look rugged and play like any other. Standard keeps the generator's own, which sit near
        // vanilla's 12% mountains and 19% hills.
        var (mountains, hills, impassable) = ImportsHeightmap
            ? (0.14, 0.19, 0.08) : TerrainShares;
        cfg.MountainProvinceShare = mountains;
        cfg.HillProvinceShare = hills;
        cfg.ImpassableShareOfLand = impassable;

        // A set-piece map type records what it drew, so the generator can find it again to name it.
        cfg.SetPiece = ImportsHeightmap || QuickCatalogue.FeatureOf(MapType) == QuickFeature.None ? "" : $"{MapType}@{Seed}";

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

        // Only for invented peoples. Real CK3 cultures already have vanilla's words for their
        // ranks and no generated language to coin new ones from; the page hides both switches then.
        bool invented = People == QuickPeople.Invented;
        cfg.NativeRankTitles = invented && NativeTitles;
        cfg.NativeRealmNames = invented && NativeRealms;

        // Every half of the paper map's pen work together. Plain is the parchment alone, with the
        // parchment pass's own thin coast. The edge feather is not pen work and stays as reset.
        cfg.FlatmapRoads = DetailedPaperMap;
        cfg.FlatmapFlourishes = DetailedPaperMap;
        cfg.FlatmapHachures = DetailedPaperMap;
        cfg.FlatmapCoastInk = DetailedPaperMap;

        // Where the invented peoples draw their culture and faces from. Without one both stay as
        // the reset left them, varied, which is every Quick world made before the choice existed.
        InspirationInWorld?.ApplyTo(cfg);

        // The fantasy choice, which for now is the world's races. Low and High are the generator's
        // own two tunings; every other race setting (how many races, terrain, minorities) stays at
        // its default, which is what those tunings are built around. None leaves RaceMode as the
        // reset left it and only switches races off, so a None world is the human world a Quick
        // run made before this choice existed.
        var fantasy = FantasyInWorld;
        cfg.EnableFantasyEthnicities = fantasy != QuickFantasy.None;
        if (fantasy == QuickFantasy.Low) cfg.RaceMode = MapConfig.FantasyRaceMode.LowFantasy;
        else if (fantasy == QuickFantasy.High) cfg.RaceMode = MapConfig.FantasyRaceMode.HighFantasy;

        // A hand-set mix takes over how much land each race holds; Low or High still decides how
        // strongly they look it. Without one the reset left Custom Race Mix off.
        MixInWorld?.ApplyTo(cfg);

        // Three start dates, as vanilla has. They follow the world's history when the player
        // accepts it later than it began — see MapGen.HistoryEras — and a world of real CK3 people
        // has vanilla's own dates instead (UsesAdditionalBookmarks says no to it).
        cfg.AdditionalBookmarks = true;
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
            Relief = Pick<QuickRelief>(),
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

/// <summary>
/// A hand-set race mix for a Quick world: how much of the land humans hold, and how the other races
/// divide the rest. Edited in the People step's race mix dialog (<see cref="RaceMixDialog"/>) and
/// written onto MapConfig's Custom Race Mix rows by <see cref="ApplyTo"/>, so a world handed to
/// Complex shows the same mix in the grid.
///
/// A mix left at its defaults is not a mix: the page drops it (<see cref="IsDefault"/>), so a world
/// made without touching the dialog is the world the fantasy choice alone makes.
/// </summary>
public sealed class QuickRaceMix
{
    /// <summary>What every race's weight starts at, as in MapConfig.</summary>
    public const int DefaultWeight = 50;

    /// <summary>
    /// Percent of the land humans hold. 0 follows the fantasy choice (85% low, 35% high), and the
    /// page puts it back to 0 when that choice changes: the weights are a matter of taste, the
    /// human share is most of what Low and High mean.
    /// </summary>
    public int HumanShare { get; set; }

    public int Dwarves { get; set; } = DefaultWeight;
    public int HighElves { get; set; } = DefaultWeight;
    public int WoodElves { get; set; } = DefaultWeight;
    public int Orcs { get; set; } = DefaultWeight;
    public int Gnomes { get; set; } = DefaultWeight;
    public int Giantkin { get; set; } = DefaultWeight;
    public int DuskElves { get; set; } = DefaultWeight;
    public int Hornkin { get; set; } = DefaultWeight;

    public QuickRaceMix Clone() => (QuickRaceMix)MemberwiseClone();

    /// <summary>One race's row: its name, the ground it favours, and its weight.</summary>
    internal sealed record Row(MapGen.RaceArchetype Race, string Name, string Ground,
        Func<QuickRaceMix, int> Get, Action<QuickRaceMix, int> Set);

    /// <summary>The races, in the order the dialog lists them. The ground is Ethnicities' affinity table in words.</summary>
    internal static readonly Row[] Rows =
    [
        new(MapGen.RaceArchetype.Dwarf, "Dwarves", "mountains and hills", m => m.Dwarves, (m, v) => m.Dwarves = v),
        new(MapGen.RaceArchetype.HighElf, "High elves", "plains and farmland", m => m.HighElves, (m, v) => m.HighElves = v),
        new(MapGen.RaceArchetype.WoodElf, "Wood elves", "forest and taiga", m => m.WoodElves, (m, v) => m.WoodElves = v),
        new(MapGen.RaceArchetype.Orc, "Orcs", "mountains, desert, steppe", m => m.Orcs, (m, v) => m.Orcs = v),
        new(MapGen.RaceArchetype.Gnome, "Gnomes", "wetlands, desert, hills", m => m.Gnomes, (m, v) => m.Gnomes = v),
        new(MapGen.RaceArchetype.Giantkin, "Giantkin", "arctic and mountains", m => m.Giantkin, (m, v) => m.Giantkin = v),
        new(MapGen.RaceArchetype.DuskElf, "Dusk elves", "hills and fens", m => m.DuskElves, (m, v) => m.DuskElves = v),
        new(MapGen.RaceArchetype.Hornkin, "Hornkin", "steppe and dry scrub", m => m.Hornkin, (m, v) => m.Hornkin = v),
    ];

    /// <summary>The human share Low or High uses, in percent: the generator's own figure.</summary>
    public static int DefaultHumanShare(QuickFantasy level) => (int)Math.Round(100 * MapGen.Ethnicities.HumanShareFor(
        level == QuickFantasy.Low ? MapConfig.FantasyRaceMode.LowFantasy : MapConfig.FantasyRaceMode.HighFantasy));

    /// <summary>The human share this mix comes to at a fantasy level, in percent.</summary>
    public int HumanPercent(QuickFantasy level) => HumanShare > 0 ? HumanShare : DefaultHumanShare(level);

    /// <summary>Each race's share of the whole land, in percent, in <see cref="Rows"/> order.</summary>
    public double[] RaceShares(QuickFantasy level)
    {
        double rest = 100 - HumanPercent(level);
        double total = Rows.Sum(r => Math.Max(0, r.Get(this)));
        return [.. Rows.Select(r => total > 0 ? rest * Math.Max(0, r.Get(this)) / total : 0.0)];
    }

    /// <summary>Nothing moved: the fantasy choice's own mix.</summary>
    public bool IsDefault => HumanShare == 0 && Rows.All(r => r.Get(this) == DefaultWeight);

    /// <summary>Every other race turned off, which leaves a world of humans: the None choice, not a mix.</summary>
    public bool NoOtherRaces => Rows.All(r => r.Get(this) <= 0);

    /// <summary>Onto the config's Custom Race Mix rows.</summary>
    public void ApplyTo(MapConfig cfg)
    {
        cfg.CustomRaceMix = true;
        cfg.RaceMixHumanShare = HumanShare;
        cfg.RaceMixDwarves = Dwarves;
        cfg.RaceMixHighElves = HighElves;
        cfg.RaceMixWoodElves = WoodElves;
        cfg.RaceMixOrcs = Orcs;
        cfg.RaceMixGnomes = Gnomes;
        cfg.RaceMixGiantkin = Giantkin;
        cfg.RaceMixDuskElves = DuskElves;
        cfg.RaceMixHornkin = Hornkin;
    }

    /// <summary>
    /// The mix in a few words, for the Review row, the Fantasy card and the run log: the human
    /// share, the two biggest races, and which are left out — "40% human · dwarves 21%, orcs 12% ·
    /// no gnomes".
    /// </summary>
    public string Describe(QuickFantasy level)
    {
        var shares = RaceShares(level);
        var parts = new List<string> { $"{HumanPercent(level)}% human" };

        var biggest = Rows.Select((r, i) => (r.Name, Share: shares[i]))
            .Where(s => s.Share > 0)
            .OrderByDescending(s => s.Share)
            .Take(2)
            .Select(s => $"{s.Name.ToLowerInvariant()} {Math.Round(s.Share):0}%")
            .ToList();
        if (biggest.Count > 0) parts.Add(string.Join(", ", biggest));

        var off = Rows.Where(r => r.Get(this) <= 0).Select(r => r.Name.ToLowerInvariant()).ToList();
        if (off.Count is > 0 and <= 3) parts.Add("no " + string.Join(" or ", off));
        else if (off.Count > 3) parts.Add($"{off.Count} races left out");

        return string.Join("  ·  ", parts);
    }
}

/// <summary>
/// One of the shipped map types: a Forge preset, with the words the page shows for it, and the
/// set-piece drawn into it for each seed (see <see cref="QuickFeatures"/>).
/// </summary>
public sealed record QuickMapType(string Key, string Title, string Blurb, QuickFeature Feature = QuickFeature.None)
{
    public string PresetPath => Path.Combine(QuickCatalogue.PresetDirectory, Key + ".json");

    /// <summary>
    /// The preset for a mountain style. A type with no classic copy (one dropped into the presets
    /// folder by hand) has only the one tuning, and uses it for both.
    /// </summary>
    public string PresetPathFor(QuickMountains mountains)
    {
        if (mountains != QuickMountains.Classic) return PresetPath;
        string classic = Path.Combine(QuickCatalogue.ClassicDirectory, Key + ".json");
        return File.Exists(classic) ? classic : PresetPath;
    }
}

/// <summary>
/// The map types, in the order the page shows them. Anything else found in the presets folder is
/// listed after, under its file name, so a preset dropped in there shows up without a code change.
/// </summary>
public static class QuickCatalogue
{
    public static string PresetDirectory => Path.Combine(AppContext.BaseDirectory, "assets", "forge-presets");

    /// <summary>The presets as they were tuned before Ranges; see <see cref="QuickMountains.Classic"/>.
    /// A subfolder, so the scan below does not list them as map types of their own.</summary>
    public static string ClassicDirectory => Path.Combine(PresetDirectory, "classic");

    private static readonly QuickMapType[] Curated =
    [
        new("continents", "Continents", "Several great landmasses parted by open sea. The classic."),
        new("pangaea", "Pangaea", "One vast supercontinent, its coasts cut by gulfs and inland seas."),
        new("twin-continents", "Twin Continents", "Two great landmasses facing each other across a strait."),
        new("inland-sea", "Inland Sea", "A ring of lands around one great central sea."),
        new("one-great-island", "One Great Island", "A single island realm with open ocean on every side."),
        new("archipelago", "Archipelago", "Islands and island chains without end. The sea is the road."),
        new("crater", "The Crater", "A vast impact basin ringed by mountains, a world within the world.", QuickFeature.Crater),
        new("crater-sea", "The Drowned Crater", "A great crater flooded into a round sea, its central peak an island.", QuickFeature.FloodedCrater),
        new("scar", "The Scar", "A chain of craters across the land, where something broke apart as it fell.", QuickFeature.Scar),
        new("rift", "The Rift", "A continent pulling apart: a sunken valley between escarpments, lakes along its floor, often the sea at one end.", QuickFeature.Rift),
        new("spine", "The Spine", "One great range runs the length of the land, the sea close under it on one side.", QuickFeature.Spine),
        new("wall", "The Wall", "A sheer mountain wall from coast to coast splits the land in two.", QuickFeature.Wall),
    ];

    /// <summary>The set-piece a shipped map type is built around; None for any other key.</summary>
    public static QuickFeature FeatureOf(string key)
        => Curated.FirstOrDefault(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Feature ?? QuickFeature.None;

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
