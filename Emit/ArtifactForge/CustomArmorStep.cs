namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using System.IO;

/// <summary>
/// Puts hand-modelled armour from <c>assets/armors/</c> onto a portrait, behind a debug flag, so a
/// custom mesh can be judged before anything depends on it.
///
/// **Why this is separate from <see cref="ArmorForgeStep"/>.** That one dresses artifacts, gated on
/// culture and type, and a custom piece dropped into it would appear on one culture-and-type pair
/// somewhere in a world and be effectively unfindable. One flag, one piece, one click instead.
///
/// **The modelling side owns the asset.** A piece that ships its own <c>.asset</c> has it copied
/// through rather than regenerated, because that file carries things the generator cannot infer:
/// blend-shape bindings, the body-morph attributes they answer to, and a deliberate shader choice.
/// Only pieces without one get an asset synthesised from their mesh.
///
/// **What the generator does own** is everything the export cannot know about the game: which bone
/// names the portrait rig uses, which gene slot a piece belongs in, and the accessory and template
/// plumbing that makes it selectable at all.
/// </summary>
public static class CustomArmorStep
{
    /// <summary>Where hand-modelled pieces live, relative to the assets folder.</summary>
    private const string SourceDir = "armors";

    /// <summary>Blend-shape meshes sit in this subfolder, both in the source and in the mod.</summary>
    private const string BlendDir = "blendshapes";

    /// <summary>
    /// A mesh whose name ends in this is a PARTS LIBRARY, not a garment: several shapes meant to be
    /// assembled rather than one piece to be worn.
    ///
    /// The same convention the weapon forge uses, and for the same reason — the alternative is a
    /// sidecar file that has to be kept in step with the assets folder by hand.
    /// </summary>
    private const string PartsSuffix = "_parts";

    /// <summary>Where the mesh and its entity go in the mod.</summary>
    private const string ModelDir = "gfx/models/artifacts/gen_armor_custom";

    /// <summary>The flag that wears whatever the first piece of each slot is.</summary>
    public const string WearFlag = "pmg_wear_custom_armor";

    /// <summary>
    /// Each piece also answers to a flag of its own, because pieces in the same slot EXCLUDE each
    /// other.
    ///
    /// A group with <c>selection_behavior = max</c> applies one entry, and a gene slot holds one
    /// accessory — so a full plate suit and a gambeson, both clothes, can never be worn together and
    /// the winner between them would be arbitrary. The shared flag stays for the common case; these
    /// let a specific piece be summoned for comparison.
    /// </summary>
    private static string FlagFor(Piece p) => $"pmg_wear_{p.Name}";

    /// <summary>
    /// What the game calls the portrait body's bones — one rig per sex, and they are NOT the same.
    ///
    /// Both carry the same 134 bones with identical suffixes, differing only in this prefix, so a
    /// male-rigged mesh cannot bind to a female body and vice versa. That is why a female piece has
    /// to be exported against the female rig rather than wired up from the male one: the geometry
    /// would be male-proportioned even if the names were forced to match.
    /// </summary>
    private const string MalePortraitRig = "male_body_skeleton_01:";
    private const string FemalePortraitRig = "female_body_skeleton_01:";

    /// <summary>
    /// Prefixes seen on exports so far, longest first so a longer one is stripped before a shorter
    /// one that is its prefix.
    ///
    /// io_pdx_mesh names bones after the Blender armature, and that name has differed on every piece
    /// received so far — <c>male_body_rig_01_ground_joint</c> on one, a bare <c>ground_joint</c> on
    /// the next. Rather than add a case each time, the suffix is taken and the correct prefix put
    /// back on unconditionally, which also makes the operation idempotent: a piece already exported
    /// with the right names passes through unchanged.
    /// </summary>
    private static readonly string[] KnownRigPrefixes =
        ["male_body_skeleton_01:", "male_body_rig_01_", "female_body_rig_01_"];

    /// <summary>
    /// Which gene slot a piece goes in, and the two that exist.
    ///
    /// <c>Clothes</c> REPLACES a character's outfit; <c>Cloaks</c> LAYERS over it. Both attach at
    /// <c>shared_pose_entity = torso</c>, so the difference is entirely which one the accessory is
    /// declared in.
    ///
    /// Putting a partial piece in the clothes slot is what produced the first custom-armour failure:
    /// a breastplate covering only the chest became the character's entire outfit, leaving the arms
    /// and belly bare, and the <c>shrink_arms</c> tag copied from vanilla war garments then thinned
    /// the uncovered arms into twigs. A full outfit in the cloak slot has the opposite problem — it
    /// would render over the clothes it was meant to replace.
    /// </summary>
    private enum Slot { Clothes, Cloaks }

    private static (string Gene, string File, string Template, int Index) Wiring(Slot slot) => slot switch
    {
        Slot.Clothes => ("clothes", "05_genes_special_accessories_clothes.txt", "gen_armor_custom_clothes", 901),
        _ => ("cloaks", "07_genes_special_accessories_misc.txt", "gen_armor_custom_cloaks", 900),
    };

    /// <summary>
    /// A piece that reaches this far down the body, as a fraction of its own top, counts as a full
    /// outfit rather than something worn over one.
    ///
    /// Measured rather than listed, because a list is one more thing to keep in step with the assets
    /// folder. The two pieces in hand separate by a wide margin: the gambeson spans y 0.0 to 145.3,
    /// so its base sits at 0% of its height, while the breastplate spans 108.3 to 146.7 and sits at
    /// 74%. Anything below half is reaching down the legs and is therefore an outfit.
    /// </summary>
    private const double OutfitReachesBelow = 0.5;

    /// <summary>
    /// Arm-region coverage above which a piece gets <c>shrink_arms</c>.
    ///
    /// The tag pulls the body's arms in so a sleeve fits over them without the arm poking through.
    /// Vanilla sets it on every war garment; we set it only where it is earned, because on a piece
    /// that does NOT enclose the arm it produces the twig-armed look the breastplate had.
    ///
    /// Measured as the share of vertices out past |x| = 22 in the shoulder-to-elbow band, which
    /// separates the cases with room to spare: a breastplate has 0%, the body-derived gambeson 22.7%
    /// - it hugs the arm, so nothing can poke through it - and the plate suit 42.9%, whose hard
    /// vambraces and gauntlets stand well off. The threshold sits in the gap.
    /// </summary>
    private const double ArmCoverageNeedingShrink = 0.30;

    /// <summary>
    /// Hand-region coverage above which a piece gets <c>shrink_hands</c>.
    ///
    /// Same bargain as the arms, one joint further out: the tag pulls the body's hands in so they do
    /// not poke out through a gauntlet. It is the ONLY lever there is — CK3 has no way to hide a
    /// hand. The complete set of body morphs is <c>shrink_{arms,belly,chest,feet,hands,legs}</c> plus
    /// <c>bs_body_no_portrait</c> (the whole figure) and <c>bs_body_no_left_leg</c>; there is no hand
    /// equivalent of the latter.
    ///
    /// And it is a SMALL lever. Measured against the base body, <c>male_bs_body_shrink_hands</c>
    /// moves hand vertices by a median of 0.68 and a maximum of 2.02 units. A gauntlet that stands
    /// further off than that still shows knuckles, so this tag is a finishing touch on geometry that
    /// already nearly fits — not a way to rescue one that does not.
    ///
    /// Measured as the share of vertices out past |x| = 44 at hand height, which separates as
    /// cleanly as the arm rule does: the breastplate scores 0%, the gambeson 8.8% (its hands were
    /// deleted, so what is left is wrist), and the plate suit 20.5% on the strength of its gauntlets.
    /// </summary>
    private const double HandCoverageNeedingShrink = 0.15;

    /// <summary>
    /// Coverage above which a piece gets the shrink tag for a CENTRAL region — chest, belly, legs.
    ///
    /// Lower than the arm and hand thresholds, and deliberately so. Those two guard against a false
    /// positive that is ugly and obvious: a tag on a piece that does not enclose the limb thins it in
    /// plain sight. The central regions carry no such risk, because a piece with any real presence
    /// over the ribs or the gut is by definition covering them — there is no "twig belly". So the bar
    /// only has to exclude a piece that merely passes through the band, such as a pauldron dipping
    /// into the chest range.
    ///
    /// Armour should occlude the body it covers; where it genuinely covers, the tag is earned.
    /// </summary>
    private const double TorsoCoverageNeedingShrink = 0.10;

    /// <summary>
    /// Coverage above which a piece gets <c>shrink_feet</c>.
    ///
    /// Lower again, because feet are a small share of any full harness — sabatons are the smallest
    /// pieces in a suit — so a threshold set at the torso's level would never be met by geometry that
    /// plainly does enclose the foot.
    /// </summary>
    private const double FootCoverageNeedingShrink = 0.05;

    private sealed record Piece(string Name, Slot Slot, bool Female, bool ShipsAsset, string Tags);

    /// <summary>
    /// Copies every piece it finds, rig-normalised, and wires it to a flag. Returns how many.
    /// Absent assets are not an error: a checkout without <c>assets/armors/</c> simply forges none.
    /// </summary>
    public static int WriteAll(string modDir, string gameDir)
    {
        // Temporary: portrait art is reserved for forged weapons. See ArtifactForgeFlags. Silent
        // where the forge above is loud - one line about armour being off is enough.
        if (!ArtifactForgeFlags.ArmorOnPortrait) return 0;

        string? dir = Locate();
        if (dir is null) return 0;

        string outDir = Path.Combine(modDir, ModelDir.Replace('/', Path.DirectorySeparatorChar));
        var found = new List<(string Name, bool FullBody, double Radius, string Tags,
            bool Female, bool ShipsAsset)>();

        foreach (string source in Directory.EnumerateFiles(dir, "*.mesh").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(source);
            if (string.IsNullOrEmpty(name)) continue;

            Directory.CreateDirectory(outDir);

            // A parts library is assembled into one garment first; a lone mesh is already one.
            string built = name.EndsWith(PartsSuffix, StringComparison.Ordinal)
                ? Assemble(source, outDir)
                : name;

            if (built.Length == 0) continue;

            string target = Path.Combine(outDir, built + ".mesh");

            // An assembled set is written by Assemble and then re-read here, so the rig rename and
            // the coverage measurement run on it exactly as they do on a hand-exported piece.
            if (CopyMesh(built == name ? source : target, target) is not { } shape) continue;

            found.Add((built, shape.FullBody, shape.Radius, shape.Tags,
                shape.Female, File.Exists(Path.Combine(dir, built + ".asset"))));
        }

        if (found.Count == 0) return 0;

        // ONE piece takes the clothes slot; everything else layers over it.
        //
        // The clothes slot replaces the outfit and holds a single accessory, so two full-body pieces
        // put there can never be worn together - which is what stopped a plate suit and a gambeson
        // appearing at once. The base is the piece that hugs the body most tightly, measured rather
        // than named: a garment worn UNDER armour is physically nearer the body than the armour is.
        // Here the gambeson averages 15.1 units from the axis against the plate suit's 19.7.
        var baseLayer = found.Where(f => f.FullBody)
            .OrderBy(f => f.Radius)
            .Select(f => f.Name)
            .FirstOrDefault();

        var pieces = found
            .Select(f => new Piece(f.Name,
                f.Name == baseLayer ? Slot.Clothes : Slot.Cloaks, f.Female, f.ShipsAsset,
                f.Name != baseLayer ? f.Tags : ""))
            .ToList();

        if (baseLayer is not null)
        {
            Console.WriteLine($"  custom armour: {baseLayer} is the base layer (tightest to the "
                + "body); everything else layers over it");
        }

        // A COMBINED piece, so a base and a suit can share one slot instead of competing for two.
        //
        // Wearing them as separate accessories works but spends both torso slots, which leaves
        // nothing for a cloak and nothing for the next layer. Merged, they are one garment in the
        // clothes slot - and because the merge batches by material, each half keeps its own texture.
        //
        // The separate pieces stay wearable on their own flags: this adds a combination rather than
        // replacing the parts it is made of.
        foreach (var suit in pieces.Where(x => x.Slot == Slot.Cloaks && x.Name.EndsWith("_set")).ToList())
        {
            if (baseLayer is null) break;

            string combined = $"{suit.Name}_over_{baseLayer}";

            if (Combine(outDir, [baseLayer, suit.Name], combined) is not { } shape) continue;

            pieces.Add(new Piece(combined, Slot.Clothes, shape.Female, false,
                shape.Tags));
        }

        CopyBlendShapes(dir, outDir);
        CopyTextures(dir, outDir);

        // Templates first: without one the accessories cannot be named by a portrait modifier, and
        // the failure is silent everywhere except error.log.
        foreach (var slot in pieces.Select(p => p.Slot).Distinct())
        {
            if (!WriteGeneTemplate(modDir, gameDir, slot, [.. pieces.Where(p => p.Slot == slot)]))
                return 0;
        }

        WriteAssets(dir, outDir, pieces);
        WriteAccessories(modDir, pieces);
        WriteModifiers(modDir, pieces);
        WriteDebugEvent(modDir, pieces);

        Console.WriteLine($"  custom armour: {pieces.Count} piece(s) - " + string.Join(", ",
            pieces.Select(p => $"{p.Name} ({(p.Female ? "female" : "male")}, "
                + $"{p.Slot.ToString().ToLowerInvariant()}"
                + (p.ShipsAsset ? ", own asset)" : ")")))
            + $" - wearable via the {WearFlag} flag");

        return pieces.Count;
    }

    /// <summary>
    /// Merges every shape of a parts library into one garment and writes it beside the others.
    ///
    /// The whole set for now, rather than a chosen subset. Assembling arbitrary combinations is the
    /// point of a parts library and is what makes armour procedural — but "does a merged skinned
    /// mesh bind and deform at all" has to be answered before which combination is worth asking, and
    /// the full set is the one that fails most visibly if it does not.
    ///
    /// Returns the assembled name, or empty when the merge was refused.
    /// </summary>
    private static string Assemble(string source, string outDir)
    {
        PdxNode library;

        try { library = PdxMesh.Read(source); }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            Console.WriteLine($"  custom armour: could not read {Path.GetFileName(source)} - {e.Message}");
            return "";
        }

        string stem = Path.GetFileNameWithoutExtension(source);
        string name = stem[..^PartsSuffix.Length] + "_set";

        var parts = library.Children.Where(c => c.Name == "object")
            .SelectMany(o => o.Children)
            .Select(sh => sh.Name)
            .ToList();

        if (ArmorAssembly.Merge([library], parts, name + "Shape") is not { } merged) return "";

        PdxMesh.Write(Path.Combine(outDir, name + ".mesh"), merged);

        Console.WriteLine($"  custom armour: assembled {name} from {parts.Count} part(s) - "
            + string.Join(", ", parts.Select(x => x.Replace(stem + "_", "").Replace("Shape", ""))));

        return name;
    }

    /// <summary>
    /// Merges already-copied pieces into one garment that occupies a single slot.
    ///
    /// Works on the mod's copies rather than the sources, so the bone rename has already happened
    /// and both halves are on the portrait rig before they meet — merging a renamed piece with an
    /// unrenamed one would produce a garment half of which binds to nothing.
    /// </summary>
    private static (bool FullBody, double Radius, string Tags, bool Female)?
        Combine(string outDir, IReadOnlyList<string> names, string combined)
    {
        var roots = new List<PdxNode>();
        var shapes = new List<string>();

        foreach (string name in names)
        {
            string path = Path.Combine(outDir, name + ".mesh");
            if (!File.Exists(path)) return null;

            var root = PdxMesh.Read(path);
            roots.Add(root);

            shapes.AddRange(root.Children.Where(c => c.Name == "object")
                .SelectMany(o => o.Children).Select(sh => sh.Name));
        }

        if (ArmorAssembly.Merge(roots, shapes, combined + "Shape") is not { } merged) return null;

        string target = Path.Combine(outDir, combined + ".mesh");
        PdxMesh.Write(target, merged);

        Console.WriteLine($"  custom armour: combined {string.Join(" + ", names)} into {combined}, "
            + "one garment in one slot");

        return CopyMesh(target, target);
    }

    /// <summary>The assets folder, checked next to the binary first — see the weapon forge's note
    /// on stale copies: a piece added since the last build is invisible until one.</summary>
    private static string? Locate()
    {
        string[] roots =
        [
            Path.Combine(AppContext.BaseDirectory, "assets", SourceDir),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", SourceDir),
        ];

        return roots.FirstOrDefault(Directory.Exists);
    }

    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Reads, normalises the skeleton's bone names, writes, and reports which slot the geometry says
    /// it belongs in. Null when the mesh cannot be read.
    ///
    /// Everything except the bone names passes through untouched — the geometry, the skin weights and
    /// the bone indices are all already correct, and the indices especially must not move: they point
    /// into this same bone list by position.
    /// </summary>
    private static (bool FullBody, double Radius, string Tags, bool Female)?
        CopyMesh(string source, string target)
    {
        PdxNode root;

        try { root = PdxMesh.Read(source); }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            Console.WriteLine($"  custom armour: could not read {Path.GetFileName(source)} - {e.Message}");
            return null;
        }

        int renamed = 0, bones = 0;
        double lowest = double.PositiveInfinity, highest = double.NegativeInfinity;

        // Read the sex off the rig the piece was exported against, before any renaming. "female"
        // contains "male", so it has to be tested first.
        bool female = root.Children.Where(c => c.Name == "object").SelectMany(o => o.Children)
            .SelectMany(sh => sh.Children.Where(c => c.Name == "skeleton"))
            .SelectMany(sk => sk.Children)
            .Any(b => b.Name.Contains("female", StringComparison.OrdinalIgnoreCase));

        foreach (var shape in root.Children.Where(c => c.Name == "object").SelectMany(o => o.Children))
        {
            foreach (var mesh in shape.Children.Where(c => c.Name == "mesh"))
            {
                float[] p = mesh.Floats("p");

                for (int v = 1; v < p.Length; v += 3)
                {
                    lowest = Math.Min(lowest, p[v]);
                    highest = Math.Max(highest, p[v]);
                }
            }

            foreach (var skeleton in shape.Children.Where(c => c.Name == "skeleton"))
            {
                foreach (var bone in skeleton.Children)
                {
                    bones++;
                    string wanted = (female ? FemalePortraitRig : MalePortraitRig) + BoneSuffix(bone.Name);
                    if (wanted == bone.Name) continue;

                    bone.Name = wanted;
                    renamed++;
                }
            }
        }

        PdxMesh.Write(target, root);

        // A blend-shape mesh carries no skeleton at all, by design; only the base does.
        if (bones > 0)
        {
            Console.WriteLine($"  custom armour: {Path.GetFileName(source)} - {renamed} of {bones} "
                + $"bone(s) renamed to the {(female ? "female" : "male")} portrait rig");
        }

        double reach = highest > 0 ? lowest / highest : 1.0;
        return (reach < OutfitReachesBelow, TorsoRadius(root), ShrinkTags(root), female);
    }

    /// <summary>
    /// Mean distance from the body's axis across the chest, which is how tightly a piece hugs it.
    ///
    /// This is what separates a BASE layer from something worn over one, and it separates them by a
    /// wide margin: the gambeson averages 15.1 units out, the plate suit 19.7. That is not a
    /// coincidence to be exploited but the actual physical fact — a garment worn under armour is
    /// nearer the body than the armour is, and the innermost layer is the one that should occupy the
    /// slot that REPLACES the outfit.
    ///
    /// Restricted to the chest band and to |x| &lt; 25 so sleeves, skirts and outstretched arms do
    /// not drag the average around.
    /// </summary>
    private static double TorsoRadius(PdxNode root)
    {
        double total = 0;
        int n = 0;

        foreach (var shape in root.Children.Where(c => c.Name == "object").SelectMany(o => o.Children))
        {
            foreach (var mesh in shape.Children.Where(c => c.Name == "mesh"))
            {
                float[] p = mesh.Floats("p");

                for (int v = 0; v + 2 < p.Length; v += 3)
                {
                    if (p[v + 1] < 100 || p[v + 1] > 140 || Math.Abs(p[v]) >= 25) continue;

                    // The body's axis sits a little behind origin in z; the offset is the same one
                    // the armour measurements elsewhere use.
                    double dz = p[v + 2] + 4.7;
                    total += Math.Sqrt(p[v] * p[v] + dz * dz);
                    n++;
                }
            }
        }

        return n == 0 ? double.MaxValue : total / n;
    }

    /// <summary>Share of a piece's vertices that sit out where the arms are.</summary>
    private static double ArmShare(PdxNode root) => RegionShare(root, 22, 85, 130);

    /// <summary>Share of a piece's vertices that sit out where the hands are.</summary>
    private static double HandShare(PdxNode root) => RegionShare(root, 44, 70, 105);

    // The four central regions. No lateral bound, because unlike the arms and hands these are not
    // defined by being out to the SIDE — a cuirass and a pauldron occupy the same heights and differ
    // only in how far out they sit. Height alone is what separates ribs from gut from thigh.
    //
    // Bands taken from the rig rather than guessed: thoracic sits at 114.8 and the clavicle at 137.5;
    // lumbar at 100.9 and the hips at 88.4; ankle at 7.5.

    /// <summary>Share sitting over the ribs, from the lower thorax up to the collarbones.</summary>
    private static double ChestShare(PdxNode root) => RegionShare(root, 0, 110, 140);

    /// <summary>Share sitting over the gut, between the hips and the lumbar joint.</summary>
    private static double BellyShare(PdxNode root) => RegionShare(root, 0, 88, 112);

    /// <summary>Share sitting on the legs, hips down to the ankles.</summary>
    private static double LegShare(PdxNode root) => RegionShare(root, 0, 12, 88);

    /// <summary>Share sitting on the feet, below the ankle joint.</summary>
    private static double FootShare(PdxNode root) => RegionShare(root, 0, 0, 12);

    /// <summary>
    /// Share of a piece's vertices inside a box: out past <paramref name="minAbsX"/> sideways, and
    /// between the two heights. File space, so the height is the SECOND component — see
    /// <see cref="CopyMesh"/>, which reads the same layout.
    /// </summary>
    private static double RegionShare(PdxNode root, double minAbsX, double loY, double hiY)
    {
        int inside = 0, all = 0;

        foreach (var shape in root.Children.Where(c => c.Name == "object").SelectMany(o => o.Children))
        {
            foreach (var mesh in shape.Children.Where(c => c.Name == "mesh"))
            {
                float[] p = mesh.Floats("p");

                for (int v = 0; v + 2 < p.Length; v += 3)
                {
                    all++;
                    if (Math.Abs(p[v]) > minAbsX && p[v + 1] >= loY && p[v + 1] <= hiY) inside++;
                }
            }
        }

        return all == 0 ? 0 : (double)inside / all;
    }

    /// <summary>
    /// The shrink tags a piece has earned, comma-joined the way vanilla writes them.
    ///
    /// Earned, never copied: a tag applied to a piece that does not enclose the part it shrinks
    /// deflates that part in plain sight, which is how the breastplate got twig arms.
    /// </summary>
    private static string ShrinkTags(PdxNode root)
    {
        var tags = new List<string>();

        // Written in vanilla's own order — see gfx/portraits/accessories/ccp5_armor.txt, which is a
        // full harness and carries shrink_arms,shrink_chest,shrink_belly,short_skirt,shrink_hands.
        if (ArmShare(root) > ArmCoverageNeedingShrink) tags.Add("shrink_arms");
        if (ChestShare(root) > TorsoCoverageNeedingShrink) tags.Add("shrink_chest");
        if (BellyShare(root) > TorsoCoverageNeedingShrink) tags.Add("shrink_belly");
        if (LegShare(root) > TorsoCoverageNeedingShrink) tags.Add("shrink_legs");
        if (HandShare(root) > HandCoverageNeedingShrink) tags.Add("shrink_hands");
        if (FootShare(root) > FootCoverageNeedingShrink) tags.Add("shrink_feet");

        return string.Join(',', tags);
    }

    /// <summary>The bone's own name, with whatever rig prefix it carries removed.</summary>
    private static string BoneSuffix(string bone)
    {
        foreach (string prefix in KnownRigPrefixes)
        {
            if (bone.StartsWith(prefix, StringComparison.Ordinal)) return bone[prefix.Length..];
        }

        return bone;
    }

    /// <summary>
    /// Blend-shape meshes, copied with their folder intact because a <c>.asset</c> names them by
    /// relative path (<c>blendshapes/x.mesh</c>). They carry no skeleton, so they need no renaming —
    /// which is also the rule that says they are blend shapes rather than models.
    /// </summary>
    private static void CopyBlendShapes(string sourceDir, string outDir)
    {
        string from = Path.Combine(sourceDir, BlendDir);
        if (!Directory.Exists(from)) return;

        string to = Path.Combine(outDir, BlendDir);
        Directory.CreateDirectory(to);

        foreach (string file in Directory.EnumerateFiles(from, "*.mesh"))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
    }

    /// <summary>
    /// Textures. CK3 resolves these globally by filename, so they only have to exist somewhere in
    /// the mod — beside the mesh is simply the tidiest place.
    /// </summary>
    private static void CopyTextures(string sourceDir, string outDir)
    {
        string from = Path.Combine(sourceDir, "textures");
        if (!Directory.Exists(from)) return;

        Directory.CreateDirectory(outDir);

        foreach (string file in Directory.EnumerateFiles(from, "*.dds"))
            File.Copy(file, Path.Combine(outDir, Path.GetFileName(file)), overwrite: true);
    }

    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A shipped <c>.asset</c> is copied verbatim; anything else gets one synthesised from its mesh.
    ///
    /// Verbatim because that file is the only place several things can come from. Blend shapes are
    /// the clearest case: a body-derived garment inherits the body's own morph deltas, so it needs
    /// six <c>blend_shape</c> declarations and six <c>attribute</c> bindings that name them, and none
    /// of that is inferable from geometry. The shader choice is deliberate too — a piece with no
    /// variation must avoid the <c>_pattern_</c> shader, which reads an unbound sampler and renders
    /// whatever the character's clothing left in the slot.
    /// </summary>
    private static void WriteAssets(string sourceDir, string outDir, List<Piece> pieces)
    {
        foreach (var piece in pieces.Where(p => p.ShipsAsset))
        {
            string text = File.ReadAllText(Path.Combine(sourceDir, piece.Name + ".asset"));

            // One invariant is enforced even on a hand-written asset: a `_pattern_` shader with no
            // portrait_accessory block reads an unbound sampler and renders whatever the character's
            // clothing left in it. That is not a style preference, it is a rendering bug, and it is
            // detectable from the file alone — so it is corrected rather than shipped, loudly enough
            // that the modelling side can fix it at source.
            if (text.Contains("_pattern", StringComparison.Ordinal)
                && !text.Contains("portrait_accessory", StringComparison.Ordinal))
            {
                text = text.Replace("_pattern", "", StringComparison.Ordinal);

                Console.WriteLine($"  custom armour: {piece.Name}.asset asked for a pattern shader "
                    + "but declares no portrait_accessory - dropped the pattern stage, which would "
                    + "otherwise sample whatever texture was left bound");
            }

            ParadoxText.WriteBom(Path.Combine(outDir, piece.Name + ".asset"), text);
        }

        var made = pieces.Where(p => !p.ShipsAsset).ToList();
        if (made.Count == 0) return;

        var b = new JominiBuilder();
        b.Comment("Assets synthesised for hand-modelled pieces that ship none of their own.\n\n"
            + "A piece WITH its own .asset is copied beside this file instead - that is where blend\n"
            + "shapes, their attribute bindings and a deliberate shader choice have to come from,\n"
            + "and none of it can be recovered from the mesh.");

        foreach (var piece in made)
        {
            var root = PdxMesh.Read(Path.Combine(outDir, piece.Name + ".mesh"));

            b.Blank();

            using (b.Block("pdxmesh"))
            {
                b.Quoted("name", $"{piece.Name}_mesh");
                b.Quoted("file", $"{piece.Name}.mesh");

                int index = 0;

                foreach (var shape in root.Children.Where(c => c.Name == "object").SelectMany(o => o.Children))
                {
                    foreach (var mesh in shape.Children.Where(c => c.Name == "mesh"))
                    {
                        var material = mesh.Children.FirstOrDefault(c => c.Name == "material");
                        if (material is null) continue;

                        using (b.Block("meshsettings"))
                        {
                            b.Quoted("name", shape.Name);
                            b.Field("index", index++);
                            b.Quoted("texture_diffuse", material.Prop("diff")?.Text ?? "");
                            b.Quoted("texture_normal", material.Prop("n")?.Text ?? "");
                            b.Quoted("texture_specular", material.Prop("spec")?.Text ?? "");
                            b.Quoted("shader", ShaderFor(material.Prop("shader")?.Text));
                            b.Quoted("shader_file", "gfx/FX/jomini/portrait.shader");
                        }
                    }
                }
            }

            b.Blank();

            using (b.Block("entity"))
            {
                b.Quoted("name", $"{piece.Name}_entity");
                b.Quoted("pdxmesh", $"{piece.Name}_mesh");
            }
        }

        ParadoxText.WriteBom(Path.Combine(outDir, "00_gen_armor_custom.asset"), b.ToString());
    }

    /// <summary>
    /// Drops the <c>_pattern</c> stage from whatever shader the mesh was exported with.
    ///
    /// **A pattern shader with no pattern data does not fall back — it reads rubbish.** A synthesised
    /// asset carries no <c>portrait_accessory</c> block, so no <c>pattern_mask</c> and no
    /// <c>variation</c> is ever bound, and <c>portrait_accessory_variation.fxh</c> opens with
    /// <c>float4 Mask = PdxTex2D( PatternMask, Input.UV0 )</c> against whatever texture happens to be
    /// in that slot — in practice whatever the character's own clothing bound for its draw. The piece
    /// came out patched with black in a pattern that changed with the outfit, and looked clean on a
    /// bare-chested character where nothing else had bound pattern data. Two symptoms, one cause.
    ///
    /// Both destinations exist in vanilla — <c>portrait_attachment</c> 103 uses,
    /// <c>portrait_attachment_alpha_to_coverage</c> 67 — so a piece exported with alpha coverage
    /// keeps it.
    /// </summary>
    private static string ShaderFor(string? meshShader)
    {
        string shader = string.IsNullOrWhiteSpace(meshShader) ? "portrait_attachment" : meshShader;
        return shader.Replace("_pattern", "", StringComparison.Ordinal);
    }

    private static void WriteAccessories(string modDir, List<Piece> pieces)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "accessories");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Accessories for the hand-modelled pieces.\n\n"
            + "set_tags is EARNED, not copied. Vanilla puts shrink_arms on every war garment so the\n"
            + "body cannot poke through a sleeve, but on a piece that does not enclose the arm the\n"
            + "same tag thins it into a twig - which is exactly what the breastplate did.\n\n"
            + "So it is applied by measurement: the share of a piece's vertices out past |x| = 22 in\n"
            + "the shoulder-to-elbow band. A breastplate scores 0%, the body-derived gambeson 22.7%\n"
            + "- it hugs the arm, so nothing can poke through it - and the plate suit 42.9%, whose\n"
            + "hard vambraces and gauntlets stand well off the arm and need the room.\n\n"
            + "shrink_hands is earned the same way, one joint further out: vertices past |x| = 44 at\n"
            + "hand height. Breastplate 0%, gambeson 8.8% (its hands were deleted, so that is wrist),\n"
            + "plate suit 20.5% on the strength of its gauntlets.\n\n"
            + "Expect little from it. male_bs_body_shrink_hands moves hand vertices a median of 0.68\n"
            + "and at most 2.02 units, and CK3 has no way to hide a hand at all - the only whole-part\n"
            + "hides in the game are bs_body_no_portrait and bs_body_no_left_leg. A gauntlet standing\n"
            + "further off than 2 units still shows knuckles, and the fix for that is the gauntlet.");

        foreach (var piece in pieces)
        {
            b.Blank();

            using (b.Block(Accessory(piece)))
            {
                if (piece.Tags.Length > 0) b.Quoted("set_tags", piece.Tags);

                b.Inline("entity", "required_tags", "=", "\"\"",
                    "shared_pose_entity", "=", "torso",
                    "entity", "=", $"{piece.Name}_entity");
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_armor_custom.txt"), b.ToString());
    }

    /// <summary>
    /// The accessory key: the mesh's name with a gender prefix.
    ///
    /// CK3 reads an accessory's gender from the START of its name — every one of the 630 entries in
    /// vanilla's clothes gene begins m_/male_ or f_/female_, with no exceptions — so an unprefixed
    /// name is treated as male. These are weighted to the male rig and only ever listed under
    /// <c>male</c>, so that is right; the prefix makes it explicit rather than lucky.
    /// </summary>
    private static string Accessory(Piece piece) => (piece.Female ? "f_" : "m_") + piece.Name;

    /// <summary>
    /// The gene template a slot's pieces belong to, spliced into a copy of vanilla's gene file.
    ///
    /// Splices accumulate — see <see cref="GeneSplice"/> — which matters here because the clothes
    /// file already carries <see cref="ArmorForgeStep"/>'s template by the time this runs.
    /// </summary>
    private static bool WriteGeneTemplate(string modDir, string gameDir, Slot slot, List<Piece> pieces)
    {
        var (gene, file, template, index) = Wiring(slot);
        var block = new JominiBuilder(startDepth: 3);

        using (block.Block(template))
        {
            block.Field("index", index);

            // Each sex lists its own pieces. A sex with none gets vanilla's no-op accessory rather
            // than a blank list - see the warning below on what that means in the clothes slot.
            foreach (bool female in new[] { false, true })
            {
                var mine = pieces.Where(p => p.Female == female).ToList();

                using (block.Block(female ? "female" : "male"))
                {
                    if (mine.Count == 0) block.Field("1", "empty");
                    else foreach (var piece in mine) block.Field("1", Accessory(piece));
                }
            }

            // On all 197 of vanilla's clothes templates without exception. Omitting them does not
            // fail to parse and ck3-tiger says nothing, but female lookups into the template stop
            // resolving.
            // On all 197 of vanilla's clothes templates without exception. Omitting them does not
            // fail to parse and ck3-tiger says nothing, but female lookups into the template stop
            // resolving.
            //
            // WATCH THE `empty` ABOVE. In the CLOTHES slot it does not mean "leave the outfit
            // alone", it means "wear nothing" - so a sex with no pieces must ALSO be excluded by the
            // portrait modifier's is_female gate. Without that gate, women selected `empty` and were
            // drawn naked.
            block.Field("boy", "male");
            block.Field("girl", "female");
        }

        return GeneSplice.Write(gameDir, modDir, file, gene,
            block.ToString().TrimEnd('\n').Split('\n'),
            $"Added by Ck3MapGen: hand-modelled armour worn from the {WearFlag} flag.");
    }

    /// <summary>
    /// A debug event with one option per piece, generated because the pieces are not known until
    /// the assets folder is read.
    ///
    /// The static Debug: Forge Armour event can only raise the shared flag, which picks the first
    /// piece of each slot. That is no use once two pieces share a slot — a full plate suit and a
    /// gambeson are both clothes, so only one can ever be worn and the choice was arbitrary. This
    /// names them.
    /// </summary>
    private static void WriteDebugEvent(string modDir, List<Piece> pieces)
    {
        string dir = Path.Combine(modDir, "events");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Wear a specific hand-modelled piece. One option per piece in assets/armors.\n\n"
            + "Each option clears every other piece's flag first, because pieces sharing a gene\n"
            + "slot exclude one another and leaving two flags up makes the winner arbitrary.");

        b.Blank();
        b.Field("namespace", "gen_armor_custom");
        b.Blank();

        using (b.Block("gen_armor_custom.0001"))
        {
            b.Field("type", "character_event");
            b.Quoted("title", "gen_armor_custom.0001.t");
            b.Quoted("desc", "gen_armor_custom.0001.desc");
            b.Field("theme", "realm");

            using (b.Block("left_portrait"))
            {
                b.Field("character", "root");
                b.Field("animation", "personality_bold");
            }

            foreach (var piece in pieces)
            {
                b.Blank();

                using (b.Block("option"))
                {
                    b.Field("name", $"gen_armor_custom.0001.{piece.Name}");

                    foreach (var other in pieces)
                        b.Field("remove_character_flag", FlagFor(other));

                    b.Field("add_character_flag", FlagFor(piece));
                }
            }

            b.Blank();

            using (b.Block("option"))
            {
                b.Field("name", "gen_armor_custom.0001.none");

                foreach (var piece in pieces) b.Field("remove_character_flag", FlagFor(piece));

                b.Field("remove_character_flag", WearFlag);
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_armor_custom_events.txt"), b.ToString());

        var loc = new LocFile();
        loc.Add("gen_armor_custom.0001.t", "Debug: Wear a Custom Armour Piece");
        loc.Add("gen_armor_custom.0001.desc",
            "Pick one hand-modelled piece to wear. Pieces sharing a slot exclude each other, so "
            + "choosing one takes off any other in the same slot.");

        foreach (var piece in pieces)
        {
            loc.Add($"gen_armor_custom.0001.{piece.Name}",
                $"{piece.Name} ({piece.Slot.ToString().ToLowerInvariant()} slot)");
        }

        loc.Add("gen_armor_custom.0001.none", "Take everything off");
        loc.Write(Path.Combine(modDir, "localization", "english",
            "zz_gen_armor_custom_l_english.yml"));
    }

    /// <summary>
    /// The portrait modifiers that put a piece on a character.
    ///
    /// Worn from a flag rather than from an artifact, because this exists to answer "does the mesh
    /// bind and deform" and an artifact gate would add two more things that could be the reason it
    /// did not show.
    ///
    /// Priority 8 sits above the artifact armour group at 7, so a piece under test cannot be masked
    /// by whatever else the character would have worn.
    /// </summary>
    private static void WriteModifiers(string modDir, List<Piece> pieces)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "portrait_modifiers");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Debug wear for hand-modelled armour. Raised by the Debug: Forge Armour event.\n\n"
            + "Each piece is declared in the gene its geometry calls for: one that reaches down the\n"
            + "body replaces the outfit (clothes), one that sits on the torso layers over it (cloaks).\n\n"
            + "ONE GROUP PER SLOT, and that matters. A group with selection_behavior = max applies a\n"
            + "single entry - the highest-weighted one - so a group holding both a clothes piece and a\n"
            + "cloak piece applies only one of them and the other silently never renders. That is what\n"
            + "made the gambeson appear not to replace the character's clothes: its entry was in the\n"
            + "same group as the breastplate's, and lost the tie.");

        var first = pieces.GroupBy(p => p.Slot).Select(g => g.First()).ToHashSet();

        foreach (var slot in pieces.Select(p => p.Slot).Distinct())
        {
            var (gene, _, template, _) = Wiring(slot);

            b.Blank();

            using (b.Block($"gen_armor_custom_{gene}_debug"))
            {
                b.Field("usage", "game");
                b.Field("selection_behavior", "max");
                b.Field("priority", 8);

                foreach (var piece in pieces.Where(p => p.Slot == slot))
                {
                b.Blank();

                using (b.Block($"{piece.Name}_debug"))
                {
                    using (b.Block("dna_modifiers"))
                    using (b.Block("accessory"))
                    {
                        b.Field("mode", "add");
                        b.Field("gene", gene);
                        b.Field("template", template);
                        b.Field("accessory", Accessory(piece));
                    }

                    b.Inline("outfit_tags", "military_outfit");

                    using (b.Block("weight"))
                    {
                        b.Field("base", 0);

                        // Its own flag outranks the shared one, so summoning a specific piece wins
                        // over whatever the shared flag would have picked in that slot.
                        using (b.Block("modifier"))
                        {
                            b.Field("add", 2000);
                            b.Field("is_female", piece.Female ? "yes" : "no");
                            b.Field("has_character_flag", FlagFor(piece));
                        }

                        using (b.Block("modifier"))
                        {
                            b.Field("add", first.Contains(piece) ? 1000 : 0);

                            // MANDATORY, not a refinement. These pieces are weighted to the male rig
                            // and so are listed only under `male`; the template's female list holds
                            // vanilla's no-op accessory `empty`. Without this gate the modifier fires
                            // on women too and resolves to `empty` — and `empty` in the CLOTHES slot
                            // is not "no change", it is "no clothes". Female characters were drawn
                            // entirely naked.
                            b.Field("is_female", piece.Female ? "yes" : "no");
                            b.Field("has_character_flag", WearFlag);
                        }
                    }
                }
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_armor_custom.txt"), b.ToString());
    }
}
