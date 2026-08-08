namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// The coarse "base" world the tectonics and the main erosion run on — a quarter of the exported
/// heightmap in each axis.
///
/// Heights here are normalised to roughly [0, 1] with sea level at a fixed <see cref="SeaLevel"/>,
/// not on the ck2rpg integer scale. That is the point: the old scale had no fixed meaning, because
/// the magma simulation's absolute values depended on how many spread rounds a seed happened to
/// need, so every threshold downstream had to be re-derived as a percentile of the map's own
/// distribution. Here "0.8" means the same thing on every seed and at every map size.
/// </summary>
public sealed class TerraWorld
{
    public required int Width;
    public required int Height;

    /// <summary>Normalised elevation. Land is above <see cref="SeaLevel"/>.</summary>
    public required float[] Land;

    public required float SeaLevel;

    /// <summary>0..1, for the preview render only; the real climate model is <see cref="Climate"/>.</summary>
    public float[] Moisture = [];

    /// <summary>Tectonic uplift rate, 0..1. Kept for the debug render — it is what makes ranges.</summary>
    public float[] Uplift = [];

    /// <summary>Drainage area in cells, from the last erosion iteration.</summary>
    public float[] Flow = [];

    /// <summary>Base-resolution river courses, cell indices in flow order. Preview only.</summary>
    public List<int[]> Rivers = [];

    public int Idx(int x, int y) => y * Width + x;
}

/// <summary>
/// One river, as a smooth polyline in province-map pixels.
///
/// The old generator walked the coarse simulation grid through cardinal neighbours only and then
/// Bresenham'd the result up 9x, which is why courses read as axis-aligned staircases. A course
/// here is extracted from the drainage network, simplified, and then resampled along a
/// Catmull-Rom spline with a meander offset, so it curves the way a real river does while still
/// following the ground the erosion actually cut.
/// </summary>
public sealed class RiverCourse
{
    /// <summary>Points in flow order, source first, in province-map pixel coordinates.</summary>
    public List<(float X, float Y)> Points = [];

    /// <summary>Drainage area at each point, parallel to <see cref="Points"/>. Drives width.</summary>
    public List<float> Discharge = [];

    /// <summary>True when this course ends by joining another river rather than reaching the sea.</summary>
    public bool IsTributary;
}
