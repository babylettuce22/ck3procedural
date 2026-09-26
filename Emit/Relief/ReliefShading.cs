namespace Ck3MapGen.Emit.Relief;

using Ck3MapGen.Core;

/// <summary>How a surface is textured before the colour ramp is applied.</summary>
public enum SurfaceTexture { Metal, Stone, Pitted, None }

/// <summary>Special treatments layered on the plain ramp-and-specular shading.</summary>
public enum MaterialKind { Plain, Wood, Patina }

/// <summary>
/// A material: a luminance-to-colour ramp, a specular tint, strength and sharpness, and a surface
/// texture. Metals differ from stones mostly in <see cref="K"/> and <see cref="Exp"/> — how much
/// of the light is mirror-like and how tight the highlight is.
/// </summary>
public sealed record Material(
    (double At, (int R, int G, int B) Col)[] Ramp,
    (int R, int G, int B) Spec,
    double K,
    double Exp,
    SurfaceTexture Texture,
    double Noise,
    MaterialKind Kind = MaterialKind.Plain);

/// <summary>What shading needs from a finished design; independent of the material.</summary>
public sealed class ReliefSurface
{
    public required int N { get; init; }
    public required bool[] Defined { get; init; }
    public required double[] Nx { get; init; }
    public required double[] Ny { get; init; }
    public required double[] Nz { get; init; }
    public required double[] Ao { get; init; }
    public required double[] Cavity { get; init; }
    public required double[] Convex { get; init; }
    public required bool[] Accent { get; init; }

    /// <summary>Coverage of the design plus its dark rim: 1 inside, 0 outside.</summary>
    public required double[] Alpha { get; init; }

    /// <summary>The drop shadow's alpha, before it is layered under <see cref="Alpha"/>.</summary>
    public required double[] Shadow { get; init; }
}

/// <summary>
/// Shades a relief heightfield as metal, stone, wood or enamel, lit from the upper left, and
/// composites it into a CK3 faith icon: a dark rim, a soft drop shadow, and a Lanczos downsample
/// from a supersampled canvas.
/// </summary>
public static class ReliefShading
{
    private static readonly (double X, double Y, double Z) Light = Norm(-0.5, -0.62, 0.6);
    private static readonly (double X, double Y, double Z) Half = Norm(Light.X, Light.Y, Light.Z + 1);

    private static (double, double, double) Norm(double x, double y, double z)
    {
        double l = Math.Sqrt(x * x + y * y + z * z);
        return (x / l, y / l, z / l);
    }

    private static (double, (int, int, int))[] R(params (double, (int, int, int))[] stops) => stops;

    public static readonly (double, (int, int, int))[] GoldRamp =
        R((0, (30, 16, 5)), (.30, (105, 64, 18)), (.52, (178, 128, 42)), (.72, (228, 184, 82)), (.88, (250, 224, 140)), (1, (255, 246, 205)));

    private static readonly (double, (int, int, int))[] CopperRamp =
        R((0, (30, 10, 4)), (.3, (100, 42, 18)), (.52, (170, 84, 46)), (.72, (214, 128, 84)), (.88, (238, 180, 140)), (1, (252, 225, 205)));

    private static readonly (double, (int, int, int))[] PatinaRamp =
        R((0, (12, 36, 32)), (.3, (38, 92, 80)), (.52, (74, 142, 120)), (.72, (112, 178, 152)), (.88, (156, 206, 184)), (1, (206, 232, 218)));

    public static readonly IReadOnlyDictionary<string, Material> Materials = new Dictionary<string, Material>
    {
        ["gold"] = new(GoldRamp, (255, 235, 190), 0.75, 70, SurfaceTexture.Metal, 0.035),
        ["electrum"] = new(R((0, (26, 24, 12)), (.3, (92, 86, 50)), (.52, (164, 156, 102)), (.72, (212, 206, 156)), (.88, (236, 232, 196)), (1, (255, 253, 236))),
            (255, 250, 225), 0.8, 75, SurfaceTexture.Metal, 0.03),
        ["silver"] = new(R((0, (16, 18, 22)), (.3, (70, 74, 82)), (.52, (140, 146, 156)), (.72, (198, 203, 212)), (.88, (232, 235, 240)), (1, (255, 255, 255))),
            (255, 255, 255), 0.9, 80, SurfaceTexture.Metal, 0.03),
        ["bronze"] = new(R((0, (24, 11, 4)), (.3, (86, 44, 18)), (.52, (146, 86, 42)), (.72, (192, 132, 76)), (.88, (224, 178, 126)), (1, (246, 222, 190))),
            (255, 220, 180), 0.6, 45, SurfaceTexture.Metal, 0.045),
        ["verdigris"] = new(PatinaRamp, (255, 220, 190), 0.5, 40, SurfaceTexture.Stone, 0.05, MaterialKind.Patina),
        ["iron"] = new(R((0, (10, 10, 12)), (.3, (40, 41, 45)), (.52, (78, 80, 86)), (.72, (118, 121, 128)), (.88, (160, 163, 170)), (1, (212, 214, 220))),
            (230, 235, 245), 0.45, 25, SurfaceTexture.Pitted, 0.08),
        ["wood"] = new(R((0, (26, 12, 5)), (.3, (74, 40, 17)), (.52, (122, 74, 36)), (.72, (160, 106, 58)), (.88, (192, 146, 92)), (1, (224, 186, 132))),
            (255, 230, 200), 0.12, 14, SurfaceTexture.None, 0.0, MaterialKind.Wood),
        ["stone"] = new(R((0, (34, 32, 30)), (.3, (80, 76, 70)), (.52, (122, 116, 106)), (.72, (158, 152, 141)), (.88, (188, 183, 172)), (1, (214, 209, 198))),
            (255, 255, 255), 0.05, 10, SurfaceTexture.Stone, 0.09),
        ["bone"] = new(R((0, (40, 32, 22)), (.3, (104, 90, 68)), (.52, (170, 154, 124)), (.72, (212, 198, 166)), (.88, (232, 222, 196)), (1, (248, 243, 228))),
            (255, 250, 235), 0.3, 25, SurfaceTexture.Stone, 0.04),
        ["jade"] = new(R((0, (6, 26, 16)), (.3, (22, 76, 48)), (.52, (48, 128, 84)), (.72, (98, 172, 124)), (.88, (164, 214, 176)), (1, (230, 248, 232))),
            (255, 255, 255), 0.7, 60, SurfaceTexture.Stone, 0.03),
        ["obsidian"] = new(R((0, (4, 4, 6)), (.3, (14, 14, 18)), (.52, (30, 30, 38)), (.72, (58, 58, 70)), (.88, (110, 110, 128)), (1, (220, 220, 240))),
            (255, 255, 255), 1.0, 120, SurfaceTexture.Metal, 0.01),
    };

    /// <summary>
    /// A vitreous enamel in any colour: deep shadow, the colour itself in the midtones, a pale
    /// sheen on top. Saturation is floored so a muted faith colour still reads as enamel.
    /// </summary>
    public static Material Enamel((double R, double G, double B) rgb, bool glossy = true)
    {
        var (h, s, _) = ToHsv(rgb.R, rgb.G, rgb.B);
        s = Math.Max(s, 0.45);
        (int, int, int) Hsv(double s2, double v2)
        {
            var (r, g, b) = FromHsv(h, Math.Clamp(s2, 0, 1), Math.Clamp(v2, 0, 1));
            return ((int)(255 * r), (int)(255 * g), (int)(255 * b));
        }
        var stops = R((0, Hsv(s, 0.12)), (.3, Hsv(s, 0.36)), (.52, Hsv(s * 0.95, 0.62)),
            (.72, Hsv(s * 0.75, 0.82)), (.88, Hsv(s * 0.4, 0.93)), (1, Hsv(0.08, 1.0)));
        return new Material(stops, (255, 255, 255), glossy ? 0.9 : 0.4, glossy ? 90 : 30, SurfaceTexture.Metal, 0.02);
    }

    public static (double H, double S, double V) ToHsv(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double v = max, s = max <= 0 ? 0 : (max - min) / max, h = 0;
        double d = max - min;
        if (d > 0)
        {
            if (max == r) h = ((g - b) / d) % 6;
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
            if (h < 0) h += 1;
        }
        return (h, s, v);
    }

    private static (double, double, double) FromHsv(double h, double s, double v)
    {
        double i = Math.Floor(h * 6), f = h * 6 - i;
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        return ((int)i % 6) switch
        {
            0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t),
            3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q),
        };
    }

    // ---- surface -----------------------------------------------------------------------------

    /// <summary>Normals, cavity and convexity of a finished design. Computed once per design.</summary>
    public static ReliefSurface Surface(ReliefCanvas cv, Mask? extraAccent = null)
    {
        int n = cv.N;
        double savedScale = cv.Scale;
        cv.Scale = 1;

        var defined = new bool[n * n];
        for (int i = 0; i < defined.Length; i++) defined[i] = cv.Defined(i);

        // Outside the design, carry the nearest edge height outward so the silhouette does not
        // produce a cliff in the gradients.
        var nearest = Raster.NearestIndex(defined, n);
        var hf = new double[n * n];
        for (int i = 0; i < hf.Length; i++) hf[i] = cv.H[nearest[i]];

        var hs = Raster.Gaussian(hf, n, 0.8 * n / 800.0);
        var nx = new double[n * n];
        var ny = new double[n * n];
        var nz = new double[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                double gx = x == 0 ? hs[i + 1] - hs[i] : x == n - 1 ? hs[i] - hs[i - 1] : (hs[i + 1] - hs[i - 1]) / 2;
                double gy = y == 0 ? hs[i + n] - hs[i] : y == n - 1 ? hs[i] - hs[i - n] : (hs[i + n] - hs[i - n]) / 2;
                double l = Math.Sqrt(gx * gx + gy * gy + 1);
                nx[i] = -gx / l;
                ny[i] = -gy / l;
                nz[i] = 1 / l;
            }

        var blur = Raster.Gaussian(hf, n, n * 0.02);
        var fine = Raster.Gaussian(hf, n, n * 0.006);
        var cav = new double[n * n];
        var convex = new double[n * n];
        var ao = new double[n * n];
        double u05 = cv.U(0.05), u006 = cv.U(0.006);
        for (int i = 0; i < hf.Length; i++)
        {
            cav[i] = Math.Clamp((blur[i] - hf[i]) / u05, 0, 1);
            convex[i] = Math.Clamp((hs[i] - fine[i]) / u006, 0, 1);
            ao[i] = 1 - 0.55 * cav[i];
        }

        var accent = (bool[])cv.Accent.Bits.Clone();
        if (extraAccent is not null)
            for (int i = 0; i < accent.Length; i++) accent[i] |= extraAccent.Bits[i];

        // Rim: the design dilated by a couple of output pixels. Shadow: that silhouette blurred
        // and dropped down and to the right, away from the light.
        var outside = new Mask(n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (!defined[y * n + x]) outside.Set(x, y);
        var dist = Raster.Edt(outside);
        double rim = Math.Max((int)cv.U(0.022), 1);
        var alpha = new double[n * n];
        for (int i = 0; i < alpha.Length; i++) alpha[i] = defined[i] || dist[i] <= rim ? 1 : 0;
        var shadow = Raster.Shift(Raster.Gaussian(alpha, n, cv.U(0.035)), n, cv.U(0.05), cv.U(0.025));

        var surf = new ReliefSurface
        {
            N = n, Defined = defined, Nx = nx, Ny = ny, Nz = nz, Ao = ao, Cavity = cav, Convex = convex,
            Accent = accent, Alpha = alpha, Shadow = shadow,
        };
        cv.Scale = savedScale;
        return surf;
    }

    // ---- shading -----------------------------------------------------------------------------

    /// <summary>
    /// Noise fields depend only on their kind, seed and size, and every icon uses the same two
    /// seeds, so a run computes each at most once. <see cref="ClearCache"/> releases them.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int, ulong), Lazy<double[]>> Cache = new();

    private static double[] Cached(string what, int n, ulong seed, Func<double[]> make)
        => Cache.GetOrAdd((what, n, seed), _ => new Lazy<double[]>(make)).Value;

    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// Colour per pixel, as three planes of 0-255 values. Only pixels where <paramref name="only"/>
    /// is set are shaded; the rest stay black, which is what the compositor never reads.
    /// </summary>
    public static double[][] Shade(ReliefSurface s, Material mat, ulong seed, bool[] only)
    {
        int n = s.N, len = n * n;
        var I = new double[len];
        var diff = new double[len];
        var spec = new double[len];
        double shiny = Math.Min(mat.K, 0.8);
        for (int i = 0; i < len; i++)
        {
            if (!only[i]) continue;
            double d = Math.Clamp(s.Nx[i] * Light.X + s.Ny[i] * Light.Y + s.Nz[i] * Light.Z, 0, 1);
            double h = Math.Clamp(s.Nx[i] * Half.X + s.Ny[i] * Half.Y + s.Nz[i] * Half.Z, 0, 1);
            double ry = 2 * s.Nz[i] * s.Ny[i];
            double env = Math.Clamp(0.5 - 1.3 * ry, 0, 1);
            diff[i] = d;
            spec[i] = Math.Pow(h, mat.Exp);
            I[i] = (0.12 + (0.52 + 0.15 * (1 - shiny)) * d + 0.36 * shiny * env + 0.1 * (1 - shiny)) * s.Ao[i];
        }

        if (mat.Texture != SurfaceTexture.None && mat.Noise > 0)
        {
            var t = Cached("tex_" + mat.Texture, n, seed, () => Texture(n, mat.Texture, new Rng(seed * 31 + 1)));
            for (int i = 0; i < len; i++) I[i] += t[i] * mat.Noise;
        }

        var col = new[] { new double[len], new double[len], new double[len] };
        var speck = new double[len];
        Array.Fill(speck, mat.K);
        Ramp(I, mat.Ramp, col, 0);

        if (mat.Kind == MaterialKind.Wood)
        {
            var late = Cached("wood_late", n, seed, () => WoodLate(n, new Rng(seed * 31 + 2)));
            var fibre = Cached("wood_fibre", n, seed, () => Raster.Noise(n, new Rng(seed * 31 + 3), n * 0.012, 0.8));
            for (int i = 0; i < len; i++)
            {
                double k = (1 - 0.32 * late[i]) * (1 + 0.05 * fibre[i]);
                col[0][i] *= k; col[1][i] *= k; col[2][i] *= k;
            }
        }
        else if (mat.Kind == MaterialKind.Patina)
        {
            // Green corrosion sits in the hollows; copper shows where hands and weather wore it.
            var blotch = Cached("blotch", n, seed, () => Raster.Noise(n, new Rng(seed * 31 + 4), n * 0.02, n * 0.02));
            var worn = Raster.Gaussian(s.Convex, n, 1.5);
            var w = new double[len];
            for (int i = 0; i < len; i++)
                w[i] = Math.Clamp(0.95 + 1.5 * s.Cavity[i] + 0.12 * blotch[i] - 1.3 * worn[i]
                                  - 2.5 * Math.Clamp(diff[i] - 0.82, 0, 1), 0, 1);
            w = Raster.Gaussian(w, n, 1.0);
            var copper = new[] { new double[len], new double[len], new double[len] };
            Ramp(I, CopperRamp, copper, 0.05);
            for (int i = 0; i < len; i++)
            {
                for (int c = 0; c < 3; c++) col[c][i] = copper[c][i] * (1 - w[i]) + col[c][i] * w[i];
                speck[i] *= 1 - w[i];
            }
        }

        for (int i = 0; i < len; i++)
        {
            col[0][i] = Math.Clamp(col[0][i] + spec[i] * mat.Spec.R * speck[i], 0, 255);
            col[1][i] = Math.Clamp(col[1][i] + spec[i] * mat.Spec.G * speck[i], 0, 255);
            col[2][i] = Math.Clamp(col[2][i] + spec[i] * mat.Spec.B * speck[i], 0, 255);
        }
        return col;
    }

    private static void Ramp(double[] I, (double At, (int R, int G, int B) Col)[] stops, double[][] outCol, double bias)
    {
        for (int i = 0; i < I.Length; i++)
        {
            double v = Math.Clamp(I[i] + bias, 0, 1);
            int k = 0;
            while (k < stops.Length - 2 && v > stops[k + 1].At) k++;
            var (a, ca) = stops[k];
            var (b, cb) = stops[k + 1];
            double f = b > a ? Math.Clamp((v - a) / (b - a), 0, 1) : 0;
            outCol[0][i] = ca.R + (cb.R - ca.R) * f;
            outCol[1][i] = ca.G + (cb.G - ca.G) * f;
            outCol[2][i] = ca.B + (cb.B - ca.B) * f;
        }
    }

    private static double[] Texture(int n, SurfaceTexture kind, Rng rng)
    {
        switch (kind)
        {
            case SurfaceTexture.Stone:
            {
                // Fine grain plus a broad mottle. Blurred white noise loses amplitude as
                // 1/(2 sigma sqrt(pi)), so the prototype's "1.5x the sigma-6 field" is 0.375x
                // of the sigma-1.5 one once both are normalised.
                var a = Raster.Noise(n, rng, 1.5, 1.5);
                var b = Raster.Noise(n, rng, 6, 6);
                for (int i = 0; i < a.Length; i++) a[i] += 0.375 * b[i];
                return Raster.Normalise(a);
            }
            case SurfaceTexture.Pitted:
            {
                var pits = new double[n * n];
                for (int i = 0; i < pits.Length; i++) pits[i] = rng.NextDouble() > 0.997 ? 1 : 0;
                pits = Raster.Gaussian(pits, n, 2.5);
                double pm = Math.Max(pits.Max(), 1e-6);
                var a = Raster.Noise(n, rng, 1.2, 1.2);
                // Pits six times deeper than the sigma-1.2 grain's raw amplitude (0.235).
                for (int i = 0; i < a.Length; i++) a[i] -= 25.5 * pits[i] / pm;
                return Raster.Normalise(a);
            }
            default:
                return Raster.Noise(n, rng, 1.2, 1.2);
        }
    }

    /// <summary>Thin dark latewood lines: growth rings from a far-off pith, warped.</summary>
    private static double[] WoodLate(int n, Rng rng)
    {
        var warp = Raster.Noise(n, rng, n * 0.05, n * 0.05);
        var late = new double[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double xx = x / (double)n, yy = y / (double)n;
                double r = Math.Sqrt((xx - 0.37) * (xx - 0.37) + (yy + 0.9) * 0.22 * (yy + 0.9) * 0.22) + 0.012 * warp[y * n + x];
                double ring = (Math.Sin(r * 2 * Math.PI * 34) + 1) / 2;
                late[y * n + x] = Math.Pow(ring, 6);
            }
        return late;
    }

    // ---- compositing -------------------------------------------------------------------------

    /// <summary>
    /// The finished icon as BGRA bytes, <paramref name="size"/> square: <paramref name="main"/> over
    /// the whole design, <paramref name="inlay"/> over its accent region, a dark rim and a drop shadow.
    /// </summary>
    public static byte[] RenderIcon(ReliefSurface s, Material main, Material? inlay, int size)
    {
        int n = s.N, len = n * n;
        var col = Shade(s, main, 3, s.Defined);
        var inlaid = new bool[len];
        bool anyInlay = false;
        for (int i = 0; i < len; i++) anyInlay |= inlaid[i] = s.Accent[i] && s.Defined[i];
        if (inlay is not null && anyInlay)
        {
            var acol = Shade(s, inlay, 7, inlaid);
            for (int i = 0; i < len; i++)
                if (s.Accent[i])
                    for (int c = 0; c < 3; c++) col[c][i] = acol[c][i];
        }

        // The rim takes the material's shadow tone.
        var rimC = main.Ramp[1].Col;
        var a = s.Alpha;
        var sh = s.Shadow;

        // Premultiplied planes for the resample; the shadow is black, so it only adds alpha.
        var planes = new[] { new double[len], new double[len], new double[len], new double[len] };
        for (int i = 0; i < len; i++)
        {
            double outA = a[i] + (1 - a[i]) * sh[i] * 0.8;
            double r = s.Defined[i] ? col[0][i] : rimC.R * 0.45;
            double g = s.Defined[i] ? col[1][i] : rimC.G * 0.45;
            double b = s.Defined[i] ? col[2][i] : rimC.B * 0.45;
            planes[0][i] = r * a[i];
            planes[1][i] = g * a[i];
            planes[2][i] = b * a[i];
            planes[3][i] = outA;
        }

        var small = Raster.Lanczos(planes, n, size);
        int m = size * size;
        var rgb = new double[3][];
        for (int c = 0; c < 3; c++)
        {
            rgb[c] = new double[m];
            for (int i = 0; i < m; i++)
            {
                double al = Math.Clamp(small[3][i], 0, 1);
                rgb[c][i] = al > 0.002 ? Math.Clamp(small[c][i] / al, 0, 255) : 0;
            }
        }

        // Unsharp mask, as PIL's UnsharpMask(radius 0.8, 45 %, threshold 1) on the colour only.
        var bgra = new byte[m * 4];
        for (int c = 0; c < 3; c++)
        {
            var blurred = Raster.Gaussian(rgb[c], size, 0.8);
            for (int i = 0; i < m; i++)
            {
                double d = rgb[c][i] - blurred[i];
                double v = Math.Abs(d) >= 1 ? rgb[c][i] + 0.45 * d : rgb[c][i];
                bgra[i * 4 + (2 - c)] = (byte)Math.Clamp(Math.Round(v), 0, 255);
            }
        }
        for (int i = 0; i < m; i++)
            bgra[i * 4 + 3] = (byte)Math.Clamp(Math.Round(small[3][i] * 255), 0, 255);
        return bgra;
    }
}
