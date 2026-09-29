using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The one field that decides where woodland is thick, read by both the ground texture and the
/// tree scatter so that the two agree about where the trees are.
///
/// They did not used to. <see cref="TreeWriter"/> placed every instance on a bare per-pixel
/// Bernoulli draw with no spatial structure at all, while the <c>forest_*</c> materials that
/// represent that same canopy were weighted off the texture writer's own <c>nA</c> selector. Both
/// were calibrated to roughly the right totals — taiga runs about 62 tree instances per thousand
/// province pixels either way — but they were statistically independent, so the agreement was only
/// ever an average. On the ground it produced patches of tree-spot texture with no trees standing
/// on them and trees standing on bare ground, which reads as far too much forest texture even when
/// the totals are right. Raising or lowering the texture share cannot fix that; only correlating
/// the two can.
///
/// The <c>forest_*</c> materials are not ordinary ground. Each is authored as the litter and shade
/// under a stand of trees — vanilla's texture is a picture of tree-spots — so painting one is a
/// claim that trees are there, and the claim has to be true.
///
/// Sampled in <b>province space, top-down image order</b>, which is the one coordinate system both
/// writers can name the same pixel in: the texture writer works in heightmap space off a
/// bottom-up row index, and the tree writer in province space off a top-down one.
/// </summary>
public static class CanopyField
{
    /// <summary>
    /// Cycles across a reference-width map. 72 puts a woodland patch at about 128 province pixels
    /// across, a little coarser than the 102 of the texture writer's own <c>nA</c>, so a stand of
    /// trees is comfortably larger than the material rotation happening inside it.
    /// </summary>
    private const double Cycles = 72.0;

    /// <summary>
    /// Derived from the map seed rather than from a live <see cref="Rng"/>, because the two callers
    /// draw different amounts from theirs before reaching this point and would otherwise get two
    /// different fields. The constant is arbitrary and only serves to decorrelate this field from
    /// anything else seeded from the same number.
    /// </summary>
    public static SimplexNoise Create(MapConfig cfg)
        => new(new Rng((ulong)(uint)cfg.Seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL));

    /// <summary>How much canopy stands at a province pixel, 0 (clearing) to 1 (thick).</summary>
    public static double At(SimplexNoise field, double x, double y)
    {
        const double f = Cycles / MapConfig.ReferenceProvinceWidth;
        return Math.Clamp(Field.Fbm(field, x * f, y * f, 3) * 0.5 + 0.5, 0, 1);
    }

    /// <summary>
    /// The tree scatter's density multiplier for that canopy value, centred on 1 so switching this
    /// on redistributes instances rather than changing how many there are.
    /// </summary>
    public static double ScatterFactor(double canopy) => 0.25 + 1.5 * canopy;

    /// <summary>
    /// The same field read much harder, for trees standing on open ground.
    ///
    /// A steppe does not carry a thin even wash of trees; it carries bare grass with occasional
    /// copses in the folds and along the water, which is what the sparse-but-diffuse scatter could
    /// never produce — at 2 instances per 1000 pixels an even draw puts one lone tree every twenty
    /// pixels forever, and never a stand of them. Cubing the canopy value concentrates the same
    /// budget into the top of the field: most of the steppe ends up with nothing at all, and the
    /// groves that do appear are dense enough to read as groves.
    ///
    /// The coefficient is set so the mean lands near 1 and this stays a redistribution rather than
    /// a cull — the field is bell-shaped about 0.5, so its cube averages roughly 0.16.
    /// </summary>
    public static double GroveFactor(double canopy) => 0.04 + 6.0 * canopy * canopy * canopy;
}
