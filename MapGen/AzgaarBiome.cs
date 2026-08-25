using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The export's biome map, read as vegetation and nothing else.
///
/// This is the last of the layers Azgaar states directly and we were still guessing at. Our own
/// answer comes from <see cref="Koppen"/>, which is a good model and is now anchored on the export's
/// own temperature and rainfall — but a Koppen class is a *climate*, and what a player sees is the
/// vegetation drawn on top of it. Azgaar has already taken that second step, per cell, and a map's
/// author can edit the result by hand. Where it has an opinion it outranks ours, for the same reason
/// its borders do: it is a statement rather than an inference.
///
/// <b>What this does not take.</b> Relief, coast and altitude stay ours throughout. Azgaar's biomes
/// are computed from height as well as climate, so a naive substitution would fight the heightmap
/// rather than agree with it — and its cells are far coarser than our pixels, so a peak inside a
/// "temperate deciduous forest" cell is still a peak. The classifier therefore keeps beach, hills,
/// mountains and the snow line exactly as it derived them, and asks this only the question it is
/// qualified to answer: what grows on the ground.
/// </summary>
public static class AzgaarBiome
{
    /// <summary>
    /// One Azgaar biome, named in terms the classifier can act on.
    ///
    /// <see cref="Unknown"/> is load-bearing: it means "the export did not say", and every caller
    /// falls back to the generated answer on it rather than painting a default. That covers a custom
    /// biome nobody here has heard of, a biome id past the end of the table, and the land pixel that
    /// landed in a <see cref="Marine"/> cell because Azgaar draws its coast as smoothed isolines
    /// while we resample a heightmap — a few per cent of shoreline disagrees on every correct
    /// import, and none of it should come out painted as ocean floor.
    /// </summary>
    public enum Kind : byte
    {
        Unknown = 0,
        Marine,
        HotDesert,
        ColdDesert,
        Savanna,
        Grassland,
        TropicalSeasonalForest,
        TemperateForest,
        TropicalRainforest,
        TemperateRainforest,
        Taiga,
        Tundra,
        Glacier,
        Wetland,
    }

    /// <summary>
    /// Azgaar's stock biome table, in its own order. Used where the export carries no names to match
    /// against — an older export whose biome block is shaped differently, or one written without it.
    ///
    /// The ids are not a guess: this is <c>biomesData</c> in Azgaar's source, and its own
    /// <c>getBiomeId</c> indexes exactly this order. A map whose author added or reordered biomes in
    /// the editor is the case <see cref="FromName"/> exists for.
    /// </summary>
    private static readonly Kind[] Stock =
    [
        Kind.Marine,                  // 0
        Kind.HotDesert,               // 1
        Kind.ColdDesert,              // 2
        Kind.Savanna,                 // 3
        Kind.Grassland,               // 4
        Kind.TropicalSeasonalForest,  // 5
        Kind.TemperateForest,         // 6  "Temperate deciduous forest"
        Kind.TropicalRainforest,      // 7
        Kind.TemperateRainforest,     // 8
        Kind.Taiga,                   // 9
        Kind.Tundra,                  // 10
        Kind.Glacier,                 // 11
        Kind.Wetland,                 // 12
    ];

    /// <summary>
    /// A biome's kind from its name, for the export whose author renamed or added one.
    ///
    /// Keyword matching rather than a lookup table, because the point is to survive words nobody
    /// here chose. Order is the whole of the logic: "tropical rainforest" contains "forest" and
    /// "cold desert" contains "desert", so every specific test has to run before the general one it
    /// would otherwise be swallowed by. Anything unrecognised comes back <see cref="Kind.Unknown"/>
    /// and the generated classification stands, which is the honest answer for a word we cannot read.
    /// </summary>
    public static Kind FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Kind.Unknown;

        string n = name.Trim().ToLowerInvariant();
        bool tropical = n.Contains("tropical") || n.Contains("jungle") || n.Contains("equatorial");

        if (n.Contains("glacier") || n.Contains("ice sheet") || n.Contains("icecap")
            || n.Contains("ice cap")) return Kind.Glacier;
        if (n.Contains("tundra") || n.Contains("permafrost")) return Kind.Tundra;
        if (n.Contains("taiga") || n.Contains("boreal")) return Kind.Taiga;

        if (n.Contains("wetland") || n.Contains("marsh") || n.Contains("swamp") || n.Contains("bog")
            || n.Contains("mangrove")) return Kind.Wetland;

        if (n.Contains("rainforest") || n.Contains("rain forest"))
            return tropical ? Kind.TropicalRainforest : Kind.TemperateRainforest;
        if (n.Contains("jungle")) return Kind.TropicalRainforest;

        if (n.Contains("desert") || n.Contains("badland"))
            return n.Contains("cold") || n.Contains("polar") || n.Contains("arctic")
                ? Kind.ColdDesert
                : Kind.HotDesert;

        if (n.Contains("savanna") || n.Contains("sahel")) return Kind.Savanna;

        if (n.Contains("forest") || n.Contains("wood"))
            return tropical || n.Contains("seasonal") || n.Contains("monsoon")
                ? Kind.TropicalSeasonalForest
                : Kind.TemperateForest;

        if (n.Contains("grass") || n.Contains("steppe") || n.Contains("prairie") || n.Contains("veld")
            || n.Contains("pampas") || n.Contains("meadow") || n.Contains("plain"))
            return Kind.Grassland;

        if (n.Contains("marine") || n.Contains("ocean") || n.Contains("sea")) return Kind.Marine;

        return Kind.Unknown;
    }

    /// <summary>
    /// The kind of every biome id the export declares, indexed by id.
    ///
    /// Names first, ids second: a stock export agrees with <see cref="Stock"/> either way, and one
    /// whose author edited the biome list only agrees when read by name. Where the name is
    /// unreadable but the id is in range the stock meaning stands anyway — a renamed biome is far
    /// more likely to be a re-flavoured version of the one it replaced than something new in that
    /// slot, and the alternative is throwing away a layer we actually have.
    /// </summary>
    public static Kind[] Table(AzgaarWorld world)
    {
        var names = world.Pack.Biomes?.Name ?? [];
        int count = Math.Max(names.Length, Stock.Length);

        var table = new Kind[count];
        for (int i = 0; i < count; i++)
        {
            var byName = i < names.Length ? FromName(names[i]) : Kind.Unknown;
            table[i] = byName != Kind.Unknown ? byName
                     : i < Stock.Length ? Stock[i]
                     : Kind.Unknown;
        }

        return table;
    }

    // The levels the classifier's mosaic noise actually exceeds a given fraction of the time.
    //
    // They are not the fractions themselves, and that is the whole reason these are named. The noise
    // is a four-octave fBm folded onto 0..1, so its values pile up around 0.5 and the ends of the
    // range are nearly empty — measured over a full 4608x2304 field it runs 0.09 to 0.92, and:
    //
    //     top  5%  0.713     top 33%  0.562     top 80%  0.380
    //     top 10%  0.674     top 50%  0.499     top 90%  0.323
    //     top 20%  0.619     top 67%  0.437
    //
    // A threshold picked as though the field were uniform therefore misses by a wide margin. The
    // first cut of this table used 0.80 to mean "the wettest fifth of the savanna" and selected the
    // wettest 0.8% — the Fleunland import came out with 8.6% drylands and no jungle at all on a map
    // Azgaar had given 9.5% savanna. Named for the share they select so a mix reads as what it is.
    private const double TopTenth = 0.674;
    private const double TopFifth = 0.619;
    private const double TopThird = 0.562;
    private const double TopHalf = 0.499;
    private const double TopTwoThirds = 0.437;
    private const double TopThreeQuarters = 0.403;
    private const double TopFourFifths = 0.380;
    private const double TopNineTenths = 0.323;

    /// <summary>
    /// What grows here, in our vocabulary.
    ///
    /// <paramref name="patch"/> is the classifier's own mosaic noise, in 0..1, and is passed through
    /// for the same reason <see cref="Koppen.Terrain"/> takes it: a biome painted as one solid class
    /// reads as a flat sheet of colour, and an Azgaar biome is a *cell* attribute, so the raw mapping
    /// would draw its Voronoi polygons onto the finished map. Breaking each biome into a dominant
    /// class with clearings of a second hides the cell edges and puts the variety back.
    ///
    /// The mixes are deliberately asymmetric: a rainforest is nearly all canopy, a taiga is forest
    /// with bogs and clearings through it, a grassland is open ground with rough patches in it.
    /// </summary>
    public static TerrainClass Terrain(Kind kind, double patch) => kind switch
    {
        Kind.HotDesert => TerrainClass.Desert,

        // Cold desert is the Gobi, not the Sahara: stony steppe, with true sand in the worst of it.
        Kind.ColdDesert => patch > TopFifth ? TerrainClass.Desert : TerrainClass.Steppe,

        // Azgaar's savanna sits between grassland and hot desert and is drier than Koppen's Aw, so
        // it leans dryland rather than plains and only breaks into gallery forest at the wet end.
        Kind.Savanna => patch > TopTenth ? TerrainClass.Jungle
                      : patch > TopHalf ? TerrainClass.Drylands
                      : TerrainClass.Plains,

        Kind.Grassland => patch > TopThird ? TerrainClass.Steppe : TerrainClass.Plains,

        Kind.TropicalSeasonalForest => patch > TopTwoThirds ? TerrainClass.Jungle : TerrainClass.Plains,
        Kind.TemperateForest => patch > TopThreeQuarters ? TerrainClass.Forest : TerrainClass.Plains,
        Kind.TropicalRainforest => TerrainClass.Jungle,
        Kind.TemperateRainforest => patch > TopNineTenths ? TerrainClass.Forest : TerrainClass.Plains,
        Kind.Taiga => patch > TopFourFifths ? TerrainClass.Taiga : TerrainClass.Plains,

        Kind.Tundra => TerrainClass.Arctic,
        Kind.Glacier => TerrainClass.Arctic,
        Kind.Wetland => TerrainClass.Wetlands,

        // Marine and Unknown never reach here — the classifier tests HasOpinion first and keeps its
        // own answer on both. Listed so the switch is total, not because it is relied on.
        _ => TerrainClass.Plains,
    };

    /// <summary>True where the export has actually said something about the vegetation.</summary>
    public static bool HasOpinion(Kind kind) => kind is not (Kind.Unknown or Kind.Marine);

    /// <summary>
    /// The Koppen zone this ground should be *painted* from, given what the export says grows on it.
    ///
    /// This exists because the terrain class is not the only thing that decides how a pixel looks.
    /// <see cref="Emit.TerrainPalette.Label"/> packs the terrain class together with a climate
    /// family derived from the Koppen zone, and it is the family that chooses the actual material
    /// set — gen_northern against gen_tropical against gen_desert. Trees and bridges read the zone
    /// the same way. While both came out of one Koppen classification the two could not disagree;
    /// importing the vegetation and leaving the zone alone broke that, and the result is a forest
    /// painted on steppe ground, which is exactly the kind of thing that reads wrong on the map
    /// without being traceable to any one wrong value.
    ///
    /// <b>Not a substitution — a constraint.</b> The zone is kept wherever the two can both be true,
    /// because Koppen is *finer* than an Azgaar biome through the whole temperate range: one
    /// "Temperate deciduous forest" covers ground our model separates into Mediterranean, oceanic
    /// and humid continental, and those paint from three different families. Overwriting the zone
    /// with a biome-derived guess would throw that away and flatten every temperate map. Only an
    /// outright contradiction moves, and it moves to the biome's own zone.
    /// </summary>
    public static KoppenClass Reconcile(Kind kind, KoppenClass zone)
        => !HasOpinion(kind) || Accepts(kind, zone) ? zone : Represents(kind);

    /// <summary>
    /// Whether this biome and this Koppen zone can describe the same ground.
    ///
    /// <b>The question is deliberately weak, and getting that wrong costs a quarter of the map.</b>
    /// It is not "would our model have produced this zone here" — that is a question about the
    /// climate model and the answer is often no on ground the export drew by hand. It is only
    /// "would painting this vegetation out of that material family look wrong", because the family
    /// is the entire visible consequence. A grassland is at home on anything from a Mediterranean
    /// hillside to a subarctic plain and paints acceptably from every one of those families; it is
    /// only bare desert and jungle floor underneath it that read as a mistake.
    ///
    /// The first cut of this table asked the strong question and refused 25.1% of Fleunland's land,
    /// most of it grassland our model called subarctic and savanna it called cold steppe — both of
    /// which paint perfectly well. Refusing them threw away a real distinction our model had made
    /// and the export had no opinion on, which is the failure this whole method exists to avoid.
    /// </summary>
    private static bool Accepts(Kind kind, KoppenClass zone) => kind switch
    {
        // True desert, and nothing green under it. Any of the arid or semi-arid families will do.
        Kind.HotDesert or Kind.ColdDesert
            => zone is KoppenClass.HotDesert or KoppenClass.ColdDesert
                    or KoppenClass.HotSteppe or KoppenClass.ColdSteppe,

        // Open dry grass with scattered trees: the dry families and the tropical dry ones, but not
        // bare desert under it and not the wet temperate greens.
        Kind.Savanna => zone is KoppenClass.HotSteppe or KoppenClass.ColdSteppe
                             or KoppenClass.TropicalSavanna or KoppenClass.TropicalMonsoon
                             or KoppenClass.Mediterranean,

        // Open ground, at home nearly everywhere. Only sand and jungle floor look wrong beneath it.
        Kind.Grassland => zone is not (KoppenClass.HotDesert or KoppenClass.ColdDesert
                                    or KoppenClass.TropicalRainforest or KoppenClass.TropicalMonsoon
                                    or KoppenClass.TropicalSavanna or KoppenClass.IceCap),

        Kind.TropicalSeasonalForest or Kind.TropicalRainforest
            => zone is KoppenClass.TropicalRainforest or KoppenClass.TropicalMonsoon
                    or KoppenClass.TropicalSavanna or KoppenClass.HumidSubtropical,

        // Broadleaf and mixed woodland. Subarctic is allowed: a temperate forest on cold ground
        // paints from the northern set, which is what vanilla paints Poland's forests from.
        Kind.TemperateForest or Kind.TemperateRainforest
            => zone is KoppenClass.Oceanic or KoppenClass.HumidSubtropical
                    or KoppenClass.Mediterranean or KoppenClass.HumidContinental
                    or KoppenClass.Subarctic,

        Kind.Taiga => zone is KoppenClass.Subarctic or KoppenClass.HumidContinental
                           or KoppenClass.Tundra or KoppenClass.Oceanic,
        Kind.Tundra => zone is KoppenClass.Tundra or KoppenClass.IceCap or KoppenClass.Subarctic,
        Kind.Glacier => zone is KoppenClass.IceCap or KoppenClass.Tundra,

        // Standing water and rank growth: anywhere it is neither frozen solid nor a desert.
        Kind.Wetland => zone is not (KoppenClass.HotDesert or KoppenClass.ColdDesert
                                  or KoppenClass.IceCap),

        _ => true,
    };

    /// <summary>The zone a biome falls back to when nothing our model said about it can stand.</summary>
    private static KoppenClass Represents(Kind kind) => kind switch
    {
        Kind.HotDesert => KoppenClass.HotDesert,
        Kind.ColdDesert => KoppenClass.ColdDesert,
        Kind.Savanna => KoppenClass.TropicalSavanna,
        Kind.Grassland => KoppenClass.ColdSteppe,
        Kind.TropicalSeasonalForest => KoppenClass.TropicalMonsoon,
        Kind.TropicalRainforest => KoppenClass.TropicalRainforest,
        Kind.TemperateForest => KoppenClass.Oceanic,
        Kind.TemperateRainforest => KoppenClass.Oceanic,
        Kind.Taiga => KoppenClass.Subarctic,
        Kind.Tundra => KoppenClass.Tundra,
        Kind.Glacier => KoppenClass.IceCap,
        Kind.Wetland => KoppenClass.Oceanic,
        _ => KoppenClass.Oceanic,
    };

    /// <summary>
    /// Desert enough that rock above the tree line is desert rock.
    ///
    /// Only the two deserts. A savanna mountain is a mountain — CK3's desert_mountains is the Atlas
    /// and the Zagros, not every dry range — and letting savanna in would paint a broad band of the
    /// tropics in the wrong material.
    /// </summary>
    public static bool IsArid(Kind kind) => kind is Kind.HotDesert or Kind.ColdDesert;

    /// <summary>
    /// Cold enough for permanent snow. Read as a floor rather than a verdict: the classifier ORs
    /// this with its own polar test instead of replacing it, because a peak is colder than the cell
    /// it stands in and our elevation resolves that where Azgaar's cells cannot.
    /// </summary>
    public static bool IsPolar(Kind kind) => kind is Kind.Tundra or Kind.Glacier;
}
