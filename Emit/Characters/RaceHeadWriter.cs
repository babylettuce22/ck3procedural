using System.Text;
using System.Text.RegularExpressions;
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Emits the race head shapes — the elves' pointed ears (one style shared by high elves, wood elves
/// and dusk elves) as blendshapes of the vanilla head, and the orcs' tusks as geometry added to the
/// vanilla teeth — plus the head and teeth assets that declare them:
///
/// <code>
/// gfx/models/portraits/{male,female}_head/{sex}_head.asset                       (vanilla + 3 lines)
/// gfx/models/portraits/{male,female}_head/blendshapes/{sex}_bs_gen_elf_ears_high.mesh
/// gfx/models/portraits/{male,female}_head/blendshapes/{sex}_bs_gen_giant_face.mesh   (see GiantFace)
/// gfx/models/portraits/{male,female}_head/blendshapes/{sex}_bs_gen_orc_brow.mesh     (see OrcBrow)
/// gfx/models/portraits/{male,female}_head/{sex}_teeth/{sex}_teeth.asset         (see PatchTeethAsset)
/// gfx/models/portraits/{male,female}_head/{sex}_teeth/*.mesh                    (see BuildTeeth)
/// </code>
///
/// The tusk half is described at <see cref="BuildTeeth"/> and <see cref="OrcTusks"/>; its gene,
/// <c>gen_bs_orc_tusks</c>, sits beside the ear gene in Core and reaches orcs the same way.
///
/// The gene that drives them, <c>gen_bs_elf_ears</c>, is static in
/// BaseFilesToCopy/Core/common/genes/gen_bs_elf_ears.txt; <see cref="RaceMorphs"/> puts it in the
/// three elves' ethnicities and <see cref="RaceMorphWriter"/> forces it by trait, like every other
/// race-defining gene. See <see cref="PointedEars"/> for why an ear is a blendshape at all.
///
/// **Built from the installed game, not shipped.** A blendshape has to match the head vertex for
/// vertex, and the head asset is a whole-file override. Copying either into BaseFilesToCopy would go
/// stale silently the day a patch touches the head; reading them from <c>gameDir</c> at generation
/// time keeps both current. The asset edit is two inserted lines — one <c>blend_shape</c> in the
/// pdxmesh, one <c>attribute</c> in the entity — placed after the last of each, and the rest of the
/// file is vanilla's bytes, BOM and line endings included (the female asset is CRLF, the male LF).
///
/// **All or nothing, per feature.** If a head or teeth file cannot be read, has changed shape, or no
/// longer has the lines this anchors on, that feature is skipped for both sexes and a warning says
/// why. Its gene then sets an attribute no mesh declares, which the engine ignores: the elves keep
/// round ears or the orcs human teeth, but every portrait still renders.
///
/// Only written when fantasy races are on, so a human-only map ships vanilla's head untouched.
/// Proved in game with the standalone elf_ear_probe mod on 2026-09-28.
/// </summary>
public static class RaceHeadWriter
{
    /// <summary>The gene and its templates; defined beside the shape in <see cref="PointedEars"/>.</summary>
    public const string Gene = PointedEars.Gene;
    public const string NoneTemplate = PointedEars.NoneTemplate;
    public const string HighTemplate = PointedEars.HighTemplate;

    /// <summary>
    /// Every ear style the writer builds: its style key (naming attribute, blendshape and file)
    /// and its shape. A new style is a row here plus a template in the gene file.
    /// </summary>
    private static readonly (string Style, EarShape Shape)[] Styles =
    [
        ("high", EarShape.HighElf),
        ("sylvan", EarShape.Sylvan),
        ("drow", EarShape.Drow),
    ];

    /// <summary>The attribute a gene template sets and the head asset maps to a blendshape.</summary>
    public static string AttributeOf(string style) => $"gen_bs_elf_ears_{style}";

    private static string BlendShapeId(string sex, string style) => $"{sex}_bs_gen_elf_ears_{style}";

    private static readonly string[] Sexes = ["male", "female"];

    public static void WriteAll(string modDir, string gameDir, MapConfig cfg)
    {
        if (!RaceMorphWriter.RacesOn(cfg)) return;
        RaceHeadCache.Write(modDir, gameDir, Build);
    }

    /// <summary>
    /// Everything this writer ships, built from the installed game into <paramref name="modDir"/>
    /// (a staging folder, when called through <see cref="RaceHeadCache"/>). Its log lines go to
    /// <paramref name="log"/> so a cache hit can replay them.
    /// </summary>
    private static void Build(string modDir, string gameDir, List<string> log)
    {
        string portraits = Path.Combine(gameDir, "gfx", "models", "portraits");
        string outPortraits = Path.Combine(modDir, "gfx", "models", "portraits");

        // Ears and tusks are independent, each all-or-nothing across both sexes: a failure in one
        // leaves the other shipping. Both touch the head asset, so it is patched once, at the end,
        // with whichever of them succeeded.
        var ears = new List<(string Path, PdxNode Root, PointedEars.Result Shape)>();
        try
        {
            foreach (string sex in Sexes)
            {
                string headDir = Path.Combine(portraits, $"{sex}_head");
                var head = PdxMesh.Read(Path.Combine(headDir, $"{sex}_head.mesh"));

                foreach (var (style, shape) in Styles)
                {
                    var built = BuildShape(head, Path.Combine(headDir, "blendshapes", $"{sex}_bs_ear_size_max.mesh"),
                        shape, BlendShapeId(sex, style));
                    ears.Add((Path.Combine(outPortraits, $"{sex}_head", "blendshapes",
                        $"{BlendShapeId(sex, style)}.mesh"), built.Root, built.Shape));
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log.Add($"  WARNING: pointed ears skipped, elves keep round ears: {e.Message}");
            ears.Clear();
        }

        // The face shapes (giantkin face, orc brow), before the tusks and horns: both mirror them as
        // follow shapes, since they move the lip a tusk exits from and the brow skin under a horn root.
        // Each is all-or-nothing across both sexes on its own.
        var faces = new List<(FaceShape Face, Dictionary<string, (PdxNode Root, PointedEars.Result Shape)> BySex)>();
        foreach (var face in FaceShapes)
        {
            try
            {
                var bySex = new Dictionary<string, (PdxNode Root, PointedEars.Result Shape)>();
                foreach (string sex in Sexes)
                    bySex[sex] = BuildFaceShape(portraits, sex, face);
                faces.Add((face, bySex));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                log.Add($"  WARNING: {face.Label} skipped, {face.Fallback}: {e.Message}");
            }
        }

        // Generated head shapes the tusks and horns must follow, per sex: (id, positions, attributes).
        List<(string Id, float[] P, string[] Attributes)> Generated(string sex) =>
            [.. faces.Select(f => (f.Face.Id(sex), f.BySex[sex].Shape.P, new[] { f.Face.Attribute }))];

        var teeth = new List<TeethOutput>();
        try
        {
            foreach (string sex in Sexes)
                teeth.Add(BuildTeeth(Path.Combine(portraits, $"{sex}_head"), sex, sex == "female" ? 0.9 : 1.0, Generated(sex)));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log.Add($"  WARNING: orc tusks skipped, orcs keep human teeth: {e.Message}");
            teeth.Clear();
        }

        var horns = new List<HornOutput>();
        try
        {
            foreach (string sex in Sexes)
                horns.Add(BuildHorns(portraits, sex, sex == "female" ? 0.9 : 1.0, Generated(sex)));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log.Add($"  WARNING: horns skipped, no horn models or horn gene this run: {e.Message}");
            horns.Clear();
        }

        if (ears.Count == 0 && teeth.Count == 0 && horns.Count == 0 && faces.Count == 0) return;

        var assets = new List<(string Path, byte[] Bytes)>();
        try
        {
            foreach (string sex in Sexes)
                assets.Add((Path.Combine(outPortraits, $"{sex}_head", $"{sex}_head.asset"),
                    PatchAsset(File.ReadAllBytes(Path.Combine(portraits, $"{sex}_head", $"{sex}_head.asset")), sex,
                        ears.Count > 0, teeth.Count > 0, horns.Count > 0, [.. faces.Select(f => f.Face)])));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log.Add($"  WARNING: race head shapes skipped, the head asset could not be patched: {e.Message}");
            return;
        }

        foreach (var (path, root, _) in ears) PdxMesh.Write(path, root);
        foreach (var (face, bySex) in faces)
            foreach (var (sex, (root, _)) in bySex)
                PdxMesh.Write(Path.Combine(outPortraits, $"{sex}_head", "blendshapes", $"{face.Id(sex)}.mesh"), root);
        foreach (var t in teeth)
        {
            string dir = Path.Combine(outPortraits, $"{t.Sex}_head", $"{t.Sex}_teeth");
            foreach (var (name, root) in t.Meshes) PdxMesh.Write(Path.Combine(dir, name), root);
            assets.Add((Path.Combine(dir, $"{t.Sex}_teeth.asset"), t.Asset));
        }

        foreach (var (path, bytes) in assets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        int bands = 0;
        if (horns.Count > 0)
        {
            WriteHorns(modDir, horns);
            bands = WriteHornCrowns(modDir, gameDir, log);
        }

        if (ears.Count > 0)
            log.Add($"  pointed ears written: {ears.Count} blendshapes " +
                              $"(tip travel {ears.Max(m => m.Shape.MaxShift):0.00}, " +
                              $"{ears.Min(m => m.Shape.Moved)}-{ears.Max(m => m.Shape.Moved)} vertices moved)");
        foreach (var (face, bySex) in faces)
            log.Add($"  {face.Label} written: {string.Join(", ", bySex.Select(g => $"{g.Key} {g.Value.Shape.Moved} vertices moved, max {g.Value.Shape.MaxShift:0.00}"))}");
        if (teeth.Count > 0)
            log.Add($"  orc tusks written: {string.Join(", ", teeth.Select(t => $"{t.Sex} {t.Meshes.Count} teeth meshes, {t.Follows} lip-follow shapes"))}");
        if (horns.Count > 0)
            log.Add($"  horns written: {Horns.Styles.Length} styles, {Horns.OrnamentMeshes().Count()} ornament shapes x {Horns.Metals.Length} metals, " +
                              $"{string.Join(", ", horns.Select(h => $"{h.Sex} {h.Meshes.Count} meshes"))}, " +
                              $"{bands} crowns worn as bands over horns");
    }

    // ---- Face shapes (giantkin face, orc brow) ------------------------------------------------

    /// <summary>A face-bone head blendshape: its log label, what the race keeps if it fails, its
    /// attribute, its per-sex blendshape id, and the shape builder.</summary>
    private sealed record FaceShape(
        string Label, string Fallback, string Attribute, Func<string, string> Id,
        Func<float[], float[], float[], int[], string[], bool, PointedEars.Result> Build);

    private static readonly FaceShape[] FaceShapes =
    [
        new("giantkin face", "giantkin keep the plain brow and jaw", GiantFace.Attribute, GiantFace.BlendShapeId, GiantFace.Shape),
        new("orc brow", "orcs keep the plain brow", OrcBrow.Attribute, OrcBrow.BlendShapeId, OrcBrow.Shape),
    ];

    /// <summary>
    /// One sex's face shape (<see cref="GiantFace"/>, <see cref="OrcBrow"/>) on a vanilla head
    /// blendshape as the container, as the ears are. The fields place themselves by each vertex's
    /// dominant bone.
    /// </summary>
    private static (PdxNode Root, PointedEars.Result Shape) BuildFaceShape(string portraits, string sex, FaceShape face)
    {
        string headDir = Path.Combine(portraits, $"{sex}_head");
        var head = PdxMesh.Read(Path.Combine(headDir, $"{sex}_head.mesh"));
        var mesh = Find(head, "mesh") ?? throw new InvalidDataException($"{sex}_head.mesh has no mesh node");
        var skin = Find(head, "skin") ?? throw new InvalidDataException($"{sex}_head.mesh has no skin node");
        var skeleton = Find(head, "skeleton") ?? throw new InvalidDataException($"{sex}_head.mesh has no skeleton");

        float[] p = mesh.Floats("p");
        int count = p.Length / 3;
        int[] ix = skin.Ints("ix");
        float[] w = skin.Floats("w");
        int per = ix.Length / Math.Max(1, count);
        var names = new Dictionary<int, string>();
        foreach (var bone in skeleton.Children)
            if (bone.Ints("ix") is [var i, ..]) names[i] = bone.Name;

        var dominant = new string[count];
        for (int v = 0; v < count; v++)
        {
            int best = 0;
            for (int k = 1; k < per; k++)
                if (w[v * per + k] > w[v * per + best]) best = k;
            dominant[v] = names.GetValueOrDefault(ix[v * per + best], "");
        }

        var shape = face.Build(p, mesh.Floats("n"), mesh.Floats("ta"), mesh.Ints("tri"), dominant, sex == "female");

        var root = PdxMesh.Read(Path.Combine(headDir, "blendshapes", $"{sex}_bs_ear_size_max.mesh"));
        var tm = Find(root, "mesh") ?? throw new InvalidDataException("ear blendshape has no mesh node");
        if (!tm.Ints("tri").AsSpan().SequenceEqual(mesh.Ints("tri")))
            throw new InvalidDataException($"{sex}_bs_ear_size_max.mesh no longer matches the head's topology");
        (Find(root, "object") ?? throw new InvalidDataException("ear blendshape has no object")).Children[0].Name = $"{face.Id(sex)}Shape";
        tm.Set("p", PdxProp.Of(shape.P));
        tm.Set("n", PdxProp.Of(shape.N));
        tm.Set("ta", PdxProp.Of(shape.Ta));
        SetBounds(tm, shape.P);
        return (root, shape);
    }

    // ---- Horns -------------------------------------------------------------------------------

    /// <summary>Where the horn models, textures and asset live.</summary>
    public const string HornModelDir = "gfx/models/portraits/attachments/gen_horns";
    private const string HornTexture = "gen_horns_keratin";
    private const string MetalTexture = "gen_horns_metal";
    private const string HornGeneFile = "common/genes/gen_horns.txt";

    /// <summary>
    /// Whether this run shipped the horn accessory gene. <see cref="PortraitWriter"/> pads every
    /// persistent DNA record with the gene's empty template, and must do so exactly when the gene is
    /// registered — a record naming an unregistered gene is an error, and a registered gene a record
    /// omits is one too. The file is the truth: horns are skipped when races are off, and also when
    /// the installed game's heads defeat <see cref="BuildHorns"/>.
    /// </summary>
    public static bool HornGeneShipped(string modDir) => File.Exists(Path.Combine(modDir, HornGeneFile));

    /// <summary>One sex's horns: mesh files by name, the skin-mound blendshape, follow shapes per style.</summary>
    private sealed record HornOutput(
        string Sex, List<(string Name, PdxNode Root)> Meshes, PdxNode Boss,
        Dictionary<string, List<(string Id, string[] Attributes)>> Follows,
        Dictionary<string, List<(string Id, string[] Attributes)>> OrnamentFollows);

    /// <summary>File/pdxmesh stem of one ornament mesh: <c>{sex}_gen_horn_orn_{style}_{shape}</c>.</summary>
    private static string OrnamentMeshName(string sex, string style, string shape) => $"{sex}_gen_horn_orn_{style}_{shape}";

    /// <summary>
    /// Every horn style for one sex, built from the installed game's head: the two-horn mesh per
    /// style (skinned to the skull, carrying the head's own skeleton so bind poses match), one follow
    /// shape per head blendshape that moves the skin at the roots, and the skin mound. The container
    /// is the vanilla teeth file — a portrait mesh with skin and skeleton and nothing else — rewritten.
    /// See <see cref="Horns"/> for the geometry.
    /// </summary>
    private static HornOutput BuildHorns(string portraits, string sex, double scale,
        List<(string Id, float[] P, string[] Attributes)> generated)
    {
        string headDir = Path.Combine(portraits, $"{sex}_head");
        string template = Path.Combine(headDir, $"{sex}_teeth", $"{sex}_teeth.mesh");

        var head = PdxMesh.Read(Path.Combine(headDir, $"{sex}_head.mesh"));
        var headMesh = Find(head, "mesh") ?? throw new InvalidDataException($"{sex}_head.mesh has no mesh node");
        var headSkin = Find(head, "skin") ?? throw new InvalidDataException($"{sex}_head.mesh has no skin node");
        var headSkeleton = Find(head, "skeleton") ?? throw new InvalidDataException($"{sex}_head.mesh has no skeleton");
        float[] hp = headMesh.Floats("p"), hn = headMesh.Floats("n");

        var roots = Horns.Roots(hp, hn, headSkin.Ints("ix"), headSkin.Floats("w"),
            BoneIndex(headSkeleton, "bn_h_forehead_l_side"), BoneIndex(headSkeleton, "bn_h_forehead_r_side"));
        int skull = BoneIndex(headSkeleton, Horns.SkullBone);
        bool flip = !CounterClockwise(hp, hn, headMesh.Ints("tri"));

        // Head blendshapes that move the skin at the roots: one translation per side.
        string headAsset = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(headDir, $"{sex}_head.asset"))).TrimStart('﻿');
        var moves = new List<(string ShapeId, string[] Attributes, OrcTusks.V[] Deltas)>();
        foreach (var (shapeId, q, attributes) in ShapePositions(headDir, headAsset, generated))
        {
            if (q.Length != hp.Length) continue;
            var deltas = roots.Select(r => MeanShift(hp, q, r.Anchor)).ToArray();
            double size = deltas.Max(d => Math.Sqrt(d.LengthSquared));
            if (size > FollowMin && size < FollowMax) moves.Add((shapeId, attributes, deltas));
        }

        var meshes = new List<(string, PdxNode)>();

        // One skinned mesh plus one follow shape per root-moving head blendshape. The follow moves
        // each side rigidly by that side's root shift, so horn and ornament ride the skin alike.
        List<(string, string[])> Emit(string name, string nodeStem, Horns.Tube tube)
        {
            meshes.Add(($"{name}.mesh", HornMesh(template, $"{name}Shape", tube, tube.P, skull, headSkeleton)));
            var list = new List<(string, string[])>();
            int index = 0;
            foreach (var (shapeId, attributes, deltas) in moves)
            {
                string id = $"{name}_follow_{(shapeId.StartsWith(sex + "_", StringComparison.Ordinal) ? shapeId[(sex.Length + 1)..] : shapeId)}";
                var moved = new float[tube.P.Length];
                for (int v = 0; v < tube.P.Length / 3; v++)
                {
                    var d = deltas[tube.SideOf[v]];
                    moved[v * 3] = (float)(tube.P[v * 3] + d.X);
                    moved[v * 3 + 1] = (float)(tube.P[v * 3 + 1] + d.Y);
                    moved[v * 3 + 2] = (float)(tube.P[v * 3 + 2] + d.Z);
                }

                // The node inside the file gets a short name: PdxMesh refuses 64+ characters, the long
                // id is already the file and blend_shape name, and nothing references the node itself.
                meshes.Add(($"{id}.mesh", HornMesh(template, $"{nodeStem}_f{index++}Shape", tube, moved, skull, null)));
                list.Add((id, attributes));
            }

            return list;
        }

        var follows = new Dictionary<string, List<(string, string[])>>();
        foreach (string style in Horns.Styles)
            follows[style] = Emit($"{sex}_gen_horns_{style}", $"{sex}_horn_{style}", Horns.Build(style, roots, scale, hp, hn, flip));

        var ornamentFollows = new Dictionary<string, List<(string, string[])>>();
        foreach (var (style, shape) in Horns.OrnamentMeshes())
        {
            string name = OrnamentMeshName(sex, style, shape);
            ornamentFollows[name] = Emit(name, $"{sex}_ho_{style}_{shape}", Horns.Ornament(style, shape, roots, scale, hp, hn, flip));
        }

        // The skin mound, on a vanilla head blendshape as the container (as the ears are).
        var boss = Horns.Boss(hp, hn, headMesh.Floats("ta"), headMesh.Ints("tri"), roots, scale);
        var bossRoot = PdxMesh.Read(Path.Combine(headDir, "blendshapes", $"{sex}_bs_ear_size_max.mesh"));
        var bossMesh = Find(bossRoot, "mesh") ?? throw new InvalidDataException("ear blendshape has no mesh node");
        if (!bossMesh.Ints("tri").AsSpan().SequenceEqual(headMesh.Ints("tri")))
            throw new InvalidDataException($"{sex}_bs_ear_size_max.mesh no longer matches the head's topology");
        (Find(bossRoot, "object") ?? throw new InvalidDataException("ear blendshape has no object")).Children[0].Name = $"{sex}_bs_gen_horn_bossShape";
        bossMesh.Set("p", PdxProp.Of(boss.P));
        bossMesh.Set("n", PdxProp.Of(boss.N));
        bossMesh.Set("ta", PdxProp.Of(boss.Ta));
        SetBounds(bossMesh, boss.P);

        return new HornOutput(sex, meshes, bossRoot, follows, ornamentFollows);
    }

    /// <summary>
    /// A horn mesh on the teeth file's container. With <paramref name="skeleton"/> it is the skinned
    /// base mesh (every vertex 100% <paramref name="skull"/>, the head's skeleton swapped in); without,
    /// a blendshape (no skin, no skeleton — as vanilla blendshapes are).
    /// </summary>
    private static PdxNode HornMesh(string template, string shapeName, Horns.Tube tube, float[] positions, int skull, PdxNode? skeleton)
    {
        var root = PdxMesh.Read(template);
        var shape = (Find(root, "object") ?? throw new InvalidDataException("teeth mesh has no object node")).Children[0];
        shape.Name = shapeName;
        var mesh = Find(root, "mesh") ?? throw new InvalidDataException("teeth mesh has no mesh node");
        mesh.Set("p", PdxProp.Of(positions));
        mesh.Set("n", PdxProp.Of(tube.N));
        mesh.Set("ta", PdxProp.Of(tube.Ta));
        mesh.Set("u0", PdxProp.Of(tube.Uv));
        mesh.Set("tri", PdxProp.Of(tube.Tri));
        if (Find(mesh, "material") is { } material) material.Set("shader", PdxProp.Of("portrait_attachment"));

        var skin = Find(mesh, "skin");
        if (skeleton is not null && skin is not null)
        {
            int count = positions.Length / 3;
            var ix = new int[count * 4];
            var w = new float[count * 4];
            for (int v = 0; v < count; v++)
            {
                ix[v * 4] = skull; ix[v * 4 + 1] = ix[v * 4 + 2] = ix[v * 4 + 3] = -1;
                w[v * 4] = 1f;
            }

            skin.Set("bones", PdxProp.Of(1));
            skin.Set("ix", PdxProp.Of(ix));
            skin.Set("w", PdxProp.Of(w));
            int at = shape.Children.FindIndex(c => c.Name == "skeleton");
            if (at >= 0) shape.Children[at] = skeleton;
        }
        else
        {
            mesh.Children.RemoveAll(c => c.Name == "skin");
            shape.Children.RemoveAll(c => c.Name == "skeleton");
        }

        SetBounds(mesh, positions);
        return root;
    }

    /// <summary>
    /// Everything the horns ship besides the head asset lines: meshes and the skin mound, the keratin
    /// textures, the asset, the accessory definitions and the accessory gene.
    ///
    /// **Headgear rules** live in the accessory definitions — the first entity whose required tags
    /// are all present wins: a closed helmet (<c>enclosed_helmet</c>) hides the horns; a crown keeps
    /// the full horns, because a horned wearer's crown is swapped for a band (<see cref="WriteHornCrowns"/>);
    /// any other close-fitting headgear (<c>snug_headgear</c>: caps, hoods, most helmets) shows the
    /// filed stump; open hats keep the full horns. The crown line comes before the snug one because
    /// several crowns are tagged both. Filed horns set no tag and have no crown line: they wear the
    /// real crown, stumps hidden inside it — passing is the point of filing.
    /// </summary>
    private static void WriteHorns(string modDir, List<HornOutput> horns)
    {
        string dir = Path.Combine(modDir, HornModelDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);

        // ~240 small files (every style and ornament with its follow shapes, both sexes). Written in
        // parallel: on Windows the cost is file creation, not bytes — sequentially the ornaments
        // alone added ~0.7 s (measured 2026-09-29). Each file is independent.
        Parallel.ForEach(horns.SelectMany(h => h.Meshes), m => PdxMesh.Write(Path.Combine(dir, m.Name), m.Root));
        foreach (var h in horns)
            PdxMesh.Write(Path.Combine(modDir, "gfx", "models", "portraits", $"{h.Sex}_head", "blendshapes", $"{h.Sex}_bs_gen_horn_boss.mesh"), h.Boss);

        // Keratin textures: diffuse generated; normal flat (ridges are geometry; x in R/G, y in A);
        // properties as vanilla's plain olifant ivory — no scattering, no metal — rougher (alpha 120).
        var (w, hgt, diffuse) = Horns.KeratinDiffuse();
        DdsWriter.WriteBgra(Path.Combine(dir, $"{HornTexture}_diffuse.dds"), w, hgt, diffuse);
        DdsWriter.WriteBgra(Path.Combine(dir, $"{HornTexture}_normal.dds"), 4, 4, Solid(0, 128, 128, 128));
        DdsWriter.WriteBgra(Path.Combine(dir, $"{HornTexture}_properties.dds"), 4, 4, Solid(0, 0, 0, 120));

        // Ornament metals: one diffuse per metal over one shared shiny-metal properties map.
        foreach (var (metal, r, g, b) in Horns.Metals)
        {
            var (mw, mh, md) = Horns.MetalDiffuse(r, g, b);
            DdsWriter.WriteBgra(Path.Combine(dir, $"{MetalTexture}_{metal}_diffuse.dds"), mw, mh, md);
        }

        var (pw, ph, pd) = Horns.MetalProperties();
        DdsWriter.WriteBgra(Path.Combine(dir, $"{MetalTexture}_properties.dds"), pw, ph, pd);

        var asset = new StringBuilder();
        foreach (var h in horns)
            foreach (string style in Horns.Styles)
            {
                string name = $"{h.Sex}_gen_horns_{style}";
                asset.Append($"pdxmesh = {{\n\tname = \"{name}_mesh\"\n\tfile = \"{name}.mesh\"\n\n\tmeshsettings = {{\n")
                     .Append($"\t\tname = \"{name}Shape\"\n\t\tindex = 0\n")
                     .Append($"\t\ttexture_diffuse = \"{HornTexture}_diffuse.dds\"\n\t\ttexture_normal = \"{HornTexture}_normal.dds\"\n")
                     .Append($"\t\ttexture_specular = \"{HornTexture}_properties.dds\"\n")
                     .Append("\t\tshader = \"portrait_attachment\"\n\t\tshader_file = \"gfx/FX/jomini/portrait.shader\"\n\t}\n");
                foreach (var (id, _) in h.Follows[style]) asset.Append($"\tblend_shape = {{ id = \"{id}\"\ttype = \"{id}.mesh\" }}\n");
                asset.Append("}\n\n");
                asset.Append($"entity = {{\n\tname = \"{name}_entity\"\n\tpdxmesh = \"{name}_mesh\"\n");
                foreach (var (id, attributes) in h.Follows[style])
                    foreach (string a in attributes) asset.Append($"\tattribute = {{ name = \"{a}\"\t\tblend_shape = \"{id}\" }}\n");
                asset.Append("}\n\n");
            }

        // Ornaments: one pdxmesh per (style, shape), one entity per metal swapping only the diffuse
        // (the pattern of vanilla's eye variants, e.g. male_eyes_dark_iris_entity).
        foreach (var h in horns)
            foreach (var (style, shape) in Horns.OrnamentMeshes())
            {
                string name = OrnamentMeshName(h.Sex, style, shape);
                var follows = h.OrnamentFollows[name];
                asset.Append($"pdxmesh = {{\n\tname = \"{name}_mesh\"\n\tfile = \"{name}.mesh\"\n\n\tmeshsettings = {{\n")
                     .Append($"\t\tname = \"{name}Shape\"\n\t\tindex = 0\n")
                     .Append($"\t\ttexture_diffuse = \"{MetalTexture}_{Horns.Metals[0].Name}_diffuse.dds\"\n\t\ttexture_normal = \"{HornTexture}_normal.dds\"\n")
                     .Append($"\t\ttexture_specular = \"{MetalTexture}_properties.dds\"\n")
                     .Append("\t\tshader = \"portrait_attachment\"\n\t\tshader_file = \"gfx/FX/jomini/portrait.shader\"\n\t}\n");
                foreach (var (id, _) in follows) asset.Append($"\tblend_shape = {{ id = \"{id}\"\ttype = \"{id}.mesh\" }}\n");
                asset.Append("}\n\n");

                foreach (var (metal, _, _, _) in Horns.Metals)
                {
                    asset.Append($"entity = {{\n\tname = \"{name}_{metal}_entity\"\n\tpdxmesh = \"{name}_mesh\"\n")
                         .Append($"\tmeshsettings = {{\n\t\tname = \"{name}Shape\"\n\t\tindex = 0\n")
                         .Append($"\t\ttexture_diffuse = \"{MetalTexture}_{metal}_diffuse.dds\"\n\t}}\n");
                    foreach (var (id, attributes) in follows)
                        foreach (string a in attributes) asset.Append($"\tattribute = {{ name = \"{a}\"\t\tblend_shape = \"{id}\" }}\n");
                    asset.Append("}\n\n");
                }
            }

        File.WriteAllText(Path.Combine(dir, "gen_horns.asset"), asset.ToString(), new UTF8Encoding(false));

        var accessories = new StringBuilder("# Generated: horn accessories. See Emit/RaceHeadWriter.cs WriteHorns for the headgear rules.\n\n");
        foreach (var h in horns)
            foreach (string style in Horns.Styles)
            {
                bool filed = style == Horns.Filed;
                accessories.Append($"{h.Sex}_gen_horns_{style} = {{\n");
                // The style tag tells the ornament accessory which horn it sits on (a character's
                // style often comes from DNA, which no portrait rule can read). Filed stumps get only
                // the style tag, never gen_horns_worn, so they still wear the real crown.
                accessories.Append(filed
                    ? $"\tset_tags = \"{Horns.StyleTag(style)}\"\n"
                    : $"\tset_tags = \"{HornsWornTag},{Horns.StyleTag(style)}\"\n");
                accessories.Append("\tentity = { required_tags = \"enclosed_helmet\" shared_pose_entity = head }\n");
                if (!filed)
                    accessories.Append($"\tentity = {{ required_tags = \"crown\" shared_pose_entity = head entity = \"{h.Sex}_gen_horns_{style}_entity\" }}\n");
                accessories.Append($"\tentity = {{ required_tags = \"snug_headgear\" shared_pose_entity = head entity = \"{h.Sex}_gen_horns_{Horns.Filed}_entity\" }}\n")
                    .Append($"\tentity = {{ required_tags = \"\" shared_pose_entity = head entity = \"{h.Sex}_gen_horns_{style}_entity\" }}\n}}\n\n");
            }

        // Ornament accessories, one per (kind, metal). They mirror the horn lines above, reading the
        // horn's style from its tag: hidden under a closed helmet; on the full horn under a crown;
        // on the stump under other close headgear; otherwise on the horn as worn. No horn tag, nothing.
        // The crown line needs TWO tags at once ("crown,<style tag>") — the comma list Elder Kings 2 uses
        // in gene settings; no vanilla entity line does. Unproven in game until the user sees it.
        foreach (var h in horns)
            foreach (string kind in Horns.OrnamentKinds)
                foreach (var (metal, _, _, _) in Horns.Metals)
                {
                    string Entity(string style) => $"{OrnamentMeshName(h.Sex, style, Horns.ShapeOn(style, kind))}_{metal}_entity";
                    accessories.Append($"{h.Sex}_{Horns.OrnamentTemplateOf(kind, metal)} = {{\n")
                        .Append("\tentity = { required_tags = \"enclosed_helmet\" shared_pose_entity = head }\n");
                    foreach (string style in Horns.Styles.Where(s => s != Horns.Filed))
                        accessories.Append($"\tentity = {{ required_tags = \"crown,{Horns.StyleTag(style)}\" shared_pose_entity = head entity = \"{Entity(style)}\" }}\n");
                    foreach (string style in Horns.Styles.Where(s => s != Horns.Filed))
                        accessories.Append($"\tentity = {{ required_tags = \"snug_headgear,{Horns.StyleTag(style)}\" shared_pose_entity = head entity = \"{Entity(Horns.Filed)}\" }}\n");
                    foreach (string style in Horns.Styles)
                        accessories.Append($"\tentity = {{ required_tags = \"{Horns.StyleTag(style)}\" shared_pose_entity = head entity = \"{Entity(style)}\" }}\n");
                    accessories.Append("\tentity = { required_tags = \"\" shared_pose_entity = head }\n}\n\n");
                }
        string accDir = Path.Combine(modDir, "gfx", "portraits", "accessories");
        Directory.CreateDirectory(accDir);
        ParadoxText.WriteBom(Path.Combine(accDir, "gen_horns.txt"), accessories.ToString());

        var gene = new StringBuilder("""
            # Generated: horns as an accessory gene (Emit/RaceHeadWriter.cs). Shipped only with the horn models,
            # since every template names an accessory; Emit/PortraitWriter.cs pads DNA with it exactly when
            # this file exists. Index 0 is `empty`, the fallback for every record that omits the gene.

            accessory_genes = {

            """);
        gene.Append($"\t{Horns.Gene} = {{\n\t\tinheritable = yes\n\n");
        gene.Append($"\t\t{Horns.NoneTemplate} = {{\n\t\t\tindex = 0\n\t\t\tmale = {{ 1 = empty }}\n\t\t\tfemale = male\n\t\t\tboy = male\n\t\t\tgirl = female\n\t\t}}\n\n");
        for (int i = 0; i < Horns.Styles.Length; i++)
        {
            string style = Horns.Styles[i];
            gene.Append($"\t\t{Horns.TemplateOf(style)} = {{\n\t\t\tindex = {i + 1}\n\t\t\tmale = {{ 1 = male_gen_horns_{style} }}\n")
                .Append($"\t\t\tfemale = {{ 1 = female_gen_horns_{style} }}\n\t\t\tboy = male\n\t\t\tgirl = female\n\t\t}}\n\n");
        }

        gene.Append("\t}\n\n");

        // Ornaments: never inherited — a portrait rule gives them by culture and rank.
        gene.Append($"\t{Horns.OrnamentGene} = {{\n\t\tinheritable = no\n\n");
        gene.Append($"\t\t{Horns.OrnamentNoneTemplate} = {{\n\t\t\tindex = 0\n\t\t\tmale = {{ 1 = empty }}\n\t\t\tfemale = male\n\t\t\tboy = male\n\t\t\tgirl = female\n\t\t}}\n\n");
        int ornamentIndex = 1;
        foreach (string kind in Horns.OrnamentKinds)
            foreach (var (metal, _, _, _) in Horns.Metals)
            {
                string t = Horns.OrnamentTemplateOf(kind, metal);
                gene.Append($"\t\t{t} = {{\n\t\t\tindex = {ornamentIndex++}\n\t\t\tmale = {{ 1 = male_{t} }}\n")
                    .Append($"\t\t\tfemale = {{ 1 = female_{t} }}\n\t\t\tboy = male\n\t\t\tgirl = female\n\t\t}}\n\n");
            }

        gene.Append("\t}\n}\n");
        string genePath = Path.Combine(modDir, HornGeneFile.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(genePath)!);
        ParadoxText.WriteBom(genePath, gene.ToString());
    }

    /// <summary>The portrait tag every worn horn accessory sets (filed stumps excepted).</summary>
    private const string HornsWornTag = "gen_horns_worn";

    /// <summary>
    /// The vanilla accessory whose default entity is the band, per sex: the plain jewelled circlet
    /// of the Western high nobility, base game. It sits 4.5–5 units below the top of the skull, under
    /// every horn root; ibex, forward, nubs and filed all clear it, and the ram's curl passes outside
    /// it, crossing only at the temple.
    /// </summary>
    private static string BandAccessory(string sex) => $"{sex}_headgear_secular_western_high_nobility_01";

    /// <summary>
    /// Crown-tagged vanilla headgear that is already a band and keeps its own look on a horned head:
    /// no horn style but the ram comes within 0.6 units of it, and its top stays below the skull's.
    /// Measured against every generated horn mesh by <c>Desktop/ck3devtools/crown_survey/survey.py</c>
    /// (2026-09-28, 102 crown-tagged accessories); the four western circlets include the bands
    /// themselves. A crown missing from this list is swapped, so a new DLC's crowns are banded too.
    /// </summary>
    private static readonly HashSet<string> BandLikeCrowns =
    [
        "male_headgear_secular_western_high_nobility_01",
        "female_headgear_secular_western_high_nobility_01",
        "f_headgear_sec_ep2_western_era1_hi_nob_01",
        "f_headgear_sec_sp2_western_hi_nob_01",
        "m_headgear_sec_ccp_emishi_com_01",
        "f_headgear_sec_ccp_emishi_com_01",
        "m_headgear_rel_ccp_emishi_com_01",
        "female_headgear_religious_northern_high_01",
        "f_headgear_sec_mpo_mongol_nob_01",
    ];

    /// <summary>
    /// Crowns become bands on horned heads. Every vanilla headgear accessory tagged <c>crown</c> is
    /// re-declared, verbatim, in a file that loads after vanilla's (accessories are one flat folder,
    /// read in filename order, and a later definition of a key replaces the earlier — AGOT's
    /// <c>epe_bodyparts.txt</c> redefines vanilla eyes the same way), with one entity line added
    /// before its default: when the horns' tag is present, draw the band instead.
    ///
    /// The swap happens at the entity, not in a portrait modifier, because only the accessory knows
    /// which crown vanilla's headgear groups actually picked: a trigger cannot read another gene's
    /// accessory. So the choice of headgear — rank, culture, era, DLC — is untouched; a horned king
    /// wears his crown's band, a horned count in a cap still wears the cap, and a hood or a mask still
    /// takes precedence (those lines stay first). The crown's own tags stay too: a crown that hides
    /// the hair (<c>no_hair</c>) still does, under the band.
    ///
    /// Copied from the installed game at generation, so a patch that changes a crown is picked up by
    /// regenerating. Returns how many crowns were re-declared; 0 with a warning if the band is missing.
    /// </summary>
    private static int WriteHornCrowns(string modDir, string gameDir, List<string> log)
    {
        string accDir = Path.Combine(gameDir, "gfx", "portraits", "accessories");
        if (!Directory.Exists(accDir)) return 0;

        // Last definition wins, in the engine's load order: filename order.
        var declared = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(accDir, "*.txt").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            string[] lines = File.ReadAllLines(file);
            foreach (var (key, first, last, closed) in ScriptScan.TopLevelDeclarations(lines, c => char.IsLetterOrDigit(c) || c == '_'))
                if (closed) declared[key] = lines[first..(last + 1)];
        }

        var band = new Dictionary<string, string>();
        foreach (string sex in Sexes)
        {
            if (declared.TryGetValue(BandAccessory(sex), out var body) && DefaultEntity(body) is { } entity) band[sex] = entity;
            else
            {
                log.Add($"  WARNING: horned heads keep their crowns: {BandAccessory(sex)} is not in this game version");
                return 0;
            }
        }

        var text = new StringBuilder("""
            # Generated (Emit/RaceHeadWriter.cs WriteHornCrowns): every vanilla crown, copied from the installed
            # game, with one line added — a horned head (the gen_horns_worn tag, set by the horn accessories in
            # gen_horns.txt) wears the plain circlet instead. Everything else is vanilla's. Loads after vanilla's
            # accessory files, so these definitions replace theirs.


            """);
        int count = 0;
        foreach (var (key, body) in declared)
        {
            if (BandLikeCrowns.Contains(key) || !IsCrown(body)) continue;
            string? sex = key.StartsWith("female_", StringComparison.Ordinal) || key.StartsWith("f_", StringComparison.Ordinal) ? "female"
                : key.StartsWith("male_", StringComparison.Ordinal) || key.StartsWith("m_", StringComparison.Ordinal) ? "male" : null;
            int at = Array.FindIndex(body, l => IsDefaultEntityLine(l));
            if (sex is null || at < 0) continue;

            for (int i = 0; i < body.Length; i++)
            {
                if (i == at)
                    text.Append($"\tentity = {{ required_tags = \"{HornsWornTag}\"\tshared_pose_entity = head\t\tentity = {band[sex]} }}\n");
                text.Append(body[i].TrimStart('﻿')).Append('\n');
            }

            text.Append('\n');
            count++;
        }

        string outDir = Path.Combine(modDir, "gfx", "portraits", "accessories");
        Directory.CreateDirectory(outDir);
        ParadoxText.WriteBom(Path.Combine(outDir, "zz_gen_horn_crowns.txt"), text.ToString());
        return count;

        static bool IsCrown(string[] body) => body.Any(l =>
        {
            var m = Regex.Match(ScriptScan.StripComment(l), @"\bset_tags\s*=\s*""([^""]*)""");
            return m.Success && m.Groups[1].Value.Split(',').Any(t => t.Trim() == "crown");
        });
    }

    /// <summary>The default (<c>required_tags = ""</c>) entity line of an accessory body.</summary>
    private static bool IsDefaultEntityLine(string line) =>
        Regex.IsMatch(ScriptScan.StripComment(line), @"^\s*entity\s*=\s*\{\s*required_tags\s*=\s*""""");

    /// <summary>The entity the default line of an accessory body draws, or null.</summary>
    private static string? DefaultEntity(string[] body) =>
        body.Where(IsDefaultEntityLine)
            .Select(l => Regex.Match(ScriptScan.StripComment(l), @"\bentity\s*=\s*""?(\w+)""?\s*\}?\s*$"))
            .Where(m => m.Success).Select(m => m.Groups[1].Value).FirstOrDefault();

    private static byte[] Solid(byte b, byte g, byte r, byte a)
    {
        var bytes = new byte[4 * 4 * 4];
        for (int i = 0; i < 16; i++) { bytes[i * 4] = b; bytes[i * 4 + 1] = g; bytes[i * 4 + 2] = r; bytes[i * 4 + 3] = a; }
        return bytes;
    }

    /// <summary>Whether a mesh winds counter-clockwise about its normals (majority vote).</summary>
    private static bool CounterClockwise(float[] p, float[] n, int[] tri)
    {
        int score = 0;
        for (int i = 0; i + 2 < tri.Length; i += 3)
        {
            OrcTusks.V At(float[] a, int v) => new(a[v * 3], a[v * 3 + 1], a[v * 3 + 2]);
            var fn = OrcTusks.V.Cross(At(p, tri[i + 1]) - At(p, tri[i]), At(p, tri[i + 2]) - At(p, tri[i]));
            score += OrcTusks.V.Dot(fn, At(n, tri[i]) + At(n, tri[i + 1]) + At(n, tri[i + 2])) > 0 ? 1 : -1;
        }

        return score >= 0;
    }

    // ---- Orc tusks ---------------------------------------------------------------------------

    /// <summary>Everything one sex's teeth folder gets: mesh files by name, and the patched asset.</summary>
    private sealed record TeethOutput(string Sex, List<(string Name, PdxNode Root)> Meshes, byte[] Asset, int Follows);

    /// <summary>
    /// Lip movement worth mirroring onto the tusk, in head units. The ceiling drops shapes like
    /// <c>no_portrait</c>, which throw the whole face 100+ units away and hide the teeth anyway.
    /// </summary>
    private const double FollowMin = 0.02, FollowMax = 5.0;

    /// <summary>
    /// One sex's teeth, with the tusks of <see cref="OrcTusks"/>:
    /// <list type="bullet">
    /// <item><c>{sex}_teeth.mesh</c> — vanilla plus both tusks, collapsed on their roots, skinned to the lip.</item>
    /// <item>every vanilla <c>{sex}_teeth_bs_*.mesh</c> — the same vertices appended, unmoved (the
    /// tusk rides the lip, not the teeth), since a blendshape must match the mesh's vertex count.</item>
    /// <item><c>{sex}_teeth_bs_gen_orc_tusks.mesh</c> — standard tusks full-grown, and one
    /// <c>{sex}_teeth_bs_gen_orc_tusks_{variant}.mesh</c> per other <see cref="OrcTusks.Variants"/>.</item>
    /// <item><c>{sex}_teeth_bs_gen_follow_*.mesh</c> — one per head blendshape that moves the lip the
    /// tusk comes out of, translating the tusk by the same amount; the teeth asset maps it under the
    /// head's own attribute names, so the lip and the tusk always move together. About 13 per sex:
    /// lip fullness and width, the ageing stages, infant, gaunt and a few special looks.</item>
    /// </list>
    /// Bone-driven lip movement (chin and jaw genes, expressions) needs no mirror: the tusk carries
    /// the lip's own skin weights. The teeth and head skeletons list the same bones in the same
    /// order, so bone ids copy across; the male teeth file's bind poses sit ~114.8 units off the
    /// head's, but consistently for every bone, so they behave exactly as its jaw weights always have.
    /// </summary>
    private static TeethOutput BuildTeeth(string headDir, string sex, double scale,
        List<(string Id, float[] P, string[] Attributes)> generated)
    {
        string teethDir = Path.Combine(headDir, $"{sex}_teeth");
        string basePath = Path.Combine(teethDir, $"{sex}_teeth.mesh");

        var head = PdxMesh.Read(Path.Combine(headDir, $"{sex}_head.mesh"));
        var headMesh = Find(head, "mesh") ?? throw new InvalidDataException($"{sex}_head.mesh has no mesh node");
        var headSkin = Find(head, "skin") ?? throw new InvalidDataException($"{sex}_head.mesh has no skin node");
        var headSkeleton = Find(head, "skeleton") ?? throw new InvalidDataException($"{sex}_head.mesh has no skeleton");
        float[] headP = headMesh.Floats("p");

        var root = PdxMesh.Read(basePath);
        var mesh = Find(root, "mesh") ?? throw new InvalidDataException($"{sex}_teeth.mesh has no mesh node");
        var skin = Find(root, "skin") ?? throw new InvalidDataException($"{sex}_teeth.mesh has no skin node");
        var skeleton = Find(root, "skeleton") ?? throw new InvalidDataException($"{sex}_teeth.mesh has no skeleton");
        if (!skeleton.Children.Select(b => b.Name).SequenceEqual(headSkeleton.Children.Select(b => b.Name)))
            throw new InvalidDataException($"{sex}_teeth and {sex}_head no longer share a bone list");

        int vertexCount = mesh.Floats("p").Length / 3;
        int per = skin.Ints("ix").Length / Math.Max(1, vertexCount);
        int used = skin.Ints("bones") is [var b, ..] ? b : per;

        List<OrcTusks.Tusk> Grown(OrcTusks.Variant variant) => OrcTusks.Build(mesh.Floats("p"), mesh.Floats("n"),
            mesh.Ints("tri"), skin.Ints("ix"), skin.Floats("w"), per, BoneIndex(skeleton, OrcTusks.JawBone),
            BoneIndex(skeleton, OrcTusks.UpperTeethBone), headP, scale, variant);

        // Every variant from the untouched vanilla streams (before AppendTusks grows the mesh), all
        // with the same roots, exits and vertex counts — the standard one carries the base mesh.
        var grown = OrcTusks.Variants.Select(v => (Variant: v, Tusks: Grown(v))).ToList();
        var tusks = grown[0].Tusks;
        var anchors = tusks.Select(t => OrcTusks.LipAnchor(headP, headSkin.Ints("ix"), headSkin.Floats("w"),
            BoneIndex(headSkeleton, OrcTusks.LowerLipBone(t.Side)), t.Exit, used)).ToList();

        OrcTusks.V Collapsed(int tusk, int vertex) => tusks[tusk].Root;
        var meshes = new List<(string, PdxNode)>();

        AppendTusks(mesh, skin, tusks, anchors, per, Collapsed);
        meshes.Add(($"{sex}_teeth.mesh", root));

        foreach (string path in Directory.GetFiles(teethDir, $"{sex}_teeth_bs_*.mesh").Order(StringComparer.Ordinal))
        {
            var bs = PdxMesh.Read(path);
            var bm = Find(bs, "mesh") ?? throw new InvalidDataException($"{Path.GetFileName(path)} has no mesh node");
            if (bm.Floats("p").Length / 3 != vertexCount)
                throw new InvalidDataException($"{Path.GetFileName(path)} no longer matches {sex}_teeth.mesh");
            AppendTusks(bm, null, tusks, anchors, per, Collapsed);
            meshes.Add((Path.GetFileName(path), bs));
        }

        // One grown shape per variant. The standard one keeps the original id, so the lower template
        // and every existing DNA keep meaning exactly what they did.
        var variantShapes = new List<(string Id, string Attribute)>();
        foreach (var (variant, shape) in grown)
        {
            if (!shape.Select(t => t.VertexCount).SequenceEqual(tusks.Select(t => t.VertexCount)))
                throw new InvalidDataException($"tusk variant {variant.Name} does not match the standard tusk's vertices");
            string id = variant.Name == "lower" ? $"{sex}_teeth_bs_gen_orc_tusks" : $"{sex}_teeth_bs_gen_orc_tusks_{variant.Name}";
            var shapeRoot = AsBlendShape(PdxMesh.Read(basePath), $"{id}Shape", out var grownMesh);
            AppendTusks(grownMesh, null, shape, anchors, per,
                (t, v) => new OrcTusks.V(shape[t].P[v * 3], shape[t].P[v * 3 + 1], shape[t].P[v * 3 + 2]));
            meshes.Add(($"{id}.mesh", shapeRoot));
            variantShapes.Add((id, OrcTusks.AttributeOf(variant.Name)));
        }

        var follows = new List<(string Id, string[] Attributes)>();
        string headAsset = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(headDir, $"{sex}_head.asset"))).TrimStart('﻿');
        foreach (var (shapeId, q, attributes) in ShapePositions(headDir, headAsset, generated))
        {
            if (q.Length != headP.Length) continue;

            var deltas = anchors.Select(a => MeanShift(headP, q, a.HeadVertices)).ToList();
            double size = deltas.Max(d => Math.Sqrt(d.LengthSquared));
            if (size <= FollowMin || size >= FollowMax) continue;

            string id = $"{sex}_teeth_bs_gen_follow_{(shapeId.StartsWith(sex + "_", StringComparison.Ordinal) ? shapeId[(sex.Length + 1)..] : shapeId)}";
            var follow = AsBlendShape(PdxMesh.Read(basePath), $"{id}Shape", out var followMesh);
            AppendTusks(followMesh, null, tusks, anchors, per, (t, v) => tusks[t].Root + deltas[t]);
            meshes.Add(($"{id}.mesh", follow));
            follows.Add((id, attributes));
        }

        var asset = PatchTeethAsset(File.ReadAllBytes(Path.Combine(teethDir, $"{sex}_teeth.asset")), sex, variantShapes, follows);
        return new TeethOutput(sex, meshes, asset, follows.Count);
    }

    /// <summary>
    /// Appends both tusks to one mesh node — and to its skin, when given — placing tusk t's vertex v at
    /// <paramref name="where"/>(t, v). Every tusk vertex carries its tusk's lip weights.
    /// </summary>
    private static void AppendTusks(
        PdxNode mesh, PdxNode? skin, List<OrcTusks.Tusk> tusks, List<OrcTusks.Anchor> anchors, int per,
        Func<int, int, OrcTusks.V> where)
    {
        var p = mesh.Floats("p").ToList(); var n = mesh.Floats("n").ToList(); var ta = mesh.Floats("ta").ToList();
        var u = mesh.Floats("u0").ToList(); var tri = mesh.Ints("tri").ToList();
        var ix = skin?.Ints("ix").ToList(); var w = skin?.Floats("w").ToList();

        for (int t = 0; t < tusks.Count; t++)
        {
            var tusk = tusks[t];
            int offset = p.Count / 3;
            for (int v = 0; v < tusk.VertexCount; v++)
            {
                var at = where(t, v);
                p.AddRange([(float)at.X, (float)at.Y, (float)at.Z]);
                n.AddRange(tusk.N.AsSpan(v * 3, 3));
                ta.AddRange(tusk.Ta.AsSpan(v * 4, 4));
                u.AddRange(tusk.Uv.AsSpan(v * 2, 2));
                if (ix is null || w is null) continue;
                for (int k = 0; k < per; k++)
                {
                    ix.Add(k < anchors[t].Bones.Length ? anchors[t].Bones[k] : -1);
                    w.Add(k < anchors[t].Weights.Length ? anchors[t].Weights[k] : 0f);
                }
            }

            tri.AddRange(tusk.Tri.Select(i => i + offset));
        }

        mesh.Set("p", PdxProp.Of([.. p]));
        mesh.Set("n", PdxProp.Of([.. n]));
        mesh.Set("ta", PdxProp.Of([.. ta]));
        mesh.Set("u0", PdxProp.Of([.. u]));
        mesh.Set("tri", PdxProp.Of([.. tri]));
        if (skin is not null && ix is not null && w is not null)
        {
            skin.Set("ix", PdxProp.Of([.. ix]));
            skin.Set("w", PdxProp.Of([.. w]));
        }

        SetBounds(mesh, [.. p]);
    }

    /// <summary>
    /// A base mesh tree turned into a blendshape container: vanilla blendshapes carry no skin under
    /// the mesh and no skeleton beside it.
    /// </summary>
    private static PdxNode AsBlendShape(PdxNode root, string shapeName, out PdxNode mesh)
    {
        var obj = Find(root, "object") ?? throw new InvalidDataException("teeth mesh has no object node");
        var shape = obj.Children[0];
        shape.Name = shapeName;
        shape.Children.RemoveAll(c => c.Name == "skeleton");
        mesh = Find(root, "mesh") ?? throw new InvalidDataException("teeth mesh has no mesh node");
        mesh.Children.RemoveAll(c => c.Name == "skin");
        return root;
    }

    private static OrcTusks.V MeanShift(float[] from, float[] to, int[] vertices)
    {
        double x = 0, y = 0, z = 0;
        foreach (int v in vertices)
        {
            x += to[v * 3] - from[v * 3];
            y += to[v * 3 + 1] - from[v * 3 + 1];
            z += to[v * 3 + 2] - from[v * 3 + 2];
        }

        return new OrcTusks.V(x / vertices.Length, y / vertices.Length, z / vertices.Length);
    }

    /// <summary>
    /// Positions of every head blendshape a tusk or horn must follow: vanilla's (from the asset), then
    /// ours. Ours are not in vanilla's asset, so reading only that let a generated shape move the skin
    /// out from under a horn root or a tusk's lip — the giantkin face moves both.
    /// </summary>
    private static IEnumerable<(string Id, float[] P, string[] Attributes)> ShapePositions(
        string headDir, string headAsset, List<(string Id, float[] P, string[] Attributes)> generated)
    {
        foreach (var (shapeId, file, attributes) in PositionShapes(headAsset))
        {
            string shapePath = Path.Combine(headDir, file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(shapePath)) continue;
            yield return (shapeId, (Find(PdxMesh.Read(shapePath), "mesh") ?? throw new InvalidDataException($"{file} has no mesh node")).Floats("p"), attributes);
        }

        foreach (var g in generated) yield return g;
    }

    /// <summary>
    /// The head's blendshapes that move positions, each with its file and the attributes that drive
    /// it. <c>data = "normal|tangent"</c> shapes (the nbs_ ones) move nothing and are skipped, as are
    /// shapes no attribute drives.
    /// </summary>
    private static List<(string Id, string File, string[] Attributes)> PositionShapes(string headAsset)
    {
        var attributes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(headAsset,
                     @"attribute\s*=\s*\{\s*name\s*=\s*""([^""]+)""\s*blend_shape\s*=\s*""([^""]+)"""))
        {
            if (!attributes.TryGetValue(m.Groups[2].Value, out var list)) attributes[m.Groups[2].Value] = list = [];
            list.Add(m.Groups[1].Value);
        }

        var shapes = new List<(string, string, string[])>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(headAsset, @"blend_shape\s*=\s*\{([^}]*)\}"))
        {
            string body = m.Groups[1].Value;
            var id = System.Text.RegularExpressions.Regex.Match(body, @"id\s*=\s*""([^""]+)""");
            var type = System.Text.RegularExpressions.Regex.Match(body, @"type\s*=\s*""([^""]+)""");
            if (!id.Success || !type.Success || body.Contains("data", StringComparison.Ordinal)) continue;
            if (attributes.TryGetValue(id.Groups[1].Value, out var names))
                shapes.Add((id.Groups[1].Value, type.Groups[1].Value, [.. names]));
        }

        return shapes;
    }

    /// <summary>
    /// Vanilla's teeth asset with the tusk shape and the follow shapes declared in the pdxmesh, their
    /// attributes in EVERY entity (normal and blackened teeth), and the shader swapped.
    ///
    /// **The shader swap is what makes the tusks appear at all.** portrait.shader declares
    /// <c>Effect portrait_teeth</c> twice, the first without <c>PDX_MESH_BLENDSHAPES</c>, and teeth
    /// drawn with it ignore blendshapes: the tusks stayed collapsed in game, with nothing in the log.
    /// <c>portrait_skin</c> is the same VS_standard + PS_skin pair with blendshapes on — what Elder
    /// Kings' tusked teeth use. A side effect is that vanilla's own teeth blendshape
    /// (<c>teeth_bs_lower_down</c>) now applies too.
    /// </summary>
    private static byte[] PatchTeethAsset(byte[] raw, string sex, List<(string Id, string Attribute)> tuskShapes,
        List<(string Id, string[] Attributes)> follows)
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        bool hasBom = raw.AsSpan().StartsWith(bom);
        string text = Encoding.UTF8.GetString(raw, hasBom ? 3 : 0, raw.Length - (hasBom ? 3 : 0));
        if (!text.Contains("shader = \"portrait_teeth\"", StringComparison.Ordinal))
            throw new InvalidDataException($"{sex}_teeth.asset no longer uses the portrait_teeth shader");
        text = text.Replace("shader = \"portrait_teeth\"", "shader = \"portrait_skin\"", StringComparison.Ordinal);

        string nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split(nl).ToList();

        var shapeLines = tuskShapes.Select(t => $"\tblend_shape = {{ id = \"{t.Id}\"\ttype = \"{t.Id}.mesh\" }}\t# Ck3MapGen orc tusks").ToList();
        var attributeLines = tuskShapes.Select(t => $"\tattribute = {{ name = \"{t.Attribute}\"\t\tblend_shape = \"{t.Id}\" }}\t# Ck3MapGen orc tusks").ToList();
        foreach (var (id, attributes) in follows)
        {
            shapeLines.Add($"\tblend_shape = {{ id = \"{id}\"\ttype = \"{id}.mesh\" }}\t# Ck3MapGen tusk follows lip");
            attributeLines.AddRange(attributes.Select(a => $"\tattribute = {{ name = \"{a}\"\t\tblend_shape = \"{id}\" }}\t# Ck3MapGen tusk follows lip"));
        }

        static bool IsAttribute(string l) => l.TrimStart().StartsWith("attribute = {", StringComparison.Ordinal);

        // Bottom-up, so earlier indices stay valid: after the last line of every attribute run...
        int runs = 0;
        for (int i = lines.Count - 1; i >= 0; i--)
            if (IsAttribute(lines[i]) && (i + 1 >= lines.Count || !IsAttribute(lines[i + 1])))
            {
                lines.InsertRange(i + 1, attributeLines);
                runs++;
            }

        // ...then after the last blend_shape, which sits above them all in the pdxmesh.
        int lastShape = lines.FindLastIndex(l => l.TrimStart().StartsWith("blend_shape = {", StringComparison.Ordinal));
        if (lastShape < 0 || runs == 0)
            throw new InvalidDataException($"{sex}_teeth.asset no longer has a blend_shape list and attribute lists");
        lines.InsertRange(lastShape + 1, shapeLines);

        byte[] body = Encoding.UTF8.GetBytes(string.Join(nl, lines));
        return hasBom ? [.. bom, .. body] : body;
    }

    /// <summary>
    /// The blendshape file. A vanilla ear blendshape is the container — same streams, same material
    /// node, no skin or skeleton — so every field is one the engine already accepts, and only the
    /// shape name and the moved streams change. Its topology is checked against the head first.
    /// </summary>
    private static (PdxNode Root, PointedEars.Result Shape) BuildShape(
        PdxNode head, string templatePath, EarShape shape, string shapeName)
    {
        var mesh = Find(head, "mesh") ?? throw new InvalidDataException("head has no mesh node");
        var skin = Find(head, "skin") ?? throw new InvalidDataException("head has no skin node");
        var skeleton = Find(head, "skeleton") ?? throw new InvalidDataException("head has no skeleton");

        int left = BoneIndex(skeleton, PointedEars.LeftEarBone);
        int right = BoneIndex(skeleton, PointedEars.RightEarBone);
        // The stride is the array length over the vertex count, NOT the `bones` value: that records
        // how many influences are in use (3 on the vanilla teeth) while the stream is always 4 wide.
        int influences = skin.Ints("ix").Length / Math.Max(1, mesh.Floats("p").Length / 3);

        var result = PointedEars.Shape(
            mesh.Floats("p"), mesh.Floats("n"), mesh.Floats("ta"), mesh.Ints("tri"),
            skin.Ints("ix"), skin.Floats("w"), influences, left, right, shape);

        var template = PdxMesh.Read(templatePath);
        var tm = Find(template, "mesh") ?? throw new InvalidDataException($"{templatePath} has no mesh node");
        if (!tm.Ints("tri").AsSpan().SequenceEqual(mesh.Ints("tri")) ||
            !tm.Floats("u0").AsSpan().SequenceEqual(mesh.Floats("u0")))
            throw new InvalidDataException($"{Path.GetFileName(templatePath)} no longer matches the head's topology");

        var obj = Find(template, "object") ?? throw new InvalidDataException($"{templatePath} has no object node");
        if (obj.Children.Count != 1) throw new InvalidDataException($"{templatePath} holds {obj.Children.Count} shapes, expected 1");
        obj.Children[0].Name = shapeName;

        tm.Set("p", PdxProp.Of(result.P));
        tm.Set("n", PdxProp.Of(result.N));
        tm.Set("ta", PdxProp.Of(result.Ta));
        SetBounds(tm, result.P);

        return (template, result);
    }

    private static void SetBounds(PdxNode mesh, float[] p)
    {
        float[] lo = [float.MaxValue, float.MaxValue, float.MaxValue];
        float[] hi = [float.MinValue, float.MinValue, float.MinValue];
        for (int i = 0; i < p.Length; i++)
        {
            lo[i % 3] = Math.Min(lo[i % 3], p[i]);
            hi[i % 3] = Math.Max(hi[i % 3], p[i]);
        }

        float[] c = [(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2];
        double r = 0;
        for (int v = 0; v < p.Length / 3; v++)
        {
            double dx = p[v * 3] - c[0], dy = p[v * 3 + 1] - c[1], dz = p[v * 3 + 2] - c[2];
            r = Math.Max(r, Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        mesh.Set("boundingsphere", PdxProp.Of(c[0], c[1], c[2], (float)r));
        var aabb = Find(mesh, "aabb");
        if (aabb is null) return;
        aabb.Set("min", PdxProp.Of(lo));
        aabb.Set("max", PdxProp.Of(hi));
    }

    /// <summary>
    /// Vanilla's head asset, in the file's own line endings, with: per ear style, one
    /// <c>blend_shape</c> line after the last blend_shape and one <c>attribute</c> line after the last
    /// attribute; and, with tusks, the tusk attribute declared on the head as <c>{sex}_bs_neutral</c> —
    /// what vanilla does for its own teeth attributes (<c>teeth_bs_lower_down</c>), and EK2 for its tusks.
    /// </summary>
    private static byte[] PatchAsset(byte[] raw, string sex, bool withEars, bool withTusks, bool withHorns, FaceShape[] faces)
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        bool hasBom = raw.AsSpan().StartsWith(bom);
        string text = Encoding.UTF8.GetString(raw, hasBom ? 3 : 0, raw.Length - (hasBom ? 3 : 0));
        string nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split(nl).ToList();

        int lastShape = lines.FindLastIndex(l => l.TrimStart().StartsWith("blend_shape = {", StringComparison.Ordinal));
        int lastAttribute = lines.FindLastIndex(l => l.TrimStart().StartsWith("attribute = {", StringComparison.Ordinal));
        if (lastShape < 0 || lastAttribute < 0 || lastShape > lastAttribute)
            throw new InvalidDataException($"{sex}_head.asset no longer has a blend_shape list followed by an attribute list");

        // Attribute first: it sits later in the file, so inserting it leaves lastShape valid.
        var attributes = new List<string>();
        if (withEars)
            attributes.AddRange(Styles.Select(s =>
                $"\tattribute = {{ name = \"{AttributeOf(s.Style)}\"\t\tblend_shape = \"{BlendShapeId(sex, s.Style)}\" }}\t# Ck3MapGen race head"));
        if (withTusks)
        {
            if (!text.Contains($"id = \"{sex}_bs_neutral\"", StringComparison.Ordinal))
                throw new InvalidDataException($"{sex}_head.asset no longer has {sex}_bs_neutral");
            attributes.AddRange(OrcTusks.Variants.Select(v =>
                $"\tattribute = {{ name = \"{OrcTusks.AttributeOf(v.Name)}\"\t\tblend_shape = \"{sex}_bs_neutral\" }}\t# Ck3MapGen orc tusks (teeth attribute)"));
        }

        if (withHorns)
            attributes.Add($"\tattribute = {{ name = \"{Horns.BossAttribute}\"\t\tblend_shape = \"{sex}_bs_gen_horn_boss\" }}\t# Ck3MapGen horns (skin mound)");

        foreach (var face in faces)
            attributes.Add($"\tattribute = {{ name = \"{face.Attribute}\"\t\tblend_shape = \"{face.Id(sex)}\" }}\t# Ck3MapGen {face.Label}");

        lines.InsertRange(lastAttribute + 1, attributes);

        var shapes = new List<string>();
        if (withEars)
            shapes.AddRange(Styles.Select(s =>
                $"\t\tblend_shape = {{ id = \"{BlendShapeId(sex, s.Style)}\"\t\ttype = \"blendshapes/{BlendShapeId(sex, s.Style)}.mesh\" }}\t# Ck3MapGen race head"));
        if (withHorns)
            shapes.Add($"\t\tblend_shape = {{ id = \"{sex}_bs_gen_horn_boss\"\t\ttype = \"blendshapes/{sex}_bs_gen_horn_boss.mesh\" }}\t# Ck3MapGen horns (skin mound)");
        foreach (var face in faces)
            shapes.Add($"\t\tblend_shape = {{ id = \"{face.Id(sex)}\"\t\ttype = \"blendshapes/{face.Id(sex)}.mesh\" }}\t# Ck3MapGen {face.Label}");
        lines.InsertRange(lastShape + 1, shapes);

        byte[] body = Encoding.UTF8.GetBytes(string.Join(nl, lines));
        return hasBom ? [.. bom, .. body] : body;
    }

    private static int BoneIndex(PdxNode skeleton, string name)
    {
        foreach (var bone in skeleton.Children)
            if (bone.Name == name && bone.Ints("ix") is [var ix, ..])
                return ix;
        throw new InvalidDataException($"head skeleton has no {name}");
    }

    /// <summary>
    /// Depth-first search by node name. Not <see cref="PdxNode.Child"/>, which creates the node
    /// when it is missing — the wrong answer for a file being validated.
    /// </summary>
    private static PdxNode? Find(PdxNode node, string name)
    {
        if (node.Name == name) return node;
        foreach (var child in node.Children)
            if (Find(child, name) is { } hit)
                return hit;
        return null;
    }
}
