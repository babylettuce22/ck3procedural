using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public sealed class MajorRiverPath
{
    public required List<(float X, float Y)> Points { get; init; }
    public required float TotalLength { get; init; }
}

public static class MajorRivers
{
    public static List<MajorRiverPath> ExtractAndCarve(
        float[] fullElev,
        int fullWidth,
        int fullHeight,
        Drainage drainage,
        MapConfig cfg,
        Rng rng)
    {
        if (!cfg.EnableMajorRivers || cfg.MajorRiverCount <= 0)
            return [];

        int pw = cfg.ProvinceWidth;
        int ph = cfg.ProvinceHeight;
        float sea = cfg.Limits.SeaLevelUpper;

        // 1. Find sea outlets and rank by catchment flow.
        var candidateOutlets = FindSeaOutlets(drainage, cfg, pw, ph);
        candidateOutlets.Sort((a, b) => b.Flow.CompareTo(a.Flow));

        var paths = new List<MajorRiverPath>();
        var occupied = new bool[pw * ph];
        int targetRivers = cfg.MajorRiverCount;

        var feeders = new List<int>[pw * ph];
        for (int i = 0; i < drainage.Receiver.Length; i++)
        {
            int into = drainage.Receiver[i];
            if (into != i && drainage.LandMask[i] != 0)
            {
                (feeders[into] ??= []).Add(i);
            }
        }

        foreach (var (outlet, flow) in candidateOutlets)
        {
            if (paths.Count >= targetRivers) break;
            if (occupied[outlet]) continue;

            var rawPoints = TraceUpstream(outlet, drainage, feeders, pw, ph, occupied, sea, cfg);

            if (rawPoints.Count >= (int)Math.Max(15, cfg.Scaled(30)))
            {
                rawPoints.Reverse(); // Source -> mouth

                // Smooth out 45°/90° raster staircase steps into natural meanders
                var smoothedPoints = SmoothAndResamplePath(rawPoints, stepSize: 1.0f);

                if (smoothedPoints.Count >= 2)
                {
                    paths.Add(new MajorRiverPath
                    {
                        Points = smoothedPoints,
                        TotalLength = smoothedPoints.Count,
                    });
                }
            }
        }

        // 2. Carve channels with strict headwater tapering and gentle valley shoulders
        CarveHeightmapChannels(fullElev, fullWidth, fullHeight, paths, cfg);

        Console.WriteLine($"  major rivers: extracted, spline-smoothed and carved {paths.Count} major river system(s)");
        return paths;
    }

    private static List<(float X, float Y)> TraceUpstream(
        int outlet,
        Drainage drainage,
        List<int>[] feeders,
        int width,
        int height,
        bool[] occupied,
        float sea,
        MapConfig cfg)
    {
        var pts = new List<(float X, float Y)>();
        int curr = outlet;

        // Stop major river before it cuts into high mountains
        float maxMajorRiverElevation = sea + (float)cfg.RiverMaxRiseAboveSea;
        float minTraceFlow = (float)cfg.RiverTraceMinFlow;

        while (curr >= 0 && pts.Count < 2000)
        {
            int cx = curr % width, cy = curr / width;
            pts.Add((cx, cy));
            occupied[curr] = true;

            // 1. Stop if entering a genuine lake basin or deep depression (<= 2.0m tolerated)
            if (drainage.LakeDepth(curr) > 2.0f)
                break;

            // 2. Stop if elevation climbs into the mountain foothills
            if (drainage.Filled[curr] > maxMajorRiverElevation)
                break;

            var upstream = feeders[curr];
            if (upstream == null || upstream.Count == 0) break;

            int bestFeeder = -1;
            float maxFlow = 0f;
            foreach (int f in upstream)
            {
                if (occupied[f]) continue;
                if (drainage.Flow[f] > maxFlow)
                {
                    maxFlow = drainage.Flow[f];
                    bestFeeder = f;
                }
            }

            // 3. Stop when flow falls below major river volume
            if (bestFeeder < 0 || maxFlow < minTraceFlow)
                break;

            curr = bestFeeder;
        }

        return pts;
    }

    /// <summary>
    /// Smooths a discrete grid path using Centripetal Catmull-Rom splines (alpha = 0.5)
    /// and resamples the curve at equidistant arc lengths.
    /// Centripetal splines are chosen specifically to eliminate cusps and overshoots on tight turns.
    /// </summary>
    private static List<(float X, float Y)> SmoothAndResamplePath(List<(float X, float Y)> raw, float stepSize)
    {
        if (raw.Count < 3) return new List<(float X, float Y)>(raw);

        // 1. Build extended control points with end-point clamping
        var cp = new List<(float X, float Y)>(raw.Count + 2);
        cp.Add((2f * raw[0].X - raw[1].X, 2f * raw[0].Y - raw[1].Y)); // Extrapolated P-1
        cp.AddRange(raw);
        cp.Add((2f * raw[^1].X - raw[^2].X, 2f * raw[^1].Y - raw[^2].Y)); // Extrapolated P+1

        // 2. Subsample each spline segment finely
        var denseSpline = new List<(float X, float Y)>();
        const int SubdivisionsPerSegment = 8;

        for (int i = 1; i < cp.Count - 2; i++)
        {
            var p0 = cp[i - 1];
            var p1 = cp[i];
            var p2 = cp[i + 1];
            var p3 = cp[i + 2];

            // Centripetal knot calculation (alpha = 0.5)
            float t0 = 0.0f;
            float t1 = t0 + MathF.Pow(DistSq(p0, p1), 0.25f);
            float t2 = t1 + MathF.Pow(DistSq(p1, p2), 0.25f);
            float t3 = t2 + MathF.Pow(DistSq(p2, p3), 0.25f);

            if (t1 - t0 < 1e-4f) t1 = t0 + 1e-4f;
            if (t2 - t1 < 1e-4f) t2 = t1 + 1e-4f;
            if (t3 - t2 < 1e-4f) t3 = t2 + 1e-4f;

            for (int step = 0; step < SubdivisionsPerSegment; step++)
            {
                float t = t1 + (t2 - t1) * (step / (float)SubdivisionsPerSegment);

                // Hierarchical linear blending
                var a1 = LerpPoint(p0, p1, (t1 - t) / (t1 - t0), (t - t0) / (t1 - t0));
                var a2 = LerpPoint(p1, p2, (t2 - t) / (t2 - t1), (t - t1) / (t2 - t1));
                var a3 = LerpPoint(p2, p3, (t3 - t) / (t3 - t2), (t - t2) / (t3 - t2));

                var b1 = LerpPoint(a1, a2, (t2 - t) / (t2 - t0), (t - t0) / (t2 - t0));
                var b2 = LerpPoint(a2, a3, (t3 - t) / (t3 - t1), (t - t1) / (t3 - t1));

                var c = LerpPoint(b1, b2, (t2 - t) / (t2 - t1), (t - t1) / (t2 - t1));

                denseSpline.Add(c);
            }
        }
        denseSpline.Add(raw[^1]);

        // 3. Resample densely evaluated spline onto equidistant arc-length intervals
        var resampled = new List<(float X, float Y)> { denseSpline[0] };
        float accumulated = 0f;

        for (int i = 1; i < denseSpline.Count; i++)
        {
            float segDist = MathF.Sqrt(DistSq(denseSpline[i - 1], denseSpline[i]));
            accumulated += segDist;

            if (accumulated >= stepSize)
            {
                resampled.Add(denseSpline[i]);
                accumulated = 0f;
            }
        }

        if (DistSq(resampled[^1], raw[^1]) > 0.01f)
        {
            resampled.Add(raw[^1]);
        }

        return resampled;

        static float DistSq((float X, float Y) a, (float X, float Y) b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        static (float X, float Y) LerpPoint((float X, float Y) a, (float X, float Y) b, float wa, float wb)
            => (a.X * wa + b.X * wb, a.Y * wa + b.Y * wb);
    }

    private static List<(int Cell, float Flow)> FindSeaOutlets(
        Drainage drainage, MapConfig cfg, int pw, int ph)
    {
        var (waterBody, bodyArea) = LabelWaterBodies(drainage.LandMask, pw, ph);
        long minOutletArea = (long)Math.Max(1.0, cfg.SeaZonePixels * cfg.MinOutletSeaZones);
        float minOutletFlow = (float)cfg.RiverTraceMinFlow;

        var outlets = new List<(int Cell, float Flow)>();
        int rejected = 0;

        for (int y = 1; y < ph - 1; y++)
        {
            for (int x = 1; x < pw - 1; x++)
            {
                int c = y * pw + x;
                if (drainage.LandMask[c] == 0) continue;

                int into = drainage.Receiver[c];
                if (drainage.LandMask[into] != 0 || drainage.Flow[c] < minOutletFlow) continue;

                int body = waterBody[into];
                if (body < 0 || bodyArea[body] < minOutletArea)
                {
                    rejected++;
                    continue;
                }

                outlets.Add((c, drainage.Flow[c]));
            }
        }

        Console.WriteLine($"  major rivers: {outlets.Count} sea outlets over {bodyArea.Count} water " +
                          $"bodies, {rejected} rejected as inland sinks under {minOutletArea:N0} px");

        return outlets;
    }

    private static (int[] Body, List<int> Area) LabelWaterBodies(byte[] landMask, int width, int height)
    {
        int n = width * height;
        var body = new int[n];
        Array.Fill(body, -1);

        var area = new List<int>();
        var frontier = new Queue<int>();

        for (int start = 0; start < n; start++)
        {
            if (landMask[start] != 0 || body[start] >= 0) continue;

            int id = area.Count;
            int count = 0;

            body[start] = id;
            frontier.Enqueue(start);

            while (frontier.Count > 0)
            {
                int c = frontier.Dequeue();
                count++;

                int cx = c % width, cy = c / width;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx8[k], ny = cy + Dy8[k];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                    int nb = ny * width + nx;
                    if (landMask[nb] != 0 || body[nb] >= 0) continue;

                    body[nb] = id;
                    frontier.Enqueue(nb);
                }
            }

            area.Add(count);
        }

        return (body, area);
    }

    private static readonly int[] Dx8 = [-1, 0, 1, -1, 1, -1, 0, 1];
    private static readonly int[] Dy8 = [-1, -1, -1, 0, 0, 1, 1, 1];

    private const double NavigableRadius = 7.0;

    private static void CarveHeightmapChannels(
            float[] fullElev,
            int fullWidth,
            int fullHeight,
            List<MajorRiverPath> paths,
            MapConfig cfg)
    {
        float sea = cfg.Limits.SeaLevelUpper;
        float carvedBedElevation = cfg.SeaFloorElevation;

        float scaleX = (float)fullWidth / cfg.ProvinceWidth;
        float scaleY = (float)fullHeight / cfg.ProvinceHeight;

        double minWidthFull = Math.Max(NavigableRadius, cfg.Scaled(cfg.RiverChannelRadiusMin));
        double maxWidthFull = Math.Max(16.0, cfg.Scaled(cfg.RiverChannelRadiusMax));

        if (maxWidthFull < minWidthFull) maxWidthFull = minWidthFull;

        float valleyReach = (float)Math.Max(1.0, cfg.RiverValleyReach);
        float bankElevation = sea + 5.0f;

        double variation = Math.Clamp(cfg.RiverWidthVariation, 0.0, 0.95);
        double variationScale = Math.Max(1.0, cfg.Scaled(cfg.RiverWidthVariationScale));
        var wobbleField = new SimplexNoise(new Rng(cfg.Seed ^ 0x81DE));

        for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var path = paths[pathIndex];
            var pts = path.Points;
            int count = pts.Count;
            if (count < 2) continue;

            double lane = pathIndex * 37.7;
            double arc = 0;

            var radChannel = new float[count];
            var radValley = new float[count];
            var bedElev = new float[count];
            var hx = new float[count];
            var hy = new float[count];

            for (int i = 0; i < count; i++)
            {
                hx[i] = pts[i].X * scaleX;
                hy[i] = pts[i].Y * scaleY;

                if (i > 0)
                {
                    float ax = pts[i].X - pts[i - 1].X;
                    float ay = pts[i].Y - pts[i - 1].Y;
                    arc += MathF.Sqrt(ax * ax + ay * ay);
                }

                float t = (float)i / (count - 1);

                // Smooth cubic taper: 0 at vertex 0, opening over first 20%
                float taper = t < 0.20f ? (t / 0.20f) * (t / 0.20f) * (3f - 2f * (t / 0.20f)) : 1.0f;

                double radius = minWidthFull + (maxWidthFull - minWidthFull) * Math.Pow(t, 0.65);

                if (variation > 0)
                {
                    double wobble = 1.0 + variation * wobbleField.Noise2D(arc / variationScale, lane);
                    radius = Math.Max(NavigableRadius,
                        radius * Math.Clamp(wobble, 1.0 - variation, 1.0 + variation));
                }

                radChannel[i] = (float)radius * taper;
                radValley[i] = radChannel[i] * valleyReach;
                bedElev[i] = bankElevation + (carvedBedElevation - bankElevation) * taper;
            }

            for (int i = 0; i < count - 1; i++)
            {
                float ax = hx[i], ay = hy[i];
                float bx = hx[i + 1], by = hy[i + 1];

                float rChanA = radChannel[i], rChanB = radChannel[i + 1];
                float rValA = radValley[i], rValB = radValley[i + 1];
                float bedA = bedElev[i], bedB = bedElev[i + 1];

                float maxR = Math.Max(rValA, rValB);
                if (maxR < 0.5f) continue;

                int minX = Math.Clamp((int)(Math.Min(ax, bx) - maxR - 2), 0, fullWidth - 1);
                int maxX = Math.Clamp((int)(Math.Max(ax, bx) + maxR + 2), 0, fullWidth - 1);
                int minY = Math.Clamp((int)(Math.Min(ay, by) - maxR - 2), 0, fullHeight - 1);
                int maxY = Math.Clamp((int)(Math.Max(ay, by) + maxR + 2), 0, fullHeight - 1);

                float segDx = bx - ax;
                float segDy = by - ay;
                float segLenSq = segDx * segDx + segDy * segDy;
                if (segLenSq < 1e-4f) continue;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float px = x - ax;
                        float py = y - ay;
                        float u = Math.Clamp((px * segDx + py * segDy) / segLenSq, 0.0f, 1.0f);

                        if (i == 0 && u <= 0.0f) continue;

                        float qx = ax + u * segDx;
                        float qy = ay + u * segDy;

                        float dx = x - qx;
                        float dy = y - qy;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);

                        float curChanR = rChanA + u * (rChanB - rChanA);
                        float curValR = rValA + u * (rValB - rValA);
                        float curBed = bedA + u * (bedB - bedA);

                        if (dist > curValR || curValR < 0.5f) continue;

                        int idx = y * fullWidth + x;
                        float original = fullElev[idx];

                        // Inside CarveHeightmapChannels in MajorRivers.cs:

                        float waterClearanceDepth = sea - 3.5f; // Ensures water is deep enough to render blue/opaque
                        float bankLipHeight = sea + 3.0f;       // Solid dry ground above mud decals

                        if (dist <= curChanR && curChanR > 0.5f)
                        {
                            float norm = dist / curChanR; // 0.0 at center, 1.0 at bank edge

                            float targetHeight;
                            if (norm < 0.85f)
                            {
                                // 1. Riverbed floor: deep and navigable (smooth parabolic bed)
                                float bedNorm = norm / 0.85f;
                                targetHeight = curBed + (waterClearanceDepth - curBed) * (bedNorm * bedNorm);
                            }
                            else
                            {
                                // 2. Steep Bank: climbs sharply from below water level to dry bank lip in the outer 15%
                                float bankT = (norm - 0.85f) / 0.15f;
                                // Hermite smoothstep for a crisp, steep cut
                                float smoothBank = bankT * bankT * (3.0f - 2.0f * bankT);
                                targetHeight = waterClearanceDepth + (bankLipHeight - waterClearanceDepth) * smoothBank;
                            }

                            if (targetHeight < original)
                            {
                                fullElev[idx] = targetHeight;
                            }
                        }
                        else if (curValR > curChanR)
                        {
                            // 3. Valley Shoulder: smoothly rises from the dry bank lip into surrounding hills
                            float valleyT = (dist - curChanR) / (curValR - curChanR);
                            float smoothValley = (1.0f - MathF.Cos(valleyT * MathF.PI)) * 0.5f;
                            float targetHeight = bankLipHeight + (original - bankLipHeight) * smoothValley;

                            if (targetHeight < original)
                            {
                                fullElev[idx] = targetHeight;
                            }
                        }
                    }
                }
            }
        }
    }
}