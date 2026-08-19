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

public sealed class GeneAccessoryEntry
{
    public required string AccessoryName { get; init; }
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
    public Dictionary<string, List<GeneAccessoryEntry>> AccessoryGenes { get; } = [];
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

            if (cfg.TieRaceToHeritage && heritageEth != null)
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

                    subArchetype = available.Count > 0 ? rng.Pick(available) : PickArchetypeForCulture(culture, cfg, rng);
                }
                else
                {
                    var baseArchetype = heritageEth?.Archetype ?? RaceArchetype.Human;
                    subArchetype = rng.Chance(0.70) ? baseArchetype : PickArchetypeForCulture(culture, cfg, rng);
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
            string reason = !cfg.TieRaceToHeritage && byCulture.Count < wanted
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

        // 1. Guaranteed Diversity Phase: Pair each unique race to its highest-affinity available heritage
        for (int i = 0; i < targetUnique && remainingHeritages.Count > 0; i++)
        {
            var unassignedRaces = candidatePool.Where(r => !assignedRaces.Contains(r)).ToList();

            // Find (Heritage, Race) pair with best terrain synergy score
            Heritage bestHeritage = remainingHeritages[0];
            RaceArchetype bestRace = unassignedRaces[0];
            double bestScore = double.MinValue;

            foreach (var h in remainingHeritages)
            {
                var shares = heritageTerrain[h];
                foreach (var race in unassignedRaces)
                {
                    // The jitter only breaks ties inside an affinity step, never across one.
                    double score = HeritageAffinity(race, shares) + rng.Double(0.0, 0.3);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestHeritage = h;
                        bestRace = race;
                    }
                }
            }

            assignments[bestHeritage] = bestRace;
            assignedRaces.Add(bestRace);
            remainingHeritages.Remove(bestHeritage);
        }

        // 2. Remainder Phase: Remaining heritages roll probabilistically according to their biomes
        foreach (var h in remainingHeritages)
        {
            assignments[h] = PickWeightedArchetype(DominantOf(heritageTerrain[h]), cfg, rng);
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

            double reach = Math.Clamp(
                (share - AffinityMinShare) / (AffinityFullShare - AffinityMinShare), 0.0, 1.0);
            best = Math.Max(best, 1.0 + (affinity - 1.0) * reach);
        }
        return best;
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

    private static RaceArchetype PickWeightedArchetype(TerrainClass dominant, MapConfig cfg, Rng rng)
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

        return dominant switch
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

    private static RaceArchetype PickArchetypeForCulture(Culture culture, MapConfig cfg, Rng rng)
    {
        if (!cfg.EnableFantasyEthnicities || cfg.RaceMode == FantasyRaceMode.HumanOnly)
            return RaceArchetype.Human;

        var pool = new[]
        {
            RaceArchetype.Human,
            RaceArchetype.Dwarf,
            RaceArchetype.HighElf,
            RaceArchetype.WoodElf,
            RaceArchetype.Orc,
            RaceArchetype.Gnome,
            RaceArchetype.Giantkin,
            RaceArchetype.Deepkin
        };

        return rng.Pick(pool);
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
            LocalizedName = $"{name} ({archetype})",
            Archetype = archetype,
            LookFamily = family,
            BaseTemplate = PickVanillaTemplate(family, rng)
        };

        ApplyMorphGenes(def, archetype, rng);
        ApplyColorGenes(def, archetype, family, mode, rng);
        ApplyAccessoryGenes(def, archetype, family);

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

    private static void ApplyMorphGenes(EthnicityDef def, RaceArchetype archetype, Rng rng)
    {
        switch (archetype)
        {
            case RaceArchetype.HighElf:
                AddGene(def, "gene_height", "normal_height", 0.65f, 0.7f);
                AddGene(def, "gene_bs_body_type", "body_shape_average", 0.10f, 0.35f);
                AddGene(def, "gene_neck_length", "neck_length_pos", 0.65f, 0.90f);
                AddGene(def, "gene_neck_width", "neck_width_neg", 0.20f, 0.40f);
                AddGene(def, "gene_bs_ear_angle", "ear_angle_pos", 0.80f, 1.0f);
                AddGene(def, "gene_bs_ear_bend", "ear_both_bend_pos", 0.85f, 1.0f);
                AddGene(def, "gene_bs_ear_outward", "ear_outward_pos", 0.60f, 0.95f);
                AddGene(def, "gene_bs_ear_size", "ear_size_pos", 0.60f, 0.85f);
                AddGene(def, "gene_bs_cheek_forward", "cheek_forward_pos", 0.60f, 0.85f);
                AddGene(def, "gene_jaw_width", "jaw_width_neg", 0.15f, 0.35f);
                AddGene(def, "gene_chin_width", "chin_width_neg", 0.20f, 0.40f);
                AddGene(def, "gene_chin_forward", "chin_forward_pos", 0.45f, 0.65f);
                AddGene(def, "gene_bs_nose_length", "nose_length_pos", 0.35f, 0.55f);
                AddGene(def, "gene_bs_nose_profile", "nose_profile_straight", 0.40f, 0.70f);
                break;

            case RaceArchetype.WoodElf:
                AddGene(def, "gene_height", "normal_height", 0.60f, 0.65f);
                AddGene(def, "gene_bs_body_type", "body_shape_average", 0.20f, 0.45f);
                AddGene(def, "gene_neck_length", "neck_length_pos", 0.50f, 0.75f);
                AddGene(def, "gene_bs_ear_angle", "ear_angle_pos", 0.70f, 0.95f);
                AddGene(def, "gene_bs_ear_bend", "ear_both_bend_pos", 0.75f, 0.95f);
                AddGene(def, "gene_bs_ear_outward", "ear_outward_pos", 0.55f, 0.85f);
                AddGene(def, "gene_eye_angle", "eye_angle_pos", 0.60f, 0.85f);
                AddGene(def, "gene_bs_eye_slant", "eye_slant_pos", 0.55f, 0.80f);
                AddGene(def, "gene_jaw_width", "jaw_width_neg", 0.25f, 0.45f);
                AddGene(def, "gene_bs_nose_ridge_angle", "nose_ridge_angle_pos", 0.45f, 0.70f);
                break;

            case RaceArchetype.Dwarf:
                AddGene(def, "gene_height", "normal_height", 0.02f, 0.10f);
                AddGene(def, "gene_bs_body_type", "body_shape_average", 0.70f, 0.95f);
                AddGene(def, "gene_neck_width", "neck_width_pos", 0.80f, 1.0f);
                AddGene(def, "gene_neck_length", "neck_length_neg", 0.10f, 0.35f);
                AddGene(def, "gene_bs_head_width_pos", "head_width_pos", 0.70f, 0.95f);
                AddGene(def, "gene_jaw_width", "jaw_width_pos", 0.75f, 1.0f);
                AddGene(def, "gene_jaw_forward", "jaw_forward_pos", 0.55f, 0.85f);
                AddGene(def, "gene_chin_width", "chin_width_pos", 0.70f, 0.95f);
                AddGene(def, "gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.65f, 0.90f);
                AddGene(def, "gene_bs_ear_size", "ear_size_neg", 0.35f, 0.55f);
                AddGene(def, "gene_bs_nose_length", "nose_length_pos", 0.50f, 0.80f);
                break;

            case RaceArchetype.Orc:
                AddGene(def, "gene_height", "normal_height", 0.65f, 0.90f);
                AddGene(def, "gene_bs_body_type", "body_shape_average", 0.75f, 1.0f);
                AddGene(def, "gene_neck_width", "neck_width_pos", 0.75f, 1.0f);
                AddGene(def, "gene_jaw_width", "jaw_width_pos", 0.70f, 1.0f);
                AddGene(def, "gene_jaw_forward", "jaw_forward_pos", 0.65f, 0.95f);
                AddGene(def, "gene_mouth_forward", "mouth_forward_pos", 0.65f, 0.95f);
                AddGene(def, "gene_bs_mouth_lower_lip_size", "mouth_lower_lip_size_pos", 0.60f, 0.90f);
                AddGene(def, "gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.75f, 1.0f);
                AddGene(def, "gene_bs_ear_angle", "ear_angle_pos", 0.65f, 0.90f);
                break;

            case RaceArchetype.Gnome:
                AddGene(def, "gene_height", "normal_height", 0.05f, 0.25f);
                AddGene(def, "gene_bs_body_type", "body_shape_average", 0.05f, 0.30f);
                AddGene(def, "gene_neck_length", "neck_length_pos", 0.55f, 0.80f);
                AddGene(def, "gene_bs_ear_size", "ear_size_pos", 0.80f, 1.0f);
                AddGene(def, "gene_bs_ear_outward", "ear_outward_pos", 0.80f, 1.0f);
                AddGene(def, "gene_bs_ear_bend", "ear_both_bend_pos", 0.70f, 0.95f);
                AddGene(def, "gene_chin_width", "chin_width_neg", 0.10f, 0.30f);
                AddGene(def, "gene_mouth_width", "mouth_width_pos", 0.65f, 0.95f);
                AddGene(def, "gene_bs_nose_length", "nose_length_pos", 0.75f, 1.0f);
                AddGene(def, "gene_bs_nose_forward", "nose_forward_pos", 0.60f, 0.90f);
                break;

            case RaceArchetype.Giantkin:
                AddGene(def, "gene_height", "normal_height", 0.94f, 1.0f);
                AddGene(def, "gene_bs_body_type", "body_shape_average", 0.80f, 1.0f);
                AddGene(def, "gene_neck_width", "neck_width_pos", 0.85f, 1.0f);
                AddGene(def, "gene_jaw_width", "jaw_width_pos", 0.75f, 1.0f);
                AddGene(def, "gene_chin_width", "chin_width_pos", 0.70f, 0.95f);
                AddGene(def, "gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.65f, 0.90f);
                break;

            case RaceArchetype.Deepkin:
                AddGene(def, "gene_height", "normal_height", 0.45f, 0.60f);
                AddGene(def, "gene_bs_body_type", "body_shape_average", 0.05f, 0.25f);
                AddGene(def, "gene_neck_length", "neck_length_pos", 0.60f, 0.85f);
                AddGene(def, "gene_neck_width", "neck_width_neg", 0.15f, 0.35f);
                AddGene(def, "gene_bs_ear_angle", "ear_angle_pos", 0.75f, 1.0f);
                AddGene(def, "gene_bs_ear_bend", "ear_both_bend_pos", 0.80f, 1.0f);
                AddGene(def, "gene_bs_ear_outward", "ear_outward_pos", 0.70f, 0.95f);
                AddGene(def, "gene_bs_cheek_forward", "cheek_forward_pos", 0.70f, 0.95f);
                AddGene(def, "gene_bs_cheek_height", "cheek_height_pos", 0.65f, 0.90f);
                AddGene(def, "gene_eye_depth", "eye_depth_pos", 0.60f, 0.85f);
                AddGene(def, "gene_bs_eye_slant", "eye_slant_pos", 0.60f, 0.85f);
                AddGene(def, "gene_jaw_width", "jaw_width_neg", 0.10f, 0.30f);
                AddGene(def, "gene_chin_width", "chin_width_neg", 0.15f, 0.35f);
                AddGene(def, "gene_bs_nose_length", "nose_length_neg", 0.30f, 0.50f);
                break;

            case RaceArchetype.Exotic:
                AddGene(def, "gene_height", "normal_height", rng.Float(0.005f, 0.35f), rng.Float(0.65f, 1.0f));
                AddGene(def, "gene_bs_ear_outward", "ear_outward_pos", rng.Float(0.1f, 0.2f), rng.Float(0.8f, 1.0f));
                AddGene(def, "gene_bs_ear_bend", "ear_both_bend_pos", rng.Float(0.1f, 0.2f), rng.Float(0.8f, 1.0f));
                AddGene(def, "gene_bs_cheek_forward", "cheek_forward_pos", rng.Float(0.1f, 0.2f), rng.Float(0.8f, 1.0f));
                break;

            case RaceArchetype.Human:
            default:
                AddGene(def, "gene_height", "normal_height", 0.35f, 0.65f);
                AddGene(def, "gene_jaw_width", "jaw_width_pos", 0.35f, 0.65f);
                AddGene(def, "gene_bs_body_type", "body_shape_average", 0.30f, 0.70f);
                break;
        }
    }

    private static void ApplyColorGenes(EthnicityDef def, RaceArchetype archetype, string family, FantasyRaceMode mode, Rng rng)
    {
        switch (archetype)
        {
            case RaceArchetype.HighElf:
                AddSkin(def, archetype, mode, 0.00f, 0.00f, 0.18f, 0.30f, weight: 55); // moonpale ivory
                AddSkin(def, archetype, mode, 0.40f, 0.05f, 0.60f, 0.35f, weight: 30); // pearl blue-white
                AddSkin(def, archetype, mode, 0.82f, 0.10f, 1.00f, 0.40f, weight: 15); // warm alabaster
                AddColor(def, "hair_color", 0.05f, 0.85f, 0.15f, 0.95f, weight: 55);
                AddColor(def, "hair_color", 0.02f, 0.10f, 0.10f, 0.25f, weight: 30);
                AddColor(def, "hair_color", 0.01f, 0.01f, 0.05f, 0.05f, weight: 15);
                AddColor(def, "eye_color", 0.30f, 0.50f, 0.45f, 0.80f, weight: 45);
                AddColor(def, "eye_color", 0.18f, 0.50f, 0.28f, 0.75f, weight: 35);
                AddColor(def, "eye_color", 0.75f, 0.35f, 0.88f, 0.65f, weight: 20);
                break;

            case RaceArchetype.WoodElf:
                AddSkin(def, archetype, mode, 0.00f, 0.20f, 0.18f, 0.55f, weight: 45); // birch tan
                AddSkin(def, archetype, mode, 0.40f, 0.25f, 0.60f, 0.60f, weight: 35); // olive bark
                AddSkin(def, archetype, mode, 0.82f, 0.35f, 1.00f, 0.75f, weight: 20); // umber loam
                AddColor(def, "hair_color", 0.20f, 0.55f, 0.35f, 0.80f, weight: 40);
                AddColor(def, "hair_color", 0.12f, 0.30f, 0.25f, 0.55f, weight: 40);
                AddColor(def, "hair_color", 0.08f, 0.75f, 0.18f, 0.90f, weight: 20);
                AddColor(def, "eye_color", 0.08f, 0.60f, 0.18f, 0.85f, weight: 45);
                AddColor(def, "eye_color", 0.20f, 0.40f, 0.32f, 0.65f, weight: 40);
                AddColor(def, "eye_color", 0.10f, 0.25f, 0.20f, 0.45f, weight: 15);
                break;

            case RaceArchetype.Dwarf:
                AddSkin(def, archetype, mode, 0.00f, 0.20f, 0.20f, 0.55f, weight: 50); // forge-flushed ruddy
                AddSkin(def, archetype, mode, 0.40f, 0.25f, 0.60f, 0.60f, weight: 30); // granite tan
                AddSkin(def, archetype, mode, 0.80f, 0.30f, 1.00f, 0.70f, weight: 20); // iron-dust grey-brown
                AddColor(def, "hair_color", 0.25f, 0.65f, 0.40f, 0.90f, weight: 35);
                AddColor(def, "hair_color", 0.08f, 0.15f, 0.22f, 0.35f, weight: 45);
                AddColor(def, "hair_color", 0.02f, 0.05f, 0.08f, 0.20f, weight: 20);
                AddColor(def, "eye_color", 0.02f, 0.10f, 0.10f, 0.30f, weight: 40);
                AddColor(def, "eye_color", 0.10f, 0.20f, 0.25f, 0.45f, weight: 40);
                AddColor(def, "eye_color", 0.25f, 0.35f, 0.40f, 0.60f, weight: 20);
                break;

            case RaceArchetype.Orc:
                AddSkin(def, archetype, mode, 0.00f, 0.15f, 0.20f, 0.55f, weight: 50); // moss green
                AddSkin(def, archetype, mode, 0.80f, 0.35f, 1.00f, 0.80f, weight: 30); // bog olive
                AddSkin(def, archetype, mode, 0.40f, 0.20f, 0.60f, 0.60f, weight: 20); // sage grey-green
                AddColor(def, "hair_color", 0.80f, 0.10f, 0.95f, 0.30f, weight: 75);
                AddColor(def, "hair_color", 0.02f, 0.05f, 0.08f, 0.20f, weight: 25);
                AddColor(def, "eye_color", 0.05f, 0.70f, 0.20f, 0.90f, weight: 50);
                AddColor(def, "eye_color", 0.08f, 0.80f, 0.18f, 0.95f, weight: 35);
                AddColor(def, "eye_color", 0.12f, 0.35f, 0.22f, 0.55f, weight: 15);
                break;

            case RaceArchetype.Gnome:
                AddSkin(def, archetype, mode, 0.00f, 0.15f, 0.20f, 0.50f, weight: 45); // ochre
                AddSkin(def, archetype, mode, 0.40f, 0.20f, 0.60f, 0.55f, weight: 35); // clay tan
                AddSkin(def, archetype, mode, 0.80f, 0.30f, 1.00f, 0.70f, weight: 20); // russet umber
                AddColor(def, "hair_color", 0.75f, 0.15f, 0.90f, 0.35f, weight: 60);
                AddColor(def, "hair_color", 0.10f, 0.45f, 0.25f, 0.65f, weight: 40);
                AddColor(def, "eye_color", 0.10f, 0.60f, 0.25f, 0.85f, weight: 50);
                AddColor(def, "eye_color", 0.20f, 0.55f, 0.32f, 0.75f, weight: 35);
                AddColor(def, "eye_color", 0.01f, 0.01f, 0.05f, 0.10f, weight: 15);
                break;

            case RaceArchetype.Giantkin:
                AddSkin(def, archetype, mode, 0.00f, 0.00f, 0.18f, 0.35f, weight: 45); // frostpale
                AddSkin(def, archetype, mode, 0.40f, 0.10f, 0.60f, 0.45f, weight: 35); // glacier blue
                AddSkin(def, archetype, mode, 0.82f, 0.20f, 1.00f, 0.60f, weight: 20); // storm grey
                AddColor(def, "hair_color", 0.05f, 0.70f, 0.20f, 0.90f, weight: 50);
                AddColor(def, "hair_color", 0.25f, 0.70f, 0.38f, 0.95f, weight: 35);
                AddColor(def, "hair_color", 0.05f, 0.10f, 0.15f, 0.25f, weight: 15);
                AddColor(def, "eye_color", 0.35f, 0.40f, 0.55f, 0.75f, weight: 60);
                AddColor(def, "eye_color", 0.05f, 0.10f, 0.15f, 0.25f, weight: 40);
                break;

            case RaceArchetype.Deepkin:
                // Reaches the bottom of the ramp on purpose. Stopping at t=0.70 left the
                // darkest third of the band unreachable, which is the third that makes a
                // drow look like a drow.
                AddSkin(def, archetype, mode, 0.00f, 0.35f, 0.18f, 1.00f, weight: 50); // obsidian
                AddSkin(def, archetype, mode, 0.82f, 0.30f, 1.00f, 0.95f, weight: 30); // violet ash
                AddSkin(def, archetype, mode, 0.40f, 0.25f, 0.60f, 0.85f, weight: 20); // slate graphite
                AddColor(def, "hair_color", 0.02f, 0.90f, 0.08f, 0.99f, weight: 60);
                AddColor(def, "hair_color", 0.01f, 0.01f, 0.04f, 0.05f, weight: 40);
                AddColor(def, "eye_color", 0.75f, 0.45f, 0.90f, 0.80f, weight: 50);
                AddColor(def, "eye_color", 0.30f, 0.60f, 0.45f, 0.90f, weight: 50);
                break;

            case RaceArchetype.Exotic:
                // The Exotic band sweeps teal -> violet -> crimson -> amber across u, so each
                // Exotic ethnicity takes a narrow window of it and reads as one coherent people
                // rather than a bag of unrelated skin colours.
                float hue = rng.Float(0.0f, 1.0f);
                float shade = rng.Float(0.0f, 0.35f);
                AddSkin(def, archetype, mode, Math.Max(0f, hue - 0.07f), shade, Math.Min(1f, hue + 0.07f), shade + 0.35f, weight: 60);
                AddSkin(def, archetype, mode, Math.Max(0f, hue - 0.14f), shade + 0.20f, Math.Min(1f, hue + 0.14f), shade + 0.60f, weight: 40);
                AddColor(def, "hair_color", rng.Float(0f, 0.5f), rng.Float(0.5f, 1f), rng.Float(0.5f, 1f), rng.Float(0.5f, 1f), weight: 60);
                AddColor(def, "hair_color", 0.01f, 0.01f, 0.05f, 0.05f, weight: 40);
                AddColor(def, "eye_color", rng.Float(0f, 0.5f), rng.Float(0.5f, 1f), rng.Float(0.5f, 1f), rng.Float(0.5f, 1f), weight: 100);
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
                        AddColor(def, "hair_color", 0.01f, 0.01f, 0.08f, 0.12f, weight: 90);
                        AddColor(def, "hair_color", 0.10f, 0.15f, 0.20f, 0.35f, weight: 10);
                        AddColor(def, "eye_color", 0.05f, 0.10f, 0.15f, 0.25f, weight: 90);
                        AddColor(def, "eye_color", 0.12f, 0.20f, 0.22f, 0.40f, weight: 10);
                        break;
                    case "asian":
                        AddColor(def, "hair_color", 0.01f, 0.01f, 0.05f, 0.08f, weight: 95);
                        AddColor(def, "hair_color", 0.08f, 0.15f, 0.18f, 0.30f, weight: 5);
                        AddColor(def, "eye_color", 0.04f, 0.08f, 0.12f, 0.20f, weight: 95);
                        AddColor(def, "eye_color", 0.10f, 0.25f, 0.20f, 0.45f, weight: 5);
                        break;
                    case "mena":
                        AddColor(def, "hair_color", 0.01f, 0.02f, 0.10f, 0.15f, weight: 85);
                        AddColor(def, "hair_color", 0.12f, 0.25f, 0.25f, 0.45f, weight: 15);
                        AddColor(def, "eye_color", 0.05f, 0.12f, 0.18f, 0.30f, weight: 80);
                        AddColor(def, "eye_color", 0.18f, 0.30f, 0.28f, 0.50f, weight: 20);
                        break;
                    case "caucasian":
                    default:
                        AddColor(def, "hair_color", 0.08f, 0.15f, 0.22f, 0.40f, weight: 50);
                        AddColor(def, "hair_color", 0.05f, 0.70f, 0.18f, 0.90f, weight: 30);
                        AddColor(def, "hair_color", 0.22f, 0.65f, 0.35f, 0.88f, weight: 10);
                        AddColor(def, "hair_color", 0.01f, 0.01f, 0.05f, 0.08f, weight: 10);
                        AddColor(def, "eye_color", 0.28f, 0.45f, 0.45f, 0.70f, weight: 45);
                        AddColor(def, "eye_color", 0.08f, 0.15f, 0.18f, 0.30f, weight: 40);
                        AddColor(def, "eye_color", 0.18f, 0.40f, 0.28f, 0.60f, weight: 15);
                        break;
                }
                break;
        }
    }

    private static void ApplyAccessoryGenes(EthnicityDef def, RaceArchetype archetype, string family)
    {
        switch (archetype)
        {
            case RaceArchetype.HighElf:
                AddAccessory(def, "hairstyles", "western_hairstyles_straight", weight: 50);
                AddAccessory(def, "hairstyles", "byzantine_hairstyles_straight", weight: 35);
                AddAccessory(def, "hairstyles", "mena_hairstyles_straight", weight: 15);
                AddAccessory(def, "beards", "no_beard", weight: 85);
                AddAccessory(def, "beards", "western_beards_clean_shaven", weight: 15);
                break;

            case RaceArchetype.WoodElf:
                AddAccessory(def, "hairstyles", "fp1_norse_hairstyles_wavy", weight: 45);
                AddAccessory(def, "hairstyles", "western_hairstyles_wavy", weight: 35);
                AddAccessory(def, "hairstyles", "steppe_hairstyles_straight", weight: 20);
                AddAccessory(def, "beards", "no_beard", weight: 75);
                AddAccessory(def, "beards", "western_beards_goatee", weight: 15);
                AddAccessory(def, "beards", "western_beards_stubble", weight: 10);
                break;

            case RaceArchetype.Dwarf:
                AddAccessory(def, "hairstyles", "fp1_norse_hairstyles_wavy", weight: 45);
                AddAccessory(def, "hairstyles", "western_hairstyles_curly", weight: 35);
                AddAccessory(def, "hairstyles", "western_hairstyles_straight", weight: 20);
                AddAccessory(def, "beards", "fp1_norse_beards_full", weight: 50);
                AddAccessory(def, "beards", "western_beards_full", weight: 35);
                AddAccessory(def, "beards", "mena_beards_full", weight: 15);
                break;

            case RaceArchetype.Orc:
                AddAccessory(def, "hairstyles", "steppe_hairstyles_straight", weight: 50);
                AddAccessory(def, "hairstyles", "fp1_norse_hairstyles_straight", weight: 30);
                AddAccessory(def, "hairstyles", "african_hairstyles_shaved", weight: 20);
                AddAccessory(def, "beards", "steppe_beards_mustache", weight: 40);
                AddAccessory(def, "beards", "fp1_norse_beards_full", weight: 35);
                AddAccessory(def, "beards", "western_beards_goatee", weight: 25);
                break;

            case RaceArchetype.Gnome:
                AddAccessory(def, "hairstyles", "steppe_hairstyles_straight", weight: 40);
                AddAccessory(def, "hairstyles", "african_hairstyles_shaved", weight: 30);
                AddAccessory(def, "hairstyles", "western_hairstyles_straight", weight: 30);
                AddAccessory(def, "beards", "western_beards_goatee", weight: 40);
                AddAccessory(def, "beards", "steppe_beards_mustache", weight: 30);
                AddAccessory(def, "beards", "no_beard", weight: 30);
                break;

            case RaceArchetype.Giantkin:
                AddAccessory(def, "hairstyles", "fp1_norse_hairstyles_straight", weight: 50);
                AddAccessory(def, "hairstyles", "western_hairstyles_wavy", weight: 50);
                AddAccessory(def, "beards", "fp1_norse_beards_full", weight: 60);
                AddAccessory(def, "beards", "western_beards_full", weight: 40);
                break;

            case RaceArchetype.Deepkin:
                AddAccessory(def, "hairstyles", "mena_hairstyles_straight", weight: 45);
                AddAccessory(def, "hairstyles", "byzantine_hairstyles_straight", weight: 35);
                AddAccessory(def, "hairstyles", "western_hairstyles_straight", weight: 20);
                AddAccessory(def, "beards", "no_beard", weight: 65);
                AddAccessory(def, "beards", "mena_beards_goatee", weight: 20);
                AddAccessory(def, "beards", "byzantine_beards_trim", weight: 15);
                break;

            case RaceArchetype.Exotic:
                AddAccessory(def, "hairstyles", "mena_hairstyles_straight", weight: 35);
                AddAccessory(def, "hairstyles", "steppe_hairstyles_straight", weight: 35);
                AddAccessory(def, "hairstyles", "byzantine_hairstyles_straight", weight: 30);
                AddAccessory(def, "beards", "western_beards_goatee", weight: 40);
                AddAccessory(def, "beards", "no_beard", weight: 40);
                AddAccessory(def, "beards", "fp1_norse_beards_full", weight: 20);
                break;

            case RaceArchetype.Human:
            default:
                switch (family)
                {
                    case "african":
                        AddAccessory(def, "hairstyles", "african_hairstyles_curly", weight: 60);
                        AddAccessory(def, "hairstyles", "african_hairstyles_shaved", weight: 40);
                        AddAccessory(def, "beards", "african_beards_full", weight: 50);
                        AddAccessory(def, "beards", "african_beards_goatee", weight: 30);
                        AddAccessory(def, "beards", "no_beard", weight: 20);
                        break;
                    case "asian":
                        AddAccessory(def, "hairstyles", "steppe_hairstyles_straight", weight: 70);
                        AddAccessory(def, "hairstyles", "western_hairstyles_straight", weight: 30);
                        AddAccessory(def, "beards", "steppe_beards_mustache", weight: 50);
                        AddAccessory(def, "beards", "no_beard", weight: 35);
                        AddAccessory(def, "beards", "western_beards_goatee", weight: 15);
                        break;
                    case "mena":
                        AddAccessory(def, "hairstyles", "mena_hairstyles_straight", weight: 60);
                        AddAccessory(def, "hairstyles", "mena_hairstyles_wavy", weight: 40);
                        AddAccessory(def, "beards", "mena_beards_full", weight: 60);
                        AddAccessory(def, "beards", "mena_beards_trim", weight: 30);
                        AddAccessory(def, "beards", "no_beard", weight: 10);
                        break;
                    case "caucasian":
                    default:
                        AddAccessory(def, "hairstyles", "western_hairstyles_straight", weight: 40);
                        AddAccessory(def, "hairstyles", "western_hairstyles_wavy", weight: 35);
                        AddAccessory(def, "hairstyles", "western_hairstyles_curly", weight: 25);
                        AddAccessory(def, "beards", "western_beards_full", weight: 40);
                        AddAccessory(def, "beards", "western_beards_goatee", weight: 30);
                        AddAccessory(def, "beards", "western_beards_clean_shaven", weight: 20);
                        AddAccessory(def, "beards", "no_beard", weight: 10);
                        break;
                }
                break;
        }
    }

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

    private static void AddAccessory(EthnicityDef def, string geneKey, string accessoryName, int weight = 10)
    {
        if (!def.AccessoryGenes.TryGetValue(geneKey, out var list))
            def.AccessoryGenes[geneKey] = list = [];

        list.Add(new GeneAccessoryEntry
        {
            AccessoryName = accessoryName,
            Weight = weight
        });
    }
}