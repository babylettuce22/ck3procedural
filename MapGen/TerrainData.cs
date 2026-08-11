using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Everything downstream of the heightmap consumes, and the only thing it consumes.
///
/// The province partition, the title hierarchy, rivers.png, the terrain textures and every emitter
/// read this object and nothing else, which is what lets the heightmap come from anywhere — any
/// program that can write a 16-bit greyscale PNG on CK3's height scale is a valid front end for
/// this one.
///
/// Rivers and lakes used to be part of the contract, on the grounds that they have to agree with
/// the heightmap pixel for pixel and deriving them twice is how things drift apart. That reasoning
/// still holds and the hydrology that was here did not: courses came out different at every map
/// resolution and most of them wandered without reaching an outlet. It was removed on 2026-08-10 to
/// be rebuilt. Whatever replaces it belongs here, for the original reason.
/// </summary>
public sealed class TerrainData
{
    /// <summary>Full heightmap resolution, in the simulation's integer elevation scale.</summary>
    public required float[] Elevation { get; init; }

    /// <summary>Province resolution, the same field downsampled 2:1. Drives the partition.</summary>
    public required float[] ProvinceElevation { get; init; }

    /// <summary>
    /// Derives everything a full-resolution elevation field implies, which is currently the
    /// province-resolution copy and nothing else.
    ///
    /// Deliberately does no erosion and no channel carving. The elevation passed in is taken as
    /// authoritative: the heightmap should come out the other end as the map its author drew.
    ///
    /// Takes no <c>Rng</c>. It used to, for the river courses; nothing random is left. A rebuilt
    /// hydrology will want one back.
    /// </summary>
    public static TerrainData FromElevation(float[] elevation, MapConfig cfg)
    {
        // Raster.ProvinceElevation, not Field.Downsample: the two grids do not line up on the
        // obvious block, and this is the one place that knows where they do.
        var province = Raster.ProvinceElevation(elevation, cfg);

        return new TerrainData
        {
            Elevation = elevation,
            ProvinceElevation = province,
        };
    }
}
