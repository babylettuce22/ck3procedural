using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

public sealed class ProvinceSeed
{
    public int X;
    public int Y;
    public bool IsLand;
    public bool IsImpassable;
    public bool IsMajorRiver;

    /// <summary>
    /// Why this province is impassable, for the preview and hover readout. <c>Score</c> means it
    /// ranked in on relief; <c>Mask</c> means the user painted it in
    /// <see cref="MapConfig.ImpassableMaskPath"/>; <c>Trapped</c> means the connectivity pass
    /// filled it because it was landlocked behind other impassables; <c>None</c> for every
    /// passable province.
    /// </summary>
    public ImpassableCause ImpassableCause;

    /// <summary>
    /// The relief score <see cref="Provinces"/> ranked this land province by when choosing
    /// impassables, and its two ingredients — the share of pixels above the mountain line and
    /// the share of steep pixels. Diagnostics only; NaN on water and on maps that skipped the pass.
    /// </summary>
    public float ImpassableScore = float.NaN, HighShare = float.NaN, SteepShare = float.NaN;

    /// <summary>
    /// The region this province grows inside and may never leave. See <see cref="ProvinceDomain"/>.
    ///
    /// Strictly finer than <see cref="IsLand"/> — water is always domain 0 — so every test that used
    /// to ask whether two provinces were on the same side of the coastline can ask about domains
    /// instead and get the old answer plus the border constraint.
    /// </summary>
    public int Domain;
}

public enum ImpassableCause : byte { None, Score, Trapped, Mask }

/// <summary>
/// What the impassable pass measured on this map, kept so the preview can show the same lines
/// and the same floor the selection used instead of re-deriving them and drifting.
/// </summary>
public sealed record ImpassableDiagnostics(
    float MountainLine, float SteepLine, double Median, double Mad, double Floor, double Cut,
    int Target, int Marked, string LimitedBy)
{
    public bool Qualifies(float score) => !float.IsNaN(score) && score >= Floor;
}

/// <summary>Pixel-level province assignment at provinces-map resolution.</summary>
public sealed class ProvinceMap
{
    public required int Width;
    public required int Height;

    /// <summary>Index into <see cref="Seeds"/> for every pixel. Never -1 after partitioning.</summary>
    public required int[] Label;

    public required List<ProvinceSeed> Seeds;

    /// <summary>Set by the impassable pass; null when it was skipped or found no mountains.</summary>
    public ImpassableDiagnostics? Impassability;

    /// <summary>
    /// True when <see cref="MapConfig.ImpassableMaskPath"/> decided the impassables instead of the
    /// relief scoring, so the preview can say "not painted" rather than "no pass ran".
    /// </summary>
    public bool ImpassableMaskUsed;

    public int Count => Seeds.Count;

    /// <summary>
    /// Whether two provinces may exchange pixels — that is, whether they grew in the same region.
    ///
    /// Every tidy-up pass after the partition used to ask <c>Seeds[a].IsLand == Seeds[b].IsLand</c>
    /// before moving a pixel or merging a province, to stop the sea eating the shore. Asking about
    /// the domain instead answers the same question and the border question together, because water
    /// is a domain of its own; that is why the border constraint needed no new test anywhere, only
    /// a wider one.
    /// </summary>
    public bool SameDomain(int a, int b) => Seeds[a].Domain == Seeds[b].Domain;
}

/// <summary>
/// Province partitioner using Parallel Delta-Stepping geodesic distance.
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

    private static readonly (int Dx, int Dy)[] Orthogonal = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    private static readonly (int Dx, int Dy)[] Ring =
        [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)];

    private const int BorderSmoothRadius = 2;
    private const double PackingRatio = 0.82;
    private const double CandidateSpacing = 0.5;

    public static ProvinceMap Build(
                byte[] mask,
                float[] elevation,
                ClimateField climate,
                int width,
                int height,
                MapConfig cfg,
                Rng rng,
                List<MajorRiverPath>? majorRivers = null,
                Drainage? drainage = null,
                AzgaarImport? azgaar = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var size = ProvinceSizeField.Build(mask, elevation, climate, width, height, cfg, rng);
        var seeds = PlaceSeeds(mask, width, height, cfg, rng, size);

        // --- Place dedicated seeds along carved Major River corridors ---
        if (majorRivers is { Count: > 0 })
        {
            int riverSeedsAdded = PlaceMajorRiverSeeds(seeds, majorRivers, mask, width, height, cfg);
            if (riverSeedsAdded > 0)
                Console.WriteLine($"  seeded {riverSeedsAdded} major river provinces along {majorRivers.Count} river system(s)");
        }

        Console.WriteLine($"  seeded {seeds.Count(s => s.IsLand)} land / " +
                          $"{seeds.Count(s => !s.IsLand && s.IsMajorRiver)} major river / " +
                          $"{seeds.Count(s => !s.IsLand && !s.IsMajorRiver)} sea provinces ({sw.ElapsedMilliseconds} ms)");

        // The hand-painted impassable mask, if any. Read before the domain field because in Snap
        // mode it *is* part of the domain field: the paint becomes a region the partition may not
        // cross, which is what makes the wall come out the shape it was drawn.
        var painted = ImpassableMask.Load(cfg, width, height);
        bool snap = painted is not null && cfg.ImpassableMaskMode == ImpassableMaskMode.Snap;

        // The region each pixel grows inside. Without an import this is the land mask by another
        // name; with one it is the export's provinces, and the partition below cannot cross them.
        var domain = Core.Stage.Detail("  · domain field",
            () => ProvinceDomain.Build(mask, azgaar, width, height, cfg, snap ? painted : null));

        foreach (var seed in seeds) seed.Domain = domain[seed.Y * width + seed.X];

        Core.Stage.Detail("  · seed coverage", () => EnsureSeedsCoverComponents(domain, width, height, seeds));

        // Rivers add crossing resistance to CostElevation
        var cost = Core.Stage.Detail("  · cost elevation blur", () => CostElevation(elevation, mask, drainage, width, height, cfg));

        var bucketManager = new ThreadBucketManager();
        var label = Core.Stage.Detail("  · delta-stepping partition",
            () => Partition(domain, cost, width, height, cfg, seeds, bucketManager));

        var map = new ProvinceMap { Width = width, Height = height, Label = label, Seeds = seeds };
        Core.Stage.Detail("  · repair unlabeled", () => RepairUnlabeled(map, domain));
        Core.Stage.Detail("  · lloyd relaxation", () => Relax(map, domain, cost, cfg, bucketManager));
        Core.Stage.Detail("  · border smoothing", () => SmoothBorders(map, cfg));
        Core.Stage.Detail("  · sever waists", () => SeverWaists(map));
        Core.Stage.Detail("  · reconnect fragments", () => ReconnectFragments(map));
        Core.Stage.Detail("  · dissolve tiny", () => DissolveTinyProvinces(map, mask, cfg));
        Core.Stage.Detail("  · impassable", () =>
        {
            // A painted mask replaces the relief scoring outright; the pocket fill and the range
            // fusing run either way, since a drawn wall can enclose land just as a ridge can.
            if (snap) MarkSnappedImpassable(map);
            else if (painted is not null) MarkPaintedImpassable(map, painted, cfg);
            else MarkImpassable(map, elevation, mask, cfg);
            MarkTrappedProvincesImpassable(map);
            MergeImpassableRanges(map, cfg);
        });
        Core.Stage.Detail("  · province report", () => Report(map, elevation, cfg));
        if (azgaar is not null || snap) VerifyDomains(map, domain);
        return map;
    }

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

        ReportEdge(map);
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

    /// <summary>
    /// How many provinces run off the side of the map, split land from water.
    ///
    /// This is the number <see cref="MapConfig.OceanBorder"/> exists to move: forcing a ring of
    /// ocean around the edge drives the land count to zero by drowning whatever reached it. With
    /// the ring off, this line is the only way to tell whether a seed actually has land at the
    /// boundary, and how much — a handful of clipped provinces is a different proposition from a
    /// continent running off the pole.
    /// </summary>
    private static void ReportEdge(ProvinceMap map)
    {
        var land = new HashSet<int>();
        var water = new HashSet<int>();

        void Note(int cell)
        {
            int label = map.Label[cell];
            (map.Seeds[label].IsLand ? land : water).Add(label);
        }

        for (int x = 0; x < map.Width; x++)
        {
            Note(x);
            Note((map.Height - 1) * map.Width + x);
        }

        for (int y = 0; y < map.Height; y++)
        {
            Note(y * map.Width);
            Note(y * map.Width + map.Width - 1);
        }

        Console.WriteLine($"  provinces touching the map edge: {land.Count} land, {water.Count} water");
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

    private static float[] CostElevation(
            float[] elevation,
            byte[] mask,
            Drainage? drainage,
            int width,
            int height,
            MapConfig cfg)
    {
        int radius = (int)Math.Round(cfg.Scaled(cfg.ProvinceTerrainSmoothPixels));
        float sea = cfg.Limits.SeaLevelUpper;
        var costField = new float[elevation.Length];

        // 1. Flatten elevation below sea level
        Parallel.For(0, height, y =>
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                costField[i] = Math.Max(elevation[i], sea);
            }
        });

        // 2. Add river / valley crossing cost if drainage is available
        if (drainage is not null)
        {
            float maxFlow = 5000f;
            for (int i = 0; i < costField.Length; i++)
            {
                if (mask[i] == 1 && drainage.Flow[i] > 100f)
                {
                    // Tributary flow acts as an elevation ridge in cost space
                    float riverPenalty = MathF.Min(1.0f, drainage.Flow[i] / maxFlow) * 15.0f;
                    costField[i] += riverPenalty;
                }
            }
        }

        if (radius < 1 || cfg.ProvinceTerrainCost <= 0) return costField;
        return Field.Blur(costField, width, height, radius, 2);
    }

    private static void Relax(ProvinceMap map, int[] domain, float[] elevation, MapConfig cfg,
        ThreadBucketManager bucketManager)
    {
        int iterations = Math.Max(0, cfg.ProvinceRelaxIterations);
        if (iterations == 0) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        double moved = 0;
        int mapCount = map.Count;
        int width = map.Width;
        int height = map.Height;

        var sumX = new double[mapCount];
        var sumY = new double[mapCount];
        var area = new int[mapCount];
        var centroidX = new double[mapCount];
        var centroidY = new double[mapCount];
        var best = new double[mapCount];
        var target = new (int X, int Y)[mapCount];

        for (int pass = 0; pass < iterations; pass++)
        {
            Array.Clear(sumX);
            Array.Clear(sumY);
            Array.Clear(area);

            Parallel.For(0, height, () => (new double[mapCount], new double[mapCount], new int[mapCount]),
                (y, _, local) =>
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int label = map.Label[row + x];
                        local.Item1[label] += x;
                        local.Item2[label] += y;
                        local.Item3[label]++;
                    }
                    return local;
                },
                local =>
                {
                    lock (sumX)
                    {
                        for (int i = 0; i < mapCount; i++)
                        {
                            sumX[i] += local.Item1[i];
                            sumY[i] += local.Item2[i];
                            area[i] += local.Item3[i];
                        }
                    }
                });

            for (int i = 0; i < mapCount; i++)
            {
                if (area[i] > 0)
                {
                    centroidX[i] = sumX[i] / area[i];
                    centroidY[i] = sumY[i] / area[i];
                }
            }

            Array.Fill(best, double.PositiveInfinity);

            Parallel.For(0, height, () =>
            {
                var localBest = new double[mapCount];
                Array.Fill(localBest, double.PositiveInfinity);
                return (localBest, new (int X, int Y)[mapCount]);
            },
            (y, _, local) =>
            {
                var (localBest, localTarget) = local;
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int label = map.Label[row + x];

                    // A seed may only move to ground its own province is allowed to grow from.
                    // Relaxation picks the pixel nearest the centroid, and the partition that
                    // follows reads the domain of wherever the seed landed — so a seed that
                    // wandered across a border would take its whole province with it next pass.
                    // In a clean map every pixel of a province already shares its domain and this
                    // rejects nothing; it matters only where a tidy-up pass has left a province
                    // holding a stray pixel from next door.
                    if (domain[row + x] != map.Seeds[label].Domain) continue;

                    double dx = x - centroidX[label];
                    double dy = y - centroidY[label];
                    double d = dx * dx + dy * dy;

                    if (Closer(d, x, y, localBest[label], localTarget[label]))
                    {
                        localBest[label] = d;
                        localTarget[label] = (x, y);
                    }
                }
                return local;
            },
            local =>
            {
                lock (best)
                {
                    for (int i = 0; i < mapCount; i++)
                    {
                        if (double.IsPositiveInfinity(local.Item1[i])) continue;
                        if (!Closer(local.Item1[i], local.Item2[i].X, local.Item2[i].Y, best[i], target[i])) continue;

                        best[i] = local.Item1[i];
                        target[i] = local.Item2[i];
                    }
                }
            });

            moved = 0;
            for (int label = 0; label < mapCount; label++)
            {
                // No pixel of its own — nothing to move towards, and (0, 0) is not an answer.
                if (double.IsPositiveInfinity(best[label])) continue;

                var seed = map.Seeds[label];
                moved += Math.Sqrt((double)(target[label].X - seed.X) * (target[label].X - seed.X)
                                   + (double)(target[label].Y - seed.Y) * (target[label].Y - seed.Y));
                seed.X = target[label].X;
                seed.Y = target[label].Y;
            }
            moved /= Math.Max(1, mapCount);

            map.Label = Partition(domain, elevation, map.Width, map.Height, cfg, map.Seeds, bucketManager);
            RepairUnlabeled(map, domain);
        }

        Console.WriteLine($"  relaxed {iterations}x: seeds moved {moved:F1} px on the last pass " +
                          $"({sw.ElapsedMilliseconds} ms)");
    }

    /// <summary>
    /// Whether a candidate pixel beats the incumbent as a province's next seed, under a total
    /// order rather than a bare distance test.
    ///
    /// The tie-break is not a nicety. The candidates are integer pixels measured against a
    /// fractional centroid, so exact ties are the common case — every pair placed symmetrically
    /// about the centre ties to the bit. Under a plain <c>&lt;</c> the winner is whichever tied
    /// pixel its worker happened to see first, which depends on how Parallel.For split the rows
    /// and on which thread reached the reduction lock first. That is the whole of the province
    /// map's run-to-run drift on a fixed seed: the relaxed seeds land a pixel apart, the next
    /// partition grows from somewhere slightly different, and the borders come out different.
    ///
    /// Ordering on position after distance — topmost row, then leftmost column — makes the winner
    /// a property of the map rather than of the schedule, so the result no longer depends on how
    /// the work was divided.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Closer(double d, int x, int y, double bestDistance, (int X, int Y) bestAt)
    {
        if (d < bestDistance) return true;
        if (d > bestDistance) return false;
        return y < bestAt.Y || (y == bestAt.Y && x < bestAt.X);
    }

    private static void SeverWaists(ProvinceMap map)
    {
        int width = map.Width, height = map.Height;
        var source = map.Label;
        var next = (int[])source.Clone();
        int severed = 0;

        Parallel.For(1, height - 1, () => 0, (y, _, localSevered) =>
        {
            Span<int> neighborCounts = stackalloc int[9];
            Span<int> neighborLabels = stackalloc int[9];
            int row = y * width;

            for (int x = 1; x < width - 1; x++)
            {
                int cell = row + x;
                int label = source[cell];

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
                    if (!map.SameDomain(other, label)) continue;

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

                if (foreign >= 6 && bestNeighbor != -1)
                {
                    next[cell] = bestNeighbor;
                    localSevered++;
                }
            }

            return localSevered;
        }, local => Interlocked.Add(ref severed, local));

        map.Label = next;
        if (severed > 0)
            Console.WriteLine($"  severed {severed} pinched waists for fragment reconnection");
    }

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
                            if (!map.SameDomain(other, label)) continue;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool OnBorder(int[] label, int width, int height, int x, int y, int own)
    {
        if (x > 0 && x < width - 1 && y > 0 && y < height - 1)
        {
            int cell = y * width + x;
            return label[cell - 1] != own || label[cell + 1] != own ||
                   label[cell - width] != own || label[cell + width] != own;
        }

        foreach (var (dx, dy) in Orthogonal)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            if (label[ny * width + nx] != own) return true;
        }
        return false;
    }

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

    private static void ReconnectFragments(ProvinceMap map)
    {
        int width = map.Width, height = map.Height;

        var piece = new int[width * height];
        Array.Fill(piece, -1);

        var sizes = new List<int>();
        var owner = new List<int>();
        var largest = new int[map.Count];
        Array.Fill(largest, -1);
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

            int held = largest[label];
            if (held == -1 || tail > sizes[held]) largest[label] = id;
        }

        var isStranded = new bool[sizes.Count];
        int strandedCount = 0;
        for (int id = 0; id < sizes.Count; id++)
        {
            if (largest[owner[id]] != id)
            {
                isStranded[id] = true;
                strandedCount++;
            }
        }
        if (strandedCount == 0) return;

        var borders = new Dictionary<int, Dictionary<int, int>>();
        for (int id = 0; id < sizes.Count; id++)
            if (isStranded[id]) borders[id] = [];

        for (int cell = 0; cell < piece.Length; cell++)
        {
            int p = piece[cell];
            if (!isStranded[p]) continue;

            var counts = borders[p];
            int x = cell % width, y = cell / width;

            foreach (var (dx, dy, _, _) in Dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int other = map.Label[ny * width + nx];
                if (other == map.Label[cell]) continue;
                if (!map.SameDomain(other, map.Label[cell])) continue;

                counts[other] = counts.GetValueOrDefault(other) + 1;
            }
        }

        var reassign = new Dictionary<int, int>();
        long pixels = 0;
        for (int id = 0; id < sizes.Count; id++)
        {
            if (!isStranded[id]) continue;

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

    private static void DissolveTinyProvinces(ProvinceMap map, byte[] mask, MapConfig cfg)
    {
        int merged = 0, flipped = 0;

        for (int pass = 0; pass < 4; pass++)
        {
            var area = new int[map.Count];
            foreach (int label in map.Label) area[label]++;

            var tiny = new List<int>();
            for (int i = 0; i < area.Length; i++)
                if (area[i] > 0 && area[i] < cfg.MinProvincePixels) tiny.Add(i);
            if (tiny.Count == 0) break;

            var isTiny = new bool[map.Count];
            var borders = new Dictionary<int, Dictionary<int, int>>();
            foreach (int t in tiny)
            {
                isTiny[t] = true;
                borders[t] = [];
            }

            for (int cell = 0; cell < map.Label.Length; cell++)
            {
                int label = map.Label[cell];
                if (!isTiny[label]) continue;

                var counts = borders[label];
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
                if (counts.Count == 0) continue;

                int best = -1, bestBorder = -1;
                foreach (var (other, border) in counts)
                {
                    if (!map.SameDomain(other, t)) continue;
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

    /// <summary>Gradient magnitude per pixel, central differences, wrapping east-west.</summary>
    internal static float[] Slopes(float[] elevation, int width, int height)
    {
        var slope = new float[elevation.Length];

        Parallel.For(0, height, y =>
        {
            int up = Math.Max(0, y - 1), down = Math.Min(height - 1, y + 1);
            float dyScale = down == up ? 1f : 1f / (down - up);

            for (int x = 0; x < width; x++)
            {
                int left = (x - 1 + width) % width, right = (x + 1) % width;
                int i = y * width + x;

                float dx = (elevation[y * width + right] - elevation[y * width + left]) * 0.5f;
                float dy = (elevation[down * width + x] - elevation[up * width + x]) * dyScale;

                slope[i] = MathF.Sqrt(dx * dx + dy * dy);
            }
        });

        return slope;
    }

    /// <summary>
    /// The value at <paramref name="fraction"/> through this map's own land, whatever the field is.
    ///
    /// It used to take a <c>minValue</c> that excluded pixels from the population before taking the
    /// percentile, and the one caller that passed it — the steep line — was the worse for it. Two
    /// reasons it is gone. It contradicted the setting it implements: SteepLineShare is documented
    /// as "share of *land* counted as steep ground", not a share of whatever already cleared a
    /// separate threshold. And it made the result move with relief: scaling every slope down pushes
    /// pixels under the cut, so the surviving population is the steep tail and its percentile sits
    /// too high — measured at 0.33 of the uncompressed steep line where the compression was 0.222,
    /// while the unfiltered mountain line tracked exactly.
    ///
    /// A floor belongs on the answer, not on the population. Apply it to the returned line.
    /// </summary>
    private static float LandLine(float[] field, byte[] mask, double fraction)
    {
        var land = new List<float>();
        for (int i = 0; i < field.Length; i += 7)
        {
            if (mask[i] != 0)
                land.Add(field[i]);
        }

        // Guards a map with essentially no land, which has no percentile worth taking.
        if (land.Count < (mask.Length / 7) * 0.01)
            return float.MaxValue;

        land.Sort();
        return land[(int)Math.Clamp(land.Count * fraction, 0, land.Count - 1)];
    }

    /// <summary>
    /// Snap mode's marking, and there is almost nothing to it: the partition was cut against the
    /// mask's domain, so a province either grew inside the paint or outside it and its seed's
    /// domain says which. Nothing is counted per pixel because nothing straddles; VerifyDomains
    /// asserts that afterwards.
    /// </summary>
    private static void MarkSnappedImpassable(ProvinceMap map)
    {
        map.ImpassableMaskUsed = true;

        var area = new int[map.Count];
        foreach (int label in map.Label) area[label]++;

        int land = 0, marked = 0;
        long pixels = 0;
        for (int i = 0; i < map.Count; i++)
        {
            var seed = map.Seeds[i];
            if (!seed.IsLand || area[i] == 0) continue;
            land++;
            if (!ProvinceDomain.IsPainted(seed.Domain)) continue;
            seed.IsImpassable = true;
            seed.ImpassableCause = ImpassableCause.Mask;
            marked++;
            pixels += area[i];
        }

        Console.WriteLine($"  impassable: {marked} of {land} land provinces cut to the mask ({pixels} px)");
    }

    /// <summary>
    /// The mask's answer to <see cref="MarkImpassable"/>: a land province turns impassable when
    /// the share of its pixels painted white reaches <see cref="MapConfig.ImpassableMaskMinShare"/>
    /// — and at the default of 0, when a single white pixel lands on it, so a stroke drawn across
    /// the map turns every province it touches and the wall it traces has no gaps. White on water
    /// is ignored. No relief score is computed, so the preview's score readout is empty on these
    /// maps; <see cref="ProvinceMap.ImpassableMaskUsed"/> tells it why.
    /// </summary>
    private static void MarkPaintedImpassable(ProvinceMap map, bool[] painted, MapConfig cfg)
    {
        map.ImpassableMaskUsed = true;

        var total = new int[map.Count];
        var white = new int[map.Count];
        long onWater = 0;
        for (int i = 0; i < map.Label.Length; i++)
        {
            int label = map.Label[i];
            if (!map.Seeds[label].IsLand)
            {
                if (painted[i]) onWater++;
                continue;
            }
            total[label]++;
            if (painted[i]) white[label]++;
        }

        double minShare = Math.Clamp(cfg.ImpassableMaskMinShare, 0, 1);
        int land = 0, marked = 0, touched = 0;
        for (int i = 0; i < map.Count; i++)
        {
            if (!map.Seeds[i].IsLand || total[i] == 0) continue;
            land++;
            if (white[i] == 0) continue;
            touched++;
            if ((double)white[i] / total[i] < minShare) continue;
            map.Seeds[i].IsImpassable = true;
            map.Seeds[i].ImpassableCause = ImpassableCause.Mask;
            marked++;
        }

        string skipped = touched > marked ? $", {touched - marked} touched but under the {minShare:P0} share" : "";
        string water = onWater > 0 ? $", {onWater} white pixels on water ignored" : "";
        Console.WriteLine($"  impassable: {marked} of {land} land provinces painted in the mask{skipped}{water}");
    }

    private static void MarkImpassable(ProvinceMap map, float[] elevation, byte[] mask, MapConfig cfg)
    {
        double share = Math.Clamp(cfg.ImpassableShareOfLand, 0, 0.5);
        if (share <= 0) return;

        float mountainLine = LandLine(elevation, mask, 1.0 - cfg.MountainLineShare);
        if (mountainLine == float.MaxValue) return;

        var slope = Slopes(elevation, map.Width, map.Height);

        // Guard the setting, then convert it to this map's scale — not the other way round, or the
        // 0.01 degenerate-guard would become the binding floor on a small map. A gradient authored
        // against vanilla-scale terrain has to travel with the relief; see MapConfig.ReliefScale.
        float minSlope = (float)(Math.Max(0.01, cfg.MinPhysicalSlope) * cfg.ReliefScale);

        // Floor on the line, never a filter on the population — see LandLine.
        float steepLine = MathF.Max(minSlope,
            LandLine(slope, mask, 1.0 - Math.Clamp(cfg.SteepLineShare, 0, 1)));


        var total = new int[map.Count];
        var high = new int[map.Count];
        var steep = new int[map.Count];
        for (int i = 0; i < map.Label.Length; i++)
        {
            int label = map.Label[i];
            if (!map.Seeds[label].IsLand) continue;
            total[label]++;
            if (elevation[i] >= mountainLine) high[label]++;
            if (slope[i] >= steepLine) steep[label]++;
        }

        double slopeWeight = Math.Clamp(cfg.ImpassableSlopeWeight, 0, 1);
        var ranked = new List<(int Label, double Score, double High, double Steep)>();
        for (int i = 0; i < map.Count; i++)
        {
            if (!map.Seeds[i].IsLand || total[i] == 0) continue;

            double highShare = (double)high[i] / total[i];
            double steepShare = (double)steep[i] / total[i];
            double score = highShare * (1 - slopeWeight) + steepShare * slopeWeight;
            ranked.Add((i, score, highShare, steepShare));

            map.Seeds[i].ImpassableScore = (float)score;
            map.Seeds[i].HighShare = (float)highShare;
            map.Seeds[i].SteepShare = (float)steepShare;
        }

        if (ranked.Count == 0) return;

        ranked.Sort((a, b) => b.Score.CompareTo(a.Score));

        double median = ranked[ranked.Count / 2].Score;

        var deviation = new double[ranked.Count];
        for (int i = 0; i < ranked.Count; i++) deviation[i] = Math.Abs(ranked[i].Score - median);
        Array.Sort(deviation);
        double mad = deviation[deviation.Length / 2];

        double adaptive = median + Math.Max(0, cfg.ImpassableScoreDeviations) * mad;
        double floor = Math.Max(cfg.ImpassableMinMountainShare, adaptive);

        int want = (int)Math.Round(ranked.Count * share);
        int marked = 0;
        double highSum = 0, steepSum = 0;
        double cut = 0;
        foreach (var (label, score, highShare, steepShare) in ranked)
        {
            if (marked >= want || score < floor) break;
            map.Seeds[label].IsImpassable = true;
            map.Seeds[label].ImpassableCause = ImpassableCause.Score;
            highSum += highShare;
            steepSum += steepShare;
            cut = score;
            marked++;
        }

        string mix = marked == 0
            ? ""
            : $", mean {highSum / marked:P0} above the line and {steepSum / marked:P0} steep";
        Console.WriteLine($"  impassable: {marked} of {ranked.Count} land provinces " +
                          $"(target {want}, mountain line {mountainLine:F0}, " +
                          $"steep line {steepLine:F2}/px{mix})");

        string bound = marked >= want ? "target share"
            : adaptive > cfg.ImpassableMinMountainShare ? "floor, adaptive"
            : "floor, absolute backstop";
        Console.WriteLine($"    score: median {median:F3}, deviation {mad:F3}, " +
                          $"floor {floor:F3} (adaptive {adaptive:F3} vs backstop " +
                          $"{cfg.ImpassableMinMountainShare:F2}), cut at {cut:F3} — " +
                          $"limited by {bound}");

        map.Impassability = new ImpassableDiagnostics(
            mountainLine, steepLine, median, mad, floor, cut, want, marked, bound);
    }

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
            if (a == b || !impassable[b] || !map.SameDomain(a, b)) return;
            if (!touching.TryGetValue(a, out var sa)) touching[a] = sa = [];
            if (!touching.TryGetValue(b, out var sb)) touching[b] = sb = [];
            sa.Add(b);
            sb.Add(a);
        }
    }

    private static void MarkTrappedProvincesImpassable(ProvinceMap map)
    {
        int width = map.Width, height = map.Height;
        int count = map.Count;

        var neighbors = new HashSet<int>[count];
        var seaAccess = new bool[count];
        for (int i = 0; i < count; i++) neighbors[i] = [];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int a = map.Label[y * width + x];

                if (x + 1 < width) CheckEdge(a, map.Label[y * width + x + 1]);
                if (y + 1 < height) CheckEdge(a, map.Label[(y + 1) * width + x]);

                void CheckEdge(int u, int v)
                {
                    if (u == v) return;
                    var seedU = map.Seeds[u];
                    var seedV = map.Seeds[v];

                    if (seedU.IsLand && !seedV.IsLand) seaAccess[u] = true;
                    if (seedV.IsLand && !seedU.IsLand) seaAccess[v] = true;

                    if (seedU.IsLand && seedV.IsLand)
                    {
                        neighbors[u].Add(v);
                        neighbors[v].Add(u);
                    }
                }
            }
        }

        var visited = new bool[count];
        var components = new List<List<int>>();

        for (int i = 0; i < count; i++)
        {
            if (visited[i] || !map.Seeds[i].IsLand || map.Seeds[i].IsImpassable) continue;

            var comp = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                comp.Add(curr);

                foreach (int nbr in neighbors[curr])
                {
                    if (visited[nbr] || !map.Seeds[nbr].IsLand || map.Seeds[nbr].IsImpassable) continue;
                    visited[nbr] = true;
                    queue.Enqueue(nbr);
                }
            }

            components.Add(comp);
        }

        if (components.Count <= 1) return;

        int largestComponentIdx = 0;
        for (int c = 1; c < components.Count; c++)
        {
            if (components[c].Count > components[largestComponentIdx].Count)
                largestComponentIdx = c;
        }

        int convertedCount = 0;
        for (int c = 0; c < components.Count; c++)
        {
            if (c == largestComponentIdx) continue;
            if (components[c].Any(p => seaAccess[p])) continue;

            foreach (int p in components[c])
            {
                map.Seeds[p].IsImpassable = true;
                map.Seeds[p].ImpassableCause = ImpassableCause.Trapped;
                convertedCount++;
            }
        }

        if (convertedCount > 0)
        {
            Console.WriteLine($"  connectivity check: filled {convertedCount} trapped province(s) into impassable_mountains");
        }
    }

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
                    int x = Math.Min(width - 1, gx + rng.Int(0, step - 1));
                    int y = Math.Min(height - 1, gy + rng.Int(0, step - 1));
                    if (mask[y * width + x] != want) continue;
                    candidates.Add(y * width + x);
                }
            }

            rng.Shuffle(candidates);

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

    private static void EnsureSeedsCoverComponents(
        int[] domain, int width, int height, List<ProvinceSeed> seeds)
    {
        int n = width * height;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int i)
        {
            int root = i;
            while (root != parent[root]) root = parent[root];
            int curr = i;
            while (curr != root)
            {
                int next = parent[curr];
                parent[curr] = root;
                curr = next;
            }
            return root;
        }

        void Union(int a, int b)
        {
            int ra = Find(a);
            int rb = Find(b);
            if (ra != rb)
            {
                if (ra < rb) parent[rb] = ra;
                else parent[ra] = rb;
            }
        }

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int cell = row + x;
                int d = domain[cell];

                if (x > 0 && domain[cell - 1] == d)
                    Union(cell, cell - 1);

                if (y > 0)
                {
                    int up = cell - width;
                    if (domain[up] == d) Union(cell, up);
                    if (x > 0 && domain[up - 1] == d) Union(cell, up - 1);
                    if (x < width - 1 && domain[up + 1] == d) Union(cell, up + 1);
                }
            }
        }

        var seededRoots = new HashSet<int>();
        foreach (var seed in seeds)
        {
            int cell = seed.Y * width + seed.X;
            seededRoots.Add(Find(cell));
        }

        var unseededRoots = new Dictionary<int, int>();
        for (int i = 0; i < n; i++)
        {
            int r = Find(i);
            if (!seededRoots.Contains(r))
            {
                unseededRoots.TryAdd(r, i);
            }
        }

        int added = 0;
        foreach (var (_, cell) in unseededRoots)
        {
            seeds.Add(new ProvinceSeed
            {
                X = cell % width,
                Y = cell / width,
                IsLand = domain[cell] != ProvinceDomain.Water,
                Domain = domain[cell],
            });
            added++;
        }

        if (added > 0)
            Console.WriteLine($"  added {added} seeds to cover otherwise-unreachable components");
    }

    private static int[] Partition(int[] domainField, float[] elevation, int width, int height,
        MapConfig cfg, List<ProvinceSeed> seeds, ThreadBucketManager bucketManager)
    {
        int n = width * height;
        var state = new ulong[n];
        const ulong Unvisited = ((ulong)0x7F800000 << 32) | 0xFFFFFFFFUL;
        Array.Fill(state, Unvisited);

        float terrainCost = (float)Math.Max(0, cfg.ProvinceTerrainCost);
        double total = 0;
        int samples = 0;
        for (int i = width; i < n - width; i += 97)
        {
            if (domainField[i] == ProvinceDomain.Water || domainField[i - 1] == ProvinceDomain.Water) continue;
            total += Math.Abs(elevation[i] - elevation[i - 1]);
            samples++;
        }
        float invRef = samples == 0 || total <= 0 ? 0f : (float)(samples / (total * 3.0));

        bucketManager.Reset();

        var currentBucketItems = new int[Math.Max(1024, seeds.Count)];
        int currentBucketCount = 0;

        for (int i = 0; i < seeds.Count; i++)
        {
            int k = seeds[i].Y * width + seeds[i].X;
            state[k] = ((ulong)0 << 32) | (uint)i;
            if (currentBucketCount == currentBucketItems.Length)
                Array.Resize(ref currentBucketItems, currentBucketItems.Length * 2);
            currentBucketItems[currentBucketCount++] = k;
        }

        int activeBucket = 0;
        int maxBucket = 0;

        while (activeBucket <= maxBucket || currentBucketCount > 0)
        {
            if (currentBucketCount == 0)
            {
                activeBucket++;
                if (activeBucket > maxBucket) break;

                currentBucketCount = bucketManager.CollectBucket(activeBucket, ref currentBucketItems);
                if (currentBucketCount == 0) continue;
            }

            int count = currentBucketCount;
            currentBucketCount = 0;

            if (count < 128)
            {
                var local = bucketManager.Rent();
                for (int i = 0; i < count; i++)
                {
                    RelaxNode(currentBucketItems[i], activeBucket, width, height, domainField, elevation,
                        terrainCost, invRef, state, local, ref maxBucket);
                }
                bucketManager.Return(local);
            }
            else
            {
                // localInit/localFinally rather than a lookup inside the body: this is what
                // guarantees the bucket is held by exactly one worker for the life of its range.
                Parallel.ForEach(Partitioner.Create(0, count, 256),
                    bucketManager.Rent,
                    (range, _, local) =>
                    {
                        int localMaxBucket = activeBucket;

                        for (int i = range.Item1; i < range.Item2; i++)
                        {
                            RelaxNode(currentBucketItems[i], activeBucket, width, height, domainField, elevation,
                                terrainCost, invRef, state, local, ref localMaxBucket);
                        }

                        if (localMaxBucket > maxBucket)
                        {
                            InterlockedMax(ref maxBucket, localMaxBucket);
                        }

                        return local;
                    },
                    bucketManager.Return);
            }

            activeBucket++;
            if (activeBucket <= maxBucket)
            {
                currentBucketCount = bucketManager.CollectBucket(activeBucket, ref currentBucketItems);
            }
        }

        var label = new int[n];
        Parallel.For(0, height, y =>
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int cell = row + x;
                label[cell] = (int)(uint)state[cell];
            }
        });

        return label;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RelaxNode(int cell, int activeBucket, int width, int height,
        int[] domainField, float[] elevation, float terrainCost, float invRef,
        ulong[] state, ThreadLocalBucket local, ref int localMaxBucket)
    {
        ulong stateU = Volatile.Read(ref state[cell]);
        float distU = BitConverter.UInt32BitsToSingle((uint)(stateU >> 32));
        if ((int)distU != activeBucket) return;

        uint labelU = (uint)stateU;
        int domain = domainField[cell];
        float elevCell = elevation[cell];
        int x = cell % width;
        int y = cell / width;

        const float DiagonalCost = 1.41421356f;

        if (x > 0 && x < width - 1 && y > 0 && y < height - 1)
        {
            // 1. Left
            int nk = cell - 1;
            if (domainField[nk] == domain)
            {
                float nd = distU + (1f + terrainCost * MathF.Abs(elevation[nk] - elevCell) * invRef);
                TryRelax(nk, nd, labelU, state, local, ref localMaxBucket);
            }

            // 2. Right
            nk = cell + 1;
            if (domainField[nk] == domain)
            {
                float nd = distU + (1f + terrainCost * MathF.Abs(elevation[nk] - elevCell) * invRef);
                TryRelax(nk, nd, labelU, state, local, ref localMaxBucket);
            }

            // 3. Up
            nk = cell - width;
            if (domainField[nk] == domain)
            {
                float nd = distU + (1f + terrainCost * MathF.Abs(elevation[nk] - elevCell) * invRef);
                TryRelax(nk, nd, labelU, state, local, ref localMaxBucket);
            }

            // 4. Down
            nk = cell + width;
            if (domainField[nk] == domain)
            {
                float nd = distU + (1f + terrainCost * MathF.Abs(elevation[nk] - elevCell) * invRef);
                TryRelax(nk, nd, labelU, state, local, ref localMaxBucket);
            }

            // 5. Up-Left (-1, -1)
            nk = cell - width - 1;
            if (domainField[nk] == domain && domainField[cell - 1] == domain && domainField[cell - width] == domain)
            {
                float nd = distU + DiagonalCost * (1f + terrainCost * MathF.Abs(elevation[nk] - elevCell) * invRef);
                TryRelax(nk, nd, labelU, state, local, ref localMaxBucket);
            }

            // 6. Up-Right (1, -1)
            nk = cell - width + 1;
            if (domainField[nk] == domain && domainField[cell + 1] == domain && domainField[cell - width] == domain)
            {
                float nd = distU + DiagonalCost * (1f + terrainCost * MathF.Abs(elevation[nk] - elevCell) * invRef);
                TryRelax(nk, nd, labelU, state, local, ref localMaxBucket);
            }

            // 7. Down-Left (-1, 1)
            nk = cell + width - 1;
            if (domainField[nk] == domain && domainField[cell - 1] == domain && domainField[cell + width] == domain)
            {
                float nd = distU + DiagonalCost * (1f + terrainCost * MathF.Abs(elevation[nk] - elevCell) * invRef);
                TryRelax(nk, nd, labelU, state, local, ref localMaxBucket);
            }

            // 8. Down-Right (1, 1)
            nk = cell + width + 1;
            if (domainField[nk] == domain && domainField[cell + 1] == domain && domainField[cell + width] == domain)
            {
                float nd = distU + DiagonalCost * (1f + terrainCost * MathF.Abs(elevation[nk] - elevCell) * invRef);
                TryRelax(nk, nd, labelU, state, local, ref localMaxBucket);
            }
        }
        else
        {
            foreach (var (dx, dy, cost, diagonal) in Dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int nk = ny * width + nx;
                if (domainField[nk] != domain) continue;

                if (diagonal)
                {
                    if (domainField[y * width + nx] != domain) continue;
                    if (domainField[ny * width + x] != domain) continue;
                }

                float nd = distU + cost * (1f + terrainCost * MathF.Abs(elevation[nk] - elevCell) * invRef);
                TryRelax(nk, nd, labelU, state, local, ref localMaxBucket);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TryRelax(int nk, float nd, uint labelU, ulong[] state,
        ThreadLocalBucket local, ref int localMaxBucket)
    {
        uint ndBits = BitConverter.SingleToUInt32Bits(nd);
        ulong newVal = ((ulong)ndBits << 32) | labelU;
        ulong curVal = Volatile.Read(ref state[nk]);

        while (newVal < curVal)
        {
            ulong prev = Interlocked.CompareExchange(ref state[nk], newVal, curVal);
            if (prev == curVal)
            {
                int targetB = (int)nd;
                local.Add(targetB, nk);
                if (targetB > localMaxBucket) localMaxBucket = targetB;
                break;
            }
            curVal = prev;
        }
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int initial, current;
        do
        {
            initial = location;
            current = Math.Max(initial, value);
        } while (Interlocked.CompareExchange(ref location, current, initial) != initial);
    }

    private static int PlaceMajorRiverSeeds(
                List<ProvinceSeed> seeds,
                List<MajorRiverPath> rivers,
                byte[] mask,
                int width,
                int height,
                MapConfig cfg)
    {
        double segmentLength = Math.Max(1.0, cfg.Scaled(cfg.RiverProvinceLength));
        int added = 0;

        foreach (var river in rivers)
        {
            var pts = river.Points;
            if (pts.Count < 2) continue;

            // Only place river province seeds where the river is navigable (t >= 0.20),
            // not at the tapered dry tip — unless the course leaves a lake, in which case it
            // has no dry tip and is navigable from its first point.
            int startIndex = river.SourceIsWater ? 1 : Math.Max(1, (int)(pts.Count * 0.20f));
            double accumulated = segmentLength * 0.5;

            for (int i = startIndex; i < pts.Count; i++)
            {
                float dx = pts[i].X - pts[i - 1].X;
                float dy = pts[i].Y - pts[i - 1].Y;
                accumulated += MathF.Sqrt(dx * dx + dy * dy);

                if (accumulated >= segmentLength)
                {
                    accumulated = 0;
                    int sx = Math.Clamp((int)MathF.Round(pts[i].X), 0, width - 1);
                    int sy = Math.Clamp((int)MathF.Round(pts[i].Y), 0, height - 1);

                    int cell = sy * width + sx;

                    // Ensure the mask registers this seed location as water
                    mask[cell] = 0;

                    seeds.Add(new ProvinceSeed
                    {
                        X = sx,
                        Y = sy,
                        IsLand = false,
                        IsMajorRiver = true,
                        IsImpassable = false,
                    });
                    added++;
                }
            }
        }

        return added;
    }
    /// <summary>
    /// Hands every pixel the partition never reached to a neighbour that was.
    ///
    /// Pixels are left over because the seed-coverage check and the growth disagree slightly about
    /// what "connected" means: the check unions diagonally, while growth refuses to cut a corner
    /// unless both orthogonal neighbours are in the same domain. Ground reachable only through a
    /// corner therefore has a seed guaranteed somewhere in its component but no path from it.
    ///
    /// The flood is restricted to the pixel's own domain first, which is what keeps the border
    /// constraint true of the finished map rather than merely of the partition — an unreached
    /// pixel filled from whichever neighbour happened to be nearest would quietly hand a scrap of
    /// one Azgaar province to a barony belonging to another. Only pixels with no labelled
    /// same-domain neighbour anywhere in reach fall through to the unrestricted pass, which cannot
    /// happen while every domain component holds a seed, and is reported when it does.
    /// </summary>
    private static void RepairUnlabeled(ProvinceMap map, int[] domain)
    {
        var label = map.Label;
        if (Array.IndexOf(label, -1) < 0) return;

        int unlabeled = 0;
        for (int i = 0; i < label.Length; i++) if (label[i] < 0) unlabeled++;
        if (unlabeled == 0) return;

        int filled = Flood(matchDomain: true);
        int strays = unlabeled - filled;
        if (strays > 0) Flood(matchDomain: false);

        Console.WriteLine($"  repaired {unlabeled} unlabeled pixels" +
                          (strays > 0 ? $" ({strays} had no same-domain neighbour)" : ""));
        return;

        int Flood(bool matchDomain)
        {
            var queue = new List<int>();
            for (int i = 0; i < label.Length; i++)
                if (label[i] >= 0) queue.Add(i);

            int taken = 0;
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
                    if (matchDomain && domain[nk] != map.Seeds[label[cell]].Domain) continue;

                    label[nk] = label[cell];
                    queue.Add(nk);
                    taken++;
                }
            }

            return taken;
        }
    }

    /// <summary>
    /// Checks that no barony ended up spanning two of Azgaar's provinces, and says so if one did.
    ///
    /// This is the assertion the whole hard-constraint approach exists to make possible. A penalty
    /// in the cost field can only be eyeballed; a partition either respects the borders or it does
    /// not, and the difference is one pass over the labels.
    ///
    /// Only land provinces are examined, and only the Azgaar province behind each pixel — water is
    /// skipped rather than treated as a second domain. That is the question rather than a loophole:
    /// a county's border is wrong when it takes ground from the state next door, and unbothered by
    /// a sea pixel <see cref="DissolveTinyProvinces"/> folded into the shoreline. Counting water
    /// reported three hundred failures on a map with none of the kind that matters — they were the
    /// drowned islets, which are meant to stop being land at all.
    ///
    /// Reported rather than thrown because a few stray pixels are a blemish rather than a reason to
    /// lose the run, but anything above zero means a tidy-up pass is still moving pixels across a
    /// border it should be treating as a wall.
    /// </summary>
    private static void VerifyDomains(ProvinceMap map, int[] domain)
    {
        var first = new int[map.Count];
        Array.Fill(first, ProvinceDomain.Water);

        var impure = new HashSet<int>();
        long strayPixels = 0;

        for (int i = 0; i < map.Label.Length; i++)
        {
            int id = map.Label[i];
            if (id < 0 || !map.Seeds[id].IsLand) continue;

            int d = domain[i];
            if (d == ProvinceDomain.Water) continue;

            if (first[id] == ProvinceDomain.Water) { first[id] = d; continue; }
            if (first[id] == d) continue;

            strayPixels++;
            impure.Add(id);
        }

        int land = 0;
        for (int i = 0; i < map.Count; i++) if (map.Seeds[i].IsLand) land++;

        if (impure.Count == 0)
        {
            Console.WriteLine($"  domain check: all {land} land provinces sit inside one region " +
                              "(azgaar province / painted wall)");
            return;
        }

        Console.WriteLine($"  ! domain check: {impure.Count} of {land} land provinces straddle a " +
                          $"region border ({strayPixels} px)");
    }
}

/// <summary>
/// Hands out per-worker frontier buckets for <see cref="Provinces.Partition"/> and drains them
/// between waves.
///
/// Buckets are *rented*, not looked up by thread identity. The earlier form indexed a fixed array
/// by <c>Environment.CurrentManagedThreadId % ProcessorCount</c>, which is not a thread-local at
/// all: managed thread ids are arbitrary and unbounded, so two live Parallel.ForEach workers
/// collide on one slot as a matter of course. <see cref="ThreadLocalBucket"/> has no
/// synchronisation, so a collision means lost updates on the count, a stale array reference
/// surviving an <c>Array.Resize</c>, and finally a count that outruns its own backing array —
/// which surfaces as an IndexOutOfRangeException somewhere in the relaxation loop, on big maps,
/// intermittently. Renting gives each concurrent worker an object nobody else holds, which is the
/// invariant the bucket type was written against.
/// </summary>
internal sealed class ThreadBucketManager
{
    /// <summary>Every bucket ever created, rented or not. Drained by <see cref="CollectBucket"/>.</summary>
    private readonly List<ThreadLocalBucket> _all = [];

    /// <summary>Buckets not currently held by a worker.</summary>
    private readonly ConcurrentBag<ThreadLocalBucket> _free = [];

    private readonly object _gate = new();

    /// <summary>
    /// Takes a bucket no other worker holds. Cheap after the first wave, since workers return
    /// theirs and the pool settles at the degree of parallelism the loop actually reached.
    /// </summary>
    public ThreadLocalBucket Rent()
    {
        if (_free.TryTake(out var bucket)) return bucket;

        bucket = new ThreadLocalBucket();
        lock (_gate) _all.Add(bucket);
        return bucket;
    }

    /// <summary>Releases a bucket back to the pool. Its contents are kept for the next collect.</summary>
    public void Return(ThreadLocalBucket bucket) => _free.Add(bucket);

    /// <summary>
    /// Concatenates and clears every bucket's entries for one distance band. Called from the
    /// sequential driver between parallel waves, so no worker holds a bucket here.
    /// </summary>
    public int CollectBucket(int bucketIdx, ref int[] targetBuffer)
    {
        lock (_gate)
        {
            int total = 0;
            foreach (var local in _all)
                total += local.GetCount(bucketIdx);

            if (total == 0) return 0;

            if (total > targetBuffer.Length)
                Array.Resize(ref targetBuffer, Math.Max(total, targetBuffer.Length * 2));

            int offset = 0;
            foreach (var local in _all)
            {
                int count = local.GetCount(bucketIdx);
                if (count > 0)
                {
                    local.CopyAndClear(bucketIdx, targetBuffer, offset);
                    offset += count;
                }
            }

            return total;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            foreach (var local in _all) local.Reset();
        }
    }
}

internal sealed class ThreadLocalBucket
{
    private int[][] _buckets = new int[256][];
    private int[] _counts = new int[256];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(int bucketIdx, int cell)
    {
        if (bucketIdx >= _counts.Length)
        {
            Grow(bucketIdx);
        }

        int count = _counts[bucketIdx];
        var arr = _buckets[bucketIdx];
        if (arr is null)
        {
            arr = _buckets[bucketIdx] = new int[32];
        }
        else if (count == arr.Length)
        {
            Array.Resize(ref _buckets[bucketIdx], count * 2);
            arr = _buckets[bucketIdx];
        }

        arr[count] = cell;
        _counts[bucketIdx] = count + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int minCapacity)
    {
        int newCap = Math.Max(minCapacity + 1, _counts.Length * 2);
        Array.Resize(ref _buckets, newCap);
        Array.Resize(ref _counts, newCap);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCount(int bucketIdx) => (bucketIdx < _counts.Length) ? _counts[bucketIdx] : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyAndClear(int bucketIdx, int[] target, int targetOffset)
    {
        int count = _counts[bucketIdx];
        if (count > 0)
        {
            Array.Copy(_buckets[bucketIdx], 0, target, targetOffset, count);
            _counts[bucketIdx] = 0;
        }
    }

    public void Reset()
    {
        Array.Clear(_counts, 0, _counts.Length);
    }
}