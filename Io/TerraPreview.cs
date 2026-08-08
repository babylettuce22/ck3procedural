using Ck3MapGen.MapGen.Terra;

namespace Ck3MapGen.Io;

/// <summary>
/// Preview renders for the from-scratch generator, so terrain can be judged without building a
/// mod and repacking a heightmap for every parameter change.
///
/// Each view is a <c>Render*</c> returning a packed RGB buffer, with the <c>Write*</c> wrappers
/// on top. The split exists so the GUI can display a preview without a round trip through the
/// filesystem — rendering straight to a bitmap is the difference between a responsive parameter
/// panel and one that writes four PNGs on every slider drag.
/// </summary>
public static class TerraPreview
{
    public readonly record struct Image(byte[] Rgb, int Width, int Height);

    public static void WriteAll(string outDir, TerraWorld world)
    {
        Write(Path.Combine(outDir, "terra_relief.png"), RenderRelief(world));
        Write(Path.Combine(outDir, "terra_height.png"), RenderHeight(world));
        Write(Path.Combine(outDir, "terra_moisture.png"), RenderMoisture(world));
        Write(Path.Combine(outDir, "terra_rivers.png"), RenderRivers(world));
    }

    private static void Write(string path, Image image)
        => PngWriter.WriteRgb8(path, image.Width, image.Height, image.Rgb);

    /// <summary>
    /// Hillshaded colour relief. This is the one worth looking at: flat greyscale hides whether
    /// erosion actually produced valley networks, whereas a shaded render shows ridgelines and
    /// drainage the way the game's lighting will.
    /// </summary>
    public static Image RenderRelief(TerraWorld world)
    {
        int w = world.Width, h = world.Height;
        var rgb = new byte[w * h * 3];
        float sea = SeaOnGrid(world);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float e = world.Land[i];

                // Slope-based shading with light from the north-west.
                int xl = (x - 1 + w) % w, xr = (x + 1) % w;
                int yu = Math.Max(0, y - 1), yd = Math.Min(h - 1, y + 1);
                float dx = world.Land[y * w + xr] - world.Land[y * w + xl];
                float dy = world.Land[yd * w + x] - world.Land[yu * w + x];
                double shade = Math.Clamp(0.5 - (dx + dy) * 26.0, 0.15, 1.35);

                (byte r, byte g, byte b) c;
                if (e <= sea)
                {
                    double depth = Math.Clamp((sea - e) / Math.Max(1e-5f, sea) * 3.0, 0, 1);
                    c = ((byte)(38 + 26 * (1 - depth)), (byte)(70 + 44 * (1 - depth)),
                         (byte)(104 + 48 * (1 - depth)));
                    shade = 1.0;
                }
                else
                {
                    double t = Math.Clamp((e - sea) / Math.Max(1e-5f, 1 - sea), 0, 1);
                    c = t < 0.12 ? ((byte)116, (byte)146, (byte)86)
                      : t < 0.35 ? ((byte)92, (byte)124, (byte)68)
                      : t < 0.58 ? ((byte)140, (byte)128, (byte)84)
                      : t < 0.78 ? ((byte)128, (byte)112, (byte)98)
                      : ((byte)232, (byte)234, (byte)238);
                }

                rgb[i * 3] = Clamp(c.r * shade);
                rgb[i * 3 + 1] = Clamp(c.g * shade);
                rgb[i * 3 + 2] = Clamp(c.b * shade);
            }
        }

        return new Image(rgb, w, h);
    }

    public static Image RenderHeight(TerraWorld world)
    {
        int w = world.Width, h = world.Height;
        var rgb = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++)
        {
            byte v = Clamp(world.Land[i] * 255.0);
            rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = v;
        }
        return new Image(rgb, w, h);
    }

    public static Image RenderMoisture(TerraWorld world)
    {
        int w = world.Width, h = world.Height;
        var rgb = new byte[w * h * 3];
        float sea = SeaOnGrid(world);

        for (int i = 0; i < w * h; i++)
        {
            if (world.Land[i] <= sea)
            {
                rgb[i * 3] = 40; rgb[i * 3 + 1] = 60; rgb[i * 3 + 2] = 84;
                continue;
            }
            double m = world.Moisture[i];
            rgb[i * 3] = Clamp(220 - 170 * m);
            rgb[i * 3 + 1] = Clamp(180 + 30 * m);
            rgb[i * 3 + 2] = Clamp(110 + 60 * m);
        }

        return new Image(rgb, w, h);
    }

    public static Image RenderRivers(TerraWorld world)
    {
        int w = world.Width, h = world.Height;
        var rgb = new byte[w * h * 3];
        float sea = SeaOnGrid(world);

        for (int i = 0; i < w * h; i++)
        {
            byte v = world.Land[i] <= sea ? (byte)28 : (byte)200;
            rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = v;
        }

        foreach (var course in world.Rivers)
            foreach (int i in course)
            {
                if (i < 0 || i >= w * h) continue;
                rgb[i * 3] = 30; rgb[i * 3 + 1] = 110; rgb[i * 3 + 2] = 220;
            }

        return new Image(rgb, w, h);
    }

    /// <summary>Sea level expressed against the coarse grid, which erosion renormalised.</summary>
    private static float SeaOnGrid(TerraWorld world)
    {
        var sorted = (float[])world.Land.Clone();
        Array.Sort(sorted);
        int landCells = 0;
        foreach (float v in world.Land) if (v > world.SeaLevel) landCells++;
        int index = Math.Clamp(sorted.Length - landCells - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);
}
