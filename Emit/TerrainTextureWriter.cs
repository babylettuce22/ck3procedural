using System.Globalization;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class TerrainTextureWriter
{
    public static bool[] UsedMaterials { get; private set; } = new bool[256];

    /// <summary>
    /// Vanilla's colormap over land, measured with the land taken from its own heightmap: luminance
    /// mean 127.8, standard deviation 10.67, mean saturation 0.075. These are the three numbers
    /// <see cref="ToVanillaEnvelope"/> aims at.
    ///
    /// Raising the spread and the saturation together keeps more biome colour in the colormap at
    /// the cost of looking less like vanilla — doubling both is a reasonable middle if the fully
    /// neutral version reads as washed out. Everything the colormap gives up is picked up by the
    /// detail materials, which already carry climate-appropriate colour of their own.
    /// </summary>
    private const double ColormapTargetLuma = 127.8;
    private const double ColormapTargetSpread = 10.67;
    private const double ColormapTargetSaturation = 0.075;

    /// <summary>
    /// Frequency of the fine material-selection jitter, as cycles across a reference-width map.
    /// Works out to 4608/K province pixels per cycle at any map size, so 520 puts one cycle every
    /// ~9 px — the scale vanilla's material mottling actually runs at. Lower it for broader, more
    /// deliberate ground; raise it and the ground starts to fizz.
    /// </summary>
    private const double MaterialJitterFrequency = 520.0;

    /// <summary>
    /// How far the fine jitter may push a selector, on the selectors' own 0-1 scale. Enough to
    /// cross the palette's variant thresholds regularly; not so much that the biome-scale trend
    /// underneath stops deciding anything.
    /// </summary>
    private const double MaterialJitter = 0.0;

    /// <summary>
    /// How hard the tail selector is dithered per pixel, on the selectors' own 0-1 scale.
    ///
    /// Separate from <see cref="MaterialJitter"/> and much stronger, because the two are aimed at
    /// opposite ends of the blend. Measured on land against Clausewitz's own recompile: it changes
    /// its dominant material across 11.3% of adjacent pixel pairs and its fourth across 41.5%,
    /// where we managed 16.0% and 14.4% — a primary noisier than it should be over a tail three
    /// times too uniform. Lowering the jitter settles the primary; this dithers the tail.
    /// </summary>
    private const double TailDither = 0.0;

    /// <summary>
    /// Per-pixel dither on the two selectors that pick the lowland pair — slots 1 and 2. Weaker
    /// than <see cref="TailDither"/> because these slots carry real weight and because nA also
    /// decides the weight ordering that determines which material ends up dominant, and the
    /// dominant is currently sitting where it should. Calibrate against slot 1 at 23.1% and slot 2
    /// at 35.0% of adjacent land pairs, with slot 0 held near 11.3%.
    /// </summary>
    private const double MidDither = 0.0;

    /// <summary>
    /// How far a pixel's weights may be scattered about themselves, as a fraction. Only the
    /// weights — see <see cref="TerrainPalette.WeightJitter"/> for why that distinction is the
    /// entire problem. Calibrate against a per-pixel blend discontinuity of about 0.21, which is
    /// what Clausewitz's own recompile produces; ours sat at 0.129 with the ground in flat fields.
    /// </summary>
    private const double WeightDither = 0.30;

    /// <summary>
    /// White noise in [0,1) from a pixel's own coordinates — deliberately uncorrelated between
    /// neighbours, which is the whole point of a dither and the one thing a noise field cannot do.
    /// </summary>
    private static double Dither(int x, int y, int salt = 0)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13; h *= 0x85EBCA6B; h ^= h >> 16;
        return h / 4294967296.0;
    }

    /// <summary>
    /// Weight below which a blend slot is dropped rather than drawn, out of 255.
    ///
    /// Measured on land, vanilla leaves the fourth slot empty on 37% of its ground and fills all
    /// four on 56%; ours filled all four on 98%, because every palette entry hands Mix four
    /// non-zero weights. A permanently saturated pixel has no room: blending in a neighbouring
    /// biome or a cliff face has to *evict* a material, and an eviction is a hard swap on whatever
    /// contour it happens along. Dropping the layers too faint to see gives the transition band
    /// somewhere to go. With the contrast curve above, 10 lands on 42%/57% against vanilla's
    /// 37%/56%.
    /// </summary>
    private const int MaterialFloor = 10;

    /// <summary>
    /// Exponent sharpening the blend toward a clear primary material — see
    /// <see cref="TerrainPalette.Normalized"/>. 1.4 was picked by sweeping it against vanilla's
    /// measured land profile: it is the point past which the dominant weight keeps improving but
    /// the fourth layer collapses and the slot distribution walks away from vanilla's.
    /// </summary>
    private const double MaterialContrast = 1.4;

    /// <summary>
    /// Colormap blur radius, in pixels of a reference-width map, scaled to whatever this one is.
    /// Wide enough that a class boundary reads as a gradient at the zoom the seam showed up at,
    /// narrow enough that a small biome is still its own colour rather than its neighbours' average.
    /// </summary>
    private const double ColormapSoftening = 18.0;

    /// <summary>
    /// Width of the transition around cultivated ground, in pixels of a reference-width map,
    /// against <see cref="MapConfig.TerrainBlendReach"/>'s 44 for everything else.
    /// </summary>
    private const double FieldBlendReach = 4.0;

    /// <summary>Orthogonal step cost in the chamfer distance transform; diagonal is 4.</summary>
    private const int ChamferOrthogonal = 3;
    private const int ChamferDiagonal = 4;

    /// <summary>
    /// For every pixel: the distance to the ground of the nearest *different* label, measured
    /// without leaving its own, and which label that is — and then the same again for the nearest
    /// label that is different from *that* one.
    ///
    /// A two-pass chamfer transform, so it is linear in the pixel count rather than one dilation
    /// pass per unit of reach — at a hundred-pixel reach over a 42-million-pixel province map,
    /// dilation would be some ten billion neighbour tests.
    ///
    /// Propagation is restricted to same-label neighbours on purpose. Letting it cross a boundary
    /// would carry a label from the far side back into the region it came from, and a pixel would
    /// end up blending toward its own class.
    ///
    /// The runner-up is the whole reason this carries two labels rather than one. Keeping only the
    /// nearest makes the *identity* of the neighbour a nearest-site Voronoi partition of the band,
    /// and under a chamfer metric a Voronoi cell boundary is a dead-straight 45-degree line. The
    /// distance either side of that line is continuous, so the mix strength is too — but the
    /// material being mixed in switches from one biome's palette to another's across a single
    /// pixel, at whatever strength the band happened to be carrying. That is a hard seam running
    /// arrow-straight through the middle of otherwise uniform ground, and it aliases into a
    /// staircase because nothing anti-aliases it. Blending toward both labels, weighted by their
    /// two distances, makes the switch continuous: on the line itself the two are equidistant and
    /// contribute equally, so there is nothing left to draw.
    /// </summary>
    private static (ushort[] Distance, byte[] Other, ushort[] Distance2, byte[] Other2)
        BoundaryField(byte[] label, int width, int height)
    {
        int n = width * height;
        var distance = new ushort[n];
        var other = new byte[n];
        var distance2 = new ushort[n];
        var other2 = new byte[n];
        const ushort Far = ushort.MaxValue;

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                byte self = label[i];
                distance[i] = Far;
                distance2[i] = Far;
                other[i] = self;
                other2[i] = self;

                // Seed *every* distinct label this pixel touches, not the first one found. A pixel
                // sitting where three regions meet is exactly the case the runner-up exists for,
                // and it is also the case that starts the straight line.
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

                        Offer(i, 0, neighbour);
                    }
                }
            }
        });

        // Forward scan, then backward. Sequential by nature — each pass depends on the one before.
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                Relax(i, x - 1, y, ChamferOrthogonal);
                Relax(i, x - 1, y - 1, ChamferDiagonal);
                Relax(i, x, y - 1, ChamferOrthogonal);
                Relax(i, x + 1, y - 1, ChamferDiagonal);
            }

        for (int y = height - 1; y >= 0; y--)
            for (int x = width - 1; x >= 0; x--)
            {
                int i = y * width + x;
                Relax(i, x + 1, y, ChamferOrthogonal);
                Relax(i, x + 1, y + 1, ChamferDiagonal);
                Relax(i, x, y + 1, ChamferOrthogonal);
                Relax(i, x - 1, y + 1, ChamferDiagonal);
            }

        return (distance, other, distance2, other2);

        // Fold one (distance, label) candidate into a pixel's best two, keeping the two labels
        // distinct. A repeat of a label already held only ever lowers that label's distance — it
        // must never be allowed to take both slots, or the runner-up is lost and the seam is back.
        void Offer(int i, int candidate, byte lab)
        {
            if (candidate >= distance2[i] && lab != other[i]) return;

            if (lab == other[i])
            {
                if (candidate < distance[i]) distance[i] = (ushort)candidate;
                return;
            }

            if (candidate < distance[i])
            {
                // The old winner is still the best *other* label, so it slides into the runner-up.
                distance2[i] = distance[i];
                other2[i] = other[i];
                distance[i] = (ushort)candidate;
                other[i] = lab;
                return;
            }

            if (candidate < distance2[i])
            {
                distance2[i] = (ushort)candidate;
                other2[i] = lab;
            }
        }

        void Relax(int target, int x, int y, int cost)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;

            int from = y * width + x;
            if (label[from] != label[target]) return;

            if (distance[from] != Far) Offer(target, distance[from] + cost, other[from]);
            if (distance2[from] != Far) Offer(target, distance2[from] + cost, other2[from]);
        }
    }


    /// <summary>
    /// Centre tap weight in <see cref="Reconcile"/>, relative to each of the eight neighbours.
    /// High enough that a texel keeps its own character, low enough that the set it ends up with
    /// is one its neighbours agree about.
    /// </summary>
    private const float ReconcileCentre = 4.0f;

    /// <summary>
    /// Re-pick every texel's four materials from a vote over its 3x3 neighbourhood, so that
    /// neighbouring texels almost always agree about which materials are present.
    ///
    /// This is aimed squarely at the one quantity CK3's terrain shader is unforgiving about.
    /// CalculateDetails point-samples detail_index across a 2x2 and folds each neighbour's mask
    /// into the centre's slots *only where the material index matches*; weight held in a material
    /// the centre does not carry is discarded outright, and a slot left under 0.1 is then zeroed
    /// by a smoothstep, so the layer disappears and the texel pops away from its neighbours.
    ///
    /// Measured against a Clausewitz recompile of the same map, the editor loses a mean of 2.28 of
    /// 255 to unmatched neighbours and 13 at the 95th percentile. Ours lost 10.35 and 70 — five
    /// times the mean, sixteen times the tail — and that is what the stair-stepping is. It is not a
    /// weight problem: the jump in weight between neighbours that *do* share a material is
    /// comparable between us and the editor (9.25 against 6.79). It is purely set membership.
    ///
    /// Voting fixes it structurally rather than case by case. The palette picks materials by
    /// thresholding noise selectors, and any threshold swaps a material at whatever weight it
    /// happened to be carrying; there are a dozen such picks and fading each one individually kept
    /// missing some. A neighbourhood vote makes the set a function of a smoothed field instead, so
    /// it turns over one member at a time and the departing member is always the faintest. On the
    /// test crop this took unmatched weight from 10.35/70 to 3.52/20, and the hard rectangular
    /// edges in the simulated render became smooth curves.
    /// </summary>
    private static void Reconcile(byte[] index, byte[] intensity, int width, int height)
    {
        var srcIndex = (byte[])index.Clone();
        var srcWeight = (byte[])intensity.Clone();

        Parallel.For(0, height, y =>
        {
            Span<byte> mats = stackalloc byte[36];
            Span<float> wts = stackalloc float[36];

            for (int x = 0; x < width; x++)
            {
                int n = 0;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= height) continue;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= width) continue;

                        float k = dx == 0 && dy == 0 ? ReconcileCentre : 1f;
                        long o = ((long)yy * width + xx) * 4;

                        for (int slot = 0; slot < 4; slot++)
                        {
                            byte m = srcIndex[o + slot];
                            byte w = srcWeight[o + slot];
                            if (m == TerrainPalette.Unused || w == 0) continue;

                            int at = -1;
                            for (int i = 0; i < n; i++)
                                if (mats[i] == m) { at = i; break; }

                            if (at >= 0) wts[at] += w * k;
                            else if (n < mats.Length) { mats[n] = m; wts[n] = w * k; n++; }
                        }
                    }
                }

                long dst = ((long)y * width + x) * 4;

                if (n == 0)
                {
                    for (int slot = 0; slot < 4; slot++)
                    {
                        index[dst + slot] = TerrainPalette.Unused;
                        intensity[dst + slot] = 0;
                    }
                    continue;
                }

                // Four heaviest, by selection — n is small and this avoids sorting the whole set.
                Span<int> pick = stackalloc int[4];
                int kept = 0;
                for (int slot = 0; slot < 4 && slot < n; slot++)
                {
                    int best = -1;
                    float bestWeight = 0;
                    for (int i = 0; i < n; i++)
                    {
                        bool taken = false;
                        for (int j = 0; j < kept; j++) if (pick[j] == i) { taken = true; break; }
                        if (taken || wts[i] <= bestWeight) continue;
                        bestWeight = wts[i];
                        best = i;
                    }
                    if (best < 0) break;
                    pick[kept++] = best;
                }

                float total = 0;
                for (int i = 0; i < kept; i++) total += wts[pick[i]];

                // Largest remainder, so the four still sum to exactly 255 as vanilla's always do.
                Span<int> scaled = stackalloc int[4];
                Span<float> remainder = stackalloc float[4];
                int assigned = 0;

                for (int i = 0; i < kept; i++)
                {
                    float exact = wts[pick[i]] * 255f / MathF.Max(total, 1e-6f);
                    scaled[i] = (int)exact;
                    remainder[i] = exact - scaled[i];
                    assigned += scaled[i];
                }

                for (int give = 255 - assigned; give > 0; give--)
                {
                    int best = 0;
                    for (int i = 1; i < kept; i++)
                        if (remainder[i] > remainder[best]) best = i;
                    scaled[best]++;
                    remainder[best] = -1;
                }

                for (int slot = 0; slot < 4; slot++)
                {
                    bool live = slot < kept && scaled[slot] > 0;
                    index[dst + slot] = live ? mats[pick[slot]] : TerrainPalette.Unused;
                    intensity[dst + slot] = live ? (byte)Math.Clamp(scaled[slot], 0, 255) : (byte)0;
                }
            }
        });
    }

    /// <summary>
    /// settings.terrain, sized for this map instead of inherited from vanilla's.
    ///
    /// Two of vanilla's seven values are statements about vanilla's own pixel dimensions, and its
    /// own comments say so: detail_tile_factor was raised from 300 to 337.5 "when map was expanded
    /// by 12.5% for Asia, to keep tiling at same size", and detail_tile_offset_y is -512 purely to
    /// keep the pre-expansion tiling where it was. Shipping no settings.terrain at all inherits
    /// both, so a map half vanilla's width drew the ground texture at half its intended physical
    /// size — twice as many repeats across the same stretch of ground — and slid the whole tiling
    /// by a quarter of the map's height rather than an eighth. The rest are physical constants of
    /// the renderer and carry over as they are.
    /// </summary>
    private static void WriteTerrainSettings(string dir, MapConfig cfg)
    {
        double tileFactor = 337.5 * cfg.ProvinceWidth / MapConfig.ReferenceProvinceWidth;

        ParadoxText.WriteNoBom(Path.Combine(dir, "settings.terrain"),
            $"""
             detail_blend_range = 0.25
             detail_tile_factor = {tileFactor.ToString("0.###", CultureInfo.InvariantCulture)}
             detail_tile_offset_x = 0
             detail_tile_offset_y = 0
             normal_height_scale = 0.8
             skirt_height_factor = 0.1
             normal_step_size = 1.6

             """);
    }

    /// <summary>
    /// Soften the colormap into the continuous tint CK3 expects, by blurring it across land.
    ///
    /// The colormap is sampled from terrain[], which is a *class index* — piecewise constant, one
    /// flat colour per class. Straight out of the lookup it is a poster: measured against vanilla,
    /// 833 distinct colours over the whole map where vanilla has 15,272, a third of all adjacent
    /// pixels exactly equal, and nearly five times as many hard luminance steps. Every terrain
    /// class boundary arrives as a full-strength tonal cliff with nothing anti-aliasing it, which
    /// is what reads in game as a stair-stepped seam running through otherwise uniform ground —
    /// the detail textures either side of it are the same, only the tint jumps.
    ///
    /// Blurred over land only. Letting sea bleed inland would put a halo along every shore, and
    /// the sea's own colormap sits under the water plane where nothing can see it anyway.
    /// </summary>
    private static void SmoothColormap(byte[] colormap, bool[] land, int width, int height, int radius)
    {
        int n = width * height;

        // Separable Gaussian, normalised. Radius is in this map's own pixels, so the softening is
        // the same fraction of a continent whatever size the map was generated at.
        int taps = radius * 2 + 1;
        var kernel = new float[taps];
        double sigma = Math.Max(0.5, radius / 2.0);
        double sum = 0;

        for (int i = 0; i < taps; i++)
        {
            double d = i - radius;
            kernel[i] = (float)Math.Exp(-(d * d) / (2 * sigma * sigma));
            sum += kernel[i];
        }
        for (int i = 0; i < taps; i++) kernel[i] /= (float)sum;

        var weight = new float[n];
        for (int i = 0; i < n; i++) weight[i] = land[i] ? 1f : 0f;

        var blurredWeight = (float[])weight.Clone();
        Blur(blurredWeight, width, height, kernel, radius);

        var channel = new float[n];

        // One channel at a time: three of these plus the weights live at once either way, and
        // holding all of them as separate blur targets is a quarter of a gigabyte on a large map.
        for (int c = 0; c < 3; c++)
        {
            for (int i = 0; i < n; i++) channel[i] = colormap[(long)i * 4 + c] * weight[i];
            Blur(channel, width, height, kernel, radius);

            for (int i = 0; i < n; i++)
            {
                if (!land[i] || blurredWeight[i] <= 1e-6f) continue;
                colormap[(long)i * 4 + c] =
                    (byte)Math.Clamp((int)Math.Round(channel[i] / blurredWeight[i]), 0, 255);
            }
        }
    }

    /// <summary>
    /// Pull the colormap down onto vanilla's tonal envelope: near-neutral, and barely varying.
    ///
    /// This is the difference that dwarfed every other one. Vanilla's colormap over land has a
    /// luminance mean of 128.5 with a standard deviation of 7.97 and a mean saturation of 0.045 —
    /// it is a faint grey wash that *tints* detail textures which already carry the colour. Ours
    /// was mean 100.3, deviation 40.7, saturation 0.370: five times the tonal range and eight times
    /// the saturation, painting the map's colour outright. At that amplitude every structure in the
    /// layer is loud, which is why boundaries in it stayed visible however smooth they were made.
    /// Vanilla could have hard edges here and they would be hard to see.
    ///
    /// Measured off the map being generated rather than hard-coded, so a map with a different
    /// biome mix lands on the same envelope instead of on whatever these constants suited once.
    /// </summary>
    private static void ToVanillaEnvelope(byte[] colormap, bool[] land, int width, int height)
    {
        int n = width * height;

        double sumL = 0, sumLL = 0, sumSat = 0;
        long count = 0;

        for (int i = 0; i < n; i++)
        {
            if (!land[i]) continue;
            long o = (long)i * 4;
            double b = colormap[o], g = colormap[o + 1], r = colormap[o + 2];

            double lum = (r + g + b) / 3.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));

            sumL += lum;
            sumLL += lum * lum;
            sumSat += max > 0 ? (max - min) / max : 0;
            count++;
        }

        if (count == 0) return;

        double mean = sumL / count;
        double spread = Math.Sqrt(Math.Max(0, sumLL / count - mean * mean));
        double saturation = sumSat / count;

        // Only ever compress. A map that already sits inside vanilla's envelope is left alone
        // rather than being stretched out to fill it.
        double kSat = saturation > 1e-6 ? Math.Min(1.0, ColormapTargetSaturation / saturation) : 1.0;
        double kLum = spread > 1e-6 ? Math.Min(1.0, ColormapTargetSpread / spread) : 1.0;

        for (int i = 0; i < n; i++)
        {
            if (!land[i]) continue;
            long o = (long)i * 4;
            double b = colormap[o], g = colormap[o + 1], r = colormap[o + 2];

            double lum = (r + g + b) / 3.0;

            // Toward grey first, then the surviving spread compressed and recentred.
            r = lum + (r - lum) * kSat;
            g = lum + (g - lum) * kSat;
            b = lum + (b - lum) * kSat;

            double target = ColormapTargetLuma + (lum - mean) * kLum;
            double shift = target - lum;

            colormap[o] = (byte)Math.Clamp((int)Math.Round(b + shift), 0, 255);
            colormap[o + 1] = (byte)Math.Clamp((int)Math.Round(g + shift), 0, 255);
            colormap[o + 2] = (byte)Math.Clamp((int)Math.Round(r + shift), 0, 255);
        }
    }

    /// <summary>Separable convolution with an odd, pre-normalised kernel. Edges clamp.</summary>
    private static void Blur(float[] data, int width, int height, float[] kernel, int radius)
    {
        var temp = new float[data.Length];

        Parallel.For(0, height, y =>
        {
            long row = (long)y * width;
            for (int x = 0; x < width; x++)
            {
                float acc = 0;
                for (int k = -radius; k <= radius; k++)
                    acc += data[row + Math.Clamp(x + k, 0, width - 1)] * kernel[k + radius];
                temp[row + x] = acc;
            }
        });

        Parallel.For(0, height, y =>
        {
            long row = (long)y * width;
            for (int x = 0; x < width; x++)
            {
                float acc = 0;
                for (int k = -radius; k <= radius; k++)
                    acc += temp[(long)Math.Clamp(y + k, 0, height - 1) * width + x] * kernel[k + radius];
                data[row + x] = acc;
            }
        });
    }

    /// <summary>
    /// Smoothstepped band falloff: 1 hard against the boundary, 0 at <paramref name="reach"/> and
    /// beyond. Clamped at both ends so a runner-up that is nowhere near simply contributes nothing.
    /// </summary>
    private static double Falloff(float edge, float reach)
    {
        double t = 1.0 - Math.Max(0f, edge) / reach;
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return t * t * (3.0 - 2.0 * t);
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

    /// <summary>
    /// Two gradient thresholds off this map's own coast: where cliff rock starts showing, and where
    /// it has taken the face over completely.
    ///
    /// Percentiles rather than an absolute rise-over-run, for the reason the hill and mountain
    /// lines are percentiles — the raw elevation scale depends on how far the tectonic sim ran, so
    /// a fixed gradient marks a different fraction of every map.
    ///
    /// Measured over the *coastal band only*, not over all land, because that band is the only
    /// place the result is applied. Taken over all land the share would be diluted by every inland
    /// mountain face — which is the steep ground on most maps — and the fraction actually landing
    /// on a shore would swing with how mountainous the interior happened to come out. Over the band
    /// itself the knob means what it says: this share of coastal ground reads as cliff.
    ///
    /// Sampled on a stride. A heightmap is 170 million pixels at vanilla size and a percentile does
    /// not need all of them, only an unbiased sample.
    /// </summary>
    private static (float Start, float Full) CliffLines(float[] elevation, int width, int height,
        int sea, double share, byte[] coastDistance, int pWidth, int pHeight, int reach)
    {
        const int Stride = 4;
        const int Bins = 2048;

        double toProvinceX = (double)pWidth / width;
        double toProvinceY = (double)pHeight / height;

        // Land, and inside the band the cliff can actually be drawn in. Both tests are needed:
        // coastDistance is province resolution, so its cell can read land while this particular
        // heightmap pixel inside it is under water.
        bool Sampled(int x, int y)
        {
            if (elevation[(long)y * width + x] <= sea) return false;

            int px = Math.Clamp((int)(x * toProvinceX), 0, pWidth - 1);
            int py = Math.Clamp((int)(y * toProvinceY), 0, pHeight - 1);
            byte d = coastDistance[(long)py * pWidth + px];
            return d >= 1 && d <= reach;
        }

        int rows = (height + Stride - 1) / Stride;

        // One pass to find the range, one to bin it. Both on the same stride, so they see the
        // same sample.
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

        // The band the cliff fades in over. A single threshold would put a hard iso-gradient line
        // around every cliff, which is the same failure the biome band was built to avoid.
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

        WriteTerrainSettings(dir, cfg);

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

        // The same two fields the tree scatter and the steppe regimes read, so painted canopy and
        // planted canopy thin out together and a steppe regime covers the same ground in both.
        var canopyField = CanopyField.Create(cfg);
        var zoneField = ZoneField.Create(cfg);

        var nAField = new SimplexNoise(rng);
        var nBField = new SimplexNoise(rng);
        var nCField = new SimplexNoise(rng);
        var warpField = new SimplexNoise(rng);
        var broadWarp = new SimplexNoise(rng);
        var bandField = new SimplexNoise(rng);
        var interlockField = new SimplexNoise(rng);
        var shareField = new SimplexNoise(rng);
        var fineField = new SimplexNoise(rng);

        const int reference = MapConfig.ReferenceProvinceWidth;
        double fA = 45.0 / reference, fB = 110.0 / reference, fC = 260.0 / reference;

        // These constants are cycles-per-map-width expressed against a reference-width map, and
        // because the heightmap is always twice the province raster they work out to a fixed
        // 4608/K province pixels per cycle whatever size the map is: 102 px for fA, 42 for fB,
        // 18 for fC. Measured against vanilla, that is three to eight times too coarse. Vanilla's
        // dominant material changes between 8.7% of adjacent pixel pairs and a 32x32 window holds
        // four different ones; ours changed across 2.6% and held a median of *one*, which is to say
        // the ground was painted in large flat fields. Two flat fields meeting still read as a seam
        // however well the boundary between them is blended — a soft band between two flat colours
        // is still a band. Vanilla has no seams to soften because it has no flat fields.
        double fFine = MaterialJitterFrequency / reference;
        double fWarp = 20.0 / reference;
        double fBroad = 6.0 / reference;

        // Heightmap space -> province space, applied to the warped coordinates below.
        double scaleX = (double)pWidth / hWidth;
        double scaleY = (double)pHeight / hHeight;

        // How far a biome's materials bleed across its boundary, in *province* pixels, which is the
        // space the boundary field below is measured in. Scaled so the band is the same fraction of
        // a continent at every map size.
        float blendReach = (float)Math.Max(1.0, cfg.Scaled(cfg.TerrainBlendReach));

        // How wide a farmland edge is allowed to be. Narrow enough to read as a surveyed line,
        // but not zero: a one-texel step would alias into the same staircase every other fix in
        // this file exists to remove, because CK3 point-samples detail_index and cannot smooth a
        // hard index change. Two pixels of weight ramp keeps both materials present across the
        // seam, so the edge is crisp without being jagged.
        float fieldReach = (float)Math.Max(1.0, cfg.Scaled(FieldBlendReach));

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

        var (boundaryDistance, boundaryOther, boundaryDistance2, boundaryOther2) =
            BoundaryField(label, pWidth, pHeight);

        // Coastal cliffs, the one thing on this map that is a property of the *slope* rather than
        // of the height. Everything above resolves from a scalar elevation against a percentile,
        // which cannot tell a sea cliff from the beach at its foot — both sit at the shore and both
        // are below the hill line, so a shore that climbs two hundred metres in five pixels was
        // getting beach_02 at weight 160 painted straight up its face.
        double cliffShare = Math.Clamp(cfg.CliffSlopeShare, 0, 1);
        int cliffReach = Math.Max(1, (int)Math.Round(cfg.Scaled(cfg.CliffCoastReach)));

        // Distance to open water at province resolution, so the sea-cliff texture stays at the sea.
        // Built first because the gradient thresholds below are percentiles *of this band*.
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

            // No coastline steep enough to measure — an all-flat or all-water map.
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

                    double canopyDensity = CanopyField.At(canopyField, x, y);
                    double zoneA = ZoneField.Primary(zoneField, x, y);
                    double zoneB = ZoneField.Secondary(zoneField, x, y);

                    double nA = Math.Clamp(Field.Fbm(nAField, wx * fA, wy * fA, 3) * 0.5 + 0.5, 0, 1);
                    double nB = Math.Clamp(Field.Fbm(nBField, wx * fB + 31.7, wy * fB - 19.3, 3) * 0.5 + 0.5, 0, 1);
                    double nC = Math.Clamp(Field.Fbm(nCField, wx * fC - 11.2, wy * fC + 43.1, 2) * 0.5 + 0.5, 0, 1);

                    // Added rather than mixed in, so the biome-scale trend these three carry is
                    // untouched and only wobbles about itself at texture scale. The wobble is what
                    // walks the selectors back and forth across the thresholds inside the palette,
                    // so which lowland pair, which accent and which hill rock a pixel draws changes
                    // every few pixels instead of every hundred. It reaches the *weights* as well
                    // as the choices, which is wanted: vanilla's ground is dithered at this scale
                    // too, and that dither is most of why its biome edges have nothing to draw.
                    // Both a smooth wobble and a per-pixel dither. The wobble keeps the biome-scale
                    // trend; the dither is what actually stipples slots 1 and 2, which come from
                    // LowlandPair(nA, nB). Lowering MaterialJitter alone settled the primary but
                    // starved those two — measured against Clausewitz's recompile, slot 1 fell to
                    // 16.8% against its 23.1% and slot 2 to 24.9% against its 35.0%, and the count
                    // of distinct index triples per 4x4 of land dropped with them. The smooth term
                    // cannot supply this: neighbouring pixels share a noise value almost exactly,
                    // and it is precisely the *disagreement* between neighbours that the shader's
                    // index-matching accumulation needs.
                    nA = Math.Clamp(nA + Field.Fbm(fineField, wx * fFine + 5.5, wy * fFine - 3.1, 2) * MaterialJitter
                                       + (Dither(x, srcY, 1) - 0.5) * MidDither, 0, 1);
                    nB = Math.Clamp(nB + Field.Fbm(fineField, wx * fFine - 61.7, wy * fFine + 44.9, 2) * MaterialJitter
                                       + (Dither(x, srcY, 2) - 0.5) * MidDither, 0, 1);

                    // nC is the *tail* selector — it picks the accent and the hill rock, which land
                    // in slots 2 and 3 — and it is dithered per pixel rather than over a noise field
                    // like the two above. Measured against a detail_index Clausewitz recompiled from
                    // our own masks, its fourth slot changes across 41.5% of adjacent land pixel
                    // pairs where ours changed across 14.4%: it lays a stable primary under a
                    // heavily stippled tail. That stipple is not decoration. The terrain shader
                    // reads detail_index with a point sampler and accumulates each neighbouring
                    // texel's mask weight only into slots whose *material index matches*, so
                    // neighbours that share no tail index contribute nothing to each other and the
                    // transition between them is a hard switch at texel resolution — which is the
                    // staircase. A tail that changes every pixel keeps enough indices shared
                    // between neighbours for the accumulation to average out instead.
                    nC = Math.Clamp(nC + (Dither(x, srcY, 3) - 0.5) * TailDither, 0, 1);

                    // The warped coordinate decides which ground this pixel is standing on, so the
                    // class boundary itself is ragged at material scale before the band is drawn.
                    int sx = Math.Clamp((int)Math.Round(wx * scaleX), 0, pWidth - 1);
                    int sy = Math.Clamp((int)Math.Round(wy * scaleY), 0, pHeight - 1);
                    int pSrc = sy * pWidth + sx;

                    byte self = label[pSrc];
                    var blend = TerrainPalette.For(TerrainPalette.TerrainOf(self),
                        TerrainPalette.ClimateFromLabel(self), relief, nA, nB, nC,
                        canopyDensity, zoneA, zoneB);

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
                    // Fields are surveyed, not grown. Every other boundary on the map is a
                    // gradual thing and is drawn as one, but a farmland edge is a property line —
                    // it should sit on the county boundary and read as a straight break in the
                    // ground, the way it does from the air. So it gets a band a couple of pixels
                    // wide instead of the biome band, and skips the noise that deliberately makes
                    // every other boundary wander off its true line.
                    bool fieldEdge =
                        TerrainPalette.TerrainOf(self) == TerrainClass.Farmlands ||
                        TerrainPalette.TerrainOf(boundaryOther[pSrc]) == TerrainClass.Farmlands;

                    float reach = fieldEdge ? fieldReach : blendReach;

                    float edge = boundaryDistance[pSrc] * (1f / ChamferOrthogonal);
                    if (edge < reach)
                    {
                        // Push the band in and out along its length so it is not a uniform ribbon.
                        // Several octaves rather than one: a single frequency displaces the edge in
                        // smooth lobes a few hundred pixels across, which the eye reads as a blotch.
                        // Stacked octaves give it fingers at every scale.
                        if (!fieldEdge)
                        {
                            double ragged = Field.Fbm(bandField, sx * bandFrequency, sy * bandFrequency, 4);
                            edge += (float)(ragged * blendReach * 0.35);

                        // And a fine dither on top, at texture scale, so the outer edge of the band
                        // is not itself a clean iso-line along which every material switches on at
                        // once.
                            double interlock = Field.Fbm(interlockField,
                                sx * interlockFrequency, sy * interlockFrequency, 2);
                            edge += (float)(interlock * blendReach * 0.14);
                        }

                        if (edge < reach)
                        {
                            // The runner-up rides the same displacement as the winner. Displacing
                            // them independently would put a step back in: the two would cross at a
                            // different place than their true distances say, and the crossing is
                            // the whole thing being smoothed here.
                            float edge2 = boundaryDistance2[pSrc] * (1f / ChamferOrthogonal)
                                        + (edge - boundaryDistance[pSrc] * (1f / ChamferOrthogonal));

                            double t1 = Falloff(edge, reach);
                            double t2 = Falloff(edge2, reach);
                            double sum = t1 + t2;

                            if (sum > 0)
                            {
                                // Band strength is still the nearest label's alone, so the band is
                                // exactly as wide and as strong as it was. Only the split between
                                // the two neighbours is new.
                                double t = Math.Max(t1, t2);

                                // Half at the boundary itself, falling to nothing at the far edge of
                                // the band. Half is the ceiling on purpose: at an even split the two
                                // sides are symmetric, so the seam disappears rather than reversing
                                // across one pixel.
                                // Clamped, because the jitter below could push it past the half it
                                // is capped at. Unit returns 0..1, so the multiplier ran 0.78 to
                                // 1.22 and the share reached 0.61 — and above a half the
                                // neighbour's palette outweighs this pixel's own, so the dominant
                                // material reverses on *both* sides of the line and back again.
                                // That is the seam the ceiling exists to prevent, and it had been
                                // firing wherever this noise field happened to run high.
                                double share = Math.Min(0.5, 0.5 * t * (0.78 + 0.44 *
                                    shareField.Unit(sx * fB - 88.2, sy * fB + 5.6)));

                                // Merge is a two-way lerp, so folding in two neighbours in sequence
                                // needs the first one's share pre-divided by what the second will
                                // take off it. Solving Merge(Merge(self, n1, a), n2, b) for the
                                // intended (1-share)*self + share*(f1*n1 + f2*n2) gives these.
                                double f2 = t2 / sum;
                                double b = share * f2;
                                double a = share * (t1 / sum) / Math.Max(1e-6, 1.0 - b);

                                byte winner = boundaryOther[pSrc];
                                var neighbour = TerrainPalette.For(TerrainPalette.TerrainOf(winner),
                                    TerrainPalette.ClimateFromLabel(winner), relief, nA, nB, nC,
                                    canopyDensity, zoneA, zoneB);

                                blend = TerrainPalette.Merge(blend, neighbour, a);

                                if (b > 0.002)
                                {
                                    byte second = boundaryOther2[pSrc];
                                    var runnerUp = TerrainPalette.For(
                                        TerrainPalette.TerrainOf(second),
                                        TerrainPalette.ClimateFromLabel(second), relief, nA, nB, nC,
                                        canopyDensity, zoneA, zoneB);

                                    blend = TerrainPalette.Merge(blend, runnerUp, b);
                                }
                            }
                        }
                    }

                    // The gradient is read at the *unwarped* position on purpose. Everything else
                    // in this loop samples through the warp, which is what makes a class boundary
                    // ragged at material scale — but a cliff has to be painted where the ground
                    // actually falls away, not where the warp says it does, or the rock sits beside
                    // the face instead of on it. The warp still shows: which climate's cliff gets
                    // drawn comes from the warped label below.
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

                                // Fades out with distance from the water, so the cliff top runs
                                // back into ordinary hill rock rather than ending on a ring.
                                double inland = 1.0 - (coast - 1.0) / Math.Max(1, cliffReach - 1);

                                // Never the whole pixel: the face keeps a little of the biome it
                                // cuts through, which is what stops a cliff line reading as a decal
                                // laid over the coast.
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

                    // CK3 does not renormalise what it is given. Vanilla's detail_intensity.tga
                    // sums to exactly 255 on every pixel of the map without a single exception —
                    // the shader reads the four weights as a partition of the pixel, so a total
                    // that is not 255 over- or under-drives the whole blend, the summed normal
                    // included. The palette entries are authored as relative strengths and nothing
                    // was making them add up.
                    // Sharpen and normalise in one step — the contrast curve must be applied once
                    // and only once — then drop the layers too faint to be worth a slot and
                    // normalise again, so what they were carrying goes back to the ones that stayed.
                    blend = TerrainPalette.Normalized(blend, MaterialContrast);
                    blend = TerrainPalette.Pruned(blend, MaterialFloor);

                    // Set is settled; now scatter only the weights.
                    blend = TerrainPalette.Normalized(TerrainPalette.WeightJitter(blend,
                        Dither(x, srcY, 5), Dither(x, srcY, 6),
                        Dither(x, srcY, 7), Dither(x, srcY, 8), WeightDither));

                    // Unused slots keep the 255 sentinel, which is what vanilla writes: measured on
                    // its own detail_index, a zero-weight slot carries 255 on 60-87% of pixels and
                    // a slot carrying any weight carries 255 on none of them, out of 102 material
                    // indices spanning 3 to 104. An earlier revision of this filled unused slots
                    // with the dominant material on the belief that 255 was undefined; it is not,
                    // it is the sentinel, and a real index in a dead slot is at best a texture
                    // fetch the shader had been told it could skip.
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

            Reconcile(index, intensity, width, height);

            WriteTga(Path.Combine(dir, "detail_index.tga"), width, height, index);
            WriteTga(Path.Combine(dir, "detail_intensity.tga"), width, height, intensity);
        }

        used[TerrainPalette.Unused] = false;
        UsedMaterials = used;

        int distinct = used.Count(u => u);
        Console.WriteLine($"  terrain: detail_index + detail_intensity {width}x{height}, " +
                          $"{distinct} materials blended, band {blendReach:F0} px");

        Console.WriteLine(coastDistance is null
            ? "  terrain: coastal cliffs off"
            : $"  terrain: coastal cliffs from gradient {cliffStart:F1} (full at {cliffFull:F1}), " +
              $"within {cliffReach} px of water");

        // colormap.dds
        {
            var colormap = new byte[(long)width * height * 4];
            var colormapLand = new bool[(long)width * height];

            Parallel.For(0, height, y =>
            {
                long row = (long)y * width * 4;

                double hy = y * toHeightY;
                long elevRow = (long)Math.Clamp((int)hy, 0, hHeight - 1) * hWidth;

                for (int x = 0; x < width; x++)
                {
                    double hx = x * toHeightX;

                    // Sampled straight, not bilinearly. The bilinear this replaces could never do
                    // anything: gx worked out to hx * (pWidth / hWidth) with hx = x * (hWidth /
                    // width) and pWidth == width, so gx == x exactly, fx == 0, and all four corner
                    // samples collapsed onto c00 on every pixel of every map. The smoothing it was
                    // meant to provide happens in SmoothColormap below, where it can span more
                    // than the one pixel a bilinear tap reaches anyway.
                    float elev = elevation[elevRow + Math.Clamp((int)hx, 0, hWidth - 1)];
                    double relief = (elev - sea) / (double)Math.Max(1, mountains - sea);
                    double nC = Selector(nCField, hx * fC - 7.3, hy * fC + 29.4);

                    var c = GroundColor(terrain[y * pWidth + x], relief, nC);

                    long o = row + x * 4;
                    colormap[o] = c.B;
                    colormap[o + 1] = c.G;
                    colormap[o + 2] = c.R;
                    colormap[o + 3] = 255;

                    colormapLand[(long)y * width + x] = elev > sea;
                }
            });

            int softening = Math.Max(1, (int)Math.Round(cfg.Scaled(ColormapSoftening)));
            SmoothColormap(colormap, colormapLand, width, height, softening);
            ToVanillaEnvelope(colormap, colormapLand, width, height);

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