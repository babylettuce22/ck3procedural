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
        };

    public static readonly string[] Frames = ["none", "ring", "medallion", "lobed", "rayed"];

    /// <summary>
    /// A finished design: the frame drawn, the motif drawn inside it, and the frame's uncovered
    /// field (if it has one) marked as inlay alongside whatever the motif marked itself.
    /// </summary>
    public static (ReliefCanvas Canvas, Mask? FieldInlay) Build(string motif, string frame, int n = 1000, double extent = 1.2)
    {
        var cv = new ReliefCanvas(n, extent);
        double scale = DrawFrame(cv, frame);
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

    private static void Ouroboros(ReliefCanvas cv)
    {
        double headA = Math.PI / 2, R = 0.66;
        var body = Lin(headA - 0.35, headA - 0.35 - Tau * 0.9, 900).Select(a => Pt.Polar(R, a)).ToArray();
        cv.Stroke(body, t => 0.02 + 0.15 * Math.Pow(1 - t, 0.6), 0, flat: 0.75, groove: 0.25);
        var hc = Pt.Polar(R, headA);
        var head = Lin(0, 1, 80).Select(s => new Pt(hc.X - 0.3 + 0.5 * s, hc.Y + 0.02 * Math.Sin(Math.PI * s))).ToArray();
        cv.Stroke(head, t => 0.17 * Math.Pow(Math.Sin(Math.PI * (0.3 + 0.62 * t)), 0.6) + 0.03, 0.05, flat: 0.7);
        var eye = new Pt(hc.X + 0.04, hc.Y + 0.07);
        cv.Dome(eye, 0.045, 0.14, 0.03);
        cv.Accent.OrWith(cv.Circle(eye, 0.042));
        cv.Engrave(cv.Poly([new(hc.X + 0.2, hc.Y - 0.012), new(hc.X + 0.02, hc.Y - 0.04), new(hc.X + 0.2, hc.Y - 0.03)]), 0.03, 0.01);
        foreach (double t in Lin(0.06, 0.82, 22))
        {
            double ang = headA - 0.35 - Tau * 0.9 * t;
            foreach (int side in new[] { -1, 1 })
            {
                double rr = R + side * (0.05 * (1 - t) + 0.01);
                cv.Engrave(cv.Circle(Pt.Polar(rr, ang), 0.02 * (1 - t) + 0.008), 0.015, 0.008);
            }
        }
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

    private static void Antlers(ReliefCanvas cv)
    {
        foreach (int sg in new[] { 1, -1 })
        {
            var beam = Bezier(new(0.12 * sg, -0.3), new(0.62 * sg, -0.24), new(0.74 * sg, 0.3), new(0.46 * sg, 0.94), 220);
            cv.Stroke(beam, t => 0.1 * (1 - t) + 0.03, 0, flat: 0.8, groove: 0.2);
            foreach (var (t, L, ang) in new[] { (0.14, 0.34, 150.0), (0.38, 0.44, 118.0), (0.62, 0.38, 104.0), (0.82, 0.26, 96.0) })
            {
                var p = beam[(int)(t * (beam.Length - 1))];
                double a = sg > 0 ? ang * Math.PI / 180 : Math.PI - ang * Math.PI / 180;
                var d = Pt.Polar(1, a);
                var tine = Bezier(p, p + d * (0.55 * L), p + d * L + new Pt(0, 0.12), 70);
                double r0 = 0.1 * (1 - t) + 0.03;
                cv.Stroke(tine, s => r0 * 0.8 * (1 - 0.8 * s) + 0.006, 0, flat: 0.8);
            }
        }
        var skull = cv.Circle(new Pt(0, -0.46), 0.25) | cv.Poly([new(-0.2, -0.5), new(0.2, -0.5), new(0.07, -0.95), new(-0.07, -0.95)]);
        cv.Plate(skull, 0.04, 0.1, 0.07, ReliefCanvas.Profile.Smooth);
        foreach (int sg in new[] { 1, -1 })
        {
            cv.Engrave(cv.Circle(new Pt(0.1 * sg, -0.5), 0.05), 0.04, 0.025);
            cv.Engrave(cv.Circle(new Pt(0.035 * sg, -0.86), 0.02), 0.03, 0.015);
        }
        cv.Accent.OrWith(cv.Circle(new Pt(0, -0.3), 0.06));
        cv.Dome(new Pt(0, -0.3), 0.06, 0.12, 0.04);
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

    /// <summary>Draws a frame and returns the scale the motif should be drawn at inside it.</summary>
    private static double DrawFrame(ReliefCanvas cv, string kind)
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
                cv.Floor = (double[])cv.H.Clone();
                double lim = cv.U(-0.05);
                var field = new Mask(cv.N);
                for (int y = disc.Y0; y < disc.Y1; y++)
                    for (int x = disc.X0; x < disc.X1; x++)
                        if (disc.Bits[y * cv.N + x] && cv.H[y * cv.N + x] < lim) field.Set(x, y);
                cv.Field = field;
                return 0.7;
            }

            case "lobed":
            {
                var m = cv.Circle(default, 0.6);
                for (int i = 0; i < 4; i++) m.OrWith(cv.Circle(Pt.Polar(0.36, Math.PI / 4 + i * Tau / 4), 0.55));
                cv.Plate(m, -0.08, 0.05, 0.04);
                cv.InsetGroove(m, 0.05, 0.014, 0.018);
                cv.Floor = (double[])cv.H.Clone();
                double lim = cv.U(-0.041);
                var field = new Mask(cv.N);
                for (int y = m.Y0; y < m.Y1; y++)
                    for (int x = m.X0; x < m.X1; x++)
                        if (m.Bits[y * cv.N + x] && cv.H[y * cv.N + x] >= lim) field.Set(x, y);
                cv.Field = field;
                return 0.72;
            }

            default:
                return 1.0;
        }
    }
}
