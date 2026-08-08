using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// Stage 2. Voronoi plates with drift vectors, turned into an uplift rate field.
///
/// This is the piece that decides mountains are *strips*. Uplift is a narrow exponential falloff
/// from a plate boundary — the core belt is under one percent of the map width — with a much wider,
/// much weaker flank for foothills. Feed that to the erosion model as a rate rather than as a
/// height and the range keeps being rebuilt where the boundary is while rivers cut into it
/// everywhere else, which is what produces a thin white line with a fan of valleys either side.
///
/// The old generator had nothing like this. Its "mountainousness" at export resolution came from
/// <c>HeightDetail</c>'s <c>belt</c> term, an independent low-frequency noise field multiplied by
/// local relief — isotropic blobs, so mountains appeared as scattered patches wherever the
/// simulation happened to be high, never as linear ranges.
///
/// Boundaries are sampled through the same style of domain warp as the coastline, so plates are
/// organic polygons rather than the straight-edged cells raw Voronoi gives.
/// </summary>
public static class PlateTectonics
{
    private readonly record struct Plate(float X, float Y, float DriftX, float DriftY, bool Oceanic);

    public sealed class Result
    {
        /// <summary>Uplift rate, 0..1. Multiplied by a per-iteration amount by the erosion model.</summary>
        public required float[] Uplift;

        /// <summary>Rift/trench subsidence rate, 0..1.</summary>
        public required float[] Rift;
    }

    public static Result Build(int width, int height, float[] baseHeight, MapConfig cfg, Rng rng)
    {
        int count = cfg.TerraPlateCount;
        var plates = new Plate[count];

        for (int i = 0; i < count; i++)
        {
            float px = (float)(rng.NextDouble() * width);
            float py = (float)(rng.NextDouble() * height);
            double angle = rng.NextDouble() * Math.Tau;
            double speed = 0.35 + rng.NextDouble() * 0.65;

            // A plate is oceanic if the continent stage put it under water. That correlation is
            // what makes collision belts land on continental margins rather than at random.
            int sx = Math.Clamp((int)px, 0, width - 1);
            int sy = Math.Clamp((int)py, 0, height - 1);
            bool oceanic = baseHeight[sy * width + sx] <= cfg.TerraSeaLevel;

            plates[i] = new Plate(px, py,
                (float)(Math.Cos(angle) * speed), (float)(Math.Sin(angle) * speed), oceanic);
        }

        var warp = new SimplexNoise(rng);
        var belt = new SimplexNoise(rng);

        double warpFreq = 6.0 / width;
        double warpAmp = width * 0.060;
        double beltFreq = cfg.TerraRangeRoughness / width;

        // The narrow belt is what reads as a mountain *range*; the wide one is its foothills.
        float coreWidth = (float)(width * cfg.TerraRangeWidth);
        float flankWidth = coreWidth * 4.0f;

        // Hard cutoff on how far from a boundary uplift can reach.
        //
        // Without it the plate tessellation is visible across the whole map as flat-shaded
        // polygons, and the cause is not the falloff being too gentle — it is that the strength
        // multiplier (convergence, plate types, belt modulation) is constant for a given *pair* of
        // plates, so it changes discontinuously wherever the second-nearest plate changes, which
        // is a line running through the middle of a plate rather than along its edge. Forcing
        // uplift to zero well before that line removes the discontinuity entirely.
        float reach = coreWidth * 14f;

        var uplift = new float[width * height];
        var rift = new float[width * height];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                double wx = x + Field.Fbm(warp, x * warpFreq, y * warpFreq, 3) * warpAmp;
                double wy = y + Field.Fbm(warp, x * warpFreq + 3.7, y * warpFreq + 8.1, 3) * warpAmp;

                int best = -1, second = -1;
                float d1 = float.MaxValue, d2 = float.MaxValue;
                for (int p = 0; p < count; p++)
                {
                    float dx = (float)(plates[p].X - wx);
                    float dy = (float)(plates[p].Y - wy);
                    float d = dx * dx + dy * dy;
                    if (d < d1) { d2 = d1; second = best; d1 = d; best = p; }
                    else if (d < d2) { d2 = d; second = p; }
                }
                if (best < 0 || second < 0) continue;

                var a = plates[best];
                var b = plates[second];

                // Distance to the boundary, up to the usual factor of two: (|d2| - |d1|) is zero on
                // the bisector and grows at roughly twice the rate of true distance away from it.
                float edge = 0.5f * (MathF.Sqrt(d2) - MathF.Sqrt(d1));

                // Closing speed along the line between the two plate centres.
                float nx = b.X - a.X, ny = b.Y - a.Y;
                float len = MathF.Sqrt(nx * nx + ny * ny);
                if (len < 1e-4f) continue;
                nx /= len; ny /= len;

                float convergence = (a.DriftX * nx + a.DriftY * ny)
                                    - (b.DriftX * nx + b.DriftY * ny);

                if (edge > reach) continue;

                float window = (float)(1.0 - Field.SmoothStep(reach * 0.45, reach, edge));
                float core = MathF.Exp(-edge / coreWidth) * window;
                float flank = MathF.Exp(-edge / flankWidth) * window;

                // Along-belt modulation, so a range has peaks, saddles and passes rather than being
                // a uniform wall. Clamped low rather than to zero so the belt stays continuous.
                double along = 0.35 + 0.65 * Math.Clamp(
                    Field.Ridged(belt, x * beltFreq, y * beltFreq, 4) * 0.5 + 0.55, 0, 1);

                if (convergence > 0)
                {
                    // Continental collision raises the most; an oceanic plate diving under a
                    // continental one gives a lower volcanic arc; two oceanic plates barely break
                    // the surface.
                    float style = !a.Oceanic && !b.Oceanic ? 1.0f
                        : a.Oceanic ^ b.Oceanic ? 0.72f
                        : 0.30f;

                    uplift[i] = (float)(convergence * style * along
                                        * (0.86f * core + 0.14f * flank));
                }
                else
                {
                    rift[i] = -convergence * (0.7f * core + 0.3f * flank);
                }
            }
        });

        Normalise(uplift);
        Normalise(rift);
        return new Result { Uplift = uplift, Rift = rift };
    }

    private static void Normalise(float[] v)
    {
        float max = 0;
        foreach (float f in v) if (f > max) max = f;
        if (max <= 1e-6f) return;
        float inv = 1f / max;
        Parallel.For(0, v.Length, i => v[i] *= inv);
    }
}
