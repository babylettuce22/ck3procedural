namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.IO;

/// <summary>One emitted pairing: which pieces it names, and the entity an artifact visual points at.</summary>
public sealed record ComposedLook(
    string Kind, string LeadFamily, string BaseFamily, string Name)
{
    /// <summary>The entity an artifact visual points at.</summary>
    public string EntityName => $"{Name}_entity";

    /// <summary>The artifact visual key this pairing is catalogued under.</summary>
    public string VisualKey => $"{Name}_visuals";
}

/// <summary>Everything one weapon kind contributes, with each piece already built exactly once.</summary>
public sealed record ComposedKind(
    string Kind,
    IReadOnlyList<(string Family, WeaponBase Built)> Bases,
    IReadOnlyList<(string Family, WeaponPiece Built, WeaponPart Part)> Leads);

/// <summary>
/// Writes composed weapons into the mod: one <c>.mesh</c> per shareable piece, and one <c>.asset</c>
/// declaring every piece and every pairing built from them.
///
/// **The shape this replaces.** <see cref="ForgedWeaponWriter"/> writes one merged mesh per weapon,
/// so a pairing costs a binary file. Here a pairing costs a few lines of text: the geometry is the
/// base assembly and the lead, each written once, and the pairing is an <c>entity</c> that names the
/// base's mesh and attaches the lead's entity at a locator. 861 pairings come out of 115 meshes on
/// the current libraries.
///
/// **Why the base is the root and the lead is attached** — the two facts are measured, not chosen.
/// An attached child receives no <c>portrait_accessory</c> binding, its own or its parent's, so only
/// the root can carry a procedural palette; and only the anchor's mesh is the same file in every
/// combination, because placement gives the anchor a shift of exactly zero. The anchor is the part
/// the hand holds. See <see cref="ComposedWeapon"/>.
///
/// **Everything lands in one directory and one <c>.asset</c>**, which is vanilla's own arrangement —
/// <c>ep1_artifacts_weapons_portrait.asset</c> declares hundreds of meshes and entities in a single
/// file, and a <c>pdxmesh</c>'s <c>file</c> resolves beside the asset that declares it.
/// </summary>
public static class ComposedWeaponWriter
{
    /// <summary>Where composed pieces and their asset live inside the mod.</summary>
    public const string ModelDir = "gfx/models/artifacts/gen_weapons";

    /// <summary>The one asset file every piece and pairing is declared in.</summary>
    private const string AssetFile = "00_gen_composed_weapons.asset";

    /// <summary>
    /// The shader for a piece with no <c>portrait_accessory</c>.
    ///
    /// Not <c>portrait_attachment_pattern</c>, and that is load-bearing rather than tidiness: a
    /// <c>_pattern</c> shader with nothing bound reads an unbound sampler and draws whatever texture
    /// was left in the slot — in practice the clothing of whoever is holding it, which is why an
    /// attached lead came out invisible on one character and red-and-black banded on another during
    /// the attach probes. <see cref="CustomArmorStep"/> enforces the same rule for armour.
    /// </summary>
    private const string PlainShader = "portrait_attachment";

    /// <summary>
    /// The base's shader when it is recoloured. Safe here and nowhere else: every entity that uses a
    /// base pdxmesh is a pairing root, and every pairing root declares the accessory the pattern
    /// stage needs. The lead never gets this, because an attached child's accessory is never bound.
    /// </summary>
    private const string PatternShader = "portrait_attachment_pattern";
    private const string ShaderFile = "gfx/FX/jomini/portrait.shader";

    /// <summary>Locator the lead hangs from. One name, because a base attaches exactly one lead.</summary>
    private const string LeadLocator = "lead_socket";

    public static string BaseMeshName(string family) => $"gen_base_{family}";
    public static string LeadMeshName(string family) => $"gen_lead_{family}";
    public static string LeadEntityName(string family) => $"{LeadMeshName(family)}_entity";

    /// <summary>
    /// The stem every name for one pairing derives from — its entity, and the artifact visual that
    /// points at it. Kept as one function so the two cannot drift apart, and so a visual key does
    /// not end up spelling <c>_entity_visuals</c> by appending to a name that already said entity.
    /// </summary>
    public static string PairName(string lead, string baseFamily)
        => $"gen_wpn_{lead}__{baseFamily}";

    /// <summary>Entity for one pairing — what an artifact visual's <c>asset</c> field names.</summary>
    public static string PairEntityName(string lead, string baseFamily)
        => $"{PairName(lead, baseFamily)}_entity";

    /// <summary>
    /// Every pairing the kinds admit, without writing anything.
    ///
    /// Split out from <see cref="WriteAll"/> because the tier plan is built from this list and the
    /// finishes are keyed on the tier, so the pairings have to be known before the assets that wear
    /// them can be written. Both walk the families in the same order, so the two agree by
    /// construction rather than by a shared sort.
    /// </summary>
    public static List<ComposedLook> Plan(
        IReadOnlyList<ComposedKind> kinds, Func<string, string, bool> mayCombine)
    {
        var looks = new List<ComposedLook>();

        foreach (var kind in kinds)
            foreach (var (baseFamily, _) in kind.Bases)
                foreach (var (leadFamily, _, _) in kind.Leads)
                    if (mayCombine(leadFamily, baseFamily))
                        looks.Add(new ComposedLook(
                            kind.Kind, leadFamily, baseFamily, PairName(leadFamily, baseFamily)));

        return looks;
    }

    /// <summary>
    /// Writes every piece and every pairing, and returns one row per pairing.
    ///
    /// <paramref name="mayCombine"/> is the caller's compatibility rule — the same one that keeps a
    /// family whose haft is twice the library's median from hosting a foreign head. Pairings it
    /// rejects are never written, so the returned rows are exactly what the catalogue may offer.
    /// </summary>
    public static List<ComposedLook> WriteAll(
        string modDir, IReadOnlyList<ComposedKind> kinds, Func<string, string, bool> mayCombine,
        IReadOnlyDictionary<string, ArtifactRarity> tierOf, ForgedRecolour? recolour = null,
        Func<string, ArtifactRarity, string>? baseLookName = null)
    {
        string dir = Path.Combine(modDir, ModelDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);

        var looks = new List<ComposedLook>();
        var b = new System.Text.StringBuilder();

        b.Append("# Composed weapons: shareable pieces, and the pairings built from them.\n");
        b.Append("# Generated by Emit/ArtifactForge/ComposedWeaponWriter.cs - do not hand-edit.\n");
        b.Append("#\n");
        b.Append("# A pairing is an entity, not a mesh. The base assembly is the root and the lead\n");
        b.Append("# is attached to it, because an attached child gets no portrait_accessory binding\n");
        b.Append("# and only the anchor's mesh is the same file in every combination.\n\n");

        foreach (var kind in kinds)
        {
            foreach (var (family, built) in kind.Bases)
            {
                PdxMesh.Write(Path.Combine(dir, $"{BaseMeshName(family)}.mesh"), built.Piece.Root);
                WritePdxMesh(b, built.Piece, BaseMeshName(family), patterned: recolour is not null);
            }

            foreach (var (family, built, _) in kind.Leads)
            {
                PdxMesh.Write(Path.Combine(dir, $"{LeadMeshName(family)}.mesh"), built.Root);
                WritePdxMesh(b, built, LeadMeshName(family), patterned: false);

                // The lead's own entity, referenced by every pairing that uses it. Plain shader and
                // no accessory: see PlainShader.
                b.Append($"entity = {{\n\tname = \"{LeadEntityName(family)}\"\n");
                b.Append($"\tpdxmesh = \"{LeadMeshName(family)}\"\n}}\n\n");
            }

            foreach (var (baseFamily, built) in kind.Bases)
            {
                float[] at = built.LeadLocator;

                foreach (var (leadFamily, _, _) in kind.Leads)
                {
                    if (!mayCombine(leadFamily, baseFamily)) continue;

                    string pair = PairName(leadFamily, baseFamily);
                    string entity = $"{pair}_entity";

                    b.Append($"entity = {{\n\tname = \"{entity}\"\n");
                    b.Append($"\tpdxmesh = \"{BaseMeshName(baseFamily)}\"\n");

                    // The accessory lives on the pairing rather than on the shared base mesh, which
                    // is what lets one set of geometry wear a different finish per rarity band: the
                    // mask is the base's own and identical across bands, and only the variation
                    // changes. The lead never gets one -- an attached child's accessory is never
                    // bound, so declaring it there would be a lie that renders as an unbound
                    // sampler.
                    if (recolour is not null && baseLookName is not null
                        && tierOf.TryGetValue(pair, out var tier))
                    {
                        string look = baseLookName(baseFamily, tier);

                        b.Append("\tgame_data = {\n\t\tportrait_entity_user_data = {\n");
                        b.Append("\t\t\tportrait_accessory = {\n");
                        b.Append($"\t\t\t\tpattern_mask = \"{recolour.MaskFor(look)}\"\n");
                        b.Append($"\t\t\t\tvariation = \"{recolour.VariationFor(look)}\"\n");
                        b.Append("\t\t\t}\n\t\t}\n\t}\n");
                    }

                    b.Append($"\tlocator = {{ name = \"{LeadLocator}\" position = "
                        + $"{{ {F(at[0])} {F(at[1])} {F(at[2])} }} }}\n");
                    b.Append($"\tattach = {{ \"{LeadLocator}\" = \"{LeadEntityName(leadFamily)}\" }}\n}}\n\n");

                    looks.Add(new ComposedLook(kind.Kind, leadFamily, baseFamily, pair));
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, AssetFile), b.ToString());
        return looks;
    }

    /// <summary>
    /// One <c>pdxmesh</c> block, with a <c>meshsettings</c> per material batch.
    ///
    /// <c>meshsettings.name</c> must equal the shape node inside the <c>.mesh</c> and the batches are
    /// addressed by <c>index</c> in node order — the same contract
    /// <see cref="ForgedWeaponWriter"/> documents, and the reason
    /// <see cref="WeaponPiece.Materials"/> is an ordered list rather than a set.
    /// </summary>
    private static void WritePdxMesh(
        System.Text.StringBuilder b, WeaponPiece piece, string meshName, bool patterned)
    {
        b.Append($"pdxmesh = {{\n\tname = \"{meshName}\"\n\tfile = \"{meshName}.mesh\"\n");

        for (int i = 0; i < piece.Materials.Count; i++)
        {
            var m = piece.Materials[i];

            b.Append($"\n\tmeshsettings = {{\n\t\tname = \"{piece.ShapeName}\"\n\t\tindex = {i}\n");
            b.Append($"\t\ttexture_diffuse = \"{m.Diffuse}\"\n");
            b.Append($"\t\ttexture_normal = \"{m.Normal}\"\n");
            b.Append($"\t\ttexture_specular = \"{m.Specular}\"\n");
            b.Append($"\t\tshader = \"{(patterned ? PatternShader : PlainShader)}\"\n");
            b.Append($"\t\tshader_file = \"{ShaderFile}\"\n\t}}\n");
        }

        b.Append("}\n\n");
    }

    /// <summary>
    /// A locator coordinate, invariant of the machine's locale.
    ///
    /// Paradox parses <c>1.5</c> and would read a comma-decimal <c>1,5</c> as two numbers, silently
    /// shifting the part it places. Formatting is pinned here rather than trusted to the ambient
    /// culture for the same reason every other emitter in this project pins it.
    /// </summary>
    private static string F(float value)
        => value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
}
