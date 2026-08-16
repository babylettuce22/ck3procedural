using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Picks the point in each province where its holding, army and siege models should stand.
///
/// The locators used to sit on the province's Dijkstra seed, which is simply wherever the
/// partitioner happened to drop a point before growing the province around it. Nothing makes that
/// spot representative: it is as likely to be a pixel from the coastline as the middle, so castles
/// ended up half in the sea and armies mustered on cliff faces. The province's centroid is no
/// better on its own, because a province bent around a bay has a centroid in the water.
///
/// So the anchor is chosen in two stages. First a distance-to-edge transform finds how deep inside
/// its own province every pixel is, and only pixels in the deepest fraction of that province stay
/// in the running — which is what keeps a model off the shoreline whatever shape the province is.
/// Among those, the flattest ground wins, with nearness to the centroid breaking ties, so the
/// model stands on level ground near the middle rather than on the side of a mountain.
/// </summary>
public static class ProvinceAnchor
{
    /// <summary>4-neighbour offsets: a pixel is on the edge if it orthogonally touches another
    /// province, since diagonal-only contact is a corner rather than a border.</summary>
    private static readonly (int Dx, int Dy)[] Orthogonal = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    /// <summary>
    /// One anchor per province label, in province-map pixels.
    /// </summary>
    public static (double X, double Y)[] Compute(ProvinceMap map, float[] elevation, MapConfig cfg)
    {
        int width = map.Width, height = map.Height;
        var depth = DistanceFromEdge(map);

        // Slope is normalised against this map's own median before it is weighed against distance
        // from the centroid, so the two terms are dimensionless and comparable. The raw gradient is
        // in whatever units the elevation field happens to use, which differ by map size and by how
        // far the erosion ran — the same reasoning the terrain classifier's percentile thresholds
        // rest on.
        var slope = SlopeField(elevation, width, height);
        double reference = MedianSlope(slope);

        // Per-province maximum depth and centroid, in one pass.
        var deepest = new int[map.Count];
        var sumX = new double[map.Count];
        var sumY = new double[map.Count];
        var area = new int[map.Count];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;
                int label = map.Label[cell];

                if (depth[cell] > deepest[label]) deepest[label] = depth[cell];
                sumX[label] += x;
                sumY[label] += y;
                area[label]++;
            }
        }

        var bestScore = new double[map.Count];
        var anchor = new (double X, double Y)[map.Count];
        Array.Fill(bestScore, double.PositiveInfinity);

        double fraction = Math.Clamp(cfg.LocatorInteriorFraction, 0, 1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;
                int label = map.Label[cell];

                // Only the interior core of the province is eligible. A province one pixel deep
                // everywhere still yields its own pixels, so nothing is ever left without an anchor.
                if (depth[cell] < deepest[label] * fraction) continue;

                double centroidX = sumX[label] / area[label];
                double centroidY = sumY[label] / area[label];
                double dx = x - centroidX, dy = y - centroidY;

                // Distance from the centroid is normalised by the province's own size so the
                // tiebreak means the same thing in a small province as in a large one.
                double radius = Math.Sqrt(area[label]) + 1;
                double score = slope[cell] / reference
                               + cfg.LocatorCentroidPull * Math.Sqrt(dx * dx + dy * dy) / radius;

                if (score >= bestScore[label]) continue;

                bestScore[label] = score;
                anchor[label] = (x, y);
            }
        }

        // A province the loop never saw cannot happen once labels are compacted, but an anchor of
        // (0,0) would put a castle in the corner of the map rather than fail loudly, so fall back
        // to the seed the partition already has.
        for (int label = 0; label < map.Count; label++)
            if (double.IsPositiveInfinity(bestScore[label]))
                anchor[label] = (map.Seeds[label].X, map.Seeds[label].Y);

        Report(map, depth, slope, anchor);
        return anchor;
    }

    /// <summary>
    /// States what the pass bought, on the two axes it is meant to buy it on: how far the model
    /// stands from the province edge, and how steep the ground under it is. Both are compared
    /// against the seed the locators used to sit on.
    /// </summary>
    private static void Report(ProvinceMap map, int[] depth, float[] slope,
        (double X, double Y)[] anchor)
    {
        var seedDepth = new List<int>(map.Count);
        var anchorDepth = new List<int>(map.Count);
        var seedSlope = new List<float>(map.Count);
        var anchorSlope = new List<float>(map.Count);

        for (int label = 0; label < map.Count; label++)
        {
            if (!map.Seeds[label].IsLand) continue;

            int seedCell = map.Seeds[label].Y * map.Width + map.Seeds[label].X;
            int anchorCell = (int)anchor[label].Y * map.Width + (int)anchor[label].X;
            if (seedCell < 0 || seedCell >= depth.Length) continue;

            seedDepth.Add(depth[seedCell]);
            anchorDepth.Add(depth[anchorCell]);
            seedSlope.Add(slope[seedCell]);
            anchorSlope.Add(slope[anchorCell]);
        }

        if (seedDepth.Count == 0) return;

        seedDepth.Sort();
        anchorDepth.Sort();
        seedSlope.Sort();
        anchorSlope.Sort();
        int mid = seedDepth.Count / 2;

        Console.WriteLine($"  locator anchors: median depth into province {seedDepth[mid]} -> " +
                          $"{anchorDepth[mid]} px, median slope under it " +
                          $"{seedSlope[mid]:F2} -> {anchorSlope[mid]:F2}");
    }

    /// <summary>
    /// How many steps every pixel is from the nearest pixel of another province, by BFS inward
    /// from all borders at once. The map edge counts as a border, so a province running off the
    /// side of the map does not read as infinitely deep.
    /// </summary>
    private static int[] DistanceFromEdge(ProvinceMap map)
    {
        int width = map.Width, height = map.Height;
        var depth = new int[width * height];
        var frontier = new Queue<int>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;
                int label = map.Label[cell];
                bool edge = false;

                foreach (var (dx, dy) in Orthogonal)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) { edge = true; break; }
                    if (map.Label[ny * width + nx] != label) { edge = true; break; }
                }

                if (!edge) continue;

                depth[cell] = 1;
                frontier.Enqueue(cell);
            }
        }

        while (frontier.Count > 0)
        {
            int cell = frontier.Dequeue();
            int x = cell % width, y = cell / width;
            int label = map.Label[cell];

            foreach (var (dx, dy) in Orthogonal)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int next = ny * width + nx;
                if (depth[next] != 0 || map.Label[next] != label) continue;

                depth[next] = depth[cell] + 1;
                frontier.Enqueue(next);
            }
        }

        return depth;
    }

    /// <summary>Gradient magnitude by central difference, which is what "steep" means here.</summary>
    private static float[] SlopeField(float[] elevation, int width, int height)
    {
        var slope = new float[width * height];

        Parallel.For(0, height, y =>
        {
            int y0 = Math.Max(0, y - 1), y1 = Math.Min(height - 1, y + 1);

            for (int x = 0; x < width; x++)
            {
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(width - 1, x + 1);

                double dx = elevation[y * width + x1] - elevation[y * width + x0];
                double dy = elevation[y1 * width + x] - elevation[y0 * width + x];

                slope[y * width + x] = (float)Math.Sqrt(dx * dx + dy * dy);
            }
        });

        return slope;
    }

    /// <summary>Median of the non-flat pixels, sampled — the whole field is millions of values and
    /// the reference only has to be the right order of magnitude.</summary>
    private static double MedianSlope(float[] slope)
    {
        var sample = new List<float>(slope.Length / 97 + 1);
        for (int i = 0; i < slope.Length; i += 97)
            if (slope[i] > 0) sample.Add(slope[i]);

        if (sample.Count == 0) return 1.0;

        sample.Sort();
        double median = sample[sample.Count / 2];
        return median > 0 ? median : 1.0;
    }
}
