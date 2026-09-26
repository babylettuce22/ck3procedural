namespace Ck3MapGen.Emit.Relief;

/// <summary>A point in a design's unit space: the motif fits the square [-1, 1], y up.</summary>
public readonly record struct Pt(double X, double Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(Pt a, double k) => new(a.X * k, a.Y * k);
    public static Pt operator *(double k, Pt a) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Pt Unit => this * (1 / Length);
    public static Pt Polar(double r, double angle) => new(r * Math.Cos(angle), r * Math.Sin(angle));
}

/// <summary>
/// A boolean raster the size of the canvas, with the bounding box of its set pixels, so that the
/// many small shapes a design is made of (a bead, a spoke, a letter of engraving) cost their own
/// area rather than the whole canvas.
/// </summary>
public sealed class Mask
{
    public readonly int N;
    public readonly bool[] Bits;
    public int X0, Y0, X1, Y1;           // bounding box, exclusive upper bounds; empty when X0 >= X1

    public Mask(int n) { N = n; Bits = new bool[n * n]; X0 = Y0 = n; X1 = Y1 = 0; }

    public bool IsEmpty => X0 >= X1 || Y0 >= Y1;

    public bool this[int i] => Bits[i];

    public void Set(int x, int y)
    {
        Bits[y * N + x] = true;
        if (x < X0) X0 = x;
        if (y < Y0) Y0 = y;
        if (x >= X1) X1 = x + 1;
        if (y >= Y1) Y1 = y + 1;
    }

    public bool Any()
    {
        for (int y = Y0; y < Y1; y++)
            for (int x = X0; x < X1; x++)
                if (Bits[y * N + x]) return true;
        return false;
    }

    public Mask Clone()
    {
        var m = new Mask(N) { X0 = X0, Y0 = Y0, X1 = X1, Y1 = Y1 };
        Array.Copy(Bits, m.Bits, Bits.Length);
        return m;
    }

    public static Mask Full(int n)
    {
        var m = new Mask(n) { X0 = 0, Y0 = 0, X1 = n, Y1 = n };
        Array.Fill(m.Bits, true);
        return m;
    }

    public static Mask operator &(Mask a, Mask b)
    {
        var m = new Mask(a.N)
        {
            X0 = Math.Max(a.X0, b.X0), Y0 = Math.Max(a.Y0, b.Y0),
            X1 = Math.Min(a.X1, b.X1), Y1 = Math.Min(a.Y1, b.Y1),
        };
        for (int y = m.Y0; y < m.Y1; y++)
            for (int x = m.X0; x < m.X1; x++)
            {
                int i = y * a.N + x;
                m.Bits[i] = a.Bits[i] && b.Bits[i];
            }
        return m;
    }

    public static Mask operator |(Mask a, Mask b)
    {
        var m = a.Clone();
        m.OrWith(b);
        return m;
    }

    /// <summary>Complement. Its bounding box is the whole canvas.</summary>
    public static Mask operator !(Mask a)
    {
        var m = new Mask(a.N) { X0 = 0, Y0 = 0, X1 = a.N, Y1 = a.N };
        for (int i = 0; i < a.Bits.Length; i++) m.Bits[i] = !a.Bits[i];
        return m;
    }

    /// <summary>In-place union, the <c>|=</c> the designs use to collect an inlay region.</summary>
    public void OrWith(Mask b)
    {
        if (b.IsEmpty) return;
        for (int y = b.Y0; y < b.Y1; y++)
            for (int x = b.X0; x < b.X1; x++)
            {
                int i = y * N + x;
                if (b.Bits[i]) Bits[i] = true;
            }
        if (IsEmpty) { X0 = b.X0; Y0 = b.Y0; X1 = b.X1; Y1 = b.Y1; return; }
        X0 = Math.Min(X0, b.X0); Y0 = Math.Min(Y0, b.Y0);
        X1 = Math.Max(X1, b.X1); Y1 = Math.Max(Y1, b.Y1);
    }
}

/// <summary>
/// A relief design under construction: a heightfield plus the masks that say which parts take a
/// second material or colour. Shapes are drawn in unit space through a small transform stack
/// (rotation, mirror, scale, offset) so a motif can be repeated around a centre or shrunk into a
/// frame without knowing about either.
///
/// Heights, like lengths, are in unit space and converted to pixels by <see cref="U"/>, so the
/// same design shades identically at any canvas resolution.
/// </summary>
public sealed class ReliefCanvas
{
    public const double Bg = -1e9;

    public readonly int N;
    public readonly double E;
    public double[] H;
    public readonly Mask Accent;
    public readonly Mask Secondary;

    public double Rot;
    public double Mirror = 1;
    public double Scale = 1;
    public Pt Offset;

    /// <summary>What a cut falls back to: a frame's field under the motif, or nothing.</summary>
    public double[]? Floor;

    /// <summary>A frame's open field, which becomes inlay wherever the motif does not cover it.</summary>
    public Mask? Field;

    public ReliefCanvas(int n, double extent)
    {
        N = n;
        E = extent;
        H = new double[n * n];
        Array.Fill(H, Bg);
        Accent = new Mask(n);
        Secondary = new Mask(n);
    }

    // ---- coordinates -------------------------------------------------------------------------

    public double U(double v) => v * N / (2 * E) * Scale;

    public (double X, double Y) Px(Pt p)
    {
        double x = p.X * Scale * Mirror, y = p.Y * Scale;
        double c = Math.Cos(Rot), s = Math.Sin(Rot);
        double rx = x * c - y * s + Offset.X, ry = x * s + y * c + Offset.Y;
        return ((rx / E + 1) / 2 * N, (1 - ry / E) / 2 * N);
    }

    public bool Defined(int i) => H[i] > Bg / 2;

    // ---- masks -------------------------------------------------------------------------------

    /// <summary>Even-odd scanline fill, sampled at pixel centres.</summary>
    public Mask Poly(IReadOnlyList<Pt> pts)
    {
        var m = new Mask(N);
        int n = pts.Count;
        var xs = new double[n];
        var ys = new double[n];
        double minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            (xs[i], ys[i]) = Px(pts[i]);
            minY = Math.Min(minY, ys[i]);
            maxY = Math.Max(maxY, ys[i]);
        }

        int r0 = Math.Max(0, (int)Math.Floor(minY)), r1 = Math.Min(N - 1, (int)Math.Ceiling(maxY));
        var hits = new List<double>();
        for (int row = r0; row <= r1; row++)
        {
            double yc = row + 0.5;
            hits.Clear();
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double ya = ys[j], yb = ys[i];
                if ((ya <= yc && yc < yb) || (yb <= yc && yc < ya))
                    hits.Add(xs[j] + (yc - ya) * (xs[i] - xs[j]) / (yb - ya));
            }
            hits.Sort();
            for (int k = 0; k + 1 < hits.Count; k += 2)
            {
                int a = Math.Max(0, (int)Math.Ceiling(hits[k] - 0.5));
                int b = Math.Min(N - 1, (int)Math.Ceiling(hits[k + 1] - 0.5) - 1);
                for (int x = a; x <= b; x++) m.Set(x, row);
            }
        }
        return m;
    }

    public Mask Circle(Pt c, double r)
    {
        var m = new Mask(N);
        var (cx, cy) = Px(c);
        double rr = U(r);
        ForDisc(cx, cy, rr, (x, y, _) => m.Set(x, y));
        return m;
    }

    public Mask Ring(Pt c, double r0, double r1) => Circle(c, r1) & !Circle(c, r0);

    /// <summary>An axis-aligned ellipse, optionally only its lower half (on screen).</summary>
    public Mask Ellipse(Pt c, double rx, double ry, bool lowerHalf)
    {
        var m = new Mask(N);
        var (cx, cy) = Px(c);
        double ux = U(rx), uy = U(ry);
        int y0 = Math.Max(0, (int)(cy - uy - 1)), y1 = Math.Min(N, (int)(cy + uy + 2));
        int x0 = Math.Max(0, (int)(cx - ux - 1)), x1 = Math.Min(N, (int)(cx + ux + 2));
        for (int y = y0; y < y1; y++)
        {
            if (lowerHalf && y < cy) continue;
            for (int x = x0; x < x1; x++)
            {
                double dx = (x - cx) / ux, dy = (y - cy) / uy;
                if (dx * dx + dy * dy <= 1) m.Set(x, y);
            }
        }
        return m;
    }

    /// <summary>Every pixel row above the unit-space height <paramref name="y"/>.</summary>
    public Mask RowsAbove(double y)
    {
        var m = new Mask(N);
        int rows = Math.Clamp((int)Px(new Pt(0, y)).Y, 0, N);
        for (int r = 0; r < rows; r++)
            for (int x = 0; x < N; x++) m.Set(x, r);
        return m;
    }

    private void ForDisc(double cx, double cy, double rr, Action<int, int, double> each)
    {
        int y0 = Math.Max(0, (int)(cy - rr - 1)), y1 = Math.Min(N, (int)(cy + rr + 2));
        int x0 = Math.Max(0, (int)(cx - rr - 1)), x1 = Math.Min(N, (int)(cx + rr + 2));
        double r2 = rr * rr;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                double d2 = dx * dx + dy * dy;
                if (d2 <= r2) each(x, y, d2);
            }
    }

    // ---- height primitives -------------------------------------------------------------------

    public enum Profile { Round, Linear, Smooth }

    /// <summary>A raised plate whose edge rises over <paramref name="w"/> to height <paramref name="h"/>.</summary>
    public void Plate(Mask mask, double z0 = 0, double w = 0.04, double h = 0.04, Profile profile = Profile.Round)
    {
        if (mask.IsEmpty) return;
        var d = Raster.Edt(mask);
        double uw = U(w), uz = U(z0), uh = U(h);
        for (int y = mask.Y0; y < mask.Y1; y++)
            for (int x = mask.X0; x < mask.X1; x++)
            {
                int i = y * N + x;
                if (!mask.Bits[i]) continue;
                double t = Math.Clamp(d[i] / uw, 0, 1);
                double p = profile switch
                {
                    Profile.Linear => t,
                    Profile.Smooth => t * t * (3 - 2 * t),
                    _ => Math.Sqrt(1 - (1 - t) * (1 - t)),
                };
                double v = uz + uh * p;
                if (v > H[i]) H[i] = v;
            }
    }

    public void Dome(Pt c, double r, double z0 = 0, double? h = null)
    {
        var (cx, cy) = Px(c);
        double rr = U(r), uz = U(z0), uh = U(h ?? r);
        if (rr <= 0) return;
        ForDisc(cx, cy, rr, (x, y, d2) =>
        {
            int i = y * N + x;
            double v = uz + uh * Math.Sqrt(Math.Max(0, 1 - d2 / (rr * rr)));
            if (v > H[i]) H[i] = v;
        });
    }

    /// <summary>Lowers the surface inside <paramref name="mask"/> with a rounded V profile.</summary>
    public void Engrave(Mask mask, double depth = 0.02, double w = 0.01)
    {
        var m = new Mask(N);
        for (int y = mask.Y0; y < mask.Y1; y++)
            for (int x = mask.X0; x < mask.X1; x++)
                if (mask.Bits[y * N + x] && Defined(y * N + x)) m.Set(x, y);
        if (m.IsEmpty) return;
        var d = Raster.Edt(m);
        double uw = Math.Max(U(w), 1e-6), ud = U(depth);
        for (int y = m.Y0; y < m.Y1; y++)
            for (int x = m.X0; x < m.X1; x++)
            {
                int i = y * N + x;
                if (!m.Bits[i]) continue;
                double t = Math.Clamp(d[i] / uw, 0, 1);
                H[i] -= ud * Math.Sqrt(1 - (1 - t) * (1 - t));
            }
    }

    /// <summary>An engraved line following the outline of <paramref name="mask"/>, inset by <paramref name="dist"/>.</summary>
    public void InsetGroove(Mask mask, double dist = 0.04, double width = 0.014, double depth = 0.018)
    {
        if (mask.IsEmpty) return;
        var d = Raster.Edt(mask);
        double a = U(dist - width / 2), b = U(dist + width / 2);
        var band = new Mask(N);
        for (int y = mask.Y0; y < mask.Y1; y++)
            for (int x = mask.X0; x < mask.X1; x++)
            {
                int i = y * N + x;
                if (mask.Bits[i] && d[i] >= a && d[i] <= b) band.Set(x, y);
            }
        Engrave(band, depth, width / 2);
    }

    public void Cut(Mask mask)
    {
        for (int y = mask.Y0; y < mask.Y1; y++)
            for (int x = mask.X0; x < mask.X1; x++)
            {
                int i = y * N + x;
                if (mask.Bits[i]) H[i] = Floor?[i] ?? Bg;
            }
    }

    /// <summary>A tube: the maximum over disc stamps with a rounded cross-section.</summary>
    public void Stamps(IReadOnlyList<Pt> pts, IReadOnlyList<double> r, IReadOnlyList<double> z,
        double flat = 0.65, double groove = 0, double gw = 0.28)
    {
        for (int k = 0; k < pts.Count; k++)
        {
            var (cx, cy) = Px(pts[k]);
            double rr = U(r[k]), zz = U(z[k]);
            if (rr < 0.5) continue;
            int x0 = Math.Max((int)(cx - rr - 1), 0), x1 = Math.Min((int)(cx + rr + 2), N);
            int y0 = Math.Max((int)(cy - rr - 1), 0), y1 = Math.Min((int)(cy + rr + 2), N);
            double inv = 1 / (rr * rr), g2 = gw * gw;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                    double d2 = (dx * dx + dy * dy) * inv;
                    if (d2 >= 1) continue;
                    double hv = zz + rr * flat * Math.Sqrt(1 - d2);
                    if (groove != 0) hv -= groove * rr * Math.Exp(-d2 / g2);
                    int i = y * N + x;
                    if (hv > H[i]) H[i] = hv;
                }
        }
    }

    public void Stamps(IReadOnlyList<Pt> pts, double r, double z, double flat = 0.65, double groove = 0, double gw = 0.28)
        => Stamps(pts, Fill(pts.Count, r), Fill(pts.Count, z), flat, groove, gw);

    public void Stamps(IReadOnlyList<Pt> pts, double r, IReadOnlyList<double> z, double flat = 0.65, double groove = 0, double gw = 0.28)
        => Stamps(pts, Fill(pts.Count, r), z, flat, groove, gw);

    private static double[] Fill(int n, double v)
    {
        var a = new double[n];
        Array.Fill(a, v);
        return a;
    }

    /// <summary>
    /// A tube along a polyline, resampled finely enough that the stamps merge. Radius and height
    /// are functions of the arc parameter s in [0, 1].
    /// </summary>
    public void Stroke(IReadOnlyList<Pt> pts, Func<double, double> r, Func<double, double> z,
        double flat = 0.65, double groove = 0, double gw = 0.28)
    {
        var cum = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + (pts[i] - pts[i - 1]).Length;
        double total = cum[^1];

        double rmin = double.MaxValue;
        for (int i = 0; i < 20; i++) rmin = Math.Min(rmin, r(i / 19.0));
        double step = Math.Max(Math.Min(rmin, 0.05) / 5, 0.6 * E * 2 / N);
        int n = Math.Max((int)(total / step), 2);

        var q = new Pt[n];
        var rr = new double[n];
        var zz = new double[n];
        int seg = 0;
        for (int k = 0; k < n; k++)
        {
            double s = k / (double)(n - 1);
            double target = s * total;
            while (seg < pts.Count - 2 && cum[seg + 1] < target) seg++;
            double span = cum[seg + 1] - cum[seg];
            double f = span > 0 ? Math.Clamp((target - cum[seg]) / span, 0, 1) : 0;
            q[k] = pts[seg] + (pts[seg + 1] - pts[seg]) * f;
            rr[k] = r(s);
            zz[k] = z(s);
        }
        Stamps(q, rr, zz, flat, groove, gw);
    }

    public void Stroke(IReadOnlyList<Pt> pts, double r, double z = 0, double flat = 0.65, double groove = 0, double gw = 0.28)
        => Stroke(pts, _ => r, _ => z, flat, groove, gw);

    public void Stroke(IReadOnlyList<Pt> pts, Func<double, double> r, double z = 0, double flat = 0.65, double groove = 0, double gw = 0.28)
        => Stroke(pts, r, _ => z, flat, groove, gw);

    /// <summary>
    /// Cuts the under-strand wherever a much higher strand passes close by, so interlaced bands
    /// read as passing over and under rather than merging.
    /// </summary>
    public void InterlaceGaps(double gap = 0.03, double thresh = 0.08)
    {
        var hf = new double[N * N];
        for (int i = 0; i < hf.Length; i++) hf[i] = Defined(i) ? H[i] : -10.0 * N;
        var hmax = Raster.MaxFilter(hf, N, Math.Max((int)U(gap), 3));
        double t = U(thresh);
        for (int i = 0; i < hf.Length; i++)
        {
            if (!Defined(i) || hmax[i] - hf[i] <= t) continue;
            if (Floor is not null && !(Floor[i] < hf[i])) continue;
            H[i] = Floor?[i] ?? Bg;
        }
    }

    /// <summary>Anything drawn inside the scope only survives where <paramref name="keep"/> is set.</summary>
    public IDisposable Clip(Mask keep) => new ClipScope(this, keep);

    private sealed class ClipScope : IDisposable
    {
        private readonly ReliefCanvas _cv;
        private readonly Mask _keep;
        private readonly double[] _snap;

        public ClipScope(ReliefCanvas cv, Mask keep)
        {
            _cv = cv;
            _keep = keep;
            _snap = (double[])cv.H.Clone();
        }

        public void Dispose()
        {
            for (int i = 0; i < _snap.Length; i++)
                if (!_keep.Bits[i]) _cv.H[i] = _snap[i];
        }
    }
}
