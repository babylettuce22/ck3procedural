using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// Stage 1. Voronoi plates with drift vectors and a continental/oceanic craton flag — and, from
/// those, the continentality field the coastline is built on.
///
/// **Plates come before continents, not after.** They used to be laid over finished geography and
/// each plate's type read from a single pixel sample at its seed point, which had two consequences.
/// A plate straddling a coast got its type from a coin flip, and that flag chooses the convergence
/// style, so whether a boundary became a continental collision or a barely-emergent island arc was
/// close to arbitrary. And nothing related coastlines to tectonics at all: a range could run
/// through open ocean or parallel to a coast purely by accident. Real maps read as legible largely
/// because margins have structure — mountains along active margins, flat passive margins where a
/// continent rifted apart.
///
/// What this deliberately does *not* do is let plates be the continents. Voronoi cells are
/// polygons, and thresholding plate membership directly gives continents with straight edges. This
/// produces a smooth <see cref="PlateField.Continentality"/> instead, which
/// <see cref="ContinentBuilder"/> uses as a bias on its noise rather than as the coastline itself.
///
/// Uplift is a narrow exponential falloff from a plate boundary — the core belt is under one
/// percent of the map width — with a much wider, much weaker flank for foothills. Fed to the
/// erosion model as a rate rather than as a height, the range keeps being rebuilt where the
/// boundary is while rivers cut into it, which is what produces a thin white line with a fan of
/// valleys either side rather than a dome.
/// </summary>
public static class PlateTectonics
{
    public readonly record struct Plate(float X, float Y, float DriftX, float DriftY, bool Oceanic);

    public sealed class PlateField
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required Plate[] Plates { get; init; }

        /// <summary>Nearest plate per cell, sampled through the domain warp.</summary>
        public required byte[] Nearest { get; init; }

        public required byte[] Second { get; init; }

        /// <summary>Distance to the boundary between those two, in cells.</summary>
        public required float[] EdgeDistance { get; init; }

        /// <summary>
        /// 1 deep inside continental crust, 0 deep inside oceanic, feathered across boundaries.
        /// </summary>
        public required float[] Continentality { get; init; }
    }

    public sealed class Result
    {
        /// <summary>Uplift rate, 0..1. Multiplied by a per-iteration amount by the erosion model.</summary>
        public required float[] Uplift { get; init; }

        /// <summary>Rift/trench subsidence rate, 0..1.</summary>
        public required float[] Rift { get; init; }
    }

    /// <summary>
    /// Lays out the plates and rasterises their geometry. Depends on nothing but the seed — this is
    /// now the first thing the pipeline does.
    /// </summary>
    public static PlateField BuildField(int width, int height, MapConfig cfg, Rng rng)
    {
        // Plate count stays constant rather than scaling with area, and this is a deliberate
        // departure from treating a small map as a window onto a vanilla-sized world.
        //
        // Scaling it was tried: at `full` correct density is 5 plates, and 5 plates supply so
        // little uplift that land eroded from a 40% target down to 16%. Plate boundaries are where
        // relief comes from, so starving a map of them drowns it. A constant count means small maps
        // run at a higher plate density than vanilla — their ranges are closer together than a
        // strict reading of the island model would give — which is the better trade.
        int count = Math.Max(2, cfg.TerraPlateCount);
        var seeds = new (float X, float Y, double Drift, double Speed)[count];

        for (int i = 0; i < count; i++)
        {
            seeds[i] = ((float)(rng.NextDouble() * width), (float)(rng.NextDouble() * height),
                rng.NextDouble() * Math.Tau, 0.35 + rng.NextDouble() * 0.65);
        }

        // Which plates carry continental crust. Chosen by ranking the plates against a
        // low-frequency field sampled at their seeds, rather than independently at random, so
        // cratons clump into a few landmasses the way Earth's do instead of scattering into an
        // archipelago of one-plate continents.
        //
        // The frequency matters more than it looks. Too low and every continental plate ends up in
        // one cluster — a single supercontinent whose interior is hundreds of cells from any coast
        // or plate boundary, so nothing gives it relief and depression filling turns it into one
        // enormous lake. Measured at 1.35 cycles: 151k lake cells against 9k before.
        var cratonNoise = new SimplexNoise(rng);
        int reference = cfg.ReferenceBaseWidth;
        double cratonFreq = (double)cfg.TerraCratonClustering / reference;

        var ranked = new (int Index, double Score)[count];
        for (int i = 0; i < count; i++)
            ranked[i] = (i, Field.Fbm(cratonNoise, seeds[i].X * cratonFreq,
                seeds[i].Y * cratonFreq, 3));
        Array.Sort(ranked, (a, b) => b.Score.CompareTo(a.Score));

        int continental = (int)Math.Round(count * Math.Clamp(cfg.TerraContinentalPlateFraction, 0.05, 0.95));
        continental = Math.Clamp(continental, 1, count - 1);

        var plates = new Plate[count];
        for (int r = 0; r < count; r++)
        {
            var (index, _) = ranked[r];
            var (x, y, angle, speed) = seeds[index];
            plates[index] = new Plate(x, y,
                (float)(Math.Cos(angle) * speed), (float)(Math.Sin(angle) * speed),
                Oceanic: r >= continental);
        }

        var warp = new SimplexNoise(rng);
        double warpFreq = 6.0 / reference;
        double warpAmp = reference * 0.060;

        var nearest = new byte[width * height];
        var second = new byte[width * height];
        var edge = new float[width * height];
        var continentality0 = new float[width * height];

        // How far either side of a boundary continentality takes to reach its plate's own value.
        // Wide on purpose: this feather is the only thing standing between plate-derived continents
        // and visibly polygonal coastlines.
        float feather = (float)(reference * Math.Max(0.005, cfg.TerraCratonFeather));

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                double wx = x + Field.Fbm(warp, x * warpFreq, y * warpFreq, 3) * warpAmp;
                double wy = y + Field.Fbm(warp, x * warpFreq + 3.7, y * warpFreq + 8.1, 3) * warpAmp;

                int best = -1, next = -1;
                float d1 = float.MaxValue, d2 = float.MaxValue;
                for (int p = 0; p < count; p++)
                {
                    float dx = (float)(plates[p].X - wx);
                    float dy = (float)(plates[p].Y - wy);
                    float d = dx * dx + dy * dy;
                    if (d < d1) { d2 = d1; next = best; d1 = d; best = p; }
                    else if (d < d2) { d2 = d; next = p; }
                }
                if (best < 0) continue;
                if (next < 0) next = best;

                // Distance to the boundary, up to the usual factor of two: (|d2| - |d1|) is zero on
                // the bisector and grows at roughly twice the rate of true distance away from it.
                float e = 0.5f * (MathF.Sqrt(d2) - MathF.Sqrt(d1));

                nearest[i] = (byte)best;
                second[i] = (byte)next;
                edge[i] = e;

                // Half-and-half exactly on the boundary, all of the nearest plate a feather away.
                float share = 0.5f * (1f - (float)Field.SmoothStep(0, feather, e));
                float mine = plates[best].Oceanic ? 0f : 1f;
                float theirs = plates[next].Oceanic ? 0f : 1f;
                continentality0[i] = mine * (1f - share) + theirs * share;
            }
        });

        // Blur the whole continentality field, hard.
        //
        // The per-cell feather above only ramps perpendicular to the nearest boundary, so inside a
        // plate the field is a flat plateau and its only gradient is the plate outline itself.
        // Adding that to the coastline noise makes the threshold contour trace Voronoi edges —
        // straight margins and sharp corners — and leaves continent interiors perfectly flat,
        // which then fill with lakes because there is no gradient for water to run down. A 2D blur
        // rounds the corners off and turns the tessellation into continental *mass*, which is what
        // it should be contributing.
        int blurRadius = (int)Math.Max(1, reference * Math.Max(0.0, cfg.TerraCratonBlur));
        var continentality = blurRadius > 1
            ? Field.Blur(continentality0, width, height, blurRadius, 3)
            : continentality0;

        int continentalCount = 0;
        foreach (var p in plates) if (!p.Oceanic) continentalCount++;
        Console.WriteLine($"  {count} plates, {continentalCount} continental / " +
                          $"{count - continentalCount} oceanic");

        return new PlateField
        {
            Width = width,
            Height = height,
            Plates = plates,
            Nearest = nearest,
            Second = second,
            EdgeDistance = edge,
            Continentality = continentality,
        };
    }

    /// <summary>
    /// Turns the plate geometry into uplift and rift rates. Runs after the coastline exists,
    /// because uplift is damped over deep ocean — but the plate *types* it keys off are a property
    /// of the plates themselves now, not a sample of the terrain.
    /// </summary>
    public static Result BuildUplift(PlateField field, float[] baseHeight, MapConfig cfg, Rng rng)
    {
        int width = field.Width, height = field.Height;
        var plates = field.Plates;

        var belt = new SimplexNoise(rng);
        int reference = cfg.ReferenceBaseWidth;
        double beltFreq = cfg.TerraRangeRoughness / reference;

        // The narrow belt is what reads as a mountain *range*; the wide one is its foothills.
        float coreWidth = (float)(reference * cfg.TerraRangeWidth);
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
                float e = field.EdgeDistance[i];
                if (e > reach) continue;

                var a = plates[field.Nearest[i]];
                var b = plates[field.Second[i]];

                // Closing speed along the line between the two plate centres.
                float nx = b.X - a.X, ny = b.Y - a.Y;
                float len = MathF.Sqrt(nx * nx + ny * ny);
                if (len < 1e-4f) continue;
                nx /= len; ny /= len;

                float convergence = (a.DriftX * nx + a.DriftY * ny)
                                    - (b.DriftX * nx + b.DriftY * ny);

                float window = (float)(1.0 - Field.SmoothStep(reach * 0.45, reach, e));
                float core = MathF.Exp(-e / coreWidth) * window;
                float flank = MathF.Exp(-e / flankWidth) * window;

                // Along-belt modulation, so a range has peaks, saddles and passes rather than being
                // a uniform wall. Clamped low rather than to zero so the belt stays continuous.
                double along = 0.35 + 0.65 * Math.Clamp(
                    Field.Ridged(belt, x * beltFreq, y * beltFreq, 4) * 0.5 + 0.55, 0, 1);

                if (convergence > 0)
                {
                    // Continental collision raises the most; an oceanic plate diving under a
                    // continental one gives a lower volcanic arc; two oceanic plates barely break
                    // the surface. These are now meaningful — the flags describe the whole plate.
                    float style = !a.Oceanic && !b.Oceanic ? 1.0f
                        : a.Oceanic ^ b.Oceanic ? 0.72f
                        : 0.30f;

                    float raw = (float)(convergence * style * along * (0.86f * core + 0.14f * flank));

                    // Damped over water, so a convergent boundary out in open ocean makes an island
                    // arc rather than a mountain range standing in the sea.
                    float landFactor = baseHeight[i] > cfg.TerraSeaLevel ? 1.0f : 0.15f;
                    uplift[i] = raw * landFactor;
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
