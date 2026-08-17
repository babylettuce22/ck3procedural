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
    ///
    /// A two-pass chamfer transform, so it is linear in the pixel count rather than one dilation
    /// pass per unit of reach — at a hundred-pixel reach over a 42-million-pixel province map,
    /// dilation would be some ten billion neighbour tests.
    ///
    /// Propagation is restricted to same-label neighbours on purpose. Letting it cross a boundary
    /// would carry a label from the far side back into the region it came from, and a pixel would
    /// end up blending toward its own class.
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

        // Forward scan, then backward. Sequential by nature — each pass depends on the one before.
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

    public static void WriteAll(string modDir, MapConfig cfg, TerrainClass[] terrain,
        KoppenClass[] climate, float[] elevation, Rng rng)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "terrain");
        Directory.CreateDirectory(dir);

        // Output resolution — province-sized, which is what vanilla ships. This is not a quality
        // preference, it is a hard ceiling: D3D11 caps a Texture2D at 16384 px a side (the same
        // limit HeightmapPacker.MaxTextureSide already respects). At vanilla's 18432x9216
        // heightmap, emitting these at heightmap resolution makes CreateTexture2D fail with
        // E_INVALIDARG, CK3 keeps the null pixel buffer, and the loading screen dies on an access
        // violation. Half of 18432 is 9216, which clears it; the full size never will.
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;

        // The lattice terrain[] and climate[] are indexed on.
        int pWidth = cfg.ProvinceWidth, pHeight = cfg.ProvinceHeight;

        // The lattice elevation[] is indexed on. The painting below is authored in *heightmap*
        // pixels — the noise frequencies, the warp amplitudes and the blend radius are all tuned
        // against that grid — so each output pixel maps up into heightmap space rather than the
        // whole algorithm being retuned for a second coordinate space. Output resolution and
        // sampling resolution are now independent, which is the property that was missing.
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

        // Heightmap space -> province space, applied to the warped coordinates below.
        double scaleX = (double)pWidth / hWidth;
        double scaleY = (double)pHeight / hHeight;

        // How far a biome's materials bleed across its boundary, in *province* pixels, which is the
        // space the boundary field below is measured in. Scaled so the band is the same fraction of
        // a continent at every map size.
        float blendReach = (float)Math.Max(1.0, cfg.Scaled(cfg.TerrainBlendReach));

        // The scale the band's own edge wanders at, and the scale it is dithered at. Deliberately
        // far apart: the first decides where one biome fingers into the next, which happens over
        // kilometres; the second is fine enough to break up the last few pixels so the outer edge
        // of the band is not itself a drawable contour.
        double bandFrequency = 170.0 / reference;
        double interlockFrequency = fA * 4;

        // Terrain and climate packed together, because the band has to be drawn wherever either
        // changes — see TerrainPalette.Label.
        var label = new byte[terrain.Length];
        Parallel.For(0, terrain.Length, i => label[i] = TerrainPalette.Label(terrain[i], climate[i]));

        var (boundaryDistance, boundaryOther) = BoundaryField(label, pWidth, pHeight);

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

                    // The warped coordinate decides which ground this pixel is standing on, so the
                    // class boundary itself is ragged at material scale before the band is drawn.
                    int sx = Math.Clamp((int)Math.Round(wx * scaleX), 0, pWidth - 1);
                    int sy = Math.Clamp((int)Math.Round(wy * scaleY), 0, pHeight - 1);
                    int pSrc = sy * pWidth + sx;

                    byte self = label[pSrc];
                    var blend = TerrainPalette.For(TerrainPalette.TerrainOf(self),
                        TerrainPalette.ClimateFromLabel(self), relief, nA, nB, nC);

                    // Distance from here to the nearest ground of a different class, measured
                    // inside its own region. A smooth function of a real distance is what makes a
                    // transition read as a gradient.
                    //
                    // This replaces a stencil that sampled four fixed cardinal probes at one reach
                    // and averaged them. Five probes yield a handful of possible mix strengths, so
                    // every transition was a staircase, the probes all flipped along the same
                    // contour, and with no runner-up fade below the fourth material swapped for the
                    // fifth on a clean line — which is what made biome edges read as hard seams and
                    // made a cultivated province look like a decal.
                    float edge = boundaryDistance[pSrc] * (1f / ChamferOrthogonal);
                    if (edge < blendReach)
                    {
                        // Push the band in and out along its length so it is not a uniform ribbon.
                        // Several octaves rather than one: a single frequency displaces the edge in
                        // smooth lobes a few hundred pixels across, which the eye reads as a blotch.
                        // Stacked octaves give it fingers at every scale.
                        double ragged = Field.Fbm(bandField, sx * bandFrequency, sy * bandFrequency, 4);
                        edge += (float)(ragged * blendReach * 0.35);

                        // And a fine dither on top, at texture scale, so the outer edge of the band
                        // is not itself a clean iso-line along which every material switches on at
                        // once.
                        double interlock = Field.Fbm(interlockField,
                            sx * interlockFrequency, sy * interlockFrequency, 2);
                        edge += (float)(interlock * blendReach * 0.14);

                        if (edge < blendReach)
                        {
                            double t = 1.0 - Math.Max(0f, edge) / blendReach;
                            t = t * t * (3.0 - 2.0 * t);

                            // Half at the boundary itself, falling to nothing at the far edge of the
                            // band. Half is the ceiling on purpose: at an even split the two sides
                            // are symmetric, so the seam disappears rather than reversing across one
                            // pixel.
                            double share = 0.5 * t * (0.78 + 0.44 *
                                shareField.Unit(sx * fB - 88.2, sy * fB + 5.6));

                            byte winner = boundaryOther[pSrc];
                            var neighbour = TerrainPalette.For(TerrainPalette.TerrainOf(winner),
                                TerrainPalette.ClimateFromLabel(winner), relief, nA, nB, nC);

                            blend = TerrainPalette.Merge(blend, neighbour, share);
                        }
                    }

                    long o = row + x * 4;
                    index[o + 2] = blend.M0;
                    index[o + 1] = blend.M1;
                    index[o + 0] = blend.M2;
                    index[o + 3] = blend.M3;

                    intensity[o + 2] = blend.W0;
                    intensity[o + 1] = blend.W1;
                    intensity[o + 0] = blend.W2;
                    intensity[o + 3] = blend.W3;

                    if (blend.W0 > 0) localUsed[blend.M0] = true;
                    if (blend.W1 > 0) localUsed[blend.M1] = true;
                    if (blend.W2 > 0) localUsed[blend.M2] = true;
                    if (blend.W3 > 0) localUsed[blend.M3] = true;
                }
                return localUsed;
            }, localUsed => { lock (gate) for (int i = 0; i < 256; i++) if (localUsed[i]) used[i] = true; });

            WriteTga(Path.Combine(dir, "detail_index.tga"), width, height, index);
            WriteTga(Path.Combine(dir, "detail_intensity.tga"), width, height, intensity);
        }

        used[TerrainPalette.Unused] = false;
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