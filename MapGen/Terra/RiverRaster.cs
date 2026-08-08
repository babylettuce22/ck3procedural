using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// Draws the smoothed river courses onto the province-resolution raster CK3 reads as rivers.png.
///
/// CK3 wants a river as a one-pixel chain whose *palette index* encodes the rendered width, not as
/// a thick stroke, so widening a river downstream means changing the colour rather than the
/// geometry. Indices follow vanilla's palette: 0 is a source, 1 a confluence, and 3 (narrowest) to
/// 11 (widest) the width ramp.
/// </summary>
public static class RiverRaster
{
    public const byte None = 255;
    private const byte Source = 0;
    private const byte Join = 1;
    private const byte NarrowestWidth = 3;
    private const byte WidestWidth = 11;

    public sealed class Result
    {
        public required byte[] Pixels;
        public required byte[] Mask;
        public required int RiverPixelCount;
    }

    public static Result Draw(List<RiverCourse> courses, int width, int height, MapConfig cfg)
    {
        var pixels = new byte[width * height];
        var mask = new byte[width * height];
        Array.Fill(pixels, None);

        // Width is ranked against the largest river on the map, on a log scale — drainage area
        // spans four or five orders of magnitude, so a linear ramp would put every river but the
        // biggest handful in the narrowest bucket.
        float peak = 1f;
        foreach (var c in courses)
            if (c.Discharge.Count > 0 && c.Discharge[^1] > peak) peak = c.Discharge[^1];
        float logPeak = MathF.Log(peak + 1f);

        foreach (var course in courses)
        {
            for (int p = 0; p + 1 < course.Points.Count; p++)
            {
                var (ax, ay) = course.Points[p];
                var (bx, by) = course.Points[p + 1];
                byte index = WidthIndex(course.Discharge.Count > p ? course.Discharge[p] : 1f, logPeak);

                Line(pixels, mask, width, height, (int)ax, (int)ay, (int)bx, (int)by, index);
            }

            if (course.Points.Count == 0) continue;

            // The source marker, and for a tributary the confluence marker at the far end. CK3
            // uses these two to work out which way the water is going.
            Stamp(pixels, width, height, course.Points[0], Source);
            if (course.IsTributary) Stamp(pixels, width, height, course.Points[^1], Join);
        }

        int count = 0;
        foreach (byte m in mask) if (m != 0) count++;

        return new Result { Pixels = pixels, Mask = mask, RiverPixelCount = count };
    }

    private static byte WidthIndex(float discharge, float logPeak)
    {
        float t = logPeak <= 0 ? 0 : MathF.Log(discharge + 1f) / logPeak;
        t = Math.Clamp(t, 0f, 1f);

        // Bias toward the narrow end: most of a river network is headwaters.
        t *= t;
        return (byte)Math.Clamp(NarrowestWidth + (int)MathF.Round(t * (WidestWidth - NarrowestWidth)),
            NarrowestWidth, WidestWidth);
    }

    private static void Stamp(byte[] pixels, int width, int height, (float X, float Y) point, byte index)
    {
        int x = (int)point.X, y = (int)point.Y;
        if (x < 0 || y < 0 || x >= width || y >= height) return;
        pixels[y * width + x] = index;
    }

    /// <summary>Bresenham, so consecutive spline samples stay eight-connected.</summary>
    private static void Line(byte[] pixels, byte[] mask, int width, int height,
        int x0, int y0, int x1, int y1, byte index)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            if (x0 >= 0 && y0 >= 0 && x0 < width && y0 < height)
            {
                int i = y0 * width + x0;
                // Never overwrite a source or confluence marker with a plain width.
                if (pixels[i] is None or >= NarrowestWidth) pixels[i] = index;
                mask[i] = 1;
            }

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
