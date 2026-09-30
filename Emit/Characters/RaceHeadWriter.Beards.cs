using System.Text;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

public static partial class RaceHeadWriter
{
    // ---- Beard follow shapes ------------------------------------------------------------------

    /// <summary>Nearest head vertices a beard vertex takes its movement from (inverse-distance²).</summary>
    private const int BeardNeighbours = 6;

    /// <summary>One beard's override: the patched asset and its follow meshes, paths relative to the mod.</summary>
    private sealed record BeardOutput(string AssetRel, byte[] Asset, List<(string Rel, PdxNode Root)> Meshes);

    /// <summary>
    /// A follow shape for every vanilla beard, per generated male head shape that reaches it (in
    /// practice the giantkin face; the orc brow sits above any beard and falls under FollowMin).
    ///
    /// Vanilla beards follow the face only through bones and two blendshapes of their own (fat,
    /// gaunt) — no beard follows a face blendshape. Vanilla's own face shapes are small enough not to
    /// matter; the giant face pushes the chin 0.9 forward and the jaw corners 1.2 out, and without a
    /// follow the chin came through the beard: measured offline (ck3devtools/portrait_render) on 79
    /// beards, 62 lost over 3% of their coverage to skin, short ones up to ~77%; with these shapes the
    /// mean loss fell from 18.7% to 3.8%, the rest being long strands that shift rather than vanish.
    ///
    /// The transfer, per beard vertex: into head space through the head's bind worlds and the beard's
    /// inverse binds (beards carry their own bind transforms, some rotated ~90°, so their raw
    /// positions are not head positions), the shape's movement there from the nearest head vertices,
    /// then back into the beard's mesh space, where a blendshape applies. The asset gains one
    /// blend_shape and one attribute line under the head shape's own attribute name, so beard and chin
    /// move by the same gene.
    ///
    /// Beards not on the head rig (the legacy <c>western_beard_*</c> meshes, rig <c>root</c>, used by no
    /// gene) or with more than one sub-mesh are left alone.
    /// </summary>
    private static List<BeardOutput> BuildBeardFollows(string portraits, List<(string Id, float[] P, string[] Attributes)> generated,
        List<string> log)
    {
        string beardDir = Path.Combine(portraits, "m_beards");
        var outputs = new List<BeardOutput>();
        if (!Directory.Exists(beardDir) || generated.Count == 0) return outputs;

        var head = PdxMesh.Read(Path.Combine(portraits, "male_head", "male_head.mesh"));
        var headMesh = Find(head, "mesh") ?? throw new InvalidDataException("male_head.mesh has no mesh node");
        var headSkeleton = Find(head, "skeleton") ?? throw new InvalidDataException("male_head.mesh has no skeleton");
        float[] hp = headMesh.Floats("p");
        var headBindWorld = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var bone in headSkeleton.Children)
            headBindWorld[BareBone(bone.Name)] = Affine.Invert(Affine.FromTx(bone.Floats("tx")));

        var grid = new VertexGrid(hp, 1.5);
        var fields = generated.Where(g => g.P.Length == hp.Length).ToList();
        int skipped = 0;

        foreach (string assetPath in Directory.GetFiles(beardDir, "*.asset", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(assetPath);
            string dir = Path.GetDirectoryName(assetPath)!;
            byte[] rawAsset = File.ReadAllBytes(assetPath);
            string assetText = Encoding.UTF8.GetString(rawAsset);
            var file = System.Text.RegularExpressions.Regex.Match(assetText, @"\bfile\s*=\s*""([^""]+\.mesh)""");
            if (!file.Success) { skipped++; continue; }
            string meshPath = Path.Combine(dir, file.Groups[1].Value);
            if (!File.Exists(meshPath)) { skipped++; continue; }

            var root = PdxMesh.Read(meshPath);
            var objects = Find(root, "object")?.Children ?? [];
            var mesh = Find(root, "mesh"); var skin = Find(root, "skin"); var skeleton = Find(root, "skeleton");
            if (objects.Count != 1 || mesh is null || skin is null || skeleton is null) { skipped++; continue; }

            // Each beard vertex's skin-weighted bind transform into head space.
            var boneMat = new Dictionary<int, double[]>();
            foreach (var bone in skeleton.Children)
            {
                if (bone.Ints("ix") is not [var bi, ..]) continue;
                if (!headBindWorld.TryGetValue(BareBone(bone.Name), out var hw)) continue;
                boneMat[bi] = Affine.Multiply(hw, Affine.FromTx(bone.Floats("tx")));
            }

            if (boneMat.Count == 0) { skipped++; continue; }   // not on the head rig

            float[] p = mesh.Floats("p");
            int count = p.Length / 3;
            int[] ix = skin.Ints("ix");
            float[] w = skin.Floats("w");
            int per = ix.Length / Math.Max(1, count);
            var toHead = new double[count][];
            var headPos = new double[count * 3];
            for (int v = 0; v < count; v++)
            {
                var m = new double[12];
                double total = 0;
                for (int k = 0; k < per; k++)
                {
                    int b = ix[v * per + k];
                    float wt = w[v * per + k];
                    if (b < 0 || wt <= 0 || !boneMat.TryGetValue(b, out var bm)) continue;
                    for (int i = 0; i < 12; i++) m[i] += wt * bm[i];
                    total += wt;
                }

                if (total <= 0) m = Affine.Identity();
                else if (Math.Abs(total - 1) > 1e-4) for (int i = 0; i < 12; i++) m[i] /= total;
                toHead[v] = m;
                var q = Affine.Apply(m, p[v * 3], p[v * 3 + 1], p[v * 3 + 2]);
                headPos[v * 3] = q.X; headPos[v * 3 + 1] = q.Y; headPos[v * 3 + 2] = q.Z;
            }

            // Neighbours and weights once per beard; every head shape reuses them.
            var near = new (int[] Index, double[] Weight)[count];
            for (int v = 0; v < count; v++)
                near[v] = grid.Nearest(headPos[v * 3], headPos[v * 3 + 1], headPos[v * 3 + 2], BeardNeighbours);

            var meshes = new List<(string, PdxNode)>();
            var follows = new List<(string Id, string[] Attributes)>();
            string rel = Path.GetRelativePath(portraits, dir);
            int index = 0;
            foreach (var (shapeId, q, attributes) in fields)
            {
                var moved = new float[p.Length];
                double size = 0;
                for (int v = 0; v < count; v++)
                {
                    double dx = 0, dy = 0, dz = 0;
                    var (nIx, nW) = near[v];
                    for (int k = 0; k < nIx.Length; k++)
                    {
                        int h = nIx[k];
                        dx += nW[k] * (q[h * 3] - hp[h * 3]);
                        dy += nW[k] * (q[h * 3 + 1] - hp[h * 3 + 1]);
                        dz += nW[k] * (q[h * 3 + 2] - hp[h * 3 + 2]);
                    }

                    size = Math.Max(size, Math.Sqrt(dx * dx + dy * dy + dz * dz));
                    var d = Affine.SolveLinear(toHead[v], dx, dy, dz);   // back into the beard's mesh space
                    moved[v * 3] = (float)(p[v * 3] + d.X);
                    moved[v * 3 + 1] = (float)(p[v * 3 + 1] + d.Y);
                    moved[v * 3 + 2] = (float)(p[v * 3 + 2] + d.Z);
                }

                if (size <= FollowMin || size >= FollowMax) continue;

                string shortId = shapeId.StartsWith("male_bs_", StringComparison.Ordinal) ? shapeId["male_bs_".Length..] : shapeId;
                string id = $"{name}_bs_{shortId}";
                var shape = PdxMesh.Read(meshPath);
                var obj = (Find(shape, "object") ?? throw new InvalidDataException($"{name} has no object")).Children[0];
                obj.Name = $"{name}_f{index++}Shape";   // PdxMesh refuses 64+ character node names
                obj.Children.RemoveAll(c => c.Name == "skeleton");
                var sm = Find(shape, "mesh")!;
                sm.Children.RemoveAll(c => c.Name == "skin");
                sm.Set("p", PdxProp.Of(moved));
                SetBounds(sm, moved);
                meshes.Add((Path.Combine(rel, $"{id}.mesh"), shape));
                follows.Add((id, attributes));
            }

            if (follows.Count == 0) continue;
            outputs.Add(new BeardOutput(Path.Combine(rel, Path.GetFileName(assetPath)), PatchBeardAsset(rawAsset, name, follows), meshes));
        }

        log.Add($"  beard follow shapes: {outputs.Count} beards follow {string.Join(", ", fields.Select(f => f.Id))}" +
                (skipped > 0 ? $" ({skipped} legacy/unskinned beard assets left alone)" : ""));
        return outputs;

        static string BareBone(string n) => n[(n.LastIndexOf(':') + 1)..];
    }

    /// <summary>
    /// Vanilla's beard asset, in its own line endings and BOM, with one <c>blend_shape</c> line after
    /// the pdxmesh's <c>file =</c> line and one <c>attribute</c> line after the entity's
    /// <c>pdxmesh =</c> line per follow. Every vanilla beard asset has both anchors, including the
    /// ones with no blendshapes of their own.
    /// </summary>
    private static byte[] PatchBeardAsset(byte[] raw, string name, List<(string Id, string[] Attributes)> follows)
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        bool hasBom = raw.AsSpan().StartsWith(bom);
        string text = Encoding.UTF8.GetString(raw, hasBom ? 3 : 0, raw.Length - (hasBom ? 3 : 0));
        string nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split(nl).ToList();

        int fileLine = lines.FindIndex(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*file\s*="));
        int entityLine = lines.FindIndex(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*entity\s*=\s*\{"));
        int meshRef = entityLine < 0 ? -1 : lines.FindIndex(entityLine, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*pdxmesh\s*="));
        if (fileLine < 0 || meshRef < 0)
            throw new InvalidDataException($"{name}.asset no longer has a pdxmesh file line and an entity pdxmesh line");

        // The entity lines sit later in the file: insert them first so fileLine stays valid.
        lines.InsertRange(meshRef + 1, follows.SelectMany(f => f.Attributes.Select(a =>
            $"\tattribute = {{ name = \"{a}\"\t\tblend_shape = \"{f.Id}\" }}\t# Ck3MapGen beard follow")));
        lines.InsertRange(fileLine + 1, follows.Select(f =>
            $"\tblend_shape = {{ id = \"{f.Id}\"\t\ttype = \"{f.Id}.mesh\" }}\t# Ck3MapGen beard follow"));

        byte[] body = Encoding.UTF8.GetBytes(string.Join(nl, lines));
        return hasBom ? [.. bom, .. body] : body;
    }

    /// <summary>Row-major 3x4 affine transforms (rotation/scale | translation), in doubles.</summary>
    private static class Affine
    {
        public static double[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0];

        /// <summary>A PDX skeleton <c>tx</c>: the 4x4's columns, three floats each (column 3 = translation).</summary>
        public static double[] FromTx(float[] tx)
        {
            if (tx.Length < 12) return Identity();
            var m = new double[12];
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 3; r++)
                    m[r * 4 + c] = tx[c * 3 + r];
            return m;
        }

        public static double[] Multiply(double[] a, double[] b)
        {
            var m = new double[12];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    double s = c == 3 ? a[r * 4 + 3] : 0;
                    for (int k = 0; k < 3; k++) s += a[r * 4 + k] * b[k * 4 + c];
                    m[r * 4 + c] = s;
                }
            }

            return m;
        }

        public static double[] Invert(double[] m)
        {
            double a = m[0], b = m[1], c = m[2], d = m[4], e = m[5], f = m[6], g = m[8], h = m[9], i = m[10];
            double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            if (Math.Abs(det) < 1e-12) return Identity();
            double[] r =
            [
                (e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det, 0,
                (f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det, 0,
                (d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det, 0,
            ];
            for (int row = 0; row < 3; row++)
                r[row * 4 + 3] = -(r[row * 4] * m[3] + r[row * 4 + 1] * m[7] + r[row * 4 + 2] * m[11]);
            return r;
        }

        public static (double X, double Y, double Z) Apply(double[] m, double x, double y, double z) =>
            (m[0] * x + m[1] * y + m[2] * z + m[3],
             m[4] * x + m[5] * y + m[6] * z + m[7],
             m[8] * x + m[9] * y + m[10] * z + m[11]);

        /// <summary>The direction d with m's linear part · d = (x, y, z).</summary>
        public static (double X, double Y, double Z) SolveLinear(double[] m, double x, double y, double z)
        {
            var inv = Invert(m);
            return (inv[0] * x + inv[1] * y + inv[2] * z,
                    inv[4] * x + inv[5] * y + inv[6] * z,
                    inv[8] * x + inv[9] * y + inv[10] * z);
        }
    }

    /// <summary>A uniform grid over the head's vertices for k-nearest queries.</summary>
    private sealed class VertexGrid
    {
        private readonly float[] _p;
        private readonly double _cell;
        private readonly Dictionary<(int, int, int), List<int>> _cells = [];

        public VertexGrid(float[] p, double cell)
        {
            _p = p; _cell = cell;
            for (int v = 0; v < p.Length / 3; v++)
            {
                var key = Key(p[v * 3], p[v * 3 + 1], p[v * 3 + 2]);
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = [];
                list.Add(v);
            }
        }

        private (int, int, int) Key(double x, double y, double z) =>
            ((int)Math.Floor(x / _cell), (int)Math.Floor(y / _cell), (int)Math.Floor(z / _cell));

        /// <summary>The k nearest vertices and their normalised inverse-distance² weights.</summary>
        public (int[] Index, double[] Weight) Nearest(double x, double y, double z, int k)
        {
            var (cx, cy, cz) = Key(x, y, z);
            var best = new List<(double D2, int V)>();
            for (int ring = 0; ring < 64; ring++)
            {
                for (int i = -ring; i <= ring; i++)
                for (int j = -ring; j <= ring; j++)
                for (int l = -ring; l <= ring; l++)
                {
                    if (Math.Max(Math.Abs(i), Math.Max(Math.Abs(j), Math.Abs(l))) != ring) continue;   // shell only
                    if (!_cells.TryGetValue((cx + i, cy + j, cz + l), out var list)) continue;
                    foreach (int v in list)
                    {
                        double dx = _p[v * 3] - x, dy = _p[v * 3 + 1] - y, dz = _p[v * 3 + 2] - z;
                        best.Add((dx * dx + dy * dy + dz * dz, v));
                    }
                }

                // Every vertex within ring * cell of the query is found once the shell is done.
                if (best.Count >= k)
                {
                    best.Sort((a, b) => a.D2.CompareTo(b.D2));
                    double reach = ring * _cell;
                    if (best[k - 1].D2 <= reach * reach) break;
                }
            }

            best.Sort((a, b) => a.D2.CompareTo(b.D2));
            int n = Math.Min(k, best.Count);
            var idx = new int[n]; var wt = new double[n];
            double total = 0;
            for (int t = 0; t < n; t++)
            {
                idx[t] = best[t].V;
                wt[t] = 1.0 / Math.Max(best[t].D2, 1e-6);   // 1/d², as the prototype (d floored at 1e-3)
                total += wt[t];
            }

            for (int t = 0; t < n; t++) wt[t] /= total;
            return (idx, wt);
        }
    }
}
