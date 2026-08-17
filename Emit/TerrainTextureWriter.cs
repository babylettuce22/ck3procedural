using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class TerrainTextureWriter
{
    public static bool[] UsedMaterials { get; private set; } = new bool[256];

    /// <summary>Orthogonal step cost in the chamfer distance transform; diagonal is 4.</summary>
    private const int ChamferOrthogonal = 3;
    private const int ChamferDiagonal = 4;

    /// <summary>
    /// For every pixel: the distance to the nearest ground of a *different* label, measured without
    /// leaving its own, and which label that is.
    /// </summary>
    private static (ushort[] Distance, byte[] Other) BoundaryField(byte[] label, int width, int height)
    {
        int n = width * height;
        var distance = new ushort[n];
        var other = new byte[n];
        const ushort Far = ushort.MaxValue;

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                byte self = label[i];
                distance[i] = Far;
                other[i] = self;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= height) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= width || (dx == 0 && dy == 0)) continue;

                        byte neighbour = label[yy * width + xx];
                        if (neighbour == self) continue;

                        distance[i] = 0;
                        other[i] = neighbour;
                        dy = 2;
                        break;
                    }
                }
            }
        });

        // Forward scan, then backward.
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (distance[i] == 0) continue;
                Relax(i, x - 1, y, ChamferOrthogonal);
                Relax(i, x - 1, y - 1, ChamferDiagonal);
                Relax(i, x, y - 1, ChamferOrthogonal);
                Relax(i, x + 1, y - 1, ChamferDiagonal);
            }

        for (int y = height - 1; y >= 0; y--)
            for (int x = width - 1; x >= 0; x--)
            {
                int i = y * width + x;
                if (distance[i] == 0) continue;
                Relax(i, x + 1, y, ChamferOrthogonal);
                Relax(i, x + 1, y + 1, ChamferDiagonal);
                Relax(i, x, y + 1, ChamferOrthogonal);
                Relax(i, x - 1, y + 1, ChamferDiagonal);
            }

        return (distance, other);

        void Relax(int target, int x, int y, int cost)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;

            int from = y * width + x;
            if (label[from] != label[target] || distance[from] == Far) return;

            int candidate = distance[from] + cost;
            if (candidate >= distance[target]) return;

            distance[target] = (ushort)candidate;
            other[target] = other[from];
        }
    }

    /// <summary>
    /// Central-difference gradient magnitude at one heightmap pixel, in elevation units per pixel.
    /// </summary>
    private static float Gradient(float[] elevation, int width, int height, int x, int y)
    {
        int xm = Math.Max(0, x - 1), xp = Math.Min(width - 1, x + 1);
        int ym = Math.Max(0, y - 1), yp = Math.Min(height - 1, y + 1);

        float dx = elevation[(long)y * width + xp] - elevation[(long)y * width + xm];
        float dy = elevation[(long)yp * width + x] - elevation[(long)ym * width + x];
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static (float Start, float Full) CliffLines(float[] elevation, int width, int height,
        int sea, double share, byte[] coastDistance, int pWidth, int pHeight, int reach)
    {
        const int Stride = 4;
        const int Bins = 2048;

        double toProvinceX = (double)pWidth / width;
        double toProvinceY = (double)pHeight / height;

        bool Sampled(int x, int y)
        {
            if (elevation[(long)y * width + x] <= sea) return false;

            int px = Math.Clamp((int)(x * toProvinceX), 0, pWidth - 1);
            int py = Math.Clamp((int)(y * toProvinceY), 0, pHeight - 1);
            byte d = coastDistance[(long)py * pWidth + px];
            return d >= 1 && d <= reach;
        }

        int rows = (height + Stride - 1) / Stride;

        float max = 0;
        object gate = new();
        Parallel.For(0, rows, () => 0f, (row, _, localMax) =>
        {
            int y = row * Stride;
            for (int x = 0; x < width; x += Stride)
            {
                if (!Sampled(x, y)) continue;
                float g = Gradient(elevation, width, height, x, y);
                if (g > localMax) localMax = g;
            }
            return localMax;
        }, localMax => { lock (gate) if (localMax > max) max = localMax; });

        if (max <= 0) return (float.MaxValue, float.MaxValue);

        var histogram = new long[Bins];
        double scale = (Bins - 1) / max;

        Parallel.For(0, rows, () => new long[Bins], (row, _, local) =>
        {
            int y = row * Stride;
            for (int x = 0; x < width; x += Stride)
            {
                if (!Sampled(x, y)) continue;
                float g = Gradient(elevation, width, height, x, y);
                local[Math.Clamp((int)(g * scale), 0, Bins - 1)]++;
            }
            return local;
        }, local => { lock (gate) for (int b = 0; b < Bins; b++) histogram[b] += local[b]; });

        long total = 0;
        foreach (long n in histogram) total += n;
        if (total == 0) return (float.MaxValue, float.MaxValue);

        float Percentile(double fraction)
        {
            long target = (long)(total * Math.Clamp(fraction, 0, 1));
            long running = 0;
            for (int b = 0; b < Bins; b++)
            {
                running += histogram[b];
                if (running >= target) return (float)(b / scale);
            }
            return max;
        }

        return (Percentile(1.0 - share), Percentile(1.0 - share * 0.25));
    }

    public static void WriteAll(string modDir, MapConfig cfg, TerrainClass[] terrain,
        KoppenClass[] climate, float[] elevation, Rng rng)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "terrain");
        Directory.CreateDirectory(dir);

        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int pWidth = cfg.ProvinceWidth, pHeight = cfg.ProvinceHeight;

        int hWidth = cfg.Width, hHeight = cfg.Height;
        double toHeightX = (double)hWidth / width;
        double toHeightY = (double)hHeight / height;

        int sea = cfg.Limits.SeaLevelUpper;
        int mountains = cfg.Limits.Mountains.Lower;

        var nAField = new SimplexNoise(rng);
        var nBField = new SimplexNoise(rng);
        var nCField = new SimplexNoise(rng);
        var warpField = new SimplexNoise(rng);
        var broadWarp = new SimplexNoise(rng);
        var bandField = new SimplexNoise(rng);
        var interlockField = new SimplexNoise(rng);
        var shareField = new SimplexNoise(rng);

        const int reference = MapConfig.ReferenceProvinceWidth;
        double fA = 45.0 / reference, fB = 110.0 / reference, fC = 260.0 / reference;
        double fWarp = 20.0 / reference;
        double fBroad = 6.0 / reference;

        double scaleX = (double)pWidth / hWidth;
        double scaleY = (double)pHeight / hHeight;

        float blendReach = (float)Math.Max(1.0, cfg.Scaled(cfg.TerrainBlendReach));
        double bandFrequency = 170.0 / reference;
        double interlockFrequency = fA * 4;

        var label = new byte[terrain.Length];
        Parallel.For(0, terrain.Length, i => label[i] = TerrainPalette.Label(terrain[i], climate[i]));

        var (boundaryDistance, boundaryOther) = BoundaryField(label, pWidth, pHeight);

        double cliffShare = Math.Clamp(cfg.CliffSlopeShare, 0, 1);
        int cliffReach = Math.Max(1, (int)Math.Round(cfg.Scaled(cfg.CliffCoastReach)));

        byte[]? coastDistance = null;
        float cliffStart = float.MaxValue, cliffFull = float.MaxValue;

        if (cliffShare > 0)
        {
            var shoreMask = new byte[terrain.Length];
            Parallel.For(0, terrain.Length,
                i => shoreMask[i] = terrain[i] == TerrainClass.Sea ? (byte)0 : (byte)1);
            coastDistance = TerrainClassifier.DistanceToWater(shoreMask, pWidth, pHeight, cliffReach);

            (cliffStart, cliffFull) = CliffLines(elevation, hWidth, hHeight, sea, cliffShare,
                coastDistance, pWidth, pHeight, cliffReach);

            if (cliffStart >= float.MaxValue) coastDistance = null;
        }

        var used = new bool[256];
        object gate = new();

        {
            var index = new byte[(long)width * height * 4];
            var intensity = new byte[(long)width * height * 4];

            Parallel.For(0, height, () => new bool[256], (y, _, localUsed) =>
            {
                int srcY = height - 1 - y;
                long row = (long)y * width * 4;

                double hy = srcY * toHeightY;
                long elevRow = (long)Math.Clamp((int)hy, 0, hHeight - 1) * hWidth;

                for (int x = 0; x < width; x++)
                {
                    double hx = x * toHeightX;
                    double relief = (elevation[elevRow + Math.Clamp((int)hx, 0, hWidth - 1)] - sea)
                                    / (double)Math.Max(1, mountains - sea);

                    // Multi-scale domain warping
                    double qx = warpField.Noise2D(hx * fWarp, hy * fWarp) * 14.0
                              + broadWarp.Noise2D(hx * fBroad, hy * fBroad) * 32.0;
                    double qy = warpField.Noise2D(hx * fWarp + 17.1, hy * fWarp - 11.3) * 14.0
                              + broadWarp.Noise2D(hx * fBroad + 23.4, hy * fBroad - 41.8) * 32.0;

                    double wx = hx + qx;
                    double wy = hy + qy;

                    double nA = Math.Clamp(Field.Fbm(nAField, wx * fA, wy * fA, 3) * 0.5 + 0.5, 0, 1);
                    double nB = Math.Clamp(Field.Fbm(nBField, wx * fB + 31.7, wy * fB - 19.3, 3) * 0.5 + 0.5, 0, 1);
                    double nC = Math.Clamp(Field.Fbm(nCField, wx * fC - 11.2, wy * fC + 43.1, 2) * 0.5 + 0.5, 0, 1);

                    double gx = wx * scaleX;
                    double gy = wy * scaleY;

                    int sx = Math.Clamp((int)Math.Round(gx), 0, pWidth - 1);
                    int sy = Math.Clamp((int)Math.Round(gy), 0, pHeight - 1);
                    int pSrc = sy * pWidth + sx;

                    byte self = label[pSrc];
                    var blend = TerrainPalette.For(TerrainPalette.TerrainOf(self),
                        TerrainPalette.ClimateFromLabel(self), relief, nA, nB, nC);

                    // Bilinear continuous sample of boundary distance (eliminates discrete pixel stepping)
                    int bx0 = Math.Clamp((int)Math.Floor(gx), 0, pWidth - 1);
                    int bx1 = Math.Clamp(bx0 + 1, 0, pWidth - 1);
                    int by0 = Math.Clamp((int)Math.Floor(gy), 0, pHeight - 1);
                    int by1 = Math.Clamp(by0 + 1, 0, pHeight - 1);
                    double bfx = gx - bx0;
                    double bfy = gy - by0;

                    float d00 = boundaryDistance[by0 * pWidth + bx0];
                    float d10 = boundaryDistance[by0 * pWidth + bx1];
                    float d01 = boundaryDistance[by1 * pWidth + bx0];
                    float d11 = boundaryDistance[by1 * pWidth + bx1];

                    float smoothDist = (float)((1 - bfx) * (1 - bfy) * d00 +
                                               bfx * (1 - bfy) * d10 +
                                               (1 - bfx) * bfy * d01 +
                                               bfx * bfy * d11);

                    float edge = smoothDist * (1f / ChamferOrthogonal);

                    if (edge < blendReach)
                    {
                        double ragged = Field.Fbm(bandField, sx * bandFrequency, sy * bandFrequency, 4);
                        edge += (float)(ragged * blendReach * 0.35);

                        double interlock = Field.Fbm(interlockField, sx * interlockFrequency, sy * interlockFrequency, 2);
                        edge += (float)(interlock * blendReach * 0.14);

                        if (edge < blendReach)
                        {
                            double t = Math.Clamp(1.0 - Math.Max(0f, edge) / blendReach, 0.0, 1.0);
                            // Smoothstep curve ensures smooth zero-gradient transition
                            t = t * t * (3.0 - 2.0 * t);

                            double share = 0.5 * t * (0.78 + 0.44 * shareField.Unit(sx * fB - 88.2, sy * fB + 5.6));
                            share = Math.Clamp(share, 0.0, 0.5);

                            if (share > 0.001)
                            {
                                byte winner = boundaryOther[pSrc];
                                var neighbour = TerrainPalette.For(TerrainPalette.TerrainOf(winner),
                                    TerrainPalette.ClimateFromLabel(winner), relief, nA, nB, nC);

                                blend = TerrainPalette.Merge(blend, neighbour, share);
                            }
                        }
                    }

                    if (coastDistance is not null)
                    {
                        byte coast = coastDistance[pSrc];
                        if (coast >= 1 && coast <= cliffReach)
                        {
                            float g = Gradient(elevation, hWidth, hHeight,
                                Math.Clamp((int)hx, 0, hWidth - 1),
                                Math.Clamp((int)hy, 0, hHeight - 1));

                            if (g > cliffStart)
                            {
                                double steep = Math.Clamp(
                                    (g - cliffStart) / Math.Max(1e-4f, cliffFull - cliffStart), 0, 1);
                                steep = steep * steep * (3.0 - 2.0 * steep);

                                double inland = 1.0 - (coast - 1.0) / Math.Max(1, cliffReach - 1);
                                double share = steep * inland * 0.92;
                                if (share > 0.02)
                                {
                                    var rock = TerrainPalette.CliffFace(
                                        TerrainPalette.ClimateFromLabel(self), nA, nC);
                                    blend = TerrainPalette.Merge(blend, rock, share);
                                }
                            }
                        }
                    }

                    long o = row + x * 4;
                    // For unused slots (weight == 0), write 0 index rather than 255
                    index[o + 2] = blend.W0 > 0 && blend.M0 != TerrainPalette.Unused ? blend.M0 : (byte)0;
                    index[o + 1] = blend.W1 > 0 && blend.M1 != TerrainPalette.Unused ? blend.M1 : (byte)0;
                    index[o + 0] = blend.W2 > 0 && blend.M2 != TerrainPalette.Unused ? blend.M2 : (byte)0;
                    index[o + 3] = blend.W3 > 0 && blend.M3 != TerrainPalette.Unused ? blend.M3 : (byte)0;

                    intensity[o + 2] = blend.W0;
                    intensity[o + 1] = blend.W1;
                    intensity[o + 0] = blend.W2;
                    intensity[o + 3] = blend.W3;

                    if (blend.W0 > 0 && blend.M0 != TerrainPalette.Unused) localUsed[blend.M0] = true;
                    if (blend.W1 > 0 && blend.M1 != TerrainPalette.Unused) localUsed[blend.M1] = true;
                    if (blend.W2 > 0 && blend.M2 != TerrainPalette.Unused) localUsed[blend.M2] = true;
                    if (blend.W3 > 0 && blend.M3 != TerrainPalette.Unused) localUsed[blend.M3] = true;
                }
                return localUsed;
            }, localUsed => { lock (gate) for (int i = 0; i < 256; i++) if (localUsed[i]) used[i] = true; });

            WriteTga(Path.Combine(dir, "detail_index.tga"), width, height, index);
            WriteTga(Path.Combine(dir, "detail_intensity.tga"), width, height, intensity);
        }

        used[TerrainPalette.Unused] = false;
        used[0] = true;
        UsedMaterials = used;

        int distinct = used.Count(u => u);
        Console.WriteLine($"  terrain: detail_index + detail_intensity {width}x{height}, " +
                          $"{distinct} materials blended, band {blendReach:F0} px");

        // Bilinear continuous colormap.dds
        {
            var colormap = new byte[(long)width * height * 4];
            Parallel.For(0, height, y =>
            {
                long row = (long)y * width * 4;

                double hy = y * toHeightY;
                long elevRow = (long)Math.Clamp((int)hy, 0, hHeight - 1) * hWidth;

                double gy = hy * scaleY;
                int y0 = Math.Clamp((int)Math.Floor(gy), 0, pHeight - 1);
                int y1 = Math.Clamp(y0 + 1, 0, pHeight - 1);
                double fy = gy - y0;

                for (int x = 0; x < width; x++)
                {
                    double hx = x * toHeightX;

                    double gx = hx * scaleX;
                    int x0 = Math.Clamp((int)Math.Floor(gx), 0, pWidth - 1);
                    int x1 = Math.Clamp(x0 + 1, 0, pWidth - 1);
                    double fx = gx - x0;

                    double relief = (elevation[elevRow + Math.Clamp((int)hx, 0, hWidth - 1)] - sea)
                                    / (double)Math.Max(1, mountains - sea);
                    double nC = Selector(nCField, hx * fC - 7.3, hy * fC + 29.4);

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
            TerrainClass.Oasis => (86, 122, 70),
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