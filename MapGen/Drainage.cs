using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Where the water goes: a depression-filled surface, a downslope receiver for every land cell, and
/// the discharge that accumulates along those receivers.
///
/// This is the layer both river features are built on, and neither is here yet. rivers.png needs
/// courses — chains of cells above a discharge gate, traced to an outlet — and the major rivers of
/// default.map need a subset of those cut below the waterline. Both are selections over this; the
/// hard part, and the part that failed last time, is the network itself.
///
/// **Every land cell drains to an outlet, by construction rather than by luck.** That was the
/// stated failure of the hydrology removed on 2026-08-10 — courses that wandered and never reached
/// the sea — so it is worth being exact about why it cannot happen here. Priority-flood pops cells
/// in non-decreasing filled height starting from the water and the map edge, so the surface it
/// leaves has no interior minimum: every land cell either has a strictly lower neighbour on
/// <see cref="Filled"/>, or sits on a filled lake surface and keeps the receiver the flood itself
/// arrived from. Order the cells by (filled height, pop index) and both kinds of receiver strictly
/// decrease that pair, so a receiver chain cannot cycle and cannot stop anywhere but a seed.
///
/// **Discharge is in cells of this map, of median-wetness runoff — and that IS the physical unit.**
/// This was briefly denominated in vanilla cells, dividing by <see cref="MapConfig.MapScale"/>
/// squared so that the same heightmap resampled reported the same catchment. That is the wrong
/// invariant for this tool. CK3 puts `WORLD_EXTENTS_X/Z` in provinces-map pixels and we write them
/// from our own raster, so a province cell is a fixed piece of world on every map and a smaller
/// heightmap is a smaller *region*, not the same region drawn coarsely. Under that model a
/// catchment of N cells is the same physical basin whatever the map, a gate written once means one
/// thing forever, and a map covering a quarter of the world gets a quarter of the rivers — which is
/// what it should get.
///
/// The trap the old code fell into is still real, just not the one it looked like: measured on a
/// 16384x8192 heightmap, a fixed 200,000-cell gate gave 55 navigable rivers, 400,000 gave 13 and
/// 800,000 gave none. That is a gate calibrated against the wrong map, not an argument for
/// rescaling the unit. Calibrate against vanilla's own province map — 9216x4608 — once.
///
/// The wetness half of that unit is why <see cref="Build"/> takes rainfall. Pure catchment area
/// puts the map's great rivers wherever the basins are largest, which on a dry continent is a wadi;
/// weighting each cell by its rainfall against the land median makes a cell of ordinary wetness
/// count as exactly one cell and a desert cell count as almost nothing, so the Nile still runs
/// (its water is collected upstream) but the Sahara around it does not sprout rivers of its own.
///
/// Runs at province resolution, not heightmap resolution. That is where rivers.png lives and where
/// vanilla's own one-pixel courses are drawn, it is half the memory and a quarter of the work, and
/// a course is kept as cells here rather than as a heightmap-space curve precisely so it can be
/// rasterised into either grid later. Note the cost regardless: five full-size arrays, about 700 MB
/// at vanilla's 9216x4608 province map.
/// </summary>
public sealed class Drainage
{
    // Eight-connected. The map is a flat rectangle and never wraps in X — see Field's note — and
    // the border is a seed anyway, so out-of-range neighbours are simply skipped.
    private static readonly int[] Dx = [-1, 0, 1, -1, 1, -1, 0, 1];
    private static readonly int[] Dy = [-1, -1, -1, 0, 0, 1, 1, 1];

    /// <summary>1/distance per neighbour, so a diagonal step is not counted as steep as it looks.</summary>
    private static readonly float[] InvDist =
    [
        0.70710678f, 1f, 0.70710678f,
        1f, 1f,
        0.70710678f, 1f, 0.70710678f,
    ];

    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>
    /// Elevation with every depression raised to the level of its lowest escape. Equal to the input
    /// everywhere else, so <c>Filled[i] - elevation[i]</c> is the depth of standing water at i — the
    /// lake map, free of charge, and the field a rebuilt lake feature would be selected from.
    /// </summary>
    public required float[] Filled { get; init; }

    /// <summary>
    /// The cell each cell drains into. A seed — any water cell, and any cell on the map border —
    /// receives itself, which is what terminates every chain.
    /// </summary>
    public required int[] Receiver { get; init; }

    /// <summary>
    /// Every cell, in the order the flood reached it, which is non-decreasing in <see cref="Filled"/>.
    /// A receiver is always earlier in this order than the cell that drains into it, so walking it
    /// backwards accumulates the whole map in one pass and walking it forwards propagates anything
    /// downstream — which is how a later course extraction should traverse, rather than by sorting.
    /// </summary>
    public required int[] Order { get; init; }

    /// <summary>
    /// Discharge through each cell, in province cells of median-wetness runoff. On a water cell
    /// this is what the land delivers to it, so a river mouth is the coastal water cell with the
    /// large number on it.
    ///
    /// Single precision on purpose: at vanilla size a continental trunk reaches roughly 2e7, where
    /// float's step is 2, so the last stretch of the largest basin on the map undercounts by well
    /// under a tenth of a percent. Double would cost another 170 MB to fix a rounding error smaller
    /// than the choice of gate.
    /// </summary>
    public required float[] Flow { get; init; }

    /// <summary>The land mask the network was built against, so consumers cannot disagree with it.</summary>
    public required byte[] LandMask { get; init; }

    public bool IsLand(int i) => LandMask[i] != 0;

    /// <summary>Depth of standing water at a cell, from the fill. Zero on dry ground.</summary>
    public float LakeDepth(float[] elevation, int i) => Math.Max(0f, Filled[i] - elevation[i]);

    /// <summary>
    /// Builds the network. Deterministic — no <c>Rng</c>, and none wanted: which way water runs off
    /// a given heightmap is not a question with a random answer. The randomness a river system needs
    /// belongs to the courses drawn on top of this, where meander and width live.
    /// </summary>
    /// <param name="runoffMm">
    /// Per-cell annual rainfall at province resolution, normally
    /// <see cref="ClimateField.AnnualMm"/>. Null weights every cell equally, which makes
    /// <see cref="Flow"/> plain catchment area.
    /// </param>
    public static Drainage Build(MapConfig cfg, float[] elevation, byte[] landMask,
        float[]? runoffMm = null)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int n = width * height;

        if (elevation.Length != n || landMask.Length != n)
            throw new ArgumentException(
                $"Drainage expects province-resolution fields ({width}x{height} = {n} cells); got " +
                $"elevation {elevation.Length} and land mask {landMask.Length}.");

        var filled = new float[n];
        var receiver = new int[n];
        var order = new int[n];

        Flood(elevation, landMask, width, height, filled, receiver, order);
        SteepestDescent(landMask, width, height, filled, receiver);

        var flow = Accumulate(landMask, filled.Length, order, receiver,
            Weights(runoffMm, landMask));

        var drainage = new Drainage
        {
            Width = width,
            Height = height,
            Filled = filled,
            Receiver = receiver,
            Order = order,
            Flow = flow,
            LandMask = landMask,
        };

        drainage.Report(elevation);
        return drainage;
    }

    /// <summary>
    /// Priority-flood from the water and the map edge inward, in Barnes' two-queue form: cells that
    /// are drowned by the level already reached go on a plain FIFO instead of the heap, because
    /// their filled height is known without comparing it to anything. On a real heightmap most of
    /// the map takes that path, which is what keeps this near linear.
    ///
    /// The guard on the plain queue matters and is easy to leave out. Draining it unconditionally is
    /// only correct while its cells sit at or below the heap's next height; past that the flood
    /// would run uphill ahead of a lower cell still waiting on the heap and fill a basin that had a
    /// lower escape.
    ///
    /// <paramref name="receiver"/> comes out of this holding the cell each cell was *discovered*
    /// from. That is a valid downhill route to an outlet everywhere — the flood expands outward from
    /// the outlets — but it is only the *right* route on flat ground, where there is nothing to
    /// choose between. <see cref="SteepestDescent"/> overwrites the rest.
    /// </summary>
    private static void Flood(float[] elevation, byte[] landMask, int width, int height,
        float[] filled, int[] receiver, int[] order)
    {
        var closed = new bool[filled.Length];
        var open = new PriorityQueue<int, float>();
        var pit = new Queue<int>();

        // Water is where land drains to. The border is a seed as well because a cell at the edge has
        // nowhere else to go, and vanilla's map is ocean along every edge anyway — see
        // MapConfig.OceanBorder, which normally makes this moot by drowning the margin outright.
        for (int y = 0; y < height; y++)
        {
            bool edgeRow = y == 0 || y == height - 1;
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (landMask[i] != 0 && !edgeRow && x != 0 && x != width - 1) continue;

                filled[i] = elevation[i];
                receiver[i] = i;
                closed[i] = true;
                open.Enqueue(i, filled[i]);
            }
        }

        int popped = 0;
        while (open.Count > 0 || pit.Count > 0)
        {
            int c;
            if (pit.Count > 0 && (open.Count == 0 || NextHeight(open) >= filled[pit.Peek()]))
                c = pit.Dequeue();
            else
                c = open.Dequeue();

            order[popped++] = c;

            int cx = c % width, cy = c / width;
            float level = filled[c];

            for (int k = 0; k < 8; k++)
            {
                int nx = cx + Dx[k], ny = cy + Dy[k];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int nb = ny * width + nx;
                if (closed[nb]) continue;

                closed[nb] = true;
                receiver[nb] = c;

                if (elevation[nb] <= level)
                {
                    // Under the level the water has already reached, so it is drowned to exactly
                    // that level and needs no comparison against anything else.
                    filled[nb] = level;
                    pit.Enqueue(nb);
                }
                else
                {
                    filled[nb] = elevation[nb];
                    open.Enqueue(nb, filled[nb]);
                }
            }
        }

        if (popped != filled.Length)
            throw new InvalidOperationException(
                $"Drainage flood reached {popped} of {filled.Length} cells. Every cell should be " +
                "reachable from the map border, so this means the neighbourhood or the seeding is wrong.");

        static float NextHeight(PriorityQueue<int, float> open)
        {
            open.TryPeek(out _, out float priority);
            return priority;
        }
    }

    /// <summary>
    /// Replaces the flood's discovery route with steepest descent wherever there is a descent to be
    /// steepest — that is, everywhere except a filled lake surface, where every neighbour is exactly
    /// level and the drop test therefore keeps the route the flood arrived by. That route leads to
    /// the lake's outlet, which is the answer wanted.
    ///
    /// A strict inequality is what makes those two cases disjoint, and it is also what keeps the
    /// receivers acyclic: a descent receiver is strictly lower, a flood receiver is no higher and
    /// strictly earlier in the pop order, so the pair (height, pop index) falls at every step.
    /// </summary>
    private static void SteepestDescent(byte[] landMask, int width, int height,
        float[] filled, int[] receiver)
    {
        Parallel.For(0, height, y =>
        {
            bool edgeRow = y == 0 || y == height - 1;
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                // Seeds keep pointing at themselves; that self-reference is what ends a chain.
                if (landMask[i] == 0 || edgeRow || x == 0 || x == width - 1) continue;

                float here = filled[i];
                float best = 0f;
                int into = -1;

                for (int k = 0; k < 8; k++)
                {
                    int nb = (y + Dy[k]) * width + x + Dx[k];
                    float drop = (here - filled[nb]) * InvDist[k];
                    if (drop <= best) continue;
                    best = drop;
                    into = nb;
                }

                if (into >= 0) receiver[i] = into;
            }
        });
    }

    /// <summary>
    /// Per-cell runoff weight: rainfall against the land median, so a cell of ordinary wetness is
    /// worth exactly one cell and the unit on <see cref="Flow"/> stays readable. Null rainfall gives
    /// every cell a 1 and turns discharge back into plain catchment area.
    ///
    /// Capped rather than taken raw. The ratio on a real climate field runs about 0.05 to 3, so the
    /// cap never binds; it is there so that a degenerate rainfall field — one cell holding the whole
    /// map's water after some future change to the climate model — cannot silently become the only
    /// river on the map.
    /// </summary>
    private static float[]? Weights(float[]? runoffMm, byte[] landMask)
    {
        if (runoffMm is null) return null;

        float median = Field.Quantile(runoffMm, i => landMask[i] != 0, 0.5);
        if (median <= 0f) return null;

        var weight = new float[runoffMm.Length];
        Parallel.For(0, weight.Length, i =>
            weight[i] = Math.Clamp(runoffMm[i] / median, 0f, 8f));
        return weight;
    }

    /// <summary>
    /// Discharge, by walking the flood order backwards. Every cell is visited after everything that
    /// drains into it, so one pass is exact and no sort or repeated relaxation is needed.
    ///
    /// Only land generates runoff — rain on the sea is not a river — but the sea still receives, so
    /// the flow left on a coastal water cell is the discharge of whatever empties there.
    /// </summary>
    private static float[] Accumulate(byte[] landMask, int n, int[] order, int[] receiver,
        float[]? weight)
    {
        var flow = new float[n];

        Parallel.For(0, n, i => flow[i] = landMask[i] == 0 ? 0f : weight?[i] ?? 1f);

        for (int k = n - 1; k >= 0; k--)
        {
            int c = order[k];
            int into = receiver[c];
            if (into != c) flow[into] += flow[c];
        }

        return flow;
    }

    /// <summary>
    /// What the network came out as, in the units a gate would be written in.
    ///
    /// The discharge quantiles are the useful line, and the number to calibrate a gate against is
    /// the one this prints on a *vanilla-sized* map, because a cell is a fixed piece of world and
    /// only that map has a world the size vanilla's numbers were authored for. The drowned share is
    /// the other half — a map that fills 30% of its land is telling you the heightmap has no
    /// drainage in it, not that the flood is broken.
    /// </summary>
    private void Report(float[] elevation)
    {
        long land = 0, drowned = 0, stranded = 0;
        float deepest = 0f;

        for (int i = 0; i < Filled.Length; i++)
        {
            if (LandMask[i] == 0) continue;
            land++;

            float depth = Filled[i] - elevation[i];
            if (depth > 0f)
            {
                drowned++;
                if (depth > deepest) deepest = depth;
            }

            // A land cell that receives itself is not on the border, so it is an interior minimum
            // the flood failed to route out of. Should be structurally impossible; counted rather
            // than asserted because the number is the diagnosis if it ever stops being zero.
            if (Receiver[i] == i) stranded++;
        }

        if (land == 0)
        {
            Console.WriteLine("  drainage: no land");
            return;
        }

        Console.WriteLine($"  drainage: {land / 1e6:F1}M land cells, " +
                          $"{100.0 * drowned / land:F1}% drowned by fill (deepest {deepest:F1})");

        var (quantiles, peak) = FlowQuantiles([0.50, 0.90, 0.99, 0.999]);
        var text = quantiles.Select((v, k) => $"p{new[] { 50, 90, 99, 99.9 }[k]:0.#} {v:N0}");

        Console.WriteLine($"  discharge on land (cells of catchment): {string.Join(", ", text)}, " +
                          $"max {peak:N0}");

        if (stranded > 0)
            Console.WriteLine($"  WARNING: {stranded} land cells drain nowhere. The flood should " +
                              "make this impossible.");
    }

    /// <summary>
    /// Quantiles of land discharge, binned on a log scale, plus the largest river on the map.
    ///
    /// Not <see cref="Field.Quantile"/>, which bins linearly. Discharge spans six decades — one cell
    /// of runoff at the top of a hill against a continental trunk at the mouth — so a linear
    /// histogram puts everything below the top thousandth into its first bin and reports the median
    /// of a map full of streams as zero. Which is exactly what it did, and the number a gate would
    /// be read off is in that flattened end.
    /// </summary>
    private (double[] Values, float Peak) FlowQuantiles(double[] at)
    {
        // 0.01 of a decade per bin, over 1 to 100 million cells. The top is above any catchment a
        // CK3-sized map can hold; anything below the bottom is a single cell of runoff.
        const int bins = 800;
        const double perDecade = 100.0;

        var histogram = new long[bins + 1];
        long total = 0;
        float peak = 0f;

        for (int i = 0; i < Flow.Length; i++)
        {
            if (LandMask[i] == 0) continue;
            total++;
            if (Flow[i] > peak) peak = Flow[i];

            int bin = Flow[i] <= 1f ? 0
                : Math.Min(bins, 1 + (int)(Math.Log10(Flow[i]) * perDecade));
            histogram[bin]++;
        }

        var values = new double[at.Length];
        if (total == 0) return (values, peak);

        int cursor = 0;
        long running = 0;
        for (int bin = 0; bin <= bins && cursor < at.Length; bin++)
        {
            running += histogram[bin];
            while (cursor < at.Length && running >= (long)(total * at[cursor]))
                values[cursor++] = bin == 0 ? 0 : Math.Pow(10, bin / perDecade);
        }

        while (cursor < at.Length) values[cursor++] = peak;
        return (values, peak);
    }

    // --- Viewing ---
    //
    // The ramp anchors live here rather than in either renderer because they are statements about
    // discharge, not about drawing, and because the GUI preview and the CLI dump have to agree or
    // the two views of the same map are not comparable.
    //
    // Both are in cells of catchment, which is a fixed physical area, so the same colour is the same
    // size of river on every map — and a map covering less world shows fewer of them, which is the
    // point rather than a defect. Read that way, this view is also the calibration instrument: a
    // gate belongs somewhere between these two anchors, and where it belongs is a judgement about
    // what a vanilla-sized map should look like.

    /// <summary>Discharge at which a cell starts being drawn as watercourse rather than as ground.
    /// Below the ~900 the old drawn-river gate used, deliberately: the point of the view is to see
    /// the network feeding the rivers, not only the rivers.</summary>
    private const float FlowFloor = 200f;

    /// <summary>Discharge at which the ramp saturates — major-river scale, three decades up.</summary>
    private const float FlowCeiling = 200_000f;

    /// <summary>Position on the discharge ramp, 0 below the floor and 1 at the ceiling.</summary>
    public double FlowFraction(int i)
    {
        float f = Flow[i];
        if (f <= FlowFloor) return 0;
        return Math.Clamp(Math.Log(f / FlowFloor) / Math.Log(FlowCeiling / FlowFloor), 0, 1);
    }

    /// <summary>
    /// How strongly a cell should win its block when the view is downsampled. A watercourse is one
    /// cell wide and a minority inside every block it crosses, so a plain point sample dots it or
    /// loses it; resolving each block to its highest discharge keeps the network connected and keeps
    /// a trunk from being replaced by a tributary that shares the block.
    /// </summary>
    public int ViewRank(int i) => LandMask[i] == 0 ? 0 : 1 + (int)(4096 * FlowFraction(i));

    /// <summary>
    /// One cell as it is drawn in both the GUI's Drainage tab and debug_drainage.png: ground shaded
    /// by height, standing water from the fill in a flat teal, and discharge as a cyan-to-white
    /// ramp over it.
    ///
    /// The three are separable on purpose. A basin that should have drained but merely filled reads
    /// as a teal blob with a course leaving it; a course that stops in open country reads as a ramp
    /// that fades on dry ground, which is the failure the last river generator shipped with and the
    /// one thing a finished rivers.png would have hidden.
    /// </summary>
    public (byte R, byte G, byte B) Shade(float[] elevation, MapConfig cfg, int i)
    {
        if (LandMask[i] == 0) return (26, 42, 68);

        float sea = cfg.Limits.SeaLevelUpper;
        float peak = Math.Max(sea + 1f, cfg.PeakElevation);
        double height = Math.Clamp((elevation[i] - sea) / (peak - sea), 0, 1);

        double r = 58 + 150 * height, g = 62 + 150 * height, b = 66 + 148 * height;

        // Half a unit of standing water, against a land range of several hundred, is well clear of
        // the fill's own noise and still catches a shallow pan.
        if (Filled[i] - elevation[i] > 0.5f)
        {
            r = r * 0.35 + 62 * 0.65;
            g = g * 0.35 + 118 * 0.65;
            b = b * 0.35 + 128 * 0.65;
        }

        double t = FlowFraction(i);
        if (t > 0)
        {
            r = r * (1 - t) + (90 + 150 * t) * t;
            g = g * (1 - t) + (150 + 102 * t) * t;
            b = b * (1 - t) + (215 + 40 * t) * t;
        }

        return ((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
    }
}
