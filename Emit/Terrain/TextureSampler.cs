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
            // Decoded by the one DDS reader; this class only samples. A file that is not a DDS,
            // or one in a format it does not read, gives null rather than a PNG decode.
            if (Io.DdsReader.Decode(File.ReadAllBytes(filePath)) is { } image)
                return new TextureSampler(image.Width, image.Height, image.Bgra);
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
}