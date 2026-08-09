using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// How large a province wants to be at a given place on the map.
///
/// One target area for the whole world is a large part of what makes a generated province map read
/// as generated: every barony the same size everywhere, which no real map is. Vanilla's own is
/// grossly uneven — a county in Russia or the Sahara covers many times what a north Italian one
/// does — because province size follows how closely a place was settled rather than the
/// convenience of whoever drew the grid.
///
/// This is that unevenness as a field: low-frequency noise over the map, stretched so that the
/// spread from the smallest region to the largest is exactly
/// <see cref="MapConfig.ProvinceSizeVariance"/>, and then rescaled so the number of provinces the
/// map ends up with is unchanged.
///
/// That last step is what keeps the setting a shape control rather than a count control. A
/// province count integrates 1/area, not area, so making half the map's provinces twice as large
/// and the other half half as large does *not* leave the count alone — it raises it, by Jensen.
/// Normalising the mean of 1/factor to 1 cancels that exactly, so turning variance up rewrites
/// where the provinces are without changing how many there are, and the knob can be tuned on its
/// own.
/// </summary>
public sealed class ProvinceSizeField
{
    /// <summary>
    /// Province-map pixels per cell of the coarse field. The field's own wavelength is hundreds of
    /// pixels wide, so sampling it every eight costs nothing and makes the build free.
    /// </summary>
    private const int CellPixels = 8;

    /// <summary>Where the normalised field is cut, so a handful of extreme cells cannot set the
    /// scale for the whole map and flatten everything between them.</summary>
    private const double Tail = 0.05;

    private readonly float[] _factor;
    private readonly int _width;
    private readonly int _height;

    /// <summary>Area multiplier of the map's smallest-province region.</summary>
    public double Smallest { get; private init; }

    /// <summary>Area multiplier of the map's largest-province region.</summary>
    public double Largest { get; private init; }

    private ProvinceSizeField(float[] factor, int width, int height)
    {
        _factor = factor;
        _width = width;
        _height = height;
    }

    /// <summary>Multiplier on the target province area at a province-map pixel.</summary>
    public double At(int x, int y)
        => Field.Sample(_factor, _width, _height,
            (float)x / CellPixels - 0.5f, (float)y / CellPixels - 0.5f);

    public static ProvinceSizeField Build(byte[] mask, int width, int height, MapConfig cfg, Rng rng)
    {
        int cw = Math.Max(2, width / CellPixels), ch = Math.Max(2, height / CellPixels);
        double variance = Math.Max(1.0, cfg.ProvinceSizeVariance);

        if (variance <= 1.0) return Uniform(cw, ch);

        // Referenced to vanilla's province map, so a region of one province size is the same
        // distance across whatever size this map is — the basis every pixel-denominated setting in
        // the tool shares.
        var noise = new SimplexNoise(rng);
        double frequency = 1.0 / Math.Max(1.0, cfg.Scaled(cfg.ProvinceSizeRegionPixels));

        var raw = new float[cw * ch];
        Parallel.For(0, ch, cy =>
        {
            double ny = cy * CellPixels * frequency;
            for (int cx = 0; cx < cw; cx++)
                raw[cy * cw + cx] = (float)Field.Fbm(noise, cx * CellPixels * frequency, ny, 3);
        });

        // Normalised against the land only. Doing it over the whole raster would let the ocean —
        // most of the map, and irrelevant here — decide where the ends of the scale are, so a map
        // whose land happened to sit in the middle of the field would come out nearly uniform.
        var land = new List<float>(cw * ch);
        for (int cy = 0; cy < ch; cy++)
        {
            int y = Math.Min(height - 1, cy * CellPixels + CellPixels / 2);
            for (int cx = 0; cx < cw; cx++)
            {
                int x = Math.Min(width - 1, cx * CellPixels + CellPixels / 2);
                if (mask[y * width + x] == 1) land.Add(raw[cy * cw + cx]);
            }
        }

        if (land.Count == 0) return Uniform(cw, ch);

        land.Sort();
        float lo = land[(int)(land.Count * Tail)];
        float hi = land[(int)Math.Min(land.Count - 1, land.Count * (1 - Tail))];
        if (hi - lo < 1e-6f) return Uniform(cw, ch);

        // [-0.5, 0.5] is what makes variance a ratio the reader can check on the map: the largest
        // region's provinces are exactly `variance` times the area of the smallest region's.
        var factor = new float[cw * ch];
        for (int i = 0; i < raw.Length; i++)
        {
            double t = Math.Clamp((raw[i] - lo) / (hi - lo), 0, 1) - 0.5;
            factor[i] = (float)Math.Pow(variance, t);
        }

        // The count correction. Weighted over land cells for the same reason the scale was.
        double inverse = 0;
        int counted = 0;
        for (int cy = 0; cy < ch; cy++)
        {
            int y = Math.Min(height - 1, cy * CellPixels + CellPixels / 2);
            for (int cx = 0; cx < cw; cx++)
            {
                int x = Math.Min(width - 1, cx * CellPixels + CellPixels / 2);
                if (mask[y * width + x] != 1) continue;
                inverse += 1.0 / factor[cy * cw + cx];
                counted++;
            }
        }

        float correction = counted == 0 ? 1f : (float)(inverse / counted);
        for (int i = 0; i < factor.Length; i++) factor[i] *= correction;

        double smallest = Math.Pow(variance, -0.5) * correction;
        double largest = Math.Pow(variance, 0.5) * correction;

        Console.WriteLine($"  province size varies {smallest:F2}x-{largest:F2}x over regions " +
                          $"{cfg.Scaled(cfg.ProvinceSizeRegionPixels):F0} px across");

        return new ProvinceSizeField(factor, cw, ch) { Smallest = smallest, Largest = largest };
    }

    private static ProvinceSizeField Uniform(int width, int height)
    {
        var factor = new float[width * height];
        Array.Fill(factor, 1f);
        return new ProvinceSizeField(factor, width, height) { Smallest = 1, Largest = 1 };
    }
}
