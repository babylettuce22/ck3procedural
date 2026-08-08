using Ck3MapGen.Config;
using Ck3MapGen.World;

namespace Ck3MapGen.Io;

/// <summary>
/// Visual dumps of the simulation grid. Not part of the mod output — these exist so terrain
/// changes can be eyeballed against what ck2rpg's canvas draws, which is the only practical way
/// to tell a faithful port from a plausible-looking wrong one.
/// </summary>
public static class DebugRender
{
    /// <summary>
    /// The palette for <see cref="MapGen.TerrainClass"/>, shared with the GUI preview so the two
    /// views of the same classification cannot drift apart.
    /// </summary>
    public static (byte R, byte G, byte B) TerrainColour(MapGen.TerrainClass terrain) => terrain switch
    {
        MapGen.TerrainClass.Sea => (38, 62, 96),
        MapGen.TerrainClass.Beach => (222, 208, 158),
        MapGen.TerrainClass.Plains => (126, 162, 88),
        MapGen.TerrainClass.Farmlands => (156, 176, 74),
        MapGen.TerrainClass.Steppe => (178, 168, 104),
        MapGen.TerrainClass.Drylands => (192, 156, 96),
        MapGen.TerrainClass.Desert => (226, 202, 138),
        MapGen.TerrainClass.Jungle => (52, 116, 60),
        MapGen.TerrainClass.Forest => (66, 110, 62),
        MapGen.TerrainClass.Taiga => (84, 118, 100),
        MapGen.TerrainClass.Wetlands => (98, 128, 118),
        MapGen.TerrainClass.Floodplains => (128, 152, 96),
        MapGen.TerrainClass.Hills => (140, 128, 92),
        MapGen.TerrainClass.Mountains => (146, 140, 136),
        MapGen.TerrainClass.DesertMountains => (176, 148, 118),
        MapGen.TerrainClass.Arctic => (238, 240, 244),
        _ => (0, 0, 0),
    };

    /// <summary>
    /// The classifier's own output at province resolution — what the map is actually painted from.
    ///
    /// Distinct from <see cref="WriteTerrain"/>, which draws ck2rpg's coarse <c>biome()</c> off the
    /// simulation grid and is only a port-fidelity check. That one is not what the mod ships, so
    /// judging climate or biome boundaries by it reads the wrong picture; this is the one to look
    /// at. Until this existed the classification had no CLI view at all, only the GUI preview.
    /// </summary>
    public static void WriteTerrainClasses(string path, MapGen.TerrainClass[] terrain,
        int width, int height, int maxWidth = 2048)
    {
        int step = Math.Max(1, width / maxWidth);
        int outW = width / step, outH = height / step;
        var rgb = new byte[outW * outH * 3];

        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                var (r, g, b) = TerrainColour(terrain[y * step * width + x * step]);
                int o = (y * outW + x) * 3;
                rgb[o] = r;
                rgb[o + 1] = g;
                rgb[o + 2] = b;
            }
        }

        PngWriter.WriteRgb8(path, outW, outH, rgb);
    }

    /// <summary>Elevation as greyscale, normalised across the actual range so detail is visible.</summary>
    public static void WriteElevation(string path, WorldGrid w, int scale = 1)
    {
        int min = int.MaxValue, max = int.MinValue;
        for (int i = 0; i < w.Count; i++)
        {
            if (w.Elevation[i] < min) min = w.Elevation[i];
            if (w.Elevation[i] > max) max = w.Elevation[i];
        }
        double range = Math.Max(1, max - min);

        var pixels = new byte[w.Count];
        for (int i = 0; i < w.Count; i++)
            pixels[i] = (byte)Math.Clamp((w.Elevation[i] - min) / range * 255.0, 0, 255);

        var (scaled, sw, sh) = Upscale(pixels, w.Width, w.Height, 1, scale);
        PngWriter.WriteGray8(path, sw, sh, scaled);
    }

    /// <summary>
    /// A terrain-coloured view, equivalent in spirit to drawWorld()'s "colorful" mode: enough
    /// to judge coastline shape, mountain placement, deserts and rivers at a glance.
    /// </summary>
    public static void WriteTerrain(string path, WorldGrid w, MapConfig cfg, int scale = 1)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int hills = cfg.Limits.Hills.Lower;
        int mtn = cfg.Limits.Mountains.Lower;
        int snow = cfg.Limits.Mountains.SnowLine;

        var rgb = new byte[w.Count * 3];
        for (int i = 0; i < w.Count; i++)
        {
            int e = w.Elevation[i];
            (byte r, byte g, byte b) c;

            if (w.River[i]) c = (60, 120, 220);
            else if (w.Lake[i]) c = (40, 90, 180);
            else if (e < 0) c = (8, 20, 70);
            else if (e < sea) c = (20, 70, 130);
            else if (w.Beach[i]) c = (232, 210, 160);
            else if (e < hills) c = w.Desert[i] ? ((byte)216, (byte)196, (byte)120) : ((byte)90, (byte)132, (byte)60);
            else if (e < mtn) c = w.Desert[i] ? ((byte)170, (byte)140, (byte)90) : ((byte)110, (byte)110, (byte)62);
            else if (e < snow) c = (128, 126, 122);
            else c = (245, 245, 250);

            rgb[i * 3] = c.r;
            rgb[i * 3 + 1] = c.g;
            rgb[i * 3 + 2] = c.b;
        }

        var (scaled, sw, sh) = Upscale(rgb, w.Width, w.Height, 3, scale);
        PngWriter.WriteRgb8(path, sw, sh, scaled);
    }

    /// <summary>
    /// Province map with a random colour per province, downsampled to a viewable size. Land
    /// provinces get warm hues and sea zones cool ones so the coastline stays readable.
    /// </summary>
    public static void WriteProvinces(string path, MapGen.ProvinceMap map, Core.Rng rng, int maxWidth = 2048)
    {
        var palette = new byte[map.Count * 3];
        for (int i = 0; i < map.Count; i++)
        {
            bool land = map.Seeds[i].IsLand;
            palette[i * 3] = (byte)(land ? rng.Int(90, 255) : rng.Int(0, 70));
            palette[i * 3 + 1] = (byte)(land ? rng.Int(60, 200) : rng.Int(20, 110));
            palette[i * 3 + 2] = (byte)(land ? rng.Int(30, 140) : rng.Int(120, 255));
        }

        int step = Math.Max(1, map.Width / maxWidth);
        int outW = map.Width / step, outH = map.Height / step;
        var rgb = new byte[outW * outH * 3];

        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                int label = map.Label[(y * step) * map.Width + x * step];
                int di = (y * outW + x) * 3;
                if (label < 0) continue;
                rgb[di] = palette[label * 3];
                rgb[di + 1] = palette[label * 3 + 1];
                rgb[di + 2] = palette[label * 3 + 2];
            }
        }

        PngWriter.WriteRgb8(path, outW, outH, rgb);
    }

    /// <summary>Nearest-neighbour upscale, so a 512-wide sim grid is legible on screen.</summary>
    private static (byte[] Pixels, int Width, int Height) Upscale(
        byte[] src, int width, int height, int channels, int scale)
    {
        if (scale <= 1) return (src, width, height);

        int dw = width * scale, dh = height * scale;
        var dst = new byte[dw * dh * channels];
        for (int y = 0; y < dh; y++)
        {
            int sy = y / scale;
            for (int x = 0; x < dw; x++)
            {
                int sx = x / scale;
                int si = (sy * width + sx) * channels;
                int di = (y * dw + x) * channels;
                for (int c = 0; c < channels; c++) dst[di + c] = src[si + c];
            }
        }
        return (dst, dw, dh);
    }
}
