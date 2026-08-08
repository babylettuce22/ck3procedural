namespace Ck3MapGen.MapGen;

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
