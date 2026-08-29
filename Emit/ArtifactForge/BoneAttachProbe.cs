namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using System.IO;

/// <summary>
/// A controlled experiment for hanging a rigid piece off a portrait bone, so that garnishing
/// "extra special" armour with pauldrons and the like can be judged before anything is modelled.
///
/// **What is already established, from vanilla rather than from guessing.**
///
/// * An accessory may declare <c>node = "&lt;bone&gt;"</c> to parent a RIGID entity to a skeleton
///   bone instead of skinning it. 285 vanilla accessories do — almost all <c>bn_r_prop</c> and
///   <c>bn_l_prop</c>, the hands.
/// * It is **not** limited to hands. <c>prophet_shield</c> hangs off <c>bn_h_head_mid</c>, and
///   vanilla's own <c>accessories/torso.txt</c> opens with a commented example attaching a crown
///   entity to <c>node = "R_shoulder"</c> — Paradox documenting this exact use.
/// * It needs **no animation hook**. The group that applies <c>prophet_shield</c>,
///   <c>special_prophet</c> in <c>99_special.txt</c>, is <c>usage = game</c>, so it is evaluated on
///   every portrait. Only the weapon path is animation-bound, and that is because
///   <c>00_animation_props.txt</c> is <c>usage = none</c> — nothing to do with the gene.
/// * The portrait rig carries <c>bn_l_shoulder</c>, <c>bn_r_shoulder</c>, <c>bn_l_clavicle</c> and
///   <c>bn_r_clavicle</c>, so the attachment points a pauldron wants exist.
///
/// **What is NOT established, and is what this probe is for.** There are only **16** accessory
/// genes in the whole game — <c>clothes</c>, <c>cloaks</c>, <c>props_left</c>, <c>props_right</c>,
/// <c>headgear</c>, <c>additive_headgear</c> and ten others. Every other name that looks like a gene
/// is a TEMPLATE inside one of those. So a pauldron either shares a gene with something else, or we
/// declare a new one — and whether a new accessory gene declared in our own file is merged and
/// rendered is an assumption nobody here has tested.
///
/// **The experiment is a 2x2, deliberately.** Each cell is the same prop, so anything that differs
/// is the cell and not the art:
///
/// | | in a NEW gene | in vanilla's <c>props_left</c> |
/// |---|---|---|
/// | on <c>bn_r_prop</c> (known-good bone) | does a new gene render at all? | **control** |
/// | on <c>bn_r_shoulder</c> | the thing we actually want | does the bone work? |
///
/// The control cell must render. If it does not, nothing else in the run can be believed — the
/// fault is in the modifier, the flag or the trigger, not in genes or bones. If the control renders
/// and the new-gene cells do not, a new gene is not viable and pauldrons must share
/// <c>props_left</c>. If the new gene renders on <c>bn_r_prop</c> but not on the shoulder, the bone
/// is the problem.
///
/// The remaining bones are offered alongside so one session also answers where each one actually
/// puts a piece, and at what orientation. The prop is a DAGGER on purpose: it is small enough not to
/// swamp the portrait and elongated enough that its rotation is readable at a glance, which a blob
/// would not be.
///
/// Gated by <see cref="ArtifactForgeFlags.BoneAttachProbe"/> and emitted nowhere near the artifact
/// path, so it can be deleted or switched off without touching anything that ships.
/// </summary>
public static class BoneAttachProbe
{
    /// <summary>A vanilla prop entity, so the probe depends on no asset of ours.</summary>
    private const string Prop = "portrait_prop_western_dagger_01_entity";

    /// <summary>Our own gene, declared in our own file — one of the things under test.</summary>
    private const string NewGene = "gen_armor_props";
    private const string NewTemplate = "gen_armor_props_probe";

    /// <summary>
    /// Vanilla's left-hand prop gene, used as the known-good comparison.
    ///
    /// Left rather than right because the forged-weapon path already puts things on the right, and
    /// two accessories in one gene means one of them silently loses.
    /// </summary>
    private const string KnownGene = "props_left";
    private const string KnownTemplate = "gen_armor_props_probe_known";

    /// <summary>
    /// The cells. Each becomes one accessory, one modifier entry and one debug option.
    ///
    /// <c>bn_r_prop</c> appears in both genes on purpose — it is the control, and having it in the
    /// new gene too is what separates "the gene did not work" from "the bone did not work".
    /// </summary>
    private static readonly (string Bone, bool NewGene, string Note)[] Cells =
    [
        ("bn_r_prop",     false, "CONTROL - vanilla gene, vanilla bone. This one must appear."),
        ("bn_r_prop",     true,  "New gene, known-good bone: does a gene of ours render at all?"),
        ("bn_r_shoulder", false, "Known-good gene, the bone we want: does the shoulder work?"),
        ("bn_r_shoulder", true,  "Both new: the combination a pauldron would actually use."),
        ("bn_l_shoulder", true,  "The other shoulder, for a matched pair."),
        ("bn_r_clavicle", true,  "Higher and further in than the shoulder."),
        ("bn_l_clavicle", true,  "The other clavicle."),
        ("bn_h_head_mid", true,  "Where prophet_shield hangs - a known-good NON-hand bone."),
    ];

    private static string Name(string bone, bool newGene) =>
        $"pmg_probe_{bone}_{(newGene ? "new" : "van")}";

    /// <summary>Vanilla's file that declares <see cref="KnownGene"/>, for the splice.</summary>
    private const string KnownGeneFile = "07_genes_special_accessories_misc.txt";

    /// <summary>Emits the probe, or nothing at all when the flag is off.</summary>
    public static int WriteAll(string modDir, string gameDir)
    {
        if (!ArtifactForgeFlags.BoneAttachProbe) return 0;

        WriteAccessories(modDir);
        WriteGene(modDir);

        // The control cells live in a VANILLA gene, and an accessory must be a member of the
        // template it cites - so props_left needs a template of ours, and it can only get one by
        // splicing into a copy of vanilla's file. Declaring props_left again in a file of our own
        // would REPLACE it and take every vanilla prop with it, which is the failure the whole
        // splice mechanism exists to avoid.
        if (!WriteKnownGeneTemplate(modDir, gameDir)) return 0;

        WriteModifiers(modDir);
        WriteEvent(modDir);

        Console.WriteLine($"  bone probe: {Cells.Length} cell(s) over "
            + $"{Cells.Select(c => c.Bone).Distinct().Count()} bone(s) x 2 gene(s) - "
            + "raise with the Debug: Bone Attach Probe event");

        return Cells.Length;
    }

    private static void WriteAccessories(string modDir)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "accessories");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Bone attach probe: the same vanilla prop on several bones, in two genes.\n\n"
            + "`node` is what makes an accessory RIGID and parented to a bone rather than skinned to\n"
            + "the body. 285 vanilla accessories use it, and prophet_shield proves it is not limited\n"
            + "to hands.");

        foreach (var (bone, newGene, note) in Cells)
        {
            b.Blank();
            b.Comment(note);

            using (b.Block(Name(bone, newGene)))
                b.Inline("entity", "required_tags", "=", "\"\"", "node", "=", $"\"{bone}\"",
                    "entity", "=", $"\"{Prop}\"");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_bone_probe.txt"), b.ToString());
    }

    /// <summary>
    /// Declares our own accessory gene, in our own file.
    ///
    /// **This is the part most likely to fail, and it fails quietly.** Redeclaring an EXISTING gene
    /// in a separate file replaces it rather than extending it — that is why armour templates are
    /// spliced into a copy of vanilla's clothes file instead. A wholly NEW gene should merge one
    /// level up, at <c>accessory_genes</c>, but "should" is the reason this probe exists. If the new
    /// gene does not work, the fallback is to put pauldrons in <c>props_left</c> and give up the
    /// left-hand prop slot.
    /// </summary>
    private static void WriteGene(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "genes");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("A NEW accessory gene, declared in a file of our own.\n\n"
            + "Vanilla has 16 accessory genes and nothing here touches any of them, so this file\n"
            + "should merge rather than replace. If characters lose an existing accessory slot when\n"
            + "this is present, that assumption is wrong and this file is the culprit.");

        using (b.Block("accessory_genes"))
        using (b.Block(NewGene))
        {
            // THE EMPTY DEFAULT, and it is not optional.
            //
            // An accessory gene is part of every character's DNA: everyone carries a value for every
            // gene, and that value picks a TEMPLATE whether or not a portrait modifier has an
            // opinion. Without an empty template at index 0 the whole world wears the probe's
            // dagger, which is what happened - and it reads as the flag being ignored rather than as
            // the gene working exactly as designed. Vanilla ships `no_props`, `no_headgear`,
            // `no_clothes` for precisely this.
            using (b.Block($"{NewTemplate}_none"))
            {
                b.Field("index", 0);

                using (b.Block("male")) { }

                b.Field("female", "male");
                b.Field("boy", "male");
                b.Field("girl", "female");
            }

            using (b.Block(NewTemplate))
            {
                // Index unique WITHIN this gene only; 0 is the empty default above.
                b.Field("index", 1);

                using (b.Block("male"))
                    foreach (var (bone, newGene, _) in Cells.Where(c => c.NewGene))
                        b.Field("1", Name(bone, newGene));

                // Present on every vanilla template without exception; children fall back to the
                // adult list of their sex.
                b.Field("female", "male");
                b.Field("boy", "male");
                b.Field("girl", "female");
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_armor_props.txt"), b.ToString());
    }

    /// <summary>
    /// Adds our template to vanilla's <c>props_left</c>, so the control cells have somewhere legal
    /// to live.
    ///
    /// The engine enforces that an accessory belongs to the template a portrait modifier cites, and
    /// ck3-tiger does not check it — so getting this wrong shows up as the accessory silently not
    /// rendering, which is exactly the outcome this probe is trying to measure. A control that fails
    /// for a bookkeeping reason would be worse than no control at all.
    /// </summary>
    private static bool WriteKnownGeneTemplate(string modDir, string gameDir)
    {
        var block = new JominiBuilder(startDepth: 2);

        using (block.Block(KnownTemplate))
        {
            // Unique within props_left. Vanilla's templates there run from 0 upwards; this sits
            // clear of them and of room to grow.
            block.Field("index", 800);

            using (block.Block("male"))
                foreach (var (bone, newGene, _) in Cells.Where(c => !c.NewGene))
                    block.Field("1", Name(bone, newGene));

            block.Field("female", "male");
            block.Field("boy", "male");
            block.Field("girl", "female");
        }

        return GeneSplice.Write(gameDir, modDir, KnownGeneFile, KnownGene,
            block.ToString().TrimEnd('\n').Split('\n'),
            "Added by Ck3MapGen: the bone-attach probe's CONTROL accessories.\n"
            + "They sit in a vanilla gene on a vanilla bone, so that a cell which fails\n"
            + "distinguishes a bad gene or bone from a broken harness.");
    }

    /// <summary>
    /// One modifier entry per cell, each raised by its own character flag.
    ///
    /// <c>usage = game</c> and not an animation pack, which is the whole point: the group is
    /// evaluated on every portrait, so a piece hung here needs no pose and no idle hook. That is how
    /// <c>special_prophet</c> shows the prophet's halo.
    ///
    /// <c>selection_behavior = max</c> means ONE entry of this group applies, so the flags are
    /// mutually exclusive by construction and two cells can never be confused for one another.
    /// </summary>
    private static void WriteModifiers(string modDir)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "portrait_modifiers");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Bone attach probe. One entry per cell, each gated on its own flag.\n\n"
            + "Priority 90 so nothing in the ordinary ladder can hide a result and send the\n"
            + "investigation somewhere it does not belong.");

        b.Blank();

        using (b.Block("gen_bone_probe"))
        {
            b.Field("usage", "game");
            b.Field("selection_behavior", "max");
            b.Field("priority", 90);

            foreach (var (bone, newGene, note) in Cells)
            {
                string name = Name(bone, newGene);

                b.Blank();
                b.Comment(note);

                using (b.Block(name))
                {
                    using (b.Block("dna_modifiers"))
                    using (b.Block("accessory"))
                    {
                        b.Field("mode", "add");
                        b.Field("gene", newGene ? NewGene : KnownGene);
                        b.Field("template", newGene ? NewTemplate : KnownTemplate);
                        b.Field("accessory", name);
                    }

                    using (b.Block("weight"))
                    {
                        b.Field("base", 0);

                        using (b.Block("modifier"))
                        {
                            b.Field("add", 1000);
                            b.Field("has_character_flag", name);
                        }
                    }
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_bone_probe.txt"), b.ToString());
    }

    /// <summary>
    /// The event that raises one cell at a time.
    ///
    /// Every option clears every flag before setting its own, so a session cannot drift into showing
    /// two pieces and reading the result as one badly placed one.
    /// </summary>
    private static void WriteEvent(string modDir)
    {
        string dir = Path.Combine(modDir, "events");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Raw("namespace = pmg_bone_probe\n\n");
        b.Comment("Bone attach probe. Raise with:  event pmg_bone_probe.0001\n\n"
            + "Read the CONTROL first - vanilla gene, vanilla bone. If that shows nothing, the\n"
            + "fault is in this harness and no other cell means anything.");
        b.Blank();

        b.Raw("pmg_bone_probe.0001 = {\n");
        b.Raw("\ttype = character_event\n");
        b.Raw("\ttitle = pmg_bone_probe.0001.t\n");
        b.Raw("\tdesc = pmg_bone_probe.0001.desc\n");
        b.Raw("\ttheme = realm\n\n");
        b.Raw("\tleft_portrait = { character = root  animation = personality_bold }\n\n");

        for (int i = 0; i < Cells.Length; i++)
        {
            var (bone, newGene, note) = Cells[i];

            b.Raw($"\t# {note}\n");
            b.Raw("\toption = {\n");
            b.Raw($"\t\tname = pmg_bone_probe.0001.{(char)('a' + i)}\n");

            foreach (var (other, otherNew, _) in Cells)
                b.Raw($"\t\tremove_character_flag = {Name(other, otherNew)}\n");

            b.Raw($"\t\tadd_character_flag = {Name(bone, newGene)}\n");
            b.Raw("\t\ttrigger_event = { id = pmg_bone_probe.0001 days = 0 }\n");
            b.Raw("\t}\n\n");
        }

        b.Raw("\toption = {\n");
        b.Raw($"\t\tname = pmg_bone_probe.0001.{(char)('a' + Cells.Length)}\n");

        foreach (var (other, otherNew, _) in Cells)
            b.Raw($"\t\tremove_character_flag = {Name(other, otherNew)}\n");

        b.Raw("\t}\n}\n");

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_bone_probe_events.txt"), b.ToString());

        WriteLoc(modDir);
    }

    private static void WriteLoc(string modDir)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var loc = new LocFile();
        loc.Add("pmg_bone_probe.0001.t", "Debug: Bone Attach Probe");
        loc.Add("pmg_bone_probe.0001.desc",
            "Hangs one vanilla dagger off one bone, so the attachment route for pauldrons and other "
            + "garnish can be judged before anything is modelled.\\n\\nThe dagger is elongated on "
            + "purpose: you are reading its POSITION and its ROTATION, and a blob would only tell you "
            + "the first.\\n\\nTake the CONTROL first. It uses a vanilla gene and a vanilla bone, so "
            + "it must appear; if it does not, this harness is broken and no other option means "
            + "anything. After that, a cell that shows nothing tells you which of the two new things "
            + "- our own accessory gene, or the bone - is the one that does not work.");

        for (int i = 0; i < Cells.Length; i++)
        {
            var (bone, newGene, _) = Cells[i];

            loc.Add($"pmg_bone_probe.0001.{(char)('a' + i)}",
                $"{bone} in {(newGene ? "our own gene" : "vanilla's props_left")}"
                + (i == 0 ? " - the control" : ""));
        }

        loc.Add($"pmg_bone_probe.0001.{(char)('a' + Cells.Length)}", "Clear them all");

        loc.Write(Path.Combine(dir, "zz_gen_bone_probe_l_english.yml"));
    }
}
