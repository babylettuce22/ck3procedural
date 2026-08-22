using System.Globalization;
using System.Text;
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
/// It has to be a generated file rather than a BaseFilesToCopy static for two reasons. The ranges
/// are fantasy-tier-scaled, and a static file cannot know whether this map is LowFantasy or
/// ExoticSurreal; and the elf skin split below needs to name generated culture keys, which a
/// static file may not do.
///
/// **Two groups, because a portrait modifier group applies exactly one of its entries.** Shape and
/// skin are separate axes that must both land, so they are separate groups. Priorities 90 and 91
/// sit above every stock group (highest is 50) and below the mod's own beard group at 100 and the
/// wilderness hider at 9999.
/// </summary>
public static class RaceMorphWriter
{
    private static readonly (RaceArchetype Archetype, string Trait)[] Races =
    [
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
        var sb = new StringBuilder();
        sb.Append("# Generated: race-defining genes forced by phenotype trait. See Emit/RaceMorphWriter.cs.\n");
        sb.Append("# The values mirror MapGen/Ethnicities.cs RaceMorphs — one table feeds both.\n\n");

        // ---- Group 1: shape --------------------------------------------------------------
        sb.Append("gen_race_morphs = {\n\n\tusage = game\n\tselection_behavior = max\n\tpriority = 90\n\n");

        foreach (var (archetype, trait) in Races)
            AppendShapeEntry(sb, $"gen_race_morph_{archetype.ToString().ToLowerInvariant()}",
                trait, RaceMorphs.Of(archetype), f);

        // Both elf archetypes share phenotype_gracile, so their entry is the merge of the two
        // tables: genes where both agree on the template, ranges averaged. Genes where the
        // templates differ (body_shape: rectangle vs triangle) are left to inherit — musculature
        // blends harmlessly, and forcing either template onto the other elf would be wrong.
        AppendShapeEntry(sb, "gen_race_morph_gracile", "phenotype_gracile",
            MergeGracile(), f);

        sb.Append("}\n\n");

        // ---- Group 2: skin ---------------------------------------------------------------
        // Separate group so it stacks with the shape entry (one entry per group applies).
        sb.Append("gen_race_skins = {\n\n\tusage = game\n\tselection_behavior = max\n\tpriority = 91\n\n");

        foreach (var (archetype, trait) in Races)
            AppendSkinEntry(sb, $"gen_race_skin_{archetype.ToString().ToLowerInvariant()}",
                trait, RaceSkin.TemplateOf(archetype)!, skinLo, skinHi, weight: 100, cultureKeys: null);

        // phenotype_gracile covers two skins. The culture decides which: wood-elf cultures get the
        // olive shift at weight 100, everyone else carrying the trait falls to the high-elf entry
        // at 90. `selection_behavior = max` picks the highest applicable. The culture keys are the
        // generated ones — the second reason this file cannot be static.
        var woodElfCultures = ethnicities.ByCulture
            .Where(kv => kv.Value.Archetype == RaceArchetype.WoodElf)
            .Select(kv => kv.Key.Key)
            .Distinct()
            .ToList();

        if (woodElfCultures.Count > 0)
            AppendSkinEntry(sb, "gen_race_skin_wood_elf", "phenotype_gracile",
                RaceSkin.TemplateOf(RaceArchetype.WoodElf)!, skinLo, skinHi, weight: 100, cultureKeys: woodElfCultures);

        AppendSkinEntry(sb, "gen_race_skin_high_elf", "phenotype_gracile",
            RaceSkin.TemplateOf(RaceArchetype.HighElf)!, skinLo, skinHi, weight: 90, cultureKeys: null);

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
        sb.Append("\tgen_race_skin_human = {\n\t\tdna_modifiers = {\n");
        sb.Append("\t\t\tmorph = { mode = replace  gene = gen_race_skin  template = gen_skin_human  range = { 0 0 } }\n");
        sb.Append("\t\t}\n");
        sb.Append("\t\tweight = {\n\t\t\tbase = 0\n\t\t\tmodifier = {\n\t\t\t\tadd = 100\n\t\t\t\texists = this\n\t\t\t\thas_character_flag = gen_phenotype_human\n\t\t\t}\n\t\t}\n\t}\n\n");

        // The six fantasy traits and the mixed-line flag are the whole roster; a character with
        // none of them (a traited-but-unmixed human, or a pre-pulse engine character) has no entry
        // fire and keeps its inherited appearance — phenotype_human
        // deliberately forces nothing, human looks belong to the ethnicity. Exotic maps to no
        // trait — its shape is rolled per people rather than authored — so it has no entry either.
        sb.Append("}\n");

        ParadoxText.WriteBom(path, sb.ToString());
        Console.WriteLine($"  race morphs written: {Races.Length + 1} shape and {Races.Length + 2} skin enforcement entries to 99_gen_race_morphs.txt");
    }

    private static void AppendShapeEntry(
        StringBuilder sb, string name, string trait, IReadOnlyList<RaceMorph> morphs, float intensity)
    {
        sb.Append($"\t{name} = {{\n\t\tdna_modifiers = {{\n");
        foreach (var m in morphs)
        {
            float scale = m.Tiered ? intensity : 1.0f;
            float n = Ethnicities.NeutralOf(m.Gene);
            float lo = Math.Clamp(n + (m.Min - n) * scale, 0f, 1f);
            float hi = Math.Clamp(n + (m.Max - n) * scale, 0f, 1f);
            sb.Append($"\t\t\tmorph = {{ mode = replace  gene = {m.Gene}  template = {m.Template}  range = {{ {F(lo)} {F(hi)} }} }}\n");
        }
        sb.Append("\t\t}\n");
        AppendWeight(sb, trait, weight: 100, cultureKeys: null);
        sb.Append("\t}\n\n");
    }

    private static void AppendSkinEntry(
        StringBuilder sb, string name, string trait, string template,
        float lo, float hi, int weight, List<string>? cultureKeys)
    {
        sb.Append($"\t{name} = {{\n\t\tdna_modifiers = {{\n");
        sb.Append($"\t\t\tmorph = {{ mode = replace  gene = gen_race_skin  template = {template}  range = {{ {F(lo)} {F(hi)} }} }}\n");
        sb.Append("\t\t}\n");
        AppendWeight(sb, trait, weight, cultureKeys);
        sb.Append("\t}\n\n");
    }

    private static void AppendWeight(StringBuilder sb, string trait, int weight, List<string>? cultureKeys)
    {
        sb.Append("\t\tweight = {\n\t\t\tbase = 0\n\t\t\tmodifier = {\n");
        sb.Append($"\t\t\t\tadd = {weight}\n");
        // exists guards the trait check: weights are evaluated for portraits with no character
        // behind them, and has_trait on nothing is an error rather than a no.
        sb.Append("\t\t\t\texists = this\n");
        sb.Append($"\t\t\t\thas_trait = {trait}\n");
        if (cultureKeys is { Count: > 0 })
        {
            sb.Append("\t\t\t\tOR = {\n");
            foreach (var key in cultureKeys)
                sb.Append($"\t\t\t\t\tculture = culture:{key}\n");
            sb.Append("\t\t\t\t}\n");
        }
        sb.Append("\t\t\t}\n\t\t}\n");
    }

    /// <summary>
    /// The gracile entry: the intersection of the two elf tables. Same gene and same template on
    /// both sides merges with averaged ranges; a template disagreement drops the gene, leaving it
    /// to ordinary inheritance.
    /// </summary>
    private static List<RaceMorph> MergeGracile()
    {
        var high = RaceMorphs.Of(RaceArchetype.HighElf);
        var wood = RaceMorphs.Of(RaceArchetype.WoodElf).ToDictionary(m => m.Gene);

        var merged = new List<RaceMorph>();
        foreach (var h in high)
        {
            if (!wood.TryGetValue(h.Gene, out var w) || w.Template != h.Template) continue;
            merged.Add(h with { Min = (h.Min + w.Min) / 2f, Max = (h.Max + w.Max) / 2f });
        }
        return merged;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
