namespace Ck3MapGen.MapGen;

/// <summary>
/// Lower orc tusks, added to the vanilla teeth mesh as geometry of their own.
///
/// **Why appended geometry, not a stretched tooth.** Vanilla's teeth are a 377-vertex shell with
/// the teeth painted on, so there is no canine to pull out. Two tapered tubes are appended instead
/// and, in the base mesh, collapsed onto their root inside the mouth — zero-area triangles, invisible.
/// A blendshape grows them to full size, so a gene value of 0.5 is a half-grown tusk.
///
/// **The path.** Each tusk rises from the lower canine behind the lower lip to the lip line, leaves
/// the mouth through the gap between the lips, then curves up in front of the upper lip and out. The
/// lip line is the upper teeth's biting edge (the lips close on it) — a crease search on the skin
/// profile misplaced the female's at her nose. The exit sits on the lip surface, so the turn from
/// forward to up happens inside the lip and what shows is one diagonal sweep.
///
/// **Pinned to the lip, not the jaw.** The lips ride <c>bn_h_mouth_{l,r}_lowerLip</c>; the lower
/// teeth ride <c>bn_h_jaw_low</c>. Skinned to the jaw, the tusks drifted off the lips on any face
/// whose chin or jaw genes differ from neutral (seen in game 2026-09-28). So every tusk vertex takes
/// the averaged skin weights of the lower-lip skin at its exit point (<see cref="LipAnchor"/>), and
/// the emitter mirrors every head blendshape that moves that skin onto the tusk as well.
///
/// File space is y up with the face toward −z and +x the head's left. Proved in game with the
/// standalone orc_tusk_probe mod, 2026-09-28.
/// </summary>
internal static class OrcTusks
{
    /// <summary>The gene (BaseFilesToCopy/Core/common/genes/gen_bs_orc_tusks.txt) and its templates.</summary>
    public const string Gene = "gen_bs_orc_tusks";
    public const string NoneTemplate = "gen_orc_tusks_none";
    public const string LowerTemplate = "gen_orc_tusks_lower";

    /// <summary>The attribute the standard template sets and the teeth asset maps to its blendshape.</summary>
    public const string Attribute = "gen_bs_orc_tusks_lower";

    /// <summary>
    /// One tusk's shape: the path's bend and tip as (out, up, forward) offsets from the lip exit,
    /// the girth at the base and the tip, and — for a snapped tusk — how far along its path it ends.
    /// </summary>
    public sealed record Shape(V Rise, V Tip, double RadiusBase, double RadiusTip, double? Cut = null);

    /// <summary>
    /// A variant: both tusks' shapes, the character's left (+x) first. Every variant grows the SAME
    /// appended vertices (<see cref="Rings"/> × <see cref="Segments"/> + 1 per tusk) from the same
    /// collapsed root, so each ships as one more teeth blendshape on one more gene template.
    /// Reviewed as offline renders and approved 2026-09-28 (ck3devtools/tusk_probe/variants.py).
    /// </summary>
    public sealed record Variant(string Name, Shape Left, Shape Right);

    // The standard tusk, thickened on review (base 0.40 -> 0.50, tip 0.05 -> 0.08): the first ones
    // read thin in game. Stubby barely clears the upper lip, blunt; great flares out and up to the
    // nostrils, boar-style; a broken tusk is the standard one snapped at 70% of its path.
    private static readonly Shape Standard = new(new(0.20, 1.10, 0.45), new(0.45, 2.20, 0.30), 0.50, 0.08);
    private static readonly Shape Stubby = new(new(0.16, 0.72, 0.42), new(0.28, 1.30, 0.34), 0.58, 0.17);
    private static readonly Shape Great = new(new(0.40, 1.35, 0.62), new(1.20, 3.10, 0.40), 0.60, 0.08);
    private static readonly Shape Snapped = Standard with { Cut = 0.70 };

    /// <summary>Every variant, the standard one first (its template is the original <c>lower</c>).</summary>
    public static readonly Variant[] Variants =
    [
        new("lower", Standard, Standard),
        new("stubby", Stubby, Stubby),
        new("great", Great, Great),
        new("broken_left", Snapped, Standard),
        new("broken_right", Standard, Snapped),
    ];

    /// <summary>The variants a DNA may carry; the broken ones are scars, set by flag, never inherited.</summary>
    public static readonly string[] Inherited = ["lower", "stubby", "great"];

    public static string TemplateOf(string variant) => $"gen_orc_tusks_{variant}";
    public static string AttributeOf(string variant) => $"gen_bs_orc_tusks_{variant}";
    public static string BrokenFlag(string side) => $"gen_tusk_broken_{side}";

    public const string JawBone = "bn_h_jaw_low";
    public const string UpperTeethBone = "bn_h_face_lower";
    public static string LowerLipBone(double side) => side > 0 ? "bn_h_mouth_l_lowerLip" : "bn_h_mouth_r_lowerLip";

    private const int Rings = 13;                  // along the spine
    private const int Segments = 8;                // around it
    private static readonly (float U, float V) EnamelUv = (0.346f, 0.244f);   // uniform ivory in both teeth diffuses

    // The path starts inside the gum; the rest is per variant (Shape), as (out, up, forward) offsets
    // from the exit point, forward being −z.
    private const double Bury = 0.4;               // spine starts this far below the canine top

    /// <summary>A full-grown tusk and where it sits. Streams are ready to append to a mesh.</summary>
    public sealed record Tusk(
        float[] P, float[] N, float[] Ta, float[] Uv, int[] Tri, V Root, double Side, V Exit)
    {
        public int VertexCount => P.Length / 3;
    }

    /// <summary>Skin weights for every vertex of one tusk, and the head vertices they came from.</summary>
    public sealed record Anchor(int[] HeadVertices, int[] Bones, float[] Weights);

    /// <summary>Both tusks of <paramref name="variant"/>, left then right.</summary>
    /// <param name="teethP">Teeth positions.</param>
    /// <param name="teethN">Teeth normals, used only to match the mesh's winding convention.</param>
    /// <param name="influences">Stride of <paramref name="teethIx"/>: ix.Length / vertex count.</param>
    /// <param name="scale">Tusk size; the female head is smaller and takes 0.9.</param>
    public static List<Tusk> Build(
        float[] teethP, float[] teethN, int[] teethTri, int[] teethIx, float[] teethW, int influences,
        int jawBone, int upperTeethBone, float[] headP, double scale, Variant variant)
    {
        bool flip = !CounterClockwise(teethP, teethN, teethTri);
        var tusks = new List<Tusk>();

        foreach (var (root, side) in CanineRoots(teethP, teethIx, teethW, influences, jawBone))
        {
            var (crease, front) = LipLine(teethP, teethIx, teethW, influences, upperTeethBone, headP, root.X);
            tusks.Add(Grow(root, side, scale, crease, front, flip, side > 0 ? variant.Left : variant.Right));
        }

        return tusks;
    }

    /// <summary>
    /// The lower-lip skin a tusk comes out of: the 3 head vertices nearest its exit whose strongest
    /// bone is that side's lower-lip bone, their weights averaged and cut to <paramref name="used"/>
    /// influences (the teeth's <c>bones</c> value), renormalised.
    /// </summary>
    public static Anchor LipAnchor(float[] headP, int[] headIx, float[] headW, int lipBone, V exit, int used)
    {
        int count = headP.Length / 3, per = headIx.Length / count;
        var candidates = new List<int>();
        for (int v = 0; v < count; v++)
        {
            int best = 0;
            for (int k = 1; k < per; k++)
                if (headW[v * per + k] > headW[v * per + best]) best = k;
            if (headIx[v * per + best] == lipBone) candidates.Add(v);
        }

        if (candidates.Count < 3) throw new InvalidDataException("the head has no lower-lip skin to pin the tusks to");

        int[] near = [.. candidates.OrderBy(v => (At(headP, v) - exit).LengthSquared).Take(3)];
        var sums = new List<(int Bone, double Weight)>();
        foreach (int v in near)
            for (int k = 0; k < per; k++)
            {
                int bone = headIx[v * per + k];
                if (bone < 0) continue;
                int i = sums.FindIndex(s => s.Bone == bone);
                if (i < 0) sums.Add((bone, headW[v * per + k]));
                else sums[i] = (bone, sums[i].Weight + headW[v * per + k]);
            }

        var top = sums.OrderByDescending(s => s.Weight).Take(used).ToList();
        double total = top.Sum(s => s.Weight);
        return new Anchor(near, [.. top.Select(s => s.Bone)], [.. top.Select(s => (float)(s.Weight / total))]);
    }

    /// <summary>Top-front of the lower teeth at the canine, one per side.</summary>
    private static List<(V Root, double Side)> CanineRoots(float[] p, int[] ix, float[] w, int per, int jawBone)
    {
        var jaw = Skinned(p, ix, w, per, jawBone);
        if (jaw.Count == 0) throw new InvalidDataException("no teeth vertex is skinned to the lower jaw");
        double half = jaw.Max(v => Math.Abs(p[v * 3]));

        var roots = new List<(V, double)>();
        foreach (double side in new[] { 1.0, -1.0 })
        {
            var band = jaw.Where(v => p[v * 3] * side > 0.43 * half && p[v * 3] * side < 0.60 * half).ToList();
            if (band.Count == 0) throw new InvalidDataException("no lower canine found on the teeth");
            double top = band.Max(v => p[v * 3 + 1]);
            var edge = band.Where(v => p[v * 3 + 1] > top - 0.35).ToList();
            roots.Add((new V(edge.Average(v => (double)p[v * 3]), edge.Average(v => (double)p[v * 3 + 1]),
                edge.Min(v => p[v * 3 + 2]) + 0.25), side));             // just behind the front face
        }

        return roots;
    }

    /// <summary>(lip-line height, lip front z) at |x|.</summary>
    private static (double Crease, double Front) LipLine(
        float[] p, int[] ix, float[] w, int per, int upperBone, float[] headP, double x)
    {
        var upper = Skinned(p, ix, w, per, upperBone).Where(v => Math.Abs(Math.Abs(p[v * 3]) - Math.Abs(x)) < 0.45).ToList();
        if (upper.Count == 0) throw new InvalidDataException("no upper teeth found above the canine");
        double crease = upper.Min(v => p[v * 3 + 1]) + 0.1;

        // z < -8 keeps to the face; the back of the head is at the other end of the same band.
        double front = double.MaxValue;
        for (int v = 0; v < headP.Length / 3; v++)
        {
            double y = headP[v * 3 + 1];
            if (Math.Abs(Math.Abs(headP[v * 3]) - Math.Abs(x)) < 0.3 && y >= crease - 0.3 && y <= crease + 1.6 && headP[v * 3 + 2] < -8)
                front = Math.Min(front, headP[v * 3 + 2]);
        }

        if (front == double.MaxValue) throw new InvalidDataException("no lip skin found in front of the canine");
        return (crease, front);
    }

    private static Tusk Grow(V root, double side, double scale, double crease, double front, bool flip, Shape shape)
    {
        var exit = new V(root.X + 0.12 * side * scale, crease + 0.15, front - 0.10);
        var inside = new V(root.X + 0.05 * side * scale, crease - 0.10, (root.Z + front) / 2 + 0.2);
        V Off(V o) => exit + new V(o.X * side * scale, o.Y * scale, -o.Z * scale);
        V[] path = [root + new V(0, -Bury * scale, 0), inside, exit, Off(shape.Rise), Off(shape.Tip)];
        double end = shape.Cut ?? 0.985;

        V Spine(double t) => CatmullRom(path, t);
        V Dir(double t) => (Spine(Math.Min(1, t + 0.01)) - Spine(Math.Max(0, t - 0.01))).Normalized;

        var pos = new List<V>(); var nrm = new List<V>(); var tan = new List<V>(); var uv = new List<(float, float)>();
        V a = default;
        for (int r = 0; r < Rings; r++)
        {
            double t = r / (double)(Rings - 1) * end;
            V c = Spine(t), d = Dir(t);
            a = (Math.Abs(d.Z) < 0.9 ? V.Cross(d, new V(0, 0, 1)) : V.Cross(d, new V(1, 0, 0))).Normalized;
            V b = V.Cross(d, a);

            // Full girth until the tusk clears the lips (t = 0.5 is the exit point), then taper.
            double tt = Math.Max(0.0, (t - 0.5) / 0.5);
            double radius = (shape.RadiusBase + (shape.RadiusTip - shape.RadiusBase) * Math.Pow(tt, 1.1)) * scale;
            bool breakRing = shape.Cut is not null && r == Rings - 1;

            for (int s = 0; s < Segments; s++)
            {
                double angle = 2 * Math.PI * s / Segments;
                V n = a * Math.Cos(angle) + b * Math.Sin(angle);
                // A snapped tusk's last ring is tilted, so the break is a slanted face, not a cut.
                pos.Add(c + n * radius + (breakRing ? d * (0.35 * radius * Math.Cos(angle)) : default)); nrm.Add(n); tan.Add(d);
                uv.Add(((float)(EnamelUv.U + 0.004 * Math.Cos(angle)), (float)(EnamelUv.V + 0.004 * t)));
            }
        }

        // The tip closes the tube: a point for a whole tusk, the middle of a flat cap for a snapped one.
        double tipAt = shape.Cut ?? 1.0;
        pos.Add(Spine(tipAt)); nrm.Add(Dir(tipAt)); tan.Add(a); uv.Add(EnamelUv);

        var tri = new List<int>();
        for (int r = 0; r < Rings - 1; r++)
            for (int s = 0; s < Segments; s++)
            {
                int a0 = r * Segments + s, a1 = r * Segments + (s + 1) % Segments, b0 = a0 + Segments, b1 = a1 + Segments;
                tri.AddRange([a0, b0, a1, a1, b0, b1]);
            }

        int last = (Rings - 1) * Segments, tip = Rings * Segments;
        for (int s = 0; s < Segments; s++) tri.AddRange([last + s, tip, last + (s + 1) % Segments]);

        // Wind every face to agree with its outward normals, then match the teeth's own convention.
        for (int i = 0; i + 2 < tri.Count; i += 3)
        {
            V fn = V.Cross(pos[tri[i + 1]] - pos[tri[i]], pos[tri[i + 2]] - pos[tri[i]]);
            bool agrees = V.Dot(fn, nrm[tri[i]] + nrm[tri[i + 1]] + nrm[tri[i + 2]]) >= 0;
            if (agrees == flip) (tri[i + 1], tri[i + 2]) = (tri[i + 2], tri[i + 1]);
        }

        return new Tusk(
            [.. pos.SelectMany(v => new[] { (float)v.X, (float)v.Y, (float)v.Z })],
            [.. nrm.SelectMany(v => new[] { (float)v.X, (float)v.Y, (float)v.Z })],
            [.. tan.SelectMany(v => new[] { (float)v.X, (float)v.Y, (float)v.Z, 1f })],
            [.. uv.SelectMany(u => new[] { u.Item1, u.Item2 })],
            [.. tri], root, side, exit);
    }

    /// <summary>Whether a mesh winds counter-clockwise about its normals (majority vote).</summary>
    private static bool CounterClockwise(float[] p, float[] n, int[] tri)
    {
        int score = 0;
        for (int i = 0; i + 2 < tri.Length; i += 3)
        {
            V fn = V.Cross(At(p, tri[i + 1]) - At(p, tri[i]), At(p, tri[i + 2]) - At(p, tri[i]));
            score += V.Dot(fn, At(n, tri[i]) + At(n, tri[i + 1]) + At(n, tri[i + 2])) > 0 ? 1 : -1;
        }

        return score >= 0;
    }

    private static List<int> Skinned(float[] p, int[] ix, float[] w, int per, int bone)
    {
        var result = new List<int>();
        for (int v = 0; v < p.Length / 3; v++)
        {
            double sum = 0;
            for (int k = 0; k < per; k++)
                if (ix[v * per + k] == bone) sum += w[v * per + k];
            if (sum > 0.5) result.Add(v);
        }

        return result;
    }

    /// <summary>Uniform Catmull-Rom through the points, t in 0..1 over the whole chain.</summary>
    private static V CatmullRom(V[] pts, double t)
    {
        int n = pts.Length - 1;
        double f = Math.Min(t * n, n - 1e-9);
        int i = (int)f;
        double u = f - i;
        V p0 = pts[Math.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.Min(n, i + 2)];
        return (p1 * 2 + (p2 - p0) * u + (p0 * 2 - p1 * 5 + p2 * 4 - p3) * (u * u) + (p1 * 3 - p0 - p2 * 3 + p3) * (u * u * u)) * 0.5;
    }

    private static V At(float[] a, int v) => new(a[v * 3], a[v * 3 + 1], a[v * 3 + 2]);

    /// <summary>A double-precision point; only this file's geometry uses it.</summary>
    internal readonly record struct V(double X, double Y, double Z)
    {
        public static V operator +(V a, V b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V operator -(V a, V b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V operator *(V a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static double Dot(V a, V b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static V Cross(V a, V b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double LengthSquared => Dot(this, this);
        public V Normalized => LengthSquared == 0 ? this : this * (1.0 / Math.Sqrt(LengthSquared));
    }
}
