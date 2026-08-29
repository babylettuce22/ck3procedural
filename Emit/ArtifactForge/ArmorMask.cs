namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using System.IO;

/// <summary>One generated mask, and what it put where.</summary>
/// <param name="ModPath">Mod-relative path, as the entity's <c>pattern_mask</c> must name it.</param>
/// <param name="DiskPath">Where it was written, so the coverage and density passes can read it back.</param>
/// <param name="MetalChannel">Which channel now marks the garment's metal. Always the last one.</param>
/// <param name="MetalArea">Fraction of the garment that is metal, for the diagnostic line.</param>
public sealed record GeneratedMask(string ModPath, string DiskPath, int MetalChannel, double MetalArea);

/// <summary>
/// Writes a replacement <c>pattern_mask</c> that marks a war garment's METAL, which vanilla's own
/// mask does not.
///
/// **Why this exists.** Measured across the war garments a world picks, every visible channel of
/// vanilla's mask has metalness ~0.00, while the garments carry 5.4%-28% genuinely metal texels of
/// which **94-98% lie outside the mask entirely**. So vanilla's mask marks the soft parts — surcoat,
/// lining, straps — and the armour plates are exactly the region a recolour can never reach. Any
/// metal substance we apply therefore lands on cloth-shaped geometry, which is why a plate artifact
/// on a cloth war garment reads as wrong however good the material is. It is not fixable by choosing
/// a different material, only by changing where the material lands.
///
/// **The garment's own properties map already knows.** <c>Properties.b</c> is metalness, per
/// <c>lighting_util.fxh</c>, and ships beside every garment as <c>*_properties.dds</c> in the same
/// UV0 layout as the mask. So the metal region can be lifted straight out of vanilla's own art with
/// no authoring at all.
///
/// **Metal goes in the LAST channel on purpose.** <c>ApplyVariationPatterns</c> walks the channels
/// in order and each one lerps over the previous, so the highest channel wins wherever it is
/// present. Putting metal there means it paints over any soft region that overlaps it rather than
/// being painted over.
///
/// Reversible: see <see cref="ArtifactForgeFlags.GeneratedArmorMasks"/>. With it off nothing here
/// runs, the entities name vanilla's mask, and the forge behaves exactly as it did before.
/// </summary>
public static class ArmorMask
{
    /// <summary>Where generated masks are written, inside the mod.</summary>
    public const string Dir = "gfx/models/artifacts/gen_armor/masks";

    /// <summary>
    /// Output resolution, and why it is smaller than the 1024x1024 source.
    ///
    /// A mask carries REGIONS, not detail — the finest thing it has to hold is the boundary between
    /// a cuirass and the cloth beside it, which is many texels wide at any sane resolution. 512
    /// halves the linear resolution and quarters the file. Must stay a multiple of four:
    /// <see cref="DdsWriter.WriteDxt5"/> refuses anything else rather than writing a torn last row.
    /// </summary>
    private const int Size = 512;

    /// <summary>
    /// Metalness above which a texel counts as metal.
    ///
    /// Mid-range on purpose. The garments measured are strongly bimodal — cloth regions sit at ~0.00
    /// and plate at ~1.00 — so anything from 0.3 to 0.7 selects the same pixels, and a threshold in
    /// the middle is the one least sensitive to a garment that turns out not to be.
    /// </summary>
    private const byte MetalCutoff = 128;

    /// <summary>
    /// Below this share of metal there is nothing worth redirecting, so vanilla's mask is kept.
    ///
    /// A garment that is 1% metal would hand its whole armour type a region too small to read, while
    /// losing one of vanilla's four soft regions to hold it — strictly worse than leaving it alone.
    /// </summary>
    private const double MinMetalArea = 0.02;

    /// <summary>
    /// Builds the mask for one garment, or null when it should keep vanilla's.
    ///
    /// Null is a normal outcome, not a failure: a garment with no properties map, a properties map
    /// that does not match the mask's layout, or too little metal to bother with all take vanilla's
    /// mask and the behaviour that goes with it.
    /// </summary>
    public static GeneratedMask? Build(string modDir, string gameDir, ArmorGarment garment)
    {
        if (garment.PatternMask is not { } rel) return null;

        string maskDisk = Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar));
        string propDisk = maskDisk.Replace("_masks.dds", "_properties.dds", StringComparison.Ordinal);

        // The mesh's own material names a properties texture that vanilla does not ship - the
        // material says `_Roughness.dds` and the folder holds `_properties.dds` - so the name is
        // derived from the mask's rather than read from the mesh.
        if (maskDisk == propDisk || !File.Exists(propDisk)) return null;

        if (DdsReader.Load(maskDisk) is not { } mask) return null;
        if (DdsReader.Load(propDisk) is not { } props) return null;

        // Both are authored against UV0 at the same layout, so a size mismatch means one of them is
        // not what we think it is and the metal region cannot be trusted to line up.
        if (mask.Width != props.Width || mask.Height != props.Height) return null;

        // Which of vanilla's channels are worth keeping, measured over the CLOTH only: a channel
        // that exists solely on the plates has nothing left once metal is taken out of it.
        var visible = new double[4];
        long cloth = 0, metalHits = 0;

        for (int p = 0; p + 3 < mask.Bgra.Length; p += 4)
        {
            bool isMetal = props.Bgra[p + 0] >= MetalCutoff;   // BGRA: metalness is Properties.b
            if (isMetal) { metalHits++; continue; }

            cloth++;
            AddVisible(mask.Bgra, p, visible);
        }

        long texels = mask.Bgra.Length / 4;
        double metalArea = (double)metalHits / texels;

        if (metalArea < MinMetalArea) return null;

        // Vanilla's four channels ranked by what survives on the cloth, best first. The top three
        // become our r, g and b; the fourth is folded into the strongest rather than dropped, so no
        // region of the garment silently stops being paintable.
        int[] order = [.. Enumerable.Range(0, 4).OrderByDescending(c => visible[c]).ThenBy(c => c)];

        string dir = Path.Combine(modDir, Dir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);

        string name = $"{garment.Accessory}_gen_masks.dds";
        string disk = Path.Combine(dir, name);
        var outBgra = new byte[Size * Size * 4];

        for (int y = 0; y < Size; y++)
        {
            int sy = y * mask.Height / Size;

            for (int x = 0; x < Size; x++)
            {
                int sx = x * mask.Width / Size;
                int src = (sy * mask.Width + sx) * 4;
                int dst = (y * Size + x) * 4;

                bool isMetal = props.Bgra[src + 0] >= MetalCutoff;

                // Channel 3 (alpha) is the metal. DXT5 stores alpha on its own interpolated ramp
                // rather than in the colour block, so the one channel whose edges matter most is
                // also the one the format preserves best.
                outBgra[dst + 3] = isMetal ? (byte)255 : (byte)0;

                if (isMetal)
                {
                    // Cleared, not merely overpainted. A soft channel left standing under the metal
                    // would still be lerped in wherever the metal's own value dips, which shows as
                    // cloth bleeding through the plates.
                    outBgra[dst + 0] = outBgra[dst + 1] = outBgra[dst + 2] = 0;
                    continue;
                }

                // THE MOST PROMINENT CLOTH REGION GOES IN THE HIGHEST CLOTH CHANNEL, because the
                // lerp chain means a higher channel paints over a lower one. Writing the ranking the
                // other way up - which this did first - puts vanilla's base coat on top of the two
                // regions it is supposed to sit under, and they measure to zero visible area.
                //
                // So: order[0] (most visible) -> b, order[1] -> g, and the two least fold into r.
                byte third = mask.Bgra[src + MaskByte(order[2])];
                byte fourth = mask.Bgra[src + MaskByte(order[3])];

                // BGRA out: index 2 is our r, 1 is g, 0 is b.
                outBgra[dst + 2] = Math.Max(third, fourth);
                outBgra[dst + 1] = mask.Bgra[src + MaskByte(order[1])];
                outBgra[dst + 0] = mask.Bgra[src + MaskByte(order[0])];
            }
        }

        DdsWriter.WriteDxt5(disk, Size, Size, outBgra);

        return new GeneratedMask($"{Dir}/{name}", disk, 3, metalArea);
    }

    /// <summary>
    /// DdsReader hands back BGRA; the shader's <c>Mask[0..3]</c> is RGBA, so channel <c>i</c> lives
    /// at these byte offsets.
    /// </summary>
    private static int MaskByte(int channel) => channel switch { 0 => 2, 1 => 1, 2 => 0, _ => 3 };

    /// <summary>
    /// Adds one texel's VISIBLE share to a running total per channel.
    ///
    /// Visible rather than raw, because the shader lerps the channels in order and a higher one
    /// paints over a lower one — so a channel's share is <c>Mask[i] * PROD(1 - Mask[j])</c> for
    /// <c>j &gt; i</c>. Ranking on the raw value picks a channel that is mostly covered up.
    /// </summary>
    private static void AddVisible(byte[] bgra, int at, double[] into)
    {
        var m = new double[4];
        for (int c = 0; c < 4; c++) m[c] = bgra[at + MaskByte(c)] / 255.0;

        for (int c = 0; c < 4; c++)
        {
            double survives = m[c];
            for (int higher = c + 1; higher < 4; higher++) survives *= 1.0 - m[higher];
            into[c] += survives;
        }
    }
}
