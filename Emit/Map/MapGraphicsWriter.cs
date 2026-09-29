// Emit/Map/MapGraphicsWriter.cs
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class MapGraphicsWriter
{
    public static void WriteAll(string modDir, string gameDir, MapConfig cfg, ProvinceMap provinces, int[] order,
        int landCount, ClimateField climate)
    {
        WriteWaterMaps(modDir, cfg, provinces, order, landCount);
        double warmShare = WriteSnowMask(modDir, cfg, climate);
        string surround = cfg.FlatmapFeather
            ? WriteFeatheredSurroundMask(modDir, provinces, order, landCount)
            : WriteSurroundMask(modDir);

        Console.WriteLine($"  map gfx: water/foam rebuilt, snow mask from the climate ({warmShare:P0} of the map " +
                          $"too warm for snow), {surround}");

        WriteSurroundShader(modDir, gameDir, cfg);
        WriteProvinceEffectsShader(modDir, gameDir, cfg);
    }

    /// <summary>
    /// Below this coldest-month mean snow may fall; above <see cref="NoSnowFullC"/> it may not, and
    /// between the two it thins. Vanilla's hand-painted mask agrees with its winter bias to within
    /// a degree or two: London (5 °C) and Paris (4 °C) are left open, Marseille (7 °C), Rome (8 °C)
    /// and everything south are painted out. The climate model cools high ground by the lapse rate,
    /// so a range inside a warm country is left open the way vanilla leaves its high ground: 75% of
    /// vanilla's land at 15–20 world units inside the painted band is open, and 98% above 25.
    /// </summary>
    private const double NoSnowFromC = 5, NoSnowFullC = 8;

    /// <summary>The quantiles vanilla's noise channels are matched at, and vanilla's values there:
    /// G (the fine noise, 10x5 cells) and B (the coarse, 5x4 — twice as wide as tall, as vanilla's
    /// is, because a square noise was stretched onto a 2:1 texture).</summary>
    private static readonly double[] NoiseQuantiles = [0.005, 0.05, 0.25, 0.5, 0.75, 0.95, 0.995];
    private static readonly double[] FineNoise = [37, 52, 74, 91, 108, 135, 167];
    private static readonly double[] CoarseNoise = [6, 26, 55, 83, 110, 155, 207];

    /// <summary>
    /// gfx/map/textures/snow_mask.dds. The terrain shader (vanilla's gfx/FX/dynamic_masks.fxh) reads
    /// it for two unrelated jobs, and the stub this replaces — every channel 0, alpha 255 — broke
    /// both:
    /// <list type="bullet">
    /// <item><b>R</b> is geography, read once across the map at (x, 1 − y), so its rows run in file
    /// order: where snow may <i>not</i> fall. Zero let snow onto deserts and the tropics. It now
    /// comes from the climate's coldest month; see <see cref="NoSnowFromC"/>.</item>
    /// <item><b>G</b> and <b>B</b> are cloud noise, sampled five times across the map through
    /// SampleNoTile, which shifts each lookup by whole texture widths — so they must tile. They
    /// break the snow's edge up and texture it; at zero, snow came out as hard-edged bands.</item>
    /// <item><b>A</b> is 255 − B, exactly, in vanilla's file. The shader multiplies the engine's
    /// per-province winter (the "game snow", <see cref="WinterWriter"/>) by 1 − A, so 255 switched
    /// game snow off everywhere.</item>
    /// </list>
    /// The noise is the same on every world, as vanilla's one texture is; it is a pure function of
    /// the texture size and not a draw from any stream. DXT5 with mips: vanilla's own is BC7 with
    /// thirteen levels, and a tiled texture with none shimmers.
    /// </summary>
    /// <returns>The share of the texture where snow may not fall at all.</returns>
    private static double WriteSnowMask(string modDir, MapConfig cfg, ClimateField climate)
    {
        // Half the province raster, as vanilla's is, and a multiple of four for the blocks.
        int w = cfg.ProvinceWidth / 2 / 4 * 4, h = cfg.ProvinceHeight / 2 / 4 * 4;

        var noSnow = new float[w * h];
        Parallel.For(0, h, y =>
        {
            int fy = Math.Min(climate.Height - 1, (int)((y + 0.5) * climate.Height / h));
            for (int x = 0; x < w; x++)
            {
                int fx = Math.Min(climate.Width - 1, (int)((x + 0.5) * climate.Width / w));
                noSnow[y * w + x] = (float)Field.SmoothStep(NoSnowFromC, NoSnowFullC,
                    climate.ColdC[fy * climate.Width + fx]);
            }
        });
        // The lapse rate carries the terrain's own detail into the field; a light blur keeps a snow
        // line on a mountainside from stepping texel by texel.
        noSnow = Field.Blur(noSnow, w, h, 2, 2);

        var fine = MatchQuantiles(TileableNoise(w, h, 10, 5, 7, 0.55, 0x5A0E1u), FineNoise);
        var coarse = MatchQuantiles(TileableNoise(w, h, 5, 4, 7, 0.5, 0x5A0E2u), CoarseNoise);

        var bgra = new byte[w * h * 4];
        long closed = 0;
        Parallel.For(0, h, () => 0L, (y, _, count) =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, o = i * 4;
                byte r = (byte)Math.Round(Math.Clamp(noSnow[i], 0f, 1f) * 255);
                bgra[o] = coarse[i];
                bgra[o + 1] = fine[i];
                bgra[o + 2] = r;
                bgra[o + 3] = (byte)(255 - coarse[i]);
                if (r == 255) count++;
            }
            return count;
        }, count => Interlocked.Add(ref closed, count));

        string texturesDir = Path.Combine(modDir, "gfx", "map", "textures");
        Directory.CreateDirectory(texturesDir);
        DdsWriter.WriteDxt5(Path.Combine(texturesDir, "snow_mask.dds"), w, h, bgra, mips: true);
        return (double)closed / (w * h);
    }

    /// <summary>
    /// Gradient noise that wraps at the texture's edges, summed over <paramref name="octaves"/>
    /// octaves each twice as fine as the last. The lattice has <paramref name="cellsX"/> by
    /// <paramref name="cellsY"/> cells at the first octave, and each gradient is hashed from its
    /// lattice point taken modulo the lattice, so the far edge meets the near one.
    /// </summary>
    private static float[] TileableNoise(int w, int h, int cellsX, int cellsY, int octaves, double gain, uint seed)
    {
        var sum = new float[w * h];
        double amplitude = 1;
        for (int octave = 0; octave < octaves; octave++)
        {
            int cx = cellsX << octave, cy = cellsY << octave;
            var gx = new float[cx * cy];
            var gy = new float[cx * cy];
            for (int j = 0; j < cy; j++)
                for (int i = 0; i < cx; i++)
                {
                    double angle = Hash((uint)i, (uint)j, seed + (uint)octave * 101u) / 4294967296.0 * 2 * Math.PI;
                    gx[j * cx + i] = (float)Math.Cos(angle);
                    gy[j * cx + i] = (float)Math.Sin(angle);
                }

            float amp = (float)amplitude;
            Parallel.For(0, h, y =>
            {
                double fy = (y + 0.5) * cy / h;
                int y0 = (int)fy, y1 = (y0 + 1) % cy;
                float ty = (float)(fy - y0), sy = Fade(ty);
                for (int x = 0; x < w; x++)
                {
                    double fx = (x + 0.5) * cx / w;
                    int x0 = (int)fx, x1 = (x0 + 1) % cx;
                    float tx = (float)(fx - x0), sx = Fade(tx);

                    float n00 = gx[y0 * cx + x0] * tx + gy[y0 * cx + x0] * ty;
                    float n10 = gx[y0 * cx + x1] * (tx - 1) + gy[y0 * cx + x1] * ty;
                    float n01 = gx[y1 * cx + x0] * tx + gy[y1 * cx + x0] * (ty - 1);
                    float n11 = gx[y1 * cx + x1] * (tx - 1) + gy[y1 * cx + x1] * (ty - 1);
                    float top = n00 + (n10 - n00) * sx, bottom = n01 + (n11 - n01) * sx;
                    sum[y * w + x] += amp * (top + (bottom - top) * sy);
                }
            });
            amplitude *= gain;
        }
        return sum;

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    }

    private static uint Hash(uint x, uint y, uint seed)
    {
        unchecked
        {
            uint h = x * 374761393u + y * 668265263u + seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    /// <summary>
    /// <paramref name="values"/> remapped so its quantiles at <see cref="NoiseQuantiles"/> land on
    /// <paramref name="target"/>, linearly between them and along the end segments beyond, then
    /// clamped to a byte. The quantiles are read off every seventh value, which is plenty for a
    /// field this smooth.
    /// </summary>
    private static byte[] MatchQuantiles(float[] values, double[] target)
    {
        var sample = new float[(values.Length + 6) / 7];
        for (int i = 0, k = 0; i < values.Length; i += 7) sample[k++] = values[i];
        Array.Sort(sample);
        var source = new double[NoiseQuantiles.Length];
        for (int q = 0; q < source.Length; q++)
            source[q] = sample[(int)Math.Round(NoiseQuantiles[q] * (sample.Length - 1))];

        var result = new byte[values.Length];
        Parallel.For(0, values.Length / 4096 + 1, block =>
        {
            for (int i = block * 4096, end = Math.Min(values.Length, i + 4096); i < end; i++)
            {
                double v = values[i];
                int k = 0;
                while (k < source.Length - 2 && v > source[k + 1]) k++;
                double span = source[k + 1] - source[k];
                double mapped = span > 0
                    ? target[k] + (v - source[k]) * (target[k + 1] - target[k]) / span
                    : target[k];
                result[i] = (byte)Math.Round(Math.Clamp(mapped, 0, 255));
            }
        });
        return result;
    }

    /// <summary>
    /// Generates gfx/map/surround_map/surround_mask.dds to cleanly frame the generated world.
    ///
    /// Channels:
    ///   R: Edge drop-shadow / ambient vignette framing the map boundary.
    ///   G: Cloud distribution (clouds drift over the ocean perimeter without obscuring land).
    ///   B: Cutout overlay (0 = playable map, 255 = table surround). Softly faded at the absolute outer edge over water.
    /// </summary>
    private static string WriteSurroundMask(string modDir)
    {
        const int width = 1024, height = 512;

        string dir = Path.Combine(modDir, "gfx", "map", "surround_map");
        Directory.CreateDirectory(dir);

        // Pure black (RGB = 0, A = 255) ensures pdxborder.shader draws borders at 100% opacity
        var pixels = new byte[width * height * 4];
        for (long i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        DdsWriter.WriteBgra(Path.Combine(dir, "surround_mask.dds"), width, height, pixels);
        return "realistic surround mask generated";
    }

    /// <summary>
    /// The surround mask with a feathered edge: blue, which pdxterrain.shader turns into the flat
    /// map's transparency (<c>alpha = 1 - B</c>, sampled in map space with row 0 at the north),
    /// rises from nothing at the flat-map frame's outer rule to full at the sheet's edge, so the
    /// paper margin dissolves into the table.
    ///
    /// Only over open sea. Wherever land is within reach the fade is held off — fully within
    /// <see cref="CoastHold"/> of any land pixel and easing in over the next
    /// <see cref="CoastEase"/> — so a coast that runs to the edge of the map keeps its paper and
    /// nothing a player can hold is ever hidden. The fade's inner edge wanders a little on
    /// low-frequency noise so the dissolve does not read as a ruled line. Red (the surround's
    /// shadow) and green (its clouds) stay black, as before.
    ///
    /// pdxborder.shader clips political borders where B passes 0.9; that happens only in the
    /// last few pixels of open sea, where no border runs.
    /// </summary>
    private static string WriteFeatheredSurroundMask(string modDir, ProvinceMap provinces, int[] order, int landCount)
    {
        int pw = provinces.Width, ph = provinces.Height;
        int width = Math.Max(1024, pw / 2), height = Math.Max(1, (int)((long)width * ph / pw));
        double scale = pw / (double)width;                 // flat-map pixels per mask pixel
        double k = FlatmapInk.Scale(pw);
        double zone = FlatmapInk.FrameInset(pw, feather: true) - 2 * k;

        // Land at mask resolution: any land pixel in the block counts, so the hold is conservative.
        var land = new bool[width * height];
        Parallel.For(0, height, my =>
        {
            int y0 = (int)(my * scale), y1 = Math.Min(ph, (int)((my + 1) * scale) + 1);
            for (int mx = 0; mx < width; mx++)
            {
                int x0 = (int)(mx * scale), x1 = Math.Min(pw, (int)((mx + 1) * scale) + 1);
                bool any = false;
                for (int y = y0; y < y1 && !any; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int label = provinces.Label[y * pw + x];
                        if (label >= 0 && label < order.Length && order[label] <= landCount) { any = true; break; }
                    }
                land[my * width + mx] = any;
            }
        });

        // Distance to land, chamfer 3-4, in mask pixels x 3.
        const int Inf = int.MaxValue / 4;
        var d = new int[width * height];
        for (int i = 0; i < d.Length; i++) d[i] = land[i] ? 0 : Inf;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x, v = d[i];
                if (v == 0) continue;
                if (x > 0) v = Math.Min(v, d[i - 1] + 3);
                if (y > 0)
                {
                    v = Math.Min(v, d[i - width] + 3);
                    if (x > 0) v = Math.Min(v, d[i - width - 1] + 4);
                    if (x < width - 1) v = Math.Min(v, d[i - width + 1] + 4);
                }
                d[i] = v;
            }
        for (int y = height - 1; y >= 0; y--)
            for (int x = width - 1; x >= 0; x--)
            {
                int i = y * width + x, v = d[i];
                if (v == 0) continue;
                if (x < width - 1) v = Math.Min(v, d[i + 1] + 3);
                if (y < height - 1)
                {
                    v = Math.Min(v, d[i + width] + 3);
                    if (x < width - 1) v = Math.Min(v, d[i + width + 1] + 4);
                    if (x > 0) v = Math.Min(v, d[i + width - 1] + 4);
                }
                d[i] = v;
            }

        double hold = CoastHold * k, ease = CoastEase * k;
        var pixels = new byte[width * height * 4];
        long faded = 0;
        Parallel.For(0, height, my =>
        {
            for (int mx = 0; mx < width; mx++)
            {
                int i = my * width + mx, o = i * 4;
                pixels[o + 3] = 255;

                double fx = (mx + 0.5) * scale, fy = (my + 0.5) * scale;
                double edge = Math.Min(Math.Min(fx, pw - fx), Math.Min(fy, ph - fy));

                // The zone's inner edge wanders between 70 % and 100 % of its width.
                double wander = 0.7 + 0.3 * (0.5 + 0.5 * Math.Sin(fx / (97 * k) + 1.3 * Math.Sin(fy / (61 * k)))
                                                  * Math.Cos(fy / (83 * k) + 0.7 * Math.Sin(fx / (131 * k))));
                double t = 1 - Math.Clamp(edge / (zone * wander), 0, 1);
                if (t <= 0) continue;
                double fade = t * t * (3 - 2 * t);

                double toLand = d[i] >= Inf ? double.MaxValue : d[i] / 3.0 * scale;
                double open = Math.Clamp((toLand - hold) / ease, 0, 1);
                fade *= open * open * (3 - 2 * open);

                byte b = (byte)Math.Round(fade * 255);
                pixels[o] = b;
                if (b > 0) Interlocked.Increment(ref faded);
            }
        });

        string dir = Path.Combine(modDir, "gfx", "map", "surround_map");
        Directory.CreateDirectory(dir);
        DdsWriter.WriteBgra(Path.Combine(dir, "surround_mask.dds"), width, height, pixels);
        return $"surround mask feathers the flat map's edge over open sea ({faded * 100.0 / (width * height):F1} % of the mask faded)";
    }

    /// <summary>At k = 1: flat-map pixels round any land that are never faded, and the ease beyond them.</summary>
    private const double CoastHold = 24, CoastEase = 40;

    /// <summary>
    /// gfx/FX/surroundmap.shader with a depth test on its three 3D effects, so the dark shadow and
    /// cloud layers beyond the map's edge stop painting over terrain that stands above them.
    ///
    /// Vanilla draws both layers after the terrain with <c>DepthEnable = no</c>: the shadow at
    /// FLAT_MAP_HEIGHT (3.92 world units), the clouds SURROUND_MAP_CLOUDHEIGHT (5) above it, each
    /// covering everything outside the map. Edge terrain higher than the shadow is painted over for
    /// a strip (height − 3.92) / tan(view angle) deep, which reads as a ruled line cutting the hills
    /// off at the north edge. Vanilla never shows it — its heightmap tapers to about 3.9 along every
    /// border and its surround mask buries the rim under clouds — but generated worlds run mountains
    /// up to 50 units into the edge row. Tried in game on 2026-09-27: the cut is gone.
    ///
    /// What this cannot fix is the view from outside the south edge (the camera always sits south of
    /// what it looks at), where the terrain's skirt and the empty space under it show. Nothing is
    /// drawn in that space for the layers to be hidden behind; only a lower edge in the heightmap
    /// would close it.
    ///
    /// The state is the one arrow.shader gives its map overlays: test, never write. surroundmap_flat
    /// is left alone, because in the flat map the paper and the surround plane sit at the same
    /// height, and testing one against the other would only buy z-fighting where the paper feathers.
    ///
    /// Patched from the installed game at every generation rather than shipped as a copy, so a CK3
    /// update to the shader is picked up by regenerating. If Paradox reshapes the file so an anchor
    /// no longer matches, nothing ships and the game uses its own shader — the cut comes back and
    /// nothing else changes. <see cref="MapConfig.SurroundDepthTest"/> turns it off outright, for an
    /// update that breaks it in a way no anchor can see.
    /// </summary>
    private static void WriteSurroundShader(string modDir, string gameDir, MapConfig cfg)
    {
        if (!cfg.SurroundDepthTest)
        {
            Console.WriteLine("  surround depth test: off — vanilla's gfx/FX/surroundmap.shader left in place");
            return;
        }

        var patch = VanillaPatch.Open(gameDir, "surround depth test", "gfx", "FX", "surroundmap.shader");
        if (patch is null) return;

        // Declared next to the file's default, which every effect falls back to. A name of our own
        // rather than arrow.shader's depth_test_no_write, so vanilla adding one to this file later
        // cannot collide with it. The newline ends the name here too, so a sibling vanilla might
        // add called DepthStencilStateSomething does not make the header ambiguous.
        patch.InsertAfterBlock("default DepthStencilState",
            "\n\n# Procedural map: the 3D surround layers test against the terrain's depth.\n"
            + $"DepthStencilState {SurroundDepthState}\n{{\n\tDepthEnable = yes\n\tDepthWriteEnable = no\n}}",
            "DepthStencilState DepthStencilState\n");

        // The newline ends the name: "Effect surroundmap" alone is a prefix of all four effects, and
        // would land in whichever comes first if Paradox ever reordered them.
        foreach (string effect in SurroundEffects3D)
            patch.InsertAfter($"Effect {effect}", $"\n\tDepthStencilState = \"{SurroundDepthState}\"",
                $"Effect {effect}\n", "{");

        patch.Ship(modDir, bom: false);
    }

    private const string SurroundDepthState = "proctool_surround_depth_test";

    /// <summary>
    /// gfx/FX/province_effects.fxh with the effect mask read over a disc rather than one texel.
    ///
    /// A situation phase's <c>map_province_effect</c> (the Wilds' summer grass, the Great Steppe's
    /// droughts, snows and green seasons) is stored per province and looked up through the province
    /// indirection texture. Vanilla's terrain path blends the four texels around the pixel, so the
    /// effect fades over exactly one province-map pixel and ends on the border as a ruled line. That
    /// is invisible where vanilla uses it, on steppe that looks the same either side, and loud on
    /// ours, where a Wilds frontier greens a desert county next to a bare one.
    ///
    /// The replacement averages <see cref="EffectTaps"/> point lookups spread over a disc, which is
    /// the share of the neighbourhood carrying the effect: 1 deep inside, 0.5 on the border, 0 a
    /// radius out. The disc turns per pixel by a fine noise, so the steps between tap counts dither
    /// rather than band, and its radius wanders with a coarse one, so the fade is ragged along the
    /// border the way the detail-texture band is. The tree and decal path (vanilla's single lookup)
    /// gets the same treatment, so trees tint with the ground under them.
    ///
    /// Patched from the installed game like <see cref="WriteSurroundShader"/>: if either function
    /// has changed shape, nothing ships and the hard edge comes back, nothing else.
    /// </summary>
    private static void WriteProvinceEffectsShader(string modDir, string gameDir, MapConfig cfg)
    {
        if (!cfg.SoftProvinceEffects)
        {
            Console.WriteLine("  soft province effects: off — vanilla's gfx/FX/province_effects.fxh left in place");
            return;
        }

        var patch = VanillaPatch.Open(gameDir, "soft province effects", "gfx", "FX", "province_effects.fxh");
        if (patch is null) return;

        string radius = Math.Max(2.0, cfg.Scaled(EffectFadeRadius)).ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);

        patch.ReplaceBlock("BilinearSampleProvinceEffectsMask",
            "void BilinearSampleProvinceEffectsMask(",
            $$"""
            // Procedural map: effects fade over a disc of province-map pixels, not one texel.
            		static const int PROCTOOL_EFFECT_TAPS = {{EffectTaps}};
            		static const float PROCTOOL_EFFECT_RADIUS = {{radius}}f;

            		void ProctoolAddEffect( float4 Sample, float Weight, inout EffectIntensities Sum )
            		{
            			float Impact = RemapClamped( Sample.g, 0.0f, OpacityLowImpactValue, 0.0f, 0.5f );
            			Impact += RemapClamped( Sample.g, OpacityLowImpactValue, OpacityHighImpactValue, 0.0f, 0.5f );
            			Impact *= Weight;

            			Sum._Drought += ( Sample.r == DROUGHT_INDEX ) * Impact;
            			Sum._Flood += ( Sample.r == FLOOD_INDEX ) * Impact;
            			Sum._Summer += ( Sample.r == SUMMER_INDEX ) * Impact;
            			Sum._Snow += ( Sample.r == SNOW_INDEX ) * Impact;
            		}

            		void ProctoolSoftSampleProvinceEffectsMask( float2 MapCoords, inout EffectIntensities ConditionData )
            		{
            			ConditionData._Drought = 0.0f;
            			ConditionData._Flood = 0.0f;
            			ConditionData._Summer = 0.0f;
            			ConditionData._Snow = 0.0f;

            			#ifdef LOW_SPEC_SHADERS
            				return;
            			#endif

            			// Fine noise turns the disc per pixel; coarse noise swells and shrinks it.
            			float2 NoiseUV = float2( MapCoords.x * 2.0f, MapCoords.y );
            			float Spin = PdxTex2D( ProvinceEffectsNoise, NoiseUV * 900.0f ).r * 6.2831853f;
            			float Reach = PROCTOOL_EFFECT_RADIUS * ( 0.6f + 0.8f * PdxTex2D( ProvinceEffectsNoise, NoiseUV * 18.0f ).r );

            			float2 Centre = MapCoords * IndirectionMapSize;
            			float Weight = 1.0f / PROCTOOL_EFFECT_TAPS;

            			for ( int i = 0; i < PROCTOOL_EFFECT_TAPS; ++i )
            			{
            				// Golden-angle spiral: even cover of the disc at any tap count.
            				float Angle = i * 2.3999632f + Spin;
            				float Distance = sqrt( ( i + 0.5f ) / PROCTOOL_EFFECT_TAPS ) * Reach;
            				float2 Texel = floor( Centre + float2( cos( Angle ), sin( Angle ) ) * Distance );
            				float2 Pixel = ( Texel + 0.5f ) * InvIndirectionMapSize;
            				ProctoolAddEffect( SampleProvinceEffects( Pixel ), Weight, ConditionData );
            			}
            		}

            		void BilinearSampleProvinceEffectsMask( float2 MapCoords, inout EffectIntensities ConditionData )
            		{
            			ProctoolSoftSampleProvinceEffectsMask( MapCoords, ConditionData );
            		}
            """);

        patch.ReplaceBlock("SampleProvinceEffectsMask",
            "void SampleProvinceEffectsMask(",
            """
            void SampleProvinceEffectsMask( float2 MapCoords, inout EffectIntensities ConditionData )
            		{
            			ProctoolSoftSampleProvinceEffectsMask( MapCoords, ConditionData );
            		}
            """);

        patch.Ship(modDir, bom: false);
    }

    /// <summary>Radius of the effect fade, in province-map pixels of a vanilla-width map. The fade
    /// runs a radius either side of the border, so 24 makes it about as wide as the 44 px band the
    /// detail textures blend biomes over.</summary>
    private const double EffectFadeRadius = 24;

    /// <summary>Lookups per pixel. Each is one indirection fetch and one buffer read.</summary>
    private const int EffectTaps = 12;

    /// <summary>Every surround effect drawn in the 3D map — the clouds, their low-spec twin and the
    /// shadow under them. Not surroundmap_flat; see <see cref="WriteSurroundShader"/>.</summary>
    private static readonly string[] SurroundEffects3D = ["surroundmap", "surroundmapLowSpec", "surroundmap_shadow"];

    private static void WriteWaterMaps(string modDir, MapConfig cfg, ProvinceMap provinces, int[] order, int landCount)
    {
        int w = cfg.ProvinceWidth / 2, h = cfg.ProvinceHeight / 2;

        var foam = new byte[(long)w * h * 4];
        var water = new byte[(long)w * h * 4];

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int px = Math.Min(x * 2, provinces.Width - 1);
                int py = Math.Min(y * 2, provinces.Height - 1);
                bool isLand = order[provinces.Label[py * provinces.Width + px]] <= landCount;

                long o = ((long)y * w + x) * 4;

                foam[o] = foam[o + 1] = foam[o + 2] = 0;
                foam[o + 3] = 255;

                water[o] = isLand ? (byte)90 : (byte)96;
                water[o + 1] = isLand ? (byte)78 : (byte)74;
                water[o + 2] = isLand ? (byte)46 : (byte)40;
                water[o + 3] = 200;
            }
        });

        string waterDir = Path.Combine(modDir, "gfx", "map", "water");
        Directory.CreateDirectory(waterDir);
        DdsWriter.WriteBgra(Path.Combine(waterDir, "foam_map.dds"), w, h, foam);
        DdsWriter.WriteBgra(Path.Combine(waterDir, "watercolor_rgb_waterspec_a.dds"), w, h, water);
    }
}