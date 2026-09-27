namespace Ck3MapGen.Emit.Relief;

/// <summary>
/// The motif library: every symbol a generated faith's icon can carry, and the frames that can
/// surround one. Each motif draws into unit space (it fills roughly the circle of radius 0.95)
/// and knows nothing of frames, scale or rotation — <see cref="Build"/> handles those.
///
/// Heights are relative: a plate at z0 = 0.05 sits proud of one at 0, and where two shapes
/// overlap the higher one wins, which is how a boss sits on a disc or a star floats in a crescent.
/// </summary>
public static class ReliefMotifs
{
    private const double Tau = 2 * Math.PI;

    public static readonly IReadOnlyDictionary<string, Action<ReliefCanvas>> Motifs =
        new Dictionary<string, Action<ReliefCanvas>>(StringComparer.Ordinal)
        {
            ["sun"] = Sun,
            ["sun12"] = cv => SunRays(cv, 12, wavy: true),
            ["sun_plain"] = cv => SunRays(cv, 16, wavy: false),
            ["estoile"] = cv => Estoile(cv),
            ["star8"] = cv => StarPlate(cv, 8, pierced: false),
            ["star7"] = cv => StarPlate(cv, 7, pierced: false),
            ["star5_pierced"] = cv => StarPlate(cv, 5, pierced: true),
            ["crescent_star"] = CrescentStar,
            ["crescent"] = Crescent,
            ["triple_moon"] = cv => Scaled(cv, 1.35, TripleMoon),
            ["eye"] = Eye,
            ["knot"] = Knot,
            ["triskele"] = Triskele,
            ["tree"] = Tree,
            ["antlers"] = Antlers,
            ["flame"] = Flame,
            ["wheel8"] = cv => Wheel(cv, 8),
            ["wheel6"] = cv => Wheel(cv, 6),
            ["lotus"] = cv => Scaled(cv, 1.22, Lotus, new Pt(0, 0.12)),
            ["rosette8"] = cv => Rosette(cv, 8),
            ["rosette12"] = cv => Rosette(cv, 12),
            ["hexagram"] = Hexagram,
            ["labrys"] = Labrys,
            ["hammer"] = Hammer,
            ["trident"] = Trident,
            ["key"] = Key,
            ["horned_disc"] = HornedDisc,
            ["mountain"] = Mountain,
            ["ouroboros"] = Ouroboros,
            ["cross_patee"] = CrossPatee,
            ["ringed_cross"] = RingedCross,
            ["looped_cross"] = LoopedCross,
            ["skull"] = Skull,
            ["hand"] = Hand,
            ["scales"] = Scales,
            ["chalice"] = Chalice,
            ["waves"] = Waves,
            ["sword"] = cv => Sword(cv, 0),
            ["crossed_swords"] = CrossedSwords,
            ["sabre"] = Sabre,
            ["leaf_sword"] = LeafSword,
            ["war_hammer"] = WarHammer,
            ["hammer_anvil"] = HammerAnvil,
            ["crossed_hammers"] = CrossedHammers,
            ["smith_hammer"] = cv => SmithHammer(cv, 0),
        };

    public static readonly string[] Frames = ["none", "ring", "medallion", "lobed", "rayed", "wreath"];

    /// <summary>
    /// A finished design: the frame drawn, the motif drawn inside it, and the frame's uncovered
    /// field (if it has one) marked as inlay alongside whatever the motif marked itself.
    /// </summary>
    /// <param name="engraving">
    /// The pattern cut into a medallion's or lobed plaque's enamelled field, by
    /// <see cref="Engraving"/> name. Ignored by frames without a field.
    /// </param>
    public static (ReliefCanvas Canvas, Mask? FieldInlay) Build(string motif, string frame, int n = 1000, double extent = 1.2,
        string? engraving = null)
    {
        var cv = new ReliefCanvas(n, extent);
        var pattern = Enum.TryParse<Engraving>(engraving, out var e) ? e : Engraving.Sunburst;
        double scale = DrawFrame(cv, frame, pattern);
        cv.Scale = scale;
        var before = (double[])cv.H.Clone();
        Motifs[motif](cv);
        cv.Scale = 1;
        cv.Rot = 0;
        cv.Mirror = 1;
        cv.Offset = default;

        Mask? inlay = null;
        if (cv.Field is { } field)
        {
            inlay = new Mask(n);
            for (int y = field.Y0; y < field.Y1; y++)
                for (int x = field.X0; x < field.X1; x++)
                {
                    int i = y * n + x;
                    if (field.Bits[i] && cv.H[i] <= before[i] + 1e-6) inlay.Set(x, y);
                }
        }
        return (cv, inlay);
    }

    // ================================================================ geometry helpers

    private static double[] Lin(double a, double b, int n)
    {
        var r = new double[n];
        for (int i = 0; i < n; i++) r[i] = n == 1 ? a : a + (b - a) * i / (n - 1);
        return r;
    }

    private static Pt[] Bezier(Pt p0, Pt p1, Pt p2, int n = 80)
    {
        var r = new Pt[n];
        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 0 : i / (double)(n - 1), u = 1 - t;
            r[i] = u * u * p0 + 2 * u * t * p1 + t * t * p2;
        }
        return r;
    }

    private static Pt[] Bezier(Pt p0, Pt p1, Pt p2, Pt p3, int n = 80)
    {
        var r = new Pt[n];
        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 0 : i / (double)(n - 1), u = 1 - t;
            r[i] = u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
        }
        return r;
    }

    private static Pt[] StarPts(Pt c, double ro, double ri, int n, double phase = Math.PI / 2)
    {
        var r = new Pt[2 * n];
        for (int i = 0; i < 2 * n; i++)
            r[i] = c + Pt.Polar(i % 2 == 0 ? ro : ri, phase + i * Math.PI / n);
        return r;
    }

    /// <summary>A closed circle or ellipse sampled without repeating its first point.</summary>
    private static Pt[] Circ(double r, int n = 1400, Pt c = default, double? rx = null, double? ry = null)
    {
        var p = new Pt[n];
        for (int i = 0; i < n; i++)
        {
            double a = Tau * i / n;
            p[i] = new Pt(c.X + (rx ?? r) * Math.Cos(a), c.Y + (ry ?? r) * Math.Sin(a));
        }
        return p;
    }

    private static Pt[] Rect(double x0, double y0, double x1, double y1)
        => [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];

    /// <summary>A right half-outline, top to bottom, completed by its mirror image into a closed one.</summary>
    private static Pt[] Mirrored(IReadOnlyList<Pt> right)
        => right.Concat(right.Reverse().Select(p => new Pt(-p.X, p.Y))).ToArray();

    /// <summary>A closed Catmull-Rom curve through <paramref name="p"/>: a smooth outline from a few key points.</summary>
    private static Pt[] CatmullRom(IReadOnlyList<Pt> p, int per = 16)
    {
        var o = new List<Pt>();
        int n = p.Count;
        for (int i = 0; i < n; i++)
        {
            Pt p0 = p[(i - 1 + n) % n], p1 = p[i], p2 = p[(i + 1) % n], p3 = p[(i + 2) % n];
            for (int k = 0; k < per; k++)
            {
                double t = k / (double)per, t2 = t * t, t3 = t2 * t;
                o.Add(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3));
            }
        }
        return o.ToArray();
    }

    /// <summary>A straight engraved line of half-width <paramref name="hw"/>.</summary>
    private static void CutLine(ReliefCanvas cv, Pt a, Pt b, double hw, double depth)
    {
        var d = (b - a).Unit;
        var n = new Pt(-d.Y, d.X) * hw;
        cv.Engrave(cv.Poly([a + n, b + n, b - n, a - n]), depth, hw * 0.9);
    }

    /// <summary>A thin engraved curve: the polyline widened by <paramref name="hw"/> either side.</summary>
    private static void CutCurve(ReliefCanvas cv, Pt[] pts, double hw, double depth)
    {
        var left = new Pt[pts.Length];
        var right = new Pt[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            var d = (pts[Math.Min(i + 1, pts.Length - 1)] - pts[Math.Max(i - 1, 0)]).Unit;
            var n = new Pt(-d.Y, d.X) * hw;
            left[i] = pts[i] + n;
            right[i] = pts[i] - n;
        }
        cv.Engrave(cv.Poly(left.Concat(right.Reverse()).ToArray()), depth, hw * 0.9);
    }

    /// <summary>A densely sampled closed polygon, its corners rounded by <paramref name="round"/>.</summary>
    private static Pt[] ClosedPoly(Pt[] pts, int nPer, double round)
    {
        var outp = new List<Pt>();
        int k = pts.Length;
        for (int i = 0; i < k; i++)
        {
            Pt a = pts[(i - 1 + k) % k], b = pts[i], c = pts[(i + 1) % k];
            Pt u1 = (a - b).Unit, u2 = (c - b).Unit;
            Pt p0 = b + u1 * round, p2 = b + u2 * round;
            outp.AddRange(Bezier(p0, b, p2, 40));
            Pt u3 = (b - c).Unit;
            Pt s1 = c + u3 * round;
            foreach (double q in Lin(0, 1, nPer)) outp.Add(p2 + (s1 - p2) * q);
        }
        return outp.ToArray();
    }

    private static void Beads(ReliefCanvas cv, double R, int n, double r, double z0 = 0, double phase = 0)
    {
        for (int i = 0; i < n; i++) cv.Dome(Pt.Polar(R, phase + i * Tau / n), r, z0);
    }

    private static void Scaled(ReliefCanvas cv, double k, Action<ReliefCanvas> draw, Pt off = default)
    {
        double s0 = cv.Scale;
        var o0 = cv.Offset;
        cv.Scale = s0 * k;
        cv.Offset = new Pt(o0.X + off.X * s0, o0.Y + off.Y * s0);
        draw(cv);
        cv.Scale = s0;
        cv.Offset = o0;
    }

    // ================================================================ weaving

    /// <summary>A closed curve's heights so that consecutive crossings alternate over and under.</summary>
    private static double[] WeaveHeights(Pt[] pts, double amp, int sub = 4)
    {
        int n = pts.Length;
        var q = Enumerable.Range(0, (n + sub - 1) / sub).Select(i => pts[i * sub]).ToArray();
        int m = q.Length;
        var steps = new double[m - 1];
        for (int i = 0; i < m - 1; i++) steps[i] = (q[i + 1] - q[i]).Length;
        double thr = Median(steps) * 1.5;

        var hits = new SortedSet<int>();
        for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++)
            {
                int g = Math.Abs(i - j);
                g = Math.Min(g, m - g);
                if (g < m / 20) continue;
                if ((q[i] - q[j]).Length < thr) { hits.Add(i); break; }
            }

        var passages = Cluster(hits.ToList(), m);
        var centres = passages.Select(c => Wrap(c.Select(p => p >= c[0] ? p : p + m).Average(), m) * sub)
                              .OrderBy(c => c).ToList();

        var z = new double[n];
        if (centres.Count == 0) return z;
        double sigma = n / (centres.Count * 2.2);
        for (int k = 0; k < centres.Count; k++)
        {
            double sign = k % 2 == 0 ? 1 : -1;
            for (int t = 0; t < n; t++)
            {
                double d = Math.Abs(t - centres[k]);
                d = Math.Min(d, n - d);
                z[t] += sign * amp * Math.Exp(-(d / sigma) * (d / sigma));
            }
        }
        return z;
    }

    private static double Wrap(double v, int m) => ((v % m) + m) % m;

    private static double Median(double[] a)
    {
        var s = a.OrderBy(v => v).ToArray();
        return s.Length == 0 ? 0 : s[s.Length / 2];
    }

    /// <summary>Groups sorted indices into runs, joining a run that wraps past the end.</summary>
    private static List<List<int>> Cluster(List<int> idx, int m)
    {
        var groups = new List<List<int>>();
        foreach (int h in idx)
        {
            if (groups.Count > 0 && h - groups[^1][^1] <= 3) groups[^1].Add(h);
            else groups.Add([h]);
        }
        if (groups.Count > 1 && groups[0][0] + m - groups[^1][^1] <= 3)
        {
            var last = groups[^1];
            groups.RemoveAt(groups.Count - 1);
            last.AddRange(groups[0]);
            groups[0] = last;
        }
        return groups;
    }

    /// <summary>
    /// Heights for several closed curves so that every crossing, including crossings between
    /// different curves, alternates. Signs are propagated curve to curve through shared crossings.
    /// </summary>
    private static double[][] WeaveMulti(Pt[][] curves, double amp)
    {
        const int sub = 3;
        var qs = curves.Select(c => Enumerable.Range(0, (c.Length + sub - 1) / sub).Select(i => c[i * sub]).ToArray()).ToArray();
        var owner = new List<int>();
        var index = new List<int>();
        var all = new List<Pt>();
        for (int i = 0; i < qs.Length; i++)
            for (int k = 0; k < qs[i].Length; k++) { all.Add(qs[i][k]); owner.Add(i); index.Add(k); }

        var st = new double[qs[0].Length - 1];
        for (int i = 0; i < st.Length; i++) st[i] = (qs[0][i + 1] - qs[0][i]).Length;
        double thr = Median(st) * 1.5;

        var passages = new List<List<(double Centre, int Curve, int Index)>>();
        for (int i = 0; i < qs.Length; i++)
        {
            var q = qs[i];
            int m = q.Length;
            var hit = new List<int>();
            var partner = new int[m];
            for (int k = 0; k < m; k++)
            {
                double best = double.MaxValue;
                int bj = -1;
                for (int j = 0; j < all.Count; j++)
                {
                    if (owner[j] == i)
                    {
                        int g = Math.Abs(k - index[j]);
                        g = Math.Min(g, m - g);
                        if (g < m / 20) continue;
                    }
                    double d = (q[k] - all[j]).Length;
                    if (d < best) { best = d; bj = j; }
                }
                partner[k] = bj;
                if (best < thr) hit.Add(k);
            }
            var ps = new List<(double, int, int)>();
            foreach (var g in Cluster(hit, m))
            {
                double c = Wrap(g.Select(x => x >= g[0] ? x : x + m).Average(), m);
                int kk = g[g.Count / 2];
                ps.Add((c, owner[partner[kk]], index[partner[kk]]));
            }
            ps.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            passages.Add(ps);
        }

        var signs = new int[]?[curves.Length];
        void Assign(int i, int start, int s)
        {
            int n = passages[i].Count;
            var row = new int[n];
            for (int t = 0; t < n; t++) row[(start + t) % n] = t % 2 == 0 ? s : -s;
            signs[i] = row;
        }

        var queue = new Stack<int>();
        for (int i = 0; i < curves.Length; i++)
        {
            if (signs[i] is null && passages[i].Count > 0) { Assign(i, 0, 1); queue.Push(i); }
            while (queue.Count > 0)
            {
                int ci = queue.Pop();
                for (int k = 0; k < passages[ci].Count; k++)
                {
                    var (_, oj, oidx) = passages[ci][k];
                    if (oj == ci || signs[oj] is not null || passages[oj].Count == 0) continue;
                    int m = qs[oj].Length;
                    int kk = 0;
                    double bd = double.MaxValue;
                    for (int p = 0; p < passages[oj].Count; p++)
                    {
                        double dd = Math.Abs(passages[oj][p].Centre - oidx);
                        dd = Math.Min(dd, m - dd);
                        if (dd < bd) { bd = dd; kk = p; }
                    }
                    Assign(oj, kk, -signs[ci]![k]);
                    queue.Push(oj);
                }
            }
        }

        var zs = new double[curves.Length][];
        for (int i = 0; i < curves.Length; i++)
        {
            int n = curves[i].Length;
            var z = new double[n];
            var cs = passages[i].Select(p => p.Centre * sub).ToList();
            for (int k = 0; k < cs.Count; k++)
            {
                var gaps = cs.Where(o => o != cs[k]).Select(o => Math.Min(Math.Abs(cs[k] - o), n - Math.Abs(cs[k] - o))).ToList();
                double sigma = 0.42 * (gaps.Count > 0 ? gaps.Min() : n / 4.0);
                for (int t = 0; t < n; t++)
                {
                    double d = Math.Abs(t - cs[k]);
                    d = Math.Min(d, n - d);
                    z[t] += signs[i]![k] * amp * Math.Exp(-(d / sigma) * (d / sigma));
                }
            }
            zs[i] = z;
        }
        return zs;
    }

    /// <summary>Flat carved bands along closed curves, woven where they cross.</summary>
    private static void Band(ReliefCanvas cv, Pt[][] curves, double w = 0.065, double amp = 0.1, bool weave = true, double z0 = 0)
    {
        var zs = weave ? WeaveMulti(curves, amp) : curves.Select(c => new double[c.Length]).ToArray();
        for (int i = 0; i < curves.Length; i++)
            cv.Stamps(curves[i], w, zs[i].Select(v => v + z0).ToArray(), flat: 0.55, groove: 0.3, gw: 0.3);
        if (weave) cv.InterlaceGaps(gap: w * 0.55, thresh: w * 0.45 + 0.03);
    }

    // ================================================================ shared parts

    /// <summary>A flame tongue: a tapering blade along a centre line, with a groove echoing its edge.</summary>
    private static void Tongue(ReliefCanvas cv, Pt[] centre, double W, double z0, double power = 1.1)
    {
        int n = centre.Length;
        var left = new Pt[n];
        var right = new Pt[n];
        for (int i = 0; i < n; i++)
        {
            Pt tang = i == 0 ? centre[1] - centre[0]
                : i == n - 1 ? centre[n - 1] - centre[n - 2]
                : (centre[i + 1] - centre[i - 1]) * 0.5;
            tang = tang.Unit;
            var nrm = new Pt(-tang.Y, tang.X);
            double s = i / (double)(n - 1);
            double w = W * Math.Pow(1 - s, power) * Math.Sqrt(Math.Clamp(s * 5, 0, 1));
            left[i] = centre[i] + nrm * w;
            right[i] = centre[i] - nrm * w;
        }
        var m = cv.Poly(left.Concat(right.Reverse()).ToArray());
        cv.Plate(m, z0, W * 0.6, 0.07, ReliefCanvas.Profile.Smooth);
        cv.InsetGroove(m, 0.045, 0.013, 0.02);
    }

    private static void Leaf(ReliefCanvas cv, Pt p, double ang, double L = 0.15, double W = 0.05, double z = 0.03)
    {
        var pts = Lin(0, 1, 40).Select(s => p + Pt.Polar(s * L, ang)).ToArray();
        cv.Stroke(pts, t => W * Math.Pow(Math.Sin(Math.PI * t), 0.8) + 0.004, z, flat: 0.8, groove: 0.4, gw: 0.25);
    }

    private static Mask Petal(ReliefCanvas cv, Pt b, double ang, double L, double W, double z0, bool groove = true)
    {
        var d = Pt.Polar(1, ang);
        var nrm = new Pt(-d.Y, d.X);
        var s = Lin(0, 1, 160);
        var left = new Pt[s.Length];
        var right = new Pt[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            Pt c = b + d * (s[i] * L);
            double w = W * Math.Pow(Math.Sin(Math.PI * Math.Clamp(s[i] * 0.92 + 0.04, 0, 1)), 0.85) * (1 - 0.35 * s[i]);
            left[i] = c + nrm * w;
            right[i] = c - nrm * w;
        }
        var m = cv.Poly(left.Concat(right.Reverse()).ToArray());
        cv.Plate(m, z0, W * 0.7, 0.06, ReliefCanvas.Profile.Smooth);
        if (groove) cv.InsetGroove(m, 0.035, 0.011, 0.016);
        return m;
    }

    // ================================================================ suns and stars

    private static void Sun(ReliefCanvas cv)
    {
        for (int i = 0; i < 16; i++)
        {
            double a = Math.PI / 2 + i * Tau / 16;
            if (i % 2 == 0)
            {
                cv.Plate(cv.Poly([Pt.Polar(0.40, a - 0.13), Pt.Polar(0.97, a), Pt.Polar(0.40, a + 0.13)]),
                    0, 0.2, 0.08, ReliefCanvas.Profile.Linear);
            }
            else
            {
                var pts = Lin(0, 1, 60).Select(s =>
                {
                    double r = 0.40 + 0.44 * s, off = 0.05 * Math.Sin(s * Tau * 1.25);
                    return new Pt(r * Math.Cos(a) - off * Math.Sin(a), r * Math.Sin(a) + off * Math.Cos(a));
                }).ToArray();
                cv.Stroke(pts, t => 0.055 * (1 - t) + 0.012, 0, flat: 0.9);
            }
        }
        cv.Plate(cv.Circle(default, 0.46), 0.05, 0.06, 0.05);
        cv.InsetGroove(cv.Circle(default, 0.46), 0.05, 0.016, 0.02);
        Beads(cv, 0.33, 16, 0.028, 0.09);
        cv.Dome(default, 0.22, 0.08, 0.07);
        cv.Engrave(cv.Ring(default, 0.105, 0.125), 0.02, 0.01);
    }

    private static void SunRays(ReliefCanvas cv, int n, bool wavy)
    {
        for (int i = 0; i < 2 * n; i++)
        {
            double a = Math.PI / 2 + i * Tau / (2 * n);
            if (i % 2 == 0)
            {
                double w = 1.6 / n;
                cv.Plate(cv.Poly([Pt.Polar(0.4, a - w), Pt.Polar(0.97, a), Pt.Polar(0.4, a + w)]),
                    0, 0.2, 0.08, ReliefCanvas.Profile.Linear);
            }
            else if (wavy)
            {
                var pts = Lin(0, 1, 60).Select(s =>
                {
                    double r = 0.4 + 0.42 * s, off = 0.05 * Math.Sin(s * Tau * 1.25);
                    return new Pt(r * Math.Cos(a) - off * Math.Sin(a), r * Math.Sin(a) + off * Math.Cos(a));
                }).ToArray();
                cv.Stroke(pts, t => 0.05 * (1 - t) + 0.012, 0, flat: 0.9);
            }
        }
        var disc = cv.Circle(default, 0.46);
        cv.Plate(disc, 0.05, 0.06, 0.05);
        cv.InsetGroove(disc, 0.05, 0.016, 0.02);
        cv.Accent.OrWith(cv.Circle(default, 0.34));
        cv.Dome(default, 0.2, 0.08, 0.06);
    }

    private static void Estoile(ReliefCanvas cv, int n = 6)
    {
        for (int i = 0; i < n; i++)
        {
            double a = Math.PI / 2 + i * Tau / n;
            var pts = Lin(0, 1, 120).Select(s =>
            {
                double r = 0.1 + 0.85 * s, off = 0.06 * Math.Sin(s * Tau * 1.5) * s;
                return new Pt(r * Math.Cos(a) - off * Math.Sin(a), r * Math.Sin(a) + off * Math.Cos(a));
            }).ToArray();
            cv.Stroke(pts, t => 0.13 * Math.Pow(1 - t, 1.05) + 0.006, 0, flat: 0.85, groove: 0.3);
        }
        cv.Dome(default, 0.18, 0.04, 0.08);
        cv.Accent.OrWith(cv.Circle(default, 0.12));
    }

    private static void StarPlate(ReliefCanvas cv, int n, bool pierced)
    {
        var m = cv.Poly(StarPts(default, 0.97, n >= 7 ? 0.42 : 0.4, n));
        cv.Plate(m, 0, 0.5, 0.2, ReliefCanvas.Profile.Linear);
        if (pierced)
        {
            cv.Cut(cv.Circle(default, 0.15));
            cv.Engrave(cv.Ring(default, 0.15, 0.2), 0.03, 0.03);
        }
        else
        {
            cv.Dome(default, 0.14, 0.12, 0.05);
            cv.Accent.OrWith(cv.Circle(default, 0.12));
        }
    }

    // ================================================================ moons

    private static void CrescentStar(ReliefCanvas cv)
    {
        var m = cv.Circle(new Pt(-0.06, -0.04), 0.84) & !cv.Circle(new Pt(0.26, 0.2), 0.68);
        cv.Plate(m, 0, 0.08, 0.07);
        cv.InsetGroove(m, 0.05, 0.014, 0.02);
        cv.Plate(cv.Poly(StarPts(new Pt(0.34, 0.30), 0.30, 0.11, 8)), 0.02, 0.12, 0.08, ReliefCanvas.Profile.Linear);
        cv.Dome(new Pt(0.34, 0.30), 0.06, 0.07, 0.04);
    }

    private static void Crescent(ReliefCanvas cv)
    {
        var m = cv.Circle(new Pt(0, -0.08), 0.80) & !cv.Circle(new Pt(0, 0.16), 0.66);
        cv.Plate(m, 0, 0.08, 0.07);
        cv.InsetGroove(m, 0.05, 0.014, 0.02);
    }

    private static void TripleMoon(ReliefCanvas cv)
    {
        var full = cv.Circle(default, 0.34);
        cv.Plate(full, 0.03, 0.08, 0.07);
        cv.InsetGroove(full, 0.05, 0.013, 0.018);
        cv.Accent.OrWith(cv.Circle(default, 0.24));
        foreach (int sg in new[] { 1, -1 })
        {
            var m = cv.Circle(new Pt(0.6 * sg, 0), 0.36) & !cv.Circle(new Pt(0.75 * sg, 0), 0.32);
            cv.Plate(m, 0, 0.07, 0.06);
            cv.InsetGroove(m, 0.04, 0.012, 0.016);
        }
    }

    // ================================================================ eye

    private static void Eye(ReliefCanvas cv)
    {
        for (int i = 0; i < 24; i++)
        {
            double a = Math.PI / 2 + i * Tau / 24;
            double ro = i % 2 == 0 ? 0.97 : 0.80, w = i % 2 == 0 ? 0.085 : 0.07;
            cv.Plate(cv.Poly([Pt.Polar(0.55, a - w), Pt.Polar(ro, a), Pt.Polar(0.55, a + w)]),
                0, 0.2, 0.06, ReliefCanvas.Profile.Linear);
        }
        cv.Plate(cv.Circle(default, 0.58), 0.03, 0.03, 0.02);
        cv.Plate(cv.Ring(default, 0.50, 0.60), 0.04, 0.035, 0.04);
        Beads(cv, 0.55, 28, 0.022, 0.07);
        var inner = cv.Circle(default, 0.48);
        for (int i = 0; i < 40; i++)
        {
            double a = i * Tau / 40, c = Math.Cos(a), s = Math.Sin(a), w = 0.007;
            var m = cv.Poly([new(0.14 * c - w * s, 0.14 * s + w * c), new(0.47 * c - w * s, 0.47 * s + w * c),
                             new(0.47 * c + w * s, 0.47 * s - w * c), new(0.14 * c + w * s, 0.14 * s - w * c)]);
            cv.Engrave(m & inner, 0.012, 0.006);
        }
        var alm = cv.Circle(new Pt(0, -0.26), 0.48) & cv.Circle(new Pt(0, 0.26), 0.48);
        cv.Plate(alm, 0.05, 0.05, 0.04);
        cv.InsetGroove(alm, 0.035, 0.012, 0.015);
        cv.Dome(default, 0.16, 0.07, 0.06);
        cv.Engrave(cv.Circle(default, 0.065), 0.05, 0.03);
        cv.Stroke(Bezier(new(-0.42, 0.26), new(0, 0.52), new(0.42, 0.26), 80),
            t => 0.03 * Math.Sin(Math.PI * t) + 0.012, 0.06, flat: 0.9);
    }

    // ================================================================ knots and spirals

    private static Pt[] Trefoil(int n, double scale, Pt c = default)
        => Enumerable.Range(0, n).Select(i =>
        {
            double t = Tau * i / n;
            return c + new Pt(Math.Sin(t) + 2 * Math.Sin(2 * t), Math.Cos(t) - 2 * Math.Cos(2 * t)) * scale;
        }).ToArray();

    private static void Knot(ReliefCanvas cv)
    {
        const double tube = 0.085;
        var pts = Trefoil(2400, 0.29);
        cv.Stamps(pts, tube, WeaveHeights(pts, 0.1), flat: 0.7, groove: 0.35, gw: 0.3);
        cv.InterlaceGaps(gap: tube * 0.55, thresh: tube * 0.7 + 0.03);
    }

    private static void Triskele(ReliefCanvas cv)
    {
        // Each arm starts at the centre, on the near side of its own spiral circle, and winds inward.
        for (int k = 0; k < 3; k++)
        {
            double th = Math.PI / 2 + k * Tau / 3;
            var C = Pt.Polar(0.44, th);
            var spiral = Lin(0, 1, 500).Select(u =>
            {
                double phi = th + Math.PI + u * Tau * 1.9;
                double rho = 0.42 * Math.Pow(1 - u, 1.15) + 0.035;
                return C + Pt.Polar(rho, phi);
            }).ToArray();
            cv.Stroke(spiral, t => 0.075 - 0.035 * t, 0, flat: 0.7, groove: 0.3);
            cv.Dome(spiral[^1], 0.055, 0, 0.05);
        }
        cv.Dome(default, 0.13, 0.03, 0.06);
        cv.Accent.OrWith(cv.Circle(default, 0.08));
    }

    /// <summary>
    /// A serpent biting its tail, head in profile at the top: skull on the outside of the ring,
    /// lower jaw inside, the tail running into an open gape. Enamelled diamonds run down the spine.
    /// </summary>
    private static void Ouroboros(ReliefCanvas cv)
    {
        const double R = 0.64, top = Math.PI / 2;
        // The head runs counter-clockwise from its back to its snout. The body starts hidden under
        // the head and runs clockwise almost all the way round, its tail ending inside the jaws.
        double aBack = top - 0.15, aSnout = top + 0.48;
        double aNeck = top + 0.05, aTail = top + 0.26;
        double sweep = Tau - (aTail - aNeck);

        // Body: full width for most of its length, pinched at the neck, tapering to a fine tail.
        double W(double t) => (0.024 + 0.108 * Math.Pow(1 - SmoothStep(0.4, 1.0, t), 0.9)) * (0.72 + 0.28 * SmoothStep(0.02, 0.2, t));
        Pt B(double t) => Pt.Polar(R, aNeck - t * sweep);
        cv.Stroke(Lin(0, 1, 1400).Select(B).ToArray(), W, 0, flat: 0.8);

        // Diamonds tip to tip along the spine, sized to the body under them.
        double arc = sweep * R;
        for (double s = 0.10 * arc; s < 0.88 * arc;)
        {
            double w = W(s / arc), L = 0.95 * w, tc = (s + L) / arc;
            Pt c = B(tc), tan = (B(tc + 0.001) - B(tc - 0.001)).Unit, nrm = new(-tan.Y, tan.X);
            var diamond = cv.Poly([c + tan * L, c + nrm * (0.5 * w), c - tan * L, c - nrm * (0.5 * w)]);
            cv.Engrave(diamond, 0.01, 0.012);
            cv.Accent.OrWith(diamond);
            s += 2 * L + 0.012;
        }

        // Head outline along the ring: widest at the jaw hinge, a blunt rounded snout.
        double HW(double u) => u < 0.32 ? 0.095 + 0.12 * SmoothStep(0, 0.32, u) : 0.215 - 0.08 * Math.Pow((u - 0.32) / 0.68, 1.4);
        double HA(double u) => aBack + u * (aSnout - aBack);
        Pt HC(double u) => Pt.Polar(R, HA(u));
        Pt HN(double u) => Pt.Polar(1, HA(u));         // outward
        Pt HT(double u) => Pt.Polar(1, HA(u) + top);   // toward the snout
        var us = Lin(0, 1, 120);
        double rTip = HW(1);
        var outline = new List<Pt>(us.Select(u => HC(u) + HN(u) * HW(u)));
        outline.AddRange(Lin(0, Math.PI, 40).Select(q => HC(1) + HN(1) * (rTip * Math.Cos(q)) + HT(1) * (rTip * 0.75 * Math.Sin(q))));
        outline.AddRange(us.Reverse().Select(u => HC(u) - HN(u) * HW(u)));

        // The gape sits below the midline, so the upper jaw is the heavier one.
        Pt tip = HC(1) + HT(1) * (rTip * 1.2) - HN(1) * 0.02;
        var gape = cv.Poly([HC(0.62) - HN(0.62) * 0.01, tip + HN(1) * 0.055, tip - HN(1) * 0.075]);
        cv.Plate(cv.Poly(outline) & !gape, 0.05, 0.11, 0.08);

        // Mouth line back from the gape, the eye under a brow ridge, a nostril.
        var mu = Lin(0.62, 0.3, 30);
        Pt Mouth(double u, double side) => HC(u) - HN(u) * (0.01 + 0.03 * (0.62 - u) / 0.32 + side);
        cv.Engrave(cv.Poly(mu.Select(u => Mouth(u, 0.008)).Concat(mu.Reverse().Select(u => Mouth(u, -0.008))).ToArray()), 0.03, 0.008);
        Pt eye = HC(0.5) + HN(0.5) * 0.085;
        cv.Dome(eye, 0.05, 0.12, 0.045);
        cv.Accent.OrWith(cv.Circle(eye, 0.047));
        var brow = Lin(0.34, 0.68, 40).Select(u => HC(u) + HN(u) * (0.145 - 0.022 * Math.Pow((u - 0.5) / 0.17, 2))).ToArray();
        cv.Stroke(brow, t => 0.022 * Math.Sin(Math.PI * t) + 0.006, 0.11, flat: 0.8);
        cv.Engrave(cv.Circle(HC(0.92) + HN(0.92) * 0.065, 0.015), 0.025, 0.012);
    }

    private static double SmoothStep(double e0, double e1, double x)
    {
        double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static void Hexagram(ReliefCanvas cv)
    {
        const int k = 3;
        const double R = 0.84;
        var curves = new Pt[2][];
        for (int j = 0; j < 2; j++)
        {
            var pts = Enumerable.Range(0, k).Select(i => Pt.Polar(R, Math.PI / 2 + j * Math.PI / k + i * Tau / k)).ToArray();
            curves[j] = ClosedPoly(pts, 400, 0.08);
        }
        Band(cv, curves, 0.06, 0.1);
        cv.Dome(default, 0.14, 0, 0.06);
        cv.Accent.OrWith(cv.Circle(default, 0.13));
    }

    // ================================================================ living things

    private static void Tree(ReliefCanvas cv)
    {
        cv.Stamps(Circ(0.88, 1600), 0.055, 0, flat: 0.7, groove: 0.3);
        Beads(cv, 0.88, 12, 0.035, 0.03, Math.PI / 12);
        using (cv.Clip(cv.Circle(default, 0.84)))
        {
            foreach (int sgn in new[] { 1, -1 })
            {
                // Canopy: primaries sweep from the trunk out to the ring, leaves alternating along them.
                foreach (var (y0, th, r0) in new[] { (-0.02, 18.0, 0.055), (0.14, 44.0, 0.05), (0.3, 68.0, 0.042) })
                {
                    var e = new Pt(0.8 * Math.Cos(th * Math.PI / 180) * sgn, 0.8 * Math.Sin(th * Math.PI / 180));
                    var p0 = new Pt(0, y0);
                    var pts = Bezier(p0, p0 + new Pt(0.32 * sgn, 0.02), e - new Pt(0.1 * sgn, 0.22), e, 120);
                    cv.Stroke(pts, t => r0 * (1 - 0.6 * t), 0, flat: 0.75);
                    double[] at = [0.38, 0.58, 0.78];
                    for (int k = 0; k < at.Length; k++)
                    {
                        int i = (int)(at[k] * (pts.Length - 1));
                        var tang = pts[Math.Min(i + 1, pts.Length - 1)] - pts[i - 1];
                        double baseA = Math.Atan2(tang.Y, tang.X);
                        Leaf(cv, pts[i], baseA + (k % 2 == 0 ? 1 : -1) * 0.9, 0.16, 0.05);
                    }
                }
                foreach (var (th, r0) in new[] { (-22.0, 0.05), (-50.0, 0.055), (-76.0, 0.05) })
                {
                    var e = new Pt(0.8 * Math.Cos(th * Math.PI / 180) * sgn, 0.8 * Math.Sin(th * Math.PI / 180));
                    var p0 = new Pt(0, -0.46);
                    var pts = Bezier(p0, p0 + new Pt(0.12 * sgn, -0.08), e + new Pt(-0.18 * sgn, 0.12), e, 100);
                    cv.Stroke(pts, t => r0 * (1 - 0.5 * t), 0, flat: 0.75);
                }
            }
            cv.Stroke([new(0, -0.6), new(0, 0.3)], t => 0.11 - 0.04 * t, 0.02, flat: 0.8);
            cv.Stroke([new(0, 0.3), new(0, 0.66)], t => 0.07 - 0.045 * t, 0.02, flat: 0.8);
            Leaf(cv, new Pt(0, 0.62), Math.PI / 2, 0.2, 0.06, 0.03);
            foreach (var p in new Pt[] { new(0.36, 0.52), new(-0.36, 0.52), new(0.52, 0.18), new(-0.52, 0.18), new(0.2, 0.3), new(-0.2, 0.3) })
                cv.Dome(p, 0.05, 0.04, 0.05);
        }
    }

    /// <summary>A stag's skull face on, a jewel on its brow, antlers branching from the pedicles.</summary>
    private static void Antlers(ReliefCanvas cv)
    {
        foreach (int sg in new[] { 1, -1 })
        {
            // Main beam: out from the pedicle, up, and curling back in at the crown.
            var beam = Bezier(new(0.12 * sg, -0.1), new(0.52 * sg, -0.04), new(0.84 * sg, 0.44), new(0.48 * sg, 0.94), 240);
            double BW(double t) => 0.09 - 0.056 * t;
            cv.Stroke(beam, BW, 0, flat: 0.8);
            cv.Dome(beam[0], 0.085, 0.01, 0.06);     // the burr where the antler leaves the skull

            // Tines rise from the inside of the beam and lean in toward the centre line as they
            // rise. The brow tine, lowest, stays short and steep: angled in any further, the two
            // met over the forehead.
            foreach (var (t, L, ang, bend) in new[] { (0.12, 0.24, 112.0, 0.0), (0.3, 0.4, 100.0, 0.1), (0.52, 0.38, 94.0, 0.1), (0.75, 0.26, 104.0, 0.06) })
            {
                var p = beam[(int)(t * (beam.Length - 1))];
                double a = sg > 0 ? ang * Math.PI / 180 : Math.PI - ang * Math.PI / 180;
                var d = Pt.Polar(1, a);
                var tine = Bezier(p, p + d * (0.45 * L), p + d * (0.85 * L) + new Pt(-bend * sg, 0), p + d * L + new Pt(-bend * 1.6 * sg, -0.02), 60);
                double r0 = BW(t) * 0.95;
                cv.Stroke(tine, s => r0 * Math.Pow(1 - s, 0.65) + 0.01, 0.005, flat: 0.8);
            }
        }

        // The skull: broad brow between the pedicles, eye sockets, a long tapering snout.
        var skull = cv.Poly(CatmullRom(Mirrored(new Pt[]
        {
            new(0.001, -0.06), new(0.14, -0.08), new(0.24, -0.18), new(0.25, -0.3), new(0.17, -0.44),
            new(0.12, -0.62), new(0.09, -0.84), new(0.05, -0.93), new(0.001, -0.94),
        })));
        cv.Plate(skull, 0.03, 0.14, 0.09, ReliefCanvas.Profile.Smooth);
        cv.Stroke([new(0, -0.3), new(0, -0.86)], t => 0.022 + 0.01 * t, 0.1, flat: 0.7);   // nasal ridge
        foreach (int sg in new[] { 1, -1 })
        {
            cv.Engrave(cv.Poly(CatmullRom([new(0.12 * sg, -0.2), new(0.21 * sg, -0.22), new(0.22 * sg, -0.3), new(0.14 * sg, -0.32)], 10)), 0.07, 0.03);
            cv.Engrave(cv.Ellipse(new Pt(0.035 * sg, -0.88), 0.022, 0.032, false), 0.04, 0.015);
        }
        var gem = cv.Poly([new(0, -0.1), new(0.07, -0.19), new(0, -0.28), new(-0.07, -0.19)]);
        cv.Plate(gem, 0.1, 0.03, 0.04);
        cv.Accent.OrWith(gem);
    }

    /// <summary>A human skull face on, its eye sockets inlaid.</summary>
    private static void Skull(ReliefCanvas cv)
    {
        // Cranium, a pinch at the temples, cheekbones flaring out, the upper jaw, then the
        // mandible's angle and a narrow chin.
        var outline = cv.Poly(CatmullRom(Mirrored(new Pt[]
        {
            new(0.001, 0.82), new(0.3, 0.77), new(0.48, 0.6), new(0.56, 0.36), new(0.55, 0.14), new(0.49, 0.0),
            new(0.53, -0.13), new(0.44, -0.25), new(0.31, -0.3), new(0.29, -0.4), new(0.35, -0.5), new(0.32, -0.64),
            new(0.2, -0.76), new(0.001, -0.8),
        })));
        cv.Plate(outline, 0, 0.26, 0.1, ReliefCanvas.Profile.Smooth);
        using (cv.Clip(outline)) cv.Dome(new Pt(0, 0.3), 0.52, 0.02, 0.16);

        foreach (int sg in new[] { 1, -1 })
        {
            // Temple hollow, brow ridge, a deep socket slanting down at its outer corner, cheekbone.
            cv.Engrave(cv.Ellipse(new Pt(0.47 * sg, 0.06), 0.07, 0.12, false), 0.03, 0.05);
            cv.Stroke(Bezier(new(0.04 * sg, 0.15), new(0.14 * sg, 0.22), new(0.3 * sg, 0.22), new(0.42 * sg, 0.12), 50),
                t => 0.035 * Math.Sin(Math.PI * (0.15 + 0.7 * t)) + 0.01, 0.12, flat: 0.8);
            var socket = cv.Poly(CatmullRom([new(0.06 * sg, 0.1), new(0.2 * sg, 0.15), new(0.36 * sg, 0.08), new(0.38 * sg, -0.06),
                                             new(0.26 * sg, -0.14), new(0.1 * sg, -0.09)]));
            cv.Engrave(socket, 0.15, 0.06);
            cv.Accent.OrWith(socket);
            cv.Stroke(Bezier(new(0.16 * sg, -0.19), new(0.3 * sg, -0.21), new(0.44 * sg, -0.17), new(0.52 * sg, -0.1), 40),
                t => 0.03 * Math.Sin(Math.PI * t) + 0.012, 0.1, flat: 0.8);
            // Where the mandible meets the skull.
            CutCurve(cv, Bezier(new(0.3 * sg, -0.3), new(0.34 * sg, -0.4), new(0.33 * sg, -0.5), new(0.28 * sg, -0.58), 20), 0.008, 0.02);
        }
        // Nasal cavity: two lobes under a point.
        cv.Engrave(cv.Poly(CatmullRom([new(0, -0.05), new(0.06, -0.17), new(0.065, -0.26), new(0.02, -0.25), new(0, -0.21),
                                       new(-0.02, -0.25), new(-0.065, -0.26), new(-0.06, -0.17)], 10)), 0.1, 0.03);

        // Teeth: two rows of rounded blocks meeting at the bite, set in a recess.
        static Pt[] Tooth(double x, double yTop, double yBot, bool upper)
        {
            const double w = 0.032, r = 0.022;
            return upper
                ? [new(x - w, yTop), new(x + w, yTop), new(x + w, yBot + r), new(x + w - r * 0.4, yBot), new(x - w + r * 0.4, yBot), new(x - w, yBot + r)]
                : [new(x - w, yBot), new(x + w, yBot), new(x + w, yTop - r), new(x + w - r * 0.4, yTop), new(x - w + r * 0.4, yTop), new(x - w, yTop - r)];
        }
        cv.Engrave(cv.Poly(Rect(-0.25, -0.58, 0.25, -0.33)), 0.05, 0.02);
        foreach (double x in Lin(-0.19, 0.19, 6))
        {
            double drop = 0.02 * (1 - Math.Abs(x) / 0.19);
            cv.Plate(cv.Poly(Tooth(x, -0.33, -0.44 - drop, upper: true)), 0.04, 0.025, 0.05);
            cv.Plate(cv.Poly(Tooth(x, -0.465 - drop, -0.57, upper: false)), 0.03, 0.025, 0.05);
        }
    }

    /// <summary>
    /// An open right hand raised palm out, a jewel in the palm. One sculpted piece: fingers,
    /// thumb, palm and wrist are a single silhouette raised together, the fingers told apart by
    /// cut lines rather than gaps. Drawn as separate tubes it read as a glove.
    /// </summary>
    private static void Hand(ReliefCanvas cv)
    {
        static Pt[] Capsule(Pt b, double ang, double len, double w0, double w1)
        {
            Pt d = Pt.Polar(1, ang), n = new(-d.Y, d.X), tip = b + d * len;
            return Lin(0, 1, 30).Select(s => b + d * (s * len) + n * (w0 + (w1 - w0) * s))
                .Concat(Lin(0, Math.PI, 24).Select(q => tip + n * (w1 * Math.Cos(q)) + d * (w1 * Math.Sin(q))))
                .Concat(Lin(1, 0, 30).Select(s => b + d * (s * len) - n * (w0 + (w1 - w0) * s)))
                .ToArray();
        }

        // Index to little finger: base, lean from vertical (toward the thumb is positive), length.
        (Pt Base, double Lean, double Len)[] fingers =
        [
            (new(-0.235, 0.06), 0.1, 0.5), (new(-0.08, 0.1), 0.03, 0.58),
            (new(0.075, 0.08), -0.05, 0.54), (new(0.22, 0.02), -0.14, 0.42),
        ];
        var hand = cv.Poly(CatmullRom([new(-0.3, 0.12), new(0.29, 0.08), new(0.34, -0.2), new(0.26, -0.48), new(0.22, -0.74),
                                       new(-0.22, -0.74), new(-0.26, -0.48), new(-0.34, -0.22)], 20));
        foreach (var (b, lean, len) in fingers)
            hand.OrWith(cv.Poly(Capsule(b - new Pt(0, 0.12), Math.PI / 2 + lean, len + 0.12, 0.084, 0.068)));
        hand.OrWith(cv.Poly(Capsule(new(-0.22, -0.32), Math.PI / 2 + 0.8, 0.44, 0.1, 0.078)));
        cv.Plate(hand, 0, 0.16, 0.1, ReliefCanvas.Profile.Smooth);

        // Cuts between the fingers from the tips down to the knuckles, a crease across each
        // finger, and the crease at the root of the thumb.
        for (int i = 0; i + 1 < fingers.Length; i++)
        {
            var (b0, l0, n0) = fingers[i];
            var (b1, l1, n1) = fingers[i + 1];
            Pt mid = (b0 + b1) * 0.5;
            double lean = (l0 + l1) / 2;
            CutLine(cv, mid + Pt.Polar(0.02, Math.PI / 2 + lean), mid + Pt.Polar(Math.Min(n0, n1), Math.PI / 2 + lean), 0.009, 0.05);
        }
        foreach (var (b, lean, len) in fingers)
        {
            Pt d = Pt.Polar(1, Math.PI / 2 + lean), n = new(-d.Y, d.X), k = b + d * (len * 0.55);
            CutLine(cv, k - n * 0.04, k + n * 0.04, 0.006, 0.015);
        }
        CutCurve(cv, Bezier(new(-0.335, 0.0), new(-0.275, -0.165), new(-0.215, -0.285), new(-0.12, -0.385), 30), 0.006, 0.02);

        Pt jewel = new(-0.01, -0.32);
        cv.Dome(jewel, 0.11, 0.07, 0.065);
        cv.Accent.OrWith(cv.Circle(jewel, 0.1));
        cv.Engrave(cv.Ring(jewel, 0.13, 0.145), 0.02, 0.008);
        cv.Stroke([new(-0.25, -0.72), new(0.25, -0.72)], 0.06, 0.05, flat: 0.8);
        foreach (double x in Lin(-0.18, 0.18, 5)) cv.Dome(new Pt(x, -0.72), 0.04, 0.09, 0.04);
    }

    private static void Lotus(ReliefCanvas cv)
    {
        var b = new Pt(0, -0.34);
        foreach (var (a, L, W) in new[] { (62.0, 0.62, 0.15), (38.0, 0.72, 0.16) })
            foreach (int sg in new[] { 1, -1 })
                Petal(cv, b, (90 - a * sg) * Math.PI / 180, L, W, -0.02);
        foreach (int sg in new[] { 1, -1 })
            Petal(cv, b, (90 - 20.0 * sg) * Math.PI / 180, 0.8, 0.17, 0.02);
        var m = Petal(cv, b, Math.PI / 2, 0.9, 0.19, 0.05);
        double hi = cv.U(0.09);
        var crown = new Mask(cv.N);
        for (int y = m.Y0; y < m.Y1; y++)
            for (int x = m.X0; x < m.X1; x++)
                if (m.Bits[y * cv.N + x] && cv.H[y * cv.N + x] > hi) crown.Set(x, y);
        cv.Accent.OrWith(crown);
        foreach (int sg in new[] { 1, -1 })
            cv.Stroke(Bezier(new(0, -0.42), new(0.35 * sg, -0.46), new(0.62 * sg, -0.36), new(0.8 * sg, -0.24), 80),
                t => 0.05 * (1 - t) + 0.012, 0.02, flat: 0.8, groove: 0.3);
        cv.Plate(cv.Poly([new(-0.46, -0.52), new(0.46, -0.52), new(0.38, -0.44), new(-0.38, -0.44)]), 0.03, 0.03, 0.03);
        for (int i = 0; i < 7; i++) cv.Dome(new Pt(-0.3 + i * 0.1, -0.62), 0.035, 0, 0.035);
    }

    private static void Rosette(ReliefCanvas cv, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double a = Math.PI / 2 + (i + 0.5) * Tau / n;
            Petal(cv, Pt.Polar(0.1, a), a, 0.6, 0.12, -0.02, groove: false);
        }
        for (int i = 0; i < n; i++)
        {
            double a = Math.PI / 2 + i * Tau / n;
            Petal(cv, Pt.Polar(0.08, a), a, 0.88, 0.17, 0.02);
        }
        cv.Dome(default, 0.2, 0.06, 0.08);
        Beads(cv, 0.2, 12, 0.03, 0.08);
        cv.Accent.OrWith(cv.Circle(default, 0.14));
    }

    // ================================================================ fire and water

    private static void Flame(ReliefCanvas cv)
    {
        using (cv.Clip(cv.RowsAbove(-0.12)))
        {
            foreach (int sg in new[] { 1, -1 })
            {
                Tongue(cv, Bezier(new(0.3 * sg, -0.2), new(0.52 * sg, 0.0), new(0.36 * sg, 0.18), new(0.56 * sg, 0.34), 120), 0.12, -0.02);
                Tongue(cv, Bezier(new(0.14 * sg, -0.2), new(0.38 * sg, 0.12), new(0.12 * sg, 0.34), new(0.34 * sg, 0.66), 150), 0.17, 0);
            }
            Tongue(cv, Lin(0, 1, 200).Select(s => new Pt(-0.12 * Math.Sin(Math.PI * 1.4 * s) * s, -0.2 + 1.14 * s)).ToArray(), 0.25, 0.03);
        }
        var bowl = cv.Ellipse(new Pt(0, -0.2), 0.46, 0.32, lowerHalf: true);
        cv.Plate(bowl, 0.03, 0.08, 0.07);
        cv.InsetGroove(bowl, 0.05, 0.012, 0.018);
        cv.Plate(cv.Poly([new(-0.54, -0.22), new(0.54, -0.22), new(0.5, -0.12), new(-0.5, -0.12)]), 0.08, 0.03, 0.035);
        for (int i = 0; i < 5; i++) cv.Dome(new Pt(-0.24 + i * 0.12, -0.35), 0.032, 0.1, 0.03);
        cv.Stroke([new(0, -0.5), new(0, -0.8)], 0.06, 0.02, flat: 0.7);
        cv.Dome(new Pt(0, -0.63), 0.1, 0.03, 0.08);
        cv.Plate(cv.Poly([new(-0.14, -0.78), new(0.14, -0.78), new(0.36, -0.9), new(-0.36, -0.9)]), 0.02, 0.04, 0.04);
    }

    private static void Mountain(ReliefCanvas cv)
    {
        (Pt Tip, double X0, double X1)[] peaks =
            [(new Pt(0, 0.72), -0.62, 0.62), (new Pt(-0.5, 0.28), -0.95, -0.02), (new Pt(0.52, 0.34), 0.06, 0.95)];
        foreach (var (tip, x0, x1) in peaks.OrderBy(p => p.Tip.Y))
        {
            var m = cv.Poly([new(x0, -0.36), tip, new(x1, -0.36)]);
            cv.Plate(m, tip.Y < 0.5 ? 0 : 0.03, 0.6, 0.18, ReliefCanvas.Profile.Linear);
            cv.Accent.OrWith(m & cv.Poly([new(tip.X - 0.5, tip.Y - 0.2), new(tip.X, tip.Y + 0.1), new(tip.X + 0.5, tip.Y - 0.2)]));
        }
        double[] ys = [-0.5, -0.66, -0.82];
        for (int i = 0; i < ys.Length; i++)
        {
            int ii = i;
            var pts = Lin(-0.8 + 0.08 * i, 0.8 - 0.08 * i, 200).Select(x => new Pt(x, ys[ii] + 0.035 * Math.Sin(x * 12 + ii))).ToArray();
            cv.Stroke(pts, 0.03, 0, flat: 0.8);
        }
    }

    /// <summary>
    /// Three rows of running scroll: each wave rises, crests and curls under into a spiral, the
    /// next rising from beneath it. An enamel bead sits in every curl.
    /// </summary>
    private static void Waves(ReliefCanvas cv)
    {
        const double p = 0.42, r0 = 0.15;
        foreach (var (y0, x0, n) in new[] { (0.5, -0.56, 3), (0.0, -0.8, 4), (-0.5, -0.56, 3) })
        {
            Pt start = new(x0 - 0.12, y0 - 0.1);
            for (int k = 0; k < n; k++)
            {
                var c = new Pt(x0 + k * p + 0.2, y0 + 0.02);
                var top = c + new Pt(0, r0);
                var rise = Bezier(start, start + new Pt(0.12, 0), top - new Pt(0.2, 0.02), top, 50);
                var curl = Lin(0, 1, 160).Select(u => c + Pt.Polar(r0 * (1 - 0.78 * u), Math.PI / 2 - u * Tau * 0.95));
                cv.Stroke(rise.Concat(curl).ToArray(), t => 0.058 - 0.026 * Math.Max(0, t - 0.45) / 0.55, 0, flat: 0.85);
                cv.Dome(c, 0.045, 0.03, 0.04);
                cv.Accent.OrWith(cv.Circle(c, 0.04));
                start = c + new Pt(0, -r0);
            }
            cv.Stroke(Bezier(start, start + new Pt(0.08, 0), start + new Pt(0.14, 0), start + new Pt(0.2, 0.03), 20), 0.056, 0, flat: 0.85);
        }
    }

    private static void Trident(ReliefCanvas cv)
    {
        cv.Stroke([new(0, -0.95), new(0, 0.3)], 0.05, 0, flat: 0.8);
        cv.Stroke(Bezier(new(-0.52, 0.52), new(-0.46, 0.08), new(0.46, 0.08), new(0.52, 0.52), 120), 0.05, 0.02, flat: 0.8);
        cv.Stroke([new(0, 0.2), new(0, 0.66)], 0.05, 0.02, flat: 0.8);
        foreach (var (x, y) in new[] { (-0.52, 0.52), (0.0, 0.66), (0.52, 0.52) })
            cv.Stroke(Lin(0, 1, 60).Select(s => new Pt(x, y + 0.3 * s)).ToArray(),
                t => 0.1 * Math.Pow(1 - t, 1.1) + 0.004, 0.04, flat: 0.9, groove: 0.35);
        cv.Dome(new Pt(0, 0.2), 0.08, 0.04, 0.05);
        cv.Accent.OrWith(cv.Circle(new Pt(0, 0.2), 0.06));
        foreach (double y in new[] { -0.5, -0.6 })
            cv.Stroke([new(-0.07, y), new(0.07, y)], 0.024, 0.03, flat: 0.9);
    }

    // ================================================================ tools and weapons

    private static void Wheel(ReliefCanvas cv, int spokes)
    {
        Band(cv, [Circ(0.72)], 0.07, weave: false);
        cv.Plate(cv.Ring(default, 0.6, 0.66), 0, 0.02, 0.02);
        for (int i = 0; i < spokes; i++)
        {
            double a = Math.PI / 2 + i * Tau / spokes;
            var d = Pt.Polar(1, a);
            cv.Stroke(Lin(0, 1, 60).Select(s => d * (0.16 + 0.54 * s)).ToArray(),
                t => 0.035 + 0.02 * Math.Exp(-((t - 0.5) / 0.14) * ((t - 0.5) / 0.14)), 0, flat: 0.8);
            cv.Stroke(Lin(0.76, 0.9, 20).Select(s => d * s).ToArray(), 0.035, 0, flat: 0.8);
            cv.Dome(d * 0.92, 0.065, 0, 0.07);
        }
        cv.Dome(default, 0.2, 0.02, 0.08);
        cv.Engrave(cv.Ring(default, 0.09, 0.11), 0.02, 0.01);
        cv.Accent.OrWith(cv.Circle(default, 0.085));
    }

    private static void Labrys(ReliefCanvas cv)
    {
        cv.Stroke([new(0, -0.92), new(0, 0.72)], 0.05, 0, flat: 0.8);
        foreach (double y in new[] { -0.62, -0.72, -0.82 })
            cv.Stroke([new(-0.065, y), new(0.065, y)], 0.022, 0.03, flat: 0.9);
        foreach (int sg in new[] { 1, -1 })
        {
            var up = Bezier(new(0.07 * sg, 0.2), new(0.3 * sg, 0.24), new(0.55 * sg, 0.42), new(0.74 * sg, 0.56), 60);
            var edge = Bezier(new(0.74 * sg, 0.56), new(0.9 * sg, 0.2), new(0.9 * sg, -0.2), new(0.74 * sg, -0.56), 80);
            var low = Bezier(new(0.74 * sg, -0.56), new(0.55 * sg, -0.42), new(0.3 * sg, -0.24), new(0.07 * sg, -0.2), 60);
            var m = cv.Poly(up.Concat(edge).Concat(low).ToArray());
            cv.Plate(m, 0, 0.3, 0.1, ReliefCanvas.Profile.Linear);
            cv.InsetGroove(m, 0.05, 0.012, 0.012);
        }
        cv.Plate(cv.Poly(Rect(-0.1, -0.24, 0.1, 0.24)), 0.04, 0.03, 0.04);
        cv.Dome(new Pt(0, 0.78), 0.075, 0, 0.07);
    }

    private static void Hammer(ReliefCanvas cv)
    {
        // A pendant hammer: short banded haft with a suspension ring, broad flared head below.
        cv.Stroke([new(0, -0.1), new(0, 0.6)], 0.1, 0, flat: 0.8);
        foreach (double y in new[] { 0.14, 0.3, 0.46 })
            cv.Stroke([new(-0.11, y), new(0.11, y)], 0.03, 0.05, flat: 0.9);
        cv.Stamps(Circ(0.14, 500, new Pt(0, 0.78)), 0.05, 0, flat: 0.7, groove: 0.3);
        var left = Bezier(new(-0.3, 0.02), new(-0.36, -0.3), new(-0.56, -0.5), new(-0.78, -0.86), 60);
        var right = left.Reverse().Select(p => new Pt(-p.X, p.Y)).ToArray();
        var bottom = Bezier(new(-0.78, -0.86), new(-0.3, -0.7), new(0.3, -0.7), new(0.78, -0.86), 60);
        var head = cv.Poly(new Pt[] { new(0.3, 0.02), new(-0.3, 0.02) }.Concat(left).Concat(bottom).Concat(right).ToArray());
        cv.Plate(head, 0.03, 0.12, 0.09, ReliefCanvas.Profile.Smooth);
        cv.InsetGroove(head, 0.05, 0.014, 0.02);
        var kp = Trefoil(900, 0.085, new Pt(0, -0.38));
        cv.Stamps(kp, 0.028, WeaveHeights(kp, 0.03).Select(v => v + 0.1).ToArray(), flat: 0.7);
        cv.Accent.OrWith(cv.Circle(new Pt(0, -0.36), 0.05));
        cv.Dome(new Pt(0, -0.36), 0.045, 0.1, 0.03);
    }

    /// <summary>
    /// A smith's hammer, haft down: flat striking face to the left, a wedge peen to the right,
    /// the head centred on (0.01, 0.53) and the haft ending at <paramref name="bottom"/>.
    /// </summary>
    private static void SmithHammer(ReliefCanvas cv, double z, double bottom = -0.86)
    {
        cv.Stroke([new(0, bottom), new(0, 0.44)], t => 0.06 - 0.012 * t, z, flat: 0.85);
        foreach (double y in Lin(bottom + 0.28, bottom + 0.06, 4)) CutLine(cv, new(-0.06, y - 0.025), new(0.06, y + 0.025), 0.007, 0.02);
        cv.Dome(new Pt(0, bottom - 0.01), 0.065, z, 0.05);

        var head = cv.Poly([
            new(-0.42, 0.38), new(-0.45, 0.42), new(-0.45, 0.64), new(-0.42, 0.68), new(-0.1, 0.68), new(-0.07, 0.72),
            new(0.09, 0.72), new(0.13, 0.66), new(0.46, 0.565), new(0.48, 0.53), new(0.46, 0.495), new(0.13, 0.4),
            new(0.09, 0.34), new(-0.07, 0.34), new(-0.1, 0.38)]);
        cv.Plate(head, z + 0.04, 0.08, 0.08, ReliefCanvas.Profile.Smooth);
        cv.InsetGroove(head, 0.035, 0.011, 0.014);
        CutLine(cv, new(-0.36, 0.4), new(-0.36, 0.66), 0.01, 0.025);
        cv.Dome(new Pt(0.01, 0.53), 0.065, z + 0.1, 0.045);
        cv.Accent.OrWith(cv.Circle(new Pt(0.01, 0.53), 0.055));
    }

    /// <summary>
    /// Two smith's hammers in saltire, the second mirrored so both faces point inward. Crossed
    /// any steeper than this, the two faces met at the top and read as a roof.
    /// </summary>
    private static void CrossedHammers(ReliefCanvas cv)
    {
        double rot0 = cv.Rot, s0 = cv.Scale, m0 = cv.Mirror;
        var o0 = cv.Offset;
        cv.Scale = s0 * 0.96;
        cv.Offset = new Pt(o0.X, o0.Y - 0.1 * s0);
        cv.Rot = rot0 - 0.98;
        SmithHammer(cv, 0);
        cv.Mirror = -m0;
        cv.Rot = rot0 + 0.98;
        SmithHammer(cv, 0.05);
        cv.Rot = rot0;
        cv.Scale = s0;
        cv.Mirror = m0;
        cv.Offset = o0;
    }

    /// <summary>
    /// An anvil in profile, a smith's hammer coming down head first onto its face, enamel
    /// sparks flying. The first draft had the hammer upside down (haft into the anvil, head in
    /// the air), a flat blade for a horn, and round beads for sparks that read as pins.
    /// </summary>
    private static void HammerAnvil(ReliefCanvas cv)
    {
        double scale0 = cv.Scale;
        cv.Scale = scale0 * 1.08;
        Pt A = new(0.12, 0.02);
        Pt P(double x, double y) => new Pt(x, y) + A;
        Pt[] Bz(Pt a, Pt b, Pt c, Pt d, int n) => Bezier(a + A, b + A, c + A, d + A, n);

        // Body: heel overhang at the right, waist, arched feet, underside running out to the horn.
        var body = cv.Poly(new[] { P(-0.3, -0.02), P(0.56, -0.02), P(0.56, -0.15) }
            .Concat(Bz(new(0.52, -0.17), new(0.36, -0.19), new(0.22, -0.24), new(0.18, -0.34), 16))
            .Concat(Bz(new(0.18, -0.36), new(0.2, -0.48), new(0.3, -0.56), new(0.46, -0.6), 16))
            .Concat(new[] { P(0.48, -0.7), P(0.14, -0.7) })
            .Concat(Bz(new(0.12, -0.7), new(0.08, -0.62), new(-0.08, -0.62), new(-0.12, -0.7), 16))
            .Concat(new[] { P(-0.48, -0.7), P(-0.46, -0.6) })
            .Concat(Bz(new(-0.3, -0.56), new(-0.2, -0.48), new(-0.18, -0.36), new(-0.2, -0.3), 16))
            .Concat(Bz(new(-0.22, -0.26), new(-0.28, -0.2), new(-0.36, -0.17), new(-0.42, -0.16), 12))
            .Concat(new[] { P(-0.42, -0.05), P(-0.3, -0.05) })
            .ToArray());
        cv.Plate(body, 0, 0.14, 0.1, ReliefCanvas.Profile.Smooth);

        // Horn: a rounded cone, its top level with the table, tapering to a blunt point.
        cv.Stroke(Bz(new(-0.36, -0.11), new(-0.56, -0.1), new(-0.76, -0.08), new(-0.92, -0.04), 60),
            t => 0.062 * Math.Pow(1 - t, 0.8) + 0.012, 0.02, flat: 0.85);

        // Steel face plate, the step down to the table, the seam where the face meets the body,
        // and a boss on the waist.
        cv.Plate(cv.Poly([P(-0.28, 0.0), P(0.56, 0.0), P(0.56, -0.06), P(-0.28, -0.06)]), 0.08, 0.03, 0.03);
        cv.Plate(cv.Poly([P(-0.42, -0.03), P(-0.28, -0.03), P(-0.28, -0.08), P(-0.42, -0.08)]), 0.06, 0.02, 0.02);
        CutLine(cv, P(-0.26, -0.09), P(0.52, -0.09), 0.007, 0.02);
        cv.Dome(P(0.0, -0.36), 0.06, 0.08, 0.04);
        cv.Accent.OrWith(cv.Circle(P(0.0, -0.36), 0.05));

        // Sparks: four-pointed stars scattered either side of the strike, clear of the hammer.
        Pt strike = P(-0.06, 0.02);
        foreach (var (deg, dist, r) in new[] { (148.0, 0.3, 0.1), (170.0, 0.46, 0.078), (192.0, 0.28, 0.06), (126.0, 0.46, 0.06),
                                                 (18.0, 0.38, 0.09), (40.0, 0.3, 0.06), (4.0, 0.58, 0.052) })
        {
            var sc = strike + Pt.Polar(dist, deg * Math.PI / 180);
            var star = cv.Poly(Enumerable.Range(0, 8).Select(i => sc + Pt.Polar(i % 2 == 0 ? r : r * 0.3, Math.PI / 2 + i * Math.PI / 4)).ToArray());
            cv.Plate(star, 0.02, r * 0.6, r * 0.5, ReliefCanvas.Profile.Linear);
            cv.Accent.OrWith(star);
        }

        // The hammer, rotated head down with its haft rising to the upper right, and placed so
        // its striking face lands on the strike point.
        double rot0 = cv.Rot, s0 = cv.Scale;
        var o0 = cv.Offset;
        const double k = 0.7, rot = 2.0;
        double c = Math.Cos(rot), s = Math.Sin(rot);
        Pt headLocal = new(0.01, 0.53);
        Pt headRot = new Pt(headLocal.X * c - headLocal.Y * s, headLocal.X * s + headLocal.Y * c) * k;
        Pt target = strike + new Pt(-0.135, 0.31);
        cv.Scale = s0 * k;
        cv.Rot = rot0 + rot;
        cv.Offset = new Pt(o0.X + (target.X - headRot.X) * s0, o0.Y + (target.Y - headRot.Y) * s0);
        SmithHammer(cv, 0.04, bottom: -0.5);
        cv.Rot = rot0;
        cv.Offset = o0;
        cv.Scale = scale0;
    }

    /// <summary>A war hammer: long haft with langets, a toothed hammer face, a curved beak behind, a spike on top.</summary>
    private static void WarHammer(ReliefCanvas cv)
    {
        cv.Stroke([new(0, -0.88), new(0, 0.62)], 0.05, 0, flat: 0.85);
        foreach (int sg in new[] { 1, -1 })
        {
            cv.Stroke([new(0.052 * sg, 0.42), new(0.052 * sg, 0.08)], 0.018, 0.045, flat: 0.85);
            foreach (double y in new[] { 0.34, 0.14 }) cv.Dome(new Pt(0.052 * sg, y), 0.024, 0.055, 0.02);
        }
        foreach (double y in Lin(-0.52, -0.8, 5)) CutLine(cv, new(-0.045, y - 0.02), new(0.045, y + 0.02), 0.007, 0.02);
        cv.Dome(new Pt(0, -0.9), 0.06, 0.01, 0.05);

        cv.Plate(cv.Poly([new(-0.07, 0.6), new(0.07, 0.6), new(0, 0.95)]), 0.02, 0.07, 0.06, ReliefCanvas.Profile.Linear);
        var block = cv.Poly(Rect(-0.38, 0.4, -0.08, 0.62));
        cv.Plate(block, 0.04, 0.05, 0.06);
        cv.InsetGroove(block, 0.03, 0.01, 0.012);
        foreach (double y in new[] { 0.45, 0.57 })
            cv.Plate(cv.Poly([new(-0.38, y - 0.05), new(-0.44, y), new(-0.38, y + 0.05)]), 0.04, 0.04, 0.05, ReliefCanvas.Profile.Linear);
        var beak = cv.Poly(Bezier(new(0.08, 0.62), new(0.3, 0.62), new(0.46, 0.5), new(0.54, 0.24), 40)
            .Concat(Bezier(new(0.54, 0.24), new(0.4, 0.4), new(0.24, 0.44), new(0.08, 0.42), 40)).ToArray());
        cv.Plate(beak, 0.04, 0.08, 0.07, ReliefCanvas.Profile.Linear);
        cv.Plate(cv.Poly(Rect(-0.1, 0.36, 0.1, 0.66)), 0.06, 0.04, 0.06);
        cv.Dome(new Pt(0, 0.51), 0.06, 0.1, 0.045);
        cv.Accent.OrWith(cv.Circle(new Pt(0, 0.51), 0.05));
    }

    private static void Key(ReliefCanvas cv)
    {
        double rot0 = cv.Rot;
        cv.Rot = rot0 - Math.PI / 9;
        cv.Stamps(Circ(0.3, 900, new Pt(0, 0.5)), 0.09, 0, flat: 0.7, groove: 0.3);
        for (int i = 0; i < 4; i++)
            cv.Dome(new Pt(0, 0.5) + Pt.Polar(0.3, i * Tau / 4 + Math.PI / 4), 0.08, 0.02, 0.06);
        cv.Stroke([new(0, 0.2), new(0, -0.86)], 0.085, 0, flat: 0.8);
        foreach (double y in new[] { 0.12, 0.02 })
            cv.Stroke([new(-0.13, y), new(0.13, y)], 0.04, 0.03, flat: 0.9);
        var bit = cv.Poly([new(0.05, -0.44), new(0.46, -0.44), new(0.46, -0.56), new(0.3, -0.56), new(0.3, -0.66),
                           new(0.46, -0.66), new(0.46, -0.86), new(0.05, -0.86)]);
        cv.Plate(bit, 0, 0.06, 0.06);
        cv.InsetGroove(bit, 0.035, 0.011, 0.015);
        cv.Dome(new Pt(0, 0.5), 0.13, 0, 0.08);
        cv.Accent.OrWith(cv.Circle(new Pt(0, 0.5), 0.11));
        cv.Rot = rot0;
    }

    private static void Scales(ReliefCanvas cv)
    {
        cv.Stroke([new(0, -0.7), new(0, 0.5)], 0.045, 0, flat: 0.8);
        cv.Plate(cv.Poly([new(-0.36, -0.88), new(0.36, -0.88), new(0.14, -0.7), new(-0.14, -0.7)]), 0.02, 0.05, 0.05);
        cv.Dome(new Pt(0, -0.7), 0.09, 0.02, 0.06);
        cv.Stroke(Lin(0, 1, 80).Select(t => new Pt(-0.62 + 1.24 * t, 0.44)).ToArray(),
            t => 0.036 + 0.024 * (1 - Math.Abs(2 * t - 1)), 0.03, flat: 0.8);
        foreach (int sg in new[] { 1, -1 })
        {
            var end = new Pt(0.62 * sg, 0.44);
            cv.Dome(end, 0.05, 0.03, 0.05);
            var pc = new Pt(0.62 * sg, -0.16);
            foreach (int e in new[] { 1, -1 })
                cv.Stroke([end, new(pc.X + 0.22 * e, pc.Y + 0.02)], 0.022, 0, flat: 0.8);
            var pan = cv.Ellipse(pc, 0.25, 0.14, lowerHalf: true);
            cv.Plate(pan, 0.02, 0.07, 0.07);
            cv.InsetGroove(pan, 0.03, 0.01, 0.012);
            cv.Stroke([new(pc.X - 0.26, pc.Y), new(pc.X + 0.26, pc.Y)], 0.025, 0.05, flat: 0.8);
        }
        cv.Dome(new Pt(0, 0.44), 0.08, 0.05, 0.06);
        cv.Dome(new Pt(0, 0.64), 0.1, 0.02, 0.08);
        cv.Accent.OrWith(cv.Circle(new Pt(0, 0.64), 0.085));
    }

    private static void Chalice(ReliefCanvas cv)
    {
        var bowl = cv.Poly(Mirrored(Bezier(new(0.44, 0.62), new(0.46, 0.22), new(0.26, 0.04), new(0.06, 0.0), 60)));
        cv.Plate(bowl, 0.02, 0.2, 0.1, ReliefCanvas.Profile.Smooth);
        cv.InsetGroove(bowl, 0.04, 0.012, 0.015);
        cv.Stroke([new(-0.47, 0.62), new(0.47, 0.62)], 0.045, 0.07, flat: 0.8);
        foreach (var (x, y) in new[] { (-0.22, 0.34), (0.0, 0.3), (0.22, 0.34) })
        {
            cv.Dome(new Pt(x, y), 0.065, 0.09, 0.05);
            cv.Accent.OrWith(cv.Circle(new Pt(x, y), 0.058));
        }
        cv.Stroke([new(0, 0.02), new(0, -0.58)], 0.05, 0, flat: 0.8);
        cv.Dome(new Pt(0, -0.25), 0.11, 0.02, 0.08);
        foreach (double y in new[] { -0.02, -0.54 })
            cv.Stroke([new(-0.12, y), new(0.12, y)], 0.03, 0.04, flat: 0.85);
        var foot = cv.Poly(Mirrored(Bezier(new(0.07, -0.56), new(0.1, -0.66), new(0.3, -0.72), new(0.4, -0.84), 40)));
        cv.Plate(foot, 0, 0.08, 0.07);
        cv.InsetGroove(foot, 0.035, 0.011, 0.014);
    }

    /// <summary>A straight arming sword, point up, raised by <paramref name="z"/> so a crossed pair can overlap.</summary>
    private static void Sword(ReliefCanvas cv, double z)
    {
        var blade = cv.Poly([new(-0.11, -0.26), new(0.11, -0.26), new(0.095, 0.58), new(0, 0.95), new(-0.095, 0.58)]);
        cv.Plate(blade, z, 0.1, 0.075, ReliefCanvas.Profile.Linear);
        CutLine(cv, new(0, -0.2), new(0, 0.56), 0.014, 0.025);
        cv.Stroke(Bezier(new(-0.3, -0.19), new(-0.14, -0.28), new(0.14, -0.28), new(0.3, -0.19), 60), 0.05, z + 0.04, flat: 0.85);
        foreach (int sg in new[] { 1, -1 }) cv.Dome(new Pt(0.31 * sg, -0.18), 0.055, z + 0.04, 0.05);
        cv.Stroke([new(0, -0.3), new(0, -0.74)], 0.065, z, flat: 0.85);
        foreach (double y in Lin(-0.36, -0.68, 6)) CutLine(cv, new(-0.06, y - 0.03), new(0.06, y + 0.03), 0.007, 0.02);
        cv.Dome(new Pt(0, -0.27), 0.07, z + 0.06, 0.05);
        cv.Accent.OrWith(cv.Circle(new Pt(0, -0.27), 0.06));
        cv.Dome(new Pt(0, -0.83), 0.1, z + 0.01, 0.08);
    }

    private static void CrossedSwords(ReliefCanvas cv)
    {
        double rot0 = cv.Rot, s0 = cv.Scale;
        cv.Scale = s0 * 1.02;
        cv.Rot = rot0 + 0.62;
        Sword(cv, 0);
        cv.Rot = rot0 - 0.62;
        Sword(cv, 0.05);
        cv.Rot = rot0;
        cv.Scale = s0;
    }

    /// <summary>A curved sabre, point up and sweeping right: edge on the convex side, fuller along the spine.</summary>
    private static void Sabre(ReliefCanvas cv)
    {
        var spine = Bezier(new(0, -0.24), new(0, 0.3), new(0.06, 0.66), new(0.26, 0.9), 120);
        Pt Nrm(int i)
        {
            var d = (spine[Math.Min(i + 1, spine.Length - 1)] - spine[Math.Max(i - 1, 0)]).Unit;
            return new Pt(-d.Y, d.X);   // toward the convex, cutting side
        }
        var edge = new List<Pt>();
        var back = new List<Pt>();
        for (int i = 0; i < spine.Length; i++)
        {
            double s = i / (double)(spine.Length - 1);
            edge.Add(spine[i] + Nrm(i) * (0.12 * Math.Pow(1 - s, 0.6)));
            back.Add(spine[i] - Nrm(i) * (0.06 * (1 - s * s * s)));
        }
        cv.Plate(cv.Poly(edge.Concat(Enumerable.Reverse(back)).ToArray()), 0, 0.1, 0.07, ReliefCanvas.Profile.Linear);
        CutCurve(cv, spine.Take(84).Skip(6).Select((p, i) => p - Nrm(i + 6) * 0.025).ToArray(), 0.012, 0.022);

        cv.Stroke([new(-0.22, -0.26), new(0.22, -0.26)], 0.05, 0.04, flat: 0.85);
        foreach (int sg in new[] { 1, -1 }) cv.Dome(new Pt(0.21 * sg, -0.26), 0.05, 0.04, 0.045);
        cv.Dome(new Pt(0, -0.26), 0.07, 0.06, 0.05);
        cv.Accent.OrWith(cv.Circle(new Pt(0, -0.26), 0.06));
        var grip = Bezier(new(0, -0.3), new(0, -0.5), new(-0.03, -0.66), new(-0.07, -0.76), 40);
        cv.Stroke(grip, 0.058, 0, flat: 0.85);
        foreach (int i in new[] { 8, 16, 24, 32 }) CutLine(cv, grip[i] + new Pt(-0.055, -0.02), grip[i] + new Pt(0.055, 0.02), 0.007, 0.02);
        cv.Dome(new Pt(-0.09, -0.82), 0.085, 0.01, 0.07);
    }

    /// <summary>An ancient leaf-bladed sword with a raised midrib and a spiral antenna pommel.</summary>
    private static void LeafSword(ReliefCanvas cv)
    {
        const double y0 = -0.22, y1 = 0.94;
        double W(double s) => s < 0.6
            ? 0.075 + 0.08 * SmoothStep(0, 0.6, s)
            : 0.155 * Math.Pow(Math.Max(0, 1 - (s - 0.6) / 0.4), 0.7);
        var ss = Lin(0, 1, 120);
        var blade = ss.Select(s => new Pt(W(s), y0 + (y1 - y0) * s))
                      .Concat(ss.Reverse().Select(s => new Pt(-W(s), y0 + (y1 - y0) * s))).ToArray();
        cv.Plate(cv.Poly(blade), 0, 0.15, 0.08, ReliefCanvas.Profile.Linear);
        cv.Stroke([new(0, y0), new(0, 0.78)], t => 0.024 - 0.012 * t, 0.06, flat: 0.8);

        cv.Stroke(Bezier(new(-0.18, -0.15), new(-0.1, -0.27), new(0.1, -0.27), new(0.18, -0.15), 40), 0.045, 0.03, flat: 0.85);
        cv.Stroke([new(0, -0.26), new(0, -0.66)], 0.052, 0, flat: 0.85);
        foreach (double y in new[] { -0.38, -0.54 }) cv.Stroke([new(-0.06, y), new(0.06, y)], 0.022, 0.04, flat: 0.9);
        cv.Dome(new Pt(0, -0.21), 0.06, 0.06, 0.045);
        cv.Accent.OrWith(cv.Circle(new Pt(0, -0.21), 0.05));
        foreach (int sg in new[] { 1, -1 })
        {
            // Antennae: each arm leaves the grip and rolls outward into a spiral.
            var c = new Pt(0.17 * sg, -0.76);
            var arm = Bezier(new(0, -0.66), new(0.04 * sg, -0.76), new(0.1 * sg, -0.86), c + new Pt(0, -0.08), 30)
                .Concat(Lin(0, 1, 80).Select(u => c + Pt.Polar(0.08 * (1 - 0.7 * u), -Math.PI / 2 + sg * u * Tau * 0.9))).ToArray();
            cv.Stroke(arm, t => 0.04 - 0.018 * t, 0, flat: 0.85);
            cv.Dome(c, 0.03, 0.02, 0.03);
        }
    }

    private static void HornedDisc(ReliefCanvas cv)
    {
        foreach (int sg in new[] { 1, -1 })
            cv.Stroke(Bezier(new(0, -0.62), new(0.75 * sg, -0.5), new(0.85 * sg, 0.45), new(0.3 * sg, 0.86), 160),
                t => 0.1 * Math.Pow(1 - t, 0.8) + 0.015, 0, flat: 0.8, groove: 0.2);
        var disc = cv.Circle(new Pt(0, 0.08), 0.4);
        cv.Plate(disc, 0.04, 0.08, 0.07);
        cv.InsetGroove(disc, 0.05, 0.014, 0.02);
        cv.Accent.OrWith(cv.Circle(new Pt(0, 0.08), 0.3));
        cv.Plate(cv.Poly([new(-0.2, -0.78), new(0.2, -0.78), new(0.12, -0.6), new(-0.12, -0.6)]), 0.02, 0.03, 0.04);
    }

    // ================================================================ crosses

    private static void CrossPatee(ReliefCanvas cv)
    {
        double rot0 = cv.Rot;
        for (int k = 0; k < 4; k++)
        {
            cv.Rot = rot0 + k * Math.PI / 2;
            var arm = cv.Poly([new(-0.09, 0), new(0.09, 0), new(0.34, 0.9), new(0, 0.82), new(-0.34, 0.9)]);
            cv.Plate(arm, 0, 0.07, 0.06);
            cv.InsetGroove(arm, 0.045, 0.012, 0.018);
        }
        cv.Rot = rot0;
        cv.Dome(default, 0.16, 0.05, 0.07);
        cv.Accent.OrWith(cv.Circle(default, 0.13));
    }

    private static void RingedCross(ReliefCanvas cv)
    {
        var ring = cv.Ring(new Pt(0, 0.12), 0.36, 0.52);
        cv.Plate(ring, -0.01, 0.04, 0.04);
        cv.InsetGroove(ring, 0.03, 0.01, 0.014);
        foreach (var (x0, y0, x1, y1) in new[] { (-0.1, 0.12, 0.1, 0.84), (-0.1, -0.95, 0.1, 0.12), (-0.66, 0.02, 0.66, 0.22) })
        {
            var m = cv.Poly(Rect(x0, y0, x1, y1));
            cv.Plate(m, 0.03, 0.04, 0.04);
            cv.InsetGroove(m, 0.035, 0.011, 0.016);
        }
        foreach (var p in new Pt[] { new(0, 0.84), new(-0.66, 0.12), new(0.66, 0.12) }) cv.Dome(p, 0.09, 0.03, 0.06);
        cv.Dome(new Pt(0, 0.12), 0.12, 0.07, 0.06);
        cv.Accent.OrWith(cv.Circle(new Pt(0, 0.12), 0.1));
        cv.Plate(cv.Poly([new(-0.22, -0.95), new(0.22, -0.95), new(0.12, -0.8), new(-0.12, -0.8)]), 0.03, 0.03, 0.04);
    }

    private static void LoopedCross(ReliefCanvas cv)
    {
        cv.Stamps(Circ(0, 900, new Pt(0, 0.44), 0.2, 0.3), 0.075, 0, flat: 0.7, groove: 0.3);
        foreach (int sg in new[] { 1, -1 })
            cv.Stroke(Lin(0, 1, 80).Select(s => new Pt(sg * (0.02 + 0.52 * s), 0.08)).ToArray(),
                t => 0.07 + 0.08 * t * t * t, 0.01, flat: 0.7, groove: 0.3);
        cv.Stroke(Lin(0, 1, 120).Select(s => new Pt(0, 0.08 - 1.0 * s)).ToArray(),
            t => 0.075 + 0.1 * t * t * t, 0.01, flat: 0.7, groove: 0.3);
        cv.Dome(new Pt(0, 0.08), 0.1, 0.05, 0.06);
        cv.Accent.OrWith(cv.Circle(new Pt(0, 0.08), 0.06));
    }

    // ================================================================ frames

    /// <summary>
    /// The patterns cut into a frame's enamelled field. A flat field read as a solid colour disc at
    /// icon size; engraved, the enamel reads as enamel laid over guilloche, and the lines soften to
    /// a sheen when the icon is drawn small. A domed field was tried too: low enough to leave the
    /// motif alone it did not show, and high enough to show it swallowed the motif's lower edge.
    /// </summary>
    public enum Engraving { Sunburst, Diaper, Spirograph }

    private const double LineHalfWidth = 0.006, LineDepth = 0.01;

    /// <summary>Fine lines cut into a frame's field for the enamel to lie over, as guilloche is.</summary>
    private static void Engrave(ReliefCanvas cv, Mask field, double radius, Engraving pattern)
    {
        void Line(Pt a, Pt b)
        {
            var d = (b - a).Unit;
            var n = new Pt(-d.Y, d.X) * LineHalfWidth;
            cv.Engrave(cv.Poly([a + n, b + n, b - n, a - n]) & field, LineDepth, LineHalfWidth * 0.8);
        }

        void Circle(Pt c, double r) => cv.Engrave(cv.Ring(c, r - LineHalfWidth, r + LineHalfWidth) & field, LineDepth, LineHalfWidth * 0.8);

        switch (pattern)
        {
            case Engraving.Sunburst:
                for (int k = 0; k < 48; k++)
                {
                    double a = k * Tau / 48;
                    Line(Pt.Polar(0.1, a), Pt.Polar(radius, a));
                }
                Circle(default, radius * 0.55);
                Circle(default, radius * 0.8);
                break;

            case Engraving.Diaper:
                // A lozenge lattice: two sets of parallel lines at 45 degrees.
                for (double o = -1.4; o <= 1.4; o += 0.13)
                {
                    Line(new Pt(o - 1.2, -1.2), new Pt(o + 1.2, 1.2));
                    Line(new Pt(o - 1.2, 1.2), new Pt(o + 1.2, -1.2));
                }
                break;

            case Engraving.Spirograph:
                // Twelve circles through the centre, overlapping into a rosette, inside a border ring.
                for (int k = 0; k < 12; k++)
                    Circle(Pt.Polar(radius * 0.45, k * Tau / 12), radius * 0.45);
                Circle(default, radius * 0.92);
                break;
        }
    }

    /// <summary>
    /// Two leafy branches rising from a knot at the bottom and stopping short of the top, with
    /// enamel berries among the leaves.
    /// </summary>
    private static void Wreath(ReliefCanvas cv)
    {
        const double R = 0.84, a0 = -Math.PI / 2 + 0.16, a1 = Math.PI / 2 - 0.42;
        const int leaves = 11;
        foreach (int sg in new[] { 1, -1 })
        {
            Pt At(double u)
            {
                double a = a0 + u * (a1 - a0);
                return new Pt(sg * Math.Cos(a) * R, Math.Sin(a) * R);
            }
            cv.Stroke(Lin(0, 1, 200).Select(At).ToArray(), t => 0.024 - 0.01 * t, 0, flat: 0.8);
            for (int k = 0; k < leaves; k++)
            {
                double u = (k + 0.5) / leaves;
                Pt p = At(u), tan = (At(u + 0.01) - At(u - 0.01)).Unit;
                double ta = Math.Atan2(tan.Y, tan.X), L = 0.19 - 0.05 * u, W = 0.058 - 0.014 * u;
                Leaf(cv, p, ta + 0.55 * sg, L, W, 0.02);
                Leaf(cv, p, ta - 0.55 * sg, L, W, 0.02);
                if (k % 3 == 1)
                {
                    Pt berry = p + Pt.Polar(0.085, ta - 1.3 * sg);
                    cv.Dome(berry, 0.03, 0.04, 0.03);
                    cv.Accent.OrWith(cv.Circle(berry, 0.027));
                }
            }
            Pt e1 = At(1), e0 = At(0.98);
            Leaf(cv, e1, Math.Atan2(e1.Y - e0.Y, e1.X - e0.X), 0.16, 0.05, 0.02);
        }
        foreach (int sg in new[] { 1, -1 })
            cv.Stroke(Bezier(new(0, -0.84), new(0.08 * sg, -0.9), new(0.16 * sg, -0.96), new(0.2 * sg, -1.02), 30),
                t => 0.04 - 0.015 * t, 0.03, flat: 0.8);
        cv.Dome(new Pt(0, -0.84), 0.065, 0.04, 0.05);
    }

    /// <summary>Draws a frame and returns the scale the motif should be drawn at inside it.</summary>
    private static double DrawFrame(ReliefCanvas cv, string kind, Engraving engraving)
    {
        switch (kind)
        {
            case "ring":
                Band(cv, [Circ(0.9)], 0.055, weave: false);
                Beads(cv, 0.9, 16, 0.035, 0.03);
                return 0.76;

            case "rayed":
                for (int i = 0; i < 24; i++)
                {
                    double a = Math.PI / 2 + (i + 0.5) * Tau / 24;
                    double ro = i % 2 == 0 ? 0.98 : 0.86;
                    cv.Plate(cv.Poly([Pt.Polar(0.7, a - 0.07), Pt.Polar(ro, a), Pt.Polar(0.7, a + 0.07)]),
                        0, 0.2, 0.06, ReliefCanvas.Profile.Linear);
                }
                Band(cv, [Circ(0.72)], 0.05, weave: false);
                return 0.6;

            case "medallion":
            {
                var disc = cv.Circle(default, 0.9);
                cv.Plate(disc, -0.08, 0.02, 0.02);
                Band(cv, [Circ(0.9)], 0.06, weave: false, z0: -0.02);
                Beads(cv, 0.9, 20, 0.035);
                double lim = cv.U(-0.05);
                var field = new Mask(cv.N);
                for (int y = disc.Y0; y < disc.Y1; y++)
                    for (int x = disc.X0; x < disc.X1; x++)
                        if (disc.Bits[y * cv.N + x] && cv.H[y * cv.N + x] < lim) field.Set(x, y);
                Engrave(cv, field, 0.84, engraving);
                cv.Floor = (double[])cv.H.Clone();
                cv.Field = field;
                return 0.7;
            }

            case "lobed":
            {
                var m = cv.Circle(default, 0.6);
                for (int i = 0; i < 4; i++) m.OrWith(cv.Circle(Pt.Polar(0.36, Math.PI / 4 + i * Tau / 4), 0.55));
                cv.Plate(m, -0.08, 0.05, 0.04);
                cv.InsetGroove(m, 0.05, 0.014, 0.018);
                double lim = cv.U(-0.041);
                var field = new Mask(cv.N);
                for (int y = m.Y0; y < m.Y1; y++)
                    for (int x = m.X0; x < m.X1; x++)
                        if (m.Bits[y * cv.N + x] && cv.H[y * cv.N + x] >= lim) field.Set(x, y);
                Engrave(cv, field, 0.95, engraving);
                cv.Floor = (double[])cv.H.Clone();
                cv.Field = field;
                return 0.72;
            }

            case "wreath":
                Wreath(cv);
                return 0.66;

            default:
                return 1.0;
        }
    }
}
