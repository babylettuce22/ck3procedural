using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// Stage 4. Turns the drainage network into river courses.
///
/// Rivers are not traced here — the erosion already decided where water goes. This reads the same
/// <see cref="FlowField"/> the last erosion iteration used, keeps the cells whose drainage area is
/// large enough to be a river, and walks that sub-network into trunk-and-tributary courses.
/// Because it is the same network, a river cannot run across a ridge or beside its own valley,
/// which the old downhill walk on a separate coarse grid regularly did.
///
/// The courses are then smoothed: Douglas-Peucker to drop the D8 staircase, a Catmull-Rom
/// resample to put a continuous curve through what is left, and a perpendicular meander offset
/// that grows with discharge. That is where the bends come from.
/// </summary>
public static class RiverNetwork
{
    private static readonly int[] Dx = [-1, 0, 1, -1, 1, -1, 0, 1];
    private static readonly int[] Dy = [-1, -1, -1, 0, 0, 1, 1, 1];

    public static List<RiverCourse> Extract(FlowField.Result flow, float[] height, int width,
        int hgt, float seaLevel, MapConfig cfg, Rng rng)
    {
        int n = width * hgt;
        var down = flow.Down;

        // The channel threshold as a quantile of drainage area over land, so river density is the
        // same on every seed rather than depending on the absolute scale of the flow field.
        float threshold = ChannelThreshold(cfg);

        var channel = new bool[n];
        Parallel.For(0, n, i =>
        {
            channel[i] = height[i] > seaLevel && flow.Flow[i] >= threshold;
        });

        var claimed = new bool[n];
        var courses = new List<RiverCourse>();

        // Trunks first, from every mouth. Walking upstream and always taking the largest
        // contributor is what makes the main stem the main stem; tracing downstream from sources
        // instead gives whichever source was visited first the trunk, which is arbitrary.
        var mouths = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (!channel[i]) continue;
            int d = down[i];
            if (d < 0 || !channel[d]) mouths.Add(i);
        }
        mouths.Sort((a, b) => flow.Flow[b].CompareTo(flow.Flow[a]));

        foreach (int mouth in mouths)
        {
            if (claimed[mouth]) continue;
            var path = WalkUpstream(mouth, channel, claimed, down, flow.Flow, width, hgt);
            if (path.Count >= cfg.TerraMinRiverCells) courses.Add(Build(path, flow, false, width));
            else foreach (int c in path) claimed[c] = true;
        }

        // Then tributaries: any channel cell still unclaimed whose downstream cell is claimed is
        // the confluence of a branch.
        var pending = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (!channel[i] || claimed[i]) continue;
            int d = down[i];
            if (d >= 0 && claimed[d]) pending.Add(i);
        }
        pending.Sort((a, b) => flow.Flow[b].CompareTo(flow.Flow[a]));

        for (int p = 0; p < pending.Count; p++)
        {
            int start = pending[p];
            if (claimed[start]) continue;

            var path = WalkUpstream(start, channel, claimed, down, flow.Flow, width, hgt);
            if (path.Count < cfg.TerraMinRiverCells) continue;

            // Carry the course one cell into the trunk so the two are visually connected.
            path.Add(down[start]);
            courses.Add(Build(path, flow, true, width));

            // A branch may have opened new confluences further up.
            foreach (int c in path)
            {
                int cx = c % width, cy = c / width;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx[k], ny = cy + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= hgt) continue;
                    int nb = ny * width + nx;
                    if (channel[nb] && !claimed[nb] && down[nb] == c) pending.Add(nb);
                }
            }
        }

        Smooth(courses, cfg, rng);
        return courses;
    }

    /// <summary>Walks from a mouth to the head of the largest branch above it, source first.</summary>
    private static List<int> WalkUpstream(int mouth, bool[] channel, bool[] claimed, int[] down,
        float[] flow, int width, int hgt)
    {
        var path = new List<int> { mouth };
        claimed[mouth] = true;
        int current = mouth;

        while (true)
        {
            int best = -1;
            float bestFlow = 0;
            int cx = current % width, cy = current / width;

            for (int k = 0; k < 8; k++)
            {
                int nx = cx + Dx[k], ny = cy + Dy[k];
                if (nx < 0 || ny < 0 || nx >= width || ny >= hgt) continue;

                int nb = ny * width + nx;
                if (!channel[nb] || claimed[nb] || down[nb] != current) continue;
                if (flow[nb] <= bestFlow) continue;

                bestFlow = flow[nb];
                best = nb;
            }

            if (best < 0) break;
            claimed[best] = true;
            path.Add(best);
            current = best;
        }

        path.Reverse();
        return path;
    }

    private static RiverCourse Build(List<int> cells, FlowField.Result flow, bool tributary,
        int width)
    {
        var course = new RiverCourse { IsTributary = tributary };
        course.Points.Capacity = cells.Count;
        course.Discharge.Capacity = cells.Count;

        foreach (int c in cells)
        {
            course.Points.Add((c % width + 0.5f, c / width + 0.5f));
            course.Discharge.Add(flow.Flow[c]);
        }
        return course;
    }

    /// <summary>
    /// A cell carries a river once its catchment exceeds a fixed number of cells.
    ///
    /// Absolute, not a quantile. Taking the top N% of land cells by drainage makes what counts as
    /// a river depend on how much land is on the map: the same stream is a river on a small map and
    /// a trickle on a large one. A cell is the same physical area at every map size, so a fixed
    /// catchment in cells is a fixed catchment in square kilometres — which is what actually
    /// decides whether a watercourse is worth drawing.
    /// </summary>
    private static float ChannelThreshold(MapConfig cfg)
        => (float)Math.Max(4.0, cfg.RiverMinCatchmentCells);

    // --- smoothing ---

    /// <summary>
    /// Replaces each course's cell path with a smooth polyline. The cells are re-derived from the
    /// flow field into pixel centres first, so the geometry and the discharge stay in step.
    /// </summary>
    private static void Smooth(List<RiverCourse> courses, MapConfig cfg, Rng rng)
    {
        var meander = new SimplexNoise(rng);

        Parallel.ForEach(courses, course =>
        {
            var raw = course.Points;
            if (raw.Count < 2) return;

            var keep = Simplify(raw, cfg.TerraRiverSimplify);
            if (keep.Count < 2) { return; }

            var dense = Resample(keep, 0.85f);
            var discharge = ResampleDischarge(course.Discharge, dense.Count);

            // A perpendicular offset along the course. Amplitude grows with discharge because a
            // big river meanders across a wide floodplain and a mountain stream does not.
            float maxAmp = (float)cfg.TerraMeanderPixels;
            float peak = discharge.Count == 0 ? 1f : discharge[^1];

            var result = new List<(float X, float Y)>(dense.Count);
            for (int i = 0; i < dense.Count; i++)
            {
                var (px, py) = dense[i];
                var (ax, ay) = dense[Math.Max(0, i - 1)];
                var (bx, by) = dense[Math.Min(dense.Count - 1, i + 1)];

                float tx = bx - ax, ty = by - ay;
                float len = MathF.Sqrt(tx * tx + ty * ty);
                if (len < 1e-4f) { result.Add((px, py)); continue; }

                float scale = peak <= 0 ? 0 : MathF.Sqrt(Math.Clamp(discharge[i] / peak, 0, 1));
                float amp = maxAmp * scale;

                // Taper to nothing at both ends so the source and the confluence stay put.
                float t = i / (float)Math.Max(1, dense.Count - 1);
                amp *= MathF.Min(1f, MathF.Min(t, 1f - t) * 6f);

                double offset = meander.Noise2D(px * 0.06, py * 0.06)
                                + 0.5 * meander.Noise2D(px * 0.17, py * 0.17);
                float d = (float)(offset / 1.5) * amp;

                result.Add((px - ty / len * d, py + tx / len * d));
            }

            course.Points = result;
            course.Discharge = discharge;
        });
    }

    /// <summary>Douglas-Peucker. Iterative, because a river can run for thousands of cells.</summary>
    private static List<(float X, float Y)> Simplify(List<(float X, float Y)> pts, double tolerance)
    {
        int n = pts.Count;
        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;

        var stack = new Stack<(int Lo, int Hi)>();
        stack.Push((0, n - 1));

        while (stack.Count > 0)
        {
            var (lo, hi) = stack.Pop();
            if (hi <= lo + 1) continue;

            var (ax, ay) = pts[lo];
            var (bx, by) = pts[hi];
            float dx = bx - ax, dy = by - ay;
            float len = MathF.Sqrt(dx * dx + dy * dy);

            double worst = -1;
            int worstAt = -1;
            for (int i = lo + 1; i < hi; i++)
            {
                var (px, py) = pts[i];
                double d = len < 1e-6f
                    ? Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay))
                    : Math.Abs(dx * (ay - py) - (ax - px) * dy) / len;
                if (d > worst) { worst = d; worstAt = i; }
            }

            if (worstAt < 0 || worst <= tolerance) continue;
            keep[worstAt] = true;
            stack.Push((lo, worstAt));
            stack.Push((worstAt, hi));
        }

        var result = new List<(float X, float Y)>();
        for (int i = 0; i < n; i++) if (keep[i]) result.Add(pts[i]);
        return result;
    }

    /// <summary>Catmull-Rom through the kept points, resampled at roughly one point per pixel.</summary>
    private static List<(float X, float Y)> Resample(List<(float X, float Y)> pts, float spacing)
    {
        var result = new List<(float X, float Y)>();

        for (int i = 0; i < pts.Count - 1; i++)
        {
            var p0 = pts[Math.Max(0, i - 1)];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = pts[Math.Min(pts.Count - 1, i + 2)];

            float segment = MathF.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y));
            int steps = Math.Max(1, (int)MathF.Ceiling(segment / spacing));

            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps;
                result.Add((Spline(p0.X, p1.X, p2.X, p3.X, t), Spline(p0.Y, p1.Y, p2.Y, p3.Y, t)));
            }
        }

        result.Add(pts[^1]);
        return result;
    }

    private static float Spline(float p0, float p1, float p2, float p3, float t)
        => 0.5f * (2f * p1 + (p2 - p0) * t
                   + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                   + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);

    private static List<float> ResampleDischarge(List<float> src, int count)
    {
        var result = new List<float>(count);
        if (src.Count == 0) { for (int i = 0; i < count; i++) result.Add(1f); return result; }

        for (int i = 0; i < count; i++)
        {
            float t = count <= 1 ? 0 : i / (float)(count - 1) * (src.Count - 1);
            int a = Math.Clamp((int)t, 0, src.Count - 1);
            int b = Math.Min(a + 1, src.Count - 1);
            result.Add(src[a] + (src[b] - src[a]) * (t - a));
        }
        return result;
    }

}
