using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public sealed class MajorRiverPath
{
    public required List<(float X, float Y)> Points { get; init; }
    public required float TotalLength { get; init; }
}

public static class MajorRivers
{
    public static List<MajorRiverPath> ExtractAndCarve(
        float[] fullElev,
        int fullWidth,
        int fullHeight,
        Drainage drainage,
        MapConfig cfg,
        Rng rng)
    {
        if (!cfg.EnableMajorRivers || cfg.MajorRiverCount <= 0)
            return [];

        int pw = cfg.ProvinceWidth;
        int ph = cfg.ProvinceHeight;
        float sea = cfg.Limits.SeaLevelUpper;

        // 1. Find coastal outlets and rank by catchment flow
        var candidateOutlets = new List<(int Cell, float Flow)>();
        for (int y = 1; y < ph - 1; y++)
        {
            for (int x = 1; x < pw - 1; x++)
            {
                int c = y * pw + x;
                if (drainage.LandMask[c] == 0) continue;

                int into = drainage.Receiver[c];
                if (drainage.LandMask[into] == 0 && drainage.Flow[c] >= 800)
                {
                    candidateOutlets.Add((c, drainage.Flow[c]));
                }
            }
        }

        candidateOutlets.Sort((a, b) => b.Flow.CompareTo(a.Flow));

        var paths = new List<MajorRiverPath>();
        var occupied = new bool[pw * ph];
        int targetRivers = cfg.MajorRiverCount;

        var feeders = new List<int>[pw * ph];
        for (int i = 0; i < drainage.Receiver.Length; i++)
        {
            int into = drainage.Receiver[i];
            if (into != i && drainage.LandMask[i] != 0)
            {
                (feeders[into] ??= []).Add(i);
            }
        }

        foreach (var (outlet, flow) in candidateOutlets)
        {
            if (paths.Count >= targetRivers) break;
            if (occupied[outlet]) continue;

            var rawPoints = TraceUpstream(outlet, drainage, feeders, pw, ph, occupied, sea, cfg);

            if (rawPoints.Count >= (int)Math.Max(15, cfg.Scaled(30)))
            {
                rawPoints.Reverse(); // Source -> mouth
                paths.Add(new MajorRiverPath
                {
                    Points = rawPoints,
                    TotalLength = rawPoints.Count,
                });
            }
        }

        // 2. Carve channels with strict headwater tapering and gentle valley shoulders
        CarveHeightmapChannels(fullElev, fullWidth, fullHeight, paths, cfg);

        Console.WriteLine($"  major rivers: extracted and carved {paths.Count} major river system(s)");
        return paths;
    }

    private static List<(float X, float Y)> TraceUpstream(
        int outlet,
        Drainage drainage,
        List<int>[] feeders,
        int width,
        int height,
        bool[] occupied,
        float sea,
        MapConfig cfg)
    {
        var pts = new List<(float X, float Y)>();
        int curr = outlet;

        // Stop major river before it cuts into high mountains
        float maxMajorRiverElevation = sea + 80.0f;

        while (curr >= 0 && pts.Count < 2000)
        {
            int cx = curr % width, cy = curr / width;
            pts.Add((cx, cy));
            occupied[curr] = true;

            // 1. Stop if entering a lake basin or depression (let the lake be natural, don't carve a trench through it)
            if (drainage.LakeDepth(drainage.Filled, curr) > 0.5f)
                break;

            // 2. Stop if elevation climbs into the mountain foothills
            if (drainage.Filled[curr] > maxMajorRiverElevation)
                break;

            var upstream = feeders[curr];
            if (upstream == null || upstream.Count == 0) break;

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
            if (bestFeeder < 0 || maxFlow < 350)
                break;

            curr = bestFeeder;
        }

        return pts;
    }

    private static void CarveHeightmapChannels(
            float[] fullElev,
            int fullWidth,
            int fullHeight,
            List<MajorRiverPath> paths,
            MapConfig cfg)
    {
        float sea = cfg.Limits.SeaLevelUpper;
        float carvedBedElevation = cfg.SeaFloorElevation; // Raw 0 in heightmap

        float scaleX = (float)fullWidth / cfg.ProvinceWidth;
        float scaleY = (float)fullHeight / cfg.ProvinceHeight;

        double minWidthFull = Math.Max(7.0, cfg.Scaled(14.0));
        double maxWidthFull = Math.Max(16.0, cfg.Scaled(32.0));

        const float ValleyReachMultiplier = 4.0f;
        float bankElevation = sea + 5.0f;

        foreach (var path in paths)
        {
            var pts = path.Points;
            int count = pts.Count;
            if (count < 2) continue;

            var radChannel = new float[count];
            var radValley = new float[count];
            var bedElev = new float[count];
            var hx = new float[count];
            var hy = new float[count];

            for (int i = 0; i < count; i++)
            {
                hx[i] = pts[i].X * scaleX;
                hy[i] = pts[i].Y * scaleY;

                float t = (float)i / (count - 1);

                // Smooth cubic taper: 0 at vertex 0, opening over first 20%
                float taper = t < 0.20f ? (t / 0.20f) * (t / 0.20f) * (3f - 2f * (t / 0.20f)) : 1.0f;

                radChannel[i] = (float)(minWidthFull + (maxWidthFull - minWidthFull) * Math.Pow(t, 0.65)) * taper;
                radValley[i] = radChannel[i] * ValleyReachMultiplier;
                bedElev[i] = bankElevation + (carvedBedElevation - bankElevation) * taper;
            }

            for (int i = 0; i < count - 1; i++)
            {
                float ax = hx[i], ay = hy[i];
                float bx = hx[i + 1], by = hy[i + 1];

                float rChanA = radChannel[i], rChanB = radChannel[i + 1];
                float rValA = radValley[i], rValB = radValley[i + 1];
                float bedA = bedElev[i], bedB = bedElev[i + 1];

                float maxR = Math.Max(rValA, rValB);
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

                        // If at the start of the river and projection is behind vertex 0, skip!
                        if (i == 0 && u <= 0.0f) continue;

                        float qx = ax + u * segDx;
                        float qy = ay + u * segDy;

                        float dx = x - qx;
                        float dy = y - qy;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);

                        float curChanR = rChanA + u * (rChanB - rChanA);
                        float curValR = rValA + u * (rValB - rValA);
                        float curBed = bedA + u * (bedB - bedA);

                        if (dist > curValR || curValR < 0.5f) continue;

                        int idx = y * fullWidth + x;
                        float original = fullElev[idx];

                        if (dist <= curChanR && curChanR > 0.5f)
                        {
                            float norm = dist / curChanR;
                            float trenchProfile = MathF.Pow(norm, 4.0f);
                            float targetHeight = curBed + (bankElevation - curBed) * trenchProfile;

                            if (targetHeight < original)
                            {
                                fullElev[idx] = targetHeight;
                            }
                        }
                        else if (curValR > curChanR)
                        {
                            float valleyT = (dist - curChanR) / (curValR - curChanR);
                            float smoothValley = (1.0f - MathF.Cos(valleyT * MathF.PI)) * 0.5f;
                            float targetHeight = bankElevation + (original - bankElevation) * smoothValley;

                            if (targetHeight < original)
                            {
                                fullElev[idx] = targetHeight;
                            }
                        }
                    }
                }
            }
        }
    }
}