namespace Ck3MapGen.Emit;

using System;
using System.IO;

public class TextureSampler
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public TextureSampler(int width, int height, byte[] bgra)
    {
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public static TextureSampler? TryLoad(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            byte[] raw = File.ReadAllBytes(filePath);
            if (raw.Length < 128 || raw[0] != 'D' || raw[1] != 'D' || raw[2] != 'S' || raw[3] != ' ')
                return null;

            int height = BitConverter.ToInt32(raw, 12);
            int width = BitConverter.ToInt32(raw, 16);
            int fourCC = BitConverter.ToInt32(raw, 84);
            int bitCount = BitConverter.ToInt32(raw, 88);
            int rMask = BitConverter.ToInt32(raw, 92);

            byte[] bgra = new byte[width * height * 4];

            // DXT1 / BC1
            if (fourCC == 0x31545844)
            {
                DecodeBc1(raw, 128, width, height, bgra);
                return new TextureSampler(width, height, bgra);
            }
            // DXT5 / BC3
            else if (fourCC == 0x35545844)
            {
                DecodeBc3(raw, 128, width, height, bgra);
                return new TextureSampler(width, height, bgra);
            }
            // DX10
            else if (fourCC == 0x30315844 && raw.Length >= 148)
            {
                int dxgiFormat = BitConverter.ToInt32(raw, 128);
                if (dxgiFormat is 70 or 71 or 72)
                {
                    DecodeBc1(raw, 148, width, height, bgra);
                    return new TextureSampler(width, height, bgra);
                }
                else if (dxgiFormat is 76 or 77 or 78)
                {
                    DecodeBc3(raw, 148, width, height, bgra);
                    return new TextureSampler(width, height, bgra);
                }
                else
                {
                    Array.Copy(raw, 148, bgra, 0, Math.Min(bgra.Length, raw.Length - 148));
                    return new TextureSampler(width, height, bgra);
                }
            }
            // Uncompressed 32-bit
            else if (bitCount == 32)
            {
                int offset = 128;
                bool isRgba = (rMask == 0x000000FF);

                for (int i = 0; i < width * height && offset + 3 < raw.Length; i++)
                {
                    int o = i * 4;
                    if (isRgba)
                    {
                        bgra[o + 0] = raw[offset + 2];
                        bgra[o + 1] = raw[offset + 1];
                        bgra[o + 2] = raw[offset + 0];
                        bgra[o + 3] = raw[offset + 3];
                    }
                    else
                    {
                        bgra[o + 0] = raw[offset + 0];
                        bgra[o + 1] = raw[offset + 1];
                        bgra[o + 2] = raw[offset + 2];
                        bgra[o + 3] = raw[offset + 3];
                    }
                    offset += 4;
                }
                return new TextureSampler(width, height, bgra);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [TextureSampler] Error reading '{filePath}': {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Samples the texture using dual-phase crossfading for completely seamless tiling.
    /// </summary>
    public (float R, float G, float B) SampleSeamlessTiled(float worldX, float worldY, float tileSize = 1024f)
    {
        float scaleX = Width / tileSize;
        float scaleY = Height / tileSize;

        float u1 = PositiveMod(worldX * scaleX, Width);
        float v1 = PositiveMod(worldY * scaleY, Height);

        float u2 = PositiveMod(worldX * scaleX + Width * 0.5f, Width);
        float v2 = PositiveMod(worldY * scaleY + Height * 0.5f, Height);

        var (r1, g1, b1) = SampleBilinear(u1, v1);
        var (r2, g2, b2) = SampleBilinear(u2, v2);

        // Smooth raised-cosine blend weight
        float nu = u1 / Width;
        float nv = v1 / Height;

        float wx = 0.5f - 0.5f * MathF.Cos(nu * MathF.PI * 2f);
        float wy = 0.5f - 0.5f * MathF.Cos(nv * MathF.PI * 2f);
        float w1 = wx * wy;
        float w2 = 1.0f - w1;

        float r = r1 * w1 + r2 * w2;
        float g = g1 * w1 + g2 * w2;
        float b = b1 * w1 + b2 * w2;

        return (r, g, b);
    }

    private (float R, float G, float B) SampleBilinear(float u, float v)
    {
        int x0 = (int)MathF.Floor(u);
        int y0 = (int)MathF.Floor(v);
        int x1 = (x0 + 1) % Width;
        int y1 = (y0 + 1) % Height;

        float fx = u - x0;
        float fy = v - y0;

        int i00 = (y0 * Width + x0) * 4;
        int i10 = (y0 * Width + x1) * 4;
        int i01 = (y1 * Width + x0) * 4;
        int i11 = (y1 * Width + x1) * 4;

        float b = Bilinear(Bgra[i00 + 0], Bgra[i10 + 0], Bgra[i01 + 0], Bgra[i11 + 0], fx, fy);
        float g = Bilinear(Bgra[i00 + 1], Bgra[i10 + 1], Bgra[i01 + 1], Bgra[i11 + 1], fx, fy);
        float r = Bilinear(Bgra[i00 + 2], Bgra[i10 + 2], Bgra[i01 + 2], Bgra[i11 + 2], fx, fy);

        return (r, g, b);
    }

    private static float PositiveMod(float val, float m)
    {
        float mod = val % m;
        return mod < 0 ? mod + m : mod;
    }

    private static float Bilinear(float c00, float c10, float c01, float c11, float fx, float fy)
    {
        float top = c00 + (c10 - c00) * fx;
        float bot = c01 + (c11 - c01) * fx;
        return top + (bot - top) * fy;
    }

    private static void DecodeBc1(byte[] src, int srcOffset, int width, int height, byte[] dst)
    {
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                if (srcOffset + 8 > src.Length) return;

                ushort c0 = BitConverter.ToUInt16(src, srcOffset);
                ushort c1 = BitConverter.ToUInt16(src, srcOffset + 2);
                uint table = BitConverter.ToUInt32(src, srcOffset + 4);
                srcOffset += 8;

                Decode565(c0, out byte r0, out byte g0, out byte b0);
                Decode565(c1, out byte r1, out byte g1, out byte b1);

                byte[][] colors = new byte[4][];
                colors[0] = [b0, g0, r0, 255];
                colors[1] = [b1, g1, r1, 255];

                if (c0 > c1)
                {
                    colors[2] = [(byte)((2 * b0 + b1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * r0 + r1) / 3), 255];
                    colors[3] = [(byte)((b0 + 2 * b1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((r0 + 2 * r1) / 3), 255];
                }
                else
                {
                    colors[2] = [(byte)((b0 + b1) / 2), (byte)((g0 + g1) / 2), (byte)((r0 + r1) / 2), 255];
                    colors[3] = [0, 0, 0, 255];
                }

                for (int py = 0; py < 4; py++)
                {
                    int y = by * 4 + py;
                    if (y >= height) continue;

                    for (int px = 0; px < 4; px++)
                    {
                        int x = bx * 4 + px;
                        if (x >= width) continue;

                        int code = (int)((table >> (2 * (py * 4 + px))) & 0x03);
                        int dstIdx = (y * width + x) * 4;

                        dst[dstIdx + 0] = colors[code][0];
                        dst[dstIdx + 1] = colors[code][1];
                        dst[dstIdx + 2] = colors[code][2];
                        dst[dstIdx + 3] = colors[code][3];
                    }
                }
            }
        }
    }

    private static void DecodeBc3(byte[] src, int srcOffset, int width, int height, byte[] dst)
    {
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                if (srcOffset + 16 > src.Length) return;

                byte a0 = src[srcOffset];
                byte a1 = src[srcOffset + 1];
                ulong aIndices = ((ulong)src[srcOffset + 2]) |
                                 ((ulong)src[srcOffset + 3] << 8) |
                                 ((ulong)src[srcOffset + 4] << 16) |
                                 ((ulong)src[srcOffset + 5] << 24) |
                                 ((ulong)src[srcOffset + 6] << 32) |
                                 ((ulong)src[srcOffset + 7] << 40);

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

                ushort c0 = BitConverter.ToUInt16(src, srcOffset + 8);
                ushort c1 = BitConverter.ToUInt16(src, srcOffset + 10);
                uint cTable = BitConverter.ToUInt32(src, srcOffset + 12);
                srcOffset += 16;

                Decode565(c0, out byte r0, out byte g0, out byte b0);
                Decode565(c1, out byte r1, out byte g1, out byte b1);

                byte[][] colors = new byte[4][];
                colors[0] = [b0, g0, r0];
                colors[1] = [b1, g1, r1];
                colors[2] = [(byte)((2 * b0 + b1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * r0 + r1) / 3)];
                colors[3] = [(byte)((b0 + 2 * b1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((r0 + 2 * r1) / 3)];

                for (int py = 0; py < 4; py++)
                {
                    int y = by * 4 + py;
                    if (y >= height) continue;

                    for (int px = 0; px < 4; px++)
                    {
                        int x = bx * 4 + px;
                        if (x >= width) continue;

                        int pixelIdx = py * 4 + px;
                        int cCode = (int)((cTable >> (2 * pixelIdx)) & 0x03);
                        int aCode = (int)((aIndices >> (3 * pixelIdx)) & 0x07);

                        int dstIdx = (y * width + x) * 4;
                        dst[dstIdx + 0] = colors[cCode][0];
                        dst[dstIdx + 1] = colors[cCode][1];
                        dst[dstIdx + 2] = colors[cCode][2];
                        dst[dstIdx + 3] = alphas[aCode];
                    }
                }
            }
        }
    }

    private static void Decode565(ushort c, out byte r, out byte g, out byte b)
    {
        r = (byte)(((c >> 11) & 0x1F) * 255 / 31);
        g = (byte)(((c >> 5) & 0x3F) * 255 / 63);
        b = (byte)((c & 0x1F) * 255 / 31);
    }
}