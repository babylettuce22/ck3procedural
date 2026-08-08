namespace Ck3MapGen.MapGen;

/// <summary>
/// Drainage: depression filling, flow direction and flow accumulation. Every hydraulic thing in
/// this generator is built on it — the erosion model, lakes, and the river courses themselves are
/// all read off the same drainage network, which is why the rivers follow the eroded landscape
/// instead of being traced separately and then pasted on.
///
/// The fill is Priority-Flood with an epsilon gradient (Barnes, Lehman &amp; Mulla 2014). The
/// priority queue is a bucket queue over quantised heights rather than a binary heap: Priority-
/// Flood never inserts below the level it is currently popping, so a monotone bucket pointer is
/// sufficient and the whole fill is O(n) instead of O(n log n). At vanilla province resolution
/// that is 42 million cells, where the difference is minutes.
/// </summary>
public static class FlowField
{
    private static readonly int[] Dx = [-1, 0, 1, -1, 1, -1, 0, 1];
    private static readonly int[] Dy = [-1, -1, -1, 0, 0, 1, 1, 1];
    private static readonly float[] Dist =
        [1.41421356f, 1f, 1.41421356f, 1f, 1f, 1.41421356f, 1f, 1.41421356f];

    private const int Buckets = 1 << 16;

    public sealed class Result
    {
        /// <summary>Heights with every inland depression raised to its spill point plus epsilon.</summary>
        public required float[] Filled;

        /// <summary>Cells in fill order — non-decreasing in <see cref="Filled"/>.</summary>
        public required int[] Order;

        /// <summary>Downstream neighbour, or -1 at the sea and at the map edge.</summary>
        public required int[] Down;

        /// <summary>Drainage area in cells, including the cell itself.</summary>
        public required float[] Flow;

        /// <summary>Total land cells, so drainage area can be expressed as a fraction of the map.</summary>
        public required long LandCells;
    }

    public static Result Compute(float[] height, int width, int hgt, float seaLevel)
    {
        int n = width * hgt;
        var filled = new float[n];
        var order = new int[n];
        var visited = new bool[n];
        var next = new int[n];
        var head = new int[Buckets];
        Array.Fill(head, -1);

        float lo = float.MaxValue, hi = float.MinValue;
        foreach (float v in height)
        {
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }
        float scale = hi - lo < 1e-9f ? 0 : (Buckets - 1) / (hi - lo);
        float epsilon = Math.Max(2e-7f, (hi - lo) * 1e-7f);

        int Bucket(float v) => Math.Clamp((int)((v - lo) * scale), 0, Buckets - 1);

        void Push(int cell)
        {
            int b = Bucket(filled[cell]);
            next[cell] = head[b];
            head[b] = cell;
        }

        // Seeds: everything already at or below sea level, plus the map edge, which drains off the
        // world. Without the edge seeds a coastal basin touching the border would be filled to the
        // height of the nearest inland ridge.
        for (int i = 0; i < n; i++)
        {
            int x = i % width, y = i / width;
            bool edge = x == 0 || y == 0 || x == width - 1 || y == hgt - 1;
            if (height[i] > seaLevel && !edge) continue;

            filled[i] = height[i];
            visited[i] = true;
            Push(i);
        }

        int count = 0;
        for (int b = 0; b < Buckets; b++)
        {
            while (head[b] >= 0)
            {
                int c = head[b];
                head[b] = next[c];
                order[count++] = c;

                int cx = c % width, cy = c / width;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx[k], ny = cy + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= hgt) continue;

                    int nb = ny * width + nx;
                    if (visited[nb]) continue;

                    filled[nb] = Math.Max(height[nb], filled[c] + epsilon);
                    visited[nb] = true;

                    // filled[nb] >= filled[c], so its bucket is never behind the pointer.
                    int nbBucket = Bucket(filled[nb]);
                    next[nb] = head[nbBucket];
                    head[nbBucket] = nb;
                }
            }
        }

        // Anything the flood could not reach (only possible if the grid is degenerate) still needs
        // a slot in the order, or the accumulation pass below would skip it.
        if (count < n)
            for (int i = 0; i < n && count < n; i++)
                if (!visited[i]) { filled[i] = height[i]; order[count++] = i; }

        var down = new int[n];
        Array.Fill(down, -1);

        // Steepest descent, restricted to cells already popped. Walking `order` forward and marking
        // as we go makes "already popped" free and guarantees the flow graph is acyclic with
        // respect to this exact order — which is what lets the accumulation below be a single
        // reverse sweep. Choosing on `filled` rather than on the raw height means a lake surface
        // drains along its epsilon gradient toward the spill point instead of stalling.
        Array.Clear(visited);
        long landCells = 0;

        for (int k = 0; k < n; k++)
        {
            int c = order[k];
            visited[c] = true;
            if (height[c] <= seaLevel) continue;

            landCells++;
            int cx = c % width, cy = c / width;
            int best = -1;
            float bestSlope = -1f;

            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dx[d], ny = cy + Dy[d];
                if (nx < 0 || ny < 0 || nx >= width || ny >= hgt) continue;

                int nb = ny * width + nx;
                if (!visited[nb]) continue;

                float slope = (filled[c] - filled[nb]) / Dist[d];
                if (slope > bestSlope) { bestSlope = slope; best = nb; }
            }

            down[c] = best;
        }

        var flow = new float[n];
        Array.Fill(flow, 1f);
        for (int k = n - 1; k >= 0; k--)
        {
            int c = order[k];
            int d = down[c];
            if (d >= 0) flow[d] += flow[c];
        }

        return new Result
        {
            Filled = filled,
            Order = order,
            Down = down,
            Flow = flow,
            LandCells = Math.Max(1, landCells),
        };
    }

    /// <summary>Distance between a cell and its neighbour, from their index difference.</summary>
    public static float StepDistance(int from, int to, int width)
    {
        int dx = Math.Abs(to % width - from % width);
        int dy = Math.Abs(to / width - from / width);
        return dx != 0 && dy != 0 ? 1.41421356f : 1f;
    }
}
