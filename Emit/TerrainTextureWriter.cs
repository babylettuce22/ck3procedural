using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class TerrainTextureWriter
{
    public static bool[] UsedMaterials { get; private set; } = new bool[256];

    public static void WriteAll(string modDir, MapConfig cfg, TerrainClass[] terrain,
        KoppenClass[] climate, float[] elevation, Rng rng)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "terrain");
        Directory.CreateDirectory(dir);

        int width = cfg.Width, height = cfg.Height;
        int pWidth = cfg.ProvinceWidth, pHeight = cfg.ProvinceHeight;
        int sea = cfg.Limits.SeaLevelUpper;
        int mountains = cfg.Limits.Mountains.Lower;

        var nAField = new SimplexNoise(rng);
        var nBField = new SimplexNoise(rng);
        var nCField = new SimplexNoise(rng);
        var warpField = new SimplexNoise(rng);
        var broadWarp = new SimplexNoise(rng);

        const int reference = MapConfig.ReferenceProvinceWidth;
        double fA = 45.0 / reference, fB = 110.0 / reference, fC = 260.0 / reference;
        double fWarp = 20.0 / reference;
        double fBroad = 6.0 / reference;

        double scaleX = (double)pWidth / width;
        double scaleY = (double)pHeight / height;

        // Wide blend radius (~50-100 heightmap pixels) to guarantee smooth photographic transitions
        double blendRadius = cfg.Scaled(52.0);

        var used = new bool[256];
        object gate = new();

        {
            var index = new byte[(long)width * height * 4];
            var intensity = new byte[(long)width * height * 4];

            Parallel.For(0, height, () => new bool[256], (y, _, localUsed) =>
            {
                int srcY = height - 1 - y;
                long row = (long)y * width * 4;

                Span<byte> candidateMats = stackalloc byte[16];
                Span<float> candidateWeights = stackalloc float[16];

                ReadOnlySpan<float> probeWeights = [0.36f, 0.16f, 0.16f, 0.16f, 0.16f];
                ReadOnlySpan<float> probeDx = [0f, 1f, -1f, 0f, 0f];
                ReadOnlySpan<float> probeDy = [0f, 0f, 0f, 1f, -1f];

                for (int x = 0; x < width; x++)
                {
                    long fullSrc = (long)srcY * width + x;
                    double relief = (elevation[fullSrc] - sea) / (double)Math.Max(1, mountains - sea);

                    // Multi-scale domain warping
                    double qx = warpField.Noise2D(x * fWarp, srcY * fWarp) * 14.0
                              + broadWarp.Noise2D(x * fBroad, srcY * fBroad) * 32.0;
                    double qy = warpField.Noise2D(x * fWarp + 17.1, srcY * fWarp - 11.3) * 14.0
                              + broadWarp.Noise2D(x * fBroad + 23.4, srcY * fBroad - 41.8) * 32.0;

                    double wx = x + qx;
                    double wy = srcY + qy;

                    double nA = Math.Clamp(Field.Fbm(nAField, wx * fA, wy * fA, 3) * 0.5 + 0.5, 0, 1);
                    double nB = Math.Clamp(Field.Fbm(nBField, wx * fB + 31.7, wy * fB - 19.3, 3) * 0.5 + 0.5, 0, 1);
                    double nC = Math.Clamp(Field.Fbm(nCField, wx * fC - 11.2, wy * fC + 43.1, 2) * 0.5 + 0.5, 0, 1);

                    int count = 0;

                    // Wide 5-probe continuous stencil (Center + 4 cardinal probes at blend radius)
                    for (int p = 0; p < 5; p++)
                    {
                        double px = wx + probeDx[p] * blendRadius;
                        double py = wy + probeDy[p] * blendRadius;

                        int sx = Math.Clamp((int)Math.Round(px * scaleX), 0, pWidth - 1);
                        int sy = Math.Clamp((int)Math.Round(py * scaleY), 0, pHeight - 1);
                        int pSrc = sy * pWidth + sx;

                        var b = TerrainPalette.For(terrain[pSrc], TerrainPalette.ClimateOf(climate[pSrc]), relief, nA, nB, nC);
                        AccumulateBlend(b, probeWeights[p], candidateMats, candidateWeights, ref count);
                    }

                    // Extract top 4 materials
                    byte m0 = 0, m1 = 0, m2 = 0, m3 = 0;
                    float cw0 = 0, cw1 = 0, cw2 = 0, cw3 = 0;

                    for (int k = 0; k < 4; k++)
                    {
                        int bestIdx = -1;
                        float bestW = 0;
                        for (int i = 0; i < count; i++)
                        {
                            if (candidateWeights[i] > bestW)
                            {
                                bestW = candidateWeights[i];
                                bestIdx = i;
                            }
                        }
                        if (bestIdx < 0) break;

                        byte mat = candidateMats[bestIdx];
                        candidateWeights[bestIdx] = 0;

                        if (k == 0) { m0 = mat; cw0 = bestW; }
                        else if (k == 1) { m1 = mat; cw1 = bestW; }
                        else if (k == 2) { m2 = mat; cw2 = bestW; }
                        else if (k == 3) { m3 = mat; cw3 = bestW; }
                    }

                    if (m1 == 0) m1 = m0;
                    if (m2 == 0) m2 = m0;
                    if (m3 == 0) m3 = m0;

                    // Normalize weights to 255 total intensity
                    float total = cw0 + cw1 + cw2 + cw3;
                    byte outW0 = 255, outW1 = 0, outW2 = 0, outW3 = 0;
                    if (total > 0.001f)
                    {
                        float inv = 255f / total;
                        outW0 = (byte)Math.Clamp((int)Math.Round(cw0 * inv), 0, 255);
                        outW1 = (byte)Math.Clamp((int)Math.Round(cw1 * inv), 0, 255);
                        outW2 = (byte)Math.Clamp((int)Math.Round(cw2 * inv), 0, 255);
                        outW3 = (byte)Math.Clamp(255 - outW0 - outW1 - outW2, 0, 255);
                    }

                    long o = row + x * 4;
                    index[o + 2] = m0;
                    index[o + 1] = m1;
                    index[o + 0] = m2;
                    index[o + 3] = m3;

                    intensity[o + 2] = outW0;
                    intensity[o + 1] = outW1;
                    intensity[o + 0] = outW2;
                    intensity[o + 3] = outW3;

                    if (outW0 > 0) localUsed[m0] = true;
                    if (outW1 > 0) localUsed[m1] = true;
                    if (outW2 > 0) localUsed[m2] = true;
                    if (outW3 > 0) localUsed[m3] = true;
                }
                return localUsed;
            }, localUsed => { lock (gate) for (int i = 0; i < 256; i++) if (localUsed[i]) used[i] = true; });

            WriteTga(Path.Combine(dir, "detail_index.tga"), width, height, index);
            WriteTga(Path.Combine(dir, "detail_intensity.tga"), width, height, intensity);
        }

        used[TerrainPalette.Unused] = false;
        UsedMaterials = used;

        Console.WriteLine($"  terrain: detail_index + detail_intensity {width}x{height}, wide-kernel splatting active");

        // Bilinear continuous colormap.dds
        {
            var colormap = new byte[(long)width * height * 4];
            Parallel.For(0, height, y =>
            {
                long row = (long)y * width * 4;
                double gy = y * scaleY;
                int y0 = Math.Clamp((int)Math.Floor(gy), 0, pHeight - 1);
                int y1 = Math.Clamp(y0 + 1, 0, pHeight - 1);
                double fy = gy - y0;

                for (int x = 0; x < width; x++)
                {
                    double gx = x * scaleX;
                    int x0 = Math.Clamp((int)Math.Floor(gx), 0, pWidth - 1);
                    int x1 = Math.Clamp(x0 + 1, 0, pWidth - 1);
                    double fx = gx - x0;

                    long fullSrc = (long)y * width + x;
                    double relief = (elevation[fullSrc] - sea) / (double)Math.Max(1, mountains - sea);
                    double nC = Selector(nCField, x * fC - 7.3, y * fC + 29.4);

                    var c00 = GroundColor(terrain[y0 * pWidth + x0], relief, nC);
                    var c10 = GroundColor(terrain[y0 * pWidth + x1], relief, nC);
                    var c01 = GroundColor(terrain[y1 * pWidth + x0], relief, nC);
                    var c11 = GroundColor(terrain[y1 * pWidth + x1], relief, nC);

                    double r = (1 - fx) * (1 - fy) * c00.R + fx * (1 - fy) * c10.R + (1 - fx) * fy * c01.R + fx * fy * c11.R;
                    double g = (1 - fx) * (1 - fy) * c00.G + fx * (1 - fy) * c10.G + (1 - fx) * fy * c01.G + fx * fy * c11.G;
                    double b = (1 - fx) * (1 - fy) * c00.B + fx * (1 - fy) * c10.B + (1 - fx) * fy * c01.B + fx * fy * c11.B;

                    long o = row + x * 4;
                    colormap[o] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
                    colormap[o + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
                    colormap[o + 2] = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
                    colormap[o + 3] = 255;
                }
            });
            DdsWriter.WriteBgra(Path.Combine(dir, "colormap.dds"), width, height, colormap);
        }

        {
            var flatmap = new byte[(long)width * height * 4];
            Parallel.For(0, height, y =>
            {
                int py = Math.Clamp((int)((long)y * pHeight / height), 0, pHeight - 1);
                long row = (long)y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int px = Math.Clamp((int)((long)x * pWidth / width), 0, pWidth - 1);
                    var (pr, pg, pb) = terrain[py * pWidth + px] == TerrainClass.Sea
                        ? (172, 164, 138)
                        : (214, 195, 155);
                    long o = row + x * 4;
                    flatmap[o] = (byte)pb; flatmap[o + 1] = (byte)pg; flatmap[o + 2] = (byte)pr;
                    flatmap[o + 3] = 255;
                }
            });

            string flatDir = Path.Combine(dir, "flat_maps");
            Directory.CreateDirectory(flatDir);
            DdsWriter.WriteBgra(Path.Combine(flatDir, "flatmap.dds"), width, height, flatmap);
            DdsWriter.WriteBgra(Path.Combine(flatDir, "flatmap_tgp.dds"), width, height, flatmap);
        }

        Console.WriteLine($"  terrain: colormap + flatmap {width}x{height}");
    }

    private static void AccumulateBlend(TerrainPalette.Blend b, float factor,
        Span<byte> candidateMats, Span<float> candidateWeights, ref int count)
    {
        AccumulateLayer(b.M0, b.W0 * factor, candidateMats, candidateWeights, ref count);
        AccumulateLayer(b.M1, b.W1 * factor, candidateMats, candidateWeights, ref count);
        AccumulateLayer(b.M2, b.W2 * factor, candidateMats, candidateWeights, ref count);
        AccumulateLayer(b.M3, b.W3 * factor, candidateMats, candidateWeights, ref count);
    }

    private static void AccumulateLayer(byte mat, float w,
        Span<byte> candidateMats, Span<float> candidateWeights, ref int count)
    {
        if (mat == TerrainPalette.Unused || w <= 0.01f) return;
        for (int i = 0; i < count; i++)
        {
            if (candidateMats[i] == mat)
            {
                candidateWeights[i] += w;
                return;
            }
        }
        if (count < 16)
        {
            candidateMats[count] = mat;
            candidateWeights[count] = w;
            count++;
        }
    }

    private static double Selector(SimplexNoise field, double x, double y)
        => Math.Clamp(Field.Fbm(field, x, y, 3) * 0.75 + 0.5, 0, 1);

    private static (byte R, byte G, byte B) GroundColor(TerrainClass t, double relief, double n)
    {
        var (r, g, b) = t switch
        {
            TerrainClass.Sea => (58, 74, 82),
            TerrainClass.Beach => (198, 186, 148),
            TerrainClass.Plains => (94, 112, 62),
            TerrainClass.Farmlands => (110, 118, 58),
            TerrainClass.Steppe => (146, 140, 86),
            TerrainClass.Drylands => (168, 146, 96),
            TerrainClass.Desert => (198, 176, 128),
            TerrainClass.Jungle => (62, 96, 46),
            TerrainClass.Forest => (68, 90, 50),
            TerrainClass.Taiga => (78, 94, 68),
            TerrainClass.Wetlands => (92, 110, 84),
            TerrainClass.Floodplains => (120, 128, 74),
            TerrainClass.Hills => (108, 112, 74),
            TerrainClass.Mountains => (122, 114, 104),
            TerrainClass.DesertMountains => (156, 132, 100),
            TerrainClass.Arctic => (232, 236, 240),
            _ => (94, 112, 62),
        };

        double shade = 1.0 - Math.Clamp(relief, 0, 1) * 0.12 + (n - 0.5) * 0.08;
        return ((byte)Math.Clamp(r * shade, 0, 255),
                (byte)Math.Clamp(g * shade, 0, 255),
                (byte)Math.Clamp(b * shade, 0, 255));
    }

    private static void WriteTga(string path, int width, int height, byte[] bgra)
    {
        var header = new byte[18];
        header[2] = 2;
        header[12] = (byte)(width & 0xFF);
        header[13] = (byte)(width >> 8);
        header[14] = (byte)(height & 0xFF);
        header[15] = (byte)(height >> 8);
        header[16] = 32;
        header[17] = 0x08;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        stream.Write(header);
        stream.Write(bgra);
    }
}