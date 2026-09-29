namespace Ck3MapGen.MapGen;

/// <summary>
/// Flat-map geometry on province-pixel positions. The map does not wrap, so distances are plain
/// Euclidean. Callers import it with <c>using static</c>.
/// </summary>
internal static class Planar
{
    public static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
