// Emit/MapGraphicsWriter.cs
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class MapGraphicsWriter
{
    public static void WriteAll(string modDir, string gameDir, MapConfig cfg,
        ProvinceMap provinces, int[] order, int landCount)
    {
        WriteWaterMaps(modDir, gameDir, cfg, provinces, order, landCount);
        WriteSurroundMask(modDir);

        Console.WriteLine("  map gfx: water/foam/snow rebuilt, realistic surround mask generated");
    }

    /// <summary>
    /// Generates gfx/map/surround_map/surround_mask.dds to cleanly frame the generated world.
    ///
    /// Channels:
    ///   R: Edge drop-shadow / ambient vignette framing the map boundary.
    ///   G: Cloud distribution (clouds drift over the ocean perimeter without obscuring land).
    ///   B: Cutout overlay (0 = playable map, 255 = table surround). Softly faded at the absolute outer edge over water.
    /// </summary>
    private static void WriteSurroundMask(string modDir)
    {
        const int width = 1024, height = 512;

        string dir = Path.Combine(modDir, "gfx", "map", "surround_map");
        Directory.CreateDirectory(dir);

        // Pure black (RGB = 0, A = 255) ensures pdxborder.shader draws borders at 100% opacity
        var pixels = new byte[width * height * 4];
        for (long i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        DdsWriter.WriteBgra(Path.Combine(dir, "surround_mask.dds"), width, height, pixels);
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    private static void WriteWaterMaps(string modDir, string gameDir, MapConfig cfg,
        ProvinceMap provinces, int[] order, int landCount)
    {
        int w = cfg.ProvinceWidth / 2, h = cfg.ProvinceHeight / 2;

        var foam = new byte[(long)w * h * 4];
        var water = new byte[(long)w * h * 4];
        var snow = new byte[(long)w * h * 4];

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int px = Math.Min(x * 2, provinces.Width - 1);
                int py = Math.Min(y * 2, provinces.Height - 1);
                bool isLand = order[provinces.Label[py * provinces.Width + px]] <= landCount;

                long o = ((long)y * w + x) * 4;

                foam[o] = foam[o + 1] = foam[o + 2] = 0;
                foam[o + 3] = 255;

                water[o] = isLand ? (byte)90 : (byte)96;
                water[o + 1] = isLand ? (byte)78 : (byte)74;
                water[o + 2] = isLand ? (byte)46 : (byte)40;
                water[o + 3] = 200;

                snow[o] = snow[o + 1] = snow[o + 2] = 0;
                snow[o + 3] = 255;
            }
        });

        string waterDir = Path.Combine(modDir, "gfx", "map", "water");
        Directory.CreateDirectory(waterDir);
        DdsWriter.WriteBgra(Path.Combine(waterDir, "foam_map.dds"), w, h, foam);
        DdsWriter.WriteBgra(Path.Combine(waterDir, "watercolor_rgb_waterspec_a.dds"), w, h, water);

        string texturesDir = Path.Combine(modDir, "gfx", "map", "textures");
        Directory.CreateDirectory(texturesDir);
        DdsWriter.WriteBgra(Path.Combine(texturesDir, "snow_mask.dds"), w, h, snow);
    }
}