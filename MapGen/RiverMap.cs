using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

public static class RiverMap
{
    public const byte PaletteSource = 0;   // #00ff00 (Green) - Source of the main system
    public const byte PaletteJoin = 1;     // #ff0000 (Red) - Tributary merge
    public const byte PaletteSplit = 2;    // #fffc00 (Yellow) - River split
    public const byte PaletteNarrow = 3;   // Light Cyan (0, 225, 255)
    public const byte PaletteWide = 11;    // Deep Blue (0, 0, 100)
    public const byte PaletteWater = 254;  // #ff0080 (Magenta) - Sea & Major Rivers
    public const byte PaletteLand = 255;   // #ffffff (White) - Land

    public static byte[] Generate(MapConfig cfg, ProvinceMap provinces, Drainage drainage)
    {
        int width = cfg.ProvinceWidth;
        int height = cfg.ProvinceHeight;
        int n = width * height;

        var indices = new byte[n];
        Array.Fill(indices, PaletteLand);

        // 1. Mark water from province partition (both open ocean and carved major rivers)
        Parallel.For(0, n, i =>
        {
            if (!provinces.Seeds[provinces.Label[i]].IsLand)
            {
                indices[i] = PaletteWater;
            }
        });

        if (!cfg.EnableMinorRivers)
            return indices;

        // Flow thresholds scaled to map resolution
        float minFlowThreshold = (float)Math.Max(350.0, cfg.Scaled(800.0) / Math.Max(0.1, cfg.RiverDensity));
        float maxFlowThreshold = (float)Math.Max(minFlowThreshold * 10f, cfg.Scaled(25000.0));
        int minLength = (int)Math.Max(10, cfg.Scaled(16.0));

        // 2. Identify candidate river sources
        var sourceCandidates = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (indices[i] != PaletteLand) continue;
            if (drainage.Flow[i] < minFlowThreshold) continue;

            bool isNewSource = true;
            for (int dy = -1; dy <= 1 && isNewSource; dy++)
            {
                for (int dx = -1; dx <= 1 && isNewSource; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int x = (i % width) + dx;
                    int y = (i / width) + dy;
                    if (x < 0 || x >= width || y < 0 || y >= height) continue;

                    int nb = y * width + x;
                    if (indices[nb] == PaletteLand && drainage.Receiver[nb] == i && drainage.Flow[nb] >= minFlowThreshold)
                    {
                        isNewSource = false;
                    }
                }
            }

            if (isNewSource) sourceCandidates.Add(i);
        }

        // Trace largest river basins first so main trunks get Green source
        sourceCandidates.Sort((a, b) => drainage.Flow[b].CompareTo(drainage.Flow[a]));

        var isRiverPixel = new bool[n];
        int drawnRivers = 0;

        foreach (int source in sourceCandidates)
        {
            var path = new List<int>();
            int curr = source;
            bool hitWater = false;
            bool hitExisting = false;
            int mergeTarget = -1;

            while (curr >= 0 && path.Count < 3000)
            {
                if (indices[curr] == PaletteWater)
                {
                    hitWater = true;
                    // Extend 1-2 pixels into water for engine spline direction
                    path.Add(curr);
                    int intoWater = drainage.Receiver[curr];
                    if (intoWater != curr && indices[intoWater] == PaletteWater)
                        path.Add(intoWater);
                    break;
                }

                if (isRiverPixel[curr])
                {
                    hitExisting = true;
                    mergeTarget = curr;
                    break;
                }

                path.Add(curr);
                int into = drainage.Receiver[curr];
                if (into == curr) break;
                curr = into;
            }

            if (path.Count < minLength && !hitExisting) continue;
            if (!hitWater && !hitExisting) continue;

            // Convert 8-connected diagonal path to strict 4-connected orthogonal path
            var orthoPath = MakeOrthogonal(path, width, height);

            // Paint pixels
            for (int k = 0; k < orthoPath.Count; k++)
            {
                int c = orthoPath[k];

                if (k == 0)
                {
                    // Only main trunks get green source; tributaries starting anew start as narrow blue
                    indices[c] = hitExisting ? PaletteNarrow : PaletteSource;
                }
                else if (k == orthoPath.Count - 1 && hitExisting && mergeTarget >= 0)
                {
                    // Merge point gets Red join
                    indices[mergeTarget] = PaletteJoin;
                }
                else
                {
                    float f = drainage.Flow[c];
                    double t = Math.Clamp(Math.Log(Math.Max(1f, f / minFlowThreshold)) /
                                          Math.Log(Math.Max(1.01f, maxFlowThreshold / minFlowThreshold)), 0, 1);
                    int tier = PaletteNarrow + (int)Math.Round(t * (PaletteWide - PaletteNarrow));
                    indices[c] = (byte)Math.Clamp(tier, PaletteNarrow, PaletteWide);
                }

                isRiverPixel[c] = true;
            }

            drawnRivers++;
        }

        Console.WriteLine($"  minor rivers: generated {drawnRivers} engine-compliant tributary streams in rivers.png");
        return indices;
    }

    /// <summary>
    /// Ensures strictly 4-connected (orthogonal) river pixels without diagonal-only corners.
    /// </summary>
    private static List<int> MakeOrthogonal(List<int> rawPath, int width, int height)
    {
        var result = new List<int>();
        if (rawPath.Count == 0) return result;

        result.Add(rawPath[0]);

        for (int i = 1; i < rawPath.Count; i++)
        {
            int prev = result[^1];
            int curr = rawPath[i];

            int px = prev % width, py = prev / width;
            int cx = curr % width, cy = curr / width;

            int dx = cx - px;
            int dy = cy - py;

            // If diagonal step, insert an intermediate orthogonal pixel
            if (dx != 0 && dy != 0)
            {
                int intermediate = py * width + cx;
                result.Add(intermediate);
            }

            result.Add(curr);
        }

        return result;
    }
}