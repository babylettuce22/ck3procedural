using Ck3MapGen.Config;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

public static class HeightmapNormalizer
{
    public static ushort[] Normalize(ushort[] raw, MapConfig cfg)
    {
        if (cfg.Normalization == HeightmapNormalization.Off) return raw;

        int width = cfg.Width;
        int height = cfg.Height;
        int sourceSea = (int)Math.Round(Math.Clamp(cfg.SourceSeaLevel, 0, 254) * MapDataWriter.Step255);

        var histogram = new int[65536];
        long landCount = 0;
        int landMin = ushort.MaxValue;

        foreach (ushort v in raw)
        {
            if (v <= sourceSea) continue;
            histogram[v]++;
            landCount++;
            if (v < landMin) landMin = v;
        }

        if (landCount == 0)
        {
            Console.WriteLine($"  WARNING: normalisation skipped — no pixel sits above the source " +
                              $"sea level of {cfg.SourceSeaLevel:F0}/255.");
            return raw;
        }

        const int water16 = MapDataWriter.WaterLevel16;
        const int lowestLand = water16 + MapDataWriter.Step255;

        int landFloor = DetectLandFloor(histogram, landMin, cfg.LandFloorDensity);

        long clippedBelow = 0;
        for (int v = landMin; v < landFloor; v++) clippedBelow += histogram[v];

        long anchored = landCount - clippedBelow;
        var want = (long)(anchored * Math.Clamp(cfg.LandTopPercentile, 0, 100) / 100.0);
        long running = 0;
        int landTop = landFloor;

        for (int v = landFloor; v < histogram.Length; v++)
        {
            running += histogram[v];
            if (running < want) continue;
            landTop = v;
            break;
        }

        if (landTop <= landFloor)
        {
            int landMax = landFloor;
            for (int v = histogram.Length - 1; v > landFloor; v--)
                if (histogram[v] != 0) { landMax = v; break; }

            landTop = Math.Max(landFloor + 1, landMax);
        }

        long clippedAbove = 0;
        for (int v = landTop + 1; v < histogram.Length; v++) clippedAbove += histogram[v];

        int topLand = Math.Clamp(
            (int)Math.Round(cfg.LandTop * MapDataWriter.Step255), lowestLand, 65535);

        bool stretch = cfg.Normalization == HeightmapNormalization.Stretch;

        double landSpan = Math.Max(1, landTop - landFloor);
        double landRange = topLand - lowestLand;
        double seaSpan = Math.Max(1, sourceSea);
        double drop = Math.Max(0, landFloor - lowestLand);

        var result = new ushort[raw.Length];

        Parallel.For(0, raw.Length, i =>
        {
            int v = raw[i];
            double scaled;

            if (v > sourceSea)
            {
                if (stretch)
                {
                    if (v < landFloor)
                    {
                        double t = (double)(v - sourceSea) / Math.Max(1, landFloor - sourceSea);
                        scaled = lowestLand + t * (MapDataWriter.Step255 * 2.0);
                    }
                    else
                    {
                        scaled = lowestLand + Math.Min(1.0, (double)(v - landFloor) / landSpan) * landRange;
                    }
                }
                else
                {
                    scaled = v - drop;
                }

                if (scaled < lowestLand) scaled = lowestLand;
            }
            else
            {
                if (sourceSea == 0 || v == 0)
                {
                    scaled = 0;
                }
                else
                {
                    // Scale water smoothly down from WaterLevel16 (19/255) to 0 (deep sea)
                    scaled = (double)v / seaSpan * (water16 - 1);
                }
            }

            result[i] = (ushort)Math.Clamp(Math.Round(scaled), 0, 65535);
        });

        double sourceWaterShare = 100.0 * (raw.LongLength - landCount) / raw.LongLength;
        double floor255 = (double)landFloor / MapDataWriter.Step255;
        double top255 = (double)landTop / MapDataWriter.Step255;

        Console.WriteLine($"  normalised ({cfg.Normalization}): source sea " +
                          $"{cfg.SourceSeaLevel:F0}/255 " +
                          $"({sourceWaterShare:F2}% of the map at or below it) → " +
                          $"{MapDataWriter.WaterLevel255}/255");

        Console.WriteLine($"  land floor detected at {floor255:F2}/255 " +
                          $"(lowest land pixel {(double)landMin / MapDataWriter.Step255:F2})");

        return result;
    }

    /// <summary>
    /// Scales land relief by <see cref="MapConfig.MapScale"/>, so a map narrower than vanilla's
    /// gets proportionally shallower terrain and its slopes come out at vanilla's gradient in
    /// world units. See <see cref="MapConfig.ScaleReliefWithMapSize"/> for why that is the
    /// correct scaling and not merely a taste setting.
    ///
    /// Only the relief finer than <see cref="MapConfig.ReliefDetailRadius"/> is scaled. Each pixel
    /// is moved toward a smooth local mean of the land around it rather than toward the waterline,
    /// so a mountain keeps the height of its mass and loses only the curvature sitting on top of
    /// it. That is what the scaling was always aiming at — LOD sag comes from what the drawn mesh
    /// cannot interpolate between its vertices, not from total relief — and aiming at it directly
    /// is what lets a half-size map hold vanilla's hypsometry and vanilla's gradient at the same
    /// time, which no uniform multiply can do. <see cref="MapConfig.ReliefDetailRadius"/> carries
    /// the argument in full, including why 0 (uniform, the old behaviour) is still reachable.
    ///
    /// Unlike the uniform form, this can raise a pixel as well as lower one: a valley floor far
    /// below its surroundings is pulled up toward them exactly as a crest is pulled down. Both are
    /// counted and logged, because a large raised count on a map with deep inland gorges is the
    /// signal that <see cref="MapConfig.ReliefDetailRadius"/> is wider than that map's landforms.
    ///
    /// A separate pass rather than a factor folded into <see cref="MapConfig.LandTop"/>, which was
    /// the obvious-looking place and is the wrong one: LandTop is only read on the Stretch branch
    /// below, so on the default Shift mode — a pure translation that preserves relief 1:1 — and on
    /// Off it would have done nothing at all. This has to apply whatever the source is on, so it
    /// sits at the one funnel every consumer goes through, <see cref="HeightmapImage.Levels"/>,
    /// after normalisation and including the Ck3Scale short-circuit.
    ///
    /// Anchored on the first land value above the waterline, so every pixel at or below the
    /// waterline is returned bit for bit. That is what keeps the pass invisible to everything that
    /// asks whether a pixel is land — coastline reconciliation in
    /// <see cref="Emit.MapDataWriter"/> most of all, whose whole job is comparing that answer
    /// against provinces.png.
    /// </summary>
    public static ushort[] CompressRelief(ushort[] levels, MapConfig cfg)
    {
        double scale = cfg.ReliefScale;
        if (Math.Abs(scale - 1.0) < 1e-9) return levels;

        const int lowestLand = MapDataWriter.WaterLevel16 + MapDataWriter.Step255;

        int radius = Math.Max(0, cfg.ReliefDetailRadius);

        // The split needs the 2D shape, and the only thing carrying it here is the config, which
        // HeightmapSource.Apply sets from the image before anything can call Levels. Guard rather
        // than trust it: a mismatch would index out of the baseline, and falling back to the
        // uniform form is a worse map, not a broken one.
        if (radius > 0 && (long)cfg.Width * cfg.Height != levels.LongLength)
        {
            Console.WriteLine($"  WARNING: relief detail split skipped — config says " +
                              $"{cfg.Width}x{cfg.Height} but the height field has " +
                              $"{levels.LongLength:N0} samples. Compressing uniformly instead.");
            radius = 0;
        }

        float[]? anchors = radius > 0 ? ReliefAnchors(levels, cfg.Width, cfg.Height, radius) : null;

        var result = new ushort[levels.Length];
        long clipped = 0, raised = 0;

        Parallel.For(0, levels.Length,
            () => (Clipped: 0L, Raised: 0L),
            (i, _, local) =>
            {
                int v = levels[i];
                if (v <= lowestLand) { result[i] = (ushort)v; return local; }

                // Toward the local land mean when the relief is split, toward the waterline when
                // it is not. Clamped up to lowestLand so an anchor that a coarse coastal cell
                // pulled below the first dry value cannot push land under the waterline.
                double anchor = anchors is null ? lowestLand : Math.Max(lowestLand, anchors[i]);
                double scaled = anchor + (v - anchor) * scale;

                if (scaled < lowestLand) scaled = lowestLand;
                if (scaled > 65535) { scaled = 65535; local.Clipped++; }
                if (scaled > v) local.Raised++;

                result[i] = (ushort)Math.Round(scaled);
                return local;
            },
            local =>
            {
                Interlocked.Add(ref clipped, local.Clipped);
                Interlocked.Add(ref raised, local.Raised);
            });

        // The mean's support is +/-2*radius, not +/-radius: LandBaseline runs two box passes at
        // radius each. Report what it actually reaches, so the log and the sweep in
        // MapConfig.ReliefDetailRadius can be read against each other.
        string what = radius > 0
            ? $"detail compressed about the land mean over +/-{2 * radius} px, mountain mass left "
              + "at full height"
            : "land above the waterline compressed toward it";

        Console.WriteLine($"  relief scaled by {scale:F3} (map is {scale:P0} of vanilla's width): "
                          + what
                          + (raised > 0 ? $", {raised:N0} px raised out of hollows" : "")
                          + (clipped > 0 ? $", {clipped:N0} px clipped at the top of the range" : ""));

        return result;
    }

    /// <summary>How much <see cref="LandBaseline"/> shrinks the map before smoothing it.</summary>
    private const int BaselineDivisor = 8;

    /// <summary>
    /// What each land pixel is compressed *toward*: the mean height of the land within
    /// <paramref name="radius"/> px of it, faded back to the waterline as the coast approaches.
    ///
    /// Water is excluded from the mean rather than counted as zero, so an inland pixel's anchor is
    /// the land around it and not something dragged halfway to the sea floor.
    ///
    /// **The fade is not a refinement, it is the thing that makes the split usable.** A shoreline
    /// is a convex corner of the height field — land only rises away from it — so the local mean is
    /// unconditionally above a beach pixel and compressing toward it *raises the beach*, while the
    /// water beside it is returned bit for bit. That is a wall at the coast, and no choice of mean
    /// avoids it: counting water at the waterline, or at its true depth, only changes how tall the
    /// wall is. Measured at radius 64 on the first build of this: coastal land up 1.23/255 on
    /// average, p99 +18, worst +45, curvature within 4 px of water up 38% against not compressing
    /// at all, and the packed atlas 12.4M px against 11.0M — the sag budget paying, in tiles, for
    /// damage this pass had done.
    ///
    /// So the anchor ramps from <see cref="MapDataWriter.WaterLevel16"/>+1 at the water's edge to
    /// the full local mean <paramref name="radius"/> px inland. At the coast that is exactly the
    /// uniform compression this pass did before the split, which cannot raise anything, since every
    /// land value is already at or above the value it is compressed toward. The cost is that land
    /// within a radius of water is compressed as a whole rather than only in its detail, so a cliff
    /// rising straight out of the sea loses height where an inland one does not.
    ///
    /// Built on a 1:<see cref="BaselineDivisor"/> grid and resampled back bilinearly. An anchor
    /// field is smooth at scales far past 8 px by construction, so the shrink costs nothing real,
    /// and it is the difference between ~10 MB and two full float copies of the map — 1.4 GB at
    /// vanilla's 18432x9216. The 8 px grid was checked for print-through and leaves none: mean
    /// second difference by column phase is flat to 2%. Two box passes rather than one, because a
    /// single box leaves its own square footprint in the mean, and the residual is the difference
    /// from it, so the footprint would come back as tiling in the compressed terrain.
    /// </summary>
    private static float[] ReliefAnchors(ushort[] levels, int width, int height, int radius)
    {
        const int water = MapDataWriter.WaterLevel16;
        const int d = BaselineDivisor;

        int cw = (width + d - 1) / d, ch = (height + d - 1) / d;

        // Both are per-cell *averages over the full d x d cell*, land counted and water not, so
        // that a blurred num/den is the true land mean over the window and partly-flooded coastal
        // cells carry only the weight of the land actually in them.
        var num = new float[cw * ch];
        var den = new float[cw * ch];

        Parallel.For(0, ch, cy =>
        {
            int y0 = cy * d, y1 = Math.Min(height, y0 + d);

            for (int cx = 0; cx < cw; cx++)
            {
                int x0 = cx * d, x1 = Math.Min(width, x0 + d);
                double sum = 0;
                int count = 0;

                for (int y = y0; y < y1; y++)
                {
                    long row = (long)y * width;
                    for (int x = x0; x < x1; x++)
                    {
                        int v = levels[row + x];
                        if (v <= water) continue;
                        sum += v;
                        count++;
                    }
                }

                int c = cy * cw + cx;
                num[c] = (float)(sum / (d * d));
                den[c] = (float)count / (d * d);
            }
        });

        // From the unblurred fractions, so the shore it measures from is the real one.
        var shore = WaterDistance(den, cw, ch);

        var blurredDen = (float[])den.Clone();
        int r = Math.Max(1, radius / d);
        BoxBlur(num, cw, ch, r);
        BoxBlur(num, cw, ch, r);
        BoxBlur(blurredDen, cw, ch, r);
        BoxBlur(blurredDen, cw, ch, r);

        const int lowestLand = MapDataWriter.WaterLevel16 + MapDataWriter.Step255;

        // NaN marks a coarse cell with no land in reach, so the resample can skip it rather than
        // average a zero into the coast.
        var coarse = num;
        for (int c = 0; c < coarse.Length; c++)
        {
            if (blurredDen[c] <= 0f) { coarse[c] = float.NaN; continue; }

            float mean = num[c] / blurredDen[c];
            float inland = Math.Clamp(shore[c] * d / radius, 0f, 1f);
            coarse[c] = lowestLand + (mean - lowestLand) * inland;
        }

        var baseline = new float[levels.Length];

        Parallel.For(0, height, y =>
        {
            double fy = (y + 0.5) / d - 0.5;
            int cy0 = (int)Math.Floor(fy);
            double ty = fy - cy0;
            int rowA = Math.Clamp(cy0, 0, ch - 1) * cw;
            int rowB = Math.Clamp(cy0 + 1, 0, ch - 1) * cw;
            long row = (long)y * width;

            for (int x = 0; x < width; x++)
            {
                double fx = (x + 0.5) / d - 0.5;
                int cx0 = (int)Math.Floor(fx);
                double tx = fx - cx0;
                int colA = Math.Clamp(cx0, 0, cw - 1);
                int colB = Math.Clamp(cx0 + 1, 0, cw - 1);

                double acc = 0, weight = 0;

                float t = coarse[rowA + colA];
                double w = (1 - tx) * (1 - ty);
                if (!float.IsNaN(t)) { acc += t * w; weight += w; }

                t = coarse[rowA + colB]; w = tx * (1 - ty);
                if (!float.IsNaN(t)) { acc += t * w; weight += w; }

                t = coarse[rowB + colA]; w = (1 - tx) * ty;
                if (!float.IsNaN(t)) { acc += t * w; weight += w; }

                t = coarse[rowB + colB]; w = tx * ty;
                if (!float.IsNaN(t)) { acc += t * w; weight += w; }

                baseline[row + x] = weight > 0 ? (float)(acc / weight) : 0f;
            }
        });

        return baseline;
    }

    /// <summary>
    /// Distance from each coarse cell to the nearest one that is more sea than land, in coarse
    /// cells, by the usual two-sweep chamfer. Exact enough for a ramp — the error against a true
    /// Euclidean transform is a few percent on diagonals, and it is being divided by a radius and
    /// clamped to 0..1.
    ///
    /// On the coarse grid because that is where the anchor lives; at 1:8 the shore it finds is the
    /// shore to within a cell, and a full-resolution transform of vanilla's 170M px would cost more
    /// than everything else in this file put together.
    /// </summary>
    private static float[] WaterDistance(float[] landFraction, int cw, int ch)
    {
        const float far = 1e9f;
        const float diag = 1.41421356f;

        var dist = new float[cw * ch];
        for (int c = 0; c < dist.Length; c++) dist[c] = landFraction[c] >= 0.5f ? far : 0f;

        for (int y = 0; y < ch; y++)
        {
            for (int x = 0; x < cw; x++)
            {
                int c = y * cw + x;
                if (dist[c] == 0f) continue;

                float best = dist[c];
                if (x > 0) best = Math.Min(best, dist[c - 1] + 1f);
                if (y > 0) best = Math.Min(best, dist[c - cw] + 1f);
                if (x > 0 && y > 0) best = Math.Min(best, dist[c - cw - 1] + diag);
                if (x < cw - 1 && y > 0) best = Math.Min(best, dist[c - cw + 1] + diag);
                dist[c] = best;
            }
        }

        for (int y = ch - 1; y >= 0; y--)
        {
            for (int x = cw - 1; x >= 0; x--)
            {
                int c = y * cw + x;
                if (dist[c] == 0f) continue;

                float best = dist[c];
                if (x < cw - 1) best = Math.Min(best, dist[c + 1] + 1f);
                if (y < ch - 1) best = Math.Min(best, dist[c + cw] + 1f);
                if (x < cw - 1 && y < ch - 1) best = Math.Min(best, dist[c + cw + 1] + diag);
                if (x > 0 && y < ch - 1) best = Math.Min(best, dist[c + cw - 1] + diag);
                dist[c] = best;
            }
        }

        return dist;
    }

    /// <summary>
    /// Separable box blur in place, edges clamped, radius <paramref name="r"/>. The running sum is
    /// a double: a float one drifts visibly across a row of this length once the samples are
    /// 16-bit heights.
    /// </summary>
    private static void BoxBlur(float[] data, int width, int height, int r)
    {
        double window = 2 * r + 1;

        Parallel.For(0, height, y =>
        {
            var line = new float[width];
            int o = y * width;
            Array.Copy(data, o, line, 0, width);

            double sum = 0;
            for (int x = -r; x <= r; x++) sum += line[Math.Clamp(x, 0, width - 1)];

            for (int x = 0; x < width; x++)
            {
                data[o + x] = (float)(sum / window);
                sum -= line[Math.Clamp(x - r, 0, width - 1)];
                sum += line[Math.Clamp(x + r + 1, 0, width - 1)];
            }
        });

        Parallel.For(0, width, x =>
        {
            var line = new float[height];
            for (int y = 0; y < height; y++) line[y] = data[y * width + x];

            double sum = 0;
            for (int y = -r; y <= r; y++) sum += line[Math.Clamp(y, 0, height - 1)];

            for (int y = 0; y < height; y++)
            {
                data[y * width + x] = (float)(sum / window);
                sum -= line[Math.Clamp(y - r, 0, height - 1)];
                sum += line[Math.Clamp(y + r + 1, 0, height - 1)];
            }
        });
    }

    private static int DetectLandFloor(int[] histogram, int landMin, double density)
    {
        if (density <= 0) return landMin;

        var coarse = new long[256];
        for (int v = landMin; v < histogram.Length; v++)
            coarse[v / MapDataWriter.Step255] += histogram[v];

        int mode = 0;
        for (int b = 1; b < coarse.Length; b++)
            if (coarse[b] > coarse[mode]) mode = b;

        double collapse = coarse[mode] * Math.Clamp(density, 0, 1);
        int lowest = landMin / MapDataWriter.Step255;

        int floor = mode;
        while (floor > lowest && coarse[floor - 1] >= collapse) floor--;

        return Math.Max(landMin, floor * MapDataWriter.Step255);
    }
}