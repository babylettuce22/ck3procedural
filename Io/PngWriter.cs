using System.Buffers.Binary;
using System.IO.Compression;

namespace Ck3MapGen.Io;

/// <summary>
/// Minimal PNG encoder. CK3 is strict about pixel format and each map file needs a different
/// exact one — 16-bit greyscale for the heightmap, 8-bit RGB for provinces, 8-bit RGBA for the
/// indirection map, palettised for rivers — so we emit the bytes ourselves rather than trusting
/// a general imaging library to preserve them.
///
/// Everything is written as a single uncompressed-filter (filter type 0) image; CK3 reads these
/// fine and it keeps the encoder trivial to audit.
/// </summary>
public static class PngWriter
{
    /// <summary>8-bit greyscale — debug elevation dumps.</summary>
    public static void WriteGray8(string path, int width, int height, byte[] pixels)
        => Write(path, width, height, 8, ColorType.Gray, pixels, bytesPerPixel: 1);

    /// <summary>16-bit greyscale, big-endian samples — the CK3 heightmap format.</summary>
    public static void WriteGray16(string path, int width, int height, ushort[] pixels)
    {
        var bytes = new byte[width * height * 2];
        for (int i = 0; i < pixels.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), pixels[i]);
        Write(path, width, height, bitDepth: 16, ColorType.Gray, bytes, bytesPerPixel: 2);
    }

    /// <summary>8-bit RGB — provinces.png.</summary>
    public static void WriteRgb8(string path, int width, int height, byte[] rgb)
        => Write(path, width, height, 8, ColorType.Rgb, rgb, bytesPerPixel: 3);

    /// <summary>8-bit RGBA — indirection_heightmap.png.</summary>
    public static void WriteRgba8(string path, int width, int height, byte[] rgba)
        => Write(path, width, height, 8, ColorType.Rgba, rgba, bytesPerPixel: 4);

    /// <summary>
    /// 8-bit palettised. <paramref name="palette"/> is RGB triples; CK3's rivers.png requires a
    /// verbatim palette, so the caller supplies it exactly.
    /// </summary>
    public static void WriteIndexed8(string path, int width, int height, byte[] indices, byte[] palette)
    {
        using var stream = File.Create(path);
        WriteSignature(stream);
        WriteIhdr(stream, width, height, 8, ColorType.Indexed);
        WriteChunk(stream, "PLTE", palette);
        WriteIdat(stream, width, height, indices, bytesPerPixel: 1);
        WriteChunk(stream, "IEND", []);
    }

    private enum ColorType : byte
    {
        Gray = 0,
        Rgb = 2,
        Indexed = 3,
        Rgba = 6,
    }

    private static void Write(string path, int width, int height, int bitDepth, ColorType colorType,
        byte[] pixels, int bytesPerPixel)
    {
        using var stream = File.Create(path);
        WriteSignature(stream);
        WriteIhdr(stream, width, height, (byte)bitDepth, colorType);
        WriteIdat(stream, width, height, pixels, bytesPerPixel);
        WriteChunk(stream, "IEND", []);
    }

    private static void WriteSignature(Stream s)
        => s.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

    private static void WriteIhdr(Stream s, int width, int height, byte bitDepth, ColorType colorType)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = bitDepth;
        header[9] = (byte)colorType;
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // no interlace
        WriteChunk(s, "IHDR", header);
    }

    private static void WriteIdat(Stream s, int width, int height, byte[] pixels, int bytesPerPixel)
    {
        int stride = width * bytesPerPixel;
        using var raw = new MemoryStream((stride + 1) * height);
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filter: None
            raw.Write(pixels, y * stride, stride);
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(deflate);
        }
        WriteChunk(s, "IDAT", compressed.ToArray());
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        s.Write(length);

        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        s.Write(typeBytes);
        s.Write(data);

        uint crc = Crc32.Compute(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        s.Write(crcBytes);
    }
}

internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public static uint Compute(byte[] a, byte[] b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte v in a) c = Table[(c ^ v) & 0xFF] ^ (c >> 8);
        foreach (byte v in b) c = Table[(c ^ v) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
