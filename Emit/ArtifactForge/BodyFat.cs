namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using System.IO;

/// <summary>
/// The portrait body's fat blend shape, as a displacement field a rigid piece can be moved along.
///
/// **Why a rigid piece needs this at all.** A bone-attached piece follows its bone and nothing else,
/// but a heavier character's body — and every garment on it — swells through the fat blend shape. A
/// piece fitted flush to an average body is then swallowed: measured across all 32 male vanilla war
/// garments' <c>_bs_fat</c> shapes, the sunburst chest disc was 99.5% buried at full fat, and still
/// 65% after the stand-off that clears every average-weight garment. The cure is a second, heavier
/// copy of the piece that the portrait modifiers pick by weight (<see cref="BonePieceStep"/>).
///
/// **Why the BODY's shape stands in for the garment's.** It ships once, in the game, and covers any
/// garment a future piece might meet, where the garments' own fat shapes are one per garment and per
/// DLC. Measured, it is a good proxy: at the chest, upper chest, side of the neck and top of the
/// shoulder, a garment's fat bulge is 0.89-1.01x the body's there (median over the 32 garments),
/// 0.98-1.22x at the 75th percentile — so <see cref="GarmentScale"/> takes the high end, since a
/// piece floating a fraction too far reads far better than one sunk into the cloth.
///
/// The base body and its blend shape carry the same 3206 vertices in the same order, so the field is
/// simply their per-vertex difference, sampled at any point by inverse-distance weighting of the
/// nearest body vertices. Both are in the .mesh file's own space — the space every piece is authored
/// and baked in — so nothing is converted.
/// </summary>
public sealed class BodyFat
{
    /// <summary>A garment's fat bulge over the body's: the 75th-percentile figure above.</summary>
    public const double GarmentScale = 1.15;

    /// <summary>
    /// A garment's fat bulge over the body's, by height (file-space y, from 92.5 in steps of 5): the
    /// MEDIAN over the vanilla war garments that ship a <c>_bs_fat</c> shape (34 male, 33 female), along
    /// the body's own bulge, torso only (|x| &lt; 16), 3-tap smoothed, capped at <see cref="GarmentScale"/>.
    ///
    /// **Why a profile, for a piece that wraps the torso.** <see cref="GarmentScale"/> was measured at the
    /// chest, upper chest, neck and shoulder only, and holds there. Below the chest it does not: the male
    /// body's belly swells 8-10 units at full fat, while the garments follow it only 0.72-0.85x (they hang
    /// from the shoulders and belt rather than wrapping the belly), so a breastplate swollen at 1.15x stood
    /// ~4 units clear of a heavy character's waist in game (2026-10-03, "pulled in around the waist"). The
    /// median rather than the 75th percentile: a plate that covers the whole front shows any overshoot.
    /// Measured by <c>ck3devtools/armor_pieces/fat_profile.py</c>.
    /// </summary>
    private static readonly double[] MaleProfile =
        [0.74, 0.75, 0.81, 0.87, 0.94, 0.98, 0.94, 0.90, 0.90, 0.97, 1.06, 1.10];

    /// <inheritdoc cref="MaleProfile"/>
    private static readonly double[] FemaleProfile =
        [0.97, 1.02, 1.03, 1.03, 1.02, 0.99, 0.97, 1.02, 1.09, 1.14, 1.15, 1.15];

    private const double ProfileFirst = 92.5, ProfileStep = 5.0;

    private readonly bool _female;

    /// <summary>The garment's share of the body's fat bulge at height <paramref name="y"/> (see <see cref="MaleProfile"/>).</summary>
    public double GarmentScaleAt(double y)
    {
        double[] t = _female ? FemaleProfile : MaleProfile;
        double f = Math.Clamp((y - ProfileFirst) / ProfileStep, 0, t.Length - 1);
        int i = Math.Min((int)f, t.Length - 2);
        return t[i] + (t[i + 1] - t[i]) * (f - i);
    }

    /// <summary>
    /// How many body vertices a sample blends. Enough to smooth over the body's own triangle density,
    /// so a piece deforms as one surface rather than picking up the facets of whatever lies under it.
    /// </summary>
    private const int Neighbours = 16;

    private static readonly Dictionary<string, BodyFat?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly float[] _base;
    private readonly float[] _delta;

    private BodyFat(float[] basePositions, float[] delta, bool female)
    {
        _base = basePositions;
        _delta = delta;
        _female = female;
    }

    /// <summary>
    /// A body and its fat shape from the installed game, or null when either is missing or they
    /// disagree on vertex count — heavier variants are an enhancement, and a world without them beats
    /// one that fails to generate.
    ///
    /// Per sex, like the bone frames (<see cref="BoneFrames"/>): a female piece is authored on the female
    /// body, so it swells along the female body's own fat shape.
    /// </summary>
    public static BodyFat? Read(string gameDir, bool female = false)
    {
        string key = (female ? "f|" : "m|") + gameDir;
        if (Cache.TryGetValue(key, out var hit)) return hit;

        string sex = female ? "female" : "male";
        string dir = Path.Combine(gameDir, "gfx", "models", "portraits", $"{sex}_body");
        BodyFat? made = null;

        try
        {
            float[] body = Largest(PdxMesh.Read(Path.Combine(dir, $"{sex}_body.mesh")));
            float[] fat = Largest(PdxMesh.Read(Path.Combine(dir, "blendshapes", $"{sex}_bs_body_fat_1.mesh")));

            if (body.Length > 0 && body.Length == fat.Length)
            {
                var delta = new float[body.Length];
                for (int i = 0; i < body.Length; i++) delta[i] = fat[i] - body[i];
                made = new BodyFat(body, delta, female);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException)
        {
            made = null;
        }

        Cache[key] = made;
        return made;
    }

    /// <summary>The positions of the mesh node carrying the most vertices — the body, not a stray LOD.</summary>
    private static float[] Largest(PdxNode node)
    {
        float[] best = node.Floats("p");

        foreach (var kid in node.Children)
        {
            float[] p = Largest(kid);
            if (p.Length > best.Length) best = p;
        }

        return best;
    }

    /// <summary>
    /// How far the body's fat shape moves the surface nearest a point, at full fat.
    ///
    /// Brute force over the body's vertices: 3206 of them, against a few thousand piece vertices per
    /// variant, is well under a second for a whole world and needs no spatial index to get right.
    /// </summary>
    public (double X, double Y, double Z) Delta(double x, double y, double z)
    {
        Span<int> near = stackalloc int[Neighbours];
        Span<double> dist = stackalloc double[Neighbours];
        dist.Fill(double.MaxValue);
        near.Fill(-1);

        for (int v = 0; v * 3 + 2 < _base.Length; v++)
        {
            double dx = _base[v * 3] - x, dy = _base[v * 3 + 1] - y, dz = _base[v * 3 + 2] - z;
            double d2 = dx * dx + dy * dy + dz * dz;
            if (d2 >= dist[Neighbours - 1]) continue;

            // Insertion into the sorted short list.
            int at = Neighbours - 1;
            while (at > 0 && dist[at - 1] > d2)
            {
                dist[at] = dist[at - 1];
                near[at] = near[at - 1];
                at--;
            }

            dist[at] = d2;
            near[at] = v;
        }

        double sx = 0, sy = 0, sz = 0, sw = 0;

        for (int k = 0; k < Neighbours; k++)
        {
            int v = near[k];
            if (v < 0) continue;

            // +1 keeps a piece vertex that sits exactly on a body vertex from taking it alone.
            double w = 1.0 / (dist[k] + 1.0);
            sx += _delta[v * 3] * w;
            sy += _delta[v * 3 + 1] * w;
            sz += _delta[v * 3 + 2] * w;
            sw += w;
        }

        return sw > 0 ? (sx / sw, sy / sw, sz / sw) : (0, 0, 0);
    }
}
