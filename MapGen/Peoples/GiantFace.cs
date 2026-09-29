namespace Ck3MapGen.MapGen;

/// <summary>
/// The giantkin face: a heavy brow shelf and a squared, jutting jaw, as ONE blendshape of the vanilla
/// head — skin-shaded for free, like the elves' ears (<see cref="PointedEars"/>).
///
/// **What it does.** The brow is a continuous bony bar across both brows, rising gently into the
/// forehead and dropping off sharply over the eyes so they sit hooded under it; it replaced vanilla's
/// brow-forward slider in the giantkin row, which only pinched the brows into a frown. The jaw pushes
/// the gonial angles out and down, brings a broadened chin forward and down, and lets the lower lip
/// ride forward a little (a slight underbite).
///
/// **How.** Analytic displacement fields over the rest head, masked to the front of the face, then
/// smoothed over the mesh graph (seam duplicates welded) so no edge of a field shows. File space is
/// y up, the face toward −z, +x the head's left.
///
/// **One design space for both heads.** The fields are authored on the male head. The female head is
/// narrower, her brow sits 3.1 units lower but her mouth only 2.1, and her face is shallower, so a
/// single offset put her brow into a scowl (rendered 2026-09-29). Each field is instead evaluated
/// with the head mapped into male space by its OWN landmark — the brow bones for the brow, the lower
/// lip for the jaw — and x scaled by the head's width. A patch that moves the head moves the
/// landmarks with it.
///
/// Tuned from renders with the user 2026-09-29 (<c>Desktop/ck3devtools/giant_probe/giantface.py</c>,
/// which this matches): brow a little stronger, chin softened. Then ~8% stronger all round on the
/// user's call before the first in-game look (brow 1.7→1.85, jaw angle 1.1→1.2, chin 0.85→0.9; the
/// .py was not updated).
/// </summary>
internal static class GiantFace
{
    /// <summary>The gene (BaseFilesToCopy/Core/common/genes/gen_bs_giant_face.txt) and its templates.</summary>
    public const string Gene = "gen_bs_giant_face";
    public const string NoneTemplate = "gen_giant_face_none";
    public const string OnTemplate = "gen_giant_face_on";
    public const string Attribute = "gen_bs_giant_face";

    public static string BlendShapeId(string sex) => $"{sex}_bs_gen_giant_face";

    // ---- brow shelf ----
    private const double BrowY = 49.4;        // crest height at the centre, male space
    private const double BrowArch = 0.25;     // the crest rises this much over each eye
    private const double BrowOut = 1.85;      // peak forward push of the crest
    private const double BrowDown = 0.35;     // the crest also sags down over the lids
    private const double BrowAbove = 2.6;     // fade up into the forehead (gradual)
    private const double BrowBelow = 1.0;     // fade down onto the lids (sharp: the overhang)
    private const double BrowHalf = 6.2;      // half-width before the temple taper

    // ---- jaw ----
    private const double JawOut = 1.2, JawDown = 0.75;         // gonial angles
    private const double ChinFwd = 0.9, ChinDown = 0.35, ChinWide = 0.6;
    private const double LipFwd = 0.35;                        // the underbite
    private const double LipY = 39.45;                         // male lip line

    // The male landmarks the fields were authored against: centroid (y, z) of the vertices whose
    // dominant bone is the named one.
    private static readonly (string Tag, double Y, double Z) BrowLandmark = ("browInner", 49.07, -10.64);
    private static readonly (string Tag, double Y, double Z) LipLandmark = ("bn_h_mouth_lowerLip", 39.11, -11.29);

    private const int SmoothPasses = 6;

    /// <param name="female">Selects the width and strength of the female head.</param>
    /// <param name="dominant">Each vertex's dominant bone name.</param>
    public static PointedEars.Result Shape(float[] p, float[] n, float[] ta, int[] tri, string[] dominant, bool female)
    {
        int count = p.Length / 3;
        double sx = female ? 0.92 : 1.0, amount = female ? 0.85 : 1.0;
        var groups = WeldGroups(p, count);
        var neighbours = Neighbours(tri, count);

        double[] Field(Func<double[], double[]> fn, (string Tag, double Y, double Z) mark)
        {
            var (ly, lz) = Landmark(p, dominant, mark.Tag);
            var q = new double[p.Length];
            for (int v = 0; v < count; v++)
            {
                q[v * 3] = p[v * 3] / sx;
                q[v * 3 + 1] = p[v * 3 + 1] - (ly - mark.Y);
                q[v * 3 + 2] = p[v * 3 + 2] - (lz - mark.Z);
            }

            var d = Smooth(fn(q), neighbours, groups, count);
            for (int i = 0; i < d.Length; i++) d[i] *= amount * (i % 3 == 0 ? sx : 1);
            return d;
        }

        var brow = Field(q => Brow(q, n, dominant), BrowLandmark);
        var jaw = Field(q => Jaw(q, dominant), LipLandmark);

        var moved = new double[p.Length];
        for (int i = 0; i < p.Length; i++) moved[i] = p[i] + brow[i] + jaw[i];
        return PointedEars.Finish(p, moved, n, ta, tri);
    }

    private static double[] Brow(double[] p, float[] n, string[] dom)
    {
        int count = p.Length / 3;
        var d = new double[p.Length];
        for (int v = 0; v < count; v++)
        {
            double x = p[v * 3], y = p[v * 3 + 1], z = p[v * 3 + 2];
            // Not the back of the head, not the lids themselves, not the eye socket below the lid crease.
            bool lid = dom[v].Contains("lid", StringComparison.Ordinal);
            if (z > -4.0 || lid || (dom[v].Contains("eye", StringComparison.Ordinal) && y < 48.3)) continue;

            double ax = Math.Abs(x);
            double crest = BrowY + BrowArch * Math.Sin(Math.Min(ax / 3.6, 1.0) * Math.PI) - 0.15 * Math.Exp(-Math.Pow(x / 0.9, 2));
            double dy = y - crest;
            double vert = dy > 0 ? Math.Exp(-Math.Pow(dy / BrowAbove, 2)) : Math.Exp(-Math.Pow(dy / BrowBelow, 2));
            double s = vert * (1.0 - SmoothStep(BrowHalf, BrowHalf + 2.2, ax));
            if (s < 1e-4) continue;

            // Forward along a blend of the skin normal and −z; the crest sags onto the lids.
            double fx = 0.35 * n[v * 3], fy = 0.35 * n[v * 3 + 1], fz = -0.65 + 0.35 * n[v * 3 + 2];
            double l = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            d[v * 3] += BrowOut * s * fx / l;
            d[v * 3 + 1] += BrowOut * s * fy / l - BrowDown * s * (dy < 0.6 ? 1 : 0.4);
            d[v * 3 + 2] += BrowOut * s * fz / l;
        }

        return d;
    }

    private static double[] Jaw(double[] p, string[] dom)
    {
        int count = p.Length / 3;
        var d = new double[p.Length];
        for (int v = 0; v < count; v++)
        {
            double x = p[v * 3], y = p[v * 3 + 1], z = p[v * 3 + 2];
            if (y > LipY + 0.2 || y < 30.0 || z > 4.0) continue;
            double ax = Math.Abs(x), side = x >= 0 ? 1 : -1;

            // The gonial angle, on the jawline under the ear.
            double g = Math.Exp(-Math.Pow((ax - 6.0) / 2.0, 2) - Math.Pow((y - 38.0) / 2.4, 2) - Math.Pow((z + 0.8) / 2.4, 2));
            d[v * 3] += side * JawOut * g;
            d[v * 3 + 1] -= JawDown * g;

            // The chin: the front of the mandible, fading out below the lower lip.
            double c = Math.Exp(-Math.Pow(x / 3.6, 4)) * SmoothStep(-4.0, -8.0, z)
                       * (1 - SmoothStep(LipY - 3.2, LipY - 1.0, y)) * SmoothStep(31.0, 34.0, y);
            d[v * 3 + 2] -= ChinFwd * c;
            d[v * 3 + 1] -= ChinDown * c * SmoothStep(LipY - 1.0, 33.0, y);
            d[v * 3] += side * ChinWide * c * SmoothStep(0.8, 2.8, ax);

            // The underbite: the lower lip and the skin under it ride forward with the mandible.
            if (dom[v].Contains("lowerLip", StringComparison.Ordinal) || dom[v].Contains("chin", StringComparison.Ordinal))
                d[v * 3 + 2] -= LipFwd * (1 - SmoothStep(LipY, LipY + 0.2, y));
        }

        return d;
    }

    /// <summary>Half-and-half Laplacian passes; seam duplicates share one value after each.</summary>
    private static double[] Smooth(double[] d, List<int>[] neighbours, List<List<int>> groups, int count)
    {
        for (int pass = 0; pass < SmoothPasses; pass++)
        {
            var e = (double[])d.Clone();
            for (int v = 0; v < count; v++)
            {
                var nb = neighbours[v];
                if (nb.Count == 0) continue;
                for (int c = 0; c < 3; c++)
                {
                    double sum = 0;
                    foreach (int u in nb) sum += d[u * 3 + c];
                    e[v * 3 + c] = 0.5 * d[v * 3 + c] + 0.5 * sum / nb.Count;
                }
            }

            foreach (var g in groups)
            {
                if (g.Count < 2) continue;
                for (int c = 0; c < 3; c++)
                {
                    double mean = g.Average(v => e[v * 3 + c]);
                    foreach (int v in g) e[v * 3 + c] = mean;
                }
            }

            d = e;
        }

        return d;
    }

    private static List<int>[] Neighbours(int[] tri, int count)
    {
        var sets = new HashSet<int>[count];
        for (int v = 0; v < count; v++) sets[v] = [];
        for (int t = 0; t + 2 < tri.Length; t += 3)
        {
            int a = tri[t], b = tri[t + 1], c = tri[t + 2];
            sets[a].Add(b); sets[a].Add(c);
            sets[b].Add(a); sets[b].Add(c);
            sets[c].Add(a); sets[c].Add(b);
        }

        return [.. sets.Select(s => s.Order().ToList())];
    }

    private static List<List<int>> WeldGroups(float[] p, int count)
    {
        var map = new Dictionary<(long, long, long), List<int>>();
        var order = new List<List<int>>();
        for (int v = 0; v < count; v++)
        {
            var key = ((long)Math.Round(p[v * 3] * 1e4), (long)Math.Round(p[v * 3 + 1] * 1e4), (long)Math.Round(p[v * 3 + 2] * 1e4));
            if (!map.TryGetValue(key, out var g)) { g = []; map[key] = g; order.Add(g); }
            g.Add(v);
        }

        return order;
    }

    private static (double Y, double Z) Landmark(float[] p, string[] dom, string tag)
    {
        double y = 0, z = 0;
        int k = 0;
        for (int v = 0; v < dom.Length; v++)
        {
            if (!(tag.StartsWith("bn_", StringComparison.Ordinal) ? dom[v] == tag : dom[v].Contains(tag, StringComparison.Ordinal))) continue;
            y += p[v * 3 + 1]; z += p[v * 3 + 2]; k++;
        }

        if (k == 0) throw new InvalidDataException($"the head has no skin on {tag} to place the giantkin face by");
        return (y / k, z / k);
    }

    private static double SmoothStep(double e0, double e1, double x)
    {
        double t = Math.Clamp((x - e0) / (e1 - e0), 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }
}
