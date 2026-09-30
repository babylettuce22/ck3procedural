namespace Ck3MapGen.Io;

/// <summary>
/// Minimal DDS writer: uncompressed 32-bit BGRA with one mip level, or DXT1/DXT5 with one or a chain.
///
/// CK3 loads uncompressed DDS perfectly well, but the cost is size: an 8192x4096 map texture is
/// 128 MB raw, and a world ships eight of them. Vanilla block-compresses every one of those
/// (colormap DXT5 with 14 mips, flatmap DXT1, water DXT5/BC7), so the big pictures go through
/// <see cref="WriteCompressed"/>, which does the same and falls back to BGRA only where the
/// dimensions rule block compression out.
///
/// Data textures whose pixels are indices or weights rather than colours — detail_index and
/// detail_intensity — must never come here: a block shares four colours between sixteen texels,
/// and a material index blended into its neighbour paints the wrong ground.
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
    // DXT1 / DXT5
    // -------------------------------------------------------------------------------------

    private const uint HeaderFlagsCompressed = 0x1 | 0x2 | 0x4 | 0x1000 | 0x80000;   // + LINEARSIZE
    private const uint PixelFormatFourCc = 0x4;
    private const uint FourCcDxt1 = 0x31545844;      // "DXT1"
    private const uint FourCcDxt5 = 0x35545844;      // "DXT5"

    /// <summary>True when D3D can take the texture block-compressed: both sides multiples of four.
    /// Map sizes are only guaranteed even, so a world can land either side of this.</summary>
    public static bool CanBlockCompress(int width, int height) => width % 4 == 0 && height % 4 == 0;

    /// <summary>
    /// A colour picture at vanilla's format for it: DXT5 when <paramref name="alpha"/> matters,
    /// DXT1 (half DXT5's size) when it is opaque, with a mip chain when asked. Falls back to
    /// uncompressed BGRA — the same picture, larger — when the sides are not multiples of four.
    /// </summary>
    /// <returns>The format written, for the log line.</returns>
    public static string WriteCompressed(string path, int width, int height, byte[] bgra,
        bool alpha, bool mips = false)
    {
        if (!CanBlockCompress(width, height))
        {
            WriteBgra(path, width, height, bgra);
            return "BGRA";
        }

        if (alpha) WriteDxt5(path, width, height, bgra, mips);
        else WriteDxt1(path, width, height, bgra, mips);
        return alpha ? "DXT5" : "DXT1";
    }

    /// <summary>
    /// Writes BGRA as DXT1: an eighth of the size, colour only. Alpha is dropped — every texel is
    /// written opaque — so this is for pictures whose alpha carries nothing, like the flatmap.
    /// Same multiple-of-four rule and mip chain as <see cref="WriteDxt5"/>.
    /// </summary>
    public static void WriteDxt1(string path, int width, int height, byte[] bgra, bool mips = false)
        => WriteBlockCompressed(path, width, height, bgra, mips, FourCcDxt1, CompressDxt1);

    /// <summary>
    /// Writes BGRA as DXT5: a quarter of the size, one mip level unless asked for a chain.
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
    ///
    /// With <paramref name="mips"/>, each level below the first is the one above averaged 2x2,
    /// for as long as both sides stay multiples of four; the chain stops there rather than
    /// padding, which DDS allows. A texture the engine tiles and draws far smaller than its own
    /// size needs them — the snow mask's noise is sampled five times across the map, and without
    /// a smaller level to read it shimmers at any zoom out.
    /// </summary>
    public static void WriteDxt5(string path, int width, int height, byte[] bgra, bool mips = false)
        => WriteBlockCompressed(path, width, height, bgra, mips, FourCcDxt5, CompressDxt5);

    private static void WriteBlockCompressed(string path, int width, int height, byte[] bgra, bool mips,
        uint fourCc, Func<int, int, byte[], byte[]> compress)
    {
        if (!CanBlockCompress(width, height))
            throw new ArgumentException($"Block compression needs dimensions that are multiples of 4, got {width}x{height}");

        var levels = new List<byte[]> { compress(width, height, bgra) };
        if (mips)
        {
            var (lw, lh, level) = (width, height, bgra);
            while (lw / 2 % 4 == 0 && lh / 2 % 4 == 0 && lw >= 8 && lh >= 8)
            {
                level = HalveBgra(lw, lh, level);
                (lw, lh) = (lw / 2, lh / 2);
                levels.Add(compress(lw, lh, level));
            }
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            1 << 20);
        using var w = new BinaryWriter(stream);

        w.Write(Magic);
        w.Write(124u);
        w.Write(levels.Count > 1 ? HeaderFlagsCompressed | MipMapCount : HeaderFlagsCompressed);
        w.Write((uint)height);
        w.Write((uint)width);
        w.Write((uint)levels[0].Length);  // linear size: the top level's surface
        w.Write(0u);                      // depth
        w.Write((uint)levels.Count);      // mip count
        for (int i = 0; i < 11; i++) w.Write(0u);

        w.Write(32u);
        w.Write(PixelFormatFourCc);
        w.Write(fourCc);
        w.Write(0u);                      // bits per pixel, unused for FourCC
        w.Write(0u);
        w.Write(0u);
        w.Write(0u);
        w.Write(0u);

        w.Write(levels.Count > 1 ? CapsTexture | CapsComplex | CapsMipMap : CapsTexture);
        w.Write(0u);
        w.Write(0u);
        w.Write(0u);
        w.Write(0u);

        foreach (var level in levels) w.Write(level);
    }

    private const uint MipMapCount = 0x20000;        // DDSD_MIPMAPCOUNT
    private const uint CapsComplex = 0x8;            // DDSCAPS_COMPLEX
    private const uint CapsMipMap = 0x400000;        // DDSCAPS_MIPMAP

    /// <summary>The next mip level down: each texel the mean of the 2x2 above it, channel by channel.</summary>
    public static byte[] HalveBgra(int width, int height, byte[] bgra)
    {
        int hw = width / 2, hh = height / 2;
        var half = new byte[hw * hh * 4];
        Parallel.For(0, hh, y =>
        {
            int top = 2 * y * width * 4, bottom = top + width * 4;
            for (int x = 0; x < hw; x++)
            {
                int i = 2 * x * 4, o = (y * hw + x) * 4;
                for (int c = 0; c < 4; c++)
                    half[o + c] = (byte)((bgra[top + i + c] + bgra[top + i + 4 + c]
                                        + bgra[bottom + i + c] + bgra[bottom + i + 4 + c] + 2) / 4);
            }
        });
        return half;
    }

    /// <summary>One 8-byte colour block per 4x4 texels, rows of blocks in parallel as in
    /// <see cref="CompressDxt5"/>.</summary>
    private static byte[] CompressDxt1(int width, int height, byte[] bgra)
    {
        int blocksWide = width / 4;
        var outBytes = new byte[blocksWide * (height / 4) * 8];

        Parallel.For(0, height / 4, row =>
        {
            var b = new byte[16];
            var g = new byte[16];
            var r = new byte[16];
            int by = row * 4, at = row * blocksWide * 8;

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
                    }
                }

                WriteColourBlock(outBytes, at, r, g, b);
                at += 8;
            }
        });

        return outBytes;
    }

    /// <summary>One 16-byte block per 4x4 texels: 8 bytes of alpha, then 8 of colour. Each row of
    /// blocks is independent and lands at a fixed offset, so the rows run in parallel and the
    /// bytes come out the same as a serial pass.</summary>
    private static byte[] CompressDxt5(int width, int height, byte[] bgra)
    {
        int blocksWide = width / 4;
        var outBytes = new byte[blocksWide * (height / 4) * 16];

        Parallel.For(0, height / 4, row =>
        {
            var b = new byte[16];
            var g = new byte[16];
            var r = new byte[16];
            var a = new byte[16];
            int by = row * 4, at = row * blocksWide * 16;

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
        });

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
    /// Colour endpoints and 2-bit indices, fitted for the least squared error of three tries.
    ///
    /// <list type="number">
    /// <item><b>Bounding box</b>: the per-channel min and max. Cheap, and right for a block whose
    /// colours rise together, like an icon's patch of one lit metal. Wrong for one whose channels
    /// move against each other — grass shading into sand drops green as it raises red, and the
    /// box's diagonal misses both.</item>
    /// <item><b>Principal axis</b>: the line the block's colours actually spread along, with the
    /// endpoints at the extreme projections onto it. That is the grass-to-sand case.</item>
    /// <item><b>Least squares</b>: holding the best try's indices fixed, the endpoints that
    /// minimise the error exactly, twice over. Pulls the ends in from outlying texels, which a
    /// smooth colormap gradient rewards.</item>
    /// </list>
    /// The box is always one of the three, so no block comes out worse than it did before the other
    /// two existed; the colormap and flatmap, which are drawn full screen, are what the others are for.
    ///
    /// The endpoints are compared and swapped so <c>c0 &gt; c1</c>, selecting the opaque four-colour
    /// mode. In DXT5 the colour block is always read in that mode regardless; in DXT1 the other
    /// order would turn index 3 transparent.
    /// </summary>
    private static void WriteColourBlock(byte[] dst, int at, byte[] r, byte[] g, byte[] b)
    {
        byte rl = 255, gl = 255, bl = 255, rh = 0, gh = 0, bh = 0;
        double mr = 0, mg = 0, mb = 0;

        for (int t = 0; t < 16; t++)
        {
            if (r[t] < rl) rl = r[t];
            if (g[t] < gl) gl = g[t];
            if (b[t] < bl) bl = b[t];
            if (r[t] > rh) rh = r[t];
            if (g[t] > gh) gh = g[t];
            if (b[t] > bh) bh = b[t];
            mr += r[t];
            mg += g[t];
            mb += b[t];
        }

        long bestErr = Fit(Rgb565(rh, gh, bh), Rgb565(rl, gl, bl), r, g, b, out ushort c0, out ushort c1, out uint bits);

        if (bestErr > 0 && (rh != rl || gh != gl || bh != bl))
        {
            mr /= 16;
            mg /= 16;
            mb /= 16;

            double crr = 0, cgg = 0, cbb = 0, crg = 0, crb = 0, cgb = 0;
            for (int t = 0; t < 16; t++)
            {
                double dr = r[t] - mr, dg = g[t] - mg, db = b[t] - mb;
                crr += dr * dr; cgg += dg * dg; cbb += db * db;
                crg += dr * dg; crb += dr * db; cgb += dg * db;
            }

            // Power iteration from the box's diagonal, which is already near the axis for most
            // blocks; eight rounds settle it far below what 5:6:5 can tell apart.
            double ar = rh - rl, ag = gh - gl, ab = bh - bl;
            for (int k = 0; k < 8; k++)
            {
                double nr = crr * ar + crg * ag + crb * ab;
                double ng = crg * ar + cgg * ag + cgb * ab;
                double nb = crb * ar + cgb * ag + cbb * ab;
                double len = Math.Sqrt(nr * nr + ng * ng + nb * nb);
                if (len < 1e-9) break;
                (ar, ag, ab) = (nr / len, ng / len, nb / len);
            }

            double tmin = double.MaxValue, tmax = double.MinValue;
            for (int t = 0; t < 16; t++)
            {
                double p = (r[t] - mr) * ar + (g[t] - mg) * ag + (b[t] - mb) * ab;
                if (p < tmin) tmin = p;
                if (p > tmax) tmax = p;
            }

            Try(Rgb565(mr + ar * tmax, mg + ag * tmax, mb + ab * tmax),
                Rgb565(mr + ar * tmin, mg + ag * tmin, mb + ab * tmin));

            for (int k = 0; k < 2 && bestErr > 0; k++)
            {
                if (!LeastSquares(bits, r, g, b, out ushort e0, out ushort e1)) break;
                Try(e0, e1);
            }
        }

        dst[at] = (byte)c0;
        dst[at + 1] = (byte)(c0 >> 8);
        dst[at + 2] = (byte)c1;
        dst[at + 3] = (byte)(c1 >> 8);
        for (int i = 0; i < 4; i++) dst[at + 4 + i] = (byte)(bits >> (i * 8));

        void Try(ushort a, ushort z)
        {
            long err = Fit(a, z, r, g, b, out ushort f0, out ushort f1, out uint fb);
            if (err >= bestErr) return;
            (bestErr, c0, c1, bits) = (err, f0, f1, fb);
        }
    }

    /// <summary>
    /// Orders two endpoints for four-colour mode, picks each texel's nearest of the four decoded
    /// colours, and returns the block's summed squared error.
    ///
    /// Matches against the DECODED endpoints, not the 8-bit originals: quantising to 5/6/5 moves
    /// them, and choosing indices against where they used to be biases every texel the same way.
    /// </summary>
    private static long Fit(ushort a, ushort z, byte[] r, byte[] g, byte[] b,
        out ushort c0, out ushort c1, out uint bits)
    {
        (c0, c1) = a >= z ? (a, z) : (z, a);

        Span<int> pr = stackalloc int[4];
        Span<int> pg = stackalloc int[4];
        Span<int> pb = stackalloc int[4];

        Decode565(c0, out pr[0], out pg[0], out pb[0]);
        Decode565(c1, out pr[1], out pg[1], out pb[1]);

        // Equal endpoints are DXT1's three-colour mode, where index 3 is transparent; index 0
        // everywhere reads the one colour in either mode.
        int palette = c0 == c1 ? 1 : 4;

        for (int i = 0; i < 2; i++)
        {
            int w0 = 2 - i, w1 = 1 + i;
            pr[i + 2] = (w0 * pr[0] + w1 * pr[1]) / 3;
            pg[i + 2] = (w0 * pg[0] + w1 * pg[1]) / 3;
            pb[i + 2] = (w0 * pb[0] + w1 * pb[1]) / 3;
        }

        bits = 0;
        long total = 0;

        for (int t = 0; t < 16; t++)
        {
            int best = 0, bestErr = int.MaxValue;

            for (int c = 0; c < palette; c++)
            {
                int dr = r[t] - pr[c], dg = g[t] - pg[c], db = b[t] - pb[c];
                int err = dr * dr + dg * dg + db * db;
                if (err >= bestErr) continue;
                bestErr = err;
                best = c;
            }

            bits |= (uint)best << (t * 2);
            total += bestErr;
        }

        return total;
    }

    /// <summary>
    /// The endpoints that minimise squared error for fixed indices: each texel is
    /// <c>w·c0 + (1−w)·c1</c> with w = 1, 0, ⅔, ⅓ for indices 0-3, which is a 2x2 linear system
    /// per channel. False when every texel sits on one end and the system is singular.
    /// </summary>
    private static bool LeastSquares(uint bits, byte[] r, byte[] g, byte[] b, out ushort e0, out ushort e1)
    {
        double aa = 0, ab = 0, bb = 0, ar = 0, ag = 0, abl = 0, br = 0, bg = 0, bbl = 0;

        for (int t = 0; t < 16; t++)
        {
            double w = ((bits >> (t * 2)) & 3) switch { 0 => 1.0, 1 => 0.0, 2 => 2.0 / 3, _ => 1.0 / 3 };
            double v = 1 - w;
            aa += w * w; ab += w * v; bb += v * v;
            ar += w * r[t]; ag += w * g[t]; abl += w * b[t];
            br += v * r[t]; bg += v * g[t]; bbl += v * b[t];
        }

        double det = aa * bb - ab * ab;
        if (Math.Abs(det) < 1e-9)
        {
            e0 = e1 = 0;
            return false;
        }

        double f = 1 / det;
        e0 = Rgb565((bb * ar - ab * br) * f, (bb * ag - ab * bg) * f, (bb * abl - ab * bbl) * f);
        e1 = Rgb565((aa * br - ab * ar) * f, (aa * bg - ab * ag) * f, (aa * bbl - ab * abl) * f);
        return true;
    }

    /// <summary>Rounds to the nearest 5:6:5 step, clamped, for fitted endpoints that land between
    /// or beyond the 8-bit ones.</summary>
    private static ushort Rgb565(double r, double g, double b)
    {
        int r5 = (int)Math.Round(Math.Clamp(r, 0, 255) * 31 / 255);
        int g6 = (int)Math.Round(Math.Clamp(g, 0, 255) * 63 / 255);
        int b5 = (int)Math.Round(Math.Clamp(b, 0, 255) * 31 / 255);
        return (ushort)((r5 << 11) | (g6 << 5) | b5);
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
