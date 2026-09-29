using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// A hand-painted impassable mask — <see cref="MapConfig.ImpassableMaskPath"/> — read onto the
/// province raster as one flag per pixel.
///
/// The contract is deliberately the simplest thing a paint program can produce: any image, white
/// for impassable and black for passable. Pixels are thresholded on brightness at mid grey, so an
/// anti-aliased brush edge, a grey stroke or a colour accidentally left on the layer all still read
/// as one of the two. Alpha is ignored: a transparent PNG exported from a layer with nothing under
/// it decodes as black, which is the passable default a painter expects.
///
/// Size is forgiven as well. The mask is meant to be painted over provinces.png, but a user who
/// opened heightmap.png instead (twice the size) or a resized copy gets it nearest-sampled onto the
/// province raster rather than rejected, with a console line saying so — a wall drawn a few pixels
/// thick survives the resample because it only has to touch a province, not cover it.
/// </summary>
public static class ImpassableMask
{
    /// <summary>
    /// One flag per province-raster pixel, true where the mask is white; null when no mask is set.
    /// A path that is set but unreadable throws, as the Azgaar loader does — a user who has
    /// pointed at a mask wants the run stopped on a typo, not quietly given the relief scoring.
    /// </summary>
    public static bool[]? Load(MapConfig cfg, int width, int height)
    {
        string path = cfg.ImpassableMaskPath;
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Impassable mask not found: {path}. Clear ImpassableMaskPath to use the built-in relief scoring.", path);

        var image = Io.DdsReader.Load(path)
            ?? throw new InvalidDataException($"Impassable mask could not be decoded as an image: {path}");

        var painted = new bool[width * height];
        long white = 0;

        bool sameSize = image.Width == width && image.Height == height;
        for (int y = 0; y < height; y++)
        {
            // Nearest-neighbour: pick the source row/column whose centre this pixel falls on.
            int sy = sameSize ? y : Math.Min(image.Height - 1, (int)((y + 0.5) * image.Height / height));
            for (int x = 0; x < width; x++)
            {
                int sx = sameSize ? x : Math.Min(image.Width - 1, (int)((x + 0.5) * image.Width / width));
                int p = (sy * image.Width + sx) * 4;
                // BGRA. Rec. 601 luma, integer, ≥128 is white.
                int luma = (image.Bgra[p + 2] * 299 + image.Bgra[p + 1] * 587 + image.Bgra[p] * 114) / 1000;
                if (luma < 128) continue;
                painted[y * width + x] = true;
                white++;
            }
        }

        string size = sameSize
            ? $"{image.Width}x{image.Height}"
            : $"{image.Width}x{image.Height}, resampled onto {width}x{height}";
        Console.WriteLine($"  impassable mask: {Path.GetFileName(path)} ({size}), " +
                          $"{white} white pixels ({100.0 * white / painted.Length:F2}% of the map)");
        if (white == 0)
            Console.WriteLine("  impassable mask: WARNING — no white pixels; nothing will be impassable");

        return painted;
    }
}
