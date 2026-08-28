namespace Ck3MapGen.Io;

/// <summary>
/// Minimal DDS writer: uncompressed 32-bit BGRA, single mip level.
///
/// Vanilla's colormap is DXT5 with 14 mips and its flatmap is DXT1, but block compression is a
/// lot of machinery for no benefit here — CK3 loads uncompressed DDS perfectly well, which
/// ck2rpg's shipped template relies on (its colormap.dds is exactly width*height*4 + 128 bytes,
/// i.e. uncompressed BGRA with no mips). The cost is file size, not correctness.
/// </summary>
public static class DdsWriter
{
    private const uint Magic = 0x20534444;      // "DDS "

    // Header flags: CAPS | HEIGHT | WIDTH | PITCH | PIXELFORMAT
    private const uint HeaderFlags = 0x1 | 0x2 | 0x4 | 0x8 | 0x1000;
    private const uint PixelFormatRgbAlpha = 0x41;   // DDPF_RGB | DDPF_ALPHAPIXELS
    private const uint CapsTexture = 0x1000;

    /// <summary>Writes BGRA bytes, four per pixel, top row first.</summary>
    public static void WriteBgra(string path, int width, int height, byte[] bgra)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            1 << 20);
        using var w = new BinaryWriter(stream);

        w.Write(Magic);
        w.Write(124u);                    // dwSize, always 124
        w.Write(HeaderFlags);
        w.Write((uint)height);
        w.Write((uint)width);
        w.Write((uint)(width * 4));       // pitch, bytes per row
        w.Write(0u);                      // depth
        w.Write(1u);                      // mip count
        for (int i = 0; i < 11; i++) w.Write(0u);   // reserved

        // DDS_PIXELFORMAT
        w.Write(32u);                     // dwSize
        w.Write(PixelFormatRgbAlpha);
        w.Write(0u);                      // fourCC (none, uncompressed)
        w.Write(32u);                     // bits per pixel
        w.Write(0x00FF0000u);             // red mask
        w.Write(0x0000FF00u);             // green mask
        w.Write(0x000000FFu);             // blue mask
        w.Write(0xFF000000u);             // alpha mask

        w.Write(CapsTexture);
        w.Write(0u);                      // caps2
        w.Write(0u);                      // caps3
        w.Write(0u);                      // caps4
        w.Write(0u);                      // reserved2

        w.Write(bgra);
    }

    // -------------------------------------------------------------------------------------
    // DXT5
    // -------------------------------------------------------------------------------------

    private const uint HeaderFlagsCompressed = 0x1 | 0x2 | 0x4 | 0x1000 | 0x80000;   // + LINEARSIZE
    private const uint PixelFormatFourCc = 0x4;
    private const uint FourCcDxt5 = 0x35545844;      // "DXT5"

    /// <summary>
    /// Writes BGRA as DXT5: a quarter of the size, single mip level.
    ///
    /// Worth it where a texture is large and drawn small. Artifact icons are the case that pays:
    /// each is a 960x240 strip weighing 922 KB uncompressed, and CK3 draws it at <b>30-60 pixels</b>,
    /// so block artefacts are far below what survives the downscale. A world's icons go from about
    /// 48 MB to 12 MB.
    ///
    /// DXT5 rather than DXT1 because the alpha is the silhouette cutout — the thing that makes an
    /// icon weapon-shaped rather than a rectangle — and DXT1's alpha is a single bit. DXT5 gives
    /// alpha its own interpolated block, which holds a soft edge.
    ///
    /// **Both dimensions must be multiples of four.** 960x240 is; the method throws rather than
    /// silently writing a texture with a torn last row or column.
    /// </summary>
    public static void WriteDxt5(string path, int width, int height, byte[] bgra)
    {
        if (width % 4 != 0 || height % 4 != 0)
            throw new ArgumentException($"DXT5 needs dimensions that are multiples of 4, got {width}x{height}");

        byte[] blocks = CompressDxt5(width, height, bgra);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            1 << 20);
        using var w = new BinaryWriter(stream);

        w.Write(Magic);
        w.Write(124u);
        w.Write(HeaderFlagsCompressed);
        w.Write((uint)height);
        w.Write((uint)width);
        w.Write((uint)blocks.Length);     // linear size: the whole (single-level) surface
        w.Write(0u);                      // depth
        w.Write(1u);                      // mip count
        for (int i = 0; i < 11; i++) w.Write(0u);

        w.Write(32u);
        w.Write(PixelFormatFourCc);
        w.Write(FourCcDxt5);
        w.Write(0u);                      // bits per pixel, unused for FourCC
        w.Write(0u);
        w.Write(0u);
        w.Write(0u);
        w.Write(0u);

        w.Write(CapsTexture);
        w.Write(0u);
        w.Write(0u);
        w.Write(0u);
        w.Write(0u);

        w.Write(blocks);
    }

    /// <summary>One 16-byte block per 4x4 texels: 8 bytes of alpha, then 8 of colour.</summary>
    private static byte[] CompressDxt5(int width, int height, byte[] bgra)
    {
        var outBytes = new byte[width / 4 * (height / 4) * 16];
        var b = new byte[16];
        var g = new byte[16];
        var r = new byte[16];
        var a = new byte[16];
        int at = 0;

        for (int by = 0; by < height; by += 4)
        {
            for (int bx = 0; bx < width; bx += 4)
            {
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int i = ((by + y) * width + bx + x) * 4;
                        int t = y * 4 + x;
                        b[t] = bgra[i];
                        g[t] = bgra[i + 1];
                        r[t] = bgra[i + 2];
                        a[t] = bgra[i + 3];
                    }
                }

                WriteAlphaBlock(outBytes, at, a);
                WriteColourBlock(outBytes, at + 8, r, g, b);
                at += 16;
            }
        }

        return outBytes;
    }

    /// <summary>
    /// Alpha endpoints and 3-bit indices.
    ///
    /// Uses the eight-value mode (<c>a0 &gt; a1</c>), which interpolates six values between the
    /// endpoints. The six-value mode reserves two codes for 0 and 255 and is worth it for alpha that
    /// is mostly fully-on or fully-off; an icon's edge and its halo are gradients, so more
    /// intermediate steps beat two exact extremes.
    /// </summary>
    private static void WriteAlphaBlock(byte[] dst, int at, byte[] a)
    {
        byte lo = 255, hi = 0;

        foreach (byte v in a)
        {
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }

        dst[at] = hi;
        dst[at + 1] = lo;

        if (hi == lo)
        {
            for (int i = 2; i < 8; i++) dst[at + i] = 0;
            return;
        }

        // Code order for a0 > a1: 0 -> a0, 1 -> a1, then 2..7 walk from a0 down to a1.
        Span<byte> table = stackalloc byte[8];
        table[0] = hi;
        table[1] = lo;
        for (int i = 0; i < 6; i++) table[i + 2] = (byte)(((6 - i) * hi + (1 + i) * lo) / 7);

        ulong bits = 0;

        for (int t = 0; t < 16; t++)
        {
            int best = 0, bestErr = int.MaxValue;

            for (int c = 0; c < 8; c++)
            {
                int err = a[t] - table[c];
                err *= err;
                if (err >= bestErr) continue;
                bestErr = err;
                best = c;
            }

            bits |= (ulong)best << (t * 3);
        }

        for (int i = 0; i < 6; i++) dst[at + 2 + i] = (byte)(bits >> (i * 8));
    }

    /// <summary>
    /// Colour endpoints and 2-bit indices.
    ///
    /// Endpoints come from the block's per-channel bounding box, which is the cheap choice and a
    /// sound one here: an icon block is a small patch of one lit metal, so its colours already lie
    /// close to a line and a principal-axis fit would land in nearly the same place.
    ///
    /// The endpoints are compared and swapped so <c>c0 &gt; c1</c>, selecting the opaque four-colour
    /// mode. In DXT5 the colour block is always read in that mode regardless, but keeping the order
    /// right means the same bytes also decode correctly if anything ever reads them as DXT1.
    /// </summary>
    private static void WriteColourBlock(byte[] dst, int at, byte[] r, byte[] g, byte[] b)
    {
        byte rl = 255, gl = 255, bl = 255, rh = 0, gh = 0, bh = 0;

        for (int t = 0; t < 16; t++)
        {
            if (r[t] < rl) rl = r[t];
            if (g[t] < gl) gl = g[t];
            if (b[t] < bl) bl = b[t];
            if (r[t] > rh) rh = r[t];
            if (g[t] > gh) gh = g[t];
            if (b[t] > bh) bh = b[t];
        }

        ushort c0 = Rgb565(rh, gh, bh);
        ushort c1 = Rgb565(rl, gl, bl);

        if (c0 < c1) (c0, c1) = (c1, c0);

        dst[at] = (byte)c0;
        dst[at + 1] = (byte)(c0 >> 8);
        dst[at + 2] = (byte)c1;
        dst[at + 3] = (byte)(c1 >> 8);

        if (c0 == c1)
        {
            dst[at + 4] = dst[at + 5] = dst[at + 6] = dst[at + 7] = 0;
            return;
        }

        // Match against the DECODED endpoints, not the 8-bit originals: quantising to 5/6/5 moves
        // them, and choosing indices against where they used to be biases every texel the same way.
        Span<int> pr = stackalloc int[4];
        Span<int> pg = stackalloc int[4];
        Span<int> pb = stackalloc int[4];

        Decode565(c0, out pr[0], out pg[0], out pb[0]);
        Decode565(c1, out pr[1], out pg[1], out pb[1]);

        for (int i = 0; i < 2; i++)
        {
            int w0 = 2 - i, w1 = 1 + i;
            pr[i + 2] = (w0 * pr[0] + w1 * pr[1]) / 3;
            pg[i + 2] = (w0 * pg[0] + w1 * pg[1]) / 3;
            pb[i + 2] = (w0 * pb[0] + w1 * pb[1]) / 3;
        }

        uint bits = 0;

        for (int t = 0; t < 16; t++)
        {
            int best = 0, bestErr = int.MaxValue;

            for (int c = 0; c < 4; c++)
            {
                int dr = r[t] - pr[c], dg = g[t] - pg[c], db = b[t] - pb[c];
                int err = dr * dr + dg * dg + db * db;
                if (err >= bestErr) continue;
                bestErr = err;
                best = c;
            }

            bits |= (uint)best << (t * 2);
        }

        for (int i = 0; i < 4; i++) dst[at + 4 + i] = (byte)(bits >> (i * 8));
    }

    private static ushort Rgb565(byte r, byte g, byte b)
        => (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

    private static void Decode565(ushort c, out int r, out int g, out int b)
    {
        int r5 = (c >> 11) & 0x1F, g6 = (c >> 5) & 0x3F, b5 = c & 0x1F;
        r = (r5 * 255 + 15) / 31;
        g = (g6 * 255 + 31) / 63;
        b = (b5 * 255 + 15) / 31;
    }
}
