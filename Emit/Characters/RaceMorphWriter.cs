using System.Globalization;
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using static Ck3MapGen.Config.MapConfig;

namespace Ck3MapGen.Emit;

/// <summary>
/// Emits <c>gfx/portraits/portrait_modifiers/99_gen_race_morphs.txt</c>: the render-time
/// enforcement of each race's shape and colour, keyed on the phenotype traits.
///
/// This is the "race-defining genes snap, everything else blends" half of mixed-parentage
/// handling. A character's DNA is left entirely alone — nose, eyes, mouth, cheeks, hair and base
/// colouring inherit from the parents normally, so children look like their families — and only
/// the genes in <see cref="RaceMorphs"/>, plus the <c>gen_race_skin</c> shift, are forced for
/// characters carrying the race's trait. That fixes the child of a drow and a human without any
/// birth-time genome surgery, and it equally fixes the courtier the engine invented with a human
/// face who was handed <c>phenotype_stocky</c> by the culture pulse: the trait now IS the look.
///
/// It has to be a generated file rather than a BaseFilesToCopy static because the ranges are
/// fantasy-tier-scaled, and a static file cannot know whether this map is LowFantasy or
/// ExoticSurreal. (It used to need generated culture keys too, for the elf skin split; one trait
/// per elf retired that.)
///
/// **Two groups, because a portrait modifier group applies exactly one of its entries.** Shape and
/// skin are separate axes that must both land, so they are separate groups. Priorities 90 and 91
/// sit above every stock group (highest is 50) and below the mod's own beard group at 100 and the
/// wilderness hider at 9999. Horns (92) and the elves' male faces (93) are further groups on the
/// same reasoning.
/// </summary>
public static class RaceMorphWriter
{
    /// <summary>
    /// One trait per race, and that one-to-one is load-bearing. The two elves used to share
    /// <c>phenotype_gracile</c>, which left this file able to force only the genes both elf tables
    /// agreed on — averaged, so high elves lost their height — and able to tell the two skins apart
    /// only by a list of generation-time culture keys, so every wood elf outside one (minorities,
    /// diverged and hybrid cultures, converts) rendered with high-elf skin.
    /// </summary>
    private static readonly (RaceArchetype Archetype, string Trait)[] Races =
    [
        (RaceArchetype.HighElf, "phenotype_gracile"),
        (RaceArchetype.WoodElf, "phenotype_sylvan"),
        (RaceArchetype.Dwarf, "phenotype_stocky"),
        (RaceArchetype.Orc, "phenotype_rough_hewn"),
        (RaceArchetype.Gnome, "phenotype_diminutive"),
        (RaceArchetype.Giantkin, "phenotype_towering"),
        (RaceArchetype.DuskElf, "phenotype_dusk_adapted"),
        (RaceArchetype.Hornkin, "phenotype_horned"),
    ];

    /// <summary>The race trait of the horned people, and the trait for horns borne by anyone else.</summary>
    public const string HornedTrait = "phenotype_horned";
    public const string HornbornTrait = "hornborn";

    /// <summary>Set by gen_file_horns_decision for five years: horns shown as the filed stump.</summary>
    public const string FiledFlag = "gen_horns_filed";

    /// <summary>A character's forced horn style, set by script (00_horn_effects.txt) for every Hornborn
    /// and for horned children whose genome may lack horns — gen_horn_style_ibex and so on.</summary>
    public static string StyleFlag(string style) => $"gen_horn_style_{style}";

    /// <summary>A human for the resets: a traited human, or a human of a mixed line (flag set at birth).</summary>
    private const string HumanCondition =
        "OR = { has_trait = phenotype_human has_character_flag = gen_phenotype_human }";

    /// <summary>
    /// Race head features that belong to some races only — each gene with its empty template. A race
    /// whose <see cref="RaceMorphs"/> row does not name one has it forced to none.
    /// </summary>
    private static readonly (string Gene, string None)[] ExclusiveFeatures =
    [
        (PointedEars.Gene, PointedEars.NoneTemplate),
        (OrcTusks.Gene, OrcTusks.NoneTemplate),
        (GiantFace.Gene, GiantFace.NoneTemplate),
        (OrcBrow.Gene, OrcBrow.NoneTemplate),
    ];

    /// <summary>
    /// Whether this map has fantasy races at all. Shared with <see cref="RaceHeadWriter"/>, whose
    /// head shapes must ship exactly when this file enforces them.
    /// </summary>
    public static bool RacesOn(MapConfig cfg)
        => cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly;

    public static void WriteAll(string modDir, MapConfig cfg, EthnicityMap ethnicities)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "portrait_modifiers");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "99_gen_race_morphs.txt");

        bool racesOn = cfg.EnableFantasyEthnicities && cfg.RaceMode != FantasyRaceMode.HumanOnly;
        WriteEthnicityTriggers(modDir, racesOn ? ethnicities : null);

        // Written even when there is nothing to write, because a mod directory is reused between
        // runs: turning fantasy off must retire the previous run's file rather than leave it
        // enforcing races nobody carries.
        if (!racesOn)
        {
            ParadoxText.WriteBom(path, "# Fantasy races are disabled for this map; nothing to enforce.\n");
            return;
        }

        float f = Ethnicities.MorphIntensity(cfg.RaceMode);
        var (skinLo, skinHi) = RaceSkin.TierRange(cfg.RaceMode);
        var b = new JominiBuilder();
        b.Comment("""
                  Generated: race-defining genes forced by phenotype trait. See Emit/RaceMorphWriter.cs.
                  The values mirror MapGen/Ethnicities.cs RaceMorphs — one table feeds both.
                  """);
        b.Blank();

        // ---- Group 1: shape --------------------------------------------------------------
        using (b.Block("gen_race_morphs"))
        {
            b.Blank();
            b.Field("usage", "game");
            b.Field("selection_behavior", "max");
            b.Field("priority", "90");
            b.Blank();

            foreach (var (archetype, trait) in Races)
                ShapeEntry(b, $"gen_race_morph_{archetype.ToString().ToLowerInvariant()}",
                    $"has_trait = {trait}", RaceMorphs.Of(archetype), f);

            // Half-races: humans born to an elf or orc parent (or to two halves of the same kind),
            // marked at birth by gen_resolve_half_race_effect. Half the full race's ears or tusks,
            // at this map's tier. Weight 90, under the race entries' 100, so a race trait always
            // wins if a character somehow carries both.
            HalfEntry(b, "gen_race_morph_half_elf", "has_character_flag = gen_half_elf",
                FeatureOf(RaceArchetype.HighElf, PointedEars.Gene), f);
            HalfEntry(b, "gen_race_morph_half_orc", "has_character_flag = gen_half_orc",
                FeatureOf(RaceArchetype.Orc, OrcTusks.Gene), f);

            // Every other human: ears and tusks are inheritable, so a quarter-elf would otherwise
            // show whatever a grandparent handed down. The half features are not passed on — only
            // the half-race entries above put them back. Weight 50, under both.
            //
            // The human TRAIT counts as well as the mixed-line flag, because history characters are
            // never flagged: a human born before the bookmark to a minority elf (culture 6 of the
            // "qw" map lists 15 = a high-elf ethnicity beside its human one) inherited the ears in
            // DNA and showed them, with nothing to switch them off. No human ethnicity carries ears
            // or tusks, so on a traited human they are always an inheritance leak.
            using (b.Block("gen_race_morph_mixed_human"))
            {
                using (b.Block("dna_modifiers"))
                    foreach (var (gene, none) in ExclusiveFeatures)
                        Morph(b, gene, none, 0f, 0f);
                Weight(b, HumanCondition, weight: 50);
            }

            b.Blank();
        }

        b.Blank();

        // ---- Group 2: skin ---------------------------------------------------------------
        // Separate group so it stacks with the shape entry (one entry per group applies).
        using (b.Block("gen_race_skins"))
        {
            b.Blank();
            b.Field("usage", "game");
            b.Field("selection_behavior", "max");
            b.Field("priority", "91");
            b.Blank();

            foreach (var (archetype, trait) in Races)
                SkinEntry(b, $"gen_race_skin_{archetype.ToString().ToLowerInvariant()}",
                    $"has_trait = {trait}", RaceSkin.TemplateOf(archetype)!, skinLo, skinHi,
                    weight: 100);

            // The human reset. gen_race_skin is INHERITABLE — that is what makes half-breeds work —
            // so the human child of an elf carries the elven shift in its DNA, and without this entry
            // nothing ever turns it off: the observed bug was literally a wood-elf-coloured human
            // child. Range { 0 0 } because there is nothing to vary — off is off.
            //
            // Keyed on the gen_phenotype_human FLAG (humans of a mixed line, set at birth — see
            // 00_phenotype_birth_effects.txt) OR the phenotype_human TRAIT. The trait used to be left
            // out because minority-race members held it while their looks came from rolled ethnicity
            // genes, so a trait-keyed reset erased them. They now hold their own race's trait
            // (gen_reconcile_phenotype_with_genes_effect at game start), so that argument is gone —
            // and the flag alone missed history characters, which are never flagged: a human born
            // before the bookmark to a minority-elf father rendered with the elven shift (and ears).
            // No human ethnicity names gen_race_skin, so resetting it on a human removes only leaks.
            SkinEntry(b, "gen_race_skin_human", HumanCondition,
                "gen_skin_human", 0f, 0f, weight: 100);

            // The eight fantasy traits, the human trait and the mixed-line flag are the whole roster;
            // a character with none of them (a pre-pulse engine character) has no entry fire and
            // keeps its inherited appearance.
        }

        // ---- Group 3: horns ----------------------------------------------------------------
        bool horns = RaceHeadWriter.HornGeneShipped(modDir);
        if (horns) HornGroup(b);
        int ornamentEntries = horns ? HornOrnamentGroup(b, cfg.Seed, ethnicities) : 0;

        // ---- Group 4: men's faces ----------------------------------------------------------
        int maleEntries = MaleFaceGroup(b, f);

        // ---- Groups 5 and 6: weight and muscle back on top of the race body ----------------
        BodyStateGroups(b);

        // ---- Group 7: tusks --------------------------------------------------------------
        TuskGroup(b, f);

        // ---- Group 8: ageless elves --------------------------------------------------------
        AgelessElfGroup(b);

        ParadoxText.WriteBom(path, b.ToString());
        Console.WriteLine($"  race morphs written: {Races.Length + 3} shape (incl. half-elf, half-orc, mixed-human), " +
                          $"{Races.Length + 1} skin{(horns ? $", {HornEntries} horn, {ornamentEntries} horn-ornament" : "")}, {maleEntries} per-sex face and 3 weight/muscle " +
                          "enforcement entries to 99_gen_race_morphs.txt");
    }

    /// <summary>
    /// The per-sex face corrections — <see cref="RaceMorphs.MaleOf"/> for men and
    /// <see cref="RaceMorphs.FemaleOf"/> for women — one entry per race and sex that has any. Its
    /// own group, after the shape group, because it overwrites one of that group's genes (jaw width)
    /// and must stack with it rather than compete. The women's entries share the group: a character
    /// matches at most one sex. Returns the entry count. (The group keeps its original name.)
    /// </summary>
    private static int MaleFaceGroup(JominiBuilder b, float intensity)
    {
        int count = 0;
        b.Blank();
        using (b.Block("gen_race_male_faces"))
        {
            b.Blank();
            b.Field("usage", "game");
            b.Field("selection_behavior", "max");
            b.Field("priority", "93");
            b.Blank();

            foreach (var (archetype, trait) in Races)
            {
                var morphs = RaceMorphs.MaleOf(archetype);
                if (morphs.Count == 0) continue;

                using (b.Block($"gen_race_male_face_{archetype.ToString().ToLowerInvariant()}"))
                {
                    using (b.Block("dna_modifiers"))
                        ForcedMorphs(b, morphs, intensity);
                    Weight(b, $"has_trait = {trait}", weight: 100, "is_female = no");
                }

                b.Blank();
                count++;
            }

            foreach (var (archetype, trait) in Races)
            {
                var morphs = RaceMorphs.FemaleOf(archetype);
                if (morphs.Count == 0) continue;

                using (b.Block($"gen_race_female_face_{archetype.ToString().ToLowerInvariant()}"))
                {
                    using (b.Block("dna_modifiers"))
                        ForcedMorphs(b, morphs, intensity);
                    Weight(b, $"has_trait = {trait}", weight: 100, "is_female = yes");
                }

                b.Blank();
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Vanilla's weight and muscularity, applied again after the race shape.
    ///
    /// Vanilla shows a character's weight (the <c>weight</c> group) and prowess (<c>muscularity</c>)
    /// by MODIFYING the strength of <c>gene_bs_body_type</c> and <c>gene_bs_body_shape</c>, in
    /// <c>gfx/portraits/portrait_modifiers/99_special.txt</c> at the default priority 0. The shape
    /// group then REPLACES both genes at 90 for every fantasy race, so the weight and the muscle were
    /// wiped: a gluttonous orc stayed lean in the body while his idle pose — picked from the weight
    /// itself in portrait_animations — went heavy. Found 2026-09-28.
    ///
    /// These two groups re-apply vanilla's own values (keep them in step with 99_special.txt) to
    /// exactly the characters the shape group replaced, the eight fantasy traits, so humans are not
    /// modified twice. Modify adds to the strength of whichever template is present, which is what
    /// vanilla does to a human's DNA: a heavy elf is heavier than a lean one, and still an elf.
    /// </summary>
    private static void BodyStateGroups(JominiBuilder b)
    {
        string raced = $"OR = {{ {string.Join(" ", Races.Select(r => $"has_trait = {r.Trait}"))} }}";

        b.Blank();
        using (b.Block("gen_race_weight"))
        {
            b.Blank();
            b.Field("usage", "game");
            b.Field("priority", "94");
            b.Blank();

            foreach (var (name, above, multiplier) in new[] { ("gen_race_overweight", true, "0.5"), ("gen_race_underweight", false, "-0.5") })
            {
                using (b.Block(name))
                {
                    b.Field("ignore_outfit_tags", "yes");
                    using (b.Block("dna_modifiers"))
                    using (b.Block("morph"))
                    {
                        b.Field("mode", "modify");
                        b.Field("gene", "gene_bs_body_type");
                        using (b.Block("value"))
                        {
                            b.Field("value", "scope:weight_for_portrait");
                            b.Field("multiply", multiplier);
                        }
                    }

                    Weight(b, raced, weight: 100,
                        above ? "scope:current_weight > overweight_threshold" : "scope:current_weight < underweight_threshold",
                        "NOT = { has_character_flag = has_scripted_weight }");
                }

                b.Blank();
            }
        }

        b.Blank();
        using (b.Block("gen_race_muscularity"))
        {
            b.Blank();
            b.Field("usage", "game");
            b.Field("priority", "95");
            b.Blank();

            using (b.Block("gen_race_muscular"))
            {
                b.Field("ignore_outfit_tags", "yes");
                using (b.Block("dna_modifiers"))
                using (b.Block("morph"))
                {
                    b.Field("mode", "modify");
                    b.Field("gene", "gene_bs_body_shape");
                    using (b.Block("value"))
                    {
                        // Vanilla's branches, less the no-character one: the weight asks for traits,
                        // so a character always exists. The designer edits scope:prowess, not the
                        // character.
                        b.Field("value", "0");
                        using (b.Block("if"))
                        {
                            using (b.Block("limit")) b.Field("exists", "scope:ruler_designer");
                            b.Field("add", "scope:prowess");
                        }

                        using (b.Block("else")) b.Field("add", "prowess_for_portrait");
                        b.Field("multiply", "2");
                        b.Field("max", "1.0");
                    }
                }

                Weight(b, raced, weight: 100);
            }
        }
    }

    /// <summary>The elves' age past which the face stops changing.</summary>
    public const int AgelessFromAge = 30;

    /// <summary>
    /// Elves stop aging in the face from <see cref="AgelessFromAge"/>: gene_age replaced with vanilla's
    /// empty <c>no_aging</c> template, so no wrinkles, old-age decals, sagging or stoop.
    ///
    /// Render-time and age-gated on purpose, NOT the ethnicity's template. gene_age also carries the
    /// CHILD shapes (infant proportions, child blendshapes), and an ethnicity that rolled no_aging gave a
    /// third of elf children adult bodies from birth (2026-10-02). Children keep their DNA's
    /// old_beauty_1 and its child block; from 30 the empty template takes over, by which point the
    /// child curves are long finished, so nothing visible switches off at the boundary.
    ///
    /// Covers every elf already in a save too, including those whose DNA came from human parents.
    /// Hair greying is NOT here: no gene or define drives it, so the engine greys hair on its own.
    /// </summary>
    private static void AgelessElfGroup(JominiBuilder b)
    {
        string elves = string.Join(" ", new[] { "phenotype_gracile", "phenotype_sylvan", "phenotype_dusk_adapted" }
            .Select(t => $"has_trait = {t}"));

        b.Blank();
        using (b.Block("gen_race_ageless_elves"))
        {
            b.Blank();
            b.Field("usage", "game");
            b.Field("selection_behavior", "max");
            b.Field("priority", "97");
            b.Blank();

            using (b.Block("gen_race_ageless_elf"))
            {
                b.Field("ignore_outfit_tags", "yes");
                using (b.Block("dna_modifiers"))
                    b.Inline("morph", "mode = replace  gene = gene_age  template = no_aging  value = 1");
                Weight(b, $"OR = {{ {elves} }}", weight: 100, $"age >= {AgelessFromAge}");
            }
        }
    }

    /// <summary>Genes the shape group leaves to a group of their own (the orc's tusks: <see cref="TuskGroup"/>).</summary>
    private static readonly HashSet<string> OwnGroupGenes = [OrcTusks.Gene];

    /// <summary>A race row's tier-scaled strength range, as <see cref="ForcedMorphs"/> computes it.</summary>
    private static (float Lo, float Hi) Range(RaceMorph m, float intensity)
    {
        float scale = m.Tiered ? intensity : 1.0f;
        float n = Ethnicities.NeutralOf(m.Gene);
        return (Math.Clamp(n + (m.Min - n) * scale, 0f, 1f), Math.Clamp(n + (m.Max - n) * scale, 0f, 1f));
    }

    /// <summary>
    /// Which tusks an orc or half-orc shows (<see cref="OrcTusks.Variants"/>). Its own group because
    /// the variant must come from the DNA — each orc culture's ethnicity carries its own mix of
    /// standard, stubby and great tusks, and families hand theirs down — where the shape group can
    /// only force one template for the whole race. Highest weight first:
    /// <list type="number">
    /// <item><b>Broken</b> (500/490): a scar, from the <c>gen_tusk_broken_{left,right}</c> flag
    /// (00_tusk_effects.txt). Never inherited.</item>
    /// <item><b>Inherited variant</b> (200/190): the DNA's stubby or great, re-drawn at the race's
    /// strength so a mixed parentage cannot leave it half-grown.</item>
    /// <item><b>Standard</b> (100/90): everyone else — the DNA's own standard tusk, or none at all
    /// (a history character's padded DNA, a human parent's genome).</item>
    /// </list>
    /// Half-orcs (the <c>gen_half_orc</c> flag) take the same at half strength, 10 below each.
    ///
    /// <c>has_gene</c> on a MORPH gene, as vanilla's beards ask <c>gene_hair_type</c>. It failed on
    /// the horns' ACCESSORY gene, so the checks only ever pick a variant: if it never fires, every orc
    /// falls through to the standard tusk — exactly the look before variants existed.
    /// </summary>
    private static void TuskGroup(JominiBuilder b, float intensity)
    {
        var (lo, hi) = Range(FeatureOf(RaceArchetype.Orc, OrcTusks.Gene), intensity);
        const string orc = "has_trait = phenotype_rough_hewn";
        const string half = "has_character_flag = gen_half_orc";

        b.Blank();
        using (b.Block("gen_race_tusks"))
        {
            b.Blank();
            b.Field("usage", "game");
            b.Field("selection_behavior", "max");
            b.Field("priority", "96");
            b.Blank();

            void Entry(string name, string variant, float scale, int weight, params string[] conditions)
            {
                using (b.Block(name))
                {
                    using (b.Block("dna_modifiers"))
                        Morph(b, OrcTusks.Gene, OrcTusks.TemplateOf(variant), lo * scale, hi * scale);
                    Weight(b, conditions[0], weight, conditions[1..]);
                }

                b.Blank();
            }

            foreach (string side in new[] { "left", "right" })
            {
                Entry($"gen_race_tusks_broken_{side}", $"broken_{side}", 1f, 500, orc, $"has_character_flag = {OrcTusks.BrokenFlag(side)}");
                Entry($"gen_race_tusks_half_broken_{side}", $"broken_{side}", 0.5f, 490, half, $"has_character_flag = {OrcTusks.BrokenFlag(side)}");
            }

            foreach (string variant in OrcTusks.Inherited.Where(v => v != "lower"))
            {
                string carries = $"has_gene = {{ category = {OrcTusks.Gene} template = {OrcTusks.TemplateOf(variant)} }}";
                Entry($"gen_race_tusks_{variant}", variant, 1f, 200, orc, carries);
                Entry($"gen_race_tusks_half_{variant}", variant, 0.5f, 190, half, carries);
            }

            Entry("gen_race_tusks_standard", "lower", 1f, 100, orc);
            Entry("gen_race_tusks_half_standard", "lower", 0.5f, 90, half);
        }
    }

    private static readonly string[] GrownStyles = ["ibex", "ram", "forward", "nubs"];
    private static int HornEntries => 5 + GrownStyles.Length;   // filed, young, each style, hornborn, dna, strip

    /// <summary>
    /// Who wears horns, and which. Its own group because horns are an ACCESSORY and stack with the
    /// shape and skin groups rather than competing with them — a Hornborn elf keeps the elf's ears
    /// and skin and adds horns. It also owns the skin mound (<c>gen_bs_horn_boss</c>), which is why
    /// that gene is not among the shape group's exclusive features: the shape entry of a Hornborn
    /// elf would otherwise flatten the mound under the elf's own horns.
    ///
    /// The horned people's horns come from their DNA — each culture's ethnicity carries a mix of
    /// styles and families inherit theirs — so for them the usual answer is "leave the accessory
    /// alone, make sure the mound is up". The other entries cover everything DNA cannot:
    /// <list type="number">
    /// <item><b>Filed</b> (500): the stump, for five years after gen_file_horns_decision.</item>
    /// <item><b>Young</b> (400): nubs under twelve, whatever the DNA says; horns grow in.</item>
    /// <item><b>By flag</b> (200): the <c>gen_horn_style_*</c> flag script gave them. Every Hornborn
    /// gets one; a horned child gets one at birth only when its genome may lack horns (a flagged or
    /// non-horned parent) — see gen_assign_horn_style_effect.</item>
    /// <item><b>Hornborn</b> (150): a Hornborn with no flag yet (engine-made, before the yearly
    /// pulse) — ibex. Never their DNA, which usually carries no horns.</item>
    /// <item><b>DNA</b> (100): the horned people with no flag — mound only, horns as inherited.</item>
    /// <item><b>Strip</b> (50): everyone else — no horns, no mound, whatever a horned ancestor left
    /// in their genome. Harmless on the vast majority, who carry none.</item>
    /// </list>
    /// **No <c>has_gene</c>.** The first version asked "does the DNA carry horns?" with
    /// <c>has_gene = { category = gen_horns template = gen_horns_none }</c>. Vanilla only ever uses
    /// has_gene on MORPH genes (gene_hair_type, gene_baldness), and in game a Hornborn rendered with
    /// the mound and no horns (2026-09-28): the test never fired, so the mound-only DNA entry won.
    /// Horns are now forced from flags wherever the DNA cannot be trusted.
    /// </summary>
    private static void HornGroup(JominiBuilder b)
    {
        b.Blank();
        using (b.Block("gen_race_horns"))
        {
            b.Blank();
            b.Field("usage", "game");
            b.Field("selection_behavior", "max");
            b.Field("priority", "92");
            b.Blank();

            string owed = $"OR = {{ has_trait = {HornedTrait} has_trait = {HornbornTrait} }}";

            HornEntry(b, "gen_race_horns_filed", Horns.Filed, true, 500, owed, $"has_character_flag = {FiledFlag}");
            HornEntry(b, "gen_race_horns_young", "nubs", true, 400, owed, "age < 12");
            foreach (string style in GrownStyles)
                HornEntry(b, $"gen_race_horns_{style}", style, true, 200, owed, $"has_character_flag = {StyleFlag(style)}");
            HornEntry(b, "gen_race_horns_hornborn", "ibex", true, 150,
                $"has_trait = {HornbornTrait}", $"NOT = {{ has_trait = {HornedTrait} }}");
            HornEntry(b, "gen_race_horns_dna", null, true, 100, $"has_trait = {HornedTrait}");
            HornEntry(b, "gen_race_horns_strip", Horns.NoneTemplate, false, 50,
                $"NOT = {{ has_trait = {HornedTrait} }}", $"NOT = {{ has_trait = {HornbornTrait} }}");
        }
    }

    /// <summary>
    /// One horn entry. <paramref name="style"/> is a style name, the none template, or null to leave
    /// the accessory as the DNA has it; <paramref name="mound"/> raises or flattens the skin mound.
    /// </summary>
    /// <summary>
    /// Horn ornaments (<see cref="Horns.Ornament"/>): each horned culture has one tradition — rings,
    /// a band or caps, in gold, bronze or silver, picked from the seed and the culture key — worn by
    /// its ranked men and women: landed at county tier or above, or the primary spouse of someone
    /// who is. Adults only; never on filed horns (filing is hiding them). Hornborn of a horned
    /// culture wear their culture's; horned folk of any other culture have no tradition to wear.
    /// Which horn the ornament sits on is not decided here — the ornament accessory reads the horn's
    /// style tag (see RaceHeadWriter.WriteHorns), since a style from DNA is invisible to these rules.
    /// Returns the entry count.
    /// </summary>
    private static int HornOrnamentGroup(JominiBuilder b, int seed, EthnicityMap ethnicities)
    {
        var cultures = ethnicities.ByCultureKey
            .Where(kv => kv.Value.Archetype == RaceArchetype.Hornkin)
            .Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();

        b.Blank();
        using (b.Block("gen_race_horn_ornaments"))
        {
            b.Blank();
            b.Field("usage", "game");
            b.Field("selection_behavior", "max");
            b.Field("priority", "92");
            b.Blank();

            foreach (string culture in cultures)
            {
                var rng = Core.Rng.For(seed, 0x40A7, Core.Rng.StableHash(culture));
                string kind = Horns.OrnamentKinds[rng.Int(0, Horns.OrnamentKinds.Length - 1)];
                string metal = Horns.Metals[rng.Int(0, Horns.Metals.Length - 1)].Name;

                using (b.Block($"gen_race_horn_ornament_{culture}"))
                {
                    using (b.Block("dna_modifiers"))
                    using (b.Block("accessory"))
                    {
                        b.Field("mode", "replace");
                        b.Field("gene", Horns.OrnamentGene);
                        b.Field("template", Horns.OrnamentTemplateOf(kind, metal));
                        b.Field("value", "0.5");
                    }

                    Weight(b, $"culture = culture:{culture}", weight: 100,
                        $"OR = {{ has_trait = {HornedTrait} has_trait = {HornbornTrait} }}",
                        "age >= 16",
                        $"NOT = {{ has_character_flag = {FiledFlag} }}",
                        "OR = { highest_held_title_tier >= tier_county primary_spouse ?= { highest_held_title_tier >= tier_county } }");
                }

                b.Blank();
            }
        }

        return cultures.Count;
    }

    private static void HornEntry(JominiBuilder b, string name, string? style, bool mound, int weight, params string[] conditions)
    {
        using (b.Block(name))
        {
            using (b.Block("dna_modifiers"))
            {
                if (style is not null)
                    using (b.Block("accessory"))
                    {
                        b.Field("mode", "replace");
                        b.Field("gene", Horns.Gene);
                        b.Field("template", style == Horns.NoneTemplate ? style : Horns.TemplateOf(style));
                        b.Field("value", "0.5");
                    }

                Morph(b, Horns.BossGene, mound ? Horns.BossTemplate : Horns.BossNoneTemplate, mound ? 1f : 0f, mound ? 1f : 0f);
            }

            using (b.Block("weight"))
            {
                b.Field("base", "0");
                using (b.Block("modifier"))
                {
                    b.Field("add", weight);
                    b.Field("exists", "this");
                    foreach (string c in conditions) b.Token(c);
                }
            }
        }

        b.Blank();
    }

    /// <summary>
    /// Emits <c>common/scripted_triggers/99_gen_race_ethnicity_triggers.txt</c>: one
    /// <c>gen_is_&lt;race&gt;_ethnicity_trigger</c> per fantasy race, true when the character's
    /// ethnicity is that race's base or any of its variants. The Fantasy script set's per-character
    /// race probe (gen_has_fantasy_race_ethnicity_trigger) is built on these.
    ///
    /// **Ethnicity, not genes, because `has_gene` is an interface trigger.** The probe used to read the
    /// <c>gen_race_skin</c> template; ck3-tiger accepts that, but the game refuses it at load
    /// ("Reading an interface trigger 'has_gene' in forbidden area") and it answers no forever. So
    /// every minority-race member stayed phenotype_human with their race's skin in their DNA — seen
    /// in game as a lowborn human with dusk elf violet skin. <c>has_ethnicity</c> is script-legal, and
    /// an engine-generated character's ethnicity is exactly the roll that gave them their genome.
    ///
    /// Generated because ethnicity keys are, and BaseFilesToCopy may not name them. All seven triggers
    /// are always written (<c>always = no</c> for an absent race) so the static callers always resolve.
    /// </summary>
    private static void WriteEthnicityTriggers(string modDir, EthnicityMap? ethnicities)
    {
        string dir = Path.Combine(modDir, "common", "scripted_triggers");
        Directory.CreateDirectory(dir);
        var b = new JominiBuilder();
        b.Comment("Generated: which ethnicities belong to each fantasy race. See Emit/RaceMorphWriter.cs.");
        b.Blank();

        foreach (var (archetype, _) in Races)
        {
            var keys = ethnicities is null
                ? []
                : ethnicities.Ethnicities.Values
                    .Where(e => e.Archetype == archetype)
                    .OrderBy(e => e.Key, StringComparer.Ordinal)
                    .SelectMany(e => e.Variants.Select(v => v.Key).Prepend(e.Key))
                    .ToList();

            using (b.Block($"{EthnicityTriggerOf(archetype)}"))
            {
                if (keys.Count == 0)
                    b.Field("always", "no");
                else
                    using (b.Block("OR"))
                        foreach (string key in keys)
                            b.Field("has_ethnicity", key);
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "99_gen_race_ethnicity_triggers.txt"), b.ToString());
    }

    /// <summary><c>gen_is_deepkin_ethnicity_trigger</c> and so on, named after the skin template.</summary>
    private static string EthnicityTriggerOf(RaceArchetype archetype)
        => $"gen_is_{RaceSkin.TemplateOf(archetype)!["gen_skin_".Length..]}_ethnicity_trigger";

    /// <summary>
    /// One shape entry: every gene the archetype forces, scaled by the world's morph intensity.
    /// </summary>
    private static void ShapeEntry(
        JominiBuilder b, string name, string condition, IReadOnlyList<RaceMorph> morphs, float intensity)
    {
        using (b.Block(name))
        {
            using (b.Block("dna_modifiers"))
            {
                ForcedMorphs(b, morphs.Where(m => !OwnGroupGenes.Contains(m.Gene)).ToList(), intensity);

                // Another race's head feature is switched OFF. The ear and tusk genes are inheritable
                // like any other, so without this an elf child of an orc parent keeps the tusks it
                // inherited, and an orc child of an elf the ears.
                foreach (var (gene, none) in ExclusiveFeatures)
                    if (morphs.All(m => m.Gene != gene))
                        Morph(b, gene, none, 0f, 0f);
            }

            Weight(b, condition, weight: 100);
        }

        b.Blank();
    }

    /// <summary>Each morph forced, its tiered rows scaled toward the gene's neutral by the world's intensity.</summary>
    private static void ForcedMorphs(JominiBuilder b, IReadOnlyList<RaceMorph> morphs, float intensity)
    {
        foreach (var m in morphs)
        {
            float scale = m.Tiered ? intensity : 1.0f;
            float n = Ethnicities.NeutralOf(m.Gene);
            float lo = Math.Clamp(n + (m.Min - n) * scale, 0f, 1f);
            float hi = Math.Clamp(n + (m.Max - n) * scale, 0f, 1f);
            Morph(b, m.Gene, m.Template, lo, hi);
        }
    }

    /// <summary>The race's row for one head-feature gene; every race that has the feature has one.</summary>
    private static RaceMorph FeatureOf(RaceArchetype archetype, string gene)
        => RaceMorphs.Of(archetype).First(m => m.Gene == gene);

    /// <summary>
    /// A half-race entry: <paramref name="feature"/> at half the full race's tier-scaled strength,
    /// and every other race's head feature switched off.
    /// </summary>
    private static void HalfEntry(JominiBuilder b, string name, string condition, RaceMorph feature, float intensity)
    {
        using (b.Block(name))
        {
            using (b.Block("dna_modifiers"))
            {
                if (!OwnGroupGenes.Contains(feature.Gene))
                {
                    var (lo, hi) = Range(feature, intensity);
                    Morph(b, feature.Gene, feature.Template, lo * 0.5f, hi * 0.5f);
                }

                foreach (var (gene, none) in ExclusiveFeatures)
                    if (gene != feature.Gene)
                        Morph(b, gene, none, 0f, 0f);
            }

            Weight(b, condition, weight: 90);
        }

        b.Blank();
    }

    /// <summary>One skin entry: a single forced <c>gen_race_skin</c> shift.</summary>
    private static void SkinEntry(
        JominiBuilder b, string name, string condition, string template,
        float lo, float hi, int weight)
    {
        using (b.Block(name))
        {
            using (b.Block("dna_modifiers")) Morph(b, "gen_race_skin", template, lo, hi);
            Weight(b, condition, weight);
        }

        b.Blank();
    }

    /// <summary>
    /// The morph line. Two spaces between the segments rather than one, which is how vanilla's own
    /// gene files are written and is worth matching so a diff against them stays readable.
    /// </summary>
    private static void Morph(JominiBuilder b, string gene, string template, float lo, float hi)
        => b.Inline("morph",
            $"mode = replace  gene = {gene}  template = {template}  range = {{ {F(lo)} {F(hi)} }}");

    private static void Weight(JominiBuilder b, string condition, int weight, params string[] also)
    {
        using (b.Block("weight"))
        {
            b.Field("base", "0");

            using (b.Block("modifier"))
            {
                b.Field("add", weight);

                // exists guards the trait check: weights are evaluated for portraits with no character
                // behind them, and has_trait on nothing is an error rather than a no.
                b.Field("exists", "this");
                b.Token(condition);
                foreach (string c in also) b.Token(c);
            }
        }
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
