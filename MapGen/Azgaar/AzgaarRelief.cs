using Ck3MapGen.Config;
using Ck3MapGen.Emit;
using Ck3MapGen.Io;
using NoiseTool.Core;
using NoiseTool.Pipeline;
using NoiseTool.Stages;

namespace Ck3MapGen.MapGen;

/// <summary>
/// An Azgaar world's terrain rebuilt the way a Forge preset builds its own: the export supplies the
/// broad shape — where the land is high, where the ranges run, where the coast is — and the Forge's
/// Hills, Ridges, painted-range and erosion stages supply everything finer.
///
/// <b>Why the exported PNG cannot simply be smoothed.</b> Azgaar's Heightmap layer draws one flat
/// shelf per whole-number height (measured on the Ondrerol pair: grey = 2.8·h + 10, r 0.989), so
/// the PNG is a stack of terraces — 93% of neighbouring land pixels identical, and after
/// <see cref="HeightmapSource.Upscale8To16Bit"/> 47% of land dead flat against vanilla's 0.2%. But
/// the terraces are only how it is drawn. What the export actually knows about height is its
/// background grid, 200×100 cells on a 1920×960 canvas — one number per ~46 px of a 9216-wide
/// map. Everything finer in the PNG is rendering, so no filter recovers detail; it has to be made,
/// and making it is exactly what the Forge's detail stages were tuned to do.
///
/// <b>The split.</b> A grey opening of the grid (radius <see cref="OpeningRadius"/> cells) keeps
/// the broad uplands and drops anything narrower — which on an Azgaar map is precisely the ranges
/// the author drew with its Range tool. The broad part becomes the base; the part the opening
/// removed becomes a <see cref="RangePaintStage"/> layer, so each drawn range is built as a range
/// (crest along the line, grain running with it) rather than left to <see cref="RidgeStage"/>'s
/// noise, which otherwise scatters ranges of its own over the whole map and drowns the author's.
///
/// <b>The numbers are ours, the order is the export's.</b> Land is ranked by the base and given
/// vanilla's own land hypsometry up to its <see cref="BaseTopQuantile"/> — the top is left for the
/// ranges — and the sea is ranked by the export's depth and given vanilla's sea. The same
/// reanchoring <see cref="AzgaarClimate"/> and the development ranking use: Azgaar decides where,
/// our curves decide how much. Without it the drawn heights sit at 2.8× vanilla's median.
///
/// <b>The coastline is the PNG's, pixel for pixel.</b> Land is exactly what
/// <see cref="HeightmapNormalizer"/> would have called land, so provinces, the alignment check and
/// every consumer of the land mask see the same world the "as drawn" path does; every stage after
/// the base honours the waterline.
///
/// Measured on Ondrerol at 8192 against the as-drawn import (vanilla in brackets): dead-flat land
/// 48% → 5.4% (0.2%), sigma-1.5 slope p50 2.9 → 95 (116), land median 9210 → 3716 (4454), regional
/// contrast 96x → 8.9x (8.1x), high flats 16% → 0.2%; pits 25% of land (13%), as on the presets.
/// Coastline identical to the pixel on Ondrerol and Poily; about 15 s at 8192 with the GPU.
/// </summary>
public static class AzgaarRelief
{
    /// <summary>The detail stages, as a Forge preset with no generator of its own.</summary>
    public static string PresetPath =>
        Path.Combine(AppContext.BaseDirectory, "assets", "forge-presets", "azgaar", "detail.json");

    /// <summary>
    /// Radius of the opening that separates drawn ranges from uplands, in grid cells. Azgaar's
    /// Range tool draws ridges two to four cells wide; 3 keeps every upland broader than about six
    /// cells and takes the ranges off it cleanly. 2 leaves the thicker ranges in the base; 4 starts
    /// taking the flanks of real uplands with them.
    /// </summary>
    private const int OpeningRadius = 3;

    /// <summary>Gaussian on the opened grid, in cells: rounds off the disc-shaped plateaus an opening leaves.</summary>
    private const double BaseSmoothing = 1.0;

    /// <summary>
    /// How far a cell must stand above the opened ground, in Azgaar height units, to begin to count
    /// as range, and how much further to count fully. Below the floor is the ordinary hummocking of
    /// Azgaar's Hill tool, which the Hills stage reproduces better than a painted range would.
    /// </summary>
    private const float RangeFloor = 8f, RangeSpan = 10f;

    /// <summary>Strength below which a cell is range flank rather than crest.</summary>
    private const float CrestThreshold = 0.25f;

    /// <summary>
    /// The shortest a raised group of cells may be, end to end in cells, and still be a range. The
    /// opening also takes lone hills off the uplands, and painted as ranges each one comes out a
    /// disc with rings in it; a drawn range is a line many cells long.
    /// </summary>
    private const double MinRangeLength = 6;

    /// <summary>How far a range's flanks reach from its crest, in grid cells.</summary>
    private const float CrestWidth = 1.8f;

    /// <summary>
    /// The share of vanilla's land hypsometry the base is given. The rest — vanilla's highest 5% —
    /// is what the ranges and ridges add on top.
    /// </summary>
    private const double BaseTopQuantile = 0.95;

    /// <summary>Authoring size of the range layer: the size every shipped preset paints at.</summary>
    private const int LayerWidth = 2048, LayerHeight = 1024;

    /// <summary>Gaussian on the finished range layer, in layer pixels (about 8 map pixels at 8192).</summary>
    private const double LayerSmoothing = 2.0;

    /// <summary>The land an Azgaar grid cell is at or above: its own sea level is 20 on 0-100.</summary>
    private const int AzgaarLand = 20;

    /// <summary>
    /// Vanilla's land height above the land line (4883), percentiles 0..100, from its own
    /// heightmap.png. What the base's land is ranked onto.
    /// </summary>
    private static readonly int[] VanillaLand =
    [
        0, 155, 289, 403, 493, 571, 637, 699, 757, 813, 869, 921,
        973, 1025, 1077, 1129, 1179, 1233, 1291, 1351, 1419, 1491, 1571, 1655,
        1745, 1841, 1939, 2041, 2141, 2247, 2351, 2451, 2553, 2655, 2755, 2853,
        2951, 3051, 3155, 3257, 3361, 3469, 3575, 3681, 3787, 3893, 4003, 4111,
        4221, 4335, 4453, 4577, 4705, 4835, 4969, 5111, 5261, 5419, 5581, 5747,
        5913, 6087, 6269, 6461, 6663, 6879, 7113, 7361, 7621, 7895, 8179, 8477,
        8785, 9115, 9461, 9825, 10205, 10605, 11023, 11443, 11895, 12365, 12841, 13339,
        13855, 14399, 14967, 15555, 16165, 16787, 17403, 18023, 18701, 19491, 20409, 21673,
        23699, 26833, 29752, 31906, 44322,
    ];

    /// <summary>
    /// Vanilla's sea bed, percentiles 0..100 of the 16-bit value: 86% of it at 0, then a narrow
    /// shelf up to the plane. What the export's depths are ranked onto.
    /// </summary>
    private static readonly int[] VanillaSea =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 86, 526, 1108, 1634, 2162, 2634, 2908, 3616, 4088, 4128,
        4214, 4378, 4558, 4726, 4882,
    ];

    /// <summary>
    /// Whether the export carries the background grid this needs. A "Minimal" export does not, and
    /// the caller then builds the heightmap as drawn.
    /// </summary>
    public static bool CanBuild(AzgaarWorld world)
        => world.Grid is { CellsX: > 0, CellsY: > 0 } grid && grid.Cells.Count > 0;

    /// <summary>
    /// The heightmap, 16-bit on CK3's own scale (land above <see cref="MapDataWriter.WaterLevel16"/>),
    /// at the drawn image's size.
    /// </summary>
    /// <param name="drawn">The exported PNG as decoded and fitted: only its coastline is used.</param>
    /// <param name="seaLevel16">
    /// The 16-bit value at or below which <paramref name="drawn"/> is sea, exactly as
    /// <see cref="HeightmapNormalizer"/> reads it, so the coastline cannot move.
    /// </param>
    public static ushort[] Build(ushort[] drawn, int width, int height, int seaLevel16, AzgaarWorld world,
        int seed, CancellationToken ct, IProgress<string>? status)
    {
        var grid = world.Grid!;
        int cx = grid.CellsX, cy = grid.CellsY;
        double canvasWidth = Math.Max(1.0, world.Info.Width);
        double canvasHeight = Math.Max(1.0, world.Info.Height);
        double spacingX = grid.Spacing > 0 ? grid.Spacing : canvasWidth / cx;
        double spacingY = grid.Spacing > 0 ? grid.Spacing : canvasHeight / cy;

        var h = new float[cx * cy];
        foreach (var (cell, at) in grid.Cells.Select((c, i) => (c, i)))
        {
            int index = cell.I >= 0 && cell.I < h.Length ? cell.I : at;
            if (index < h.Length) h[index] = cell.H;
        }

        var land = new bool[drawn.Length];
        Parallel.For(0, land.Length, i => land[i] = drawn[i] > seaLevel16);

        // The broad uplands and the drawn ranges, on the grid.
        var filled = FillSeaFromLand(h, cx, cy);
        var opened = Dilate(Erode(filled, cx, cy, OpeningRadius), cx, cy, OpeningRadius);
        var strength = new float[h.Length];
        for (int i = 0; i < h.Length; i++)
            strength[i] = h[i] >= AzgaarLand
                ? Math.Clamp((filled[i] - opened[i] - RangeFloor) / RangeSpan, 0f, 1f)
                : 0f;
        KeepLongRanges(strength, cx, cy);
        var broad = Gaussian(opened, cx, cy, BaseSmoothing);

        ct.ThrowIfCancellationRequested();
        status?.Report("azgaar relief: base");

        // Land ranked by the uplands, sea by the export's own depths.
        var upland = SampleBSpline(broad, cx, cy, width, height, canvasWidth, canvasHeight, spacingX, spacingY);
        var depth = SampleBSpline(h, cx, cy, width, height, canvasWidth, canvasHeight, spacingX, spacingY);

        const int landFloor = MapDataWriter.WaterLevel16 + 2;
        var landRank = Ranks(upland, land, want: true);
        var seaRank = Ranks(depth, land, want: false);

        var field = new HeightField(width, height);
        Parallel.For(0, field.Data.Length, i =>
        {
            float v = land[i]
                ? landFloor + Table(VanillaLand, landRank[i] * BaseTopQuantile)
                : Math.Min(MapDataWriter.WaterLevel16 - 1, Table(VanillaSea, seaRank[i]));
            field.Data[i] = v / 65535f;
        });
        upland = depth = landRank = seaRank = null!;

        // The detail stages, with the drawn ranges painted into the range stage.
        var pipeline = new HeightPipeline();
        var loaded = PresetIO.Load(pipeline, PresetPath);
        foreach (string warning in loaded.Warnings)
            Console.WriteLine($"  azgaar relief: {warning}");

        if (!HydraulicErosionStage.GpuAvailable)
        {
            foreach (var stage in pipeline.Stages.Where(s => s is HydraulicErosionStage)) stage.Enabled = false;
            Console.WriteLine("  azgaar relief: no GPU for erosion, so the valleys are not cut");
        }

        double rangeShare = 0;
        if (pipeline.Stages.OfType<RangePaintStage>().FirstOrDefault(s => s.Enabled) is { } ranges)
            rangeShare = PaintRanges(ranges, strength, cx, cy, canvasWidth, canvasHeight, spacingX, spacingY);

        var stages = pipeline.Stages.Where(s => s.Enabled).ToList();
        for (int i = 0; i < stages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            status?.Report($"azgaar relief: [{i + 1}/{stages.Count}] {stages[i].DisplayName}");
            field = stages[i].Process(field, new StageContext
            {
                Width = width,
                Height = height,
                SeaLevel = Ck3.SeaLevelNormalised,
                MasterSeed = seed,
                IsPreview = false,
                PreviewLongEdge = 0,
                Cancellation = ct,
                Status = status,
            });
        }

        var raw = field.ToUInt16();

        // The stages keep to the waterline, and this is what makes that a guarantee rather than a
        // promise: a coastline that moved here would disagree with provinces.png.
        long moved = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (land[i] == (raw[i] > MapDataWriter.WaterLevel16)) continue;
            raw[i] = land[i] ? (ushort)landFloor : (ushort)(MapDataWriter.WaterLevel16 - 1);
            moved++;
        }

        Console.WriteLine($"  azgaar relief: {cx}x{cy} grid, {rangeShare:P1} of the map under drawn ranges, "
                          + $"{stages.Count} detail stage(s), seed {seed}"
                          + (moved > 0 ? $", {moved:N0} px put back on the drawn coastline" : ""));
        return raw;
    }

    /// <summary>
    /// Fills the range stage's layer from the grid: full strength along each drawn crest, falling
    /// smoothly to nothing <see cref="CrestWidth"/> cells either side. Returns the share of the map
    /// the layer touches.
    ///
    /// Crest strength varies along a range, so the reach has to carry it: a pixel beside a strong
    /// crest must read higher than one beside a faint one at the same distance. That is a grey
    /// dilation — the maximum over the crest of strength × falloff(distance) — done here as one
    /// distance transform per strength level, which keeps it exact at the crest (where it is the
    /// strength itself) and continuous everywhere, so the range's flanks carry no steps.
    /// </summary>
    private static double PaintRanges(RangePaintStage stage, float[] strength, int cx, int cy,
        double canvasWidth, double canvasHeight, double spacingX, double spacingY)
    {
        var layer = stage.Channels.First(c => c.Key == "range").Layer;
        if (layer.Width != LayerWidth || layer.Height != LayerHeight) stage.EnsureLayers(LayerWidth, LayerHeight);
        layer = stage.Channels.First(c => c.Key == "range").Layer;

        int lw = layer.Width, lh = layer.Height;
        var s = new float[lw * lh];
        Parallel.For(0, lh, y =>
        {
            double gy = (y + 0.5) / lh * canvasHeight / spacingY - 0.5;
            for (int x = 0; x < lw; x++)
            {
                double gx = (x + 0.5) / lw * canvasWidth / spacingX - 0.5;
                s[y * lw + x] = Bilinear(strength, cx, cy, gx, gy);
            }
        });

        float reach = (float)(CrestWidth * lw / (canvasWidth / spacingX));
        var m = new float[s.Length];
        for (int i = 0; i < m.Length; i++) m[i] = s[i] >= CrestThreshold ? s[i] : 0f;

        const int levels = 8;
        for (int k = 2; k <= levels; k++)
        {
            float level = (float)k / levels;
            var crest = new bool[s.Length];
            bool any = false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= level) { crest[i] = true; any = true; }
            if (!any) break;

            var d2 = DistanceField.SquaredDistance(crest, lw, lh, seedWhen: true);
            Parallel.For(0, m.Length, i =>
            {
                float t = MathF.Sqrt(d2[i]) / reach;
                if (t >= 1f) return;
                float fall = 1f - t * t * (3f - 2f * t);
                float v = level * fall;
                if (v > m[i]) m[i] = v;
            });
        }

        // A maximum of smooth terms still creases where the winning level changes, and the range
        // stage's profile would turn each crease into a rib down the flank.
        m = Gaussian(m, lw, lh, LayerSmoothing);

        // Written a tile at a time because that is how a layer is marked as painted on.
        for (int tile = 0; tile < layer.TilesX * layer.TilesY; tile++)
        {
            var r = layer.TileRect(tile);
            var contents = new float[r.Width * r.Height];
            for (int y = 0; y < r.Height; y++)
                Array.Copy(m, (r.Y + y) * lw + r.X, contents, y * r.Width, r.Width);
            layer.SwapTile(tile, contents);
        }
        layer.Bump();

        long touched = 0;
        foreach (float v in m) if (v > 0.05f) touched++;
        return (double)touched / m.Length;
    }

    /// <summary>
    /// Clears every raised group shorter than <see cref="MinRangeLength"/>. Groups are the
    /// 8-connected cells with any strength; length is the longest axis of the group's spread
    /// (√12 standard deviations along its principal axis, which is exactly the length of a uniform
    /// line and 1.7 radii of a disc), so a long thin range passes where a round knot of the same
    /// cell count does not. What is
    /// cleared stays in the base, where the Hills stage gives it texture.
    /// </summary>
    private static void KeepLongRanges(float[] strength, int cx, int cy)
    {
        var seen = new bool[strength.Length];
        var members = new List<int>();
        var stack = new Stack<int>();

        for (int start = 0; start < strength.Length; start++)
        {
            if (seen[start] || strength[start] <= 0f) continue;

            members.Clear();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                members.Add(i);
                int x = i % cx, y = i / cx;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= cx || ny >= cy) continue;
                        int n = ny * cx + nx;
                        if (seen[n] || strength[n] <= 0f) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
            }

            double mx = 0, my = 0;
            foreach (int i in members) { mx += i % cx; my += i / cx; }
            mx /= members.Count;
            my /= members.Count;

            double sxx = 0, syy = 0, sxy = 0;
            foreach (int i in members)
            {
                double ddx = i % cx - mx, ddy = i / cx - my;
                sxx += ddx * ddx; syy += ddy * ddy; sxy += ddx * ddy;
            }
            sxx /= members.Count; syy /= members.Count; sxy /= members.Count;

            double major = (sxx + syy) / 2 + Math.Sqrt((sxx - syy) * (sxx - syy) / 4 + sxy * sxy);
            double length = Math.Sqrt(12 * major);
            if (length >= MinRangeLength) continue;

            foreach (int i in members) strength[i] = 0f;
        }
    }

    /// <summary>
    /// Sea cells take the height of the nearest land, breadth-first in index order. An opening run
    /// over the raw grid would take the sea floor as the "ground" every coastal cell stands on, and
    /// every stretch of coast would then read as a range.
    /// </summary>
    private static float[] FillSeaFromLand(float[] h, int cx, int cy)
    {
        var filled = (float[])h.Clone();
        var done = new bool[h.Length];
        var queue = new Queue<int>();
        for (int i = 0; i < h.Length; i++)
            if (h[i] >= AzgaarLand) { done[i] = true; queue.Enqueue(i); }
        if (queue.Count == 0) return filled;

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % cx, y = i / cx;
            foreach (int n in (int[])[x > 0 ? i - 1 : -1, x < cx - 1 ? i + 1 : -1, y > 0 ? i - cx : -1, y < cy - 1 ? i + cx : -1])
            {
                if (n < 0 || done[n]) continue;
                done[n] = true;
                filled[n] = filled[i];
                queue.Enqueue(n);
            }
        }
        return filled;
    }

    private static float[] Erode(float[] a, int cx, int cy, int r) => Morph(a, cx, cy, r, min: true);

    private static float[] Dilate(float[] a, int cx, int cy, int r) => Morph(a, cx, cy, r, min: false);

    /// <summary>Min or max over a disc of radius <paramref name="r"/> cells, edges clamped to the grid.</summary>
    private static float[] Morph(float[] a, int cx, int cy, int r, bool min)
    {
        var result = new float[a.Length];
        for (int y = 0; y < cy; y++)
        {
            for (int x = 0; x < cx; x++)
            {
                float best = a[y * cx + x];
                for (int dy = -r; dy <= r; dy++)
                {
                    int ny = y + dy;
                    if (ny < 0 || ny >= cy) continue;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = x + dx;
                        if (nx < 0 || nx >= cx || dx * dx + dy * dy > r * r) continue;
                        float v = a[ny * cx + nx];
                        if (min ? v < best : v > best) best = v;
                    }
                }
                result[y * cx + x] = best;
            }
        }
        return result;
    }

    /// <summary>Separable Gaussian on the grid, edges clamped.</summary>
    private static float[] Gaussian(float[] a, int cx, int cy, double sigma)
    {
        int r = (int)Math.Ceiling(3 * sigma);
        var k = new double[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++) sum += k[i + r] = Math.Exp(-0.5 * i * i / (sigma * sigma));
        for (int i = 0; i < k.Length; i++) k[i] /= sum;

        var tmp = new float[a.Length];
        var result = new float[a.Length];
        for (int y = 0; y < cy; y++)
            for (int x = 0; x < cx; x++)
            {
                double acc = 0;
                for (int i = -r; i <= r; i++) acc += k[i + r] * a[y * cx + Math.Clamp(x + i, 0, cx - 1)];
                tmp[y * cx + x] = (float)acc;
            }
        for (int y = 0; y < cy; y++)
            for (int x = 0; x < cx; x++)
            {
                double acc = 0;
                for (int i = -r; i <= r; i++) acc += k[i + r] * tmp[Math.Clamp(y + i, 0, cy - 1) * cx + x];
                result[y * cx + x] = (float)acc;
            }
        return result;
    }

    /// <summary>
    /// The grid resampled to the map with a uniform cubic B-spline: smooth to the second derivative,
    /// so nothing of the lattice prints through as facets once hillshaded — which an interpolating
    /// cubic over Azgaar's cells visibly does. Separable: rows first onto the output's columns, then
    /// down.
    /// </summary>
    private static float[] SampleBSpline(float[] a, int cx, int cy, int width, int height,
        double canvasWidth, double canvasHeight, double spacingX, double spacingY)
    {
        var rows = new float[cy * width];
        Parallel.For(0, cy, gy =>
        {
            for (int x = 0; x < width; x++)
            {
                double g = (x + 0.5) / width * canvasWidth / spacingX - 0.5;
                int i0 = (int)Math.Floor(g);
                var (w0, w1, w2, w3) = Weights(g - i0);
                int row = gy * cx;
                rows[gy * width + x] = (float)(w0 * a[row + Math.Clamp(i0 - 1, 0, cx - 1)]
                                             + w1 * a[row + Math.Clamp(i0, 0, cx - 1)]
                                             + w2 * a[row + Math.Clamp(i0 + 1, 0, cx - 1)]
                                             + w3 * a[row + Math.Clamp(i0 + 2, 0, cx - 1)]);
            }
        });

        var result = new float[width * height];
        Parallel.For(0, height, y =>
        {
            double g = (y + 0.5) / height * canvasHeight / spacingY - 0.5;
            int j0 = (int)Math.Floor(g);
            var (w0, w1, w2, w3) = Weights(g - j0);
            int r0 = Math.Clamp(j0 - 1, 0, cy - 1) * width, r1 = Math.Clamp(j0, 0, cy - 1) * width;
            int r2 = Math.Clamp(j0 + 1, 0, cy - 1) * width, r3 = Math.Clamp(j0 + 2, 0, cy - 1) * width;
            int o = y * width;
            for (int x = 0; x < width; x++)
                result[o + x] = (float)(w0 * rows[r0 + x] + w1 * rows[r1 + x] + w2 * rows[r2 + x] + w3 * rows[r3 + x]);
        });
        return result;

        static (double, double, double, double) Weights(double t)
        {
            double t2 = t * t, t3 = t2 * t;
            return ((1 - 3 * t + 3 * t2 - t3) / 6, (4 - 6 * t2 + 3 * t3) / 6,
                    (1 + 3 * t + 3 * t2 - 3 * t3) / 6, t3 / 6);
        }
    }

    private static float Bilinear(float[] a, int cx, int cy, double gx, double gy)
    {
        int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
        double fx = gx - x0, fy = gy - y0;
        float At(int x, int y) => a[Math.Clamp(y, 0, cy - 1) * cx + Math.Clamp(x, 0, cx - 1)];
        double top = At(x0, y0) + (At(x0 + 1, y0) - At(x0, y0)) * fx;
        double bottom = At(x0, y0 + 1) + (At(x0 + 1, y0 + 1) - At(x0, y0 + 1)) * fx;
        return (float)(top + (bottom - top) * fy);
    }

    /// <summary>
    /// Each selected pixel's rank among the selected, 0..1, from a 65,536-bin histogram: linear in
    /// the pixel count and deterministic, and equal values share a rank rather than being ordered
    /// by where they sit. Unselected pixels read 0.
    /// </summary>
    private static float[] Ranks(float[] values, bool[] land, bool want)
    {
        const int bins = 1 << 16;
        float lo = float.MaxValue, hi = float.MinValue;
        long n = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (land[i] != want) continue;
            float v = values[i];
            if (v < lo) lo = v;
            if (v > hi) hi = v;
            n++;
        }

        var ranks = new float[values.Length];
        if (n == 0) return ranks;

        double scale = hi > lo ? (bins - 1) / (double)(hi - lo) : 0;
        var counts = new long[bins];
        for (int i = 0; i < values.Length; i++)
            if (land[i] == want) counts[(int)((values[i] - lo) * scale)]++;

        var mid = new float[bins];
        long below = 0;
        for (int b = 0; b < bins; b++)
        {
            mid[b] = (float)((below + counts[b] * 0.5) / n);
            below += counts[b];
        }

        Parallel.For(0, values.Length, i =>
        {
            if (land[i] == want) ranks[i] = mid[(int)((values[i] - lo) * scale)];
        });
        return ranks;
    }

    /// <summary>A percentile table (0..100) read at <paramref name="q"/> in 0..1, linearly.</summary>
    private static float Table(int[] table, double q)
    {
        double at = Math.Clamp(q, 0, 1) * (table.Length - 1);
        int i = Math.Min((int)at, table.Length - 2);
        double t = at - i;
        return (float)(table[i] + (table[i + 1] - table[i]) * t);
    }
}

/// <summary>
/// An Azgaar world's heightmap made by <see cref="AzgaarRelief"/>: the exported PNG for the
/// coastline, the export's grid for everything else, finished by the Forge's detail stages. The
/// Azgaar page's "Weathered" terrain; "As drawn" is the plain <see cref="FileHeightmapProvider"/>.
///
/// The seed is fixed when the provider is made, as <see cref="AppGUI.QuickReliefProvider"/>'s is,
/// so the stamp can say exactly what <see cref="Produce"/> will return.
/// </summary>
public sealed class AzgaarReliefProvider(string heightmapPath, string exportPath, int seed,
    (int Width, int Height)? fitTo = null, bool allowUnverifiedSize = false) : HeightmapProvider
{
    public string HeightmapPath { get; } = heightmapPath;
    public string ExportPath { get; } = exportPath;

    public override string Label => $"{Path.GetFileName(HeightmapPath)} · weathered";

    public override string Detail =>
        $"{HeightmapPath}\n\nAzgaar's coastline with its ranges and uplands read from "
        + $"{Path.GetFileName(ExportPath)}, finished with the Forge's hills, ridges and erosion "
        + $"(seed {seed})."
        + (fitTo is { } fit ? $" Built at {fit.Width}x{fit.Height}." : "");

    public override string PhaseName => "heightmap forge";

    public override string Stamp
    {
        get
        {
            static string Of(string path)
            {
                var info = new FileInfo(path);
                return info.Exists ? $"{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}" : $"{path}|missing";
            }

            return $"azgaar-relief|{Of(HeightmapPath)}|{Of(ExportPath)}|{Of(AzgaarRelief.PresetPath)}|seed={seed}"
                   + (fitTo is { } f ? $"|fit={f.Width}x{f.Height}" : "")
                   + (allowUnverifiedSize ? "|unverified" : "");
        }
    }

    public override HeightmapImage Produce(MapConfig cfg, CancellationToken ct, IProgress<string>? status)
    {
        var drawn = HeightmapSource.Read(HeightmapPath, cfg, fitTo, allowUnverifiedSize);

        var world = AzgaarJson.Load(ExportPath).World;
        if (!AzgaarRelief.CanBuild(world))
        {
            Console.WriteLine("  azgaar relief: the export has no background grid (a Minimal export?), "
                              + "so the heightmap is built as drawn");
            return drawn;
        }

        int sea = (int)Math.Round(Math.Clamp(cfg.SourceSeaLevel, 0, 254) * MapDataWriter.Step255);
        var raw = AzgaarRelief.Build(drawn.Raw, drawn.Width, drawn.Height, sea, world, seed, ct, status);
        // Weathered relief, but the coastline is still the one the author drew.
        return HeightmapSource.FromRaw(raw, drawn.Width, drawn.Height, Label, cfg, allowUnverifiedSize,
            importedCoastline: true);
    }
}
