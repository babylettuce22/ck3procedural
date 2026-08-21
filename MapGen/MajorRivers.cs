using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public sealed class MajorRiverPath
{
    public required List<(float X, float Y)> Points { get; init; }
    public required float TotalLength { get; init; }

    /// <summary>
    /// True when the course begins in a lake rather than on dry ground. A river rising in the
    /// hills tapers to nothing at its head; a lake's outlet is full width from the first metre,
    /// and the channel has to be carved that way or the water in the lake and the water in the
    /// river never meet. Read by the carve, which skips the taper, and by the province seeding,
    /// which otherwise leaves the first fifth of a course unseeded as "the dry tip".
    /// </summary>
    public bool SourceIsWater { get; init; }
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

        // 1. Find sea outlets and rank by catchment flow.
        var candidateOutlets = FindSeaOutlets(drainage, cfg, pw, ph);
        candidateOutlets.Sort((a, b) => b.Flow.CompareTo(a.Flow));

        var paths = new List<MajorRiverPath>();
        var occupied = new bool[pw * ph];
        int targetRivers = cfg.MajorRiverCount;
        int systems = 0, lakeCrossings = 0;

        // Lakes feed as land does: a lake cell's receiver is the next cell towards the spill, so
        // the trace can walk in over the outlet, across the water and out again up the strongest
        // inflow, which is what makes one river of a chain of lakes.
        var feeders = new List<int>[pw * ph];
        for (int i = 0; i < drainage.Receiver.Length; i++)
        {
            int into = drainage.Receiver[i];
            if (into != i && drainage.Drains(i))
            {
                (feeders[into] ??= []).Add(i);
            }
        }

        int minLength = (int)Math.Max(15, cfg.Scaled(30));
        int lakeSystems = 0;

        // Lakes first, and over and above the count. A lake's outlet is not something to be
        // chosen by discharge against the other rivers on the map: the lake is there, the water in
        // it has to get to the sea, and the course from the spill downhill always exists — it is
        // walked downstream along the receivers, so unlike a trace up from the sea it cannot be
        // stopped by a dry bowl in between. Upstream of the lake the usual trace runs, in over the
        // spill, across the water and up the strongest inflow, so a chain of lakes becomes one
        // system. Going by falling discharge means the lower lake of a chain is traced first and
        // the upper one found already occupied.
        foreach (var (exit, flow) in FindLakeExits(drainage, cfg))
        {
            if (occupied[exit]) continue;

            var rawCells = TraceUpstream(exit, drainage, feeders, pw, ph, occupied, cfg);
            rawCells.Reverse(); // Source -> lake exit
            rawCells.AddRange(TraceDownstream(drainage.Receiver[exit], drainage, occupied));

            if (AddCourses(rawCells)) { systems++; lakeSystems++; }
        }

        foreach (var (outlet, flow) in candidateOutlets)
        {
            if (systems - lakeSystems >= targetRivers) break;
            if (occupied[outlet]) continue;

            var rawCells = TraceUpstream(outlet, drainage, feeders, pw, ph, occupied, cfg);
            if (rawCells.Count < minLength) continue;

            rawCells.Reverse(); // Source -> mouth
            if (AddCourses(rawCells)) systems++;
        }

        // One trace, several courses: the water between the inflow of a lake and its outlet is
        // the lake's own, not a channel to carve or a corridor to seed, so the course is cut
        // there and each dry stretch becomes a river of its own. Each keeps one wet cell at
        // either end it touches water, so the carve reaches into the lake it leaves or enters
        // rather than stopping on the shore.
        bool AddCourses(List<int> rawCells)
        {
            int added = 0;
            for (int start = 0; start < rawCells.Count;)
            {
                if (!drainage.IsLand(rawCells[start])) { start++; continue; }

                int end = start;
                while (end < rawCells.Count && drainage.IsLand(rawCells[end])) end++;

                // Runs are maximal, so whatever precedes this one is water; and every run ends in
                // water — the last at the sea outlet, the others in the lake the trace walked on
                // into.
                bool fromWater = start > 0;
                int from = fromWater ? start - 1 : start;
                int to = Math.Min(rawCells.Count - 1, end);

                var rawPoints = new List<(float X, float Y)>(to - from + 1);
                for (int k = from; k <= to; k++)
                    rawPoints.Add((rawCells[k] % pw, rawCells[k] / pw));

                // A short dry stretch is still worth carving when it joins two waters — that is
                // the connection this is all for — but a short stub at the head of a system is
                // not a river.
                if (rawPoints.Count >= minLength || (fromWater && rawPoints.Count >= 2))
                {
                    // Smooth out 45°/90° raster staircase steps into natural meanders
                    var smoothedPoints = SmoothAndResamplePath(rawPoints, stepSize: 1.0f);

                    if (smoothedPoints.Count >= 2)
                    {
                        paths.Add(new MajorRiverPath
                        {
                            Points = smoothedPoints,
                            TotalLength = smoothedPoints.Count,
                            SourceIsWater = fromWater,
                        });
                        added++;
                        if (fromWater) lakeCrossings++;
                    }
                }

                start = end;
            }

            return added > 0;
        }

        // 2. Carve channels aggressively with sheer vertical drops to black (carvedBedElevation)
        CarveHeightmapChannels(fullElev, fullWidth, fullHeight, paths, cfg);

        Console.WriteLine($"  major rivers: {systems} system(s) ({lakeSystems} from lakes) traced into {paths.Count} course(s), " +
                          $"{lakeCrossings} of them flowing out of a lake; spline-smoothed and carved");
        return paths;
    }

    /// <summary>
    /// Walks up the strongest feeder from a sea outlet, or from the cell a lake drains through,
    /// and returns the cells, mouth first.
    ///
    /// Two stops and one suspension. The trace stops where the filled surface climbs past the
    /// configured rise above sea, and where the discharge falls under the major-river floor. It
    /// is suspended, not stopped, on entering a filled depression: a dry bowl in the heightmap is
    /// no place to trench a navigable river, but a bowl with a lake at the bottom is exactly
    /// where one belongs, and there is no telling the two apart from the rim. So the trace carries
    /// on provisionally: if it reaches water the bowl was a lake basin and every cell of it is
    /// kept, and if it climbs out the far side dry the bowl was a bowl, the trace ends on the near
    /// rim as it always did, and the cells inside are given back. Coming out of a lake the same
    /// basin is crossed in the other direction, and that crossing is always kept — it is the lake's
    /// own shore, and the river upstream of the lake has to climb it to get anywhere.
    /// </summary>
    private static List<int> TraceUpstream(
        int outlet,
        Drainage drainage,
        List<int>[] feeders,
        int width,
        int height,
        bool[] occupied,
        MapConfig cfg)
    {
        var cells = new List<int>();
        int curr = outlet;
        int committed = 0;
        bool leavingLake = false;   // on land inside the basin of a lake just walked out of
        bool inDryDip = false;      // inside a filled bowl entered from dry ground, not yet proven a lake basin

        float sea = cfg.Limits.SeaLevelUpper;
        // Stop major river before it cuts into high mountains
        float maxMajorRiverElevation = sea + (float)cfg.RiverMaxRiseAboveSea;
        float minTraceFlow = (float)cfg.RiverTraceMinFlow;

        while (curr >= 0 && cells.Count < 4000)
        {
            cells.Add(curr);
            occupied[curr] = true;

            // 1. Where the course may end: water, drained ground (<= 2.0m of fill tolerated), or
            //    the basin of a lake it has just left. A bowl entered from dry ground is provisional
            //    until water proves it a lake basin; climbing out of it dry ends the trace.
            if (!drainage.IsLand(curr))
            {
                committed = cells.Count;
                leavingLake = true;
                inDryDip = false;
            }
            else if (drainage.LakeDepth(curr) <= 2.0f)
            {
                if (inDryDip) break;
                committed = cells.Count;
                leavingLake = false;
            }
            else if (leavingLake)
            {
                committed = cells.Count;
            }
            else
            {
                inDryDip = true;
            }

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

        // Give back the dry bowl the trace wandered into without finding a lake in it.
        for (int k = committed; k < cells.Count; k++) occupied[cells[k]] = false;
        cells.RemoveRange(committed, cells.Count - committed);

        return cells;
    }

    /// <summary>
    /// Walks the receivers from a lake's spill down to the sea, or into the first cell some
    /// earlier course already holds, where this one joins it. Always arrives: the flood guarantees
    /// every drained cell a route to the sea, and lakes on the way are drained cells like any
    /// other and are walked straight through.
    /// </summary>
    private static List<int> TraceDownstream(int spill, Drainage drainage, bool[] occupied)
    {
        var cells = new List<int>();
        int curr = spill;

        while (cells.Count < 8000)
        {
            cells.Add(curr);
            if (occupied[curr]) break;          // joined a course already traced
            occupied[curr] = true;

            int into = drainage.Receiver[curr];
            if (into == curr || drainage.IsSea(into)) break;
            curr = into;
        }

        return cells;
    }

    /// <summary>
    /// The cell each qualifying lake drains through — the lake cell whose receiver is land and
    /// which carries the most flow — paired with that flow, largest first. A lake qualifies on
    /// area, against <see cref="MapConfig.LakeOutletMinSeaZones"/>, and on discharge, against the
    /// same floor any major river must clear.
    /// </summary>
    private static List<(int Cell, float Flow)> FindLakeExits(Drainage drainage, MapConfig cfg)
    {
        int bodies = drainage.WaterBodyArea.Length;
        var exit = new int[bodies];
        Array.Fill(exit, -1);

        for (int c = 0; c < drainage.Receiver.Length; c++)
        {
            if (!drainage.IsLake(c)) continue;
            int into = drainage.Receiver[c];
            if (into == c || !drainage.IsLand(into)) continue;

            int b = drainage.WaterBody[c];
            if (exit[b] < 0 || drainage.Flow[c] > drainage.Flow[exit[b]]) exit[b] = c;
        }

        long minArea = (long)Math.Max(1.0, cfg.SeaZonePixels * cfg.LakeOutletMinSeaZones);
        float minFlow = (float)cfg.RiverTraceMinFlow;

        var exits = new List<(int Cell, float Flow)>();
        int lakes = 0;
        for (int b = 0; b < bodies; b++)
        {
            if (drainage.WaterBodyIsSea[b]) continue;
            lakes++;
            if (exit[b] < 0 || drainage.WaterBodyArea[b] < minArea || drainage.Flow[exit[b]] < minFlow) continue;
            exits.Add((exit[b], drainage.Flow[exit[b]]));
        }

        exits.Sort((a, b) => b.Flow.CompareTo(a.Flow));
        Console.WriteLine($"  major rivers: {exits.Count} of {lakes} lake(s) large enough for a carved outlet " +
                          $"(at least {minArea:N0} px and {minFlow:N0} discharge)");
        return exits;
    }

    /// <summary>
    /// Smooths a discrete grid path using Centripetal Catmull-Rom splines (alpha = 0.5)
    /// and resamples the curve at equidistant arc lengths.
    /// </summary>
    private static List<(float X, float Y)> SmoothAndResamplePath(List<(float X, float Y)> raw, float stepSize)
    {
        if (raw.Count < 3) return new List<(float X, float Y)>(raw);

        var cp = new List<(float X, float Y)>(raw.Count + 2);
        cp.Add((2f * raw[0].X - raw[1].X, 2f * raw[0].Y - raw[1].Y));
        cp.AddRange(raw);
        cp.Add((2f * raw[^1].X - raw[^2].X, 2f * raw[^1].Y - raw[^2].Y));

        var denseSpline = new List<(float X, float Y)>();
        const int SubdivisionsPerSegment = 8;

        for (int i = 1; i < cp.Count - 2; i++)
        {
            var p0 = cp[i - 1];
            var p1 = cp[i];
            var p2 = cp[i + 1];
            var p3 = cp[i + 2];

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
        long minOutletArea = (long)Math.Max(1.0, cfg.SeaZonePixels * cfg.MinOutletSeaZones);
        float minOutletFlow = (float)cfg.RiverTraceMinFlow;

        var outlets = new List<(int Cell, float Flow)>();
        int rejected = 0;

        for (int y = 1; y < ph - 1; y++)
        {
            for (int x = 1; x < pw - 1; x++)
            {
                int c = y * pw + x;
                if (!drainage.IsLand(c)) continue;

                int into = drainage.Receiver[c];
                if (drainage.IsLand(into) || drainage.Flow[c] < minOutletFlow) continue;

                // A lake big enough to count as a sea is a mouth in its own right, and the river
                // that reaches it is its own system; a smaller lake is something the trace from
                // the sea passes through on its way upstream, so nothing starts there.
                int body = drainage.WaterBody[into];
                if (body < 0 || drainage.WaterBodyArea[body] < minOutletArea)
                {
                    rejected++;
                    continue;
                }

                outlets.Add((c, drainage.Flow[c]));
            }
        }

        Console.WriteLine($"  major rivers: {outlets.Count} outlets over {drainage.WaterBodyArea.Length} water " +
                          $"bodies, {rejected} rejected as mouths on lakes under {minOutletArea:N0} px");

        return outlets;
    }

    private const double NavigableRadius = 7.0;

    private static void CarveHeightmapChannels(
            float[] fullElev,
            int fullWidth,
            int fullHeight,
            List<MajorRiverPath> paths,
            MapConfig cfg)
    {
        float sea = cfg.Limits.SeaLevelUpper;
        // Pure deep bed elevation (drops straight to 0 / black in the heightmap)
        float carvedBedElevation = cfg.SeaFloorElevation;

        float scaleX = (float)fullWidth / cfg.ProvinceWidth;
        float scaleY = (float)fullHeight / cfg.ProvinceHeight;

        double minWidthFull = Math.Max(NavigableRadius, cfg.Scaled(cfg.RiverChannelRadiusMin));
        double maxWidthFull = Math.Max(16.0, cfg.Scaled(cfg.RiverChannelRadiusMax));

        if (maxWidthFull < minWidthFull) maxWidthFull = minWidthFull;

        float valleyReach = (float)Math.Max(1.0, cfg.RiverValleyReach);
        float bankElevation = sea + 3.0f; // Firm low bank line

        double variation = Math.Clamp(cfg.RiverWidthVariation, 0.0, 0.95);
        double variationScale = Math.Max(1.0, cfg.Scaled(cfg.RiverWidthVariationScale));
        var wobbleField = new SimplexNoise(new Rng(cfg.Seed ^ 0x81DE));

        for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var path = paths[pathIndex];
            var pts = path.Points;
            int count = pts.Count;
            if (count < 2) continue;

            // A lake outlet leaves the lake already a river; only a course rising on dry ground
            // narrows to nothing at its head.
            bool taperHead = !path.SourceIsWater;

            double lane = pathIndex * 37.7;
            double arc = 0;

            var radChannel = new float[count];
            var radValley = new float[count];
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

                // Smooth cubic taper: 0 at vertex 0, opening over first 15%
                float taper = taperHead && t < 0.15f ? (t / 0.15f) * (t / 0.15f) * (3f - 2f * (t / 0.15f)) : 1.0f;

                double radius = minWidthFull + (maxWidthFull - minWidthFull) * Math.Pow(t, 0.65);

                if (variation > 0)
                {
                    double wobble = 1.0 + variation * wobbleField.Noise2D(arc / variationScale, lane);
                    radius = Math.Max(NavigableRadius,
                        radius * Math.Clamp(wobble, 1.0 - variation, 1.0 + variation));
                }

                radChannel[i] = (float)radius * taper;
                radValley[i] = radChannel[i] * valleyReach;
            }

            for (int i = 0; i < count - 1; i++)
            {
                float ax = hx[i], ay = hy[i];
                float bx = hx[i + 1], by = hy[i + 1];

                float rChanA = radChannel[i], rChanB = radChannel[i + 1];
                float rValA = radValley[i], rValB = radValley[i + 1];

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

                        if (i == 0 && u <= 0.0f && taperHead) continue;

                        float qx = ax + u * segDx;
                        float qy = ay + u * segDy;

                        float dx = x - qx;
                        float dy = y - qy;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);

                        float curChanR = rChanA + u * (rChanB - rChanA);
                        float curValR = rValA + u * (rValB - rValA);

                        if (dist > curValR || curValR < 0.5f) continue;

                        int idx = y * fullWidth + x;
                        float original = fullElev[idx];

                        // 1. INSIDE WATER CHANNEL: Sheer, sharp vertical drop straight to deep black (no smoothing)
                        if (dist <= curChanR && curChanR > 0.5f)
                        {
                            fullElev[idx] = carvedBedElevation;
                        }
                        // 2. OUTSIDE BANK: Gentle surrounding valley slope on dry land only
                        else if (curValR > curChanR)
                        {
                            float valleyT = (dist - curChanR) / (curValR - curChanR);
                            float smoothValley = (1.0f - MathF.Cos(valleyT * MathF.PI)) * 0.5f;
                            float targetHeight = bankElevation + (original - bankElevation) * smoothValley;

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