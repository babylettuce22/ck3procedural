namespace Ck3MapGen.MapGen;

/// <summary>
/// The orc brow: a low, heavy bar of bone sagging onto the lids — the giantkin brow field
/// (<see cref="GiantFace.BrowShape.Orc"/>) without the jaw, as its own head blendshape.
///
/// It replaced vanilla's <c>gene_bs_forehead_brow_forward</c> in the orc row. Forced to 0.8–1.0, that
/// slider gathers the inner brows into a frown with creases between the eyes rather than building
/// bone (rendered 2026-09-29 beside this, in <c>Desktop/ck3devtools/giant_probe/brows.py</c>). Hornkin
/// were rendered too and left as they were: at a light strength the brow barely showed.
/// </summary>
internal static class OrcBrow
{
    /// <summary>The gene (BaseFilesToCopy/Core/common/genes/gen_bs_orc_brow.txt) and its templates.</summary>
    public const string Gene = "gen_bs_orc_brow";
    public const string NoneTemplate = "gen_orc_brow_none";
    public const string OnTemplate = "gen_orc_brow_on";
    public const string Attribute = "gen_bs_orc_brow";

    public static string BlendShapeId(string sex) => $"{sex}_bs_gen_orc_brow";

    public static PointedEars.Result Shape(float[] p, float[] n, float[] ta, int[] tri, string[] dominant, bool female)
        => GiantFace.BrowOnly(p, n, ta, tri, dominant, female, GiantFace.BrowShape.Orc);
}
