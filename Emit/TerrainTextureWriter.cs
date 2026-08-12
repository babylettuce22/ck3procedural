using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Paints the terrain material textures — gfx/map/terrain/detail_index.tga and
/// detail_intensity.tga — plus the colormap and flatmap.
///
/// Without these, vanilla's load. They are the right *size* at vanilla dimensions, so nothing
/// errors; they simply carry vanilla's material painting and land Anatolian scrub and Alpine rock
/// on wherever our continents happen to be.
///
/// Format, measured from vanilla rather than assumed:
///   32-bit uncompressed TGA (image type 2), province resolution, descriptor 0x08 — 8 alpha bits
///   and a **bottom-left origin**, so rows are stored bottom-up.
///   Channels are stored B,G,R,A. Four material layers blend per pixel; layer 0 is the R channel,
///   then G, then B, then A.
///
/// This used to paint one material per province, sampled at the province's seed cell, with the
/// other three layers disabled. Measured against vanilla, that gave 7 distinct materials and a
/// single layer on 100% of pixels, where vanilla blends 2-4 layers on 98.85% across ~101
/// materials — and it meant one river pixel under a seed painted a whole county as floodplains.
/// Terrain is now resolved per pixel (see <see cref="TerrainClassifier"/>) and blended by
/// <see cref="TerrainPalette"/>.
/// </summary>
public static class TerrainTextureWriter
{
    /// <summary>
    /// How far, in *vanilla* province pixels, a biome's materials bleed across its boundary into
    /// the next. Scaled by <see cref="MapConfig.Scaled"/>, so the transition is the same fraction
    /// of a continent at every map size — as a flat pixel count it was a third of a percent of the
    /// map wide at vanilla and seven times that at tiny.
    /// </summary>
    private const int BlendReachAtVanilla = 110;

    /// <summary>Orthogonal step cost in the chamfer distance transform; diagonal is 4.</summary>
    private const int ChamferOrthogonal = 3;
    private const int ChamferDiagonal = 4;

    /// <summary>
    /// What a pixel is painted from, packed into one byte: the terrain class in the low nibble and
    /// the climate family in the high one.
    ///
    /// The two are packed together because the transition band below has to be drawn around
    /// *either* changing. Once the climate picks the material family, two stretches of the same
    /// terrain class in different climates are as different on the ground as two terrain classes
    /// are — a plain running from mediterranean into central scrub swaps its whole palette — and a
    /// band that only knew about terrain classes would leave that edge as a hard seam.
    /// </summary>
    private static byte Label(TerrainClass terrain, KoppenClass zone)
        => (byte)((int)terrain | ((int)TerrainPalette.ClimateOf(zone) << 4));

    private static TerrainClass TerrainOf(byte label) => (TerrainClass)(label & 0x0F);

    private static TerrainPalette.Climate ClimateOf(byte label)
        => (TerrainPalette.Climate)(label >> 4);

    /// <summary>
    /// For every pixel: the distance to the nearest ground of a *different* label, measured
    /// without leaving its own, and which label that is.
    ///
    /// A two-pass chamfer transform, so it is linear in the pixel count rather than one dilation
    /// pass per unit of reach — at a 110-pixel reach over a 42-million-pixel province map, dilation
    /// would be some ten billion neighbour tests.
    ///
    /// Propagation is restricted to same-class neighbours on purpose. Letting it cross a boundary
    /// would carry a label from the far side back into the region it came from, and a pixel would
    /// end up blending toward its own class.
    /// </summary>
    private static (ushort[] Distance, byte[] Other) BoundaryField(
        byte[] label, int width, int height)
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

    /// <summary>
    /// Which materials the painting actually used. Indexed by material id; the mask writer needs
    /// it so it can paint exactly those and blank everything else vanilla ships.
    /// </summary>
    public static bool[] UsedMaterials { get; private set; } = new bool[256];

    public static void WriteAll(string modDir, MapConfig cfg, TerrainClass[] terrain,
        KoppenClass[] climate, float[] provinceElevation, Rng rng)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "terrain");
        Directory.CreateDirectory(dir);

        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int sea = cfg.Limits.SeaLevelUpper;
        int mountains = cfg.Limits.Mountains.Lower;

        // Three decorrelated fields. Using one noise for every choice makes the lowland variants
        // switch together, which reads as banding rather than as texture.
        var nAField = new SimplexNoise(rng);
        var nBField = new SimplexNoise(rng);
        var nCField = new SimplexNoise(rng);
        var bandField = new SimplexNoise(rng);
        var interlockField = new SimplexNoise(rng);
        var shareField = new SimplexNoise(rng);

        // Referenced to vanilla's province map, so material patches are a fixed pixel size.
        const int reference = MapConfig.ReferenceProvinceWidth;
        double fA = 55.0 / reference, fB = 130.0 / reference, fC = 300.0 / reference;

        // The transition band, and the scale at which its edge wanders. Deliberately much coarser
        // than the material noise: it decides where one biome fingers into the next, which happens
        // over kilometres, not metres. The interlock frequency is the opposite end — fine enough to
        // dither the last few pixels of the band so its outer edge is not a drawable contour.
        float blendReach = BlendReachAtVanilla;
        double bandFrequency = 170.0 / reference;
        double interlockFrequency = fA * 4;

        var label = new byte[terrain.Length];
        Parallel.For(0, terrain.Length, i => label[i] = Label(terrain[i], climate[i]));

        var (boundaryDistance, boundaryOther) = BoundaryField(label, width, height);

        // Each map-sized buffer is 162 MB at vanilla resolution, so they are allocated, written and
        // released one at a time. Holding index, intensity, colormap and flatmap simultaneously —
        // 648 MB on top of the elevation and province rasters — was enough to end the process with
        // an out-of-memory kill and no managed exception.
        var used = new bool[256];
        object gate = new();

        {
            var index = new byte[(long)width * height * 4];
            var intensity = new byte[(long)width * height * 4];

            Parallel.For(0, height, () => new bool[256], (y, _, localUsed) =>
            {
                // Bottom-left origin: the first row written is the bottom row of the image.
                int srcY = height - 1 - y;
                long row = (long)y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    var blend = BlendAt(x, srcY);
                    long o = row + x * 4;

                    // B, G, R, A — layer 0 is R, then G, then B, then A.
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
                          $"{distinct} materials blended");

        // The DDS files are top-down, unlike the bottom-up TGAs above.
        {
            var colormap = new byte[(long)width * height * 4];
            Parallel.For(0, height, y =>
            {
                long row = (long)y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int src = y * width + x;
                    double relief = (provinceElevation[src] - sea) / (double)Math.Max(1, mountains - sea);
                    double nC = Selector(nCField, x * fC - 7.3, y * fC + 29.4);

                    var (r, g, b) = GroundColor(terrain[src], relief, nC);
                    long o = row + x * 4;
                    colormap[o] = b; colormap[o + 1] = g; colormap[o + 2] = r; colormap[o + 3] = 255;
                }
            });
            DdsWriter.WriteBgra(Path.Combine(dir, "colormap.dds"), width, height, colormap);
        }

        // Vanilla ships flatmap.dds and flatmap_tgp.dds; leaving either behind puts vanilla's
        // papyrus Europe back on the zoomed-out map.
        {
            var flatmap = new byte[(long)width * height * 4];
            Parallel.For(0, height, y =>
            {
                long row = (long)y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    var (pr, pg, pb) = terrain[y * width + x] == TerrainClass.Sea
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
        return;

        TerrainPalette.Blend BlendAt(int x, int y)
        {
            int src = y * width + x;
            double relief = (provinceElevation[src] - sea) / (double)Math.Max(1, mountains - sea);

            double nA = Selector(nAField, x * fA, y * fA);
            double nB = Selector(nBField, x * fB + 41.7, y * fB - 13.1);
            double nC = Selector(nCField, x * fC - 7.3, y * fC + 29.4);

            byte self = label[src];
            var home = TerrainPalette.For(TerrainOf(self), ClimateOf(self), relief, nA, nB, nC);

            // Distance from this pixel to the nearest ground of a different class, measured inside
            // its own region, plus which class that is. A smooth function of a real distance is
            // what makes a transition read as a gradient.
            //
            // This replaced a scheme that probed three fixed reaches along a warp direction and
            // counted how many landed on a different class. Counting three probes yields four
            // possible mix strengths, so every transition was a four-step staircase — which is most
            // of why the boundaries looked splotchy — and the reach was a flat 30 pixels at every
            // map size, which at vanilla's 9216-wide province map is a transition band three
            // tenths of one percent of the map wide, i.e. invisible.
            float edge = boundaryDistance[src] * (1f / ChamferOrthogonal);
            if (edge >= blendReach) return home;

            // Push the band in and out along its length so it is not a uniform ribbon. Several
            // octaves rather than one: a single frequency displaces the edge in smooth lobes a few
            // hundred pixels across, which is a shape the eye reads as a blotch. Stacked octaves
            // give it fingers at every scale down to the material textures, which is what a biome
            // boundary does on the ground.
            double ragged = Field.Fbm(bandField, x * bandFrequency, y * bandFrequency, 4);
            edge += (float)(ragged * blendReach * 0.35);

            // And a fine dither on top, at texture scale. Without it the band still ends on a clean
            // iso-line: the outermost pixels carry a small but uniform share of the neighbouring
            // palette, so its materials all switch on together along one curve.
            double interlock = Field.Fbm(interlockField, x * interlockFrequency, y * interlockFrequency, 2);
            edge += (float)(interlock * blendReach * 0.14);

            if (edge >= blendReach) return home;
            if (edge < 0) edge = 0;

            double t = 1.0 - edge / blendReach;
            t = t * t * (3.0 - 2.0 * t);

            // Half at the boundary itself, falling to nothing at the far edge of the band. Half is
            // the ceiling on purpose: at an even split the two sides are symmetric, so the seam
            // disappears rather than reversing across one pixel.
            //
            // The strength wobbles on its own field. Modulating it by nB — the same noise that
            // picks the second lowland variant — tied how much of the neighbour showed through to
            // which texture was under it, so the two changed along the same contours and reinforced
            // each other into patches.
            double share = 0.5 * t * (0.78 + 0.44 * shareField.Unit(x * fB - 88.2, y * fB + 5.6));

            byte winner = boundaryOther[src];
            var neighbour = TerrainPalette.For(TerrainOf(winner), ClimateOf(winner), relief,
                nA, nB, nC);

            return TerrainPalette.Merge(home, neighbour, share);
        }

    }

    /// <summary>
    /// One of the fields the palette picks textures with, in 0..1.
    ///
    /// Several octaves rather than one, because the palette quantises these: a lowland variant is
    /// chosen by which quarter of the range the value falls in, and an accent by a threshold. A
    /// single octave of simplex noise has smooth, rounded contours, so a threshold on it cuts a
    /// smooth, rounded patch — a dozen or so map-scale blobs of one texture, hard-edged against the
    /// next. Folding in finer octaves leaves the same large-scale distribution but makes every
    /// contour fractal, so the same switch reads as two textures interleaving at texture scale.
    /// </summary>
    /// <remarks>
    /// The 1.5 restores the spread. Averaging three independent octaves shrinks the standard
    /// deviation to about two thirds of one octave's, and these values are read against fixed
    /// thresholds — so left alone it would quietly stop reaching the buckets at the ends of the
    /// range and drop the outer lowland variants and the rarer accents off the map entirely.
    /// </remarks>
    private static double Selector(SimplexNoise field, double x, double y)
        => Math.Clamp(Field.Fbm(field, x, y, 3) * 0.75 + 0.5, 0, 1);

    /// <summary>
    /// Ground tint per terrain class, nudged by relief and noise so the colormap is not flat
    /// inside a class — it is what the terrain reads as before the detail materials resolve.
    /// </summary>
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

        // Slight darkening with altitude and a little per-pixel variation.
        double shade = 1.0 - Math.Clamp(relief, 0, 1) * 0.12 + (n - 0.5) * 0.08;
        return ((byte)Math.Clamp(r * shade, 0, 255),
                (byte)Math.Clamp(g * shade, 0, 255),
                (byte)Math.Clamp(b * shade, 0, 255));
    }

    /// <summary>Uncompressed 32-bit TGA, bottom-left origin, matching vanilla byte for byte.</summary>
    private static void WriteTga(string path, int width, int height, byte[] bgra)
    {
        var header = new byte[18];
        header[2] = 2;                          // uncompressed true-colour
        header[12] = (byte)(width & 0xFF);
        header[13] = (byte)(width >> 8);
        header[14] = (byte)(height & 0xFF);
        header[15] = (byte)(height >> 8);
        header[16] = 32;                        // bits per pixel
        header[17] = 0x08;                      // 8 alpha bits, bottom-left origin

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            1 << 20);
        stream.Write(header);
        stream.Write(bgra);
    }
}
