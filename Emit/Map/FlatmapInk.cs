// Emit/Map/FlatmapInk.cs
namespace Ck3MapGen.Emit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

using Pt = (double X, double Y);

/// <summary>
/// Pen work on the parchment flat map, drawn over the finished picture before it is encoded.
///
/// Two independent halves, each behind its own setting:
///
/// * <b>Roads</b> (<see cref="Config.MapConfig.FlatmapRoads"/>): the route network from
///   <see cref="Routes"/> inked in red-brown. Trunk routes are heavier, roads into wilderness are
///   dashed tracks, a road's stretch over water (a strait or river crossing) is dotted like a
///   ferry, sea lanes are faint dotted lines on the water only, and each market is a small mark —
///   kingdom seats ringed.
/// * <b>Flourishes</b> (<see cref="Config.MapConfig.FlatmapFlourishes"/>): a compass rose in the
///   widest open ocean with the thirty-two rhumb lines ruled from it across the open sea, hand
///   hatching over the unsettled wilderness, and a graduated border around the sheet.
/// * <b>Hachures</b> (<see cref="Config.MapConfig.FlatmapHachures"/>): slope strokes
///   over the impassable mountains and a ")(" mark on each pass through them.
/// * <b>Coast</b> (<see cref="Config.MapConfig.FlatmapCoastInk"/>): the coastline and the
///   waterlines ruled off it, drawn here at the map's scale instead of by the parchment pass.
///
/// Everything is drawn as ink: a coverage layer is built up shape by shape, taking the maximum
/// where shapes overlap so a crossing or a joint never prints darker than the line, and is then
/// multiplied into the parchment so the paper grain shows through the ink. The rose's medallion
/// lifts the paper instead, so it reads as drawn on the page rather than on the sea glaze.
///
/// Line widths are authored for a 4096-wide flat map and scale with the map's width. Nothing here
/// takes an Rng: the hatching's broken strokes hash their own positions and the world seed.
///
/// Sea creatures were tried here and taken out on 2026-09-27: a hand-drawn serpent, cut-outs of
/// Olaus Magnus's and Sebastian Münster's woodcut monsters, and game-icons.net silhouettes in
/// outline. None sat well on a map this plain.
/// </summary>
public static class FlatmapInk
{
    private static readonly (int R, int G, int B) Sepia = (70, 50, 34);
    private static readonly (int R, int G, int B) RoadInk = (122, 46, 32);
    private static readonly (int R, int G, int B) Vermilion = (176, 56, 42);

    /// <summary>
    /// How far in from the edge of the sheet the frame's outer rule stands, in flat-map pixels.
    /// With the edge feather on, the paper margin outside it is what dissolves into the table
    /// (<see cref="MapGraphicsWriter"/> writes that fade into the surround mask), so the rule
    /// stands well in and stays crisp; without it, the rule hugs the edge.
    /// </summary>
    public static double FrameInset(int w, bool feather) => (feather ? FeatherMargin : BorderOuter) * Scale(w);

    /// <summary>Line widths are authored for a 4096-wide flat map.</summary>
    public static double Scale(int w) => Math.Max(0.5, w / 4096.0);

    /// <summary>Draws the enabled halves onto <paramref name="bgra"/> in place and returns a line for the log.</summary>
    public static string Draw(byte[] bgra, int w, int h, bool[] land, ProvinceMap provinces, int[] order,
        RouteNetwork? routes, WildernessMap? wilderness, int seed, bool roads, bool flourishes, bool feather = false,
        float[]? elevation = null, bool hachures = false, IReadOnlyList<Landmark>? landmarks = null, string? gameDir = null,
        bool coast = false)
    {
        var cv = new Canvas(bgra, w, h);
        double k = Scale(w);
        double inset = FrameInset(w, feather);
        var notes = new List<string>();

        // Placed before anything is drawn so the sea lanes can keep off it.
        var (rose, roseRadius) = flourishes ? PlaceRose(land, w, h, k, inset) : (null, 0);

        bool Occluded(Pt p) => rose is { } rc && Dist(p, rc) < roseRadius * 1.08;

        // First: the sea's own linework, under everything ruled or lettered across it.
        if (coast)
            notes.Add(InkCoast(cv, land, k, seed, inset + (BorderBand + 3) * k, Occluded));

        if (flourishes && wilderness is not null)
        {
            int hatched = Hatch(cv, provinces, order, wilderness, k, seed);
            if (hatched > 0) notes.Add($"hatched {hatched} wilderness counties");
        }

        // Under the roads, so a road through a pass reads over the shading.
        if (hachures && elevation is not null)
        {
            var (strokes, pixels) = Hachure(cv, provinces, elevation, k, seed);
            if (strokes > 0) notes.Add($"hachured the mountains ({strokes} strokes over {pixels} px)");
        }

        // Under the sea lanes, so a lane reads over the chart's grid rather than tangled in it.
        if (rose is { } origin)
        {
            DrawRhumbs(cv, OpenWater(land, w, h, inset + (BorderBand + 3) * k), origin, roseRadius, k);
            notes.Add("rhumb lines");
        }

        if (roads && routes is not null && routes.Edges.Count > 0)
            notes.Add(InkRoads(cv, land, provinces, order, routes, k, Occluded));

        if (hachures)
        {
            int passes = MarkPasses(cv, provinces, k);
            if (passes > 0) notes.Add($"{passes} pass marks");
        }

        if (rose is { } centre)
        {
            DrawRose(cv, centre, roseRadius);
            notes.Add("compass rose");
        }

        // Over the hachures and roads, which the paper lifted round each name keeps clear of it.
        if (flourishes && landmarks is { Count: > 0 })
        {
            int lettered = LetterLandmarks(cv, landmarks, gameDir, k, Occluded);
            if (lettered > 0) notes.Add($"lettered {lettered} landmark(s)");
        }

        // Last, over everything that runs to the edge.
        if (flourishes)
        {
            DrawBorder(cv, h, k, inset);
            notes.Add(feather ? "border (inset for the edge feather)" : "border");
        }

        return notes.Count == 0 ? "nothing to ink" : string.Join(", ", notes);
    }

    // ------------------------------------------------------------------------------------------
    // Landmark names

    /// <summary>
    /// Each landmark's name (see <see cref="Landmark"/>), lettered along its line in spaced italic,
    /// the way an old map names a range or a sea: small beside the realm names, in the coastline's
    /// sepia a little short of full strength, with the paper lifted just round the letters so they
    /// read over hachures and roads. The type is the game's own italic (Gitan), so it sits with the
    /// rest of the map's text rather than competing with the realm names' script.
    ///
    /// The size is the largest that fits in most of the line, capped by kind: a crater's name is
    /// set a little larger than a line's, which has more length to spread along. A name that would
    /// come out too small to read is left off rather than squeezed.
    /// </summary>
    private static int LetterLandmarks(Canvas cv, IReadOnlyList<Landmark> landmarks, string? gameDir, double k,
        Func<Pt, bool> occluded)
    {
        if (LetteringFont(gameDir) is not { } family) return 0;
        const float tracking = 0.28f;
        int lettered = 0;

        foreach (var landmark in landmarks)
        {
            var line = landmark.Label.Select(p => (Pt)(p.X * cv.W, p.Y * cv.H)).ToList();
            if (line.Count < 2) continue;
            if (line[^1].X < line[0].X) line.Reverse();

            // A sea name is placed without knowing where the rose went; one that would run into it
            // is left off rather than printed over it.
            if (line.Any(occluded)) continue;
            double length = 0;
            for (int i = 1; i < line.Count; i++) length += Dist(line[i - 1], line[i]);

            // Measured at a round size, then scaled: advance grows in step with the size.
            var probe = new SixLabors.Fonts.TextOptions(family.CreateFont(100f)) { Tracking = tracking };
            double per100 = SixLabors.Fonts.TextMeasurer.MeasureAdvance(landmark.Name, probe).Width;
            bool round = landmark.Kind is "basin" or "sea";
            double cap = (round ? 44 : 34) * k;
            double size = Math.Min(cap, 100 * 0.8 * length / Math.Max(1, per100));
            if (size < 10 * k) continue;

            // Then spaced out along the line, as an old map spreads a range's name along the range:
            // a long feature's letters stand wide apart over half its length, an arch's closer.
            double natural = per100 * size / 100;
            double wanted = Math.Max(natural, (round ? 0.7 : 0.5) * length);
            int gaps = Math.Max(1, landmark.Name.Length - 1);
            double spread = Math.Min((wanted - natural) / (gaps * size), round ? 0.5 : 2.2);

            var options = new SixLabors.Fonts.TextOptions(family.CreateFont((float)size))
            {
                Tracking = tracking + (float)spread,
                VerticalAlignment = SixLabors.Fonts.VerticalAlignment.Center,
            };
            double width = SixLabors.Fonts.TextMeasurer.MeasureAdvance(landmark.Name, options).Width;
            var baseline = Stretch(line, (length - width) / 2, (length + width) / 2 + size);
            var path = new SixLabors.ImageSharp.Drawing.Path(new SixLabors.ImageSharp.Drawing.LinearLineSegment(
                baseline.Select(p => new SixLabors.ImageSharp.PointF((float)p.X, (float)p.Y)).ToArray()));

            // One set of rings per glyph, filled even-odd so the counters of an "a" or an "o" stay open.
            var glyphs = SixLabors.ImageSharp.Drawing.Text.TextBuilder.GenerateGlyphs(landmark.Name, path, options)
                .Select(g => g.Paths.SelectMany(p => p.Flatten()).Select(r => r.Points.ToArray().Select(p => (Pt)(p.X, p.Y)).ToList()).ToList())
                .Where(g => g.Count > 0)
                .ToList();
            if (glyphs.Count == 0) continue;

            // The paper lifted round the letters keeps a name legible over hachures and roads. Open
            // water has nothing under it to fight, and on the sea glaze the lift read as a pale
            // outline round every letter, so a sea's name is plain ink.
            if (landmark.Baronies.Length > 0)
            {
                foreach (var rings in glyphs)
                {
                    cv.FillEvenOdd(rings);
                    foreach (var ring in rings) cv.Stroke([.. ring, ring[0]], 2.4 * k);
                }
                cv.Lift(0.3);
            }
            foreach (var rings in glyphs) cv.FillEvenOdd(rings);
            cv.Ink(Sepia, 0.78);
            lettered++;
        }
        return lettered;
    }

    /// <summary>The stretch of <paramref name="line"/> between two distances along it.</summary>
    private static List<Pt> Stretch(List<Pt> line, double from, double to)
    {
        var part = new List<Pt>();
        double walked = 0;
        for (int i = 1; i < line.Count; i++)
        {
            double step = Dist(line[i - 1], line[i]);
            double a = walked, b = walked + step;
            walked = b;
            if (b < from || a > to || step <= 0) continue;
            if (part.Count == 0) part.Add(Lerp(line[i - 1], line[i], Math.Clamp((from - a) / step, 0, 1)));
            part.Add(Lerp(line[i - 1], line[i], Math.Clamp((to - a) / step, 0, 1)));
        }
        return part;
    }

    /// <summary>The game's italic if the install has it, a Windows italic serif if not.</summary>
    private static SixLabors.Fonts.FontFamily? LetteringFont(string? gameDir)
    {
        if (gameDir is not null)
        {
            string path = System.IO.Path.Combine(gameDir, "fonts", "Gitan", "GitanLatin-Italic.otf");
            if (System.IO.File.Exists(path))
            {
                try { return new SixLabors.Fonts.FontCollection().Add(path); }
                catch (Exception e) when (e is System.IO.IOException or SixLabors.Fonts.InvalidFontFileException) { }
            }
        }
        foreach (string name in new[] { "Georgia", "Book Antiqua", "Cambria" })
            if (SixLabors.Fonts.SystemFonts.TryGet(name, out var family)) return family;
        return null;
    }

    // ------------------------------------------------------------------------------------------
    // Roads

    private static string InkRoads(Canvas cv, bool[] land, ProvinceMap provinces, int[] order,
        RouteNetwork routes, double k, Func<Pt, bool> occluded)
    {
        var pos = ProvincePositions(provinces, order);
        int w = cv.W, h = cv.H;

        bool IsLand(Pt p)
        {
            int x = (int)p.X, y = (int)p.Y;
            return x >= 0 && y >= 0 && x < w && y < h && land[y * w + x];
        }

        // Sea lanes first and faintest: dots on open water only, so a lane starts at the shore
        // rather than at the port's province centre inland, and stops short of the flourishes.
        int lanes = 0;
        foreach (var e in routes.Edges.Where(e => e.Kind == RouteKind.Sea))
        {
            var path = PathOf(e, pos);
            if (path.Count < 2) continue;
            lanes++;
            int spacing = Math.Max(3, (int)Math.Round((e.Primary ? 6.0 : 7.5) * k));
            double r = (e.Primary ? 0.95 : 0.72) * k;
            for (int i = spacing / 2; i < path.Count; i += spacing)
                if (!IsLand(path[i]) && !occluded(path[i])) cv.Disc(path[i], r);
        }
        cv.Ink(Sepia, 0.55);

        int roadCount = 0, trunk = 0, tracks = 0, ferries = 0;
        foreach (var e in routes.Edges.Where(e => e.Kind == RouteKind.Land))
        {
            var path = PathOf(e, pos);
            if (path.Count < 2) continue;
            roadCount++;
            if (e.Primary) trunk++;

            bool wild = !e.Primary && (routes.Hubs[e.A].Wilderness || routes.Hubs[e.B].Wilderness);
            if (wild) tracks++;
            double r = e.Primary ? 1.3 * k : 0.62 * k;

            bool ferried = false;
            foreach (var (run, onLand) in Runs(path, IsLand, minWater: (int)Math.Ceiling(3 * k)))
            {
                if (!onLand)
                {
                    ferried = true;
                    Dotted(cv, run, Math.Max(3, (int)Math.Round(3.4 * k)), r + 0.3 * k);
                }
                else if (wild) Dashed(cv, run, 5.5 * k, 3.5 * k, r);
                else cv.Stroke(run, r);
            }
            if (ferried) ferries++;
        }

        // Markets on top of the roads that meet at them: kingdom seats ringed, wild ones hollow.
        foreach (var hub in routes.Hubs)
        {
            var p = pos[hub.ProvinceId];
            if (double.IsNaN(p.X)) continue;
            if (hub.KingdomSeat)
            {
                cv.Ring(p, 3.6 * k, 0.6 * k);
                cv.Disc(p, 1.6 * k);
            }
            else if (hub.Wilderness) cv.Ring(p, 1.8 * k, 0.45 * k);
            else cv.Disc(p, 1.8 * k);
        }
        cv.Ink(RoadInk, 0.88);

        return $"inked {roadCount} roads ({trunk} trunk, {tracks} wild tracks, {ferries} ferried), {lanes} sea lanes, {routes.Hubs.Count} markets";
    }

    private static Pt[] ProvincePositions(ProvinceMap provinces, int[] order)
    {
        var pos = new Pt[provinces.Count + 1];
        Array.Fill(pos, (double.NaN, double.NaN));
        for (int label = 0; label < order.Length && label < provinces.Seeds.Count; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= provinces.Count)
                pos[id] = (provinces.Seeds[label].X + 0.5, provinces.Seeds[label].Y + 0.5);
        }
        return pos;
    }

    /// <summary>
    /// A route's line: its provinces' centres, smoothed into a pen stroke and resampled at one
    /// pixel so dashes and dots can be spaced by index.
    /// </summary>
    private static List<Pt> PathOf(RouteEdge e, Pt[] pos)
    {
        var raw = new List<Pt>(e.Provinces.Count);
        foreach (int id in e.Provinces)
        {
            if (id < 1 || id >= pos.Length || double.IsNaN(pos[id].X)) continue;
            if (raw.Count > 0 && raw[^1] == pos[id]) continue;
            raw.Add(pos[id]);
        }
        return raw.Count < 2 ? raw : Resample(Chaikin(raw, 3), 1.0);
    }

    /// <summary>
    /// Splits a resampled path into land and water runs. A water run shorter than
    /// <paramref name="minWater"/> is a corner clipped off a bay and is kept as land.
    /// </summary>
    private static IEnumerable<(List<Pt> Run, bool OnLand)> Runs(List<Pt> path, Func<Pt, bool> isLand, int minWater)
    {
        var flags = path.Select(isLand).ToArray();
        for (int i = 0; i < flags.Length;)
        {
            int j = i;
            while (j < flags.Length && flags[j] == flags[i]) j++;
            if (!flags[i] && j - i < minWater && i > 0 && j < flags.Length)
                for (int m = i; m < j; m++) flags[m] = true;
            i = j;
        }

        int start = 0;
        for (int i = 1; i <= path.Count; i++)
        {
            if (i < path.Count && flags[i] == flags[start]) continue;
            // Each run carries the next run's first point so the two join without a gap.
            int end = Math.Min(i, path.Count - 1);
            yield return (path.GetRange(start, end - start + 1), flags[start]);
            start = i;
        }
    }

    private static void Dashed(Canvas cv, List<Pt> run, double on, double off, double r)
    {
        double period = on + off;
        var dash = new List<Pt>();
        for (int i = 0; i < run.Count; i++)
        {
            if (i % period < on) dash.Add(run[i]);
            else if (dash.Count > 0) { cv.Stroke(dash, r); dash.Clear(); }
        }
        if (dash.Count > 0) cv.Stroke(dash, r);
    }

    private static void Dotted(Canvas cv, List<Pt> run, int spacing, double r)
    {
        for (int i = spacing / 2; i < run.Count; i += spacing) cv.Disc(run[i], r);
    }

    // ------------------------------------------------------------------------------------------
    // Coast

    /// <summary>
    /// At k = 1, for each waterline out from the coast: how far out it stands, its half-width,
    /// how dark it prints, and how much of it the pen leaves out (a threshold on the break noise,
    /// whose values bunch round 0.5, so 0.3 already lifts the pen for a good part of the line).
    /// </summary>
    private static readonly (double At, double Half, double Strength, double Gap)[] Waterlines =
    [
        (5.5, 0.5, 0.55, 0.28),
        (12, 0.45, 0.42, 0.36),
        (20, 0.4, 0.3, 0.42),
        (30, 0.36, 0.2, 0.47),
    ];

    /// <summary>
    /// At k = 1: water narrower than twice this — a river, a strait — keeps its coastline but gets
    /// no waterlines, which would crowd it.
    /// </summary>
    private const double WaterlineOpen = 14;

    /// <summary>
    /// The coastline, and waterlines ruled parallel to it out to sea the way an engraver shaded
    /// the water off a coast: each line further out than the last by a wider step, finer and
    /// fainter, wandering a little and more broken the further it stands from land. Measured on
    /// an exact distance from the land, so the lines keep their width and spacing at any map size
    /// and bend round a headland instead of squaring off. The coast is inked everywhere (the
    /// border's paper covers it at the edge); the waterlines stop at the frame's inner rule and
    /// keep off the rose.
    /// </summary>
    private static string InkCoast(Canvas cv, bool[] land, double k, int seed, double frame, Func<Pt, bool> occluded)
    {
        int w = cv.W, h = cv.H;
        var d2 = LandDistanceSquared(land, w, h);

        // The coastline, on the water side of the land's edge.
        double coastWidth = 1.1 * k;
        cv.TouchAll();
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (land[i]) continue;
                double e = Math.Sqrt(d2[i]) - 0.5;
                if (e <= coastWidth + 0.5) cv.Set(i, Math.Clamp(coastWidth + 0.5 - e, 0, 1));
            }
        });
        cv.Ink(Sepia, 0.75);

        // The waterlines are measured on a blurred copy of the distance, so they round off the
        // pixel coast's jags instead of echoing them.
        var off = new float[w * h];
        Parallel.For(0, off.Length, i => off[i] = land[i] ? 0 : (float)Math.Sqrt(d2[i]) - 0.5f);
        int soften = Math.Max(1, (int)Math.Round(2.5 * k));
        var smooth = BoxBlur(BoxBlur(off, w, h, soften), w, h, soften);

        // Open water: within reach of somewhere a full WaterlineOpen from every shore. A pocket
        // where a river widens has a little of that too, and would get a ring of its own, so deep
        // water counts only in stretches of some size.
        double open = WaterlineOpen * k;
        var deep = new bool[w * h];
        Parallel.For(0, deep.Length, i => deep[i] = off[i] >= open);
        DropSmall(deep, w, h, (long)(4 * open * open));
        var toDeep = LandDistanceSquared(deep, w, h);

        double reach = (Waterlines[^1].At + 4) * k;
        int f = (int)Math.Ceiling(frame);
        long wander = seed * 7919L + 1, breaks = seed * 104729L + 2;
        cv.TouchAll();
        Parallel.For(f, h - f, y =>
        {
            for (int x = f; x < w - f; x++)
            {
                int i = y * w + x;
                if (land[i] || toDeep[i] >= open * open) continue;
                double e = smooth[i];
                if (e > reach) continue;
                // Tapered, not cut, where the water narrows.
                double taper = SmoothStep(open, open - 4 * k, Math.Sqrt(toDeep[i]));

                // One wander for all the lines, growing outward, so they drift together and never cross.
                double drift = ValueNoise(x / (22 * k), y / (22 * k), wander) - 0.5;
                double best = 0;
                for (int r = 0; r < Waterlines.Length; r++)
                {
                    var (at, half, strength, gap) = Waterlines[r];
                    double target = (at + drift * (0.8 + 0.6 * r)) * k;
                    double c = Math.Clamp(half * k + 0.5 - Math.Abs(e - target), 0, 1);
                    if (c <= 0) continue;
                    double pen = ValueNoise(x / (14 * k), y / (14 * k), breaks + r);
                    c *= SmoothStep(gap - 0.04, gap + 0.04, pen) * strength;
                    if (c > best) best = c;
                }
                if (best > 0 && !occluded((x + 0.5, y + 0.5))) cv.Set(i, best * taper);
            }
        });
        cv.Ink(Sepia, 1.0);

        return $"coastline and {Waterlines.Length} waterlines";
    }

    /// <summary>Clears every connected patch of <paramref name="mask"/> smaller than <paramref name="minArea"/> pixels.</summary>
    private static void DropSmall(bool[] mask, int w, int h, long minArea)
    {
        var seen = new bool[mask.Length];
        var queue = new Queue<int>();
        var members = new List<int>();
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || seen[start]) continue;
            seen[start] = true;
            queue.Enqueue(start);
            members.Clear();
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                members.Add(i);
                int x = i % w, y = i / w;
                if (x > 0 && mask[i - 1] && !seen[i - 1]) { seen[i - 1] = true; queue.Enqueue(i - 1); }
                if (x < w - 1 && mask[i + 1] && !seen[i + 1]) { seen[i + 1] = true; queue.Enqueue(i + 1); }
                if (y > 0 && mask[i - w] && !seen[i - w]) { seen[i - w] = true; queue.Enqueue(i - w); }
                if (y < h - 1 && mask[i + w] && !seen[i + w]) { seen[i + w] = true; queue.Enqueue(i + w); }
            }
            if (members.Count < minArea)
                foreach (int i in members) mask[i] = false;
        }
    }

    /// <summary>
    /// Squared Euclidean distance from each pixel's centre to the nearest land pixel's centre (0 on
    /// land, infinite on a map with none): Felzenszwalb and Huttenlocher's two-pass transform,
    /// down the columns and then along the rows.
    /// </summary>
    private static float[] LandDistanceSquared(bool[] land, int w, int h)
    {
        var d2 = new float[w * h];
        Parallel.For(0, w, x =>
        {
            var fc = new double[h]; var dc = new double[h]; var v = new int[h]; var z = new double[h + 1];
            for (int y = 0; y < h; y++) fc[y] = land[y * w + x] ? 0 : double.PositiveInfinity;
            Transform1D(fc, dc, v, z, h);
            for (int y = 0; y < h; y++) d2[y * w + x] = (float)dc[y];
        });
        Parallel.For(0, h, y =>
        {
            var fr = new double[w]; var dr = new double[w]; var v = new int[w]; var z = new double[w + 1];
            for (int x = 0; x < w; x++) fr[x] = d2[y * w + x];
            Transform1D(fr, dr, v, z, w);
            for (int x = 0; x < w; x++) d2[y * w + x] = (float)dr[x];
        });
        return d2;
    }

    /// <summary>The lower envelope of the parabolas rooted at each finite sample of <paramref name="f"/>.</summary>
    private static void Transform1D(double[] f, double[] d, int[] v, double[] z, int n)
    {
        int k = -1;
        for (int q = 0; q < n; q++)
        {
            if (double.IsPositiveInfinity(f[q])) continue;
            double s = double.NegativeInfinity;
            while (k >= 0)
            {
                s = (f[q] + (double)q * q - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * (q - v[k]));
                if (s > z[k]) break;
                k--;
            }
            k++;
            v[k] = q;
            z[k] = k == 0 ? double.NegativeInfinity : s;
        }
        if (k < 0)
        {
            Array.Fill(d, double.PositiveInfinity, 0, n);
            return;
        }
        z[k + 1] = double.PositiveInfinity;
        int j = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[j + 1] < q) j++;
            double dq = q - v[j];
            d[q] = dq * dq + f[v[j]];
        }
    }

    /// <summary>Smoothly interpolated lattice noise in 0..1, one lattice cell per unit.</summary>
    private static double ValueNoise(double x, double y, long seed)
    {
        long cx = (long)Math.Floor(x), cy = (long)Math.Floor(y);
        double fx = x - cx, fy = y - cy;
        double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        double Corner(long ax, long ay) => Hash01(seed, ax * 1_000_003L + ay);
        double top = Corner(cx, cy) + (Corner(cx + 1, cy) - Corner(cx, cy)) * sx;
        double bottom = Corner(cx, cy + 1) + (Corner(cx + 1, cy + 1) - Corner(cx, cy + 1)) * sx;
        return top + (bottom - top) * sy;
    }

    private static double SmoothStep(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    // ------------------------------------------------------------------------------------------
    // Wilderness hatching

    /// <summary>
    /// Diagonal hatching over every unsettled county, the way an old map marks ground nobody has
    /// surveyed. Hand-ruled rather than mechanical: each line wanders a little, the pen lifts now
    /// and then, and the strokes fade in over the last few pixels to the border instead of being
    /// cut off square. Ruins are left clear — they are ground someone once held.
    /// </summary>
    private static int Hatch(Canvas cv, ProvinceMap provinces, int[] order, WildernessMap wilderness, double k, int seed)
    {
        var wildProvince = new bool[provinces.Count + 2];
        int counties = 0;
        foreach (var county in wilderness.Unsettled)
        {
            counties++;
            foreach (var barony in county.Children)
                if (barony.ProvinceId >= 1 && barony.ProvinceId < wildProvince.Length)
                    wildProvince[barony.ProvinceId] = true;
        }
        if (counties == 0) return 0;

        int w = cv.W, h = cv.H;
        double fade = 6.0 * k;
        int cap = Math.Min(250, (int)Math.Ceiling(fade) + 2);

        var mask = new bool[w * h];
        for (int i = 0; i < mask.Length; i++)
        {
            int label = provinces.Label[i];
            if (label < 0 || label >= order.Length) continue;
            int id = order[label];
            if (id >= 1 && id < wildProvince.Length && wildProvince[id]) mask[i] = true;
        }
        var inside = InsideDistance(mask, w, h, cap);

        double spacing = 7.0 * k, r = 0.5 * k, dash = 30.0 * k;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                byte d = inside[i];
                if (d == 0) continue;

                // "/" strokes: lines of constant x + y, walked along x - y.
                double along = (x - y) * 0.70710678;
                double across = (x + y) * 0.70710678;
                across += 0.8 * k * Math.Sin(along / (19.0 * k) + 2.0 * Math.Sin(across / (57.0 * k)));

                double q = across / spacing;
                double m = Math.Round(q);
                double cov = r + 0.5 - Math.Abs(q - m) * spacing;
                if (cov <= 0) continue;

                long line = (long)m;
                double phase = Hash01(line, seed) * dash;
                long stroke = (long)Math.Floor((along + phase) / dash);
                if (Hash01(line * 7919 + seed, stroke) < 0.16) continue;

                double edge = d == Far ? 1.0 : Math.Min(1.0, (d - 0.5) / fade);
                cov = Math.Min(1.0, cov) * edge * (0.72 + 0.28 * Hash01(line * 31 + 5, stroke));
                cv.Set(i, cov);
            }
        });
        cv.TouchAll();
        cv.Ink(Sepia, 0.5);
        return counties;
    }

    /// <summary>Marks a pixel deep inside a mask in <see cref="InsideDistance"/>.</summary>
    private const byte Far = 255;

    /// <summary>
    /// Distance inside <paramref name="mask"/>, counted in from its edge (1 on the edge pixel) and
    /// stopped short of <paramref name="cap"/>: anything deeper stays <see cref="Far"/>, anything
    /// outside is 0.
    /// </summary>
    private static byte[] InsideDistance(bool[] mask, int w, int h, int cap)
    {
        var inside = new byte[w * h];
        for (int i = 0; i < inside.Length; i++)
            if (mask[i]) inside[i] = Far;

        var queue = new Queue<int>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (inside[i] == 0) continue;
                if ((x > 0 && inside[i - 1] == 0) || (x < w - 1 && inside[i + 1] == 0) ||
                    (y > 0 && inside[i - w] == 0) || (y < h - 1 && inside[i + w] == 0))
                {
                    inside[i] = 1;
                    queue.Enqueue(i);
                }
            }
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int next = inside[i] + 1;
            if (next >= cap) continue;
            int x = i % w, y = i / w;
            if (x > 0 && inside[i - 1] == Far) { inside[i - 1] = (byte)next; queue.Enqueue(i - 1); }
            if (x < w - 1 && inside[i + 1] == Far) { inside[i + 1] = (byte)next; queue.Enqueue(i + 1); }
            if (y > 0 && inside[i - w] == Far) { inside[i - w] = (byte)next; queue.Enqueue(i - w); }
            if (y < h - 1 && inside[i + w] == Far) { inside[i + w] = (byte)next; queue.Enqueue(i + w); }
        }

        return inside;
    }

    // ------------------------------------------------------------------------------------------
    // Mountains

    /// <summary>
    /// Hachures over the impassable mountains: short strokes that run straight down the slope,
    /// the way an engraver drew relief before contour lines. Each stroke stays inside one band
    /// of height, so the strokes stand in rows along the contours; they are laid as evenly spaced
    /// streamlines (a stroke stops where it would crowd another), heavier where the slope is
    /// steep and on the flanks turned away from a north-west light, tapering from the crest end
    /// down. Level tops are left bare, and the strokes fade in over the last few pixels to the
    /// wall's edge like the wilderness hatching does. Nothing crosses a pass: its corridor is
    /// passable land, so it reads as a gap in the shading.
    /// </summary>
    private static (int Strokes, int Pixels) Hachure(Canvas cv, ProvinceMap provinces, float[] elevation, double k, int seed)
    {
        int w = cv.W, h = cv.H;
        var mask = new bool[w * h];
        int pixels = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            int label = provinces.Label[i];
            if (label >= 0 && label < provinces.Seeds.Count && provinces.Seeds[label] is { IsLand: true, IsImpassable: true })
            {
                mask[i] = true;
                pixels++;
            }
        }
        if (pixels == 0) return (0, 0);

        // The pen follows a softened copy of the heights at a coarser grid: on the raw heightmap
        // its small-scale noise turns every stroke a different way and the walls read as fur.
        int f = Math.Max(1, (int)Math.Round(2 * k));
        int sw = (w + f - 1) / f, sh = (h + f - 1) / f;
        var soft = new float[sw * sh];
        Parallel.For(0, sh, sy =>
        {
            for (int sx = 0; sx < sw; sx++)
            {
                double sum = 0;
                int n = 0;
                for (int y = sy * f; y < Math.Min(h, sy * f + f); y++)
                    for (int x = sx * f; x < Math.Min(w, sx * f + f); x++) { sum += elevation[y * w + x]; n++; }
                soft[sy * sw + sx] = (float)(sum / n);
            }
        });
        soft = BoxBlur(BoxBlur(soft, sw, sh, 3), sw, sh, 3);

        float Elev(double fx, double fy)
        {
            fx = Math.Clamp(fx / f - 0.5, 0, sw - 1.001);
            fy = Math.Clamp(fy / f - 0.5, 0, sh - 1.001);
            int x = (int)fx, y = (int)fy;
            float tx = (float)(fx - x), ty = (float)(fy - y);
            int i = y * sw + x;
            float top = soft[i] + (soft[i + 1] - soft[i]) * tx;
            float bottom = soft[i + sw] + (soft[i + sw + 1] - soft[i + sw]) * tx;
            return top + (bottom - top) * ty;
        }

        double reach = Math.Max(1.0, f);
        Pt Grad(Pt p) => (
            (Elev(p.X + reach, p.Y) - Elev(p.X - reach, p.Y)) / (2 * reach),
            (Elev(p.X, p.Y + reach) - Elev(p.X, p.Y - reach)) / (2 * reach));
        bool In(Pt p)
        {
            int x = (int)p.X, y = (int)p.Y;
            return x >= 0 && y >= 0 && x < w && y < h && mask[y * w + x];
        }

        // The slopes this map's walls actually have, so the bands and weights suit any relief scale.
        var slopes = new List<double>();
        int sample = Math.Max(1, (int)Math.Sqrt(pixels / 20000.0));
        for (int y = 0; y < h; y += sample)
            for (int x = 0; x < w; x += sample)
                if (mask[y * w + x])
                {
                    var g = Grad((x + 0.5, y + 0.5));
                    slopes.Add(Math.Sqrt(g.X * g.X + g.Y * g.Y));
                }
        slopes.Sort();
        double median = slopes[slopes.Count / 2], steep = slopes[(int)(slopes.Count * 0.9)];
        if (!(median > 0)) return (0, pixels);

        double bandPx = 11.0 * k;                    // a stroke's length on ground of median slope
        double band = median * bandPx;               // the height each row of strokes spans
        double flat = 0.3 * median;                  // gentler than this is a level top, left bare
        double spacing = 3.4 * k;                    // between neighbouring strokes
        double maxLength = 2.2 * bandPx, minLength = 0.4 * bandPx;
        const double Step = 0.6;
        Pt sun = (-0.70710678, -0.70710678);         // light from the north-west, as the hillshade

        // Evenly spaced streamlines: every accepted stroke's points go into a grid, and a new
        // stroke may not start within `spacing` of one nor run within half of it.
        double cell = spacing;
        int gw = (int)Math.Ceiling(w / cell) + 1, gh = (int)Math.Ceiling(h / cell) + 1;
        var grid = new List<Pt>?[gw * gh];
        bool Crowded(Pt p, double within)
        {
            int cx = (int)(p.X / cell), cy = (int)(p.Y / cell);
            double w2 = within * within;
            for (int y = Math.Max(0, cy - 1); y <= Math.Min(gh - 1, cy + 1); y++)
                for (int x = Math.Max(0, cx - 1); x <= Math.Min(gw - 1, cx + 1); x++)
                    if (grid[y * gw + x] is { } pts)
                        foreach (var q in pts)
                            if ((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y) < w2) return true;
            return false;
        }
        void Claim(List<Pt> stroke)
        {
            for (int i = 0; i < stroke.Count; i += 2)
            {
                var p = stroke[i];
                int c = (int)(p.Y / cell) * gw + (int)(p.X / cell);
                (grid[c] ??= new List<Pt>()).Add(p);
            }
        }

        // One way along the fall line from `start`: +1 climbs, -1 descends. Stops at the band's
        // edge, a level top, the wall's edge, a neighbouring stroke, or a turn back on itself.
        List<Pt> Trace(Pt start, double sign, long row, double budget)
        {
            var pts = new List<Pt>();
            Pt p = start, last = (0, 0);
            for (double run = 0; run < budget; run += Step)
            {
                var g = Grad(p);
                double mag = Math.Sqrt(g.X * g.X + g.Y * g.Y);
                if (mag < flat) break;
                Pt d = (sign * g.X / mag, sign * g.Y / mag);
                if (pts.Count > 0 && d.X * last.X + d.Y * last.Y < 0.6) break;
                Pt next = (p.X + d.X * Step, p.Y + d.Y * Step);
                if (!In(next) || (long)Math.Floor(Elev(next.X, next.Y) / band) != row) break;
                if (Crowded(next, spacing * 0.5)) break;
                pts.Add(next);
                p = next;
                last = d;
            }
            return pts;
        }

        // Seeds on the contours first, so the rows start on a common line, then anywhere left.
        var seeds = new List<(double Key, int I)>();
        for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                int i = y * w + x;
                if (!mask[i]) continue;
                long b0 = (long)Math.Floor(Elev(x + 0.5, y + 0.5) / band);
                bool contour = (long)Math.Floor(Elev(x + 1.5, y + 0.5) / band) != b0 || (long)Math.Floor(Elev(x + 0.5, y + 1.5) / band) != b0;
                if (contour) seeds.Add((Hash01(i, seed), i));
                else if (x % 3 == 0 && y % 3 == 0) seeds.Add((1 + Hash01(i, seed), i));
            }
        seeds.Sort((a, b) => a.Key.CompareTo(b.Key));

        int strokes = 0;
        foreach (var (_, i) in seeds)
        {
            Pt start = (i % w + 0.5, i / w + 0.5);
            if (Crowded(start, spacing)) continue;
            var g0 = Grad(start);
            if (Math.Sqrt(g0.X * g0.X + g0.Y * g0.Y) < flat) continue;

            long row = (long)Math.Floor(Elev(start.X, start.Y) / band);
            var up = Trace(start, +1, row, maxLength);
            double used = up.Count * Step;
            var down = Trace(start, -1, row, maxLength - used);

            // Crest end first.
            var stroke = new List<Pt>(up.Count + down.Count + 1);
            for (int j = up.Count - 1; j >= 0; j--) stroke.Add(up[j]);
            stroke.Add(start);
            stroke.AddRange(down);
            if ((stroke.Count - 1) * Step < minLength) continue;

            Claim(stroke);
            strokes++;

            // Weight from the stroke's middle: steeper is heavier, lit flanks lighter.
            var mid = stroke[stroke.Count / 2];
            var g = Grad(mid);
            double mag = Math.Sqrt(g.X * g.X + g.Y * g.Y);
            double steepness = Math.Clamp((mag - flat) / (steep - flat), 0, 1);
            double lit = (-g.X * sun.X - g.Y * sun.Y) / mag;            // downhill · toward the sun
            double r = (0.3 + 0.45 * steepness) * k * (1 - 0.28 * lit);
            double jitter = 0.85 + 0.3 * Hash01(i * 13 + 7, seed);
            r *= jitter;

            for (int j = 1; j < stroke.Count; j += 2)
            {
                int to = Math.Min(stroke.Count - 1, j + 1);
                double t0 = (j - 1) / (double)(stroke.Count - 1), t1 = to / (double)(stroke.Count - 1);
                cv.Capsule(stroke[j - 1], stroke[to], r * (1 - 0.55 * t0), r * (1 - 0.55 * t1));
            }
        }

        // Fade in from the wall's edge, and nothing spills past it.
        double fade = 5.0 * k;
        var inside = InsideDistance(mask, w, h, Math.Min(250, (int)Math.Ceiling(fade) + 2));
        cv.Scale(i => inside[i] == 0 ? 0 : inside[i] == Far ? 1 : Math.Min(1.0, (inside[i] - 0.5) / fade));
        cv.Ink(Sepia, 0.62);
        return (strokes, pixels);
    }

    /// <summary>
    /// A pass mark on each pass cut through a wall: a pair of arcs, ")(", facing each other across
    /// the corridor and bowing in to pinch it, drawn along the line of the crossing. Each arc
    /// stands just outside the corridor on its side, where the hachures of the wall begin.
    /// </summary>
    private static int MarkPasses(Canvas cv, ProvinceMap provinces, double k)
    {
        if (provinces.AutoCut?.Passes is not { Count: > 0 } passes) return 0;
        int w = cv.W, h = cv.H;

        bool Wall(Pt p)
        {
            int x = (int)p.X, y = (int)p.Y;
            if (x < 0 || y < 0 || x >= w || y >= h) return false;
            int label = provinces.Label[y * w + x];
            return label >= 0 && label < provinces.Seeds.Count && provinces.Seeds[label] is { IsLand: true, IsImpassable: true };
        }

        int marked = 0;
        foreach (var pass in passes)
        {
            Pt m = (pass.Middle.X + 0.5, pass.Middle.Y + 0.5);
            if (Wall(m)) continue;
            double dx = pass.SideB.X - pass.SideA.X, dy = pass.SideB.Y - pass.SideA.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) continue;
            Pt along = (dx / len, dy / len), perp = (-along.Y, along.X);

            // How far out each side the wall starts, looking square across the corridor.
            double limit = 14 * k;
            double Gap(double side)
            {
                for (double s = 1; s < limit; s += 0.5)
                    if (Wall((m.X + perp.X * side * s, m.Y + perp.Y * side * s))) return s;
                return limit;
            }

            marked++;
            foreach (double side in new[] { -1.0, 1.0 })
            {
                double gap = Math.Clamp(Gap(side) + 0.8 * k, 2.2 * k, limit);
                double half = Math.Max(8.0 * k, gap * 1.4), sag = 0.35 * half;
                var arc = new List<Pt>();
                for (int s = -12; s <= 12; s++)
                {
                    double t = s / 12.0;
                    double off = side * (gap + sag * t * t);
                    arc.Add((m.X + along.X * t * half + perp.X * off, m.Y + along.Y * t * half + perp.Y * off));
                }
                for (int s = 1; s < arc.Count; s++)
                {
                    double t0 = Math.Abs((s - 1) / 12.0 - 1), t1 = Math.Abs(s / 12.0 - 1);
                    cv.Capsule(arc[s - 1], arc[s], k * (1.25 - 0.85 * t0), k * (1.25 - 0.85 * t1));
                }
            }
        }
        cv.Ink(Sepia, 0.9);
        return marked;
    }

    // ------------------------------------------------------------------------------------------
    // Where the rose goes

    /// <summary>
    /// Finds room in the open ocean for the rose, as close to a corner of the frame as it will
    /// go — tucked into the corner when the corner is sea, and otherwise at the free spot nearest
    /// any corner, never over land or the coast's wave marks. A distance-to-land field on a
    /// coarse grid says where it fits. A map with no ocean wide enough gets a smaller rose, then
    /// none.
    /// </summary>
    private static (Pt? Centre, double Radius) PlaceRose(bool[] land, int w, int h, double k, double inset)
    {
        int f = Math.Max(2, w / 1024);
        int gw = (w + f - 1) / f, gh = (h + f - 1) / f;
        const int Inf = int.MaxValue / 4;
        var d = new int[gw * gh];

        Parallel.For(0, gh, gy =>
        {
            for (int gx = 0; gx < gw; gx++)
            {
                bool any = false;
                for (int y = gy * f; y < Math.Min(h, gy * f + f) && !any; y++)
                    for (int x = gx * f; x < Math.Min(w, gx * f + f); x++)
                        if (land[y * w + x]) { any = true; break; }
                d[gy * gw + gx] = any ? 0 : Inf;
            }
        });

        // Chamfer 3-4, forward then back.
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                int i = y * gw + x, v = d[i];
                if (v == 0) continue;
                if (x > 0) v = Math.Min(v, d[i - 1] + 3);
                if (y > 0)
                {
                    v = Math.Min(v, d[i - gw] + 3);
                    if (x > 0) v = Math.Min(v, d[i - gw - 1] + 4);
                    if (x < gw - 1) v = Math.Min(v, d[i - gw + 1] + 4);
                }
                d[i] = v;
            }
        for (int y = gh - 1; y >= 0; y--)
            for (int x = gw - 1; x >= 0; x--)
            {
                int i = y * gw + x, v = d[i];
                if (v == 0) continue;
                if (x < gw - 1) v = Math.Min(v, d[i + 1] + 3);
                if (y < gh - 1)
                {
                    v = Math.Min(v, d[i + gw] + 3);
                    if (x < gw - 1) v = Math.Min(v, d[i + gw + 1] + 4);
                    if (x > 0) v = Math.Min(v, d[i + gw - 1] + 4);
                }
                d[i] = v;
            }

        double DistPx(int i) => d[i] >= Inf ? double.MaxValue : d[i] / 3.0 * f;
        Pt CentreOf(int i) => ((i % gw + 0.5) * f, (i / gw + 0.5) * f);

        // Clear of the border and a little breathing room inside it.
        double frame = inset + (BorderBand + 10) * k;
        bool Fits(Pt c, double need) =>
            c.X >= need + frame && c.Y >= need + frame && c.X <= w - need - frame && c.Y <= h - need - frame;

        // Its footprint includes the N above the ring; the coast pad keeps it off the wave marks
        // FlatmapWriter draws in the first 24 pixels of sea.
        foreach (double scale in new[] { 1.0, 0.8, 0.64 })
        {
            double R = 0.052 * h * scale, footprint = 1.36 * R, coastPad = 26 * k;
            double reach = footprint + frame;
            Pt[] corners = [(reach, reach), (w - reach, reach), (reach, h - reach), (w - reach, h - reach)];
            double best = double.MaxValue;
            Pt? rose = null;
            for (int i = 0; i < d.Length; i++)
            {
                if (DistPx(i) < footprint + coastPad) continue;
                var c = CentreOf(i);
                if (!Fits(c, footprint)) continue;
                double nearest = corners.Min(corner => Dist(c, corner));
                if (nearest < best) { best = nearest; rose = c; }
            }
            if (rose is not null) return (rose, R);
        }
        return (null, 0);
    }

    // ------------------------------------------------------------------------------------------
    // The compass rose

    /// <summary>
    /// A sixteen-point rose on a paper medallion: double outer ring with degree ticks, eight
    /// minor points under eight intercardinal and cardinal ones, each point half inked and half
    /// open, north's inked half in vermilion and an N above it.
    /// </summary>
    private static void DrawRose(Canvas cv, Pt c, double R)
    {
        double lw = Math.Max(0.6, R * 0.0095);
        Pt Dir(double a) => (Math.Cos(a), Math.Sin(a));
        Pt At(double a, double radius) => (c.X + Math.Cos(a) * radius, c.Y + Math.Sin(a) * radius);

        cv.Disc(c, R + lw);
        cv.Lift(0.3);

        cv.Ring(c, R, lw * 1.5);
        cv.Ring(c, R * 0.9, lw * 0.8);
        for (int t = 0; t < 64; t++)
        {
            double a = t * Math.PI * 2 / 64;
            double inner = t % 8 == 0 ? 0.9 : t % 2 == 0 ? 0.935 : 0.955;
            cv.Capsule(At(a, R * inner), At(a, R), lw * 0.55, lw * 0.55);
        }
        cv.Ring(c, R * 0.34, lw * 0.7);

        // Back to front, each point erasing what is under it so its open half reads as paper.
        (int[] Points, double Length, double Half)[] tiers =
        [
            (new[] { 1, 3, 5, 7, 9, 11, 13, 15 }, 0.56, 0.055),
            (new[] { 2, 6, 10, 14 }, 0.76, 0.095),
            (new[] { 0, 4, 8, 12 }, 1.0, 0.13),
        ];
        Pt[]? northHalf = null;
        foreach (var (points, length, half) in tiers)
        {
            foreach (int p in points)
            {
                double a = p * Math.PI / 8 - Math.PI / 2;
                var dir = Dir(a);
                Pt perp = (-dir.Y, dir.X);
                Pt tip = (c.X + dir.X * length * R, c.Y + dir.Y * length * R);
                Pt left = (c.X + perp.X * half * R, c.Y + perp.Y * half * R);
                Pt right = (c.X - perp.X * half * R, c.Y - perp.Y * half * R);

                cv.Erase([tip, left, right]);
                if (p == 0) northHalf = [c, tip, left];
                else cv.Fill([c, tip, left]);
                cv.Capsule(tip, left, lw * 0.7, lw * 0.7);
                cv.Capsule(tip, right, lw * 0.7, lw * 0.7);
                cv.Capsule(c, tip, lw * 0.45, lw * 0.45);
                cv.Capsule(left, right, lw * 0.5, lw * 0.5);
            }
        }

        cv.Disc(c, R * 0.05);
        cv.Ring(c, R * 0.085, lw * 0.5);

        // The N: thin stems, heavy diagonal, small serifs.
        double nx = R * 0.07, top = c.Y - R * 1.31, bottom = c.Y - R * 1.07;
        Pt tl = (c.X - nx, top), bl = (c.X - nx, bottom), tr = (c.X + nx, top), br = (c.X + nx, bottom);
        cv.Capsule(tl, bl, lw * 0.6, lw * 0.6);
        cv.Capsule(tr, br, lw * 0.6, lw * 0.6);
        cv.Capsule(tl, br, lw * 1.5, lw * 1.5);
        double serif = R * 0.035;
        cv.Capsule((tl.X - serif, top), (tl.X + serif * 0.6, top), lw * 0.5, lw * 0.5);
        cv.Capsule((bl.X - serif, bottom), (bl.X + serif, bottom), lw * 0.5, lw * 0.5);
        cv.Capsule((tr.X - serif, top), (tr.X + serif, top), lw * 0.5, lw * 0.5);
        cv.Ink(Sepia, 0.92);

        if (northHalf is not null)
        {
            cv.Fill(northHalf);
            cv.Ink(Vermilion, 0.9);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Rhumb lines

    /// <summary>
    /// Water bodies big enough to be sea: every connected stretch of water holding at least a
    /// fiftieth of the map. Lakes and landlocked inlets fall below it, so the rhumb lines, which
    /// belong to the navigator's sea, never cross them. Nothing within <paramref name="inset"/>
    /// of the edge counts either: that is the border's, and the lines stop at its inner rule.
    /// </summary>
    private static bool[] OpenWater(bool[] land, int w, int h, double inset)
    {
        var label = new int[w * h];
        var open = new bool[w * h];
        var queue = new Queue<int>();
        var members = new List<int>();
        long minArea = (long)w * h / 50;
        int next = 0;

        for (int start = 0; start < label.Length; start++)
        {
            if (land[start] || label[start] != 0) continue;
            label[start] = ++next;
            queue.Enqueue(start);
            members.Clear();
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                members.Add(i);
                int x = i % w, y = i / w;
                if (x > 0 && !land[i - 1] && label[i - 1] == 0) { label[i - 1] = next; queue.Enqueue(i - 1); }
                if (x < w - 1 && !land[i + 1] && label[i + 1] == 0) { label[i + 1] = next; queue.Enqueue(i + 1); }
                if (y > 0 && !land[i - w] && label[i - w] == 0) { label[i - w] = next; queue.Enqueue(i - w); }
                if (y < h - 1 && !land[i + w] && label[i + w] == 0) { label[i + w] = next; queue.Enqueue(i + w); }
            }
            if (members.Count >= minArea)
                foreach (int i in members) open[i] = true;
        }

        int edge = (int)Math.Ceiling(inset);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x < edge || y < edge || x >= w - edge || y >= h - edge) open[y * w + x] = false;
        return open;
    }

    /// <summary>
    /// The thirty-two winds, ruled from the rose's rim to the edge of the sheet the way a
    /// portolan chart's are: the eight principal winds heaviest, the half-winds lighter, the
    /// quarter-winds a hairline. Inked on open sea only — the land and the lakes interrupt them.
    /// </summary>
    private static void DrawRhumbs(Canvas cv, bool[] open, Pt c, double R, double k)
    {
        double reach = Math.Sqrt((double)cv.W * cv.W + (double)cv.H * cv.H);
        (int Step, int Offset, double Radius, double Opacity)[] tiers =
        [
            (2, 1, 0.38 * k, 0.2),   // quarter-winds: the odd points
            (4, 2, 0.45 * k, 0.28),  // half-winds
            (4, 0, 0.55 * k, 0.36),  // principal winds
        ];
        foreach (var (step, offset, radius, opacity) in tiers)
        {
            for (int p = offset; p < 32; p += step)
            {
                double a = p * Math.PI / 16 - Math.PI / 2;
                Pt from = (c.X + Math.Cos(a) * R * 1.02, c.Y + Math.Sin(a) * R * 1.02);
                Pt to = (c.X + Math.Cos(a) * reach, c.Y + Math.Sin(a) * reach);
                cv.Line(from, to, radius);
            }
            cv.KeepOnly(open);
            cv.Ink(Sepia, opacity);
        }
    }

    // ------------------------------------------------------------------------------------------
    // The border

    /// <summary>
    /// At k = 1: the frame's outer rule's distance from the edge without and with the edge
    /// feather, and the graduated band's width.
    /// </summary>
    private const double BorderOuter = 5, FeatherMargin = 40, BorderBand = 12;

    /// <summary>
    /// A printed map's frame: a paper margin, a heavy outer rule and a fine inner one, and between
    /// them a graduated band — alternate blocks inked along its outer half, each block ten degrees
    /// of a whole-world sheet (36 along the top, 18 down the side on a 2:1 map), with ticks at every
    /// two degrees. The blocks are sized off the height so they stay square on any shape of map.
    /// </summary>
    private static void DrawBorder(Canvas cv, int h, double k, double outer)
    {
        int w = cv.W;
        double band = BorderBand * k, inner = outer + band, mid = outer + band * 0.5;
        Pt[] Rect(double x0, double y0, double x1, double y1) => [(x0, y0), (x1, y0), (x1, y1), (x0, y1)];

        // Paper under the margin and the band.
        cv.Fill(Rect(0, 0, w, inner));
        cv.Fill(Rect(0, h - inner, w, h));
        cv.Fill(Rect(0, 0, inner, h));
        cv.Fill(Rect(w - inner, 0, w, h));
        cv.Lift(0.3);

        void Frame(double inset, double radius)
        {
            Pt tl = (inset, inset), tr = (w - inset, inset), br = (w - inset, h - inset), bl = (inset, h - inset);
            cv.Line(tl, tr, radius);
            cv.Line(tr, br, radius);
            cv.Line(br, bl, radius);
            cv.Line(bl, tl, radius);
        }
        Frame(outer, 1.2 * k);
        Frame(inner, 0.55 * k);
        Frame(mid, 0.35 * k);
        Frame(inner + 3 * k, 0.35 * k);

        // Graduation: blocks along each side, alternate ones inked in the outer half of the band.
        void Graduate(Pt start, Pt end, Pt inward)
        {
            double length = Dist(start, end);
            int blocks = Math.Max(2, (int)Math.Round(length / (h / 18.0)));
            double step = length / blocks;
            Pt along = ((end.X - start.X) / length, (end.Y - start.Y) / length);
            Pt At(double s, double depth) => (start.X + along.X * s + inward.X * depth, start.Y + along.Y * s + inward.Y * depth);

            for (int b = 0; b < blocks; b++)
            {
                if (b % 2 == 0)
                    cv.Fill([At(b * step, outer), At((b + 1) * step, outer), At((b + 1) * step, mid), At(b * step, mid)]);
                for (int t = 1; t < 5; t++)
                    cv.Line(At((b + t / 5.0) * step, mid), At((b + t / 5.0) * step, mid + band * 0.3), 0.35 * k);
            }
        }
        Graduate((inner, 0), (w - inner, 0), (0, 1));
        Graduate((inner, h), (w - inner, h), (0, -1));
        Graduate((0, inner), (0, h - inner), (1, 0));
        Graduate((w, inner), (w, h - inner), (-1, 0));

        // Corner squares where the bands meet.
        foreach (var (x, y) in new[] { (outer, outer), (w - inner, outer), (outer, h - inner), (w - inner, h - inner) })
        {
            cv.Line((x, y), (x + band, y + band), 0.35 * k);
            cv.Line((x + band, y), (x, y + band), 0.35 * k);
        }

        cv.Ink(Sepia, 0.9);
    }

    // ------------------------------------------------------------------------------------------
    // Geometry

    private static double Dist(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Pt Lerp(Pt a, Pt b, double t) => (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>Corner-cutting, with the ends held where they are.</summary>
    private static List<Pt> Chaikin(List<Pt> pts, int iterations)
    {
        for (int it = 0; it < iterations && pts.Count >= 3; it++)
        {
            var next = new List<Pt>(pts.Count * 2) { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (i > 0) next.Add(Lerp(pts[i], pts[i + 1], 0.25));
                if (i < pts.Count - 2) next.Add(Lerp(pts[i], pts[i + 1], 0.75));
            }
            next.Add(pts[^1]);
            pts = next;
        }
        return pts;
    }

    private static List<Pt> Resample(List<Pt> pts, double step)
    {
        var result = new List<Pt> { pts[0] };
        double carry = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            var (a, b) = (pts[i - 1], pts[i]);
            double seg = Dist(a, b);
            if (seg == 0) continue;
            double s = step - carry;
            while (s <= seg)
            {
                result.Add(Lerp(a, b, s / seg));
                s += step;
            }
            carry = seg - (s - step);
        }
        if (result[^1] != pts[^1]) result.Add(pts[^1]);
        return result;
    }

    /// <summary>A separable box blur of radius <paramref name="r"/>, edges clamped.</summary>
    private static float[] BoxBlur(float[] src, int w, int h, int r)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        float norm = 1f / (2 * r + 1);
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                for (int d = -r; d <= r; d++) s += src[y * w + Math.Clamp(x + d, 0, w - 1)];
                tmp[y * w + x] = s * norm;
            }
        });
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                for (int d = -r; d <= r; d++) s += tmp[Math.Clamp(y + d, 0, h - 1) * w + x];
                dst[y * w + x] = s * norm;
            }
        });
        return dst;
    }

    private static double Hash01(long a, long b)
    {
        ulong z = unchecked((ulong)a * 0x9E3779B97F4A7C15UL + (ulong)b * 0xC2B2AE3D27D4EB4FUL + 0x165667B19E3779F9UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }

    // ------------------------------------------------------------------------------------------
    // The pen

    /// <summary>
    /// One layer of ink over the picture. Shapes accumulate into a coverage buffer by maximum;
    /// <see cref="Ink"/> or <see cref="Lift"/> then applies the layer to the pixels and clears it.
    /// Coverage is analytic for strokes and rings and 4x4 supersampled for polygons.
    /// </summary>
    private sealed class Canvas(byte[] bgra, int w, int h)
    {
        public readonly int W = w, H = h;
        private readonly byte[] cover = new byte[w * h];
        private int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;

        private void Touch(int ax, int ay, int bx, int by)
        {
            x0 = Math.Min(x0, ax); y0 = Math.Min(y0, ay);
            x1 = Math.Max(x1, bx); y1 = Math.Max(y1, by);
        }

        public void TouchAll() => Touch(0, 0, W - 1, H - 1);

        /// <summary>Raw write for a caller that owns the pixel — safe from a parallel row loop.</summary>
        public void Set(int i, double c)
        {
            byte v = (byte)Math.Min(255, c * 255 + 0.5);
            if (v > cover[i]) cover[i] = v;
        }

        private bool Box(double minX, double minY, double maxX, double maxY,
            out int bx0, out int by0, out int bx1, out int by1)
        {
            bx0 = Math.Max(0, (int)Math.Floor(minX)); by0 = Math.Max(0, (int)Math.Floor(minY));
            bx1 = Math.Min(W - 1, (int)Math.Ceiling(maxX)); by1 = Math.Min(H - 1, (int)Math.Ceiling(maxY));
            return bx0 <= bx1 && by0 <= by1;
        }

        private static double CapsuleCover(double px, double py, Pt a, double dx, double dy, double len2, double ra, double rb)
        {
            double qx = px - a.X, qy = py - a.Y;
            double t = len2 > 0 ? Math.Clamp((qx * dx + qy * dy) / len2, 0, 1) : 0;
            double ex = qx - t * dx, ey = qy - t * dy;
            double r = ra + (rb - ra) * t;
            return Math.Clamp(r + 0.5 - Math.Sqrt(ex * ex + ey * ey), 0, 1);
        }

        /// <summary>A stroke from a to b whose radius runs from ra to rb, round-ended.</summary>
        public void Capsule(Pt a, Pt b, double ra, double rb)
        {
            double r = Math.Max(ra, rb) + 1;
            if (!Box(Math.Min(a.X, b.X) - r, Math.Min(a.Y, b.Y) - r, Math.Max(a.X, b.X) + r, Math.Max(a.Y, b.Y) + r,
                    out int bx0, out int by0, out int bx1, out int by1)) return;
            Touch(bx0, by0, bx1, by1);
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    double c = CapsuleCover(x + 0.5, y + 0.5, a, dx, dy, len2, ra, rb);
                    if (c > 0) Set(y * W + x, c);
                }
        }

        public void Disc(Pt c, double r) => Capsule(c, c, r, r);

        /// <summary>
        /// A long straight stroke, laid as short capsules so each one's bounding box stays small —
        /// one capsule across the whole map would test every pixel on it.
        /// </summary>
        public void Line(Pt a, Pt b, double r)
        {
            int pieces = Math.Max(1, (int)Math.Ceiling(Dist(a, b) / 48));
            for (int i = 0; i < pieces; i++)
                Capsule(Lerp(a, b, i / (double)pieces), Lerp(a, b, (i + 1) / (double)pieces), r, r);
        }

        /// <summary>Multiplies the layer by <paramref name="factor"/> of each pixel, in 0..1.</summary>
        public void Scale(Func<int, double> factor)
        {
            if (x1 < 0) return;
            int ax = x0, bx = x1;
            Parallel.For(y0, y1 + 1, y =>
            {
                for (int x = ax; x <= bx; x++)
                {
                    int i = y * W + x;
                    if (cover[i] != 0) cover[i] = (byte)(cover[i] * factor(i) + 0.5);
                }
            });
        }

        /// <summary>Clears the layer wherever <paramref name="keep"/> is false.</summary>
        public void KeepOnly(bool[] keep)
        {
            if (x1 < 0) return;
            int ax = x0, bx = x1;
            Parallel.For(y0, y1 + 1, y =>
            {
                for (int x = ax; x <= bx; x++)
                {
                    int i = y * W + x;
                    if (!keep[i]) cover[i] = 0;
                }
            });
        }

        public void Stroke(IReadOnlyList<Pt> pts, double r)
        {
            if (pts.Count == 1) Disc(pts[0], r);
            for (int i = 1; i < pts.Count; i++) Capsule(pts[i - 1], pts[i], r, r);
        }

        public void Ring(Pt c, double radius, double half)
        {
            double r = radius + half + 1;
            if (!Box(c.X - r, c.Y - r, c.X + r, c.Y + r, out int bx0, out int by0, out int bx1, out int by1)) return;
            Touch(bx0, by0, bx1, by1);
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    double dx = x + 0.5 - c.X, dy = y + 0.5 - c.Y;
                    double cov = Math.Clamp(half + 0.5 - Math.Abs(Math.Sqrt(dx * dx + dy * dy) - radius), 0, 1);
                    if (cov > 0) Set(y * W + x, cov);
                }
        }

        private double PolygonCover(IReadOnlyList<Pt> poly, int x, int y)
        {
            int hits = 0;
            for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                    if (Inside(poly, x + (sx + 0.5) / 4, y + (sy + 0.5) / 4)) hits++;
            return hits / 16.0;
        }

        public void Fill(IReadOnlyList<Pt> poly)
        {
            if (!Box(poly.Min(p => p.X) - 1, poly.Min(p => p.Y) - 1, poly.Max(p => p.X) + 1, poly.Max(p => p.Y) + 1,
                    out int bx0, out int by0, out int bx1, out int by1)) return;
            Touch(bx0, by0, bx1, by1);
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    double c = PolygonCover(poly, x, y);
                    if (c > 0) Set(y * W + x, c);
                }
        }

        /// <summary>
        /// Several rings filled as one shape, even-odd: a ring inside another is a hole. What a
        /// letter needs, whose counters are rings of their own; <see cref="Fill"/> would close them.
        /// </summary>
        public void FillEvenOdd(IReadOnlyList<IReadOnlyList<Pt>> rings)
        {
            var all = rings.Where(r => r.Count > 2).ToList();
            if (all.Count == 0) return;
            if (!Box(all.Min(r => r.Min(p => p.X)) - 1, all.Min(r => r.Min(p => p.Y)) - 1,
                    all.Max(r => r.Max(p => p.X)) + 1, all.Max(r => r.Max(p => p.Y)) + 1,
                    out int bx0, out int by0, out int bx1, out int by1)) return;
            Touch(bx0, by0, bx1, by1);
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            double px = x + (sx + 0.5) / 4, py = y + (sy + 0.5) / 4;
                            bool inside = false;
                            foreach (var ring in all)
                                if (Inside(ring, px, py)) inside = !inside;
                            if (inside) hits++;
                        }
                    if (hits > 0) Set(y * W + x, hits / 16.0);
                }
        }

        public void Erase(IReadOnlyList<Pt> poly)
        {
            if (!Box(poly.Min(p => p.X) - 1, poly.Min(p => p.Y) - 1, poly.Max(p => p.X) + 1, poly.Max(p => p.Y) + 1,
                    out int bx0, out int by0, out int bx1, out int by1)) return;
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    int i = y * W + x;
                    if (cover[i] == 0) continue;
                    double c = PolygonCover(poly, x, y);
                    if (c > 0) cover[i] = (byte)(cover[i] * (1 - c) + 0.5);
                }
        }

        private static bool Inside(IReadOnlyList<Pt> poly, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var (xi, yi) = poly[i];
                var (xj, yj) = poly[j];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        /// <summary>Multiplies the layer into the picture in <paramref name="ink"/>, then clears it.</summary>
        public void Ink((int R, int G, int B) ink, double opacity)
        {
            double kr = 1 - ink.R / 255.0, kg = 1 - ink.G / 255.0, kb = 1 - ink.B / 255.0;
            Apply((o, a) =>
            {
                a *= opacity;
                bgra[o] = (byte)(bgra[o] * (1 - a * kb) + 0.5);
                bgra[o + 1] = (byte)(bgra[o + 1] * (1 - a * kg) + 0.5);
                bgra[o + 2] = (byte)(bgra[o + 2] * (1 - a * kr) + 0.5);
            });
        }

        /// <summary>Brightens the picture under the layer toward bare paper, then clears it.</summary>
        public void Lift(double amount)
        {
            Apply((o, a) =>
            {
                double gain = 1 + a * amount;
                bgra[o] = (byte)Math.Min(255, bgra[o] * gain + 0.5);
                bgra[o + 1] = (byte)Math.Min(255, bgra[o + 1] * gain + 0.5);
                bgra[o + 2] = (byte)Math.Min(255, bgra[o + 2] * gain + 0.5);
            });
        }

        private void Apply(Action<int, double> blend)
        {
            if (x1 < 0) return;
            int ax = x0, bx = x1;
            Parallel.For(y0, y1 + 1, y =>
            {
                for (int x = ax; x <= bx; x++)
                {
                    int i = y * W + x;
                    byte v = cover[i];
                    if (v == 0) continue;
                    cover[i] = 0;
                    blend(i * 4, v / 255.0);
                }
            });
            x0 = y0 = int.MaxValue;
            x1 = y1 = -1;
        }
    }
}
