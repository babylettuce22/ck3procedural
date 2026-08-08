namespace Ck3MapGen.Core;

/// <summary>
/// 2D simplex noise, equivalent to the <c>SimplexNoise</c> in
/// js/external_libraries/simplex-noise.js but seeded from <see cref="Rng"/> instead of
/// Math.random, so terrain is reproducible.
/// <see cref="Noise2D"/> returns [-1, 1]; <see cref="Unit"/> matches the JS helper
/// <c>genericNoise</c>, which maps that to [0, 1].
/// </summary>
public sealed class SimplexNoise
{
    private static readonly double F2 = 0.5 * (Math.Sqrt(3.0) - 1.0);
    private static readonly double G2 = (3.0 - Math.Sqrt(3.0)) / 6.0;

    private static readonly int[,] Grad3 =
    {
        { 1, 1 }, { -1, 1 }, { 1, -1 }, { -1, -1 },
        { 1, 0 }, { -1, 0 }, { 1, 0 }, { -1, 0 },
        { 0, 1 }, { 0, -1 }, { 0, 1 }, { 0, -1 },
    };

    private readonly byte[] _perm = new byte[512];
    private readonly byte[] _permMod12 = new byte[512];

    public SimplexNoise(Rng rng)
    {
        var p = new byte[256];
        for (int i = 0; i < 256; i++) p[i] = (byte)i;
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Int(0, i);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++)
        {
            _perm[i] = p[i & 255];
            _permMod12[i] = (byte)(_perm[i] % 12);
        }
    }

    /// <summary>Raw simplex value in [-1, 1].</summary>
    public double Noise2D(double xin, double yin)
    {
        double s = (xin + yin) * F2;
        int i = FastFloor(xin + s);
        int j = FastFloor(yin + s);
        double t = (i + j) * G2;
        double x0 = xin - (i - t);
        double y0 = yin - (j - t);

        int i1, j1;
        if (x0 > y0) { i1 = 1; j1 = 0; } else { i1 = 0; j1 = 1; }

        double x1 = x0 - i1 + G2;
        double y1 = y0 - j1 + G2;
        double x2 = x0 - 1.0 + 2.0 * G2;
        double y2 = y0 - 1.0 + 2.0 * G2;

        int ii = i & 255;
        int jj = j & 255;

        double n0 = Corner(x0, y0, _permMod12[ii + _perm[jj]]);
        double n1 = Corner(x1, y1, _permMod12[ii + i1 + _perm[jj + j1]]);
        double n2 = Corner(x2, y2, _permMod12[ii + 1 + _perm[jj + 1]]);

        return 70.0 * (n0 + n1 + n2);
    }

    /// <summary>Port of genericNoise(nx, ny, s) — simplex remapped to [0, 1].</summary>
    public double Unit(double x, double y) => Noise2D(x, y) / 2.0 + 0.5;

    private static double Corner(double x, double y, int gi)
    {
        double t = 0.5 - x * x - y * y;
        if (t < 0) return 0.0;
        t *= t;
        return t * t * (Grad3[gi, 0] * x + Grad3[gi, 1] * y);
    }

    private static int FastFloor(double x)
    {
        int xi = (int)x;
        return x < xi ? xi - 1 : xi;
    }
}
