using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen.Terra;

/// <summary>
/// The terrain generator, end to end.
///
/// <code>
///   continents (W/4)  ->  plates  ->  landscape evolution  ->  upscale to W
///                                                                    |
///                                             detail  ->  drainage at W/2  ->  rivers
///                                                                    |
///                                        second incision  ->  relax  ->  carve channels
/// </code>
///
/// Three resolutions, each doing the job it is suited to: continents and tectonics are decided
/// where a cell is four export pixels across and the erosion can afford to run thirty-odd
/// iterations; rivers are extracted at the province map's own resolution, because that is the
/// raster CK3 reads them from; detail and channel carving happen at full resolution, where they
/// are visible.
/// </summary>
public static class TerraPipeline
{
    public static TerraResult Generate(MapConfig cfg, Rng rng)
    {
        int fw = cfg.Width, fh = cfg.Height;
        int pw = cfg.ProvinceWidth, ph = cfg.ProvinceHeight;
        int bw = fw / cfg.TerraBaseDivisor, bh = fh / cfg.TerraBaseDivisor;
        float sea = cfg.TerraSeaLevel;

        var total = System.Diagnostics.Stopwatch.StartNew();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Console.WriteLine($"Terra: base {bw}x{bh}, provinces {pw}x{ph}, heightmap {fw}x{fh}");

        // --- 1. Which crust is continental ---
        //
        // Plates first. Continents are derived from them rather than the other way round, so
        // coastlines sit on continental crust and margins line up with plate boundaries, and a
        // plate's type is a property of the plate rather than of whichever pixel its seed landed on.
        var plateField = PlateTectonics.BuildField(bw, bh, cfg, rng);
        Console.WriteLine($"  plate field ({sw.ElapsedMilliseconds} ms)");

        // --- 2. Where is land ---
        sw.Restart();
        var continents = ContinentBuilder.Build(bw, bh, plateField, cfg, rng);
        var baseHeight = continents.Height;
        AddInteriorRelief(baseHeight, bw, bh, sea, cfg, rng);
        Console.WriteLine($"  continents + warp ({sw.ElapsedMilliseconds} ms), " +
                          $"{LandFraction(baseHeight, sea) * 100:F1}% land");

        // --- 3. Where are mountains ---
        sw.Restart();
        var plates = PlateTectonics.BuildUplift(plateField, baseHeight, cfg, rng);
        Console.WriteLine($"  uplift belts ({sw.ElapsedMilliseconds} ms)");

        // --- 3. Erosion, the long pass ---
        sw.Restart();
        var options = new LandscapeEvolution.Options
        {
            Iterations = cfg.TerraErosionIterations,
            Erodibility = cfg.TerraErodibilityScaled,
            UpliftPerStep = cfg.TerraUpliftPerStep,
            Deposition = cfg.TerraDeposition,
            Talus = cfg.TerraTalusScaled,
            DepositionSlope = cfg.TerraDepositionSlopeScaled,
            MaxIncisionPerStep = cfg.TerraMaxIncisionScaled,
        };
        var baseFlow = LandscapeEvolution.Run(baseHeight, bw, bh, sea, plates.Uplift, plates.Rift,
            options);
        Console.WriteLine($"  {options.Iterations} erosion iterations ({sw.ElapsedMilliseconds} ms), " +
                          $"{LandFraction(baseHeight, sea) * 100:F1}% land");

        var preview = BuildPreview(baseHeight, baseFlow, plates, bw, bh, sea, cfg);
        baseFlow = null!;

        // --- 4. Up to full resolution, and add what the coarse grid could not hold ---
        sw.Restart();
        var full = Field.Upsample(baseHeight, bw, bh, fw, fh);
        DetailPass.AddDetail(full, fw, fh, baseHeight, bw, bh, sea, cfg, rng);
        Console.WriteLine($"  upscaled to {fw}x{fh} + detail ({sw.ElapsedMilliseconds} ms)");

        // --- 5. Refinement erosion, then drainage and rivers, at the province map's resolution ---
        //
        // This is where dendritic channels at export resolution come from, and it has to be real
        // erosion iterations rather than a closed-form approximation of them. Drainage networks are
        // an emergent property of routing flow and incising repeatedly; scaling each pixel by its
        // own drainage area in one pass, which is what this used to do, deepens existing valleys
        // but cannot branch. Below the base grid's four-export-pixel cell everything was therefore
        // the ridged noise from AddDetail, which reads as concentric swirls because that is what
        // ridged multifractal looks like.
        sw.Restart();
        var province = Field.Downsample(full, fw, fh, 2);
        var beforeRefine = (float[])province.Clone();

        var refineOptions = new LandscapeEvolution.Options
        {
            Iterations = cfg.TerraRefineIterations,
            Erodibility = (float)(cfg.TerraRefineErodibility / cfg.SlopeScaleFor(pw)),
            Deposition = cfg.TerraDeposition,
            DepositionSlope = (float)(0.03 * cfg.SlopeScaleFor(pw)),
            MaxIncisionPerStep = (float)(0.02 / cfg.SlopeScaleFor(pw)),

            // Headwaters, not trunk rivers. The base pass uses the literature-standard 0.5, which
            // puts almost all incision into the few largest catchments; branching texture is made
            // by the smallest streams, so this pass weights area far more weakly.
            AreaExponent = cfg.TerraRefineAreaExponent,

            // No thermal erosion here. It runs every iteration and pulls anything steeper than the
            // talus angle back toward its neighbours, which is precisely the freshly cut channel
            // walls — it fills in the dissection as fast as the incision creates it. Excessive
            // steepness is dealt with once at the end by DetailPass.Relax instead.
            TalusPasses = 0,
        };

        // No uplift: this is re-eroding relief the base pass already built, not making more.
        var provinceFlow = cfg.TerraRefineIterations > 0
            ? LandscapeEvolution.Run(province, pw, ph, sea, null, null, refineOptions)
            : FlowField.Compute(province, pw, ph, sea);

        // Carry only what the refinement *changed* up to full resolution, so the detail already
        // there survives and the erosion is added on top of it.
        if (cfg.TerraRefineIterations > 0)
        {
            Parallel.For(0, beforeRefine.Length, i => beforeRefine[i] = province[i] - beforeRefine[i]);
            DetailPass.ApplyDelta(full, fw, fh, beforeRefine, pw, ph, sea);
        }
        beforeRefine = null!;

        Console.WriteLine($"  {cfg.TerraRefineIterations} refinement iterations + drainage over " +
                          $"{provinceFlow.LandCells:N0} land cells ({sw.ElapsedMilliseconds} ms)");

        sw.Restart();
        var courses = RiverNetwork.Extract(provinceFlow, province, pw, ph, sea, cfg, rng);
        var rivers = RiverRaster.Draw(courses, pw, ph, cfg);
        var lakes = LakeMask(provinceFlow, province, pw, ph, sea, cfg, out int lakeCells);
        Console.WriteLine($"  {courses.Count} rivers over {rivers.RiverPixelCount:N0} pixels, " +
                          $"{lakeCells:N0} lake cells ({sw.ElapsedMilliseconds} ms)");

        // Each of the drainage arrays is the size of the province map and none is needed past this
        // point; dropping the reference lets them be collected before the full-resolution passes.
        provinceFlow = null!;
        province = null!;

        // --- 6. Full resolution: settle the added detail, then cut the river channels ---
        sw.Restart();
        DetailPass.Relax(full, fw, fh, sea, cfg.TerraDetailTalusScaled, 0.5f);
        DetailPass.CarveChannels(full, fw, fh, courses, pw, ph, sea, cfg);
        Console.WriteLine($"  full-resolution incision, relax and channels ({sw.ElapsedMilliseconds} ms)");

        // --- 7. Onto the simulation's elevation scale ---
        sw.Restart();
        var scale = TerraScale.Calibrate(full, sea, cfg);
        scale.ApplyInPlace(full);

        // Derived after every edit, so the province partition and the heightmap agree by
        // construction rather than by a reconciliation pass afterwards.
        var provinceElevation = Field.Downsample(full, fw, fh, 2);
        Console.WriteLine($"  height scale: {scale} ({sw.ElapsedMilliseconds} ms)");
        Console.WriteLine($"Terra complete in {total.ElapsedMilliseconds} ms");

        return new TerraResult
        {
            Elevation = full,
            ProvinceElevation = provinceElevation,
            RiverPixels = rivers.Pixels,
            RiverMask = rivers.Mask,
            LakeMask = lakes,
            Courses = courses,
            Preview = preview,
        };
    }

    /// <summary>
    /// Broad, low relief across continent interiors, so land away from any plate boundary is
    /// gently hilly rather than a plate. Amplitude is a fraction of what uplift produces, which is
    /// what keeps ranges reading as the only high ground.
    /// </summary>
    private static void AddInteriorRelief(float[] height, int width, int hgt, float sea,
        MapConfig cfg, Rng rng)
    {
        var relief = new SimplexNoise(rng);
        var warp = new SimplexNoise(rng);

        double freq = 9.0 / width;
        double warpFreq = 3.0 / width;
        double warpAmp = width * 0.05;
        float amplitude = cfg.TerraInteriorRelief;

        Parallel.For(0, hgt, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (height[i] <= sea) continue;

                double qx = warp.Noise2D(x * warpFreq, y * warpFreq) * warpAmp;
                double qy = warp.Noise2D(x * warpFreq + 4.1, y * warpFreq + 2.9) * warpAmp;

                double v = Field.Fbm(relief, (x + qx) * freq, (y + qy) * freq, 5, gain: 0.52);

                // Ramp in over the first fraction of the continental rise. Applied as a step at
                // the waterline instead — which is what "skip everything at or below sea level"
                // amounts to — it puts a cliff of up to the full amplitude around every coast.
                double shore = Field.SmoothStep(sea, sea + 0.025, height[i]);
                height[i] += (float)((v * 0.5 + 0.5) * amplitude * shore);
            }
        });
    }

    /// <summary>
    /// Depressions the fill had to raise are lakes. Only the ones big enough to read as water at
    /// map scale are kept — the rest are single-cell artefacts of the D8 network.
    ///
    /// The mask feeds terrain classification only. It deliberately does not become water in
    /// rivers.png or a sea zone in provinces.png: those two and the heightmap have to agree pixel
    /// for pixel, and adding a third source of "this is water" is exactly the disagreement that
    /// produced sea provinces rendering as dry ground.
    /// </summary>
    private static byte[] LakeMask(FlowField.Result flow, float[] height, int width, int hgt,
        float sea, MapConfig cfg, out int cells)
    {
        int n = width * hgt;
        var candidate = new bool[n];
        float tolerance = cfg.TerraLakeDepth;

        Parallel.For(0, n, i =>
        {
            candidate[i] = height[i] > sea && flow.Filled[i] - height[i] > tolerance;
        });

        var mask = new byte[n];
        var stack = new Stack<int>();
        var component = new List<int>();
        int minCells = cfg.TerraMinLakeCells;
        cells = 0;

        for (int start = 0; start < n; start++)
        {
            if (!candidate[start]) continue;

            component.Clear();
            stack.Push(start);
            candidate[start] = false;

            while (stack.Count > 0)
            {
                int c = stack.Pop();
                component.Add(c);

                int cx = c % width, cy = c / width;
                Push(cx - 1, cy); Push(cx + 1, cy); Push(cx, cy - 1); Push(cx, cy + 1);
            }

            if (component.Count < minCells) continue;
            foreach (int c in component) mask[c] = 1;
            cells += component.Count;
        }

        return mask;

        void Push(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= hgt) return;
            int i = y * width + x;
            if (!candidate[i]) return;
            candidate[i] = false;
            stack.Push(i);
        }
    }

    private static TerraWorld BuildPreview(float[] height, FlowField.Result flow,
        PlateTectonics.Result plates, int width, int hgt, float sea, MapConfig cfg)
    {
        var world = new TerraWorld
        {
            Width = width,
            Height = hgt,
            Land = (float[])height.Clone(),
            SeaLevel = sea,
            Uplift = plates.Uplift,
            Flow = (float[])flow.Flow.Clone(),
            Moisture = new float[width * hgt],
        };

        // A cheap stand-in for the climate model, purely so the preview render has something to
        // draw: wetness tracks drainage, which is close enough to see whether valleys formed.
        float reference = Field.Quantile(flow.Flow, i => height[i] > sea, 0.97);
        Parallel.For(0, world.Moisture.Length, i =>
        {
            world.Moisture[i] = Math.Clamp(flow.Flow[i] / Math.Max(1f, reference), 0f, 1f);
        });

        // Base-resolution courses, for the rivers preview only.
        float channel = Field.Quantile(flow.Flow, i => height[i] > sea,
            1.0 - Math.Clamp(cfg.TerraRiverDensity, 0.0002, 0.1));
        var course = new List<int>();
        for (int i = 0; i < height.Length; i++)
            if (height[i] > sea && flow.Flow[i] >= channel) course.Add(i);
        world.Rivers.Add(course.ToArray());

        return world;
    }

    private static double LandFraction(float[] height, float sea)
    {
        long land = 0;
        foreach (float h in height) if (h > sea) land++;
        return (double)land / height.Length;
    }
}
