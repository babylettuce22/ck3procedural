namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// Stage 3. A landscape evolution model: uplift competing against fluvial incision, run
/// iteratively until the terrain reaches its own shape.
///
/// Each iteration is <c>dh = U - K A^m S^n</c> — the stream power law — plus sediment routing and
/// a thermal slope limit. Two properties matter for this project:
///
/// * It is the erosion the brief asks for, done as simulated hydraulics that can be iterated, and
///   it shares its drainage network with the river extraction, so the rivers necessarily follow
///   the valleys the erosion cut.
/// * It is deterministic and its inner loops are either parallel or a single linear sweep. Droplet
///   erosion is the usual choice here and gives comparable results, but the droplets have to write
///   to shared cells, so a parallel version is not reproducible from a seed — which this project
///   depends on, since the way it is tested is running one seed and looking at it in game.
///
/// Feeding uplift in as a *rate* rather than stamping mountains in as a height is what keeps
/// ranges narrow: the belt is continuously rebuilt where the plate boundary is while rivers cut
/// into it from both sides, so the range stays a strip with a fan of valleys either side, instead
/// of relaxing into a dome.
/// </summary>
public static class LandscapeEvolution
{
    public sealed class Options
    {
        public int Iterations = 34;

        /// <summary>K. Drainage area is normalised by land area first, so this is O(1).</summary>
        public float Erodibility = 3.2f;

        /// <summary>m, the drainage-area exponent. 0.4-0.6 in the literature.</summary>
        public float AreaExponent = 0.5f;

        /// <summary>n, the slope exponent.</summary>
        public float SlopeExponent = 1.0f;

        /// <summary>Height added per iteration where uplift is 1.</summary>
        public float UpliftPerStep = 0.026f;

        /// <summary>Height removed per iteration where the rift rate is 1.</summary>
        public float RiftPerStep = 0.004f;

        /// <summary>Fraction of the sediment passing through a cell that settles there.</summary>
        public float Deposition = 0.35f;

        /// <summary>Slope above which deposition stops entirely, in height per cell.</summary>
        public float DepositionSlope = 0.03f;

        /// <summary>Steepest slope the ground will hold, in height per cell.</summary>
        public float Talus = 0.045f;

        /// <summary>Fraction of the excess above the talus angle that moves per pass.</summary>
        public float TalusRate = 0.5f;

        public int TalusPasses = 1;

        /// <summary>Cap on how much a single cell may lose in one iteration.</summary>
        public float MaxIncisionPerStep = 0.02f;
    }

    private static readonly int[] Dx = [-1, 0, 1, -1, 1, -1, 0, 1];
    private static readonly int[] Dy = [-1, -1, -1, 0, 0, 1, 1, 1];
    private static readonly float[] Dist =
        [1.41421356f, 1f, 1.41421356f, 1f, 1f, 1.41421356f, 1f, 1.41421356f];

    /// <summary>
    /// Erodes <paramref name="height"/> in place. Returns the drainage of the final iteration, so
    /// the caller can extract rivers and lakes from exactly the terrain it is handed back.
    /// </summary>
    public static FlowField.Result Run(float[] height, int width, int hgt, float seaLevel,
        float[] uplift, float[] rift, Options o)
    {
        int n = width * hgt;
        var incision = new float[n];
        var sediment = new float[n];
        FlowField.Result flow = null!;

        for (int iteration = 0; iteration < o.Iterations; iteration++)
        {
            Parallel.For(0, n, i =>
            {
                height[i] += uplift[i] * o.UpliftPerStep - rift[i] * o.RiftPerStep;
            });

            flow = FlowField.Compute(height, width, hgt, seaLevel);
            float areaScale = 1f / flow.LandCells;

            Parallel.For(0, n, i =>
            {
                incision[i] = 0;
                sediment[i] = 0;

                int d = flow.Down[i];
                if (d < 0 || height[i] <= seaLevel) return;

                float slope = (flow.Filled[i] - flow.Filled[d])
                              / FlowField.StepDistance(i, d, width);
                if (slope <= 0) return;

                float area = flow.Flow[i] * areaScale;
                float dz = o.Erodibility
                           * MathF.Pow(area, o.AreaExponent)
                           * MathF.Pow(slope, o.SlopeExponent);

                incision[i] = MathF.Min(dz, o.MaxIncisionPerStep);
            });

            // One linear sweep from the highest cell to the lowest. `Order` is ascending, and
            // FlowField guarantees every cell's downstream neighbour appears earlier in it, so
            // walking it backwards visits each cell only after everything draining into it.
            var order = flow.Order;
            var down = flow.Down;
            for (int k = n - 1; k >= 0; k--)
            {
                int c = order[k];
                int d = down[c];

                float carried = sediment[c] + incision[c];
                if (carried <= 0) continue;

                if (d < 0)
                {
                    // Reached the sea or the map edge; the load leaves the system. Dropping it
                    // here instead would pile a whole catchment's sediment onto one river mouth.
                    sediment[c] = 0;
                    continue;
                }

                float slope = (flow.Filled[c] - flow.Filled[d])
                              / FlowField.StepDistance(c, d, width);
                float flatness = 1f - MathF.Min(1f, slope / o.DepositionSlope);
                float settle = carried * o.Deposition * flatness;

                sediment[d] += carried - settle;
                sediment[c] = settle;
            }

            Parallel.For(0, n, i =>
            {
                if (height[i] <= seaLevel && incision[i] <= 0) return;
                height[i] += sediment[i] - incision[i];
            });

            for (int p = 0; p < o.TalusPasses; p++)
                Thermal(height, width, hgt, o.Talus, o.TalusRate);
        }

        return flow ?? FlowField.Compute(height, width, hgt, seaLevel);
    }

    /// <summary>
    /// Thermal erosion: anything steeper than the talus angle sheds material onto its lowest
    /// neighbour. Written as two gather passes rather than one scatter so it stays parallel and
    /// mass-conserving without atomics.
    /// </summary>
    public static void Thermal(float[] height, int width, int hgt, float talus, float rate)
    {
        int n = width * hgt;
        var outflow = new float[n];
        var target = new sbyte[n];

        Parallel.For(0, hgt, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                target[i] = -1;

                float bestDrop = 0;
                int bestK = -1;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Dx[k], ny = y + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= hgt) continue;

                    float drop = (height[i] - height[ny * width + nx]) / Dist[k];
                    if (drop > bestDrop) { bestDrop = drop; bestK = k; }
                }

                if (bestK < 0 || bestDrop <= talus) continue;

                target[i] = (sbyte)bestK;

                // Half the excess, so the pair does not overshoot past each other and oscillate.
                outflow[i] = (bestDrop - talus) * Dist[bestK] * rate * 0.5f;
            }
        });

        var result = new float[n];
        Parallel.For(0, hgt, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float sum = height[i] - outflow[i];

                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Dx[k], ny = y + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= hgt) continue;

                    int nb = ny * width + nx;
                    // Does that neighbour shed onto this cell?
                    sbyte t = target[nb];
                    if (t < 0) continue;
                    if (nx + Dx[t] == x && ny + Dy[t] == y) sum += outflow[nb];
                }

                result[i] = sum;
            }
        });

        Array.Copy(result, height, n);
    }
}
