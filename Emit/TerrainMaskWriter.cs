using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes gfx/map/terrain/masks and gfx/map/terrain/masks_gen — one 8-bit greyscale coverage
/// image per material, at province resolution, matching vanilla's format exactly.
///
/// **masks_gen was never being written at all, and that is why vanilla geography kept showing
/// through.** Vanilla ships 52 files there and materials.settings references the directory 50
/// times: they are the masks for the gen_* climate/landform family (indices 55-104). We shipped
/// none of them, so every one of vanilla's — painted for Europe and Asia — stayed loaded.
///
/// Blanking all of them is not an option either; that was tried and rendered the map as
/// missing-texture purple, because a material whose mask is empty everywhere has nothing to
/// sample. So they are generated, from exactly the same blend the detail textures were painted
/// from — each mask is that material's blend weight at each pixel. The two therefore cannot
/// disagree about what is where, by construction.
///
/// Materials the painting never used still get a file, written empty, so the vanilla copy is
/// displaced rather than left in place. A constant image compresses to a few KB, so the cost of
/// the unused ones is negligible.
/// </summary>
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

        // Material name -> id, in materials.settings file order. That order *is* the index, which
        // is why vanilla warns against reordering it.
        var names = ReadMaterialOrder(Path.Combine(gameDir, "gfx", "map", "terrain",
            "materials.settings"));
        if (names.Count == 0)
        {
            Console.WriteLine("  terrain masks: SKIPPED (materials.settings unreadable)");
            return;
        }

        var used = TerrainTextureWriter.UsedMaterials;
        int painted = 0, blanked = 0;

        // Both directories, so neither vanilla set survives.
        foreach (string sub in (string[])["masks", "masks_gen"])
        {
            string source = Path.Combine(gameDir, "gfx", "map", "terrain", sub);
            string destination = Path.Combine(terrainDir, sub);
            if (!Directory.Exists(source)) continue;
            Directory.CreateDirectory(destination);

            foreach (string path in Directory.GetFiles(source, "*.png"))
            {
                string file = Path.GetFileName(path);
                string material = Path.GetFileNameWithoutExtension(path);
                if (material.EndsWith("_mask", StringComparison.Ordinal)) material = material[..^5];

                var coverage = new byte[(long)width * height];

                if (names.TryGetValue(material, out int id) && id < 256 && used[id])
                {
                    byte target = (byte)id;

                    // The TGAs are stored bottom-up; the masks are top-down like every other PNG,
                    // so the row is flipped back on the way out.
                    Parallel.For(0, height, y =>
                    {
                        long srcRow = (long)(height - 1 - y) * width * 4;
                        long dstRow = (long)y * width;

                        for (int x = 0; x < width; x++)
                        {
                            long o = srcRow + x * 4;

                            // Layer order within the pixel: R, G, B, A -> bytes 2, 1, 0, 3.
                            byte weight = 0;
                            if (index[o + 2] == target) weight = Math.Max(weight, intensity[o + 2]);
                            if (index[o + 1] == target) weight = Math.Max(weight, intensity[o + 1]);
                            if (index[o + 0] == target) weight = Math.Max(weight, intensity[o + 0]);
                            if (index[o + 3] == target) weight = Math.Max(weight, intensity[o + 3]);

                            coverage[dstRow + x] = weight;
                        }
                    });
                    painted++;
                }
                else blanked++;

                PngWriter.WriteGray8(Path.Combine(destination, file), width, height, coverage);
            }
        }

        Console.WriteLine($"  terrain masks: {painted} painted from the blend, {blanked} blanked " +
                          $"(masks + masks_gen)");
    }

    /// <summary>
    /// Material id by name, taken from the order <c>name = "..."</c> appears in materials.settings.
    /// </summary>
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

    /// <summary>Reads back the pixel payload of an uncompressed 32-bit TGA we just wrote.</summary>
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
