using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// CK3 1.21's road geometry, <c>gfx/map/spline_network/spline_network.splnet</c>: the line each road
/// is drawn along. <see cref="RoadWriter"/> writes which roads exist; this writes where they run.
///
/// <para><b>What the game asks for.</b> A road file entry is a chain of province ids. The game draws
/// it one neighbouring pair at a time, looking up a <i>strip</i> between the two provinces'
/// <i>hub anchors</i> ("Could not find spline network strip of type T between hub anchor A and hub
/// anchor B" when one is missing). A land province's hub is <c>province * 100</c>; a water province's,
/// and a port's sea-side twin, is <c>0x800000 | province * 100</c>. A sea road therefore runs port →
/// its sea twin (a type-0 strip) → water → water … → the far port's twin → that port (type 0 again),
/// and every water-to-water step is type 1.</para>
///
/// <para><b>The format</b>, read from CK3 1.21's own exporter (ck3.exe 0x14232dd90 → 0x1439a1e00, with
/// per-item writers 0x1439a67f0 points, 0x1439ae6e0 segments, 0x1439ae8a0 strips) and checked
/// against vanilla's file with <c>ck3devtools\calibration\splnet.py</c>. Paradox binary tokens:
/// <c>0xee</c> = version 4; <c>0x45a</c> = { point, segment, strip counts }; then three lists.
/// <list type="bullet">
/// <item>point <c>{ 0xb = u32 id  0x4c = { x y } }</c>: a hub, or a control point
/// <c>0x10000000 | n</c> along a segment. Positions are world units (<see cref="WorldSpace"/>).</item>
/// <item>segment <c>{ 0xb = u64 index &lt;&lt; 8 | type  0x5f7 = { u32 point ids } }</c>, running from the
/// smaller hub to the larger, as all 9,391 of vanilla's single-segment strips do.</item>
/// <item>strip <c>{ 0xb = u64 (hubA &lt;&lt; 35) | (hubB &lt;&lt; 6) | type  0x5f5 = { u64 segment ids } }</c>,
/// hubA the larger id. The key layout is the lookup's own (ck3.exe 0x14397eca0), and all 9,481 of
/// vanilla's keys decode to two of its points.</item>
/// </list></para>
///
/// <para><b>The line itself.</b> A pixel path between the two hubs that keeps to the pair's own two
/// provinces where it can (land for a land strip, water for a sea strip, other provinces of the same
/// kind at a cost), cut from a window around the pair so it stays cheap. It is then smoothed and
/// resampled at vanilla's spacing: its control points sit a median 6.7 px apart (p90 11.9), and the
/// engine rejects a gap it finds too long. A land road pays heavily for every water pixel, so where it
/// must cross a major river it crosses at the narrows, and <see cref="BridgeWriter"/> then puts the
/// bridge on that line.</para>
/// </summary>
internal static class SplineNetworkWriter
{
    public const int SeaBit = 0x800000;
    private const uint ControlBit = 0x10000000;

    /// <summary>Control-point spacing in pixels: vanilla's median gap.</summary>
    private const double Spacing = 6.7;

    /// <summary>How far past the two hubs the path search may wander, in pixels.</summary>
    private const int Margin = 48;

    private const ushort Eq = 0x0001, Open = 0x0003, Close = 0x0004,
        Int = 0x000c, Float = 0x000d, UInt = 0x0014, ULong = 0x029c;

    private readonly record struct Strip(uint A, uint B, int Type);

    /// <param name="LandLines">Every land strip's drawn line in province-map pixels, hub to hub: what
    /// <see cref="BridgeWriter"/> puts its bridges on.</param>
    public sealed record Stats(int Hubs, int ControlPoints, int Strips, int Straight,
        IReadOnlyList<List<(double X, double Y)>> LandLines);

    /// <summary>
    /// Writes the network for <paramref name="roads"/>: each road's province chain and whether it
    /// is a sea road. With no roads it writes an empty network, which still keeps vanilla's out.
    /// </summary>
    public static Stats Write(string modDir, IReadOnlyList<(List<int> Provinces, bool Sea)> roads,
        ProvinceMap provinces, int[] order, ProvinceAnchor.Anchors anchors)
    {
        int width = provinces.Width, height = provinces.Height;
        var labelOf = new Dictionary<int, int>();
        for (int label = 0; label < order.Length && label < provinces.Count; label++) labelOf[order[label]] = label;
        bool IsWater(int cell) => !provinces.Seeds[provinces.Label[cell]].IsLand;

        // ---- Which strips, hub to hub -------------------------------------------------------
        var strips = new HashSet<Strip>();
        void Add(uint a, uint b, int type)
        {
            if (a == b) return;
            strips.Add(a > b ? new Strip(a, b, type) : new Strip(b, a, type));
        }
        uint LandHub(int p) => (uint)(p * 100);
        uint SeaHub(int p) => (uint)(SeaBit | p * 100);

        foreach (var (chain, sea) in roads)
        {
            if (chain.Count < 2) continue;
            if (!sea)
            {
                for (int i = 0; i + 1 < chain.Count; i++) Add(LandHub(chain[i]), LandHub(chain[i + 1]), 0);
                continue;
            }
            // port, water…, port
            Add(LandHub(chain[0]), SeaHub(chain[0]), 0);
            for (int i = 0; i + 1 < chain.Count; i++) Add(SeaHub(chain[i]), SeaHub(chain[i + 1]), 1);
            Add(SeaHub(chain[^1]), LandHub(chain[^1]), 0);
        }

        // ---- Where each hub stands -----------------------------------------------------------
        var hubPos = new Dictionary<uint, (double X, double Y)>();
        (double X, double Y)? Hub(uint id)
        {
            if (hubPos.TryGetValue(id, out var known)) return known;
            int province = (int)((id & ~(uint)SeaBit) / 100);
            if (!labelOf.TryGetValue(province, out int label)) return null;
            var at = anchors.Holding[label];
            bool waterProvince = !provinces.Seeds[label].IsLand;
            if ((id & SeaBit) != 0 && !waterProvince) at = Offshore(at);   // a port's sea-side twin
            hubPos[id] = at;
            return at;
        }

        // The nearest water pixel to a port's holding, nudged three pixels further out so the twin is
        // off the beach: vanilla's twins sit a median 14 px from the land hub.
        (double X, double Y) Offshore((double X, double Y) from)
        {
            int fx = (int)from.X, fy = (int)from.Y;
            for (int r = 1; r <= 96; r++)
            {
                double best = double.MaxValue; int bx = -1, by = -1;
                for (int y = Math.Max(0, fy - r); y <= Math.Min(height - 1, fy + r); y++)
                    for (int x = Math.Max(0, fx - r); x <= Math.Min(width - 1, fx + r); x++)
                    {
                        if (Math.Max(Math.Abs(x - fx), Math.Abs(y - fy)) != r || !IsWater(y * width + x)) continue;
                        double d = (x - fx) * (x - fx) + (y - fy) * (y - fy);
                        if (d < best) { best = d; bx = x; by = y; }
                    }
                if (bx < 0) continue;
                double len = Math.Sqrt(best), ux = (bx - fx) / len, uy = (by - fy) / len;
                for (int push = 3; push > 0; push--)
                {
                    int px = (int)Math.Round(bx + ux * push), py = (int)Math.Round(by + uy * push);
                    if (px >= 0 && py >= 0 && px < width && py < height && IsWater(py * width + px)) return (px + 0.5, py + 0.5);
                }
                return (bx + 0.5, by + 0.5);
            }
            return from;
        }

        // ---- Lines ---------------------------------------------------------------------------
        var points = new List<(uint Id, double X, double Y)>();
        var pointSeen = new HashSet<uint>();
        var segments = new List<(ulong Id, List<uint> Points)>();
        var stripOut = new List<(ulong Key, ulong Segment)>();
        uint nextControl = 1;
        int straight = 0;
        var landLines = new List<List<(double X, double Y)>>();

        foreach (var s in strips.OrderBy(s => s.A).ThenBy(s => s.B).ThenBy(s => s.Type))
        {
            var a = Hub(s.A);
            var b = Hub(s.B);
            if (a is null || b is null) continue;

            // From the smaller hub to the larger, as vanilla's segments run.
            var from = b.Value; var to = a.Value;
            int pa = labelOf[(int)((s.A & ~(uint)SeaBit) / 100)], pb = labelOf[(int)((s.B & ~(uint)SeaBit) / 100)];
            bool wantWater = s.Type == 1;
            bool twin = pa == pb;   // a port to its own sea-side twin: crosses the shore by design

            var path = FindPath(from, to, pa, pb, wantWater, twin);
            if (path is null) { straight++; path = [from, to]; }
            var line = Resample(Smooth(path), Spacing);
            if (!wantWater && !twin) landLines.Add(line);

            var ids = new List<uint> { s.B };
            foreach (var p in line.Skip(1).SkipLast(1))
            {
                uint id = ControlBit | nextControl++;
                points.Add((id, p.X, p.Y));
                ids.Add(id);
            }
            ids.Add(s.A);

            ulong segmentId = ((ulong)segments.Count << 8) | (uint)s.Type;
            segments.Add((segmentId, ids));
            stripOut.Add(((ulong)s.A << 35 | (ulong)s.B << 6 | (uint)s.Type, segmentId));

            foreach (var (hub, at) in new[] { (s.A, to), (s.B, from) })
                if (pointSeen.Add(hub)) points.Add((hub, at.X, at.Y));
        }

        WriteFile(modDir, points, segments, stripOut, height);
        return new Stats(pointSeen.Count, points.Count - pointSeen.Count, stripOut.Count, straight, landLines);

        // A* over pixels inside a window around the two hubs. Cost 1 on the pair's own provinces
        // in the wanted medium, 6 on other provinces of that medium, 25 across the other medium
        // (a strait, a river mouth) — and a twin strip may cross the shore freely, since that is
        // what it is for.
        List<(double X, double Y)>? FindPath((double X, double Y) start, (double X, double Y) goal,
            int labelA, int labelB, bool water, bool shore)
        {
            int x0 = Math.Max(0, (int)Math.Min(start.X, goal.X) - Margin);
            int y0 = Math.Max(0, (int)Math.Min(start.Y, goal.Y) - Margin);
            int x1 = Math.Min(width - 1, (int)Math.Max(start.X, goal.X) + Margin);
            int y1 = Math.Min(height - 1, (int)Math.Max(start.Y, goal.Y) + Margin);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            int sx = Math.Clamp((int)start.X, x0, x1) - x0, sy = Math.Clamp((int)start.Y, y0, y1) - y0;
            int gx = Math.Clamp((int)goal.X, x0, x1) - x0, gy = Math.Clamp((int)goal.Y, y0, y1) - y0;

            double Step(int lx, int ly)
            {
                int cell = (ly + y0) * width + lx + x0;
                int label = provinces.Label[cell];
                bool cellWater = !provinces.Seeds[label].IsLand;
                if (shore) return label == labelA || label == labelB ? 1 : 6;
                if (cellWater != water) return 25;
                return label == labelA || label == labelB ? 1 : 6;
            }

            var dist = new float[w * h];
            Array.Fill(dist, float.MaxValue);
            var prev = new int[w * h];
            var queue = new PriorityQueue<int, float>();
            int startIx = sy * w + sx, goalIx = gy * w + gx;
            dist[startIx] = 0;
            queue.Enqueue(startIx, 0);
            double Heuristic(int ix) { int dx = Math.Abs(ix % w - gx), dy = Math.Abs(ix / w - gy); return Math.Max(dx, dy) + 0.414 * Math.Min(dx, dy); }

            while (queue.TryDequeue(out int at, out float pri))
            {
                if (at == goalIx) break;
                if (pri - Heuristic(at) > dist[at] + 1e-3) continue;
                int ax = at % w, ay = at / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = ax + dx, ny = ay + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int nIx = ny * w + nx;
                        float nd = dist[at] + (float)(Step(nx, ny) * (dx != 0 && dy != 0 ? 1.4142 : 1.0));
                        if (nd >= dist[nIx]) continue;
                        dist[nIx] = nd;
                        prev[nIx] = at;
                        queue.Enqueue(nIx, nd + (float)Heuristic(nIx));
                    }
            }
            if (dist[goalIx] == float.MaxValue) return null;

            var cells = new List<(double X, double Y)>();
            for (int ix = goalIx; ; ix = prev[ix])
            {
                cells.Add((ix % w + x0 + 0.5, ix / w + y0 + 0.5));
                if (ix == startIx) break;
            }
            cells.Reverse();
            cells[0] = start;
            cells[^1] = goal;
            return cells;
        }
    }

    /// <summary>
    /// Takes the staircase out of a pixel path: keeps every fourth pixel, then two rounds of Chaikin
    /// corner cutting. The ends stay where they are, since they are the hubs.
    /// </summary>
    private static List<(double X, double Y)> Smooth(List<(double X, double Y)> path)
    {
        var line = new List<(double X, double Y)>();
        for (int i = 0; i < path.Count; i += 4) line.Add(path[i]);
        if (line[^1] != path[^1]) line.Add(path[^1]);
        for (int round = 0; round < 2 && line.Count > 2; round++)
        {
            var cut = new List<(double X, double Y)> { line[0] };
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var (p, q) = (line[i], line[i + 1]);
                if (i > 0) cut.Add((0.75 * p.X + 0.25 * q.X, 0.75 * p.Y + 0.25 * q.Y));
                if (i + 2 < line.Count) cut.Add((0.25 * p.X + 0.75 * q.X, 0.25 * p.Y + 0.75 * q.Y));
            }
            cut.Add(line[^1]);
            line = cut;
        }
        return line;
    }

    /// <summary>Evenly spaced points along a polyline, both ends included.</summary>
    private static List<(double X, double Y)> Resample(List<(double X, double Y)> line, double spacing)
    {
        double total = 0;
        for (int i = 0; i + 1 < line.Count; i++) total += Distance(line[i], line[i + 1]);
        int pieces = Math.Max(1, (int)Math.Round(total / spacing));
        double step = total / pieces;

        var result = new List<(double X, double Y)> { line[0] };
        double want = step, walked = 0;
        for (int i = 0; i + 1 < line.Count && result.Count < pieces; i++)
        {
            double len = Distance(line[i], line[i + 1]);
            while (want <= walked + len && result.Count < pieces)
            {
                double t = len > 0 ? (want - walked) / len : 0;
                result.Add((line[i].X + t * (line[i + 1].X - line[i].X), line[i].Y + t * (line[i + 1].Y - line[i].Y)));
                want += step;
            }
            walked += len;
        }
        result.Add(line[^1]);
        return result;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static void WriteFile(string modDir, List<(uint Id, double X, double Y)> points,
        List<(ulong Id, List<uint> Points)> segments, List<(ulong Key, ulong Segment)> strips, int height)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            void Key(ushort key) { w.Write(key); w.Write(Eq); }

            Key(0x00ee); w.Write(Int); w.Write(4);
            Key(0x045a); w.Write(Open);
            foreach (int n in new[] { points.Count, segments.Count, strips.Count }) { w.Write(Int); w.Write(n); }
            w.Write(Close);

            Key(0x05f4); w.Write(Open);
            foreach (var (id, x, y) in points)
            {
                var (wx, wz) = WorldSpace.FromImage(x, y, height);
                w.Write(Open);
                Key(0x000b); w.Write(UInt); w.Write(id);
                Key(0x004c); w.Write(Open);
                w.Write(Float); w.Write((float)wx);
                w.Write(Float); w.Write((float)wz);
                w.Write(Close);
                w.Write(Close);
            }
            w.Write(Close);

            Key(0x05f5); w.Write(Open);
            foreach (var (id, ids) in segments)
            {
                w.Write(Open);
                Key(0x000b); w.Write(ULong); w.Write(id);
                Key(0x05f7); w.Write(Open);
                foreach (uint p in ids) { w.Write(UInt); w.Write(p); }
                w.Write(Close);
                w.Write(Close);
            }
            w.Write(Close);

            Key(0x05f6); w.Write(Open);
            foreach (var (key, segment) in strips)
            {
                w.Write(Open);
                Key(0x000b); w.Write(ULong); w.Write(key);
                Key(0x05f5); w.Write(Open);
                w.Write(ULong); w.Write(segment);
                w.Write(Close);
                w.Write(Close);
            }
            w.Write(Close);
        }

        string dir = Path.Combine(modDir, "gfx", "map", "spline_network");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "spline_network.splnet"), ms.ToArray());
    }
}
