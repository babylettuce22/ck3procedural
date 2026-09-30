using Ck3MapGen.Config;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Everything the Azgaar page asks: the two files, and the overrides — the few places a user may
/// want the world to differ from what the export says.
///
/// Every override starts on "Azgaar", meaning the export's own answer: how advanced its peoples
/// are, a barony per town with each of its provinces kept whole as one county, its unclaimed land left
/// wild, its race tags drawn. What the export decides outright (countries, cultures, faiths, names,
/// climate, the calendar) is not asked at all.
///
/// Like <see cref="QuickChoices"/> it is a way of filling in the normal settings, not an importer of
/// its own. <see cref="ApplyTo"/> writes these onto a config just reset to defaults; the export's
/// path goes where Complex's Azgaar menu puts it, the heightmap becomes the source the way the
/// heightmap button makes it, and from there it is an ordinary run. So "Customize in Complex"
/// hands over exactly the world that was made.
///
/// Only the files are remembered across sessions (<see cref="GuiState.Azgaar"/>); the overrides
/// start on Azgaar at every visit, so a choice made for one map never quietly applies to the next.
/// </summary>
public sealed class AzgaarChoices
{
    public string HeightmapPath { get; set; } = "";
    public string ExportPath { get; set; } = "";

    /// <summary>
    /// How advanced the world's cultures are, as a vanilla year (867, 1066, 1178), or 0 for Azgaar:
    /// read from the export's peoples (<see cref="MapGen.AzgaarAdvancement"/>). Not when the game
    /// starts: the export's own year is the calendar.
    /// </summary>
    public int Advancement { get; set; }

    /// <summary>
    /// Barony size, or null for Azgaar: each province cut into a barony per town
    /// (<see cref="MapConfig.AzgaarBaroniesFromTowns"/>).
    /// </summary>
    public QuickDensity? Density { get; set; }

    /// <summary>
    /// Under Azgaar density, the uniform barony size for land outside the export's provinces: the
    /// one at which the whole map would hold a barony per town. Worked out by the page from the
    /// files (<see cref="AzgaarFiles.ProvinceScale"/>).
    /// </summary>
    public double AzgaarCountyScale { get; set; } = 1.25;

    /// <summary>Azgaar: land no country claims starts wild. Off settles it like everywhere else.</summary>
    public bool Wilderness { get; set; } = true;

    /// <summary>
    /// Azgaar: peoples the export tags with a race — "Dunirr (Dwarven)" — look like that race.
    /// Off makes everyone human. Means nothing on an export that tags nobody.
    /// </summary>
    public bool FantasyRaces { get; set; } = true;

    /// <summary>
    /// How the terrain is finished. The one override whose first card is not the export's picture
    /// as it stands: Azgaar's heightmap is a stack of flat terraces, so the default keeps its
    /// coastline, ranges and uplands and makes the rest the way the map types do
    /// (<see cref="MapGen.AzgaarRelief"/>). As drawn is the exported PNG, stretched onto CK3's scale.
    /// </summary>
    public AzgaarTerrain Terrain { get; set; } = AzgaarTerrain.Weathered;

    public AzgaarChoices Clone() => (AzgaarChoices)MemberwiseClone();

    public double CountyScale => Density switch
    {
        QuickDensity.Fewer => 1.6,
        QuickDensity.Balanced => 1.25,
        QuickDensity.More => 1.0,
        _ => AzgaarCountyScale,
    };

    /// <summary>
    /// Writes these choices over a config. Call it on a config just reset to defaults, so nothing a
    /// Complex session left behind leaks into the imported world.
    /// </summary>
    /// <param name="hasRaceTags">Whether the export tags any people with a race.</param>
    public void ApplyTo(MapConfig cfg, int seed, bool hasRaceTags)
    {
        cfg.Seed = seed;
        cfg.AzgaarJsonPath = ExportPath;

        // The one setting every Azgaar import needs (Complex offers it when an export is chosen):
        // Azgaar's heights sit compressed against CK3's scale, and Stretch is what lands the relief.
        cfg.Normalization = HeightmapNormalization.Stretch;

        // The export's year replaces StartYear as the run loads it. Advancement left at 0 is read
        // from the export there (AzgaarImport.AdoptCalendar); a year picked here is used as it is,
        // and doubles as the start year for an export that carries no year of its own.
        if (Advancement > 0) cfg.StartYear = Advancement;
        cfg.EraAnchorYear = Math.Max(0, Advancement);

        // Azgaar: each province cut into a barony per town (MapGen.AzgaarSeeding), with the land
        // outside any province at the uniform size that matches the export's towns overall. An
        // override cuts everything at one size instead.
        cfg.AzgaarBaroniesFromTowns = Density is null;
        cfg.CountyScale = CountyScale;

        cfg.EnableMagic = false;   // not a finished feature; see QuickChoices.ApplyTo
        cfg.EnableWilderness = Wilderness;

        // The export's race tags are read whenever races are on; LowFantasy, the default mode,
        // keeps every untagged people mostly human, so it is the peoples the author tagged that
        // change.
        cfg.EnableFantasyEthnicities = FantasyRaces && hasRaceTags;
        cfg.RaceMode = MapConfig.FantasyRaceMode.LowFantasy;
    }

    /// <summary>
    /// The heightmap source these choices build from. Weathered is made here rather than in
    /// <see cref="ApplyTo"/> because it is a source, not a setting: it takes the seed
    /// <see cref="ApplyTo"/> just wrote so the terrain belongs to the same world as everything else.
    /// </summary>
    public MapGen.HeightmapProvider Source(int seed, (int Width, int Height)? fit) => Terrain switch
    {
        AzgaarTerrain.Weathered => new MapGen.AzgaarReliefProvider(HeightmapPath, ExportPath, seed, fit),
        _ => new MapGen.FileHeightmapProvider(HeightmapPath, fit),
    };
}

/// <summary>How an Azgaar world's terrain is finished; see <see cref="AzgaarChoices.Terrain"/>.</summary>
public enum AzgaarTerrain
{
    /// <summary>Azgaar's coastline, ranges and uplands, with hills, ridges and valleys made by the Forge.</summary>
    Weathered,

    /// <summary>The exported heightmap as it stands, terraces and all.</summary>
    AsDrawn,
}
