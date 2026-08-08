using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

/// <summary>
/// Preview views that need the finished province partition rather than just the coarse Terra
/// world — the terrain classification and the province map itself.
///
/// Both are rendered at province resolution and downsampled by whole pixels on the way out. A
/// vanilla-size province map is 42 million pixels and no screen is going to show it; point
/// sampling keeps class boundaries crisp, which is exactly what these views exist to let you
/// judge.
/// </summary>
public static class PreviewRenderer
{
    private const int MaxWidth = 2048;

    /// <summary>
    /// The per-pixel terrain classification, in the same colours the map reads as. This is the
    /// view for judging biome blending: it shows the class boundaries the texture writer then has
    /// to blend across.
    /// </summary>
    public static TerraPreview.Image RenderTerrain(GenerationResult result)
    {
        var cfg = result.Config;
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;

        var terrain = TerrainClassifier.Classify(result.World, cfg, result.ProvinceElevation,
            result.LandMask, new Rng(cfg.Seed));

        return Downsample(width, height, i => Colour(terrain[i]));
    }

    /// <summary>Province cells in randomised colours, land and sea tinted apart.</summary>
    public static TerraPreview.Image RenderProvinces(GenerationResult result)
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

    private static (byte R, byte G, byte B) Colour(TerrainClass terrain) => terrain switch
    {
        TerrainClass.Sea => (38, 62, 96),
        TerrainClass.Beach => (222, 208, 158),
        TerrainClass.Plains => (126, 162, 88),
        TerrainClass.Farmlands => (156, 176, 74),
        TerrainClass.Steppe => (178, 168, 104),
        TerrainClass.Drylands => (192, 156, 96),
        TerrainClass.Desert => (226, 202, 138),
        TerrainClass.Jungle => (52, 116, 60),
        TerrainClass.Forest => (66, 110, 62),
        TerrainClass.Taiga => (84, 118, 100),
        TerrainClass.Wetlands => (98, 128, 118),
        TerrainClass.Floodplains => (128, 152, 96),
        TerrainClass.Hills => (140, 128, 92),
        TerrainClass.Mountains => (146, 140, 136),
        TerrainClass.DesertMountains => (176, 148, 118),
        TerrainClass.Arctic => (238, 240, 244),
        _ => (0, 0, 0),
    };

    private static TerraPreview.Image Downsample(int width, int height,
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

        return new TerraPreview.Image(rgb, outWidth, outHeight);
    }
}
