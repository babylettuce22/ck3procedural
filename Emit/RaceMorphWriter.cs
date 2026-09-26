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
/// wilderness hider at 9999.
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
        (RaceArchetype.Deepkin, "phenotype_dusk_adapted"),
    ];

    public static void WriteAll(string modDir, MapConfig cfg, EthnicityMap ethnicities)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "portrait_modifiers");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "99_gen_race_morphs.txt");

        // Written even when there is nothing to write, because a mod directory is reused between
        // runs: turning fantasy off must retire the previous run's file rather than leave it
        // enforcing races nobody carries.
        if (!cfg.EnableFantasyEthnicities || cfg.RaceMode == FantasyRaceMode.HumanOnly)
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
            // Keyed on the gen_phenotype_human FLAG, deliberately NOT the phenotype_human TRAIT. The
            // trait is broad racial identity and sits on every member of a human culture; the flag is
            // set only for humans of a mixed line (see 00_phenotype_birth_effects.txt), which is
            // exactly the population whose inherited shift needs snapping off.
            //
            // Minority-race members used to be the reason for the split — they held the human trait
            // while their looks came from rolled ethnicity genes, so keying the reset on the trait
            // erased them. They now hold their own race's trait instead (resolved from their genes by
            // gen_reconcile_phenotype_with_genes_effect), so they are no longer the argument. The split
            // still is the right one: a mixed-line human is precisely "human whose inherited shift must
            // go", and no trait describes that.
            SkinEntry(b, "gen_race_skin_human", "has_character_flag = gen_phenotype_human",
                "gen_skin_human", 0f, 0f, weight: 100);

            // The seven fantasy traits and the mixed-line flag are the whole roster; a character with
            // none of them (a traited-but-unmixed human, or a pre-pulse engine character) has no entry
            // fire and keeps its inherited appearance — phenotype_human deliberately forces nothing,
            // human looks belong to the ethnicity.
        }

        ParadoxText.WriteBom(path, b.ToString());
        Console.WriteLine($"  race morphs written: {Races.Length} shape and {Races.Length + 1} skin enforcement entries to 99_gen_race_morphs.txt");
    }

    /// <summary>
    /// One shape entry: every gene the archetype forces, scaled by the world's morph intensity.
    /// </summary>
    private static void ShapeEntry(
        JominiBuilder b, string name, string condition, IReadOnlyList<RaceMorph> morphs, float intensity)
    {
        using (b.Block(name))
        {
            using (b.Block("dna_modifiers"))
                foreach (var m in morphs)
                {
                    float scale = m.Tiered ? intensity : 1.0f;
                    float n = Ethnicities.NeutralOf(m.Gene);
                    float lo = Math.Clamp(n + (m.Min - n) * scale, 0f, 1f);
                    float hi = Math.Clamp(n + (m.Max - n) * scale, 0f, 1f);
                    Morph(b, m.Gene, m.Template, lo, hi);
                }

            Weight(b, condition, weight: 100);
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

    private static void Weight(JominiBuilder b, string condition, int weight)
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

            }
        }
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
