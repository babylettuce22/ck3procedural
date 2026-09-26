using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Ck3MapGen.Io;

/// <summary>
/// The one DDS decoder: the top mip of a BC1, BC3 or uncompressed 32-bit texture, with or without
/// a DX10 header, to BGRA. <see cref="Emit.TextureSampler"/> used to carry a second copy that knew
/// DX10 and channel masks where this one did not, and got BC1's transparent colour wrong; this is
/// the union of the two, each case as the correct one had it.
/// </summary>
public static class DdsReader
{
    public readonly record struct DecodedImage(int Width, int Height, byte[] Bgra);

    private const uint Dxt1 = 0x31545844, Dxt5 = 0x35545844, Dx10 = 0x30315844;

    public static DecodedImage? Load(string path)
    {
        if (!File.Exists(path)) return null;

        // If it's a PNG or other standard format
        if (!path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
        {
            using var bmp = new Bitmap(path);
            return FromBitmap(bmp);
        }

        return Decode(File.ReadAllBytes(path));
    }

    /// <summary>A DDS file's top mip as BGRA, or null when it is not one this can read.</summary>
    public static DecodedImage? Decode(byte[] data)
    {
        if (data.Length < 128 || BitConverter.ToUInt32(data, 0) != 0x20534444) // "DDS "
            return null;

        int height = BitConverter.ToInt32(data, 12);
        int width = BitConverter.ToInt32(data, 16);
        uint fourCC = BitConverter.ToUInt32(data, 84);
        uint rgbBitCount = BitConverter.ToUInt32(data, 88);
        uint redMask = BitConverter.ToUInt32(data, 92);

        byte[] bgra = new byte[width * height * 4];

        switch (fourCC)
        {
            case Dxt1:
                DecodeDxt1(data, 128, width, height, bgra);
                break;

            // Common in CK3 icons.
            case Dxt5:
                DecodeDxt5(data, 128, width, height, bgra);
                break;

            // The extended header: a DXGI format after the 128 bytes, and the pixels after that.
            case Dx10 when data.Length >= 148:
                int dxgiFormat = BitConverter.ToInt32(data, 128);
                if (dxgiFormat is 70 or 71 or 72) DecodeDxt1(data, 148, width, height, bgra);
                else if (dxgiFormat is 76 or 77 or 78) DecodeDxt5(data, 148, width, height, bgra);
                else Array.Copy(data, 148, bgra, 0, Math.Min(bgra.Length, data.Length - 148));
                break;

            // Uncompressed: BGRA as written, or RGBA when the red mask sits in the low byte.
            case 0 when rgbBitCount == 32:
                CopyUncompressed(data, 128, bgra, rgba: redMask == 0x000000FF);
                break;

            default:
                return null;
        }

        return new DecodedImage(width, height, bgra);
    }

    private static void CopyUncompressed(byte[] src, int offset, byte[] dst, bool rgba)
    {
        int length = Math.Min(dst.Length, src.Length - offset);
        Array.Copy(src, offset, dst, 0, length);
        if (!rgba) return;
        for (int o = 0; o + 3 < length; o += 4)
            (dst[o], dst[o + 2]) = (dst[o + 2], dst[o]);
    }

    private static DecodedImage FromBitmap(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] bgra = new byte[bmp.Width * bmp.Height * 4];
        Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
        bmp.UnlockBits(data);
        return new DecodedImage(bmp.Width, bmp.Height, bgra);
    }

    private static void DecodeDxt1(byte[] src, int offset, int width, int height, byte[] dst)
    {
        int srcOffset = offset;
        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
            {
                if (srcOffset + 8 > src.Length) return;
                ushort c0 = BitConverter.ToUInt16(src, srcOffset);
                ushort c1 = BitConverter.ToUInt16(src, srcOffset + 2);
                uint table = BitConverter.ToUInt32(src, srcOffset + 4);
                srcOffset += 8;

                Decode565(c0, out byte r0, out byte g0, out byte b0);
                Decode565(c1, out byte r1, out byte g1, out byte b1);

                byte[][] colors = [
                    [b0, g0, r0, 255],
                    [b1, g1, r1, 255],
                    c0 > c1
                        ? [(byte)((2 * b0 + b1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * r0 + r1) / 3), 255]
                        : [(byte)((b0 + b1) / 2), (byte)((g0 + g1) / 2), (byte)((r0 + r1) / 2), 255],
                    c0 > c1
                        ? [(byte)((b0 + 2 * b1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((r0 + 2 * r1) / 3), 255]
                        : [0, 0, 0, 0]
                ];

                for (int py = 0; py < 4; py++)
                {
                    for (int px = 0; px < 4; px++)
                    {
                        if (x + px < width && y + py < height)
                        {
                            int code = (int)((table >> (2 * (py * 4 + px))) & 3);
                            int dstIdx = ((y + py) * width + (x + px)) * 4;
                            Array.Copy(colors[code], 0, dst, dstIdx, 4);
                        }
                    }
                }
            }
        }
    }

    private static void DecodeDxt5(byte[] src, int offset, int width, int height, byte[] dst)
    {
        int srcOffset = offset;
        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
            {
                if (srcOffset + 16 > src.Length) return;

                byte a0 = src[srcOffset];
                byte a1 = src[srcOffset + 1];
                ulong aTable = src[srcOffset + 2] | ((ulong)src[srcOffset + 3] << 8) |
                               ((ulong)src[srcOffset + 4] << 16) | ((ulong)src[srcOffset + 5] << 24) |
                               ((ulong)src[srcOffset + 6] << 32) | ((ulong)src[srcOffset + 7] << 40);
                srcOffset += 8;

                byte[] alphas = new byte[8];
                alphas[0] = a0;
                alphas[1] = a1;
                if (a0 > a1)
                {
                    for (int i = 1; i <= 6; i++) alphas[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
                }
                else
                {
                    for (int i = 1; i <= 4; i++) alphas[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
                    alphas[6] = 0;
                    alphas[7] = 255;
                }

                ushort c0 = BitConverter.ToUInt16(src, srcOffset);
                ushort c1 = BitConverter.ToUInt16(src, srcOffset + 2);
                uint cTable = BitConverter.ToUInt32(src, srcOffset + 4);
                srcOffset += 8;

                Decode565(c0, out byte r0, out byte g0, out byte b0);
                Decode565(c1, out byte r1, out byte g1, out byte b1);

                byte[][] rgb = [
                    [b0, g0, r0],
                    [b1, g1, r1],
                    [(byte)((2 * b0 + b1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * r0 + r1) / 3)],
                    [(byte)((b0 + 2 * b1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((r0 + 2 * r1) / 3)]
                ];

                for (int py = 0; py < 4; py++)
                {
                    for (int px = 0; px < 4; px++)
                    {
                        if (x + px < width && y + py < height)
                        {
                            int shift = (py * 4 + px) * 3;
                            int aCode = (int)((aTable >> shift) & 7);
                            int cCode = (int)((cTable >> (2 * (py * 4 + px))) & 3);

                            int dstIdx = ((y + py) * width + (x + px)) * 4;
                            dst[dstIdx + 0] = rgb[cCode][0];
                            dst[dstIdx + 1] = rgb[cCode][1];
                            dst[dstIdx + 2] = rgb[cCode][2];
                            dst[dstIdx + 3] = alphas[aCode];
                        }
                    }
                }
            }
        }
    }

    /// <summary>An RGB565 endpoint widened to 8 bits a channel by truncation. <c>DdsWriter</c>
    /// rounds instead, and keeps its own.</summary>
    private static void Decode565(ushort c, out byte r, out byte g, out byte b)
    {
        r = (byte)(((c >> 11) & 31) * 255 / 31);
        g = (byte)(((c >> 5) & 63) * 255 / 63);
        b = (byte)((c & 31) * 255 / 31);
    }
}