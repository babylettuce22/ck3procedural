using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// Stages 5-7. Everything that happens at the exported heightmap's own resolution.
///
/// The coarse world carries where continents, ranges and valleys are; none of it has any detail
/// finer than a base cell, which is four export pixels. This adds that detail and then erodes it,
/// so the fine structure is worn rather than sprinkled on.
///
/// Every routine here is written to avoid allocating a second array the size of the heightmap. At
/// vanilla dimensions that array is 680 MB, and the old pipeline held two of them at once inside
/// <c>HeightDetail.CarveRivers</c>. The passes that genuinely need neighbour reads run over
/// horizontal bands with a three-row rolling window, with the row on each band seam saved up front
/// so a band never reads a value another band has already overwritten — which keeps the result
/// identical whatever the thread count.
/// </summary>
public static class DetailPass
{
    /// <summary>
    /// Adds fine relief, gated on the coarse slope so flats stay flat and only real terrain gets
    /// roughened. The gate is sampled from the coarse field rather than from the full-resolution
    /// neighbours, which keeps this a pure per-pixel function — no ordering, no races, no scratch
    /// buffer.
    /// </summary>
    public static void AddDetail(float[] full, int fw, int fh, float[] coarse, int cw, int ch,
        float seaLevel, MapConfig cfg, Rng rng)
    {
        var warp = new SimplexNoise(rng);
        var ridge = new SimplexNoise(rng);
        var rolling = new SimplexNoise(rng);

        double ridgeFreq = cfg.TerraDetailScale / fw;
        double rollFreq = ridgeFreq * 0.42;
        double warpFreq = ridgeFreq * 0.28;
        double warpAmp = fw / cfg.TerraDetailScale * 2.4;

        float sx = (float)cw / fw, sy = (float)ch / fh;

        Parallel.For(0, fh, y =>
        {
            float gy = (y + 0.5f) * sy - 0.5f;

            for (int x = 0; x < fw; x++)
            {
                long i = (long)y * fw + x;
                float h = full[i];
                if (h <= seaLevel) continue;

                float gx = (x + 0.5f) * sx - 0.5f;

                // Coarse slope, in height per coarse cell.
                float left = Field.Sample(coarse, cw, ch, gx - 1, gy);
                float right = Field.Sample(coarse, cw, ch, gx + 1, gy);
                float up = Field.Sample(coarse, cw, ch, gx, gy - 1);
                float dn = Field.Sample(coarse, cw, ch, gx, gy + 1);
                float slope = MathF.Sqrt((right - left) * (right - left)
                                         + (dn - up) * (dn - up)) * 0.5f;

                float rugged = MathF.Min(1f, slope / cfg.TerraDetailSlopeRefScaled);

                // Fade to nothing at the waterline, or the detail cuts holes in the coast that
                // the province partition then has to disagree with.
                float shore = MathF.Min(1f, (h - seaLevel) / 0.012f);

                double qx = warp.Noise2D(x * warpFreq, y * warpFreq) * warpAmp;
                double qy = warp.Noise2D(x * warpFreq + 11.3, y * warpFreq - 6.7) * warpAmp;
                double nx = (x + qx) * ridgeFreq, ny = (y + qy) * ridgeFreq;

                double sharp = Field.Ridged(ridge, nx, ny, 5);
                double soft = Field.Fbm(rolling, x * rollFreq, y * rollFreq, 4);

                // fBm dominant, ridged only as a minority term.
                //
                // This is a substrate for the refinement erosion to cut, not the final shape.
                // Leaning on ridged multifractal here — which is what this used to do on any slope
                // at all — bakes in concentric ring-and-spoke structures that correspond to nothing
                // hydrological, and at export resolution they read as swirls. Ridges in real
                // terrain are what is *left standing* after erosion removes the material around
                // them, so they have to come out of the drainage pass, not out of the noise.
                double value = soft + sharp * rugged * 0.35;

                // The floor matters as much as the ceiling. Vanilla's heightmap carries visible
                // ridge-and-valley texture across its *lowlands*, not only on its ranges; gating
                // detail almost entirely on slope leaves flat ground looking like a smooth ramp,
                // which is what a straight bicubic upsample of the coarse grid already looks like.
                float amplitude = cfg.TerraDetailAmplitude * (0.35f + 0.65f * rugged) * shore;

                full[i] = MathF.Max(seaLevel + 1e-4f, h + (float)(value * amplitude));
            }
        });
    }

    /// <summary>
    /// Adds a coarse height *delta* onto the full-resolution field, sampled bilinearly.
    ///
    /// This is how the province-resolution refinement erosion reaches the heightmap. Carrying the
    /// difference rather than the refined surface itself preserves every bit of full-resolution
    /// detail that was already there and adds only what the erosion changed, and sampling the
    /// coarse array per pixel avoids allocating a second array the size of the heightmap.
    /// </summary>
    public static void ApplyDelta(float[] full, int fw, int fh, float[] delta, int cw, int ch,
        float seaLevel)
    {
        float sx = (float)cw / fw, sy = (float)ch / fh;

        Parallel.For(0, fh, y =>
        {
            float gy = (y + 0.5f) * sy - 0.5f;
            for (int x = 0; x < fw; x++)
            {
                long i = (long)y * fw + x;
                float h = full[i];
                if (h <= seaLevel) continue;

                float gx = (x + 0.5f) * sx - 0.5f;
                full[i] = MathF.Max(seaLevel + 1e-4f, h + Field.Sample(delta, cw, ch, gx, gy));
            }
        });
    }

    /// <summary>
    /// Slope-limited relaxation over the whole heightmap: anything steeper than the talus angle is
    /// pulled back toward its neighbours. This is what turns the ridged detail added above into
    /// something that looks worn instead of like noise.
    ///
    /// Banded with a three-row rolling window so it needs no second full-size array, and the rows
    /// on the band seams are copied out before any writing starts so the answer does not depend on
    /// how the bands were scheduled.
    /// </summary>
    public static void Relax(float[] full, int fw, int fh, float seaLevel, float talus, float rate)
    {
        int bands = Math.Max(1, Math.Min(Environment.ProcessorCount * 2, fh / 64));
        int bandRows = (fh + bands - 1) / bands;

        // Original copies of both rows on every band seam. A band's first row is read by the band
        // above it as its "row below", and its last row by the band beneath it as its "row above",
        // in both cases after the owning band may already have rewritten it.
        var seamFirst = new float[bands][];
        var seamLast = new float[bands][];
        for (int b = 0; b < bands; b++)
        {
            int y0 = b * bandRows;
            int y1 = Math.Min(y0 + bandRows, fh);
            if (y0 >= y1) continue;

            seamFirst[b] = new float[fw];
            seamLast[b] = new float[fw];
            Array.Copy(full, (long)y0 * fw, seamFirst[b], 0, fw);
            Array.Copy(full, (long)(y1 - 1) * fw, seamLast[b], 0, fw);
        }

        Parallel.For(0, bands, b =>
        {
            int y0 = b * bandRows;
            int y1 = Math.Min(y0 + bandRows, fh);
            if (y0 >= y1) return;

            var above = new float[fw];
            var current = new float[fw];
            var below = new float[fw];

            if (b > 0 && seamLast[b - 1] is { } previous) Array.Copy(previous, above, fw);
            else Array.Copy(full, (long)y0 * fw, above, 0, fw);

            Array.Copy(full, (long)y0 * fw, current, 0, fw);

            for (int y = y0; y < y1; y++)
            {
                if (y + 1 >= y1)
                {
                    if (b + 1 < bands && seamFirst[b + 1] is { } following)
                        Array.Copy(following, below, fw);
                    else
                        Array.Copy(current, below, fw);
                }
                else
                {
                    Array.Copy(full, (long)(y + 1) * fw, below, 0, fw);
                }

                for (int x = 0; x < fw; x++)
                {
                    float h = current[x];
                    if (h <= seaLevel) continue;

                    int xl = Math.Max(0, x - 1), xr = Math.Min(fw - 1, x + 1);

                    // Steepest drop to any of the eight neighbours.
                    float drop = 0;
                    drop = MathF.Max(drop, h - current[xl]);
                    drop = MathF.Max(drop, h - current[xr]);
                    drop = MathF.Max(drop, h - above[x]);
                    drop = MathF.Max(drop, h - below[x]);
                    drop = MathF.Max(drop, (h - above[xl]) * 0.70710678f);
                    drop = MathF.Max(drop, (h - above[xr]) * 0.70710678f);
                    drop = MathF.Max(drop, (h - below[xl]) * 0.70710678f);
                    drop = MathF.Max(drop, (h - below[xr]) * 0.70710678f);

                    if (drop <= talus) continue;
                    full[(long)y * fw + x] = MathF.Max(seaLevel + 1e-4f,
                        h - (drop - talus) * rate);
                }

                (above, current, below) = (current, below, above);
            }
        });
    }

    /// <summary>
    /// Cuts each river's channel into the heightmap: a V-notch whose width and depth grow with
    /// discharge, plus a shallower floodplain shoulder.
    ///
    /// Without this the ground under a river is no lower than its banks, and CK3 renders the water
    /// surface floating across the terrain. The notch follows the *smoothed* course, not the raw
    /// D8 path, so the valley in the heightmap and the blue line in rivers.png are the same curve.
    /// </summary>
    public static void CarveChannels(float[] full, int fw, int fh, List<RiverCourse> courses,
        int pw, int ph, float seaLevel, MapConfig cfg)
    {
        float scale = (float)fw / pw;
        float peak = 1f;
        foreach (var c in courses)
            if (c.Discharge.Count > 0 && c.Discharge[^1] > peak) peak = c.Discharge[^1];

        foreach (var course in courses)
        {
            for (int p = 0; p < course.Points.Count; p++)
            {
                var (px, py) = course.Points[p];
                float q = course.Discharge.Count > p ? course.Discharge[p] : 1f;
                float strength = MathF.Sqrt(Math.Clamp(q / peak, 0.02f, 1f));

                int radius = (int)MathF.Round(cfg.TerraChannelRadius * scale * (0.45f + 0.55f * strength));
                if (radius < 1) radius = 1;
                float depth = cfg.TerraChannelDepth * (0.35f + 0.65f * strength);

                int cx = (int)(px * scale), cy = (int)(py * scale);

                for (int dy = -radius; dy <= radius; dy++)
                {
                    int yy = cy + dy;
                    if (yy < 0 || yy >= fh) continue;

                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int xx = cx + dx;
                        if (xx < 0 || xx >= fw) continue;

                        float d = MathF.Sqrt(dx * dx + dy * dy) / radius;
                        if (d > 1f) continue;

                        long i = (long)yy * fw + xx;
                        float h = full[i];
                        if (h <= seaLevel) continue;

                        float cut = depth * (1f - d) * (1f - d);
                        full[i] = MathF.Max(seaLevel + 1e-4f, h - cut);
                    }
                }
            }
        }
    }
}
