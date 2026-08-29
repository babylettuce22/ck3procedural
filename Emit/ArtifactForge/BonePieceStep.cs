namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using System.IO;

/// <summary>A place a rigid piece can hang, and the bone that carries it.</summary>
/// <param name="Suffix">What a source filename ends in to claim this slot.</param>
/// <param name="Bone">The portrait-skeleton bone the piece is parented to.</param>
/// <param name="Opposite">
/// The slot on the other side of the body, when this one has a counterpart.
///
/// Set only for slots that genuinely come in mirrored pairs. A head or spine slot has none, and a
/// piece there must never be auto-mirrored — there is nothing to mirror it onto.
/// </param>
public sealed record BoneSlot(string Suffix, string Bone, string? Opposite = null)
{
    /// <summary>The slot's short name, used in gene and accessory keys.</summary>
    public string Key => Suffix.TrimStart('_');
}

/// <summary>
/// One drawable shape inside a piece, and the textures its material names.
///
/// Carried out of the bake because the entity has to restate them: a <c>.mesh</c> names a shader and
/// three textures, but NOT which shader file the shader lives in, so an entity with no
/// <c>meshsettings</c> sends the engine looking in the default <c>gfx/FX/pdxmesh.shader</c> and it
/// fails there. See <see cref="BonePieceStep.WriteAssets"/>.
/// </summary>
public sealed record PieceShape(string Name, string? Diffuse, string? Normal, string? Specular);

/// <summary>One source mesh, and where it goes.</summary>
/// <param name="Mirror">
/// Whether the source is the OTHER side's mesh and must be reflected across the body's midline
/// before it is baked. See <see cref="BonePieceStep.Reflect"/>.
/// </param>
public sealed record BonePiece(string Set, BoneSlot Slot, string Source, bool Mirror = false)
{
    public string Name => $"gen_piece_{Set}_{Slot.Key}";

    /// <summary>Filled in by the bake, which is the only pass that reads the mesh.</summary>
    public List<PieceShape> Shapes { get; } = [];
}

/// <summary>
/// Hangs rigid pieces off the portrait skeleton, so an artifact can be garnished with GEOMETRY
/// rather than only repainted.
///
/// Pauldrons are the first use and the reason it exists, but nothing here is about shoulders: a
/// piece declares its slot in its filename, and a slot is one row of <see cref="Slots"/>. Adding a
/// helm crest, a back banner or a hip ornament is that row plus a mesh.
///
/// **Why geometry is the only answer for some of this.** A war garment's mask marks its CLOTH, not
/// its plates — every visible channel measures metalness ~0.00, and 94-98% of a garment's genuinely
/// metal texels lie outside the mask. So no recolour can make a cloth garment read as plate; adding
/// metal is the only way to have metal. See <see cref="ArmorMask"/> for the measurement, and for why
/// aiming paint at the real plates was built, tried, and judged worse.
///
/// **The mechanism, established by <see cref="BoneAttachProbe"/> and confirmed in game.** An
/// accessory may declare <c>node = "&lt;bone&gt;"</c> to parent a RIGID entity to a bone, the way
/// every weapon hangs off <c>bn_r_prop</c>. It is not hand-only — <c>prophet_shield</c> uses
/// <c>bn_h_head_mid</c> — it needs no animation hook provided the modifier group is
/// <c>usage = game</c>, and a wholly new accessory gene declared in a file of ours merges and
/// renders. A rigid piece needs no skinning, no blend shapes and no per-sex rig.
///
/// **Authoring is in BODY space; the bake happens here.** A modeller places the piece on a reference
/// body where it looks right — the only part that genuinely needs a human — and this step converts
/// it into the bone's local space, because the engine places it as <c>world = bone * local</c>.
/// Blender's own bone matrices cannot be used: io_pdx_mesh builds ~0.5-unit stub bones and Blender
/// forces +Y along a bone, so its orientations are synthetic. Only the game file knows the real
/// frame — see <see cref="BoneFrames"/>.
///
/// **One bake serves both sexes.** The transform is RELATIVE to its bone, and the male and female
/// rigs differ almost entirely in where a bone sits (shoulder at 135.7 against 127.4) rather than
/// how it is turned — measured local axes agree to within 0.01. So a piece baked once lands on
/// whichever body wears it, the genes declare <c>female = male</c>, and the accessory names need no
/// <c>m_</c>/<c>f_</c> prefix. <c>prophet_shield</c> is the vanilla precedent for both.
///
/// **Left and right are NOT interchangeable.** The two shoulder frames are 180 degrees apart about
/// the shoulder's own local X — a pure rotation, determinant +1, not a mirror — so one mesh on both
/// bones caps the right shoulder and hangs under the left armpit. Each side is its own source mesh,
/// which is why the slot, not merely the set, is in the filename.
/// </summary>
public static class BonePieceStep
{
    /// <summary>Where hand-placed pieces live, relative to the assets folder.</summary>
    private const string SourceDir = "attachments";

    /// <summary>Where the baked meshes and their entities go in the mod.</summary>
    private const string ModelDir = "gfx/models/artifacts/gen_pieces";

    /// <summary>
    /// Every slot a piece can claim, matched against the end of its filename.
    ///
    /// **Longest suffix first**, because the shorthands overlap the explicit forms: without that
    /// ordering <c>iso_shoulder_l</c> would match the bare <c>_l</c> and be indistinguishable from a
    /// file that never said which slot it meant.
    ///
    /// The shorthands exist because shoulders are the common case and were the first thing built.
    /// A new slot is one row here plus a mesh named for it — no other code changes.
    /// </summary>
    private static readonly BoneSlot[] Slots =
    [
        new("_shoulder_l", "bn_l_shoulder", "_shoulder_r"),
        new("_shoulder_r", "bn_r_shoulder", "_shoulder_l"),
        new("_head",       "bn_h_head_mid"),
        new("_l",          "bn_l_shoulder", "_r"),
        new("_r",          "bn_r_shoulder", "_l"),
    ];

    private static string GeneOf(BoneSlot slot) => $"gen_armor_piece_{slot.Key}";

    /// <summary>
    /// The empty template every character sits on until a modifier moves them off it.
    ///
    /// See <see cref="WriteGenes"/> — without one, the whole world wears the accessory.
    /// </summary>
    private static string EmptyTemplateOf(BoneSlot slot) => $"gen_armor_piece_{slot.Key}_none";

    /// <summary>
    /// ONE TEMPLATE PER PIECE, sharing the accessory's name.
    ///
    /// A template holding several accessories leaves which one appears up to the gene's DNA value,
    /// even though the portrait modifier names an accessory outright — so two sets sharing a slot
    /// could swap. Vanilla's own convention is one template per prop with the same name for both
    /// (<c>prophet_shield</c> the template holds <c>prophet_shield</c> the accessory), which removes
    /// the question entirely.
    /// </summary>
    private static string TemplateOf(BonePiece piece) => piece.Name;

    /// <summary>The flag that forces a set on, for looking at one without hunting for an artifact.</summary>
    private static string FlagOf(string set) => $"pmg_wear_piece_{set}";

    /// <summary>
    /// Rarity the garnish is reserved for.
    ///
    /// Illustrious alone on purpose. The point is to make the top of the ladder feel like a different
    /// KIND of object rather than a brighter repaint, and a piece that turns up on common armour
    /// spends that distinction for nothing.
    /// </summary>
    private const string Rarity = "illustrious";

    public static int WriteAll(string modDir, string gameDir, IReadOnlyList<string> cultureKeys)
    {
        if (!ArtifactForgeFlags.BonePieces) return 0;

        string? dir = Locate();
        if (dir is null) return 0;

        var pieces = Read(dir);
        if (pieces.Count == 0) return 0;

        var frames = BoneFrames.Read(gameDir);

        if (frames.Count == 0)
        {
            Console.WriteLine("  bone pieces: could not read the portrait skeleton from the game "
                + "directory - nothing baked");
            return 0;
        }

        string outDir = Path.Combine(modDir, ModelDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(outDir);

        var baked = new List<BonePiece>();

        foreach (var piece in pieces)
        {
            if (!frames.TryGetValue(piece.Slot.Bone, out var frame))
            {
                Console.WriteLine($"  bone pieces: {piece.Slot.Bone} is not in the portrait skeleton "
                    + $"- {Path.GetFileName(piece.Source)} skipped");
                continue;
            }

            if (Bake(piece.Source, Path.Combine(outDir, piece.Name + ".mesh"), frame, piece))
                baked.Add(piece);
        }

        if (baked.Count == 0) return 0;

        var sets = baked.Select(p => p.Set).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        var byCulture = AssignSets(cultureKeys, sets);

        int converted = ConvertTextures(modDir, dir, baked);
        LendTextures(baked);
        int textures = CopyTextures(modDir, dir, baked);

        WriteAssets(modDir, baked);
        WriteAccessories(modDir, baked);
        WriteGenes(modDir, baked);
        WriteModifiers(modDir, baked, byCulture);
        WriteDebug(modDir, sets);

        Console.WriteLine($"  bone pieces: {baked.Count} piece(s), {sets.Count} set(s) "
            + $"({string.Join(", ", sets)}), slots "
            + string.Join("/", baked.Select(p => p.Slot.Key).Distinct().OrderBy(s => s, StringComparer.Ordinal))
            + $", {textures} copied + {converted} repacked texture set(s), on {Rarity} artifacts");

        foreach (string set in sets)
            Console.WriteLine($"    {set}: {byCulture[set].Count} culture(s)");

        return baked.Count;
    }

    private static string? Locate()
    {
        string[] roots =
        [
            Path.Combine(AppContext.BaseDirectory, "assets", SourceDir),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", SourceDir),
        ];

        return roots.FirstOrDefault(Directory.Exists);
    }

    /// <summary>
    /// Every piece in the folder, by the slot its filename ends in.
    ///
    /// A suffix rather than a sidecar file, the same convention the weapon forge uses for parts
    /// libraries and for the same reason: a sidecar is one more thing to keep in step with the
    /// folder. A mesh claiming no slot is skipped and said so — the slot decides which bone it is
    /// baked against, and guessing would place it silently wrong.
    /// </summary>
    private static List<BonePiece> Read(string dir)
    {
        var found = new List<BonePiece>();

        foreach (string path in Directory.EnumerateFiles(dir, "*.mesh")
            .OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var slot = Slots.FirstOrDefault(s => name.EndsWith(s.Suffix, StringComparison.Ordinal));

            if (slot is null)
            {
                Console.WriteLine($"  bone pieces: {Path.GetFileName(path)} claims no slot - expected "
                    + $"a name ending in {string.Join(", ", Slots.Select(s => s.Suffix))} - skipped");
                continue;
            }

            found.Add(new BonePiece(name[..^slot.Suffix.Length], slot, path));
        }

        return Pair(found);
    }

    /// <summary>
    /// Fills in the missing half of a pair by reflecting the half that exists.
    ///
    /// **Why the generator does this rather than the modeller.** Armour that sits on a limb comes in
    /// mirrored pairs, and the reflection is not just "negate x": it also has to reverse every
    /// triangle's winding and flip the tangent handedness, because a mirror inverts orientation. Get
    /// either wrong and the piece renders inside-out or lights backwards — a mistake that is cheap to
    /// make once per piece by hand and free to never make here.
    ///
    /// It cannot simply reuse the mesh on the other bone: the two shoulder frames are 180 degrees
    /// apart about the shoulder's own local X, a pure rotation with determinant +1 rather than a
    /// mirror, so the unreflected mesh caps one shoulder and hangs under the other armpit.
    ///
    /// **An explicit file always wins.** Authoring both sides is still supported and is the only way
    /// to get a deliberately asymmetric pair; this only fills a gap. A slot with no
    /// <see cref="BoneSlot.Opposite"/> — a head or spine piece — is never mirrored, because there is
    /// nowhere to mirror it to.
    /// </summary>
    private static List<BonePiece> Pair(List<BonePiece> pieces)
    {
        var have = pieces.Select(p => (p.Set, p.Slot.Suffix)).ToHashSet();
        var added = new List<BonePiece>();

        foreach (var piece in pieces)
        {
            if (piece.Slot.Opposite is not { } opposite) continue;
            if (have.Contains((piece.Set, opposite))) continue;

            var other = Slots.FirstOrDefault(s => s.Suffix == opposite);
            if (other is null) continue;

            added.Add(new BonePiece(piece.Set, other, piece.Source, Mirror: true));

            Console.WriteLine($"    {piece.Set}: no {opposite} mesh, mirroring {Path.GetFileName(piece.Source)} "
                + $"onto {other.Bone} - author one to override");
        }

        return [.. pieces, .. added];
    }

    /// <summary>
    /// Which culture gets which set.
    ///
    /// Deterministic rather than rolled, and keyed on the culture, so a world always garnishes a
    /// given culture the same way and two cultures differ. Same hash as the garment choice in
    /// <see cref="ArmorForgeStep"/>, and for the same reason: <c>string.GetHashCode</c> is randomised
    /// per process and would redress the world on every run.
    /// </summary>
    private static Dictionary<string, List<string>> AssignSets(
        IReadOnlyList<string> cultures, List<string> sets)
    {
        var byCulture = sets.ToDictionary(s => s, _ => new List<string>(), StringComparer.Ordinal);

        foreach (string culture in cultures)
        {
            uint h = 2166136261;

            foreach (char c in culture)
            {
                h ^= c;
                h *= 16777619;
            }

            byCulture[sets[(int)(h % (uint)sets.Count)]].Add(culture);
        }

        return byCulture;
    }

    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Rewrites one mesh from body space into a bone's local space.
    ///
    /// Positions move; normals and tangents only ROTATE, because they are directions — translating
    /// them would tip every one toward the bone's origin and light the piece inside-out. A tangent is
    /// four floats and only the first three are a direction: the fourth is a handedness sign and must
    /// be copied untouched, or every normal map on the piece flips.
    ///
    /// The bounds are recomputed rather than transformed. An axis-aligned box is not axis-aligned
    /// once rotated, so carrying the old one over would describe a volume the geometry has left — and
    /// the engine culls against it, which shows as a piece that vanishes at certain angles.
    /// </summary>
    private static bool Bake(string source, string target, BoneFrame frame, BonePiece piece)
    {
        PdxNode root;

        try
        {
            root = PdxMesh.Read(source);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException)
        {
            Console.WriteLine($"  bone pieces: {Path.GetFileName(source)} could not be read - {e.Message}");
            return false;
        }

        int meshes = 0;
        Walk(root, "");

        if (meshes == 0)
        {
            Console.WriteLine($"  bone pieces: {Path.GetFileName(source)} carries no vertex positions - skipped");
            return false;
        }

        PdxMesh.Write(target, root);
        return true;

        // The shape is the mesh node's PARENT, and its name is what a meshsettings block has to
        // match - so the parent is threaded down rather than looked up afterwards.
        void Walk(PdxNode node, string parent)
        {
            float[] p = node.Floats("p");

            if (p.Length >= 3)
            {
                meshes++;

                var material = node.Children.FirstOrDefault(c => c.Name == "material");

                piece.Shapes.Add(new PieceShape(
                    parent,
                    material?.Prop("diff")?.Text,
                    material?.Prop("n")?.Text,
                    material?.Prop("spec")?.Text));

                // Reflect in BODY space, before the bone transform: mirroring is about the body's
                // midline, and doing it after the bake would reflect across the bone's own axes and
                // put the piece somewhere arbitrary.
                if (piece.Mirror) Reflect(node, p);

                for (int i = 0; i + 2 < p.Length; i += 3)
                {
                    var (x, y, z) = frame.ToLocal(p[i], p[i + 1], p[i + 2]);
                    p[i] = (float)x; p[i + 1] = (float)y; p[i + 2] = (float)z;
                }

                float[] n = node.Floats("n");

                for (int i = 0; i + 2 < n.Length; i += 3)
                {
                    var (x, y, z) = frame.RotateToLocal(n[i], n[i + 1], n[i + 2]);
                    n[i] = (float)x; n[i + 1] = (float)y; n[i + 2] = (float)z;
                }

                float[] ta = node.Floats("ta");

                for (int i = 0; i + 3 < ta.Length; i += 4)
                {
                    var (x, y, z) = frame.RotateToLocal(ta[i], ta[i + 1], ta[i + 2]);
                    ta[i] = (float)x; ta[i + 1] = (float)y; ta[i + 2] = (float)z;
                    // ta[i + 3] is handedness, deliberately untouched.
                }

                Rebound(node, p);
            }

            foreach (var kid in node.Children) Walk(kid, node.Name);
        }
    }

    /// <summary>
    /// Reflects one mesh node across the body's midline, in place.
    ///
    /// **Three things have to change together, and forgetting any one of them is a distinct visible
    /// bug.**
    ///
    /// * Positions and directions negate their X. Positions alone would put the geometry on the
    ///   other side while every normal still faced the way it used to, lighting the piece as though
    ///   lit from the wrong side.
    /// * Triangle WINDING reverses. A reflection inverts orientation, so a mesh that was
    ///   counter-clockwise is now clockwise and every face is back-facing — which under normal
    ///   culling means the piece is inside-out, and reads as a hollow shell rather than as armour.
    /// * Tangent HANDEDNESS flips. The fourth tangent float encodes whether the bitangent is
    ///   <c>cross(N, T)</c> or its negation; a mirror swaps which, and leaving it puts every normal
    ///   map's bumps in relief where they should be recessed.
    /// </summary>
    private static void Reflect(PdxNode node, float[] p)
    {
        for (int i = 0; i < p.Length; i += 3) p[i] = -p[i];

        float[] n = node.Floats("n");
        for (int i = 0; i < n.Length; i += 3) n[i] = -n[i];

        float[] ta = node.Floats("ta");
        for (int i = 0; i + 3 < ta.Length; i += 4)
        {
            ta[i] = -ta[i];
            ta[i + 3] = -ta[i + 3];
        }

        // Swapping any two corners of each triangle restores the winding a reflection inverted.
        int[] tri = node.Ints("tri");
        for (int i = 0; i + 2 < tri.Length; i += 3) (tri[i + 1], tri[i + 2]) = (tri[i + 2], tri[i + 1]);
    }

    /// <summary>Recomputes whatever bounds a node declares, from the moved positions.</summary>
    private static void Rebound(PdxNode node, float[] p)
    {
        float[] min = [float.MaxValue, float.MaxValue, float.MaxValue];
        float[] max = [float.MinValue, float.MinValue, float.MinValue];

        for (int i = 0; i + 2 < p.Length; i += 3)
            for (int c = 0; c < 3; c++)
            {
                min[c] = Math.Min(min[c], p[i + c]);
                max[c] = Math.Max(max[c], p[i + c]);
            }

        if (node.Children.FirstOrDefault(c => c.Name == "aabb") is { } aabb)
        {
            aabb.Set("min", PdxProp.Of(min));
            aabb.Set("max", PdxProp.Of(max));
        }

        if (node.Prop("boundingsphere") is null) return;

        float[] centre = [(min[0] + max[0]) / 2, (min[1] + max[1]) / 2, (min[2] + max[2]) / 2];
        double radius = 0;

        for (int i = 0; i + 2 < p.Length; i += 3)
        {
            double dx = p[i] - centre[0], dy = p[i + 1] - centre[1], dz = p[i + 2] - centre[2];
            radius = Math.Max(radius, Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        node.Set("boundingsphere", PdxProp.Of([centre[0], centre[1], centre[2], (float)radius]));
    }

    /// <summary>
    /// The shader a bone-attached piece is drawn with, and the file it lives in.
    ///
    /// **The <c>shader_file</c> is the whole reason <c>meshsettings</c> cannot be skipped.** A
    /// <c>.mesh</c> stores a shader NAME in its material but not which file defines it, so an entity
    /// that declares no meshsettings sends the engine to the default <c>gfx/FX/pdxmesh.shader</c>,
    /// where no portrait shader exists:
    ///
    /// <code>
    /// [E][pdxassetutil.cpp:900]: Failed to create material with shader
    /// portrait_attachment_pattern_alpha_to_coverage (in gfx/FX/pdxmesh.shader) for mesh [...]
    /// </code>
    ///
    /// The mesh still loads — it is even named in later texture-streaming warnings — so nothing says
    /// "missing model". The piece simply never draws, which reads exactly like a portrait modifier
    /// that failed to apply and sends the search to entirely the wrong place.
    ///
    /// <c>portrait_attachment</c> rather than the <c>_pattern_</c> variant the source meshes happen
    /// to name: the pattern shaders expect a variation and a mask, and these pieces declare neither.
    /// <c>prophet_shield</c> — vanilla's own rigid bone-attached prop — uses exactly this pairing.
    /// </summary>
    private const string Shader = "portrait_attachment";
    private const string ShaderFile = "gfx/FX/jomini/portrait.shader";

    /// <summary>
    /// One entity per piece, with a <c>meshsettings</c> block per shape.
    ///
    /// The block's <c>name</c> must match the shape's name inside the <c>.mesh</c> — it is how the
    /// engine pairs settings to geometry — which is why the bake carries those names out rather than
    /// this pass guessing them from the filename.
    /// </summary>
    private static void WriteAssets(string modDir, List<BonePiece> pieces)
    {
        string dir = Path.Combine(modDir, ModelDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Bone-attached piece entities. The geometry is already in its bone's local space\n"
            + "- see BonePieceStep - so the engine's `world = bone * local` puts it in place.\n\n"
            + "meshsettings is NOT optional: a .mesh names its shader but not the file that defines\n"
            + "it, and without shader_file the engine looks in gfx/FX/pdxmesh.shader and the material\n"
            + "fails to create. The mesh still loads, so the piece silently never draws.");

        foreach (var piece in pieces)
        {
            b.Blank();
            b.Raw("pdxmesh = {\n");
            b.Raw($"\tname = \"{piece.Name}_mesh\"\n");
            b.Raw($"\tfile = \"{piece.Name}.mesh\"\n");

            for (int i = 0; i < piece.Shapes.Count; i++)
            {
                var shape = piece.Shapes[i];

                b.Raw("\n\tmeshsettings = {\n");
                b.Raw($"\t\tname = \"{shape.Name}\"\n");
                b.Raw($"\t\tindex = {i}\n");

                if (shape.Diffuse is { Length: > 0 } d) b.Raw($"\t\ttexture_diffuse = \"{d}\"\n");
                if (shape.Normal is { Length: > 0 } n) b.Raw($"\t\ttexture_normal = \"{n}\"\n");
                if (shape.Specular is { Length: > 0 } s) b.Raw($"\t\ttexture_specular = \"{s}\"\n");

                b.Raw($"\t\tshader = \"{Shader}\"\n");
                b.Raw($"\t\tshader_file = \"{ShaderFile}\"\n");
                b.Raw("\t}\n");
            }

            b.Raw("}\n\n");
            b.Raw("entity = {\n");
            b.Raw($"\tname = \"{piece.Name}_entity\"\n");
            b.Raw($"\tpdxmesh = \"{piece.Name}_mesh\"\n");
            b.Raw("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_pieces.asset"), b.ToString());
    }

    /// <summary>
    /// Repacks a set's own authoring textures into CK3's layouts, and points its shapes at them.
    ///
    /// **These OVERRIDE whatever the mesh's material names**, deliberately. A harvested mesh names
    /// the maps it shipped with — <c>material_0_baseColor.jpeg</c> and the like — and expecting a
    /// modeller to retype a generated DDS name into the material before every export is a step that
    /// will be forgotten once and then debugged for an hour. The convention is positional instead:
    /// textures for set <c>X</c> live at
    /// <c>assets/attachments/textures/X_{diffuse,normal,properties}.png</c>, and a set that has them
    /// uses them.
    ///
    /// A set with none is untouched, which is what keeps the ISO pauldrons on the plate atlas their
    /// mesh already names.
    /// </summary>
    private static int ConvertTextures(string modDir, string assetsDir, List<BonePiece> pieces)
    {
        string texturesDir = Path.Combine(assetsDir, "textures");
        if (!Directory.Exists(texturesDir)) return 0;

        string outDir = Path.Combine(modDir, ModelDir.Replace('/', Path.DirectorySeparatorChar));
        int done = 0;

        foreach (string set in pieces.Select(p => p.Set).Distinct())
        {
            if (PieceTextures.Convert(texturesDir, outDir, set) is not { } made) continue;

            foreach (var piece in pieces.Where(p => p.Set == set))
            {
                for (int i = 0; i < piece.Shapes.Count; i++)
                {
                    piece.Shapes[i] = piece.Shapes[i] with
                    {
                        Diffuse = made.Diffuse,
                        Normal = made.Normal,
                        Specular = made.Properties,
                    };
                }
            }

            Console.WriteLine($"    {set}: repacked its own textures to {made.Diffuse} and two more");
            done++;
        }

        return done;
    }

    /// <summary>
    /// Gives a shape that names no textures a set borrowed from one that does.
    ///
    /// **A stand-in, and it will look wrong.** The borrowed atlas was unwrapped for different
    /// geometry, so the piece is drawn with somebody else's texels — recognisable as armour, not as
    /// anything deliberate. It is here because the alternative is worse: a mesh imported from glTF
    /// or FBX arrives with a bare material naming nothing, and a meshsettings block with no textures
    /// risks the same silent non-draw that a missing <c>shader_file</c> causes. Something visible
    /// and obviously provisional beats a piece that is simply absent for a reason nothing reports.
    ///
    /// Authoring real textures replaces this automatically: the moment a mesh's own material names
    /// them, nothing is borrowed.
    /// </summary>
    private static void LendTextures(List<BonePiece> pieces)
    {
        var donor = pieces
            .SelectMany(p => p.Shapes)
            .FirstOrDefault(s => !string.IsNullOrEmpty(s.Diffuse));

        if (donor is null) return;

        foreach (var piece in pieces)
        {
            for (int i = 0; i < piece.Shapes.Count; i++)
            {
                var shape = piece.Shapes[i];
                if (!string.IsNullOrEmpty(shape.Diffuse)) continue;

                piece.Shapes[i] = shape with
                {
                    Diffuse = donor.Diffuse,
                    Normal = donor.Normal,
                    Specular = donor.Specular,
                };

                Console.WriteLine($"    {piece.Name}: no textures of its own, borrowing "
                    + $"{donor.Diffuse} as a stand-in - it will not line up");
            }
        }
    }

    /// <summary>
    /// Ships the textures the pieces name, from beside the source meshes.
    ///
    /// CK3 resolves textures globally by bare filename, so where they land does not matter — only
    /// that they exist somewhere in the mod. Beside the meshes is simply the tidiest place.
    ///
    /// A texture that cannot be found is reported rather than silently skipped: the failure it
    /// produces is a piece drawn with whatever was last bound, which on a portrait reads as a hole
    /// or as garbage rather than as a missing file.
    /// </summary>
    private static int CopyTextures(string modDir, string assetsDir, List<BonePiece> pieces)
    {
        var wanted = new SortedSet<string>(pieces
            .SelectMany(p => p.Shapes)
            .SelectMany(s => new[] { s.Diffuse, s.Normal, s.Specular })
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!), StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0) return 0;

        // Beside the pieces first, then the armour library - the plate set's textures live there and
        // the isolated pauldrons are cut from that same atlas.
        string[] roots =
        [
            Path.Combine(assetsDir, "textures"),
            Path.Combine(Path.GetDirectoryName(assetsDir) ?? assetsDir, "armors", "textures"),
        ];

        string outDir = Path.Combine(modDir, ModelDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(outDir);
        int copied = 0;

        // Already in the mod is already resolvable. CustomArmorStep ships the plate atlas for its
        // own pieces, and the isolated pauldrons are cut from that same set - copying it a second
        // time changes nothing about what the engine finds and earns a duplicate-item warning per
        // file, which is noise in the one log that has to stay readable.
        var present = new HashSet<string>(
            Directory.Exists(Path.Combine(modDir, "gfx"))
                ? Directory.EnumerateFiles(Path.Combine(modDir, "gfx"), "*.dds", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                : [],
            StringComparer.OrdinalIgnoreCase);

        foreach (string name in wanted)
        {
            if (present.Contains(name)) continue;

            string? found = roots
                .Select(r => Path.Combine(r, name))
                .FirstOrDefault(File.Exists);

            if (found is null)
            {
                Console.WriteLine($"  bone pieces: texture {name} is named by a piece but is in "
                    + "neither attachments/textures nor armors/textures - the piece will draw untextured");
                continue;
            }

            File.Copy(found, Path.Combine(outDir, name), overwrite: true);
            copied++;
        }

        return copied;
    }

    /// <summary>
    /// One accessory per piece, each naming the bone it hangs from.
    ///
    /// <c>node</c> is what makes this rigid and parented rather than skinned to the body — the same
    /// field every weapon uses for <c>bn_r_prop</c>. No <c>shared_pose_entity</c>: that is for
    /// geometry that deforms with the torso, which is the opposite of what this is.
    /// </summary>
    private static void WriteAccessories(string modDir, List<BonePiece> pieces)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "accessories");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Bone-attached accessories, one per piece and slot.\n\n"
            + "No m_/f_ name prefix, and none is needed: the genes declare `female = male`, so there\n"
            + "is no per-sex list for the engine to infer membership of. prophet_shield is the\n"
            + "vanilla precedent for both that and for a non-hand node.");

        foreach (var piece in pieces)
        {
            b.Blank();

            using (b.Block(piece.Name))
                b.Inline("entity", "required_tags", "=", "\"\"", "node", "=", $"\"{piece.Slot.Bone}\"",
                    "entity", "=", $"\"{piece.Name}_entity\"");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_pieces.txt"), b.ToString());
    }

    /// <summary>
    /// A gene per slot, in a file of our own.
    ///
    /// One per slot and not one for everything, because a gene holds exactly ONE accessory — a left
    /// and a right pauldron have to appear together, so they cannot compete for the same slot. It is
    /// also why these do not live in <c>props_left</c>/<c>props_right</c>: those are where weapons
    /// go, and a pauldron would silently evict a sword.
    ///
    /// Declaring wholly new genes in our own file merges rather than replaces — verified in game by
    /// <see cref="BoneAttachProbe"/>. Redeclaring an EXISTING gene here would replace it and take
    /// every vanilla accessory in it with it.
    /// </summary>
    private static void WriteGenes(string modDir, List<BonePiece> pieces)
    {
        string dir = Path.Combine(modDir, "common", "genes");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Genes for bone-attached pieces: one slot each.\n\n"
            + "New genes, so this file merges into accessory_genes rather than replacing anything.\n"
            + "If characters lose an accessory slot when this is present, that assumption is wrong\n"
            + "and this is the file to delete.");

        using (b.Block("accessory_genes"))
        {
            foreach (var slot in pieces.Select(p => p.Slot).Distinct().OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                using (b.Block(GeneOf(slot)))
                {
                    // AN EMPTY TEMPLATE AT INDEX 0, AND IT IS NOT OPTIONAL.
                    //
                    // An accessory gene is part of every character's DNA: everyone carries a value
                    // for every gene, and that value selects a TEMPLATE whether or not any portrait
                    // modifier has an opinion. A gene whose only template holds our accessory
                    // therefore puts that accessory on the entire world - which is exactly what
                    // happened, and it looked like the trigger being ignored rather than like the
                    // gene doing its job.
                    //
                    // Vanilla always ships the empty default: `no_props` in props_left,
                    // `no_headgear` in headgear, `no_clothes` in clothes, every one of them index 0
                    // with an empty male list. A portrait modifier naming the real template below is
                    // what moves a character off it.
                    using (b.Block(EmptyTemplateOf(slot)))
                    {
                        b.Field("index", 0);

                        using (b.Block("male")) { }

                        b.Field("female", "male");
                        b.Field("boy", "male");
                        b.Field("girl", "female");
                    }

                    int index = 1;

                    foreach (var piece in pieces.Where(p => p.Slot == slot))
                    {
                        using (b.Block(TemplateOf(piece)))
                        {
                            // Unique within this gene; index 0 is the empty default above.
                            b.Field("index", index++);

                            using (b.Block("male"))
                                b.Field("1", piece.Name);

                            // One bake serves both sexes - the transform is relative to the bone,
                            // and the rigs differ in where a bone sits rather than how it is turned.
                            b.Field("female", "male");
                            b.Field("boy", "male");
                            b.Field("girl", "female");
                        }
                    }
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_pieces.txt"), b.ToString());
    }

    /// <summary>
    /// What decides that a character is wearing a set, and which one.
    ///
    /// <c>usage = game</c>, so the group is evaluated on every portrait and needs no animation hook —
    /// the property that makes bone attachment usable for armour at all.
    ///
    /// One group per SLOT rather than one for everything: <c>selection_behavior = max</c> applies a
    /// single entry per group, so left and right in one group would mean only ever one shoulder. That
    /// is the same trap <see cref="CustomArmorStep"/> hit when a clothes piece and a cloak shared a
    /// group.
    ///
    /// Two ways in, deliberately. The artifact gate is the real one; the character flag is what lets
    /// a set be looked at without hunting the world for an illustrious piece of the right culture,
    /// and it outweighs the artifact so a deliberate test always wins.
    /// </summary>
    private static void WriteModifiers(string modDir, List<BonePiece> pieces,
        Dictionary<string, List<string>> byCulture)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "portrait_modifiers");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Bone-attached pieces on illustrious artifacts.\n\n"
            + "Gated on the artifact's CREATOR culture, exactly as the armour itself is, so a piece\n"
            + "keeps its own look when it changes hands rather than being re-garnished by whoever\n"
            + "stole it.\n\n"
            + "Priority 8 puts these above the artifact armour at 7, since they layer over it.");

        foreach (var slot in pieces.Select(p => p.Slot).Distinct().OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            b.Blank();

            using (b.Block($"gen_piece_{slot.Key}"))
            {
                b.Field("usage", "game");
                b.Field("selection_behavior", "max");
                b.Field("priority", 8);

                foreach (var piece in pieces.Where(p => p.Slot == slot))
                {
                    var cultures = byCulture.TryGetValue(piece.Set, out var list) ? list : [];

                    b.Blank();

                    using (b.Block(piece.Name))
                    {
                        using (b.Block("dna_modifiers"))
                        using (b.Block("accessory"))
                        {
                            b.Field("mode", "add");
                            b.Field("gene", GeneOf(slot));
                            b.Field("template", TemplateOf(piece));
                            b.Field("accessory", piece.Name);
                        }

                        b.Field("outfit_tags", "{ military_outfit }");

                        using (b.Block("weight"))
                        {
                            b.Field("base", 0);

                            // The debug flag, weighted above the artifact so a forced set always wins.
                            using (b.Block("modifier"))
                            {
                                b.Field("add", 2000);
                                b.Field("has_character_flag", FlagOf(piece.Set));
                            }

                            // No culture drew this set - possible when there are more sets than
                            // cultures - so the artifact gate could never fire and is left out
                            // rather than emitted as a trigger nothing can satisfy.
                            if (cultures.Count == 0) continue;

                            using (b.Block("modifier"))
                            {
                                b.Field("add", 1000);

                                using (b.Block("any_equipped_character_artifact"))
                                {
                                    b.Field("rarity", Rarity);

                                    // Written by hand because `?=` is ONE token and Block would put
                                    // the builder's " = " separator inside it, giving `creator ? =`.
                                    // The safe form matters for its own sake too: an artifact with
                                    // no creator - anything made before history - would otherwise
                                    // throw on every portrait that evaluates this.
                                    // All of it raw, and the nesting counted by hand: Raw does not
                                    // advance the builder's depth, so a Block opened after one would
                                    // indent as though it were a sibling of `creator` rather than a
                                    // child. Valid script either way, but unreadable, and it reads as
                                    // a different trigger to anyone skimming it.
                                    b.Raw($"{b.IndentAt(b.Depth)}creator ?= {{\n");
                                    b.Raw($"{b.IndentAt(b.Depth + 1)}OR = {{\n");

                                    foreach (string culture in cultures)
                                        b.Raw($"{b.IndentAt(b.Depth + 2)}culture = culture:{culture}\n");

                                    b.Raw($"{b.IndentAt(b.Depth + 1)}}}\n");
                                    b.Raw($"{b.IndentAt(b.Depth)}}}\n");

                                    using (b.Block("NOT"))
                                        b.Field("has_variable", "gen_artifact_hide_on_portrait");
                                }
                            }
                        }
                    }
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_pieces.txt"), b.ToString());
    }

    /// <summary>
    /// An event that forces one set on, so placement can be judged without finding an artifact.
    ///
    /// Every option clears every flag first, so a session cannot end up wearing two sets and reading
    /// the overlap as one badly placed piece.
    /// </summary>
    private static void WriteDebug(string modDir, List<string> sets)
    {
        string dir = Path.Combine(modDir, "events");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Raw("namespace = pmg_piece\n\n");
        b.Comment("Bone-piece debug. Raise with:  event pmg_piece.0001\n\n"
            + "The flag outweighs the artifact gate, so a forced set shows on any character\n"
            + "regardless of what they are carrying.");
        b.Blank();

        b.Raw("pmg_piece.0001 = {\n\ttype = character_event\n");
        b.Raw("\ttitle = pmg_piece.0001.t\n\tdesc = pmg_piece.0001.desc\n\ttheme = realm\n\n");
        b.Raw("\tleft_portrait = { character = root  animation = personality_bold }\n\n");

        for (int i = 0; i < sets.Count; i++)
        {
            b.Raw("\toption = {\n");
            b.Raw($"\t\tname = pmg_piece.0001.{(char)('a' + i)}\n");

            foreach (string other in sets) b.Raw($"\t\tremove_character_flag = {FlagOf(other)}\n");

            b.Raw($"\t\tadd_character_flag = {FlagOf(sets[i])}\n");
            b.Raw("\t\ttrigger_event = { id = pmg_piece.0001 days = 0 }\n\t}\n\n");
        }

        b.Raw("\toption = {\n");
        b.Raw($"\t\tname = pmg_piece.0001.{(char)('a' + sets.Count)}\n");

        foreach (string other in sets) b.Raw($"\t\tremove_character_flag = {FlagOf(other)}\n");

        b.Raw("\t}\n}\n");

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_piece_events.txt"), b.ToString());

        var loc = new LocFile();
        loc.Add("pmg_piece.0001.t", "Debug: Attached Pieces");
        loc.Add("pmg_piece.0001.desc",
            "Forces one set of bone-attached pieces onto this character, ignoring what they are "
            + "carrying.\\n\\nIn play these appear only on ILLUSTRIOUS artifacts, and which set you "
            + "get is decided by the culture of whoever created the piece - so a world dresses each "
            + "culture consistently and two cultures differ.\\n\\nWhat to look for: that every slot "
            + "the set covers is filled, that the pieces sit ON the body rather than through it or "
            + "beside it, and that they hold up on a fat or muscular character - a bone-attached "
            + "piece is rigid and does not follow the body's shape, which is the one thing that "
            + "could sink the approach.");

        for (int i = 0; i < sets.Count; i++)
            loc.Add($"pmg_piece.0001.{(char)('a' + i)}", $"Wear the '{sets[i]}' set");

        loc.Add($"pmg_piece.0001.{(char)('a' + sets.Count)}", "Take them off");

        loc.Write(Path.Combine(modDir, "localization", "english", "zz_gen_piece_l_english.yml"));
    }
}
