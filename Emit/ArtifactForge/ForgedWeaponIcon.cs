namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using System.IO;

/// <summary>
/// Gives a forged weapon its own inventory icon by recolouring the stock one.
///
/// **Why a tint rather than a render.** Artifact icons are a 960x240 strip of four 238x240 frames
/// (<c>framesize = { 238 240 }</c> in vanilla's gui), and they are drawn at **30-60 pixels**. At
/// that size a player perceives colour, the rough silhouette class, and almost nothing else — blade
/// curvature, guard shape and surface grain are all below the resolution. Since the forge already
/// computes a colour per part, tinting the stock icon captures very nearly all of the uniqueness a
/// 50-pixel icon can carry, for a fraction of the cost of a software rasteriser.
///
/// The machinery is deliberately shaped so a real render could replace <see cref="Tint"/> later
/// without touching anything else: the caller asks for "an icon for this weapon" and gets back a
/// filename. If it ever is replaced, the thing worth drawing is the **hilt**, not the whole weapon —
/// a sword is roughly 95 units long by 18 wide, so a whole one in a 238x240 cell is a thin diagonal
/// that wastes the frame, while the hilt is nearly square and is where the identity lives.
/// </summary>
public static class ForgedWeaponIcon
{
    /// <summary>Both vanilla's icons and ours live here; the <c>icon</c> field is a bare filename.</summary>
    public const string IconDir = "gfx/interface/icons/artifact";

    /// <summary>
    /// Source luminance that maps to the tint at full strength.
    ///
    /// Not 255: metal artwork sits mostly in the midtones, so mapping pure white to the tint would
    /// render everything dark. Anchoring on a typical midtone keeps the shading intact and lets
    /// specular highlights clamp toward white, which is what makes the result still read as metal.
    /// </summary>
    private const int MidGrey = 178;

    /// <summary>
    /// Writes a tinted copy of <paramref name="sourceIcon"/> and returns its filename, or null if
    /// the source could not be read — in which case the caller keeps using the stock icon.
    /// </summary>
    public static string? Write(
        string modDir, string gameDir, string weaponName, string sourceIcon,
        (byte R, byte G, byte B) colour)
    {
        string srcPath = Path.Combine(gameDir,
            IconDir.Replace('/', Path.DirectorySeparatorChar), sourceIcon);

        if (DdsReader.Load(srcPath) is not { } img) return null;

        string fileName = $"{weaponName}_icon.dds";
        string outDir = Path.Combine(modDir, IconDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(outDir);

        // DXT5 where the source allows it, which vanilla's own 960x240 strips do. Falls back to
        // uncompressed for anything whose dimensions are not multiples of four, rather than throwing:
        // this path already exists to salvage an icon when the better one is unavailable, so it
        // should not be the thing that fails.
        string outPath = Path.Combine(outDir, fileName);
        byte[] tinted = Tint(img.Bgra, colour);

        if (img.Width % 4 == 0 && img.Height % 4 == 0)
            DdsWriter.WriteDxt5(outPath, img.Width, img.Height, tinted);
        else
            DdsWriter.WriteBgra(outPath, img.Width, img.Height, tinted);

        return fileName;
    }

    /// <summary>
    /// Recolours opaque pixels, preserving the source's shading and its alpha exactly.
    ///
    /// Alpha is what makes the icon a weapon-shaped cutout rather than a rectangle, so a
    /// fully transparent pixel is left untouched rather than tinted — tinting it would put colour
    /// into the transparent margin, which shows up as a halo wherever the icon is drawn over a
    /// lighter background.
    /// </summary>
    private static byte[] Tint(byte[] bgra, (byte R, byte G, byte B) colour)
    {
        var outBytes = new byte[bgra.Length];

        for (int i = 0; i < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            outBytes[i + 3] = a;
            if (a == 0) continue;

            // Rec. 709 luminance, on BGRA input.
            int lum = (54 * bgra[i + 2] + 183 * bgra[i + 1] + 19 * bgra[i]) >> 8;

            outBytes[i + 0] = Scale(colour.B, lum);
            outBytes[i + 1] = Scale(colour.G, lum);
            outBytes[i + 2] = Scale(colour.R, lum);
        }

        return outBytes;
    }

    private static byte Scale(byte channel, int luminance)
        => (byte)Math.Clamp(channel * luminance / MidGrey, 0, 255);
}
