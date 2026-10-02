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
    DuskElf,
    // Appended, not inserted, so every existing race keeps its ordinal.
    Hornkin
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
        RaceArchetype.DuskElf => "gen_skin_deepkin",
        RaceArchetype.Hornkin => "gen_skin_hornkin",
        _ => null
    };

    /// <summary>
    /// Where in the stock gradient a race's base tone is drawn from, before the shift.
    ///
    /// <c>x</c> is the undertone axis stock uses — cool European around 0.0-0.5, warm Asian from
    /// 0.6 up — and <c>y</c> is lightness, running from the palest skin at 0.12 to the deepest at
    /// 0.96. Choosing this well matters most for <see cref="RaceArchetype.DuskElf"/>, whose shift
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
        RaceArchetype.DuskElf => (0.00f, 0.30f, 0.50f, 0.52f),
        // Weathered herders: mid tones, sun-darkened rather than pale.
        RaceArchetype.Hornkin => (0.20f, 0.38f, 0.70f, 0.62f),
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
            // The real pointed ear (Emit/Characters/RaceHeadWriter.cs). It replaced ear_angle and ear_bend,
            // which were the stock-geometry approximation of one: stacked on the point, the bend
            // curled the tip forward into a crease (rendered 2026-09-28). Bend is now forced to
            // zero so no inherited human fold can do the same; size and outward stay, giving a
            // bolder ear that sits a little off the skull.
            new(PointedEars.Gene, PointedEars.HighTemplate, 0.80f, 1.00f),
            new("gene_bs_ear_bend", "ear_both_bend_pos", 0.00f, 0.00f, Tiered: false),
            new("gene_bs_ear_outward", "ear_outward_pos", 0.20f, 0.40f),
            new("gene_bs_ear_size", "ear_size_pos", 0.30f, 0.50f),
            new("gene_jaw_width", "jaw_width_neg", 0.34f, 0.44f),
        ],
        RaceArchetype.WoodElf =>
        [
            new("gene_height", "normal_height", 0.46f, 0.56f),
            new("gene_bs_body_type", "body_fat_head_fat_low", 0.47f, 0.57f),
            new("gene_bs_body_shape", "body_shape_triangle_half", 0.35f, 0.55f, Tiered: false),
            // Their own leaf-shaped ear (EarShape.Sylvan); see the high elf above for why angle and
            // bend gave way to the pointed ear.
            new(PointedEars.Gene, PointedEars.SylvanTemplate, 0.80f, 1.00f),
            new("gene_bs_ear_bend", "ear_both_bend_pos", 0.00f, 0.00f, Tiered: false),
            new("gene_bs_ear_outward", "ear_outward_pos", 0.20f, 0.40f),
            new("gene_bs_ear_size", "ear_size_pos", 0.25f, 0.45f),
            new("gene_jaw_width", "jaw_width_neg", 0.25f, 0.45f),
        ],
        RaceArchetype.Dwarf =>
        [
            new("gene_height", "normal_height", 0.34f, 0.44f),
            new("gene_bs_body_type", "body_fat_head_fat_medium", 0.60f, 0.75f),
            new("gene_bs_body_shape", "body_shape_rectangle_full", 0.75f, 1.00f, Tiered: false),
            // Was 0.80-1.00, which with the jaw shape and chin width below read as oversized
            // (user, in game, 2026-09-29) — worst on women; see FemaleOf for their correction.
            new("gene_jaw_width", "jaw_width_pos", 0.70f, 0.90f),
            new("gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.65f, 0.90f),
            new("gene_bs_ear_size", "ear_size_neg", 0.35f, 0.55f),
        ],
        RaceArchetype.Orc =>
        [
            new("gene_height", "normal_height", 0.58f, 0.72f),
            new("gene_bs_body_type", "body_fat_head_fat_medium", 0.52f, 0.64f),
            new("gene_bs_body_shape", "body_shape_triangle_full", 0.80f, 1.00f, Tiered: false),
            // Lower tusks (Emit/Characters/RaceHeadWriter.cs), pinned to the lower lip so they line up on
            // every face. Tiered: a low-fantasy orc shows half-grown ones.
            new(OrcTusks.Gene, OrcTusks.LowerTemplate, 0.80f, 1.00f),
            new("gene_jaw_width", "jaw_width_pos", 0.88f, 1.00f),
            // The heavy brow bone (MapGen/OrcBrow.cs). It replaced gene_bs_forehead_brow_forward at
            // 0.8-1.0, which pinched the inner brows into a frown; that gene is now forced to zero.
            new(OrcBrow.Gene, OrcBrow.OnTemplate, 0.80f, 1.00f),
            new("gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.00f, 0.00f, Tiered: false),
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
            // Was 0.75-1.00; GiantFace's jaw now pushes the gonial angles out itself, so the slider
            // no longer has to carry the width alone. Women get their own value in FemaleOf.
            new("gene_jaw_width", "jaw_width_pos", 0.65f, 0.85f),
            // The brow shelf and heavy jaw (Emit/Characters/RaceHeadWriter.cs, MapGen/GiantFace.cs).
            // It replaced gene_bs_forehead_brow_forward, which only pinched the brows into a frown;
            // that gene is now forced to zero so an inherited human brow cannot stack on the shelf.
            new(GiantFace.Gene, GiantFace.OnTemplate, 0.80f, 1.00f),
            new("gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.00f, 0.00f, Tiered: false),
            new("gene_bs_ear_size", "ear_size_neg", 0.50f, 0.80f),
        ],
        RaceArchetype.DuskElf =>
        [
            new("gene_height", "normal_height", 0.46f, 0.58f),
            new("gene_bs_body_type", "body_fat_head_fat_low", 0.45f, 0.55f),
            new("gene_bs_body_shape", "body_shape_hourglass_half", 0.08f, 0.26f, Tiered: false),
            // Elves too, as drow: a long ear swept back along the skull (EarShape.Drow).
            new(PointedEars.Gene, PointedEars.DrowTemplate, 0.80f, 1.00f),
            new("gene_bs_ear_bend", "ear_both_bend_pos", 0.00f, 0.00f, Tiered: false),
            new("gene_bs_ear_outward", "ear_outward_pos", 0.25f, 0.45f),
            new("gene_jaw_width", "jaw_width_neg", 0.32f, 0.42f),
        ],
        // Highland and steppe herders: rangy rather than bulky, with a plain brow under the horns.
        // The horns themselves are not in this table — they are an accessory gene (a mix of
        // styles per culture, written in ApplyMorphGenes) and are enforced by their own portrait
        // group in Emit/Characters/RaceMorphWriter.cs, together with the skin mound they grow from.
        RaceArchetype.Hornkin =>
        [
            new("gene_height", "normal_height", 0.54f, 0.66f),
            new("gene_bs_body_type", "body_fat_head_fat_low", 0.44f, 0.54f),
            new("gene_bs_body_shape", "body_shape_triangle_half", 0.45f, 0.70f, Tiered: false),
            new("gene_jaw_width", "jaw_width_pos", 0.60f, 0.80f),
            // Forced to zero: at 0.55-0.8 vanilla's slider pinched the brows into a frown rather than
            // building bone (rendered 2026-09-29). A light brow-bone shape barely showed, so the
            // horns alone carry the look.
            new("gene_bs_forehead_brow_forward", "forehead_brow_forward_pos", 0.00f, 0.00f, Tiered: false),
        ],
        _ => []
    };

    /// <summary>
    /// What the elves' men get on top of <see cref="Of"/>, forced at render time only — an ethnicity
    /// cannot split by sex, so this has no <see cref="Ethnicities.ApplyMorphGenes"/> counterpart.
    ///
    /// The elf tables narrow jaw, chin and neck for everyone, and vanilla's handsome men lean on a
    /// solid jaw. Take it away and nothing narrows the mouth to match, so an inherited human mouth
    /// became the widest thing in the lower face, full-lipped and pushed out past a receding chin —
    /// under upswept eyes, arched brows and thin plucked eyebrows (rendered 2026-09-28, a high elf
    /// king who read as made-up rather than ageless). So for men: a gentler jaw narrowing, a cut
    /// jawline, the chin brought forward, a narrower, thinner, set-back mouth, and ordinary brows.
    /// Women keep the ethnicity's face untouched.
    ///
    /// The mouth and chin rows overrule inheritance for these men, which costs some family
    /// resemblance there; the ranges are wide enough that brothers still differ.
    /// </summary>
    public static IReadOnlyList<RaceMorph> MaleOf(RaceArchetype archetype) => archetype switch
    {
        RaceArchetype.HighElf =>
        [
            new("gene_jaw_width", "jaw_width_neg", 0.42f, 0.48f),
            // The ethnicity's 0.15-0.35 cheeks and 0.10-0.28 brow arch, pulled down for men only.
            new("gene_bs_cheek_forward", "cheek_forward_pos", 0.08f, 0.22f),
            new("gene_bs_forehead_brow_curve", "forehead_brow_curve_pos", 0.00f, 0.15f),
            .. MaleElfFace,
            new("gene_eyebrows_shape", "avg_spacing_avg_thickness", 0.00f, 1.00f, Tiered: false),
            new("gene_eyebrows_fullness", "layer_2_avg_thickness", 0.00f, 1.00f, Tiered: false),
        ],
        RaceArchetype.WoodElf =>
        [
            new("gene_jaw_width", "jaw_width_neg", 0.38f, 0.48f),
            .. MaleElfFace,
        ],
        RaceArchetype.DuskElf =>
        [
            new("gene_jaw_width", "jaw_width_neg", 0.40f, 0.47f),
            .. MaleElfFace,
            new("gene_eyebrows_fullness", "layer_2_avg_thickness", 0.00f, 1.00f, Tiered: false),
        ],
        _ => []
    };

    /// <summary>
    /// What a race's women get on top of <see cref="Of"/>, forced at render time only, as
    /// <see cref="MaleOf"/> is for elf men.
    ///
    /// Dwarf women: the dwarf face widens jaw and chin and pushes the jaw forward for everyone, and
    /// vanilla's gene_jaw_width gives women the full male range on a narrower head ("Female uses full
    /// range", 01_genes_morph.txt), so their jaws came out the most oversized thing on the map (user,
    /// in game, 2026-09-29). Here: a moderately broad jaw rather than a maximal one, a light jaw shape,
    /// and chin and jaw brought back toward vanilla. Untiered, like the men's corrections.
    /// Giantkin women: the same jaw exposure, stacked on the GiantFace jaw; see their entry.
    /// </summary>
    public static IReadOnlyList<RaceMorph> FemaleOf(RaceArchetype archetype) => archetype switch
    {
        RaceArchetype.Dwarf =>
        [
            new("gene_jaw_width", "jaw_width_pos", 0.58f, 0.68f, Tiered: false),
            new("gene_bs_jaw_def", "jaw_def_pos", 0.10f, 0.28f, Tiered: false),
            new("gene_chin_width", "chin_width_pos", 0.55f, 0.66f, Tiered: false),
            new("gene_jaw_forward", "jaw_forward_pos", 0.50f, 0.60f, Tiered: false),
        ],
        // Giantkin women: the same full-male-range jaw_width exposure as dwarf women, and GiantFace
        // already pushes the gonial angles out and broadens the chin, while body_shape_triangle_full
        // adds up to +0.4 neck_width on top of the ethnicity's 0.85-1.0. Each of the three was widened
        // twice. Still above neutral so they stay broad; head width is left at the race's value.
        RaceArchetype.Giantkin =>
        [
            new("gene_jaw_width", "jaw_width_pos", 0.55f, 0.65f, Tiered: false),
            new("gene_chin_width", "chin_width_pos", 0.55f, 0.68f, Tiered: false),
            new("gene_neck_width", "neck_width_pos", 0.55f, 0.70f, Tiered: false),
        ],
        _ => []
    };

    /// <summary>
    /// The lower face every elf man shares. Untiered: this corrects toward vanilla's own male
    /// ranges rather than exaggerating a race, so a surreal map should not push it further.
    /// </summary>
    private static readonly RaceMorph[] MaleElfFace =
    [
        new("gene_bs_jaw_def", "jaw_def_pos", 0.12f, 0.32f, Tiered: false),
        new("gene_chin_forward", "chin_forward_pos", 0.54f, 0.62f, Tiered: false),
        new("gene_mouth_width", "mouth_width_neg", 0.40f, 0.47f, Tiered: false),
        new("gene_mouth_forward", "mouth_forward_neg", 0.42f, 0.48f, Tiered: false),
        new("gene_bs_mouth_upper_lip_full", "mouth_upper_lip_full_neg", 0.10f, 0.30f, Tiered: false),
        new("gene_bs_mouth_lower_lip_full", "mouth_lower_lip_full_neg", 0.05f, 0.25f, Tiered: false),
    ];
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
    /// see Emit/Characters/RaceMorphWriter.cs. Before that, more colouring variety per race would have been
    /// more chances to drift out of the race.
    /// </summary>
    public List<EthnicityVariant> Variants { get; } = [];
}

/// <summary>
/// One colouring of a base look. A fantasy variant is a hair palette and nothing else, emitted as
/// `template = &lt;base key&gt;` plus that block, exactly as vanilla's `caucasian_blond` is.
///
/// A human variant is one culture's dress over one vanilla ethnicity (see <see cref="HumanLooks"/>):
/// <see cref="Template"/> names that vanilla key, and it carries the culture's skin window, the
/// template's own hair and eyes, and its gene leans.
/// </summary>
public sealed class EthnicityVariant
{
    public required string Key { get; init; }
    public required string LocalizedName { get; init; }

    /// <summary>The key this variant is templated on, or null for its base.</summary>
    public string? Template { get; init; }

    /// <summary>Its weight in its culture's list, for variants built per culture.</summary>
    public int Weight { get; init; }

    public Dictionary<string, List<ColorPaletteRange>> ColorGenes { get; } = [];
    public Dictionary<string, List<GeneMorphEntry>> MorphGenes { get; } = [];
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

    /// <summary>
    /// Races the mode's land ratio could not give a realm, seated instead as ~13% minorities in
    /// the listed human host culture. They count as delivered for GuaranteedRaceCount. History
    /// writes them phenotype_human (traits are stamped per culture); the Fantasy script set swaps
    /// in their own race's trait at game start by reading their ethnicity (RaceMorphWriter's
    /// generated gen_is_&lt;race&gt;_ethnicity_trigger).
    /// </summary>
    public required List<(RaceArchetype Race, Culture Host)> MinorityPlacements { get; init; }

    /// <summary>What the human looks were placed with, so the editor can dress a culture the same
    /// way. Null on the quiet race pass.</summary>
    public HumanLooks.Context? Looks { get; init; }

    /// <summary>
    /// Every colour and gene block one member of this culture is drawn from, resolved the way the
    /// engine resolves <c>template =</c>: one weighted pick of the culture's own variants, over the
    /// vanilla ethnicity its chain bottoms out in. For the bookmark DNA writer, which must roll a
    /// whole face the way the game would have rolled it.
    ///
    /// The vanilla layer is what makes it a whole face. Our ethnicities override ~30 genes and
    /// inherit the other ~100 from vanilla, so without <paramref name="vanilla"/> the caller only
    /// learns about the overrides.
    ///
    /// A human variant is templated straight on a vanilla key (see <see cref="HumanLooks"/>), so its
    /// base's blocks are not in its chain; a fantasy variant is templated on its base and inherits
    /// them. Only the culture's own race's variants are candidates: a host culture's list can also
    /// name a minority race's variant, and its bookmark ruler is not of that race.
    /// </summary>
    public (Dictionary<string, List<ColorPaletteRange>> Colors, Dictionary<string, List<GeneMorphEntry>> Genes)
        GenesFor(Culture culture, Rng rng,
            IReadOnlyDictionary<string, HumanLooks.VanillaEthnicity>? vanilla = null)
    {
        var def = For(culture);
        var own = def.Variants.ToDictionary(v => v.Key);
        var picks = VariantsFor(culture).Where(p => own.ContainsKey(p.Key)).ToList();

        EthnicityVariant? variant = null;
        if (picks.Count > 0)
        {
            int i = rng.WeightedIndex(picks, p => p.Weight);
            variant = own[picks[i < 0 ? 0 : i].Key];
        }

        var colors = new Dictionary<string, List<ColorPaletteRange>>(StringComparer.Ordinal);
        var genes = new Dictionary<string, List<GeneMorphEntry>>(StringComparer.Ordinal);

        void Lay(Dictionary<string, List<ColorPaletteRange>> c, Dictionary<string, List<GeneMorphEntry>> g)
        {
            foreach (var (k, v) in c) colors[k] = v;
            foreach (var (k, v) in g) genes[k] = v;
        }

        bool onVanilla = variant?.Template is { } t && t != def.Key;
        string root = onVanilla ? variant!.Template! : def.BaseTemplate;
        if (vanilla is not null && vanilla.TryGetValue(root, out var chain)) Lay(chain.Colors, chain.Genes);
        if (!onVanilla) Lay(def.ColorGenes, def.MorphGenes);
        if (variant is not null) Lay(variant.ColorGenes, variant.MorphGenes);

        return (colors, genes);
    }

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
        Rng rng,
        WildernessMap? wilderness = null,
        bool quiet = false,
        HumanLooks.Inputs? humanLooks = null)
    {
        // quiet: the preliminary pass the culture stage runs to learn each people's race before
        // naming (see Cultures.SpeakAsRace). The real pass follows with the same seed and inputs and
        // arrives at the same races, so its report is the one worth printing.
        void Say(string line) { if (!quiet) Console.WriteLine(line); }

        // A culture whose every county fell to wilderness writes no province history: it exists as
        // a definition and holds nothing a player will ever see. Before this set existed such
        // ghosts were not only eligible for races, they were MAGNETS for them — GetTerrainShares
        // hands a landless culture a random terrain at 100% share, so a ghost that rolled Arctic
        // out-scored every real arctic culture for the giantkin seat, and the race vanished from
        // the visible map. Ghosts are now human, spend no ratio budget, host no minorities, and
        // are invisible to every seeding pass.
        int LandCount(Culture c) => c.Counties.Count(k => wilderness?.Contains(k) != true);
        var ghosts = cultures.Where(c => LandCount(c) == 0).ToHashSet();
        if (ghosts.Count > 0)
            Say($"  ethnicities: {ghosts.Count} culture(s) hold only wilderness — kept human and out of every race roll");

        var ethnicities = new Dictionary<string, EthnicityDef>(StringComparer.OrdinalIgnoreCase);
        var byCulture = new Dictionary<Culture, EthnicityDef>();
        var byHeritage = new Dictionary<Heritage, EthnicityDef>();
        var byCultureKey = new Dictionary<string, EthnicityDef>(StringComparer.OrdinalIgnoreCase);
        var byCultureVariants = new Dictionary<Culture, List<(string Key, int Weight)>>();
        var byHeritageKey = new Dictionary<string, EthnicityDef>(StringComparer.OrdinalIgnoreCase);

        int ethIndex = 0;

        // The mode's human:fantasy ratio, expressed as the counties fantasy races may hold.
        // Under TieRaceToHeritage the heritage phase spends it; otherwise the culture loop does,
        // and the heritage phase runs budgetless because its output is only the suggestion the
        // 70%-follow drift reads.
        int totalCounties = Math.Max(1, cultures.Sum(c => c.Counties.Count));
        double fantasyBudget = (1.0 - HumanShareOf(cfg)) * totalCounties;
        double fantasySpent = 0;
        var minorityQueue = new List<RaceArchetype>();

        // A custom race mix splits that budget per race (see MixBudget). Null without one, which
        // leaves the single budget in charge and every roll below exactly as it was. Its shares are
        // of SETTLED land: the mode's budget counts wilderness counties too while spending only
        // settled ones, which on a wild map leaves fantasy races well over the mode's figure.
        int totalLand = Math.Max(1, cultures.Sum(LandCount));
        var mix = MixBudget.For(cfg, (1.0 - HumanShareOf(cfg)) * totalLand);

        // 1. Determine Archetypes with Guaranteed Variety Guarantee
        var heritageArchetypes = AssignDiverseArchetypes(heritages, cultures, provinceTerrain, cfg, rng,
            wilderness,
            cfg.TieRaceToHeritage ? fantasyBudget : null,
            cfg.TieRaceToHeritage && cfg.AllowMinorityRaces,
            cfg.TieRaceToHeritage ? mix : null,
            out var heritageOverflow);
        minorityQueue.AddRange(heritageOverflow);

        // 2. Generate Heritage Ethnicities
        foreach (var heritage in heritages)
        {
            var archetype = heritageArchetypes.GetValueOrDefault(heritage, RaceArchetype.Human);
            var heritageEth = CreateEthnicity($"gen_ethnicity_{ethIndex++}", archetype, heritage.Name, cfg.RaceMode, cfg.DominantLook, rng);

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

        // 4. Assign Culture Ethnicities.
        //
        // Iterated in a shuffled order: the land budget below runs out partway through, and in
        // list order that would make everything after the cut-off human — one contiguous end of
        // the map, since the culture list is built region by region. Shuffling turns the cut-off
        // into scatter. Ethnicity keys come out in a different order than list order as a result,
        // which nothing depends on.
        var cultureOrder = new List<Culture>(cultures);
        rng.Shuffle(cultureOrder);

        foreach (var culture in cultureOrder)
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
                cultureEth = CreateEthnicity($"gen_ethnicity_{ethIndex++}", race, culture.Name, cfg.RaceMode, cfg.DominantLook, rng);
                ethnicities[cultureEth.Key] = cultureEth;
                usedArchetypes.Add(race);
                if (race != RaceArchetype.Human)
                {
                    fantasySpent += LandCount(culture);
                    mix?.Spend(race, LandCount(culture));
                }
            }
            else if (cfg.TieRaceToHeritage && heritageEth != null)
            {
                cultureEth = heritageEth;
            }
            else
            {
                RaceArchetype subArchetype;

                // A ghost takes Human unconditionally: it holds no visible land, so a race seated
                // here vanishes from the map — the observed bug was the gnomes AND the giants both
                // living on cultures with zero province history while the preview and the game
                // showed neither. Checked before the quota so a ghost can never satisfy it.
                if (ghosts.Contains(culture))
                {
                    subArchetype = RaceArchetype.Human;
                }
                // If we still haven't met the quota (e.g. very few heritages), guarantee it at culture level
                else if (usedArchetypes.Count < targetQuota && cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly)
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
                        var shares = GetTerrainShares([culture], provinceTerrain);
                        available = available.Where(a => FitsTerrain(a, shares)).ToList();
                    }

                    if (mix is not null)
                    {
                        // The mix's quota edition. The culture goes to an unplaced race whose share
                        // still has room for it, the more it is owed the likelier. With none, the
                        // first fantasy culture is seated anyway (as below), then humans take it
                        // if they are still unplaced themselves, and failing that an unplaced race
                        // is demoted to a minority, or wins the land when minorities are off.
                        int land = LandCount(culture);
                        var shares = GetTerrainShares([culture], provinceTerrain);
                        var fantasy = available.Where(a => a != RaceArchetype.Human).ToList();

                        if (mix.Pick(fantasy, land, r => TerrainPull(cfg, r, shares), rng) is { } owed)
                            subArchetype = owed;
                        else if (fantasySpent == 0 && fantasy.Count > 0)
                            subArchetype = rng.Pick(fantasy);
                        else if (available.Contains(RaceArchetype.Human))
                            subArchetype = RaceArchetype.Human;
                        else if (fantasy.Count > 0 && cfg.AllowMinorityRaces)
                        {
                            var demoted = rng.Pick(fantasy);
                            minorityQueue.Add(demoted);
                            usedArchetypes.Add(demoted); // spoken for, just not with land
                            subArchetype = RaceArchetype.Human;
                        }
                        else
                            subArchetype = fantasy.Count > 0 ? rng.Pick(fantasy) : PickArchetypeForCulture(culture, provinceTerrain, cfg, rng);
                    }
                    else
                    {
                        // The ratio gate, quota edition. A race still owed when the land budget is
                        // gone arrives as a minority inside a human culture rather than as a realm —
                        // the guarantee and the ratio both hold, which neither could alone. With
                        // minorities disallowed the guarantee simply wins the land, as its name says
                        // it should, and the ratio shortfall is reported at the end. The first
                        // fantasy culture is always seated for the same reason the heritage phase
                        // seats its first seed: a fantasy mode should never produce zero fantasy.
                        bool overBudget = fantasySpent > 0
                            && fantasySpent + LandCount(culture) > fantasyBudget;

                        if (available.Count > 0 && overBudget && cfg.AllowMinorityRaces)
                        {
                            var demoted = rng.Pick(available);
                            minorityQueue.Add(demoted);
                            usedArchetypes.Add(demoted); // spoken for, just not with land
                            subArchetype = RaceArchetype.Human;
                        }
                        else
                        {
                            subArchetype = available.Count > 0 ? rng.Pick(available) : PickArchetypeForCulture(culture, provinceTerrain, cfg, rng);
                        }
                    }
                }
                else if (mix is not null)
                {
                    // The mix's drift edition. Most cultures still follow their heritage's race,
                    // while that race's share has room; the rest, and the ones it has no room for,
                    // go to whichever race is most owed land that suits this culture's own ground,
                    // and to humans once no race is owed any.
                    int land = LandCount(culture);
                    var baseArchetype = heritageEth?.Archetype ?? RaceArchetype.Human;
                    if (rng.Chance(0.70) && mix.Fits(baseArchetype, land))
                    {
                        subArchetype = baseArchetype;
                    }
                    else
                    {
                        var shares = GetTerrainShares([culture], provinceTerrain);
                        var suited = quotaPool
                            .Where(r => cfg.RaceTerrain != RaceTerrainRule.Require || FitsTerrain(r, shares))
                            .ToList();
                        subArchetype = mix.Pick(suited, land, r => TerrainPull(cfg, r, shares), rng) ?? RaceArchetype.Human;
                    }
                }
                else
                {
                    var baseArchetype = heritageEth?.Archetype ?? RaceArchetype.Human;
                    subArchetype = rng.Chance(0.70) ? baseArchetype : PickArchetypeForCulture(culture, provinceTerrain, cfg, rng);

                    // The ratio gate, drift edition. Following the heritage or the terrain roll
                    // is flavour, not a guarantee, so over budget it simply yields to human.
                    if (subArchetype != RaceArchetype.Human
                        && fantasySpent + LandCount(culture) > fantasyBudget)
                        subArchetype = RaceArchetype.Human;
                }

                if (subArchetype != RaceArchetype.Human)
                {
                    fantasySpent += LandCount(culture);
                    mix?.Spend(subArchetype, LandCount(culture));
                }

                cultureEth = CreateEthnicity($"gen_ethnicity_{ethIndex++}", subArchetype, culture.Name, cfg.RaceMode, cfg.DominantLook, rng);
                ethnicities[cultureEth.Key] = cultureEth;
                usedArchetypes.Add(subArchetype);
            }

            byCulture[culture] = cultureEth;
            byCultureKey[culture.Key] = cultureEth;
            byCultureVariants[culture] = PickCultureVariants(cultureEth, rng);
        }

        // 4b. The humans' faces. Everything above settles who is human; this decides what they look
        // like, placing each human heritage on a vanilla look that suits its climate with the four
        // families kept in an even split, and dressing each culture over vanilla's own ethnicities.
        // A pass of its own, on its own random streams, so the race rolls above and every fantasy
        // people's draws are exactly what they were. See HumanLooks.
        HumanLooks.Context? looksContext = null;
        if (humanLooks is not null)
        {
            looksContext = new HumanLooks.Context
            {
                Data = humanLooks.Data,
                ProvinceClimate = humanLooks.ProvinceClimate,
                Seed = cfg.Seed
            };

            var humanCultures = cultures.Where(c => byCulture[c].Archetype == RaceArchetype.Human).ToList();
            if (humanCultures.Count > 0)
            {
                var placed = HumanLooks.Place(heritages, humanCultures, ghosts, LandCount, cfg.DominantLook,
                    LookTemplates(cfg.DominantLook), humanLooks, cfg.Seed, Say);

                // The definitions the loop made for humans are replaced wholesale; any a heritage
                // with no human culture still points at is put back below.
                foreach (var old in ethnicities.Values.Where(e => e.Archetype == RaceArchetype.Human).ToList())
                    ethnicities.Remove(old.Key);

                // One base per heritage and template: a culture refitted onto a sibling template
                // gets its own, so the base always names the look its cultures actually wear.
                var bases = new Dictionary<(Heritage, string), EthnicityDef>();
                var leans = new Dictionary<Heritage, List<HumanLooks.Lean>>();

                foreach (var culture in humanCultures)
                {
                    var heritage = culture.Heritage;
                    string template = placed.TryGetValue(culture, out var t) ? t : byCulture[culture].BaseTemplate;

                    if (!bases.TryGetValue((heritage, template), out var baseDef))
                    {
                        baseDef = new EthnicityDef
                        {
                            Key = $"gen_ethnicity_{ethIndex++}",
                            LocalizedName = heritage.Name,
                            Archetype = RaceArchetype.Human,
                            LookFamily = HumanLooks.FamilyOf(template),
                            BaseTemplate = template
                        };
                        bases[(heritage, template)] = baseDef;
                        ethnicities[baseDef.Key] = baseDef;
                    }

                    if (!leans.TryGetValue(heritage, out var heritageLeans))
                        leans[heritage] = heritageLeans = HumanLooks.HeritageLeans(cfg.Seed, heritage);

                    var variants = HumanLooks.Dress(culture, template,
                        $"{baseDef.Key}_{culture.Key.Replace("gen_culture_", "c")}", humanLooks.Data,
                        ClothingClimate.Of(culture.Counties, humanLooks.ProvinceClimate), heritageLeans, cfg.Seed);

                    baseDef.Variants.AddRange(variants);
                    byCulture[culture] = baseDef;
                    byCultureKey[culture.Key] = baseDef;
                    byCultureVariants[culture] = variants.Select(v => (v.Key, v.Weight)).ToList();
                }

                foreach (var heritage in heritages)
                {
                    if (!byHeritage.TryGetValue(heritage, out var heritageEth)
                        || heritageEth.Archetype != RaceArchetype.Human) continue;

                    var first = bases.Where(kv => kv.Key.Item1 == heritage).Select(kv => kv.Value).FirstOrDefault();
                    if (first is not null)
                    {
                        byHeritage[heritage] = first;
                        byHeritageKey[heritage.Key] = first;
                    }
                    else
                    {
                        ethnicities[heritageEth.Key] = heritageEth;
                    }
                }
            }
        }

        // 5. Seat the minorities. Each race the land budget could not give a realm gets a small
        // presence (~13% of generated characters, weight 15 against a 70/30 variant list) inside
        // the human culture whose terrain suits it best — the dwarves live among the humans of
        // the mountains, not in a randomly chosen fishing village. The ethnicity is real and
        // emitted like any other; only the culture's ethnicities list carries the smallness.
        //
        // Minority members get their race's phenotype trait at RUNTIME, not from HistoryWriter.
        // HistoryWriter stamps traits per culture and the host culture is human, so at emit time
        // every minority member is written as phenotype_human — and the generator cannot do better,
        // because it does not know who the gnomes are. History characters carry no `dna` (only the
        // bookmark cast does), so the engine rolls each one's ethnicity out of this weighted list
        // when the save is created, long after these files are written.
        //
        // The correction lives in BaseFilesToCopy/Fantasy: gen_race_skin has a uniquely named
        // template per race, so gen_reconcile_phenotype_with_genes_effect reads the genome at game
        // start and swaps phenotype_human for the race the body actually names.
        //
        // Their LOOK never needed the trait — ApplyMorphGenes bakes RaceMorphs.Of(archetype) into
        // the ethnicity, so a minority gnome is short and splay-eared from its genes alone. What
        // the trait carries is everything else: the chip in the character view, the same/opposite
        // opinion web, InteractionWriter's marriage reluctance, and — the one that compounds —
        // gen_resolve_child_phenotype_effect, which reads TRAITS at birth. An untraited minority
        // was invisible to it, so a gnome's children were resolved as two plain humans and the line
        // dissolved into whatever the inherited genes happened to drift to within a generation.
        var minorityPlaced = new List<(RaceArchetype Race, Culture Host)>();
        var hostsUsed = new HashSet<Culture>();
        foreach (var minorityRace in minorityQueue.Distinct().ToList())
        {
            Culture? bestHost = null;
            double bestScore = double.MinValue;
            foreach (var candidate in cultures)
            {
                if (byCulture[candidate].Archetype != RaceArchetype.Human) continue;
                if (ghosts.Contains(candidate)) continue; // a minority nobody can meet is no minority

                // Each race gets its own host while hosts last. Without this every minority piles
                // into whichever single culture scores best map-wide — the observed case was the
                // dwarves AND the gnomes both inside one people while every other human culture
                // carried nobody — and one culture with three minorities reads as a bug where
                // three cultures with one each read as a world.
                if (hostsUsed.Contains(candidate) && hostsUsed.Count < cultures.Count(c => byCulture[c].Archetype == RaceArchetype.Human)) continue;

                double score = HeritageAffinity(minorityRace, GetTerrainShares([candidate], provinceTerrain))
                    + rng.Double(0.0, 0.3);
                if (score > bestScore) { bestScore = score; bestHost = candidate; }
            }

            // No human culture to host them — a fully fantastical map. Nothing to do; the race
            // was only demoted because the world was too fantasy-heavy already, so this cannot
            // happen with a sane budget, but a zero-human world should not crash over it.
            if (bestHost is null) break;

            var minorityEth = CreateEthnicity($"gen_ethnicity_{ethIndex++}", minorityRace,
                bestHost.Name, cfg.RaceMode, cfg.DominantLook, rng);
            ethnicities[minorityEth.Key] = minorityEth;

            string entry = minorityEth.Variants.Count > 0
                ? rng.Pick(minorityEth.Variants).Key
                : minorityEth.Key;
            byCultureVariants[bestHost].Add((entry, 15));
            minorityPlaced.Add((minorityRace, bestHost));
            hostsUsed.Add(bestHost);
        }

        var tallies = byCulture.Values
            .GroupBy(e => e.Archetype)
            .Select(g => $"{g.Count()} {g.Key}");

        // Counted from byCulture, not from usedArchetypes: the tally beside it comes from the
        // cultures, and the two used to be able to disagree. Minorities count as delivered — a
        // race living among the humans is on the map, which is what the guarantee promises.
        int deliveredRaces = byCulture.Values.Select(e => e.Archetype)
            .Concat(minorityPlaced.Select(m => m.Race))
            .Distinct().Count();

        if (minorityPlaced.Count > 0)
            Say("  ethnicities: minorities — " + string.Join(", ",
                minorityPlaced.Select(m => $"{m.Race} among the {m.Host.Name}")));

        if (cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly)
        {
            if (mix is null)
            {
                int humanCounties = byCulture
                    .Where(kv => kv.Value.Archetype == RaceArchetype.Human)
                    .Sum(kv => kv.Key.Counties.Count);
                Say($"  ethnicities: humans hold {(double)humanCounties / totalCounties:P0} " +
                                  $"of counties (mode target ~{HumanShareFor(cfg.RaceMode):P0})");
            }
            else
            {
                // Each race's settled land against its share, so a mix that came out lumpy says so
                // rather than looking ignored. A race with no land but a minority seat says that too.
                var held = byCulture
                    .GroupBy(kv => kv.Value.Archetype)
                    .ToDictionary(g => g.Key, g => g.Sum(kv => LandCount(kv.Key)));
                var minorities = minorityPlaced.Select(m => m.Race).ToHashSet();
                Say($"  ethnicities: humans hold {(double)held.GetValueOrDefault(RaceArchetype.Human) / totalLand:P0} " +
                    $"of settled counties (custom target ~{HumanShareOf(cfg):P0})");
                Say("  ethnicities: custom mix — " + string.Join(", ", mix.Races.Select(r =>
                    $"{RaceName(r)} {(double)held.GetValueOrDefault(r) / totalLand:P0} of {mix.Budget(r) / totalLand:P0}"
                    + (minorities.Contains(r) ? " + minority" : ""))));
            }
        }

        Say($"  ethnicities: {byCulture.Count} cultures across {deliveredRaces} distinct races -> {string.Join(", ", tallies)}");

        // Delivering fewer races than asked for used to be silent, which made a clipped quota
        // look like bad luck in the seed. Say which constraint actually bound. Capped at the pool,
        // as the quota itself is: asking for more races than exist is not a shortfall, and a mix
        // asks only for the races it kept — the ones it switched off were never wanted.
        int wanted = Math.Clamp(cfg.GuaranteedRaceCount, 1, Math.Max(1, quotaPool.Count));
        if (cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly
            && deliveredRaces < wanted)
        {
            // Ordered by which constraint actually binds first.
            string reason = !cfg.AllowMinorityRaces
                ? "the mode's human:fantasy ratio left no land for them and AllowMinorityRaces is off — turn it on to seat them as minorities, or accept fewer races"
                : cfg.RaceTerrain == RaceTerrainRule.Require
                ? "RaceTerrain is Require, so races with no suitable terrain anywhere on this map were left unplaced rather than misplaced — set it to Prefer to settle them anyway"
                : !cfg.TieRaceToHeritage && byCulture.Count < wanted
                ? $"only {byCulture.Count} culture(s) exist — lower CountiesPerCulture to make more"
                : cfg.TieRaceToHeritage && heritages.Count < wanted
                    ? $"only {heritages.Count} heritage(s) exist — lower CulturesPerHeritage or CountiesPerCulture to make more, or untick TieRaceToHeritage to place races per culture instead"
                    : "the terrain roll did not spread them this seed — try another";
            Say($"  WARNING: asked for {wanted} distinct races but delivered {deliveredRaces}: {reason}");
        }

        return new EthnicityMap
        {
            Ethnicities = ethnicities,
            ByCulture = byCulture,
            ByHeritage = byHeritage,
            ByCultureKey = byCultureKey,
            ByHeritageKey = byHeritageKey,
            VariantsByCulture = byCultureVariants,
            MinorityPlacements = minorityPlaced,
            Looks = looksContext
        };
    }

    /// <param name="fantasyBudget">Counties fantasy races may hold in total, or null when the
    /// caller enforces the ratio itself (the culture-level loop under TieRaceToHeritage = false,
    /// where these assignments are only suggestions that the 70%-follow drift reads).</param>
    /// <param name="allowOverflow">Whether a guaranteed race the budget cannot seat becomes a
    /// minority (returned in <paramref name="overflow"/>) instead of taking land anyway. False
    /// means the guarantee wins the land and the ratio is knowingly sacrificed.</param>
    /// <param name="mix">The custom race mix's per-race split of <paramref name="fantasyBudget"/>,
    /// which then gates each race on its own share instead of all of them on the total. Null
    /// without a mix, and whenever <paramref name="fantasyBudget"/> is.</param>
    private static Dictionary<Heritage, RaceArchetype> AssignDiverseArchetypes(
        List<Heritage> heritages,
        List<Culture> cultures,
        TerrainClass[] provinceTerrain,
        MapConfig cfg,
        Rng rng,
        WildernessMap? wilderness,
        double? fantasyBudget,
        bool allowOverflow,
        MixBudget? mix,
        out List<RaceArchetype> overflow)
    {
        overflow = [];
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
            heritageTerrain[heritage] = GetTerrainShares(heritageCultures, provinceTerrain);
        }

        // Land bookkeeping for the mode ratio. Imported assignments spend budget too — the
        // export's races are as much land as generated ones — but they are never demoted to
        // minorities, because the export said they hold ground and that word stands.
        var heritageCounties = new Dictionary<Heritage, int>();
        foreach (var heritage in heritages)
            heritageCounties[heritage] = cultures
                .Where(c => c.Heritage == heritage || c.Heritage?.Key == heritage.Key)
                .Sum(c => c.Counties.Count(k => wilderness?.Contains(k) != true));
        double meanCounties = Math.Max(1.0, heritageCounties.Values.DefaultIfEmpty(1).Average());
        double fantasySpent = 0;

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
                    var cultureShares = GetTerrainShares([culture], provinceTerrain);
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

        // A heritage with no real land cannot seat a race a player will ever meet — and worse,
        // its zero county count made it look FREE to the budget, so the greedy treated ghost
        // heritages as the cheapest seats on the map. Human, out of the running, before anything
        // is paired.
        foreach (var h in remainingHeritages.Where(h => heritageCounties[h] == 0).ToList())
        {
            assignments[h] = RaceArchetype.Human;
            remainingHeritages.Remove(h);
        }

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
            if (race != RaceArchetype.Human)
            {
                fantasySpent += heritageCounties[h];
                mix?.Spend(race, heritageCounties[h]);
            }
            importedCount++;
        }

        if (importedCount > 0)
            Console.WriteLine($"  ethnicities: {importedCount} of {heritages.Count} heritages took " +
                              $"their race from the export's tags");

        // A custom mix seats races by how much land each is owed rather than by the phases below;
        // see SeatByMix. The export's tags above stand either way.
        if (mix is not null)
        {
            SeatByMix(mix, remainingHeritages, heritageCounties, heritageTerrain, heritageFits, assignments,
                cfg, rng, allowOverflow, overflow);
            return assignments;
        }

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

                    // A guaranteed race should cost as little of the human land budget as its
                    // affinities allow, so bigger-than-average heritages are penalised — hard on
                    // a low-fantasy map, barely at all on a surreal one. Human seeds are free
                    // land and take no penalty.
                    double sizePenalty = fantasyBudget is null || race == RaceArchetype.Human
                        ? 0.0
                        : SizePressureFor(cfg.RaceMode) * (heritageCounties[h] / meanCounties);

                    double score = affinity - sizePenalty + rng.Double(0.0, 0.3);
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

            // The ratio gate. A fantasy seed that would blow the land budget becomes a minority
            // instead of a realm — except the FIRST one, which is always seated: a fantasy mode
            // whose budget is smaller than the smallest heritage should still put one fantasy
            // realm on the map rather than none, because "rare realms" is the mode's own promise.
            if (bestRace != RaceArchetype.Human && fantasyBudget is { } budget)
            {
                bool firstSeed = fantasySpent == 0;
                if (!firstSeed && fantasySpent + heritageCounties[bestHeritage] > budget)
                {
                    if (allowOverflow)
                    {
                        overflow.Add(bestRace);
                        assignedRaces.Add(bestRace); // spoken for, just not with land
                        continue;
                    }
                    // Guarantee wins the land; the caller reports the broken ratio.
                }
                fantasySpent += heritageCounties[bestHeritage];
            }

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
            // The mode ratio, enforced in land rather than by the old per-heritage coin flip: a
            // remainder heritage goes fantasy while the budget holds and human once it is spent.
            // With no budget (the tie=false suggestion pass) every remainder rolls fantasy and
            // the culture loop applies the ratio itself.
            if (fantasyBudget is { } b && fantasySpent + heritageCounties[h] > b)
            {
                assignments[h] = RaceArchetype.Human;
                continue;
            }

            var pick = PickWeightedArchetype(heritageTerrain[h], heritageFits?[h], assignedRaces, cfg, rng);
            assignments[h] = pick;
            assignedRaces.Add(pick);
            if (pick != RaceArchetype.Human) fantasySpent += heritageCounties[h];
        }

        // 3. Top-up Phase: races the heritage COUNT could not seat. targetUnique clamps to the
        //    number of heritages, so a seven-heritage world asked for eight races never even
        //    attempts the eighth — it was not squeezed out by the land budget, it was clamped away
        //    before the greedy began, and no overflow entry was ever written for it. With
        //    minorities allowed the guarantee is not a land promise, so those races are owed a
        //    minority seat. Run AFTER the remainder on purpose: the remainder prefers unseated
        //    races for its realms, and a race that can still get land should, with the minority
        //    list as the fallback rather than the first resort.
        if (allowOverflow)
        {
            int wanted = Math.Clamp(cfg.GuaranteedRaceCount, 1, candidatePool.Count);

            // Distinct union, not a sum. The greedy adds a budget-demoted race to BOTH
            // assignedRaces (so nothing re-attempts it) and overflow (so it gets its minority
            // seat), and summing the two lists counted every demoted race twice — which is how a
            // seven-heritage world asked for eight races delivered seven: the sum hit `wanted`
            // while a pool race the clamp had never let the greedy attempt was still missing.
            var delivered = new HashSet<RaceArchetype>(assignedRaces);
            delivered.UnionWith(overflow);

            foreach (var race in candidatePool)
            {
                if (delivered.Count >= wanted) break;
                if (race == RaceArchetype.Human) continue; // humans need no minority seat
                if (!delivered.Add(race)) continue;
                overflow.Add(race);
            }
        }

        return assignments;
    }

    /// <summary>
    /// A custom mix's heritage seating, in place of the diversity greedy and the terrain remainder.
    ///
    /// Round after round, the race owed the most land takes the heritage that suits it best among
    /// those its share has room for (<see cref="MixBudget.Fits"/>). So a heavy weight gets its
    /// second heritage before a light one gets its first, and the ground still decides where each
    /// one goes. Equal claims, as every race has at the start of an even mix, go to whichever has
    /// the best-suited heritage on offer, which is how the greedy this replaces chose. Heritages no
    /// race is owed are human.
    ///
    /// A race that never fits anywhere gets a minority seat, or with minorities off takes its best
    /// heritage anyway, as the guarantee does elsewhere. If no race fits at all, the most owed is
    /// seated regardless: a world with races on never comes out with none, the modes' own promise.
    /// </summary>
    private static void SeatByMix(
        MixBudget mix,
        List<Heritage> remaining,
        Dictionary<Heritage, int> counties,
        Dictionary<Heritage, Dictionary<TerrainClass, double>> terrain,
        Dictionary<Heritage, HashSet<RaceArchetype>>? requireFits,
        Dictionary<Heritage, RaceArchetype> assignments,
        MapConfig cfg,
        Rng rng,
        bool allowOverflow,
        List<RaceArchetype> overflow)
    {
        // The heritage that suits a race best of those `admit` lets through, with the greedy's
        // jitter to break ties inside an affinity step. Require's verdict is never overridden.
        (Heritage? Heritage, double Score) BestFor(RaceArchetype race, Func<Heritage, bool> admit)
        {
            Heritage? best = null;
            double bestScore = double.MinValue;
            foreach (var h in remaining)
            {
                if (requireFits is not null && !requireFits[h].Contains(race)) continue;
                if (!admit(h)) continue;
                double score = TerrainPull(cfg, race, terrain[h]) + rng.Double(0.0, 0.3);
                if (score > bestScore) { bestScore = score; best = h; }
            }
            return (best, bestScore);
        }

        void Seat(RaceArchetype race, Heritage h)
        {
            assignments[h] = race;
            mix.Spend(race, counties[h]);
            remaining.Remove(h);
        }

        while (remaining.Count > 0)
        {
            RaceArchetype? pick = null;
            Heritage? pickHeritage = null;
            double pickOwed = -1.0, pickScore = double.MinValue;

            foreach (var race in mix.Races)
            {
                double owed = mix.Owed(race);
                if (owed <= 0) continue;

                var (h, score) = BestFor(race, x => mix.Fits(race, counties[x]));
                if (h is null) continue;

                bool claims = owed > pickOwed + 1e-9 || (Math.Abs(owed - pickOwed) <= 1e-9 && score > pickScore);
                if (claims) (pick, pickHeritage, pickOwed, pickScore) = (race, h, owed, score);
            }

            if (pick is not { } chosen || pickHeritage is null) break;
            Seat(chosen, pickHeritage);
        }

        var landed = assignments.Values.ToHashSet();

        if (!landed.Any(r => r != RaceArchetype.Human))
        {
            foreach (var race in mix.Races.OrderByDescending(mix.Owed))
            {
                if (BestFor(race, _ => true).Heritage is not { } h) continue;
                Seat(race, h);
                landed.Add(race);
                break;
            }
        }

        foreach (var h in remaining) assignments[h] = RaceArchetype.Human;
        if (remaining.Count > 0) landed.Add(RaceArchetype.Human);

        // The races with weight but no land, up to the guarantee (which by default is all of them).
        int wanted = Math.Clamp(cfg.GuaranteedRaceCount, 1, mix.Races.Count + 1);
        var delivered = new HashSet<RaceArchetype>(landed);
        foreach (var race in mix.Races)
        {
            if (delivered.Count >= wanted) break;
            if (delivered.Contains(race)) continue;

            if (allowOverflow)
            {
                overflow.Add(race);
                delivered.Add(race);
                continue;
            }

            // Minorities off: the guarantee wins land from the humans, and the caller reports the ratio.
            var humanHeld = assignments.Where(kv => kv.Value == RaceArchetype.Human).Select(kv => kv.Key).ToList();
            remaining.Clear();
            remaining.AddRange(humanHeld);
            if (BestFor(race, _ => true).Heritage is not { } taken) continue;
            Seat(race, taken);
            delivered.Add(race);
        }
    }

    /// <summary>Terrain the heritage has most of — the old modal reading, kept for the remainder roll.</summary>
    private static TerrainClass DominantOf(Dictionary<TerrainClass, double> shares) =>
        shares.OrderByDescending(kv => kv.Value).First().Key;

    /// <summary>
    /// The share of the world's COUNTIES humans should hold, per mode — the ratio each mode's own
    /// documentation promises ("Humans dominant (~85%)" for LowFantasy) and the number the whole
    /// budget system below steers toward.
    ///
    /// Counties, not cultures or heritages, because land share is what the ratio reads as on the
    /// map: one sprawling elven heritage outweighs five human duchies. The old implementation had
    /// no budget at all — the mode ratio lived in a single per-heritage coin flip that only ran
    /// AFTER the guarantee phase, and the guarantee phase usually consumed every heritage (the
    /// heritage floor in Cultures.cs raises the heritage count to GuaranteedRaceCount, so the two
    /// were equal on all but huge maps). Measured result: LowFantasy delivered ~10% human. The
    /// mode was decorative.
    /// </summary>
    internal static double HumanShareFor(FantasyRaceMode mode) => mode switch
    {
        FantasyRaceMode.LowFantasy => 0.85,
        FantasyRaceMode.HighFantasy => 0.35,
        FantasyRaceMode.ExoticSurreal => 0.12,
        _ => 1.0
    };

    /// <summary>
    /// The human share this world is actually built to: the custom mix's when it sets one, the
    /// mode's otherwise. See <see cref="MapConfig.RaceMixHumanShare"/> for the bounds.
    /// </summary>
    private static double HumanShareOf(MapConfig cfg)
        => HasCustomMix(cfg) && cfg.RaceMixHumanShare > 0
            ? Math.Clamp(cfg.RaceMixHumanShare, MapConfig.MinRaceMixHumanShare, MapConfig.MaxRaceMixHumanShare) / 100.0
            : HumanShareFor(cfg.RaceMode);

    /// <summary>Whether a custom race mix is in force: switched on, on a world that has races at all.</summary>
    private static bool HasCustomMix(MapConfig cfg)
        => cfg.CustomRaceMix && cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly;

    /// <summary>A fantasy race's weight in the custom mix, 0 for humans (their share is a percentage, not a weight).</summary>
    internal static int MixWeight(MapConfig cfg, RaceArchetype race) => Math.Max(0, race switch
    {
        RaceArchetype.Dwarf => cfg.RaceMixDwarves,
        RaceArchetype.HighElf => cfg.RaceMixHighElves,
        RaceArchetype.WoodElf => cfg.RaceMixWoodElves,
        RaceArchetype.Orc => cfg.RaceMixOrcs,
        RaceArchetype.Gnome => cfg.RaceMixGnomes,
        RaceArchetype.Giantkin => cfg.RaceMixGiantkin,
        RaceArchetype.DuskElf => cfg.RaceMixDuskElves,
        RaceArchetype.Hornkin => cfg.RaceMixHornkin,
        _ => 0
    });

    /// <summary>
    /// How strongly a mix pick leans toward ground that suits the race: its affinity for the
    /// region, or nothing at all under <see cref="RaceTerrainRule.Ignore"/>.
    /// </summary>
    private static double TerrainPull(MapConfig cfg, RaceArchetype race, Dictionary<TerrainClass, double> shares)
        => cfg.RaceTerrain == RaceTerrainRule.Ignore ? 1.0 : HeritageAffinity(race, shares);

    /// <summary>
    /// A custom race mix (<see cref="MapConfig.CustomRaceMix"/>) as land: the fantasy budget split
    /// between the races by their weights, and how much each has been given so far.
    ///
    /// Races settle whole heritages (or cultures under TieRaceToHeritage = false), so no share can
    /// be hit exactly. Every gate asks the same question instead, <see cref="Fits"/>: does giving
    /// the race this land leave it nearer its share than not giving it? A share smaller than half
    /// the smallest region a race could take therefore never wins land, and the race is seated as a
    /// minority instead, which is what the mix's description promises for small shares.
    ///
    /// The same question is asked of all the races together, against the whole fantasy budget.
    /// Without it each race could run over by up to half a region, and on a map of nine heritages
    /// six of those overruns took the land meant for humans: 5% human against a 35% target.
    /// </summary>
    private sealed class MixBudget
    {
        private readonly Dictionary<RaceArchetype, double> _budget = [];
        private readonly Dictionary<RaceArchetype, double> _spent = [];
        private readonly double _total;
        private double _totalSpent;

        /// <summary>The fantasy races the mix keeps, in pool order.</summary>
        public IReadOnlyList<RaceArchetype> Races { get; }

        private MixBudget(IReadOnlyList<RaceArchetype> races, MapConfig cfg, double fantasyBudget)
        {
            Races = races;
            _total = fantasyBudget;
            double total = races.Sum(r => (double)MixWeight(cfg, r));
            foreach (var r in races)
                _budget[r] = total > 0 ? fantasyBudget * MixWeight(cfg, r) / total : 0.0;
        }

        /// <summary>The mix for this config, or null when there is none.</summary>
        public static MixBudget? For(MapConfig cfg, double fantasyBudget)
            => HasCustomMix(cfg)
                ? new MixBudget([.. FantasyPoolFor(cfg).Where(r => r != RaceArchetype.Human)], cfg, fantasyBudget)
                : null;

        /// <summary>The counties the race's share comes to.</summary>
        public double Budget(RaceArchetype race) => _budget.GetValueOrDefault(race);

        /// <summary>Land the race's share still has room for; never negative.</summary>
        public double Owed(RaceArchetype race) => Math.Max(0.0, Budget(race) - _spent.GetValueOrDefault(race));

        /// <summary>
        /// Whether <paramref name="land"/> more counties leave the race nearer its share than it is
        /// now (short by less, or over by less than it was short), and the races together nearer
        /// theirs. Humans always fit; theirs is the land no race is owed.
        /// </summary>
        public bool Fits(RaceArchetype race, int land)
            => race == RaceArchetype.Human
               || (land < 2.0 * Owed(race) && land < 2.0 * Math.Max(0.0, _total - _totalSpent));

        public void Spend(RaceArchetype race, int land)
        {
            if (race == RaceArchetype.Human) return;
            _spent[race] = _spent.GetValueOrDefault(race) + land;
            _totalSpent += land;
        }

        /// <summary>
        /// A fantasy race from <paramref name="candidates"/> that <see cref="Fits"/> the land,
        /// drawn by how much land it is still owed times <paramref name="pull"/>, how well the
        /// ground suits it. Null when none fits, which every caller reads as "this land is humans'".
        /// </summary>
        public RaceArchetype? Pick(IReadOnlyList<RaceArchetype> candidates, int land,
            Func<RaceArchetype, double> pull, Rng rng)
        {
            var fitting = candidates.Where(r => r != RaceArchetype.Human && Fits(r, land)).ToList();
            if (fitting.Count == 0) return null;

            var weights = fitting.Select(r => Owed(r) * pull(r)).ToList();
            double roll = rng.Double(0.0, weights.Sum());
            for (int i = 0; i < fitting.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0.0) return fitting[i];
            }
            return fitting[^1];
        }
    }

    /// <summary>
    /// How hard the guarantee phase is pushed toward SMALL heritages, per mode. A guaranteed race
    /// has to exist somewhere; on a low-fantasy map it should exist somewhere small, so the
    /// guarantee costs as little of the human land budget as it can. Scaled by the heritage's
    /// county count relative to the mean and subtracted from the greedy's affinity score.
    /// </summary>
    private static double SizePressureFor(FantasyRaceMode mode) => mode switch
    {
        FantasyRaceMode.LowFantasy => 4.0,
        FantasyRaceMode.HighFantasy => 1.5,
        _ => 0.5
    };

    /// <summary>
    /// The races a map may draw on, given its mode. One definition so the culture-level quota, the
    /// heritage diversity phase and the shortfall report cannot disagree about what was achievable
    /// — they previously each built their own list and could disagree about what was reachable.
    /// </summary>
    private static IReadOnlyList<RaceArchetype> FantasyPoolFor(MapConfig cfg)
    {
        // ExoticSurreal is an INTENSITY setting, not an extra race. It pushes every race's colour
        // and morphology further from human; it does not add a people of its own. The roster is the
        // same nine in every mode.
        IReadOnlyList<RaceArchetype> all =
        [
            RaceArchetype.Human, RaceArchetype.Dwarf, RaceArchetype.WoodElf, RaceArchetype.HighElf,
            RaceArchetype.Orc, RaceArchetype.Gnome, RaceArchetype.Giantkin, RaceArchetype.DuskElf,
            RaceArchetype.Hornkin
        ];

        // A custom mix leaves out every race it gave no weight, and with it that race's quota
        // seat, minority seat and every terrain roll. Humans always stay; see MixBudget.Fits.
        return HasCustomMix(cfg)
            ? [.. all.Where(r => r == RaceArchetype.Human || MixWeight(cfg, r) > 0)]
            : all;
    }

    /// <summary>A race needs at least this share of a heritage before its terrain counts at all.</summary>
    private const double AffinityMinShare = 0.05;

    /// <summary>...and this much before the terrain scores its full affinity.</summary>
    private const double AffinityFullShare = 0.35;

    /// <summary>
    /// How well a race suits a heritage, given the heritage's whole terrain make-up.
    ///
    /// Taking the *modal* terrain instead — which is what this used to do — is why dwarves,
    /// giantkin and dusk elves went missing. A heritage spans dozens of counties, so its mode is
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
        RaceArchetype.Gnome, RaceArchetype.Giantkin, RaceArchetype.DuskElf, RaceArchetype.Hornkin
    ];

    /// <summary>Every race a culture-level roll may produce.</summary>
    private static readonly RaceArchetype[] CultureRaces =
    [
        RaceArchetype.Human, RaceArchetype.Dwarf, RaceArchetype.HighElf, RaceArchetype.WoodElf,
        RaceArchetype.Orc, RaceArchetype.Gnome, RaceArchetype.Giantkin, RaceArchetype.DuskElf,
        RaceArchetype.Hornkin
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
        List<Culture> cultures, TerrainClass[] provinceTerrain)
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
            // Empty on purpose, NOT a random terrain. The old fallback handed a landless culture
            // one random terrain at 100% share — full affinity reach, undiluted by any real mix —
            // which made ghost cultures out-score every real candidate for whichever race their
            // roll happened to suit. An empty profile scores the affinity floor everywhere:
            // neutral, ineligible under Require, and never anyone's best offer.
            return [];
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
            // Caves under broken country and the dark of the fens — the "subterranean depths" of
            // their trait, kept off the high mountains dwarves, orcs and giants already contest.
            (RaceArchetype.DuskElf, TerrainClass.Hills or TerrainClass.Wetlands) => 10,
            // Herders of the open ground nobody else claims — steppe and dry scrub, where only orcs
            // otherwise score — then the highland pastures and tribal forests they share.
            (RaceArchetype.Hornkin, TerrainClass.Steppe or TerrainClass.Drylands) => 12,
            (RaceArchetype.Hornkin, TerrainClass.Hills or TerrainClass.Taiga) => 10,
            (RaceArchetype.Hornkin, TerrainClass.Forest) => 8,
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
        // No human coin flip here any more. The mode's human:fantasy ratio is enforced upstream
        // as a COUNTY budget (see HumanShareFor) — the caller only reaches this while the budget
        // still holds, so this picks the best-fitting fantasy race and nothing else. The old coin
        // was the only place the ratio lived, and it ran per heritage regardless of size, after a
        // guarantee phase that usually consumed every heritage anyway; the measured result was
        // ~10% human in every mode.
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
                    => [RaceArchetype.Dwarf, RaceArchetype.Orc, RaceArchetype.Gnome, RaceArchetype.DuskElf, RaceArchetype.Hornkin, RaceArchetype.Human],

                TerrainClass.Forest or TerrainClass.Taiga or TerrainClass.Jungle
                    => [RaceArchetype.WoodElf, RaceArchetype.Gnome, RaceArchetype.Orc, RaceArchetype.Hornkin],

                TerrainClass.Steppe or TerrainClass.Drylands
                    => [RaceArchetype.Hornkin, RaceArchetype.Orc],

                TerrainClass.Arctic
                    => [RaceArchetype.Giantkin, RaceArchetype.Dwarf],

                TerrainClass.Desert
                    => [RaceArchetype.Orc, RaceArchetype.Gnome, RaceArchetype.HighElf],

                TerrainClass.Wetlands
                    => [RaceArchetype.Gnome, RaceArchetype.DuskElf, RaceArchetype.WoodElf],

                _ => [RaceArchetype.HighElf, RaceArchetype.Giantkin, RaceArchetype.Orc, RaceArchetype.Human, RaceArchetype.Dwarf]
            };
        }

        // A custom mix's switched-off races are never candidates, whatever the ground says.
        if (HasCustomMix(cfg))
        {
            var pool = FantasyPoolFor(cfg);
            candidates = [.. candidates.Where(pool.Contains)];
            if (candidates.Count == 0) return RaceArchetype.Human;
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

        // Less a custom mix's switched-off races; the same array otherwise, so the rolls are too.
        IReadOnlyList<RaceArchetype> races = HasCustomMix(cfg)
            ? [.. CultureRaces.Where(FantasyPoolFor(cfg).Contains)]
            : CultureRaces;

        if (cfg.RaceTerrain == RaceTerrainRule.Ignore)
            return rng.Pick(races);

        var shares = GetTerrainShares([culture], provinceTerrain);

        if (cfg.RaceTerrain == RaceTerrainRule.Require)
        {
            var fitting = races.Where(r => FitsTerrain(r, shares)).ToList();
            return fitting.Count > 0 ? rng.Pick(fitting) : RaceArchetype.Human;
        }

        return PickByAffinity(races, shares, rng);
    }

    private static EthnicityDef CreateEthnicity(
        string key,
        RaceArchetype archetype,
        string name,
        FantasyRaceMode mode,
        HumanLook look,
        Rng rng,
        string? forcedTemplate = null)
    {
        // Every people, fantasy or human, draws its base template the same way: from the world's
        // human-look setting. The template supplies every face feature a race does not author
        // (nose detail, lips, lids), so it is a people's "local" face under the race's own genes —
        // a dwarf in a Mediterranean world is a Mediterranean-looking dwarf.
        //
        // Races used to be pinned to a family instead — orcs and gnomes on the Asian templates,
        // dusk elves on the African ones, the rest European — which tied fantasy races to real-world
        // ethnicities for no reason the race itself gave. Nothing a race IS depends on the family:
        // its skin is its own skin_color band plus the gen_race_skin shift, its hair and eyes are
        // its own palettes (see ApplyColorGenes), and its shape is RaceMorphs forced at render.
        //
        // A human goes template first, family derived, which is the only way a preset can span two
        // families (a Mediterranean world wants byzantine and arab both) or move a template between
        // them (papuan belongs beside its South East Asian neighbours). See PickHumanLook.
        //
        // `forcedTemplate` is the editor's way in -- see Retemplate, the only caller that passes
        // one, which refuses anything but a human first. It skips a draw the generation path makes,
        // which is why nothing on the generation path may ever pass it.
        var (family, template) = forcedTemplate is { } forced
            ? (FamilyOf(forced), forced)
            : PickHumanLook(look, rng);

        var def = new EthnicityDef
        {
            Key = key,
            LocalizedName = archetype == RaceArchetype.Human ? name : $"{name} ({RaceName(archetype)})",
            Archetype = archetype,
            LookFamily = family,
            BaseTemplate = template
        };

        ApplyMorphGenes(def, archetype, mode, rng);
        ApplyColorGenes(def, archetype, family, mode);
        BuildVariants(def);

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
    /// The vanilla templates each <see cref="HumanLook"/> preset draws from, and how heavily.
    ///
    /// Every key here is one this file already used above, which is deliberate rather than lazy:
    /// a template name CK3 does not know is not an error, it is a silent fall-through to the
    /// ethnicity's own defaults, so an invented key would quietly undo the whole setting. Widening
    /// these lists means harvesting the install's real ethnicity keys first, the way
    /// <c>VanillaVocabulary</c> harvests everything else.
    ///
    /// The weights are the point of the feature, not decoration. A flat list over one region's
    /// templates still reads as scrambled at the culture level — it is the lean toward a couple of
    /// dominant looks, with the rest as a minority presence, that makes a world feel like one
    /// place.
    /// </summary>
    /// <remarks>
    /// Since HumanLooks these weights are a world's proportions rather than per-heritage odds: a
    /// preset's heritages are dealt the templates in exactly these shares (to the nearest heritage)
    /// and then placed by climate. The main loop still draws from them once per human so the race
    /// stream is unchanged, but that draw is replaced.
    /// </remarks>
    internal static IReadOnlyList<(string Template, int Weight)> LookTemplates(HumanLook look) => look switch
    {
        HumanLook.WesternEuropean => [("caucasian", 65), ("circumpolar", 35)],

        HumanLook.Mediterranean =>
            [("mediterranean", 45), ("byzantine", 30), ("arab", 15), ("turkic_west", 10)],

        HumanLook.SubSaharan => [("african", 60), ("east_african", 40)],

        HumanLook.EastAsian =>
            [("asian_han_chinese", 35), ("asian", 15), ("asian_mongol", 15), ("asian_manchu_korean", 15),
             ("asian_japanese", 15), ("asian_tibetan", 5)],

        HumanLook.SoutheastAsian =>
            [("asian_malay", 40), ("asian_austronesian", 35), ("papuan", 25)],

        HumanLook.MixedEuropean =>
            [("caucasian", 30), ("mediterranean", 20), ("slavic", 20), ("byzantine", 15), ("circumpolar", 15)],

        HumanLook.MixedMediterranean =>
            [("mediterranean", 30), ("arab", 20), ("african", 20), ("byzantine", 15), ("east_african", 15)],

        HumanLook.MixedAsian =>
            [("asian", 10), ("asian_han_chinese", 20), ("asian_mongol", 10), ("asian_manchu_korean", 5),
             ("asian_japanese", 5), ("asian_tibetan", 5), ("asian_malay", 15), ("indian", 15), ("south_indian", 15)],

        // Vanilla's own proportions for the region: Bedouin and Egyptian are all arab, Persian
        // 44/28/28 arab/mediterranean/turkic_west, Kurdish and Khwarezmian arab over turkic_west,
        // Sogdian with a turkic share.
        HumanLook.MiddleEastern =>
            [("arab", 55), ("turkic_west", 25), ("mediterranean", 10), ("turkic", 10)],

        // Cumans, Pechenegs and Khazars are turkic_west; Karluks, Kirghiz and Uyghurs turkic;
        // Mongols and Khitans asian_mongol.
        HumanLook.Steppe => [("turkic_west", 35), ("turkic", 35), ("asian_mongol", 30)],

        _ => []
    };

    /// <summary>
    /// Which of the four look families a vanilla template belongs to — the families the even split
    /// in <see cref="HumanLooks"/> is kept over. One table, there, so the split and the label a
    /// culture's inspector shows cannot disagree.
    /// </summary>
    private static string FamilyOf(string template) => HumanLooks.FamilyOf(template);

    /// <summary>
    /// A human's vanilla template and the colouring family that follows from it.
    ///
    /// <see cref="HumanLook.Varied"/> is not a preset with every template in it — it runs the
    /// original two-step draw verbatim, family first and template inside it. That is worth the
    /// duplication: it is the default, and a preset's single weighted draw consumes a different
    /// number of values from the sequence, so folding Varied into the same path would give every
    /// seed generated before this setting existed a different world.
    /// </summary>
    private static (string Family, string Template) PickHumanLook(HumanLook look, Rng rng)
    {
        if (look == HumanLook.Varied)
        {
            string family = rng.Pick(["caucasian", "african", "asian", "mena"]);
            return (family, PickVanillaTemplate(family, rng));
        }

        var templates = LookTemplates(look);
        if (templates.Count == 0) // An enum member with no table yet; behave as Varied rather than throw.
        {
            string family = rng.Pick(["caucasian", "african", "asian", "mena"]);
            return (family, PickVanillaTemplate(family, rng));
        }

        string picked = PickWeighted(templates, rng);
        return (FamilyOf(picked), picked);
    }

    /// <summary>
    /// One weighted draw. Local rather than on <see cref="Rng"/> because it is the only weighted
    /// pick in the generator, and it goes through <c>Int</c> so it stays seed-deterministic like
    /// every other draw.
    /// </summary>
    /// <summary>
    /// Every vanilla template a human culture may be moved onto in the editor.
    ///
    /// The same keys placement draws from (<see cref="HumanLooks.Roots"/>), in family order, because
    /// the same reason applies: a template CK3 does not know is not rejected, it is ignored, and the
    /// culture quietly keeps the look it had. Offering a key the install lacks would therefore
    /// produce a dropdown entry that silently does nothing.
    /// </summary>
    public static IReadOnlyList<string> HumanTemplates { get; } =
        HumanLooks.Roots.Select(r => r.Key).ToList();

    /// <summary>
    /// Moves one culture onto a different vanilla look, leaving every other culture alone.
    ///
    /// **The fork is the point.** Under <c>TieRaceToHeritage</c> every culture in a heritage shares
    /// one <see cref="EthnicityDef"/> object, so editing the definition in place would repaint the
    /// whole heritage — the opposite of what someone retemplating a single culture is asking for.
    /// This builds a fresh definition instead and repoints only this culture's entries.
    /// <c>ByHeritage</c> is deliberately not touched: the siblings resolve through <c>ByCulture</c>,
    /// where they still point at the original.
    ///
    /// **Humans only**, which is the whole contract with the fantasy races. A race's look is its
    /// <c>gen_race_skin</c> shift over a base tone chosen for that race; pointing an orc at
    /// <c>caucasian</c> would shift the base out from under the green without making the culture
    /// human, which is a worse outcome than refusing. Non-human cultures return false unchanged.
    ///
    /// The key is derived from the culture rather than from a counter, so retemplating the same
    /// culture repeatedly reuses one key instead of leaving a trail of dead definitions. The
    /// definition it replaces is left in the map on purpose — an ethnicity nothing references is
    /// inert in CK3, whereas pruning risks dropping one that a host culture's weighted list still
    /// names as a minority.
    /// </summary>
    /// <returns>False if the culture is not human, or the template is not one CK3 has.</returns>
    public static bool Retemplate(
        EthnicityMap map, Culture culture, string template, FantasyRaceMode mode, Rng rng)
    {
        var current = map.For(culture);
        if (current.Archetype != RaceArchetype.Human) return false;

        string? match = HumanTemplates
            .FirstOrDefault(t => string.Equals(t, template.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null) return false;

        var def = CreateEthnicity($"gen_ethnicity_{culture.Key}_edit", RaceArchetype.Human,
            culture.Name, mode, HumanLook.Varied, rng, forcedTemplate: match);

        // Dressed by the rules generation used — a vanilla people's mix, a skin window for the
        // culture's climate, its heritage's leans — with a fresh draw for the rest, so a second
        // pick of the same template visibly re-rolls. Without the context (a map built before it
        // existed) the culture simply wears the vanilla template as it stands.
        var ctx = map.Looks;
        var variants = HumanLooks.Dress(culture, match, def.Key, ctx?.Data, ctx?.ClimateOf(culture),
            ctx is null ? [] : HumanLooks.HeritageLeans(ctx.Seed, culture.Heritage), rng.Int(1, int.MaxValue - 1));
        def.Variants.AddRange(variants);

        Assign(map, culture, def, variants.Select(v => (v.Key, v.Weight)).ToList());
        return true;
    }

    /// <summary>
    /// Points a culture at a definition. The one place a culture's three entries are written
    /// together, so a retemplate and the revert that undoes it cannot disagree about what a
    /// complete assignment is.
    /// </summary>
    public static void Assign(
        EthnicityMap map, Culture culture, EthnicityDef def, List<(string Key, int Weight)> variants)
    {
        map.Ethnicities[def.Key] = def;
        map.ByCulture[culture] = def;
        map.ByCultureKey[culture.Key] = def;
        map.VariantsByCulture[culture] = variants;
    }

    private static string PickWeighted(IReadOnlyList<(string Template, int Weight)> options, Rng rng)
    {
        int i = rng.WeightedIndex(options, o => o.Weight);
        return options[i < 0 ? 0 : i].Template;
    }

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
    /// <summary>Public for the Cultures inspector, which reports a fantasy culture's race in place
    /// of the vanilla template it does not have.</summary>
    public static string RaceName(RaceArchetype archetype) => archetype switch
    {
        RaceArchetype.HighElf => "High Elf",
        RaceArchetype.WoodElf => "Wood Elf",
        _ => archetype.ToString()
    };

    /// <summary>
    /// The phonology a race speaks, or null for humans, who draw from the world's real-world
    /// flavours like any people. Five fantasy tongues for eight races, grouped by kinship: the
    /// dusk elves are estranged elves, gnomes are dwarf-kin, and giants are as rough-tongued as orcs.
    /// The hornkin have a tongue of their own, from the back of the throat.
    /// Applied by <see cref="Cultures.SpeakAsRace"/>.
    /// </summary>
    public static LanguageFlavour? TongueOf(RaceArchetype archetype) => archetype switch
    {
        RaceArchetype.HighElf or RaceArchetype.WoodElf or RaceArchetype.DuskElf => LanguageFlavour.Sylvan,
        RaceArchetype.Dwarf or RaceArchetype.Gnome => LanguageFlavour.Dwarven,
        RaceArchetype.Orc or RaceArchetype.Giantkin => LanguageFlavour.Harsh,
        RaceArchetype.Hornkin => LanguageFlavour.Guttural,
        _ => null
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
                // portrait-modifier enforcement (Emit/Characters/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.58f, 0.74f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_neg", 0.34f, 0.44f);
                // The ear itself is a real pointed blendshape now (gen_bs_elf_ears, in the table
                // above). Vanilla's size and outward stay low beside it: pushed toward 1.0 they give
                // a comic ear rather than an elegant one.
                // Upswept eyes are the strongest elf cue stock geometry has after height, so the
                // high elf takes it harder than the wood elf does.
                // Vanilla's own ceiling for this gene is 0.70, and at the old 0.58-0.70 every high
                // elf sat on it: upswept became a squint.
                Shape(def, rng, f, "gene_eye_angle", "eye_angle_pos", 0.55f, 0.63f);
                // No gene_eye_distance. Close-set eyes read as unsettling in a human face at any
                // strength, and vanilla holds this gene to 0.45-0.55 for a beautiful character —
                // pushing it to 0.20 was working directly against the look this race wants.
                //
                // Every gene_bs_ value below is a blendshape strength, and vanilla's register for
                // those is small: its ethnicities put ~90% of weight in 0-0.2 (@blend1), a few
                // percent in 0.2-0.5, and 0.5-0.8 (@blend3) is a one-in-a-hundred outlier. The old
                // tables had cheeks and brow at 0.55-0.85 — every elf a blend3 outlier on three
                // genes at once, which rendered as hollow, bony, gaunt faces (2026-09-28). The
                // race now sits at the top of blend1 and into blend2: distinct, not deformed.
                Shape(def, rng, f, "gene_bs_eye_fold_shape", "eye_fold_shape_02_pos", 0.10f, 0.24f);
                Shape(def, rng, f, "gene_head_height", "head_height_pos", 0.54f, 0.64f);
                Shape(def, rng, f, "gene_forehead_height", "forehead_height_pos", 0.54f, 0.64f);
                Shape(def, rng, f, "gene_forehead_brow_height", "forehead_brow_height_pos", 0.56f, 0.68f);
                Shape(def, rng, f, "gene_bs_forehead_brow_curve", "forehead_brow_curve_pos", 0.10f, 0.28f);
                Shape(def, rng, f, "gene_bs_cheek_forward", "cheek_forward_pos", 0.15f, 0.35f);
                Shape(def, rng, f, "gene_bs_cheek_height", "cheek_height_pos", 0.15f, 0.35f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_neg", 0.36f, 0.46f);
                Shape(def, rng, f, "gene_bs_nose_length", "nose_length_pos", 0.08f, 0.24f);
                // gene_bs_nose_profile has no "straight" template — only _neg, _pos, and the two
                // hawk variants — so the straight elven nose is a *weak* _pos rather than a template
                // of its own. On a bs gene the neutral end is 0, not 0.5, so this range is correct.
                Shape(def, rng, f, "gene_bs_nose_profile", "nose_profile_pos", 0.05f, 0.20f);
                // Ages slowly rather than not at all: old_beauty_1 only, never `no_aging`. gene_age
                // also carries the CHILD shapes (infant head and body proportions, child blendshapes),
                // and `no_aging` is an empty template, so it took those too: a third of elf children
                // rendered as adults from birth (user, in game, 2026-10-02). old_beauty_1 keeps the
                // full children block and its old-age shapes only begin at 38 and finish at 85.
                // Its strength is irrelevant (every setting is a fixed min = max), so no range to tune.
                AddGene(def, "gene_age", "old_beauty_1", 0.0f, 0.6f);
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
                // portrait-modifier enforcement (Emit/Characters/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.50f, 0.75f);
                // Slanted eyes come from gene_eye_angle alone. There is no gene_bs_eye_slant in
                // vanilla — nothing matching "slant" exists at all.
                // Blendshape strengths held to vanilla's register, as for the high elf above; the
                // old 0.45-0.75 cheeks and nose ridge were one-in-a-hundred outliers on everyone.
                Shape(def, rng, f, "gene_eye_angle", "eye_angle_pos", 0.54f, 0.64f);
                Shape(def, rng, f, "gene_bs_eye_size", "eye_size_pos", 0.10f, 0.28f);
                Shape(def, rng, f, "gene_head_width", "head_width_pos", 0.52f, 0.64f);
                Shape(def, rng, f, "gene_bs_cheek_forward", "cheek_forward_pos", 0.12f, 0.32f);
                Shape(def, rng, f, "gene_bs_cheek_height", "cheek_height_pos", 0.15f, 0.35f);
                Shape(def, rng, f, "gene_bs_nose_size", "nose_size_neg", 0.10f, 0.28f);
                Shape(def, rng, f, "gene_bs_nose_ridge_angle", "nose_ridge_angle_pos", 0.10f, 0.30f);
                // old_beauty_1 only; see the high elf for why `no_aging` (no child shapes) is gone.
                AddGene(def, "gene_age", "old_beauty_1", 0.0f, 0.7f);
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
                // portrait-modifier enforcement (Emit/Characters/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_width", "neck_width_pos", 0.85f, 1.0f);
                Shape(def, rng, f, "gene_neck_length", "neck_length_neg", 0.05f, 0.25f);
                Shape(def, rng, f, "gene_head_width", "head_width_pos", 0.70f, 0.95f);
                Shape(def, rng, f, "gene_jaw_forward", "jaw_forward_pos", 0.55f, 0.85f);
                Shape(def, rng, f, "gene_bs_jaw_def", "jaw_def_pos", 0.40f, 0.70f);   // was 0.60-0.90
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
                // portrait-modifier enforcement (Emit/Characters/RaceMorphWriter.cs) cannot drift apart.
                // The tusk row is the standard tusk's strength; AddTusks spreads it over the variants.
                foreach (var m in RaceMorphs.Of(archetype))
                {
                    if (m.Gene == OrcTusks.Gene) AddTusks(def, rng, m.Tiered ? f : Untiered, m);
                    else Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                }

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
                // portrait-modifier enforcement (Emit/Characters/RaceMorphWriter.cs) cannot drift apart.
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
                // portrait-modifier enforcement (Emit/Characters/RaceMorphWriter.cs) cannot drift apart.
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

            case RaceArchetype.DuskElf:
                // The third elf, and it has to hold its own shape against the other two. Large
                // light-adapted eyes are the distinguishing feature; the old values sank the eye
                // with eye_depth_pos 0.60-0.85 instead, which is the opposite read.
                // The race-defining genes come from the shared table so the ethnicity and the
                // portrait-modifier enforcement (Emit/Characters/RaceMorphWriter.cs) cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_length", "neck_length_pos", 0.56f, 0.72f);
                Shape(def, rng, f, "gene_neck_width", "neck_width_neg", 0.32f, 0.42f);
                // Blendshape strengths held to vanilla's register, as for the high elf. The eyes
                // stay the one gene allowed well into blend2, since they are this race's mark.
                Shape(def, rng, f, "gene_bs_eye_size", "eye_size_pos", 0.20f, 0.40f);
                Shape(def, rng, f, "gene_eye_depth", "eye_depth_pos", 0.36f, 0.46f);
                Shape(def, rng, f, "gene_eye_angle", "eye_angle_pos", 0.54f, 0.63f);
                Shape(def, rng, f, "gene_bs_cheek_forward", "cheek_forward_pos", 0.12f, 0.32f);
                Shape(def, rng, f, "gene_bs_cheek_height", "cheek_height_pos", 0.12f, 0.32f);
                Shape(def, rng, f, "gene_chin_width", "chin_width_neg", 0.34f, 0.44f);
                Shape(def, rng, f, "gene_bs_nose_length", "nose_length_neg", 0.10f, 0.26f);
                // old_beauty_1 only; see the high elf for why `no_aging` (no child shapes) is gone.
                AddGene(def, "gene_age", "old_beauty_1", 0.0f, 0.6f);
                AddGene(def, "gene_eyebrows_fullness", "layer_2_low_thickness", 0.0f, 1.0f);
                AddGene(def, "complexion", "complexion_beauty_1", 0.50f, 0.85f);
                AddGene(def, "gene_body_hair", "body_hair_sparse", 0.15f, 0.40f);
                AddGene(def, "gene_baldness", "no_baldness", 0.0f, 0.15f);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 80);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 20);
                break;

            case RaceArchetype.Hornkin:
                // Highland and steppe herders. The race-defining genes come from the shared table
                // so the ethnicity and the portrait-modifier enforcement cannot drift apart.
                foreach (var m in RaceMorphs.Of(archetype))
                    Shape(def, rng, m.Tiered ? f : Untiered, m.Gene, m.Template, m.Min, m.Max);
                Shape(def, rng, f, "gene_neck_width", "neck_width_pos", 0.55f, 0.75f);
                Shape(def, rng, f, "gene_bs_cheek_height", "cheek_height_pos", 0.50f, 0.70f);
                Shape(def, rng, f, "gene_bs_nose_profile", "nose_profile_pos", 0.20f, 0.45f);
                AddGene(def, "complexion", "complexion_5", 0.30f, 0.70f);
                AddGene(def, "gene_body_hair", "body_hair_dense", 0.35f, 0.70f);
                AddGene(def, "gene_hair_type", "hair_wavy", 0.0f, 1.0f, weight: 45);
                AddGene(def, "gene_hair_type", "hair_curly", 0.0f, 1.0f, weight: 35);
                AddGene(def, "gene_hair_type", "hair_straight", 0.0f, 1.0f, weight: 20);
                AddHorns(def, mode, rng);
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
        (geneKey.StartsWith("gene_bs_", StringComparison.Ordinal) && geneKey != "gene_bs_body_type")
        // Our own blendshape genes (gen_bs_elf_ears) are 0-neutral for the same reason.
        || geneKey.StartsWith("gen_bs_", StringComparison.Ordinal)
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

    private static void ApplyColorGenes(EthnicityDef def, RaceArchetype archetype, string family, FantasyRaceMode mode)
    {
        switch (archetype)
        {
            case RaceArchetype.HighElf:
                ApplyRaceSkin(def, archetype, mode);
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
                ApplyRaceSkin(def, archetype, mode);
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
                ApplyRaceSkin(def, archetype, mode);
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
                ApplyRaceSkin(def, archetype, mode);
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
                ApplyRaceSkin(def, archetype, mode);
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
                ApplyRaceSkin(def, archetype, mode);
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

            case RaceArchetype.DuskElf:
                // Reaches the bottom of the ramp on purpose. Stopping at t=0.70 left the
                // darkest third of the band unreachable, which is the third that makes a
                // drow look like a drow.
                ApplyRaceSkin(def, archetype, mode);
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

            case RaceArchetype.Hornkin:
                // Ashen at low intensity, crimson at the top of the ramp (see gen_skin_hornkin), under
                // dark hair and amber-to-gold eyes.
                ApplyRaceSkin(def, archetype, mode);
                AddColor(def, "hair_color", Hair.Black, weight: 35);
                AddColor(def, "hair_color", Hair.DarkBrown, weight: 30);
                AddColor(def, "hair_color", Hair.Auburn, weight: 20);
                AddColor(def, "hair_color", Hair.Brown, weight: 15);
                AddColor(def, "eye_color", Eye.Amber, weight: 35);
                AddColor(def, "eye_color", Eye.Gold, weight: 30);
                AddColor(def, "eye_color", Eye.Hazel, weight: 20);
                AddColor(def, "eye_color", Eye.DarkBrown, weight: 15);
                break;


            case RaceArchetype.Human:
            default:
                // Nothing, as in ApplyMorphGenes. A human's colouring is vanilla's: skin, hair and
                // eyes all come from the template, and per-culture variety is added by HumanLooks as
                // variants over vanilla's own keys. This case used to write our own hair and eye
                // palettes per family over the template's, and they were measurably off — the eye
                // swatches 1.5–2x brighter than vanilla's (hazel/green luminance ~72 against
                // vanilla's 48, the darkest brown 22 against vanilla African's 6), and a "Black" and
                // a "BlueBlack" hair band 6 and 4 in luminance, which split into two variants that
                // no one could tell apart.
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
    private static void BuildVariants(EthnicityDef def)
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

    /// <summary>
    /// A horned people's mix of horn styles, and the skin mound every horn grows from.
    ///
    /// A mix rather than one style, so a people reads as a people and not a uniform: a shuffled
    /// order takes weights 50/25/15/10, so each culture has a signature style, a common second and
    /// rarer others — and because <c>gen_horns</c> is inheritable, families keep theirs. On a
    /// low-fantasy map the nubs are pushed to the front, so horned folk there are marked rather than
    /// monstrous. The filed stump is never rolled; filing is something a character does.
    /// </summary>
    private static void AddHorns(EthnicityDef def, FantasyRaceMode mode, Rng rng)
    {
        string[] styles = ["ibex", "ram", "forward", "nubs"];
        for (int i = styles.Length - 1; i > 0; i--)
        {
            int j = rng.Int(0, i);                                // inclusive both ends
            (styles[i], styles[j]) = (styles[j], styles[i]);
        }

        int[] weights = [50, 25, 15, 10];
        for (int i = 0; i < styles.Length; i++)
        {
            int w = weights[i] + (mode == FantasyRaceMode.LowFantasy && styles[i] == "nubs" ? 60 : 0);
            AddGene(def, Horns.Gene, Horns.TemplateOf(styles[i]), 0.0f, 1.0f, weight: w);
        }

        AddGene(def, Horns.BossGene, Horns.BossTemplate, 1.0f, 1.0f);
    }

    /// <summary>
    /// An orc culture's mix of inherited tusk shapes (MapGen/Peoples/OrcTusks.cs): standard, stubby and great
    /// shuffled onto weights 12/6/3, so one clan runs to great tusks and another to stubby ones, and
    /// families keep theirs by inheritance. All at the standard tusk's strength. Broken tusks are
    /// scars and never in a DNA.
    /// </summary>
    private static void AddTusks(EthnicityDef def, Rng rng, float intensity, RaceMorph standard)
    {
        string[] variants = [.. OrcTusks.Inherited];
        for (int i = variants.Length - 1; i > 0; i--)
        {
            int j = rng.Int(0, i);                                // inclusive both ends
            (variants[i], variants[j]) = (variants[j], variants[i]);
        }

        int[] weights = [12, 6, 3];
        for (int i = 0; i < variants.Length; i++)
            Shape(def, rng, intensity, OrcTusks.Gene, OrcTusks.TemplateOf(variants[i]), standard.Min, standard.Max, weights[i]);
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
        EthnicityDef def, RaceArchetype archetype, FantasyRaceMode mode)
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