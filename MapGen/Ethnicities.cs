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
    Deepkin
}

/// <summary>
/// How a fantasy race gets its colour, now that it is a gene rather than a painted texture.
///
/// **The old approach and why it is gone.** This used to repaint free space in the mod's own copy
/// of <c>gfx/portraits/skin_palette.dds</c>, giving every race its own block of pigment and
/// pointing that race's <c>skin_color</c> rect at it. The texture side of that worked — the
/// builder proved it never touched a pixel a stock ethnicity samples — but the *inheritance* side
/// could not be made to work at all. <c>skin_color</c> is a coordinate into that texture and CK3
/// inherits it by interpolating between the parents' coordinates, so the child of a drow and a
/// human landed on whatever pixel sat between their two blocks: a third race's stripe, or bare
/// gradient. The engine interpolates in texture space and cannot be told the blocks mean anything.
///
/// **The replacement**, which is what Elder Kings does. Every character now samples the ordinary
/// stock human gradient, so <c>skin_color</c> interpolation stays inside human skin tones where it
/// is well behaved, and the race rides on a separate gene — <c>gen_race_skin</c>, declared in
/// BaseFilesToCopy/Core/common/genes/gen_race_skin.txt — that rotates hue and pushes saturation on
/// whatever tone came out. A half-orc gets a human base tone under a half-strength green rotation,
/// which is the answer you would want and the answer the old scheme could not give.
///
/// So a race contributes two things here: <see cref="BaseTone"/>, a rect in stock territory saying
/// roughly how light the race is, and <see cref="TemplateOf"/>, the shift that gives it its
/// character. <see cref="TierRange"/> then decides how hard the shift is pushed, replacing the
/// three painted intensity strips the palette used to carry.
/// </summary>
internal static class RaceSkin
{
    /// <summary>
    /// The <c>gen_race_skin</c> template a race wears, or null for
    /// <see cref="RaceArchetype.Human"/>, which sets the gene at all and so falls to the empty
    /// index-0 template.
    /// </summary>
    public static string? TemplateOf(RaceArchetype archetype) => archetype switch
    {
        RaceArchetype.HighElf => "gen_skin_high_elf",
        RaceArchetype.WoodElf => "gen_skin_wood_elf",
        RaceArchetype.Dwarf => "gen_skin_dwarf",
        RaceArchetype.Orc => "gen_skin_orc",
        RaceArchetype.Gnome => "gen_skin_gnome",
        RaceArchetype.Giantkin => "gen_skin_giantkin",
        RaceArchetype.Deepkin => "gen_skin_deepkin",
        _ => null
    };

    /// <summary>
    /// Where in the stock gradient a race's base tone is drawn from, before the shift.
    ///
    /// <c>x</c> is the undertone axis stock uses — cool European around 0.0-0.5, warm Asian from
    /// 0.6 up — and <c>y</c> is lightness, running from the palest skin at 0.12 to the deepest at
    /// 0.96. Choosing this well matters most for <see cref="RaceArchetype.Deepkin"/>, whose shift
    /// is strongly negative in value: darkening a base that is already deep only crushes it to
    /// black, so the drow draw from the pale half and let the gene do the darkening.
    /// </summary>
    public static (float X1, float Y1, float X2, float Y2) BaseTone(RaceArchetype archetype) => archetype switch
    {
        RaceArchetype.HighElf => (0.00f, 0.15f, 0.45f, 0.32f),
        RaceArchetype.WoodElf => (0.30f, 0.40f, 0.70f, 0.62f),
        RaceArchetype.Dwarf => (0.20f, 0.32f, 0.60f, 0.55f),
        RaceArchetype.Orc => (0.30f, 0.42f, 0.80f, 0.68f),
        RaceArchetype.Gnome => (0.30f, 0.35f, 0.80f, 0.58f),
        RaceArchetype.Giantkin => (0.00f, 0.30f, 0.45f, 0.52f),
        RaceArchetype.Deepkin => (0.00f, 0.30f, 0.50f, 0.52f),
        _ => (0.10f, 0.25f, 0.70f, 0.55f)
    };

    /// <summary>
    /// How hard to push the shift, by the map's fantasy level — the same job the three painted
    /// strips per race used to do. The spread inside each band is what makes one orc greener than
    /// the next rather than every orc in a culture being identical.
    /// </summary>
    public static (float Min, float Max) TierRange(FantasyRaceMode mode) => mode switch
    {
        FantasyRaceMode.HighFantasy => (0.50f, 0.70f),
        FantasyRaceMode.ExoticSurreal => (0.92f, 1.00f),
        _ => (0.06f, 0.20f)
    };
}

/// <summary>
/// The genes whose values ARE the race — the set that snaps rather than blends.
///
/// This table is read from two places, and that is its whole reason to exist as a table:
/// <see cref="Ethnicities.ApplyMorphGenes"/> feeds it through <c>Shape</c> so a race's ethnicity
/// carries these values with tier scaling, jitter and bell weighting; and
/// <see cref="Emit.RaceMorphWriter"/> emits the same values into
/// gfx/portraits/portrait_modifiers/99_gen_race_morphs.txt, where they are forced by phenotype
/// trait at render time so a mixed-parentage child (or an engine-generated courtier) reads as its
/// race no matter what its inherited DNA says. Tune a value here and both stay in step; the drift
/// between an ethnicity and its enforcement is exactly the bug class this prevents.
///
/// Everything NOT in this table — nose, eyes, mouth, cheeks, wrinkles, hair, colouring — is
/// authored in <see cref="Ethnicities.ApplyMorphGenes"/> alone and inherits from parents normally,
/// which is what keeps family resemblance alive.
/// </summary>
internal sealed record RaceMorph(string Gene, string Template, float Min, float Max, bool Tiered = true);

internal static class RaceMorphs
{
    public static IReadOnlyList<RaceMorph> Of(RaceArchetype archetype) => archetype switch
    {
        RaceArchetype.HighElf =>
        [
            new("gene_height", "normal_height", 0.60f, 0.70f),
            new("gene_bs_body_type", "body_fat_head_fat_low", 0.46f, 0.56f),
            new("gene_bs_body_shape", "body_shape_hourglass_half", 0.14f, 0.30f, Tiered: false),
            new("gene_bs_ear_angle", "ear_angle_pos", 0.62f, 0.80f),
            new("gene_bs_ear_bend", "ear_both_bend_pos", 0.85f, 1.00f),
            new("gene_bs_ear_outward", "ear_outward_pos", 0.20f, 0.40f),
            new("gene_bs_ear_size", "ear_size_pos", 0.30f, 0.50f),
            new("gene_jaw_width", "jaw_width_neg", 0.34f, 0.44f),
        ],
        RaceArchetype.WoodElf =>
        [
            new("gene_height", "normal_height", 0.46f, 0.56f),
            new("gene_bs_body_type", "body_fat_head_fat_low", 0.47f, 0.57f),
            new("gene_bs_body_shape", "body_shape_triangle_half", 0.35f, 0.55f, Tiered: false),
            new("gene_bs_ear_angle", "ear_angle_pos", 0.58f, 0.76f),
            new("gene_bs_ear_bend", "ear_both_bend_pos", 0.75f, 0.95f),
            new("gene_bs_ear_outward", "ear_outward_pos", 0.20f, 0.40f),
            new("gene_bs_ear_size", "ear_size_pos", 0.25f, 0.45f),
            new("gene_jaw_width", "jaw_width_neg", 0.25f, 0.45f),
        ],
        RaceArchetype.Dwarf =>
        [
            new("gene_height", "normal_height", 0.34f, 0.44f),
            new("gene_bs_body_type", "body_fat_head_fat_medium", 0.60f, 0.75f),
            new("gene_bs_body_shape", "body_shape_rectangle_full", 0.75f, 1.00f, Tiered: false),
            new("gene_jaw_width", "jaw_width_pos", 0.80f, 1.00f),
            new("gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.65f, 0.90f),
            new("gene_bs_ear_size", "ear_size_neg", 0.35f, 0.55f),
        ],
        RaceArchetype.Orc =>
        [
            new("gene_height", "normal_height", 0.58f, 0.72f),
            new("gene_bs_body_type", "body_fat_head_fat_medium", 0.52f, 0.64f),
            new("gene_bs_body_shape", "body_shape_triangle_full", 0.80f, 1.00f, Tiered: false),
            new("gene_jaw_width", "jaw_width_pos", 0.88f, 1.00f),
            new("gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.80f, 1.00f),
        ],
        RaceArchetype.Gnome =>
        [
            new("gene_height", "normal_height", 0.06f, 0.22f),
            new("gene_bs_body_type", "body_fat_head_fat_low", 0.22f, 0.38f),
            new("gene_bs_body_shape", "body_shape_average", 0.00f, 0.14f, Tiered: false),
            new("gene_bs_ear_size", "ear_size_pos", 0.85f, 1.00f),
            new("gene_bs_ear_outward", "ear_outward_pos", 0.80f, 1.00f),
            new("gene_bs_ear_bend", "ear_both_bend_pos", 0.70f, 0.95f),
        ],
        RaceArchetype.Giantkin =>
        [
            new("gene_height", "normal_height", 0.86f, 1.00f),
            new("gene_bs_body_type", "body_fat_head_fat_full", 0.52f, 0.62f),
            new("gene_bs_body_shape", "body_shape_triangle_full", 0.72f, 0.95f, Tiered: false),
            new("gene_jaw_width", "jaw_width_pos", 0.75f, 1.00f),
            new("gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.65f, 0.90f),
            new("gene_bs_ear_size", "ear_size_neg", 0.50f, 0.80f),
        ],
        RaceArchetype.Deepkin =>
        [
            new("gene_height", "normal_height", 0.46f, 0.58f),
            new("gene_bs_body_type", "body_fat_head_fat_low", 0.45f, 0.55f),
            new("gene_bs_body_shape", "body_shape_hourglass_half", 0.08f, 0.26f, Tiered: false),
            new("gene_bs_ear_angle", "ear_angle_pos", 0.60f, 0.78f),
            new("gene_bs_ear_bend", "ear_both_bend_pos", 0.80f, 1.00f),
            new("gene_bs_ear_outward", "ear_outward_pos", 0.25f, 0.45f),
            new("gene_jaw_width", "jaw_width_neg", 0.32f, 0.42f),
        ],
        _ => []
    };
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

    /// <summary>
    /// Colouring variants over this look, and the only thing a culture ever points at.
    ///
    /// This mirrors how vanilla is built rather than being an invention. `caucasian_base` carries
    /// the face genes, is `visible = no`, and is referenced by no culture at all; the four things
    /// cultures actually name — `caucasian_blond`, `_ginger`, `_brown_hair`, `_dark_hair` — declare
    /// no genes of their own, only `template = caucasian_base` and a different `hair_color` block.
    /// A European culture then lists all four with weights, which is where within-culture variety
    /// comes from. Measured across stock: 244 cultures share 38 ethnicities, and 92 of those
    /// cultures name more than one.
    ///
    /// We had neither half. One ethnicity per heritage carried both the race and the colouring, and
    /// every culture emitted a single `100 = key`, so a 42-culture world had seven looks in it and
    /// no two siblings could differ in hair. Splitting the two lets the number of LOOKS scale with
    /// the world while the number of RACES stays what the user asked for.
    ///
    /// Safe only because race-defining genes are now forced by phenotype trait at render time —
    /// see Emit/RaceMorphWriter.cs. Before that, more colouring variety per race would have been
    /// more chances to drift out of the race.
    /// </summary>
    public List<EthnicityVariant> Variants { get; } = [];
}

/// <summary>
/// One colouring of a base look: a hair palette, an eye palette, and nothing else. Emitted as
/// `template = &lt;base key&gt;` plus those two blocks, exactly as vanilla's `caucasian_blond` is.
/// </summary>
public sealed class EthnicityVariant
{
    public required string Key { get; init; }
    public required string LocalizedName { get; init; }
    public Dictionary<string, List<ColorPaletteRange>> ColorGenes { get; } = [];
}

public sealed class EthnicityMap
{
    public required Dictionary<string, EthnicityDef> Ethnicities { get; init; }
    public required Dictionary<Culture, EthnicityDef> ByCulture { get; init; }
    public required Dictionary<Heritage, EthnicityDef> ByHeritage { get; init; }
    public required Dictionary<string, EthnicityDef> ByCultureKey { get; init; }
    public required Dictionary<string, EthnicityDef> ByHeritageKey { get; init; }

    /// <summary>
    /// The weighted ethnicity list each culture actually writes, which is a selection of its base's
    /// variants rather than the base itself. Held per culture, not per base, because that is the
    /// whole point: under TieRaceToHeritage every culture in a heritage shares one base, and the
    /// differing selections here are what stop them being the same people.
    /// </summary>
    public required Dictionary<Culture, List<(string Key, int Weight)>> VariantsByCulture { get; init; }

    /// <summary>The list a culture should emit, falling back to its base when it has no selection.</summary>
    public List<(string Key, int Weight)> VariantsFor(Culture culture) =>
        VariantsByCulture.TryGetValue(culture, out var picked) && picked.Count > 0
            ? picked
            : [(For(culture).Key, 100)];

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
        var byCultureVariants = new Dictionary<Culture, List<(string Key, int Weight)>>();
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

        // 3. Track unique assigned races so far.
        //
        // Seeded from the heritage assignments ONLY when cultures actually wear them. Under
        // TieRaceToHeritage = false a culture builds its own ethnicity and the heritage's is never
        // worn by anybody, so counting those races as "placed" is counting races nobody has. That
        // miscount is what made a high GuaranteedRaceCount deliver FEWER races than a low one: with
        // a quota above the eight-race pool, usedArchetypes could never reach it, so every culture
        // took the quota branch below, found `available` empty, and fell through to the terrain
        // roll — throwing away the 70% inherit-your-heritage's-race path that was doing most of the
        // spreading. On a terrain-skewed map the roll then piled onto whatever the terrain favoured.
        var usedArchetypes = cfg.TieRaceToHeritage
            ? new HashSet<RaceArchetype>(heritageArchetypes.Values)
            : [];

        // The quota can never exceed the pool it draws from, and asking for more used to be actively
        // harmful rather than merely unmet — see above. Clamped once here so both the branch below
        // and the shortfall report agree on what was actually achievable.
        var quotaPool = FantasyPoolFor(cfg);
        int targetQuota = Math.Clamp(cfg.GuaranteedRaceCount, 1, Math.Max(1, quotaPool.Count));

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
                && FantasyPoolFor(cfg).Contains(tagged)
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
                if (usedArchetypes.Count < targetQuota && cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly)
                {
                    // Require has to be honoured here too. This branch used to pick from the
                    // unplaced races with no terrain test at all, which is how an all-forest map
                    // under Require still produced dwarves, giantkin and drow: the rule was applied
                    // in the heritage phase and in PickArchetypeForCulture, but not on the path
                    // between them. Falling back to the unfiltered list when nothing fits keeps the
                    // quota meaningful on a map where no race is a clean match.
                    var available = quotaPool
                        .Where(a => !usedArchetypes.Contains(a))
                        .ToList();

                    // No "if nothing fits, use the unfiltered list anyway" fallback here. That is
                    // precisely the behaviour Require exists to forbid, and it silently reinstated
                    // the bug: once the two races a forest can hold were placed, every later
                    // culture found `fitting` empty and helped itself to dwarves and giantkin.
                    // Emptying `available` instead drops through to PickArchetypeForCulture, which
                    // honours the rule and settles on Human when the land suits nobody.
                    if (cfg.RaceTerrain == RaceTerrainRule.Require)
                    {
                        var shares = GetTerrainShares([culture], provinceTerrain, rng);
                        available = available.Where(a => FitsTerrain(a, shares)).ToList();
                    }

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
            byCultureVariants[culture] = PickCultureVariants(cultureEth, rng);
        }

        var tallies = byCulture.Values
            .GroupBy(e => e.Archetype)
            .Select(g => $"{g.Count()} {g.Key}");

        // Counted from byCulture, not from usedArchetypes: the tally beside it comes from the
        // cultures, and the two used to be able to disagree.
        int deliveredRaces = byCulture.Values.Select(e => e.Archetype).Distinct().Count();

        Console.WriteLine($"  ethnicities: {byCulture.Count} cultures across {deliveredRaces} distinct races -> {string.Join(", ", tallies)}");

        // Delivering fewer races than asked for used to be silent, which made a clipped quota
        // look like bad luck in the seed. Say which constraint actually bound.
        int wanted = Math.Max(1, cfg.GuaranteedRaceCount);
        if (cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly
            && deliveredRaces < wanted)
        {
            // Ordered by which constraint actually binds first. The pool cap leads because it is
            // the only one the user cannot fix by changing culture density — telling someone who
            // asked for ten races to make more heritages sends them after a limit that was never
            // the problem.
            string reason = wanted > quotaPool.Count
                ? $"only {quotaPool.Count} races exist in this mode"
                  + (cfg.RaceMode == FantasyRaceMode.ExoticSurreal
                        ? " — that is the ceiling"
                        : " — ExoticSurreal adds a ninth")
                : cfg.RaceTerrain == RaceTerrainRule.Require
                ? "RaceTerrain is Require, so races with no suitable terrain anywhere on this map were left unplaced rather than misplaced — set it to Prefer to settle them anyway"
                : !cfg.TieRaceToHeritage && byCulture.Count < wanted
                ? $"only {byCulture.Count} culture(s) exist — lower CountiesPerCulture to make more"
                : cfg.TieRaceToHeritage && heritages.Count < wanted
                    ? $"only {heritages.Count} heritage(s) exist — lower CulturesPerHeritage or CountiesPerCulture to make more, or untick TieRaceToHeritage to place races per culture instead"
                    : "the terrain roll did not spread them this seed — try another";
            Console.WriteLine($"  WARNING: asked for {wanted} distinct races but delivered {deliveredRaces}: {reason}");
        }

        return new EthnicityMap
        {
            Ethnicities = ethnicities,
            ByCulture = byCulture,
            ByHeritage = byHeritage,
            ByCultureKey = byCultureKey,
            ByHeritageKey = byHeritageKey,
            VariantsByCulture = byCultureVariants
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

        // Which races Require would let each heritage hold. Judged against the aggregate AND
        // against every member culture, and a race passes if either does. The aggregate alone is
        // how a whole race class went missing on a large map: a heritage spanning half a continent
        // averages its terrain toward the map-wide mix, so the mountains its dwarves would live in
        // are 2% of the whole even when one member culture is nothing but mountains. The culture
        // is the scale a race actually settles at; the aggregate is kept in the union for the
        // opposite case, where the wanted terrain is real but spread thinly across members.
        Dictionary<Heritage, HashSet<RaceArchetype>>? heritageFits = null;
        if (cfg.RaceTerrain == RaceTerrainRule.Require)
        {
            heritageFits = new Dictionary<Heritage, HashSet<RaceArchetype>>();
            foreach (var heritage in heritages)
            {
                var fits = TerrainRaces.Where(r => FitsTerrain(r, heritageTerrain[heritage])).ToHashSet();
                foreach (var culture in cultures)
                {
                    if (culture.Heritage != heritage && culture.Heritage?.Key != heritage.Key) continue;
                    var cultureShares = GetTerrainShares([culture], provinceTerrain, rng);
                    foreach (var r in TerrainRaces)
                        if (!fits.Contains(r) && FitsTerrain(r, cultureShares))
                            fits.Add(r);
                }
                heritageFits[heritage] = fits;
            }
        }

        // Pool of available candidate races
        // FantasyPoolFor already includes Exotic in ExoticSurreal; adding it again here would put
        var candidatePool = FantasyPoolFor(cfg).ToList();

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
                    // ineligible, and if no heritage will take it the race goes unplaced. The
                    // fitness is the culture-granular union computed above, never the raw
                    // aggregate — see heritageFits for why the difference decides whole races.
                    if (heritageFits is not null
                        && race != RaceArchetype.Human
                        && !heritageFits[h].Contains(race))
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

        // 2. Remainder Phase: remaining heritages roll probabilistically according to their
        //    biomes — but a race nobody has yet is always preferred over a second helping of one
        //    somebody does. Without that, a map whose commonest terrain suits one race stacks
        //    culture after culture onto it while rarer-terrain races never appear at all; with it,
        //    duplicates only begin once every race that fits somewhere is on the map. assignedRaces
        //    keeps accumulating here so the rule holds across the whole remainder, not per roll.
        foreach (var h in remainingHeritages)
        {
            var pick = PickWeightedArchetype(heritageTerrain[h], heritageFits?[h], assignedRaces, cfg, rng);
            assignments[h] = pick;
            assignedRaces.Add(pick);
        }

        return assignments;
    }

    /// <summary>Terrain the heritage has most of — the old modal reading, kept for the remainder roll.</summary>
    private static TerrainClass DominantOf(Dictionary<TerrainClass, double> shares) =>
        shares.OrderByDescending(kv => kv.Value).First().Key;

    /// <summary>
    /// The races a map may draw on, given its mode. One definition so the culture-level quota, the
    /// heritage diversity phase and the shortfall report cannot disagree about what was achievable
    /// — they previously each built their own list and could disagree about what was reachable.
    /// </summary>
    private static IReadOnlyList<RaceArchetype> FantasyPoolFor(MapConfig cfg)
    {
        // ExoticSurreal is an INTENSITY setting, not a ninth race. It pushes every race's colour
        // and morphology further from human; it does not add a people of its own. The roster is the
        // same eight in every mode.
        return
        [
            RaceArchetype.Human, RaceArchetype.Dwarf, RaceArchetype.WoodElf, RaceArchetype.HighElf,
            RaceArchetype.Orc, RaceArchetype.Gnome, RaceArchetype.Giantkin, RaceArchetype.Deepkin
        ];
    }

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
    /// </summary>
    private static bool FitsTerrain(RaceArchetype race, Dictionary<TerrainClass, double> shares)
    {
        if (race is RaceArchetype.Human) return true;

        foreach (var (terrain, share) in shares)
            if (GetTerrainAffinityScore(race, terrain) > 1 && Reach(share) >= RequiredReach)
                return true;

        return false;
    }

    /// <summary>
    /// The fantasy races a terrain roll may land on, Human excluded — it is decided before this
    /// point by its own roll rather than competing on terrain.
    /// </summary>
    private static readonly RaceArchetype[] TerrainRaces =
    [
        RaceArchetype.Dwarf, RaceArchetype.HighElf, RaceArchetype.WoodElf, RaceArchetype.Orc,
        RaceArchetype.Gnome, RaceArchetype.Giantkin, RaceArchetype.Deepkin
    ];

    /// <summary>Every race a culture-level roll may produce.</summary>
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
    /// desert may throw up a high elf, which no affinity score would allow. Require draws from the
    /// culture-granular fitness set computed by the caller instead, because the mode hides exactly
    /// the minority terrain a race needs.
    ///
    /// Whatever the rule produces as candidates, a race not yet on the map wins over one that is —
    /// see the remainder phase for why that ordering is a guarantee and not a preference.
    /// </summary>
    private static RaceArchetype PickWeightedArchetype(
        Dictionary<TerrainClass, double> shares,
        HashSet<RaceArchetype>? requireFits,
        HashSet<RaceArchetype> used,
        MapConfig cfg,
        Rng rng)
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

        IReadOnlyList<RaceArchetype> candidates;

        if (cfg.RaceTerrain == RaceTerrainRule.Ignore)
        {
            candidates = TerrainRaces;
        }
        else if (requireFits is not null)
        {
            var fitting = TerrainRaces.Where(requireFits.Contains).ToList();
            if (fitting.Count == 0) return RaceArchetype.Human;
            candidates = fitting;
        }
        else
        {
            candidates = DominantOf(shares) switch
            {
                TerrainClass.Mountains or TerrainClass.DesertMountains
                    => [RaceArchetype.Dwarf, RaceArchetype.Orc, RaceArchetype.Giantkin],

                TerrainClass.Hills
                    => [RaceArchetype.Dwarf, RaceArchetype.Orc, RaceArchetype.Gnome, RaceArchetype.Human],

                TerrainClass.Forest or TerrainClass.Taiga or TerrainClass.Jungle
                    => [RaceArchetype.WoodElf, RaceArchetype.Gnome, RaceArchetype.Orc],

                TerrainClass.Arctic
                    => [RaceArchetype.Giantkin, RaceArchetype.Deepkin, RaceArchetype.Dwarf],

                TerrainClass.Desert
                    => [RaceArchetype.Orc, RaceArchetype.Gnome, RaceArchetype.Deepkin, RaceArchetype.HighElf],

                TerrainClass.Wetlands
                    => [RaceArchetype.Gnome, RaceArchetype.Deepkin, RaceArchetype.WoodElf],

                _ => [RaceArchetype.HighElf, RaceArchetype.Giantkin, RaceArchetype.Orc, RaceArchetype.Human, RaceArchetype.Dwarf]
            };
        }

        // One of each before seconds of any: a candidate the map does not have yet always beats
        // one it does. Only when every candidate is already represented does this fall back to a
        // straight pick, which is where duplicates legitimately begin.
        var fresh = candidates.Where(r => !used.Contains(r)).ToList();
        return fresh.Count > 0 ? rng.Pick(fresh) : rng.Pick(candidates);
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
        BuildVariants(def, rng);

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
                // The race-defining genes come from the shared table so the ethnicity and the
                // portrait-modifier enforcement (Emit/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.58f, 0.74f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_neg", 0.34f, 0.44f);
                // Ears swept up and back, NOT enlarged and NOT pushed off the skull. Vanilla's ear
                // genes make a round ear bigger and splay it outward; pushing all four toward 1.0
                // gets a comic ear rather than an elegant one, so size and outward stay low while
                // angle and bend — the two that sweep it — carry the shape.
                // Upswept eyes are the strongest elf cue stock geometry has after height, so the
                // high elf takes it harder than the wood elf does.
                Shape(def, rng, f, "gene_eye_angle", "eye_angle_pos", 0.58f, 0.70f);
                // No gene_eye_distance. Close-set eyes read as unsettling in a human face at any
                // strength, and vanilla holds this gene to 0.45-0.55 for a beautiful character —
                // pushing it to 0.20 was working directly against the look this race wants.
                Shape(def, rng, f, "gene_bs_eye_fold_shape", "eye_fold_shape_02_pos", 0.18f, 0.34f);
                Shape(def, rng, f, "gene_head_height", "head_height_pos", 0.54f, 0.64f);
                Shape(def, rng, f, "gene_forehead_height", "forehead_height_pos", 0.54f, 0.64f);
                Shape(def, rng, f, "gene_forehead_brow_height", "forehead_brow_height_pos", 0.56f, 0.68f);
                Shape(def, rng, f, "gene_bs_forehead_brow_curve", "forehead_brow_curve_pos", 0.55f, 0.80f);
                Shape(def, rng, f, "gene_bs_cheek_forward", "cheek_forward_pos", 0.60f, 0.85f);
                Shape(def, rng, f, "gene_bs_cheek_height", "cheek_height_pos", 0.55f, 0.80f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_neg", 0.36f, 0.46f);
                Shape(def, rng, f, "gene_bs_nose_length", "nose_length_pos", 0.25f, 0.45f);
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
                AddGene(def, "complexion", "complexion_beauty_1", 0.55f, 0.90f);
                AddGene(def, "gene_body_hair", "body_hair_sparse", 0.10f, 0.40f);
                AddGene(def, "gene_baldness", "no_baldness", 0.0f, 0.15f);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 70);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 30);
                break;

            case RaceArchetype.WoodElf:
                // Human height, and a hunter rather than an aristocrat — broader skull, sharper
                // cheekbones and appreciably more muscle than the high elf carries.
                // The race-defining genes come from the shared table so the ethnicity and the
                // portrait-modifier enforcement (Emit/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.50f, 0.75f);
                // Slanted eyes come from gene_eye_angle alone. There is no gene_bs_eye_slant in
                // vanilla — nothing matching "slant" exists at all.
                Shape(def, rng, f, "gene_eye_angle", "eye_angle_pos", 0.55f, 0.75f);
                Shape(def, rng, f, "gene_bs_eye_size", "eye_size_pos", 0.30f, 0.52f);
                Shape(def, rng, f, "gene_head_width", "head_width_pos", 0.52f, 0.64f);
                Shape(def, rng, f, "gene_bs_cheek_forward", "cheek_forward_pos", 0.45f, 0.70f);
                Shape(def, rng, f, "gene_bs_cheek_height", "cheek_height_pos", 0.50f, 0.70f);
                Shape(def, rng, f, "gene_bs_nose_size", "nose_size_neg", 0.26f, 0.44f);
                Shape(def, rng, f, "gene_bs_nose_ridge_angle", "nose_ridge_angle_pos", 0.45f, 0.70f);
                AddGene(def, "gene_age", "old_beauty_1", 0.0f, 0.7f, weight: 70);
                AddGene(def, "gene_age", "no_aging", 0.0f, 1.0f, weight: 30);
                AddGene(def, "gene_eyebrows_fullness", "layer_2_avg_thickness", 0.0f, 1.0f);
                // The lightest and blotchiest of the numbered head textures: +2.4 lightness and
                // +1.2 unevenness against the base, which is as close to freckled as stock gets.
                AddGene(def, "complexion", "complexion_beauty_1", 0.35f, 0.65f);
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
                // The race-defining genes come from the shared table so the ethnicity and the
                // portrait-modifier enforcement (Emit/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_width", "neck_width_pos", 0.85f, 1.0f);
                Shape(def, rng, f, "gene_neck_length", "neck_length_neg", 0.05f, 0.25f);
                Shape(def, rng, f, "gene_head_width", "head_width_pos", 0.70f, 0.95f);
                Shape(def, rng, f, "gene_jaw_forward", "jaw_forward_pos", 0.55f, 0.85f);
                Shape(def, rng, f, "gene_bs_jaw_def", "jaw_def_pos", 0.60f, 0.90f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_pos", 0.75f, 0.95f);
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
                // The race-defining genes come from the shared table so the ethnicity and the
                // portrait-modifier enforcement (Emit/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_width", "neck_width_pos", 0.80f, 1.0f);
                // A brow that juts without also sitting low over a sunken eye reads as a bump
                // rather than a scowl, so the ridge, its height, the forehead slope and the eye
                // behind it all move together.
                Shape(def, rng, f, "gene_forehead_brow_height", "forehead_brow_height_neg", 0.15f, 0.35f);
                Shape(def, rng, f, "gene_forehead_angle", "forehead_angle_neg", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_eye_depth", "eye_depth_pos", 0.65f, 0.90f);
                Shape(def, rng, f, "gene_bs_eye_size", "eye_size_neg", 0.45f, 0.70f);
                Shape(def, rng, f, "gene_jaw_forward", "jaw_forward_pos", 0.82f, 1.0f);
                Shape(def, rng, f, "gene_bs_jaw_def", "jaw_def_pos", 0.85f, 1.0f);
                // Vanilla has no tusk gene of any kind. What a tusked mouth actually reads as is a
                // heavy padded lower lip under a thin upper one with the corners pulled down, and
                // all four of those are stock genes.
                Shape(def, rng, f, "gene_mouth_forward", "mouth_forward_pos", 0.65f, 0.95f);
                Shape(def, rng, f, "gene_mouth_lower_lip_size", "mouth_lower_lip_size_pos", 0.60f, 0.90f);
                Shape(def, rng, f, "gene_mouth_upper_lip_size", "mouth_upper_lip_size_neg", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_bs_mouth_lower_lip_pad", "mouth_lower_lip_pad_pos", 0.55f, 0.85f);
                Shape(def, rng, f, "gene_mouth_corner_height", "mouth_corner_height_neg", 0.15f, 0.35f);
                Shape(def, rng, f, "gene_bs_cheek_width", "cheek_width_pos", 0.70f, 0.95f);
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
                // The race-defining genes come from the shared table so the ethnicity and the
                // portrait-modifier enforcement (Emit/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.55f, 0.80f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_neg", 0.20f, 0.40f);
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
                // The race-defining genes come from the shared table so the ethnicity and the
                // portrait-modifier enforcement (Emit/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_width", "neck_width_pos", 0.85f, 1.0f);
                Shape(def, rng, f, "gene_head_width", "head_width_pos", 0.60f, 0.85f);
                Shape(def, rng, f, "gene_head_height", "head_height_pos", 0.55f, 0.80f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_pos", 0.70f, 0.95f);
                // Small ears on a large skull is what sells the scale — a big head with big ears
                // just reads as a normal head. EK2's giant does the same thing.
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
                // The race-defining genes come from the shared table so the ethnicity and the
                // portrait-modifier enforcement (Emit/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.56f, 0.72f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_neg", 0.32f, 0.42f);
                Shape(def, rng, f, "gene_bs_eye_size", "eye_size_pos", 0.45f, 0.68f);
                Shape(def, rng, f, "gene_eye_depth", "eye_depth_pos", 0.36f, 0.46f);
                Shape(def, rng, f, "gene_eye_angle", "eye_angle_pos", 0.56f, 0.70f);
                Shape(def, rng, f, "gene_bs_cheek_forward", "cheek_forward_pos", 0.52f, 0.74f);
                Shape(def, rng, f, "gene_bs_cheek_height", "cheek_height_pos", 0.50f, 0.72f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_neg", 0.34f, 0.44f);
                Shape(def, rng, f, "gene_bs_nose_length", "nose_length_neg", 0.20f, 0.38f);
                AddGene(def, "gene_age", "old_beauty_1", 0.0f, 0.6f, weight: 65);
                AddGene(def, "gene_age", "no_aging", 0.0f, 1.0f, weight: 35);
                AddGene(def, "gene_eyebrows_fullness", "layer_2_low_thickness", 0.0f, 1.0f);
                AddGene(def, "complexion", "complexion_beauty_1", 0.50f, 0.85f);
                AddGene(def, "gene_body_hair", "body_hair_sparse", 0.15f, 0.40f);
                AddGene(def, "gene_baldness", "no_baldness", 0.0f, 0.15f);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 80);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 20);
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
    internal static float NeutralOf(string geneKey) =>
        geneKey.StartsWith("gene_bs_", StringComparison.Ordinal) && geneKey != "gene_bs_body_type"
            ? 0.0f
            : 0.5f;

    /// <summary>
    /// How far a race's body may depart from a plain human, by the map's fantasy level — the same
    /// idea <see cref="RaceSkin.TierRange"/> applies to skin, applied to shape. On a low-fantasy map
    /// a dwarf should be a short broad people rather than a caricature, so every racial gene is
    /// pulled most of the way back toward neutral; on a surreal one it is pushed past its authored
    /// value and clamped at the gene's limit.
    /// </summary>
    internal static float MorphIntensity(FantasyRaceMode mode) => mode switch
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
    /// are fixed and nothing else varies them. A small per-gene shift
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
                ApplyRaceSkin(def, archetype, mode, rng);
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
                ApplyRaceSkin(def, archetype, mode, rng);
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
                ApplyRaceSkin(def, archetype, mode, rng);
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
                ApplyRaceSkin(def, archetype, mode, rng);
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
                ApplyRaceSkin(def, archetype, mode, rng);
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
                ApplyRaceSkin(def, archetype, mode, rng);
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
                ApplyRaceSkin(def, archetype, mode, rng);
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

    /// <summary>
    /// Splits a finished look into colouring variants, one per hair band it already carries.
    ///
    /// A variant is NOT "only blonde". It is the same palette re-weighted to lean on one band, which
    /// is what vanilla's `caucasian_blond` actually is — the blond entry is heavy, the others are
    /// still there. That distinction matters: per-CHARACTER variety already came from the weighted
    /// bands inside one ethnicity and was never the problem. What was missing was per-CULTURE
    /// variety, because every culture in a heritage pointed at the same single definition and
    /// therefore the same single distribution.
    ///
    /// Deriving the variants from the base's own bands rather than from a fresh palette is what
    /// keeps them in-race: a drow cannot acquire blonde hair here, because a drow's band list never
    /// contained any. The race envelope is set once in ApplyColorGenes and this only redistributes
    /// inside it.
    /// </summary>
    private static void BuildVariants(EthnicityDef def, Rng rng)
    {
        if (!def.ColorGenes.TryGetValue("hair_color", out var bands) || bands.Count < 2)
            return;

        // Four is vanilla's own ceiling for one base, and past it the leans stop being tellable
        // apart on a portrait.
        int count = Math.Min(4, bands.Count);

        for (int i = 0; i < count; i++)
        {
            var variant = new EthnicityVariant
            {
                Key = $"{def.Key}_v{i}",
                LocalizedName = def.LocalizedName
            };

            var reweighted = new List<ColorPaletteRange>(bands.Count);
            for (int b = 0; b < bands.Count; b++)
            {
                // The lean, not an exclusion. The dominant band takes most of the mass and the rest
                // keep a real minority share, so a culture that leans dark still throws the
                // occasional fair head — which is the whole texture the split is trying to buy.
                int weight = b == i ? 70 : Math.Max(4, 30 / Math.Max(1, bands.Count - 1));
                reweighted.Add(new ColorPaletteRange
                {
                    X1 = bands[b].X1, Y1 = bands[b].Y1,
                    X2 = bands[b].X2, Y2 = bands[b].Y2,
                    Weight = weight
                });
            }

            variant.ColorGenes["hair_color"] = reweighted;
            def.Variants.Add(variant);
        }
    }

    /// <summary>
    /// The variants one culture draws from, and their weights.
    ///
    /// Two or three of the base's variants rather than all of them, so neighbouring cultures on the
    /// same base are visibly different peoples rather than the same distribution twice. The base
    /// itself is never named here — it is `visible = no` and carries the morphology, exactly as
    /// `caucasian_base` does.
    /// </summary>
    private static List<(string Key, int Weight)> PickCultureVariants(EthnicityDef def, Rng rng)
    {
        // A look with nothing to vary — a race whose palette had a single band — still has to give
        // the culture something to point at, so it points at the base.
        if (def.Variants.Count == 0)
            return [(def.Key, 100)];

        var pool = new List<EthnicityVariant>(def.Variants);
        rng.Shuffle(pool);

        int take = Math.Min(pool.Count, rng.Chance(0.55) ? 2 : 3);
        var picked = new List<(string Key, int Weight)>(take);

        // A clear lead and a tail, rather than an even split: an even split makes every culture the
        // same blend of the same variants, which is the sameness this is meant to break.
        int lead = take == 2 ? 70 : 55;
        int rest = (100 - lead) / Math.Max(1, take - 1);

        for (int i = 0; i < take; i++)
            picked.Add((pool[i].Key, i == 0 ? lead : rest));

        return picked;
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
    /// A fantasy race's colouring: a base tone drawn from the stock human gradient, plus the
    /// <c>gen_race_skin</c> shift that turns it into that race's. See <see cref="RaceSkin"/> for
    /// why this is two genes rather than one coordinate into a repainted palette.
    ///
    /// The base tone is emitted as three overlapping bands rather than one flat rect so a
    /// population has light and dark members the way vanilla ethnicities do, and the shift is a
    /// single entry because every member of a people shares its hue.
    /// </summary>
    private static void ApplyRaceSkin(
        EthnicityDef def, RaceArchetype archetype, FantasyRaceMode mode, Rng rng)
    {
        string? template = RaceSkin.TemplateOf(archetype);

        // Humans set neither, and that is the whole reason they are unaffected by any of this:
        // no skin_color means they inherit their vanilla template's complexion, and no
        // gen_race_skin means they fall to its empty index-0 template and take no shift.
        if (template is null) return;

        var (x1, y1, x2, y2) = RaceSkin.BaseTone(archetype);
        float midY = (y1 + y2) / 2f;
        float qY = (y2 - y1) / 4f;
        AddColor(def, "skin_color", x1, y1, x2, midY + qY, weight: 40);          // lighter half
        AddColor(def, "skin_color", x1, midY - qY, x2, y2, weight: 40);          // darker half
        AddColor(def, "skin_color", x1, y1, x2, y2, weight: 20);                 // the whole spread

        var (lo, hi) = RaceSkin.TierRange(mode);
        AddGene(def, "gen_race_skin", template, lo, hi, weight: 10);
    }
}