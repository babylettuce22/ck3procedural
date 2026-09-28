namespace Ck3MapGen.MapGen;

/// <summary>
/// The settings for one pointed ear. The tip travels along (<see cref="Out"/>, <see cref="Up"/>,
/// <see cref="Back"/>), normalised, by <see cref="Length"/> head units — the vanilla ear is about
/// 6.2 units tall, so the high elf's 4.2 is a tip two thirds again the ear's own height.
/// </summary>
/// <param name="Taper">How far the upper rim closes onto the tip line, 0..1. This is what makes a
/// point rather than a stretched round top.</param>
/// <param name="Thin">How much the flap thins toward the tip, 0..1.</param>
/// <param name="MaskLo">Ear-bone skin weight below which a vertex does not move. Weighting fades out
/// across the join with the skull, so this keeps the cheek and scalp still.</param>
/// <param name="PivotHeight">Where along the ear's height (0 lobe, 1 top) the stretch starts from.</param>
internal sealed record EarShape(
    double Length, double Out, double Up, double Back, double Taper, double Thin,
    double MaskLo = 0.25, double PivotHeight = 0.45)
{
    /// <summary>Long, swept up and back, tapering to a fine point. Seen in game 2026-09-28.</summary>
    public static readonly EarShape HighElf = new(Length: 4.2, Out: 0.22, Up: 0.80, Back: 0.56, Taper: 0.72, Thin: 0.35);
}

/// <summary>
/// Pointed ears made as a blendshape of the vanilla head, rather than modelled and attached.
///
/// **Why a blendshape.** An attached portrait mesh is textured by its own DDS on the
/// <c>portrait_attachment</c> shader, so it can never match a character's skin, let alone our
/// <c>gen_race_skin</c> hue shift. A blendshape is the head itself with vertices moved, so the ear
/// is drawn by the head's <c>portrait_skin</c> shader: skin tone, freckles, ageing and the race
/// shift all follow for free, and a half-strength value gives a half-elf. Elder Kings does its mer
/// ears the same way.
///
/// **Why computed, not sculpted.** A blendshape must match the head vertex for vertex — 2770 in
/// vanilla 1.19, male and female alike — and a Blender round trip may reorder vertices. Building it
/// here from the installed game's own head keeps the order by construction, and keeps up with any
/// patch that reshapes the head.
///
/// **The method.** The ear is every vertex skinned to <c>bn_h_ear_l_main</c> / <c>bn_h_ear_r_main</c>
/// (about 200 per side), and the bone weight doubles as a mask, so the join with the skull
/// feathers out on the rig's own falloff. Each side works in an ear-local frame: D toward the tip,
/// T across the flap, Nf through it. A vertex at fraction t of the way up D moves Length·t² along
/// D, has its offset across the flap shrunk by Taper·t^1.5, and its thickness by Thin·t². File
/// space is y up with the face toward −z and +x the head's left.
///
/// Normals and tangents are not recomputed from scratch, which would flatten the authored
/// smoothing and open the UV seams. Each keeps its authored direction, turned by however much the
/// surface under it turned (both measured with seam duplicates welded, so the turn is continuous).
/// </summary>
internal static class PointedEars
{
    /// <summary>
    /// The gene (BaseFilesToCopy/Core/common/genes/gen_bs_elf_ears.txt) and its style templates.
    /// One gene so a character carries one ear style.
    /// </summary>
    public const string Gene = "gen_bs_elf_ears";
    public const string NoneTemplate = "gen_elf_ears_none";
    public const string HighTemplate = "gen_elf_ears_high";

    public const string LeftEarBone = "bn_h_ear_l_main";
    public const string RightEarBone = "bn_h_ear_r_main";

    /// <summary>A finished blendshape's streams, plus two numbers worth logging.</summary>
    public sealed record Result(float[] P, float[] N, float[] Ta, int Moved, double MaxShift);

    /// <param name="p">Positions, 3 per vertex.</param>
    /// <param name="n">Normals, 3 per vertex.</param>
    /// <param name="ta">Tangents, 4 per vertex (xyz + handedness).</param>
    /// <param name="tri">Triangle indices.</param>
    /// <param name="skinIx">Bone index per influence, <paramref name="influences"/> per vertex.</param>
    /// <param name="skinW">Weight per influence, parallel to <paramref name="skinIx"/>.</param>
    public static Result Shape(
        float[] p, float[] n, float[] ta, int[] tri, int[] skinIx, float[] skinW, int influences,
        int leftBone, int rightBone, EarShape s)
    {
        int count = p.Length / 3;
        var q = new double[p.Length];
        for (int i = 0; i < p.Length; i++) q[i] = p[i];

        ShapeSide(p, q, skinIx, skinW, influences, count, leftBone, +1.0, s);
        ShapeSide(p, q, skinIx, skinW, influences, count, rightBone, -1.0, s);

        var groups = WeldGroups(p, count);
        var n0 = VertexNormals(ToDouble(p), tri, groups, count);
        var n1 = VertexNormals(q, tri, groups, count);

        var outP = new float[p.Length];
        var outN = (float[])n.Clone();
        var outTa = (float[])ta.Clone();
        int moved = 0;
        double maxShift = 0;

        for (int v = 0; v < count; v++)
        {
            for (int k = 0; k < 3; k++) outP[v * 3 + k] = (float)q[v * 3 + k];

            var d = Sub(Get(q, v), ToDoubleVec(p, v));
            double shift = Math.Sqrt(Dot(d, d));
            if (shift > 0) moved++;
            maxShift = Math.Max(maxShift, shift);

            var a = n0[v];
            var b = n1[v];
            if (a == b) continue;

            var nv = Norm(Rotate(a, b, (n[v * 3], n[v * 3 + 1], n[v * 3 + 2])));
            var tv = Norm(Rotate(a, b, (ta[v * 4], ta[v * 4 + 1], ta[v * 4 + 2])));
            (outN[v * 3], outN[v * 3 + 1], outN[v * 3 + 2]) = ((float)nv.X, (float)nv.Y, (float)nv.Z);
            (outTa[v * 4], outTa[v * 4 + 1], outTa[v * 4 + 2]) = ((float)tv.X, (float)tv.Y, (float)tv.Z);
        }

        return new Result(outP, outN, outTa, moved, maxShift);
    }

    private static void ShapeSide(
        float[] p, double[] q, int[] skinIx, float[] skinW, int influences, int count,
        int bone, double side, EarShape s)
    {
        var weight = new double[count];
        var ear = new List<int>();
        var core = new List<int>();

        for (int v = 0; v < count; v++)
        {
            double w = 0;
            for (int k = 0; k < influences; k++)
                if (skinIx[v * influences + k] == bone) w += skinW[v * influences + k];
            weight[v] = w;
            if (w > 0.01) ear.Add(v);
            if (w > 0.95) core.Add(v);
        }

        if (core.Count == 0)
            throw new InvalidDataException($"No vertex is fully skinned to ear bone {bone}; the head rig has changed.");

        double lo = double.MaxValue, hi = double.MinValue, cx = 0, cz = 0;
        foreach (int v in core)
        {
            lo = Math.Min(lo, p[v * 3 + 1]);
            hi = Math.Max(hi, p[v * 3 + 1]);
            cx += p[v * 3];
            cz += p[v * 3 + 2];
        }

        var pivot = (X: cx / core.Count, Y: lo + s.PivotHeight * (hi - lo), Z: cz / core.Count);
        var dir = Norm((s.Out * side, s.Up, s.Back));
        var across = Norm(Cross(dir, (side, 0.0, 0.0)));
        var through = Norm(Cross(across, dir));

        double sMax = double.MinValue, mid = 0;
        foreach (int v in core)
        {
            var r = Sub(ToDoubleVec(p, v), pivot);
            sMax = Math.Max(sMax, Dot(r, dir));
            mid += Dot(r, through);
        }

        mid /= core.Count;                                   // the flap's mid-surface

        foreach (int v in ear)
        {
            double m = Smooth(s.MaskLo, 1.0, weight[v]);
            if (m <= 0) continue;

            var r = Sub(ToDoubleVec(p, v), pivot);
            double along = Dot(r, dir), u = Dot(r, across), h = Dot(r, through) - mid;
            double t = Math.Min(Math.Max(0.0, along / sMax), 1.25);
            double t1 = Math.Min(1.0, t);

            double along2 = along + s.Length * t * t;
            double u2 = u * (1.0 - s.Taper * Math.Pow(t1, 1.5));
            double h2 = h * (1.0 - s.Thin * t1 * t1);

            var target = Add(pivot, Add(Add(Mul(dir, along2), Mul(across, u2)), Mul(through, h2 + mid)));
            for (int k = 0; k < 3; k++)
            {
                double old = p[v * 3 + k];
                q[v * 3 + k] = old + (Component(target, k) - old) * m;
            }
        }
    }

    /// <summary>
    /// Vertices sharing a position — the duplicates a UV seam splits — so both sides of a seam turn
    /// their normals together. Rounded to 1e-4 so float noise between duplicates cannot split a pair.
    /// </summary>
    private static List<List<int>> WeldGroups(float[] p, int count)
    {
        var map = new Dictionary<(long, long, long), List<int>>();
        var order = new List<List<int>>();
        for (int v = 0; v < count; v++)
        {
            var key = ((long)Math.Round(p[v * 3] * 1e4), (long)Math.Round(p[v * 3 + 1] * 1e4), (long)Math.Round(p[v * 3 + 2] * 1e4));
            if (!map.TryGetValue(key, out var g))
            {
                g = [];
                map[key] = g;
                order.Add(g);
            }

            g.Add(v);
        }

        return order;
    }

    /// <summary>Area-weighted vertex normals, shared across each weld group.</summary>
    private static (double X, double Y, double Z)[] VertexNormals(double[] p, int[] tri, List<List<int>> groups, int count)
    {
        var acc = new (double X, double Y, double Z)[count];
        for (int t = 0; t + 2 < tri.Length; t += 3)
        {
            int a = tri[t], b = tri[t + 1], c = tri[t + 2];
            var pa = Get(p, a);
            var fn = Cross(Sub(Get(p, b), pa), Sub(Get(p, c), pa));
            acc[a] = Add(acc[a], fn);
            acc[b] = Add(acc[b], fn);
            acc[c] = Add(acc[c], fn);
        }

        var result = new (double X, double Y, double Z)[count];
        foreach (var g in groups)
        {
            (double X, double Y, double Z) sum = (0, 0, 0);
            foreach (int v in g) sum = Add(sum, acc[v]);
            sum = Norm(sum);
            foreach (int v in g) result[v] = sum;
        }

        return result;
    }

    /// <summary>Turns <paramref name="x"/> by the rotation that carries unit <paramref name="a"/> onto unit <paramref name="b"/> (Rodrigues).</summary>
    private static (double X, double Y, double Z) Rotate(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b, (double X, double Y, double Z) x)
    {
        double c = Dot(a, b);
        var axis = Cross(a, b);
        double sn = Math.Sqrt(Dot(axis, axis));
        if (sn < 1e-9) return x;

        var k = Mul(axis, 1.0 / sn);
        double angle = Math.Atan2(sn, c), ca = Math.Cos(angle), sa = Math.Sin(angle);
        return Add(Add(Mul(x, ca), Mul(Cross(k, x), sa)), Mul(k, Dot(k, x) * (1 - ca)));
    }

    private static double Smooth(double e0, double e1, double x)
    {
        double t = Math.Clamp((x - e0) / (e1 - e0), 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }

    private static double[] ToDouble(float[] a)
    {
        var d = new double[a.Length];
        for (int i = 0; i < a.Length; i++) d[i] = a[i];
        return d;
    }

    private static (double X, double Y, double Z) ToDoubleVec(float[] a, int v) => (a[v * 3], a[v * 3 + 1], a[v * 3 + 2]);
    private static (double X, double Y, double Z) Get(double[] a, int v) => (a[v * 3], a[v * 3 + 1], a[v * 3 + 2]);
    private static double Component((double X, double Y, double Z) a, int k) => k == 0 ? a.X : k == 1 ? a.Y : a.Z;
    private static (double X, double Y, double Z) Add((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static (double X, double Y, double Z) Sub((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static (double X, double Y, double Z) Mul((double X, double Y, double Z) a, double s) => (a.X * s, a.Y * s, a.Z * s);
    private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static (double X, double Y, double Z) Norm((double X, double Y, double Z) a)
    {
        double l = Math.Sqrt(Dot(a, a));
        return l == 0 ? a : Mul(a, 1.0 / l);
    }
}
