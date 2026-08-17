using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class TerrainMaskWriter
{
    public static void WriteAll(string modDir, string gameDir, MapConfig cfg)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;

        string terrainDir = Path.Combine(modDir, "gfx", "map", "terrain");
        var index = ReadTga(Path.Combine(terrainDir, "detail_index.tga"));
        var intensity = ReadTga(Path.Combine(terrainDir, "detail_intensity.tga"));
        if (index is null || intensity is null)
        {
            Console.WriteLine("  terrain masks: SKIPPED (detail textures not written)");
            return;
        }

        if (index.Width != width || index.Height != height ||
            intensity.Width != width || intensity.Height != height)
        {
            Console.WriteLine($"  terrain masks: SKIPPED (detail textures are " +
                              $"{index.Width}x{index.Height} / {intensity.Width}x{intensity.Height}, " +
                              $"expected {width}x{height})");
            return;
        }

        var names = ReadMaterialOrder(Path.Combine(gameDir, "gfx", "map", "terrain", "materials.settings"));
        if (names.Count == 0)
        {
            Console.WriteLine("  terrain masks: SKIPPED (materials.settings unreadable)");
            return;
        }

        byte[] indexPixels = index.Pixels, intensityPixels = intensity.Pixels;
        var used = TerrainTextureWriter.UsedMaterials;
        int totalPixels = width * height;

        foreach (string sub in (string[])["masks", "masks_gen"])
        {
            string source = Path.Combine(gameDir, "gfx", "map", "terrain", sub);
            string destination = Path.Combine(terrainDir, sub);
            if (!Directory.Exists(source)) continue;
            Directory.CreateDirectory(destination);

            var maskFiles = Directory.GetFiles(source, "*.png");
            var coverageMap = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in maskFiles)
            {
                string file = Path.GetFileName(path);
                string material = Path.GetFileNameWithoutExtension(path);
                if (material.EndsWith("_mask", StringComparison.Ordinal)) material = material[..^5];

                var coverage = new byte[totalPixels];

                if (names.TryGetValue(material, out int id) && id < 256 && used[id])
                {
                    byte target = (byte)id;

                    for (int y = 0; y < height; y++)
                    {
                        long srcRow = (long)(height - 1 - y) * width * 4;
                        long dstRow = (long)y * width;

                        for (int x = 0; x < width; x++)
                        {
                            long o = srcRow + x * 4;

                            byte weight = 0;
                            if (indexPixels[o + 2] == target) weight = Math.Max(weight, intensityPixels[o + 2]);
                            if (indexPixels[o + 1] == target) weight = Math.Max(weight, intensityPixels[o + 1]);
                            if (indexPixels[o + 0] == target) weight = Math.Max(weight, intensityPixels[o + 0]);
                            if (indexPixels[o + 3] == target) weight = Math.Max(weight, intensityPixels[o + 3]);

                            coverage[dstRow + x] = weight;
                        }
                    }

                    coverage = SmoothCoverage(coverage, width, height);
                }

                coverageMap[file] = coverage;
            }

            // Normalization pass to eliminate 0-weight dropouts across all masks
            var activeCoverages = coverageMap.Values.Where(c => c.Any(b => b > 0)).ToList();
            if (activeCoverages.Count > 0)
            {
                Parallel.For(0, totalPixels, i =>
                {
                    int sum = 0;
                    for (int m = 0; m < activeCoverages.Count; m++) sum += activeCoverages[m][i];
                    if (sum > 0 && sum < 255)
                    {
                        float norm = 255f / sum;
                        for (int m = 0; m < activeCoverages.Count; m++)
                        {
                            if (activeCoverages[m][i] > 0)
                                activeCoverages[m][i] = (byte)Math.Clamp((int)Math.Round(activeCoverages[m][i] * norm), 0, 255);
                        }
                    }
                });
            }

            foreach (var kvp in coverageMap)
            {
                PngWriter.WriteGray8(Path.Combine(destination, kvp.Key), width, height, kvp.Value);
            }

            foreach (string path in Directory.GetFiles(source))
            {
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), overwrite: true);
            }
        }

        Console.WriteLine($"  terrain masks written & normalized");
    }

    private static byte[] SmoothCoverage(byte[] src, int width, int height)
    {
        var temp = new byte[src.Length];
        var dst = new byte[src.Length];

        Parallel.For(0, height, y =>
        {
            long row = (long)y * width;
            for (int x = 0; x < width; x++)
            {
                int xm2 = Math.Max(0, x - 2);
                int xm1 = Math.Max(0, x - 1);
                int xp1 = Math.Min(width - 1, x + 1);
                int xp2 = Math.Min(width - 1, x + 2);

                int val = (src[row + xm2] * 1 +
                           src[row + xm1] * 4 +
                           src[row + x] * 6 +
                           src[row + xp1] * 4 +
                           src[row + xp2] * 1) >> 4;

                temp[row + x] = (byte)val;
            }
        });

        Parallel.For(0, height, y =>
        {
            int ym2 = Math.Max(0, y - 2);
            int ym1 = Math.Max(0, y - 1);
            int yp1 = Math.Min(height - 1, y + 1);
            int yp2 = Math.Min(height - 1, y + 2);

            long rowM2 = (long)ym2 * width;
            long rowM1 = (long)ym1 * width;
            long row = (long)y * width;
            long rowP1 = (long)yp1 * width;
            long rowP2 = (long)yp2 * width;

            for (int x = 0; x < width; x++)
            {
                int val = (temp[rowM2 + x] * 1 +
                           temp[rowM1 + x] * 4 +
                           temp[row + x] * 6 +
                           temp[rowP1 + x] * 4 +
                           temp[rowP2 + x] * 1) >> 4;

                dst[row + x] = (byte)val;
            }
        });

        return dst;
    }

    private static Dictionary<string, int> ReadMaterialOrder(string path)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!File.Exists(path)) return order;

        int next = 0;
        foreach (string line in File.ReadLines(path))
        {
            string text = line.Trim();
            if (!text.StartsWith("name", StringComparison.Ordinal)) continue;

            int open = text.IndexOf('"');
            if (open < 0) continue;
            int close = text.IndexOf('"', open + 1);
            if (close < 0) continue;

            order.TryAdd(text[(open + 1)..close], next);
            next++;
        }
        return order;
    }

    private sealed record Tga(byte[] Pixels, int Width, int Height);

    private static Tga? ReadTga(string path)
    {
        if (!File.Exists(path)) return null;

        byte[] file = File.ReadAllBytes(path);
        if (file.Length < 18) return null;

        int idLength = file[0];
        int offset = 18 + idLength;
        if (file[2] != 2 || file[16] != 32 || offset >= file.Length) return null;

        int width = file[12] | (file[13] << 8);
        int height = file[14] | (file[15] << 8);

        var pixels = new byte[file.Length - offset];
        Array.Copy(file, offset, pixels, 0, pixels.Length);

        if ((long)width * height * 4 != pixels.Length) return null;

        return new Tga(pixels, width, height);
    }
}