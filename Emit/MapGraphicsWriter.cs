using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Replaces the map-sized graphics under gfx/map that are painted for vanilla's world.
///
/// These are easy to miss because none of them errors: they are the right *format*, and once the
/// map is emitted at vanilla dimensions they are the right *size* too, so CK3 loads them happily
/// and draws Europe's coastlines and terrain over ours. The giveaway in game is recognisable
/// geography — Italy showing up as relief in open ocean.
///
/// The offenders, all inherited whole from vanilla until now:
///   water/foam_map.dds                      surf drawn along vanilla's coasts
///   water/watercolor_rgb_waterspec_a.dds    water colour and depth tint from vanilla bathymetry
///   textures/snow_mask.dds                  snow wherever vanilla is cold
///   terrain/masks/*.png, masks_gen/*.png    per-material terrain masks, one per material
///   terrain/flat_maps/flatmap_tgp.dds       the second flatmap variant
///
/// Masks are blanked rather than painted: the runtime blend is driven by detail_index and
/// detail_intensity (see <see cref="TerrainTextureWriter"/>), and the mask images exist for the
/// map editor's painting workflow. A black mask contributes nothing, which is what we want —
/// but if terrain ever renders flat, this is the first thing to suspect.
/// </summary>
public static class MapGraphicsWriter
{
    public static void WriteAll(string modDir, string gameDir, MapConfig cfg,
        ProvinceMap provinces, int[] order, int landCount)
    {
        int w = cfg.ProvinceWidth, h = cfg.ProvinceHeight;

        WriteWaterMaps(modDir, gameDir, cfg, provinces, order, landCount);

        Console.WriteLine("  map gfx: water/foam/snow rebuilt");
    }

    // gfx/map/terrain/masks and masks_gen are owned by TerrainMaskWriter, which runs after the
    // detail textures and derives every mask from them.
    //
    // Blanking all 121 of them to black was tried once and broke terrain rendering outright: the
    // whole map drew as the missing-texture purple, because a material whose mask is empty
    // everywhere has nothing to sample. Painting them from the same blend as detail_index is what
    // that note asked for, and is what now happens — in particular masks_gen, all 52 files of
    // which we previously did not write at all, leaving vanilla's Europe-shaped gen_* coverage
    // loaded underneath our terrain.

    /// <summary>
    /// Water colour, coastline foam and the snow mask, all rebuilt against our own land/water
    /// split. Vanilla keeps these at half the province map's resolution, so we match that.
    /// </summary>
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
                // Nearest province pixel: these maps are half the province map's resolution.
                int px = Math.Min(x * 2, provinces.Width - 1);
                int py = Math.Min(y * 2, provinces.Height - 1);
                bool isLand = order[provinces.Label[py * provinces.Width + px]] <= landCount;

                long o = ((long)y * w + x) * 4;

                // Foam is drawn from this mask, so an empty one simply means no surf. Better
                // nothing than vanilla's surf tracing coastlines that are not there.
                foam[o] = foam[o + 1] = foam[o + 2] = 0;
                foam[o + 3] = 255;

                // Uniform open-water tint; RGB is colour, alpha carries specularity.
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
