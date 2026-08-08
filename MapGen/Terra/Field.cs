using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// Scalar-field helpers shared by the terrain stages.
///
/// Nothing here wraps in X. CK3's map is a flat rectangle, not a cylinder — WORLD_EXTENTS_X is a
/// hard edge and the poles are forced to ocean anyway — so wrapping only costs speed and buys an
/// invariant the engine never uses.
/// </summary>
internal static class Field
{
    public static double Fbm(SimplexNoise n, double x, double y, int octaves,
        double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0, amp = 1, freq = 1, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += n.Noise2D(x * freq, y * freq) * amp;
            norm += amp;
            freq *= lacunarity;
            amp *= gain;
        }
        return norm == 0 ? 0 : sum / norm;
    }

    /// <summary>
    /// Ridged multifractal in [-1, 1]. The per-octave weight carried forward is what concentrates
    /// detail onto the crests instead of spreading it evenly, which is what makes a range read as
    /// a ridge rather than as lumpy noise.
    /// </summary>
    public static double Ridged(SimplexNoise n, double x, double y, int octaves,
        double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0, amp = 1, freq = 1, norm = 0, weight = 1;
        for (int o = 0; o < octaves; o++)
        {
            double v = 1.0 - Math.Abs(n.Noise2D(x * freq, y * freq));
            v *= v;
            v *= weight;
            weight = Math.Clamp(v * 2.0, 0, 1);

            sum += v * amp;
            norm += amp;
            freq *= lacunarity;
            amp *= gain;
        }
        return norm == 0 ? 0 : sum / norm * 2.0 - 1.0;
    }

    /// <summary>Hermite smoothstep between two edges, tolerating edge0 &gt; edge1.</summary>
    public static double SmoothStep(double edge0, double edge1, double x)
    {
        if (edge0 == edge1) return x < edge0 ? 0 : 1;
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>
    /// The value at quantile <paramref name="q"/> of the cells the mask selects, via a histogram
    /// rather than a sort — the arrays involved run to hundreds of millions of entries.
    /// </summary>
    public static float Quantile(float[] values, Func<int, bool>? include, double q, int bins = 8192)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < values.Length; i++)
        {
            if (include is not null && !include(i)) continue;
            if (values[i] < lo) lo = values[i];
            if (values[i] > hi) hi = values[i];
        }
        if (lo > hi) return 0;
        if (hi - lo < 1e-9f) return lo;

        var histogram = new long[bins];
        double scale = (bins - 1) / (double)(hi - lo);
        long total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (include is not null && !include(i)) continue;
            histogram[(int)((values[i] - lo) * scale)]++;
            total++;
        }
        if (total == 0) return lo;

        long want = (long)(total * Math.Clamp(q, 0, 1));
        long running = 0;
        for (int b = 0; b < bins; b++)
        {
            running += histogram[b];
            if (running >= want) return (float)(lo + b / scale);
        }
        return hi;
    }

    /// <summary>Separable box blur, repeated to approximate a gaussian. Edges clamp.</summary>
    public static float[] Blur(float[] src, int width, int height, int radius, int passes)
    {
        var a = (float[])src.Clone();
        var b = new float[src.Length];

        for (int p = 0; p < passes; p++)
        {
            Parallel.For(0, height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float sum = 0;
                    int n = 0;
                    for (int d = -radius; d <= radius; d++)
                    {
                        int xx = x + d;
                        if (xx < 0 || xx >= width) continue;
                        sum += a[row + xx];
                        n++;
                    }
                    b[row + x] = sum / n;
                }
            });
            Parallel.For(0, width, x =>
            {
                for (int y = 0; y < height; y++)
                {
                    float sum = 0;
                    int n = 0;
                    for (int d = -radius; d <= radius; d++)
                    {
                        int yy = y + d;
                        if (yy < 0 || yy >= height) continue;
                        sum += b[yy * width + x];
                        n++;
                    }
                    a[y * width + x] = sum / n;
                }
            });
        }

        return a;
    }

    /// <summary>
    /// Catmull-Rom upsample. Bilinear was what the old pipeline used and it leaves first-derivative
    /// creases along every source-cell boundary — invisible in a greyscale dump, clearly visible
    /// once CK3 lights the terrain as a 3D surface.
    /// </summary>
    public static float[] Upsample(float[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new float[(long)dw * dh];
        double sx = (double)sw / dw, sy = (double)sh / dh;

        Parallel.For(0, dh, y =>
        {
            double gy = (y + 0.5) * sy - 0.5;
            int y1 = (int)Math.Floor(gy);
            float fy = (float)(gy - y1);

            Span<float> col = stackalloc float[4];
            for (int x = 0; x < dw; x++)
            {
                double gx = (x + 0.5) * sx - 0.5;
                int x1 = (int)Math.Floor(gx);
                float fx = (float)(gx - x1);

                for (int k = 0; k < 4; k++)
                {
                    int yy = Math.Clamp(y1 - 1 + k, 0, sh - 1) * sw;
                    col[k] = CubicRow(src, yy, x1, fx, sw);
                }

                dst[(long)y * dw + x] = Cubic(col[0], col[1], col[2], col[3], fy);
            }
        });

        return dst;
    }

    private static float CubicRow(float[] src, int rowOffset, int x1, float fx, int sw)
    {
        float p0 = src[rowOffset + Math.Clamp(x1 - 1, 0, sw - 1)];
        float p1 = src[rowOffset + Math.Clamp(x1, 0, sw - 1)];
        float p2 = src[rowOffset + Math.Clamp(x1 + 1, 0, sw - 1)];
        float p3 = src[rowOffset + Math.Clamp(x1 + 2, 0, sw - 1)];
        return Cubic(p0, p1, p2, p3, fx);
    }

    private static float Cubic(float p0, float p1, float p2, float p3, float t)
        => p1 + 0.5f * t * (p2 - p0 + t * (2f * p0 - 5f * p1 + 4f * p2 - p3
                                           + t * (3f * (p1 - p2) + p3 - p0)));

    /// <summary>Bilinear sample of a coarse field at fractional coordinates in that field's space.</summary>
    public static float Sample(float[] src, int sw, int sh, float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        int x0c = Math.Clamp(x0, 0, sw - 1), x1c = Math.Clamp(x0 + 1, 0, sw - 1);
        int y0c = Math.Clamp(y0, 0, sh - 1), y1c = Math.Clamp(y0 + 1, 0, sh - 1);

        float top = src[y0c * sw + x0c] * (1 - fx) + src[y0c * sw + x1c] * fx;
        float bottom = src[y1c * sw + x0c] * (1 - fx) + src[y1c * sw + x1c] * fx;
        return top * (1 - fy) + bottom * fy;
    }

    /// <summary>Box-average downsample by an exact integer factor.</summary>
    public static float[] Downsample(float[] src, int sw, int sh, int factor)
    {
        int dw = sw / factor, dh = sh / factor;
        var dst = new float[(long)dw * dh];
        float inv = 1f / (factor * factor);

        Parallel.For(0, dh, y =>
        {
            for (int x = 0; x < dw; x++)
            {
                float sum = 0;
                for (int j = 0; j < factor; j++)
                {
                    long row = (long)(y * factor + j) * sw + x * factor;
                    for (int i = 0; i < factor; i++) sum += src[row + i];
                }
                dst[(long)y * dw + x] = sum * inv;
            }
        });

        return dst;
    }
}
