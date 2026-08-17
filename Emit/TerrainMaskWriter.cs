using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes gfx/map/terrain/masks and gfx/map/terrain/masks_gen — one 8-bit greyscale coverage
/// image per material at full map resolution with Gaussian anti-aliasing.
/// </summary>
public static class TerrainMaskWriter
{
    public static void WriteAll(string modDir, string gameDir, MapConfig cfg)
    {
        int width = cfg.Width, height = cfg.Height;

        string terrainDir = Path.Combine(modDir, "gfx", "map", "terrain");
        var index = ReadTga(Path.Combine(terrainDir, "detail_index.tga"));
        var intensity = ReadTga(Path.Combine(terrainDir, "detail_intensity.tga"));
        if (index is null || intensity is null)
        {
            Console.WriteLine("  terrain masks: SKIPPED (detail textures not written)");
            return;
        }

        var names = ReadMaterialOrder(Path.Combine(gameDir, "gfx", "map", "terrain", "materials.settings"));
        if (names.Count == 0)
        {
            Console.WriteLine("  terrain masks: SKIPPED (materials.settings unreadable)");
            return;
        }

        var used = TerrainTextureWriter.UsedMaterials;
        int painted = 0, blanked = 0, carried = 0;

        foreach (string sub in (string[])["masks", "masks_gen"])
        {
            string source = Path.Combine(gameDir, "gfx", "map", "terrain", sub);
            string destination = Path.Combine(terrainDir, sub);
            if (!Directory.Exists(source)) continue;
            Directory.CreateDirectory(destination);

            foreach (string path in Directory.GetFiles(source))
            {
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), overwrite: true);
                carried++;
            }

            Parallel.ForEach(Directory.GetFiles(source, "*.png"), path =>
            {
                string file = Path.GetFileName(path);
                string material = Path.GetFileNameWithoutExtension(path);
                if (material.EndsWith("_mask", StringComparison.Ordinal)) material = material[..^5];

                var coverage = new byte[(long)width * height];

                if (names.TryGetValue(material, out int id) && id < 256 && used[id])
                {
                    byte target = (byte)id;

                    // TGAs are stored bottom-up; PNG masks are top-down
                    for (int y = 0; y < height; y++)
                    {
                        long srcRow = (long)(height - 1 - y) * width * 4;
                        long dstRow = (long)y * width;

                        for (int x = 0; x < width; x++)
                        {
                            long o = srcRow + x * 4;

                            byte weight = 0;
                            if (index[o + 2] == target) weight = Math.Max(weight, intensity[o + 2]);
                            if (index[o + 1] == target) weight = Math.Max(weight, intensity[o + 1]);
                            if (index[o + 0] == target) weight = Math.Max(weight, intensity[o + 0]);
                            if (index[o + 3] == target) weight = Math.Max(weight, intensity[o + 3]);

                            coverage[dstRow + x] = weight;
                        }
                    }

                    // 5-tap Gaussian blur to eliminate the 4-layer cutoff staircases
                    coverage = SmoothCoverage(coverage, width, height);
                    Interlocked.Increment(ref painted);
                }
                else
                {
                    Interlocked.Increment(ref blanked);
                }

                PngWriter.WriteGray8(Path.Combine(destination, file), width, height, coverage);
            });
        }

        Console.WriteLine($"  terrain masks: {painted} smoothed from blend, {blanked} blanked (masks + masks_gen), {carried} non-mask files carried");
    }

    /// <summary>
    /// Separable 5-tap Gaussian smoothing filter [1, 4, 6, 4, 1] / 16.
    /// Eliminates sharp 1-pixel cutoff edges where materials enter/leave the top 4 blend slots.
    /// </summary>
    private static byte[] SmoothCoverage(byte[] src, int width, int height)
    {
        var temp = new byte[src.Length];
        var dst = new byte[src.Length];

        // Horizontal Pass
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

        // Vertical Pass
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

    private static byte[]? ReadTga(string path)
    {
        if (!File.Exists(path)) return null;

        byte[] file = File.ReadAllBytes(path);
        if (file.Length < 18) return null;

        int idLength = file[0];
        int offset = 18 + idLength;
        if (file[2] != 2 || file[16] != 32 || offset >= file.Length) return null;

        var pixels = new byte[file.Length - offset];
        Array.Copy(file, offset, pixels, 0, pixels.Length);
        return pixels;
    }
}