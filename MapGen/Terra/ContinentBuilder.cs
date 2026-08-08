using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// Stage 1. Decides where land is, and nothing else.
///
/// The output is deliberately almost flat: continents sit a few percent above sea level with a
/// gentle rise toward their interiors, and the sea floor slopes away from the coast. All the
/// relief comes later, from tectonic uplift and erosion. Separating "where is land" from "how high
/// is it" is the main structural change from the old pipeline, where both fell out of the same
/// magma accumulation and could not be tuned independently — growing enough land there
/// necessarily meant piling up elevation everywhere, which is the root cause of the map being
/// uniformly mountainous.
///
/// Coastlines come from a two-stage domain warp rather than from a raw noise threshold. A plain
/// threshold gives blobs with a characteristic single scale of wobble; warping the *sample
/// position* first, with a warp that is itself warped, produces the peninsulas, bays and
/// long-armed shapes real coastlines have.
/// </summary>
public static class ContinentBuilder
{
    public sealed class Result
    {
        public required float[] Height;

        /// <summary>The raw warped field, before thresholding. Reused as a continent-interior cue.</summary>
        public required float[] Mask;

        public required float Threshold;
    }

    public static Result Build(int width, int height, MapConfig cfg, Rng rng)
    {
        var warpCoarse = new SimplexNoise(rng);
        var warpFine = new SimplexNoise(rng);
        var shape = new SimplexNoise(rng);
        var detail = new SimplexNoise(rng);

        // Frequencies as "cycles across the map width", so a change of map size does not change
        // the shape of the world — only how finely it is sampled.
        double shapeFreq = cfg.TerraContinentScale / width;
        double detailFreq = shapeFreq * 5.5;
        double warpCoarseFreq = 1.7 / width;
        double warpFineFreq = 6.5 / width;

        // Amplitudes are a fraction of map width. These are small on purpose: at 0.20 the coarse
        // warp shears the shape field so far that continents come out as long thin filaments
        // rather than landmasses, because the warp displaces by more than the feature size.
        double warpCoarseAmp = width * 0.075;
        double warpFineAmp = width * 0.022;

        var mask = new float[width * height];
        double halfHeight = (height - 1) / 2.0;

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double qx = Field.Fbm(warpCoarse, x * warpCoarseFreq, y * warpCoarseFreq, 3);
                double qy = Field.Fbm(warpCoarse, x * warpCoarseFreq + 5.2, y * warpCoarseFreq + 1.3, 3);
                double wx = x + qx * warpCoarseAmp;
                double wy = y + qy * warpCoarseAmp;

                double rx = Field.Fbm(warpFine, wx * warpFineFreq, wy * warpFineFreq, 3);
                double ry = Field.Fbm(warpFine, wx * warpFineFreq + 9.1, wy * warpFineFreq - 4.4, 3);
                wx += rx * warpFineAmp;
                wy += ry * warpFineAmp;

                double v = Field.Fbm(shape, wx * shapeFreq, wy * shapeFreq, 5, gain: 0.55);
                v += 0.30 * Field.Fbm(detail, wx * detailFreq, wy * detailFreq, 4);

                // Drown the poles. Vanilla's top and bottom rows are entirely sea, and a province
                // clipped by the map boundary has an open border the locator pass cannot close.
                double lat = Math.Abs(y - halfHeight) / halfHeight;
                double taper = 1.0 - Field.SmoothStep(0.84, 1.0, lat);
                v = v * taper - (1.0 - taper) * 0.8;

                mask[y * width + x] = (float)v;
            }
        });

        float threshold = ThresholdFor(mask, cfg.TargetLandFraction);
        var result = new float[width * height];

        float sea = cfg.TerraSeaLevel;
        float shelf = cfg.TerraContinentRise;
        float abyss = cfg.TerraOceanDepth;

        // How fast the shelf and the abyss approach their asymptotes, in units of the mask's own
        // spread — scaling by the spread keeps the coastal gradient the same on every seed.
        float spread = Math.Max(1e-4f, Field.Quantile(mask, null, 0.98) - threshold);
        float landK = 1.5f / spread;

        // The sea floor has to fall away *fast* just offshore. Measured against vanilla, a gentle
        // ramp here leaves water 20 px out at 13.8/255 where vanilla is at 4.5 — barely below the
        // 19/255 water plane, so the sea-floor material shows through the water along every coast,
        // which is the "ocean above water / mud at coastal province borders" symptom.
        //
        // The legacy generator got this right for the wrong reason: HeightDetail.ShapeSeafloor used
        // (1 - nearness^3), whose cube is what made it drop off sharply. Note this only shapes the
        // *ranking* of depths; the absolute values still come from MapDataWriter's measured
        // VanillaWaterCurve, which independently forces 85% of water to pure black.
        float seaK = (float)cfg.TerraShelfSteepness
                     / Math.Max(1e-4f, threshold - Field.Quantile(mask, null, 0.02));

        Parallel.For(0, result.Length, i =>
        {
            float d = mask[i] - threshold;
            // Both branches meet at exactly `sea` when d is 0, so the coastline has no step in it.
            // Anything that offsets one side is a cliff at every shoreline on the map.
            result[i] = d > 0
                ? sea + shelf * (1f - MathF.Exp(-d * landK))
                : sea - abyss * (1f - MathF.Exp(d * seaK));
        });

        return new Result { Height = result, Mask = mask, Threshold = threshold };
    }

    /// <summary>
    /// The mask value that puts exactly <paramref name="fraction"/> of the map above water.
    ///
    /// This replaces GrowLandToTarget, which hit the same target by running up to 400 extra rounds
    /// of magma emission and diffusion — inflating elevation everywhere in order to move a
    /// coastline. Solving for the threshold instead is exact, instant, and leaves the height field
    /// untouched.
    /// </summary>
    private static float ThresholdFor(float[] mask, double fraction)
        => Field.Quantile(mask, null, 1.0 - Math.Clamp(fraction, 0.02, 0.9));
}
