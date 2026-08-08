using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// Converts Terra's normalised heights onto the integer elevation scale the rest of the project is
/// written against, where sea level is <c>Limits.SeaLevelUpper</c> and the mountain line is
/// <c>Limits.Mountains.Lower</c>.
///
/// The map is piecewise linear with its knee placed on a percentile of this map's own land, so a
/// fixed share of land lands above the mountain line whatever the seed did. That matters because
/// <see cref="Biome"/> switches to <c>Mountain</c> on an absolute comparison against 255 and
/// <c>Terrain.FloodFillMountains</c> groups ranges the same way — thresholds that were meaningless
/// under the old generator, whose absolute elevations depended on how many spread rounds a seed
/// happened to need, and which every downstream classifier had to work around by re-deriving its
/// own percentiles.
///
/// Being monotonic, it moves nothing: the terrain keeps exactly the shape the erosion gave it, and
/// only the numbers attached to it change. <c>MapDataWriter.ElevationTo16</c> then applies
/// vanilla's measured hypsometric curve on top, which is likewise monotonic.
/// </summary>
public sealed class TerraScale
{
    private float _sea, _floor, _knee, _peak;
    private int _seaUnits, _mountainUnits, _topUnits, _floorUnits;

    public static TerraScale Calibrate(float[] height, float sea, MapConfig cfg)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (float h in height)
        {
            if (h < min) min = h;
            if (h > max) max = h;
        }

        return new TerraScale
        {
            _sea = sea,
            _floor = MathF.Min(min, sea - 1e-3f),
            _knee = Field.Quantile(height, i => height[i] > sea, 1.0 - cfg.TerraMountainShare),
            _peak = MathF.Max(max, sea + 1e-3f),
            _seaUnits = cfg.Limits.SeaLevelUpper,
            _mountainUnits = cfg.Limits.Mountains.Lower,
            _topUnits = cfg.TerraTopElevation,
            _floorUnits = cfg.TerraFloorElevation,
        };
    }

    public float Convert(float h)
    {
        if (h <= _sea)
        {
            float t = (h - _floor) / MathF.Max(1e-6f, _sea - _floor);
            return _floorUnits + Math.Clamp(t, 0f, 1f) * (_seaUnits - _floorUnits);
        }

        if (h <= _knee)
        {
            float t = (h - _sea) / MathF.Max(1e-6f, _knee - _sea);
            return _seaUnits + 1 + Math.Clamp(t, 0f, 1f) * (_mountainUnits - _seaUnits - 1);
        }

        float u = (h - _knee) / MathF.Max(1e-6f, _peak - _knee);
        return _mountainUnits + 1 + Math.Clamp(u, 0f, 1f) * (_topUnits - _mountainUnits - 1);
    }

    public void ApplyInPlace(float[] height)
        => Parallel.For(0, height.Length, i => height[i] = Convert(height[i]));

    public override string ToString()
        => $"floor {_floor:F3} / sea {_sea:F3} / knee {_knee:F3} / peak {_peak:F3} " +
           $"-> {_floorUnits}..{_seaUnits}..{_mountainUnits}..{_topUnits}";
}
