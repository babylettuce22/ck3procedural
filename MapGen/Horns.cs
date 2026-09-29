using V = Ck3MapGen.MapGen.OrcTusks.V;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Procedural horns for the vanilla heads, as portrait attachments with their own keratin texture.
///
/// **Why attachments, when the ears are blendshapes.** Horn is not skin. It wants its own colour,
/// grain and roughness, which an attachment on the <c>portrait_attachment</c> shader has and a head
/// blendshape (drawn by the skin shader) cannot. What IS skin is the swelling the horn rises from,
/// and that is a head blendshape (<see cref="Boss"/>), so the join blends into the scalp.
///
/// **One generator, five styles.** Every style shares the root (upper-front of the forehead-side
/// skin), the initial growth direction and the radius at the skin; each then thins on one smooth
/// curve to its own tip. The filed stump is a short truncated cone from the same root, so it stands
/// in for any style — which is what makes filing a single model swap.
///
/// **Rules learned from renders (2026-09-28), each fixing something that looked wrong:**
/// <list type="bullet">
/// <item>The buried stretch sinks along the SURFACE NORMAL. Along the tilted growth direction it ran
/// back out of the convex forehead below the root and read as a collar.</item>
/// <item>The flare's widest point sits below the skin, so what shows is a trumpet widening into the
/// scalp, not a ring standing proud of it.</item>
/// <item>Only styles that wrap round the head are pushed out of the skull; on short horns the push
/// crumpled the spine.</item>
/// <item>Normals lean toward the tip by the slope dr/ds; radial normals lit cones like cylinders.</item>
/// <item>Sizes are set against measured vanilla hair: over the root, hair stands ~1.9 units at the
/// median and 2.6 at p75 along the horn direction, so the stump is 3.0 and the nubs ~4.5. The
/// most voluminous hairstyles still bury the short styles, which the user accepted.</item>
/// </list>
///
/// Skinned rigidly to <c>bn_h_skull</c> (horn is rooted in bone; brow bones would wobble it with
/// every frown). File space is y up, the face toward −z, +x the head's left.
/// Proved in game with the standalone horn_probe mod, 2026-09-28.
/// </summary>
internal static class Horns
{
    /// <summary>The accessory gene (generated with the models) and its templates, gen_horns_{style}.</summary>
    public const string Gene = "gen_horns";
    public const string NoneTemplate = "gen_horns_none";
    public static string TemplateOf(string style) => $"gen_horns_{style}";

    /// <summary>The skin-mound morph gene (static, BaseFilesToCopy/Core/common/genes/gen_bs_horn_boss.txt).</summary>
    public const string BossGene = "gen_bs_horn_boss";
    public const string BossNoneTemplate = "gen_horn_boss_none";
    public const string BossTemplate = "gen_horn_boss_on";
    public const string BossAttribute = "gen_bs_horn_boss";

    public const string SkullBone = "bn_h_skull";

    /// <summary>Every style, in gene-template order. "filed" is the stump.</summary>
    public static readonly string[] Styles = ["ibex", "ram", "forward", "nubs", "filed"];
    public const string Filed = "filed";

    private const int Segments = 12;          // around (plus one seam duplicate for UVs)
    private const double Size = 1.7;          // every style's reach past the base
    private const double BaseLen = 1.4;       // the straight run every style starts with
    private const double FiledLen = 3.0;      // the stump
    private const double Bury = 1.0;          // the tube starts this far inside the skull
    private const double R0 = 2.0;            // radius at the skin
    private const double Flare = 0.45, FlareWidth = 1.0, FlareDepth = 0.9;
    private const double BossRadius = 2.2;    // skin mound radius, in multiples of R0
    private const double BossHeight = 1.0;
    private static readonly (float U, float V) CutUv = (0.5f, 0.975f);

    private sealed record Style(V[]? Points, double Tip, bool Ridges, double Taper = 1.35, bool Spiral = false, bool Clear = false);

    /// <summary>Control points after the base, in (out, up, back) from the base's end, times <see cref="Size"/>.</summary>
    private static readonly Dictionary<string, Style> Shapes = new()
    {
        ["ibex"] = new([new(0.4, 1.6, 1.0), new(1.0, 3.2, 3.4), new(1.5, 3.9, 6.4), new(1.7, 3.5, 9.0)], 0.10, true),
        ["forward"] = new([new(1.2, 0.9, -0.1), new(2.4, 2.2, -0.7), new(2.8, 3.9, -0.6)], 0.08, false),
        ["nubs"] = new([new(0.15, 0.9, 0.30), new(0.30, 1.6, 0.60)], 0.35, false, Taper: 1.1),
        // Stays thick like a real ram's, and wraps round the skull so it must be pushed clear of it.
        ["ram"] = new(null, 0.30, true, Taper: 0.85, Spiral: true, Clear: true),
    };

    /// <summary>Where one horn rises: the root on the skin, the skin normal there, the side, and the
    /// head vertices whose movement the horn must follow.</summary>
    public sealed record Root(V Point, V Normal, double Side, int[] Anchor);

    /// <summary>A horn pair's streams, ready for a mesh node, and which side each vertex belongs to.</summary>
    public sealed record Tube(float[] P, float[] N, float[] Ta, float[] Uv, int[] Tri, int[] SideOf);

    /// <summary>
    /// The two roots, left then right: the dominant-bone region of <c>bn_h_forehead_{l,r}_side</c>,
    /// its upper 45%, frontmost half — where a tiefling's horns rise.
    /// </summary>
    public static List<Root> Roots(float[] p, float[] n, int[] ix, float[] w, int leftForehead, int rightForehead)
    {
        int count = p.Length / 3, per = ix.Length / count;
        int Dominant(int v)
        {
            int best = 0;
            for (int k = 1; k < per; k++)
                if (w[v * per + k] > w[v * per + best]) best = k;
            return ix[v * per + best];
        }

        var roots = new List<Root>();
        foreach (var (side, bone) in new[] { (1.0, leftForehead), (-1.0, rightForehead) })
        {
            var region = Enumerable.Range(0, count).Where(v => Dominant(v) == bone && p[v * 3] * side > 0).ToList();
            if (region.Count < 4) throw new InvalidDataException("the head has no forehead-side skin to root horns in");
            var ys = region.Select(v => p[v * 3 + 1]).Order().ToList();
            float ycut = ys[(int)(ys.Count * 0.55)];
            var top = region.Where(v => p[v * 3 + 1] >= ycut).ToList();
            var zs = top.Select(v => p[v * 3 + 2]).Order().ToList();
            float zcut = zs[zs.Count / 2];
            var spot = top.Where(v => p[v * 3 + 2] <= zcut).ToList();
            var centre = new V(spot.Average(v => (double)p[v * 3]), spot.Average(v => (double)p[v * 3 + 1]), spot.Average(v => (double)p[v * 3 + 2]));

            int[] near = [.. Enumerable.Range(0, count).OrderBy(v => (At(p, v) - centre).LengthSquared).Take(4)];
            var root = new V(near.Average(v => (double)p[v * 3]), near.Average(v => (double)p[v * 3 + 1]), near.Average(v => (double)p[v * 3 + 2]));
            var normal = near.Aggregate(new V(0, 0, 0), (acc, v) => acc + At(n, v)).Normalized;
            roots.Add(new Root(root, normal, side, near));
        }

        return roots;
    }

    /// <summary>Both horns of one style as one set of streams, wound to the head's convention.</summary>
    public static Tube Build(string style, List<Root> roots, double scale, float[] headP, float[] headN, bool flip)
    {
        var p = new List<float>(); var n = new List<float>(); var ta = new List<float>();
        var uv = new List<float>(); var tri = new List<int>(); var sideOf = new List<int>();

        foreach (var root in roots)
        {
            var spine = Spine(style, root, scale, headP, headN);
            var (pos, nrm, tan, uvs, t) = Ring(style, spine, scale);

            // Wind each face to agree with its outward normals, then match the head's convention.
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                V fn = V.Cross(pos[t[i + 1]] - pos[t[i]], pos[t[i + 2]] - pos[t[i]]);
                bool agrees = V.Dot(fn, nrm[t[i]] + nrm[t[i + 1]] + nrm[t[i + 2]]) >= 0;
                if (agrees == flip) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            }

            int offset = p.Count / 3;
            tri.AddRange(t.Select(i => i + offset));
            foreach (var v in pos) p.AddRange([(float)v.X, (float)v.Y, (float)v.Z]);
            foreach (var v in nrm) n.AddRange([(float)v.X, (float)v.Y, (float)v.Z]);
            foreach (var v in tan) ta.AddRange([(float)v.X, (float)v.Y, (float)v.Z, 1f]);
            foreach (var (u, vv) in uvs) uv.AddRange([u, vv]);
            sideOf.AddRange(Enumerable.Repeat(root.Side > 0 ? 0 : 1, pos.Count));
        }

        return new Tube([.. p], [.. n], [.. ta], [.. uv], [.. tri], [.. sideOf]);
    }

    /// <summary>
    /// The skin mound: every head vertex within <see cref="BossRadius"/>·R0 of a root raised along its
    /// normal by a smooth (1 − (d/R)²)² bump, with normals and tangents turned to match.
    /// </summary>
    public static PointedEars.Result Boss(float[] p, float[] n, float[] ta, int[] tri, List<Root> roots, double scale)
    {
        var q = new double[p.Length];
        for (int i = 0; i < p.Length; i++) q[i] = p[i];
        double radius = BossRadius * R0 * scale, height = BossHeight * scale;

        foreach (var root in roots)
            for (int v = 0; v < p.Length / 3; v++)
            {
                double d = Math.Sqrt((At(p, v) - root.Point).LengthSquared);
                if (d >= radius) continue;
                double k = height * Math.Pow(1 - (d / radius) * (d / radius), 2);
                for (int c = 0; c < 3; c++) q[v * 3 + c] += n[v * 3 + c] * k;
            }

        return PointedEars.Finish(p, q, n, ta, tri);
    }

    // ---- spines ----------------------------------------------------------------------------------

    private static List<(V Point, double S)> Spine(string style, Root root, double scale, float[] headP, float[] headN)
    {
        V d0 = (root.Normal + new V(0, 0.9, 0) + new V(0.15 * root.Side, 0, 0.35)).Normalized;
        V start = root.Point + root.Normal * (-Bury * scale);
        V baseEnd = root.Point + d0 * (BaseLen * scale);

        if (style == Filed)
            return WithLength(Sample([start, root.Point + d0 * (FiledLen * 0.5 * scale), root.Point + d0 * (FiledLen * scale)], 10));

        var spec = Shapes[style];
        List<V> samples;
        if (spec.Spiral)
        {
            // A flattening spiral across the side of the head, swinging outward fast so it wraps
            // AROUND the skull: up, back, down behind the ear and forward under it.
            double side = root.Side, k = scale * Size;
            V c = baseEnd + new V(1.5 * side * k, -3.6 * k, 3.2 * k);
            double r0 = Math.Sqrt((baseEnd.Y - c.Y) * (baseEnd.Y - c.Y) + (baseEnd.Z - c.Z) * (baseEnd.Z - c.Z));
            double a0 = Math.Atan2(baseEnd.Y - c.Y, baseEnd.Z - c.Z);
            const double turns = 0.85;
            const int steps = 40;
            var pts = new List<V> { start, root.Point, baseEnd };
            for (int i = 1; i <= steps; i++)
            {
                double f = i / (double)steps, a = a0 - 2 * Math.PI * turns * f, r = r0 * (1 - 0.45 * f);
                pts.Add(new V(baseEnd.X + side * k * 5.0 * (1 - (1 - f) * (1 - f)), c.Y + r * Math.Sin(a), c.Z + r * Math.Cos(a)));
            }

            samples = Sample(pts, 3);
        }
        else
        {
            double k = scale * Size;
            var pts = new List<V> { start, root.Point, baseEnd };
            pts.AddRange(spec.Points!.Select(o => baseEnd + new V(o.X * root.Side * k, o.Y * k, o.Z * k)));
            samples = Sample(pts, 10);
        }

        if (spec.Clear) samples = ClearOfSkull(samples, headP, headN, (Bury + BaseLen) * scale, scale);
        return WithLength(samples);
    }

    private static List<V> Sample(IReadOnlyList<V> pts, int perSegment)
    {
        int n = (pts.Count - 1) * perSegment;
        return [.. Enumerable.Range(0, n + 1).Select(i => CatmullRom(pts, i / (double)n))];
    }

    private static List<(V, double)> WithLength(List<V> samples)
    {
        var result = new List<(V, double)> { (samples[0], 0.0) };
        double s = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            s += Math.Sqrt((samples[i] - samples[i - 1]).LengthSquared);
            result.Add((samples[i], s));
        }

        return result;
    }

    /// <summary>
    /// Pushes every sample past the base out of the head: its height above the nearest head vertex,
    /// along that vertex's normal, must cover the horn's own radius plus a margin. Then relaxes the
    /// moved stretch so the push leaves no kinks. The base never moves.
    /// </summary>
    private static List<V> ClearOfSkull(List<V> samples, float[] headP, float[] headN, double keepLen, double scale)
    {
        var lens = new List<double> { 0 };
        for (int i = 1; i < samples.Count; i++) lens.Add(lens[^1] + Math.Sqrt((samples[i] - samples[i - 1]).LengthSquared));
        double total = lens[^1];
        var free = Enumerable.Range(0, samples.Count).Where(i => lens[i] > keepLen + 0.3 * scale).ToList();
        if (free.Count == 0) return samples;

        var pts = new List<V>(samples);
        int count = headP.Length / 3;
        for (int pass = 0; pass < 4; pass++)
        {
            foreach (int i in free)
            {
                double f = (lens[i] - keepLen) / Math.Max(1e-6, total - keepLen);
                double need = R0 * scale * (1 - 0.6 * f) + 0.35 * scale;
                int nearest = 0;
                double best = double.MaxValue;
                for (int v = 0; v < count; v++)
                {
                    double dd = (At(headP, v) - pts[i]).LengthSquared;
                    if (dd < best) { best = dd; nearest = v; }
                }

                V nrm = At(headN, nearest);
                double height = V.Dot(pts[i] - At(headP, nearest), nrm);
                if (height < need) pts[i] = pts[i] + nrm * (need - height);
            }

            foreach (int i in free.Take(free.Count - 1))                  // relax; the tip stays put
                if (i + 1 < pts.Count) pts[i] = (pts[i - 1] + pts[i + 1] + pts[i] * 2) * 0.25;
        }

        return pts;
    }

    // ---- the tube --------------------------------------------------------------------------------

    private static double FlareAt(double s, double scale)
    {
        double below = Math.Max(0, s - (Bury - FlareDepth) * scale);
        return 1 + Flare * Math.Exp(-Math.Pow(below / (FlareWidth * scale), 2));
    }

    private static (List<V> Pos, List<V> Nrm, List<V> Tan, List<(float, float)> Uv, List<int> Tri) Ring(
        string style, List<(V Point, double S)> spine, double scale)
    {
        double length = spine[^1].S, skin = Bury * scale, visibleLength = Math.Max(1e-6, length - skin);
        Shapes.TryGetValue(style, out var spec);
        double tipRadius = (spec?.Tip ?? 0.4) * scale;

        // Pass 1: a parallel-transport frame and a radius per ring.
        V t0 = (spine[1].Point - spine[0].Point).Normalized;
        V a = (Math.Abs(t0.Y) < 0.95 ? V.Cross(t0, new V(0, 1, 0)) : V.Cross(t0, new V(1, 0, 0))).Normalized;
        V prev = t0;
        var frames = new List<(V C, V T, V A, V B)>();
        var radii = new List<double>();
        for (int i = 0; i < spine.Count; i++)
        {
            V t = (spine[Math.Min(i + 1, spine.Count - 1)].Point - spine[Math.Max(i - 1, 0)].Point).Normalized;
            V axis = V.Cross(prev, t);
            double sn = Math.Sqrt(axis.LengthSquared);
            if (sn > 1e-9)
            {
                V k = axis * (1 / sn);
                double angle = Math.Atan2(sn, V.Dot(prev, t)), ca = Math.Cos(angle), sa = Math.Sin(angle);
                a = a * ca + V.Cross(k, a) * sa + k * (V.Dot(k, a) * (1 - ca));
            }

            a = (a - t * V.Dot(a, t)).Normalized;
            V b = V.Cross(t, a);
            prev = t;
            frames.Add((spine[i].Point, t, a, b));

            double s = spine[i].S, visible = Math.Max(0, s - skin), flare = FlareAt(s, scale), r;
            if (style == Filed)
                r = R0 * scale * (1 - 0.28 * visible / (FiledLen * scale)) * flare;     // a truncated cone
            else
            {
                double f = visible / visibleLength;
                r = (tipRadius + (R0 * scale - tipRadius) * Math.Pow(1 - f, spec!.Taper)) * flare;
                if (spec.Ridges && visible > 0.6 * scale)
                    r *= 1 + 0.05 * Math.Sin(2 * Math.PI * visible / (0.6 * scale)) * Math.Pow(1 - f, 0.6);
            }

            radii.Add(r);
        }

        // Pass 2: rings, normals leaning toward the tip by the slope.
        var pos = new List<V>(); var nrm = new List<V>(); var tan = new List<V>(); var uv = new List<(float, float)>();
        for (int i = 0; i < spine.Count; i++)
        {
            var (c, t, fa, fb) = frames[i];
            int j0 = Math.Max(i - 1, 0), j1 = Math.Min(i + 1, spine.Count - 1);
            double slope = (radii[j1] - radii[j0]) / Math.Max(1e-6, spine[j1].S - spine[j0].S);
            float v = (float)(0.94 * spine[i].S / length);
            for (int j = 0; j <= Segments; j++)
            {
                double angle = 2 * Math.PI * j / Segments;
                V radial = fa * Math.Cos(angle) + fb * Math.Sin(angle);
                pos.Add(c + radial * radii[i]);
                nrm.Add((radial - t * slope).Normalized);
                tan.Add(t);
                uv.Add(((float)j / Segments, v));
            }
        }

        var tri = new List<int>();
        for (int i = 0; i < spine.Count - 1; i++)
            for (int j = 0; j < Segments; j++)
            {
                int a0 = i * (Segments + 1) + j, a1 = a0 + 1, b0 = a0 + Segments + 1, b1 = b0 + 1;
                tri.AddRange([a0, b0, a1, a1, b0, b1]);
            }

        int last = (spine.Count - 1) * (Segments + 1);
        var (lc, lt, la, lb) = frames[^1];
        double lr = radii[^1];
        if (style == Filed)
        {
            // Worn cut: a rounded bevel ring, then a flat cap (own vertices, flat normals) and centre.
            V top = lc + lt * (0.10 * scale);
            int bevel = pos.Count;
            for (int j = 0; j <= Segments; j++)
            {
                double angle = 2 * Math.PI * j / Segments;
                V radial = la * Math.Cos(angle) + lb * Math.Sin(angle);
                pos.Add(top + radial * (lr * 0.88)); nrm.Add((radial + lt).Normalized); tan.Add(lt); uv.Add(((float)j / Segments, 0.94f));
            }

            for (int j = 0; j < Segments; j++)
            {
                int a0 = last + j, a1 = a0 + 1, b0 = bevel + j, b1 = b0 + 1;
                tri.AddRange([a0, b0, a1, a1, b0, b1]);
            }

            int cap = pos.Count;
            for (int j = 0; j <= Segments; j++)
            {
                double angle = 2 * Math.PI * j / Segments;
                V radial = la * Math.Cos(angle) + lb * Math.Sin(angle);
                pos.Add(top + radial * (lr * 0.88)); nrm.Add(lt); tan.Add(la);
                uv.Add(((float)(CutUv.U + 0.02 * Math.Cos(angle)), (float)(CutUv.V + 0.01 * Math.Sin(angle))));
            }

            int centre = pos.Count;
            pos.Add(top); nrm.Add(lt); tan.Add(la); uv.Add(CutUv);
            for (int j = 0; j < Segments; j++) tri.AddRange([cap + j, centre, cap + j + 1]);
        }
        else
        {
            int tip = pos.Count;
            pos.Add(spine[^1].Point); nrm.Add(lt); tan.Add(la); uv.Add((0.5f, 0.94f));
            for (int j = 0; j < Segments; j++) tri.AddRange([last + j, tip, last + j + 1]);
        }

        return (pos, nrm, tan, uv, tri);
    }

    // ---- texture ---------------------------------------------------------------------------------

    /// <summary>
    /// The keratin diffuse, BGRA top row first (128 × 256; u around the horn, v root to tip): warm
    /// grey-brown with lengthwise fibres, pale-warm at the root and darkening to the tip, and a pale
    /// cut band with growth rings at the bottom for the filed cap. Deterministic — a fixed hash, not a
    /// seeded RNG, since horn is the same material on every map.
    /// </summary>
    public static (int Width, int Height, byte[] Bgra) KeratinDiffuse()
    {
        const int w = 128, h = 256;
        var raw = new double[w];
        for (int x = 0; x < w; x++)
        {
            uint z = (uint)x * 2654435761u ^ 0x9E3779B9u;
            z ^= z >> 15; z *= 0x85EBCA6Bu; z ^= z >> 13;
            raw[x] = z / (double)uint.MaxValue * 2 - 1;
        }

        var fibre = new double[w];
        for (int x = 0; x < w; x++) fibre[x] = (raw[(x + w - 1) % w] + 2 * raw[x] + raw[(x + 1) % w]) / 4;

        var bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            double v = y / (double)(h - 1);
            for (int x = 0; x < w; x++)
            {
                double r, g, b;
                if (v > 0.955)
                {
                    double ring = 0.5 + 0.5 * Math.Sin(Math.Sqrt(Math.Pow(x / (double)w - 0.5, 2) + Math.Pow(v - 0.975, 2)) * 260);
                    double k0 = 0.92 + 0.08 * ring;
                    (r, g, b) = (176 * k0, 160 * k0, 134 * k0);
                }
                else
                {
                    double t = Math.Min(v / 0.94, 1.0), f;
                    if (t < 0.6) { f = t / 0.6; (r, g, b) = (132 - 24 * f, 116 - 20 * f, 96 - 16 * f); }
                    else { f = (t - 0.6) / 0.4; (r, g, b) = (108 - 46 * f, 96 - 40 * f, 80 - 32 * f); }
                    double k = 1 + 0.10 * fibre[x] + 0.04 * Math.Sin(y * 0.9 + fibre[x] * 6);
                    (r, g, b) = (r * k, g * k, b * k);
                }

                int o = (y * w + x) * 4;
                bgra[o] = Byte(b); bgra[o + 1] = Byte(g); bgra[o + 2] = Byte(r); bgra[o + 3] = 255;
            }
        }

        return (w, h, bgra);
    }

    private static byte Byte(double c) => (byte)Math.Clamp((int)c, 0, 255);

    private static V At(float[] a, int v) => new(a[v * 3], a[v * 3 + 1], a[v * 3 + 2]);

    /// <summary>Uniform Catmull-Rom through the points, t in 0..1 over the whole chain.</summary>
    private static V CatmullRom(IReadOnlyList<V> pts, double t)
    {
        int n = pts.Count - 1;
        double f = Math.Min(t * n, n - 1e-9);
        int i = (int)f;
        double u = f - i;
        V p0 = pts[Math.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.Min(n, i + 2)];
        return (p1 * 2 + (p2 - p0) * u + (p0 * 2 - p1 * 5 + p2 * 4 - p3) * (u * u) + (p1 * 3 - p0 - p2 * 3 + p3) * (u * u * u)) * 0.5;
    }
}
