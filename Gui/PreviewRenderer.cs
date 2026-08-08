using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

/// <summary>
/// The preview views, all rendered off the finished province partition.
///
/// Everything here is rendered at province resolution and downsampled by whole pixels on the way
/// out. A vanilla-size province map is 42 million pixels and no screen is going to show it; point
/// sampling keeps class and border boundaries crisp, which is exactly what these views exist to
/// let you judge.
/// </summary>
public static class PreviewRenderer
{
    /// <summary>A rendered view: packed RGB, three bytes per pixel.</summary>
    public readonly record struct Image(byte[] Rgb, int Width, int Height);

    private const int MaxWidth = 2048;

    /// <summary>
    /// The per-pixel terrain classification, in the same colours the map reads as. This is the
    /// view for judging biome blending: it shows the class boundaries the texture writer then has
    /// to blend across.
    /// </summary>
    public static Image RenderTerrain(GenerationResult result)
    {
        var cfg = result.Config;
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;

        var terrain = TerrainClassifier.Classify(result.World, cfg, result.ProvinceElevation,
            result.LandMask, new Rng(cfg.Seed));

        return Downsample(width, height, i => Colour(terrain[i]));
    }

    /// <summary>
    /// Hillshaded elevation from the province raster.
    ///
    /// Built from <see cref="GenerationResult.ProvinceElevation"/>, which is the heightmap as it
    /// was read, so this shows the relief the mod will actually ship rather than an intermediate.
    /// </summary>
    public static Image RenderElevation(GenerationResult result)
    {
        var cfg = result.Config;
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        var elevation = result.ProvinceElevation;

        float sea = cfg.Limits.SeaLevelUpper;
        float peak = Math.Max(sea + 1f, cfg.PeakElevation);

        return Downsample(width, height, i =>
        {
            float e = elevation[i];
            if (e <= sea)
            {
                float depth = Math.Clamp((sea - e) / Math.Max(1f, sea - cfg.SeaFloorElevation), 0, 1);
                return ((byte)(38 + 26 * (1 - depth)), (byte)(70 + 44 * (1 - depth)),
                        (byte)(104 + 48 * (1 - depth)));
            }

            // Slope from the two neighbours that exist regardless of where we sampled.
            int x = i % width, y = i / width;
            float left = elevation[y * width + Math.Max(0, x - 1)];
            float up = elevation[Math.Max(0, y - 1) * width + x];
            double shade = Math.Clamp(0.75 - ((e - left) + (e - up)) * 0.05, 0.25, 1.35);

            double t = Math.Clamp((e - sea) / (peak - sea), 0, 1);
            var (r, g, b) = t < 0.10 ? (116, 146, 86)
                : t < 0.28 ? (92, 124, 68)
                : t < 0.48 ? (140, 128, 84)
                : t < 0.70 ? (128, 112, 98)
                : (232, 234, 238);

            return ((byte)Math.Clamp(r * shade, 0, 255), (byte)Math.Clamp(g * shade, 0, 255),
                    (byte)Math.Clamp(b * shade, 0, 255));
        });
    }

    /// <summary>Province cells in randomised colours, land and sea tinted apart.</summary>
    public static Image RenderProvinces(GenerationResult result)
    {
        var map = result.Provinces;
        var rng = new Rng(result.Config.Seed ^ 0x9E37);

        var colours = new (byte R, byte G, byte B)[map.Count];
        for (int i = 0; i < map.Count; i++)
        {
            bool land = map.Seeds[i].IsLand;
            colours[i] = land
                ? ((byte)rng.Int(60, 235), (byte)rng.Int(90, 235), (byte)rng.Int(55, 190))
                : ((byte)rng.Int(20, 70), (byte)rng.Int(45, 105), (byte)rng.Int(90, 170));
        }

        return Downsample(map.Width, map.Height, i => colours[map.Label[i]]);
    }

    private static (byte R, byte G, byte B) Colour(TerrainClass terrain)
        => Io.DebugRender.TerrainColour(terrain);

    private static Image Downsample(int width, int height,
        Func<int, (byte R, byte G, byte B)> colour)
    {
        int step = Math.Max(1, (width + MaxWidth - 1) / MaxWidth);
        int outWidth = width / step, outHeight = height / step;
        var rgb = new byte[outWidth * outHeight * 3];

        Parallel.For(0, outHeight, y =>
        {
            for (int x = 0; x < outWidth; x++)
            {
                var (r, g, b) = colour((y * step) * width + x * step);
                int o = (y * outWidth + x) * 3;
                rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = b;
            }
        });

        return new Image(rgb, outWidth, outHeight);
    }
}
