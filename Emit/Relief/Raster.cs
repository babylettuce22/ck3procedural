namespace Ck3MapGen.Emit.Relief;

/// <summary>
/// The image operations the relief renderer needs: an exact Euclidean distance transform, blurs,
/// a running-max filter and a Lanczos downsample. All on square N×N rasters, row-major.
/// </summary>
public static class Raster
{
    private const double Inf = 1e20;

    /// <summary>
    /// For every set pixel of <paramref name="mask"/>, the distance to the nearest unset pixel.
    /// Zero elsewhere. As scipy's <c>distance_transform_edt</c>: the canvas edge is not
    /// background, only unset pixels are.
    ///
    /// Only the mask's bounding box, plus the one-pixel ring around it, is transformed. That is
    /// exact whenever the ring is background, because the ring is nearer than anything beyond it;
    /// a mask whose ring is not all background (it reaches the canvas edge, or it came out of a
    /// complement) is transformed over the whole canvas instead.
    /// </summary>
    public static double[] Edt(Mask mask)
    {
        int n = mask.N;
        var outD = new double[n * n];
        if (mask.IsEmpty) return outD;

        int x0 = Math.Max(0, mask.X0 - 1), y0 = Math.Max(0, mask.Y0 - 1);
        int x1 = Math.Min(n, mask.X1 + 1), y1 = Math.Min(n, mask.Y1 + 1);
        bool ringClear = mask.X0 > 0 && mask.Y0 > 0 && mask.X1 < n && mask.Y1 < n;
        if (!ringClear) { x0 = 0; y0 = 0; x1 = n; y1 = n; }
        int w = x1 - x0, h = y1 - y0;

        var f = new double[w * h];
        bool anyBackground = false;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool inside = mask.Bits[(y + y0) * n + x + x0];
                f[y * w + x] = inside ? Inf : 0;
                anyBackground |= !inside;
            }
        if (!anyBackground)
        {
            Array.Fill(outD, double.PositiveInfinity);
            return outD;
        }

        var d2 = Transform2D(f, w, h, null);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int gi = (y + y0) * n + x + x0;
                if (mask.Bits[gi]) outD[gi] = Math.Sqrt(d2[y * w + x]);
            }
        return outD;
    }

    /// <summary>For every pixel, the index of the nearest pixel where <paramref name="source"/> is true.</summary>
    public static int[] NearestIndex(bool[] source, int n)
    {
        var f = new double[n * n];
        for (int i = 0; i < f.Length; i++) f[i] = source[i] ? 0 : Inf;
        var nearest = new int[n * n];
        Transform2D(f, n, n, nearest);
        return nearest;
    }

    /// <summary>Squared EDT of a 0/Inf field (Felzenszwalb &amp; Huttenlocher), optionally with argmins.</summary>
    private static double[] Transform2D(double[] f, int w, int h, int[]? nearest)
    {
        int m = Math.Max(w, h);
        var col = new double[m];
        var d = new double[m];
        var which = new int[m];
        var v = new int[m];
        var z = new double[m + 1];

        // Columns first: squared distance within the column, and which row it came from.
        var pass1 = new double[w * h];
        int[]? rowOf = nearest is null ? null : new int[w * h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++) col[y] = f[y * w + x];
            Dt1(col, h, d, which, v, z);
            for (int y = 0; y < h; y++)
            {
                pass1[y * w + x] = d[y];
                if (rowOf is not null) rowOf[y * w + x] = which[y];
            }
        }

        var result = new double[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) col[x] = pass1[y * w + x];
            Dt1(col, w, d, which, v, z);
            for (int x = 0; x < w; x++)
            {
                result[y * w + x] = d[x];
                if (nearest is not null) nearest[y * w + x] = rowOf![y * w + which[x]] * w + which[x];
            }
        }
        return result;
    }

    private static void Dt1(double[] f, int n, double[] d, int[] which, int[] v, double[] z)
    {
        int k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s;
            while (true)
            {
                int p = v[k];
                s = (f[q] + (double)q * q - (f[p] + (double)p * p)) / (2.0 * q - 2.0 * p);
                if (s <= z[k] && k > 0) k--;
                else break;
            }
            if (s <= z[k])
            {
                // k == 0 and the new parabola dominates everywhere
                v[0] = q;
                z[0] = double.NegativeInfinity;
                z[1] = double.PositiveInfinity;
                continue;
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            int p = v[k];
            d[q] = (double)(q - p) * (q - p) + f[p];
            which[q] = p;
        }
    }

    /// <summary>
    /// Gaussian blur with separate sigmas per axis. Exact kernels below sigma 3; above that, three
    /// box passes, which are indistinguishable at the sizes shading uses and cost the same at any
    /// radius. Edges clamp.
    /// </summary>
    public static double[] Gaussian(double[] src, int n, double sigmaY, double sigmaX)
    {
        var tmp = new double[n * n];
        var outp = new double[n * n];
        BlurAxis(src, tmp, n, sigmaX, horizontal: true);
        BlurAxis(tmp, outp, n, sigmaY, horizontal: false);
        return outp;
    }

    public static double[] Gaussian(double[] src, int n, double sigma) => Gaussian(src, n, sigma, sigma);

    private static void BlurAxis(double[] src, double[] dst, int n, double sigma, bool horizontal)
    {
        if (sigma <= 0.01) { Array.Copy(src, dst, src.Length); return; }

        if (sigma < 3)
        {
            int rad = (int)Math.Ceiling(4 * sigma);
            var k = new double[2 * rad + 1];
            double sum = 0;
            for (int i = -rad; i <= rad; i++) sum += k[i + rad] = Math.Exp(-i * i / (2 * sigma * sigma));
            for (int i = 0; i < k.Length; i++) k[i] /= sum;

            if (horizontal)
            {
                Lines(n, line =>
                {
                    for (int p = 0; p < n; p++)
                    {
                        double acc = 0;
                        for (int t = -rad; t <= rad; t++)
                            acc += k[t + rad] * src[line * n + Math.Clamp(p + t, 0, n - 1)];
                        dst[line * n + p] = acc;
                    }
                });
            }
            else
            {
                // Whole rows at a time, so the inner loop walks memory in order.
                Array.Clear(dst);
                for (int y = 0; y < n; y++)
                {
                    var outRow = dst.AsSpan(y * n, n);
                    for (int t = -rad; t <= rad; t++)
                    {
                        double kw = k[t + rad];
                        var inRow = src.AsSpan(Math.Clamp(y + t, 0, n - 1) * n, n);
                        for (int x = 0; x < n; x++) outRow[x] += kw * inRow[x];
                    }
                }
            }
            return;
        }

        // Three box passes of a width chosen so their combined variance matches sigma^2.
        int r = Math.Max(1, (int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2));
        if (horizontal)
        {
            Lines(n, line =>
            {
                var a = new double[n];
                var b = new double[n];
                Array.Copy(src, line * n, a, 0, n);
                Box(a, b, n, r);
                Box(b, a, n, r);
                Box(a, b, n, r);
                Array.Copy(b, 0, dst, line * n, n);
            });
        }
        else
        {
            var a = (double[])src.Clone();
            var b = new double[n * n];
            BoxRows(a, b, n, r);
            BoxRows(b, a, n, r);
            BoxRows(a, dst, n, r);
        }
    }

    /// <summary>A vertical box pass done a row at a time: a running sum per column.</summary>
    private static void BoxRows(double[] src, double[] dst, int n, int r)
    {
        double inv = 1.0 / (2 * r + 1);
        var acc = new double[n];
        for (int t = -r; t <= r; t++)
        {
            var row = src.AsSpan(Math.Clamp(t, 0, n - 1) * n, n);
            for (int x = 0; x < n; x++) acc[x] += row[x];
        }
        for (int y = 0; y < n; y++)
        {
            var outRow = dst.AsSpan(y * n, n);
            var add = src.AsSpan(Math.Min(y + r + 1, n - 1) * n, n);
            var sub = src.AsSpan(Math.Max(y - r, 0) * n, n);
            for (int x = 0; x < n; x++)
            {
                outRow[x] = acc[x] * inv;
                acc[x] += add[x] - sub[x];
            }
        }
    }

    /// <summary>
    /// Runs a per-line body in order. Sequential on purpose: the parallelism is one level up, one
    /// design per thread, and nesting a second fan-out under it only made the threads queue.
    /// </summary>
    private static void Lines(int n, Action<int> body)
    {
        for (int i = 0; i < n; i++) body(i);
    }

    private static void Box(double[] src, double[] dst, int n, int r)
    {
        double inv = 1.0 / (2 * r + 1);
        double acc = 0;
        for (int t = -r; t <= r; t++) acc += src[Math.Clamp(t, 0, n - 1)];
        for (int p = 0; p < n; p++)
        {
            dst[p] = acc * inv;
            acc += src[Math.Min(p + r + 1, n - 1)] - src[Math.Max(p - r, 0)];
        }
    }

    /// <summary>Square running maximum of side <paramref name="size"/>, centred as scipy centres it.</summary>
    public static double[] MaxFilter(double[] src, int n, int size)
    {
        int lo = size / 2, hi = size - 1 - lo;
        var tmp = new double[n * n];
        var outp = new double[n * n];
        Lines(n, y =>
        {
            for (int x = 0; x < n; x++)
            {
                double m = double.MinValue;
                for (int t = Math.Max(0, x - lo); t <= Math.Min(n - 1, x + hi); t++) m = Math.Max(m, src[y * n + t]);
                tmp[y * n + x] = m;
            }
        });
        Lines(n, x =>
        {
            for (int y = 0; y < n; y++)
            {
                double m = double.MinValue;
                for (int t = Math.Max(0, y - lo); t <= Math.Min(n - 1, y + hi); t++) m = Math.Max(m, tmp[t * n + x]);
                outp[y * n + x] = m;
            }
        });
        return outp;
    }

    /// <summary>Moves the image content by (dy, dx) pixels, bilinear, filling with zero.</summary>
    public static double[] Shift(double[] src, int n, double dy, double dx)
    {
        var outp = new double[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double sy = y - dy, sx = x - dx;
                int iy = (int)Math.Floor(sy), ix = (int)Math.Floor(sx);
                double fy = sy - iy, fx = sx - ix;
                double v = 0;
                for (int a = 0; a < 2; a++)
                    for (int b = 0; b < 2; b++)
                    {
                        int yy = iy + a, xx = ix + b;
                        if (yy < 0 || xx < 0 || yy >= n || xx >= n) continue;
                        v += src[yy * n + xx] * (a == 1 ? fy : 1 - fy) * (b == 1 ? fx : 1 - fx);
                    }
                outp[y * n + x] = v;
            }
        return outp;
    }

    /// <summary>Separable Lanczos-3 resample of several planes from n×n to size×size.</summary>
    public static double[][] Lanczos(double[][] planes, int n, int size)
    {
        var (idx, wts) = LanczosWeights(n, size);
        var result = new double[planes.Length][];
        for (int c = 0; c < planes.Length; c++)
        {
            var src = planes[c];
            var tmp = new double[n * size];         // n rows, size columns
            for (int y = 0; y < n; y++)
                for (int o = 0; o < size; o++)
                {
                    double acc = 0;
                    for (int t = 0; t < idx[o].Length; t++) acc += wts[o][t] * src[y * n + idx[o][t]];
                    tmp[y * size + o] = acc;
                }
            var dst = new double[size * size];
            for (int o = 0; o < size; o++)
                for (int x = 0; x < size; x++)
                {
                    double acc = 0;
                    for (int t = 0; t < idx[o].Length; t++) acc += wts[o][t] * tmp[idx[o][t] * size + x];
                    dst[o * size + x] = acc;
                }
            result[c] = dst;
        }
        return result;
    }

    private static (int[][], double[][]) LanczosWeights(int n, int size)
    {
        double scale = (double)n / size;
        var idx = new int[size][];
        var wts = new double[size][];
        for (int o = 0; o < size; o++)
        {
            double centre = (o + 0.5) * scale - 0.5;
            int a = (int)Math.Floor(centre - 3 * scale), b = (int)Math.Ceiling(centre + 3 * scale);
            var ii = new List<int>();
            var ww = new List<double>();
            double sum = 0;
            for (int x = a; x <= b; x++)
            {
                double t = (x - centre) / scale;
                double wv = Sinc(t) * Sinc(t / 3);
                if (Math.Abs(t) >= 3) wv = 0;
                if (wv == 0) continue;
                ii.Add(Math.Clamp(x, 0, n - 1));
                ww.Add(wv);
                sum += wv;
            }
            idx[o] = ii.ToArray();
            wts[o] = ww.Select(v => v / sum).ToArray();
        }
        return (idx, wts);
    }

    private static double Sinc(double x) => Math.Abs(x) < 1e-9 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);

    /// <summary>Unit-variance Gaussian noise, blurred by <paramref name="sigma"/> and renormalised.</summary>
    public static double[] Noise(int n, Core.Rng rng, double sigmaY, double sigmaX)
    {
        var a = new double[n * n];
        for (int i = 0; i < a.Length; i += 2)
        {
            double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
            double r = Math.Sqrt(-2 * Math.Log(u1));
            a[i] = r * Math.Cos(2 * Math.PI * u2);
            if (i + 1 < a.Length) a[i + 1] = r * Math.Sin(2 * Math.PI * u2);
        }
        var b = Gaussian(a, n, sigmaY, sigmaX);
        return Normalise(b);
    }

    public static double[] Normalise(double[] a)
    {
        double mean = a.Average();
        double var = 0;
        foreach (double v in a) var += (v - mean) * (v - mean);
        double sd = Math.Sqrt(var / a.Length);
        if (sd <= 0) return a;
        for (int i = 0; i < a.Length; i++) a[i] = (a[i] - mean) / sd;
        return a;
    }
}
