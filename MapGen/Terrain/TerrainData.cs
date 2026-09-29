using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

public sealed class TerrainData
{
    /// <summary>Full heightmap resolution, in simulation elevation scale.</summary>
    public required float[] Elevation { get; init; }

    /// <summary>Province resolution, downsampled 2:1 for province partitioning.</summary>
    public required float[] ProvinceElevation { get; set; }

    public List<MajorRiverPath> MajorRiversList { get; set; } = [];

    public static TerrainData FromElevation(float[] elevation, MapConfig cfg)
    {
        var province = Raster.ProvinceElevation(elevation, cfg);

        return new TerrainData
        {
            Elevation = elevation,
            ProvinceElevation = province,
        };
    }
}