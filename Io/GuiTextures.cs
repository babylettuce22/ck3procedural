namespace Ck3MapGen.Io;

/// <summary>
/// Turns a texture path out of a <c>.gui</c> file into a <c>data:</c> URI a browser can draw.
///
/// The host half of the GUI preview, and it lives here rather than beside the preview on purpose:
/// decoding DDS costs a dependency on <see cref="DdsReader"/> and, through it, System.Drawing, and
/// the preview is meant to stay portable enough to lift into a standalone editor. The preview asks
/// for a picture through a delegate and does not care where one comes from — on a host that cannot
/// decode DDS it gets null back and draws a labelled placeholder instead.
///
/// CK3's interface art is loose on disk rather than packed, which is what makes this worth doing at
/// all: 8,396 <c>.dds</c> files under <c>gfx/interface</c>, of which a sampled 99.5% are the three
/// formats <see cref="DdsReader"/> already handles (uncompressed 32bpp, DXT5, DXT1). The rest —
/// DXT3 and a couple of BC7 — come back null and show as a placeholder, which is a fair trade for
/// not writing two more block decoders.
/// </summary>
public sealed class GuiTextures(params string[] roots)
{
    /// <summary>
    /// Where textures are looked for, in order. Pass the mod folder first so a texture this project
    /// ships wins over the vanilla one of the same name, which is what the game does.
    /// </summary>
    private readonly string[] _roots = roots;

    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Textures asked for that were not found or could not be decoded.</summary>
    public SortedSet<string> Missing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Loaded { get; private set; }

    /// <summary>
    /// A <c>data:image/png;base64,…</c> URI for one texture path, or null.
    ///
    /// Cached including the misses, because a window that references a missing texture usually
    /// references it from every row of a list.
    /// </summary>
    public string? DataUri(string texturePath)
    {
        if (_cache.TryGetValue(texturePath, out string? cached)) return cached;

        string? uri = Load(texturePath);
        _cache[texturePath] = uri;

        if (uri is null) Missing.Add(texturePath);
        else Loaded++;

        return uri;
    }

    private string? Load(string texturePath)
    {
        // A texture reference can be a datafunction rather than a path — the council seats resolve
        // theirs through `[Illustration.GetTexture(…)]`. Nothing static can follow that.
        if (texturePath.Contains('[')) return null;

        string relative = texturePath.Replace('/', Path.DirectorySeparatorChar).TrimStart('\\', '/');

        foreach (string root in _roots)
        {
            string full = Path.Combine(root, relative);

            // The files are inconsistent about which extension they name; the engine resolves
            // either, and a .dds reference to a shipped .png is common in mod content.
            foreach (string candidate in Candidates(full))
            {
                if (!File.Exists(candidate)) continue;

                var decoded = DdsReader.Load(candidate);
                if (decoded is not { } image) continue;

                return "data:image/png;base64," + Convert.ToBase64String(
                    PngWriter.EncodeRgba8(image.Width, image.Height, ToRgba(image.Bgra)));
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string full)
    {
        yield return full;

        string swapped = Path.ChangeExtension(full, full.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
            ? ".png"
            : ".dds");

        yield return swapped;
    }

    /// <summary>BGRA as <see cref="DdsReader"/> hands it over, to the RGBA a PNG wants.</summary>
    private static byte[] ToRgba(byte[] bgra)
    {
        var rgba = new byte[bgra.Length];

        for (int i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        return rgba;
    }
}
