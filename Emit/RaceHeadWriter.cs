using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Emits the race head shapes — the elves' pointed ears (one style shared by high elves, wood elves
/// and deepkin) as blendshapes of the vanilla head, and the orcs' tusks as geometry added to the
/// vanilla teeth — plus the head and teeth assets that declare them:
///
/// <code>
/// gfx/models/portraits/{male,female}_head/{sex}_head.asset                       (vanilla + 3 lines)
/// gfx/models/portraits/{male,female}_head/blendshapes/{sex}_bs_gen_elf_ears_high.mesh
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
    ];

    /// <summary>The attribute a gene template sets and the head asset maps to a blendshape.</summary>
    public static string AttributeOf(string style) => $"gen_bs_elf_ears_{style}";

    private static string BlendShapeId(string sex, string style) => $"{sex}_bs_gen_elf_ears_{style}";

    private static readonly string[] Sexes = ["male", "female"];

    public static void WriteAll(string modDir, string gameDir, MapConfig cfg)
    {
        if (!RaceMorphWriter.RacesOn(cfg)) return;

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
            Console.WriteLine($"  WARNING: pointed ears skipped, elves keep round ears: {e.Message}");
            ears.Clear();
        }

        var teeth = new List<TeethOutput>();
        try
        {
            foreach (string sex in Sexes)
                teeth.Add(BuildTeeth(Path.Combine(portraits, $"{sex}_head"), sex, sex == "female" ? 0.9 : 1.0));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.WriteLine($"  WARNING: orc tusks skipped, orcs keep human teeth: {e.Message}");
            teeth.Clear();
        }

        if (ears.Count == 0 && teeth.Count == 0) return;

        var assets = new List<(string Path, byte[] Bytes)>();
        try
        {
            foreach (string sex in Sexes)
                assets.Add((Path.Combine(outPortraits, $"{sex}_head", $"{sex}_head.asset"),
                    PatchAsset(File.ReadAllBytes(Path.Combine(portraits, $"{sex}_head", $"{sex}_head.asset")), sex,
                        ears.Count > 0, teeth.Count > 0)));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.WriteLine($"  WARNING: race head shapes skipped, the head asset could not be patched: {e.Message}");
            return;
        }

        foreach (var (path, root, _) in ears) PdxMesh.Write(path, root);
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

        if (ears.Count > 0)
            Console.WriteLine($"  pointed ears written: {ears.Count} blendshapes " +
                              $"(tip travel {ears.Max(m => m.Shape.MaxShift):0.00}, " +
                              $"{ears.Min(m => m.Shape.Moved)}-{ears.Max(m => m.Shape.Moved)} vertices moved)");
        if (teeth.Count > 0)
            Console.WriteLine($"  orc tusks written: {string.Join(", ", teeth.Select(t => $"{t.Sex} {t.Meshes.Count} teeth meshes, {t.Follows} lip-follow shapes"))}");
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
    /// <item><c>{sex}_teeth_bs_gen_orc_tusks.mesh</c> — tusks full-grown.</item>
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
    private static TeethOutput BuildTeeth(string headDir, string sex, double scale)
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

        var tusks = OrcTusks.Build(mesh.Floats("p"), mesh.Floats("n"), mesh.Ints("tri"), skin.Ints("ix"), skin.Floats("w"),
            per, BoneIndex(skeleton, OrcTusks.JawBone), BoneIndex(skeleton, OrcTusks.UpperTeethBone), headP, scale);
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

        string tuskId = $"{sex}_teeth_bs_gen_orc_tusks";
        var grown = AsBlendShape(PdxMesh.Read(basePath), $"{tuskId}Shape", out var grownMesh);
        AppendTusks(grownMesh, null, tusks, anchors, per,
            (t, v) => new OrcTusks.V(tusks[t].P[v * 3], tusks[t].P[v * 3 + 1], tusks[t].P[v * 3 + 2]));
        meshes.Add(($"{tuskId}.mesh", grown));

        var follows = new List<(string Id, string[] Attributes)>();
        string headAsset = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(headDir, $"{sex}_head.asset"))).TrimStart('﻿');
        foreach (var (shapeId, file, attributes) in PositionShapes(headAsset))
        {
            string shapePath = Path.Combine(headDir, file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(shapePath)) continue;
            var q = (Find(PdxMesh.Read(shapePath), "mesh") ?? throw new InvalidDataException($"{file} has no mesh node")).Floats("p");
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

        var asset = PatchTeethAsset(File.ReadAllBytes(Path.Combine(teethDir, $"{sex}_teeth.asset")), sex, tuskId, follows);
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
    private static byte[] PatchTeethAsset(byte[] raw, string sex, string tuskId, List<(string Id, string[] Attributes)> follows)
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        bool hasBom = raw.AsSpan().StartsWith(bom);
        string text = Encoding.UTF8.GetString(raw, hasBom ? 3 : 0, raw.Length - (hasBom ? 3 : 0));
        if (!text.Contains("shader = \"portrait_teeth\"", StringComparison.Ordinal))
            throw new InvalidDataException($"{sex}_teeth.asset no longer uses the portrait_teeth shader");
        text = text.Replace("shader = \"portrait_teeth\"", "shader = \"portrait_skin\"", StringComparison.Ordinal);

        string nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split(nl).ToList();

        var shapeLines = new List<string> { $"\tblend_shape = {{ id = \"{tuskId}\"\ttype = \"{tuskId}.mesh\" }}\t# Ck3MapGen orc tusks" };
        var attributeLines = new List<string> { $"\tattribute = {{ name = \"{OrcTusks.Attribute}\"\t\tblend_shape = \"{tuskId}\" }}\t# Ck3MapGen orc tusks" };
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
    private static byte[] PatchAsset(byte[] raw, string sex, bool withEars, bool withTusks)
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
            attributes.Add($"\tattribute = {{ name = \"{OrcTusks.Attribute}\"\t\tblend_shape = \"{sex}_bs_neutral\" }}\t# Ck3MapGen orc tusks (teeth attribute)");
        }

        lines.InsertRange(lastAttribute + 1, attributes);

        if (withEars)
            lines.InsertRange(lastShape + 1, Styles.Select(s =>
                $"\t\tblend_shape = {{ id = \"{BlendShapeId(sex, s.Style)}\"\t\ttype = \"blendshapes/{BlendShapeId(sex, s.Style)}.mesh\" }}\t# Ck3MapGen race head"));

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
