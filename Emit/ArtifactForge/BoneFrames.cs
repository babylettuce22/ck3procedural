namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using System.IO;

/// <summary>
/// A bone's rest frame in body space: where it sits, and which way its local axes point.
/// </summary>
/// <param name="Axis">
/// The bone's local axes as columns — <c>Axis[r, c]</c> is component <c>r</c> of local axis
/// <c>c</c>. Orthonormal, so its inverse is its transpose.
/// </param>
/// <param name="Origin">The bone's position in body space.</param>
public sealed record BoneFrame(double[,] Axis, double[] Origin)
{
    /// <summary>Body space to bone-local: <c>local = Axis^T * (p - Origin)</c>.</summary>
    public (double X, double Y, double Z) ToLocal(double x, double y, double z)
    {
        double dx = x - Origin[0], dy = y - Origin[1], dz = z - Origin[2];

        return (Axis[0, 0] * dx + Axis[1, 0] * dy + Axis[2, 0] * dz,
                Axis[0, 1] * dx + Axis[1, 1] * dy + Axis[2, 1] * dz,
                Axis[0, 2] * dx + Axis[1, 2] * dy + Axis[2, 2] * dz);
    }

    /// <summary>
    /// The same rotation with no translation, for directions.
    ///
    /// Normals and tangents are directions, not points: translating them would tip every one of them
    /// toward the bone's origin and light the piece as though it were inside-out.
    /// </summary>
    public (double X, double Y, double Z) RotateToLocal(double x, double y, double z)
    {
        return (Axis[0, 0] * x + Axis[1, 0] * y + Axis[2, 0] * z,
                Axis[0, 1] * x + Axis[1, 1] * y + Axis[2, 1] * z,
                Axis[0, 2] * x + Axis[1, 2] * y + Axis[2, 2] * z);
    }
}

/// <summary>
/// Reads the portrait skeleton's rest pose out of a vanilla mesh.
///
/// **The convention is the whole difficulty, and it is not the obvious one.** A skeleton bone node
/// carries <c>tx</c>, twelve floats holding the INVERSE bind pose — world to bone — with the 3x3
/// stored **column-major**. So with <c>A = reshape(tx[0..9])</c> read row by row:
///
/// * the bone's local axes in body space are the COLUMNS of <c>A</c>, and
/// * the bone's origin in body space is <c>-A * tx[9..12]</c>.
///
/// Reading it row-major instead is the trap: it gives correct answers for any bone whose rotation is
/// identity — every <c>bn_*_prop</c>, which is exactly what one tests first — and puts the left-side
/// bones at the character's feet. The check that catches it is symmetry: <c>bn_l_shoulder</c> and
/// <c>bn_r_shoulder</c> must come out at mirrored x with identical y and z, and they do only under
/// the column-major reading (±17.94, 135.68, −2.07 on the male rig).
///
/// **Not from Blender.** io_pdx_mesh builds ~0.5-unit stub bones and Blender forces +Y along a bone,
/// so <c>bone.matrix_local</c> reports an orientation the game never had. Positions survive; frames
/// do not.
/// </summary>
public static class BoneFrames
{
    /// <summary>Cached per game directory — the search walks a few thousand files.</summary>
    private static readonly Dictionary<string, Dictionary<string, BoneFrame>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A bone every portrait rig has, used to recognise a mesh that carries the skeleton.</summary>
    private const string Probe = "bn_r_shoulder";

    /// <summary>
    /// Every bone of the portrait skeleton, by bare name.
    ///
    /// Found by searching rather than by naming a file, because any garment skinned to the body
    /// carries the whole 134-bone rest pose and hard-coding one path would break the moment that
    /// garment moved between DLC folders. Returns empty rather than throwing: pauldrons are an
    /// enhancement, and a world that generates without them beats one that refuses to generate.
    /// </summary>
    public static Dictionary<string, BoneFrame> Read(string gameDir)
    {
        if (Cache.TryGetValue(gameDir, out var hit)) return hit;

        var found = new Dictionary<string, BoneFrame>(StringComparer.Ordinal);
        string root = Path.Combine(gameDir, "gfx", "models", "portraits");

        if (!Directory.Exists(root))
        {
            Cache[gameDir] = found;
            return found;
        }

        // Male clothes first: they are skinned to the full body rig, and the male frame is the one
        // pieces are authored against. Any of them will do, so the first that parses wins.
        foreach (string path in Directory
            .EnumerateFiles(root, "*.mesh", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p).StartsWith("m_clothes", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                Collect(PdxMesh.Read(path), found);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException)
            {
                found.Clear();
                continue;
            }

            if (found.ContainsKey(Probe)) break;

            found.Clear();
        }

        Cache[gameDir] = found;
        return found;
    }

    private static void Collect(PdxNode node, Dictionary<string, BoneFrame> into)
    {
        if (node.Prop("tx") is { Floats.Length: 12 } tx)
        {
            float[] t = tx.Floats;

            // Column-major: A[r, c] = t[c * 3 + r] would be the row-major reading. The stored order
            // is rows of the INVERSE, so the matrix whose COLUMNS are the local axes is the plain
            // row-by-row fill, and the origin is -A * translation.
            var a = new double[3, 3];

            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    a[r, c] = t[r * 3 + c];

            var origin = new double[3];

            for (int r = 0; r < 3; r++)
                origin[r] = -(a[r, 0] * t[9] + a[r, 1] * t[10] + a[r, 2] * t[11]);

            string name = node.Name;
            int colon = name.LastIndexOf(':');
            if (colon >= 0) name = name[(colon + 1)..];

            into[name] = new BoneFrame(a, origin);
        }

        foreach (var kid in node.Children) Collect(kid, into);
    }
}
