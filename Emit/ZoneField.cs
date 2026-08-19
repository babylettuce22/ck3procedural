using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// A selector for which *regional* variant of a biome a pixel belongs to — the axis that makes an
/// eastern steppe look different from a western one.
///
/// Every other selector in the texture writer is far too fine for this. The coarsest, <c>nA</c>,
/// cycles about every 102 province pixels, which is a patch a county could sit inside; vanilla's
/// steppe changes character over distances more like a kingdom. Measured across vanilla's 378
/// steppe provinces the biome falls into four well-separated looks of 120 / 87 / 99 / 72 provinces
/// — bare eastern grass, dense bush, open lowland and a mixed scrub — and each is a contiguous
/// region, not a patch that repeats every hundred pixels.
///
/// So this runs an order of magnitude coarser than <c>nA</c> and is consulted only for that
/// choice. Two octaves rather than three: the point is the broad shape of a region, and fine
/// detail on top of it would only reintroduce the patchiness this exists to remove.
/// </summary>
public static class ZoneField
{
    /// <summary>
    /// Cycles across a reference-width map. 9 puts a region at roughly 1000 province pixels
    /// across — about ten times <c>nA</c>'s patch, and the scale a steppe actually changes over.
    /// </summary>
    private const double Cycles = 9.0;

    /// <inheritdoc cref="CanopyField.Create"/>
    public static SimplexNoise Create(MapConfig cfg)
        => new(new Rng((ulong)(uint)cfg.Seed * 0xD6E8FEB86659FD93UL + 0x14057B7EF767814FUL));

    /// <summary>
    /// The first regional axis, 0..1. For steppe this is bushiness: bare in the east, dense scrub
    /// north of the Caucasus.
    /// </summary>
    public static double Primary(SimplexNoise field, double x, double y) => Sample(field, x, y, 0, 0);

    /// <summary>
    /// The second regional axis, 0..1, decorrelated from <see cref="Primary"/> by a large offset in
    /// noise space rather than by a second field. For steppe this is which lowland leads.
    /// </summary>
    public static double Secondary(SimplexNoise field, double x, double y)
        => Sample(field, x, y, 137.9, -211.3);

    private static double Sample(SimplexNoise field, double x, double y, double ox, double oy)
    {
        const double f = Cycles / MapConfig.ReferenceProvinceWidth;
        return Math.Clamp(Field.Fbm(field, x * f + ox, y * f + oy, 2) * 0.5 + 0.5, 0, 1);
    }
}
