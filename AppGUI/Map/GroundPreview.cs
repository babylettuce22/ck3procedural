using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;
using Ck3MapGen.Io;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The CK3 ground view: the written mod's terrain as the game's terrain shader paints it, from the
/// written <c>detail_index</c>/<c>detail_intensity</c>, the written colormap and the game's own
/// detail textures.
///
/// The blend is the one in vanilla's <c>pdxterrain.shader</c> (its low-spec path, which is the
/// same arithmetic without per-material UVs):
/// <list type="bullet">
/// <item>the detail maps are point-sampled at the four texel corners around a pixel, and each
///   corner's masks are added into the base texel's slots <i>only where the material index
///   matches</i>, weighted by the bilinear factors — why an unmatched neighbour makes a layer pop;</item>
/// <item>each slot's diffuse is taken at <c>tile_factor</c> repeats across the map (the material's
///   own override if it has one) and multiplied by <c>smoothstep(0, 0.1, mask)</c>;</item>
/// <item>the slots are height-blended on the diffuse alpha plus mask over <c>detail_blend_range</c>;</item>
/// <item>the colormap goes on with Pegtop soft light, weighted by <c>1 - properties.r</c>, in linear space.</item>
/// </list>
/// Unlit on purpose: the 3D view shades whatever it drapes, and a map mode is read flat. No normal
/// maps, specular, snow, trees or province effects; water is the relief view's sea.
///
/// What it is for is judging the paint — whether a biome reads as vanilla's fine mottling or as
/// flat fields and bands — without launching the game. It shows the pattern at map scale, not the
/// grain of a texture: at one pixel per detail texel a texture tile is ~27 pixels across.
/// </summary>
public static class GroundPreview
{
    /// <summary>Widest image produced. One pixel per detail texel up to this.</summary>
    private const int MaxWidth = 4096;

    private static readonly string TerrainDir = Path.Combine("gfx", "map", "terrain");

    /// <summary>
    /// Game textures downsized to a tile size, keyed by file and size. The game's textures do not
    /// change under a running program, and decoding a 1024² BC3 is the expensive step here.
    /// </summary>
    private static readonly ConcurrentDictionary<(string Path, int Size), Tile> Tiles = new();

    /// <summary>One detail texture at render scale: linear RGB, height (diffuse alpha), properties red.</summary>
    private sealed record Tile(int Size, float[] Rgb, float[] Height, float[] Props);

    private sealed record Material(string Diffuse, string Properties, double? TileFactor);

    /// <summary>
    /// How deep the sea is at a map pixel (top-down index into the detail map's own grid): 0 at the
    /// shore to 1 at the sea floor, negative on land.
    /// </summary>
    private delegate float SeaDepth(long cell);

    public static PreviewRenderer.Image Render(GenerationResult r, WrittenContent? written)
    {
        var cfg = r.Config;
        float[] elevation = r.ProvinceElevation;
        float sea = cfg.Limits.SeaLevelUpper;
        float seaFloor = cfg.SeaFloorElevation;

        return Guarded(cfg.ProvinceWidth, cfg.ProvinceHeight, () => RenderOrThrow(
            written?.ModDir ?? throw new InvalidOperationException("no written mod to read"),
            written.GameDir, elevation.Length,
            cell => elevation[cell] is var e && e <= sea
                ? Math.Clamp((sea - e) / Math.Max(1f, sea - seaFloor), 0, 1)
                : -1));
    }

    /// <summary>
    /// For a mod opened from disk, which has no elevation in memory: the sea is whatever the
    /// province map says is sea, drawn at one middling depth.
    ///
    /// A mod written without its own detail maps would otherwise be painted with vanilla's, which
    /// describe a different world, so that is refused rather than drawn.
    /// </summary>
    public static PreviewRenderer.Image Render(string modDir, string? gameDir, int width, int height, Func<long, bool> isSea)
        => Guarded(width, height, () => File.Exists(Path.Combine(modDir, TerrainDir, "detail_index.tga"))
            ? RenderOrThrow(modDir, gameDir, (long)width * height, cell => isSea(cell) ? 0.5f : -1)
            : throw new InvalidOperationException("this mod ships no terrain detail maps of its own"));

    private static PreviewRenderer.Image Guarded(int width, int height, Func<PreviewRenderer.Image> render)
    {
        try
        {
            return render();
        }
        catch (Exception ex)
        {
            // A map mode has to return an image. A flat grey one says "nothing to show" without
            // passing for some other view; the reason goes to the log.
            Console.WriteLine($"CK3 ground: {ex.Message}");
            return Blank(width, height);
        }
    }

    /// <param name="cells">How many cells <paramref name="seaDepth"/> covers; when that is not the
    /// detail map's size the two grids do not line up, and everything is drawn as land.</param>
    private static PreviewRenderer.Image RenderOrThrow(string modDir, string? gameDir, long cells, SeaDepth seaDepth)
    {
        gameDir ??= GameLocator.FindGameDir()
            ?? throw new InvalidOperationException("no CK3 game folder to take terrain textures from");

        string First(string rel)
        {
            string mod = Path.Combine(modDir, rel);
            return File.Exists(mod) ? mod : Path.Combine(gameDir, rel);
        }

        var index = ReadTga(First(Path.Combine(TerrainDir, "detail_index.tga")));
        var intensity = ReadTga(First(Path.Combine(TerrainDir, "detail_intensity.tga")));
        if (index.Width != intensity.Width || index.Height != intensity.Height)
            throw new InvalidDataException("detail_index and detail_intensity differ in size");

        var colormap = DdsReader.Load(First(Path.Combine(TerrainDir, "colormap.dds")))
            ?? throw new InvalidDataException("colormap.dds could not be read");

        var (tileDefault, blendRange) = ReadSettings(First(Path.Combine(TerrainDir, "settings.terrain")));
        var materials = ReadMaterials(First(Path.Combine(TerrainDir, "materials.settings")));

        int W = index.Width, H = index.Height;
        int step = Math.Max(1, (W + MaxWidth - 1) / MaxWidth);
        int ow = W / step, oh = H / step;

        // Every material the detail map names, at the size one tile covers in the output.
        var used = new bool[256];
        for (long i = 0; i < index.Pixels.Length; i++) used[index.Pixels[i]] = true;

        var tiles = new Tile?[256];
        var repeat = new double[256];   // texels per tile repeat, exact
        Parallel.For(0, Math.Min(256, materials.Count), m =>
        {
            if (!used[m]) return;
            double tileTexels = W / (materials[m].TileFactor ?? tileDefault);
            repeat[m] = tileTexels;
            int size = Math.Max(4, (int)Math.Round(tileTexels / step));
            tiles[m] = LoadTile(First(Path.Combine(TerrainDir, materials[m].Diffuse)),
                First(Path.Combine(TerrainDir, materials[m].Properties)), size);
        });

        bool waterKnown = cells == (long)W * H;

        var rgb = new byte[ow * oh * 3];

        Parallel.For(0, oh, oy =>
        {
            Span<int> baseIdx = stackalloc int[4];
            Span<float> mask = stackalloc float[4];
            Span<float> heights = stackalloc float[4];
            Span<float> blend = stackalloc float[4];
            Span<int> texel = stackalloc int[4];
            Span<float> dr = stackalloc float[4];
            Span<float> dg = stackalloc float[4];
            Span<float> db = stackalloc float[4];
            Span<float> dp = stackalloc float[4];

            for (int ox = 0; ox < ow; ox++)
            {
                // Pixel centre in texel space (top-down), floored and split the way the shader
                // does it: the base texel is floor(coordinate), the factors its fraction.
                double u = (ox + 0.5) * step, v = (oy + 0.5) * step;
                int bx = Math.Clamp((int)Math.Floor(u), 0, W - 2), by = Math.Clamp((int)Math.Floor(v), 0, H - 2);
                float ax = (float)Math.Clamp(u - bx, 0, 1), ay = (float)Math.Clamp(v - by, 0, 1);

                int centre = by * W + bx;
                float depth = waterKnown ? seaDepth((long)(oy * step) * W + ox * step) : -1;
                if (depth >= 0)
                {
                    int at = (oy * ow + ox) * 3;
                    rgb[at] = (byte)(38 + 26 * (1 - depth));
                    rgb[at + 1] = (byte)(70 + 44 * (1 - depth));
                    rgb[at + 2] = (byte)(104 + 48 * (1 - depth));
                    continue;
                }

                // Corners: base, +x, +y, +xy — the shader's order.
                texel[0] = centre;
                texel[1] = centre + 1;
                texel[2] = centre + W;
                texel[3] = centre + W + 1;
                float f0 = (1 - ax) * (1 - ay), f1 = ax * (1 - ay), f2 = (1 - ax) * ay, f3 = ax * ay;

                long b0 = RowOf(index, texel[0]);
                for (int s = 0; s < 4; s++)
                {
                    baseIdx[s] = index.Pixels[b0 + s];
                    mask[s] = intensity.Pixels[b0 + s] / 255f * f0;
                }

                AddMatching(index, intensity, RowOf(index, texel[1]), f1, baseIdx, mask);
                AddMatching(index, intensity, RowOf(index, texel[2]), f2, baseIdx, mask);
                AddMatching(index, intensity, RowOf(index, texel[3]), f3, baseIdx, mask);

                // Tiling runs from the bottom-left of the map, as the shader's world XZ does.
                double wu = (ox + 0.5) * step, wv = H - (oy + 0.5) * step;

                float r0 = 0, g0 = 0, bl0 = 0, props = 0;

                float best = float.MinValue;
                for (int s = 0; s < 4; s++)
                {
                    dr[s] = dg[s] = db[s] = dp[s] = heights[s] = 0;
                    var tile = baseIdx[s] < 256 ? tiles[baseIdx[s]] : null;
                    float present = SmoothStep01(mask[s] / 0.1f);
                    if (tile is not null && present > 0)
                    {
                        double tileTexels = repeat[baseIdx[s]];
                        int tu = Wrap((int)(wu / tileTexels % 1.0 * tile.Size), tile.Size);
                        int tv = Wrap((int)(wv / tileTexels % 1.0 * tile.Size), tile.Size);
                        int t = (tile.Size - 1 - tv) * tile.Size + tu;   // tile rows are top-down
                        dr[s] = tile.Rgb[t * 3] * present;
                        dg[s] = tile.Rgb[t * 3 + 1] * present;
                        db[s] = tile.Rgb[t * 3 + 2] * present;
                        dp[s] = tile.Props[t];
                        heights[s] = tile.Height[t] * present;
                    }
                    blend[s] = heights[s] + mask[s];
                    best = Math.Max(best, blend[s]);
                }

                float start = best - (float)blendRange, sum = 0;
                for (int s = 0; s < 4; s++) { blend[s] = Math.Max(blend[s] - start, 0); sum += blend[s]; }
                sum += 0.00001f;
                for (int s = 0; s < 4; s++)
                {
                    float wgt = blend[s] / sum;
                    r0 += dr[s] * wgt; g0 += dg[s] * wgt; bl0 += db[s] * wgt; props += dp[s] * wgt;
                }

                // Colormap: sampled at its own resolution, which need not be the detail map's.
                int cx = Math.Clamp((int)((ox + 0.5) * step * colormap.Width / W), 0, colormap.Width - 1);
                int cy = Math.Clamp((int)((oy + 0.5) * step * colormap.Height / H), 0, colormap.Height - 1);
                int c = (cy * colormap.Width + cx) * 4;
                float cr = ToLinear[colormap.Bgra[c + 2]], cg = ToLinear[colormap.Bgra[c + 1]], cb = ToLinear[colormap.Bgra[c]];
                float opacity = 1 - props;

                int o = (oy * ow + ox) * 3;
                rgb[o] = ToSrgb(SoftLight(r0, cr, opacity));
                rgb[o + 1] = ToSrgb(SoftLight(g0, cg, opacity));
                rgb[o + 2] = ToSrgb(SoftLight(bl0, cb, opacity));
            }
        });

        return new PreviewRenderer.Image(rgb, ow, oh);
    }

    /// <summary>
    /// Where a top-down texel's four bytes sit in a TGA payload. The writer stores rows bottom-up
    /// (origin bit clear), so image row y is file row H-1-y.
    /// </summary>
    private static long RowOf(Tga tga, int topDownTexel)
    {
        int y = topDownTexel / tga.Width, x = topDownTexel % tga.Width;
        int fileRow = tga.TopDown ? y : tga.Height - 1 - y;
        return ((long)fileRow * tga.Width + x) * 4;
    }

    private static void AddMatching(Tga index, Tga intensity, long at, float factor,
        ReadOnlySpan<int> baseIdx, Span<float> mask)
    {
        if (factor <= 0) return;
        for (int i = 0; i < 4; i++)
        {
            int m = index.Pixels[at + i];
            float w = intensity.Pixels[at + i] / 255f * factor;
            for (int j = 0; j < 4; j++)
                if (baseIdx[j] == m) mask[j] += w;
        }
    }

    private static float SoftLight(float baseC, float blendC, float opacity)
    {
        float soft = (1 - 2 * blendC) * baseC * baseC + 2 * baseC * blendC;
        return baseC + (soft - baseC) * opacity;
    }

    private static float SmoothStep01(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    private static int Wrap(int v, int n) => ((v % n) + n) % n;

    private static readonly float[] ToLinear = Enumerable.Range(0, 256).Select(i =>
    {
        double c = i / 255.0;
        return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
    }).ToArray();

    private static readonly byte[] SrgbTable = Enumerable.Range(0, 4096).Select(i =>
    {
        double c = i / 4095.0;
        double s = c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(s * 255), 0, 255);
    }).ToArray();

    private static byte ToSrgb(float linear) => SrgbTable[Math.Clamp((int)(linear * 4095 + 0.5f), 0, 4095)];

    /// <summary>
    /// A texture decoded once and box-filtered down to <paramref name="size"/>², which is what a
    /// mip level would give the GPU at this scale. Channels are averaged independently — never
    /// premultiplied by alpha, which here is height, not coverage, and is all zero on some
    /// textures (medi_farmlands).
    /// </summary>
    private static Tile LoadTile(string diffusePath, string propsPath, int size)
        => Tiles.GetOrAdd((diffusePath + "|" + propsPath, size), _ =>
        {
            var diffuse = DdsReader.Load(diffusePath) ?? throw new InvalidDataException($"cannot read {diffusePath}");
            var props = DdsReader.Load(propsPath);

            var rgb = new float[size * size * 3];
            var height = new float[size * size];
            var pr = new float[size * size];

            Box(diffuse, size, (t, b, g, r, a) =>
            {
                rgb[t * 3] = ToLinear[(int)r];
                rgb[t * 3 + 1] = ToLinear[(int)g];
                rgb[t * 3 + 2] = ToLinear[(int)b];
                height[t] = a / 255f;
            });
            if (props is { } p)
                Box(p, size, (t, _, _, r, _) => pr[t] = r / 255f);

            return new Tile(size, rgb, height, pr);
        });

    /// <summary>Area-average a BGRA image into size² cells; hands each cell's mean B, G, R, A.</summary>
    private static void Box(DdsReader.DecodedImage img, int size, Action<int, float, float, float, float> put)
    {
        for (int ty = 0; ty < size; ty++)
        {
            int y0 = ty * img.Height / size, y1 = Math.Max(y0 + 1, (ty + 1) * img.Height / size);
            for (int tx = 0; tx < size; tx++)
            {
                int x0 = tx * img.Width / size, x1 = Math.Max(x0 + 1, (tx + 1) * img.Width / size);
                double b = 0, g = 0, r = 0, a = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * img.Width;
                    for (int x = x0; x < x1; x++)
                    {
                        int at = (row + x) * 4;
                        b += img.Bgra[at]; g += img.Bgra[at + 1]; r += img.Bgra[at + 2]; a += img.Bgra[at + 3];
                    }
                }
                double n = (y1 - y0) * (x1 - x0);
                put(ty * size + tx, (float)(b / n), (float)(g / n), (float)(r / n), (float)(a / n));
            }
        }
    }

    private static (double TileFactor, double BlendRange) ReadSettings(string path)
    {
        string text = File.Exists(path) ? File.ReadAllText(path) : "";
        double Get(string key, double fallback)
        {
            var m = Regex.Match(text, $@"^\s*{key}\s*=\s*([-\d.]+)", RegexOptions.Multiline);
            return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
        }
        return (Get("detail_tile_factor", 300), Get("detail_blend_range", 0.25));
    }

    /// <summary>
    /// The material list in index order. Anchored on each <c>diffuse =</c> line rather than on the
    /// braces: the file's comments carry braces of their own. Index = order of entries (105 in
    /// vanilla, 2026-09).
    /// </summary>
    private static List<Material> ReadMaterials(string path)
    {
        string text = Regex.Replace(File.ReadAllText(path), @"#[^\n]*", "");
        var list = new List<Material>();
        foreach (Match d in Regex.Matches(text, @"^\s*diffuse\s*=", RegexOptions.Multiline))
        {
            int open = text.LastIndexOf('{', d.Index), close = text.IndexOf('}', d.Index);
            string block = text[(open + 1)..close];
            string diffuse = Regex.Match(block, "diffuse\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
            string props = Regex.Match(block, "material\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
            var tf = Regex.Match(block, @"tile_factor\s*=\s*([\d.]+)");
            list.Add(new Material(diffuse, props,
                tf.Success ? double.Parse(tf.Groups[1].Value, CultureInfo.InvariantCulture) : null));
        }
        return list;
    }

    private sealed record Tga(byte[] Pixels, int Width, int Height, bool TopDown);

    /// <summary>An uncompressed 32-bit TGA's payload, as the texture writer produces them.</summary>
    private static Tga ReadTga(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        int offset = 18 + file[0];
        if (file.Length < 18 || file[2] != 2 || file[16] != 32)
            throw new InvalidDataException($"{Path.GetFileName(path)} is not an uncompressed 32-bit TGA");

        int width = file[12] | (file[13] << 8), height = file[14] | (file[15] << 8);
        if ((long)width * height * 4 > file.Length - offset)
            throw new InvalidDataException($"{Path.GetFileName(path)} is shorter than its header says");

        var pixels = new byte[(long)width * height * 4];
        Array.Copy(file, offset, pixels, 0, pixels.Length);
        return new Tga(pixels, width, height, (file[17] & 0x20) != 0);
    }

    private static PreviewRenderer.Image Blank(int width, int height)
    {
        int w = Math.Max(1, Math.Min(width, 1024)), h = Math.Max(1, height * w / Math.Max(1, width));
        var rgb = new byte[w * h * 3];
        Array.Fill(rgb, (byte)96);
        return new PreviewRenderer.Image(rgb, w, h);
    }
}
