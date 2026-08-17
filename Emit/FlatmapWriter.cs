// Emit/FlatmapWriter.cs
namespace Ck3MapGen.Emit;

using System;
using System.IO;
using System.Threading.Tasks;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

public static class FlatmapWriter
{
    public static void WriteAll(
        string modDir, MapConfig cfg, ProvinceMap provinces,
        int[] order, int landCount, float[] elevation,
        TerrainClass[]? provinceTerrain = null)
    {
        int w = cfg.ProvinceWidth;
        int h = cfg.ProvinceHeight;

        var pixels = new byte[w * h * 4];

        string[] candidatePaths = [
            Path.Combine(AppContext.BaseDirectory, "assets", "parchment.dds"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "assets", "parchment.dds"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "parchment.dds"),
            Path.Combine(modDir, "assets", "parchment.dds"),
            Path.Combine(AppContext.BaseDirectory, "parchment.dds")
        ];

        TextureSampler? parchment = null;
        foreach (var candidate in candidatePaths)
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    parchment = TextureSampler.TryLoad(fullPath);
                    if (parchment != null)
                    {
                        Console.WriteLine($"  flatmap: loaded texture from '{fullPath}' ({parchment.Width}x{parchment.Height})");
                        break;
                    }
                }
            }
            catch { }
        }

        if (parchment == null)
        {
            Console.WriteLine("  flatmap: using procedural warm parchment tone");
        }

        var coastDist = ComputeCoastDistance(provinces, order, landCount, w, h, out var landMask);

        const float sunAzimuth = 315.0f * MathF.PI / 180.0f;
        const float sunElevation = 45.0f * MathF.PI / 180.0f;
        const float parchmentTileSize = 1024.0f;

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                bool isLand = landMask[idx];

                // 1. Sample Seamless Dual-Phase Parchment
                float baseR, baseG, baseB;
                if (parchment != null)
                {
                    (baseR, baseG, baseB) = parchment.SampleSeamlessTiled(x, y, parchmentTileSize);
                }
                else
                {
                    baseR = 232f; baseG = 222f; baseB = 200f;
                }

                float r, g, b;

                if (isLand)
                {
                    // 2. Lightened Paper Canvas for Land
                    const float landLift = 1.08f;
                    r = baseR * landLift;
                    g = baseG * landLift;
                    b = baseB * landLift;

                    // 3. Gentle Shaded Relief
                    float shade = CalculateSoftHillshade(elevation, x, y, w, h, sunAzimuth, sunElevation);
                    r *= shade;
                    g *= shade;
                    b *= shade;
                }
                else
                {
                    // 4. Ocean Glaze & Coastal Echo Waves
                    int dist = coastDist[idx];

                    float oceanDepth = Math.Clamp(dist / 64.0f, 0.0f, 1.0f);
                    float owr = Lerp(190f, 145f, oceanDepth * 0.40f);
                    float owg = Lerp(198f, 155f, oceanDepth * 0.40f);
                    float owb = Lerp(192f, 150f, oceanDepth * 0.35f);

                    r = (baseR * owr) / 255.0f;
                    g = (baseG * owg) / 255.0f;
                    b = (baseB * owb) / 255.0f;

                    if (dist == 1)
                    {
                        // Coastline boundary ink stroke
                        r *= 0.45f; g *= 0.40f; b *= 0.35f;
                    }
                    else if (dist <= 24 && (dist % 6 == 0))
                    {
                        // Coastal wave echoes
                        float echoAlpha = 1.0f - (dist / 24.0f);
                        r = Lerp(r, r * 0.82f, echoAlpha * 0.40f);
                        g = Lerp(g, g * 0.78f, echoAlpha * 0.40f);
                        b = Lerp(b, b * 0.74f, echoAlpha * 0.40f);
                    }
                }

                int o = idx * 4;
                pixels[o + 0] = (byte)Math.Clamp(b, 0, 255);
                pixels[o + 1] = (byte)Math.Clamp(g, 0, 255);
                pixels[o + 2] = (byte)Math.Clamp(r, 0, 255);
                pixels[o + 3] = 255;
            }
        });

        string flatMapDir = Path.Combine(modDir, "gfx", "map", "terrain", "flat_maps");
        Directory.CreateDirectory(flatMapDir);

        DdsWriter.WriteBgra(Path.Combine(flatMapDir, "flatmap.dds"), w, h, pixels);
        DdsWriter.WriteBgra(Path.Combine(flatMapDir, "flatmap_tgp.dds"), w, h, pixels);

        Console.WriteLine($"  flatmap: rendered illuminated flatmaps ({w}x{h})");
    }

    private static float CalculateSoftHillshade(float[] elevation, int x, int y, int w, int h, float sunAz, float sunEl)
    {
        var (dx1, dy1) = SampleGradient(elevation, x, y, w, h, radius: 2);
        var (dx2, dy2) = SampleGradient(elevation, x, y, w, h, radius: 6);

        float dx = dx1 * 0.7f + dx2 * 0.3f;
        float dy = dy1 * 0.7f + dy2 * 0.3f;

        float slopeMagnitude = MathF.Sqrt(dx * dx + dy * dy);
        float slope = MathF.Atan(Math.Clamp(slopeMagnitude * 1.5f, 0.0f, 2.5f));
        float aspect = MathF.Atan2(dy, -dx);

        float shade = MathF.Sin(sunEl) * MathF.Cos(slope) + MathF.Cos(sunEl) * MathF.Sin(slope) * MathF.Cos(sunAz - aspect);

        return Math.Clamp(0.88f + (shade - 0.707f) * 0.35f, 0.82f, 1.10f);
    }

    private static (float dx, float dy) SampleGradient(float[] elevation, int x, int y, int w, int h, int radius)
    {
        int x0 = Math.Max(0, x - radius), x1 = Math.Min(w - 1, x + radius);
        int y0 = Math.Max(0, y - radius), y1 = Math.Min(h - 1, y + radius);

        float eTL = elevation[y0 * w + x0], eT = elevation[y0 * w + x], eTR = elevation[y0 * w + x1];
        float eL = elevation[y * w + x0], eR = elevation[y * w + x1];
        float eBL = elevation[y1 * w + x0], eB = elevation[y1 * w + x], eBR = elevation[y1 * w + x1];

        float dx = ((eTR + 2 * eR + eBR) - (eTL + 2 * eL + eBL)) / (8.0f * radius);
        float dy = ((eBL + 2 * eB + eBR) - (eTL + 2 * eT + eTR)) / (8.0f * radius);

        return (dx, dy);
    }

    private static byte[] ComputeCoastDistance(ProvinceMap provinces, int[] order, int landCount, int w, int h, out bool[] landMask)
    {
        landMask = new bool[w * h];
        var dist = new byte[w * h];
        Array.Fill(dist, (byte)255);

        var queue = new System.Collections.Generic.Queue<int>();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool isLand = order[provinces.Label[i]] <= landCount;
                landMask[i] = isLand;

                if (isLand)
                {
                    dist[i] = 0;
                    if (x > 0 && order[provinces.Label[i - 1]] > landCount ||
                        x < w - 1 && order[provinces.Label[i + 1]] > landCount ||
                        y > 0 && order[provinces.Label[i - w]] > landCount ||
                        y < h - 1 && order[provinces.Label[i + w]] > landCount)
                    {
                        queue.Enqueue(i);
                    }
                }
            }
        }

        int[] dxs = [-1, 1, 0, 0], dys = [0, 0, -1, 1];
        while (queue.Count > 0)
        {
            int curr = queue.Dequeue();
            int cx = curr % w, cy = curr / w;
            int nextDist = dist[curr] + 1;
            if (nextDist > 32) continue;

            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dxs[d], ny = cy + dys[d];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                int ni = ny * w + nx;
                if (!landMask[ni] && dist[ni] > nextDist)
                {
                    dist[ni] = (byte)nextDist;
                    queue.Enqueue(ni);
                }
            }
        }

        return dist;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0.0f, 1.0f);
}