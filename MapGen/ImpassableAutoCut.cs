using System.Numerics;
using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// What <see cref="ImpassableAutoCut"/> decided, for the log and the preview's hover text.
/// <c>CutHeight</c> is the smoothed elevation the quota stopped at; mountain ground below it is
/// passable unless the plateau rule took it. Shares are of land area. <c>Passes</c> are the
/// corridors cut through the walls, empty with <see cref="MapConfig.MountainPasses"/> off.
/// </summary>
public sealed record AutoCutDiagnostics(
    float MountainLine, float SteepLine, float GateLine, float CutHeight,
    double TargetShare, double PlateauShare, double MaskShare, int Pieces,
    IReadOnlyList<MountainPass>? Passes = null);

/// <summary>
/// The impassable mask drawn from the terrain before any province exists, so the partition can be
/// cut along it exactly as it is along a painted mask in Snap mode. See
/// <see cref="MapConfig.ImpassableAutoCut"/>.
///
/// Scoring provinces after the partition can only take a province or leave it, and a province
/// grown without regard to the mountains straddles the foot of the range. The walls that result
/// are as coarse as the provinces, which on a small map is very coarse: a barony is the same
/// 3192 px at every map size, so on a 4096 map it spans twice the terrain it does at 8192. Cutting
/// first puts the wall's edge on the terrain instead.
///
/// Seven steps, all per pixel on the province raster:
/// <list type="number">
/// <item>Mountain ground: land above the gate line (the mountain line capped at
/// <see cref="MapConfig.ImpassableGateHeight"/>), plus steep ground within a short reach of it.</item>
/// <item>Plateaus: mountain ground whose barony-wide neighbourhood is mostly above the mountain
/// line is always taken, outside the quota, as <see cref="MapConfig.ImpassableMountainPlateaus"/>
/// does for provinces.</item>
/// <item>Cores: the rest of the mountain ground, ranked by its lightly smoothed height — less
/// flat ground below the mountain line, which <see cref="MapConfig.ImpassableMinRuggedness"/>
/// keeps passable up to <see cref="MapConfig.ImpassableCeilingHeight"/>. Mountain ground above
/// the ceiling is always a core, whatever the share.</item>
/// <item>Foot: each core runs down its own slopes to where the ground stands
/// <see cref="MapConfig.ImpassableFootRelief"/> of the way from its local floor to its local peak.
/// The core height cut is searched so the walls, flanks included, cover
/// <see cref="MapConfig.ImpassableShareOfLand"/> of land area — or less, where the scored pass's
/// floor says the map's mountains run out sooner. Where a flank is a cliff, the wall runs on down
/// it, outside the share (<see cref="MapConfig.ImpassableCliffSlope"/>).</item>
/// <item>Clean: close small gaps, open hairlines, fill enclosed holes smaller than a barony and
/// ledges cut off against the water smaller than half a barony, and drop pieces smaller than half
/// a barony.</item>
/// <item>Crests: each wall may run on along its own ridge
/// (<see cref="MapConfig.ImpassableCrestFollow"/>), then walls still smaller than
/// <see cref="MapConfig.ImpassableMinWallBaronies"/> are dropped and the rest cleaned again.</item>
/// <item>Passes: a corridor per wall, more on a long one, through a thin neck the land route goes a long way
/// round; see <see cref="MountainPasses"/>.</item>
/// </list>
/// A wall therefore ends where its range's slopes give out, at whatever height that is for that
/// range; with the foot turned off, every wall ends on one height line.
///
/// Height ranks, not the slope-weighted score <c>MarkImpassable</c> uses. Ranking pixels by that
/// score repeats its fault at a finer grain: a gentle massif top loses to its own steep flanks, so
/// the flanks are walled in a crescent and the top is left passable. Measured on three worlds
/// against the scored rule (small Lowlands 4096, Lowlands and Highlands 8192): ground that is not
/// mountain fell from 14.3%, 8.3% and 4.1% of the walls to 0.3%, 2.2% and 0%, and passable
/// summits beside a wall from 3, 0 and 11 to none.
///
/// Everything here is a fixed number of passes over the raster, each row-major and parallel, so
/// the cost follows the map's pixel count and barely its barony size; the foot's floor and peak
/// are found on a pooled grid. The floods are the exceptions: the foot's cut search runs ten, each
/// over the walls alone, and the hole fill starts only beside the mask and gives up at a barony's
/// worth. On an 8192 map the whole cut is a second or two, the foot about a second of it.
/// </summary>
public static class ImpassableAutoCut
{
    /// <summary>
    /// The mask, one flag per province-raster pixel, or null when there is nothing to cut — no
    /// target share, no land, or no mountain ground — in which case the scored pass runs instead.
    /// </summary>
    public static (bool[] Mask, AutoCutDiagnostics Diagnostics)? Build(
        byte[] landMask, float[] elevation, int width, int height, MapConfig cfg)
    {
        double share = Math.Clamp(cfg.ImpassableShareOfLand, 0, 0.5);
        if (share <= 0) return null;

        float mountainLine = Provinces.LandLine(elevation, landMask, 1.0 - cfg.MountainLineShare);
        if (mountainLine == float.MaxValue) return null;

        var slope = Provinces.Slopes(elevation, width, height);
        float steepLine = Provinces.SteepLine(slope, landMask, cfg);
        float gateLine = Provinces.GateLine(mountainLine, cfg);
        float ceiling = Provinces.CeilingLine(cfg);
        double gateMin = Math.Clamp(cfg.ImpassableMinMountainGround, 0, 1);

        // Neighbourhoods are province-sized, because they stand in for a province; reaches that
        // follow the terrain scale with the map, because the terrain does. The reaches are the
        // 12 px and 4 px the prototype was measured with on an 8192 map, in vanilla pixels.
        double barony = cfg.BaronyPixels;
        int radius = Math.Max(1, (int)Math.Round(Math.Sqrt(barony / Math.PI)));
        int reach = Math.Max(3, (int)Math.Round(cfg.Scaled(13.5)));
        int close = Math.Max(2, (int)Math.Round(cfg.Scaled(4.5)));

        int n = width * height;
        var land = new byte[n];
        var high = new byte[n];
        var gate = new byte[n];
        var steep = new byte[n];
        long landTotal = SumRows(height, y =>
        {
            long count = 0;
            for (int i = y * width, end = i + width; i < end; i++)
            {
                if (landMask[i] == 0) continue;
                land[i] = 1;
                count++;
                if (elevation[i] >= mountainLine) high[i] = 1;
                if (elevation[i] >= gateLine) gate[i] = 1;
                if (slope[i] >= steepLine) steep[i] = 1;
            }
            return count;
        });
        if (landTotal == 0) return null;

        // 1. Mountain ground. Steep ground counts only near ground above the gate line; on its own,
        // the eroded sides of low hills are as steep as any range.
        // Whole-raster arrays are the cost here, not arithmetic: an 8192 map's int raster is 128 MB,
        // allocated while the generator already holds several GB. The box counts share one scratch
        // row buffer, and the steep counts reuse this one once the ground is known.
        var scratch = new int[n];
        var nearGate = BoxCount(gate, width, height, reach, scratch);
        var ground = new bool[n];
        Parallel.For(0, height, y =>
        {
            for (int i = y * width, end = i + width; i < end; i++)
                ground[i] = land[i] == 1 && (gate[i] == 1 || (steep[i] == 1 && nearGate[i] > 0));
        });

        // 2 and 3. Plateaus outside the quota; the rest ranked by height.
        var landNear = BoxCount(land, width, height, radius, scratch);
        var highNear = BoxCount(high, width, height, radius, scratch);
        var gateNear = BoxCount(gate, width, height, radius, scratch);
        bool plateaus = cfg.ImpassableMountainPlateaus;

        var raw = new bool[n];
        var candidate = new bool[n];
        long plateauPixels = SumRows(height, y =>
        {
            long count = 0;
            for (int i = y * width, end = i + width; i < end; i++)
            {
                if (!ground[i]) continue;
                if (plateaus && highNear[i] * 2 > landNear[i]) { raw[i] = true; count++; continue; }
                if (landNear[i] > 0 && gateNear[i] >= gateMin * landNear[i]) candidate[i] = true;
            }
            return count;
        });

        // The foot needs the smoothed terrain everywhere, to find each place's local floor and peak;
        // without it only the candidates are ranked, so only their rows are finished.
        double foot = Math.Clamp(cfg.ImpassableFootRelief, 0, 1);
        double rugged = Math.Max(0, cfg.ImpassableMinRuggedness);
        var smooth = Gaussian(elevation, land, foot > 0 || rugged > 0 ? null : candidate, width, height, radius / 4.0);

        // Flat ground below the mountain line is not wall, however high it stands: a tableland or
        // a bench partway up a range is somewhere people live and armies march. Height alone let
        // it in — on an inland-sea map 14.6% of the walls were flat ground under the mountain line,
        // long smooth shelves lower than the peaks beside them. The plateau rule still takes flat
        // ground mostly above the mountain line; nothing here touches it. Nor is anything above the
        // ceiling flat for this purpose, however smooth: on a map whose mountain line stood at 475,
        // tablelands at 400–478 were exempt, and higher than anything vanilla stands on.
        var flat = rugged > 0
            ? FlatGround(smooth, elevation, MathF.Min(mountainLine, ceiling), land, width, height,
                Math.Max(1, radius / 2), rugged)
            : null;
        if (flat is not null)
            Parallel.For(0, height, y =>
            {
                for (int i = y * width, end = i + width; i < end; i++)
                    if (flat[i]) candidate[i] = false;
            });
        int candidates = 0;
        for (int i = 0; i < n; i++) if (candidate[i]) candidates++;

        // The scored pass stops where its floor says the mountains have run out, however much of the
        // share is left; the cut keeps that. Its floor is the same median + deviations over the
        // same score, 0.35 high + 0.65 steep, taken over a barony-wide window instead of a
        // province, and the land that clears it caps the share. Where the mountains are ample the
        // share still binds (the three worlds the cut was measured on cleared it on 16.7%, 17.7%
        // and 7.2% of land against shares of 8%, 5% and 5%); on the Ondrerol import, whose relief
        // is gentler, 4.8% cleared it against 8%, and without the cap the cut walled 10.9% of land
        // where the scored pass had walled 4.3%.
        var steepNear = BoxCount(steep, width, height, radius, scratch, into: nearGate);
        double qualifying = QualifyingShare(landNear, highNear, steepNear, land, landTotal, cfg, out double floor);
        double target = Math.Min(share, qualifying);
        long room = (long)(target * landTotal);

        // The share decides how far down the walls reach; it may not leave mountain ground above the
        // ceiling passable. Ranked by height, the share is spent on the tallest ranges first, and
        // each grows down its own flanks: on a continents world whose top 3.5% of land began at 475,
        // the cores began at 510, those ranges' walls ran down to 230–300, and eleven highlands
        // peaking at 480–520 were left passable. Capping the cut there walls every one of them,
        // past the share, and leaves a map whose mountains stay under the ceiling untouched.
        float cutHeight;
        bool capped = false;
        bool[]? footAnywhere = null;
        if (foot > 0)
        {
            // Walls run from their height-ranked cores down to their own foot; the cores shrink to
            // leave the quota's room for the flanks. See FootGround.
            var footGround = FootGround(smooth, landNear, gateNear, land, gateMin, width, height,
                (int)Math.Round(3.0 * radius), (float)foot, out footAnywhere);
            if (flat is not null)
                Parallel.For(0, height, y =>
                {
                    for (int i = y * width, end = i + width; i < end; i++)
                        if (flat[i]) footGround[i] = footAnywhere[i] = false;
                });
            cutHeight = FootCut(raw, candidate, smooth, footGround, candidates,
                target * landTotal + plateauPixels, width, height);
            capped = cutHeight > ceiling;
            if (capped) cutHeight = ceiling;
            raw = Grow(raw, candidate, smooth, cutHeight, footGround, width, height, out _);
        }
        else
        {
            if (room >= candidates) cutHeight = float.NegativeInfinity;
            else if (room <= 0) cutHeight = float.PositiveInfinity;
            else
            {
                var values = new float[candidates];
                for (int i = 0, k = 0; i < n; i++) if (candidate[i]) values[k++] = smooth[i];
                cutHeight = Provinces.Select(values, candidates - (int)room);
            }
            capped = cutHeight > ceiling;
            if (capped) cutHeight = ceiling;
            Parallel.For(0, height, y =>
            {
                for (int i = y * width, end = i + width; i < end; i++)
                    if (candidate[i] && smooth[i] >= cutHeight) raw[i] = true;
            });
        }

        // Cliffs, outside the share. The foot is measured against the local floor, and beside the
        // sea the floor is the sea, so the lower half of a sea cliff stayed passable however sheer
        // it was, and the partition put baronies on it: on a 4096 inland-sea world, three holdings
        // stood on ground 2.1–3.3x as steep as the steepest under any vanilla holding, in small
        // provinces squeezed between a wall and the water, one of them a pass cut down the cliff.
        // So a wall runs on down its flank for as long as the flank stays that steep. See
        // ExtendDownCliffs.
        string cliffs = "";
        if (cfg.ImpassableCliffSlope > 0)
        {
            long added = ExtendDownCliffs(raw, elevation, land, cfg.Limits.SeaLevelUpper,
                (float)cfg.ImpassableCliffSlope, width, height);
            if (added > 0) cliffs = $"; cliffs +{(double)added / landTotal:P1} of land";
        }

        // 4. Clean. Closing bridges the pixel gaps a threshold leaves in a range; opening removes
        // hairline spurs that would cut slivers into the neighbouring provinces.
        var mask = Erode(Dilate(raw, width, height, close), width, height, close);
        Parallel.For(0, height, y =>
        {
            for (int i = y * width, end = i + width; i < end; i++) mask[i] &= land[i] == 1;
        });
        // The opening also clears strips narrower than about a quarter of a barony: with flat ground
        // left out, the steep rim of a freed tableland would otherwise stay walled as a thin ring
        // around it.
        int open = Math.Max(Math.Max(1, close / 2), radius / 4);
        mask = Dilate(Erode(mask, width, height, open), width, height, open);

        FillHoles(mask, land, width, height, (int)Math.Ceiling(barony));

        var (parts, partSizes) = Label(mask, width, height);
        int pieces = 0;
        foreach (int size in partSizes) if (size >= barony / 2) pieces++;
        long masked = SumRows(height, y =>
        {
            long count = 0;
            for (int i = y * width, end = i + width; i < end; i++)
            {
                int p = parts[i];
                if (p == 0) continue;
                if (partSizes[p - 1] < barony / 2) mask[i] = false;
                else count++;
            }
            return count;
        });

        // 5. Crests, on the cleaned walls: before the clean, a wall is still hundreds of fragments
        // that the closing is about to fuse, so a size rule there drops pieces of real walls, and
        // each fragment's crest spreads on its own (on the inland-sea world that walled 12–14% of
        // land instead of 8%). The extension leaves ragged edges, so it is cleaned the same way.
        double minWall = Math.Max(0.5, cfg.ImpassableMinWallBaronies);
        string crests = "";
        if (masked > 0 && (cfg.ImpassableCrestFollow > 0 || minWall > 0.5))
        {
            var (followed, added, dropped) = FollowCrests(mask, smooth, footAnywhere, flat, land, width, height,
                radius, barony, cfg.Limits.SeaLevelUpper, gateLine, cfg.ImpassableCrestFollow,
                cfg.ImpassableCrestReachBaronies, minWall);
            mask = Erode(Dilate(followed, width, height, close), width, height, close);
            Parallel.For(0, height, y =>
            {
                for (int i = y * width, end = i + width; i < end; i++) mask[i] &= land[i] == 1;
            });
            mask = Dilate(Erode(mask, width, height, open), width, height, open);
            FillHoles(mask, land, width, height, (int)Math.Ceiling(barony));

            (parts, partSizes) = Label(mask, width, height);
            pieces = 0;
            foreach (int size in partSizes) if (size >= barony / 2) pieces++;
            masked = SumRows(height, y =>
            {
                long count = 0;
                for (int i = y * width, end = i + width; i < end; i++)
                {
                    int p = parts[i];
                    if (p == 0) continue;
                    if (partSizes[p - 1] < barony / 2) mask[i] = false;
                    else count++;
                }
                return count;
            });
            crests = $"; crests +{(double)added / landTotal:P1} of land, {dropped} wall(s) under {minWall:0.#} baronies dropped";
        }

        if (masked == 0) return null;

        // 6. Passes, on the finished walls: any earlier and the closing would seal them again.
        var passes = MountainPasses.Carve(mask, land, elevation, parts, partSizes, width, height, radius, barony,
            cfg, out long opened);
        masked -= opened;

        var diagnostics = new AutoCutDiagnostics(mountainLine, steepLine, gateLine, cutHeight,
            target, (double)plateauPixels / landTotal, (double)masked / landTotal, pieces, passes);
        string cut = float.IsNegativeInfinity(cutHeight)
            ? "all mountain ground fit the share"
            : capped
                ? $"quota stopped above the ceiling, so cores start at the ceiling ({cutHeight:F0}), past the share"
                : $"quota stopped at smoothed height {cutHeight:F0}";
        if (foot > 0) cut += $", walls taken down to {foot:P0} of their local relief";
        string quota = qualifying < share
            ? $"share {target:P1}, capped by the floor {floor:F2} (setting {share:P0})"
            : $"share {share:P0}";
        Console.WriteLine($"  impassable auto-cut: {diagnostics.MaskShare:P1} of land in {pieces} wall piece(s) " +
                          $"({quota} + plateaus {diagnostics.PlateauShare:P1}; mountain ground above " +
                          $"{gateLine:F0}, mountain line {mountainLine:F0}, steep line {steepLine:F2}/px; {cut}{cliffs}{crests})");
        if (cfg.MountainPasses) MountainPasses.Report(passes);
        return (mask, diagnostics);
    }

    /// <summary>
    /// The share of land whose barony-wide relief score clears the scored pass's floor:
    /// <see cref="MapConfig.ImpassableMinMountainShare"/>, or the median plus
    /// <see cref="MapConfig.ImpassableScoreDeviations"/> median absolute deviations when that is
    /// higher. The median and deviation are taken over every third land pixel, which is plenty for
    /// two percentiles of a field this smooth.
    /// </summary>
    private static double QualifyingShare(int[] landNear, int[] highNear, int[] steepNear, byte[] land,
        long landTotal, MapConfig cfg, out double floor)
    {
        float weight = (float)Math.Clamp(cfg.ImpassableSlopeWeight, 0, 1);
        float Score(int i) => landNear[i] == 0 ? 0f
            : ((1 - weight) * highNear[i] + weight * steepNear[i]) / landNear[i];

        var sample = new List<float>();
        for (int i = 0; i < land.Length; i += 3)
            if (land[i] != 0) sample.Add(Score(i));
        var values = sample.ToArray();
        float median = Provinces.Select(values, values.Length / 2);
        for (int i = 0; i < values.Length; i++) values[i] = Math.Abs(values[i] - median);
        float mad = Provinces.Select(values, values.Length / 2);

        floor = Math.Max(cfg.ImpassableMinMountainShare, median + Math.Max(0, cfg.ImpassableScoreDeviations) * mad);
        float line = (float)floor;
        const int block = 4096;
        long over = SumRows((land.Length + block - 1) / block, b =>
        {
            long count = 0;
            for (int i = b * block, end = Math.Min(land.Length, i + block); i < end; i++)
                if (land[i] != 0 && Score(i) >= line) count++;
            return count;
        });
        return (double)over / landTotal;
    }

    /// <summary>Runs <paramref name="row"/> over every row in parallel and sums what it returns.</summary>
    private static long SumRows(int height, Func<int, long> row)
    {
        long total = 0;
        Parallel.For(0, height, () => 0L, (y, _, sum) => sum + row(y),
            sum => Interlocked.Add(ref total, sum));
        return total;
    }

    /// <summary>
    /// For every pixel, how many set pixels lie in the (2r+1)² square around it. Integer, so a
    /// share compared against a threshold is exact. Edges mirror (d c b a | a b c d), as scipy's
    /// "reflect" does, which is what the prototype was measured with.
    /// </summary>
    private static int[] BoxCount(byte[] src, int width, int height, int r, int[] rows, int[]? into = null)
    {
        Parallel.For(0, height, y =>
        {
            int row = y * width, sum = 0;
            for (int d = -r; d <= r; d++) sum += src[row + Mirror(d, width)];
            for (int x = 0; x < width; x++)
            {
                rows[row + x] = sum;
                sum += src[row + Mirror(x + r + 1, width)] - src[row + Mirror(x - r, width)];
            }
        });

        // The vertical pass runs down bands of columns, keeping one running sum per column, so
        // each step reads a contiguous run of a row instead of striding a whole row per pixel.
        const int band = 512;
        var result = into ?? new int[src.Length];
        Parallel.For(0, (width + band - 1) / band, b =>
        {
            int x0 = b * band, count = Math.Min(width, x0 + band) - x0;
            var sum = new int[count];
            for (int d = -r; d <= r; d++)
            {
                int row = Mirror(d, height) * width + x0;
                for (int x = 0; x < count; x++) sum[x] += rows[row + x];
            }
            for (int y = 0; y < height; y++)
            {
                int o = y * width + x0;
                int add = Mirror(y + r + 1, height) * width + x0, sub = Mirror(y - r, height) * width + x0;
                for (int x = 0; x < count; x++)
                {
                    result[o + x] = sum[x];
                    sum[x] += rows[add + x] - rows[sub + x];
                }
            }
        });
        return result;
    }

    /// <summary>
    /// Where a wall may run down to from its core: land near real mountains (the window gate) that
    /// stands at least <paramref name="foot"/> of the way from its local floor to its local peak.
    ///
    /// Floor and peak are the smoothed terrain's lowest and highest values over a
    /// (2<paramref name="reach"/>+1)² square — a grey opening and a grey dilation, three baronies
    /// across — each blurred so the squares leave no mark. A range rising out of a lowland has its
    /// foot low; one standing on a high plateau has its foot high; either way the wall stops where
    /// its own slopes give out, not at one height for the whole map. Measured on a continents map,
    /// wall edges spanned 257 to 294 (IQR 17) with a single height line and 198 to 325 (IQR 78) at
    /// 0.45, still with no wall below elevation 119.
    ///
    /// The cores are not replaced, only extended: ranking by relative height alone leaves a broad
    /// plateau passable (it is low against its own floor) inside a ring of walled flanks.
    /// </summary>
    /// <param name="anywhere">
    /// The same relief test without the window gate: where a wall's flank may run once something
    /// other than a core has put the wall there. See <see cref="FollowCrests"/>.
    /// </param>
    private static bool[] FootGround(float[] smooth, int[] landNear, int[] gateNear, byte[] land, double gateMin,
        int width, int height, int reach, float foot, out bool[] anywhere)
    {
        // Floor and peak are fields three baronies across, blurred by half that, so they are found
        // on a grid a few pixels to the cell and read back bilinearly: the blur alone was over five
        // seconds on an 8192 map at full resolution. Each cell carries its block's lowest value into
        // the floor and highest into the peak, so pooling loses none of the extremes the filters
        // look for.
        int q = Math.Max(1, reach / 24);
        int cw = (width + q - 1) / q, ch = (height + q - 1) / q;
        int coarse = Math.Max(1, Math.Min((int)Math.Round((double)reach / q), Math.Min(cw, ch) - 1));
        var (lowest, highest) = Pool(smooth, width, height, q, cw, ch);
        var floor = Upsample(Gaussian(Extreme(Extreme(lowest, cw, ch, coarse, max: false), cw, ch, coarse, max: true),
            null, null, cw, ch, coarse / 2.0), cw, ch, q, width, height);
        var peak = Upsample(Gaussian(Extreme(highest, cw, ch, coarse, max: true), null, null, cw, ch, coarse / 2.0),
            cw, ch, q, width, height);

        var ground = new bool[smooth.Length];
        var any = new bool[smooth.Length];
        Parallel.For(0, height, y =>
        {
            for (int i = y * width, end = i + width; i < end; i++)
            {
                if (land[i] == 0) continue;
                float relief = Math.Clamp((smooth[i] - floor[i]) / Math.Max(peak[i] - floor[i], 1f), 0f, 1f);
                any[i] = relief >= foot;
                if (landNear[i] == 0 || gateNear[i] < gateMin * landNear[i]) continue;
                ground[i] = any[i];
            }
        });
        anywhere = any;
        return ground;
    }

    /// <summary>
    /// Runs each wall on along its own ridge, then drops the walls that are still too small to
    /// block anything. Returns the new mask, the pixels the ridges added, and the walls dropped.
    ///
    /// The foot grows a wall only where enough mountain ground lies within a barony's square
    /// window, so around a lone peak just over the gate line, on a ridge whose crest sits just
    /// under it, a wall is the window's footprint: one square province on the summit, with the
    /// rest of the range passable. Such a wall either belongs to its range or is nothing. From
    /// every wall at once, breadth first so the nearer wall takes contested ground, it extends over
    /// connected land whose smoothed height stays above sea + <paramref name="fraction"/> × (the
    /// wall's peak − sea), at most <paramref name="reachBaronies"/> barony widths out. The new
    /// crest then takes flanks down to the wall's foot (<paramref name="footAnywhere"/>, the relief
    /// test without the window gate) within a barony's radius. Walls under
    /// <paramref name="minBaronies"/> afterwards are dropped.
    ///
    /// Line relative to each wall's own peak, not a map-wide line: a lower line everywhere fuses
    /// ranges into systems (at 200 on the inland-sea world, the largest connected mountain ground
    /// grew from 66 baronies to 149), and a lone spike on a low ridge would decide whether the
    /// whole ridge is walled. The reach bounds it for the same reason.
    ///
    /// A crest never crosses <paramref name="flat"/> ground, which the cores and the foot already
    /// leave out: a ridge is not flat. Without that, a small wall's line fell below a smooth apron
    /// beside it and the crest spread over the whole apron. On a drowned-crater world, a lone wall
    /// peaking at 276 had a line of 228, and its crest flooded the crater's outer slope at 230–280.
    /// Crests made up a fifth of the walls there and 77% of the walled flat low ground, and 62% of
    /// what they added was ground the flat rule had exempted.
    ///
    /// Nor does a crest run below <paramref name="gate"/>, the line mountain ground starts at. A
    /// wall whose peak barely clears the gate is a mound on a hill, and 80% of its height is the
    /// hill: on a scar world, walls peaking at 239–257 had lines of 198–213, and their crests ran
    /// along hill bands at 190–220. Up to 85% of such a wall was crest, standing no higher or
    /// steeper than the passable land around it, and each had been grown past the size at which
    /// it would have been dropped. Walls that peak above about 290 have lines above the gate
    /// anyway.
    /// </summary>
    private static (bool[] Mask, long Added, int Dropped) FollowCrests(bool[] mask, float[] smooth,
        bool[]? footAnywhere, bool[]? flat, byte[] land, int width, int height, int radius, double barony, float sea,
        float gate, double fraction, double reachBaronies, double minBaronies)
    {
        int n = width * height;
        var owner = new int[n];
        var queue = new List<int>();

        // Walls, eight-connected, each with its peak.
        var peaks = new List<float> { 0f };
        var stack = new Stack<int>();
        for (int start = 0; start < n; start++)
        {
            if (!mask[start] || owner[start] != 0) continue;
            int id = peaks.Count;
            float top = float.MinValue;
            owner[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                queue.Add(p);
                top = MathF.Max(top, smooth[p]);
                int x = p % width, y = p / width;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int q = ny * width + nx;
                    if (mask[q] && owner[q] == 0) { owner[q] = id; stack.Push(q); }
                }
            }
            peaks.Add(top);
        }

        long added = 0;
        if (fraction > 0)
        {
            var steps = new int[n];
            int reach = (int)Math.Round(reachBaronies * 2 * radius);

            // The crest, from every wall at once.
            var crest = new List<int>();
            for (int head = 0; head < queue.Count; head++)
            {
                int p = queue[head];
                if (steps[p] >= reach) continue;
                int id = owner[p];
                float line = MathF.Max(gate, sea + (float)fraction * (peaks[id] - sea));
                int x = p % width, y = p / width;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int q = ny * width + nx;
                    if (owner[q] != 0 || land[q] == 0 || smooth[q] < line || (flat is not null && flat[q])) continue;
                    owner[q] = id;
                    steps[q] = steps[p] + 1;
                    queue.Add(q);
                    crest.Add(q);
                }
            }
            added += crest.Count;

            // Its flanks, down the foot ground, a barony's radius out.
            if (footAnywhere is not null)
            {
                foreach (int p in crest) steps[p] = 0;
                for (int head = 0; head < crest.Count; head++)
                {
                    int p = crest[head];
                    if (steps[p] >= radius) continue;
                    int id = owner[p];
                    int x = p % width, y = p / width;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int q = ny * width + nx;
                        if (owner[q] != 0 || land[q] == 0 || !footAnywhere[q]) continue;
                        owner[q] = id;
                        steps[q] = steps[p] + 1;
                        crest.Add(q);
                        added++;
                    }
                }
            }
        }

        var area = new long[peaks.Count];
        for (int i = 0; i < n; i++) area[owner[i]]++;
        long least = (long)(minBaronies * barony);
        int dropped = 0;
        for (int id = 1; id < peaks.Count; id++) if (area[id] < least) dropped++;

        var result = new bool[n];
        Parallel.For(0, height, y =>
        {
            for (int i = y * width, end = i + width; i < end; i++)
                result[i] = owner[i] != 0 && area[owner[i]] >= least;
        });
        return (result, added, dropped);
    }

    /// <summary>
    /// Land below the mountain line whose ruggedness is under <paramref name="rugged"/> times the
    /// map's median. Ruggedness is the slope of the smoothed terrain — the lie of the land at a
    /// barony's scale, not the erosion texture a single-pixel slope picks up — averaged over the
    /// (2<paramref name="r"/>+1)² square around each pixel, so a shelf reads flat as a whole rather
    /// than pixel by pixel.
    /// </summary>
    private static bool[] FlatGround(float[] smooth, float[] elevation, float mountainLine, byte[] land,
        int width, int height, int r, double rugged)
    {
        // Central differences, one-sided at the edges.
        var slope = new float[smooth.Length];
        Parallel.For(0, height, y =>
        {
            int up = y > 0 ? y - 1 : y, down = y < height - 1 ? y + 1 : y;
            float dyScale = down - up == 2 ? 0.5f : 1f;
            for (int x = 0; x < width; x++)
            {
                int left = x > 0 ? x - 1 : x, right = x < width - 1 ? x + 1 : x;
                float dxScale = right - left == 2 ? 0.5f : 1f;
                float dx = (smooth[y * width + right] - smooth[y * width + left]) * dxScale;
                float dy = (smooth[down * width + x] - smooth[up * width + x]) * dyScale;
                slope[y * width + x] = MathF.Sqrt(dx * dx + dy * dy);
            }
        });

        var sample = new List<float>();
        for (int i = 0; i < land.Length; i++) if (land[i] != 0) sample.Add(slope[i]);
        if (sample.Count == 0) return new bool[smooth.Length];
        var values = sample.ToArray();
        float line = (float)(rugged * Provinces.Select(values, values.Length / 2));

        var mean = BoxMean(slope, width, height, r);
        var flat = new bool[smooth.Length];
        Parallel.For(0, height, y =>
        {
            for (int i = y * width, end = i + width; i < end; i++)
                flat[i] = land[i] != 0 && elevation[i] < mountainLine && mean[i] < line;
        });
        return flat;
    }

    /// <summary>The mean over the (2r+1)² square around every pixel, edges mirrored.</summary>
    private static float[] BoxMean(float[] src, int width, int height, int r)
    {
        var rows = new float[src.Length];
        Parallel.For(0, height, y =>
        {
            int row = y * width;
            double sum = 0;
            for (int d = -r; d <= r; d++) sum += src[row + Mirror(d, width)];
            for (int x = 0; x < width; x++)
            {
                rows[row + x] = (float)sum;
                sum += src[row + Mirror(x + r + 1, width)] - src[row + Mirror(x - r, width)];
            }
        });

        const int band = 512;
        float area = (2 * r + 1) * (2 * r + 1);
        var result = new float[src.Length];
        Parallel.For(0, (width + band - 1) / band, b =>
        {
            int x0 = b * band, count = Math.Min(width, x0 + band) - x0;
            var sum = new double[count];
            for (int d = -r; d <= r; d++)
            {
                int row = Mirror(d, height) * width + x0;
                for (int x = 0; x < count; x++) sum[x] += rows[row + x];
            }
            for (int y = 0; y < height; y++)
            {
                int o = y * width + x0;
                int add = Mirror(y + r + 1, height) * width + x0, sub = Mirror(y - r, height) * width + x0;
                for (int x = 0; x < count; x++)
                {
                    result[o + x] = (float)(sum[x] / area);
                    sum[x] += rows[add + x] - rows[sub + x];
                }
            }
        });
        return result;
    }

    /// <summary>The lowest and highest value in each <paramref name="q"/>² block.</summary>
    private static (float[] Lowest, float[] Highest) Pool(float[] src, int width, int height, int q, int cw, int ch)
    {
        var lowest = new float[cw * ch];
        var highest = new float[cw * ch];
        Parallel.For(0, ch, cy =>
        {
            for (int cx = 0; cx < cw; cx++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int y = cy * q, ye = Math.Min(height, y + q); y < ye; y++)
                    for (int x = cx * q, xe = Math.Min(width, x + q); x < xe; x++)
                    {
                        float v = src[y * width + x];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                lowest[cy * cw + cx] = lo;
                highest[cy * cw + cx] = hi;
            }
        });
        return (lowest, highest);
    }

    /// <summary>A coarse field read back at every pixel, bilinearly between cell centres.</summary>
    private static float[] Upsample(float[] coarse, int cw, int ch, int q, int width, int height)
    {
        if (q == 1) return coarse;
        var result = new float[width * height];
        double half = (q - 1) / 2.0;
        Parallel.For(0, height, y =>
        {
            double fy = Math.Clamp((y - half) / q, 0, ch - 1);
            int y0 = (int)fy, y1 = Math.Min(ch - 1, y0 + 1);
            float ty = (float)(fy - y0);
            for (int x = 0; x < width; x++)
            {
                double fx = Math.Clamp((x - half) / q, 0, cw - 1);
                int x0 = (int)fx, x1 = Math.Min(cw - 1, x0 + 1);
                float tx = (float)(fx - x0);
                float top = coarse[y0 * cw + x0] + (coarse[y0 * cw + x1] - coarse[y0 * cw + x0]) * tx;
                float bottom = coarse[y1 * cw + x0] + (coarse[y1 * cw + x1] - coarse[y1 * cw + x0]) * tx;
                result[y * width + x] = top + (bottom - top) * ty;
            }
        });
        return result;
    }

    /// <summary>
    /// The core height cut that leaves the grown walls covering <paramref name="goal"/> pixels:
    /// ten halvings over the candidates' heights, as the prototype searched. The grown area only
    /// shrinks as the cut rises, so this converges on the lowest cut that keeps within the goal, to
    /// a thousandth of the candidates.
    /// </summary>
    private static float FootCut(bool[] plateau, bool[] candidate, float[] smooth, bool[] footGround,
        int candidates, double goal, int width, int height)
    {
        if (candidates == 0) return float.PositiveInfinity;
        var grower = new FootGrower(plateau, candidate, smooth, footGround, width, height);
        var values = (float[])grower.CandidateHeights.Clone();

        int lo = 0, hi = candidates - 1;
        for (int step = 0; step < 10; step++)
        {
            int mid = (lo + hi) / 2;
            if (grower.Run(Provinces.Select(values, mid)) > goal) lo = mid;
            else hi = mid;
        }
        return Provinces.Select(values, hi);
    }

    /// <summary>
    /// The cores — plateau ground, and candidates at or above <paramref name="cut"/> — spread
    /// eight-connected through <paramref name="footGround"/>.
    /// </summary>
    private static bool[] Grow(bool[] plateau, bool[] candidate, float[] smooth, float cut, bool[] footGround,
        int width, int height, out long area)
    {
        var grower = new FootGrower(plateau, candidate, smooth, footGround, width, height);
        area = grower.Run(cut);
        return grower.Wall;
    }

    /// <summary>
    /// The flood behind <see cref="Grow"/>, set up once so the cut search can run it ten times: the
    /// core pixels are gathered up front, and each run clears only what the last one reached
    /// instead of the whole raster.
    /// </summary>
    private sealed class FootGrower
    {
        private readonly int[] _plateau;
        private readonly int[] _candidates;
        private readonly bool[] _footGround;
        private readonly int _width, _height;
        private int[] _queue = new int[1 << 16];
        private int _reached;

        public float[] CandidateHeights { get; }
        public bool[] Wall { get; }

        public FootGrower(bool[] plateau, bool[] candidate, float[] smooth, bool[] footGround, int width, int height)
        {
            var plateauList = new List<int>();
            var candidateList = new List<int>();
            for (int i = 0; i < plateau.Length; i++)
            {
                if (plateau[i]) plateauList.Add(i);
                else if (candidate[i]) candidateList.Add(i);
            }
            _plateau = plateauList.ToArray();
            _candidates = candidateList.ToArray();
            CandidateHeights = new float[_candidates.Length];
            for (int k = 0; k < _candidates.Length; k++) CandidateHeights[k] = smooth[_candidates[k]];
            _footGround = footGround;
            _width = width;
            _height = height;
            Wall = new bool[plateau.Length];
        }

        /// <summary>Floods from the cores at <paramref name="cut"/>; returns the pixels reached.</summary>
        public long Run(float cut)
        {
            for (int k = 0; k < _reached; k++) Wall[_queue[k]] = false;
            int head = 0, tail = 0;
            foreach (int i in _plateau) Visit(i);
            for (int k = 0; k < _candidates.Length; k++)
                if (CandidateHeights[k] >= cut) Visit(_candidates[k]);

            while (head < tail)
            {
                int p = _queue[head++];
                int x = p % _width, y = p / _width;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = y + dy;
                    if (ny < 0 || ny >= _height) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        if (nx < 0 || nx >= _width) continue;
                        int k = ny * _width + nx;
                        if (!Wall[k] && _footGround[k]) Visit(k);
                    }
                }
            }
            _reached = tail;
            return tail;

            void Visit(int i)
            {
                if (Wall[i]) return;
                Wall[i] = true;
                if (tail == _queue.Length) Array.Resize(ref _queue, _queue.Length * 2);
                _queue[tail++] = i;
            }
        }
    }

    /// <summary>
    /// The lowest (or highest) value over the (2r+1)² square around every pixel, edges mirrored:
    /// separable, and each line done by van Herk's block prefix and suffix, so the cost does not
    /// grow with <paramref name="r"/>.
    /// </summary>
    private static float[] Extreme(float[] src, int width, int height, int r, bool max)
    {
        int w = 2 * r + 1;
        var rows = new float[src.Length];
        Parallel.For(0, height, () => (new float[width + 2 * r], new float[width + 2 * r], new float[width + 2 * r]),
            (y, _, buffers) =>
            {
                var (padded, prefix, suffix) = buffers;
                int row = y * width, m = width + 2 * r;
                for (int j = 0; j < m; j++) padded[j] = src[row + Mirror(j - r, width)];
                Blocks(padded, prefix, suffix, m, 1, w, max);
                for (int x = 0; x < width; x++)
                    rows[row + x] = max ? Math.Max(suffix[x], prefix[x + w - 1]) : Math.Min(suffix[x], prefix[x + w - 1]);
                return buffers;
            }, _ => { });

        // Down columns in bands, each band's rows laid side by side so every step is contiguous.
        const int band = 64;
        var result = new float[src.Length];
        Parallel.For(0, (width + band - 1) / band, b =>
        {
            int x0 = b * band, count = Math.Min(width, x0 + band) - x0, m = height + 2 * r;
            var padded = new float[m * count];
            var prefix = new float[m * count];
            var suffix = new float[m * count];
            for (int j = 0; j < m; j++)
                Array.Copy(rows, Mirror(j - r, height) * width + x0, padded, j * count, count);
            Blocks(padded, prefix, suffix, m, count, w, max);
            for (int y = 0; y < height; y++)
                for (int c = 0; c < count; c++)
                {
                    float a = suffix[y * count + c], z = prefix[(y + w - 1) * count + c];
                    result[y * width + x0 + c] = max ? Math.Max(a, z) : Math.Min(a, z);
                }
        });
        return result;
    }

    /// <summary>Running extremes within blocks of <paramref name="w"/> lines, forward into
    /// <paramref name="prefix"/> and backward into <paramref name="suffix"/>, over
    /// <paramref name="lines"/> lines of <paramref name="count"/> values each.</summary>
    private static void Blocks(float[] padded, float[] prefix, float[] suffix, int lines, int count, int w, bool max)
    {
        for (int j = 0; j < lines; j++)
        {
            int o = j * count;
            if (j % w == 0) Array.Copy(padded, o, prefix, o, count);
            else
                for (int c = 0; c < count; c++)
                {
                    float a = prefix[o - count + c], v = padded[o + c];
                    prefix[o + c] = max ? Math.Max(a, v) : Math.Min(a, v);
                }
        }
        for (int j = lines - 1; j >= 0; j--)
        {
            int o = j * count;
            if (j % w == w - 1 || j == lines - 1) Array.Copy(padded, o, suffix, o, count);
            else
                for (int c = 0; c < count; c++)
                {
                    float a = suffix[o + count + c], v = padded[o + c];
                    suffix[o + c] = max ? Math.Max(a, v) : Math.Min(a, v);
                }
        }
    }

    /// <summary>
    /// <paramref name="src"/> blurred with a Gaussian of <paramref name="sigma"/> px, truncated at
    /// 4σ with mirrored edges. With <paramref name="land"/> given, only land counts and the sea is
    /// taken as 0, so the coast sits low. With <paramref name="needed"/> given, only rows holding a
    /// needed pixel are finished, and only the rows those draw on are blurred across; everything
    /// else is left 0. Every sample is raised to at least <paramref name="floorAt"/> first.
    /// </summary>
    private static float[] Gaussian(float[] elevation, byte[]? land, bool[]? needed, int width, int height, double sigma,
        float floorAt = float.NegativeInfinity)
    {
        int r = (int)(4 * sigma + 0.5);
        var weights = new double[2 * r + 1];
        double total = 0;
        for (int d = -r; d <= r; d++) total += weights[d + r] = Math.Exp(-0.5 * d * d / (sigma * sigma));
        var kernel = new float[weights.Length];
        for (int d = 0; d < kernel.Length; d++) kernel[d] = (float)(weights[d] / total);

        var rowNeeded = new bool[height];
        Parallel.For(0, height, y =>
        {
            if (needed is null) { rowNeeded[y] = true; return; }
            for (int i = y * width, end = i + width; i < end; i++)
                if (needed[i]) { rowNeeded[y] = true; break; }
        });
        var rowUsed = new bool[height];
        for (int y = 0; y < height; y++)
            if (rowNeeded[y])
                for (int d = -r; d <= r; d++) rowUsed[Mirror(y + d, height)] = true;

        int lanes = Vector<float>.Count;
        var rows = new float[elevation.Length];
        Parallel.For(0, height, y =>
        {
            if (!rowUsed[y]) return;
            int row = y * width;
            var padded = new float[width + 2 * r + lanes];
            for (int j = 0; j < width + 2 * r; j++)
            {
                int k = row + Mirror(j - r, width);
                padded[j] = MathF.Max(land is null || land[k] != 0 ? elevation[k] : 0f, floorAt);
            }
            int x = 0;
            for (; x + lanes <= width; x += lanes)
            {
                var sum = Vector<float>.Zero;
                for (int d = 0; d < kernel.Length; d++) sum += new Vector<float>(padded, x + d) * kernel[d];
                sum.CopyTo(rows, row + x);
            }
            for (; x < width; x++)
            {
                float sum = 0;
                for (int d = 0; d < kernel.Length; d++) sum += padded[x + d] * kernel[d];
                rows[row + x] = sum;
            }
        });

        var result = new float[elevation.Length];
        Parallel.For(0, height, y =>
        {
            if (!rowNeeded[y]) return;
            var sum = new float[width];
            for (int d = -r; d <= r; d++)
            {
                int source = Mirror(y + d, height) * width;
                float weight = kernel[d + r];
                int x = 0;
                for (; x + lanes <= width; x += lanes)
                    (new Vector<float>(sum, x) + new Vector<float>(rows, source + x) * weight).CopyTo(sum, x);
                for (; x < width; x++) sum[x] += rows[source + x] * weight;
            }
            Array.Copy(sum, 0, result, y * width, width);
        });
        return result;
    }

    private static int Mirror(int i, int n) => i < 0 ? -i - 1 : i >= n ? 2 * n - i - 1 : i;

    private static bool[] Dilate(bool[] src, int width, int height, int r) => DiscAny(src, width, height, r, false);

    /// <summary>Erosion by a disc, with everything off the raster counted as unset.</summary>
    private static bool[] Erode(bool[] src, int width, int height, int r)
    {
        var inverse = new bool[src.Length];
        Parallel.For(0, height, y =>
        {
            for (int i = y * width, end = i + width; i < end; i++) inverse[i] = !src[i];
        });
        var hit = DiscAny(inverse, width, height, r, true);
        Parallel.For(0, height, y =>
        {
            for (int i = y * width, end = i + width; i < end; i++) hit[i] = !hit[i];
        });
        return hit;
    }

    /// <summary>
    /// Whether any set pixel lies within the disc x² + y² ≤ r² around each pixel, with pixels off
    /// the raster counted as <paramref name="outside"/>. Each row first records how far each pixel
    /// is from the nearest set pixel along the row, so the disc test is one lookup per disc row.
    /// </summary>
    private static bool[] DiscAny(bool[] src, int width, int height, int r, bool outside)
    {
        byte cap = (byte)Math.Min(255, r + 1);
        var along = new byte[src.Length];
        Parallel.For(0, height, y =>
        {
            int row = y * width;
            int last = outside ? -1 : int.MinValue / 2;
            for (int x = 0; x < width; x++)
            {
                if (src[row + x]) last = x;
                along[row + x] = (byte)Math.Min(cap, x - last);
            }
            last = outside ? width : int.MaxValue / 2;
            for (int x = width - 1; x >= 0; x--)
            {
                if (src[row + x]) last = x;
                along[row + x] = (byte)Math.Min(along[row + x], Math.Min(cap, last - x));
            }
        });

        var span = new int[r + 1];
        for (int d = 0; d <= r; d++) span[d] = (int)Math.Floor(Math.Sqrt(r * r - d * d));

        // Row by row: each disc row is one contiguous sweep of the row it covers.
        var result = new bool[src.Length];
        Parallel.For(0, height, y =>
        {
            int row = y * width;
            for (int dy = -r; dy <= r; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height)
                {
                    if (!outside) continue;
                    Array.Fill(result, true, row, width);
                    return;
                }
                int source = ny * width;
                byte reach = (byte)span[Math.Abs(dy)];
                for (int x = 0; x < width; x++)
                    if (along[source + x] <= reach) result[row + x] = true;
            }
        });
        return result;
    }

    /// <summary>
    /// Adds to <paramref name="walls"/> every land pixel at least <paramref name="line"/> steep that
    /// the walls reach through ground as steep, four-connected, and returns how many it added.
    ///
    /// Steepness is the gradient of the terrain blurred at σ 1.5 px, about the footprint of a
    /// holding, which is the scale the line was measured at on vanilla; the single-pixel slope
    /// would read every eroded gully as a cliff. Water is taken at sea level before the blur, so
    /// the drop to the seabed does not make every coast a cliff. The line is per pixel, which is
    /// per world unit, because CK3 takes the world's width from the province map.
    ///
    /// Only ground joined to a wall is taken. A cliff on its own — the rim of a passable plateau,
    /// the side of a gorge — would wall as a thin ring, the shape the opening in the clean is there
    /// to remove.
    /// </summary>
    private static long ExtendDownCliffs(bool[] walls, float[] elevation, byte[] land, float sea, float line,
        int width, int height)
    {
        var blurred = Gaussian(elevation, null, null, width, height, 1.5, floorAt: sea);

        // Central differences over two pixels, one-sided at the edges, as the vanilla measure took them.
        var cliff = new bool[walls.Length];
        Parallel.For(0, height, y =>
        {
            int up = y > 0 ? y - 1 : y, down = y < height - 1 ? y + 1 : y;
            float dyScale = down - up == 2 ? 0.5f : 1f;
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (land[i] == 0 || walls[i]) continue;
                int left = x > 0 ? x - 1 : x, right = x < width - 1 ? x + 1 : x;
                float dxScale = right - left == 2 ? 0.5f : 1f;
                float dx = (blurred[y * width + right] - blurred[y * width + left]) * dxScale;
                float dy = (blurred[down * width + x] - blurred[up * width + x]) * dyScale;
                cliff[i] = dx * dx + dy * dy >= line * line;
            }
        });

        var stack = new Stack<int>();
        long added = 0;
        for (int i = 0; i < walls.Length; i++)
        {
            if (!walls[i]) continue;
            int x = i % width;
            Take(x > 0, i - 1);
            Take(x + 1 < width, i + 1);
            Take(i >= width, i - width);
            Take(i + width < walls.Length, i + width);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                int kx = k % width;
                Take(kx > 0, k - 1);
                Take(kx + 1 < width, k + 1);
                Take(k >= width, k - width);
                Take(k + width < walls.Length, k + width);
            }
        }
        return added;

        void Take(bool inside, int k)
        {
            if (!inside || !cliff[k]) return;
            cliff[k] = false;
            walls[k] = true;
            added++;
            stack.Push(k);
        }
    }

    /// <summary>
    /// Turns into wall every four-connected pocket of passable land the mask cuts off: one smaller
    /// than <paramref name="limit"/> pixels that the mask encloses, or one smaller than half that
    /// which the mask pins against the water. Floods start only beside the mask and stop the moment
    /// they reach ground already known to be open or the limit, so the open country the mask borders
    /// is walked no further than a barony from any wall.
    ///
    /// The pocket against the water is the ledge a wall leaves where its range falls into the sea.
    /// The foot of a coastal range is often a low, flat apron a few pixels wide, which is not
    /// mountain ground, so the wall stops short of the shore; each ledge it cuts off at both ends
    /// is a region of its own, and the partition gives every region a barony. On an inland-sea
    /// world that was 18 baronies of 42 to 361 px against a barony of 3192, most a few pixels
    /// wide, each with a holding on a strip of beach under a cliff. The limit there is half the
    /// enclosed one because a pocket by the water may be the last passable ground of a small
    /// coast or island rather than a hole; half a barony is also where the cut drops a wall
    /// piece as too small to stand.
    /// </summary>
    private static void FillHoles(bool[] mask, byte[] land, int width, int height, int limit)
    {
        const byte Open = 1, Seen = 2;
        int wetLimit = (limit + 1) / 2;
        var state = new byte[mask.Length];
        var visited = new List<int>();
        var stack = new Stack<int>();
        bool wet = false;

        bool Passable(int i) => land[i] != 0 && !mask[i];

        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start]) continue;
            int sx = start % width;
            Try(sx > 0 ? start - 1 : -1);
            Try(sx + 1 < width ? start + 1 : -1);
            Try(start - width);
            Try(start + width < mask.Length ? start + width : -1);
        }
        return;

        void Try(int seed)
        {
            if (seed < 0 || state[seed] != 0 || !Passable(seed)) return;

            visited.Clear();
            stack.Clear();
            state[seed] = Seen;
            stack.Push(seed);
            bool open = false;
            wet = false;
            while (stack.Count > 0 && !open)
            {
                int i = stack.Pop();
                visited.Add(i);
                if (visited.Count >= (wet ? wetLimit : limit)) { open = true; break; }
                int x = i % width;
                open |= Step(x > 0, i - 1) | Step(x + 1 < width, i + 1)
                      | Step(i >= width, i - width) | Step(i + width < mask.Length, i + width);
            }

            // Whatever the flood reached is one pocket, so it is all open or all hole. Pixels still
            // on the stack were reached too, and may be what takes a wet pocket past its limit.
            while (stack.Count > 0) visited.Add(stack.Pop());
            if (wet && visited.Count >= wetLimit) open = true;
            foreach (int i in visited)
            {
                if (open) state[i] = Open;
                else mask[i] = true;
            }
        }

        bool Step(bool inside, int k)
        {
            if (!inside) return false;
            if (land[k] == 0) { wet = true; return false; }   // the sea, a lake or a river channel
            if (mask[k]) return false;
            if (state[k] == Open) return true;
            if (state[k] == Seen) return false;
            state[k] = Seen;
            stack.Push(k);
            return false;
        }
    }

    /// <summary>Four-connected components of the set pixels; labels from 1, with each
    /// component's size at index label − 1.</summary>
    private static (int[] Labels, List<int> Sizes) Label(bool[] set, int width, int height)
    {
        int n = width * height;
        var labels = new int[n];
        var sizes = new List<int>();
        var stack = new int[1024];
        for (int start = 0; start < n; start++)
        {
            if (labels[start] != 0 || !set[start]) continue;
            int id = sizes.Count + 1, size = 0, top = 0;
            labels[start] = id;
            stack[top++] = start;
            while (top > 0)
            {
                int i = stack[--top];
                size++;
                int x = i % width;
                if (top + 4 > stack.Length) Array.Resize(ref stack, stack.Length * 2);
                if (x > 0 && labels[i - 1] == 0 && set[i - 1]) { labels[i - 1] = id; stack[top++] = i - 1; }
                if (x + 1 < width && labels[i + 1] == 0 && set[i + 1]) { labels[i + 1] = id; stack[top++] = i + 1; }
                if (i >= width && labels[i - width] == 0 && set[i - width]) { labels[i - width] = id; stack[top++] = i - width; }
                if (i + width < n && labels[i + width] == 0 && set[i + width]) { labels[i + width] = id; stack[top++] = i + width; }
            }
            sizes.Add(size);
        }
        return (labels, sizes);
    }
}
