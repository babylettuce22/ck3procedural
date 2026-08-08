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
}
