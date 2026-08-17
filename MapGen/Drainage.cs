using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public sealed class Drainage
{
    private static readonly int[] Dx = [-1, 0, 1, -1, 1, -1, 0, 1];
    private static readonly int[] Dy = [-1, -1, -1, 0, 0, 1, 1, 1];
    private static readonly float[] InvDist =
    [
        0.70710678f, 1f, 0.70710678f,
        1f, 1f,
        0.70710678f, 1f, 0.70710678f,
    ];

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required float[] Filled { get; init; }
    public required int[] Receiver { get; init; }
    public required float[] Flow { get; init; }
    public required byte[] LandMask { get; init; }

    /// <summary>
    /// The elevation the drainage was solved from, kept so <see cref="LakeDepth(int)"/> can be
    /// asked without the caller having to supply the right array.
    /// </summary>
    public required float[] Source { get; init; }

    public bool IsLand(int i) => LandMask[i] != 0;

    /// <summary>
    /// How deep the fill stands over the original ground — the depth of a closed depression, and
    /// zero wherever the terrain already drained.
    ///
    /// Prefer this overload. The one taking an explicit array is easy to call with
    /// <see cref="Filled"/> itself, which computes <c>Filled[i] - Filled[i]</c> and is therefore
    /// identically zero, so a guard written against it silently never fires. That is exactly what
    /// had happened to the lake-basin stop in <see cref="MajorRivers"/>.
    /// </summary>
    public float LakeDepth(int i) => Math.Max(0f, Filled[i] - Source[i]);

    public float LakeDepth(float[] elevation, int i) => Math.Max(0f, Filled[i] - elevation[i]);

    public static Drainage Build(MapConfig cfg, float[] elevation, byte[] landMask,
        float[]? runoffMm = null, Rng? rng = null)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int n = width * height;
        rng ??= new Rng(cfg.Seed);

        var noise = new SimplexNoise(rng);

        // 1. Add subtle macroscopic valley contours to break planar flatness
        var contouredElev = new float[n];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float e = elevation[i];
                if (landMask[i] == 0)
                {
                    contouredElev[i] = e;
                    continue;
                }
                // Low-frequency gentle undulation
                double n1 = noise.Noise2D(x * 0.008, y * 0.008) * 4.0;
                double n2 = noise.Noise2D(x * 0.03 + 17.1, y * 0.03 + 9.3) * 1.5;
                contouredElev[i] = (float)(e + n1 + n2);
            }
        });

        // 2. Monotonic Priority Flood (guarantees every cell has a strictly lower downhill route)
        var filled = new float[n];
        var receiver = new int[n];
        FloodMonotonic(contouredElev, landMask, width, height, filled, receiver);

        // 3. Exact In-Degree Topological Flow Accumulation
        var flow = AccumulateTopological(landMask, width, height, receiver, Weights(runoffMm, landMask));

        var drainage = new Drainage
        {
            Width = width,
            Height = height,
            Filled = filled,
            Receiver = receiver,
            Flow = flow,
            LandMask = landMask,
            Source = elevation,
        };

        drainage.Report(elevation);
        return drainage;
    }

    private static void FloodMonotonic(
        float[] elevation, byte[] landMask, int width, int height,
        float[] filled, int[] receiver)
    {
        int n = width * height;
        var closed = new bool[n];
        var open = new PriorityQueue<int, double>();
        const double Epsilon = 1e-5;

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

        while (open.Count > 0)
        {
            int c = open.Dequeue();
            int cx = c % width, cy = c / width;
            double curLevel = filled[c];

            for (int k = 0; k < 8; k++)
            {
                int nx = cx + Dx[k], ny = cy + Dy[k];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int nb = ny * width + nx;
                if (closed[nb]) continue;

                closed[nb] = true;
                receiver[nb] = c;

                double nextLevel = elevation[nb];
                if (nextLevel <= curLevel)
                {
                    // Slightly lift flooded depression so water slopes strictly towards outlet
                    nextLevel = curLevel + Epsilon;
                }

                filled[nb] = (float)nextLevel;
                open.Enqueue(nb, nextLevel);
            }
        }
    }

    private static float[] AccumulateTopological(
        byte[] landMask, int width, int height, int[] receiver, float[]? weight)
    {
        int n = width * height;
        var flow = new float[n];
        var inDegree = new int[n];

        Parallel.For(0, n, i =>
        {
            flow[i] = landMask[i] == 0 ? 0f : (weight?[i] ?? 1f);
            int into = receiver[i];
            if (into != i && landMask[i] != 0)
            {
                Interlocked.Increment(ref inDegree[into]);
            }
        });

        // Queue all leaf cells with no upstream contributors
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (landMask[i] != 0 && inDegree[i] == 0)
            {
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int into = receiver[c];
            if (into == c) continue;

            flow[into] += flow[c];

            if (--inDegree[into] == 0 && landMask[into] != 0)
            {
                queue.Enqueue(into);
            }
        }

        return flow;
    }

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
            if (Receiver[i] == i) stranded++;
        }

        Console.WriteLine($"  drainage: {land / 1e6:F1}M land cells, {100.0 * drowned / Math.Max(1, land):F1}% filled");

        var (quantiles, peak) = FlowQuantiles([0.50, 0.90, 0.99, 0.999]);
        var text = quantiles.Select((v, k) => $"p{new[] { 50, 90, 99, 99.9 }[k]:0.#} {v:N0}");
        Console.WriteLine($"  discharge on land: {string.Join(", ", text)}, max {peak:N0}");
    }

    private (double[] Values, float Peak) FlowQuantiles(double[] at)
    {
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

    // --- Viewing & Preview Rendering ---

    private const float FlowFloor = 200f;
    private const float FlowCeiling = 200_000f;

    public double FlowFraction(int i)
    {
        float f = Flow[i];
        if (f <= FlowFloor) return 0;
        return Math.Clamp(Math.Log(f / FlowFloor) / Math.Log(FlowCeiling / FlowFloor), 0, 1);
    }

    public int ViewRank(int i) => LandMask[i] == 0 ? 0 : 1 + (int)(4096 * FlowFraction(i));

    public (byte R, byte G, byte B) Shade(float[] elevation, MapConfig cfg, int i)
    {
        if (LandMask[i] == 0) return (26, 42, 68);

        float sea = cfg.Limits.SeaLevelUpper;
        float peak = Math.Max(sea + 1f, cfg.PeakElevation);
        double height = Math.Clamp((elevation[i] - sea) / (peak - sea), 0, 1);

        double r = 58 + 150 * height, g = 62 + 150 * height, b = 66 + 148 * height;

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