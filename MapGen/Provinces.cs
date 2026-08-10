using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

public sealed class ProvinceSeed
{
    public int X;
    public int Y;
    public bool IsLand;

    /// <summary>
    /// Land, but declared impassable_mountains in default.map: no barony, no holder, armies route
    /// around it. Vanilla has 1,188 of them against 11,301 baronied provinces, and only 4 of those
    /// carry a barony — so this is a third class of province rather than a flag on a normal one.
    /// </summary>
    public bool IsImpassable;
}

/// <summary>Pixel-level province assignment at provinces-map resolution.</summary>
public sealed class ProvinceMap
{
    public required int Width;
    public required int Height;

    /// <summary>Index into <see cref="Seeds"/> for every pixel. Never -1 after partitioning.</summary>
    public required int[] Label;

    public required List<ProvinceSeed> Seeds;

    public int Count => Seeds.Count;
}

/// <summary>
/// Port of the province partitioner in jsck3mapper/run.js.
///
/// Provinces are grown by a multi-source Dijkstra from seed points, using geodesic distance
/// with an absolute land/sea barrier: a land seed can never claim a sea pixel and vice versa.
/// That barrier is what makes provinces follow coastlines instead of spilling across them, and
/// it is why the partition must run on the mask rather than on raw distance.
/// </summary>
public static class Provinces
{
    /// <summary>8-neighbour offsets with their costs; the last four are diagonals.</summary>
    private static readonly (int Dx, int Dy, float Cost, bool Diagonal)[] Dirs =
    [
        (-1, 0, 1f, false), (1, 0, 1f, false), (0, -1, 1f, false), (0, 1, 1f, false),
        (-1, -1, 1.41421356f, true), (1, -1, 1.41421356f, true),
        (-1, 1, 1.41421356f, true), (1, 1, 1.41421356f, true),
    ];

    public static ProvinceMap Build(byte[] mask, float[] elevation, byte[] rivers, byte[] lakes,
        int width, int height, MapConfig cfg, Rng rng)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var size = ProvinceSizeField.Build(mask, elevation, rivers, lakes, width, height, cfg, rng);
        var seeds = PlaceSeeds(mask, width, height, cfg, rng, size);
        Console.WriteLine($"  seeded {seeds.Count(s => s.IsLand)} land / " +
                          $"{seeds.Count(s => !s.IsLand)} sea provinces ({sw.ElapsedMilliseconds} ms)");

        var (component, componentCount) = Core.Stage.Detail("  · seed coverage",
            () => EnsureSeedsCoverComponents(mask, width, height, seeds));

        var cost = Core.Stage.Detail("  · cost elevation blur",
            () => CostElevation(elevation, width, height, cfg));
        var label = Core.Stage.Detail("  · dijkstra",
            () => Partition(mask, cost, width, height, cfg, seeds, component, componentCount));

        var map = new ProvinceMap { Width = width, Height = height, Label = label, Seeds = seeds };
        Core.Stage.Detail("  · repair unlabeled", () => RepairUnlabeled(map, mask));
        Core.Stage.Detail("  · lloyd relaxation",
            () => Relax(map, mask, cost, cfg, component, componentCount));
        Core.Stage.Detail("  · border smoothing", () => SmoothBorders(map, cfg));
        Core.Stage.Detail("  · sever waists", () => SeverWaists(map));
        Core.Stage.Detail("  · reconnect fragments", () => ReconnectFragments(map));
        Core.Stage.Detail("  · dissolve tiny", () => DissolveTinyProvinces(map, mask, cfg));
        Core.Stage.Detail("  · impassable", () => { MarkImpassable(map, elevation, mask, cfg); MergeImpassableRanges(map, cfg); });
        Core.Stage.Detail("  · province report", () => Report(map, elevation, cfg));
        return map;
    }

    /// <summary>
    /// What the land provinces actually came out at, against what was asked for.
    ///
    /// Three numbers, none of which can be read off the settings. The spread says whether
    /// <see cref="MapConfig.ProvinceSizeVariance"/> survived the partition. The median says whether
    /// the seeding is still hitting <see cref="MapConfig.BaronyPixels"/> — the number CK3 crashes
    /// over if it drifts far enough down. And the last says whether borders are still on the ground
    /// they are supposed to be on: it is the average height difference across a province border
    /// against the average across any pair of neighbouring land pixels, so 1.0 means the partition
    /// is ignoring the terrain and anything above it means borders are finding the steep ground.
    /// Smoothing and relaxation both trade that number away for rounder provinces, and it is the
    /// only way to see how much.
    /// </summary>
    private static void Report(ProvinceMap map, float[] elevation, MapConfig cfg)
    {
        double borderRelief = 0, allRelief = 0;
        long borderPairs = 0, allPairs = 0;

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int cell = y * map.Width + x;
                if (!map.Seeds[map.Label[cell]].IsLand) continue;

                if (x + 1 < map.Width) Pair(cell, cell + 1);
                if (y + 1 < map.Height) Pair(cell, cell + map.Width);
            }
        }

        if (allPairs > 0)
            Console.WriteLine($"  province borders sit on relief " +
                              $"{borderRelief / borderPairs / (allRelief / allPairs):F2}x the land average");

        ReportSizes(map, cfg);
        return;

        void Pair(int a, int b)
        {
            if (!map.Seeds[map.Label[b]].IsLand) return;

            double relief = Math.Abs(elevation[a] - elevation[b]);
            allRelief += relief;
            allPairs++;

            if (map.Label[a] == map.Label[b]) return;
            borderRelief += relief;
            borderPairs++;
        }
    }

    private static void ReportSizes(ProvinceMap map, MapConfig cfg)
    {
        var area = new int[map.Count];
        foreach (int label in map.Label) area[label]++;

        var land = new List<int>();
        for (int i = 0; i < map.Count; i++)
            if (map.Seeds[i].IsLand && !map.Seeds[i].IsImpassable && area[i] > 0) land.Add(area[i]);
        if (land.Count == 0) return;

        land.Sort();
        int p10 = land[land.Count / 10];
        int median = land[land.Count / 2];
        int p90 = land[land.Count * 9 / 10];

        Console.WriteLine($"  land province area: p10 {p10} / median {median} / p90 {p90} px " +
                          $"(target {cfg.BaronyPixels:F0}, p90/p10 {(double)p90 / Math.Max(1, p10):F1}x)");
    }

    /// <summary>
    /// The elevation the partition costs its steps against, which is not the elevation the map
    /// ships.
    ///
    /// The cost term is a first difference, so it answers to the roughest thing in the field. On a
    /// real heightmap that is pixel-scale detail, and a frontier that stalls at every scrap of it
    /// gives a border fringed at the same scale — a fray, not a ridgeline. Smoothing first leaves
    /// the mountain range and takes away the noise on its flanks, which is the only part of the
    /// signal a province border was ever meant to follow.
    ///
    /// Water is flattened to sea level before the blur rather than blurred as it is. Otherwise
    /// every coastal land pixel is dragged toward the sea floor, and the false cliff that puts
    /// along the whole coast makes the frontier stall just inland of it — a ring of thin coastal
    /// provinces around every landmass, produced entirely by the smoothing.
    /// </summary>
    private static float[] CostElevation(float[] elevation, int width, int height, MapConfig cfg)
    {
        int radius = (int)Math.Round(cfg.Scaled(cfg.ProvinceTerrainSmoothPixels));
        if (radius < 1 || cfg.ProvinceTerrainCost <= 0) return elevation;

        float sea = cfg.Limits.SeaLevelUpper;
        var flattened = new float[elevation.Length];
        for (int i = 0; i < elevation.Length; i++) flattened[i] = Math.Max(elevation[i], sea);

        return Field.Blur(flattened, width, height, radius, 2);
    }

    /// <summary>
    /// Moves every seed to the middle of the province it grew, and grows them all again.
    ///
    /// A seed sits wherever it was thrown, which is rarely the middle of anything: the province
    /// around it reaches much further one way than the other, and where two lopsided provinces face
    /// each other the one in between is squeezed into a waist. Lloyd's algorithm is the standard
    /// answer — replace each seed with the centroid of its own cell and repartition — and it is the
    /// difference between a voronoi diagram and a *centroidal* one, which is the version that looks
    /// drawn by hand.
    ///
    /// The seed is put on the province pixel nearest the centroid rather than on the centroid
    /// itself. A province bent around a bay has its centroid in the water, and a land seed in the
    /// sea grows nothing at all.
    ///
    /// One round does most of the work and each further one costs a whole repartition, which is why
    /// this is a count rather than a flag.
    /// </summary>
    private static void Relax(ProvinceMap map, byte[] mask, float[] elevation, MapConfig cfg,
        int[] component, int componentCount)
    {
        int iterations = Math.Max(0, cfg.ProvinceRelaxIterations);
        if (iterations == 0) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        double moved = 0;

        for (int pass = 0; pass < iterations; pass++)
        {
            var sumX = new double[map.Count];
            var sumY = new double[map.Count];
            var area = new int[map.Count];

            for (int cell = 0; cell < map.Label.Length; cell++)
            {
                int label = map.Label[cell];
                sumX[label] += cell % map.Width;
                sumY[label] += cell / map.Width;
                area[label]++;
            }

            var best = new double[map.Count];
            Array.Fill(best, double.PositiveInfinity);
            var target = new (int X, int Y)[map.Count];

            for (int cell = 0; cell < map.Label.Length; cell++)
            {
                int label = map.Label[cell];
                int x = cell % map.Width, y = cell / map.Width;

                double dx = x - sumX[label] / area[label];
                double dy = y - sumY[label] / area[label];
                double d = dx * dx + dy * dy;
                if (d >= best[label]) continue;

                best[label] = d;
                target[label] = (x, y);
            }

            moved = 0;
            for (int label = 0; label < map.Count; label++)
            {
                var seed = map.Seeds[label];
                moved += Math.Sqrt((double)(target[label].X - seed.X) * (target[label].X - seed.X)
                                   + (double)(target[label].Y - seed.Y) * (target[label].Y - seed.Y));
                seed.X = target[label].X;
                seed.Y = target[label].Y;
            }
            moved /= Math.Max(1, map.Count);

            map.Label = Partition(mask, elevation, map.Width, map.Height, cfg, map.Seeds,
                component, componentCount);
            RepairUnlabeled(map, mask);
        }

        Console.WriteLine($"  relaxed {iterations}x: seeds moved {moved:F1} px on the last pass " +
                          $"({sw.ElapsedMilliseconds} ms)");
    }

    /// <summary>
    /// Cuts the one- and two-pixel waists that border smoothing narrowed but could not close.
    ///
    /// SmoothBorders will not take a pixel whose removal would disconnect what is left of its
    /// province, which is exactly the pixel holding a waist together: it thins a dumbbell down to
    /// its bar and then protects the bar. Severing it hands the far lobe to ReconnectFragments,
    /// which absorbs a fragment into whichever neighbour it borders most — so this runs before it.
    /// </summary>
    private static void SeverWaists(ProvinceMap map)
    {
        int width = map.Width, height = map.Height;
        var source = map.Label;
        var next = (int[])source.Clone();
        int severed = 0;

        // Hoisted out of the loop: a stackalloc inside one grows the frame on every iteration.
        Span<int> neighborCounts = stackalloc int[9];
        Span<int> neighborLabels = stackalloc int[9];

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int cell = y * width + x;
                int label = source[cell];

                // On a border and *not* safe to give away is the signature: the only pixel whose
                // removal splits its own province is one the province is hanging together by.
                if (!OnBorder(source, width, height, x, y, label)) continue;
                if (SafeToGiveAway(source, width, height, x, y, label)) continue;

                int foreign = 0;
                int bestNeighbor = -1;
                int bestCount = 0;
                int distinct = 0;

                foreach (var (dx, dy) in Ring)
                {
                    int other = source[(y + dy) * width + (x + dx)];
                    if (other == label) continue;
                    if (map.Seeds[other].IsLand != map.Seeds[label].IsLand) continue;

                    foreign++;
                    int slot = 0;
                    while (slot < distinct && neighborLabels[slot] != other) slot++;
                    if (slot == distinct)
                    {
                        neighborLabels[distinct] = other;
                        neighborCounts[distinct++] = 0;
                    }

                    if (++neighborCounts[slot] > bestCount)
                    {
                        bestCount = neighborCounts[slot];
                        bestNeighbor = other;
                    }
                }

                // Six of eight neighbours foreign means the pixel is surrounded on all but two
                // sides, so what it joins is a bar one or two pixels wide. Anything looser than
                // that is an ordinary border pixel, and cutting those would chew up the coast.
                if (foreign >= 6 && bestNeighbor != -1)
                {
                    next[cell] = bestNeighbor;
                    severed++;
                }
            }
        }

        map.Label = next;
        if (severed > 0)
            Console.WriteLine($"  severed {severed} pinched waists for fragment reconnection");
    }

    /// <summary>
    /// Rounds the teeth off province borders.
    ///
    /// The partition is a raster flood, so every border is a staircase, and the terrain term puts a
    /// tooth in it wherever the ground is locally rough. CK3 traces those borders and draws them as
    /// lines, so the teeth survive all the way to the map table.
    ///
    /// Each pass hands a border pixel to whichever province holds most of the block around it — a
    /// majority filter, the standard way to round a raster region boundary, and one that by
    /// construction cannot move a border further than its own window. Two guards keep it honest.
    /// A pixel never changes domain, so no coastline moves and no land pixel is given to a sea
    /// zone. And a pixel is left where it is if its own province's neighbourhood would fall into
    /// two pieces without it, which is what stops the filter severing a province at a narrow waist
    /// instead of widening it.
    /// </summary>
    private static void SmoothBorders(ProvinceMap map, MapConfig cfg)
    {
        int passes = Math.Max(0, cfg.ProvinceBorderSmoothing);
        if (passes == 0) return;

        int width = map.Width, height = map.Height, radius = BorderSmoothRadius;
        int total = 0;

        for (int pass = 0; pass < passes; pass++)
        {
            var source = map.Label;
            var next = (int[])source.Clone();
            int changed = 0;

            // Double-buffered rather than in place: reading a half-updated map would let the pass
            // chase its own output across the raster and give the result a scan-order grain.
            Parallel.For(0, height, () => 0, (y, _, local) =>
            {
                Span<int> labels = stackalloc int[(2 * radius + 1) * (2 * radius + 1)];
                Span<int> counts = stackalloc int[(2 * radius + 1) * (2 * radius + 1)];

                for (int x = 0; x < width; x++)
                {
                    int cell = y * width + x;
                    int label = source[cell];
                    if (!OnBorder(source, width, height, x, y, label)) continue;

                    int distinct = 0, own = 0, best = label, bestCount = 0;

                    for (int j = Math.Max(0, y - radius); j <= Math.Min(height - 1, y + radius); j++)
                    {
                        for (int i = Math.Max(0, x - radius); i <= Math.Min(width - 1, x + radius); i++)
                        {
                            int other = source[j * width + i];
                            if (other == label) { own++; continue; }
                            if (map.Seeds[other].IsLand != map.Seeds[label].IsLand) continue;

                            int slot = 0;
                            while (slot < distinct && labels[slot] != other) slot++;
                            if (slot == distinct) { labels[distinct] = other; counts[distinct++] = 0; }

                            if (++counts[slot] <= bestCount) continue;
                            bestCount = counts[slot];
                            best = other;
                        }
                    }

                    if (bestCount <= own || best == label) continue;
                    if (!SafeToGiveAway(source, width, height, x, y, label)) continue;

                    next[cell] = best;
                    local++;
                }

                return local;
            }, local => Interlocked.Add(ref changed, local));

            map.Label = next;
            total += changed;
            if (changed == 0) break;
        }

        Console.WriteLine($"  border smoothing: {total} pixels reassigned over {passes} passes");
    }

    /// <summary>Window half-width of the majority filter. Two gives a 5x5 block, which is wide
    /// enough to see past a single tooth and narrow enough not to erase a real inlet.</summary>
    private const int BorderSmoothRadius = 2;

    private static bool OnBorder(int[] label, int width, int height, int x, int y, int own)
    {
        foreach (var (dx, dy) in Orthogonal)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            if (label[ny * width + nx] != own) return true;
        }
        return false;
    }

    /// <summary>4-neighbour offsets, for the tests that ask whether a pixel is on a border or holds
    /// one together — diagonal-only contact is a corner rather than either.</summary>
    private static readonly (int Dx, int Dy)[] Orthogonal = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    /// <summary>The eight neighbours in ring order, so a walk around them is a walk around the
    /// pixel.</summary>
    private static readonly (int Dx, int Dy)[] Ring =
        [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)];

    /// <summary>
    /// Whether this pixel can be given away without cutting its own province in two here.
    ///
    /// The ring of eight neighbours is walked once and its runs of same-province pixels counted. One
    /// run means they all reach each other around the outside and the middle pixel is holding
    /// nothing together; two or more means it is the only thing joining them, which is precisely a
    /// waist — and taking a waist pixel away is how a majority filter turns one province into two.
    /// This is the crossing-number test from digital topology, and it is local, so it cannot see a
    /// province that is joined only somewhere else; <see cref="ReconnectFragments"/> is what catches
    /// the rest.
    /// </summary>
    private static bool SafeToGiveAway(int[] label, int width, int height, int x, int y, int own)
    {
        int runs = 0;
        bool previous = Same(Ring.Length - 1);

        for (int i = 0; i < Ring.Length; i++)
        {
            bool here = Same(i);
            if (here && !previous) runs++;
            previous = here;
        }

        return runs <= 1;

        bool Same(int i)
        {
            int nx = x + Ring[i].Dx, ny = y + Ring[i].Dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) return false;
            return label[ny * width + nx] == own;
        }
    }

    /// <summary>
    /// Gives every province back a single body.
    ///
    /// A Dijkstra partition does not guarantee one. A pixel goes to whichever seed reaches it
    /// cheapest, so a cheaper route arriving late can carve a corridor out from under an earlier
    /// label and strand a scrap of it on the far side. Border smoothing severs a province at a
    /// one-pixel waist now and then for the same reason. Either way CK3 is handed a province in two
    /// places: it draws both, gives them one id and one holding, and puts the locator in whichever
    /// piece the geometry happens to pick.
    ///
    /// Each province keeps its largest piece and every other piece joins whichever neighbour it
    /// shares most border with — the rule <see cref="DissolveTinyProvinces"/> already uses, for the
    /// same reason: the longest shared border is the neighbour the scrap most looks like part of.
    /// </summary>
    private static void ReconnectFragments(ProvinceMap map)
    {
        int width = map.Width, height = map.Height;

        // Pieces are found with 4-connectivity, so a province joined to itself only across a
        // diagonal counts as two. That is the right call rather than a limitation: a diagonal
        // touch is a pinch point one pixel wide, which is not a province anybody can hold.
        var piece = new int[width * height];
        Array.Fill(piece, -1);

        var sizes = new List<int>();
        var owner = new List<int>();
        var largest = new Dictionary<int, int>();
        var queue = new int[width * height];

        for (int start = 0; start < piece.Length; start++)
        {
            if (piece[start] != -1) continue;

            int id = sizes.Count;
            int label = map.Label[start];
            int head = 0, tail = 0;
            queue[tail++] = start;
            piece[start] = id;

            while (head < tail)
            {
                int cell = queue[head++];
                int cx = cell % width, cy = cell / width;

                foreach (var (dx, dy) in Orthogonal)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                    int next = ny * width + nx;
                    if (piece[next] != -1 || map.Label[next] != label) continue;

                    piece[next] = id;
                    queue[tail++] = next;
                }
            }

            sizes.Add(tail);
            owner.Add(label);

            if (!largest.TryGetValue(label, out int held) || tail > sizes[held]) largest[label] = id;
        }

        var stranded = new HashSet<int>();
        for (int id = 0; id < sizes.Count; id++)
            if (largest[owner[id]] != id) stranded.Add(id);
        if (stranded.Count == 0) return;

        // Shared border length per (stranded piece, neighbouring province) pair.
        var borders = new Dictionary<int, Dictionary<int, int>>();
        foreach (int id in stranded) borders[id] = [];

        for (int cell = 0; cell < piece.Length; cell++)
        {
            if (!borders.TryGetValue(piece[cell], out var counts)) continue;

            int x = cell % width, y = cell / width;
            foreach (var (dx, dy, _, _) in Dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int other = map.Label[ny * width + nx];
                if (other == map.Label[cell]) continue;
                if (map.Seeds[other].IsLand != map.Seeds[map.Label[cell]].IsLand) continue;

                counts[other] = counts.GetValueOrDefault(other) + 1;
            }
        }

        var reassign = new Dictionary<int, int>();
        long pixels = 0;
        foreach (int id in stranded)
        {
            // A piece whose every neighbour is across water is an island the partition reached
            // legitimately, and there is nothing sensible on the far side to give it to. Left
            // alone; DissolveTinyProvinces takes it if it is too small to stand.
            int best = -1, bestBorder = 0;
            foreach (var (other, border) in borders[id])
                if (border > bestBorder) { best = other; bestBorder = border; }

            if (best < 0) continue;
            reassign[id] = best;
            pixels += sizes[id];
        }

        if (reassign.Count == 0) return;

        for (int cell = 0; cell < piece.Length; cell++)
            if (reassign.TryGetValue(piece[cell], out int target)) map.Label[cell] = target;

        CompactLabels(map);

        Console.WriteLine($"  reconnected {reassign.Count} stranded province fragments " +
                          $"({pixels} px)");
    }

    /// <summary>
    /// Merges away provinces below <see cref="MapConfig.MinProvincePixels"/>.
    ///
    /// This is not in ck2rpg but it is not optional: a province of a handful of pixels has no
    /// interior, so CK3 cannot derive its borders, centroid or locator positions. It then fails
    /// inside geometry code, which logs *nothing* — a crash with an empty log is the signature.
    /// EnsureSeedsCoverComponents guarantees every speck of an island becomes a province, so
    /// without this pass they are generated by the dozen.
    /// </summary>
    private static void DissolveTinyProvinces(ProvinceMap map, byte[] mask, MapConfig cfg)
    {
        int merged = 0, flipped = 0;

        // Merging only ever grows the survivor, so a couple of passes converge.
        for (int pass = 0; pass < 4; pass++)
        {
            var area = new int[map.Count];
            foreach (int label in map.Label) area[label]++;

            var tiny = new List<int>();
            for (int i = 0; i < area.Length; i++)
                if (area[i] > 0 && area[i] < cfg.MinProvincePixels) tiny.Add(i);
            if (tiny.Count == 0) break;

            // Shared border length per (tiny province, neighbour) pair.
            var borders = new Dictionary<int, Dictionary<int, int>>();
            foreach (int t in tiny) borders[t] = [];

            for (int cell = 0; cell < map.Label.Length; cell++)
            {
                int label = map.Label[cell];
                if (!borders.TryGetValue(label, out var counts)) continue;

                int x = cell % map.Width, y = cell / map.Width;
                foreach (var (dx, dy, _, _) in Dirs)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height) continue;
                    int other = map.Label[ny * map.Width + nx];
                    if (other == label) continue;
                    counts[other] = counts.GetValueOrDefault(other) + 1;
                }
            }

            var reassign = new Dictionary<int, int>();
            foreach (int t in tiny)
            {
                var counts = borders[t];
                if (counts.Count == 0) continue; // a whole isolated component; nothing to merge into

                // Prefer a neighbour in the same domain so land never absorbs sea.
                int best = -1, bestBorder = -1;
                foreach (var (other, border) in counts)
                {
                    if (map.Seeds[other].IsLand != map.Seeds[t].IsLand) continue;
                    if (border <= bestBorder) continue;
                    best = other;
                    bestBorder = border;
                }

                if (best >= 0)
                {
                    reassign[t] = best;
                    merged++;
                    continue;
                }

                // A tiny island with no same-domain neighbour: drown it, as ck2rpg's
                // tiny-island handling does, and give the pixels to the surrounding water.
                foreach (var (other, border) in counts)
                {
                    if (border <= bestBorder) continue;
                    best = other;
                    bestBorder = border;
                }
                if (best >= 0)
                {
                    reassign[t] = best;
                    flipped++;
                }
            }

            if (reassign.Count == 0) break;

            for (int cell = 0; cell < map.Label.Length; cell++)
            {
                if (!reassign.TryGetValue(map.Label[cell], out int target)) continue;
                map.Label[cell] = target;
                mask[cell] = map.Seeds[target].IsLand ? (byte)1 : (byte)0;
            }
        }

        CompactLabels(map);

        if (merged + flipped > 0)
            Console.WriteLine($"  dissolved {merged} tiny provinces, drowned {flipped} tiny islands " +
                              $"(min {cfg.MinProvincePixels} px)");
    }

    /// <summary>
    /// Flags the most mountainous land provinces as impassable_mountains.
    ///
    /// Ranked by the share of the province standing above the mountain line rather than by mean
    /// height, so a province is impassable because it is *mostly* mountain, not because it happens
    /// to contain one peak. A floor on that share stops a map with little high ground from having
    /// impassable provinces forced on it just to reach the target count.
    ///
    /// Vanilla's proportion is the reference: 1,188 impassable against 11,301 baronied provinces.
    /// </summary>
    private static void MarkImpassable(ProvinceMap map, float[] elevation, byte[] mask, MapConfig cfg)
    {
        double share = Math.Clamp(cfg.ImpassableShareOfLand, 0, 0.5);
        if (share <= 0) return;

        // The mountain line, as a percentile of this map's own land — the same basis the terrain
        // classifier and the heightmap's hypsometry use, so "mountain" means one thing throughout.
        var land = new List<float>();
        for (int i = 0; i < elevation.Length; i += 7)
            if (mask[i] != 0) land.Add(elevation[i]);
        if (land.Count == 0) return;

        land.Sort();
        float mountainLine = land[(int)Math.Clamp(
            land.Count * (1.0 - cfg.MountainLineShare), 0, land.Count - 1)];

        var total = new int[map.Count];
        var high = new int[map.Count];
        for (int i = 0; i < map.Label.Length; i++)
        {
            int label = map.Label[i];
            if (!map.Seeds[label].IsLand) continue;
            total[label]++;
            if (elevation[i] >= mountainLine) high[label]++;
        }

        var ranked = new List<(int Label, double Share)>();
        for (int i = 0; i < map.Count; i++)
            if (map.Seeds[i].IsLand && total[i] > 0)
                ranked.Add((i, (double)high[i] / total[i]));

        ranked.Sort((a, b) => b.Share.CompareTo(a.Share));

        int want = (int)Math.Round(ranked.Count * share);
        int marked = 0;
        foreach (var (label, mountainous) in ranked)
        {
            if (marked >= want || mountainous < cfg.ImpassableMinMountainShare) break;
            map.Seeds[label].IsImpassable = true;
            marked++;
        }

        Console.WriteLine($"  impassable: {marked} of {ranked.Count} land provinces " +
                          $"(target {want}, mountain line {mountainLine:F0})");
    }

    /// <summary>
    /// Fuses touching impassable provinces into whole mountain ranges.
    ///
    /// <see cref="MarkImpassable"/> judges each province on its own, so a range comes out as a
    /// scatter of separate impassable provinces with borders drawn through the middle of it. On the
    /// map those borders are meaningless — nobody holds either side and nothing can cross — so all
    /// they do is break up a feature the player reads as one wall of rock.
    ///
    /// Contiguity is the whole test for "same range". At province resolution two impassable
    /// provinces that touch are the same massif by definition; there is no separate range identity
    /// to consult, and the mountain groups the coarse world model carries are not available here.
    ///
    /// Growth is capped, which is the one thing this cannot do without. Following contiguity to its
    /// conclusion merges an entire continental spine into a single province — on a full-size map
    /// that is a tenth of all land under one id, with a bounding box spanning the continent, and
    /// CK3 derives borders and a centroid from it. The cap keeps a merged range to a plausible
    /// massif and starts a new one beyond that, so a long chain becomes a handful of large
    /// provinces rather than either a hundred small ones or a single monster.
    /// </summary>
    private static void MergeImpassableRanges(ProvinceMap map, MapConfig cfg)
    {
        double maxPixels = cfg.ImpassableRangeMaxBaronies * cfg.BaronyPixels;
        if (maxPixels <= 0) return;

        var area = new int[map.Count];
        foreach (int label in map.Label) area[label]++;

        var impassable = new bool[map.Count];
        int before = 0;
        for (int i = 0; i < map.Count; i++)
            if (map.Seeds[i].IsLand && map.Seeds[i].IsImpassable) { impassable[i] = true; before++; }
        if (before == 0) return;

        // Which impassable provinces touch which. Only orthogonal contact counts: two ranges that
        // meet at a single diagonal corner are not one massif.
        var touching = new Dictionary<int, HashSet<int>>();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int a = map.Label[y * map.Width + x];
                if (!impassable[a]) continue;

                if (x + 1 < map.Width) Link(a, map.Label[y * map.Width + x + 1]);
                if (y + 1 < map.Height) Link(a, map.Label[(y + 1) * map.Width + x]);
            }
        }

        // Grow each range greedily from the lowest-numbered unclaimed province, stopping before the
        // cap rather than after it, so no merged province ever exceeds the limit.
        var survivor = new int[map.Count];
        for (int i = 0; i < map.Count; i++) survivor[i] = i;

        var claimed = new bool[map.Count];
        int after = 0;

        for (int start = 0; start < map.Count; start++)
        {
            if (!impassable[start] || claimed[start]) continue;

            claimed[start] = true;
            after++;

            long total = area[start];
            var frontier = new Queue<int>();
            frontier.Enqueue(start);

            while (frontier.Count > 0)
            {
                int current = frontier.Dequeue();
                if (!touching.TryGetValue(current, out var neighbours)) continue;

                foreach (int next in neighbours.OrderBy(n => n))
                {
                    if (claimed[next] || total + area[next] > maxPixels) continue;

                    claimed[next] = true;
                    survivor[next] = start;
                    total += area[next];
                    frontier.Enqueue(next);
                }
            }
        }

        if (after == before) return;

        for (int i = 0; i < map.Label.Length; i++) map.Label[i] = survivor[map.Label[i]];
        CompactLabels(map);

        Console.WriteLine($"  impassable ranges: {before} provinces fused into {after} " +
                          $"(cap {maxPixels:F0} px, {cfg.ImpassableRangeMaxBaronies} baronies)");
        return;

        void Link(int a, int b)
        {
            if (a == b || !impassable[b]) return;
            if (!touching.TryGetValue(a, out var sa)) touching[a] = sa = [];
            if (!touching.TryGetValue(b, out var sb)) touching[b] = sb = [];
            sa.Add(b);
            sb.Add(a);
        }
    }

    /// <summary>Drops provinces that no longer own any pixel and renumbers the rest densely.</summary>
    private static void CompactLabels(ProvinceMap map)
    {
        var used = new bool[map.Count];
        foreach (int label in map.Label) used[label] = true;

        var remap = new int[map.Count];
        var kept = new List<ProvinceSeed>();
        for (int i = 0; i < map.Count; i++)
        {
            if (!used[i]) { remap[i] = -1; continue; }
            remap[i] = kept.Count;
            kept.Add(map.Seeds[i]);
        }

        if (kept.Count == map.Count) return;

        for (int i = 0; i < map.Label.Length; i++)
            map.Label[i] = remap[map.Label[i]];
        map.Seeds = kept;
    }

    /// <summary>
    /// How much of the separation a province of a given area asks for it actually gets. Dart
    /// throwing saturates at roughly three quarters of the point density a grid of the same spacing
    /// packs, so a radius taken straight from the target area yields a third fewer provinces than
    /// asked for; pulling it in by this much lands on the same count the old grid did.
    /// </summary>
    private const double PackingRatio = 0.82;

    /// <summary>Candidate spacing as a fraction of the smallest separation on the map. Coarser than
    /// this and the throw stops before it has filled, leaving holes a province would have fitted.</summary>
    private const double CandidateSpacing = 0.5;

    /// <summary>
    /// Where province seeds go. Spacing comes from the target province *area*, so the province
    /// count falls out of how much land there is rather than being fixed in advance.
    ///
    /// It used to work the other way round — spacing was solved from a target count — which meant
    /// a map kept the same number of provinces at every resolution, and a barony at <c>tiny</c>
    /// covered 1/81 of the pixels one at <c>vanilla</c> did. See <see cref="MapConfig.CountyScale"/>.
    ///
    /// Seeds are dart-thrown against a minimum separation rather than dropped one per cell of a
    /// jittered grid. A grid puts no floor at all on how close two seeds get — neighbouring cells
    /// can both jitter into the corner they share — and two seeds a few pixels apart is exactly the
    /// sliver province that pinches to nothing between its neighbours. A grid also has one spacing
    /// everywhere by construction, so it cannot express <see cref="ProvinceSizeField"/> at all.
    /// </summary>
    private static List<ProvinceSeed> PlaceSeeds(byte[] mask, int width, int height, MapConfig cfg,
        Rng rng, ProvinceSizeField size)
    {
        long landPixels = 0;
        for (int i = 0; i < mask.Length; i++) if (mask[i] == 1) landPixels++;

        Console.WriteLine($"  province seeding: {cfg.BaronyPixels:F0} px per land province " +
                          $"(~{landPixels / Math.Max(1, cfg.BaronyPixels):F0} expected), " +
                          $"{cfg.SeaZonePixels:F0} px per sea zone");

        var seeds = new List<ProvinceSeed>();
        Scatter(cfg.BaronyPixels, isLand: true);
        Scatter(cfg.SeaZonePixels, isLand: false);
        return seeds;

        static double Radius(double area) => PackingRatio * Math.Sqrt(Math.Max(4.0, area));

        void Scatter(double targetArea, bool isLand)
        {
            byte want = isLand ? (byte)1 : (byte)0;

            double smallest = Radius(targetArea * size.Smallest);
            double largest = Radius(targetArea * size.Largest);

            int step = Math.Max(1, (int)(smallest * CandidateSpacing));
            var candidates = new List<int>();

            for (int gy = 0; gy < height; gy += step)
            {
                for (int gx = 0; gx < width; gx += step)
                {
                    // Jittered inside the cell, and kept only if it landed on the right side of a
                    // coastline — a seed in the wrong domain would grow nothing.
                    int x = Math.Min(width - 1, gx + rng.Int(0, step - 1));
                    int y = Math.Min(height - 1, gy + rng.Int(0, step - 1));
                    if (mask[y * width + x] != want) continue;
                    candidates.Add(y * width + x);
                }
            }

            // Taken in scan order the accepted set acquires a grain — every seed rejects its
            // right-hand neighbour and keeps the one above — so the order is randomised.
            rng.Shuffle(candidates);

            // Buckets one largest-radius across, so the separation test only ever reads the nine
            // buckets around a candidate: nothing further than that can reject it.
            int bucket = Math.Max(1, (int)Math.Ceiling(largest));
            int bw = width / bucket + 1, bh = height / bucket + 1;
            var buckets = new List<int>?[bw * bh];
            var accepted = new List<(int X, int Y, double Radius)>();

            foreach (int cell in candidates)
            {
                int x = cell % width, y = cell / width;
                double radius = Radius(targetArea * size.At(x, y));
                if (TooClose(x, y, radius)) continue;

                (buckets[y / bucket * bw + x / bucket] ??= []).Add(accepted.Count);
                accepted.Add((x, y, radius));
                seeds.Add(new ProvinceSeed { X = x, Y = y, IsLand = isLand });
            }

            bool TooClose(int x, int y, double radius)
            {
                int bx = x / bucket, by = y / bucket;

                for (int j = Math.Max(0, by - 1); j <= Math.Min(bh - 1, by + 1); j++)
                {
                    for (int i = Math.Max(0, bx - 1); i <= Math.Min(bw - 1, bx + 1); i++)
                    {
                        var occupants = buckets[j * bw + i];
                        if (occupants is null) continue;

                        foreach (int k in occupants)
                        {
                            var (ax, ay, ar) = accepted[k];

                            // Each seed contributes its own half of the gap, so where a region of
                            // large provinces meets one of small provinces the border between them
                            // is a gradient rather than a step.
                            double gap = (radius + ar) * 0.5;
                            double dx = ax - x, dy = ay - y;
                            if (dx * dx + dy * dy < gap * gap) return true;
                        }
                    }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Port of ensureSeedsCoverComponents(). Any connected land or sea region without a seed
    /// would be left unlabeled by the Dijkstra, so every component gets at least one.
    ///
    /// The component map it builds on the way is handed back rather than thrown away, because it is
    /// exactly what lets <see cref="Partition"/> run in parallel: a province can never span two
    /// components, so the components are independent problems.
    /// </summary>
    private static (int[] Component, int Count) EnsureSeedsCoverComponents(
        byte[] mask, int width, int height, List<ProvinceSeed> seeds)
    {
        var component = new int[mask.Length];
        Array.Fill(component, -1);

        var seeded = new HashSet<int>();
        var queue = new int[mask.Length];
        int components = 0;
        var representative = new List<int>();

        for (int start = 0; start < mask.Length; start++)
        {
            if (component[start] != -1) continue;

            int id = components++;
            representative.Add(start);
            byte domain = mask[start];

            int head = 0, tail = 0;
            queue[tail++] = start;
            component[start] = id;

            while (head < tail)
            {
                int cell = queue[head++];
                int cx = cell % width, cy = cell / width;
                foreach (var (dx, dy, _, _) in Dirs)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int n = ny * width + nx;
                    if (component[n] != -1 || mask[n] != domain) continue;
                    component[n] = id;
                    queue[tail++] = n;
                }
            }
        }

        foreach (var seed in seeds)
            seeded.Add(component[seed.Y * width + seed.X]);

        int added = 0;
        for (int id = 0; id < components; id++)
        {
            if (seeded.Contains(id)) continue;
            int cell = representative[id];
            seeds.Add(new ProvinceSeed
            {
                X = cell % width,
                Y = cell / width,
                IsLand = mask[cell] == 1,
            });
            added++;
        }

        if (added > 0)
            Console.WriteLine($"  added {added} seeds to cover otherwise-unreachable components");

        return (component, components);
    }

    /// <summary>
    /// Multi-source Dijkstra. Diagonal moves additionally require both orthogonal cells between
    /// source and target to be in the same domain, which stops provinces leaking through a
    /// one-pixel diagonal gap in a coastline.
    ///
    /// Run once per connected component, in parallel. A province cannot span two components - the
    /// frontier never crosses a coastline, so a landmass and the ocean around it are separate
    /// problems that happen to share an array - and each component only ever writes its own cells,
    /// so no lock is needed and the answer is the same one solving them in turn would give. This is
    /// where the tool spent 39% of a run: the two Dijkstras, this one and the relaxation's,
    /// dominated everything else put together, and Dijkstra itself does not parallelise.
    ///
    /// Components are started largest first. They are wildly uneven - one continent and its ocean
    /// are typically most of the map, against a hundred islands of a few hundred pixels - so
    /// leaving the scheduler to discover that at the end would leave the big one running alone.
    /// </summary>
    private static int[] Partition(byte[] mask, float[] elevation, int width, int height,
        MapConfig cfg, List<ProvinceSeed> seeds, int[] component, int componentCount)
    {
        int n = width * height;
        var label = new int[n];
        var dist = new float[n];
        Array.Fill(label, -1);
        Array.Fill(dist, float.MaxValue);

        // Reference slope, so the weight means the same thing on any map. Sampled rather than
        // measured exactly: this only has to set the scale.
        float terrainCost = (float)Math.Max(0, cfg.ProvinceTerrainCost);
        double total = 0;
        int samples = 0;
        for (int i = width; i < n - width; i += 97)
        {
            if (mask[i] == 0 || mask[i - 1] == 0) continue;
            total += Math.Abs(elevation[i] - elevation[i - 1]);
            samples++;
        }
        float invRef = samples == 0 || total <= 0 ? 0f : (float)(samples / (total * 3.0));

        var seedsIn = new List<int>?[componentCount];
        for (int i = 0; i < seeds.Count; i++)
        {
            int c = component[seeds[i].Y * width + seeds[i].X];
            (seedsIn[c] ??= []).Add(i);
        }

        var cells = new int[componentCount];
        foreach (int c in component) cells[c]++;

        var order = Enumerable.Range(0, componentCount)
            .Where(c => seedsIn[c] is not null)
            .OrderByDescending(c => cells[c])
            .ToArray();

        Parallel.ForEach(order, c =>
        {
            var heap = new MinHeap(cells[c]);

            foreach (int i in seedsIn[c]!)
            {
                int k = seeds[i].Y * width + seeds[i].X;
                label[k] = i;
                dist[k] = 0;
                heap.Push(k, 0);
            }

            while (heap.TryPop(out int cell, out float d))
            {
                if (d > dist[cell]) continue; // stale entry

                int li = label[cell];
                byte domain = mask[cell];
                int x = cell % width, y = cell / width;

                foreach (var (dx, dy, cost, diagonal) in Dirs)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                    int nk = ny * width + nx;
                    if (mask[nk] != domain) continue;

                    if (diagonal)
                    {
                        if (mask[y * width + nx] != domain) continue;
                        if (mask[ny * width + x] != domain) continue;
                    }

                    // Terrain-aware cost: stepping across a slope costs more than stepping along a
                    // flat, so the frontier stalls at ridgelines and two provinces meet there. With
                    // a uniform cost the partition is a plain geodesic voronoi and boundaries fall
                    // wherever the seeds happen to be equidistant, cutting straight over mountains.
                    float nd = d + cost * (1f + terrainCost * MathF.Abs(elevation[nk] - elevation[cell]) * invRef);
                    if (nd >= dist[nk]) continue;
                    dist[nk] = nd;
                    label[nk] = li;
                    heap.Push(nk, nd);
                }
            }
        });

        return label;
    }

    /// <summary>
    /// Port of repairUnlabeled(). Anything the Dijkstra could not reach is handed to the label
    /// of its nearest labeled neighbour, so no pixel is left without a province.
    /// </summary>
    private static void RepairUnlabeled(ProvinceMap map, byte[] mask)
    {
        var label = map.Label;
        var queue = new List<int>();

        for (int i = 0; i < label.Length; i++)
            if (label[i] >= 0) queue.Add(i);

        int unlabeled = label.Length - queue.Count;
        if (unlabeled == 0) return;

        int head = 0;
        while (head < queue.Count)
        {
            int cell = queue[head++];
            int x = cell % map.Width, y = cell / map.Width;
            foreach (var (dx, dy, _, _) in Dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height) continue;
                int nk = ny * map.Width + nx;
                if (label[nk] >= 0) continue;
                label[nk] = label[cell];
                queue.Add(nk);
            }
        }

        Console.WriteLine($"  repaired {unlabeled} unlabeled pixels");
    }
}

/// <summary>
/// Binary min-heap keyed by float, with lazy invalidation — a cell may be pushed more than
/// once and stale entries are skipped on pop. Equivalent to makeMinHeap in run.js.
/// </summary>
internal sealed class MinHeap(int capacity)
{
    private int[] _keys = new int[Math.Max(16, capacity / 4)];
    private float[] _priorities = new float[Math.Max(16, capacity / 4)];
    private int _count;

    public void Push(int key, float priority)
    {
        if (_count == _keys.Length)
        {
            Array.Resize(ref _keys, _count * 2);
            Array.Resize(ref _priorities, _count * 2);
        }

        int i = _count++;
        _keys[i] = key;
        _priorities[i] = priority;

        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (_priorities[parent] <= _priorities[i]) break;
            Swap(i, parent);
            i = parent;
        }
    }

    public bool TryPop(out int key, out float priority)
    {
        if (_count == 0)
        {
            key = -1;
            priority = 0;
            return false;
        }

        key = _keys[0];
        priority = _priorities[0];

        _count--;
        if (_count > 0)
        {
            _keys[0] = _keys[_count];
            _priorities[0] = _priorities[_count];

            int i = 0;
            while (true)
            {
                int left = 2 * i + 1, right = left + 1, smallest = i;
                if (left < _count && _priorities[left] < _priorities[smallest]) smallest = left;
                if (right < _count && _priorities[right] < _priorities[smallest]) smallest = right;
                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
        }

        return true;
    }

    private void Swap(int a, int b)
    {
        (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
        (_priorities[a], _priorities[b]) = (_priorities[b], _priorities[a]);
    }
}
