using Ck3MapGen.Config;
using Ck3MapGen.Core;
using SixLabors.ImageSharp.PixelFormats;

// WinForms drags in System.Drawing, which has its own Image. Alias rather than rely on using
// order, so the reference cannot silently rebind to the wrong one later.
using SharpImage = SixLabors.ImageSharp.Image;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Builds <see cref="TerrainData"/> from a heightmap on disk, so the whole mod can be emitted
/// around a map somebody drew rather than one this program generated.
///
/// Everything downstream — the province partition, rivers.png, the terrain textures, the title
/// hierarchy — reads <see cref="TerrainData"/> and nothing else, so it cannot tell the difference.
/// Rivers and lakes are derived from the imported field with exactly the same drainage code the
/// generator uses on its own output.
///
/// Reading is done with ImageSharp rather than by hand. The project writes its own PNGs because
/// CK3 needs an exact pixel format per file and a general imaging library will not guarantee one;
/// reading has no such constraint, and hand-rolling an inflate to avoid a dependency already in
/// the project would be its own bug surface.
/// </summary>
public static class HeightmapSource
{
    /// <summary>
    /// How much coarser than the heightmap the climate grid is. 16 reproduces what the old size
    /// presets used: an 8192-wide map got a 512-wide grid, a vanilla-sized one 1024.
    /// </summary>
    private const int CoarseGridDivisor = 16;

    /// <summary>
    /// Loads a heightmap and derives everything from it.
    ///
    /// The image is authoritative about map size: <paramref name="cfg"/>'s Width and Height are set
    /// from it, because provinces.png, rivers.png and every terrain texture are sized off those and
    /// a mismatch is a silent CK3 failure. Dimensions must be even, since the province map is
    /// exactly half the heightmap.
    /// </summary>
    public static TerrainData Load(string path, MapConfig cfg, Rng rng)
    {
        using var image = SharpImage.Load<L16>(path);

        if (image.Width % 2 != 0 || image.Height % 2 != 0)
            throw new InvalidOperationException(
                $"Heightmap is {image.Width}x{image.Height}; both dimensions must be even because " +
                "provinces.png and rivers.png are exactly half the heightmap's resolution.");

        cfg.Width = image.Width;
        cfg.Height = image.Height;

        // The coarse climate grid follows the image too. It used to come from a size preset, which
        // no longer exists now that the image is the only source of truth about size — and a fixed
        // coarse grid against a variable heightmap would mean climate bands sampled at a different
        // resolution on every map. Clamped at the top because the grid is a summary: past about a
        // thousand cells across it stops being cheaper than the field it summarises.
        cfg.WorldWidth = Math.Clamp(cfg.Width / CoarseGridDivisor, 128, 1024);
        cfg.WorldHeight = Math.Max(64, cfg.WorldWidth / 2);

        Console.WriteLine($"Heightmap loaded from {path}: {cfg.Width}x{cfg.Height}, " +
                          $"provinces {cfg.ProvinceWidth}x{cfg.ProvinceHeight}, " +
                          $"climate grid {cfg.WorldWidth}x{cfg.WorldHeight}");

        var elevation = ToSimulationScale(image, cfg);
        Report(elevation, cfg);

        return TerrainData.FromElevation(elevation, cfg, rng);
    }

    /// <summary>
    /// CK3's 16-bit height scale back onto the simulation's elevation units.
    ///
    /// The inverse of what <c>MapDataWriter.ElevationTo16</c> does on the way out, piecewise about
    /// the water plane so that a pixel at exactly <c>WaterLevel16</c> comes back at exactly sea
    /// level and the coastline survives the round trip.
    ///
    /// Note the round trip is not the identity by design: on the way out the land is remapped onto
    /// vanilla's measured hypsometric curve, which rescales heights (never moves them, the mapping
    /// is monotonic). Set <c>MatchVanillaHypsometry</c> false to keep an imported map's own
    /// height distribution.
    /// </summary>
    private static float[] ToSimulationScale(SixLabors.ImageSharp.Image<L16> image, MapConfig cfg)
    {
        int width = cfg.Width, height = cfg.Height;
        var elevation = new float[(long)width * height];

        float sea = cfg.Limits.SeaLevelUpper;
        float floor = cfg.SeaFloorElevation;
        float top = cfg.PeakElevation;
        const float water = Emit.MapDataWriter.WaterLevel16;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                long offset = (long)y * width;

                for (int x = 0; x < row.Length; x++)
                {
                    float v = row[x].PackedValue;
                    elevation[offset + x] = v <= water
                        ? floor + v / water * (sea - floor)
                        : sea + 1f + (v - water) / (65535f - water) * (top - sea - 1f);
                }
            }
        });

        return elevation;
    }

    private static void Report(float[] elevation, MapConfig cfg)
    {
        float sea = cfg.Limits.SeaLevelUpper;
        long land = 0;
        float min = float.MaxValue, max = float.MinValue;

        foreach (float e in elevation)
        {
            if (e > sea) land++;
            if (e < min) min = e;
            if (e > max) max = e;
        }

        Console.WriteLine($"  elevation {min:F0}..{max:F0} (sea {sea:F0}), " +
                          $"{100.0 * land / elevation.Length:F1}% land");

        if (land == 0)
            Console.WriteLine("  WARNING: no pixel is above the water plane. Expected a 16-bit " +
                              "greyscale heightmap on CK3's scale, where water is at or below " +
                              $"{Emit.MapDataWriter.WaterLevel16}.");
    }
}
