using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// A way cut through an impassable wall. Positions are province-raster pixels, y from the top.
/// <c>Thickness</c> is the wall's thickness at the neck and <c>Detour</c> the least the way round
/// was found to be, both in barony widths; <c>Saddle</c> is the highest elevation the corridor
/// crosses; <c>Corridor</c> the pixels taken out of the wall.
/// </summary>
public sealed record MountainPass(
    (int X, int Y) Middle, (int X, int Y) SideA, (int X, int Y) SideB,
    float Saddle, double Thickness, double Detour, int[] Corridor);

/// <summary>
/// Passes through the auto-cut's walls. See <see cref="MapConfig.MountainPasses"/>.
///
/// Vanilla breaks a range into chunks with passable mountain provinces between them. The auto-cut
/// draws a range as one wall, ending where its slopes give out, so a long range has no door. This
/// finds, wall by wall, the necks where the wall is thin and the land route between its two sides
/// is long, and opens the lowest one — a corridor along the lowest ground through it, wide enough
/// for a barony of its own. One pass per wall at most, and none where a short walk goes round.
///
/// It runs on the finished mask, after the auto-cut has closed, opened, filled and dropped: run
/// before, the closing would seal the corridor again. Nothing it does touches the heightmap; the
/// pass crosses the saddle the terrain already has.
/// </summary>
public static class MountainPasses
{
    /// <summary>How many necks of one wall are tried, a barony apart, before its pass is chosen:
    /// enough to cover a range forty baronies long end to end.</summary>
    private const int TestsPerWall = 64;

    /// <summary>
    /// Two rim pixels whose floods meet inside the wall are on opposite sides of it only if they
    /// are about as far apart as the wall is thick there; floods from neighbours along the same side
    /// meet too, a long way in and a short way apart.
    /// </summary>
    private const double OppositeSides = 0.7;

    /// <summary>How much steeper the corridor's cost gets across its window's height range: the
    /// path pays up to this many times its length again to cross the highest ground instead of the
    /// lowest, so it follows the saddle rather than the straight line.</summary>
    private const float HeightPenalty = 8f;

    /// <summary>
    /// How far either side of the straight line across the neck the path may stray, as a share of
    /// the crossing's length. The lowest ground in any wall is its own foot, so a path free to
    /// wander walked along the rim and cut a crescent off the wall's edge instead of crossing it.
    /// </summary>
    private const double Band = 0.35;

    /// <summary>The corridor's half-width over the saddle and at its two mouths, in barony radii.
    /// Wide mouths are what make it read as a valley opening into the lowland, not a slot.</summary>
    private const double WaistWidth = 0.3, MouthWidth = 1.1;

    /// <summary>How fast rising ground stops the corridor spreading: every <see cref="ClimbScale"/>
    /// elevation units above the path at that point cost another <see cref="Climb"/> times the step,
    /// so it runs out along a broad valley floor and stops against steep sides.</summary>
    private const float Climb = 2f, ClimbScale = 25f;

    private static readonly (int Dx, int Dy, float Step)[] Around =
    [
        (-1, 0, 1f), (1, 0, 1f), (0, -1, 1f), (0, 1, 1f),
        (-1, -1, 1.41421356f), (1, -1, 1.41421356f), (-1, 1, 1.41421356f), (1, 1, 1.41421356f),
    ];

    /// <summary>
    /// Cuts the passes into <paramref name="mask"/> and returns them. <paramref name="parts"/> and
    /// <paramref name="partSizes"/> label the walls as they stood before small pieces were dropped;
    /// a label whose pixels are no longer in the mask is skipped. <paramref name="cleared"/> is how
    /// many mask pixels were given back to passable land, corridors and the slivers they cut off
    /// together.
    /// </summary>
    internal static List<MountainPass> Carve(bool[] mask, byte[] land, float[] elevation, int[] parts,
        List<int> partSizes, int width, int height, int radius, double barony, MapConfig cfg, out long cleared)
    {
        cleared = 0;
        var passes = new List<MountainPass>();
        if (!cfg.MountainPasses) return passes;

        double across = 2.0 * radius;
        int maxSpan = Math.Max(2, (int)Math.Round(cfg.MountainPassMaxThickness * across));
        double minDetour = Math.Max(0, cfg.MountainPassMinDetour) * across;

        // Each wall's bounding box, so every per-wall array is the size of the wall, not the map.
        int walls = partSizes.Count;
        var box = new (int X0, int Y0, int X1, int Y1)[walls];
        for (int k = 0; k < walls; k++) box[k] = (width, height, -1, -1);
        for (int y = 0; y < height; y++)
            for (int i = y * width, end = i + width; i < end; i++)
            {
                int p = parts[i];
                if (p == 0 || !mask[i]) continue;
                int x = i - y * width;
                ref var b = ref box[p - 1];
                b = (Math.Min(b.X0, x), Math.Min(b.Y0, y), Math.Max(b.X1, x), Math.Max(b.Y1, y));
            }

        var routes = new RouteGrid(mask, land, width, height, Math.Max(2, radius / 4));

        for (int wall = 1; wall <= walls; wall++)
        {
            var b = box[wall - 1];
            if (b.X1 < 0 || partSizes[wall - 1] < barony / 2) continue;

            var pass = TryWall(wall, b, mask, land, elevation, parts, width, height, maxSpan, minDetour,
                across, radius, barony, routes, ref cleared);
            if (pass is not null) passes.Add(pass);
        }

        return passes;
    }

    /// <summary>One log line: how many passes, and for each how thick the wall was, how high the
    /// corridor climbs and how far the way round was at least.</summary>
    internal static void Report(IReadOnlyList<MountainPass> passes)
    {
        if (passes.Count == 0)
        {
            Console.WriteLine("  mountain passes: none — no wall is both thin enough and long enough to need one");
            return;
        }
        Console.WriteLine($"  mountain passes: {passes.Count} cut — " + string.Join("; ", passes.Select(p =>
            $"{p.Thickness:F2} baronies thick, saddle {p.Saddle:F0}, way round over {p.Detour:F0} baronies")));
    }

    private static MountainPass? TryWall(int wall, (int X0, int Y0, int X1, int Y1) b, bool[] mask, byte[] land,
        float[] elevation, int[] parts, int width, int height, int maxSpan, double minDetour, double across,
        int radius, double barony, RouteGrid routes, ref long cleared)
    {
        // The box grows by one so the rim's passable neighbours are inside it.
        int x0 = Math.Max(0, b.X0 - 1), y0 = Math.Max(0, b.Y0 - 1);
        int x1 = Math.Min(width - 1, b.X1 + 1), y1 = Math.Min(height - 1, b.Y1 + 1);
        int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
        bool InWall(int g) => parts[g] == wall && mask[g];
        bool Open(int g) => land[g] != 0 && !mask[g];

        // 1. Flood in from the rim. Each wall pixel learns how far it is from the nearest passable
        // pixel and which one that is, as Crossings floods water from each shore.
        var dist = new ushort[bw * bh];
        var from = new int[bw * bh];
        var queue = new Queue<int>();
        for (int ly = 0; ly < bh; ly++)
            for (int lx = 0; lx < bw; lx++)
            {
                int g = (y0 + ly) * width + x0 + lx;
                if (!InWall(g)) continue;
                foreach (var (dx, dy, _) in Around)
                {
                    int nx = x0 + lx + dx, ny = y0 + ly + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height || !Open(ny * width + nx)) continue;
                    dist[ly * bw + lx] = 1;
                    from[ly * bw + lx] = ny * width + nx;
                    queue.Enqueue(ly * bw + lx);
                    break;
                }
            }
        while (queue.Count > 0)
        {
            int l = queue.Dequeue();
            if (dist[l] >= maxSpan) continue;
            int lx = l % bw, ly = l / bw;
            foreach (var (dx, dy, _) in Around)
            {
                int nx = lx + dx, ny = ly + dy;
                if (nx < 0 || ny < 0 || nx >= bw || ny >= bh) continue;
                int n = ny * bw + nx;
                if (dist[n] != 0 || !InWall((y0 + ny) * width + x0 + nx)) continue;
                dist[n] = (ushort)(dist[l] + 1);
                from[n] = from[l];
                queue.Enqueue(n);
            }
        }

        // 2. Necks: where floods from opposite sides meet within the thickness allowed.
        var necks = new List<(float Saddle, int Span, int A, int B)>();
        var seen = new HashSet<(int, int)>();
        for (int ly = 0; ly < bh; ly++)
            for (int lx = 0; lx < bw; lx++)
            {
                int l = ly * bw + lx;
                if (dist[l] == 0) continue;
                if (lx + 1 < bw) Meet(l, l + 1);
                if (ly + 1 < bh) Meet(l, l + bw);
            }
        if (necks.Count == 0) return null;

        // Thinnest first, so the necks tried spread along the whole wall. Ranking by saddle instead
        // spent every try on the wall's tapering ends, which are low, thin and a short walk round;
        // the necks worth a pass sit mid-range, where the wall is high. The pixel indices settle
        // ties so the order is the map's.
        necks.Sort((p, q) => p.Span != q.Span ? p.Span.CompareTo(q.Span)
            : p.Saddle != q.Saddle ? p.Saddle.CompareTo(q.Saddle)
            : p.A != q.A ? p.A.CompareTo(q.A) : p.B.CompareTo(q.B));

        // 3. Every neck whose way round is long enough, of at most TestsPerWall tried, never two
        // tried within a barony of each other; then the lowest of those.
        var tried = new List<(double X, double Y)>();
        var qualified = new List<(float Saddle, int Span, int A, int B, double Needed)>();
        foreach (var neck in necks)
        {
            if (tried.Count >= TestsPerWall) break;
            double mx = (neck.A % width + neck.B % width) / 2.0, my = (neck.A / width + neck.B / width) / 2.0;
            if (tried.Any(t => (t.X - mx) * (t.X - mx) + (t.Y - my) * (t.Y - my) < across * across)) continue;
            tried.Add((mx, my));

            double needed = Math.Max(minDetour, 4.0 * neck.Span);
            if (!routes.Within(neck.A, neck.B, needed)) qualified.Add((neck.Saddle, neck.Span, neck.A, neck.B, needed));
        }

        foreach (var neck in qualified.OrderBy(q => q.Saddle).ThenBy(q => q.Span).ThenBy(q => q.A))
        {
            var path = LowestPath(neck.A, neck.B, neck.Span, wall, mask, parts, elevation, width, height);
            if (path is null) continue;

            // 4. Cut the corridor and give back whatever it cuts off that is too small to stand.
            var (corridor, middle) = Widen(path, wall, mask, parts, elevation, width, height, radius);
            foreach (int g in corridor) mask[g] = false;
            routes.Open(corridor);
            cleared += corridor.Count + DropSlivers(wall, x0, y0, bw, bh, mask, parts, width, barony, routes);

            float saddle = float.MinValue;
            foreach (int g in path) saddle = Math.Max(saddle, elevation[g]);
            return new MountainPass((middle % width, middle / width), (neck.A % width, neck.A / width),
                (neck.B % width, neck.B / width), saddle, neck.Span / across, neck.Needed / across, corridor.ToArray());
        }
        return null;

        void Meet(int l, int m)
        {
            if (dist[m] == 0 || from[l] == from[m]) return;
            int span = dist[l] + dist[m];
            if (span > maxSpan) return;
            int a = Math.Min(from[l], from[m]), c = Math.Max(from[l], from[m]);
            double ax = a % width, ay = a / width, cx = c % width, cy = c / width;
            double apart = Math.Sqrt((ax - cx) * (ax - cx) + (ay - cy) * (ay - cy));
            if (apart < OppositeSides * span || !seen.Add((a, c))) return;

            // The saddle along the straight line between the two sides: what ranks the necks, before
            // the corridor finds its own way through.
            int steps = (int)Math.Ceiling(apart);
            float top = float.MinValue;
            for (int s = 0; s <= steps; s++)
            {
                double t = steps == 0 ? 0 : (double)s / steps;
                int px = (int)Math.Round(ax + (cx - ax) * t), py = (int)Math.Round(ay + (cy - ay) * t);
                top = Math.Max(top, elevation[py * width + px]);
            }
            necks.Add((top, span, a, c));
        }
    }

    /// <summary>
    /// The cheapest way through the wall from <paramref name="a"/> to <paramref name="b"/>, each
    /// step costing its length times 1 + <see cref="HeightPenalty"/>·t², t the step's height within
    /// the window's range; null if the wall has closed the way. Held to a band either side of the
    /// straight line across the neck (see <see cref="Band"/>), so it bends to the low ground there
    /// without wandering off to find some.
    /// </summary>
    private static List<int>? LowestPath(int a, int b, int span, int wall, bool[] mask, int[] parts,
        float[] elevation, int width, int height)
    {
        double ax = a % width, ay = a / width, bx = b % width, by = b / width;
        double straight = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        double band = Math.Max(3, Band * Math.Max(span, straight));
        double ux = straight > 0 ? (bx - ax) / straight : 0, uy = straight > 0 ? (by - ay) / straight : 0;
        bool InBand(int g)
        {
            double px = g % width - ax, py = g / width - ay, t = px * ux + py * uy;
            return t >= -band && t <= straight + band && Math.Abs(px * uy - py * ux) <= band;
        }

        int pad = (int)Math.Ceiling(band) + 2;
        int x0 = Math.Max(0, Math.Min(a % width, b % width) - pad), x1 = Math.Min(width - 1, Math.Max(a % width, b % width) + pad);
        int y0 = Math.Max(0, Math.Min(a / width, b / width) - pad), y1 = Math.Min(height - 1, Math.Max(a / width, b / width) + pad);
        int ww = x1 - x0 + 1, wh = y1 - y0 + 1;
        int Local(int g) => (g / width - y0) * ww + (g % width - x0);
        bool Node(int g) => g == a || g == b || (parts[g] == wall && mask[g] && InBand(g));

        float lo = float.MaxValue, hi = float.MinValue;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int g = y * width + x;
                if (!Node(g)) continue;
                lo = Math.Min(lo, elevation[g]);
                hi = Math.Max(hi, elevation[g]);
            }
        float range = Math.Max(hi - lo, 1f);

        var cost = new float[ww * wh];
        Array.Fill(cost, float.PositiveInfinity);
        var parent = new int[ww * wh];
        var open = new PriorityQueue<int, float>();
        cost[Local(a)] = 0;
        open.Enqueue(a, 0);
        while (open.TryDequeue(out int g, out float c))
        {
            if (c > cost[Local(g)]) continue;
            if (g == b) break;
            int gx = g % width, gy = g / width;
            foreach (var (dx, dy, step) in Around)
            {
                int nx = gx + dx, ny = gy + dy;
                if (nx < x0 || ny < y0 || nx > x1 || ny > y1) continue;
                int n = ny * width + nx;
                if (!Node(n)) continue;
                float t = (elevation[n] - lo) / range;
                float next = c + step * (1 + HeightPenalty * t * t);
                if (next >= cost[Local(n)]) continue;
                cost[Local(n)] = next;
                parent[Local(n)] = g;
                open.Enqueue(n, next);
            }
        }
        if (float.IsPositiveInfinity(cost[Local(b)])) return null;

        var path = new List<int>();
        for (int g = b; g != a; g = parent[Local(g)]) path.Add(g);
        path.Add(a);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// The corridor round <paramref name="path"/>, and its middle pixel. It grows out from a
    /// smoothed copy of the path, each step out costing more the higher it climbs above the path at
    /// that point, until the allowance for that point is spent. The allowance is narrow over the
    /// saddle, flares toward the mouths and wavers a little between, so the pass takes the shape
    /// of the valley it runs in — broad where the floor is, pinched where the slopes close in —
    /// rather than a tube of one width. The result is rounded, and held to what connects to the
    /// path, so no specks are left in the wall.
    /// </summary>
    private static (List<int> Corridor, int Middle) Widen(List<int> path, int wall, bool[] mask, int[] parts,
        float[] elevation, int width, int height, int radius)
    {
        bool InWall(int g) => parts[g] == wall && mask[g];

        // The centreline: the path averaged over a twelfth of its length each side, ends kept,
        // then resampled a pixel apart.
        int n = path.Count, reach = Math.Max(2, n / 12);
        var py = new double[n];
        var px = new double[n];
        for (int i = 0; i < n; i++)
        {
            int lo = i == 0 || i == n - 1 ? i : Math.Max(0, i - reach);
            int hi = i == 0 || i == n - 1 ? i : Math.Min(n - 1, i + reach);
            for (int j = lo; j <= hi; j++) { py[i] += path[j] / width; px[i] += path[j] % width; }
            py[i] /= hi - lo + 1;
            px[i] /= hi - lo + 1;
        }
        var along = new double[n];
        for (int i = 1; i < n; i++)
            along[i] = along[i - 1] + Math.Sqrt((py[i] - py[i - 1]) * (py[i] - py[i - 1]) + (px[i] - px[i - 1]) * (px[i] - px[i - 1]));
        double length = Math.Max(1, along[n - 1]);
        int count = Math.Max(2, (int)length + 1);

        // Where each sample stands, what it may spend, and the ground it starts from. The waver's
        // phases come from the path's own ends, so a pass looks the same every time it is made.
        uint seed = Hash((uint)path[0], (uint)path[n - 1], 0x9A55u);
        double phase1 = (seed & 0xFFFF) / 65536.0 * 2 * Math.PI, phase2 = (seed >> 16) / 65536.0 * 2 * Math.PI;
        var at = new int[count];
        var allow = new float[count];
        var floor = new float[count];
        for (int m = 0, i = 0; m < count; m++)
        {
            double s = length * m / (count - 1);
            while (i < n - 2 && along[i + 1] < s) i++;
            double f = along[i + 1] > along[i] ? Math.Clamp((s - along[i]) / (along[i + 1] - along[i]), 0, 1) : 0;
            int x = (int)Math.Round(px[i] + (px[i + 1] - px[i]) * f), y = (int)Math.Round(py[i] + (py[i + 1] - py[i]) * f);
            at[m] = Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1);
            double u = s / length, flare = Math.Pow(1 - Math.Sin(Math.PI * u), 2);
            double waver = 1 + 0.18 * Math.Sin(2 * Math.PI * 1.7 * u + phase1) + 0.1 * Math.Sin(2 * Math.PI * 3.3 * u + phase2);
            allow[m] = (float)(radius * (WaistWidth + (MouthWidth - WaistWidth) * flare) * waver);
            floor[m] = elevation[at[m]];
        }

        // A window round the samples, as far out as the widest allowance can reach.
        int pad = (int)Math.Ceiling(radius * MouthWidth * 1.3) + 4;
        int x0 = width, y0 = height, x1 = 0, y1 = 0;
        foreach (int g in at)
        {
            x0 = Math.Min(x0, g % width); x1 = Math.Max(x1, g % width);
            y0 = Math.Min(y0, g / width); y1 = Math.Max(y1, g / width);
        }
        x0 = Math.Max(0, x0 - pad); y0 = Math.Max(0, y0 - pad);
        x1 = Math.Min(width - 1, x1 + pad); y1 = Math.Min(height - 1, y1 + pad);
        int ww = x1 - x0 + 1, wh = y1 - y0 + 1;
        int Local(int g) => (g / width - y0) * ww + (g % width - x0);
        int Global(int l) => (y0 + l / ww) * width + x0 + l % ww;

        // Grown out from every sample at once, each pixel owned by whichever sample reached it
        // cheapest and spending that sample's allowance.
        var cost = new float[ww * wh];
        Array.Fill(cost, float.PositiveInfinity);
        var owner = new int[ww * wh];
        var open = new PriorityQueue<int, float>();
        for (int m = 0; m < count; m++)
        {
            int l = Local(at[m]);
            if (cost[l] == 0) continue;
            cost[l] = 0;
            owner[l] = m;
            open.Enqueue(l, 0);
        }
        while (open.TryDequeue(out int l, out float c))
        {
            if (c > cost[l]) continue;
            int m = owner[l], lx = l % ww, ly = l / ww;
            foreach (var (dx, dy, step) in Around)
            {
                int nx = lx + dx, ny = ly + dy;
                if (nx < 0 || ny < 0 || nx >= ww || ny >= wh) continue;
                int k = ny * ww + nx;
                float rise = Math.Max(0, elevation[Global(k)] - floor[m]);
                float next = c + step * (1 + Climb * rise / ClimbScale);
                if (next > allow[m] || next >= cost[k]) continue;
                cost[k] = next;
                owner[k] = m;
                open.Enqueue(k, next);
            }
        }

        // What the growth reached, plus the centreline itself, rounded: closed to fill its bays,
        // opened to take off its spurs, and the centreline put back so the way stays open.
        var reached = new bool[ww * wh];
        for (int l = 0; l < reached.Length; l++) reached[l] = !float.IsPositiveInfinity(cost[l]);
        var line = new bool[ww * wh];
        foreach (int g in at)
        {
            int gx = g % width - x0, gy = g / width - y0;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    if (dx * dx + dy * dy <= 4 && gx + dx >= 0 && gy + dy >= 0 && gx + dx < ww && gy + dy < wh)
                        line[(gy + dy) * ww + gx + dx] = true;
        }
        for (int l = 0; l < reached.Length; l++) reached[l] |= line[l];
        reached = Open(Close(reached, ww, wh, 3), ww, wh, 2);

        // Only wall of this wall, and only what connects to the centreline.
        var keep = new bool[ww * wh];
        var stack = new Stack<int>();
        for (int l = 0; l < line.Length; l++)
            if (line[l] && InWall(Global(l)) && !keep[l]) { keep[l] = true; stack.Push(l); }
        while (stack.Count > 0)
        {
            int l = stack.Pop(), lx = l % ww, ly = l / ww;
            foreach (var (dx, dy, _) in Around)
            {
                int nx = lx + dx, ny = ly + dy;
                if (nx < 0 || ny < 0 || nx >= ww || ny >= wh) continue;
                int k = ny * ww + nx;
                if (keep[k] || !(reached[k] || line[k]) || !InWall(Global(k))) continue;
                keep[k] = true;
                stack.Push(k);
            }
        }

        // The middle is the corridor pixel nearest the centreline's halfway point, so the pass's
        // seed stands in the pass even where smoothing drew the line across a bend.
        var corridor = new List<int>();
        int halfway = at[count / 2], middle = halfway;
        long nearest = long.MaxValue;
        for (int l = 0; l < keep.Length; l++)
        {
            if (!keep[l]) continue;
            int g = Global(l);
            corridor.Add(g);
            long dx = g % width - halfway % width, dy = g / width - halfway / width, d = dx * dx + dy * dy;
            if (d < nearest) { nearest = d; middle = g; }
        }
        return (corridor, middle);
    }

    /// <summary>A disc closing on a small window: dilate then erode, off-window counted as unset.</summary>
    private static bool[] Close(bool[] set, int w, int h, int r) => Disc(Disc(set, w, h, r, true), w, h, r, false);

    /// <summary>A disc opening on a small window: erode then dilate.</summary>
    private static bool[] Open(bool[] set, int w, int h, int r) => Disc(Disc(set, w, h, r, false), w, h, r, true);

    private static bool[] Disc(bool[] set, int w, int h, int r, bool dilate)
    {
        var result = new bool[set.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool any = false, all = true;
                for (int dy = -r; dy <= r && (dilate ? !any : all); dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > r * r) continue;
                        int nx = x + dx, ny = y + dy;
                        bool v = nx >= 0 && ny >= 0 && nx < w && ny < h && set[ny * w + nx];
                        any |= v;
                        all &= v;
                    }
                result[y * w + x] = dilate ? any : all;
            }
        return result;
    }

    private static uint Hash(uint a, uint b, uint salt)
    {
        unchecked
        {
            uint h = a * 374761393u + b * 668265263u + salt * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    /// <summary>
    /// Gives back to passable land every piece of the wall the corridor left under half a barony,
    /// as the auto-cut drops such pieces everywhere else. Returns the pixels given back.
    /// </summary>
    private static long DropSlivers(int wall, int x0, int y0, int bw, int bh, bool[] mask, int[] parts, int width,
        double barony, RouteGrid routes)
    {
        var label = new int[bw * bh];
        var members = new List<int>();
        var stack = new Stack<int>();
        long dropped = 0;
        int next = 0;
        for (int start = 0; start < label.Length; start++)
        {
            int g0 = (y0 + start / bw) * width + x0 + start % bw;
            if (label[start] != 0 || parts[g0] != wall || !mask[g0]) continue;

            members.Clear();
            label[start] = ++next;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int l = stack.Pop();
                members.Add(l);
                int lx = l % bw, ly = l / bw;
                Visit(lx - 1, ly); Visit(lx + 1, ly); Visit(lx, ly - 1); Visit(lx, ly + 1);
            }
            if (members.Count >= barony / 2) continue;

            var freed = new List<int>(members.Count);
            foreach (int l in members)
            {
                int g = (y0 + l / bw) * width + x0 + l % bw;
                mask[g] = false;
                freed.Add(g);
            }
            routes.Open(freed);
            dropped += freed.Count;
        }
        return dropped;

        void Visit(int lx, int ly)
        {
            if (lx < 0 || ly < 0 || lx >= bw || ly >= bh) return;
            int l = ly * bw + lx;
            int g = (y0 + ly) * width + x0 + lx;
            if (label[l] != 0 || parts[g] != wall || !mask[g]) return;
            label[l] = next;
            stack.Push(l);
        }
    }

    /// <summary>
    /// Passable land on a grid a few pixels to the cell, for asking how far round a wall the land
    /// route goes. A cell is open if any of its pixels is passable land, so a narrow gap between
    /// two walls stays a way through — erring toward a short way round, and so toward no pass.
    /// </summary>
    private sealed class RouteGrid
    {
        private readonly bool[] _open;
        private readonly float[] _cost;
        private readonly List<int> _touched = [];
        private readonly int _q, _gw, _gh, _width;

        public RouteGrid(bool[] mask, byte[] land, int width, int height, int q)
        {
            _q = q;
            _width = width;
            _gw = (width + q - 1) / q;
            _gh = (height + q - 1) / q;
            _open = new bool[_gw * _gh];
            _cost = new float[_gw * _gh];
            Array.Fill(_cost, float.PositiveInfinity);
            Parallel.For(0, _gh, cy =>
            {
                for (int cx = 0; cx < _gw; cx++)
                {
                    bool open = false;
                    for (int y = cy * q, ye = Math.Min(height, y + q); y < ye && !open; y++)
                        for (int i = y * width + cx * q, end = y * width + Math.Min(width, cx * q + q); i < end; i++)
                            if (land[i] != 0 && !mask[i]) { open = true; break; }
                    _open[cy * _gw + cx] = open;
                }
            });
        }

        private int Cell(int g) => g / _width / _q * _gw + g % _width / _q;

        public void Open(IEnumerable<int> pixels)
        {
            foreach (int g in pixels) _open[Cell(g)] = true;
        }

        /// <summary>Whether the land route from pixel <paramref name="a"/> to pixel
        /// <paramref name="b"/> is shorter than <paramref name="limit"/> pixels.</summary>
        public bool Within(int a, int b, double limit)
        {
            int start = Cell(a), goal = Cell(b);
            float reach = (float)(limit / _q);
            var open = new PriorityQueue<int, float>();
            _cost[start] = 0;
            _touched.Add(start);
            open.Enqueue(start, 0);
            bool found = false;
            while (open.TryDequeue(out int c, out float d))
            {
                if (d > _cost[c]) continue;
                if (c == goal) { found = true; break; }
                int cx = c % _gw, cy = c / _gw;
                foreach (var (dx, dy, step) in Around)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= _gw || ny >= _gh) continue;
                    int n = ny * _gw + nx;
                    float next = d + step;
                    if (!_open[n] || next >= reach || next >= _cost[n]) continue;
                    if (float.IsPositiveInfinity(_cost[n])) _touched.Add(n);
                    _cost[n] = next;
                    open.Enqueue(n, next);
                }
            }
            foreach (int c in _touched) _cost[c] = float.PositiveInfinity;
            _touched.Clear();
            return found;
        }
    }

    /// <summary>
    /// The land province each pass became, by id: the one holding the pass's middle pixel, where
    /// its pinned seed stands. Passes whose middle ended up in anything but a barony are left out.
    /// </summary>
    public static IEnumerable<(MountainPass Pass, int ProvinceId)> ProvinceIds(ProvinceMap map, int[] order, int baronyCount)
    {
        if (map.AutoCut?.Passes is not { Count: > 0 } passes) yield break;
        foreach (var pass in passes)
        {
            int id = order[map.Label[pass.Middle.Y * map.Width + pass.Middle.X]];
            if (id >= 1 && id <= baronyCount) yield return (pass, id);
        }
    }

    /// <summary>The word a pass takes after its barony's name: a gap where it crosses the range low,
    /// a pass where it has to climb.</summary>
    public static string Word(MountainPass pass, MapConfig cfg)
        => pass.Saddle < 0.75 * Provinces.CeilingLine(cfg) ? "Gap" : "Pass";

    /// <summary>
    /// Renames each pass barony for its pass — "Hald" becomes "Hald Pass" — keeping its key. The
    /// barony's own name is the culture's word for the place already, so the pass takes no new draw
    /// from any stream and no other name on the map moves. A title the real world supplied keeps
    /// the name it came with.
    /// </summary>
    public static int Name(List<Title> empires, ProvinceMap map, int[] order, int baronyCount, MapConfig cfg)
    {
        var baronies = Titles.Flatten(empires).Where(t => t.Tier == "b" && t.ProvinceId > 0)
            .ToDictionary(t => t.ProvinceId);
        int named = 0;
        foreach (var (pass, id) in ProvinceIds(map, order, baronyCount))
        {
            if (!baronies.TryGetValue(id, out var barony) || barony.Inherited) continue;
            barony.Name = $"{barony.Name} {Word(pass, cfg)}";
            named++;
        }
        return named;
    }
}
