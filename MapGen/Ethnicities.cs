using Ck3MapGen.Config;
using Ck3MapGen.Core;
using static Ck3MapGen.Config.MapConfig;

namespace Ck3MapGen.MapGen;

public enum RaceArchetype
{
    Human,
    HighElf,
    WoodElf,
    Dwarf,
    Orc,
    Gnome,
    Giantkin,
    Deepkin,
    Exotic
}

/// <summary>
/// The layout of the mod's own <c>gfx/portraits/skin_palette.dds</c>, built by
/// tools/palettes/build_skin_palettes.py and shipped from BaseFilesToCopy/Core.
///
/// CK3 turns a <c>skin_color</c> rectangle into a UV lookup in that texture, and stock CK3
/// fills it with one continuous human gradient — there is nowhere in it for a green orc to
/// point. Our copy is the same 256x256, and stock ethnicities read the same texture, so it
/// only repaints coordinates no stock ethnicity samples: the generator rasterises every
/// <c>skin_color</c> rect in the game's own ethnicity files and refuses to write if a race
/// band overlaps one. Stock only claims about 46% of the palette; the race strips live in
/// the free left-hand column below the midtones.
///
/// That leaves human complexions entirely alone — generated humans emit no
/// <c>skin_color</c> at all and inherit it from their vanilla template. See
/// <see cref="Ethnicities.PickVanillaTemplate"/>.
///
/// Each race is painted <see cref="TiersPerRace"/> times over, once per
/// <see cref="FantasyRaceMode"/>, so the same orc is a faintly olive man on a low-fantasy
/// map and vividly green on a surreal one — see <see cref="TierOf"/>.
///
/// Every coordinate this file emits is derived from the constants below rather than written
/// out by hand, so the palette and the ethnicities cannot drift apart. They mirror
/// FANTASY_COL1 / FANTASY_ROW0 / TIER_ROWS / TIERS in the generator — change them together,
/// and reorder BANDS or TIERS there if you reorder <see cref="BandOf"/> or
/// <see cref="TierOf"/> here.
/// </summary>
internal static class SkinPalette
{
    private const int PaletteSize = 256;

    /// <summary>Last column of the free block. Stock's papuan rect starts at column 76.</summary>
    private const int FantasyCol1 = 73;

    /// <summary>First painted row. The stock rect above this one ends at row 102.</summary>
    private const int FantasyRow0 = 105;

    private const int TierRows = 6;

    /// <summary>How many intensity strips each race gets — one per fantasy race mode.</summary>
    private const int TiersPerRace = 3;

    /// <summary>
    /// How lurid a race's pigment is, chosen by the map's <see cref="FantasyRaceMode"/>. Each
    /// race is painted three times over: the low-fantasy strip sits close to a real human
    /// complexion of the same brightness, the exotic one is pushed well past it.
    /// <see cref="FantasyRaceMode.HumanOnly"/> generates no fantasy races at all, so its tier
    /// is never actually sampled.
    /// </summary>
    public static int TierOf(FantasyRaceMode mode) => mode switch
    {
        FantasyRaceMode.HighFantasy => 1,
        FantasyRaceMode.ExoticSurreal => 2,
        _ => 0
    };

    /// <summary>
    /// The band a race occupies, or -1 for <see cref="RaceArchetype.Human"/>, which has no
    /// band because it never overrides <c>skin_color</c> in the first place.
    /// </summary>
    public static int BandOf(RaceArchetype archetype) => archetype switch
    {
        RaceArchetype.HighElf => 0,
        RaceArchetype.WoodElf => 1,
        RaceArchetype.Dwarf => 2,
        RaceArchetype.Orc => 3,
        RaceArchetype.Gnome => 4,
        RaceArchetype.Giantkin => 5,
        RaceArchetype.Deepkin => 6,
        RaceArchetype.Exotic => 7,
        _ => -1
    };

    /// <summary>
    /// Maps a rectangle in a band's own space onto palette coordinates: <paramref name="u1"/>
    /// and <paramref name="u2"/> run across that race's hue variants, <paramref name="t1"/>
    /// and <paramref name="t2"/> from its lightest tone to its darkest. Both axes stop half a
    /// texel short of the painted edge, so bilinear filtering cannot reach the neighbouring
    /// band or the stock gradient beside it.
    /// </summary>
    public static (float X1, float Y1, float X2, float Y2) Swatch(
        int band, int tier, float u1, float t1, float u2, float t2)
    {
        const float n = PaletteSize - 1;
        float width = (FantasyCol1 - 0.5f) / n;
        int row0 = FantasyRow0 + (band * TiersPerRace + tier) * TierRows;
        float top = (row0 + 0.5f) / n;
        float span = (TierRows - 2f) / n;
        return (u1 * width, top + t1 * span, u2 * width, top + t2 * span);
    }
}

public sealed class GeneMorphEntry
{
    public required string SubGeneName { get; init; }
    public required float Min { get; init; }
    public required float Max { get; init; }
    public int Weight { get; init; } = 10;
}

public sealed class ColorPaletteRange
{
    public required float X1 { get; init; }
    public required float Y1 { get; init; }
    public required float X2 { get; init; }
    public required float Y2 { get; init; }
    public int Weight { get; init; } = 10;
}

public sealed class EthnicityDef
{
    public required string Key { get; init; }
    public required string LocalizedName { get; init; }
    public required RaceArchetype Archetype { get; init; }

    /// <summary>
    /// The vanilla ethnicity this inherits from — a real key from CK3's common/ethnicities.
    /// Humans override no <c>skin_color</c>, so whichever key this names *is* their
    /// complexion.
    /// </summary>
    public required string BaseTemplate { get; init; }

    /// <summary>
    /// Which broad look — caucasian, african, asian or mena — drives the hair, eye and
    /// accessory choices. <see cref="BaseTemplate"/> is one specific vanilla member of it.
    /// </summary>
    public required string LookFamily { get; init; }

    public Dictionary<string, List<GeneMorphEntry>> MorphGenes { get; } = [];
    public Dictionary<string, List<ColorPaletteRange>> ColorGenes { get; } = [];
}

public sealed class EthnicityMap
{
    public required Dictionary<string, EthnicityDef> Ethnicities { get; init; }
    public required Dictionary<Culture, EthnicityDef> ByCulture { get; init; }
    public required Dictionary<Heritage, EthnicityDef> ByHeritage { get; init; }
    public required Dictionary<string, EthnicityDef> ByCultureKey { get; init; }
    public required Dictionary<string, EthnicityDef> ByHeritageKey { get; init; }

    public EthnicityDef For(Culture culture)
    {
        if (ByCulture.TryGetValue(culture, out var eth))
            return eth;

        if (ByCultureKey.TryGetValue(culture.Key, out var keyEth))
            return keyEth;

        if (culture.Heritage is not null)
        {
            if (ByHeritage.TryGetValue(culture.Heritage, out var hEth))
                return hEth;

            if (ByHeritageKey.TryGetValue(culture.Heritage.Key, out var hKeyEth))
                return hKeyEth;
        }

        return Ethnicities.Values.FirstOrDefault(e => e.Archetype == RaceArchetype.Human)
            ?? Ethnicities.Values.First();
    }
}

public static class Ethnicities
{
    public static EthnicityMap Build(
        List<Heritage> heritages,
        List<Culture> cultures,
        TerrainClass[] provinceTerrain,
        MapConfig cfg,
        Rng rng)
    {
        var ethnicities = new Dictionary<string, EthnicityDef>(StringComparer.OrdinalIgnoreCase);
        var byCulture = new Dictionary<Culture, EthnicityDef>();
        var byHeritage = new Dictionary<Heritage, EthnicityDef>();
        var byCultureKey = new Dictionary<string, EthnicityDef>(StringComparer.OrdinalIgnoreCase);
        var byHeritageKey = new Dictionary<string, EthnicityDef>(StringComparer.OrdinalIgnoreCase);

        int ethIndex = 0;

        // 1. Determine Archetypes with Guaranteed Variety Guarantee
        var heritageArchetypes = AssignDiverseArchetypes(heritages, cultures, provinceTerrain, cfg, rng);

        // 2. Generate Heritage Ethnicities
        foreach (var heritage in heritages)
        {
            var archetype = heritageArchetypes.GetValueOrDefault(heritage, RaceArchetype.Human);
            var heritageEth = CreateEthnicity($"gen_ethnicity_{ethIndex++}", archetype, heritage.Name, cfg.RaceMode, rng);

            ethnicities[heritageEth.Key] = heritageEth;
            byHeritage[heritage] = heritageEth;
            byHeritageKey[heritage.Key] = heritageEth;
        }

        // 3. Track unique assigned races so far
        var usedArchetypes = new HashSet<RaceArchetype>(heritageArchetypes.Values);

        // 4. Assign Culture Ethnicities
        foreach (var culture in cultures)
        {
            Heritage? heritage = culture.Heritage ?? heritages.FirstOrDefault(h => culture.Heritage != null && h.Key == culture.Heritage.Key) ?? heritages.FirstOrDefault();
            EthnicityDef? heritageEth = null;

            if (heritage != null)
            {
                byHeritage.TryGetValue(heritage, out heritageEth);
                if (heritageEth == null) byHeritageKey.TryGetValue(heritage.Key, out heritageEth);
            }

            EthnicityDef cultureEth;

            // A culture the export tagged with a different race than its heritage keeps its own
            // body. TieRaceToHeritage exists to stop *generated* cultures scattering races at
            // random inside one people; an export that drew an orcish enclave inside elvish ground
            // is not scatter, it is the map, and the tie yields to it.
            var importedRace = culture.ImportedArchetype is { } tagged
                && cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly
                && (cfg.RaceMode == FantasyRaceMode.ExoticSurreal || tagged != RaceArchetype.Exotic)
                    ? culture.ImportedArchetype
                    : null;

            if (importedRace is { } race && race != (heritageEth?.Archetype ?? RaceArchetype.Human))
            {
                cultureEth = CreateEthnicity($"gen_ethnicity_{ethIndex++}", race, culture.Name, cfg.RaceMode, rng);
                ethnicities[cultureEth.Key] = cultureEth;
                usedArchetypes.Add(race);
            }
            else if (cfg.TieRaceToHeritage && heritageEth != null)
            {
                cultureEth = heritageEth;
            }
            else
            {
                RaceArchetype subArchetype;

                // If we still haven't met the quota (e.g. very few heritages), guarantee it at culture level
                int targetQuota = Math.Max(1, cfg.GuaranteedRaceCount);
                if (usedArchetypes.Count < targetQuota && cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly)
                {
                    var available = Enum.GetValues<RaceArchetype>()
                        .Cast<RaceArchetype>()
                        .Where(a => !usedArchetypes.Contains(a) && (cfg.RaceMode == FantasyRaceMode.ExoticSurreal || a != RaceArchetype.Exotic))
                        .ToList();

                    subArchetype = available.Count > 0 ? rng.Pick(available) : PickArchetypeForCulture(culture, provinceTerrain, cfg, rng);
                }
                else
                {
                    var baseArchetype = heritageEth?.Archetype ?? RaceArchetype.Human;
                    subArchetype = rng.Chance(0.70) ? baseArchetype : PickArchetypeForCulture(culture, provinceTerrain, cfg, rng);
                }

                cultureEth = CreateEthnicity($"gen_ethnicity_{ethIndex++}", subArchetype, culture.Name, cfg.RaceMode, rng);
                ethnicities[cultureEth.Key] = cultureEth;
                usedArchetypes.Add(subArchetype);
            }

            byCulture[culture] = cultureEth;
            byCultureKey[culture.Key] = cultureEth;
        }

        var tallies = byCulture.Values
            .GroupBy(e => e.Archetype)
            .Select(g => $"{g.Count()} {g.Key}");

        Console.WriteLine($"  ethnicities: {byCulture.Count} cultures across {usedArchetypes.Count} distinct races -> {string.Join(", ", tallies)}");

        // Delivering fewer races than asked for used to be silent, which made a clipped quota
        // look like bad luck in the seed. Say which constraint actually bound.
        int wanted = Math.Max(1, cfg.GuaranteedRaceCount);
        if (cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly
            && usedArchetypes.Count < wanted)
        {
            string reason = cfg.RaceTerrain == RaceTerrainRule.Require
                ? "RaceTerrain is Require, so races with no suitable terrain anywhere on this map were left unplaced rather than misplaced — set it to Prefer to settle them anyway"
                : !cfg.TieRaceToHeritage && byCulture.Count < wanted
                ? $"only {byCulture.Count} culture(s) exist — lower CountiesPerCulture to make more"
                : heritages.Count < wanted
                    ? $"only {heritages.Count} heritage(s) exist — lower CulturesPerHeritage or CountiesPerCulture to make more"
                    : "the candidate pool ran out — ExoticSurreal adds a ninth race";
            Console.WriteLine($"  WARNING: asked for {wanted} distinct races but delivered {usedArchetypes.Count}: {reason}");
        }

        return new EthnicityMap
        {
            Ethnicities = ethnicities,
            ByCulture = byCulture,
            ByHeritage = byHeritage,
            ByCultureKey = byCultureKey,
            ByHeritageKey = byHeritageKey
        };
    }

    private static Dictionary<Heritage, RaceArchetype> AssignDiverseArchetypes(
        List<Heritage> heritages,
        List<Culture> cultures,
        TerrainClass[] provinceTerrain,
        MapConfig cfg,
        Rng rng)
    {
        var assignments = new Dictionary<Heritage, RaceArchetype>();
        if (heritages.Count == 0) return assignments;

        if (!cfg.EnableFantasyEthnicities || cfg.RaceMode == FantasyRaceMode.HumanOnly)
        {
            foreach (var h in heritages) assignments[h] = RaceArchetype.Human;
            return assignments;
        }

        // Terrain make-up per heritage, as a share of its baronies. Scoring against the whole
        // profile rather than the single modal terrain is what lets a mountain-loving race
        // claim a heritage that is only *partly* mountainous — see HeritageAffinity.
        var heritageTerrain = new Dictionary<Heritage, Dictionary<TerrainClass, double>>();
        foreach (var heritage in heritages)
        {
            var heritageCultures = cultures.Where(c => c.Heritage == heritage || (c.Heritage != null && c.Heritage.Key == heritage.Key)).ToList();
            heritageTerrain[heritage] = GetTerrainShares(heritageCultures, provinceTerrain, rng);
        }

        // Pool of available candidate races
        var candidatePool = new List<RaceArchetype>
        {
            RaceArchetype.Human,
            RaceArchetype.Dwarf,
            RaceArchetype.WoodElf,
            RaceArchetype.HighElf,
            RaceArchetype.Orc,
            RaceArchetype.Gnome,
            RaceArchetype.Giantkin,
            RaceArchetype.Deepkin
        };

        if (cfg.RaceMode == FantasyRaceMode.ExoticSurreal)
            candidatePool.Add(RaceArchetype.Exotic);

        // Calculate how many distinct races we must guarantee
        int targetUnique = Math.Clamp(cfg.GuaranteedRaceCount, 1, Math.Min(heritages.Count, candidatePool.Count));
        var remainingHeritages = new List<Heritage>(heritages);
        rng.Shuffle(remainingHeritages);

        var assignedRaces = new HashSet<RaceArchetype>();

        // 0. Imported Phase: heritages the export tagged with a race take that race outright,
        //    before the terrain greedy sees them. The affinity machinery below exists to *guess*
        //    which peoples are dwarves; when a fantasy-preset export has already said so, guessing
        //    over it would put the mountain folk the map was drawn around on the wrong bodies.
        //    Only races the candidate pool permits are honoured — an Exotic tag on a low-fantasy
        //    map falls through to the guess, which is the mode doing its job, not a loss.
        int importedCount = 0;
        foreach (var h in heritages)
        {
            if (h.ImportedArchetype is not { } race || !candidatePool.Contains(race)) continue;

            assignments[h] = race;
            assignedRaces.Add(race);
            remainingHeritages.Remove(h);
            importedCount++;
        }

        if (importedCount > 0)
            Console.WriteLine($"  ethnicities: {importedCount} of {heritages.Count} heritages took " +
                              $"their race from the export's tags");

        // 1. Guaranteed Diversity Phase: Pair each unique race to its highest-affinity available heritage
        for (int i = 0; i < targetUnique && remainingHeritages.Count > 0; i++)
        {
            var unassignedRaces = candidatePool.Where(r => !assignedRaces.Contains(r)).ToList();
            if (unassignedRaces.Count == 0) break;

            // Find (Heritage, Race) pair with best terrain synergy score
            Heritage bestHeritage = remainingHeritages[0];
            RaceArchetype bestRace = unassignedRaces[0];
            double bestScore = double.MinValue;
            bool found = false;

            foreach (var h in remainingHeritages)
            {
                var shares = heritageTerrain[h];
                foreach (var race in unassignedRaces)
                {
                    // Require refuses the pair outright rather than scoring it low, which is what
                    // separates it from Prefer: an unsuited race is not merely unlikely here, it is
                    // ineligible, and if no heritage will take it the race goes unplaced.
                    if (cfg.RaceTerrain == RaceTerrainRule.Require && !FitsTerrain(race, shares))
                        continue;

                    // Ignore flattens the affinity term so only the jitter is left, which turns the
                    // greedy into a random pairing without needing a second code path for it.
                    // The jitter only breaks ties inside an affinity step, never across one.
                    double affinity = cfg.RaceTerrain == RaceTerrainRule.Ignore
                        ? 0.0
                        : HeritageAffinity(race, shares);

                    double score = affinity + rng.Double(0.0, 0.3);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestHeritage = h;
                        bestRace = race;
                        found = true;
                    }
                }
            }

            // Under Require this is the map telling us it has no room for anything left in the
            // pool. Stopping hands the remaining heritages to the terrain roll below, which will
            // settle them on races that do fit, and the shortfall is reported by the caller.
            if (!found) break;

            assignments[bestHeritage] = bestRace;
            assignedRaces.Add(bestRace);
            remainingHeritages.Remove(bestHeritage);
        }

        // 2. Remainder Phase: Remaining heritages roll probabilistically according to their biomes
        foreach (var h in remainingHeritages)
        {
            assignments[h] = PickWeightedArchetype(heritageTerrain[h], cfg, rng);
        }

        return assignments;
    }

    /// <summary>Terrain the heritage has most of — the old modal reading, kept for the remainder roll.</summary>
    private static TerrainClass DominantOf(Dictionary<TerrainClass, double> shares) =>
        shares.OrderByDescending(kv => kv.Value).First().Key;

    /// <summary>A race needs at least this share of a heritage before its terrain counts at all.</summary>
    private const double AffinityMinShare = 0.05;

    /// <summary>...and this much before the terrain scores its full affinity.</summary>
    private const double AffinityFullShare = 0.35;

    /// <summary>
    /// How well a race suits a heritage, given the heritage's whole terrain make-up.
    ///
    /// Taking the *modal* terrain instead — which is what this used to do — is why dwarves,
    /// giantkin and deepkin went missing. A heritage spans dozens of counties, so its mode is
    /// almost always whichever terrain is commonest map-wide (plains, farmlands, forest), and
    /// every race whose affinities are for minority terrain scored the <c>_ => 1</c> floor no
    /// matter how much mountain or wetland it actually contained. Those races then sorted to
    /// the tail of the greedy and fell off the end whenever the quota was short.
    ///
    /// Scoring against presence fixes that: a heritage that is a fifth mountains is a real
    /// home for dwarves even when four fifths of it is grassland.
    /// </summary>
    private static double HeritageAffinity(RaceArchetype race, Dictionary<TerrainClass, double> shares)
    {
        double best = 1.0; // the floor an unmatched race scores
        foreach (var (terrain, share) in shares)
        {
            int affinity = GetTerrainAffinityScore(race, terrain);
            if (affinity <= 1) continue;

            best = Math.Max(best, 1.0 + (affinity - 1.0) * Reach(share));
        }
        return best;
    }

    /// <summary>
    /// How much of a region has to be terrain a race wants before
    /// <see cref="RaceTerrainRule.Require"/> will let that race settle there — expressed as a
    /// position on the same <see cref="AffinityMinShare"/>..<see cref="AffinityFullShare"/> ramp
    /// <see cref="HeritageAffinity"/> scores against, so the bar means the same thing for a race
    /// whose best terrain is worth 12 as for one whose best is worth 10.
    ///
    /// At the default ramp, 0.5 lands on a fifth of the region: enough that a mountainous
    /// quarter of a mostly-grassland heritage is still a home for dwarves, but a heritage with
    /// one mountain barony in forty is not.
    /// </summary>
    private const double RequiredReach = 0.5;

    /// <summary>Where a terrain share sits on the affinity ramp, 0 (irrelevant) to 1 (full).</summary>
    private static double Reach(double share) =>
        Math.Clamp((share - AffinityMinShare) / (AffinityFullShare - AffinityMinShare), 0.0, 1.0);

    /// <summary>
    /// Whether a race has enough of the terrain it wants here to settle under
    /// <see cref="RaceTerrainRule.Require"/>.
    ///
    /// <see cref="RaceArchetype.Human"/> always passes: humans are the fallback every other
    /// branch falls back *to*, so blocking them on a map whose terrain none of their affinities
    /// cover would leave a region with nobody to put in it.
    /// <see cref="RaceArchetype.Exotic"/> always passes because it is defined as the race that
    /// belongs nowhere in particular.
    /// </summary>
    private static bool FitsTerrain(RaceArchetype race, Dictionary<TerrainClass, double> shares)
    {
        if (race is RaceArchetype.Human or RaceArchetype.Exotic) return true;

        foreach (var (terrain, share) in shares)
            if (GetTerrainAffinityScore(race, terrain) > 1 && Reach(share) >= RequiredReach)
                return true;

        return false;
    }

    /// <summary>
    /// The fantasy races a terrain roll may land on, Human and Exotic excluded — both are
    /// decided before this point by their own rolls rather than competing on terrain.
    /// </summary>
    private static readonly RaceArchetype[] TerrainRaces =
    [
        RaceArchetype.Dwarf, RaceArchetype.HighElf, RaceArchetype.WoodElf, RaceArchetype.Orc,
        RaceArchetype.Gnome, RaceArchetype.Giantkin, RaceArchetype.Deepkin
    ];

    /// <summary>Every race a culture-level roll may produce, Exotic excluded.</summary>
    private static readonly RaceArchetype[] CultureRaces =
    [
        RaceArchetype.Human, RaceArchetype.Dwarf, RaceArchetype.HighElf, RaceArchetype.WoodElf,
        RaceArchetype.Orc, RaceArchetype.Gnome, RaceArchetype.Giantkin, RaceArchetype.Deepkin
    ];

    /// <summary>
    /// A race drawn in proportion to how well the land suits it. The affinity floor is 1.0 rather
    /// than 0, so an unsuited race keeps a small chance — which is the whole difference between
    /// <see cref="RaceTerrainRule.Prefer"/> and <see cref="RaceTerrainRule.Require"/>.
    /// </summary>
    private static RaceArchetype PickByAffinity(
        IReadOnlyList<RaceArchetype> pool, Dictionary<TerrainClass, double> shares, Rng rng)
    {
        var weights = new double[pool.Count];
        double total = 0.0;
        for (int i = 0; i < pool.Count; i++)
        {
            weights[i] = HeritageAffinity(pool[i], shares);
            total += weights[i];
        }

        double roll = rng.Double(0.0, total);
        for (int i = 0; i < pool.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0.0) return pool[i];
        }
        return pool[^1];
    }

    /// <summary>
    /// What fraction of a heritage's baronies sits on each terrain. Shares rather than a
    /// single winner, because <see cref="HeritageAffinity"/> needs to see the minority terrain
    /// a race might actually want.
    /// </summary>
    private static Dictionary<TerrainClass, double> GetTerrainShares(
        List<Culture> cultures, TerrainClass[] provinceTerrain, Rng rng)
    {
        var terrainCounts = new Dictionary<TerrainClass, int>();
        int total = 0;
        foreach (var culture in cultures)
        {
            if (culture.Counties == null) continue;
            foreach (var county in culture.Counties)
            {
                foreach (var barony in county.Children)
                {
                    if (barony.ProvinceId > 0 && barony.ProvinceId < provinceTerrain.Length)
                    {
                        var t = provinceTerrain[barony.ProvinceId];
                        terrainCounts[t] = terrainCounts.GetValueOrDefault(t) + 1;
                        total++;
                    }
                }
            }
        }

        if (total == 0)
        {
            var fallback = rng.Pick([TerrainClass.Mountains, TerrainClass.Hills, TerrainClass.Forest,
                                     TerrainClass.Desert, TerrainClass.Plains, TerrainClass.Arctic]);
            return new Dictionary<TerrainClass, double> { [fallback] = 1.0 };
        }

        return terrainCounts.ToDictionary(kv => kv.Key, kv => kv.Value / (double)total);
    }

    private static int GetTerrainAffinityScore(RaceArchetype archetype, TerrainClass terrain)
    {
        return (archetype, terrain) switch
        {
            (RaceArchetype.Dwarf, TerrainClass.Mountains or TerrainClass.DesertMountains or TerrainClass.Hills) => 12,
            (RaceArchetype.WoodElf, TerrainClass.Forest or TerrainClass.Taiga or TerrainClass.Jungle) => 12,
            (RaceArchetype.HighElf, TerrainClass.Plains or TerrainClass.Farmlands or TerrainClass.Floodplains) => 10,
            (RaceArchetype.Orc, TerrainClass.Mountains or TerrainClass.Desert or TerrainClass.Hills or TerrainClass.Steppe) => 10,
            (RaceArchetype.Gnome, TerrainClass.Wetlands or TerrainClass.Desert or TerrainClass.Hills) => 11,
            (RaceArchetype.Giantkin, TerrainClass.Arctic or TerrainClass.Mountains) => 12,
            (RaceArchetype.Deepkin, TerrainClass.Wetlands or TerrainClass.Arctic or TerrainClass.Desert) => 10,
            (RaceArchetype.Human, TerrainClass.Plains or TerrainClass.Farmlands or TerrainClass.Hills) => 8,
            _ => 1
        };
    }

    /// <summary>
    /// The race for a heritage the diversity phase did not claim.
    ///
    /// <see cref="RaceTerrainRule.Prefer"/> keeps the hand-written table below, which is keyed on
    /// the heritage's *modal* terrain and so carries flavour the raw affinity scores do not — a
    /// desert may throw up a high elf, which no affinity score would allow. Require cannot use it,
    /// because the mode hides exactly the minority terrain a race needs (the reason
    /// <see cref="HeritageAffinity"/> scores shares instead), so it goes to the shares directly.
    /// </summary>
    private static RaceArchetype PickWeightedArchetype(
        Dictionary<TerrainClass, double> shares, MapConfig cfg, Rng rng)
    {
        double fantasyChance = cfg.RaceMode switch
        {
            FantasyRaceMode.LowFantasy => 0.35,
            FantasyRaceMode.HighFantasy => 0.75,
            FantasyRaceMode.ExoticSurreal => 0.90,
            _ => 0.0
        };

        if (!rng.Chance(fantasyChance))
            return RaceArchetype.Human;

        if (cfg.RaceMode == FantasyRaceMode.ExoticSurreal && rng.Chance(0.25))
            return RaceArchetype.Exotic;

        if (cfg.RaceTerrain == RaceTerrainRule.Ignore)
            return rng.Pick(TerrainRaces);

        if (cfg.RaceTerrain == RaceTerrainRule.Require)
        {
            var fitting = TerrainRaces.Where(r => FitsTerrain(r, shares)).ToList();
            return fitting.Count > 0 ? rng.Pick(fitting) : RaceArchetype.Human;
        }

        return DominantOf(shares) switch
        {
            TerrainClass.Mountains or TerrainClass.DesertMountains
                => rng.Pick([RaceArchetype.Dwarf, RaceArchetype.Orc, RaceArchetype.Giantkin]),

            TerrainClass.Hills
                => rng.Pick([RaceArchetype.Dwarf, RaceArchetype.Orc, RaceArchetype.Gnome, RaceArchetype.Human]),

            TerrainClass.Forest or TerrainClass.Taiga or TerrainClass.Jungle
                => rng.Pick([RaceArchetype.WoodElf, RaceArchetype.Gnome, RaceArchetype.Orc]),

            TerrainClass.Arctic
                => rng.Pick([RaceArchetype.Giantkin, RaceArchetype.Deepkin, RaceArchetype.Dwarf]),

            TerrainClass.Desert
                => rng.Pick([RaceArchetype.Orc, RaceArchetype.Gnome, RaceArchetype.Deepkin, RaceArchetype.HighElf]),

            TerrainClass.Wetlands
                => rng.Pick([RaceArchetype.Gnome, RaceArchetype.Deepkin, RaceArchetype.WoodElf]),

            _ => rng.Pick([RaceArchetype.HighElf, RaceArchetype.Giantkin, RaceArchetype.Orc, RaceArchetype.Human, RaceArchetype.Dwarf])
        };
    }

    /// <summary>
    /// The race for a single culture, used when cultures carry their own race rather than their
    /// heritage's, and as the fallback when the diversity quota has nothing left to hand out.
    ///
    /// This scores against the culture's own counties rather than its heritage's, which is the
    /// point of a culture-level race: a forest duchy inside a mountain heritage can be the wood
    /// elves. Note that <see cref="RaceTerrainRule.Ignore"/> is what this did unconditionally
    /// before the rule existed — it was the one place terrain was disregarded entirely.
    /// </summary>
    private static RaceArchetype PickArchetypeForCulture(
        Culture culture, TerrainClass[] provinceTerrain, MapConfig cfg, Rng rng)
    {
        if (!cfg.EnableFantasyEthnicities || cfg.RaceMode == FantasyRaceMode.HumanOnly)
            return RaceArchetype.Human;

        if (cfg.RaceTerrain == RaceTerrainRule.Ignore)
            return rng.Pick(CultureRaces);

        var shares = GetTerrainShares([culture], provinceTerrain, rng);

        if (cfg.RaceTerrain == RaceTerrainRule.Require)
        {
            var fitting = CultureRaces.Where(r => FitsTerrain(r, shares)).ToList();
            return fitting.Count > 0 ? rng.Pick(fitting) : RaceArchetype.Human;
        }

        return PickByAffinity(CultureRaces, shares, rng);
    }

    private static EthnicityDef CreateEthnicity(
        string key,
        RaceArchetype archetype,
        string name,
        FantasyRaceMode mode,
        Rng rng)
    {
        string family = archetype switch
        {
            RaceArchetype.Orc or RaceArchetype.Gnome => "asian",
            RaceArchetype.Deepkin => "african",
            RaceArchetype.WoodElf or RaceArchetype.HighElf => "caucasian",
            RaceArchetype.Dwarf or RaceArchetype.Giantkin => "caucasian",
            _ => rng.Pick(["caucasian", "african", "asian", "mena"])
        };

        var def = new EthnicityDef
        {
            Key = key,
            LocalizedName = archetype == RaceArchetype.Human ? name : $"{name} ({RaceName(archetype)})",
            Archetype = archetype,
            LookFamily = family,
            BaseTemplate = PickVanillaTemplate(family, rng)
        };

        ApplyMorphGenes(def, archetype, mode, rng);
        ApplyColorGenes(def, archetype, family, mode, rng);

        return def;
    }

    /// <summary>
    /// A concrete vanilla ethnicity for a look family to inherit from.
    ///
    /// Humans emit no <c>skin_color</c> of their own — they take the template's, which is how
    /// they keep stock complexions while the fantasy races repaint theirs. That makes this
    /// pick the whole of a human's colouring, so each family spreads across the stock
    /// ethnicities that share its look rather than collapsing onto one key.
    ///
    /// Note there is no <c>mena</c> ethnicity in CK3 — that look lives under arab, turkic and
    /// the Indian keys, so "mena" is only ever a family name here, never a template.
    /// </summary>
    private static string PickVanillaTemplate(string family, Rng rng) => family switch
    {
        "african" => rng.Pick(["african", "east_african", "papuan"]),
        "asian" => rng.Pick(["asian", "asian_han_chinese", "asian_mongol", "asian_malay", "asian_austronesian"]),
        "mena" => rng.Pick(["arab", "turkic", "turkic_west", "indian", "south_indian"]),
        _ => rng.Pick(["caucasian", "slavic", "byzantine", "mediterranean", "circumpolar"])
    };

    /// <summary>
    /// The shape of a race, expressed only in genes CK3 actually reads.
    ///
    /// **Two value conventions, and mixing them up silently inverts a feature.** Confirmed against
    /// the <c>setting</c> blocks in <c>game/common/genes/01_genes_morph.txt</c>:
    ///
    /// - <c>gene_bs_*</c> genes drive a blend shape directly: <c>value = { min = 0.0 max = 1.0 }</c>,
    ///   so **0 is neutral and 1 is full strength**, and <c>_neg</c>/<c>_pos</c> are two separate
    ///   mirrored shapes rather than two ends of one axis. Big ears are <c>ear_size_pos</c> at 0.9;
    ///   small ears are <c>ear_size_neg</c> at 0.9. Writing 0.5 here is a half-strength feature, not
    ///   an average one.
    /// - Every other gene maps its value onto an attribute over <c>{ -0.5, 0.499 }</c>, so **0.5 is
    ///   neutral**, below it is the negative direction and above it the positive. Vanilla's own
    ///   convention is to name the <c>_neg</c> template when you sit under 0.5 and <c>_pos</c> when
    ///   you sit over it, and this follows that.
    ///
    /// <c>gene_bs_body_type</c> is the exception that proves the rule: it is <c>bs_</c>-named but
    /// curve-driven with 0.5 neutral, so <see cref="NeutralOf"/> special-cases it.
    ///
    /// **Height and dwarfism are the same slider.** <c>normal_height</c> sets <c>body_height</c>
    /// *and* blends in <c>bs_dwarf_1</c> — CK3's achondroplasia shape, the one the Dwarf trait uses —
    /// on a curve that reaches full strength at 0 and zero at 0.5. So a value of 0.05 is not "very
    /// short", it is "short-limbed with an enlarged head". No vanilla human ethnicity goes below
    /// 0.30. That is why <see cref="RaceArchetype.Gnome"/> sits at the bottom of the ramp on purpose
    /// and <see cref="RaceArchetype.Dwarf"/> deliberately does not: a dwarf is short and *broad*,
    /// which is <c>gene_bs_body_shape</c> and <c>gene_bs_body_type</c>, not a height value.
    /// </summary>
    /// <summary>
    /// The race as a player reads it. The enum spellings go straight into a localisation string,
    /// and "HighElf" is not a word.
    /// </summary>
    private static string RaceName(RaceArchetype archetype) => archetype switch
    {
        RaceArchetype.HighElf => "High Elf",
        RaceArchetype.WoodElf => "Wood Elf",
        _ => archetype.ToString()
    };

    private static void ApplyMorphGenes(EthnicityDef def, RaceArchetype archetype, FantasyRaceMode mode, Rng rng)
    {
        float f = MorphIntensity(mode);

        switch (archetype)
        {
            case RaceArchetype.HighElf:
                // Tall, narrow and unmuscled. The height alone does not read as elven — it is the
                // absence of bulk beside it that does.
                Shape(def, rng, f, "gene_height", "normal_height", 0.60f, 0.70f);
                Shape(def, rng, f, "gene_bs_body_type", "body_fat_head_fat_low", 0.26f, 0.40f);
                Shape(def, rng, Untiered, "gene_bs_body_shape", "body_shape_rectangle_half", 0.02f, 0.16f);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.65f, 0.90f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_neg", 0.20f, 0.40f);
                // Ears swept up and back, NOT enlarged and NOT pushed off the skull. Vanilla's ear
                // genes make a round ear bigger and splay it outward; pushing all four toward 1.0
                // gets a comic ear rather than an elegant one, so size and outward stay low while
                // angle and bend — the two that sweep it — carry the shape.
                Shape(def, rng, f, "gene_bs_ear_angle", "ear_angle_pos", 0.80f, 1.0f);
                Shape(def, rng, f, "gene_bs_ear_bend", "ear_both_bend_pos", 0.85f, 1.0f);
                Shape(def, rng, f, "gene_bs_ear_outward", "ear_outward_pos", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_bs_ear_size", "ear_size_pos", 0.30f, 0.50f);
                // Upswept eyes are the strongest elf cue stock geometry has after height, so the
                // high elf takes it harder than the wood elf does.
                Shape(def, rng, f, "gene_eye_angle", "eye_angle_pos", 0.70f, 0.95f);
                Shape(def, rng, f, "gene_eye_distance", "eye_distance_neg", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_bs_eye_fold_shape", "eye_fold_shape_02_pos", 0.40f, 0.70f);
                Shape(def, rng, f, "gene_head_height", "head_height_pos", 0.62f, 0.85f);
                Shape(def, rng, f, "gene_forehead_height", "forehead_height_pos", 0.62f, 0.85f);
                Shape(def, rng, f, "gene_forehead_brow_height", "forehead_brow_height_pos", 0.65f, 0.85f);
                Shape(def, rng, f, "gene_bs_forehead_brow_curve", "forehead_brow_curve_pos", 0.55f, 0.80f);
                Shape(def, rng, f, "gene_bs_cheek_forward", "cheek_forward_pos", 0.60f, 0.85f);
                Shape(def, rng, f, "gene_bs_cheek_height", "cheek_height_pos", 0.55f, 0.80f);
                Shape(def, rng, f, "gene_jaw_width", "jaw_width_neg", 0.15f, 0.35f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_neg", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_bs_nose_length", "nose_length_pos", 0.35f, 0.55f);
                // gene_bs_nose_profile has no "straight" template — only _neg, _pos, and the two
                // hawk variants — so the straight elven nose is a *weak* _pos rather than a template
                // of its own. On a bs gene the neutral end is 0, not 0.5, so this range is correct.
                Shape(def, rng, f, "gene_bs_nose_profile", "nose_profile_pos", 0.15f, 0.35f);
                // Ages slowly rather than not at all. `no_aging` is literally an empty template, so
                // at high weight a high elf who reigns for sixty years never changes face, which
                // costs the player a cue they actually read.
                AddGene(def, "gene_age", "old_beauty_1", 0.0f, 0.6f, weight: 65);
                AddGene(def, "gene_age", "no_aging", 0.0f, 1.0f, weight: 35);
                AddGene(def, "gene_eyebrows_shape", "close_spacing_low_thickness", 0.0f, 1.0f);
                AddGene(def, "gene_eyebrows_fullness", "layer_2_low_thickness", 0.0f, 1.0f);
                AddGene(def, "complexion", "complexion_smooth_1", 0.30f, 0.60f);
                AddGene(def, "gene_body_hair", "body_hair_sparse", 0.10f, 0.40f);
                AddGene(def, "gene_baldness", "no_baldness", 0.0f, 0.15f);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 70);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 30);
                break;

            case RaceArchetype.WoodElf:
                // Human height, and a hunter rather than an aristocrat — broader skull, sharper
                // cheekbones and appreciably more muscle than the high elf carries.
                Shape(def, rng, f, "gene_height", "normal_height", 0.46f, 0.56f);
                Shape(def, rng, f, "gene_bs_body_type", "body_fat_head_fat_low", 0.32f, 0.46f);
                Shape(def, rng, Untiered, "gene_bs_body_shape", "body_shape_triangle_half", 0.35f, 0.55f);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.50f, 0.75f);
                Shape(def, rng, f, "gene_bs_ear_angle", "ear_angle_pos", 0.70f, 0.95f);
                Shape(def, rng, f, "gene_bs_ear_bend", "ear_both_bend_pos", 0.75f, 0.95f);
                Shape(def, rng, f, "gene_bs_ear_outward", "ear_outward_pos", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_bs_ear_size", "ear_size_pos", 0.25f, 0.45f);
                // Slanted eyes come from gene_eye_angle alone. There is no gene_bs_eye_slant in
                // vanilla — nothing matching "slant" exists at all.
                Shape(def, rng, f, "gene_eye_angle", "eye_angle_pos", 0.55f, 0.75f);
                Shape(def, rng, f, "gene_bs_eye_size", "eye_size_pos", 0.45f, 0.70f);
                Shape(def, rng, f, "gene_head_width", "head_width_pos", 0.55f, 0.72f);
                Shape(def, rng, f, "gene_jaw_width", "jaw_width_neg", 0.25f, 0.45f);
                Shape(def, rng, f, "gene_bs_cheek_forward", "cheek_forward_pos", 0.45f, 0.70f);
                Shape(def, rng, f, "gene_bs_cheek_height", "cheek_height_pos", 0.70f, 0.95f);
                Shape(def, rng, f, "gene_bs_nose_size", "nose_size_neg", 0.45f, 0.70f);
                Shape(def, rng, f, "gene_bs_nose_ridge_angle", "nose_ridge_angle_pos", 0.45f, 0.70f);
                AddGene(def, "gene_age", "old_beauty_1", 0.0f, 0.7f, weight: 70);
                AddGene(def, "gene_age", "no_aging", 0.0f, 1.0f, weight: 30);
                AddGene(def, "gene_eyebrows_fullness", "layer_2_avg_thickness", 0.0f, 1.0f);
                // The lightest and blotchiest of the numbered head textures: +2.4 lightness and
                // +1.2 unevenness against the base, which is as close to freckled as stock gets.
                AddGene(def, "complexion", "complexion_2", 0.35f, 0.70f);
                AddGene(def, "gene_body_hair", "body_hair_sparse", 0.35f, 0.65f);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 45);
                AddGene(def, "gene_hair_type", "hair_curly", 0.0f, 1.0f, weight: 30);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 25);
                break;

            case RaceArchetype.Dwarf:
                // Short but PROPORTIONATE, which is the whole trick. 0.34-0.44 keeps bs_dwarf_1
                // down at 0.12-0.32 — visibly short, still built like an adult — and the mass that
                // makes a dwarf a dwarf comes from the two body genes underneath instead. Dropping
                // to the old 0.02-0.10 put bs_dwarf_1 at ~0.90 and made this indistinguishable from
                // the gnome, which really does want the bottom of the ramp.
                Shape(def, rng, f, "gene_height", "normal_height", 0.34f, 0.44f);
                Shape(def, rng, f, "gene_bs_body_type", "body_fat_head_fat_medium", 0.60f, 0.75f);
                Shape(def, rng, Untiered, "gene_bs_body_shape", "body_shape_rectangle_full", 0.75f, 1.0f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_pos", 0.85f, 1.0f);
                Shape(def, rng, f, "gene_neck_length", "neck_length_neg", 0.05f, 0.25f);
                Shape(def, rng, f, "gene_head_width", "head_width_pos", 0.70f, 0.95f);
                Shape(def, rng, f, "gene_jaw_width", "jaw_width_pos", 0.80f, 1.0f);
                Shape(def, rng, f, "gene_jaw_forward", "jaw_forward_pos", 0.55f, 0.85f);
                Shape(def, rng, f, "gene_bs_jaw_def", "jaw_def_pos", 0.60f, 0.90f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_pos", 0.75f, 0.95f);
                Shape(def, rng, f, "gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.65f, 0.90f);
                Shape(def, rng, f, "gene_bs_ear_size", "ear_size_neg", 0.35f, 0.55f);
                Shape(def, rng, f, "gene_bs_nose_length", "nose_length_pos", 0.50f, 0.80f);
                Shape(def, rng, f, "gene_bs_nose_size", "nose_size_pos", 0.55f, 0.80f);
                // The weathering is carried by these three, not by `complexion`. They are genuine
                // normal-map decals whose range drives their alpha, so the strength is real.
                AddGene(def, "face_detail_temple_def", "temple_def", 0.40f, 0.90f);
                AddGene(def, "expression_forehead_wrinkles", "forehead_wrinkles_02", 0.50f, 0.90f);
                AddGene(def, "face_detail_cheek_def", "cheek_def_01", 0.45f, 0.85f);
                AddGene(def, "complexion", "complexion_5", 0.40f, 0.75f);
                AddGene(def, "gene_body_hair", "body_hair_dense", 0.75f, 1.0f);
                AddGene(def, "gene_eyebrows_fullness", "layer_2_high_thickness", 0.0f, 1.0f);
                AddGene(def, "gene_baldness", "no_baldness", 0.0f, 0.2f, weight: 60);
                AddGene(def, "gene_baldness", "male_pattern_baldness", 0.35f, 0.70f, weight: 40);
                AddGene(def, "gene_hair_type", "hair_curly", 0.0f, 1.0f, weight: 45);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 35);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 20);
                break;

            case RaceArchetype.Orc:
                // Muscular, not fat. Those are separate axes and the old values conflated them:
                // body_type sat well onto the fat side at 0.58-0.72 while musculature was only
                // 0.65-0.92, which reads as a heavy bodybuilder. Mass now comes from the muscle
                // axis and body_type sits barely above neutral.
                Shape(def, rng, f, "gene_height", "normal_height", 0.58f, 0.72f);
                Shape(def, rng, f, "gene_bs_body_type", "body_fat_head_fat_medium", 0.52f, 0.64f);
                Shape(def, rng, Untiered, "gene_bs_body_shape", "body_shape_triangle_full", 0.80f, 1.0f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_pos", 0.80f, 1.0f);
                // A brow that juts without also sitting low over a sunken eye reads as a bump
                // rather than a scowl, so the ridge, its height, the forehead slope and the eye
                // behind it all move together.
                Shape(def, rng, f, "gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.80f, 1.0f);
                Shape(def, rng, f, "gene_forehead_brow_height", "forehead_brow_height_neg", 0.15f, 0.35f);
                Shape(def, rng, f, "gene_forehead_angle", "forehead_angle_neg", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_eye_depth", "eye_depth_pos", 0.65f, 0.90f);
                Shape(def, rng, f, "gene_bs_eye_size", "eye_size_neg", 0.45f, 0.70f);
                Shape(def, rng, f, "gene_jaw_width", "jaw_width_pos", 0.75f, 1.0f);
                Shape(def, rng, f, "gene_jaw_forward", "jaw_forward_pos", 0.70f, 0.95f);
                Shape(def, rng, f, "gene_bs_jaw_def", "jaw_def_pos", 0.70f, 1.0f);
                // Vanilla has no tusk gene of any kind. What a tusked mouth actually reads as is a
                // heavy padded lower lip under a thin upper one with the corners pulled down, and
                // all four of those are stock genes.
                Shape(def, rng, f, "gene_mouth_forward", "mouth_forward_pos", 0.65f, 0.95f);
                Shape(def, rng, f, "gene_mouth_lower_lip_size", "mouth_lower_lip_size_pos", 0.60f, 0.90f);
                Shape(def, rng, f, "gene_mouth_upper_lip_size", "mouth_upper_lip_size_neg", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_bs_mouth_lower_lip_pad", "mouth_lower_lip_pad_pos", 0.55f, 0.85f);
                Shape(def, rng, f, "gene_mouth_corner_height", "mouth_corner_height_neg", 0.15f, 0.35f);
                Shape(def, rng, f, "gene_bs_cheek_width", "cheek_width_pos", 0.55f, 0.85f);
                Shape(def, rng, f, "gene_bs_nose_nostril_width", "nose_nostril_width_pos", 0.60f, 0.90f);
                // No ear gene at all. Vanilla's only make a round ear bigger or splay it outward,
                // neither of which is orcish, and EK2 sweeps its orc ears back with ear_angle_neg
                // rather than out. Without a mesh this is a weak signal either way, so the budget
                // goes to the brow instead.
                //
                // complexion_ugly_1 is the one head texture with real character: +4.5 redness and
                // +2.8 unevenness against the base, where the numbered variants differ from each
                // other by about 2%. Note the range does not scale it — complexion swaps the
                // texture through `texture_override`, which has no alpha curve; only the lip decal
                // inside the template responds to the value.
                AddGene(def, "complexion", "complexion_ugly_1", 0.40f, 0.80f);
                AddGene(def, "face_detail_cheek_def", "cheek_def_02", 0.60f, 1.0f);
                AddGene(def, "expression_brow_wrinkles", "brow_wrinkles_03", 0.55f, 0.95f);
                AddGene(def, "gene_body_hair", "body_hair_dense", 0.60f, 0.95f);
                AddGene(def, "gene_eyebrows_fullness", "layer_2_high_thickness", 0.0f, 1.0f);
                // Bald or topknot is the classic orc silhouette, so baldness leads here.
                AddGene(def, "gene_baldness", "male_pattern_baldness", 0.35f, 0.75f, weight: 60);
                AddGene(def, "gene_baldness", "no_baldness", 0.0f, 0.2f, weight: 40);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 60);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 40);
                break;

            case RaceArchetype.Gnome:
                // The one race that WANTS bs_dwarf_1. At 0.06-0.22 it sits at 0.56-0.88, which is
                // the short-limbed, large-headed silhouette a gnome reads as — and paired with
                // near-zero muscle and a thin neck it no longer collides with the dwarf. The big
                // splayed ears here are deliberate and are why the elves gave theirs up.
                Shape(def, rng, f, "gene_height", "normal_height", 0.06f, 0.22f);
                Shape(def, rng, f, "gene_bs_body_type", "body_fat_head_fat_low", 0.22f, 0.38f);
                Shape(def, rng, Untiered, "gene_bs_body_shape", "body_shape_average", 0.0f, 0.14f);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.55f, 0.80f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_neg", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_bs_ear_size", "ear_size_pos", 0.85f, 1.0f);
                Shape(def, rng, f, "gene_bs_ear_outward", "ear_outward_pos", 0.80f, 1.0f);
                Shape(def, rng, f, "gene_bs_ear_bend", "ear_both_bend_pos", 0.70f, 0.95f);
                Shape(def, rng, f, "gene_bs_eye_size", "eye_size_pos", 0.55f, 0.85f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_neg", 0.10f, 0.30f);
                Shape(def, rng, f, "gene_mouth_width", "mouth_width_pos", 0.65f, 0.95f);
                Shape(def, rng, f, "gene_bs_nose_length", "nose_length_pos", 0.75f, 1.0f);
                Shape(def, rng, f, "gene_bs_nose_forward", "nose_forward_pos", 0.60f, 0.90f);
                Shape(def, rng, f, "gene_bs_nose_size", "nose_size_pos", 0.70f, 0.95f);
                AddGene(def, "complexion", "complexion_1", 0.35f, 0.75f);
                AddGene(def, "gene_body_hair", "body_hair_sparse", 0.20f, 0.45f);
                AddGene(def, "gene_hair_type", "hair_curly", 0.0f, 1.0f, weight: 50);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 30);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 20);
                break;

            case RaceArchetype.Giantkin:
                // The top of the ramp. gene_height's own `giant_height` template would also work and
                // skips bs_dwarf_1 entirely, but it pins body_height to a fixed 0.38-0.5 regardless
                // of the value written, so it cannot respond to MorphIntensity. normal_height at the
                // ceiling reaches the same place and still scales with the map's fantasy level.
                Shape(def, rng, f, "gene_height", "normal_height", 0.86f, 1.0f);
                Shape(def, rng, f, "gene_bs_body_type", "body_fat_head_fat_full", 0.58f, 0.74f);
                Shape(def, rng, Untiered, "gene_bs_body_shape", "body_shape_rectangle_full", 0.55f, 0.85f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_pos", 0.85f, 1.0f);
                Shape(def, rng, f, "gene_head_width", "head_width_pos", 0.60f, 0.85f);
                Shape(def, rng, f, "gene_head_height", "head_height_pos", 0.55f, 0.80f);
                Shape(def, rng, f, "gene_jaw_width", "jaw_width_pos", 0.75f, 1.0f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_pos", 0.70f, 0.95f);
                Shape(def, rng, f, "gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.65f, 0.90f);
                // Small ears on a large skull is what sells the scale — a big head with big ears
                // just reads as a normal head. EK2's giant does the same thing.
                Shape(def, rng, f, "gene_bs_ear_size", "ear_size_neg", 0.50f, 0.80f);
                AddGene(def, "complexion", "complexion_5", 0.40f, 0.80f);
                AddGene(def, "expression_forehead_wrinkles", "forehead_wrinkles_01", 0.45f, 0.85f);
                AddGene(def, "gene_body_hair", "body_hair_dense", 0.55f, 0.90f);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 50);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 50);
                break;

            case RaceArchetype.Deepkin:
                // The third elf, and it has to hold its own shape against the other two. Large
                // light-adapted eyes are the distinguishing feature; the old values sank the eye
                // with eye_depth_pos 0.60-0.85 instead, which is the opposite read.
                Shape(def, rng, f, "gene_height", "normal_height", 0.46f, 0.58f);
                Shape(def, rng, f, "gene_bs_body_type", "body_fat_head_fat_low", 0.24f, 0.38f);
                Shape(def, rng, Untiered, "gene_bs_body_shape", "body_shape_hourglass_half", 0.08f, 0.26f);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.60f, 0.85f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_neg", 0.15f, 0.35f);
                Shape(def, rng, f, "gene_bs_ear_angle", "ear_angle_pos", 0.75f, 1.0f);
                Shape(def, rng, f, "gene_bs_ear_bend", "ear_both_bend_pos", 0.80f, 1.0f);
                Shape(def, rng, f, "gene_bs_ear_outward", "ear_outward_pos", 0.25f, 0.45f);
                Shape(def, rng, f, "gene_bs_eye_size", "eye_size_pos", 0.65f, 0.90f);
                Shape(def, rng, f, "gene_eye_depth", "eye_depth_pos", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_eye_angle", "eye_angle_pos", 0.60f, 0.85f);
                Shape(def, rng, f, "gene_bs_cheek_forward", "cheek_forward_pos", 0.70f, 0.95f);
                Shape(def, rng, f, "gene_bs_cheek_height", "cheek_height_pos", 0.65f, 0.90f);
                Shape(def, rng, f, "gene_jaw_width", "jaw_width_neg", 0.10f, 0.30f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_neg", 0.15f, 0.35f);
                Shape(def, rng, f, "gene_bs_nose_length", "nose_length_neg", 0.30f, 0.50f);
                AddGene(def, "gene_age", "old_beauty_1", 0.0f, 0.6f, weight: 65);
                AddGene(def, "gene_age", "no_aging", 0.0f, 1.0f, weight: 35);
                AddGene(def, "gene_eyebrows_fullness", "layer_2_low_thickness", 0.0f, 1.0f);
                AddGene(def, "complexion", "complexion_smooth_1", 0.40f, 0.75f);
                AddGene(def, "gene_body_hair", "body_hair_sparse", 0.15f, 0.40f);
                AddGene(def, "gene_baldness", "no_baldness", 0.0f, 0.15f);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 80);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 20);
                break;

            case RaceArchetype.Exotic:
                // Rolled rather than authored, but rolled inside the same three-gene frame as the
                // others so the result still reads as one coherent people: a height, a mass to go
                // with it, and a silhouette to carry the mass.
                Shape(def, rng, f, "gene_height", "normal_height", rng.Float(0.05f, 0.35f), rng.Float(0.62f, 1.0f));
                Shape(def, rng, f, "gene_bs_body_type", rng.Pick(["body_fat_head_fat_low", "body_fat_head_fat_medium", "body_fat_head_fat_full"]),
                    rng.Float(0.20f, 0.45f), rng.Float(0.55f, 0.80f));
                Shape(def, rng, Untiered, "gene_bs_body_shape", rng.Pick([
                        "body_shape_average", "body_shape_apple_full", "body_shape_pear_full",
                        "body_shape_rectangle_full", "body_shape_triangle_full", "body_shape_hourglass_full"]),
                    rng.Float(0.0f, 0.3f), rng.Float(0.5f, 1.0f));
                Shape(def, rng, f, "gene_bs_ear_outward", "ear_outward_pos", rng.Float(0.1f, 0.2f), rng.Float(0.8f, 1.0f));
                Shape(def, rng, f, "gene_bs_ear_bend", "ear_both_bend_pos", rng.Float(0.1f, 0.2f), rng.Float(0.8f, 1.0f));
                Shape(def, rng, f, "gene_bs_ear_size", rng.Pick(["ear_size_pos", "ear_size_neg"]), rng.Float(0.3f, 0.5f), rng.Float(0.8f, 1.0f));
                Shape(def, rng, f, "gene_bs_cheek_forward", "cheek_forward_pos", rng.Float(0.1f, 0.2f), rng.Float(0.8f, 1.0f));
                Shape(def, rng, f, "gene_bs_eye_size", rng.Pick(["eye_size_pos", "eye_size_neg"]), rng.Float(0.2f, 0.5f), rng.Float(0.7f, 1.0f));
                AddGene(def, "complexion", rng.Pick(["complexion_3", "complexion_7", "complexion_ugly_1"]), 0.40f, 0.90f);
                AddGene(def, "gene_hair_type", rng.Pick(["hair_straight", "hair_wavy", "hair_curly", "hair_afro"]), 0.0f, 1.0f);
                break;

            case RaceArchetype.Human:
            default:
                // Nothing on purpose, exactly as in ApplyColorGenes. A generated human's whole
                // morphology is the vanilla ethnicity named by BaseTemplate, whose weighted curves
                // are far better tuned than anything worth writing here. This case used to emit a
                // flat gene_height, gene_jaw_width and gene_bs_body_type, which overrode those
                // curves with uniform blocks and made every human population shape the same —
                // and body_average carries no shape at all, only a muscle texture decal.
                break;
        }
    }

    /// <summary>
    /// Where a morph gene's value sits when the feature is switched off — 0 for the
    /// blend-shape genes, 0.5 for the signed ones. See <see cref="ApplyMorphGenes"/> for why the
    /// two families differ and why <c>gene_bs_body_type</c> is not one of the blend-shape ones.
    /// </summary>
    private static float NeutralOf(string geneKey) =>
        geneKey.StartsWith("gene_bs_", StringComparison.Ordinal) && geneKey != "gene_bs_body_type"
            ? 0.0f
            : 0.5f;

    /// <summary>
    /// How far a race's body may depart from a plain human, by the map's fantasy level — the same
    /// idea <see cref="SkinPalette.TierOf"/> applies to skin, applied to shape. On a low-fantasy map
    /// a dwarf should be a short broad people rather than a caricature, so every racial gene is
    /// pulled most of the way back toward neutral; on a surreal one it is pushed past its authored
    /// value and clamped at the gene's limit.
    /// </summary>
    private static float MorphIntensity(FantasyRaceMode mode) => mode switch
    {
        FantasyRaceMode.HighFantasy => 1.0f,
        FantasyRaceMode.ExoticSurreal => 1.25f,
        _ => 0.60f
    };

    /// <summary>
    /// Passed as the intensity for a gene that carries ordinary human variation rather than the
    /// race's departure from human. Musculature is the case that matters: muscular people exist on
    /// a low-fantasy map, so thinning it down with everything else would make the race blander
    /// without making it more human.
    /// </summary>
    private const float Untiered = 1.0f;

    /// <summary>
    /// How far one culture's band may slide from the authored one, in gene units.
    ///
    /// Without this every dwarven people on a map is byte-identical, because the archetype tables
    /// are fixed and only <see cref="RaceArchetype.Exotic"/> rolls anything. A small per-gene shift
    /// gives each culture its own face while keeping it recognisably its race, and it is applied
    /// after the fantasy-level scale so peoples stay distinguishable from each other even on a
    /// low-fantasy map where they are all closer to human.
    /// </summary>
    private const float ShapeJitter = 0.04f;

    /// <summary>
    /// Weights across the sub-bands of a <see cref="Shape"/> range, lightest at the edges.
    ///
    /// Vanilla never states a gene as one flat range; <c>ethnicity_template</c> gives every gene
    /// four to six weighted entries so the population has a mode and thin tails. A single entry
    /// says "every orc's jaw is somewhere in 0.75-1.0, uniformly", which is a different and worse
    /// claim than "orc jaws cluster wide, and a few are extreme".
    /// </summary>
    private static readonly int[] BellWeights = [6, 24, 40, 24, 6];

    /// <summary>
    /// A morph gene whose distance from human IS the race. The authored band is scaled by
    /// <paramref name="intensity"/> about the gene's own neutral point, slid by a per-culture
    /// jitter, then emitted as a weighted bell rather than one flat range.
    ///
    /// Genes that express a categorical choice instead — which hair type, whether the race ages,
    /// which complexion texture — go through <see cref="AddGene"/> directly, because splitting a
    /// choice into five sub-bands would say nothing and only multiply lines.
    /// </summary>
    private static void Shape(
        EthnicityDef def, Rng rng, float intensity, string geneKey, string subGeneName,
        float min, float max, int weight = 10)
    {
        float n = NeutralOf(geneKey);
        float shift = rng.Float(-ShapeJitter, ShapeJitter);

        float lo = Math.Clamp(n + (min - n) * intensity + shift, 0.0f, 1.0f);
        float hi = Math.Clamp(n + (max - n) * intensity + shift, 0.0f, 1.0f);

        // A band the clamp has squeezed flat would otherwise emit five identical entries.
        if (hi - lo < 0.02f)
        {
            AddGene(def, geneKey, subGeneName, lo, hi, weight);
            return;
        }

        float step = (hi - lo) / BellWeights.Length;
        for (int i = 0; i < BellWeights.Length; i++)
            AddGene(def, geneKey, subGeneName, lo + i * step, lo + (i + 1) * step, BellWeights[i] * weight / 10);
    }

    /// <summary>
    /// A rectangle in one of CK3's colour palette textures, in the form an ethnicity's colour
    /// genes address them: <c>weight = { x1 y1 x2 y2 }</c>, all four in 0..1.
    /// </summary>
    private readonly record struct Swatch(float X1, float Y1, float X2, float Y2);

    /// <summary>
    /// Named regions of <c>gfx/portraits/hair_palette.dds</c>, sampled out of the stock texture
    /// rather than guessed, with the average RGB each one yields noted beside it.
    ///
    /// **The axes are not what they look like.** <c>x</c> is warmth — ash and neutral browns on
    /// the left, fiery ginger on the right — and <c>y</c> is *darkness*, running from the palest
    /// hair at 0 to black at 1. Vanilla's own <c>ethnicity_template</c> agrees: its Black entry is
    /// <c>{ 0.0 0.9 0.5 1.0 }</c>, down at the bottom.
    ///
    /// Reading <c>y</c> the other way round is what put near-white hair on most of this
    /// generator's populations. The entry every race carried as its "black" sat at
    /// <c>{ 0.01 0.01 0.05 0.08 }</c> — the very top of the texture, which is the lightest
    /// platinum it holds — and for the african, asian and mena human families that entry carried
    /// 85-95% of the weight, so those cultures came out essentially all white-haired.
    /// </summary>
    private static class Hair
    {
        public static readonly Swatch Platinum = new(0.00f, 0.00f, 0.20f, 0.05f);   // #f4dabb
        public static readonly Swatch Silver = new(0.00f, 0.04f, 0.14f, 0.10f);     // #eed4b7
        public static readonly Swatch AshBlonde = new(0.00f, 0.10f, 0.24f, 0.20f);  // #dcb690
        public static readonly Swatch GoldBlonde = new(0.26f, 0.10f, 0.52f, 0.22f); // #e0a876
        public static readonly Swatch LightBrown = new(0.10f, 0.30f, 0.42f, 0.45f); // #9f754d
        public static readonly Swatch Brown = new(0.08f, 0.50f, 0.42f, 0.64f);      // #66492d
        public static readonly Swatch DarkBrown = new(0.08f, 0.68f, 0.45f, 0.80f);  // #2f1f11
        public static readonly Swatch Black = new(0.00f, 0.88f, 0.45f, 1.00f);      // #090401
        public static readonly Swatch BlueBlack = new(0.00f, 0.93f, 0.16f, 1.00f);  // #050301
        public static readonly Swatch Ginger = new(0.74f, 0.20f, 0.98f, 0.38f);     // #cb4825
        public static readonly Swatch Auburn = new(0.60f, 0.44f, 0.88f, 0.60f);     // #773116
    }

    /// <summary>
    /// Named regions of <c>gfx/portraits/eye_palette.dds</c>, sampled the same way.
    ///
    /// Here <c>x</c> is hue — brown at 0, through amber, hazel, green and teal, to blue at 1 —
    /// and <c>y</c> is again darkness, palest at 0. Vanilla keeps every ordinary eye between
    /// <c>y</c> 0.5 and 0.8; anything much above that ramp reads as luminous rather than
    /// coloured, which is why the fantasy races borrow it and the human families do not.
    /// </summary>
    private static class Eye
    {
        public static readonly Swatch DarkBrown = new(0.00f, 0.55f, 0.14f, 0.76f);  // #340f07
        public static readonly Swatch Brown = new(0.00f, 0.34f, 0.16f, 0.54f);      // #4b180e
        public static readonly Swatch Amber = new(0.20f, 0.28f, 0.34f, 0.50f);      // #622d12
        public static readonly Swatch Hazel = new(0.32f, 0.34f, 0.48f, 0.56f);      // #70430d
        public static readonly Swatch Green = new(0.44f, 0.34f, 0.60f, 0.58f);      // #414e18
        public static readonly Swatch Teal = new(0.56f, 0.30f, 0.72f, 0.55f);       // #205035
        public static readonly Swatch GreyBlue = new(0.68f, 0.34f, 0.84f, 0.58f);   // #154246
        public static readonly Swatch Blue = new(0.82f, 0.28f, 1.00f, 0.54f);       // #11415d
        public static readonly Swatch IceBlue = new(0.82f, 0.10f, 1.00f, 0.26f);    // #386fa2
        public static readonly Swatch PaleGreen = new(0.44f, 0.10f, 0.62f, 0.26f);  // #728c3f
        public static readonly Swatch Crimson = new(0.00f, 0.14f, 0.14f, 0.30f);    // #732919
        public static readonly Swatch Gold = new(0.30f, 0.12f, 0.46f, 0.28f);       // #ab6c1e
    }

    private static void ApplyColorGenes(EthnicityDef def, RaceArchetype archetype, string family, FantasyRaceMode mode, Rng rng)
    {
        switch (archetype)
        {
            case RaceArchetype.HighElf:
                AddSkin(def, archetype, mode, 0.00f, 0.00f, 0.18f, 0.30f, weight: 55); // moonpale ivory
                AddSkin(def, archetype, mode, 0.40f, 0.05f, 0.60f, 0.35f, weight: 30); // pearl blue-white
                AddSkin(def, archetype, mode, 0.82f, 0.10f, 1.00f, 0.40f, weight: 15); // warm alabaster
                AddColor(def, "hair_color", Hair.Platinum, weight: 35);
                AddColor(def, "hair_color", Hair.GoldBlonde, weight: 25);
                AddColor(def, "hair_color", Hair.Silver, weight: 20);
                AddColor(def, "hair_color", Hair.AshBlonde, weight: 20);
                AddColor(def, "eye_color", Eye.IceBlue, weight: 25);
                AddColor(def, "eye_color", Eye.Blue, weight: 25);
                AddColor(def, "eye_color", Eye.Green, weight: 20);
                AddColor(def, "eye_color", Eye.Gold, weight: 15);
                AddColor(def, "eye_color", Eye.Teal, weight: 15);
                break;

            case RaceArchetype.WoodElf:
                AddSkin(def, archetype, mode, 0.00f, 0.20f, 0.18f, 0.55f, weight: 45); // birch tan
                AddSkin(def, archetype, mode, 0.40f, 0.25f, 0.60f, 0.60f, weight: 35); // olive bark
                AddSkin(def, archetype, mode, 0.82f, 0.35f, 1.00f, 0.75f, weight: 20); // umber loam
                AddColor(def, "hair_color", Hair.Brown, weight: 30);
                AddColor(def, "hair_color", Hair.DarkBrown, weight: 25);
                AddColor(def, "hair_color", Hair.Auburn, weight: 20);
                AddColor(def, "hair_color", Hair.LightBrown, weight: 15);
                AddColor(def, "hair_color", Hair.Ginger, weight: 10);
                AddColor(def, "eye_color", Eye.Green, weight: 40);
                AddColor(def, "eye_color", Eye.Hazel, weight: 25);
                AddColor(def, "eye_color", Eye.Amber, weight: 20);
                AddColor(def, "eye_color", Eye.Brown, weight: 15);
                break;

            case RaceArchetype.Dwarf:
                AddSkin(def, archetype, mode, 0.00f, 0.20f, 0.20f, 0.55f, weight: 50); // forge-flushed ruddy
                AddSkin(def, archetype, mode, 0.40f, 0.25f, 0.60f, 0.60f, weight: 30); // granite tan
                AddSkin(def, archetype, mode, 0.80f, 0.30f, 1.00f, 0.70f, weight: 20); // iron-dust grey-brown
                AddColor(def, "hair_color", Hair.Ginger, weight: 25);
                AddColor(def, "hair_color", Hair.Auburn, weight: 20);
                AddColor(def, "hair_color", Hair.DarkBrown, weight: 20);
                AddColor(def, "hair_color", Hair.Brown, weight: 20);
                AddColor(def, "hair_color", Hair.Black, weight: 10);
                AddColor(def, "hair_color", Hair.Silver, weight: 5);
                AddColor(def, "eye_color", Eye.Brown, weight: 30);
                AddColor(def, "eye_color", Eye.DarkBrown, weight: 25);
                AddColor(def, "eye_color", Eye.Hazel, weight: 20);
                AddColor(def, "eye_color", Eye.GreyBlue, weight: 15);
                AddColor(def, "eye_color", Eye.Green, weight: 10);
                break;

            case RaceArchetype.Orc:
                AddSkin(def, archetype, mode, 0.00f, 0.15f, 0.20f, 0.55f, weight: 50); // moss green
                AddSkin(def, archetype, mode, 0.80f, 0.35f, 1.00f, 0.80f, weight: 30); // bog olive
                AddSkin(def, archetype, mode, 0.40f, 0.20f, 0.60f, 0.60f, weight: 20); // sage grey-green
                // Coarse black hair. The old values put 75% of orcs on bright ginger, because the
                // rect meant as "near-black desaturated" sat at x 0.80-0.95 — the fiery end of the
                // warmth axis — rather than at the dark end of the darkness axis.
                AddColor(def, "hair_color", Hair.Black, weight: 55);
                AddColor(def, "hair_color", Hair.DarkBrown, weight: 30);
                AddColor(def, "hair_color", Hair.BlueBlack, weight: 15);
                AddColor(def, "eye_color", Eye.Amber, weight: 30);
                AddColor(def, "eye_color", Eye.Crimson, weight: 25);
                AddColor(def, "eye_color", Eye.DarkBrown, weight: 25);
                AddColor(def, "eye_color", Eye.Gold, weight: 20);
                break;

            case RaceArchetype.Gnome:
                AddSkin(def, archetype, mode, 0.00f, 0.15f, 0.20f, 0.50f, weight: 45); // ochre
                AddSkin(def, archetype, mode, 0.40f, 0.20f, 0.60f, 0.55f, weight: 35); // clay tan
                AddSkin(def, archetype, mode, 0.80f, 0.30f, 1.00f, 0.70f, weight: 20); // russet umber
                AddColor(def, "hair_color", Hair.Ginger, weight: 30);
                AddColor(def, "hair_color", Hair.GoldBlonde, weight: 20);
                AddColor(def, "hair_color", Hair.Brown, weight: 20);
                AddColor(def, "hair_color", Hair.LightBrown, weight: 15);
                AddColor(def, "hair_color", Hair.Auburn, weight: 15);
                AddColor(def, "eye_color", Eye.Green, weight: 30);
                AddColor(def, "eye_color", Eye.Hazel, weight: 25);
                AddColor(def, "eye_color", Eye.Brown, weight: 25);
                AddColor(def, "eye_color", Eye.Amber, weight: 20);
                break;

            case RaceArchetype.Giantkin:
                AddSkin(def, archetype, mode, 0.00f, 0.00f, 0.18f, 0.35f, weight: 45); // frostpale
                AddSkin(def, archetype, mode, 0.40f, 0.10f, 0.60f, 0.45f, weight: 35); // glacier blue
                AddSkin(def, archetype, mode, 0.82f, 0.20f, 1.00f, 0.60f, weight: 20); // storm grey
                AddColor(def, "hair_color", Hair.Platinum, weight: 25);
                AddColor(def, "hair_color", Hair.Silver, weight: 20);
                AddColor(def, "hair_color", Hair.AshBlonde, weight: 20);
                AddColor(def, "hair_color", Hair.LightBrown, weight: 20);
                AddColor(def, "hair_color", Hair.Ginger, weight: 15);
                AddColor(def, "eye_color", Eye.IceBlue, weight: 35);
                AddColor(def, "eye_color", Eye.GreyBlue, weight: 30);
                AddColor(def, "eye_color", Eye.Blue, weight: 20);
                AddColor(def, "eye_color", Eye.PaleGreen, weight: 15);
                break;

            case RaceArchetype.Deepkin:
                // Reaches the bottom of the ramp on purpose. Stopping at t=0.70 left the
                // darkest third of the band unreachable, which is the third that makes a
                // drow look like a drow.
                AddSkin(def, archetype, mode, 0.00f, 0.35f, 0.18f, 1.00f, weight: 50); // obsidian
                AddSkin(def, archetype, mode, 0.82f, 0.30f, 1.00f, 0.95f, weight: 30); // violet ash
                AddSkin(def, archetype, mode, 0.40f, 0.25f, 0.60f, 0.85f, weight: 20); // slate graphite
                // White and silver against near-black skin is the whole drow silhouette. This is
                // the one race whose old colours were accidentally right — its "black" entry
                // landed on platinum, which is what a drow wants anyway.
                AddColor(def, "hair_color", Hair.Platinum, weight: 45);
                AddColor(def, "hair_color", Hair.Silver, weight: 30);
                AddColor(def, "hair_color", Hair.BlueBlack, weight: 25);
                AddColor(def, "eye_color", Eye.Crimson, weight: 30);
                AddColor(def, "eye_color", Eye.IceBlue, weight: 25);
                AddColor(def, "eye_color", Eye.Teal, weight: 25);
                AddColor(def, "eye_color", Eye.Gold, weight: 20);
                break;

            case RaceArchetype.Exotic:
                // The Exotic band sweeps teal -> violet -> crimson -> amber across u, so each
                // Exotic ethnicity takes a narrow window of it and reads as one coherent people
                // rather than a bag of unrelated skin colours.
                float hue = rng.Float(0.0f, 1.0f);
                float shade = rng.Float(0.0f, 0.35f);
                AddSkin(def, archetype, mode, Math.Max(0f, hue - 0.07f), shade, Math.Min(1f, hue + 0.07f), shade + 0.35f, weight: 60);
                AddSkin(def, archetype, mode, Math.Max(0f, hue - 0.14f), shade + 0.20f, Math.Min(1f, hue + 0.14f), shade + 0.60f, weight: 40);
                // Rolled across the whole warmth axis but held to a single narrow darkness band,
                // so an Exotic people reads as one hair colour rather than every colour at once.
                float hairX = rng.Float(0.0f, 0.80f);
                float hairY = rng.Float(0.02f, 0.85f);
                AddColor(def, "hair_color", hairX, hairY, Math.Min(1f, hairX + 0.18f), Math.Min(1f, hairY + 0.10f), weight: 65);
                AddColor(def, "hair_color", Hair.BlueBlack, weight: 35);
                float eyeX = rng.Float(0.0f, 0.85f);
                AddColor(def, "eye_color", eyeX, 0.12f, Math.Min(1f, eyeX + 0.15f), 0.34f, weight: 60);
                AddColor(def, "eye_color", Eye.Crimson, weight: 40);
                break;

            case RaceArchetype.Human:
            default:
                // No skin_color here on purpose. Leaving the block out entirely makes CK3 fall
                // through to the vanilla template's own skin, so generated humans come out with
                // stock complexions and never touch the repainted part of the palette. Hair and
                // eyes still vary per culture — those palettes are untouched.
                switch (family)
                {
                    case "african":
                        AddColor(def, "hair_color", Hair.Black, weight: 70);
                        AddColor(def, "hair_color", Hair.BlueBlack, weight: 20);
                        AddColor(def, "hair_color", Hair.DarkBrown, weight: 10);
                        AddColor(def, "eye_color", Eye.DarkBrown, weight: 70);
                        AddColor(def, "eye_color", Eye.Brown, weight: 30);
                        break;
                    case "asian":
                        AddColor(def, "hair_color", Hair.Black, weight: 75);
                        AddColor(def, "hair_color", Hair.BlueBlack, weight: 15);
                        AddColor(def, "hair_color", Hair.DarkBrown, weight: 10);
                        AddColor(def, "eye_color", Eye.DarkBrown, weight: 65);
                        AddColor(def, "eye_color", Eye.Brown, weight: 35);
                        break;
                    case "mena":
                        AddColor(def, "hair_color", Hair.Black, weight: 55);
                        AddColor(def, "hair_color", Hair.DarkBrown, weight: 35);
                        AddColor(def, "hair_color", Hair.Brown, weight: 10);
                        AddColor(def, "eye_color", Eye.DarkBrown, weight: 45);
                        AddColor(def, "eye_color", Eye.Brown, weight: 35);
                        AddColor(def, "eye_color", Eye.Hazel, weight: 12);
                        AddColor(def, "eye_color", Eye.Green, weight: 8);
                        break;
                    case "caucasian":
                    default:
                        AddColor(def, "hair_color", Hair.Brown, weight: 30);
                        AddColor(def, "hair_color", Hair.DarkBrown, weight: 20);
                        AddColor(def, "hair_color", Hair.AshBlonde, weight: 15);
                        AddColor(def, "hair_color", Hair.GoldBlonde, weight: 12);
                        AddColor(def, "hair_color", Hair.Black, weight: 13);
                        AddColor(def, "hair_color", Hair.Ginger, weight: 10);
                        AddColor(def, "eye_color", Eye.Blue, weight: 25);
                        AddColor(def, "eye_color", Eye.Brown, weight: 20);
                        AddColor(def, "eye_color", Eye.GreyBlue, weight: 15);
                        AddColor(def, "eye_color", Eye.Green, weight: 15);
                        AddColor(def, "eye_color", Eye.Hazel, weight: 15);
                        AddColor(def, "eye_color", Eye.DarkBrown, weight: 10);
                        break;
                }
                break;
        }
    }

    private static void AddColor(EthnicityDef def, string colorType, Swatch s, int weight = 10)
        => AddColor(def, colorType, s.X1, s.Y1, s.X2, s.Y2, weight);

    private static void AddGene(EthnicityDef def, string geneKey, string subGeneName, float min, float max, int weight = 10)
    {
        if (!def.MorphGenes.TryGetValue(geneKey, out var list))
            def.MorphGenes[geneKey] = list = [];

        list.Add(new GeneMorphEntry
        {
            SubGeneName = subGeneName,
            Min = Math.Clamp(min, 0.0f, 1.0f),
            Max = Math.Clamp(max, 0.0f, 1.0f),
            Weight = weight
        });
    }

    private static void AddColor(EthnicityDef def, string colorType, float x1, float y1, float x2, float y2, int weight = 10)
    {
        if (!def.ColorGenes.TryGetValue(colorType, out var list))
            def.ColorGenes[colorType] = list = [];

        list.Add(new ColorPaletteRange
        {
            X1 = Math.Clamp(x1, 0.0f, 1.0f),
            Y1 = Math.Clamp(y1, 0.0f, 1.0f),
            X2 = Math.Clamp(x2, 0.0f, 1.0f),
            Y2 = Math.Clamp(y2, 0.0f, 1.0f),
            Weight = weight
        });
    }

    /// <summary>
    /// A skin swatch inside a fantasy race's own band of the palette. <paramref name="u1"/> and
    /// <paramref name="u2"/> select across that race's hue variants, <paramref name="t1"/> and
    /// <paramref name="t2"/> run from its lightest tone to its darkest — see
    /// <see cref="SkinPalette"/> for why the coordinates are derived rather than literal.
    /// </summary>
    private static void AddSkin(
        EthnicityDef def, RaceArchetype archetype, FantasyRaceMode mode,
        float u1, float t1, float u2, float t2, int weight)
    {
        int band = SkinPalette.BandOf(archetype);
        if (band < 0)
            throw new ArgumentOutOfRangeException(
                nameof(archetype), archetype,
                "has no palette band — humans inherit skin from their vanilla template instead");

        var (x1, y1, x2, y2) = SkinPalette.Swatch(band, SkinPalette.TierOf(mode), u1, t1, u2, t2);
        AddColor(def, "skin_color", x1, y1, x2, y2, weight);
    }
}