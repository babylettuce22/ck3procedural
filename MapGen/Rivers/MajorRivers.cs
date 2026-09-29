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

/// <summary>Why an upstream trace ended where it did — reported per run in the build log.</summary>
public enum TraceStop
{
    /// <summary>Discharge fell under <see cref="MapConfig.RiverTraceMinFlow"/>.</summary>
    FlowFloor,
    /// <summary>The filled surface climbed past <see cref="MapConfig.RiverMaxRiseAboveSea"/>.</summary>
    HeightCeiling,
    /// <summary>Ended for one of the other reasons while inside a filled bowl that held no lake, so
    /// the bowl was given back and the course ends on its near rim.</summary>
    DryBowl,
    /// <summary>The only feeder big enough was already another course's.</summary>
    Occupied,
    /// <summary>No cell drains into this one: the drainage's own source.</summary>
    Source,
    /// <summary>Hit the trace's cell cap.</summary>
    Length,
}

public static class MajorRivers
{
    /// <summary>
    /// The counties a major river runs through or past — the ground a flood would take.
    ///
    /// A course is a list of map-pixel points down the middle of the channel, and the channel
    /// itself is water, so the land at risk is whatever sits within a few pixels of those points.
    /// Each sample resolves through the same <c>order[Label[pixel]]</c> lookup the partition uses,
    /// keeps only playable land baronies, and lifts them to their counties. No horizontal wrap,
    /// matching <see cref="Titles.BuildAdjacency"/>.
    ///
    /// Read by the natural-disaster placement in <c>Emit/CompatibilityWriter.cs</c>, which seeds
    /// vanilla's river-flood regions here in preference to bare terrain. Floodplain terrain is a
    /// fair proxy for a river basin, but an actual riverbank is the thing itself — and a generated
    /// map may have no floodplains at all, which is exactly when the proxy stops meaning anything.
    /// </summary>
    public static HashSet<Title> RiversideCounties(List<MajorRiverPath> rivers, ProvinceMap map,
        int[] order, int baronyCount, List<Title> counties, int radius)
    {
        var riverside = new HashSet<Title>();
        if (rivers.Count == 0 || radius < 1) return riverside;

        var countyOf = new Dictionary<int, Title>();
        foreach (var county in counties)
            foreach (var barony in county.Children)
                if (barony.ProvinceId >= 1 && barony.ProvinceId <= baronyCount)
                    countyOf[barony.ProvinceId] = county;

        int w = map.Width, h = map.Height;
        foreach (var river in rivers)
        {
            foreach (var (px, py) in river.Points)
            {
                int cx = (int)px, cy = (int)py;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= h) continue;
                    int row = y * w;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= w) continue;
                        int cell = map.Label[row + x];
                        int id = order[cell];
                        if (id < 1 || id > baronyCount || !map.Seeds[cell].IsLand) continue;
                        if (countyOf.TryGetValue(id, out var county)) riverside.Add(county);
                    }
                }
            }
        }
        return riverside;
    }

    public static List<MajorRiverPath> ExtractAndCarve(
        float[] fullElev,
        int fullWidth,
        int fullHeight,
        Drainage drainage,
        MapConfig cfg)
    {
        if (!cfg.EnableMajorRivers || cfg.MajorRiverDensity <= 0)
            return [];

        int pw = cfg.ProvinceWidth;
        int ph = cfg.ProvinceHeight;

        // 1. Find sea outlets and rank by catchment flow.
        var candidateOutlets = FindSeaOutlets(drainage, cfg, pw, ph);
        candidateOutlets.Sort((a, b) => b.Flow.CompareTo(a.Flow));

        var paths = new List<MajorRiverPath>();
        var occupied = new bool[pw * ph];
        var mouths = new List<(int X, int Y)>();   // where a course reaches the sea or a sea-sized lake
        int systems = 0, lakeCrossings = 0;

        // The length budget: MajorRiverDensity units of course per thousand square units of land.
        // Province pixels are world units, and the courses are resampled a unit apart, so a path's
        // point count is its length.
        long landCells = 0;
        for (int i = 0; i < pw * ph; i++) if (drainage.IsLand(i)) landCells++;
        double budget = cfg.MajorRiverDensity * landCells / 1000.0;
        double traced = 0, tracedFromLakes = 0;
        var stops = new int[Enum.GetValues<TraceStop>().Length];

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

        // Lakes first, and whatever the budget says — their length counts against it, but they are
        // carved even past it. A lake's outlet is not something to be
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

            var rawCells = TraceUpstream(exit, drainage, feeders, occupied, cfg, out var stop);
            rawCells.Reverse(); // Source -> lake exit
            rawCells.AddRange(TraceDownstream(drainage.Receiver[exit], drainage, occupied));

            if (AddCourses(rawCells)) { systems++; lakeSystems++; stops[(int)stop]++; }
        }
        tracedFromLakes = traced;

        // Then the sea outlets, biggest first, until the courses add up to the budget. The river
        // that crosses the line is kept whole.
        foreach (var (outlet, flow) in candidateOutlets)
        {
            if (traced >= budget) break;
            if (occupied[outlet]) continue;

            var rawCells = TraceUpstream(outlet, drainage, feeders, occupied, cfg, out var stop);
            if (rawCells.Count < minLength) continue;

            rawCells.Reverse(); // Source -> mouth
            if (AddCourses(rawCells)) { systems++; stops[(int)stop]++; }
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
                        int last = rawCells[to];
                        if (drainage.IsLand(last) && !drainage.IsLand(drainage.Receiver[last]))
                            mouths.Add((last % pw, last / pw));

                        paths.Add(new MajorRiverPath
                        {
                            Points = smoothedPoints,
                            TotalLength = smoothedPoints.Count,
                            SourceIsWater = fromWater,
                        });
                        traced += smoothedPoints.Count - 1;
                        added++;
                        if (fromWater) lakeCrossings++;
                    }
                }

                start = end;
            }

            return added > 0;
        }

        // 2. Carve channels aggressively with sheer vertical drops to black (carvedBedElevation)
        CarveHeightmapChannels(fullElev, fullWidth, fullHeight, paths, drainage, cfg);

        Console.WriteLine($"  major rivers: {systems} system(s) ({lakeSystems} from lakes) traced into {paths.Count} course(s), " +
                          $"{lakeCrossings} of them flowing out of a lake; spline-smoothed and carved");
        Console.WriteLine($"  major rivers: {traced:N0} u of course against a budget of {budget:N0} " +
                          $"({cfg.MajorRiverDensity:0.##} per 1000 u² of {landCells:N0} land) — " +
                          $"{tracedFromLakes:N0} of it lake outlets, achieved density {1000.0 * traced / Math.Max(1, landCells):0.00}" +
                          (traced < budget ? $"; ran out of outlets at {100.0 * traced / Math.Max(1, budget):F0}% of the budget" : ""));
        Console.WriteLine("  major rivers: heads stopped by " +
                          string.Join(", ", Enum.GetValues<TraceStop>().Select(s => $"{s} {stops[(int)s]}")));

        // Nothing keeps two outlets apart, so two mouths can land a few units from each other and
        // leave a sliver of land between their river provinces. Reported, not acted on.
        if (mouths.Count >= 2)
        {
            double closest = double.MaxValue;
            (int X, int Y) ca = default, cb = default;
            int under10 = 0, under20 = 0;
            for (int a = 0; a < mouths.Count; a++)
            {
                for (int b = a + 1; b < mouths.Count; b++)
                {
                    double d = Math.Sqrt(Math.Pow(mouths[a].X - mouths[b].X, 2) + Math.Pow(mouths[a].Y - mouths[b].Y, 2));
                    if (d < 10) under10++;
                    if (d < 20) under20++;
                    if (d < closest) { closest = d; ca = mouths[a]; cb = mouths[b]; }
                }
            }
            Console.WriteLine($"  major rivers: {mouths.Count} mouths, closest pair {closest:F1} u apart " +
                              $"at ({ca.X},{ca.Y}) and ({cb.X},{cb.Y}); {under10} pair(s) under 10 u, {under20} under 20 u");
        }
        return paths;
    }

    /// <summary>
    /// Walks up the strongest feeder from a sea outlet, or from the cell a lake drains through,
    /// and returns the cells, mouth first.
    ///
    /// Two stops. The trace stops where the filled surface climbs past the configured rise above
    /// sea, and where the discharge falls under the major-river floor. A filled depression on the
    /// way is crossed: kept if the trace reaches water in it (a lake basin) or climbs out the far
    /// side (a dry bowl), and given back only if the trace ends inside it, so the course then ends
    /// on the near rim rather than in the bottom of a pit.
    ///
    /// Climbing out of a dry bowl used to *end* the trace, on the reasoning that a bowl is no place
    /// to trench a navigable river. But the channel is carved to sea level everywhere, the height
    /// ceiling already bounds any rim it must cut, and a bowl is any fill over 2 elevation units —
    /// 0.2 world units, so every noise pit. Measured 2026-09-26 on an 8192 inland-sea world, that
    /// rule ended 76 of 103 rivers mid-trunk with discharge still in the hundreds of thousands:
    /// rivers stopping a short way from the far coast, and a length budget spent on coastal stubs.
    /// Crossing bowls gave 13 whole rivers for the same length. The cost is that a course over a
    /// filled flat follows the flat's drainage routing, which runs in straight lines.
    /// </summary>
    private static List<int> TraceUpstream(
        int outlet,
        Drainage drainage,
        List<int>[] feeders,
        bool[] occupied,
        MapConfig cfg,
        out TraceStop stop)
    {
        stop = TraceStop.Length;
        var cells = new List<int>();
        int curr = outlet;
        int committed = 0;
        bool leavingLake = false;   // on land inside the basin of a lake just walked out of

        float sea = cfg.Limits.SeaLevelUpper;
        // Stop major river before it cuts into high mountains
        float maxMajorRiverElevation = sea + (float)cfg.RiverMaxRiseAboveSea;
        float minTraceFlow = (float)cfg.RiverTraceMinFlow;

        while (curr >= 0 && cells.Count < 4000)
        {
            cells.Add(curr);
            occupied[curr] = true;

            // 1. Where the course may end: water, drained ground (<= 2.0m of fill tolerated), or
            //    the basin of a lake it has just left. Inside a bowl entered from dry ground nothing
            //    is committed, so a trace that ends in there gives the bowl back; one that reaches
            //    water or climbs out the far side commits all of it.
            if (!drainage.IsLand(curr))
            {
                committed = cells.Count;
                leavingLake = true;
            }
            else if (drainage.LakeDepth(curr) <= 2.0f)
            {
                committed = cells.Count;
                leavingLake = false;
            }
            else if (leavingLake)
            {
                committed = cells.Count;
            }

            // 2. Stop if elevation climbs into the mountain foothills
            if (drainage.Filled[curr] > maxMajorRiverElevation) { stop = TraceStop.HeightCeiling; break; }

            var upstream = feeders[curr];
            if (upstream == null || upstream.Count == 0) { stop = TraceStop.Source; break; }

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
            {
                stop = bestFeeder < 0 && upstream.Any(f => occupied[f] && drainage.Flow[f] >= minTraceFlow)
                    ? TraceStop.Occupied : TraceStop.FlowFloor;
                break;
            }

            curr = bestFeeder;
        }

        // Give back the dry bowl the trace wandered into without finding a lake in it.
        if (committed < cells.Count) stop = TraceStop.DryBowl;
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

    /// <summary>
    /// The narrowest a navigable channel is carved, bank to bank, in world units. Province growth
    /// will not cut a corner unless both orthogonal neighbours are water, so a channel has to stay
    /// 4-connected across its diagonal reaches or the river province chain splits; five units does
    /// that with room left for the width variation. It replaced a radius floor of 7 heightmap pixels
    /// that dated from the 2:1 province downsample and, at 1:1, made every river at least 14 wide.
    /// </summary>
    private const double NavigableWidth = 5.0;

    /// <summary>
    /// Carves every major river into the heightmap: a channel at sea level with a sheer bank, a
    /// flood plain either side, and valley walls rising back to the land.
    ///
    /// <b>Width follows discharge.</b> Each point takes the drainage flow under it, held to a
    /// running maximum so a river never narrows downstream, and the channel is placed between
    /// <see cref="MapConfig.RiverChannelWidthMin"/> and <see cref="MapConfig.RiverChannelWidthMax"/>
    /// on a log scale from the trace floor (<see cref="MapConfig.RiverTraceMinFlow"/>) to the
    /// largest mouth on the map. So the map's biggest river is the widest, a short coastal river
    /// stays narrow its whole length, and a trunk steps wider where tributaries come in — where
    /// it used to open on one fixed curve from source to mouth whatever it carried.
    ///
    /// <b>The valley spends height over distance.</b> A major river runs at sea level the whole
    /// way, so all the height of the land beside it has to be lost between the bank and the valley
    /// rim. The old ramp lost it over a fixed multiple of the channel, which on high ground made a
    /// trench. Here the land beside the flood plain is *sunk* by the depth the bank needs, fading
    /// to nothing over a distance chosen so the added slope stays under
    /// <see cref="MapConfig.RiverValleyWallSlope"/>, up to <see cref="MapConfig.RiverValleyMaxReach"/>
    /// — past which it steepens instead. Sinking rather than replacing keeps every hill its own
    /// shape: a slope-limited ceiling was tried first and planed flat facets onto any hill in
    /// reach. Most reaches need no sinking at all, because the course already follows the valley
    /// floor; measured 2026-09-26, the median reach sank 0 and p90 sank 9 elevation units, and
    /// all of the few that steepened were sea-traced rivers climbing towards
    /// <see cref="MapConfig.RiverMaxRiseAboveSea"/>, none of them lake outlets.
    /// </summary>
    private static void CarveHeightmapChannels(
            float[] fullElev,
            int fullWidth,
            int fullHeight,
            List<MajorRiverPath> paths,
            Drainage drainage,
            MapConfig cfg)
    {
        float sea = cfg.Limits.SeaLevelUpper;
        // Pure deep bed elevation (drops straight to 0 / black in the heightmap)
        float carvedBedElevation = cfg.SeaFloorElevation;

        int pw = cfg.ProvinceWidth, ph = cfg.ProvinceHeight;
        float scaleX = (float)fullWidth / pw;
        float scaleY = (float)fullHeight / ph;

        // The widths are bank-to-bank in world units; the carve works in heightmap pixels from the
        // centreline, so halve and multiply by the heightmap pixels behind each world unit. Not
        // MapScale — see RiverChannelWidthMin.
        double floorRadius = NavigableWidth * 0.5 * scaleX;
        double minWidthFull = Math.Max(NavigableWidth, cfg.RiverChannelWidthMin) * 0.5 * scaleX;
        double maxWidthFull = Math.Max(minWidthFull, cfg.RiverChannelWidthMax * 0.5 * scaleX);

        float bankElevation = sea + 3.0f; // Firm low bank line

        // The valley, in heightmap pixels and elevation per heightmap pixel. The flood plain is a
        // multiple of channel width, so of twice the radius; it rises a little across its width so
        // it does not read as a terrace, then the wall eases in over a few units rather than
        // starting on a crease.
        float floodplainPerRadius = (float)Math.Max(0.0, cfg.RiverFloodplainWidth) * 2f;
        float wallSlope = (float)(Math.Max(0.05, cfg.RiverValleyWallSlope) * cfg.ReliefScale) / scaleX;
        float maxReach = (float)Math.Max(1.0, cfg.RiverValleyMaxReach) * scaleX;
        float ease = 4f * scaleX;
        float plainRise = wallSlope * 0.08f;

        double variation = Math.Clamp(cfg.RiverWidthVariation, 0.0, 0.95);
        double variationScale = Math.Max(1.0, cfg.Scaled(cfg.RiverWidthVariationScale));
        var wobbleField = new SimplexNoise(new Rng(cfg.Seed ^ 0x81DE));

        // Discharge along each course. The 3x3 max tolerates the spline wandering a cell off the
        // drainage path it was smoothed from; the running max keeps the width from ever falling
        // downstream.
        var flow = new float[paths.Count][];
        float topFlow = 1f;
        for (int p = 0; p < paths.Count; p++)
        {
            var pts = paths[p].Points;
            var f = new float[pts.Count];
            float running = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                int cx = Math.Clamp((int)MathF.Round(pts[i].X), 0, pw - 1);
                int cy = Math.Clamp((int)MathF.Round(pts[i].Y), 0, ph - 1);
                float here = 0f;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= pw || ny >= ph) continue;
                        here = Math.Max(here, drainage.Flow[ny * pw + nx]);
                    }
                }
                running = Math.Max(running, here);
                f[i] = running;
            }
            flow[p] = f;
            topFlow = Math.Max(topFlow, running);
        }

        double logLow = Math.Log(Math.Max(1.0, cfg.RiverTraceMinFlow));
        double logSpan = Math.Max(1e-6, Math.Log(topFlow) - logLow);

        var widths = new List<float>();
        var drops = new List<float>();
        var reaches = new List<float>();
        int steepened = 0, steepenedFromLakes = 0;

        // Every course is measured against the land as it was before any carving, so the order the
        // rivers are carved in cannot change how steep another's valley is.
        var courses = new (float[] Hx, float[] Hy, float[] Chan, float[] Taper, float[] Plain,
            float[] Sink, float[] Reach, bool TaperHead)[paths.Count];

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
            var tapers = new float[count];
            var plains = new float[count];
            var slopes = new float[count];
            var reachAt = new float[count];
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

                double carried = Math.Clamp((Math.Log(Math.Max(1f, flow[pathIndex][i])) - logLow) / logSpan, 0.0, 1.0);
                double radius = minWidthFull + (maxWidthFull - minWidthFull) * carried;

                if (variation > 0)
                {
                    double wobble = 1.0 + variation * wobbleField.Noise2D(arc / variationScale, lane);
                    radius = Math.Max(floorRadius,
                        radius * Math.Clamp(wobble, 1.0 - variation, 1.0 + variation));
                }

                float r = (float)radius * taper;
                float plain = floodplainPerRadius * r;

                radChannel[i] = r;
                tapers[i] = taper;
                plains[i] = plain;

                // How far the land just past the flood plain stands above it: the depth this
                // reach of valley has to sink.
                slopes[i] = Math.Max(0f, BankHeight(fullElev, fullWidth, fullHeight, hx[i], hy[i], r + plain + ease)
                                         - (bankElevation + plainRise * plain));
            }

            // The sink is smoothed along the course so one high spoke does not dent the valley
            // wall at a single point, then turned into the distance the wall needs to spend it at
            // no more than the wall slope — capped by the reach, which steepens it instead.
            var sink = Smooth(slopes, 12);
            for (int i = 0; i < count; i++)
            {
                float room = Math.Max(ease, maxReach - radChannel[i] - plains[i]);
                float wall = Math.Max(ease, 1.5f * sink[i] / wallSlope);
                if (wall > room)
                {
                    wall = room;
                    if (tapers[i] >= 1f)
                    {
                        steepened++;
                        if (path.SourceIsWater) steepenedFromLakes++;
                    }
                }

                slopes[i] = sink[i];                                   // depth to sink
                reachAt[i] = radChannel[i] + plains[i] + wall;         // where the sag ends

                if (tapers[i] >= 1f)
                {
                    widths.Add(2f * radChannel[i] / scaleX);
                    drops.Add(sink[i]);
                    reaches.Add(reachAt[i] / scaleX);
                }
            }

            courses[pathIndex] = (hx, hy, radChannel, tapers, plains, slopes, reachAt, taperHead);
        }

        // Every segment reads the land as it was, so overlapping segments take the deeper of their
        // two sags rather than sinking the same ground twice.
        var before = (float[])fullElev.Clone();

        for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var (hx, hy, radChannel, tapers, plains, slopes, reachAt, taperHead) = courses[pathIndex];
            if (hx is null) continue;
            int count = hx.Length;

            for (int i = 0; i < count - 1; i++)
            {
                float ax = hx[i], ay = hy[i];
                float bx = hx[i + 1], by = hy[i + 1];

                float rChanA = radChannel[i], rChanB = radChannel[i + 1];

                float maxR = Math.Max(reachAt[i], reachAt[i + 1]);
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
                        float curReach = reachAt[i] + u * (reachAt[i + 1] - reachAt[i]);

                        if (dist > curReach) continue;

                        int idx = y * fullWidth + x;
                        float original = before[idx];

                        // 1. INSIDE WATER CHANNEL: Sheer, sharp vertical drop straight to deep black (no smoothing)
                        if (dist <= curChanR && curChanR > 0.5f)
                        {
                            fullElev[idx] = carvedBedElevation;
                            continue;
                        }

                        // 2. OUTSIDE BANK: a flat flood plain, then the land sunk by the depth
                        // this reach needs, fading to nothing at the reach. Sinking rather than
                        // replacing keeps every hill its own shape; nothing is taken below the
                        // plain's line, so a low bank is never flooded; and it fades in with the
                        // taper so a river rising in the hills does not open at full depth.
                        float weight = tapers[i] + u * (tapers[i + 1] - tapers[i]);
                        if (weight <= 0f) continue;

                        float plain = plains[i] + u * (plains[i + 1] - plains[i]);
                        float depth = slopes[i] + u * (slopes[i + 1] - slopes[i]);
                        float d = Math.Max(0f, dist - curChanR);
                        float floorLine = bankElevation + plainRise * Math.Min(d, plain);

                        float target;
                        if (d <= plain)
                        {
                            target = floorLine;
                        }
                        else
                        {
                            float fade = 1f - (d - plain) / Math.Max(1f, curReach - curChanR - plain);
                            fade = Math.Clamp(fade, 0f, 1f);
                            fade = fade * fade * (3f - 2f * fade);
                            target = Math.Max(original - depth * fade, floorLine);
                        }

                        if (target < original)
                        {
                            target = original + (target - original) * weight;
                            if (target < fullElev[idx]) fullElev[idx] = target;
                        }
                    }
                }
            }
        }

        if (widths.Count > 0)
        {
            Console.WriteLine($"  major river valleys: channel {Pct(widths, 0.1):F1}/{Pct(widths, 0.5):F1}/{Pct(widths, 0.9):F1} u wide (p10/50/90), " +
                              $"valley sunk {Pct(drops, 0.5):F0}/{Pct(drops, 0.9):F0}/{Pct(drops, 1.0):F0} elevation (p50/90/max), " +
                              $"reaching {Pct(reaches, 0.5):F0}/{Pct(reaches, 0.9):F0} u, " +
                              $"{100.0 * steepened / widths.Count:F1}% of reaches steeper than the wall slope to fit " +
                              $"({steepenedFromLakes} of those {steepened} on courses leaving a lake)");
        }

        static float Pct(List<float> values, double q)
        {
            var sorted = values.ToArray();
            Array.Sort(sorted);
            return sorted[(int)Math.Clamp(Math.Round(q * (sorted.Length - 1)), 0, sorted.Length - 1)];
        }
    }

    /// <summary>
    /// The height of the land on a ring of <paramref name="radius"/> around a course point: the
    /// upper quartile of sixteen spokes. The spokes running along the course land on the valley
    /// floor and the ones across it on the banks, and the quartile takes the higher bank rather
    /// than the average of the two — the low one is protected by the floor line instead.
    /// </summary>
    private static float BankHeight(float[] elevation, int width, int height, float cx, float cy, float radius)
    {
        const int Spokes = 16;
        Span<float> ring = stackalloc float[Spokes];
        int n = 0;

        for (int k = 0; k < Spokes; k++)
        {
            double angle = k * (2 * Math.PI / Spokes);
            int x = (int)(cx + radius * Math.Cos(angle));
            int y = (int)(cy + radius * Math.Sin(angle));
            if (x < 0 || y < 0 || x >= width || y >= height) continue;
            ring[n++] = elevation[(long)y * width + x];
        }

        if (n == 0) return 0f;
        var taken = ring[..n];
        taken.Sort();
        return taken[(int)(0.75f * (n - 1))];
    }

    /// <summary>A centred moving average of <paramref name="halfWidth"/> points either side.</summary>
    private static float[] Smooth(float[] values, int halfWidth)
    {
        var result = new float[values.Length];
        double sum = 0;
        int lo = 0, hi = -1;

        for (int i = 0; i < values.Length; i++)
        {
            int wantLo = Math.Max(0, i - halfWidth), wantHi = Math.Min(values.Length - 1, i + halfWidth);
            while (hi < wantHi) sum += values[++hi];
            while (lo < wantLo) sum -= values[lo++];
            result[i] = (float)(sum / (hi - lo + 1));
        }

        return result;
    }
}