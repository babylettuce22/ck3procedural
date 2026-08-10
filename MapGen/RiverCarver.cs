using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Cuts river channels into the heightmap, and decides which rivers are navigable.
///
/// CK3 has two unrelated river systems and they are drawn from opposite ends. Measured off vanilla
/// 1.19:
///
///   * <b>Drawn streams</b> — the blue indices in rivers.png. 98.30% of them lie inside *land*
///     provinces. The ground under them is notched a median of 65/65535 (0.25/255) below its dry
///     surroundings: a groove, not a valley.
///   * <b>Navigable rivers</b> — the 224 <c>river_provinces</c>, a province class of their own,
///     disjoint from the 471 sea zones. They are genuinely water: cut 478/65535 below their banks
///     with the bank higher 99.8% of the time, and held at one elevation (p5 2564, median 2676,
///     p95 3448 against land's 4689..26073) just under the waterline. Only 0.47% of vanilla's drawn
///     blue pixels fall inside one. A big river is water first and is barely drawn at all.
///
/// So this runs before anything reads the heightmap: the carve has to be in the elevation *before*
/// the land mask is taken, or a navigable river is not water and none of the rest follows. It also
/// means the climate model picks navigable rivers up on its own, since it reads the same land mask.
///
/// Why carve at all, when the tool's rule is that the heightmap passes through as its author drew
/// it. Because a river that is only painted has nowhere to sit: the ribbon is laid across whatever
/// slope it crosses and has to intersect the ground, which is what made rivers look half-buried.
/// The carve is the smallest thing that gives it a bed. Nothing else about the heightmap is touched.
/// </summary>
public static class RiverCarver
{
    /// <summary>
    /// A river's classification. Ordinary rivers are drawn into rivers.png; navigable ones become
    /// water and are drawn only as the magenta index that marks a water body.
    /// </summary>
    public sealed class Result
    {
        /// <summary>The carved elevation, full heightmap resolution.</summary>
        public required float[] Elevation { get; init; }

        /// <summary>Province resolution, 1 where a navigable river was cut.</summary>
        public required byte[] NavigableMask { get; init; }

        /// <summary>The courses that stayed ordinary rivers, for rivers.png.</summary>
        public required List<RiverCourse> Drawn { get; init; }

        /// <summary>The courses that became water.</summary>
        public required List<RiverCourse> Navigable { get; init; }
    }

    /// <summary>
    /// Splits the courses, cuts both kinds into the elevation and reports what happened.
    ///
    /// <paramref name="province"/> is the pre-carve province-resolution elevation, used only to
    /// read a course's bed height — the courses are already in province-pixel coordinates.
    /// </summary>
    public static Result Carve(float[] elevation, float[] province, List<RiverCourse> courses,
        int pw, int ph, MapConfig cfg)
    {
        float sea = cfg.Limits.SeaLevelUpper;
        var carved = (float[])elevation.Clone();
        var navigableMask = new byte[pw * ph];

        var drawn = new List<RiverCourse>();
        var navigable = new List<RiverCourse>();

        // Both gates are required and neither works alone. Discharge alone runs a sea-level canal
        // up a mountain gorge, because a big catchment says nothing about how high its channel is.
        // Height alone drowns every creek that happens to reach a coast. A navigable river is a big
        // river *in a lowland*, which is two statements.
        double dischargeGate = Math.Max(1.0, cfg.NavigableMinCatchmentCells / Math.Max(0.05, cfg.RiverPropensity));
        double heightGate = sea + cfg.NavigableMaxHeightAboveSea;

        foreach (var course in courses)
        {
            if (IsNavigable(course, province, pw, ph, dischargeGate, heightGate)) navigable.Add(course);
            else drawn.Add(course);
        }

        // Navigable first, so where a tributary is drawn over a trunk the trunk's bed wins.
        int scaleX = cfg.Width / pw, scaleY = cfg.Height / ph;
        float bed = sea - cfg.NavigableBedBelowSea;

        foreach (var course in navigable)
            Cut(course, carved, navigableMask, pw, ph, cfg, scaleX, scaleY, bed, flat: true);

        foreach (var course in drawn)
            Cut(course, carved, null, pw, ph, cfg, scaleX, scaleY, cfg.RiverNotchDepth, flat: false);

        long navPixels = 0;
        foreach (byte m in navigableMask) navPixels += m;

        Console.WriteLine($"  rivers: {navigable.Count} navigable ({navPixels:N0} px carved to water), " +
                          $"{drawn.Count} drawn and notched {cfg.RiverNotchDepth:F0}");

        return new Result
        {
            Elevation = carved,
            NavigableMask = navigableMask,
            Drawn = drawn,
            Navigable = navigable,
        };
    }

    /// <summary>
    /// Whether a course is a navigable river: enough catchment at its mouth, and a bed low enough
    /// along most of its length that flooding it does not put a lake on a hillside.
    /// </summary>
    private static bool IsNavigable(RiverCourse course, float[] province, int pw, int ph,
        double dischargeGate, double heightGate)
    {
        if (course.Points.Count < 2 || course.Discharge.Count == 0) return false;

        // A tributary of a navigable river can be navigable, but it is judged on its own water.
        float mouth = course.Discharge[^1];
        for (int i = 0; i < course.Discharge.Count; i++)
            if (course.Discharge[i] > mouth) mouth = course.Discharge[i];

        if (mouth < dischargeGate) return false;

        // Most of the course has to be low, not just its mouth. Every river reaches sea level
        // eventually; that is not what makes one navigable.
        int low = 0, counted = 0;
        for (int i = 0; i < course.Points.Count; i++)
        {
            int x = (int)course.Points[i].X, y = (int)course.Points[i].Y;
            if (x < 0 || y < 0 || x >= pw || y >= ph) continue;

            counted++;
            if (province[y * pw + x] <= heightGate) low++;
        }

        return counted > 0 && low >= counted * 0.6;
    }

    /// <summary>
    /// Cuts one course into the full-resolution elevation.
    ///
    /// <paramref name="flat"/> is the difference between the two river kinds. A navigable river is
    /// held at one height for its whole length, because it is a water body and water is level —
    /// vanilla's sit in a band a few hundred units wide over the entire map. An ordinary river is
    /// notched *relative to the ground it crosses*, so it keeps running downhill and the notch is
    /// only deep enough to give the ribbon a groove to lie in.
    /// </summary>
    private static void Cut(RiverCourse course, float[] elevation, byte[]? mask, int pw, int ph,
        MapConfig cfg, int scaleX, int scaleY, float depth, bool flat)
    {
        double logPeak = Math.Log(Math.Max(2, cfg.NavigableMinCatchmentCells));

        for (int i = 0; i < course.Points.Count; i++)
        {
            var (fx, fy) = course.Points[i];
            float discharge = i < course.Discharge.Count ? course.Discharge[i] : 1f;

            // Wider where there is more water, on a log scale, the same rule rivers.png widths use.
            double t = Math.Clamp(Math.Log(Math.Max(1, discharge)) / logPeak, 0, 1);
            double radius = flat
                ? cfg.Scaled(cfg.NavigableWidthAtVanilla) * (0.5 + 0.5 * t)
                : cfg.Scaled(cfg.RiverNotchWidthAtVanilla) * (0.4 + 0.6 * t);
            radius = Math.Max(0.75, radius);

            int px = (int)Math.Round(fx), py = (int)Math.Round(fy);
            int reach = (int)Math.Ceiling(radius);

            for (int dy = -reach; dy <= reach; dy++)
            {
                int y = py + dy;
                if (y < 0 || y >= ph) continue;

                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = px + dx;
                    if (x < 0 || x >= pw) continue;

                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;

                    if (mask is not null) mask[y * pw + x] = 1;

                    // Falls off toward the bank rather than cutting a slot, so the channel has
                    // sides the renderer can shade instead of a vertical wall one pixel wide.
                    double falloff = 1 - (d / radius) * (d / radius);

                    for (int j = 0; j < scaleY; j++)
                    {
                        var (y0, x0) = Raster.ProvinceBlock(x, y, scaleX, scaleY, cfg.Width, cfg.Height);
                        long row = (long)(y0 + j) * cfg.Width + x0;

                        for (int k = 0; k < scaleX; k++)
                        {
                            long cell = row + k;
                            float target = flat ? depth : elevation[cell] - depth * (float)falloff;
                            if (flat)
                            {
                                // Never raise ground to meet a river: a navigable river cuts down
                                // to its bed and does nothing where the ground is already lower.
                                if (elevation[cell] > target) elevation[cell] = target;
                            }
                            else if (target < elevation[cell]) elevation[cell] = target;
                        }
                    }
                }
            }
        }
    }
}
