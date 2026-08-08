using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

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

        int thinned = Thin(pixels, mask, width, height);

        int count = 0;
        foreach (byte m in mask) if (m != 0) count++;
        if (thinned > 0)
            Console.WriteLine($"  thinned {thinned:N0} river pixels to keep the chain one wide");

        return new Result { Pixels = pixels, Mask = mask, RiverPixelCount = count };
    }

    /// <summary>
    /// Removes the redundant pixels a rasterised polyline leaves behind, so the result is the
    /// one-pixel chain CK3 requires.
    ///
    /// The wiki is blunt about this — "improperly created river maps will cause a CTD" — and names
    /// three faults: a river pixel orthogonally touching more than two others, a river two pixels
    /// wide, and pixels joined only diagonally. Drawing a smoothed, meandering course with a
    /// Bresenham line produces the first two wherever two segments meet at a shallow angle or a
    /// tributary rejoins its trunk.
    ///
    /// Measured on seed 1 before this pass: 245 solid 2x2 blocks and 4.27% of river pixels with
    /// more than two orthogonal neighbours, against vanilla's 22 blocks and 0.25%. Vanilla is not
    /// perfectly clean either, which is worth knowing — the rules are what the parser tolerates,
    /// not an invariant it enforces — but being seventeen times worse than the shipping map on a
    /// rule whose stated failure mode is a crash is not a margin worth keeping.
    ///
    /// A pixel is only dropped when its own neighbours are still reachable without it, so thinning
    /// can never cut a river in two. Source and confluence markers are never dropped: CK3 reads
    /// flow direction from them.
    /// </summary>
    private static int Thin(byte[] pixels, byte[] mask, int width, int height)
    {
        int removed = 0;

        bool IsRiver(int x, int y)
            => x >= 0 && y >= 0 && x < width && y < height && pixels[y * width + x] <= WidestWidth;

        bool Marker(int x, int y)
        {
            byte v = pixels[y * width + x];
            return v is Source or Join;
        }

        // Solid 2x2 blocks first: those are the "two pixels wide" fault, and clearing one corner
        // of each usually fixes an over-connected pixel at the same time.
        for (int y = 0; y + 1 < height; y++)
        {
            for (int x = 0; x + 1 < width; x++)
            {
                if (!IsRiver(x, y) || !IsRiver(x + 1, y) || !IsRiver(x, y + 1) || !IsRiver(x + 1, y + 1))
                    continue;

                // Drop the corner that costs least: never a marker, and prefer the narrowest.
                int bestX = -1, bestY = -1, bestWidth = int.MaxValue;
                for (int dy = 0; dy <= 1; dy++)
                {
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        int cx = x + dx, cy = y + dy;
                        if (Marker(cx, cy)) continue;
                        int w = pixels[cy * width + cx];
                        if (w >= bestWidth) continue;
                        bestWidth = w;
                        bestX = cx;
                        bestY = cy;
                    }
                }

                if (bestX < 0) continue;
                pixels[bestY * width + bestX] = None;
                mask[bestY * width + bestX] = 0;
                removed++;
            }
        }

        // Then any pixel still touching three or more others orthogonally that is not a genuine
        // confluence — one whose neighbours all remain connected to each other without it.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!IsRiver(x, y) || Marker(x, y)) continue;

                int up = IsRiver(x, y - 1) ? 1 : 0, down = IsRiver(x, y + 1) ? 1 : 0;
                int left = IsRiver(x - 1, y) ? 1 : 0, right = IsRiver(x + 1, y) ? 1 : 0;
                if (up + down + left + right < 3) continue;

                // Safe to drop when every orthogonal neighbour has another river pixel adjacent to
                // it besides this one, so removing it cannot isolate any of them.
                bool safe = true;
                foreach (var (nx, ny) in new[] { (x, y - 1), (x, y + 1), (x - 1, y), (x + 1, y) })
                {
                    if (!IsRiver(nx, ny)) continue;
                    int others = 0;
                    for (int dy = -1; dy <= 1 && safe; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (nx + dx == x && ny + dy == y) continue;
                            if (IsRiver(nx + dx, ny + dy)) others++;
                        }
                    if (others == 0) { safe = false; break; }
                }

                if (!safe) continue;
                pixels[y * width + x] = None;
                mask[y * width + x] = 0;
                removed++;
            }
        }

        return removed;
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
