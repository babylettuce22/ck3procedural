namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using System.IO;

/// <summary>The three textures a piece is drawn with, as the entity must name them.</summary>
public sealed record PieceTextureSet(string Diffuse, string Normal, string Properties);

/// <summary>
/// Converts a set's authoring textures into the three DDS files CK3 expects.
///
/// **Why a conversion rather than "export DDS from the modelling tool".** A harvested model arrives
/// with glTF/PBR maps — base colour, a packed metallic-roughness, and an OpenGL-style tangent normal
/// — and CK3 wants none of those layouts. Asking a modeller to repack channels by hand is asking for
/// the one mistake that is invisible until it is lit: a normal map with its axes in the wrong
/// channels looks *almost* right and shades inside-out on curvature.
///
/// So the convention is: drop the maps a model shipped with into
/// <c>assets/attachments/textures/&lt;set&gt;_{diffuse,normal,properties}.{png,jpg,jpeg,tga,bmp}</c>
/// and this repacks them.
///
/// **The two layouts that are not obvious.**
///
/// *Normals are DXT5nm.* <c>texture_decals_base.fxh</c> unpacks them as
/// <c>Normal.xy = NormalSample.ga * 2 - 1</c> — X in GREEN, Y in ALPHA — so a flat normal is
/// (128, 128, 255, 128), not the (128, 128, 255, 255) an ordinary tangent map would store. The
/// source's red goes to green and its green goes to alpha; blue is unused and left opaque.
///
/// *Properties is (AO, spec, metalness, roughness).* Per <c>lighting_util.fxh</c>,
/// <c>GetMaterialProperties</c> reads roughness from <c>.a</c>, spec from <c>.g</c> and metalness
/// from <c>.b</c>, and <c>portrait_accessory_variation.fxh</c> additionally multiplies the diffuse by
/// <c>.r</c>, making red ambient occlusion. glTF packs roughness in GREEN and metalness in BLUE of
/// one map, so the two channels are swapped into place and the other two supplied: AO from the
/// source's red if it carries one, else fully lit.
/// </summary>
public static class PieceTextures
{
    /// <summary>Extensions accepted for an authoring texture, in preference order.</summary>
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".tga", ".bmp", ".dds"];

    /// <summary>
    /// Spec written into the properties map, on the scale vanilla's own swatches use.
    ///
    /// glTF has no separate specular channel — it is implied by metalness — so there is nothing to
    /// convert and a constant is the honest answer. 128 sits with vanilla's authored swatches, which
    /// measure 122 to 128.
    /// </summary>
    private const byte Spec = 128;

    /// <summary>
    /// Builds the DDS set for one piece set, or null when it has no authoring textures.
    ///
    /// Null is the normal outcome for a piece whose mesh already names textures that ship with the
    /// game or the mod — the ISO pauldrons cut from the plate atlas, for instance. Only a set with
    /// its own maps needs converting.
    /// </summary>
    public static PieceTextureSet? Convert(string texturesDir, string outDir, string set)
    {
        string? diffuse = Find(texturesDir, $"{set}_diffuse");
        if (diffuse is null) return null;

        string? normal = Find(texturesDir, $"{set}_normal");
        string? properties = Find(texturesDir, $"{set}_properties");

        Directory.CreateDirectory(outDir);

        string outDiffuse = $"gen_piece_{set}_diffuse.dds";
        string outNormal = $"gen_piece_{set}_normal.dds";
        string outProps = $"gen_piece_{set}_properties.dds";

        if (!WriteDiffuse(diffuse, Path.Combine(outDir, outDiffuse))) return null;

        // A set with a diffuse but no normal or properties still renders; it just renders flat. Both
        // are written from a neutral default rather than omitted, because a meshsettings block that
        // names only some of its textures leaves the rest bound to whatever was there before.
        WriteNormal(normal, Path.Combine(outDir, outNormal), diffuse);
        WriteProperties(properties, Path.Combine(outDir, outProps), diffuse);

        return new PieceTextureSet(outDiffuse, outNormal, outProps);
    }

    private static string? Find(string dir, string stem) => Extensions
        .Select(ext => Path.Combine(dir, stem + ext))
        .FirstOrDefault(File.Exists);

    /// <summary>Dimensions rounded down to a multiple of four, which DXT5 requires.</summary>
    private static (int W, int H) Blocked(int w, int h) => (Math.Max(4, w - w % 4), Math.Max(4, h - h % 4));

    private static bool WriteDiffuse(string source, string target)
    {
        if (DdsReader.Load(source) is not { } img) return false;

        var (w, h) = Blocked(img.Width, img.Height);
        var bgra = Crop(img, w, h);

        // Opaque throughout: a portrait attachment with a stray alpha would punch holes in itself,
        // and a harvested base colour routinely carries a meaningless alpha channel.
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;

        DdsWriter.WriteDxt5(target, w, h, bgra);
        return true;
    }

    private static void WriteNormal(string? source, string target, string sizeLike)
    {
        var img = source is null ? null : DdsReader.Load(source);
        var like = img ?? DdsReader.Load(sizeLike);
        if (like is not { } reference) return;

        var (w, h) = Blocked(reference.Width, reference.Height);
        var bgra = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;

                // Flat when there is nothing to convert: (128, 128, 255, 128) in DXT5nm.
                byte nx = 128, ny = 128;

                if (img is { } src)
                {
                    int s = (y * src.Width + x) * 4;
                    nx = src.Bgra[s + 2];   // source red  -> X
                    ny = src.Bgra[s + 1];   // source green-> Y
                }

                bgra[i + 0] = 255;   // blue: unused by the unpack, kept opaque
                bgra[i + 1] = nx;    // green carries X
                bgra[i + 2] = 128;   // red: unused
                bgra[i + 3] = ny;    // alpha carries Y
            }
        }

        DdsWriter.WriteDxt5(target, w, h, bgra);
    }

    private static void WriteProperties(string? source, string target, string sizeLike)
    {
        var img = source is null ? null : DdsReader.Load(source);
        var like = img ?? DdsReader.Load(sizeLike);
        if (like is not { } reference) return;

        var (w, h) = Blocked(reference.Width, reference.Height);
        var bgra = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;

                // Fully lit, mid spec, non-metal, mid rough - a plausible surface when nothing is
                // supplied, rather than a black one.
                byte ao = 255, metal = 0, rough = 128;

                if (img is { } src)
                {
                    int s = (y * src.Width + x) * 4;
                    ao = src.Bgra[s + 2];      // glTF leaves red for occlusion; flat 1.0 if unused
                    rough = src.Bgra[s + 1];   // glTF green  -> roughness
                    metal = src.Bgra[s + 0];   // glTF blue   -> metalness
                }

                bgra[i + 0] = metal;   // B: metalness
                bgra[i + 1] = Spec;    // G: spec
                bgra[i + 2] = ao;      // R: ambient occlusion
                bgra[i + 3] = rough;   // A: roughness
            }
        }

        DdsWriter.WriteDxt5(target, w, h, bgra);
    }

    /// <summary>Copies the top-left w x h of a decoded image, so DXT5 gets whole blocks.</summary>
    private static byte[] Crop(DdsReader.DecodedImage img, int w, int h)
    {
        var bgra = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
            Array.Copy(img.Bgra, (y * img.Width) * 4, bgra, (y * w) * 4, w * 4);

        return bgra;
    }
}
